using System;
using EOS.Core;
using Epic.OnlineServices;
using Epic.OnlineServices.Auth;
using Epic.OnlineServices.Platform;
using UnityEngine;

namespace EOS.Auth
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

        public static event Action OnLoggedOut;

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

        private static AuthManager _instance;
        public static AuthManager Instance => _instance ??=
            (EpicTransport.EOSSDKComponent.PlatformInterface != null
                ? new AuthManager(EpicTransport.EOSSDKComponent.PlatformInterface)
                : null);

        public AuthManager(PlatformInterface platformInterface = null)
        {
            _platformInterface = platformInterface ?? EpicTransport.EOSSDKComponent.PlatformInterface;
            _authInterface = _platformInterface?.GetAuthInterface();
            EpicTransport.EOSSDKComponent.OnShutdown += () => _instance = null;
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
        /// Executes full Epic login flow (AccountPortal/ExchangeCode -> Connect token exchange -> DisplayName).
        /// </summary>
        public void LoginWithEpic(bool rememberMe, Action<bool, string> onComplete)
        {
            EpicLoginManager.LoginWithEpic(rememberMe, onComplete);
        }

        /// <summary>
        /// Checks launcher arguments, remembered Epic credentials, or Steam for automatic silent login.
        /// </summary>
        public void TryAutoLogin(Action<bool, string> onComplete)
        {
            if (HasExchangeCode())
            {
                Debug.Log("[AuthManager] Epic Games Launcher exchange code detected. Auto-logging in...");
                LoginWithEpic(false, onComplete);
                return;
            }

            switch (AuthPreferences.LastLoginProvider)
            {
                case LoginProvider.Epic when AuthPreferences.RememberEpicAccount:
                    Debug.Log("[AuthManager] Auto-logging in via Epic PersistentAuth...");
                    LoginWithPersistentAuth(fallbackToAccountPortal: false, (authSuccess, authError) =>
                    {
                        if (authSuccess)
                        {
                            string token = CopyUserAuthToken();
                            EpicLoginManager.LoginWithToken(token, true, onComplete);
                        }
                        else
                        {
                            onComplete?.Invoke(false, authError);
                        }
                    });
                    break;

                case LoginProvider.Steam when SteamLoginManager.IsAvailable:
                    Debug.Log("[AuthManager] Auto-logging in via Steam...");
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
        public void LogoutAll(Action<bool, string> onComplete = null)
        {
            void FinalizeLogout()
            {
                ConnectManager.Instance?.CancelSteamTicket();
                ConnectManager.Instance?.ResetSession();
                AuthPreferences.ClearSession();
                OnLoggedOut?.Invoke();
                onComplete?.Invoke(true, null);
            }

            if (IsLoggedIn)
            {
                Logout(forgetUser: true, (success, error) => FinalizeLogout());
            }
            else
            {
                FinalizeLogout();
            }
        }

        /// <summary>
        /// Internal helper that executes AuthInterface.Login and handles retries / fallback.
        /// </summary>
        private void PerformLogin(LoginCredentialType credentialType, string token, bool fallbackToAccountPortal, Action<bool, string> onComplete)
        {
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
                    CompleteOperation(onComplete, true);
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
                    CompleteOperation(onComplete, false, error);
                }
            });
        }

        public void DeletePersistentAuth(Action<bool, string> onComplete = null)
        {
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
                CompleteOperation(onComplete, success, success ? null : callbackInfo.ResultCode.ToString());
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
                CompleteOperation(onComplete, false, "[AuthManager] Cannot log out: Not logged in or AuthInterface is null.");
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
                    CompleteOperation(onComplete, true);
                }
                else
                {
                    string error = $"[AuthManager] Epic Games Logout Failed: {logoutCallbackInfo.ResultCode}";
                    CompleteOperation(onComplete, false, error);
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