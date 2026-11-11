using System;
using System.Collections.Generic;

namespace EOS
{
    // Cloud storage persistence contract for cross-platform friends, friend requests and blocked users
    public interface ICrossPlayFriendsStore
    {
        IReadOnlyList<CrossPlayFriendEntry> Friends { get; }
        IReadOnlyList<CrossPlayFriendEntry> Blocked { get; }
        IReadOnlyList<CrossPlayFriendEntry> PendingIncoming { get; }
        IReadOnlyList<CrossPlayFriendEntry> PendingOutgoing { get; }
        IReadOnlyList<FriendSignal> Outbox { get; }

        bool IsLoaded { get; }
        void Load(Action<bool> onComplete);
        void Save(Action<bool, string> onComplete = null);

        /// <summary>Drops all in-memory data (used on logout so one account's data never leaks into the next session).</summary>
        void Reset();

        bool AddFriend(string productUserId, string displayName, LoginProvider platform);
        bool RemoveFriend(string productUserId);
        bool BlockPlayer(string productUserId, string displayName, LoginProvider platform);
        bool UnblockPlayer(string productUserId);
        bool IsFriend(string productUserId);
        bool IsBlocked(string productUserId);

        bool AddPendingIncoming(string productUserId, string displayName, LoginProvider platform);
        bool RemovePendingIncoming(string productUserId);
        bool IsPendingIncoming(string productUserId);
        bool TryGetPendingIncoming(string productUserId, out CrossPlayFriendEntry entry);

        bool AddPendingOutgoing(string productUserId, string displayName, LoginProvider platform);
        bool RemovePendingOutgoing(string productUserId);
        bool IsPendingOutgoing(string productUserId);

        bool TryGetFriend(string productUserId, out CrossPlayFriendEntry entry);

        void EnqueueSignal(FriendSignal signal);
        bool RemoveSignal(string signalId);
    }
}
