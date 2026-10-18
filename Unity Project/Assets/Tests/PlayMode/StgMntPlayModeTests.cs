using System.Collections;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using AirlineCEO.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// STG 战略 + MNT 机务 PlayMode（Phase 2 Week 9）。
    /// 覆盖：导航工厂、战略/维修数值、Manager 回写 GameState。
    /// </summary>
    public class StgMntPlayModeTests : IsolatedPlayModeFixture
    {
        // —— 导航：运行时工厂可打开 STG + MNT 全部子页 ——

        [UnityTest]
        public IEnumerator RuntimeFactories_BuildAllSubpages()
        {
            var host = new GameObject("StgMntRuntimeHost", typeof(RectTransform));

            string[] stgPages = { "n76_机队与运力规划", "n77_航权与时刻槽", "n78_联盟与战略合作", "n79_战略投资与并购", "n80_战略研究与预案", "n35_新航线_RPM_评估台", "n102_谈判弹窗", "n108_临空商业" };
            foreach (var page in stgPages)
            {
                var go = StrategyRuntimeFactory.TryCreate(page, host.transform);
                Assert.IsNotNull(go, "应能构建 " + page);
                Assert.IsNotNull(go.GetComponent<StrategySubpagePanel>(), page + " 应挂 StrategySubpagePanel");
            }

            string[] mntPages = { "n81_航线与定检维修", "n82_工程技术与计划", "n83_航材与供应链", "n84_安全质量与适航", "n85_维修培训与资质", "n105_订单确认" };
            foreach (var page in mntPages)
            {
                var go = MaintenanceRuntimeFactory.TryCreate(page, host.transform);
                Assert.IsNotNull(go, "应能构建 " + page);
                Assert.IsNotNull(go.GetComponent<MaintenanceSubpagePanel>(), page + " 应挂 MaintenanceSubpagePanel");
            }

            Object.Destroy(host);
            yield return null;
        }

        // —— 数值：航权竞标 / 时刻槽价值 / 投资 IRR / 定检成本 / 库存健康 ——

        [UnityTest]
        public IEnumerator StrategyMaintenanceEconomics_Scoring()
        {
            // 航权竞标：经济性/投资承诺/政府关系/战略契合更高 → 分数更高
            float strongBid = StrategyEconomics.RouteRightBidScore(90f, 85f, 80f, 90f);
            float weakBid = StrategyEconomics.RouteRightBidScore(30f, 20f, 20f, 30f);
            Assert.Greater(strongBid, weakBid);

            // 时刻槽价值：枢纽 > 干线 > 支线；早班 > 夜班
            Assert.Greater(StrategyEconomics.SlotValueWan(0, 0), StrategyEconomics.SlotValueWan(2, 3));

            // 联盟收益随等级/航线增加
            Assert.Greater(StrategyEconomics.AllianceRevenueLiftWan(3, 10, 5),
                StrategyEconomics.AllianceRevenueLiftWan(1, 2, 0));

            // 投资 IRR 为正（NPV>0）
            Assert.Greater(StrategyEconomics.InvestmentIrr(1200f, 2000f, 5), 0f);

            // 地缘风险加成
            Assert.AreEqual(0.20f, StrategyEconomics.GeoRiskSurcharge(80), 0.001f);
            Assert.AreEqual(0f, StrategyEconomics.GeoRiskSurcharge(20), 0.001f);

            // 定检成本：D > C > B > A
            Assert.Greater(MaintenanceEconomics.CheckCostWan("D", 10), MaintenanceEconomics.CheckCostWan("C", 10));
            Assert.Greater(MaintenanceEconomics.CheckCostWan("C", 10), MaintenanceEconomics.CheckCostWan("B", 10));

            // 维修策略可用率：全面预防 > 智能预测 > 最低合规
            Assert.Greater(MaintenanceEconomics.AvailabilityByStrategy(0), MaintenanceEconomics.AvailabilityByStrategy(2));
            Assert.Greater(MaintenanceEconomics.AvailabilityByStrategy(2), MaintenanceEconomics.AvailabilityByStrategy(1));

            // 库存健康度：缺料越多越低
            Assert.Greater(MaintenanceEconomics.StockHealth(120, 90, 0),
                MaintenanceEconomics.StockHealth(120, 90, 3));

            yield return null;
        }

        // —— Manager 回写：航权/联盟/投资/预案 + 维修/航材/定检 ——

        [UnityTest]
        public IEnumerator StrategyManager_WritesState()
        {
            var gsGo = new GameObject("StgGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 10000f;
            var mgrGo = new GameObject("StgManager");
            var mgr = mgrGo.AddComponent<StrategyManager>();

            // 时刻槽购买回写
            Assert.IsTrue(mgr.AcquireSlot("PEK", 1, out _));
            Assert.AreEqual(1, gs.stg.slots.Count);

            // 航权申请回写（消耗政府关系）
            float govBefore = gs.stg.governmentRelation;
            Assert.IsTrue(mgr.ApplyRouteRight("PEK", "SIN", 5, out _));
            Assert.Less(gs.stg.governmentRelation, govBefore);
            Assert.AreEqual(1, gs.stg.rights.Count);

            // 联盟升级回写（需机队 ≥20，先注入机队）
            for (int i = 0; i < 20; i++)
                gs.fleet.Add(new Aircraft { registration = "B-" + (1001 + i) });
            Assert.IsTrue(mgr.UpgradeAlliance(out _));
            Assert.Greater(gs.stg.alliance.tier, 0);

            // 投资立项：只入待批，不立刻扣款；执行后才扣资本
            Assert.IsTrue(mgr.ProposeInvestment(StrategyManager.CatalogHub(), out _));
            Assert.AreEqual(1, gs.stg.investments.Count);
            Assert.AreEqual("待批", gs.stg.investments[0].stage);
            Assert.IsFalse(gs.stg.investments[0].approved);
            float cashBefore = gs.cash;
            Assert.IsTrue(mgr.ExecuteInvestment(gs.stg.investments[0].id, out _));
            Assert.AreEqual("执行", gs.stg.investments[0].stage);
            Assert.IsTrue(gs.stg.investments[0].approved);
            Assert.Less(gs.cash, cashBefore);

            // 预案回写
            Assert.IsTrue(mgr.SaveContingency(new ContingencyPlan { trigger = "油价暴涨", autoMeasure = "锁定套保", lossReduction = 0.35f }, out _));
            Assert.AreEqual(1, gs.stg.contingencyPlans.Count);

            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AirportCommerce_InvestAndRefresh()
        {
            var gsGo = new GameObject("CommerceGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 100000f;

            // EnsureState 初始化四类设施占位
            var state = AirportCommerceManager.EnsureState(gs);
            Assert.AreEqual(4, state.facilities.Count, "应初始化四类商业设施");
            Assert.AreEqual(0, AirportCommerceManager.GetFacility(state, AirportCommerceEconomics.DutyFree).level);

            // 注入高吞吐（模拟已解锁高端档），绕开 Refresh 重算
            state.unlockedTier = 3;
            state.hubAnnualThroughputWan = 3500f;

            // 投资免税店 L1（300 万）成功回写
            float cashBefore = gs.cash;
            Assert.IsTrue(AirportCommerceManager.InvestFacility(gs, AirportCommerceEconomics.DutyFree, 1, out _));
            Assert.AreEqual(1, AirportCommerceManager.GetFacility(state, AirportCommerceEconomics.DutyFree).level);
            Assert.Less(gs.cash, cashBefore, "投资应扣减现金");

            // 降档后无法继续升级（解锁限制）
            state.unlockedTier = 0;
            Assert.IsFalse(AirportCommerceManager.InvestFacility(gs, AirportCommerceEconomics.DutyFree, 2, out _));
            Assert.AreEqual(1, AirportCommerceManager.GetFacility(state, AirportCommerceEconomics.DutyFree).level, "未解锁时不应升级");

            // 吞吐估算公式：1 条 PEK 航线 周7班 × 180座 × 55% 客座
            gs.hubIata = "PEK";
            gs.fleet = new System.Collections.Generic.List<Aircraft>
            {
                new Aircraft { registration = "B-1001", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy }
            };
            gs.activeRoutes = new System.Collections.Generic.List<RouteData>
            {
                new RouteData { origin = "PEK", destination = "PVG", weeklyFlights = 7 }
            };
            gs.loadFactorPercent = 55f;
            AirportCommerceManager.Refresh(gs);
            float expected = AirportCommerceEconomics.HubAnnualThroughputWan(7, 180, 0.55f);
            Assert.AreEqual(expected, state.hubAnnualThroughputWan, 0.01f, "吞吐应与公式一致");

            Object.Destroy(gsGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MaintenanceManager_WritesState()
        {
            var gsGo = new GameObject("MntGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 10000f;
            var mgrGo = new GameObject("MntManager");
            var mgr = mgrGo.AddComponent<MaintenanceManager>();

            // 维修策略回写
            Assert.IsTrue(mgr.SetStrategy(0, out _));
            Assert.AreEqual(0, gs.mnt.strategy);
            Assert.AreEqual(MaintenanceEconomics.AvailabilityByStrategy(0), gs.mnt.fleetAvailability, 0.01f);

            // 采购订单创建 + 确认（入库）
            Assert.IsTrue(mgr.CreateOrder("PN-ENG-001", "OEM 原厂", 5, 120000f, false, out _));
            Assert.AreEqual(1, gs.mnt.orders.Count);
            Assert.IsTrue(mgr.ConfirmOrder(gs.mnt.orders[0].orderNo, out _));
            Assert.AreEqual("已下单", gs.mnt.orders[0].status);
            Assert.AreEqual(1, gs.mnt.parts.Count);
            Assert.AreEqual(5, gs.mnt.parts[0].quantity);

            Assert.IsTrue(mgr.CancelOrder("PO-MISSING", out _) == false);
            Assert.IsTrue(mgr.CreateOrder("PN-HYD-002", "OEM 原厂", 2, 80000f, true, out _));
            string cancelNo = gs.mnt.orders[gs.mnt.orders.Count - 1].orderNo;
            Assert.IsTrue(mgr.CancelOrder(cancelNo, out _));
            Assert.AreEqual("已取消", gs.mnt.orders.Find(o => o.orderNo == cancelNo).status);

            // OEM 关系升级回写
            int starsBefore = gs.mnt.oems[0].stars;
            Assert.IsTrue(mgr.UpgradeOem("波音", out _));
            Assert.Greater(gs.mnt.oems[0].stars, starsBefore);

            // 定检排程回写
            Assert.IsTrue(mgr.ScheduleCheck("B-1001", "C", 3, out _));
            Assert.AreEqual(1, gs.mnt.checks.Count);

            Object.Destroy(gsGo);
            Object.Destroy(mgrGo);
            yield return null;
        }
    }
}
