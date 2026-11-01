using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>公司与部门报表投影；由原文件整方法搬移，不改变行为。</summary>
    public static partial class DeptReportLogic
    {
        // ———————————————————————— 公司整体（航空顾问） ————————————————————————

        /// <summary>
        /// 「公司整体情况」由**航空顾问**答，而不是当前页那个部门。
        ///
        /// 理由不是排版而是口径：这个问题要的是跨部门的**判断**（现金够不够、网络赚不赚、
        /// 机队与机组跟不跟得上、董事会怎么看），任何单一部门的账本都只覆盖其中一格；
        /// 站在人事页问它、却收到一封招聘缺口信，就把「公司层面」和「部门层面」混淆了。
        ///
        /// 每个数都取自与各部门汇报**同一个源头**（跑道、月收、航线、机队、编制），
        /// 顾问只做汇总与排序，不新增任何估算 —— 否则同一局里两封信会各说一个数。
        /// </summary>
        static DeptAnswer CompanyOverview(GameState gs)
        {
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            float monthlyRevenue = gs.monthlyRevenue;
            float net = monthlyRevenue - gs.EffectiveMonthlyBurn;

            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int weekly = 0, loss = 0;
            float daily = 0f;
            var worst = new List<RouteData>();
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    weekly += r.WeeklyFlightsDeparture();
                    daily += r.dailyProfit;
                    if (r.dailyProfit < 0f) { loss++; worst.Add(r); }
                }
            }
            worst.Sort((a, b) => a.dailyProfit.CompareTo(b.dailyProfit));

            int inService = FleetManager.InServiceCount(gs);
            int airworthy = 0, maint = 0;
            if (gs.fleet != null)
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var a = gs.fleet[i];
                    if (a == null || a.status == AircraftStatuses.Retired) continue;
                    if (a.status == AircraftStatuses.Maintenance) maint++;
                    else airworthy++;
                }
            }
            int crewGap = CrewGapTotal(gs);
            var credit = LoanManager.GetCreditRating(gs);

            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section("顾问判断"));
            sb.Append(InboxLetter.Bullet(CompanyVerdict(runway, daily, net)));

            sb.Append(InboxLetter.Section("财务"));
            sb.Append(InboxLetter.Row("现金余额", $"{gs.cash:N0} 万美元"));
            sb.Append(InboxLetter.Row("现金跑道", CashRunnelCalculator.FormatPlayerLabel(runway)));
            sb.Append(InboxLetter.Row("月收入 / 月净现金", $"{monthlyRevenue:N0} 万 / {(net >= 0 ? "+" : "")}{net:N0} 万"));
            sb.Append(InboxLetter.Row("信用评级", LoanManager.CreditLabel(credit)));

            sb.Append(InboxLetter.Section("网络"));
            sb.Append(InboxLetter.Row("在营航线 / 每周班次", $"{routes} 条 / {weekly} 班"));
            sb.Append(InboxLetter.Row("日利润合计", $"{daily:N1} 万美元"));
            sb.Append(InboxLetter.Row("准点率 / 客座率", $"{gs.onTimePercent:F1}% / {gs.loadFactorPercent:F1}%"));
            if (worst.Count > 0)
            {
                var names = new List<string>();
                for (int i = 0; i < worst.Count && i < 3; i++)
                    names.Add($"{worst[i].origin}—{worst[i].destination}（{worst[i].dailyProfit:N1} 万）");
                sb.Append(InboxLetter.Row("亏损航线", $"{loss} 条 · {string.Join("、", names)}"));
            }
            else sb.Append(InboxLetter.Row("亏损航线", "无"));

            sb.Append(InboxLetter.Section("运力与队伍"));
            sb.Append(InboxLetter.Row("在册机队", $"{inService} 架（可执行 {airworthy} · 停场 {maint}）"));
            sb.Append(InboxLetter.Row("运行岗缺口", $"{crewGap} 人"));
            sb.Append(InboxLetter.Row("董事会满意度 / 品牌", $"{gs.boardSatisfaction:F0} / 100 · {gs.brandAwareness:F0} / 100"));

            var advice = CompanyAdvice(runway, loss, worst, crewGap, maint);
            sb.Append(InboxLetter.Section("建议优先处理"));
            for (int i = 0; i < advice.Count; i++) sb.Append(InboxLetter.Bullet($"{i + 1}. {advice[i]}"));
            sb.Append(InboxLetter.Bullet("口径　本封由顾问汇总各部门台账，未改动任何班次、机号或现金；"
                                         + "要动哪一项，请找对应部门下指令（例：把北京—上海加密到每天 2 班）。"));

            string subject = $"【公司简报】现金 {gs.cash:N0} 万 · 跑道 {runway:F1} 月 · 日利 {daily:N1} 万 · 航线 {routes} 条";
            return Answer(CeoCommandRouter.DeptAdvisor, subject,
                InboxLetter.Frame(gs, "航空顾问", "首席航空顾问", subject, sb.ToString(),
                    "本封为通报，无需批复。", null, needDecision: false),
                $"现金 {gs.cash:N0} 万 · 跑道 {runway:F1} 月 · 日利 {daily:N1} 万 · 亏损线 {loss} 条");
        }

        /// <summary>一句话结论：先说不好的那件事，别用「总体平稳」把风险盖掉。</summary>
        static string CompanyVerdict(float runway, float daily, float net)
        {
            if (runway < 3f || daily < 0f)
                return "结论：【需要立刻处置】"
                       + (runway < 3f ? $"现金跑道只剩 {runway:F1} 个月；" : "")
                       + (daily < 0f ? "网络整体日利为负；" : "")
                       + "在止血之前不宜扩张。";
            if (runway < 6f || net < 0f)
                return "结论：【偏紧】还能正常经营，但没有多少余量，扩张前先确认现金与机组。";
            return "结论：【稳健】现金与网络都在健康区间，可按计划推进扩张。";
        }

        /// <summary>按「先止损、再补人、后扩张」的顺序给 3 条，避免一次列十条让人不知道先做哪个。</summary>
        static List<string> CompanyAdvice(float runway, int loss, List<RouteData> worst, int crewGap, int maint)
        {
            var list = new List<string>();
            if (loss > 0 && worst.Count > 0)
                list.Add($"处置亏损线 {loss} 条：{worst[0].origin}—{worst[0].destination} 日利 {worst[0].dailyProfit:N1} 万最差，"
                         + "可停飞或改班（命令台说「停飞…航线」或「把…削减到每天 1 班」）。");
            if (runway < 6f)
                list.Add($"现金跑道 {runway:F1} 个月：先压缩非必要开支或安排融资，再谈开新线。");
            if (crewGap > 0)
                list.Add($"运行岗缺口 {crewGap} 人：不补人，加密班次会在排班阶段被卡住（人力 → 招聘）。");
            if (maint > 0)
                list.Add($"停场 {maint} 架：确认定检排期，别让可用运力继续变薄（机务）。");
            if (list.Count == 0)
                list.Add("没有必须马上处理的风险；若要扩张，可先做一份开航可行性（例：分析开通北京—纽约航线的可行性）。");
            return list;
        }

        /// <summary>运行岗缺口合计（口径与人力部汇报一致：编制 − 在职 − 在途）。</summary>
        static int CrewGapTotal(GameState gs)
        {
            int total = 0;
            for (int i = 0; i < CrewPosts.Length; i++)
            {
                if (!HrManager.RecruitRoomLeft(gs, CrewPosts[i], out int _, out int _, out int _, out int room))
                    continue;
                total += Math.Max(0, room);
            }
            return total;
        }

        // ———————————————————————— 财务 ————————————————————————

        static DeptAnswer Finance(GameState gs)
        {
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            float net = gs.monthlyRevenue - gs.EffectiveMonthlyBurn;
            var credit = LoanManager.GetCreditRating(gs);

            var body = new StringBuilder();
            body.Append(InboxLetter.Section("现金与跑道"));
            body.Append(InboxLetter.Row("现金余额", $"{gs.cash:N0} 万美元"));
            body.Append(InboxLetter.Row("现金跑道", CashRunnelCalculator.FormatPlayerLabel(runway)));
            body.Append(InboxLetter.Row("月净现金", $"{(net >= 0 ? "+" : "")}{net:N0} 万美元/月"));
            body.Append(InboxLetter.Section("收支结构"));
            body.Append(InboxLetter.Row("月收入", $"{gs.monthlyRevenue:N0} 万美元"));
            body.Append(InboxLetter.Row("月固定开销", $"{gs.monthlyBurnRate:N0} 万美元"));
            body.Append(InboxLetter.Row("机队月维护", $"{gs.FleetMonthlyMaintenance:N0} 万美元"));
            body.Append(InboxLetter.Row("机队月租金", $"{gs.FleetMonthlyLease:N0} 万美元"));
            body.Append(InboxLetter.Section("信用与提示"));
            body.Append(InboxLetter.Row("信用评级", LoanManager.CreditLabel(credit)));
            body.Append(InboxLetter.Bullet(runway < 3f
                ? "跑道已不足 3 个月，建议立即压缩非必要开支或启动融资。"
                : runway < 6f
                    ? "跑道偏薄，扩张前请先确认现金融资安排。"
                    : "跑道健康，可按计划推进机队与航线扩张。"));

            string subject = $"【汇报】当前财务状况：现金 {gs.cash:N0} 万美元 · 跑道 {runway:F1} 个月";
            return Answer("FIN", subject, InboxLetter.Frame(gs, "财务部", "财务汇报", subject, body.ToString(),
                       "本封为通报，无需批复。", null, needDecision: false),
                $"现金 {gs.cash:N0} 万 · 跑道 {runway:F1} 个月 · 月净 {(net >= 0 ? "+" : "")}{net:N0} 万");
        }

        static DeptAnswer HumanResources(GameState gs)
        {
            var hr = HrManager.EnsureState(gs);
            var sb = new StringBuilder();
            int totalGap = 0;
            int cabinGap = 0;

            sb.Append(InboxLetter.Section("运行岗编制"));
            for (int i = 0; i < CrewPosts.Length; i++)
            {
                string post = CrewPosts[i];
                if (!HrManager.RecruitRoomLeft(gs, post, out int est, out int onDuty, out int inFlight, out int room))
                    continue;
                int gap = Math.Max(0, room);
                totalGap += gap;
                if (post == "乘务员") cabinGap = gap;
                sb.Append(InboxLetter.Bullet(
                    $"{post}：编制 {est} 人 · 在职 {onDuty} · 在途 {inFlight} · 缺口 {gap} 人"));
            }

            sb.Append(InboxLetter.Section("队伍状态"));
            sb.Append(InboxLetter.Row("员工满意度", $"{hr.employeeSatisfaction:F0} / 100"));
            sb.Append(InboxLetter.Row("空缺率", $"{hr.vacancyRate * 100f:F1}%"));
            sb.Append(InboxLetter.Row("罢工风险", $"{hr.strikeRisk:F0} / 100"));
            sb.Append(InboxLetter.Bullet(totalGap <= 0
                ? "运行岗当前满编，可按现有机队执行班次。"
                : $"合计缺口 {totalGap} 人；缺口未补齐前，加密班次会因机组不足排不出。建议先走「人力 → 招聘」。"));
            sb.Append(InboxLetter.Bullet("口径：编制由在役机队规模派生，「缺口 = 编制 − 在职 − 在途」，与招聘页「运行岗缺口」同源。"));

            string subject = $"【汇报】运行岗编制：合计缺口 {totalGap} 人（乘务员 {cabinGap} 人）";
            return Answer("HR", subject, InboxLetter.Frame(gs, "人力资源部", "编制答复", subject, sb.ToString(),
                       "本封为通报，无需批复。", null, needDecision: false),
                $"运行岗缺口 {totalGap} 人 · 乘务员 {cabinGap} 人 · 满意度 {hr.employeeSatisfaction:F0}");
        }

        // ———————————————————————— 机队 ————————————————————————

        static DeptAnswer Fleet(GameState gs)
        {
            int inService = FleetManager.InServiceCount(gs);
            int airworthy = 0, maint = 0, retired = 0;
            float ageSum = 0f;
            int ageCount = 0;
            if (gs.fleet != null)
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var a = gs.fleet[i];
                    if (a == null) continue;
                    if (a.status == AircraftStatuses.Retired) { retired++; continue; }
                    if (a.status == AircraftStatuses.Maintenance) maint++;
                    else airworthy++;
                    ageSum += a.age;
                    ageCount++;
                }
            }

            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section("机队规模"));
            sb.Append(InboxLetter.Row("在册飞机", $"{inService} 架"));
            sb.Append(InboxLetter.Row("可执行航班", $"{airworthy} 架"));
            sb.Append(InboxLetter.Row("维护中", $"{maint} 架"));
            sb.Append(InboxLetter.Row("已退役", $"{retired} 架"));
            sb.Append(InboxLetter.Row("平均机龄", ageCount > 0 ? $"{ageSum / ageCount:F1} 年" : "—"));
            sb.Append(InboxLetter.Section("成本与处置"));
            sb.Append(InboxLetter.Row("机队估值", $"{FleetManager.FleetTotalValue(gs):N0} 万美元"));
            sb.Append(InboxLetter.Row("月维护成本", $"{gs.FleetMonthlyMaintenance:N0} 万美元"));
            sb.Append(InboxLetter.Row("月租金", $"{gs.FleetMonthlyLease:N0} 万美元"));
            sb.Append(InboxLetter.Bullet(airworthy > 0
                ? $"当前有 {airworthy} 架可投入新航线；开通新线前请先确认航程与座位匹配。"
                : "当前没有可自由支配的适航飞机，新增运力须先引进或等检修出厂。"));

            string subject = $"【汇报】机队现状：在册 {inService} 架 · 可执行 {airworthy} 架";
            return Answer(CeoCommandRouter.DeptFleet, subject,
                InboxLetter.Frame(gs, "机队发展部", "机队汇报", subject, sb.ToString(),
                    "本封为通报，无需批复。", null, needDecision: false),
                $"在册 {inService} 架 · 可执行 {airworthy} 架 · 维护中 {maint} 架");
        }

        // ———————————————————————— 运营 ————————————————————————

        static DeptAnswer Operations(GameState gs)
        {
            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int weekly = 0, loss = 0;
            float daily = 0f;
            var worst = new List<RouteData>();
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    weekly += r.WeeklyFlightsDeparture();
                    daily += r.dailyProfit;
                    if (r.dailyProfit < 0f) { loss++; worst.Add(r); }
                }
            }
            worst.Sort((a, b) => a.dailyProfit.CompareTo(b.dailyProfit));

            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section("航线与班次"));
            sb.Append(InboxLetter.Row("在营航线", $"{routes} 条"));
            sb.Append(InboxLetter.Row("每周离港班次", $"{weekly} 班"));
            sb.Append(InboxLetter.Row("准点率", $"{gs.onTimePercent:F1}%"));
            sb.Append(InboxLetter.Row("日利润合计", $"{daily:N1} 万美元"));
            sb.Append(InboxLetter.Section("亏损线"));
            if (worst.Count == 0)
            {
                sb.Append(InboxLetter.Bullet("当前没有日利为负的航线。"));
            }
            else
            {
                for (int i = 0; i < worst.Count && i < 3; i++)
                {
                    var r = worst[i];
                    sb.Append(InboxLetter.Bullet(
                        $"{r.origin}—{r.destination}：日利 {r.dailyProfit:N1} 万 · {r.DualWeeklyLine()}"));
                }
                sb.Append(InboxLetter.Bullet("如需改班，请在「航线 → 单航线」下达运力调令；命令台本封不改任何班次。"));
            }

            string subject = $"【汇报】运营现状：{routes} 条航线 · 周 {weekly} 班 · 准点 {gs.onTimePercent:F1}%";
            return Answer("OPS", subject,
                InboxLetter.Frame(gs, "运营部", "运营汇报", subject, sb.ToString(),
                    "本封为通报，无需批复。", null, needDecision: false),
                $"航线 {routes} 条 · 周 {weekly} 班 · 准点 {gs.onTimePercent:F1}% · 亏损线 {loss} 条");
        }

        // ———————————————————————— 市场 ————————————————————————

        static DeptAnswer Market(GameState gs)
        {
            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section("市场读数"));
            sb.Append(InboxLetter.Row("平均客座率", $"{gs.loadFactorPercent:F1}%"));
            sb.Append(InboxLetter.Row("品牌认知", $"{gs.brandAwareness:F0} / 100"));
            sb.Append(InboxLetter.Row("在营航线", $"{(gs.activeRoutes != null ? gs.activeRoutes.Count : 0)} 条"));
            sb.Append(InboxLetter.Bullet(gs.loadFactorPercent < 70f
                ? "客座率偏低，可先看票价与班期是否与需求错位。"
                : "客座率在健康区间，票价策略可维持。"));
            sb.Append(InboxLetter.Bullet("具体的票价与产品调整在「市场销售部」页办理；本封只报读数。"));

            string subject = $"【汇报】市场读数：客座率 {gs.loadFactorPercent:F1}% · 品牌 {gs.brandAwareness:F0}";
            return Answer("MKT", subject,
                InboxLetter.Frame(gs, "市场销售部", "市场汇报", subject, sb.ToString(),
                    "本封为通报，无需批复。", null, needDecision: false),
                $"客座率 {gs.loadFactorPercent:F1}% · 品牌 {gs.brandAwareness:F0} · 航线 {(gs.activeRoutes != null ? gs.activeRoutes.Count : 0)} 条");
        }

        // ———————————————————————— 机务 ————————————————————————

        static DeptAnswer Maintenance(GameState gs)
        {
            var mnt = MaintenanceManager.EnsureState(gs);
            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section("机队可用性"));
            sb.Append(InboxLetter.Row("维修策略目标可用率", $"{mnt.fleetAvailability:F1}%"));
            sb.Append(InboxLetter.Row("停飞飞机", $"{mnt.groundedCount} 架"));
            sb.Append(InboxLetter.Row("维修差错率", $"{mnt.errorRate:F1}%"));
            sb.Append(InboxLetter.Row("持证工程师", $"{mnt.engineerCount} 人"));
            sb.Append(InboxLetter.Bullet(mnt.groundedCount > 0
                ? $"当前 {mnt.groundedCount} 架停飞，机务正在排检修；可用运力以「机队」页为准。"
                : "当前没有停飞飞机，可用运力充足。"));

            string subject = $"【汇报】机务现状：目标可用率 {mnt.fleetAvailability:F1}% · 停飞 {mnt.groundedCount} 架";
            return Answer("MNT", subject,
                InboxLetter.Frame(gs, "机务工程部", "机务汇报", subject, sb.ToString(),
                    "本封为通报，无需批复。", null, needDecision: false),
                $"目标可用率 {mnt.fleetAvailability:F1}% · 停飞 {mnt.groundedCount} 架");
        }

        // ———————————————————————— 战略（非可行性） ————————————————————————

        static DeptAnswer Strategy(GameState gs)
        {
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section("战略视角"));
            sb.Append(InboxLetter.Row("在营航线", $"{(gs.activeRoutes != null ? gs.activeRoutes.Count : 0)} 条"));
            sb.Append(InboxLetter.Row("在册机队", $"{FleetManager.InServiceCount(gs)} 架"));
            sb.Append(InboxLetter.Row("董事会满意度", $"{gs.boardSatisfaction:F0} / 100"));
            sb.Append(InboxLetter.Row("品牌认知", $"{gs.brandAwareness:F0} / 100"));
            sb.Append(InboxLetter.Row("现金跑道", CashRunnelCalculator.FormatPlayerLabel(runway)));
            sb.Append(InboxLetter.Bullet("要评估某条新线的可行性，请直接说：分析开通北京—纽约航线的可行性。"));

            string subject = "【汇报】战略视角：扩张空间与董事会态度";
            return Answer("STG", subject,
                InboxLetter.Frame(gs, "战略发展部", "战略汇报", subject, sb.ToString(),
                    "本封为通报，无需批复。", null, needDecision: false),
                $"航线 {(gs.activeRoutes != null ? gs.activeRoutes.Count : 0)} 条 · 机队 {FleetManager.InServiceCount(gs)} 架 · 跑道 {runway:F1} 个月");
        }
    }
}
