#if STEAMWORKS
using System;
using EOS.Auth;
using EOS.Core;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;

namespace EOS.Friends
{
    // Native Steam platform provider querying friends, persona state, and avatar textures
    // Based on : https://partner.steamgames.com/doc/api/isteamfriends
    public class SteamFriendsProvider : IPlatformFriendsProvider
    {
        public LoginProvider Platform => LoginProvider.Steam;
        public bool IsAvailable => SteamManager.Initialized;

        private Callback<PersonaStateChange_t> _personaStateCallback;

        public void Initialize()
        {
            _personaStateCallback = Callback<PersonaStateChange_t>.Create(HandlePersonaStateChanged);
        }

        // AI Implementation , populating friends list
        public void FetchFriends(Action<List<PlatformFriend>> onComplete)
        {
            var results = new List<PlatformFriend>();

            int friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < friendCount; i++)
            {
                CSteamID friendSteamId = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                string personaName = SteamFriends.GetFriendPersonaName(friendSteamId);
                EPersonaState personaState = SteamFriends.GetFriendPersonaState(friendSteamId);

                FriendPresenceState presence = MapSteamPresence(personaState);
                Texture2D avatar = SteamProfileUtility.GetOrCreateAvatar(friendSteamId);

                results.Add(new PlatformFriend
                {
                    PlatformUserId = friendSteamId.m_SteamID.ToString(),
                    DisplayName = personaName,
                    Platform = LoginProvider.Steam,
                    Presence = presence,
                    AvatarTexture = avatar
                });
            }

            onComplete?.Invoke(results);
        }

        private FriendPresenceState MapSteamPresence(EPersonaState state)
        {
            return state switch
            {
                EPersonaState.k_EPersonaStateOffline => FriendPresenceState.Offline,
                _ => FriendPresenceState.Online
            };
        }

        private void HandlePersonaStateChanged(PersonaStateChange_t data)
        {
            SteamProfileUtility.EvictAvatar(data.m_ulSteamID);
        }

        // AI Implementation
        public void Shutdown()
        {
            _personaStateCallback?.Dispose();
            _personaStateCallback = null;

            SteamProfileUtility.ClearAvatars();
        }
    }
}
#endif
