using System;
using EOS.Core;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

namespace EOS.Lobby
{
    /// <summary>
    /// Shared mutable session state and low-level EOS LobbyDetails/attribute helpers
    /// used by LobbyManager and its internal services.
    /// </summary>
    internal sealed class LobbySessionState
    {
        public string CurrentLobbyId { get; set; }
        public string CurrentJoinCode { get; set; }
        public int CurrentMemberCount { get; set; }
        public bool IsLobbyOwner { get; set; }
        public bool AreAllJoinersReady { get; set; }
        public bool IsCleaningOrphan { get; set; }
        public bool HasGameStarted { get; set; }
        public bool AllowJoinInProgress { get; set; }
        public string CurrentHostAddress { get; set; }
        public ProductUserId LocalProductUserId { get; set; }

        public bool IsInLobby => !string.IsNullOrEmpty(CurrentLobbyId);

        public string EffectiveHostAddress =>
            IsLobbyOwner
                ? (LocalProductUserId != null ? LocalProductUserId.ToString() : null)
                : CurrentHostAddress;

        public void Reset()
        {
            CurrentLobbyId = null;
            CurrentJoinCode = null;
            IsLobbyOwner = false;
            CurrentMemberCount = 0;
            AreAllJoinersReady = false;
            CurrentHostAddress = null;
            HasGameStarted = false;
        }

        public static string ResolveLocalDisplayName(string fallback = "Player")
        {
            return !string.IsNullOrWhiteSpace(EOSSDKManager.LocalDisplayName)
                ? EOSSDKManager.LocalDisplayName
                : fallback;
        }

        public static bool TryReadAttribute(LobbyDetails detailsHandle, string key, out string value)
        {
            value = string.Empty;
            if (detailsHandle == null) return false;

            var options = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key };
            if (detailsHandle.CopyAttributeByKey(options, out Epic.OnlineServices.Lobby.Attribute attr) == Result.Success &&
                attr?.Data?.Value != null)
            {
                value = attr.Data.Value.AsUtf8 ?? string.Empty;
                return true;
            }
            return false;
        }

        public static bool TryReadMemberAttribute(LobbyDetails detailsHandle, ProductUserId targetUserId, string key, out string value)
        {
            value = string.Empty;
            if (detailsHandle == null || targetUserId == null) return false;

            var options = new LobbyDetailsCopyMemberAttributeByKeyOptions
            {
                TargetUserId = targetUserId,
                AttrKey = key
            };
            if (detailsHandle.CopyMemberAttributeByKey(options, out Epic.OnlineServices.Lobby.Attribute attr) == Result.Success &&
                attr?.Data?.Value != null)
            {
                value = attr.Data.Value.AsUtf8 ?? string.Empty;
                return true;
            }
            return false;
        }

        public bool TryCopyCurrentDetails(LobbyInterface lobbyHandle, out LobbyDetails detailsHandle)
        {
            detailsHandle = null;
            if (lobbyHandle == null || string.IsNullOrEmpty(CurrentLobbyId)) return false;

            var copyOptions = new CopyLobbyDetailsHandleOptions
            {
                LobbyId = CurrentLobbyId,
                LocalUserId = LocalProductUserId
            };
            return lobbyHandle.CopyLobbyDetailsHandle(copyOptions, out detailsHandle) == Result.Success && detailsHandle != null;
        }
    }
}

