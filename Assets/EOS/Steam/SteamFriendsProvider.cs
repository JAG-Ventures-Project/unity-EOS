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
        private const EPersonaChange RelevantPersonaChanges =
            EPersonaChange.k_EPersonaChangeName |
            EPersonaChange.k_EPersonaChangeStatus |
            EPersonaChange.k_EPersonaChangeComeOnline |
            EPersonaChange.k_EPersonaChangeGoneOffline |
            EPersonaChange.k_EPersonaChangeGamePlayed |
            EPersonaChange.k_EPersonaChangeAvatar;

        public LoginProvider Platform => LoginProvider.Steam;
        public bool IsAvailable => SteamManager.Initialized;

        public event Action<string, string, FriendPresenceState, Texture2D> OnFriendUpdated;

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

                FriendPresenceState presence = MapSteamPresence(friendSteamId);
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

        private FriendPresenceState MapSteamPresence(CSteamID friendSteamId)
        {
            if (SteamFriends.GetFriendPersonaState(friendSteamId) == EPersonaState.k_EPersonaStateOffline)
            {
                return FriendPresenceState.Offline;
            }

            if (SteamFriends.GetFriendGamePlayed(friendSteamId, out FriendGameInfo_t gameInfo) &&
                gameInfo.m_gameID.AppID() == SteamUtils.GetAppID())
            {
                return FriendPresenceState.InGame;
            }

            return FriendPresenceState.Online;
        }

        private void HandlePersonaStateChanged(PersonaStateChange_t data)
        {
            if ((data.m_nChangeFlags & EPersonaChange.k_EPersonaChangeAvatar) != 0)
            {
                SteamProfileUtility.EvictAvatar(data.m_ulSteamID);
            }

            if ((data.m_nChangeFlags & RelevantPersonaChanges) == 0)
            {
                return;
            }

            var friendSteamId = new CSteamID(data.m_ulSteamID);
            if (!SteamFriends.HasFriend(friendSteamId, EFriendFlags.k_EFriendFlagImmediate))
            {
                return;
            }

            string personaName = SteamFriends.GetFriendPersonaName(friendSteamId);
            FriendPresenceState presence = MapSteamPresence(friendSteamId);
            Texture2D avatar = SteamProfileUtility.GetOrCreateAvatar(friendSteamId);
            OnFriendUpdated?.Invoke(friendSteamId.m_SteamID.ToString(), personaName, presence, avatar);
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
