using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS.UI.Analytics
{
    /// <summary>
    /// UI Controller for displaying the player ping scoreboard.
    /// Activated by AnalyticsManager when the analytics menu is opened.
    /// Displays the ping of all connected players (excluding the host) updated every 5 seconds.
    /// </summary>
    [DisallowMultipleComponent]
    public class PingAnalyticsUI : MonoBehaviour
    {
        [Header("UI Containers")]
        [SerializeField] private Transform pingListParent;
        [SerializeField] private PingRowUI pingRowPrefab;

        private readonly List<PingRowUI> _rowPool = new List<PingRowUI>();
        private bool _isOpen;

        public bool IsOpen => _isOpen;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(pingListParent, "[PingAnalyticsUI] PingListParent reference is missing. Please assign the parent transform.");
            Assert.IsNotNull(pingRowPrefab, "[PingAnalyticsUI] PingRowPrefab reference is missing. Please assign the prefab.");
        }
#endif

        private void OnEnable()
        {
            if (PingAnalyticsManager.Instance != null)
            {
                PingAnalyticsManager.Instance.OnPingsUpdated -= HandlePingsUpdated;
                PingAnalyticsManager.Instance.OnPingsUpdated += HandlePingsUpdated;
                RefreshRows(PingAnalyticsManager.Instance.LatestPings);
            }
        }

        private void Start()
        {
            if (PingAnalyticsManager.Instance != null)
            {
                PingAnalyticsManager.Instance.OnPingsUpdated -= HandlePingsUpdated;
                PingAnalyticsManager.Instance.OnPingsUpdated += HandlePingsUpdated;
                RefreshRows(PingAnalyticsManager.Instance.LatestPings);
            }
        }

        private void OnDisable()
        {
            if (PingAnalyticsManager.Instance != null)
            {
                PingAnalyticsManager.Instance.OnPingsUpdated -= HandlePingsUpdated;
            }
        }

        public void SetMenuOpen(bool open)
        {
            if (_isOpen == open) return;
            _isOpen = open;

            if (open && PingAnalyticsManager.Instance != null)
            {
                RefreshRows(PingAnalyticsManager.Instance.LatestPings);
            }
        }

        private void HandlePingsUpdated(IReadOnlyList<PlayerPingData> pings)
        {
            if (isActiveAndEnabled || _isOpen)
            {
                RefreshRows(pings);
            }
        }

        private void RefreshRows(IReadOnlyList<PlayerPingData> pings)
        {
            if (pingRowPrefab == null || pingListParent == null) return;

            int count = pings != null ? pings.Count : 0;

            while (_rowPool.Count < count)
            {
                var row = Instantiate(pingRowPrefab, pingListParent);
                _rowPool.Add(row);
            }

            for (int i = 0; i < count; i++)
            {
                _rowPool[i].gameObject.SetActive(true);
                _rowPool[i].SetData(pings[i].playerName, pings[i].pingMs);
            }

            for (int i = count; i < _rowPool.Count; i++)
            {
                _rowPool[i].gameObject.SetActive(false);
            }
        }
    }
}
