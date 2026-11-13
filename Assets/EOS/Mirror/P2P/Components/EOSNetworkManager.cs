using System;
using System.Collections.Generic;
#if MIRROR
using Mirror;
#endif
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using EpicTransport;
using EOS.Core;

namespace EOS.P2P
{
#if MIRROR
    [RequireComponent(typeof(EosTransport))]
    [DisallowMultipleComponent]
    public class EOSNetworkManager : NetworkManager, INetworkSessionService
    {
        [Header("Pet Settings")]
        [SerializeField] private GameObject petPrefab;

        private readonly Dictionary<NetworkConnectionToClient, GameObject> _spawnedPets = new();
        private bool _isVoluntaryDisconnect;

        public static EOSNetworkManager Instance => singleton as EOSNetworkManager;

        public event Action OnUnexpectedClientDisconnect;
        public Func<NetworkConnectionToClient, string> CustomDisplayNameResolver { get; set; }
        public Func<string, string> CustomAddressDisplayNameResolver { get; set; }

        bool INetworkSessionService.IsServerActive => NetworkServer.active;
        bool INetworkSessionService.IsClientConnected => NetworkClient.isConnected;
        bool INetworkSessionService.IsClientActive => NetworkClient.active;
        void INetworkSessionService.StartHost(string targetScene) => StartEosHost(targetScene);
        void INetworkSessionService.StartClient(string hostAddress) => StartEosClient(hostAddress);
        void INetworkSessionService.StopNetwork() => StopNetwork();

        private EosTransport _eosTransport;

#if UNITY_EDITOR

        private void OnValidate()
        {
            // We didn't add the pet because it's optional
            Assert.IsNotNull(playerPrefab, "[EOSNetworkManager] Player Prefab is null!");
            Assert.IsNotNull(offlineScene, "[EOSNetworkManager] Offline Scene is null!");
            Assert.IsNotNull(onlineScene, "[EOSNetworkManager] Online Scene is null!");
        }

#endif

        public override void Awake()
        {
            base.Awake();
            TryGetComponent(out _eosTransport);
            NetworkService.Current = this;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (NetworkService.Current == (INetworkSessionService)this)
            {
                NetworkService.Current = new NullNetworkSessionService();
            }
        }

        #region Connection Handlers

        public void StartEosHost(string targetScene = null)
        {
            onlineScene = targetScene;
            StartHost();
        }

        public void StartEosClient(string hostProductUserId)
        {
            if (string.IsNullOrEmpty(hostProductUserId))
            {
                Debug.LogWarning("[EOSNetworkManager] StartEosClient called with null or empty hostProductUserId.");
                return;
            }

            if (NetworkServer.active || NetworkClient.active)
            {
                StartCoroutine(SwitchHostRoutine(hostProductUserId));
                return;
            }

            networkAddress = hostProductUserId;
            StartClient();
        }

        public void ConnectToHost(string hostProductUserId) => StartEosClient(hostProductUserId);

        private System.Collections.IEnumerator SwitchHostRoutine(string hostProductUserId)
        {
            _isVoluntaryDisconnect = true;

            string savedOfflineScene = offlineScene;
            offlineScene = string.Empty;

            try
            {
                if (NetworkServer.active && NetworkClient.isConnected)
                {
                    StopHost();
                }
                else if (NetworkClient.active)
                {
                    StopClient();
                }
                else if (NetworkServer.active)
                {
                    StopServer();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EOSNetworkManager] Exception while stopping previous network session: {ex.Message}");
            }

            // Wait for Mirror and transport to cleanly finish shutting down
            float timeout = Time.unscaledTime + 2.0f;
            while ((NetworkClient.active || NetworkServer.active) && Time.unscaledTime < timeout)
            {
                yield return null;
            }

            if (NetworkClient.active)
            {
                NetworkClient.Shutdown();
            }
            if (NetworkServer.active)
            {
                NetworkServer.Shutdown();
            }

            yield return null;

            offlineScene = savedOfflineScene;
            _isVoluntaryDisconnect = false;

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

        public override void OnClientConnect()
        {
            // Mirror architecture: OnClientConnect handles Ready & AddPlayer only when no scene is loaded.
            // When an online scene is used (like GameScene), Mirror's OnClientSceneChanged will
            // trigger Ready and AddPlayer once the scene load finishes.
            if (!clientLoadedScene)
            {
                if (!NetworkClient.ready)
                {
                    NetworkClient.Ready();
                }

                if (autoCreatePlayer && NetworkClient.localPlayer == null)
                {
                    NetworkClient.AddPlayer();
                }
            }
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

        public override void OnStartServer()
        {
            base.OnStartServer();
            _spawnedPets.Clear();

            if (string.IsNullOrEmpty(networkSceneName) || networkSceneName == offlineScene)
            {
                string activePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
                networkSceneName = !string.IsNullOrEmpty(activePath) ? activePath : UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }
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

            if (CustomAddressDisplayNameResolver != null && !string.IsNullOrEmpty(conn.address))
            {
                string resolvedName = CustomAddressDisplayNameResolver.Invoke(conn.address);
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
#else
    [DisallowMultipleComponent]
    public class EOSNetworkManager : MonoBehaviour
    {
        public static EOSNetworkManager Instance => null;

        public event Action OnUnexpectedClientDisconnect;

        public void StartEosHost(string targetScene = null)
        {
            Debug.LogWarning("[EOSNetworkManager] Mirror is not installed. StartEosHost skipped.");
        }

        public void StartEosClient(string hostProductUserId)
        {
            Debug.LogWarning("[EOSNetworkManager] Mirror is not installed. StartEosClient skipped.");
        }

        public void ConnectToHost(string hostProductUserId) => StartEosClient(hostProductUserId);

        public void StopNetwork()
        {
        }

        public bool StartMatch(string sceneName)
        {
            Debug.LogWarning("[EOSNetworkManager] Mirror is not installed. StartMatch skipped.");
            return false;
        }
    }
#endif
}