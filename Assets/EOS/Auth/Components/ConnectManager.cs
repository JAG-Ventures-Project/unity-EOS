using System;
using EOS.Core;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Platform;

namespace EOS.Auth
{
    /// <summary>
    /// Core manager for the EOS Connect Interface.
    /// Responsible for ProductUserId session state and connecting across all external providers.
    /// </summary>
    public class ConnectManager
    {
        private static ConnectManager _instance;
        public static ConnectManager Instance => _instance ??=
            (EOSSDKManager.PlatformInterface != null
                ? new ConnectManager(EOSSDKManager.PlatformInterface)
                : null);

        public ProductUserId LocalProductUserId { get; private set; } = null;
        public string LocalProductUserIdString =>
            (LocalProductUserId != null && LocalProductUserId.IsValid()) ? LocalProductUserId.ToString() : string.Empty;
        public bool IsLoggedIn => LocalProductUserId != null && LocalProductUserId.IsValid();
        public static event Action<ProductUserId> OnLoginSuccess;

        public static void TriggerLoginSuccess(ProductUserId localUserId)
        {
            if (_instance != null)
            {
                _instance.LocalProductUserId = localUserId;
            }
            OnLoginSuccess?.Invoke(localUserId);
        }

        private readonly ConnectInterface _connectInterface;

        public ConnectManager(PlatformInterface platformInterface = null)
        {
            var platform = platformInterface ?? EOSSDKManager.PlatformInterface;
            _connectInterface = platform?.GetConnectInterface();
            EOSSDKManager.OnShutdown += HandleShutdown;
        }

        private void HandleShutdown()
        {
            EOSSDKManager.OnShutdown -= HandleShutdown;
            _instance = null;
        }

        #region Provider Login Links

        public void LoginWithSteam(bool rememberMe, Action<bool, string> onComplete)
            => SteamLoginManager.LoginWithSteam(rememberMe, onComplete);

        public void LoginWithSteam(string steamTicketHex, string displayName, Action<bool, string> onComplete)
            => SteamLoginManager.LoginWithTicket(steamTicketHex, displayName, false, onComplete);

        public void LoginWithEpic(bool rememberMe, Action<bool, string> onComplete)
            => EpicLoginManager.LoginWithEpic(rememberMe, onComplete);

        public void LoginWithEpic(string epicAuthToken, Action<bool, string> onComplete)
        => EpicLoginManager.LoginWithToken(epicAuthToken, false, onComplete);

        public void LoginAsGuest(string displayName, bool rememberMe, Action<bool, string> onComplete)
            => GuestLoginManager.LoginAsGuest(displayName, rememberMe, onComplete);

        public void CancelSteamTicket()
            => SteamLoginManager.CancelTicket();

        #endregion

        #region Core Connect Interface Execution

        /// <summary>
        /// Authenticates with the EOS Connect interface using the specified external credentials.
        /// Used by SteamLoginManager, EpicLoginManager, and GuestLoginManager.
        /// </summary>
        public void PerformConnectLogin(ExternalCredentialType credentialType, string token, string displayName, Action<bool, string> onComplete)
        {
            if (_connectInterface == null)
            {
                CompleteOperation(onComplete, false, "EOS ConnectInterface is not initialized.");
                return;
            }

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
                    Debug.Log($"[ConnectManager] {credentialType} connect login success! PUID: {LocalProductUserId}");
                    OnLoginSuccess?.Invoke(LocalProductUserId);
                    CompleteOperation(onComplete, true);
                }
                else if (loginCb.ResultCode == Result.InvalidUser)
                {
                    Debug.Log($"[ConnectManager] First time login with {credentialType}: Creating new EOS user with ContinuanceToken...");
                    CreateNewConnectUser(loginCb.ContinuanceToken, onComplete);
                }
                else
                {
                    CompleteOperation(onComplete, false, $"Connect Login failed: {loginCb.ResultCode}");
                }
            });

        }
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

        public void ResetSession()
        {
            CancelSteamTicket();
            LocalProductUserId = null;
            Debug.Log("[ConnectManager] Connect session reset.");
        }

        #endregion

        #region Operation Completion Helpers

        private void CompleteOperation(Action<bool, string> onComplete, bool success, string resultOrError = null)
        {
            if (!success && !string.IsNullOrEmpty(resultOrError))
            {
                Debug.LogError(resultOrError);
            }
            onComplete?.Invoke(success, resultOrError);
        }

        #endregion
    }
}
