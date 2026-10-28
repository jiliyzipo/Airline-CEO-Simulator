using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// K14：压测的「机场池」缝。
    ///
    /// `LogicSelfTests` 不在 `unity-editmode` profile 里，只有选司网络被这个可选参数改坏，
    /// 预检仍会全绿 —— 与 K19 同理，把回归接进 NUnit 才算数。
    /// </summary>
    public class NetworkPoolVisibilityEditModeTests
    {
        [Test]
        public void NetworkPoolSeamIsVisibleAndInert()
        {
            Assert.IsTrue(
                NetworkPoolVisibilityRegression.Run(),
                "`TakeoverWorldBuilder.BuildPreview(id, universe)` 要么改动了默认（全目录）网络，"
                + "要么收窄后仍生成被移除城市的航线 —— 详见 Console 里的「K14 网络池可见性」那一行。");
        }
    }
}
