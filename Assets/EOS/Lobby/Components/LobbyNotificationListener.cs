using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

namespace EOS.Lobby
{
    /// <summary>
    /// Manages EOS lobby notification handle subscriptions and dispatches invite, attribute, and member events.
    /// </summary>
    internal sealed class LobbyNotificationListener : IDisposable
    {
        private const ulong InvalidNotificationId = Epic.OnlineServices.Common.InvalidNotificationid;

        private readonly LobbyInterface _lobbyHandle;
        private readonly LobbySessionState _state;
        private readonly LobbyAttributeService _attributes;

        private ulong _memberStatusNotificationId = InvalidNotificationId;
        private ulong _lobbyUpdateNotificationId = InvalidNotificationId;
        private ulong _memberUpdateNotificationId = InvalidNotificationId;
        private ulong _inviteNotificationId = InvalidNotificationId;

        public event Action<LobbyInviteCallbackData> OnInviteReceived;
        public event Action OnLobbyUpdated;
        public event Action<string> OnGameStarted;
        public event Action<ProductUserId> OnPlayerJoined;
        public event Action<ProductUserId> OnPlayerLeft;
        public event Action<string> OnUnexpectedLeft;

        public LobbyNotificationListener(
            LobbyInterface lobbyHandle,
            LobbySessionState state,
            LobbyAttributeService attributes)
        {
            _lobbyHandle = lobbyHandle;
            _state = state;
            _attributes = attributes;
        }

        public void SubscribeToInviteNotifications()
        {
            if (_inviteNotificationId != InvalidNotificationId) return;

            var options = new AddNotifyLobbyInviteReceivedOptions();
            _inviteNotificationId = _lobbyHandle.AddNotifyLobbyInviteReceived(options, this, OnLobbyInviteReceivedCallback);
        }

        public void UnsubscribeFromInviteNotifications()
        {
            if (_inviteNotificationId != InvalidNotificationId)
            {
                _lobbyHandle.RemoveNotifyLobbyInviteReceived(_inviteNotificationId);
                _inviteNotificationId = InvalidNotificationId;
            }
        }

        public void SubscribeToSessionNotifications()
        {
            UnsubscribeFromSessionNotifications();

            _memberStatusNotificationId = _lobbyHandle.AddNotifyLobbyMemberStatusReceived(
                new AddNotifyLobbyMemberStatusReceivedOptions(), this, OnMemberStatusChanged);

            _lobbyUpdateNotificationId = _lobbyHandle.AddNotifyLobbyUpdateReceived(
                new AddNotifyLobbyUpdateReceivedOptions(), this, OnLobbyUpdateReceived);

            _memberUpdateNotificationId = _lobbyHandle.AddNotifyLobbyMemberUpdateReceived(
                new AddNotifyLobbyMemberUpdateReceivedOptions(), this, OnLobbyMemberUpdateReceived);
        }

        public void UnsubscribeFromSessionNotifications()
        {
            if (_memberStatusNotificationId != InvalidNotificationId)
            {
                _lobbyHandle.RemoveNotifyLobbyMemberStatusReceived(_memberStatusNotificationId);
                _memberStatusNotificationId = InvalidNotificationId;
            }

            if (_lobbyUpdateNotificationId != InvalidNotificationId)
            {
                _lobbyHandle.RemoveNotifyLobbyUpdateReceived(_lobbyUpdateNotificationId);
                _lobbyUpdateNotificationId = InvalidNotificationId;
            }

            if (_memberUpdateNotificationId != InvalidNotificationId)
            {
                _lobbyHandle.RemoveNotifyLobbyMemberUpdateReceived(_memberUpdateNotificationId);
                _memberUpdateNotificationId = InvalidNotificationId;
            }
        }

        private static void OnLobbyInviteReceivedCallback(LobbyInviteReceivedCallbackInfo data)
        {
            var listener = data.ClientData as LobbyNotificationListener;
            if (listener == null || string.IsNullOrEmpty(data.InviteId)) return;

            var copyOptions = new CopyLobbyDetailsHandleByInviteIdOptions { InviteId = data.InviteId };
            Result result = listener._lobbyHandle.CopyLobbyDetailsHandleByInviteId(copyOptions, out LobbyDetails detailsHandle);
            if (result != Result.Success || detailsHandle == null) return;

            detailsHandle.CopyInfo(new LobbyDetailsCopyInfoOptions(), out LobbyDetailsInfo info);

            LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.JoinCodeKey, out string joinCode);
            LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.HostAddressKey, out string hostAddress);
            LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.HostNameKey, out string hostName);

            if (string.IsNullOrEmpty(hostName) && info?.LobbyOwnerUserId != null)
            {
                LobbySessionState.TryReadMemberAttribute(detailsHandle, info.LobbyOwnerUserId, LobbyManager.DisplayNameKey, out hostName);
            }

            if (string.IsNullOrEmpty(hostName) && info?.LobbyOwnerUserId != null)
            {
                string externalName = LobbyManager.ExternalDisplayNameResolver?.Invoke(info.LobbyOwnerUserId.ToString());
                if (!string.IsNullOrEmpty(externalName))
                {
                    hostName = externalName;
                }
            }

            var callbackData = new LobbyInviteCallbackData
            {
                InviteId = data.InviteId,
                LobbyId = info?.LobbyId,
                InviterUserId = info?.LobbyOwnerUserId,
                InviterDisplayName = hostName,
                JoinCode = joinCode,
                HostAddress = hostAddress,
                AvailableSlots = info?.AvailableSlots ?? 0,
                MaxMembers = info?.MaxMembers ?? 0
            };

            detailsHandle.Release();
            listener.OnInviteReceived?.Invoke(callbackData);
        }

        private static void OnLobbyMemberUpdateReceived(LobbyMemberUpdateReceivedCallbackInfo data)
        {
            var listener = data.ClientData as LobbyNotificationListener;
            if (listener == null || string.IsNullOrEmpty(data.LobbyId) || data.LobbyId != listener._state.CurrentLobbyId)
                return;

            listener._attributes.UpdateAndCheckReadyStatus();
            listener.OnLobbyUpdated?.Invoke();
        }

        private static void OnLobbyUpdateReceived(LobbyUpdateReceivedCallbackInfo data)
        {
            var listener = data.ClientData as LobbyNotificationListener;
            if (listener == null || string.IsNullOrEmpty(data.LobbyId) || data.LobbyId != listener._state.CurrentLobbyId)
                return;

            if (!listener._state.TryCopyCurrentDetails(listener._lobbyHandle, out LobbyDetails detailsHandle))
                return;

            if (LobbySessionState.TryReadAttribute(detailsHandle, LobbyManager.MatchStartedKey, out string matchStartedValue))
            {
                if (matchStartedValue == "true")
                {
                    if (!listener._state.HasGameStarted)
                    {
                        listener._state.HasGameStarted = true;
                        if (!listener._state.IsLobbyOwner)
                        {
                            listener.OnGameStarted?.Invoke(listener._state.CurrentHostAddress);
                        }
                    }
                }
                else if (matchStartedValue == "false")
                {
                    listener._state.HasGameStarted = false;
                }
            }

            detailsHandle.Release();
            listener._attributes.UpdateAndCheckReadyStatus();
            listener.OnLobbyUpdated?.Invoke();
        }

        private static void OnMemberStatusChanged(LobbyMemberStatusReceivedCallbackInfo data)
        {
            var listener = data.ClientData as LobbyNotificationListener;
            if (listener == null || string.IsNullOrEmpty(data.LobbyId) || data.LobbyId != listener._state.CurrentLobbyId)
                return;

            if (data.TargetUserId == listener._state.LocalProductUserId)
            {
                if (data.CurrentStatus == LobbyMemberStatus.Disconnected || data.CurrentStatus == LobbyMemberStatus.Kicked)
                {
                    listener.OnUnexpectedLeft?.Invoke($"Disconnected from room ({data.CurrentStatus}).");
                    return;
                }
            }

            switch (data.CurrentStatus)
            {
                case LobbyMemberStatus.Joined:
                    listener._attributes.UpdateAndCheckReadyStatus();
                    listener.OnPlayerJoined?.Invoke(data.TargetUserId);
                    break;
                case LobbyMemberStatus.Left:
                case LobbyMemberStatus.Disconnected:
                case LobbyMemberStatus.Kicked:
                    listener._attributes.UpdateAndCheckReadyStatus();
                    listener.OnPlayerLeft?.Invoke(data.TargetUserId);
                    break;
                case LobbyMemberStatus.Closed:
                    if (!listener._state.IsLobbyOwner)
                    {
                        listener.OnUnexpectedLeft?.Invoke("The host closed the room.");
                    }
                    break;
            }
        }

        public void Dispose()
        {
            UnsubscribeFromInviteNotifications();
            UnsubscribeFromSessionNotifications();
        }
    }
}

