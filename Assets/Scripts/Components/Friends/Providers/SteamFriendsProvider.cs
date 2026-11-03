using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;

namespace EOS
{
    // AI implementation: Native Steam platform provider querying friends, persona state, and avatar textures
    public class SteamFriendsProvider : IPlatformFriendsProvider
    {
        public LoginProvider Platform => LoginProvider.Steam;
        public bool IsAvailable => SteamManager.Initialized;

        private Callback<PersonaStateChange_t> _personaStateCallback;
        private readonly Dictionary<ulong, Texture2D> _avatarCache = new();

        public void Initialize()
        {
            if (!IsAvailable) return;

            _personaStateCallback = Callback<PersonaStateChange_t>.Create(HandlePersonaStateChanged);
        }

        public void FetchFriends(Action<List<PlatformFriend>> onComplete)
        {
            var results = new List<PlatformFriend>();

            if (!IsAvailable)
            {
                onComplete?.Invoke(results);
                return;
            }

            int friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < friendCount; i++)
            {
                CSteamID friendSteamId = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                string personaName = SteamFriends.GetFriendPersonaName(friendSteamId);
                EPersonaState personaState = SteamFriends.GetFriendPersonaState(friendSteamId);

                FriendPresenceState presence = MapSteamPresence(personaState);
                Texture2D avatar = GetOrCreateSteamAvatar(friendSteamId);

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

        private Texture2D GetOrCreateSteamAvatar(CSteamID steamId)
        {
            ulong key = steamId.m_SteamID;
            if (_avatarCache.TryGetValue(key, out Texture2D cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            int imageId = SteamFriends.GetLargeFriendAvatar(steamId);
            if (imageId <= 0) return null;

            if (SteamUtils.GetImageSize(imageId, out uint width, out uint height) && width > 0 && height > 0)
            {
                byte[] rgba = new byte[width * height * 4];
                if (SteamUtils.GetImageRGBA(imageId, rgba, (int)(width * height * 4)))
                {
                    Texture2D texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
                    byte[] flipped = new byte[rgba.Length];
                    int rowSize = (int)width * 4;
                    for (int y = 0; y < (int)height; y++)
                    {
                        Array.Copy(rgba, y * rowSize, flipped, ((int)height - 1 - y) * rowSize, rowSize);
                    }
                    texture.LoadRawTextureData(flipped);
                    texture.Apply();
                    _avatarCache[key] = texture;
                    return texture;
                }
            }

            return null;
        }

        private void HandlePersonaStateChanged(PersonaStateChange_t data)
        {
            // Avatar or status updated; clear cache for this user to re-query next fetch
            _avatarCache.Remove(data.m_ulSteamID);
        }

        public void Shutdown()
        {
            _personaStateCallback?.Dispose();
            _personaStateCallback = null;

            foreach (var texture in _avatarCache.Values)
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
            _avatarCache.Clear();
        }
    }
}

