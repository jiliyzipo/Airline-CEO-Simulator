#if UNITY_EDITOR
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 命令台四类**写指令**的隔离回归（2026-10-25 · P22）。
    ///
    /// 四类指令（开线 / 购机 / 招聘 / 融资）共同的红线只有两条，这里逐条钉死：
    /// <list type="number">
    /// <item><b>只走既有呈报管线</b>：产物必须是 <c>InboxManager</c> 那四个既有方法发出来的标准请示件，
    ///   不是命令台自己另写的第三套执行路径。断言方式 = 收件箱里那封信的 <c>action</c> 字段，
    ///   而不是「没报错」——「没报错」在什么都没发生时也成立。</item>
    /// <item><b>读类不许被写成写类</b>：「汇报融资情况」「招聘进度如何」「贷款还剩多少」都是问情况，
    ///   一旦被认成 Act，CEO 就会收到一封自己没下过的请示。这条比少认一句写指令严重得多，
    ///   所以反例比正例还多。</item>
    /// </list>
    ///
    /// 隔离手段与 <see cref="CeoCommandRegression"/> 相同：内存 GameState + 静态函数，不起壳层。
    /// </summary>
    public static class WriteCommandRegression
    {
        public static bool Run()
        {
            // ———— 一、路由：四类写指令（纯函数，不碰 GameState） ————

            var open = CeoCommandRouter.Route("n12_航线网络", "开通北京—纽约航线");
            bool openOk = open.ok && open.kind == CeoCommandRouter.CeoAskKind.Act
                          && open.topic == CeoCommandRouter.TopicOpenRoute
                          && open.origin == "PEK" && open.dest == "JFK";

            // 缺一端机场：不装作受理，只给引导（少一个机场就开航 = 猜一条不存在的航线）
            var openBad = CeoCommandRouter.Route("n12_航线网络", "开通一条新航线");
            bool openBadOk = !openBad.ok && openBad.topic == CeoCommandRouter.TopicOpenRoute
                             && !string.IsNullOrEmpty(openBad.note);

            // 「分析开通…的可行性」仍是战略部的分析件，不能被开航令抢走
            var openAnalyze = CeoCommandRouter.Route("n12_航线网络", "分析开通北京—纽约航线的可行性");
            bool openAnalyzeOk = openAnalyze.ok
                                 && openAnalyze.kind == CeoCommandRouter.CeoAskKind.Analyze
                                 && openAnalyze.topic == CeoCommandRouter.TopicFeasibility;

            var fleet = CeoCommandRouter.Route("n9_机队", "买 2 架 A320neo");
            bool fleetOk = fleet.ok && fleet.kind == CeoCommandRouter.CeoAskKind.Act
                           && fleet.topic == CeoCommandRouter.TopicFleetBuy
                           && fleet.aircraftType == "A320neo" && fleet.count == 2
                           && fleet.source == "自有资金买断";

            // 机型必须**最长命中**：「B737-8」（MAX 8）是「B737-800」的前缀，
            // 取第一个命中会把 737-800 的订单记到 MAX 8 上。
            var fleetLong = CeoCommandRouter.Route("n9_机队", "引进两架B737-800，融资租赁");
            bool fleetLongOk = fleetLong.ok && fleetLong.aircraftType == "B737-800"
                               && fleetLong.count == 2 && fleetLong.source == "融资租赁";

            var fleetLease = CeoCommandRouter.Route("n9_机队", "采购 3 架 A321neo 经营租赁");
            bool fleetLeaseOk = fleetLease.ok && fleetLease.aircraftType == "A321neo"
                                && fleetLease.count == 3 && fleetLease.source == "经营租赁";

            // 没写架数：不下请示（1 架和 10 架是两笔完全不同的钱）
            var fleetNoCount = CeoCommandRouter.Route("n9_机队", "买A320neo");
            bool fleetNoCountOk = !fleetNoCount.ok && fleetNoCount.topic == CeoCommandRouter.TopicFleetBuy
                                  && fleetNoCount.aircraftType == "A320neo"
                                  && !string.IsNullOrEmpty(fleetNoCount.note);

            var hr = CeoCommandRouter.Route("n18_人力资源部", "招聘 10 名飞行员");
            bool hrOk = hr.ok && hr.kind == CeoCommandRouter.CeoAskKind.Act
                        && hr.topic == CeoCommandRouter.TopicHrRecruit
                        && hr.position == "飞行员" && hr.count == 10
                        && hr.channel == 0 && hr.urgency == 2;

            var hrLoose = CeoCommandRouter.Route("n18_人力资源部", "招五名乘务员，猎头挖角");
            bool hrLooseOk = hrLoose.ok && hrLoose.position == "乘务员" && hrLoose.count == 5
                             && hrLoose.channel == 1;

            var hrUrgent = CeoCommandRouter.Route("n18_人力资源部", "紧急招聘 20 名机务工程师");
            bool hrUrgentOk = hrUrgent.ok && hrUrgent.position == "机务工程师" && hrUrgent.count == 20
                              && hrUrgent.urgency == 3;

            var hrNoCount = CeoCommandRouter.Route("n18_人力资源部", "招聘飞行员");
            bool hrNoCountOk = !hrNoCount.ok && hrNoCount.topic == CeoCommandRouter.TopicHrRecruit
                               && !string.IsNullOrEmpty(hrNoCount.note);

            var loan = CeoCommandRouter.Route("n15_财务部", "申请银行贷款");
            bool loanOk = loan.ok && loan.topic == CeoCommandRouter.TopicFinance && loan.financeKind == 1;

            var bridge = CeoCommandRouter.Route("n15_财务部", "办理高息过桥");
            bool bridgeOk = bridge.ok && bridge.financeKind == 2;

            var equity = CeoCommandRouter.Route("n15_财务部", "申请股东增资");
            bool equityOk = equity.ok && equity.financeKind == 3;

            // ———— 二、读类绝不能被写成写类 ————
            var finReport = CeoCommandRouter.Route("n15_财务部", "汇报融资情况");
            bool finReportOk = finReport.kind != CeoCommandRouter.CeoAskKind.Act && finReport.deptCode == "FIN";

            var finLeft = CeoCommandRouter.Route("n15_财务部", "贷款还剩多少");
            bool finLeftOk = finLeft.kind != CeoCommandRouter.CeoAskKind.Act;

            var hrProgress = CeoCommandRouter.Route("n18_人力资源部", "招聘进度如何");
            bool hrProgressOk = hrProgress.kind != CeoCommandRouter.CeoAskKind.Act;

            var fleetCount = CeoCommandRouter.Route("n9_机队", "机队现在几架");
            bool fleetCountOk = fleetCount.kind != CeoCommandRouter.CeoAskKind.Act;

            var whyBuy = CeoCommandRouter.Route("n9_机队", "为什么买飞机这么贵");
            bool whyBuyOk = whyBuy.kind != CeoCommandRouter.CeoAskKind.Act;

            // ———— 三、投递：必须落在既有管线的那封请示件上 ————
            bool fleetRun = false, fleetDedupe = false, hrRun = false, finRun = false, openRun = false;
            string openReceipt = "";

            var go = new GameObject("__write_command_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1; gs.quarter = 1;
                gs.cash = 900f;
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.activeRoutes = new List<RouteData>();
                gs.fleet = new List<Aircraft>();

                string rawFleet = "买 2 架 A320neo";
                var fleetAns = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n9_机队", rawFleet), rawFleet);
                bool fleetNeedsRun = fleetAns.ok && fleetAns.needsRun;
                bool fleetDone = fleetNeedsRun && DeptReportLogic.RunPending(gs, fleetAns, rawFleet, out string _);
                var fleetMail = FindByAction(gs, InboxManager.FleetBuyAction);
                fleetRun = fleetDone && fleetMail != null
                           && fleetMail.dest == "A320neo" && fleetMail.origin == "2"
                           && fleetMail.stopover == "自有资金买断";

                int afterFleet = gs.inbox.Count;
                bool fleetAgain = DeptReportLogic.RunPending(gs, fleetAns, rawFleet, out string _);
                fleetDedupe = !fleetAgain && gs.inbox.Count == afterFleet;

                string rawHr = "招聘 10 名飞行员";
                var hrAns = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n18_人力资源部", rawHr), rawHr);
                bool hrDone = hrAns.ok && hrAns.needsRun
                              && DeptReportLogic.RunPending(gs, hrAns, rawHr, out string _);
                var hrMail = FindByAction(gs, InboxManager.HrRecruitAction);
                hrRun = hrDone && hrMail != null && hrMail.dest == "飞行员";

                string rawFin = "申请银行贷款";
                var finAns = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n15_财务部", rawFin), rawFin);
                bool finDone = finAns.ok && finAns.needsRun
                               && DeptReportLogic.RunPending(gs, finAns, rawFin, out string _);
                var finMail = FindByAction(gs, InboxManager.FinanceLoanAction);
                finRun = finDone && finMail != null && finMail.dest == "1";

                // 开航研究会按工作日延迟投递 → 落在 pendingInbox（到点才进收件箱），
                // 所以这里断言的是「已排期」而不是「已在收件箱」。
                string rawOpen = "开通北京—纽约航线";
                var openAns = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n12_航线网络", rawOpen), rawOpen);
                bool openDone = openAns.ok && openAns.needsRun
                                && DeptReportLogic.RunPending(gs, openAns, rawOpen, out openReceipt);
                var openMail = FindPendingByAction(gs, "open_route");
                openRun = openDone && openMail != null
                          && !string.IsNullOrEmpty(openMail.subject) && openMail.subject.Contains("开通")
                          && openReceipt.Contains("开航研究会");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }

            bool all = openOk && openBadOk && openAnalyzeOk
                       && fleetOk && fleetLongOk && fleetLeaseOk && fleetNoCountOk
                       && hrOk && hrLooseOk && hrUrgentOk && hrNoCountOk
                       && loanOk && bridgeOk && equityOk
                       && finReportOk && finLeftOk && hrProgressOk && fleetCountOk && whyBuyOk
                       && fleetRun && fleetDedupe && hrRun && finRun && openRun;

            Debug.Log($"命令台写指令 → 开线={openOk}（{open.origin}—{open.dest}）　缺机场退回引导={openBadOk}"
                      + $"　分析件不被抢={openAnalyzeOk}"
                      + $"　购机={fleetOk}（{fleet.aircraftType}×{fleet.count}·{fleet.source}）"
                      + $"　最长机型={fleetLongOk}（{fleetLong.aircraftType}×{fleetLong.count}·{fleetLong.source}）"
                      + $"　经营租赁={fleetLeaseOk}　缺架数退回引导={fleetNoCountOk}"
                      + $"　招聘={hrOk}（{hr.position}×{hr.count}·渠道{hr.channel}·急{hr.urgency}）"
                      + $"　口语人数/渠道={hrLooseOk}（{hrLoose.count}·渠道{hrLoose.channel}）"
                      + $"　紧迫度={hrUrgentOk}　缺人数退回引导={hrNoCountOk}"
                      + $"　融资=贷款{loanOk}/过桥{bridgeOk}/增资{equityOk}"
                      + $"　读类不写成类=融资汇报{finReportOk}/余额{finLeftOk}/招聘进度{hrProgressOk}/机队规模{fleetCountOk}/为什么{whyBuyOk}"
                      + $"　投递=购机{fleetRun}/去重{fleetDedupe}/招聘{hrRun}/融资{finRun}/开线{openRun}"
                      + $"　{(all ? "✓" : "✗")}");
            return all;
        }

        static InboxMail FindByAction(GameState gs, string action)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m != null && m.action == action) return m;
            }
            return null;
        }

        static InboxMail FindPendingByAction(GameState gs, string action)
        {
            if (gs?.pendingInbox == null) return null;
            for (int i = 0; i < gs.pendingInbox.Count; i++)
            {
                var p = gs.pendingInbox[i];
                if (p?.mail != null && p.mail.action == action) return p.mail;
            }
            return null;
        }
    }
}
#endif
