using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace EOS.Notification
{
    [DisallowMultipleComponent]
    public class NotificationManager : MonoBehaviour, INotification
    {
        [Header("Default Temporary UI Prefab (Optional)")]
        [SerializeField] private Notification notificationPanelPrefab;

        public static NotificationManager Instance { get; private set; }

        /// <summary>
        /// When set, NotificationManager.Show(...) delegates directly to this system.
        /// </summary>
        public static INotification ActiveHandler { get; set; }

        private Notification activeNotification;
        private Canvas _canvas;

        // remove when pluged in to new system
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
                EnsureCanvasExists();
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
            }
        }


        public static void SetHandler(INotification handler)
        {
            ActiveHandler = handler;
        }

        public static void ClearHandler()
        {
            ActiveHandler = null;
        }

        /// <summary>
        /// Shows a notification via the plugged-in notification system or the default temporary UI.
        /// An optional onResult callback is invoked with true (continue/confirm) or false (cancel/dismiss).
        /// </summary>
        public static void Show(string message, NotificationType type = NotificationType.Normal, Action<bool> onResult = null)
        {
            // the if check can be reovemed when we have a system for the handler
            if (ActiveHandler != null)
            {
                ActiveHandler.ShowNotification(message, type, onResult);
                return;
            }

            // remove when pluged in to new system
            if (Instance != null)
            {
                Instance.ShowNotification(message, type, onResult);
                return;
            }

            onResult?.Invoke(true);
        }

        // remove when pluged in to new system
        public void ShowNotification(string message, NotificationType type = NotificationType.Normal, Action<bool> onResult = null)
        {
            if (activeNotification == null)
            {
                activeNotification = Instantiate(notificationPanelPrefab, _canvas.transform, false);
            }

            activeNotification.gameObject.SetActive(true);
            activeNotification.SetNotification(message, type, onResult);
            activeNotification.transform.SetAsLastSibling();
        }

        // remove when pluged in to new system
        private void EnsureCanvasExists()
        {
            if (_canvas != null) return;

            TryGetComponent(out _canvas);
            if (_canvas == null)
            {
                _canvas = gameObject.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 999;

                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);

                gameObject.AddComponent<GraphicRaycaster>();
            }
        }
    }
}