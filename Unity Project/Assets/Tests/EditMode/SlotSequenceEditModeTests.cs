using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 批次一的**完整申请序列** NUnit 入口（Codex 2026-11-09 裁决第 2 条）。
    ///
    /// 与 `CapacityServiceEditModeTests` 的分工：
    /// · 那条证明「19,968 格容量数字与派生接口一字不差」——判据层；
    /// · 本条走**真实调用链**（提案 → 批准 → 到期交割 → 驳回分支），
    ///   比较批准/拒绝、持仓时刻与方向、资金流水与余额——序列层。
    ///
    /// 判据层全等不能顶替序列层全等，这条因此不能省。
    /// </summary>
    public class SlotSequenceEditModeTests
    {
        [Test]
        public void ApplicationSequenceKeepsApprovalsHoldingsAndCash()
        {
            Assert.IsTrue(
                SlotSequenceRegression.Run(),
                "时刻槽申请序列在抽取后发生漂移（批准/驳回、持仓时刻与方向、资金流水或余额），"
                + "逐项结果见 Console 的「SLOTSEQ|」行。");
        }
    }
}
