using System;
using UnityEngine;
using EOS.Friends;

namespace EOS.Core
{
    /// <summary>
    /// Fallback implementation used when Steamworks is not installed, disabled, or unavailable.
    /// Provides safe no-op responses without throwing errors or referencing Steamworks types.
    /// </summary>
    public class NullSteamService : ISteamService
    {
        public bool IsAvailable => false;
        public bool HasActiveTicket => false;

        public string GetPersonaName() => null;

        public Texture2D GetAvatar() => null;

        public void RequestWebTicket(Action<bool, string, string> onComplete)
        {
            Debug.LogWarning("[EOS] Steam service is not available or not installed.");
            onComplete?.Invoke(false, "Steam is not installed or enabled in this project.", null);
        }

        public void CancelTicket()
        {
            // No-op
        }

        public IPlatformFriendsProvider CreateFriendsProvider() => null;
    }
}

