#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 开航全流程九关（2026-11-04 用户要求）。
    ///
    /// 用户原话：
    /// <list type="number">
    /// <item>「当 CEO 下达开通一条航线的指令后，应该列出从零到航班运行的全流程。」</item>
    /// <item>「目前只是从零到取得航权和时刻就结束了，这个流程不完整，
    ///   应该补上排航班计划、定价、安排机型、维修等等一系列的流程。」</item>
    /// </list>
    ///
    /// 旧版病根：`RoutePermitLogic.NodeSlot`（第 6 关）名叫「时刻协调与开航」，
    /// 既是时刻、又是开航、又是排班，一格全包 —— 图上走到第 6 格就显示「已开航」，
    /// 而运营部的航班计划其实还躺在收件箱等 CEO 批准，第 9 关（首班载客、票价生效）从未出现在图上。
    ///
    /// 本回归钉住四件事：
    /// <list type="number">
    /// <item>节点目录是 9 关，且 7/8/9 分别叫「开航执行 / 航班计划编排 / 投入运营」；</item>
    /// <item>发证未开航 → 停在第 6 关；开航完成但编班待批 → 第 8 关；编班批完 → 第 9 关；</item>
    /// <item>研究呈报正文里能读到「航班计划编排、投入运营、机组、维修、票价」这些字眼
    ///   —— 这正是用户说的「补上排航班计划、定价、安排机型、维修」；</item>
    /// <item>负面控制：把编班件结案（模拟「其实还没排班」以外的旧判据），
    ///   确认判定会从 9 掉回 8，证明第 8/9 关不是常量。</item>
    /// </list>
    ///
    /// 隔离手段与其它回归相同：内存 GameState + 静态入口，不起壳层、不碰 UI、不写存档。
    /// </summary>
    public static class RouteLifecycleRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__route_lifecycle__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1;
                gs.quarter = 1;
                gs.SetSimUtc(new DateTime(2026, 11, 4, 2, 0, 0, DateTimeKind.Utc));
                gs.cash = 50000f;
                gs.onTimePercent = 88f;
                gs.hubIata = "PEK";
                gs.stg = new StrategyState();
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.flightRings = new List<FlightRing>();
                gs.mkt = new MktState();
                gs.fleet = new List<Aircraft>
                {
                    new Aircraft
                    {
                        registration = "B-6001", type = "B787-9", isActive = true,
                        status = AircraftStatuses.Airworthy, age = 3, condition = 96f,
                        economySeats = 260, businessSeats = 30
                    }
                };

                var route = new RouteData
                {
                    routeId = "R-PEK-LHR-LIFE", origin = "PEK", destination = "LHR",
                    distanceKm = 8145f, aircraftType = "B787-9", flightNumber = "NV242",
                    registration = "B-6001", depTime = "13:30", arrTime = "17:40",
                    daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 18f, dailyProfit = 18f, groundStaffAssigned = 4,
                    schedulePending = true
                };
                route.SyncFlightsFromPrimary();
                gs.activeRoutes = new List<RouteData> { route };

                // ———— ① 目录是九关，第 7/8/9 关各有其名 ————
                bool nodesOk = RoutePermitLogic.NodeLast == 9
                               && RoutePermitLogic.NodeLaunch == 7
                               && RoutePermitLogic.NodeSchedule == 8
                               && RoutePermitLogic.NodeOperate == 9
                               && RoutePermitLogic.NodeName(RoutePermitLogic.NodeLaunch) == "开航执行"
                               && RoutePermitLogic.NodeName(RoutePermitLogic.NodeSchedule) == "航班计划编排"
                               && RoutePermitLogic.NodeName(RoutePermitLogic.NodeOperate) == "投入运营"
                               && RoutePermitLogic.NodeName(RoutePermitLogic.NodeSlot) == "时刻协调";
                Debug.Log($"RL ① 节点目录 共 {RoutePermitLogic.NodeLast} 关（应 9）"
                          + $"　7={RoutePermitLogic.NodeName(RoutePermitLogic.NodeLaunch)}"
                          + $"　8={RoutePermitLogic.NodeName(RoutePermitLogic.NodeSchedule)}"
                          + $"　9={RoutePermitLogic.NodeName(RoutePermitLogic.NodeOperate)}"
                          + $"　{Flag(nodesOk)}");
                if (!nodesOk)
                    Debug.LogError("RL ① 节点目录不是九关：开航后的排班/运营又被塞回某一格里了。");

                // ———— ② DisplayNode 三档：发证待开航 / 编班待批 / 编班已批 ————
                var mail = new InboxMail
                {
                    id = "life1", sender = "运营部", origin = "PEK", dest = "LHR",
                    permitStage = RoutePermitLogic.StageGranted,
                    permitNode = RoutePermitLogic.NodeLaunch,
                    effectPending = true,     // 已发证、开航执行还没落地
                    permitGate = ""
                };
                int nWaitLaunch = RoutePermitLogic.DisplayNode(mail, gs);

                mail.effectPending = false;   // 开航已执行，编班还挂在收件箱（route.schedulePending = true）
                int nWaitSchedule = RoutePermitLogic.DisplayNode(mail, gs);

                // 负面控制：把 schedulePending 关掉、且不留待批件 → 应当直接到第 9 关
                route.schedulePending = false;
                int nOperate = RoutePermitLogic.DisplayNode(mail, gs);

                bool displayOk = nWaitLaunch == RoutePermitLogic.NodeSlot
                                 && nWaitSchedule == RoutePermitLogic.NodeSchedule
                                 && nOperate == RoutePermitLogic.NodeOperate;
                Debug.Log($"RL ② 进度定位　发证待开航={nWaitLaunch}"
                          + $"（应 {RoutePermitLogic.NodeSlot}）"
                          + $"　开航后编班待批={nWaitSchedule}（应 {RoutePermitLogic.NodeSchedule}）"
                          + $"　编班批完={nOperate}（应 {RoutePermitLogic.NodeOperate}）"
                          + $"　{Flag(displayOk)}");
                if (!displayOk)
                    Debug.LogError("RL ② 进度定位错档：开航后仍显示「已开航」，或编班待批被当成已运营。");

                // ———— ②b 负面控制：把 schedulePending 再打开，判定必须回到第 8 关 ————
                route.schedulePending = true;
                bool canaryBack = RoutePermitLogic.DisplayNode(mail, gs) == RoutePermitLogic.NodeSchedule;
                route.schedulePending = false;   // 复原
                Debug.Log($"RL ②b 负面控制　重新挂起编班 → 回到第 8 关={canaryBack}（应 True）"
                          + $"　{Flag(canaryBack)}");
                if (!canaryBack)
                    Debug.LogError("RL ②b canary 失效：schedulePending 翻回去节点号不变 —— "
                                   + "那么 ② 里那个 8 只能是巧合，证明不了它在看真实状态。");

                // ———— ③ 研究呈报正文：九关全貌 + 机型/编班/机组/维修/票价 ————
                string body = RoutePermitLogic.LifecycleChart(true, true)
                              + RoutePermitLogic.LifecycleNotes(true, true);
                bool bodyOk = Has(body, "共 9 个节点")
                              && Has(body, "开航执行")
                              && Has(body, "航班计划编排")
                              && Has(body, "投入运营")
                              && Has(body, "机型")
                              && Has(body, "机组")
                              && Has(body, "维修")
                              && Has(body, "票价");
                Debug.Log($"RL ③ 呈报正文含全流程={bodyOk}"
                          + $"　匹配：9 关={Has(body, "共 9 个节点")} 开航执行={Has(body, "开航执行")}"
                          + $" 编班={Has(body, "航班计划编排")} 运营={Has(body, "投入运营")}"
                          + $" 机型={Has(body, "机型")} 机组={Has(body, "机组")}"
                          + $" 维修={Has(body, "维修")} 票价={Has(body, "票价")}"
                          + $"　{Flag(bodyOk)}");
                if (!bodyOk)
                    Debug.LogError("RL ③ 全流程目录缺项：CEO 签字前看不到排班/定价/机型/维修那一段。");

                // ———— ③b 国内 + 非协调机场：第 3 关（航权）与第 6 关（时刻）应标「无需办理」 ————
                string domBody = RoutePermitLogic.LifecycleChart(false, false);
                bool skipOk = Has(domBody, "国内不涉航权额度") && Has(domBody, "非协调机场无需时刻")
                              && Has(domBody, "无需办理");
                Debug.Log($"RL ③b 国内非协调线 → 航权/时刻标无需办理={skipOk}　{Flag(skipOk)}");
                if (!skipOk)
                    Debug.LogError("RL ③b 国内线仍把航权/时刻列为待办：CEO 会去等一个不需要办的手续。");

                // ———— ④ 开航后保障四项都在（机组产能 / 维修 / 地面 / 运价） ————
                string guard = CallSafeguard(gs, route);
                bool guardOk = Has(guard, "机组") && Has(guard, "维修") && Has(guard, "地面") && Has(guard, "运价");
                Debug.Log($"RL ④ 开航后保障段含机组/维修/地面/运价={guardOk}　{Flag(guardOk)}");
                if (!guardOk)
                    Debug.LogError("RL ④ 开航后保障段缺项：CEO 看不到维修与地勤的真实状态。");

                bool all = nodesOk && displayOk && canaryBack && bodyOk && skipOk && guardOk;
                Debug.Log($"RL 开航全流程九关 → 目录={Flag(nodesOk)}　定位={Flag(displayOk)}"
                          + $"　负面控制={Flag(canaryBack)}　正文={Flag(bodyOk)}"
                          + $"　国内跳过={Flag(skipOk)}　保障段={Flag(guardOk)}　{Flag(all)}");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 走真实入口拿「开航后保障」段：<c>LaunchSafeguardBlock</c> 是 private，
        /// 但它是**唯一**一处把机组产能、维修状态、地勤编制、票价摊开写的地方，
        /// 所以这里用反射调用而不是复制一份判据 —— 复制出来的判据绿了也证明不了邮件里写了什么。
        /// </summary>
        static string CallSafeguard(GameState gs, RouteData route)
        {
            var t = typeof(InboxManager);
            var mi = t.GetMethod("LaunchSafeguardBlock",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (mi == null) return "";
            try
            {
                return (string)mi.Invoke(null, new object[] { gs, route }) ?? "";
            }
            catch (Exception e)
            {
                Debug.LogError("RL ④ 调 LaunchSafeguardBlock 失败：" + e);
                return "";
            }
        }

        static bool Has(string s, string needle)
            => !string.IsNullOrEmpty(s) && s.IndexOf(needle, StringComparison.Ordinal) >= 0;

        static string Flag(bool ok) => ok ? "✓" : "✗";
    }
}
#endif
