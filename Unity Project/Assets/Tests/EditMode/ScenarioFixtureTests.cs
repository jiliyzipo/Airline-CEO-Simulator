using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 场景夹具（T05.2 缺员/停飞/维护、T05.6 缺槽）的 NUnit 入口。
    ///
    /// 断言在 <see cref="ScenarioFixtureRegression"/> 里，这里只把失败原因带进报告。
    /// 重点不是「夹具改没改字段」，而是**游戏自己的判定口认不认这个局面**：
    /// 停飞要判成持续性 <c>Retired</c>，缺槽要真的是「池满且无可用」。
    /// </summary>
    public class ScenarioFixtureTests
    {
        [Test]
        public void ScenarioFixture_ProducesStatesTheGameItselfRecognises()
        {
            Assert.IsTrue(ScenarioFixtureRegression.Run(),
                "场景夹具回归失败：看 Console 里「场景夹具回归」的 ✗ 项。"
                + "常见原因：①改了 isActive 但地面判定仍返回 None（不属持续性停飞，界面不会标红）；"
                + "②乘务判据在夹具里另写了一份，与 GameState.IsCabinCrew 分叉；"
                + "③填池只填了代表时刻，没填满整个网格，SlotsAvailable 仍大于 0；"
                + "④已经处于目标状态时仍返回 true，手测会以为「又生效了一次」。");
        }
    }
}
