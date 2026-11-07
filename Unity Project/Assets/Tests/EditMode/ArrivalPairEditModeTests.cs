using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 「一槽一对：离港 + 到港」的不变量回归（★2026-11-06）。
    ///
    /// 用户要求机场要有到港时刻、申请离港时一并申请到港、且到港可单独申请或购买。
    /// 详见 <see cref="ArrivalPairRegression"/> 的头注释。
    /// </summary>
    public class ArrivalPairEditModeTests
    {
        [Test]
        public void ArrivalPairIsRecordedAndNotDoubleCounted()
        {
            Assert.IsTrue(
                ArrivalPairRegression.Run(),
                "到港配对回归未通过：可能成对入账时把到港时刻丢了、池容量被到港与离港各扣一次"
                + "（机场容量凭空翻倍）、只到港持仓被当成离港槽排了班、二级市场买入后到港属性丢失、"
                + "到港端已满却照批了单，或「只申请到港」在收件箱这条路上丢了 kind。"
                + "详见 Console 里的「R46」那几行。");
        }
    }
}
