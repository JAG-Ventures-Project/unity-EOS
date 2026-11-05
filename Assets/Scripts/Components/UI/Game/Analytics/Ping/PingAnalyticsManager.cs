using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace EOS.UI.Analytics
{
    /// <summary>
    /// Manages network synchronization of player pings.
    /// Runs on both Host (measuring client RTT and broadcasting every 5s)
    /// and Clients (receiving pings and notifying the UI).
    /// Excludes the Host from the player list.
    /// </summary>
    [DisallowMultipleComponent]
    public class PingAnalyticsManager : MonoBehaviour
    {
        private static PingAnalyticsManager _instance;
        public static PingAnalyticsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<PingAnalyticsManager>();
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Settings")]
        [Tooltip("Interval in seconds between ping broadcasts from host to clients.")]
        [SerializeField] private float updateInterval = 5.0f;

        public float UpdateInterval => updateInterval;

        public event Action<IReadOnlyList<PlayerPingData>> OnPingsUpdated;

        private readonly Dictionary<int, string> _playerNames = new Dictionary<int, string>();
        private List<PlayerPingData> _cachedPings = new List<PlayerPingData>();
        private Coroutine _broadcastCoroutine;

        public IReadOnlyList<PlayerPingData> LatestPings => _cachedPings;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void OnEnable()
        {
            RegisterHandlers();
            NetworkClient.OnConnectedEvent += HandleClientConnected;
            NetworkClient.OnDisconnectedEvent += HandleClientDisconnected;
        }

        private void OnDisable()
        {
            UnregisterHandlers();
            NetworkClient.OnConnectedEvent -= HandleClientConnected;
            NetworkClient.OnDisconnectedEvent -= HandleClientDisconnected;

            StopBroadcastRoutine();
            _cachedPings.Clear();
        }

        private void Start()
        {
            // In GameScene, all players are already connected
            SendClientRegisterName();

            // Only the host runs the server broadcast routine
            if (NetworkServer.active)
            {
                StartBroadcastRoutine();
                BroadcastPingList();
            }
        }

        #region Handlers & Lifecycle Registration

        private void RegisterHandlers()
        {
            NetworkClient.ReplaceHandler<PlayersPingListMessage>(OnClientReceivePingList, false);
            NetworkServer.ReplaceHandler<RegisterPlayerNameMessage>(OnServerReceiveRegisterName, false);
        }

        private void UnregisterHandlers()
        {
            if (NetworkClient.active)
            {
                NetworkClient.UnregisterHandler<PlayersPingListMessage>();
            }

            if (NetworkServer.active)
            {
                NetworkServer.UnregisterHandler<RegisterPlayerNameMessage>();
            }
        }

        private void HandleClientDisconnected()
        {
            _cachedPings.Clear();
            OnPingsUpdated?.Invoke(_cachedPings);
        }

        #endregion

        #region Server Broadcast (Coroutine)

        private void StartBroadcastRoutine()
        {
            if (_broadcastCoroutine == null)
            {
                _broadcastCoroutine = StartCoroutine(PingBroadcastRoutine());
            }
        }

        private void StopBroadcastRoutine()
        {
            if (_broadcastCoroutine != null)
            {
                StopCoroutine(_broadcastCoroutine);
                _broadcastCoroutine = null;
            }
        }

        private IEnumerator PingBroadcastRoutine()
        {
            var wait = new WaitForSecondsRealtime(updateInterval);

            while (NetworkServer.active)
            {
                yield return wait;
                BroadcastPingList();
            }

            _broadcastCoroutine = null;
        }

        private void OnServerReceiveRegisterName(NetworkConnectionToClient conn, RegisterPlayerNameMessage msg)
        {
            if (conn == null) return;

            string sanitizedName = !string.IsNullOrWhiteSpace(msg.playerName)
                ? msg.playerName.Trim()
                : $"Player {conn.connectionId}";

            _playerNames[conn.connectionId] = sanitizedName;

            // Broadcast full list so host and all clients immediately have this player
            BroadcastPingList();
        }

        public List<PlayerPingData> CollectActivePings()
        {
            List<PlayerPingData> list = new List<PlayerPingData>();
            if (!NetworkServer.active) return list;

            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn == null || IsHostConnection(conn)) continue;

                int pingMs = Mathf.Max(0, Mathf.RoundToInt((float)(conn.rtt * 1000.0)));

                if (!_playerNames.TryGetValue(conn.connectionId, out string playerName))
                {
                    playerName = EOSNetworkManager.Instance != null
                        ? EOSNetworkManager.Instance.GetPlayerDisplayName(conn)
                        : $"Player {conn.connectionId}";
                    _playerNames[conn.connectionId] = playerName;
                }

                list.Add(new PlayerPingData(conn.connectionId, playerName, pingMs));
            }

            list.Sort((a, b) => a.connectionId.CompareTo(b.connectionId));
            return list;
        }

        public void BroadcastPingList()
        {
            if (!NetworkServer.active) return;

            List<PlayerPingData> list = CollectActivePings();

            PlayersPingListMessage msg = new PlayersPingListMessage
            {
                players = list.ToArray()
            };

            NetworkServer.SendToAll(msg);

            // Update host's local cache directly
            _cachedPings = list;
            OnPingsUpdated?.Invoke(_cachedPings);
        }

        public static bool IsHostConnection(NetworkConnectionToClient conn)
        {
            return conn is LocalConnectionToClient ||
                   conn == NetworkServer.localConnection ||
                   conn.connectionId == 0;
        }

        #endregion

        #region Client-Side Logic

        private void HandleClientConnected()
        {
            SendClientRegisterName();
        }

        private void SendClientRegisterName()
        {
            if (NetworkClient.isConnected)
            {
                NetworkClient.Send(new RegisterPlayerNameMessage
                {
                    playerName = EOSSDKManager.LocalDisplayName
                });
            }
        }

        private void OnClientReceivePingList(PlayersPingListMessage msg)
        {
            _cachedPings = msg.players != null
                ? new List<PlayerPingData>(msg.players)
                : new List<PlayerPingData>();

            OnPingsUpdated?.Invoke(_cachedPings);
        }

        #endregion
    }
}
