using System.Collections.Generic;
using UnityEngine;

namespace EOS.Test.Analytics
{
    /// <summary>
    /// Displays comprehensive network diagnostics and system performance metrics on the Analytics HUD.
    /// Dynamically instantiates and updates rows using AnalyticRowUI.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkDiagnosticsUI : MonoBehaviour
    {
        [Header("UI Containers & Prefab")]
        [SerializeField] private Transform rowsParent;
        [SerializeField] private AnalyticRowUI rowPrefab;

        [Header("Status Colors")]
        [SerializeField] private Color goodColor = new Color(0.2f, 0.85f, 0.3f, 1f);
        [SerializeField] private Color mediumColor = new Color(1f, 0.75f, 0.1f, 1f);
        [SerializeField] private Color badColor = new Color(0.95f, 0.25f, 0.2f, 1f);

        private static readonly (string StatName, string Title)[] StatDefinitions = new[]
        {
            ("Input Responsiveness", "Input Responsiveness : "),
            ("Simulation Delay", "Simulation Delay : "),
            ("Timeline Offset", "Timeline Offset : "),
            ("Rtt", "Rtt : "),
            ("Jitter", "Jitter : "),
            ("Packet Loss", "Packet Loss : "),
            ("Nat Type", "Nat Type : "),
            ("P2P Queue", "P2P Queue : "),
            ("Server Send Rate", "Server Send Rate : ")
        };

        private readonly List<AnalyticRowUI> _spawnedRows = new List<AnalyticRowUI>();
        private bool _isOpen;

        public bool IsOpen => _isOpen;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (UnityEditor.EditorUtility.IsPersistent(this))
            {
                return;
            }

            if (rowsParent == null) Debug.LogWarning("[NetworkDiagnosticsUI] Rows Parent is null! Please assign in inspector.", this);
            if (rowPrefab == null) Debug.LogWarning("[NetworkDiagnosticsUI] Row Prefab is null! Please assign in inspector.", this);
        }
#endif

        private void Awake()
        {
            InitializeRows();
        }

        private void OnEnable()
        {
            if (NetworkDiagnosticsManager.Instance != null)
            {
                NetworkDiagnosticsManager.Instance.OnStatsUpdated -= HandleStatsUpdated;
                NetworkDiagnosticsManager.Instance.OnStatsUpdated += HandleStatsUpdated;
                NetworkDiagnosticsManager.Instance.SetProbingActive(true);
                UpdateDisplay(NetworkDiagnosticsManager.Instance.CurrentStats);
            }
        }

        private void Start()
        {
            if (NetworkDiagnosticsManager.Instance != null)
            {
                NetworkDiagnosticsManager.Instance.OnStatsUpdated -= HandleStatsUpdated;
                NetworkDiagnosticsManager.Instance.OnStatsUpdated += HandleStatsUpdated;
                UpdateDisplay(NetworkDiagnosticsManager.Instance.CurrentStats);
            }
        }

        private void OnDisable()
        {
            if (NetworkDiagnosticsManager.Instance != null)
            {
                NetworkDiagnosticsManager.Instance.OnStatsUpdated -= HandleStatsUpdated;
                NetworkDiagnosticsManager.Instance.SetProbingActive(false);
            }
        }

        public void SetMenuOpen(bool open)
        {
            if (_isOpen == open) return;
            _isOpen = open;

            if (NetworkDiagnosticsManager.Instance != null)
            {
                NetworkDiagnosticsManager.Instance.SetProbingActive(open);

                if (open)
                {
                    UpdateDisplay(NetworkDiagnosticsManager.Instance.CurrentStats);
                }
            }
        }

        private void HandleStatsUpdated(NetworkPerformanceStats stats)
        {
            if (isActiveAndEnabled || _isOpen)
            {
                UpdateDisplay(stats);
            }
        }

        private void InitializeRows()
        {
            for (int i = 0; i < StatDefinitions.Length; i++)
            {
                AnalyticRowUI row = Instantiate(rowPrefab, rowsParent);
                row.gameObject.name = StatDefinitions[i].StatName;
                row.SetTitle(StatDefinitions[i].Title);
                _spawnedRows.Add(row);
            }
        }

        private void UpdateDisplay(NetworkPerformanceStats stats)
        {
            // 0: Input Responsiveness
            Color respColor = stats.inputResponsivenessMs < 50f
                ? goodColor
                : (stats.inputResponsivenessMs < 100f ? mediumColor : badColor);
            _spawnedRows[0].SetValue($"{stats.inputResponsivenessMs:0.0} ms", respColor);

            // 1: Simulation Delay
            Color simColor = stats.simulationDelayMs < 20f ? goodColor : mediumColor;
            _spawnedRows[1].SetValue($"{stats.simulationDelayMs:0.0} ms", simColor);

            // 2: Timeline Offset
            _spawnedRows[2].SetValue($"{stats.timelineOffsetMs:0.0} ms");

            // 3: Rtt
            Color rttColor = stats.rttMs < 60f ? goodColor : (stats.rttMs < 120f ? mediumColor : badColor);
            _spawnedRows[3].SetValue($"{stats.rttMs:0} ms", rttColor);

            // 4: Jitter
            Color jitterColor = stats.jitterMs < 5f ? goodColor : (stats.jitterMs < 15f ? mediumColor : badColor);
            _spawnedRows[4].SetValue($"±{stats.jitterMs:0.0} ms", jitterColor);

            // 5: Packet Loss
            Color lossColor = stats.packetLossPercent == 0f
                ? goodColor
                : (stats.packetLossPercent < 2f ? mediumColor : badColor);
            _spawnedRows[5].SetValue($"{stats.packetLossPercent:0.0} %", lossColor);

            // 6: Nat Type
            Color natColor = stats.natType switch
            {
                "Open" => goodColor,
                "Moderate" => mediumColor,
                "Strict" => badColor,
                _ => Color.black
            };
            _spawnedRows[6].SetValue(stats.natType, natColor);

            // 7: P2P Queue
            _spawnedRows[7].SetValue($"{stats.incomingPacketsQueued} in / {stats.outgoingPacketsQueued} out");

            // 8: Server Send Rate
            _spawnedRows[8].SetValue($"{stats.serverSendRate} Hz");
        }
    }
}