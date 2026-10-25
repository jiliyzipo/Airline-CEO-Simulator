using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// K16 · 中国境内超短航段（&lt; 250 km）不得进种子网络。
    ///
    /// 与 <see cref="AirportCatalogEditModeTests"/> 分开的原因：那条钉的是机场**底表**三表一致，
    /// 这条钉的是**航线生成**口径 —— 判据写对而目的地池没接上，底表回归照样全绿。
    /// 期望值来自任务书 §18 K16（2026-10-23 裁定）：拦境内 250 km 以内、保留 LAX–SAN 等真实短途。
    /// </summary>
    public class ShortHaulEditModeTests
    {
        [Test]
        public void ChinaDomesticShortHaulIsNotGenerated()
        {
            Assert.IsTrue(
                ShortHaulRegression.Run(),
                "中国境内 250 km 以内的航段仍被生成为定期客运，或真实短途被误伤；"
                + "详细分支见 Console 里的「K16 中国境内超短航段」那一行。");
        }
    }
}
