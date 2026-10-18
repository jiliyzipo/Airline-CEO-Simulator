using System;
using System.Collections.Generic;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI.Shell
{
    /// <summary>n0 底栏 Tab：点击 → pageId，由 NavigationRouter 打开 L1。
    /// 状态（财务/航线/机队）独立成一条状态栏，悬在底栏正上方（PrefabNavIntegration 构建并定位）。</summary>
    public class ShellBarView : MonoBehaviour
    {
        [Serializable]
        public class TabButton
        {
            public string pageId;
            public Button button;
            public TextMeshProUGUI label;
            public Image background;
            public bool core;
        }

        [SerializeField] List<TabButton> tabs = new List<TabButton>();
        [SerializeField] Color activeColor = UITheme.NavActive;
        [SerializeField] Color idleColor = UITheme.NavIdle;

        public string ActivePageId { get; private set; }
        Button _backBtn;
        Button _fwdBtn;
        public event Action<string> TabSelected;
        public event Action BackClicked;
        public event Action ForwardClicked;

        public IReadOnlyList<TabButton> Tabs => tabs;

        GameObject _inboxBadge;

        public void SetTabs(List<TabButton> list)
        {
            tabs = list ?? new List<TabButton>();
            WireClicks();
            RefreshBadge();   // 收件箱角标依赖 tabs，换表后重建
        }

        void Awake()
        {
            WireClicks();
        }

        void Start()
        {
            RefreshBadge();
        }

        void OnEnable()
        {
            MessageCenter.Changed -= RefreshBadge;
            MessageCenter.Changed += RefreshBadge;
            InboxManager.Changed -= RefreshBadge;
            InboxManager.Changed += RefreshBadge;
            RefreshBadge();
        }

        void OnDisable()
        {
            MessageCenter.Changed -= RefreshBadge;
            InboxManager.Changed -= RefreshBadge;
        }

        void WireClicks()
        {
            if (tabs == null) return;
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                if (t?.button == null) continue;
                t.button.onClick.RemoveAllListeners();
                string id = t.pageId;
                t.button.onClick.AddListener(() => Select(id, invoke: true));
            }
        }

        /// <summary>收件箱 Tab 未读角标：只计未点开的邮件；全部点开后隐藏。</summary>
        void RefreshBadge()
        {
            if (_inboxBadge == null) BuildInboxBadge();
            int unread = InboxManager.UnreadCount(GameState.Instance);
            UIFactory.SetBadge(_inboxBadge, unread);
        }

        void BuildInboxBadge()
        {
            if (tabs == null) return;
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                if (t?.pageId == null || !t.pageId.Contains("收件箱") || t.button == null) continue;
                if (_inboxBadge == null)
                    _inboxBadge = UIFactory.Badge(t.button.transform, "UnreadBadge", new Vector2(-4f, -4f), UITheme.Danger);
                return;
            }
        }

        public void Select(string pageId, bool invoke = true)
        {
            ActivePageId = pageId;
            if (tabs != null)
            {
                for (int i = 0; i < tabs.Count; i++)
                {
                    var t = tabs[i];
                    if (t == null) continue;
                    bool on = t.pageId == pageId;
                    if (t.background != null)
                    {
                        if (t.core)
                            t.background.color = on ? UITheme.NavCoreActive : UITheme.NavCoreIdle;
                        else
                            t.background.color = on ? activeColor : idleColor;
                    }
                }
            }

            if (invoke) TabSelected?.Invoke(pageId);
        }

        const string LockPrefix = "锁 ";

        /// <summary>P3 功能闸门：按财年刷新底栏部门 Tab 的锁定视觉（🔒 + 不可点击）。</summary>
        public void RefreshLocks(int fiscalYear)
        {
            if (tabs == null) return;
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                if (t == null) continue;
                int unlockYear = ProgressionGate.UnlockYearForDept(t.pageId);
                bool locked = unlockYear > 0 && fiscalYear < unlockYear;
                if (t.button != null) t.button.interactable = !locked;
                if (t.label != null)
                {
                    string cur = t.label.text ?? "";
                    string baseText = cur.StartsWith(LockPrefix, StringComparison.Ordinal)
                        ? cur.Substring(LockPrefix.Length)
                        : cur;
                    t.label.text = locked ? LockPrefix + baseText : baseText;
                    ChineseFontProvider.EnsureCharacters(t.label.text);
                }
            }
        }

        /// <summary>按 <see cref="UIPrefabCatalog.ShellTabs"/> 重建底栏（覆盖过期 Prefab 的 12 部门 tab）。</summary>
        public void RebuildFromCatalog()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child != null && child.name.StartsWith("Tab_", StringComparison.Ordinal))
                    DestroyImmediate(child.gameObject);
            }

            _inboxBadge = null;
            EnsureHistoryButtons();
            var list = BuildTabButtons(transform);
            SetTabs(list);
            if (!string.IsNullOrEmpty(ActivePageId))
                Select(ActivePageId, invoke: false);
        }

        static List<TabButton> BuildTabButtons(Transform bar)
        {
            var list = new List<TabButton>();
            var defs = UIPrefabCatalog.ShellTabs;
            float total = 0f;
            for (int i = 0; i < defs.Length; i++)
                total += Mathf.Max(0.2f, defs[i].weight);
            if (total <= 0f) total = defs.Length;

            const float tabStart = 0.11f;
            float cursor = 0f;
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                float w = Mathf.Max(0.2f, def.weight);
                float x0 = tabStart + (1f - tabStart) * (cursor / total);
                cursor += w;
                float x1 = tabStart + (1f - tabStart) * (cursor / total);
                Color idle = def.core ? UITheme.NavCoreIdle : UITheme.NavIdle;
                int font = def.core ? 15 : 13;
                var btn = UIFactory.Button(bar, "Tab_" + def.label, def.label,
                    new UIFactory.Anchor(new Vector2(x0 + 0.002f, 0.12f), new Vector2(x1 - 0.002f, 0.88f),
                        Vector2.zero, Vector2.zero),
                    idle, () => { }, font);
                list.Add(new TabButton
                {
                    pageId = def.pageId,
                    button = btn,
                    background = btn.GetComponent<Image>(),
                    label = btn.GetComponentInChildren<TextMeshProUGUI>(),
                    core = def.core
                });
            }

            return list;
        }

        /// <summary>运行时构建底栏（无 Prefab 时的回退路径，保持 UIFactory 兼容）。</summary>
        public static ShellBarView BuildRuntime(Transform parent)
        {
            var bar = UIFactory.Panel(parent, UIPrefabCatalog.ShellN0,
                new UIFactory.Anchor(new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(12, 8), new Vector2(-12, 80)),
                UITheme.Glass);
            UIFactory.AddBorder(bar, UITheme.Border, new Vector2(1f, -1f));

            var view = bar.AddComponent<ShellBarView>();
            view.EnsureHistoryButtons();
            view.SetTabs(BuildTabButtons(bar.transform));
            return view;
        }

        void EnsureHistoryButtons()
        {
            if (_backBtn == null)
            {
                var existing = transform.Find("NavBack");
                _backBtn = existing != null ? existing.GetComponent<Button>() : null;
            }
            if (_fwdBtn == null)
            {
                var existing = transform.Find("NavFwd");
                _fwdBtn = existing != null ? existing.GetComponent<Button>() : null;
            }
            if (_backBtn == null)
            {
                _backBtn = UIFactory.Button(transform, "NavBack", "<",
                    new UIFactory.Anchor(new Vector2(0.004f, 0.12f), new Vector2(0.052f, 0.88f),
                        Vector2.zero, Vector2.zero),
                    UITheme.NavIdle, () => BackClicked?.Invoke(), 18);
            }
            if (_fwdBtn == null)
            {
                _fwdBtn = UIFactory.Button(transform, "NavFwd", ">",
                    new UIFactory.Anchor(new Vector2(0.056f, 0.12f), new Vector2(0.104f, 0.88f),
                        Vector2.zero, Vector2.zero),
                    UITheme.NavIdle, () => ForwardClicked?.Invoke(), 18);
            }
            if (_backBtn != null)
            {
                _backBtn.onClick.RemoveAllListeners();
                _backBtn.onClick.AddListener(() => BackClicked?.Invoke());
            }
            if (_fwdBtn != null)
            {
                _fwdBtn.onClick.RemoveAllListeners();
                _fwdBtn.onClick.AddListener(() => ForwardClicked?.Invoke());
            }
            SetHistoryEnabled(false, false);
        }

        public void SetHistoryEnabled(bool canBack, bool canForward)
        {
            if (_backBtn != null) _backBtn.interactable = canBack;
            if (_fwdBtn != null) _fwdBtn.interactable = canForward;
        }
    }
}
