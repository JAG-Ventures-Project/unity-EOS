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
            if(!SteamManager.Initialized)
            {
                Debug.LogError("[EOSLoginManager] Steam is not running! Please make sure Steam is running and try again.");
                return;
            }

            SetButtonsInteractable(false);
            
            byte[] ticket = new byte[1024];
            var identity = new SteamNetworkingIdentity();
            
            HAuthTicket authTicket = SteamUser.GetAuthSessionTicket(ticket, ticket.Length, out uint ticketSize, ref identity);
            if (authTicket == HAuthTicket.Invalid || ticketSize == 0)
            {
                Debug.LogError("[EOSLoginManager] Failed to get Steam session ticket!");
                SetButtonsInteractable(true);
                return;
            }
            
            // Save ticket in the persistent manager so it survives scene load
            EOSSDKManager.SteamAuthTicket = authTicket;
            
            string ticketHex = BitConverter.ToString(ticket, 0, (int)ticketSize).Replace("-", "");
            
            string steamName = SteamFriends.GetPersonaName();
            
            // from https://dev.epicgames.com/docs/api-ref/structs/eos-auth-credentials
            EOSSDKManager.Instance.ConnectManager.LoginWithSteam(ticketHex, steamName, (success, error) =>
            {
                if (success)
                {
                    Debug.Log($"<color=green>[EOSLoginManager] Steam Login Success ({steamName})! Loading {nextSceneName}...</color>");
                    SceneManager.LoadScene(nextSceneName);
                }
                else
                {
                    Debug.LogError($"[EOSLoginManager] Steam Login Failed: {error}");
                    // If login failed, cancel the ticket right away
                    SteamUser.CancelAuthTicket(authTicket);
                    EOSSDKManager.SteamAuthTicket = HAuthTicket.Invalid;
                    SetButtonsInteractable(true);
                }
            });
        }

        private void SetButtonsInteractable(bool state)
        {
            if (guestLoginButton != null) guestLoginButton.interactable = state;
            if (epicGamesLoginButton != null) epicGamesLoginButton.interactable = state;
            if (steamLoginButton != null) steamLoginButton.interactable = state;
        }

    }
}
