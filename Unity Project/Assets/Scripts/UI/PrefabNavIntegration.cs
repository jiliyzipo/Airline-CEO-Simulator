using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Navigation;
using AirlineCEO.UI.Shell;
using UnityEngine;

namespace AirlineCEO.UI
{
    /// <summary>
    /// Phase 1：底栏 Shell 驱动导航。有完整玩法的页走 MVPBootstrap；其余走 Prefab 占位。
    /// </summary>
    public class PrefabNavIntegration : MonoBehaviour
    {
        [SerializeField] bool autoAttachOnStart = true;
        [SerializeField] bool openDefaultMarket = false;

        ShellBarView _shell;
        StatusBarView _statusBar;
        NavigationRouter _router;
        RectTransform _contentHost;
        RectTransform _overlayHost;
        Transform _topBar;
        MVPBootstrap _bootstrap;
        bool _gameplayReady;
        readonly NavHistory _history = new NavHistory();
        bool _historyApplying;

        /// <summary>Shell 底栏高度（像素）：prefab 与运行时构建保持一致。</summary>
        const float ShellBarHeight = 72f;
        /// <summary>状态栏下缘（画布像素）：底栏上缘（8+72）+ 4 间距。</summary>
        const float StatusBarBottomY = 84f;
        /// <summary>内容区下缘（画布像素）：状态栏上缘（84+30）+ 4 间距。</summary>
        const float ContentBottomY = 118f;

        public NavigationRouter Router => _router;
        public ShellBarView Shell => _shell;
        public StatusBarView StatusBar => _statusBar;
        public bool GameplayReady => _gameplayReady;

        void Start()
        {
            if (autoAttachOnStart && !SuppressAutoAttachForTests)
                Attach();
        }

        /// <summary>
        /// 测试用：关掉 <see cref="Start"/> 里的自动 <see cref="Attach"/>。
        /// `Attach()` 需要 Canvas / Resources 并会建出整条壳层，闸门类用例只关心
        /// `_gameplayReady` 这一个布尔量，不该被壳层构建的副作用牵连（同
        /// `SetupFlowController.BypassForTests` 的口径）。
        /// </summary>
        public static bool SuppressAutoAttachForTests;

        public void Attach()
        {
            if (_router != null) return;

            _bootstrap = GetComponent<MVPBootstrap>() ?? FindFirstObjectByType<MVPBootstrap>();

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[Phase1] 未找到 Canvas，跳过 PrefabNavIntegration");
                return;
            }

            Transform canvasTf = canvas.transform;

            // 销毁可能残留的旧左侧导航
            var legacyNav = canvasTf.Find("LeftNav");
            if (legacyNav != null)
                Destroy(legacyNav.gameObject);

            var hostGo = UIFactory.Panel(canvasTf, "Phase1_ContentHost",
                new UIFactory.Anchor(new Vector2(0, 0), new Vector2(1, 1),
                    new Vector2(20, ContentBottomY), new Vector2(-20, -UITheme.TopBarHeight - 12)),
                new Color(0, 0, 0, 0), rounded: false);
            hostGo.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            _contentHost = hostGo.GetComponent<RectTransform>();
            hostGo.SetActive(false);

            var overlayGo = UIFactory.Panel(canvasTf, "Phase1_OverlayHost",
                new UIFactory.Anchor(new Vector2(0, 0), new Vector2(1, 1),
                    new Vector2(0, ContentBottomY), new Vector2(0, -UITheme.TopBarHeight)),
                new Color(0, 0, 0, 0), rounded: false);
            var overlayImg = overlayGo.GetComponent<UnityEngine.UI.Image>();
            if (overlayImg != null) overlayImg.raycastTarget = false;
            _overlayHost = overlayGo.GetComponent<RectTransform>();

            var routerGo = new GameObject("NavigationRouter", typeof(RectTransform));
            routerGo.transform.SetParent(canvasTf, false);
            _router = routerGo.AddComponent<NavigationRouter>();
            _router.SetPanelRoot(_contentHost);
            int loaded = _router.LoadCatalogFromResources();
            Debug.Log($"[Phase1] NavigationRouter catalog={loaded}");
            _router.Navigated -= OnRouterNavigated;
            _router.Navigated += OnRouterNavigated;
            _router.Closed -= OnRouterClosed;
            _router.Closed += OnRouterClosed;
            _router.Closed += _ =>
            {
                if (_router.StackDepth == 0 && _contentHost != null)
                    _contentHost.gameObject.SetActive(false);
            };

            var shellPrefab = LoadShellPrefab();
            if (shellPrefab != null)
            {
                var shellGo = Instantiate(shellPrefab, canvasTf, false);
                shellGo.name = UIPrefabCatalog.ShellN0;
                var srt = shellGo.GetComponent<RectTransform>();
                if (srt != null)
                {
                    srt.anchorMin = new Vector2(0, 0);
                    srt.anchorMax = new Vector2(1, 0);
                    srt.pivot = new Vector2(0.5f, 0);
                    srt.offsetMin = new Vector2(12, 8);
                    srt.offsetMax = new Vector2(-12, 8 + ShellBarHeight);
                }
                _shell = shellGo.GetComponent<ShellBarView>();
            }

            if (_shell == null)
                _shell = ShellBarView.BuildRuntime(canvasTf);
            else
                _shell.RebuildFromCatalog();

            // 状态栏：悬在底栏正上方一条（财务/航线/机队实时状态）
            _statusBar = StatusBarView.Build(canvasTf);
            var brt = _statusBar.transform as RectTransform;
            if (brt != null)
            {
                brt.offsetMin = new Vector2(12, StatusBarBottomY);
                brt.offsetMax = new Vector2(-12, StatusBarBottomY + StatusBarView.BarHeight);
            }

            _topBar = canvasTf.Find("TopBar");
            RaiseChrome();

            _shell.TabSelected += OnTabSelected;
            _shell.BackClicked += GoBack;
            _shell.ForwardClicked += GoForward;
            RefreshHistoryButtons();
            ApplyChromeVisibility();

            // 默认不打开玩法页：等 SetupFlowController 开局完成后再 OpenHomeDashboard
            if (openDefaultMarket)
            {
                // 这个开关的语义就是「跳过向导直接进局内」，所以要显式打开闸门，
                // 否则下面的 OnTabSelected 会被 RejectIfNotGameplayReady 拦掉。
                EnterGameplay();
                OnTabSelected("n14_市场销售部");
            }

            EnsureShellCatalogExtras();
        }

        /// <summary>
        /// 壳层 chrome 显隐。抽成**静态**是为了让「三条一起」这条规则可被测试直接钉住 ——
        /// 实例版本要跑起来得先把整套导航 `Attach` 起来，测试成本过高。
        /// </summary>
        public static void ApplyChromeVisibility(bool visible, ShellBarView shell, StatusBarView statusBar, Transform topBar)
        {
            if (shell != null) shell.gameObject.SetActive(visible);
            if (statusBar != null) statusBar.gameObject.SetActive(visible);
            if (topBar != null) topBar.gameObject.SetActive(visible);
        }

        /// <summary>
        /// 壳层 chrome（顶栏 / 底栏 / 状态栏）随开局态显隐。
        ///
        /// 开局向导（主菜单 / 资料 / 选航司 / 公司注册 / 董事会…）是**全屏闸门**，此刻这三条都该收起来：
        /// 顶栏上全是**局内**信息 —— 航司名、游戏内钟表、速度档、机场搜索。
        /// 向导阶段它们不是「暂时用不上」而是**错的**：`RefreshHud` 在 `companyNameZh` 为空时兜底成
        /// 默认的「天际航空」，`RefreshClock` 读的是默认 `GameState` 的财年 1 时钟，玩家看到的是
        /// 一家自己没选过的航司 + 一个还没开始的日期；而速度档按钮是**活的**，在菜单里点它会真的改
        /// `Time.timeScale`。
        ///
        /// 底栏 `_shell` 与状态栏 `_statusBar` 此前已在向导期收起，**顶栏漏了** —— 补上这一个。
        /// </summary>
        void ApplyChromeVisibility() => ApplyChromeVisibility(_gameplayReady, _shell, _statusBar, _topBar);

        /// <summary>开局 / 读档完成：放开玩法态闸门（壳层 chrome 上线）。不可逆。</summary>
        public void EnterGameplay()
        {
            _gameplayReady = true;
            ApplyChromeVisibility();
        }

        /// <summary>开局完成后进入主页。</summary>
        public void OpenHomeDashboard()
        {
            EnterGameplay();
            if (_router == null) Attach();
            if (_shell != null)
                _shell.RefreshLocks(GameState.Instance != null ? GameState.Instance.year : 1);
            if (_statusBar != null)
                _statusBar.RefreshStats();
            SelectTab("n6_主页_Dashboard");

            // P3 功能闸门：跟随状态变化刷新底栏锁（财年推进后自动解锁新部门）
            var gs = GameState.Instance;
            if (gs != null)
            {
                gs.OnStateChanged -= RefreshGateLocks;
                gs.OnStateChanged += RefreshGateLocks;
            }
        }

        void RefreshGateLocks()
        {
            var gs = GameState.Instance;
            if (gs != null && _shell != null)
                _shell.RefreshLocks(gs.year);
        }

        public void SelectTab(string pageId)
        {
            // 闸门放在 Attach() 之前：向导期不仅不该导航，也不该顺手把整套壳层建起来。
            if (RejectIfNotGameplayReady("切换页签 " + pageId)) return;
            if (_router == null) Attach();
            OnTabSelected(pageId);
        }

        /// <summary>
        /// 玩法态闸门：开局 / 读档完成前，导航一律拒绝执行。
        ///
        /// 到这一步为止，向导期的防护全是「入口不可达」——底栏、顶栏、顾问、收件箱在
        /// `!_gameplayReady` 时都隐藏了，所以点不到。但「点不到」不是「进不去」：
        /// 机场搜索的回车就是这样一条绕过整个开局向导的路子（已单独堵掉），导航 API 本身
        /// 也仍是公开的。这条闸门把最后这层补上，口径与 `ApplyChromeVisibility` /
        /// `AirportSearchAllowedFor` 完全同源，都是 `_gameplayReady`。
        ///
        /// 编辑器菜单（`OpenCommercePanelMenu` / `OpenFinancePanelMenu` / `OpsManualTestMenu`）
        /// 不受影响：它们都先 `EnsureInGameplay()`，走的是
        /// `SetupFlowController.EnterGameplay` → `OpenHomeDashboard` → 本类 `EnterGameplay()`。
        /// </summary>
        /// <returns>true = 应拒绝本次导航。</returns>
        bool RejectIfNotGameplayReady(string what)
        {
            if (_gameplayReady) return false;
            Debug.LogWarning(
                $"[Phase1] 忽略「{what}」：开局 / 读档尚未完成，导航已锁定。" +
                "（向导期应只有 SetupFlowController 的叠加层可见；若向导已结束，检查是否漏调 PrefabNavIntegration.EnterGameplay）");
            return true;
        }

        void EnsureShellCatalogExtras()
        {
            if (_router == null) return;
            RegisterIfMissing("n8_航空事务官_AO", "航空事务官", "L2", "n0_主界面框架_常驻");
            RegisterIfMissing("n40_设置_存档", "设置 / 存档", "L2", "n0_主界面框架_常驻");
            RegisterIfMissing(ExternalSourcePanelUI.PageKey, "外部信息源", "L2", "n40_设置_存档");
            RegisterIfMissing(QuoteSourcePanelUI.PageKey, "行情源", "L3", ExternalSourcePanelUI.PageKey);
            RegisterIfMissing("n28_宏观经济面板", "宏观经济面板", "L2", "n6_主页_Dashboard");
            RegisterIfMissing("n30_随机事件日志", "随机事件日志", "L2", "n6_主页_Dashboard");
            RegisterIfMissing("n38_历史回放", "历史回放", "L2", "n6_主页_Dashboard");
            RegisterIfMissing("n51_消息中心", "消息中心", "L2", "n0_主界面框架_常驻");
            RegisterIfMissing("n21_CEO_决策会议", "CEO 决策会议", "L4", "n7_收件箱");
            RegisterIfMissing("n104_补条件表单", "补条件表单", "L5", "n21_CEO_决策会议");
            RegisterIfMissing("n5_年度目标确认", "年度目标确认", "L0", "n16_战略发展部");
            RegisterIfMissing("n20_董事会", "董事会", "L2", "n16_战略发展部");
            RegisterIfMissing("n22_年度预算编制_审批", "年度预算编制 / 审批", "L2", "n15_财务部");
            RegisterIfMissing("n26_财年结算_续聘_解雇", "财年结算 / 续聘 / 解雇", "L4", "n20_董事会");
            RegisterIfMissing("n6_主页_Dashboard", "主页 Dashboard", "L1", "n0_主界面框架_常驻");
            RegisterIfMissing(CeoDossierPanel.PageProfile, "个人资料", "L2", "n6_主页_Dashboard");
            RegisterIfMissing(CeoDossierPanel.PageContract, "CEO 合同", "L2", "n6_主页_Dashboard");
            RegisterIfMissing("n7_收件箱", "收件箱", "L1", "n0_主界面框架_常驻");
            RegisterIfMissing("n12_航线网络", "航线网络", "L1", "n0_主界面框架_常驻");
            RegisterIfMissing("n114_员工信息", "员工信息", "L3", "n18_人力资源部");
            RegisterIfMissing("n113_部门信息", "部门信息", "L2", "n18_人力资源部");
            RegisterIfMissing("n112_职业介绍所", "职业介绍所", "L2", "n18_人力资源部");
            RegisterIfMissing("n41_单航线", "单航线", "L2", "n12_航线网络");
            RegisterIfMissing("n35_新航线_RPM_评估台", "RPM 评估台", "L2", "n16_战略发展部");
        }

        void RegisterIfMissing(string pageId, string title, string level, string parentId)
        {
            if (_router.TryGet(pageId, out var existing) && existing != null) return;
            var cfg = ScriptableObject.CreateInstance<UIConfig>();
            cfg.pageId = pageId;
            cfg.title = title;
            cfg.level = level;
            cfg.parentId = parentId;
            cfg.meta = "Phase1.4 runtime";
            _router.Register(cfg);
        }

        /// <summary>顾问/邮件跳转：底栏 L1 走切 Tab（替换当前页），其余走叠层子页。</summary>
        public void OpenGameplayPage(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return;
            // n77 航权与时刻槽已内嵌进「航线」模块页签：深链统一改投 n12 并直开该页签。
            if (pageId.StartsWith("n77", System.StringComparison.Ordinal))
            {
                FleetNetContext.OpenRightsTab = true;
                SelectTab("n12_航线网络");
                var net = FindFirstObjectByType<NetworkPanelUI>(FindObjectsInactive.Include);
                net?.ShowRightsTab();
                return;
            }
            if (IsShellTab(pageId))
            {
                SelectTab(pageId);
                return;
            }
            OpenSubPage(pageId);
        }

        /// <summary>从运营部等玩法页打开 L2/L3 Prefab 子页（叠在内容层）。</summary>
        public void OpenSubPage(string pageId)
        {
            if (RejectIfNotGameplayReady("打开子页 " + pageId)) return;

            if (!string.IsNullOrEmpty(pageId) && (pageId.StartsWith("n51") || pageId.Contains("消息中心")))
            {
                SelectTab("n7_收件箱");
                return;
            }
            if (_router == null) Attach();

            // 设置 / CEO 资料合同 / 外部信息源：始终叠在内容层，不嵌进机队/财务等模块子框
            // （否则从底栏部门页再点齿轮会得到空白页：内容层已关闭，子框只剩空壳）
            bool overlay = IsGlobalOverlayPage(pageId);
            bool settingsPopover = IsSettingsPage(pageId);
            if (settingsPopover)
            {
                ModuleFrame.ReleaseHost();
                if (_overlayHost != null)
                    _router?.SetPanelRoot(_overlayHost);
                RaiseChrome();
            }
            else if (overlay)
            {
                ModuleFrame.ReleaseHost();
                _bootstrap?.HideAllLegacyCenterPanels();
                if (_contentHost != null)
                {
                    _contentHost.gameObject.SetActive(true);
                    _contentHost.SetAsLastSibling();
                    _router?.SetPanelRoot(_contentHost);
                }
                RaiseChrome();
            }

            var module = (overlay || settingsPopover) ? null : FindActiveModulePanel();
            bool inFrame = module != null;
            if (inFrame)
            {
                ModuleFrame.EnsureSubHost(module);
                ModuleFrame.EnterSubpage();
                _router.SetPanelRoot(ModuleFrame.Body);
            }
            else if (!settingsPopover)
            {
                if (_contentHost != null) _contentHost.gameObject.SetActive(true);
                if (_contentHost != null) _contentHost.SetAsLastSibling();
                RaiseChrome();
            }

            if (_router == null)
            {
                Debug.LogWarning("[Phase2] OpenSubPage failed: router missing");
                return;
            }

            EnsureOpsConfig(pageId);
            EnsureFinanceConfig(pageId);
            EnsureFleetNetConfig(pageId);
            EnsureMktConfig(pageId);
            EnsureStrategyConfig(pageId);
            EnsureMaintenanceConfig(pageId);
            EnsureHrConfig(pageId);
            EnsureSafetyConfig(pageId);
            EnsureShellCatalogExtras();

            if (settingsPopover)
            {
                var old = _router.GetSpawned(pageId);
                if (old != null && old.transform.Find("Dimmer") == null)
                    _router.DiscardInstance(pageId);
            }

            if (!_router.Navigate(pageId, replaceCurrent: false))
            {
                Debug.LogWarning("[Phase2] OpenSubPage failed: " + pageId);
                return;
            }
            if (settingsPopover)
                ReparentToOverlayHost(_router.GetSpawned(pageId));
            else if (overlay)
                ReparentToContentHost(_router.GetSpawned(pageId));
            RecordHistory(pageId);
            Transform scanRoot = _contentHost;
            if (inFrame)
            {
                var spawnedGo = _router.GetSpawned(pageId);
                if (spawnedGo != null && ModuleFrame.Body != null)
                    spawnedGo.transform.SetParent(ModuleFrame.Body, false);
                ModuleFrame.Flatten(spawnedGo);
                _router.SetPanelRoot(_contentHost);
                RaiseChrome();
                if (ModuleFrame.Body != null) scanRoot = ModuleFrame.Body;
            }

            var spawned = scanRoot != null
                ? scanRoot.GetComponentsInChildren<OpsSubpagePanel>(true)
                : null;
            if (spawned != null)
            {
                for (int i = 0; i < spawned.Length; i++)
                {
                    if (spawned[i] != null && spawned[i].gameObject.activeInHierarchy)
                        spawned[i].RefreshBody();
                }
            }

            var finSpawned = scanRoot != null
                ? scanRoot.GetComponentsInChildren<FinanceSubpagePanel>(true)
                : null;
            if (finSpawned != null)
            {
                for (int i = 0; i < finSpawned.Length; i++)
                {
                    if (finSpawned[i] != null && finSpawned[i].gameObject.activeInHierarchy)
                        finSpawned[i].RefreshBody();
                }
            }

            var fleetSpawned = scanRoot != null
                ? scanRoot.GetComponentsInChildren<FleetSubpagePanel>(true)
                : null;
            if (fleetSpawned != null)
            {
                for (int i = 0; i < fleetSpawned.Length; i++)
                {
                    if (fleetSpawned[i] != null && fleetSpawned[i].gameObject.activeInHierarchy)
                        fleetSpawned[i].RefreshBody();
                }
            }

            var purchaseSpawned = scanRoot != null
                ? scanRoot.GetComponentsInChildren<PurchaseDecisionPanel>(true)
                : null;
            if (purchaseSpawned != null)
            {
                for (int i = 0; i < purchaseSpawned.Length; i++)
                {
                    if (purchaseSpawned[i] != null && purchaseSpawned[i].gameObject.activeInHierarchy)
                        purchaseSpawned[i].RefreshBody();
                }
            }

            var routeSpawned = scanRoot != null
                ? scanRoot.GetComponentsInChildren<RouteDetailPanel>(true)
                : null;
            if (routeSpawned != null)
            {
                for (int i = 0; i < routeSpawned.Length; i++)
                {
                    if (routeSpawned[i] != null && routeSpawned[i].gameObject.activeInHierarchy)
                        routeSpawned[i].RefreshBody();
                }
            }

            var mktSpawned = scanRoot != null
                ? scanRoot.GetComponentsInChildren<MarketSubpagePanel>(true)
                : null;
            if (mktSpawned != null)
            {
                for (int i = 0; i < mktSpawned.Length; i++)
                {
                    if (mktSpawned[i] != null && mktSpawned[i].gameObject.activeInHierarchy)
                        mktSpawned[i].RefreshBody();
                }
            }

            var selectors = scanRoot != null
                ? scanRoot.GetComponentsInChildren<OpsRouteRingSelectorUI>(true)
                : null;
            if (selectors == null) return;
            for (int i = 0; i < selectors.Length; i++)
                if (selectors[i] != null && selectors[i].gameObject.activeInHierarchy)
                    selectors[i].Refresh();
        }

        void EnsureOpsConfig(string pageId)
        {
            if (_router == null || string.IsNullOrEmpty(pageId)) return;
            if (_router.TryGet(pageId, out var existing) && existing != null) return;

            var cfg = ScriptableObject.CreateInstance<UIConfig>();
            cfg.pageId = pageId;
            cfg.tabGroup = "ops";
            cfg.badge = "Phase2";
            if (pageId.StartsWith("n42"))
            {
                cfg.title = "航班周总计划";
                cfg.level = "L2";
                cfg.parentId = "n13_运营部";
            }
            else if (pageId.StartsWith("n43"))
            {
                cfg.title = "航班飞行计划";
                cfg.level = "L2";
                cfg.parentId = "n42_航班周总计划";
            }
            else if (pageId.StartsWith("n66"))
            {
                cfg.title = "航季总纲";
                cfg.level = "L2";
                cfg.parentId = "n13_运营部";
            }
            else if (pageId.StartsWith("n36"))
            {
                cfg.title = "构建航班环";
                cfg.level = "L3";
                cfg.parentId = "n13_运营部";
            }
            else if (pageId.StartsWith("n98"))
            {
                cfg.title = "航线/航班环选择器";
                cfg.level = "L3";
                cfg.parentId = "n10_飞机详情_履历";
            }
            else if (pageId.StartsWith("n24") || pageId.Contains("IRROPS") || pageId.Contains("航班异常"))
            {
                cfg.title = "航班异常";
                cfg.level = "L4";
                cfg.parentId = "n13_运营部";
            }
            else return;

            cfg.meta = "OPS runtime";
            _router.Register(cfg);
        }

        void EnsureFinanceConfig(string pageId)
        {            if (_router == null || string.IsNullOrEmpty(pageId)) return;
            if (_router.TryGet(pageId, out var existing) && existing != null) return;

            var cfg = ScriptableObject.CreateInstance<UIConfig>();
            cfg.pageId = pageId;
            cfg.tabGroup = "fin";
            cfg.badge = "Phase2";
            if (pageId.StartsWith("n22"))
            {
                cfg.title = "年度预算编制 / 审批";
                cfg.level = "L3";
                cfg.parentId = "n15_财务部";
            }
            else if (pageId.StartsWith("n31"))
            {
                cfg.title = "对冲工作台";
                cfg.level = "L3";
                cfg.parentId = "n15_财务部";
            }
            else if (pageId.StartsWith("n32"))
            {
                cfg.title = "碳配额管理";
                cfg.level = "L3";
                cfg.parentId = "n15_财务部";
            }
            else if (pageId.StartsWith("n25"))
            {
                cfg.title = "紧急融资 / 破产保护";
                cfg.level = "L4";
                cfg.parentId = "n15_财务部";
            }
            else if (pageId.StartsWith("n103"))
            {
                cfg.title = "条款确认";
                cfg.level = "L5";
                cfg.parentId = "n25_紧急融资_破产保护";
            }
            else if (pageId.StartsWith("n106"))
            {
                cfg.title = "重组条件勾选";
                cfg.level = "L5";
                cfg.parentId = "n25_紧急融资_破产保护";
            }
            else return;

            cfg.meta = "FIN runtime";
            _router.Register(cfg);
        }

        void EnsureFleetNetConfig(string pageId)
        {
            if (_router == null || string.IsNullOrEmpty(pageId)) return;
            if (_router.TryGet(pageId, out var existing) && existing != null) return;

            var cfg = ScriptableObject.CreateInstance<UIConfig>();
            cfg.pageId = pageId;
            cfg.tabGroup = "fleet";
            cfg.badge = "Phase2";
            if (pageId.StartsWith("n10_"))
            {
                cfg.title = "飞机详情（履历）";
                cfg.level = "L2";
                cfg.parentId = "n9_机队";
            }
            else if (AirlineCEO.UI.Navigation.PageIdMatch.Is(pageId, "n11"))
            {
                cfg.title = "载客量配置";
                cfg.level = "L3";
                cfg.parentId = "n10_飞机详情_履历";
            }
            else if (pageId.StartsWith("n107"))
            {
                cfg.title = "预算确认";
                cfg.level = "L5";
                cfg.parentId = "n10_飞机详情_履历";
            }
            else if (pageId.StartsWith("n97"))
            {
                cfg.title = "购机决策会议";
                cfg.level = "L4";
                cfg.parentId = "n9_机队";
            }
            else if (pageId.StartsWith("n41"))
            {
                cfg.title = "单航线";
                cfg.level = "L2";
                cfg.parentId = "n12_航线网络";
            }
            else return;

            cfg.meta = "FLEET/NET runtime";
            _router.Register(cfg);
        }

        void EnsureMktConfig(string pageId)
        {
            if (_router == null || string.IsNullOrEmpty(pageId)) return;
            if (_router.TryGet(pageId, out var existing) && existing != null) return;

            var cfg = ScriptableObject.CreateInstance<UIConfig>();
            cfg.pageId = pageId;
            cfg.tabGroup = "mkt";
            cfg.badge = "Phase2";
            if (pageId.StartsWith("n44"))
            {
                cfg.title = "航线收益分析";
                cfg.level = "L2";
                cfg.parentId = "n14_市场销售部";
            }
            else if (pageId.StartsWith("n49"))
            {
                cfg.title = "航班定价";
                cfg.level = "L2";
                cfg.parentId = "n14_市场销售部";
            }
            else if (pageId.StartsWith("n45"))
            {
                cfg.title = "单航线定价详情";
                cfg.level = "L3";
                cfg.parentId = "n49_航班定价";
            }
            else if (pageId.StartsWith("n46"))
            {
                cfg.title = "动态定价算法面板";
                cfg.level = "L3";
                cfg.parentId = "n49_航班定价";
            }
            else if (pageId.StartsWith("n47"))
            {
                cfg.title = "收益管理仪表盘";
                cfg.level = "L3";
                cfg.parentId = "n49_航班定价";
            }
            else if (pageId.StartsWith("n48"))
            {
                cfg.title = "辅助收入管理";
                cfg.level = "L3";
                cfg.parentId = "n49_航班定价";
            }
            else if (pageId.StartsWith("n50_外部环境"))
            {
                cfg.title = "外部环境分析";
                cfg.level = "L3";
                cfg.parentId = "n49_航班定价";
            }
            else if (pageId.StartsWith("n55"))
            {
                cfg.title = "季节性日历";
                cfg.level = "L2";
                cfg.parentId = "n14_市场销售部";
            }
            else if (pageId.StartsWith("n56"))
            {
                cfg.title = "航季对比详情";
                cfg.level = "L3";
                cfg.parentId = "n55_季节性日历";
            }
            else if (pageId.StartsWith("n57"))
            {
                cfg.title = "旺季策略配置器";
                cfg.level = "L3";
                cfg.parentId = "n55_季节性日历";
            }
            else if (pageId.StartsWith("n58"))
            {
                cfg.title = "季节性历史趋势";
                cfg.level = "L3";
                cfg.parentId = "n55_季节性日历";
            }
            else if (pageId.StartsWith("n59"))
            {
                cfg.title = "旺季影响预测";
                cfg.level = "L4";
                cfg.parentId = "n55_季节性日历";
            }
            else if (pageId.StartsWith("n33"))
            {
                cfg.title = "FFP 常旅客管理";
                cfg.level = "L3";
                cfg.parentId = "n14_市场销售部";
            }
            else if (pageId.StartsWith("n34"))
            {
                cfg.title = "产品工坊";
                cfg.level = "L3";
                cfg.parentId = "n14_市场销售部";
            }
            else if (pageId.StartsWith("n60"))
            {
                cfg.title = "竞争雷达";
                cfg.level = "L2";
                cfg.parentId = "n14_市场销售部";
            }
            else if (pageId.StartsWith("n61"))
            {
                cfg.title = "竞对详情";
                cfg.level = "L3";
                cfg.parentId = "n60_竞争雷达";
            }
            else if (pageId.StartsWith("n62"))
            {
                cfg.title = "竞争 · 航线网络对标";
                cfg.level = "L3";
                cfg.parentId = "n60_竞争雷达";
            }
            else if (pageId.StartsWith("n63"))
            {
                cfg.title = "竞争 · 产品服务对标";
                cfg.level = "L3";
                cfg.parentId = "n60_竞争雷达";
            }
            else if (pageId.StartsWith("n64"))
            {
                cfg.title = "竞争 · 运营成本对标";
                cfg.level = "L3";
                cfg.parentId = "n60_竞争雷达";
            }
            else if (pageId.StartsWith("n65"))
            {
                cfg.title = "竞争 · 战略资本对标";
                cfg.level = "L3";
                cfg.parentId = "n60_竞争雷达";
            }
            else if (pageId.StartsWith("n67"))
            {
                cfg.title = "大客户合同";
                cfg.level = "L2";
                cfg.parentId = "n14_市场销售部";
            }
            else if (pageId.StartsWith("n68"))
            {
                cfg.title = "渠道预算";
                cfg.level = "L2";
                cfg.parentId = "n14_市场销售部";
            }
            else if (pageId.StartsWith("n69"))
            {
                cfg.title = "分销渠道管理";
                cfg.level = "L3";
                cfg.parentId = "n68_渠道预算";
            }
            else if (pageId.StartsWith("n70"))
            {
                cfg.title = "直销渠道管理";
                cfg.level = "L3";
                cfg.parentId = "n68_渠道预算";
            }
            else if (pageId.StartsWith("n71"))
            {
                cfg.title = "渠道ROI分析";
                cfg.level = "L3";
                cfg.parentId = "n68_渠道预算";
            }
            else if (pageId.StartsWith("n72"))
            {
                cfg.title = "品牌危机公关";
                cfg.level = "L2";
                cfg.parentId = "n14_市场销售部";
            }
            else if (pageId.StartsWith("n73"))
            {
                cfg.title = "舆情监测中心";
                cfg.level = "L3";
                cfg.parentId = "n72_品牌危机公关";
            }
            else if (pageId.StartsWith("n74"))
            {
                cfg.title = "危机响应指挥部";
                cfg.level = "L3";
                cfg.parentId = "n72_品牌危机公关";
            }
            else if (pageId.StartsWith("n75"))
            {
                cfg.title = "品牌修复计划";
                cfg.level = "L3";
                cfg.parentId = "n72_品牌危机公关";
            }
            else return;

            cfg.meta = "MKT runtime";
            _router.Register(cfg);
        }

        void EnsureStrategyConfig(string pageId)
        {
            if (_router == null || string.IsNullOrEmpty(pageId)) return;
            if (_router.TryGet(pageId, out var existing) && existing != null) return;

            var cfg = ScriptableObject.CreateInstance<UIConfig>();
            cfg.pageId = pageId;
            cfg.tabGroup = "stg";
            cfg.badge = "Phase2";
            if (pageId.StartsWith("n76"))
            {
                cfg.title = "机队与运力规划";
                cfg.level = "L2";
                cfg.parentId = "n16_战略发展部";
            }
            else if (pageId.StartsWith("n77"))
            {
                // n77 航权与时刻槽：已从「愿景」移入「航线」模块（NetworkPanelUI 内嵌页签），
                // 导航归属与返回栈都要跟 n12 走。
                cfg.title = "航权与时刻槽";
                cfg.level = "L2";
                cfg.parentId = "n12_航线网络";
                cfg.tabGroup = "network";
            }
            else if (pageId.StartsWith("n78"))
            {
                cfg.title = "联盟与战略合作";
                cfg.level = "L2";
                cfg.parentId = "n16_战略发展部";
            }
            else if (pageId.StartsWith("n79"))
            {
                cfg.title = "战略投资与并购";
                cfg.level = "L2";
                cfg.parentId = "n16_战略发展部";
            }
            else if (pageId.StartsWith("n80"))
            {
                cfg.title = "战略研究与预案";
                cfg.level = "L2";
                cfg.parentId = "n16_战略发展部";
            }
            else if (pageId.StartsWith("n35"))
            {
                cfg.title = "新航线 RPM 评估台";
                cfg.level = "L3";
                cfg.parentId = "n16_战略发展部";
            }
            else if (pageId.StartsWith("n102"))
            {
                cfg.title = "谈判弹窗";
                cfg.level = "L4";
                cfg.parentId = "n78_联盟与战略合作";
            }
            else if (pageId.StartsWith("n108"))
            {
                cfg.title = "临空商业 · 非航收入";
                cfg.level = "L2";
                cfg.parentId = "n16_战略发展部";
            }
            else return;

            cfg.meta = pageId.StartsWith("n77") ? "network runtime" : "STG runtime";
            _router.Register(cfg);
        }

        void EnsureMaintenanceConfig(string pageId)
        {
            if (_router == null || string.IsNullOrEmpty(pageId)) return;
            if (_router.TryGet(pageId, out var existing) && existing != null) return;

            var cfg = ScriptableObject.CreateInstance<UIConfig>();
            cfg.pageId = pageId;
            cfg.tabGroup = "mnt";
            cfg.badge = "Phase2";
            if (pageId.StartsWith("n81"))
            {
                cfg.title = "航线与定检维修";
                cfg.level = "L2";
                cfg.parentId = "n17_机务工程部";
            }
            else if (pageId.StartsWith("n82"))
            {
                cfg.title = "工程技术与计划";
                cfg.level = "L2";
                cfg.parentId = "n17_机务工程部";
            }
            else if (pageId.StartsWith("n83"))
            {
                cfg.title = "航材与供应链";
                cfg.level = "L2";
                cfg.parentId = "n17_机务工程部";
            }
            else if (pageId.StartsWith("n84"))
            {
                cfg.title = "安全质量与适航";
                cfg.level = "L2";
                cfg.parentId = "n17_机务工程部";
            }
            else if (pageId.StartsWith("n85"))
            {
                cfg.title = "维修培训与资质";
                cfg.level = "L2";
                cfg.parentId = "n17_机务工程部";
            }
            else if (pageId.StartsWith("n105"))
            {
                cfg.title = "订单确认";
                cfg.level = "L5";
                cfg.parentId = "n83_航材与供应链";
            }
            else return;

            cfg.meta = "MNT runtime";
            _router.Register(cfg);
        }

        void EnsureHrConfig(string pageId)
        {
            if (_router == null || string.IsNullOrEmpty(pageId)) return;
            if (_router.TryGet(pageId, out var existing) && existing != null) return;

            var cfg = ScriptableObject.CreateInstance<UIConfig>();
            cfg.pageId = pageId;
            cfg.tabGroup = "hr";
            cfg.badge = "Phase2";
            if (pageId.StartsWith("n86"))
            {
                cfg.title = "人力职能";
                cfg.level = "L2";
                cfg.parentId = "n18_人力资源部";
            }
            else if (pageId.StartsWith("n87"))
            {
                cfg.title = "培训";
                cfg.level = "L1";
                cfg.parentId = "n0_主界面框架_常驻";
            }
            else if (pageId.StartsWith("n88"))
            {
                cfg.title = "绩效与薪酬管理";
                cfg.level = "L2";
                cfg.parentId = "n18_人力资源部";
            }
            else if (pageId.StartsWith("n89"))
            {
                cfg.title = "员工关系与企业文化";
                cfg.level = "L2";
                cfg.parentId = "n18_人力资源部";
            }
            else if (pageId.StartsWith("n90"))
            {
                cfg.title = "合规与风险管理";
                cfg.level = "L2";
                cfg.parentId = "n18_人力资源部";
            }
            else if (pageId.StartsWith("n37"))
            {
                cfg.title = "工会谈判桌";
                cfg.level = "L3";
                cfg.parentId = "n18_人力资源部";
            }
            else if (pageId.StartsWith("n91"))
            {
                cfg.title = "人才档案与继任";
                cfg.level = "L3";
                cfg.parentId = "n18_人力资源部";
            }
            else if (pageId.StartsWith("n114"))
            {
                cfg.title = "员工信息";
                cfg.level = "L3";
                cfg.parentId = "n18_人力资源部";
            }
            else if (pageId.StartsWith("n113"))
            {
                cfg.title = "部门信息";
                cfg.level = "L2";
                cfg.parentId = "n18_人力资源部";
            }
            else if (pageId.StartsWith("n112"))
            {
                cfg.title = "职业介绍所";
                cfg.level = "L2";
                cfg.parentId = "n18_人力资源部";
            }
            // n99「招聘策略弹窗」已并入 n112（同一页完成），不再注册独立节点。
            else return;

            cfg.meta = "HR runtime";
            _router.Register(cfg);
        }

        void EnsureSafetyConfig(string pageId)
        {
            if (_router == null || string.IsNullOrEmpty(pageId)) return;
            if (_router.TryGet(pageId, out var existing) && existing != null) return;

            var cfg = ScriptableObject.CreateInstance<UIConfig>();
            cfg.pageId = pageId;
            cfg.tabGroup = "saf";
            cfg.badge = "Phase2";
            if (pageId.StartsWith("n92"))
            {
                cfg.title = "SMS与风险管理";
                cfg.level = "L2";
                cfg.parentId = "n17_机务工程部";
            }
            else if (pageId.StartsWith("n93"))
            {
                cfg.title = "安全监察与审核";
                cfg.level = "L2";
                cfg.parentId = "n17_机务工程部";
            }
            else if (pageId.StartsWith("n94"))
            {
                cfg.title = "事件调查与分析";
                cfg.level = "L2";
                cfg.parentId = "n17_机务工程部";
            }
            else if (pageId.StartsWith("n95"))
            {
                cfg.title = "局方与法规事务";
                cfg.level = "L2";
                cfg.parentId = "n17_机务工程部";
            }
            else if (pageId.StartsWith("n96"))
            {
                cfg.title = "安全文化与培训";
                cfg.level = "L2";
                cfg.parentId = "n87_培训与发展";
            }
            else if (pageId.StartsWith("n100"))
            {
                cfg.title = "检查应对弹窗";
                cfg.level = "L4";
                cfg.parentId = "n95_局方与法规事务";
            }
            else if (pageId.StartsWith("n101"))
            {
                cfg.title = "策略切换确认弹窗";
                cfg.level = "L4";
                cfg.parentId = "n96_安全文化与培训";
            }
            else return;

            cfg.meta = "SAF runtime";
            _router.Register(cfg);
        }

        void OnDestroy()
        {
            if (_shell != null)
            {
                _shell.TabSelected -= OnTabSelected;
                _shell.BackClicked -= GoBack;
                _shell.ForwardClicked -= GoForward;
            }
        }

        void RecordHistory(string pageId)
        {
            if (_historyApplying) return;
            _history.Record(pageId);
            RefreshHistoryButtons();
        }

        void RefreshHistoryButtons()
        {
            bool overlayBack = _router != null && _router.StackDepth > 0
                               && !IsShellTab(_router.CurrentPageId);
            _shell?.SetHistoryEnabled(_history.CanBack || overlayBack, _history.CanForward);
        }

        void GoBack()
        {
            // 栈顶若是底栏 L1，不要 Pop（会清空内容区变空白），按浏览历史回上一页。
            if (_router != null && _router.StackDepth > 0 && !IsShellTab(_router.CurrentPageId))
            {
                _router.Back();
                if (_router.StackDepth == 0 || IsShellTab(_router.CurrentPageId))
                    ModuleFrame.ExitSubpage();
                RefreshHistoryButtons();
                return;
            }
            if (!_history.MoveBack()) return;
            ApplyHistory(_history.Current);
        }

        void GoForward()
        {
            if (!_history.MoveForward()) return;
            ApplyHistory(_history.Current);
        }

        void ApplyHistory(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return;
            _historyApplying = true;
            try
            {
                if (IsShellTab(pageId))
                    SelectTab(pageId);
                else
                    OpenSubPage(pageId);
            }
            finally
            {
                _historyApplying = false;
                RefreshHistoryButtons();
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

        static bool IsMapHomePage(string pageId) =>
            !string.IsNullOrEmpty(pageId) &&
            (pageId.StartsWith("n6_", System.StringComparison.Ordinal)
             || pageId.Contains("主页") || pageId.Contains("Dashboard"));

        /// <summary>顶栏 / 底栏始终压在地图与夜幕之上。</summary>
        void RaiseChrome()
        {
            if (_overlayHost != null) _overlayHost.SetAsLastSibling();
            if (_statusBar != null) _statusBar.transform.SetAsLastSibling();
            if (_shell != null) _shell.transform.SetAsLastSibling();
            // 顶栏只在玩法态上浮：向导期它是隐藏的（见 ApplyChromeVisibility），
            // 一旦在此处 SetAsLastSibling，它就会压到 SetupOverlay 之上 —— 那正是
            // 「开始界面顶栏浮在菜单上」这个现象的成因（叠加层只在创建那一刻 SetAsLastSibling 过一次）。
            if (_gameplayReady && _topBar != null) _topBar.SetAsLastSibling();
            FindFirstObjectByType<AdvisorModalUI>()?.BringToFront();
        }

        /// <summary>主页地图全幅：ContentHost 贴左右屏幕边、上到顶栏下缘、下到状态栏上缘（无四周留白）；其他页恢复内边距。</summary>
        void ApplyContentHostEdgeToEdge(bool edge)
        {
            float bottom = edge
                ? StatusBarBottomY + StatusBarView.BarHeight + 4f
                : ContentBottomY;
            float top = edge ? -UITheme.TopBarHeight : -UITheme.TopBarHeight - 12f;
            float x = edge ? 0f : 20f;
            if (_contentHost != null)
            {
                _contentHost.offsetMin = new Vector2(edge ? 0f : x, bottom);
                _contentHost.offsetMax = new Vector2(edge ? 0f : -x, top);
            }
            if (_overlayHost != null)
            {
                _overlayHost.offsetMin = new Vector2(0f, StatusBarBottomY + StatusBarView.BarHeight + 4f);
                _overlayHost.offsetMax = new Vector2(0f, -UITheme.TopBarHeight);
            }
        }

        void OnRouterNavigated(string pageId)
        {
            string vis = pageId;
            if (_router != null && IsSettingsPage(_router.CurrentPageId))
                vis = _router.PageUnderTop ?? _history.Current;
            ApplyContentHostEdgeToEdge(IsMapHomePage(vis));
            RaiseChrome();
        }

        void OnRouterClosed(string pageId)
        {
            if (IsSettingsPage(pageId) && _contentHost != null)
                _router?.UseShellRoot(_contentHost);
            ApplyContentHostEdgeToEdge(IsMapHomePage(_router != null ? _router.CurrentPageId : null));
            if (!_historyApplying && _history.Current == pageId)
                _history.MoveBack();
            if (_router == null || _router.StackDepth == 0 || IsShellTab(_router.CurrentPageId))
                ModuleFrame.ExitSubpage();
            RefreshHistoryButtons();
        }

        void ReparentToOverlayHost(GameObject go)
        {
            if (go == null || _overlayHost == null) return;
            go.transform.SetParent(_overlayHost, false);
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
            go.transform.SetAsLastSibling();
            RaiseChrome();
        }

        void ReparentToContentHost(GameObject go)
        {
            if (go == null || _contentHost == null) return;
            go.transform.SetParent(_contentHost, false);
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
            go.transform.SetAsLastSibling();
            if (go.name != null && go.name.StartsWith("n40"))
                return;
            var img = go.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
            {
                img.color = UITheme.Panel;
                img.raycastTarget = true;
            }
        }

        static bool IsSettingsPage(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return false;
            return pageId.StartsWith("n40") || (pageId.Contains("设置") && !pageId.Contains("外部"));
        }

        /// <summary>全局叠层页：不得嵌进部门 ModuleFrame，也不得留在已关闭的内容层之外。</summary>
        static bool IsGlobalOverlayPage(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return false;
            return pageId.StartsWith("n40") || pageId.Contains("设置")
                || pageId.StartsWith("n115") || pageId.Contains("外部信息源")
                || pageId.StartsWith("n116") || pageId.Contains("行情源")
                || pageId.StartsWith("n110") || pageId.Contains("CEO资料")
                || pageId.StartsWith("n111") || pageId.Contains("CEO合同");
        }

        static Transform FindActiveModulePanel()
        {
            Component[] mods =
            {
                Object.FindFirstObjectByType<NetworkPanelUI>(),
                Object.FindFirstObjectByType<OpsPanelUI>(),
                Object.FindFirstObjectByType<FleetUI>(),
                Object.FindFirstObjectByType<MarketPanelUI>(),
                Object.FindFirstObjectByType<FinancePanelUI>(),
                Object.FindFirstObjectByType<HrPanelUI>(),
                Object.FindFirstObjectByType<StrategyPanelUI>(),
                Object.FindFirstObjectByType<MaintenancePanelUI>(),
                Object.FindFirstObjectByType<SafetyPanelUI>(),
                Object.FindFirstObjectByType<InboxPanelUI>()
            };
            for (int i = 0; i < mods.Length; i++)
            {
                var c = mods[i] as MonoBehaviour;
                if (c != null && c.isActiveAndEnabled)
                    return c.transform;
            }
            return null;
        }

        void OnTabSelected(string pageId)
        {
            if (RejectIfNotGameplayReady("切换页签 " + pageId)) return;

            if (!string.IsNullOrEmpty(pageId) && pageId.StartsWith("n87", System.StringComparison.Ordinal))
                EnsureHrConfig(pageId);

            // P3 功能闸门：七大部门按财年渐进解锁（第1财年 OPS/MKT/MNT，第2财年 FIN/HR，第3财年 STG/SAF）
            var gsGate = GameState.Instance;
            if (gsGate != null && !ProgressionGate.IsDeptUnlocked(pageId, gsGate.year))
            {
                int unlockYear = ProgressionGate.UnlockYearForDept(pageId);
                _bootstrap?.ShowErrorPopup(
                    $"该部门尚未解锁：当前为「{ProgressionGate.StageName(gsGate.year)}」阶段，" +
                    $"此功能需 {ProgressionGate.LockHint(unlockYear)}。");
                return;
            }

            // GDB 38：匿名遥测（部门访问热度）
            TelemetryManager.OnDeptVisited(pageId);

            _shell?.Select(pageId, invoke: false);
            _bootstrap?.HideAdvisorPanel();
            ModuleFrame.ReleaseHost();
            if (_contentHost != null)
                _router?.UseShellRoot(_contentHost);

            int legacy = MVPBootstrap.MapShellPageToLegacyNav(pageId);
            if (legacy >= 0)
            {
                if (_contentHost != null) _contentHost.gameObject.SetActive(false);
                _router?.ClearStack(destroyInstances: false);
                _bootstrap?.HandleShellNavigation(pageId);
                RecordHistory(pageId);
                RaiseChrome();
                return;
            }

            _bootstrap?.HandleShellNavigation(pageId);
            if (_contentHost != null) _contentHost.gameObject.SetActive(true);
            if (_router == null) return;
            bool ok = _router.Navigate(pageId, replaceCurrent: true);
            if (!ok)
                Debug.LogWarning($"[Phase1] Navigate failed: {pageId}");
            RecordHistory(pageId);
            RaiseChrome();
        }

        static GameObject LoadShellPrefab()
        {
            var configs = Resources.LoadAll<UIConfig>(UIPrefabCatalog.ResourcesConfigFolder);
            if (configs != null)
            {
                for (int i = 0; i < configs.Length; i++)
                {
                    if (configs[i] != null && configs[i].pageId == "n0_主界面框架_常驻" && configs[i].prefab != null)
                        return configs[i].prefab;
                }
            }
            return null;
        }
    }
}
