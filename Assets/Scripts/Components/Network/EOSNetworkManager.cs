using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Assertions;
using EpicTransport;
using Minimap.FPC;

namespace EOS
{
    [RequireComponent(typeof(EosTransport))]   
    [DisallowMultipleComponent]
    public class EOSNetworkManager : NetworkManager
    {
        [Header("Pet Settings")]
        [SerializeField] private GameObject petPrefab;

        private readonly Dictionary<NetworkConnectionToClient, GameObject> _spawnedPets = new();

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
            spawnPrefabs.Add(petPrefab);
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
            if (NetworkServer.active && NetworkClient.isConnected)
            {
                StopHost();
            }
            else if (NetworkClient.isConnected)
            {
                StopClient();
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

#endregion
   
#region Server-Authoritative Spawning Overrides
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            Transform startPos = GetStartPosition();
            GameObject playerInstance = startPos != null
                ? Instantiate(playerPrefab, startPos.position, startPos.rotation)
                : Instantiate(playerPrefab);
            playerInstance.name = $"{playerPrefab.name} [ConnId={conn.connectionId}]";
            NetworkServer.AddPlayerForConnection(conn, playerInstance);

            // TODO: not all player may have pets (in future) 
            Vector3 spawnOffset = playerInstance.transform.position + Vector3.right * 1.5f;
            GameObject petInstance = Instantiate(petPrefab, spawnOffset, Quaternion.identity);
            petInstance.name = $"{petPrefab.name} [Owner={conn.connectionId}]";
            petInstance.TryGetComponent(out PetController petController);
            petController.SetOwner(playerInstance.transform);
            NetworkServer.Spawn(petInstance, conn);
            _spawnedPets[conn] = petInstance;
        }

        
        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            base.OnServerDisconnect(conn);

            //  do it if he has a pet (in future)
            _spawnedPets.TryGetValue(conn, out GameObject petInstance);
            NetworkServer.Destroy(petInstance);
            _spawnedPets.Remove(conn);
        }
#endregion

    }
}