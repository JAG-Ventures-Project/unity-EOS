using System;
using EOS.Auth;
using System.Collections.Generic;

namespace EOS.Friends
{
    // Modular contract for platform-specific friend queries (Steam, Epic, PSN, Xbox, Nintendo)
    public interface IPlatformFriendsProvider
    {
        LoginProvider Platform { get; }
        bool IsAvailable { get; }
        void Initialize();
        void FetchFriends(Action<List<PlatformFriend>> onComplete);
        void Shutdown();
    }
}
