using System.Collections.Generic;
#if MIRROR
using Mirror;
#endif
using UnityEngine;
using EOS.P2P;

namespace EOS.Test.Pet
{
    /// <summary>
    /// Test/game subclass of EOSNetworkManager that spawns and cleans up a per-player companion Pet prefab.
    /// Keeps EOSNetworkManager strictly focused on Mirror + EOS transport lifecycle.
    /// </summary>
#if MIRROR
    public class TestPetNetworkManager : EOSNetworkManager
    {
        [Header("Pet Settings")]
        [SerializeField] private GameObject petPrefab;

        private readonly Dictionary<NetworkConnectionToClient, GameObject> _spawnedPets = new();

        public override void OnStartServer()
        {
            base.OnStartServer();
            _spawnedPets.Clear();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _spawnedPets.Clear();
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            base.OnServerAddPlayer(conn);

            if (petPrefab != null && conn.identity != null)
            {
                string playerName = GetPlayerDisplayName(conn);
                Vector3 spawnOffset = conn.identity.transform.position + Vector3.right * 1.5f;
                GameObject petInstance = Instantiate(petPrefab, spawnOffset, Quaternion.identity);
                petInstance.name = $"{petPrefab.name} [{playerName}]";
                NetworkServer.Spawn(petInstance, conn);
                _spawnedPets[conn] = petInstance;
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            base.OnServerDisconnect(conn);

            if (_spawnedPets.Remove(conn, out GameObject petInstance) && petInstance != null)
            {
                NetworkServer.Destroy(petInstance);
            }
        }
    }
#else
    public class TestPetNetworkManager : EOSNetworkManager
    {
        [Header("Pet Settings")]
        [SerializeField] private GameObject petPrefab;
    }
#endif
}

