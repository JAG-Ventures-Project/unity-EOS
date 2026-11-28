using System;
using EOS.Notification;
using EOS.Core;
using System.Collections.Generic;
using Epic.OnlineServices;
using UnityEngine.Assertions;

namespace EOS.Friends
{
    /// <summary>
    /// Direct P2P invite manager. Handles peer-to-peer game invitations and joins directly
    /// via network session service without requiring an EOS Lobby.
    /// </summary>
    public class DirectP2PInviteManager : ILobbyInviteService
    {
        private readonly ICrossPlayFriendsStore _friendsStore;
        private readonly List<LobbyInviteModel> _pendingInvites = new();
        private readonly Dictionary<string, LobbyInviteModel> _invitesById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LobbyInviteModel> _invitesByInviter = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<LobbyInviteModel> PendingInvites => _pendingInvites;

        public event Action<LobbyInviteModel> OnInviteReceived;
        public event Action<string> OnInviteRemoved;

        public DirectP2PInviteManager(ICrossPlayFriendsStore friendsStore)
        {
            Assert.IsNotNull(friendsStore, "[DirectP2PInviteManager] ICrossPlayFriendsStore must not be null.");
            _friendsStore = friendsStore;
        }

        public void InviteFriendToLobby(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {
            if (targetUserId == null)
            {
                onComplete?.Invoke(false, "Target user ID is null.");
                return;
            }

            if (_friendsStore.IsBlocked(targetUserId.ToString()))
            {
                onComplete?.Invoke(false, "Cannot invite a blocked player.");
                return;
            }

            // Ensure game host is active before dispatching invite
            if (!NetworkService.Current.IsServerActive)
            {
                NetworkService.Current.StartHost();
            }

            FriendsManager.Instance.SendDirectGameInvite(targetUserId.ToString(), onComplete);
        }

        public void AcceptInvite(string inviteId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(inviteId), "[DirectP2PInviteManager] inviteId must not be null or empty.");

            if (!_invitesById.TryGetValue(inviteId, out LobbyInviteModel invite))
            {
                onComplete?.Invoke(false, "Invite not found.");
                return;
            }

            RemoveInviteInternal(invite);
            OnInviteRemoved?.Invoke(inviteId);

            if (invite.InviterUserId != null && _friendsStore.IsBlocked(invite.InviterUserId.ToString()))
            {
                onComplete?.Invoke(false, "Cannot accept an invite from a blocked player.");
                return;
            }

            if (invite.InviterUserId != null)
            {
                FriendsManager.Instance.SendDirectInviteAccepted(invite.InviterUserId.ToString());
            }

            NetworkService.Current.StartClient(invite.HostAddress);
            onComplete?.Invoke(true, null);
        }

        public void DeclineInvite(string inviteId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(inviteId), "[DirectP2PInviteManager] inviteId must not be null or empty.");

            if (_invitesById.TryGetValue(inviteId, out LobbyInviteModel invite))
            {
                RemoveInviteInternal(invite);
                OnInviteRemoved?.Invoke(inviteId);

                if (invite.InviterUserId != null)
                {
                    FriendsManager.Instance.SendDirectInviteDeclined(invite.InviterUserId.ToString());
                }
            }

            onComplete?.Invoke(true, null);
        }

        public void RemoveInviteFromPlayer(string inviterPuid)
        {
            if (string.IsNullOrEmpty(inviterPuid) || !_invitesByInviter.TryGetValue(inviterPuid, out LobbyInviteModel existing))
            {
                return;
            }

            RemoveInviteInternal(existing);
            OnInviteRemoved?.Invoke(existing.InviteId);
        }

        public void RegisterDirectInvite(LobbyInviteModel invite)
        {
            if (invite == null || string.IsNullOrEmpty(invite.InviteId)) return;

            string inviterPuid = invite.InviterUserId != null ? invite.InviterUserId.ToString() : string.Empty;
            if (!string.IsNullOrEmpty(inviterPuid) && _friendsStore.IsBlocked(inviterPuid))
            {
                return;
            }

            if (!string.IsNullOrEmpty(inviterPuid))
            {
                FriendsManager.Instance.RemoveIncomingJoinRequest(inviterPuid);

                if (_invitesByInviter.TryGetValue(inviterPuid, out LobbyInviteModel previousByInviter))
                {
                    RemoveInviteInternal(previousByInviter);
                }
            }

                if (_invitesById.TryGetValue(invite.InviteId, out LobbyInviteModel previousById))
                {
                    RemoveInviteInternal(previousById);
                }

            _pendingInvites.Add(invite);
            _invitesById[invite.InviteId] = invite;
            if (!string.IsNullOrEmpty(inviterPuid))
            {
                _invitesByInviter[inviterPuid] = invite;
            }

            OnInviteReceived?.Invoke(invite);
            NotificationManager.Show($"{invite.InviterDisplayName} invited you to play!", NotificationType.Normal);
        }

        public void ClearAllInvites()
        {
            _pendingInvites.Clear();
            _invitesById.Clear();
            _invitesByInviter.Clear();
        }

        public void Dispose()
        {
            ClearAllInvites();
        }

        private void RemoveInviteInternal(LobbyInviteModel invite)
        {
                _invitesById.Remove(invite.InviteId);
            if (invite.InviterUserId != null)
            {
                    string inviterPuid = invite.InviterUserId.ToString();
                if (_invitesByInviter.TryGetValue(inviterPuid, out LobbyInviteModel current) && current == invite)
                {
                    _invitesByInviter.Remove(inviterPuid);
                }
            }
            _pendingInvites.Remove(invite);
        }
    }
}
