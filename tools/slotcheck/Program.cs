using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;

namespace SlotCheck
{
    /// <summary>
    /// 「两端时刻槽」纯规则离线校验（不依赖 Unity 运行时，也不依赖 GameState）。
    ///
    /// 一个往返班次在两端各需要一个「离港时刻」：
    ///   去程离港 = 本场（origin）按 depTime 起飞；
    ///   回程离港 = 对端（destination）按 depTime + 轮挡 + 过站起飞。
    /// 这里只核对与 GameState 无关的部分（需求枚举、回程时刻推导、分端祖父判定）。
    /// 涉及持仓占用/购买/缺槽归因的部分需要 GameState（MonoBehaviour，离线无法构造），
    /// 由 Unity 编辑器菜单 LogicSelfTests 覆盖（含 ReleaseByRoute / BindRouteFlights / BandGapText）。
    /// </summary>
    public static class Program
    {
        public static int Main()
        {
            int fail = 0;

            // —— 1. 协调机场判定：两端都只看「离港」是否协调 ——
            Check("SZX 离港需槽", RoutePermitLogic.NeedsDepartureSlot("SZX"), ref fail);
            Check("PEK 离港需槽", RoutePermitLogic.NeedsDepartureSlot("PEK"), ref fail);
            Check("SHA 离港需槽", RoutePermitLogic.NeedsDepartureSlot("SHA"), ref fail);
            Check("非协调机场 AKL 离港无需槽", !RoutePermitLogic.NeedsDepartureSlot("AKL"), ref fail);

            // —— 2. 单班次：两端各一条需求 ——
            var route = MakeRoute("SZ100", "08:00", "10:30", destination: "PEK");
            var needs = RoutePermitLogic.SlotNeeds(route);
            Console.WriteLine("R14 两端槽需求：" + Describe(needs));
            Check("往返班次两端各一条（共 2 条）", needs.Count == 2, ref fail);
            Check("去程端 = 离港机场 SZX 早", needs.Count > 0
                && needs[0].airport == "SZX" && needs[0].band == 0 && !needs[0].inbound, ref fail);
            Check("回程端 = 对端机场 PEK", needs.Count > 1
                && needs[1].airport == "PEK" && needs[1].inbound, ref fail);
            Check("回程端航班号与去程相差 1", needs.Count > 1 && needs[1].flightNo == "SZ101", ref fail);
            Check("两端航班号不同（去程号/回程号）",
                needs.Count > 1 && needs[0].flightNo != needs[1].flightNo, ref fail);

            // —— 3. 回程离港时刻 = 去程 + 轮挡 + 过站 ——
            int block = LiveClockLogic.BlockMinutes(route.depTime, route.arrTime, route.distanceKm);
            int turn = LiveClockLogic.TurnMinutes(route.aircraftType, route.groundStaffAssigned);
            int expect = LiveClockLogic.ParseMinutes(route.depTime) + block + turn;
            int returnMin = RoutePermitLogic.ReturnDepMinutes(route.depTime, route.arrTime,
                route.distanceKm, route.aircraftType, route.groundStaffAssigned);
            Console.WriteLine($"R14 回程离港 {ScheduleSlots.FormatClock(returnMin)}"
                + $"（去程 {route.depTime} + 轮挡 {block} 分 + 过站 {turn} 分）");
            Check("回程离港 = 去程 + 轮挡 + 过站", returnMin == expect % (24 * 60), ref fail);
            Check("回程离港晚于去程离港", returnMin > LiveClockLogic.ParseMinutes(route.depTime), ref fail);
            Check("回程端时段取自回程离港时刻（中班）", needs.Count > 1 && needs[1].band == 1, ref fail);

            // —— 4. 跨夜回程：红眼去程的回程落在次日凌晨，仍要占对端该时刻的槽 ——
            var redEye = MakeRoute("SZ200", "22:00", "00:30", destination: "PEK");
            int redReturn = RoutePermitLogic.ReturnDepMinutes(redEye.depTime, redEye.arrTime,
                redEye.distanceKm, redEye.aircraftType, redEye.groundStaffAssigned);
            var redNeeds = RoutePermitLogic.SlotNeeds(redEye);
            Console.WriteLine($"R14 红眼回程离港 {ScheduleSlots.FormatClock(redReturn)} → "
                + $"{ScheduleSlots.SlotBandName(redNeeds.Count > 1 ? redNeeds[1].band : -1)}");
            Check("跨夜回程仍在 0–1439 分钟范围内", redReturn >= 0 && redReturn < 24 * 60, ref fail);
            Check("跨夜回程时刻早于去程（次日凌晨）", redReturn < LiveClockLogic.ParseMinutes(redEye.depTime), ref fail);
            Check("跨夜回程仍生成两端需求", redNeeds.Count == 2, ref fail);

            // —— 5. 拟排时段（尚无班次）：BandNeeds 同样两端各一条 ——
            var noonNeeds = RoutePermitLogic.BandNeeds(route, 1);
            Console.WriteLine("R14 午班两端需求：" + Describe(noonNeeds));
            Check("午班两端各一条需求", noonNeeds.Count == 2, ref fail);
            Check("午班去程端时段 = 午（1）", noonNeeds.Count > 0 && noonNeeds[0].band == 1, ref fail);
            Check("午班回程端时段 = 按代表时刻预估",
                noonNeeds.Count > 1 && noonNeeds[1].band == RoutePermitLogic.ReturnBandOf(route, 1), ref fail);

            // —— 6. 分端祖父条款：两端各自依据本端在飞时刻 ——
            Check("本航线在飞班次含去程早班（去程端祖父）", RoutePermitLogic.RouteHasBand(route, 0), ref fail);
            Check("本航线在飞班次不含去程中班", !RoutePermitLogic.RouteHasBand(route, 1), ref fail);
            Check("本航线在飞班次含回程中班（回程端祖父）",
                RoutePermitLogic.RouteHasReturnBand(route, 1), ref fail);
            Check("本航线在飞班次不含回程早班（回程端无祖父）",
                !RoutePermitLogic.RouteHasReturnBand(route, 0), ref fail);

            // —— 7. 多班次航线：需求按班次数倍增，且两端成对 ——
            var twin = MakeRoute("SZ300", "08:00", "10:30", destination: "PEK");
            twin.flights.Add(new RouteFlight
            {
                registration = "B-9002",
                depTime = "08:45",
                arrTime = "11:15"
            });
            var twinNeeds = RoutePermitLogic.SlotNeeds(twin);
            Console.WriteLine($"R14 双班次需求 {twinNeeds.Count} 条：" + Describe(twinNeeds));
            Check("双班次共 4 条需求（每班两端）", twinNeeds.Count == 4, ref fail);
            Check("第 1 班两端：SZX 去程 + PEK 回程",
                twinNeeds.Count == 4 && twinNeeds[0].airport == "SZX" && !twinNeeds[0].inbound
                && twinNeeds[1].airport == "PEK" && twinNeeds[1].inbound, ref fail);
            Check("第 2 班两端：SZX 去程 + PEK 回程",
                twinNeeds.Count == 4 && twinNeeds[2].airport == "SZX" && !twinNeeds[2].inbound
                && twinNeeds[3].airport == "PEK" && twinNeeds[3].inbound, ref fail);

            // —— 8. 非协调对端：只在本场一端要槽 ——
            var freeDest = MakeRoute("SZ400", "08:00", "10:30", destination: "AKL");
            var freeNeeds = RoutePermitLogic.SlotNeeds(freeDest);
            Console.WriteLine("R14 非协调对端需求：" + Describe(freeNeeds));
            Check("非协调对端只生成 1 条需求（本场离港）", freeNeeds.Count == 1, ref fail);
            Check("该端为本场 SZX 去程离港",
                freeNeeds.Count == 1 && freeNeeds[0].airport == "SZX" && !freeNeeds[0].inbound, ref fail);

            // —— 9. 两端均非协调：不需要任何时刻槽 ——
            var noneCoord = MakeRoute("SZ500", "08:00", "10:30", destination: "AKL", origin: "AKL");
            Check("两端均非协调 → 0 条需求", RoutePermitLogic.SlotNeeds(noneCoord).Count == 0, ref fail);

            Console.WriteLine(fail == 0 ? "SLOTCHECK OK" : $"SLOTCHECK FAIL {fail}");
            return fail == 0 ? 0 : 1;
        }

        static RouteData MakeRoute(string flightNo, string dep, string arr,
            string destination, string origin = "SZX")
        {
            var route = new RouteData
            {
                routeId = $"R-{origin}-{destination}-{flightNo}",
                origin = origin,
                destination = destination,
                distanceKm = 1940f,
                aircraftType = "A320neo",
                flightNumber = flightNo,
                registration = "B-9001",
                depTime = dep,
                arrTime = arr,
                daysOfWeek = "每日",
                weeklyFlights = 7,
                groundStaffAssigned = 3,
                baseDailyProfit = 10f,
                dailyProfit = 10f
            };
            route.SyncFlightsFromPrimary();
            return route;
        }

        static string Describe(List<RoutePermitLogic.SlotNeed> needs)
        {
            var parts = new List<string>();
            for (int i = 0; i < needs.Count; i++)
                parts.Add($"{RoutePermitLogic.LegName(needs[i])} {needs[i].airport}"
                    + $"{ScheduleSlots.SlotBandName(needs[i].band)} {needs[i].flightNo}");
            return parts.Count == 0 ? "（无）" : string.Join("、", parts);
        }

        static void Check(string name, bool ok, ref int fail)
        {
            if (!ok) fail++;
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}");
        }
    }
}
