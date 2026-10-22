using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>按游戏本地时钟执行起飞/降落（含过站回程），并按日计提固定成本。</summary>
    public static class LiveClockSimulator
    {
        public static void Advance(GameState gs, DateTime fromUtc, DateTime toUtc)
        {
            if (gs == null || toUtc <= fromUtc) return;
            if ((toUtc - fromUtc).TotalMinutes > LiveClockLogic.MaxCatchUpMinutes)
                fromUtc = toUtc.AddMinutes(-LiveClockLogic.MaxCatchUpMinutes);

            var offset = GameState.LocalUtcOffset;
            DateTime fromL = fromUtc + offset;
            DateTime toL = toUtc + offset;
            bool dirty = false;

            DateTime cursor = fromL.Date.AddDays(1);
            while (cursor <= toL)
            {
                if (cursor > fromL)
                {
                    ApplyDailyFixed(gs);
                    // 过渡业务与固定成本同节拍逐日入账（飞机在租期间的租金）
                    string transitionText = TransitionBusinessLogic.OnDayTick(gs);
                    if (!string.IsNullOrEmpty(transitionText)) gs.AddDecisionLog(transitionText);
                    dirty = true;
                }
                cursor = cursor.AddDays(1);
            }

            if (gs.activeRoutes == null)
            {
                if (dirty) gs.NotifyChanged();
                return;
            }

            // 本 tick 的停飞/复航呈报收口：按**整条航线**判定（一班停、一班飞的多班航线不得来回刷屏），
            // 同一条航线的同一轮停飞只报一次（route.groundedNoticed），整 tick 聚合为至多两条决策日志。
            var report = new GroundReport();
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var route = gs.activeRoutes[i];
                if (route == null || route.schedulePending) continue;
                route.EnsureFlights();
                for (int f = 0; f < route.flights.Count; f++)
                {
                    if (ProcessFlight(gs, route, route.flights[f], fromL, toL))
                        dirty = true;
                }
                TrackGroundState(gs, route, report);
            }

            if (Flush(gs, report)) dirty = true;

            if (dirty) gs.NotifyChanged();
        }

        /// <summary>本 tick 的停飞 / 复航收集器（聚合后每 tick 至多两条决策日志）。</summary>
        sealed class GroundReport
        {
            public readonly List<GroundEntry> started = new List<GroundEntry>();
            public readonly List<string> resumed = new List<string>();
            public bool Empty => started.Count == 0 && resumed.Count == 0;
        }

        /// <summary>一条停飞呈报：航线（起讫）+ 原因。</summary>
        sealed class GroundEntry
        {
            public string where;
            public GroundReason reason;
            public string Format() => $"{where}（{FlightGroundLogic.Label(reason)}）";
        }

        static bool ProcessFlight(GameState gs, RouteData route, RouteFlight flight, DateTime fromL, DateTime toL)
        {
            bool dirty = false;
            if (flight.airborne && TryLandIfDue(gs, route, flight, fromL, toL))
                dirty = true;

            // 无机可派：不得起飞（旧实现在找不到机号时直接放行 —— 无机号的「幽灵航班」，
            // 见 FlightGroundLogic 的类注释）。持续性原因**不再整段提前返回**：本班当天的
            // 「应飞」仍须计入时刻槽使用率的分母（见下方 NoteDueForFlight），只是不试飞。
            // 空中航班（起飞时机号仍有效、途中才被退役/转租）不受影响，仍按上方 TryLandIfDue 正常落地结算。
            var ground = FlightGroundLogic.Of(gs, route, flight);
            bool persistentGround = !flight.airborne && FlightGroundLogic.IsPersistent(ground);

            DateTime day = fromL.Date;
            DateTime last = toL.Date;
            while (day <= last)
            {
                if (LiveClockLogic.OperatesOn(flight.daysOfWeek, day, flight.weeklyFlights))
                {
                    // 时刻槽使用率考核（IATA WASG 80/20）：这一天的两端离港「应飞」各记一次，
                    // **与飞不飞得起来无关** —— 无机停飞必须照记分母，否则「整季停飞」的航线
                    // 分母恒为 0、永远不失格，正好与真实规则相反（不飞才是最大的未使用）。
                    // 正当事由（无机/停场/机场关闭）另计一路，供 CEO 事后向局方申请豁免。
                    SlotUsageLogic.NoteDueForFlight(gs, route, flight, day, ground);

                    if (!persistentGround)
                    {
                        LiveClockLogic.RoundTripWindow(day, flight.depTime, flight.arrTime, route.distanceKm,
                            route.aircraftType, route.groundStaffAssigned,
                            out DateTime outDep, out _, out DateTime retDep, out _);

                        string outKey = LiveClockLogic.OpKey(outDep);
                        string retKey = LiveClockLogic.OpKey(retDep) + "R";

                        if (outDep > fromL && outDep <= toL && flight.lastOpKey != outKey && !flight.airborne)
                        {
                            if (TryTakeoff(gs, route, flight, outDep, outKey, inbound: false))
                            {
                                SlotUsageLogic.NoteOperatedForLeg(gs, route, flight, outDep, inbound: false);
                                dirty = true;
                            }
                        }

                        if (flight.airborne && TryLandIfDue(gs, route, flight, fromL, toL))
                            dirty = true;

                        if (retDep > fromL && retDep <= toL && flight.lastOpKey != retKey && !flight.airborne)
                        {
                            if (TryTakeoff(gs, route, flight, retDep, retKey, inbound: true))
                            {
                                SlotUsageLogic.NoteOperatedForLeg(gs, route, flight, retDep, inbound: true);
                                dirty = true;
                            }
                        }

                        if (flight.airborne && TryLandIfDue(gs, route, flight, fromL, toL))
                            dirty = true;
                    }
                }

                day = day.AddDays(1);
            }

            return dirty;
        }

        static bool TryLandIfDue(GameState gs, RouteData route, RouteFlight flight, DateTime fromL, DateTime toL)
        {
            if (!flight.airborne) return false;
            DateTime dep = DateTime.FromBinary(flight.departLocalTicks);
            int block = LiveClockLogic.BlockMinutes(flight.depTime, flight.arrTime, route.distanceKm);
            DateTime arr = dep.AddMinutes(block);
            if (arr > fromL && arr <= toL)
            {
                Land(gs, route, flight, block);
                return true;
            }
            return false;
        }

        static bool TryTakeoff(GameState gs, RouteData route, RouteFlight flight, DateTime depLocal, string key, bool inbound)
        {
            if (route != null
                && (ExternalEventManager.IsAirportFrozen(gs, route.origin)
                    || ExternalEventManager.IsAirportFrozen(gs, route.destination)))
                return false;

            // 硬性闸门：没有可执飞的飞机就不许起飞。
            // 旧实现是 `var ac = FindAircraft(...); if (ac != null) {…}` —— ac == null（无机号、
            // 或机号已不在机队）时整段校验被跳过，直接 airborne = true，于是「幽灵航班」照飞照赚。
            if (FlightGroundLogic.Of(gs, route, flight) != GroundReason.None)
                return false;

            // 机号以 FlightGroundLogic 的口径为准（班次机号缺失时回退航线平面字段），
            // 并回填班次，保证随后的落地结算用的是同一架飞机。
            string reg = FlightGroundLogic.RegistrationOf(route, flight);
            var ac = FlightGroundLogic.FindAircraft(gs, reg);
            flight.registration = reg;
            ac.status = AircraftStatuses.InFlight;

            flight.airborne = true;
            flight.inboundLeg = inbound;
            flight.departLocalTicks = depLocal.ToBinary();
            flight.lastOpKey = key;
            return true;
        }

        /// <summary>
        /// 按「整条航线」收口停飞/复航呈报：只要有任一班持续性无机可派即报停飞（同轮只报一次），
        /// 全部班次重新具备执飞资格时报复航。逐班判定会把「一班停、一班飞」的多波航线
        /// 变成每 tick 一停一复的刷屏，故判据必须落在航线层。
        /// </summary>
        static void TrackGroundState(GameState gs, RouteData route, GroundReport report)
        {
            if (route == null || report == null) return;

            var reason = FlightGroundLogic.RouteOf(gs, route);
            if (FlightGroundLogic.IsPersistent(reason))
            {
                if (route.groundedNoticed) return;
                route.groundedNoticed = true;
                report.started.Add(new GroundEntry { where = $"{route.origin}→{route.destination}", reason = reason });
                return;
            }

            if (!route.groundedNoticed) return;
            route.groundedNoticed = false;
            report.resumed.Add($"{route.origin}→{route.destination}");
        }

        /// <summary>把本 tick 收集到的停飞/复航写成决策日志（至多两条，附下一步建议）。</summary>
        static bool Flush(GameState gs, GroundReport report)
        {
            if (gs == null || report == null || report.Empty) return false;

            if (report.started.Count > 0)
            {
                // 单一原因时给出该原因专属的下一步；多因混杂则不提具体建议，避免张冠李戴。
                string advice = report.started.Count == 1
                    ? FlightGroundLogic.Advice(report.started[0].reason)
                    : "请逐条核对后下达运力指令改派执飞机";
                gs.AddDecisionLog($"运营：【停飞】{report.started.Count} 条航线无机可派：{PreviewStarted(report.started)}"
                                  + $"　相关班次取消、不计营收。{advice}");
            }
            if (report.resumed.Count > 0)
                gs.AddDecisionLog($"运营：【复航】{report.resumed.Count} 条航线恢复执飞：{Preview(report.resumed)}");
            return true;
        }

        static string Preview(List<string> items, int max = 3)
        {
            if (items == null || items.Count == 0) return "";
            if (items.Count <= max) return string.Join("、", items);
            return string.Join("、", items.GetRange(0, max)) + $" 等 {items.Count} 条";
        }

        static string PreviewStarted(List<GroundEntry> items, int max = 3)
        {
            if (items == null || items.Count == 0) return "";
            int n = Math.Min(items.Count, max);
            var parts = new List<string>(n);
            for (int i = 0; i < n; i++) parts.Add(items[i].Format());
            return n < items.Count
                ? string.Join("、", parts) + $" 等 {items.Count} 条"
                : string.Join("、", parts);
        }

        static void Land(GameState gs, RouteData route, RouteFlight flight, int blockMin)
        {
            float hours = blockMin / 60f;
            // 空中航班一律按「起飞时的机号」结算；起飞时已确保飞机可执飞（见 TryTakeoff），
            // 故此处的空值只可能来自老存档的空中残留，容错处理但不再补发任何营收。
            var ac = FlightGroundLogic.FindAircraft(gs, flight.registration);
            if (ac != null)
            {
                ac.flightHours += hours;
                ac.flightCycles += 1;
                ac.condition = Mathf.Clamp(ac.condition - hours * 0.04f, 1f, 100f);
                if (ac.status == AircraftStatuses.InFlight)
                    ac.status = AircraftStatuses.Airworthy;
            }

            float pnl = flight.dailyProfit;
            if (Mathf.Abs(pnl) < 0.0001f) pnl = route.dailyProfit / Mathf.Max(1, route.FlightCount);
            pnl *= 0.5f;
            // 航线周期固定成本必须逐航段实扣。`flight.dailyProfit` 存的是**毛贡献**份额
            // （= RouteData.GrossDailyProfit / 班次数），而 RouteData.dailyProfit 才是净日利润。
            // 旧实现只入毛、从不扣 FixedDailyCostWan，于是现金比报表净利更宽松：
            // ★5 每季虚高约 594 万（固定 198 万/月 × 3），报表说亏、现金却在多出来。
            // 按班次均摊再乘半程，与 pnl 的 0.5f 同口径。
            pnl -= route.FixedDailyCostWan / Mathf.Max(1, route.FlightCount) * 0.5f;
            // 机组战力质量乘数：必须与 GameState.monthlyRevenue 同口径缩放，
            // 否则季结按 monthlyRevenue×3 轧差时会把缩掉的部分又补回来（等于白扣）。
            pnl *= gs.CrewQualityMultiplier;
            gs.cash += pnl;
            gs.yearNetProfit += pnl;
            gs.liveQuarterIncome += pnl;
            flight.airborne = false;
            flight.inboundLeg = false;
        }

        static void ApplyDailyFixed(GameState gs)
        {
            float day = LiveClockLogic.DailyFixedCostWan(
                gs.monthlyBurnRate,
                gs.FleetMonthlyMaintenance,
                gs.FleetMonthlyLease,
                gs.CeoMonthlyPayWan,
                gs.financeCrisisBurnBonus,
                gs.OperationalPayrollMonthlyWan,
                gs.LaborCostMonthlyWan);
            gs.cash -= day;
            gs.yearNetProfit -= day;
            gs.liveQuarterExpense += day;
        }
    }
}
