using System;
using EOS.Core;
using UnityEngine;

namespace EOS.Auth
{
    /// <summary>
    /// Coordinates cross-provider authentication session lifecycle:
    /// automatic silent login on startup and full multi-provider logout.
    /// </summary>
    public static class SessionAuthCoordinator
    {
        public static event Action OnLoggedOut;

        /// <summary>
        /// Checks launcher arguments, remembered Epic credentials, or Steam for automatic silent login.
        /// </summary>
        public static void TryAutoLogin(Action<bool, string> onComplete)
        {
            if (AuthManager.HasExchangeCode())
            {
                Debug.Log("[SessionAuthCoordinator] Epic Games Launcher exchange code detected. Auto-logging in...");
                EpicLoginManager.LoginWithEpic(false, onComplete);
                return;
            }

            switch (AuthPreferences.LastLoginProvider)
            {
                case LoginProvider.Epic when AuthPreferences.RememberEpicAccount:
                    if (AuthManager.Instance == null)
                    {
                        onComplete?.Invoke(false, "AuthManager is not initialized.");
                        return;
                    }

                    Debug.Log("[SessionAuthCoordinator] Auto-logging in via Epic PersistentAuth...");
                    AuthManager.Instance.LoginWithPersistentAuth(fallbackToAccountPortal: false, (authSuccess, authError) =>
                    {
                        if (authSuccess)
                        {
                            string token = AuthManager.Instance?.CopyUserAuthToken();
                            EpicLoginManager.LoginWithToken(token, true, onComplete);
                        }
                        else
                        {
                            onComplete?.Invoke(false, authError);
                        }
                    });
                    break;

                case LoginProvider.Steam when SteamLoginManager.IsAvailable:
                    Debug.Log("[SessionAuthCoordinator] Auto-logging in via Steam...");
                    SteamLoginManager.LoginWithSteam(true, onComplete);
                    break;

                default:
                    onComplete?.Invoke(false, null);
                    break;
            }
        }

        /// <summary>
        /// Signs out of all authentication providers, cancels Steam tickets, and clears session data.
        /// </summary>
        public static void LogoutAll(Action<bool, string> onComplete = null)
        {
            void FinalizeLogout()
            {
                ConnectManager.Instance?.CancelSteamTicket();
                ConnectManager.Instance?.ResetSession();
                EOSSDKManager.LocalDisplayName = "Player";
                AuthPreferences.ClearSession();
                OnLoggedOut?.Invoke();
                onComplete?.Invoke(true, null);
            }

            if (AuthManager.Instance != null && AuthManager.Instance.IsLoggedIn)
            {
                AuthManager.Instance.Logout(forgetUser: true, (success, error) => FinalizeLogout());
            }
            else
            {
                FinalizeLogout();
            }
        }
    }
}

