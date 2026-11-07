using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>可行性分析与确认、执行管线；由原文件整方法搬移，不改变行为。</summary>
    public static partial class DeptReportLogic
    {
        // ———————————————————————— 开通可行性 ————————————————————————

        static DeptAnswer Feasibility(GameState gs, CeoCommandRouter.CeoAsk ask)
        {
            string o = ask.origin, d = ask.dest;
            if (string.IsNullOrEmpty(o) || string.IsNullOrEmpty(d))
                return Fail("这台机器没听出出发地与目的地。请写成：分析开通北京—纽约航线的可行性。");
            if (o == d) return Fail("出发地与目的地不能相同。");

            var plan = RouteFeasibility.Build(gs, o, d);
            bool international = RoutePermitLogic.IsInternational(o, d);
            bool fromHub = RoutePermitLogic.FromHub(o, d);
            bool needsSlot = RoutePermitLogic.NeedsSlot(o, d, international, fromHub);
            int slots = RoutePermitLogic.AvailableSlots(gs);
            int airworthy = CountAirworthy(gs);
            float startup = ActionExecutor.OpenRouteStartupCost;

            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section("航线"));
            sb.Append(InboxLetter.Row("线路", $"{AirportCatalog.Label(o)} → {AirportCatalog.Label(d)}"));
            sb.Append(InboxLetter.Row("航距", $"{plan.distanceKm:N0} km（直飞）"));
            sb.Append(InboxLetter.Row("性质", international ? "国际/地区航线（需航权与时刻）" : "国内航线"));

            sb.Append(InboxLetter.Section("需求与收益（1 班/日基准）"));
            sb.Append(InboxLetter.Row("建议机型", $"{plan.aircraftType} · {plan.seats} 座"));
            sb.Append(InboxLetter.Row("需求系数", $"{plan.demandMultiplier:F2}（城市经济台账）"));
            sb.Append(InboxLetter.Row("日收入", $"{plan.output.dailyRevenue:N1} 万美元"));
            sb.Append(InboxLetter.Row("日成本", $"{plan.output.dailyCost:N1} 万美元"));
            // 日利润只在这里格式化一次，命令台回执复用同一个字符串（见文件末尾 ProfitText）。
            // 两处各写一遍 N1 是「同一个数印出两个样」的经典来路。
            string profitText = ProfitText(plan.output.dailyProfit);
            sb.Append(InboxLetter.Row("日利润", profitText));
            sb.Append(InboxLetter.Row("综合评分", $"{plan.output.score} 分 · 评级 {plan.output.rating}"));

            // ———— 开航前置：必须按**实际办理顺序**列，不能只报最后一道 ————
            // 旧版只写「时刻槽不足」，于是 CEO 以为补两条时刻就能开 PEK—CDG；
            // 而现实规则（RoutePermitLogic）是：航权 → 机组资质 → 经营许可 → 时刻 → 开航。
            // 只报末环等于把最贵、最慢的那道闸藏起来了。
            bool rightOk = !international || StrategyManager.HasRouteRight(gs, o, d);
            bool crewOk = !international || RoutePermitLogic.HasOverseasQualification(gs);
            bool cashOk = gs.cash >= startup;
            bool planeOk = airworthy > 0;
            bool slotOk = !needsSlot || slots > 0;

            int reviewQuarters = RoutePermitLogic.ReviewQuarters(international, 1);

            sb.Append(InboxLetter.Section("开航前置（按办理顺序）"));
            // 标签保持等宽（4 个汉字 = 8 格），否则 InboxLetter.Row 的填充会让数值参差不齐
            if (international)
            {
                sb.Append(InboxLetter.Row("① 航权额度", rightOk
                    ? $"已持有 {o}—{d} 双边额度"
                    : "未持有 —— 先办这道，其余都排在它后面"));
                sb.Append(InboxLetter.Row("② 运行资质", crewOk
                    ? "已具备（海外引进机组或持证飞行员）"
                    : "缺 —— 海外引进 1 人或培训中心 3 级考证"));
            }
            else
            {
                sb.Append(InboxLetter.Row("① 航权额度", "国内航线不涉航权，随经营许可核准"));
            }
            sb.Append(InboxLetter.Row(international ? "③ 经营许可" : "② 经营许可",
                $"须递件审批，预计 {reviewQuarters} 个季度批复（第 {1 + reviewQuarters} 季起可飞）"));
            sb.Append(InboxLetter.Row(international ? "④ 时刻槽" : "③ 时刻槽", needsSlot
                ? (slotOk ? $"已备 {slots} 个（发证后再绑定）" : "不足 —— 但排在许可之后，先别买")
                : "不需要（非协调机场）"));
            sb.Append(InboxLetter.Row(international ? "⑤ 资金运力" : "④ 资金运力",
                $"启动 {startup:N0} 万 · 可支配 {airworthy} 架"
                + (cashOk && planeOk ? "" : "（" + (cashOk ? "" : "现金不足") + (!cashOk && !planeOk ? "、" : "")
                   + (planeOk ? "" : "无机可派") + "）")));

            bool profitOk = plan.output.dailyProfit > 0f;
            bool readyToApply = rightOk && crewOk && cashOk && planeOk;
            bool canProceed = readyToApply && profitOk && slotOk;

            sb.Append(InboxLetter.Section("结论"));
            // 结论按**同一个办理顺序**报缺口：先说航权，再说资质、许可、时刻、资金运力。
            // 只报最后一道（时刻）会让 CEO 把钱花在最不该先花的地方。
            if (canProceed)
                sb.Append(InboxLetter.Bullet("建议推进：前置齐备、经济性为正，可按「航线 → 开通新航线」提交。"));
            else
            {
                if (!profitOk) sb.Append(InboxLetter.Bullet("经济性不成立：按 1 班/日基准日利为负，建议先谈时刻与票价或改机型。"));
                if (international && !rightOk)
                    sb.Append(InboxLetter.Bullet($"航权未落定：{o}—{d} 这条线的双边额度还没拿到。"
                        + "这是国际线的第一道闸，没有它后面的许可一律批不下来 —— "
                        + "先进「开通新航线」递件：许可审查发现缺这一对航权时，运营部会把航权申请自动送进收件箱，"
                        + "批准后下季办结（每条约 25 点政府关系，点数每季自然回升）。"));
                if (international && !crewOk)
                    sb.Append(InboxLetter.Bullet("国际运行资质缺口：需至少 1 名海外引进渠道的运行人员，"
                        + "或把培训中心升到 3 级后给飞行员考取「国际运行资质」。"));
                if (!cashOk) sb.Append(InboxLetter.Bullet($"现金不足：开通需 {startup:N0} 万美元，当前 {gs.cash:N0} 万。"));
                if (!planeOk) sb.Append(InboxLetter.Bullet("没有可自由支配的适航飞机，需先引进或等检修出厂。"));
                if (!slotOk)
                {
                    // 国内线没有航权这道闸（同一封邮件上方刚写过「国内航线不涉航权」），
                    // 旧文案对所有航线都写「先拿下航权与许可」，于是正文自相矛盾。
                    sb.Append(InboxLetter.Bullet(international
                        ? "时刻槽不足 —— 但这不是第一步：先拿下航权与许可，"
                          + "发证后再向协调机场申请时刻，避免未获许可时时刻费打水漂。"
                        : "时刻槽不足 —— 但排在经营许可之后：先递件拿许可，"
                          + "发证后再向协调机场申请时刻，避免许可未批就把时刻费花掉。"));
                }
            }
            sb.Append(InboxLetter.Bullet("口径：本条与「开通新航线」页的 RPM 评估同源（RouteFeasibility），不是另算一套。"));
            sb.Append(InboxLetter.Bullet("本封只是研究结论，不构成开通动作；是否开线由您在开通页确认。"));

            string subject = $"【研究】开通 {AirportCatalog.Label(o)} — {AirportCatalog.Label(d)} 航线的可行性（评级 {plan.output.rating}）";
            // 回执与正文同源：日利润直接用 profitText，结论也必须是**同一组判定**。
            // 回执只印「第一件该办的事」，不堆五个缺口 —— 回执是行动指令，不是第二份正文。
            string verdict = canProceed ? "建议推进"
                           : !profitOk ? "不建议"
                           : international && !rightOk ? "先办航权"
                           : international && !crewOk ? "先备国际运行资质"
                           : !cashOk ? "先备启动资金"
                           : !planeOk ? "先备运力"
                           : "先办许可/时刻";
            string oneLine = $"{o}—{d} 日利润 {profitText}（1 班/日）· 评级 {plan.output.rating} · {verdict}";
            return Answer("STG", subject,
                InboxLetter.Frame(gs, "战略发展部", "可行性研究", subject, sb.ToString(),
                    "本封为研究结论，无需批复；是否开线请在「开通新航线」页确认。", null, needDecision: false),
                oneLine);
        }

        static int CountAirworthy(GameState gs)
        {
            if (gs?.fleet == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a != null && a.status == AircraftStatuses.Airworthy) n++;
            }
            return n;
        }

        // ———————————————————————— 取消航线（确认卡 → 执行） ————————————————————————

        /// <summary>
        /// 关线是命令台唯一的写指令，故分两道：
        /// · 匹配到在营航线 → 回一张**确认卡**（不投递、不动账），把日利/班期/机号/要退的槽一次列清；
        /// · 没匹配到 → 仍出一封复核函说明差在哪（这种信有用，不能静默）。
        /// 真正的执行在 <see cref="ExecuteClose"/>。
        /// </summary>
        static DeptAnswer CloseRouteReview(GameState gs, CeoCommandRouter.CeoAsk ask)
        {
            RouteData route = null;
            if (!string.IsNullOrEmpty(ask.origin) && !string.IsNullOrEmpty(ask.dest))
                route = FindRoute(gs, ask.origin, ask.dest);
            if (route == null) route = FindRouteByName(gs, ask.originText, ask.destText);

            string saidOrigin = !string.IsNullOrEmpty(ask.originText) ? ask.originText
                              : !string.IsNullOrEmpty(ask.origin) ? ask.origin : null;
            string saidDest = !string.IsNullOrEmpty(ask.destText) ? ask.destText
                            : !string.IsNullOrEmpty(ask.dest) ? ask.dest : null;

            if (route != null)
            {
                int samePair = CountRoutePair(gs, route.origin, route.destination);
                if (samePair > 1) return CloseAmbiguous(gs, route);

                var plan = RouteCloseLogic.Build(gs, route);
                if (plan.ok) return CloseConfirm(plan);
            }

            var sb = new StringBuilder();
            if (route != null)
            {
                var plan = RouteCloseLogic.Build(gs, route);
                sb.Append(InboxLetter.Section("拟处置航线"));
                sb.Append(InboxLetter.Row("线路", $"{route.origin} — {route.destination}"));
                sb.Append(InboxLetter.Row("班期", route.DualWeeklyLine()));
                sb.Append(InboxLetter.Row("日利", ProfitText(route.dailyProfit)));
                sb.Append(InboxLetter.Section("为什么没有执行"));
                sb.Append(InboxLetter.Bullet(string.IsNullOrEmpty(plan.block) ? "这条航线当前不满足关线条件。" : plan.block));
            }
            else
            {
                sb.Append(InboxLetter.Section("未匹配到在营航线"));
                sb.Append(InboxLetter.Row("CEO 原话所指",
                    string.IsNullOrEmpty(saidOrigin) || string.IsNullOrEmpty(saidDest)
                        ? "（没能从句子里拆出出发地/目的地）"
                        : $"{saidOrigin} — {saidDest}"));
                if (!string.IsNullOrEmpty(saidOrigin) && !string.IsNullOrEmpty(saidDest))
                    sb.Append(InboxLetter.Bullet("台账里没有这条在营航线（或机场名不在机场目录内），"
                                                 + "本次没有可执行的对象，未改动任何台账。"));
                else
                    sb.Append(InboxLetter.Bullet("这句话里没有能拆开的「出发地—目的地」，"
                                                 + "例如：取消北京大兴—上海虹桥的航线。"));
                sb.Append(InboxLetter.Bullet("关线只能针对**在营航线**；要新开一条请用「分析开通…的可行性」。"));
            }

            string subject = route != null
                ? $"【复核】{route.origin}—{route.destination} 本次未执行（不满足关线条件）"
                : string.IsNullOrEmpty(saidOrigin) || string.IsNullOrEmpty(saidDest)
                    ? "【复核】未能匹配到要取消的航线（未改动台账）"
                    : $"【复核】未找到「{saidOrigin}—{saidDest}」这条在营航线（未改动台账）";
            return Answer("OPS", subject,
                InboxLetter.Frame(gs, "运营部", "关线复核", subject, sb.ToString(),
                    "本封为复核通报，未改动任何班次、机号或时刻槽。", null, needDecision: false),
                route != null
                    ? $"{route.origin}—{route.destination} 本次未执行；台账未改动"
                    : "未匹配到在营航线；台账未改动");
        }

        /// <summary>
        /// 同一城对有多条在营航线（一个机场对可以开好几班）：**不猜**。
        /// 确认卡只能写一条线，猜错的代价是关掉另一条 —— 所以把候选列清楚，请 CEO 指明航班号。
        /// </summary>
        static DeptAnswer CloseAmbiguous(GameState gs, RouteData sample)
        {
            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section("这条城对有多条在营航线"));
            sb.Append(InboxLetter.Row("城对", $"{sample.origin} — {sample.destination}"));
            if (gs?.activeRoutes != null)
            {
                int n = 0;
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null || !SamePair(r, sample.origin, sample.destination)) continue;
                    n++;
                    sb.Append(InboxLetter.Bullet(
                        $"{n}. 航班号 {r.flightNumber} · {r.depTime} 离港 · {r.DualWeeklyLine()} · "
                        + $"日利 {ProfitText(r.dailyProfit)}"));
                }
            }
            sb.Append(InboxLetter.Section("为什么没有直接执行"));
            sb.Append(InboxLetter.Bullet("命令台按「出发地—目的地」认线，这一对有多条，"
                                         + "贸然执行可能关掉您并不想停的那一班。"));
            sb.Append(InboxLetter.Bullet("请到「航线 → 单航线」选定具体班次，再用「运力调令」减班；"
                                         + "或先说明航班号，我部再复核。"));
            string subject = $"【复核】{sample.origin}—{sample.destination} 有多条在营航线，请指明班次（未改动台账）";
            return Answer("OPS", subject,
                InboxLetter.Frame(gs, "运营部", "关线复核", subject, sb.ToString(),
                    "本封为复核通报，未改动任何班次、机号或时刻槽。", null, needDecision: false),
                $"这条城对有多条在营航线，请指明航班号；台账未改动");
        }

        static int CountRoutePair(GameState gs, string a, string b)
        {
            if (gs?.activeRoutes == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
                if (gs.activeRoutes[i] != null && SamePair(gs.activeRoutes[i], a, b)) n++;
            return n;
        }

        static bool SamePair(RouteData r, string a, string b)
        {
            if (r == null) return false;
            return (string.Equals(r.origin, a, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.destination, b, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(r.origin, b, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.destination, a, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>确认卡：回答「关掉哪条、代价是什么、能不能反悔」，一个字都不能含糊。</summary>
        static DeptAnswer CloseConfirm(RouteCloseLogic.Plan plan)
        {
            var body = new StringBuilder();
            body.Append($"日利 {plan.dailyProfit:N1} 万美元/日 · 每日 {plan.flightsPerDay} 班 · 周 {plan.weeklyFlights} 班");
            body.Append('\n').Append("执飞 ").Append(plan.aircraft)
                .Append(string.IsNullOrEmpty(plan.flightNumber) ? "" : " · 航班号 " + plan.flightNumber);
            body.Append('\n').Append($"将退还时刻槽 {plan.slotsToRelease} 个 · 撤销航班环 {plan.ringsToRemove} 个");
            if (plan.hasSubsidy)
                body.Append('\n').Append($"将终止航线补贴 {plan.subsidyAnnualWan:N0} 万/年（余 {plan.subsidyYears} 年）");
            body.Append('\n').Append("关线后不可自动恢复（航权仍持有，可重新申请开通）");
            if (!string.IsNullOrEmpty(plan.blockNote))
                body.Append('\n').Append("⚠ ").Append(plan.blockNote);

            return new DeptAnswer
            {
                ok = true,
                needsConfirm = true,
                routeKey = plan.routeId,
                confirmTitle = $"确认停飞 {plan.origin} — {plan.dest}？",
                confirmBody = body.ToString(),
                deptCode = "OPS",
                sender = "运营部",
                role = "关线确认",
                glyph = "运",
                subject = $"【待确认】停飞 {plan.origin}—{plan.dest}（日利 {plan.dailyProfit:N1} 万美元/日）",
                oneLine = $"待确认：停飞 {plan.origin}—{plan.dest}"
            };
        }

        /// <summary>
        /// CEO 点了「确认停飞」之后：先执行，再出两封公函（运营部处置 + 战略部网络复核）。
        /// 执行失败（航线已不在）时返回一封解释函，绝不留一张「看起来已经关掉」的空回执。
        /// </summary>
        public static bool ExecuteClose(GameState gs, string routeKey, string raw,
            out DeptAnswer ops, out DeptAnswer stg, out string message)
        {
            ops = default; stg = default;
            ops.ok = false; stg.ok = false;

            var planRoute = RouteCloseLogic.FindByRouteId(gs, routeKey);
            if (planRoute == null)
            {
                // 值类型结构体：先给一封可投递的解释函，避免「点了确认却什么都没发生」
                message = "这条航线已不在运营，本次确认作废（未改动任何台账）。";
                string goneSubject = "【复核】关线确认已作废（航线已不在运营）";
                var goneBody = new StringBuilder();
                goneBody.Append(InboxLetter.Section("为什么没执行"));
                goneBody.Append(InboxLetter.Bullet(message));
                goneBody.Append(InboxLetter.Bullet("如仍需减运力，请重新下达关线指令。"));
                ops = Answer("OPS", goneSubject,
                    InboxLetter.Frame(gs, "运营部", "关线复核", goneSubject, goneBody.ToString(),
                        "本封为复核通报，未改动任何班次、机号或时刻槽。", null, needDecision: false),
                    message);
                return false;
            }

            if (!RouteCloseLogic.Execute(gs, routeKey, out var o, out message))
            {
                string subj = "【复核】关线确认已作废（航线已不在运营）";
                var body = new StringBuilder();
                body.Append(InboxLetter.Section("为什么没执行"));
                body.Append(InboxLetter.Bullet(string.IsNullOrEmpty(message) ? "未能定位到这条航线。" : message));
                body.Append(InboxLetter.Bullet("台账未被改动。如仍需减运力，请重新下达关线指令。"));
                ops = Answer("OPS", subj,
                    InboxLetter.Frame(gs, "运营部", "关线复核", subj, body.ToString(),
                        "本封为复核通报，未改动任何班次、机号或时刻槽。", null, needDecision: false),
                    message);
                return false;
            }

            ops = CloseDoneLetter(gs, o);
            stg = CloseNetworkLetter(gs, o, raw);
            return true;
        }

        static DeptAnswer CloseDoneLetter(GameState gs, RouteCloseLogic.Outcome o)
        {
            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section("处置结果"));
            sb.Append(InboxLetter.Row("线路", $"{o.originLabel} → {o.destLabel}"));
            sb.Append(InboxLetter.Row("已停飞", $"每日 {o.flightsPerDay} 班（周 {o.weeklyFlights} 班）"));
            sb.Append(InboxLetter.Row("原日利", ProfitText(o.dailyProfit)));
            if (!string.IsNullOrEmpty(o.aircraft))
                sb.Append(InboxLetter.Row("机号", $"{o.aircraft}（已回到可用机队）"));

            sb.Append(InboxLetter.Section("台账变动"));
            sb.Append(InboxLetter.Row("时刻槽", o.slotsReleased > 0
                ? $"已退还 {o.slotsReleased} 个（可分配给新线）"
                : "本线未占用时刻槽"));
            sb.Append(InboxLetter.Row("航班环", o.ringsRemoved > 0
                ? $"已撤销 {o.ringsRemoved} 个（连班两段之一已停飞）"
                : "无受影响航班环"));
            if (o.subsidyAnnualWan > 0f)
                sb.Append(InboxLetter.Row("航线补贴",
                    $"已终止 {o.subsidyAnnualWan:N0} 万/年（余 {o.subsidyYears} 年不再入账）"));
            sb.Append(InboxLetter.Row("在营航线", $"{o.routesBefore} → {o.routesAfter} 条"));

            sb.Append(InboxLetter.Section("后续"));
            sb.Append(InboxLetter.Bullet("航权与机场时刻池里的历史优先权**不随关线失效**，重新开航时通常走较快通道。"
                                         + "（时刻槽持仓本身已退回池中，重开需重新绑定。）"));
            sb.Append(InboxLetter.Bullet("机组与机务编制按在役机队派生，本线未改动机队规模，故运行岗缺口不受影响。"));
            if (o.routesAfter == 0)
                sb.Append(InboxLetter.Bullet("★公司现无在营航线：现金只出不进，请尽快补线或收紧开支。"));

            string subject = $"【处置】已停飞 {o.origin}—{o.dest}（原日利 {o.dailyProfit:N1} 万美元/日）";
            return Answer("OPS", subject,
                InboxLetter.Frame(gs, "运营部", "关线处置", subject, sb.ToString(),
                    "本封为处置通报，已执行完毕，无需批复。", null, needDecision: false),
                $"已停飞 {o.origin}—{o.dest}；退还时刻槽 {o.slotsReleased} 个，航线 {o.routesBefore}→{o.routesAfter} 条");
        }

        static DeptAnswer CloseNetworkLetter(GameState gs, RouteCloseLogic.Outcome o, string raw)
        {
            float delta = o.NetworkDailyDelta;
            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section("网络影响"));
            sb.Append(InboxLetter.Row("公司日利", $"{o.networkDailyBefore:N1} → {o.networkDailyAfter:N1} 万美元/日"
                + $"（{(delta >= 0f ? "+" : "")}{delta:N1}）"));
            sb.Append(InboxLetter.Row("在营航线", $"{o.routesBefore} → {o.routesAfter} 条"));
            sb.Append(InboxLetter.Section("战略判断"));
            if (o.dailyProfit < 0f)
                sb.Append(InboxLetter.Bullet("本线原本在亏，关停属于**止血**：网络日利并未因此变差。"));
            else
                sb.Append(InboxLetter.Bullet($"本线原本在赚（{ProfitText(o.dailyProfit)}），关停后网络日利同步下降 "
                                             + $"{Math.Abs(delta):N1} 万美元/日 —— 这是主动让出的运力，不是亏损止血。"));
            sb.Append(InboxLetter.Bullet(o.routesAfter == 0
                ? "★网络已清空，战略部建议立即启动基地出发的新线评估。"
                : "释放出的运力与时刻槽可用于更高收益的城对；如需我部评估替代航线，请下达「分析开通…可行性」。"));
            sb.Append(InboxLetter.Bullet("本线若日后重启，需重新走航权/资质/时刻槽流程（历史记录在案，不保证自动通过）。"));

            string subject = $"【复核】{o.origin}—{o.dest} 停飞后的网络影响（日利 {o.networkDailyBefore:N1} → {o.networkDailyAfter:N1}）";
            return Answer("STG", subject,
                InboxLetter.Frame(gs, "战略发展部", "网络复核", subject, sb.ToString(),
                    "本封为复核通报，无需批复。", null, needDecision: false),
                $"网络日利 {o.networkDailyBefore:N1} → {o.networkDailyAfter:N1} 万美元/日");
        }

        // ———————————————— 运力调令（只解析 + 调起既有管线） ————————————————

        /// <summary>
        /// 命令台的写指令②：加减班 / 改班期 / 换机号。
        ///
        /// **本方法不改任何台账**，只回答「这句话落到哪条航线上」；执行在
        /// <see cref="RunCapacityDirective"/>。两者分开的理由与关线一致：
        /// 认线（可能认错）与办线（不可逆）是两件事，混在一起就没法单独测认线。
        /// </summary>
        /// <summary>
        /// 航班环调令（2026-10-30）：CEO 一句话 → 运营部六步后台编成 → 方案/缺口报告进收件箱。
        ///
        /// 命令台只回一句结论（「编成 N 个环，已呈报」），全文在邮件里 ——
        /// 这正是 CEO 视角重构要的形态：他不看拼图过程，只批方案。
        /// </summary>
        static DeptAnswer RotationDirective(GameState gs, CeoCommandRouter.CeoAsk ask, string raw)
        {
            var opt = new OpsRotationBuilder.Options();
            if (!string.IsNullOrEmpty(ask.originText)) opt.PreferredBase = ask.originText;
            var plan = OpsRotationBuilder.BuildFromGameState(gs, opt);

            if (plan.TotalLegs == 0)
            {
                return new DeptAnswer
                {
                    ok = true,
                    deptCode = "OPS",
                    sender = "运营部",
                    role = "排班答复",
                    glyph = "运",
                    subject = "航班环",
                    body = OpsRotationBuilder.FormatBody(plan, raw),
                    oneLine = "全网航班都已排定，暂无待编航段。"
                };
            }

            InboxManager.QueueRotationPlan(gs, plan, raw);
            string head = OpsRotationBuilder.Headline(plan);
            return new DeptAnswer
            {
                ok = true,
                deptCode = "OPS",
                sender = "运营部",
                role = "航班环方案",
                glyph = "运",
                subject = "航班环方案",
                body = OpsRotationBuilder.FormatBody(plan, raw),
                oneLine = head + " 已呈报收件箱，待您批复。"
            };
        }

        static DeptAnswer CapacityDirective(GameState gs, CeoCommandRouter.CeoAsk ask, string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return Fail("缺少原话，无法解析调令。例：把北京—上海加密到每天 3 班。");

            if (!TryResolveRoute(gs, ask, raw, out RouteData route))
                return Fail(DescribeMissingRoute(gs, ask, asking: false));

            // 一个机场对可以开多班：不猜。猜错的代价是改错另一条线的班次。
            // 由**页面上下文**定位的那条线例外：CEO 就站在它的详情页上，屏幕已经替他指明了是哪一条。
            int samePair = string.IsNullOrEmpty(ask.contextRouteKey) && ask.flightNumber == null
                ? CountRoutePair(gs, route.origin, route.destination)
                : 1;
            if (samePair > 1)
            {
                string eg = FirstFlightNumberOf(gs, route.origin, route.destination);
                return Fail($"{route.origin}—{route.destination} 有 {samePair} 条在营航线"
                            + $"（{FlightNumbersOf(gs, route.origin, route.destination)}），"
                            + $"请指明班号{(string.IsNullOrEmpty(eg) ? "" : "（例：" + eg + "）")}；本次未改动任何台账。");
            }

            return new DeptAnswer
            {
                ok = true,
                needsRun = true,
                // ★2026-10-29：`ask` 快照必须带出去。RunPending 按 `ask.topic` 分派，
                //   漏了这一行，运力调令在命令台上就会落到「没有可执行的管线」——
                //   四类写指令走 Directive() 都有 ask，唯独调令单独构造，很容易再漏。
                ask = ask,
                routeKey = route.routeId,
                deptCode = "OPS",
                sender = "运营部",
                role = "运力调令",
                glyph = "运",
                subject = $"运力调令 {route.origin}—{route.destination}",
                oneLine = $"{route.origin}—{route.destination}"
            };
        }

        /// <summary>
        /// 具体时刻加一班（2026-11-01）：命令台说「06:30 给北京—上海加一班」。
        ///
        /// 与运力调令分开的理由：调令说的是**一天几班**，铺哪些时刻由运营部按机队窗口安排；
        /// 本指令点的是**一个具体时刻槽**。混进调令会让「06:30 加一班」被办成「加密到每天 N 班」，
        /// 时刻由系统自己挑 —— CEO 要的那一格未必被排上。
        /// 执行仍然交给既有管线（<see cref="InboxManager.QueueAddFlightByOps"/> → 呈报待批），
        /// 本方法只负责认线与回一句话。
        /// </summary>
        static DeptAnswer AddFlightDirective(GameState gs, CeoCommandRouter.CeoAsk ask, string raw)
        {
            if (string.IsNullOrEmpty(ask.clock))
                return Fail("没读到要加在哪一刻。例：06:30 给北京—上海加一班。");

            if (!TryResolveRoute(gs, ask, raw, out RouteData route))
                return Fail(DescribeMissingRoute(gs, ask, asking: false));

            int samePair = string.IsNullOrEmpty(ask.contextRouteKey) && ask.flightNumber == null
                ? CountRoutePair(gs, route.origin, route.destination)
                : 1;
            if (samePair > 1)
            {
                string eg = FirstFlightNumberOf(gs, route.origin, route.destination);
                return Fail($"{route.origin}—{route.destination} 有 {samePair} 条在营航线"
                            + $"（{FlightNumbersOf(gs, route.origin, route.destination)}），"
                            + $"请指明班号{(string.IsNullOrEmpty(eg) ? "" : "（例：" + eg + "）")}；本次未作呈报。");
            }

            return new DeptAnswer
            {
                ok = true,
                needsRun = true,
                ask = ask,
                routeKey = route.routeId,
                deptCode = "OPS",
                sender = "运营部",
                role = "加班呈报",
                glyph = "运",
                subject = $"加机呈报 {route.origin}—{route.destination} {ask.clock}",
                oneLine = $"{route.origin}—{route.destination} {ask.clock}"
            };
        }

        /// <summary>
        /// 真正下达：交运营部「挑一架排得进的机 → 呈报」的那条既有管线。
        ///
        /// 之所以不让命令台自己挑机：挑机要一起判值勤上限、过站、宵禁、串飞（
        /// <see cref="OpsRotationLogic.PickForWindow"/>），那是运营部的排班口径；
        /// 命令台只该把「哪条线、哪一刻」讲清楚。
        /// </summary>
        public static bool RunAddFlight(GameState gs, string routeKey, string clock, string raw, out string receipt)
        {
            receipt = "";
            if (gs == null) { receipt = "游戏尚未就绪。"; return false; }

            var route = RouteCloseLogic.FindByRouteId(gs, routeKey);
            if (route == null)
            {
                receipt = "这条航线已不在运营，本次指令作废（未改动任何台账）。";
                return false;
            }

            bool ok = InboxManager.QueueAddFlightByOps(gs, route, clock, out string msg);
            receipt = string.IsNullOrEmpty(msg)
                ? $"{route.origin}—{route.destination} {clock}：运营部已受理，结果以邮件呈报。"
                : msg;
            return ok;
        }

        /// <summary>
        /// 真正下达：解析原话 → 交 <see cref="CapacityDirectiveLogic"/> 既有管线 → 呈报件进收件箱。
        ///
        /// 三条必须照抄页面侧的做法（否则命令台会变成「第三套结算」）：
        /// · 问句先转答复件，绝不能把「为什么周三不排班」当调令办；
        /// · 方向/幅度没读懂就**不执行**，只把拟定方案回显给 CEO；
        /// · 改派机号只发提名件（管线自己投递），这里不再补一封运力呈报。
        /// </summary>
        public static bool RunCapacityDirective(GameState gs, string routeKey, string raw, out string receipt)
        {
            receipt = "";
            if (gs == null) { receipt = "游戏尚未就绪。"; return false; }

            var route = RouteCloseLogic.FindByRouteId(gs, routeKey);
            if (route == null)
            {
                receipt = "这条航线已不在运营，本次指令作废（未改动任何台账）。";
                return false;
            }

            DirectiveLexicon.Init();                     // 「记忆」是解析的一等输入，先挂上（幂等）
            var d = CapacityDirectiveLogic.Parse(raw);
            // 只说目标、不说增减（「调整为每日一段往返」）：方向由现状与目标比出来
            CapacityDirectiveLogic.ResolveStatedTarget(route, d);

            if (d.query)
            {
                bool queued = InboxManager.QueueOpsQueryAnswer(gs, route, raw, out string qmsg);
                receipt = queued
                    ? $"{route.origin}—{route.destination}：这是询问，运营部已答复（见收件箱），班次与机号未改动"
                    : $"未能发出答复：{qmsg}";
                return queued;
            }

            if (CapacityDirectiveLogic.NeedsConfirm(d))
            {
                // 纯改班期（「改成每周一三五」）读得到班期、读不到增减：
                // 这类不能猜（猜成加密就是真的加密），也不该冒充「已改」——指回既有页面。
                bool daysOnly = !string.IsNullOrEmpty(d.days) && d.waves <= 0
                                && (d.deps == null || d.deps.Length == 0);
                if (daysOnly)
                {
                    receipt = $"未执行：读到班期 {d.days}，但没读到班次增减；"
                              + "单独改班期请到「运营 → 周计划」页调整，或连班次一起说"
                              + "（例：加密到每天 2 班，每周一三五执飞）";
                    return false;
                }
                // 回显「系统打算怎么改」而不是猜着办：方向读错会把减班办成加密，事后看不出来。
                receipt = $"未执行（没读懂幅度/方向）：拟定 {CapacityDirectiveLogic.PlanLine(route, d)}"
                          + "；请写明目标，例：加密到每天 3 班、削减到每天 1 班";
                return false;
            }

            // ★2026-10-29：改为**试算**（Plan）—— 运营部排一遍拿到呈报里的数字，随即回退，
            //   账面停在指令前；真正排班在 CEO 批准那一刻（ApproveCapacityDirective 重跑 Execute）。
            var rep = CapacityDirectiveLogic.Plan(gs, route, d);
            if (!rep.ok)
            {
                receipt = string.IsNullOrEmpty(rep.note) ? "未能执行（运营部未改动本线班次）。" : rep.note;
                return false;
            }

            if (rep.assignment)
            {
                // 提名件已由管线投递；退回等于没提过，本线维持现状
                receipt = $"已提名执飞机（{route.origin}—{route.destination}）：请到收件箱选定一架，退回则不指派";
                return true;
            }

            bool ok = InboxManager.QueueCapacityReport(gs, route, rep, out bool studying, -1, true);
            // 挂起「原话 → 解析结果」：CEO 在收件箱批准后才真正成为记忆（与页面同源）
            DirectiveLexicon.Stage(d.raw, d, d.fromAi);

            string ring = string.IsNullOrEmpty(rep.ringIdsCsv) ? "未编环" : "拟定航班环 " + rep.ringIdsCsv;
            // ★2026-11-04 用户问询：「还没收到反馈邮件时再输入同样指令，游戏怎么应对？这是个 bug 吗？」
            //   旧行为确实是 bug：同一条航线重下同一句令，`Schedule` 会把**同一封**待发呈报的
            //   到件时刻改到「新令 + 研究期」，于是研究期被一次次重置，CEO 永远等不到回音。
            //   现在同目标的重下不再重排期、也不生成第二份方案，只如实告诉他原令还在研究中。
            string wait = InboxManager.CapacityStudyWaitText(gs, route);
            receipt = ok
                ? (studying
                    ? $"{CapacityDirectiveLogic.RegsOf(route)} · {route.DualWeeklyLine()} · {ring}"
                      + $" · 这条调令运营部**已在研究中**（{wait}），本次不重复受理、不会顺延到件时间"
                      + "（若要改方案，请下达不同的目标）"
                    : $"{CapacityDirectiveLogic.RegsOf(route)} · {route.DualWeeklyLine()} · {ring}"
                      + $" · 运营部已受理，进入研究期：约 {InboxCadence.CapacityStudyBusinessDays} 个工作日后进收件箱（{wait}）"
                      + "（班次此刻未动，批准后立即排班）")
                : $"{route.DualWeeklyLine()} · 呈报未能投递，请检查收件箱状态";
            return ok;
        }

        // ———————————————————————— 四类写指令（开线 / 购机 / 招聘 / 融资） ————————————————————————

        /// <summary>
        /// 这四类的产物**本来就是请示件**：CEO 要在收件箱批一次，批错了还能驳回。
        ///
        /// 所以这里刻意**不再弹确认卡**（与运力调令同一条理由）：同一件事问两遍，
        /// 会让人以为第一次没生效，而且真正的决定点始终是收件箱里那封信。
        /// 本类只负责把参数原样交给 <c>InboxManager</c> 的既有呈报方法，**不写任何台账**。
        /// </summary>
        static DeptAnswer Directive(CeoCommandRouter.CeoAsk ask, string dept, string oneLine)
            => new DeptAnswer
            {
                ok = true,
                needsRun = true,
                ask = ask,
                deptCode = dept,
                oneLine = oneLine
            };

        static DeptAnswer OpenRouteDirective(CeoCommandRouter.CeoAsk ask)
            => Directive(ask, "OPS",
                $"{ask.origin}—{ask.dest}" + (string.IsNullOrEmpty(ask.via) ? "" : "（经停 " + ask.via + "）"));

        static DeptAnswer FleetBuyDirective(CeoCommandRouter.CeoAsk ask)
            => Directive(ask, CeoCommandRouter.DeptFleet, $"{ask.aircraftType} ×{ask.count} · {ask.source}");

        static DeptAnswer HrRecruitDirective(CeoCommandRouter.CeoAsk ask)
            => Directive(ask, "HR", $"{ask.position} ×{ask.count} · {ChannelName(ask.channel)}");

        static DeptAnswer FinanceDirective(CeoCommandRouter.CeoAsk ask)
            => Directive(ask, "FIN", FinanceKindName(ask.financeKind));

        /// <summary>
        /// 命令台「申请 &lt;机场&gt; &lt;时刻&gt; [到港] 时刻槽」（★2026-11-06 用户裁决）。
        ///
        /// 用户原话：「取消浮窗里的按钮，所有申请时刻命令，都在命令台下达。」
        /// 机场时刻页浮窗里那颗「只申请到港时刻」按钮已删除，于是命令台必须真的能办这件事，
        /// 而不是像 2026-11-04 那样只会回一句「这不是 CEO 的指令」——那等于把入口删掉了却没补新的。
        ///
        /// 产物仍是**提案**（<c>apply_slot</c>，收件箱待批），与运营部自己递单走同一条管线：
        /// 协调池是免费的、要排队，CEO 点一句话只是把单子递出去，成交与否看协调人。
        /// </summary>
        static DeptAnswer SlotDealDirective(CeoCommandRouter.CeoAsk ask)
        {
            if (ask.slotBuy)
                return Fail("买入时刻要走二级市场（或让运营部递购入提案、由你批「买不买」，提案落在收件箱），"
                            + "命令台不直接扣款成交通。本次未向任何部门派件、未动账。");

            bool arr = string.Equals(ask.slotKind, SlotHolding.SlotKindArrival, StringComparison.OrdinalIgnoreCase);
            string end = arr ? "到港" : "离港";
            int n = ask.count < 1 ? 1 : ask.count;
            return new DeptAnswer
            {
                ok = true,
                needsRun = true,
                ask = ask,
                deptCode = "STG",
                sender = "战略发展部",
                role = "航权提案",
                glyph = "战",
                subject = $"向协调池申请 {ask.origin} {ask.clock} {end}时刻槽",
                oneLine = $"{ask.origin} {ask.clock} {end} {n} 条"
            };
        }

        /// <summary>
        /// 真正递单：交 <c>InboxManager.QueueApplySlots</c>（与运营部自动申请同一入口，
        /// 池满 / 该刻无槽 / 同一时刻已在申报时如实驳回，不静默挪到别的刻）。
        /// </summary>
        public static bool RunSlotApply(GameState gs, CeoCommandRouter.CeoAsk ask, out string receipt)
        {
            receipt = "";
            if (gs == null) { receipt = "游戏尚未就绪。"; return false; }
            bool arr = string.Equals(ask.slotKind, SlotHolding.SlotKindArrival, StringComparison.OrdinalIgnoreCase);
            string kind = arr ? SlotHolding.SlotKindArrival : SlotHolding.SlotKindDeparture;
            int n = ask.count < 1 ? 1 : ask.count;
            bool ok = InboxManager.QueueApplySlots(gs, ask.origin, ask.clock, n, null, out string msg,
                arr ? "CEO 在命令台点名申请的到港时刻" : "CEO 在命令台点名申请的离港时刻",
                null, null, 0, false, null, kind);
            receipt = ok
                ? $"已就 {ask.origin} {ask.clock} 的{(arr ? "到港" : "离港")}时刻向协调池递单 {n} 条"
                  + "（免费渠道；提案在收件箱待批，批准后按天交割）"
                : msg;
            return ok;
        }

        static string FinanceKindName(int kind)
            => kind == 3 ? "股东增资" : kind == 2 ? "高息过桥" : "银行贷款";

        static string ChannelName(int channel)
            => channel == 3 ? "内部转岗" : channel == 2 ? "海外引进" : channel == 1 ? "猎头挖角" : "校园招聘";

        /// <summary>
        /// 命令条的统一执行入口：按 topic 把参数交给**既有**管线。
        ///
        /// 之所以要这样一个入口，是因为命令条不该知道「哪一类指令走哪个方法」——
        /// 那会让壳层（UI）再长出第二套分派表，与路由里的那张迟早对不上。
        /// </summary>
        public static bool RunPending(GameState gs, DeptAnswer ans, string raw, out string receipt)
        {
            receipt = "";
            switch (ans.ask.topic)
            {
                case CeoCommandRouter.TopicCapacity:
                    return RunCapacityDirective(gs, ans.routeKey, raw, out receipt);
                case CeoCommandRouter.TopicAddFlight:
                    return RunAddFlight(gs, ans.routeKey, ans.ask.clock, raw, out receipt);
                case CeoCommandRouter.TopicOpenRoute:
                    return RunOpenRoute(gs, ans.ask, out receipt);
                case CeoCommandRouter.TopicFleetBuy:
                    return RunFleetBuy(gs, ans.ask, out receipt);
                case CeoCommandRouter.TopicHrRecruit:
                    return RunHrRecruit(gs, ans.ask, out receipt);
                case CeoCommandRouter.TopicFinance:
                    return RunFinanceRequest(gs, ans.ask, out receipt);
                case CeoCommandRouter.TopicSlotDeal:
                    return RunSlotApply(gs, ans.ask, out receipt);
            }
            receipt = "这条指令没有可执行的管线。";
            return false;
        }

        static bool RunOpenRoute(GameState gs, CeoCommandRouter.CeoAsk ask, out string receipt)
        {
            // 与页面「提交研究」同一个组装函数：同一条航线不会有两个版本的研究会纪要。
            var plan = OpenRouteStudyLogic.Build(gs, ask.origin, ask.dest, ask.via, ask.rush);
            if (!plan.ok)
            {
                receipt = string.IsNullOrEmpty(plan.reject) ? "这条航线不成立，未发请示。" : plan.reject;
                return false;
            }
            bool ok = InboxManager.QueueOpenRouteStudy(gs, ask.origin, ask.dest, ask.via,
                plan.subject, plan.body, ask.rush);
            receipt = ok
                // ★2026-11-03 用户问询：「反馈行里写『75 分』是什么意思？」——
                //   光写一个裸分数等于让人猜。这里带上量纲（/100）、评级，以及它到底在评什么；
                //   同时把「已在收件箱呈报」改成「已交运营部研究」：信要等工期到了才进收件箱，
                //   旧措辞和同一条反馈里的「约 2 个工作日后到件」自相矛盾。
                ? $"{plan.path}：已交运营部做开航研究会（航线评分 {plan.rpm.score}/100 · {plan.rpm.rating}，"
                  + "按预估日利润、利润率与竞争强度折算）"
                  + (ask.rush
                      ? $"；加急会商，约 {InboxCadence.ReplyHours} 小时后把纪要与前置条件清单送进收件箱；"
                        + "局方审查仍按工期走，受理后可用「加急请示」再压"
                      : $"；约 {InboxCadence.StudyBusinessDays} 个工作日后把纪要与前置条件清单送进收件箱；"
                        + "批准后仍须办妥准入")
                : $"{plan.path}：请示未能投递，请检查收件箱状态";
            return ok;
        }

        static bool RunFleetBuy(GameState gs, CeoCommandRouter.CeoAsk ask, out string receipt)
        {
            bool ok = InboxManager.OfferFleetBuy(gs, ask.aircraftType, ask.count, ask.source, out string msg);
            receipt = msg;
            return ok;
        }

        static bool RunHrRecruit(GameState gs, CeoCommandRouter.CeoAsk ask, out string receipt)
        {
            bool ok = InboxManager.OfferHrRecruit(gs, ask.position, ask.count, ask.urgency, ask.channel, out string msg);
            receipt = msg;
            return ok;
        }

        static bool RunFinanceRequest(GameState gs, CeoCommandRouter.CeoAsk ask, out string receipt)
        {
            bool ok = InboxManager.OfferFinanceLoan(gs, ask.financeKind, out string msg);
            receipt = msg;
            return ok;
        }
    }
}
