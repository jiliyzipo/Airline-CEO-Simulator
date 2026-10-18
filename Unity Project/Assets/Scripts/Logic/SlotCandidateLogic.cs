using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 时刻槽**备选时刻对**提名（2026-10-15 CEO 第十五次问询）。
    ///
    /// 问询原话：「是否应该在邮件提供几个备选时刻槽，并分析优缺点。」
    ///
    /// 在改之前，「买哪个时刻的槽」这条路上没有**选择**可言：`ApplyWaves` 拿调令要的那一刻去问
    /// <see cref="RoutePermitLogic.FirstSchedulableClock"/>，问不出来就把「缺槽」记在那**一刻**上，
    /// 邮件申请的就是那一刻本身（见 §15.8 修订）。也就是说运营部只报「办不成」，从不告诉 CEO
    /// 「还有别的选法、代价分别是什么」。而现实里的航司恰恰相反：协调池张榜公布哪些时刻还有余量，
    /// 航司拿着**一串**备选去谈，因为不同时刻的收益系数、对端回程能否对上、能不能串飞同一架飞机、
    /// 二级市场要花多少钱，全都不一样。
    ///
    /// 本类把这件事补成与「执飞飞机提名」（<see cref="AircraftNominationLogic"/>）**同源的两段式**：
    /// 运营部按「两端可得性 → 有没有机 → 价格 → 离原计划远近」摆出 2–4 个备选，每个带有利/代价
    /// → CEO 在收件箱点定一个 → 才为该时刻开申请单（免费渠道）或购入单（二级市场）。
    ///
    /// 一条备选**不是「一个时刻」，而是「一段往返时刻对」**：去程离港时刻定了，回程离港时刻就由
    /// 轮挡 + 过站推出来（再吸附到系列时刻，见 <see cref="ScheduleSlots.NearestSeries"/>），
    /// 而对端那一刻的池况、价格、宵禁可能与去程端完全不同 —— 备选之间的真实差异大多出自这里，
    /// 不是出自「早 30 分钟」本身。故 <see cref="SlotOption"/> 两端各记一个状态。
    /// </summary>
    public static class SlotCandidateLogic
    {
        /// <summary>呈报邮件 action（收件箱按此识别「时刻槽备选」）。</summary>
        public const string Action = "slot_select";

        /// <summary>备选上限：再多 CEO 就挑不动了（3–4 个是「有比较、不啰嗦」的区间）。</summary>
        public const int MaxCandidates = 4;

        const char FieldSep = '|';
        const char ItemSep = ';';

        // ———————————————————————— 单端可得性档 ————————————————————————

        /// <summary>该端非协调机场，无需时刻槽（不是「拿不到」，是「不用」）。</summary>
        public const string EndNone = "none";
        /// <summary>该端此刻已有可用持仓（本线占用 / 空闲待绑 / 祖父在飞）→ 不必申请、不必花钱。</summary>
        public const string EndReady = "ready";
        /// <summary>该端此刻无空闲持仓，但所在时段池尚有余量 → 可走协调池免费申请，下季交割。</summary>
        public const string EndApply = "apply";
        /// <summary>
        /// 该端缺槽，但**本线在该站已经有一条槽**，只是时刻对不上（超 15 分容差、在改时窗口内）
        /// → 走**时刻变更申请**（改时），不是新申请。依据见 <see cref="RoutePermitLogic.TryTimeChange"/>。
        /// </summary>
        public const string EndChange = "change";
        /// <summary>该端此刻无空闲持仓、且时段池已满 → 申请与购入都无门，只能等别的航线释放。</summary>
        public const string EndBlocked = "blocked";

        public static string EndStateName(string state)
        {
            switch (state)
            {
                case EndReady: return "即刻可排";
                case EndApply: return "需申请";
                case EndChange: return "需改时";
                case EndBlocked: return "拿不到";
                default: return "无需槽";
            }
        }

        /// <summary>一个备选时刻对（去程离港时刻为身份，回程时刻由轮挡 + 过站推出）。</summary>
        public class SlotOption
        {
            public string depClock = "";      // 去程离港时刻（备选身份）
            public string pegClock = "";      // 调令原本要的那一刻（用于标「你原本要的」）
            public int band;                  // 所属需求波（池容量按它归集）
            public string outAirport = "";
            public string inAirport = "";
            public string inClock = "";       // 回程离港时刻（已吸附到系列）
            public string outState = EndNone;
            public string inState = EndNone;
            /// <summary>★2026-10-16：经停端需求条数（经停站一趟往返两条：去程一次、回程一次）。</summary>
            public int viaTotal;
            /// <summary>★2026-10-16：其中还**办不成**（需申请）的条数。</summary>
            public int viaApplyCount;
            /// <summary>★2026-10-16 第四十一次修订：经停端里**需改时**（已有槽、时刻滑点）的条数。</summary>
            public int viaChangeCount;
            /// <summary>★2026-10-16：经停端逐条的可得性说明（本场端/对端已有 outState/inState，经停端列在此）。</summary>
            public List<string> endNotes = new List<string>();
            public float priceWan;            // 两端「需取得」者合计的二级市场参考价（协调池渠道为 0）
            public float demand = 1f;         // 需求系数（该时刻所属需求波）
            public bool aircraftOk;           // 那一刻能否真派出一架（含值勤/宵禁/串飞）
            public string aircraftNote = "";
            public bool curfewOk = true;
            public string curfewNote = "";
            public bool sameDay = true;       // 当日能否回场
            public int windowMin;             // 占窗分钟（日利用率贡献）
            public bool viable;               // 两端都拿得到（ready/apply/none）
            public bool readyNow;             // 两端都不必申请、不必花钱，立刻能排
            public int nearestGapMin = -1;    // 与本线现有班次的最近间隔
            public string pros = "";
            public string cons = "";

            /// <summary>是否需要为这一刻花申请/购入的动作。</summary>
            public bool NeedsAction => !readyNow;
        }

        // ———————————————————————— 备选生成 ————————————————————————

        /// <summary>
        /// 在 <paramref name="pegClock"/> 所属需求波窗口的**整张 30 分钟网格**上生成备选，
        /// 含**早于**峰点的时刻（这是与旧口径的关键差别：手上握着 07:30 的槽、却因为
        /// 调令写的是 08:00 而报「缺槽要去买」，正是旧排班器「只向后搜」逼出来的假缺口）。
        ///
        /// 传 <paramref name="pegClock"/> 那一刻若两端齐备，调用方不该生成备选（没得挑也没必要挑）。
        /// 返回已排好序、已截断到 <see cref="MaxCandidates"/> 的备选表；一个都排不出时返回空表，
        /// 调用方据此改发「受阻」汇报件。
        /// </summary>
        public static List<SlotOption> Options(GameState gs, RouteData route, string pegClock)
        {
            var result = new List<SlotOption>();
            if (gs == null || route == null) return result;
            int pm = ScheduleSlots.ParseClock(pegClock);
            if (pm < 0) return result;

            int band = ScheduleSlots.BandOfDep(ScheduleSlots.FormatClock(pm));
            var plan = new OpsRotationLogic.RotationPlan();
            // 含本线自身：候选要回答的是「再加这一波排不排得下」，本线既有的波次自然也在占用之列。
            plan.LoadFrom(gs.activeRoutes);

            var taken = ExistingClocks(route);
            var grid = ScheduleSlots.SlotGridLabels(band);
            var pool = new List<SlotOption>();
            for (int k = 0; k < grid.Count; k++)
            {
                string clock = grid[k];
                // 本线已在那一刻飞着一班 —— 一槽一班，同一刻不能再排第二波
                if (ClockNear(taken, clock)) continue;
                var opt = Build(gs, route, clock, pegClock, band, plan);
                if (opt == null || !opt.viable) continue;
                pool.Add(opt);
            }

            pool.Sort(Compare);
            for (int i = 0; i < pool.Count && i < MaxCandidates; i++) result.Add(pool[i]);
            return result;
        }

        /// <summary>
        /// 备选排序口径（逐条可复现）：
        /// ① 两端即刻可排者优先（不用申请、不用花钱、不用等一季）；
        /// ② 那—刻派得出机的优先（否则槽拿到手也飞不了）；
        /// ③ 总价低者优先；
        /// ④ 离 CEO 原本要的那一刻近者优先（尽量不改需求波里的位置）；
        /// ⑤ 时刻升序，保证同一台账每次生成同一顺序。
        /// </summary>
        static int Compare(SlotOption x, SlotOption y)
        {
            int c = y.readyNow.CompareTo(x.readyNow);
            if (c != 0) return c;
            c = y.aircraftOk.CompareTo(x.aircraftOk);
            if (c != 0) return c;
            c = y.sameDay.CompareTo(x.sameDay);
            if (c != 0) return c;
            c = x.priceWan.CompareTo(y.priceWan);
            if (c != 0) return c;
            c = Offset(x).CompareTo(Offset(y));
            if (c != 0) return c;
            return string.CompareOrdinal(x.depClock, y.depClock);
        }

        static int Offset(SlotOption o)
        {
            int a = ScheduleSlots.ParseClock(o.depClock);
            int b = ScheduleSlots.ParseClock(o.pegClock);
            if (a < 0 || b < 0) return int.MaxValue;
            return a > b ? a - b : b - a;
        }

        /// <summary>本线现有班次的离港时刻（同一刻不能再排一波）。</summary>
        static List<string> ExistingClocks(RouteData route)
        {
            var list = new List<string>();
            if (route == null) return list;
            route.EnsureFlights();
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null || string.IsNullOrEmpty(f.depTime)) continue;
                if (!list.Contains(f.depTime)) list.Add(f.depTime);
            }
            return list;
        }

        static bool ClockNear(IList<string> list, string clock)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (ScheduleSlots.ClockDiff(list[i], clock) <= ScheduleSlots.SlotToleranceMinutes) return true;
            return false;
        }

        /// <summary>就该时刻（去程离港）评估一段往返：两端可得性 → 机队 → 宵禁 → 回程与日利用率 → 优缺点。</summary>
        static SlotOption Build(GameState gs, RouteData route, string clock, string pegClock, int band,
            OpsRotationLogic.RotationPlan plan)
        {
            int m = ScheduleSlots.ParseClock(clock);
            if (m < 0) return null;

            var opt = new SlotOption
            {
                depClock = clock,
                pegClock = pegClock,
                band = band,
                outAirport = route.origin,
                inAirport = route.destination,
                demand = ScheduleSlots.DemandOfTime(clock)
            };

            // —— 各停站可得性：按到站时刻表逐站判（本场端 / 经停端·去程 / 对端 / 经停端·回程）——
            var needs = RoutePermitLogic.ClockNeeds(route, clock);
            bool viable = true, readyNow = true;
            var endNotes = new List<string>();
            for (int i = 0; i < needs.Count; i++)
            {
                var need = needs[i];
                string st = EndStateOf(gs, route, need);
                if (need.station == RoutePermitLogic.StationOrigin)
                {
                    opt.outState = st;
                }
                else if (need.station == RoutePermitLogic.StationDest)
                {
                    opt.inState = st;
                    opt.inClock = RoutePermitLogic.TimeOf(need);
                }
                else
                {
                    // ★2026-10-16：经停站一趟往返停两次 → 两条独立的槽需求，各自单独计价与说明。
                    // 旧版只认「去程离港 / 回程离港」两条，经停端的槽从来没进过这份报价。
                    opt.viaTotal++;
                    if (st == EndApply) opt.viaApplyCount++;
                    if (st == EndChange) opt.viaChangeCount++;
                    endNotes.Add($"{RoutePermitLogic.LegName(need)} {need.airport} {RoutePermitLogic.LegClocks(need)}　{EndStateName(st)}"
                                 + (st == EndChange ? $"（{RoutePermitLogic.SlotChangeNote(gs, route, need)}）" : ""));
                }
                if (st == EndBlocked) viable = false;
                if (st == EndReady || st == EndNone) continue;
                readyNow = false;
                // 需取得的那一端才计价：协调池申请免费，二级市场按机场等级 × 时段计价
                opt.priceWan += StrategyEconomics.SlotValueWan(need.airport, need.band);
            }
            opt.viable = viable;
            opt.readyNow = viable && readyNow;
            opt.endNotes = endNotes;

            // —— 机队：那一刻真派不派得出机（与排班同一套判据：值勤 / 过站 / 宵禁 / 串飞）——
            var ac = OpsRotationLogic.PickForWindow(route, gs.fleet, plan, band, false, gs,
                                                    out int depMin, out string why, m);
            opt.aircraftOk = ac != null
                             && RoutePermitLogic.CanScheduleClock(gs, route, ScheduleSlots.FormatClock(depMin), true);
            opt.aircraftNote = ac == null
                ? (string.IsNullOrEmpty(why) ? "那一刻无可用运力" : why)
                : $"{ac.registration}（{ac.type}）可派";

            // —— 宵禁：一次往返涉 4 个起降时刻，逐个核两端机场 ——
            opt.curfewOk = !OpsRotationLogic.CurfewBlocked(route, m, out string cw);
            opt.curfewNote = cw;

            // —— 回程与日利用率 ——
            int block = OpsRotationLogic.BlockOf(route);
            int turn = OpsRotationLogic.TurnOf(route);
            int retDep = m + block + turn;
            int retArr = retDep + block;
            opt.windowMin = retArr - m;
            opt.sameDay = retArr <= 24 * 60;

            // —— 与本线现有班次的最近间隔（够不够过站 → 能不能串飞同一架）——
            opt.nearestGapMin = NearestGap(route, m);

            BuildProsCons(route, opt, block, turn);
            return opt;
        }

        /// <summary>
        /// 单端可得性档。判据与排班闸门 <see cref="RoutePermitLogic.CanScheduleEnd"/> 同源：
        /// 先看该刻有没有**属于本线的占用**或**空闲持仓**，再看祖父条款，最后看池还有没有余量。
        /// </summary>
        public static string EndStateOf(GameState gs, RouteData route, RoutePermitLogic.SlotNeed need)
        {
            if (string.IsNullOrEmpty(need.airport) || !RoutePermitLogic.NeedsDepartureSlot(need.airport)) return EndNone;
            switch (RoutePermitLogic.EndStatusKey(gs, route, need))
            {
                case "bound":
                case "free":
                case "grandfather":
                    return EndReady;
                case "missing":
                    // 池还有余量 → 可申请（免费）；池满 → 申请与购入都无门
                    // ★2026-10-16：判据抽到 RoutePermitLogic.CanApplyEnd，与「开几张申请单」同源
                    // ★2026-10-16 第四十一次修订：若本线在该站**已有一条槽、只是时刻滑点**，
                    //   现实里走的是「时刻变更申请」（WASG 9.1.3），比新申请快且不占新容量 ——
                    //   但协调人仍按**系列余量**批驳：目标系列已满 → 与「拿不到」同档。
                    if (RoutePermitLogic.TryTimeChange(gs, route, need, out _, out _, out _, out bool room))
                        return room ? EndChange : EndBlocked;
                    return RoutePermitLogic.CanApplyEnd(gs, route, need) ? EndApply : EndBlocked;
                default:
                    return EndNone;
            }
        }

        /// <summary>离该时刻最近的现有班次间隔（分钟）；本线尚未开飞返回 −1。</summary>
        static int NearestGap(RouteData route, int depMin)
        {
            var clocks = ExistingClocks(route);
            int best = -1;
            for (int i = 0; i < clocks.Count; i++)
            {
                int c = ScheduleSlots.ParseClock(clocks[i]);
                if (c < 0) continue;
                int d = c > depMin ? c - depMin : depMin - c;
                if (best < 0 || d < best) best = d;
            }
            return best;
        }

        /// <summary>逐条利弊。**只写台账里算得出来的事实**，不编「客流会更好」这类无据之说。</summary>
        static void BuildProsCons(RouteData route, SlotOption opt, int block, int turn)
        {
            var pros = new List<string>();
            var cons = new List<string>();

            string bandName = StrategyManager.SlotBandName(opt.band);
            int pegMin = ScheduleSlots.ParseClock(opt.pegClock);
            int depMin = ScheduleSlots.ParseClock(opt.depClock);
            int delta = pegMin >= 0 && depMin >= 0 ? depMin - pegMin : 0;

            // ① 各停站可得性 —— 最硬的一条：决定要不要花钱、要不要等一季
            if (opt.readyNow)
                pros.Add("各停站此刻都有可用持仓，**不必申请、不必花钱、本季即可排班**");
            else
            {
                if (opt.outState == EndApply)
                    cons.Add($"本场端 {opt.outAirport} {opt.depClock} 无空闲持仓，需走协调池申请（免费，下季交割）");
                if (opt.inState == EndApply)
                    cons.Add($"对端 {opt.inAirport} {opt.inClock} 无空闲持仓，需走协调池申请（免费，下季交割）");
                if (opt.outState == EndReady) pros.Add($"本场端 {opt.outAirport} {opt.depClock} 已有持仓，这一段不用再办");
                if (opt.inState == EndReady) pros.Add($"对端 {opt.inAirport} {opt.inClock} 已有持仓，这一段不用再办");
                // ★2026-10-16 第四十一次修订：**需改时** —— 本线在该站已有槽，只是时刻滑点。
                //   现实里改时是持槽后的动作（WASG 9.1.3），比新申请快、不占新容量；
                //   但仍要协调人按系列余量批，故写成「要办但要批」的一条利弊。
                if (opt.outState == EndChange)
                    cons.Add($"本场端 {opt.outAirport} {opt.depClock} 缺槽，但本线在该站已有一条槽 —— "
                             + "需向协调人提**时刻变更申请**（改时，免费），批准即用，不必等新槽");
                if (opt.inState == EndChange)
                    cons.Add($"对端 {opt.inAirport} {opt.inClock} 缺槽，但本线在该站已有一条槽 —— "
                             + "需向协调人提**时刻变更申请**（改时，免费），批准即用，不必等新槽");
                // ★2026-10-16：经停端两条（去程/回程各一次停留）也逐条进利弊 —— 它们同样要办、同样要花钱
                for (int i = 0; i < (opt.endNotes == null ? 0 : opt.endNotes.Count); i++)
                {
                    string note = opt.endNotes[i];
                    if (note.IndexOf(EndStateName(EndApply), StringComparison.Ordinal) >= 0)
                        cons.Add($"经停端 {note} —— 经停站一趟往返停两次，两次的槽都要单独办");
                    else if (note.IndexOf(EndStateName(EndChange), StringComparison.Ordinal) >= 0)
                        cons.Add($"经停端 {note} —— 该站已有槽但时刻滑点，需提时刻变更申请（改时）");
                    else if (note.IndexOf(EndStateName(EndReady), StringComparison.Ordinal) >= 0)
                        pros.Add($"经停端 {note}");
                    else if (note.IndexOf(EndStateName(EndBlocked), StringComparison.Ordinal) >= 0)
                        cons.Add($"经停端 {note}");
                }
                if (opt.priceWan > 0.01f)
                    cons.Add($"若不愿排队，走二级市场购入约 {opt.priceWan:F0} 万（协调池申请不收费）");
            }

            // ② 机队
            if (opt.aircraftOk) pros.Add($"机队　{opt.aircraftNote}，值勤与过站都排得下");
            else cons.Add($"机队　那一刻派不出机：{opt.aircraftNote}");

            // ③ 宵禁
            if (!opt.curfewOk) cons.Add($"宵禁　{opt.curfewNote} —— 该时刻不可排");

            // ④ 回程与日利用率
            string hr = $"{opt.windowMin / 60f:F1} 小时";
            if (opt.sameDay)
                pros.Add($"当日往返（去程 {opt.depClock} → 回程 {opt.inClock} → 本场），占窗 {hr}");
            else
                cons.Add($"回程跨夜（{opt.inClock} 起飞），当日回不了本场，日利用率只算半天（占窗 {hr}）");

            // ⑤ 与现有班次的间隔：够过站就能串飞同一架，利用率与成本都占优
            if (opt.nearestGapMin >= 0)
            {
                int need = block + turn;
                if (opt.nearestGapMin >= need)
                    pros.Add($"与本线现有班次间隔 {opt.nearestGapMin} 分（≥ 最短衔接 {need} 分），可串飞同一架飞机");
                else
                    cons.Add($"与本线现有班次只差 {opt.nearestGapMin} 分（不足最短衔接 {need} 分），须另派一架");
            }

            // ⑥ 与原计划的位置差 —— 让 CEO 一眼看出「这一条是往前挪还是往后挪」
            if (delta != 0 && pegMin >= 0)
            {
                string dir = delta < 0 ? "早" : "晚";
                pros.Add($"较你原本要的 {opt.pegClock} {dir} {Math.Abs(delta)} 分（同属{bandName}需求波，系数 {opt.demand:F2} 不变）");
            }
            else if (pegMin >= 0)
            {
                pros.Add($"就是你原本要的 {opt.pegClock}（{bandName}需求波，系数 {opt.demand:F2}）");
            }

            opt.pros = string.Join("\n", pros.ToArray());
            opt.cons = cons.Count > 0 ? string.Join("\n", cons.ToArray()) : "";
        }

        // ———————————————————————— 编解码（存在邮件里） ————————————————————————

        public static string Encode(IList<SlotOption> list)
        {
            if (list == null || list.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == null || string.IsNullOrEmpty(o.depClock)) continue;
                if (sb.Length > 0) sb.Append(ItemSep);
                sb.Append(o.depClock).Append(FieldSep).Append(o.outState).Append(FieldSep).Append(o.inState)
                  .Append(FieldSep).Append(o.inClock).Append(FieldSep).Append(o.band)
                  .Append(FieldSep).Append(o.priceWan.ToString("F2"))
                  .Append(FieldSep).Append(o.demand.ToString("F2"))
                  .Append(FieldSep).Append(o.readyNow ? "1" : "0")
                  .Append(FieldSep).Append(o.aircraftOk ? "1" : "0")
                  .Append(FieldSep).Append(o.viaTotal)
                  .Append(FieldSep).Append(o.viaApplyCount)
                  // ★2026-10-16 第四十一次修订：`需改时`的条数另立一格 —— 旧档没这一格，
                  //   Decode 用 f.Length 判界，读到旧档就当 0（不炸、不误判）。
                  .Append(FieldSep).Append(o.viaChangeCount);
            }
            return sb.ToString();
        }

        public static List<SlotOption> Decode(string csv)
        {
            var list = new List<SlotOption>();
            if (string.IsNullOrEmpty(csv)) return list;
            string[] items = csv.Split(new[] { ItemSep }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < items.Length; i++)
            {
                string[] f = items[i].Split(new[] { FieldSep }, StringSplitOptions.None);
                if (f.Length < 2) continue;
                var o = new SlotOption { depClock = f[0].Trim() };
                if (string.IsNullOrEmpty(o.depClock)) continue;
                o.outState = f[1].Trim();
                if (f.Length > 2) o.inState = f[2].Trim();
                if (f.Length > 3) o.inClock = f[3].Trim();
                if (f.Length > 4) int.TryParse(f[4], out o.band);
                if (f.Length > 5) float.TryParse(f[5], out o.priceWan);
                if (f.Length > 6) float.TryParse(f[6], out o.demand);
                o.readyNow = f.Length > 7 && f[7].Trim() == "1";
                o.aircraftOk = f.Length > 8 && f[8].Trim() == "1";
                if (f.Length > 9) int.TryParse(f[9], out o.viaTotal);
                if (f.Length > 10) int.TryParse(f[10], out o.viaApplyCount);
                if (f.Length > 11) int.TryParse(f[11], out o.viaChangeCount);
                o.viable = true;
                list.Add(o);
            }
            return list;
        }

        public static int IndexOf(IList<SlotOption> list, string depClock)
        {
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null
                    && string.Equals(list[i].depClock, depClock, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        // ———————————————————————— 文案 ————————————————————————

        public static string Subject(RouteData route, bool blocked, int count = 0)
            => blocked
                ? $"关于 {route.origin}—{route.destination} 加班时刻：本窗口无槽可得（汇报）"
                : $"关于 {route.origin}—{route.destination} 加班时刻槽：{count} 个备选（请选定一个）";

        /// <summary>为什么提这条 → 备选逐条（含利弊）→ 口径 → 批准后的动作。</summary>
        public static string FormatBody(GameState gs, RouteData route, IList<SlotOption> opts, string pegClock)
        {
            if (route == null) return "航线不可用。";
            var sb = new StringBuilder();
            int pm = ScheduleSlots.ParseClock(pegClock);
            string peg = pm >= 0 ? ScheduleSlots.FormatClock(pm) : pegClock;
            int band = pm >= 0 ? ScheduleSlots.BandOfDep(peg) : 0;

            string fn = string.IsNullOrEmpty(route.flightNumber) ? "" : $" {route.flightNumber}";
            sb.Append($"运营部就 {route.origin}—{route.destination}{fn}"
                      + $"（{route.distanceKm:F0} km）的加班时刻槽给出备选。\n");
            sb.Append($"本线现状　{route.DualWeeklyLine()}　日利 {route.dailyProfit:F2} 万　"
                      + $"在飞机 {(string.IsNullOrEmpty(route.registration) ? "—" : route.registration)}\n");
            sb.Append($"本波要的　{peg}（{StrategyManager.SlotBandName(band)}需求波，窗口 "
                      + $"{ScheduleSlots.FormatClock(ScheduleSlots.WindowStartOf(band))}–"
                      + $"{ScheduleSlots.FormatClock(ScheduleSlots.WindowEndOf(band))}）\n");
            sb.Append($"票已问过协调人　{peg} 这一刻各停站拿不齐，但同一窗口内还有下列{opts.Count}条路可走。\n\n");

            for (int i = 0; i < opts.Count; i++)
            {
                var o = opts[i];
                if (o == null) continue;
                string mark = i == 0 ? "（运营部推荐）" : "";
                sb.Append($"候选 {i + 1}　{o.depClock} 起飞的往返{mark}\n");
                sb.Append($"　各停站　{EndLine(gs, route, o.depClock, RoutePermitLogic.StationOrigin, false)}　"
                          + $"{EndLine(gs, route, o.depClock, RoutePermitLogic.StationDest, true)}\n");
                // ★2026-10-16：经停站一趟往返停两次 → 两条独立的槽，逐条列出（非经停线此行不出现）
                var viaLines = ViaEndLines(gs, route, o.depClock);
                for (int k = 0; k < viaLines.Count; k++) sb.Append("　经停端　" + viaLines[k] + "\n");
                sb.Append($"　日利系数　{o.demand:F2}（{StrategyManager.SlotBandName(o.band)}）"
                          + $"　占窗　{o.windowMin / 60f:F1} 小时"
                          + (o.sameDay ? "（当日回场）" : "（跨夜回场）") + "\n");
                if (o.priceWan > 0.01f)
                    sb.Append($"　取槽成本　协调池申请 0（排队）／二级市场约 {o.priceWan:F0} 万\n");
                if (!string.IsNullOrEmpty(o.pros))
                    sb.Append("　有利　" + o.pros.Replace("\n", "\n　　　　") + "\n");
                if (!string.IsNullOrEmpty(o.cons))
                    sb.Append("　代价　" + o.cons.Replace("\n", "\n　　　　") + "\n");
            }

            sb.Append("\n口径：\n");
            sb.Append($"　① 时刻槽精确到时刻　一条槽就是「某日某刻的一次到场 or 一次离场」，"
                      + $"{ScheduleSlots.SlotToleranceMinutes} 分容差内的班次才算数（{ScheduleSlots.SlotToleranceMinutes * 2} 分以外的槽帮不上这一刻）；\n");
            sb.Append("　② 一停站一对槽　飞机在哪一站停一次，那一站就要一对（到场 + 离场）：本场端 1 对、对端 1 对；"
                      + "**经停站一趟往返停两次，故要 2 对**（去程一次、回程一次）。各站时刻由轮挡 + 过站（+ 停站）推出并吸附到系列；\n");
            sb.Append("　③ 两条渠道　协调池分配（WASG 正常渠道，免费，下季交割）／二级市场购入（付费，同期交割）；"
                      + "**另有第三条：已持有该站槽、只是时刻滑点 → 时刻变更申请（改时，WASG 9.1.3，免费）**；\n");
            sb.Append("　④ 排序口径　即刻可排 → 那一刻派得出机 → 当日回场 → 价格低 → 离原计划近。\n");

            sb.Append("\n批准后的动作：按选定时刻为**仍然缺槽的每一个停站**各开一张单 ——"
                      + "没持有过就走协调池申请（免费，下季交割）；**已持有、只是时刻滑点的走时刻变更申请（改时，免费）**，"
                      + "两类都齐了运营部自动重排本线并呈报加机。\n");
            sb.Append("退回则本线维持现状，不申请、不改时、不花钱。");
            return sb.ToString();
        }

        /// <summary>
        /// 一行停站可得性：`本场端 FRA 到场 17:00 · 离场 06:30 已有持仓`。
        ///
        /// ★2026-10-16：从「去程离港 / 回程离港」改成**停站 + 一对时刻**（到场 + 离场）。
        /// 一条槽是「一次到场 or 一次离场」的许可，只报离场等于只说了半条，而呈报正文里
        /// 「先批两端时刻槽申请」那种说法正是这么读错出来的。
        /// </summary>
        static string EndLine(GameState gs, RouteData route, string depClock, string station, bool inbound)
        {
            var need = FindNeed(gs, route, depClock, station, inbound);
            if (need.airport == null) return "—";
            string st = EndStateOf(gs, route, need);
            string head = $"{RoutePermitLogic.LegName(need)} {need.airport} {RoutePermitLogic.LegClocks(need)}";
            return st == EndNone ? $"{head} 非协调，无需槽" : $"{head} {EndStateName(st)}";
        }

        /// <summary>经停端两条需求的可得性说明（非经停线返回空表）。</summary>
        static List<string> ViaEndLines(GameState gs, RouteData route, string depClock)
        {
            var lines = new List<string>();
            var needs = RoutePermitLogic.ClockNeeds(route, depClock);
            for (int i = 0; i < needs.Count; i++)
            {
                var n = needs[i];
                if (n.station != RoutePermitLogic.StationVia) continue;
                lines.Add($"{n.airport} {RoutePermitLogic.LegClocks(n)}　{EndStateName(EndStateOf(gs, route, n))}");
            }
            return lines;
        }

        /// <summary>在该刻的需求表里取指定停站那一条（缺则返回 airport 为 null 的空需求）。</summary>
        static RoutePermitLogic.SlotNeed FindNeed(GameState gs, RouteData route, string depClock, string station, bool inbound)
        {
            var needs = RoutePermitLogic.ClockNeeds(route, depClock);
            for (int i = 0; i < needs.Count; i++)
            {
                var n = needs[i];
                if (!string.Equals(n.station, station, StringComparison.OrdinalIgnoreCase)) continue;
                if (n.inbound != inbound) continue;
                return n;
            }
            return default;
        }

        /// <summary>一个备选都排不出时的汇报件正文（该窗口整条网格都拿不到）。</summary>
        public static string FormatBlockedBody(GameState gs, RouteData route, string pegClock)
        {
            if (route == null) return "航线不可用。";
            int pm = ScheduleSlots.ParseClock(pegClock);
            string peg = pm >= 0 ? ScheduleSlots.FormatClock(pm) : pegClock;
            int band = pm >= 0 ? ScheduleSlots.BandOfDep(peg) : 0;
            var sb = new StringBuilder();
            sb.Append($"运营部就 {route.origin}—{route.destination} 的加班时刻槽核过协调池，"
                      + $"**本窗口（{StrategyManager.SlotBandName(band)} "
                      + $"{ScheduleSlots.FormatClock(ScheduleSlots.WindowStartOf(band))}–"
                      + $"{ScheduleSlots.FormatClock(ScheduleSlots.WindowEndOf(band))}）整张时刻表上都拿不到可用的往返时刻对**。\n\n");
            sb.Append($"本线现状　{route.DualWeeklyLine()}　日利 {route.dailyProfit:F2} 万\n");
            sb.Append($"本波要的　{peg}\n\n");
            sb.Append("各停站池况：\n");
            if (RoutePermitLogic.NeedsDepartureSlot(route.origin))
                sb.Append(InboxLetter.Bullet(RoutePermitLogic.SlotPoolLine(gs, route.origin, band)));
            string via = RoutePermitLogic.StopoverOf(route);
            if (via != null && RoutePermitLogic.NeedsDepartureSlot(via))
                sb.Append(InboxLetter.Bullet(RoutePermitLogic.SlotPoolLine(gs, via, band)));
            if (RoutePermitLogic.NeedsDepartureSlot(route.destination))
                sb.Append(InboxLetter.Bullet(RoutePermitLogic.SlotPoolLine(gs, route.destination, band)));
            sb.Append("\n下一步（三条路）：\n");
            sb.Append(InboxLetter.Bullet("等其它航线释放时刻后再下加机指令（换季清算时会释出闲置持仓）"));
            sb.Append(InboxLetter.Bullet("改到其它需求波试一次（如早班拿不到，午班/晚班的池况是另一本账）"));
            sb.Append(InboxLetter.Bullet($"或到「航权与时刻槽」页查看 {route.origin}、{route.destination}"
                + (via == null ? "" : $"、{via}") + " 的持仓与池余量"));
            sb.Append(InboxLetter.Bullet("本函为汇报，无需批复"));
            return sb.ToString();
        }
    }
}
