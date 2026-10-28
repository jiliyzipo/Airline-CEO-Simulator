using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 教程与引导文案的 NUnit 入口（P23）。
    ///
    /// 断的是两件事：文案里点名的句子命令台真的听得懂；文案里的数取自常量而不是另写一遍字面量。
    /// 具体断言见 <see cref="TutorialCopyRegression"/>。
    /// </summary>
    public class TutorialCopyTests
    {
        [Test]
        public void TutorialCopy_MatchesRoutingAndConstants()
        {
            Assert.IsTrue(TutorialCopyRegression.Run(),
                "教程文案回归失败：看 Console 里「教程/引导文案」那一行的 ✗ 项。"
                + "常见原因：①改了「启动资金/首航结算」的常量却没走 StartupCostText / FirstFlight*；"
                + "②占位语或 S6 教了一句命令台不认的话（玩家会以为是自己说错了）；"
                + "③某页的提示语漏了本页能办的那一类指令。");
        }
    }
}
