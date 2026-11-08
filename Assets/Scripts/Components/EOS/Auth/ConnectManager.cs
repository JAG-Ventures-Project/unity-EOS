using System;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Platform;

namespace EOS
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
        private readonly ConnectInterface _connectInterface;

        public ConnectManager(PlatformInterface platformInterface = null)
        {
            var platform = platformInterface ?? EpicTransport.EOSSDKComponent.PlatformInterface;
            _connectInterface = platform?.GetConnectInterface();
            EpicTransport.EOSSDKComponent.OnShutdown += () => _instance = null;
        }

        /// <summary>
        /// Logs in as a Guest using the local machine's Device ID.
        /// </summary>
        public void LoginAsGuest(string displayName, Action<bool, string> onComplete)
        {
            var createDeviceIdOptions = new CreateDeviceIdOptions
            {
                DeviceModel = SystemInfo.deviceModel ?? "PC Windows"
            };

            _connectInterface.CreateDeviceId(createDeviceIdOptions, null, (createCb) =>
            {
                // make sure the device has an ID and it's not a duplicate , we call this becasue the network call can fail
                if (createCb.ResultCode != Result.Success && createCb.ResultCode != Result.DuplicateNotAllowed)
                {
                    CompleteOperation(onComplete, false, $"CreateDeviceId failed: {createCb.ResultCode}");
                    return;
                }
                PerformConnectLogin(ExternalCredentialType.DeviceidAccessToken, null, displayName, onComplete);
            });
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
                    CompleteOperation(onComplete, true);
                }
                else
                {
                    CompleteOperation(onComplete, false, $"CreateUser failed: {createCb.ResultCode}");
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

        private void PerformConnectLogin(ExternalCredentialType credentialType, string token, string displayName, Action<bool, string> onComplete)
        {
            var loginOptions = new LoginOptions
            {
                Credentials = new Credentials
                {
                    Type = credentialType,
                    Token = token // null if device id , hex string for steam
                },
                
                UserLoginInfo = credentialType == ExternalCredentialType.DeviceidAccessToken
                    ? new UserLoginInfo { DisplayName = displayName }
                    : null
            };

            _connectInterface.Login(loginOptions, null, (loginCb) =>
            {
                // already has an account
                if (loginCb.ResultCode == Result.Success)
                {
                    LocalProductUserId = loginCb.LocalUserId;
                    EpicTransport.EOSSDKComponent.SetExternalLocalProductUserId(LocalProductUserId);
                    Debug.Log($"[ConnectManager] {credentialType} login success! PUID: {LocalProductUserId}");
                    CompleteOperation(onComplete, true);
                }
                // first time on this pc (creats new account)
                else if (loginCb.ResultCode == Result.InvalidUser)
                {
                    Debug.Log($"[ConnectManager] First time login with {credentialType}: Creating new user with ContinuanceToken...");
                    CreateNewConnectUser(loginCb.ContinuanceToken, onComplete);
                }
                // other error (e.g. Network Disconnected)
                else
                {
                    CompleteOperation(onComplete, false, $"Connect Login failed: {loginCb.ResultCode}");
                }
            });
        }

        public void ResetSession()
        {
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
