using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using AirlineCEO.UI.Shell;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// n41 单航线（L2）：从航线网络点入。运行时构建（无 Prefab），导航栈驱动。
    /// CEO 在此查看运力体检与班次周排班。
    ///
    /// ★2026-10-28 用户裁定：**删掉本页底部的运力指令框**。它和底部 CEO 命令台是两条
    /// 入口做同一件事（同一条 <c>CapacityDirectiveLogic</c> 管线），而命令台是全局的。
    /// 句子照旧在那条命令条上说，本页只负责「看」。
    /// 直接后果是正文可以往下铺满原来被指令框占掉的 26% 高度 —— 班次列表与周排班都变长。
    /// </summary>
    public class RouteDetailPanel : BasePanel
    {
        // ———————————————————————— 版面分段 ————————————————————————
        // 内容区（Card/Content）纵向只有一段：正文铺满 [BodyBottom .. 1.00]。
        // 原先底部 26% 归「运力调整指令」框，框拆掉后这段高度还给正文。
        // 每一段内的文字仍各自占用独立区间，并限制换行/行数，避免挤压重叠。
        /// <summary>正文可用区下边界（留一点边距，不贴内容框底）。</summary>
        const float BodyBottom = 0.02f;

        /// <summary>班次列表单行最小节距（低于此值就减少显示行数，不硬挤）。</summary>
        const float ListMinRowPitch = 0.065f;

        Transform _content;
        TextMeshProUGUI _toast;

        /// <summary>当前查看的班次下标；-1 表示航线总览（班次列表）。</summary>
        int _selFlight = -1;
        /// <summary>上一次渲染的航线（换航线时回到总览，避免停在旧下标）。</summary>
        string _lastRouteId;

        public static RouteDetailPanel Build(string title)
        {
            var root = UIFactory.Panel(null, "RouteDetail", UIFactory.Fill,
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
            content.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;

            var toast = UIFactory.Label(card.transform, "Toast", "", 12,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.20f, 0.005f), new Vector2(0.80f, 0.03f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            var panel = root.AddComponent<RouteDetailPanel>();
            panel._content = content.transform;
            panel._toast = toast;
            panel.AssignRefs(root.GetComponent<RectTransform>(), titleLabel, close, cg);
            panel.BindPage("n41_单航线", title);
            panel.RefreshBody();
            return panel;
        }

        public override void BindPage(string pageId, string title = null)
        {
            base.BindPage(pageId, title);
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
        }

        RouteData CurrentRoute()
        {
            var gs = GameState.Instance;
            string id = FleetNetContext.SelectedRouteId;
            if (string.IsNullOrEmpty(id))
                id = OpsSubpageContext.SelectedRouteId;
            if (string.IsNullOrEmpty(id) || gs?.activeRoutes == null) return null;

            // 键的两种形态都由 RouteData.FindByKey 认：精确 routeId（各页面点进来）与机场对
            // （正文里 TermNavMap 包出来的航线链接只有 FRA—ATL 两个三字码，没有序号）。
            // ★旧版只做精确比对、失败就**悄悄给第一条**，于是「点 FRA—ATL 却打开 FRA—LHR」
            //   （2026-10-13 CEO 报障）——导航错位比报错更难发现，故这里一律不兜底。
            var r = RouteData.FindByKey(gs.activeRoutes, id);
            if (r != null)
            {
                // 顺手把解析结果写回上下文：本页后续刷新、以及「详览 · 周计划 / 机队」等同源深链
                // 都从这两个字段取键，留着那串解析不出航线的原始键会让它们各自再错一次。
                string rid = r.routeId ?? "";
                if (!string.Equals(FleetNetContext.SelectedRouteId, rid)) FleetNetContext.SelectedRouteId = rid;
                if (!string.Equals(OpsSubpageContext.SelectedRouteId, rid)) OpsSubpageContext.SelectedRouteId = rid;
                return r;
            }

            return null;
        }

        /// <summary>
        /// 这个键像不像**已开通航线**的 routeId（`PKX-SHA-1` / `FRA-ATL-2` / `...-great` 不算）？
        ///
        /// 区分「提案航线」与「已停飞」全靠最后一段：开通时序号是数字（见 ActionExecutor /
        /// TakeoverWorldBuilder），提案则固定写作 `-great`（见 ExpansionProposalManager）。
        /// 判错的代价是把一条刚停飞的线画成「尚未开通」，CEO 会以为关线没生效。
        /// </summary>
        static bool LooksLikeOpenedRouteId(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var parts = key.Split('-');
            if (parts.Length < 3) return false;
            string tail = parts[parts.Length - 1];
            if (tail.Length == 0) return false;
            for (int i = 0; i < tail.Length; i++)
                if (tail[i] < '0' || tail[i] > '9') return false;
            return true;
        }

        public void RefreshBody()
        {
            ResolveRefs();
            if (_content == null) return;
            Clear(_content);

            var r = CurrentRoute();
            if (r == null)
            {
                string pending = FleetNetContext.SelectedRouteId;
                if (string.IsNullOrEmpty(pending)) pending = OpsSubpageContext.SelectedRouteId;

                // ★2026-10-20：航线刚被命令台停飞，键还留在上下文里，但它已经不在 activeRoutes。
                //   旧版会把 `PKX-SHA-1` 按前两段拆成机场对，于是刚关掉的线在页面上变成
                //   「提案航线，尚未开通」—— 提案与停飞是两件事，不能混着说。
                if (LooksLikeOpenedRouteId(pending))
                {
                    OpsSubpageContext.SelectedRouteId = null;
                    FleetNetContext.SelectedRouteId = null;
                    L("H", "单航线", 0.0f, 0.90f, 1.0f, 0.97f, 16, UITheme.TextMuted, true);
                    L("E", "本航线已停飞，不在在营航线清单内。\n\n"
                         + "如需重新开通，请在命令台下达「分析开通…的可行性」，"
                         + "或到「航线 → 开通航线」重新申请（航权记录仍在，但时刻槽需重新绑定）。",
                        0.0f, 0.74f, 1.0f, 0.86f, 14, UITheme.TextPrimary);
                    return;
                }

                if (!string.IsNullOrEmpty(pending) && pending.Contains("-"))
                {
                    string[] parts = pending.Split('-');
                    if (parts.Length >= 2)
                    {
                        var pendingDemand = RouteGravityModel.Evaluate(GameState.Instance, parts[0], parts[1]);
                        L("H", AirportCatalog.Od(parts[0], parts[1]), 0.0f, 0.90f, 1.0f, 0.97f, 18, UITheme.TextPrimary, true);
                        L("E", "提案航线，尚未开通。批准后下季度投入运营。\n\n" + pendingDemand.FormatBlock(),
                            0.0f, 0.20f, 1.0f, 0.86f, 14, UITheme.Warning);
                    }
                    else
                    {
                        L("E", "提案航线，尚未开通。请返回收件箱批准；下季度投入运营后可在此查看班期与收益。",
                            0.0f, 0.72f, 1.0f, 0.86f, 14, UITheme.Warning);
                    }
                    return;
                }

                L("H", "单航线", 0.0f, 0.90f, 1.0f, 0.97f, 16, UITheme.TextMuted, true);
                L("E", "未选择航线。请从航线网络点击航线进入。", 0.0f, 0.78f, 1.0f, 0.86f, 14, UITheme.Warning);
                return;
            }

            var gs = GameState.Instance;
            var m = OpsEconomics.Evaluate(
                r.distanceKm > 0 ? r.distanceKm : 1000f,
                r.weeklyFlights > 0 ? r.weeklyFlights : 7,
                r.aircraftType, r.groundStaffAssigned, gs != null ? gs.onTimePercent : 88f);

            // 换了一条航线：回到该航线的班次列表
            if (!string.Equals(r.routeId, _lastRouteId, StringComparison.Ordinal))
            {
                _lastRouteId = r.routeId;
                _selFlight = -1;
            }

            if (_selFlight >= 0)
                RenderFlightDetail(r, gs);
            else
                RenderOverview(r, gs, m);
        }

        // ———————————————————————— 班次列表 → 班次周排班 ————————————————————————

        /// <summary>航线总览：紧凑摘要 + 全部班次列表（点班次进入周排班）。</summary>
        void RenderOverview(RouteData r, GameState gs, OpsEconomics.ScheduleMetrics m)
        {
            L1("H", AirportCatalog.OdRoundTrip(r.origin, r.destination), 0.0f, 0.945f, 0.55f, 1.0f, 18, UITheme.TextPrimary, true);
            string stop = string.IsNullOrEmpty(r.stopover) ? "直飞" : $"经停 {r.stopover}";
            FlightScheduleFormat.Numbers(r, 0, out string noA, out string noB);
            L1("Sub", $"{noA}/{noB} · {stop} · {r.season}季 · 开航 {r.openedDate}",
                0.0f, 0.888f, 0.55f, 0.938f, 12, UITheme.TextMuted);
            WireKeywordNav(L1("Acs", FormatOperatingAircrafts(r, gs), 0.55f, 0.888f, 1.0f, 0.938f, 12,
                null, false, TextAlignmentOptions.MidlineRight));

            // 紧凑摘要：每行独立区间，最多两行，超出截断而不外溢
            L2("Plan", $"航班计划　{r.DualWeeklyLine()}　｜　班次 {r.FlightCount} 班/日（{SlotSummary(r)}）" +
                       $"　｜　轮挡 {m.blockHoursPerFlight:F1} 小时 · 日利用率 {m.dailyUtilizationHours:F1} 小时" +
                       $"　｜　地勤 {r.groundStaffAssigned} 人（建议 {OpsEconomics.RecommendedGroundStaff(r.aircraftType)}）· 过站 {m.turnMinutes:F0} 分" +
                       $"　｜　{r.DutyAttributionLine()}" +
                       $"　｜　机场 {AirportCatalog.TierLabelOf(r.origin)}⇄{AirportCatalog.TierLabelOf(r.destination)}",
                0.0f, 0.828f, 1.0f, 0.882f, 12);
            L2("Ops", $"运营与市场　客座率 {gs?.loadFactorPercent ?? 0:F0}% · 准点 {gs?.onTimePercent ?? 0:F0}% · 延误率 {m.delayProbability * 100:F0}% · 调度效率 {m.scheduleEfficiency:F2}" +
                      $"　｜　日利 {r.dailyProfit:F2} 万 · 周利 {r.dailyProfit * 7f:F1} 万" + GroundSuffix(gs, r),
                0.0f, 0.768f, 1.0f, 0.822f, 12, GroundRowColor(gs, r));

            var g = RouteGravityModel.EvaluateRoute(gs, r);
            L2("Dem", $"周需求（引力模型）　商务 {g.weeklyC:N0}　超经 {g.weeklyW:N0}　经济 {g.weeklyY:N0}　｜　货邮 {g.weeklyCargoTons:F1} 吨/周",
                0.0f, 0.708f, 1.0f, 0.762f, 12, UITheme.TextMuted);
            L2("Health", CapacityDirectiveLogic.HealthCheck(gs, r, out _) + "　｜　" + CapacityDirectiveLogic.Precheck(gs, r),
                0.0f, 0.648f, 1.0f, 0.702f, 12, UITheme.Warning, true);

            // 全宽：班次与执飞（列出该航线全部班次；点击进入该班次周排班）
            r.EnsureFlights();
            L1("C3", $"班次与执飞　Scheduled Flights · 共 {r.FlightCount} 班 · 值勤日 {r.DutyPeriodCount()} 个值勤期（跨夜班按报到日归组，点任一班次查看该班周排班）",
                0.0f, 0.588f, 1.0f, 0.642f, 14, UITheme.Accent, true);
            RenderFlightList(r, 0.578f, BodyBottom);
        }

        /// <summary>班次列表：每行三段（班次/航班号、时刻班期、机号），点击进入周排班。</summary>
        void RenderFlightList(RouteData r, float top, float bottom)
        {
            int n = r.flights != null ? r.flights.Count : 0;
            if (n == 0)
            {
                L2("NoF", "该航线暂无班次。请在底部 CEO 命令台说一句运力指令（例：这条线加密到每天 3 班），"
                          + "运营部将自动编班并构建航班环。",
                    0f, top - 0.06f, 1f, top, 12, UITheme.Warning);
                return;
            }

            float band = top - bottom;
            int fits = Mathf.Max(1, Mathf.FloorToInt(band / ListMinRowPitch));
            int rows = Mathf.Min(n, fits);
            bool more = n > rows;
            if (more) band -= 0.045f;                     // 预留「另有 N 班」提示行
            float pitch = band / rows;

            for (int i = 0; i < rows; i++)
            {
                var f = r.flights[i];
                if (f == null) continue;
                int idx = i;
                float rTop = top - i * pitch;
                float rBot = rTop - pitch * 0.82f;

                var row = UIFactory.Panel(_content, "FR" + i,
                    new UIFactory.Anchor(new Vector2(0f, rBot), new Vector2(1f, rTop), Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep);
                // 整行点击 → 打开该班次周排班（机号单独可点，直达飞机详情）
                UIFactory.Button(row.transform, "Hit", "", UIFactory.Fill, new Color(0, 0, 0, 0), () => OpenFlight(idx), 12);

                FlightScheduleFormat.Numbers(r, i, out string noA, out string noB);
                bool noPlane = string.IsNullOrEmpty(f.registration);
                string reg = noPlane ? RouteData.UnassignedAircraftLabel : f.registration;
                string slot = string.IsNullOrEmpty(f.slotLabel) ? "—" : f.slotLabel;
                string occ = RoutePermitLogic.SlotStatusOf(GameState.Instance, r, i);

                // 左：班次序号 + 航班号 + 城对
                RowLabel(row.transform, "Main", $"#{i + 1}　{noA}/{noB}　{r.origin}⇄{r.destination}",
                    0.014f, 0.42f, 12, UITheme.TextPrimary, true);
                // 中：时段 + 一轮往返时刻 + 班期/周班次 + 槽位
                RowLabel(row.transform, "Meta",
                    $"{slot} {FlightScheduleFormat.WindowLabel(r, f)}　{FlightScheduleFormat.DaysSummary(f.daysOfWeek)} {FlightScheduleFormat.WeeklyOf(f)} 班/周 · {occ}",
                    0.43f, 0.83f, 11, UITheme.TextMuted);
                // 右：机号（可点击 → 飞机详情）与机型；无执飞机时不得并写出机型
                WireKeywordNav(RowLabel(row.transform, "Ac",
                    noPlane ? $"{reg} ›" : $"{TermNavMap.AircraftLink(reg)} {r.aircraftType} ›",
                    0.84f, 1.0f, 12, noPlane ? UITheme.Warning : UITheme.Accent, false, TextAlignmentOptions.MidlineRight));
            }

            if (more)
                L1("More", $"另有 {n - rows} 个班次未列出（下达运力指令可调整班次数量）",
                    0f, bottom, 1f, bottom + 0.04f, 11, UITheme.TextMuted);
        }

        /// <summary>列表行内的单行标签：不换行、超出省略，保证不与相邻列重叠。</summary>
        TextMeshProUGUI RowLabel(Transform host, string name, string text, float x0, float x1,
            int size, Color? color = null, bool bold = false,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var l = UIFactory.Label(host, name, text, size, align,
                new UIFactory.Anchor(new Vector2(x0, 0.12f), new Vector2(x1 - 0.008f, 0.88f), Vector2.zero, Vector2.zero),
                color ?? UITheme.TextPrimary, bold ? FontStyles.Bold : FontStyles.Normal);
            ClampLines(l, 1, 0f);
            return l;
        }

        /// <summary>班次周排班（参照 AS：航班号 / 星期圈 / 去程 / 过站 / 回程）。</summary>
        void RenderFlightDetail(RouteData r, GameState gs)
        {
            r.EnsureFlights();
            if (_selFlight < 0 || _selFlight >= r.flights.Count) _selFlight = r.flights.Count > 0 ? 0 : -1;
            if (_selFlight < 0)
            {
                L("H", "班次周排班", 0f, 0.90f, 1f, 0.98f, 18, UITheme.TextMuted, true);
                L("E", "该航线暂无班次。", 0f, 0.80f, 1f, 0.88f, 13, UITheme.Warning);
                L("Back", "← 返回班次列表", 0f, 0.72f, 0.24f, 0.79f, 13, UITheme.Accent);
                return;
            }

            var f = r.flights[_selFlight];
            var trip = FlightScheduleFormat.Trip(r, f);
            FlightScheduleFormat.Numbers(r, _selFlight, out string noA, out string noB);

            // 顶栏：航班号 + 城市对
            L1("H", $"{noA} / {noB}", 0.0f, 0.945f, 0.5f, 1.0f, 18, UITheme.TextPrimary, true);
            L1("Od", $"{AirportCatalog.Label(r.origin)} {r.origin}　⇄　{AirportCatalog.Label(r.destination)} {r.destination}",
                0.5f, 0.945f, 1.0f, 1.0f, 13, UITheme.TextMuted, false, TextAlignmentOptions.MidlineRight);
            L1("Sub", $"第 {_selFlight + 1} / {r.FlightCount} 班 · {r.season}季 · "
                      + (string.IsNullOrEmpty(f.registration) ? RouteData.UnassignedAircraftLabel : r.aircraftType)
                      + $" · {(string.IsNullOrEmpty(f.slotLabel) ? "—" : f.slotLabel)}",
                0.0f, 0.888f, 1.0f, 0.938f, 12, UITheme.TextMuted);

            // 返回按钮（回到班次列表）
            UIFactory.Button(_content, "Back", "← 班次列表",
                new UIFactory.Anchor(new Vector2(0.0f, 0.818f), new Vector2(0.16f, 0.878f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, CloseFlight, 13);

            // —— AS 式周视图：左时间轴 × 上星期 × 往返块（去程段 / 橙条过站 / 回程段）——
            // 指令框拆掉后底部空出 26%，整段还给周视图（0.355 → 0.085）。
            // 这张图是 CEO 判断「这班到底排得开不开」的唯一凭据，行高比留白值钱。
            var card = UIFactory.Panel(_content, "SchedCard",
                new UIFactory.Anchor(new Vector2(0f, 0.085f), new Vector2(1f, 0.808f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1f, -1f));

            // 周利与单班日利都用**净额**（航线日利 ÷ 班次），与周计划逐班行、n43 单班页同一口径。
            // 旧版直接用 f.dailyProfit（毛贡献份额，Σ 各班 = 毛贡献），比航线日利多出整笔周期固定成本，
            // 于是同一个 NS100 在单航线页与邮件/周计划里会印出两个不同的「日利」。
            // ★ 2026-10-27：周利改走 RouteData.WeeklyProfitOf —— 旧式「单班日利 × 本班周班次」
            //   会把 dailyProfit 里已含的 weeklyFlights/7 再乘一遍（20 班/周的航线虚高 2.86 倍）。
            float weekProfit = r.WeeklyProfitOf(f);
            ClampLines(UIFactory.Label(card.transform, "T",
                (string.IsNullOrEmpty(f.registration) ? "Flight Schedule　班次周排班" : $"{f.registration} Flight Schedule")
                + "　·　列 = 执飞日",
                13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.90f), new Vector2(0.60f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold), 1, 0f);
            ClampLines(UIFactory.Label(card.transform, "W",
                $"WEEKLY PROFIT　{weekProfit:F1} 万　｜　{noA}/{noB}　{FlightScheduleFormat.WindowLabel(r, f)}　过站 {FlightScheduleFormat.Duration(trip.turnMinutes)}（在 {r.destination}）",
                11, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.52f, 0.90f), new Vector2(0.98f, 0.995f), Vector2.zero, Vector2.zero),
                f.dailyProfit >= 0 ? UITheme.Success : UITheme.Danger), 1, 0f);

            var grid = UIFactory.Panel(card.transform, "Grid",
                new UIFactory.Anchor(new Vector2(0.01f, 0.072f), new Vector2(0.99f, 0.89f), Vector2.zero, Vector2.zero),
                new Color(0.03f, 0.06f, 0.10f, 0.95f));
            var gImg = grid.GetComponent<Image>();
            if (gImg != null) gImg.raycastTarget = false;
            var blocks = AsWeekGridUI.Collect(gs, f.registration, r, _selFlight);
            AsWeekGridUI.Draw(grid.transform, blocks);

            // 图例：把「过站」与「本场地面」分开 —— 否则一条 18 小时的长条里，
            // 唯一看得见的空档（本场停场）会被读成「在对方站过站了这么久」（第八次报障）
            ClampLines(UIFactory.Label(card.transform, "Legend",
                $"橙条 = 对方站 {r.destination} 过站 {FlightScheduleFormat.Duration(trip.turnMinutes)}"
                + $"　｜　橙条外空白 = 本场地面 {r.origin}　｜　上=出发站、下=到达站"
                + $"　｜　跨午夜回程画到 24:00，次日凌晨细条 = 前一日落地",
                9, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.006f), new Vector2(0.98f, 0.066f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted), 1, 0f);

            // —— 执飞机与收益（压成一行，把高度让给周视图）——
            string reg = string.IsNullOrEmpty(f.registration) ? "未分配" : f.registration;
            string ring = RingOf(gs, r, reg);
            WireKeywordNav(L1("Ac",
                $"执飞　{TermNavMap.AircraftLink(reg)}　{r.aircraftType}　｜　" +
                (string.IsNullOrEmpty(ring) ? "未串环（本线往返）" : "航班环 " + ring) +
                $"　｜　单班日利 {r.FlightNetDailyProfit:F2} 万 · 往返 {FlightScheduleFormat.Duration(trip.cycleMinutes)}（含过站 {trip.turnMinutes} 分）· {TotalSeats(r, reg)} 座",
                0.0f, 0.005f, 1.0f, 0.075f, 12, f.dailyProfit >= 0 ? UITheme.Success : UITheme.Danger));
        }

        void OpenFlight(int index)
        {
            _selFlight = index;
            RefreshBody();
        }

        void CloseFlight()
        {
            _selFlight = -1;
            RefreshBody();
        }

        static string SlotSummary(RouteData r)
        {
            if (r?.flights == null || r.flights.Count == 0) return "—";
            var sb = new StringBuilder();
            for (int i = 0; i < r.flights.Count; i++)
            {
                if (r.flights[i] == null) continue;
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(r.flights[i].depTime);
            }
            return sb.Length > 0 ? sb.ToString() : "—";
        }

        static string RingOf(GameState gs, RouteData r, string reg)
        {
            if (gs?.flightRings == null || r == null || string.IsNullOrEmpty(reg)) return "";
            for (int i = 0; i < gs.flightRings.Count; i++)
            {
                var ring = gs.flightRings[i];
                if (ring == null) continue;
                if (!string.Equals(ring.registration, reg, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(ring.legARouteId, r.routeId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ring.legBRouteId, r.routeId, StringComparison.OrdinalIgnoreCase))
                    return $"{ring.ringId}（过站 {ring.turnMinutes} 分 · {(ring.valid ? "合格" : "未通过")}）";
            }
            return "";
        }

        static int TotalSeats(RouteData r, string reg)
        {
            var fm = FleetManager.Instance;
            var ac = fm != null ? fm.GetAircraftByRegistration(reg) : null;
            return ac != null ? ac.TotalSeats : 0;
        }

        // ———————————————————————— 关键词跳转 ————————————————————————

        static string FormatOperatingAircrafts(RouteData r, GameState gs)
        {
            var regs = CollectRegs(r);
            if (regs.Count == 0) return "执飞飞机　" + RouteData.UnassignedAircraftLabel;
            var sb = new StringBuilder("执飞飞机　");
            for (int i = 0; i < regs.Count; i++)
            {
                if (i > 0) sb.Append("　");
                sb.Append(TermNavMap.AircraftLink(regs[i]));
                // 机号还在航线记录里、但已不可执飞（退役转售 / 停场 / 已租出）：照实标注，
                // 否则「执飞飞机 B-2003」看起来一切正常，而该班其实一班都飞不出去。
                sb.Append(FlightGroundLogic.RegNote(gs, regs[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 详情页「日利」后面的停飞标注：设计口径照常显示（好让 CEO 知道修好值不值），
        /// 但必须写清本季不计营收 —— 季结按 <see cref="GameState.monthlyRevenue"/> 轧差，
        /// 停飞航线已被剔除，若不加这句，玩家会以为日利照进账。
        /// </summary>
        static string GroundSuffix(GameState gs, RouteData r)
        {
            var reason = FlightGroundLogic.RouteOf(gs, r);
            if (!FlightGroundLogic.IsPersistent(reason)) return "";
            return $"　⚠ {FlightGroundLogic.Label(reason)}｜本季不计营收（{FlightGroundLogic.Advice(reason)}）";
        }

        static Color GroundRowColor(GameState gs, RouteData r)
            => FlightGroundLogic.IsPersistent(FlightGroundLogic.RouteOf(gs, r))
                ? UITheme.Warning
                : (r.dailyProfit >= 0 ? UITheme.Success : UITheme.Danger);

        static List<string> CollectRegs(RouteData r)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            void Add(string x)
            {
                if (string.IsNullOrEmpty(x) || x == "未分配" || x == RouteData.UnassignedAircraftLabel) return;
                if (seen.Add(x)) list.Add(x);
            }
            Add(r.registration);
            r.EnsureFlights();
            if (r.flights != null)
            {
                for (int i = 0; i < r.flights.Count; i++)
                    if (r.flights[i] != null) Add(r.flights[i].registration);
            }
            return list;
        }

        void WireKeywordNav(TextMeshProUGUI label)
        {
            if (label == null) return;
            label.raycastTarget = true;
            var click = label.GetComponent<InboxLinkClick>();
            if (click == null) click = label.gameObject.AddComponent<InboxLinkClick>();
            click.text = label;
            click.onLink = OnKeyword;
        }

        void OnKeyword(string linkId)
        {
            if (!TermNavMap.TryResolve(linkId, out var hit)) return;
            if (hit.pageId != null && hit.pageId.StartsWith("n10"))
            {
                OpenAircraft(hit.routeKey);
                return;
            }
            // ★2026-10-19：本页正文也会走 TermNavMap.Decorate，于是航班号（flight:→n43）与
            // 三字码对（route:→n41）这类带键链接会出现在这里。落地页按上下文键选对象，
            // 不把键写回去就会打开「上一次选中的航线」—— 看着跳了，跳的是别人。
            if (!string.IsNullOrEmpty(hit.routeKey) && hit.pageId != null)
            {
                if (hit.pageId.StartsWith("n12"))
                {
                    FleetNetContext.SelectedCityIata = hit.routeKey;
                    FleetNetContext.OpenEconomyTab = true;
                }
                else if (hit.pageId.StartsWith("n43"))
                {
                    // 航班号 → n43 单班页：连班次下标一起写，否则会落到本线第 1 班。
                    OpsSubpageContext.SelectFlight(hit.routeKey, hit.flightIndex);
                }
                else
                {
                    FleetNetContext.SelectedRouteId = hit.routeKey;
                    OpsSubpageContext.SelectedRouteId = hit.routeKey;
                }
            }
            var nav = FindFirstObjectByType<PrefabNavIntegration>();
            if (nav != null && !string.IsNullOrEmpty(hit.pageId))
                nav.OpenGameplayPage(hit.pageId);
        }

        void OpenAircraft(string reg = null)
        {
            if (string.IsNullOrEmpty(reg) || reg == "未分配")
            {
                var r = CurrentRoute();
                if (r != null) reg = r.registration;
            }
            if (string.IsNullOrEmpty(reg) || reg == "未分配") return;
            FleetNetContext.SelectedRegistration = reg;
            OpsSubpageContext.SelectedRegistration = reg;
            FleetUI.OpenFleetPage("n10_飞机详情_履历");
        }

        // ———————————————————————— 工具 ————————————————————————

        /// <summary>清空内容层：保留运力指令对话框，避免正在输入的指令被刷新打断。</summary>
        /// <summary>清空内容层后重建（原先要留住运力指令框，框拆掉后整层都是可重建的正文）。</summary>
        void Clear(Transform host)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var child = host.GetChild(i);
                if (child == null) continue;
                Destroy(child.gameObject);
            }
        }

        TextMeshProUGUI L(string name, string text, float x0, float y0, float x1, float y1,
            int size, Color? color = null, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var l = UIFactory.Label(_content, name, text, size, align,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.TextPrimary, bold ? FontStyles.Bold : FontStyles.Normal);
            return l;
        }

        /// <summary>单行标签：不换行、超出省略号，绝不越界压到相邻区块。</summary>
        TextMeshProUGUI L1(string name, string text, float x0, float y0, float x1, float y1,
            int size, Color? color = null, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var l = L(name, text, x0, y0, x1, y1, size, color, bold, align);
            ClampLines(l, 1, 0f);
            return l;
        }

        /// <summary>最多两行的标签：换行但截断，行距收紧。</summary>
        TextMeshProUGUI L2(string name, string text, float x0, float y0, float x1, float y1,
            int size, Color? color = null, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var l = L(name, text, x0, y0, x1, y1, size, color, bold, align);
            ClampLines(l, 2, 1f);
            return l;
        }

        /// <summary>限定最大行数并收紧行距；TMP 默认会向外溢出，必须显式约束。</summary>
        static void ClampLines(TextMeshProUGUI t, int maxLines, float lineSpacing)
        {
            if (t == null) return;
            t.textWrappingMode = maxLines <= 1 ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Truncate;
            t.maxVisibleLines = Mathf.Max(1, maxLines);
            t.lineSpacing = lineSpacing;
        }
    }
}

