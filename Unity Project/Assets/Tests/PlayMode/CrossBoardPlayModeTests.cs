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
    /// Phase 3 · CROSS 跨部门决策流 + BOARD 董事会 PlayMode。
    /// 覆盖：运行时工厂、决策五部门意见/裁决/补条件模板、Manager 回写 GameState。
    /// </summary>
    public class CrossBoardPlayModeTests : IsolatedPlayModeFixture
    {
        // —— 导航：运行时工厂可构建 Phase 3 全部页 ——

        [UnityTest]
        public IEnumerator RuntimeFactory_BuildsAllPhase3Pages()
        {
            var host = new GameObject("CrossRuntimeHost", typeof(RectTransform));

            var cases = new (string id, System.Type type)[]
            {
                ("n21_CEO_决策会议", typeof(DecisionMeetingPanel)),
                ("n104_补条件表单", typeof(SupplementConditionPanel)),
                ("n23_部门负责人任命", typeof(AppointmentPanel)),
                ("n26_财年结算_续聘_解雇", typeof(YearEndSettlementPanel)),
                ("n28_宏观经济面板", typeof(MacroPanelUI)),
                ("n30_随机事件日志", typeof(EventLogPanelUI)),
                ("n38_历史回放", typeof(HistoryReplayPanel)),
            };

            foreach (var (id, type) in cases)
            {
                var go = ShellRuntimeFactory.TryCreate(id, host.transform);
                Assert.IsNotNull(go, "应能构建 " + id);
                Assert.IsNotNull(go.GetComponent(type), id + " 应挂 " + type.Name);
            }

            Object.Destroy(host);
            yield return null;
        }

        // —— 数值：五部门意见 / 裁决 / 补条件模板 ——

        [UnityTest]
        public IEnumerator DecisionEconomics_OpinionsAndVerdict()
        {
            // 资金充裕 + 高准点 + 高客座 + 高满意度 + 高安全 → 全绿 → 批准
            var good = new DecisionSnapshot
            {
                cash = 5000f, monthlyBurn = 100f, runwayMonths = 20f,
                loadFactor = 85f, onTime = 90f, safetyIndex = 90f,
                activeRoutes = 5, yearNetProfit = 300f, monthlyRevenue = 900f,
                gdpPhase = "boom", boardSatisfaction = 75f
            };
            var purchase = new DecisionProposal { topic = "引进 A320neo", category = DecisionCategories.Purchase, costWan = 1500f };
            var opinions = DecisionEconomics.EvaluateOpinions(purchase, good);
            Assert.AreEqual(5, opinions.Length);
            Assert.AreEqual("approve", DecisionEconomics.Verdict(opinions));
            Assert.AreEqual("绿5 黄0 红0", DecisionEconomics.OpinionSummary(opinions));

            // 现金不足 → 财务红 → 拒绝
            var poor = new DecisionSnapshot
            {
                cash = 200f, monthlyBurn = 100f, runwayMonths = 2f,
                loadFactor = 70f, onTime = 90f, safetyIndex = 90f,
                activeRoutes = 5, yearNetProfit = -50f, monthlyRevenue = 400f,
                gdpPhase = "boom", boardSatisfaction = 50f
            };
            var poorOpinions = DecisionEconomics.EvaluateOpinions(purchase, poor);
            Assert.AreEqual("reject", DecisionEconomics.Verdict(poorOpinions));

            // 资金面偏紧 → 至少一个黄 → 补条件
            var tight = new DecisionSnapshot
            {
                cash = 1600f, monthlyBurn = 100f, runwayMonths = 3f,
                loadFactor = 85f, onTime = 90f, safetyIndex = 90f,
                activeRoutes = 5, yearNetProfit = 100f, monthlyRevenue = 900f,
                gdpPhase = "boom", boardSatisfaction = 60f
            };
            var tightOpinions = DecisionEconomics.EvaluateOpinions(purchase, tight);
            Assert.AreEqual("defer", DecisionEconomics.Verdict(tightOpinions));

            // 补条件模板：购机/开线/招聘/通用 各有条目
            Assert.Greater(DecisionEconomics.ConditionTemplates(DecisionCategories.Purchase).Count, 0);
            Assert.Greater(DecisionEconomics.ConditionTemplates(DecisionCategories.Route).Count, 0);
            Assert.Greater(DecisionEconomics.ConditionTemplates(DecisionCategories.Recruit).Count, 0);
            Assert.Greater(DecisionEconomics.ConditionTemplates(DecisionCategories.Generic).Count, 0);

            // 延期损失文案（开线日利>0 应量化）
            var routeProposal = new DecisionProposal { topic = "开通航线", category = DecisionCategories.Route, expectedDailyProfit = 5f };
            Assert.IsTrue(DecisionEconomics.DeferLossText(routeProposal).Contains("机会成本"));

            yield return null;
        }

        // —— Manager 回写：决策会议批准/暂缓 → history / supplementForms ——

        [UnityTest]
        public IEnumerator DecisionMeetingManager_WritesState()
        {
            var gsGo = new GameObject("CrossGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 5000f;
            gs.boardSatisfaction = 70f;

            if (DecisionMeetingManager.Instance == null)
            {
                var mgrGo = new GameObject("DecisionMeetingManager");
                mgrGo.AddComponent<DecisionMeetingManager>();
            }

            var mgr = DecisionMeetingManager.Instance;
            Assert.IsNotNull(mgr);

            var p = new DecisionProposal
            {
                id = "test_meet",
                topic = "引进 1 架 A320neo",
                category = DecisionCategories.Purchase,
                costWan = 1500f,
                sourceLabel = "运营部",
                sourcePageId = "n9_机队"
            };

            mgr.OpenMeeting(p);
            Assert.IsNotNull(mgr.CurrentProposal);
            Assert.AreEqual(5, mgr.CurrentOpinions.Length);

            string outcome = mgr.Approve();
            Assert.AreEqual("approved", mgr.LastOutcome);
            Assert.IsTrue(outcome.Contains("批准"));
            Assert.AreEqual(1, gs.decision.history.Count);
            Assert.AreEqual("approved", gs.decision.history[0].outcome);

            // 暂缓 → 生成补条件表单
            mgr.OpenMeeting(p);
            mgr.Defer();
            Assert.IsNotNull(mgr.ActiveSupplement);
            Assert.Greater(mgr.ActiveSupplement.conditions.Count, 0);
            Assert.AreEqual(1, gs.decision.supplementForms.Count);
            mgr.ClearSupplement();

            // 取消不落记录
            int historyBefore = gs.decision.history.Count;
            mgr.OpenMeeting(p);
            mgr.Cancel();
            Assert.AreEqual(historyBefore, gs.decision.history.Count);

            Object.Destroy(gsGo);
            yield return null;
        }
    }
}
