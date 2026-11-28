using System;
using EOS.Notification;
using EOS.Core;
using EOS.Friends;
using System.Collections.Generic;
using Epic.OnlineServices;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS.Lobby
{
    /// <summary>
    /// Manages lobby invitations, concurrency batching for multi-friend invites, and auto-room crea /// 
    /// </summary>
    public class LobbyInviteManager : ILobbyInviteService
    {
        private readonly ILobby _lobby;
        private readonly ICrossPlayFriendsStore _friendsStore;

        private readonly List<LobbyInviteModel> _pendingInvites = new();
        private readonly Dictionary<string, LobbyInviteModel> _invitesById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LobbyInviteModel> _invitesByInviter = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PendingOutgoingInvite> _pendingOutgoingQueue = new();
        private bool _isCreatingLobby;

        private string _pendingAcceptInviteId;
        private Action<bool, string> _pendingAcceptCallback;

        public IReadOnlyList<LobbyInviteModel> PendingInvites => _pendingInvites;

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
            Assert.IsNotNull(_friendsStore, "[LobbyInviteManager] ICrossPlayFriendsStore must not be null.");

            _lobby.OnLobbyInviteReceived += HandleLobbyInviteReceived;
        }

        /// <summary>
        /// Invites a friend to the lobby. If the local player is not in a lobby, automatically creates a single lobby
        /// and batches all concurrent outgoing friend invites so they all join the same room.
        /// </summary>
        public void InviteFriendToLobby(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {

            if (_friendsStore.IsBlocked(targetUserId.ToString()))
            {
                onComplete?.Invoke(false, "Cannot invite a blocked player.");
                return;
            }

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
            if (NetworkService.Current.IsServerActive)
            {
                _lobby.AllowJoinInProgress = true;
                _lobby.StartGame();
            }

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

                if (_invitesById.TryGetValue(inviteId, out LobbyInviteModel invite))
                {
                    if (invite.InviterUserId != null && _friendsStore.IsBlocked(invite.InviterUserId.ToString()))
                    {
                        DeclineInvite(inviteId, null);
                        onComplete?.Invoke(false, "Cannot accept an invite from a blocked player.");
                        return;
                    }

                    if (invite.IsFull)
                    {
                        onComplete?.Invoke(false, "Lobby is full.");
                        return;
                    }
                }

            // Flush and save cloud cross-play data before switching lobbies
            _friendsStore.Save();

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

            bool removeInvite = success ||
                (error != null &&
                 (error.IndexOf("NotFound", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  error.IndexOf("already started", StringComparison.OrdinalIgnoreCase) >= 0));
            if (removeInvite && !string.IsNullOrEmpty(inviteId))
            {
                RemoveInviteById(inviteId);
                OnInviteRemoved?.Invoke(inviteId);
            }

            // if joining an in-progress game match, auto-connect client directly to host if not already connecting
            if (success && _lobby.HasGameStarted && !NetworkService.Current.IsClientActive && !NetworkService.Current.IsClientConnected && !string.IsNullOrEmpty(error))
            {
                NetworkService.Current.StartClient(error);
            }

            callback?.Invoke(success, error);
        }

        public void DeclineInvite(string inviteId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(inviteId), "[LobbyInviteManager] inviteId must not be null or empty.");

            RemoveInviteById(inviteId);
            OnInviteRemoved?.Invoke(inviteId);
            _lobby.RejectInvite(inviteId, onComplete);
        }

        public void RemoveInviteFromPlayer(string inviterPuid)
        {
            if (string.IsNullOrEmpty(inviterPuid) || !_invitesByInviter.TryGetValue(inviterPuid, out LobbyInviteModel existing))
            {
                return;
            }

            RemoveInviteInternal(existing);
            if (!string.Equals(existing.InviteId, _pendingExecuteInviteId, StringComparison.Ordinal))
            {
                _lobby.RejectInvite(existing.InviteId, null);
            }
            OnInviteRemoved?.Invoke(existing.InviteId);
        }

        public void ClearAllInvites()
        {
            _pendingInvites.Clear();
            _invitesById.Clear();
            _invitesByInviter.Clear();
        }

        private void HandleLobbyInviteReceived(LobbyInviteCallbackData data)
        {
            if (data == null || string.IsNullOrEmpty(data.InviteId)) return;

            if (_lobby.IsInLobby && _lobby.CurrentLobbyId == data.LobbyId)
            {
                _lobby.RejectInvite(data.InviteId, null);
                return;
            }

            string inviterPuidStr = data.InviterUserId != null ? data.InviterUserId.ToString() : string.Empty;

            // Blocked user isolation: Automatically reject and suppress any invites from blocked users
                if (!string.IsNullOrEmpty(inviterPuidStr) && _friendsStore.IsBlocked(inviterPuidStr))
                {
                    Debug.Log($"[LobbyInviteManager] Suppressed and rejected lobby invite from blocked player: {data.InviterUserId}");
                    _lobby.RejectInvite(data.InviteId, null);
                    return;
                }

            if (FriendsManager.Instance.IsPendingJoinRequestTarget(inviterPuidStr))
            {
                FriendsManager.Instance.ClearPendingJoinRequest(inviterPuidStr);
                FriendsManager.Instance.RemoveIncomingJoinRequest(inviterPuidStr);
                RemoveInviteFromPlayer(inviterPuidStr);
                NotificationManager.Show("Join request accepted! Joining lobby...", NotificationType.Normal);
                AcceptInvite(data.InviteId, null);
                return;
            }

            string inviterName = data.InviterDisplayName;
            if (string.IsNullOrWhiteSpace(inviterName) || inviterName == "Unknown Player" || inviterName == "Player")
            {
                    if (!string.IsNullOrEmpty(inviterPuidStr) && _friendsStore.TryGetFriend(inviterPuidStr, out CrossPlayFriendEntry friend))
                    {
                        inviterName = friend.displayName;
                    }
            }

            if (string.IsNullOrWhiteSpace(inviterName))
            {
                inviterName = "Player";
            }

            var inviteModel = new LobbyInviteModel
            {
                InviteId = data.InviteId,
                LobbyId = data.LobbyId,
                HostAddress = data.HostAddress,
                JoinCode = data.JoinCode,
                InviterDisplayName = inviterName,
                InviterUserId = data.InviterUserId,
                AvailableSlots = data.AvailableSlots,
                MaxMembers = data.MaxMembers,
                ReceivedAt = DateTime.UtcNow
            };

                if (!string.IsNullOrEmpty(inviterPuidStr))
                {
                        FriendsManager.Instance.RemoveIncomingJoinRequest(inviterPuidStr);

                    if (_invitesByInviter.TryGetValue(inviterPuidStr, out LobbyInviteModel previousByInviter))
                    {
                        RemoveInviteInternal(previousByInviter);
                        if (previousByInviter.InviteId != data.InviteId)
                        {
                            _lobby.RejectInvite(previousByInviter.InviteId, null);
                        }
                    }
                }

                    if (_invitesById.TryGetValue(data.InviteId, out LobbyInviteModel previousById))
                    {
                        RemoveInviteInternal(previousById);
                    }

            _pendingInvites.Add(inviteModel);
            _invitesById[data.InviteId] = inviteModel;
            if (!string.IsNullOrEmpty(inviterPuidStr))
            {
                _invitesByInviter[inviterPuidStr] = inviteModel;
            }

            OnInviteReceived?.Invoke(inviteModel);

            NotificationManager.Show($"{inviteModel.InviterDisplayName} invited you to a room.", NotificationType.Normal);
        }

        public void Dispose()
        {
            _lobby.OnLobbyInviteReceived -= HandleLobbyInviteReceived;
            ClearAllInvites();
            _pendingOutgoingQueue.Clear();
        }

        private void RemoveInviteById(string inviteId)
        {
                if (_invitesById.TryGetValue(inviteId, out LobbyInviteModel invite))
                {
                    RemoveInviteInternal(invite);
                }
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