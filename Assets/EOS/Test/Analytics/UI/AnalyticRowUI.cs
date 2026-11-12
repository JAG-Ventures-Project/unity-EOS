using TMPro;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS.Test.Analytics
{
    /// <summary>
    /// Represents an individual diagnostic stat row (Title : Value) in the Network Analytics HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public class AnalyticRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text valueText;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(titleText, "[AnalyticRowUI] Title Text is null");
            Assert.IsNotNull(valueText, "[AnalyticRowUI] Value Text is null");
        }
#endif

        public void SetTitle(string title)
        {
            titleText.text = title;
        }

        public void SetValue(string value, Color? color = null)
        {
            valueText.text = value;
            if (color.HasValue)
            {
                valueText.color = color.Value;
            }
        }

        public void SetData(string title, string value, Color? color = null)
        {
            SetTitle(title);
            SetValue(value, color);
        }
    }
}
