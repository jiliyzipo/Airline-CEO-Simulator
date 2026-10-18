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
    /// FLEET/NET 机队+航线网络 PlayMode（Phase 2 Week 6）：
    /// 覆盖导航工厂、数值公式、载客配置/大修/购机回写。
    /// </summary>
    public class FleetNetPlayModeTests : IsolatedPlayModeFixture
    {
        // —— 导航：运行时工厂可打开 FLEET/NET 全部子页 ——

        [UnityTest]
        public IEnumerator FleetNetRuntimeFactory_BuildsAllSubpages()
        {
            var host = new GameObject("FleetNetRuntimeHost", typeof(RectTransform));

            string[] pages =
            {
                "n10_飞机详情_履历", "n11_座位配置", "n107_预算确认",
                "n97_购机决策会议", "n41_单航线"
            };
            foreach (var page in pages)
            {
                var go = FleetNetRuntimeFactory.TryCreate(page, host.transform);
                Assert.IsNotNull(go, "应能构建 " + page);
                Assert.IsTrue(
                    go.GetComponent<FleetSubpagePanel>() != null ||
                    go.GetComponent<PurchaseDecisionPanel>() != null ||
                    go.GetComponent<RouteDetailPanel>() != null,
                    page + " 应挂 FLEET/NET 面板组件");
            }

            Object.Destroy(host);
            yield return null;
        }

        // —— 数值：残值 / 维护 / 大修 / 舱位乘数 / 日利 ——

        [UnityTest]
        public IEnumerator FleetEconomics_Formulas()
        {
            // 残值：直线折旧，机龄 3%/年，下限 10%
            Assert.AreEqual(11000f, FleetEconomics.ResidualValue(11000f, 0), 0.01f);
            Assert.AreEqual(11000f * 0.91f, FleetEconomics.ResidualValue(11000f, 3), 0.01f);
            Assert.AreEqual(11000f * 0.10f, FleetEconomics.ResidualValue(11000f, 40), 0.01f);

            // 月维护：基础 5 万 × (1 + 机龄 × 6%)；>20 年翻倍
            Assert.AreEqual(5f, FleetEconomics.MonthlyMaintenance(0), 0.001f);
            Assert.AreEqual(5f * 1.60f, FleetEconomics.MonthlyMaintenance(10), 0.001f);
            Assert.Greater(FleetEconomics.MonthlyMaintenance(21), 5f * 1.20f * 2f - 0.01f);

            // 大修成本：D 检 > C 检 > B 检 > A 检；宽体 ×1.6
            Assert.Greater(FleetEconomics.OverhaulCost(FleetEconomics.OverhaulD, false),
                FleetEconomics.OverhaulCost(FleetEconomics.OverhaulC, false));
            Assert.AreEqual(
                FleetEconomics.OverhaulCost(FleetEconomics.OverhaulC, false) * 1.6f,
                FleetEconomics.OverhaulCost(FleetEconomics.OverhaulC, true), 0.01f);
            Assert.Greater(FleetEconomics.OverhaulDays(FleetEconomics.OverhaulD),
                FleetEconomics.OverhaulDays(FleetEconomics.OverhaulA));

            // 舱位收益乘数：全经济（高端占比 0）低于默认，全商务高于默认
            float allEco = FleetEconomics.SeatMixRevenueMultiplier(0, 0, 180);
            float allBiz = FleetEconomics.SeatMixRevenueMultiplier(0, 180, 0);
            Assert.Less(allEco, 1f);
            Assert.Greater(allBiz, 1f);
            Assert.Greater(allBiz, allEco);

            // 日利折算：周 14 班 = 单班基准 × 2
            Assert.AreEqual(200f, FleetEconomics.DailyProfitFromWeekly(100f, 14), 0.01f);

            // 回收期：价格 / 月贡献
            Assert.AreEqual(24f, FleetEconomics.PaybackMonths(1200f, 50f), 0.01f);
            Assert.AreEqual(999f, FleetEconomics.PaybackMonths(1200f, 0f), 0.01f);

            yield return null;
        }

        // —— 主操作回写：载客配置 + 大修 ——

        [UnityTest]
        public IEnumerator FleetManager_SeatConfigAndOverhaul_Writeback()
        {
            var gsGo = new GameObject("FleetGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 500f;

            var fmGo = new GameObject("FleetManager");
            var fm = fmGo.AddComponent<FleetManager>();
            yield return null; // 等待 Start() → EnsureStarterFleet()

            Assert.AreEqual(3, gs.fleet.Count, "初始机队应 3 架");
            string reg = gs.fleet[0].registration;

            // 载客配置回写
            bool okSeat = fm.ApplySeatConfig(reg, 8, 24, 150, out string seatMsg);
            Assert.IsTrue(okSeat, seatMsg);
            var ac = fm.GetAircraftByRegistration(reg);
            Assert.AreEqual(8, ac.firstClassSeats);
            Assert.AreEqual(24, ac.businessSeats);
            Assert.AreEqual(150, ac.economySeats);
            Assert.AreEqual(182, ac.TotalSeats);

            // 大修回写（C 检窄体 300 万）
            float cashBefore = gs.cash;
            bool okOverhaul = fm.ScheduleOverhaul(reg, FleetEconomics.OverhaulC, out string ovhMsg);
            Assert.IsTrue(okOverhaul, ovhMsg);
            Assert.AreEqual(cashBefore - 300f, gs.cash, 0.01f);
            Assert.AreEqual(AircraftStatuses.Maintenance, ac.status);
            Assert.AreEqual(FleetEconomics.OverhaulC, ac.lastOverhaulType);

            Object.Destroy(fmGo);
            Object.Destroy(gsGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FleetManager_AcquireFleet_AddsAircraft()
        {
            var gsGo = new GameObject("FleetGS2");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 100000f;

            var dbGo = new GameObject("DataBase");
            dbGo.AddComponent<DataBase>();

            var fmGo = new GameObject("FleetManager2");
            var fm = fmGo.AddComponent<FleetManager>();
            yield return null;

            int before = gs.fleet.Count;
            float cashBefore = gs.cash;

            // 买断 2 架 A320neo：11000 × 2
            bool ok = fm.TryAcquireFleet("A320neo", 2, "自有资金买断", out string msg);
            Assert.IsTrue(ok, msg);
            Assert.AreEqual(before + 2, gs.fleet.Count);
            Assert.AreEqual(cashBefore - 22000f, gs.cash, 0.01f);

            // 租赁：10% 首付
            bool okLease = fm.TryAcquireFleet("B787-9", 1, "融资租赁", out string leaseMsg);
            Assert.IsTrue(okLease, leaseMsg);
            Assert.AreEqual("融资租赁", gs.fleet[gs.fleet.Count - 1].source);

            Object.Destroy(fmGo);
            Object.Destroy(dbGo);
            Object.Destroy(gsGo);
            yield return null;
        }

        // —— 开通航线表单：默认基地 = 玩家选定的总部基地（2026-10-09）——

        /// <summary>
        /// 默认端点取自总部基地，而不是写死的 PEK。
        ///
        /// 旧行为：玩家把总部设在广州（CAN）后打开「开通新航线」，基地仍预填 PEK。
        /// 只是「麻烦」的话尚可接受，真正的问题是始发地落在非枢纽 →
        /// <c>RouteRightManager.IsFromHomeBase</c> 返回 false →
        /// 基地始发干线**本应占用的时刻槽被静默跳过**，玩家在不知情下绕过了枢纽的时刻成本。
        /// </summary>
        [UnityTest]
        public IEnumerator OpenRoute_BaseDefaultsToPlayerHub()
        {
            var gsGo = new GameObject("OpenRouteGS");
            var gs = gsGo.AddComponent<GameState>();
            yield return null;   // 等 Awake 落 Instance

            gs.hubIata = "CAN";
            OpenRouteModalUI.DefaultEndpoints(gs.hubIata, out string baseCode, out string destCode);

            Assert.AreEqual("CAN", baseCode, "基地应预填玩家总部基地，而非写死的 PEK");
            Assert.AreNotEqual(baseCode, destCode,
                "默认目的地不得与基地相同，否则默认值本身会落在「基地与目的地不能相同」拒绝分支");
            Assert.IsTrue(RouteRightManager.IsFromHomeBase(baseCode, destCode),
                "预填的基地必须被认定为枢纽始发，否则枢纽时刻成本被默认值绕过");

            // 旧默认值（PEK）在总部为 CAN 时不是枢纽始发 —— 这正是被修掉的偏差
            Assert.IsFalse(RouteRightManager.IsFromHomeBase("PEK", destCode),
                "对照：总部 CAN 时以 PEK 为始发地不是枢纽始发（旧硬编码默认值的后果）");

            // 规范化：空白 / 小写 / 缺失都要收敛到可解析的代码
            OpenRouteModalUI.DefaultEndpoints(" pek ", out string trimBase, out _);
            OpenRouteModalUI.DefaultEndpoints("sin", out string lowerBase, out _);
            OpenRouteModalUI.DefaultEndpoints(null, out string nullBase, out _);
            OpenRouteModalUI.DefaultEndpoints("   ", out string blankBase, out _);
            Assert.AreEqual("PEK", trimBase);
            Assert.AreEqual("SIN", lowerBase);
            Assert.AreEqual(OpenRouteModalUI.FallbackBase, nullBase, "无总部信息时回落旧默认 PEK");
            Assert.AreEqual(OpenRouteModalUI.FallbackBase, blankBase);

            // 基地本身就是虹桥时，目的地必须让开
            OpenRouteModalUI.DefaultEndpoints("SHA", out string shaBase, out string shaDest);
            Assert.AreEqual("SHA", shaBase);
            Assert.AreNotEqual("SHA", shaDest, "基地已是虹桥时，目的地不得也是虹桥");

            Object.Destroy(gsGo);
            yield return null;
        }
    }
}
