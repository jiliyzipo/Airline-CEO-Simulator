using System;
using System.Text;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航班时刻展示口径（单航线页 / 飞机详情页共用）：
    /// 班期解析、航班号（去程 / 回程）、ICAO 四字码、往返时刻与过站文案。
    /// 时刻与过站一律取自 LiveClockLogic，保证与引擎执行口径一致。
    /// </summary>
    public static class FlightScheduleFormat
    {
        static readonly string[] DayNamesZh = { "一", "二", "三", "四", "五", "六", "日" };
        static readonly string[] DayNamesEn = { "M", "T", "W", "T", "F", "S", "S" };

        /// <summary>班期字符串 → 周一~周日布尔（"每日"全周；"一三五"/"二四六"按字取）。</summary>
        public static bool[] DaysOf(string daysOfWeek, int weeklyFlights = 0)
        {
            var d = new bool[7];
            string s = daysOfWeek ?? "";
            if (s.Length == 0
                || s.IndexOf("每日", StringComparison.Ordinal) >= 0
                || s.IndexOf("每天", StringComparison.Ordinal) >= 0
                || s.IndexOf("天天", StringComparison.Ordinal) >= 0
                || s.IndexOf("全周", StringComparison.Ordinal) >= 0)
            {
                for (int i = 0; i < 7; i++) d[i] = true;
                return d;
            }
            bool any = false;
            for (int i = 0; i < 7; i++)
            {
                d[i] = s.IndexOf(DayNamesZh[i], StringComparison.Ordinal) >= 0;
                if (d[i]) any = true;
            }
            if (!any) for (int i = 0; i < 7; i++) d[i] = true;
            return d;
        }

        /// <summary>周班次数：优先取字段，缺失时按班期统计。</summary>
        public static int WeeklyOf(RouteFlight f)
        {
            if (f == null) return 0;
            if (f.weeklyFlights > 0) return f.weeklyFlights;
            var d = DaysOf(f.daysOfWeek);
            int n = 0;
            for (int i = 0; i < 7; i++) if (d[i]) n++;
            return n;
        }

        public static string DayMarkZh(int index)
            => index >= 0 && index < 7 ? DayNamesZh[index] : "";

        public static string DayMarkEn(int index)
            => index >= 0 && index < 7 ? DayNamesEn[index] : "";

        public static string DaysSummary(string daysOfWeek)
            => DaysSummary(DaysOf(daysOfWeek));

        /// <summary>周一~周日占用 → 「每日」或「周一/三/五」。</summary>
        public static string DaysSummary(bool[] d)
        {
            if (d == null || d.Length < 7) return "—";
            int n = 0;
            var sb = new StringBuilder();
            for (int i = 0; i < 7; i++)
            {
                if (!d[i]) continue;
                n++;
                if (sb.Length > 0) sb.Append('/');
                sb.Append(DayNamesZh[i]);
            }
            if (n == 7) return "每日";
            return sb.Length > 0 ? "周" + sb : "—";
        }

        /// <summary>按日操作次数 → 有班的星期。</summary>
        public static string DaysSummary(int[] counts)
        {
            var d = new bool[7];
            if (counts != null)
                for (int i = 0; i < 7 && i < counts.Length; i++)
                    d[i] = counts[i] > 0;
            return DaysSummary(d);
        }

        /// <summary>
        /// 每条航线可以预留的波次数（每天最多 4 班：早 / 午 / 晚 / 红眼，见 `ClampWaves`）。
        /// </summary>
        public const int WavesPerRoute = 4;

        /// <summary>
        /// ★每条航线预留的航班号**块大小 = 8**（4 个波次 × 去程/回程）。
        ///
        /// 2026-10-13 第六次报障（「周计划里 FRA-ATL 是 FR106、FRA-ORD 是 FR107，
        /// 而 FRA-ATL 单航线是 FR106/FR107」）的根因：航线的**基号是连号分配的**
        /// （`{航司码}{100 + i}`，i 是第几条线），而**回程号 = 去程号 + 1**、**每个波次再 +2**。
        /// 于是第 i 条线要占 `base … base+7`（4 波次），第 i+1 条线的基号 `base+1` 当场落进它的块里 ——
        /// 一条线只需 2 个数（1 个波次）时也会撞，多波次（周班次 ≥ 8，如 LHR 的 26 班/周 = 4 波）时更是整块重叠。
        ///
        /// 改法：**块与块之间按本常量错开**，各航线互不重叠（第一条第 0 个波次仍是 FR100）。
        /// 为什么必须留满 8 个号、而不是按当前波次数预留 2 个：时刻槽把航班号写进了持仓
        /// （`StrategySlot.usedByFlightNo`），号一变，那条持仓就认不出来 ——
        /// 故号必须**创建即定、此后再不动**，能加密到几班就得预留到几班。
        /// </summary>
        public const int BlockStride = WavesPerRoute * 2;

        /// <summary>块首号（第一条航线是 100）。</summary>
        public const int FirstBase = 100;

        /// <summary>第 blockIndex 条航线（从 0 起）的航班号块首，如 100 / 108 / 116 …</summary>
        public static int BaseNumberOf(int blockIndex)
            => FirstBase + (blockIndex < 0 ? 0 : blockIndex) * BlockStride;

        /// <summary>航班号的字母前缀（"FR106" → "FR"）；取不到返回空串。</summary>
        public static string PrefixOf(string flightNumber)
        {
            if (string.IsNullOrEmpty(flightNumber)) return "";
            int i = 0;
            while (i < flightNumber.Length && !char.IsDigit(flightNumber[i])) i++;
            return flightNumber.Substring(0, i);
        }

        /// <summary>航班号里的数字（"FR106" → 106）；取不到返回 -1。</summary>
        public static int NumberOf(string flightNumber)
        {
            string p = PrefixOf(flightNumber);
            if (p.Length >= (flightNumber?.Length ?? 0)) return -1;
            return int.TryParse(flightNumber.Substring(p.Length), out int n) ? n : -1;
        }

        /// <summary>
        /// 航班号：同一航线第 index 个班次 → 去程 / 回程两个号（相差 1）。
        /// 基准取 route.flightNumber，形如「SZ100」；无数字时用基地三字码 + 100 起编。
        /// </summary>
        public static void Numbers(RouteData route, int index, out string outbound, out string inbound)
        {
            string baseNo = route != null ? route.flightNumber : null;
            string prefix = PrefixOf(baseNo);
            int num = NumberOf(baseNo);
            if (num < 0)
            {
                prefix = string.IsNullOrEmpty(prefix)
                    ? (route != null && !string.IsNullOrEmpty(route.origin) ? route.origin : "NV")
                    : prefix;
                num = FirstBase;
            }
            if (index < 0) index = 0;
            outbound = $"{prefix}{num + index * 2}";
            inbound = $"{prefix}{num + index * 2 + 1}";
        }

        /// <summary>一天的分钟数（24:00）。</summary>
        public const int MinutesPerDay = 24 * 60;

        /// <summary>
        /// 一次往返的**四个时刻**（当天分钟；后面几段可 &gt; 1440 = 落到次日）：
        /// 去程起飞 → 去程落地（对方站）→ **回程起飞（= 去程落地 + 过站）** → 回程落地。
        ///
        /// 周视图要把一趟往返画成「去程段 / 过站段 / 回程段」，就得知道中间这两个点；
        /// 只有首尾两个点（`WindowMinutes`）时，屏幕上一整段 18 小时的长条里
        /// **看不出 50 分钟的过站在哪** —— 2026-10-14 第八次报障（「空余时间感觉都是在 ATL 过站，
        /// 而且超过了 50 分钟」）正是这么来的。
        /// </summary>
        public static void WindowTimes(RouteData route, RouteFlight flight,
            out int outDep, out int outArr, out int retDep, out int retArr)
        {
            outDep = outArr = retDep = retArr = 0;
            if (route == null || flight == null) return;
            var t = Trip(route, flight);
            outDep = LiveClockLogic.ParseMinutes(t.outDepFull);
            outArr = LiveClockLogic.ParseMinutes(t.outArrFull);
            retDep = LiveClockLogic.ParseMinutes(t.inDepFull);
            retArr = LiveClockLogic.ParseMinutes(t.inArrFull);
            // 逐段归一为单调递增（跨午夜就加一天）：过站只可能落在去程落地之后，
            // 回程落地只可能落在回程起飞之后 —— 任一段不单调即说明它跨过了 24:00。
            while (outArr <= outDep) outArr += MinutesPerDay;
            while (retDep < outArr) retDep += MinutesPerDay;
            while (retArr < retDep) retArr += MinutesPerDay;
        }

        /// <summary>一次往返的**真实占窗**（去程起飞 → 回程落地，当天分钟；`end` 可 &gt; 1440 = 跨午夜）。</summary>
        public static void WindowMinutes(RouteData route, RouteFlight flight, out int start, out int end)
        {
            WindowTimes(route, flight, out start, out _, out _, out end);
        }

        /// <summary>
        /// 把一段占窗拆成「当天主段」与「次日尾段」；不跨午夜则尾段为 0。
        ///
        /// ★**主段的 end 允许等于 1440**（画到坐标轴底）。旧版 `AsWeekGridUI.Place` 自己拆窗时
        /// 把主段的 end 传成 1440、又用 `end >= 1440 ? end - 1440 : end` 归一 → 主段变成 0 分钟被丢弃，
        /// 屏幕上**只剩次日凌晨的尾段**：周一/三/五/日出发的班，看着像周二/四/六/一在飞 ——
        /// 正是报障截图里星期圈错开一天的现象。
        ///
        /// `dayOffset`：这一段本身是否整个落在次日（去程 22:00 出发、回程次日 09:00 起飞时，
        /// 「回程段」的起点就 ≥ 1440）——那时整段平移一天，**不再**当作「当天主段 + 尾段」拆。
        /// </summary>
        public static void SplitWindow(int start, int end,
            out int dayOffset, out int mainStart, out int mainEnd, out int tailEnd)
        {
            dayOffset = 0; mainStart = start; mainEnd = end; tailEnd = 0;
            // 起点自身已过午夜：整段落在次日（或更后），平移一天后重新判定
            while (mainStart >= MinutesPerDay)
            {
                mainStart -= MinutesPerDay;
                mainEnd -= MinutesPerDay;
                dayOffset++;
            }
            if (mainEnd > MinutesPerDay)
            {
                tailEnd = mainEnd - MinutesPerDay;
                mainEnd = MinutesPerDay;
            }
            if (mainEnd < mainStart) mainEnd = mainStart;   // 起点已过轴底：不画主段，只留尾段
        }

        /// <summary>旧签名（不带次日偏移）：等价于 `dayOffset` 恒为 0 的调用方。</summary>
        public static void SplitWindow(int start, int end, out int mainStart, out int mainEnd, out int tailEnd)
            => SplitWindow(start, end, out _, out mainStart, out mainEnd, out tailEnd);

        /// <summary>
        /// 周视图的**行列依据**：本线班次的**执飞日**（= 周计划那行的班期，同口径）。
        ///
        /// ★**不要在这里把「回程跨午夜落地的次日」也算成有班**（2026-10-14 第九次报障）。
        /// 上一版这么做，本意是「尾段那天也有块，不标出来就像漏画」，结果 `Draw` 是按 `days[d]`
        /// **整天重绘整趟往返**的 —— 于是一趟 06:30 出发、次日 00:46 落地的班，在**周一至周日
        /// 七列上都画出了完整的往返**，屏幕上看着就是「每日一班」，CEO 当场质问
        /// 「这样排班可以排成每日一班，为何又说只能排周一三五日？」
        ///
        /// 正确做法：**列 = 执飞日**；跨过 24:00 的部分由各段自己按算术落到次日列
        /// （见 `AsWeekGridUI.DrawSpan` 与 `FlightScheduleFormat.SplitWindow`）——
        /// 06:30 那班在周二只留一条 46 分的细条（前一日回程落地的真实残留），
        /// 而 22:00 红眼那班次日整上午都在飞，就该整块画在周二。**是真是假由时刻算，不靠预标**。
        /// </summary>
        public static bool[] GridRowDays(RouteFlight flight)
            => DaysOf(flight != null ? flight.daysOfWeek : null);

        /// <summary>「06:30-00:46+1」式往返时刻（+1 = 回程次日落地）；不跨夜即「08:00-08:46」。</summary>
        public static string WindowLabel(RouteData route, RouteFlight flight)
        {
            if (route == null || flight == null) return "—";
            WindowMinutes(route, flight, out int s, out int e);
            string tail = e >= MinutesPerDay ? "+1" : "";
            return $"{ScheduleSlots.FormatClock(s)}-{ScheduleSlots.FormatClock(e % MinutesPerDay)}{tail}";
        }

        /// <summary>
        /// 全局排班自检：把**周计划那一行**（航线级 班期/周班/时刻）与**单航线页**（班次级）逐条对账。
        /// 返回问题清单；空 = 两处口径一致。供 `slotcheck R31` / `LogicSelfTests R31` 与呈报共用。
        /// </summary>
        public static System.Collections.Generic.List<string> AuditSchedule(System.Collections.Generic.IList<RouteData> routes)
        {
            var issues = new System.Collections.Generic.List<string>();
            if (routes == null) return issues;
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (r == null) continue;
                r.EnsureFlights();
                string od = $"{r.origin}—{r.destination}";
                var flights = r.flights;
                if (flights == null || flights.Count == 0)
                {
                    issues.Add($"{od}：无班次（周计划却列出班期/时刻）");
                    continue;
                }
                if (!string.Equals(r.depTime, flights[0].depTime, System.StringComparison.Ordinal))
                    issues.Add($"{od}：周计划时刻 {r.depTime} ≠ 单航线首班 {flights[0].depTime}");

                var union = new bool[7];
                int weeklySum = 0;
                for (int k = 0; k < flights.Count; k++)
                {
                    var f = flights[k];
                    if (f == null) continue;
                    if (string.IsNullOrEmpty(f.daysOfWeek))
                        issues.Add($"{od} 第{k + 1}班：班期为空（按兜底显示成每日）");
                    var dk = DaysOf(f.daysOfWeek);
                    int wk = 0;
                    for (int d = 0; d < 7; d++) { union[d] |= dk[d]; if (dk[d]) wk++; }
                    if (f.weeklyFlights > 0 && f.weeklyFlights != wk)
                        issues.Add($"{od} 第{k + 1}班：周班 {f.weeklyFlights} ≠ 班期天数 {wk}");
                    weeklySum += f.weeklyFlights > 0 ? f.weeklyFlights : wk;
                }
                var rdays = DaysOf(r.daysOfWeek);
                for (int d = 0; d < 7; d++)
                    if (rdays[d] != union[d])
                    {
                        issues.Add($"{od}：周计划班期 {DaysSummary(rdays)} ≠ 各班班期并集 {DaysSummary(union)}");
                        break;
                    }
                if (r.weeklyFlights > 0 && r.weeklyFlights != weeklySum)
                    issues.Add($"{od}：周计划周班 {r.weeklyFlights} ≠ 各班周班之和 {weeklySum}");
            }
            return issues;
        }

        /// <summary>一次往返（去程 + 过站 + 回程）的时刻与轮挡。</summary>
        public struct RoundTrip
        {
            public string outDep;
            public string outArr;
            public string inDep;
            public string inArr;
            public int blockMinutes;    // 去程（≈回程）轮挡
            public int turnMinutes;     // 过站
            public int cycleMinutes;    // 去程起飞 → 回程落地
            public string outDepFull;   // HH:mm
            public string outArrFull;
            public string inDepFull;
            public string inArrFull;
        }

        public static RoundTrip Trip(RouteData route, RouteFlight flight)
        {
            var t = new RoundTrip();
            if (route == null || flight == null) return t;
            var day = new DateTime(2026, 10, 1);   // 仅用于取时刻，日期无意义
            LiveClockLogic.RoundTripWindow(day, flight.depTime, flight.arrTime,
                route.distanceKm > 0 ? route.distanceKm : 1000f,
                route.aircraftType, route.groundStaffAssigned,
                out DateTime od, out DateTime oa, out DateTime rd, out DateTime ra);

            t.outDepFull = $"{od:HH:mm}";
            t.outArrFull = $"{oa:HH:mm}";
            t.inDepFull = $"{rd:HH:mm}";
            t.inArrFull = $"{ra:HH:mm}";
            t.outDep = flight.depTime;
            t.outArr = flight.arrTime;
            t.inDep = $"{rd:HH:mm}";
            t.inArr = $"{ra:HH:mm}";
            t.blockMinutes = LiveClockLogic.BlockMinutes(flight.depTime, flight.arrTime,
                route.distanceKm > 0 ? route.distanceKm : 1000f);
            t.turnMinutes = LiveClockLogic.TurnMinutes(route.aircraftType, route.groundStaffAssigned);
            t.cycleMinutes = (int)Math.Round((ra - od).TotalMinutes);
            return t;
        }

        /// <summary>「2 小时 40 分」式时长文案。</summary>
        public static string Duration(int minutes)
        {
            if (minutes <= 0) return "—";
            int h = minutes / 60;
            int m = minutes % 60;
            if (h <= 0) return $"{m} 分";
            if (m == 0) return $"{h} 小时";
            return $"{h} 小时 {m} 分";
        }

        /// <summary>「2:40」式时长文案（表格式紧凑展示）。</summary>
        public static string DurationShort(int minutes)
        {
            if (minutes <= 0) return "—";
            return $"{minutes / 60}:{minutes % 60:00}";
        }

        /// <summary>ICAO 四字码（目录未收录时返回空串）。</summary>
        public static string Icao(string iata)
        {
            if (string.IsNullOrEmpty(iata)) return "";
            switch (iata.ToUpperInvariant())
            {
                case "PEK": return "ZBAA";
                case "PKX": return "ZBAD";
                case "SHA": return "ZSSS";
                case "PVG": return "ZSPD";
                case "CAN": return "ZGGG";
                case "SZX": return "ZGSZ";
                case "CTU": return "ZUUU";
                case "TFU": return "ZUTF";
                case "CKG": return "ZUCK";
                case "XIY": return "ZLXY";
                case "KMG": return "ZPPP";
                case "HGH": return "ZSHC";
                case "NKG": return "ZSNJ";
                case "WUH": return "ZHHH";
                case "CSX": return "ZGHA";
                case "XMN": return "ZSAM";
                case "FOC": return "ZSFZ";
                case "TAO": return "ZSQD";
                case "TSN": return "ZBTJ";
                case "DLC": return "ZYTL";
                case "SHE": return "ZYTX";
                case "HRB": return "ZYHB";
                case "URC": return "ZWWW";
                case "LHW": return "ZLLL";
                case "SYX": return "ZJSY";
                case "HAK": return "ZJHK";
                case "HKG": return "VHHH";
                case "MFM": return "VMMC";
                case "TPE": return "RCTP";
                case "ICN": return "RKSI";
                case "NRT": return "RJAA";
                case "HND": return "RJTT";
                case "KIX": return "RJBB";
                case "SIN": return "WSSS";
                case "BKK": return "VTBS";
                case "KUL": return "WMKK";
                case "LHR": return "EGLL";
                case "CDG": return "LFPG";
                case "FRA": return "EDDF";
                case "AMS": return "EHAM";
                case "LAX": return "KLAX";
                case "SFO": return "KSFO";
                case "JFK": return "KJFK";
                case "ORD": return "KORD";
                case "SYD": return "YSSY";
                case "DXB": return "OMDB";
                case "DOH": return "OTHH";
                case "IST": return "LTFM";
                case "MAD": return "LEMD";
                case "BCN": return "LEBL";
                case "FCO": return "LIRF";
                case "MUC": return "EDDM";
                case "ZRH": return "LSZH";
                case "VIE": return "LOWW";
                case "CPH": return "EKCH";
                case "ARN": return "ESSA";
                case "HEL": return "EFHK";
                case "SVO": return "UUEE";
                case "DEL": return "VIDP";
                case "BOM": return "VABB";
                case "CGK": return "WIII";
                case "MNL": return "RPLL";
                default: return "";
            }
        }

        /// <summary>「北京首都 PEK / ZBAA」式过站机场文案（无 ICAO 时略去）。</summary>
        public static string AirportWithIcao(string iata)
        {
            if (string.IsNullOrEmpty(iata)) return "—";
            string icao = Icao(iata);
            return string.IsNullOrEmpty(icao)
                ? AirportCatalog.Label(iata)
                : $"{AirportCatalog.Label(iata)} / {icao}";
        }
    }
}
