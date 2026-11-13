using System;
using EOS.Auth;
using EOS.Core;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS.Lobby
{
    /// <summary>
    /// Owns EOS LobbyInterface CRUD, search, invite acceptance/rejection, leave/kick, and orphan cleanup.
    /// </summary>
    internal sealed class LobbySessionService
    {
        private const string BucketId = "GameLobby";

        private readonly LobbyInterface _lobbyHandle;
        private readonly LobbySessionState _state;
        private readonly LobbyAttributeService _attributes;
        private readonly LobbyNotificationListener _notifications;

        private readonly Action<string> _notifyLobbyCreated;
        private readonly Action<string> _notifyLobbyJoined;
        private readonly Action _notifyLobbyLeft;
        private readonly Action<string> _notifyGameStarted;
        private readonly Action<Action<bool, string>, bool, string> _completeOperation;
        private readonly Action<Action<bool>, bool, string> _completeBoolOperation;

        public LobbySessionService(
            LobbyInterface lobbyHandle,
            LobbySessionState state,
            LobbyAttributeService attributes,
            LobbyNotificationListener notifications,
            Action<string> notifyLobbyCreated,
            Action<string> notifyLobbyJoined,
            Action notifyLobbyLeft,
            Action<string> notifyGameStarted,
            Action<Action<bool, string>, bool, string> completeOperation,
            Action<Action<bool>, bool, string> completeBoolOperation)
        {
            _lobbyHandle = lobbyHandle;
            _state = state;
            _attributes = attributes;
            _notifications = notifications;
            _notifyLobbyCreated = notifyLobbyCreated;
            _notifyLobbyJoined = notifyLobbyJoined;
            _notifyLobbyLeft = notifyLobbyLeft;
            _notifyGameStarted = notifyGameStarted;
            _completeOperation = completeOperation;
            _completeBoolOperation = completeBoolOperation;
        }

        public void CreateLobby(uint maxMembers, Action<bool, string> onComplete = null)
        {
            Assert.IsTrue(!_state.IsInLobby, "[LobbyManager] Cannot create lobby: Player is already in an active lobby.");
            Assert.IsFalse(_state.IsCleaningOrphan, "[LobbyManager] Cannot create lobby: Orphan cleanup is still pending.");

            _state.LocalProductUserId = ConnectManager.Instance?.LocalProductUserId;
            Assert.IsNotNull(_state.LocalProductUserId, "[LobbyManager] LocalProductUserId must not be null.");

            var createOptions = new CreateLobbyOptions
            {
                LocalUserId = _state.LocalProductUserId,
                MaxLobbyMembers = maxMembers,
                PermissionLevel = LobbyPermissionLevel.Publicadvertised,
                PresenceEnabled = true,
                AllowInvites = true,
                BucketId = BucketId
            };
            _lobbyHandle.CreateLobby(createOptions, onComplete, HandleCreateLobbyCallback);
        }

        public void CheckAndCleanOrphanLobbyOnLogin(Action onComplete = null)
        {
            _state.LocalProductUserId = ConnectManager.Instance?.LocalProductUserId;

            if (_state.LocalProductUserId == null ||
                !LobbyPreferences.HasOrphanLobby(_state.LocalProductUserId, out string orphanLobbyId, out bool isOwner))
            {
                _state.IsCleaningOrphan = false;
                onComplete?.Invoke();
                return;
            }

            _state.IsCleaningOrphan = true;
            Debug.Log($"[LobbyManager] Dirty exit detected. Cleaning up orphan lobby {orphanLobbyId} (isOwner: {isOwner})...");
            try
            {
                if (isOwner)
                {
                    var destroyOptions = new DestroyLobbyOptions { LobbyId = orphanLobbyId, LocalUserId = _state.LocalProductUserId };
                    _lobbyHandle.DestroyLobby(destroyOptions, onComplete, HandleDirectDestroyOrphanCallback);
                }
                else
                {
                    var leaveOptions = new LeaveLobbyOptions { LobbyId = orphanLobbyId, LocalUserId = _state.LocalProductUserId };
                    _lobbyHandle.LeaveLobby(leaveOptions, onComplete, HandleDirectLeaveOrphanCallback);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Direct orphan cleanup failed: {ex.Message}");
                _state.IsCleaningOrphan = false;
                LobbyPreferences.ClearInLobby(_state.LocalProductUserId);
                onComplete?.Invoke();
            }
        }

        private void HandleDirectDestroyOrphanCallback(DestroyLobbyCallbackInfo data)
        {
            _state.IsCleaningOrphan = false;
            var onComplete = data.ClientData as Action;
            LobbyPreferences.ClearInLobby(_state.LocalProductUserId);
            Debug.Log($"[LobbyManager] Destroy orphan lobby result: {data.ResultCode}");
            onComplete?.Invoke();
        }

        private void HandleDirectLeaveOrphanCallback(LeaveLobbyCallbackInfo data)
        {
            _state.IsCleaningOrphan = false;
            var onComplete = data.ClientData as Action;
            LobbyPreferences.ClearInLobby(_state.LocalProductUserId);
            Debug.Log($"[LobbyManager] Leave orphan lobby result: {data.ResultCode}");
            onComplete?.Invoke();
        }

        private void HandleCreateLobbyCallback(CreateLobbyCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool, string>;

            if (data.ResultCode != Result.Success)
            {
                _completeOperation(onComplete, false, $"CreateLobby failed: {data.ResultCode}");
                return;
            }

            _state.CurrentLobbyId = data.LobbyId;
            _state.CurrentJoinCode = LobbyManager.CustomJoinCodeGenerator != null
                ? LobbyManager.CustomJoinCodeGenerator.Invoke()
                : _state.CurrentLobbyId;
            _state.IsLobbyOwner = true;
            LobbyPreferences.SetInLobby(_state.LocalProductUserId, _state.CurrentLobbyId, isOwner: true);

            _attributes.SetInitialLobbyAttributes(
                _state.CurrentLobbyId,
                _state.CurrentJoinCode,
                onAppliedSuccess: () =>
                {
                    _notifications.SubscribeToSessionNotifications();
                    _notifyLobbyCreated?.Invoke(_state.CurrentJoinCode);
                },
                onComplete: onComplete);
        }

        public void JoinLobbyByCode(string joinCode, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(_state.IsCleaningOrphan, "[LobbyManager] Cannot join lobby: Orphan cleanup is still pending.");
            _state.LocalProductUserId = ConnectManager.Instance?.LocalProductUserId;
            string formattedCode = joinCode.Trim().ToUpperInvariant();

            Result result = _lobbyHandle.CreateLobbySearch(new CreateLobbySearchOptions { MaxResults = 50 }, out LobbySearch searchHandle);
            if (result != Result.Success)
            {
                _completeOperation(onComplete, false, $"CreateLobbySearch failed: {result}");
                return;
            }

            searchHandle.SetParameter(new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData { Key = "bucket", Value = BucketId },
                ComparisonOp = ComparisonOp.Equal
            });
            searchHandle.SetParameter(new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData { Key = LobbyManager.JoinCodeKey, Value = formattedCode },
                ComparisonOp = ComparisonOp.Equal
            });

            searchHandle.Find(
                new LobbySearchFindOptions { LocalUserId = _state.LocalProductUserId },
                (searchHandle, onComplete, formattedCode),
                HandleFindLobbiesCallback);
        }

        private void HandleFindLobbiesCallback(LobbySearchFindCallbackInfo data)
        {
            var (searchHandle, onComplete, joinCode) = ((LobbySearch, Action<bool, string>, string))data.ClientData;
            if (data.ResultCode != Result.Success)
            {
                searchHandle.Release();
                _completeOperation(onComplete, false, $"Search failed: {data.ResultCode}");
                return;
            }

            if (searchHandle.GetSearchResultCount(new LobbySearchGetSearchResultCountOptions()) == 0)
            {
                searchHandle.Release();
                _completeOperation(onComplete, false, "Lobby not found. Please verify the room code.");
                return;
            }

            Result copyResult = searchHandle.CopySearchResultByIndex(new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 }, out LobbyDetails detailsHandle);
            searchHandle.Release();
            if (copyResult != Result.Success)
            {
                _completeOperation(onComplete, false, $"Failed to copy lobby search details: {copyResult}");
                return;
            }

            JoinFromDetailsHandle(detailsHandle, joinCode, onComplete);
        }

        public void JoinLobbyByInvite(string inviteId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(_state.IsCleaningOrphan, "[LobbyManager] Cannot join lobby: Orphan cleanup is still pending.");
            _state.LocalProductUserId = ConnectManager.Instance?.LocalProductUserId;
            Assert.IsNotNull(_state.LocalProductUserId, "[LobbyManager] LocalProductUserId must not be null.");

            Result result = _lobbyHandle.CopyLobbyDetailsHandleByInviteId(
                new CopyLobbyDetailsHandleByInviteIdOptions { InviteId = inviteId },
                out LobbyDetails detailsHandle);

            if (result != Result.Success || detailsHandle == null)
            {
                _completeOperation(onComplete, false, $"Failed to inspect lobby invite: {result}");
                return;
            }

            if (detailsHandle.CopyInfo(new LobbyDetailsCopyInfoOptions(), out LobbyDetailsInfo info) == Result.Success &&
                info != null &&
                info.AvailableSlots == 0)
            {
                detailsHandle.Release();
                _completeOperation(onComplete, false, "Lobby is full.");
                return;
            }

            LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.JoinCodeKey, out string joinCode);
            JoinFromDetailsHandle(detailsHandle, joinCode, onComplete);
        }

        private void JoinFromDetailsHandle(LobbyDetails detailsHandle, string joinCode, Action<bool, string> onComplete)
        {
            LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.HostAddressKey, out string hostAddress);
            bool matchAlreadyStarted = LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.MatchStartedKey, out string startedVal)
                && startedVal == "true";

            if (matchAlreadyStarted)
            {
                bool allowsJoinInProgress = LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.AllowJoinInProgressKey, out string allowVal)
                    && allowVal == "true";

                if (!allowsJoinInProgress)
                {
                    detailsHandle.Release();
                    _completeOperation(onComplete, false, "Match has already started in this room.");
                    return;
                }
            }

            var joinOptions = new JoinLobbyOptions
            {
                LobbyDetailsHandle = detailsHandle,
                LocalUserId = _state.LocalProductUserId,
                PresenceEnabled = true
            };
            _lobbyHandle.JoinLobby(joinOptions, (onComplete, joinCode, hostAddress, detailsHandle, matchAlreadyStarted), HandleJoinLobbyCallback);
        }

        private void HandleJoinLobbyCallback(JoinLobbyCallbackInfo data)
        {
            Action<bool, string> onComplete = null;
            string joinCode = string.Empty;
            string hostAddress = string.Empty;
            LobbyDetails detailsHandle = null;
            bool matchAlreadyStarted = false;

            if (data.ClientData is ValueTuple<Action<bool, string>, string, string, LobbyDetails, bool> tuple5)
            {
                (onComplete, joinCode, hostAddress, detailsHandle, matchAlreadyStarted) = tuple5;
            }
            else if (data.ClientData is ValueTuple<Action<bool, string>, string, string, LobbyDetails> tuple4)
            {
                (onComplete, joinCode, hostAddress, detailsHandle) = tuple4;
            }

            detailsHandle?.Release();

            if (data.ResultCode != Result.Success)
            {
                string errorMsg = (data.ResultCode == Result.LobbyTooManyPlayers) ? "Lobby is full." : $"JoinLobby failed: {data.ResultCode}";
                _completeOperation(onComplete, false, errorMsg);
                return;
            }

            _state.CurrentLobbyId = data.LobbyId;
            _state.CurrentJoinCode = joinCode;
            _state.IsLobbyOwner = false;
            LobbyPreferences.SetInLobby(_state.LocalProductUserId, _state.CurrentLobbyId, isOwner: false);
            _state.CurrentHostAddress = hostAddress;
            _state.HasGameStarted = matchAlreadyStarted;

            _notifications.SubscribeToSessionNotifications();
            _attributes.UpdateAndCheckReadyStatus();
            _attributes.SetLocalMemberDisplayName(LobbySessionState.ResolveLocalDisplayName("Player"));

            _notifyLobbyJoined?.Invoke(hostAddress);
            _completeOperation(onComplete, true, hostAddress);

            // If match has already started, immediately fire OnGameStarted so late-joiner connects to the active match
            if (matchAlreadyStarted)
            {
                _notifyGameStarted?.Invoke(hostAddress);
            }
        }

        public void SendInvite(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {
            Assert.IsTrue(_state.IsInLobby, "[LobbyManager] Cannot send invite: Not in a lobby.");
            Assert.IsNotNull(targetUserId, "[LobbyManager] targetUserId must not be null.");

            var options = new SendInviteOptions
            {
                LobbyId = _state.CurrentLobbyId,
                LocalUserId = _state.LocalProductUserId,
                TargetUserId = targetUserId
            };
            _lobbyHandle.SendInvite(options, onComplete, HandleSendInviteCallback);
        }

        private void HandleSendInviteCallback(SendInviteCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool, string>;
            if (data.ResultCode != Result.Success)
            {
                _completeOperation(onComplete, false, $"SendInvite failed: {data.ResultCode}");
                return;
            }
            _completeOperation(onComplete, true, null);
        }

        public void RejectInvite(string inviteId, Action<bool, string> onComplete = null)
        {
            _state.LocalProductUserId = ConnectManager.Instance?.LocalProductUserId;
            Assert.IsNotNull(_state.LocalProductUserId, "[LobbyManager] LocalProductUserId must not be null.");
            Assert.IsFalse(string.IsNullOrEmpty(inviteId), "[LobbyManager] inviteId must not be null or empty.");

            var options = new RejectInviteOptions
            {
                InviteId = inviteId,
                LocalUserId = _state.LocalProductUserId
            };
            _lobbyHandle.RejectInvite(options, onComplete, HandleRejectInviteCallback);
        }

        private void HandleRejectInviteCallback(RejectInviteCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool, string>;
            if (data.ResultCode != Result.Success)
            {
                _completeOperation(onComplete, false, $"RejectInvite failed: {data.ResultCode}");
                return;
            }
            _completeOperation(onComplete, true, null);
        }

        public void LeaveLobby(Action<bool> onComplete = null)
        {
            if (!_state.IsInLobby)
            {
                _completeBoolOperation(onComplete, true, null);
                return;
            }

            if (_state.IsLobbyOwner)
            {
                var destroyOptions = new DestroyLobbyOptions { LobbyId = _state.CurrentLobbyId, LocalUserId = _state.LocalProductUserId };
                _lobbyHandle.DestroyLobby(destroyOptions, onComplete, HandleDestroyLobbyCallback);
            }
            else
            {
                var leaveOptions = new LeaveLobbyOptions { LobbyId = _state.CurrentLobbyId, LocalUserId = _state.LocalProductUserId };
                _lobbyHandle.LeaveLobby(leaveOptions, onComplete, HandleLeaveLobbyCallback);
            }
        }

        public void KickMember(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {
            Assert.IsTrue(_state.IsLobbyOwner, "[LobbyManager] Only the lobby owner can kick members.");
            Assert.IsNotNull(targetUserId, "[LobbyManager] TargetUserId must not be null.");

            var kickOptions = new KickMemberOptions
            {
                LobbyId = _state.CurrentLobbyId,
                LocalUserId = _state.LocalProductUserId,
                TargetUserId = targetUserId
            };
            _lobbyHandle.KickMember(kickOptions, onComplete, HandleKickMemberCallback);
        }

        private void HandleKickMemberCallback(KickMemberCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool, string>;
            if (data.ResultCode != Result.Success)
            {
                _completeOperation(onComplete, false, $"KickMember failed: {data.ResultCode}");
                return;
            }
            _completeOperation(onComplete, true, null);
        }

        private void HandleDestroyLobbyCallback(DestroyLobbyCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool>;
            ResetStateAndNotifyLeft();
            bool success = data.ResultCode == Result.Success;
            _completeBoolOperation(onComplete, success, success ? null : $"DestroyLobby failed: {data.ResultCode}");
        }

        private void HandleLeaveLobbyCallback(LeaveLobbyCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool>;
            ResetStateAndNotifyLeft();
            bool success = data.ResultCode == Result.Success;
            _completeBoolOperation(onComplete, success, success ? null : $"LeaveLobby failed: {data.ResultCode}");
        }

        public void ResetState()
        {
            LobbyPreferences.ClearInLobby(_state.LocalProductUserId);
            _notifications.UnsubscribeFromSessionNotifications();
            _state.Reset();
        }

        private void ResetStateAndNotifyLeft()
        {
            ResetState();
            _notifyLobbyLeft?.Invoke();
        }
    }
}

