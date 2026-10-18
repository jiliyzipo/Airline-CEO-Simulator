using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>FLEET / NET 子页共享上下文（跨页面传参）。</summary>
    public static class FleetNetContext
    {
        public static string SelectedRegistration;
        public static string SelectedRouteId;
        public static string SelectedCityIata;
        public static bool OpenEconomyTab;
        /// <summary>深链：进入 n12 时直接切到「航权与时刻槽」页签（收件箱「详见」与导航 n77 用）。</summary>
        public static bool OpenRightsTab;
        public static string PurchaseTypeId;    // n97 购机会议选中机型
        public static int PurchaseCount = 1;    // n97 购机数量
    }

    /// <summary>
    /// FLEET 子页：n10 飞机详情 / n11 载客量配置 / n107 大修预算确认。
    /// 运行时构建（无 Prefab），导航栈驱动。
    /// </summary>
    public class FleetSubpagePanel : BasePanel
    {
        string _pageKind;
        Transform _content;
        TextMeshProUGUI _toast;

        // n11 舱位编辑态
        int _firstSeats;
        int _businessSeats;
        int _economySeats;

        // n107 大修类型选择态
        string _overhaulKind = FleetEconomics.OverhaulC;

        public static FleetSubpagePanel Build(string pageKind, string title)
        {
            var root = UIFactory.Panel(null, "FleetSub_" + pageKind, UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.1f, 0.72f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.94f), Vector2.zero, Vector2.zero),
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
                new UIFactory.Anchor(new Vector2(0.20f, 0.005f), new Vector2(0.80f, 0.03f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            var panel = root.AddComponent<FleetSubpagePanel>();
            panel._pageKind = NormalizeKind(pageKind);
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
                _pageKind = NormalizeKind(PageId);
        }

        public void RefreshBody()
        {
            ResolveRefs();
            if (_content == null) return;
            Clear(_content);

            switch (_pageKind)
            {
                case "n10": RenderDetail(); break;
                case "n11": RenderSeats(); break;
                case "n107": RenderOverhaul(); break;
                default: RenderFallback(); break;
            }
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
            var l = UIFactory.Label(_content, name, text, size, align,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.TextPrimary, bold ? FontStyles.Bold : FontStyles.Normal);
            return l;
        }

        Button B(string name, string text, float x0, float y0, float x1, float y1,
            Color? color, UnityEngine.Events.UnityAction action, int size = 13)
        {
            return UIFactory.Button(_content, name, text,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.Accent, action, size);
        }

        Aircraft CurrentAircraft()
        {
            if (FleetManager.Instance == null) return null;
            string reg = FleetNetContext.SelectedRegistration;
            if (string.IsNullOrEmpty(reg))
                reg = OpsSubpageContext.SelectedRegistration;
            return FleetManager.Instance.GetAircraftByRegistration(reg);
        }

        List<RouteData> RoutesFor(Aircraft ac)
        {
            var list = new List<RouteData>();
            var gs = GameState.Instance;
            if (ac == null || gs?.activeRoutes == null) return list;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r != null && r.IsFlownBy(ac.registration))
                    list.Add(r);
            }
            return list;
        }

        // ———————————————————————— 该机的班次与周排班 ————————————————————————

        /// <summary>
        /// 值勤预算一行（R9/R10/R12）：把该机当日各值勤期的报到 → 落地、实耗 / 上限、
        /// 剩余可用，以及机型口径（窄体分级表 / 宽体增援）如实呈现，便于 CEO 理解「为什么一天只能飞这么多」。
        /// </summary>
        string DutyLine(Aircraft ac, List<Leg> legs)
        {
            if (ac == null || legs == null || legs.Count == 0) return "";
            var plan = new OpsRotationLogic.RotationPlan();
            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                if (leg?.route == null || leg.flight == null) continue;
                plan.Assign(ac.registration, OpsRotationLogic.WindowOf(leg.route, leg.flight.depTime));
            }
            if (plan.TurnCount(ac.registration) == 0) return "";

            bool wide = AircraftCatalog.IsWidebody(ac.type);
            string head = wide
                ? $"值勤预算　{ac.type} 宽体增援（{AircraftCatalog.RestClassLabel(AircraftCatalog.RestClassOf(ac.type))}）"
                : $"值勤预算　{ac.type} 窄体（2 人制，上限随航段数递减）";

            // 报到 → 落地 / 实耗 / 剩余 / 口径说明一律由 `DutySummary` 给出（它已含外站换组的
            // 两段值勤期）—— 旧版在这里**另算一遍**剩余，洲际线于是印出「实耗 19.3h（… ≤15h）」加
            // 「剩余 −4.8h」这种自相矛盾的行（第九次报障）。一处口径，只此一处。
            return $"{head}　｜　{plan.DutySummary(ac.registration, ac.type)}";
        }

        /// <summary>一架飞机在一个时段的执行班（用于周排班表，可跨多条航线）。</summary>
        class Leg
        {
            public RouteData route;
            public RouteFlight flight;
            public bool[] days;          // 0=周一 … 6=周日
            public string ringId = "";   // 若该段属于本机某个航班环，记其编号
            public string Slot => string.IsNullOrEmpty(flight.slotLabel) ? "—" : flight.slotLabel;
        }

        /// <summary>汇总本机所有排班班次（按起飞时刻排序、同航线同时刻去重）。</summary>
        List<Leg> LegsFor(GameState gs, Aircraft ac)
        {
            var list = new List<Leg>();
            if (gs?.activeRoutes == null || ac == null) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                r.EnsureFlights();
                for (int k = 0; k < r.flights.Count; k++)
                {
                    var f = r.flights[k];
                    if (f == null || string.IsNullOrEmpty(f.registration)) continue;
                    if (!string.Equals(f.registration, ac.registration, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seen.Add(r.routeId + "@" + f.depTime)) continue;
                    list.Add(new Leg
                    {
                        route = r,
                        flight = f,
                        days = ParseDays(f.daysOfWeek),
                        ringId = RingIdForRoute(gs, ac.registration, r.routeId)
                    });
                }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.flight.depTime ?? "", b.flight.depTime ?? ""));
            return list;
        }

        /// <summary>班期字符串 → 周一~周日布尔（"每日"全周；"一三五"/"二四六"按字取）。</summary>
        static bool[] ParseDays(string daysOfWeek) => FlightScheduleFormat.DaysOf(daysOfWeek);

        static string RingIdForRoute(GameState gs, string reg, string routeId)
        {
            if (gs?.flightRings == null || string.IsNullOrEmpty(reg) || string.IsNullOrEmpty(routeId)) return "";
            for (int i = 0; i < gs.flightRings.Count; i++)
            {
                var ring = gs.flightRings[i];
                if (ring == null || !ring.valid) continue;
                if (!string.Equals(ring.registration, reg, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(ring.legARouteId, routeId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ring.legBRouteId, routeId, StringComparison.OrdinalIgnoreCase))
                    return ring.ringId;
            }
            return "";
        }

        static string RouteSummary(List<RouteData> routes)
        {
            if (routes == null || routes.Count == 0) return "暂无（该机尚未分配定期航线）";
            var sb = new StringBuilder();
            for (int i = 0; i < routes.Count && i < 3; i++)
            {
                if (i > 0) sb.Append("　｜　");
                var r = routes[i];
                int wd = r.WeeklyFlightsDeparture();
                int wu = r.WeeklyFlightsDuty();
                string freq = wd == wu ? $"周 {wd} 班" : $"离港周 {wd} 班／值勤周 {wu} 班";
                sb.Append($"{r.origin}→{r.destination}（{freq} · 日利 {r.dailyProfit:F1} 万）");
            }
            if (routes.Count > 3) sb.Append($"　… 共 {routes.Count} 条");
            return sb.ToString();
        }

        static string RingSummary(GameState gs, Aircraft ac)
        {
            var sb = new StringBuilder();
            int n = 0;
            if (gs?.flightRings != null && ac != null)
            {
                for (int i = 0; i < gs.flightRings.Count; i++)
                {
                    var ring = gs.flightRings[i];
                    if (ring == null) continue;
                    if (!string.Equals(ring.registration, ac.registration, StringComparison.OrdinalIgnoreCase)) continue;
                    sb.Append(n > 0 ? "　｜　" : "");
                    sb.Append($"{ring.ringId}（过站 {ring.turnMinutes} 分 · {(ring.valid ? "合格" : "未通过")}）");
                    n++;
                }
            }
            if (n == 0)
                return "每条班次均按往返执行（去程 + 回程 2 段/班）；未跨航线串环。可在「分配航线」串环，或由 CEO 运力指令自动编环";
            return sb.ToString();
        }

        /// <summary>该机当日班次共同的出发基地（不一致时提示多基地）。</summary>
        static string BaseAirport(List<Leg> legs)
        {
            if (legs == null || legs.Count == 0) return "—";
            string b = legs[0].route != null ? legs[0].route.origin : "";
            for (int i = 1; i < legs.Count; i++)
            {
                if (legs[i].route != null && !string.Equals(legs[i].route.origin, b, StringComparison.OrdinalIgnoreCase))
                    return "多基地";
            }
            return string.IsNullOrEmpty(b) ? "—" : b;
        }

        // —— n10 飞机详情（履历）——
        void RenderDetail()
        {
            var ac = CurrentAircraft();
            if (ac == null)
            {
                L("H", "飞机详情", 0.0f, 0.90f, 1.0f, 0.97f, 16, UITheme.TextMuted, true);
                L("E", "未选择飞机。请从机队页点击飞机进入。", 0.0f, 0.78f, 1.0f, 0.86f, 14, UITheme.Warning);
                return;
            }

            var gs = GameState.Instance;
            var routes = RoutesFor(ac);
            var legs = LegsFor(gs, ac);
            float residual = FleetManager.CalcResidualValue(ac);
            bool disposed = !string.IsNullOrEmpty(ac.ExitLabel);
            // 已退役 / 已退租：月维护与月租同步停止（FleetManager.MonthlyMaintOf 与 GameState.FleetMonthlyLease 同口径）
            float maint = disposed ? 0f : FleetEconomics.MonthlyMaintenance(ac.age);
            string statusWord = disposed ? "已" + ac.ExitLabel : ac.status;
            Color statusColor = ac.status == AircraftStatuses.Airworthy ? UITheme.Success
                : ac.status == AircraftStatuses.Maintenance ? UITheme.Warning : UITheme.TextMuted;
            string ovh = string.IsNullOrEmpty(ac.lastOverhaulType) ? "无记录" : $"{ac.lastOverhaulType}（{ac.lastOverhaulYear} 年）";

            // 顶栏：身份与状态
            L("Reg", $"{ac.registration} · {ac.type}", 0.0f, 0.885f, 0.55f, 0.965f, 18, UITheme.TextPrimary, true);
            L("Name", $"「{(string.IsNullOrEmpty(ac.aircraftName) ? "未命名" : ac.aircraftName)}」 · {ac.source}", 0.55f, 0.885f, 1.0f, 0.965f, 13,
                UITheme.TextMuted, false, TextAlignmentOptions.MidlineRight);
            L("Status", $"状态 {statusWord} · 机龄 {ac.age} 年 · 机况 {ac.condition:F0}% · 舱位 头 {ac.firstClassSeats}/商 {ac.businessSeats}/经 {ac.economySeats}（总 {ac.TotalSeats}） · 估值 {residual:F0} 万 · 月维护 {maint:F1} 万 · 累计 {ac.flightHours:F0} FH / {ac.flightCycles} FC · 最近大修 {ovh}",
                0.0f, 0.815f, 1.0f, 0.885f, 11, statusColor, true);

            // 执飞航线 + 日循环 + 航班环
            string rotationPart = legs.Count > 0
                ? $"　｜　日循环 基地 {BaseAirport(legs)} · {legs.Count} 段往返（{legs.Count * 2} 个航段）"
                : "";
            L("Routes", "执飞航线　" + RouteSummary(routes) + rotationPart, 0.0f, 0.745f, 1.0f, 0.815f, 11, UITheme.TextMuted);

            string ringSummary = RingSummary(gs, ac);
            L("Ring", "航班环　" + ringSummary, 0.0f, 0.712f, 1.0f, 0.745f, 11,
                ringSummary.StartsWith("每条班次") ? UITheme.TextMuted : UITheme.Success, true);

            // 值勤预算（R9/R10/R12）：受 FDP 上限约束的「一天能飞几段」口径，显示报到 / 上限 / 剩余
            string dutyPart = DutyLine(ac, legs);
            if (!string.IsNullOrEmpty(dutyPart))
                L("Duty", dutyPart, 0.0f, 0.675f, 1.0f, 0.712f, 11, UITheme.Warning, true);

            var grid = UIFactory.Panel(_content, "AsGrid",
                new UIFactory.Anchor(new Vector2(0f, 0.27f), new Vector2(1f, 0.665f), Vector2.zero, Vector2.zero),
                new Color(0.03f, 0.06f, 0.10f, 0.95f));
            UIFactory.AddBorder(grid, UITheme.Border, new Vector2(1f, -1f));
            var asBlocks = AsWeekGridUI.Collect(gs, ac.registration, null, -1);
            AsWeekGridUI.Draw(grid.transform, asBlocks);

            // 操作栏
            // 「退役」/「退租」按产权分：自有 / 二手 → 退役回收残值；租赁 → 交还出租人（融资租赁另付违约金）
            bool leased = FleetManager.IsLeased(ac);
            float exitFee = leased ? FleetManager.LeaseReturnCost(gs, ac) : 0f;
            string exitLabel = disposed ? "已" + ac.ExitLabel
                : leased ? (exitFee > 0.01f ? $"退租（违约金 {exitFee:F0}）" : "退租")
                : $"转售/退役(+{residual:F0})";
            B("Seat", "载客量配置", 0.0f, 0.16f, 0.20f, 0.25f, UITheme.Accent, () => FleetUI.OpenFleetPage("n11_座位配置"), 13);
            B("Assign", "分配航线", 0.22f, 0.16f, 0.40f, 0.25f, UITheme.Accent, AssignRoute, 13);
            B("Retire", exitLabel, 0.42f, 0.16f, 0.66f, 0.25f, UITheme.Danger, Retire, 12);
            B("Overhaul", "安排大修", 0.68f, 0.16f, 0.86f, 0.25f, UITheme.Warning, () => FleetUI.OpenFleetPage("n107_预算确认"), 13);
            B("Replace", "申请替换", 0.88f, 0.16f, 1.0f, 0.25f, UITheme.PanelDeep, () => FleetUI.OpenPurchaseMeeting(), 11);
        }

        void AssignRoute()
        {
            var ac = CurrentAircraft();
            if (ac != null) OpsSubpageContext.SelectedRegistration = ac.registration;
            OpsPanelUI.OpenOpsPage("n98_航线航班环选择器");
        }

        void Retire()
        {
            var ac = CurrentAircraft();
            if (ac == null || FleetManager.Instance == null) return;
            bool ok = FleetManager.Instance.TryRetireAircraft(ac.registration, out string msg);
            Toast(msg);
            if (ok) BackOne();
        }

        // —— n11 载客量配置 ——
        void RenderSeats()
        {
            var ac = CurrentAircraft();
            if (ac == null)
            {
                L("H", "载客量配置", 0.0f, 0.90f, 1.0f, 0.97f, 16, UITheme.TextMuted, true);
                L("E", "未选择飞机。", 0.0f, 0.78f, 1.0f, 0.86f, 14, UITheme.Warning);
                return;
            }

            if (_firstSeats == 0 && _businessSeats == 0 && _economySeats == 0)
            {
                _firstSeats = ac.firstClassSeats;
                _businessSeats = ac.businessSeats;
                _economySeats = ac.economySeats;
            }

            L("H", $"{ac.registration} · 舱位平面图", 0.0f, 0.90f, 1.0f, 0.97f, 16, UITheme.Accent, true);
            L("Hint", "调整各舱座位数；收益随高端舱位占比变化（权重 头等 2× / 商务 1×）", 0.0f, 0.82f, 1.0f, 0.89f, 12, UITheme.TextMuted);

            SeatRow("头等舱", 0.70f, () => _firstSeats, v => _firstSeats = v);
            SeatRow("商务舱", 0.56f, () => _businessSeats, v => _businessSeats = v);
            SeatRow("经济舱", 0.42f, () => _economySeats, v => _economySeats = v);

            float mult = FleetEconomics.SeatMixRevenueMultiplier(_firstSeats, _businessSeats, _economySeats);
            L("Tot", $"总座位 {_firstSeats + _businessSeats + _economySeats} · 收益乘数 ×{mult:F3}",
                0.0f, 0.30f, 1.0f, 0.37f, 14, UITheme.Success, true);

            B("Save", "保存配置", 0.0f, 0.18f, 0.32f, 0.27f, UITheme.Accent, SaveSeats, 15);
            B("Reset", "恢复默认", 0.34f, 0.18f, 0.60f, 0.27f, UITheme.PanelDeep, ResetSeats, 14);
        }

        void SeatRow(string label, float y, Func<int> get, Action<int> set)
        {
            L("Lbl_" + label, label, 0.0f, y, 0.24f, y + 0.075f, 14);
            B("Minus_" + label, "−10", 0.26f, y, 0.40f, y + 0.075f, UITheme.PanelDeep, () => { set(Mathf.Max(0, get() - 10)); RefreshBody(); }, 13);
            L("Val_" + label, get().ToString(), 0.42f, y, 0.58f, y + 0.075f, 16, UITheme.TextPrimary, true, TextAlignmentOptions.Center);
            B("Plus_" + label, "+10", 0.60f, y, 0.74f, y + 0.075f, UITheme.PanelDeep, () => { set(get() + 10); RefreshBody(); }, 13);
        }

        void SaveSeats()
        {
            var ac = CurrentAircraft();
            if (ac == null || FleetManager.Instance == null) return;
            bool ok = FleetManager.Instance.ApplySeatConfig(ac.registration, _firstSeats, _businessSeats, _economySeats, out string msg);
            Toast(msg);
            if (ok) BackOne();
        }

        void ResetSeats()
        {
            var ac = CurrentAircraft();
            if (ac == null) return;
            var cat = DataBase.Instance != null ? DataBase.Instance.GetAircraftType(ac.type) : null;
            int eco = cat != null ? cat.maxPassengers : EconomyBalanceParams.DefaultSeatsFor(ac.type);
            _firstSeats = 0;
            _businessSeats = 0;
            _economySeats = eco;
            RefreshBody();
        }

        // —— n107 大修预算确认 ——
        void RenderOverhaul()
        {
            var ac = CurrentAircraft();
            if (ac == null)
            {
                L("H", "预算确认", 0.0f, 0.90f, 1.0f, 0.97f, 16, UITheme.TextMuted, true);
                L("E", "未选择飞机。", 0.0f, 0.78f, 1.0f, 0.86f, 14, UITheme.Warning);
                return;
            }

            bool wide = EconomyBalanceParams.IsWidebody(ac.type ?? "");
            float cost = FleetEconomics.OverhaulCost(_overhaulKind, wide);
            int days = FleetEconomics.OverhaulDays(_overhaulKind);
            float contingency = cost * 0.12f;
            var gs = GameState.Instance;

            L("H", $"{ac.registration} · 大修预算确认", 0.0f, 0.90f, 1.0f, 0.97f, 16, UITheme.Warning, true);
            L("Info", $"{ac.type} · 状态 {ac.status} · 机龄 {ac.age} 年", 0.0f, 0.82f, 1.0f, 0.89f, 13, UITheme.TextMuted);

            L("Kind", "大修类型", 0.0f, 0.72f, 0.40f, 0.78f, 13, UITheme.Accent, true);
            var kinds = FleetEconomics.OverhaulKinds;
            float y = 0.64f;
            for (int i = 0; i < kinds.Length; i++)
            {
                string k = kinds[i];
                var color = k == _overhaulKind ? UITheme.NavActive : UITheme.PanelDeep;
                B("K" + i, k, 0.0f, y, 0.40f, y + 0.052f, color, () => { _overhaulKind = k; RefreshBody(); }, 12);
                y -= 0.062f;
            }

            // 预算面板（右侧）
            L("Budget", "预算与财务", 0.44f, 0.72f, 1.0f, 0.78f, 13, UITheme.Accent, true);
            L("B1", $"预估费用 {cost:F0} 万", 0.44f, 0.64f, 1.0f, 0.70f, 13);
            L("B2", $"不可预见费(+12%) {contingency:F0} 万", 0.44f, 0.57f, 1.0f, 0.63f, 13);
            L("B3", $"停场 {days} 天 · 状态→维护中", 0.44f, 0.50f, 1.0f, 0.56f, 13, UITheme.Warning);
            L("B4", $"当前现金 {gs?.cash ?? 0:F0} 万", 0.44f, 0.43f, 1.0f, 0.49f, 13, UITheme.TextMuted);

            B("Confirm", "确认排程", 0.44f, 0.30f, 0.74f, 0.39f, UITheme.Accent, ConfirmOverhaul, 15);
            L("Note", "确认后锁定大修窗口并占用预算，写入机务维修甘特图", 0.44f, 0.22f, 1.0f, 0.28f, 11, UITheme.TextMuted);
        }

        void ConfirmOverhaul()
        {
            var ac = CurrentAircraft();
            if (ac == null || FleetManager.Instance == null) return;
            bool ok = FleetManager.Instance.ScheduleOverhaul(ac.registration, _overhaulKind, out string msg);
            Toast(msg);
            if (ok) BackOne();
        }

        void RenderFallback()
        {
            L("H", "FLEET 子页", 0.0f, 0.90f, 1.0f, 0.97f, 16, UITheme.TextMuted);
            L("B", "未知页面：" + _pageKind, 0.0f, 0.80f, 1.0f, 0.88f, 14, UITheme.TextMuted);
        }

        static string NormalizeKind(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (id.StartsWith("n10_") || id.Contains("详情")) return "n10";
            if (AirlineCEO.UI.Navigation.PageIdMatch.Is(id, "n11") || id.Contains("座位") || id.Contains("载客量")) return "n11";
            if (id.StartsWith("n107") || id.Contains("大修") || id.Contains("预算")) return "n107";
            return id;
        }
    }
}
