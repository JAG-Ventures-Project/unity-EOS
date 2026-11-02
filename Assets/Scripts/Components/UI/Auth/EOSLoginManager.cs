using System;
using TMPro; 
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using Steamworks;

namespace EOS
{
    public class EOSLoginManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_InputField playerName;
        [SerializeField] private Button guestLoginButton;
        [SerializeField] private Button epicGamesLoginButton;
        [SerializeField] private Button steamLoginButton;
        [SerializeField] private Toggle rememberAccountToggle;

        [Header("Navigation")]
        [SerializeField, HideInInspector] private string nextSceneName = "GameScene";

        private Callback<GetTicketForWebApiResponse_t> steamTicketCallback;

#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset nextScene;

        private void OnValidate() {
            nextSceneName = (nextScene != null) ? nextScene.name : "GameScene";
            Assert.IsNotNull(playerName, "[EOSLoginManager] playerName is null! Please assign an TMP_InputField in the inspector.");
            Assert.IsNotNull(guestLoginButton, "[EOSLoginManager] guestLoginButton is null! Please assign an Button in the inspector.");
            Assert.IsNotNull(epicGamesLoginButton, "[EOSLoginManager] epicGamesLoginButton is null! Please assign an Button in the inspector.");
            Assert.IsNotNull(rememberAccountToggle, "[EOSLoginManager] rememberAccountToggle is null! Please assign a Toggle in the inspector.");
            Assert.IsNotNull(steamLoginButton, "[EOSLoginManager] steamLoginButton is null! Please assign an Button in the inspector.");
        }
#endif
        private void Awake() {
            Assert.IsNotNull(EOSSDKManager.Instance, "[EOSLoginManager] EOSSDKManager.Instance is null! Make sure EOSSDKManager is on a GameObject in the scene and active.");
            Assert.IsNotNull(EOSSDKManager.Instance.ConnectManager, "[EOSLoginManager] ConnectManager is null! Check if EOSSDKManager initialized successfully.");
            Assert.IsNotNull(EOSSDKManager.Instance.AuthManager, "[EOSLoginManager] AuthManager is null! Check if EOSSDKManager initialized successfully.");
            
            playerName.text = AuthPreferences.LastGuestName;
            rememberAccountToggle.SetIsOnWithoutNotify(AuthPreferences.LastLoginProvider != LoginProvider.None);

            guestLoginButton.onClick.AddListener(GuestLogin);
            epicGamesLoginButton.onClick.AddListener(EpicGamesLogin);
            rememberAccountToggle.onValueChanged.AddListener(OnRememberAccountToggleValueChanged);
            steamLoginButton.onClick.AddListener(SteamLogin);
        }

        private void Start() {
            // used Start() to make the call after the eos sdk is initialized in the Awake() method
            // 1. If launched via Epic Games Launcher with -AUTH_PASSWORD, sign in automatically with ExchangeCode
            if (AuthManager.HasExchangeCode())
            {
                Debug.Log("[EOSLoginManager] Epic Games Launcher exchange code detected. Auto-logging in...");
                EpicGamesLogin();
                return;
            }
            // 2. In Unity Editor or standalone build: if player enabled Remember Me, attempt silent auto-login via PersistentAuth
            // Q: is it better to do this before adding listeners in Awake()? add it 
            switch (AuthPreferences.LastLoginProvider)
            {
                case LoginProvider.Epic when AuthPreferences.RememberEpicAccount:
                    Debug.Log("[EOSLoginManager] Auto-logging in via Epic PersistentAuth...");
                    AutoLoginWithPersistentAuth();
                    break;
                case LoginProvider.Steam when SteamManager.Initialized:
                    Debug.Log("[EOSLoginManager] Auto-logging in via Steam...");
                    SteamLogin();
                    break;
            }
        }

        private void OnEnable() {
            // guestLoginButton.interactable = true; 
            // epicGamesLoginButton.interactable = true; 
            // steamLoginButton.interactable = true; 
        }

        private void OnDisable() {
            // guestLoginButton.interactable = false; 
            // epicGamesLoginButton.interactable = false; 
            // steamLoginButton.interactable = false; 
        }

        private void GuestLogin() 
        {
            string displayName = string.IsNullOrWhiteSpace(playerName.text) 
                                ? "Guest" 
                                : playerName.text.Trim();

            SetButtonsInteractable(false);
            EOSSDKManager.Instance.ConnectManager.LoginAsGuest(displayName, (success, error) =>
            {
                if (success)
                {
                    EOSSDKManager.LocalDisplayName = displayName;
                    if (rememberAccountToggle.isOn)
                    {
                        AuthPreferences.LastLoginProvider = LoginProvider.Guest;
                        AuthPreferences.LastGuestName = playerName.text;
                    }
                    else
                    {
                        AuthPreferences.LastLoginProvider = LoginProvider.None;
                    }

                    SceneManager.LoadScene(nextSceneName);
                }
                else
                {
                    Debug.LogError($"[EOSLoginManager] Guest Login Failed: {error}");
                    NotificationManager.Show($"Guest Login Failed: {error}", NotificationType.Error);
                    SetButtonsInteractable(true);
                }
            });
        }

        // attempt auto login with persistent auth , if it's not successful stay on the login page
        private void AutoLoginWithPersistentAuth()
        {
            SetButtonsInteractable(false);

            EOSSDKManager.Instance.AuthManager.LoginWithPersistentAuth(fallbackToAccountPortal: false, (authSuccess, authError) =>
            {
                if (!authSuccess)
                {
                    Debug.LogWarning($"[EOSLoginManager] Silent PersistentAuth auto-login failed: {authError}. Ready for manual login.");
                    SetButtonsInteractable(true);
                    return;
                }

                CompleteEpicConnectLogin();
            });
        }

        private void EpicGamesLogin() 
        {
            SetButtonsInteractable(false);

            EOSSDKManager.Instance.AuthManager.IsRememberMe = rememberAccountToggle.isOn;

            EOSSDKManager.Instance.AuthManager.Login(fallbackToAccountPortal: true , (authSuccess, authError) =>
            {
                if (!authSuccess)
                {
                    Debug.LogError($"[EOSLoginManager] Epic Games Auth Login Failed: {authError}");
                    NotificationManager.Show($"Epic Games Auth Login Failed: {authError}", NotificationType.Error);
                    SetButtonsInteractable(true);
                    return;
                }
                
                AuthPreferences.LastLoginProvider = rememberAccountToggle.isOn 
                                                    ? LoginProvider.Epic 
                                                    : LoginProvider.None;
                CompleteEpicConnectLogin();
            });
        }

        private void CompleteEpicConnectLogin()
        {
            string userAuthToken = EOSSDKManager.Instance.AuthManager.CopyUserAuthToken();
            if (string.IsNullOrEmpty(userAuthToken))
            {
                Debug.LogError("[EOSLoginManager] Failed to copy Epic user auth token!");
                NotificationManager.Show("Failed to copy Epic user auth token.", NotificationType.Error);
                SetButtonsInteractable(true);
                return;
            }

            EOSSDKManager.Instance.ConnectManager.LoginWithEpic(userAuthToken, (connectSuccess, connectError) =>
            {
                if (connectSuccess)
                {
                    EOSSDKManager.Instance.AuthManager.GetDisplayName((epicDisplayName) =>
                    {
                        EOSSDKManager.LocalDisplayName = epicDisplayName;
                        SceneManager.LoadScene(nextSceneName);
                    });
                }
                else
                {
                    Debug.LogError($"[EOSLoginManager] Epic Games Connect Login Failed: {connectError}");
                    NotificationManager.Show($"Epic Games Connect Login Failed: {connectError}", NotificationType.Error);
                    SetButtonsInteractable(true);
                }
            });
        }

        private void OnRememberAccountToggleValueChanged(bool value) 
        {
            EOSSDKManager.Instance.AuthManager.IsRememberMe = value;
        }

        private void SteamLogin()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("Steam is not initialized!");
                NotificationManager.Show("Steam is not open or initialized. Please make sure Steam is running.", NotificationType.Warning);
                return;
            }

            SetButtonsInteractable(false);

            // Q: does this need cleaning up in the OnDestroy method?
            steamTicketCallback ??=
                Callback<GetTicketForWebApiResponse_t>.Create(OnSteamTicketReceived);

            HAuthTicket ticket =
                SteamUser.GetAuthTicketForWebApi("epiconlineservices");

            if (ticket == HAuthTicket.Invalid)
            {
                Debug.LogError("Failed to request Steam Web API ticket!");
                NotificationManager.Show("Failed to request Steam Web API ticket.", NotificationType.Error);
                SetButtonsInteractable(true);
                return;
            }

            EOSSDKManager.SteamAuthTicket = ticket;
        }

        private void OnSteamTicketReceived(GetTicketForWebApiResponse_t result)
        {
            if (result.m_eResult != EResult.k_EResultOK)
            {
                Debug.LogError($"Steam ticket failed: {result.m_eResult}");
                NotificationManager.Show($"Steam ticket failed: {result.m_eResult}", NotificationType.Error);

                if (EOSSDKManager.SteamAuthTicket != HAuthTicket.Invalid)
                {
                    SteamUser.CancelAuthTicket(EOSSDKManager.SteamAuthTicket);
                    EOSSDKManager.SteamAuthTicket = HAuthTicket.Invalid;
                }

                SetButtonsInteractable(true);
                return;
            }

            byte[] ticketBytes = result.m_rgubTicket;

            string ticketHex = BitConverter
                .ToString(ticketBytes, 0, (int)result.m_cubTicket)
                .Replace("-", "");

            string steamName = SteamFriends.GetPersonaName();

            EOSSDKManager.Instance.ConnectManager.LoginWithSteam(
                ticketHex,
                steamName,
                (success, error) =>
                {
                    if (success)
                    {
                        EOSSDKManager.LocalDisplayName = steamName;
                        AuthPreferences.LastLoginProvider = rememberAccountToggle.isOn 
                                                            ? LoginProvider.Steam 
                                                            : LoginProvider.None;
                        SceneManager.LoadScene(nextSceneName);
                    }
                    else
                    {
                        Debug.LogError($"EOS Steam Login Failed: {error}");
                        NotificationManager.Show($"Steam Login Failed: {error}", NotificationType.Error);

                        SteamUser.CancelAuthTicket(
                            EOSSDKManager.SteamAuthTicket
                        );

                        EOSSDKManager.SteamAuthTicket = HAuthTicket.Invalid;

                        SetButtonsInteractable(true);
                    }
                }
            );
        }

        private void SetButtonsInteractable(bool state)
        {
            playerName.interactable = state;
            guestLoginButton.interactable = state;
            epicGamesLoginButton.interactable = state;
            steamLoginButton.interactable = state;
            rememberAccountToggle.interactable = state;
        }
    }
}
