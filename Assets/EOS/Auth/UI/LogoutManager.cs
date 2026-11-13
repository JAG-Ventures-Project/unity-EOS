using System;
using EOS.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;

namespace EOS.Auth
{
    public class LogoutManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button logoutButton;

        [Header("Navigation")]
        [SerializeField, HideInInspector] private string nextSceneName = "LoginScene";

        public static event Action OnLogoutInitiated;

#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset nextScene;

        private void OnValidate()
        {
            nextSceneName = (nextScene != null) ? nextScene.name : "LoginScene";
            Assert.IsNotNull(logoutButton, "[LogoutManager] logoutButton is null! Please assign a Button in the inspector.");
        }
#endif

        private void Awake()
        {
            Assert.IsNotNull(EOSSDKManager.Instance, "[LogoutManager] EOSSDKManager.Instance is null! Make sure EOSSDKManager is on a GameObject in the scene and active.");
            Assert.IsNotNull(ConnectManager.Instance, "[LogoutManager] ConnectManager is null! Check if EOSSDKManager initialized successfully.");
            Assert.IsNotNull(AuthManager.Instance, "[LogoutManager] AuthManager is null! Check if EOSSDKManager initialized successfully.");

            logoutButton.onClick.AddListener(Logout);
        }

        private void OnDestroy()
        {
            if (logoutButton != null)
            {
                logoutButton.onClick.RemoveListener(Logout);
            }
        }

        private void Logout()
        {
            SetButtonsInteractable(false);
            OnLogoutInitiated?.Invoke();

            SessionAuthCoordinator.LogoutAll(HandleLogoutCallback);
        }

        private void HandleLogoutCallback(bool success, string error)
        {
            SceneManager.LoadScene(nextSceneName);
        }

        private void SetButtonsInteractable(bool interactable)
        {
            logoutButton.interactable = interactable;
        }
    }
}