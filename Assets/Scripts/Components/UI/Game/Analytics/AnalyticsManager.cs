using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace EOS.UI.Analytics
{
    /// <summary>
    /// Central manager for the Analytics UI.
    /// Handles the activation of the Analytics menu via input (e.g. Tab key)
    /// and coordinates the activation of underlying analytics modules (e.g. Ping).
    /// </summary>
    [DisallowMultipleComponent]
    public class AnalyticsManager : MonoBehaviour
    {
        public static AnalyticsManager Instance { get; private set; }

        [Header("Menu Panels")]
        [SerializeField] private GameObject analyticsMenuPanel;

        [Header("Input Configuration")]
        [SerializeField] private InputActionReference tabAction;
        [SerializeField] private bool holdToOpen = true;

        [Header("Analytics Modules")]
        [SerializeField] private PingAnalyticsUI pingAnalyticsUI;
        [SerializeField] private NetworkDiagnosticsUI networkDiagnosticsUI;

        public event Action<bool> OnAnalyticsToggled;

        public bool IsMenuOpen { get; private set; }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(analyticsMenuPanel, "[AnalyticsManager] analyticsMenuPanel is null! Please assign the panel GameObject in inspector.");
            Assert.IsNotNull(tabAction, "[AnalyticsManager] tabAction is null! Please assign the input action in inspector.");
        }
#endif

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            analyticsMenuPanel.SetActive(false);
        }

        private void OnEnable()
        {
            tabAction.action.started += OnTabStarted;
            tabAction.action.performed += OnTabPerformed;
            if (holdToOpen)
            {
                tabAction.action.canceled += OnTabCanceled;
            }
            tabAction.action.Enable();
        }

        private void OnDisable()
        {
            tabAction.action.started -= OnTabStarted;
            tabAction.action.performed -= OnTabPerformed;
            if (holdToOpen)
            {
                tabAction.action.canceled -= OnTabCanceled;
            }
            tabAction.action.Disable();

            if (IsMenuOpen)
            {
                SetMenuOpen(false);
            }
        }

        private void OnTabStarted(InputAction.CallbackContext ctx)
        {
            if (holdToOpen)
            {
                SetMenuOpen(true);
            }
        }

        private void OnTabPerformed(InputAction.CallbackContext ctx)
        {
            if (holdToOpen)
            {
                SetMenuOpen(true);
            }
            else
            {
                SetMenuOpen(!IsMenuOpen);
            }
        }

        private void OnTabCanceled(InputAction.CallbackContext ctx)
        {
            if (holdToOpen)
            {
                SetMenuOpen(false);
            }
        }

        private void Update()
        {
            if (holdToOpen && IsMenuOpen)
            {
                // Verify whether the tab key is still physically pressed
                bool isActionPressed = tabAction != null && tabAction.action != null && tabAction.action.IsPressed();
                bool isKeyPhysicallyPressed = Keyboard.current != null && Keyboard.current.tabKey.isPressed;

                if (!isActionPressed && !isKeyPhysicallyPressed)
                {
                    SetMenuOpen(false);
                }
            }
        }

        public void SetMenuOpen(bool open)
        {
            if (IsMenuOpen == open) return;
            IsMenuOpen = open;

            analyticsMenuPanel.SetActive(open);
            pingAnalyticsUI.SetMenuOpen(open);
            networkDiagnosticsUI.SetMenuOpen(open);
            OnAnalyticsToggled?.Invoke(open);
        }
    }
}