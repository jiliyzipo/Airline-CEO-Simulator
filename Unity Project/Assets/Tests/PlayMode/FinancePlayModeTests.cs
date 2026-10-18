using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    /// FIN 财务部 PlayMode（Phase 2 Week 5）：覆盖导航工厂、数值公式、预算提交/碳配额回写。
    /// </summary>
    public class FinancePlayModeTests : IsolatedPlayModeFixture
    {
        // —— 导航：运行时工厂可打开 FIN 全部子页 ——

        [UnityTest]
        public IEnumerator FinanceRuntimeFactory_BuildsAllFinSubpages()
        {
            var host = new GameObject("FinRuntimeHost", typeof(RectTransform));

            string[] pages =
            {
                "n22_年度预算编制_审批", "n31_对冲工作台", "n32_碳配额管理",
                "n25_紧急融资_破产保护", "n103_条款确认", "n106_重组条件勾选"
            };
            foreach (var page in pages)
            {
                var go = FinanceRuntimeFactory.TryCreate(page, host.transform);
                Assert.IsNotNull(go, "应能构建 " + page);
                Assert.IsNotNull(go.GetComponent<FinanceSubpagePanel>(), page + " 应挂 FinanceSubpagePanel");
            }

            Object.Destroy(host);
            yield return null;
        }

        // —— 数值：预算 / 董事会投票 / WACC / 信用评级 ——

        [UnityTest]
        public IEnumerator FinanceEconomics_BudgetBoardAndWacc()
        {
            // 建议预算 = 现金 50%；500 万 → 250 万，落在 40%~60% 区间 → 5 票通过
            Assert.AreEqual(250f, FinanceEconomics.RecommendedBudget(500f), 0.01f);
            Assert.AreEqual(5, FinanceEconomics.EstimateBoardVotes(250f, 500f, 50f));
            Assert.IsTrue(FinanceEconomics.BudgetApproved(250f, 500f, 50f));

            // 激进预算（75%+）会被董事会压到 2 票 → 否决
            Assert.Less(FinanceEconomics.EstimateBoardVotes(400f, 500f, 50f), FinanceEconomics.BoardVotesRequired);

            // WACC：AAA × 繁荣期 = 6.0 − 0.5
            Assert.AreEqual(5.5f, FinanceEconomics.Wacc(CreditRating.AAA, "boom"), 0.01f);
            Assert.Greater(FinanceEconomics.Wacc(CreditRating.B, "recession"), FinanceEconomics.Wacc(CreditRating.AAA, "boom"));

            // 信用评级：现金宽裕 → AAA
            Assert.AreEqual(CreditRating.AAA, FinanceEconomics.EstimateCreditRating(1000f, 0f, 50f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FinanceEconomics_CarbonAndFinancingTerms()
        {
            // 碳负债 = 排放 × 碳价 / 10000
            float emit = FinanceEconomics.EstimateAnnualEmissionsTons(3);
            float liability = FinanceEconomics.CarbonLiabilityWan(emit, 0f, 85f);
            Assert.Greater(emit, 0f);
            Assert.AreEqual(emit * 85f / 10000f, liability, 0.01f);
            Assert.AreEqual(0f, FinanceEconomics.CarbonLiabilityWan(emit, emit, 85f), 0.01f);

            // 融资条款：银行贷款（BBB / 繁荣期）
            var input = new FinanceEconomics.FinancingInput
            {
                cash = 100f,
                monthlyBurn = 80f,
                rating = CreditRating.BBB,
                economicPhase = "boom",
                boardSatisfaction = 50f,
                fleetCount = 5
            };
            var bank = FinanceEconomics.BuildFinancingTerm(FinancingKind.Bank, input);
            Assert.AreEqual(360f, bank.amountWan, 0.01f); // max(80, 80*4.5)
            Assert.AreEqual(7.5f, bank.annualRate, 0.01f); // 5 + 2.5(BBB) + 0
            Assert.AreEqual(12, bank.termMonths);

            var bridge = FinanceEconomics.BuildFinancingTerm(FinancingKind.Bridge, input);
            Assert.Greater(bridge.annualRate, bank.annualRate);

            // 政府救助仅萧条期解锁
            Assert.IsTrue(FinanceEconomics.IsBailoutUnlocked("recession"));
            Assert.IsFalse(FinanceEconomics.IsBailoutUnlocked("boom"));
            yield return null;
        }

        // —— 主操作回写：预算提交 ——

        [UnityTest]
        public IEnumerator FinanceManager_SubmitBudget_WritesGameState()
        {
            var gsGo = new GameObject("FinGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 500f;
            gs.boardSatisfaction = 50f;

            var finGo = new GameObject("FinanceManager");
            var fin = finGo.AddComponent<FinanceManager>();

            fin.EnsureDefaultBudget(gs);
            Assert.AreEqual(8, gs.budgetItems.Count);
            Assert.AreEqual(250f, FinanceManager.BudgetTotal(gs), 1f);

            bool ok = fin.SubmitBudget(out string msg);
            Assert.IsTrue(ok, msg);
            Assert.IsTrue(gs.budgetSubmitted);
            Assert.IsTrue(gs.budgetApproved);

            Object.Destroy(gsGo);
            Object.Destroy(finGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FinanceManager_PurchaseCarbon_ReducesCash()
        {
            var gsGo = new GameObject("FinGS2");
            var gs = gsGo.AddComponent<GameState>();
            gs.cash = 500f;
            gs.carbonPricePerTon = 85f;

            var finGo = new GameObject("FinanceManager2");
            var fin = finGo.AddComponent<FinanceManager>();

            float cashBefore = gs.cash;
            bool ok = fin.PurchaseCarbon(1000f, out string msg);
            Assert.IsTrue(ok, msg);
            Assert.AreEqual(1000f, gs.carbonPurchasedQuotaTons, 0.01f);
            Assert.AreEqual(cashBefore - 8.5f, gs.cash, 0.01f); // 1000 × 85 / 10000

            Object.Destroy(gsGo);
            Object.Destroy(finGo);
            yield return null;
        }

        // —— 财务细分标签页（P2 长线运营 · 税务/保险/PDP/碳合规 2×2 四宫格）——

        [UnityTest]
        public IEnumerator FinancePanelUI_DetailTab_ShowsFinancialSegmentation()
        {
            var gsGo = new GameObject("FinDetailGS");
            var gs = gsGo.AddComponent<GameState>();
            gs.hubIata = "PEK";
            gs.year = 1;
            gs.safetyRating = SafetyRating.B;
            gs.insuranceCoverage = InsuranceCoverage.Full;
            gs.carbonPricePerTon = 85f;
            gs.carbonPurchasedQuotaTons = 100f;
            gs.yearTaxPaidWan = 123f;
            gs.cumulativeTaxPaidWan = 456f;
            gs.pdpPayableWan = 888f;

            gs.fleet.Add(new Aircraft
            {
                registration = "B-0001",
                type = "A320neo",
                purchasePrice = 11000f,
                age = 1,
                isActive = true,
                status = AircraftStatuses.Airworthy
            });
            gs.routeSubsidies.Add(new RouteSubsidy
            {
                routeId = "PEK-SZX",
                annualAmountWan = 50f,
                yearsRemaining = 3,
                note = "测试补贴"
            });
            gs.aircraftOrders.Add(new AircraftOrder
            {
                orderId = "ORD-1",
                typeId = "A320neo",
                count = 2,
                unitPriceWan = 11000f,
                totalWan = 22000f,
                orderYear = 1,
                orderQuarter = 1,
                deliveryYear = 3,
                deliveryQuarter = 1,
                deliveryQuartersTotal = 8,
                depositPaidWan = 3300f
            });
            gs.carbonCompliance.etsEmissionsTons = 1000f;
            gs.carbonCompliance.corsiaEmissionsTons = 2000f;
            gs.carbonCompliance.etsRequiredTons = 250f;
            gs.carbonCompliance.corsiaRequiredTons = 300f;
            gs.carbonCompliance.etsCostWan = 10f;
            gs.carbonCompliance.corsiaCostWan = 12f;

            var host = new GameObject("FinDetailHost", typeof(RectTransform));
            var ui = FinancePanelUI.Build(host.transform);
            Assert.IsNotNull(ui);

            var tab = ui.transform.Find("FinanceHome/SummaryBox/OpenDetail");
            Assert.IsNotNull(tab, "财务总览应能进入已存在的税务、保险与订单明细");
            var button = tab.GetComponent<UnityEngine.UI.Button>();
            Assert.IsNotNull(button);
            button.onClick.Invoke();

            var tax = GetPrivateField(ui, "_taxText");
            var ins = GetPrivateField(ui, "_insuranceText");
            var ord = GetPrivateField(ui, "_orderText");
            var car = GetPrivateField(ui, "_carbonText");
            Assert.IsNotNull(tax, "税务卡片文本应存在");
            Assert.IsNotNull(ins, "保险卡片文本应存在");
            Assert.IsNotNull(ord, "订单簿卡片文本应存在");
            Assert.IsNotNull(car, "碳合规卡片文本应存在");

            string taxText = GetText(tax);
            string insText = GetText(ins);
            string ordText = GetText(ord);
            string carText = GetText(car);

            Assert.IsTrue(taxText.Contains("企业所得税"), "税务卡片应含所得税率，实际：" + taxText);
            Assert.IsTrue(taxText.Contains("25%"), "PEK 枢纽税率应为 25%，实际：" + taxText);
            Assert.IsTrue(insText.Contains("全险"), "保险卡片应显示当前档位，实际：" + insText);
            Assert.IsTrue(ordText.Contains("ORD-1"), "订单簿应显示订单号，实际：" + ordText);
            Assert.IsTrue(carText.Contains("EU ETS") && carText.Contains("CORSIA"), "碳合规应含 ETS/CORSIA，实际：" + carText);

            Object.Destroy(host);
            Object.Destroy(gsGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator P16_FinanceCrisisMail_OfferApproveRejectExpire()
        {
            var gsGo = new GameObject("P16GS");
            var gs = gsGo.AddComponent<GameState>();
            gs.inbox = new List<InboxMail>();
            gs.cash = 80f;
            gs.monthlyBurnRate = 80f;
            gs.ceoWeeklyPayWan = 0f;
            gs.financeCrisisBurnBonus = 0f;
            gs.boardSatisfaction = 55f;
            gs.year = 1;
            gs.quarter = 2;

            Assert.IsTrue(LoanManager.IsFinanceUnlocked(gs));
            InboxManager.MaybeOfferFinanceCrisis(gs);
            Assert.AreEqual(1, gs.inbox.Count);
            var mail = gs.inbox[0];
            Assert.AreEqual("finance_crisis", mail.action);
            Assert.AreEqual(InboxManager.RecommendFinanceDest(gs), mail.dest);

            gs.cash = 400f;
            gs.monthlyBurnRate = 80f;
            gs.financeCrisisBurnBonus = 0f;
            InboxManager.MaybeOfferFinanceCrisis(gs);
            Assert.AreEqual(1, gs.inbox.Count, "跑道回升后不应重复发信");

            float cashBefore = gs.cash;
            float satBefore = gs.boardSatisfaction;
            FinanceEconomics.CrisisInaction(gs.EffectiveMonthlyBurn, out float fee, out float bonus);
            Assert.IsTrue(InboxManager.RejectFinanceCrisis(gs, mail, out string msg));
            Assert.AreEqual("rejected", mail.status);
            Assert.AreEqual(cashBefore - fee, gs.cash, 0.01f);
            Assert.AreEqual(satBefore - 12f, gs.boardSatisfaction, 0.01f);
            Assert.AreEqual(bonus, gs.financeCrisisBurnBonus, 0.01f);
            Assert.IsTrue(msg.Contains("供应商催款"));

            gs.cash = 50f;
            gs.monthlyBurnRate = 80f;
            gs.financeCrisisBurnBonus = 0f;
            gs.boardSatisfaction = 30f;
            gs.inbox.Clear();
            gs.year = 1;
            gs.quarter = 3;
            InboxManager.MaybeOfferFinanceCrisis(gs);
            var crisis = gs.inbox[0];
            Assert.AreEqual("restructure", crisis.dest);

            crisis.receivedYear = 1;
            crisis.receivedQuarter = 1;
            crisis.deadlineQuarters = 1;
            gs.year = 1;
            gs.quarter = 3;
            float cash2 = gs.cash;
            InboxManager.ExpireOpenFinanceCrisis(gs);
            Assert.AreEqual("rejected", crisis.status);
            Assert.Less(gs.cash, cash2);

            Object.Destroy(gsGo);
            yield return null;
        }

        private static object GetPrivateField(FinancePanelUI ui, string fieldName)
        {
            var f = typeof(FinancePanelUI).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return f?.GetValue(ui);
        }

        private static string GetText(object textComponent)
        {
            if (textComponent == null) return null;
            var prop = textComponent.GetType().GetProperty("text");
            return prop?.GetValue(textComponent) as string;
        }
    }
}
