using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 机组产能 —— 排班的**第四道闸门**（与时刻槽 / 值勤 / 机队并列）。
    ///
    /// 由来（2026-10-14 CEO 第十一次问询，接第十次）：
    /// 上一问「一组飞机能不能一周七趟」的答案是「机体排得开、机组要三组」；CEO随即收口：
    /// **「当 CEO 输入『增加此航线的航班量』，那么除了要获得更多的时刻槽，也要考虑增加轮换机组。」
    /// —— 这正是本类存在的理由：**加密不是只买槽，还要买人**。旧版把机组只写成呈报里的一句
    /// 提示（`CrewDemandLine`），排班器完全不看它，于是「槽批下来了、飞机也在、一行班次排出去」
    /// 而没人可派 —— 三根轴里唯一会被**静默忽略**的那根。
    ///
    /// ★2026-10-14 第十三次问询（CEO：「机组是包括乘务人员吗？」）—— **两岗口径**：
    /// 现实里「机组」＝ **飞行机组（驾驶舱）＋ 客舱乘务组**，而本类原先只算驾驶舱。
    /// 按 CEO 裁决把客舱一并纳入闸门，并明确两者的分工：
    /// <list type="bullet">
    /// <item>**组数共用**：一个轮转班底里两岗一起走（同去程、同驻站、同回程），
    ///   故组数只有 <see cref="OpsRotationLogic.CrewSetsForDaily"/> 一个算术；</item>
    /// <item>**每组人数各算各的**：驾驶舱看值勤时长（<see cref="OpsRotationLogic.PilotsPerDuty"/>，
    ///   单段 ≥9h 走 4 人制增援），客舱看座位（<see cref="OpsRotationLogic.CabinPerDuty"/>，
    ///   每 50 座 1 名）；</item>
    /// <item>**两岗取小**：飞行员与乘务员是不可互替的两支队伍，任一岗到顶则本段排不出
    ///   （<see cref="MaxTripsWithin"/>）。</item>
    /// </list>
    ///
    /// 口径（与 <see cref="OpsRotationLogic.RouteCrewNeed"/> 同一套算术，不另立一套）：
    /// <list type="number">
    /// <item>**需求** ＝ 本线每日 N 段的机组组数 × 每组人数（两岗各一）。外站换组的洲际线是 3N 组
    ///   （一组每 3 天最多 2 段），窄体同组往返线按值勤预算折算；</item>
    /// <item>**占用** ＝ 其它在营航线各按现值值班次折算的人数 —— 机组是全公司共享资源，
    ///   本线可用 = 全公司可用 − 其它航线占用；</item>
    /// <item>**可用** ＝ <see cref="GameState.PilotFilled"/> / <see cref="GameState.CabinFilled"/>（在职）；
    ///   该岗在职为 0 时退到该岗编制（<see cref="GameState.PilotEstablishment"/> /
    ///   <see cref="GameState.CabinEstablishment"/>）—— 见下「为什么有这道退化」。</item>
    /// </list>
    ///
    /// ★为什么在职为 0 时退到编制口径（而不是一律按在职拦死）：
    /// 从零开局（`SetupFlowController` 的 FromScratch 分支）**只播种管理序列**，
    /// 运行岗一个都不建 —— 机组成本走「每机人力费率」的聚合口径，玩家手里并没有逐人名单。
    /// 若照在职拦，这类存档会连一班都加不了，而程序界面上根本没有「你有 0 名飞行员」这件事
    /// （叙事层说你有整个机组）。故：**该岗未按人建档 → 该岗按编制放行，并在呈报里写明用的是哪一套**；
    /// 一旦建了该岗的档（哪怕只招到 1 人），口径立刻切到在职、按人算。
    /// ★**两岗的退化各自独立判定**：只招了飞行员没招乘务员时，驾驶舱按在职、客舱按编制 ——
    /// 「招飞行员」这件事不该顺手把乘务的口径也切掉（否则会凭空多出一道与被招岗位无关的缺口）。
    /// 编制本身由在役机队推导（飞行员：窄体 8 名/架、宽体 +2；乘务：每 50 座 4 名 + 每架 4 名备份），
    /// 所以「编制不够」的正确处置是**加机**（编制随在役机队上涨），不是招人
    /// （编制公式在 <see cref="GameState.PilotEstablishment"/> / <see cref="GameState.CabinEstablishment"/>
    /// → <see cref="HrEconomics.OperationalEstablishment"/>）。
    ///
    /// 在职不足（覆盖率 &lt; 100%）不在这里拦：它已有真实代价 ——
    /// `GameState.CrewQualityMultiplier` 按覆盖率把航线利润折损在 [0.85, 1.0] 之间，
    /// 补员方向由 人力 → 招聘 承担。本类只回答一个问题：**这条线这点班次，配得起机组吗**。
    /// （该乘数仍只按**驾驶舱**覆盖率算，未随本次扩到客舱 —— 刻意的：扩它会改动平衡数字，
    /// 且「服务品质」与「有没有人飞」是两个轴。见 §17.18 留档。）
    /// </summary>
    public static class CrewCapacityLogic
    {
        /// <summary>缺口类型码（<see cref="OpsScheduleLogic.WaveShortage.kind"/>）——缺机组。</summary>
        public const string KindCrew = "crew";

        /// <summary>缺口类型的中文短名（单航线页/缺口汇总用）。</summary>
        public const string Label = "缺机组";

        /// <summary>「不设限」：拿不到在册/编制信息时（离线纯函数、无 GameState）一律放行。</summary>
        public const int NoLimit = int.MaxValue;

        /// <summary>驾驶舱岗名（文案用）。</summary>
        public const string RolePilot = "飞行员";

        /// <summary>客舱岗名（文案用）。</summary>
        public const string RoleCabin = "乘务员";

        // ———————————————————————— 需求侧 ————————————————————————

        /// <summary>本线每日 <paramref name="tripsPerDay"/> 段的机组需求（组数 + 两岗人数）。</summary>
        public static OpsRotationLogic.CrewDemand Demand(RouteData route, int tripsPerDay)
            => OpsRotationLogic.RouteCrewNeed(route, tripsPerDay > 0 ? tripsPerDay : 0);

        /// <summary>本线每日 <paramref name="tripsPerDay"/> 段所需的机组组数（0 段 = 0 组）。</summary>
        public static int RouteSets(RouteData route, int tripsPerDay) => Demand(route, tripsPerDay).sets;

        /// <summary>本线每日 <paramref name="tripsPerDay"/> 段所需的**驾驶舱**人数（0 段 = 0 名）。</summary>
        public static int RoutePilots(RouteData route, int tripsPerDay) => Demand(route, tripsPerDay).pilots;

        /// <summary>本线每日 <paramref name="tripsPerDay"/> 段所需的**客舱**人数（0 段 = 0 名）。</summary>
        public static int RouteCabin(RouteData route, int tripsPerDay) => Demand(route, tripsPerDay).cabin;

        /// <summary>
        /// 在给定**两岗**预算下，本线**最多**能撑起几段（0 = 连一段都配不齐机组）。
        /// 两岗取小：飞行员与乘务员不可互替，任一岗到顶则本段排不出。
        /// 纯函数（只看航线参数与预算），故离线校验可复算；需求对段数单调不减，故可用逐段扫描。
        /// </summary>
        public static int MaxTripsWithin(RouteData route, int budgetPilots, int budgetCabin)
        {
            if (route == null) return NoLimit;
            if (budgetPilots <= 0 || budgetCabin <= 0) return 0;
            int best = 0;
            int cap = ScheduleSlots.DepTimes.Length;
            for (int n = 1; n <= cap; n++)
            {
                var need = Demand(route, n);
                if (need.pilots > budgetPilots || need.cabin > budgetCabin) break;
                best = n;
            }
            return best;
        }

        /// <summary>其它在营航线已占用的**飞行员**人数（按各线现值值班次折算；同一机队的机组不可两用）。</summary>
        public static int OtherRoutesPilots(GameState gs, string excludeRouteId)
            => OtherRoutesOccupied(gs, excludeRouteId, cabin: false);

        /// <summary>其它在营航线已占用的**乘务员**人数（口径同上，另一个岗位）。</summary>
        public static int OtherRoutesCabin(GameState gs, string excludeRouteId)
            => OtherRoutesOccupied(gs, excludeRouteId, cabin: true);

        static int OtherRoutesOccupied(GameState gs, string excludeRouteId, bool cabin)
        {
            if (gs?.activeRoutes == null) return 0;
            int sum = 0;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                if (!string.IsNullOrEmpty(excludeRouteId)
                    && string.Equals(r.routeId, excludeRouteId, StringComparison.OrdinalIgnoreCase)) continue;
                var need = Demand(r, r.FlightCount);
                sum += cabin ? need.cabin : need.pilots;
            }
            return sum;
        }

        // ———————————————————————— 供给侧 ————————————————————————

        /// <summary>在职飞行员（未按人建机组时为 0）。</summary>
        public static int OnDutyPilots(GameState gs) => gs != null ? gs.PilotFilled : 0;

        /// <summary>飞行员编制（按在役机队推导：窄体 8 名/架，宽体 +2）。</summary>
        public static int EstablishmentPilots(GameState gs) => gs != null ? gs.PilotEstablishment : 0;

        /// <summary>在职客舱乘务（未按人建机组时为 0）。</summary>
        public static int OnDutyCabin(GameState gs) => gs != null ? gs.CabinFilled : 0;

        /// <summary>客舱乘务编制（按在役机队与总座位数推导：每 50 座 4 名 + 每架 4 名备份）。</summary>
        public static int EstablishmentCabin(GameState gs) => gs != null ? gs.CabinEstablishment : 0;

        /// <summary>
        /// 全公司可用飞行员，并回传口径：<paramref name="byOnDuty"/> = true 走「在职」，
        /// false = 未按人建机组、退到「编制」（口径说明见类注释）。
        /// </summary>
        public static int AvailablePilots(GameState gs, out int onDuty, out int establishment, out bool byOnDuty)
        {
            onDuty = OnDutyPilots(gs);
            establishment = EstablishmentPilots(gs);
            byOnDuty = onDuty > 0;
            return byOnDuty ? onDuty : establishment;
        }

        /// <summary>全公司可用客舱乘务，口径退化与 <see cref="AvailablePilots"/> **各自独立**判定。</summary>
        public static int AvailableCabin(GameState gs, out int onDuty, out int establishment, out bool byOnDuty)
        {
            onDuty = OnDutyCabin(gs);
            establishment = EstablishmentCabin(gs);
            byOnDuty = onDuty > 0;
            return byOnDuty ? onDuty : establishment;
        }

        /// <summary>
        /// 本线可用飞行员 = 全公司可用 − 其它航线占用（下限 0）。
        /// <paramref name="byOnDuty"/> 回传口径（在职业 / 编制）。
        /// </summary>
        public static int BudgetFor(GameState gs, RouteData route,
            out int available, out int otherOccupied, out bool byOnDuty)
        {
            available = AvailablePilots(gs, out _, out _, out byOnDuty);
            otherOccupied = OtherRoutesPilots(gs, route != null ? route.routeId : null);
            int budget = available - otherOccupied;
            return budget > 0 ? budget : 0;
        }

        /// <summary>
        /// 本线可用乘务员 = 全公司可用 − 其它航线占用（下限 0）。与 <see cref="BudgetFor"/> 同形、另一个岗位。
        /// </summary>
        public static int CabinBudgetFor(GameState gs, RouteData route,
            out int available, out int otherOccupied, out bool byOnDuty)
        {
            available = AvailableCabin(gs, out _, out _, out byOnDuty);
            otherOccupied = OtherRoutesCabin(gs, route != null ? route.routeId : null);
            int budget = available - otherOccupied;
            return budget > 0 ? budget : 0;
        }

        /// <summary>
        /// 本线在机组口径下最多能排几段（两岗取小）。拿不到在册/编制信息（无 GameState）时返回 <see cref="NoLimit"/>。
        /// </summary>
        public static int MaxCrewedTrips(GameState gs, RouteData route)
        {
            if (gs == null || route == null) return NoLimit;
            return MaxTripsWithin(route,
                BudgetFor(gs, route, out _, out _, out _),
                CabinBudgetFor(gs, route, out _, out _, out _));
        }

        /// <summary>判据：本线每日 <paramref name="tripsPerDay"/> 段配不配得起机组（两岗都够才返回 true）。</summary>
        public static bool CanCrew(GameState gs, RouteData route, int tripsPerDay, out string why)
        {
            why = "";
            if (gs == null || route == null || tripsPerDay <= 0) return true;
            var need = Demand(route, tripsPerDay);
            int pBudget = BudgetFor(gs, route, out _, out _, out _);
            int cBudget = CabinBudgetFor(gs, route, out _, out _, out _);
            if (need.pilots <= pBudget && need.cabin <= cBudget) return true;
            why = Verdict(gs, route, tripsPerDay);
            return false;
        }

        /// <summary>
        /// 本线这点班次**是哪一岗先卡住**（呈报标题 / 缺口汇总用）。
        /// 返回空串 = 两岗都够；两岗都不够时返回「飞行员、乘务员」。
        /// </summary>
        public static string BindingRoles(GameState gs, RouteData route, int tripsPerDay)
        {
            if (gs == null || route == null || tripsPerDay <= 0) return "";
            var need = Demand(route, tripsPerDay);
            int pBudget = BudgetFor(gs, route, out _, out _, out _);
            int cBudget = CabinBudgetFor(gs, route, out _, out _, out _);
            bool pShort = need.pilots > pBudget;
            bool cShort = need.cabin > cBudget;
            if (pShort && cShort) return RolePilot + "、" + RoleCabin;
            if (pShort) return RolePilot;
            if (cShort) return RoleCabin;
            return "";
        }

        // ———————————————————————— 文案（呈报 / 答复件共用） ————————————————————————

        /// <summary>口径标签：「在职」或「编制（未按人建机组）」。</summary>
        public static string BasisLabel(GameState gs)
        {
            AvailablePilots(gs, out _, out _, out bool byOnDuty);
            return byOnDuty ? "在职飞行员" : "飞行员编制（本档未按人建机组）";
        }

        /// <summary>一岗的「可供」片段：「在职飞行员 60 名 − 其它航线占用 0 名 = 本线可用 60 名」。</summary>
        static string SupplyText(string role, int available, int other, int budget, bool byOnDuty)
            => $"{(byOnDuty ? "在职" : "编制")}{role} {available} 名 − 其它航线占用 {other} 名 = 本线可用 {budget} 名";

        /// <summary>
        /// 机组需求与可供的一句话口径（呈报「飞行机组」段 / 单航线页预检 / 运营部答复件共用）。
        /// 两岗分别报需求与可供，并点名先卡住的那一岗。
        /// </summary>
        public static string Line(GameState gs, RouteData route, int tripsPerDay)
        {
            if (route == null) return "—";
            int trips = Math.Max(1, tripsPerDay);
            var need = Demand(route, trips);
            int perDutyP = OpsRotationLogic.PilotsPerDuty(OpsRotationLogic.BlockOf(route));
            int perDutyC = OpsRotationLogic.CabinPerDuty(route.aircraftType);
            string how = need.shape == OpsRotationLogic.RoundTripShape.CrewSwap
                ? $"外站换组：每日 {trips} 段 = {trips * 2} 个值勤期/天，一组每 3 天最多 2 段 → {need.sets} 组"
                : $"同组往返：每日 {trips} 段，一组一天最多 "
                  + $"{OpsRotationLogic.RoundTripsPerCrewSet(route.aircraftType, LiveClockLogic.ParseMinutes(route.depTime), OpsRotationLogic.BlockOf(route), OpsRotationLogic.TurnOf(route))} 段 → {need.sets} 组";
            var sb = new StringBuilder();
            sb.Append($"需求　每日 {trips} 段需 **{need.sets} 组**机组"
                      + $"（驾驶舱 每组 {perDutyP} 名 = {need.pilots} 名 · 客舱 每组 {perDutyC} 名 = {need.cabin} 名）；{how}。");
            if (gs == null)
            {
                sb.Append("可供　（离线口径，无在册信息 → 不设限）。");
                return sb.ToString();
            }
            int pBudget = BudgetFor(gs, route, out int pAvail, out int pOther, out bool pByOnDuty);
            int cBudget = CabinBudgetFor(gs, route, out int cAvail, out int cOther, out bool cByOnDuty);
            sb.Append($"可供　{SupplyText(RolePilot, pAvail, pOther, pBudget, pByOnDuty)}；");
            sb.Append($"{SupplyText(RoleCabin, cAvail, cOther, cBudget, cByOnDuty)}。");
            if (!pByOnDuty || !cByOnDuty)
                sb.Append(DegradeNote(pByOnDuty, cByOnDuty));
            if (pAvail == 0 || cAvail == 0)
            {
                string zero = pAvail == 0 ? RolePilot : RoleCabin;
                sb.Append($" **{zero}编制为 0**：机队为空或未在册，先有飞机才有编制与机组。");
            }
            string binding = BindingRoles(gs, route, trips);
            if (binding.Length == 0)
                sb.Append(" 机组够排。");
            else
                sb.Append($" **机组不够**：{binding} 不足（{need.pilots} / {need.cabin} 名 vs 可用 "
                          + $"{pBudget} / {cBudget} 名，驾驶舱/客舱）—— 该班次排不出，见下方处置。");
            return sb.ToString();
        }

        /// <summary>口径退化的说明（两岗各自独立判定，故只把真正退化的那一岗写进去）。</summary>
        static string DegradeNote(bool pilotsByOnDuty, bool cabinByOnDuty)
        {
            var parts = new List<string>();
            if (!pilotsByOnDuty) parts.Add(RolePilot);
            if (!cabinByOnDuty) parts.Add(RoleCabin);
            return $"（{string.Join("、", parts)}未按人建机组：其成本在每机人力费率里，故按编制口径核算；"
                   + "招到第一名该岗位人员即切到在职口径。两岗口径各自独立判定）";
        }

        /// <summary>缺口原因（逐波缺口 <c>reason</c> 用）：为什么这一点班次配不出机组。</summary>
        public static string Reason(GameState gs, RouteData route, int tripsPerDay)
            => Verdict(gs, route, Math.Max(1, tripsPerDay));

        static string Verdict(GameState gs, RouteData route, int trips)
        {
            var need = Demand(route, trips);
            int pBudget = BudgetFor(gs, route, out int pAvail, out int pOther, out bool pByOnDuty);
            int cBudget = CabinBudgetFor(gs, route, out int cAvail, out int cOther, out bool cByOnDuty);
            string binding = BindingRoles(gs, route, trips);
            var sb = new StringBuilder();
            sb.Append($"机组缺口：每日 {trips} 段需 {need.sets} 组（驾驶舱 {need.pilots} 名 · 客舱 {need.cabin} 名），");
            sb.Append($"本公司 {SupplyText(RolePilot, pAvail, pOther, pBudget, pByOnDuty)}、"
                      + $"{SupplyText(RoleCabin, cAvail, cOther, cBudget, cByOnDuty)}");
            sb.Append($"（{(need.shape == OpsRotationLogic.RoundTripShape.CrewSwap ? "外站换组，一组每 3 天最多 2 段" : "同组往返，按值勤预算折算")}）");
            if (binding.Length > 0) sb.Append($"。**先卡住的是 {binding}**");
            sb.Append('。');
            return sb.ToString();
        }

        /// <summary>可选处置（与时刻槽缺口并列的第二道闸门 —— 两件都要办才加得了班）。</summary>
        public static string Remedy(GameState gs, RouteData route)
        {
            AvailablePilots(gs, out _, out int pEst, out bool pByOnDuty);
            AvailableCabin(gs, out _, out int cEst, out bool cByOnDuty);
            var sb = new StringBuilder();
            sb.Append("可选处置　");
            sb.Append("① 按缺的那一岗到 人力 → 招聘 补人："
                      + (pByOnDuty ? $"飞行员（编制 {pEst} 名，缺口先在编制内补）" : "飞行员（本档未按人建机组）")
                      + "、"
                      + (cByOnDuty ? $"乘务员（编制 {cEst} 名，按座位配员）" : "乘务员（本档未按人建机组）"));
            sb.Append("；② 未按人建机组的岗位：编制按在役机队推导（飞行员 窄体 8 名/架、宽体 +2；"
                      + "乘务员 每 50 座 4 名 + 每架 4 名备份），加机后编制与可排机组同步上涨");
            sb.Append($"；③ 降低本条线的每日班次（每日 1 段的外站换组线要 3 组，每日 2 段就要 6 组，是线性增长）");
            sb.Append("；④ 先把别的航线班次收一收，把机组腾给本线（机组是全公司共享的）");
            sb.Append("；⑤ 「时刻槽」与「机组」是两道并列的闸门 —— 只批槽不补机组，班次排出来也没人飞。");
            return sb.ToString();
        }

        /// <summary>缺口明细（逐机归因位置上的那一行）。</summary>
        public static List<string> Details(GameState gs, RouteData route, int tripsPerDay)
        {
            var list = new List<string>();
            int trips = Math.Max(1, tripsPerDay);
            int pBudget = BudgetFor(gs, route, out int pAvail, out int pOther, out bool pByOnDuty);
            int cBudget = CabinBudgetFor(gs, route, out int cAvail, out int cOther, out bool cByOnDuty);
            for (int n = trips; n >= 1; n--)
            {
                var need = Demand(route, n);
                if (need.pilots <= pBudget && need.cabin <= cBudget)
                {
                    list.Add($"机组口径：本线最多排 {n} 段/日（{need.sets} 组 · 驾驶舱 {need.pilots} 名 ≤ 可用 {pBudget} 名"
                             + $" · 客舱 {need.cabin} 名 ≤ 可用 {cBudget} 名）");
                    return list;
                }
            }
            var one = Demand(route, 1);
            list.Add($"机组口径：连每日 1 段（{one.sets} 组 · 驾驶舱 {one.pilots} 名 / 客舱 {one.cabin} 名）都超出可用 "
                     + $"（驾驶舱 {pBudget} 名 · 客舱 {cBudget} 名）—— 本线暂无可用机组"
                     + $"（{(pByOnDuty ? "在职飞行员" : "飞行员编制")} {pAvail} 名、其它航线已占 {pOther} 名；"
                     + $"{(cByOnDuty ? "在职乘务员" : "乘务员编制")} {cAvail} 名、其它航线已占 {cOther} 名）");
            return list;
        }
    }
}
