using System;
using EOS.Auth;
using EOS.Core;
using EOS.Friends;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS.Lobby
{
    /// <summary>
    /// Handles automatic match rejoin after unexpected internet/network drops or game restarts/crashes,
    /// even when AllowJoinInProgress is false, for players whitelisted in the match.
    /// Polls OS reachability and EOS LobbySearch.SetLobbyId only while an auto-rejoin is active.
    /// </summary>
    // AI implementation: Subscribes to EOSSDKManager OnTick on demand only during active auto-rejoin
    // so zero per-frame checks run during normal gameplay or menus.
    internal sealed class LobbyRejoinService
    {
        private const float InitialProbeDelaySeconds = 1.5f;
        private const float ProbeIntervalSeconds = 3.0f;
        private const float OfflineCheckIntervalSeconds = 1.0f;
        private const int MaxConsecutiveMirrorFailures = 2;

        private readonly LobbySessionState _state;
        private readonly LobbySessionService _sessions;
        private readonly LobbyNotificationListener _notifications;

        private bool _isProbing;
        private float _nextProbeTime;
        private int _consecutiveMirrorFailures;
        private bool _hasNotifiedWaiting;
        private bool _isTickSubscribed;
        private Action _pendingFallbackCallback;

        public event Action<bool, string> OnAutoRejoinStateChanged;

        public LobbyRejoinService(
            LobbySessionState state,
            LobbySessionService sessions,
            LobbyNotificationListener notifications)
        {
            _state = state;
            _sessions = sessions;
            _notifications = notifications;

            Assert.IsNotNull(_state, "[LobbyRejoinService] LobbySessionState must not be null.");
            Assert.IsNotNull(_sessions, "[LobbyRejoinService] LobbySessionService must not be null.");
            Assert.IsNotNull(_notifications, "[LobbyRejoinService] LobbyNotificationListener must not be null.");
        }

        public void BeginAutoRejoin(
            string lobbyId,
            string hostAddress,
            string joinCode,
            Action onFallbackComplete = null)
        {
            if (!_state.AllowAutoRejoin || _state.IsLobbyOwner || string.IsNullOrEmpty(lobbyId))
            {
                onFallbackComplete?.Invoke();
                return;
            }

            bool alreadyRejoining = _state.IsAutoRejoining;
            _state.SetPendingRejoin(lobbyId, hostAddress, joinCode);
            if (onFallbackComplete != null)
            {
                _pendingFallbackCallback += onFallbackComplete;
            }

            if (!alreadyRejoining)
            {
                _consecutiveMirrorFailures = 0;
                _hasNotifiedWaiting = false;
                _nextProbeTime = Time.unscaledTime + InitialProbeDelaySeconds;
                SetTickSubscribed(true);
                NotifyWaiting("Connection lost. Waiting to rejoin match...");
            }
        }

        public void HandleUnexpectedNetworkDisconnect()
        {
            if (_state.IsLobbyOwner) return;

            if (_state.IsAutoRejoining)
            {
                _consecutiveMirrorFailures++;
                if (_consecutiveMirrorFailures >= MaxConsecutiveMirrorFailures)
                {
                    FinishAutoRejoin(clearPreferences: true, leaveLobbyIfConnected: true, "Unable to reconnect to the match host.");
                    return;
                }

                _nextProbeTime = Time.unscaledTime + ProbeIntervalSeconds;
                return;
            }

            if (!_state.IsInLobby || !_state.HasGameStarted) return;

            if (!_state.AllowAutoRejoin)
            {
                _sessions.LeaveLobby();
                return;
            }

            ProductUserId puid = _state.LocalProductUserId ?? ConnectManager.Instance?.LocalProductUserId;
            if (puid != null)
            {
                LobbyPreferences.SetInLobby(
                    puid,
                    _state.CurrentLobbyId,
                    isOwner: false,
                    inMatch: true,
                    _state.CurrentHostAddress,
                    _state.CurrentJoinCode);
            }

            BeginAutoRejoin(_state.CurrentLobbyId, _state.CurrentHostAddress, _state.CurrentJoinCode);
        }

        public bool HandleLobbyDisconnected()
        {
            if (_state.IsLobbyOwner) return false;

            string lobbyId = _state.CurrentLobbyId;
            string hostAddress = _state.CurrentHostAddress;
            string joinCode = _state.CurrentJoinCode;
            bool wasInMatch = _state.HasGameStarted;

            _notifications.UnsubscribeFromSessionNotifications();
            _state.Reset();

            if (_state.AllowAutoRejoin && wasInMatch && !string.IsNullOrEmpty(lobbyId))
            {
                BeginAutoRejoin(lobbyId, hostAddress, joinCode);
                return true;
            }

            LobbyPreferences.ClearInLobby(_state.LocalProductUserId ?? ConnectManager.Instance?.LocalProductUserId);
            return false;
        }

        public void CancelAutoRejoin(bool clearPreferences = true, bool leaveOrphanLobby = true)
        {
            string pendingLobbyId = _state.PendingRejoinLobbyId;
            if (!_state.IsAutoRejoining && _pendingFallbackCallback == null)
            {
                if (clearPreferences)
                {
                    LobbyPreferences.ClearInLobby(_state.LocalProductUserId ?? ConnectManager.Instance?.LocalProductUserId);
                }
                return;
            }

            FinishAutoRejoin(clearPreferences, leaveLobbyIfConnected: false, message: null);

            if (leaveOrphanLobby && !_state.IsInLobby && !string.IsNullOrEmpty(pendingLobbyId))
            {
                _sessions.LeaveLobbyById(pendingLobbyId);
            }
        }

        public void Dispose()
        {
            SetTickSubscribed(false);
            _isProbing = false;
            _pendingFallbackCallback = null;
        }

        private void Tick()
        {
            // Once Mirror finishes reconnecting to the host, complete auto-rejoin cleanly.
            if (NetworkService.Current.IsClientConnected)
            {
                FinishAutoRejoin(clearPreferences: false, leaveLobbyIfConnected: false, message: null);
                return;
            }

            if (_isProbing || NetworkService.Current.IsClientActive || Time.unscaledTime < _nextProbeTime)
            {
                return;
            }

            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                _nextProbeTime = Time.unscaledTime + OfflineCheckIntervalSeconds;
                NotifyWaiting("No internet connection. Waiting to reconnect...");
                return;
            }

            if (EOSSDKManager.PlatformInterface == null || ConnectManager.Instance == null || !ConnectManager.Instance.IsLoggedIn)
            {
                _nextProbeTime = Time.unscaledTime + OfflineCheckIntervalSeconds;
                return;
            }

            _state.LocalProductUserId = ConnectManager.Instance.LocalProductUserId;
            _isProbing = true;
            _sessions.FindLobbyById(_state.PendingRejoinLobbyId, HandleLobbyProbeResult);
        }

        private void HandleLobbyProbeResult(Result result, LobbyDetails detailsHandle)
        {
            _isProbing = false;

            if (!_state.IsAutoRejoining)
            {
                detailsHandle?.Release();
                return;
            }

            if (result == Result.NotFound || (result == Result.Success && detailsHandle == null))
            {
                detailsHandle?.Release();
                FinishAutoRejoin(clearPreferences: true, leaveLobbyIfConnected: true, "Match has ended.");
                return;
            }

            if (result != Result.Success)
            {
                detailsHandle?.Release();
                // Connectivity/timeout/transient EOS errors mean internet/EOS is still unavailable; retry.
                _nextProbeTime = Time.unscaledTime + ProbeIntervalSeconds;
                NotifyWaiting("Connection lost. Waiting for internet to reconnect...");
                return;
            }

            bool matchStarted = LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.MatchStartedKey, out string startedVal)
                && startedVal == "true";
            bool allowsJoinInProgress = LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.AllowJoinInProgressKey, out string allowVal)
                && allowVal == "true";
            LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.HostAddressKey, out string hostAddress);
            LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.JoinCodeKey, out string joinCode);

            if (string.IsNullOrEmpty(hostAddress))
            {
                hostAddress = _state.PendingRejoinHostAddress;
            }

            if (!LobbySessionState.IsHostInLobby(detailsHandle, hostAddress))
            {
                detailsHandle.Release();
                string staleLobbyId = _state.PendingRejoinLobbyId;
                FinishAutoRejoin(clearPreferences: true, leaveLobbyIfConnected: true, "Host is no longer in the match.");
                if (!_state.IsInLobby && !string.IsNullOrEmpty(staleLobbyId))
                {
                    _sessions.LeaveLobbyById(staleLobbyId);
                }
                return;
            }

            ProductUserId localPuid = _state.LocalProductUserId ?? ConnectManager.Instance?.LocalProductUserId;
            bool isStillMember = LobbySessionState.IsUserMemberOfLobby(detailsHandle, localPuid);

            if (!matchStarted)
            {
                if (isStillMember && _state.IsInLobby)
                {
                    detailsHandle.Release();
                    _state.HasGameStarted = false;
                    LobbyPreferences.SetInLobby(localPuid, _state.PendingRejoinLobbyId, isOwner: false, inMatch: false, hostAddress, joinCode);
                    FinishAutoRejoin(clearPreferences: false, leaveLobbyIfConnected: false, "Match has returned to the lobby.");
                }
                else if (isStillMember && !_state.IsInLobby)
                {
                    detailsHandle.Release();
                    _isProbing = true;
                    _sessions.LeaveLobbyById(_state.PendingRejoinLobbyId, HandleStalePresenceLeaveComplete);
                }
                else
                {
                    // Rejoin the open lobby without entering the match (detailsHandle is released by JoinFoundLobbyDetails).
                    _isProbing = true;
                    _sessions.JoinFoundLobbyDetails(detailsHandle, joinCode, isAutoRejoin: true, HandleReturnToLobbyJoinResult);
                }
                return;
            }

            if (!allowsJoinInProgress && !LobbySessionState.IsUserAllowedToRejoin(detailsHandle, localPuid))
            {
                detailsHandle.Release();
                FinishAutoRejoin(clearPreferences: true, leaveLobbyIfConnected: true, "You are no longer allowed to rejoin this match.");
                return;
            }

            if (isStillMember && _state.IsInLobby)
            {
                detailsHandle.Release();
                _state.CurrentHostAddress = hostAddress;
                if (!string.IsNullOrEmpty(joinCode)) _state.CurrentJoinCode = joinCode;
                _state.HasGameStarted = true;
                _notifications.SubscribeToSessionNotifications();
                ConnectMirrorClient(hostAddress);
                return;
            }

            if (isStillMember && !_state.IsInLobby)
            {
                detailsHandle.Release();
                _isProbing = true;
                _sessions.LeaveLobbyById(_state.PendingRejoinLobbyId, HandleStalePresenceLeaveComplete);
                return;
            }

            // Rejoin EOS Lobby first (detailsHandle is released by JoinFoundLobbyDetails).
            _isProbing = true;
            _sessions.JoinFoundLobbyDetails(detailsHandle, joinCode, isAutoRejoin: true, HandleRejoinLobbyResult);
        }

        private void HandleRejoinLobbyResult(bool success, string hostAddressOrError)
        {
            _isProbing = false;

            if (!_state.IsAutoRejoining)
            {
                return;
            }

            if (!success)
            {
                if (string.Equals(hostAddressOrError, "Lobby is full.", StringComparison.Ordinal) ||
                    string.Equals(hostAddressOrError, "Match has already started in this room.", StringComparison.Ordinal))
                {
                    FinishAutoRejoin(clearPreferences: true, leaveLobbyIfConnected: true, hostAddressOrError);
                }
                else if (hostAddressOrError != null &&
                         (hostAddressOrError.Contains("LobbyPresenceLobbyExists", StringComparison.Ordinal) ||
                          hostAddressOrError.Contains("LobbyLobbyAlreadyExists", StringComparison.Ordinal)))
                {
                    _isProbing = true;
                    _sessions.LeaveLobbyById(_state.PendingRejoinLobbyId, HandleStalePresenceLeaveComplete);
                }
                else
                {
                    _nextProbeTime = Time.unscaledTime + ProbeIntervalSeconds;
                }
                return;
            }

            string hostAddress = !string.IsNullOrEmpty(hostAddressOrError)
                ? hostAddressOrError
                : _state.PendingRejoinHostAddress;
            ConnectMirrorClient(hostAddress);
        }

        private void HandleReturnToLobbyJoinResult(bool success, string hostAddressOrError)
        {
            _isProbing = false;

            if (!_state.IsAutoRejoining)
            {
                return;
            }

            if (success)
            {
                FinishAutoRejoin(clearPreferences: false, leaveLobbyIfConnected: false, "Rejoined the lobby.");
            }
            else
            {
                FinishAutoRejoin(clearPreferences: true, leaveLobbyIfConnected: false, hostAddressOrError);
            }
        }

        private void HandleStalePresenceLeaveComplete()
        {
            _isProbing = false;
            _nextProbeTime = Time.unscaledTime + 0.5f;
        }

        private void ConnectMirrorClient(string hostAddress)
        {
            if (string.IsNullOrEmpty(hostAddress))
            {
                FinishAutoRejoin(clearPreferences: true, leaveLobbyIfConnected: true, "Match host address is unavailable.");
                return;
            }

            if (!NetworkService.Current.IsClientActive && !NetworkService.Current.IsClientConnected)
            {
                Debug.Log($"[LobbyRejoinService] Internet/lobby reachable. Auto-reconnecting to host {hostAddress}...");
                FriendsOverlayManager.Instance?.PersistForGameScene();
                NetworkService.Current.StartClient(hostAddress);
            }
        }

        private void FinishAutoRejoin(bool clearPreferences, bool leaveLobbyIfConnected, string message)
        {
            SetTickSubscribed(false);
            _isProbing = false;
            _consecutiveMirrorFailures = 0;
            _hasNotifiedWaiting = false;
            _state.ClearPendingRejoin();

            if (clearPreferences)
            {
                LobbyPreferences.ClearInLobby(_state.LocalProductUserId ?? ConnectManager.Instance?.LocalProductUserId);
            }

            if (leaveLobbyIfConnected && _state.IsInLobby)
            {
                _sessions.LeaveLobby();
            }

            Action fallback = _pendingFallbackCallback;
            _pendingFallbackCallback = null;
            OnAutoRejoinStateChanged?.Invoke(false, message);
            fallback?.Invoke();
        }

        private void SetTickSubscribed(bool subscribe)
        {
            if (_isTickSubscribed == subscribe) return;
            EOSPlatformService service = EOSSDKManager.Instance?.Service;
            if (service == null) return;

            if (subscribe)
            {
                service.OnTick -= Tick;
                service.OnTick += Tick;
                _isTickSubscribed = true;
            }
            else
            {
                service.OnTick -= Tick;
                _isTickSubscribed = false;
            }
        }

        private void NotifyWaiting(string message)
        {
            if (_hasNotifiedWaiting) return;
            _hasNotifiedWaiting = true;
            OnAutoRejoinStateChanged?.Invoke(true, message);
        }
    }
}
