using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Auth;
using Epic.OnlineServices.Platform;
using UnityEngine;

namespace EOS
{   
    /// <summary>
    /// Manages Epic Account Services (AuthInterface) authentication.
    /// Handles PersistentAuth (Remember Me), AccountPortal (interactive login), and ExchangeCode (launcher builds).
    /// based on https://dev.epicgames.com/docs/epic-online-services/accounts-and-social/eos-epic-account-services/auth-interface
    /// and https://dev.epicgames.com/docs/epic-online-services/accounts-and-social/eos-epic-account-services/auth-interface/auth-guide/log-in-with-the-account-portal
    /// and some extra ideas 
    /// </summary>
    public class AuthManager
    {
        private const string EpicRememberMeKey = "EOS_RememberEpicAccount";

        private readonly PlatformInterface _platformInterface;
        private readonly AuthInterface _authInterface;
        private EpicAccountId _epicId;

        public EpicAccountId LocalEpicAccountId => _epicId;
        public bool IsLoggedIn => _epicId != null && _epicId.IsValid();

        /// <summary>
        /// Gets or sets whether to remember the user's Epic Games login across sessions via Persistent Authorization.
        /// When set to false, existing persistent credentials are deleted from the local device.
        /// </summary>
        public bool IsRememberMe
        {
            get => AuthPreferences.RememberEpicAccount;
            set
            {
                AuthPreferences.RememberEpicAccount = value;
                if (!value)
                {
                    DeletePersistentAuth();
                }
            }
        }

        public AuthManager(PlatformInterface platformInterface)
        {
            // Q: is it ok to keep this or delte it no need for it ? 
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
        /// - If launcher launch arguments (-AUTH_PASSWORD) are present: uses ExchangeCode.
        /// - Else if IsRememberMe is true: attempts silent login via PersistentAuth.
        /// - Otherwise: opens AccountPortal for interactive login.
        /// </summary>
        public void Login(bool fallbackToAccountPortal = true, Action<bool, string> onComplete = null)
        {
            string exchangeCode = GetCommandLineArg("-AUTH_PASSWORD");
            if (!string.IsNullOrEmpty(exchangeCode))
            {
                Debug.Log("[AuthManager] (Build) Detected -AUTH_PASSWORD from Epic Games Launcher. Signing in via ExchangeCode...");
                PerformLogin(LoginCredentialType.ExchangeCode, exchangeCode, false, onComplete);
                return;
            }

            if (IsRememberMe)
            {
                Debug.Log("[AuthManager] Remember Me is enabled. Attempting silent login via PersistentAuth...");
                PerformLogin(LoginCredentialType.PersistentAuth, null, fallbackToAccountPortal, onComplete);
                return;
            }

            Debug.Log("[AuthManager] Signing in via AccountPortal...");
            PerformLogin(LoginCredentialType.AccountPortal, null, false, onComplete);
        }

        /// <summary>
        /// Explicitly attempts login using stored PersistentAuth credentials.
        /// </summary>
        public void LoginWithPersistentAuth(bool fallbackToAccountPortal = false, Action<bool, string> onComplete = null)
        {
            PerformLogin(LoginCredentialType.PersistentAuth, null, fallbackToAccountPortal, onComplete);
        }

        /// <summary>
        /// Internal helper that executes AuthInterface.Login and handles retries / fallback.
        /// </summary>
        private void PerformLogin(LoginCredentialType credentialType, string token, bool fallbackToAccountPortal, Action<bool, string> onComplete)
        {
            // Q: is it ok to keep this or delte it no need for it ?
            // if (_authInterface == null)
            // {
            //     string error = "[AuthManager] AuthInterface is null! Ensure PlatformInterface has been initialized.";
            //     Debug.LogError(error);
            //     onComplete?.Invoke(false, error);
            //     return;
            // }

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
                    Debug.Log($"<color=green>[AuthManager] {credentialType} Login Success! EpicAccountId: {_epicId}</color>");
                    onComplete?.Invoke(true, null);
                }
                else if (!Common.IsOperationComplete(loginCallbackInfo.ResultCode))
                {
                    // If the code gets here, the operation is retrying and is not yet complete.
                    return;
                }
                else if (credentialType == LoginCredentialType.PersistentAuth && fallbackToAccountPortal)
                {
                    Debug.LogWarning($"[AuthManager] PersistentAuth failed ({loginCallbackInfo.ResultCode}). Falling back to AccountPortal...");
                    PerformLogin(LoginCredentialType.AccountPortal, null, false, onComplete);
                }
                else
                {
                    string error = $"[AuthManager] {credentialType} Login Failed: {loginCallbackInfo.ResultCode}";
                    Debug.LogError(error);
                    onComplete?.Invoke(false, error);
                }
            });
        }

        public void DeletePersistentAuth(Action<bool, string> onComplete = null)
        {
            // Q: is it ok to keep this or delte it no need for it ? 
            // if (_authInterface == null)
            // {
            //     onComplete?.Invoke(false, "[AuthManager] AuthInterface is null.");
            //     return;
            // }

            // even though the documentaion says use "LogoutOptions" we used "DeletePersistentAuthOptions" to Delete the stored token from the OS keychain
            // https://dev.epicgames.com/docs/api-ref/functions/eos-auth-delete-persistent-auth
            var deleteOptions = new DeletePersistentAuthOptions();
            _authInterface.DeletePersistentAuth(deleteOptions, null, (DeletePersistentAuthCallbackInfo callbackInfo) =>
            {
                bool success = callbackInfo.ResultCode == Result.Success;
                if (!success)
                {
                    Debug.LogWarning($"[AuthManager] DeletePersistentAuth failed: {callbackInfo.ResultCode}");
                }
                else
                {
                    Debug.Log("[AuthManager] Persistent Auth credentials successfully deleted.");
                }
                onComplete?.Invoke(success, success ? null : callbackInfo.ResultCode.ToString());
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
        /// we'll use " forgetUser = true " when the user clicks logut button (and was signed in with epic) 
        /// </summary>
        public void Logout(bool forgetUser = false, Action<bool, string> onComplete = null)
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

                    Debug.Log("[AuthManager] Epic Logout Success");
                    _epicId = null;

                    if (forgetUser)
                    {
                        IsRememberMe = false;
                    }
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

        public void GetDisplayName(Action<string> onComplete)
        {
            var userInfoInterface = _platformInterface.GetUserInfoInterface();
            var queryOptions = new Epic.OnlineServices.UserInfo.QueryUserInfoOptions
            {
                LocalUserId = _epicId,
                TargetUserId = _epicId
            };

            userInfoInterface.QueryUserInfo(queryOptions, null, (queryCb) =>
            {
                if (queryCb.ResultCode == Result.Success)
                {
                    var copyOptions = new Epic.OnlineServices.UserInfo.CopyUserInfoOptions
                    {
                        LocalUserId = _epicId,
                        TargetUserId = _epicId
                    };

                    if (userInfoInterface.CopyUserInfo(copyOptions, out Epic.OnlineServices.UserInfo.UserInfoData data) == Result.Success)
                    {
                        onComplete?.Invoke(data.DisplayName);
                        return;
                    }
                }
                onComplete?.Invoke("EpicPlayer");
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