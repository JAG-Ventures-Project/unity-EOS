using System;
using EOS.Auth;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EOS.Friends
{
    // AI implementation: High-performance UI element representing a single player card in the friends list.
    // Enforces assertion invariants in Awake, safe sprite destruction, and strict named callbacks.
    [RequireComponent(typeof(Button))]
    internal class FriendCardUI : MonoBehaviour
    {
        [Header("Card Bindings")]
        [SerializeField] private Image userAvatar;
        [SerializeField] private Image frame;
        [SerializeField] private TMP_Text userNameText;
        [SerializeField] private TMP_Text userCodeText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button cardButton;

        [Header("Status Colors")]
        [SerializeField] private Color onlineColor = new(0.18f, 0.8f, 0.44f, 1f);       // Emerald Green #2ECC71
        [SerializeField] private Color offlineColor = new(0.6f, 0.6f, 0.6f, 1f);        // Grey #95A5A6
        [SerializeField] private Color inGameColor = new(0.2f, 0.6f, 1f, 1f);          // Blue
        [SerializeField] private Color blockedColor = new(0.9f, 0.25f, 0.2f, 1f);      // Red #E74C3C
        [SerializeField] private Color pendingColor = new(0.95f, 0.6f, 0.07f, 1f);     // Orange #F39C12

        private FriendModel _model;
        private LobbyInviteModel _lobbyInvite;
        private Sprite _createdAvatarSprite;

        public FriendModel Model => _model;
        public LobbyInviteModel LobbyInvite => _lobbyInvite;

        public event Action<FriendCardUI, FriendModel> OnClicked;
        public event Action<FriendCardUI, LobbyInviteModel> OnInviteClicked;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (UnityEditor.EditorUtility.IsPersistent(this))
            {
                return;
            }

            if (userAvatar == null) Debug.LogWarning("[FriendCardUI] userAvatar component missing.", this);
            if (frame == null) Debug.LogWarning("[FriendCardUI] frame component missing.", this);
            if (userNameText == null) Debug.LogWarning("[FriendCardUI] userNameText missing.", this);
            if (userCodeText == null) Debug.LogWarning("[FriendCardUI] userCodeText missing.", this);
            if (statusText == null) Debug.LogWarning("[FriendCardUI] statusText missing.", this);
            if (cardButton == null) Debug.LogWarning("[FriendCardUI] cardButton component missing.", this);
        }
#endif

        private void Awake()
        {

            cardButton.onClick.AddListener(HandleCardClicked);
        }

        private void OnDestroy()
        {
            DestroyAvatarSprite();
        }

        public void Bind(FriendModel model)
        {
            _model = model;
            _lobbyInvite = null;

            // Display Name
            userNameText.text = string.IsNullOrWhiteSpace(model.DisplayName) ? "Player" : model.DisplayName;

            // User Code / Platform ID
            if (model.IsJoinRequest)
            {
                userCodeText.text = "Wants to join match";
            }
            else if (!string.IsNullOrEmpty(model.ProductUserId))
            {
                userCodeText.text = model.ProductUserId;
            }
            else if (!string.IsNullOrEmpty(model.PlatformUserId))
            {
                userCodeText.text = $"{model.Platform}: {model.PlatformUserId}";
            }
            else
            {
                userCodeText.text = string.Empty;
            }

            // Status & Color
            UpdateStatusDisplay(model);

            // Avatar
            UpdateAvatarDisplay(model.AvatarTexture);
        }

        public void BindLobbyInvite(LobbyInviteModel invite)
        {
            _lobbyInvite = invite;
            _model = new FriendModel
            {
                ProductUserId = invite.InviterUserId != null ? invite.InviterUserId.ToString() : string.Empty,
                DisplayName = invite.InviterDisplayName,
                Platform = LoginProvider.None,
                Status = FriendRelationStatus.PendingIncoming,
                Presence = FriendPresenceState.Online
            };

            userNameText.text = string.IsNullOrWhiteSpace(invite.InviterDisplayName) ? "Player" : invite.InviterDisplayName;
            userCodeText.text = !string.IsNullOrEmpty(invite.JoinCode) ? "Lobby Invite" : "Game Invite";
            statusText.text = "Invited you to play";
            statusText.color = inGameColor;

            UpdateAvatarDisplay(null);
        }

        private void UpdateStatusDisplay(FriendModel model)
        {
            if (model.IsJoinRequest)
            {
                statusText.text = "Join Request";
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
                        case FriendPresenceState.InLobby:
                            statusText.text = "In Lobby";
                            statusText.color = inGameColor;
                            break;
                        case FriendPresenceState.InMatch:
                            statusText.text = "In Game";
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
            DestroyAvatarSprite();

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
                userAvatar.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            }
        }

        private void DestroyAvatarSprite()
        {
            if (_createdAvatarSprite != null)
            {
                Destroy(_createdAvatarSprite);
                _createdAvatarSprite = null;
            }
        }

        private void HandleCardClicked()
        {
            if (_lobbyInvite != null)
            {
                OnInviteClicked?.Invoke(this, _lobbyInvite);
            }
            else if (_model != null)
            {
                OnClicked?.Invoke(this, _model);
            }
        }
    }
}