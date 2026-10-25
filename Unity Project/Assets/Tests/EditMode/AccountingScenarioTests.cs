using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// T04.3「8 场景现金对账」的 NUnit 入口。
    ///
    /// 先钉一条判定规则（否则会自我欺骗）：**COVERED = 有 NUnit 用例把现金增量或应计桶与期望值比对**。
    /// 只断言「状态变了」不算，只 <c>Debug.Log</c> 打印期望值更不算。
    /// 本用例覆盖此前零断言的 **S5 退租**，以及只验过 <c>checks.Count</c> 的 **S4 维护定检**。
    /// </summary>
    public class AccountingScenarioTests
    {
        [Test]
        public void LeaseReturnAndMaintenanceCheck_MatchCashAndAccrual()
        {
            Assert.IsTrue(
                AccountingScenarioRegression.Run(),
                "T04.3 账务场景回归失败：或自有退役未按残值入账、或经营租赁退租白送了残值（造钱口子）、"
                + "或融资租赁提前退租违约金不等于剩余租期租金 × 30%、或期满仍在罚、"
                + "或重复处置二次入账、或现金不足仍退租、"
                + "或退出后月租/月维护未停计、或在册数仍含退出机；"
                + "或定检扣款不等于 CheckCostWan、或定检进了损益、或现金不足仍排检。"
                + "详见 Console 里的「T04.3-S5」「T04.3-S4」两行。");
        }
    }
}
