using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 开航研究会纪要「只有一个作者」的 NUnit 入口（S12-AC5）。
    ///
    /// 断言见 <see cref="OpenRouteStudyRegression"/>：不成立的机场对在算日利之前被否决、
    /// 同一输入产出逐字节相同的纪要、UI 侧不再有第二个正文组装函数。
    /// </summary>
    public class OpenRouteStudyTests
    {
        [Test]
        public void OpenRouteMemo_HasSingleSourceAndRejectsBeforeProfit()
        {
            Assert.IsTrue(OpenRouteStudyRegression.Run(),
                "开航纪要回归失败：看 Console 里「开航纪要单一出处」那一行的 ✗ 项。"
                + "常见原因：①否决判据被挪到日利计算之后（会产出一封推销不该存在的航线的信）；"
                + "②流程类上又加了正文/路径组装函数（纪要重新出现两个作者）；"
                + "③距离改成了直飞与经停两套算法。");
        }
    }
}
