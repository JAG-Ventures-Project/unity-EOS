using System;
using System.Collections.Generic;
using EOS.Auth;
using EOS.Core;
using EOS.Lobby;
using EOS.Notification;
using Epic.OnlineServices;

namespace EOS.Friends
{
    /// <summary>
    /// Owns lobby/direct-P2P game invites, incoming/outgoing join requests, and session membership checks.
    /// </summary>
    internal sealed class FriendInviteService
    {
        private readonly ICrossPlayFriendsStore _friendsStore;
        private readonly IFriendSignalChannel _signalChannel;
        private readonly PlayerLookupService _lookupService;
        private readonly FriendsPresenceService _presenceService;
        private readonly Func<bool> _getUseLobbySystem;
        private readonly Func<ILobby> _getActiveLobby;
        private readonly Func<ILobbyInviteService> _getLobbyInviteService;
        private readonly Action _notifyListChanged;

        private readonly Dictionary<string, FriendModel> _incomingJoinRequests = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pendingOutgoingJoinRequests = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<(string TargetPuid, FriendSignal Signal, Action<bool, string> OnComplete)> _pendingAcceptedJoins = new();
        private string _pendingJoinLobbyCode;

        public void CopyIncomingJoinRequestsTo(List<FriendModel> target)
        {
            foreach (FriendModel joinReq in _incomingJoinRequests.Values)
            {
                target.Add(joinReq);
            }
        }

        public FriendInviteService(
            ICrossPlayFriendsStore friendsStore,
            IFriendSignalChannel signalChannel,
            PlayerLookupService lookupService,
            FriendsPresenceService presenceService,
            Func<bool> getUseLobbySystem,
            Func<ILobby> getActiveLobby,
            Func<ILobbyInviteService> getLobbyInviteService,
            Action notifyListChanged)
        {
            _friendsStore = friendsStore;
            _signalChannel = signalChannel;
            _lookupService = lookupService;
            _presenceService = presenceService;
            _getUseLobbySystem = getUseLobbySystem;
            _getActiveLobby = getActiveLobby;
            _getLobbyInviteService = getLobbyInviteService;
            _notifyListChanged = notifyListChanged;
        }

        public void InviteFriend(FriendModel friend, Action<bool, string> onComplete = null)
        {
            if (friend == null)
            {
                onComplete?.Invoke(false, "Friend parameter cannot be null.");
                return;
            }

            if (IsPlayerInSameLobbyOrGame(friend))
            {
                onComplete?.Invoke(false, "Already in the same lobby or game.");
                return;
            }

            ProductUserId puid = !string.IsNullOrEmpty(friend.ProductUserId) ? ProductUserId.FromString(friend.ProductUserId) : null;
            if (puid != null && puid.IsValid())
            {
                InviteFriendToLobby(puid, onComplete);
                return;
            }

            if (!string.IsNullOrEmpty(friend.PlatformUserId))
            {
                _lookupService.ResolveExternalPlatformPuid(friend, (success, mapped) =>
                {
                    if (success && mapped != null && mapped.IsValid())
                    {
                        InviteFriendToLobby(mapped, onComplete);
                    }
                    else
                    {
                        onComplete?.Invoke(false, "This friend has not logged into the game yet.");
                    }
                });
                return;
            }

            onComplete?.Invoke(false, "This friend does not have an active cross-play ID to invite.");
        }

        public void InviteFriendToLobby(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {
            string targetPuid = targetUserId.ToString();
            if (_friendsStore.IsBlocked(targetPuid))
            {
                onComplete?.Invoke(false, "Cannot invite a blocked player.");
                return;
            }

            RemoveIncomingJoinRequest(targetPuid);
            _getLobbyInviteService()?.InviteFriendToLobby(targetUserId, onComplete);
        }

        public void SendDirectGameInvite(string targetPuid, Action<bool, string> onComplete = null)
        {
            if (string.IsNullOrEmpty(targetPuid))
            {
                onComplete?.Invoke(false, "Target player ID is invalid.");
                return;
            }

            if (_friendsStore.IsBlocked(targetPuid))
            {
                onComplete?.Invoke(false, "Cannot invite a blocked player.");
                return;
            }

            RemoveIncomingJoinRequest(targetPuid);

            if (!NetworkService.Current.IsServerActive)
            {
                NetworkService.Current.StartHost();
            }

            string myPuid = ConnectManager.Instance?.LocalProductUserIdString;
            if (string.IsNullOrEmpty(myPuid))
            {
                onComplete?.Invoke(false, "Local user ProductUserId is unavailable.");
                return;
            }

            FriendSignal signal = CreateInviteSignal(FriendSignalType.GameInvite, targetPuid);
            signal.presenceState = (int)FriendPresenceState.InMatch;
            signal.hostAddress = myPuid;

            _signalChannel?.Send(targetPuid, signal);
            NotificationManager.Show("Game invite sent!", NotificationType.Normal);
            onComplete?.Invoke(true, "Game invite sent!");
        }

        public void SendDirectInviteAccepted(string targetPuid)
        {
            if (string.IsNullOrEmpty(targetPuid)) return;
            _signalChannel?.Send(targetPuid, CreateInviteSignal(FriendSignalType.GameInviteAccepted, targetPuid));
        }

        public void SendDirectInviteDeclined(string targetPuid)
        {
            if (string.IsNullOrEmpty(targetPuid)) return;
            _signalChannel?.Send(targetPuid, CreateInviteSignal(FriendSignalType.GameInviteDeclined, targetPuid));
        }

        public bool IsPendingJoinRequestTarget(string puid)
            => !string.IsNullOrEmpty(puid) && _pendingOutgoingJoinRequests.Contains(puid);

        public void ClearPendingJoinRequest(string puid)
        {
            if (!string.IsNullOrEmpty(puid)) _pendingOutgoingJoinRequests.Remove(puid);
        }

        public void SendJoinRequest(FriendModel friend, Action<bool, string> onComplete = null)
        {
            if (friend == null)
            {
                onComplete?.Invoke(false, "Friend cannot be null.");
                return;
            }

            if (IsPlayerInSameLobbyOrGame(friend))
            {
                onComplete?.Invoke(false, "Already in the same lobby or game.");
                return;
            }

            // Auto-join if a pending invite from this friend already exists
            ILobbyInviteService inviteService = _getLobbyInviteService();
            LobbyInviteModel pendingInvite = GetPendingInviteForFriend(friend);
            if (pendingInvite != null && inviteService != null)
            {
                inviteService.AcceptInvite(pendingInvite.InviteId, onComplete);
                return;
            }

            string targetPuid = friend.ProductUserId;
            if (string.IsNullOrEmpty(targetPuid))
            {
                if (!string.IsNullOrEmpty(friend.PlatformUserId))
                {
                    _lookupService.ResolveExternalPlatformPuid(friend, (success, mapped) =>
                    {
                        if (success && mapped != null && mapped.IsValid())
                        {
                            SendJoinRequest(friend, onComplete);
                        }
                        else
                        {
                            onComplete?.Invoke(false, "Friend's cross-play ID could not be resolved.");
                        }
                    });
                    return;
                }

                onComplete?.Invoke(false, "Cannot send join request: Friend's cross-play ID is not available.");
                return;
            }

            _pendingOutgoingJoinRequests.Add(targetPuid);

            FriendSignal signal = CreateInviteSignal(FriendSignalType.JoinRequest, targetPuid);
            _signalChannel?.Send(targetPuid, signal);
            onComplete?.Invoke(true, "Join request sent!");
        }

        public void AcceptJoinRequest(string productUserId, Action<bool, string> onComplete = null)
        {
            _incomingJoinRequests.Remove(productUserId);

            bool useLobby = _getUseLobbySystem();
            ILobby activeLobby = _getActiveLobby();

            if (!useLobby)
            {
                if (!NetworkService.Current.IsServerActive)
                {
                    NetworkService.Current.StartHost();
                }

                FriendSignal directSignal = CreateInviteSignal(FriendSignalType.JoinRequestAccepted, productUserId);
                directSignal.presenceState = (int)FriendPresenceState.InMatch;
                directSignal.hostAddress = ConnectManager.Instance?.LocalProductUserIdString;

                _signalChannel?.Send(productUserId, directSignal);
                _notifyListChanged.Invoke();
                onComplete?.Invoke(true, "Join request accepted!");
                return;
            }

            FriendSignal signal = CreateInviteSignal(FriendSignalType.JoinRequestAccepted, productUserId);
            signal.presenceState = (int)_presenceService.GetLocalPresenceState(useLobby, activeLobby);
            signal.lobbyId = activeLobby?.CurrentLobbyId;
            signal.hostAddress = activeLobby?.CurrentHostAddress ?? ConnectManager.Instance?.LocalProductUserIdString;
            signal.joinCode = activeLobby?.CurrentJoinCode;

            ProductUserId targetPuidObj = ProductUserId.FromString(productUserId);
            if (targetPuidObj != null && targetPuidObj.IsValid() && activeLobby != null && activeLobby.IsInLobby)
            {
                // Dispatch EOS invite (and commit mid-match whitelist) before signaling acceptance so the joiner is authorized on EOS
                _pendingAcceptedJoins.Enqueue((productUserId, signal, onComplete));
                _notifyListChanged.Invoke();
                activeLobby.SendInvite(targetPuidObj, HandleAcceptJoinInviteSent);
                return;
            }

            _signalChannel?.Send(productUserId, signal);
            _notifyListChanged.Invoke();
            onComplete?.Invoke(true, "Join request accepted!");
        }

        private void HandleAcceptJoinInviteSent(bool success, string error)
        {
            if (_pendingAcceptedJoins.Count == 0) return;

            var (targetPuid, signal, onComplete) = _pendingAcceptedJoins.Dequeue();
            if (!success)
            {
                onComplete?.Invoke(false, error);
                return;
            }

            _signalChannel?.Send(targetPuid, signal);
            onComplete?.Invoke(true, "Join request accepted!");
        }

        public void DeclineJoinRequest(string productUserId, Action<bool, string> onComplete = null)
        {
            _incomingJoinRequests.Remove(productUserId);

            FriendSignal signal = CreateInviteSignal(FriendSignalType.JoinRequestDeclined, productUserId);
            _signalChannel?.Send(productUserId, signal);

            _notifyListChanged.Invoke();
            onComplete?.Invoke(true, "Join request declined.");
        }

        public bool IsPlayerInSameLobbyOrGame(FriendModel friend)
        {
            if (friend == null) return false;

            ILobby lobby = _getActiveLobby();
            if (lobby != null && lobby.IsInLobby)
            {
                List<ProductUserId> members = lobby.GetCurrentMemberUserIds();
                if (members != null)
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        if (members[i] == null) continue;
                        string mPuid = members[i].ToString();

                        if (!string.IsNullOrEmpty(friend.ProductUserId) &&
                            string.Equals(mPuid, friend.ProductUserId, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (!string.IsNullOrEmpty(friend.DisplayName) &&
                            string.Equals(lobby.GetMemberDisplayName(members[i]), friend.DisplayName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            if (NetworkService.Current.IsServerActive || NetworkService.Current.IsClientConnected)
            {
                if (NetworkService.Current.IsPlayerInSession(friend.ProductUserId, friend.DisplayName))
                {
                    return true;
                }
            }

            return false;
        }

        public LobbyInviteModel GetPendingInviteForFriend(FriendModel friend)
        {
            ILobbyInviteService inviteService = _getLobbyInviteService();
            if (friend == null || inviteService == null) return null;

            var invites = inviteService.PendingInvites;
            if (invites == null || invites.Count == 0) return null;

            for (int i = 0; i < invites.Count; i++)
            {
                LobbyInviteModel inv = invites[i];
                if (inv == null) continue;

                if (!string.IsNullOrEmpty(friend.ProductUserId) && inv.InviterUserId != null &&
                    string.Equals(friend.ProductUserId, inv.InviterUserId.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return inv;
                }

                if (!string.IsNullOrEmpty(friend.DisplayName) && !string.IsNullOrEmpty(inv.InviterDisplayName) &&
                    string.Equals(friend.DisplayName, inv.InviterDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    return inv;
                }
            }

            return null;
        }

        public void RemoveIncomingJoinRequest(string senderPuid)
        {
            if (!string.IsNullOrEmpty(senderPuid) && _incomingJoinRequests.Remove(senderPuid))
            {
                _notifyListChanged.Invoke();
            }
        }

        public void HandleInviteSignal(string senderPuid, FriendSignal signal)
        {
            switch (signal.type)
            {
                case FriendSignalType.JoinRequest:
                    _getLobbyInviteService()?.RemoveInviteFromPlayer(senderPuid);
                    NotificationManager.Show($"{signal.senderName} requested to join your match!", NotificationType.Normal);
                    _incomingJoinRequests[senderPuid] = new FriendModel
                    {
                        ProductUserId = senderPuid,
                        DisplayName = signal.senderName,
                        Platform = (LoginProvider)signal.senderPlatform,
                        Status = FriendRelationStatus.PendingIncoming,
                        Presence = FriendPresenceState.Online,
                        IsJoinRequest = true
                    };
                    _notifyListChanged.Invoke();
                    break;

                case FriendSignalType.JoinRequestAccepted:
                    HandleJoinRequestAccepted(senderPuid, signal);
                    break;

                case FriendSignalType.JoinRequestDeclined:
                    NotificationManager.Show($"{signal.senderName} declined your join request.", NotificationType.Warning);
                    break;

                case FriendSignalType.GameInvite:
                    HandleGameInviteSignal(senderPuid, signal);
                    break;

                case FriendSignalType.GameInviteAccepted:
                    NotificationManager.Show($"{signal.senderName} accepted your game invite and is joining!", NotificationType.Normal);
                    break;

                case FriendSignalType.GameInviteDeclined:
                    NotificationManager.Show($"{signal.senderName} declined your game invite.", NotificationType.Warning);
                    break;
            }
        }

        public void Clear()
        {
            _incomingJoinRequests.Clear();
            _pendingOutgoingJoinRequests.Clear();
            _pendingAcceptedJoins.Clear();
            _pendingJoinLobbyCode = null;
        }

        private void HandleJoinRequestAccepted(string senderPuid, FriendSignal signal)
        {
            NotificationManager.Show($"{signal.senderName} accepted your join request! Joining...", NotificationType.Normal);
            ClearPendingJoinRequest(senderPuid);

            bool useLobby = _getUseLobbySystem();
            ILobby activeLobby = _getActiveLobby();

            if (!useLobby)
            {
                if (!string.IsNullOrEmpty(signal.hostAddress))
                {
                    NetworkService.Current.StartClient(signal.hostAddress);
                }
            }
            else if (!string.IsNullOrEmpty(signal.joinCode) && activeLobby != null)
            {
                if (activeLobby.IsInLobby)
                {
                    _pendingJoinLobbyCode = signal.joinCode;
                    activeLobby.LeaveLobby(HandleLeaveBeforeJoiningAcceptedLobby);
                }
                else
                {
                    activeLobby.JoinLobbyByCode(signal.joinCode);
                }
            }
            else if (!string.IsNullOrEmpty(signal.hostAddress))
            {
                NetworkService.Current.StartClient(signal.hostAddress);
            }
        }

        private void HandleLeaveBeforeJoiningAcceptedLobby(bool success)
        {
            string code = _pendingJoinLobbyCode;
            _pendingJoinLobbyCode = null;
            ILobby activeLobby = _getActiveLobby();
            if (!string.IsNullOrEmpty(code) && activeLobby != null)
            {
                activeLobby.JoinLobbyByCode(code);
            }
        }

        private void HandleGameInviteSignal(string senderPuid, FriendSignal signal)
        {
            if (_friendsStore.IsBlocked(senderPuid)) return;

            string inviterName = string.IsNullOrWhiteSpace(signal.senderName) ? "Player" : signal.senderName;
            if (inviterName == "Player" && _friendsStore.TryGetFriend(senderPuid, out CrossPlayFriendEntry friend))
            {
                inviterName = friend.displayName;
            }

            var directInvite = new LobbyInviteModel
            {
                InviteId = string.IsNullOrEmpty(signal.id) ? Guid.NewGuid().ToString("N") : signal.id,
                LobbyId = null,
                HostAddress = !string.IsNullOrEmpty(signal.hostAddress) ? signal.hostAddress : senderPuid,
                JoinCode = null,
                InviterDisplayName = inviterName,
                InviterUserId = ProductUserId.FromString(senderPuid),
                AvailableSlots = 4,
                MaxMembers = 4,
                ReceivedAt = DateTime.UtcNow
            };

            (_getLobbyInviteService() as DirectP2PInviteManager)?.RegisterDirectInvite(directInvite);
            _notifyListChanged.Invoke();
        }

        private static FriendSignal CreateInviteSignal(FriendSignalType type, string targetPuid)
        {
            return new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = type,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)LocalPlayerIdentity.CurrentPlatform,
                targetPuid = targetPuid,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }
    }
}

