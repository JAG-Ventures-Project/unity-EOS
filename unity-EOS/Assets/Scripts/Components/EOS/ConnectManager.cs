using System;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Platform;

namespace EOS
{
    public class ConnectManager
    {
        public ProductUserId LocalProductUserId { get; private set; } = null;
        public bool IsLoggedIn => LocalProductUserId != null;
        private readonly ConnectInterface _connectInterface;

        public ConnectManager(PlatformInterface platformInterface)
        {
            _connectInterface = platformInterface.GetConnectInterface();
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
                    onComplete?.Invoke(false, $"CreateDeviceId failed: {createCb.ResultCode}");
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
                    Debug.Log($"[ConnectManager] Created new EOS user! PUID: {LocalProductUserId}");
                    onComplete?.Invoke(true, null);
                }
                else
                {
                    onComplete?.Invoke(false, $"CreateUser failed: {createCb.ResultCode}");
                }
            });
        }

        public void LoginWithSteam(string steamTicketHex, string displayName, Action<bool, string> onComplete)
        {
            if (string.IsNullOrEmpty(steamTicketHex))
            {
                onComplete?.Invoke(false, "Steam ticket is null or empty!");
                return;
            }
            PerformConnectLogin(ExternalCredentialType.SteamSessionTicket, steamTicketHex, displayName, onComplete);
        }

        public void LoginWithEpic(string epicAuthToken, Action<bool, string> onComplete)
        {
            if (string.IsNullOrEmpty(epicAuthToken))
            {
                onComplete?.Invoke(false, "Epic auth token is null or empty!");
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
                    Debug.Log($"[ConnectManager] {credentialType} login success! PUID: {LocalProductUserId}");
                    onComplete?.Invoke(true, null);
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
                    onComplete?.Invoke(false, $"Connect Login failed: {loginCb.ResultCode}");
                }
            });
        }
        public void ResetSession()
        {
            LocalProductUserId = null;
            Debug.Log("[ConnectManager] Connect session reset.");
        }
    }
}