using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using UnityEngine;

namespace EOS
{
    // AI implementation: Describes the local session (user code, name, platform, avatar) from live SDK state.
    // Platform is derived from the CURRENT session rather than AuthPreferences.LastLoginProvider, which is
    // reset to None whenever "Remember me" is off and would otherwise hide platform friends.
    internal sealed class LocalPlayerIdentity : ILocalPlayerIdentity
    {
        private const string FallbackDisplayName = "Player";

        public bool IsLoggedIn => EpicTransport.EOSSDKComponent.LocalUserProductId != null;

        public string UserCode
        {
            get
            {
                string puid = EpicTransport.EOSSDKComponent.LocalUserProductIdString;
                if (!string.IsNullOrEmpty(puid)) return puid;
                if (ConnectManager.Instance != null && ConnectManager.Instance.LocalProductUserId != null)
                {
                    return ConnectManager.Instance.LocalProductUserId.ToString();
                }
                return string.Empty;
            }
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(EOSSDKManager.LocalDisplayName))
                {
                    return EOSSDKManager.LocalDisplayName;
                }

                if (Platform == LoginProvider.Steam && SteamManager.Initialized)
                {
                    string steamName = Steamworks.SteamFriends.GetPersonaName();
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
                if (EOSSDKManager.SteamAuthTicket != Steamworks.HAuthTicket.Invalid) return LoginProvider.Steam;
                return AuthPreferences.LastLoginProvider;
            }
        }

        public LoginProvider Platform => CurrentPlatform;

        public Texture2D AvatarTexture => Platform == LoginProvider.Steam ? SteamProfileUtility.GetLocalAvatar() : null;
    }
}

