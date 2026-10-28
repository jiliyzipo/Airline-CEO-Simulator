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
        public static bool Run()
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
                //
                // ★2026-10-27：这条断言此前一直是红的，而且**红得没有意义** ——
                //   它依赖 TermNavMap 内部的 `GameState.Instance`，而 GameState 没有
                //   [ExecuteInEditMode]，EditMode 下 AddComponent 不跑 Awake，Instance 恒为 null。
                //   实测 [实际包裹] = 原文未变即证此。现改用 Decorate(body, host) 显式喂本地的 gs，
                //   这条断言才**第一次真正执行到**航班号包裹分支。
                string decorated = TermNavMap.Decorate($"{no2A} 红眼班，请核对时刻。", gs);
                bool decorateWave = decorated.Contains($"link=flight:{route.routeId}#2");
                // 失败时把实际包裹结果打出来：只记 False 无法区分「没包」「包错班次」「包成别的 id」。
                string decorateProof = decorated == $"{no2A} 红眼班，请核对时刻。"
                    ? $"{no2A} 未被包裹（host 上有 {gs.activeRoutes?.Count ?? 0} 条航线，号段未覆盖？）"
                    : decorated;

                // ⑧ 去程 / 回程分段时刻：两段都能取到，且回程起飞 = 去程落地 + 过站（不是同一段抄两遍）
                FlightScheduleFormat.LegClock(route, route.flights[0], out string legOut, out string legIn);
                var trip0 = FlightScheduleFormat.Trip(route, route.flights[0]);
                bool legSplit = legOut.StartsWith(route.flights[0].depTime)
                                && legOut != legIn
                                && legIn.StartsWith(trip0.inDepFull)
                                && trip0.inDepFull != route.flights[0].depTime;

                // ⑨ 跨午夜：红眼班的回程落地要标 +1，否则「23:30-01:48」会被读成回程早于去程
                //
                // ★2026-10-27：原夹具写的是 `01:00 → 06:00`，注释却说「23:30-01:48」—— 两者不是一回事。
                //   实测回程窗口 `07:14-12:14` 全程落在同一天：`BlockMinutes` 取的是 `arr − dep`（300 分），
                //   01:00 出发的 5 小时往返根本走不到午夜之后，所以这条断言**永远不可能通过**，
                //   与产品行为无关。现按注释里的原意改成真正的红眼时刻。
                //   `BlockMinutes` 对 `23:30 → 01:48` 的 span 会 +24h 得 138 分，正是跨天场景。
                var redEye = new RouteData
                {
                    routeId = "R-PEK-IST-NIGHT", origin = "PEK", destination = "IST",
                    distanceKm = 1080f, aircraftType = "A320neo", registration = "B-2004",
                    groundStaffAssigned = 3
                };
                var nightFlight = new RouteFlight
                {
                    registration = "B-2004", depTime = "23:30", arrTime = "01:48",
                    daysOfWeek = "每日", weeklyFlights = 7
                };
                FlightScheduleFormat.LegClock(redEye, nightFlight, out _, out string nightIn);
                bool crossMidnight = nightIn.Contains("+1");

                // ⑩ 航线文案用双箭头（一班 = 去程 + 回程，航线是双向的）
                string odText = AirportCatalog.OdRoundTrip(route.origin, route.destination);
                bool roundArrow = odText.Contains("⇄") && !odText.Contains("→");

                // ⑪ 周利不得把「加密」乘两遍（2026-10-27 T03.8 逐屏查出）：
                //    dailyProfit 已按 weeklyFlights/7 摊到日历日，周利只能是 dailyProfit × 7（按班次占比分）。
                //    旧式 FlightNetDailyProfit × WeeklyOf(f) 只在每班 7 班/周时恰好正确，
                //    所以本夹具（3 波 × 7 班）与旧式**结果相同**，单靠它抓不住缺陷。
                //    因此再补一条「20 班/周的班次」反例：旧式会给 12.0，正确值 4.2。
                float weeklySum = 0f;
                for (int i = 0; i < route.flights.Count; i++) weeklySum += route.WeeklyProfitOf(route.flights[i]);
                bool weeklyAddsUp = Mathf.Abs(weeklySum - route.dailyProfit * 7f) < 0.005f;

                var denseRoute = new RouteData
                {
                    routeId = "R-PEK-PVG-DENSE", origin = "PEK", destination = "PVG",
                    distanceKm = 1080f, aircraftType = "A320neo", registration = "B-2002",
                    weeklyFlights = 20, daysOfWeek = "每日", dailyProfit = 0.60f, baseDailyProfit = 0.60f,
                    groundStaffAssigned = 3
                };
                denseRoute.flights = new List<RouteFlight>
                {
                    new RouteFlight { registration = "B-2002", depTime = "08:00", arrTime = "11:26",
                                      daysOfWeek = "每日", weeklyFlights = 20 }
                };
                float denseWeekly = denseRoute.WeeklyProfitOf(denseRoute.flights[0]);
                float legacyWrong = denseRoute.FlightNetDailyProfit * FlightScheduleFormat.WeeklyOf(denseRoute.flights[0]);
                bool denseNotDoubled = Mathf.Abs(denseWeekly - 4.2f) < 0.005f
                                       && Mathf.Abs(legacyWrong - 12.0f) < 0.005f;   // 旧式确实虚高 20/7
                // 与摘要行「周利 = 日利 × 7」必须是同一个数：屏上两处周利不许打架
                bool denseMatchesSummary = Mathf.Abs(denseWeekly - denseRoute.dailyProfit * 7f) < 0.005f;

                bool all = perFlightNo && perFlightWindow && oldMismatch && netSplit && grossDiffers
                           && indexPairing && linkNew && linkLegacy && decorateWave
                           && legSplit && crossMidnight && roundArrow
                           && weeklyAddsUp && denseNotDoubled && denseMatchesSummary;
                Debug.Log($"运营计划页口径 → 逐班航班号={perFlightNo}（期望 True）"
                          + $"　逐班往返窗口={perFlightWindow}（期望 True）"
                          + $"　航线级/班次级周班确有差={oldMismatch}（期望 True）"
                          + $"　单班净利均摊={netSplit}（期望 True）"
                          + $"　净额≠毛贡献={grossDiffers}（期望 True）"
                          + $"　班次下标随航线作废={indexPairing}（期望 True）"
                          + $"　航班号链接带下标={linkNew}（期望 True）　老链接兼容={linkLegacy}（期望 True）"
                          + $"　正文第3波带 #2={decorateWave}（期望 True）"
                          + $"　[实际包裹] {decorateProof}"
                          + $"　去程/回程分段={legSplit}（期望 True）"
                          + $"　跨午夜标 +1={crossMidnight}（期望 True）"
                          + $"　[红眼回程实测] {nightIn}（23:30 出发 · 1080 km · A320neo）"
                          + $"　城对双箭头={roundArrow}（期望 True）"
                          + $"　Σ各班周利==航线日利×7={weeklyAddsUp}（期望 True）"
                          + $"　{route.WeeklyProfitOf(route.flights[0]):F2}×3={weeklySum:F2} vs {route.dailyProfit * 7f:F2}"
                          + $"　密班周利不翻倍={denseNotDoubled}（期望 True）"
                          + $"　20班/周 → 新 {denseWeekly:F1} / 旧 {legacyWrong:F1}（旧式虚高 20/7）"
                          + $"　密班周利==摘要周利={denseMatchesSummary}（期望 True）"
                          + $"　{(all ? "✓" : "✗")}");
                return all;
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
