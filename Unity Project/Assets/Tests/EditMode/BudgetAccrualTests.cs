using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 裁定 R-A / A2（2026-10-25）：部门预算的季末分摊改为**逐日累计**，口径限于四项。
    ///
    /// 这里钉的是「口径」，不是「累加」：
    /// · 部门可管固定成本 = 烧钱率 + 机队维护 + 租赁 + CEO 薪酬，**不含**危机加成、运行薪资、存量人力；
    /// · 该值与现金口径的日固定成本同日同费率，但两把尺子故意不相等；
    /// · `AccrueBudgetSpend` 只按给定总额分摊，自己不重算 —— 分母由日推进提供。
    /// 「逐日累计不会拿季末费率反推整季」由
    /// `DevelopmentAccountingScenarioTests.S8` 的累加器断言证明（那里能真跑日推进）。
    /// </summary>
    public class BudgetAccrualTests
    {
        [Test]
        public void DeptDailyFixedCost_ExcludesPayrollAndCrisis_ButKeepsTheFourDepartmentItems()
        {
            // 四项：100 + 40 + 20 + 8 = 168 万/月 → 5.6 万/日
            Assert.AreEqual(5.6f, LiveClockLogic.DeptDailyFixedCostWan(100f, 40f, 20f, 8f), 1e-4f);

            // 现金口径在同样四项上多出「危机加成 + 运行薪资 + 存量人力」—— 两把尺子必须分得开
            float cashScope = LiveClockLogic.DailyFixedCostWan(100f, 40f, 20f, 8f, 12f, 30f, 15f);
            float deptScope = LiveClockLogic.DeptDailyFixedCostWan(100f, 40f, 20f, 8f);
            Assert.AreEqual((168f + 12f + 30f + 15f) / 30f, cashScope, 1e-4f);
            Assert.Less(deptScope, cashScope,
                "部门口径不得把工资与危机加成摊进部门预算（否则运营／人力部每季必「超支」）");
        }

        [Test]
        public void DeptDailyFixedCost_ClampsNegativeMonthToZero()
        {
            Assert.AreEqual(0f, LiveClockLogic.DeptDailyFixedCostWan(-50f, 10f, 0f, 0f), 1e-4f,
                "月度合计为负时按 0 处理，不能出现「负支出」反向冲销预算");
        }

        [Test]
        public void AccrueBudgetSpend_AllocatesExactlyTheGivenTotal_ByAmountShare()
        {
            var go = new GameObject("BudgetAccrualTests");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.budgetItems = new List<BudgetItem>
                {
                    new BudgetItem { deptId = "n13_运营部", amountWan = 60f },
                    new BudgetItem { deptId = "n17_机务工程部", amountWan = 20f },
                    new BudgetItem { deptId = "n18_人力资源部", amountWan = 20f },
                };

                FinanceManager.AccrueBudgetSpend(gs, 100f);

                Assert.AreEqual(60f, gs.budgetItems[0].spentWan, 1e-3f);
                Assert.AreEqual(20f, gs.budgetItems[1].spentWan, 1e-3f);
                Assert.AreEqual(20f, gs.budgetItems[2].spentWan, 1e-3f);

                float sum = 0f;
                for (int i = 0; i < gs.budgetItems.Count; i++) sum += gs.budgetItems[i].spentWan;
                Assert.AreEqual(100f, sum, 1e-3f,
                    "分摊必须严格等于分母（少摊会让部门显得没超支，多摊会凭空造支出）");

                // 分母为 0（老存档首季没有累加器）→ 早退，不写 8 个 0，也不抛
                float before = gs.budgetItems[0].spentWan;
                FinanceManager.AccrueBudgetSpend(gs, 0f);
                Assert.AreEqual(before, gs.budgetItems[0].spentWan, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
