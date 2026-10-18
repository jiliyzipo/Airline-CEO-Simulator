using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AirlineCEO.UI.Framework
{
    /// <summary>互斥 Tab 组。</summary>
    public class TabGroupView : MonoBehaviour
    {
        [Serializable]
        public class TabEntry
        {
            public string tabId;
            public Button button;
            public TextMeshProUGUI label;
            public GameObject pageRoot;
        }

        [SerializeField] TabEntry[] tabs;
        [SerializeField] Color activeColor = Color.white;
        [SerializeField] Color inactiveColor = new Color(1f, 1f, 1f, 0.55f);

        public string ActiveTabId { get; private set; }
        public event Action<string> TabChanged;

        void Awake()
        {
            if (tabs == null) return;
            for (int i = 0; i < tabs.Length; i++)
            {
                var t = tabs[i];
                if (t?.button == null) continue;
                string id = t.tabId;
                t.button.onClick.AddListener(() => Select(id));
            }
            if (tabs.Length > 0 && !string.IsNullOrEmpty(tabs[0].tabId))
                Select(tabs[0].tabId);
        }

        public void Select(string tabId)
        {
            if (tabs == null) return;
            ActiveTabId = tabId;
            for (int i = 0; i < tabs.Length; i++)
            {
                var t = tabs[i];
                if (t == null) continue;
                bool on = t.tabId == tabId;
                if (t.pageRoot != null) t.pageRoot.SetActive(on);
                if (t.label != null) t.label.color = on ? activeColor : inactiveColor;
            }
            TabChanged?.Invoke(tabId);
        }
    }
}
