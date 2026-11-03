using System;
using System.Collections.Generic;

namespace EOS
{
    // AI implementation: Cloud storage persistence contract for cross-platform friends and blocked users
    public interface ICrossPlayFriendsStore
    {
        IReadOnlyList<CrossPlayFriendEntry> Friends { get; }
        IReadOnlyList<CrossPlayFriendEntry> Blocked { get; }
        IReadOnlyList<CrossPlayFriendEntry> PendingIncoming { get; }
        IReadOnlyList<CrossPlayFriendEntry> PendingOutgoing { get; }

        bool IsLoaded { get; }
        void Load(Action<bool> onComplete);
        void Save(Action<bool, string> onComplete = null);

        bool AddFriend(string productUserId, string displayName, LoginProvider platform);
        bool RemoveFriend(string productUserId);
        bool BlockPlayer(string productUserId, string displayName, LoginProvider platform);
        bool UnblockPlayer(string productUserId);
        bool IsFriend(string productUserId);
        bool IsBlocked(string productUserId);
    }
}

