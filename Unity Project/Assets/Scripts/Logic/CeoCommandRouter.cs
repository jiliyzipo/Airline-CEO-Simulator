using System;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// CEO 命令台（底栏输入框）的**意图分派器**：一句话 → 交给哪个部门、是问还是办。
    ///
    /// 三条刻意写死的规矩：
    /// <list type="number">
    /// <item><b>先认事、再认页</b>。当前页面只当「默认值」和「提示语」，不当过滤器 ——
    ///   站在人事页问「汇报财务状况」也必须由财务部答，不能因为上面开着人事页就派给人力。
    ///   页面唯一的作用是：这句话里看不出任何部门时，按当前页兜底。</item>
    /// <item><b>执行类指令必须能被认出来</b>。命令台既有「读」（汇报/分析）也有「写」
    ///   （关线 / 运力调令），所以「取消某某航线」「加密到每天 3 班」绝不能被默默当成
    ///   「运营部汇报航线」—— 那会让 CEO 以为事情已经办了。认成
    ///   <see cref="CeoAskKind.Act"/> 后交对应管线：关线走确认卡，运力调令走呈报件。</item>
    /// <item><b>纯函数</b>。不读 GameState、不碰 UI、不发邮件：这样路由规则可以在
    ///   Editor 隔离回归里逐句钉住，不必把壳层跑起来。</item>
    /// </list>
    /// </summary>
    public static class CeoCommandRouter
    {
        /// <summary>机队发展部：不在 <see cref="DepartmentCatalog"/> 的七部门里，但收件箱一直在用它发函。</summary>
        public const string DeptFleet = "FLT";
        /// <summary>
        /// 航空顾问（顶栏「顾问」那位）：**不在任何部门的序列里**，也不该被塞进某个部门的账本。
        /// 只有「公司整体」这一类问题由他答 —— 那是一份跨部门的判断，任何单一部门都答不了。
        /// </summary>
        public const string DeptAdvisor = "ADV";

        public enum CeoAskKind
        {
            Ask,        // 问一句 → 部门答复（汇报件）
            Analyze,    // 让部门做一份分析（如开通可行性）
            Act         // 让部门**改台账**（第一期不支持，必须明确回绝）
        }

        public struct CeoAsk
        {
            public bool ok;            // false = 没听懂/缺少必要实体，不派件
            public string deptCode;    // OPS/FIN/HR/FLT/MKT/MNT/STG
            public CeoAskKind kind;
            public string topic;       // "" = 该部门概览；feasibility / close_route 为特例
            public string origin;      // 机场三字码（可行性、关线）
            public string dest;
            // 三字码解析不出来时的**原文**（「珠海金湾」这类目录里没有的机场）。
            // 关线复核仍要把这句话原样念回去，不能因为对不上目录就说「你没说目的地」。
            public string originText;
            public string destText;
            /// <summary>
            /// 原话里点名的航班号（NV1002 / FR106）。
            ///
            /// 一个机场对可以开好几班，此时光有城对认不出是哪一条；CEO 说「为什么 NV1002 周三不排班」
            /// 时唯一能定位的线索就是这个号。这里只记原话里的**候选**，真伪由台账去对
            /// （<see cref="DeptReportLogic"/> 按在营航线的 flightNumber 精确匹配），
            /// 路由层不假装自己认识机队。
            /// </summary>
            public string flightNumber;
            /// <summary>
            /// 页面上下文里**已经选中**的航线（routeId）。只在单航线页出现，且只在句子
            /// 有调令信号、又没说城对/班号时使用 —— 见 <see cref="Route(string,string,PieceResolver,string)"/>。
            ///
            /// 它补的是被删掉的那个页面内指令框的位置：那个框靠上下文认线，命令台原来不认。
            /// 空串 = 没有上下文可用，一切照旧（要求城对或班号）。
            /// </summary>
            public string contextRouteKey;
            public string note;        // 未派件时的原因与引导

            // ——— 写指令的参数（全部来自原话，**解析不出就退回引导，不猜**) ———

            /// <summary>经停机场（开航）。暂不从句子里认，留 0 表示直飞。</summary>
            public string via;
            /// <summary>机型 typeId（购机），必须命中 <see cref="AircraftCatalog"/> 里的型号。</summary>
            public string aircraftType;
            /// <summary>采购方式（购机）：自有资金买断 / 融资租赁 / 经营租赁 / 二手引进。</summary>
            public string source;
            /// <summary>岗位（招聘）：飞行员 / 乘务员 / 机务工程师。</summary>
            public string position;
            /// <summary>数量：架数（购机）或人数（招聘）。</summary>
            public int count;
            /// <summary>融资种类：1 银行贷款 / 2 高息过桥 / 3 股东增资。</summary>
            public int financeKind;
            /// <summary>招聘渠道：0 校招 / 1 猎头 / 2 海外引进 / 3 内部转岗。</summary>
            public int channel;
            /// <summary>招聘紧迫度（1 常规 / 2 较急 / 3 紧急）。</summary>
            public int urgency;
        }

        /// <summary>把一个名字片段解析成三字码；解不出返回 null。用于把「在营航线」也当词表。</summary>
        public delegate string PieceResolver(string piece);

        public const string TopicFeasibility = "feasibility";
        public const string TopicCloseRoute = "close_route";
        /// <summary>开航研究会：命令台说「开通北京—纽约」→ 与页面同一个入口，投递开航请示件。</summary>
        public const string TopicOpenRoute = "open_route";
        /// <summary>购机请示：说「买 2 架 A320neo」→ 机队发展部发购机请示件。</summary>
        public const string TopicFleetBuy = "fleet_buy";
        /// <summary>招聘请示：说「招聘 10 名飞行员」→ 人力资源部发招聘请示件。</summary>
        public const string TopicHrRecruit = "hr_recruit";
        /// <summary>融资请示：说「申请银行贷款」→ 财务部发融资请示件。</summary>
        public const string TopicFinance = "finance_request";
        /// <summary>运力调令（加减班 / 改班期 / 换机号）：不自己改账，交给既有运力管线。</summary>
        public const string TopicCapacity = "capacity";
        /// <summary>问某一条**具体航线**的现状（班期/为什么没排/几班/谁飞）：交运营部按本线台账答复。</summary>
        public const string TopicRouteQuery = "route_query";
        /// <summary>公司整体经营（跨部门判断）：由航空顾问答，不归任何单一部门。</summary>
        public const string TopicCompany = "company";

        // —— 词表 ——
        // 顺序即优先级：同一句话可能同时命中两组词，先命中的赢。所以「可行性/关线」必须排在
        // 「航线」前面，否则「分析开通北京—纽约航线的可行性」会被当成一句普通的运营部汇报。
        // ★2026-10-20：补「停飞 / 停航」—— 界面按钮叫「确认停飞」，CEO 自然也这么写；
        //   漏掉这两个词的结果是句子落进「运营部汇报航线」，航线一条没动、却像是受理了。
        static readonly string[] CloseWords =
            { "取消", "关停", "撤销", "撤掉", "停掉", "砍掉", "关闭", "停飞", "停航" };
        static readonly string[] HrWords =
        {
            "乘务", "空乘", "乘务员", "飞行员", "机长", "机务工程师", "编制", "缺口", "招聘",
            "人力", "员工", "满意度", "罢工", "离职", "薪酬", "排班员", "地勤"
        };
        static readonly string[] FinWords =
        {
            "财务", "现金", "资金", "跑道", "盈亏", "盈利", "亏损", "成本", "预算", "负债",
            "贷款", "汇兑", "利润", "营收", "收入", "支出", "烧钱", "报表", "融资", "现金流"
        };
        static readonly string[] FleetWords = { "机队", "飞机数量", "几架", "机龄", "退役", "租机", "购机", "适航" };
        static readonly string[] MarketWords = { "客座", "票价", "收益管理", "销售", "市场", "品牌", "竞争", "需求" };
        static readonly string[] MaintWords = { "维护", "机务", "维修", "定检", "保障", "故障", "可用率" };
        static readonly string[] OpsWords = { "航线", "航班", "班次", "准点", "时刻槽", "机组", "排班", "运力", "延误" };

        /// <summary>
        /// 「改班期」这一族：运力管线的方向词表里没有它们（它们不表达增/减），
        /// 但一样会改台账。故单列一小张表，只在命令台补这一层 —— 前提仍是切得出城对。
        /// 刻意不收裸的「每天 / 每周」：那是**指代**不是命令，「汇报北京—上海每天的日利」
        /// 里也有「每天」，它必须留给财务/市场去答。
        /// </summary>
        static readonly string[] ScheduleVerbs =
            { "改成", "改为", "调整到", "调整为", "调整成", "班期调整", "班期改", "维持", "保持" };

        /// <summary>
        /// 句子里出现这些词，说明 CEO 想动的**不是「这条航线还在不在」**，而是它的某个部件：
        /// 班次频率、班期、执飞机、补贴、时刻槽、航班环。
        ///
        /// 只用于「没写『航线』二字、但切得出城对」的那条关线判据（见 <c>Route</c> 的 ①）：
        /// 「取消北京—上海」该关线，「取消北京—上海每天 2 班」该减班 ——
        /// 两者只差几个字，认错一边的代价分别是「一条线没了」和「一条线没动」，
        /// 所以宁可把带部件的句子让给运力管线，也不猜。
        /// </summary>
        static readonly string[] RoutePartWords =
        {
            "加班", "加密", "减班", "增班", "班次", "班期", "每天", "每日", "每周", "一周",
            "机号", "机型", "换机", "执飞", "补贴", "时刻槽", "槽位", "航班环"
        };

        /// <summary>「取消北京—上海每天 2 班」这类句子带数字班次 —— 那是在改运力，不是关线。</summary>
        static bool TargetsOtherThanRoute(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (HasAny(text, RoutePartWords)) return true;
            // 「每天 2 班」「3 班」：数字 + 班
            for (int i = 0; i < text.Length - 1; i++)
            {
                if (!char.IsDigit(text[i])) continue;
                int j = i;
                while (j < text.Length && (char.IsDigit(text[j]) || text[j] == ' ')) j++;
                if (j < text.Length && text[j] == '班') return true;
            }
            return false;
        }

        /// <summary>
        /// 「这确实是在问一件事」的通用问法：只有它们允许在看不出部门时按当前页兜底。
        ///
        /// 这张表存在的理由与 <see cref="ScheduleVerbs"/> 相反：那张表放行**能办的事**，
        /// 这张表放行**能答的话**。「该城对 2 条线」两样都不是 —— 它没有部门、没有航线、
        /// 也没有在问什么，只值一句引导，不值一封总账。
        /// </summary>
        static readonly string[] AskVerbs =
        {
            "汇报", "报告", "简报", "介绍", "说说", "讲讲", "聊聊", "情况", "概况", "概览",
            "现状", "总结", "怎么样", "如何", "多少", "看看", "帮我", "分析", "评估"
        };

        /// <summary>只有口气、不承载对象的词：「汇报**一下**」里的「一下」不是要汇报的东西。</summary>
        static readonly string[] AskFillers =
        {
            "一下", "下", "吧", "呢", "啊", "呀", "嘛", "现在", "目前", "最近", "近期",
            "整体", "大概", "简要", "简单", "简单说说", "给我", "一份", "一个", "当前", "这边", "那边"
        };

        // ———— 公司整体（跨部门）与七个部门的分界 ————

        /// <summary>明说「整个公司」的写法：无论句子里还提到什么，都由顾问答。</summary>
        static readonly string[] CompanyStrong =
        {
            "全公司", "全司", "整个公司", "公司整体", "公司层面", "公司全貌", "公司全局",
            "经营全貌", "集团整体", "整个集团", "全集团"
        };

        /// <summary>主体词：「公司/集团」+ 下面任一限定词才算问公司整体。</summary>
        static readonly string[] CompanySubjects = { "公司", "企业", "集团", "航司" };

        static readonly string[] CompanyQualifiers =
        {
            "整体", "全面", "全貌", "全局", "全盘", "总体", "现状", "概况", "情况",
            "怎么样", "如何", "经营", "面面"
        };

        public const int CompanyScopeNone = 0;
        public const int CompanyScopeWeak = 1;
        public const int CompanyScopeStrong = 2;

        /// <summary>
        /// 这句话在问「整个公司」吗？
        ///
        /// 刻意**不做**成「出现『整体』就算」：那条规则会让「财务部整体情况」也被顾问抢走，
        /// 而部门账本恰恰是各部门自己最说得清的。所以弱档要求「主体词 + 限定词」同时出现。
        /// </summary>
        public static int CompanyScope(string text)
        {
            if (string.IsNullOrEmpty(text)) return CompanyScopeNone;
            if (HasAny(text, CompanyStrong)) return CompanyScopeStrong;
            if (HasAny(text, CompanySubjects) && HasAny(text, CompanyQualifiers)) return CompanyScopeWeak;
            return CompanyScopeNone;
        }

        /// <summary>七个部门 + 机队发展部的全部关键词（判断「句子里是不是已经指名了某个部门」）。</summary>
        static readonly string[] DeptWords = Concat(HrWords, FinWords, FleetWords, MarketWords, OpsWords, MaintWords);

        static string[] Concat(params string[][] parts)
        {
            int n = 0;
            for (int i = 0; i < parts.Length; i++) n += parts[i] != null ? parts[i].Length : 0;
            var all = new string[n];
            int k = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                for (int j = 0; j < parts[i].Length; j++) all[k++] = parts[i][j];
            }
            return all;
        }

        /// <summary>当前页 → 默认部门（只在句子里看不出部门时兜底）。</summary>
        public static string PageDept(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return "OPS";
            if (pageId.Contains("财务")) return "FIN";
            if (pageId.Contains("人力") || pageId.Contains("人员") || pageId.Contains("培训")
                || pageId.StartsWith("n18", StringComparison.Ordinal)) return "HR";
            if (pageId.Contains("机队") || pageId.Contains("飞机") || pageId.Contains("购机")
                || pageId.StartsWith("n9_", StringComparison.Ordinal)) return DeptFleet;
            if (pageId.Contains("机务") || pageId.Contains("维护")) return "MNT";
            if (pageId.Contains("战略") || pageId.Contains("愿景")) return "STG";
            if (pageId.Contains("市场") || pageId.Contains("销售")) return "MKT";
            if (pageId.Contains("安全")) return "SAF";
            return "OPS";
        }

        /// <summary>
        /// 当前页的输入提示语。占位文字本身就是「这一页能问什么」的第一层说明。
        ///
        /// 2026-10-25（P22）：命令台现在也有「写」了，所以每条提示都在问句后面补一个**能办的事**。
        /// 只写问句会让玩家以为这条只能问 —— 而他要的东西（开线 / 买机 / 招人 / 找钱）
        /// 恰恰都在写指令里，且都只到「发请示」这一步，批不批仍由他定。
        /// </summary>
        public static string PageHint(string pageId)
        {
            // 「公司整体」与页面无关：任何页都能问，故在每一条提示里都留在同一位置，不随页变化。
            const string company = "；公司整体情况由顾问答（说「汇报公司整体情况」）";
            switch (PageDept(pageId))
            {
                case "FIN": return "向财务部提问，回车下达…  例如：汇报目前的财务状况；要钱说「申请银行贷款」" + company;
                case "HR": return "向人力资源部提问，回车下达…  例如：现在乘务员缺口多少；要人则说「招聘 10 名飞行员」" + company;
                case DeptFleet: return "向机队发展部提问，回车下达…  例如：机队现在几架、几架适航；买机说「买 2 架 A320neo」" + company;
                case "MNT": return "向机务工程部提问，回车下达…  例如：现在的机队可用率是多少" + company;
                case "MKT": return "向市场销售部提问，回车下达…  例如：汇报客座率与票价水平" + company;
                case "STG": return "向战略发展部提问，回车下达…  例如：分析开通北京—纽约航线的可行性；真要开就说「开通北京—纽约」" + company;
                default: return "输入一句指令，回车下达…  例如：汇报本周运力与准点率；开线说「开通成都—上海」" + company;
            }
        }

        /// <summary>一句话 → 部门 / 问还是办 / 实体。</summary>
        public static CeoAsk Route(string pageId, string raw) => Route(pageId, raw, null);

        /// <summary>
        /// 同上，但允许调用方补一层"本地词表"（通常是在营航线的城市名）。
        /// 机场目录是有限集合，玩家嘴里的地名却是无限的；把 GS 里的航线名接进来，
        /// 至少能让「取消我那条珠海金湾的线」对上确实在营的航线，而不是一律说解析失败。
        /// </summary>
        public static CeoAsk Route(string pageId, string raw, PieceResolver extra)
            => Route(pageId, raw, extra, null);

        /// <summary>
        /// 再补一层**页面上下文**：单航线页已经选好了某条线，命令台就应该能接手原来那个
        /// 页面内的指令框（2026-10-28 用户裁定删除该框）。
        ///
        /// 只影响一种句子：**有调令信号、没说城对也没点班号**（「加密到每天 3 班」）。
        /// 显式说了城对/班号的永远优先 —— 上下文是兜底，不是覆盖。
        /// </summary>
        public static CeoAsk Route(string pageId, string raw, PieceResolver extra, string contextRouteKey)
        {
            string text = (raw ?? "").Trim().TrimEnd('。', '！', '!', '？', '?');
            var ask = new CeoAsk { deptCode = PageDept(pageId), kind = CeoAskKind.Ask, topic = "" };

            if (text.Length == 0)
            {
                ask.note = "请先输入一句话。";
                return ask;
            }

            // ① 改台账的句子：先认出来，再交给对应管线（问句不算 —— 「为什么停飞这条线」是问，不是令）
            //
            // ★2026-10-22 报障：「取消北京—上海」只回了运营部一封本线答复，**没有确认卡**。
            //   旧判据要求句子里出现「航线 / 航」二字，可 CEO 最自然的说法根本不带；
            //   于是这句掉进 ①c 的「本线问句」—— `OpsQueryLogic.IdleWords` 把「取消」当「停飞」
            //   话题，回函写着「未改动本线运力」，看起来受理了，实际一条线没动。
            //   新判据：命中关线动词，且句子**真的切出了一对可解析机场**，就按关线办。
            //   但带频率/班期/机号/补贴/时刻这类**别的对象**的句子仍留给各自管线
            //   （「取消北京—上海每天 2 班」是在改班次，认成关线就是整条线没了）。
            if (HasAny(text, CloseWords) && !CapacityDirectiveLogic.LooksLikeQuestion(text))
            {
                bool pairResolved = SplitPair(text, extra, out string co, out string cd, out string cot, out string cdt);
                bool namesRouteWord = Has(text, "航线") || Has(text, "航");
                if (namesRouteWord || (pairResolved && !TargetsOtherThanRoute(text)))
                {
                    ask.deptCode = "OPS";
                    ask.kind = CeoAskKind.Act;
                    ask.topic = TopicCloseRoute;
                    ask.ok = true;
                    if (pairResolved)
                    {
                        ask.origin = co;
                        ask.dest = cd;
                        ask.originText = cot;
                        ask.destText = cdt;
                    }
                    return ask;
                }
            }

            // ①b 运力调令：加减班 / 改班期 / 换机号。
            //    **必须能切出「出发地—目的地」才算调令** —— 判据直接用运力管线自己的
            //    HasDirectiveSignal（增/减/维持/派机四族），不另起一套词表：
            //    两套词表迟早会分叉，届时「页面上能办、命令台说没听懂」就成了新的报障源。
            //    切不出城对时**不返回**，让句子按部门词表照常走（「机队现在几架」不能被当成调令）。
            //    点名航班号（「把 NV1002 加密到每天 2 班」）也算数：同城对标了班号才认得出是哪一条。
            string capFlight = TryReadFlightNumber(text, out string cf) ? cf : null;
            bool capSignal = CapacityDirectiveLogic.HasDirectiveSignal(text) || HasAny(text, ScheduleVerbs);
            bool capIsQuestion = CapacityDirectiveLogic.LooksLikeQuestion(text);
            bool capPair = SplitPair(text, extra, out string mo, out string md, out string mot, out string mdt);
            if (capSignal && !capIsQuestion && (capPair || capFlight != null))
            {
                ask.deptCode = "OPS";
                ask.kind = CeoAskKind.Act;
                ask.topic = TopicCapacity;
                ask.ok = true;
                ask.origin = mo;
                ask.dest = md;
                ask.originText = mot;
                ask.destText = mdt;
                ask.flightNumber = capFlight;
                return ask;
            }

            // ①b′ 页面上下文兜底：单航线页上「加密到每天 3 班」这种**不带城对**的调令。
            //
            //    ★2026-10-28：用户裁定删掉单航线页底部那个指令框，改由命令台统一入口。
            //      但那个框本来就是靠「CEO 正看着哪条线」来认线的，命令台原先只认城对/班号 ——
            //      框一删，同一句话在同一个页面上就会从「照办」变成「没听懂」。
            //      入口可以合并，能力不能丢，所以这里把上下文接上。
            //
            //    仍然是**兜底**：显式点了城对或班号的句子在上面就返回了，走不到这里。
            //    也必须真有调令信号（加密/削减/改班期/派机），否则「这条线整体怎么样」
            //    会被当成调令 —— 那是问句，`capIsQuestion` 也挡了一道。
            if (capSignal && !capIsQuestion && capFlight == null && !capPair
                && !string.IsNullOrEmpty(contextRouteKey))
            {
                ask.deptCode = "OPS";
                ask.kind = CeoAskKind.Act;
                ask.topic = TopicCapacity;
                ask.ok = true;
                ask.contextRouteKey = contextRouteKey;
                return ask;
            }

            // ①c 问某一条具体航线（「为什么北京—上海周三不排班」「北京—上海现在几班」）。
            //    **必须早于部门词表**：这类句子几乎都带「排班 / 班次 / 航」，落在词表里就是
            //    「运营部汇报航线网络」—— 一封 8 条线的总账，答不到「周三为什么不排」。
            //    判据复用 OpsQueryLogic.TopicsOf（班期/停飞/运力/执飞/时刻/值勤/收益），
            //    与真正的答复件同一套词表：命令台认的话题，和写回信时肯答的话题不会分叉。
            if (!CapacityDirectiveLogic.HasDirectiveSignal(text) || CapacityDirectiveLogic.LooksLikeQuestion(text))
            {
                bool flightOnly = TryReadFlightNumber(text, out string fn);
                bool queryish = OpsQueryLogic.TopicsOf(text).Count > 0;
                bool pairish = SplitPair(text, extra, out string qo, out string qd, out string qot, out string qdt);
                if (queryish && (pairish || flightOnly))
                {
                    ask.deptCode = "OPS";
                    ask.kind = CeoAskKind.Ask;
                    ask.topic = TopicRouteQuery;
                    ask.ok = true;
                    ask.origin = qo;
                    ask.dest = qd;
                    ask.originText = qot;
                    ask.destText = qdt;
                    ask.flightNumber = fn;
                    return ask;
                }
            }

            // ② 开通可行性：必须带出发地/目的地，缺了不猜
            //    「可能性」也认：CEO 嘴里不会严格区分「可行性/可能性」（2026-10-20 实测原话）。
            if (Has(text, "可行性") || Has(text, "可能性")
                || (Has(text, "分析") && (Has(text, "开通") || Has(text, "新航线"))))
            {
                ask.deptCode = "STG";
                ask.kind = CeoAskKind.Analyze;
                ask.topic = TopicFeasibility;
                if (TryResolvePair(text, extra, out string fo, out string fd, out string _, out string _))
                {
                    ask.origin = fo;
                    ask.dest = fd;
                    ask.ok = true;
                }
                else
                {
                    ask.note = "可行性分析要先知道出发地与目的地。例如：分析开通北京—纽约航线的可行性。";
                }
                return ask;
            }

            // ②c 四类写指令（开线 / 购机 / 招聘 / 融资）。
            //
            //    命令台既有「读」（汇报/分析）也有「写」。这四类**一律不自己改台账**：
            //    它们只把请求交给既有的呈报管线（<c>InboxManager</c> 的四个 Offer/Queue 方法），
            //    生成一封标准请示件，由 CEO 在收件箱批一次 —— 与页面按钮完全同一条路。
            //    绝不另起第三套执行路径，否则同一件事会有两本账。
            //
            //    判据统一是「动词 + 具体实体 + 不是问句/不是汇报」：
            //    · 「分析开通北京—纽约的可行性」在 ② 已返回 Analyze，不会变成开航令；
            //    · 「汇报融资情况」「贷款还剩多少」「机队现在几架」都留在读类；
            //    · 参数缺一个就**退回引导**（ok=false + note），绝不替 CEO 猜架数/人数。
            if (LooksLikeRequest(text))
            {
                if (TryOpenRoute(text, extra, out var openAsk)) return openAsk;
                if (TryFleetBuy(text, out var fleetAsk)) return fleetAsk;
                if (TryHrRecruit(text, out var hrAsk)) return hrAsk;
                if (TryFinanceRequest(text, out var finAsk)) return finAsk;
            }

            // ②b 公司整体经营：问的不是某个部门，而是「这家公司现在怎么样」。
            //    必须排在部门词表**之前**：这类句子里常顺带提到现金、航线、机队，
            //    落到词表里就成了某一部门的账本 —— 而 CEO 要的是跨部门的判断（2026-10-21 实测原话）。
            //    只认「公司/全司/集团」这类主体词：否则「这条线整体怎么样」也会被当成问全公司。
            if (!CapacityDirectiveLogic.HasDirectiveSignal(text) || CapacityDirectiveLogic.LooksLikeQuestion(text))
            {
                int scope = CompanyScope(text);
                // 点明了某一条航线（「全公司里北京—上海表现如何」）→ 仍按本线问，交运营部答
                bool namedPair = SplitPair(text, extra, out string _, out string _, out string _, out string _);
                if (!namedPair && (scope == CompanyScopeStrong
                                   || (scope == CompanyScopeWeak && !HasAny(text, DeptWords))))
                {
                    ask.deptCode = DeptAdvisor;
                    ask.kind = CeoAskKind.Ask;
                    ask.topic = TopicCompany;
                    ask.ok = true;
                    return ask;
                }
            }

            // ③ 部门词表（顺序即优先级）
            if (HasAny(text, HrWords)) { ask.deptCode = "HR"; ask.ok = true; return ask; }
            if (HasAny(text, FinWords)) { ask.deptCode = "FIN"; ask.ok = true; return ask; }
            if (HasAny(text, FleetWords)) { ask.deptCode = DeptFleet; ask.ok = true; return ask; }
            if (HasAny(text, MarketWords)) { ask.deptCode = "MKT"; ask.ok = true; return ask; }
            if (HasAny(text, OpsWords)) { ask.deptCode = "OPS"; ask.ok = true; return ask; }
            if (HasAny(text, MaintWords)) { ask.deptCode = "MNT"; ask.ok = true; return ask; }

            // ④ 看不出部门：只有在**这确实是在问一件事**时，才按当前页兜底。
            //    判据是「有问法 + 有对象」：
            //    · 「该城对 2 条线」两样都没有，换来一封 8 条线的总账比不理更糟 ——
            //      它会让人以为系统读懂了并在回答（2026-10-21 实测原话）；
            //    · 「汇报一下」有问法、没对象，也是一句不完整的话（同日实测原话）：
            //      派件等于替 CEO 决定了「汇报什么」，而邮件是收不回来的。
            ask.deptCode = PageDept(pageId);
            if (HasAny(text, AskVerbs))
            {
                if (HasAskSubject(text))
                {
                    ask.ok = true;
                    ask.note = "未识别出具体部门，按当前页交给对应部门。";
                    return ask;
                }
                ask.note = BuildIncompleteNote();
                return ask;
            }

            ask.note = BuildNoClueNote(text);
            return ask;
        }

        /// <summary>
        /// 剥掉问法与口气词之后，还剩不剩「问的是什么」。
        ///
        /// 剩得下就派件，剩不下就请 CEO 补一句 —— 这条界线刻意定在「对象」上而不是句子长度上：
        /// 「汇报一下」「给我一份总结」都不说汇报什么，前者四个字、后者六个字，一样该请人补。
        /// </summary>
        static bool HasAskSubject(string text)
        {
            string s = text;
            for (int round = 0; round < 6; round++)
            {
                string before = s;
                s = StripAll(s, AskVerbs);
                s = StripAll(s, AskFillers);
                s = s.Trim(' ', '\u3000', '，', ',', '。', '、', '的', '了', '！', '!', '？', '?');
                if (string.Equals(s, before, StringComparison.Ordinal)) break;
            }
            int cjk = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] > 0x2E80) cjk++;
                else if (char.IsLetterOrDigit(s[i])) return true;   // 英文/数字：Q3、A330、PEK 也算对象
            }
            return cjk >= 2;
        }

        static string StripAll(string s, string[] words)
        {
            if (string.IsNullOrEmpty(s) || words == null) return s;
            for (int i = 0; i < words.Length; i++)
            {
                if (string.IsNullOrEmpty(words[i])) continue;
                s = s.Replace(words[i], "");
            }
            return s;
        }

        /// <summary>有问法、没对象：不当成一句完整指令，也不派件。</summary>
        static string BuildIncompleteNote()
            => "这句话不完整：没说要汇报什么。请补上对象，例如「汇报目前的财务状况」「汇报本周运力与准点率」"
             + "「为什么北京—上海周三不排班」「北京—上海现在几班」。本次未向任何部门派件。";

        /// <summary>
        /// 问某条具体航线（「几班」「为什么周三不排」）而不是某个部门时的兜底话术。
        /// 这类句子的通病是**有话题、没航线**：与其猜一条线去答，不如把能认的形式教一遍。
        /// </summary>
        static string BuildNoClueNote(string text)
        {
            var topics = OpsQueryLogic.TopicsOf(text);
            if (topics.Count > 0)
                return "没认出是哪条航线。可以写成：为什么北京—上海周三不排班 / 北京—上海现在几班 / "
                     + "北京—上海的执飞飞机是哪架；也可以直接报周计划上的班号（如 为什么 NS100 周三不排班）。"
                     + "也可在「航线」页打开单条航线直接提问。";
            return "没看懂这句话。可以问：财务状况 / 乘务员缺口 / 机队规模 / 公司整体情况；"
                 + "也可以说：分析开通北京—纽约航线的可行性、把北京—上海加密到每天 3 班。";
        }

        // ———— 四类写指令的词表与参数读取 ————

        static readonly string[] OpenVerbs = { "开通", "开航", "新开", "开辟" };
        static readonly string[] BuyVerbs = { "购买", "购置", "采购", "订购", "引进", "买" };
        static readonly string[] RecruitVerbs = { "招聘", "招募", "招收", "招" };

        /// <summary>
        /// 「这句话是在**要东西**，不是在**问情况**」。四类写指令共用这一道闸。
        ///
        /// 少了它，「汇报融资情况」会因为句子里有「融资」而变成一封紧急融资请示，
        /// 「招聘进度如何」会变成第二封招聘请示 —— 这类误判的代价是**收不回来的信**，
        /// 比少认一句写指令严重得多，所以闸门宁可关得紧一点。
        /// </summary>
        static readonly string[] ReportOrQuestionWords =
        {
            "汇报", "报告", "简报", "情况", "进度", "现状", "多少", "多久", "怎么样", "如何",
            "为什么", "为何", "吗", "呢", "是否", "能不能", "可不可以", "分析", "评估", "看看", "说说"
        };

        static bool LooksLikeRequest(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return !HasAny(text, ReportOrQuestionWords);
        }

        /// <summary>
        /// 「开通北京—纽约」→ 开航研究会请示。
        ///
        /// 与 ②「可行性」的分界只有一条：**有没有「分析/评估」**。有分析就是让战略部出一份可行性，
        /// 没有就是要运营部按既有流程召集会商 —— 后者和页面里选完机场点「提交研究」是同一件事。
        /// 切不出城对时不装作受理：返回一张 ok=false 的引导卡，命令条上直接说清缺什么。
        /// </summary>
        static bool TryOpenRoute(string text, PieceResolver extra, out CeoAsk ask)
        {
            ask = default;
            if (!HasAny(text, OpenVerbs)) return false;

            if (!TryResolvePair(text, extra, out string o, out string d, out string ot, out string dt))
            {
                ask = new CeoAsk
                {
                    deptCode = "OPS",
                    kind = CeoAskKind.Act,
                    topic = TopicOpenRoute,
                    note = "开航要先知道出发地与目的地。例如：开通北京—纽约航线。"
                         + "（只报一条航线就够，具体班次与机型由开航研究会评估。）"
                };
                return true;
            }

            ask = new CeoAsk
            {
                ok = true,
                deptCode = "OPS",
                kind = CeoAskKind.Act,
                topic = TopicOpenRoute,
                origin = o,
                dest = d,
                originText = ot,
                destText = dt
            };
            return true;
        }

        /// <summary>
        /// 「买 2 架 A320neo」→ 机队发展部购机请示。
        ///
        /// 机型必须是 <see cref="AircraftCatalog"/> 里真实存在的型号（取**最长命中**：
        /// 「A320neo」不会被记成「A320」，「B737-800」不会被记成「B737-8」）。
        /// 架数读不到就退回引导 —— 1 架与 10 架是两笔完全不同的钱。
        /// </summary>
        static bool TryFleetBuy(string text, out CeoAsk ask)
        {
            ask = default;
            if (!HasAny(text, BuyVerbs)) return false;

            string typeId = MatchAircraftType(text);
            if (string.IsNullOrEmpty(typeId)) return false;   // 没点机型：交给机队部按汇报答

            if (!TryReadCount(text, out int count))
            {
                ask = new CeoAsk
                {
                    deptCode = DeptFleet,
                    kind = CeoAskKind.Act,
                    topic = TopicFleetBuy,
                    aircraftType = typeId,
                    note = $"要引进 {typeId} 几架？请写明架数，例如：买 2 架 {typeId}。"
                         + "（数量不明时不下请示 —— 1 架和 10 架是两笔完全不同的钱。）"
                };
                return true;
            }

            ask = new CeoAsk
            {
                ok = true,
                deptCode = DeptFleet,
                kind = CeoAskKind.Act,
                topic = TopicFleetBuy,
                aircraftType = typeId,
                count = count,
                source = ReadBuySource(text)
            };
            return true;
        }

        /// <summary>
        /// 「招聘 10 名飞行员」→ 人力资源部招聘请示。
        ///
        /// 岗位只认编制表里的三个运行岗（飞行员 / 乘务员 / 机务工程师），别的一律不当招聘目标；
        /// 渠道与紧迫度有默认值（校招 · 较急），句子里写了才改 —— 这两项**不改人数与成本口径**，
        /// 且请示件里会原样写出来，CEO 批复前看得见。
        /// </summary>
        static bool TryHrRecruit(string text, out CeoAsk ask)
        {
            ask = default;
            if (!HasAny(text, RecruitVerbs)) return false;

            string position = ReadRecruitPosition(text);
            if (string.IsNullOrEmpty(position)) return false;  // 没点岗位：交人力部按缺口答

            if (!TryReadCount(text, out int count))
            {
                ask = new CeoAsk
                {
                    deptCode = "HR",
                    kind = CeoAskKind.Act,
                    topic = TopicHrRecruit,
                    position = position,
                    note = $"要招几名「{position}」？请写明人数，例如：招聘 10 名{position}。"
                };
                return true;
            }

            ask = new CeoAsk
            {
                ok = true,
                deptCode = "HR",
                kind = CeoAskKind.Act,
                topic = TopicHrRecruit,
                position = position,
                count = count,
                channel = ReadRecruitChannel(text),
                urgency = ReadRecruitUrgency(text)
            };
            return true;
        }

        /// <summary>
        /// 「申请银行贷款 / 办理高息过桥 / 申请股东增资」→ 财务部融资请示。
        ///
        /// 必须带**申请/办理**这类动作词，裸的「融资」「贷款」不算：
        /// 「汇报融资情况」里的「融资」是话题不是命令（见 <see cref="LooksLikeRequest"/> 那道闸）。
        /// 种类只有三档，与 <c>InboxManager.OfferFinanceLoan</c> 的 kind 一一对应。
        /// </summary>
        static bool TryFinanceRequest(string text, out CeoAsk ask)
        {
            ask = default;
            bool acts = (Has(text, "申请") || Has(text, "办理") || Has(text, "要") || Has(text, "需要一笔"))
                        && (Has(text, "贷款") || Has(text, "融资") || Has(text, "过桥") || Has(text, "增资"));
            if (!acts) return false;

            int kind = (Has(text, "增资") || Has(text, "股东")) ? 3
                     : (Has(text, "过桥") || Has(text, "高息")) ? 2
                     : 1;
            ask = new CeoAsk
            {
                ok = true,
                deptCode = "FIN",
                kind = CeoAskKind.Act,
                topic = TopicFinance,
                financeKind = kind
            };
            return true;
        }

        /// <summary>
        /// 机型最长命中。**必须取最长**：目录里「A320」是「A320neo」的前缀、
        /// 「B737-8」是「B737-800」的前缀，取第一个命中会把订单记到错型号上。
        /// </summary>
        static string MatchAircraftType(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string best = null;
            var all = AircraftCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                string id = all[i].typeId;
                if (string.IsNullOrEmpty(id)) continue;
                if (text.IndexOf(id, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (best == null || id.Length > best.Length) best = id;
            }
            return best;
        }

        /// <summary>采购方式：与购机决策会四个按钮一字不差，否则批准后会落到别的付款口径上。</summary>
        static string ReadBuySource(string text)
        {
            if (Has(text, "经营租赁")) return "经营租赁";
            if (Has(text, "租赁") || Has(text, "融资租")) return "融资租赁";
            if (Has(text, "二手")) return "二手引进";
            return "自有资金买断";
        }

        static string ReadRecruitPosition(string text)
        {
            if (Has(text, "飞行") || Has(text, "机长") || Has(text, "副驾")) return "飞行员";
            if (Has(text, "乘务") || Has(text, "空乘") || Has(text, "空姐")) return "乘务员";
            if (Has(text, "机务") || Has(text, "维修工程师")) return "机务工程师";
            return null;
        }

        static int ReadRecruitChannel(string text)
        {
            if (Has(text, "内部转岗")) return 3;
            if (Has(text, "海外") || Has(text, "外籍")) return 2;
            if (Has(text, "猎头") || Has(text, "挖角")) return 1;
            return 0;   // 校园招聘：默认渠道里成本最低、周期最长，适合「不急但要多招」
        }

        static int ReadRecruitUrgency(string text)
        {
            if (Has(text, "紧急") || Has(text, "加急") || Has(text, "马上")) return 3;
            if (Has(text, "不急") || Has(text, "常规") || Has(text, "慢慢")) return 1;
            return 2;
        }

        static readonly char[] CountUnits = { '架', '名', '人', '位', '个' };

        /// <summary>
        /// 读「2 架 / 两架 / 10 名 / 五人」里的数量。读不到返回 false —— 调用方必须自己决定
        /// 是退回引导还是用默认值，本函数不提供「猜一个」的选项。
        /// </summary>
        static bool TryReadCount(string text, out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(text)) return false;

            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i])) continue;
                int j = i;
                while (j < text.Length && (char.IsDigit(text[j]) || text[j] == ' ')) j++;
                if (j >= text.Length || Array.IndexOf(CountUnits, text[j]) < 0) continue;
                if (int.TryParse(text.Substring(i, j - i).Trim(), out int n) && n > 0)
                {
                    count = n;
                    return true;
                }
            }

            for (int i = 0; i < text.Length; i++)
            {
                int v = CjkDigit(text[i]);
                if (v <= 0) continue;
                int j = i + 1;
                if (j < text.Length && text[j] == '十')      // 十五 / 二十 / 二十五
                {
                    int tens = v * 10;
                    j++;
                    if (j < text.Length && CjkDigit(text[j]) > 0) tens += CjkDigit(text[j++]);
                    if (j < text.Length && Array.IndexOf(CountUnits, text[j]) >= 0) { count = tens; return true; }
                    continue;
                }
                if (j < text.Length && Array.IndexOf(CountUnits, text[j]) >= 0) { count = v; return true; }
            }
            return false;
        }

        /// <summary>「两」「俩」都按 2 读 —— CEO 不会写「二架」。</summary>
        static int CjkDigit(char c)
        {
            switch (c)
            {
                case '一': return 1;
                case '两': case '二': return 2;
                case '三': return 3;
                case '四': return 4;
                case '五': return 5;
                case '六': return 6;
                case '七': return 7;
                case '八': return 8;
                case '九': return 9;
                default: return 0;
            }
        }

        // ———— 机场名解析 ————

        /// <summary>
        /// 从一句话里抽「出发地—目的地」。
        ///
        /// 不复用 <see cref="AirportCatalog.TryResolve"/>：它的兜底是 `q.Contains("北京") → PEK`，
        /// 于是「北京大兴」会被吞成首都机场，而「取消北京大兴—珠海金湾」这种整串更是必错。
        /// 这里只在**词边界**上匹配（左串取后缀、右串取前缀），并要求两侧都解析成功。
        /// </summary>
        public static bool TryResolvePair(string raw, out string origin, out string dest)
            => TryResolvePair(raw, null, out origin, out dest, out string _, out string _);

        /// <summary>同上，可附带调用方的本地词表（在营航线名）。</summary>
        public static bool TryResolvePair(string raw, PieceResolver extra,
                                          out string origin, out string dest,
                                          out string originText, out string destText)
        {
            origin = dest = null;
            if (!SplitPair(raw, extra, out origin, out dest, out originText, out destText)) return false;
            return origin != null && dest != null && origin != dest;
        }

        /// <summary>
        /// 把一句话切成「出发地 / 目的地」两段原文，并尽量解析成三字码。
        /// 只要**切得开**就返回 true —— 三字码可能是 null（目录里没有这个机场），
        /// 由调用方决定这算不算致命：可行性分析必须有两端代码，关线复核只要求念得出原文。
        /// </summary>
        public static bool SplitPair(string raw, PieceResolver extra,
                                     out string origin, out string dest,
                                     out string originText, out string destText)
        {
            origin = dest = null;
            originText = destText = null;
            if (string.IsNullOrEmpty(raw)) return false;

            string text = raw.Replace('－', '-').Replace('—', '-').Replace('–', '-')
                             .Replace('~', '-').Replace('～', '-');

            string[] seps = { "--", " - ", "-", "到", "至", "飞往", "飞" };
            for (int s = 0; s < seps.Length; s++)
            {
                string sep = seps[s];
                int at = text.IndexOf(sep, StringComparison.Ordinal);
                if (at <= 0) continue;
                // 「飞」太宽：只认「飞北京」这种紧贴机场名的写法，避免「飞机」被当成分隔符
                if (sep == "飞")
                {
                    if (at + 1 >= text.Length) continue;
                    if (text[at + 1] == '机' || text[at + 1] == '行') continue;
                }

                string left = text.Substring(0, at);
                string right = text.Substring(at + sep.Length);
                string o = MatchSide(left, fromEnd: true, extra: extra, out string ot);
                string d = MatchSide(right, fromEnd: false, extra: extra, out string dt);
                if (o == null && d == null) continue;   // 两侧都认不出：换个分隔符再试
                if (o != null && d != null && o == d) continue; // 「北京到北京」——换下一个写法

                origin = o;
                dest = d;
                originText = ot;
                destText = dt;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 在一侧文字里找机场：从最长到最短枚举**词边界片段**再解析。
        ///
        /// 左串取后缀、右串取前缀 —— 因为左边前面可能挂着动词（「分析开通北京」），
        /// 右边后面可能挂着尾巴（「纽约航线」）。逐长度回退比一次正则更好懂，
        /// 也不会把「飞机」误当分隔符之后又错配成某个机场。
        /// </summary>
        static string MatchSide(string token, bool fromEnd, PieceResolver extra, out string text)
        {
            text = null;
            string t = CleanSide(token);
            if (t.Length < 2) return null;
            text = t;
            int max = Math.Min(t.Length, 12);
            for (int len = max; len >= 2; len--)
            {
                string piece = fromEnd ? t.Substring(t.Length - len) : t.Substring(0, len);
                string code = ResolvePiece(piece) ?? extra?.Invoke(piece);
                if (!string.IsNullOrEmpty(code)) return code;
            }
            return null;
        }

        static string ResolvePiece(string piece)
        {
            var list = AirportCatalog.Airports;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (string.IsNullOrEmpty(a.nameZh)) continue;
                if (string.Equals(a.nameZh, piece, StringComparison.Ordinal)) return a.code;
            }
            // 城市简称（「纽约」「北京」）：必须是机场名的前缀，且至少两字，
            // 否则「纽」这种单字会把一句闲话配成 JFK。
            if (piece.Length >= 2)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var a = list[i];
                    if (string.IsNullOrEmpty(a.nameZh) || a.nameZh.Length <= piece.Length) continue;
                    if (a.nameZh.StartsWith(piece, StringComparison.Ordinal)) return a.code;
                }
            }
            if (piece.Length == 3)
            {
                var a = AirportCatalog.FindByCode(piece.ToUpperInvariant());
                if (!string.IsNullOrEmpty(a.code)) return a.code;
            }
            return null;
        }

        /// <summary>剥掉动词与「的航线 / 机场」这类尾巴（逐轮剥，直到不再变化）。</summary>
        static string CleanSide(string s)
        {
            string t = (s ?? "").Trim().Trim('-', '—', '–', '～', '~', ' ');
            string[] tail = { "的航线", "航线", "的航班", "航班", "机场", "之间", "往返", "的" };
            string[] head =
            {
                "请帮我", "帮我", "请你", "请", "分析一下", "分析", "评估一下", "评估",
                "研究一下", "研究", "开通", "新开", "取消", "关停", "撤销", "撤掉", "停掉", "砍掉", "关闭"
            };
            for (int round = 0; round < 4; round++)
            {
                string before = t;
                for (int i = 0; i < tail.Length; i++)
                    if (t.EndsWith(tail[i], StringComparison.Ordinal))
                    {
                        t = t.Substring(0, t.Length - tail[i].Length);
                        break;
                    }
                for (int i = 0; i < head.Length; i++)
                    if (t.StartsWith(head[i], StringComparison.Ordinal))
                    {
                        t = t.Substring(head[i].Length);
                        break;
                    }
                t = t.Trim().Trim('的', '，', ',', '、', ' ');
                if (t == before) break;
            }
            return t;
        }

        static bool Has(string s, string w) => s.IndexOf(w, StringComparison.Ordinal) >= 0;

        /// <summary>航司两字码 + 2–4 位数字（NV1002 / FR106）。机型（A320 / B787 / CRJ900）不在此形状内。</summary>
        static readonly System.Text.RegularExpressions.Regex RxFlightNumber =
            new System.Text.RegularExpressions.Regex(@"(?<![A-Za-z0-9])([A-Za-z]{2}\d{2,4})(?![0-9])");

        /// <summary>原话里点名的航班号。只当**线索**用，是否真有这条线由台账核。</summary>
        static bool TryReadFlightNumber(string text, out string code)
        {
            code = null;
            if (string.IsNullOrEmpty(text)) return false;
            var m = RxFlightNumber.Match(text);
            if (!m.Success) return false;
            code = m.Groups[1].Value.ToUpperInvariant();
            return true;
        }
        static bool HasAny(string s, string[] words)
        {
            for (int i = 0; i < words.Length; i++)
                if (Has(s, words[i])) return true;
            return false;
        }
    }
}
