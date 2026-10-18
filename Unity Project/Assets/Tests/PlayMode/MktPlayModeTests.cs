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
    /// MKT 市场销售部 PlayMode（Phase 2 Week 7 · 批次 A）：导航工厂、需求弹性/季节/旺季 ROI 数值、票价与旺季策略回写。
    /// </summary>
    public class MktPlayModeTests : IsolatedPlayModeFixture
    {
        // —— 导航：运行时工厂可打开 MKT 全部子页 ——

        [UnityTest]
        public IEnumerator MktRuntimeFactory_BuildsAllMktSubpages()
        {
            var host = new GameObject("MktRuntimeHost", typeof(RectTransform));

            string[] pages =
            {
                "n44_航线收益分析", "n49_航班定价", "n45_单航线定价详情", "n46_动态定价算法",
                "n47_收益管理仪表盘", "n48_辅助收入管理", "n50_外部环境分析", "n55_季节性日历",
                "n56_航季对比详情", "n57_旺季策略配置器", "n58_历史趋势分析", "n59_旺季影响预测弹窗",
                "n33_FFP_常旅客管理", "n34_产品工坊"
            };
            foreach (var page in pages)
            {
                var go = MktRuntimeFactory.TryCreate(page, host.transform);
                Assert.IsNotNull(go, "应能构建 " + page);
                Assert.IsNotNull(go.GetComponent<MarketSubpagePanel>(), page + " 应挂 MarketSubpagePanel");
            }

            Object.Destroy(host);
            yield return null;
        }

        // —— 数值：需求价格弹性 / 季节系数 / 旺季 ROI / 辅助收入 ——

        [UnityTest]
        public IEnumerator MktEconomics_PricingElasticityAndSeasonal()
        {
            // 价格弹性：休闲 > 商务（|ε| 更大）
            Assert.AreEqual(-1.5f, MktEconomics.ElasticityFor("leisure"), 0.001f);
            Assert.AreEqual(-0.8f, MktEconomics.ElasticityFor("business"), 0.001f);

            // 涨价（×1.2）→ 客座率下降；休闲下降比商务快
            float leisureLoad = MktEconomics.LoadFactorAfterPrice(0.8f, 1.2f, "leisure");
            float businessLoad = MktEconomics.LoadFactorAfterPrice(0.8f, 1.2f, "business");
            Assert.Less(leisureLoad, 0.8f);
            Assert.Greater(businessLoad, leisureLoad);

            // 最优乘数在 [0.7, 1.5] 内
            float opt = MktEconomics.OptimalPriceMult(180, 0.8f, 1000f, 6f, "leisure");
            Assert.GreaterOrEqual(opt, 0.7f);
            Assert.LessOrEqual(opt, 1.5f);

            // 季节系数：春节/暑运/国庆为峰，11 月为谷
            Assert.Greater(MktEconomics.MonthSeasonalCoefficient(2), 1.0f);
            Assert.Greater(MktEconomics.MonthSeasonalCoefficient(8), 1.0f);
            Assert.Less(MktEconomics.MonthSeasonalCoefficient(11), 1.0f);

            // 节日曲线形态
            var spring = MktEconomics.HolidayInfo("春节");
            Assert.AreEqual("V型", spring.shape);
            Assert.AreEqual(3.0f, spring.peak, 0.001f);

            // 旺季策略 ROI > 0（增量收入 > 增量成本）
            var pkg = new PeakSeasonPackage
            {
                festival = "暑运",
                capacityChoice = 1,
                pricingChoice = 2,
                serviceChoice = 1,
                extendShoulder = true
            };
            float roi = MktEconomics.PeakSeasonRoi(1000f, 700f, pkg);
            Assert.Greater(roi, 0f);

            // 辅助收入：人均 = 行李 + 选座 + 机上销售 + 退改×0.2
            var aux = new AncillarySettings
            {
                baggageFeeUsd = 15f,
                seatFeeUsd = 8f,
                changeFeeUsd = 30f,
                onboardSalesUsd = 6f
            };
            Assert.AreEqual(35f, MktEconomics.AuxRevenuePerPaxUsd(aux), 0.001f);

            // 高铁替代威胁
            Assert.AreEqual("高", MktEconomics.HighSpeedRailThreat(400f));
            Assert.AreEqual("低", MktEconomics.HighSpeedRailThreat(1500f));
            yield return null;
        }

        // —— 主操作回写：票价 / 旺季策略 ——

        [UnityTest]
        public IEnumerator MktManager_WriteFareAndSeasonToGameState()
        {
            var gsGo = new GameObject("MktGS");
            var gs = gsGo.AddComponent<GameState>();
            var route = new RouteData
            {
                routeId = "r_test",
                origin = "深圳",
                destination = "北京",
                distanceKm = 1960f,
                aircraftType = "A320neo"
            };
            gs.activeRoutes.Add(route);

            var mktGo = new GameObject("MktManager");
            var mkt = mktGo.AddComponent<MktManager>();

            // 设置经济舱票价 ×1.3
            Assert.IsTrue(mkt.SetRouteFare("r_test", CabinClasses.Economy, 1.3f, out _));
            Assert.AreEqual(1.3f, MktManager.FareMultFor(gs.mkt, "r_test", CabinClasses.Economy), 0.001f);

            // 保存旺季策略包
            var pkg = new PeakSeasonPackage { festival = "春节", pricingChoice = 1 };
            Assert.IsTrue(mkt.SaveSeasonPackage(pkg, out _));
            var loaded = MktManager.GetSeasonPackage(gs.mkt, "春节");
            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.pricingChoice);
            Assert.IsTrue(loaded.applied);

            // 提交打包产品
            var product = new ProductDesign { name = "商务出行包", priceUsd = 199f, expectedConversion = 0.4f };
            Assert.IsTrue(mkt.SubmitProduct(product, out _));
            Assert.AreEqual(1, gs.mkt.products.Count);

            Object.Destroy(gsGo);
            Object.Destroy(mktGo);
            yield return null;
        }
    }
}
