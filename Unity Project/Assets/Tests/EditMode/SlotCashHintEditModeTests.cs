using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// K19：时刻槽提案的现金跑道提示。
    ///
    /// 与 <see cref="AirportCatalogEditModeTests"/> 同理 —— 不接进 NUnit 的回归等于没做：
    /// `LogicSelfTests` 那条菜单不在 `unity-editmode` profile 里，只有邮箱里那行文案坏了，
    /// 预检照样全绿。这里直接跑 <see cref="SlotCashHintRegression.Run"/>。
    /// </summary>
    public class SlotCashHintEditModeTests
    {
        [Test]
        public void SlotProposalShowsCashRunway()
        {
            Assert.IsTrue(
                SlotCashHintRegression.Run(),
                "时刻槽提案没有写出扣款后的现金跑道，或跑道数字与 (现金−报价)/净烧钱 不一致，"
                + "或「风险 … 危机线」行在跑道 <3 月时缺失 / 在跑道充足时误发；"
                + "详见 Console 里的「K19 时刻槽提案现金跑道」那一行。");
        }
    }
}
