using EOS.Core;
using EOS.Friends;
using EOS.Lobby;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;

namespace EOS.Test.Cursor
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
            _lobby = LobbyManager.Instance;
        }

        private void OnDestroy()
        {
            endGameButton.onClick.RemoveListener(OnEndGameButtonClicked);
        }

        private void OnEndGameButtonClicked()
        {
            endGameButton.interactable = false;

            if (!FriendsManager.UseLobbySystem)
            {
                NetworkService.Current.StopNetwork();
                return;
            }

            var lobby = _lobby ?? LobbyManager.Instance;

            // Host/Server check: host resets the match state in EOS lobby and brings all players back
            if (NetworkService.Current.IsServerActive)
            {
                if (lobby != null)
                {
                    lobby.ResetMatchState(HandleHostResetMatchCallback);
                }
                else
                {
                    NetworkService.Current.StopNetwork();
                }
            }
            else
            {
                // Client: leave the old EOS lobby and stop client
                if (lobby != null && lobby.IsInLobby)
                {
                    lobby.LeaveLobby(HandleClientLeaveLobbyCallback);
                }
                else
                {
                    NetworkService.Current.StopNetwork();
                }
            }
        }

        private void HandleHostResetMatchCallback(bool success, string error)
        {
            if (!success)
            {
                Debug.LogWarning($"[EndGame] Host reset match callback note: {error}");
            }

            // Stop host in network session: disconnects all clients and automatically loads offlineScene
            NetworkService.Current.StopNetwork();
        }

        private void HandleClientLeaveLobbyCallback(bool success)
        {
            NetworkService.Current.StopNetwork();
        }
    }
}