using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.UI;
using AirlineCEO.UI.Framework;
using AirlineCEO.UI.Shell;
using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>
    /// 按 pageId 导航：注册 UIConfig → 实例化 Prefab → 压栈/返回。
    /// Phase 1：支持 Resources 目录加载、L1 替换导航、隐藏非栈顶面板。
    /// </summary>
    public class NavigationRouter : MonoBehaviour
    {
        public static NavigationRouter Instance { get; private set; }

        [SerializeField] Transform panelRoot;
        [SerializeField] List<UIConfig> catalog = new List<UIConfig>();
        [SerializeField] bool hideNonTop = true;

        readonly Dictionary<string, UIConfig> _byId = new Dictionary<string, UIConfig>(StringComparer.Ordinal);
        readonly Stack<string> _stack = new Stack<string>();
        readonly Dictionary<string, GameObject> _spawned = new Dictionary<string, GameObject>(StringComparer.Ordinal);

        public string CurrentPageId => _stack.Count > 0 ? _stack.Peek() : null;
        /// <summary>栈顶下面一页（设置浮层打开时用来保持底下页可见）。</summary>
        public string PageUnderTop
        {
            get
            {
                if (_stack.Count < 2) return null;
                var arr = _stack.ToArray();
                return arr.Length > 1 ? arr[1] : null;
            }
        }
        public int StackDepth => _stack.Count;
        public Transform PanelRoot => panelRoot;

        public event Action<string> Navigated;
        public event Action<string> Closed;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            RebuildIndex();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetPanelRoot(Transform root) => panelRoot = root;

        /// <summary>底栏 L1 必须挂回内容区。子页曾把 panelRoot 指到 ModuleFrame，不复位则新页建在已关闭的宿主里（空白）。</summary>
        public void UseShellRoot(Transform root)
        {
            panelRoot = root;
            if (root == null) return;
            foreach (var kv in _spawned)
            {
                if (kv.Value == null) continue;
                if (kv.Key != null && (kv.Key.StartsWith("n40") || kv.Key.Contains("设置")))
                    continue;
                if (kv.Value.transform.parent != root)
                    kv.Value.transform.SetParent(root, false);
            }
        }

        public void SetCatalog(IEnumerable<UIConfig> configs)
        {
            catalog.Clear();
            if (configs != null) catalog.AddRange(configs);
            RebuildIndex();
        }

        /// <summary>从 Resources/UIConfig 加载全部 UIConfig。</summary>
        public int LoadCatalogFromResources()
        {
            var loaded = Resources.LoadAll<UIConfig>(UIPrefabCatalog.ResourcesConfigFolder);
            SetCatalog(loaded);
            return loaded != null ? loaded.Length : 0;
        }

        public void Register(UIConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.pageId)) return;
            _byId[config.pageId] = config;
            if (!catalog.Contains(config)) catalog.Add(config);
        }

        public bool TryGet(string pageId, out UIConfig config) =>
            _byId.TryGetValue(pageId, out config);

        /// <summary>打开页面；replaceCurrent 为 true 时先弹出当前页（底栏 Tab 切换用）。</summary>
        public bool Navigate(string pageId, bool replaceCurrent = false)
        {
            if (string.IsNullOrEmpty(pageId) || !_byId.TryGetValue(pageId, out var cfg))
            {
                Debug.LogWarning($"[NavigationRouter] 未知 pageId: {pageId}");
                return false;
            }

            if (replaceCurrent && _stack.Count > 0)
                PopInternal(destroy: false);

            if (_stack.Count > 0 && _stack.Peek() == pageId)
            {
                RefreshVisibility();
                return true;
            }

            EnsureInstance(cfg);
            _stack.Push(pageId);
            RefreshVisibility();
            Navigated?.Invoke(pageId);
            return true;
        }

        public bool Back()
        {
            if (_stack.Count == 0) return false;
            string closed = PopInternal(destroy: false);
            RefreshVisibility();
            Closed?.Invoke(closed);
            return true;
        }

        /// <summary>关掉压在当前模块上的子页，保留栈底 L1。</summary>
        public void PopOverlaysKeepBase()
        {
            while (_stack.Count > 1)
                PopInternal(destroy: false);
            RefreshVisibility();
        }

        /// <summary>右上角 ×：一键关闭整层叠弹窗（清空导航栈），回到底层玩法页。</summary>
        public void CloseOverlay()
        {
            string last = CurrentPageId;
            ClearStack(destroyInstances: false);
            Closed?.Invoke(last ?? "");
        }

        public void ClearStack(bool destroyInstances = false)
        {
            while (_stack.Count > 0)
                PopInternal(destroy: destroyInstances);
            if (destroyInstances)
            {
                foreach (var kv in _spawned)
                {
                    if (kv.Value != null) Destroy(kv.Value);
                }
                _spawned.Clear();
            }
            else RefreshVisibility();
        }

        void RebuildIndex()
        {
            _byId.Clear();
            for (int i = 0; i < catalog.Count; i++)
            {
                var c = catalog[i];
                if (c == null || string.IsNullOrEmpty(c.pageId)) continue;
                _byId[c.pageId] = c;
            }
        }

        void EnsureInstance(UIConfig cfg)
        {
            if (_spawned.TryGetValue(cfg.pageId, out var existing) && existing != null)
            {
                if (PreferFleetNetRuntime(cfg.pageId) && !HasFleetNetRuntimePanel(existing))
                {
                    _spawned.Remove(cfg.pageId);
                    Destroy(existing);
                    existing = null;
                }
                else
                {
                    existing.SetActive(true);
                    ReparentToPanelRoot(existing);
                    return;
                }
            }

            var parent = panelRoot != null ? panelRoot : transform;
            GameObject go = null;

            if (PreferShellRuntime(cfg.pageId))
            {
                go = ShellRuntimeFactory.TryCreate(cfg.pageId, parent);
            }

            if (go == null && PreferFleetNetRuntime(cfg.pageId))
            {
                go = FleetNetRuntimeFactory.TryCreate(cfg.pageId, parent);
            }

            if (go == null && cfg.prefab != null)
            {
                go = Instantiate(cfg.prefab, parent, false);
            }

            if (go == null)
            {
                go = OpsRuntimeFactory.TryCreate(cfg.pageId, parent);
                if (go == null)
                    go = FinanceRuntimeFactory.TryCreate(cfg.pageId, parent);
                if (go == null)
                    go = HrRuntimeFactory.TryCreate(cfg.pageId, parent);
                if (go == null)
                    go = FleetNetRuntimeFactory.TryCreate(cfg.pageId, parent);
                if (go == null)
                    go = MktRuntimeFactory.TryCreate(cfg.pageId, parent);
                if (go == null)
                    go = StrategyRuntimeFactory.TryCreate(cfg.pageId, parent);
                if (go == null)
                    go = MaintenanceRuntimeFactory.TryCreate(cfg.pageId, parent);
                if (go == null)
                    go = SafetyRuntimeFactory.TryCreate(cfg.pageId, parent);
                if (go == null)
                    go = ShellRuntimeFactory.TryCreate(cfg.pageId, parent);
                if (go == null)
                    return; // 允许仅占栈
            }

            go.name = cfg.pageId;
            ReparentToPanelRoot(go);

            _spawned[cfg.pageId] = go;

            var dept = go.GetComponent<DeptL1PanelView>();
            if (dept != null)
            {
                dept.ApplyConfig(cfg);
                dept.Show();
                return;
            }

            var panel = go.GetComponent<BasePanel>();
            panel?.BindPage(cfg.pageId, cfg.title);
            panel?.Show();
        }

        void ReparentToPanelRoot(GameObject go)
        {
            if (go == null) return;
            var parent = panelRoot != null ? panelRoot : transform;
            if (parent != null && go.transform.parent != parent)
                go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
            }
            go.SetActive(true);
        }

        static bool PreferFleetNetRuntime(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return false;
            return pageId.StartsWith("n10_") || PageIdMatch.Is(pageId, "n11")
                || pageId.StartsWith("n107") || pageId.StartsWith("n97") || pageId.StartsWith("n41")
                || pageId.Contains("飞机详情") || pageId.Contains("单航线") || pageId.Contains("购机");
        }

        static bool HasFleetNetRuntimePanel(GameObject go)
        {
            if (go == null) return false;
            return go.GetComponent<FleetSubpagePanel>() != null
                || go.GetComponent<PurchaseDecisionPanel>() != null
                || go.GetComponent<RouteDetailPanel>() != null;
        }

        static bool PreferShellRuntime(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return false;
            return pageId.StartsWith("n6_") || pageId.StartsWith("n7_") || pageId.StartsWith("n8_")
                   || pageId.StartsWith("n110") || pageId.StartsWith("n111")
                   || pageId.StartsWith("n12_") || pageId.StartsWith("n28_")
                   || pageId.StartsWith("n30_") || pageId.StartsWith("n38_")
                   || pageId.StartsWith("n21_") || pageId.StartsWith("n104_")
                   || pageId.StartsWith("n23_") || pageId.StartsWith("n26_")
                   || pageId.StartsWith("n40_") || pageId.StartsWith("n115") || pageId.StartsWith("n116")
                   || pageId.Contains("主页") || pageId.Contains("收件箱") || pageId.Contains("航空事务官")
                   || pageId.Contains("设置") || pageId.Contains("外部信息源") || pageId.Contains("行情源");
        }

        /// <summary>测试/运行时附加已生成实例。</summary>
        public void AttachRuntimeInstance(string pageId, GameObject go)
        {
            if (string.IsNullOrEmpty(pageId) || go == null) return;
            _spawned[pageId] = go;
            RefreshVisibility();
        }

        /// <summary>丢掉已生成实例，下次 Navigate 会按新工厂重建。</summary>
        public void DiscardInstance(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return;
            if (_spawned.TryGetValue(pageId, out var go))
            {
                _spawned.Remove(pageId);
                if (go != null) Destroy(go);
            }
        }

        public GameObject GetSpawned(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return null;
            return _spawned.TryGetValue(pageId, out var go) ? go : null;
        }

        public bool HasInstance(string pageId) =>
            !string.IsNullOrEmpty(pageId) &&
            _spawned.TryGetValue(pageId, out var go) && go != null;

        void RefreshVisibility()
        {
            string top = CurrentPageId;
            bool topIsShell = IsShellTab(top);
            foreach (var kv in _spawned)
            {
                if (kv.Value == null) continue;
                bool isHome = kv.Key != null && (kv.Key.StartsWith("n6_", StringComparison.Ordinal) || kv.Key.IndexOf("Dashboard", StringComparison.Ordinal) >= 0);
                bool settingsOnTop = top != null && (top.StartsWith("n40") || (top.Contains("设置") && !top.Contains("外部")));
                string under = settingsOnTop ? PageUnderTop : null;
                bool show = !hideNonTop || kv.Key == top || (settingsOnTop && kv.Key == under);
                if (!show && !topIsShell && IsModuleFrameHost(kv.Value))
                    show = true;

                // 主页地图不 SetActive(false)，避免每次回来都重载瓦片。
                if (isHome)
                {
                    kv.Value.SetActive(true);
                    var cg = kv.Value.GetComponent<CanvasGroup>();
                    if (cg == null) cg = kv.Value.AddComponent<CanvasGroup>();
                    bool on = kv.Key == top || (settingsOnTop && under != null &&
                        (under == kv.Key || under.StartsWith("n6_") || under.IndexOf("Dashboard", StringComparison.Ordinal) >= 0));
                    cg.alpha = on ? 1f : 0f;
                    cg.blocksRaycasts = on && !settingsOnTop;
                    cg.interactable = on && !settingsOnTop;
                    continue;
                }

                kv.Value.SetActive(show);
                if (show && !settingsOnTop) kv.Value.transform.SetAsLastSibling();
            }
        }

        static bool IsShellTab(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return false;
            var defs = UIPrefabCatalog.ShellTabs;
            for (int i = 0; i < defs.Length; i++)
                if (defs[i].pageId == pageId) return true;
            return false;
        }

        static bool IsModuleFrameHost(GameObject go)
        {
            var body = ModuleFrame.Body;
            return go != null && body != null && body.IsChildOf(go.transform);
        }

        string PopInternal(bool destroy)
        {
            string id = _stack.Pop();
            if (_spawned.TryGetValue(id, out var go) && go != null)
            {
                if (destroy)
                {
                    Destroy(go);
                    _spawned.Remove(id);
                }
                else go.SetActive(false);
            }
            return id;
        }
    }
}
