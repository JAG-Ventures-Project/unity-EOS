using EOS.Auth;
using EOS.Core;
using UnityEngine;

namespace EOS.Friends
{
    // AI implementation: Describes the local session (user code, name, platform, avatar) from live SDK state.
    // Platform is derived from the CURRENT session rather than AuthPreferences.LastLoginProvider, which is
    // reset to None whenever "Remember me" is off and would otherwise hide platform friends.
    internal sealed class LocalPlayerIdentity : ILocalPlayerIdentity
    {
        private const string FallbackDisplayName = "Player";

        public bool IsLoggedIn => ConnectManager.Instance != null && ConnectManager.Instance.IsLoggedIn;

        public string UserCode => ConnectManager.Instance?.LocalProductUserIdString ?? string.Empty;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(EOSSDKManager.LocalDisplayName))
                {
                    return EOSSDKManager.LocalDisplayName;
                }

                if (Platform == LoginProvider.Steam && EOSSDKManager.SteamService.IsAvailable)
                {
                    string steamName = EOSSDKManager.SteamService.GetPersonaName();
                    if (!string.IsNullOrEmpty(steamName)) return steamName;
                }

                return FallbackDisplayName;
            }
        }

        public static LoginProvider CurrentPlatform
        {
            get
            {
                if (AuthManager.Instance != null && AuthManager.Instance.IsLoggedIn) return LoginProvider.Epic;
                if (EOSSDKManager.SteamService.HasActiveTicket) return LoginProvider.Steam;
                return AuthPreferences.LastLoginProvider;
            }
        }

        public LoginProvider Platform => CurrentPlatform;

        public Texture2D AvatarTexture => Platform == LoginProvider.Steam ? EOSSDKManager.SteamService.GetAvatar() : null;
    }
}
