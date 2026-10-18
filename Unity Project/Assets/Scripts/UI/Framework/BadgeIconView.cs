using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AirlineCEO.UI.Framework
{
    /// <summary>角标 / 状态徽章。</summary>
    public class BadgeIconView : MonoBehaviour
    {
        [SerializeField] Image background;
        [SerializeField] TextMeshProUGUI labelText;

        public void AssignRefs(Image backgroundRef, TextMeshProUGUI labelRef)
        {
            background = backgroundRef;
            labelText = labelRef;
        }

        public void Set(string text, Color color)
        {
            if (labelText != null) labelText.text = text;
            if (background != null) background.color = color;
        }
    }
}
