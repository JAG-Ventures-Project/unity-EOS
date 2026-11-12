using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using EpicTransport;

namespace EOS.P2P
{
    [RequireComponent(typeof(EosTransport))]
    [RequireComponent(typeof(EOSSDKComponent))]
    [DisallowMultipleComponent]
    public class EOSNetworkManager : NetworkManager
    {
        [Header("Pet Settings")]
        [SerializeField] private GameObject petPrefab;

        private readonly Dictionary<NetworkConnectionToClient, GameObject> _spawnedPets = new();
        private bool _isVoluntaryDisconnect;

        public static EOSNetworkManager Instance => singleton as EOSNetworkManager;

        public event Action OnUnexpectedClientDisconnect;
        public Func<NetworkConnectionToClient, string> CustomDisplayNameResolver { get; set; }

        private EosTransport _eosTransport;
        private EOSSDKComponent _eosSdkComponent;

#if UNITY_EDITOR

        private void OnValidate()
        {
            // We didn't add the pet because it's optional
            Assert.IsNotNull(playerPrefab, "[EOSNetworkManager] Player Prefab is null!");
        }

#endif

        public override void Awake()
        {
            base.Awake();
            TryGetComponent(out _eosTransport);
            TryGetComponent(out _eosSdkComponent);
        }

        #region Connection Handlers

        public void StartEosHost(string targetScene = null)
        {
            onlineScene = targetScene;
            StartHost();
        }

        public override void OnStartHost()
        {
            base.OnStartHost();
            if (!string.IsNullOrEmpty(EOSSDKComponent.LocalUserProductIdString))
            {
                GUIUtility.systemCopyBuffer = EOSSDKComponent.LocalUserProductIdString;
            }
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
            else
            {
                if (NetworkClient.active || NetworkClient.isConnected)
                {
                    StopClient();
                }
                if (NetworkServer.active)
                {
                    StopServer();
                }
            }

            if (!string.IsNullOrEmpty(offlineScene))
            {
                string currentScene = SceneManager.GetActiveScene().name;
                string targetSceneName = System.IO.Path.GetFileNameWithoutExtension(offlineScene);

                if (currentScene != offlineScene && currentScene != targetSceneName)
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

            OnUnexpectedClientDisconnect?.Invoke();
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

            if (petPrefab != null)
            {
                Vector3 spawnOffset = playerInstance.transform.position + Vector3.right * 1.5f;
                GameObject petInstance = Instantiate(petPrefab, spawnOffset, Quaternion.identity);
                petInstance.name = $"{petPrefab.name} [{playerName}]";
                NetworkServer.Spawn(petInstance, conn);
                _spawnedPets[conn] = petInstance;
            }
        }

        public string GetPlayerDisplayName(NetworkConnectionToClient conn)
        {
            if (CustomDisplayNameResolver != null)
            {
                string resolvedName = CustomDisplayNameResolver.Invoke(conn);
                if (!string.IsNullOrWhiteSpace(resolvedName))
                {
                    return resolvedName;
                }
            }

            if (conn is LocalConnectionToClient || conn.connectionId == 0)
            {
                return !string.IsNullOrWhiteSpace(EOSSDKComponent.DisplayName)
                    ? EOSSDKComponent.DisplayName
                    : "Host";
            }

            return !string.IsNullOrEmpty(conn.address)
                ? conn.address
                : $"Player {conn.connectionId}";
        }


        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            base.OnServerDisconnect(conn);

            if (_spawnedPets.Remove(conn, out GameObject petInstance) && petInstance != null)
            {
                NetworkServer.Destroy(petInstance);
            }
        }
        #endregion

    }
}