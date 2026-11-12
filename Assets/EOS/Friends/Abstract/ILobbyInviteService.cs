using System;
using System.Collections.Generic;
using Epic.OnlineServices;

namespace EOS.Friends
{
    // Service contract for managing incoming and outgoing lobby invitations
    public interface ILobbyInviteService : IDisposable
    {
        IReadOnlyList<LobbyInviteModel> PendingInvites { get; }

        event Action<LobbyInviteModel> OnInviteReceived;
        event Action<string> OnInviteRemoved;

        void InviteFriendToLobby(ProductUserId targetUserId, Action<bool, string> onComplete = null);
        void AcceptInvite(string inviteId, Action<bool, string> onComplete = null);
        void DeclineInvite(string inviteId, Action<bool, string> onComplete = null);
        void ClearAllInvites();
    }
}
