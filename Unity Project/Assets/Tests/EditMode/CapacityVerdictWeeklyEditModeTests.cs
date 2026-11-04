using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 运力批复回执的裁定档位（2026-11-03 用户报障）。
    ///
    /// 报障原文：「关于增加 PEK-LHR 航线航班的第一封邮件是航班量调整为每日一趟往返，
    /// 第二封邮件却说运力未变动。发现 PEK-IST 航线也有这个问题。这是矛盾吗？」
    ///
    /// 不是矛盾，是**裁定只看每日波次**造成的假话：
    /// 「3 班/周（周一/三/五）→ 每日 1 班」前后都是 1 波，`beforeWaves == now == 1`，
    /// 于是被判成「一班未动」，回执标题写「运力未变动（维持 1 段）」、正文写「与指令前完全一致」
    /// —— 而同一封邮件下面那行「每周班次（离港日）」明明写着 7 班。
    ///
    /// 判据改成「波次没变 **且** 周频没变」才算未变动。
    /// </summary>
    public class CapacityVerdictWeeklyEditModeTests
    {
        static GameState NewGame(out GameObject host)
        {
            host = new GameObject("__capweekly__");
            var gs = host.AddComponent<GameState>();
            gs.EnsureDefaults();
            BindInstance(gs);
            gs.year = 1;
            gs.quarter = 1;
            gs.SetSimUtc(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
            gs.cash = 50000f;
            gs.onTimePercent = 88f;
            gs.stg = new StrategyState();
            gs.inbox = new List<InboxMail>();
            gs.pendingInbox = new List<PendingInboxMail>();
            gs.flightRings = new List<FlightRing>();
            gs.fleet = new List<Aircraft>
            {
                new Aircraft { registration = "B-3000", type = "A320neo", isActive = true,
                               status = AircraftStatuses.Airworthy, age = 4 },
                new Aircraft { registration = "B-3001", type = "A320neo", isActive = true,
                               status = AircraftStatuses.Airworthy, age = 6 }
            };
            return gs;
        }

        static void BindInstance(GameState gs)
        {
            var prop = typeof(GameState).GetProperty(
                "Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(prop, "GameState.Instance 改了名字，这条回归需要跟着改");
            prop.GetSetMethod(true).Invoke(null, new object[] { gs });
        }

        /// <summary>
        /// 报障形态：**每周 3 班（周一/三/五）、每个执飞日 1 段往返**，要加密到每日。
        /// 用短程线做夹具：长程每日往返受机队/机组轮换所限常常只排到 4~5 班，
        /// 而这里要验的是「排满了却仍被判成没动」，必须让目标真的达成。
        /// </summary>
        static RouteData AddThreeDayRoute(GameState gs)
        {
            var route = new RouteData
            {
                routeId = "R-PEK-SHA-W",
                origin = "PEK",
                destination = "SHA",
                distanceKm = 1077f,
                aircraftType = "A320neo",
                flightNumber = "NS309",
                registration = "B-3000",
                depTime = "06:30",
                arrTime = "07:46",
                daysOfWeek = "周一/周三/周五",
                weeklyFlights = 3,
                baseDailyProfit = 10f,
                dailyProfit = 10f,
                groundStaffAssigned = 3
            };
            route.SyncFlightsFromPrimary();
            gs.activeRoutes = new List<RouteData> { route };
            return route;
        }

        static InboxMail FindCapacityReport(GameState gs)
            => gs.inbox.Find(x => x != null && x.action == InboxManager.CapacityDirectiveAction);

        static InboxMail FindReceipt(GameState gs)
            => gs.inbox.Find(x => x != null && !string.IsNullOrEmpty(x.id) && x.id.StartsWith("fx_cap_", StringComparison.Ordinal));

        /// <summary>走一个工作日的投递（回执按 <see cref="InboxCadence.ReplyReady"/> 延后 4 小时）。</summary>
        static void FlushPending(GameState gs, int hours = 8)
        {
            for (int h = 0; h < hours; h++)
            {
                gs.SetSimUtc(gs.SimUtc.AddHours(1));
                InboxManager.DeliverDue(gs, gs.SimUtc);
            }
        }

        /// <summary>
        /// 核心回归：「每周 3 班 → 每日」只动了班期、没动波次，回执必须说「已生效」。
        /// </summary>
        [Test]
        public void WeeklyDensify_WithoutChangingDailyWaves_IsReportedAsEffective()
        {
            var gs = NewGame(out var host);
            try
            {
                var route = AddThreeDayRoute(gs);
                int wavesBefore = route.FlightCount;      // 1
                int weeklyBefore = route.WeeklyFlightsDeparture(); // 3
                Assert.AreEqual(1, wavesBefore, "夹具前提：每个执飞日 1 段往返");
                Assert.AreEqual(3, weeklyBefore, "夹具前提：每周 3 班");

                var rep = CapacityDirectiveLogic.Execute(
                    gs, route, CapacityDirectiveLogic.Parse("加密到每日1班往返"));
                Assert.IsTrue(rep.ok, rep.note);
                InboxManager.QueueCapacityReport(gs, route, rep, 0);   // 0 = 立即入箱：验内容不验延迟

                var report = FindCapacityReport(gs);
                Assert.IsNotNull(report, "没生成运力呈报");
                int weeklyAfter = route.WeeklyFlightsDeparture();
                Assert.AreEqual(wavesBefore, report.capBeforeWaves);
                Assert.AreEqual(weeklyBefore, report.capBeforeWeekly,
                    "周频没随呈报留档：回执就分不清「一班没动」和「只动了班期」");
                Assert.AreEqual(weeklyAfter, report.capTargetWeekly);

                // 线路此刻确实加密了，而波次仍是 1 —— 这正是报障里「第一封邮件」说的那件事
                Assert.AreNotEqual(weeklyBefore, weeklyAfter, "夹具没真的改动班期，测不到这个缺陷");
                Assert.AreEqual(1, route.FlightCount, "波次没变（前后都是每个执飞日 1 段）");

                Assert.IsTrue(InboxManager.ApproveCapacityDirective(gs, report, out string msg), msg);
                FlushPending(gs);

                var receipt = FindReceipt(gs);
                Assert.IsNotNull(receipt, "批准后没发批复回执");
                StringAssert.Contains("已生效", receipt.subject,
                    $"回执标题说「{receipt.subject}」—— 周频已从 {weeklyBefore} 班加到 {weeklyAfter} 班，不许说未变动");
                StringAssert.DoesNotContain("未变动", receipt.subject);
                StringAssert.DoesNotContain("一班未动", receipt.body,
                    "正文说「一班未动」，与同一封邮件里的周频前后对比自相矛盾");
                StringAssert.Contains($"{weeklyBefore} 班 → {weeklyAfter} 班", receipt.body,
                    "回执必须把周频的前后对比写出来，否则 CEO 看不出这次动了什么");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 反面控制：真的一班没动时，仍必须说「未变动」—— 不许为了修上一个 bug 把这条改成「已生效」。
        /// 夹具直接摆「已经每日 7 班、目标也是每日 1 段／每周 7 班」的待批呈报。
        /// </summary>
        [Test]
        public void TrulyUnchanged_IsStillReportedAsUnchanged()
        {
            var gs = NewGame(out var host);
            try
            {
                var route = AddThreeDayRoute(gs);
                route.weeklyFlights = 7;
                route.daysOfWeek = "每日";
                route.SyncFlightsFromPrimary();
                Assert.AreEqual(7, route.WeeklyFlightsDeparture());
                Assert.AreEqual(1, route.FlightCount);

                var mail = new InboxMail
                {
                    id = "m_cap_dir_R-PEK-SHA-W_9",
                    sender = "运营部",
                    role = "运营部负责人",
                    kind = "report",
                    action = InboxManager.CapacityDirectiveAction,
                    origin = route.routeId,
                    dest = route.destination,
                    subject = "关于 PEK—LHR 运力调整与航班环的报告（请批复）",
                    status = "pending",
                    receivedYear = gs.year,
                    receivedQuarter = gs.quarter,
                    directiveNote = "加密到每日1班往返",
                    capTargetWaves = 1,
                    capBeforeWaves = 1,
                    capTargetWeekly = 7,
                    capBeforeWeekly = 7,
                    deadlineQuarters = InboxManager.DefaultDeadlineQuarters
                };
                InboxManager.Add(gs, mail);

                Assert.IsTrue(InboxManager.ApproveCapacityDirective(gs, mail, out string msg), msg);
                FlushPending(gs);

                var receipt = FindReceipt(gs);
                Assert.IsNotNull(receipt, "批准后没有回执");
                StringAssert.Contains("未变动", receipt.subject,
                    $"这次真的一个班次都没动，回执却写「{receipt.subject}」");
                StringAssert.Contains("一班未动", receipt.body);
                StringAssert.DoesNotContain("已生效", receipt.subject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>旧档（呈报没记周频）：退回纯波次口径，不许凭空断言「动了」。</summary>
        [Test]
        public void LegacyReportWithoutWeeklyFacts_FallsBackToWavesOnly()
        {
            var gs = NewGame(out var host);
            try
            {
                var route = AddThreeDayRoute(gs);
                var v = CapacityDirectiveLogic.VerdictOf(route, 1, 1, 0, 0);
                Assert.AreEqual(CapacityDirectiveLogic.CapVerdict.Unchanged, v,
                    "旧档没有周频可比，此时仍应保守地说「未变动」");
                Assert.AreEqual(CapacityDirectiveLogic.CapVerdict.Keep,
                    CapacityDirectiveLogic.VerdictOf(route, -1, 1, 0, 0));
                Assert.AreEqual(CapacityDirectiveLogic.CapVerdict.Unknown,
                    CapacityDirectiveLogic.VerdictOf(route, 0, 1, 0, 0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
