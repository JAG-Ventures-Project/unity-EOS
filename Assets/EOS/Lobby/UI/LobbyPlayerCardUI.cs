using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Epic.OnlineServices;

namespace EOS.Lobby
{
    [DisallowMultipleComponent]
    public class LobbyPlayerCardUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private Button kickPlayerButton;
        [SerializeField] private Button addFriendButton;
        [SerializeField] private Image playerAvatarImage;
        [SerializeField] private GameObject readyIndicator;

        public ProductUserId UserId { get; private set; }
        public string PlayerName { get; private set; }
        public Sprite AvatarSprite { get; private set; }
        public event Action<ProductUserId> OnKickClicked;
        public event Action<ProductUserId> OnAddFriendClicked;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (UnityEditor.EditorUtility.IsPersistent(this))
            {
                return;
            }

            if (playerNameText == null) Debug.LogWarning("[LobbyPlayerCardUI] PlayerNameText must not be null.", this);
            if (kickPlayerButton == null) Debug.LogWarning("[LobbyPlayerCardUI] kickPlayerButton must not be null.", this);
            if (addFriendButton == null) Debug.LogWarning("[LobbyPlayerCardUI] addFriendButton must not be null.", this);
            if (playerAvatarImage == null) Debug.LogWarning("[LobbyPlayerCardUI] PlayerAvatarImage must not be null.", this);
            if (readyIndicator == null) Debug.LogWarning("[LobbyPlayerCardUI] readyIndicator must not be null.", this);
        }
#endif

        private void Awake()
        {
            kickPlayerButton.onClick.AddListener(OnKickPlayerClicked);
            addFriendButton.onClick.AddListener(OnAddFriendPlayerClicked);
        }

        private void OnDestroy()
        {
            kickPlayerButton.onClick.RemoveListener(OnKickPlayerClicked);
            addFriendButton.onClick.RemoveListener(OnAddFriendPlayerClicked);
        }

        public void SetData(ProductUserId userId, string playerName, Sprite avatarSprite, bool isReady = false, bool canKick = false, bool canAddFriend = false)
        {
            UserId = userId;
            PlayerName = !string.IsNullOrWhiteSpace(playerName) ? playerName : "Player";
            AvatarSprite = avatarSprite;

            playerNameText.text = PlayerName;
            playerAvatarImage.sprite = avatarSprite;
            playerAvatarImage.enabled = (avatarSprite != null);
            SetReady(isReady);
            SetKickButtonActive(canKick);
            SetAddFriendButtonActive(canAddFriend);
        }

        public void SetKickButtonActive(bool active)
        {
            kickPlayerButton.gameObject.SetActive(active);
            kickPlayerButton.interactable = active;
        }

        public void SetAddFriendButtonActive(bool active)
        {
            addFriendButton.gameObject.SetActive(active);
            addFriendButton.interactable = active;
        }

        public void SetReady(bool isReady)
        {
            readyIndicator.SetActive(isReady);
        }

        public void UpdatePlayerName(string newName)
        {
            PlayerName = newName;
            playerNameText.text = newName;
        }

        private void OnKickPlayerClicked()
        {
            OnKickClicked?.Invoke(UserId);
        }

        private void OnAddFriendPlayerClicked()
        {
            OnAddFriendClicked?.Invoke(UserId);
        }
    }
}