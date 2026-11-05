using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// R48 行业情报两张表（★2026-11-05）。
    ///
    /// 与其它回归同理：不接进 NUnit 等于没做。这里最关键的是**容量恒等式**
    /// 与「表上余量 = 玩法余量」—— 两者一旦分家，CEO 会照着表去申请一个必然失败的槽。
    /// 详见 <see cref="IndustryAtlasRegression"/> 的头注释。
    /// </summary>
    public class IndustryAtlasEditModeTests
    {
        [Test]
        public void AtlasMatchesPlayablePool()
        {
            Assert.IsTrue(
                IndustryAtlasRegression.Run(),
                "行业情报表回归未通过：可能是「表上余量」与 SlotPool / SlotsAvailable 分家、"
                + "他航占满了整个时刻池（开航变成死结）、表格每次刷新都在变（不稳定哈希），"
                + "或航权表混进了国内航段；详见 Console 里的「R48 行业情报表」那几行。");
        }
    }
}
