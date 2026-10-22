using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// D2 / K15：航司星级必须随客流单调。
    ///
    /// 与 <see cref="SlotCashHintEditModeTests"/> 同理 —— 不接进 NUnit 的回归等于没做：
    /// `tools/balance --routeinventory` 里那条断言不在 `unity-editmode` profile 里，
    /// 只有星级分错时，集中验证照样全绿。
    /// </summary>
    public class AirlineTierEditModeTests
    {
        [Test]
        public void StarTierFollowsPassengerVolume()
        {
            Assert.IsTrue(
                AirlineTierRegression.Run(),
                "星级没有随客流单调（大客流被分到更低档），或两档的客流区间重叠，"
                + "或某一档被清空，或四个具名预设自身的档位没有随客流递增（如「广州★2 / 成都★5」）。"
                + "详见 Console 里的「D2 星级随客流单调」那一行。");
        }
    }
}
