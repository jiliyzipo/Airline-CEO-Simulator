using System;
using System.Collections;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>Behavior regressions for fiscal boundaries and batched elapsed time.</summary>
    public class DevelopmentTimePipelineTests : IsolatedPlayModeFixture
    {
        readonly List<GameObject> owned = new List<GameObject>();

        GameState State()
        {
            if (Application.companyName != "AirlineCEORegression")
                Assert.Ignore("Run in the isolated baseline project, never against player preferences.");
            var go = new GameObject("DevelopmentClockGS"); owned.Add(go);
            var gs = go.AddComponent<GameState>();
            gs.year = 1; gs.quarter = 1; gs.cash = 10000f;
            gs.monthlyBurnRate = 30f;
            gs.activeRoutes.Clear(); gs.fleet.Clear();
            gs.SetSimUtc(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            gs.simDay = 1;
            return gs;
        }

        TimeManager Clock()
        {
            var go = new GameObject("DevelopmentClock"); owned.Add(go);
            var clock = go.AddComponent<TimeManager>();
            TimeManager.SimulationEnabled = false;
            return clock;
        }

        [TearDown]
        public void Cleanup()
        {
            TimeManager.SimulationEnabled = false;
            // Only the isolated test project is allowed to execute these side-effecting managers.
            foreach (var manager in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (manager != null && manager.GetType().Namespace == "AirlineCEO.Systems")
                    UnityEngine.Object.DestroyImmediate(manager.gameObject);
            owned.Clear();
        }

        [Test]
        public void QuarterCallback_SeesBoundaryClock_NotPreviousQuarterTime()
        {
            var gs = State(); var clock = Clock();
            gs.simDay = 90;
            var start = new DateTime(2026, 3, 31, 23, 59, 0, DateTimeKind.Utc);
            gs.SetSimUtc(start);
            DateTime observed = DateTime.MinValue;
            clock.OnQuarterAdvanced += result => observed = gs.SimUtc;
            clock.AdvanceGameMinutes(gs, 2);
            Assert.AreEqual(start.AddMinutes(1), observed, "The closed quarter must include its final minute before settlement.");
            Assert.AreEqual(start.AddMinutes(2), gs.SimUtc);
            Assert.AreEqual(2, gs.quarter);
            Assert.AreEqual(1, gs.simDay);
        }

        [Test]
        public void ThreeDayAdvance_AccruesEveryDay_NotOnlyCatchUpTail()
        {
            var gs = State(); var clock = Clock();
            float opening = gs.cash;
            float daily = LiveClockLogic.DailyFixedCostWan(gs.monthlyBurnRate, gs.FleetMonthlyMaintenance,
                gs.FleetMonthlyLease, gs.CeoMonthlyPayWan, gs.financeCrisisBurnBonus,
                gs.OperationalPayrollMonthlyWan, gs.LaborCostMonthlyWan);
            clock.AdvanceGameMinutes(gs, 3 * 24 * 60);
            Assert.AreEqual(opening - 3 * daily, gs.cash, 0.05f);
            Assert.AreEqual(3 * daily, gs.liveQuarterExpense, 0.01f);
            Assert.AreEqual(4, gs.simDay);
        }

        [Test]
        public void SpeedAndSessionStart_RespectConfirmedRatios()
        {
            var gs = State(); var clock = Clock();
            Assert.AreEqual(1f / 60f, GameSpeed.GameMinutesPerRealSecond[GameSpeed.L1]);
            Assert.AreEqual(1f, GameSpeed.GameMinutesPerRealSecond[GameSpeed.L2]);
            Assert.AreEqual(3f, GameSpeed.GameMinutesPerRealSecond[GameSpeed.L3]);
            var savedUtc = gs.SimUtc;
            GameSpeed.Set(GameSpeed.L3);
            clock.BeginPlaySession();
            Assert.AreEqual(GameSpeed.L1, GameSpeed.Level);
            Assert.AreEqual(savedUtc, gs.SimUtc, "Loading a session must not reset the saved UTC clock.");
        }

        [Test]
        public void InquiryAndReplyDelay_DoNotPerformACommandOrArriveInstantly()
        {
            var gs = State();
            var route = new RouteData { routeId = "TIME-QUERY", origin = "PEK", destination = "SHA",
                aircraftType = "A320neo", depTime = "08:00", arrTime = "09:30", weeklyFlights = 7 };
            route.SyncFlightsFromPrimary(); gs.activeRoutes.Add(route);
            var before = JsonUtility.ToJson(route);
            Assert.IsTrue(CapacityDirectiveLogic.LooksLikeQuestion("为什么不能增加这条航线的班次？"));
            Assert.IsTrue(CapacityDirectiveLogic.LooksLikeQuestion("请解释为什么不能增加这条航线的班次？"));
            Assert.IsFalse(CapacityDirectiveLogic.LooksLikeQuestion("为什么不能增加班次？请增加到每日3班"));
            Assert.IsFalse(CapacityDirectiveLogic.LooksLikeQuestion("能不能加密到每日3班？"));
            OpsQueryLogic.Build(gs, route, "为什么不能增加这条航线的班次？");
            Assert.AreEqual(before, JsonUtility.ToJson(route));
            Assert.AreEqual(gs.SimUtc.AddHours(4), InboxCadence.ReplyReady(gs.SimUtc));
            var notice = new InboxMail { action = "news", status = "pending", receivedYear = 1,
                receivedQuarter = 1, deadlineQuarters = 0 };
            Assert.IsFalse(InboxManager.RequiresDecision(notice));
        }

        [Test]
        public void Deadline_OnlyUnresolvedDecisionsMayBlockTime()
        {
            var gs = State();
            var mail = new InboxMail { action = InboxManager.ApplySlotAction, status = "pending",
                receivedYear = 1, receivedQuarter = 1, deadlineQuarters = 1 };
            gs.inbox.Clear(); gs.inbox.Add(mail);
            Assert.IsFalse(InboxManager.IsDue(mail, 1, 2), "The deadline quarter is still available for a decision.");
            gs.quarter = 3;
            Assert.IsTrue(InboxManager.HasDueMails(gs));
            mail.meetingOutcome = "deferred";
            Assert.IsFalse(InboxManager.HasDueMails(gs));
            mail.meetingOutcome = ""; mail.status = "rejected";
            Assert.IsFalse(InboxManager.HasDueMails(gs));
            mail.status = "pending"; mail.action = "news";
            Assert.IsFalse(InboxManager.HasDueMails(gs), "A non-decision notice must never become a forced overdue decision.");
        }

        [Test]
        public void ClockQuarterEnd_DoesNotPayForecastRevenueForFlightsThatNeverLanded()
        {
            var gs = State(); var clock = Clock();
            gs.fleet.Add(new Aircraft { registration = "B-LEDGER", type = "A320neo", isActive = true,
                status = AircraftStatuses.Airworthy, condition = 100 });
            var route = new RouteData { routeId = "LEDGER", origin = "PEK", destination = "SHA",
                registration = "B-LEDGER", aircraftType = "A320neo", depTime = "08:00", arrTime = "09:30",
                dailyProfit = 12, baseDailyProfit = 12, weeklyFlights = 7 };
            route.SyncFlightsFromPrimary(); gs.activeRoutes.Add(route);
            gs.liveQuarterIncome = 5; gs.liveQuarterExpense = 20;
            gs.simDay = 90; gs.SetSimUtc(new DateTime(2026, 3, 31, 23, 59, 0, DateTimeKind.Utc));
            QuarterResult closed = null;
            clock.OnQuarterAdvanced += value => closed = value;
            clock.AdvanceGameMinutes(gs, 1);
            Assert.IsNotNull(closed);
            Assert.AreEqual(5, closed.quarterIncome, 0.001f,
                "A forecast at the last minute must not retroactively pay an entire quarter of unflown service.");
            Assert.AreEqual(0, closed.netChange, 0.001f, "Already booked operating cash must not be booked again.");
        }

        [UnityTest]
        public IEnumerator RealTimeUpdate_OverdueDecisionCannotBeBypassedByChangingSpeed()
        {
            var gs = State(); Clock();
            gs.quarter = 3;
            gs.inbox.Clear();
            gs.inbox.Add(new InboxMail { action = InboxManager.ApplySlotAction, status = "pending",
                receivedYear = 1, receivedQuarter = 1, deadlineQuarters = 0 });
            var before = gs.SimUtc;
            TimeManager.SimulationEnabled = true; GameSpeed.Set(GameSpeed.L3);
            yield return null; yield return null;
            Assert.IsTrue(GameSpeed.IsPaused);
            Assert.AreEqual(before, gs.SimUtc);
            gs.inbox[0].status = "rejected";
            GameSpeed.Set(GameSpeed.L1);
            yield return null;
            Assert.IsFalse(GameSpeed.IsPaused);
        }
    }
}
