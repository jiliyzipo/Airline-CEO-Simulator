using System;
using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>运营部自动编班：选可用飞机 + 时刻/班期（CEO 只批方案，不亲手排）。</summary>
    public static class OpsScheduleLogic
    {
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

            bool longHaul = route.distanceKm >= 5000f;
            string days = longHaul
                ? (revision % 2 == 0 ? "二四六" : "一三五")
                : "每日";
            int weekly = longHaul ? 4 : 7;

            route.registration = ac.registration;
            route.aircraftType = string.IsNullOrEmpty(ac.type) ? route.aircraftType : ac.type;
            route.depTime = dep;
            route.arrTime = arr;
            route.daysOfWeek = days;
            route.weeklyFlights = weekly;
            route.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff(route.aircraftType);
            if (string.IsNullOrEmpty(route.flightNumber))
                route.flightNumber = "NV" + (100 + Math.Abs(route.routeId != null ? route.routeId.GetHashCode() : 0) % 800);
            route.schedulePending = true;
            route.opsPlanRevision = revision;
            route.SyncFlightsFromPrimary();
            return true;
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
            allDaySlots = ContainsAny(s, "全天", "持续", "从早", "多班", "班次")
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
            ParseCeoNote(note, out bool allDay, out bool max);
            string[] deps = (allDay || max)
                ? (string[])ScheduleSlots.DepTimes.Clone()
                : new[] { ScheduleSlots.DepTimes[Math.Abs(route.opsPlanRevision) % ScheduleSlots.DepTimes.Length] };
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
            if (ApplyFromCeoNote(route, fleet, allRoutes, note))
            {
                opsReply = "未接通分析服务或解析失败，已按批复关键词加密早班至红眼。";
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
            /// <summary>缺口类型：no_aircraft / no_slot / curfew / duty。</summary>
            public string kind = "no_aircraft";
            /// <summary>缺槽时：卡住的那一端机场（去程离港或回程离港），供按机场/时段归集申请。</summary>
            public string slotAirport;
            /// <summary>缺槽时：卡住的那一端时段（0 早 / 1 中 / 2 晚 / 3 夜）；−1 = 未知。</summary>
            public int slotBand = -1;
            /// <summary>缺槽时：true = 卡在对端机场的回程离港端。</summary>
            public bool slotInbound;
        }

        public static bool ApplyWaves(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes, string[] deps, string days)
            => ApplyWaves(route, fleet, allRoutes, deps, days, out _);

        /// <summary>编班。shortages 回传「想要但排不出」的时段及原因，便于上层如实呈报。</summary>
        public static bool ApplyWaves(RouteData route, IList<Aircraft> fleet, IList<RouteData> allRoutes,
            string[] deps, string days, out List<WaveShortage> shortages,
            AirlineCEO.Systems.GameState gsRef = null)
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

            var assigned = new List<RouteFlight>();
            var usedBands = new List<int>();
            for (int i = 0; i < target; i++)
            {
                // 需求波窗口：不再固定到 4 个时刻点，而是「该波窗口内、该机落回后最早可起飞」
                string peg = PickDep(deps, i, target);
                int band = ScheduleSlots.BandOfDep(peg);
                if (usedBands.Contains(band)) continue;
                usedBands.Add(band);

                bool grandfather = keepBands.Contains(band);
                if (!RoutePermitLogic.CanScheduleBand(gs, route, band, grandfather))
                {
                    shortages.Add(DiagnoseBand(route, fleet, plan, band, "", gs));
                    continue;
                }

                var ac = OpsRotationLogic.PickForWindow(route, fleet, plan, band, false, gs,
                                                        out int depMin, out string why);
                if (ac == null)
                {
                    var s = DiagnoseBand(route, fleet, plan, band, why, gs);
                    shortages.Add(s);
                    continue;
                }

                string dep = ScheduleSlots.FormatClock(depMin);
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
            int weeklyEach = longHaul ? 4 : 7;
            string daysUse = longHaul && (days == "每日" || string.IsNullOrEmpty(days)) ? "二四六" : (string.IsNullOrEmpty(days) ? "每日" : days);
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
            if (w.Contains("检修") || w.Contains("停用") || w.Contains("退役")) return "检修/停用";
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
        /// 该航线该去程时段的时刻槽缺口说明；两端（去程离港端 + 回程离港端）都齐备则返回空。
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
            for (int i = 0; i < ScheduleSlots.DepTimes.Length; i++)
            {
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
                if (RangeKm(a.type) + 50f < Math.Max(200f, route.distanceKm)) continue;
                ok.Add(a);
            }
            return ok;
        }

        public static float RangeKm(string type)
        {
            if (string.IsNullOrEmpty(type)) return 6300f;
            if (type.IndexOf("787", StringComparison.OrdinalIgnoreCase) >= 0) return 14100f;
            if (type.IndexOf("777", StringComparison.OrdinalIgnoreCase) >= 0) return 13600f;
            if (type.IndexOf("A350", StringComparison.OrdinalIgnoreCase) >= 0) return 15000f;
            if (type.IndexOf("A330", StringComparison.OrdinalIgnoreCase) >= 0) return 11750f;
            if (type.IndexOf("737", StringComparison.OrdinalIgnoreCase) >= 0) return 5600f;
            if (type.IndexOf("A321", StringComparison.OrdinalIgnoreCase) >= 0) return 7400f;
            if (type.IndexOf("A320", StringComparison.OrdinalIgnoreCase) >= 0) return 6300f;
            return 8000f;
        }

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
                sb.Append($"班次 {i + 1}　{f.registration}　{f.depTime} → {f.arrTime}　{ScheduleSlots.LabelForDep(f.depTime)}　{f.daysOfWeek} / 周{f.weeklyFlights}班\n");
            }
            sb.Append($"\n过站回程　落地后约 {turn} 分钟返航\n");
            sb.Append($"预估日利　约 {route.dailyProfit:F1} 万\n\n");
            sb.Append("建议：批准后按此计划投入运行。可点航班周总计划 或 航班详细计划 查看。\n");
            sb.Append("写下修改意见后点「提出修改」，运营部将改计划再报。退回则本航线暂不排班。");
            return sb.ToString();
        }
    }
}
