using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS
{
    // AI implementation: Manages lobby invitations, concurrency batching for multi-friend invites, and auto-room creation
    public class LobbyInviteManager : ILobbyInviteService
    {
        private readonly ILobby _lobby;
        private readonly ICrossPlayFriendsStore _friendsStore;

        private readonly Dictionary<string, LobbyInviteModel> _pendingInvites = new();
        private readonly List<PendingOutgoingInvite> _pendingOutgoingQueue = new();
        private bool _isCreatingLobby;

        private string _pendingAcceptInviteId;
        private Action<bool, string> _pendingAcceptCallback;

        public IReadOnlyList<LobbyInviteModel> PendingInvites => new List<LobbyInviteModel>(_pendingInvites.Values);

        public event Action<LobbyInviteModel> OnInviteReceived;
        public event Action<string> OnInviteRemoved;

        private class PendingOutgoingInvite
        {
            public ProductUserId TargetUserId;
            public Action<bool, string> OnComplete;
        }

        public LobbyInviteManager(ILobby lobby, ICrossPlayFriendsStore friendsStore)
        {
            _lobby = lobby;
            _friendsStore = friendsStore;
            Assert.IsNotNull(_lobby, "[LobbyInviteManager] ILobby must not be null.");

            _lobby.OnLobbyInviteReceived += HandleLobbyInviteReceived;
        }

        /// <summary>
        /// Invites a friend to the lobby. If the local player is not in a lobby, automatically creates a single lobby
        /// and batches all concurrent outgoing friend invites so they all join the same room.
        /// </summary>
        public void InviteFriendToLobby(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {
            Assert.IsNotNull(targetUserId, "[LobbyInviteManager] targetUserId must not be null.");

            if (_lobby.IsInLobby)
            {
                _lobby.SendInvite(targetUserId, onComplete);
                return;
            }

            // Not currently in a lobby: queue outgoing invite
            _pendingOutgoingQueue.Add(new PendingOutgoingInvite
            {
                TargetUserId = targetUserId,
                OnComplete = onComplete
            });

            if (!_isCreatingLobby)
            {
                _isCreatingLobby = true;
                _lobby.CreateLobby(4, HandleCreateLobbyForInvitesCallback);
            }
        }

        private void HandleCreateLobbyForInvitesCallback(bool success, string error)
        {
            _isCreatingLobby = false;

            if (!success)
            {
                for (int i = 0; i < _pendingOutgoingQueue.Count; i++)
                {
                    _pendingOutgoingQueue[i].OnComplete?.Invoke(false, $"Failed to create lobby: {error}");
                }
                _pendingOutgoingQueue.Clear();
                return;
            }

            // Lobby successfully created; dispatch invites to all queued friends
            for (int i = 0; i < _pendingOutgoingQueue.Count; i++)
            {
                PendingOutgoingInvite item = _pendingOutgoingQueue[i];
                _lobby.SendInvite(item.TargetUserId, item.OnComplete);
            }

            _pendingOutgoingQueue.Clear();
        }

        /// <summary>
        /// Accepts a received lobby invitation. If the player is already in another lobby, leaves the current lobby,
        /// flushes/saves pending data, and joins the new room.
        /// </summary>
        public void AcceptInvite(string inviteId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(inviteId), "[LobbyInviteManager] inviteId must not be null or empty.");

            if (_pendingInvites.TryGetValue(inviteId, out LobbyInviteModel invite) && invite.IsFull)
            {
                onComplete?.Invoke(false, "Lobby is full.");
                return;
            }

            // Flush and save cloud cross-play data before switching lobbies
            _friendsStore?.Save();

            if (_lobby.IsInLobby)
            {
                _pendingAcceptInviteId = inviteId;
                _pendingAcceptCallback = onComplete;
                _lobby.LeaveLobby(HandleLeaveCurrentLobbyBeforeAccepting);
                return;
            }

            ExecuteJoinInvite(inviteId, onComplete);
        }

        private void HandleLeaveCurrentLobbyBeforeAccepting(bool success)
        {
            string inviteId = _pendingAcceptInviteId;
            Action<bool, string> callback = _pendingAcceptCallback;
            _pendingAcceptInviteId = null;
            _pendingAcceptCallback = null;

            ExecuteJoinInvite(inviteId, callback);
        }

        private string _pendingExecuteInviteId;
        private Action<bool, string> _pendingExecuteCallback;

        private void ExecuteJoinInvite(string inviteId, Action<bool, string> onComplete)
        {
            _pendingExecuteInviteId = inviteId;
            _pendingExecuteCallback = onComplete;
            _lobby.JoinLobbyByInvite(inviteId, HandleJoinByInviteCompleted);
        }

        private void HandleJoinByInviteCompleted(bool success, string error)
        {
            string inviteId = _pendingExecuteInviteId;
            Action<bool, string> callback = _pendingExecuteCallback;
            _pendingExecuteInviteId = null;
            _pendingExecuteCallback = null;

            if (success && !string.IsNullOrEmpty(inviteId))
            {
                _pendingInvites.Remove(inviteId);
                OnInviteRemoved?.Invoke(inviteId);
            }

            callback?.Invoke(success, error);
        }

        public void DeclineInvite(string inviteId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(inviteId), "[LobbyInviteManager] inviteId must not be null or empty.");

            _pendingInvites.Remove(inviteId);
            OnInviteRemoved?.Invoke(inviteId);
            _lobby.RejectInvite(inviteId, onComplete);
        }

        public void ClearAllInvites()
        {
            _pendingInvites.Clear();
        }

        private void HandleLobbyInviteReceived(LobbyInviteCallbackData data)
        {
            if (data == null || string.IsNullOrEmpty(data.InviteId)) return;

            var inviteModel = new LobbyInviteModel
            {
                InviteId = data.InviteId,
                LobbyId = data.LobbyId,
                HostAddress = data.HostAddress,
                JoinCode = data.JoinCode,
                InviterDisplayName = !string.IsNullOrEmpty(data.InviterDisplayName) ? data.InviterDisplayName : "Unknown Player",
                InviterUserId = data.InviterUserId,
                AvailableSlots = data.AvailableSlots,
                MaxMembers = data.MaxMembers,
                ReceivedAt = DateTime.UtcNow
            };

            _pendingInvites[data.InviteId] = inviteModel;
            OnInviteReceived?.Invoke(inviteModel);

            NotificationManager.Show($"{inviteModel.InviterDisplayName} invited you to a room.", NotificationType.Normal);
        }

        public void Dispose()
        {
            if (_lobby != null)
            {
                _lobby.OnLobbyInviteReceived -= HandleLobbyInviteReceived;
            }

            _pendingInvites.Clear();
            _pendingOutgoingQueue.Clear();
        }
    }
}
