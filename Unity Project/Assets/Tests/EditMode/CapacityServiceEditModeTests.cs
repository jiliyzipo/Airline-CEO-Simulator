using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 批次一（M1 机场索引 + C0 容量服务抽取）的 NUnit 入口。
    ///
    /// 为什么必须单独有一条：`CapacityServiceRegression` 只挂在编辑器菜单与
    /// 触发桥 whitelist 后面时，`unity-editmode` profile 跑的用例里**一条都不会执行它**，
    /// 于是「抽取没改变容量」这件事在预检里只看得到「编译通过」。接进 NUnit 后：
    /// · 19,968 格任一格与抽取前不同 → 红；
    /// · 四段池 / 小时 / 日容量 / 方向池任一派生接口漂移 → 红；
    /// · `FindByCode` 与线性扫描在大小写、空串、未知码上任一不一致 → 红。
    ///
    /// 边界：这是 **C# 判据层**的等价回归，不是完整经营链路的 A/B 对照。
    /// </summary>
    public class CapacityServiceEditModeTests
    {
        [Test]
        public void ExtractionPreservesEveryCapacityCell()
        {
            Assert.IsTrue(
                CapacityServiceRegression.Run(),
                "容量服务抽取改变了容量数字，或机场索引与线性扫描语义不一致；"
                + "详细分支见 Console 里的「容量服务 / 机场索引（批次一）」那一行。");
        }
    }
}
