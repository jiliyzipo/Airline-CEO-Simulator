using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// T05.6「备选时刻可按真实约束检验」的 NUnit 入口。断言在 <see cref="SlotCandidateRegression"/> 里。
    ///
    /// 这个入口来得晚（2026-10-31）：此前备选时刻只挂在 <c>LogicSelfTests.Run()</c> 的手动总集合上，
    /// 而协作规则禁止拿全量自测替代隔离回归，于是它在隔离跑批里长期不在执行范围内 ——
    /// 「运营部说这一刻立刻能排，玩家照做却排不上」这类问题不会有任何东西变红。
    /// </summary>
    public class SlotCandidateTests
    {
        [Test]
        public void SlotAlternatives_StateOnlyWhatTheLedgerCanBackUp()
        {
            Assert.IsTrue(SlotCandidateRegression.Run(),
                "T05.6 备选时刻回归失败：看 Console 里「T05.6 备选时刻可检验」那一行的 ✗ 项。"
                + "重点看「篡改可检出」——它为 False 说明复检是空的，前面的通过不算证据。");
        }
    }
}
