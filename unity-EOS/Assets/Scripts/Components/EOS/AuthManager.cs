using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Auth;
using Epic.OnlineServices.Platform;
using UnityEngine;

namespace EOS
{   
    /// <summary>
    /// Manages Epic Account Services (AuthInterface) authentication.
    /// Handles AccountPortal in Unity Editor and ExchangeCode in launcher builds.
    /// based on https://dev.epicgames.com/docs/epic-online-services/accounts-and-social/eos-epic-account-services/auth-interface
    /// and some extra ideas 
    /// </summary>
    public class AuthManager
    {
        private readonly PlatformInterface _platformInterface;
        private readonly AuthInterface _authInterface;
        private EpicAccountId _epicId;

        public EpicAccountId LocalEpicAccountId => _epicId;
        public bool IsLoggedIn => _epicId != null && _epicId.IsValid();

        public AuthManager(PlatformInterface platformInterface)
        {
            // if (EOSSDKManager.Instance != null)
            // {
            //     _platformInterface = EOSSDKManager.Instance.GetEOSPlatformInterface();
            // }

            _platformInterface = platformInterface;
            _authInterface = _platformInterface?.GetAuthInterface();
        }

        /// <summary>
        /// AI Implementation
        /// Logs in to Epic Account Services.
        /// - In Unity Editor: Uses AccountPortal for interactive login via browser/overlay.
        /// - In Build: Checks command-line args for -AUTH_PASSWORD to log in silently via ExchangeCode.
        ///   Falls back to AccountPortal if launch arguments are missing.
        /// </summary>
        public void Login(Action<bool, string> onComplete = null)
        {
            if (_authInterface == null)
            {
                string error = "[AuthManager] AuthInterface is null! Ensure PlatformInterface has been initialized.";
                Debug.LogError(error);
                onComplete?.Invoke(false, error);
                return;
            }

            LoginCredentialType credentialType;
            string token = null;

#if UNITY_EDITOR
            credentialType = LoginCredentialType.AccountPortal;
            Debug.Log("[AuthManager] (Editor) Signing in via AccountPortal...");
#else
            string exchangeCode = GetCommandLineArg("-AUTH_PASSWORD");
            if (!string.IsNullOrEmpty(exchangeCode))
            {
                credentialType = LoginCredentialType.ExchangeCode;
                token = exchangeCode;
                Debug.Log("[AuthManager] (Build) Detected -AUTH_PASSWORD from Epic Games Launcher. Signing in via ExchangeCode...");
            }
            else
            {
                credentialType = LoginCredentialType.AccountPortal;
                Debug.LogWarning("[AuthManager] (Build) No -AUTH_PASSWORD argument found. Falling back to AccountPortal...");
            }
#endif

            var loginOptions = new LoginOptions
            {
                Credentials = new Credentials
                {
                    Type = credentialType,
                    Id = null,
                    Token = token
                },
                ScopeFlags = AuthScopeFlags.BasicProfile | AuthScopeFlags.FriendsList | AuthScopeFlags.Presence
            };

            _authInterface.Login(loginOptions, null, (LoginCallbackInfo loginCallbackInfo) =>
            {
                if (loginCallbackInfo.ResultCode == Result.Success)
                {
                    _epicId = loginCallbackInfo.LocalUserId;
                    Debug.Log($"<color=green>[AuthManager] Epic Games Account Login Success! EpicAccountId: {_epicId}</color>");
                    onComplete?.Invoke(true, null);
                }
                else
                {
                    string error = $"[AuthManager] Epic Games Login Failed: {loginCallbackInfo.ResultCode}";
                    Debug.LogError(error);
                    onComplete?.Invoke(false, error);
                }
            });
        }

        /// <summary>
        /// Copies the authenticated user's access token to use with ConnectInterface.Login (ExternalCredentialType.Epic).
        /// </summary>
        public string CopyUserAuthToken()
        {
            if (_authInterface == null || _epicId == null)
            {
                Debug.LogError("[AuthManager] Cannot copy auth token: Not logged in or AuthInterface is null.");
                return null;
            }

            var copyOptions = new CopyUserAuthTokenOptions();
            Result result = _authInterface.CopyUserAuthToken(copyOptions, _epicId, out Token userAuthToken);

            if (result == Result.Success && userAuthToken != null)
            {
                return userAuthToken.AccessToken;
            }

            Debug.LogError($"[AuthManager] CopyUserAuthToken failed: {result}");
            return null;
        }

        /// <summary>
        /// Signs out of Epic Account Services.
        /// </summary>
        public void Logout(Action<bool, string> onComplete = null)
        {
            if (_authInterface == null || _epicId == null)
            {
                onComplete?.Invoke(false, "[AuthManager] Cannot log out: Not logged in or AuthInterface is null.");
                return;
            }

            var logoutOptions = new LogoutOptions
            {
                LocalUserId = _epicId
            };

            _authInterface.Logout(logoutOptions, null, (LogoutCallbackInfo logoutCallbackInfo) =>
            {
                if (logoutCallbackInfo.ResultCode == Result.Success)
                {
                    Debug.Log("[AuthManager] Epic Games Logout Success.");
                    _epicId = null;
                    onComplete?.Invoke(true, null);
                }
                else
                {
                    string error = $"[AuthManager] Epic Games Logout Failed: {logoutCallbackInfo.ResultCode}";
                    Debug.LogError(error);
                    onComplete?.Invoke(false, error);
                }
            });
        }

        /// <summary>
        /// Retrieves the value of a command-line argument passed to the game.
        /// Supports both -key=value and -key value formats.
        /// </summary>
        public static string GetCommandLineArg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            if (args == null) return null;
            // used for to itrate through the args instead of using dictionary. Because Epic only passes 8 args 
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg)) continue;

                // Match -KEY=VALUE
                if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(name.Length + 1).Trim('"', '\'');
                }

                // Match -KEY VALUE
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1].Trim('"', '\'');
                }
            }

            return null;
        }

        /// <summary>
        /// Returns true if the Epic Games Launcher passed an exchange code via -AUTH_PASSWORD.
        /// </summary>
        public static bool HasExchangeCode()
        {
            return !string.IsNullOrEmpty(GetCommandLineArg("-AUTH_PASSWORD"));
        }
    }   
}