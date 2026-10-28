using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 收件箱列表行排版的 NUnit 入口（2026-10-28 报障：主题折成两行压住发件人）。
    /// 只钉「单行收敛的三个属性」与「主题日期前缀的去除规则」；
    /// 像素级是否真的不重叠属界面手测，本用例不宣称已完成。
    /// </summary>
    public class InboxRowLayoutTests
    {
        [Test]
        public void ListRow_StaysSingleLineAndDropsRedundantDate()
        {
            Assert.IsTrue(
                InboxRowLayoutRegression.Run(),
                "收件箱列表行排版回归失败：或单行收敛没把 Wrap 关掉（长主题仍会折行压住发件人）、"
                + "或来文主题的日期前缀被留在了列表上、或把不像日期的正常前缀误砍、或空值会炸。"
                + "详见 Console 里的「收件箱列表行排版」一行。");
        }
    }
}
