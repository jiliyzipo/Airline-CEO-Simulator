using System;
using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>运营部自动编班：选可用飞机 + 时刻/班期（CEO 只批方案，不亲手排）。</summary>
    public static class OpsScheduleLogic
    {
        /// <summary>
        /// 航程容差（km）：目录写的是**厂商典型航程（按设计载荷）**，实际减载后能多飞一点，
        /// 故比对时给**机型侧**加这 50 km 余量（见 <see cref="RangeCovers"/>）。
        /// 方向不能搞反：若把它加在本线需求上（`距离 + 50`），就变成「更严」，
        /// 会把 A321LR 从 FRA–ATL（7,400 vs 7,409）这种现实里真在飞的线上剔掉。
        /// </summary>
        public const float RangeToleranceKm = 50f;

        public static bool ApplyDraft(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes, int revision)
        {
            if (route == null) return false;
            revision = revision < 0 ? 0 : revision;
            var ac = PickAircraft(route, fleet, allRoutes, revision);
            if (ac == null) return false;

            int slot = revision % ScheduleSlots.DepTimes.Length;
            string dep = ScheduleSlots.DepTimes[slot];
            // 到达时刻按真实轮挡推算（旧口径用固定 08:00→10:30 的 150 分占位跨度，
            // 会让短程航线的到点/排班窗口都偏长一倍）
            int block = OpsRotationLogic.BlockOf(route);
            if (route.distanceKm >= 5000f) block = Math.Max(block, 360);
            int arrMin = LiveClockLogic.ParseMinutes(dep) + block;
            arrMin %= 24 * 60;
            string arr = $"{arrMin / 60:00}:{arrMin % 60:00}";

            // 班次/班期统一由 RouteFrequencyModel 推算（取代 km>=5000 ? 4 : 7）。
            // 单波草案只有 1 个班次承载，故周班次上限取 7；更密的班次由 ApplyWaves 多波铺开。
            int weekly = Math.Min(RouteFrequencyModel.ClampWeekly(RouteFrequencyModel.ForRoute(route).weeklyFlights), 7);
            string days = RouteFrequencyModel.DaysForWaveCount(weekly);

            route.registration = ac.registration;
            route.aircraftType = string.IsNullOrEmpty(ac.type) ? route.aircraftType : ac.type;
            route.depTime = dep;
            route.arrTime = arr;
            route.daysOfWeek = days;
            route.weeklyFlights = weekly;
            route.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff(route.aircraftType);
            if (string.IsNullOrEmpty(route.flightNumber))
                route.flightNumber = NextFreeNumber(allRoutes);
            route.schedulePending = true;
            route.opsPlanRevision = revision;
            route.SyncFlightsFromPrimary();
            return true;
        }

        /// <summary>
        /// 兜底航班号：取**下一个空块**的块首（块首 = 现有最大块首 + 块距）。
        /// 旧版写 `"NV" + (100 + hash % 800)` —— 哈希会撞号，而航班号要写进时刻槽持仓
        /// （`usedByFlightNo`），撞号会让两条线认领同一条槽。块粒度见 `FlightScheduleFormat.BlockStride`。
        /// </summary>
        static string NextFreeNumber(IList<RouteData> allRoutes)
        {
            string prefix = "";
            int maxBase = FlightScheduleFormat.FirstBase - FlightScheduleFormat.BlockStride;
            if (allRoutes != null)
                for (int i = 0; i < allRoutes.Count; i++)
                {
                    var r = allRoutes[i];
                    if (r == null) continue;
                    if (string.IsNullOrEmpty(prefix)) prefix = FlightScheduleFormat.PrefixOf(r.flightNumber);
                    int n = FlightScheduleFormat.NumberOf(r.flightNumber);
                    if (n > maxBase) maxBase = n;
                }
            if (string.IsNullOrEmpty(prefix)) prefix = "NV";
            return $"{prefix}{maxBase + FlightScheduleFormat.BlockStride}";
        }

        public static Aircraft PickAircraft(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes, int revision)
        {
            int slot = Math.Abs(revision) % ScheduleSlots.DepTimes.Length;
            string dep = ScheduleSlots.DepTimes[slot];

            // 一机多段：优先按「同基地往返轮转」选机（其它时段已飞的飞机可继续串飞）
            var plan = new OpsRotationLogic.RotationPlan();
            plan.LoadFrom(allRoutes, route);
            var rotated = OpsRotationLogic.PickForSlot(route, fleet, plan, dep);
            if (rotated != null) return rotated;

            // 兜底：仅完全空闲的飞机（旧口径）
            var ok = ListFreeAircraft(route, fleet, allRoutes);
            if (ok.Count == 0) return null;
            int i = revision % ok.Count;
            if (i < 0) i += ok.Count;
            return ok[i];
        }

        public static bool ParseCeoNote(string note, out bool allDaySlots, out bool maximize)
        {
            string s = note ?? "";
            maximize = ContainsAny(s, "最大", "加密", "潜力", "客货流", "很多", "高峰", "挖掘");
            // 「班次」曾在 allDaySlots 词表里，但它是中文里描述班次最常用的词，
            // 于是「把班次收一收」「班次太多了」都会命中 → 判成「挖满全天」→ 直排 4 段，
            // 把「想减班」办成「从 1 班加密到 4 班」。用户想说的是「别猜」，
            // 所以这里只保留真正表达了「铺满全天」的词。
            allDaySlots = ContainsAny(s, "全天", "全天候", "持续", "从早", "多班")
                          || (ContainsAny(s, "早班") && ContainsAny(s, "红眼"))
                          || maximize;
            return !string.IsNullOrWhiteSpace(s);
        }

        static bool ContainsAny(string s, params string[] keys)
        {
            if (string.IsNullOrEmpty(s) || keys == null) return false;
            for (int i = 0; i < keys.Length; i++)
                if (s.IndexOf(keys[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        public static bool ApplyFromCeoNote(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes, string note)
        {
            return ApplyFromCeoNote(route, fleet, allRoutes, note, out _);
        }

        /// <summary>
        /// 关键词通道排班。<paramref name="how"/> 回一句话说明**依据了什么**。
        ///
        /// 旧版只认「全天 vs 非全天」：玩家说「换到红眼」，`allDay=false`，于是被当成
        /// 「随便挑一个时段」，按 `opsPlanRevision` 取模落一个 —— 说了等于没说。
        /// 现在显式说出的时段优先于「随便挑」，与 §14 的「不猜 / 不忽略玩家明说的」同源。
        /// </summary>
        public static bool ApplyFromCeoNote(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes,
            string note, out string how)
        {
            ParseCeoNote(note, out bool allDay, out bool max);
            var parsed = CapacityDirectiveLogic.Parse(note);
            string[] deps;
            if (allDay || max)
            {
                deps = (string[])ScheduleSlots.DepTimes.Clone();
                how = "已按批复铺满早班至红眼。";
            }
            else if (parsed.deps != null && parsed.deps.Length > 0)
            {
                deps = parsed.deps;
                how = "已按批复把班次排在 " + string.Join("、", parsed.deps) + "。";
            }
            else
            {
                deps = new[] { ScheduleSlots.DepTimes[Math.Abs(route.opsPlanRevision) % ScheduleSlots.DepTimes.Length] };
                how = "批复未指明时段，运营部按现稿替换了一个时段。";
            }
            return ApplyWaves(route, fleet, allRoutes, deps, "每日");
        }

        public static bool ApplyFromCeoNoteOrAi(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes, string note, string aiJson, out string opsReply)
        {
            opsReply = "";
            if (TryReadAiDepsCsv(aiJson, out string depsCsv, out string days, out string rationale, out bool coverAll))
            {
                string[] deps = SplitDeps(depsCsv);
                if (coverAll || deps == null || deps.Length == 0)
                    deps = (string[])ScheduleSlots.DepTimes.Clone();
                if (ApplyWaves(route, fleet, allRoutes, deps, days))
                {
                    opsReply = string.IsNullOrEmpty(rationale)
                        ? "已按您的批复加密班次并覆盖早班至红眼。"
                        : rationale;
                    return true;
                }
            }
            if (ApplyFromCeoNote(route, fleet, allRoutes, note, out string how))
            {
                opsReply = "未接通分析服务或解析失败，" + how;
                return true;
            }
            opsReply = "无法按批复改班，已维持改时刻方案。";
            return ApplyDraft(route, fleet, allRoutes, route.opsPlanRevision + 1);
        }

        static string PickDep(string[] deps, int i, int waves)
        {
            if (deps == null || deps.Length == 0) return "08:00";
            if (waves <= 1 || deps.Length == 1) return deps[0];
            int idx = i * (deps.Length - 1) / Math.Max(1, waves - 1);
            if (idx < 0) idx = 0;
            if (idx >= deps.Length) idx = deps.Length - 1;
            return deps[idx];
        }

        static string[] SplitDeps(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return null;
            var parts = csv.Split(new[] { ',', '，', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length >= 4 && p.IndexOf(':') > 0) list.Add(p);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        /// <summary>某一时段排不出班的缺口：缺哪一班、为什么缺（如实告知 CEO，不再静默少排）。</summary>
        public class WaveShortage
        {
            public string dep;         // 时段代表时刻，如 12:00
            public string slotLabel;   // 时段名，如 午班
            public string reason;      // 为什么该时段无机可排（汇总）
            public List<string> details; // 逐机归因（到具体机号，便于追溯）
            /// <summary>
            /// 缺口类型：no_aircraft / no_slot / curfew / duty / crew。
            ///
            /// ★crew（2026-10-14 第十一次问询后新增）：机队有时刻槽、飞机也排得开，但**没人可派**
            /// —— 与时刻槽并列的第四道闸门，见 <see cref="CrewCapacityLogic"/>。
            /// </summary>
            public string kind = "no_aircraft";
            /// <summary>
            /// 缺槽时：卡住的那**第一个**端机场（去程离港在本场 / 回程离港在对端）。
            ///
            /// ⚠️ 这是**归因**字段，不是「要办几张单」的清单 —— 它由
            /// <see cref="RoutePermitLogic.TryShortEnd"/>/<see cref="RoutePermitLogic.TryShortClock"/> 填，
            /// 那两个函数遇到第一个不满足的端就 return，而本场端在需求列表里排下标 0，
            /// 所以它**恒定是本场端**，对端缺不缺它并不知道。
            /// 要办单/要列清单请用 `RoutePermitLogic.WaveEndNeeds` + `CanApplyEnd` 逐端自问
            /// （2026-10-16 报障：旧版把它当清单用，于是往返只申请了起飞机场的时刻槽，落地机场一次都没被提起）。
            /// </summary>
            public string slotAirport;
            /// <summary>缺槽时：卡住的那一端时段（0 早 / 1 中 / 2 晚 / 3 夜）；−1 = 未知。</summary>
            public int slotBand = -1;
            /// <summary>缺槽时：true = 卡在对端机场那一次停留。（同上：归因字段，非办单清单）</summary>
            public bool slotInbound;
        }

        public static bool ApplyWaves(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes, string[] deps, string days)
            => ApplyWaves(route, fleet, allRoutes, deps, days, out _);

        /// <summary>
        /// 编班。shortages 回传「想要但排不出」的时段及原因，便于上层如实呈报。
        ///
        /// <paramref name="honorWeekly"/>（★2026-10-14 第十次问询后新增）：
        /// 周班次的**权威**。传 0 = 按 <see cref="RouteFrequencyModel"/> 的建议值摊分（旧口径，
        /// 非调令路径沿用）；传 &gt;0 = **以 CEO 明说的班次为准**（`CapacityDirectiveLogic` 由
        /// 「班期天数 × 波次数」算出后传入）。
        ///
        /// 为什么要这个参数：旧版无论 CEO 说什么，周班次都按模型现算 —— 于是
        /// ① 「加密到每天 1 班」在一周模型只给 4 班的长程线上，永远排不出「每日」；
        /// ② 「加密到每天 3 班」反而把周班次从 4 摊成 3（波次重分同一个总数）；
        /// ③ 呈报里「班期未照办…先改周班次（加密/削减）」是一句**死路建议**（加密抬不动周班次）。
        /// 现改为：**CEO 明说的班次优先**，模型降为建议值，代价（日利用率偏离目标）写进呈报。
        /// 硬闸门仍是时刻槽 / 值勤 / 机队 / **机组** —— 排不出就如实进「运力缺口」。
        /// </summary>
        public static bool ApplyWaves(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes,
            string[] deps, string days, out List<WaveShortage> shortages,
            AirlineCEO.Systems.GameState gsRef = null, int honorWeekly = 0)
        {
            shortages = new List<WaveShortage>();
            if (route == null || deps == null || deps.Length == 0) return false;

            bool longHaul = route.distanceKm >= 5000f;
            int block = OpsRotationLogic.BlockOf(route);
            if (longHaul) block = Math.Max(block, 360);

            // 一机多段：按全机队当天的「值勤期 / 往返」占用选机（同基地可串飞多个需求波）
            var plan = new OpsRotationLogic.RotationPlan();
            plan.LoadFrom(allRoutes, route);

            var gs = gsRef ?? AirlineCEO.Systems.GameState.Instance;
            var keepBands = new List<int>();
            if (route.flights != null)
            {
                for (int k = 0; k < route.flights.Count; k++)
                {
                    var fl = route.flights[k];
                    if (fl == null) continue;
                    int b0 = ScheduleSlots.BandOfDep(fl.depTime);
                    if (!keepBands.Contains(b0)) keepBands.Add(b0);
                }
            }
            RoutePermitLogic.ReleaseByRoute(gs, route.routeId);

            int target = Math.Min(deps.Length, ScheduleSlots.DepTimes.Length);
            if (target < 1) target = 1;

            // —— 机组闸门（第四道，★2026-10-14 CEO 第十一次问询）——
            // 原话：「当 CEO 输入『增加此航线的航班量』，那么除了要获得更多的时刻槽，也要考虑增加轮换机组。」
            // 旧版排班器**完全不看机组**：槽批下来了、飞机也在，班次就排出去 —— 三根轴里唯一会被
            // 静默忽略的那根。现改为：超出机组产能的波次记成「缺机组」缺口（kind=crew），
            // 呈报给出「先补机组 → 再重下加密指令」两阶段，与时刻槽缺口同一套处置形状。
            //
            // 只拦**新增**波次（下限 = 现值段数）：机组不足不该反过来把已有班次砍掉 ——
            // 那是「削减」指令的活，不该是加密失败时的副作用。
            int crewCap = CrewCapacityLogic.MaxCrewedTrips(gs, route);
            int crewAllowed = crewCap == CrewCapacityLogic.NoLimit
                ? int.MaxValue
                : Math.Max(route.FlightCount, crewCap);

            var assigned = new List<RouteFlight>();
            var takenClocks = new List<string>();
            for (int i = 0; i < target; i++)
            {
                // 需求波窗口：不再固定到 4 个时刻点，而是「该波窗口内、该机落回后最早可起飞」
                string peg = PickDep(deps, i, target);
                int band = ScheduleSlots.BandOfDep(peg);
                // ★2026-10-15 去掉「同时段只排一波」的桶去重。现实里一个时段窗口内可以塞很多波：
                // 早高峰 06:30–09:30 就是枢纽一天最挤的一排，06:30 / 07:00 / 07:30 / 08:00 各飞一班
                // 才是常态。旧版按段去重后，一天一段只出一波 —— 这正是「加密指令排不动」的根因。
                // 现在能否再排一波，完全由三根硬轴决定：**那一刻有没有槽**、有没有机、有没有机组。

                // 机组闸门：已排定的段数到顶 → 本波没人可派，不排（缺口如实记，不静默少排）
                if (assigned.Count >= crewAllowed)
                {
                    shortages.Add(CrewShortage(route, band, target, gs));
                    continue;
                }

                bool grandfather = keepBands.Contains(band);
                // 本波落在哪一刻：在该波网格上取「第一个既没被本次排班占用、又有槽可用」的时刻。
                // 本航线在该波持有的槽（各自时刻不同）自然就是这些候选点 —— 持有几条不同时刻的槽，
                // 这一波就能排几班，这正是「同段多波」的物理上限。
                string candidate = RoutePermitLogic.FirstSchedulableClock(
                    gs, route, band, takenClocks, grandfather, ScheduleSlots.ParseClock(peg));
                if (candidate == null)
                {
                    shortages.Add(DiagnoseClock(route, fleet, plan, peg, band, "", gs));
                    continue;
                }

                var ac = OpsRotationLogic.PickForWindow(route, fleet, plan, band, false, gs,
                                                        out int depMin, out string why,
                                                        ScheduleSlots.ParseClock(candidate));
                if (ac == null)
                {
                    shortages.Add(DiagnoseClock(route, fleet, plan, candidate, band, why, gs));
                    continue;
                }

                string dep = ScheduleSlots.FormatClock(depMin);
                // 实际离港必须落在某条槽上（容差内）：时刻槽是「某一刻的一次离港」。
                // 飞机赶不上 06:30 的槽、只能 07:40 起飞时，那一刻没有槽就如实报缺口，
                // 不偷偷把班次记在别的槽名下（否则 80% 统计与实际运行对不上）。
                if (!RoutePermitLogic.CanScheduleClock(gs, route, dep, grandfather))
                {
                    shortages.Add(DiagnoseClock(route, fleet, plan, dep, band, why, gs));
                    continue;
                }
                takenClocks.Add(dep);
                // R4：先取本条值勤的报到时刻（并入既有值勤期则取该值勤期首段），再落班
                int dutyReport = OpsRotationLogic.ReportAt(plan, ac.registration, depMin);
                plan.Assign(ac.registration, OpsRotationLogic.WindowOf(route, dep));
                int arrMin = (depMin + block) % (24 * 60);
                assigned.Add(new RouteFlight
                {
                    registration = ac.registration,
                    depTime = dep,
                    arrTime = $"{arrMin / 60:00}:{arrMin % 60:00}",
                    slotLabel = ScheduleSlots.LabelForDep(dep),
                    dutyStart = dutyReport
                });
            }

            if (assigned.Count == 0)
            {
                // 全时段都排不出：退回单班草案；若草案也失败，缺口已记录，交由上层如实说明
                bool drafted = ApplyDraft(route, fleet, allRoutes, route.opsPlanRevision + 1);
                if (drafted && shortages.Count == 0)
                {
                    shortages.Add(new WaveShortage
                    {
                        dep = deps[0],
                        slotLabel = ScheduleSlots.LabelForDep(deps[0]),
                        reason = "按单班草案降级执行。"
                    });
                }
                return drafted;
            }

            int maxWaves = assigned.Count;
            // 周班次权威：CEO 明说（honorWeekly > 0）优先，否则按模型建议值摊分到实际排得出的波次上。
            int modelWeekly = RouteFrequencyModel.ForRoute(route).weeklyFlights;
            int weeklyTarget = honorWeekly > 0 ? RouteFrequencyModel.ClampWeekly(honorWeekly) : modelWeekly;
            int weeklyEach = RouteFrequencyModel.PerWaveWeekly(weeklyTarget, maxWaves);
            // 尊重 CEO 已选班期：天数与每波班次一致时原样保留，否则回落到规范班期字符串。
            string daysUse = RouteFrequencyModel.ReconcileDays(days, weeklyEach);
            float share = 1f / maxWaves;
            for (int i = 0; i < assigned.Count; i++)
            {
                assigned[i].daysOfWeek = daysUse;
                assigned[i].weeklyFlights = weeklyEach;
                assigned[i].dailyProfit = OpsEconomics.ResolvedSingleFlightBaseline(route) * share;
            }

            route.flights = assigned;
            route.SyncPrimaryFromFlights();
            route.weeklyFlights = weeklyEach * maxWaves;
            route.daysOfWeek = daysUse;
            route.schedulePending = true;
            route.opsPlanRevision++;
            RoutePermitLogic.BindRouteFlights(gs, route);
            return true;
        }

        /// <summary>
        /// 诊断某一时段「为什么无机可排」：逐架飞机归因（检修/过渡业务/航程/机型/值勤超限/时段冲突/宵禁），
        /// 供邮件缺口说明与单航线页预检如实告知 CEO。
        /// </summary>
        public static string DiagnoseSlot(RouteData route, IList<Aircraft> fleet, OpsRotationLogic.RotationPlan plan, string dep)
            => DiagnoseSlotDetail(route, fleet, plan, dep, out _);

        /// <summary>逐机归因：返回汇总说明，并回传逐机明细（到具体机号，便于追溯）。</summary>
        public static string DiagnoseSlotDetail(RouteData route, IList<Aircraft> fleet, OpsRotationLogic.RotationPlan plan,
            string dep, out List<string> details)
            => DiagnoseSlotDetail(route, fleet, plan, dep, out details, out _);

        /// <summary>逐机归因（并回传缺口类型 kind：no_aircraft / curfew / duty）。</summary>
        public static string DiagnoseSlotDetail(RouteData route, IList<Aircraft> fleet, OpsRotationLogic.RotationPlan plan,
            string dep, out List<string> details, out string kind)
            => DiagnoseSlotDetail(route, fleet, plan, dep, -1, out details, out kind);

        /// <summary>
        /// 逐机归因（band ≥ 0 时按需求波窗口逐机推算「最早可起飞时刻」，与选机口径完全一致，
        /// 避免用窗口名义时刻去核值勤/冲突而给出不实原因）。
        /// </summary>
        public static string DiagnoseSlotDetail(RouteData route, IList<Aircraft> fleet, OpsRotationLogic.RotationPlan plan,
            string dep, int band, out List<string> details, out string kind)
        {
            details = new List<string>();
            kind = "no_aircraft";
            if (route == null || fleet == null || plan == null) return "无可排运力。";
            var gs = AirlineCEO.Systems.GameState.Instance;

            int winStart = band >= 0 ? ScheduleSlots.WindowStartOf(band) : -1;
            int winEnd = band >= 0 ? ScheduleSlots.WindowEndOf(band) : -1;

            int total = 0;
            var buckets = new List<string>();
            var counts = new List<int>();
            bool anyCurfew = false, anyDuty = false;

            for (int i = 0; i < fleet.Count; i++)
            {
                var a = fleet[i];
                if (a == null) continue;
                total++;
                string reg = string.IsNullOrEmpty(a.registration) ? "（无机号）" : a.registration;

                string useDep = dep;
                if (band >= 0)
                {
                    int ready = plan.ReadyAt(a.registration);
                    int d = OpsRotationLogic.RoundUpTo5(ready == int.MinValue ? winStart : Math.Max(ready, winStart));
                    if (d > winEnd)
                    {
                        string late = $"回场过晚（最早 {ScheduleSlots.FormatClock(d)} 可起飞，晚于窗口 "
                                      + $"{ScheduleSlots.FormatClock(winEnd)}）";
                        details.Add($"{reg}　{late}");
                        int li = buckets.IndexOf("回场过晚");
                        if (li < 0) { buckets.Add("回场过晚"); counts.Add(1); } else counts[li]++;
                        continue;
                    }
                    useDep = ScheduleSlots.FormatClock(d);
                }

                if (OpsRotationLogic.CanTake(route, plan, a, useDep, gs, false, out string why)) continue;

                details.Add($"{reg}　{why}");
                string bucket = BucketOf(why);
                if (bucket == "宵禁") anyCurfew = true;
                if (bucket == "值勤超限" || bucket == "增援航段上限") anyDuty = true;
                int idx = buckets.IndexOf(bucket);
                if (idx < 0) { buckets.Add(bucket); counts.Add(1); }
                else counts[idx]++;
            }

            if (anyCurfew) kind = "curfew";
            else if (anyDuty) kind = "duty";

            var parts = new List<string>();
            for (int i = 0; i < buckets.Count; i++)
                parts.Add($"{counts[i]} 架{buckets[i]}");
            if (parts.Count == 0) parts.Add("无在役可用飞机");

            return $"机队共 {total} 架，其中 " + string.Join("、", parts.ToArray()) + "。";
        }

        /// <summary>把否决原因归入一档，供缺口汇总。</summary>
        static string BucketOf(string why)
        {
            string w = why ?? "";
            if (w.Contains("检修") || w.Contains("停用") || w.Contains("退役") || w.Contains("退租")) return "检修/停用";
            if (w.Contains("过渡业务")) return "已投入过渡业务";
            if (w.Contains("航程不足")) return "航程不足";
            if (w.Contains("机型不符")) return "机型不符";
            if (w.Contains("值勤超限")) return "值勤超限";
            if (w.Contains("增援航段上限")) return "增援航段上限";
            if (w.Contains("宵禁")) return "宵禁";
            if (w.Contains("时段冲突")) return "时段冲突";
            if (w.Contains("回场过晚")) return "回场过晚";
            return "其它";
        }

        /// <summary>缺口明细打包（含逐机归因与类型），供邮件与页面使用。</summary>
        public static WaveShortage Diagnose(RouteData route, IList<Aircraft> fleet, OpsRotationLogic.RotationPlan plan, string dep)
        {
            var s = new WaveShortage { dep = dep, slotLabel = ScheduleSlots.LabelForDep(dep) };
            s.reason = DiagnoseSlotDetail(route, fleet, plan, dep, out var details, out string kind);
            s.details = details;
            s.kind = kind;
            return s;
        }

        /// <summary>需求波窗口的缺口打包：说明该波窗口在哪个区间、为什么排不出。</summary>
        public static WaveShortage DiagnoseBand(RouteData route, IList<Aircraft> fleet,
            OpsRotationLogic.RotationPlan plan, int band, string pickWhy,
            AirlineCEO.Systems.GameState gsRef = null)
        {
            int i = band < 0 ? 0 : band > 3 ? 3 : band;
            string peg = ScheduleSlots.DepTimes[i];
            var s = new WaveShortage { dep = peg, slotLabel = ScheduleSlots.LabelForDep(peg) };
            string win = $"{ScheduleSlots.FormatClock(ScheduleSlots.WindowStartOf(i))}"
                         + $"–{ScheduleSlots.FormatClock(ScheduleSlots.WindowEndOf(i))}";
            string summary = DiagnoseSlotDetail(route, fleet, plan, peg, i, out var details, out string kind);
            s.details = details;
            s.kind = kind;
            string extra = string.IsNullOrEmpty(pickWhy) ? "" : $"选机结论：{pickWhy}。";
            s.reason = $"{ScheduleSlots.Labels[i]}需求波窗口 {win} 排不出：{extra}{summary}";

            // P3：往返任一端（去程离港 / 回程离港）没有空闲持仓 → 缺时刻槽是排班主因（池有余量也要先申请/购买）
            string slotWhy = SlotGapOf(route, i, gsRef);
            if (!string.IsNullOrEmpty(slotWhy))
            {
                s.details.Add("时刻槽　" + slotWhy);
                var gsNow = gsRef ?? AirlineCEO.Systems.GameState.Instance;
                if (RoutePermitLogic.TryShortEnd(gsNow, route, i, out var shortEnd))
                {
                    s.slotAirport = shortEnd.airport;
                    s.slotBand = shortEnd.band;
                    s.slotInbound = shortEnd.inbound;
                }
                if (s.kind == "no_aircraft" || !RoutePermitLogic.CanScheduleBand(gsRef, route, i, true))
                {
                    s.kind = "no_slot";
                    s.reason = $"{ScheduleSlots.Labels[i]}需求波窗口 {win} 排不出：{slotWhy}";
                }
            }
            return s;
        }

        /// <summary>
        /// 需求波窗口的「缺机组」缺口打包（第四道闸门，★2026-10-14 CEO 第十一次问询）。
        ///
        /// 与 <see cref="DiagnoseBand"/> 同形，但不做逐机归因 —— 卡点不在飞机，而在**没人可派**：
        /// 逐机看过去每架都「有空」，旧版据此照排，班次排出来却无人执飞。故这里改为写清
        /// 需求（本线 N 段要几组/几人）、可供（在册/编制基准 − 其它航线占用）、以及两阶段处置
        /// （先补机组 → 再重下加密指令），与时刻槽缺口同一套形状，便于上层并列处理。
        /// </summary>
        public static WaveShortage CrewShortage(RouteData route, int band, int targetTrips,
            AirlineCEO.Systems.GameState gsRef = null)
        {
            int i = band < 0 ? 0 : band > 3 ? 3 : band;
            string peg = ScheduleSlots.DepTimes[i];
            var s = new WaveShortage
            {
                dep = peg,
                slotLabel = ScheduleSlots.LabelForDep(peg),
                kind = CrewCapacityLogic.KindCrew,
                details = new List<string>()
            };
            if (route == null) { s.reason = "机组缺口：航线数据缺失。"; return s; }

            var gs = gsRef ?? AirlineCEO.Systems.GameState.Instance;
            int want = Math.Max(1, Math.Min(targetTrips, ScheduleSlots.DepTimes.Length));
            int now = Math.Max(1, route.FlightCount);
            int cap = CrewCapacityLogic.MaxCrewedTrips(gs, route);
            string win = $"{ScheduleSlots.FormatClock(ScheduleSlots.WindowStartOf(i))}"
                         + $"–{ScheduleSlots.FormatClock(ScheduleSlots.WindowEndOf(i))}";

            s.details.Add("机组需求　" + CrewCapacityLogic.Line(gs, route, want));
            s.details.AddRange(CrewCapacityLogic.Details(gs, route, want));
            s.reason = $"{ScheduleSlots.Labels[i]}需求波窗口 {win} 排不出：**机组不足** —— "
                       + CrewCapacityLogic.Reason(gs, route, want)
                       + $"本条线机组口径最多排 {(cap == CrewCapacityLogic.NoLimit ? now : cap)} 段/日，"
                       + $"现值 {now} 段，本次要排 {want} 段。";
            return s;
        }

        /// <summary>
        /// 该航线该去程时段的时刻槽缺口说明；**每一个协调停站**（本场端 / 经停端 / 对端）都齐备则返回空。
        /// 协调机场无空闲持仓即构成排班缺口（不论时刻池是否还有可申请余量）。
        /// </summary>
        public static string SlotGapOf(RouteData route, int band, AirlineCEO.Systems.GameState gsRef = null)
        {
            if (route == null) return "";
            try { return SlotGapOfCore(route, band, gsRef); }
            catch (Exception) { return ""; }
        }

        static string SlotGapOfCore(RouteData route, int band, AirlineCEO.Systems.GameState gsRef)
        {
            var gs = gsRef ?? AirlineCEO.Systems.GameState.Instance;
            if (gs == null) return "";
            if (RoutePermitLogic.BandNeeds(route, band).Count == 0) return "";   // 两端均非协调机场
            if (RoutePermitLogic.CanScheduleBand(gs, route, band, true)) return "";
            return RoutePermitLogic.BandGapText(gs, route, band);
        }

        /// <summary>某**具体时刻**的时刻槽缺口说明（★2026-10-15：时刻槽精确到时刻后的归因口径）。</summary>
        public static string SlotGapOfClock(RouteData route, string clock, AirlineCEO.Systems.GameState gsRef = null)
        {
            if (route == null || ScheduleSlots.ParseClock(clock) < 0) return "";
            try
            {
                var gs = gsRef ?? AirlineCEO.Systems.GameState.Instance;
                if (gs == null) return "";
                if (RoutePermitLogic.ClockNeeds(route, clock).Count == 0) return "";
                if (RoutePermitLogic.CanScheduleClock(gs, route, clock, true)) return "";
                return RoutePermitLogic.ClockGapText(gs, route, clock);
            }
            catch (Exception) { return ""; }
        }

        /// <summary>
        /// 某**具体时刻**排不出的缺口打包（★2026-10-15）。与 <see cref="DiagnoseBand"/> 同形，
        /// 但把「哪一刻」写进缺口，CEO 才能据此去申请/购买**那一条**槽。
        /// </summary>
        public static WaveShortage DiagnoseClock(RouteData route, IList<Aircraft> fleet,
            OpsRotationLogic.RotationPlan plan, string clock, int band, string pickWhy,
            AirlineCEO.Systems.GameState gsRef = null)
        {
            int m = ScheduleSlots.ParseClock(clock);
            string at = m < 0 ? (clock ?? "") : ScheduleSlots.FormatClock(m);
            int i = m < 0 ? (band < 0 ? 0 : band > 3 ? 3 : band) : ScheduleSlots.BandOfDep(at);
            string win = $"{ScheduleSlots.FormatClock(ScheduleSlots.WindowStartOf(i))}"
                         + $"–{ScheduleSlots.FormatClock(ScheduleSlots.WindowEndOf(i))}";
            var s = new WaveShortage { dep = at, slotLabel = ScheduleSlots.LabelForDep(at) };
            string summary = DiagnoseSlotDetail(route, fleet, plan, at, i, out var details, out string kind);
            s.details = details;
            s.kind = kind;
            string extra = string.IsNullOrEmpty(pickWhy) ? "" : $"选机结论：{pickWhy}。";
            s.reason = $"{at} 这一波（{win}）排不出：{extra}{summary}";

            string slotWhy = SlotGapOfClock(route, at, gsRef);
            if (!string.IsNullOrEmpty(slotWhy))
            {
                s.details.Add("时刻槽　" + slotWhy);
                var gsNow = gsRef ?? AirlineCEO.Systems.GameState.Instance;
                if (RoutePermitLogic.TryShortClock(gsNow, route, at, out var shortEnd))
                {
                    s.slotAirport = shortEnd.airport;
                    s.slotBand = shortEnd.band;
                    s.slotInbound = shortEnd.inbound;
                }
                if (s.kind == "no_aircraft" || !RoutePermitLogic.CanScheduleClock(gsRef, route, at, true))
                {
                    s.kind = "no_slot";
                    s.reason = $"{at} 这一波（{win}）排不出：{slotWhy}";
                }
            }
            return s;
        }

        /// <summary>当天分钟数 → HH:mm（跨夜以 +1 标注）。</summary>
        static string FormatClock(int minutes)
        {
            int m = minutes % OpsRotationLogic.MinutesPerDay;
            if (m < 0) m += OpsRotationLogic.MinutesPerDay;
            string hm = $"{m / 60:00}:{m % 60:00}";
            return minutes >= OpsRotationLogic.MinutesPerDay ? hm + "+1" : hm;
        }

        /// <summary>冲突航线的可读标签：城对（航班号），便于 CEO 追溯是哪条线占用了飞机。</summary>
        public static string RouteTag(AirlineCEO.Systems.GameState gs, string routeId)
        {
            if (gs?.activeRoutes != null && !string.IsNullOrEmpty(routeId))
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null || !string.Equals(r.routeId, routeId, StringComparison.OrdinalIgnoreCase)) continue;
                    string no = string.IsNullOrEmpty(r.flightNumber) ? "" : " " + r.flightNumber;
                    return $"{r.origin}⇄{r.destination}{no}";
                }
            }
            return string.IsNullOrEmpty(routeId) ? "其它航线" : routeId;
        }

        /// <summary>
        /// 运力预检（只读、不写回任何状态）：按现有全机队日循环试算这条航线四个需求波各能排到什么时刻，
        /// 以及与 ApplyWaves 同口径、具体卡在哪（值勤超限 / 宵禁 / 时段冲突）。供单航线页在下达指令前如实提示。
        /// </summary>
        public static List<WaveShortage> PrecheckSlots(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes,
            out List<string> feasible)
            => PrecheckSlots(route, fleet, allRoutes, out feasible, null);

        /// <summary>同上，并可指定 GameState（缺省用单例），用于时刻槽缺口归因。</summary>
        public static List<WaveShortage> PrecheckSlots(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes,
            out List<string> feasible, AirlineCEO.Systems.GameState gsRef)
        {
            feasible = new List<string>();
            var shortages = new List<WaveShortage>();
            if (route == null || fleet == null) return shortages;

            var plan = new OpsRotationLogic.RotationPlan();
            plan.LoadFrom(allRoutes, route);

            // —— 机组闸门（第四道，★2026-10-14 CEO 第十一次问询）——
            // 与 ApplyWaves 同口径：能排的段数一旦到顶，后续波次即便「有机有槽」也如实报「缺机组」，
            // 免得预检说「四个波次都排得出」、真下指令却少排两班（旧版就是因为不看机组而这样说）。
            int crewCap = CrewCapacityLogic.MaxCrewedTrips(gsRef, route);
            int crewAllowed = crewCap == CrewCapacityLogic.NoLimit
                ? int.MaxValue
                : Math.Max(route.FlightCount, crewCap);
            int crewed = 0;

            for (int i = 0; i < ScheduleSlots.DepTimes.Length; i++)
            {
                if (crewed >= crewAllowed)
                {
                    shortages.Add(CrewShortage(route, i, ScheduleSlots.DepTimes.Length, gsRef));
                    continue;
                }
                if (!RoutePermitLogic.CanScheduleBand(gsRef, route, i, true))
                {
                    shortages.Add(DiagnoseBand(route, fleet, plan, i, "", gsRef));
                    continue;
                }
                var ac = OpsRotationLogic.PickForWindow(route, fleet, plan, i, false, gsRef,
                                                       out int depMin, out string why);
                if (ac == null)
                {
                    shortages.Add(DiagnoseBand(route, fleet, plan, i, why, gsRef));
                    continue;
                }
                string dep = ScheduleSlots.FormatClock(depMin);
                feasible.Add($"{ScheduleSlots.Labels[i]} {dep}");
                plan.Assign(ac.registration, OpsRotationLogic.WindowOf(route, dep));
                crewed++;
            }
            return shortages;
        }

        static bool HasDep(List<RouteFlight> flights, string dep)
        {
            if (flights == null || string.IsNullOrEmpty(dep)) return false;
            for (int i = 0; i < flights.Count; i++)
                if (flights[i] != null && string.Equals(flights[i].depTime, dep, StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool TryReadAiDepsCsv(string json, out string depsCsv, out string days, out string rationale, out bool coverAll)
        {
            depsCsv = ReadJsonString(json, "depsCsv");
            days = ReadJsonString(json, "days");
            rationale = ReadJsonString(json, "rationale");
            coverAll = ReadJsonBool(json, "coverAllSlots") || ReadJsonBool(json, "maximize");
            return coverAll || !string.IsNullOrEmpty(depsCsv);
        }

        static bool ReadJsonBool(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return false;
            int colon = json.IndexOf(':', i);
            if (colon < 0) return false;
            string rest = json.Substring(colon + 1).TrimStart();
            return rest.StartsWith("true", StringComparison.OrdinalIgnoreCase);
        }

        static string ReadJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return "";
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return "";
            int colon = json.IndexOf(':', i);
            if (colon < 0) return "";
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return "";
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return "";
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        static List<Aircraft> ListFreeAircraft(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (allRoutes != null)
            {
                for (int i = 0; i < allRoutes.Count; i++)
                {
                    var r = allRoutes[i];
                    if (r == null || r == route) continue;
                    if (!string.IsNullOrEmpty(r.registration)) used.Add(r.registration);
                    if (r.flights == null) continue;
                    for (int f = 0; f < r.flights.Count; f++)
                        if (r.flights[f] != null && !string.IsNullOrEmpty(r.flights[f].registration))
                            used.Add(r.flights[f].registration);
                }
            }
            var ok = new List<Aircraft>();
            if (fleet == null) return ok;
            var gsRef = AirlineCEO.Systems.GameState.Instance;
            for (int i = 0; i < fleet.Count; i++)
            {
                var a = fleet[i];
                if (a == null || !a.isActive) continue;
                if (a.status == AircraftStatuses.Maintenance || a.status == AircraftStatuses.Retired) continue;
                if (used.Contains(a.registration)) continue;
                // 已投入过渡业务（干租/湿租/包机/培训）的飞机不可再排定期航班
                if (TransitionBusinessLogic.IsBusy(gsRef, a.registration)) continue;
                if (!OpsScheduleLogic.RangeKnown(a.type)) continue;          // 机型不在目录：航程无法核实，不予放行
                if (!OpsScheduleLogic.RangeCovers(a.type, route)) continue;
                ok.Add(a);
            }
            return ok;
        }

        /// <summary>
        /// 机型航程（km）—— **目录真值**（<see cref="AircraftCatalog.RangeKmOf"/>）。
        /// 0 = 机型不在目录，航程无法核实：调用方必须按「飞不了」处理并向玩家说明，
        /// 不得像旧版那样给未知机型兜底 8,000 km（那会让飞不到 7,409 km 的飞机被派上洲际线）。
        /// </summary>
        public static float RangeKm(string type) => AirlineCEO.Data.AircraftCatalog.RangeKmOf(type);

        /// <summary>
        /// 本线需要的**机型航程**（km）＝ 航段距离，下限 200 km。
        /// <see cref="RangeToleranceKm"/> 不在这里扣 —— 余量给的是机型侧（见 <see cref="RangeCovers"/>）。
        /// 于是对外文案里的「本线需 N km」就是玩家看到的航线距离，可直接核对。
        /// </summary>
        public static float RequiredRangeKm(RouteData route)
            => Math.Max(200f, route != null ? route.distanceKm : 0f);

        /// <summary>
        /// 该机型能否覆盖该航线：**机型航程 + 减载余量 ≥ 本线距离**。
        ///
        /// 余量 50 km 是刻意留的，且必须加在机型侧：目录写的是厂商典型航程（载荷受限），
        /// 减载后能多飞一点 —— A321LR 目录 7,400 km vs FRA–ATL 7,409 km 这种「差一点」的线，
        /// 若一分余量都不给，就会把一个现实里真在飞的机型判成飞不了。
        /// （旧写法 `航程 ≥ 距离 + 50` 恰好相反：把 A321LR 判成够不着，与这段说明自相矛盾。）
        /// </summary>
        public static bool RangeCovers(string type, RouteData route)
        {
            float r = RangeKm(type);
            return r > 0f && r + RangeToleranceKm >= RequiredRangeKm(route);
        }

        /// <summary>该机型航程是否可核实（在目录里）。不在目录的机型不得参与排班判定。</summary>
        public static bool RangeKnown(string type) => RangeKm(type) > 0f;

        public static string FormatPlanBody(RouteData route, string ceoNote = null, string opsReply = null)
        {
            if (route == null) return "航线方案不可用。";
            int turn = LiveClockLogic.TurnMinutes(route.aircraftType, route.groundStaffAssigned);
            route.EnsureFlights();
            var sb = new System.Text.StringBuilder();
            sb.Append($"运营部已为 {route.origin}—{route.destination} 编制航班计划（第 {route.opsPlanRevision + 1} 稿）。\n\n");
            if (!string.IsNullOrWhiteSpace(ceoNote))
                sb.Append($"CEO 批复：{ceoNote.Trim()}\n\n");
            if (!string.IsNullOrWhiteSpace(opsReply))
                sb.Append($"运营部说明：{opsReply.Trim()}\n\n");
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                // 时刻印**一次往返的真实占窗**（跨夜标 +1），与周计划/单航线页同一口径 —— 2026-10-14 报障
                sb.Append($"班次 {i + 1}　{f.registration}　{FlightScheduleFormat.WindowLabel(route, f)}　{ScheduleSlots.LabelForDep(f.depTime)}　{f.daysOfWeek} / 周{f.weeklyFlights}班\n");
            }
            sb.Append($"\n过站回程　落地后约 {turn} 分钟返航\n");
            sb.Append($"预估日利　约 {route.dailyProfit:F1} 万\n\n");
            sb.Append("建议：批准后按此计划投入运行。可点航班周总计划 或 航班详细计划 查看。\n");
            sb.Append("写下修改意见后点「提出修改」，运营部将改计划再报。退回则本航线暂不排班。");
            return sb.ToString();
        }
    }
}
