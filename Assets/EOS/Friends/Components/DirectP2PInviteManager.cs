using System;
using EOS.Notification;
using EOS.P2P;
using System.Collections.Generic;
using Epic.OnlineServices;
using UnityEngine.Assertions;

namespace EOS.Friends
{
    /// <summary>
    /// Direct P2P invite manager. Handles peer-to-peer game invitations and joins directly
    /// via Mirror EOS P2P without requiring an EOS Lobby.
    /// </summary>
    public class DirectP2PInviteManager : ILobbyInviteService
    {
        private readonly ICrossPlayFriendsStore _friendsStore;
        private readonly Dictionary<string, LobbyInviteModel> _pendingInvites = new();

        public IReadOnlyList<LobbyInviteModel> PendingInvites => new List<LobbyInviteModel>(_pendingInvites.Values);

        public event Action<LobbyInviteModel> OnInviteReceived;
        public event Action<string> OnInviteRemoved;

        public DirectP2PInviteManager(ICrossPlayFriendsStore friendsStore = null)
        {
            _friendsStore = friendsStore;
        }

        public void InviteFriendToLobby(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {
            if (targetUserId == null)
            {
                onComplete?.Invoke(false, "Target user ID is null.");
                return;
            }

            if (_friendsStore != null && _friendsStore.IsBlocked(targetUserId.ToString()))
            {
                onComplete?.Invoke(false, "Cannot invite a blocked player.");
                return;
            }

            // Ensure Mirror game host is active before dispatching invite
            if (EOSNetworkManager.Instance != null && !Mirror.NetworkServer.active)
            {
                EOSNetworkManager.Instance.StartEosHost();
            }

            if (FriendsManager.Instance != null)
            {
                FriendsManager.Instance.SendDirectGameInvite(targetUserId.ToString(), onComplete);
            }
            else
            {
                onComplete?.Invoke(false, "FriendsManager unavailable.");
            }
        }

        public void AcceptInvite(string inviteId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(inviteId), "[DirectP2PInviteManager] inviteId must not be null or empty.");

            if (!_pendingInvites.TryGetValue(inviteId, out LobbyInviteModel invite))
            {
                onComplete?.Invoke(false, "Invite not found.");
                return;
            }

            _pendingInvites.Remove(inviteId);
            OnInviteRemoved?.Invoke(inviteId);

            if (invite.InviterUserId != null && _friendsStore != null && _friendsStore.IsBlocked(invite.InviterUserId.ToString()))
            {
                onComplete?.Invoke(false, "Cannot accept an invite from a blocked player.");
                return;
            }

            FriendsOverlayManager.Instance?.PersistForGameScene();

            if (invite.InviterUserId != null && FriendsManager.Instance != null)
            {
                FriendsManager.Instance.SendDirectInviteAccepted(invite.InviterUserId.ToString());
            }

            EOSNetworkManager.Instance?.StartEosClient(invite.HostAddress);
            onComplete?.Invoke(true, null);
        }

        public void DeclineInvite(string inviteId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(inviteId), "[DirectP2PInviteManager] inviteId must not be null or empty.");

            if (_pendingInvites.TryGetValue(inviteId, out LobbyInviteModel invite))
            {
                _pendingInvites.Remove(inviteId);
                OnInviteRemoved?.Invoke(inviteId);

                if (invite.InviterUserId != null && FriendsManager.Instance != null)
                {
                    FriendsManager.Instance.SendDirectInviteDeclined(invite.InviterUserId.ToString());
                }
            }

            onComplete?.Invoke(true, null);
        }

        public void RegisterDirectInvite(LobbyInviteModel invite)
        {
            if (invite == null || string.IsNullOrEmpty(invite.InviteId)) return;

            if (invite.InviterUserId != null && _friendsStore != null && _friendsStore.IsBlocked(invite.InviterUserId.ToString()))
            {
                return;
            }

            _pendingInvites[invite.InviteId] = invite;
            OnInviteReceived?.Invoke(invite);
            NotificationManager.Show($"{invite.InviterDisplayName} invited you to play!", NotificationType.Normal);
        }

        public void ClearAllInvites()
        {
            _pendingInvites.Clear();
        }

        public void Dispose()
        {
            _pendingInvites.Clear();
        }
    }
}
