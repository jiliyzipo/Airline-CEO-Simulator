using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 新闻官转呈（2026-10-24）的 NUnit 入口。
    ///
    /// 只钉数据口径：发件人是谁、姓名是否随存档派生、原文是否还在。
    /// 「列表上显示成什么样」属界面手测范围，本用例不宣称已目视确认。
    /// </summary>
    public class PressDeskTests
    {
        [Test]
        public void ExternalMail_IsForwardedByNamedPressOfficer()
        {
            Assert.IsTrue(
                PressDeskRegression.Run(),
                "新闻官回归失败：或同一存档取到两个不同姓名、或跨年/换季改了名、"
                + "或换存档仍是同一人、或信笺把新闻官拼成双重职务、"
                + "或外源来文的发件人仍是发布单位、或转呈后原文（体例与落款）被改写。"
                + "详见 Console 里的「新闻官」一行。");
        }
    }
}
