using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using EpicTransport;
using Minimap.FPC;
using Epic.OnlineServices;

namespace EOS
{
    [RequireComponent(typeof(EosTransport))]   
    [DisallowMultipleComponent]
    public class EOSNetworkManager : NetworkManager
    {
        [Header("Pet Settings")]
        [SerializeField] private GameObject petPrefab;

        private readonly Dictionary<NetworkConnectionToClient, GameObject> _spawnedPets = new();
        private bool _isVoluntaryDisconnect;

        public static EOSNetworkManager Instance => singleton as EOSNetworkManager;
        
        private EosTransport _eosTransport;

#if UNITY_EDITOR
        
        private void OnValidate()
        {
            Assert.IsNotNull(petPrefab, "Pet Prefab is null!");
        }
        
#endif

        public override void Awake()
        {
            base.Awake();
            TryGetComponent(out _eosTransport);
        }    

#region Lobby UI Connection Handlers

        public void StartEosHost(string targetScene = null)
        {
            onlineScene = targetScene;
            StartHost();
        }
        
        public void StartEosClient(string hostProductUserId)
        {
            networkAddress = hostProductUserId;
            StartClient();
        }

        public void StopNetwork()
        {          
            _isVoluntaryDisconnect = true;
            if (NetworkServer.active && NetworkClient.isConnected)
            {
                StopHost();
            }
            else if (NetworkClient.isConnected)
            {
                StopClient();
            }
            else
            {
                if (NetworkServer.active)
                {
                    StopServer();
                }
                if (NetworkClient.active)
                {
                    StopClient();
                }
                else if (!string.IsNullOrEmpty(offlineScene) && SceneManager.GetActiveScene().name != offlineScene)
                {
                    SceneManager.LoadScene(offlineScene);
                }
            }
        }

        public bool StartMatch(string sceneName)
        {    
            if (NetworkServer.active)
            {
                ServerChangeScene(sceneName);
                return true;
            }
            return false;
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();

            bool wasVoluntary = _isVoluntaryDisconnect;
            _isVoluntaryDisconnect = false;

            if (wasVoluntary)
            {
                return;
            }

            var lobby = EOSSDKManager.Instance?.Lobby;
            if (lobby != null && lobby.IsInLobby && !lobby.HasGameStarted)
            {
                return;
            }

            if (lobby != null && lobby.IsInLobby)
            {
                lobby.LeaveLobby();
            }

            NotificationManager.Show("Disconnected from server.", NotificationType.Warning);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _isVoluntaryDisconnect = false;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _spawnedPets.Clear();
            _isVoluntaryDisconnect = false;
        }

#endregion
   
#region Server-Authoritative Spawning Overrides
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            Transform startPos = GetStartPosition();
            GameObject playerInstance = startPos != null
                ? Instantiate(playerPrefab, startPos.position, startPos.rotation)
                : Instantiate(playerPrefab);
            
            string playerName = GetPlayerDisplayName(conn);
            playerInstance.name = $"{playerPrefab.name} [{playerName}]";
            NetworkServer.AddPlayerForConnection(conn, playerInstance);

            Vector3 spawnOffset = playerInstance.transform.position + Vector3.right * 1.5f;
            GameObject petInstance = Instantiate(petPrefab, spawnOffset, Quaternion.identity);
            petInstance.name = $"{petPrefab.name} [{playerName}]";
            petInstance.TryGetComponent(out PetController petController);
            petController.SetOwner(playerInstance.transform);
            NetworkServer.Spawn(petInstance, conn);
            _spawnedPets[conn] = petInstance;
        }

        public string GetPlayerDisplayName(NetworkConnectionToClient conn)
        {
            // Host player: use local display name (from Epic, Steam, or custom input field)
            if (conn is LocalConnectionToClient || conn.connectionId == 0)
            {
                return !string.IsNullOrWhiteSpace(EOSSDKManager.LocalDisplayName)
                    ? EOSSDKManager.LocalDisplayName
                    : "Host";
            }

            // Remote client: resolve via EOS Lobby using conn.address (ProductUserId)
            var lobby = EOSSDKManager.Instance?.Lobby;
            ProductUserId userId = ProductUserId.FromString(conn.address);
            string memberName = lobby.GetMemberDisplayName(userId);
            return memberName;
        }

        
        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            base.OnServerDisconnect(conn);

            _spawnedPets.TryGetValue(conn, out GameObject petInstance);
            NetworkServer.Destroy(petInstance);
            _spawnedPets.Remove(conn);
        }
#endregion

    }
}