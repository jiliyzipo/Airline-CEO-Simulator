using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// R45 时刻两条渠道（协调池免费 / 二级市场付费）。
    ///
    /// ★2026-11-06：这个回归原先**只能通过 AI 触发桥跑**（白名单里有 `slot-channel`，
    /// 但 NUnit 侧没有壳），于是 batchmode 整包 EditMode 永远碰不到它。
    /// 而本轮把 `IndustryHeld` 从 45%–80% 抬到 85%–92% 之后，它的夹具门槛（原要求
    /// 某波段余量 ≥3 条）在物理上再也满足不了 —— 改口径时没人会发现，直到某天桥跑起来报红。
    /// 补上壳，让「渠道分野」这条不变量进入常规回归视野。
    ///
    /// 详见 <see cref="SlotChannelRegression"/> 的头注释。
    /// </summary>
    public class SlotChannelEditModeTests
    {
        [Test]
        public void FreeAndPaidChannelsStayDistinct()
        {
            Assert.IsTrue(
                SlotChannelRegression.Run(),
                "时刻渠道回归未通过：可能是「协调池申请」开始收费、付费渠道被池容量挡住"
                + "（池满时付钱也买不到 = 二级市场失去存在意义）、提案阶段就扣了钱，"
                + "或池满时免费渠道没有拒绝（免费 ≠ 无限）。详见 Console 里的「R45」那几行。");
        }
    }
}
