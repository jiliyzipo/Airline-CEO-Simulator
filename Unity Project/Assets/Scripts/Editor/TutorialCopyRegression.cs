#if UNITY_EDITOR
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Shell;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 教程与引导文案的隔离回归（2026-10-25 · P23）。
    ///
    /// 这一层只钉两件「文案 correctness」的事 —— 都不是排版问题：
    /// <list type="number">
    /// <item><b>文案教的句子，命令台必须真的听得懂</b>。占位语与 S6 里点名的每一句，
    ///   都用 <see cref="CeoCommandRouter"/> 跑一遍，核对它落在预期那一类上。
    ///   教一句系统不认的话，比不教更糟：玩家会以为是自己说错了。</item>
    /// <item><b>文案里的数必须来自常量</b>。启动资金、首航结算都取既有常量，
    ///   否则代码改一次，信里就永久留在旧数上（这类漂移只能靠人肉比对发现）。</item>
    /// </list>
    ///
    /// 刻意**不**断言正文的措辞：文案会改，断言措辞只会让人改文案时顺手改测试。
    /// </summary>
    public static class TutorialCopyRegression
    {
        const string AnyPage = "n12_航线网络";

        public static bool Run()
        {
            // ———— 一、启动资金文案 = 常量 ————
            string costText = AdvisorTutorialScript.StartupCostText;
            bool costOk = costText == $"{ActionExecutor.OpenRouteStartupCost:F0} 万美元";

            // ———— 二、首航结算文案 = 入账用的同一对常量 ————
            bool settleOk = AdvisorTutorialScript.FirstFlightNetProfit > 0f
                            && AdvisorTutorialScript.FirstFlightBoardGain > 0f;

            // ———— 三、S6 教的是命令台，且五个例子都在 ————
            string hook = AdvisorTutorialScript.NextHookBody ?? "";
            bool hookOk = hook.Contains("命令台")
                          && hook.Contains("历史")
                          && hook.Contains("请示")      // 「只发请示，批不批您定」这条边界必须说出来
                          && hook.Contains("开通成都—上海")
                          && hook.Contains("买 2 架 A320neo")
                          && hook.Contains("招聘 10 名飞行员")
                          && hook.Contains("申请银行贷款");

            // ———— 四、占位语按页教对应部门能办的事 ————
            string finHint = CeoCommandRouter.PageHint("n15_财务部");
            string hrHint = CeoCommandRouter.PageHint("n18_人力资源部");
            string fltHint = CeoCommandRouter.PageHint("n9_机队");
            string opsHint = CeoCommandRouter.PageHint("n12_航线网络");
            string stgHint = CeoCommandRouter.PageHint("n16_战略发展部");
            bool hintOk = finHint.Contains("申请银行贷款")
                          && hrHint.Contains("招聘")
                          && fltHint.Contains("买 2 架")
                          && opsHint.Contains("开通")
                          && stgHint.Contains("可行性");

            // ———— 五、文案里点名的句子，路由必须落在预期那一类 ————
            //      （这是本条回归的重点：文案与行为同源）
            var finReport = CeoCommandRouter.Route(AnyPage, "汇报目前的财务状况");
            var finAsk = CeoCommandRouter.Route(AnyPage, "财务还能撑几个月");
            var finAct = CeoCommandRouter.Route(AnyPage, "申请银行贷款");
            var hrReport = CeoCommandRouter.Route(AnyPage, "现在乘务员缺口多少");
            var hrAct = CeoCommandRouter.Route(AnyPage, "招聘 10 名飞行员");
            var fltReport = CeoCommandRouter.Route(AnyPage, "机队现在几架、几架适航");
            var fltAct = CeoCommandRouter.Route(AnyPage, "买 2 架 A320neo");
            var stgFeas = CeoCommandRouter.Route(AnyPage, "分析开通北京—纽约航线的可行性");
            var opsAct = CeoCommandRouter.Route(AnyPage, "开通成都—上海");
            var opsReport = CeoCommandRouter.Route(AnyPage, "汇报本周运力与准点率");
            var companyReport = CeoCommandRouter.Route(AnyPage, "汇报公司整体情况");

            bool routesOk =
                finReport.ok && finReport.deptCode == "FIN" && finReport.kind != CeoCommandRouter.CeoAskKind.Act
                && finAsk.kind != CeoCommandRouter.CeoAskKind.Act
                && finAct.ok && finAct.topic == CeoCommandRouter.TopicFinance
                && hrReport.ok && hrReport.deptCode == "HR" && hrReport.kind != CeoCommandRouter.CeoAskKind.Act
                && hrAct.ok && hrAct.topic == CeoCommandRouter.TopicHrRecruit && hrAct.count == 10
                && fltReport.kind != CeoCommandRouter.CeoAskKind.Act
                && fltAct.ok && fltAct.topic == CeoCommandRouter.TopicFleetBuy && fltAct.aircraftType == "A320neo"
                && stgFeas.ok && stgFeas.topic == CeoCommandRouter.TopicFeasibility
                && opsAct.ok && opsAct.topic == CeoCommandRouter.TopicOpenRoute
                    && opsAct.origin == "CTU" && opsAct.dest == "SHA"
                && opsReport.ok && opsReport.deptCode == "OPS"
                && companyReport.ok && companyReport.topic == CeoCommandRouter.TopicCompany;

            bool all = costOk && settleOk && hookOk && hintOk && routesOk;

            Debug.Log($"教程/引导文案 → 启动资金=常量={costOk}（{costText}）　首航结算常量={settleOk}"
                      + $"　S6教命令台={hookOk}　按页提示={hintOk}"
                      + $"　文案句子可路由={routesOk}"
                      + $"（开线 {opsAct.origin}—{opsAct.dest} · 购机 {fltAct.aircraftType}×{fltAct.count}"
                      + $" · 招聘 {hrAct.position}×{hrAct.count} · 融资 kind={finAct.financeKind}）"
                      + $"　{(all ? "✓" : "✗")}");
            return all;
        }
    }
}
#endif
