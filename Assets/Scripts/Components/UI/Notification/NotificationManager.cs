using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;

namespace EOS
{

    [DisallowMultipleComponent]
    public class NotificationManager : MonoBehaviour, INotification
    {
        [SerializeField] private Notification notificationPanelPrefab;

        public static NotificationManager Instance { get; private set; }

        private Notification activeNotification;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(notificationPanelPrefab, "[NotificationManager] notificationPanelPrefab is null! Please assign a GameObject in the inspector.");
        }
#endif

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
            }
        }

        public void ShowNotification(string message, NotificationType type = NotificationType.Normal)
        {
            if (activeNotification == null)
            {
                // Q: how to know which canvas to go for? or do we create new canvs only for this user.
                Canvas canvas = FindFirstObjectByType<Canvas>();
                activeNotification = Instantiate(notificationPanelPrefab, canvas.transform, false);
            }

            activeNotification.gameObject.SetActive(true);
            activeNotification.SetNotification(message, type);
            activeNotification.transform.SetAsLastSibling();
        }
    }
}