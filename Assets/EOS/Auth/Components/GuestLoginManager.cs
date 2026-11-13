using System;
using EOS.Core;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;

namespace EOS.Auth
{
    /// <summary>
    /// Standalone Device-ID-based Guest Login for development and test workflows.
    /// Obtains a local Device ID and connects via ConnectManager.
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
        /// Logs in as a Guest using the local machine's Device ID via ConnectManager.
        /// </summary>
        public static void LoginAsGuest(string displayName, bool rememberMe, Action<bool, string> onComplete)
        {
            string cleanName = string.IsNullOrWhiteSpace(displayName) ? "Guest" : displayName.Trim();

            var platform = EOSSDKManager.PlatformInterface;
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

                ConnectManager.Instance.PerformConnectLogin(
                    ExternalCredentialType.DeviceidAccessToken,
                    null,
                    cleanName,
                    (success, error) =>
                    {
                        if (success)
                        {
                            EOSSDKManager.LocalDisplayName = cleanName;
                            if (rememberMe)
                            {
                                AuthPreferences.LastLoginProvider = LoginProvider.Guest;
                                LastGuestName = cleanName;
                            }
                            else
                            {
                                AuthPreferences.LastLoginProvider = LoginProvider.None;
                            }

                            Debug.Log($"[GuestLoginManager] Guest connect login succeeded! DisplayName: {cleanName}");
                            onComplete?.Invoke(true, null);
                        }
                        else
                        {
                            onComplete?.Invoke(false, error);
                        }
                    });
            });
        }
    }
}
