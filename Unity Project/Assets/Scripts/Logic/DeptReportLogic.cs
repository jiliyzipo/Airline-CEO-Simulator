using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 命令台的**答复组装**：把一句已分派好的话，变成某个部门写给 CEO 的一封汇报件。
    ///
    /// 与 <see cref="CeoCommandRouter"/> 分开的理由：路由是「谁来回」，这里是「回什么」。
    /// 路由要能在回归里逐句断言（纯文本进、枚举出），答复则要读台账；混在一起就没法单独测。
    ///
    /// 读类指令一律**不改台账**（汇报与分析）。
    ///
    /// 唯一的写入口是关线：本类**只负责出确认卡与事后公函**，真正的台账改动在
    /// <see cref="RouteCloseLogic.Execute"/>。这样「谁改了账」永远只有一个答案，
    /// 而命令条那一步拦得住误解析 —— 一句话认错机场，代价是一条在营航线。
    /// </summary>
    public static class DeptReportLogic
    {
        /// <summary>
        /// 命令条「取消」确认卡时显示的原文（T05.5）。
        /// 抽成常量是为了让回归能断言**取消路径的对外说法**：
        /// 取消只收起卡片、不写台账，这句话必须同时声明「未停飞」与「台账未改动」。
        /// </summary>
        public const string CloseCancelNotice = "已取消：未停飞任何航线，台账未改动。";

        public struct DeptAnswer
        {
            public bool ok;         // false = 不派件，只把 note 显示在命令条上

            /// <summary>true = 命令条弹确认卡，**不投递任何邮件**；CEO 点确认后由调用方执行。</summary>
            public bool needsConfirm;
            public string confirmTitle;
            public string confirmBody;
            /// <summary>
            /// true = 命令条**调起既有管线**（运力调令）：本结构体只带路由信息，
            /// 执行与投递都由 <see cref="RunCapacityDirective"/> 一次办完。
            ///
            /// 这里刻意不再加一张确认卡：运力管线的产物本来就是「呈报件」——
            /// CEO 要在收件箱批一次，批错了还能退回（Revert）。再叠一道确认，等于同一件事问两遍。
            /// </summary>
            public bool needsRun;
            /// <summary>
            /// 写指令的参数快照：<see cref="Build"/> 收到什么就原样存这里，
            /// <see cref="RunPending"/> 执行时**只认这一份**，不再读命令条上的输入框 ——
            /// 否则 CEO 在确认前改了草稿，执行的就是另一句话了。
            /// </summary>
            public CeoCommandRouter.CeoAsk ask;
            /// <summary>
            /// true = 这是一句**关于某条具体航线**的询问：交运营部按本线台账写答复件
            /// （汇报件，无需批复），不走「部门总账」那张汇报表。
            /// </summary>
            public bool needsQuery;
            public string routeKey; // 确认/执行/答复要落到的那条航线（routeId，不是机场对）

            public string deptCode;
            public string sender;
            public string role;
            public string glyph;
            public string subject;
            public string body;
            public string oneLine;  // 命令条上的一行结论（全文进收件箱）
            public string note;
        }

        public static DeptAnswer Build(GameState gs, CeoCommandRouter.CeoAsk ask) => Build(gs, ask, null);

        /// <summary>
        /// <paramref name="raw"/> 只给「要读原话」的写指令用（运力调令的解析在
        /// <see cref="CapacityDirectiveLogic"/> 里，不在路由里）。读类答复不需要它，故可省。
        /// </summary>
        public static DeptAnswer Build(GameState gs, CeoCommandRouter.CeoAsk ask, string raw)
        {
            if (gs == null) return Fail("游戏尚未就绪。");
            if (!ask.ok) return Fail(string.IsNullOrEmpty(ask.note) ? "没听懂这句话。" : ask.note);

            if (ask.topic == CeoCommandRouter.TopicCloseRoute) return CloseRouteReview(gs, ask);
            if (ask.topic == CeoCommandRouter.TopicCapacity) return CapacityDirective(gs, ask, raw);
            if (ask.topic == CeoCommandRouter.TopicRotation) return RotationDirective(gs, ask, raw);
            if (ask.topic == CeoCommandRouter.TopicOpenRoute) return OpenRouteDirective(ask);
            if (ask.topic == CeoCommandRouter.TopicFleetBuy) return FleetBuyDirective(ask);
            if (ask.topic == CeoCommandRouter.TopicHrRecruit) return HrRecruitDirective(ask);
            if (ask.topic == CeoCommandRouter.TopicFinance) return FinanceDirective(ask);
            if (ask.topic == CeoCommandRouter.TopicRouteQuery) return RouteQuery(gs, ask, raw);
            if (ask.topic == CeoCommandRouter.TopicFeasibility) return Feasibility(gs, ask);
            if (ask.topic == CeoCommandRouter.TopicCompany) return CompanyOverview(gs);

            switch (ask.deptCode)
            {
                case "FIN": return Finance(gs);
                case "HR": return HumanResources(gs);
                case CeoCommandRouter.DeptFleet: return Fleet(gs);
                case "MKT": return Market(gs);
                case "MNT": return Maintenance(gs);
                case "STG": return Strategy(gs);
                default: return Operations(gs);
            }
        }

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

        // ———————————————————————— 人力 ————————————————————————

        static readonly string[] CrewPosts = { "飞行员", "乘务员", "机务工程师" };

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
                        + "请到「战略 → 航权与时刻槽」申请/竞标，批复后再回「开通新航线」递件。"));
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

            bool ok = InboxManager.QueueCapacityReport(gs, route, rep, -1, true);
            // 挂起「原话 → 解析结果」：CEO 在收件箱批准后才真正成为记忆（与页面同源）
            DirectiveLexicon.Stage(d.raw, d, d.fromAi);

            string ring = string.IsNullOrEmpty(rep.ringIdsCsv) ? "未编环" : "拟定航班环 " + rep.ringIdsCsv;
            receipt = ok
                ? $"{CapacityDirectiveLogic.RegsOf(route)} · {route.DualWeeklyLine()} · {ring}"
                  + $" · 运营部已受理：约 {InboxCadence.StudyBusinessDays} 个工作日研究后进收件箱"
                  + "（班次此刻未动，批准后立即排班）"
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
                case CeoCommandRouter.TopicOpenRoute:
                    return RunOpenRoute(gs, ans.ask, out receipt);
                case CeoCommandRouter.TopicFleetBuy:
                    return RunFleetBuy(gs, ans.ask, out receipt);
                case CeoCommandRouter.TopicHrRecruit:
                    return RunHrRecruit(gs, ans.ask, out receipt);
                case CeoCommandRouter.TopicFinance:
                    return RunFinanceRequest(gs, ans.ask, out receipt);
            }
            receipt = "这条指令没有可执行的管线。";
            return false;
        }

        static bool RunOpenRoute(GameState gs, CeoCommandRouter.CeoAsk ask, out string receipt)
        {
            // 与页面「提交研究」同一个组装函数：同一条航线不会有两个版本的研究会纪要。
            var plan = OpenRouteStudyLogic.Build(gs, ask.origin, ask.dest, ask.via);
            if (!plan.ok)
            {
                receipt = string.IsNullOrEmpty(plan.reject) ? "这条航线不成立，未发请示。" : plan.reject;
                return false;
            }
            bool ok = InboxManager.QueueOpenRouteStudy(gs, ask.origin, ask.dest, ask.via,
                plan.subject, plan.body);
            receipt = ok
                ? $"{plan.path}：开航研究会已在收件箱呈报（{plan.rpm.score} 分，"
                  + $"约 {InboxCadence.StudyBusinessDays} 个工作日后到件；批准后仍须办妥准入）"
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

        static bool TryResolveRoute(GameState gs, CeoCommandRouter.CeoAsk ask, string raw, out RouteData route)
        {
            route = null;
            // 航班号优先：一个机场对可以开好几班，班号是唯一能分辨的那一格。
            // ★点名的班号在台账里不存在时**直接失败**，不再退回按城对猜 ——
            //   否则「把 NV9999 加密到每天 2 班」会在同城对里随便挑一条改掉班次。
            if (!string.IsNullOrEmpty(ask.flightNumber))
                return (route = FindRouteByFlightNumber(gs, ask.flightNumber)) != null;
            if (!string.IsNullOrEmpty(ask.origin) && !string.IsNullOrEmpty(ask.dest))
                route = FindRoute(gs, ask.origin, ask.dest);
            if (route == null) route = FindRouteByName(gs, ask.originText, ask.destText);
            // 页面上下文：单航线页上不带城对的调令（「加密到每天 3 班」）落到 CEO 正看着的那条线。
            // 排在显式城对/班号之后 —— 上下文只兜底，绝不覆盖 CEO 亲口点明的对象。
            if (route == null && !string.IsNullOrEmpty(ask.contextRouteKey))
                route = RouteCloseLogic.FindByRouteId(gs, ask.contextRouteKey);
            // 原话里带班号但路由没截出来（例如「NV1001 周三不排班」的写法）：再用原话扫一遍
            if (route == null && !string.IsNullOrEmpty(raw))
            {
                string fn = FlightNumberIn(gs, raw);
                if (!string.IsNullOrEmpty(fn)) route = FindRouteByFlightNumber(gs, fn);
            }
            return route != null;
        }

        static RouteData FindRouteByFlightNumber(GameState gs, string flightNumber)
        {
            if (gs?.activeRoutes == null || string.IsNullOrEmpty(flightNumber)) return null;
            string want = flightNumber.Trim();
            // ① 基号（航线抬头写的那个：FR106 / NS100）
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null || string.IsNullOrEmpty(r.flightNumber)) continue;
                if (string.Equals(r.flightNumber.Trim(), want, StringComparison.OrdinalIgnoreCase))
                    return r;
            }
            // ② 逐班号：第 w 个波次的去程 = 基号 + 2w、回程 = 基号 + 2w + 1。
            //    CEO 是从「周计划 / 周总计划」抄号来问的，那张表上的号按波次递增 ——
            //    只认基号就会把「NV2003 周三不排班」判成「没有这条航线」（2026-10-21 报障）。
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                r.EnsureFlights();
                int waves = r.flights != null ? r.flights.Count : 0;
                for (int w = 0; w < waves; w++)
                {
                    FlightScheduleFormat.Numbers(r, w, out string outNo, out string inNo);
                    if (string.Equals(outNo, want, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(inNo, want, StringComparison.OrdinalIgnoreCase))
                        return r;
                }
            }
            return null;
        }

        static string FlightNumberIn(GameState gs, string raw)
        {
            if (gs?.activeRoutes == null || string.IsNullOrEmpty(raw)) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                if (!string.IsNullOrEmpty(r.flightNumber) && IndexIgnoreCase(raw, r.flightNumber.Trim()) >= 0)
                    return r.flightNumber;
            }
            // 原话里可能抄的是第 w 个波次的号：逐个比对，命中即认
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                r.EnsureFlights();
                int waves = r.flights != null ? r.flights.Count : 0;
                for (int w = 0; w < waves; w++)
                {
                    FlightScheduleFormat.Numbers(r, w, out string outNo, out string inNo);
                    if (IndexIgnoreCase(raw, outNo) >= 0) return outNo;
                    if (IndexIgnoreCase(raw, inNo) >= 0) return inNo;
                }
            }
            return null;
        }

        static int IndexIgnoreCase(string haystack, string needle)
            => string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)
                ? -1
                : haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);

        static string RouteLabelOf(CeoCommandRouter.CeoAsk ask)
        {
            string o = !string.IsNullOrEmpty(ask.originText) ? ask.originText : ask.origin;
            string d = !string.IsNullOrEmpty(ask.destText) ? ask.destText : ask.dest;
            if (string.IsNullOrEmpty(o) && string.IsNullOrEmpty(d)) return "（未指明）";
            return $"{o}—{d}";
        }

        // ———————————————— 单航线询问（问，不是令） ————————————————

        /// <summary>
        /// 命令台的「问某条线」：只认线，不改任何台账、不发总账。
        ///
        /// 为什么必须单独一条路：问句落进部门词表后由 <see cref="Operations"/> 答，
        /// 那是一张「航线 N 条 / 周 M 班 / 亏损线」的总表 —— 对「北京—上海周三为什么不排班」
        /// 一个字都答不到（2026-10-21 报障原话）。真正的答复在
        /// <see cref="OpsQueryLogic.Build"/>：它按本线班期表、运力预检、时刻槽逐段对台账答。
        /// </summary>
        static DeptAnswer RouteQuery(GameState gs, CeoCommandRouter.CeoAsk ask, string raw)
        {
            if (!TryResolveRoute(gs, ask, raw, out RouteData route))
                return Fail(DescribeMissingRoute(gs, ask, asking: true));

            int samePair = CountRoutePair(gs, route.origin, route.destination);
            if (samePair > 1 && string.IsNullOrEmpty(ask.flightNumber))
            {
                string eg = FirstFlightNumberOf(gs, route.origin, route.destination);
                return Fail($"{route.origin}—{route.destination} 有 {samePair} 条在营航线"
                            + $"（{FlightNumbersOf(gs, route.origin, route.destination)}），"
                            + $"请指明班号{(string.IsNullOrEmpty(eg) ? "" : "（例：" + eg + "）")}；"
                            + "本次未作答复，也未改动台账。");
            }

            return new DeptAnswer
            {
                ok = true,
                needsQuery = true,
                routeKey = route.routeId,
                deptCode = "OPS",
                sender = "运营部",
                role = "排班咨询",
                glyph = "运",
                subject = $"运营部答复：{route.origin}—{route.destination} 排班咨询",
                oneLine = $"{route.origin}—{route.destination}"
            };
        }

        /// <summary>投递答复件（与单航线页底部提问走的是同一个 <see cref="InboxManager.QueueOpsQueryAnswer"/>）。</summary>
        public static bool RunRouteQuery(GameState gs, string routeKey, string raw, out string receipt)
        {
            receipt = "";
            if (gs == null) { receipt = "游戏尚未就绪。"; return false; }

            var route = RouteCloseLogic.FindByRouteId(gs, routeKey);
            if (route == null)
            {
                receipt = "这条航线已不在运营，无法按本线台账答复。";
                return false;
            }

            var ans = OpsQueryLogic.Build(gs, route, raw);   // 纯读：只用来取话题标题
            bool ok = InboxManager.QueueOpsQueryAnswer(gs, route, raw, out string msg);
            receipt = ok
                ? $"{route.origin}—{route.destination}：已就「{OpsQueryLogic.TopicTitles(ans.topics)}」"
                  + "按本线台账答复（收件箱 · 汇报件，无需批复）；班次与机号未改动"
                : $"未能发出答复：{msg}";
            return ok;
        }

        /// <summary>认不出航线时把话说清楚：缺什么、台账里**真实存在**的班号与城对是什么。</summary>
        static string DescribeMissingRoute(GameState gs, CeoCommandRouter.CeoAsk ask, bool asking)
        {
            bool nothing = string.IsNullOrEmpty(ask.origin) && string.IsNullOrEmpty(ask.dest)
                           && string.IsNullOrEmpty(ask.originText) && string.IsNullOrEmpty(ask.destText)
                           && string.IsNullOrEmpty(ask.flightNumber);
            if (nothing)
                return asking
                    ? "要先知道问的是哪条航线。例：为什么北京—上海周三不排班 / 北京—上海现在几班。"
                    : "调令要先知道是哪条航线。例：把北京—上海加密到每天 3 班。";

            string action = asking ? "本次未作答" : "本次未执行";
            // 点名的班号不存在：只报**真实**班号，别再举虚构例子 ——
            // 旧版把示例写死成 NV1001，CEO 照着抄一遍，系统又说「没有这条航线」（2026-10-21 报障）。
            if (!string.IsNullOrEmpty(ask.flightNumber))
            {
                bool pairKnown = !string.IsNullOrEmpty(ask.origin) && !string.IsNullOrEmpty(ask.dest);
                string pool = pairKnown
                    ? FlightNumbersOf(gs, ask.origin, ask.dest)
                    : ExistingFlightList(gs, 4);
                if (!string.IsNullOrEmpty(pool))
                    return (pairKnown
                        ? $"{ask.origin}—{ask.dest} 现有班号：{pool}，没有「{ask.flightNumber}」；"
                        : $"台账里没有「{ask.flightNumber}」这个班号；现有班次：{pool}。")
                        + $"{action}，也未改动任何台账。请照抄上面列出的班号，或到「航线」页核对。";
                // 台账里根本没有班号（旧档/新开线可能没填）：别让 CEO 去找一个不存在的字段
                return $"台账里没有「{ask.flightNumber}」这个班号，且现有航线未登记班号；"
                     + $"请改用「出发地—目的地」提问（例：为什么北京—上海周三不排班）。{action}。";
            }

            int n = gs?.activeRoutes?.Count ?? 0;
            string existing = ExistingPairList(gs, 4);
            return $"台账里没有「{RouteLabelOf(ask)}」这条在营航线（现有 {n} 条{existing}），{action}也未改动任何台账。"
                 + "可在「航线」页核对线路名或航班号。";
        }

        /// <summary>现有在营城对的前几对（用三字码，和航线页抬头同一套写法），帮 CEO 把名字对上。</summary>
        static string ExistingPairList(GameState gs, int max)
        {
            if (gs?.activeRoutes == null || gs.activeRoutes.Count == 0) return "";
            var list = new List<string>();
            for (int i = 0; i < gs.activeRoutes.Count && list.Count < max; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                string pair = $"{r.origin}—{r.destination}";
                if (!list.Contains(pair)) list.Add(pair);
            }
            return list.Count == 0 ? "" : "：" + string.Join("、", list)
                                          + (gs.activeRoutes.Count > list.Count ? " 等" : "");
        }

        static string FlightNumbersOf(GameState gs, string a, string b)
        {
            var list = new List<string>();
            if (gs?.activeRoutes != null)
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null || !SamePair(r, a, b)) continue;
                    string no = RouteNumberLabel(r);
                    if (string.IsNullOrEmpty(no)) continue;
                    list.Add($"{no}（{r.depTime}）");
                }
            return string.Join("、", list);
        }

        /// <summary>
        /// 一条线的班号概览：`NS100/NS101`（多波次补「共 N 个波次」）。
        ///
        /// 用**逐班号**而不是 `route.flightNumber`：CEO 是从周计划上抄号来问的，
        /// 那张表第 2 波次写的是基号 +2，与航线抬头那个基号并不相同。
        /// </summary>
        static string RouteNumberLabel(RouteData r)
        {
            if (r == null) return "";
            r.EnsureFlights();
            int waves = r.flights != null ? r.flights.Count : 0;
            if (waves <= 0)
                return string.IsNullOrEmpty(r.flightNumber) ? "" : r.flightNumber.Trim();
            FlightScheduleFormat.Numbers(r, 0, out string outNo, out string inNo);
            return waves > 1 ? $"{outNo}/{inNo}…（共 {waves} 个波次）" : $"{outNo}/{inNo}";
        }

        /// <summary>该城对第一个真实班号（用于把示例写成台账里有的号，而不是虚构的 NV1001）。</summary>
        static string FirstFlightNumberOf(GameState gs, string a, string b)
        {
            if (gs?.activeRoutes == null) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null || !SamePair(r, a, b)) continue;
                r.EnsureFlights();
                if (r.flights != null && r.flights.Count > 0)
                {
                    FlightScheduleFormat.Numbers(r, 0, out string outNo, out string _);
                    if (!string.IsNullOrEmpty(outNo)) return outNo;
                }
                if (!string.IsNullOrEmpty(r.flightNumber)) return r.flightNumber.Trim();
            }
            return null;
        }

        /// <summary>全台账前几个班次「班号（城对 · 离港）」，认不出班号时让 CEO 照抄。</summary>
        static string ExistingFlightList(GameState gs, int max)
        {
            if (gs?.activeRoutes == null) return "";
            var list = new List<string>();
            for (int i = 0; i < gs.activeRoutes.Count && list.Count < max; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                string no = RouteNumberLabel(r);
                if (string.IsNullOrEmpty(no)) continue;
                string one = $"{no}（{r.origin}—{r.destination} · {r.depTime}）";
                if (!list.Contains(one)) list.Add(one);
            }
            if (list.Count == 0) return "";
            return string.Join("、", list) + (gs.activeRoutes.Count > list.Count ? " 等" : "");
        }

        /// <summary>
        /// 把「在营航线的机场名」接成路由器的补充词表。
        /// 机场目录是死表，玩家的航线是活表：只要这条线在飞，它的两端名字就该被认出来。
        /// </summary>
        public static CeoCommandRouter.PieceResolver RouteNameResolver(GameState gs)
        {
            if (gs?.activeRoutes == null || gs.activeRoutes.Count == 0) return null;
            return piece =>
            {
                if (string.IsNullOrEmpty(piece) || piece.Length < 2) return null;
                string hit = null;
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    hit = EndpointHit(r.origin, piece, hit);
                    hit = EndpointHit(r.destination, piece, hit);
                    if (hit == Ambiguous) return null;
                }
                return hit;
            };
        }

        const string Ambiguous = "?";

        static string EndpointHit(string code, string piece, string hit)
        {
            if (string.IsNullOrEmpty(code)) return hit;
            bool match = string.Equals(code, piece, StringComparison.OrdinalIgnoreCase);
            if (!match)
            {
                string label = AirportCatalog.Label(code);
                match = !string.IsNullOrEmpty(label)
                        && (string.Equals(label, piece, StringComparison.Ordinal)
                            || label.IndexOf(piece, StringComparison.Ordinal) >= 0);
            }
            if (!match) return hit;
            if (hit == null) return code;
            return string.Equals(hit, code, StringComparison.OrdinalIgnoreCase) ? hit : Ambiguous;
        }

        static RouteData FindRoute(GameState gs, string a, string b)
        {
            if (gs?.activeRoutes == null) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                if ((string.Equals(r.origin, a, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(r.destination, b, StringComparison.OrdinalIgnoreCase))
                    || (string.Equals(r.origin, b, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.destination, a, StringComparison.OrdinalIgnoreCase)))
                    return r;
            }
            return null;
        }

        /// <summary>三字码没解析出来时，按 CEO 原话里的地名去在营航线里再捞一次。</summary>
        static RouteData FindRouteByName(GameState gs, string originText, string destText)
        {
            if (gs?.activeRoutes == null) return null;
            if (string.IsNullOrEmpty(originText) || string.IsNullOrEmpty(destText)) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                bool a = NameMatches(r.origin, originText) && NameMatches(r.destination, destText);
                bool b = NameMatches(r.origin, destText) && NameMatches(r.destination, originText);
                if (a || b) return r;
            }
            return null;
        }

        static bool NameMatches(string code, string text)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(text)) return false;
            if (string.Equals(code, text, StringComparison.OrdinalIgnoreCase)) return true;
            string label = AirportCatalog.Label(code);
            if (string.IsNullOrEmpty(label)) return false;
            return string.Equals(label, text, StringComparison.Ordinal)
                || label.IndexOf(text, StringComparison.Ordinal) >= 0
                || text.IndexOf(label, StringComparison.Ordinal) >= 0;
        }

        // ———————————————————————— 公共 ————————————————————————

        /// <summary>
        /// 日利润的**唯一**排版：正文那一行与命令台回执必须是同一个字符串。
        /// 任何地方再写一次 `:N1 万`，就等于给自己留了一个「同一封函两个数」的口子。
        /// </summary>
        static string ProfitText(float dailyProfitWan) => $"{dailyProfitWan:N1} 万美元";

        static DeptAnswer Answer(string dept, string subject, string body, string oneLine)
        {
            var a = new DeptAnswer
            {
                ok = true,
                deptCode = dept,
                sender = SenderOf(dept),
                role = RoleOf(dept),
                glyph = GlyphOf(dept),
                subject = subject,
                body = body,
                oneLine = oneLine
            };
            return a;
        }

        static DeptAnswer Fail(string note) => new DeptAnswer { ok = false, note = note };

        public static string SenderOf(string dept)
        {
            switch (dept)
            {
                case CeoCommandRouter.DeptAdvisor: return "航空顾问";
                case "FIN": return "财务部";
                case "HR": return "人力资源部";
                case CeoCommandRouter.DeptFleet: return "机队发展部";
                case "MKT": return "市场销售部";
                case "MNT": return "机务工程部";
                case "STG": return "战略发展部";
                case "SAF": return "安全质量部";
                default: return "运营部";
            }
        }

        public static string RoleOf(string dept)
        {
            switch (dept)
            {
                case CeoCommandRouter.DeptAdvisor: return "公司简报";
                case "FIN": return "财务汇报";
                case "HR": return "编制答复";
                case CeoCommandRouter.DeptFleet: return "机队汇报";
                case "MKT": return "市场汇报";
                case "MNT": return "机务汇报";
                case "STG": return "战略汇报";
                case "SAF": return "安全汇报";
                default: return "运营汇报";
            }
        }

        static string GlyphOf(string dept)
        {
            switch (dept)
            {
                case CeoCommandRouter.DeptAdvisor: return "顾";
                case "FIN": return "财";
                case "HR": return "人";
                case CeoCommandRouter.DeptFleet: return "机";
                case "MKT": return "市";
                case "MNT": return "维";
                case "STG": return "战";
                case "SAF": return "安";
                default: return "运";
            }
        }
    }
}
