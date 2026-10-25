using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// K17 / D4：接管成本继承粒度。
    ///
    /// `LogicSelfTests` 那条菜单不在 `unity-editmode` profile 里，只放在那里等于没做 ——
    /// 部门预算摊错、`Apply` 没接上账本，预检照样全绿。这里直接跑
    /// <see cref="TakeoverCostLedgerRegression.Run"/>，覆盖「预览四段式 ↔ 部门账本 ↔ 落盘预算 ↔ 季度问责」整条链。
    /// </summary>
    public class TakeoverCostLedgerEditModeTests
    {
        [Test]
        public void TakeoverCostInheritsPerDepartment()
        {
            Assert.IsTrue(
                TakeoverCostLedgerRegression.Run(),
                "接管成本的四段式与部门账本不同账，或跑道路标发生漂移（K10 的 8/6/4/3 不得变），"
                + "或落盘预算没按继承结构摊分 / 额度合计不等于董事会规则，"
                + "或季度问责分摊与部门额度不同尺；详见 Console 里的「K17 接管成本继承粒度」那一行。");
        }
    }
}
