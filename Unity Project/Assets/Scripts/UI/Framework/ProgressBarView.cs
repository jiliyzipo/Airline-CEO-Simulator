using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AirlineCEO.UI.Framework
{
    /// <summary>带标签的进度条（0~1）。</summary>
    public class ProgressBarView : MonoBehaviour
    {
        [SerializeField] Image fill;
        [SerializeField] TextMeshProUGUI labelText;
        [SerializeField] TextMeshProUGUI percentText;

        public void Set(float normalized01, string label = null)
        {
            float v = Mathf.Clamp01(normalized01);
            if (fill != null) fill.fillAmount = v;
            if (labelText != null && label != null) labelText.text = label;
            if (percentText != null) percentText.text = $"{v * 100f:F0}%";
        }
    }
}
