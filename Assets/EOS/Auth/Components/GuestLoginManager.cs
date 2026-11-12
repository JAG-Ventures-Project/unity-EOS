using System;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;

namespace EOS.Auth
{
    /// <summary>
    /// Handles Device-ID-based Guest Login for development and test workflows.
    /// Encapsulates Device ID creation and guest authentication separate from production auth scripts.
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
        /// Logs in as a Guest using the local machine's Device ID.
        /// </summary>
        public static void LoginAsGuest(string displayName, bool rememberMe, Action<bool, string> onComplete)
        {
            var connectManager = ConnectManager.Instance;
            if (connectManager == null)
            {
                onComplete?.Invoke(false, "ConnectManager is not available.");
                return;
            }

            connectManager.CancelSteamTicket();

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

                connectManager.PerformConnectLogin(ExternalCredentialType.DeviceidAccessToken, null, cleanName, (success, error) =>
                {
                    if (success)
                    {
                        EpicTransport.EOSSDKComponent.DisplayName = cleanName;
                        if (rememberMe)
                        {
                            AuthPreferences.LastLoginProvider = LoginProvider.Guest;
                            LastGuestName = cleanName;
                        }
                        else
                        {
                            AuthPreferences.LastLoginProvider = LoginProvider.None;
                        }
                    }

                    onComplete?.Invoke(success, error);
                });
            });
        }
    }
}

