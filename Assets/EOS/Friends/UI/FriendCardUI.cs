using System;
using LazyList;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EOS.Friends
{
    // AI implementation: Player card view displaying avatar, name, status, and tab-based action menus with per-button bindings.
    [RequireComponent(typeof(Button))]
    public class FriendCardUI : LazyListItemView, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Card Bindings")]
        [SerializeField] private Image userAvatar;
        [SerializeField] private TMP_Text userNameText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button cardButton;

        [Header("Action Menus")]
        [SerializeField] private Transform actionsContainer;
        [SerializeField] private GameObject friendsActionMenu;
        [SerializeField] private GameObject notificationsActionMenu;
        [SerializeField] private GameObject addFriendActionMenu;
        [SerializeField] private GameObject blockedActionMenu;
        [SerializeField] private bool showActionsOnlyOnHover = true;

        [Header("Friends Menu Buttons")]
        [SerializeField] private Button joinButton;
        [SerializeField] private Button inviteButton;
        [SerializeField] private Button removeButton;
        [SerializeField] private Button blockButton;

        [Header("Notifications Menu Buttons")]
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button declineButton;

        [Header("Add Friend Menu Buttons")]
        [SerializeField] private Button addFriendButton;

        [Header("Blocked Menu Buttons")]
        [SerializeField] private Button unblockButton;

        [Header("Status Colors")]
        [SerializeField] private Color onlineColor = new(0.18f, 0.8f, 0.44f, 1f);
        [SerializeField] private Color offlineColor = new(0.6f, 0.6f, 0.6f, 1f);
        [SerializeField] private Color inGameColor = new(0.2f, 0.6f, 1f, 1f);
        [SerializeField] private Color blockedColor = new(0.9f, 0.25f, 0.2f, 1f);
        [SerializeField] private Color pendingColor = new(0.95f, 0.6f, 0.07f, 1f);

        private FriendModel _model;
        private LobbyInviteModel _lobbyInvite;
        private Sprite _createdAvatarSprite;
        private Texture2D _boundAvatarTexture;
        private bool _hasAvatarBound;
        private bool _hasActiveMenu;
        private bool _isHovered;
        private HorizontalLayoutGroup _friendsActionLayoutGroup;
        private RectTransform _friendsActionMenuRect;
        private bool _friendsActionLayoutDirty;
        private Action<FriendActionType, FriendModel, LobbyInviteModel> _onActionClicked;

        public FriendModel Model => _model;
        public LobbyInviteModel LobbyInvite => _lobbyInvite;

        public event Action<FriendCardUI> OnClicked;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (UnityEditor.EditorUtility.IsPersistent(this))
            {
                return;
            }

            if (cardButton == null)
            {
                TryGetComponent(out cardButton);
                UnityEditor.EditorUtility.SetDirty(this);
            }
            if (userAvatar == null) Debug.LogWarning("[FriendCardUI] userAvatar component missing.", this);
            if (userNameText == null) Debug.LogWarning("[FriendCardUI] userNameText missing.", this);
            if (statusText == null) Debug.LogWarning("[FriendCardUI] statusText missing.", this);
            if (cardButton == null) Debug.LogWarning("[FriendCardUI] cardButton component missing.", this);
            if (actionsContainer == null) Debug.LogWarning("[FriendCardUI] actionsContainer missing.", this);
            if (friendsActionMenu == null) Debug.LogWarning("[FriendCardUI] friendsActionMenu missing.", this);
            if (notificationsActionMenu == null) Debug.LogWarning("[FriendCardUI] notificationsActionMenu missing.", this);
            if (addFriendActionMenu == null) Debug.LogWarning("[FriendCardUI] addFriendActionMenu missing.", this);
            if (blockedActionMenu == null) Debug.LogWarning("[FriendCardUI] blockedActionMenu missing.", this);
            if (joinButton == null) Debug.LogWarning("[FriendCardUI] joinButton missing.", this);
            if (inviteButton == null) Debug.LogWarning("[FriendCardUI] inviteButton missing.", this);
            if (removeButton == null) Debug.LogWarning("[FriendCardUI] removeButton missing.", this);
            if (blockButton == null) Debug.LogWarning("[FriendCardUI] blockButton missing.", this);
            if (acceptButton == null) Debug.LogWarning("[FriendCardUI] acceptButton missing.", this);
            if (declineButton == null) Debug.LogWarning("[FriendCardUI] declineButton missing.", this);
            if (addFriendButton == null) Debug.LogWarning("[FriendCardUI] addFriendButton missing.", this);
            if (unblockButton == null) Debug.LogWarning("[FriendCardUI] unblockButton missing.", this);
        }
#endif

        private void Awake()
        {
            cardButton.onClick.AddListener(HandleCardClicked);

            joinButton.onClick.AddListener(HandleJoinClicked);
            inviteButton.onClick.AddListener(HandleInviteClicked);
            removeButton.onClick.AddListener(HandleRemoveClicked);
            blockButton.onClick.AddListener(HandleBlockClicked);
            acceptButton.onClick.AddListener(HandleAcceptClicked);
            declineButton.onClick.AddListener(HandleDeclineClicked);
            addFriendButton.onClick.AddListener(HandleAddFriendClicked);
            unblockButton.onClick.AddListener(HandleUnblockClicked);

            if (friendsActionMenu.TryGetComponent(out _friendsActionLayoutGroup))
            {
                _friendsActionMenuRect = (RectTransform)friendsActionMenu.transform;
                _friendsActionLayoutGroup.enabled = false;
            }

            UpdateActionsVisibility();
        }
        private void OnDisable()
        {
            if (_isHovered)
            {
                _isHovered = false;
                UpdateActionsVisibility();
            }
        }

        private void OnDestroy()
        {
            DestroyAvatarSprite();
        }

        public void Bind(
            FriendModel model,
            FriendsTabType tab,
            IFriendsService friendsService,
            Action<FriendActionType, FriendModel, LobbyInviteModel> onActionClicked)
        {
            _model = model;
            _lobbyInvite = null;
            _onActionClicked = onActionClicked;

            userNameText.text = string.IsNullOrWhiteSpace(model.DisplayName) ? "Player" : model.DisplayName;
            UpdateStatusDisplay(model);
            UpdateAvatarDisplay(model.AvatarTexture);
            ActivateActionMenu(tab, friendsService);
        }

        public void BindLobbyInvite(
            LobbyInviteModel invite,
            IFriendsService friendsService,
            Action<FriendActionType, FriendModel, LobbyInviteModel> onActionClicked)
        {
            _lobbyInvite = invite;
            _model = null;
            _onActionClicked = onActionClicked;

            userNameText.text = string.IsNullOrWhiteSpace(invite.InviterDisplayName) ? "Player" : invite.InviterDisplayName;
            statusText.text = "Let's Play Together!";
            statusText.color = inGameColor;

            UpdateAvatarDisplay(null);
            ActivateActionMenu(FriendsTabType.Notifications, friendsService);
        }

        public void SetSelected(bool isSelected)
        {
            if (!isSelected && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == cardButton.gameObject)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_lobbyInvite == null && _model == null)
            {
                return;
            }

            _isHovered = true;
            UpdateActionsVisibility();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            UpdateActionsVisibility();
        }

        private void ActivateActionMenu(FriendsTabType tab, IFriendsService friendsService)
        {
            _hasActiveMenu = true;

            friendsActionMenu.SetActive(tab == FriendsTabType.Friends);
            notificationsActionMenu.SetActive(tab == FriendsTabType.Notifications);
            addFriendActionMenu.SetActive(tab == FriendsTabType.AddFriend);
            blockedActionMenu.SetActive(tab == FriendsTabType.Blocked);

            switch (tab)
            {
                case FriendsTabType.Friends:
                    joinButton.gameObject.SetActive(FriendActionExecutor.IsActionAvailable(FriendActionType.Join, _model, tab, friendsService));
                    inviteButton.gameObject.SetActive(FriendActionExecutor.IsActionAvailable(FriendActionType.Invite, _model, tab, friendsService));
                    removeButton.gameObject.SetActive(FriendActionExecutor.IsActionAvailable(FriendActionType.Remove, _model, tab, friendsService));
                    _friendsActionLayoutDirty = true;
                    break;

                case FriendsTabType.AddFriend:
                    addFriendButton.gameObject.SetActive(FriendActionExecutor.IsActionAvailable(FriendActionType.AddFriend, _model, tab, friendsService));
                    _friendsActionLayoutDirty = false;
                    break;

                default:
                    _friendsActionLayoutDirty = false;
                    break;
            }

            UpdateActionsVisibility();
        }

        private void UpdateActionsVisibility()
        {
            bool shouldShow = (!showActionsOnlyOnHover || _isHovered) && _hasActiveMenu;
            actionsContainer.gameObject.SetActive(shouldShow);

            if (shouldShow && _friendsActionLayoutDirty)
            {
                RebuildFriendsActionLayout();
            }
        }

        private void RebuildFriendsActionLayout()
        {
            if (_friendsActionLayoutGroup == null || !friendsActionMenu.activeInHierarchy)
            {
                return;
            }

            _friendsActionLayoutDirty = false;
            _friendsActionLayoutGroup.enabled = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_friendsActionMenuRect);
            _friendsActionLayoutGroup.enabled = false;
        }

        private void UpdateStatusDisplay(FriendModel model)
        {
            if (model.IsJoinRequest)
            {
                statusText.text = "Wants to join match";
                statusText.color = inGameColor;
                return;
            }

            switch (model.Status)
            {
                case FriendRelationStatus.Blocked:
                    statusText.text = "Blocked";
                    statusText.color = blockedColor;
                    break;

                case FriendRelationStatus.PendingIncoming:
                    statusText.text = "Friend Request";
                    statusText.color = pendingColor;
                    break;

                case FriendRelationStatus.PendingOutgoing:
                    statusText.text = "Request Sent";
                    statusText.color = offlineColor;
                    break;

                case FriendRelationStatus.None:
                    statusText.text = "Not in friends";
                    statusText.color = offlineColor;
                    break;

                default:
                    switch (model.Presence)
                    {
                        case FriendPresenceState.Online:
                            statusText.text = "Online";
                            statusText.color = onlineColor;
                            break;
                        case FriendPresenceState.InGame:
                            statusText.text = "In Game";
                            statusText.color = onlineColor;
                            break;
                        case FriendPresenceState.InLobby:
                            statusText.text = "In Lobby";
                            statusText.color = inGameColor;
                            break;
                        case FriendPresenceState.InMatch:
                            statusText.text = "In Match";
                            statusText.color = inGameColor;
                            break;
                        default:
                            statusText.text = "Offline";
                            statusText.color = offlineColor;
                            break;
                    }
                    break;
            }
        }

        private void UpdateAvatarDisplay(Texture2D texture)
        {
            if (_hasAvatarBound && _boundAvatarTexture == texture)
            {
                return;
            }

            DestroyAvatarSprite();
            _hasAvatarBound = true;
            _boundAvatarTexture = texture;

            if (texture != null)
            {
                _createdAvatarSprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
                userAvatar.sprite = _createdAvatarSprite;
                userAvatar.color = Color.white;
            }
            else
            {
                userAvatar.sprite = null;
                userAvatar.color = new Color(0.1843f, 0.1843f, 0.2039f, 1f);
            }
        }

        private void DestroyAvatarSprite()
        {
            if (_createdAvatarSprite != null)
            {
                Destroy(_createdAvatarSprite);
                _createdAvatarSprite = null;
            }
            _boundAvatarTexture = null;
            _hasAvatarBound = false;
        }

        private void HandleJoinClicked() => _onActionClicked?.Invoke(FriendActionType.Join, _model, _lobbyInvite);
        private void HandleInviteClicked() => _onActionClicked?.Invoke(FriendActionType.Invite, _model, _lobbyInvite);
        private void HandleRemoveClicked() => _onActionClicked?.Invoke(FriendActionType.Remove, _model, _lobbyInvite);
        private void HandleBlockClicked() => _onActionClicked?.Invoke(FriendActionType.Block, _model, _lobbyInvite);
        private void HandleAcceptClicked() => _onActionClicked?.Invoke(FriendActionType.Accept, _model, _lobbyInvite);
        private void HandleDeclineClicked() => _onActionClicked?.Invoke(FriendActionType.Decline, _model, _lobbyInvite);
        private void HandleAddFriendClicked() => _onActionClicked?.Invoke(FriendActionType.AddFriend, _model, _lobbyInvite);
        private void HandleUnblockClicked() => _onActionClicked?.Invoke(FriendActionType.Unblock, _model, _lobbyInvite);

        private void HandleCardClicked()
        {
            if (_lobbyInvite != null || _model != null)
            {
                OnClicked?.Invoke(this);
            }
        }
    }
}
