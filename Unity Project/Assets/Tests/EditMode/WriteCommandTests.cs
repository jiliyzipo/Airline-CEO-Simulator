using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 命令台四类写指令（开线 / 购机 / 招聘 / 融资）的 NUnit 入口。
    ///
    /// 断言全部在 <see cref="WriteCommandRegression"/> 里：这里只负责把失败原因带进测试报告。
    /// 重点不是「有没有报错」，而是**请示件有没有真的落在既有管线上**，
    /// 以及**读类句子有没有被误认成写类** —— 后者会给 CEO 送来他根本没下过的请示。
    /// </summary>
    public class WriteCommandTests
    {
        [Test]
        public void WriteCommands_RouteToExistingProposalPipelines()
        {
            Assert.IsTrue(WriteCommandRegression.Run(),
                "命令台写指令回归失败：看 Console 里「命令台写指令」那一行的 ✗ 项。"
                + "常见原因：①四类指令有一类没被认成 Act（落进了部门汇报）；"
                + "②购机机型命中了短前缀（A320 抢先于 A320neo / B737-8 抢先于 B737-800）；"
                + "③缺参数时没有退回引导，而是按默认值发了请示；"
                + "④投递没走 InboxManager 的既有 action（收件箱里那一封的 action 字段不对）。");
        }
    }
}
