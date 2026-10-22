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
    /// Phase 2 OPS L1：KPI + 航线列表 + 进入 L2（周计划/航季/构建航班环）。
    /// </summary>
    public class OpsPanelUI : MonoBehaviour
    {
        TextMeshProUGUI _kpiText;
        TextMeshProUGUI _hintText;
        TextMeshProUGUI _fuelLinkText;
        Transform _routeCardsRoot;
        readonly List<GameObject> _cards = new List<GameObject>();
        Button _openRouteBtn;
        System.Action _onOpenRoute;

        public Transform RouteCardsRoot => _routeCardsRoot;
        public Button OpenRouteButton => _openRouteBtn;

        public static OpsPanelUI Build(Transform parent, System.Action onOpenRoute)
        {
            var panel = UIFactory.Panel(parent, "Panel_Ops", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<OpsPanelUI>();
            ui._onOpenRoute = onOpenRoute;
            ui.BuildChrome(panel.transform);
            return ui;
        }

        void BuildChrome(Transform root)
        {
            UIFactory.Label(root, "Title", "运营部 · 航线与调度", 22, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.91f), new Vector2(0.55f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _openRouteBtn = UIFactory.Button(root, "OpenRouteButton", "开通航线",
                new UIFactory.Anchor(new Vector2(0.72f, 0.90f), new Vector2(0.96f, 0.97f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => _onOpenRoute?.Invoke(), 16);

            UIFactory.Button(root, "EventLogBtn", "事件日志",
                new UIFactory.Anchor(new Vector2(0.56f, 0.90f), new Vector2(0.70f, 0.97f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => FindFirstObjectByType<EventLogUI>()?.Show(), 13);

            _kpiText = UIFactory.Label(root, "Kpi", "KPI 加载中…", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.78f), new Vector2(0.62f, 0.90f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _kpiText.textWrappingMode = TextWrappingModes.Normal;

            _fuelLinkText = UIFactory.Label(root, "FuelLink", "燃油：—", 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.64f, 0.82f), new Vector2(0.96f, 0.89f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _fuelLinkText.textWrappingMode = TextWrappingModes.Normal;   // 第二行放现实报价基准

            _hintText = UIFactory.Label(root, "Hint", "开通航线后可编制周计划与航班环", 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.72f), new Vector2(0.55f, 0.78f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Button(root, "BtnSeason", "航季总纲",
                new UIFactory.Anchor(new Vector2(0.03f, 0.64f), new Vector2(0.22f, 0.71f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => OpenOpsPage("n66_航季总纲"), 13);
            UIFactory.Button(root, "BtnWeek", "航班周计划",
                new UIFactory.Anchor(new Vector2(0.24f, 0.64f), new Vector2(0.45f, 0.71f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => OpenOpsPage("n42_航班周总计划"), 13);
            UIFactory.Button(root, "BtnRing", "构建航班环",
                new UIFactory.Anchor(new Vector2(0.47f, 0.64f), new Vector2(0.68f, 0.71f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => OpenOpsPage("n36_航班环构建器"), 13);
            UIFactory.Button(root, "BtnRecalc", "重算调度",
                new UIFactory.Anchor(new Vector2(0.70f, 0.64f), new Vector2(0.96f, 0.71f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, RecalcAllRoutes, 13);

            UIFactory.Button(root, "BtnIrrops", "航班异常",
                new UIFactory.Anchor(new Vector2(0.03f, 0.56f), new Vector2(0.18f, 0.63f), Vector2.zero, Vector2.zero),
                UITheme.Danger, OpenIrrops, 12);
            UIFactory.Button(root, "BtnBuyAdvise", "购机建议",
                new UIFactory.Anchor(new Vector2(0.20f, 0.56f), new Vector2(0.38f, 0.63f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, SubmitFleetAdvice, 12);
            UIFactory.Button(root, "BtnAssign", "航线分配",
                new UIFactory.Anchor(new Vector2(0.40f, 0.56f), new Vector2(0.58f, 0.63f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenOpsPage("n98_航线航班环选择器"), 12);
            UIFactory.Button(root, "BtnStg", "战略·时刻",
                new UIFactory.Anchor(new Vector2(0.60f, 0.56f), new Vector2(0.78f, 0.63f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenShellTab("n16_战略发展部"), 12);
            UIFactory.Button(root, "BtnMx", "机务·停飞",
                new UIFactory.Anchor(new Vector2(0.80f, 0.56f), new Vector2(0.96f, 0.63f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenShellTab("n17_机务工程部"), 12);

            UIFactory.Label(root, "RoutesTitle", "已开通航线", 15, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.50f), new Vector2(0.4f, 0.56f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var listHost = UIFactory.Panel(root, "RouteList",
                new UIFactory.Anchor(new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.50f), Vector2.zero, Vector2.zero),
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
        }

        public void SetHint(string text)
        {
            if (_hintText != null) _hintText.text = text;
        }

        public void SetOpenRouteInteractable(bool on)
        {
            if (_openRouteBtn != null) _openRouteBtn.interactable = on;
        }

        public void Refresh()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            float avgDelay = 0f;
            int n = 0;
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    var m = OpsEconomics.Evaluate(
                        r.distanceKm > 0 ? r.distanceKm : 2000f,
                        r.weeklyFlights > 0 ? r.weeklyFlights : 7,
                        r.aircraftType,
                        r.groundStaffAssigned,
                        gs.onTimePercent);
                    avgDelay += m.delayProbability;
                    n++;
                }
            }
            if (n > 0) avgDelay /= n;

            int airworthy = FleetManager.Instance != null ? FleetManager.Instance.CountAirworthy(gs) : 0;
            _kpiText.text =
                $"准点率 {gs.onTimePercent:F0}% · 客座示意 {gs.loadFactorPercent:F0}% · 预测延误 {avgDelay * 100f:F0}%\n" +
                $"适航飞机 {airworthy} · 开通航线 {(gs.activeRoutes?.Count ?? 0)} · 油价 {gs.fuelPricePerTon:F0} $/t";

            int hedges = gs.hedgeContracts?.Count ?? 0;
            _fuelLinkText.text = hedges > 0
                ? $"燃油联动：现货 {gs.fuelPricePerTon:F0} · 对冲合约 {hedges} 份（详见财务部）"
                : $"燃油联动：现货 {gs.fuelPricePerTon:F0} $/t · 尚未套保（财务部可开对冲）";
            _fuelLinkText.text += "\n" + ExternalDataFetcher.QuoteStatusLine(compact: true);

            if ((gs.activeRoutes?.Count ?? 0) == 0)
                SetHint("点「开通航线」：选机场 → 决策（批准/暂缓）→ 再去周计划或航班环");
            else
                SetHint("可编制航季/周计划，或进入「构建航班环」串联航班");

            RebuildCards(gs);
        }

        void RebuildCards(GameState gs)
        {
            for (int i = 0; i < _cards.Count; i++)
                if (_cards[i] != null) Destroy(_cards[i]);
            _cards.Clear();
            if (_routeCardsRoot == null || gs.activeRoutes == null) return;

            if (gs.activeRoutes.Count == 0)
            {
                var empty = UIFactory.Label(_routeCardsRoot, "Empty",
                    "暂无航线 — 点「开通航线」或去「市场」分析后执行开通建议",
                    15, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(0, 0.6f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                empty.textWrappingMode = TextWrappingModes.Normal;
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
                _cards.Add(empty.gameObject);
                return;
            }

            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var route = gs.activeRoutes[i];
                var card = RouteCard.BuildRuntime(_routeCardsRoot);
                card.Bind(route);
                var le = card.gameObject.GetComponent<LayoutElement>();
                if (le == null) le = card.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 72;
                le.flexibleWidth = 1;
                _cards.Add(card.gameObject);

                var btn = card.gameObject.GetComponent<Button>();
                if (btn == null) btn = card.gameObject.AddComponent<Button>();
                string detailId = route.routeId;
                btn.onClick.AddListener(() =>
                {
                    // 这里是航线卡片（不是某个班次）：写 -1 表示按第 0 班显示，
                    // 顺带把「上一条线指定过的第 N 班」作废，免得停在一个不相干的班次上。
                    OpsSubpageContext.SelectFlight(detailId, -1);
                    OpenOpsPage("n43_航班详细计划");
                });
            }
        }

        public void RecalcAllRoutes()
        {
            var gs = GameState.Instance;
            if (gs?.activeRoutes == null) return;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                // 负基准＝真实亏损，必须原样透传；只有「从未设过」（|值|≈0）才用 RPM 重推基准。
                // （原先写成 `<= 0.01f`，把亏损基准一并当成未设，一按「重算全部航线」即把亏损抹平。）
                float baseline = OpsEconomics.ResolvedSingleFlightBaseline(r);
                if (Mathf.Abs(r.baseDailyProfit) <= 0.01f && r.distanceKm > 0)
                {
                    var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
                    {
                        distance = r.distanceKm,
                        fuelPricePerTon = gs.fuelPricePerTon,
                        competitorSupply = 0.4f,
                        demandMultiplier = 1.1f,
                        brandEquity = gs.brandAwareness,
                        aircraftType = r.aircraftType,
                        seats = EconomyBalanceParams.DefaultSeatsFor(r.aircraftType),
                        passengerType = "leisure"
                    });
                    baseline = rpm.dailyProfit;
                    r.score = rpm.score;
                    r.rating = rpm.rating;
                }
                OpsEconomics.ApplyToRoute(r, baseline, gs.onTimePercent);
                // 多班次：按原比例分摊**毛贡献**（不含航线周期固定成本）。
                // 不变量：Σ flights == 毛贡献，而 route.dailyProfit == 毛贡献 − 固定成本；
                // 若把固定成本也摊进各班，RecalcDailyProfit 会重复计提。
                r.EnsureFlights();
                if (r.flights.Count > 1)
                {
                    float oldSum = 0f;
                    for (int fi = 0; fi < r.flights.Count; fi++) oldSum += r.flights[fi].dailyProfit;
                    if (oldSum > 0.001f)
                    {
                        float scale = r.GrossDailyProfit / oldSum;
                        for (int fi = 0; fi < r.flights.Count; fi++) r.flights[fi].dailyProfit *= scale;
                    }
                }
                r.SyncPrimaryFromFlights();
            }

            float avgDelay = 0f;
            int n = 0;
            foreach (var r in gs.activeRoutes)
            {
                if (r == null) continue;
                avgDelay += OpsEconomics.Evaluate(r.distanceKm, r.weeklyFlights, r.aircraftType,
                    r.groundStaffAssigned, gs.onTimePercent).delayProbability;
                n++;
            }
            if (n > 0)
                gs.onTimePercent = OpsEconomics.ProjectOnTimePercent(gs.onTimePercent, avgDelay / n);

            gs.NotifyChanged();
            Refresh();
        }

        public static void OpenOpsPage(string pageId)
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
                Debug.LogWarning("[OPS] NavigationRouter 未就绪：" + pageId);
        }

        public static void OpenShellTab(string pageId)
        {
            var nav = FindFirstObjectByType<PrefabNavIntegration>();
            if (nav?.Shell != null)
            {
                nav.Shell.Select(pageId, invoke: true);
                return;
            }

            var bootstrap = FindFirstObjectByType<MVPBootstrap>();
            bootstrap?.HandleShellNavigation(pageId);
        }

        public static void OpenIrrops()
        {
            OpsIrropsModalUI.PendingMailId = null;
            var kinds = new[]
            {
                OpsIrropsLogic.IrropsKind.Weather,
                OpsIrropsLogic.IrropsKind.Aog,
                OpsIrropsLogic.IrropsKind.Atc,
                OpsIrropsLogic.IrropsKind.CrewShortage
            };
            int idx = (Time.frameCount / 7) % kinds.Length;
            OpenIrrops(kinds[idx]);
        }

        public static void OpenIrrops(OpsIrropsLogic.IrropsKind kind)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                OpenOpsPage("n24_危机_IRROPS_弹窗");
                return;
            }
            OpsIrropsModalUI.EnsureOverlay(canvas.transform, kind);
        }

        static void SubmitFleetAdvice()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            int airworthy = FleetManager.Instance != null ? FleetManager.Instance.CountAirworthy(gs) : 0;
            int routes = gs.activeRoutes?.Count ?? 0;
            int gap = CapacityPlanning.Gap(routes, airworthy);
            string msg = gap > 0
                ? $"运力缺口约 {gap} 架：已提交购机/租机议题到收件箱。"
                : "当前运力基本覆盖航网；可按战略规划预购窄体机作备份。";
            gs.AddDecisionLog($"【运营部→CEO 购机建议】适航 {airworthy} · 航线 {routes}。{msg}");
            if (gap > 0) InboxManager.QueueCapacityMail(gs, gap);
            gs.NotifyChanged();
            FindFirstObjectByType<EventLogUI>()?.Show();
            SetHintStatic(msg);
            OpenShellTab("n7_收件箱");
        }

        static void SetHintStatic(string text)
        {
            var ui = FindFirstObjectByType<OpsPanelUI>();
            ui?.SetHint(text);
        }
    }

    public static class OpsSubpageContext
    {
        public static string SelectedRouteId;
        public static string SelectedRingNote;
        public static string SelectedRegistration;

        /// <summary>
        /// n43 要看的**班次下标**（0 起；-1 / 越界都按第 0 班处理）。
        ///
        /// 2026-10-19：n43「航班飞行计划」从航线级改成真正的单班页后，光有 routeId 定位不到班次 ——
        /// 一条线一天可能 3 波，标题写 NS108 却给整条线的数据（旧版正是如此）。
        /// 必须与「这个下标属于哪条航线」成对记录，否则从邮件点进 NS110 之后再从导航树打开
        /// n43，会把别的航线也停在「第 3 班」—— 看着跳了，跳的是别人。
        /// </summary>
        public static int SelectedFlightIndex = -1;

        /// <summary><see cref="SelectedFlightIndex"/> 属于哪条航线；空 / 不等即视为未指定。</summary>
        static string _flightIndexRouteId;

        /// <summary>成对写下「航线 + 班次」。凡是打开 n43 的入口都必须走这里。</summary>
        public static void SelectFlight(string routeId, int flightIndex)
        {
            SelectedRouteId = routeId;
            SelectedFlightIndex = flightIndex;
            _flightIndexRouteId = routeId;
        }

        /// <summary>该航线上次被指定的班次下标；不是同一条线就返回 -1（按第 0 班处理）。</summary>
        public static int FlightIndexOf(string routeId)
            => string.Equals(_flightIndexRouteId, routeId, System.StringComparison.Ordinal)
                ? SelectedFlightIndex
                : -1;
    }
}
