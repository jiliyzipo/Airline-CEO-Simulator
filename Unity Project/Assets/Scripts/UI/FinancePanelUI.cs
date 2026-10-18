using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>底栏「财务」：总览 / 预算 / 对冲 / 碳配额 / 融资（定稿 ⑤）。</summary>
    public class FinancePanelUI : MonoBehaviour
    {
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _priceText;
        private TextMeshProUGUI _historyText;
        private TextMeshProUGUI _lockPreviewText;
        private TextMeshProUGUI _hedgeListText;
        private TextMeshProUGUI _financeHintText;
        private TextMeshProUGUI _loanListText;
        private TextMeshProUGUI _toastText;
        private HedgeTutorialUI _tutorialUi;
        private GameObject _homeRoot;
        private GameObject _workspaceRoot;
        private TextMeshProUGUI _summaryText;
        private TextMeshProUGUI _eventFeedText;
        private readonly Image[] _tabImgs = new Image[6];
        private GameObject _budgetRoot;
        private GameObject _carbonRoot;
        private Transform _budgetBody;
        private TextMeshProUGUI _budgetToast;
        private TextMeshProUGUI _carbonHubText;
        private TextMeshProUGUI _carbonToast;
        private GameObject _hedgeColGo;
        private GameObject _loanColGo;
        private GameObject _macroRoot;
        private GameObject _detailRoot;
        private TextMeshProUGUI _taxText;
        private TextMeshProUGUI _insuranceText;
        private TextMeshProUGUI _orderText;
        private TextMeshProUGUI _carbonText;
        private MacroEconomyUI _macroEconomyUi;

        #region Build

        public static FinancePanelUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Finance", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<FinancePanelUI>();
            ui.BuildContent(panel.transform);
            return ui;
        }

        /// <summary>从财务部打开 L3/L4/L5 FIN 子页（叠在内容层）。</summary>
        public static void OpenFinancePage(string pageId)
        {
            var nav = FindFirstObjectByType<PrefabNavIntegration>();
            if (nav != null)
            {
                nav.OpenSubPage(pageId);
                return;
            }

            var router = NavigationRouter.Instance;
            if (router != null)
                router.Navigate(pageId, replaceCurrent: false);
            else
                Debug.LogWarning("[FIN] NavigationRouter 未就绪：" + pageId);
        }

        public void BindTutorial(HedgeTutorialUI tutorial) => _tutorialUi = tutorial;

        private void BuildContent(Transform panel)
        {
            UIFactory.Label(panel, "Title", "财务", 20, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.94f), new Vector2(0.10f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            string[] tabLabels = { "总览", "预算", "对冲", "碳配额", "融资", "宏观" };
            UnityEngine.Events.UnityAction[] tabActions =
            {
                ShowHome, ShowBudget, ShowHedge, ShowCarbon, ShowLoan, ShowMacro
            };
            var bar = UIFactory.Panel(panel, "FinTabs",
                new UIFactory.Anchor(new Vector2(0.11f, 0.935f), new Vector2(0.98f, 0.995f), Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.10f, 0.18f, 0.55f));
            bar.GetComponent<Image>().raycastTarget = false;
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                float x0 = i / 6f;
                var btn = UIFactory.Button(bar.transform, "FT" + i, tabLabels[i],
                    new UIFactory.Anchor(new Vector2(x0 + 0.008f, 0.10f), new Vector2(x0 + 0.158f, 0.90f),
                        Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, tabActions[idx], 11);
                _tabImgs[i] = btn.GetComponent<Image>();
            }

            BuildHome(panel);
            BuildWorkspace(panel);
            BuildBudgetHub(panel);
            BuildCarbonHub(panel);
            BuildMacro(panel);
            BuildDetail(panel);
            ShowHome();
        }

        private void BuildHome(Transform panel)
        {
            _homeRoot = UIFactory.Panel(panel, "FinanceHome",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.92f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var homeImg = _homeRoot.GetComponent<Image>();
            if (homeImg != null)
            {
                homeImg.color = new Color(0, 0, 0, 0);
                homeImg.raycastTarget = false;
            }

            var summaryBox = UIFactory.Panel(_homeRoot.transform, "SummaryBox",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(0.48f, 1f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(summaryBox, UITheme.Border, new Vector2(1f, -1f));
            UIFactory.Label(summaryBox.transform, "SumTitle", "公司财务概况", 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.93f), new Vector2(0.96f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            _summaryText = UIFactory.Label(summaryBox.transform, "SumBody", "", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _summaryText.textWrappingMode = TextWrappingModes.Normal;
            _summaryText.overflowMode = TextOverflowModes.Truncate;
            _summaryText.lineSpacing = 6f;
            // Keep the six CEO navigation tabs; expose existing segmentation from its overview.
            _summaryText.rectTransform.anchorMin = new Vector2(0.04f, 0.11f);
            UIFactory.Button(summaryBox.transform, "OpenDetail", "税务、保险与订单明细",
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, ShowDetail, 12);

            var feedBox = UIFactory.Panel(_homeRoot.transform, "EventFeed",
                new UIFactory.Anchor(new Vector2(0.52f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(feedBox, UITheme.Border, new Vector2(1f, -1f));
            UIFactory.Label(feedBox.transform, "FeedTitle", "事项动态", 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.93f), new Vector2(0.96f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            _eventFeedText = UIFactory.Label(feedBox.transform, "FeedBody", "", 13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _eventFeedText.textWrappingMode = TextWrappingModes.Normal;
            _eventFeedText.overflowMode = TextOverflowModes.Truncate;
            _eventFeedText.lineSpacing = 4f;
        }

        private void BuildWorkspace(Transform panel)
        {
            _workspaceRoot = UIFactory.Panel(panel, "FinanceWorkspace",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var wsImg = _workspaceRoot.GetComponent<Image>();
            if (wsImg != null)
            {
                wsImg.color = new Color(0, 0, 0, 0);
                wsImg.raycastTarget = false;
            }

            UIFactory.Label(_workspaceRoot.transform, "Tip",
                "财务总监将方案发到收件箱，由您在邮件内批复。本页只列示账目。",
                12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.0f, 0.94f), new Vector2(0.70f, 1f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            _statusText = UIFactory.Label(_workspaceRoot.transform, "Status", "", 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.0f, 0.88f), new Vector2(1f, 0.94f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var left = UIFactory.Panel(_workspaceRoot.transform, "HedgeCol",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(0.49f, 0.87f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(left, UITheme.Border, new Vector2(1f, -1f));
            _hedgeColGo = left;
            BuildHedgeColumn(left.transform);

            var right = UIFactory.Panel(_workspaceRoot.transform, "LoanCol",
                new UIFactory.Anchor(new Vector2(0.51f, 0f), new Vector2(1f, 0.87f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(right, UITheme.Border, new Vector2(1f, -1f));
            _loanColGo = right;
            BuildLoanColumn(right.transform);

            _toastText = UIFactory.Label(_workspaceRoot.transform, "Toast", "", 13, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.15f, 0.0f), new Vector2(0.85f, 0.04f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            _workspaceRoot.SetActive(false);
        }

        private void BuildHedgeColumn(Transform col)
        {
            UIFactory.Label(col, "HTitle", "燃油对冲", 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.945f), new Vector2(0.96f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);

            _priceText = UIFactory.Label(col, "Price", "当前油价：$850 / 吨", 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.90f), new Vector2(0.96f, 0.945f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            _historyText = UIFactory.Label(col, "Hist", "走势：—", 10, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.855f), new Vector2(0.96f, 0.90f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            _lockPreviewText = UIFactory.Label(col, "Brief",
                "财务总监按市况拟订对冲方案并发函。开仓、改仓均在收件箱批复。",
                13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.42f), new Vector2(0.96f, 0.845f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _lockPreviewText.textWrappingMode = TextWrappingModes.Normal;

            _hedgeListText = UIFactory.Label(col, "HList", "合约：无", 13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.40f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _hedgeListText.textWrappingMode = TextWrappingModes.Normal;
        }

        private void BuildLoanColumn(Transform col)
        {
            UIFactory.Label(col, "LTitle", "紧急融资", 18, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.9f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.Warning, FontStyles.Bold);

            _financeHintText = UIFactory.Label(col, "Hint", "", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.72f), new Vector2(0.96f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            _loanListText = UIFactory.Label(col, "LoanList", "贷款：无", 12, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.26f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
        }

        private void BuildMacro(Transform panel)
        {
            _macroRoot = UIFactory.Panel(panel, "FinanceMacro",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var mImg = _macroRoot.GetComponent<Image>();
            if (mImg != null)
            {
                mImg.color = new Color(0, 0, 0, 0);
                mImg.raycastTarget = false;
            }

            _macroEconomyUi = MacroEconomyUI.Build(_macroRoot.transform);
            _macroRoot.SetActive(false);
        }

        private void BuildDetail(Transform panel)
        {
            _detailRoot = UIFactory.Panel(panel, "FinanceDetail",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.92f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var dImg = _detailRoot.GetComponent<Image>();
            if (dImg != null)
            {
                dImg.color = new Color(0, 0, 0, 0);
                dImg.raycastTarget = false;
            }

            // 2×2 四宫格：税务 / 航空保险 / PDP 订单簿 / 碳合规
            var tax = BuildDetailCard(_detailRoot.transform, "TaxCard", "税务与补贴",
                new UIFactory.Anchor(new Vector2(0f, 0.5f), new Vector2(0.49f, 1f), Vector2.zero, Vector2.zero));
            _taxText = UIFactory.ScrollableLabel(tax, "TaxBody",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, 13);

            var ins = BuildDetailCard(_detailRoot.transform, "InsuranceCard", "航空保险",
                new UIFactory.Anchor(new Vector2(0.51f, 0.5f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero));
            _insuranceText = UIFactory.ScrollableLabel(ins, "InsuranceBody",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, 13);

            var ord = BuildDetailCard(_detailRoot.transform, "OrderCard", "PDP 订单簿",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(0.49f, 0.49f), Vector2.zero, Vector2.zero));
            _orderText = UIFactory.ScrollableLabel(ord, "OrderBody",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, 13);

            var carbon = BuildDetailCard(_detailRoot.transform, "CarbonCard", "碳合规（EU ETS + CORSIA）",
                new UIFactory.Anchor(new Vector2(0.51f, 0f), new Vector2(1f, 0.49f), Vector2.zero, Vector2.zero));
            _carbonText = UIFactory.ScrollableLabel(carbon, "CarbonBody",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, 13);

            _detailRoot.SetActive(false);
        }

        void BuildBudgetHub(Transform panel)
        {
            _budgetRoot = UIFactory.Panel(panel, "FinanceBudget",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.92f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var img = _budgetRoot.GetComponent<Image>();
            if (img != null) { img.color = new Color(0, 0, 0, 0); img.raycastTarget = false; }

            UIFactory.Label(_budgetRoot.transform, "Hint",
                "各部门预算（万美元）· 草案由财务总监发函，您在收件箱批复后提交董事会",
                12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.92f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var host = UIFactory.Panel(_budgetRoot.transform, "BudgetBody",
                new UIFactory.Anchor(new Vector2(0.01f, 0.08f), new Vector2(0.99f, 0.90f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(host, UITheme.Border, new Vector2(1f, -1f));
            _budgetBody = host.transform;

            _budgetToast = UIFactory.Label(_budgetRoot.transform, "BToast", "", 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.01f), new Vector2(0.98f, 0.07f), Vector2.zero, Vector2.zero),
                UITheme.Warning);
            _budgetRoot.SetActive(false);
        }

        void BuildCarbonHub(Transform panel)
        {
            _carbonRoot = UIFactory.Panel(panel, "FinanceCarbon",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.92f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var img = _carbonRoot.GetComponent<Image>();
            if (img != null) { img.color = new Color(0, 0, 0, 0); img.raycastTarget = false; }

            var box = UIFactory.Panel(_carbonRoot.transform, "CarbonBox",
                new UIFactory.Anchor(new Vector2(0.01f, 0.12f), new Vector2(0.99f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(box, UITheme.Border, new Vector2(1f, -1f));
            UIFactory.Label(box.transform, "CTitle", "碳配额（EU ETS + CORSIA）", 16,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.88f), new Vector2(0.70f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            _carbonHubText = UIFactory.Label(box.transform, "CBody", "", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _carbonHubText.textWrappingMode = TextWrappingModes.Normal;

            _carbonToast = UIFactory.Label(_carbonRoot.transform, "CToast", "", 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.01f), new Vector2(0.98f, 0.10f), Vector2.zero, Vector2.zero),
                UITheme.Warning);
            _carbonRoot.SetActive(false);
        }

        static void StretchCol(GameObject go, bool full)
        {
            if (go == null) return;
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            if (full)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = new Vector2(1f, 0.87f);
            }
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Transform BuildDetailCard(Transform parent, string name, string title, UIFactory.Anchor anchor)
        {
            var box = UIFactory.Panel(parent, name, anchor, UITheme.PanelDeep);
            UIFactory.AddBorder(box, UITheme.Border, new Vector2(1f, -1f));
            UIFactory.Label(box.transform, name + "_Title", title, 15, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.88f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            return box.transform;
        }

        #endregion

        #region Open / Tutorial / Home

        /// <summary>导航切入财务：只刷新总览，不自动弹燃油提醒。</summary>
        public void OnOpened()
        {
            ShowHome();
            Refresh();
        }

        void HideContent()
        {
            if (_homeRoot != null) _homeRoot.SetActive(false);
            if (_workspaceRoot != null) _workspaceRoot.SetActive(false);
            if (_macroRoot != null) _macroRoot.SetActive(false);
            if (_detailRoot != null) _detailRoot.SetActive(false);
            if (_budgetRoot != null) _budgetRoot.SetActive(false);
            if (_carbonRoot != null) _carbonRoot.SetActive(false);
        }

        private void ShowHome()
        {
            HideContent();
            if (_homeRoot != null) _homeRoot.SetActive(true);
            SetTabVisual(0);
            RefreshHome();
        }

        public void ShowBudget()
        {
            HideContent();
            if (_budgetRoot != null) _budgetRoot.SetActive(true);
            SetTabVisual(1);
            RebuildBudgetHub();
        }

        public void ShowHedge()
        {
            HideContent();
            if (_workspaceRoot != null) _workspaceRoot.SetActive(true);
            if (_hedgeColGo != null) _hedgeColGo.SetActive(true);
            if (_loanColGo != null) _loanColGo.SetActive(false);
            StretchCol(_hedgeColGo, true);
            SetTabVisual(2);
            Refresh();
        }

        public void ShowCarbon()
        {
            HideContent();
            if (_carbonRoot != null) _carbonRoot.SetActive(true);
            SetTabVisual(3);
            RefreshCarbonHub();
        }

        public void ShowLoan()
        {
            HideContent();
            if (_workspaceRoot != null) _workspaceRoot.SetActive(true);
            if (_hedgeColGo != null) _hedgeColGo.SetActive(false);
            if (_loanColGo != null) _loanColGo.SetActive(true);
            StretchCol(_loanColGo, true);
            SetTabVisual(4);
            Refresh();
        }

        /// <summary>旧入口：对冲页。</summary>
        void ShowWorkspace() => ShowHedge();

        private void ShowMacro()
        {
            HideContent();
            if (_macroRoot != null) _macroRoot.SetActive(true);
            SetTabVisual(5);
            if (_macroEconomyUi != null) _macroEconomyUi.Refresh();
        }

        private void ShowDetail()
        {
            HideContent();
            if (_detailRoot != null) _detailRoot.SetActive(true);
            SetTabVisual(3);
            RefreshDetail();
        }

        private void SetTabVisual(int activeIndex)
        {
            for (int i = 0; i < _tabImgs.Length; i++)
            {
                if (_tabImgs[i] == null) continue;
                _tabImgs[i].color = i == activeIndex ? UITheme.Accent : UITheme.PanelDeep;
                var h = _tabImgs[i].GetComponent<UIHoverBrighten>();
                if (h != null) h.RefreshBase();
            }
        }

        private void ShowTutorial()
        {
            if (_tutorialUi == null)
                _tutorialUi = FindFirstObjectByType<HedgeTutorialUI>(FindObjectsInactive.Include);
            if (_tutorialUi == null)
            {
                Debug.LogWarning("[Finance] HedgeTutorialUI 未找到");
                return;
            }

            _tutorialUi.Show(OnTutorialTrySafe, OnTutorialDiy);
        }

        private void OnTutorialTrySafe()
        {
            ShowWorkspace();
            InboxManager.MaybeOfferFuelHedge(GameState.Instance);
            Toast("保本方案已由财务总监发到收件箱，请在邮件内批复。");
            RefreshHome();
        }

        private void OnTutorialDiy()
        {
            ShowWorkspace();
            InboxManager.MaybeOfferFuelHedge(GameState.Instance);
            Toast("对冲方案将发到收件箱，本页只列示合约。");
            RefreshHome();
        }

        private void RefreshHome()
        {
            var gs = GameState.Instance;
            if (gs == null || _summaryText == null) return;

            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            var credit = LoanManager.GetCreditRating(gs);
            int hedges = gs.hedgeContracts != null ? gs.hedgeContracts.Count : 0;
            int loans = gs.activeLoans != null ? gs.activeLoans.Count : 0;
            float burn = gs.EffectiveMonthlyBurn;
            float rev = gs.monthlyRevenue;
            float wacc = FinanceEconomics.Wacc(credit, gs.economicPhase);
            FinanceManager.Instance?.EnsureDefaultBudget(gs);
            float budget = FinanceManager.BudgetTotal(gs);
            float carbon = FinanceManager.CarbonLiability(gs);

            _summaryText.text =
                $"现金　{gs.cash:F0} 万美元\n" +
                $"月收入　约 {rev:F0} 万　·　净月烧钱　{burn:F0} 万\n" +
                $"过渡业务　月 +{gs.FleetMonthlyTransitionIncome:F0} 万（{gs.TransitionLeasedCount} 架在租）\n" +
                $"{CashRunnelCalculator.FormatPlayerLabel(runway)}\n" +
                $"状态　{CashRunnelCalculator.FormatWarningPlain(runway)}\n\n" +
                $"航油　${gs.fuelPricePerTon:F0} / 吨\n" +
                $"汇率　1 美元 = {AirlineCEO.Logic.FxEconomics.RateOf(gs):F2} 元　·　累计汇兑 {(gs.cumulativeFxPnlWan >= 0f ? "+" : "")}{gs.cumulativeFxPnlWan:F0} 万\n" +
                $"对冲合约　{hedges} 份　·　贷款/增资　{loans} 笔\n" +
                $"信用评级　{LoanManager.CreditLabel(credit)}　·　WACC {wacc:F1}%\n" +
                $"年度预算　{budget:F0} 万（{(gs.budgetApproved ? "已批准" : (gs.budgetSubmitted ? "待批准" : "未提交"))}）\n" +
                $"碳负债　{carbon:F1} 万　·　PDP 应付 {gs.pdpPayableWan:F0} 万\n" +
                $"董事会满意度　{gs.boardSatisfaction:F0}\n" +
                $"准点率　{gs.onTimePercent:F0}%　·　客座率　{gs.loadFactorPercent:F0}%\n" +
                $"品牌认知　{gs.brandAwareness:F0}\n" +
                $"ERP 提示：财务数据 T−1 更新（明细见 ERP 台账）";

            DispatchFinanceMails(gs);
            RefreshEventFeed(gs, runway, hedges);
        }

        static void DispatchFinanceMails(GameState gs)
        {
            InboxManager.MaybeOfferFuelHedge(gs);
            InboxManager.MaybeOfferAnnualBudget(gs);
            InboxManager.MaybeOfferCarbonBuy(gs);
            InboxManager.MaybeOfferFinanceCrisis(gs);
        }

        static string MailProgress(InboxMail m)
        {
            if (m == null) return "尚未发函";
            if (InboxManager.IsOpen(m)) return "财务总监已发函，待收件箱批复";
            if (m.status == "approved") return "已获批复";
            if (m.status == "rejected") return "已搁置";
            return m.status ?? "—";
        }

        void RefreshEventFeed(GameState gs, float runway, int hedges)
        {
            if (_eventFeedText == null) return;
            var sb = new StringBuilder();

            var oilMail = InboxManager.FindLatestProgress(gs, "油价");
            if (oilMail != null)
            {
                sb.AppendLine("航油价格");
                sb.AppendLine($"　{oilMail.subject}");
                sb.AppendLine($"　现价 ${gs.fuelPricePerTon:F0} / 吨 · 通报已发出");
                sb.AppendLine();
            }

            var hedgeMail = InboxManager.FindLatestAction(gs, InboxManager.FuelHedgeAction);
            sb.AppendLine("燃油对冲");
            if (hedges > 0)
            {
                sb.AppendLine($"　在册 {hedges} 份合约，按季结算");
                sb.AppendLine($"　{MailProgress(hedgeMail)}");
            }
            else
            {
                sb.AppendLine("　尚无合约");
                sb.AppendLine($"　{MailProgress(hedgeMail)}");
            }
            sb.AppendLine();

            var settleMail = InboxManager.FindLatestProgress(gs, "结算");
            if (settleMail != null)
            {
                sb.AppendLine("对冲结算");
                sb.AppendLine($"　{settleMail.subject}");
                sb.AppendLine("　回执已进收件箱");
                sb.AppendLine();
            }

            var budgetMail = InboxManager.FindLatestAction(gs, InboxManager.AnnualBudgetAction);
            sb.AppendLine("年度预算");
            sb.AppendLine($"　合计 {FinanceManager.BudgetTotal(gs):F0} 万 · {MailProgress(budgetMail)}");
            sb.AppendLine();

            var carbonMail = InboxManager.FindLatestAction(gs, InboxManager.CarbonBuyAction);
            sb.AppendLine("碳配额");
            sb.AppendLine($"　负债 {FinanceManager.CarbonLiability(gs):F1} 万 · {MailProgress(carbonMail)}");
            sb.AppendLine();

            var crisis = InboxManager.FindLatestAction(gs, "finance_crisis");
            if (crisis != null || runway < 6f)
            {
                sb.AppendLine("流动性");
                sb.AppendLine($"　跑道约 {runway:F1} 个月 · {CashRunnelCalculator.FormatWarningPlain(runway)}");
                sb.AppendLine($"　{MailProgress(crisis)}");
                sb.AppendLine();
            }

            if (sb.Length == 0)
                sb.Append("暂无事项动态。");
            _eventFeedText.text = sb.ToString().TrimEnd();
        }

        void RebuildBudgetHub()
        {
            if (_budgetBody == null) return;
            for (int i = _budgetBody.childCount - 1; i >= 0; i--)
            {
                var c = _budgetBody.GetChild(i);
                if (c.name == "Border") continue;
                Destroy(c.gameObject);
            }

            var gs = GameState.Instance;
            if (gs == null)
            {
                UIFactory.Label(_budgetBody, "Empty", "游戏未就绪", 14, TextAlignmentOptions.Center, UIFactory.Fill, UITheme.TextMuted);
                return;
            }
            FinanceManager.Instance?.EnsureDefaultBudget(gs);
            DispatchFinanceMails(gs);

            float y = 0.88f;
            if (gs.budgetItems != null)
            {
                for (int i = 0; i < gs.budgetItems.Count; i++)
                {
                    var item = gs.budgetItems[i];
                    string deptId = item.deptId;
                    UIFactory.Label(_budgetBody, "D" + i, item.deptName, 14, TextAlignmentOptions.MidlineLeft,
                        new UIFactory.Anchor(new Vector2(0.04f, y), new Vector2(0.40f, y + 0.08f), Vector2.zero, Vector2.zero),
                        UITheme.TextPrimary);
                    UIFactory.Label(_budgetBody, "A" + i, item.amountWan.ToString("F0") + " 万", 14,
                        TextAlignmentOptions.MidlineLeft,
                        new UIFactory.Anchor(new Vector2(0.55f, y), new Vector2(0.96f, y + 0.08f), Vector2.zero, Vector2.zero),
                        UITheme.Success, FontStyles.Bold);
                    y -= 0.09f;
                }
            }

            float total = FinanceManager.BudgetTotal(gs);
            float low = FinanceEconomics.BudgetBandLow(gs.cash);
            float high = FinanceEconomics.BudgetBandHigh(gs.cash);
            int votes = FinanceManager.EstimateVotes(gs);
            UIFactory.Label(_budgetBody, "Tot",
                $"合计 {total:F0} 万 · 建议 {low:F0}～{high:F0} 万 · 预估 {votes}/{FinanceEconomics.BoardSeats} 票" +
                (gs.budgetApproved ? " · 已批准" : gs.budgetSubmitted ? " · 已提交董事会" : " · 请示已发收件箱"),
                13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.20f), Vector2.zero, Vector2.zero),
                UITheme.Warning, FontStyles.Bold);
        }

        void RefreshCarbonHub()
        {
            var gs = GameState.Instance;
            if (_carbonHubText == null) return;
            if (gs == null)
            {
                _carbonHubText.text = "游戏未就绪";
                return;
            }
            FinanceManager.Instance?.RefreshEmissions(gs);
            float liability = FinanceManager.CarbonLiability(gs);
            var sb = new StringBuilder();
            sb.AppendLine($"累计排放 {gs.carbonEmissionsTons:F0} 吨 CO2");
            sb.AppendLine($"已购配额 {gs.carbonPurchasedQuotaTons:F0} 吨 · 碳价参考 ${gs.carbonPricePerTon:F0}/吨");
            sb.AppendLine($"碳负债 {liability:F1} 万");
            sb.AppendLine();
            sb.AppendLine("航线碳成本（示意）");
            int shown = 0;
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count && shown < 8; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    float cost = FinanceEconomics.CarbonCostPerRouteYearWan(
                        r.distanceKm, r.aircraftType, r.weeklyFlights, gs.carbonPricePerTon);
                    sb.AppendLine($"  {r.origin}-{r.destination}  {cost:F2} 万/年");
                    shown++;
                }
            }
            if (shown == 0) sb.AppendLine("  暂无航线");
            sb.AppendLine();
            sb.Append("采购请示由财务总监发到收件箱，本页不直接下单。");
            _carbonHubText.text = sb.ToString();
            DispatchFinanceMails(gs);
        }

        #endregion

        #region Refresh

        public void Refresh()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            DispatchFinanceMails(gs);

            if (_homeRoot != null && _homeRoot.activeSelf)
                RefreshHome();
            if (_detailRoot != null && _detailRoot.activeSelf)
                RefreshDetail();
            if (_budgetRoot != null && _budgetRoot.activeSelf)
                RebuildBudgetHub();
            if (_carbonRoot != null && _carbonRoot.activeSelf)
                RefreshCarbonHub();

            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            var credit = LoanManager.GetCreditRating(gs);
            if (_statusText != null)
            {
                _statusText.text =
                    $"{CashRunnelCalculator.FormatPlayerLabel(runway)} · {CashRunnelCalculator.FormatWarningPlain(runway)}" +
                    $" ｜ 信用 {LoanManager.CreditLabel(credit)} ｜ 现金 {gs.cash:F0} 万";
            }

            if (_priceText != null)
                _priceText.text = $"当前油价：${gs.fuelPricePerTon:F0} / 吨";

            if (_historyText != null)
                _historyText.text = "走势：" + FormatHistory(gs.fuelPriceHistory);

            RefreshHedgeList(gs);
            RefreshFinanceSide(gs, runway);
            LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
        }

        private static string FormatHistory(List<float> hist)
        {
            if (hist == null || hist.Count == 0) return "—";
            var sb = new StringBuilder();
            int start = Mathf.Max(0, hist.Count - 8);
            for (int i = start; i < hist.Count; i++)
            {
                if (i > start) sb.Append(" → ");
                sb.Append($"${hist[i]:F0}");
            }
            return sb.ToString();
        }

        private void RefreshHedgeList(GameState gs)
        {
            if (_hedgeListText == null) return;
            if (gs.hedgeContracts == null || gs.hedgeContracts.Count == 0)
            {
                _hedgeListText.text = "合约：无";
                return;
            }

            var sb = new StringBuilder("合约：\n");
            for (int i = 0; i < gs.hedgeContracts.Count; i++)
            {
                var c = gs.hedgeContracts[i];
                string t = string.IsNullOrEmpty(c.lockType) ? HedgeLockTypes.Fixed : c.lockType;
                string extra = t switch
                {
                    HedgeLockTypes.Collar => $"[{c.floorPrice:F0}~{c.capPrice:F0}]",
                    HedgeLockTypes.Call => $"权金{c.premiumPaid:F1}万",
                    _ => $"锁${c.lockPrice:F0}"
                };
                sb.AppendLine(
                    $"· {HedgeLockTypes.DisplayName(t)} {c.hedgeRatio * 100:F0}%/{c.termMonths}月 {extra} 余{c.monthsRemaining}月");
            }
            _hedgeListText.text = sb.ToString().TrimEnd();
        }

        private void RefreshFinanceSide(GameState gs, float runway)
        {
            bool unlock = LoanManager.IsFinanceUnlocked(gs);

            if (_financeHintText != null)
            {
                if (!unlock)
                {
                    _financeHintText.text =
                        "暂无融资需求。现金跑道告急时，财务总监会发函请示贷款、过桥或增资。\n" +
                        $"当前信用评级：{LoanManager.CreditLabel(LoanManager.GetCreditRating(gs))}。";
                }
                else
                {
                    _financeHintText.text =
                        $"⚠ 现金告急（跑道 {runway:F1} 月）· 信用 {LoanManager.CreditLabel(LoanManager.GetCreditRating(gs))}\n" +
                        "融资方案已发收件箱，请在邮件内批复。本页只列示在贷。";
                }
            }

            if (_loanListText == null) return;
            if (gs.activeLoans == null || gs.activeLoans.Count == 0)
            {
                _loanListText.text = "贷款：无";
                return;
            }

            var sb = new StringBuilder("贷款：\n");
            for (int i = 0; i < gs.activeLoans.Count; i++)
            {
                var L = gs.activeLoans[i];
                if (L == null) continue;
                if (L.type == "equity")
                    sb.AppendLine($"· 增资 {L.principal:F0} 万（{L.createdAt}）");
                else
                    sb.AppendLine(
                        $"· {L.id} 余本 {L.remainingPrincipal:F0} · 月供 {L.monthlyPayment:F1} · 余 {L.monthsRemaining}月");
            }
            _loanListText.text = sb.ToString().TrimEnd();
        }

        #endregion

        #region 财务细分展示

        private void RefreshDetail()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            // 税务
            if (_taxText != null)
            {
                float rate = TaxEconomics.CorporateTaxRateForHub(gs.hubIata);
                _taxText.text =
                    $"枢纽 {gs.hubIata} · 企业所得税 {rate * 100f:F0}%\n" +
                    $"本财年已缴　{gs.yearTaxPaidWan:F0} 万\n" +
                    $"累计已缴　{gs.cumulativeTaxPaidWan:F0} 万\n" +
                    $"转移定价调查风险（基准 5%）\n" +
                    $"补贴 {gs.routeSubsidies?.Count ?? 0} 条 · 依赖警示见日志";
            }

            // 航空保险
            if (_insuranceText != null)
            {
                float fleetValue = FleetManager.FleetTotalValue(gs);
                float rate = InsuranceEconomics.AnnualRate(gs.safetyRating, gs.insuranceCoverage);
                float q = InsuranceEconomics.QuarterlyPremiumWan(fleetValue, gs.safetyRating, gs.insuranceCoverage);
                _insuranceText.text =
                    $"档位　{CoverageLabel(gs.insuranceCoverage)}\n" +
                    $"安全评级 {gs.safetyRating} · 年费率 {rate * 100f:F1}%\n" +
                    $"机队价值　{fleetValue:F0} 万\n" +
                    $"本季保费　{q:F0} 万\n" +
                    $"累计保费 {gs.cumulativeInsurancePremiumWan:F0} 万\n" +
                    $"累计理赔 {gs.cumulativeInsuranceClaimWan:F0} 万";
            }

            // PDP 订单簿
            if (_orderText != null)
            {
                var orders = gs.aircraftOrders;
                var sb = new StringBuilder();
                if (orders == null || orders.Count == 0)
                {
                    sb.Append("暂无采购订单。\n下单后此处展示交付时间轴与 PDP 付款节奏。");
                }
                else
                {
                    for (int i = 0; i < orders.Count; i++)
                    {
                        var o = orders[i];
                        if (o == null) continue;
                        float paid = o.depositPaidWan + o.pdp24PaidWan + o.pdp12PaidWan + o.balancePaidWan;
                        string state = o.delivered ? "已交付" : o.cancelled ? "已取消" :
                            $"交付 {o.deliveryYear}年Q{o.deliveryQuarter}";
                        sb.AppendLine($"· {o.orderId} {o.typeId}×{o.count}");
                        sb.AppendLine($"　总价 {o.totalWan:F0} 万 · 已付 {paid:F0} 万 · {state}");
                    }
                }
                sb.AppendLine();
                sb.Append($"PDP 应付总额　{gs.pdpPayableWan:F0} 万");
                _orderText.text = sb.ToString().TrimEnd();
            }

            // 碳合规
            if (_carbonText != null)
            {
                var cc = gs.carbonCompliance ?? new CarbonComplianceState();
                int calYear = CarbonEconomics.CalendarYear(gs.year);
                string corsiaNote = CarbonEconomics.CorsiaMandatory(calYear) ? "强制期" : "自愿期";
                _carbonText.text =
                    $"日历 {calYear} · 碳价 {gs.carbonPricePerTon:F0} 美元/吨\n" +
                    $"EU ETS　排放 {cc.etsEmissionsTons:F0} t · 需购 {cc.etsRequiredTons:F0} t（免费 {CarbonEconomics.EtsFreeQuotaRatio(calYear) * 100f:F0}%）\n" +
                    $"CORSIA　排放 {cc.corsiaEmissionsTons:F0} t · 需购 {cc.corsiaRequiredTons:F0} t（{corsiaNote}）\n" +
                    $"配额成本　ETS {cc.etsCostWan:F1} + CORSIA {cc.corsiaCostWan:F1} 万\n" +
                    $"已购配额 {gs.carbonPurchasedQuotaTons:F0} t\n" +
                    $"年结自动补缴未覆盖配额（见年度结算日志）";
            }
        }

        private static string CoverageLabel(InsuranceCoverage coverage) => coverage switch
        {
            InsuranceCoverage.Full => "全险",
            InsuranceCoverage.Basic => "基本险",
            _ => "自保"
        };

        #endregion

        #region Actions

        private void Toast(string msg)
        {
            if (_toastText != null) _toastText.text = msg ?? "";
            Debug.Log("[Finance] " + msg);
        }

        #endregion
    }
}
