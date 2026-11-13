using System;
using System.Collections.Generic;
using EOS.Auth;
using EOS.Core;
using EOS.Lobby;
using Epic.OnlineServices;
using UnityEngine;

namespace EOS.Friends
{
    /// <summary>
    /// Tracks peer presence heartbeats (PresencePing/PresencePong), lobby presence synchronization,
    /// and stale timeout expiry.
    /// </summary>
    internal sealed class FriendsPresenceService
    {
        private const float PeerOnlineTimeoutSeconds = 45f;
        private const float HeartbeatIntervalSeconds = 15f;

        private readonly ICrossPlayFriendsStore _friendsStore;
        private readonly IFriendSignalChannel _signalChannel;

        private readonly Dictionary<string, DateTime> _onlinePuids = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FriendPresenceState> _peerPresence = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _peerLobbyId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _peerHostAddress = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _peerJoinCode = new(StringComparer.OrdinalIgnoreCase);

        private float _lastHeartbeatTimer;
        private FriendPresenceState _lastLocalPresence = FriendPresenceState.Offline;

        public FriendsPresenceService(ICrossPlayFriendsStore friendsStore, IFriendSignalChannel signalChannel)
        {
            _friendsStore = friendsStore;
            _signalChannel = signalChannel;
        }

        public void MarkPeerSeen(string puid)
        {
            if (!string.IsNullOrEmpty(puid))
            {
                _onlinePuids[puid] = DateTime.UtcNow;
            }
        }

        public FriendPresenceState GetLocalPresenceState(bool useLobbySystem, ILobby activeLobby)
        {
            if (NetworkService.Current.IsServerActive || NetworkService.Current.IsClientConnected)
            {
                return FriendPresenceState.InMatch;
            }

            if (useLobbySystem && activeLobby != null && activeLobby.IsInLobby)
            {
                return activeLobby.HasGameStarted ? FriendPresenceState.InMatch : FriendPresenceState.InLobby;
            }

            return FriendPresenceState.Online;
        }

        public void SyncLocalPresence(bool useLobbySystem, ILobby activeLobby)
        {
            _lastLocalPresence = GetLocalPresenceState(useLobbySystem, activeLobby);
        }

        public void Tick(float deltaTime, bool useLobbySystem, ILobby activeLobby, IReadOnlyList<FriendModel> allFriends)
        {
            _lastHeartbeatTimer += deltaTime;
            FriendPresenceState currentPresence = GetLocalPresenceState(useLobbySystem, activeLobby);

            if (currentPresence != _lastLocalPresence)
            {
                _lastLocalPresence = currentPresence;
                _lastHeartbeatTimer = 0f;
                PingAllFriends(allFriends, useLobbySystem, activeLobby);
            }
            else if (_lastHeartbeatTimer >= HeartbeatIntervalSeconds)
            {
                _lastHeartbeatTimer = 0f;
                PingAllFriends(allFriends, useLobbySystem, activeLobby);
            }
        }

        public void PingFriend(string targetPuid, bool useLobbySystem, ILobby activeLobby)
        {
            if (string.IsNullOrEmpty(targetPuid) || _friendsStore.IsBlocked(targetPuid)) return;

            FriendSignal ping = BuildPresenceSignal(FriendSignalType.PresencePing, targetPuid, useLobbySystem, activeLobby);
            _signalChannel?.Send(targetPuid, ping);
        }

        public void PingAllFriends(IReadOnlyList<FriendModel> allFriends, bool useLobbySystem, ILobby activeLobby)
        {
            if (allFriends == null) return;

            for (int i = 0; i < allFriends.Count; i++)
            {
                FriendModel friend = allFriends[i];
                if (!string.IsNullOrEmpty(friend.ProductUserId))
                {
                    PingFriend(friend.ProductUserId, useLobbySystem, activeLobby);
                }
            }
        }

        public void HandlePresencePing(string senderPuid, FriendSignal signal, bool useLobbySystem, ILobby activeLobby)
        {
            RecordPeerPresence(senderPuid, signal);
            FriendSignal pong = BuildPresenceSignal(FriendSignalType.PresencePong, senderPuid, useLobbySystem, activeLobby);
            _signalChannel?.Send(senderPuid, pong);
        }

        public void HandlePresencePong(string senderPuid, FriendSignal signal)
        {
            RecordPeerPresence(senderPuid, signal);
        }

        public void UpdateLobbyPresenceInPlace(List<FriendModel> allFriends, bool useLobbySystem, ILobby activeLobby)
        {
            if (!useLobbySystem || allFriends == null) return;

            bool inLobby = activeLobby != null && activeLobby.IsInLobby;
            bool gameStarted = inLobby && activeLobby.HasGameStarted;
            var lobbyMemberPuids = CollectLobbyMemberPuids(activeLobby, inLobby);

            for (int i = 0; i < allFriends.Count; i++)
            {
                FriendModel friend = allFriends[i];
                string key = friend.ProductUserId;
                if (!string.IsNullOrEmpty(key) && lobbyMemberPuids.Contains(key))
                {
                    friend.Presence = gameStarted ? FriendPresenceState.InMatch : FriendPresenceState.InLobby;
                }
                else if (friend.Presence == FriendPresenceState.InLobby || friend.Presence == FriendPresenceState.InMatch)
                {
                    friend.Presence = ResolveFallbackPeerPresence(key);
                }
            }
        }

        public HashSet<string> CollectLobbyMemberInfo(
            ILobby activeLobby,
            bool inLobby,
            out HashSet<string> lobbyMemberNames)
        {
            lobbyMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lobbyMemberPuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!inLobby || activeLobby == null) return lobbyMemberPuids;

            List<ProductUserId> members = activeLobby.GetCurrentMemberUserIds();
            if (members == null) return lobbyMemberPuids;

            for (int m = 0; m < members.Count; m++)
            {
                if (members[m] == null) continue;

                string puidStr = members[m].ToString();
                lobbyMemberPuids.Add(puidStr);
                _onlinePuids[puidStr] = DateTime.UtcNow;

                string name = activeLobby.GetMemberDisplayName(members[m]);
                if (!string.IsNullOrWhiteSpace(name) && !name.Equals("Player", StringComparison.OrdinalIgnoreCase))
                {
                    lobbyMemberNames.Add(name);
                }
            }

            return lobbyMemberPuids;
        }

        public FriendPresenceState ResolveFriendPresence(
            string puid,
            string platformUserId,
            string displayName,
            FriendPresenceState nativeFallback,
            bool inLobby,
            bool gameStarted,
            HashSet<string> lobbyMemberPuids,
            HashSet<string> lobbyMemberNames)
        {
            if (!string.IsNullOrEmpty(puid) &&
                _peerPresence.TryGetValue(puid, out FriendPresenceState peerState) &&
                IsPeerFresh(puid))
            {
                return peerState;
            }

            if (inLobby && (
                (!string.IsNullOrEmpty(platformUserId) && lobbyMemberPuids.Contains(platformUserId)) ||
                (!string.IsNullOrEmpty(puid) && lobbyMemberPuids.Contains(puid)) ||
                (!string.IsNullOrEmpty(displayName) && lobbyMemberNames.Contains(displayName))))
            {
                return gameStarted ? FriendPresenceState.InMatch : FriendPresenceState.InLobby;
            }

            if (!string.IsNullOrEmpty(puid) && IsPeerFresh(puid))
            {
                return FriendPresenceState.Online;
            }

            return nativeFallback != FriendPresenceState.Offline ? nativeFallback : FriendPresenceState.Offline;
        }

        public void PopulatePeerLobbyInfo(FriendModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.ProductUserId)) return;

            if (_peerLobbyId.TryGetValue(model.ProductUserId, out string lobbyId))
            {
                model.LobbyId = lobbyId;
            }

            if (_peerHostAddress.TryGetValue(model.ProductUserId, out string hostAddress))
            {
                model.HostAddress = hostAddress;
            }
        }

        public void Clear()
        {
            _onlinePuids.Clear();
            _peerPresence.Clear();
            _peerLobbyId.Clear();
            _peerHostAddress.Clear();
            _peerJoinCode.Clear();
            _lastHeartbeatTimer = 0f;
            _lastLocalPresence = FriendPresenceState.Offline;
        }

        private void RecordPeerPresence(string senderPuid, FriendSignal signal)
        {
            _onlinePuids[senderPuid] = DateTime.UtcNow;
            _peerPresence[senderPuid] = (FriendPresenceState)signal.presenceState;
            if (!string.IsNullOrEmpty(signal.lobbyId)) _peerLobbyId[senderPuid] = signal.lobbyId;
            if (!string.IsNullOrEmpty(signal.hostAddress)) _peerHostAddress[senderPuid] = signal.hostAddress;
            if (!string.IsNullOrEmpty(signal.joinCode)) _peerJoinCode[senderPuid] = signal.joinCode;
        }

        private FriendSignal BuildPresenceSignal(FriendSignalType type, string targetPuid, bool useLobbySystem, ILobby activeLobby)
        {
            ILobby lobby = useLobbySystem ? activeLobby : null;
            return new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = type,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)LocalPlayerIdentity.CurrentPlatform,
                targetPuid = targetPuid,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                presenceState = (int)GetLocalPresenceState(useLobbySystem, activeLobby),
                lobbyId = lobby?.CurrentLobbyId,
                hostAddress = lobby?.CurrentHostAddress ?? ConnectManager.Instance?.LocalProductUserIdString,
                joinCode = lobby?.CurrentJoinCode
            };
        }

        private HashSet<string> CollectLobbyMemberPuids(ILobby activeLobby, bool inLobby)
        {
            var lobbyMemberPuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!inLobby || activeLobby == null) return lobbyMemberPuids;

            List<ProductUserId> members = activeLobby.GetCurrentMemberUserIds();
            if (members == null) return lobbyMemberPuids;

            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] != null)
                {
                    string puid = members[i].ToString();
                    lobbyMemberPuids.Add(puid);
                    _onlinePuids[puid] = DateTime.UtcNow;
                }
            }

            return lobbyMemberPuids;
        }

        private FriendPresenceState ResolveFallbackPeerPresence(string key)
        {
            if (string.IsNullOrEmpty(key) || !IsPeerFresh(key))
            {
                return FriendPresenceState.Offline;
            }

            return _peerPresence.TryGetValue(key, out FriendPresenceState peerState)
                ? peerState
                : FriendPresenceState.Online;
        }

        private bool IsPeerFresh(string puid)
        {
            return _onlinePuids.TryGetValue(puid, out DateTime lastSeen) &&
                (DateTime.UtcNow - lastSeen).TotalSeconds < PeerOnlineTimeoutSeconds;
        }
    }
}

