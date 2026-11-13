using System;
using UnityEngine;
using Epic.OnlineServices;
using EOS.Core;

namespace EOS.Auth
{
    /// <summary>
    /// Standalone Steam Login Manager.
    /// Obtains Steam Web API session ticket via ISteamService and connects via ConnectManager.
    /// </summary>
    public static class SteamLoginManager
    {
        public static bool IsAvailable => EOSSDKManager.SteamService.IsAvailable;

        public static void CancelTicket()
        {
            EOSSDKManager.SteamService.CancelTicket();
        }

        public static void LoginWithSteam(bool rememberMe, Action<bool, string> onComplete)
        {
            if (!EOSSDKManager.SteamService.IsAvailable)
            {
                onComplete?.Invoke(false, "Steam is not open or initialized. Please make sure Steam is running.");
                Debug.LogError("Ensure Steam Manager is added in the scene");
                return;
            }

            EOSSDKManager.SteamService.RequestWebTicket((success, ticketHex, steamName) =>
            {
                if (!success || string.IsNullOrEmpty(ticketHex))
                {
                    onComplete?.Invoke(false, ticketHex ?? "Failed to request Steam Web API ticket!");
                    return;
                }

                LoginWithTicket(ticketHex, steamName, rememberMe, onComplete);
            });
        }

        public static void LoginWithTicket(string ticketHex, string displayName, bool rememberMe, Action<bool, string> onComplete)
        {
            if (string.IsNullOrEmpty(ticketHex))
            {
                onComplete?.Invoke(false, "Steam ticket is null or empty!");
                return;
            }

            ConnectManager.Instance.PerformConnectLogin(
                ExternalCredentialType.SteamSessionTicket,
                ticketHex,
                displayName,
                (loginSuccess, error) =>
                {
                    if (loginSuccess)
                    {
                        EOSSDKManager.LocalDisplayName = displayName;
                        AuthPreferences.LastLoginProvider = rememberMe ? LoginProvider.Steam : LoginProvider.None;
                        Debug.Log($"[SteamLoginManager] Steam connect login succeeded! DisplayName: {displayName}");
                        onComplete?.Invoke(true, null);
                    }
                    else
                    {
                        CancelTicket();
                        onComplete?.Invoke(false, error);
                    }
                });
        }
    }
}

