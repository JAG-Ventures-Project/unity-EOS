using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
using Mirror;

namespace EOS
{
    public class EndGame : MonoBehaviour
    {
        [SerializeField] private Button endGameButton;

        private ILobby _lobby;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(endGameButton, "[EndGame] endGameButton is null! Please assign in inspector.");
        }
#endif

        private void Awake()
        {
            endGameButton.onClick.AddListener(OnEndGameButtonClicked);
            _lobby = EOSSDKManager.Instance?.Lobby;
        }

        private void OnDestroy()
        {
            endGameButton.onClick.RemoveListener(OnEndGameButtonClicked);
        }

        private void OnEndGameButtonClicked()
        {
            endGameButton.interactable = false;

            // Host/Server check: host resets the match state in EOS lobby and brings all players back
            if (NetworkServer.active)
            {
                _lobby.ResetMatchState(HandleHostResetMatchCallback);
            }
            else
            {
                // Client: leave the old EOS lobby and stop Mirror client
                _lobby.LeaveLobby(HandleClientLeaveLobbyCallback);
            }
        }

        private void HandleHostResetMatchCallback(bool success, string error)
        {
            if (!success)
            {
                Debug.LogError($"[EndGame] Failed to reset match state in EOS lobby: {error}");
                NotificationManager.Show($"Match reset error: {error}", NotificationType.Error);
            }

            // Stop host in Mirror: disconnects all clients and automatically loads offlineScene (Lobby.unity)
            EOSNetworkManager.Instance?.StopNetwork();
        }

        private void HandleClientLeaveLobbyCallback(bool success)
        {
            EOSNetworkManager.Instance?.StopNetwork();
        }
    }
}