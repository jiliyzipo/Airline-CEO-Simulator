using System;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航线准入（Phase 18）：现实里 CEO 批准开航计划后，公司还不能开飞，
    /// 必须向主管机构办完「航线经营许可 / 航权额度 / 航班时刻」并过公示期。
    ///
    /// 本类只做判定与取值（不改状态）；状态推进与邮件在 InboxManager 里。
    /// - 国内航线：地区管理局「航线经营许可」+ 协调机场时刻
    /// - 国际/地区航线：先占双边航权额度，再办国际航线经营许可 + 对方许可 + 对方机场时刻
    /// </summary>
    public static class RoutePermitLogic
    {
        public const int StageNone = 0;      // 未受理
        public const int StageReview = 1;    // 已受理 · 审查中
        public const int StageNotice = 2;    // 公示期（可能被竞对异议）
        public const int StageGranted = 3;   // 已发证
        public const int StageDenied = 4;    // 未获批

        /// <summary>港澳台按地区航线处理（需航权/许可）。</summary>
        static readonly string[] RegionCodes = { "HKG", "MFM", "TPE" };

        public static string StageName(int stage) => stage switch
        {
            StageReview => "审查中",
            StageNotice => "公示期",
            StageGranted => "已发证",
            StageDenied => "未获批",
            _ => "未受理"
        };

        /// <summary>是否国际/地区航线：任一端点在境外，或为港澳台。</summary>
        public static bool IsInternational(string origin, string dest)
        {
            return Outside(origin) || Outside(dest) || IsRegion(origin) || IsRegion(dest);
        }

        static bool IsRegion(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            string c = code.Trim().ToUpperInvariant();
            for (int i = 0; i < RegionCodes.Length; i++)
                if (c == RegionCodes[i]) return true;
            return false;
        }

        static bool Outside(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            if (AirlineCEO.UI.MapWidget.TryLonLat(code, out float lon, out float lat))
                return AirlineCEO.UI.CoordConverter.OutOfChina(lon, lat);
            return false;
        }

        /// <summary>是否基地始发（航线双向等价）。</summary>
        public static bool FromHub(string origin, string dest) => RouteRightManager.IsFromHomeBase(origin, dest);

        /// <summary>审查 + 公示所需季度数：国际/地区更长；重申一次加一季。</summary>
        public static int ReviewQuarters(bool international, int attempt)
        {
            int q = international ? 2 : 1;
            q += Mathf.Clamp(attempt - 1, 0, 2);
            return q;
        }

        /// <summary>是否需要占用时刻槽：国际/地区航线，或基地始发的干线。</summary>
        public static bool NeedsSlot(string origin, string dest, bool international, bool fromHub)
            => international || fromHub;

        /// <summary>可用（未占用）时刻槽数量。</summary>
        public static int AvailableSlots(GameState gs)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return 0;
            int n = 0;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s != null && !s.inUse) n++;
            }
            return n;
        }

        #region P1 时刻槽池（按机场等级 × 时段推导容量）

        /// <summary>
        /// 一个往返班次在**某一停站**的时刻槽需求。
        ///
        /// ★2026-10-16 口径更正（CEO 第十八次问询）：**一条持仓 = 该停站本次停留的一对
        /// 「到场 + 离场」**（现实里 coordinators 台账上是一次 landing + 一次 take-off）。
        /// 因此：
        /// <list type="bullet">
        /// <item>本场端（去程始发 + 回程落地，一次停留）→ 1 条；</item>
        /// <item>对端（到场 + 过站 + 回程离场，一次停留）→ 1 条；</item>
        /// <item>**经停端（去程落一次、回程再落一次，两次停留）→ 2 条**。</item>
        /// </list>
        /// 旧注释把本端写成「离港端」，等于把一整对讲成了半条 —— 这正是「只有起飞机场的时刻槽」
        /// 那种读法的源头（WASG 1.6.1：slot 是 arrive **or** depart 的许可，两端都要）。
        /// </summary>
        public struct SlotNeed
        {
            public string airport;    // 该停站机场
            /// <summary>该站本次停留的**离场时刻** `"HH:mm"`（★2026-10-15：时刻槽的身份）。</summary>
            public string time;
            /// <summary>★2026-10-16：该站本次停留的**到场时刻** `"HH:mm"`（同一对里的 landing）。</summary>
            public string arrTime;
            public int band;          // 该站离场时刻所在时段（0 早 / 1 中 / 2 晚 / 3 夜），仅供池容量归集
            public string routeId;
            public string flightNo;   // 去程号 / 回程号
            public bool inbound;      // true = 该停留发生在回程方向
            /// <summary>停站键：<c>"origin"</c> / <c>"via"</c> / <c>"dest"</c>（见 <see cref="StationOrigin"/> 等）。</summary>
            public string station;
        }

        /// <summary>停站键：本场端。</summary>
        public const string StationOrigin = "origin";
        /// <summary>停站键：经停端。</summary>
        public const string StationVia = "via";
        /// <summary>停站键：对端。</summary>
        public const string StationDest = "dest";

        /// <summary>
        /// 该需求属于哪一停站的**全称**（文案用）。★2026-10-16 改名：从「去程离港 / 回程离港」
        /// 改成「本场端 / 经停端（去程）/ 经停端（回程）/ 对端」—— 见 <see cref="SlotNeed"/> 的口径说明。
        /// </summary>
        public static string LegName(SlotNeed need) => LegName(need.station, need.inbound);

        /// <summary>停站键 + 方向 → 停站全称（文案用）。见 <see cref="SlotNeed"/> 的口径说明。</summary>
        public static string LegName(string station, bool inbound)
        {
            if (station == StationVia)
                return inbound ? "经停端（回程）" : "经停端（去程）";
            if (station == StationDest) return "对端";
            if (station == StationOrigin) return "本场端";
            // 未标停站的旧口径需求：按方向兜底，不得退回「离港端」这种半条说法
            return inbound ? "对端" : "本场端";
        }

        /// <summary>
        /// 该停站本次停留的**一对时刻**文案：`到场 07:15 · 离场 08:15`（缺失侧写「—」）。
        /// 槽是「一次到场 or 一次离场」的许可，明细里必须成对列出 —— 只写离场就等于只说了半条。
        /// </summary>
        public static string LegClocks(SlotNeed need)
        {
            string a = string.IsNullOrEmpty(need.arrTime) ? "—" : ScheduleSlots.FormatClock(ParseMinutesSafe(need.arrTime));
            string d = string.IsNullOrEmpty(need.time) ? "—" : ScheduleSlots.FormatClock(ParseMinutesSafe(need.time));
            return $"到场 {a} · 离场 {d}";
        }

        /// <summary>`"HH:mm"` → 分钟；非法返回 −1。</summary>
        static int ParseMinutesSafe(string clock) => ScheduleSlots.ParseClock(clock);

        /// <summary>
        /// 本航线的**经停机场**（大写 IATA；无经停或与两端重合时返回 null ——
        /// 与两点重合的「经停」是同一次落地，不是第三站）。
        /// </summary>
        public static string StopoverOf(RouteData route)
        {
            if (route == null) return null;
            return StopoverOf(route.stopover, route.origin, route.destination);
        }

        /// <summary>
        /// 同上，但拿**裸字符串**判（开航许可件 `InboxMail.stopover` 在航线建档之前就要用）：
        /// 与两端重合的「经停」是同一次落地，不是第三站，故仍返回 null。
        /// </summary>
        public static string StopoverOf(string stopover, string origin, string dest)
        {
            if (string.IsNullOrWhiteSpace(stopover)) return null;
            string v = stopover.Trim().ToUpperInvariant();
            if (v.Length == 0) return null;
            if (string.Equals(v, origin, System.StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(v, dest, System.StringComparison.OrdinalIgnoreCase)) return null;
            return v;
        }

        /// <summary>
        /// 一趟往返的**完整到站时刻表**（分钟，未取模；≥1440 = 次日）。
        ///
        /// 有经停时，去程是「本场 → 经停 → 对端」两段，回程同理，中间在经停站各停
        /// <see cref="OpsRotationParams.StopoverGroundMinutes"/> 分钟。轮挡总数取自班期跨度
        /// （已含停站时间），两段按大圆距离比例分摊 —— 见 <see cref="RoundTripOf"/>。
        /// </summary>
        public struct RoundTripLine
        {
            public bool hasVia;
            public int depOrigin;    // 本场离场（去程起飞）
            public int arrViaOut;    // 经停站到场（去程）
            public int depViaOut;    // 经停站离场（去程）
            public int arrDest;      // 对端到场
            public int depDest;      // 对端离场（回程起飞）
            public int arrViaIn;     // 经停站到场（回程）
            public int depViaIn;     // 经停站离场（回程）
            public int arrOrigin;    // 本场到场（回程落地）
        }

        /// <summary>
        /// 由「本场离场时刻 + 该班期跨度」推出整趟往返的到站时刻表（**单一权威**）。
        ///
        /// 轮挡取 <see cref="LiveClockLogic.BlockMinutes"/>（班期跨度优先，否则按距离估），
        /// 其已包含经停停站时间；有经停时按 `本场→经停` 与 `经停→对端` 的大圆距离比例
        /// 把空中时间分摊为两段，停站时间不参与分摊。
        /// </summary>
        public static RoundTripLine RoundTripOf(RouteData route, string depClock, string arrClock)
        {
            var line = new RoundTripLine();
            line.depOrigin = -1;
            if (route == null) return line;
            int dep = ScheduleSlots.ParseClock(depClock);
            if (dep < 0) return line;

            int span = LiveClockLogic.BlockMinutes(depClock, arrClock, route.distanceKm);
            if (span < 45) span = 45;
            int turn = LiveClockLogic.TurnMinutes(route.aircraftType, route.groundStaffAssigned);
            string via = StopoverOf(route);

            line.hasVia = via != null;
            line.depOrigin = dep;
            if (via == null)
            {
                line.arrDest = dep + span;
                line.depDest = line.arrDest + turn;
                line.arrOrigin = line.depDest + span;
                return line;
            }

            int stop = OpsRotationParams.StopoverGroundMinutes;
            int air = span - stop;
            if (air < 45) air = 45;                 // 班期跨度短到装不下一次停站时，空中先保底
            float d1 = AirlineCEO.UI.MapWidget.GreatCircleKm(route.origin, via);
            float d2 = AirlineCEO.UI.MapWidget.GreatCircleKm(via, route.destination);
            int s1 = SectorShare(air, d1, d1 + d2);
            int s2 = air - s1;

            line.arrViaOut = dep + s1;
            line.depViaOut = line.arrViaOut + stop;
            line.arrDest = line.depViaOut + s2;
            line.depDest = line.arrDest + turn;
            line.arrViaIn = line.depDest + s2;
            line.depViaIn = line.arrViaIn + stop;
            line.arrOrigin = line.depViaIn + s1;
            return line;
        }

        /// <summary>按距离比例分摊空中时间；两段大圆都为 0（同一点）时对半。</summary>
        static int SectorShare(int airMinutes, float dist, float total)
        {
            if (total <= 0f) return airMinutes / 2;
            int a = (int)System.Math.Round(airMinutes * (dist / total));
            if (a < 20) a = 20;                     // 单段下限：任何一段都不是瞬移
            if (a > airMinutes - 20) a = airMinutes - 20;
            return a < 0 ? 0 : a;
        }

        /// <summary>
        /// 一架飞机一趟往返的**各站停留记录**（每一站停一次 = 一条）。
        ///
        /// 这是「本趟往返在哪些站各停了一次、到场与离场分别是什么时刻」的**单一来源**：
        /// 槽需求、槽使用率台账、明细文案与自测全部由它派生，不许任何一处另算一遍。
        ///
        /// 一趟往返的停留条数：无经停 2 条（本场端 + 对端）；有经停 4 条
        /// （本场端 + 经停端·去程 + 对端 + 经停端·回程）——**经停站一次往返要停两次，
        /// 因此要办两对槽**，这是它与另两端唯一的区别。
        ///
        /// 每条记录自带该次停留的到场与离场两个时刻（本场端的到场 = 回程落地，
        /// 它与本场端的离场（去程起飞）构成同一对）。
        /// </summary>
        public struct StationCall
        {
            public string airport;
            public string station;   // origin / via / dest
            public bool inbound;     // 该次停留的**离场**是否属于回程航班号
            public int arr;          // 到场分钟（未取模）
            public int dep;          // 离场分钟（未取模）
        }

        public static System.Collections.Generic.List<StationCall> StationCalls(RouteData route, string depClock, string arrClock)
        {
            var calls = new System.Collections.Generic.List<StationCall>();
            if (route == null) return calls;
            var line = RoundTripOf(route, depClock, arrClock);
            if (line.depOrigin < 0) return calls;
            string via = StopoverOf(route);

            calls.Add(new StationCall
            {
                airport = route.origin, station = StationOrigin, inbound = false,
                arr = line.arrOrigin, dep = line.depOrigin
            });
            if (via != null)
                calls.Add(new StationCall
                {
                    airport = via, station = StationVia, inbound = false,
                    arr = line.arrViaOut, dep = line.depViaOut
                });
            calls.Add(new StationCall
            {
                airport = route.destination, station = StationDest, inbound = true,
                arr = line.arrDest, dep = line.depDest
            });
            if (via != null)
                calls.Add(new StationCall
                {
                    airport = via, station = StationVia, inbound = true,
                    arr = line.arrViaIn, dep = line.depViaIn
                });
            return calls;
        }

        /// <summary>
        /// 逐条停留记录生成槽需求：**一条停留 = 一条需求（到场 + 离场成对）**。
        /// 只列**协调机场**的停留（非协调机场不需要槽，列进来只会让缺口清单凭空多几行）。
        /// </summary>
        public static System.Collections.Generic.List<SlotNeed> NeedsFromCalls(RouteData route,
            System.Collections.Generic.List<StationCall> calls, string outboundNo, string inboundNo)
        {
            var list = new System.Collections.Generic.List<SlotNeed>();
            if (route == null || calls == null) return list;
            for (int i = 0; i < calls.Count; i++)
            {
                var c = calls[i];
                if (!NeedsDepartureSlot(c.airport)) continue;
                string dep = ScheduleSlots.FormatClock(Mod24(c.dep));
                string arr = ScheduleSlots.FormatClock(Mod24(c.arr));
                list.Add(new SlotNeed
                {
                    airport = c.airport,
                    time = dep,
                    arrTime = arr,
                    band = ScheduleSlots.BandOfDep(dep),
                    routeId = route.routeId,
                    flightNo = c.inbound ? inboundNo : outboundNo,
                    inbound = c.inbound,
                    station = c.station
                });
            }
            return list;
        }

        static int Mod24(int minutes) => ((minutes % (24 * 60)) + 24 * 60) % (24 * 60);

        /// <summary>该端是去程还是回程（文案用）。</summary>
        public static string DirectionName(SlotNeed need) => need.inbound ? "回程" : "去程";

        /// <summary>
        /// 槽需求的具体时刻。★2026-10-15：若调用点只给了时段（`time` 为空），按该时段的**峰点**折算 ——
        /// 时刻槽精确到时刻之后，时段只能当「峰点」这一个时刻来用，不能再当成一条槽的身份。
        ///
        /// 返回值一律经 <see cref="ScheduleSlots.NearestSeries"/> 归到**系列时刻**（见该函数的说明）：
        /// 经停端/对端的离场时刻是推导出来的（12:00 去程 → 17:33 回程），机场没有 17:33 的系列，
        /// 航司也不会为对齐槽去改轮挡 —— 现实里是把过站时刻按获批槽时刻排定，故需求时刻
        /// 必须落在系列网格上（17:33 → 17:30）。匹配/预约/文案全走本函数，口径只有一个。
        /// </summary>
        public static string TimeOf(SlotNeed need)
        {
            int m = ScheduleSlots.ParseClock(need.time);
            if (m >= 0)
                return ScheduleSlots.NearestSeries(ScheduleSlots.FormatClock(m));
            int b = need.band < 0 ? 0 : need.band > 3 ? 3 : need.band;
            return ScheduleSlots.DepTimes[b];
        }

        /// <summary>
        /// 把「想要的那一刻」折成**可以申请/购入的那一刻**（★2026-10-15）。
        ///
        /// 时刻槽的身份是系列时刻（该波窗口的 30 分钟网格）；而排班器产出的离港时刻是 **5 分钟粒度**的
        /// （飞机赶不上 07:30 的槽 → 实际 07:40 起飞），**07:40 不是任何机场会公布的系列**。
        /// 旧版把 `WaveShortage.dep` 原样当成申请时刻，于是「飞机晚于槽」那一类缺口会开出一张
        /// **非系列时刻的申请单**（申请 07:40）—— 交割回来的槽永远不落在 07:40，这张单买不到东西。
        ///
        /// 故凡是要**写进申请单/购入单/交割单**的时刻，一律先过本函数吸附到最近的系列时刻；
        /// 班次表里的实际离港时刻不受影响（它就该是 07:40，容差 15 分内仍由 07:30 的槽服务）。
        /// </summary>
        public static string SlotRequestClock(string clock)
            => ScheduleSlots.NearestSeries(clock);

        /// <summary>
        /// 回程离港时刻（当天分钟）：去程轮挡 + 过站后从对端起飞，可跨夜。
        ///
        /// ⚠️ 本重载的 `outArr` **必须已含经停停站时间**（即班期跨度本身）。有经停的航线请用
        /// <see cref="ReturnDepMinutes(RouteData, string, string)"/> —— 那个走 <see cref="RoundTripOf"/>
        /// 的完整到站时刻表，不会漏掉停站那一段。
        /// </summary>
        public static int ReturnDepMinutes(string outDep, string outArr, float distanceKm,
            string aircraftType, int groundStaff)
        {
            int block = LiveClockLogic.BlockMinutes(outDep, outArr, distanceKm);
            int turn = LiveClockLogic.TurnMinutes(aircraftType, groundStaff);
            int m = LiveClockLogic.ParseMinutes(outDep) + block + turn;
            return ((m % (24 * 60)) + 24 * 60) % (24 * 60);
        }

        /// <summary>
        /// 回程离港时刻（当天分钟）—— **权威重载**：由 <see cref="RoundTripOf"/> 的到站时刻表取出，
        /// 经停航线的停站时间天然包含在内（一个口径，不另算一遍）。
        /// </summary>
        public static int ReturnDepMinutes(RouteData route, string depClock, string arrClock)
        {
            var line = RoundTripOf(route, depClock, arrClock);
            if (line.depOrigin < 0) return -1;
            int m = line.depDest % (24 * 60);
            return ((m % (24 * 60)) + 24 * 60) % (24 * 60);
        }

        /// <summary>本航线某去程时段的回程离港时段（尚无具体班次时按代表时刻预估，供开工前预检）。</summary>
        public static int ReturnBandOf(RouteData route, int outBand)
        {
            string clock = ReturnTimeOf(route, outBand);
            return ScheduleSlots.BandOfDep(clock);
        }

        /// <summary>
        /// 本航线某去程时段的回程离港**时刻**（按代表时刻预估，供开工前预检）。
        ///
        /// ★2026-10-16（第十八次修订）：探测时刻的到场一侧改由
        /// <see cref="ProbeArrivalClock"/> 按**时长**折算（`离场 + 单程轮挡`）。
        /// 旧版把 `route.arrTime`（**首班**的到场钟点）当作探测时刻的对端钟点喂给
        /// <see cref="RoundTripOf"/>，`LiveClockLogic.BlockMinutes` 会把二者之差当成跨夜环绕差，
        /// 于是回程刻**不随波次走**（整条线只有一个对端刻 = 首班到场 + 过站），
        /// 与排班器 `OpsRotationLogic.WindowOf` 的口径对不上 —— 见 `ProbeArrivalClock` 的说明。
        /// </summary>
        public static string ReturnTimeOf(RouteData route, int outBand)
        {
            int b = outBand < 0 ? 0 : outBand > 3 ? 3 : outBand;
            string dep = ScheduleSlots.DepTimes[b];
            if (route == null) return dep;
            int m = ReturnDepMinutes(route, dep, ProbeArrivalClock(route, dep));
            if (m < 0) return dep;
            return ScheduleSlots.FormatClock(m);
        }

        /// <summary>
        /// 单个班次的**各站槽需求**（一停站一对；经停站两对）。
        /// ★2026-10-16：由 <see cref="StationCalls"/> 派生 —— 经停站过去**一条都不开**。
        /// </summary>
        public static System.Collections.Generic.List<SlotNeed> FlightNeeds(RouteData route, int flightIndex)
        {
            var list = new System.Collections.Generic.List<SlotNeed>();
            if (route == null) return list;
            route.EnsureFlights();
            if (flightIndex < 0 || flightIndex >= route.flights.Count) return list;
            var f = route.flights[flightIndex];
            if (f == null) return list;

            FlightScheduleFormat.Numbers(route, flightIndex, out string outbound, out string inbound);
            string dep = string.IsNullOrEmpty(f.depTime) ? ScheduleSlots.DepTimes[0] : f.depTime;
            return NeedsFromCalls(route, StationCalls(route, dep, f.arrTime), outbound, inbound);
        }

        /// <summary>本航线全部班次的各站槽需求。</summary>
        public static System.Collections.Generic.List<SlotNeed> SlotNeeds(RouteData route)
        {
            var list = new System.Collections.Generic.List<SlotNeed>();
            if (route == null) return list;
            route.EnsureFlights();
            for (int i = 0; i < route.flights.Count; i++)
                list.AddRange(FlightNeeds(route, i));
            return list;
        }

        /// <summary>
        /// 「拟排某一刻」时，**该班去程的到场时刻**：`离场时刻 + 单程轮挡`（把轮挡当**时长**用）。
        ///
        /// ★2026-10-16（第十八次问询，报障「落地机场的槽一次都没被提起」的**最后一道成因**）：
        /// 探测时刻**不能**把 `route.arrTime` 当成对端钟点喂给 <see cref="StationCalls"/> ——
        /// `route.arrTime` 是**首班**的到场钟点（PEK–SHA 首班 06:30→07:46），而增班探测的时刻常晚于它
        /// （08:00 / 12:00 / 18:00 / 22:00），`LiveClockLogic.BlockMinutes` 会把二者之差当成**跨夜**（+1440 分），
        /// 于是推出的对端停留时刻荒谬到**到场早于离场**（到场 13:30 · 离场 14:00），
        /// 且**对每一波都是同一个值** —— 它恰好落在某个已排班次的停留上时，
        /// <see cref="RouteHasTime"/> 就判「该端已有历史优先权（祖父条款）」→
        /// <see cref="CanApplyEnd"/> 为 false → **落地机场永远开不出申请单**。
        ///
        /// 单程轮挡以 <see cref="OpsRotationLogic.LegBlockMinutes"/> 为准：排班窗口
        /// （`OpsRotationLogic.WindowOf` / `BlockOf` / `CurfewBlocked`）用的就是它，
        /// 闸门与排班自此同口径 —— 闸门说「这一波两端缺槽」时，排班器算出的对端停留就是同一刻。
        /// </summary>
        public static string ProbeArrivalClock(RouteData route, string depClock)
        {
            if (route == null) return null;
            int dep = ScheduleSlots.ParseClock(depClock);
            if (dep < 0) return null;
            return ScheduleSlots.FormatClock(dep + OpsRotationLogic.LegBlockMinutes(route));
        }

        /// <summary>拟排一个去程时段时的各站槽需求（按该时段代表时刻预估，供开工前预检）。</summary>
        public static System.Collections.Generic.List<SlotNeed> BandNeeds(RouteData route, int outBand)
        {
            if (route == null) return new System.Collections.Generic.List<SlotNeed>();
            int b = outBand < 0 ? 0 : outBand > 3 ? 3 : outBand;
            string depTime = ScheduleSlots.DepTimes[b];
            return NeedsFromCalls(route, StationCalls(route, depTime, ProbeArrivalClock(route, depTime)), null, null);
        }

        /// <summary>拟排一个**具体时刻**时的各站槽需求（★2026-10-15：时刻槽精确到时刻后新增）。</summary>
        public static System.Collections.Generic.List<SlotNeed> ClockNeeds(RouteData route, string depClock)
        {
            var empty = new System.Collections.Generic.List<SlotNeed>();
            if (route == null) return empty;
            int m = ScheduleSlots.ParseClock(depClock);
            if (m < 0) return empty;
            string dep = ScheduleSlots.FormatClock(m);
            return NeedsFromCalls(route, StationCalls(route, dep, ProbeArrivalClock(route, dep)), null, null);
        }

        /// <summary>
        /// 某一波**实际要办的各站槽需求**：一对槽的两个时刻一律吸附到系列网格
        /// （<see cref="SlotRequestClock"/>），与排班闸门、申请单上的时刻保持同一口径。
        ///
        /// ★2026-10-16：抽出来是因为「该开几张申请单」（`InboxManager.QueueSlotApplications`）与
        /// 「呈报里该列哪几站缺槽」（`CapacityDirectiveLogic.BuildSlotSection`）必须问同一件事 ——
        /// 两处各写一份，就会出现「正文写停站、清单只列一端、单子只开一张」这种自相矛盾。
        /// 本函数天然覆盖**经停端**（`ClockNeeds` 已含它、且经停站一趟往返两条），
        /// 因此「缺几站办几张单」在有无经停的航线上是同一句话。
        /// </summary>
        public static System.Collections.Generic.List<SlotNeed> WaveEndNeeds(RouteData route, string waveClock)
        {
            var list = new System.Collections.Generic.List<SlotNeed>();
            if (route == null) return list;
            string clk = SlotRequestClock(waveClock);
            if (ScheduleSlots.ParseClock(clk) < 0) return list;
            var raw = ClockNeeds(route, clk);
            for (int i = 0; i < raw.Count; i++)
            {
                var n = raw[i];
                n.time = SlotRequestClock(n.time);
                if (!string.IsNullOrEmpty(n.arrTime)) n.arrTime = SlotRequestClock(n.arrTime);
                n.band = ScheduleSlots.BandOfDep(n.time);
                list.Add(n);
            }
            return list;
        }

        /// <summary>该机场该时段的时刻池总容量（由机场等级推导，枢纽 &gt; 干线 &gt; 支线）。</summary>
        public static int SlotPoolCapacity(string airport, int band)
            => AirlineCEO.Data.AirportCatalog.SlotCapacity(airport, band);

        /// <summary>该机场该时段已持有的时刻槽数（含已用与未用）。时段由持仓时刻反推。</summary>
        public static int SlotsHeld(GameState gs, string airport, int band)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return 0;
            int b = band < 0 ? 0 : band > 3 ? 3 : band;
            int n = 0;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null) continue;
                if (!string.Equals(s.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.BandOfClock(s.slotTime) != b) continue;
                n++;
            }
            return n;
        }

        /// <summary>该机场**某具体时刻**（± 容差）已持有的时刻槽数（含已用与未用）。</summary>
        public static int SlotsHeldAt(GameState gs, string airport, string clock)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return 0;
            int n = 0;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null) continue;
                if (!string.Equals(s.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.ClockDiff(s.slotTime, clock) > ScheduleSlots.SlotToleranceMinutes) continue;
                n++;
            }
            return n;
        }

        /// <summary>该机场该时段还能取得多少个时刻槽（池容量 − 已持有，下限 0）。</summary>
        public static int SlotsAvailable(GameState gs, string airport, int band)
        {
            int left = SlotPoolCapacity(airport, band) - SlotsHeld(gs, airport, band);
            return left < 0 ? 0 : left;
        }

        /// <summary>时刻池是否已满（满则不能再买 / 再申请）。</summary>
        public static bool SlotPoolFull(GameState gs, string airport, int band)
            => SlotsAvailable(gs, airport, band) <= 0;

        /// <summary>
        /// 时刻池占用的一行式说明，供 n77 与邮件使用。
        /// ★2026-10-15：池是**窗口级**口径（协调人按小时/时段公布容量），故这里写**时刻区间**
        /// 而不是光写「早时段」；单条槽的身份一律用具体时刻（见 <see cref="SlotPoolLineAt"/>）。
        /// </summary>
        public static string SlotPoolLine(GameState gs, string airport, int band)
        {
            int b = band < 0 ? 0 : band > 3 ? 3 : band;
            int cap = SlotPoolCapacity(airport, b);
            int held = SlotsHeld(gs, airport, b);
            int free = cap - held; if (free < 0) free = 0;
            string curfew = AirlineCEO.Data.AirportCatalog.CurfewLabel(airport);
            string tail = string.IsNullOrEmpty(curfew) ? "24 小时" : curfew;
            string coord = AirlineCEO.Data.AirportCatalog.IsCoordinated(airport) ? "协调机场" : "非协调";
            return $"{airport} {ScheduleSlots.FormatClock(ScheduleSlots.WindowStartOf(b))}–"
                   + $"{ScheduleSlots.FormatClock(ScheduleSlots.WindowEndOf(b))}（{StrategyManager.SlotBandName(b)}班池）"
                   + $" · 池 {held}/{cap}（余 {free}）· {coord} · {tail}";
        }

        /// <summary>某个**具体时刻**的时刻池占用一行式（n77 与申请/购入邮件用）。</summary>
        public static string SlotPoolLineAt(GameState gs, string airport, string clock)
        {
            int m = ScheduleSlots.ParseClock(clock);
            if (m < 0) return "";
            int b = ScheduleSlots.BandOfDep(ScheduleSlots.FormatClock(m));
            int cap = SlotPoolCapacity(airport, b);
            int heldBand = SlotsHeld(gs, airport, b);
            int heldAt = SlotsHeldAt(gs, airport, clock);
            string coord = AirlineCEO.Data.AirportCatalog.IsCoordinated(airport) ? "协调机场" : "非协调";
            return $"{airport} {ScheduleSlots.FormatClock(m)} · 该时刻已持 {heldAt} 条；"
                   + $"所在{StrategyManager.SlotBandName(b)}班池 {heldBand}/{cap}（余 {Math.Max(0, cap - heldBand)}）· {coord}";
        }

        /// <summary>
        /// 该机场起飞的班次是否需占时刻槽（协调机场）。
        /// 逐停站同口径：每一个协调停站（本场端 / 经停端 / 对端）的每一次停留都要一对槽。
        /// </summary>
        public static bool NeedsDepartureSlot(string origin)
            => AirlineCEO.Data.AirportCatalog.IsCoordinated(origin);

        /// <summary>该机场该时段尚未绑定班次的持仓数（可立刻用于排班）。</summary>
        public static int FreeSlots(GameState gs, string airport, int band)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return 0;
            int b = band < 0 ? 0 : band > 3 ? 3 : band;
            int n = 0;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null || s.inUse) continue;
                if (!string.Equals(s.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.BandOfClock(s.slotTime) != b) continue;
                n++;
            }
            return n;
        }

        /// <summary>
        /// 该机场**指定时刻 ± 容差**内尚未绑定班次的持仓数（★2026-10-15：排班匹配的唯一判据）。
        /// 一条 06:30 的槽接不了 07:00 的班 —— 这正是「一槽一班」在时刻上的量化。
        /// </summary>
        public static int FreeSlotsAt(GameState gs, string airport, string clock)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return 0;
            int n = 0;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null || s.inUse) continue;
                if (!string.Equals(s.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.ClockDiff(s.slotTime, clock) > ScheduleSlots.SlotToleranceMinutes) continue;
                n++;
            }
            return n;
        }

        /// <summary>
        /// 按机场 + **具体时刻**预约一条未占用持仓，并写下占用航线/航班号。
        /// 返回槽位下标；-1 表示该时刻（± 容差）没有空闲持仓。
        /// 匹配取**时刻最接近**的一条，免得 07:00 的班把 06:30 的槽拿走、令 06:30 自己反而没槽。
        /// </summary>
        public static int ReserveSlot(GameState gs, string airport, string clock, string routeId, string flightNo)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return -1;
            if (ScheduleSlots.ParseClock(clock) < 0) return -1;
            int pick = -1;
            int bestD = int.MaxValue;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null || s.inUse) continue;
                if (!string.Equals(s.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                int d = ScheduleSlots.ClockDiff(s.slotTime, clock);
                if (d > ScheduleSlots.SlotToleranceMinutes) continue;
                if (d < bestD) { bestD = d; pick = i; }
            }
            if (pick < 0) return -1;
            stg.slots[pick].inUse = true;
            stg.slots[pick].usedByRouteId = routeId ?? "";
            stg.slots[pick].usedByFlightNo = flightNo ?? "";
            return pick;
        }

        /// <summary>按时段预约（兼容旧调用）：折成该时段的峰点后再按时刻匹配。</summary>
        public static int ReserveSlot(GameState gs, string airport, int band, string routeId, string flightNo)
        {
            int b = band < 0 ? 0 : band > 3 ? 3 : band;
            return ReserveSlot(gs, airport, ScheduleSlots.DepTimes[b], routeId, flightNo);
        }

        /// <summary>退还本航线占用的全部时刻槽（重编或退回呈报时调用）。</summary>
        public static int ReleaseByRoute(GameState gs, string routeId)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null || string.IsNullOrEmpty(routeId)) return 0;
            int n = 0;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null) continue;
                if (!string.Equals(s.usedByRouteId, routeId, System.StringComparison.OrdinalIgnoreCase)) continue;
                s.inUse = false;
                s.usedByRouteId = "";
                s.usedByFlightNo = "";
                n++;
            }
            return n;
        }

        /// <summary>某端的槽状态档：bound 已占用 / free 有空闲持仓待绑定 / grandfather 祖父条款 / missing 缺槽 / none 非协调。</summary>
        public static string EndStatusKey(GameState gs, RouteData route, SlotNeed need)
        {
            if (string.IsNullOrEmpty(need.airport) || !NeedsDepartureSlot(need.airport)) return "none";
            if (HasBoundSlot(StrategyManager.EnsureState(gs), need)) return "bound";
            if (FreeSlotsAt(gs, need.airport, TimeOf(need)) > 0) return "free";
            if (RouteHasTime(route, need) && SlotUsageLogic.HasAnyHistoricPrecedence(gs, need.airport, TimeOf(need)))
                return "grandfather";
            return "missing";
        }

        /// <summary>
        /// 某一端**能不能走协调池申请**（WASG 免费渠道）补齐：此刻既没有属于本线的占用、也没有空闲持仓，
        /// 但所在时段池还有余量（协调人还能分）。池已满 → 申请与购入都无门，开单也是空单。
        ///
        /// ★2026-10-16：一趟往返要**逐停站**办槽（本场端 1 对、对端 1 对、经停站一趟往返停两次故 2 对），
        /// 排班闸门 <see cref="CanScheduleClock"/> / <see cref="CanScheduleBand"/> 每个协调停站都要求 ——
        /// 所以「该开几张申请单」必须**逐停站**问一遍。此处是这件事的**单一判据**：开单
        /// （`InboxManager.QueueSlotApplications`）与页面档位（<see cref="SlotCandidateLogic.EndStateOf"/>）
        /// 原先各写了一份同样的判断，改一处漏一处。
        /// </summary>
        public static bool CanApplyEnd(GameState gs, RouteData route, SlotNeed need)
            => EndStatusKey(gs, route, need) == "missing"
               && SlotsAvailable(gs, need.airport, need.band) > 0;

        /// <summary>
        /// 「时刻变更（改时）」可动的范围：本线在该站已有的那条槽，与本次所需时刻相差多少分钟之内
        /// 才算**改时**而不是**另要一条**。
        ///
        /// 现实口径：改时（slot time change）是把**已经分配到的系列**挪一挪 —— 协调人看的是
        /// 「你是不是在这条服务上、是不是同一段时刻」。差出大半天就不是改时，而是另提一条新槽
        /// （新申请，走 <see cref="CanApplyEnd"/>）。4 小时 = 同一段服务的日内窗口，够覆盖
        /// 「按缺口买的是 12:00、飞机当天最早 12:45 才能走」这类滑点。
        /// </summary>
        public const int SlotChangeWindowMinutes = 240;

        /// <summary>
        /// 本线在该机场某刻 ± 容差内**未绑定班次**的持仓下标（−1 = 没有）。
        ///
        /// 只认**未绑定**（`inUse == false`）且属于本线或尚无归属的持仓：已绑班次的槽挪走，
        /// 正在飞的那一班当场没槽；别条航线的槽更不能拿来给自己改时。
        /// </summary>
        public static int FreeHoldingIndexAt(GameState gs, string airport, string clock, string routeId)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null || ScheduleSlots.ParseClock(clock) < 0) return -1;
            int pick = -1, bestD = int.MaxValue;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null || s.inUse) continue;
                if (!string.Equals(s.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(s.usedByRouteId)
                    && !string.Equals(s.usedByRouteId, routeId, System.StringComparison.OrdinalIgnoreCase)) continue;
                int d = ScheduleSlots.ClockDiff(s.slotTime, clock);
                if (d > ScheduleSlots.SlotToleranceMinutes) continue;
                if (d < bestD) { bestD = d; pick = i; }
            }
            return pick;
        }

        /// <summary>
        /// 本端该不该走**时刻变更申请**（改时）—— 现实里这是**持槽之后**才有的动作，不是新申请。
        ///
        /// 条件（三条全中）：
        /// 1. 本端此刻**缺槽**（`endStatus == missing`：既无本线占用、也无空闲持仓、也非祖父）；
        /// 2. 本线在该站**已持有一条未绑定的槽**，其时刻与所需时刻之差 **> 15 分容差**
        ///    （≤ 容差就是同一条槽，闸门本就放行，不必改）且 **≤ <see cref="SlotChangeWindowMinutes"/>**；
        /// 3. 该槽可动（未绑班次、属于本线或尚无归属）。
        ///
        /// <paramref name="poolRoom"/> = 目标系列的**池余量**：协调人按它批驳（余量为 0 就是驳），
        /// 故调用处若拿不到余量就别开单（开了也是一张注定被驳的空单）。
        ///
        /// 依据：WASG 9.1.3（航司须「request the necessary **slot adjustments** in a timely manner」）、
        /// 8.10.1（槽不绑航线/航班号，可变更用途）、8.7.1(e)（系列 = 同一或**约同一**时刻
        /// —— 这正是「15 分容差内算同一条、之外要报批」的由来）。
        /// </summary>
        public static bool TryTimeChange(GameState gs, RouteData route, SlotNeed need,
            out int index, out string fromClock, out string toClock, out bool poolRoom)
        {
            index = -1; fromClock = ""; toClock = ""; poolRoom = false;
            if (gs == null || route == null) return false;
            if (string.IsNullOrEmpty(need.airport) || !NeedsDepartureSlot(need.airport)) return false;
            if (EndStatusKey(gs, route, need) != "missing") return false;

            string want = TimeOf(need);
            if (ScheduleSlots.ParseClock(want) < 0) return false;
            string to = SlotRequestClock(want);
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return false;

            int pick = -1, bestD = int.MaxValue;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null || s.inUse) continue;
                if (!string.Equals(s.airport, need.airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(s.usedByRouteId)
                    && !string.Equals(s.usedByRouteId, need.routeId, System.StringComparison.OrdinalIgnoreCase)) continue;
                int d = ScheduleSlots.ClockDiff(s.slotTime, to);
                if (d <= ScheduleSlots.SlotToleranceMinutes) continue;   // 容差内 = 同一条槽，无需变更
                if (d > SlotChangeWindowMinutes) continue;               // 差太远 = 另要一条，走新申请
                if (d < bestD) { bestD = d; pick = i; }
            }
            if (pick < 0) return false;

            index = pick;
            fromClock = stg.slots[pick].slotTime;
            toClock = to;
            poolRoom = SlotsAvailable(gs, need.airport, ScheduleSlots.BandOfDep(to)) > 0;
            return true;
        }

        /// <summary>同上，只问「有没有改时这回事」（不进文案、不判余量）。</summary>
        public static bool CanTimeChange(GameState gs, RouteData route, SlotNeed need)
            => TryTimeChange(gs, route, need, out _, out _, out _, out _);

        /// <summary>
        /// 改时「一行式」文案：`本场端 PEK 12:00 → 12:30（差 30 分，超 15 分容差）`。
        /// 供申请单正文与页面明细共用，免得两处各写一种说法。
        /// </summary>
        public static string SlotChangeNote(GameState gs, RouteData route, SlotNeed need)
        {
            if (!TryTimeChange(gs, route, need, out _, out string from, out string to, out bool room))
                return "";
            int d = ScheduleSlots.ClockDiff(from, to);
            return $"{LegName(need)} {need.airport} {from} → {to}"
                   + $"（差 {d} 分，超 {ScheduleSlots.SlotToleranceMinutes} 分容差；"
                   + (room ? "目标系列有余量，协调人可批" : "目标系列已满，协调人多半会驳") + "）";
        }

        /// <summary>
        /// 本端是否已有**属于本航线**的占用持仓（★2026-10-15：按**具体时刻 ± 容差**匹配）。
        ///
        /// 两条口径（取决于 `need` 是「具体班次」还是「拟排时刻」）：
        /// - 有航班号（`SlotNeeds` / `FlightNeeds`）：`机场 × 时刻 × 本线 × 本航班号` 四项全中；
        /// - 无航班号（`BandNeeds` / `ClockNeeds`，开工前的拟排）：`机场 × 时刻 × 本线` 即算 ——
        ///   拟排时航班号还没定，但本航线早就为这个时刻预订/买入过一条槽；
        ///   若此处不认，就会出现「自己预订出去的槽，反而让本航线自己排不了」。
        ///
        /// 抽成单一判据是因为 `EndStatusKey`（报状态）与 `CanScheduleEnd`（判能不能排）过去各写了一遍，
        /// 于是「界面上说已占用、逻辑上却排不了」这种自相矛盾迟早会出现 —— 2026-10 的时刻槽使用率
        /// 考核就正好把它撞了出来（失格把祖父条款关掉后，持槽端仍报 bound 却排不了）。两处从此共用本函数。
        /// </summary>
        static bool HasBoundSlot(StrategyState stg, SlotNeed need)
        {
            if (stg?.slots == null) return false;
            bool byFlight = !string.IsNullOrEmpty(need.flightNo);
            if (!byFlight && string.IsNullOrEmpty(need.routeId)) return false;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null || !s.inUse) continue;
                if (!string.Equals(s.airport, need.airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.ClockDiff(s.slotTime, TimeOf(need)) > ScheduleSlots.SlotToleranceMinutes) continue;
                if (!string.Equals(s.usedByRouteId, need.routeId, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (byFlight
                    && !string.Equals(s.usedByFlightNo, need.flightNo, System.StringComparison.OrdinalIgnoreCase)) continue;
                return true;
            }
            return false;
        }

        /// <summary>某端的槽状态（长文，供呈报与页面明细）。</summary>
        public static string EndStatusText(GameState gs, RouteData route, SlotNeed need)
        {
            string leg = LegName(need);
            string at = ScheduleSlots.SlotLabel(need.airport, TimeOf(need));
            switch (EndStatusKey(gs, route, need))
            {
                case "none": return $"{leg} {need.airport}：非协调，无需槽";
                case "bound":
                    // 「已占用」不等于「稳了」：本季使用率未达标时历史优先权已终止，该持仓换季会被退回时刻池。
                    // 这正是最该提前预警的一刻 —— 只说「已占用」会把玩家的注意力从补救窗口上引开。
                    return SlotUsageLogic.HasAnyHistoricPrecedence(gs, need.airport, TimeOf(need))
                        ? $"{leg} {at}：已占用"
                        : $"{leg} {at}：已占用（但本期使用率未达 80%，历史优先权已终止，换季将退回时刻池）";
                case "free": return $"{leg} {at}：有空闲持仓，待绑定";
                case "grandfather": return $"{leg} {at}：祖父条款（在飞保留，未绑持仓）";
                default:
                    // 「本航线在飞、却报缺槽」只有一种成因：上季使用率未达 80% 失掉了历史优先权
                    // （见 SlotUsageLogic）。这与「从来没有过槽」是两回事，文案必须分开 ——
                    // 否则 CEO 看到一条天天在飞的航线报「缺槽」，只会当成 bug。
                    return RouteHasTime(route, need)
                        ? $"{leg} {at}：历史优先权已终止（上季使用率未达 80%），需重新申请或购买时刻"
                        : $"{leg} {at}：缺槽";
            }
        }

        /// <summary>某端的槽状态（短标，供单航线页一行内展示：✓ 已占用 / ○ 待绑定 / △ 祖父 / ✗ 缺槽）。</summary>
        public static string EndStatusMark(GameState gs, RouteData route, SlotNeed need)
        {
            switch (EndStatusKey(gs, route, need))
            {
                case "none": return "";
                case "bound": return "✓";
                case "free": return "○";
                case "grandfather": return "△";
                default: return "✗";
            }
        }

        /// <summary>该班次**各停站**的槽位状态（短标，如「SZX 06:30✓ PEK 12:10△」）。</summary>
        public static string SlotStatusOf(GameState gs, RouteData route, int flightIndex)
        {
            if (route == null) return "—";
            var needs = FlightNeeds(route, flightIndex);
            if (needs.Count == 0) return "非协调，无需槽";
            var sb = new System.Text.StringBuilder("槽 ");
            for (int i = 0; i < needs.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                string mark = EndStatusMark(gs, route, needs[i]);
                if (mark.Length == 0) continue;
                sb.Append(needs[i].airport).Append(' ').Append(needs[i].time).Append(mark);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 某一端的缺槽说明（空 = 该端齐备）。
        /// 协调机场无**该时刻**（± 容差）的空闲持仓即构成排班缺口（不论池内还有多少可申请余量）。
        /// ★2026-10-15：由「该时段有没有空闲持仓」改为「该时刻有没有」—— 时刻槽精确到时刻后，
        /// 同段其它时刻的槽帮不上这一刻的班。
        /// </summary>
        public static string EndGapText(GameState gs, RouteData route, SlotNeed need)
        {
            if (CanScheduleEnd(gs, route, need, true)) return "";
            string tier = AirlineCEO.Data.AirportCatalog.TierLabelOf(need.airport);
            string leg = LegName(need);
            string at = ScheduleSlots.SlotLabel(need.airport, TimeOf(need));
            string pool = SlotPoolLineAt(gs, need.airport, TimeOf(need));
            int freeAt = FreeSlotsAt(gs, need.airport, TimeOf(need));
            int room = SlotsAvailable(gs, need.airport, need.band);
            int freeBand = FreeSlots(gs, need.airport, need.band);
            if (room <= 0 && freeAt <= 0)
                return $"缺时刻槽（{leg}）：{at}（{tier}）—— 该时刻无持仓且其所在时段池已满（{pool}），"
                       + $"需等其它航线释放时刻，或改到 {TimesWithRoomText(gs, need.airport, TimeOf(need))}。";
            return $"缺时刻槽（{leg}）：{at}（{tier}）—— 该时刻（±{ScheduleSlots.SlotToleranceMinutes} 分）没有空闲持仓"
                   + $"（同段空闲 {freeBand} 条，{pool}）。时刻槽精确到时刻，06:30 的槽不能拿来飞 07:00 的班；"
                   + $"请申请或购买 **{TimeOf(need)}** 这条槽后再排（下季交割）。";
        }

        /// <summary>
        /// 该机场还能容得下加班次的**时刻**（除 excludeClock 附近外）：池内尚有余量（可申请/购买）
        /// 或该时刻已有空闲持仓（可立刻排）。
        /// 供缺槽时「改到哪个时刻」指路 —— 只说「池满了」而不给去处，等于把问题丢回给玩家。
        /// ★2026-10-15：由「给时段」改为「给时刻」（最多列 4 个，免得一行放不下）。
        /// </summary>
        public static string TimesWithRoomText(GameState gs, string airport, string excludeClock = null)
        {
            var list = new System.Collections.Generic.List<string>();
            int exclude = ScheduleSlots.ParseClock(excludeClock);
            int excludeBand = exclude < 0 ? -1 : ScheduleSlots.BandOfDep(ScheduleSlots.FormatClock(exclude));
            for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
            {
                if (b == excludeBand) continue;
                if (SlotsAvailable(gs, airport, b) <= 0 && FreeSlots(gs, airport, b) <= 0) continue;
                var grid = ScheduleSlots.SlotGridLabels(b);
                for (int i = 0; i < grid.Count && list.Count < 4; i++)
                    list.Add(grid[i]);
                if (list.Count >= 4) break;
            }
            return list.Count == 0 ? "暂无余量" : string.Join("、", list) + " 等";
        }

        /// <summary>兼容旧名（按时段给去处）：该机场各时段名里尚有余量者。</summary>
        public static string BandsWithRoomText(GameState gs, string airport, int excludeBand = -1)
        {
            var list = new System.Collections.Generic.List<string>();
            for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
            {
                if (b == excludeBand) continue;
                if (FreeSlots(gs, airport, b) <= 0 && SlotsAvailable(gs, airport, b) <= 0) continue;
                list.Add(StrategyManager.SlotBandName(b));
            }
            return string.Join("、", list);
        }

        /// <summary>本航线该时段**各停站**的缺槽说明（空 = 均齐备或均非协调）。</summary>
        public static string BandGapText(GameState gs, RouteData route, int outBand)
        {
            if (TryShortEnd(gs, route, outBand, out var need)) return EndGapText(gs, route, need);
            return "";
        }

        /// <summary>
        /// 本航线**某具体时刻**各停站的缺槽说明（空 = 均齐备）★2026-10-15：按时刻报缺口，
        /// 因为同段不同时刻各自有槽（06:30 齐备、07:00 缺槽是常态）。
        /// </summary>
        public static string ClockGapText(GameState gs, RouteData route, string depClock)
        {
            var needs = ClockNeeds(route, depClock);
            for (int i = 0; i < needs.Count; i++)
            {
                string gap = EndGapText(gs, route, needs[i]);
                if (!string.IsNullOrEmpty(gap)) return gap;
            }
            return "";
        }

        /// <summary>本航线该时段第一处缺槽的那一端（供批量申请邮件按机场/时段归集）。</summary>
        public static bool TryShortEnd(GameState gs, RouteData route, int outBand, out SlotNeed need)
        {
            need = default;
            if (gs == null || route == null) return false;
            var needs = BandNeeds(route, outBand);
            for (int i = 0; i < needs.Count; i++)
            {
                if (CanScheduleEnd(gs, route, needs[i], true)) continue;
                need = needs[i];
                return true;
            }
            return false;
        }

        /// <summary>本航线该**具体时刻**第一处缺槽的那一端（供申请单写明买哪一刻）。</summary>
        public static bool TryShortClock(GameState gs, RouteData route, string depClock, out SlotNeed need)
        {
            need = default;
            if (gs == null || route == null) return false;
            var needs = ClockNeeds(route, depClock);
            for (int i = 0; i < needs.Count; i++)
            {
                if (CanScheduleEnd(gs, route, needs[i], true)) continue;
                need = needs[i];
                return true;
            }
            return false;
        }

        /// <summary>按当前班次列表重新占用时刻槽（退回呈报、批准后对齐占用）。祖父班次无持仓则跳过。</summary>
        public static int BindRouteFlights(GameState gs, RouteData route)
        {
            if (gs == null || route == null) return 0;
            var needs = SlotNeeds(route);
            int n = 0;
            for (int i = 0; i < needs.Count; i++)
            {
                var need = needs[i];
                if (ReserveSlot(gs, need.airport, TimeOf(need), need.routeId, need.flightNo) >= 0) n++;
            }
            return n;
        }

        /// <summary>
        /// 该端此刻能否排班：非协调机场始终可以；协调机场需该端该**时刻**（± 容差）
        /// 有一条空闲持仓，或本航线该时刻已有在飞班次（祖父条款，避免无槽就停飞）。
        ///
        /// 祖父条款自 2026-10 起**不再是无限期的**：还须该机场该时刻仍保有历史优先权
        /// （上季使用率 ≥ 80%，见 <see cref="SlotUsageLogic"/>）。失格后即使航线仍在飞，
        /// 也不得再据此加/改班次，须重新申请或购买时刻。
        ///
        /// 注意「已绑持仓」与「祖父条款」是两回事：绑到本端的持仓是**自有资产**（换季清算前仍是本公司的），
        /// 因此即便 `grandfather = false`（严格档，例如自测里比对「关掉祖父会不会缺槽」）也照样可排。
        /// 少了这一条，失格关掉祖父之后会出现「状态报 bound、本函数报排不了」的自相矛盾。
        /// </summary>
        public static bool CanScheduleEnd(GameState gs, RouteData route, SlotNeed need, bool grandfather)
        {
            if (string.IsNullOrEmpty(need.airport) || !NeedsDepartureSlot(need.airport)) return true;
            if (gs == null) return true;
            if (HasBoundSlot(StrategyManager.EnsureState(gs), need)) return true;
            if (FreeSlotsAt(gs, need.airport, TimeOf(need)) > 0) return true;
            if (grandfather
                && RouteHasTime(route, need)
                && SlotUsageLogic.HasAnyHistoricPrecedence(gs, need.airport, TimeOf(need)))
                return true;
            return false;
        }

        /// <summary>
        /// 该去程时段此刻能否排班：**每一个协调停站**都要过（本场端 / 经停端（去程/回程）/ 对端），
        /// 缺任一停站即不可排 —— 经停站一趟往返停两次，故那两对也得齐。
        /// 全部停站均为非协调机场时始终可以。
        /// </summary>
        public static bool CanScheduleBand(GameState gs, RouteData route, int band, bool grandfather)
        {
            if (route == null) return false;
            var needs = BandNeeds(route, band);
            for (int i = 0; i < needs.Count; i++)
                if (!CanScheduleEnd(gs, route, needs[i], grandfather)) return false;
            return true;
        }

        /// <summary>
        /// 该**具体时刻**此刻能否排班（★2026-10-15：排班与加机的时刻口径闸门）。
        /// 与 <see cref="CanScheduleBand"/> 的区别就是按时刻而不是按时段判 ——
        /// 同段 06:30 有槽、07:00 无槽时，前者可排后者不可，这是本机制的核心。
        /// </summary>
        public static bool CanScheduleClock(GameState gs, RouteData route, string depClock, bool grandfather)
        {
            if (route == null) return false;
            var needs = ClockNeeds(route, depClock);
            for (int i = 0; i < needs.Count; i++)
                if (!CanScheduleEnd(gs, route, needs[i], grandfather)) return false;
            return true;
        }

        /// <summary>
        /// 在某需求波窗口内，从 <paramref name="fromMinute"/> 起找出**第一个可排的时刻**（★2026-10-15）。
        ///
        /// 这是「同段多波」的落点算法：候选点就是该窗口的 30 分钟网格，逐个问
        /// <see cref="CanScheduleClock"/> —— 本航线在该波持有的槽（各自时刻不同）会让对应网格点
        /// 通过，没槽的网格点自然被跳过。于是「一个早高峰能排几波」= 该波持有几条不同时刻的槽，
        /// 而不是旧版写死的「一段只出一波」。
        ///
        /// <paramref name="taken"/> 为本次排班已占用的时刻（同一网格点不重复排）。
        /// 返回 `"HH:mm"`；窗口内无可用时刻返回 null。
        /// </summary>
        public static string FirstSchedulableClock(GameState gs, RouteData route, int band,
            System.Collections.Generic.IList<string> taken, bool grandfather, int fromMinute)
        {
            int b = band < 0 ? 0 : band > 3 ? 3 : band;
            var grid = ScheduleSlots.SlotGrid(b);
            int start = fromMinute <= 0 ? ScheduleSlots.WindowStartOf(b) : fromMinute;
            for (int k = 0; k < grid.Count; k++)
            {
                if (grid[k] < start) continue;
                string c = ScheduleSlots.FormatClock(grid[k]);
                if (TakenNear(taken, c)) continue;
                if (!CanScheduleClock(gs, route, c, grandfather)) continue;
                return c;
            }
            return null;
        }

        static bool TakenNear(System.Collections.Generic.IList<string> taken, string clock)
        {
            if (taken == null) return false;
            for (int i = 0; i < taken.Count; i++)
                if (ScheduleSlots.ClockDiff(taken[i], clock) <= ScheduleSlots.SlotToleranceMinutes) return true;
            return false;
        }

        /// <summary>
        /// 本航线是否已有班次在该**停站的这一次停留**（同一站 + 同方向 + 同一具体时刻 ± 容差）
        /// —— 该端祖父条款的依据。
        ///
        /// ★2026-10-16：改为按到站时刻表逐站比对（<see cref="StationCalls"/>）。旧的
        /// 「inbound ？回程离港 ：去程离港」二分法在**经停端**上没有意义（经停站一趟往返两条，
        /// 都既不是本场离港也不是对端离港），照旧写会把经停端的祖父条款判成「从来没有过」。
        /// </summary>
        public static bool RouteHasTime(RouteData route, SlotNeed need)
        {
            if (route == null) return false;
            route.EnsureFlights();
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                var calls = StationCalls(route, f.depTime, f.arrTime);
                for (int k = 0; k < calls.Count; k++)
                {
                    var c = calls[k];
                    if (!string.Equals(c.airport, need.airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (c.inbound != need.inbound) continue;
                    if (!string.Equals(c.station, need.station, System.StringComparison.OrdinalIgnoreCase)) continue;
                    // 两边都先归到系列时刻再比：回程/经停时刻是推导值（17:33），槽只在网格上（17:30），
                    // 不吸附就会出现「本线明明回程飞 17:33，却说该时刻没有历史优先权」的自相矛盾。
                    string t = ScheduleSlots.FormatClock(Mod24(c.dep));
                    if (ScheduleSlots.ClockDiff(ScheduleSlots.NearestSeries(t), TimeOf(need))
                        <= ScheduleSlots.SlotToleranceMinutes) return true;
                }
            }
            return false;
        }

        public static bool RouteHasBand(RouteData route, int band)
        {
            if (route == null) return false;
            route.EnsureFlights();
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                if (ScheduleSlots.BandOfDep(f.depTime) == band) return true;
            }
            return false;
        }

        /// <summary>本航线是否已在「该回程时段」从对端机场起飞（回程端的祖父条款依据）。</summary>
        public static bool RouteHasReturnBand(RouteData route, int band)
        {
            if (route == null) return false;
            route.EnsureFlights();
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                int rm = ReturnDepMinutes(route, f.depTime, f.arrTime);
                if (rm < 0) continue;
                if (ScheduleSlots.BandOfDep(ScheduleSlots.FormatClock(rm)) == band) return true;
            }
            return false;
        }

        #endregion

        /// <summary>发证时占用一个时刻槽（优先同机场），返回是否占到位。</summary>
        public static bool ConsumeSlot(GameState gs, string airport) => ReserveSlot(gs, airport) >= 0;

        /// <summary>预约一个时刻槽（优先同机场），返回槽位下标；-1 表示无可用槽。</summary>
        public static int ReserveSlot(GameState gs, string airport)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return -1;
            int pick = -1;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null || s.inUse) continue;
                if (!string.IsNullOrEmpty(airport)
                    && string.Equals(s.airport, airport, System.StringComparison.OrdinalIgnoreCase))
                {
                    pick = i;
                    break;
                }
                if (pick < 0) pick = i;
            }
            if (pick < 0) return -1;
            stg.slots[pick].inUse = true;
            return pick;
        }

        /// <summary>退还预约的时刻槽（开航执行失败时调用，避免槽位被空占）。</summary>
        public static void ReleaseSlot(GameState gs, int index)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return;
            if (index < 0 || index >= stg.slots.Count) return;
            var s = stg.slots[index];
            if (s != null) s.inUse = false;
        }

        /// <summary>
        /// 预约指定机场的一条未占用持仓（不跨机场兜底）。
        /// **逐停站各占一条**，故每个停站必须各自有持仓，不能用别处的槽顶替（经停端也一样）。
        /// </summary>
        public static int ReserveSlotAt(GameState gs, string airport)
        {
            if (string.IsNullOrEmpty(airport)) return -1;
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null) return -1;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null || s.inUse) continue;
                if (!string.Equals(s.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                stg.slots[i].inUse = true;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// 许可前置缺口：0=齐备 1=缺航权额度 2=缺国际运行资质机组。
        /// 注意：时刻槽不是发证条件——协调机场的时刻（尤其竞拍时段）应在取得许可后再办，
        /// 否则未获许可时已付的时刻费用会打水漂；时刻只卡「开航」。
        ///
        /// 缺口 2 是「海外引进」渠道的专属生态位：国际/地区航线要求承运人至少
        /// 拥有 1 名海外引进渠道到岗的运行序列人员（民航局视角 = 具备国际运行经验的机组）。
        /// 校招/猎头/内转都不提供该资质，海外渠道由此摆脱「被猎头支配的劣势选项」定位。
        /// </summary>
        public static int MissingPrerequisite(GameState gs, string origin, string dest,
            bool international, out string note)
        {
            note = "";
            if (international && !StrategyManager.HasRouteRight(gs, origin, dest))
            {
                note = $"国际/地区航线须先占用双边航权额度（{origin}—{dest}）——航权来自两国航空运输协定，额度用尽即无法获批。";
                return 1;
            }
            if (international && !HasOverseasQualification(gs))
            {
                note = "国际/地区航线须具备国际运行资质机组。两条路径：" +
                       "① 到「职业介绍所」用「海外引进」渠道至少录用 1 名运行人员；" +
                       "② 把培训中心升到 3 级「航司大学」，在员工档案的「培训」页为飞行员考取「国际运行资质」。";
                return 2;
            }
            return 0;
        }

        /// <summary>
        /// 是否已具备国际运行资质。两条等价路径：
        /// ① 至少 1 名「海外引进」渠道到岗的运行序列人员（机组本身带资质）；
        /// ② 至少 1 名运行序列人员考取并持有有效的「国际运行资质」证照（公司自建培训中心培养）。
        /// 路径 ② 是 2026-10-08 新增：此前该资质是海外引进渠道的独占生态位，公司无法自行取得。
        /// </summary>
        public static bool HasOverseasQualification(GameState gs) =>
            gs != null && (gs.OverseasCrewCount > 0 || IntlOpsCertifiedCrewCount(gs) > 0);

        /// <summary>通过自建培训考取「国际运行资质」的运行序列在职人数。</summary>
        public static int IntlOpsCertifiedCrewCount(GameState gs)
        {
            if (gs?.staffPool == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || p.onMarket || !p.isOperational) continue;
                if (CertificationLogic.HasValid(p, CertificationCatalog.IntlOps, gs.SimUtc)) n++;
            }
            return n;
        }

        /// <summary>许可通过率：基准 − 竞对运力 + 基地加成 + 安全评级 + 局方关系 + 政府关系 − 重申惩罚。</summary>
        public static float PassChance(GameState gs, string origin, string dest, bool international, int attempt)
        {
            float p = international ? 0.58f : 0.86f;
            p -= RouteRightManager.EstimateCompetitorShare(origin, dest) * 0.25f;
            if (FromHub(origin, dest)) p += 0.10f;
            p += SafetyAdjust(gs);
            p += RegulatorAdjust(gs);
            p += GovAdjust(gs);
            p -= 0.12f * Mathf.Clamp(attempt - 1, 0, 3);
            return Mathf.Clamp(p, 0.05f, 0.95f);
        }

        static float SafetyAdjust(GameState gs)
        {
            if (gs == null) return 0f;
            return gs.safetyRating switch
            {
                SafetyRating.A => 0.05f,
                SafetyRating.C => -0.08f,
                SafetyRating.D => -0.18f,
                _ => 0f
            };
        }

        static float RegulatorAdjust(GameState gs)
        {
            var saf = gs?.saf;
            if (saf?.regulators == null) return 0f;
            var rel = saf.regulators.Find(r => r != null && r.regulator == "CAAC");
            if (rel == null && saf.regulators.Count > 0) rel = saf.regulators[0];
            if (rel == null) return 0f;
            return (rel.status - 2) * 0.05f;   // 0红 −0.10 … 3绿 +0.05
        }

        static float GovAdjust(GameState gs)
        {
            var stg = gs?.stg;
            if (stg == null) return 0f;
            return (stg.governmentRelation - 50f) / 50f * 0.06f;
        }

        /// <summary>逐项列出影响许可通过率的因素（供邮件正文）。</summary>
        public static string ChanceBreakdown(GameState gs, string origin, string dest, bool international, int attempt)
        {
            float share = RouteRightManager.EstimateCompetitorShare(origin, dest);
            string baseLine = international ? "国际/地区基准 58%" : "国内基准 86%";
            var sb = new System.Text.StringBuilder();
            sb.Append(baseLine);
            sb.Append($" · 竞对运力 {share * 100f:F0}% → {share * 0.25f * 100f:F0}%");
            if (FromHub(origin, dest)) sb.Append(" · 主基地出发 +10%");
            float safAdj = SafetyAdjust(gs);
            if (!Mathf.Approximately(safAdj, 0f)) sb.Append($" · 安全评级 {safAdj * 100f:+0.#;-0.#}%");
            float regAdj = RegulatorAdjust(gs);
            if (!Mathf.Approximately(regAdj, 0f)) sb.Append($" · 局方关系 {regAdj * 100f:+0.#;-0.#}%");
            float govAdj = GovAdjust(gs);
            if (!Mathf.Approximately(govAdj, 0f)) sb.Append($" · 政府关系 {govAdj * 100f:+0.#;-0.#}%");
            if (attempt > 1) sb.Append($" · 第{attempt}次申请 −{(attempt - 1) * 12}%");
            return sb.ToString();
        }

        /// <summary>在办准入许可（CEO 已批准、尚未开航结案）——供 n77 展示办理进度。</summary>
        public static System.Collections.Generic.List<InboxMail> InFlightPermits(GameState gs)
        {
            var list = new System.Collections.Generic.List<InboxMail>();
            if (gs?.inbox == null) return list;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != "open_route") continue;
                if (m.permitStage < StageReview) continue;
                if (m.permitStage == StageGranted && !m.effectPending) continue;
                list.Add(m);
            }
            return list;
        }

        /// <summary>局方受理号（给人看的案号，不是程序邮件 id）。</summary>
        public static string CaseNo(InboxMail m)
        {
            if (m == null) return "—";
            int y = m.permitAcceptYear > 0 ? m.permitAcceptYear : m.receivedYear;
            int q = m.permitAcceptQuarter > 0 ? m.permitAcceptQuarter : m.receivedQuarter;
            if (y <= 0) y = 1;
            if (q <= 0) q = 1;
            string o = string.IsNullOrEmpty(m.origin) ? "XXX" : m.origin.ToUpperInvariant();
            string d = string.IsNullOrEmpty(m.dest) ? "XXX" : m.dest.ToUpperInvariant();
            int n = Mathf.Max(1, m.permitAttempts);
            return $"局受{y}年Q{q}-{o}{d}-{n:00}";
        }

        /// <summary>当前办理到第几步（1–6：五道许可关 + 时刻开航）。</summary>
        public static int DisplayNode(InboxMail m)
        {
            if (m == null) return 0;
            if (m.permitStage == StageGranted)
                return m.permitGate == "时刻" ? NodeGrant : NodeSlot;
            if (m.permitStage == StageDenied)
                return Mathf.Clamp(m.permitNode + 1, NodeAccept, NodeGrant);
            return Mathf.Clamp(m.permitNode + 1, NodeAccept, NodeGrant);
        }

        public static float Progress01(InboxMail m) => DisplayNode(m) / (float)NodeSlot;

        public static string NodeShort(int node) => node switch
        {
            NodeAccept => "受理",
            NodeReview => "审查",
            NodeRights => "航权",
            NodeNotice => "公示",
            NodeGrant => "批复",
            NodeSlot => "时刻",
            _ => "—"
        };

        /// <summary>一行式办理进度（供 n77 与邮件复用）。</summary>
        public static string ProgressLine(InboxMail m)
        {
            if (m == null) return "";
            string stage = StageName(m.permitStage);
            if (m.permitStage == StageGranted)
                stage = !m.effectPending ? "已发证"
                    : m.permitGate == "时刻" ? "已核准 · 待配时刻"
                    : "已核准 · 待开航";
            else if (m.permitStage == StageDenied) stage = "未获批 · 待重申或放弃";
            else if (!string.IsNullOrEmpty(m.permitGate)) stage += " · 受阻于" + m.permitGate;
            string node = "";
            if (m.permitStage == StageReview || m.permitStage == StageNotice)
            {
                int n = Mathf.Clamp(m.permitNode + 1, NodeAccept, NodeGrant);
                node = $" · 节点 {n}/{NodeGrant} {NodeName(n)}";
            }
            string due = (m.permitDueYear > 0 || m.permitDueQuarter > 0)
                ? $" · 预计 第{m.permitDueYear}年 Q{m.permitDueQuarter}" : "";
            return $"{m.origin}—{m.dest} · {stage}{node}{due} · 通过率 {m.permitChance * 100f:F0}%";
        }

        #region 节点与进度表（Phase 18.2：每到一个节点发进度汇报）

        public const int NodeAccept = 1;   // 受理立案
        public const int NodeReview = 2;   // 材料与运力审查
        public const int NodeRights = 3;   // 航权额度核验
        public const int NodeNotice = 4;   // 局方公示
        public const int NodeGrant = 5;    // 批复与发证
        public const int NodeSlot = 6;     // 时刻协调与开航（发证后）

        public static int Turn(int year, int quarter) => year * 4 + (quarter - 1);

        /// <summary>季度平移 n 季。</summary>
        public static void After(int year, int quarter, int n, out int outYear, out int outQuarter)
        {
            int t = Turn(year, quarter) + n;
            outYear = t / 4;
            outQuarter = t % 4 + 1;
        }

        public static string NodeName(int node) => node switch
        {
            NodeAccept => "受理立案",
            NodeReview => "材料与运力审查",
            NodeRights => "航权额度核验",
            NodeNotice => "局方公示",
            NodeGrant => "批复与发证",
            NodeSlot => "时刻协调与开航",
            _ => "未启动"
        };

        /// <summary>节点的办理事项（局方查什么、公司做什么）。</summary>
        public static string NodeDetail(int node, bool international, bool needsSlot) => node switch
        {
            NodeAccept => "运营部递交申请，局方核对材料齐备性并出具受理号",
            NodeReview => "局方审查运力安排、安全运行能力与市场竞争状况",
            NodeRights => international
                ? "核配双边航空运输协定内的航权额度与指定承运人名额（国际硬前置）"
                : "国内航线不涉航权额度，随经营许可一并核准",
            NodeNotice => "局方公示拟开通航线，竞对可在公示期内提出异议（可能顺延审批）",
            NodeGrant => "许可核准发证（发证前不得售票）；时刻协调与开航均在此之后办理",
            NodeSlot => needsSlot
                ? "凭证向协调机场申请时刻：按夏秋/冬春协调季分配，热门时段需竞拍或购买（先证后槽，未获许可则不必花钱）"
                : "非协调机场无时刻限制，凭证报备航班计划后即可排班开航",
            _ => ""
        };

        /// <summary>节点相对受理季度的偏移：0 = 受理季，span = 批复季，发证后节点在 span 之后。</summary>
        public static int OffsetOf(int node, int span)
        {
            if (span < 1) span = 1;
            if (node <= NodeAccept) return 0;
            if (node == NodeGrant) return span;
            if (node > NodeGrant) return span + (node - NodeGrant);
            if (span <= 1) return span;                 // 单季办结：中间节点与批复同季并联
            if (node == NodeReview) return 1;
            if (node == NodeNotice) return span - 1;
            int mid = 1 + Mathf.FloorToInt((span - 2) / 2f + 0.5f);
            return Mathf.Clamp(mid, 1, Mathf.Max(1, span - 1));
        }

        /// <summary>本季应上报的节点（已上报的不再重复；受理季与批复季由专门邮件汇报）。</summary>
        public static System.Collections.Generic.List<int> NodesAt(int doneNode,
            int acceptYear, int acceptQuarter, int dueYear, int dueQuarter, int nowYear, int nowQuarter)
        {
            var list = new System.Collections.Generic.List<int>();
            int span = Turn(dueYear, dueQuarter) - Turn(acceptYear, acceptQuarter);
            int elapsed = Turn(nowYear, nowQuarter) - Turn(acceptYear, acceptQuarter);
            if (span < 1) span = 1;
            if (elapsed <= 0 || elapsed >= span) return list;
            for (int node = NodeReview; node <= NodeNotice; node++)
            {
                if (node <= doneNode) continue;
                if (OffsetOf(node, span) == elapsed) list.Add(node);
            }
            return list;
        }

        /// <summary>节点所在季度。</summary>
        public static void NodeQuarter(int node, int span, int acceptYear, int acceptQuarter,
            out int year, out int quarter)
        {
            After(acceptYear, acceptQuarter, OffsetOf(node, span), out year, out quarter);
        }

        /// <summary>
        /// 准入进度表：受理 → 批复逐节点列出目标季度与状态（doneNode = 已上报节点），
        /// 需要时刻的航线再补一行「时刻协调与开航」（发证后办理，只卡开航不卡发证）。
        /// </summary>
        public static string ProgressTable(bool international, bool needsSlot,
            int acceptYear, int acceptQuarter, int dueYear, int dueQuarter, int doneNode,
            int stage = 0, bool launchPending = false)
        {
            int span = Mathf.Max(1, Turn(dueYear, dueQuarter) - Turn(acceptYear, acceptQuarter));
            var sb = new System.Text.StringBuilder();
            for (int node = NodeAccept; node <= NodeGrant; node++)
            {
                NodeQuarter(node, span, acceptYear, acceptQuarter, out int y, out int q);
                string state;
                if (node == NodeGrant && stage == StageDenied) state = "✗ 未获批（可重申）";
                else if (node <= doneNode) state = "✓ 已完成";
                else if (node == doneNode + 1) state = "◐ 进行中";
                else state = "○ 待办";
                sb.Append($"<b>{node}) {NodeName(node)}</b>　第{y}年 Q{q}　{state}\n");
                sb.Append($"　　{NodeDetail(node, international, needsSlot)}\n");
            }

            NodeQuarter(NodeSlot, span, acceptYear, acceptQuarter, out int sy, out int sq);
            string launchState;
            if (!launchPending && stage == StageGranted) launchState = "✓ 已完成";
            else if (stage == StageDenied) launchState = "— 需先取得许可";
            else if (launchPending && stage == StageGranted) launchState = "◐ 进行中";
            else launchState = "○ 待办";
            sb.Append($"<b>{NodeSlot}) {NodeName(NodeSlot)}</b>　第{sy}年 Q{sq} 起　{launchState}"
                + (needsSlot ? "" : "（非协调机场，无需时刻）") + "\n");
            sb.Append($"　　{NodeDetail(NodeSlot, international, needsSlot)}\n");
            return sb.ToString();
        }

        #endregion

        /// <summary>按 RPM 估算日利润（万美元/日），供发证后排班与航线记录使用。</summary>
        public static float EstimateDailyProfitWan(GameState gs, string origin, string dest, string via)
        {
            if (gs == null) return 0f;
            float dist = string.IsNullOrEmpty(via)
                ? AirlineCEO.UI.MapWidget.GreatCircleKm(origin, dest)
                : AirlineCEO.UI.MapWidget.GreatCircleKm(origin, via)
                  + AirlineCEO.UI.MapWidget.GreatCircleKm(via, dest);
            string ac = dist >= 5000f ? "B787-9" : "A320neo";
            var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = dist,
                fuelPricePerTon = gs.fuelPricePerTon,
                competitorSupply = 0.4f,
                demandMultiplier = CityEconomyLogic.OdDemandMultiplier(gs, origin, dest),
                brandEquity = gs.brandAwareness,
                aircraftType = ac,
                seats = EconomyBalanceParams.DefaultSeatsFor(ac),
                passengerType = "leisure"
            });
            return rpm.dailyProfit;
        }
    }
}
