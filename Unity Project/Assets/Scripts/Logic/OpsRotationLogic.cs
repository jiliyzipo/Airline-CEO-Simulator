using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 一机多段 / 枢纽往返轮转（Ops Rotation）。
    /// 现实口径：一架飞机一天按 8~12 小时日利用率，在基地机场连续执行多个时段的往返
    /// （去程落地 → 过站 → 回程落地回到基地 → 再执行下一个时段），因此一天可串 2~3 条航线的班次。
    /// 本类只负责「飞机 ↔ 班次」的时段可行性判定与机队轮转编排：
    /// 不改班期、不改时刻、不改收益口径（班次与收益仍由 OpsScheduleLogic / OpsEconomics 决定）。
    /// </summary>
    public static class OpsRotationLogic
    {
        /// <summary>一天的分钟数。权威值在 OpsRotationParams（纯 C#，供离线模拟共用）。</summary>
        public const int MinutesPerDay = OpsRotationParams.MinutesPerDay;
        /// <summary>轮转裕度（清洁 / 加油 / 拖曳）。</summary>
        public const int RotationGapMinutes = 30;
        /// <summary>期望日利用率（2 段往返 ≈ 10 小时），超过则降权，避免过度集中在少数飞机。</summary>
        public const int PreferredTurnsPerAircraft = 2;

        // ================= 机组值勤期（FDP）基准（R9 / R10 / R12） =================
        //
        // 「一天」= 一个值勤期：报到 → 最后一个航班落地；受宵禁与最低休息约束。
        // 上限由「航段数 + 报到时刻」决定（窄体）；宽体走增援机组的绝对上限，且航段 ≤ 3。

        /// <summary>报到提前量（首段起飞前）。权威值在 OpsRotationParams（纯 C#，供离线模拟共用）。</summary>
        public const int ReportBeforeMinutes = OpsRotationParams.ReportBeforeMinutes;
        /// <summary>两个值勤期之间的最低休息（基地可有限度降至 9h 并补偿，此处不建模）。</summary>
        public const int MinRestMinutes = 10 * 60;
        /// <summary>增援运行的航段数硬上限（FAA §117.17(d) / EASA 同为 3）。</summary>
        public const int AugmentedSectorCap = 3;
        /// <summary>单段超过此时长（分钟）视为长程（换组口径的**下界参考值**）。
        ///
        /// ★2026-10-13：本常量**已不再用作判据** —— 判据改为「当天往返装不进值勤上限」的结果导向
        /// （见 <see cref="CanTake"/>）。留它只为「一眼看出这是远程线」的文案与既有用例。
        /// 旧写法以 `单段 > 9h` 做闸门，留下一条 5,500–7,650 km 的死区（详见 CanTake 注释）。</summary>
        public const int LongHaulLegMinutes = 9 * 60;

        /// <summary>
        /// 否决原因的分类。**给「无机可派」的汇报件用** —— 那张函必须能说清
        /// 「本线到底卡在哪一条」，而不是把所有飞机一律归因成「航程不足」。
        ///
        /// 为什么必须分类：CEO 报障那封函把值勤超限的线写成了「机队里没有能飞本线的飞机」
        /// 并建议「采购/租赁满足航程的机型」，而机队最长航程本就够 —— 照它办等于花钱买飞机
        /// 却依然飞不了。归因错了，处置就全错。
        /// </summary>
        public enum BlockKind
        {
            None = 0,
            Missing,        // 参数缺失
            Status,         // 停用 / 退役 / 停场检修
            Transition,     // 已投入过渡业务或已预约机位
            TypeMismatch,   // 机型不符（调用方要求同型）
            Range,          // 航程不足 / 机型不在目录
            Conflict,       // 时段冲突（被本线或他线的往返占用）
            Duty,           // 值勤超限（含「连外站换组也装不下」）
            SectorCap,      // 增援航段上限
            Curfew          // 宵禁
        }

        /// <summary>
        /// 基础值勤上限（分钟）：按航段数分级，2 人制机组、报到 06:00–13:29 基准
        /// （EASA CS-FTL.1）。航段越多，上限越低 —— 这是双重收紧。
        /// </summary>
        public static int DutyLimitBySectors(int sectors)
        {
            if (sectors <= 2) return 13 * 60;
            switch (sectors)
            {
                case 3: return 12 * 60 + 30;
                case 4: return 12 * 60;
                case 5: return 11 * 60 + 30;
                case 6: return 11 * 60;
                default: return 10 * 60;
            }
        }

        /// <summary>报到时刻修正（分钟）：越早 / 越晚报到，上限越低。</summary>
        public static int ReportAdjust(int reportMin)
        {
            int m = ((reportMin % 1440) + 1440) % 1440;
            if (m < 4 * 60) return -60;
            if (m < 6 * 60) return -30;
            if (m <= 13 * 60 + 29) return 0;
            if (m <= 17 * 60) return -30;
            return -60;
        }

        /// <summary>
        /// 一次值勤的驾驶舱人数（3 人制 / 超长程 4 人制）。
        ///
        /// 从 <see cref="BudgetOf"/> 里抽出来单列，是因为「一条线要几个机组组、要几名飞行员」
        /// 也要用它 —— 同一件事只能有一套算术（2026-10-14 第十次问询）。
        /// </summary>
        public static int PilotsPerDuty(int longestBlockMin) => longestBlockMin >= 9 * 60 ? 4 : 3;

        /// <summary>
        /// 外站换组的线按「每天 N 圈」运行时需要几个机组**组**（= 几个轮转班底）。
        ///
        /// ★「组」是**驾驶舱与客舱共用**的单位：一个班底里两岗一起走（同去程、同驻站、同回程），
        /// 所以组数只有这一个算术。**每组各有几人**才分两岗各算各的 ——
        /// 驾驶舱看值勤时长（<see cref="PilotsPerDuty"/>），客舱看座位（<see cref="CabinPerDuty"/>）。
        /// （2026-10-14 第十三次问询修正：原注释写作「一组 = 一整套机组，含驾驶舱与客舱」，
        /// 与本类只算驾驶舱的人数是**注释与算术打架**，现改为两岗分别明确。）
        ///
        /// 依据（全部取自本类自己的休息口径，可由 R34 复算）：
        /// <list type="number">
        /// <item>每天的值勤期数 = 2 × 每日圈数（一圈拆成去程组 + 驻站回程组）；</item>
        /// <item>一组每 3 天最多飞 2 段：飞完去程人已在对方站，只能接**次日**回程；
        ///   回程落地本场后又是深夜（本线 00:46），接不上当日/次日早班报到，必空一天；</item>
        /// <item>故一组 2/3 段/天 → 组数 = ⌈2N ÷ (2/3)⌉ = 3N。</item>
        /// </list>
        /// 短程同组往返（<see cref="RoundTripShape.SingleDuty"/>）按**值勤预算**算一天能塞几圈：
        /// 一组连飞多圈只受值勤上限约束（FRA–ATL 这类超长程根本进不了这一支）。
        /// </summary>
        public static int CrewSetsForDaily(int tripsPerDay, RoundTripShape shape, string aircraftType,
            int depMin, int blockMin, int turnMin)
        {
            int n = Math.Max(1, tripsPerDay);
            if (shape == RoundTripShape.CrewSwap) return 3 * n;
            return (int)Math.Ceiling(n / (float)RoundTripsPerCrewSet(aircraftType, depMin, blockMin, turnMin));
        }

        /// <summary>一天内一组机组能飞几圈（同组往返口径；受值勤预算与航段上限约束）。</summary>
        public static int RoundTripsPerCrewSet(string aircraftType, int depMin, int blockMin, int turnMin)
        {
            int report = depMin - ReportBeforeMinutes;
            var budget = BudgetOf(aircraftType, report, blockMin, 2);
            int dutyLen = ReportBeforeMinutes + blockMin * 2 + turnMin;   // 报到 → 回场
            if (dutyLen <= 0) return 1;
            int n = budget.limit / dutyLen;
            return n < 1 ? 1 : n;
        }

        /// <summary>
        /// 一次值勤的**客舱**人数（乘务员）—— 按座位配员，与值勤时长无关。
        ///
        /// 依据（民航通用配员下限）：每 <see cref="HrEconomics.SeatsPerCabinCrewUnit"/> 座配 1 名乘务员，
        /// 向上取整、下限 1 名。
        ///
        /// ★座级必须与**编制**同一口径：这里读 <see cref="EconomyBalanceParams.DefaultSeatsFor"/> ——
        /// 也就是 <see cref="GameState.GetFleetStaffing"/> 推客舱编制时用的那一个座级口径。
        /// **不可**改用 <see cref="CapacityEconomics.SeatsOfType"/>（机型目录的 `maxPassengers`）：
        /// 两者对同一机型给出的数并不相同（A330-300 目录 300 座、本作座级口径 290 座；
        /// 且新建 <see cref="Aircraft"/> 的 `economySeats` 默认 180，`TotalSeats` 就取 180 而不是目录值），
        /// 于是「每班配员 × 4 组 + 备份」这条编制闭环会在两个座级之间悄悄断开 ——
        /// 编制 18 名而 4 组配员要 24 名，满编机队也飞不出每日两段。
        /// 改口径前先改 <see cref="GameState.GetFleetStaffing"/>，两处必须同源。
        ///
        /// ★2026-10-14 第十三次问询（CEO：「机组是包括乘务人员吗？」）：
        /// 现实口径里「机组」＝ 飞行机组（驾驶舱）+ 客舱乘务组，而本类原先只算驾驶舱。
        /// 按 CEO 裁决把客舱一并纳入排班闸门（见 <see cref="CrewCapacityLogic"/>）：
        /// 组数两岗共用（<see cref="CrewSetsForDaily"/>），每组人数驾驶舱看值勤、客舱看座位。
        /// </summary>
        public static int CabinPerDuty(string aircraftType)
        {
            int seats = EconomyBalanceParams.DefaultSeatsFor(aircraftType);
            int n = (int)Math.Ceiling(seats / (float)HrEconomics.SeatsPerCabinCrewUnit);
            return n < 1 ? 1 : n;
        }

        /// <summary>
        /// 一条线「每日 N 段」的机组需求。**组数两岗共用，人数两岗各算各的** ——
        /// 驾驶舱 <see cref="PilotsPerDuty"/>（3 / 4 人制）、客舱 <see cref="CabinPerDuty"/>（按座位）。
        ///
        /// 做成结构体而非一串 out 参数，是因为闸门 / 呈报 / 答复件 / 缺口明细都要同时拿这两岗的数，
        /// 一路 out 下去会让调用点变成「六个下划线」而看不出哪一岗在哪。
        /// </summary>
        public struct CrewDemand
        {
            /// <summary>机组组数（轮转班底数）：驾驶舱与客舱共用，见 <see cref="CrewSetsForDaily"/>。</summary>
            public int sets;
            /// <summary>驾驶舱人数 = 组数 × <see cref="PilotsPerDuty"/>（含长程增援）。</summary>
            public int pilots;
            /// <summary>客舱人数 = 组数 × <see cref="CabinPerDuty"/>。</summary>
            public int cabin;
            /// <summary>往返形状（换组 / 同组 / 排不出），文案与闸门共用。</summary>
            public RoundTripShape shape;
        }

        /// <summary>本线「每天 N 段」的机组需求（口径同 <see cref="CrewSetsForDaily"/>）。0 段 = 全 0。</summary>
        public static CrewDemand CrewDemandForDaily(string aircraftType, int depMin, int blockMin, int turnMin,
            RoundTripShape shape, int tripsPerDay)
        {
            var d = new CrewDemand { shape = shape };
            if (tripsPerDay <= 0) return d;
            d.sets = CrewSetsForDaily(tripsPerDay, shape, aircraftType, depMin, blockMin, turnMin);
            d.pilots = d.sets * PilotsPerDuty(blockMin);
            d.cabin = d.sets * CabinPerDuty(aircraftType);
            return d;
        }

        /// <summary>
        /// 按航线取机组口径（供运力呈报与运营部答复件共用 —— 同一件事只能有一套算术）。
        /// </summary>
        public static CrewDemand RouteCrewNeed(RouteData route, int tripsPerDay)
        {
            if (route == null || tripsPerDay <= 0) return new CrewDemand { shape = RoundTripShape.SingleDuty };
            int block = BlockOf(route);
            int turn = TurnOf(route);
            int depMin = LiveClockLogic.ParseMinutes(route.depTime);
            var shape = DutyShapeOf(route.aircraftType, depMin, block, turn, out _, out _);
            return CrewDemandForDaily(route.aircraftType, depMin, block, turn, shape, tripsPerDay);
        }

        /// <summary>一次值勤的预算（上限 + 航段上限 + 口径说明）。</summary>
        public struct DutyBudget
        {
            public int limit;       // 值勤上限（分钟，已含报到修正）
            public int sectorCap;   // 航段数上限
            public bool augmented;  // 是否宽体增援口径
            public string note;     // 呈报用的口径说明
        }

        /// <summary>
        /// 值勤预算。窄体走基础分级表；宽体走增援绝对上限（非「基础 + 加成」）。
        /// sectors 传「本次值勤完成后的航段数」——存在循环依赖，故排班侧用贪心 + 复检。
        /// </summary>
        public static DutyBudget BudgetOf(string aircraftType, int reportMin, int longestBlockMin, int sectors)
        {
            var b = new DutyBudget { sectorCap = int.MaxValue };
            int adj = ReportAdjust(reportMin);

            if (AirlineCEO.Data.AircraftCatalog.IsWidebody(aircraftType))
            {
                int pilots = PilotsPerDuty(longestBlockMin);      // 超长程加第 4 名机组
                int restClass = AirlineCEO.Data.AircraftCatalog.RestClassOf(aircraftType);
                int cap = AirlineCEO.Data.AircraftCatalog.AugmentedLimit(restClass, pilots);
                // EASA CS FTL.1.205(d)：含单段 > 9h 连续飞行且 ≤ 2 段时可再 +1h
                if (longestBlockMin > 9 * 60 && sectors <= 2) cap += 60;

                int needRest = AirlineCEO.Data.AircraftCatalog.MinInflightRestMinutes(restClass, cap);
                int haveRest = AirlineCEO.Data.AircraftCatalog.AvailableInflightRestMinutes(longestBlockMin);
                if (!AirlineCEO.Data.AircraftCatalog.InflightRestSatisfied(restClass, cap, longestBlockMin))
                {
                    int basic = DutyLimitBySectors(sectors);
                    b.limit = basic + adj;
                    b.note = $"宽体增援未生效（空中休息需 {needRest} 分，本段可休息 {haveRest} 分）→ {sectors} 航段 ≤{basic / 60f:0.##}h";
                    return b;
                }

                b.limit = cap + adj;
                b.sectorCap = AugmentedSectorCap;
                b.augmented = true;
                b.note = $"宽体增援 {pilots} 人制 {AirlineCEO.Data.AircraftCatalog.RestClassLabel(restClass)}"
                         + $" ≤{cap / 60f:0.##}h（空中休息 ≥{needRest} 分，本段可休息 {haveRest} 分）";
            }
            else
            {
                int cap = DutyLimitBySectors(sectors);
                b.limit = cap + adj;
                b.note = $"{sectors} 航段 ≤{cap / 60f:0.##}h";
            }
            return b;
        }

        // ================= 一次往返的**机组值勤结构**（外站换组） =================
        //
        // 背景（2026-10-14 第九次报障，CEO 问「过站时间包括机乘人员的休息时间吗？」）：
        // **不包括**。过站公式（`OpsEconomics.EstimateTurnMinutes`）只有地面作业 ——
        // 50 分基准 + 缺勤地勤 ×12 分，没有任何休息项；在值勤口径里过站是**值勤时间**
        // （`BudgetOf` 把「报到 → 最后落地」当一个连续值勤期算）。
        //
        // 于是洲际线（本作最长的 FRA–ATL 7,409 km：06:30 出发 → 15:13 落 ATL → 过站 50 分
        // → 16:03 起飞 → 次日 00:46 回 FRA，往返 18h16m）暴露出一处硬矛盾：
        //   同一组机组连飞 = 报到 05:30 → 回场 00:46+1 = **19.3h**，
        //   而 A330-300 的上限是宽体增援 3 人制 Class 2 = 15h，报到 05:30 再 −30 分 → **14.5h**。
        // 旧版把它记成「一个 19.3h 的值勤期」，飞机详情因此印出「实耗 19.3h（… ≤15h）｜剩余 −4.8h」
        // 这种自相矛盾的行 —— 判据那边其实早已承认装不下（走的是「外站过夜」分支），
        // 只有记账这一半没跟上。
        //
        // 真航司的做法是**外站换组**：飞机照样 50 分过站当天回，但回程由**对方站驻站机组**执飞，
        // 去程机组在对方站休息、次日接别的班回。于是同一趟往返是**两个值勤期**：
        //   值勤1（去程组）：报到 05:30 → 落 ATL 15:13 = 9.7h ≤ 14.5h ✓
        //   值勤2（驻站回程组）：报到 15:03 → 落 FRA 00:46+1 = 9.7h ≤ 14.5h ✓
        // 两段之间没有任何休息余量被计入（换的是人，不是等休息）—— 这正是「过站不含休息」的含义。

        /// <summary>一次往返的机组值勤结构。</summary>
        public enum RoundTripShape
        {
            /// <summary>一个值勤期装得下当天往返：同一组机组去、等过站、回。</summary>
            SingleDuty = 0,
            /// <summary>装不下，但**每一段**都装得下 → 拆两个值勤期：回程由对方站驻站机组执飞。</summary>
            CrewSwap = 1,
            /// <summary>连单段都超上限（真·超远程）：谁都飞不了。</summary>
            Infeasible = 2
        }

        /// <summary>一个机组值勤切片：**一段（1 个航段）**的报到 → 落地，附该段适用的值勤预算。</summary>
        public struct DutySlice
        {
            public int report;    // 报到时刻（分钟；< 0 = 前一日）
            public int end;       // 落地时刻（分钟；> 1440 = 次日）
            public int sectors;   // 航段数（单切片恒为 1；同组往返为 2）
            public int limit;     // 该切片适用的值勤上限（分钟）
            public string role;   // 文案：同组往返 / 去程组 / 驻站回程组
            public string station; // 报到所在机场（机组驻地）
            public string note;   // 值勤口径说明
            public int Length => end - report;
            public int Remaining => limit - Length;
        }

        /// <summary>
        /// 判一次往返的机组值勤结构（**唯一权威**）：`CanTake` 的放行、飞机详情的记账、
        /// 邮件里的日循环说明全部走这里，避免「判据说装不下、账上却记成一个值勤期」的漂移。
        /// 传入的都是纯数值，故离线模拟器也能用。
        /// </summary>
        public static RoundTripShape DutyShapeOf(string aircraftType, int depMin, int blockMin, int turnMin,
            out DutySlice outbound, out DutySlice inbound)
        {
            inbound = default;
            int outArr = depMin + blockMin;
            int retDep = outArr + turnMin;
            int retArr = retDep + blockMin;

            // ① 同组往返：报到 → 回场装进「2 航段」上限
            int mergedReport = depMin - ReportBeforeMinutes;
            var merged = BudgetOf(aircraftType, mergedReport, blockMin, 2);
            outbound = new DutySlice
            {
                report = mergedReport, end = retArr, sectors = 2, limit = merged.limit,
                role = "同组往返", station = "", note = merged.note
            };
            if (retArr - mergedReport <= merged.limit) return RoundTripShape.SingleDuty;

            // ② 每段各自成立 → 外站换组（去程组 / 驻站回程组）
            int r1 = depMin - ReportBeforeMinutes;
            var b1 = BudgetOf(aircraftType, r1, blockMin, 1);
            int r2 = retDep - ReportBeforeMinutes;
            var b2 = BudgetOf(aircraftType, r2, blockMin, 1);

            outbound = new DutySlice
            {
                report = r1, end = outArr, sectors = 1, limit = b1.limit,
                role = "去程组", station = "", note = b1.note
            };
            inbound = new DutySlice
            {
                report = r2, end = retArr, sectors = 1, limit = b2.limit,
                role = "驻站回程组", station = "", note = b2.note
            };
            if (outbound.Length > b1.limit || inbound.Length > b2.limit) return RoundTripShape.Infeasible;
            return RoundTripShape.CrewSwap;
        }

        /// <summary>同上，按机上窗口判（供只拿到 `TurnWindow` 的显示路径使用）。</summary>
        public static RoundTripShape DutyShapeOf(string aircraftType, TurnWindow w,
            out DutySlice outbound, out DutySlice inbound)
        {
            var shape = DutyShapeOf(aircraftType, w.start, w.block, w.turn, out outbound, out inbound);
            outbound.station = w.origin;
            if (shape == RoundTripShape.CrewSwap) inbound.station = w.destination;
            return shape;
        }

        /// <summary>
        /// 外站换组的口径说明（呈报 / 邮件 / 答复件复用）。非换组结构返回空串，调用方据此决定写不写这段。
        /// </summary>
        public static string CrewSwapNote(RoundTripShape shape, string destination, string depTime)
            => shape != RoundTripShape.CrewSwap ? "" :
               $"{depTime} 去程一组机组、回程由 {destination} 驻站机组执飞（外站换组）："
               + "当天往返装不进一个值勤期，故拆成两个值勤期，各自在上限内；"
               + "过站时间只含地面作业，不含机组休息（换的是机组，不是等机组休息）。";

        public static string RotationGapLabel() => RotationGapMinutes + " 分钟";

        /// <summary>一次往返占用的时间窗（当天分钟；end 可 &gt;1440 表示跨夜返场）。</summary>
        public struct TurnWindow
        {
            public int start;     // 去程起飞
            public int end;       // 回程落地
            public int block;     // 单程轮挡（分钟）
            public int turn;      // 过站（分钟）
            public string routeId;
            public string origin; // 基地（去程出发地，回程落地回此处）
            /// <summary>对方站（过站与**驻站回程组**报到之处）—— 外站换组的文案要用它。</summary>
            public string destination;
        }

        /// <summary>机队轮转占用表（按机号索引一天的往返窗口）。</summary>
        public class RotationPlan
        {
            readonly Dictionary<string, List<TurnWindow>> _byAircraft =
                new Dictionary<string, List<TurnWindow>>(StringComparer.OrdinalIgnoreCase);

            public List<TurnWindow> Of(string reg)
            {
                if (string.IsNullOrEmpty(reg)) return new List<TurnWindow>();
                if (_byAircraft.TryGetValue(reg, out var l) && l != null) return l;
                return new List<TurnWindow>();
            }

            public int TurnCount(string reg) => Of(reg).Count;

            public void Assign(string reg, TurnWindow w)
            {
                if (string.IsNullOrEmpty(reg)) return;
                if (!_byAircraft.TryGetValue(reg, out var l) || l == null)
                {
                    l = new List<TurnWindow>();
                    _byAircraft[reg] = l;
                }
                l.Add(w);
            }

            public void Clear() => _byAircraft.Clear();

            public List<string> Registrations()
            {
                var list = new List<string>();
                foreach (var kv in _byAircraft) list.Add(kv.Key);
                list.Sort(StringComparer.OrdinalIgnoreCase);
                return list;
            }

            /// <summary>该机在指定基地已有的往返段数（枢纽轮转连贯性）。</summary>
            public int SameOriginCount(string reg, string origin)
            {
                if (string.IsNullOrEmpty(origin)) return 0;
                var l = Of(reg);
                int n = 0;
                for (int i = 0; i < l.Count; i++)
                    if (string.Equals(l[i].origin, origin, StringComparison.OrdinalIgnoreCase)) n++;
                return n;
            }

            /// <summary>该机在其它基地的往返段数（异基地串飞权重更低）。</summary>
            public int ForeignOriginCount(string reg, string origin) => Of(reg).Count - SameOriginCount(reg, origin);

            /// <summary>该机当日最后一个回程落地时刻（当天分钟；可 &gt;1440 表示跨夜）。无任务返回 int.MinValue。</summary>
            public int DutyEnd(string reg)
            {
                var l = Of(reg);
                int last = int.MinValue;
                for (int i = 0; i < l.Count; i++) if (l[i].end > last) last = l[i].end;
                return last;
            }

            /// <summary>该机当日首个去程起飞时刻。无任务返回 int.MinValue。</summary>
            public int FirstDeparture(string reg)
            {
                var l = Of(reg);
                int first = int.MaxValue;
                for (int i = 0; i < l.Count; i++) if (l[i].start < first) first = l[i].start;
                return first == int.MaxValue ? int.MinValue : first;
            }

            /// <summary>该机的报到时刻 = 首个去程起飞 − 报到提前量。无任务返回 int.MinValue。</summary>
            public int ReportAt(string reg)
            {
                int first = FirstDeparture(reg);
                return first == int.MinValue ? int.MinValue : first - ReportBeforeMinutes;
            }

            /// <summary>该机本段任务的最长轮挡（用于增援机组人数判定）。</summary>
            public int LongestBlock(string reg)
            {
                var l = Of(reg);
                int best = 0;
                for (int i = 0; i < l.Count; i++) if (l[i].block > best) best = l[i].block;
                return best;
            }

            /// <summary>
            /// 该机当日可再次起飞的最早时刻 = 最后一个回程落地 + 过站 + 轮转裕度。
            /// 当天尚无任务返回 int.MinValue（整天可用）。
            /// </summary>
            public int ReadyAt(string reg)
            {
                var l = Of(reg);
                if (l.Count == 0) return int.MinValue;
                int last = int.MinValue, turn = 0;
                for (int i = 0; i < l.Count; i++)
                {
                    if (l[i].end > last) { last = l[i].end; turn = l[i].turn; }
                }
                return last + turn + RotationGapMinutes;
            }

            // —— 值勤期（FDP）切分：相邻往返间隔 ≥ 最低休息即视为新的值勤期 ——

            /// <summary>把该机的往返段按「最低休息 10h」切分为若干值勤期（按起飞时刻升序）。</summary>
            public List<List<TurnWindow>> DutiesOf(string reg)
            {
                var sorted = new List<TurnWindow>(Of(reg));
                sorted.Sort((x, y) => x.start.CompareTo(y.start));
                var duties = new List<List<TurnWindow>>();
                List<TurnWindow> cur = null;
                int curEnd = int.MinValue;
                for (int i = 0; i < sorted.Count; i++)
                {
                    var w = sorted[i];
                    if (cur == null || w.start - curEnd >= MinRestMinutes)
                    {
                        cur = new List<TurnWindow>();
                        duties.Add(cur);
                    }
                    cur.Add(w);
                    if (w.end > curEnd) curEnd = w.end;
                }
                return duties;
            }

            /// <summary>
            /// 新段若在 start 起飞，会并入哪个在役值勤期；间隔 ≥ 最低休息则应开新值勤期，返回 null。
            /// 只在末尾追加，故仅需检查最后一个值勤期。
            /// </summary>
            public List<TurnWindow> DutyJoining(string reg, int start)
            {
                var duties = DutiesOf(reg);
                if (duties.Count == 0) return null;
                var last = duties[duties.Count - 1];
                int end = DutyLastEnd(last);
                if (start >= end && start - end < MinRestMinutes) return last;
                return null;
            }

            public static int DutyFirstStart(List<TurnWindow> duty)
            {
                int first = int.MaxValue;
                for (int i = 0; i < duty.Count; i++) if (duty[i].start < first) first = duty[i].start;
                return first == int.MaxValue ? int.MinValue : first;
            }

            public static int DutyLastEnd(List<TurnWindow> duty)
            {
                int last = int.MinValue;
                for (int i = 0; i < duty.Count; i++) if (duty[i].end > last) last = duty[i].end;
                return last;
            }

            public static int DutyLongestBlock(List<TurnWindow> duty)
            {
                int best = 0;
                for (int i = 0; i < duty.Count; i++) if (duty[i].block > best) best = duty[i].block;
                return best;
            }

            /// <summary>值勤期汇总（供 n10 飞机详情与呈报展示）。</summary>
            ///
            /// ★外站换组（2026-10-14）：一段往返若**装不进一个值勤期**（洲际线），它不是一个
            /// 「19.3h 的值勤期」—— 而是**两个值勤期**：去程组（报到 → 落对方站）+ 驻站回程组
            /// （对方站报到 → 落回本场）。旧版把两者记成一个值勤期，于是屏幕上出现
            /// 「实耗 19.3h（… ≤15h）｜剩余 −4.8h」这种自相矛盾的行（第九次报障）。
            public string DutySummary(string reg, string aircraftType = null)
            {
                var duties = DutiesOf(reg);
                if (duties.Count == 0) return "当日无值勤";
                var sb = new System.Text.StringBuilder();
                int no = 0;
                for (int i = 0; i < duties.Count; i++)
                {
                    var d = duties[i];

                    // 组里有换组往返：逐窗口印（同一时刻的重复条目 = 同一日循环，只印一次）
                    if (HasCrewSwap(d, aircraftType))
                    {
                        var seen = new System.Collections.Generic.HashSet<int>();
                        for (int k = 0; k < d.Count; k++)
                        {
                            var w = d[k];
                            if (!seen.Add(w.start)) continue;
                            var shape = DutyShapeOf(aircraftType, w, out var outb, out var inb);
                            if (shape == RoundTripShape.CrewSwap)
                            {
                                Append(sb, SliceLine(++no, outb));
                                Append(sb, SliceLine(++no, inb));
                                continue;
                            }
                            // 混装（本机同组还接了普通往返）：如实按一个值勤期印
                            int rep = w.start - ReportBeforeMinutes;
                            var b = BudgetOf(aircraftType, rep, w.block, 2);
                            Append(sb, $"{Tag(++no)} {FormatMinute(rep)} 报到 → {FormatMinute(w.end)} 落地"
                                     + $"，1 段往返 / 2 航段，实耗 {(w.end - rep) / 60f:0.#}h"
                                     + $"，剩余 {(b.limit - (w.end - rep)) / 60f:0.#}h（{b.note}）");
                        }
                        continue;
                    }

                    int report = DutyFirstStart(d) - ReportBeforeMinutes;
                    int end = DutyLastEnd(d);
                    int sectors = d.Count * 2;
                    var budget = BudgetOf(aircraftType, report, DutyLongestBlock(d), sectors);
                    Append(sb, $"{Tag(++no)} {FormatMinute(report)} 报到 → {FormatMinute(end)} 落地"
                             + $"，{d.Count} 段往返 / {sectors} 航段，实耗 {(end - report) / 60f:0.#}h"
                             + $"，剩余 {(budget.limit - (end - report)) / 60f:0.#}h（{budget.note}）");
                }
                return sb.ToString();
            }

            static bool HasCrewSwap(List<TurnWindow> d, string aircraftType)
            {
                for (int i = 0; i < d.Count; i++)
                    if (DutyShapeOf(aircraftType, d[i], out _, out _) == RoundTripShape.CrewSwap) return true;
                return false;
            }

            /// <summary>一个机组值勤切片一行（外站换组的两段之一）。</summary>
            static string SliceLine(int no, DutySlice s)
            {
                string station = string.IsNullOrEmpty(s.station) ? "" : $"（{s.station} 机组）";
                return $"{Tag(no)} {s.role}{station}　{FormatMinute(s.report)} 报到 → {FormatMinute(s.end)} 落地"
                     + $"，{s.sectors} 航段，实耗 {s.Length / 60f:0.#}h"
                     + $"，剩余 {s.Remaining / 60f:0.#}h（{s.note}）";
            }

            static string Tag(int no) => $"值勤{no}";

            static void Append(System.Text.StringBuilder sb, string line)
            {
                if (sb.Length > 0) sb.Append(" ｜ ");
                sb.Append(line);
            }

            /// <summary>从已生效航线装载占用（不含 exclude，便于整体重排）。</summary>
            public void LoadFrom(IList<RouteData> routes, RouteData exclude = null)
            {
                if (routes == null) return;
                for (int i = 0; i < routes.Count; i++)
                {
                    var r = routes[i];
                    if (r == null || r == exclude) continue;
                    r.EnsureFlights();
                    for (int f = 0; f < r.flights.Count; f++)
                    {
                        var fl = r.flights[f];
                        if (fl == null || string.IsNullOrEmpty(fl.registration)) continue;
                        Assign(fl.registration, WindowOf(r, fl.depTime));
                    }
                }
            }
        }

        /// <summary>单程轮挡（分钟）。权威实现在 OpsRotationParams（纯 C#，供离线模拟共用）。</summary>
        public static int BlockMinutes(float distanceKm) => OpsRotationParams.BlockMinutes(distanceKm);

        /// <summary>班期跨度与物理轮挡的绝对容差（分钟；另按轮挡 25% 放宽）。权威值在 OpsRotationParams。</summary>
        public const int ScheduleSpanToleranceMinutes = OpsRotationParams.ScheduleSpanToleranceMinutes;

        /// <summary>班期跨度与物理轮挡的相对容差。权威值在 OpsRotationParams。</summary>
        public const float ScheduleSpanToleranceRatio = OpsRotationParams.ScheduleSpanToleranceRatio;

        /// <summary>
        /// 单程轮挡（分钟）：以物理估算（按航程推算）为准，航线自带的「起飞→到达」只有落在
        /// 容差内才视为真实班期。
        /// 例：历史占位时刻恒为 08:00→10:30（150 分），1100 km 级航线真实轮挡仅 76 分，
        /// 若采信占位跨度，一段往返会被算成 6.0h（实际 3.4h），直接吃掉后续需求波——
        /// 这正是「PEK–SHA 一天只能排一段往返、B-2000 加不上第二段」的成因。
        /// </summary>
        public static int LegBlockMinutes(RouteData route)
        {
            int phys = BlockMinutes(route != null ? route.distanceKm : 1000f);
            if (route == null) return phys;
            if (string.IsNullOrEmpty(route.depTime) || string.IsNullOrEmpty(route.arrTime)) return phys;

            int span = LiveClockLogic.BlockMinutes(route.depTime, route.arrTime, route.distanceKm);
            if (span <= 0 || span >= MinutesPerDay / 2) return phys;      // 跨天/异常跨度不采信
            int tol = ScheduleSpanToleranceMinutes;
            int rel = (int)Math.Round(phys * ScheduleSpanToleranceRatio);
            if (rel > tol) tol = rel;
            // 只拒「明显偏长」（占位/历史脏数据）；偏短一律以物理轮挡为准，不缩小既有排班口径。
            return span > phys + tol ? phys : Math.Max(phys, span);
        }

        /// <summary>一次往返窗口：去程 + 过站 + 回程（均回到出发地）。</summary>
        public static TurnWindow WindowOf(RouteData route, string dep)
        {
            string d = string.IsNullOrEmpty(dep) ? (route != null ? route.depTime : "08:00") : dep;
            int start = LiveClockLogic.ParseMinutes(d);
            int block = LegBlockMinutes(route);
            int turn = LiveClockLogic.TurnMinutes(
                route != null ? route.aircraftType : "",
                route != null ? route.groundStaffAssigned : OpsEconomics.RecommendedGroundStaff(""));
            return new TurnWindow
            {
                start = start,
                end = start + block * 2 + turn,
                block = block,
                turn = turn,
                routeId = route != null ? route.routeId : "",
                origin = route != null ? route.origin : "",
                destination = route != null ? route.destination : ""
            };
        }

        /// <summary>本航线单程轮挡（分钟）：与轮转窗口同口径，保证「展示到达时刻」与「排班窗口」一致。</summary>
        public static int BlockOf(RouteData route)
            => LegBlockMinutes(route);

        /// <summary>本航线过站时间（分钟）。</summary>
        public static int TurnOf(RouteData route)
            => route == null ? 50
               : LiveClockLogic.TurnMinutes(
                     route.aircraftType,
                     route.groundStaffAssigned > 0 ? route.groundStaffAssigned : OpsEconomics.RecommendedGroundStaff(route.aircraftType));

        /// <summary>
        /// 宵禁校验：一次往返涉及 4 个起降时刻（去程起飞 / 去程落地 / 回程起飞 / 回程落地），
        /// 逐个核对相应机场的宵禁窗口。命中则返回 true 并说明原因。
        /// </summary>
        public static bool CurfewBlocked(RouteData route, int dep, out string why)
        {
            why = "";
            if (route == null) return false;
            int block = BlockOf(route);
            int turn = TurnOf(route);
            return CurfewAt(route.origin, dep, "去程起飞", out why)
                || CurfewAt(route.destination, dep + block, "去程落地", out why)
                || CurfewAt(route.destination, dep + block + turn, "回程起飞", out why)
                || CurfewAt(route.origin, dep + block * 2 + turn, "回程落地", out why);
        }

        static bool CurfewAt(string airport, int minutes, string what, out string why)
        {
            why = "";
            if (string.IsNullOrEmpty(airport)) return false;
            if (!AirlineCEO.Data.AirportCatalog.InCurfew(airport, minutes)) return false;
            string label = AirlineCEO.Data.AirportCatalog.CurfewLabel(airport);
            why = $"{airport} {what} {FormatMinute(minutes)} 落在{label}窗口内";
            return true;
        }

        /// <summary>
        /// 长程（外站换组）的宵禁校验：去程按当日严格核；回程按「≥ 最低休息后，取首个合法时刻返场」核。
        /// 若按整段往返同一天核，会把回程的时刻错算到当日，产生假宵禁冲突。
        ///
        /// ★已知偏差（2026-10-14，**刻意保留**，见 §17.16）：换组口径下回程是**当天**（如 FRA–ATL 的 16:03），
        /// 而本函数把它放到「≥10h 后」＝次日。原因是 FRA–ATL 06:30 出发的回程落地 00:46 正落在 FRA 的
        /// 23:00–06:00 宵禁窗口内 —— 一旦改成按当天时刻核，本线整条班期会被判不可排。
        /// 改它等于改玩法（班期得挪到别的时段），故本轮只记账、不动它，并把偏差写在文档里。
        /// </summary>
        public static bool CurfewBlockedLongHaul(RouteData route, int dep, out string why)
        {
            why = "";
            if (route == null) return false;
            int block = BlockOf(route);
            int turn = TurnOf(route);
            int arrDest = dep + block;

            // 去程：出发地起飞 + 目的地落地
            if (CurfewAt(route.origin, dep, "去程起飞", out why)) return true;
            if (CurfewAt(route.destination, arrDest, "去程落地", out why)) return true;

            // 回程：外站休息（≥ 最低休息）后，宵禁只是把航班推迟到窗口外（而非取消），
            // 故取首个「离站 + 返场落地」都合法的时刻；36h 内无合法时刻才算不可排。
            int restMin = Math.Max(turn + RotationGapMinutes, MinRestMinutes);
            int backDep = arrDest + restMin;
            return !TrySnapLeg(route.destination, route.origin, backDep, block, LongHaulSnapHorizonMinutes, out _, out why);
        }

        /// <summary>长程回程的合法离站时刻回溯上限（36h），超出即判为无合法时刻。</summary>
        public const int LongHaulSnapHorizonMinutes = 36 * 60;

        /// <summary>
        /// 从 fromMinutes 起找首个「离站机场起飞 + 到达机场落地」都不落宵禁的时刻。
        /// 用于长程回程：宵禁只是把航班推迟到窗口外，而不是取消航班。
        /// </summary>
        public static bool TrySnapLeg(string depAirport, string arrAirport, int fromMinutes, int block,
            int horizonMinutes, out int depOut, out string why)
        {
            depOut = fromMinutes;
            why = "";
            for (int t = fromMinutes; t <= fromMinutes + horizonMinutes; t++)
            {
                if (AirlineCEO.Data.AirportCatalog.InCurfew(depAirport, t)) continue;
                if (AirlineCEO.Data.AirportCatalog.InCurfew(arrAirport, t + block)) continue;
                depOut = t;
                return true;
            }
            why = $"{depAirport} 起飞 / {arrAirport} 落地在 {horizonMinutes / 60}h 内均落在宵禁窗口，无合法时刻";
            return false;
        }

        /// <summary>当天分钟 → HH:mm（跨夜加 +1）。</summary>
        public static string FormatMinute(int minutes)
        {
            int m = ((minutes % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
            string hm = $"{m / 60:00}:{m % 60:00}";
            return minutes >= MinutesPerDay ? hm + "+1" : hm;
        }

        // ================= R4 跨夜班归属（双口径） =================
        // 「一天能飞几段」由报到时刻 + 值勤上限决定，与午夜无关。因此跨夜 ≠ 新的一天：
        //   · 值勤 / 机组（排班用）→ 按「值勤期起始日」（= 报到时刻所在日）归组
        //   · 航班量统计（weeklyFlights）→ 按「计划起飞时刻所在日」归组
        // 两者仅在「00:00–00:59 离港且报到在前一日 23:xx」时才分歧。
        //
        // 实现权威值在 OpsRotationParams（纯 C#，RouteData 与 tools/balance 共用），
        // 此处转发以保持既有调用点（CapacityDirectiveLogic / SeasonReviewLogic 等）不变。

        /// <summary>分钟轴上的「日偏移」：0 = 当日，1 = 次日，-1 = 前一日。</summary>
        public static int DayOf(int minutes) => OpsRotationParams.DayOf(minutes);

        /// <summary>值勤日偏移（按报到时刻）—— 整个值勤期都归这一天。</summary>
        public static int DutyDayOf(int reportMin) => OpsRotationParams.DutyDayOf(reportMin);

        /// <summary>
        /// 把「本条值勤期内」的离港时刻放到以报到时刻为原点的连续轴上：
        /// 早于报到时刻的钟点（如报到 22:30、离港 00:30）自动接续到次日，而不是被午夜切回当天。
        /// </summary>
        public static int LegOnDutyAxis(int reportMin, int depMin)
            => OpsRotationParams.LegOnDutyAxis(reportMin, depMin);

        /// <summary>离港日偏移（按计划起飞时刻，相对同一值勤期的报到时刻）—— 航班量按这一天统计。</summary>
        public static int DepartureDayOf(int reportMin, int depMin) => OpsRotationParams.DepartureDayOf(reportMin, depMin);

        /// <summary>两套口径是否同属一天（跨夜班为 false）。</summary>
        public static bool SameAttributionDay(int reportMin, int depMin)
            => OpsRotationParams.SameAttributionDay(reportMin, depMin);

        /// <summary>本条值勤的报到时刻（并入既有值勤期则取该值勤期首段；否则以本段推算）。</summary>
        public static int ReportAt(RotationPlan plan, string reg, int depMin)
        {
            var duty = plan != null ? plan.DutyJoining(reg, depMin) : null;
            return duty == null ? depMin - ReportBeforeMinutes
                                : RotationPlan.DutyFirstStart(duty) - ReportBeforeMinutes;
        }

        /// <summary>双口径的可读说明，供单航线页与呈报邮件使用（R4 / R14：必须标明用的是哪一套）。</summary>
        public static string AttributionLabel(int reportMin, int depMin)
        {
            int dd = DutyDayOf(reportMin), pd = DepartureDayOf(reportMin, depMin);
            if (dd == pd) return "值勤日 = 离港日";
            return $"跨夜：值勤计入前 {pd - dd} 日（值勤日），航班量计入离港日";
        }

        /// <summary>两个往返窗口是否冲突（按 24 小时循环比较，含跨夜与轮转裕度）。</summary>
        public static bool Overlaps(TurnWindow a, TurnWindow b, int gap)
        {
            if (gap < 0) gap = 0;
            for (int k = -1; k <= 1; k++)
            {
                int s = b.start + k * MinutesPerDay;
                int e = b.end + k * MinutesPerDay;
                if (a.start - gap < e && s < a.end + gap) return true;
            }
            return false;
        }

        /// <summary>机型航程是否够（与 OpsScheduleLogic 同口径）。</summary>
        public static bool RangeOk(Aircraft a, RouteData route)
        {
            if (a == null || route == null) return false;
            return OpsScheduleLogic.RangeCovers(a.type, route);
        }

        /// <summary>该机当前是否可被排班（在役、非维修/退役、未投入过渡业务、航程够）。</summary>
        public static bool CanFly(GameState gs, Aircraft a, RouteData route, bool requireSameType = false)
        {
            if (a == null || !a.isActive) return false;
            if (a.status == AircraftStatuses.Maintenance || a.status == AircraftStatuses.Retired) return false;
            if (TransitionBusinessLogic.IsBusy(gs, a.registration)) return false;
            if (requireSameType && route != null && !string.IsNullOrEmpty(route.aircraftType)
                && !string.Equals(a.type, route.aircraftType, StringComparison.OrdinalIgnoreCase))
                return false;
            return RangeOk(a, route);
        }

        /// <summary>
        /// 该机能否承接「dep 出发的本次往返」：在役 / 航程 / 机型 / 时段不冲突 / 值勤预算不超 / 不落宵禁。
        /// why 回传具体否决原因（供缺口归因与单航线页如实告知）。
        /// </summary>
        public static bool CanTake(RouteData route, RotationPlan plan, Aircraft a, string dep,
            GameState gsRef, bool requireSameType, out string why)
            => CanTake(route, plan, a, dep, gsRef, requireSameType, out why, out _);

        /// <summary>同上，并回传否决原因的分类（见 <see cref="BlockKind"/>）。</summary>
        public static bool CanTake(RouteData route, RotationPlan plan, Aircraft a, string dep,
            GameState gsRef, bool requireSameType, out string why, out BlockKind kind)
        {
            why = "";
            kind = BlockKind.None;
            if (a == null || route == null || plan == null) { why = "参数缺失"; kind = BlockKind.Missing; return false; }

            var gs = gsRef ?? AirlineCEO.Systems.GameState.Instance;
            if (!a.isActive) { why = "已停用"; kind = BlockKind.Status; return false; }
            if (a.status == AircraftStatuses.Maintenance) { why = "检修中"; kind = BlockKind.Status; return false; }
            if (a.status == AircraftStatuses.Retired) { why = $"已{a.ExitLabel}"; kind = BlockKind.Status; return false; }
            if (TransitionBusinessLogic.IsBusy(gs, a.registration)) { why = "已投入过渡业务或已预约机位（需先召回/取消预约）"; kind = BlockKind.Transition; return false; }
            if (requireSameType && !string.IsNullOrEmpty(route.aircraftType)
                && !string.Equals(a.type, route.aircraftType, StringComparison.OrdinalIgnoreCase))
            {
                why = $"机型不符（需 {route.aircraftType}，现为 {a.type}）";
                kind = BlockKind.TypeMismatch;
                return false;
            }
            if (!RangeOk(a, route))
            {
                float r = OpsScheduleLogic.RangeKm(a.type);
                why = r > 0f
                    ? $"航程不足（{r:F0} km＋减载余量 {OpsScheduleLogic.RangeToleranceKm:F0} km"
                      + $" < 本线 {OpsScheduleLogic.RequiredRangeKm(route):F0} km）"
                    : $"机型不在目录（{a.type}），航程无法核实";
                kind = BlockKind.Range;
                return false;
            }

            var want = WindowOf(route, dep);
            var windows = plan.Of(a.registration);
            for (int w = 0; w < windows.Count; w++)
            {
                if (!Overlaps(want, windows[w], RotationGapMinutes)) continue;
                bool sameRoute = string.Equals(windows[w].routeId, route.routeId, StringComparison.OrdinalIgnoreCase);
                string who = sameRoute
                    ? "本航线前后班次冲突"
                    : $"被 {OpsScheduleLogic.RouteTag(gs, windows[w].routeId)} 占用";
                why = $"时段冲突：{who}（已排 {FormatMinute(windows[w].start)}–{FormatMinute(windows[w].end)} 的往返）";
                kind = BlockKind.Conflict;
                return false;
            }

            // —— 值勤预算（FDP）：航段数 + 报到时刻 + 机型口径 ——
            //
            // 判据是**结果导向**的（2026-10-13 修，CEO 报障）：先按「当天往返」合并算一个值勤期；
            // 装不下、但**每段**装得下 → 拆成两个值勤期：回程由**对方站驻站机组**执飞
            // （**外站换组**，2026-10-14 定名 —— 过站仍是当天 50 分、飞机当天回，换的是机组）。
            // 旧文案叫「外站过夜」，容易被读成「飞机在对方站停一晚」，与时刻表不符。
            //
            // 判据与记账共用 `DutyShapeOf`（唯一权威），避免「这边说装不下、那边记成一个值勤期」。
            var duty = plan.DutyJoining(a.registration, want.start);   // 并入既有值勤期；null = 开新值勤期
            int reportAt = duty == null
                ? want.start - ReportBeforeMinutes
                : RotationPlan.DutyFirstStart(duty) - ReportBeforeMinutes;
            int sectors = (duty == null ? 0 : duty.Count * 2) + 2;      // 一段往返 = 2 航段
            int longest = Math.Max(duty == null ? 0 : RotationPlan.DutyLongestBlock(duty), want.block);
            int dutyEnd = duty == null ? want.end : Math.Max(RotationPlan.DutyLastEnd(duty), want.end);
            var budget = BudgetOf(a.type, reportAt, longest, sectors);

            if (sectors > budget.sectorCap)
            {
                why = $"增援航段上限：本次将达 {sectors} 航段 > {budget.sectorCap}（{budget.note}）";
                kind = BlockKind.SectorCap;
                return false;
            }
            int dutyLen = dutyEnd - reportAt;
            if (dutyLen > budget.limit)
            {
                var shape = DutyShapeOf(a.type, want.start, want.block, want.turn,
                    out var legSlice, out var backSlice);
                if (duty == null && shape == RoundTripShape.CrewSwap)
                {
                    // ★刻意保留的偏差（2026-10-14，见 §17.16 已知缺口）：换组口径下回程是**当天**
                    // 16:03，但宵禁仍按 `CurfewBlockedLongHaul` 核（它把回程放到「≥10h 后首个合法
                    // 时刻」＝次日）。原因：FRA–ATL 06:30 出发的回程落地 00:46 正落在 FRA 的
                    // 23:00–06:00 宵禁窗口内 —— 一旦改成按当天时刻核，本线整条班期会被判不可排。
                    // 这是**保留的模型近似**（改它等于改玩法：班期要挪到别的时段），不是疏忽。
                    if (CurfewBlockedLongHaul(route, want.start, out string curfewSwap))
                    {
                        why = curfewSwap;
                        kind = BlockKind.Curfew;
                        return false;
                    }
                    return true;    // 外站换组成立：去程组 + 驻站回程组，两段各自在上限内
                }

                var bad = legSlice.Length > legSlice.limit ? legSlice : backSlice;
                why = duty != null
                    ? $"值勤超限：本机当日已有值勤期（{FormatMinute(reportAt)} 报到），再接本次往返共 "
                      + $"{dutyLen / 60f:0.#}h > 上限 {budget.limit / 60f:0.##}h（{budget.note}；"
                      + $"本机当日已排班，不能改按外站换组）"
                    : $"值勤超限：{FormatMinute(reportAt)} 报到 → {FormatMinute(dutyEnd)} 落地 共 "
                      + $"{dutyLen / 60f:0.#}h > 上限 {budget.limit / 60f:0.##}h（{budget.note}），"
                      + $"且单段 {bad.Length / 60f:0.#}h 也超上限 {bad.limit / 60f:0.##}h（连外站换组也不成立）";
                kind = BlockKind.Duty;
                return false;
            }

            // —— 宵禁（去程起飞 / 去程落地 / 回程起飞 / 回程落地 四处）——
            if (CurfewBlocked(route, want.start, out string curfewWhy)) { why = curfewWhy; kind = BlockKind.Curfew; return false; }

            return true;
        }

        /// <summary>
        /// 为一个时段的班次选机：优先「同一基地已有往返」的飞机（枢纽日循环），
        /// 其次完全空闲的飞机；异基地串飞与超过期望段数会降权。
        /// </summary>
        public static Aircraft PickForSlot(RouteData route, IList<Aircraft> fleet, RotationPlan plan,
            string dep, bool requireSameType = false, GameState gsRef = null)
            => PickForSlot(route, fleet, plan, dep, requireSameType, gsRef, out _);

        /// <summary>同上，并回传未选中时最贴近的原因（供缺口说明）。</summary>
        public static Aircraft PickForSlot(RouteData route, IList<Aircraft> fleet, RotationPlan plan,
            string dep, bool requireSameType, GameState gsRef, out string rejectWhy)
        {
            rejectWhy = "";
            if (route == null || fleet == null || plan == null) { rejectWhy = "参数缺失"; return null; }
            try { return Pick(route, fleet, plan, dep, requireSameType, gsRef, out rejectWhy); }
            finally { if (string.IsNullOrEmpty(rejectWhy)) rejectWhy = "无可轮转运力"; }
        }

        static Aircraft Pick(RouteData route, IList<Aircraft> fleet, RotationPlan plan, string dep,
            bool requireSameType, GameState gsRef, out string rejectWhy)
        {
            rejectWhy = "";
            var gs = gsRef ?? AirlineCEO.Systems.GameState.Instance;
            Aircraft best = null;
            int bestScore = int.MinValue;
            string firstWhy = "";

            for (int i = 0; i < fleet.Count; i++)
            {
                var a = fleet[i];
                if (a == null) continue;
                if (!CanTake(route, plan, a, dep, gs, requireSameType, out string why))
                {
                    if (firstWhy.Length == 0) firstWhy = why;
                    continue;
                }

                int turns = plan.Of(a.registration).Count;
                int same = plan.SameOriginCount(a.registration, route.origin);
                int foreign = turns - same;
                int score = same * 5 - foreign * 3 - turns;
                if (turns == 0) score += 1;
                if (turns >= PreferredTurnsPerAircraft) score -= 8;
                if (score > bestScore) { bestScore = score; best = a; }
            }
            if (best == null) rejectWhy = firstWhy.Length > 0 ? firstWhy : "无可轮转运力";
            return best;
        }

        /// <summary>
        /// 按需求波窗口选机并**链式**给出起飞时刻：起飞 = max(该机可再次起飞时刻, 窗口开始)，
        /// 且不得晚于窗口结束；再受值勤预算与宵禁约束。
        /// 这是「飞机不再空等固定时刻槽」的关键：落回后只要有需求波窗口就立刻续飞。
        ///
        /// ★2026-10-15：新增 <paramref name="preferredDepMin"/>。时刻槽的身份是**具体时刻**
        /// （WASG 1.7.2d：不得故意在明显不同于获批时刻的时间运行），所以本航线在该波持有某条
        /// 时刻的槽时，班次就该落在那**一刻**上（该机来得及的话），而不是窗口内随便挑一个。
        /// </summary>
        public static Aircraft PickForWindow(RouteData route, IList<Aircraft> fleet, RotationPlan plan,
            int band, bool requireSameType, GameState gsRef, out int chosenDep, out string rejectWhy,
            int preferredDepMin = 0)
        {
            chosenDep = -1;
            rejectWhy = "";
            if (route == null || fleet == null || plan == null) { rejectWhy = "参数缺失"; return null; }
            var gs = gsRef ?? AirlineCEO.Systems.GameState.Instance;
            int winStart = ScheduleSlots.WindowStartOf(band);
            int winEnd = ScheduleSlots.WindowEndOf(band);

            Aircraft best = null;
            int bestScore = int.MinValue;
            int bestDep = -1;

            for (int i = 0; i < fleet.Count; i++)
            {
                var a = fleet[i];
                if (a == null) continue;

                int ready = plan.ReadyAt(a.registration);
                int dep = ready == int.MinValue ? winStart : Math.Max(ready, winStart);
                dep = RoundUpTo5(dep);
                // 有持仓时刻该机又来得及 → 钉在该时刻上
                if (preferredDepMin > 0 && dep <= preferredDepMin
                    && preferredDepMin >= winStart && preferredDepMin <= winEnd)
                    dep = preferredDepMin;
                if (dep > winEnd)
                {
                    if (string.IsNullOrEmpty(rejectWhy))
                        rejectWhy = $"该机回场过晚（最早 {FormatMinute(dep)} 才可起飞，晚于窗口 {FormatMinute(winEnd)}）";
                    continue;
                }

                if (!CanTake(route, plan, a, FormatMinute(dep), gs, requireSameType, out string why))
                {
                    if (string.IsNullOrEmpty(rejectWhy)) rejectWhy = why;
                    continue;
                }

                int turns = plan.Of(a.registration).Count;
                int same = plan.SameOriginCount(a.registration, route.origin);
                int foreign = turns - same;
                int score = same * 5 - foreign * 3 - turns;
                if (turns == 0) score += 1;
                if (turns >= PreferredTurnsPerAircraft) score -= 8;
                // 同分时优先「更早起飞」（提高利用率），避免无谓空等
                if (score > bestScore || (score == bestScore && best != null && dep < bestDep))
                {
                    bestScore = score; best = a; bestDep = dep;
                }
            }

            if (best == null)
            {
                if (string.IsNullOrEmpty(rejectWhy)) rejectWhy = "无可轮转运力";
                return null;
            }
            chosenDep = bestDep;
            return best;
        }

        /// <summary>向上取整到 5 分钟（排班时刻惯例）。</summary>
        public static int RoundUpTo5(int minutes)
        {
            int r = ((minutes % 5) + 5) % 5;
            return r == 0 ? minutes : minutes + (5 - r);
        }

        /// <summary>机队日循环统计（用于换季评审呈报与机队页展示）。</summary>
        public struct FleetRotationStats
        {
            public int aircraftTotal;      // 机队飞机数（可排班口径）
            public int aircraftUsed;       // 有往返任务的飞机数
            public int turnsTotal;         // 全部往返段数
            public int maxTurns;           // 单机最多往返段数
            public float avgTurns;         // 平均每架在用飞机的往返段数
            public string busiestReg;      // 最忙机号
            public int busiestTurns;
        }

        public static FleetRotationStats StatsOf(IList<Aircraft> fleet, RotationPlan plan)
        {
            var st = new FleetRotationStats();
            if (fleet != null) st.aircraftTotal = fleet.Count;
            if (plan == null) return st;
            var regs = plan.Registrations();
            for (int i = 0; i < regs.Count; i++)
            {
                int n = plan.TurnCount(regs[i]);
                st.turnsTotal += n;
                if (n > 0) st.aircraftUsed++;
                if (n > st.maxTurns) st.maxTurns = n;
                if (n > st.busiestTurns) { st.busiestTurns = n; st.busiestReg = regs[i]; }
            }
            st.avgTurns = st.aircraftUsed > 0 ? (float)st.turnsTotal / st.aircraftUsed : 0f;
            return st;
        }

        /// <summary>
        /// 机队整体重排：按枢纽日循环把各航线班次分配回飞机（apply=false 时仅试算，不写回）。
        /// 保守约束：机型必须与航线现用机型一致；无法轮转的班次保持原机号，绝不丢班。
        /// </summary>
        public static int ReplanFleet(GameState gs, IList<RouteData> routes, IList<Aircraft> fleet, bool apply,
            out FleetRotationStats stats, out List<string> freed)
        {
            stats = new FleetRotationStats();
            freed = new List<string>();
            if (routes == null || fleet == null) return 0;

            var before = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var plan = new RotationPlan();
            string busiest = "";

            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (r == null) continue;
                r.EnsureFlights();
                if (r.flights == null || r.flights.Count == 0) continue;

                // 保留原有班次（时刻/班期/收益不动），只重排机号；按起飞时刻先后编排
                var ordered = new List<RouteFlight>(r.flights);
                ordered.Sort((a, b) => string.CompareOrdinal(a?.depTime ?? "", b?.depTime ?? ""));

                var newRegs = new string[ordered.Count];
                for (int k = 0; k < ordered.Count; k++)
                {
                    var f = ordered[k];
                    if (f == null) continue;
                    if (!string.IsNullOrEmpty(f.registration)) before.Add(f.registration);

                    var ac = PickForSlot(r, fleet, plan, f.depTime, requireSameType: true, gsRef: gs);
                    if (ac == null)
                        ac = PickForSlot(r, fleet, plan, f.depTime, requireSameType: false, gsRef: gs);

                    if (ac != null)
                    {
                        newRegs[k] = ac.registration;
                        plan.Assign(ac.registration, WindowOf(r, f.depTime));
                    }
                    else
                    {
                        // 无可轮转运力：保持原机号，保证不断班
                        newRegs[k] = f.registration;
                        if (!string.IsNullOrEmpty(f.registration))
                            plan.Assign(f.registration, WindowOf(r, f.depTime));
                    }
                }

                if (apply)
                {
                    for (int k = 0; k < ordered.Count; k++)
                        if (ordered[k] != null && !string.IsNullOrEmpty(newRegs[k]))
                            ordered[k].registration = newRegs[k];
                    r.flights = ordered;
                    r.SyncPrimaryFromFlights();
                }
            }

            stats = StatsOf(fleet, plan);
            var nowRegs = new HashSet<string>(plan.Registrations(), StringComparer.OrdinalIgnoreCase);
            foreach (var reg in before)
                if (!string.IsNullOrEmpty(reg) && !nowRegs.Contains(reg)) freed.Add(reg);
            freed.Sort(StringComparer.OrdinalIgnoreCase);
            busiest = stats.busiestReg;

            if (apply && gs != null)
                PruneInvalidRings(gs);
            return freed.Count;
        }

        /// <summary>重排后清理不再由同机执飞的航班环（避免周计划里出现虚假连班）。</summary>
        public static int PruneInvalidRings(GameState gs)
        {
            if (gs?.flightRings == null || gs.flightRings.Count == 0 || gs.activeRoutes == null) return 0;
            int removed = 0;
            for (int i = gs.flightRings.Count - 1; i >= 0; i--)
            {
                var ring = gs.flightRings[i];
                if (ring == null) { gs.flightRings.RemoveAt(i); removed++; continue; }
                var a = FindRoute(gs, ring.legARouteId);
                var b = FindRoute(gs, ring.legBRouteId);
                bool stillValid = a != null && b != null
                                  && a.IsFlownBy(ring.registration) && b.IsFlownBy(ring.registration);
                if (!stillValid)
                {
                    gs.flightRings.RemoveAt(i);
                    removed++;
                }
            }
            if (removed > 0)
                gs.AddDecisionLog($"运营：机队日循环重排后，{removed} 个已失效的航班环被撤销（班次已改由其它飞机执飞）。");
            return removed;
        }

        static RouteData FindRoute(GameState gs, string routeId)
        {
            if (gs?.activeRoutes == null || string.IsNullOrEmpty(routeId)) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r != null && string.Equals(r.routeId, routeId, StringComparison.OrdinalIgnoreCase))
                    return r;
            }
            return null;
        }
    }
}
