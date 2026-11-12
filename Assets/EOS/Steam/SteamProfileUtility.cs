#if STEAMWORKS
using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;

namespace EOS.Core
{
    // AI implementation: Shared Steam profile helpers (avatar textures and persona names) used by the Steam friends provider
    // and the local player identity, so avatar decoding lives in exactly one place.
    internal static class SteamProfileUtility
    {
        private static readonly Dictionary<ulong, Texture2D> s_avatarCache = new();

        public static Texture2D GetLocalAvatar()
        {
            return SteamManager.Initialized ? GetOrCreateAvatar(SteamUser.GetSteamID()) : null;
        }

        public static Texture2D GetOrCreateAvatar(CSteamID steamId)
        {
            ulong key = steamId.m_SteamID;
            if (s_avatarCache.TryGetValue(key, out Texture2D cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            int imageId = SteamFriends.GetLargeFriendAvatar(steamId);
            if (imageId <= 0) return null;

            if (!SteamUtils.GetImageSize(imageId, out uint width, out uint height) || width == 0 || height == 0)
            {
                return null;
            }

            int byteCount = (int)(width * height * 4);
            byte[] rgba = new byte[byteCount];
            if (!SteamUtils.GetImageRGBA(imageId, rgba, byteCount))
            {
                return null;
            }

            // Steam returns rows top-to-bottom; Unity textures are bottom-to-top
            byte[] flipped = new byte[rgba.Length];
            int rowSize = (int)width * 4;
            for (int y = 0; y < (int)height; y++)
            {
                Array.Copy(rgba, y * rowSize, flipped, ((int)height - 1 - y) * rowSize, rowSize);
            }

            var texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(flipped);
            texture.Apply();
            s_avatarCache[key] = texture;
            return texture;
        }

        /// <summary>Persona name of a Steam user if Steam already has it cached locally; null otherwise.</summary>
        public static string GetCachedPersonaName(ulong steamId64)
        {
            if (!SteamManager.Initialized) return null;

            string name = SteamFriends.GetFriendPersonaName(new CSteamID(steamId64));
            return string.IsNullOrWhiteSpace(name) || name == "[unknown]" ? null : name;
        }

        public static void EvictAvatar(ulong steamId64)
        {
            s_avatarCache.Remove(steamId64);
        }

        public static void ClearAvatars()
        {
            foreach (Texture2D texture in s_avatarCache.Values)
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
            s_avatarCache.Clear();
        }
    }
}
#endif
