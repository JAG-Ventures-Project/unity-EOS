using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;

namespace EOS
{
    public class Notification : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text notificationText;
        [SerializeField] private Button dismissButton;

        [Header("Type Colors")]
        [SerializeField] private Color normalColor = new Color32(0x5E, 0x5E, 0x5E, 0xFF);   // #5E5E5E
        [SerializeField] private Color warningColor = new Color32(0xF7, 0xB8, 0x01, 0xFF);  // #F7B801
        [SerializeField] private Color errorColor = new Color32(0xE5, 0x10, 0x10, 0xFF);    // #E51010

        [Header("Current State")]
        [SerializeField] private Color notificationTextColor;

        public event Action OnDismissed;

        public TMP_Text TitleText => titleText;
        public TMP_Text NotificationText => notificationText;
        public Button DismissButton => dismissButton;
        public Color CurrentTextColor => notificationTextColor;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(titleText, "[Notification] titleText is null! Please assign a TMP_Text in the inspector.");
            Assert.IsNotNull(notificationText, "[Notification] notificationText is null! Please assign a TMP_Text in the inspector.");
            Assert.IsNotNull(dismissButton, "[Notification] dismissButton is null! Please assign a Button in the inspector.");
        }
#endif

        private void Awake()
        {
            dismissButton.onClick.AddListener(Dismiss);
        }

        public void SetNotification(string message, NotificationType type = NotificationType.Normal)
        {
            notificationTextColor = GetColorForType(type);

            notificationText.text = message ?? string.Empty;
            notificationText.color = notificationTextColor;

            titleText.text = type switch
            {
                NotificationType.Warning => "Warning",
                NotificationType.Error => "Error",
                _ => "Notification"
            };
        }

        public Color GetColorForType(NotificationType type)
        {
            return type switch
            {
                NotificationType.Warning => warningColor,
                NotificationType.Error => errorColor,
                _ => normalColor
            };
        }

        public void Dismiss()
        {
            gameObject.SetActive(false);
            OnDismissed?.Invoke();
        }
    }
}