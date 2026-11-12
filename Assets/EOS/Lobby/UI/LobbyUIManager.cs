using EOS.Notification;
using EOS.P2P;
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
            Assert.IsNotNull(menuPanel, "[LobbyUIManager] menuPanel is null! Please assign in inspector.");
            Assert.IsNotNull(inLobbyPanel, "[LobbyUIManager] inLobbyPanel is null! Please assign in inspector.");
            Assert.IsNotNull(createLobbyButton, "[LobbyUIManager] createLobbyButton is null! Please assign in inspector.");
            Assert.IsNotNull(joinCodeInputField, "[LobbyUIManager] joinCodeInputField is null! Please assign in inspector.");
            Assert.IsNotNull(joinLobbyButton, "[LobbyUIManager] joinLobbyButton is null! Please assign in inspector.");
            Assert.IsNotNull(roomCodeText, "[LobbyUIManager] roomCodeText is null! Please assign in inspector.");
            Assert.IsNotNull(copyCodeButton, "[LobbyUIManager] copyCodeButton is null! Please assign in inspector.");
            Assert.IsNotNull(memberCountText, "[LobbyUIManager] memberCountText is null! Please assign in inspector.");
            Assert.IsNotNull(leaveLobbyButton, "[LobbyUIManager] leaveLobbyButton is null! Please assign in inspector.");
            Assert.IsNotNull(startGameButton, "[LobbyUIManager] startGameButton is null! Please assign in inspector.");
            Assert.IsNotNull(playerCardPrefab, "[LobbyUIManager] playerCardPrefab is null! Please assign in inspector.");
            Assert.IsNotNull(playerCardsParent, "[LobbyUIManager] playerCardsParent is null! Please assign in inspector.");
            Assert.IsTrue(playerCardImages != null && playerCardImages.Count > 0, "[LobbyUIManager] playerCardImages list is empty! Please assign at least one image.");
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
        }

        private void Start()
        {
            if (FriendsManager.Instance != null)
            {
                FriendsManager.Instance.OnFriendsListUpdated -= HandleFriendsListUpdated;
                FriendsManager.Instance.OnFriendsListUpdated += HandleFriendsListUpdated;
            }

            if (EOSNetworkManager.Instance != null)
            {
                EOSNetworkManager.Instance.CustomDisplayNameResolver = ResolveLobbyDisplayName;
                EOSNetworkManager.Instance.OnUnexpectedClientDisconnect -= HandleUnexpectedClientDisconnect;
                EOSNetworkManager.Instance.OnUnexpectedClientDisconnect += HandleUnexpectedClientDisconnect;
            }

            if (_lobby != null && _lobby.IsInLobby)
            {
                RestoreInLobbyUI();
            }
            else
            {
                joinCodeInputField.text = string.Empty;
                SetPanelState(isInLobby: false);
                SetMenuButtonsInteractable(false);
                _lobby.CheckAndCleanOrphanLobbyOnLogin(HandleOrphanCleanupComplete);
                _lobby.AllowJoinInProgress = allowJoinInProgress;
            }
        }

        private void HandleOrphanCleanupComplete()
        {
            SetMenuButtonsInteractable(true);
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
            if (FriendsManager.Instance != null)
            {
                FriendsManager.Instance.OnFriendsListUpdated -= HandleFriendsListUpdated;
            }

            EOSNetworkManager.Instance.OnUnexpectedClientDisconnect -= HandleUnexpectedClientDisconnect;

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

            // Start Mirror Host first so transport is listening for clients
            EOSNetworkManager.Instance?.StartEosHost(nextSceneName);

            // Signal other players via EOS Lobby that match has started
            _lobby.StartGame(HandleStartGameCallback);
        }

        private void HandleStartGameCallback(bool success, string error)
        {
            if (!success)
            {
                EOSNetworkManager.Instance?.StopNetwork();
                UpdateActionButtonsDisplay();
                NotificationManager.Show($"Failed to start match: {error}", NotificationType.Error);
            }
        }

        private void HandleOnGameStarted(string hostAddress)
        {
            SetLobbyButtonsInteractable(false);

            FriendsOverlayManager.Instance?.PersistForGameScene();

            NotificationManager.Show("Match is starting...", NotificationType.Normal);
            EOSNetworkManager.Instance?.StartEosClient(hostAddress);
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
            EOSNetworkManager.Instance?.StopNetwork();
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
            _lobby?.HandleNetworkDisconnect();
            NotificationManager.Show("Disconnected from server.", NotificationType.Warning);
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
                ? EpicTransport.EOSSDKComponent.DisplayName
                : _lobby.GetMemberDisplayName(userId);

            bool canKick = _lobby.IsLobbyOwner && (userId != _lobby.LocalProductUserId);
            bool canAddFriend = CanAddFriend(userId);
            card.SetData(userId, playerName, GetRandomAvatar(), _lobby.IsMemberReady(userId), canKick, canAddFriend);
            card.gameObject.SetActive(true);
            _activePlayerCards[key] = card;
        }

        private bool CanAddFriend(ProductUserId userId)
        {
            if (userId == null || userId == _lobby.LocalProductUserId) return false;
            if (FriendsManager.Instance == null) return false;

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

        private static string ResolveLobbyDisplayName(Mirror.NetworkConnectionToClient conn)
        {
            if (conn is Mirror.LocalConnectionToClient || conn.connectionId == 0)
            {
                return !string.IsNullOrWhiteSpace(EpicTransport.EOSSDKComponent.DisplayName)
                    ? EpicTransport.EOSSDKComponent.DisplayName
                    : "Host";
            }

            return LobbyManager.Instance?.ResolveDisplayName(conn.address);
        }

        #endregion
    }
}