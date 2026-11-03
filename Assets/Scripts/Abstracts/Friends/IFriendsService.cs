using System;
using System.Collections.Generic;
using Epic.OnlineServices;

namespace EOS
{
    // AI implementation: Unified social facade contract providing all friends, blocklist, and invite operations to UI
    public interface IFriendsService
    {
        IReadOnlyList<FriendModel> AllFriends { get; }
        IReadOnlyList<FriendModel> BlockedPlayers { get; }
        bool IsRefreshing { get; }

        event Action OnFriendsListUpdated;
        event Action<FriendModel> OnFriendStatusChanged;

        void Initialize();
        void RefreshFriends(Action onComplete = null);
        void AddFriend(string searchIdentifier, Action<bool, string> onComplete = null);
        void RemoveFriend(string productUserId, Action<bool, string> onComplete = null);
        void BlockPlayer(string productUserId, string displayName, Action<bool, string> onComplete = null);
        void UnblockPlayer(string productUserId, Action<bool, string> onComplete = null);
        void InviteFriendToLobby(ProductUserId targetUserId, Action<bool, string> onComplete = null);
        void Shutdown();
    }
}

