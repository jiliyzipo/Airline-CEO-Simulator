using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 「深链借来安放子页的部门页，不许吃掉一次返回」的 NUnit 入口
    /// （2026-11-04 报障：邮件点机号 → 飞机详情 → &lt; 落在机队，再按一次才回信）。
    /// </summary>
    public class NavBackUnwindTests
    {
        [Test]
        public void Back_HopsOverTheBorrowedDepartmentPage()
        {
            Assert.IsTrue(
                NavBackUnwindRegression.Run(),
                "返回口径回归失败：或借来的部门页仍然吃掉了一次返回、或把「用户本来就在机队」"
                + "的正常路径也弹走了。详见 Console 里的「返回口径」一行。");
        }
    }
}
