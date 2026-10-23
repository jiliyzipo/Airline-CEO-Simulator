using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// K12：季末一次性项与月烧钱分离。
    ///
    /// 与 <see cref="SlotCashHintEditModeTests"/> 同理 —— 不接进 NUnit 的回归等于没做：
    /// `LogicSelfTests` 的菜单入口不在 `unity-editmode` profile 里，界面那一行文案坏了预检照样全绿。
    /// 这里直接跑 <see cref="QuarterlyOneOffRegression.Run"/>。
    /// </summary>
    public class QuarterlyOneOffEditModeTests
    {
        [Test]
        public void OneOffItemsStayOutOfMonthlyBurn()
        {
            Assert.IsTrue(
                QuarterlyOneOffRegression.Run(),
                "季末一次性项的预计区间没有覆盖 K10 实测汇兑值（含符号），"
                + "或带宽常数与 FxEconomics.RateConvergeBand 不同源，"
                + "或文案没有写明「不计入月烧钱」，"
                + "或实际分项 / 预计对照（落在区间 / 超出区间）缺失；"
                + "详见 Console 里的「K12 季末一次性项」那一行。");
        }
    }
}
