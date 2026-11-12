using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
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
            Assert.IsNotNull(playerNameText, "[LobbyPlayerCardUI] PlayerNameText must not be null.");
            Assert.IsNotNull(kickPlayerButton, "[LobbyPlayerCardUI] kickPlayerButton must not be null.");
            Assert.IsNotNull(addFriendButton, "[LobbyPlayerCardUI] addFriendButton must not be null.");
            Assert.IsNotNull(playerAvatarImage, "[LobbyPlayerCardUI] PlayerAvatarImage must not be null.");
            Assert.IsNotNull(readyIndicator, "[LobbyPlayerCardUI] readyIndicator must not be null.");
        }
#endif

        public void SetData(string playerName, Sprite avatarSprite, bool isReady = false, bool canKick = false, bool canAddFriend = false)
        {
            SetData(null, playerName, avatarSprite, isReady, canKick, canAddFriend);
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
            kickPlayerButton.onClick.AddListener(OnKickPlayerClicked);
            addFriendButton.onClick.AddListener(OnAddFriendPlayerClicked);
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

        public void UpdateAvatar(Sprite newAvatar)
        {
            AvatarSprite = newAvatar;
            playerAvatarImage.sprite = newAvatar;
            playerAvatarImage.enabled = (newAvatar != null);
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