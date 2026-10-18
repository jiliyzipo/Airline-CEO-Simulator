using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 换季运力评审（每季度一次，对应现实航季交替）：
    /// 运营部复核「机队日循环 + 航线运力」，给出建议（机队轮转整合 / 加密 / 减班），
    /// 呈报 CEO 批准后执行。周排班（班期 + 时刻）仍是执行单位，评审只改“用几架飞机、每条航线几班”。
    /// 收益口径统一由 OpsEconomics 计算，本类不另立算法。
    /// </summary>
    public static class SeasonReviewLogic
    {
        public const int MaxWavesPerRoute = 4;
        /// <summary>
        /// 日利用率低于该值（小时）且日利为正 → 建议加密。
        /// 口径（★2026-10-14 统一）：**执飞日当天**的轮挡小时 = 每段轮挡 × 2 段 × 每日波次。
        /// </summary>
        public const float LowUtilHours = 4.0f;
        /// <summary>日利低于该值（万/日）→ 建议减到 1 班。</summary>
        public const float TrimProfitWan = 0.2f;
        /// <summary>
        /// 日利用率高于该值（小时）→ 建议减班避险。同一口径（执飞日当天、一圈 2 段）：
        /// 洲际线一天一圈 = 17.4h（FRA–ATL 8.72h × 2）→ 超过 13h，故复核会提示减班 ——
        /// 这与「单机日利用率目标 10h」是同一条建议口径，不是硬闸门。
        /// </summary>
        public const float TrimUtilHours = 13.0f;
        public const int MaxProposalsPerKind = 3;

        public class RouteLine
        {
            public string routeId;
            public string origin;
            public string dest;
            public string slots;
            public string reg;
            public int waves;
            public float util;
            public float profit;
            public string action = "";
        }

        /// <summary>一期换季评审方案（可试算、可呈报、可执行）。</summary>
        public class SeasonPlan
        {
            public int year;
            public int quarter;
            public int rev;
            public string intent = "keep";
            public bool rotationOnly;

            public int aircraftTotal;
            public int aircraftUsed;
            public int turnsTotal;
            public int maxTurns;
            public float avgTurns;
            public int freedCount;
            public List<string> freed = new List<string>();
            public string rotationNote = "";

            public List<string> densify = new List<string>();
            public List<string> trim = new List<string>();
            public List<RouteLine> lines = new List<RouteLine>();

            public string DensifyCsv => string.Join(",", densify);
            public string TrimCsv => string.Join(",", trim);
            public string FreedCsv => string.Join(",", freed);
            public bool HasProposal => densify.Count > 0 || trim.Count > 0 || freedCount > 0;
        }

        public static List<string> SplitCsv(string csv)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(csv)) return list;
            string[] parts = csv.Split(new[] { ',', '，', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length > 0 && !list.Contains(p)) list.Add(p);
            }
            return list;
        }

        public static SeasonPlan FromMail(InboxMail mail)
        {
            var p = new SeasonPlan();
            if (mail == null) return p;
            p.year = mail.receivedYear;
            p.quarter = mail.receivedQuarter;
            p.rev = mail.seasonRev;
            p.densify = SplitCsv(mail.seasonDensifyCsv);
            p.trim = SplitCsv(mail.seasonTrimCsv);
            return p;
        }

        /// <summary>
        /// 生成（或试算）本期换季方案。apply=true 时同时把机队日循环落地。
        /// intent：keep=只整合机队日循环；increase=再加密；reduce=减班避险。
        /// </summary>
        public static SeasonPlan Build(GameState gs, CapacityDirectiveLogic.Directive directive, bool apply)
        {
            var p = new SeasonPlan { year = gs?.year ?? 0, quarter = gs?.quarter ?? 0 };
            p.intent = directive != null ? directive.intent : "keep";
            if (string.IsNullOrEmpty(p.intent)) p.intent = "keep";
            p.rotationOnly = p.intent == "keep";

            if (gs?.activeRoutes == null || gs.activeRoutes.Count == 0)
            {
                p.rotationNote = "暂无在营航线，本期无需调整。";
                return p;
            }

            // 1) 机队日循环：把各航线班次按枢纽往返轮转重新分给飞机
            var stats = new OpsRotationLogic.FleetRotationStats();
            var freed = new List<string>();
            OpsRotationLogic.ReplanFleet(gs, gs.activeRoutes, gs.fleet, apply, out stats, out freed);
            p.aircraftTotal = stats.aircraftTotal;
            p.aircraftUsed = stats.aircraftUsed;
            p.turnsTotal = stats.turnsTotal;
            p.maxTurns = stats.maxTurns;
            p.avgTurns = stats.avgTurns;
            p.freed = freed;
            p.freedCount = freed.Count;

            // 2) 航线复核：日利用率偏低 → 加密；日利过低/利用率过高 → 减班
            var densCand = new List<RouteLine>();
            var trimCand = new List<RouteLine>();
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null || r.schedulePending) continue;   // 待批方案尚未生效，不纳入本期
                r.EnsureFlights();
                if (r.flights == null || r.flights.Count == 0) continue;

                var line = new RouteLine
                {
                    routeId = r.routeId,
                    origin = r.origin,
                    dest = r.destination,
                    reg = CapacityDirectiveLogic.RegsOf(r),
                    waves = r.FlightCount,
                    profit = r.dailyProfit
                };
                // 口径与 OpsEconomics 一致：一圈 = 去 + 回两段（★2026-10-14 统一，旧版漏算回程一半）
                line.util = OpsEconomics.BlockHours(r.distanceKm > 0 ? r.distanceKm : 1000f)
                            * OpsEconomics.RoundTripSectors * Math.Max(1, r.FlightCount);
                var sb = new StringBuilder();
                for (int f = 0; f < r.flights.Count; f++)
                {
                    if (r.flights[f] == null) continue;
                    if (sb.Length > 0) sb.Append(" / ");
                    sb.Append(r.flights[f].depTime);
                }
                line.slots = sb.ToString();
                p.lines.Add(line);

                if (r.FlightCount < MaxWavesPerRoute && line.profit >= TrimProfitWan && line.util < LowUtilHours)
                    densCand.Add(line);
                else if (r.FlightCount > 1 && (line.profit < TrimProfitWan || line.util > TrimUtilHours))
                    trimCand.Add(line);
            }
            p.lines.Sort((a, b) => b.profit.CompareTo(a.profit));
            densCand.Sort((a, b) => a.util.CompareTo(b.util));
            trimCand.Sort((a, b) => a.profit.CompareTo(b.profit));

            if (p.intent == "increase")
            {
                int target = directive != null && directive.waves > 0
                    ? Math.Min(directive.waves, MaxWavesPerRoute) : 0;
                for (int i = 0; i < densCand.Count; i++)
                {
                    if (target > 0) { if (densCand[i].waves >= target) continue; }
                    else if (p.densify.Count >= MaxProposalsPerKind) break;
                    p.densify.Add(densCand[i].routeId);
                    densCand[i].action = target > 0 ? $"加密到 {target} 班" : "加密 1 班";
                }
            }
            else if (p.intent == "reduce")
            {
                for (int i = 0; i < trimCand.Count && i < MaxProposalsPerKind; i++)
                {
                    p.trim.Add(trimCand[i].routeId);
                    trimCand[i].action = "减到 1 班";
                }
            }

            return p;
        }

        public static string FormatBody(GameState gs, SeasonPlan p, string ceoNote = null)
        {
            if (p == null) return "换季评审方案不可用。";
            var sb = new StringBuilder();
            sb.Append($"运营部已完成第 {p.year} 年 Q{p.quarter} 换季运力评审（第 {p.rev + 1} 稿）。\n\n");
            if (!string.IsNullOrWhiteSpace(ceoNote))
                sb.Append($"CEO 意见：{ceoNote.Trim()}\n\n");

            sb.Append("一、机队日循环\n");
            if (!string.IsNullOrEmpty(p.rotationNote))
                sb.Append($"　{p.rotationNote}\n");
            else
            {
                sb.Append($"　现网 {p.aircraftTotal} 架，在用 {p.aircraftUsed} 架承担 {p.turnsTotal} 段往返/日；");
                sb.Append($"单机最多 {p.maxTurns} 段（平均 {p.avgTurns:F1} 段/架）。\n");
                sb.Append("　排班口径按真实航司标准：单机「一天」= 一个值勤期（FDP），"
                          + $"报到提前 {OpsRotationLogic.ReportBeforeMinutes} 分钟，值勤上限随航段数递减"
                          + $"（2 航段 {OpsRotationLogic.DutyLimitBySectors(2) / 60f:0.##}h → 4 航段 {OpsRotationLogic.DutyLimitBySectors(4) / 60f:0.##}h）；"
                          + $"两个值勤期之间最低休息 {OpsRotationLogic.MinRestMinutes / 60f:0.#} 小时。"
                          + "因此加密班次主要靠增加飞机，而不是把同一架排得更满。\n");
                if (p.freedCount > 0)
                    sb.Append($"　释放运力 {p.freedCount} 架：{p.FreedCsv}（可投入新航线或过渡业务）。\n");
            }

            sb.Append("\n二、航线复核（按日利排序）\n");
            int shown = 0;
            for (int i = 0; i < p.lines.Count && shown < 8; i++)
            {
                var l = p.lines[i];
                if (l == null) continue;
                sb.Append($"　{l.origin}—{l.dest}　{l.slots}｜{l.waves} 班/日｜日利用率 {l.util:F1}h｜日利 {l.profit:F1} 万");
                if (!string.IsNullOrEmpty(l.action)) sb.Append($"　→ 建议{l.action}");
                sb.Append('\n');
                shown++;
            }
            if (p.lines.Count == 0) sb.Append("　（本期无在营航线）\n");

            sb.Append("\n三、批准后执行\n");
            sb.Append("　1) 机队按枢纽日循环重排（不改班期与时刻，只改“哪架飞机飞哪一班”）；\n");
            sb.Append($"　2) 加密 {p.densify.Count} 条航线：{(p.densify.Count == 0 ? "无" : JoinRouteNames(gs, p))}；\n");
            sb.Append($"　3) 减班 {p.trim.Count} 条航线至 1 班：{(p.trim.Count == 0 ? "无" : JoinRouteNames(gs, p, true))}。\n\n");
            sb.Append("批准后本季度按此执行；退回则维持现有排班，不动机队分工。\n");
            sb.Append("也可写下意见（如“先整合日循环，不要加密”“把亏损线都减到 1 班”）后点「提出修改」，运营部重编再报。");
            return sb.ToString();
        }

        static string JoinRouteNames(GameState gs, SeasonPlan p, bool trim = false)
        {
            var ids = trim ? p.trim : p.densify;
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                var r = FindRoute(gs, ids[i]);
                if (sb.Length > 0) sb.Append("、");
                sb.Append(r != null ? $"{r.origin}—{r.destination}" : ids[i]);
            }
            return sb.ToString();
        }

        /// <summary>执行一期换季方案（机队日循环 + 加密 / 减班），返回执行说明。</summary>
        public static string Apply(GameState gs, SeasonPlan p)
        {
            if (gs == null || p == null) return "换季方案不可用。";
            var stats = new OpsRotationLogic.FleetRotationStats();
            var freed = new List<string>();
            OpsRotationLogic.ReplanFleet(gs, gs.activeRoutes, gs.fleet, true, out stats, out freed);

            int densified = 0;
            for (int i = 0; i < p.densify.Count; i++)
                if (AddOneWave(gs, p.densify[i])) densified++;

            int trimmed = 0;
            for (int i = 0; i < p.trim.Count; i++)
                if (TrimToOneWave(gs, p.trim[i])) trimmed++;

            var sb = new StringBuilder();
            sb.Append($"机队日循环重排为 {stats.aircraftUsed} 架承担 {stats.turnsTotal} 段往返/日（单机最多 {stats.maxTurns} 段）");
            if (freed.Count > 0) sb.Append($"，释放 {freed.Count} 架运力（{string.Join("、", freed)}）");
            sb.Append($"；加密 {densified} 条航线，减班 {trimmed} 条航线。");
            return sb.ToString();
        }

        /// <summary>给指定航线加一个尚未占用的时段（保持原有时刻不变）。</summary>
        static bool AddOneWave(GameState gs, string routeId)
        {
            var route = FindRoute(gs, routeId);
            if (route == null) return false;
            route.EnsureFlights();
            if (route.flights.Count >= MaxWavesPerRoute) return false;

            var deps = new List<string>();
            for (int i = 0; i < route.flights.Count; i++)
                if (route.flights[i] != null && !deps.Contains(route.flights[i].depTime)) deps.Add(route.flights[i].depTime);
            for (int i = 0; i < ScheduleSlots.DepTimes.Length; i++)
            {
                if (deps.Contains(ScheduleSlots.DepTimes[i])) continue;
                deps.Add(ScheduleSlots.DepTimes[i]);
                break;
            }
            if (deps.Count == route.flights.Count) return false;

            if (!OpsScheduleLogic.ApplyWaves(route, gs.fleet, gs.activeRoutes, deps.ToArray(), "每日")) return false;
            route.schedulePending = false;
            route.directivePending = false;
            route.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff(route.aircraftType);
            // 负基准＝真实亏损，必须原样透传（不得用 Math.Max(1f,…) 兜底，那会把亏损洗成盈利）
            float baseline = OpsEconomics.ResolvedSingleFlightBaseline(route);
            OpsEconomics.ApplyToRoute(route, baseline, gs.onTimePercent);
            return true;
        }

        /// <summary>把航线压到 1 班（保留日利最高的那一班时刻）。</summary>
        static bool TrimToOneWave(GameState gs, string routeId)
        {
            var route = FindRoute(gs, routeId);
            if (route == null) return false;
            route.EnsureFlights();
            if (route.flights.Count <= 1) return false;

            string keep = route.flights[0].depTime;
            float best = route.flights[0].dailyProfit;
            for (int i = 1; i < route.flights.Count; i++)
            {
                if (route.flights[i] != null && route.flights[i].dailyProfit > best)
                {
                    best = route.flights[i].dailyProfit;
                    keep = route.flights[i].depTime;
                }
            }
            if (!OpsScheduleLogic.ApplyWaves(route, gs.fleet, gs.activeRoutes, new[] { keep }, "每日")) return false;
            route.schedulePending = false;
            route.directivePending = false;
            route.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff(route.aircraftType);
            // 负基准＝真实亏损，必须原样透传（不得用 Math.Max(1f,…) 兜底，那会把亏损洗成盈利）
            float baseline = OpsEconomics.ResolvedSingleFlightBaseline(route);
            OpsEconomics.ApplyToRoute(route, baseline, gs.onTimePercent);
            return true;
        }

        static RouteData FindRoute(GameState gs, string routeId)
        {
            if (gs?.activeRoutes == null || string.IsNullOrEmpty(routeId)) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r != null && string.Equals(r.routeId, routeId, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }
    }
}
