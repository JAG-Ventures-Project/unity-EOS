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

        [Header("Navigation")]
        [SerializeField] private string nextSceneName = "GameScene";

        private Callback<GetTicketForWebApiResponse_t> steamTicketCallback;

#if UNITY_EDITOR
        private void OnValidate() {
            Assert.IsNotNull(playerName, "[EOSLoginManager] playerName is null! Please assign an TMP_InputField in the inspector.");
            Assert.IsNotNull(guestLoginButton, "[EOSLoginManager] guestLoginButton is null! Please assign an Button in the inspector.");
            Assert.IsNotNull(epicGamesLoginButton, "[EOSLoginManager] epicGamesLoginButton is null! Please assign an Button in the inspector.");
            Assert.IsNotNull(steamLoginButton, "[EOSLoginManager] steamLoginButton is null! Please assign an Button in the inspector.");
        }
#endif
        private void Awake() {
            Assert.IsNotNull(EOSSDKManager.Instance, "[EOSLoginManager] EOSSDKManager.Instance is null! Make sure EOSSDKManager is on a GameObject in the scene and active.");
            Assert.IsNotNull(EOSSDKManager.Instance.ConnectManager, "[EOSLoginManager] ConnectManager is null! Check if EOSSDKManager initialized successfully.");
            Assert.IsNotNull(EOSSDKManager.Instance.AuthManager, "[EOSLoginManager] AuthManager is null! Check if EOSSDKManager initialized successfully.");

            guestLoginButton.onClick.AddListener(GuestLogin);
            epicGamesLoginButton.onClick.AddListener(EpicGamesLogin);
            steamLoginButton.onClick.AddListener(SteamLogin);
        }

        private void Start() {
            // If launched via Epic Games Launcher with -AUTH_PASSWORD, sign in automatically
            // used Start() to make the call after the eos sdk is initialized in the Awake() method
            if (AuthManager.HasExchangeCode())
            {
                Debug.Log("[EOSLoginManager] Epic Games Launcher exchange code detected. Auto-logging in...");
                EpicGamesLogin();
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

        private void OnDestroy() {
            guestLoginButton.onClick.RemoveListener(GuestLogin);   
            epicGamesLoginButton.onClick.RemoveListener(EpicGamesLogin);
            steamLoginButton.onClick.RemoveListener(SteamLogin);
        }

        private void GuestLogin() 
        {
            SetButtonsInteractable(false);
            EOSSDKManager.Instance.ConnectManager.LoginAsGuest(playerName.text ?? "Guest", (success, error) =>
            {
                if (success)
                {
                    Debug.Log($"<color=green>[EOSLoginManager] Guest Login Successful! Loading {nextSceneName}...</color>");
                    SceneManager.LoadScene(nextSceneName);
                }
                else
                {
                    Debug.LogError($"[EOSLoginManager] Guest Login Failed: {error}");
                    SetButtonsInteractable(true);
                }
            });
        }

        private void EpicGamesLogin() 
        {
            SetButtonsInteractable(false);

            EOSSDKManager.Instance.AuthManager.Login((authSuccess, authError) =>
            {
                if (!authSuccess)
                {
                    Debug.LogError($"[EOSLoginManager] Epic Games Auth Login Failed: {authError}");
                    SetButtonsInteractable(true);
                    return;
                }

                string userAuthToken = EOSSDKManager.Instance.AuthManager.CopyUserAuthToken();
                if (string.IsNullOrEmpty(userAuthToken))
                {
                    Debug.LogError("[EOSLoginManager] Failed to copy Epic user auth token!");
                    SetButtonsInteractable(true);
                    return;
                }

                EOSSDKManager.Instance.ConnectManager.LoginWithEpic(userAuthToken, (connectSuccess, connectError) =>
                {
                    if (connectSuccess)
                    {
                        Debug.Log($"<color=green>[EOSLoginManager] Epic Games Login Success! Loading {nextSceneName}...</color>");
                        SceneManager.LoadScene(nextSceneName);
                    }
                    else
                    {
                        Debug.LogError($"[EOSLoginManager] Epic Games Connect Login Failed: {connectError}");
                        SetButtonsInteractable(true);
                    }
                });
            });
        }

        private void SteamLogin()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("Steam is not initialized!");
                return;
            }

            SetButtonsInteractable(false);

            steamTicketCallback ??=
                Callback<GetTicketForWebApiResponse_t>.Create(OnSteamTicketReceived);

            HAuthTicket ticket =
                SteamUser.GetAuthTicketForWebApi("epiconlineservices");

            if (ticket == HAuthTicket.Invalid)
            {
                Debug.LogError("Failed to request Steam Web API ticket!");
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
                        Debug.Log("Steam EOS login successful!");
                        SceneManager.LoadScene(nextSceneName);
                    }
                    else
                    {
                        Debug.LogError($"EOS Steam Login Failed: {error}");

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
            if (guestLoginButton != null) guestLoginButton.interactable = state;
            if (epicGamesLoginButton != null) epicGamesLoginButton.interactable = state;
            if (steamLoginButton != null) steamLoginButton.interactable = state;
        }
    }
}
