using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EOS.Test.Analytics
{
    /// <summary>
    /// AI Implementation
    /// Represents an individual player's ping entry in the Tab menu scoreboard list.
    /// </summary>
    [DisallowMultipleComponent]
    public class PingRowUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private TMP_Text pingText;
        [SerializeField] private Image pingIndicatorImage;

        [Header("Ping Color Thresholds")]
        [SerializeField] private Color goodPingColor = new Color(0.2f, 0.85f, 0.3f, 1f);   // < 60ms (Green)
        [SerializeField] private Color mediumPingColor = new Color(1f, 0.75f, 0.1f, 1f);  // 60-120ms (Yellow)
        [SerializeField] private Color badPingColor = new Color(0.95f, 0.25f, 0.2f, 1f);   // > 120ms (Red)

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (UnityEditor.EditorUtility.IsPersistent(this))
            {
                return;
            }

            if (playerNameText == null) Debug.LogWarning("[PingRowUI] playerNameText is null", this);
            if (pingText == null) Debug.LogWarning("[PingRowUI] pingText is null", this);
            if (pingIndicatorImage == null) Debug.LogWarning("[PingRowUI] pingIndicatorImage is null", this);
        }
#endif

        public void SetData(string playerName, int pingMs)
        {
            playerNameText.text = !string.IsNullOrWhiteSpace(playerName) ? playerName : "Unknown Player";

            pingText.text = $"{pingMs} ms";
            pingText.color = GetPingColor(pingMs);
            pingIndicatorImage.color = GetPingColor(pingMs);
        }

        private Color GetPingColor(int pingMs)
        {
            if (pingMs < 60) return goodPingColor;
            if (pingMs <= 120) return mediumPingColor;
            return badPingColor;
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }
    }
}
