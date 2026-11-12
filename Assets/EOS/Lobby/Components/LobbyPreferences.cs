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

        internal static void SetInLobby(ProductUserId puid, string lobbyId, bool isOwner)
        {
            Assert.IsNotNull(puid, "[LobbyPreferences] ProductUserId must not be null.");
            Assert.IsFalse(string.IsNullOrEmpty(lobbyId), "[LobbyPreferences] LobbyId must not be null or empty.");

            string idStr = puid.ToString();
            PlayerPrefs.SetInt(KeyInLobbyPrefix + idStr, 1);
            PlayerPrefs.SetString(KeyLobbyIdPrefix + idStr, lobbyId);
            PlayerPrefs.SetInt(KeyIsOwnerPrefix + idStr, isOwner ? 1 : 0);
            PlayerPrefs.Save();
        }

        internal static void ClearInLobby(ProductUserId puid)
        {
            if (puid == null) return;
            string idStr = puid.ToString();
            PlayerPrefs.DeleteKey(KeyInLobbyPrefix + idStr);
            PlayerPrefs.DeleteKey(KeyLobbyIdPrefix + idStr);
            PlayerPrefs.DeleteKey(KeyIsOwnerPrefix + idStr);
            PlayerPrefs.Save();
        }

        internal static bool HasOrphanLobby(ProductUserId puid, out string lobbyId, out bool isOwner)
        {
            lobbyId = string.Empty;
            isOwner = false;

            string idStr = puid.ToString();
            bool inLobby = PlayerPrefs.GetInt(KeyInLobbyPrefix + idStr, 0) == 1;
            if (!inLobby) return false;

            lobbyId = PlayerPrefs.GetString(KeyLobbyIdPrefix + idStr, string.Empty);
            isOwner = PlayerPrefs.GetInt(KeyIsOwnerPrefix + idStr, 0) == 1;
            return !string.IsNullOrEmpty(lobbyId);
        }
    }
}