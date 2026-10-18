using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 运营部答复件：CEO 在单航线页（或收件箱改稿框）**问**「为什么 / 哪几天 / 多少班」时，
    /// 不把它当调令（更不许猜着执行），而是用本线**真实台账**回一封汇报件。
    ///
    /// ★2026-10-13 CEO 报障：他在单航线页写「为何 FR106/107 周三、周五、周日不安排执飞？」，
    /// 结果运营部**真的去提名机号了** —— 因为句子里既有「安排」（指派动词表里的词）
    /// 又有「FR106」（字母+数字，与机型代号 A330 同形），旧判据把它读成「改派飞机」的命令。
    /// 他只是在问为什么。同一个坑还有第二层：这类句子在旧逻辑里要么被当成命令执行、
    /// 要么回一句红色的「没读懂这条指令想增班还是减班」——两种都不是「回答」。
    ///
    /// 本类的职责只有一件事：**把问题当问题回答**。三条自我约束，与 §14「不猜」同源：
    /// <list type="number">
    /// <item><b>不编数字</b>：答复里出现的每个数都来自台账或纯函数
    ///   （班次表 / <see cref="RouteFrequencyModel"/> / <see cref="CapacityDirectiveLogic.Precheck"/> /
    ///   时刻槽台账 / <see cref="OpsRotationLogic"/> 值勤口径），没有一句是模型生成的；</item>
    /// <item><b>答不上就说答不上</b>：认不出问的是哪一点时，只把本线现状摊开并请 CEO 换个说法，
    ///   **不硬凑一个像样的解释**（兜底见 <see cref="NoAnswerBlock"/>）；</item>
    /// <item><b>与他看到的不一致时以台账为准、并解释差异</b>：例如跨夜班会在周视图次日凌晨留一个
    ///   2 小时的小块，看着像「那天也有航班」，其实是前一天的回程尾段 —— 这类图上错觉要主动说破。</item>
    /// </list>
    ///
    /// 纯静态、无 UnityEngine 依赖，故 <c>tools/slotcheck</c> 可离线钉住答复要点。
    /// </summary>
    public static class OpsQueryLogic
    {
        /// <summary>答复件（汇报件：无决策按钮，故不占收件箱的待办）。</summary>
        public class Answer
        {
            public bool ok;
            public string subject = "";
            public string body = "";
            /// <summary>本次命中的话题（<see cref="TopicLabel"/> 可转中文）。</summary>
            public List<string> topics = new List<string>();
        }

        // ———————————————————————— 话题 ————————————————————————

        public const string TopicDays = "days";        // 班期：哪几天飞、为何不是每天
        public const string TopicIdle = "idle";        // 停飞：为什么没有航班 / 为什么不安排
        public const string TopicCapacity = "capacity";// 运力上限：为什么不能再加密、最多几班
        public const string TopicAircraft = "aircraft";// 执飞机：谁飞、为什么是这架
        public const string TopicSlot = "slot";        // 时刻槽
        public const string TopicDuty = "duty";        // 值勤 / 机组 / 宵禁
        public const string TopicEcon = "econ";        // 收益 / 客座 / 需求

        static readonly string[] DaysWords =
        {
            "班期", "每天", "每日", "天天", "礼拜", "星期", "周几", "哪天", "哪几", "几天",
            "周一", "周二", "周三", "周四", "周五", "周六", "周日",
            "不排班", "不安排", "没排", "空着", "什么时候飞", "飞几",
            // ★2026-10-14 第十次问询：「这样一架飞机也可以安排**一周七趟**往返？」——
            //   「每周 / 一周」是 CEO 问周班次的常用说法，「趟」是数往返的量词（「能排几趟」），
            //   不补进去这句问话就只能命中「执飞机」，答不到班期与机组那两段。
            "每周", "一周", "趟"
        };

        static readonly string[] IdleWords =
            { "停飞", "不飞", "没飞", "飞不了", "没有航班", "无航班", "没航班", "取消" };

        static readonly string[] CapacityWords =
        {
            "加密", "加不了", "不能加", "再加", "最多", "上限", "几班", "缺口", "排不进", "排不出",
            "运力", "不够", "为什么只有", "只有一班", "只有 1 班"
        };

        static readonly string[] AircraftWords =
            { "飞机", "机型", "机号", "执飞", "谁飞", "谁在飞", "谁执飞", "宽体", "窄体", "座级", "座位", "机队", "换机" };

        static readonly string[] SlotWords = { "时刻槽", "槽位", "航权", "交割", "槽" };
        static readonly string[] DutyWords =
        {
            "值勤", "执勤", "超时", "机组", "休息", "航段", "宵禁",
            // ★2026-10-14 第十次问询：「**两组机乘人员轮换**…**05:30 报到**」——
            //   机组这件事 CEO 会说「机乘」「轮换」「报到」，只说「机组」就漏了这一问的核心。
            "机乘", "轮换", "报到"
        };
        static readonly string[] EconWords = { "亏", "赚钱", "盈利", "利润", "收益", "日利", "客座", "上座", "需求", "客流" };

        public static string TopicLabel(string topic)
        {
            switch (topic)
            {
                case TopicDays: return "班期";
                case TopicIdle: return "停飞";
                case TopicCapacity: return "运力上限";
                case TopicAircraft: return "执飞机";
                case TopicSlot: return "时刻槽";
                case TopicDuty: return "值勤";
                case TopicEcon: return "收益";
                default: return "现状";
            }
        }

        /// <summary>
        /// 这句话在问哪几点？—— 按顺序判定（谁先命中谁先排），最多取 <see cref="MaxTopics"/> 个。
        ///
        /// 之所以允许多个话题（「为何周三不安排执飞」既是班期问题也是执飞机问题）：
        /// 每个话题都是一段**各自独立的台账**，都答上比只挑一个准。
        /// </summary>
        public static List<string> TopicsOf(string raw)
        {
            var list = new List<string>();
            string s = raw ?? "";
            if (s.Length == 0) return list;
            Add(list, TopicDays, DaysWords, s);
            Add(list, TopicIdle, IdleWords, s);
            Add(list, TopicCapacity, CapacityWords, s);
            Add(list, TopicAircraft, AircraftWords, s);
            Add(list, TopicSlot, SlotWords, s);
            Add(list, TopicDuty, DutyWords, s);
            Add(list, TopicEcon, EconWords, s);
            return list;
        }

        const int MaxTopics = 3;

        static void Add(List<string> list, string topic, string[] words, string s)
        {
            if (list.Count >= MaxTopics) return;
            if (list.Contains(topic)) return;
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0 && s.IndexOf(words[i], StringComparison.Ordinal) >= 0)
                {
                    list.Add(topic);
                    return;
                }
            }
        }

        // ———————————————————————— 成函 ————————————————————————

        /// <summary>按话题组一封答复件（纯函数：不改动任何游戏状态）。</summary>
        public static Answer Build(GameState gs, RouteData route, string raw)
        {
            var a = new Answer();
            if (route == null)
            {
                a.subject = "运营部答复：缺少航线信息";
                a.body = "运营部答复不可用：未指定航线。\n";
                return a;
            }

            gs = gs ?? GameState.Instance;
            route.EnsureFlights();
            string q = (raw ?? "").Trim();
            a.topics = TopicsOf(q);

            var sb = new StringBuilder();
            // ★Label 本身已经是「城市 三字码」（法兰克福 FRA），后面**不能再拼一次三字码**：
            //   拼了以后 TermNavMap 会把那个多出来的裸码当成一个新的机场词再包一遍链接，
            //   屏幕上就是「法兰克福 FRA 法兰克福 FRA」（2026-10-13 CEO 报障）。
            sb.Append($"运营部答复：{AirportCatalog.Label(route.origin)} ⇄ "
                      + $"{AirportCatalog.Label(route.destination)} 排班咨询\n");

            sb.Append(InboxLetter.Section("您的提问"));
            sb.Append(InboxLetter.Bullet(q.Length == 0 ? "（未记录原话）" : q));

            sb.Append(InboxLetter.Section("答复" + (a.topics.Count > 0 ? " · " + TopicTitles(a.topics) : "")));
            if (a.topics.Count == 0) sb.Append(NoAnswerBlock());
            else
            {
                // 话题之间可能指向同一段台账（「不排班」既命中班期也命中停飞）——
                // 按段落去重，免得同一段台账在信里印两遍。
                var emitted = new HashSet<string>();
                for (int i = 0; i < a.topics.Count; i++)
                {
                    string t = a.topics[i];
                    if (t == TopicIdle && a.topics.Contains(TopicDays)) continue;   // 班期段已含「有没有航班」
                    string block = t == TopicDays || t == TopicIdle ? "days"
                                 : t == TopicCapacity ? "capacity"
                                 : t == TopicAircraft ? "aircraft"
                                 : t == TopicSlot ? "slot"
                                 : t == TopicDuty ? "duty"
                                 : t == TopicEcon ? "econ" : t;
                    if (!emitted.Add(block)) continue;
                    if (emitted.Count > 1) sb.Append('\n');
                    if (block == "days") sb.Append(DaysBlock(gs, route));
                    else if (block == "capacity") sb.Append(CapacityBlock(gs, route));
                    else if (block == "aircraft") sb.Append(AircraftBlock(gs, route));
                    else if (block == "slot") sb.Append(SlotBlock(gs, route));
                    else if (block == "duty") sb.Append(DutyBlock(route));
                    else if (block == "econ") sb.Append(EconBlock(gs, route));
                }
            }

            sb.Append(InboxLetter.Section("本线现况"));
            sb.Append(StatusBlock(gs, route));

            sb.Append('\n');
            sb.Append("口径　以上每个数都取自本线台账（班次表 / 班次模型 / 运力预检 / 时刻槽台账 / 值勤口径），"
                    + "运营部未作任何估计。\n");
            sb.Append(InboxLetter.Bullet("若要办一件事，请用调令句式：增班「加密到每天 N 班」· 减班「削减到每天 N 班」· "
                                       + "派机「安排一架合适的飞机执飞」· 班期「班期 每日」（班期须与每周班次数一致）。"));
            sb.Append("本函为汇报，无需批复。\n");

            a.body = sb.ToString();
            a.subject = $"运营部答复：{route.origin}—{route.destination} 排班咨询"
                      + (a.topics.Count > 0 ? $"（{TopicTitles(a.topics)}）" : "（未分类）");
            a.ok = true;
            return a;
        }

        /// <summary>话题串（邮件标题/状态行共用）。</summary>
        public static string TopicTitles(List<string> topics)
        {
            if (topics == null || topics.Count == 0) return "未分类";
            var parts = new List<string>();
            for (int i = 0; i < topics.Count; i++) parts.Add(TopicLabel(topics[i]));
            return string.Join(" · ", parts.ToArray());
        }

        // ———————————————————————— 各话题的答复 ————————————————————————

        /// <summary>
        /// 班期 / 为什么有的星期没有航班。
        ///
        /// 本段要正面回答三件事，缺一都会让 CEO 觉得在打太极：
        /// ① 实际哪几天有航班（白纸黑字两个名单：有 / 没有）；
        /// ② 为什么是这几天 —— 班期不是运营部随手挑的，是「每周班次 ÷ 每日波次」落到**班期阶梯**上；
        /// ③ 想每天飞，差什么 —— 差的是**每周班次**（阶梯要 7 班/周才是每日），
        ///    而周班次由班次模型按「需求 × 座级 × 单机日利用率」给，不是 CEO 一句话能改的量。
        /// </summary>
        static string DaysBlock(GameState gs, RouteData route)
        {
            var sb = new StringBuilder();
            var days = new bool[7];
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                var d = FlightScheduleFormat.DaysOf(f.daysOfWeek);
                for (int k = 0; k < 7 && d != null && k < d.Length; k++) days[k] |= d[k];
            }
            var off = new bool[7];
            for (int k = 0; k < 7; k++) off[k] = !days[k];

            int weekly = route.WeeklyFlightsDeparture();
            sb.Append(InboxLetter.Bullet($"有航班的星期　{FlightScheduleFormat.DaysSummary(days)}"
                                       + $"（每周 {weekly} 班 · 每日 {route.FlightCount} 个波次）"));
            string none = FlightScheduleFormat.DaysSummary(off);
            sb.Append(InboxLetter.Bullet("没有航班的星期　" + (none == "—" ? "无（每天都排了）" : none)));

            bool anyCross = false;
            int focusCycle = 0;
            string crossExample = "";
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                var trip = FlightScheduleFormat.Trip(route, f);
                if (focusCycle <= 0) focusCycle = trip.cycleMinutes;
                bool cross = LiveClockLogic.ParseMinutes(trip.inArrFull) <= LiveClockLogic.ParseMinutes(trip.outDepFull)
                             && trip.cycleMinutes >= 6 * 60;
                if (cross && crossExample.Length == 0)
                    crossExample = $"{trip.outDepFull} 出发、次日 {trip.inArrFull} 回场";
                if (cross) anyCross = true;
                sb.Append($"　　#{i + 1}　{(string.IsNullOrEmpty(f.slotLabel) ? "—" : f.slotLabel)} "
                        + $"{trip.outDepFull} → {trip.inArrFull}{(cross ? "（次日）" : "")}　"
                        + $"{FlightScheduleFormat.DaysSummary(f.daysOfWeek)} · {FlightScheduleFormat.WeeklyOf(f)} 班/周\n");
            }

            // 跨夜误会：周视图把「次日凌晨的尾段」画进了第二天的列 —— 图上看着像那天也有航班。
            if (anyCross)
                sb.Append(InboxLetter.Bullet(
                    $"跨夜说明　本线航班是跨夜的（{crossExample}），周视图里**次日凌晨那个小块是前一天的回程尾段**，"
                    + "不是当天的新班次 —— 数航班请按「出港时刻」数，别按小块数。"));

            int waves = Math.Max(1, route.FlightCount);
            int each = RouteFrequencyModel.PerWaveWeekly(Math.Max(1, weekly), waves);
            string have = FlightScheduleFormat.DaysSummary(days);
            sb.Append(InboxLetter.Bullet(
                $"班期口径　每周 {weekly} 班 ÷ 每日 {waves} 个波次 = 每波 {each} 班/周 → 本线班期「{have}」；"
                + "**班期以您的调令为准**（写「班期 每日」或「加密到每天 N 班」即按它排），"
                + "只有调令没指定班期时才回落到阶梯。"));
            sb.Append(InboxLetter.Bullet(
                "阶梯速查（仅供调令未指定班期时兜底）　1 班→周一 · 2 班→周二/五 · 3 班→周一/三/五 · 4 班→周一/三/五/日 · "
                + "5 班→周一至五 · 6 班→周一至六 · 7 班→每日。"));

            var freq = RouteFrequencyModel.ForRoute(route);
            float cycleHours = RoundTripHours(route);
            int seats = EconomyBalanceParams.DefaultSeatsFor(route.aircraftType);
            float load = EconomyBalanceParams.DefaultLoadFactor;
            sb.Append(InboxLetter.Bullet(
                $"若要每天都飞　下「加密到每天 1 班」即可按**每日**重编 —— 周班次由您的调令定"
                + $"（班期天数 × 每日波次；每日 1 班 = 7 班/周，现 {weekly} 班）；"
                + "闸门只有时刻槽、值勤与飞行员。"));
            sb.Append(InboxLetter.Bullet(
                $"班次模型现算（**建议值，不是闸门**）　需求侧 {freq.demandPaxPerDay:N0} 人/日 ÷（{seats} 座 × 客座 {load:0%}）"
                + $" ≈ {freq.flightsByDemand:0.##} 班/日；供给侧 单机日利用率目标 "
                + $"{EconomyBalanceParams.TargetDailyUtilizationHours:0.#}h ÷ 单圈 {cycleHours:0.#}h（一圈 {OpsEconomics.RoundTripSectors} 段）"
                + $" ≈ {freq.flightsByAircraft:0.##} 班/日 → 取小 × 7 ≈ 建议每周 {freq.weeklyFlights} 班"
                + $"（本线现排 {weekly} 班/周）。"));
            sb.Append(InboxLetter.Bullet(BindingSideNote(route, freq, cycleHours, gs)));

            // 波次上限：班期之外还有一道「每天能铺几个波次」的闸门（时刻槽 / 值勤 / 宵禁）。
            if (gs?.fleet != null && gs.fleet.Count > 0)
            {
                var shortages = OpsScheduleLogic.PrecheckSlots(route, gs.fleet, gs.activeRoutes, out var feasible, gs);
                int max = feasible != null ? feasible.Count : 0;
                sb.Append(InboxLetter.Bullet(
                    $"日常波次　本线每天最多可排 {max}/{ScheduleSlots.DepTimes.Length} 段往返"
                    + $"（现排 {route.FlightCount} 段）"
                    + (shortages != null && shortages.Count > 0 ? $"；排不出的波次：{ShortageBrief(shortages)}。" : "，暂无受限波次。")));
            }
            else sb.Append(InboxLetter.Bullet("日常波次　本局尚无在册飞机，无法核算可排波次。"));
            return sb.ToString();
        }

        /// <summary>
        /// 模型给到的班次卡在哪一侧 —— 这句是「为什么建议不加密」的真正答案。
        ///
        /// ★2026-10-14 第十次问询后重写。旧版这一句是**错的**：它说本线「卡在供给侧」，
        /// 然后劝 CEO「要把班期提到每日，得让单圈变短（改机型 / 缩短航程），或减少本线已排班次
        /// 对同一架飞机的占用」—— ① 本线单圈 18.3h 远小于 24h，每日一班**排得出来**
        /// （同一页的运力预检就说「最多可排 1/4 段往返」= 每天 1 段）；② 后半句在本线
        /// （一架飞机只飞本线）根本没有对应操作。日利用率目标是**建议口径**，不是闸门。
        /// </summary>
        static string BindingSideNote(RouteData route, RouteFrequencyModel.Frequency f, float cycleHours, GameState gs)
        {
            float target = EconomyBalanceParams.TargetDailyUtilizationHours;
            var met = OpsEconomics.Evaluate(route.distanceKm > 0f ? route.distanceKm : 1000f,
                Math.Max(1, route.weeklyFlights), route.aircraftType, route.groundStaffAssigned,
                gs != null ? gs.onTimePercent : 88f);
            int weekly = route.WeeklyFlightsDeparture();
            int trips = Math.Max(1, route.FlightCount);
            float dailyUtil = OpsEconomics.BlockHours(route.distanceKm) * OpsEconomics.RoundTripSectors * trips;

            string head = f.flightsByDemand < f.flightsByAircraft
                ? $"结论　模型建议不加密是因为**需求侧**：两地客流只支撑 {f.flightsByDemand:0.##} 班/日，"
                  + "再加密会先撑高客座、后摊薄单班收益。"
                : $"结论　模型建议 {f.weeklyFlights} 班/周是因为**日利用率口径**：单圈 {cycleHours:0.#}h ÷ 单机日利用率目标 "
                  + $"{target:0.#}h ≈ {f.flightsByAircraft:0.##} 班/日 —— 这是「飞机该有多忙」的建议值，不是能不能排的闸门。";
            string num = dailyUtil / trips > 0f
                ? $"本线每日 {trips} 段 = 单机 {dailyUtil:0.#}h/日（目标 {target:0.#}h"
                  + $"{(dailyUtil > target ? $"，超 {(dailyUtil / target - 1f) * 100f:0}%" : "")}）；"
                  + $"现排 {weekly} 班/周 ≈ {met.dailyUtilizationHours:0.#}h/日（周均，效率乘数 {met.scheduleEfficiency:0.##}）。"
                : "";
            return head + "　代价　" + num
                 + "要每日一班就下「加密到每天 1 班」——排得出来，代价写在「日利用率」上（超目标会压效率乘数、抬高延误概率），"
                 + "要不要换这份代价由您定。";
        }

        /// <summary>为什么不能再加密 / 最多能排几班（复用单航线页那份运力预检，口径同源）。</summary>
        static string CapacityBlock(GameState gs, RouteData route)
        {
            var sb = new StringBuilder();
            if (gs?.fleet == null || gs.fleet.Count == 0)
            {
                sb.Append(InboxLetter.Bullet("本局尚无在册飞机，无法编班 —— 加密先要有机。"));
                return sb.ToString();
            }
            var shortages = OpsScheduleLogic.PrecheckSlots(route, gs.fleet, gs.activeRoutes, out var feasible, gs);
            int max = feasible != null ? feasible.Count : 0;
            sb.Append(InboxLetter.Bullet(
                $"上限　本线每天最多可排 {max}/{ScheduleSlots.DepTimes.Length} 段往返"
                + (feasible != null && feasible.Count > 0 ? $"（{string.Join("、", feasible.ToArray())}）" : "")
                + $"；现排 {route.FlightCount} 段。"));
            if (shortages != null && shortages.Count > 0)
            {
                sb.Append(Indent("排不出的波次与原因"));
                for (int i = 0; i < shortages.Count; i++)
                {
                    var s = shortages[i];
                    if (s == null) continue;
                    sb.Append($"　　{s.slotLabel} {s.dep}　{s.reason}\n");
                }
                sb.Append(InboxLetter.Bullet(CapacityDirectiveLogic.RemedyForShortages(shortages)));
            }
            else sb.Append(InboxLetter.Bullet("逐波复核：四个需求波都排得进，本线尚有加密余量（时段可任选）。"));
            return sb.ToString();
        }

        static string Indent(string text) => "　" + text + "\n";

        static string ShortageBrief(List<OpsScheduleLogic.WaveShortage> shortages)
        {
            var parts = new List<string>();
            for (int i = 0; i < shortages.Count; i++)
            {
                var s = shortages[i];
                if (s == null) continue;
                parts.Add($"{s.slotLabel} {s.dep}（{KindLabel(s.kind)}）");
            }
            return string.Join("、", parts.ToArray());
        }

        static string KindLabel(string kind)
        {
            switch (kind)
            {
                case "no_slot": return "缺时刻槽";
                case "crew": return CrewCapacityLogic.Label;
                case "duty": return "值勤超限";
                case "curfew": return "宵禁";
                case "no_aircraft": return "缺运力";
                default: return string.IsNullOrEmpty(kind) ? "受限" : kind;
            }
        }

        /// <summary>谁在飞 / 为什么这架 / 为什么没有飞机。无机可派时用与「运力核对汇报」同一份归因。</summary>
        static string AircraftBlock(GameState gs, RouteData route)
        {
            var sb = new StringBuilder();
            route.EnsureFlights();
            string regs = CapacityDirectiveLogic.RegsOf(route);
            bool any = !string.IsNullOrEmpty(regs) && regs != "未分配";

            if (!any)
            {
                sb.Append(InboxLetter.Bullet($"本线目前没有执飞机号（无机号即排不出班次）—— 本线 {route.origin}—{route.destination} 处于停飞状态。"));
                var rejected = AircraftNominationLogic.RejectionDetails(gs, route, 6);
                sb.Append(Indent("运营部逐机核对（与收件箱《运力核对汇报》同一份归因）："));
                for (int i = 0; i < rejected.Count; i++) sb.Append($"　　· {rejected[i].Line()}\n");
                sb.Append(AircraftNominationLogic.BlockerSummary(gs, route, rejected).Replace("\n", "\n　"));
                return sb.ToString();
            }

            int seats = CapacityEconomics.SeatsOfType(route.aircraftType);
            sb.Append(InboxLetter.Bullet($"现执飞　{CapacityDirectiveLogic.RegsOf(route)} · {route.aircraftType}"
                                       + (seats > 0 ? $"（{seats} 座）" : "") + $" · 每周 {route.WeeklyFlightsDeparture()} 班。"));
            var cands = AircraftNominationLogic.Nominate(gs, route);
            if (cands != null && cands.Count > 0)
                sb.Append(InboxLetter.Bullet(
                    $"可选候选　机队另有 {cands.Count} 架符合硬门槛（航程/机型/适航/过渡业务/值勤/宵禁/时刻槽），"
                    + $"投影日利最优 {cands[0].profitWan:F2} 万。"));
            sb.Append(InboxLetter.Bullet(
                "改派口径　说「安排一架合适的飞机执飞」，运营部按硬门槛提名 2–3 架候选（含投影收益）呈报，"
                + "您选定才落班；退回则不指派、本线维持现状。"));
            return sb.ToString();
        }

        /// <summary>时刻槽：逐停站是否需要、每个班次占没占上、缺的是哪一刻、已批待交割的何时入账。</summary>
        static string SlotBlock(GameState gs, RouteData route)
        {
            var sb = new StringBuilder();
            if (gs == null)
            {
                sb.Append(InboxLetter.Bullet("时刻槽台账需在游戏内查看。"));
                return sb.ToString();
            }
            string viaApt = RoutePermitLogic.StopoverOf(route);
            bool needOrigin = RoutePermitLogic.NeedsDepartureSlot(route.origin);
            bool needDest = RoutePermitLogic.NeedsDepartureSlot(route.destination);
            bool needVia = viaApt != null && RoutePermitLogic.NeedsDepartureSlot(viaApt);
            if (!needOrigin && !needDest && !needVia)
            {
                sb.Append(InboxLetter.Bullet($"各停站（{route.origin} / {route.destination}"
                                           + (viaApt == null ? "" : $" / {viaApt}")
                                           + "）均为非协调机场，本线离港班次不需要时刻槽 —— 加密不受槽位限制。"));
                return sb.ToString();
            }

            route.EnsureFlights();
            sb.Append(InboxLetter.Bullet("口径　一停站一对槽（到场 + 离场）：本场端 1 对、对端 1 对；"
                + "经停站一趟往返停两次，故 2 对（去程一次、回程一次）。缺任一停站，那一波排不进班。"));
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                FlightScheduleFormat.Numbers(route, i, out string outbound, out string inbound);
                string slot = string.IsNullOrEmpty(f.slotLabel) ? ScheduleSlots.LabelForDep(f.depTime) : f.slotLabel;
                sb.Append(InboxLetter.Row($"班次 {i + 1} {outbound}/{inbound} {slot} {f.depTime}",
                    RoutePermitLogic.SlotStatusOf(gs, route, i)));
                var needs = RoutePermitLogic.FlightNeeds(route, i);
                for (int k = 0; k < needs.Count; k++)
                    sb.Append(InboxLetter.Bullet($"{RoutePermitLogic.EndStatusText(gs, route, needs[k])}"
                        + $"　{RoutePermitLogic.SlotPoolLine(gs, needs[k].airport, needs[k].band)}"));
            }

            string pend = PendingSettlementLine(gs, route);
            sb.Append(InboxLetter.Bullet(pend));
            return sb.ToString();
        }

        /// <summary>已批准、待下季交割的时刻槽（免得 CEO 以为「缺槽」是没钱买运力）。</summary>
        static string PendingSettlementLine(GameState gs, RouteData route)
        {
            var pend = InboxManager.PendingSlotSettlements(gs);
            int qty = 0, y = 0, q = 0;
            var clocks = new List<string>();   // ★2026-10-15：按**具体时刻**归集，不再写「早/中/晚/夜」
            var apts = new List<string>();
            string viaPend = RoutePermitLogic.StopoverOf(route);
            if (pend != null)
            {
                for (int i = 0; i < pend.Count; i++)
                {
                    var m = pend[i];
                    if (m == null) continue;
                    if (!SameAirport(m.origin, route.origin) && !SameAirport(m.origin, route.destination)
                        && !SameAirport(m.origin, viaPend)) continue;
                    string at = (string.IsNullOrEmpty(m.origin) ? "HUB" : m.origin)
                                + " " + InboxManager.SlotMailClock(m);
                    if (!clocks.Contains(at)) clocks.Add(at);
                    if (!string.IsNullOrEmpty(m.origin) && !apts.Contains(m.origin)) apts.Add(m.origin);
                    qty += InboxManager.SlotQtyOf(m);
                    y = m.effectYear; q = m.effectQuarter;
                }
            }
            if (qty <= 0)
                return "待交割　本线各停站暂无已批准待交割的时刻槽；缺槽请先在收件箱批准「申请时刻槽」，下季交割后方可排那一波。";
            return $"待交割　{string.Join("、", apts.ToArray())} 已批准 {qty} 条（{string.Join("、", clocks.ToArray())}），"
                 + $"第{y}年 Q{q} 入账 —— 在此之前缺槽波次排不进班，不是缺运力。";
        }

        static bool SameAirport(string a, string b)
            => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>值勤：为什么「同一架飞机多飞一段」会超限（这条常被当成 bug 报上来）。</summary>
        static string DutyBlock(RouteData route)
        {
            var sb = new StringBuilder();
            int block = OpsRotationLogic.BlockOf(route);
            int turn = OpsRotationLogic.TurnOf(route);
            int depMin = LiveClockLogic.ParseMinutes(route.depTime);
            int report = depMin - OpsRotationLogic.ReportBeforeMinutes;
            var budget = OpsRotationLogic.BudgetOf(route.aircraftType, report, block, 2);
            var shape = OpsRotationLogic.DutyShapeOf(route.aircraftType, depMin, block, turn,
                out var outSlice, out var backSlice);
            int mergedLen = block * 2 + turn + OpsRotationLogic.ReportBeforeMinutes;   // 同组连飞（报到 → 回场）

            sb.Append(InboxLetter.Bullet(
                $"本线一圈　{route.depTime} 起飞（{OpsRotationLogic.FormatMinute(report)} 报到）→ 回场，"
                + $"单段轮挡 {block / 60f:0.##}h · 过站 {turn} 分 · 去+回连过站 {(block * 2 + turn) / 60f:0.#}h。"));
            sb.Append(InboxLetter.Bullet(
                $"值勤口径　报到提前 {OpsRotationLogic.ReportBeforeMinutes} 分钟；上限随航段数递减"
                + $"（2 航段 {OpsRotationLogic.DutyLimitBySectors(2) / 60f:0.##}h → "
                + $"4 航段 {OpsRotationLogic.DutyLimitBySectors(4) / 60f:0.##}h）；"
                + $"两个值勤期之间最低休息 {OpsRotationLogic.MinRestMinutes / 60f:0.#} 小时。"));
            if (AirlineCEO.Data.AircraftCatalog.IsWidebody(route.aircraftType))
                sb.Append(InboxLetter.Bullet(
                    $"宽体增援　本线用 {route.aircraftType}，可配增援机组提高上限（航段硬性 ≤ "
                    + $"{OpsRotationLogic.AugmentedSectorCap} 段）；本次预算 {budget.limit / 60f:0.##}h（{budget.note}）。"));

            // ★「过站含不含机组休息」—— 常被问到的口径，直接写清：过站是地面作业，机组在值勤不在休息
            int restClass = AirlineCEO.Data.AircraftCatalog.RestClassOf(route.aircraftType);
            sb.Append(InboxLetter.Bullet(
                "休息口径　过站时间**只含地面作业**（上下客 / 清洁 / 配餐 / 加油 / 装卸），机组此时仍在值勤，"
                + "**不是**休息。机组休息只在两处：① 两个值勤期之间 ≥ "
                + $"{OpsRotationLogic.MinRestMinutes / 60f:0.#} 小时"
                + (AirlineCEO.Data.AircraftCatalog.IsWidebody(route.aircraftType)
                    ? $"，② 宽体增援机组的**空中**休息（巡航段，本机型 {AirlineCEO.Data.AircraftCatalog.RestClassLabel(restClass)}"
                      + $" 需 ≥ {AirlineCEO.Data.AircraftCatalog.MinInflightRestMinutes(restClass, budget.limit)} 分，"
                      + $"本段可休息 {AirlineCEO.Data.AircraftCatalog.AvailableInflightRestMinutes(block)} 分）"
                    : "")
                + "。"));

            if (shape == OpsRotationLogic.RoundTripShape.CrewSwap)
            {
                sb.Append(InboxLetter.Bullet(
                    $"结论　「去程 + 回程连着飞」要 {mergedLen / 60f:0.#}h（{OpsRotationLogic.FormatMinute(report)} 报到 → "
                    + $"回场），超过 {budget.limit / 60f:0.##}h 的预算 —— 所以本线按真实航司做法走**外站换组**："
                    + $"{route.depTime} 去程是一组机组（{OpsRotationLogic.FormatMinute(outSlice.report)} 报到 → "
                    + $"落 {route.destination} {OpsRotationLogic.FormatMinute(outSlice.end)}，{outSlice.Length / 60f:0.#}h），"
                    + $"回程由 {route.destination} 驻站机组执飞（{OpsRotationLogic.FormatMinute(backSlice.report)} 报到 → 回场 "
                    + $"{OpsRotationLogic.FormatMinute(backSlice.end)}，{backSlice.Length / 60f:0.#}h）—— 两个值勤期各自在上限内。"
                    + "**换的是机组，不是等机组休息**；这也是本线同机多飞一段会被判「值勤超限」的原因，不是排班写错时间。"));
                // ★2026-10-14 第十次问询：机组**组数**（第三根轴）——排班器不管这件事，必须在这里如实报。
                // ★2026-10-14 第十三次问询：组数是两岗共用的班底，**每组几人两岗各算各的**
                //   （驾驶舱看值勤、客舱看座位 1/50），故这里两个人数都要报，不能让 CEO 以为「机组」只有飞行员。
                var need = OpsRotationLogic.RouteCrewNeed(route, Math.Max(1, route.FlightCount));
                int sets = need.sets, pilots = need.pilots, cabin = need.cabin;
                int setsDaily = OpsRotationLogic.CrewSetsForDaily(1, shape, route.aircraftType, depMin, block, turn);
                int pilotsDaily = setsDaily * OpsRotationLogic.PilotsPerDuty(block);
                int cabinDaily = setsDaily * OpsRotationLogic.CabinPerDuty(route.aircraftType);
                int retEnd = backSlice.end;                                 // 回程回场（本线 00:46）
                int reportNextMorning = 1440 + report;                      // 次日同刻报到（本线 05:30）
                sb.Append(InboxLetter.Bullet(
                    $"机组组数　「每日一趟」要 {setsDaily} 组（每组 {OpsRotationLogic.PilotsPerDuty(block)} 名飞行员"
                    + $" + {OpsRotationLogic.CabinPerDuty(route.aircraftType)} 名乘务员，共 {pilotsDaily} 名 + {cabinDaily} 名），"
                    + $"现排每日 {Math.Max(1, route.FlightCount)} 段需 {sets} 组（{pilots} 名飞行员 + {cabin} 名乘务员）。"
                    + "「组」是**两岗共用的轮转班底**（同去程、同驻站、同回程），但每组几人两岗不同："
                    + "驾驶舱按值勤时长配（超长程走 4 人制增援），客舱按座位配（每 50 座 1 名）。"
                    + "为什么不是两组：一组飞完去程人已在对方站，只能接**次日**回程；回程落地本场是深夜"
                    + $"（{OpsRotationLogic.FormatMinute(retEnd)}），接不上当日/次日早班报到 → 一组每 3 天最多 2 段"
                    + $"（{2f / 3f:0.##} 段/天），每天 2 个值勤期就要 3 组。"
                    + $"同理，「落地那组 05:30 再报到」也不成立：只隔 {(reportNextMorning - retEnd) / 60f:0.#}h < 最低休息 "
                    + $"{OpsRotationLogic.MinRestMinutes / 60f:0.#}h —— 05:30 报到的是**另一组**（去程组）。"));
                // ★2026-10-14 第十二次问询：组数不是「算出来好看」，而是排班的**第四道硬闸门** ——
                // 当面把「本线可用飞行员 vs 需求」算给 CEO 看（与 ApplyWaves / PrecheckSlots 同一套算术）。
                sb.Append(InboxLetter.Bullet(
                    CrewCapacityLogic.Line(GameState.Instance, route, Math.Max(1, route.FlightCount))));
            }
            else if (shape == OpsRotationLogic.RoundTripShape.Infeasible)
                sb.Append(InboxLetter.Bullet(
                    $"结论　单段 {block / 60f:0.##}h 本身已超单段值勤上限 {outSlice.limit / 60f:0.##}h —— "
                    + "连「外站换组」也拆不开，本线当日往返在任何值勤口径下都排不出来。"));
            else
                sb.Append(InboxLetter.Bullet(
                    $"结论　{mergedLen / 60f:0.#}h（含报到 {OpsRotationLogic.ReportBeforeMinutes} 分）"
                    + $"在 {budget.limit / 60f:0.##}h 预算之内，本线当日往返可行；"
                    + "加密的瓶颈在时刻槽或机队余量，不在值勤。"));
            return sb.ToString();
        }

        /// <summary>收益：为什么不赚钱 / 客座与需求对不对得上。</summary>
        static string EconBlock(GameState gs, RouteData route)
        {
            var sb = new StringBuilder();
            var g = RouteGravityModel.EvaluateRoute(gs, route);
            int seats = 0;
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                int s = CapacityEconomics.SeatsOfType(route.aircraftType);
                seats += Math.Max(1, s) * Math.Max(1, FlightScheduleFormat.WeeklyOf(f));
            }
            int flights = Math.Max(1, route.FlightCount);
            sb.Append(InboxLetter.Bullet(
                $"收益　单班日利 {route.dailyProfit / flights:F2} 万 · 周利 ≈ {route.dailyProfit * 7f:F1} 万"
                + $"（全周口径，含固定成本摊销）。"));
            sb.Append(InboxLetter.Bullet(
                $"供需　周需求 {g.weeklyPax:N0} 人次（商务 {g.weeklyC:N0} / 超经 {g.weeklyW:N0} / 经济 {g.weeklyY:N0}）"
                + $"　vs　周座位 {seats:N0} 座"));
            sb.Append(InboxLetter.Bullet(CapacityDirectiveLogic.HealthCheck(gs, route, out string suggest)
                + (string.IsNullOrEmpty(suggest) ? "" : $"　建议 {suggest}。")));
            if (route.dailyProfit < 0f)
                sb.Append(InboxLetter.Bullet(
                    "亏损说明　单班基准为负（真实亏损）时不建议继续加密 —— 加密只会把亏损等比例放大；"
                    + "可先核对两端需求、座级与班期，或按「削减到每天 N 班」收缩到不亏的班次。"));
            return sb.ToString();
        }

        /// <summary>
        /// 认不出问的是哪一点时的兜底：**不编答案**，只把本线现状摊开 + 请 CEO 换个说法。
        ///
        /// 刻意不写「关于您的问题，运营部认为……」这类搪塞话术 —— 那种函看起来答了，
        /// 实际什么都没答，比明确说「没读懂」更浪费时间（§14.4 同源）。
        /// </summary>
        static string NoAnswerBlock()
            => InboxLetter.Bullet("这句话运营部没读准问的是哪一点 —— 本函**不编答案**，只把本线台账摊开给您；请换个说法再问一次。")
             + InboxLetter.Bullet("可以这样问　「为何周三不排班」「为什么不能加密」「最多能排几班」"
                                + "「这条线谁飞」「为什么亏钱」「时刻槽还差几个」「值勤会不会超限」。");

        /// <summary>本线现状一行一条（班次 / 班期 / 执飞 / 槽 / 收益），所有答复共用。</summary>
        static string StatusBlock(GameState gs, RouteData route)
        {
            var sb = new StringBuilder();
            route.EnsureFlights();
            int weekly = route.WeeklyFlightsDeparture();
            var days = new bool[7];
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                var d = FlightScheduleFormat.DaysOf(f.daysOfWeek);
                for (int k = 0; k < 7 && d != null && k < d.Length; k++) days[k] |= d[k];
            }

            sb.Append(InboxLetter.Bullet($"班次　每日 {route.FlightCount} 班 · 离港日口径每周 {weekly} 班"
                                       + $"（值勤日口径每周 {route.WeeklyFlightsDuty()} 班）"));
            sb.Append(InboxLetter.Bullet("班期　" + FlightScheduleFormat.DaysSummary(days)
                                       + (route.flights.Count > 0 && !string.IsNullOrEmpty(route.flights[0].daysOfWeek)
                                          ? $"　｜　波次 {SlotSummary(route)}" : "")));
            sb.Append(InboxLetter.Bullet($"执飞　{CapacityDirectiveLogic.RegsOf(route)} · {route.aircraftType}"));

            if (gs?.fleet != null && gs.fleet.Count > 0)
            {
                var shortages = OpsScheduleLogic.PrecheckSlots(route, gs.fleet, gs.activeRoutes, out var feasible, gs);
                int max = feasible != null ? feasible.Count : 0;
                sb.Append(InboxLetter.Bullet(
                    $"运力　最多可排 {max}/{ScheduleSlots.DepTimes.Length} 段往返"
                    + (shortages != null && shortages.Count > 0 ? $"；受限　{ShortageBrief(shortages)}" : "，暂无受限波次")));
            }
            else sb.Append(InboxLetter.Bullet("运力　本局尚无在册飞机，无法编班。"));

            var g = RouteGravityModel.EvaluateRoute(gs, route);
            sb.Append(InboxLetter.Bullet(
                $"收益　单班日利 {route.dailyProfit / Math.Max(1, route.FlightCount):F2} 万 · 周利 ≈ "
                + $"{route.dailyProfit * 7f:F1} 万 · 周需求 {g.weeklyPax:N0} 人次"));
            return sb.ToString();
        }

        static string SlotSummary(RouteData r)
        {
            if (r?.flights == null || r.flights.Count == 0) return "—";
            var parts = new List<string>();
            for (int i = 0; i < r.flights.Count; i++)
            {
                var f = r.flights[i];
                if (f == null) continue;
                parts.Add(string.IsNullOrEmpty(f.slotLabel) ? f.depTime : f.slotLabel);
            }
            return parts.Count > 0 ? string.Join(" / ", parts.ToArray()) : "—";
        }

        /// <summary>按模型的同一条公式算单圈耗时（与 <see cref="RouteFrequencyModel.Compute"/> 同源，故可直接引用）。</summary>
        static float RoundTripHours(RouteData route)
        {
            float km = route != null && route.distanceKm > 0f ? route.distanceKm : 1000f;
            string type = route != null ? route.aircraftType : null;
            return (2f * RouteFrequencyModel.BlockMinutes(km, type) + EconomyBalanceParams.BaseTurnMinutes) / 60f;
        }
    }
}
