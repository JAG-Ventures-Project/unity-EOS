using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using Mirror;

namespace EOS
{
    public class EndGame : MonoBehaviour
    {
        [SerializeField] private Button endGameButton;
        [SerializeField, HideInInspector] private string nextSceneName = "Lobby"; 

        private ILobby _lobby;

#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset nextScene;
        
        private void OnValidate()
        {
            nextSceneName = (nextScene != null) ? nextScene.name : "Lobby";
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
                _lobby.ResetMatchState((success, error) =>
                {
                    if (!success)
                    {
                        Debug.LogError($"[EndGame] Failed to reset match state in EOS lobby: {error}");
                        NotificationManager.Instance?.ShowNotification($"Match reset error: {error}", NotificationType.Error);
                    }

                    // Stop host in Mirror: disconnects all clients and automatically loads offlineScene (Lobby.unity)
                    EOSNetworkManager.Instance?.StopHost();
                });
            }
            else
            {
                // Client: leave the old EOS lobby, stop Mirror client, and load the Lobby scene (Create/Join menu)
                _lobby.LeaveLobby(_ =>
                {
                    EOSNetworkManager.Instance.StopClient();
                    SceneManager.LoadScene(nextSceneName);
                });
            }
        }
    }
}