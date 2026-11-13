using System;
using System.Collections.Generic;
using EOS.Core;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.Platform;
using UnityEngine.Assertions;

namespace EOS.Lobby
{
    /// <summary>
    /// Facade implementing ILobby by delegating to focused session, attribute, and notification services.
    /// </summary>
    public class LobbyManager : ILobby
    {
        public const string JoinCodeKey = "JOIN_CODE";
        public const string HostAddressKey = "HOST_ADDRESS";
        public const string MatchStartedKey = "MATCH_STARTED";
        public const string AllowJoinInProgressKey = "ALLOW_JOIN_IN_PROGRESS";
        public const string ReadyKey = "READY";
        public const string DisplayNameKey = "DISPLAY_NAME";
        public const string HostNameKey = "HOST_NAME";

        public static Func<string, string> ExternalDisplayNameResolver { get; set; }

        /// <summary>
        /// Optional delegate for custom lobby join code generation (e.g., 5-character short codes provided by test components).
        /// When null, CurrentLobbyId is used directly as the join code.
        /// </summary>
        public static Func<string> CustomJoinCodeGenerator { get; set; }

        public string CurrentLobbyId => _state.CurrentLobbyId;
        public string CurrentJoinCode => _state.CurrentJoinCode;
        public int CurrentMemberCount => _state.CurrentMemberCount;
        public bool IsInLobby => _state.IsInLobby;
        public bool IsLobbyOwner => _state.IsLobbyOwner;
        public bool AreAllJoinersReady => _state.AreAllJoinersReady;
        public bool IsCleaningOrphan => _state.IsCleaningOrphan;
        public bool HasGameStarted => _state.HasGameStarted;
        public bool AllowJoinInProgress
        {
            get => _state.AllowJoinInProgress;
            set => _state.AllowJoinInProgress = value;
        }
        public ProductUserId LocalProductUserId => _state.LocalProductUserId;
        public string CurrentHostAddress => _state.EffectiveHostAddress;
        public bool IsLocalReady => _attributes.IsMemberReady(_state.LocalProductUserId);

        public event Action<string> OnLobbyCreated;
        public event Action<string> OnLobbyJoined;
        public event Action OnLobbyLeft;
        public event Action<ProductUserId> OnPlayerJoined;
        public event Action<ProductUserId> OnPlayerLeft;
        public event Action<string> OnLobbyError;
        public event Action<string> OnGameStarted;
        public event Action<bool> OnReadyStatusChanged;
        public event Action OnLobbyUpdated;
        public event Action<LobbyInviteCallbackData> OnLobbyInviteReceived;

        private readonly PlatformInterface _platformHandle;
        private readonly LobbyInterface _lobbyHandle;
        private readonly LobbySessionState _state = new();
        private readonly LobbyAttributeService _attributes;
        private readonly LobbyNotificationListener _notifications;
        private readonly LobbySessionService _sessions;

        private static LobbyManager _instance;
        public static LobbyManager Instance => _instance ??=
            (EOSSDKManager.PlatformInterface != null
                ? new LobbyManager(EOSSDKManager.PlatformInterface)
                : null);

        public LobbyManager(PlatformInterface platformInterface = null)
        {
            _platformHandle = platformInterface ?? EOSSDKManager.PlatformInterface;
            Assert.IsNotNull(_platformHandle, "[LobbyManager] PlatformInterface must not be null.");

            _lobbyHandle = _platformHandle.GetLobbyInterface();
            Assert.IsNotNull(_lobbyHandle, "[LobbyManager] LobbyInterface failed to initialize.");

            _attributes = new LobbyAttributeService(
                _lobbyHandle,
                _state,
                CompleteOperation,
                ready => OnReadyStatusChanged?.Invoke(ready));

            _notifications = new LobbyNotificationListener(_lobbyHandle, _state, _attributes);
            _notifications.OnInviteReceived += invite => OnLobbyInviteReceived?.Invoke(invite);
            _notifications.OnLobbyUpdated += () => OnLobbyUpdated?.Invoke();
            _notifications.OnGameStarted += host => OnGameStarted?.Invoke(host);
            _notifications.OnPlayerJoined += userId => OnPlayerJoined?.Invoke(userId);
            _notifications.OnPlayerLeft += userId => OnPlayerLeft?.Invoke(userId);
            _notifications.OnUnexpectedLeft += HandleUnexpectedLobbyLeft;

            _sessions = new LobbySessionService(
                _lobbyHandle,
                _state,
                _attributes,
                _notifications,
                code => OnLobbyCreated?.Invoke(code),
                host => OnLobbyJoined?.Invoke(host),
                () => OnLobbyLeft?.Invoke(),
                host => OnGameStarted?.Invoke(host),
                CompleteOperation,
                CompleteOperation);

            _notifications.SubscribeToInviteNotifications();
            EOSSDKManager.OnShutdown += Dispose;
        }

        private void HandleUnexpectedLobbyLeft(string error)
        {
            _sessions.ResetState();
            OnLobbyLeft?.Invoke();
            OnLobbyError?.Invoke(error);
        }

        public void CreateLobby(uint maxMembers, Action<bool, string> onComplete = null)
            => _sessions.CreateLobby(maxMembers, onComplete);

        public void CheckAndCleanOrphanLobbyOnLogin(Action onComplete = null)
            => _sessions.CheckAndCleanOrphanLobbyOnLogin(onComplete);

        public void JoinLobbyByCode(string joinCode, Action<bool, string> onComplete = null)
            => _sessions.JoinLobbyByCode(joinCode, onComplete);

        public void JoinLobbyByInvite(string inviteId, Action<bool, string> onComplete = null)
            => _sessions.JoinLobbyByInvite(inviteId, onComplete);

        public void SendInvite(ProductUserId targetUserId, Action<bool, string> onComplete = null)
            => _sessions.SendInvite(targetUserId, onComplete);

        public void RejectInvite(string inviteId, Action<bool, string> onComplete = null)
            => _sessions.RejectInvite(inviteId, onComplete);

        public void LeaveLobby(Action<bool> onComplete = null)
            => _sessions.LeaveLobby(onComplete);

        public void KickMember(ProductUserId targetUserId, Action<bool, string> onComplete = null)
            => _sessions.KickMember(targetUserId, onComplete);

        public void StartGame(Action<bool, string> onComplete = null)
            => _attributes.StartGame(onComplete);

        public void SetReadyStatus(bool isReady, Action<bool, string> onComplete = null)
            => _attributes.SetReadyStatus(isReady, onComplete);

        public void ToggleReadyStatus(Action<bool, string> onComplete = null)
            => _attributes.ToggleReadyStatus(onComplete);

        public void ResetLocalReadyStatus(Action<bool, string> onComplete = null)
            => _attributes.ResetLocalReadyStatus(onComplete);

        public void ResetMatchState(Action<bool, string> onComplete = null)
            => _attributes.ResetMatchState(onComplete);

        public void SetLocalMemberDisplayName(string displayName, Action<bool, string> onComplete = null)
            => _attributes.SetLocalMemberDisplayName(displayName, onComplete);

        public List<ProductUserId> GetCurrentMemberUserIds()
            => _attributes.GetCurrentMemberUserIds();

        public string GetMemberDisplayName(ProductUserId userId)
            => _attributes.GetMemberDisplayName(userId);

        public string ResolveDisplayName(string address)
            => _attributes.ResolveDisplayName(address);

        public bool IsMemberReady(ProductUserId userId)
            => _attributes.IsMemberReady(userId);

        public bool UpdateAndCheckReadyStatus()
            => _attributes.UpdateAndCheckReadyStatus();

        public void HandleNetworkDisconnect()
        {
            if (IsInLobby && HasGameStarted)
            {
                LeaveLobby();
            }
        }

        private void CompleteOperation(Action<bool, string> onComplete, bool success, string resultOrError = null)
        {
            if (!success)
            {
                OnLobbyError?.Invoke(resultOrError);
            }

            onComplete?.Invoke(success, resultOrError);
        }

        private void CompleteOperation(Action<bool> onComplete, bool success, string error = null)
        {
            if (!success)
            {
                OnLobbyError?.Invoke(error);
            }

            onComplete?.Invoke(success);
        }

        public void Dispose()
        {
            EOSSDKManager.OnShutdown -= Dispose;
            _instance = null;
            _notifications.Dispose();
            LobbyPreferences.ClearInLobby(_state.LocalProductUserId);

            if (IsInLobby)
            {
                LeaveLobby();
            }
        }
    }
}
