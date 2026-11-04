#if UNITY_EDITOR
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 命令台「在某一刻加一班」的隔离回归（2026-11-01）。
    ///
    /// 背景：n98「航线分配」页整页删除后，CEO 原先在页面上「选机 → 点时刻 → 呈报」的路径没了。
    /// 用户裁定这条链路不能废：改由命令台接收（「06:30 给北京—深圳加一班」），
    /// 运营部在后台挑机、缺槽自己开单，结果以邮件反馈。
    ///
    /// 本回归钉三件事：
    /// <list type="number">
    /// <item><b>认得准</b>：带具体时刻的加班句 → <see cref="CeoCommandRouter.TopicAddFlight"/>；
    ///   而「加密到每天 3 班」（数量）仍归运力调令，问句不办；</item>
    /// <item><b>办得对</b>：运营部挑出真排得进的机 → 呈报件进收件箱，班次表一根汗毛不动
    ///   （与页面当年「批准才落班」同一口径）；</item>
    /// <item><b>办不成也要回信</b>：一刻已有班次 → 直接说清楚不重复报；
    ///   无机可派 → 发运力核对汇报（汇报件，不占待办），绝不硬落一班排不出来的。</item>
    /// </list>
    ///
    /// 与 <see cref="CeoCommandRegression"/> 一样是隔离回归：只碰静态函数与内存 GameState。
    /// </summary>
    public static class AddFlightCommandRegression
    {
        public static bool Run()
        {
            // ———— ① 认得准（纯函数，无需台账）————
            var hit = CeoCommandRouter.Route("n12_航线网络", "06:30 给北京—深圳加一班");
            bool hitOk = hit.ok && hit.topic == CeoCommandRouter.TopicAddFlight
                         && hit.kind == CeoCommandRouter.CeoAskKind.Act
                         && hit.deptCode == "OPS"
                         && hit.clock == "06:30"
                         && hit.origin == "PEK" && hit.dest == "SZX";

            // 数量口径（加密到每天 N 班）不能被这个新意图抢走：铺哪几个时刻是运营部的活
            var cap = CeoCommandRouter.Route("n12_航线网络", "把北京—深圳加密到每天 3 班");
            bool capOk = cap.ok && cap.topic == CeoCommandRouter.TopicCapacity;

            // 问句不办（「06:30 还能加一班吗」是询问，不是命令）
            var ask = CeoCommandRouter.Route("n12_航线网络", "06:30 还能加一班吗");
            bool askOk = ask.topic != CeoCommandRouter.TopicAddFlight;

            // 定位不到航线时不猜：不能随便挑一条线去动它的班次
            var noRoute = CeoCommandRouter.Route("n12_航线网络", "06:30 加一班");
            bool noRouteOk = noRoute.topic != CeoCommandRouter.TopicAddFlight;

            var go = new GameObject("__add_flight_test_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.activeRoutes = new List<RouteData>();
                gs.fleet = new List<Aircraft>();
                gs.inbox = new List<InboxMail>();
                gs.year = 1;
                gs.quarter = 1;

                var route = new RouteData
                {
                    routeId = "R-AF1", origin = "PEK", destination = "SZX", distanceKm = 1900f,
                    aircraftType = "A320neo", flightNumber = "SU700", registration = "B-3001",
                    depTime = "08:00", arrTime = "11:00", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                };
                route.SyncFlightsFromPrimary();
                gs.activeRoutes.Add(route);
                int flights0 = route.flights.Count;

                // ② 认线 → 交给运营部（只回答「落到哪条线」，不改账）
                var ans = DeptReportLogic.Build(gs, hit, "06:30 给北京—深圳加一班");
                bool lineOk = ans.ok && ans.needsRun && ans.routeKey == "R-AF1"
                              && route.flights.Count == flights0;

                // ③ 无机可派：发运力核对汇报（汇报件，不占待办），且不落班
                gs.fleet.Clear();
                int mailsBefore = gs.inbox.Count;
                bool noAcOk = !InboxManager.QueueAddFlightByOps(gs, route, "06:30", out string msgNoAc)
                              && gs.inbox.Count > mailsBefore
                              && route.flights.Count == flights0;
                var lastMail = gs.inbox.Count > mailsBefore ? gs.inbox[gs.inbox.Count - 1] : null;
                noAcOk = noAcOk && lastMail != null && lastMail.action != InboxManager.AssignFlightAction
                         && !InboxManager.RequiresDecision(lastMail);

                // ④ 备妥两端**逐时刻**的槽（与 R21 同一夹具口径：时刻槽精确到时刻）
                gs.fleet.Add(new Aircraft { registration = "B-3001", type = "A320neo" });
                gs.fleet.Add(new Aircraft { registration = "B-3002", type = "A320neo" });
                for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
                {
                    var grid = ScheduleSlots.SlotGridLabels(b);
                    for (int k = 0; k < grid.Count; k++)
                    {
                        SeedSlotAt(gs, "PEK", grid[k]);
                        SeedSlotAt(gs, "SZX", grid[k]);
                    }
                }

                // ⑤ 正路：运营部挑机 → 呈报待批；班次表一根汗毛不动
                int beforeOk = gs.inbox.Count;
                bool queued = InboxManager.QueueAddFlightByOps(gs, route, "06:30", out string msgOk);
                var assign = FindAssignMail(gs);
                bool okOk = queued && assign != null && route.flights.Count == flights0
                            && InboxManager.RequiresDecision(assign)
                            && gs.inbox.Count > beforeOk;

                // ⑥ 那一刻已有班次（08:00 首班）→ 不重复报
                bool dupOk = !InboxManager.QueueAddFlightByOps(gs, route, "08:00", out string msgDup)
                             && msgDup.Contains("已有班次");

                Debug.Log($"加机指令 → 认得准={hitOk}（{hit.clock}，{hit.origin}—{hit.dest}）"
                          + $"　数量口径仍走调令={capOk}（{cap.topic}）　问句不办={askOk}　无线索不猜={noRouteOk}"
                          + $"\n　认线不改账={lineOk}（routeKey={ans.routeKey}，班次 {route.flights.Count}）"
                          + $"\n　无机可派→汇报件={noAcOk}（{msgNoAc}）"
                          + $"\n　正路呈报={okOk}　收件箱={gs.inbox.Count}　班次未动={route.flights.Count}（期望 {flights0}）"
                          + $"\n　说明：{msgOk}"
                          + $"\n　同刻不重复={dupOk}（{msgDup}）"
                          + $"\n　{(hitOk && capOk && askOk && noRouteOk && lineOk && noAcOk && okOk && dupOk ? "✓" : "✗")}");

                return hitOk && capOk && askOk && noRouteOk && lineOk && noAcOk && okOk && dupOk;
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        static InboxMail FindAssignMail(GameState gs)
        {
            if (gs?.inbox == null) return null;
            for (int i = gs.inbox.Count - 1; i >= 0; i--)
                if (gs.inbox[i] != null && gs.inbox[i].action == InboxManager.AssignFlightAction)
                    return gs.inbox[i];
            return null;
        }

        /// <summary>给某机场某时刻补一条空闲持仓（该刻已有空闲槽时不重复加）。</summary>
        static void SeedSlotAt(GameState gs, string airport, string clock)
        {
            if (gs?.stg == null) return;
            if (gs.stg.slots == null) gs.stg.slots = new List<SlotHolding>();
            if (RoutePermitLogic.FreeSlotsAt(gs, airport, clock) > 0) return;
            gs.stg.slots.Add(new SlotHolding { airport = airport, slotTime = clock, inUse = false });
        }
    }
}
#endif
