using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// K11 遗留：局内跑道并入还本付息之后的「逐季读数」。
    ///
    /// `RunwayDebtRegression` 原先只挂在 `LogicSelfTests` 菜单上，而菜单入口不在
    /// `unity-editmode` profile 里 —— 等于「写了断言但预检永远跑不到」，§14 末尾那条
    /// 「第 2、3 季 monthsRemaining 递减未逐季留痕」就是这么留下的。这里把它接进快照：
    /// 接管瞬间 / 4 个季末的月数、烧钱、跑道逐格打印，任何一格倒退都判失败。
    /// </summary>
    public class RunwayDebtEditModeTests
    {
        [Test]
        public void QuarterlyPayoff_LowersBurnAndNeverShortensRunway()
        {
            Assert.IsTrue(
                RunwayDebtRegression.Run(),
                "跑道/月供口径出现回归：或权益型被当成负债、或已还清仍计入、或末季未按剩余月份摊，"
                + "或局内烧钱漏了月供、或四预设跑道路标漂移，"
                + "或逐季推进时烧钱未降 / 跑道倒退 / 清偿季降幅不等于一个月供；"
                + "详见 Console 里的「跑道/月供口径」与「逐季推进」两行。");
        }
    }
}
