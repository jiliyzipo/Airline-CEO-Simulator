#if UNITY_EDITOR
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 运营计划两页的口径回归（2026-10-19）：
    /// · n42「航班周总计划」—— 一个**班次**一行，而不是一条航线一行；
    /// · n43「航班飞行计划」—— **一个班次**一页，而不是挂着班次号显示航线级数据。
    ///
    /// 背景：多波次航线（ICN 21 班/周 = 3 波）在旧版两页里都只露出 `flights[0]`：
    /// 周计划每线一行、时刻/航班号都取首班；详细页标题写 NS108、正文却是整条线的
    /// 「每日 · 21 班/周」。页面看着完全正常，只是**少掉了第二、第三波**。
    ///
    /// 与 <see cref="InboxRouteLinkRegression"/> 一样是隔离回归：只碰内存里的 GameState 与静态函数，
    /// 不依赖任何 Panel / Prefab，也不会写存档。
    /// </summary>
    public static class SchedulePageRegression
    {
        public static void Run()
        {
            var go = new GameObject("__schedule_page_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1; gs.quarter = 1;
                gs.onTimePercent = 88f;

                // 3 波航线：日净利 1.05 万 → 每班净利 0.35 万；每班毛贡献 0.42 万（旧版误当「日利」印的那个数）
                var route = new RouteData
                {
                    routeId = "R-PEK-PVG-MULTI",
                    origin = "PEK", destination = "PVG", distanceKm = 1080f,
                    aircraftType = "A320neo", registration = "B-2000", flightNumber = "NS100",
                    weeklyFlights = 21, daysOfWeek = "每日", depTime = "08:00", arrTime = "11:26",
                    baseDailyProfit = 0.60f, dailyProfit = 1.05f,
                    groundStaffAssigned = 3, season = "夏秋", openedDate = "第1年 Q1",
                    score = 70, rating = "B"
                };
                route.flights = new List<RouteFlight>
                {
                    Wave("08:00", "11:26"), Wave("14:00", "17:26"), Wave("20:00", "23:26")
                };
                gs.activeRoutes = new List<RouteData> { route };

                // ① 每班都有自己的航班号与往返窗口 → 逐班展开才有意义
                FlightScheduleFormat.Numbers(route, 0, out string no0A, out string no0B);
                FlightScheduleFormat.Numbers(route, 1, out string no1A, out string no1B);
                FlightScheduleFormat.Numbers(route, 2, out string no2A, out string no2B);
                bool perFlightNo = no0A == "NS100" && no0B == "NS101"
                                   && no1A == "NS102" && no1B == "NS103"
                                   && no2A == "NS104" && no2B == "NS105";
                string w0 = FlightScheduleFormat.WindowLabel(route, route.flights[0]);
                string w2 = FlightScheduleFormat.WindowLabel(route, route.flights[2]);
                bool perFlightWindow = w0 != w2 && w0.StartsWith("08:00") && w2.StartsWith("20:00");

                // ② 旧版口径错位：航线级周班 21 ≠ 首班周班 7（这正是「NS108 却写每日 21 班/周」的来源）
                bool oldMismatch = route.weeklyFlights == 21
                                   && FlightScheduleFormat.WeeklyOf(route.flights[0]) == 7
                                   && FlightScheduleFormat.WeeklyOf(route.flights[2]) == 7;

                // ③ 单班日利取净额均摊：Σ 各班 == 航线日利（周计划逐行相加必须回到卡片上的那个数）
                float per = route.FlightNetDailyProfit;
                bool netSplit = Mathf.Abs(per - 0.35f) < 0.005f
                                && Mathf.Abs(per * route.FlightCount - route.dailyProfit) < 0.005f;

                // ④ 净额与「毛贡献份额」确实不是同一个数 —— 否则③是空转（固定成本为 0 时分不出来）
                bool grossDiffers = Mathf.Abs(route.flights[0].dailyProfit - per) > 0.01f;

                // ⑤ 班次下标必须与航线成对：同线取得到，换线一律视为未指定（否则停在上一条线的第 N 班）
                OpsSubpageContext.SelectFlight(route.routeId, 2);
                bool indexPairing = OpsSubpageContext.FlightIndexOf(route.routeId) == 2
                                    && OpsSubpageContext.FlightIndexOf("R-PEK-CAN") == -1;

                // ⑥ 邮件里的航班号链接带上班次下标；老格式（无 #）仍解析得出航线，下标按未指定处理
                bool linkNew = TermNavMap.TryResolve($"flight:{route.routeId}#2", out var hitNew)
                               && hitNew.routeKey == route.routeId && hitNew.flightIndex == 2
                               && hitNew.pageId.StartsWith("n43");
                bool linkLegacy = TermNavMap.TryResolve($"flight:{route.routeId}", out var hitOld)
                                  && hitOld.routeKey == route.routeId && hitOld.flightIndex == -1;

                // ⑦ 正文里的第 3 波航班号（NS104）必须包成 #2 的链接，点下去才落到那一班
                string decorated = TermNavMap.Decorate($"{no2A} 红眼班，请核对时刻。");
                bool decorateWave = decorated.Contains($"link=flight:{route.routeId}#2");

                // ⑧ 去程 / 回程分段时刻：两段都能取到，且回程起飞 = 去程落地 + 过站（不是同一段抄两遍）
                FlightScheduleFormat.LegClock(route, route.flights[0], out string legOut, out string legIn);
                var trip0 = FlightScheduleFormat.Trip(route, route.flights[0]);
                bool legSplit = legOut.StartsWith(route.flights[0].depTime)
                                && legOut != legIn
                                && legIn.StartsWith(trip0.inDepFull)
                                && trip0.inDepFull != route.flights[0].depTime;

                // ⑨ 跨午夜：红眼班的回程落地要标 +1，否则「23:30-01:48」会被读成回程早于去程
                var redEye = new RouteData
                {
                    routeId = "R-PEK-IST-NIGHT", origin = "PEK", destination = "IST",
                    distanceKm = 7000f, aircraftType = "A330-300", registration = "B-2004",
                    groundStaffAssigned = 3
                };
                var nightFlight = new RouteFlight
                {
                    registration = "B-2004", depTime = "01:00", arrTime = "06:00",
                    daysOfWeek = "每日", weeklyFlights = 7
                };
                FlightScheduleFormat.LegClock(redEye, nightFlight, out _, out string nightIn);
                bool crossMidnight = nightIn.Contains("+1");

                // ⑩ 航线文案用双箭头（一班 = 去程 + 回程，航线是双向的）
                string odText = AirportCatalog.OdRoundTrip(route.origin, route.destination);
                bool roundArrow = odText.Contains("⇄") && !odText.Contains("→");

                bool all = perFlightNo && perFlightWindow && oldMismatch && netSplit && grossDiffers
                           && indexPairing && linkNew && linkLegacy && decorateWave
                           && legSplit && crossMidnight && roundArrow;
                Debug.Log($"运营计划页口径 → 逐班航班号={perFlightNo}（期望 True）"
                          + $"　逐班往返窗口={perFlightWindow}（期望 True）"
                          + $"　航线级/班次级周班确有差={oldMismatch}（期望 True）"
                          + $"　单班净利均摊={netSplit}（期望 True）"
                          + $"　净额≠毛贡献={grossDiffers}（期望 True）"
                          + $"　班次下标随航线作废={indexPairing}（期望 True）"
                          + $"　航班号链接带下标={linkNew}（期望 True）　老链接兼容={linkLegacy}（期望 True）"
                          + $"　正文第3波带 #2={decorateWave}（期望 True）"
                          + $"　去程/回程分段={legSplit}（期望 True）"
                          + $"　跨午夜标 +1={crossMidnight}（期望 True）"
                          + $"　城对双箭头={roundArrow}（期望 True）"
                          + $"　{(all ? "✓" : "✗")}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        static RouteFlight Wave(string dep, string arr) => new RouteFlight
        {
            registration = "B-2000",
            depTime = dep,
            arrTime = arr,
            daysOfWeek = "每日",
            weeklyFlights = 7,
            slotLabel = ScheduleSlots.LabelForDep(dep),
            dutyStart = LiveClockLogic.ParseMinutes(dep) - OpsRotationParams.ReportBeforeMinutes,
            dailyProfit = 0.42f
        };
    }
}
#endif
