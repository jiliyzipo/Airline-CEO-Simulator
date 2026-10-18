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
    /// MKT 市场销售部 PlayMode（Phase 2 Week 8 · 批次 B：竞争/渠道/品牌）。
    /// 覆盖：导航工厂、竞争对标/渠道 ROI/品牌危机数值、渠道预算与大客户/品牌回写。
    /// </summary>
    public class MktBPlayModeTests : IsolatedPlayModeFixture
    {
        // —— 导航：运行时工厂可打开 MKT 批次 B 全部子页 ——

        [UnityTest]
        public IEnumerator MktRuntimeFactory_BuildsAllBatchBSubpages()
        {
            var host = new GameObject("MktBRuntimeHost", typeof(RectTransform));

            string[] pages =
            {
                "n60_竞争雷达", "n61_竞对详情", "n62_竞争_航线网络", "n63_竞争_产品服务",
                "n64_竞争_运营成本", "n65_竞争_战略资本",
                "n67_大客户合同", "n68_渠道预算", "n69_分销渠道管理", "n70_直销渠道管理",
                "n71_渠道ROI分析", "n72_品牌危机公关", "n73_舆情监测中心", "n74_危机响应指挥部",
                "n75_品牌修复计划"
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

        // —— 数值：竞争对标 / 渠道 ROI / 品牌危机 ——

        [UnityTest]
        public IEnumerator MktCompetitionEconomics_ScoringChannelBrand()
        {
            // 7 维评分：航线越多网络分越高；负债越低成本/资本分越高
            var strong = new MktCompetitionEconomics.BenchInput
            {
                routes = 12, marketShare = 25f, avgFare = 1000f, reputation = 80f,
                debtRatio = 0.2f, cash = 2000f, fleetSize = 20, ancillaryScore = 70f, macroScore = 70f
            };
            var weak = new MktCompetitionEconomics.BenchInput
            {
                routes = 3, marketShare = 8f, avgFare = 700f, reputation = 40f,
                debtRatio = 0.8f, cash = 200f, fleetSize = 4, ancillaryScore = 30f, macroScore = 30f
            };
            float strongScore = MktCompetitionEconomics.OverallScore(MktCompetitionEconomics.DimensionScores(strong));
            float weakScore = MktCompetitionEconomics.OverallScore(MktCompetitionEconomics.DimensionScores(weak));
            Assert.Greater(strongScore, weakScore);

            // 态势判断
            Assert.AreEqual("领先：综合竞争力高于最强竞对，可巩固优势并主动出击。",
                MktCompetitionEconomics.SituationalVerdict(strongScore, weakScore));

            // 渠道 ROI：收入 > 成本 → 正 ROI
            Assert.Greater(MktCompetitionEconomics.ChannelRoi(100f, 20f), 0f);
            Assert.AreEqual(0f, MktCompetitionEconomics.ChannelRoi(10f, 0f), 0.001f);
            Assert.Greater(MktCompetitionEconomics.Cac(12f, 4000), 0f);

            // 危机分级：负面占比越高等级越高
            Assert.AreEqual(0, MktCompetitionEconomics.CrisisLevel(0.1f, 50));
            Assert.AreEqual(1, MktCompetitionEconomics.CrisisLevel(0.35f, 100));
            Assert.AreEqual(2, MktCompetitionEconomics.CrisisLevel(0.6f, 300));

            // 信任指数随负面下降；修复后回升
            float trust = MktCompetitionEconomics.TrustIndex(20f, 0.15f);
            float recovered = MktCompetitionEconomics.RecoveredTrust(trust, 1f);
            Assert.Greater(recovered, trust);

            // 大客户价值：全球性 × 战略系数
            Assert.Greater(
                MktCompetitionEconomics.KeyAccountValueWan(800f, 0.9f, 0),
                MktCompetitionEconomics.KeyAccountValueWan(800f, 0.9f, 2));
            yield return null;
        }

        // —— 主操作回写：渠道预算 / 大客户 / 品牌危机 ——

        [UnityTest]
        public IEnumerator MktManager_WritesChannelAccountBrand()
        {
            var gsGo = new GameObject("MktBGS");
            var gs = gsGo.AddComponent<GameState>();
            var mktGo = new GameObject("MktBManager");
            var mkt = mktGo.AddComponent<MktManager>();

            // 渠道预算回写
            var budget = new ChannelBudget { annualBudgetWan = 300f, directRatio = 0.5f };
            Assert.IsTrue(mkt.SaveChannelBudget(budget, out _));
            Assert.AreEqual(0.5f, gs.mkt.channelBudget.directRatio, 0.001f);

            // 大客户签约回写
            var account = new KeyAccount { name = "环球科技", tier = 0, annualSpendWan = 800f, frontDiscount = 0.9f };
            Assert.IsTrue(mkt.SignKeyAccount(account, out _));
            Assert.AreEqual(1, gs.mkt.keyAccounts.Count);
            Assert.IsTrue(gs.mkt.keyAccounts[0].signed);

            // 品牌危机回写
            var brand = new BrandCrisis { active = true, crisisLevel = 1, negativeVolume = 300 };
            Assert.IsTrue(mkt.UpdateBrandCrisis(brand, out _));
            Assert.IsTrue(gs.mkt.brand.active);
            Assert.AreEqual(1, gs.mkt.brand.crisisLevel);

            // 整改措施回写
            Assert.IsTrue(mkt.SubmitRepairMeasure(new RepairMeasure { name = "服务流程整改", dept = "运营部" }, out _));
            Assert.AreEqual(1, gs.mkt.brand.repairMeasures.Count);

            Object.Destroy(gsGo);
            Object.Destroy(mktGo);
            yield return null;
        }
    }
}
