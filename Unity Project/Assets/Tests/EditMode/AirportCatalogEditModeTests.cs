using AirlineCEO.EditorTools;
using NUnit.Framework;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 把「机场底表三表贯通」这条回归接进 NUnit。
    ///
    /// 为什么必须有这一层：`AirportCatalogRegression` 原先只挂在编辑器菜单
    /// `AirlineCEO/Run Logic Self Tests` 后面，而那条菜单**不是 NUnit 用例** ——
    /// `unity-editmode` profile 跑的 25 条 EditMode 用例里一条都不会执行它，
    /// 于是「批次 A/B 机场入表是否正确」在 CI/预检里从来没有真正被验证过，
    /// 只看得到「编译通过」。加上这一条之后：
    /// · 断言红 → 预检红，不会再有「发布出一个机场名解析不出来的航司」这种静默缺陷；
    /// · 采集到的数据（三表同集合、坐标可用、等级推导、≥245 km 无超短、搜索不串码）
    ///   都能在隔离快照里逐条复跑。
    /// </summary>
    public class AirportCatalogEditModeTests
    {
        [Test]
        public void TripleTablesStayInSync()
        {
            Assert.IsTrue(
                AirportCatalogRegression.Run(),
                "机场目录 / 城市经济 / 经纬度三表不一致，或批次 A/B 入表断言失败；"
                + "详细分支见 Console 里的「机场底表三表贯通」那一行。");
        }
    }
}
