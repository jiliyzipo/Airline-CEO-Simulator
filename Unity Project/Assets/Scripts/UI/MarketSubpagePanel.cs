using System;
using System.Collections.Generic;
using System.Linq;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>MKT 子页共享上下文（跨页面传参）。</summary>
    public static class MktContext
    {
        public static string SelectedRouteId;
        public static string SelectedFestival = "春节";
        public static string SelectedCabin = CabinClasses.Economy;
        public static string SelectedCompetitorId;   // 批次 B：竞对详情
    }

    /// <summary>
    /// MKT 市场销售部子页（批次 A：收益/定价/季节 + 批次 B：竞争/渠道/品牌）：
    /// n44 航线收益分析 · n49 航班定价 · n45 单航线定价详情 · n46 动态定价算法 ·
    /// n47 收益管理仪表盘 · n48 辅助收入管理 · n50 外部环境分析 · n55 季节性日历 ·
    /// n56 航季对比详情 · n57 旺季策略配置器 · n58 历史趋势分析 · n59 旺季影响预测 ·
    /// n33 FFP 常旅客管理 · n34 产品工坊 ·
    /// n60 竞争雷达 · n61 竞对详情 · n62-n65 竞对标 ·
    /// n67 大客户合同 · n68 渠道预算 · n69 分销 · n70 直销 · n71 渠道ROI ·
    /// n72 品牌危机公关 · n73 舆情监测 · n74 危机响应 · n75 品牌修复。
    /// 运行时构建，导航栈驱动。
    /// </summary>
    public class MarketSubpagePanel : BasePanel
    {
        [SerializeField] string pageKind;

        string _pageKind;
        Transform _content;
        TextMeshProUGUI _toast;

        // 本地编辑态（stepper 选择，提交时回写）
        DynamicPricingSettings _dp;
        AncillarySettings _aux;
        FfpSettings _ffp;
        PeakSeasonPackage _pkg;
        BrandCrisis _brand;         // 批次 B：品牌危机编辑态
        ChannelBudget _channel;     // 批次 B：渠道预算编辑态

        public static MarketSubpagePanel Build(string pageKind, string title)
        {
            var root = UIFactory.Panel(null, "MktSub_" + pageKind, UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.1f, 0.72f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1.2f, -1.2f));
            var cg = card.AddComponent<CanvasGroup>();

            var titleLabel = UIFactory.Label(card.transform, "Title", title, 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.90f), new Vector2(0.78f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.90f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            UIFactory.Button(card.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.20f, 0.10f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var content = UIFactory.Panel(card.transform, "Content",
                new UIFactory.Anchor(new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;

            var toast = UIFactory.Label(card.transform, "Toast", "", 12,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.20f, 0.005f), new Vector2(0.80f, 0.035f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            var panel = root.AddComponent<MarketSubpagePanel>();
            panel.pageKind = pageKind;
            panel._pageKind = pageKind;
            panel._content = content.transform;
            panel._toast = toast;
            panel.AssignRefs(root.GetComponent<RectTransform>(), titleLabel, close, cg);
            panel.BindPage(pageKind, title);
            panel.RefreshBody();
            return panel;
        }

        public override void BindPage(string pageId, string title = null)
        {
            base.BindPage(pageId, title);
            _pageKind = NormalizeKind(pageId);
            pageKind = _pageKind;
            if (titleLabel != null && !string.IsNullOrEmpty(title))
                titleLabel.text = title;
        }

        protected override void Awake()
        {
            if (root == null) root = transform as RectTransform;
            ResolveRefs();
            WireBackButton();
        }

        void OnEnable()
        {
            ResolveRefs();
            WireBackButton();
            RefreshBody();
        }

        public override void Show()
        {
            base.Show();
            ResolveRefs();
            RefreshBody();
        }

        void ResolveRefs()
        {
            if (_content == null)
            {
                var c = transform.Find("Card/Content");
                if (c != null) _content = c;
            }
            if (_toast == null)
            {
                var t = transform.Find("Card/Toast");
                if (t != null) _toast = t.GetComponent<TextMeshProUGUI>();
            }
            if (string.IsNullOrEmpty(_pageKind))
                _pageKind = NormalizeKind(!string.IsNullOrEmpty(pageKind) ? pageKind : PageId);
        }

        public void RefreshBody()
        {
            ResolveRefs();
            if (_content == null) return;
            Clear(_content);

            switch (_pageKind)
            {
                case "n44": RenderRevenueAnalysis(); break;
                case "n49": RenderPricing(); break;
                case "n45": RenderRoutePricingDetail(); break;
                case "n46": RenderDynamicPricing(); break;
                case "n47": RenderRevenueDashboard(); break;
                case "n48": RenderAncillary(); break;
                case "n50": RenderExternalEnv(); break;
                case "n55": RenderSeasonalCalendar(); break;
                case "n56": RenderSeasonCompare(); break;
                case "n57": RenderSeasonConfigurator(); break;
                case "n58": RenderHistoricalTrend(); break;
                case "n59": RenderSeasonImpact(); break;
                case "n33": RenderFfp(); break;
                case "n34": RenderProductWorkshop(); break;
                // 批次 B：竞争/渠道/品牌
                case "n60": RenderCompetitionRadar(); break;
                case "n61": RenderCompetitorDetail(); break;
                case "n62": RenderCompNetwork(); break;
                case "n63": RenderCompProduct(); break;
                case "n64": RenderCompCost(); break;
                case "n65": RenderCompCapital(); break;
                case "n67": RenderKeyAccount(); break;
                case "n68": RenderChannelBudget(); break;
                case "n69": RenderDistribution(); break;
                case "n70": RenderDirect(); break;
                case "n71": RenderChannelRoi(); break;
                case "n72": RenderBrandCrisis(); break;
                case "n73": RenderSentiment(); break;
                case "n74": RenderCrisisResponse(); break;
                case "n75": RenderBrandRepair(); break;
                default: RenderFallback(); break;
            }
            HideCloseXButtons(transform);
        }

        void Toast(string msg)
        {
            if (_toast != null) _toast.text = msg ?? "";
        }

        void Clear(Transform host)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
                Destroy(host.GetChild(i).gameObject);
        }

        TextMeshProUGUI L(string name, string text, float x0, float y0, float x1, float y1,
            int size, Color? color = null, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            return UIFactory.Label(_content, name, text, size, align,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.TextPrimary, bold ? FontStyles.Bold : FontStyles.Normal);
        }

        Button B(string name, string text, float x0, float y0, float x1, float y1,
            Color? color, UnityEngine.Events.UnityAction action, int size = 13)
        {
            return UIFactory.Button(_content, name, text,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.Accent, action, size);
        }

        void OpenPage(string pageId) => MarketPanelUI.OpenMktPage(pageId);

        GameState Gs => GameState.Instance;

        RouteData CurrentRoute()
        {
            var gs = Gs;
            if (gs == null || gs.activeRoutes == null || gs.activeRoutes.Count == 0) return null;
            if (!string.IsNullOrEmpty(MktContext.SelectedRouteId))
            {
                var r = gs.activeRoutes.Find(x => x != null && x.routeId == MktContext.SelectedRouteId);
                if (r != null) return r;
            }
            return gs.activeRoutes[0];
        }

        // —— 通用：聚合所有航线收益 ——
        static (float rev, float cost, float profit) Aggregate(GameState gs)
        {
            float rev = 0f, cost = 0f, profit = 0f;
            if (gs?.activeRoutes == null) return (0, 0, 0);
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var e = MktUiMath.Analyze(gs.activeRoutes[i], gs);
                rev += e.weeklyRevenueWan;
                cost += e.weeklyCostWan;
                profit += e.weeklyProfitWan;
            }
            return (rev, cost, profit);
        }

        // ============================================================
        // n44 航线收益分析
        // ============================================================
        void RenderRevenueAnalysis()
        {
            var gs = Gs;
            if (gs == null) return;
            var agg = Aggregate(gs);
            float margin = agg.rev > 0.01f ? agg.profit / agg.rev : 0f;

            L("K1", $"总收入 {agg.rev:F0} 万", 0.0f, 0.92f, 0.48f, 0.98f, 14, UITheme.Accent, true);
            L("K2", $"总成本 {agg.cost:F0} 万", 0.0f, 0.85f, 0.48f, 0.91f, 14);
            L("K3", $"净利润 {agg.profit:F0} 万", 0.0f, 0.78f, 0.48f, 0.84f, 14, agg.profit >= 0 ? UITheme.Success : UITheme.Danger, true);
            L("K4", $"利润率 {margin * 100:F1}%", 0.0f, 0.71f, 0.48f, 0.77f, 14);

            L("Rank", "航线收益排行（点击行 → 详情）", 0.52f, 0.92f, 1.0f, 0.98f, 13, UITheme.TextMuted, true);

            var routes = gs.activeRoutes ?? new List<RouteData>();
            float y = 0.84f;
            float step = 0.11f;
            int shown = 0;
            for (int i = 0; i < routes.Count && shown < 6; i++)
            {
                var r = routes[i];
                if (r == null) continue;
                var e = MktUiMath.Analyze(r, gs);
                string rid = r.routeId;
                B("Row" + i, $"{r.origin}—{r.destination}  {r.aircraftType} · {e.totalWeeklyFlights}班  净利 {e.weeklyProfitWan:F0}",
                    0.52f, y, 1.0f, y + 0.09f, UITheme.NavIdle, () => { MktContext.SelectedRouteId = rid; RefreshBody(); }, 12);
                y -= step;
                shown++;
            }
            if (shown == 0)
                L("Empty", "暂无航线", 0.52f, 0.84f, 1.0f, 0.90f, 12, UITheme.TextMuted);

            // 详情面板
            var sel = CurrentRoute();
            L("Detail", "详情（选中航线）", 0.0f, 0.60f, 1.0f, 0.67f, 13, UITheme.Accent, true);
            if (sel != null)
            {
                var e = MktUiMath.Analyze(sel, gs);
                float bl = MktEconomics.BreakevenLoad(e.weeklyCostWan, e.weeklyRevenueWan, e.loadFactor);
                string detail =
                    $"收入 {e.weeklyRevenueWan:F0} · 成本 {e.weeklyCostWan:F0} · 净利 {e.weeklyProfitWan:F0} · 客座率 {e.loadFactor * 100:F0}%\n" +
                    $"RASK ${e.raskUsd:F3} · CASK ${e.caskUsd:F3} · 盈亏平衡客座率 {bl * 100:F0}% · 高铁威胁 {MktEconomics.HighSpeedRailThreat(sel.distanceKm)}";
                L("D0", detail, 0.0f, 0.32f, 1.0f, 0.58f, 13, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            }
            else
                L("D0", "暂无航线可分析", 0.0f, 0.40f, 1.0f, 0.52f, 13, UITheme.TextMuted);

            B("GoPricing", "去航班定价", 0.0f, 0.20f, 0.30f, 0.28f, UITheme.Accent, () => OpenPage("n49_航班定价"), 13);
            B("GoDash", "收益仪表盘", 0.32f, 0.20f, 0.62f, 0.28f, UITheme.PanelDeep, () => OpenPage("n47_收益管理仪表盘"), 13);
            B("GoSeason", "季节性趋势", 0.64f, 0.20f, 0.95f, 0.28f, UITheme.PanelDeep, () => OpenPage("n55_季节性日历"), 13);
        }

        // ============================================================
        // n49 航班定价
        // ============================================================
        void RenderPricing()
        {
            var gs = Gs;
            if (gs == null) return;
            var agg = Aggregate(gs);
            float avgFare = 0f, raskSum = 0f, caskSum = 0f;
            int n = 0;
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    var e = MktUiMath.Analyze(r, gs);
                    avgFare += MktManager.EstimateBaseFareUsd(r);
                    raskSum += e.raskUsd;
                    caskSum += e.caskUsd;
                    n++;
                }
            }
            avgFare = n > 0 ? avgFare / n : 0f;
            float rask = n > 0 ? raskSum / n : 0f;
            float cask = n > 0 ? caskSum / n : 0f;

            L("K1", $"平均票价 ${avgFare:F0}", 0.0f, 0.92f, 0.48f, 0.98f, 13, UITheme.Accent, true);
            L("K2", $"客座率 {gs.loadFactorPercent:F0}%", 0.0f, 0.85f, 0.48f, 0.91f, 13);
            L("K3", $"RASK ${rask:F3}", 0.0f, 0.78f, 0.48f, 0.84f, 13, rask >= cask ? UITheme.Success : UITheme.Danger);
            L("K4", $"CASK ${cask:F3}", 0.0f, 0.71f, 0.48f, 0.77f, 13);

            L("Cabin", "航线舱位看板（点击行 → 单航线定价）", 0.52f, 0.92f, 1.0f, 0.98f, 13, UITheme.TextMuted, true);

            var routes = gs.activeRoutes ?? new List<RouteData>();
            float y = 0.84f;
            int shown = 0;
            for (int i = 0; i < routes.Count && shown < 6; i++)
            {
                var r = routes[i];
                if (r == null) continue;
                string rid = r.routeId;
                var mkt = MktManager.EnsureState(gs);
                float econMult = MktManager.FareMultFor(mkt, rid, CabinClasses.Economy);
                float econFare = MktManager.EstimateBaseFareUsd(r) * econMult;
                B("Row" + i, $"{r.origin}—{r.destination}  经济舱 ${econFare:F0} (×{econMult:F2})",
                    0.52f, y, 1.0f, y + 0.085f, UITheme.NavIdle, () => { MktContext.SelectedRouteId = rid; OpenPage("n45_单航线定价详情"); }, 12);
                y -= 0.10f;
                shown++;
            }
            if (shown == 0)
                L("Empty", "暂无航线", 0.52f, 0.84f, 1.0f, 0.90f, 12, UITheme.TextMuted);

            B("Ai", "🤖 AI 动态调价", 0.0f, 0.46f, 0.32f, 0.56f, UITheme.Accent, () => OpenPage("n46_动态定价算法"), 13);
            B("Dash", "📈 收益仪表盘", 0.34f, 0.46f, 0.66f, 0.56f, UITheme.PanelDeep, () => OpenPage("n47_收益管理仪表盘"), 13);
            B("Aux", "💳 辅助收入", 0.0f, 0.34f, 0.32f, 0.44f, UITheme.PanelDeep, () => OpenPage("n48_辅助收入管理"), 13);
            B("Ffp", "🎫 FFP 常旅客", 0.34f, 0.34f, 0.66f, 0.44f, UITheme.PanelDeep, () => OpenPage("n33_FFP_常旅客管理"), 13);
            B("Hsr", "🚄 高铁热力图", 0.68f, 0.46f, 1.0f, 0.56f, UITheme.PanelDeep, () => OpenPage("n50_外部环境分析"), 13);
        }

        // ============================================================
        // n45 单航线定价详情
        // ============================================================
        void RenderRoutePricingDetail()
        {
            var gs = Gs;
            var r = CurrentRoute();
            if (r == null)
            {
                L("H", "单航线定价详情", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
                L("Empty", "暂无航线 — 请先开通航线。", 0.0f, 0.80f, 1.0f, 0.88f, 14, UITheme.TextMuted);
                return;
            }
            var e = MktUiMath.Analyze(r, gs);
            var mkt = MktManager.EnsureState(gs);
            string rid = r.routeId;

            L("H", AirportCatalog.Od(r.origin, r.destination), 0.0f, 0.92f, 0.7f, 0.98f, 15, UITheme.Accent, true);
            L("Info", $"{r.aircraftType} · {e.totalWeeklyFlights} 班/周 · 航程 {r.distanceKm:F0} km · {e.passengerType}", 0.0f, 0.85f, 1.0f, 0.91f, 12, UITheme.TextMuted);

            // 舱位票价矩阵
            string[] cabins = { CabinClasses.First, CabinClasses.Business, CabinClasses.PremiumEconomy, CabinClasses.Economy };
            float y = 0.72f;
            float baseFare = MktManager.EstimateBaseFareUsd(r);
            for (int i = 0; i < cabins.Length; i++)
            {
                string cb = cabins[i];
                float mult = MktManager.FareMultFor(mkt, rid, cb);
                L("C" + i, cb, 0.0f, y, 0.18f, y + 0.06f, 13);
                B("M" + i, "−0.1", 0.20f, y, 0.33f, y + 0.06f, UITheme.PanelDeep, () => StepFare(rid, cb, -0.1f), 11);
                L("V" + i, $"${baseFare * mult:F0}  (×{mult:F2})", 0.35f, y, 0.62f, y + 0.06f, 13, UITheme.Success, true);
                B("P" + i, "+0.1", 0.64f, y, 0.77f, y + 0.06f, UITheme.PanelDeep, () => StepFare(rid, cb, +0.1f), 11);
                y -= 0.10f;
            }

            // 调价模拟器
            float economyMult = MktManager.FareMultFor(mkt, rid, CabinClasses.Economy);
            float baseLoad = e.loadFactor;
            float baseCostPerFlight = e.totalWeeklyFlights > 0 ? e.weeklyCostWan / e.totalWeeklyFlights : 1f;
            float loadAfter = MktEconomics.LoadFactorAfterPrice(baseLoad, economyMult, e.passengerType);
            float profitAfter = MktEconomics.ProfitAfterPrice(e.seats, baseLoad, baseFare, baseCostPerFlight, economyMult, e.passengerType);
            L("Sim", $"模拟：客座率 {loadAfter * 100:F0}% · 单班日利润 {profitAfter:F2} 万", 0.0f, 0.10f, 1.0f, 0.16f, 13, UITheme.Warning, true);

            B("Ai", "✨ 一键 AI 建议", 0.0f, 0.02f, 0.33f, 0.09f, UITheme.Accent, () => ApplyAi(rid), 13);
            B("Ok", "✓ 确认调价", 0.35f, 0.02f, 0.66f, 0.09f, UITheme.Success, ConfirmFare, 13);
        }

        void StepFare(string routeId, string cabin, float delta)
        {
            var gs = Gs;
            if (gs == null) return;
            var mkt = MktManager.EnsureState(gs);
            float cur = MktManager.FareMultFor(mkt, routeId, cabin);
            MktManager.Instance?.SetRouteFare(routeId, cabin, Mathf.Clamp(cur + delta, 0.5f, 2.0f), out _);
            RefreshBody();
        }

        void ApplyAi(string routeId)
        {
            var r = CurrentRoute();
            if (r == null) return;
            string pt = MktUiMath.PassengerType(r);
            if (MktManager.Instance != null && MktManager.Instance.ApplyAiFare(routeId, pt, out string msg))
                Toast(msg);
            RefreshBody();
        }

        void ConfirmFare()
        {
            Toast("调价已保存并回写市场部。");
        }

        // ============================================================
        // n46 动态定价算法
        // ============================================================
        void RenderDynamicPricing()
        {
            var gs = Gs;
            if (gs == null) return;
            if (_dp == null) _dp = Clone(MktManager.EnsureState(gs).dynamicPricing);

            L("H", "AI 动态调价", 0.0f, 0.92f, 0.5f, 0.98f, 15, UITheme.Accent, true);
            B("Toggle", _dp.enabled ? "启用中" : "已暂停", 0.52f, 0.92f, 1.0f, 0.98f,
                _dp.enabled ? UITheme.Success : UITheme.PanelDeep, () => { _dp.enabled = !_dp.enabled; RefreshBody(); }, 13);

            L("CW", "竞对票价权重", 0.0f, 0.80f, 0.5f, 0.86f, 12, UITheme.TextMuted);
            B("CWm", "−", 0.52f, 0.80f, 0.62f, 0.86f, UITheme.PanelDeep, () => { _dp.competitorWeight = Clamp(_dp.competitorWeight - 0.1f); RefreshBody(); }, 13);
            L("CWv", $"{(int)(_dp.competitorWeight * 100)}%", 0.64f, 0.80f, 0.82f, 0.86f, 13, UITheme.Success, true);
            B("CWp", "+", 0.84f, 0.80f, 0.94f, 0.86f, UITheme.PanelDeep, () => { _dp.competitorWeight = Clamp(_dp.competitorWeight + 0.1f); RefreshBody(); }, 13);

            L("LW", "航班负载权重", 0.0f, 0.70f, 0.5f, 0.76f, 12, UITheme.TextMuted);
            B("LWm", "−", 0.52f, 0.70f, 0.62f, 0.76f, UITheme.PanelDeep, () => { _dp.loadWeight = Clamp(_dp.loadWeight - 0.1f); RefreshBody(); }, 13);
            L("LWv", $"{(int)(_dp.loadWeight * 100)}%", 0.64f, 0.70f, 0.82f, 0.76f, 13, UITheme.Success, true);
            B("LWp", "+", 0.84f, 0.70f, 0.94f, 0.76f, UITheme.PanelDeep, () => { _dp.loadWeight = Clamp(_dp.loadWeight + 0.1f); RefreshBody(); }, 13);

            L("HS", "节假日敏感度", 0.0f, 0.60f, 0.5f, 0.66f, 12, UITheme.TextMuted);
            string sens = _dp.holidaySensitivity == 0 ? "低" : _dp.holidaySensitivity == 1 ? "中" : "高";
            B("HS", sens, 0.52f, 0.60f, 0.72f, 0.66f, UITheme.PanelDeep, () => { _dp.holidaySensitivity = (_dp.holidaySensitivity + 1) % 3; RefreshBody(); }, 13);

            L("Floor", "最低票价保护（≥成本%）", 0.0f, 0.50f, 0.5f, 0.56f, 12, UITheme.TextMuted);
            B("Fm", "−5", 0.52f, 0.50f, 0.62f, 0.56f, UITheme.PanelDeep, () => { _dp.minFareFloorPercent = Clamp(_dp.minFareFloorPercent - 5f, 50f, 120f); RefreshBody(); }, 12);
            L("Fv", $"{_dp.minFareFloorPercent:F0}%", 0.64f, 0.50f, 0.82f, 0.56f, 13, UITheme.Success, true);
            B("Fp", "+5", 0.84f, 0.50f, 0.94f, 0.56f, UITheme.PanelDeep, () => { _dp.minFareFloorPercent = Clamp(_dp.minFareFloorPercent + 5f, 50f, 120f); RefreshBody(); }, 12);

            L("Max", "每日最多调价次数", 0.0f, 0.40f, 0.5f, 0.46f, 12, UITheme.TextMuted);
            B("Xm", "−", 0.52f, 0.40f, 0.62f, 0.46f, UITheme.PanelDeep, () => { _dp.maxAdjustsPerDay = Mathf.Max(1, _dp.maxAdjustsPerDay - 1); RefreshBody(); }, 13);
            L("Xv", $"{_dp.maxAdjustsPerDay} 次", 0.64f, 0.40f, 0.82f, 0.46f, 13, UITheme.Success, true);
            B("Xp", "+", 0.84f, 0.40f, 0.94f, 0.46f, UITheme.PanelDeep, () => { _dp.maxAdjustsPerDay = Mathf.Min(12, _dp.maxAdjustsPerDay + 1); RefreshBody(); }, 13);

            B("Save", "保存设置", 0.0f, 0.20f, 0.48f, 0.28f, UITheme.Accent, SaveDynamicPricing, 14);
            B("Override", "手动覆盖 → 单航线定价", 0.50f, 0.20f, 1.0f, 0.28f, UITheme.PanelDeep, () =>
            {
                var r = CurrentRoute();
                if (r != null) { MktContext.SelectedRouteId = r.routeId; OpenPage("n45_单航线定价详情"); }
                else Toast("暂无航线");
            }, 12);
        }

        void SaveDynamicPricing()
        {
            if (MktManager.Instance != null && MktManager.Instance.SaveDynamicPricing(_dp, out string msg))
                Toast(msg);
        }

        static float Clamp(float v) => Mathf.Clamp(v, 0f, 1f);
        static float Clamp(float v, float min, float max) => Mathf.Clamp(v, min, max);

        static DynamicPricingSettings Clone(DynamicPricingSettings s)
        {
            return s == null ? new DynamicPricingSettings() : new DynamicPricingSettings
            {
                enabled = s.enabled,
                competitorWeight = s.competitorWeight,
                loadWeight = s.loadWeight,
                holidaySensitivity = s.holidaySensitivity,
                minFareFloorPercent = s.minFareFloorPercent,
                maxAdjustsPerDay = s.maxAdjustsPerDay
            };
        }

        // ============================================================
        // n47 收益管理仪表盘
        // ============================================================
        void RenderRevenueDashboard()
        {
            var gs = Gs;
            if (gs == null) return;
            var agg = Aggregate(gs);
            float raskSum = 0f, caskSum = 0f, loadSum = 0f;
            int n = 0;
            if (gs.activeRoutes != null)
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var e = MktUiMath.Analyze(gs.activeRoutes[i], gs);
                    raskSum += e.raskUsd; caskSum += e.caskUsd; loadSum += e.loadFactor; n++;
                }
            float rask = n > 0 ? raskSum / n : 0f;
            float cask = n > 0 ? caskSum / n : 0f;
            float load = n > 0 ? loadSum / n : 0f;

            L("H", "收益管理仪表盘", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("K1", $"整体客座率 {load * 100:F0}%", 0.0f, 0.82f, 0.48f, 0.89f, 13);
            L("K2", $"周收入 {agg.rev:F0} 万 / 成本 {agg.cost:F0} 万", 0.0f, 0.72f, 0.48f, 0.79f, 13);
            L("K3", $"RASK ${rask:F3}", 0.0f, 0.62f, 0.48f, 0.69f, 13, rask >= cask ? UITheme.Success : UITheme.Danger, true);
            L("K4", $"CASK ${cask:F3}", 0.0f, 0.52f, 0.48f, 0.59f, 13);

            L("Rank", "航线收益排行", 0.52f, 0.82f, 1.0f, 0.89f, 13, UITheme.TextMuted, true);
            var routes = gs.activeRoutes ?? new List<RouteData>();
            float y = 0.73f;
            int shown = 0;
            for (int i = 0; i < routes.Count && shown < 6; i++)
            {
                var r = routes[i];
                if (r == null) continue;
                var e = MktUiMath.Analyze(r, gs);
                string rid = r.routeId;
                B("Row" + i, $"{r.origin}—{r.destination}  净利 {e.weeklyProfitWan:F0}",
                    0.52f, y, 1.0f, y + 0.08f, UITheme.NavIdle, () => { MktContext.SelectedRouteId = rid; OpenPage("n44_航线收益分析"); }, 12);
                y -= 0.09f;
                shown++;
            }
            if (shown == 0)
                L("Empty", "暂无航线", 0.52f, 0.73f, 1.0f, 0.80f, 12, UITheme.TextMuted);

            B("Export", "导出报表（CSV）", 0.0f, 0.20f, 0.48f, 0.28f, UITheme.PanelDeep, () => Toast("报表导出功能即将开放。"), 12);
        }

        // ============================================================
        // n48 辅助收入管理
        // ============================================================
        void RenderAncillary()
        {
            var gs = Gs;
            if (gs == null) return;
            if (_aux == null) _aux = CloneAux(MktManager.EnsureState(gs).ancillary);

            L("H", "辅助收入管理（美元/客）", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);

            StepRow("行李费", () => _aux.baggageFeeUsd, v => _aux.baggageFeeUsd = v, 0.80f, 2f);
            StepRow("选座溢价", () => _aux.seatFeeUsd, v => _aux.seatFeeUsd = v, 0.70f, 2f);
            StepRow("退改签费", () => _aux.changeFeeUsd, v => _aux.changeFeeUsd = v, 0.60f, 2f);
            StepRow("机上销售", () => _aux.onboardSalesUsd, v => _aux.onboardSalesUsd = v, 0.50f, 1f);
            StepRow("非航占比", () => _aux.nonAirShare, v => _aux.nonAirShare = v, 0.40f, 0.05f);

            float perPax = MktEconomics.AuxRevenuePerPaxUsd(_aux);
            L("Sum", $"人均辅助收入约 ${perPax:F1}", 0.0f, 0.28f, 1.0f, 0.34f, 13, UITheme.Warning, true);

            B("Save", "保存辅助收入", 0.0f, 0.14f, 0.48f, 0.22f, UITheme.Accent, SaveAncillary, 14);
        }

        void StepRow(string label, Func<float> get, Action<float> set, float y, float delta)
        {
            bool pct = label.Contains("占比");
            L("Lbl", label, 0.0f, y, 0.40f, y + 0.06f, 13);
            B("m", "−", 0.42f, y, 0.52f, y + 0.06f, UITheme.PanelDeep, () => { set(Mathf.Max(0f, get() - delta)); RefreshBody(); }, 13);
            L("v", pct ? $"{get() * 100:F0}%" : $"${get():F0}", 0.54f, y, 0.76f, y + 0.06f, 13, UITheme.Success, true);
            B("p", "+", 0.78f, y, 0.88f, y + 0.06f, UITheme.PanelDeep, () => { set(get() + delta); RefreshBody(); }, 13);
        }

        void SaveAncillary()
        {
            if (MktManager.Instance != null && MktManager.Instance.SaveAncillary(_aux, out string msg))
                Toast(msg);
        }

        static AncillarySettings CloneAux(AncillarySettings s)
        {
            return s == null ? new AncillarySettings() : new AncillarySettings
            {
                baggageFeeUsd = s.baggageFeeUsd,
                seatFeeUsd = s.seatFeeUsd,
                changeFeeUsd = s.changeFeeUsd,
                onboardSalesUsd = s.onboardSalesUsd,
                nonAirShare = s.nonAirShare
            };
        }

        // ============================================================
        // n50 外部环境分析
        // ============================================================
        void RenderExternalEnv()
        {
            var gs = Gs;
            if (gs == null) return;

            L("H", "高铁竞争热力图（≤1000km 航线）", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);

            var routes = gs.activeRoutes ?? new List<RouteData>();
            float y = 0.80f;
            int shown = 0;
            for (int i = 0; i < routes.Count && shown < 8; i++)
            {
                var r = routes[i];
                if (r == null) continue;
                string threat = MktEconomics.HighSpeedRailThreat(r.distanceKm);
                Color c = threat == "高" ? UITheme.Danger : threat == "中" ? UITheme.Warning : UITheme.Success;
                L("R" + i, $"{r.origin}—{r.destination}  {r.distanceKm:F0} km  威胁：{threat}", 0.0f, y, 1.0f, y + 0.06f, 13, c, true);
                y -= 0.08f;
                shown++;
            }
            if (shown == 0)
                L("Empty", "暂无短程航线", 0.0f, 0.80f, 1.0f, 0.86f, 13, UITheme.TextMuted);

            L("Comp", "竞对战略动态（来自竞争态势系统）", 0.0f, 0.20f, 1.0f, 0.27f, 13, UITheme.TextMuted, true);
            L("C0", "· 云翼航空：深圳—北京航线降价 8%（中影响）\n· 青鸾航空：申请北京—伦敦航权（低影响）", 0.0f, 0.04f, 1.0f, 0.18f, 12, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
        }

        // ============================================================
        // n55 季节性日历
        // ============================================================
        void RenderSeasonalCalendar()
        {
            var gs = Gs;
            if (gs == null) return;

            L("H", "季节性日历", 0.0f, 0.92f, 0.5f, 0.98f, 15, UITheme.Accent, true);
            L("Season", "当前航季：夏秋 · 距换季约 45 天", 0.0f, 0.85f, 0.5f, 0.91f, 12, UITheme.TextMuted);

            // 12 月需求系数
            string months = "";
            for (int m = 1; m <= 12; m++)
                months += $"{m}月 {MktEconomics.MonthSeasonalCoefficient(m):F2}   ";
            L("Months", months, 0.0f, 0.74f, 1.0f, 0.84f, 11, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);

            L("Fest", "节假日（点击选择）", 0.0f, 0.62f, 0.48f, 0.70f, 13, UITheme.TextMuted, true);
            var holidays = MktEconomics.Holidays;
            float y = 0.52f;
            int shown = 0;
            for (int i = 0; i < holidays.Length && shown < 7; i++)
            {
                string h = holidays[i];
                var info = MktEconomics.HolidayInfo(h);
                bool sel = MktContext.SelectedFestival == h;
                B("H" + i, $"{h}（{info.shape} · 峰{info.peak:F1}）", 0.0f, y, 0.48f, y + 0.065f,
                    sel ? UITheme.Accent : UITheme.NavIdle, () => { MktContext.SelectedFestival = h; RefreshBody(); }, 11);
                y -= 0.075f;
                shown++;
            }

            L("Sel", $"已选：{MktContext.SelectedFestival}", 0.52f, 0.52f, 1.0f, 0.60f, 13, UITheme.Warning, true);
            B("Conf", "🚀 配置旺季策略", 0.52f, 0.36f, 1.0f, 0.46f, UITheme.Accent, () => OpenPage("n57_旺季策略配置器"), 13);
            B("Compare", "🔄 航季对比", 0.52f, 0.24f, 1.0f, 0.34f, UITheme.PanelDeep, () => OpenPage("n56_航季对比详情"), 12);
            B("Hist", "📈 历史趋势", 0.52f, 0.12f, 1.0f, 0.22f, UITheme.PanelDeep, () => OpenPage("n58_历史趋势分析"), 12);
        }

        // ============================================================
        // n56 航季对比详情
        // ============================================================
        void RenderSeasonCompare()
        {
            var gs = Gs;
            if (gs == null) return;
            L("H", "航季对比 · 夏秋 vs 冬春", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);

            int summer = 0, winter = 0;
            var routes = gs.activeRoutes ?? new List<RouteData>();
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (r == null) continue;
                if (r.season == "冬春") winter++; else summer++;
            }
            L("S", $"夏秋航季航线 {summer} 条 · 冬春航季航线 {winter} 条", 0.0f, 0.78f, 1.0f, 0.85f, 14);
            L("Hint", "换季影响：夏秋为主运力季（暑运/国庆），冬春侧重春运与春节高峰。\n建议在 8 月（暑运峰）与 2 月（春节）加密班次。", 0.0f, 0.50f, 1.0f, 0.70f, 13, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            B("Back", "返回季节性日历", 0.0f, 0.20f, 0.4f, 0.28f, UITheme.PanelDeep, () => OpenPage("n55_季节性日历"), 13);
        }

        // ============================================================
        // n57 旺季策略配置器
        // ============================================================
        void RenderSeasonConfigurator()
        {
            var gs = Gs;
            if (gs == null) return;
            if (_pkg == null || _pkg.festival != MktContext.SelectedFestival)
            {
                var existing = MktManager.GetSeasonPackage(MktManager.EnsureState(gs), MktContext.SelectedFestival);
                _pkg = existing != null ? ClonePkg(existing) : new PeakSeasonPackage { festival = MktContext.SelectedFestival };
            }

            L("H", $"旺季策略配置器 · {_pkg.festival}", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);

            string cap = _pkg.capacityChoice switch { 0 => "保持", 1 => "加密", 2 => "窄转宽", 3 => "湿租/加班", _ => "保持" };
            string prc = _pkg.pricingChoice switch { 0 => "无", 1 => "早鸟折扣", 2 => "旺季溢价", 3 => "舱位开放", 4 => "差旅收紧", 5 => "打包产品", _ => "无" };
            string svc = _pkg.serviceChoice switch { 0 => "无", 1 => "旅游航线", 2 => "节日体验", 3 => "特殊旅客", _ => "无" };

            L("CapL", "运力投放", 0.0f, 0.80f, 0.35f, 0.86f, 12, UITheme.TextMuted);
            B("Cap", cap, 0.37f, 0.80f, 0.66f, 0.86f, UITheme.PanelDeep, () => { _pkg.capacityChoice = (_pkg.capacityChoice + 1) % 4; RefreshBody(); }, 12);
            L("PrcL", "收益定价", 0.0f, 0.70f, 0.35f, 0.76f, 12, UITheme.TextMuted);
            B("Prc", prc, 0.37f, 0.70f, 0.66f, 0.76f, UITheme.PanelDeep, () => { _pkg.pricingChoice = (_pkg.pricingChoice + 1) % 6; RefreshBody(); }, 12);
            L("SvcL", "产品服务", 0.0f, 0.60f, 0.35f, 0.66f, 12, UITheme.TextMuted);
            B("Svc", svc, 0.37f, 0.60f, 0.66f, 0.66f, UITheme.PanelDeep, () => { _pkg.serviceChoice = (_pkg.serviceChoice + 1) % 4; RefreshBody(); }, 12);

            B("Ext", _pkg.extendShoulder ? "平季延伸：开" : "平季延伸：关", 0.0f, 0.48f, 0.45f, 0.56f,
                _pkg.extendShoulder ? UITheme.Success : UITheme.PanelDeep, () => { _pkg.extendShoulder = !_pkg.extendShoulder; RefreshBody(); }, 12);

            L("Sim", "策略影响（可预览）", 0.52f, 0.60f, 1.0f, 0.68f, 12, UITheme.TextMuted, true);
            var agg = Aggregate(gs);
            var eff = MktEconomics.PeakSeasonEffect(_pkg);
            L("E", $"收入 ×{eff.revenueMult:F2} · 成本 ×{eff.costMult:F2}", 0.52f, 0.48f, 1.0f, 0.56f, 13, UITheme.Warning);

            B("Sim2", "查看影响预测", 0.52f, 0.36f, 1.0f, 0.46f, UITheme.PanelDeep, () => OpenPage("n59_旺季影响预测弹窗"), 12);
            B("Save", "保存策略包", 0.0f, 0.20f, 0.45f, 0.28f, UITheme.Accent, SaveSeasonPkg, 14);
            B("Apply", "应用至全部航线", 0.47f, 0.20f, 1.0f, 0.28f, UITheme.Success, () => { SaveSeasonPkg(); }, 13);
        }

        void SaveSeasonPkg()
        {
            if (_pkg == null) return;
            if (MktManager.Instance != null && MktManager.Instance.SaveSeasonPackage(_pkg, out string msg))
                Toast(msg);
        }

        static PeakSeasonPackage ClonePkg(PeakSeasonPackage p)
        {
            return new PeakSeasonPackage
            {
                festival = p.festival,
                capacityChoice = p.capacityChoice,
                pricingChoice = p.pricingChoice,
                serviceChoice = p.serviceChoice,
                extendShoulder = p.extendShoulder,
                extendDays = p.extendDays,
                extendPricingMult = p.extendPricingMult,
                applied = p.applied
            };
        }

        // ============================================================
        // n58 历史趋势分析
        // ============================================================
        void RenderHistoricalTrend()
        {
            L("H", "季节性历史趋势（近 3 年）", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("B0",
                "· 春节：V型曲线，峰值系数 3.0，节前 3 天预订激增\n" +
                "· 暑运：高原型，7-8 月维持 1.3~1.55 高位\n" +
                "· 国庆：V型，峰值 2.6，短途航线受益明显\n" +
                "· 平季延伸趋势：5 月与 11 月低谷可促销延伸",
                0.0f, 0.50f, 1.0f, 0.85f, 13, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            L("AO", "AO 建议：去年国庆前 3 天预订量激增，建议今年提前 5 天启动预售。", 0.0f, 0.30f, 1.0f, 0.42f, 13, UITheme.Warning, false, TextAlignmentOptions.TopLeft);
            B("Go", "去配置旺季策略", 0.0f, 0.18f, 0.4f, 0.26f, UITheme.Accent, () => OpenPage("n57_旺季策略配置器"), 13);
        }

        // ============================================================
        // n59 旺季影响预测弹窗
        // ============================================================
        void RenderSeasonImpact()
        {
            var gs = Gs;
            if (gs == null) return;
            var agg = Aggregate(gs);
            var pkg = _pkg;
            if (pkg == null || pkg.festival != MktContext.SelectedFestival)
            {
                var existing = MktManager.GetSeasonPackage(MktManager.EnsureState(gs), MktContext.SelectedFestival);
                pkg = existing ?? new PeakSeasonPackage { festival = MktContext.SelectedFestival };
                _pkg = pkg;
            }
            float baseLoad = gs.loadFactorPercent / 100f;
            var impact = MktEconomics.PeakSeasonImpact(agg.rev, agg.cost, baseLoad, pkg);

            L("H", $"旺季影响预测 · {pkg.festival}", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("P", $"预期利润增量 {impact.profitDelta:F1} 万/周", 0.0f, 0.76f, 1.0f, 0.83f, 14, impact.profitDelta >= 0 ? UITheme.Success : UITheme.Danger, true);
            L("Ld", $"客座率变化 +{impact.loadDelta * 100:F1}%", 0.0f, 0.66f, 1.0f, 0.73f, 14);
            L("C", $"成本增量 {impact.costDelta:F1} 万/周", 0.0f, 0.56f, 1.0f, 0.63f, 14, UITheme.TextMuted);
            L("Note", "以上为预估，实际受宏观与竞对影响。", 0.0f, 0.44f, 1.0f, 0.52f, 12, UITheme.TextMuted);

            B("Apply", "确认应用", 0.0f, 0.24f, 0.48f, 0.34f, UITheme.Success, () => { SaveSeasonPkg(); BackOne(); }, 14);
            B("Back", "返回修改", 0.52f, 0.24f, 1.0f, 0.34f, UITheme.PanelDeep, BackOne, 13);
        }

        // ============================================================
        // n33 FFP 常旅客管理
        // ============================================================
        void RenderFfp()
        {
            var gs = Gs;
            if (gs == null) return;
            if (_ffp == null) _ffp = CloneFfp(MktManager.EnsureState(gs).ffp);

            L("H", "FFP 常旅客管理", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);

            L("Earn", "累计里程（/元）", 0.0f, 0.80f, 0.4f, 0.86f, 12, UITheme.TextMuted);
            B("Em", "−1", 0.42f, 0.80f, 0.52f, 0.86f, UITheme.PanelDeep, () => { _ffp.earnRatePerYuan = Mathf.Max(1, _ffp.earnRatePerYuan - 1); RefreshBody(); }, 12);
            L("Ev", $"{_ffp.earnRatePerYuan}", 0.54f, 0.80f, 0.66f, 0.86f, 13, UITheme.Success, true);
            B("Ep", "+1", 0.68f, 0.80f, 0.78f, 0.86f, UITheme.PanelDeep, () => { _ffp.earnRatePerYuan++; RefreshBody(); }, 12);

            L("Rdm", "兑换里程（/元）", 0.0f, 0.70f, 0.4f, 0.76f, 12, UITheme.TextMuted);
            B("Rm", "−10", 0.42f, 0.70f, 0.52f, 0.76f, UITheme.PanelDeep, () => { _ffp.redeemRatePerYuan = Mathf.Max(10, _ffp.redeemRatePerYuan - 10); RefreshBody(); }, 12);
            L("Rv", $"{_ffp.redeemRatePerYuan}", 0.54f, 0.70f, 0.66f, 0.76f, 13, UITheme.Success, true);
            B("Rp", "+10", 0.68f, 0.70f, 0.78f, 0.76f, UITheme.PanelDeep, () => { _ffp.redeemRatePerYuan += 10; RefreshBody(); }, 12);

            B("CoBrand", _ffp.coBrandApproved ? "联名卡：已上线" : "联名卡：未上线", 0.0f, 0.58f, 0.45f, 0.66f,
                _ffp.coBrandApproved ? UITheme.Success : UITheme.PanelDeep, () => { _ffp.coBrandApproved = !_ffp.coBrandApproved; RefreshBody(); }, 12);

            float liability = MktEconomics.FfpLiabilityWan(_ffp);
            float lift = MktEconomics.FfpBrandLift(_ffp);
            L("Liab", $"里程负债约 {liability:F0} 万 · 品牌提升 +{lift:F1}", 0.0f, 0.44f, 1.0f, 0.52f, 13, UITheme.Warning, true);

            B("Save", "保存 FFP 规则", 0.0f, 0.20f, 0.48f, 0.28f, UITheme.Accent, SaveFfp, 14);
        }

        void SaveFfp()
        {
            if (MktManager.Instance != null && MktManager.Instance.SaveFfp(_ffp, out string msg))
                Toast(msg);
        }

        static FfpSettings CloneFfp(FfpSettings s)
        {
            return s == null ? new FfpSettings() : new FfpSettings
            {
                earnRatePerYuan = s.earnRatePerYuan,
                redeemRatePerYuan = s.redeemRatePerYuan,
                coBrandApproved = s.coBrandApproved,
                members = s.members,
                activeRate = s.activeRate
            };
        }

        // ============================================================
        // n34 产品工坊
        // ============================================================
        TMP_InputField _prodName;
        TMP_InputField _prodPrice;
        string _prodBundle = "机票+行李";
        float _prodConv = 0.4f;

        void RenderProductWorkshop()
        {
            var gs = Gs;
            if (gs == null) return;
            var mkt = MktManager.EnsureState(gs);

            L("H", "产品工坊", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);

            // 已上线产品列表
            L("List", "已上线产品", 0.0f, 0.80f, 0.48f, 0.88f, 13, UITheme.TextMuted, true);
            float y = 0.70f;
            int shown = 0;
            for (int i = 0; i < mkt.products.Count && shown < 6; i++)
            {
                var p = mkt.products[i];
                L("P" + i, $"{p.name} · {p.bundle} · ${p.priceUsd:F0} · 转化 {p.expectedConversion * 100:F0}%", 0.0f, y, 0.48f, y + 0.07f, 12, UITheme.Success);
                y -= 0.09f;
                shown++;
            }
            if (shown == 0)
                L("Empty", "暂无产品", 0.0f, 0.70f, 0.48f, 0.78f, 12, UITheme.TextMuted);

            // 新建
            L("New", "新建打包产品", 0.52f, 0.80f, 1.0f, 0.88f, 13, UITheme.TextMuted, true);
            L("N1", "名称", 0.52f, 0.70f, 0.66f, 0.78f, 12, UITheme.TextMuted);
            _prodName = UIFactory.Input(_content, "ProdName", "商务出行包", new UIFactory.Anchor(new Vector2(0.68f, 0.70f), new Vector2(1.0f, 0.78f), Vector2.zero, Vector2.zero));
            L("N2", "打包", 0.52f, 0.60f, 0.66f, 0.68f, 12, UITheme.TextMuted);
            B("Bundle", _prodBundle, 0.68f, 0.60f, 1.0f, 0.68f, UITheme.PanelDeep, () =>
            {
                _prodBundle = _prodBundle == "机票+行李" ? "机票+贵宾厅" : _prodBundle == "机票+贵宾厅" ? "机票+酒店" : "机票+行李";
                RefreshBody();
            }, 12);
            L("N3", "价格", 0.52f, 0.50f, 0.66f, 0.58f, 12, UITheme.TextMuted);
            _prodPrice = UIFactory.Input(_content, "ProdPrice", "199", new UIFactory.Anchor(new Vector2(0.68f, 0.50f), new Vector2(1.0f, 0.58f), Vector2.zero, Vector2.zero));
            L("N4", "转化率", 0.52f, 0.40f, 0.66f, 0.48f, 12, UITheme.TextMuted);
            B("Cm", "−", 0.68f, 0.40f, 0.76f, 0.48f, UITheme.PanelDeep, () => { _prodConv = Mathf.Clamp(_prodConv - 0.1f, 0.1f, 0.9f); RefreshBody(); }, 12);
            L("Cv", $"{_prodConv * 100:F0}%", 0.78f, 0.40f, 0.88f, 0.48f, 13, UITheme.Success, true);
            B("Cp", "+", 0.90f, 0.40f, 1.0f, 0.48f, UITheme.PanelDeep, () => { _prodConv = Mathf.Clamp(_prodConv + 0.1f, 0.1f, 0.9f); RefreshBody(); }, 12);

            B("Submit", "上线产品", 0.52f, 0.20f, 1.0f, 0.28f, UITheme.Accent, SubmitProduct, 13);
        }

        void SubmitProduct()
        {
            string name = _prodName != null ? _prodName.text.Trim() : "";
            float price = 199f;
            if (_prodPrice != null) float.TryParse(_prodPrice.text.Trim(), out price);
            var d = new ProductDesign
            {
                name = name,
                bundle = _prodBundle,
                priceUsd = price,
                expectedConversion = _prodConv
            };
            if (MktManager.Instance != null && MktManager.Instance.SubmitProduct(d, out string msg))
            {
                Toast(msg);
                RefreshBody();
            }
        }

        // ============================================================
        // 批次 B：竞争 / 渠道 / 品牌
        // ============================================================

        List<AIAirline> Rivals()
        {
            var list = new List<AIAirline>();
            var gs = Gs;
            if (gs?.aiAirlines == null) return list;
            for (int i = 0; i < gs.aiAirlines.Count; i++)
            {
                var ai = gs.aiAirlines[i];
                if (ai != null && ai.isAlive) list.Add(ai);
            }
            return list;
        }

        AIAirline SelRival()
        {
            var rivals = Rivals();
            if (rivals.Count == 0) return null;
            if (!string.IsNullOrEmpty(MktContext.SelectedCompetitorId))
            {
                var r = rivals.Find(x => x != null &&
                    (x.id == MktContext.SelectedCompetitorId || x.name == MktContext.SelectedCompetitorId));
                if (r != null) return r;
            }
            return rivals[0];
        }

        float[] PlayerDims(GameState gs) => MktCompetitionEconomics.DimensionScores(MktUiMath.PlayerBench(gs));
        float[] RivalDims(AIAirline ai) => MktCompetitionEconomics.DimensionScores(MktUiMath.AiBench(ai));

        /// <summary>7 维对标行（我司 vs 竞对），x0/x1/y0/y1 为布局区。</summary>
        void DimRows(GameState gs, AIAirline rival, float x0, float x1, float y0, float y1)
        {
            var pd = PlayerDims(gs);
            var rd = rival != null ? RivalDims(rival) : new float[MktCompetitionEconomics.DimCount];
            float h = (y1 - y0) / MktCompetitionEconomics.DimCount;
            for (int i = 0; i < MktCompetitionEconomics.DimCount; i++)
            {
                float yy = y1 - (i + 1) * h;
                string dim = MktCompetitionEconomics.DimNames[i];
                L("Dim" + i, $"{dim}　我司 {pd[i]:F0} ｜ 竞对 {rd[i]:F0}", x0, yy, x1, yy + h * 0.92f, 11,
                    pd[i] >= rd[i] ? UITheme.Success : UITheme.Warning, false, TextAlignmentOptions.MidlineLeft);
            }
        }

        // —— n60 竞争雷达 ——
        void RenderCompetitionRadar()
        {
            var gs = Gs;
            if (gs == null) return;
            var rivals = Rivals();
            float playerOverall = MktCompetitionEconomics.OverallScore(PlayerDims(gs));
            float bestRival = 0f;
            AIAirline best = null;
            for (int i = 0; i < rivals.Count; i++)
            {
                float s = MktCompetitionEconomics.OverallScore(RivalDims(rivals[i]));
                if (s > bestRival) { bestRival = s; best = rivals[i]; }
            }

            L("H", "竞争雷达 · 多维对标", 0.0f, 0.93f, 0.7f, 0.99f, 15, UITheme.Accent, true);
            L("Verdict", MktCompetitionEconomics.SituationalVerdict(playerOverall, bestRival), 0.0f, 0.86f, 1.0f, 0.92f, 12, UITheme.Warning);
            L("Score", $"我司综合 {playerOverall:F0} ｜ 最强竞对 {best?.name ?? "—"} {bestRival:F0}", 0.0f, 0.80f, 1.0f, 0.86f, 13, UITheme.TextPrimary, true);

            // 竞对卡片
            L("Cards", "主要竞对（点击进入详情）", 0.0f, 0.72f, 0.55f, 0.79f, 12, UITheme.TextMuted, true);
            float y = 0.62f;
            int shown = 0;
            for (int i = 0; i < rivals.Count && shown < 4; i++)
            {
                var ai = rivals[i];
                float s = MktCompetitionEconomics.OverallScore(RivalDims(ai));
                string id = ai.id;
                B("Card" + i, $"{ai.name} · {AIAirlineTypes.DisplayName(ai.type)} · {s:F0}分",
                    0.0f, y, 0.55f, y + 0.075f, UITheme.NavIdle,
                    () => { MktContext.SelectedCompetitorId = id; OpenPage("n61_竞对详情"); }, 11);
                y -= 0.085f;
                shown++;
            }
            if (shown == 0)
                L("Empty", "暂无竞对数据", 0.0f, 0.62f, 0.55f, 0.68f, 12, UITheme.TextMuted);

            // 7 维对标
            L("Dims", "逐维对标（我司 vs 最强竞对）", 0.58f, 0.72f, 1.0f, 0.79f, 12, UITheme.TextMuted, true);
            DimRows(gs, best, 0.58f, 1.0f, 0.02f, 0.70f);

            // 深度分析入口
            B("Net", "航线网络对标", 0.0f, 0.30f, 0.48f, 0.38f, UITheme.PanelDeep, () => OpenPage("n62_竞争_航线网络"), 12);
            B("Prod", "产品服务对标", 0.0f, 0.20f, 0.48f, 0.28f, UITheme.PanelDeep, () => OpenPage("n63_竞争_产品服务"), 12);
            B("Cost", "运营成本对标", 0.0f, 0.10f, 0.48f, 0.18f, UITheme.PanelDeep, () => OpenPage("n64_竞争_运营成本"), 12);
            B("Cap", "战略资本对标", 0.52f, 0.10f, 1.0f, 0.18f, UITheme.PanelDeep, () => OpenPage("n65_竞争_战略资本"), 12);
            B("Ao", "AO 建议 → 去航班定价", 0.52f, 0.30f, 1.0f, 0.38f, UITheme.Accent, () => OpenPage("n49_航班定价"), 12);
        }

        // —— n61 竞对详情 ——
        void RenderCompetitorDetail()
        {
            var gs = Gs;
            var ai = SelRival();
            if (gs == null) return;
            if (ai == null)
            {
                L("H", "竞对详情", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
                L("Empty", "暂无竞对数据", 0.0f, 0.80f, 1.0f, 0.88f, 14, UITheme.TextMuted);
                return;
            }
            var dims = RivalDims(ai);
            float overall = MktCompetitionEconomics.OverallScore(dims);

            L("H", $"竞对详情 · {ai.name}", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Info", $"{AIAirlineTypes.DisplayName(ai.type)} · {AIAirlineStates.DisplayName(ai.state)} · 综合 {overall:F0} 分", 0.0f, 0.85f, 1.0f, 0.91f, 12, UITheme.TextMuted);
            L("Metrics", $"机队 {ai.fleetSize} · 航线 {(ai.routes != null ? ai.routes.Length : 0)} · 份额 {ai.marketShare:F1}% · 现金 {ai.cash:F0}万 · 均价 ${ai.avgFare:F0} · 负债率 {ai.debtRatio * 100:F0}%",
                0.0f, 0.76f, 1.0f, 0.84f, 13, UITheme.TextPrimary, false, TextAlignmentOptions.TopLeft);

            L("Dims", "7 维雷达（该竞对 vs 我司）", 0.0f, 0.66f, 1.0f, 0.73f, 12, UITheme.TextMuted, true);
            DimRows(gs, ai, 0.0f, 1.0f, 0.10f, 0.64f);

            string intent = ai.state == AIAirlineStates.Expanding
                ? "战略研判：处于进攻扩张期，重点争夺核心航线与航权。"
                : ai.state == AIAirlineStates.Dying
                ? "战略研判：经营承压，可能退出部分航线或寻求并购。"
                : "战略研判：稳态经营，伺机而动。";
            L("Intent", intent, 0.0f, 0.02f, 1.0f, 0.09f, 12, UITheme.Warning, false, TextAlignmentOptions.TopLeft);
        }

        // —— n62 竞争·航线网络对标 ——
        void RenderCompNetwork()
        {
            var gs = Gs;
            if (gs == null) return;
            var ai = SelRival();
            int myRoutes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int rivalRoutes = ai?.routes != null ? ai.routes.Length : 0;
            L("H", "竞争 · 航线网络对标", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("B", $"航线总数　我司 {myRoutes} ｜ 竞对 {ai?.name ?? "—"} {rivalRoutes}\n覆盖城市　我司 {myRoutes + 1} ｜ 竞对 {rivalRoutes + 1}\n市场份额　我司 {gs.playerMarketShare:F1}% ｜ 竞对 {ai?.marketShare ?? 0:F1}%",
                0.0f, 0.62f, 1.0f, 0.86f, 13, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            L("Hint", "独飞航线：我司掌握 {(myRoutes >= rivalRoutes ? myRoutes - rivalRoutes : 0)} 条相对独飞优势。", 0.0f, 0.44f, 1.0f, 0.56f, 12, UITheme.Warning, false, TextAlignmentOptions.TopLeft);
            B("Back", "← 返回竞争雷达", 0.0f, 0.24f, 0.42f, 0.34f, UITheme.PanelDeep, () => OpenPage("n60_竞争雷达"), 13);
        }

        // —— n63 竞争·产品服务对标 ——
        void RenderCompProduct()
        {
            var gs = Gs;
            if (gs == null) return;
            var ai = SelRival();
            L("H", "竞争 · 产品服务对标", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("B", $"品牌/声誉　我司 {gs.brandAwareness:F0} ｜ 竞对 {ai?.reputation ?? 0:F0}\n准点率　我司 {gs.onTimePercent:F0}% ｜ 竞对 86%\nSkytrax　我司 ★★★ ｜ 竞对 ★★★★\nFFP 会员　我司 {MktManager.EnsureState(gs).ffp.members / 10000:F0} 万 ｜ 竞对 15 万",
                0.0f, 0.60f, 1.0f, 0.86f, 13, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            L("Hint", "产品服务维度短板：建议提升舱位服务与准点表现。", 0.0f, 0.44f, 1.0f, 0.54f, 12, UITheme.Warning, false, TextAlignmentOptions.TopLeft);
            B("Back", "← 返回竞争雷达", 0.0f, 0.24f, 0.42f, 0.34f, UITheme.PanelDeep, () => OpenPage("n60_竞争雷达"), 13);
        }

        // —— n64 竞争·运营成本对标 ——
        void RenderCompCost()
        {
            var gs = Gs;
            if (gs == null) return;
            var ai = SelRival();
            var agg = Aggregate(gs);
            float myCask = 0f;
            int n = 0;
            if (gs.activeRoutes != null)
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var e = MktUiMath.Analyze(gs.activeRoutes[i], gs);
                    myCask += e.caskUsd; n++;
                }
            myCask = n > 0 ? myCask / n : 0f;
            L("H", "竞争 · 运营成本对标", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("B", $"单位成本 CASK　我司 ${myCask:F3} ｜ 竞对 ${(ai != null ? Math.Max(0.04f, myCask * 0.92f) : 0.045f):F3}\n负债率　我司 {Mathf.Clamp01(LoanManager.TotalDebt(gs) / Mathf.Max(1f, gs.cash + 300f)) * 100:F0}% ｜ 竞对 {(ai?.debtRatio ?? 0.3f) * 100:F0}%\n机队日利用率　我司 8.6h ｜ 竞对 9.4h",
                0.0f, 0.60f, 1.0f, 0.86f, 13, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            L("Hint", "成本结构：燃油约 32% · 人力 22% · 维修 12% · 起降 18% · 其他 16%。", 0.0f, 0.44f, 1.0f, 0.54f, 12, UITheme.Warning, false, TextAlignmentOptions.TopLeft);
            B("Back", "← 返回竞争雷达", 0.0f, 0.24f, 0.42f, 0.34f, UITheme.PanelDeep, () => OpenPage("n60_竞争雷达"), 13);
        }

        // —— n65 竞争·战略资本对标 ——
        void RenderCompCapital()
        {
            var gs = Gs;
            if (gs == null) return;
            var ai = SelRival();
            L("H", "竞争 · 战略资本对标", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("B", $"现金储备　我司 {gs.cash:F0}万 ｜ 竞对 {ai?.cash ?? 0:F0}万\n负债率　我司 {Mathf.Clamp01(LoanManager.TotalDebt(gs) / Mathf.Max(1f, gs.cash + 300f)) * 100:F0}% ｜ 竞对 {(ai?.debtRatio ?? 0.3f) * 100:F0}%\n信用评级　我司 {LoanManager.GetCreditRating(gs)} ｜ 竞对 A\n联盟成员　我司 未加入 ｜ 竞对 星空联盟",
                0.0f, 0.60f, 1.0f, 0.86f, 13, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            L("Hint", "近期动态：竞对正筹备 1 架宽体机采购，可能争夺洲际航线。", 0.0f, 0.44f, 1.0f, 0.54f, 12, UITheme.Warning, false, TextAlignmentOptions.TopLeft);
            B("Back", "← 返回竞争雷达", 0.0f, 0.24f, 0.42f, 0.34f, UITheme.PanelDeep, () => OpenPage("n60_竞争雷达"), 13);
        }

        // —— n67 大客户合同 ——
        void RenderKeyAccount()
        {
            var gs = Gs;
            if (gs == null) return;
            var mkt = MktManager.EnsureState(gs);

            L("H", "大客户合同 · 企业差旅协议", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Board", $"已签合同 {mkt.keyAccounts.Count(k => k.signed)} 份 · 年度协议采购总额 {TotalKeyAccountSpend(mkt):F0} 万", 0.0f, 0.84f, 1.0f, 0.90f, 12, UITheme.TextMuted);

            L("List", "已签客户", 0.0f, 0.74f, 0.48f, 0.82f, 12, UITheme.TextMuted, true);
            float y = 0.64f;
            int shown = 0;
            for (int i = 0; i < mkt.keyAccounts.Count && shown < 6; i++)
            {
                var k = mkt.keyAccounts[i];
                if (!k.signed) continue;
                float val = MktCompetitionEconomics.KeyAccountValueWan(k.annualSpendWan, k.frontDiscount, k.tier);
                L("K" + i, $"{k.name} · {TierName(k.tier)} · {k.annualSpendWan:F0}万/年 · 价值 {val:F0}万", 0.0f, y, 0.48f, y + 0.06f, 11, UITheme.Success);
                y -= 0.075f;
                shown++;
            }
            if (shown == 0)
                L("Empty", "暂无签约客户", 0.0f, 0.64f, 0.48f, 0.72f, 12, UITheme.TextMuted);

            // 快速签约
            L("New", "快速签约（示例客户）", 0.52f, 0.74f, 1.0f, 0.82f, 12, UITheme.TextMuted, true);
            B("Sign1", "签约「华南制造集团」（全国 · 300万/年 · 8折）", 0.52f, 0.60f, 1.0f, 0.70f, UITheme.Accent, () =>
            {
                string msg = "签约失败";
                if (MktManager.Instance != null)
                    MktManager.Instance.SignKeyAccount(new KeyAccount { name = "华南制造集团", tier = 1, annualSpendWan = 300f, frontDiscount = 0.8f, backRewardRate = 0.03f, expiryYear = gs.year + 1 }, out msg);
                Toast(msg); RefreshBody();
            }, 11);
            B("Sign2", "签约「环球科技」（全球 · 800万/年 · 9折）", 0.52f, 0.48f, 1.0f, 0.58f, UITheme.Accent, () =>
            {
                string msg = "签约失败";
                if (MktManager.Instance != null)
                    MktManager.Instance.SignKeyAccount(new KeyAccount { name = "环球科技", tier = 0, annualSpendWan = 800f, frontDiscount = 0.9f, backRewardRate = 0.05f, expiryYear = gs.year + 2 }, out msg);
                Toast(msg); RefreshBody();
            }, 11);

            B("Ffp", "差旅分析 → FFP 联动", 0.52f, 0.28f, 1.0f, 0.36f, UITheme.PanelDeep, () => OpenPage("n33_FFP_常旅客管理"), 11);
            B("Prod", "打包差旅产品 → 产品工坊", 0.52f, 0.18f, 1.0f, 0.26f, UITheme.PanelDeep, () => OpenPage("n34_产品工坊"), 11);
        }

        static float TotalKeyAccountSpend(MktState mkt)
        {
            float s = 0f;
            if (mkt?.keyAccounts == null) return 0f;
            for (int i = 0; i < mkt.keyAccounts.Count; i++)
                if (mkt.keyAccounts[i].signed) s += mkt.keyAccounts[i].annualSpendWan;
            return s;
        }

        static string TierName(int tier) => tier switch { 0 => "全球性", 1 => "全国性", _ => "地区性" };

        // —— n68 渠道预算 ——
        void RenderChannelBudget()
        {
            var gs = Gs;
            if (gs == null) return;
            if (_channel == null) _channel = CloneChannel(MktManager.EnsureState(gs).channelBudget);
            var mkt = MktManager.EnsureState(gs);
            float used = _channel.usedWan;
            float total = Mathf.Max(0.1f, _channel.annualBudgetWan);
            float pct = Mathf.Clamp01(used / total);

            L("H", "渠道预算", 0.0f, 0.92f, 0.5f, 0.98f, 15, UITheme.Accent, true);
            L("Board", $"年度总预算 {total:F0}万 · 已用 {used:F0}万 · 剩余 {total - used:F0}万 · 使用率 {pct * 100:F0}%", 0.0f, 0.84f, 1.0f, 0.90f, 12, UITheme.TextMuted);

            // 提直降代滑块
            L("Dir", "直销占比（提直降代）", 0.0f, 0.70f, 0.5f, 0.77f, 12, UITheme.TextMuted);
            B("Dm", "−5%", 0.52f, 0.70f, 0.62f, 0.77f, UITheme.PanelDeep, () => { _channel.directRatio = Mathf.Clamp(_channel.directRatio - 0.05f, 0.1f, 0.9f); RefreshBody(); }, 12);
            L("Dv", $"{_channel.directRatio * 100:F0}%", 0.64f, 0.70f, 0.80f, 0.77f, 13, UITheme.Success, true);
            B("Dp", "+5%", 0.82f, 0.70f, 0.94f, 0.77f, UITheme.PanelDeep, () => { _channel.directRatio = Mathf.Clamp(_channel.directRatio + 0.05f, 0.1f, 0.9f); RefreshBody(); }, 12);

            // 分销成本设置
            StepBudget("GDS 航段费", () => _channel.gdsFeePerSegment, v => _channel.gdsFeePerSegment = v, 0.58f, 1f, true);
            StepBudget("OTA 佣金率", () => _channel.otaCommissionRate, v => _channel.otaCommissionRate = v, 0.46f, 0.02f, false);
            StepBudget("TMC 航段费", () => _channel.tmcSegmentFee, v => _channel.tmcSegmentFee = v, 0.34f, 1f, true);
            StepBudget("旅行社返点", () => _channel.agencyRebateRate, v => _channel.agencyRebateRate = v, 0.22f, 0.01f, false);

            var agg = Aggregate(gs);
            float distCost = MktCompetitionEconomics.AvgDistributionCostUsd(_channel.gdsFeePerSegment, _channel.otaCommissionRate, 900f, _channel.tmcSegmentFee, _channel.agencyRebateRate, 900f);
            float savings = MktCompetitionEconomics.DirectShiftSavingsWan(agg.rev, _channel.otaCommissionRate, _channel.directRatio - 0.35f);
            L("Sim", $"预估单票分销成本 ${distCost:F1} · 提直降代节省 {savings:F0}万/周", 0.0f, 0.10f, 1.0f, 0.18f, 12, UITheme.Warning, true);

            B("Dist", "分销渠道管理", 0.0f, 0.30f, 0.32f, 0.38f, UITheme.PanelDeep, () => OpenPage("n69_分销渠道管理"), 12);
            B("Direct", "直销渠道管理", 0.34f, 0.30f, 0.66f, 0.38f, UITheme.PanelDeep, () => OpenPage("n70_直销渠道管理"), 12);
            B("Roi", "渠道ROI分析", 0.68f, 0.30f, 1.0f, 0.38f, UITheme.PanelDeep, () => OpenPage("n71_渠道ROI分析"), 12);
            B("Save", "保存渠道预算", 0.0f, 0.18f, 0.48f, 0.26f, UITheme.Accent, SaveChannel, 13);
        }

        void StepBudget(string label, Func<float> get, Action<float> set, float y, float delta, bool dollars)
        {
            bool pct = !dollars;
            L("Lbl", label, 0.0f, y, 0.40f, y + 0.06f, 12);
            B("m", "−", 0.42f, y, 0.52f, y + 0.06f, UITheme.PanelDeep, () => { set(Mathf.Max(0f, get() - delta)); RefreshBody(); }, 13);
            L("v", pct ? $"{get() * 100:F0}%" : $"${get():F0}", 0.54f, y, 0.76f, y + 0.06f, 13, UITheme.Success, true);
            B("p", "+", 0.78f, y, 0.88f, y + 0.06f, UITheme.PanelDeep, () => { set(get() + delta); RefreshBody(); }, 13);
        }

        void SaveChannel()
        {
            if (MktManager.Instance != null && MktManager.Instance.SaveChannelBudget(_channel, out string msg))
                Toast(msg);
        }

        static ChannelBudget CloneChannel(ChannelBudget s)
        {
            return s == null ? new ChannelBudget() : new ChannelBudget
            {
                annualBudgetWan = s.annualBudgetWan,
                usedWan = s.usedWan,
                directRatio = s.directRatio,
                gdsFeePerSegment = s.gdsFeePerSegment,
                otaCommissionRate = s.otaCommissionRate,
                tmcSegmentFee = s.tmcSegmentFee,
                agencyRebateRate = s.agencyRebateRate,
                surchargeEnabled = s.surchargeEnabled,
                surchargeUsd = s.surchargeUsd
            };
        }

        // —— n69 分销渠道管理 ——
        void RenderDistribution()
        {
            var gs = Gs;
            if (gs == null) return;
            var ch = MktManager.EnsureState(gs).channelBudget;
            L("H", "分销渠道管理", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("B", $"GDS 航段费 ${ch.gdsFeePerSegment:F0} · OTA 佣金率 {ch.otaCommissionRate * 100:F0}% · TMC 航段费 ${ch.tmcSegmentFee:F0} · 旅行社返点 {ch.agencyRebateRate * 100:F0}%\n附加费 {(ch.surchargeEnabled ? $"已启用（${ch.surchargeUsd:F0}/次）" : "已关闭")}",
                0.0f, 0.62f, 1.0f, 0.84f, 13, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            L("Hint", "分销渠道：Amadeus / Sabre / Travelport · OTA：携程 / 飞猪 / 去哪儿 · TMC 大客户差旅 · 旅行社", 0.0f, 0.50f, 1.0f, 0.58f, 12, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            B("Surch", ch.surchargeEnabled ? "关闭附加费" : "启用附加费", 0.0f, 0.36f, 0.5f, 0.44f, UITheme.PanelDeep, () =>
            {
                ch.surchargeEnabled = !ch.surchargeEnabled;
                MktManager.Instance?.SaveChannelBudget(ch, out _);
                RefreshBody();
            }, 12);
            B("Back", "← 返回渠道预算", 0.0f, 0.22f, 0.45f, 0.30f, UITheme.PanelDeep, () => OpenPage("n68_渠道预算"), 13);
        }

        // —— n70 直销渠道管理 ——
        void RenderDirect()
        {
            var gs = Gs;
            if (gs == null) return;
            var ch = MktManager.EnsureState(gs).channelBudget;
            L("H", "直销渠道管理", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("B", $"直销占比 {ch.directRatio * 100:F0}% · 官网/APP 转化率 3.2% · 呼叫中心首呼解决率 78%\n品牌推广 SEM/SEO 月投放 12 万 · 会员拉新 CAC ${MktCompetitionEconomics.Cac(12f, 4000):F1}/人",
                0.0f, 0.60f, 1.0f, 0.84f, 13, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            L("Hint", "直销效能：自营渠道单票成本低于 OTA 约 30%，建议加大官网促销力度。", 0.0f, 0.46f, 1.0f, 0.56f, 12, UITheme.Warning, false, TextAlignmentOptions.TopLeft);
            B("Prod", "新建促销产品 → 产品工坊", 0.0f, 0.30f, 0.6f, 0.38f, UITheme.Accent, () => OpenPage("n34_产品工坊"), 12);
            B("Back", "← 返回渠道预算", 0.0f, 0.18f, 0.45f, 0.26f, UITheme.PanelDeep, () => OpenPage("n68_渠道预算"), 13);
        }

        // —— n71 渠道ROI分析 ——
        void RenderChannelRoi()
        {
            var gs = Gs;
            if (gs == null) return;
            var ch = MktManager.EnsureState(gs).channelBudget;
            var agg = Aggregate(gs);
            float distRev = agg.rev * (1f - ch.directRatio);
            float directRev = agg.rev * ch.directRatio;
            float distCost = Mathf.Max(1f, distRev * 0.12f);
            float directCost = Mathf.Max(1f, directRev * 0.06f);
            float distRoi = MktCompetitionEconomics.ChannelRoi(distRev, distCost);
            float directRoi = MktCompetitionEconomics.ChannelRoi(directRev, directCost);

            L("H", "渠道ROI分析", 0.0f, 0.90f, 1.0f, 0.97f, 15, UITheme.Accent, true);
            L("B", $"分销渠道 ROI {distRoi:F2}（收入 {distRev:F0}万 / 成本 {distCost:F0}万）\n直销渠道 ROI {directRoi:F2}（收入 {directRev:F0}万 / 成本 {directCost:F0}万）\n渠道集中度 {MktCompetitionEconomics.ChannelConcentration(new[] { ch.directRatio, 1f - ch.directRatio }):F2}（越低越均衡）",
                0.0f, 0.60f, 1.0f, 0.86f, 13, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            string best = directRoi >= distRoi ? "直销渠道 ROI 更高，建议继续「提直降代」。" : "分销渠道 ROI 更高，建议优化分销投入结构。";
            L("Hint", best, 0.0f, 0.46f, 1.0f, 0.56f, 12, UITheme.Warning, false, TextAlignmentOptions.TopLeft);
            B("Dist", "钻取分销", 0.0f, 0.30f, 0.42f, 0.38f, UITheme.PanelDeep, () => OpenPage("n69_分销渠道管理"), 12);
            B("Direct", "钻取直销", 0.44f, 0.30f, 0.86f, 0.38f, UITheme.PanelDeep, () => OpenPage("n70_直销渠道管理"), 12);
            B("Back", "← 返回渠道预算", 0.0f, 0.18f, 0.45f, 0.26f, UITheme.PanelDeep, () => OpenPage("n68_渠道预算"), 13);
        }

        // —— n72 品牌危机公关 ——
        void RenderBrandCrisis()
        {
            var gs = Gs;
            if (gs == null) return;
            var b = MktManager.EnsureState(gs).brand;
            int level = MktCompetitionEconomics.CrisisLevel(b.negativeVolume / 1000f, b.negativeVolume);

            L("H", "品牌危机公关", 0.0f, 0.92f, 0.5f, 0.98f, 15, UITheme.Accent, true);
            L("Health", $"信任指数 {b.trustIndex:F0} · NPS {b.nps} · 舆情等级 {MktManager.LevelName(level)} · {(b.active ? "危机处理中" : "正常")}",
                0.0f, 0.84f, 1.0f, 0.90f, 13, b.active ? UITheme.Danger : UITheme.Success, true);

            L("Risk", "三大风险看板", 0.0f, 0.74f, 0.48f, 0.82f, 12, UITheme.TextMuted, true);
            L("R1", "服务与保障危机（延误/餐食/行李）", 0.0f, 0.66f, 0.48f, 0.72f, 12);
            L("R2", "人员行为与歧视危机", 0.0f, 0.58f, 0.48f, 0.64f, 12);
            L("R3", "收益与口碑失衡危机", 0.0f, 0.50f, 0.48f, 0.56f, 12);

            L("Feed", "实时舆情信息流", 0.52f, 0.74f, 1.0f, 0.82f, 12, UITheme.TextMuted, true);
            float y = 0.64f;
            int shown = 0;
            for (int i = 0; i < b.feed.Count && shown < 5; i++)
            {
                var s = b.feed[i];
                string tag = s.sentiment > 0 ? "正面" : s.sentiment < 0 ? "负面" : "中性";
                Color c = s.sentiment < 0 ? UITheme.Danger : s.sentiment > 0 ? UITheme.Success : UITheme.TextMuted;
                L("F" + i, $"[{tag}] {s.platform} · {s.text}", 0.52f, y, 1.0f, y + 0.06f, 11, c);
                y -= 0.07f;
                shown++;
            }
            if (shown == 0)
                L("Empty", "暂无舆情", 0.52f, 0.64f, 1.0f, 0.70f, 12, UITheme.TextMuted);

            B("Sent", "舆情监测中心", 0.0f, 0.30f, 0.32f, 0.38f, UITheme.Accent, () => OpenPage("n73_舆情监测中心"), 12);
            B("Resp", "危机响应指挥部", 0.34f, 0.30f, 0.66f, 0.38f, UITheme.Accent, () => OpenPage("n74_危机响应指挥部"), 12);
            B("Repair", "品牌修复计划", 0.68f, 0.30f, 1.0f, 0.38f, UITheme.Accent, () => OpenPage("n75_品牌修复计划"), 12);
            B("Drill", "预警演练（模拟危机）", 0.0f, 0.18f, 0.48f, 0.26f, UITheme.PanelDeep, () =>
            {
                SeedDemoCrisis(b);
                string msg = "演练失败";
                if (MktManager.Instance != null)
                    MktManager.Instance.UpdateBrandCrisis(b, out msg);
                Toast(msg); RefreshBody();
            }, 12);
        }

        static void SeedDemoCrisis(BrandCrisis b)
        {
            b.active = true;
            b.negativeVolume = 350;
            b.crisisLevel = 1;
            b.responseStage = 0;
            if (b.feed == null) b.feed = new List<SentimentEntry>();
            if (b.feed.Count == 0)
            {
                b.feed.Add(new SentimentEntry { platform = "微博", text = "航班延误未及时通知，体验很差。", sentiment = -1, heat = 88 });
                b.feed.Add(new SentimentEntry { platform = "抖音", text = "餐食质量下降引发不满。", sentiment = -1, heat = 75 });
                b.feed.Add(new SentimentEntry { platform = "小红书", text = "行李托运出现问题。", sentiment = -1, heat = 60 });
                b.feed.Add(new SentimentEntry { platform = "新闻", text = "媒体关注服务投诉上升。", sentiment = -1, heat = 90 });
            }
        }

        // —— n73 舆情监测中心 ——
        void RenderSentiment()
        {
            var gs = Gs;
            if (gs == null) return;
            var b = MktManager.EnsureState(gs).brand;
            int pos = 0, neg = 0;
            if (b.feed != null)
                for (int i = 0; i < b.feed.Count; i++)
                {
                    if (b.feed[i].sentiment > 0) pos++;
                    else if (b.feed[i].sentiment < 0) neg++;
                }
            float total = Mathf.Max(1, b.feed != null ? b.feed.Count : 0);
            float sentimentScore = MktCompetitionEconomics.SentimentScore(pos / total, neg / total);

            L("H", "舆情监测中心 · AI 舆情雷达", 0.0f, 0.92f, 0.55f, 0.98f, 15, UITheme.Accent, true);
            L("Score", $"全网情感打分 {sentimentScore:F0}（-100~+100）· 负面声量 {b.negativeVolume}", 0.0f, 0.84f, 1.0f, 0.90f, 12, UITheme.TextMuted);

            L("Feed", "舆情条目（情感分析）", 0.0f, 0.72f, 1.0f, 0.80f, 12, UITheme.TextMuted, true);
            float y = 0.62f;
            int shown = 0;
            if (b.feed != null)
                for (int i = 0; i < b.feed.Count && shown < 6; i++)
                {
                    var s = b.feed[i];
                    string tag = s.sentiment > 0 ? "正面" : s.sentiment < 0 ? "负面" : "中性";
                    Color c = s.sentiment < 0 ? UITheme.Danger : s.sentiment > 0 ? UITheme.Success : UITheme.TextMuted;
                    L("F" + i, $"[{tag}·热度{s.heat}] {s.platform}：{s.text}", 0.0f, y, 1.0f, y + 0.06f, 11, c);
                    y -= 0.075f;
                    shown++;
                }
            if (shown == 0)
                L("Empty", "暂无舆情数据", 0.0f, 0.62f, 1.0f, 0.68f, 12, UITheme.TextMuted);

            L("Rules", "分级预警：一般（负面>15%）→ 邮件 ｜ 重要（>30%）→ AO弹窗 ｜ 重大（>50%）→ 强制CEO弹窗", 0.0f, 0.30f, 1.0f, 0.40f, 11, UITheme.TextMuted, false, TextAlignmentOptions.TopLeft);
            B("Resp", "启动危机响应 → 指挥部", 0.0f, 0.16f, 0.55f, 0.26f, UITheme.Accent, () => OpenPage("n74_危机响应指挥部"), 13);
        }

        // —— n74 危机响应指挥部 ——
        void RenderCrisisResponse()
        {
            var gs = Gs;
            if (gs == null) return;
            if (_brand == null) _brand = CloneBrand(MktManager.EnsureState(gs).brand);

            string stage = _brand.responseStage switch { 0 => "研判", 1 => "应对", 2 => "处置", _ => "善后" };
            L("H", "危机响应指挥部", 0.0f, 0.92f, 0.5f, 0.98f, 15, UITheme.Accent, true);
            L("Status", $"响应等级 {MktManager.LevelName(_brand.crisisLevel)} · 阶段 {stage} · 剩余窗口 {Mathf.Max(0, 60 - _brand.responseStage * 15)} 分钟", 0.0f, 0.84f, 1.0f, 0.90f, 12, _brand.active ? UITheme.Danger : UITheme.Success);

            L("Flow", "5步标准流程", 0.0f, 0.74f, 0.48f, 0.82f, 12, UITheme.TextMuted, true);
            string[] steps = { "① 速度第一·黄金时间回应", "② 统一口径·分级发声", "③ 坦诚沟通·不推诿", "④ 立即行动·纠正错误", "⑤ 受害者安抚·合理补偿" };
            for (int i = 0; i < steps.Length; i++)
                L("S" + i, steps[i], 0.0f, 0.66f - i * 0.08f, 0.48f, 0.72f - i * 0.08f, 11, _brand.responseStage >= i ? UITheme.Success : UITheme.TextMuted);

            // 声明模板
            L("Tpl", "声明模板", 0.52f, 0.74f, 1.0f, 0.82f, 12, UITheme.TextMuted, true);
            string tpl = _brand.statementTemplate;
            B("TplBtn", $"当前：{tpl}", 0.52f, 0.62f, 1.0f, 0.72f, UITheme.PanelDeep, () =>
            {
                _brand.statementTemplate = tpl switch { "道歉" => "澄清", "澄清" => "行动", "行动" => "善后", _ => "道歉" };
                RefreshBody();
            }, 12);

            B("Stage", "推进阶段", 0.52f, 0.48f, 1.0f, 0.56f, UITheme.Accent, () =>
            {
                _brand.responseStage = Mathf.Min(3, _brand.responseStage + 1);
                if (_brand.responseStage >= 3) _brand.active = false;
                RefreshBody();
            }, 12);
            B("Save", "保存危机状态", 0.52f, 0.36f, 1.0f, 0.44f, UITheme.Success, () =>
            {
                string msg = "保存失败";
                if (MktManager.Instance != null)
                    MktManager.Instance.UpdateBrandCrisis(_brand, out msg);
                Toast(msg);
            }, 12);
            B("Repair", "→ 品牌修复计划", 0.52f, 0.24f, 1.0f, 0.32f, UITheme.PanelDeep, () => OpenPage("n75_品牌修复计划"), 11);
        }

        static BrandCrisis CloneBrand(BrandCrisis b)
        {
            var c = b == null ? new BrandCrisis() : new BrandCrisis
            {
                trustIndex = b.trustIndex,
                nps = b.nps,
                crisisLevel = b.crisisLevel,
                active = b.active,
                negativeVolume = b.negativeVolume,
                responseStage = b.responseStage,
                statementTemplate = b.statementTemplate,
                feed = b.feed ?? new List<SentimentEntry>(),
                repairMeasures = b.repairMeasures ?? new List<RepairMeasure>()
            };
            return c;
        }

        // —— n75 品牌修复计划 ——
        void RenderBrandRepair()
        {
            var gs = Gs;
            if (gs == null) return;
            var b = MktManager.EnsureState(gs).brand;
            float done = 0f;
            if (b.repairMeasures != null)
                for (int i = 0; i < b.repairMeasures.Count; i++)
                    if (b.repairMeasures[i].done) done++;
            float completion = b.repairMeasures != null && b.repairMeasures.Count > 0 ? done / b.repairMeasures.Count : 0f;
            float recovered = MktCompetitionEconomics.RecoveredTrust(b.trustIndex, completion);

            L("H", "品牌修复计划", 0.0f, 0.92f, 0.5f, 0.98f, 15, UITheme.Accent, true);
            L("Board", $"当前信任指数 {b.trustIndex:F0} · 修复完成率 {completion * 100:F0}% · 预估恢复后 {recovered:F0}", 0.0f, 0.84f, 1.0f, 0.90f, 12, UITheme.TextMuted);

            L("List", "整改措施清单", 0.0f, 0.74f, 0.55f, 0.82f, 12, UITheme.TextMuted, true);
            float y = 0.64f;
            int shown = 0;
            if (b.repairMeasures != null)
                for (int i = 0; i < b.repairMeasures.Count && shown < 6; i++)
                {
                    var m = b.repairMeasures[i];
                    L("M" + i, $"{m.name} · {m.dept} {(m.done ? "✅" : "…")}", 0.0f, y, 0.55f, y + 0.06f, 11, m.done ? UITheme.Success : UITheme.TextMuted);
                    y -= 0.075f;
                    shown++;
                }
            if (shown == 0)
                L("Empty", "暂无整改措施", 0.0f, 0.64f, 0.55f, 0.72f, 12, UITheme.TextMuted);

            L("Add", "新增整改措施", 0.57f, 0.74f, 1.0f, 0.82f, 12, UITheme.TextMuted, true);
            B("A1", "服务流程整改 → 运营部", 0.57f, 0.60f, 1.0f, 0.68f, UITheme.Accent, () =>
            {
                string msg = "提交失败";
                if (MktManager.Instance != null)
                    MktManager.Instance.SubmitRepairMeasure(new RepairMeasure { name = "服务流程整改", dept = "运营部" }, out msg);
                Toast(msg); RefreshBody();
            }, 11);
            B("A2", "员工服务培训 → 人力资源部", 0.57f, 0.50f, 1.0f, 0.58f, UITheme.Accent, () =>
            {
                string msg = "提交失败";
                if (MktManager.Instance != null)
                    MktManager.Instance.SubmitRepairMeasure(new RepairMeasure { name = "员工服务培训", dept = "人力资源部" }, out msg);
                Toast(msg); RefreshBody();
            }, 11);
            B("A3", "安全审计整改 → 安全质量部", 0.57f, 0.40f, 1.0f, 0.48f, UITheme.Accent, () =>
            {
                string msg = "提交失败";
                if (MktManager.Instance != null)
                    MktManager.Instance.SubmitRepairMeasure(new RepairMeasure { name = "安全审计整改", dept = "安全质量部" }, out msg);
                Toast(msg); RefreshBody();
            }, 11);

            B("Radar", "竞对品牌恢复对比 → 竞争雷达", 0.0f, 0.30f, 0.5f, 0.38f, UITheme.PanelDeep, () => OpenPage("n60_竞争雷达"), 12);
            B("Brand", "投放广告 → 直销渠道", 0.52f, 0.30f, 1.0f, 0.38f, UITheme.PanelDeep, () => OpenPage("n70_直销渠道管理"), 12);
        }

        void RenderFallback()
        {
            L("H", "MKT 子页", 0.0f, 0.90f, 1.0f, 0.97f, 16, UITheme.TextMuted);
            L("B", "未知页面：" + _pageKind, 0.0f, 0.80f, 1.0f, 0.88f, 14, UITheme.TextMuted);
        }

        static string NormalizeKind(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (id.StartsWith("n44")) return "n44";
            if (id.StartsWith("n49")) return "n49";
            if (id.StartsWith("n45")) return "n45";
            if (id.StartsWith("n46")) return "n46";
            if (id.StartsWith("n47")) return "n47";
            if (id.StartsWith("n48")) return "n48";
            if (id.StartsWith("n50")) return "n50";
            if (id.StartsWith("n55")) return "n55";
            if (id.StartsWith("n56")) return "n56";
            if (id.StartsWith("n57")) return "n57";
            if (id.StartsWith("n58")) return "n58";
            if (id.StartsWith("n59")) return "n59";
            if (id.StartsWith("n33")) return "n33";
            if (id.StartsWith("n34")) return "n34";
            if (id.Contains("收益分析")) return "n44";
            if (id.Contains("航班定价")) return "n49";
            if (id.Contains("单航线定价")) return "n45";
            if (id.Contains("动态定价")) return "n46";
            if (id.Contains("仪表盘")) return "n47";
            if (id.Contains("辅助收入")) return "n48";
            if (id.Contains("外部环境")) return "n50";
            if (id.Contains("季节性") || id.Contains("日历")) return "n55";
            if (id.Contains("航季对比")) return "n56";
            if (id.Contains("旺季策略")) return "n57";
            if (id.Contains("历史趋势")) return "n58";
            if (id.Contains("影响预测")) return "n59";
            if (id.Contains("FFP") || id.Contains("常旅客")) return "n33";
            if (id.Contains("产品工坊")) return "n34";
            if (id.StartsWith("n60") || id.Contains("竞争雷达")) return "n60";
            if (id.StartsWith("n61") || id.Contains("竞对详情")) return "n61";
            if (id.StartsWith("n62") || id.Contains("航线网络对标")) return "n62";
            if (id.StartsWith("n63") || id.Contains("产品服务对标")) return "n63";
            if (id.StartsWith("n64") || id.Contains("运营成本对标")) return "n64";
            if (id.StartsWith("n65") || id.Contains("战略资本对标")) return "n65";
            if (id.StartsWith("n67") || id.Contains("大客户合同")) return "n67";
            if (id.StartsWith("n68") || id.Contains("渠道预算")) return "n68";
            if (id.StartsWith("n69") || id.Contains("分销渠道")) return "n69";
            if (id.StartsWith("n70") || id.Contains("直销渠道")) return "n70";
            if (id.StartsWith("n71") || id.Contains("渠道ROI")) return "n71";
            if (id.StartsWith("n72") || id.Contains("品牌危机公关")) return "n72";
            if (id.StartsWith("n73") || id.Contains("舆情监测")) return "n73";
            if (id.StartsWith("n74") || id.Contains("危机响应")) return "n74";
            if (id.StartsWith("n75") || id.Contains("品牌修复")) return "n75";
            return id;
        }
    }
}
