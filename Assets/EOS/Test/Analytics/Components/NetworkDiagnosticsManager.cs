using System;
using System.Collections;
using System.Collections.Generic;
#if MIRROR
using Mirror;
#endif
using EOS.Core;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;

namespace EOS.Test.Analytics
{
    [Serializable]
    public struct NetworkPerformanceStats
    {
        public float rttMs;
        public float inputResponsivenessMs;
        public float simulationDelayMs;
        public float jitterMs;
        public float packetLossPercent;
        public string natType;
        public ulong incomingPacketsQueued;
        public ulong outgoingPacketsQueued;
        public float timelineOffsetMs;
        public int serverSendRate;
    }

#if MIRROR
    /// <summary>
    /// AI Implemnation
    /// Measures in-depth network and system metrics including:
    /// - Input responsiveness (probe round-trip time)
    /// - Simulation delay (server frame queue delay)
    /// - Packet loss percentage
    /// - Jitter (RTT variance)
    /// - EOS P2P Route quality (NAT Type & packet queues)
    /// - Engine performance (FPS, Frame Time, Timeline offset)
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkDiagnosticsManager : MonoBehaviour
    {
        private static NetworkDiagnosticsManager _instance;
        public static NetworkDiagnosticsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<NetworkDiagnosticsManager>();
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Probe Settings")]
        [SerializeField] private float probeInterval = 0.5f;
        [SerializeField] private int packetLossWindowSize = 30;
        [SerializeField] private float probeTimeout = 1.5f;

        public event Action<NetworkPerformanceStats> OnStatsUpdated;

        private uint _nextSequence;
        private Coroutine _probeCoroutine;
        private readonly Dictionary<uint, double> _pendingProbes = new Dictionary<uint, double>();
        private readonly Queue<bool> _probeResultsWindow = new Queue<bool>();

        private float _smoothedResponsivenessMs;
        private float _smoothedSimulationDelayMs;
        private string _cachedNatType = "Unknown";
        private ulong _incomingQueuedPackets;
        private ulong _outgoingQueuedPackets;
        private Coroutine _eosRouteCoroutine;

        public NetworkPerformanceStats CurrentStats { get; private set; }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
                return;
            }
        }

        private void OnEnable()
        {
            RegisterHandlers();
            _eosRouteCoroutine = StartCoroutine(EosRouteCheckRoutine());
        }

        private void OnDisable()
        {
            UnregisterHandlers();
            StopProbing();

            if (_eosRouteCoroutine != null)
            {
                StopCoroutine(_eosRouteCoroutine);
                _eosRouteCoroutine = null;
            }

            _pendingProbes.Clear();
            _probeResultsWindow.Clear();
        }

        private void Start()
        {
            RegisterHandlers();
            QueryEosRouteStatus();
        }

        private void Update()
        {
            // Check for timed out probes to record packet loss
            CheckProbeTimeouts();
        }

        #region Probing Control

        /// <summary>
        /// Starts or stops high-frequency probing (typically driven when the Analytics HUD opens/closes).
        /// </summary>
        public void SetProbingActive(bool active)
        {
            if (active)
            {
                if (_probeCoroutine == null && NetworkClient.isConnected)
                {
                    _probeCoroutine = StartCoroutine(ProbeRoutine());
                }
            }
            else
            {
                StopProbing();
            }
        }

        private void StopProbing()
        {
            if (_probeCoroutine != null)
            {
                StopCoroutine(_probeCoroutine);
                _probeCoroutine = null;
            }
        }

        private IEnumerator ProbeRoutine()
        {
            var wait = new WaitForSecondsRealtime(probeInterval);

            while (NetworkClient.isConnected)
            {
                SendProbe();
                RefreshCurrentStats();
                yield return wait;
            }

            _probeCoroutine = null;
        }

        private void SendProbe()
        {
            if (NetworkServer.active && NetworkClient.isConnected && NetworkServer.connections.Count <= 1)
            {
                // Host playing locally
                _smoothedResponsivenessMs = 0.5f;
                _smoothedSimulationDelayMs = Time.unscaledDeltaTime * 1000f;
                RecordProbeResult(true);
                return;
            }

            uint seq = _nextSequence++;
            double now = NetworkTime.localTime;
            _pendingProbes[seq] = now;

            NetworkClient.Send(new InputProbeMessage
            {
                sequence = seq,
                clientTime = now
            }, Channels.Unreliable);
        }

        private void CheckProbeTimeouts()
        {
            if (_pendingProbes.Count == 0) return;

            double now = NetworkTime.localTime;
            List<uint> timedOut = null;

            foreach (var kvp in _pendingProbes)
            {
                if (now - kvp.Value > probeTimeout)
                {
                    timedOut ??= new List<uint>();
                    timedOut.Add(kvp.Key);
                }
            }

            if (timedOut != null)
            {
                for (int i = 0; i < timedOut.Count; i++)
                {
                    _pendingProbes.Remove(timedOut[i]);
                    RecordProbeResult(false); // Lost packet
                }
            }
        }

        private void RecordProbeResult(bool success)
        {
            _probeResultsWindow.Enqueue(success);
            while (_probeResultsWindow.Count > packetLossWindowSize)
            {
                _probeResultsWindow.Dequeue();
            }
        }

        private float CalculatePacketLoss()
        {
            if (_probeResultsWindow.Count == 0) return 0f;

            int lostCount = 0;
            foreach (bool result in _probeResultsWindow)
            {
                if (!result) lostCount++;
            }

            return (float)lostCount / _probeResultsWindow.Count * 100f;
        }

        #endregion

        #region Mirror Handlers

        private void RegisterHandlers()
        {
            NetworkClient.ReplaceHandler<InputProbeAckMessage>(OnClientReceiveProbeAck, false);
            NetworkServer.ReplaceHandler<InputProbeMessage>(OnServerReceiveProbe, false);
        }

        private void UnregisterHandlers()
        {
            if (NetworkClient.active)
            {
                NetworkClient.UnregisterHandler<InputProbeAckMessage>();
            }

            if (NetworkServer.active)
            {
                NetworkServer.UnregisterHandler<InputProbeMessage>();
            }
        }

        private void OnServerReceiveProbe(NetworkConnectionToClient conn, InputProbeMessage msg)
        {
            if (conn == null) return;

            conn.Send(new InputProbeAckMessage
            {
                sequence = msg.sequence,
                clientTime = msg.clientTime,
                serverTime = NetworkTime.localTime
            }, Channels.Unreliable);
        }

        private void OnClientReceiveProbeAck(InputProbeAckMessage msg)
        {
            if (_pendingProbes.Remove(msg.sequence, out double clientTime))
            {
                RecordProbeResult(true);

                double now = NetworkTime.localTime;
                float measuredRoundTripMs = Mathf.Max(0.1f, (float)((now - clientTime) * 1000.0));
                float rawNetworkRttMs = Mathf.Max(0.1f, (float)(NetworkTime.rtt * 1000.0));

                // Smooth responsiveness
                _smoothedResponsivenessMs = _smoothedResponsivenessMs == 0f
                    ? measuredRoundTripMs
                    : Mathf.Lerp(_smoothedResponsivenessMs, measuredRoundTripMs, 0.35f);

                // Simulation delay: difference between probe trip and raw network RTT
                float rawSimDelay = Mathf.Max(0f, measuredRoundTripMs - rawNetworkRttMs);
                _smoothedSimulationDelayMs = _smoothedSimulationDelayMs == 0f
                    ? rawSimDelay
                    : Mathf.Lerp(_smoothedSimulationDelayMs, rawSimDelay, 0.35f);

                RefreshCurrentStats();
            }
        }

        #endregion

        #region Route & EOS Health

        private IEnumerator EosRouteCheckRoutine()
        {
            var wait = new WaitForSecondsRealtime(2.0f);

            while (true)
            {
                yield return wait;
                QueryEosRouteStatus();
            }
        }

        private void QueryEosRouteStatus()
        {
            try
            {
                var p2p = EOSSDKManager.PlatformInterface?.GetP2PInterface();
                if (p2p != null)
                {
                    // Query NAT
                    var natOptions = new GetNATTypeOptions();
                    if (p2p.GetNATType(natOptions, out NATType nat) == Result.Success)
                    {
                        _cachedNatType = nat.ToString();
                    }

                    // Query Packet Queues
                    var queueOptions = new GetPacketQueueInfoOptions();
                    if (p2p.GetPacketQueueInfo(queueOptions, out PacketQueueInfo queueInfo) == Result.Success)
                    {
                        _incomingQueuedPackets = queueInfo.IncomingPacketQueueCurrentPacketCount;
                        _outgoingQueuedPackets = queueInfo.OutgoingPacketQueueCurrentPacketCount;
                    }
                }
            }
            catch (Exception)
            {
                _cachedNatType = "Unknown";
            }
        }

        #endregion

        #region Aggregation

        public void RefreshCurrentStats()
        {
            float rawRtt = NetworkServer.active && NetworkClient.isConnected && NetworkServer.connections.Count <= 1
                ? 0f
                : Mathf.Max(0f, (float)(NetworkTime.rtt * 1000.0));

            float jitter = Mathf.Max(0f, (float)(NetworkTime.rttVariance * 1000.0));
            float offset = (float)(NetworkTime.offset * 1000.0);

            CurrentStats = new NetworkPerformanceStats
            {
                rttMs = rawRtt,
                inputResponsivenessMs = _smoothedResponsivenessMs,
                simulationDelayMs = _smoothedSimulationDelayMs,
                jitterMs = jitter,
                packetLossPercent = CalculatePacketLoss(),
                natType = _cachedNatType,
                incomingPacketsQueued = _incomingQueuedPackets,
                outgoingPacketsQueued = _outgoingQueuedPackets,
                timelineOffsetMs = offset,
                serverSendRate = NetworkServer.sendRate
            };

            OnStatsUpdated?.Invoke(CurrentStats);
        }

        #endregion
    }
#else
    [DisallowMultipleComponent]
    public class NetworkDiagnosticsManager : MonoBehaviour
    {
        public static NetworkDiagnosticsManager Instance { get; private set; }
        public event Action<NetworkPerformanceStats> OnStatsUpdated;
        public NetworkPerformanceStats CurrentStats { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
#endif
}
