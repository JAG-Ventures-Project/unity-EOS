using System;
using System.Collections.Generic;
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
        private Action<string, string, string, Action> _onOrphanMatchFound;
        private readonly Queue<(ProductUserId TargetUserId, Action<bool, string> OnComplete)> _pendingWhitelistInvites = new();

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

        public void SetOrphanMatchHandler(Action<string, string, string, Action> handler)
        {
            _onOrphanMatchFound = handler;
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
                !LobbyPreferences.HasOrphanLobby(
                    _state.LocalProductUserId,
                    out string orphanLobbyId,
                    out bool isOwner,
                    out bool wasInMatch,
                    out string hostAddress,
                    out string joinCode))
            {
                _state.IsCleaningOrphan = false;
                onComplete?.Invoke();
                return;
            }

            if (_state.AllowAutoRejoin && !isOwner && wasInMatch && _onOrphanMatchFound != null)
            {
                _state.IsCleaningOrphan = false;
                Debug.Log($"[LobbyManager] Active match disconnect/crash detected for lobby {orphanLobbyId}. Attempting automatic rejoin...");
                _onOrphanMatchFound.Invoke(orphanLobbyId, hostAddress, joinCode, onComplete);
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
            _state.CurrentJoinCode = (_state.UseShortJoinCode && LobbyManager.CustomJoinCodeGenerator != null)
                ? LobbyManager.CustomJoinCodeGenerator.Invoke()
                : _state.CurrentLobbyId;
            _state.IsLobbyOwner = true;
            LobbyPreferences.SetInLobby(
                _state.LocalProductUserId,
                _state.CurrentLobbyId,
                isOwner: true,
                inMatch: false,
                _state.EffectiveHostAddress,
                _state.CurrentJoinCode);

            _attributes.SetInitialLobbyAttributes(
                _state.CurrentLobbyId,
                _state.CurrentJoinCode,
                onAppliedSuccess: HandleInitialAttributesApplied,
                onComplete: onComplete);
        }

        private void HandleInitialAttributesApplied()
        {
            _notifications.SubscribeToSessionNotifications();
            _notifyLobbyCreated.Invoke(_state.CurrentJoinCode);
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

        public void FindLobbyById(string lobbyId, Action<Result, LobbyDetails> onComplete)
        {
            if (string.IsNullOrEmpty(lobbyId) || onComplete == null)
            {
                onComplete?.Invoke(Result.InvalidParameters, null);
                return;
            }

            _state.LocalProductUserId = ConnectManager.Instance?.LocalProductUserId;
            if (_state.LocalProductUserId == null || !_state.LocalProductUserId.IsValid())
            {
                onComplete.Invoke(Result.InvalidUser, null);
                return;
            }

            Result result = _lobbyHandle.CreateLobbySearch(new CreateLobbySearchOptions { MaxResults = 1 }, out LobbySearch searchHandle);
            if (result != Result.Success || searchHandle == null)
            {
                onComplete.Invoke(result, null);
                return;
            }

            searchHandle.SetLobbyId(new LobbySearchSetLobbyIdOptions { LobbyId = lobbyId });
            searchHandle.Find(
                new LobbySearchFindOptions { LocalUserId = _state.LocalProductUserId },
                (searchHandle, onComplete),
                HandleFindLobbyByIdCallback);
        }

        private void HandleFindLobbyByIdCallback(LobbySearchFindCallbackInfo data)
        {
            var (searchHandle, onComplete) = ((LobbySearch, Action<Result, LobbyDetails>))data.ClientData;
            if (data.ResultCode != Result.Success)
            {
                searchHandle.Release();
                onComplete?.Invoke(data.ResultCode, null);
                return;
            }

            if (searchHandle.GetSearchResultCount(new LobbySearchGetSearchResultCountOptions()) == 0)
            {
                searchHandle.Release();
                onComplete?.Invoke(Result.NotFound, null);
                return;
            }

            Result copyResult = searchHandle.CopySearchResultByIndex(new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 }, out LobbyDetails detailsHandle);
            searchHandle.Release();
            onComplete?.Invoke(copyResult, copyResult == Result.Success ? detailsHandle : null);
        }

        public void JoinFoundLobbyDetails(LobbyDetails detailsHandle, string joinCode, bool isAutoRejoin, Action<bool, string> onComplete)
        {
            if (detailsHandle == null)
            {
                _completeOperation(onComplete, false, "Lobby details unavailable.");
                return;
            }

            JoinFromDetailsHandle(detailsHandle, joinCode, onComplete, isAutoRejoin);
        }

        private void JoinFromDetailsHandle(LobbyDetails detailsHandle, string joinCode, Action<bool, string> onComplete, bool isAutoRejoin = false)
        {
            LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.HostAddressKey, out string hostAddress);
            bool matchAlreadyStarted = LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.MatchStartedKey, out string startedVal)
                && startedVal == "true";

            if (matchAlreadyStarted)
            {
                bool allowsJoinInProgress = LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.AllowJoinInProgressKey, out string allowVal)
                    && allowVal == "true";
                bool isWhitelistedRejoiner = LobbySessionState.IsUserAllowedToRejoin(detailsHandle, _state.LocalProductUserId);

                if (!allowsJoinInProgress && !isWhitelistedRejoiner)
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
            _lobbyHandle.JoinLobby(joinOptions, (onComplete, joinCode, hostAddress, detailsHandle, matchAlreadyStarted, isAutoRejoin), HandleJoinLobbyCallback);
        }

        private void HandleJoinLobbyCallback(JoinLobbyCallbackInfo data)
        {
            var (onComplete, joinCode, hostAddress, detailsHandle, matchAlreadyStarted, isAutoRejoin) =
                ((Action<bool, string>, string, string, LobbyDetails, bool, bool))data.ClientData;

            if (detailsHandle != null)
            {
                LobbySessionState.ParseAllowedRejoiners(detailsHandle, _state.AllowedRejoiners);
                detailsHandle.Release();
            }

            if (data.ResultCode != Result.Success && data.ResultCode != Result.LobbyMemberUpdateOnly)
            {
                string errorMsg = (data.ResultCode == Result.LobbyTooManyPlayers) ? "Lobby is full." : $"JoinLobby failed: {data.ResultCode}";
                if (isAutoRejoin)
                {
                    onComplete?.Invoke(false, errorMsg);
                }
                else
                {
                    _completeOperation(onComplete, false, errorMsg);
                }
                return;
            }

            _state.CurrentLobbyId = data.LobbyId;
            _state.CurrentJoinCode = joinCode;
            _state.IsLobbyOwner = false;
            LobbyPreferences.SetInLobby(
                _state.LocalProductUserId,
                _state.CurrentLobbyId,
                isOwner: false,
                inMatch: matchAlreadyStarted,
                hostAddress,
                joinCode);
            _state.CurrentHostAddress = hostAddress;
            _state.HasGameStarted = matchAlreadyStarted;

            _notifications.SubscribeToSessionNotifications();
            _attributes.UpdateAndCheckReadyStatus();
            _attributes.SetLocalMemberDisplayName(LobbySessionState.ResolveLocalDisplayName("Player"));

            _notifyLobbyJoined.Invoke(hostAddress);
            _completeOperation(onComplete, true, hostAddress);

            // If match has already started (manual join-in-progress / invite), fire OnGameStarted so client connects to the active match.
            // Auto-rejoin invokes StartClient directly via LobbyRejoinService.
            if (matchAlreadyStarted && !isAutoRejoin)
            {
                _notifyGameStarted.Invoke(hostAddress);
            }
        }

        public void SendInvite(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {
            Assert.IsTrue(_state.IsInLobby, "[LobbyManager] Cannot send invite: Not in a lobby.");
            Assert.IsNotNull(targetUserId, "[LobbyManager] targetUserId must not be null.");

            if (_state.IsLobbyOwner && _state.HasGameStarted && !_state.AllowJoinInProgress)
            {
                _pendingWhitelistInvites.Enqueue((targetUserId, onComplete));
                _attributes.AddAllowedRejoiner(targetUserId, HandleWhitelistBeforeInviteCompleted);
                return;
            }

            DispatchSendInvite(targetUserId, onComplete);
        }

        private void HandleWhitelistBeforeInviteCompleted(bool success, string error)
        {
            if (_pendingWhitelistInvites.Count == 0) return;

            var (targetUserId, onComplete) = _pendingWhitelistInvites.Dequeue();
            if (!success)
            {
                _completeOperation(onComplete, false, error);
                return;
            }

            if (!_state.IsInLobby)
            {
                _completeOperation(onComplete, false, "[LobbyManager] Cannot send invite: Left lobby before invite was dispatched.");
                return;
            }

            DispatchSendInvite(targetUserId, onComplete);
        }

        private void DispatchSendInvite(ProductUserId targetUserId, Action<bool, string> onComplete)
        {
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
            if (data.ResultCode != Result.Success && data.ResultCode != Result.NotFound)
            {
                _completeOperation(onComplete, false, $"RejectInvite failed: {data.ResultCode}");
                return;
            }
            _completeOperation(onComplete, true, null);
        }

        public void LeaveLobby(Action<bool> onComplete = null, bool notifyLeft = true)
        {
            if (!_state.IsInLobby)
            {
                if (!notifyLeft) ResetState();
                _completeBoolOperation(onComplete, true, null);
                return;
            }

            if (_state.IsLobbyOwner)
            {
                var destroyOptions = new DestroyLobbyOptions { LobbyId = _state.CurrentLobbyId, LocalUserId = _state.LocalProductUserId };
                _lobbyHandle.DestroyLobby(destroyOptions, (onComplete, notifyLeft), HandleDestroyLobbyCallback);
            }
            else
            {
                var leaveOptions = new LeaveLobbyOptions { LobbyId = _state.CurrentLobbyId, LocalUserId = _state.LocalProductUserId };
                _lobbyHandle.LeaveLobby(leaveOptions, (onComplete, notifyLeft), HandleLeaveLobbyCallback);
            }
        }

        // Leaves a specific lobby ID (used when clearing stale orphan/presence membership during rejoin).
        public void LeaveLobbyById(string lobbyId, Action onComplete = null)
        {
            _state.LocalProductUserId = ConnectManager.Instance?.LocalProductUserId;
            if (string.IsNullOrEmpty(lobbyId) || _state.LocalProductUserId == null || !_state.LocalProductUserId.IsValid())
            {
                onComplete?.Invoke();
                return;
            }

            var leaveOptions = new LeaveLobbyOptions { LobbyId = lobbyId, LocalUserId = _state.LocalProductUserId };
            _lobbyHandle.LeaveLobby(leaveOptions, onComplete, HandleLeaveLobbyByIdCallback);
        }

        private static void HandleLeaveLobbyByIdCallback(LeaveLobbyCallbackInfo data)
        {
            (data.ClientData as Action)?.Invoke();
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
            _lobbyHandle.KickMember(kickOptions, (onComplete, targetUserId), HandleKickMemberCallback);
        }

        private void HandleKickMemberCallback(KickMemberCallbackInfo data)
        {
            var (onComplete, targetUserId) = ((Action<bool, string>, ProductUserId))data.ClientData;
            if (data.ResultCode != Result.Success)
            {
                _completeOperation(onComplete, false, $"KickMember failed: {data.ResultCode}");
                return;
            }

            _attributes.RemoveAllowedRejoiner(targetUserId);
            _completeOperation(onComplete, true, null);
        }

        private void HandleDestroyLobbyCallback(DestroyLobbyCallbackInfo data)
        {
            var (onComplete, notifyLeft) = ((Action<bool>, bool))data.ClientData;
            CompleteLeaveOrDestroy(onComplete, notifyLeft, data.ResultCode, "DestroyLobby");
        }

        private void HandleLeaveLobbyCallback(LeaveLobbyCallbackInfo data)
        {
            var (onComplete, notifyLeft) = ((Action<bool>, bool))data.ClientData;
            CompleteLeaveOrDestroy(onComplete, notifyLeft, data.ResultCode, "LeaveLobby");
        }

        private void CompleteLeaveOrDestroy(Action<bool> onComplete, bool notifyLeft, Result resultCode, string operation)
        {
            if (notifyLeft)
            {
                ResetStateAndNotifyLeft();
            }
            else
            {
                ResetState();
            }

            bool success = resultCode == Result.Success;
            _completeBoolOperation(onComplete, success, success ? null : $"{operation} failed: {resultCode}");
        }

        public void ResetState()
        {
            _pendingWhitelistInvites.Clear();
            LobbyPreferences.ClearInLobby(_state.LocalProductUserId);
            _notifications.UnsubscribeFromSessionNotifications();
            _state.Reset();
        }

        private void ResetStateAndNotifyLeft()
        {
            ResetState();
            _notifyLobbyLeft.Invoke();
        }
    }
}

