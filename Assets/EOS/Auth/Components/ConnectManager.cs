using System;
using EOS.Core;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Platform;
using Steamworks;

namespace EOS.Auth
{
    public class ConnectManager
    {
        private static ConnectManager _instance;
        public static ConnectManager Instance => _instance ??=
            (EpicTransport.EOSSDKComponent.PlatformInterface != null
                ? new ConnectManager(EpicTransport.EOSSDKComponent.PlatformInterface)
                : null);

        public ProductUserId LocalProductUserId { get; private set; } = null;
        public bool IsLoggedIn => LocalProductUserId != null;
        public static event Action<ProductUserId> OnLoginSuccess;
        private readonly ConnectInterface _connectInterface;

        private Callback<GetTicketForWebApiResponse_t> _steamTicketCallback;
        private Action<bool, string> _pendingSteamLoginCallback;
        private bool _pendingSteamRememberMe;

        public ConnectManager(PlatformInterface platformInterface = null)
        {
            var platform = platformInterface ?? EpicTransport.EOSSDKComponent.PlatformInterface;
            _connectInterface = platform?.GetConnectInterface();
            EpicTransport.EOSSDKComponent.OnShutdown += () => _instance = null;
        }

        public void CancelSteamTicket()
        {
            if (EOSSDKManager.SteamAuthTicket != HAuthTicket.Invalid && SteamManager.Initialized)
            {
                SteamUser.CancelAuthTicket(EOSSDKManager.SteamAuthTicket);
                EOSSDKManager.SteamAuthTicket = HAuthTicket.Invalid;
            }
        }

        /// <summary>
        /// Requests Steam Web API ticket, retrieves PersonaName, and performs Connect login.
        /// </summary>
        public void LoginWithSteam(bool rememberMe, Action<bool, string> onComplete)
        {
            if (!SteamManager.Initialized)
            {
                CompleteOperation(onComplete, false, "Steam is not open or initialized. Please make sure Steam is running.");
                return;
            }

            _pendingSteamRememberMe = rememberMe;
            _pendingSteamLoginCallback = onComplete;

            _steamTicketCallback ??= Callback<GetTicketForWebApiResponse_t>.Create(OnSteamTicketReceived);

            HAuthTicket ticket = SteamUser.GetAuthTicketForWebApi("epiconlineservices");
            if (ticket == HAuthTicket.Invalid)
            {
                CompleteOperation(onComplete, false, "Failed to request Steam Web API ticket!");
                return;
            }

            EOSSDKManager.SteamAuthTicket = ticket;
        }

        private void OnSteamTicketReceived(GetTicketForWebApiResponse_t result)
        {
            var callback = _pendingSteamLoginCallback;
            _pendingSteamLoginCallback = null;

            if (result.m_eResult != EResult.k_EResultOK)
            {
                CancelSteamTicket();
                callback?.Invoke(false, $"Steam ticket failed: {result.m_eResult}");
                return;
            }

            byte[] ticketBytes = result.m_rgubTicket;
            string ticketHex = BitConverter.ToString(ticketBytes, 0, (int)result.m_cubTicket).Replace("-", "");
            string steamName = SteamFriends.GetPersonaName();

            LoginWithSteam(ticketHex, steamName, (success, error) =>
            {
                if (success)
                {
                    EpicTransport.EOSSDKComponent.DisplayName = steamName;
                    AuthPreferences.LastLoginProvider = _pendingSteamRememberMe ? LoginProvider.Steam : LoginProvider.None;
                    callback?.Invoke(true, null);
                }
                else
                {
                    CancelSteamTicket();
                    callback?.Invoke(false, error);
                }
            });
        }

        public void LoginWithSteam(string steamTicketHex, string displayName, Action<bool, string> onComplete)
        {
            if (string.IsNullOrEmpty(steamTicketHex))
            {
                CompleteOperation(onComplete, false, "Steam ticket is null or empty!");
                return;
            }
            PerformConnectLogin(ExternalCredentialType.SteamSessionTicket, steamTicketHex, displayName, onComplete);
        }

        public void LoginWithEpic(string epicAuthToken, Action<bool, string> onComplete)
        {
            if (string.IsNullOrEmpty(epicAuthToken))
            {
                CompleteOperation(onComplete, false, "Epic auth token is null or empty!");
                return;
            }
            PerformConnectLogin(ExternalCredentialType.Epic, epicAuthToken, null, onComplete);
        }

        // runs once per machine life time
        private void CreateNewConnectUser(ContinuanceToken continuanceToken, Action<bool, string> onComplete)
        {
            var createUserOptions = new CreateUserOptions
            {
                ContinuanceToken = continuanceToken
            };
            _connectInterface.CreateUser(createUserOptions, null, (createCb) =>
            {
                if (createCb.ResultCode == Result.Success)
                {
                    LocalProductUserId = createCb.LocalUserId;
                    EpicTransport.EOSSDKComponent.SetExternalLocalProductUserId(LocalProductUserId);
                    Debug.Log($"[ConnectManager] Created new EOS user! PUID: {LocalProductUserId}");
                    OnLoginSuccess?.Invoke(LocalProductUserId);
                    CompleteOperation(onComplete, true);
                }
                else
                {
                    CompleteOperation(onComplete, false, $"CreateUser failed: {createCb.ResultCode}");
                }
            });
        }

        /// <summary>
        /// Authenticates with the EOS Connect interface using the specified external credentials.
        /// Can be used for production credentials (Steam, Epic) or test credentials (e.g. Device ID for guest testing).
        /// </summary>
        public void PerformConnectLogin(ExternalCredentialType credentialType, string token, string displayName, Action<bool, string> onComplete)
        {
            var loginOptions = new LoginOptions
            {
                Credentials = new Credentials
                {
                    Type = credentialType,
                    Token = token
                },

                UserLoginInfo = credentialType == ExternalCredentialType.DeviceidAccessToken
                    ? new UserLoginInfo { DisplayName = displayName }
                    : null
            };

            _connectInterface.Login(loginOptions, null, (loginCb) =>
            {
                if (loginCb.ResultCode == Result.Success)
                {
                    LocalProductUserId = loginCb.LocalUserId;
                    EpicTransport.EOSSDKComponent.SetExternalLocalProductUserId(LocalProductUserId);
                    Debug.Log($"[ConnectManager] {credentialType} login success! PUID: {LocalProductUserId}");
                    OnLoginSuccess?.Invoke(LocalProductUserId);
                    CompleteOperation(onComplete, true);
                }
                else if (loginCb.ResultCode == Result.InvalidUser)
                {
                    Debug.Log($"[ConnectManager] First time login with {credentialType}: Creating new user with ContinuanceToken...");
                    CreateNewConnectUser(loginCb.ContinuanceToken, onComplete);
                }
                else
                {
                    CompleteOperation(onComplete, false, $"Connect Login failed: {loginCb.ResultCode}");
                }
            });
        }

        public void ResetSession()
        {
            CancelSteamTicket();
            LocalProductUserId = null;
            EpicTransport.EOSSDKComponent.SetExternalLocalProductUserId(null);
            Debug.Log("[ConnectManager] Connect session reset.");
        }

        #region Operation Completion Helpers

        private void CompleteOperation(Action<bool, string> onComplete, bool success, string resultOrError = null)
        {
            if (!success)
            {
                Debug.LogError(resultOrError);
            }

            onComplete?.Invoke(success, resultOrError);
        }

        #endregion
    }
}