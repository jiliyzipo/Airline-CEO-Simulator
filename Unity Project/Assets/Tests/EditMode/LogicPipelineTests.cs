using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Testing;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    public class LogicPipelineTests
    {
        [Test]
        public void RPM_PekLhr_Calibrated_Ballpark()
        {
            var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = 8150f,
                fuelPricePerTon = 850f,
                competitorSupply = 0.65f,
                demandMultiplier = 1.2f,
                brandEquity = 55f,
                seats = 290,
                loadFactor = 0.80f,
                aircraftType = "B787-9",
                passengerType = "business"
            });

            // Phase 1.3：长航单班日利润应在个位～十几万美元量级，不再是 60+
            Assert.Greater(rpm.dailyRevenue, 5f);
            Assert.Less(rpm.dailyRevenue, 25f);
            Assert.Greater(rpm.dailyCost, 3f);
            Assert.Less(rpm.dailyProfit, 15f);
            Assert.Greater(rpm.fuelCostWan, 2f);
            Assert.That(rpm.rating, Is.EqualTo("绿").Or.EqualTo("黄").Or.EqualTo("红"));
        }

        [Test]
        public void RPM_PekSha_Narrowbody_ThinMargin()
        {
            var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = 1080f,
                fuelPricePerTon = 850f,
                competitorSupply = 0.35f,
                demandMultiplier = 1.15f,
                brandEquity = 55f,
                seats = 180,
                loadFactor = 0.82f,
                aircraftType = "A320neo",
                passengerType = "leisure"
            });

            Assert.Greater(rpm.dailyRevenue, 0.8f);
            Assert.Less(rpm.dailyRevenue, 6f);
            Assert.Less(Math.Abs(rpm.dailyProfit), 4f);
        }

        [Test]
        public void CashRunway_300_over_80_IsGreen()
        {
            float months = CashRunnelCalculator.GetRunwayMonths(300f, 80f);
            Assert.AreEqual(3.75f, months, 0.001f);
            Assert.AreEqual("绿", CashRunnelCalculator.GetRunwayWarning(months));
        }

        [Test]
        public void TickPipeline_SettleMonth_IsDeterministic()
        {
            var a = MockGameState.CreateMinimalAirline();
            var b = MockGameState.CreateMinimalAirline();

            var ra = TickExecutionPipeline.Execute(new TickExecutionPipeline.TickInput
            {
                State = a,
                FuelShockMultiplier = 1f,
                SettleMonth = true
            });
            var rb = TickExecutionPipeline.Execute(new TickExecutionPipeline.TickInput
            {
                State = b,
                FuelShockMultiplier = 1f,
                SettleMonth = true
            });

            Assert.AreEqual(ra.CashAfter, rb.CashAfter, 0.001f);
            Assert.AreEqual(ra.NetDelta, rb.NetDelta, 0.001f);
            Assert.AreEqual(ra.RunwayWarning, rb.RunwayWarning);
        }

        [Test]
        public void SaveSnapshot_RoundTrip_PreservesCashAndRoutes()
        {
            var original = MockGameState.CreateMinimalAirline();
            original.cash = 777.5f;
            original.year = 2;
            original.quarter = 3;

            string json = SaveSnapshot.FromMock(original).ToJson();
            var restored = SaveSnapshot.FromJson(json).ToMock();

            Assert.AreEqual(777.5f, restored.cash, 0.001f);
            Assert.AreEqual(2, restored.year);
            Assert.AreEqual(3, restored.quarter);
            Assert.AreEqual(original.activeRoutes.Count, restored.activeRoutes.Count);
            Assert.AreEqual(original.fleet.Count, restored.fleet.Count);
            Assert.AreEqual(original.activeRoutes[0].routeId, restored.activeRoutes[0].routeId);
        }
    }

    /// <summary>
    /// 每周班次的「离港日 / 值勤日」双口径。
    /// 关键不变量：两条口径只是把**同一批班次**归到不同的星期（跨夜班按报到日归组），
    /// 故周合计必须恒等，差异只体现在星期分布上。
    /// 历史事故：接管局既有航线经 <see cref="RouteData.SyncFlightsFromPrimary"/> 压成单条目后，
    /// 值勤口径按「每天 +1」计，25 班/周 被报成 7，界面并写成「离港25/值勤7班/周」。
    /// </summary>
    public class RouteDutyAttributionTests
    {
        /// <summary>压扁成单条目的接管局航线（25 班/周 · 每日）两侧合计必须都是 25。</summary>
        [Test]
        public void CollapsedSingleEntry_DutyTotalEqualsDepartureTotal()
        {
            var route = new RouteData
            {
                routeId = "T-R15", origin = "DXB", destination = "DOH", distanceKm = 380f,
                aircraftType = "A320neo", openedDate = RouteData.OpenedBeforeTakeover,
                weeklyFlights = 25, daysOfWeek = "每日", depTime = "08:00", arrTime = "09:10"
            };
            route.SyncFlightsFromPrimary();

            Assert.AreEqual(1, route.flights.Count, "前置条件：单条目代表整周 25 班");
            Assert.AreEqual(25, route.WeeklyFlightsDeparture(), "离港日口径 = 每周班次");
            Assert.AreEqual(route.WeeklyFlightsDeparture(), route.WeeklyFlightsDuty(),
                "双口径周合计恒等（旧实现此处为 7：把「天数」当成了「班次」）");

            var dist = route.WeekdayOpsDeparture();
            int sum = 0;
            for (int i = 0; i < dist.Length; i++) sum += dist[i];
            Assert.AreEqual(25, sum, "7 日分布合计必须等于每周班次，不得少报");
            CollectionAssert.AreEqual(new[] { 4, 4, 4, 4, 3, 3, 3 }, dist,
                "25 班摊在 7 天：按 N/D 均摊、余数分给靠前的几天");
        }

        /// <summary>正常排班（波次列表保留）语义不得回归：各波班期与班次自洽时每天 1 班。</summary>
        [Test]
        public void MaterializedWaves_KeepOneFlightPerScheduledDay()
        {
            var route = new RouteData
            {
                routeId = "T-R16", origin = "DXB", destination = "DOH", distanceKm = 380f,
                aircraftType = "A320neo", weeklyFlights = 25, daysOfWeek = "每日", depTime = "08:00"
            };
            route.flights = new List<RouteFlight>
            {
                new RouteFlight { depTime = "08:00", daysOfWeek = "每日", weeklyFlights = 7 },
                new RouteFlight { depTime = "13:00", daysOfWeek = "每日", weeklyFlights = 7 },
                new RouteFlight { depTime = "18:00", daysOfWeek = "每日", weeklyFlights = 7 },
                new RouteFlight { depTime = "22:30", daysOfWeek = "周一/三/五/日", weeklyFlights = 4 }
            };

            Assert.AreEqual(25, route.WeeklyFlightsDeparture());
            Assert.AreEqual(25, route.WeeklyFlightsDuty(), "四波摊开后两侧仍恒等");
            CollectionAssert.AreEqual(new[] { 4, 3, 4, 3, 4, 3, 4 }, route.WeekdayOpsDeparture());
        }

        /// <summary>跨夜班：值勤归到报到那一日（周一/三/五 → 周日/二/四），但两侧周合计不变。</summary>
        [Test]
        public void CrossNightFlight_ShiftsDutyWeekday_WithoutChangingTotal()
        {
            var route = new RouteData
            {
                routeId = "T-R17", origin = "SZX", destination = "PEK", distanceKm = 1950f,
                aircraftType = "A320neo", weeklyFlights = 3, daysOfWeek = "一三五"
            };
            route.flights = new List<RouteFlight>
            {
                new RouteFlight
                {
                    depTime = "00:30", arrTime = "02:48",
                    daysOfWeek = "一三五", weeklyFlights = 3, dutyStart = 23 * 60 + 30
                }
            };

            Assert.AreEqual(3, route.WeeklyFlightsDeparture());
            Assert.AreEqual(3, route.WeeklyFlightsDuty());
            CollectionAssert.AreEqual(new[] { 1, 0, 1, 0, 1, 0, 0 }, route.WeekdayOpsDeparture(),
                "离港落在周一/三/五");
            CollectionAssert.AreEqual(new[] { 0, 1, 0, 1, 0, 0, 1 }, route.WeekdayOpsDuty(),
                "值勤落在周日/二/四：00:30 起飞的机组前一晚报到");
        }

        /// <summary>开航时间文案：接管局的哨兵值不得被当成日期直接塞进「开通：」。</summary>
        [Test]
        public void OpenedText_SeparatesTakeoverSentinelFromRealDate()
        {
            Assert.AreEqual("接管前已开航", RouteData.OpenedText(RouteData.OpenedBeforeTakeover));
            Assert.AreEqual("第 1 年 Q1 开航", RouteData.OpenedText("第 1 年 Q1"));
            Assert.AreEqual("开航时间未知", RouteData.OpenedText(null));
            Assert.IsFalse(RouteData.OpenedText(RouteData.OpenedBeforeTakeover).StartsWith("开通："),
                "不得沿用把哨兵值塞进「开通：」日期位的旧写法");
        }

        /// <summary>卡片头行：两侧恒等时只报一个数字，且不出现「离港N/值勤M班/周」并写。</summary>
        [Test]
        public void CardHeadline_ReportsSingleNumberWhenBothCountersAgree()
        {
            var route = new RouteData
            {
                openedDate = RouteData.OpenedBeforeTakeover,
                weeklyFlights = 25, daysOfWeek = "每日", depTime = "08:00"
            };
            route.SyncFlightsFromPrimary();

            Assert.AreEqual("接管前已开航 · 25 班/周", route.CardHeadline());
        }
    }
}
