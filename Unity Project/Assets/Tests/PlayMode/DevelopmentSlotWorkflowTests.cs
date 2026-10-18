using System;
using System.Collections.Generic;
using System.Linq;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.PlayMode
{
    public class DevelopmentSlotWorkflowTests : IsolatedPlayModeFixture
    {
        GameState Fixture(out RouteData route)
        {
            if (Application.companyName != "AirlineCEORegression") Assert.Ignore("Isolated baseline only.");
            var gs = new GameObject("SlotWorkflowGS").AddComponent<GameState>();
            gs.year = 1; gs.quarter = 1; gs.cash = 100000f;
            gs.SetSimUtc(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
            gs.fleet.Clear(); gs.activeRoutes.Clear(); gs.inbox.Clear(); gs.pendingInbox.Clear();
            gs.stg = new StrategyState();
            for (int i = 0; i < 3; i++) gs.fleet.Add(new Aircraft { registration = "B-31" + i,
                type = "A320neo", status = AircraftStatuses.Airworthy, isActive = true, condition = 100 });
            route = new RouteData { routeId = "PIPELINE-PEK-SHA", origin = "PEK", destination = "SHA",
                aircraftType = "A320neo", registration = "B-310", flightNumber = "NG310",
                distanceKm = 1077, depTime = "08:00", arrTime = "09:16", daysOfWeek = "每日", weeklyFlights = 7,
                baseDailyProfit = 12, dailyProfit = 12, groundStaffAssigned = 3 };
            route.SyncFlightsFromPrimary(); gs.activeRoutes.Add(route);
            return gs;
        }

        [Test]
        public void PartialSettlement_WaitsForOtherStation_ThenProducesCeoAssignment()
        {
            var gs = Fixture(out var route);
            var needs = RoutePermitLogic.ClockNeeds(route, "18:00");
            Assert.AreEqual(2, needs.Count);
            var applications = new List<InboxMail>();
            foreach (var need in needs)
            {
                Assert.IsTrue(InboxManager.QueueApplySlots(gs, need.airport, need.time, 1, "pipe_" + need.airport,
                    out string note, routeId: route.routeId, flightClock: "18:00"), note);
                applications.Add(gs.inbox[0]);
            }
            float cash = gs.cash;
            Assert.IsTrue(InboxManager.Approve(gs, applications[0], out _));
            gs.quarter = 2; InboxManager.ProcessPendingEffects(gs);
            Assert.IsFalse(RoutePermitLogic.CanScheduleClock(gs, route, "18:00", true));
            Assert.AreEqual(1, route.FlightCount);
            Assert.IsTrue(InboxManager.Approve(gs, applications[1], out _));
            gs.quarter = 3; InboxManager.ProcessPendingEffects(gs);
            Assert.IsTrue(RoutePermitLogic.CanScheduleClock(gs, route, "18:00", true));
            Assert.AreEqual(cash, gs.cash, "Coordinator allocation is free.");
            Assert.AreEqual(1, route.FlightCount, "Settlement itself does not impersonate CEO approval.");
            Assert.IsTrue(gs.inbox.Any(x => x.action == InboxManager.AssignFlightAction && InboxManager.IsOpen(x)));
            int holdings = gs.stg.slots.Count;
            InboxManager.ProcessPendingEffects(gs);
            Assert.AreEqual(holdings, gs.stg.slots.Count, "Settled effects must not replay.");
            var assignment = gs.inbox.Find(x => x.action == InboxManager.AssignFlightAction && InboxManager.IsOpen(x));
            Assert.IsTrue(InboxManager.ApproveAssignFlight(gs, assignment, out string execution), execution);
            Assert.Greater(route.FlightCount, 1, execution);
            DateTime departure = new DateTime(2026, 1, 5, 18, 0, 0) - GameState.LocalUtcOffset;
            gs.SetSimUtc(departure.AddHours(6));
            LiveClockSimulator.Advance(gs, departure.AddMinutes(-1), gs.SimUtc);
            Assert.Greater(gs.liveQuarterIncome, 0f, "The newly approved flight must actually operate and earn revenue.");
            Assert.IsTrue(gs.fleet.Any(a => a.flightCycles > 0));
        }

        [Test]
        public void RejectDirective_RestoresBaseline_WithoutChargingOrAcquiringSlots()
        {
            var gs = Fixture(out var route); float cash = gs.cash;
            var report = CapacityDirectiveLogic.Execute(gs, route, CapacityDirectiveLogic.Parse("增加到每日3段往返"));
            Assert.IsTrue(InboxManager.QueueCapacityReport(gs, route, report));
            var mail = gs.inbox.Find(x => x.action == InboxManager.CapacityDirectiveAction && InboxManager.IsOpen(x));
            Assert.IsTrue(InboxManager.RejectCapacityDirective(gs, mail, out _));
            Assert.AreEqual("rejected", mail.status);
            Assert.AreEqual(1, route.FlightCount);
            Assert.AreEqual(cash, gs.cash);
            Assert.IsFalse(route.directivePending);
            Assert.IsFalse(gs.inbox.Any(x => x.action == InboxManager.ApplySlotAction && x.effectPending));
            Assert.AreEqual(0, gs.stg.slots.Count);
        }

        [TestCase("pending")]
        [TestCase("approved")]
        [TestCase("assignment")]
        [TestCase("airborne")]
        public void Application_SaveLoad_PreservesStageAndExactClocks(string stage)
        {
            var gs = Fixture(out var route);
            Assert.IsTrue(InboxManager.QueueApplySlots(gs, "PEK", "18:00", 1, "save_slot", out _,
                routeId: route.routeId, flightClock: "18:00"));
            if (stage != "pending") Assert.IsTrue(InboxManager.Approve(gs, gs.inbox[0], out _));
            if (stage == "assignment")
            {
                foreach (var need in RoutePermitLogic.ClockNeeds(route, "18:00"))
                    Assert.IsTrue(StrategyManager.AcquireSlotsAt(gs, need.airport, need.time, 1, true, out _));
                Assert.IsTrue(InboxManager.QueueAssignFlight(gs, route, "B-311", "18:00", out _));
            }
            if (stage == "airborne")
            {
                DateTime departure = new DateTime(2026, 1, 5, 8, 0, 0) - GameState.LocalUtcOffset;
                gs.SetSimUtc(departure.AddMinutes(1));
                LiveClockSimulator.Advance(gs, departure.AddMinutes(-1), gs.SimUtc);
                Assert.IsTrue(route.flights[0].airborne, "The flight must actually take off before saving.");
            }
            var save = new GameObject("SlotWorkflowSave").AddComponent<SaveManager>();
            save.Save();
            gs.inbox.Clear(); gs.cash = -123;
            save.Load();
            var restored = gs.inbox.Find(x => x.id == "save_slot");
            Assert.IsNotNull(restored);
            Assert.AreEqual(stage != "pending", restored.effectPending);
            Assert.AreEqual(stage == "pending" ? "pending" : "approved", restored.status);
            Assert.AreEqual("PEK", restored.origin);
            Assert.AreEqual("18:00", restored.slotClock);
            Assert.AreEqual(route.routeId, restored.slotRouteId);
            if (stage != "pending") Assert.AreEqual(2, restored.effectQuarter);
            if (stage == "assignment") Assert.IsTrue(gs.inbox.Any(x => x.action == InboxManager.AssignFlightAction && InboxManager.IsOpen(x)));
            if (stage == "airborne") Assert.IsTrue(gs.activeRoutes[0].flights[0].airborne);
        }

        [Test]
        public void QuestionAndCrewShortage_DoNotSilentlyAddFlights()
        {
            var gs = Fixture(out var route);
            var before = JsonUtility.ToJson(route);
            var answer = OpsQueryLogic.Build(gs, route, "为什么不能增加班次？");
            Assert.AreEqual(before, JsonUtility.ToJson(route));
            gs.staffPool = new List<PersonnelData> {
                new PersonnelData { id = "one-pilot", department = "OPS", jobTitle = "飞行员", isOperational = true, onMarket = false },
                new PersonnelData { id = "one-cabin", department = "OPS", jobTitle = "乘务员", isOperational = true, onMarket = false }
            };
            CapacityDirectiveLogic.Execute(gs, route, CapacityDirectiveLogic.Parse("增加到每日3段往返"));
            Assert.AreEqual(1, route.FlightCount, "No new wave may bypass either crew role.");
            gs.fleet[0].status = AircraftStatuses.Retired;
            Assert.AreNotEqual(GroundReason.None, FlightGroundLogic.RouteOf(gs, route));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TimeChange_SettlesOnlyWhenCoordinatorHasCapacity(bool fillTargetPool)
        {
            var gs = Fixture(out var route);
            Assert.IsTrue(StrategyManager.AcquireSlotsAt(gs, "PEK", "09:30", 1, true, out _));
            Assert.IsTrue(InboxManager.QueueChangeSlot(gs, "PEK", "09:30", "12:00", 1, "change-test", out _));
            var mail = gs.inbox.Find(x => x.id == "change-test");
            Assert.IsTrue(InboxManager.Approve(gs, mail, out _));
            if (fillTargetPool)
            {
                int cap = RoutePermitLogic.SlotsAvailable(gs, "PEK", ScheduleSlots.BandOfDep("12:00"));
                Assert.IsTrue(StrategyManager.AcquireSlotsAt(gs, "PEK", "12:00", cap, true, out _));
            }
            float cash = gs.cash;
            gs.quarter = 2; InboxManager.ProcessPendingEffects(gs);
            bool oldHolding = gs.stg.slots.Any(s => s.airport == "PEK" && s.slotTime == "09:30");
            Assert.AreEqual(fillTargetPool, oldHolding, "Rejected change must preserve original holding; successful change replaces it.");
            Assert.AreEqual(cash, gs.cash, "Coordinator time changes are free.");
            Assert.IsFalse(mail.effectPending);
        }

        [Test]
        public void Stopover_RequiresBothDirectionsAtIntermediateStation()
        {
            var gs = Fixture(out var route);
            route.destination = "SZX"; route.stopover = "SHA"; route.distanceKm = 1900;
            var needs = RoutePermitLogic.ClockNeeds(route, "18:00");
            Assert.AreEqual(4, needs.Count, "Origin, destination and two separate intermediate calls are required.");
            Assert.AreEqual(2, needs.Count(x => x.airport == "SHA"));
            Assert.IsTrue(needs.Any(x => x.airport == "SHA" && x.inbound));
            Assert.IsTrue(needs.Any(x => x.airport == "SHA" && !x.inbound));
            for (int i = 0; i < needs.Count - 1; i++)
                Assert.IsTrue(StrategyManager.AcquireSlotsAt(gs, needs[i].airport, needs[i].time, 1, true, out _));
            Assert.IsFalse(RoutePermitLogic.CanScheduleClock(gs, route, "18:00", true));
            var last = needs[needs.Count - 1];
            Assert.IsTrue(StrategyManager.AcquireSlotsAt(gs, last.airport, last.time, 1, true, out _));
            Assert.IsTrue(RoutePermitLogic.CanScheduleClock(gs, route, "18:00", true));
        }

        [TestCase("飞行员")]
        [TestCase("乘务员")]
        public void EachCrewRoleIndependentlyConstrainsCapacity_AndRecoversAfterStaffing(string scarceRole)
        {
            var gs = Fixture(out var route);
            gs.staffPool = new List<PersonnelData>();
            foreach (var role in new[] { "飞行员", "乘务员" })
                for (int i = 0; i < (role == scarceRole ? 1 : 100); i++)
                    gs.staffPool.Add(new PersonnelData { id = role + i, jobTitle = role,
                        isOperational = true, department = "OPS", source = "market" });
            gs.InvalidateCrewCache();
            Assert.IsFalse(CrewCapacityLogic.CanCrew(gs, route, 3, out _));
            for (int i = 1; i < 100; i++) gs.staffPool.Add(new PersonnelData { id = scarceRole + i,
                jobTitle = scarceRole, isOperational = true, department = "OPS", source = "market" });
            gs.InvalidateCrewCache();
            Assert.IsTrue(CrewCapacityLogic.CanCrew(gs, route, 3, out string why), why);
            Assert.AreEqual(1, route.FlightCount, "Checking staffing cannot delete the existing service.");
        }
    }
}
