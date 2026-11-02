using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
using Epic.OnlineServices;

namespace EOS
{
    [DisallowMultipleComponent]
    public class LobbyPlayerCardUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private Image playerAvatarImage;
        [SerializeField] private GameObject readyIndicator;
        [SerializeField] private Button kickPlayerButton;

        public ProductUserId UserId { get; private set; }
        public string PlayerName { get; private set; }
        public Sprite AvatarSprite { get; private set; }
        public event Action<ProductUserId> OnKickClicked;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(playerNameText, "[LobbyPlayerCardUI] PlayerNameText must not be null.");
            Assert.IsNotNull(playerAvatarImage, "[LobbyPlayerCardUI] PlayerAvatarImage must not be null.");
            Assert.IsNotNull(readyIndicator, "[LobbyPlayerCardUI] readyIndicator must not be null.");
            Assert.IsNotNull(kickPlayerButton, "[LobbyPlayerCardUI] kickPlayerButton must not be null.");
        }
#endif

        public void SetData(string playerName, Sprite avatarSprite, bool isReady = false, bool canKick = false)
        {
            SetData(null, playerName, avatarSprite, isReady, canKick);
        }

        public void SetData(ProductUserId userId, string playerName, Sprite avatarSprite, bool isReady = false, bool canKick = false)
        {
            UserId = userId;
            PlayerName = !string.IsNullOrWhiteSpace(playerName) ? playerName : "Player";
            AvatarSprite = avatarSprite;

            playerNameText.text = PlayerName;
            playerAvatarImage.sprite = avatarSprite;
            playerAvatarImage.enabled = (avatarSprite != null);
            SetReady(isReady);
            kickPlayerButton.onClick.AddListener(OnKickPlayerClicked);
            SetKickButtonActive(canKick);
        }

        public void SetKickButtonActive(bool active)
        {
            kickPlayerButton.gameObject.SetActive(active);
            kickPlayerButton.interactable = active;
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
    }
}
