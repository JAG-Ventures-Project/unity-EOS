using UnityEngine;
using Epic.OnlineServices;
using UnityEngine.Assertions;

namespace EOS.Lobby
{
    // AI implementation: Persists active lobby session state to detect dirty exits / crashes per ProductUserId.
    internal static class LobbyPreferences
    {
        private const string KeyInLobbyPrefix = "EOS_IsInLobby_";
        private const string KeyLobbyIdPrefix = "EOS_LastLobbyId_";
        private const string KeyIsOwnerPrefix = "EOS_IsLobbyOwner_";
        private const string KeyInMatchPrefix = "EOS_InMatch_";
        private const string KeyHostAddressPrefix = "EOS_HostAddress_";
        private const string KeyJoinCodePrefix = "EOS_JoinCode_";

        internal static void SetInLobby(
            ProductUserId puid,
            string lobbyId,
            bool isOwner,
            bool inMatch = false,
            string hostAddress = null,
            string joinCode = null)
        {
            Assert.IsNotNull(puid, "[LobbyPreferences] ProductUserId must not be null.");
            Assert.IsFalse(string.IsNullOrEmpty(lobbyId), "[LobbyPreferences] LobbyId must not be null or empty.");

            string idStr = puid.ToString();
            PlayerPrefs.SetInt(KeyInLobbyPrefix + idStr, 1);
            PlayerPrefs.SetString(KeyLobbyIdPrefix + idStr, lobbyId);
            PlayerPrefs.SetInt(KeyIsOwnerPrefix + idStr, isOwner ? 1 : 0);
            PlayerPrefs.SetInt(KeyInMatchPrefix + idStr, inMatch ? 1 : 0);
            PlayerPrefs.SetString(KeyHostAddressPrefix + idStr, hostAddress ?? string.Empty);
            PlayerPrefs.SetString(KeyJoinCodePrefix + idStr, joinCode ?? string.Empty);
            PlayerPrefs.Save();
        }

        internal static void ClearInLobby(ProductUserId puid)
        {
            if (puid == null) return;
            string idStr = puid.ToString();
            PlayerPrefs.DeleteKey(KeyInLobbyPrefix + idStr);
            PlayerPrefs.DeleteKey(KeyLobbyIdPrefix + idStr);
            PlayerPrefs.DeleteKey(KeyIsOwnerPrefix + idStr);
            PlayerPrefs.DeleteKey(KeyInMatchPrefix + idStr);
            PlayerPrefs.DeleteKey(KeyHostAddressPrefix + idStr);
            PlayerPrefs.DeleteKey(KeyJoinCodePrefix + idStr);
            PlayerPrefs.Save();
        }

        internal static bool HasOrphanLobby(ProductUserId puid, out string lobbyId, out bool isOwner)
        {
            return HasOrphanLobby(puid, out lobbyId, out isOwner, out _, out _, out _);
        }

        internal static bool HasOrphanLobby(
            ProductUserId puid,
            out string lobbyId,
            out bool isOwner,
            out bool inMatch,
            out string hostAddress,
            out string joinCode)
        {
            lobbyId = string.Empty;
            isOwner = false;
            inMatch = false;
            hostAddress = string.Empty;
            joinCode = string.Empty;

            if (puid == null) return false;

            string idStr = puid.ToString();
            bool inLobby = PlayerPrefs.GetInt(KeyInLobbyPrefix + idStr, 0) == 1;
            if (!inLobby) return false;

            lobbyId = PlayerPrefs.GetString(KeyLobbyIdPrefix + idStr, string.Empty);
            isOwner = PlayerPrefs.GetInt(KeyIsOwnerPrefix + idStr, 0) == 1;
            inMatch = PlayerPrefs.GetInt(KeyInMatchPrefix + idStr, 0) == 1;
            hostAddress = PlayerPrefs.GetString(KeyHostAddressPrefix + idStr, string.Empty);
            joinCode = PlayerPrefs.GetString(KeyJoinCodePrefix + idStr, string.Empty);
            return !string.IsNullOrEmpty(lobbyId);
        }
    }
}