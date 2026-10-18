using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 时刻槽使用率考核（IATA《Worldwide Airport Slot Guidelines》§8.6 use-it-or-lose-it
    /// ＋ §8.7 historic precedence）。
    ///
    /// 现实规则与本文的对应（**不做「一年一个率」的简化**，口径逐条落地）：
    ///
    /// | 真实（WASG） | 本作 |
    /// |---|---|
    /// | 判定周期是**航季**，一年两次 | 航季 = 2 个季度：夏秋 = Q2+Q3 / 冬春 = Q4+Q1 |
    /// | 每个**星期几**是独立系列（8.7.1f） | 系列 = 机场 × 具体时刻 × 星期几 |
    /// | 系列 = 同一天同一（近似）时刻的 ≥5 个时刻（1.7.2e） | 本作班期只有「每日 / 一三五 / 二四六」，按量级对齐；不做 ≥5 硬闸门 |
    /// | 基准日 1/31、8/31（8.7.1a） | 取航季**第 2 个季度的第一天**（夏秋 Q3 初 / 冬春 Q1 初） |
    /// | 基准日之后的取消一律算未使用（8.7.3.1） | **只在 HBD 之后计数**，此前的取消不计入 |
    /// | 未达 80% 则失去下一等价航季的历史优先权（8.6.1） | 失格系列于**下一等价航季开端**释放持仓、并终止祖父条款 |
    /// | 整季未使用的时刻不存在保留理由（8.6.1） | 闲置持仓（一次没绑班次）换季直接退回池 |
    /// | 正当未用须**主动申请**并经局方裁定（8.8，JNUS） | 收件箱决策邮件：列出当季真实事由，CEO 批准才豁免 |
    ///
    /// **为什么分母记在「应飞」时，而不是按日历反推**：本作班期字段（`daysOfWeek` + `weeklyFlights`）
    /// 允许「25 班/周 · 每日」这种把 N 班摊在 7 天的写法，按日历反推分母必然与引擎实际执行口径分叉。
    /// 这里改为**在引擎逐日判定班期的同一处记「应飞」**，与「实际执飞」共用同一个日历循环，两侧同源。
    ///
    /// **关键不变量**：`due` 必须在**无机停飞时照样累加**。否则「整季停飞的航线」分母恒为 0、
    /// 永远不失格 —— 那正好与真实规则相反（不飞才是最大的未使用）。
    ///
    /// ★2026-10-15：系列与持仓的粒度都由「时段」（早/中/晚/夜）改为**具体时刻** `"HH:mm"` ——
    /// 这正是 WASG 的口径（1.7.2d/e：系列钉在时刻上）。两条后果：
    /// - 同段不同时刻是**不同系列**：06:30 天天飞、07:00 一周飞两天，前者保权后者掉格，本就该分开判；
    /// - 失格释放能对应到具体时刻（06:30 掉格不会误退 07:00 的槽），只剩「星期几」那一维仍是近似
    ///   （持仓不区分班期，见设计文档取舍表）。
    /// </summary>
    public static class SlotUsageLogic
    {
        /// <summary>保留历史优先权所需的最低使用率（WASG 8.6.1）。</summary>
        public const float Threshold = 0.80f;

        public const int Summer = 0;
        public const int Winter = 1;

        // ————————————————— 航季数学 —————————————————

        /// <summary>
        /// 季度 → 航季 key。key 在世界轴上**自增 1**，故「上一等价航季」恒为 key − 1、
        /// 「下一等价航季」恒为 key + 1 —— 所有跨季比较都不必再做年份加减。
        ///
        /// 夏秋 = Q2 + Q3；冬春 = Q4 + Q1（跨年，故 Q1 归入**上一年**起算的冬春）。
        /// </summary>
        public static int KeyOf(int year, int quarter)
        {
            int y = year <= 0 ? 1 : year;
            switch (quarter)
            {
                case 1: return (y - 1) * 2 + Winter;   // Q1 属上一年起算的冬春
                case 2: return y * 2 + Summer;
                case 3: return y * 2 + Summer;
                default: return y * 2 + Winter;        // Q4
            }
        }

        public static int StartYearOf(int key) => key / 2;
        /// <summary>0 = 夏秋 / 1 = 冬春。</summary>
        public static int SeasonOf(int key) => key % 2;
        public static string SeasonName(int key) => SeasonOf(key) == Winter ? "冬春" : "夏秋";
        /// <summary>航季的起始季度（夏秋 Q2 / 冬春 Q4）。</summary>
        public static int StartQuarterOf(int key) => SeasonOf(key) == Summer ? 2 : 4;
        /// <summary>HBD（历史基准日）所在季度（夏秋 Q3 / 冬春 Q1）—— 即航季的第 2 个季度。</summary>
        public static int HbdQuarterOf(int key) => SeasonOf(key) == Summer ? 3 : 1;
        /// <summary>HBD 所在年份（冬春的 HBD 落在起始年的次年）。</summary>
        public static int HbdYearOf(int key) => SeasonOf(key) == Summer ? StartYearOf(key) : StartYearOf(key) + 1;
        /// <summary>航季的结束季度（夏秋 Q3 / 冬春 Q1 == HBD 季度）。</summary>
        public static int EndQuarterOf(int key) => HbdQuarterOf(key);

        public static string LabelOf(int key) => $"第 {StartYearOf(key)} 年 {SeasonName(key)}";

        /// <summary>该季度是否为某个航季的**开端**（夏秋 Q2 / 冬春 Q4）—— 换季清算时点。</summary>
        public static bool IsSeasonStartQuarter(int quarter) => quarter == 2 || quarter == 4;
        /// <summary>该季度是否为某航季的 **HBD**（夏秋 Q3 / 冬春 Q1）—— 开始计数时点。</summary>
        public static bool IsHbdQuarter(int quarter) => quarter == 1 || quarter == 3;

        public static int CurrentKey(GameState gs) => KeyOf(gs?.year ?? 1, gs?.quarter ?? 1);

        // ————————————————— 系列口径 —————————————————

        /// <summary>「周一」…「周日」。必须带「周」字前缀 —— DayMarkZh 只给「一」，直接拼会得到「早班 一」。</summary>
        static string WeekdayName(int weekday)
            => weekday >= 0 && weekday < 7 ? "周" + FlightScheduleFormat.DayMarkZh(weekday) : "?";

        /// <summary>系列标识（展示用，如「PEK 08:00 周一」）。★2026-10-15：写具体时刻，不写「早班」。</summary>
        public static string SeriesLabel(string airport, string time, int weekday)
            => $"{airport} {ScheduleSlots.BandNameToClock(time)} {WeekdayName(weekday)}";

        /// <summary>按时段名的兼容入口：折成该时段峰点后同上一函数（存量调用点用）。</summary>
        public static string SeriesLabel(string airport, int band, int weekday)
            => SeriesLabel(airport, ScheduleSlots.DepTimes[band < 0 ? 0 : band > 3 ? 3 : band], weekday);

        /// <summary>
        /// 把任意离港时刻归一到它所属需求波窗口内的**网格时刻**（★2026-10-15）。
        /// 系列与持仓都在 30 分钟网格上，实际离港（排班器按 5 分钟取整）必须先归到网格，
        /// 否则「06:35 飞的班」会自成一个永远集不齐的系列。
        /// </summary>
        public static string SeriesTimeOf(int rawMinutes)
        {
            int band = ScheduleSlots.BandOfDep(ScheduleSlots.FormatClock(rawMinutes));
            int m = ScheduleSlots.SnapIntoWindow(band, ((rawMinutes % 1440) + 1440) % 1440);
            return ScheduleSlots.FormatClock(ScheduleSlots.SnapToGrid(band, m));
        }

        /// <summary>系列时刻的归一（字符串入口，幂等）。</summary>
        public static string SeriesTimeOf(string clock)
        {
            int m = ScheduleSlots.ParseClock(clock);
            return m < 0 ? ScheduleSlots.BandNameToClock(clock) : SeriesTimeOf(m);
        }

        /// <summary>
        /// 星期几下标（0 = 周一 … 6 = 周日）。必须与 <see cref="FlightScheduleFormat.DaysOf"/> 的
        /// 「一…日」字面顺序同口径，否则班期判定与使用率统计会错位一天。
        /// </summary>
        public static int WeekdayOf(DateTime day) => ((int)day.DayOfWeek + 6) % 7;

        static SlotUsageState State(GameState gs)
        {
            if (gs == null) return null;
            var stg = StrategyManager.EnsureState(gs);
            if (stg == null) return null;
            if (stg.slotUsage == null) stg.slotUsage = new SlotUsageState();
            return stg.slotUsage;
        }

        static SlotSeries Find(GameState gs, string airport, string time, int weekday, bool create)
        {
            var s = State(gs);
            if (s == null || string.IsNullOrEmpty(airport)) return null;
            string t = SeriesTimeOf(time);
            for (int i = 0; i < s.series.Count; i++)
            {
                var x = s.series[i];
                if (x == null) continue;
                if (x.weekday != weekday) continue;
                if (!string.Equals(x.airport, airport, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(SeriesTimeOf(x.time), t, StringComparison.Ordinal)) continue;
                return x;
            }
            if (!create) return null;
            var made = new SlotSeries { airport = airport, time = t, weekday = weekday };
            s.series.Add(made);
            return made;
        }

        /// <summary>该星期几的系列是否仍保有历史优先权（无考核记录时视为**有** —— 没被考核过的不该算已失格）。</summary>
        public static bool HasHistoricPrecedence(GameState gs, string airport, string time, int weekday)
        {
            var x = Find(gs, airport, time, weekday, false);
            return x == null || x.historicPrecedence;
        }

        /// <summary>按时段名的兼容入口（折成该时段峰点）：存量调用点用。</summary>
        public static bool HasHistoricPrecedence(GameState gs, string airport, int band, int weekday)
            => HasHistoricPrecedence(gs, airport, ScheduleSlots.DepTimes[band < 0 ? 0 : band > 3 ? 3 : band], weekday);

        /// <summary>
        /// 该机场该**时刻**下是否**任一星期几**仍保有历史优先权（供祖父条款闸门用）。
        /// 有记录但全部失格 → 不再享有；全无记录 → 视为尚未受考核，照旧享有。
        /// </summary>
        public static bool HasAnyHistoricPrecedence(GameState gs, string airport, string time)
        {
            var s = State(gs);
            if (s?.series == null) return true;
            string t = SeriesTimeOf(time);
            bool seen = false;
            for (int i = 0; i < s.series.Count; i++)
            {
                var x = s.series[i];
                if (x == null) continue;
                if (!string.Equals(x.airport, airport, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(SeriesTimeOf(x.time), t, StringComparison.Ordinal)) continue;
                seen = true;
                if (x.historicPrecedence) return true;
            }
            return !seen;
        }

        /// <summary>按时段名的兼容入口（折成该时段峰点）：存量调用点用。</summary>
        public static bool HasAnyHistoricPrecedence(GameState gs, string airport, int band)
            => HasAnyHistoricPrecedence(gs, airport, ScheduleSlots.DepTimes[band < 0 ? 0 : band > 3 ? 3 : band]);

        // ————————————————— 计数 —————————————————

        /// <summary>
        /// 进入本季的 HBD：**计数器清零、历史优先权与裁定记录保留**。
        /// 这两件事必须分开 —— 若连同 `historicPrecedence` 一起清掉，失格状态会在下一个 HBD
        /// 复活，整个机制凭空蒸发（这是本类最容易写错的一处）。
        /// </summary>
        public static void BeginSeason(GameState gs, int key)
        {
            var s = State(gs);
            if (s == null) return;
            s.countingKey = key;
            for (int i = 0; i < s.series.Count; i++)
            {
                var x = s.series[i];
                if (x == null) continue;
                x.due = 0;
                x.operated = 0;
                x.justified = 0;
                x.jnusReasonCsv = "";
            }
        }

        /// <summary>
        /// 记一次「应飞」。在引擎逐日判定班期的同一处调用，**不论本班是否真飞得起来**
        /// （无机停飞必须照记分母，见类注释的不变量）。
        /// </summary>
        /// <param name="justifiedReason">飞不了时的正当事由（None = 无正当理由，按未使用计）。</param>
        public static void NoteDue(GameState gs, string airport, string time, int weekday, GroundReason justifiedReason)
        {
            var s = State(gs);
            if (s == null || s.countingKey < 0) return;
            var x = Find(gs, airport, time, weekday, true);
            if (x == null) return;
            x.due++;
            if (justifiedReason != GroundReason.None)
            {
                x.justified++;
                x.jnusReasonCsv = BumpReason(x.jnusReasonCsv, JustifiedLabel(justifiedReason));
            }
        }

        /// <summary>记一次「实际执飞」。</summary>
        public static void NoteOperated(GameState gs, string airport, string time, int weekday)
        {
            var s = State(gs);
            if (s == null || s.countingKey < 0) return;
            var x = Find(gs, airport, time, weekday, true);
            if (x != null) x.operated++;
        }

        /// <summary>按时段名的兼容入口（折成该时段峰点）：存量调用点用。</summary>
        public static void NoteDue(GameState gs, string airport, int band, int weekday, GroundReason justifiedReason)
            => NoteDue(gs, airport, ScheduleSlots.DepTimes[band < 0 ? 0 : band > 3 ? 3 : band], weekday, justifiedReason);

        /// <summary>按时段名的兼容入口（折成该时段峰点）：存量调用点用。</summary>
        public static void NoteOperated(GameState gs, string airport, int band, int weekday)
            => NoteOperated(gs, airport, ScheduleSlots.DepTimes[band < 0 ? 0 : band > 3 ? 3 : band], weekday);

        /// <summary>
        /// 一条航线的某一班次当天**各协调停站**的「应飞」各记一次。
        ///
        /// ★2026-10-16：改为按到站时刻表逐站记（<see cref="RoutePermitLogic.StationCalls"/>）。
        /// 旧口径只记本场 + 对端，于是**经停站持有的槽永远没有使用记录** —— 既不进 80% 考核的分母，
        /// 也不会被记为「已飞」，换季清算会把它们当成闲置持仓收回（`SlotHolding.expiresTurn`）。
        /// 跨午夜的停留（离场落在次日）归到**次日**的星期几 —— 系列按「该离场动作落在哪个星期几」定义，
        /// 而不是按前一段的起飞日。
        /// </summary>
        /// <param name="reason">本班当天的停飞原因（None = 具备执飞资格）。</param>
        public static void NoteDueForFlight(GameState gs, RouteData route, RouteFlight flight, DateTime day, GroundReason reason)
        {
            if (gs == null || route == null || flight == null) return;
            var s = State(gs);
            if (s == null || s.countingKey < 0) return;

            int wd = WeekdayOf(day);
            var calls = RoutePermitLogic.StationCalls(route, flight.depTime, flight.arrTime);
            for (int i = 0; i < calls.Count; i++)
            {
                var c = calls[i];
                if (!RoutePermitLogic.NeedsDepartureSlot(c.airport)) continue;
                int callWd = (wd + c.dep / (24 * 60)) % 7;
                NoteDue(gs, c.airport, SeriesTimeOf(ScheduleSlots.FormatClock(Mod24(c.dep))), callWd,
                    ReasonAt(gs, c.airport, reason));
            }
        }

        /// <summary>
        /// 某一班次**某方向各协调停站**的「实际执飞」各记一次（在起飞的唯一闸门口调用）。
        /// </summary>
        /// <param name="inbound">true = 本次是回程方向（对端离场 + 经停端·回程离场）。</param>
        public static void NoteOperatedForLeg(GameState gs, RouteData route, RouteFlight flight,
            DateTime departureLocal, bool inbound)
        {
            if (gs == null || route == null || flight == null) return;
            var s = State(gs);
            if (s == null || s.countingKey < 0) return;

            var line = RoutePermitLogic.RoundTripOf(route, flight.depTime, flight.arrTime);
            if (line.depOrigin < 0) return;
            int baseDep = inbound ? line.depDest : line.depOrigin;
            int wd = WeekdayOf(departureLocal);

            var calls = RoutePermitLogic.StationCalls(route, flight.depTime, flight.arrTime);
            for (int i = 0; i < calls.Count; i++)
            {
                var c = calls[i];
                if (c.inbound != inbound) continue;
                if (!RoutePermitLogic.NeedsDepartureSlot(c.airport)) continue;
                int callWd = (wd + (c.dep - baseDep) / (24 * 60)) % 7;
                NoteOperated(gs, c.airport, SeriesTimeOf(ScheduleSlots.FormatClock(Mod24(c.dep))), callWd);
            }
        }

        static int Mod24(int minutes) => ((minutes % (24 * 60)) + 24 * 60) % (24 * 60);

        /// <summary>该端当天的停飞原因；机场被外部事件关闭时也计入正当（对应真实的机场关闭豁免）。</summary>
        static GroundReason ReasonAt(GameState gs, string airport, GroundReason reason)
        {
            if (ExternalEventManager.IsAirportFrozen(gs, airport)) return GroundReason.Conflict;
            return reason;
        }

        /// <summary>正当事由的中文名（WASG §8.8 的落地口径）。</summary>
        public static string JustifiedLabel(GroundReason r)
        {
            switch (r)
            {
                case GroundReason.Conflict: return "机场关闭/流控";
                case GroundReason.NoAssignment:
                case GroundReason.Missing:
                case GroundReason.Retired: return "无机可派";
                case GroundReason.Maintenance: return "停场检修";
                case GroundReason.LeasedOut: return "运力投入过渡业务";
                default: return "";
            }
        }

        static string BumpReason(string csv, string label)
        {
            if (string.IsNullOrEmpty(label)) return csv ?? "";
            string cur = csv ?? "";
            int at = cur.IndexOf(label + "×", StringComparison.Ordinal);
            if (at < 0)
                return cur.Length == 0 ? label + "×1" : cur + "、" + label + "×1";
            int n = at + label.Length + 1;
            int end = n;
            while (end < cur.Length && char.IsDigit(cur[end])) end++;
            int v = 0;
            int.TryParse(cur.Substring(n, end - n), out v);
            return cur.Substring(0, at) + label + "×" + (v + 1) + cur.Substring(end);
        }

        // ————————————————— 裁定 —————————————————

        /// <summary>一季评定的汇总（供邮件正文与决策日志）。</summary>
        public class SeasonVerdict
        {
            public int key = -1;
            public string label = "";
            public int kept;
            public int lost;
            public int pendingJnus;
            public int unjudged;
            public readonly List<SlotSeries> lostSeries = new List<SlotSeries>();
            public readonly List<SlotSeries> jnusSeries = new List<SlotSeries>();
            public bool HasSomething => lost > 0 || pendingJnus > 0;
        }

        /// <summary>
        /// 结算一个航季的使用率：逐系列判定 80%。
        /// 未达标且**有**正当未用事由 → 进入待裁（JNUS）队列（暂不释放）；无正当理由 → 直接判失格。
        /// </summary>
        public static SeasonVerdict Evaluate(GameState gs, int key)
        {
            var s = State(gs);
            var v = new SeasonVerdict { key = key, label = LabelOf(key) };
            if (s?.series == null) return v;

            // 本季根本没计过数（首个 HBD 之前 / 读档错位）→ 无可裁定，且不得据以判失格。
            if (s.countingKey != key)
            {
                s.countingKey = -1;
                return v;
            }

            for (int i = 0; i < s.series.Count; i++)
            {
                var x = s.series[i];
                if (x == null) continue;
                if (x.due <= 0) { v.unjudged++; continue; }

                float usage = x.operated / (float)x.due;
                x.lastUsage = usage;
                x.lastEvalSeason = v.label;

                if (usage >= Threshold - 1e-4f)
                {
                    x.lastResult = "kept";
                    x.historicPrecedence = true;
                    v.kept++;
                    continue;
                }

                if (x.justified > 0)
                {
                    x.lastResult = "pending_jnus";
                    v.pendingJnus++;
                    v.jnusSeries.Add(x);
                    s.pendingJnus.Add(new SlotPendingAction
                    {
                        airport = x.airport,
                        time = x.time,
                        weekday = x.weekday,
                        applyKey = key + 2,               // 下一等价航季
                        label = SeriesLabel(x.airport, x.time, x.weekday)
                    });
                    continue;
                }

                x.lastResult = "lost";
                x.historicPrecedence = false;
                v.lost++;
                v.lostSeries.Add(x);
                s.pendingReleases.Add(new SlotPendingAction
                {
                    airport = x.airport,
                    time = x.time,
                    weekday = x.weekday,
                    applyKey = key + 2,
                    label = SeriesLabel(x.airport, x.time, x.weekday)
                });
            }

            s.lastEvalSeason = v.label;
            s.lastKept = v.kept;
            s.lastLost = v.lost;
            s.lastPending = v.pendingJnus;
            // 本季计到此为止：下一季的计数要等它自己的 HBD 才开张。若不清零，航季头一个季度的
            // 班次会被算进上一季的分母（真实规则里那一段本来就不进任何一季的账）。
            s.countingKey = -1;
            return v;
        }

        /// <summary>
        /// 执行「已到季」的失格释放：退回对应持仓并终止该系列的历史优先权。
        /// 真正的资产损失发生在这里 —— 时刻是花钱买的，退回池子即等于这笔钱打了水漂。
        /// </summary>
        public static List<string> ApplyDueReleases(GameState gs, int key, out int releasedCount)
        {
            var notes = new List<string>();
            releasedCount = 0;
            var s = State(gs);
            if (s?.pendingReleases == null || s.pendingReleases.Count == 0) return notes;

            for (int i = s.pendingReleases.Count - 1; i >= 0; i--)
            {
                var p = s.pendingReleases[i];
                if (p == null) { s.pendingReleases.RemoveAt(i); continue; }
                if (p.applyKey != key) continue;

                int n = ReleaseHolding(gs, p.airport, p.time, out bool freedBound);
                releasedCount += n;
                MarkLost(gs, p.airport, p.time, p.weekday);
                notes.Add(n > 0
                    ? $"{p.label}：使用率未达标，已退回时刻池{(freedBound ? "（该持仓原已绑定班次）" : "")}"
                    : $"{p.label}：使用率未达标，历史优先权终止（本就无持仓，一直在用祖父条款）");
                s.pendingReleases.RemoveAt(i);
            }
            return notes;
        }

        /// <summary>
        /// 处理到季的待裁 JNUS：**未获批者按失格执行**（到期未处理视同驳回）。
        /// 已获批准者在 <see cref="GrantJnus"/> 里就已被移出队列，不会走到这里。
        /// </summary>
        public static List<string> SettlePendingJnus(GameState gs, int key, out int releasedCount)
        {
            var notes = new List<string>();
            releasedCount = 0;
            var s = State(gs);
            if (s?.pendingJnus == null || s.pendingJnus.Count == 0) return notes;

            for (int i = s.pendingJnus.Count - 1; i >= 0; i--)
            {
                var p = s.pendingJnus[i];
                if (p == null) { s.pendingJnus.RemoveAt(i); continue; }
                if (p.applyKey != key) continue;

                int n = ReleaseHolding(gs, p.airport, p.time, out _);
                releasedCount += n;
                MarkLost(gs, p.airport, p.time, p.weekday);
                notes.Add($"{p.label}：未获正当未用豁免，已按失格处置");
                s.pendingJnus.RemoveAt(i);
            }
            return notes;
        }

        /// <summary>批准某系列的本季正当未用豁免（CEO 在收件箱裁定后调用）。</summary>
        public static bool GrantJnus(GameState gs, string airport, string time, int weekday)
        {
            var s = State(gs);
            if (s == null) return false;
            var x = Find(gs, airport, time, weekday, false);
            if (x != null)
            {
                x.lastResult = "jnus";
                x.historicPrecedence = true;
            }
            if (!RemovePending(s.pendingJnus, airport, time, weekday, out _)) return false;
            s.lastJnus++;
            s.lastPending = Math.Max(0, s.lastPending - 1);
            return true;
        }

        /// <summary>驳回某系列的本季正当未用豁免：转为失格，但释放仍等下一等价航季开端（与直判失格同轨）。</summary>
        public static bool DenyJnus(GameState gs, string airport, string time, int weekday)
        {
            var s = State(gs);
            if (s == null) return false;
            var x = Find(gs, airport, time, weekday, false);
            if (x != null)
            {
                x.lastResult = "lost";
                x.historicPrecedence = false;
            }
            if (!RemovePending(s.pendingJnus, airport, time, weekday, out SlotPendingAction p)) return false;
            if (p != null) s.pendingReleases.Add(p);
            s.lastPending = Math.Max(0, s.lastPending - 1);
            s.lastLost++;
            return true;
        }

        /// <summary>按时段名的兼容入口（折成该时段峰点）：存量调用点用。</summary>
        public static bool GrantJnus(GameState gs, string airport, int band, int weekday)
            => GrantJnus(gs, airport, ScheduleSlots.DepTimes[band < 0 ? 0 : band > 3 ? 3 : band], weekday);

        /// <summary>按时段名的兼容入口（折成该时段峰点）：存量调用点用。</summary>
        public static bool DenyJnus(GameState gs, string airport, int band, int weekday)
            => DenyJnus(gs, airport, ScheduleSlots.DepTimes[band < 0 ? 0 : band > 3 ? 3 : band], weekday);

        static bool RemovePending(List<SlotPendingAction> list, string airport, string time, int weekday,
            out SlotPendingAction removed)
        {
            removed = null;
            if (list == null) return false;
            string t = SeriesTimeOf(time);
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null) continue;
                if (p.weekday != weekday) continue;
                if (!string.Equals(p.airport, airport, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(SeriesTimeOf(p.time), t, StringComparison.Ordinal)) continue;
                removed = p;
                list.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>本季待裁豁免的系列（供邮件正文与 n77）。</summary>
        public static List<SlotSeries> PendingJnusSeries(GameState gs)
        {
            var list = new List<SlotSeries>();
            var s = State(gs);
            if (s?.series == null) return list;
            for (int i = 0; i < s.series.Count; i++)
            {
                var x = s.series[i];
                if (x != null && x.lastResult == "pending_jnus") list.Add(x);
            }
            return list;
        }

        /// <summary>本季有应飞的系列（供 n77 明细；按机场 → 时段 → 星期排序）。</summary>
        public static List<SlotSeries> Series(GameState gs)
        {
            var list = new List<SlotSeries>();
            var s = State(gs);
            if (s?.series == null) return list;
            for (int i = 0; i < s.series.Count; i++)
                if (s.series[i] != null && s.series[i].due > 0) list.Add(s.series[i]);
            list.Sort((a, b) =>
            {
                int c = string.Compare(a.airport, b.airport, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                c = string.Compare(SeriesTimeOf(a.time), SeriesTimeOf(b.time), StringComparison.Ordinal);
                if (c != 0) return c;
                return a.weekday.CompareTo(b.weekday);
            });
            return list;
        }

        /// <summary>本季已失格的系列（供 n77 警示与邮件）。</summary>
        public static List<SlotSeries> LostSeries(GameState gs)
        {
            var list = new List<SlotSeries>();
            var s = State(gs);
            if (s?.series == null) return list;
            for (int i = 0; i < s.series.Count; i++)
            {
                var x = s.series[i];
                if (x != null && !x.historicPrecedence) list.Add(x);
            }
            return list;
        }

        static void MarkLost(GameState gs, string airport, string time, int weekday)
        {
            var x = Find(gs, airport, time, weekday, false);
            if (x != null) x.historicPrecedence = false;
        }

        /// <summary>
        /// 退回该机场该**时刻**（± 容差）的一条持仓：优先**未绑定**者；无未绑定者时退回最早的一条已绑定持仓
        /// （该班次此后应按缺槽处理）。★2026-10-15：由「按时段退」改为「按时刻退」—— 06:30 掉格
        /// 不会再去误退 07:00 的槽；只剩「星期几」那一维仍是近似（持仓不区分班期）。
        /// 返回退回条数（0 = 本就在用祖父条款、没有持仓可退）。
        /// </summary>
        static int ReleaseHolding(GameState gs, string airport, string time, out bool freedBound)
        {
            freedBound = false;
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return 0;

            int pick = -1;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var h = stg.slots[i];
                if (h == null || h.inUse) continue;
                if (!string.Equals(h.airport, airport, StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.ClockDiff(h.slotTime, time) > ScheduleSlots.SlotToleranceMinutes) continue;
                pick = i;
                break;
            }
            if (pick < 0)
            {
                for (int i = 0; i < stg.slots.Count; i++)
                {
                    var h = stg.slots[i];
                    if (h == null) continue;
                    if (!string.Equals(h.airport, airport, StringComparison.OrdinalIgnoreCase)) continue;
                    if (ScheduleSlots.ClockDiff(h.slotTime, time) > ScheduleSlots.SlotToleranceMinutes) continue;
                    pick = i;
                    freedBound = true;
                    break;
                }
            }
            if (pick < 0) return 0;
            stg.slots.RemoveAt(pick);
            return 1;
        }

        /// <summary>
        /// use-it-or-lose-it：换季清算时收回**闲置持仓**（★2026-10-15 新增）。
        ///
        /// 为什么单独立这一条：系列考核只能裁到「有过应飞记录」的槽。一次都没绑上班次的持仓
        /// **没有系列**，80% 规则根本碰不到它 —— 于是「买了槽不飞」这一半漏在规则之外，这正好
        /// 与真实规则相反（WASG 8.6.1：整季未使用的时刻没有任何保留理由）。
        ///
        /// 判定用 <see cref="SlotHolding.expiresTurn"/>（取得季 + 2 个季度）：给 CEO 留一个航季的
        /// 筹备窗口，到点仍未被任何班次绑定即退回池子，并如实记成**资产损失**（钱白花了，不是白得）。
        /// </summary>
        /// <param name="lostWan">退回持仓的取得成本合计（万美元），供邮件向 CEO 说明损失。</param>
        public static List<string> ReleaseIdleHoldings(GameState gs, int turn, out int releasedCount, out float lostWan)
        {
            var notes = new List<string>();
            releasedCount = 0;
            lostWan = 0f;
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return notes;

            for (int i = stg.slots.Count - 1; i >= 0; i--)
            {
                var h = stg.slots[i];
                if (h == null) { stg.slots.RemoveAt(i); continue; }
                if (h.inUse) continue;                 // 绑了班次的走系列考核，不在这里收
                if (h.expiresTurn < 0 || h.expiresTurn > turn) continue;
                lostWan += h.acquisitionCostWan;
                notes.Add($"{ScheduleSlots.SlotLabel(h.airport, h.slotTime)}：整个航季未投运，按 use-it-or-lose-it 退回时刻池");
                stg.slots.RemoveAt(i);
                releasedCount++;
            }
            return notes;
        }

        // ————————————————— 展示 —————————————————

        /// <summary>一行式考核说明（供 n77 与邮件页眉）。</summary>
        public static string FormatSeasonLine(GameState gs)
        {
            var s = State(gs);
            if (s == null) return "时刻槽考核：未启用";
            int key = CurrentKey(gs);
            var sb = new StringBuilder();
            sb.Append($"考核航季：{LabelOf(key)}（Q{StartQuarterOf(key)}–Q{EndQuarterOf(key)}）· 阈值 {Threshold * 100f:F0}%");
            if (s.countingKey < 0)
                sb.Append(" · 尚未进入历史基准日，本季未开始计数");
            else if (s.countingKey != key)
                sb.Append($" · 正在计数 {LabelOf(s.countingKey)}");
            else
                sb.Append($" · 已进入历史基准日（第 {HbdYearOf(key)} 年 Q{HbdQuarterOf(key)} 起计）");
            if (!string.IsNullOrEmpty(s.lastEvalSeason))
                sb.Append($" · 上季（{s.lastEvalSeason}）达标 {s.lastKept} / 失格 {s.lastLost} / 待裁 {s.lastPending}");
            return sb.ToString();
        }

        /// <summary>某系列的现状一行式（供 n77 明细）。</summary>
        public static string FormatSeriesLine(SlotSeries x)
        {
            if (x == null) return "";
            string usage = x.due > 0 ? $"{x.operated}/{x.due} = {x.operated / (float)x.due * 100f:F0}%" : "本季无应飞";
            string tail = string.IsNullOrEmpty(x.jnusReasonCsv) ? "" : $" · 正当未用 {x.jnusReasonCsv}";
            return $"{SeriesLabel(x.airport, x.time, x.weekday)}　{usage}　{ResultLabel(x.lastResult, x.historicPrecedence)}{tail}";
        }

        public static string ResultLabel(string result, bool historicPrecedence)
        {
            switch (result)
            {
                case "kept": return "✓ 达标，保留历史优先权";
                case "jnus": return "✓ 正当未用获批，保留历史优先权";
                case "pending_jnus": return "◐ 未达标 · 待申请正当未用豁免";
                case "lost": return "✗ 未达标 · 已失格（下一等价航季释放）";
                default: return historicPrecedence ? "· 考核中" : "✗ 已失格";
            }
        }

        /// <summary>
        /// 把系列清单编码成 `机场|时刻|星期,机场|时刻|星期` —— 存进邮件字段，CEO 裁定后据此回溯定位。
        /// 用可编码字符串而不是把对象塞进邮件，是因为存档里邮件与状态是两套列表，互相引用会在读档后悬空。
        /// </summary>
        public static string EncodeSeries(List<SlotSeries> list)
        {
            var sb = new StringBuilder();
            if (list == null) return "";
            for (int i = 0; i < list.Count; i++)
            {
                var x = list[i];
                if (x == null) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(x.airport).Append('|').Append(SeriesTimeOf(x.time)).Append('|').Append(x.weekday);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 解码 <see cref="EncodeSeries"/>；解析不出任何条目的片段会被跳过（存档脏数据不抛异常）。
        /// ★2026-10-15：兼容旧档的**时段下标**写法（`PEK|0|0`）—— 旧版存的是 0–3 的下标，
        /// 直接按 `HH:mm` 去解析会一律判无效、把待裁邮件里的系列整批丢掉（历史优先权裁决就此悬空）。
        /// 故中间字段若是 0–3 的纯整数，按该时段峰点折算。
        /// </summary>
        public static List<SlotSeries> DecodeSeries(string csv)
        {
            var list = new List<SlotSeries>();
            if (string.IsNullOrEmpty(csv)) return list;
            string[] parts = csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string[] f = parts[i].Split('|');
                if (f.Length < 3) continue;
                string apt = f[0].Trim();
                if (apt.Length == 0) continue;
                int wd;
                if (!int.TryParse(f[2], out wd)) continue;
                if (wd < 0 || wd > 6) continue;
                string mid = f[1].Trim();
                string time;
                if (ScheduleSlots.ParseClock(mid) >= 0)
                {
                    time = SeriesTimeOf(mid);
                }
                else if (int.TryParse(mid, out int legacyBand) && legacyBand >= 0 && legacyBand <= 3)
                {
                    time = SeriesTimeOf(ScheduleSlots.DepTimes[legacyBand]);   // 旧档：时段下标 → 峰点
                }
                else if (ScheduleSlots.IsKnownTimeOrBand(mid))
                {
                    time = SeriesTimeOf(mid);                                  // 手改成「早班」这类时段名 → 峰点
                }
                else
                {
                    continue;   // 既不是时刻也不是合法下标/时段名 → 当作脏数据跳过
                }
                list.Add(new SlotSeries { airport = apt, time = time, weekday = wd });
            }
            return list;
        }

        /// <summary>
        /// 风险提示：本季使用率已低于阈值、且样本已足够的系列 —— 供 n77 提前预警
        /// （真实协调员也会在季中主动提示「有掉出风险」的系列，WASG 8.6.3）。
        /// </summary>
        public static List<SlotSeries> AtRisk(GameState gs)
        {
            var list = new List<SlotSeries>();
            var s = State(gs);
            if (s?.series == null) return list;
            for (int i = 0; i < s.series.Count; i++)
            {
                var x = s.series[i];
                if (x == null || x.due < 5 || !x.historicPrecedence) continue;
                if (x.operated / (float)x.due < Threshold) list.Add(x);
            }
            return list;
        }
    }
}
