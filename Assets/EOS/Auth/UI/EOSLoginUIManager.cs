using TMPro;
using EOS.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using EOS.Notification;

namespace EOS.Auth
{
    public class EOSLoginManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button epicGamesLoginButton;
        [SerializeField] private Button steamLoginButton;
        [SerializeField] private Toggle rememberAccountToggle;

        [Header("Optional Guest Login")]
        [SerializeField] private Button guestLoginButton;
        [SerializeField] private TMP_InputField playerName;

        [Header("Navigation")]
        [SerializeField, HideInInspector] private string nextSceneName = "GameScene";

#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset nextScene;

        private void OnValidate()
        {
            nextSceneName = (nextScene != null) ? nextScene.name : "GameScene";
        }
#endif

        private void Awake()
        {
            Assert.IsNotNull(EOSSDKManager.Instance, "[EOSLoginManager] EOSSDKManager.Instance is null! Make sure EOSSDKManager is on a GameObject in the scene and active.");
            Assert.IsNotNull(ConnectManager.Instance, "[EOSLoginManager] ConnectManager is null! Check if EOSSDKManager initialized successfully.");
            Assert.IsNotNull(AuthManager.Instance, "[EOSLoginManager] AuthManager is null! Check if EOSSDKManager initialized successfully.");

            if (playerName != null)
            {
                playerName.text = GuestLoginManager.LastGuestName;
            }

            if (rememberAccountToggle != null)
            {
                rememberAccountToggle.SetIsOnWithoutNotify(AuthPreferences.LastLoginProvider != LoginProvider.None);
                rememberAccountToggle.onValueChanged.AddListener(OnRememberAccountToggleValueChanged);
            }

            epicGamesLoginButton?.onClick.AddListener(EpicGamesLogin);
            steamLoginButton?.onClick.AddListener(SteamLogin);
            guestLoginButton?.onClick.AddListener(GuestLogin);
        }

        private void Start()
        {
            SetButtonsInteractable(false);

            AuthManager.Instance.TryAutoLogin((success, error) =>
            {
                if (success)
                {
                    SceneManager.LoadScene(nextSceneName);
                }
                else
                {
                    SetButtonsInteractable(true);
                }
            });
        }

        private void GuestLogin()
        {
            SetButtonsInteractable(false);
            string cleanName = playerName != null ? playerName.text : "Guest";
            GuestLoginManager.LoginAsGuest(cleanName, rememberAccountToggle != null && rememberAccountToggle.isOn, HandleLoginResult);
        }

        private void EpicGamesLogin()
        {
            SetButtonsInteractable(false);
            EpicLoginManager.LoginWithEpic(rememberAccountToggle != null && rememberAccountToggle.isOn, HandleLoginResult);
        }

        private void SteamLogin()
        {
            SetButtonsInteractable(false);
            SteamLoginManager.LoginWithSteam(rememberAccountToggle != null && rememberAccountToggle.isOn, HandleLoginResult);
        }

        private void HandleLoginResult(bool success, string error)
        {
            if (success)
            {
                SceneManager.LoadScene(nextSceneName);
            }
            else
            {
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError($"[EOSLoginManager] Login Failed: {error}");
                    NotificationManager.Show(error, NotificationType.Error);
                }

                SetButtonsInteractable(true);
            }
        }

        private void OnRememberAccountToggleValueChanged(bool value)
        {
            AuthManager.Instance.IsRememberMe = value;
        }

        private void SetButtonsInteractable(bool state)
        {
            if (playerName != null) playerName.interactable = state;
            if (guestLoginButton != null) guestLoginButton.interactable = state;
            if (epicGamesLoginButton != null) epicGamesLoginButton.interactable = state;
            if (steamLoginButton != null) steamLoginButton.interactable = state;
            if (rememberAccountToggle != null) rememberAccountToggle.interactable = state;
        }
    }
}