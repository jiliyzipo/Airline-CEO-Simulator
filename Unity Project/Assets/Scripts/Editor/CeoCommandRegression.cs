#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 底栏「CEO 命令台」的分派与答复口径回归（2026-10-20）。
    ///
    /// 这一层最容易出的错不是排版，而是**派错部门**与**把汇报当成了执行**。所以这里只钉三件事：
    /// · 句子里的部门优先于当前页面（站在人事页问财务，必须财务部答）；
    /// · 「取消某某航线」必须被认成执行类并明确回绝，不能落进「运营部汇报航线」；
    /// · 同一句话问两遍只更新同一封函，不刷屏。
    ///
    /// 与 <see cref="RpmPreviewRegression"/> 一样是隔离回归：只碰静态函数和内存 GameState。
    /// </summary>
    public static class CeoCommandRegression
    {
        public static void Run()
        {
            // ① 页面 → 默认部门
            bool pageMap =
                CeoCommandRouter.PageDept("n15_财务部") == "FIN"
                && CeoCommandRouter.PageDept("n18_人力资源部") == "HR"
                && CeoCommandRouter.PageDept("n9_机队") == CeoCommandRouter.DeptFleet
                && CeoCommandRouter.PageDept("n12_航线网络") == "OPS"
                && CeoCommandRouter.PageDept("n16_战略发展部") == "STG";

            // ② 可行性：识别 + 两端机场解析（北京 = 北京首都，纽约 = 纽约肯尼迪）
            var feas = CeoCommandRouter.Route("n12_航线网络", "分析开通北京--纽约航线的可行性");
            bool feasOk = feas.ok && feas.topic == CeoCommandRouter.TopicFeasibility
                          && feas.kind == CeoCommandRouter.CeoAskKind.Analyze
                          && feas.deptCode == "STG"
                          && feas.origin == "PEK" && feas.dest == "JFK";

            // ③ 缺机场不猜：只给引导，不派件
            var feasBad = CeoCommandRouter.Route("n12_航线网络", "分析开通某条航线的可行性");
            bool feasBadOk = !feasBad.ok && feasBad.deptCode == "STG";

            // ④ 人力 / 财务：句子里的部门优先于页面
            var hr = CeoCommandRouter.Route("n12_航线网络", "现在乘务员缺口数量");
            var fin = CeoCommandRouter.Route("n18_人力资源部", "汇报目前的财务状况");
            bool deptFirst = hr.ok && hr.deptCode == "HR" && fin.ok && fin.deptCode == "FIN";

            // ⑤ 关线：必须认成 Act，且两端机场要解析出来（大兴 ≠ 首都）
            var close = CeoCommandRouter.Route("n12_航线网络", "取消北京大兴--上海虹桥的航线");
            bool closeOk = close.ok && close.kind == CeoCommandRouter.CeoAskKind.Act
                           && close.topic == CeoCommandRouter.TopicCloseRoute
                           && close.origin == "PKX" && close.dest == "SHA";

            // ⑤b 目的地不在机场目录里（佛山沙堤）：仍要认成 Act，并把 CEO 原话记下来。
            //     这里最容易犯的错是「一侧解析失败 → 整句当没听懂」，于是 CEO 以为系统没收到。
            //     ★ 2026-10-21：原夹具用的是「珠海金湾」，但 ZUH 已入目录，那条反例变成了正例；
            //       必须换成**仍未收录**的机场，否则这条断言在悄悄失去意义。
            var closeUnknown = CeoCommandRouter.Route("n12_航线网络", "取消北京大兴--佛山沙堤的航线");
            bool closeUnknownOk = closeUnknown.ok && closeUnknown.kind == CeoCommandRouter.CeoAskKind.Act
                                  && closeUnknown.origin == "PKX" && closeUnknown.dest == null
                                  && closeUnknown.originText == "北京大兴"
                                  && closeUnknown.destText == "佛山沙堤";

            // ⑥ 看不懂的句子不派件
            var gibberish = CeoCommandRouter.Route("n12_航线网络", "asdfgh");
            bool gibberishOk = !gibberish.ok;

            var go = new GameObject("__ceo_command_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1; gs.quarter = 1;
                gs.cash = 900f;
                gs.inbox = new List<InboxMail>();
                gs.activeRoutes = new List<RouteData>();
                gs.fleet = new List<Aircraft>();
                gs.fleet.Add(new Aircraft { registration = "B-1001", type = "A320neo", status = AircraftStatuses.Airworthy });

                // ⑦ 部门答复：四类都要出信，且正文是公函（有称谓、有落款）
                var finAns = DeptReportLogic.Build(gs, fin);
                bool finBody = finAns.ok && finAns.body.Contains("尊敬的")
                               && finAns.body.Contains("财务部") && !string.IsNullOrEmpty(finAns.oneLine);

                var hrAns = DeptReportLogic.Build(gs, hr);
                bool hrBody = hrAns.ok && hrAns.body.Contains("乘务员") && hrAns.subject.Contains("缺口");

                var feasAns = DeptReportLogic.Build(gs, feas);
                bool feasBody = feasAns.ok && feasAns.body.Contains("日利润")
                                && feasAns.body.Contains("可行性")       // 自我介绍里的「事由」要读得通
                                && feasAns.subject.Contains("研究");

                // ⑦b 回执与正文同源：日利润必须是同一个字面量，结论也不能一个说「建议推进」、
                //     正文却因时刻槽不足说「需先申请」。
                string bodyProfit = ValueAfter(feasAns.body, "日利润");
                bool sameProfit = !string.IsNullOrEmpty(bodyProfit) && feasAns.oneLine.Contains(bodyProfit);
                bool sameVerdict = feasAns.body.Contains("建议推进：") == feasAns.oneLine.Contains("建议推进");

                // ⑦c 开航前置必须**按办理顺序**报：国际线的第一道闸是航权，不是时刻槽。
                //     旧版只列时刻不足，等于把最慢最贵的那道闸藏起来（CEO 会先去买时刻）。
                int atRight = feasAns.body.IndexOf("航权", StringComparison.Ordinal);
                int atSlot = feasAns.body.IndexOf("时刻槽", StringComparison.Ordinal);
                bool gateOrder = atRight >= 0 && atSlot > atRight
                                 && feasAns.body.Contains("未持有")
                                 && feasAns.oneLine.Contains("先办航权");
                bool noSlotFirst = !feasAns.oneLine.Contains("先办时刻");

                // ⑦d 国际/地区判定：底表国家优先，坐标矩形只兜底。
                //     `OutOfChina` 的矩形把新加坡/曼谷/东京/首尔都算作境内，
                //     于是 PEK—SIN 被写成「国内航线」，航权与资质两行整段消失。
                bool intlPin = RoutePermitLogic.IsInternational("PEK", "SIN")
                               && RoutePermitLogic.IsInternational("PEK", "NRT")
                               && RoutePermitLogic.IsInternational("PEK", "ICN")
                               && RoutePermitLogic.IsInternational("PEK", "HKG")
                               && !RoutePermitLogic.IsInternational("PEK", "SHA")
                               && !RoutePermitLogic.IsInternational("PEK", "URC");

                // ⑦e 同一句话写成「可能性」也要走可行性分析（CEO 的措辞不按词表走）
                var feasAlt = CeoCommandRouter.Route("n12_航线网络", "分析开通北京—新加坡航线的可能性");
                bool feasAltOk = feasAlt.ok && feasAlt.topic == CeoCommandRouter.TopicFeasibility
                                 && feasAlt.origin == "PEK" && feasAlt.dest == "SIN";

                // ⑦f 正文装饰不得把机场名印两遍（旧版把三字码展成完整标签：
                //     「北京首都 PEK」→「北京首都 北京首都 PEK」）
                string deco = TermNavMap.Decorate("线路　北京首都 PEK → 新加坡樟宜 SIN");
                string decoVisible = System.Text.RegularExpressions.Regex.Replace(deco, "<[^>]+>", "");
                bool decoOk = decoVisible.Contains("北京首都 PEK")
                              && !decoVisible.Contains("北京首都 北京首都")
                              && decoVisible.Contains("新加坡樟宜 SIN")
                              && !decoVisible.Contains("新加坡樟宜 新加坡樟宜");

                // ⑦g 国内线的正文不得出现「先拿下航权」：同一封正文上方刚写过「国内航线不涉航权」，
                //     旧文案对所有航线都印「先拿下航权与许可」，于是同一封信自相矛盾（2026-10-21 截图报障）。
                string hubBefore = gs.hubIata;
                gs.hubIata = "PEK";                       // 基地始发 → NeedsSlot=true，逼出时刻槽不足分支
                var feasDom = CeoCommandRouter.Route("n12_航线网络", "分析开通北京首都--珠海金湾航线的可行性");
                var feasDomAns = DeptReportLogic.Build(gs, feasDom);
                gs.hubIata = hubBefore;
                bool domNoRightWord = feasDom.ok && feasDomAns.ok
                                      && feasDomAns.body.Contains("国内航线不涉航权")
                                      && !feasDomAns.body.Contains("先拿下航权")
                                      && !feasDomAns.oneLine.Contains("先办航权");

                // ⑧ 关线第一步：确认卡。必须**不投递、不动账**，只把代价列清。
                //     旧版在这里出复核函并写「命令台只读」；从 P2 起关线可执行，
                //     但执行前必须过一道显式确认 —— 一句话认错机场就是一条航线没了。
                gs.activeRoutes.Add(new RouteData
                {
                    routeId = "PKX-SHA-1", origin = "PKX", destination = "SHA",
                    dailyProfit = 4.2f, flightNumber = "NV1001",
                    registration = "B-1001", aircraftType = "A320neo"
                });
                StrategyManager.EnsureState(gs);
                gs.stg.slots.Add(new SlotHolding
                {
                    airport = "PKX", slotTime = "08:00", inUse = true, usedByRouteId = "PKX-SHA-1"
                });
                gs.flightRings.Add(new FlightRing
                {
                    ringId = "R-PKX-SHA", registration = "B-1001",
                    legARouteId = "PKX-SHA-1", legBRouteId = "PKX-SHA-1"
                });
                gs.routeSubsidies.Add(new RouteSubsidy
                {
                    routeId = "PKX-SHA-1", annualAmountWan = 800f, yearsRemaining = 3
                });

                int inboxBeforeConfirm = gs.inbox.Count;
                var confirm = DeptReportLogic.Build(gs, close);
                bool confirmOk = confirm.ok && confirm.needsConfirm
                                 && confirm.routeKey == "PKX-SHA-1"
                                 && string.IsNullOrEmpty(confirm.body)          // 没有信，只有卡
                                 && confirm.confirmTitle.Contains("PKX")
                                 && confirm.confirmBody.Contains("4.2")
                                 && confirm.confirmBody.Contains("时刻槽 1 个")
                                 && confirm.confirmBody.Contains("航班环 1 个")
                                 && confirm.confirmBody.Contains("800 万/年")
                                 && confirm.confirmBody.Contains("不可自动恢复")
                                 && gs.inbox.Count == inboxBeforeConfirm
                                 && gs.activeRoutes.Count == 1;                 // 台账一动没动

                // ⑧b 目录外目的地：没有可执行对象 → 仍出复核函把原话念回去（不能静默）
                var closeUnknownAns = DeptReportLogic.Build(gs, closeUnknown);
                bool closeUnknownBody = closeUnknownAns.ok && !closeUnknownAns.needsConfirm
                                        && closeUnknownAns.body.Contains("佛山沙堤")
                                        && closeUnknownAns.subject.Contains("未改动台账");

                // ⑧c 点「确认停飞」：一次办完退槽 / 撤环 / 终止补贴 / 摘航线，并出两封公函
                bool execOk = DeptReportLogic.ExecuteClose(gs, confirm.routeKey,
                    "取消北京大兴--上海虹桥的航线", out var opsLetter, out var stgLetter, out string execMsg);
                bool execLedger = execOk
                                  && gs.activeRoutes.Count == 0
                                  && StrategyManager.EnsureState(gs).slots[0].inUse == false
                                  && string.IsNullOrEmpty(gs.stg.slots[0].usedByRouteId)
                                  && gs.flightRings.Count == 0
                                  && gs.routeSubsidies.Count == 0
                                  && execMsg.Contains("退还时刻槽 1 个")
                                  && execMsg.Contains("终止补贴 800 万/年");
                bool execLetters = opsLetter.ok && stgLetter.ok
                                   && opsLetter.subject.Contains("已停飞")
                                   && opsLetter.body.Contains("1 → 0 条")
                                   && stgLetter.subject.Contains("网络影响")
                                   && stgLetter.body.Contains("主动让出");

                // ⑧d 重复确认：航线没了就作废，不能再删一次（旧快照不能当命令用）
                bool execOnce = !DeptReportLogic.ExecuteClose(gs, confirm.routeKey,
                                    "取消北京大兴--上海虹桥的航线", out var opsAgain, out _, out string msgAgain)
                                && opsAgain.ok && msgAgain.Contains("不在运营");

                // ⑧e 同一城对多条在营航线：不猜，列候选并要求指明班次（猜错就是关掉另一条）
                gs.activeRoutes.Add(new RouteData { routeId = "PKX-SHA-2", origin = "PKX", destination = "SHA", dailyProfit = 3.1f, flightNumber = "NV1002", depTime = "18:00" });
                gs.activeRoutes.Add(new RouteData { routeId = "PKX-SHA-3", origin = "PKX", destination = "SHA", dailyProfit = 2.5f, flightNumber = "NV1003", depTime = "21:30" });
                var dupAns = DeptReportLogic.Build(gs, close);
                bool dupOk = dupAns.ok && !dupAns.needsConfirm
                             && dupAns.subject.Contains("多条在营航线")
                             && dupAns.body.Contains("NV1002") && dupAns.body.Contains("NV1003")
                             && gs.activeRoutes.Count == 2;
                gs.activeRoutes.Clear();

                // ⑧f 不带「航线」二字的关线 —— 2026-10-22 报障原话「取消北京—上海」。
                //     旧判据要求句中出现「航线 / 航」，可 CEO 最自然的说法根本不带；
                //     这句话于是掉进「本线问句」（OpsQueryLogic 把「取消」列为停飞话题），
                //     回函写着「未改动本线运力」，没有确认卡 —— 看起来受理了，实际一条线没动。
                //     新判据：命中关线动词 + 真的切出可解析城对，就按关线办。
                gs.activeRoutes.Add(new RouteData
                {
                    routeId = "PEK-SHA-1", origin = "PEK", destination = "SHA",
                    dailyProfit = 3.3f, flightNumber = "NV1010", aircraftType = "A320neo"
                });
                var bareCloseReq = CeoCommandRouter.Route("n12_航线网络", "取消北京--上海");
                var bareCloseAns = DeptReportLogic.Build(gs, bareCloseReq);
                bool bareCloseOk = bareCloseReq.ok
                                   && bareCloseReq.kind == CeoCommandRouter.CeoAskKind.Act
                                   && bareCloseReq.topic == CeoCommandRouter.TopicCloseRoute
                                   && bareCloseReq.origin == "PEK" && bareCloseReq.dest == "SHA"
                                   && bareCloseAns.ok && bareCloseAns.needsConfirm
                                   && bareCloseAns.routeKey == "PEK-SHA-1"
                                   && string.IsNullOrEmpty(bareCloseAns.body)   // 只有卡，没有信
                                   && gs.activeRoutes.Count == 1;          // 台账一动没动

                // ⑧g 反向三种：带班次/班期的是改运力，带疑问的是提问 —— 都不许被认成关线。
                //     认错一边的代价不对等：误判成关线 = 整条线没了；误判成问句 = 一条线没动。
                var bareFreq = CeoCommandRouter.Route("n12_航线网络", "取消北京--上海每天 2 班");
                bool bareFreqOk = bareFreq.ok && bareFreq.topic != CeoCommandRouter.TopicCloseRoute
                                  && bareFreq.kind != CeoCommandRouter.CeoAskKind.Act;
                var bareWhy = CeoCommandRouter.Route("n12_航线网络", "为什么北京--上海停飞了");
                bool bareWhyOk = bareWhy.ok && bareWhy.topic != CeoCommandRouter.TopicCloseRoute
                                 && bareWhy.kind != CeoCommandRouter.CeoAskKind.Act;
                gs.activeRoutes.Clear();

                // ⑨ 投递去重：同一句话问两遍 → 收件箱只有一封
                InboxManager.QueueDeptAnswer(gs, finAns, "汇报目前的财务状况", out _);
                int afterFirst = gs.inbox.Count;
                InboxManager.QueueDeptAnswer(gs, finAns, "汇报目前的财务状况", out _);
                int afterSecond = gs.inbox.Count;
                bool dedupe = afterFirst == 1 && afterSecond == 1;

                // ⑩ 汇报件不是待办：不该给 CEO 再压一个批复按钮
                bool noDecision = gs.inbox.Count > 0 && !InboxManager.RequiresDecision(gs.inbox[0])
                                  && gs.inbox[0].status == "approved";

                // ⑪ 运力调令（P2）：命令台只解析 + 调起既有管线，产物是「呈报件」。
                //    这里钉三件事：能不能落账、读不懂时会不会照样办、问句会不会被当调令。
                var capRoute = new RouteData
                {
                    routeId = "R-CAP-1", origin = "PEK", destination = "SHA", distanceKm = 1100f,
                    aircraftType = "A320neo", flightNumber = "NV2001", registration = "B-3001",
                    depTime = "08:00", arrTime = "10:20", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 12f, dailyProfit = 12f, groundStaffAssigned = 2
                };
                capRoute.SyncFlightsFromPrimary();
                gs.activeRoutes.Add(capRoute);
                gs.fleet.Add(new Aircraft { registration = "B-3001", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 4 });
                gs.fleet.Add(new Aircraft { registration = "B-3002", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 6 });
                gs.fleet.Add(new Aircraft { registration = "B-3003", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy, age = 8 });

                // ⑪a 认线：调令必须落到 needsRun（不弹确认卡 —— 呈报件本身就是那道确认）
                var capAsk = CeoCommandRouter.Route("n12_航线网络", "把北京--上海加密到每天 2 班");
                var capAns = DeptReportLogic.Build(gs, capAsk, "把北京--上海加密到每天 2 班");
                bool capRouteOk = capAns.ok && capAns.needsRun && !capAns.needsConfirm
                                  && capAns.routeKey == "R-CAP-1";

                // ⑪b 真下达：管线落账（directivePending = 呈报待批），回执指向收件箱
                bool capRan = DeptReportLogic.RunCapacityDirective(gs, "R-CAP-1",
                    "把北京--上海加密到每天 2 班", out string capReceipt);
                bool capLedger = capRan && capRoute.directivePending
                                 && capReceipt.Contains("收件箱");

                // ⑪c 读不懂幅度就不办：「加密」只有方向没有量，系统不得自己定幅度
                int wavesAfterCap = capRoute.FlightCount;
                bool capNoGuess = !DeptReportLogic.RunCapacityDirective(gs, "R-CAP-1",
                                      "把北京--上海加密", out string guessReceipt)
                                  && guessReceipt.Contains("未执行")
                                  && capRoute.FlightCount == wavesAfterCap;

                // ⑪d 问句不是调令：「为什么…不排班」要转答复件，班次一动不能动
                bool capQuery = DeptReportLogic.RunCapacityDirective(gs, "R-CAP-1",
                                    "为什么北京--上海周三不排班", out string queryReceipt)
                                && queryReceipt.Contains("询问")
                                && capRoute.FlightCount == wavesAfterCap;

                // ⑪e 纯改班期：读得到班期、读不到增减 → 不猜，指回周计划页
                bool capDaysOnly = !DeptReportLogic.RunCapacityDirective(gs, "R-CAP-1",
                                       "把北京--上海改成每周一三五执飞", out string daysReceipt)
                                   && daysReceipt.Contains("周计划")
                                   && capRoute.FlightCount == wavesAfterCap;

                // ⑪f 同城对多班：不猜，列出航班号
                gs.activeRoutes.Add(new RouteData
                {
                    routeId = "R-CAP-2", origin = "PEK", destination = "SHA",
                    flightNumber = "NV2002", depTime = "18:00", dailyProfit = 3f
                });
                var capDupText = "把北京--上海加密到每天 3 班";
                var capDupAns = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n12_航线网络", capDupText), capDupText);
                bool capDup = !capDupAns.ok && !capDupAns.needsRun
                              && capDupAns.note.Contains("NV2001") && capDupAns.note.Contains("NV2002");
                gs.activeRoutes.RemoveAt(gs.activeRoutes.Count - 1);

                // ⑪g 航线已不在：确认/指令一律作废，不能拿旧 routeKey 去改别的线
                bool capGone = !DeptReportLogic.RunCapacityDirective(gs, "R-CAP-GONE",
                                   "加密到每天 2 班", out string goneReceipt)
                               && goneReceipt.Contains("不在运营");

                // ⑫ 单航线询问（P2）：问句必须按**本线台账**答，不能落进「运营部总账」。
                //    总账里只有「航线 8 条 / 周 84 班 / 亏损线 2 条」，答不到「周三为什么不排」。
                string qRaw = "为什么北京--上海周三不排班";
                var qAsk = CeoCommandRouter.Route("n12_航线网络", qRaw);
                var qAns = DeptReportLogic.Build(gs, qAsk, qRaw);
                bool qRouteOk = qAsk.topic == CeoCommandRouter.TopicRouteQuery
                                && qAns.ok && qAns.needsQuery && !qAns.needsRun && !qAns.needsConfirm
                                && qAns.routeKey == "R-CAP-1";

                // ★ 这里必须换一句**没问过**的话：⑪d 已经用 qRaw 走过一次询问，
                //   答复件 id = QueryMailId(routeId, 原话)，同一句再问只会更新那封、入箱 +0。
                //   拿 qRaw 验「会新投一封」测到的是去重行为，不是投递行为（2026-10-21 实测 False）。
                string letterRaw = "北京--上海现在几班";
                int inboxBeforeQ = gs.inbox.Count;
                bool qRan = DeptReportLogic.RunRouteQuery(gs, "R-CAP-1", letterRaw, out string qReceipt);
                // ★ 新信是**插到队首**的（InboxManager.Add → Insert(0)），取队尾只会读到最早那封。
                var qMail = gs.inbox.Count > 0 ? gs.inbox[0] : null;
                bool qLetter = qRan && gs.inbox.Count == inboxBeforeQ + 1 && qMail != null
                               && qMail.subject.Contains("排班咨询")
                               && qMail.body.Contains("您的提问")
                               && qMail.body.Contains(letterRaw)          // 原话要回显，否则答的不是这句
                               && qMail.origin == "R-CAP-1"
                               && !InboxManager.RequiresDecision(qMail);
                // 失败时把「差在哪一格」一起打出来：只印一个 False 等于让人猜。
                int qDelta = gs.inbox.Count - inboxBeforeQ;
                string qSubject = qMail != null && !string.IsNullOrEmpty(qMail.subject) ? qMail.subject : "（无新信）";
                string qLetterWhy = qLetter ? "" : $"（入箱 {(qDelta >= 0 ? "+" : "")}{qDelta} · {qSubject}"
                                                   + $" · origin={qMail?.origin} · 正文含提问={qMail != null && qMail.body.Contains("您的提问")}"
                                                   + $" · 含原话={qMail != null && qMail.body.Contains(letterRaw)}）";

                // ⑫a1 同一句话再问一遍：只更新同一封，不刷屏（与 ⑨ 部门答复同一口径）
                int inboxAfterQ = gs.inbox.Count;
                bool qAskAgain = DeptReportLogic.RunRouteQuery(gs, "R-CAP-1", letterRaw, out _);
                bool qDedupe = qAskAgain && gs.inbox.Count == inboxAfterQ
                               && gs.inbox.Count > 0 && gs.inbox[0].origin == "R-CAP-1";

                // ⑫a 问句不得动账：答复前后班次/待批标记完全一致
                bool qNoLedger = capRoute.FlightCount == wavesAfterCap && capRoute.directivePending;

                // ⑫a2 点名的班号在台账里不存在 → 不退回按城对猜，且只报真实班号
                string badFnRaw = "为什么 NV9999 周三不排班";
                var badFn = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n12_航线网络", badFnRaw), badFnRaw);
                bool badFnOk = !badFn.ok && badFn.note.Contains("NV9999")
                               && badFn.note.Contains("NV2001");   // 报的是台账里真有的班号

                // ⑫b 泛问句仍走部门总账 —— 但必须**有对象**且指向某个部门（「汇报一下公司整体情况」归顾问）
                var genAns = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n12_航线网络", "汇报一下运营情况"));
                bool genStillWorks = genAns.ok && !genAns.needsQuery && genAns.deptCode == "OPS";

                // ⑫b1 公司整体：由航空顾问答，不落当前页那个部门（2026-10-21 实测原话）
                string companyRaw = "汇报一下公司整体情况";
                var companyAsk = CeoCommandRouter.Route("n18_人力资源部", companyRaw);
                var companyAns = DeptReportLogic.Build(gs, companyAsk, companyRaw);
                bool companyOk = companyAsk.topic == CeoCommandRouter.TopicCompany
                                 && companyAsk.deptCode == CeoCommandRouter.DeptAdvisor
                                 && companyAns.ok && companyAns.deptCode == CeoCommandRouter.DeptAdvisor
                                 && companyAns.sender == "航空顾问"
                                 && companyAns.body.Contains("顾问判断")
                                 && companyAns.body.Contains("现金跑道")
                                 && companyAns.body.Contains("建议优先处理")
                                 && companyAns.subject.Contains("公司简报")
                                 && !companyAns.subject.Contains("乘务员");

                // ⑫b2 「汇报一下」只有问法、没有对象：不派件、不发信（2026-10-21 实测原话）
                int inboxBeforeBare = gs.inbox.Count;
                var bareAsk = CeoCommandRouter.Route("n12_航线网络", "汇报一下");
                var bareAns = DeptReportLogic.Build(gs, bareAsk);
                bool bareOk = !bareAns.ok && bareAns.note.Contains("不完整")
                              && gs.inbox.Count == inboxBeforeBare;

                // ⑫c 有话题、无航线、无部门 → 只引导，不发总账（「该城对 2 条线」实测原话）
                var noClue = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n12_航线网络", "该城对 2 条线"));
                bool noClueOk = !noClue.ok && noClue.note.Contains("航线");

                // ⑫d 同城对多班时问句不猜；点名航班号才答
                gs.activeRoutes.Add(new RouteData
                {
                    routeId = "R-CAP-2", origin = "PEK", destination = "SHA",
                    flightNumber = "NV2002", depTime = "18:00", dailyProfit = 3f
                });
                var ambQ = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n12_航线网络", qRaw), qRaw);
                bool ambQOk = !ambQ.ok && ambQ.note.Contains("NV2001") && ambQ.note.Contains("NV2002");
                string fnRaw = "为什么 NV2002 周三不排班";
                var fnAns = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n12_航线网络", fnRaw), fnRaw);
                bool fnOk = fnAns.ok && fnAns.needsQuery && fnAns.routeKey == "R-CAP-2";
                gs.activeRoutes.RemoveAt(gs.activeRoutes.Count - 1);

                // ⑫e 抄周计划上的**逐班号**（第 2 波次 = 基号 +2）也要认得出是哪条线：
                //     只认基号时，CEO 按周计划抄来的号会被判成「没有这条航线」（2026-10-21 报障）。
                //     ★ 夹具必须**真有两班**：capRoute 只有一班，之前拿它取「最后一波」永远等于基号，
                //       这条断言就退化成「基号能认」——测不到被测的东西（2026-10-21 实测 False）。
                var waveRoute = new RouteData
                {
                    routeId = "R-WAVE-1", origin = "CAN", destination = "CTU", distanceKm = 1240f,
                    aircraftType = "A320neo", flightNumber = "NV2100", registration = "B-4001",
                    depTime = "08:00", arrTime = "10:10", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 9f, dailyProfit = 18f, groundStaffAssigned = 2
                };
                waveRoute.SyncFlightsFromPrimary();
                waveRoute.flights.Add(new RouteFlight
                {
                    registration = waveRoute.registration,
                    depTime = "14:00", arrTime = "16:10",
                    daysOfWeek = "每日", weeklyFlights = 7,
                    slotLabel = ScheduleSlots.LabelForDep("14:00"),
                    dutyStart = LiveClockLogic.ParseMinutes("14:00") - OpsRotationParams.ReportBeforeMinutes,
                    dailyProfit = 9f
                });
                gs.activeRoutes.Add(waveRoute);

                int capWaves = waveRoute.flights.Count;
                FlightScheduleFormat.Numbers(waveRoute, capWaves - 1, out string lastOut, out string _);
                string legRaw = $"为什么 {lastOut} 周三不排班";
                var legAns = DeptReportLogic.Build(gs, CeoCommandRouter.Route("n12_航线网络", legRaw), legRaw);
                bool legOk = capWaves > 1 && !string.IsNullOrEmpty(lastOut)
                             && !string.Equals(lastOut, waveRoute.flightNumber, StringComparison.OrdinalIgnoreCase)
                             && legAns.ok && legAns.needsQuery && legAns.routeKey == "R-WAVE-1";
                gs.activeRoutes.Remove(waveRoute);
                string legWhy = legOk ? "" : $"（{lastOut}/{capWaves} 波 · routeKey={legAns.routeKey}"
                                            + $" · note={Trim(legAns.note, 48)}）";

                bool all = pageMap && feasOk && feasBadOk && deptFirst && closeOk && closeUnknownOk
                           && gibberishOk && finBody && hrBody && feasBody && sameProfit && sameVerdict
                           && gateOrder && noSlotFirst && intlPin && feasAltOk && decoOk && domNoRightWord
                           && confirmOk && closeUnknownBody && execOk && execLedger && execLetters
                           && execOnce && dupOk && bareCloseOk && bareFreqOk && bareWhyOk && capRouteOk && capLedger && capNoGuess && capQuery
                           && capDaysOnly && capDup && capGone
                           && qRouteOk && qLetter && qDedupe && qNoLedger && badFnOk && genStillWorks && companyOk && bareOk && noClueOk && ambQOk && fnOk && legOk
                           && dedupe && noDecision;

                Debug.Log($"CEO 命令台 → 页面映射={pageMap}"
                          + $"　可行性={feasOk}（{feas.origin}—{feas.dest}）　缺机场拒答={feasBadOk}"
                          + $"　句子优先于页面={deptFirst}（乘务员→{hr.deptCode}，财务→{fin.deptCode}）"
                          + $"　关线识别={closeOk}（{close.origin}—{close.dest}）"
                          + $"　目录外关线={closeUnknownOk}（{closeUnknown.originText}—{closeUnknown.destText}）"
                          + $"　乱码不派件={gibberishOk}"
                          + $"　财务信={finBody}　人力信={hrBody}　可行性信={feasBody}"
                          + $"　关线确认卡={confirmOk}（不动账）　目录外复核={closeUnknownBody}"
                          + $"　关线执行={execOk}　台账清账={execLedger}　双部门公函={execLetters}　重复确认作废={execOnce}"
                          + $"　同城对不猜={dupOk}"
                          + $"　裸关线（不带「航线」）={bareCloseOk}　带班次不关线={bareFreqOk}　疑问不关线={bareWhyOk}"
                          + $"　运力调令认线={capRouteOk}　已下达={capLedger}　不猜幅度={capNoGuess}"
                          + $"　问句不办={capQuery}　改班期指回页面={capDaysOnly}　多班不猜={capDup}　航线已消失={capGone}"
                          + $"　单线询问={qRouteOk}　答复件={qLetter}{qLetterWhy}　同句只更新={qDedupe}　问句不动账={qNoLedger}　未知班号={badFnOk}　泛问句仍答={genStillWorks}　空问句不派件={bareOk}"
                          + $"　无航线不派件={noClueOk}　同城对问句不猜={ambQOk}　点名航班号={fnOk}　逐班号={legOk}（{lastOut}）{legWhy}"
                          + $"　公司整体→顾问={companyOk}"
                          + $"　回执=正文日利润={sameProfit}（{bodyProfit}）　回执结论一致={sameVerdict}"
                          + $"　前置顺序(航权先于时刻)={gateOrder}　回执不指向时刻={noSlotFirst}"
                          + $"　国际判定(新/日/韩/港)={intlPin}　「可能性」措辞={feasAltOk}　正文机场名不重复={decoOk}"
                          + $"　国内线不提航权={domNoRightWord}"
                          + $"　同句去重={dedupe}（{afterFirst}/{afterSecond}）　汇报件无需批复={noDecision}"
                          + $"　{(all ? "✓" : "✗")}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>失败诊断用：把长文案截短并压成一行，别让一封公函把日志撑爆。</summary>
        static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        /// <summary>取「标签 + 填充 + 值」这一行的值（<see cref="InboxLetter.Row"/> 用全角空格对齐）。</summary>
        static string ValueAfter(string text, string label)

        {
            if (string.IsNullOrEmpty(text)) return null;
            int at = text.IndexOf(label, StringComparison.Ordinal);
            if (at < 0) return null;
            int start = at + label.Length;
            int end = text.IndexOf('\n', start);
            if (end < 0) end = text.Length;
            return text.Substring(start, end - start).Trim(' ', '\u3000', '\r');
        }
    }
}
#endif
