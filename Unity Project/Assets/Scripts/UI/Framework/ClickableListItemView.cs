using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AirlineCEO.UI.Framework
{
    /// <summary>可点击列表项模板。</summary>
    public class ClickableListItemView : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] TextMeshProUGUI titleText;
        [SerializeField] TextMeshProUGUI subtitleText;

        public string ItemId { get; private set; }
        public event Action<string> Clicked;

        void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(() => Clicked?.Invoke(ItemId));
        }

        public void Bind(string id, string title, string subtitle = null)
        {
            ItemId = id;
            if (titleText != null) titleText.text = title;
            if (subtitleText != null) subtitleText.text = subtitle ?? "";
        }
    }
}
