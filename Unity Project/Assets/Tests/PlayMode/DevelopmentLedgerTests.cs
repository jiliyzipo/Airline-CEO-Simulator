using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.PlayMode
{
    public class DevelopmentLedgerTests : IsolatedPlayModeFixture
    {
        GameState State()
        {
            if (Application.companyName != "AirlineCEORegression") Assert.Ignore("Isolated baseline only.");
            ExternalDataFetcher.Enabled = false; ExternalDataFetcher.QuoteEnabled = false;
            UnityEngine.Random.InitState(42);
            var gs = new GameObject("LedgerGS").AddComponent<GameState>();
            gs.cash = 10000; gs.yearNetProfit = 0; gs.year = 1; gs.quarter = 1; gs.simDay = 1;
            gs.SetSimUtc(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            gs.activeRoutes.Clear(); gs.fleet.Clear(); gs.inbox.Clear(); gs.pendingInbox.Clear();
            return gs;
        }

        [Test]
        public void DailyLedger_CoversPayrollMaintenanceLeaseCrisisAndContract_ThenSurvivesSaveLoad()
        {
            var gs = State();
            gs.monthlyBurnRate = 30; gs.financeCrisisBurnBonus = 9;
            gs.fleet.Add(new Aircraft { registration = "B-COST", type = "A320neo", isActive = true,
                status = AircraftStatuses.Airworthy, condition = 100, monthlyLeaseWan = 6 });
            new GameObject("LedgerFleet").AddComponent<FleetManager>();
            gs.staffPool = new List<PersonnelData> { new PersonnelData { id = "payroll", baseSalary = 120000,
                jobTitle = "飞行员", department = "OPS", isOperational = true, source = "market" } };
            gs.InvalidateCrewCache();
            gs.transitionContracts = new List<TransitionContract> { new TransitionContract { id = "contract",
                aircraftReg = "B-COST", kind = TransitionKinds.Dry, totalDays = 2, rateWanPerDay = 2,
                dailyCashWan = 2, balanceWan = 3, startUtcTicks = gs.SimUtc.Ticks } };
            float daily = LiveClockLogic.DailyFixedCostWan(gs.monthlyBurnRate, gs.FleetMonthlyMaintenance,
                gs.FleetMonthlyLease, gs.CeoMonthlyPayWan, gs.financeCrisisBurnBonus,
                gs.OperationalPayrollMonthlyWan, gs.LaborCostMonthlyWan);
            Assert.Greater(gs.OperationalPayrollMonthlyWan, 0); Assert.Greater(gs.FleetMonthlyMaintenance, 0);
            var clock = new GameObject("LedgerClock").AddComponent<TimeManager>();
            clock.AdvanceGameMinutes(gs, 1440);
            Assert.AreEqual(10000 - daily + 2, gs.cash, 0.02);
            var save = new GameObject("LedgerSave").AddComponent<SaveManager>(); save.Save();
            gs.cash = -1; gs.liveQuarterExpense = -1; gs.liveQuarterTransitionIncome = -1;
            save.Load();
            Assert.AreEqual(daily, gs.liveQuarterExpense, 0.001);
            Assert.AreEqual(2, gs.liveQuarterTransitionIncome, 0.001);
            clock.AdvanceGameMinutes(gs, 1440);
            Assert.AreEqual(10000 - 2 * daily + 7, gs.cash, 0.04);
            Assert.AreEqual(0, gs.transitionContracts.Count, "Completed contract and final payment cannot replay.");
            Assert.AreEqual(7, gs.liveQuarterTransitionIncome, 0.001);
        }

        [Test]
        public void DeveloperQuarter_UsesActualElapsedTime_AndDoesNotRebookDailyCash()
        {
            var gs = State(); gs.monthlyBurnRate = 30;
            var clock = new GameObject("DeveloperQuarterClock").AddComponent<TimeManager>();
            clock.oilEventChance = 0;
            float daily = LiveClockLogic.DailyFixedCostWan(gs.monthlyBurnRate, 0, 0, gs.CeoMonthlyPayWan, 0,
                gs.OperationalPayrollMonthlyWan, gs.LaborCostMonthlyWan);
            var before = gs.SimUtc;
            var result = clock.AdvanceQuarter();
            Assert.IsTrue(result.success);
            Assert.AreEqual(before.AddDays(90), gs.SimUtc);
            Assert.AreEqual(90 * daily, result.quarterExpense, 0.02);
            Assert.AreEqual(0, result.quarterIncome, 0.001);
            Assert.AreEqual(0, result.netChange, 0.001);
        }

        [Test]
        public void LoanRepayment_DeductsPrincipalFromCash_ButOnlyInterestFromProfit()
        {
            var gs = State();
            gs.activeLoans = new List<Loan> { new Loan { id = "ledger-loan", type = "bank", principal = 120,
                remainingPrincipal = 120, interestRate = 0.12f, monthlyPayment = 11.2f,
                termMonths = 12, monthsRemaining = 12 } };
            var loans = new GameObject("LedgerLoans").AddComponent<LoanManager>();
            loans.ProcessQuarterRepayments();
            Assert.AreEqual(10000 - 33.6f, gs.cash, 0.002);
            Assert.AreEqual(90, gs.activeLoans[0].remainingPrincipal, 0.001);
            Assert.AreEqual(9, gs.activeLoans[0].monthsRemaining);
            Assert.AreEqual(-3.6f, gs.yearNetProfit, 0.001,
                "Loan principal is financing cash, not an operating expense or a loss.");
        }

        [Test]
        public void TakeoverLoanRates_AreFractional_AndLegacySaveNormalizesExactlyOnce()
        {
            var gs = State();
            foreach (var id in new[] { PresetAirlines.Startup, PresetAirlines.Regional, PresetAirlines.Legacy, PresetAirlines.Distressed })
                foreach (var loan in TakeoverWorldBuilder.BuildPreview(id).openingLoans)
                    Assert.That(loan.interestRate, Is.InRange(0f, 0.2f), id);
            var loans = new GameObject("LegacyTakeoverLoans").AddComponent<LoanManager>();
            loans.LoansFromJson(JsonUtility.ToJson(new LoanListSave { items = new[] {
                new Loan { id = "L-TAKEOVER", createdAt = "接管前", interestRate = 9.5f } } }));
            Assert.AreEqual(0.095f, gs.activeLoans[0].interestRate, 0.00001);
            loans.LoansFromJson(loans.LoansToJson());
            Assert.AreEqual(0.095f, gs.activeLoans[0].interestRate, 0.00001);
        }
    }
}
