using System;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;

namespace EOS.Auth
{
    /// <summary>
    /// Standalone Device-ID-based Guest Login for development and test workflows.
    /// Operates directly on the EOS ConnectInterface with zero dependency on ConnectManager.
    /// </summary>
    public static class GuestLoginManager
    {
        private const string KeyLastGuestName = "EOS_LastGuestName";

        public static string LastGuestName
        {
            get => PlayerPrefs.GetString(KeyLastGuestName, string.Empty);
            set
            {
                PlayerPrefs.SetString(KeyLastGuestName, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Logs in as a Guest using the local machine's Device ID directly via EOS ConnectInterface.
        /// </summary>
        public static void LoginAsGuest(string displayName, bool rememberMe, Action<bool, string> onComplete)
        {
            string cleanName = string.IsNullOrWhiteSpace(displayName) ? "Guest" : displayName.Trim();

            var platform = EpicTransport.EOSSDKComponent.PlatformInterface;
            var connectInterface = platform?.GetConnectInterface();
            if (connectInterface == null)
            {
                onComplete?.Invoke(false, "EOS ConnectInterface is not initialized.");
                return;
            }

            var createDeviceIdOptions = new CreateDeviceIdOptions
            {
                DeviceModel = SystemInfo.deviceModel ?? "PC Windows"
            };

            connectInterface.CreateDeviceId(createDeviceIdOptions, null, (createCb) =>
            {
                if (createCb.ResultCode != Result.Success && createCb.ResultCode != Result.DuplicateNotAllowed)
                {
                    onComplete?.Invoke(false, $"CreateDeviceId failed: {createCb.ResultCode}");
                    return;
                }

                PerformDeviceLogin(connectInterface, cleanName, rememberMe, onComplete);
            });
        }

        private static void PerformDeviceLogin(ConnectInterface connectInterface, string displayName, bool rememberMe, Action<bool, string> onComplete)
        {
            var loginOptions = new LoginOptions
            {
                Credentials = new Credentials
                {
                    Type = ExternalCredentialType.DeviceidAccessToken,
                    Token = null
                },
                UserLoginInfo = new UserLoginInfo { DisplayName = displayName }
            };

            connectInterface.Login(loginOptions, null, (loginCb) =>
            {
                if (loginCb.ResultCode == Result.Success)
                {
                    HandleLoginSuccess(loginCb.LocalUserId, displayName, rememberMe, onComplete);
                }
                else if (loginCb.ResultCode == Result.InvalidUser)
                {
                    Debug.Log("[GuestLoginManager] First time guest login: creating EOS user...");
                    var createUserOptions = new CreateUserOptions { ContinuanceToken = loginCb.ContinuanceToken };
                    connectInterface.CreateUser(createUserOptions, null, (createCb) =>
                    {
                        if (createCb.ResultCode == Result.Success)
                        {
                            HandleLoginSuccess(createCb.LocalUserId, displayName, rememberMe, onComplete);
                        }
                        else
                        {
                            onComplete?.Invoke(false, $"CreateUser failed: {createCb.ResultCode}");
                        }
                    });
                }
                else
                {
                    onComplete?.Invoke(false, $"Connect Login failed: {loginCb.ResultCode}");
                }
            });
        }

        private static void HandleLoginSuccess(ProductUserId localUserId, string displayName, bool rememberMe, Action<bool, string> onComplete)
        {
            EpicTransport.EOSSDKComponent.SetExternalLocalProductUserId(localUserId);
            EpicTransport.EOSSDKComponent.DisplayName = displayName;

            if (rememberMe)
            {
                AuthPreferences.LastLoginProvider = LoginProvider.Guest;
                LastGuestName = displayName;
            }
            else
            {
                AuthPreferences.LastLoginProvider = LoginProvider.None;
            }

            Debug.Log($"[GuestLoginManager] Guest login succeeded! PUID: {localUserId}");
            ConnectManager.TriggerLoginSuccess(localUserId);
            onComplete?.Invoke(true, null);
        }
    }
}

