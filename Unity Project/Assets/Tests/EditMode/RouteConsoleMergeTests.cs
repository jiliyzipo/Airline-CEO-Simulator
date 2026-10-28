using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 单航线页命令台合并的 NUnit 入口（2026-10-28 用户裁定：删掉页面内的运力指令框）。
    ///
    /// 钉的是「删框之后能力还在」：不带城对的调令靠页面上下文落到 CEO 正看着的那条线，
    /// 没上下文时仍然不猜，显式城对优先于上下文，页面里也不再留第二个输入框。
    /// 回执条观感与焦点行为属界面手测，本用例不宣称已完成。
    /// </summary>
    public class RouteConsoleMergeTests
    {
        [Test]
        public void RoutePage_UsesGlobalConsoleWithContextFallback()
        {
            Assert.IsTrue(
                RouteConsoleMergeRegression.Run(),
                "单航线页命令台合并回归失败：或带上下文仍不认调令（删掉指令框后那句话已无人受理）、"
                + "或无上下文时开始乱猜航线、或上下文盖掉了显式城对、或问句被当成调令、"
                + "或页面里又长出了输入框、或 canary 失效。详见 Console 里的「单航线页命令台合并」一行。");
        }
    }
}
