using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// R47 时刻二级市场（2026-11-02）。
    ///
    /// 与 <see cref="SlotCashHintEditModeTests"/> 同理：不接进 NUnit 的回归等于没做。
    /// 这里最关键的一条是「池满仍可成交」—— 旧版付费渠道带着池容量判据，
    /// 池一满就买不到，而池满恰恰是玩家唯一想花钱的时候。详见
    /// <see cref="SlotMarketRegression"/> 的头注释。
    /// </summary>
    public class SlotMarketEditModeTests
    {
        [Test]
        public void SecondaryMarketTradesEvenWhenPoolFull()
        {
            Assert.IsTrue(
                SlotMarketRegression.Run(),
                "时刻二级市场回归未通过：可能是「池满时买不到」（305 号缺陷复发）、"
                + "挂牌/成交账目不平、使用中的槽被挂牌，或卖出意向没进收件箱；"
                + "详见 Console 里的「R47 时刻二级市场」那几行。");
        }
    }
}
