using System;
using System.Collections.Generic;
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
        public bool UseShortJoinCode { get; set; } = true;
        public bool AllowAutoRejoin { get; set; } = true;
        public string CurrentHostAddress { get; set; }
        public ProductUserId LocalProductUserId { get; set; }

        public HashSet<string> AllowedRejoiners { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsAutoRejoining { get; set; }
        public string PendingRejoinLobbyId { get; set; }
        public string PendingRejoinHostAddress { get; set; }
        public string PendingRejoinJoinCode { get; set; }

        public bool IsInLobby => !string.IsNullOrEmpty(CurrentLobbyId);

        public string EffectiveHostAddress =>
            IsLobbyOwner
                ? (LocalProductUserId != null ? LocalProductUserId.ToString() : null)
                : CurrentHostAddress;

        public void SetPendingRejoin(string lobbyId, string hostAddress, string joinCode)
        {
            PendingRejoinLobbyId = lobbyId;
            PendingRejoinHostAddress = hostAddress;
            PendingRejoinJoinCode = joinCode;
            IsAutoRejoining = !string.IsNullOrEmpty(lobbyId);
        }

        public void ClearPendingRejoin()
        {
            IsAutoRejoining = false;
            PendingRejoinLobbyId = null;
            PendingRejoinHostAddress = null;
            PendingRejoinJoinCode = null;
        }

        public bool IsRejoinAllowed(string productUserId)
        {
            return !string.IsNullOrEmpty(productUserId) && AllowedRejoiners.Contains(productUserId);
        }

        public void Reset()
        {
            CurrentLobbyId = null;
            CurrentJoinCode = null;
            IsLobbyOwner = false;
            CurrentMemberCount = 0;
            AreAllJoinersReady = false;
            CurrentHostAddress = null;
            HasGameStarted = false;
            AllowedRejoiners.Clear();
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

        public static void ParseAllowedRejoiners(LobbyDetails detailsHandle, HashSet<string> target)
        {
            if (target == null) return;
            target.Clear();

            if (!TryReadAttribute(detailsHandle, LobbyManager.AllowedRejoinersKey, out string raw) ||
                string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    target.Add(trimmed);
                }
            }
        }

        public static bool IsUserAllowedToRejoin(LobbyDetails detailsHandle, ProductUserId userId)
        {
            if (detailsHandle == null || userId == null || !userId.IsValid()) return false;

            if (!TryReadAttribute(detailsHandle, LobbyManager.AllowedRejoinersKey, out string raw) ||
                string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string target = userId.ToString();
            int start = 0;
            while (start < raw.Length)
            {
                int comma = raw.IndexOf(',', start);
                int end = (comma < 0) ? raw.Length : comma;

                while (start < end && char.IsWhiteSpace(raw[start])) start++;
                while (end > start && char.IsWhiteSpace(raw[end - 1])) end--;

                int length = end - start;
                if (length == target.Length &&
                    string.Compare(raw, start, target, 0, length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }

                start = (comma < 0) ? raw.Length : comma + 1;
            }

            return false;
        }

        public static bool IsUserMemberOfLobby(LobbyDetails detailsHandle, ProductUserId userId)
        {
            if (detailsHandle == null || userId == null || !userId.IsValid()) return false;

            uint count = detailsHandle.GetMemberCount(new LobbyDetailsGetMemberCountOptions());
            for (uint i = 0; i < count; i++)
            {
                ProductUserId member = detailsHandle.GetMemberByIndex(new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i });
                if (member == userId)
                {
                    return true;
                }
            }
            return false;
        }

        // AI implementation: Verifies the original match host is still the active lobby owner.
        // Uses LobbyDetailsInfo rather than GetMemberByIndex because EOS search results do not populate member PUIDs for non-members.
        public static bool IsHostInLobby(LobbyDetails detailsHandle, string hostAddress)
        {
            if (detailsHandle == null || string.IsNullOrEmpty(hostAddress)) return false;

            ProductUserId hostPuid = ProductUserId.FromString(hostAddress);
            if (hostPuid == null || !hostPuid.IsValid()) return false;

            if (detailsHandle.CopyInfo(new LobbyDetailsCopyInfoOptions(), out LobbyDetailsInfo info) != Result.Success ||
                info == null ||
                info.LobbyOwnerUserId == null ||
                !info.LobbyOwnerUserId.IsValid())
            {
                return false;
            }

            if (info.LobbyOwnerUserId != hostPuid &&
                !string.Equals(info.LobbyOwnerUserId.ToString(), hostAddress, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return info.AvailableSlots < info.MaxMembers;
        }
    }
}

