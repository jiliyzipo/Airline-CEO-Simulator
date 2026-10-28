using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 转呈件信面（2026-10-28）的 NUnit 入口。
    ///
    /// 钉的是「CEO 打开这封信看到什么」：抬头点明来文机关与文日、正文末尾只署新闻官。
    /// 四种体例（政府 / 通讯社 / 企业 / 机构）各一封，预告与施行各一遍。
    /// </summary>
    public class ExternalForwardingTests
    {
        [Test]
        public void ForwardedMail_IsSignedOnlyByPressOfficer()
        {
            Assert.IsTrue(
                ExternalForwardingRegression.Run(),
                "转呈件信面不符：或发件人不是新闻官、或抬头没点明来文机关与文日、"
                + "或正文末尾不是「新闻官 姓名 / 日期」、或右对齐的机关落款块没有被撤掉。"
                + "详见 Console 里的「转呈件」一行。");
        }
    }
}
