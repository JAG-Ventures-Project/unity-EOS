using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

namespace EOS.Lobby
{
    /// <summary>
    /// Owns lobby/member attributes (ready status, match state, display names) and member roster queries.
    /// </summary>
    internal sealed class LobbyAttributeService
    {
        private readonly LobbyInterface _lobbyHandle;
        private readonly LobbySessionState _state;
        private readonly Action<Action<bool, string>, bool, string> _completeOperation;
        private readonly Action<bool> _notifyReadyStatusChanged;

        public LobbyAttributeService(
            LobbyInterface lobbyHandle,
            LobbySessionState state,
            Action<Action<bool, string>, bool, string> completeOperation,
            Action<bool> notifyReadyStatusChanged)
        {
            _lobbyHandle = lobbyHandle;
            _state = state;
            _completeOperation = completeOperation;
            _notifyReadyStatusChanged = notifyReadyStatusChanged;
        }

        public void SetInitialLobbyAttributes(
            string lobbyId,
            string joinCode,
            Action onAppliedSuccess,
            Action<bool, string> onComplete)
        {
            if (!TryCreateModification(lobbyId, out LobbyModification modHandle, onComplete))
                return;

            AddPublicLobbyAttribute(modHandle, LobbyManager.JoinCodeKey, joinCode);
            AddPublicLobbyAttribute(modHandle, LobbyManager.HostAddressKey, _state.LocalProductUserId.ToString());
            AddPublicLobbyAttribute(modHandle, LobbyManager.MatchStartedKey, "false");
            AddPublicLobbyAttribute(modHandle, LobbyManager.AllowJoinInProgressKey, _state.AllowJoinInProgress ? "true" : "false");
            AddPublicLobbyAttribute(modHandle, LobbyManager.AllowedRejoinersKey, string.Empty);

            string hostDisplayName = LobbySessionState.ResolveLocalDisplayName("Host");
            AddPublicLobbyAttribute(modHandle, LobbyManager.HostNameKey, hostDisplayName);
            AddPublicMemberAttribute(modHandle, LobbyManager.DisplayNameKey, hostDisplayName);

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modHandle };
            _lobbyHandle.UpdateLobby(updateOptions, (onComplete, modHandle, onAppliedSuccess), HandleInitialAttributesUpdated);
        }

        private void HandleInitialAttributesUpdated(UpdateLobbyCallbackInfo data)
        {
            var (onComplete, modHandle, onAppliedSuccess) =
                ((Action<bool, string>, LobbyModification, Action))data.ClientData;
            modHandle?.Release();

            if (data.ResultCode != Result.Success)
            {
                _completeOperation(onComplete, false, $"Setting lobby attributes failed: {data.ResultCode}");
                return;
            }

            _state.CurrentMemberCount = 1;
            _state.AreAllJoinersReady = true;
            onAppliedSuccess?.Invoke();
            _completeOperation(onComplete, true, _state.CurrentJoinCode);
        }

        public void StartGame(Action<bool, string> onComplete = null)
        {
            if (!_state.IsInLobby || !_state.IsLobbyOwner)
            {
                _completeOperation(onComplete, false, "[LobbyManager] Cannot start game: Not the lobby owner or not in a lobby.");
                return;
            }

            if (!TryCreateModification(_state.CurrentLobbyId, out LobbyModification modHandle, onComplete))
                return;

            // Keep lobby searchable so disconnected match participants can auto-rejoin even when AllowJoinInProgress is false;
            // unauthorized players are blocked by JoinFromDetailsHandle and EosTransport.ConnectionFilter.
            modHandle.SetPermissionLevel(new LobbyModificationSetPermissionLevelOptions
            {
                PermissionLevel = LobbyPermissionLevel.Publicadvertised
            });

            SnapshotAllowedRejoiners();
            AddPublicLobbyAttribute(modHandle, LobbyManager.AllowedRejoinersKey, BuildAllowedRejoinersAttribute());
            AddPublicLobbyAttribute(modHandle, LobbyManager.MatchStartedKey, "true");

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modHandle };
            _lobbyHandle.UpdateLobby(updateOptions, (onComplete, modHandle), HandleStartGameUpdateCallback);
        }

        private void HandleStartGameUpdateCallback(UpdateLobbyCallbackInfo data)
        {
            var (onComplete, modHandle) = ((Action<bool, string>, LobbyModification))data.ClientData;
            modHandle?.Release();

            if (data.ResultCode != Result.Success)
            {
                _completeOperation(onComplete, false, $"[LobbyManager] StartGame failed: {data.ResultCode}");
                return;
            }

            _state.HasGameStarted = true;
            if (_state.LocalProductUserId != null && _state.IsInLobby)
            {
                LobbyPreferences.SetInLobby(
                    _state.LocalProductUserId,
                    _state.CurrentLobbyId,
                    _state.IsLobbyOwner,
                    inMatch: true,
                    _state.EffectiveHostAddress,
                    _state.CurrentJoinCode);
            }
            _completeOperation(onComplete, true, null);
        }

        public void RemoveAllowedRejoiner(ProductUserId userId)
        {
            if (userId == null || !_state.AllowedRejoiners.Remove(userId.ToString()))
            {
                return;
            }

            if (!_state.IsInLobby || !_state.IsLobbyOwner || !_state.HasGameStarted)
            {
                return;
            }

            if (!TryCreateModification(_state.CurrentLobbyId, out LobbyModification modHandle, null))
            {
                return;
            }

            AddPublicLobbyAttribute(modHandle, LobbyManager.AllowedRejoinersKey, BuildAllowedRejoinersAttribute());
            _lobbyHandle.UpdateLobby(
                new UpdateLobbyOptions { LobbyModificationHandle = modHandle },
                ((Action<bool, string>)null, modHandle),
                HandleUpdateCallback);
        }

        private void SnapshotAllowedRejoiners()
        {
            _state.AllowedRejoiners.Clear();
            List<ProductUserId> members = GetCurrentMemberUserIds();
            for (int i = 0; i < members.Count; i++)
            {
                ProductUserId member = members[i];
                if (member != null && member.IsValid())
                {
                    _state.AllowedRejoiners.Add(member.ToString());
                }
            }
        }

        private string BuildAllowedRejoinersAttribute()
        {
            if (_state.AllowedRejoiners.Count == 0)
            {
                return string.Empty;
            }

            // Cap to EOS lobby attribute value limit (~1000 bytes) while keeping the in-memory whitelist unbounded.
            var builder = new System.Text.StringBuilder();
            foreach (string puid in _state.AllowedRejoiners)
            {
                int needed = builder.Length == 0 ? puid.Length : puid.Length + 1;
                if (builder.Length + needed > 990)
                {
                    break;
                }

                if (builder.Length > 0) builder.Append(',');
                builder.Append(puid);
            }
            return builder.ToString();
        }

        public void SetReadyStatus(bool isReady, Action<bool, string> onComplete = null)
        {
            if (!_state.IsInLobby)
            {
                _completeOperation(onComplete, false, "[LobbyManager] Cannot set ready status: Not in a lobby.");
                return;
            }

            if (!TryCreateModification(_state.CurrentLobbyId, out LobbyModification modHandle, onComplete))
                return;

            AddPublicMemberAttribute(modHandle, LobbyManager.ReadyKey, isReady ? "true" : "false");

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modHandle };
            _lobbyHandle.UpdateLobby(updateOptions, (onComplete, modHandle), HandleUpdateCallback);
        }

        public void ToggleReadyStatus(Action<bool, string> onComplete = null)
        {
            SetReadyStatus(!IsMemberReady(_state.LocalProductUserId), onComplete);
        }

        public void ResetLocalReadyStatus(Action<bool, string> onComplete = null)
        {
            if (!_state.IsInLobby || _state.IsLobbyOwner)
            {
                onComplete?.Invoke(true, null);
                return;
            }

            SetReadyStatus(false, onComplete);
        }

        public void ResetMatchState(Action<bool, string> onComplete = null)
        {
            if (!_state.IsInLobby || !_state.IsLobbyOwner)
            {
                _completeOperation(onComplete, false, "[LobbyManager] Cannot reset match state: Not the lobby owner or not in a lobby.");
                return;
            }

            if (!TryCreateModification(_state.CurrentLobbyId, out LobbyModification modHandle, onComplete))
                return;

            // Restore lobby to Publicadvertised so it can be searched/joined again
            modHandle.SetPermissionLevel(new LobbyModificationSetPermissionLevelOptions
            {
                PermissionLevel = LobbyPermissionLevel.Publicadvertised
            });
            _state.AllowedRejoiners.Clear();
            AddPublicLobbyAttribute(modHandle, LobbyManager.AllowedRejoinersKey, string.Empty);
            AddPublicLobbyAttribute(modHandle, LobbyManager.MatchStartedKey, "false");

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modHandle };
            _lobbyHandle.UpdateLobby(updateOptions, (onComplete, modHandle), HandleResetMatchStateUpdateCallback);
        }

        private void HandleResetMatchStateUpdateCallback(UpdateLobbyCallbackInfo data)
        {
            var (onComplete, modHandle) = ((Action<bool, string>, LobbyModification))data.ClientData;
            modHandle?.Release();

            if (data.ResultCode != Result.Success)
            {
                _completeOperation(onComplete, false, $"[LobbyManager] ResetMatchState failed: {data.ResultCode}");
                return;
            }

            _state.HasGameStarted = false;
            if (_state.LocalProductUserId != null && _state.IsInLobby)
            {
                LobbyPreferences.SetInLobby(
                    _state.LocalProductUserId,
                    _state.CurrentLobbyId,
                    _state.IsLobbyOwner,
                    inMatch: false,
                    _state.EffectiveHostAddress,
                    _state.CurrentJoinCode);
            }
            _completeOperation(onComplete, true, null);
        }

        public void SetLocalMemberDisplayName(string displayName, Action<bool, string> onComplete = null)
        {
            if (!_state.IsInLobby) return;

            if (!TryCreateModification(_state.CurrentLobbyId, out LobbyModification modHandle, onComplete))
                return;

            AddPublicMemberAttribute(modHandle, LobbyManager.DisplayNameKey, displayName);

            _lobbyHandle.UpdateLobby(
                new UpdateLobbyOptions { LobbyModificationHandle = modHandle },
                (onComplete, modHandle),
                HandleUpdateCallback);
        }

        private void HandleUpdateCallback(UpdateLobbyCallbackInfo data)
        {
            var (onComplete, modHandle) = ((Action<bool, string>, LobbyModification))data.ClientData;
            modHandle?.Release();

            if (data.ResultCode != Result.Success)
            {
                _completeOperation(onComplete, false, $"[LobbyManager] Lobby update failed: {data.ResultCode}");
                return;
            }

            _completeOperation(onComplete, true, null);
        }

        public List<ProductUserId> GetCurrentMemberUserIds()
        {
            var userIds = new List<ProductUserId>();
            if (!_state.TryCopyCurrentDetails(_lobbyHandle, out LobbyDetails detailsHandle))
            {
                if (_state.LocalProductUserId != null) userIds.Add(_state.LocalProductUserId);
                return userIds;
            }

            uint count = detailsHandle.GetMemberCount(new LobbyDetailsGetMemberCountOptions());
            for (uint i = 0; i < count; i++)
            {
                ProductUserId puid = detailsHandle.GetMemberByIndex(new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i });
                if (puid != null) userIds.Add(puid);
            }

            detailsHandle.Release();
            return userIds;
        }

        public string GetMemberDisplayName(ProductUserId userId)
        {
            if (userId == null || userId == _state.LocalProductUserId)
            {
                return LobbySessionState.ResolveLocalDisplayName("Player");
            }

            if (!_state.TryCopyCurrentDetails(_lobbyHandle, out LobbyDetails detailsHandle))
            {
                string fallback = LobbyManager.ExternalDisplayNameResolver?.Invoke(userId.ToString());
                return !string.IsNullOrEmpty(fallback) ? fallback : "Player";
            }

            LobbySessionState.TryReadMemberAttribute(detailsHandle, userId, LobbyManager.DisplayNameKey, out string displayName);
            detailsHandle.Release();

            if (string.IsNullOrWhiteSpace(displayName) || displayName == "Player")
            {
                string fallback = LobbyManager.ExternalDisplayNameResolver?.Invoke(userId.ToString());
                if (!string.IsNullOrEmpty(fallback))
                {
                    displayName = fallback;
                }
            }

            return !string.IsNullOrWhiteSpace(displayName) ? displayName : "Player";
        }

        public string ResolveDisplayName(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            var userId = ProductUserId.FromString(address);
            return userId != null ? GetMemberDisplayName(userId) : null;
        }

        public bool IsMemberReady(ProductUserId userId)
        {
            if (userId == null || !_state.IsInLobby) return false;
            if (userId == _state.LocalProductUserId && _state.IsLobbyOwner) return true;

            if (!_state.TryCopyCurrentDetails(_lobbyHandle, out LobbyDetails detailsHandle))
                return false;

            if (detailsHandle.CopyInfo(new LobbyDetailsCopyInfoOptions(), out LobbyDetailsInfo info) == Result.Success &&
                info != null &&
                userId == info.LobbyOwnerUserId)
            {
                detailsHandle.Release();
                return true;
            }

            bool isReady = LobbySessionState.TryReadMemberAttribute(detailsHandle, userId, LobbyManager.ReadyKey, out string value)
                && value == "true";

            detailsHandle.Release();
            return isReady;
        }

        public bool UpdateAndCheckReadyStatus()
        {
            if (!_state.TryCopyCurrentDetails(_lobbyHandle, out LobbyDetails detailsHandle))
            {
                return _state.AreAllJoinersReady;
            }

            uint memberCount = detailsHandle.GetMemberCount(new LobbyDetailsGetMemberCountOptions());
            _state.CurrentMemberCount = (int)memberCount;

            ProductUserId ownerUserId = null;
            if (detailsHandle.CopyInfo(new LobbyDetailsCopyInfoOptions(), out LobbyDetailsInfo info) == Result.Success && info != null)
            {
                ownerUserId = info.LobbyOwnerUserId;
            }

            bool allReady = true;
            int joinerCount = 0;

            for (uint i = 0; i < memberCount; i++)
            {
                ProductUserId memberPuid = detailsHandle.GetMemberByIndex(new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i });
                if (memberPuid == null || memberPuid == ownerUserId)
                    continue;

                joinerCount++;
                if (!LobbySessionState.TryReadMemberAttribute(detailsHandle, memberPuid, LobbyManager.ReadyKey, out string readyVal) ||
                    readyVal != "true")
                {
                    allReady = false;
                }
            }

            detailsHandle.Release();

            _state.AreAllJoinersReady = (joinerCount == 0) || allReady;
            _notifyReadyStatusChanged?.Invoke(_state.AreAllJoinersReady);
            return _state.AreAllJoinersReady;
        }

        private bool TryCreateModification(string lobbyId, out LobbyModification modHandle, Action<bool, string> onComplete)
        {
            var modOptions = new UpdateLobbyModificationOptions
            {
                LobbyId = lobbyId,
                LocalUserId = _state.LocalProductUserId
            };
            Result result = _lobbyHandle.UpdateLobbyModification(modOptions, out modHandle);
            if (result != Result.Success)
            {
                _completeOperation(onComplete, false, $"UpdateLobbyModification failed: {result}");
                return false;
            }
            return true;
        }

        private static void AddPublicLobbyAttribute(LobbyModification modHandle, string key, string value)
        {
            modHandle.AddAttribute(new LobbyModificationAddAttributeOptions
            {
                Attribute = new AttributeData { Key = key, Value = value },
                Visibility = LobbyAttributeVisibility.Public
            });
        }

        private static void AddPublicMemberAttribute(LobbyModification modHandle, string key, string value)
        {
            modHandle.AddMemberAttribute(new LobbyModificationAddMemberAttributeOptions
            {
                Attribute = new AttributeData { Key = key, Value = value },
                Visibility = LobbyAttributeVisibility.Public
            });
        }
    }
}

