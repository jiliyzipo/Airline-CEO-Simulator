using UnityEngine;
using TMPro;

namespace AirlineCEO.UI.Framework
{
    /// <summary>键值统计行：标签 + 数值。</summary>
    public class StatRowView : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI labelText;
        [SerializeField] TextMeshProUGUI valueText;

        public void Set(string label, string value)
        {
            if (labelText != null) labelText.text = label;
            if (valueText != null) valueText.text = value;
        }
    }
}
