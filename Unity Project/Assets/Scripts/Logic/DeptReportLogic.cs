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
    public static partial class DeptReportLogic
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
            if (ask.topic == CeoCommandRouter.TopicAddFlight) return AddFlightDirective(gs, ask, raw);
            if (ask.topic == CeoCommandRouter.TopicOpenRoute) return OpenRouteDirective(ask);
            if (ask.topic == CeoCommandRouter.TopicFleetBuy) return FleetBuyDirective(ask);
            if (ask.topic == CeoCommandRouter.TopicHrRecruit) return HrRecruitDirective(ask);
            if (ask.topic == CeoCommandRouter.TopicFinance) return FinanceDirective(ask);
            if (ask.topic == CeoCommandRouter.TopicRouteQuery) return RouteQuery(gs, ask, raw);
            if (ask.topic == CeoCommandRouter.TopicSlotDeal) return SlotDealDirective(ask);
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


        // ———————————————————————— 人力 ————————————————————————

        static readonly string[] CrewPosts = { "飞行员", "乘务员", "机务工程师" };



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
