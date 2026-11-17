using EOS.Notification;
using EOS.Core;
using EOS.Friends;
using EOS.Auth;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;
using Epic.OnlineServices;

namespace EOS.Lobby
{
    [DisallowMultipleComponent]
    public class LobbyUIManager : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private GameObject inLobbyPanel;

        [Header("Menu View Controls")]
        [SerializeField] private Button createLobbyButton;
        [SerializeField] private TMP_InputField joinCodeInputField;
        [SerializeField] private Button joinLobbyButton;
        [SerializeField] private uint maxLobbyMembers = 4;
        [SerializeField] private bool allowJoinInProgress = false;
        [SerializeField] private bool useShortJoinCode = true;
        [SerializeField] private bool allowAutoRejoin = true;

        [Header("In-Lobby View Controls")]
        [SerializeField] private TextMeshProUGUI roomCodeText;
        [SerializeField] private Button copyCodeButton;
        [SerializeField] private TextMeshProUGUI memberCountText;
        [SerializeField] private Button leaveLobbyButton;
        [SerializeField] private Button startGameButton;

        [Header("Player Cards")]
        [SerializeField] private LobbyPlayerCardUI playerCardPrefab;
        [SerializeField] private Transform playerCardsParent;
        [SerializeField] private List<Sprite> playerCardImages = new();

        [Header("Navigation")]
        [SerializeField, HideInInspector] private string nextSceneName = "GameScene";

        private ILobby _lobby;
        private TextMeshProUGUI _actionButtonText;
        private readonly List<LobbyPlayerCardUI> _cardPool = new();
        private readonly Dictionary<string, LobbyPlayerCardUI> _activePlayerCards = new();

#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset nextScene;

        private void OnValidate()
        {
            nextSceneName = (nextScene != null) ? nextScene.name : "GameScene";

            if (UnityEditor.EditorUtility.IsPersistent(this))
            {
                return;
            }

            if (menuPanel == null) Debug.LogWarning("[LobbyUIManager] menuPanel is null! Please assign in inspector.", this);
            if (inLobbyPanel == null) Debug.LogWarning("[LobbyUIManager] inLobbyPanel is null! Please assign in inspector.", this);
            if (createLobbyButton == null) Debug.LogWarning("[LobbyUIManager] createLobbyButton is null! Please assign in inspector.", this);
            if (joinCodeInputField == null) Debug.LogWarning("[LobbyUIManager] joinCodeInputField is null! Please assign in inspector.", this);
            if (joinLobbyButton == null) Debug.LogWarning("[LobbyUIManager] joinLobbyButton is null! Please assign in inspector.", this);
            if (roomCodeText == null) Debug.LogWarning("[LobbyUIManager] roomCodeText is null! Please assign in inspector.", this);
            if (copyCodeButton == null) Debug.LogWarning("[LobbyUIManager] copyCodeButton is null! Please assign in inspector.", this);
            if (memberCountText == null) Debug.LogWarning("[LobbyUIManager] memberCountText is null! Please assign in inspector.", this);
            if (leaveLobbyButton == null) Debug.LogWarning("[LobbyUIManager] leaveLobbyButton is null! Please assign in inspector.", this);
            if (startGameButton == null) Debug.LogWarning("[LobbyUIManager] startGameButton is null! Please assign in inspector.", this);
            if (playerCardPrefab == null) Debug.LogWarning("[LobbyUIManager] playerCardPrefab is null! Please assign in inspector.", this);
            if (playerCardsParent == null) Debug.LogWarning("[LobbyUIManager] playerCardsParent is null! Please assign in inspector.", this);
            if (playerCardImages == null || playerCardImages.Count == 0) Debug.LogWarning("[LobbyUIManager] playerCardImages list is empty! Please assign at least one image.", this);
        }
#endif

        private void Awake()
        {
            _lobby = LobbyManager.Instance;
            Assert.IsNotNull(_lobby, "[LobbyUIManager] Failed to instantiate LobbyManager. Ensure EOS is initialized.");

            _actionButtonText = startGameButton.GetComponentInChildren<TextMeshProUGUI>();
            SetPanelState(isInLobby: _lobby.IsInLobby);
            SetMenuButtonsInteractable(false);

            createLobbyButton.onClick.AddListener(OnCreateLobbyButtonClicked);
            joinLobbyButton.onClick.AddListener(OnJoinLobbyButtonClicked);
            copyCodeButton.onClick.AddListener(OnCopyCodeButtonClicked);
            leaveLobbyButton.onClick.AddListener(OnLeaveLobbyButtonClicked);
            startGameButton.onClick.AddListener(OnActionButtonClicked);

            _lobby.OnLobbyCreated += HandleOnLobbyCreated;
            _lobby.OnLobbyJoined += HandleOnLobbyJoined;
            _lobby.OnLobbyLeft += HandleOnLobbyLeft;
            _lobby.OnPlayerJoined += HandleOnPlayerJoined;
            _lobby.OnPlayerLeft += HandleOnPlayerLeft;
            _lobby.OnLobbyError += HandleOnLobbyError;
            _lobby.OnGameStarted += HandleOnGameStarted;
            _lobby.OnReadyStatusChanged += HandleOnReadyStatusChanged;
            _lobby.OnLobbyUpdated += HandleOnLobbyUpdated;
            _lobby.OnAutoRejoinStateChanged += HandleAutoRejoinStateChanged;
        }

        private void Start()
        {
            FriendsManager.OnInitialized -= HandleFriendsSystemInitialized;
            FriendsManager.OnInitialized += HandleFriendsSystemInitialized;

            if (IsFriendsSystemAvailable())
            {
                SubscribeFriendsEvents();
            }

            NetworkService.Current.CustomAddressDisplayNameResolver = ResolveLobbyDisplayName;
            NetworkService.Current.OnUnexpectedClientDisconnect -= HandleUnexpectedClientDisconnect;
            NetworkService.Current.OnUnexpectedClientDisconnect += HandleUnexpectedClientDisconnect;

            _lobby.AllowJoinInProgress = allowJoinInProgress;
            _lobby.UseShortJoinCode = useShortJoinCode;
            _lobby.AllowAutoRejoin = allowAutoRejoin;

            if (_lobby.IsAutoRejoining)
            {
                SetPanelState(isInLobby: false);
                SetMenuButtonsInteractable(false);
            }
            else if (_lobby.IsInLobby)
            {
                if (_lobby.HasGameStarted && !_lobby.IsLobbyOwner)
                {
                    SetPanelState(isInLobby: false);
                    SetMenuButtonsInteractable(false);
                    _lobby.HandleNetworkDisconnect();
                }
                else
                {
                    RestoreInLobbyUI();
                }
            }
            else
            {
                joinCodeInputField.text = string.Empty;
                SetPanelState(isInLobby: false);
                SetMenuButtonsInteractable(false);
                _lobby.CheckAndCleanOrphanLobbyOnLogin(HandleOrphanCleanupComplete);
            }
        }

        private void HandleOrphanCleanupComplete()
        {
            SetMenuButtonsInteractable(!_lobby.IsAutoRejoining && !_lobby.IsInLobby);
        }

        private void RestoreInLobbyUI()
        {
            roomCodeText.text = _lobby.CurrentJoinCode;
            UpdateMemberCountDisplay();
            _lobby.ResetLocalReadyStatus();

            UpdateActionButtonsDisplay();
            SetPanelState(isInLobby: true);
            SetLobbyButtonsInteractable(true);

            SyncPlayerCards();
        }

        private void OnDestroy()
        {
            FriendsManager.OnInitialized -= HandleFriendsSystemInitialized;

            if (FriendsManager.HasInstance)
            {
                FriendsManager.Instance.OnFriendsListUpdated -= HandleFriendsListUpdated;
            }

            NetworkService.Current.OnUnexpectedClientDisconnect -= HandleUnexpectedClientDisconnect;

            if (_lobby != null)
            {
                _lobby.OnLobbyCreated -= HandleOnLobbyCreated;
                _lobby.OnLobbyJoined -= HandleOnLobbyJoined;
                _lobby.OnLobbyLeft -= HandleOnLobbyLeft;
                _lobby.OnPlayerJoined -= HandleOnPlayerJoined;
                _lobby.OnPlayerLeft -= HandleOnPlayerLeft;
                _lobby.OnLobbyError -= HandleOnLobbyError;
                _lobby.OnGameStarted -= HandleOnGameStarted;
                _lobby.OnReadyStatusChanged -= HandleOnReadyStatusChanged;
                _lobby.OnLobbyUpdated -= HandleOnLobbyUpdated;
                _lobby.OnAutoRejoinStateChanged -= HandleAutoRejoinStateChanged;
            }

            for (int i = 0; i < _cardPool.Count; i++)
            {
                if (_cardPool[i] != null)
                {
                    _cardPool[i].OnKickClicked -= HandleOnKickPlayerClicked;
                    _cardPool[i].OnAddFriendClicked -= HandleOnAddFriendClicked;
                }
            }
        }

        private void OnCreateLobbyButtonClicked()
        {
            SetMenuButtonsInteractable(false);
            _lobby.AllowJoinInProgress = allowJoinInProgress;
            _lobby.UseShortJoinCode = useShortJoinCode;
            _lobby.AllowAutoRejoin = allowAutoRejoin;
            _lobby.CreateLobby(maxLobbyMembers);
        }

        private void OnJoinLobbyButtonClicked()
        {
            string code = joinCodeInputField.text;
            if (string.IsNullOrWhiteSpace(code))
            {
                NotificationManager.Show("Please enter a valid room code.", NotificationType.Warning);
                return;
            }
            SetMenuButtonsInteractable(false);
            _lobby.JoinLobbyByCode(code);
        }

        private void OnCopyCodeButtonClicked()
        {
            if (string.IsNullOrEmpty(_lobby.CurrentJoinCode)) return;
            GUIUtility.systemCopyBuffer = _lobby.CurrentJoinCode;
        }

        private void OnLeaveLobbyButtonClicked()
        {
            SetLobbyButtonsInteractable(false);
            _lobby.LeaveLobby();
        }

        private void OnActionButtonClicked()
        {
            if (_lobby.IsLobbyOwner)
            {
                OnStartGameButtonClicked();
            }
            else
            {
                OnToggleReadyButtonClicked();
            }
        }

        private void OnToggleReadyButtonClicked()
        {
            startGameButton.interactable = false;
            _lobby.ToggleReadyStatus(HandleSetReadyStatusCallback);
        }

        private void HandleSetReadyStatusCallback(bool success, string error)
        {
            startGameButton.interactable = true;
            if (!success)
            {
                NotificationManager.Show($"Failed to update ready: {error}", NotificationType.Error);
                return;
            }

            UpdateActionButtonsDisplay();

            if (_activePlayerCards.TryGetValue(_lobby.LocalProductUserId.ToString(), out LobbyPlayerCardUI card))
            {
                card.SetReady(_lobby.IsLocalReady);
            }
        }

        private void OnStartGameButtonClicked()
        {
            SetLobbyButtonsInteractable(false);

            FriendsOverlayManager.Instance?.PersistForGameScene();

            NetworkService.Current.StartHost(nextSceneName);

            // Signal other players via EOS Lobby that match has started
            _lobby.StartGame(HandleStartGameCallback);
        }

        private void HandleStartGameCallback(bool success, string error)
        {
            if (!success)
            {
                NetworkService.Current.StopNetwork();
                UpdateActionButtonsDisplay();
                NotificationManager.Show($"Failed to start match: {error}", NotificationType.Error);
            }
        }

        private void HandleOnGameStarted(string hostAddress)
        {
            if (NetworkService.Current.IsClientActive || NetworkService.Current.IsClientConnected)
            {
                return;
            }

            SetLobbyButtonsInteractable(false);

            FriendsOverlayManager.Instance?.PersistForGameScene();

            NotificationManager.Show("Match is starting...", NotificationType.Normal);
            NetworkService.Current.StartClient(hostAddress);
        }

        private void HandleOnLobbyCreated(string shortCode)
        {
            roomCodeText.text = shortCode;
            UpdateMemberCountDisplay();
            UpdateActionButtonsDisplay();
            SetPanelState(isInLobby: true);
            DeactivateAllCards();
            AddPlayerCard(_lobby.LocalProductUserId);
        }

        private void HandleOnLobbyJoined(string hostAddress)
        {
            if (_lobby.IsAutoRejoining)
            {
                return;
            }

            roomCodeText.text = _lobby.CurrentJoinCode;
            UpdateMemberCountDisplay();
            UpdateActionButtonsDisplay();
            SetPanelState(isInLobby: true);
            SyncPlayerCards();
        }

        private void UpdateActionButtonsDisplay()
        {
            if (_lobby.IsLobbyOwner)
            {
                _actionButtonText.text = "Start";
                startGameButton.gameObject.SetActive(true);
                startGameButton.interactable = _lobby.AreAllJoinersReady;
                leaveLobbyButton.interactable = true;
            }
            else
            {
                _actionButtonText.text = _lobby.IsLocalReady ? "Unready" : "Ready";
                startGameButton.gameObject.SetActive(true);
                startGameButton.interactable = true;
                leaveLobbyButton.interactable = !_lobby.IsLocalReady;
            }
        }

        private void HandleOnReadyStatusChanged(bool allReady)
        {
            if (_lobby.IsLobbyOwner)
            {
                startGameButton.interactable = allReady;
            }
        }

        private void HandleOnLobbyLeft()
        {
            DeactivateAllCards();
            SetPanelState(isInLobby: false);
            SetMenuButtonsInteractable(true);
            leaveLobbyButton.interactable = true;
            joinCodeInputField.text = string.Empty;
            NetworkService.Current.StopNetwork();
        }

        private void HandleOnPlayerJoined(ProductUserId userId)
        {
            UpdateMemberCountDisplay();
            AddPlayerCard(userId);
            NotificationManager.Show("A player joined the room.", NotificationType.Normal);
        }

        private void HandleOnPlayerLeft(ProductUserId userId)
        {
            UpdateMemberCountDisplay();
            RemovePlayerCard(userId);
            NotificationManager.Show("A player left the room.", NotificationType.Normal);
        }

        private void HandleOnLobbyError(string errorMessage)
        {
            SetMenuButtonsInteractable(true);
            leaveLobbyButton.interactable = true;
            Debug.LogError($"[LobbyUIManager] {errorMessage}");
            NotificationManager.Show(errorMessage, NotificationType.Error);
        }

        private void HandleUnexpectedClientDisconnect()
        {
            if (_lobby != null && _lobby.IsAutoRejoining)
            {
                return;
            }

            NotificationManager.Show("Disconnected from server.", NotificationType.Warning);
        }

        private void HandleAutoRejoinStateChanged(bool isRejoining, string message)
        {
            if (isRejoining)
            {
                SetPanelState(isInLobby: false);
                SetMenuButtonsInteractable(false);
                if (!string.IsNullOrEmpty(message))
                {
                    NotificationManager.Show(message, NotificationType.Warning);
                }
                return;
            }

            if (_lobby != null && _lobby.IsInLobby && !_lobby.HasGameStarted)
            {
                RestoreInLobbyUI();
            }
            else if (_lobby == null || !_lobby.IsInLobby)
            {
                SetPanelState(isInLobby: false);
                SetMenuButtonsInteractable(true);
            }

            if (!string.IsNullOrEmpty(message))
            {
                NotificationManager.Show(message, NotificationType.Normal);
            }
        }

        private void SetPanelState(bool isInLobby)
        {
            menuPanel.SetActive(!isInLobby);
            inLobbyPanel.SetActive(isInLobby);
        }

        private void SetMenuButtonsInteractable(bool interactable)
        {
            createLobbyButton.interactable = interactable;
            joinLobbyButton.interactable = interactable;
            joinCodeInputField.interactable = interactable;
        }

        private void SetLobbyButtonsInteractable(bool interactable)
        {
            leaveLobbyButton.interactable = interactable;
            startGameButton.interactable = interactable;
        }

        private void UpdateMemberCountDisplay()
        {
            if (memberCountText != null && _lobby != null)
            {
                memberCountText.text = $"{_lobby.CurrentMemberCount} / {maxLobbyMembers}";
            }
        }

        #region Player Cards Management

        private void HandleOnLobbyUpdated()
        {
            foreach (var card in _activePlayerCards.Values)
            {
                card.UpdatePlayerName(_lobby.GetMemberDisplayName(card.UserId));
                card.SetReady(_lobby.IsMemberReady(card.UserId));
                bool canKick = _lobby.IsLobbyOwner && (card.UserId != _lobby.LocalProductUserId);
                card.SetKickButtonActive(canKick);
                card.SetAddFriendButtonActive(CanAddFriend(card.UserId));
            }
        }

        private LobbyPlayerCardUI GetCardFromPool()
        {
            for (int i = 0; i < _cardPool.Count; i++)
            {
                if (!_cardPool[i].gameObject.activeSelf)
                    return _cardPool[i];
            }

            LobbyPlayerCardUI newCard = Instantiate(playerCardPrefab, playerCardsParent);
            newCard.OnKickClicked += HandleOnKickPlayerClicked;
            newCard.OnAddFriendClicked += HandleOnAddFriendClicked;
            _cardPool.Add(newCard);
            return newCard;
        }

        private void AddPlayerCard(ProductUserId userId)
        {
            if (userId == null) return;
            string key = userId.ToString();
            if (_activePlayerCards.ContainsKey(key)) return;

            LobbyPlayerCardUI card = GetCardFromPool();
            card.transform.SetAsLastSibling();

            string playerName = (userId == _lobby.LocalProductUserId)
                ? EOSSDKManager.LocalDisplayName
                : _lobby.GetMemberDisplayName(userId);

            bool canKick = _lobby.IsLobbyOwner && (userId != _lobby.LocalProductUserId);
            bool canAddFriend = CanAddFriend(userId);
            card.SetData(userId, playerName, GetRandomAvatar(), _lobby.IsMemberReady(userId), canKick, canAddFriend);
            card.gameObject.SetActive(true);
            _activePlayerCards[key] = card;
        }

        private static bool IsFriendsSystemAvailable()
        {
            return FriendsManager.HasInstance && FriendsManager.Instance.IsInitialized;
        }

        private void SubscribeFriendsEvents()
        {
            if (!FriendsManager.HasInstance) return;
            FriendsManager.Instance.OnFriendsListUpdated -= HandleFriendsListUpdated;
            FriendsManager.Instance.OnFriendsListUpdated += HandleFriendsListUpdated;
        }

        private void HandleFriendsSystemInitialized()
        {
            SubscribeFriendsEvents();
            UpdateAllCardsAddFriendStatus();
        }

        private bool CanAddFriend(ProductUserId userId)
        {
            if (userId == null || userId == _lobby.LocalProductUserId) return false;
            if (!IsFriendsSystemAvailable()) return false;

            string displayName = _lobby.GetMemberDisplayName(userId);
            FriendRelationStatus status = FriendsManager.Instance.GetRelationship(userId.ToString(), displayName);
            return status == FriendRelationStatus.None;
        }

        private void HandleOnAddFriendClicked(ProductUserId targetUserId)
        {
            Assert.IsNotNull(targetUserId, "[LobbyUIManager] targetUserId must not be null.");
            if (!CanAddFriend(targetUserId)) return;

            string targetPuid = targetUserId.ToString();
            string displayName = _lobby.GetMemberDisplayName(targetUserId);

            FriendsManager.Instance?.SendFriendRequest(targetPuid, displayName, LoginProvider.None, HandleAddFriendCompleted);
        }

        private void HandleAddFriendCompleted(bool success, string message)
        {
            if (success)
            {
                NotificationManager.Show(message ?? "Friend request sent!", NotificationType.Normal);
                UpdateAllCardsAddFriendStatus();
            }
            else if (!string.IsNullOrEmpty(message))
            {
                NotificationManager.Show(message, NotificationType.Error);
            }
        }

        private void HandleFriendsListUpdated()
        {
            UpdateAllCardsAddFriendStatus();
        }

        private void UpdateAllCardsAddFriendStatus()
        {
            foreach (var card in _activePlayerCards.Values)
            {
                card.SetAddFriendButtonActive(CanAddFriend(card.UserId));
            }
        }

        private void HandleOnKickPlayerClicked(ProductUserId targetUserId)
        {
            Assert.IsNotNull(targetUserId, "[LobbyUIManager] targetUserId must not be null.");
            if (!_lobby.IsLobbyOwner) return;

            _lobby.KickMember(targetUserId, HandleKickMemberCompleted);
        }

        private void HandleKickMemberCompleted(bool success, string error)
        {
            if (!success && !string.IsNullOrEmpty(error))
            {
                NotificationManager.Show($"Failed to kick player: {error}", NotificationType.Error);
            }
        }

        private void RemovePlayerCard(ProductUserId userId)
        {
            string key = userId.ToString();
            if (_activePlayerCards.TryGetValue(key, out LobbyPlayerCardUI card))
            {
                card.gameObject.SetActive(false);
                _activePlayerCards.Remove(key);
            }
        }

        private void DeactivateAllCards()
        {
            for (int i = 0; i < _cardPool.Count; i++)
            {
                _cardPool[i].gameObject.SetActive(false);
            }
            _activePlayerCards.Clear();
        }

        private void SyncPlayerCards()
        {
            DeactivateAllCards();
            var memberIds = _lobby.GetCurrentMemberUserIds();
            for (int i = 0; i < memberIds.Count; i++)
            {
                AddPlayerCard(memberIds[i]);
            }
        }

        private Sprite GetRandomAvatar()
        {
            return playerCardImages[UnityEngine.Random.Range(0, playerCardImages.Count)];
        }

        private static string ResolveLobbyDisplayName(string address)
        {
            return LobbyManager.Instance?.ResolveDisplayName(address);
        }

        #endregion
    }
}