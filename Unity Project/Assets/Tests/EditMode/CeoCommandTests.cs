using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 底栏 CEO 命令台的分派口径 NUnit 入口。断言在 <see cref="CeoCommandRegression"/> 里。
    ///
    /// 这个入口来得晚（2026-10-29）：此前 CeoCommandRegression 只挂在
    /// <c>LogicSelfTests.Run()</c> 的手动总集合上，而协作规则禁止拿全量自测替代隔离回归，
    /// 于是它在隔离跑批里长期不在执行范围内 —— 这正是「指令没有可执行的管线」能活下来的原因：
    /// 回归里测的全是 <c>RunCapacityDirective</c> 本身，没人测**命令台有没有调它**。
    /// </summary>
    public class CeoCommandTests
    {
        [Test]
        public void CeoCommand_RoutesSentencesToTheRightPipeline()
        {
            Assert.IsTrue(CeoCommandRegression.Run(),
                "命令台回归失败：看 Console 里「CEO 命令台 → 页面映射」那一行的 ✗ 项。"
                + "重点看末尾「命令台分派=」：它为 False 说明 Build 出来的 DeptAnswer 没带 ask 快照，"
                + "命令台读不到 topic，任何运力调令都会落到「这条指令没有可执行的管线」。");
        }
    }
}
