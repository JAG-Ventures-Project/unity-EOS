using System;
using System.Collections.Generic;
using EOS.Auth;
using UnityEngine;

namespace EOS.Friends
{
    // Modular contract for platform-specific friend queries (Steam, Epic, PSN, Xbox, Nintendo)
    public interface IPlatformFriendsProvider
    {
        LoginProvider Platform { get; }
        bool IsAvailable { get; }
        event Action<string, string, FriendPresenceState, Texture2D> OnFriendUpdated;
        void Initialize();
        void FetchFriends(Action<List<PlatformFriend>> onComplete);
        void Shutdown();
    }
}
