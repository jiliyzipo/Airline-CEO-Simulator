using System;
using System.Collections.Generic;
using AirlineCEO.Logic;

#nullable disable

namespace AirlineCEO.Data
{
    /// <summary>已开通航线运行时记录（Phase 2 OPS 扩展）。</summary>
    [Serializable]
    public class RouteData
    {
        /// <summary>
        /// 「接管前」哨兵值：接管局的既有航线开航时间未知，统一写这个值。
        /// 与 <see cref="GameState.DateLabel"/>（第 N 年 QX）不是同一种东西，展示层必须区分，
        /// 否则会出现「开通：接管前」这种把非日期塞进日期位的文案。
        /// </summary>
        public const string OpenedBeforeTakeover = "接管前";

        public string routeId;
        public string origin;
        public string destination;
        public string stopover; // 经停机场 IATA，可空
        public float dailyProfit;   // 万美元/日（全部班次合计）
        /// <summary>开航时间。接管局的既有航线写哨兵值 <see cref="OpenedBeforeTakeover"/>（不是真实日期）。</summary>
        public string openedDate;
        public int score;
        public string rating;

        // —— Phase 2 OPS 调度字段（作为首班 mirror，向后兼容）——
        public float distanceKm;
        public string aircraftType = "A320neo";
        public string registration;
        public string flightNumber;
        public int weeklyFlights = 7;
        public string daysOfWeek = "每日";
        public string depTime = "08:00";
        public string arrTime = "10:30";
        public string season = "夏秋"; // 夏秋 / 冬春
        public int groundStaffAssigned = 2;
        /// <summary>
        /// 单班毛贡献基准（万美元/班/日；未乘班次与调度乘数，也**未扣**航线周期固定成本）。
        /// 可为**负**——负值表示该线单班即亏损，必须原样透传，不得当成「未设置」而改写符号。
        /// </summary>
        public float baseDailyProfit;
        /// <summary>运营部已编班、待 CEO 批准后才执行。</summary>
        public bool schedulePending;
        /// <summary>运营部方案改稿次数。</summary>
        public int opsPlanRevision;
        /// <summary>运力已按 CEO 指令调整、呈报待批复（不冻结航班执行，仅避免重复呈报）。</summary>
        public bool directivePending;
        /// <summary>运力指令下达前的排班快照（呈报被退回时回退）。</summary>
        public bool hasPreDirective;
        public List<RouteFlight> preDirectiveFlights = new List<RouteFlight>();
        public int preDirectiveRevision;
        /// <summary>
        /// 已就「无机可派 · 停飞」呈报过 CEO（同一轮停飞只报一次，复航时复位）。
        /// 仅供 <see cref="AirlineCEO.Systems.LiveClockSimulator"/> 的提示去重，不参与任何计算；
        /// 老存档缺该字段时为 false —— 正好让存量问题在下次停飞时被如实报到。
        /// </summary>
        public bool groundedNoticed;

        /// <summary>该航线上的全部班次（一架飞机在一个时段执飞 = 一班）。</summary>
        public List<RouteFlight> flights = new List<RouteFlight>();

        /// <summary>
        /// 本航线的**周期固定成本**（万美元/日）：与班次无关，航线存在即发生。
        /// 加密不增加它，故班次越密每班分摊越薄——这是「1 班亏 / 3 班赚」的唯一合法来源。
        /// 派生值（由航程与机型算出），不入存档，故无迁移问题。
        /// </summary>
        public float FixedDailyCostWan => OpsEconomics.RouteFixedDailyWan(distanceKm, aircraftType);

        /// <summary>
        /// 毛贡献（万美元/日）：未扣航线周期固定成本。
        /// 不变量：各班次 dailyProfit 之和 == 毛贡献，而 <see cref="dailyProfit"/> == 毛贡献 − 固定成本。
        /// </summary>
        public float GrossDailyProfit => dailyProfit + FixedDailyCostWan;

        /// <summary>
        /// 本航线的**经停停站时间**（分钟；无经停为 0）。它计入班期跨度（`arrTime`）——
        /// 飞机在经停站落地再起飞的那一段地面时间是航程的一部分，不是免费的。
        /// </summary>
        public int StopoverGroundMinutes =>
            string.IsNullOrWhiteSpace(stopover) ? 0 : AirlineCEO.Logic.OpsRotationParams.StopoverGroundMinutes;

        /// <summary>确保 flights 非空（老存档/旧数据回填首班）。</summary>
        public void EnsureFlights()
        {
            if (flights == null) flights = new List<RouteFlight>();
            if (flights.Count == 0)
            {
                flights.Add(new RouteFlight
                {
                    registration = registration,
                    depTime = depTime,
                    arrTime = arrTime,
                    daysOfWeek = daysOfWeek,
                    weeklyFlights = weeklyFlights,
                    slotLabel = ScheduleSlots.LabelForDep(depTime),
                    dutyStart = LiveClockLogic.ParseMinutes(depTime) - OpsRotationParams.ReportBeforeMinutes,
                    dailyProfit = GrossDailyProfit
                });
            }
            RepairPlaceholderSpan();
        }

        /// <summary>
        /// 占位时刻校正：创建航线时默认写成 08:00→10:30（150 分），与按航程推算的真实轮挡
        /// 相差过大（1100 km 仅 76 分）。这会同时污染「展示到达时刻」与「轮转窗口」，
        /// 使一段往返被算成 6 小时、挤掉后续需求波。单班航线在此按真实轮挡校正一次。
        /// </summary>
        void RepairPlaceholderSpan()
        {
            if (flights.Count != 1 || flights[0] == null) return;
            if (string.IsNullOrEmpty(depTime) || string.IsNullOrEmpty(arrTime)) return;

            int phys = OpsRotationParams.BlockMinutes(distanceKm) + StopoverGroundMinutes;
            int span = LiveClockLogic.BlockMinutes(depTime, arrTime, distanceKm);
            if (span <= 0 || span >= 24 * 60 / 2) return;
            int tol = OpsRotationParams.ScheduleSpanToleranceMinutes;
            int rel = (int)Math.Round(phys * OpsRotationParams.ScheduleSpanToleranceRatio);
            if (rel > tol) tol = rel;
            if (Math.Abs(span - phys) <= tol) return;      // 与真实轮挡相符 → 视为真实班期，保留

            int arr = (LiveClockLogic.ParseMinutes(depTime) + phys) % (24 * 60);
            string arrHhmm = $"{arr / 60:00}:{arr % 60:00}";
            arrTime = arrHhmm;
            flights[0].depTime = depTime;
            flights[0].arrTime = arrHhmm;
        }

        /// <summary>用首班同步平面字段（registration/depTime…）。</summary>
        public void SyncPrimaryFromFlights()
        {
            EnsureFlights();
            var f = flights[0];
            registration = f.registration;
            depTime = f.depTime;
            arrTime = f.arrTime;
            daysOfWeek = f.daysOfWeek;
            weeklyFlights = f.weeklyFlights;
        }

        /// <summary>
        /// 用平面字段重建单班列表（教程/单班排班用）。
        /// R4 双口径：单班航线本班即一个值勤期，报到时刻按「起飞 − 报到提前量」记下，
        /// 供「值勤日 / 离港日」两口径一致地对齐。
        /// </summary>
        public void SyncFlightsFromPrimary()
        {
            flights = new List<RouteFlight>
            {
                new RouteFlight
                {
                    registration = registration,
                    depTime = depTime,
                    arrTime = arrTime,
                    daysOfWeek = daysOfWeek,
                    weeklyFlights = weeklyFlights,
                    slotLabel = ScheduleSlots.LabelForDep(depTime),
                    dutyStart = LiveClockLogic.ParseMinutes(depTime) - OpsRotationParams.ReportBeforeMinutes,
                    dailyProfit = GrossDailyProfit
                }
            };
        }

        /// <summary>
        /// 航线日净利 = 各班次毛贡献之和 − 航线周期固定成本。
        /// 不变量：各班 dailyProfit 存的是**毛贡献**，固定成本只在航线层级扣一次；
        /// 若把固定成本摊进各班再在此处相减，会重复计提。
        /// </summary>
        public void RecalcDailyProfit()
        {
            EnsureFlights();
            float total = 0f;
            for (int i = 0; i < flights.Count; i++)
                total += flights[i].dailyProfit;
            dailyProfit = total - FixedDailyCostWan;
        }

        /// <summary>运力指令前快照：已存在快照时不覆盖（多轮指令退回应回到最初状态）。</summary>
        public void BackupScheduleForDirective()
        {
            if (hasPreDirective) return;
            EnsureFlights();
            preDirectiveFlights = new List<RouteFlight>();
            for (int i = 0; i < flights.Count; i++)
            {
                var f = flights[i];
                if (f == null) continue;
                preDirectiveFlights.Add(new RouteFlight
                {
                    registration = f.registration,
                    depTime = f.depTime,
                    arrTime = f.arrTime,
                    daysOfWeek = f.daysOfWeek,
                    weeklyFlights = f.weeklyFlights,
                    slotLabel = f.slotLabel,
                    dailyProfit = f.dailyProfit,
                    inboundLeg = f.inboundLeg
                });
            }
            preDirectiveRevision = opsPlanRevision;
            hasPreDirective = true;
        }

        /// <summary>回退到运力指令前的排班（退回呈报时调用）。</summary>
        public bool RestoreScheduleFromBackup()
        {
            if (!hasPreDirective || preDirectiveFlights == null || preDirectiveFlights.Count == 0) return false;
            flights = new List<RouteFlight>(preDirectiveFlights);
            opsPlanRevision = preDirectiveRevision;
            SyncPrimaryFromFlights();
            hasPreDirective = false;
            directivePending = false;
            return true;
        }

        /// <summary>班次数（无 flights 时按 1 班计）。</summary>
        public int FlightCount => (flights != null && flights.Count > 0) ? flights.Count : 1;

        /// <summary>
        /// 单班日利（**净额**，万美元/班/日）= 航线日净利 ÷ 班次数。
        ///
        /// 口径来源：邮件里的「单班日利」一直按 <c>dailyProfit / 班次数</c> 算
        /// （<c>OpsQueryLogic</c> 的答复件、运力呈报），周计划逐班行与 n43 班次详情必须同源，
        /// 否则同一个 NS100 在两个页面会印出两个数。
        ///
        /// 为什么**不能**直接用 <see cref="RouteFlight.dailyProfit"/>：那是**毛贡献份额**，
        /// 满足 Σ 各班 = <see cref="GrossDailyProfit"/>，未扣航线周期固定成本。
        /// 单班航线拿它当「日利」印会比航线日利大出整笔固定成本（PEK—PVG 实测 0.42 vs 0.35），
        /// 而这正是「两个页面数字对不上」那类报障的来源。
        /// </summary>
        public float FlightNetDailyProfit => dailyProfit / FlightCount;

        /// <summary>
        /// 该班次的**周净利**（万美元/周），与 <see cref="dailyProfit"/> 同口径、可直接相加。
        ///
        /// 必须是「航线日净利 × 7 × 本班周班次占比」，**不是**「单班日利 × 本班周班次」：
        /// <see cref="dailyProfit"/> 已经由 <c>OpsEconomics.ToDailyProfit</c> 按 <c>weeklyFlights/7</c>
        /// 摊到每个日历日了，再乘一次周班次等于把「加密」那部分乘第二遍。
        ///
        /// 实测事故（2026-10-27，T03.8 逐屏 · NS100/NS101 PEK⇄PVG 20 班/周）：
        /// 同一屏上摘要行写「周利 4.2 万」（= 日利 × 7，正确）、时刻表头写「WEEKLY PROFIT 12.0 万」
        /// （= 单班日利 × 20），比值 12.0/4.2 = 20/7 = 2.857 —— 正好是周班次倍数。
        /// 旧式 `FlightNetDailyProfit * WeeklyOf(f)` 只在「每班恰好 7 班/周」时正确，
        /// 所以 3 波 × 7 班的回归夹具与每周 7 班的长航线都测不出来，加密到非 7 班才暴露。
        /// </summary>
        /// <param name="f">本航线下的某一班次；为 null 或不属于本航线时按整条航线算。</param>
        public float WeeklyProfitOf(RouteFlight f)
        {
            int total = WeeklyFlightsDeparture();
            if (total <= 0) total = 7;
            int here = FlightScheduleFormat.WeeklyOf(f);
            if (here <= 0) here = total;   // 无班次信息时退化成整条航线，不做 0 权重
            return dailyProfit * 7f * here / total;
        }


        /// <summary>本航线是否有执飞机（接管局的机队缺口会造成无飞机的航线）。</summary>
        public bool HasAircraft => !string.IsNullOrEmpty(registration);

        /// <summary>
        /// 「机号 + 机型」展示文案。无执飞机时**不得报机型**：机型字段的兜底值（A320neo）
        /// 会让界面写出「未分配 A320neo 在飞 7,409 km 的 FRA–ATL」这种自相矛盾的行。
        /// </summary>
        public string AircraftLabel => HasAircraft
            ? registration + (string.IsNullOrEmpty(aircraftType) ? "" : " " + aircraftType)
            : UnassignedAircraftLabel;

        /// <summary>无机航线的统一文案（接管局机队缺口 / 待复航）。</summary>
        public const string UnassignedAircraftLabel = "运力不足 · 待复航";

        /// <summary>某注册号是否执飞本航线任一班。</summary>
        public bool IsFlownBy(string reg)
        {
            if (string.IsNullOrEmpty(reg)) return false;
            if (flights == null || flights.Count == 0)
                return string.Equals(registration, reg, StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < flights.Count; i++)
                if (string.Equals(flights[i].registration, reg, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// 把「文案 / 链接里的航线键」解析成在营航线。三档，**不猜**：
        /// <list type="number">
        /// <item>精确 <c>routeId</c>（如 <c>FRA-ATL-3</c>）—— 各页面点进来时用的键；</item>
        /// <item>机场对（如 <c>FRA-ATL</c>，取前两段；**正反两个方向都认**）——
        ///   正文里由 <see cref="AirlineCEO.Logic.TermNavMap"/> 包出来的航线链接走这一档
        ///   （链接只有机场两字码，拿不到序号）；</item>
        /// <item>都不是 → <c>null</c>。调用方须如实说「未选择航线 / 该航线不在运营」，
        ///   **绝不退而给第一条** —— 那会让「点 FRA—ATL 却打开 FRA—LHR」（2026-10-13 CEO 报障）重演。</item>
        /// </list>
        /// </summary>
        public static RouteData FindByKey(IList<RouteData> routes, string key)
        {
            if (routes == null || string.IsNullOrEmpty(key)) return null;
            string k = key.Trim();

            // ① 精确 routeId
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (r != null && string.Equals(r.routeId, k, StringComparison.OrdinalIgnoreCase)) return r;
            }

            // ② 机场对：取键的前两段（"FRA-ATL" 与 "FRA-ATL-3-ATL" 这类拼接都能认出来）
            string[] parts = k.Split('-');
            if (parts.Length < 2 || parts[0].Length != 3 || parts[1].Length != 3) return null;
            string o = parts[0].ToUpperInvariant();
            string d = parts[1].ToUpperInvariant();
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (r == null) continue;
                if (string.Equals(r.origin, o, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.destination, d, StringComparison.OrdinalIgnoreCase)) return r;
            }
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (r == null) continue;
                if (string.Equals(r.origin, d, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.destination, o, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }

        // ——————————————— R4 跨夜班归属（双口径） ———————————————
        // 值勤 / 机组按「值勤期起始日」归组（同机 + 同值勤日 = 一个值勤期）；
        // 航班量按「离港日」归组（即 flights 本身）。两者不混，UI 须标明用的是哪一套。

        /// <summary>本班所属值勤期的报到时刻（分钟）；未记录时按本班起飞 − 报到提前量推导。</summary>
        public static int ReportMinutesOf(RouteFlight f)
        {
            if (f == null) return -1;
            if (f.dutyStart >= 0) return f.dutyStart;
            return LiveClockLogic.ParseMinutes(f.depTime) - OpsRotationParams.ReportBeforeMinutes;
        }

        /// <summary>本班的离港时刻（分钟）。</summary>
        public static int DepartureMinutesOf(RouteFlight f)
            => f == null ? -1 : LiveClockLogic.ParseMinutes(f.depTime);

        /// <summary>
        /// 按「值勤日」分组值勤期（同机号 + 报到时刻同属一天 = 同一值勤期）。
        /// 跨夜的深夜班因此仍归入它起飞前那一日的值勤，而不会被午夜切开。
        /// </summary>
        public List<List<RouteFlight>> DutyPeriods()
        {
            var groups = new List<List<RouteFlight>>();
            if (flights == null || flights.Count == 0) return groups;
            var ordered = new List<RouteFlight>(flights);
            ordered.Sort((a, b) => DepartureMinutesOf(a).CompareTo(DepartureMinutesOf(b)));
            for (int i = 0; i < ordered.Count; i++)
            {
                var f = ordered[i];
                if (f == null) continue;
                int day = OpsRotationParams.DutyDayOf(ReportMinutesOf(f));
                string reg = f.registration ?? "";
                List<RouteFlight> hit = null;
                for (int g = 0; g < groups.Count; g++)
                {
                    var cand = groups[g];
                    if (cand == null || cand.Count == 0) continue;
                    string candReg = cand[0].registration ?? "";
                    if (!string.Equals(candReg, reg, StringComparison.OrdinalIgnoreCase)) continue;
                    if (OpsRotationParams.DutyDayOf(ReportMinutesOf(cand[0])) != day) continue;
                    hit = cand;
                    break;
                }
                if (hit == null) { hit = new List<RouteFlight>(); groups.Add(hit); }
                hit.Add(f);
            }
            return groups;
        }

        /// <summary>「值勤日」口径下的值勤期数量。</summary>
        public int DutyPeriodCount() => DutyPeriods().Count;

        /// <summary>离港日口径的每周班次（各班 weeklyFlights 之和；无班次时用航线字段）。</summary>
        public int WeeklyFlightsDeparture()
        {
            if (flights == null || flights.Count == 0)
                return weeklyFlights > 0 ? weeklyFlights : FlightScheduleFormat.WeeklyOf(new RouteFlight { weeklyFlights = weeklyFlights, daysOfWeek = daysOfWeek });
            int n = 0;
            for (int i = 0; i < flights.Count; i++)
                n += FlightScheduleFormat.WeeklyOf(flights[i]);
            return n;
        }

        /// <summary>值勤日口径的每周班次（跨夜班挪到报到所在星期后加总）。
        /// 与 <see cref="WeeklyFlightsDeparture"/> 的周合计**恒等** —— 两条口径只是把同一批班次
        /// 归到不同的星期，总量不变（见 <see cref="AddWeeklyFlights"/>）；差异要看星期分布（<see cref="WeekdayOpsDuty"/>）。</summary>
        public int WeeklyFlightsDuty()
        {
            var duty = WeekdayOpsDuty();
            int n = 0;
            for (int i = 0; i < duty.Length; i++) n += duty[i];
            return n > 0 ? n : WeeklyFlightsDeparture();
        }

        /// <summary>周一~周日：按计划离港日计的班次数。</summary>
        public int[] WeekdayOpsDeparture()
        {
            var counts = new int[7];
            AccumulateWeekdayOps(counts, null);
            return counts;
        }

        /// <summary>周一~周日：按值勤报到日计的班次数。</summary>
        public int[] WeekdayOpsDuty()
        {
            var counts = new int[7];
            AccumulateWeekdayOps(null, counts);
            return counts;
        }

        void AccumulateWeekdayOps(int[] depCounts, int[] dutyCounts)
        {
            if (flights == null || flights.Count == 0)
            {
                var days = FlightScheduleFormat.DaysOf(daysOfWeek, weeklyFlights);
                AddWeeklyFlights(days, 0, weeklyFlights > 0 ? weeklyFlights : CountMarkedDays(days), depCounts, dutyCounts);
                return;
            }
            for (int i = 0; i < flights.Count; i++)
            {
                var f = flights[i];
                if (f == null) continue;
                var days = FlightScheduleFormat.DaysOf(f.daysOfWeek, f.weeklyFlights);
                int report = ReportMinutesOf(f);
                int dep = DepartureMinutesOf(f);
                int shift = OpsRotationParams.DutyDayOf(report) - OpsRotationParams.DepartureDayOf(report, dep);
                AddWeeklyFlights(days, shift, FlightScheduleFormat.WeeklyOf(f), depCounts, dutyCounts);
            }
        }

        /// <summary>
        /// 把一个「班次列表条目」代表的每周班次按班期天数摊到周一~周日（离港日落在原星期，
        /// 值勤日按 <paramref name="dutyShift"/> 平移）。两侧只是**归组用的星期**不同，
        /// 每条班次在两边各计一次，故两侧周合计数**必然相等** —— 差异只体现在分布，不在总量。
        ///
        /// 一个条目可能代表多于一班/日：接管局的既有航线经 <see cref="SyncFlightsFromPrimary"/>
        /// 压成单条目后写的是「25 班/周 · 每日」，即 25 班摊在 7 天上。此时按 N/D 均摊、
        /// 余数分给靠前的几天（25/7 → 4,4,4,4,3,3,3）。若像旧实现那样「每天只 +1」，
        /// 值勤口径会常年少报（接管界面因此出现「离港25/值勤7班/周」这种量纲不等的数字）。
        /// </summary>
        static void AddWeeklyFlights(bool[] days, int dutyShift, int weeklyCount, int[] depCounts, int[] dutyCounts)
        {
            if (days == null) return;
            int dayCount = CountMarkedDays(days);
            if (dayCount <= 0 || weeklyCount <= 0 || (depCounts == null && dutyCounts == null)) return;
            int per = weeklyCount / dayCount;   // 每天的基数班次
            int rem = weeklyCount % dayCount;   // 余数：分给靠前的几天，保证合计精确等于 weeklyCount
            int seen = 0;
            for (int d = 0; d < 7; d++)
            {
                if (!days[d]) continue;
                int n = per + (seen < rem ? 1 : 0);
                seen++;
                if (n <= 0) continue;
                if (depCounts != null) depCounts[d] += n;
                if (dutyCounts != null) dutyCounts[((d + dutyShift) % 7 + 7) % 7] += n;
            }
        }

        static int CountMarkedDays(bool[] days)
        {
            int n = 0;
            for (int i = 0; i < days.Length; i++) if (days[i]) n++;
            return n;
        }

        /// <summary>
        /// 开航时间文案。接管局的既有航线写的是哨兵值 <see cref="OpenedBeforeTakeover"/>，**不是日期**；
        /// 若与「第 N 年 QX」共用同一句式（如统一写成「开通：X」），就会把非日期塞进日期位，含义不明。
        /// </summary>
        public static string OpenedText(string openedDate)
        {
            if (string.IsNullOrEmpty(openedDate)) return "开航时间未知";
            return openedDate == OpenedBeforeTakeover ? openedDate + "已开航" : openedDate + " 开航";
        }

        /// <summary>
        /// 每周班次一行式（航线卡片/呈报）。两侧合计恒等时只报一个数字；若真要并报，
        /// 每个数字各自带单位，避免「离港25/值勤7班/周」这种把两个不同量纲并列的写法。
        /// </summary>
        public string WeeklyLine()
        {
            int wd = WeeklyFlightsDeparture();
            if (wd <= 0) return "未排班";
            int wu = WeeklyFlightsDuty();
            return wd == wu ? $"{wd} 班/周" : $"离港 {wd} 班/周 · 值勤 {wu} 班/周";
        }

        /// <summary>可复现「接管界面航线卡片」那一行的自检文案（需求侧数字见 RouteGravityModel.FormatOneLine）。</summary>
        public string CardHeadline() => $"{OpenedText(openedDate)} · {WeeklyLine()}";

        /// <summary>R14：离港日与值勤日两套周班次（班期一并标出）。</summary>
        public string DualWeeklyLine()
        {
            int wd = WeeklyFlightsDeparture();
            int wu = WeeklyFlightsDuty();
            string depDays = FlightScheduleFormat.DaysSummary(WeekdayOpsDeparture());
            string dutyDays = FlightScheduleFormat.DaysSummary(WeekdayOpsDuty());
            // ★「每个执飞日」而不是「每日」：这半边说的是**有班的那天**各飞几班（per operating day），
            // 不是「天天都飞」。旧文案写「离港日 每日 1 班／周 4 班（周一/三/五/日）」，
            // 与同一行的「周 4 班（周一/三/五/日）」自相矛盾，CEO 当场读成「每日一班」
            // 并质问「为何又说只能排周一三五日」（2026-10-14 第九次报障）。
            return $"离港日 每个执飞日 {FlightCount} 班 · 周 {wd} 班（{depDays}）"
                 + $"　｜　值勤日 每个值勤期 {FlightCount} 班 · 周 {wu} 班（{dutyDays}）";
        }

        /// <summary>
        /// R4 双口径一行式说明（供单航线页与呈报）。同时给出两套数字，避免 CEO 看到对不上的数字。
        /// </summary>
        public string DutyAttributionLine()
        {
            int legs = (flights != null && flights.Count > 0) ? flights.Count : 1;
            var duties = DutyPeriods();
            bool crossNight = false;
            for (int i = 0; i < duties.Count && !crossNight; i++)
            {
                var d = duties[i];
                for (int k = 0; k < d.Count; k++)
                {
                    if (!OpsRotationParams.SameAttributionDay(ReportMinutesOf(d[k]), DepartureMinutesOf(d[k])))
                    { crossNight = true; break; }
                }
            }
            string tail = crossNight
                ? "（含跨夜班：值勤按报到日归组、航班量按离港日统计，两口径已分别标明）"
                : "（本期无跨夜班，两口径一致）";
            return $"口径说明　离港日 {legs} 班/日 · 周 {WeeklyFlightsDeparture()} 班　｜　值勤日 {duties.Count} 个值勤期 · 周 {WeeklyFlightsDuty()} 班{tail}";
        }
    }

    /// <summary>航线上的单班航班：一架飞机在一个时段执飞。</summary>
    [Serializable]
    public class RouteFlight
    {
        public string registration;
        public string depTime = "08:00";
        public string arrTime = "10:30";
        public string daysOfWeek = "每日";
        public int weeklyFlights = 7;
        public string slotLabel;    // 早班 / 午班 / 晚班 / 红眼
        public float dailyProfit;   // 该班次日利润（万美元/日）
        /// <summary>当前执飞是否为回程（去程落地后飞回出发地）。</summary>
        public bool inboundLeg;
        /// <summary>正在按本地时刻执行本班。</summary>
        public bool airborne;
        /// <summary>本班起飞的本地 DateTime.ToBinary()。</summary>
        public long departLocalTicks;
        /// <summary>上次起飞键 yyyyMMddHHmm，防重复。</summary>
        public string lastOpKey;
        /// <summary>
        /// R4 双口径：本班所在「值勤期」的报到时刻（当天分钟；值勤跨夜时可按 &gt;1440 接续到次日）。
        /// −1 = 未记录，按「本班起飞 − 报到提前量」自行推导。
        /// 值勤与机组按此归组；航班量（weeklyFlights）一律按 depTime 的离港日统计，两口径不混。
        /// </summary>
        public int dutyStart = -1;
    }

    /// <summary>航班环（两段连班）最小结构。</summary>
    [Serializable]
    public class FlightRing
    {
        public string ringId;
        public string registration;
        public string legARouteId;
        public string legBRouteId;
        public int turnMinutes = 60;
        public bool valid;
        public string validationNote;
    }

    [Serializable]
    public class RouteListSave
    {
        public RouteData[] items;
    }

    [Serializable]
    public class FlightRingListSave
    {
        public FlightRing[] items;
    }
}
