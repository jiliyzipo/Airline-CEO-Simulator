using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.PlayMode
{
    public class DevelopmentLongRunTests : IsolatedPlayModeFixture
    {
        [TestCase("scratch")]
        [TestCase(PresetAirlines.Startup)]
        [TestCase(PresetAirlines.Regional)]
        [TestCase(PresetAirlines.Legacy)]
        [TestCase(PresetAirlines.Distressed)]
        public void TwoFiscalYears_RemainFinite_AndClockMatchesQuarterBoundaries(string scenario)
        {
            if (Application.companyName != "AirlineCEORegression") Assert.Ignore("Isolated baseline only.");
            ExternalDataFetcher.Enabled = false;
            ExternalDataFetcher.QuoteEnabled = false;
            UnityEngine.Random.InitState(42);
            var gs = new GameObject("LongRunGS").AddComponent<GameState>();
            gs.year = 1; gs.quarter = 1;
            gs.SetSimUtc(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)); gs.simDay = 1;
            new GameObject("LongRunFleet").AddComponent<FleetManager>();
            new GameObject("LongRunLoans").AddComponent<LoanManager>();
            if (scenario == "scratch")
            {
                gs.cash = 10000;
                gs.fleet = new List<Aircraft> { new Aircraft { registration = "B-LONG", type = "A320neo",
                    isActive = true, status = AircraftStatuses.Airworthy, condition = 100 } };
                gs.activeRoutes.Clear();
            }
            else
            {
                var profile = TakeoverWorldBuilder.BuildPreview(scenario);
                Assert.IsNotNull(profile);
                TakeoverWorldBuilder.Apply(gs, profile);
            }
            var time = new GameObject("LongRunClock").AddComponent<TimeManager>();
            TimeManager.SimulationEnabled = false;
            int closed = 0;
            for (int quarter = 0; quarter < 8 && !gs.gameOver; quarter++)
            {
                DateTime before = gs.SimUtc;
                time.AdvanceGameMinutes(gs, GameState.DaysPerQuarter * 1440);
                Assert.IsFalse(float.IsNaN(gs.cash) || float.IsInfinity(gs.cash));
                Assert.IsFalse(float.IsNaN(gs.monthlyRevenue) || float.IsInfinity(gs.monthlyRevenue));
                Assert.GreaterOrEqual(gs.SimUtc, before);
                if (!gs.gameOver) Assert.AreEqual(before.AddDays(GameState.DaysPerQuarter), gs.SimUtc);
                closed++;
            }
            Debug.Log("LONGRUN scenario=" + scenario + " quarters=" + closed + " cash=" + gs.cash
                + " fleet=" + gs.fleet.Count + " routes=" + gs.activeRoutes.Count + " gameOver=" + gs.gameOver
                + " payrollMonthly=" + (gs.OperationalPayrollMonthlyWan + gs.LaborCostMonthlyWan)
                + " maintenanceMonthly=" + gs.FleetMonthlyMaintenance + " loans=" + gs.activeLoans.Count
                + " reason=" + gs.gameOverReason);
            Assert.Greater(closed, 0);
        }
    }
}
