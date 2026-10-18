using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// MKT 市场销售部 L1（n14）：总览（KPI + 航线收益排行 + 子页入口）/ RPM 评估 / 竞争态势。
    /// </summary>
    public class MarketPanelUI : MonoBehaviour
    {
        private GameObject _homeRoot;
        private GameObject _rpmRoot;
        private GameObject _compRoot;
        private Transform _rankingRoot;
        private TextMeshProUGUI _kpiText;
        private TextMeshProUGUI _secondaryKpiText;
        private CompetitionUI _competitionUi;

        private TMP_InputField _originInput;
        private TMP_InputField _destInput;
        private TextMeshProUGUI _rpmResultText;
        private Image _rpmLight;

        private Button _tabHomeBtn;
        private Button _tabRpmBtn;
        private Button _tabCompBtn;
        private Image _tabHomeImg;
        private Image _tabRpmImg;
        private Image _tabCompImg;

        private readonly List<GameObject> _rankingRows = new List<GameObject>();

        public static MarketPanelUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Market", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<MarketPanelUI>();
            ui.BuildContent(panel.transform);
            return ui;
        }

        /// <summary>从市场部打开 L2/L3/L4 MKT 子页（叠在内容层）。</summary>
        public static void OpenMktPage(string pageId)
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
                Debug.LogWarning("[MKT] NavigationRouter 未就绪：" + pageId);
        }

        public void OnOpened()
        {
            ShowHome();
            Refresh();
        }

        private void BuildContent(Transform panel)
        {
            UIFactory.Label(panel, "Title", "市场销售部", 20, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.94f), new Vector2(0.24f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _tabHomeBtn = UIFactory.Button(panel, "TabHome", "总览",
                new UIFactory.Anchor(new Vector2(0.26f, 0.94f), new Vector2(0.38f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => ShowTab(0), 12);
            _tabHomeImg = _tabHomeBtn.GetComponent<Image>();
            _tabRpmBtn = UIFactory.Button(panel, "TabRpm", "RPM 评估",
                new UIFactory.Anchor(new Vector2(0.40f, 0.94f), new Vector2(0.54f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => ShowTab(1), 12);
            _tabRpmImg = _tabRpmBtn.GetComponent<Image>();
            _tabCompBtn = UIFactory.Button(panel, "TabComp", "竞争态势",
                new UIFactory.Anchor(new Vector2(0.56f, 0.94f), new Vector2(0.70f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => ShowTab(2), 12);
            _tabCompImg = _tabCompBtn.GetComponent<Image>();

            // 子页快捷入口
            UIFactory.Button(panel, "NavRevenue", "航线收益分析",
                new UIFactory.Anchor(new Vector2(0.72f, 0.94f), new Vector2(0.84f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenMktPage("n44_航线收益分析"), 11);
            UIFactory.Button(panel, "NavPricing", "航班定价",
                new UIFactory.Anchor(new Vector2(0.86f, 0.94f), new Vector2(0.99f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenMktPage("n49_航班定价"), 11);

            BuildHome(panel);
            BuildRpm(panel);
            BuildComp(panel);
            ShowTab(0);
        }

        private void BuildHome(Transform panel)
        {
            _homeRoot = UIFactory.Panel(panel, "MarketHome",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var img = _homeRoot.GetComponent<Image>();
            if (img != null) { img.color = new Color(0, 0, 0, 0); img.raycastTarget = false; }

            // KPI 卡片（收入/成本/净利润/利润率）
            _kpiText = UIFactory.Label(_homeRoot.transform, "Kpi",
                "", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.84f), new Vector2(0.56f, 1.0f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _kpiText.textWrappingMode = TextWrappingModes.Normal;

            // 次级 KPI（客座率/单位收益/品牌净值/市场份额）
            _secondaryKpiText = UIFactory.Label(_homeRoot.transform, "Kpi2",
                "", 13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.58f, 0.84f), new Vector2(1.0f, 1.0f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _secondaryKpiText.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.Label(_homeRoot.transform, "RankTitle", "航线收益排行（点表头排序 · 点行看详情）", 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.80f), new Vector2(0.98f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);

            var listHost = UIFactory.Panel(_homeRoot.transform, "RankingList",
                new UIFactory.Anchor(new Vector2(0.02f, 0.23f), new Vector2(0.98f, 0.80f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            var vlg = listHost.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 6;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            _rankingRoot = listHost.transform;

            // 底部入口条
            UIFactory.Button(_homeRoot.transform, "BtnSeasonal", "🗓 季节性日历",
                new UIFactory.Anchor(new Vector2(0.02f, 0.03f), new Vector2(0.20f, 0.11f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenMktPage("n55_季节性日历"), 12);
            UIFactory.Button(_homeRoot.transform, "BtnFfp", "💳 FFP 常旅客",
                new UIFactory.Anchor(new Vector2(0.22f, 0.03f), new Vector2(0.40f, 0.11f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenMktPage("n33_FFP_常旅客管理"), 12);
            UIFactory.Button(_homeRoot.transform, "BtnWorkshop", "🎁 产品工坊",
                new UIFactory.Anchor(new Vector2(0.42f, 0.03f), new Vector2(0.60f, 0.11f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenMktPage("n34_产品工坊"), 12);
            UIFactory.Button(_homeRoot.transform, "BtnComp2", "📡 竞争雷达",
                new UIFactory.Anchor(new Vector2(0.62f, 0.03f), new Vector2(0.80f, 0.11f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenMktPage("n60_竞争雷达"), 12);
            UIFactory.Button(_homeRoot.transform, "BtnBrand", "🆘 品牌危机公关",
                new UIFactory.Anchor(new Vector2(0.82f, 0.03f), new Vector2(0.98f, 0.11f), Vector2.zero, Vector2.zero),
                UITheme.Danger, () => OpenMktPage("n72_品牌危机公关"), 12);

            // 批次 B 入口（大客户 / 渠道预算 / 竞争态势）
            UIFactory.Button(_homeRoot.transform, "BtnKeyAcct", "🤝 大客户合同",
                new UIFactory.Anchor(new Vector2(0.02f, 0.13f), new Vector2(0.20f, 0.21f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenMktPage("n67_大客户合同"), 12);
            UIFactory.Button(_homeRoot.transform, "BtnChannel", "💸 渠道预算",
                new UIFactory.Anchor(new Vector2(0.22f, 0.13f), new Vector2(0.40f, 0.21f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenMktPage("n68_渠道预算"), 12);
            UIFactory.Button(_homeRoot.transform, "BtnComp29", "竞争态势",
                new UIFactory.Anchor(new Vector2(0.42f, 0.13f), new Vector2(0.62f, 0.21f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => ShowTab(2), 12);
        }

        private void BuildRpm(Transform panel)
        {
            _rpmRoot = UIFactory.Panel(panel, "MarketRpmRoot",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var img = _rpmRoot.GetComponent<Image>();
            if (img != null) img.color = new Color(0, 0, 0, 0);

            UIFactory.Label(_rpmRoot.transform, "Sub", "输入出发/到达城市，评估综合得分与日利润", 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.9f), new Vector2(0.7f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Label(_rpmRoot.transform, "OriginLabel", "出发", 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.76f), new Vector2(0.18f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _originInput = UIFactory.Input(_rpmRoot.transform, "OriginInput", "北京",
                new UIFactory.Anchor(new Vector2(0.2f, 0.76f), new Vector2(0.55f, 0.86f), Vector2.zero, Vector2.zero));

            UIFactory.Label(_rpmRoot.transform, "DestLabel", "到达", 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.62f), new Vector2(0.18f, 0.72f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _destInput = UIFactory.Input(_rpmRoot.transform, "DestInput", "伦敦",
                new UIFactory.Anchor(new Vector2(0.2f, 0.62f), new Vector2(0.55f, 0.72f), Vector2.zero, Vector2.zero));

            UIFactory.Button(_rpmRoot.transform, "AnalyzeButton", "分析航线",
                new UIFactory.Anchor(new Vector2(0.6f, 0.76f), new Vector2(0.82f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.Accent, OnAnalyzeRpm, 16);

            _rpmLight = UIFactory.Panel(_rpmRoot.transform, "RpmLight",
                new UIFactory.Anchor(new Vector2(0.85f, 0.76f), new Vector2(0.94f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted).GetComponent<Image>();

            var resultBox = UIFactory.Panel(_rpmRoot.transform, "ResultBox",
                new UIFactory.Anchor(new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.55f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            _rpmResultText = UIFactory.Label(resultBox.transform, "RpmResult",
                "点击「分析航线」查看综合评定与红绿灯", 17,
                TextAlignmentOptions.TopLeft, UIFactory.Fill, UITheme.TextPrimary);
            var rt = _rpmResultText.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(16, 12);
            rt.offsetMax = new Vector2(-16, -12);

            _rpmRoot.SetActive(false);
        }

        private void BuildComp(Transform panel)
        {
            _competitionUi = CompetitionUI.Build(panel,
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.93f), Vector2.zero, Vector2.zero));
            _compRoot = _competitionUi != null ? _competitionUi.gameObject : null;
            if (_compRoot != null) _compRoot.SetActive(false);
        }

        private void ShowTab(int tab)
        {
            if (_homeRoot != null) _homeRoot.SetActive(tab == 0);
            if (_rpmRoot != null) _rpmRoot.SetActive(tab == 1);
            if (_compRoot != null) _compRoot.SetActive(tab == 2);
            if (tab == 2 && _competitionUi != null) _competitionUi.Refresh();
            SetTabVisual(tab);
        }

        private void ShowHome() => ShowTab(0);

        private void SetTabVisual(int active)
        {
            if (_tabHomeImg != null) { _tabHomeImg.color = active == 0 ? UITheme.Accent : UITheme.PanelDeep; var h = _tabHomeImg.GetComponent<UIHoverBrighten>(); if (h != null) h.RefreshBase(); }
            if (_tabRpmImg != null) { _tabRpmImg.color = active == 1 ? UITheme.Accent : UITheme.PanelDeep; var h = _tabRpmImg.GetComponent<UIHoverBrighten>(); if (h != null) h.RefreshBase(); }
            if (_tabCompImg != null) { _tabCompImg.color = active == 2 ? UITheme.Accent : UITheme.PanelDeep; var h = _tabCompImg.GetComponent<UIHoverBrighten>(); if (h != null) h.RefreshBase(); }
        }

        public void Refresh()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            // 汇总航线收益
            float totalRev = 0f, totalCost = 0f, totalProfit = 0f, raskSum = 0f, caskSum = 0f;
            int n = 0;
            var rows = new List<(RouteData route, MktUiMath.RouteEcon econ)>();
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    var e = MktUiMath.Analyze(r, gs);
                    totalRev += e.weeklyRevenueWan;
                    totalCost += e.weeklyCostWan;
                    totalProfit += e.weeklyProfitWan;
                    raskSum += e.raskUsd;
                    caskSum += e.caskUsd;
                    n++;
                    rows.Add((r, e));
                }
            }

            float margin = totalRev > 0.01f ? totalProfit / totalRev : 0f;
            float avgRask = n > 0 ? raskSum / n : 0f;
            float avgCask = n > 0 ? caskSum / n : 0f;

            if (_kpiText != null)
                _kpiText.text =
                    $"航线总收入　{totalRev:F0} 万美元\n" +
                    $"航线总成本　{totalCost:F0} 万美元\n" +
                    $"航线净利润　{totalProfit:F0} 万美元\n" +
                    $"综合利润率　{margin * 100:F1}%（{(margin >= 0 ? "盈利" : "亏损")}）";

            if (_secondaryKpiText != null)
                _secondaryKpiText.text =
                    $"客座率　{gs.loadFactorPercent:F0}%\n" +
                    $"单位收益 RASK　${avgRask:F3} / 座公里\n" +
                    $"单位成本 CASK　${avgCask:F3} / 座公里\n" +
                    $"品牌净值　{gs.brandAwareness:F0}\n" +
                    $"市场份额　{gs.playerMarketShare * 100:F1}%";

            // 按周净利润降序排序
            rows.Sort((a, b) => b.econ.weeklyProfitWan.CompareTo(a.econ.weeklyProfitWan));
            RebuildRanking(rows);
        }

        private void RebuildRanking(List<(RouteData route, MktUiMath.RouteEcon econ)> rows)
        {
            for (int i = 0; i < _rankingRows.Count; i++)
                if (_rankingRows[i] != null) Destroy(_rankingRows[i]);
            _rankingRows.Clear();
            if (_rankingRoot == null) return;

            if (rows.Count == 0)
            {
                var empty = UIFactory.Label(_rankingRoot, "Empty",
                    "暂无航线 — 先开通航线后，此处展示各航线收益排行。",
                    14, TextAlignmentOptions.Center, UIFactory.Fill, UITheme.TextMuted);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
                _rankingRows.Add(empty.gameObject);
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i].route;
                var e = rows[i].econ;
                string routeId = r.routeId;
                var row = UIFactory.Button(_rankingRoot, "Rank_" + i,
                    $"{i + 1}. {r.origin}—{r.destination}　{r.aircraftType} · {e.totalWeeklyFlights} 班/周\n" +
                    $"收入 {e.weeklyRevenueWan:F0} · 成本 {e.weeklyCostWan:F0} · 净利 {e.weeklyProfitWan:F0} · 客座率 {e.loadFactor * 100:F0}% · {(e.weeklyProfitWan >= 0 ? "↗" : "↘")}",
                    new UIFactory.Anchor(new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                    UITheme.NavIdle, () => { MktContext.SelectedRouteId = routeId; OpenMktPage("n44_航线收益分析"); }, 12);
                var le = row.gameObject.GetComponent<LayoutElement>();
                if (le == null) le = row.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 58;
                le.flexibleWidth = 1;
                _rankingRows.Add(row.gameObject);
            }
        }

        private void OnAnalyzeRpm()
        {
            string origin = _originInput != null ? _originInput.text.Trim() : "";
            string dest = _destInput != null ? _destInput.text.Trim() : "";

            bool isPekLhr =
                (origin.Contains("北京") || origin.ToUpperInvariant().Contains("PEK")) &&
                (dest.Contains("伦敦") || dest.ToUpperInvariant().Contains("LHR") || dest.ToUpperInvariant().Contains("LON"));

            var macro = MacroEconomyManager.Instance;
            float baseDemand = 1.2f;
            if (macro != null)
                baseDemand *= macro.GetDemandMultiplier("leisure");

            var input = new RPMCalculator.RPMInput
            {
                distance = isPekLhr ? 8150f : 4500f,
                fuelPricePerTon = GameState.Instance != null ? GameState.Instance.fuelPricePerTon : 850f,
                competitorSupply = isPekLhr ? 0.65f : 0.4f,
                demandMultiplier = baseDemand,
                brandEquity = GameState.Instance != null ? GameState.Instance.brandAwareness : 55f
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

        private void Toast(string msg) => Debug.Log("[MKT] " + msg);
    }
}
