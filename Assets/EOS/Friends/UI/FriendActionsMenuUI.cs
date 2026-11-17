using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace EOS.Friends
{
    // action menu popover showing dynamic operations (Add, Invite, Join, Block/Unblock, Remove, Accept/Decline).
    // Uses object pooling to instantiate and recycle action buttons dynamically depending on the active tab and target friend state.
    // Positions smoothly right beside the friend card (6px offset), accounting for pivot.
    // Closes immediately on any action or when clicking anywhere outside the menu bounds.
    [RequireComponent(typeof(RectTransform))]
    internal class FriendActionsMenuUI : MonoBehaviour
    {
        [Header("Button Pooling")]
        [SerializeField] private Button actionButtonPrefab;
        [SerializeField] private Transform buttonsParent;

        private readonly List<Button> _buttonPool = new();
        private readonly List<Button> _activeButtons = new();

        private IFriendsService _friendsService;
        private ILobbyInviteService _lobbyInviteService;
        private FriendModel _targetFriend;
        private LobbyInviteModel _targetInvite;
        private FriendsTabType _currentTab;

        public static FriendActionsMenuUI ActiveMenu { get; private set; }

        public bool IsOpen => gameObject.activeSelf;
        public FriendModel CurrentTargetFriend => _targetFriend;
        public LobbyInviteModel CurrentTargetInvite => _targetInvite;

        public event Action OnActionExecuted;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (UnityEditor.EditorUtility.IsPersistent(this))
            {
                return;
            }

            if (actionButtonPrefab == null) Debug.LogWarning("[FriendActionsMenuUI] actionButtonPrefab is null.", this);
        }
#endif
        private void Awake()
        {
            Transform actionsTransform = transform.Find("Actions");
            buttonsParent = actionsTransform != null ? actionsTransform : transform;
        }

        public void Initialize(IFriendsService friendsService, ILobbyInviteService lobbyInviteService)
        {
            _friendsService = friendsService;
            _lobbyInviteService = lobbyInviteService;
            Hide();
        }

        public void ShowForFriend(FriendModel friend, FriendsTabType tab, RectTransform cardTransform)
        {
            ActiveMenu?.Hide();
            ActiveMenu = this;

            _targetFriend = friend;
            _targetInvite = null;
            _currentTab = tab;

            ConfigureButtonsForFriend(tab, friend);
            PositionToCard(cardTransform);
            gameObject.SetActive(true);
        }

        public void ShowForInvite(LobbyInviteModel invite, RectTransform cardTransform)
        {
            ActiveMenu?.Hide();
            ActiveMenu = this;

            _targetFriend = null;
            _targetInvite = invite;
            _currentTab = FriendsTabType.Notifications;

            ReleaseActiveButtons();
            string acceptLabel = (invite != null && !string.IsNullOrEmpty(invite.JoinCode)) ? "Join Lobby" : "Join Game";
            AddActionButton(acceptLabel, HandleAcceptLobbyInviteClicked);
            AddActionButton("Decline", HandleDeclineLobbyInviteClicked);

            PositionToCard(cardTransform);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            ActiveMenu = null;
            ReleaseActiveButtons();
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            ActiveMenu = null;
        }

        private void PositionToCard(RectTransform cardTransform)
        {
            Assert.IsNotNull(cardTransform, "[FriendActionsMenuUI] cardTransform cannot be null.");

            Vector3[] corners = new Vector3[4];
            cardTransform.GetWorldCorners(corners);
            Vector3 cardTopLeft = transform.parent.InverseTransformPoint(corners[1]);

            RectTransform self = (RectTransform)transform;
            float panelWidth = self.rect.width;
            float panelHeight = self.rect.height;

            // Offset the panel so its right edge is 6 units away from the card's left edge, accounting for pivot
            float rightPivotOffset = (1f - self.pivot.x) * panelWidth;
            float topPivotOffset = (1f - self.pivot.y) * panelHeight;

            float targetX = cardTopLeft.x - rightPivotOffset - 6f;
            float targetY = cardTopLeft.y - topPivotOffset;

            self.localPosition = new Vector3(targetX, targetY, 0f);
        }

        private void ReleaseActiveButtons()
        {
            for (int i = 0; i < _activeButtons.Count; i++)
            {
                Button btn = _activeButtons[i];
                btn.onClick.RemoveAllListeners();
                btn.gameObject.SetActive(false);
                _buttonPool.Add(btn);
            }
            _activeButtons.Clear();
        }

        private Button AddActionButton(string label, UnityEngine.Events.UnityAction onClickAction)
        {
            Button btn;
            if (_buttonPool.Count > 0)
            {
                btn = _buttonPool[_buttonPool.Count - 1];
                _buttonPool.RemoveAt(_buttonPool.Count - 1);
            }
            else
            {
                Assert.IsNotNull(actionButtonPrefab, "[FriendActionsMenuUI] actionButtonPrefab is null.");
                btn = Instantiate(actionButtonPrefab, buttonsParent != null ? buttonsParent : transform);
            }

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(onClickAction);

            TMP_Text txt = btn.GetComponentInChildren<TMP_Text>();
            if (txt != null)
            {
                txt.text = label;
            }

            btn.gameObject.SetActive(true);
            btn.transform.SetAsLastSibling();
            _activeButtons.Add(btn);
            return btn;
        }

        private void ConfigureButtonsForFriend(FriendsTabType tab, FriendModel friend)
        {
            ReleaseActiveButtons();

            bool inSameSession = _friendsService != null && _friendsService.IsPlayerInSameLobbyOrGame(friend);
            LobbyInviteModel pendingInvite = _friendsService != null ? _friendsService.GetPendingInviteForFriend(friend) : null;
            bool hasInvite = pendingInvite != null;

            switch (tab)
            {
                case FriendsTabType.Blocked:
                    AddActionButton("Unblock", HandleUnblockClicked);
                    break;

                case FriendsTabType.AddFriend:
                    if (friend.Status != FriendRelationStatus.CrossPlay &&
                        friend.Status != FriendRelationStatus.Platform &&
                        friend.Status != FriendRelationStatus.PendingOutgoing)
                    {
                        AddActionButton("Add Friend", HandleAddClicked);
                    }
                    AddActionButton("Block", HandleBlockClicked);
                    break;

                case FriendsTabType.Notifications:
                    if (friend.IsJoinRequest)
                    {
                        AddActionButton("Accept", HandleAcceptJoinRequestClicked);
                        AddActionButton("Decline", HandleDeclineJoinRequestClicked);
                        AddActionButton("Block", HandleBlockClicked);
                    }
                    else
                    {
                        AddActionButton("Accept", HandleAcceptFriendRequestClicked);
                        AddActionButton("Decline", HandleDeclineFriendRequestClicked);
                        AddActionButton("Block", HandleBlockClicked);
                    }
                    break;

                case FriendsTabType.Platform:
                case FriendsTabType.AllFriends:
                case FriendsTabType.CrossPlay:
                default:
                    // Hide invite and join buttons if in the same lobby or game together
                    if (!inSameSession)
                    {
                        if (friend.Presence != FriendPresenceState.Offline)
                        {
                            AddActionButton("Invite", HandleInviteClicked);
                        }

                        // Show Join button if they invited us OR if they are in a lobby / match
                        if (hasInvite || friend.Presence == FriendPresenceState.InMatch || friend.Presence == FriendPresenceState.InLobby)
                        {
                            AddActionButton("Join", HandleJoinClicked);
                        }
                    }

                    AddActionButton("Block", HandleBlockClicked);
                    if (friend.Status == FriendRelationStatus.CrossPlay)
                    {
                        AddActionButton("Remove", HandleRemoveClicked);
                    }
                    break;
            }
        }

        private void HandleAddClicked()
        {
            FriendModel friend = _targetFriend;
            Hide();
            if (friend == null) return;
            string id = !string.IsNullOrEmpty(friend.ProductUserId) ? friend.ProductUserId : friend.PlatformUserId;
            _friendsService?.SendFriendRequest(id, friend.DisplayName, friend.Platform, HandleActionCallback);
        }

        private void HandleInviteClicked()
        {
            FriendModel friend = _targetFriend;
            Hide();
            if (friend == null) return;
            if (_friendsService != null && _friendsService.IsPlayerInSameLobbyOrGame(friend)) return;
            _friendsService?.InviteFriend(friend, HandleActionCallback);
        }

        private void HandleJoinClicked()
        {
            FriendModel friend = _targetFriend;
            Hide();
            if (friend == null) return;
            if (_friendsService != null && _friendsService.IsPlayerInSameLobbyOrGame(friend)) return;

            // Auto-join if a pending invite from this friend already exists
            LobbyInviteModel pendingInvite = _friendsService != null ? _friendsService.GetPendingInviteForFriend(friend) : null;
            if (pendingInvite != null && _lobbyInviteService != null)
            {
                _lobbyInviteService.AcceptInvite(pendingInvite.InviteId, HandleActionCallback);
                return;
            }

            _friendsService?.SendJoinRequest(friend, HandleActionCallback);
        }

        private void HandleAcceptJoinRequestClicked()
        {
            FriendModel friend = _targetFriend;
            Hide();
            string id = !string.IsNullOrEmpty(friend.ProductUserId) ? friend.ProductUserId : friend.PlatformUserId;
            _friendsService?.AcceptJoinRequest(id, HandleActionCallback);
        }

        private void HandleDeclineJoinRequestClicked()
        {
            FriendModel friend = _targetFriend;
            Hide();
            string id = !string.IsNullOrEmpty(friend.ProductUserId) ? friend.ProductUserId : friend.PlatformUserId;
            _friendsService?.DeclineJoinRequest(id, HandleActionCallback);
        }

        private void HandleAcceptFriendRequestClicked()
        {
            FriendModel friend = _targetFriend;
            Hide();
            string id = !string.IsNullOrEmpty(friend.ProductUserId) ? friend.ProductUserId : friend.PlatformUserId;
            _friendsService?.AcceptFriendRequest(id, HandleActionCallback);
        }

        private void HandleDeclineFriendRequestClicked()
        {
            FriendModel friend = _targetFriend;
            Hide();
            string id = !string.IsNullOrEmpty(friend.ProductUserId) ? friend.ProductUserId : friend.PlatformUserId;
            _friendsService?.DeclineFriendRequest(id, HandleActionCallback);
        }

        private void HandleBlockClicked()
        {
            FriendModel friend = _targetFriend;
            Hide();
            _friendsService?.BlockFriend(friend, HandleActionCallback);
        }

        private void HandleUnblockClicked()
        {
            FriendModel friend = _targetFriend;
            Hide();
            string id = !string.IsNullOrEmpty(friend.ProductUserId) ? friend.ProductUserId : friend.PlatformUserId;
            _friendsService?.UnblockPlayer(id, HandleActionCallback);
        }

        private void HandleRemoveClicked()
        {
            FriendModel friend = _targetFriend;
            Hide();
            string id = !string.IsNullOrEmpty(friend.ProductUserId) ? friend.ProductUserId : friend.PlatformUserId;
            _friendsService?.RemoveFriend(id, HandleActionCallback);
        }

        private void HandleAcceptLobbyInviteClicked()
        {
            LobbyInviteModel invite = _targetInvite;
            Hide();
            _lobbyInviteService?.AcceptInvite(invite.InviteId, HandleActionCallback);
        }

        private void HandleDeclineLobbyInviteClicked()
        {
            LobbyInviteModel invite = _targetInvite;
            Hide();
            _lobbyInviteService?.DeclineInvite(invite.InviteId);
            OnActionExecuted?.Invoke();
        }

        private void HandleActionCallback(bool success, string message)
        {
            if (!success && !string.IsNullOrEmpty(message))
            {
                Debug.LogWarning($"[FriendActionsMenuUI] Action failed: {message}");
            }
            OnActionExecuted?.Invoke();
        }
    }
}