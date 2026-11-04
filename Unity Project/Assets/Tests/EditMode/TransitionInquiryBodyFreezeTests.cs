using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 询价函办结口径的 NUnit 入口（2026-11-04 用户手测：信里的「当前可签闲置机」
    /// 把已经租走的 B-2027 还挂着，与「机队 › 过渡业务」的在租表互相打脸）。
    /// 断言都在 <see cref="TransitionInquiryBodyFreezeRegression"/> 里，桥与这里同源。
    /// </summary>
    public class TransitionInquiryBodyFreezeTests
    {
        [Test]
        public void ClosedInquiry_StopsClaimingAircraftAreStillAvailable()
        {
            Assert.IsTrue(
                TransitionInquiryBodyFreezeRegression.Run(),
                "询价函办结口径回归失败：或名单没冻结、或底部还在让人「在本函底部选择」、"
                + "或每日 Ensure 把冻结说明套娃了。详见 Console 里的「询价函办结口径-✗」行。");
        }
    }
}
