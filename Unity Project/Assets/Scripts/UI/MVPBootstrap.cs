using System.Collections;
using System.Collections.Generic;
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
        private const float FirstFlightReward = 15f;
        private const float FirstFlightSeconds = 5f;

        private TextMeshProUGUI _dateText;
        private TextMeshProUGUI _companyText;
        private CashMeter _cashMeter;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _popupText;
        private GameObject _popupRoot;
        private GameObject _progressRoot;
        private Image _progressFill;
        private TextMeshProUGUI _progressLabel;
        private GameObject _settleRoot;
        private TextMeshProUGUI _settleText;
        private Button _firstFlightBtn;
        private TMP_InputField _originInput;
        private TMP_InputField _destInput;
        private TextMeshProUGUI _rpmResultText;
        private Image _rpmLight;
        private TMP_InputField _chatInput;
        private TextMeshProUGUI _chatOutput;
        private Button _executeAdviceBtn;
        private TextMeshProUGUI _executeHintText;
        private TextMeshProUGUI _opsHintText;
        private Transform _routeCardsRoot;
        private readonly List<GameObject> _routeCardInstances = new List<GameObject>();
        private AdvisorResponse _pendingAdvice;
        private bool _flightInProgress;
        private bool _quarterInProgress;
        private Button _advanceQuarterBtn;
        private TextMeshProUGUI _hourglassText;

        private GameObject _panelOps;
        private GameObject _panelMarket;
        private GameObject _panelBoard;
        private GameObject _panelHr;
        private GameObject _panelSafety;
        private GameObject _panelFinance;
        private GameObject _panelAi;
        private BoardTargetUI _boardTargetUi;
        private HRUI _hrUi;
        private FinancePanelUI _financeUi;
        private HedgeTutorialUI _hedgeTutorialUi;
        private SafetyUI _safetyUi;
        private VoteUI _voteUi;
        private AOGUI _aogUi;
        private ExpansionProposalUI _expansionUi;
        private FloatingPanelChrome _chatChrome;
        private QuarterResult _pendingBoardAnnounceResult;
        private readonly List<Button> _navButtons = new List<Button>();
        private readonly List<Image> _navImages = new List<Image>();
        private int _activeNav;

        private void Awake()
        {
            EnsureCoreSystems();
            EnsureEventSystem();
            ChineseFontProvider.WarmupSampleChinese();
            BuildUI();
            ShowNavPanel(1); // 默认「市场」= RPM
            RefreshHud();

            if (GameState.Instance != null)
                GameState.Instance.OnStateChanged += RefreshHud;
        }

        private void OnDestroy()
        {
            if (GameState.Instance != null)
                GameState.Instance.OnStateChanged -= RefreshHud;
            if (AOGManager.Instance != null)
                AOGManager.Instance.OnAOGResolved -= OnAOGResolved;
            if (ExpansionProposalManager.Instance != null)
                ExpansionProposalManager.Instance.OnProposalResolved -= OnExpansionResolved;
            if (DepartmentManager.Instance != null)
                DepartmentManager.Instance.OnResignation -= OnDepartmentResignation;
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

        private void BuildUI()
        {
            var canvasGo = new GameObject("Cockpit_Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            UIFactory.Panel(canvasGo.transform, "Background", UIFactory.Fill, UITheme.Bg, rounded: false);

            // 轻微网格氛围
            var grid = UIFactory.Panel(canvasGo.transform, "GridHint",
                new UIFactory.Anchor(new Vector2(0.12f, 0.05f), new Vector2(1f, 0.9f), Vector2.zero, Vector2.zero),
                new Color(0.1f, 0.2f, 0.35f, 0.18f), rounded: false);
            grid.GetComponent<Image>().raycastTarget = false;

            BuildTopBar(canvasGo.transform);
            BuildLeftNav(canvasGo.transform);
            BuildCenterPanels(canvasGo.transform);
            BuildChatBubble(canvasGo.transform);
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

            _hourglassText = UIFactory.Label(canvasGo.transform, "HourglassText", "", 22, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.35f, 0.48f), new Vector2(0.72f, 0.58f), Vector2.zero, Vector2.zero),
                UITheme.Warning);
            _hourglassText.gameObject.SetActive(false);
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

            // 左：公司
            _companyText = UIFactory.Label(bar.transform, "Company",
                ChineseFontProvider.CompanyPlanePrefix + " 天际航空 · CEO", 22, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.015f, 0), new Vector2(0.28f, 1), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            // 中：日期
            _dateText = UIFactory.Label(bar.transform, "DateText", "第 1 年 Q1", 24, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.28f, 0.15f), new Vector2(0.48f, 0.85f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _advanceQuarterBtn = UIFactory.Button(bar.transform, "AdvanceQuarterButton", "执行季度运营",
                new UIFactory.Anchor(new Vector2(0.485f, 0.22f), new Vector2(0.62f, 0.78f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnAdvanceQuarterClicked, 15);

            // 右：现金油量表
            var meterPrefab = Resources.Load<GameObject>("UI/CashMeter");
            if (meterPrefab != null)
            {
                var go = Object.Instantiate(meterPrefab, bar.transform);
                go.name = "CashMeter";
                var rt = go.GetComponent<RectTransform>();
                UIFactory.ApplyAnchor(rt, new UIFactory.Anchor(new Vector2(0.63f, 0.08f), new Vector2(0.99f, 0.92f), Vector2.zero, Vector2.zero));
                _cashMeter = go.GetComponent<CashMeter>();
            }
            if (_cashMeter == null)
            {
                _cashMeter = CashMeter.BuildRuntime(bar.transform,
                    new UIFactory.Anchor(new Vector2(0.63f, 0.08f), new Vector2(0.99f, 0.92f), Vector2.zero, Vector2.zero));
            }

            _statusText = UIFactory.Label(bar.transform, "StatusText", "F5 存档 · F9 读档", 11, TextAlignmentOptions.BottomLeft,
                new UIFactory.Anchor(new Vector2(0.015f, 0.02f), new Vector2(0.28f, 0.28f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
        }

        private void BuildLeftNav(Transform parent)
        {
            var nav = UIFactory.Panel(parent, "LeftNav",
                new UIFactory.Anchor(new Vector2(0, 0), new Vector2(0, 1),
                    new Vector2(12, 16), new Vector2(UITheme.LeftNavWidth, -UITheme.TopBarHeight - 8)),
                UITheme.Glass);
            UIFactory.AddBorder(nav, UITheme.Border, new Vector2(1.2f, -1.2f));

            UIFactory.Label(nav.transform, "NavTitle", "驾驶舱", 14, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.08f, 0.92f), new Vector2(0.92f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            // 0运营 1市场 2董事会 3人事 4安全 5财务 6AI
            string[] labels = { "运营", "市场", "董事会", "👤 人事", "安全", "财务", "AI 顾问" };
            float top = 0.90f;
            float h = 0.062f;
            float gap = 0.01f;
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                float yMax = top - i * (h + gap);
                float yMin = yMax - h;
                var btn = UIFactory.Button(nav.transform, $"Nav_{labels[i]}", labels[i],
                    new UIFactory.Anchor(new Vector2(0.1f, yMin), new Vector2(0.9f, yMax), Vector2.zero, Vector2.zero),
                    UITheme.NavIdle, () => ShowNavPanel(index), 17);
                _navButtons.Add(btn);
                _navImages.Add(btn.GetComponent<Image>());
            }
        }

        private void BuildCenterPanels(Transform parent)
        {
            var center = UIFactory.Panel(parent, "CenterHost",
                new UIFactory.Anchor(new Vector2(0, 0), new Vector2(1, 1),
                    new Vector2(UITheme.LeftNavWidth + 28, 20), new Vector2(-28, -UITheme.TopBarHeight - 12)),
                new Color(0, 0, 0, 0), rounded: false);
            center.GetComponent<Image>().raycastTarget = false;

            _panelOps = BuildOpsPanel(center.transform);
            _panelMarket = BuildMarketPanel(center.transform);
            _boardTargetUi = BoardTargetUI.Build(center.transform);
            _panelBoard = _boardTargetUi.gameObject;
            _hrUi = HRUI.Build(center.transform);
            _panelHr = _hrUi.gameObject;
            _safetyUi = SafetyUI.Build(center.transform);
            _panelSafety = _safetyUi.gameObject;
            _financeUi = FinancePanelUI.Build(center.transform);
            _panelFinance = _financeUi.gameObject;
            _panelAi = BuildAiHelpPanel(center.transform);
        }

        private GameObject BuildOpsPanel(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Ops", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));

            UIFactory.Label(panel.transform, "Title", "运营中心 · 航线与首航", 22, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.9f), new Vector2(0.7f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _opsHintText = UIFactory.Label(panel.transform, "Hint", "完成第一班飞行，入账 15 万美元", 15, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.82f), new Vector2(0.7f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            _firstFlightBtn = UIFactory.Button(panel.transform, "FirstFlightButton", "确认首航",
                new UIFactory.Anchor(new Vector2(0.72f, 0.84f), new Vector2(0.96f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnFirstFlightClicked, 18);

            UIFactory.Label(panel.transform, "RoutesTitle", "已开通航线", 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.74f), new Vector2(0.5f, 0.82f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var listHost = UIFactory.Panel(panel.transform, "RouteList",
                new UIFactory.Anchor(new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.74f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            var vlg = listHost.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 10;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            _routeCardsRoot = listHost.transform;

            return panel;
        }

        private GameObject BuildMarketPanel(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Market", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));

            UIFactory.Label(panel.transform, "Title", "市场 · RPM 航线分析", 22, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.9f), new Vector2(0.7f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Label(panel.transform, "Sub", "输入出发/到达城市，评估综合得分与日利润", 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.84f), new Vector2(0.7f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Label(panel.transform, "OriginLabel", "出发", 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.7f), new Vector2(0.18f, 0.8f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _originInput = UIFactory.Input(panel.transform, "OriginInput", "北京",
                new UIFactory.Anchor(new Vector2(0.2f, 0.7f), new Vector2(0.55f, 0.8f), Vector2.zero, Vector2.zero));

            UIFactory.Label(panel.transform, "DestLabel", "到达", 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.56f), new Vector2(0.18f, 0.66f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _destInput = UIFactory.Input(panel.transform, "DestInput", "伦敦",
                new UIFactory.Anchor(new Vector2(0.2f, 0.56f), new Vector2(0.55f, 0.66f), Vector2.zero, Vector2.zero));

            UIFactory.Button(panel.transform, "AnalyzeButton", "分析航线",
                new UIFactory.Anchor(new Vector2(0.6f, 0.7f), new Vector2(0.82f, 0.8f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnAnalyzeRpm, 16);

            _rpmLight = UIFactory.Panel(panel.transform, "RpmLight",
                new UIFactory.Anchor(new Vector2(0.85f, 0.7f), new Vector2(0.94f, 0.8f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted).GetComponent<Image>();

            var resultBox = UIFactory.Panel(panel.transform, "ResultBox",
                new UIFactory.Anchor(new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.5f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            _rpmResultText = UIFactory.Label(resultBox.transform, "RpmResult",
                "点击「分析航线」查看综合评定与红绿灯", 17,
                TextAlignmentOptions.TopLeft, UIFactory.Fill, UITheme.TextPrimary);
            var rt = _rpmResultText.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(16, 12);
            rt.offsetMax = new Vector2(-16, -12);

            return panel;
        }

        private GameObject BuildAiHelpPanel(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_AI", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));

            UIFactory.Label(panel.transform, "Title", "AI 顾问 · 使用说明", 22, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.88f), new Vector2(0.8f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Label(panel.transform, "Body",
                "右下角悬浮气泡即 Neural Command。\n\n" +
                "· 输入「分析航线」获取开通建议\n" +
                "· 出现「执行建议」后需手动确认才会改现金\n" +
                "· API 超时自动切本地容灾，不影响试玩\n\n" +
                "字体验收：马格努斯·林德格伦 · 北京 → 伦敦",
                16, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.2f), new Vector2(0.95f, 0.85f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            return panel;
        }

        private void BuildChatBubble(Transform parent)
        {
            ChatBubble bubble = null;
            var prefab = Resources.Load<GameObject>("UI/ChatBubble");
            GameObject panel;
            if (prefab != null)
            {
                panel = Object.Instantiate(prefab, parent);
                panel.name = "ChatBubble";
                UIFactory.ApplyAnchor(panel.GetComponent<RectTransform>(),
                    new UIFactory.Anchor(new Vector2(0.62f, 0.03f), new Vector2(0.985f, 0.36f), Vector2.zero, Vector2.zero));
                bubble = panel.GetComponent<ChatBubble>();
            }
            else
            {
                bubble = ChatBubble.BuildRuntime(parent,
                    new UIFactory.Anchor(new Vector2(0.62f, 0.03f), new Vector2(0.985f, 0.36f), Vector2.zero, Vector2.zero));
                panel = bubble.gameObject;
            }

            if (bubble != null && bubble.BodyText != null)
                _chatOutput = bubble.BodyText;
            else
            {
                _chatOutput = UIFactory.Label(panel.transform, "ChatOutput",
                    "战略顾问：输入「分析航线」后，可点「执行建议」真正开通。", 14,
                    TextAlignmentOptions.TopLeft,
                    new UIFactory.Anchor(new Vector2(0.04f, 0.36f), new Vector2(0.96f, 0.86f), Vector2.zero, Vector2.zero),
                    new Color(0.82f, 0.9f, 0.86f));
            }

            _executeAdviceBtn = UIFactory.Button(panel.transform, "ExecuteAdviceButton", "执行建议",
                new UIFactory.Anchor(new Vector2(0.04f, 0.22f), new Vector2(0.42f, 0.34f), Vector2.zero, Vector2.zero),
                UITheme.Success, OnExecuteAdvice, 14);
            _executeAdviceBtn.gameObject.SetActive(false);

            _executeHintText = UIFactory.Label(panel.transform, "ExecuteHint", "", 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.44f, 0.22f), new Vector2(0.96f, 0.34f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            _chatInput = UIFactory.Input(panel.transform, "ChatInput", "输入指令…",
                new UIFactory.Anchor(new Vector2(0.04f, 0.04f), new Vector2(0.68f, 0.18f), Vector2.zero, Vector2.zero));

            UIFactory.Button(panel.transform, "SendButton", "发送",
                new UIFactory.Anchor(new Vector2(0.72f, 0.04f), new Vector2(0.96f, 0.18f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnChatSend, 15);

            // 默认圆形图标；点击展开/收拢对话，图标可拖动
            _chatChrome = FloatingPanelChrome.Attach(panel, "Neural Command · 战略顾问");
        }

        private void ShowNavPanel(int index)
        {
            _activeNav = index;
            // 0运营 1市场 2董事会 3人事 4安全 5财务 6AI
            if (_panelOps != null) _panelOps.SetActive(index == 0);
            if (_panelMarket != null) _panelMarket.SetActive(index == 1);
            if (_panelBoard != null) _panelBoard.SetActive(index == 2);
            if (_panelHr != null) _panelHr.SetActive(index == 3);
            if (_panelSafety != null) _panelSafety.SetActive(index == 4);
            if (_panelFinance != null) _panelFinance.SetActive(index == 5);
            if (_panelAi != null) _panelAi.SetActive(index == 6);

            // 进人事时收拢为图标，避免挡住列表
            if (index == 3)
                _chatChrome?.CollapseIfExpanded();

            if (index == 2 && _boardTargetUi != null)
                _boardTargetUi.Refresh();
            if (index == 3 && _hrUi != null)
                _hrUi.Refresh();
            if (index == 4 && _safetyUi != null)
                _safetyUi.Refresh();
            if (index == 5 && _financeUi != null)
                _financeUi.OnOpened();

            for (int i = 0; i < _navImages.Count; i++)
            {
                if (_navImages[i] == null) continue;
                _navImages[i].color = i == index ? UITheme.NavActive : UITheme.NavIdle;
                var hover = _navImages[i].GetComponent<UIHoverBrighten>();
                if (hover != null) hover.RefreshBase();
            }
        }

        private void BuildPopup(Transform parent)
        {
            _popupRoot = UIFactory.Panel(parent, "Popup",
                new UIFactory.Anchor(new Vector2(0.28f, 0.35f), new Vector2(0.72f, 0.68f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.1f, 0.18f, 0.97f)).gameObject;
            UIFactory.AddBorder(_popupRoot, UITheme.Accent, new Vector2(2f, -2f));
            _popupText = UIFactory.Label(_popupRoot.transform, "PopupText", "开发中", 22, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.06f, 0.32f), new Vector2(0.94f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            UIFactory.Button(_popupRoot.transform, "PopupClose", "关闭",
                new UIFactory.Anchor(new Vector2(0.35f, 0.08f), new Vector2(0.65f, 0.26f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => _popupRoot.SetActive(false), 16);
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
                new UIFactory.Anchor(new Vector2(0.3f, 0.35f), new Vector2(0.7f, 0.68f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.14f, 0.12f, 0.97f)).gameObject;
            UIFactory.AddBorder(_settleRoot, UITheme.Success, new Vector2(2f, -2f));

            _settleText = UIFactory.Label(_settleRoot.transform, "SettleText", "首航成功！赚了 15 万", 24, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.06f, 0.32f), new Vector2(0.94f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.Success);

            UIFactory.Button(_settleRoot.transform, "SettleClose", "确认",
                new UIFactory.Anchor(new Vector2(0.32f, 0.08f), new Vector2(0.68f, 0.26f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => _settleRoot.SetActive(false), 16);

            _settleRoot.SetActive(false);
        }

        private void RefreshHud()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            if (_dateText != null) _dateText.text = gs.DateLabel;

            float months = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            if (_cashMeter != null)
                _cashMeter.SetImmediate(gs.cash, months, gs.monthlyRevenue);

            RebuildRouteCards(gs);

            if (_firstFlightBtn != null)
            {
                var label = _firstFlightBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (gs.firstFlightCompleted)
                {
                    _firstFlightBtn.interactable = false;
                    if (label != null) label.text = "首航已完成";
                    if (_opsHintText != null) _opsHintText.text = "首航已完成，继续开通航线与季度运营";
                }
            }

            if (_statusText != null && SaveManager.Instance != null)
                _statusText.text = SaveManager.Instance.LastStatus;

            if (BoardManager.Instance != null)
            {
                if (gs.currentTargets == null || gs.currentTargets.Count == 0)
                    BoardManager.Instance.EnsureAnnualTargets();
                else
                    BoardManager.Instance.RefreshTargetProgress();
            }

            if (_boardTargetUi != null && _activeNav == 2)
                _boardTargetUi.Refresh();
            if (_hrUi != null && _activeNav == 3)
                _hrUi.Refresh();
            if (_safetyUi != null && _activeNav == 4)
                _safetyUi.Refresh();
            if (_financeUi != null && _activeNav == 5)
                _financeUi.Refresh();
        }

        private void RebuildRouteCards(GameState gs)
        {
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

        private void OnFirstFlightClicked()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.firstFlightCompleted || _flightInProgress) return;
            StartCoroutine(FirstFlightRoutine());
        }

        private void OnAdvanceQuarterClicked()
        {
            if (_quarterInProgress || _flightInProgress) return;
            var gs = GameState.Instance;
            if (gs != null && gs.gameOver)
            {
                ShowErrorPopup("游戏已结束\n" + (gs.gameOverReason ?? "董事会已解雇 CEO。"));
                return;
            }
            if (TimeManager.Instance == null)
            {
                ShowPopup("TimeManager 未就绪");
                return;
            }

            StartCoroutine(AdvanceQuarterRoutine());
        }

        private IEnumerator AdvanceQuarterRoutine()
        {
            _quarterInProgress = true;
            if (_advanceQuarterBtn != null)
                _advanceQuarterBtn.interactable = false;

            var gs = GameState.Instance;
            float cashBefore = gs != null ? gs.cash : 0f;
            string dateBefore = gs != null ? gs.DateLabel : "";

            if (_hourglassText != null)
            {
                _hourglassText.gameObject.SetActive(true);
                _hourglassText.text = "⏳ 季度运营结算中…";
            }

            if (_progressRoot != null)
            {
                _progressRoot.SetActive(true);
                _progressFill.fillAmount = 0f;
                _progressLabel.text = "沙漏转动…";
            }

            float duration = 1.6f;
            float t = 0f;
            string[] sand = { "⏳", "⌛", "⏳", "⌛" };
            int frame = 0;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                if (_progressFill != null) _progressFill.fillAmount = p;
                if (_hourglassText != null)
                    _hourglassText.text = $"{sand[frame % sand.Length]} 结算中 {Mathf.RoundToInt(p * 100)}%";
                if (_progressLabel != null)
                    _progressLabel.text = $"季度推进… {Mathf.CeilToInt(duration - t)}s";
                if (Mathf.FloorToInt(t * 6f) != frame)
                    frame = Mathf.FloorToInt(t * 6f);
                yield return null;
            }

            var result = TimeManager.Instance.AdvanceQuarter();

            if (gs != null && _cashMeter != null)
            {
                float months = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
                _cashMeter.AnimateTo(gs.cash, months, gs.monthlyRevenue, 0.75f);
                yield return new WaitForSeconds(0.75f);
            }

            if (_progressRoot != null) _progressRoot.SetActive(false);
            if (_hourglassText != null) _hourglassText.gameObject.SetActive(false);

            RefreshHud();

            if (result != null && result.success)
            {
                string tip =
                    $"{dateBefore} → {gs.DateLabel}\n" +
                    $"收入 {result.quarterIncome:F0} − 支出 {result.quarterExpense:F0} = " +
                    $"{(result.netChange >= 0 ? "+" : "")}{result.netChange:F0} 万\n" +
                    $"现金 {cashBefore:F0} → {result.cashAfter:F0} 万";
                if (!string.IsNullOrEmpty(result.eventText))
                    tip += "\n" + result.eventText;

                if (result.yearEnded && result.yearEnd != null && result.yearEnd.showSettlementPopup)
                {
                    ShowVoteResult(result.yearEnd, tip);
                }
                else if (result.isBankruptWarning)
                {
                    ShowErrorPopup("破产警告\n" + tip + "\n现金已为负，请尽快融资或削减成本！");
                }
                else if (result.pendingAOG || result.pendingExpansionProposal)
                {
                    // 事件弹窗优先；董事会目标公告延后
                    _pendingBoardAnnounceResult = result;
                }
                else
                {
                    ShowPopup("季度运营完成\n" + tip);
                    TryAnnounceBoardTargets(gs, result);
                }

                if (result.needsFinanceWarning && !string.IsNullOrEmpty(result.financeWarning))
                    PushFinanceWarningToChat(result.financeWarning);

                // 结算动画结束后再弹事件（避免与 WaitForSeconds 冲突）
                if (result.pendingAOG && AOGManager.Instance != null)
                    AOGManager.Instance.TriggerAOG();
                else if (result.pendingExpansionProposal && ExpansionProposalManager.Instance != null)
                    ExpansionProposalManager.Instance.TriggerProposal();
            }
            else
            {
                ShowErrorPopup(result != null ? result.message : "季度推进失败");
            }

            if (_advanceQuarterBtn != null)
                _advanceQuarterBtn.interactable = true;
            _quarterInProgress = false;
        }

        private void OnAOGResolved(int choice)
        {
            RefreshHud();
            var gs = GameState.Instance;
            string tip = choice == 1
                ? "AOG 已处置：调机备援，现金 -20 万，航班准点。"
                : "AOG 已处置：延误维修，现金 -15 万，满意度与准点率下降。";
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
            if (_hrUi != null)
                _hrUi.ShowResignation(deptCode, name);
            else
                Debug.Log($"[HR] 离职：{name} @ {deptCode}");
            RefreshHud();
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
                // 把季度财务摘要附在投票正文后
                if (!string.IsNullOrEmpty(quarterTip))
                    yearEnd.voteBody = (yearEnd.voteBody ?? "") + "\n\n" + quarterTip;

                _voteUi.Show(yearEnd, () =>
                {
                    RefreshHud();
                    if (yearEnd.gameOver)
                    {
                        ShowErrorPopup("Game Over\n" + (yearEnd.voteBody ?? yearEnd.message));
                        if (_advanceQuarterBtn != null)
                            _advanceQuarterBtn.interactable = false;
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
            string body =
                $"年度结算（第 {yearEnd.finishedYear} 年）· {yearEnd.voteTitle}\n" +
                $"完成率 {yearEnd.completionRate:F0}%（{yearEnd.targetsCompleted}/{yearEnd.targetsTotal}）\n" +
                $"{yearEnd.voteBody}\n\n" +
                quarterTip;
            if (yearEnd.gameOver)
                ShowErrorPopup(body);
            else
                ShowPopup(body);
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

        private IEnumerator FirstFlightRoutine()
        {
            _flightInProgress = true;
            if (_firstFlightBtn != null) _firstFlightBtn.interactable = false;
            _progressRoot.SetActive(true);
            _progressFill.fillAmount = 0f;

            float t = 0f;
            while (t < FirstFlightSeconds)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / FirstFlightSeconds);
                _progressFill.fillAmount = p;
                _progressLabel.text = $"起飞中… {Mathf.CeilToInt((FirstFlightSeconds - t))}s";
                yield return null;
            }

            _progressRoot.SetActive(false);

            var gs = GameState.Instance;
            float cashBefore = gs.cash;
            gs.firstFlightCompleted = true;
            gs.AddCash(FirstFlightReward);
            gs.yearNetProfit += FirstFlightReward;

            if (_cashMeter != null)
            {
                float monthsBefore = CashRunnelCalculator.GetRunwayMonths(cashBefore, gs.EffectiveMonthlyBurn);
                float monthsAfter = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
                _cashMeter.SetImmediate(cashBefore, monthsBefore, gs.monthlyRevenue);
                _cashMeter.AnimateTo(gs.cash, monthsAfter, gs.monthlyRevenue, 0.55f);
            }

            _settleText.text = $"首航成功！\n本班结算赚了 {FirstFlightReward:F0} 万美元\n当前现金：{gs.cash:F0} 万";
            _settleRoot.SetActive(true);
            _flightInProgress = false;

            if (_dateText != null) _dateText.text = gs.DateLabel;
            RebuildRouteCards(gs);
            if (_firstFlightBtn != null)
            {
                _firstFlightBtn.interactable = false;
                var label = _firstFlightBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = "首航已完成";
            }
            if (_opsHintText != null) _opsHintText.text = "首航已完成，继续开通航线与季度运营";
        }

        private void OnAnalyzeRpm()
        {
            string origin = _originInput != null ? _originInput.text.Trim() : "";
            string dest = _destInput != null ? _destInput.text.Trim() : "";

            bool isPekLhr =
                (origin.Contains("北京") || origin.ToUpperInvariant().Contains("PEK")) &&
                (dest.Contains("伦敦") || dest.ToUpperInvariant().Contains("LHR") || dest.ToUpperInvariant().Contains("LON"));

            var input = new RPMCalculator.RPMInput
            {
                distance = isPekLhr ? 8150f : 4500f,
                fuelPricePerTon = GameState.Instance != null ? GameState.Instance.fuelPricePerTon : 850f,
                competitorSupply = isPekLhr ? 0.65f : 0.4f,
                demandMultiplier = 1.2f,
                brandEquity = 55f
            };

            var output = RPMCalculator.Calculate(input);
            _rpmResultText.text =
                $"{origin} → {dest}\n" +
                $"综合评定 {output.score} 分（{output.rating}）\n" +
                $"日收入 {output.dailyRevenue:F2} / 成本 {output.dailyCost:F2}\n" +
                $"日利润 {output.dailyProfit:F2} 万美元" +
                (isPekLhr ? "" : "\n（非预设 OD，使用简化参数）");

            _rpmLight.color = output.rating switch
            {
                "绿" => UITheme.Success,
                "黄" => UITheme.Warning,
                _ => UITheme.Danger
            };
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
                    _chatOutput.text = (_chatOutput.text ?? "") + $"\n\n✅ {result.message}";
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
                    _executeHintText.text = result.message;
                }
                ShowErrorPopup(result.message);
                // 航权被拒：保留「执行建议」，可立刻再点重试
            }
        }

        private void ShowErrorPopup(string message)
        {
            _popupText.color = UITheme.Danger;
            _popupText.text = message;
            _popupRoot.SetActive(true);
        }

        private void ShowPopup(string message)
        {
            if (_popupText != null)
                _popupText.color = UITheme.TextPrimary;
            _popupText.text = message;
            _popupRoot.SetActive(true);
        }
    }
}
