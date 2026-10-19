using System.Collections;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.AI;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace AirlineCEO.UI
{
    /// <summary>
    /// Phase 3～4：深色航空驾驶舱 HUD + TMP 中文字体。
    /// </summary>
    public class MVPBootstrap : MonoBehaviour
    {
        private Button _openRouteBtn;

        private TextMeshProUGUI _companyText;
        private TextMeshProUGUI _companySubText;   // 顶栏副标题：英文名 + CEO
        private TextMeshProUGUI _logoText;          // LOGO 圆内的 IATA 码
        private TextMeshProUGUI _localTimeText;     // 顶栏右上：本地日期时间
        private TextMeshProUGUI _utcTimeText;       // 顶栏右上：UTC 时间 + 游戏财季
        private TMP_InputField _searchInput;        // 顶栏搜索框（机场定位）
        private TextMeshProUGUI _popupText;
        private GameObject _popupRoot;
        private GameObject _progressRoot;
        private Image _progressFill;
        private TextMeshProUGUI _progressLabel;
        private GameObject _settleRoot;
        private TextMeshProUGUI _settleText;
        private TMP_InputField _chatInput;
        private TextMeshProUGUI _chatOutput;
        private Button _executeAdviceBtn;
        private TextMeshProUGUI _executeHintText;
        private Transform _routeCardsRoot;
        private readonly List<GameObject> _routeCardInstances = new List<GameObject>();
        private AdvisorResponse _pendingAdvice;
        private bool _flightInProgress;

        private OpsPanelUI _opsPanelUi;
        private OpenRouteModalUI _openRouteModal;
        private OpenRouteDecisionFlow _openRouteFlow;
        private GameObject _panelOps;
        private GameObject _panelMarket;
        private GameObject _panelBoard;
        private GameObject _panelHr;
        private GameObject _panelSafety;
        private GameObject _panelFinance;
        private GameObject _panelFleet;
        private GameObject _panelAi;
        private GameObject _panelStrategy;
        private GameObject _panelMaintenance;
        private BoardTargetUI _boardTargetUi;
        private HrPanelUI _hrPanelUi;
        private FinancePanelUI _financeUi;
        private FleetUI _fleetUi;
        private MarketPanelUI _marketUi;
        private StrategyPanelUI _strategyUi;
        private MaintenancePanelUI _maintenanceUi;
        private FleetPurchaseUI _fleetPurchaseUi;
        private HedgeTutorialUI _hedgeTutorialUi;
        private EventLogUI _eventLogUi;
        private SafetyPanelUI _safetyPanelUi;
        private VoteUI _voteUi;
        private AOGUI _aogUi;
        private ExpansionProposalUI _expansionUi;
        private AirlineCEO.UI.Shell.AdvisorModalUI _advisorModal;
        private QuarterResult _pendingBoardAnnounceResult;
        private int _activeNav = -1;

        /// <summary>底栏 Shell 切换时由 PrefabNavIntegration 调用。</summary>
        public void HandleShellNavigation(string pageId)
        {
            int legacy = MapShellPageToLegacyNav(pageId);
            if (legacy >= 0)
            {
                ShowNavPanel(legacy);
                if (pageId.StartsWith("n19_", System.StringComparison.Ordinal))
                    _maintenanceUi?.ShowSafetyTab();
                if (pageId.StartsWith("n5_", System.StringComparison.Ordinal))
                    _strategyUi?.ShowTargetsTab();
                return;
            }

            // 无完整玩法页：隐藏旧中心面板，交给 Prefab 占位
            HideAllLegacyCenterPanels();
            _activeNav = -1;
        }

        public static int MapShellPageToLegacyNav(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return -1;
            if (pageId.StartsWith("n13_", System.StringComparison.Ordinal) || pageId == "n13_运营部") return 0;
            if (pageId.StartsWith("n5_", System.StringComparison.Ordinal) || pageId == "n5_年度目标确认") return 8;
            if (pageId.StartsWith("n14_", System.StringComparison.Ordinal) || pageId.Contains("市场")) return 1;
            if (pageId.StartsWith("n20_", System.StringComparison.Ordinal) || pageId.Contains("董事会")) return 2;
            if (pageId.StartsWith("n18_", System.StringComparison.Ordinal) || pageId.Contains("人力") || pageId.Contains("人员")) return 3;
            if (pageId.StartsWith("n19_", System.StringComparison.Ordinal)) return 9;
            if (pageId.StartsWith("n15_", System.StringComparison.Ordinal) || pageId.Contains("财务")) return 5;
            if (pageId.StartsWith("n9_", System.StringComparison.Ordinal) || pageId == "n9_机队") return 6;
            if (pageId.StartsWith("n16_", System.StringComparison.Ordinal) || pageId == "n16_战略发展部" || pageId.Contains("愿景")) return 8;
            if (pageId.StartsWith("n17_", System.StringComparison.Ordinal) || pageId == "n17_机务工程部" || pageId.Contains("维护")) return 9;
            // n12 航线网络走 Prefab / ShellRuntime，不再误映射到运营中心
            return -1;
        }

        public void HideAllLegacyCenterPanels()
        {
            var panels = new[] { _panelOps, _panelMarket, _panelBoard, _panelHr, _panelSafety, _panelFinance, _panelFleet, _panelAi, _panelStrategy, _panelMaintenance };
            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] != null) panels[i].SetActive(false);
            }
        }

        private void Awake()
        {
            EnsureCoreSystems();
            AppSettings.Load();
            ModManager.LoadAll();
            if (gameObject.GetComponent<KeyboardNav>() == null)
                gameObject.AddComponent<KeyboardNav>();
            EnsureEventSystem();
            ChineseFontProvider.WarmupSampleChinese();
            BuildUI();
            if (GetComponent<PrefabNavIntegration>() == null)
                gameObject.AddComponent<PrefabNavIntegration>();
            // Phase 1.4：不再默认进市场；由 SetupFlow 闸门后进主页
            HideAllLegacyCenterPanels();
            RefreshHud();

            if (GameState.Instance != null)
                GameState.Instance.OnStateChanged += RefreshHud;

            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnQuarterAdvanced -= OnQuarterAdvancedUi;
                TimeManager.Instance.OnQuarterAdvanced += OnQuarterAdvancedUi;
                TimeManager.Instance.OnYearEnded -= OnYearEndedUi;
                TimeManager.Instance.OnYearEnded += OnYearEndedUi;
                TimeManager.Instance.OnClockTick -= RefreshClock;
                TimeManager.Instance.OnClockTick += RefreshClock;
            }

            StartCoroutine(ClockLoop());
            RefreshClock();

            var setup = GetComponent<AirlineCEO.UI.Setup.SetupFlowController>()
                        ?? gameObject.AddComponent<AirlineCEO.UI.Setup.SetupFlowController>();
            setup.Begin();
        }

        /// <summary>开局流完成后：刷新顶栏公司名，显示壳层，启动游戏时钟。</summary>
        public void OnSetupCompleted()
        {
            RefreshHud();
            // 向导结束：顾问停靠区上线（图标常驻右下角），但先保持面板收起，
            // 由 TryStartAdvisorTutorial 决定是否自动展开。
            _advisorModal?.SetDockVisible(true);
            _advisorModal?.Hide();
            // 顾问使用独立 sortingOrder，不被后打开的内容页盖住。
            _advisorModal?.BringToFront();
            TimeManager.SimulationEnabled = true;
            TimeManager.Instance?.BeginPlaySession();
            RefreshClock();
            // 过渡业务（开局三架飞机的盈利设计）：铺常驻报价 + 投递运营部建议信
            AirlineCEO.Logic.TransitionBusinessLogic.Bootstrap(GameState.Instance);
            GameOverOverlay.Ensure(GetComponentInParent<Canvas>());
            GameOverOverlay.ShowIfNeeded();
        }

        private void OnDestroy()
        {
            if (GameState.Instance != null)
                GameState.Instance.OnStateChanged -= RefreshHud;
            GameSpeed.OnLevelChanged -= OnSpeedChanged;
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnQuarterAdvanced -= OnQuarterAdvancedUi;
                TimeManager.Instance.OnYearEnded -= OnYearEndedUi;
                TimeManager.Instance.OnClockTick -= RefreshClock;
            }
            if (AOGManager.Instance != null)
                AOGManager.Instance.OnAOGResolved -= OnAOGResolved;
            if (ExpansionProposalManager.Instance != null)
                ExpansionProposalManager.Instance.OnProposalResolved -= OnExpansionResolved;
            if (DepartmentManager.Instance != null)
                DepartmentManager.Instance.OnResignation -= OnDepartmentResignation;
        }

        bool _pausedForSettle;

        void PauseForSettleIfNeeded()
        {
            if (GameSpeed.IsPaused) return; // 危机已停表，勿抢占 Resume 档
            GameSpeed.Pause();
            _pausedForSettle = true;
        }

        void ResumeAfterSettleIfNeeded()
        {
            if (!_pausedForSettle) return;
            _pausedForSettle = false;
            if (GameSpeed.IsPaused) GameSpeed.Resume();
        }

        void OnQuarterAdvancedUi(QuarterResult result)
        {
            if (result == null || !result.success) return;
            RefreshHud();
            RefreshClock();

            // 年结投票由 OnYearEndedUi 处理（此时 result.yearEnd 尚为空）
            if (!result.yearEnded && _settleRoot != null && _settleText != null)
            {
                PauseForSettleIfNeeded();
                _settleText.text = ChineseFontProvider.Sanitize(result.message ?? "");
                _settleRoot.SetActive(true);
            }

            if (result.pendingAOG)
                AOGManager.Instance?.TryTriggerAOG();
            if (result.pendingExpansionProposal)
                ExpansionProposalManager.Instance?.TryTriggerProposal();
            if (result.needsFinanceWarning && !string.IsNullOrEmpty(result.financeWarning))
                PushFinanceWarningToChat(result.financeWarning);

            TryAnnounceBoardTargets(GameState.Instance, result);

            if (result.pendingDueMail)
                GetComponent<PrefabNavIntegration>()?.SelectTab("n7_收件箱");
        }

        void OnYearEndedUi(YearEndResult yearEnd)
        {
            if (yearEnd == null) return;
            PauseForSettleIfNeeded();
            ShowVoteResult(yearEnd, "");
        }

        private void EnsureCoreSystems()
        {
            if (GameState.Instance == null)
            {
                var go = new GameObject("GameState");
                go.AddComponent<GameState>();
            }

            if (SaveManager.Instance == null)
            {
                var go = new GameObject("SaveManager");
                go.AddComponent<SaveManager>();
            }

            if (DataBase.Instance == null)
            {
                var go = new GameObject("DataBase");
                go.AddComponent<DataBase>();
            }

            if (AIChatManager.Instance == null)
            {
                var go = new GameObject("AIChatManager");
                go.AddComponent<AIChatManager>();
            }

            if (ActionExecutor.Instance == null)
            {
                var go = new GameObject("ActionExecutor");
                go.AddComponent<ActionExecutor>();
            }

            if (TimeManager.Instance == null)
            {
                var go = new GameObject("TimeManager");
                go.AddComponent<TimeManager>();
            }

            if (BoardManager.Instance == null)
            {
                var go = new GameObject("BoardManager");
                go.AddComponent<BoardManager>();
            }

            if (SafetyEventManager.Instance == null)
            {
                var go = new GameObject("SafetyEventManager");
                go.AddComponent<SafetyEventManager>();
            }

            if (RouteRightManager.Instance == null)
            {
                var go = new GameObject("RouteRightManager");
                go.AddComponent<RouteRightManager>();
            }

            if (AOGManager.Instance == null)
            {
                var go = new GameObject("AOGManager");
                go.AddComponent<AOGManager>();
            }

            if (ExpansionProposalManager.Instance == null)
            {
                var go = new GameObject("ExpansionProposalManager");
                go.AddComponent<ExpansionProposalManager>();
            }

            if (DepartmentManager.Instance == null)
            {
                var go = new GameObject("DepartmentManager");
                go.AddComponent<DepartmentManager>();
            }

            if (HedgeManager.Instance == null)
            {
                var go = new GameObject("HedgeManager");
                go.AddComponent<HedgeManager>();
            }

            if (LoanManager.Instance == null)
            {
                var go = new GameObject("LoanManager");
                go.AddComponent<LoanManager>();
            }

            if (FinanceManager.Instance == null)
            {
                var go = new GameObject("FinanceManager");
                go.AddComponent<FinanceManager>();
            }

            if (FleetManager.Instance == null)
            {
                var go = new GameObject("FleetManager");
                go.AddComponent<FleetManager>();
            }

            if (MktManager.Instance == null)
            {
                var go = new GameObject("MktManager");
                go.AddComponent<MktManager>();
            }

            if (StrategyManager.Instance == null)
            {
                var go = new GameObject("StrategyManager");
                go.AddComponent<StrategyManager>();
            }

            if (MaintenanceManager.Instance == null)
            {
                var go = new GameObject("MaintenanceManager");
                go.AddComponent<MaintenanceManager>();
            }

            if (HrManager.Instance == null)
            {
                var go = new GameObject("HrManager");
                go.AddComponent<HrManager>();
            }

            if (SafetyManager.Instance == null)
            {
                var go = new GameObject("SafetyManager");
                go.AddComponent<SafetyManager>();
            }

            if (DecisionMeetingManager.Instance == null)
            {
                var go = new GameObject("DecisionMeetingManager");
                go.AddComponent<DecisionMeetingManager>();
            }

            if (AIManager.Instance == null)
            {
                var go = new GameObject("AIManager");
                go.AddComponent<AIManager>();
            }

            if (CompetitionManager.Instance == null)
            {
                var go = new GameObject("CompetitionManager");
                go.AddComponent<CompetitionManager>();
            }

            if (RandomEventManager.Instance == null)
            {
                var go = new GameObject("RandomEventManager");
                go.AddComponent<RandomEventManager>();
            }

            if (CargoManager.Instance == null)
            {
                var go = new GameObject("CargoManager");
                go.AddComponent<CargoManager>();
            }

            if (MacroEconomyManager.Instance == null)
            {
                var go = new GameObject("MacroEconomyManager");
                go.AddComponent<MacroEconomyManager>();
            }
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        /// <summary>
        /// 驾驶舱氛围底：仅底部一条色带（底栏后方），无左右侧栏色块。
        /// </summary>
        void BuildCockpitBackdrop(Transform parent)
        {
            const float bottomH = 84f;

            var bottomBand = UIFactory.Panel(parent, "BottomStage",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0f, 0f), new Vector2(0f, bottomH)),
                UITheme.PanelDeep, rounded: false);
            bottomBand.GetComponent<Image>().raycastTarget = false;
            UIFactory.AddBorder(bottomBand, UITheme.Border, new Vector2(0f, 1.5f));
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("Cockpit_Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UITheme.SortMain;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            UIFactory.Panel(canvasGo.transform, "Background", UIFactory.Fill, UITheme.Bg, rounded: false);
            BuildCockpitBackdrop(canvasGo.transform);

            BuildTopBar(canvasGo.transform);
            BuildCenterPanels(canvasGo.transform);
            _advisorModal = AirlineCEO.UI.Shell.AdvisorModalUI.Create(canvasGo.transform);
            // 开局向导期间收起顾问停靠区（气泡图标 + 面板），避免浮在向导之上；
            // OnSetupCompleted 里再打开。图标本身是画布级的，覆盖所有页面。
            _advisorModal.SetDockVisible(false);
            BuildPopup(canvasGo.transform);
            BuildProgress(canvasGo.transform);
            BuildSettle(canvasGo.transform);

            _voteUi = VoteUI.Create(canvasGo.transform);
            _aogUi = AOGUI.Create(canvasGo.transform);
            if (AOGManager.Instance != null)
            {
                AOGManager.Instance.BindUI(_aogUi);
                AOGManager.Instance.OnAOGResolved -= OnAOGResolved;
                AOGManager.Instance.OnAOGResolved += OnAOGResolved;
            }

            _hedgeTutorialUi = HedgeTutorialUI.Create(canvasGo.transform);
            if (_financeUi != null)
                _financeUi.BindTutorial(_hedgeTutorialUi);

            _fleetPurchaseUi = FleetPurchaseUI.Create(canvasGo.transform);
            if (_fleetUi != null)
                _fleetUi.BindPurchase(_fleetPurchaseUi);

            _expansionUi = ExpansionProposalUI.Create(canvasGo.transform);
            if (ExpansionProposalManager.Instance != null)
            {
                ExpansionProposalManager.Instance.BindUI(_expansionUi);
                ExpansionProposalManager.Instance.OnProposalResolved -= OnExpansionResolved;
                ExpansionProposalManager.Instance.OnProposalResolved += OnExpansionResolved;
            }

            if (DepartmentManager.Instance != null)
            {
                DepartmentManager.Instance.OnResignation -= OnDepartmentResignation;
                DepartmentManager.Instance.OnResignation += OnDepartmentResignation;
            }
        }

        private void BuildTopBar(Transform parent)
        {
            // 深蓝渐变：上下两层
            var bar = UIFactory.Panel(parent, "TopBar",
                new UIFactory.Anchor(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -UITheme.TopBarHeight), Vector2.zero),
                UITheme.TopBarBottom, rounded: false);
            UIFactory.Panel(bar.transform, "TopGradient",
                new UIFactory.Anchor(new Vector2(0, 0.45f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                UITheme.TopBarTop, rounded: false);
            UIFactory.AddBorder(bar, UITheme.Border, new Vector2(0f, -1.5f));

            // —— 左：航司 LOGO + 中文名 / 英文名 + CEO ——（红字①②③④：LOGO/英文名/中文名/CEO名字）
            var logo = UIFactory.Panel(bar.transform, "Logo",
                new UIFactory.Anchor(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(16f, -26f), new Vector2(68f, 26f)),
                UITheme.Accent);
            var logoImg = logo.GetComponent<Image>();
            logoImg.sprite = UIFactory.CircleSprite;
            logoImg.type = Image.Type.Simple;
            _logoText = UIFactory.Label(logo.transform, "Code", "NV", 18, TextAlignmentOptions.Center,
                UIFactory.Fill, Color.white, FontStyles.Bold);

            _companyText = UIFactory.Label(bar.transform, "Company",
                "", 15, TextAlignmentOptions.BottomLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(80f, -4f), new Vector2(400f, 30f)),
                UITheme.TextPrimary, FontStyles.Bold);
            _companyText.textWrappingMode = TextWrappingModes.NoWrap;
            _companyText.overflowMode = TextOverflowModes.Truncate;

            _companySubText = UIFactory.Label(bar.transform, "CompanySub",
                "", 12, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(80f, -30f), new Vector2(400f, -6f)),
                UITheme.TextMuted);
            _companySubText.textWrappingMode = TextWrappingModes.NoWrap;
            _companySubText.overflowMode = TextOverflowModes.Truncate;

            // —— 中：搜索框（放大镜 + 机场搜索，回车定位）——（红字⑦：搜索框）
            BuildSearchBox(bar.transform);

            // —— 右：图标按钮组 + 日期时间 ——（红字⑤⑥：帮助/设置；速度三键；⑧⑨：本地/UTC 时间）
            BuildTopBarIcons(bar.transform);

            // 快讯跑马灯已移除：全球航空动态统一进收件箱（未读角标提示）
        }

        void BuildSearchBox(Transform bar)
        {
            var box = UIFactory.Panel(bar, "SearchBox",
                new UIFactory.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(-170f, -18f), new Vector2(170f, 18f)),
                new Color(0.05f, 0.10f, 0.20f, 0.92f));
            box.GetComponent<Image>().sprite = UIFactory.RoundedSprite;
            box.GetComponent<Image>().type = Image.Type.Sliced;
            UIFactory.AddBorder(box, UITheme.Border, new Vector2(1f, -1f));

            // 放大镜图标
            var icon = new GameObject("SearchIcon", typeof(RectTransform));
            icon.transform.SetParent(box.transform, false);
            var irt = icon.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(10f, 0f);
            irt.sizeDelta = new Vector2(18f, 18f);
            var iimg = icon.AddComponent<Image>();
            iimg.sprite = UIFactory.SearchSprite;
            iimg.type = Image.Type.Simple;
            iimg.preserveAspect = true;
            iimg.color = UITheme.TextMuted;
            iimg.raycastTarget = false;

            _searchInput = UIFactory.Input(box.transform, "SearchInput", "搜索机场（代码 / 名称，回车定位）…",
                new UIFactory.Anchor(new Vector2(0.06f, 0.06f), new Vector2(0.98f, 0.94f), Vector2.zero, Vector2.zero));
            _searchInput.onSubmit.AddListener(new UnityEngine.Events.UnityAction<string>(OnSearchSubmit));
        }

        void OnSearchSubmit(string query)
        {
            _searchInput.DeactivateInputField();

            // 闸门：开局（新游戏）或读档完成后才允许搜机场。
            // 顶栏（含这个 SearchBox）在向导期整体隐藏，所以正常情况下这里根本点不到 ——
            // 但这句不是冗余：回车会 `SelectTab("n6_主页_Dashboard")`，是**绕过开局向导直接进局内**的路子，
            // 一旦顶栏哪天被单独打开（或搜索框被挪出 TopBar），只有这层闸门挡得住。
            if (!AirportSearchAllowedFor(GetComponent<PrefabNavIntegration>()))
                return;

            if (AirlineCEO.Data.AirportCatalog.TryResolve(query, out var ap))
            {
                // 先回主页（同步构建地图），再定位机场
                GetComponent<PrefabNavIntegration>()?.SelectTab("n6_主页_Dashboard");
                if (MapWidget.FocusAirport(ap.code))
                    return;
            }
            ToastTop($"未找到机场：{query}");
        }

        /// <summary>
        /// 机场搜索的可用口径：`PrefabNavIntegration.GameplayReady` 为真，即开局 / 读档完成。
        /// 抽成静态以便测试直接钉住这个口径（不必把整套 MVPBootstrap 跑起来）。
        /// </summary>
        public static bool AirportSearchAllowedFor(PrefabNavIntegration nav)
            => nav != null && nav.GameplayReady;

        TextMeshProUGUI _toastTopText;
        void ToastTop(string msg)
        {
            if (_toastTopText == null) return;
            _toastTopText.text = msg ?? "";
            if (_toastFadeCo != null) StopCoroutine(_toastFadeCo);
            _toastFadeCo = StartCoroutine(FadeTopToast());
        }

        Coroutine _toastFadeCo;
        System.Collections.IEnumerator FadeTopToast()
        {
            yield return new WaitForSeconds(2.5f);
            if (_toastTopText != null) _toastTopText.text = "";
        }

        /// <summary>顶栏时间区右边距（设计像素）：保证文字不贴窗口右边缘。</summary>
        const float TopTimeRightMargin = 48f;

        /// <summary>
        /// 顶栏右侧文字定位：以「右边缘 = 栏右侧 − rightMargin、竖直中点 = 栏中线 + centerY」显式锚定。
        /// 用 pivot(1, 0.5) + anchoredPosition，与图标组同一写法，不依赖 anchorMin==anchorMax 时 offsetMax 的语义。
        /// </summary>
        static void PlaceTopRightText(TextMeshProUGUI text, float width, float height, float centerY)
        {
            if (text == null) return;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-TopTimeRightMargin, centerY);
        }

        void BuildTopBarIcons(Transform bar)
        {
            const float IconPx = 40f;
            const float Gap = 6f;
            // 右起基准：先放时间区（最右），图标组紧邻其左
            // 时间区：游戏本地时 / 游戏 UTC · 财年季日（随倍速推进）
            _localTimeText = UIFactory.Label(bar, "LocalTime", "--年--月--日  星期-  --:--", 14, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-360f, 2f), new Vector2(-16f, 30f)),
                UITheme.TextPrimary, FontStyles.Bold);
            _utcTimeText = UIFactory.Label(bar, "UtcTime", "UTC --:--", 12, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-360f, -30f), new Vector2(-16f, -4f)),
                UITheme.TextMuted);

            // 时间区定位改用「右边缘 = 栏右侧 − TopTimeRightMargin」的显式锚定（与下方图标组同一种写法）。
            // 原先只靠 offsetMax.x = -16 定右边距，实机下日期文字会顶到窗口右边缘被切掉（末位字符只剩左半），
            // 且标签框宽度不足时右对齐文字还会继续往右溢。这里把框加宽并留足边距，两种情况都能兜住。
            PlaceTopRightText(_localTimeText, 420f, 30f, 16f);
            PlaceTopRightText(_utcTimeText, 420f, 26f, -15f);

            // 搜索失败提示（挂在时间区左侧，定时淡出）
            _toastTopText = UIFactory.Label(bar, "SearchToast", "", 13, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-520f, 2f), new Vector2(-370f, 30f)),
                UITheme.Warning);
            _toastTopText.textWrappingMode = TextWrappingModes.NoWrap;
            _toastTopText.overflowMode = TextOverflowModes.Overflow;

            // 图标组：帮助 / 设置 / 1× / 2× / 3×；编辑器另加开发档「1分=1日」
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int count = 6;
#else
            int count = 5;
#endif
            float width = count * IconPx + (count - 1) * Gap;
            var icons = new GameObject("TopIcons", typeof(RectTransform));
            icons.transform.SetParent(bar, false);
            var rt = icons.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-380f, 0f); // 为完整日期时间让位
            rt.sizeDelta = new Vector2(width, IconPx);

            // 像素布局：第 i 个按钮（从左到右），左锚 + 偏移
            void IconBtn(string name, Sprite sprite, int i, UnityEngine.Events.UnityAction onClick)
            {
                float x = i * (IconPx + Gap);
                var btn = UIFactory.IconButton(icons.transform, name, sprite,
                    new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(0f, 0f),
                        new Vector2(x, 0f), new Vector2(x + IconPx, IconPx)),
                    UITheme.PanelDeep, onClick, UITheme.TextPrimary);
                _speedIconTints[name] = btn.transform.Find("Icon").GetComponent<Image>();
            }

            IconBtn("Help", UIFactory.HelpSprite, 0, () => ToggleAdvisorPanel());
            IconBtn("Settings", UIFactory.GearSprite, 1, () =>
            {
                GetComponent<PrefabNavIntegration>()?.OpenSubPage("n40_设置_存档");
            });
            IconBtn("Speed1", UIFactory.FastForwardSprite(1), 2, () => SetGameSpeed(GameSpeed.L1));
            IconBtn("Speed2", UIFactory.FastForwardSprite(2), 3, () => SetGameSpeed(GameSpeed.L2));
            IconBtn("Speed3", UIFactory.FastForwardSprite(3), 4, () => SetGameSpeed(GameSpeed.L3));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            IconBtn("SpeedDev", UIFactory.FastForwardSprite(3), 5, () => SetGameSpeed(GameSpeed.DevDay));
            var devIcon = icons.transform.Find("SpeedDev");
            if (devIcon != null)
            {
                UIFactory.Label(devIcon, "DevMark", "日", 10, TextAlignmentOptions.BottomRight,
                    UIFactory.Fill, UITheme.Warning, FontStyles.Bold);
            }
#endif

            // 选中态高亮：监听速度变化
            GameSpeed.OnLevelChanged -= OnSpeedChanged;
            GameSpeed.OnLevelChanged += OnSpeedChanged;
            OnSpeedChanged(GameSpeed.Level);
        }

        readonly Dictionary<string, Image> _speedIconTints = new Dictionary<string, Image>();

        void OnSpeedChanged(int level)
        {
            string active = "";
            if (level == GameSpeed.L1) active = "Speed1";
            else if (level == GameSpeed.L2) active = "Speed2";
            else if (level == GameSpeed.L3) active = "Speed3";
            else if (level == GameSpeed.DevDay) active = "SpeedDev";

            foreach (var kv in _speedIconTints)
            {
                if (kv.Value == null) continue;
                // 帮助/设置不参与倍速高亮
                if (kv.Key == "Help" || kv.Key == "Settings")
                {
                    kv.Value.color = UITheme.TextPrimary;
                    continue;
                }
                kv.Value.color = kv.Key == active
                    ? new Color(0.45f, 0.85f, 1f, 1f)      // 选中：亮蓝
                    : new Color(0.5f, 0.58f, 0.66f, 0.75f); // 未选：灰
            }
        }

        private void BuildCenterPanels(Transform parent)
        {
            // 全宽：无左侧导航；底部留给 Shell 底栏 + 状态栏（快讯条已移除，不留 TickerHeight）
            var center = UIFactory.Panel(parent, "CenterHost",
                new UIFactory.Anchor(new Vector2(0, 0), new Vector2(1, 1),
                    new Vector2(20, 118), new Vector2(-20, -UITheme.TopBarHeight - 12)),
                new Color(0, 0, 0, 0), rounded: false);
            center.GetComponent<Image>().raycastTarget = false;

            _panelOps = BuildOpsPanel(center.transform);
            _panelMarket = BuildMarketPanel(center.transform);
            _boardTargetUi = BoardTargetUI.Build(center.transform);
            _panelBoard = _boardTargetUi.gameObject;
            _hrPanelUi = HrPanelUI.Build(center.transform);
            _panelHr = _hrPanelUi.gameObject;
            _safetyPanelUi = SafetyPanelUI.Build(center.transform);
            _panelSafety = _safetyPanelUi.gameObject;
            _financeUi = FinancePanelUI.Build(center.transform);
            _panelFinance = _financeUi.gameObject;
            _fleetUi = FleetUI.Build(center.transform);
            _panelFleet = _fleetUi.gameObject;
            _panelAi = BuildAiHelpPanel(center.transform);
            _panelStrategy = BuildStrategyPanel(center.transform);
            _panelMaintenance = BuildMaintenancePanel(center.transform);

            _eventLogUi = EventLogUI.Build(center.transform,
                new UIFactory.Anchor(new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.88f), Vector2.zero, Vector2.zero));
            if (_eventLogUi != null)
                _eventLogUi.gameObject.SetActive(false);
        }

        private GameObject BuildOpsPanel(Transform parent)
        {
            _opsPanelUi = OpsPanelUI.Build(parent, OnOpenRouteClicked);
            _openRouteBtn = _opsPanelUi.OpenRouteButton;
            _routeCardsRoot = _opsPanelUi.RouteCardsRoot;
            _opsPanelUi.Refresh();
            return _opsPanelUi.gameObject;
        }

        private GameObject BuildMarketPanel(Transform parent)
        {
            _marketUi = MarketPanelUI.Build(parent);
            return _marketUi.gameObject;
        }

        private GameObject BuildStrategyPanel(Transform parent)
        {
            _strategyUi = StrategyPanelUI.Build(parent);
            return _strategyUi.gameObject;
        }

        private GameObject BuildMaintenancePanel(Transform parent)
        {
            _maintenanceUi = MaintenancePanelUI.Build(parent);
            return _maintenanceUi.gameObject;
        }

        private GameObject BuildAiHelpPanel(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_AI", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));

            UIFactory.Label(panel.transform, "Title", "AI 顾问 · 使用说明", 22, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.88f), new Vector2(0.8f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Label(panel.transform, "Body",
                "点右下角的对话气泡（或顶栏 ? 键）打开航空顾问面板。\n\n" +
                "新开局约 2 秒后会自动弹出开局引导（深圳—北京航线）。\n" +
                "也可随时再次打开继续或回顾引导。",
                16, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.2f), new Vector2(0.95f, 0.85f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            return panel;
        }

        /// <summary>顶栏「顾问」：开关居中顾问弹窗（开局引导 S0–S6）。</summary>
        public void ToggleAdvisorPanel()
        {
            _advisorModal?.Toggle();
        }

        /// <summary>关闭顾问弹窗。</summary>
        public void HideAdvisorPanel()
        {
            _advisorModal?.Hide();
        }

        /// <summary>开局进入主页后延迟唤起顾问引导。</summary>
        public void TryStartAdvisorTutorial(float delaySeconds = 0.5f)
        {
            _advisorModal?.TryAutoStartAfterDelay(delaySeconds);
        }

        void SetGameSpeed(int level)
        {
            if (level == GameSpeed.Paused) return; // 正式 UI 不设暂停
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            if (level == GameSpeed.DevDay) return;
#endif
            if (GameState.Instance != null)
                GameState.Instance.AddDecisionLog($"游戏速度：{GameSpeed.Label(level)}");
            GameSpeed.Set(level);
        }

        /// <summary>进入游戏 / 读档：默认 1×，不改游戏日历。</summary>
        public void ApplyStartingModeSpeed()
        {
            TimeManager.Instance?.BeginPlaySession();
        }

        // 快讯跑马灯已移除：全球航空动态进收件箱，未读数由底栏收件箱 Tab 角标提示。
        // 每秒刷新顶栏时钟（游戏本地 + 游戏 UTC）。
        System.Collections.IEnumerator ClockLoop()
        {
            var wait = new WaitForSecondsRealtime(0.5f);
            while (true)
            {
                RefreshClock();
                yield return wait;
            }
        }

        void RefreshClock()
        {
            if (_localTimeText == null || _utcTimeText == null) return;
            var gs = GameState.Instance;
            if (gs == null)
            {
                _localTimeText.text = "--:--";
                _utcTimeText.text = "UTC --:--";
                return;
            }

            _localTimeText.text = gs.GameLocalDateTimeLabel;
            _utcTimeText.text = $"UTC {gs.GameUtcClockLabel}";
            MapWidget.RefreshLivePlanes();
        }

        private void ShowNavPanel(int index)
        {
            int prev = _activeNav;
            _activeNav = index;

            // Phase 14：页签切换淡入动效（先隐藏旧 → 显示新 → 淡入）
            var panels = new[] { _panelOps, _panelMarket, _panelBoard, _panelHr, _panelSafety, _panelFinance, _panelFleet, _panelAi, _panelStrategy, _panelMaintenance };
            if (prev >= 0 && prev < panels.Length && panels[prev] != null)
            {
                StartCoroutine(CrossfadeOut(panels[prev], () =>
                {
                    if (index >= 0 && index < panels.Length && panels[index] != null)
                    {
                        panels[index].SetActive(true);
                        StartCoroutine(CrossfadeIn(panels[index]));
                    }
                }));
            }
            else if (index >= 0 && index < panels.Length && panels[index] != null)
            {
                panels[index].SetActive(true);
                StartCoroutine(CrossfadeIn(panels[index]));
            }

            // 隐藏其他面板
            for (int i = 0; i < panels.Length; i++)
            {
                if (i != index && i != prev && panels[i] != null)
                    panels[i].SetActive(false);
            }

            // 进人事时收拢为图标（面板向右下角收起，气泡图标保留），避免挡住列表
            if (index == 3)
                _advisorModal?.Hide();

            if (index == 2 && _boardTargetUi != null)
                _boardTargetUi.Refresh();
            if (index == 3 && _hrPanelUi != null)
                _hrPanelUi.OnOpened();
            if (index == 4 && _safetyPanelUi != null)
                _safetyPanelUi.OnOpened();
            if (index == 5 && _financeUi != null)
                _financeUi.OnOpened();
            if (index == 6 && _fleetUi != null)
                _fleetUi.Refresh();
            if (index == 0 && _opsPanelUi != null)
                _opsPanelUi.Refresh();
            if (index == 1 && _marketUi != null)
                _marketUi.OnOpened();
            if (index == 8 && _strategyUi != null)
                _strategyUi.OnOpened();
            if (index == 9 && _maintenanceUi != null)
                _maintenanceUi.OnOpened();
        }

        private void BuildPopup(Transform parent)
        {
            _popupRoot = UIFactory.Panel(parent, "Popup",
                new UIFactory.Anchor(new Vector2(0.20f, 0.14f), new Vector2(0.80f, 0.86f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.1f, 0.18f, 0.97f)).gameObject;
            UIFactory.AddBorder(_popupRoot, UITheme.Accent, new Vector2(2f, -2f));

            _popupText = UIFactory.ScrollableLabel(_popupRoot.transform, "PopupText",
                new UIFactory.Anchor(new Vector2(0.04f, 0.16f), new Vector2(0.96f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, 15, TextAlignmentOptions.TopLeft);

            UIFactory.Button(_popupRoot.transform, "PopupClose", "关闭",
                new UIFactory.Anchor(new Vector2(0.32f, 0.03f), new Vector2(0.68f, 0.13f), Vector2.zero, Vector2.zero),
                UIFactory.ModalButton, () => _popupRoot.SetActive(false), 16);
            _popupRoot.SetActive(false);
        }

        private void BuildProgress(Transform parent)
        {
            _progressRoot = UIFactory.Panel(parent, "FlightProgress",
                new UIFactory.Anchor(new Vector2(0.3f, 0.42f), new Vector2(0.7f, 0.58f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.1f, 0.16f, 0.96f)).gameObject;
            UIFactory.AddBorder(_progressRoot, UITheme.Accent, new Vector2(1.5f, -1.5f));

            _progressLabel = UIFactory.Label(_progressRoot.transform, "ProgressLabel", "起飞中…", 20, TextAlignmentOptions.Top,
                new UIFactory.Anchor(new Vector2(0, 0.55f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            var track = UIFactory.Panel(_progressRoot.transform, "Track",
                new UIFactory.Anchor(new Vector2(0.1f, 0.22f), new Vector2(0.9f, 0.45f), Vector2.zero, Vector2.zero),
                new Color(0.06f, 0.1f, 0.16f));
            _progressFill = UIFactory.Panel(track.transform, "Fill", UIFactory.Fill, UITheme.Accent).GetComponent<Image>();
            _progressFill.sprite = UIFactory.RoundedSprite;
            _progressFill.type = Image.Type.Filled;
            _progressFill.fillMethod = Image.FillMethod.Horizontal;
            _progressFill.fillAmount = 0f;

            _progressRoot.SetActive(false);
        }

        private void BuildSettle(Transform parent)
        {
            _settleRoot = UIFactory.Panel(parent, "SettlePopup",
                new UIFactory.Anchor(new Vector2(0.24f, 0.22f), new Vector2(0.76f, 0.78f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.14f, 0.12f, 0.97f)).gameObject;
            UIFactory.AddBorder(_settleRoot, UITheme.Success, new Vector2(2f, -2f));

            _settleText = UIFactory.ScrollableLabel(_settleRoot.transform, "SettleText",
                new UIFactory.Anchor(new Vector2(0.05f, 0.20f), new Vector2(0.95f, 0.94f), Vector2.zero, Vector2.zero),
                UITheme.Success, 18, TextAlignmentOptions.Top);

            UIFactory.Button(_settleRoot.transform, "SettleClose", "确认",
                new UIFactory.Anchor(new Vector2(0.32f, 0.04f), new Vector2(0.68f, 0.16f), Vector2.zero, Vector2.zero),
                UIFactory.ModalButton, () =>
                {
                    _settleRoot.SetActive(false);
                    ResumeAfterSettleIfNeeded();
                }, 16);

            _settleRoot.SetActive(false);
        }

        private void RefreshHud()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            // 顶栏：公司中文名（主）/ 英文名 + CEO（副）/ LOGO IATA 码
            if (_companyText != null)
            {
                string co = string.IsNullOrEmpty(gs.companyNameZh) ? "天际航空" : gs.companyNameZh;
                _companyText.text = ChineseFontProvider.CompanyPlanePrefix + " " + co;
            }
            if (_companySubText != null)
            {
                string en = string.IsNullOrEmpty(gs.companyNameEn) ? "" : gs.companyNameEn + " · ";
                string ceo = string.IsNullOrEmpty(gs.ceoDisplayName) ? "CEO" : gs.ceoDisplayName;
                _companySubText.text = $"{en}CEO {ceo}";
            }
            if (_logoText != null)
                _logoText.text = string.IsNullOrEmpty(gs.companyIata) ? "NV" : gs.companyIata;
            RefreshClock();

            RebuildRouteCards(gs);

            if (BoardManager.Instance != null)
            {
                if (gs.currentTargets == null || gs.currentTargets.Count == 0)
                    BoardManager.Instance.EnsureAnnualTargets();
                else
                    BoardManager.Instance.RefreshTargetProgress();
            }

            if (_boardTargetUi != null && _activeNav == 2)
                _boardTargetUi.Refresh();
            if (_hrPanelUi != null && _activeNav == 3)
                _hrPanelUi.Refresh();
            if (_safetyPanelUi != null && _activeNav == 4)
                _safetyPanelUi.Refresh();
            if (_financeUi != null && _activeNav == 5)
                _financeUi.Refresh();
            if (_fleetUi != null && _activeNav == 6)
                _fleetUi.Refresh();
            if (_marketUi != null && _activeNav == 1)
                _marketUi.Refresh();
            if (_strategyUi != null && _activeNav == 8)
                _strategyUi.Refresh();
            if (_maintenanceUi != null && _activeNav == 9)
                _maintenanceUi.Refresh();
        }

        private void RebuildRouteCards(GameState gs)
        {
            if (_opsPanelUi != null)
            {
                _opsPanelUi.Refresh();
                return;
            }

            if (_routeCardsRoot == null) return;

            for (int i = 0; i < _routeCardInstances.Count; i++)
            {
                if (_routeCardInstances[i] != null)
                    Destroy(_routeCardInstances[i]);
            }
            _routeCardInstances.Clear();

            if (gs.activeRoutes.Count == 0)
            {
                var empty = UIFactory.Label(_routeCardsRoot, "Empty", "暂无航线 — 可在「市场」分析后，用 AI 执行开通", 15, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(0, 0.7f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
                _routeCardInstances.Add(empty.gameObject);
                return;
            }

            var cardPrefab = Resources.Load<GameObject>("UI/RouteCard");
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                RouteCard card;
                if (cardPrefab != null)
                {
                    var go = Object.Instantiate(cardPrefab, _routeCardsRoot);
                    go.name = "RouteCard_" + i;
                    var le = go.GetComponent<LayoutElement>();
                    if (le == null) le = go.AddComponent<LayoutElement>();
                    le.preferredHeight = 72;
                    le.flexibleWidth = 1;
                    card = go.GetComponent<RouteCard>();
                    _routeCardInstances.Add(go);
                }
                else
                {
                    card = RouteCard.BuildRuntime(_routeCardsRoot);
                    var le = card.gameObject.GetComponent<LayoutElement>();
                    if (le == null) le = card.gameObject.AddComponent<LayoutElement>();
                    le.preferredHeight = 72;
                    le.flexibleWidth = 1;
                    _routeCardInstances.Add(card.gameObject);
                }

                if (card != null)
                    card.Bind(gs.activeRoutes[i]);
            }
        }

        private void OnOpenRouteClicked()
        {
            var gs = GameState.Instance;
            if (gs == null || _flightInProgress) return;
            if (gs.gameOver)
            {
                if (gs.gameOverVictory)
                    ShowVictoryPopup(gs.lastLegacyScore, gs.lastLegacyGrade);
                else
                    ShowErrorPopup("游戏已结束\n" + (gs.gameOverReason ?? "董事会已解雇 CEO。"));
                return;
            }

            OpsPanelUI.OpenShellTab("n12_航线网络");
            var net = FindFirstObjectByType<NetworkPanelUI>();
            net?.ShowOpenRouteTab();
        }

        private void OnAOGResolved(int choice)
        {
            RefreshHud();
            var gs = GameState.Instance;
            string tip = choice == 1
                ? "停飞已处置：调机备援，现金 -20 万，航班准点。"
                : "停飞已处置：延误维修，现金 -15 万，满意度与准点率下降。";
            ShowPopup(tip);

            // 若与进入 Q2 的董事会目标公告叠在一起，危机结束后再播报
            if (_pendingBoardAnnounceResult != null)
            {
                var r = _pendingBoardAnnounceResult;
                _pendingBoardAnnounceResult = null;
                TryAnnounceBoardTargets(gs, r);
            }
        }

        private void OnDepartmentResignation(string deptCode, string name)
        {
            AppointmentContext.DeptCode = deptCode;
            ShowPopup($"离职事件\n\n{name}（{DepartmentCatalog.DisplayName(deptCode)}）\n因忠诚度过低主动辞职。\n该部门现已空缺，进入任命流程。");
            RefreshHud();
            GetComponent<PrefabNavIntegration>()?.OpenSubPage("n23_部门负责人任命");
        }

        private void OnExpansionResolved(int choice)
        {
            RefreshHud();
            var gs = GameState.Instance;
            string tip = choice switch
            {
                1 => "已接受扩张提案：北京→纽约已开通，解锁「伟大航路」。",
                2 => "已暂缓扩张提案：下一年 Q1 将再次收到提案。",
                3 => "已拒绝扩张提案：战略部不再就此航线提案。",
                _ => "扩张提案已处理。"
            };
            if (gs != null && gs.greatRouteUnlocked)
                tip += "\n成就：伟大航路 ✓";
            ShowPopup(tip);

            if (_pendingBoardAnnounceResult != null)
            {
                var r = _pendingBoardAnnounceResult;
                _pendingBoardAnnounceResult = null;
                TryAnnounceBoardTargets(gs, r);
            }
        }

        private void ShowVoteResult(YearEndResult yearEnd, string quarterTip)
        {
            if (_voteUi != null)
            {
                // 把季度财务摘要附在投票正文后，带清晰标题与分隔
                if (!string.IsNullOrEmpty(quarterTip))
                    yearEnd.voteBody = (yearEnd.voteBody ?? "")
                        + "\n\n———————————————\n■ 本季财务总结\n" + quarterTip;

                _voteUi.Show(yearEnd, () =>
                {
                    ResumeAfterSettleIfNeeded();
                    RefreshHud();
                    if (yearEnd.victory)
                    {
                        ShowVictoryPopup(yearEnd);
                    }
                    else if (yearEnd.gameOver)
                    {
                        ShowErrorPopup("Game Over\n" + (yearEnd.voteBody ?? yearEnd.message));
                        GameOverOverlay.Ensure(GetComponentInParent<Canvas>());
                        GameOverOverlay.ShowIfNeeded();
                    }
                    else if (_boardTargetUi != null && _activeNav == 2)
                    {
                        _boardTargetUi.Refresh();
                    }
                });
                return;
            }

            // 兜底：无 VoteUI 时用普通弹窗
            ShowYearEndSettlement(yearEnd, quarterTip);
        }

        private void ShowYearEndSettlement(YearEndResult yearEnd, string quarterTip)
        {
            if (yearEnd.victory)
            {
                ShowVictoryPopup(yearEnd);
                return;
            }

            string body =
                $"年度结算（第 {yearEnd.finishedYear} 年）· {yearEnd.voteTitle}\n" +
                $"完成率 {yearEnd.completionRate:F0}%（{yearEnd.targetsCompleted}/{yearEnd.targetsTotal}）\n" +
                $"{yearEnd.voteBody}\n\n" +
                quarterTip;
            if (yearEnd.gameOver)
                ShowErrorPopup(body);
            else
                ShowPopup(body);
            if (yearEnd.gameOver && !yearEnd.victory)
            {
                GameOverOverlay.Ensure(GetComponentInParent<Canvas>());
                GameOverOverlay.ShowIfNeeded();
            }
        }

        /// <summary>光荣退休胜利结算弹窗：评分卡 + 解锁奖励。</summary>
        private void ShowVictoryPopup(YearEndResult yearEnd)
        {
            var legacy = yearEnd.legacy;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("🏆 光荣退休");
            sb.AppendLine($"执掌航空 {yearEnd.finishedYear} 个财年，功成身退。\n");

            if (legacy != null)
            {
                sb.AppendLine($"  遗产评级  {legacy.grade}  （总分 {legacy.total:F0}）");
                sb.AppendLine("———————————————");
                sb.AppendLine($"  市值表现（40%）  {legacy.marketCapScore:F0}  · 市值 {legacy.marketCapWan:F0} 万美元");
                sb.AppendLine($"  安全飞行（30%）  {legacy.safetyScore:F0}  · 累计飞行 {legacy.totalFlightHours:F0} 小时");
                sb.AppendLine($"  航线覆盖（20%）  {legacy.coverageScore:F0}  · {legacy.coveredAirports} 机场 / {legacy.coveredCountries} 国 / {legacy.coveredContinents} 洲");
                sb.AppendLine($"  董事会满意度（10%） {legacy.satisfactionScore:F0}");
            }

            sb.AppendLine("\n" + (yearEnd.legacyUnlockText ?? ""));
            ShowPopup(sb.ToString());
        }

        /// <summary>已在游戏结束时（如点击已通关的入口）回显遗产评级。</summary>
        private void ShowVictoryPopup(float score, string grade)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("🏆 游戏已通关");
            sb.AppendLine("光荣退休 · 遗产已结算。");
            if (!string.IsNullOrEmpty(grade))
                sb.AppendLine($"\n  遗产评级  {grade}  （总分 {score:F0}）");
            sb.AppendLine("\n已解锁：传奇模式 · 新游戏+ · 快速模式");
            ShowPopup(sb.ToString());
        }

        private void TryAnnounceBoardTargets(GameState gs, QuarterResult result)
        {
            if (gs == null || result == null || gs.boardTargetsAnnounced) return;
            // 进入第 1 年 Q2：董事会「突然」强调年度目标清单
            if (!(result.toYear == 1 && result.toQuarter == 2)) return;
            if (gs.currentTargets == null || gs.currentTargets.Count == 0) return;

            gs.boardTargetsAnnounced = true;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("董事会发来年度目标清单：");
            for (int i = 0; i < gs.currentTargets.Count; i++)
                sb.AppendLine("· " + gs.currentTargets[i].ProgressLabel);
            sb.Append("请打开左侧「董事会」查看进度。");
            ShowPopup(sb.ToString());
            if (_chatOutput != null)
                _chatOutput.text = (_chatOutput.text ?? "") + "\n\n" + sb;
        }

        private void PushFinanceWarningToChat(string warning)
        {
            if (_chatOutput == null) return;
            string existing = _chatOutput.text ?? "";
            _chatOutput.text = existing + "\n\n" + warning;
            HideExecuteAdvice();

            _pendingAdvice = new AdvisorResponse
            {
                advisor = "财务顾问",
                emotion = "谨慎",
                message = warning,
                suggestedAction = "delay",
                data = new AdvisorDataPayload { profit = 0f, risk = "high" },
                fromFallback = true
            };
            if (_executeAdviceBtn != null)
                _executeAdviceBtn.gameObject.SetActive(true);
            if (_executeHintText != null)
            {
                _executeHintText.color = UITheme.Danger;
                _executeHintText.text = "建议：暂缓扩张（满意度+2）";
            }
        }

        private void OnChatSend()
        {
            string msg = _chatInput != null ? _chatInput.text : "";
            if (_chatInput != null) _chatInput.text = "";

            HideExecuteAdvice();

            if (AIChatManager.Instance == null)
            {
                _chatOutput.text = NeuralCommandMatcher.Reply(msg);
                return;
            }

            if (AIChatManager.Instance.IsBusy)
            {
                _chatOutput.text = "战略顾问：上一条还在处理，请稍候。";
                return;
            }

            _chatOutput.text = "战略顾问：思考中…";
            AIChatManager.Instance.Ask(msg, response =>
            {
                if (_chatOutput == null || response == null) return;
                string text = response.ToDisplayText();
                if (response.fromFallback && !string.IsNullOrEmpty(response.errorHint))
                    text += $"\n（{response.errorHint}）";
                _chatOutput.text = text;
                ShowExecuteAdviceIfNeeded(response);
            });
        }

        private void ShowExecuteAdviceIfNeeded(AdvisorResponse response)
        {
            _pendingAdvice = response;
            var action = SuggestedActionParser.Parse(response.suggestedAction);
            bool show = action == SuggestedAction.OpenRoute
                        || action == SuggestedAction.Delay
                        || action == SuggestedAction.Reject
                        || action == SuggestedAction.CheckCash;

            if (_executeAdviceBtn != null)
                _executeAdviceBtn.gameObject.SetActive(show);

            if (_executeHintText != null)
            {
                if (!show)
                {
                    _executeHintText.text = "";
                    return;
                }

                _executeHintText.color = UITheme.Warning;
                switch (action)
                {
                    case SuggestedAction.OpenRoute:
                        string o = response.data?.origin ?? "北京";
                        string d = response.data?.destination ?? "伦敦";
                        _executeHintText.text = $"将开通 {o}-{d}（启动费 300 万）";
                        break;
                    case SuggestedAction.Delay:
                    case SuggestedAction.Reject:
                        _executeHintText.text = "将记录暂缓，满意度 +2";
                        break;
                    case SuggestedAction.CheckCash:
                        _executeHintText.text = "查看现金与航线收入";
                        break;
                    default:
                        _executeHintText.text = "";
                        break;
                }
            }
        }

        private void HideExecuteAdvice()
        {
            _pendingAdvice = null;
            if (_executeAdviceBtn != null)
                _executeAdviceBtn.gameObject.SetActive(false);
            if (_executeHintText != null)
                _executeHintText.text = "";
        }

        private void OnExecuteAdvice()
        {
            var advice = _pendingAdvice ?? AIChatManager.Instance?.LastAdvice;
            if (advice == null)
            {
                ShowPopup("暂无建议可执行");
                return;
            }

            if (ActionExecutor.Instance == null)
            {
                ShowPopup("ActionExecutor 未就绪");
                return;
            }

            var result = ActionExecutor.Instance.Execute(advice);
            if (result == null)
                return;

            if (result.success)
            {
                if (_executeHintText != null)
                {
                    _executeHintText.color = UITheme.Success;
                    _executeHintText.text = "已执行";
                }
                if (_chatOutput != null)
                    _chatOutput.text = (_chatOutput.text ?? "") + "\n\n" + ChineseFontProvider.Sanitize("✅ " + result.message);
                var action = SuggestedActionParser.Parse(advice.suggestedAction);
                // 已开通 / 开通成功：都收起「执行建议」，避免误以为还能再掷航权
                if (action == SuggestedAction.OpenRoute)
                    HideExecuteAdvice();
                RefreshHud();
                // 说明类成功（如已开通提示）用普通弹窗，不用红色报错
                if (result.message != null && result.message.Contains("已经开通"))
                    ShowPopup(result.message);
            }
            else
            {
                if (_executeHintText != null)
                {
                    _executeHintText.color = UITheme.Danger;
                    _executeHintText.text = ChineseFontProvider.Sanitize(result.message);
                }
                ShowErrorPopup(result.message);
                // 航权被拒：保留「执行建议」，可立刻再点重试
            }
        }

        public void ShowErrorPopup(string message)
        {
            ApplyPopupText(message, UITheme.Danger);
            _popupRoot.SetActive(true);
        }

        private void ShowPopup(string message)
        {
            ApplyPopupText(message, UITheme.TextPrimary);
            _popupRoot.SetActive(true);

            // Phase 14：弹窗淡入 + 轻微上滑动效
            var cg = _popupRoot.GetComponent<CanvasGroup>();
            if (cg == null) cg = _popupRoot.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            var rt = _popupRoot.GetComponent<RectTransform>();
            var targetPos = rt.anchoredPosition;
            rt.anchoredPosition = targetPos + new Vector2(0, -20f);
            StartCoroutine(AnimatePopupGeneric(cg, rt, targetPos));
        }

        private System.Collections.IEnumerator AnimatePopupGeneric(CanvasGroup cg, RectTransform rt, Vector2 targetPos)
        {
            float dur = 0.35f;
            float t = 0f;
            Vector2 from = rt.anchoredPosition;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float p = t / dur;
                p = 1f - (1f - p) * (1f - p);
                cg.alpha = Mathf.Lerp(0f, 1f, p);
                rt.anchoredPosition = Vector2.Lerp(from, targetPos, p);
                yield return null;
            }
            cg.alpha = 1f;
            rt.anchoredPosition = targetPos;
        }

        private void ApplyPopupText(string message, Color color)
        {
            if (_popupText == null) return;
            _popupText.color = color;
            _popupText.text = ChineseFontProvider.Sanitize(message);
            Canvas.ForceUpdateCanvases();
            var scroll = _popupText.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private string BuildQuarterPopupText(QuarterResult result, float cashBefore,
            string dateBefore, GameState gs)
        {
            var sb = new System.Text.StringBuilder();

            // ── 标题 ──
            sb.AppendLine($"<b>{dateBefore} → {gs.DateLabel}</b>");
            sb.AppendLine();

            // ── ■ 财务概况 ──
            sb.AppendLine("■ 财务概况");
            float operatingMargin = result.quarterIncome - result.quarterExpense;
            sb.AppendLine(
                $"  航班及联盟收入 {result.quarterIncome:F0} − 日常支出 {result.quarterExpense:F0} = " +
                $"{(operatingMargin >= 0 ? "+" : "")}{operatingMargin:F0} 万（已按实际记账）");
            sb.AppendLine($"  基础结算追加入账 {(result.netChange >= 0 ? "+" : "")}{result.netChange:F0} 万；其他专项见事件明细");
            sb.AppendLine($"  现金 {cashBefore:F0} → {result.cashAfter:F0} 万");

            // ── ■ 🚨 紧急（亮黄色）──
            if (!string.IsNullOrEmpty(result.urgentEventText))
            {
                sb.AppendLine();
                sb.AppendLine($"<color=#FFD600>■ 🚨 紧急</color>");
                foreach (var line in result.urgentEventText.Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        sb.AppendLine($"<color=#FFD600>  ▸ {line.Trim()}</color>");
                }
            }

            // ── ■ 📋 随机事件 ──
            if (!string.IsNullOrEmpty(result.randomEventText))
            {
                sb.AppendLine();
                sb.AppendLine("■ 📋 随机事件");
                foreach (var line in result.randomEventText.Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        sb.AppendLine($"  {line.Trim()}");
                }
            }

            // ── ■ 📋 AI 竞对动态 ──
            if (!string.IsNullOrEmpty(result.aiEventText))
            {
                sb.AppendLine();
                sb.AppendLine("■ 📋 AI 竞对动态");
                foreach (var line in result.aiEventText.Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        sb.AppendLine($"  {line.Trim()}");
                }
            }

            // ── ■ 📋 其他运营记录 ──
            if (!string.IsNullOrEmpty(result.otherEventText))
            {
                sb.AppendLine();
                sb.AppendLine("■ 📋 其他运营记录");
                foreach (var line in result.otherEventText.Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        sb.AppendLine($"  {line.Trim()}");
                }
            }

            // ── Phase 13：■ 🌐 宏观经济展望（每年 Q1 出现）──
            if (!string.IsNullOrEmpty(result.macroEventText))
            {
                sb.AppendLine();
                sb.AppendLine("■ 🌐 宏观经济展望");
                foreach (var line in result.macroEventText.Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        sb.AppendLine($"  {line.Trim()}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        // Phase 14：页签切换淡出
        private System.Collections.IEnumerator CrossfadeOut(GameObject panel, System.Action onDone)
        {
            var cg = panel.GetComponent<CanvasGroup>();
            if (cg == null) cg = panel.AddComponent<CanvasGroup>();
            float duration = 0.15f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Lerp(1f, 0f, elapsed / duration);
                yield return null;
            }
            cg.alpha = 0f;
            panel.SetActive(false);
            cg.alpha = 1f; // reset for next show
            onDone?.Invoke();
        }

        private System.Collections.IEnumerator CrossfadeIn(GameObject panel)
        {
            var cg = panel.GetComponent<CanvasGroup>();
            if (cg == null) cg = panel.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            float duration = 0.3f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / duration;
                t = 1f - (1f - t) * (1f - t);
                cg.alpha = Mathf.Lerp(0f, 1f, t);
                yield return null;
            }
            cg.alpha = 1f;
        }
    }
}
