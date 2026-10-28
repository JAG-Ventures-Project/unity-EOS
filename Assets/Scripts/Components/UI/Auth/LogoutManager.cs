using System;
using TMPro; 
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using Steamworks;

namespace EOS
{
    public class LogoutManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button logoutButton;

        [Header("Navigation")]
        [SerializeField, HideInInspector] private string nextSceneName = "LoginScene";

#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset nextScene;
        
        private void OnValidate() {
            nextSceneName = (nextScene != null) ? nextScene.name : "LoginScene";
            Assert.IsNotNull(logoutButton, "[LogoutManager] logoutButton is null! Please assign a Button in the inspector.");
        }
#endif
        private void Awake() {
            Assert.IsNotNull(EOSSDKManager.Instance, "[LogoutManager] EOSSDKManager.Instance is null! Make sure EOSSDKManager is on a GameObject in the scene and active.");
            Assert.IsNotNull(EOSSDKManager.Instance.ConnectManager, "[LogoutManager] ConnectManager is null! Check if EOSSDKManager initialized successfully.");
            Assert.IsNotNull(EOSSDKManager.Instance.AuthManager, "[LogoutManager] AuthManager is null! Check if EOSSDKManager initialized successfully.");
            
            logoutButton.onClick.AddListener(Logout);
        }

        private void Logout() {
            SetButtonsInteractable(false);

            if (EOSSDKManager.Instance.AuthManager.IsLoggedIn)
            {
                EOSSDKManager.Instance.AuthManager.Logout(true, (success, error) =>
                {
                    if (!success)
                    {
                        Debug.LogWarning($"[LogoutManager] Epic Logout warning: {error}");
                        NotificationManager.Instance?.ShowNotification($"Logout warning: {error}", NotificationType.Warning);
                        SetButtonsInteractable(true);
                    }
                    FinalizeLogout();
                });
            }
            else
            {
                if (EOSSDKManager.SteamAuthTicket != HAuthTicket.Invalid && SteamManager.Initialized)
                {
                    SteamUser.CancelAuthTicket(EOSSDKManager.SteamAuthTicket);
                    EOSSDKManager.SteamAuthTicket = HAuthTicket.Invalid;
                    Debug.Log("[LogoutManager] Steam auth ticket cancelled.");
                }
                FinalizeLogout();
            }
        }

        private void FinalizeLogout()
        {
            EOSSDKManager.Instance.ConnectManager.ResetSession();
            AuthPreferences.ClearSession();
            SceneManager.LoadScene(nextSceneName);
        }


        private void SetButtonsInteractable(bool interactable)
        {
            logoutButton.interactable = interactable;
        }
    }
}