using AirlineCEO.Data;
using AirlineCEO.Logic;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    public class OpsEconomicsTests
    {
        [Test]
        public void Schedule_HighUtilization_RaisesDelayProbability()
        {
            var calm = OpsEconomics.Evaluate(1080f, 7, "A320neo", 3, onTimePercent: 90f);
            var busy = OpsEconomics.Evaluate(1080f, 21, "A320neo", 1, onTimePercent: 70f);
            Assert.Greater(busy.delayProbability, calm.delayProbability);
            Assert.Less(busy.profitMultiplier, calm.profitMultiplier);
        }

        [Test]
        public void ApplyToRoute_ScalesDailyProfitByFrequency()
        {
            var route = new RouteData
            {
                routeId = "T1",
                origin = "PEK",
                destination = "SHA",
                distanceKm = 1080f,
                aircraftType = "A320neo",
                weeklyFlights = 14,
                groundStaffAssigned = 3
            };
            OpsEconomics.ApplyToRoute(route, singleFlightBaseline: 2f, onTimePercent: 88f);
            Assert.AreEqual(2f, route.baseDailyProfit, 0.01f);
            Assert.Greater(route.dailyProfit, 2f);
        }

        [Test]
        public void RouteFixedDailyWan_GrowsWithDistanceAndGauge()
        {
            float shortNarrow = OpsEconomics.RouteFixedDailyWan(200f, "A320neo");
            float longNarrow = OpsEconomics.RouteFixedDailyWan(6000f, "A320neo");
            float longWide = OpsEconomics.RouteFixedDailyWan(6000f, "B787-9");

            Assert.Greater(shortNarrow, 0f, "周期固定成本必须为正，否则加密无从摊薄");
            Assert.Greater(longNarrow, shortNarrow, "航程越长，外站与分销投入越高");
            Assert.Greater(longWide, longNarrow, "同航程下宽体场站/分销投入更高");
        }

        /// <summary>
        /// 核心新行为：周期固定成本与班次无关，故加密可摊薄它——
        /// 「1 班/日亏损、3 班/日盈利」必须成立（这正是亏损航线加密转盈的<strong>合法</strong>来源）。
        /// </summary>
        [Test]
        public void ApplyToRoute_FixedCostAmortizes_OneFlightLoses_ThreeFlightsProfits()
        {
            // 由固定成本反推一个「单班覆盖不住、三班覆盖得住」的边际毛贡献，
            // 这样调整 RouteFixedBaseDailyWan 时本测试无需跟着改常数。
            var m7 = OpsEconomics.Evaluate(1080f, 7, "A320neo", 3, onTimePercent: 88f);
            float fixedDaily = OpsEconomics.RouteFixedDailyWan(1080f, "A320neo");
            float baseline = 0.8f * fixedDaily / m7.profitMultiplier;

            var one = NewRoute(weeklyFlights: 7, baseline: baseline);
            OpsEconomics.ApplyToRoute(one, baseline, onTimePercent: 88f);

            var three = NewRoute(weeklyFlights: 21, baseline: baseline);
            OpsEconomics.ApplyToRoute(three, baseline, onTimePercent: 88f);

            Assert.Less(one.dailyProfit, 0f, "1 班/日覆盖不住固定成本 → 应亏损");
            Assert.Greater(three.dailyProfit, 0f, "3 班/日摊薄固定成本 → 应转盈");
        }

        /// <summary>
        /// 回归护栏：亏损基准在任何班次下都不得被改写为正、更不得把亏损洗成盈利。
        ///
        /// 历史缺陷：`CapacityDirectiveLogic` 等处把 `baseDailyProfit &lt;= 0.01f` 当成「未设置」，
        /// 补一句 `baseline = 1f`，于是下达运力调令即凭空获得 +1 万/班的盈利基准并写回存档。
        /// </summary>
        [Test]
        public void ApplyToRoute_LossBaseline_StaysNegativeAtEveryFrequency()
        {
            foreach (int weeklyFlights in new[] { 7, 14, 21 })
            {
                var route = NewRoute(weeklyFlights: weeklyFlights, baseline: -0.40f);

                float baseline = OpsEconomics.ResolvedSingleFlightBaseline(route);
                Assert.AreEqual(-0.40f, baseline, 0.001f,
                    $"周 {weeklyFlights} 班：负基准被当成「未设置」而改写");
                Assert.Less(baseline, 0f, "亏损基准必须原样透传");

                OpsEconomics.ApplyToRoute(route, baseline, onTimePercent: 88f);

                Assert.AreEqual(-0.40f, route.baseDailyProfit, 0.001f, "写回航线的基准不得被篡改");
                Assert.Less(route.dailyProfit, 0f,
                    $"周 {weeklyFlights} 班：单班毛贡献为负时，加密只能放大亏损，绝不可能转盈");
            }
        }

        [Test]
        public void BreakEvenDailyFlights_InfiniteWhenPerFlightContributionNonPositive()
        {
            var m = OpsEconomics.Evaluate(1080f, 7, "A320neo", 3, onTimePercent: 88f);
            float fixedDaily = OpsEconomics.RouteFixedDailyWan(1080f, "A320neo");

            Assert.IsTrue(float.IsPositiveInfinity(OpsEconomics.BreakEvenDailyFlights(-0.4f, m, fixedDaily)),
                "单班毛贡献非正 → 无论加密到多少班都救不回来");
            Assert.Greater(OpsEconomics.BreakEvenDailyFlights(0.10f, m, fixedDaily), 0f);
        }

        /// <summary>
        /// 不变量：各班 dailyProfit 存<strong>毛贡献</strong>（不含周期固定成本），固定成本只在航线层级扣一次。
        /// 若把固定成本摊进各班再在 RecalcDailyProfit 里相减，会重复计提。
        /// </summary>
        [Test]
        public void RecalcDailyProfit_SubtractsFixedCostExactlyOnce()
        {
            var route = NewRoute(weeklyFlights: 7, baseline: 2f);
            OpsEconomics.ApplyToRoute(route, 2f, onTimePercent: 88f);
            route.SyncFlightsFromPrimary();

            Assert.AreEqual(route.GrossDailyProfit, route.flights[0].dailyProfit, 0.01f,
                "单班字段应为毛贡献（未扣固定成本）");

            float afterApply = route.dailyProfit;
            route.RecalcDailyProfit();

            Assert.AreEqual(afterApply, route.dailyProfit, 0.01f,
                "RecalcDailyProfit 必须与 ApplyToRoute 口径一致且幂等（不得重复计提固定成本）");
        }

        static RouteData NewRoute(int weeklyFlights, float baseline) => new RouteData
        {
            routeId = "T-FIXED",
            origin = "PEK",
            destination = "SHA",
            distanceKm = 1080f,
            aircraftType = "A320neo",
            weeklyFlights = weeklyFlights,
            groundStaffAssigned = 3,
            baseDailyProfit = baseline,
            dailyProfit = baseline
        };

        /// <summary>
        /// 调参旋钮：覆盖 `RouteFixedBaseDailyWan` 必须<strong>线性</strong>缩放 F 与盈亏平衡班次，
        /// 且复位后逐位回到出厂默认（保证静态覆盖不外溢到其他用例）。
        /// </summary>
        [Test]
        public void RouteFixedBase_Tunable_ScalesFixedCostLinearly_AndResets()
        {
            var m = OpsEconomics.Evaluate(1080f, 14, "A320neo", 3, onTimePercent: 88f);
            try
            {
                Assert.AreEqual(EconomyBalanceParams.RouteFixedBaseDailyWanDefault,
                    EconomyBalanceParams.RouteFixedBaseDailyWan, 1e-6f, "测试前应处于出厂默认");
                Assert.IsFalse(EconomyBalanceParams.IsRouteFixedBaseOverridden);

                float fixedDefault = OpsEconomics.RouteFixedDailyWan(1080f, "A320neo");
                float breakEvenDefault = OpsEconomics.BreakEvenDailyFlights(0.10f, m, fixedDefault);

                Assert.IsTrue(EconomyBalanceParams.SetRouteFixedBaseDailyWan(
                    EconomyBalanceParams.RouteFixedBaseDailyWanDefault * 2f));
                Assert.IsTrue(EconomyBalanceParams.IsRouteFixedBaseOverridden);

                float fixedDoubled = OpsEconomics.RouteFixedDailyWan(1080f, "A320neo");
                float breakEvenDoubled = OpsEconomics.BreakEvenDailyFlights(0.10f, m, fixedDoubled);

                Assert.AreEqual(fixedDefault * 2f, fixedDoubled, 0.02f, "基准翻倍 → F 翻倍");
                Assert.AreEqual(breakEvenDefault * 2f, breakEvenDoubled, 0.05f,
                    "基准翻倍 → 盈亏平衡班次翻倍（旋钮必须单调线性，扫参才可解释）");
            }
            finally
            {
                EconomyBalanceParams.ResetRouteFixedBaseDailyWan();
            }

            Assert.AreEqual(EconomyBalanceParams.RouteFixedBaseDailyWanDefault,
                EconomyBalanceParams.RouteFixedBaseDailyWan, 1e-6f, "复位必须回到出厂默认");
            Assert.IsFalse(EconomyBalanceParams.IsRouteFixedBaseOverridden);
        }

        /// <summary>
        /// 非法取值必须<strong>被拒且不写入</strong>（不做静默夹取）——
        /// 否则扫参曲线会与实际请求的取值不符，得出错误结论。
        /// </summary>
        [Test]
        public void RouteFixedBase_RejectsIllegalValues_KeepsPrevious()
        {
            try
            {
                EconomyBalanceParams.ResetRouteFixedBaseDailyWan();
                float before = EconomyBalanceParams.RouteFixedBaseDailyWan;

                Assert.IsFalse(EconomyBalanceParams.SetRouteFixedBaseDailyWan(0f), "0 非法");
                Assert.IsFalse(EconomyBalanceParams.SetRouteFixedBaseDailyWan(-0.05f), "负值非法");
                Assert.IsFalse(EconomyBalanceParams.SetRouteFixedBaseDailyWan(float.NaN), "NaN 非法");
                Assert.IsFalse(EconomyBalanceParams.SetRouteFixedBaseDailyWan(float.PositiveInfinity), "Inf 非法");
                Assert.IsFalse(EconomyBalanceParams.SetRouteFixedBaseDailyWan(
                    EconomyBalanceParams.RouteFixedBaseDailyWanCeiling + 0.01f), "超上限非法");

                Assert.AreEqual(before, EconomyBalanceParams.RouteFixedBaseDailyWan, 1e-6f,
                    "非法输入不得改变当前取值");

                Assert.IsTrue(EconomyBalanceParams.SetRouteFixedBaseDailyWan(
                    EconomyBalanceParams.RouteFixedBaseDailyWanCeiling), "上限本身应被接受");
            }
            finally
            {
                EconomyBalanceParams.ResetRouteFixedBaseDailyWan();
            }
        }

        [Test]
        public void ValidateRing_NeedsTwoLegs()
        {
            var ring = OpsEconomics.ValidateRing("B-1001", null, null, 60);
            Assert.IsFalse(ring.valid);
        }

        [Test]
        public void ValidateRing_SameLegs_StableRingId()
        {
            var a = new RouteData { routeId = "PEK-SHA-demo", origin = "PEK", destination = "SHA", aircraftType = "A320neo", groundStaffAssigned = 3 };
            var b = new RouteData { routeId = "PEK-ICN-demo", origin = "PEK", destination = "ICN", aircraftType = "A320neo", groundStaffAssigned = 3 };
            var r1 = OpsEconomics.ValidateRing("B-1001", a, b, 70);
            var r2 = OpsEconomics.ValidateRing("B-1001", a, b, 70);
            Assert.AreEqual(r1.ringId, r2.ringId);
            Assert.IsTrue(r1.validationNote.Contains("勉强合格") || r1.validationNote.Contains("连班合格") || r1.validationNote.Contains("无法连班"));
            Assert.IsFalse(r1.validationNote.Contains("valid="));
        }

        [Test]
        public void ValidateRing_HubMatch_TrueWhenDestinationEqualsNextOrigin()
        {
            var a = new RouteData
            {
                routeId = "PEK-SHA",
                origin = "PEK",
                destination = "SHA",
                aircraftType = "A320neo",
                groundStaffAssigned = 3
            };
            var b = new RouteData
            {
                routeId = "SHA-PEK",
                origin = "SHA",
                destination = "PEK",
                aircraftType = "A320neo",
                groundStaffAssigned = 3
            };
            var ring = OpsEconomics.ValidateRing("B-1001", a, b, 90);
            Assert.IsTrue(ring.valid);
            Assert.IsTrue(ring.validationNote.Contains("连班合格"));
        }

        [Test]
        public void AirportCatalog_ResolvesIataAndChinese()
        {
            Assert.IsTrue(AirportCatalog.TryResolve("PEK", out var pek));
            Assert.AreEqual("PEK", pek.code);
            Assert.IsTrue(AirportCatalog.TryResolve("上海虹桥", out var sha));
            Assert.AreEqual("SHA", sha.code);
        }

        [Test]
        public void ProjectOnTimePercent_MovesTowardDelayImpliedTarget()
        {
            float projected = OpsEconomics.ProjectOnTimePercent(90f, avgDelayProb: 0.4f);
            Assert.Less(projected, 90f);
            Assert.Greater(projected, 55f);
        }

        [Test]
        public void Irrops_AogFerry_CostsTwenty()
        {
            var o = OpsIrropsLogic.Resolve(OpsIrropsLogic.IrropsKind.Aog, OpsIrropsLogic.Choice.A);
            Assert.AreEqual(-20f, o.cashDeltaWan, 0.01f);
            Assert.AreEqual(0f, o.onTimeDelta, 0.01f);
        }
    }
}
