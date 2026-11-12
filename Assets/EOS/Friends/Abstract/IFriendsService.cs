using System;
using EOS.Auth;
using System.Collections.Generic;
using Epic.OnlineServices;

namespace EOS.Friends
{
    // Unified social facade contract providing all friends, request, blocklist, and invite operations to UI.
    // Every Action<bool, string> completion receives (success, message): message is a human-readable result
    // (error text on failure, optional confirmation text on success) that UI may show directly.
    public interface IFriendsService
    {
        IReadOnlyList<FriendModel> AllFriends { get; }
        IReadOnlyList<FriendModel> BlockedPlayers { get; }
        IReadOnlyList<FriendModel> IncomingRequests { get; }
        IReadOnlyList<FriendModel> OutgoingRequests { get; }
        bool IsRefreshing { get; }

        ILobbyInviteService LobbyInvites { get; }

        event Action OnFriendsListUpdated;
        event Action<FriendModel> OnFriendStatusChanged;

        void Initialize();
        void RefreshFriends(Action onComplete = null);

        /// <summary>Resolves a user code / Steam ID to a player and sends them a friend request.</summary>
        void AddFriend(string searchIdentifier, Action<bool, string> onComplete = null);

        /// <summary>Resolves a user code / Steam ID to a player without changing any relationship (Add Friend search).</summary>
        void ResolvePlayer(string identifier, Action<PlayerLookupResult> onComplete);
        FriendRelationStatus GetRelationship(string productUserId);
        FriendRelationStatus GetRelationship(string productUserId, string displayName);

        void SendFriendRequest(string productUserId, string displayName, LoginProvider platform, Action<bool, string> onComplete = null);
        void AcceptFriendRequest(string productUserId, Action<bool, string> onComplete = null);
        void DeclineFriendRequest(string productUserId, Action<bool, string> onComplete = null);

        void RemoveFriend(string productUserId, Action<bool, string> onComplete = null);
        void BlockPlayer(string productUserId, string displayName, Action<bool, string> onComplete = null);
        void BlockFriend(FriendModel friend, Action<bool, string> onComplete = null);
        void UnblockPlayer(string productUserId, Action<bool, string> onComplete = null);

        void InviteFriend(FriendModel friend, Action<bool, string> onComplete = null);
        void InviteFriendToLobby(ProductUserId targetUserId, Action<bool, string> onComplete = null);
        void SendJoinRequest(FriendModel friend, Action<bool, string> onComplete = null);
        void AcceptJoinRequest(string productUserId, Action<bool, string> onComplete = null);
        void DeclineJoinRequest(string productUserId, Action<bool, string> onComplete = null);
        void Shutdown();
    }
}