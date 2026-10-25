using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// T05.2「延迟与状态提示」的 NUnit 入口：在途送达提示、「已批准·待交割」标签、统一状态行。
    /// UI 是否真的画到屏幕上属界面手测，本用例只钉数据口径（与 T04.3 的判定规则一致：
    /// 只认「值 vs 期望值」，不认「函数写好了」）。
    /// </summary>
    public class InboxStatusTests
    {
        [Test]
        public void InflightAndStatusLabels_AreDistinguishable()
        {
            Assert.IsTrue(
                InboxStatusRegression.Run(),
                "T05.2 状态提示回归失败：或在途邮件仍然不可见、或「已批准·待交割」被并入「已批准」、"
                + "或统一状态行缺「在等 / 何时推进 / 需 CEO」三件事、或已到期的在途信仍显示「约 0 小时后送达」。"
                + "详见 Console 里的「T05.2 状态提示」一行。");
        }
    }
}
