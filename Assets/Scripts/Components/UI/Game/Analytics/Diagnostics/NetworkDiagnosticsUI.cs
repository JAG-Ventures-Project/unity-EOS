using TMPro;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS.UI.Analytics
{
    /// <summary>
    /// Displays comprehensive network diagnostics and system performance metrics on the Analytics HUD.
    /// Metrics include:
    /// - Input Responsiveness (Action delay)
    /// - Simulation Delay (Server frame queuing)
    /// - Jitter (RTT variance)
    /// - Packet Loss percentage
    /// - EOS Route & NAT Quality
    /// - Timeline Offset and Server Send Rate
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkDiagnosticsUI : MonoBehaviour
    {
        [Header("Responsiveness & Delays")]
        [SerializeField] private TMP_Text inputResponsivenessText;
        [SerializeField] private TMP_Text simulationDelayText;
        [SerializeField] private TMP_Text timelineOffsetText;

        [Header("Network Stability & Loss")]
        [SerializeField] private TMP_Text rttText;
        [SerializeField] private TMP_Text jitterText;
        [SerializeField] private TMP_Text packetLossText;

        [Header("EOS Route & Transport")]
        [SerializeField] private TMP_Text natTypeText;
        [SerializeField] private TMP_Text p2pQueueText;

        [Header("Server Simulation")]
        [SerializeField] private TMP_Text serverSendRateText;

        [Header("Status Colors")]
        [SerializeField] private Color goodColor = new Color(0.2f, 0.85f, 0.3f, 1f);
        [SerializeField] private Color mediumColor = new Color(1f, 0.75f, 0.1f, 1f);
        [SerializeField] private Color badColor = new Color(0.95f, 0.25f, 0.2f, 1f);

        private bool _isOpen;

        public bool IsOpen => _isOpen;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(inputResponsivenessText, "[NetworkDiagnosticsUI] Input Responsiveness Text is null");
            Assert.IsNotNull(simulationDelayText, "[NetworkDiagnosticsUI] Simulation Delay Text is null");
            Assert.IsNotNull(timelineOffsetText, "[NetworkDiagnosticsUI] Timeline Offset Text is null");
            Assert.IsNotNull(rttText, "[NetworkDiagnosticsUI] RTT Text is null");
            Assert.IsNotNull(jitterText, "[NetworkDiagnosticsUI] Jitter Text is null");
            Assert.IsNotNull(packetLossText, "[NetworkDiagnosticsUI] Packet Loss Text is null");
            Assert.IsNotNull(natTypeText, "[NetworkDiagnosticsUI] NAT Type Text is null");
            Assert.IsNotNull(p2pQueueText, "[NetworkDiagnosticsUI] P2P Queue Text is null");
            Assert.IsNotNull(serverSendRateText, "[NetworkDiagnosticsUI] Server Send Rate Text is null");
        }
#endif

        private void OnEnable()
        {
            NetworkDiagnosticsManager.Instance.OnStatsUpdated += HandleStatsUpdated;
            NetworkDiagnosticsManager.Instance.SetProbingActive(true);
            UpdateDisplay(NetworkDiagnosticsManager.Instance.CurrentStats);
        }

        private void OnDisable()
        {
            NetworkDiagnosticsManager.Instance.OnStatsUpdated -= HandleStatsUpdated;
            NetworkDiagnosticsManager.Instance.SetProbingActive(false);
        }

        public void SetMenuOpen(bool open)
        {
            if (_isOpen == open) return;
            _isOpen = open;

            gameObject.SetActive(open);

            NetworkDiagnosticsManager.Instance.SetProbingActive(open);

            if (open)
            {
                UpdateDisplay(NetworkDiagnosticsManager.Instance.CurrentStats);
            }
        }

        private void HandleStatsUpdated(NetworkPerformanceStats stats)
        {
            if (isActiveAndEnabled || _isOpen)
            {
                UpdateDisplay(stats);
            }
        }

        private void UpdateDisplay(NetworkPerformanceStats stats)
        {
            // --- Responsiveness ---
            inputResponsivenessText.text = $"Input Delay: {stats.inputResponsivenessMs:0.0} ms";
            inputResponsivenessText.color = stats.inputResponsivenessMs < 50f
                ? goodColor
                : (stats.inputResponsivenessMs < 100f ? mediumColor : badColor);

            simulationDelayText.text = $"Simulation Delay: {stats.simulationDelayMs:0.0} ms";
            simulationDelayText.color = stats.simulationDelayMs < 20f ? goodColor : mediumColor;

            timelineOffsetText.text = $"Clock Offset: {stats.timelineOffsetMs:0.0} ms";

            // --- Network Stability ---
            rttText.text = $"RTT (Ping): {stats.rttMs:0} ms";
            rttText.color = stats.rttMs < 60f ? goodColor : (stats.rttMs < 120f ? mediumColor : badColor);

            jitterText.text = $"Jitter: ±{stats.jitterMs:0.0} ms";
            jitterText.color = stats.jitterMs < 5f ? goodColor : (stats.jitterMs < 15f ? mediumColor : badColor);

            packetLossText.text = $"Packet Loss: {stats.packetLossPercent:0.0} %";
            packetLossText.color = stats.packetLossPercent == 0f
                ? goodColor
                : (stats.packetLossPercent < 2f ? mediumColor : badColor);

            // --- EOS Route ---
            natTypeText.text = $"NAT Route: {stats.natType}";
            natTypeText.color = stats.natType switch
            {
                "Open" => goodColor,
                "Moderate" => mediumColor,
                "Strict" => badColor,
                _ => Color.white
            };

            p2pQueueText.text = $"P2P Queues: {stats.incomingPacketsQueued} in / {stats.outgoingPacketsQueued} out";

            // --- Server Tick ---
            serverSendRateText.text = $"Server Tick: {stats.serverSendRate} Hz";
        }
    }
}
