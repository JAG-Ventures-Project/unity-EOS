using System;
using UnityEngine;
using Epic.OnlineServices;
using EOS.Core;

namespace EOS.Auth
{
    /// <summary>
    /// Standalone Epic Games Login Manager.
    /// Authenticates with Epic Account Services (AuthInterface) to obtain an auth token,
    /// then performs Connect login through ConnectManager.
    /// </summary>
    public static class EpicLoginManager
    {
        public static void LoginWithEpic(bool rememberMe, Action<bool, string> onComplete)
        {
            if (AuthManager.Instance == null)
            {
                onComplete?.Invoke(false, "AuthManager is not initialized.");
                return;
            }

            EOSSDKManager.SteamService.CancelTicket();
            AuthManager.Instance.IsRememberMe = rememberMe;

            AuthManager.Instance.Login(fallbackToAccountPortal: true, (authSuccess, authError) =>
            {
                if (!authSuccess)
                {
                    onComplete?.Invoke(false, authError);
                    return;
                }

                CompleteEpicConnectLogin(rememberMe, onComplete);
            });
        }

        public static void LoginWithToken(string epicAuthToken, bool rememberMe, Action<bool, string> onComplete)
        {
            if (string.IsNullOrEmpty(epicAuthToken))
            {
                onComplete?.Invoke(false, "Epic auth token is null or empty!");
                return;
            }

            ConnectManager.Instance.PerformConnectLogin(
                ExternalCredentialType.Epic,
                epicAuthToken,
                null,
                (connectSuccess, connectError) =>
                {
                    if (!connectSuccess)
                    {
                        onComplete?.Invoke(false, connectError);
                        return;
                    }

                    if (AuthManager.Instance != null)
                    {
                        AuthManager.Instance.GetDisplayName((epicDisplayName) =>
                        {
                            EOSSDKManager.LocalDisplayName = epicDisplayName;
                            AuthPreferences.LastLoginProvider = rememberMe ? LoginProvider.Epic : LoginProvider.None;
                            Debug.Log($"[EpicLoginManager] Epic connect login succeeded! DisplayName: {epicDisplayName}");
                            onComplete?.Invoke(true, null);
                        });
                    }
                    else
                    {
                        AuthPreferences.LastLoginProvider = rememberMe ? LoginProvider.Epic : LoginProvider.None;
                        onComplete?.Invoke(true, null);
                    }
                });
        }

        private static void CompleteEpicConnectLogin(bool rememberMe, Action<bool, string> onComplete)
        {
            string token = AuthManager.Instance?.CopyUserAuthToken();
            if (string.IsNullOrEmpty(token))
            {
                onComplete?.Invoke(false, "Failed to copy Epic user auth token.");
                return;
            }

            LoginWithToken(token, rememberMe, onComplete);
        }
    }
}

