using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 运力调整指令（CEO 在单航线页用对话框下达，非按钮）：
    /// 系统解析指令 → 运营部自动编班 + 构建航班环 → 市场/机务/财务/地勤联动 → 呈报邮件待批复。
    /// CEO 不做排班操作；本类负责把一句话指令变成可执行的排班与航班环。
    /// </summary>
    public static class CapacityDirectiveLogic
    {
        /// <summary>呈报邮件 action（收件箱按此识别「运力调整呈报」）。</summary>
        public const string Action = "capacity_directive";
        /// <summary>复核汇报件的 role（指令未能执行时的汇报件：action 留空，无需批复）。</summary>
        public const string FailureRole = "运力复核汇报";

        /// <summary>每日班次上限：与时刻槽模型一致（早/午/晚/红眼）。</summary>
        public const int MaxWavesPerDay = 4;

        /// <summary>
        /// 排班引擎修订号。待批呈报的 capEngineRev 低于此值时，运营部按原文令重编一次
        /// （用于占位时刻污染窗口等规则修复后，不要求 CEO 再点「提出修改」）。
        /// </summary>
        public const int EngineRev = 4;

        public class Directive
        {
            public string raw;                  // CEO 原文
            public string intent = "increase";  // increase / reduce / keep
            /// <summary>
            /// 意图是否**明确说出来了**（命中词表，或分析服务给出）。
            ///
            /// 这个字段的存在是为了修一个会「反向执行」的漏洞：过去 intent 默认就是 increase，
            /// 于是「这条线不赚钱，把班次收一收」被解析成**增班** —— 而「把班次收一收」不在任何词表里，
            /// 系统其实什么都没读懂，却按「增班」办了，且不告诉玩家。CEO 想砍班、系统反而加密，
            /// 这是本类最危险的一处默认值。
            /// 现在：没读懂就 <see cref="NeedsConfirm"/> 拦一道，先回显方案请确认，绝不猜着执行。
            /// </summary>
            public bool intentKnown;
            /// <summary>
            /// 是否来自**已确认的记忆**（见 <see cref="SetLearnedLookup"/>）。
            /// 用真了意味着这句话 CEO 自己确认过含义，优先级高于关键词猜测。
            /// </summary>
            public bool fromLearned;
            public int waves;                   // 目标每日班次，0=自动
            public string[] deps;               // 指定离港时刻，空=按班次自动铺
            public string days;                 // 班期，空=默认
            public bool buildRing = true;       // 是否构建航班环
            public bool maximize;               // 挖满运力（全天时段）
            public bool fromAi;                 // 是否经分析服务解析
            public string analysis = "";        // 系统解析说明（人类可读）
            /// <summary>
            /// 这是一句**询问**（「为何周三不安排执飞？」），不是调令。
            ///
            /// 这个字段的存在是为了修一类「问句被当成命令办掉」的漏洞（2026-10-13 CEO 报障）：
            /// 「为何 FR106/107 周三、周五、周日不安排执飞？」里既有「安排」（指派动词）
            /// 又有「FR106」（与机型代号 A330 同形），旧判据因此把它当成**改派飞机**的命令 ——
            /// 运营部真的去提名机号了。他只是在问为什么。
            ///
            /// 现口径：疑问句一律**不解析成意图、不执行、不猜**，转由运营部发答复件
            /// （<see cref="AirlineCEO.Logic.OpsQueryLogic"/>）用真实数据回答。
            /// </summary>
            public bool query;
            /// <summary>
            /// 这句是「按目标设定」（「把FR106/FR107调整为每日一段往返」）：方向由现状与目标比较得出，
            /// 不是关键词读出来的。见 <see cref="ResolveStatedTarget"/>。
            /// </summary>
            public bool statedTarget;
        }

        /// <summary>
        /// 一条**已确认**的「原话 → 解析结果」映射（玩家级记忆，见 <see cref="SetLearnedLookup"/>）。
        ///
        /// 它只记句子，不做泛化：`把班次收一收` 学到的就是这七个字，换个说法不认。
        /// 这是有意的 —— 泛化是语义层（分析服务）的活，记忆只负责「把说过的原话记住」。
        /// </summary>
        public class LearnedDirective
        {
            public string intent = "increase";
            public int waves;
            public string[] deps;
            public string days = "";
            public bool buildRing = true;
            public bool maximize;
        }

        /// <summary>
        /// 记忆查询：入参是 <see cref="NormalizeRaw"/> 归一后的原话，命中则返回该话已确认的含义。
        /// 由宿主（Unity 侧 <c>DirectiveLexicon</c>）注册；离线校验（tools/slotcheck）可注入桩。
        /// </summary>
        public delegate bool LearnedDirectiveLookup(string normalizedRaw, out LearnedDirective learned);

        static LearnedDirectiveLookup _learnedLookup;

        /// <summary>
        /// 注册/注销「已确认原话」的记忆源。传 null 即关闭记忆，解析退回关键词兜底。
        ///
        /// 之所以做成注入而不是直接读 PlayerPrefs：本类是**纯函数**，`tools/slotcheck` 离线跑它，
        /// 碰不到 UnityEngine。存储归宿主，判定归这里 —— 顺带让记忆机制本身也可被离线钉住。
        /// </summary>
        public static void SetLearnedLookup(LearnedDirectiveLookup lookup) => _learnedLookup = lookup;

        /// <summary>
        /// 归一化原话，作为记忆的键：去首尾空白、把连续空白压成一个空格、去掉常见中英标点。
        ///
        /// 只做「同一句话的不同写法」（多打一个空格、结尾多个句号）的归并，**不做近义改写** ——
        /// 近义归一是语义层的活，凭标点猜同义会把两张不同的意思合并成一张。
        /// </summary>
        public static string NormalizeRaw(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            bool prevSpace = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsWhiteSpace(c)) { if (!prevSpace && sb.Length > 0) { sb.Append(' '); prevSpace = true; } continue; }
                // 标点一律丢弃（中英混用很常见），但保留数字与冒号——时刻 08:00 靠它。
                if (c == '。' || c == '，' || c == '、' || c == '；' || c == '！' || c == '？'
                    || c == '.' || c == ',' || c == ';' || c == '!' || c == '?' || c == '~'
                    || c == '“' || c == '”' || c == '‘' || c == '’') continue;
                sb.Append(c);
                prevSpace = false;
            }
            return sb.ToString().Trim();
        }

        /// <summary>解析来源标签：记忆 / 分析服务 / 关键词兜底。UI 与邮件共用，避免两处口径漂移。</summary>
        public static string ChannelLabel(Directive d, bool fromAi)
        {
            if (d != null && d.fromLearned) return "记忆（您确认过的说法）";
            return fromAi ? "分析服务" : "关键词兜底";
        }

        public class Report
        {
            public bool ok;
            public string note = "";            // 失败原因 / 提示
            public string directiveRaw;
            public string analysis;
            public string capacitySection = ""; // 调整后运力
            public string ringSection = "";     // 航班环
            public string linkedSection = "";   // 联动部门
            public string impactSection = "";   // 影响
            public string shortageSection = ""; // 运力缺口（想要但排不出的时段）
            public string sourcingSection = ""; // 运力来源：复用 / 闲置 / 串用 / 新增
            public string slotSection = "";     // P4：时刻槽占用与申请
            public bool slotFirst;              // P4：缺槽时先批槽、再加密班次
            public string crewSection = "";     // ★2026-10-14：飞行机组产能与缺员处置
            public bool crewFirst;              // ★缺机组时先补机组（招聘/加机）、再重下加密指令
            public List<OpsScheduleLogic.WaveShortage> shortages; // 缺口明细（供 UI 摘要）
            public string ringIdsCsv = "";      // 本次建立的航班环（退回时撤销）
            /// <summary>目标每日波次（0 = 本次指令就是「维持现状」）。供批复回执判断调令是否落地。</summary>
            public int requestedWaves;
            /// <summary>指令**前**的每日波次（区分「一班没动」与「动了但没排满」）。</summary>
            public int beforeWaves;
            public float profitBefore;
            public float profitAfter;
            /// <summary>
            /// 本呈报是「指派/改派执飞飞机」而非班次调整（见 <see cref="AircraftNominationLogic"/>）。
            /// 两者的呈报邮件类型不同（assign_aircraft vs capacity_directive），调用方须按此分流，
            /// 否则会把一份飞机提名当成运力呈报投递出去。
            /// </summary>
            public bool assignment;
            /// <summary>失败时复核报告是否已投进收件箱（UI 据此如实告诉 CEO「去收件箱看」）。</summary>
            public bool failureQueued;
            /// <summary>失败时复核报告的投递回执（成功为空串或原文）。</summary>
            public string failureNote = "";
        }

        // ———————————————————————— 指令解析 ————————————————————————

        // 班次口径：CEO 口中的「班 / 班次 / 段往返 / 往返 / 趟 / 轮」都是每日往返次数
        static readonly Regex RxWaves = new Regex(@"(\d+)\s*(?:班|个?班次|段(?:往返)?|个?往返|趟|轮)", RegexOptions.Compiled);
        /// <summary>「增加到每日 4 段」「加密到每天 3 班」「减到 1 班」——取动词后的目标数字，而不是现状。</summary>
        static readonly Regex RxTargetWaves = new Regex(
            @"(?:增加(?:到|至)?|加密(?:到|至)?|调(?:整)?到|调(?:整)?为|改(?:到|成|为)|升到|提到|加到|减到|减少到|压缩到|砍到)[^0-9]{0,20}(\d+)\s*(?:班|个?班次|段(?:往返)?|个?往返|趟|轮)",
            RegexOptions.Compiled);

        // —— 中文数字（★2026-10-13 四次报障）——
        // CEO 写「把FR106/FR107**调整为每日一段往返**」，量是**说清楚了的**（1 段往返/日），
        // 但上面两条正则只认阿拉伯数字 → waves 读成 0 → NeedsConfirm 拦下回显，
        // 或更糟：CEO 按 Enter 执行时 ResolveDeps 退化成「现状 + 1 段」，把「每日一段」办成「每日两段」。
        // 故补中文数词这一路。负向断言排除「**这**一段 / **那**一段 / **每**一段 / **第**一段 / 前/后/同/某」——
        // 那些是指代，不是量（「这一段运力太紧」不是要他排 1 段）。
        const string CnNum = "一二两三四";
        static readonly Regex RxWavesCn = new Regex(
            @"(?<![这那每前后同某上下第" + CnNum + @"])([" + CnNum + @"])\s*(?:班|个?班次|段(?:往返)?|个?往返|趟|轮)",
            RegexOptions.Compiled);
        static readonly Regex RxTargetWavesCn = new Regex(
            @"(?:增加(?:到|至)?|加密(?:到|至)?|调(?:整)?到|调(?:整)?为|改(?:到|成|为)|升到|提到|加到|减到|减少到|压缩到|砍到)[^0-9" + CnNum + @"]{0,20}([" + CnNum + @"])\s*(?:班|个?班次|段(?:往返)?|个?往返|趟|轮)",
            RegexOptions.Compiled);

        /// <summary>
        /// 增班意图词表。**与减班词表严格互斥**：两者都不命中才算「没读懂」。
        ///
        /// 关键词兜底（分析服务不可用时）的判定顺序是「维持 &gt; 减班 &gt; 增班 &gt; 不明」——
        /// 把不明单列出来而不是并进增班，是整个改动里最关键的一行。
        /// </summary>
        static readonly string[] IntentIncreaseWords =
            { "加密", "增加", "增班", "提升", "提高", "加大", "多排", "扩充", "加开", "升到", "提到", "调到", "加到" };

        static readonly string[] IntentKeepWords =
            { "保持", "维持", "不变", "不动", "暂不调整", "先不改" };

        static readonly string[] IntentReduceWords =
            { "削减", "减少", "减班", "压缩", "砍", "收缩", "降", "少排", "收回" };

        /// <summary>
        /// 指派/改派执飞飞机意图词表（2026-10-13）。
        ///
        /// 这一族词**必须先于增/减判定**：CEO 说「安排一架合适的飞机执飞此航班」，
        /// 里面既没有增班也没有减班的信号，落进旧逻辑就是「没读懂」。而这句话要办的事
        /// 根本不是班次，是**指派机号**（本线可能正因为无机而停飞）。
        ///
        /// 词表刻意只收「明确指向飞机/机号」的说法：单纯出现「飞机」二字不算（「这架飞机排得太满」
        /// 是排班问题的抱怨，不是改派指令），必须能与「换/派/安排 + 飞机/机型/机号」对应上。
        ///
        /// ★2026-10-13 二次修（CEO 实测报障）：他把话说成**「请给此航段分配可执飞的飞机」**，
        /// 不在下面这张表里的任何一条上 —— 于是又回到「没读懂」。**枚举短语这条路本身就有尽头**：
        /// 玩家总会换一种说法。故本表退为「快路」，另配一条**结构性判据**
        /// （<see cref="LooksLikeAircraftAssignment"/>：飞机身份词 + 指派类动词）覆盖未枚举的说法。
        /// </summary>
        static readonly string[] IntentAssignWords =
        {
            "安排飞机", "安排一架", "安排合适", "派一架", "派机", "配机", "指派飞机", "指定飞机",
            "改派", "换飞机", "换机", "换个飞机", "换一架", "换机型", "改机型", "换执飞", "改执飞",
            "合适的飞机", "合适的机型", "无机可派", "没飞机飞", "待复航", "复航", "执飞此航班",
            "执飞本线", "安排执飞", "派飞机", "调一架"
        };

        /// <summary>
        /// 「飞机身份」词：说的是**哪一架 / 什么机型**，而不是**几班**。
        /// 与 <see cref="AssignIntentWords"/> 同时命中才判为派机 —— 二者缺一都不足以定性
        /// （只有身份词是抱怨「这架飞机排得太满」，只有动词是排班「换到红眼」）。
        ///
        /// 刻意**不收「航段」「运力」**：它们是这条线的口径，不是飞机 ——「给此航段安排一下」
        /// 靠这两个词就会被误判成派机，而那句话根本没说清楚要办什么（该问，不该猜）。
        /// 机型代号（A330、B787、A321LR、B-8802）另由 <see cref="HasTypeCodeHint"/> 兜。
        /// </summary>
        static readonly string[] AircraftIdentityWords =
            { "飞机", "机型", "机号", "机队", "宽体", "窄体", "执飞", "波音", "空客", "商飞" };

        /// <summary>
        /// 指派类动词 / 疑问词（谁、哪架…）：把某个主体「给 / 派 / 换 / 调 / 选」到这条线上。
        /// 刻意不收「租」——「租一架 A320」是运力引进，不是把现有飞机派到这条线。
        /// </summary>
        static readonly string[] AssignIntentWords =
        {
            "分配", "指派", "指定", "安排", "派", "配", "换", "调", "选",
            "谁", "哪架", "哪一型", "用哪", "交给", "改由", "改派"
        };

        /// <summary>句中是否提到「飞机」这件事（用于把确认文案问成两句有意义的选择，而不是问无关的方向）。</summary>
        public static bool MentionsAircraft(string raw)
            => raw != null && (ContainsAny(raw, AircraftIdentityWords) || HasTypeCodeHint(raw));

        // ———————————————————————— 询问 vs 调令（2026-10-13） ————————————————————————

        /// <summary>
        /// 询问现状的疑问词：问「为什么 / 怎么 / 哪几天 / 多少」。
        ///
        /// 与 <see cref="RequestMarks"/> 分开：这一族是**想知道**，那一族是**请办事**。
        /// 例如「为何周三不安排执飞？」只要答案，运营部不该动任何东西；
        /// 「能否派一架飞机来飞？」却在请求派机（而派机是零副作用意图，照办更贴心）。
        /// </summary>
        static readonly string[] InquiryMarks =
        {
            "为何", "为什么", "为么", "怎么", "怎样", "如何", "哪几", "哪些", "哪个", "哪家",
            "多少", "几点", "多久", "多长时间", "多长", "什么", "吗", "呢",
            // 「每周飞几班」「一天几班」这类**没写问号也没写疑问词**的口语问法，
            // 靠下面这几条兜住。刻意**不**收裸的「几班」：「加几班」是没写清量级的调令，
            // 该走「请确认」的回显（问一句要不要办），不该被当成咨询。
            // 故只收「几」紧跟疑问语境的说法（飞几 / 每周几 / 每天几 / 排几班）。
            "飞几", "每周几", "一周几", "一天几", "每天几", "排几班"
        };

        /// <summary>请求许可的疑问词（能不能 / 能否 / 可否 …）：这一族是在**请办事**。</summary>
        static readonly string[] RequestMarks =
            { "能不能", "能否", "可否", "可不可以", "可以吗", "好不好", "好吗", "行吗" };

        /// <summary>
        /// 这句话里有没有「要办一件事」的**方向信号**。
        ///
        /// 只有增 / 减 / 维持 / 派机这四族算 —— 它们表达「我要它变成什么样」。
        /// 刻意**不**把「提到某个时段/班期/班次数字」算进来：那是**指代**不是命令 ——
        /// 「为何只排早班？」「为什么只有 1 班？」都提到时段与班次，却都是纯粹的疑问。
        /// </summary>
        public static bool HasDirectiveSignal(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            if (ContainsAny(raw, IntentIncreaseWords) || ContainsAny(raw, IntentReduceWords)
                || ContainsAny(raw, IntentKeepWords)) return true;
            return ContainsAny(raw, IntentAssignWords) || LooksLikeAircraftAssignment(raw);
        }

        /// <summary>
        /// 这是一句**询问**吗？（问「为什么/哪几天/多少」，而且没有要办的方向）
        ///
        /// 判据三段：
        /// <list type="number">
        /// <item>句里有增/减/维持的方向词 → **不是**询问。哪怕写成问句也是在请求办事
        ///   （「能不能加密到每天 3 班？」照旧走调令；办不成时呈报会把受阻原因讲清）；</item>
        /// <item>句里有疑问词（为何/为什么/怎么/哪几/多少/什么…）或以问号收尾 → 是询问；</item>
        /// <item>例外：请求型疑问词（能不能/能否/可否）**加上**明确的指派结构 → 按派机办。
        ///   这一条只为准许「能否派一架飞机来飞本线？」这类真请求 —— 派机是零副作用意图
        ///   （只发一封提名呈报，退回等于没提过），照办比只回一封信更贴心。</item>
        /// </list>
        ///
        /// ★为什么「安排」二字不再挡路：「为何周三不安排执飞？」里的「不安排」是**否定式**，
        /// 子串匹配却命中了指派动词表 —— 旧版因此把一句抱怨当成改派命令执行掉了。
        /// 疑问判据排在意图判定之前，就是为了让这类句子先被认成「问」。
        /// </summary>
        public static bool LooksLikeQuestion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim();
            // An explanation can mention increasing/reducing flights without requesting either.
            // Explicit "please do X" clauses still take the normal directive/confirmation path.
            bool explanation = s.Contains("为什么") || s.Contains("为何") || s.Contains("原因") || s.Contains("依据");
            bool explicitOrder = Regex.IsMatch(s, @"(?:请|立即|马上)\s*(?:把[^。？?]{0,20})?(?:增加|加密|减少|削减|调整|维持)");
            if (explanation && !explicitOrder) return true;
            if (ContainsAny(s, IntentIncreaseWords) || ContainsAny(s, IntentReduceWords)
                || ContainsAny(s, IntentKeepWords)) return false;

            bool inquiry = ContainsAny(s, InquiryMarks) || s.EndsWith("？") || s.EndsWith("?");
            if (!inquiry) return false;

            bool requesting = ContainsAny(s, RequestMarks) && LooksLikeAircraftAssignment(s);
            return !requesting;
        }

        /// <summary>
        /// 机型族前缀：航司**不会**拿它们当航班号前缀（航班号是 2 个字母，如 FR106 / SU100 / CZ3456）。
        /// 单字母 A/B/C/E/F（空客/波音/商飞/巴航/福克）+ 双/三字母的 MD/DC/CRJ/ARJ/ATR/IL/TU/SSJ/Y。
        /// </summary>
        static readonly string[] TypeCodePrefixes =
            { "A", "B", "C", "E", "F", "Y", "MD", "DC", "CRJ", "ARJ", "ATR", "IL", "TU", "SSJ" };

        /// <summary>
        /// 机型代号线索：**飞机制造厂/族前缀** + 2–4 位数字 → A330 / B787-9 / C919 / E190 / MD80 / CRJ900 / B-8802。
        /// 收它的理由：CEO 常直接说机型号而不说「飞机」二字（「把 A330 调到本线」）。
        /// 门槛设成「至少两位数字」是为了不把 T2 航站楼、第 1 项之类的编号当成机型。
        ///
        /// ★2026-10-13 四次报障修（「把FR106/FR107调整为每日一段往返」被当成派机令）：
        /// 旧版只按形状认（1–3 个字母 + 2–4 位数字），于是**本线的航班号** FR106 与 A330 同形，
        /// 被当作机型代号 → 与「调整」里的「调」凑成「身份词 + 指派动词」→ 运营部把一句班次调令
        /// 当成改派飞机去提名机号，班次一点没动。航班号前缀（FR/SU/CZ/MU/HU/MF/JL…）是航司两字码，
        /// 与机型族前缀交集为空，故这里改为**认前缀**而不是认形状。
        /// </summary>
        static bool HasTypeCodeHint(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int i = 0;
            while (i < s.Length)
            {
                // ★只认 **ASCII 字母**：char.IsLetter 对汉字也返回 true，用它会得出一堆假机型 ——
                // 「换到每天12班」里的「到每天」会被当成字母串，后面接 12 就成了机型号。
                if (!IsAsciiLetter(s[i])) { i++; continue; }
                // ★数字后面紧跟字母串的是**混合编号**（3U1234 = 川航航班号；B738 的 B 前面不是数字），
                // 不能把「U1234」当成机型，故前一字符是数字时整段跳过。
                if (i > 0 && char.IsDigit(s[i - 1])) { i++; continue; }
                int start = i;
                while (i < s.Length && IsAsciiLetter(s[i])) i++;   // 贪婪吃掉整串字母再数长度
                int letters = i - start;
                if (letters > 3) continue;                          // 4 个以上字母：英文单词，不是机型号
                string prefix = s.Substring(start, letters).ToUpperInvariant();
                if (Array.IndexOf(TypeCodePrefixes, prefix) < 0) continue;
                int j = i;
                if (j < s.Length && s[j] == '-') j++;
                int digits = 0;
                while (j < s.Length && char.IsDigit(s[j])) { j++; digits++; }
                if (digits >= 2 && digits <= 4) return true;
            }
            return false;
        }

        static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        /// <summary>
        /// 结构性判据：**身份词 + 指派动词**同时出现 → 这是一句「改派执飞飞机」的指令。
        ///
        /// 为什么这条可以放宽：四种意图里只有 assign 是**零副作用**的 —— 它只发一封提名呈报，
        /// 不动班次、不动机号，认错了在收件箱退回即可（退回等于没提过）。而判成 increase/reduce
        /// 会真的改排班。故「宁可多认几个改派说法」在方向上是安全的那一侧。
        /// </summary>
        static bool LooksLikeAircraftAssignment(string raw)
            => MentionsAircraft(raw) && ContainsAny(raw, AssignIntentWords);

        public static string BuildParseSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.Append("你是航空公司运营部调度。用户是 CEO，用一句话下达运力调整指令。只输出 JSON：");
            sb.Append("{\"intent\":\"increase|reduce|keep|assign\",\"waves\":3,\"depsCsv\":\"08:00,12:00,18:00,22:00\",");
            sb.Append("\"days\":\"每日\",\"buildRing\":true,\"rationale\":\"一句中文说明\"}。");
            sb.Append("intent=assign 专指「指派/改派执飞飞机」，凡「给这条线分配/安排/指派一架（合适的、可执飞的）飞机」"
                    + "「换机型」「谁来执飞这条线」都算 assign（如「安排一架合适的飞机执飞此航班」「请给此航段分配可执飞的飞机」），");
            sb.Append("此时 waves/depsCsv/days 一律填 0/空串/空串（本系统会另行按机队条件提名候选，不需要你排班次）；");
            sb.Append("depsCsv 只能从 08:00、12:00、18:00、22:00 中选（早班/午班/晚班/红眼）；");
            sb.Append("waves 为目标每日往返段数 1~4（CEO 说「往返段数/几段往返/几班」都按这个填）；");
            sb.Append("若指令里同时出现现状和目标（如「由每日1段增加到每日4段」），waves 必须填目标（4），绝不能填现状（1）；无法判断填 0；");
            sb.Append("buildRing 表示是否为该机编连班航班环（优先跨航线同枢纽，其次本航线去程+回程往返环）。");
            return sb.ToString();
        }

        public static string BuildParseUserPrompt(GameState gs, RouteData route, string text)
        {
            var sb = new StringBuilder();
            if (route != null)
            {
                route.EnsureFlights();
                var demand = RouteGravityModel.EvaluateRoute(gs, route);
                sb.Append($"航线 {route.origin}-{route.destination}，航程 {route.distanceKm:F0} km，机型 {route.aircraftType}，");
                sb.Append($"当前 {route.DualWeeklyLine()}，机号 {RegsOf(route)}，");
                sb.Append($"日利 {route.dailyProfit:F1} 万，周需求 {demand.weeklyPax:N0} 人次。");
            }
            sb.Append($"\nCEO 指令：{text}");
            return sb.ToString();
        }

        /// <summary>
        /// 这条指令是否**必须先给 CEO 过目再执行**。
        ///
        /// 判据只有一条：系统要不要「猜」。CEO 说清了「增还是减」以及「几班」（或哪几个时段），
        /// 就直接办 —— 保持「一句话下达」的爽感；只要有一处要猜，就先回显方案请确认。
        ///
        /// 之所以不能猜着执行：解析默认是增班，猜错的方向恰好是「玩家想砍、系统给加密」，
        /// 而这类错误当时看不出来（班次真的变了、邮件也真的发了），只在账面上慢慢显形。
        /// </summary>
        public static bool NeedsConfirm(Directive d)
        {
            if (d == null) return false;
            if (d.query) return true;                              // 询问不是调令：一律拦下，由调用方转答复件
            if (d.intent == "assign") return false;                // 指派飞机只发一封提名呈报，不动班次、无副作用
            if (!d.intentKnown) return true;                       // 增/减都没读懂 —— 绝不猜
            if (d.intent == "keep") return false;                  // 维持现状无副作用
            if (d.waves > 0) return false;                         // 说了几班
            if (d.deps != null && d.deps.Length > 0) return false; // 说了哪几个时段
            // 只剩「加密」「减班」这类**只有方向没有量**的说法。注意这里刻意**不**放行 maximize：
            // 「加密」一词会同时点亮 maximize（旧词表把它当作「挖满全天」的信号），
            // 若因此放行，一句「这条线运力偏紧，加密一下」就会从 1 班直排 4 段 —— 方向没错，
            // 幅度却是系统自己定的，正是该请 CEO 过目的情形（「提到最大」那种真说了量的另有 deps 兜住）。
            return true;
        }

        /// <summary>
        /// 一句话里**同时**写了「改派飞机」和「增/减班」时的如实交代（无混写则返回空串）。
        ///
        /// 指派一族排在增/减判定之前是有理由的（「安排一架合适的飞机执飞此航班」里没有班次信号，
        /// 落进增减判定就是「没读懂」）。代价是：若 CEO 真的把两件事写进同一句
        /// （「换机型并减到每天 1 班」），系统只办改派，减班那半句会被**静默丢掉** ——
        /// 这与 §14.4「不猜」是同一种病，且更难发现（邮件真的发了、飞机真的换了）。
        /// 故此处如实写明「只办了哪一半、另一半请另下一条」，而不是假装读全了。
        /// </summary>
        public static string MixedCapacityNote(string raw)
        {
            string s = raw ?? "";
            if (string.IsNullOrWhiteSpace(s)) return "";
            bool keep = ContainsAny(s, IntentKeepWords);
            bool reduce = ContainsAny(s, IntentReduceWords);
            bool increase = ContainsAny(s, IntentIncreaseWords);
            OpsScheduleLogic.ParseCeoNote(s, out bool allDay, out bool maximize);
            bool hasWaves = ReadWaves(s) > 0 || allDay || maximize;
            if (!keep && !reduce && !increase && !hasWaves) return "";
            return "注意：本句另有班次调整的表述（增/减班或班期），本次只办「改派执飞飞机」，不动班次；"
                 + "班次请另行下达一条指令。";
        }

        /// <summary>
        /// 待确认时的预览：把「系统打算怎么改」翻成 CEO 能一眼核对的短句。
        /// 复用 <see cref="ResolveDeps"/> 的取值顺序，保证预览与实际执行**同源** ——
        /// 预览说 3 段、执行给 4 段是最伤信任的一种 bug。
        /// </summary>
        public static string PlanLine(RouteData route, Directive d)
        {
            if (d == null) return "";
            if (d.query) return "把您的问题转呈运营部，答复以邮件呈报（不改动班次与机号）";
            if (d.intent == "assign") return "运营部提名 2–3 架合适飞机（含投影收益），请选定一架";
            if (d.intent == "keep") return "维持现有班次，不改动运力";
            // 方向都没读懂时**不冒充方向**：旧版走默认 increase，于是 CEO 写「把班次收一收」，
            // 屏幕上却堂堂正正写着「加密至 2 段往返」——「拟定」是系统的建议，不能是系统的臆测。
            // 读到的量照报，方向留给 CEO 明确。
            if (!d.intentKnown)
            {
                if (d.waves > 0) return $"调整为 {d.waves} 段往返（方向待您明确：加密或削减）";
                if (d.deps != null && d.deps.Length > 0)
                    return $"调整到 {string.Join("、", d.deps)}（方向待您明确：加密或削减）";
                return "（未读到方向与幅度，暂不拟定）";
            }
            // 无航线上下文（换线 / 刷新间隙里重绘提示）时**不得**去算「当前 N 段 +1」——
            // 那既没有依据，也会把 null 传进 ResolveDeps 直接崩掉。这里只报已说清的部分。
            if (route == null)
            {
                var sb0 = new StringBuilder();
                sb0.Append(d.intent == "reduce" ? "削减运力" : "加密运力");
                if (d.waves > 0) sb0.Append($" 至 {d.waves} 段往返");
                if (d.deps != null && d.deps.Length > 0) sb0.Append($"（{string.Join("、", d.deps)}）");
                return sb0.ToString();
            }
            string[] deps = ResolveDeps(route, d);
            int n = deps != null ? deps.Length : 0;
            var sb = new StringBuilder();
            sb.Append(d.intent == "reduce" ? "削减至 " : "加密至 ");
            sb.Append(n > 0 ? n + " 段往返" : "自动定班");
            if (deps != null && deps.Length > 0) sb.Append("（" + string.Join("、", deps) + "）");
            if (!string.IsNullOrEmpty(d.days)) sb.Append(" · 班期 " + d.days);
            sb.Append(" · " + (d.buildRing ? "编连班环" : "不编环"));
            sb.Append($"　当前 {route.FlightCount} 段");
            return sb.ToString();
        }

        /// <summary>
        /// 待确认态按 Enter 能不能直接执行？——**方向没读懂的不能**。
        ///
        /// 此时「拟定」的东西要么空着，要么是系统按默认增班填的；让 CEO 按一次 Enter 就把它办了，
        /// 等于他替系统背了这个猜 —— 而这正是本函数存在要防的那件事（猜错的方向恰好是「想砍班、
        /// 结果加密」，且执行后班次真的变了、邮件也真的发了，当时看不出错）。
        /// </summary>
        public static bool CanConfirmOnEnter(Directive d)
            => d != null && d.intentKnown;

        /// <summary>
        /// 待确认时的完整提示（正文两行 + 规范词速查）。
        /// 措辞刻意先说「我没读懂什么」，再说「怎么改写」—— 否则玩家会把确认当成一次普通点击。
        /// </summary>
        public static string ConfirmPrompt(RouteData route, Directive d, bool fromAi)
        {
            var sb = new StringBuilder();
            if (d.query)
            {
                // 询问不是调令。这里刻意**先说不做什么**：CEO 看到红字会以为系统又没读懂，
                // 而真相是「读懂了，但这句是问句」——所以要把「您在问、我不动手」说清楚，
                // 再告诉他答复在哪、想办事该怎么写。
                sb.Append("ℹ 这是一句询问，不是调令：本线运力与机号不会被改动。\n");
                sb.Append("拟定　").Append(PlanLine(route, d)).Append('\n');
                sb.Append("答复　运营部正按本线台账逐条答复（班期口径 / 运力上限 / 执飞机 / 时刻槽 / 值勤 / 收益），"
                        + "以答复件形式发到收件箱。\n");
                sb.Append("若要办一件事，请改写为「加密到每天 N 班」「削减到每天 N 班」"
                        + (MentionsAircraft(d.raw) ? "「安排一架合适的飞机执飞」" : "") + "。");
                return sb.ToString();
            }
            if (!d.intentKnown)
            {
                // 问成两句**有意义**的选择。旧文案一律问「想增班还是减班」——可 CEO 说的是
                // 「给此航段分配可执飞的飞机」，他脑子里根本没有增/减这回事，这句问话只会让人
                // 以为系统坏了（2026-10-13 实测就是这么报上来的）。
                if (MentionsAircraft(d.raw))
                    sb.Append("⚠ 没读懂这句要办哪件事：是「改派执飞飞机」还是「调整班次」。为避免办反，先不做任何改动。\n");
                else
                    sb.Append("⚠ 没读懂这条指令想「增班」还是「减班」，为避免办反，先不做任何改动。\n");
            }
            else
                sb.Append("⚠ 指令没有说明改几班，运营部拟好方案，请先核对再执行。\n");
            sb.Append("拟定　").Append(PlanLine(route, d)).Append('\n');
            if (CanConfirmOnEnter(d))
                sb.Append("按 Enter 执行；或改写为「加密到每天 N 班」「削减到每天 N 班」重新下达。");
            else if (MentionsAircraft(d.raw))
                sb.Append("若指派机，请改写为「安排飞机执飞」（词表见下）；若指班次，请写「加密到每天 N 班」或「削减到每天 N 班」。");
            else
                sb.Append("请改写为「加密到每天 N 班」或「削减到每天 N 班」后重新下达（方向不明时不会代您执行）。");
            sb.Append("\n").Append(KeywordHint());
            sb.Append("　（本次解析：").Append(ChannelLabel(d, fromAi)).Append("）");
            return sb.ToString();
        }

        /// <summary>
        /// 规范词速查（常驻在指令框下方）。**这张表不是装饰**：关键词兜底通道只认这些词，
        /// 而分析服务可能因未配 Key / 请求过频 / 超时 / 返回非法 JSON 而静默缺席，
        /// 玩家需要随时看得到「说什么才一定算数」。
        /// </summary>
        public static string KeywordHint()
            => "规范词　增班 加密/增加/提升　减班 削减/减少/压缩/砍　维持 保持/不变　"
             + "派机 分配/安排/指派一架飞机·派机·改派·换机型　"
             + "班次 N 班或 N 段往返　时段 早班08:00·午班12:00·晚班18:00·红眼22:00　"
             + "全天 早班到红眼/挖满/最大　班期 每日/一三五/二四六　环 编连班环/不组环　"
             + "咨询 直接问（「为何周三不排班」）→ 运营部发答复件，不当作调令";

        /// <summary>关键词解析（分析服务不可用时的兜底）。</summary>
        public static Directive Parse(string text)
        {
            var d = new Directive { raw = text ?? "" };
            string s = text ?? "";
            if (string.IsNullOrWhiteSpace(s))
            {
                d.analysis = "未收到指令内容。";
                return d;
            }

            // —— 记忆优先：CEO 自己确认过含义的原话，优先级高于关键词猜测 ——
            // 它是「他真这么说过、并且批准过结果」，比子串匹配可靠。
            string norm = NormalizeRaw(s);
            if (_learnedLookup != null && norm.Length > 0
                && _learnedLookup(norm, out LearnedDirective learned) && learned != null)
            {
                d.fromLearned = true;
                d.intentKnown = true;
                d.intent = string.IsNullOrEmpty(learned.intent) ? "increase" : learned.intent;
                d.waves = learned.waves;
                d.deps = learned.deps != null && learned.deps.Length > 0 ? learned.deps : null;
                d.days = learned.days ?? "";
                d.buildRing = learned.buildRing;
                d.maximize = learned.maximize;
                d.analysis = DescribeAnalysis(d);
                return d;
            }

            // —— 询问优先于一切意图（2026-10-13 三次报障）——
            // CEO 写「为何 FR106/107 周三、周五、周日不安排执飞？」时，句里有「安排」（指派动词）
            // 又有「FR106」（与 A330 同形的字母+数字），旧判据把他的一句抱怨当成**改派飞机**照办了。
            // 现在：问句不解析成意图、不执行、不猜，交给 OpsQueryLogic 用真实数据回一封答复件。
            // 位置刻意在所有意图判定**之前** —— 这是「先分清是问还是令」的一道闸门。
            if (LooksLikeQuestion(s))
            {
                d.query = true;
                d.intentKnown = false;
                d.analysis = DescribeAnalysis(d);
                return d;
            }

            // —— 指派/改派执飞机优先：这一族话里没有增/减班信号，落进下面就会判成「没读懂」，
            //    而它要办的是机号（本线可能正因为无机而停飞），与班次多少无关。
            //    两条路：① 枚举快路（说出的是我们预见过的说法）② 结构性判据（身份词 + 指派动词）。
            if (ContainsAny(s, IntentAssignWords) || LooksLikeAircraftAssignment(s))
            {
                d.intentKnown = true;
                d.intent = "assign";
                d.buildRing = true;
                d.analysis = DescribeAnalysis(d);
                string mixed = MixedCapacityNote(s);
                if (!string.IsNullOrEmpty(mixed)) d.analysis += "　" + mixed;
                return d;
            }

            bool keep = ContainsAny(s, IntentKeepWords);
            bool reduce = !keep && ContainsAny(s, IntentReduceWords);
            bool increase = !keep && !reduce && ContainsAny(s, IntentIncreaseWords);
            // 三者都不命中 = 系统没读懂 CEO 想增还是想减。这时**不能**默认成增班：
            // 玩家说「这条线不赚钱，把班次收一收」，默认增班就会把 1 班加密到 4 班。
            // 故 intentKnown 单列，交由 NeedsConfirm 拦下先请确认（AI 解析回来会把它置真）。
            d.intentKnown = keep || reduce || increase;
            d.intent = keep ? "keep" : reduce ? "reduce" : "increase";

            OpsScheduleLogic.ParseCeoNote(s, out bool allDay, out bool maximize);
            d.maximize = maximize || allDay;
            d.waves = ReadWaves(s);
            if (reduce && d.waves <= 0) d.waves = 1;
            d.deps = ReadSlots(s);
            d.days = ReadDays(s);

            bool noRing = ContainsAny(s, "不组环", "不要环", "不连班", "不串", "单飞", "只飞一段", "不衔接");
            bool wantRing = ContainsAny(s, "组环", "连班", "串联", "航班环", "衔接", "对飞", "回程");
            d.buildRing = wantRing || !noRing;

            d.analysis = DescribeAnalysis(d);
            return d;
        }

        /// <summary>叠加分析服务返回的 JSON（能识别的字段覆盖关键词结果）。</summary>
        public static void MergeAiJson(Directive d, string json)
        {
            if (d == null || string.IsNullOrWhiteSpace(json)) return;
            // 询问不是调令：模型无权把一句「为什么」改写成可执行的意图。
            // （调用方通常在 Parse 之后才让模型解析，这条判断是最后一道闸门。）
            if (d.query) return;
            string intent = ReadJsonString(json, "intent");
            if (!string.IsNullOrEmpty(intent))
            {
                intent = intent.Trim().ToLowerInvariant();
                // 模型明确给出意图即视为「读懂」——哪怕关键词一个都没命中。
                // 但**记忆优先于模型**：intent 是玩家自己确认过的方向，不接受模型改写。
                bool known = intent == "increase" || intent == "reduce" || intent == "keep" || intent == "assign";
                // 关键词已明确读出「指派飞机」时（这个词族带着具体机号语义），模型无权把它改回增/减班：
                // 否则一句「请安排合适的飞机执飞此航班」在分析服务在线时又会被拉回「加密运力」的老路上。
                bool blockedByAssign = d.intent == "assign" && intent != "assign";
                if (known && !d.fromLearned && !blockedByAssign)
                {
                    d.intent = intent;
                    d.intentKnown = true;
                }
            }
            if (d.intent == "assign")
            {
                // 指派飞机不吃 waves/deps/班期这些排班字段，直接收（模型只补 rationale 更好）
                d.fromAi = true;
                d.analysis = DescribeAnalysis(d);
                string why = ReadJsonString(json, "rationale");
                if (!string.IsNullOrEmpty(why)) d.analysis += "　" + why.Trim();
                string mixedAi = MixedCapacityNote(d.raw);
                if (!string.IsNullOrEmpty(mixedAi)) d.analysis += "　" + mixedAi;
                return;
            }

            if (TryReadJsonInt(json, "waves", out int waves) && waves > 0)
            {
                // 记忆里已钉住的量同样不被改写；记忆没记量（waves=0）时，才由模型补上下文相关的幅度。
                if (!(d.fromLearned && d.waves > 0))
                {
                    int ai = Math.Min(waves, MaxWavesPerDay);
                    int kw = ReadWaves(d.raw);
                    // 增加运力时：关键词已读出的目标不可被模型误填成「现状 1 班」覆盖掉
                    if (d.intent == "reduce") d.waves = kw > 0 ? Math.Min(ai, kw) : ai;
                    else d.waves = Math.Max(ai, kw);
                }
            }

            string depsCsv = ReadJsonString(json, "depsCsv");
            string[] deps = SplitDeps(depsCsv);
            if (deps != null && deps.Length > 0 && !(d.fromLearned && d.deps != null)) d.deps = deps;

            string days = ReadJsonString(json, "days");
            if (!string.IsNullOrEmpty(days) && !(d.fromLearned && !string.IsNullOrEmpty(d.days))) d.days = days;

            if (!d.fromLearned && TryReadJsonBool(json, "buildRing", out bool buildRing)) d.buildRing = buildRing;

            d.fromAi = true;
            d.analysis = DescribeAnalysis(d);
            string rationale = ReadJsonString(json, "rationale");
            if (!string.IsNullOrEmpty(rationale))
                d.analysis += "　" + rationale.Trim();
        }

        /// <summary>「改为/调整为/调成」+ 目标值：只说目标、不说增减的那一族说法。</summary>
        static readonly string[] SetTargetWords =
            { "调整为", "调整成", "调整至", "调为", "调成", "调至", "改成", "改为", "改到" };

        /// <summary>
        /// 「把 FR106/FR107 **调整为**每日一段往返」这类**只写目标、不写增减**的调令。
        ///
        /// 关键词通道读不出方向（「调整」两个字本身没有方向），旧版于是走确认闸门，
        /// 让 CEO 对着「没读懂想增班还是减班，请改写为『加密到每天 N 班』」重写一遍 ——
        /// 可他明明把目标写得一清二楚（2026-10-13 四次报障：「发布增加每日一段往返的指令，
        /// 收件箱没有收到市场部反馈邮件」，那句原话就是「把FR106/FR107调整为每日一段往返」）。
        ///
        /// 这里把方向**算出来**，不是猜出来：目标是 CEO 写的，现状是本线台账，一比即得
        /// （目标多于现排班 = 加密，少于 = 削减）。系统没有自定任何量，执行结果就是他写的那句话。
        ///
        /// 只在**关键词与模型都没读出方向**、句子确实写了目标班次时生效；记忆命中、分析服务已给
        /// intent、询问、派机令一律不碰 —— 那几种情形各有更可靠的方向来源。
        /// </summary>
        public static void ResolveStatedTarget(RouteData route, Directive d)
        {
            if (route == null || d == null) return;
            if (d.intentKnown || d.query || d.fromLearned || d.fromAi) return;
            if (d.intent == "assign") return;
            if (!ContainsAny(d.raw, SetTargetWords)) return;
            // 没写班次数（如「把班期调整为每日」）时不在此处补方向：那会落进 ResolveDeps 的
            // 「现状 + 1 段」兜底，等于替 CEO 定了一个他没说的量 —— 仍交给确认闸门请他补一句。
            if (d.waves <= 0) return;

            int now = Math.Max(1, route.FlightCount);
            d.intent = d.waves < now ? "reduce" : "increase";
            d.intentKnown = true;
            d.statedTarget = true;
            d.analysis = DescribeAnalysis(d);
        }

        static int ReadWaves(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;

            var t = RxTargetWaves.Match(s);
            if (t.Success && int.TryParse(t.Groups[1].Value, out int target))
                return ClampWaves(target);
            // 「调整为每日一段往返」：量是用中文写的，走这一路（动词后的目标数，优先于全句最后一个数）
            var tc = RxTargetWavesCn.Match(s);
            if (tc.Success) return ClampWaves(CnDigit(tc.Groups[1].Value));

            var matches = RxWaves.Matches(s);
            var matchesCn = RxWavesCn.Matches(s);
            if (matches.Count == 0 && matchesCn.Count == 0) return 0;
            // 「由每日 1 段往返…每日 4 段往返」：后一个数字才是目标
            int atAr = matches.Count > 0 ? matches[matches.Count - 1].Index : -1;
            int atCn = matchesCn.Count > 0 ? matchesCn[matchesCn.Count - 1].Index : -1;
            if (atCn > atAr)
                return ClampWaves(CnDigit(matchesCn[matchesCn.Count - 1].Groups[1].Value));
            if (!int.TryParse(matches[matches.Count - 1].Groups[1].Value, out int n)) return 0;
            return ClampWaves(n);
        }

        /// <summary>中文数词 → 数字（只用到四，班次上限就是 4）。</summary>
        static int CnDigit(string cn)
        {
            if (string.IsNullOrEmpty(cn)) return 0;
            switch (cn[0])
            {
                case '一': return 1;
                case '两': return 2;
                case '二': return 2;
                case '三': return 3;
                case '四': return 4;
                default: return 0;
            }
        }

        static int ClampWaves(int n) => Math.Max(1, Math.Min(n, MaxWavesPerDay));

        static string[] ReadSlots(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            bool all = ContainsAny(s, "全天", "全天候", "从早班到红眼", "早班到红眼", "各时段", "所有时段", "挖满", "最大");
            if (all) return (string[])ScheduleSlots.DepTimes.Clone();

            var list = new List<string>();
            void Add(int idx)
            {
                if (idx < 0 || idx >= ScheduleSlots.DepTimes.Length) return;
                if (!list.Contains(ScheduleSlots.DepTimes[idx])) list.Add(ScheduleSlots.DepTimes[idx]);
            }
            if (s.IndexOf("早班", StringComparison.Ordinal) >= 0) Add(0);
            if (s.IndexOf("午班", StringComparison.Ordinal) >= 0) Add(1);
            if (s.IndexOf("晚班", StringComparison.Ordinal) >= 0) Add(2);
            if (s.IndexOf("红眼", StringComparison.Ordinal) >= 0) Add(3);
            if (list.Count == 0) return null;
            list.Sort((a, b) => string.CompareOrdinal(a, b));
            return list.ToArray();
        }

        static string ReadDays(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (ContainsAny(s, "每日", "每天", "天天", "每日班")) return "每日";
            if (ContainsAny(s, "一三五")) return "一三五";
            if (ContainsAny(s, "二四六")) return "二四六";
            // 显式逐日列举（「周一/三/五」「周二、周五」）——必须带「周」字头，
            // 免得把句子里的裸「三」「五」当成班期（问句在 Parse 阶段已先被拦成询问）。
            var mask = new bool[7];
            bool any = false;
            for (int i = 0; i < 7; i++)
            {
                if (s.IndexOf("周" + FlightScheduleFormat.DayMarkZh(i), StringComparison.Ordinal) < 0) continue;
                mask[i] = true; any = true;
            }
            return any ? FlightScheduleFormat.DaysSummary(mask) : "";
        }

        /// <summary>
        /// 未能执行时的统一出口：记下原因，并**把复核报告投进收件箱**。
        ///
        /// 为什么不能只把原因写在页面状态行上（2026-10-13 四次报障）：单航线页底部那行提示
        /// 向 CEO 承诺的是「结果以邮件呈报，须到收件箱批复」。失败若不发信，他按提示走一趟收件箱
        /// 却什么都找不到 —— 报障原话正是「发布增加每日一段往返的指令，收件箱没有收到反馈邮件」。
        /// 与「无机可派 → 发运力核对汇报」同源：**办不成也要把原因与出路回一封**（汇报件，不占待办）。
        ///
        /// 询问不走这里（答复件由 <see cref="InboxManager.QueueOpsQueryAnswer"/> 负责），
        /// 航线/指令不可用也不走（没有可汇报的对象）。
        /// </summary>
        static string Fail(GameState gs, RouteData route, Directive d, Report rep, string note)
        {
            InboxManager.QueueCapacityFailureReport(gs, route, d != null ? d.raw : "", note, out string queued);
            rep.failureQueued = !string.IsNullOrEmpty(queued);
            rep.failureNote = queued ?? "";
            return note;
        }

        static string DescribeAnalysis(Directive d)
        {
            var sb = new StringBuilder();
            if (d.query) return "意图　这是一句询问（不是调令）：不改动运力与机号，已转运营部答复";
            // 未读懂的意图必须如实写成「未明确」——旧版 unconditionally 走 increase 分支，
            // 于是「维持现状不变」的回显会说「目标每日在现有基础上加 1 段往返」，
            // 与实际执行（keepAsIs 不动班次）相反，纯属吓人。
            if (!d.intentKnown) sb.Append("意图　未明确（待确认）");
            else if (d.statedTarget) sb.Append("意图　按您写定的目标设定运力（方向由现状与目标比较得出）");
            else if (d.intent == "assign") sb.Append("意图　指派/改派执飞飞机（不动班次，提名后由 CEO 选定）");
            else if (d.intent == "reduce") sb.Append("意图　压缩运力");
            else if (d.intent == "keep") sb.Append("意图　维持运力");
            else sb.Append("意图　增加运力");
            if (d.intent == "assign") return sb.ToString();
            if (d.maximize) sb.Append("（挖满全天时段）");
            sb.Append("；目标每日 ");
            if (d.waves > 0) sb.Append(d.waves).Append(" 段往返");
            else if (d.deps != null && d.deps.Length > 0) sb.Append(d.deps.Length).Append(" 段往返");
            else if (d.intent == "keep") sb.Append("不变（维持现有班次）");
            else if (d.maximize) sb.Append(MaxWavesPerDay).Append(" 段往返（挖满全天）");
            else if (d.intent == "reduce") sb.Append("1 段往返");
            else sb.Append("在现有基础上加 1 段往返");
            if (d.deps != null && d.deps.Length > 0) sb.Append("（" + string.Join("、", d.deps) + "）");
            sb.Append("；班期 " + (string.IsNullOrEmpty(d.days) ? "按航线默认" : d.days));
            sb.Append("；航班环 " + (d.buildRing ? "编连班环（优先跨航线同枢纽，其次本线往返）" : "不编环（单段执飞）"));
            if (d.fromLearned) sb.Append("；依据　记忆（您此前确认过的说法）");
            return sb.ToString();
        }

        // ———————————————————————— 自动执行 ————————————————————————

        /// <summary>
        /// 运营部自动执行：编班（机号/时刻/班期）→ 地勤配齐 → 构建航班环 → 联动部门核对。
        /// 不写入任何待批准状态之外的副作用；失败时返回原因且不改动运力。
        /// </summary>
        public static Report Execute(GameState gs, RouteData route, Directive d)
        {
            var rep = new Report { directiveRaw = d != null ? d.raw : "", analysis = d != null ? d.analysis : "" };
            if (gs == null || route == null || d == null)
            {
                rep.note = "航线或指令不可用。";
                return rep;
            }

            // —— 询问：不改动任何东西（最后的兜底闸门）——
            // 正常路径上调用方已经在 Parse 之后就把问句转给运营部答复了；这里再拦一道，
            // 免得将来新加的调用点只写 Parse → Execute 就把「为什么周三不排班」当调令办了。
            if (d.query)
            {
                rep.note = "这是一句询问，不是调令：运营部未改动本线运力与机号，答复以答复件呈报。";
                return rep;
            }

            // —— 指派/改派执飞飞机：与班次无关的另一条路 ——
            // 这一族指令（「安排一架合适的飞机执飞此航班」）里没有增/减班信号，若让它落进下面的
            // ApplyWaves，ResolveDeps 会把「当前 N 段 +1」当目标 —— 在一架飞机都没有的停飞航线上
            // 那等于凭空造出一班，且恰好办反（要害是机号，不是班次多少）。
            // 故这里改交给提名通道：运营部按硬门槛挑 2–3 架候选（各带优缺点与投影日利）呈报，
            // **CEO 在收件箱选定一架才算落班** —— 与「点时段加机」同源的两段式。
            if (d.intent == "assign")
            {
                rep.assignment = true;
                rep.profitBefore = route.dailyProfit;
                rep.profitAfter = route.dailyProfit;
                bool queued = InboxManager.QueueAircraftNominationMail(gs, route, d.raw, d.analysis, out string nomMsg);
                rep.ok = queued;
                rep.note = nomMsg;
                rep.capacitySection = InboxLetter.Bullet(nomMsg);
                return rep;
            }

            int beforeWaves = route.FlightCount;
            int beforeWeekly = route.weeklyFlights;
            int beforeStaff = route.groundStaffAssigned;
            string beforeRegs = RegsOf(route);
            float beforeProfit = route.dailyProfit;
            var beforeThis = CollectRegs(route);
            var beforeAny = CollectRegs(gs.activeRoutes);
            rep.profitBefore = beforeProfit;

            // 「维持现状」且未指定班次：不动班次，只复核联动部门与航班环后呈报
            bool keepAsIs = d.intent == "keep"
                            && d.waves <= 0
                            && (d.deps == null || d.deps.Length == 0);

            string oldType = route.aircraftType;
            int requestedWaves = 0;
            List<OpsScheduleLogic.WaveShortage> shortages = null;
            if (!keepAsIs)
            {
                string[] deps = ResolveDeps(route, d);
                if (deps == null || deps.Length == 0)
                {
                    rep.note = Fail(gs, route, d, rep, "未能从指令中解析出目标班次。");
                    return rep;
                }
                requestedWaves = deps.Length;

                // 周班次权威（★2026-10-14）：CEO 明说了班期，就按「班期天数 × 波次数」排，
                // 模型（`RouteFrequencyModel`）退为**建议值**，代价写进呈报。
                // 旧版无论 CEO 说什么都由模型现算周班次 → 「加密到每天 1 班」在一周只给 4 班的
                // 长程线上永远排不出「每日」，而「加密到每天 3 班」反把周班次从 4 摊成 3。
                int honorWeekly = string.IsNullOrEmpty(d.days)
                    ? 0
                    : RouteFrequencyModel.DayCount(d.days) * requestedWaves;

                // 先留快照：呈报被退回时回退到指令前的排班
                route.BackupScheduleForDirective();
                if (!OpsScheduleLogic.ApplyWaves(route, gs.fleet, gs.activeRoutes, deps, d.days, out shortages, gs, honorWeekly))
                {
                    route.RestoreScheduleFromBackup();
                    string why = "运力不足：没有可用飞机（在干租/湿租/包机中的飞机须先召回，或先引进运力）。";
                    if (shortages != null && shortages.Count > 0 && shortages[0] != null)
                        why += $"\n{shortages[0].slotLabel} {shortages[0].dep}　{shortages[0].reason}";
                    rep.note = Fail(gs, route, d, rep, why);
                    return rep;
                }

                // ApplyWaves 会把航线标记为「待批准才执行」；运力指令是对在营航线的调整，
                // 不应冻结航班执行，故清除该标记，改由 directivePending 表示「呈报待批复」。
                route.schedulePending = false;
                route.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff(route.aircraftType);
                // 单班基准：负值＝真实亏损，必须原样透传。
                // （历史缺陷：此处曾写 `if (baseline <= 0.01f) baseline = 1f;`，把亏损基准当成「未设置」，
                //   凭空生成 +1 万/班的盈利基准并写回 baseDailyProfit —— 亏损航线一次调令即永久转盈。）
                float baseline = OpsEconomics.ResolvedSingleFlightBaseline(route);
                OpsEconomics.ApplyToRoute(route, baseline, gs.onTimePercent);
            }

            route.directivePending = true;
            rep.profitAfter = route.dailyProfit;
            rep.shortages = shortages;
            rep.shortageSection = BuildShortageSection(shortages, requestedWaves);
            // ★2026-10-16 第四十二次修订：把「目标 / 指令前」随呈报一起留下来 —— 批复回执只有拿到
            //   这两个数，才能说出「已生效 / 部分生效 / 一班没动」这一句真话（旧版回执恒定写「已生效」）。
            rep.requestedWaves = keepAsIs ? 0 : requestedWaves;
            rep.beforeWaves = beforeWaves;

            rep.capacitySection = keepAsIs
                ? InboxLetter.Bullet($"CEO 要求维持现有运力，运营部未改动班次：{route.DualWeeklyLine()} · {RegsOf(route)}。")
                : BuildCapacitySection(route, beforeWaves, beforeWeekly, beforeStaff, beforeRegs, oldType, requestedWaves);
            // 「班期 每日」这类指定不一定能照办（要与每周班次的天数一致才生效）——
            // 不能照办就**写进呈报**，别让 CEO 以为改成了（旧版这一步是静默的）。
            string daysNote = DaysHonorNote(route, d);
            if (!string.IsNullOrEmpty(daysNote)) rep.capacitySection += InboxLetter.Bullet(daysNote);
            if (!keepAsIs) rep.capacitySection += InboxLetter.Bullet(CapacityBasisNote(route));
            rep.sourcingSection = BuildSourcingSection(gs, route, beforeThis, beforeAny, beforeWaves, beforeProfit, requestedWaves, shortages, keepAsIs);
            rep.ringSection = BuildRingSection(gs, route, d, out string ringIds);
            rep.ringIdsCsv = ringIds;
            rep.slotSection = BuildSlotSection(gs, route, shortages);
            rep.slotFirst = HasSlotShortage(shortages) && requestedWaves > route.FlightCount;
            rep.crewSection = BuildCrewSection(gs, route, shortages, requestedWaves);
            rep.crewFirst = HasCrewShortage(shortages) && requestedWaves > route.FlightCount;
            rep.linkedSection = BuildLinkedSection(gs, route, beforeStaff);
            rep.impactSection = BuildImpactSection(gs, route, beforeProfit);
            rep.ok = true;
            return rep;
        }

        static string[] ResolveDeps(RouteData route, Directive d)
        {
            string[] specified = d.deps != null && d.deps.Length > 0 ? Dedupe(d.deps) : null;

            int n;
            if (d.waves > 0) n = d.waves;
            else if (specified != null) n = specified.Length;
            else if (d.maximize) n = MaxWavesPerDay;
            else if (d.intent == "reduce") n = 1;
            // route 可能为空（预览路径允许无航线上下文）：此时退化为「最少 1 段」，
            // 不拿 null 去取 FlightCount —— 这里过去是 PlanLine 崩栈的落点。
            else n = Math.Min(MaxWavesPerDay, Math.Max(1, (route != null ? route.FlightCount : 0) + 1));
            n = Math.Max(1, Math.Min(n, MaxWavesPerDay));

            // 模型若只填了早班一个时刻，但 waves=4：按目标班次把其余时段补齐
            var list = new List<string>();
            if (specified != null)
            {
                for (int i = 0; i < specified.Length && list.Count < n; i++)
                    list.Add(specified[i]);
            }
            for (int i = 0; i < ScheduleSlots.DepTimes.Length && list.Count < n; i++)
            {
                string t = ScheduleSlots.DepTimes[i];
                if (!list.Contains(t)) list.Add(t);
            }
            return list.ToArray();
        }

        static string[] Dedupe(string[] deps)
        {
            var list = new List<string>();
            for (int i = 0; i < deps.Length; i++)
            {
                string p = deps[i] != null ? deps[i].Trim() : "";
                if (p.Length < 4 || p.IndexOf(':') <= 0) continue;
                if (!list.Contains(p)) list.Add(p);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        static string BuildCapacitySection(RouteData route, int beforeWaves, int beforeWeekly, int beforeStaff, string beforeRegs, string oldType, int requestedWaves)
        {
            var sb = new StringBuilder();
            if (requestedWaves > 0 && route.FlightCount < requestedWaves)
                sb.Append(InboxLetter.Row("目标班次", $"CEO 要求每日 {requestedWaves} 段往返，实际排定 {route.FlightCount} 段（缺口见「运力缺口」）"));
            sb.Append(InboxLetter.Row("每日班次", $"{beforeWaves} 班 → {route.FlightCount} 班"));
            sb.Append(InboxLetter.Row("每周班次（离港日）", $"{beforeWeekly} 班 → {route.WeeklyFlightsDeparture()} 班"));
            sb.Append(InboxLetter.Row("每周班次（值勤日）", $"{beforeWeekly} 班 → {route.WeeklyFlightsDuty()} 班（{FlightScheduleFormat.DaysSummary(route.WeekdayOpsDuty())}）"));
            sb.Append(InboxLetter.Row("执飞机号", $"{beforeRegs} → {RegsOf(route)}"));
            if (!string.Equals(oldType, route.aircraftType, StringComparison.OrdinalIgnoreCase))
                sb.Append(InboxLetter.Row("机型", $"{oldType} → {route.aircraftType}"));
            sb.Append(InboxLetter.Row("班期", route.daysOfWeek));
            sb.Append(InboxLetter.Row("地勤", $"{beforeStaff} 人 → {route.groundStaffAssigned} 人"));
            route.EnsureFlights();
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                string slot = string.IsNullOrEmpty(f.slotLabel) ? "—" : f.slotLabel;
                // 时刻印**一次往返的真实占窗**（跨夜标 +1），与周计划/单航线页同一口径 —— 2026-10-14 报障
                sb.Append(InboxLetter.Bullet($"班次 {i + 1}　{f.registration}　{slot} {FlightScheduleFormat.WindowLabel(route, f)}　{f.daysOfWeek} / 周 {f.weeklyFlights} 班"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 运力来源：复用本线在飞机 / 调用闲置机 / 串用其它航线飞机 / 需新增运力，并给出边际日利与引进回本。
        /// </summary>
        static string BuildSourcingSection(GameState gs, RouteData route,
            HashSet<string> beforeThis, HashSet<string> beforeAny,
            int beforeWaves, float beforeProfit, int requestedWaves,
            List<OpsScheduleLogic.WaveShortage> shortages, bool keepAsIs)
        {
            var sb = new StringBuilder();
            var after = CollectRegs(route);
            var reuse = new List<string>();
            var idle = new List<string>();
            var borrow = new List<string>();
            foreach (var reg in after)
            {
                if (string.IsNullOrEmpty(reg)) continue;
                if (beforeThis != null && beforeThis.Contains(reg)) reuse.Add(reg);
                else if (beforeAny == null || !beforeAny.Contains(reg)) idle.Add(reg);
                else borrow.Add(reg);
            }
            reuse.Sort(StringComparer.OrdinalIgnoreCase);
            idle.Sort(StringComparer.OrdinalIgnoreCase);
            borrow.Sort(StringComparer.OrdinalIgnoreCase);

            int extra = Math.Max(0, route.FlightCount - beforeWaves);
            float delta = route.dailyProfit - beforeProfit;
            float unit = extra > 0 ? delta / extra
                : (route.FlightCount > 0 ? route.dailyProfit / route.FlightCount : 0f);

            if (keepAsIs)
            {
                sb.Append(InboxLetter.Bullet(
                    $"未改班次，仍由本线在飞机执飞：{(reuse.Count > 0 ? JoinRegs(reuse) : RegsOf(route))}。"));
                return sb.ToString();
            }

            sb.Append(InboxLetter.Bullet(
                $"方案摘要　本线 {beforeWaves} → {route.FlightCount} 班，日利 {beforeProfit:F1} → {route.dailyProfit:F1} 万"
                + $"（边际 {delta:+0.0;-0.0} 万/日"
                + (extra > 0 ? $"，约 {unit:F1} 万/新增班" : "")
                + $"）。来源　复用本线 {reuse.Count} 架 · 闲置投入 {idle.Count} 架 · 串用其它航线 {borrow.Count} 架。"));

            if (reuse.Count > 0)
            {
                sb.Append(InboxLetter.Bullet(
                    $"复用本线在飞机　{JoinRegs(reuse)}　继续执飞并尽量串飞加段，无借调代价；加密能否成立取决于该机剩余值勤窗口。"));
            }
            if (idle.Count > 0)
            {
                sb.Append(InboxLetter.Bullet(
                    $"调用闲置机　{JoinRegs(idle)}　指令前无定期航班，本次投入本线。"
                    + $"机会成本：备机减少 {idle.Count} 架；不挤占其它航线班次。"));
            }
            if (borrow.Count > 0)
            {
                var bits = new List<string>();
                for (int i = 0; i < borrow.Count; i++)
                {
                    string od = OtherOdsOf(gs, borrow[i], route);
                    bits.Add(string.IsNullOrEmpty(od)
                        ? borrow[i]
                        : $"{borrow[i]}（仍飞 {od}）");
                }
                sb.Append(InboxLetter.Bullet(
                    $"串用其它航线飞机　{string.Join("、", bits.ToArray())}。"
                    + "不减其它航线班次，只消耗该机剩余值勤与过站窗口；若那些航线再加密，将与本线争窗口。"));
            }

            int gap = requestedWaves > route.FlightCount ? requestedWaves - route.FlightCount : 0;
            bool needAc = ShortageNeedAircraft(shortages);
            int idleLeft = CountIdleAirworthy(gs, route, after);
            if (gap > 0 && needAc)
            {
                var cat = AirlineCEO.Data.AircraftCatalog.Find(route.aircraftType);
                float price = cat != null ? cat.priceWan : 0f;
                float lease = price * 0.022f;
                float monthly = unit * 30f;
                float payback = FleetEconomics.PaybackMonths(price, monthly);
                if (idleLeft > 0)
                {
                    sb.Append(InboxLetter.Bullet(
                        $"需新增运力　目标还差 {gap} 班，机队仍有 {idleLeft} 架适航闲置但未能排入（见「运力缺口」：值勤 / 宵禁 / 时刻槽）。"
                        + "此时引进新机也突破不了这些约束，应先解缺口再议购机。"));
                }
                else
                {
                    string pay = payback >= 900f ? "按当前边际日利无法回本"
                        : $"购机回收约 {payback:0.#} 个月";
                    sb.Append(InboxLetter.Bullet(
                        $"需新增运力　目标还差 {gap} 班，在飞机与闲置机都排不满。"
                        + $"建议引进 1 架 {route.aircraftType}"
                        + (price > 0.01f
                            ? $"：目录价 {price:F0} 万，经营租赁约 {lease:F0} 万/月；按加密一班边际日利 {unit:F1} 万估算，月利约 {monthly:F0} 万，{pay}。"
                            : "（请到机队页查看引进方案）。")
                        + "也可召回干租/湿租/包机中的飞机。"));
                }
            }
            else if (gap > 0)
            {
                sb.Append(InboxLetter.Bullet(
                    $"未排满的 {gap} 班不是缺机，而是宵禁或时刻槽等约束（见「运力缺口」）；引进飞机不能直接补上。"));
            }
            else if (reuse.Count + idle.Count + borrow.Count == 0)
            {
                sb.Append(InboxLetter.Bullet("本次未写入新的执飞机号。"));
            }
            return sb.ToString();
        }

        static bool ShortageNeedAircraft(List<OpsScheduleLogic.WaveShortage> shortages)
        {
            if (shortages == null) return false;
            for (int i = 0; i < shortages.Count; i++)
            {
                var s = shortages[i];
                if (s == null) continue;
                if (s.kind == "no_aircraft" || s.kind == "duty") return true;
            }
            return false;
        }

        static HashSet<string> CollectRegs(RouteData route)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddRegs(route, set);
            return set;
        }

        static HashSet<string> CollectRegs(IList<RouteData> routes)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (routes == null) return set;
            for (int i = 0; i < routes.Count; i++) AddRegs(routes[i], set);
            return set;
        }

        static void AddRegs(RouteData r, HashSet<string> set)
        {
            if (r == null || set == null) return;
            r.EnsureFlights();
            bool any = false;
            if (r.flights != null)
            {
                for (int i = 0; i < r.flights.Count; i++)
                {
                    var f = r.flights[i];
                    if (f != null && !string.IsNullOrEmpty(f.registration))
                    {
                        set.Add(f.registration);
                        any = true;
                    }
                }
            }
            if (!any && !string.IsNullOrEmpty(r.registration))
                set.Add(r.registration);
        }

        static string JoinRegs(List<string> regs)
        {
            if (regs == null || regs.Count == 0) return "—";
            return string.Join("、", regs.ToArray());
        }

        static string OtherOdsOf(GameState gs, string reg, RouteData except)
        {
            if (gs?.activeRoutes == null || string.IsNullOrEmpty(reg)) return "";
            var parts = new List<string>();
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null || r == except) continue;
                if (!r.IsFlownBy(reg)) continue;
                parts.Add($"{r.origin}—{r.destination}");
            }
            return parts.Count > 0 ? string.Join("、", parts.ToArray()) : "";
        }

        static int CountIdleAirworthy(GameState gs, RouteData route, HashSet<string> assigned)
        {
            if (gs?.fleet == null || route == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || string.IsNullOrEmpty(a.registration)) continue;
                if (assigned != null && assigned.Contains(a.registration)) continue;
                if (!a.isActive || a.status != AircraftStatuses.Airworthy) continue;
                if (TransitionBusinessLogic.IsBusy(gs, a.registration)) continue;
                if (!OpsScheduleLogic.RangeCovers(a.type, route)) continue;
                n++;
            }
            return n;
        }

        /// <summary>运力缺口说明：把「想要但排不出」的时段、逐机原因与可选处置写清楚。</summary>
        static string BuildShortageSection(List<OpsScheduleLogic.WaveShortage> shortages, int requestedWaves)
        {
            if (shortages == null || shortages.Count == 0) return "";
            var sb = new StringBuilder();
            sb.Append(InboxLetter.Bullet(
                $"运力缺口　CEO 要求每日 {requestedWaves} 段往返，其中 {shortages.Count} 个需求波排不出班："));
            for (int i = 0; i < shortages.Count; i++)
            {
                var s = shortages[i];
                if (s == null) continue;
                sb.Append($"　　{s.slotLabel} {s.dep}　{s.reason}\n");
                sb.Append(FormatShortageDetails(s));
            }
            sb.Append(InboxLetter.Bullet(RemedyLine(shortages)));
            return sb.ToString();
        }

        /// <summary>按缺口类型给出对症的处置建议（宵禁 / 值勤 / 时刻槽 / 机组 / 无运力，处置各不相同）。</summary>
        static string RemedyLine(List<OpsScheduleLogic.WaveShortage> shortages)
        {
            bool curfew = false, duty = false, slot = false, crew = false, dutySector = false;
            for (int i = 0; i < shortages.Count; i++)
            {
                var s = shortages[i];
                if (s == null) continue;
                if (s.kind == "curfew") curfew = true;
                else if (s.kind == "duty") duty = true;
                else if (s.kind == "no_slot") slot = true;
                else if (s.kind == "crew") crew = true;
                if (s.reason != null && s.reason.Contains("增援航段上限")) dutySector = true;
            }

            var tips = new List<string>();
            if (crew)
                tips.Add("**缺机组 = 缺飞行员**：加密班次不只买时刻槽，还要配轮换机组 —— "
                         + "到 人力 → 招聘 补飞行员（本档若未按人建机组，则编制随在役机队上涨，加机即增编），"
                         + "或先收其它航线的班次把机组腾给本线；补齐后**重下这条加密指令**即可排满。");
            if (duty || dutySector)
                tips.Add("按真实航司口径，单机单值勤期的飞行时间是硬约束（报到时刻越早/越晚、航段越多，上限越低）；"
                         + "加密只能靠增加飞机 —— 购买或租赁运力，而不是靠把同一架飞机排得更满");
            if (curfew)
                tips.Add("部分机场有夜间宵禁（欧洲机场 23:00–06:00；中国仅 PEK 单列；另有 NRT/HND/SYD/TPE），"
                         + "此时段起降不可排，只能避开宵禁或调整班期");
            if (slot)
                tips.Add("协调机场每个离港班次需占 1 个对应时段时刻槽；缺槽请批准收件箱的批量申请，下季交割后再加密（池满则需等释放或换时段）");
            tips.Add("召回干租/湿租/包机中的飞机；改班期错开高峰；或减少其它航线的时段占用；呈报「运力来源」已按复用本线 / 闲置机 / 串用其它航线拆开，缺机时含引进回本测算");

            var sb = new StringBuilder();
            sb.Append("可选处置　");
            for (int i = 0; i < tips.Count; i++)
            {
                if (i > 0) sb.Append('；');
                sb.Append($"{(char)('①' + i)} {tips[i]}");
            }
            sb.Append('。');
            return sb.ToString();
        }

        /// <summary>逐机归因明细：默认列前 4 架，其余折叠为「另 N 架」。</summary>
        static string FormatShortageDetails(OpsScheduleLogic.WaveShortage s)
        {
            if (s.details == null || s.details.Count == 0) return "";
            const int MaxShown = 4;
            var sb = new StringBuilder();
            int shown = Math.Min(s.details.Count, MaxShown);
            for (int j = 0; j < shown; j++)
            {
                if (string.IsNullOrEmpty(s.details[j])) continue;
                sb.Append($"　　　　· {s.details[j]}\n");
            }
            if (s.details.Count > shown)
                sb.Append($"　　　　· 另 {s.details.Count - shown} 架同样无法排入\n");
            return sb.ToString();
        }

        /// <summary>
        /// 班期指定是否照办？——没照办就返回一句如实交代（照办或无指定返回空串）。
        ///
        /// ★2026-10-14：口径改了。旧版周班次由 <see cref="RouteFrequencyModel"/> 现算，
        /// 于是班期只能落在「周班次 ÷ 波次」的阶梯上，没照办就劝 CEO「先改周班次（加密/削减）」——
        /// 而加密**改不动**周班次（模型现算），那是一条死路。
        /// 现在周班次以调令为准（`ApplyWaves` 的 honorWeekly），班期没落定就只可能是
        /// **某些波次排不出来**（时刻槽 / 值勤 / 机队），故这里改说这件事，并指到「运力缺口」。
        /// </summary>
        public static string DaysHonorNote(RouteData route, Directive d)
        {
            if (route == null || d == null || string.IsNullOrEmpty(d.days)) return "";
            if (string.Equals(route.daysOfWeek, d.days, StringComparison.Ordinal)) return "";
            int want = RouteFrequencyModel.DayCount(d.days);
            int waves = Math.Max(1, route.FlightCount);
            int weekly = route.WeeklyFlightsDeparture();
            return $"班期未照办　您指定「{d.days}」（{want} 天/周 × 每日 {waves} 段 = {want * waves} 班/周），"
                 + $"实际落定「{route.daysOfWeek}」（{weekly} 班/周）。差额来自**排不出的波次**（见「运力缺口」），"
                 + "不是班期被改：补齐运力 / 时刻槽 / 飞行员后重下一遍即可照办。";
        }

        /// <summary>
        /// 每班次数是谁定的 —— 写进呈报，免得 CEO 以为屏幕上的 4 班/周是「上限」。
        /// 模型（<see cref="RouteFrequencyModel"/>）现在只是**建议值**：它按「需求 × 单机日利用率目标」
        /// 给一个整数，供 CEO 参考；真正定数是调令（班期天数 × 波次数），代价落在日利用率上。
        /// </summary>
        public static string CapacityBasisNote(RouteData route)
        {
            if (route == null) return "";
            var f = RouteFrequencyModel.ForRoute(route);
            int weekly = route.WeeklyFlightsDeparture();
            string cmp = weekly == f.weeklyFlights
                ? $"与本线现排 {weekly} 班/周一致"
                : weekly > f.weeklyFlights
                    ? $"低于本线现排 {weekly} 班/周（超出建议的代价见「影响」：日利用率与效率乘数）"
                    : $"高于本线现排 {weekly} 班/周（本线尚有加密余地）";
            return $"班次口径　周班次由您的调令定（班期天数 × 每日波次 = {weekly} 班/周）；"
                 + $"班次模型建议值 {f.weeklyFlights} 班/周（需求侧 {f.flightsByDemand:0.##} 班/日、"
                 + $"供给侧 {f.flightsByAircraft:0.##} 班/日取小）{cmp} —— 模型只作建议，不作闸门；"
                 + "闸门是时刻槽、值勤与飞行员。";
        }

        /// <summary>受限波的处置建议（供答复件与预检共用，避免两处口径漂移）。</summary>
        public static string RemedyForShortages(List<OpsScheduleLogic.WaveShortage> shortages)
            => RemedyLine(shortages);

        /// <summary>单航线页预检提示（只读、不写回状态）：这条线当前最多能排几段、卡在哪几个需求波。</summary>
        public static string Precheck(GameState gs, RouteData route)
        {
            if (route == null) return "运力预检　未选择航线。";
            if (gs?.fleet == null || gs.fleet.Count == 0) return "运力预检　暂无在册飞机，无法编班。";

            var shortages = OpsScheduleLogic.PrecheckSlots(route, gs.fleet, gs.activeRoutes, out var feasible, gs);
            int max = feasible != null ? feasible.Count : 0;
            var sb = new StringBuilder();
            sb.Append($"运力预检　最多可排 {max}/{ScheduleSlots.DepTimes.Length} 段往返");
            if (feasible != null && feasible.Count > 0)
                sb.Append($"（{string.Join("、", feasible.ToArray())}）");
            if (shortages != null && shortages.Count > 0)
            {
                sb.Append("　｜　受限：");
                for (int i = 0; i < shortages.Count; i++)
                {
                    if (i > 0) sb.Append("、");
                    sb.Append($"{shortages[i].slotLabel} {shortages[i].dep}");
                    string k = ShortKind(shortages[i].kind);
                    if (!string.IsNullOrEmpty(k)) sb.Append($"（{k}）");
                }
            }

            // R5：单航线页必须讲清「为什么不能再加密」的口径
            sb.Append($"\n值勤口径　按真实航司标准：报到提前 {OpsRotationLogic.ReportBeforeMinutes} 分钟，"
                      + $"值勤上限随航段数递减（2 航段 {OpsRotationLogic.DutyLimitBySectors(2) / 60f:0.##}h → "
                      + $"4 航段 {OpsRotationLogic.DutyLimitBySectors(4) / 60f:0.##}h）；"
                      + $"两个值勤期之间最低休息 {OpsRotationLogic.MinRestMinutes / 60f:0.#} 小时。");
            if (AirlineCEO.Data.AircraftCatalog.IsWidebody(route.aircraftType))
                sb.Append($"本线使用宽体 {route.aircraftType}，可配增援机组以提高值勤上限，但须满足最低空中休息，且航段仍硬性 ≤ {OpsRotationLogic.AugmentedSectorCap} 段。");

            string co = AirlineCEO.Data.AirportCatalog.CurfewLabel(route.origin);
            string cd = AirlineCEO.Data.AirportCatalog.CurfewLabel(route.destination);
            string tier = $"{AirlineCEO.Data.AirportCatalog.TierLabelOf(route.origin)}⇄{AirlineCEO.Data.AirportCatalog.TierLabelOf(route.destination)}";
            sb.Append($"\n时刻资源　{route.origin}（{AirlineCEO.Data.AirportCatalog.TierLabelOf(route.origin)}）"
                      + $"⇄ {route.destination}（{AirlineCEO.Data.AirportCatalog.TierLabelOf(route.destination)}），机场等级 {tier}。");
            if (!string.IsNullOrEmpty(co)) sb.Append($"　{route.origin} {co}。");
            if (!string.IsNullOrEmpty(cd)) sb.Append($"　{route.destination} {cd}。");
            string slotNote = SlotGapNote(gs, route, shortages);
            if (!string.IsNullOrEmpty(slotNote)) sb.Append("\n" + slotNote);
            return sb.ToString();
        }

        /// <summary>受限波的短原因，供单航线页一眼分辨是缺槽、缺机还是值勤/宵禁。</summary>
        static string ShortKind(string kind)
        {
            switch (kind)
            {
                case "no_slot": return "缺时刻槽";
                case "crew": return CrewCapacityLogic.Label;
                case "duty": return "值勤超限";
                case "curfew": return "宵禁";
                case "no_aircraft": return "缺运力";
                default: return "";
            }
        }

        /// <summary>受限波里若含缺槽，说明已批准待交割的时刻槽何时入账（避免误以为是运力不足）。</summary>
        static string SlotGapNote(GameState gs, RouteData route, List<OpsScheduleLogic.WaveShortage> shortages)
        {
            if (route == null || shortages == null) return "";
            var shortApts = new List<string>();
            for (int i = 0; i < shortages.Count; i++)
            {
                var s = shortages[i];
                if (s == null || s.kind != "no_slot") continue;
                // ★2026-10-16：缺槽的停站**逐条**收（每个停站各是独立的一条槽）。
                // 旧版只收 `s.slotAirport`（＝第一个缺端），于是对端与经停端已批准待交割的槽不会被这份说明提到。
                var ends = RoutePermitLogic.WaveEndNeeds(route, s.dep);
                if (ends.Count == 0)
                {
                    string only = string.IsNullOrEmpty(s.slotAirport) ? route.origin : s.slotAirport;
                    if (!string.IsNullOrEmpty(only) && !ContainsIgnoreCase(shortApts, only)) shortApts.Add(only);
                    continue;
                }
                for (int k = 0; k < ends.Count; k++)
                {
                    string apt = ends[k].airport;
                    if (!string.IsNullOrEmpty(apt) && !ContainsIgnoreCase(shortApts, apt)) shortApts.Add(apt);
                }
            }
            if (shortApts.Count == 0) return "";

            gs = gs != null ? gs : GameState.Instance;
            var pend = InboxManager.PendingSlotSettlements(gs);
            var clocks = new List<string>();   // ★2026-10-15：按**具体时刻**归集，不再写「早/中/晚/夜」
            var apts = new List<string>();
            int qty = 0, y = 0, q = 0;
            if (pend != null)
            {
                for (int i = 0; i < pend.Count; i++)
                {
                    var m = pend[i];
                    if (m == null) continue;
                    if (!ContainsIgnoreCase(shortApts, m.origin)) continue;
                    string at = (string.IsNullOrEmpty(m.origin) ? "HUB" : m.origin)
                                + " " + InboxManager.SlotMailClock(m);
                    if (!clocks.Contains(at)) clocks.Add(at);
                    if (!string.IsNullOrEmpty(m.origin) && !ContainsIgnoreCase(apts, m.origin)) apts.Add(m.origin);
                    qty += InboxManager.SlotQtyOf(m);
                    y = m.effectYear; q = m.effectQuarter;
                }
            }
            if (qty > 0)
                return $"时刻槽办理　{string.Join("、", apts.ToArray())} 已批准待交割 {qty} 条（{string.Join("、", clocks.ToArray())}），"
                       + $"第{y}年 Q{q} 入账后方可排那一波；在此之前该波属「缺时刻槽」，不是缺运力。";
            return "时刻槽办理　受限波是「缺时刻槽」：每一个停站各需一对其**对应时刻**的持仓（本场端 + 对端，经停站两对），"
                   + "请批准收件箱的批量申请（下季交割），或改排各站都已有持仓的时刻。";
        }

        static bool ContainsIgnoreCase(List<string> list, string v)
        {
            if (list == null || string.IsNullOrEmpty(v)) return false;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], v, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool HasSlotShortage(List<OpsScheduleLogic.WaveShortage> shortages)
        {
            if (shortages == null) return false;
            for (int i = 0; i < shortages.Count; i++)
                if (shortages[i] != null && shortages[i].kind == "no_slot") return true;
            return false;
        }

        static bool HasCrewShortage(List<OpsScheduleLogic.WaveShortage> shortages)
        {
            if (shortages == null) return false;
            for (int i = 0; i < shortages.Count; i++)
                if (shortages[i] != null && shortages[i].kind == CrewCapacityLogic.KindCrew) return true;
            return false;
        }

        /// <summary>
        /// ★2026-10-14 CEO 第十一次问询：飞行机组产能与缺员处置。
        ///
        /// 原话「当 CEO 输入『增加此航线的航班量』，那么除了要获得更多的时刻槽，也要考虑增加轮换机组」
        /// —— 本段与「时刻槽申请」并列成第二份资源清单：加密要两样东西同时到位。
        /// 写清需求（每日 N 段要几组/几人）、可供（在册或编制基准 − 其它航线占用）、
        /// 缺口与两阶段处置（先补机组 → 再重下加密指令），与时刻槽缺口同一套形状。
        /// </summary>
        static string BuildCrewSection(GameState gs, RouteData route,
            List<OpsScheduleLogic.WaveShortage> shortages, int requestedWaves)
        {
            if (route == null) return "";
            route.EnsureFlights();
            int want = Math.Max(1, requestedWaves > 0 ? requestedWaves : route.FlightCount);
            var sb = new StringBuilder();
            sb.Append(InboxLetter.Bullet(CrewCapacityLogic.Line(gs, route, want)));
            if (HasCrewShortage(shortages))
            {
                sb.Append(InboxLetter.Bullet(CrewCapacityLogic.Remedy(gs, route)));
                string shortRole = CrewCapacityLogic.BindingRoles(gs, route, want);
                if (shortRole.Length == 0) shortRole = CrewCapacityLogic.RolePilot + "、" + CrewCapacityLogic.RoleCabin;
                sb.Append(InboxLetter.Bullet($"两阶段　先补机组（人力 → 招聘 补 {shortRole}；本档若未按人建机组，则加机抬编制）"
                    + "→ 再重下这条加密指令，运营部按新机组产能重排班次。本呈报已排班次可单独批准生效。"
                    + "注意「机组」＝驾驶舱（飞行员）+ 客舱（乘务员）两支不可互替的队伍，两岗都要到位。"));
            }
            else
            {
                sb.Append(InboxLetter.Bullet("机组产能足够覆盖本次班次（驾驶舱与客舱两岗都已核算）；"
                    + "退回呈报时按指令前的机组占用恢复。"));
            }
            return sb.ToString();
        }

        /// <summary>P4：本航线已排班次的槽位占用（各停站）+ 缺槽申请说明。</summary>
        static string BuildSlotSection(GameState gs, RouteData route, List<OpsScheduleLogic.WaveShortage> shortages)
        {
            var sb = new StringBuilder();
            if (route == null) return "";
            var needsAll = RoutePermitLogic.SlotNeeds(route);
            if (needsAll.Count == 0)
            {
                sb.Append(InboxLetter.Bullet($"{route.origin} ⇄ {route.destination} 各停站均为非协调机场，离港班次无需时刻槽。"));
                return sb.ToString();
            }

            route.EnsureFlights();
            sb.Append(InboxLetter.Bullet("口径　一停站一对槽：飞机在哪一站停一次，那一站就要一对（到场 + 离场）。"
                + "本场端 1 对、对端 1 对；**经停站一趟往返停两次，故要 2 对**（去程一次、回程一次）。"));
            if (RoutePermitLogic.NeedsDepartureSlot(route.origin))
                sb.Append(InboxLetter.Bullet($"本场端 {route.origin}（{AirportCatalog.TierLabelOf(route.origin)}，协调机场）"));
            else
                sb.Append(InboxLetter.Bullet($"本场端 {route.origin} 非协调机场，无需槽"));
            string viaLeg = RoutePermitLogic.StopoverOf(route);
            if (!string.IsNullOrEmpty(viaLeg))
            {
                if (RoutePermitLogic.NeedsDepartureSlot(viaLeg))
                    sb.Append(InboxLetter.Bullet($"经停端 {viaLeg}（{AirportCatalog.TierLabelOf(viaLeg)}，协调机场）"
                        + "　—— 经停站要**两对**：去程一次停留、回程一次停留"));
                else
                    sb.Append(InboxLetter.Bullet($"经停端 {viaLeg} 非协调机场，无需槽"));
            }
            if (RoutePermitLogic.NeedsDepartureSlot(route.destination))
                sb.Append(InboxLetter.Bullet($"对端 {route.destination}（{AirportCatalog.TierLabelOf(route.destination)}，协调机场）"));
            else
                sb.Append(InboxLetter.Bullet($"对端 {route.destination} 非协调机场，无需槽"));

            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                FlightScheduleFormat.Numbers(route, i, out string outbound, out string inbound);
                string slot = string.IsNullOrEmpty(f.slotLabel) ? ScheduleSlots.LabelForDep(f.depTime) : f.slotLabel;
                sb.Append(InboxLetter.Row(
                    $"班次 {i + 1} {outbound}/{inbound} {slot} {f.depTime}",
                    RoutePermitLogic.SlotStatusOf(gs, route, i)));
                var needs = RoutePermitLogic.FlightNeeds(route, i);
                for (int k = 0; k < needs.Count; k++)
                    sb.Append(InboxLetter.Bullet($"{RoutePermitLogic.EndStatusText(gs, route, needs[k])}"
                        + $"　{RoutePermitLogic.SlotPoolLine(gs, needs[k].airport, needs[k].band)}"));
            }

            int slotGaps = 0;
            if (shortages != null)
            {
                for (int i = 0; i < shortages.Count; i++)
                {
                    var s = shortages[i];
                    if (s == null || s.kind != "no_slot") continue;
                    slotGaps++;
                    // ★2026-10-16：缺口**逐端**列 —— 一张申请单只对应一端的槽。
                    // 旧版只写一端（`s.slotAirport`），而那是 `TryShortEnd/TryShortClock` 返回的**第一个**缺端，
                    // 本场端排在需求列表下标 0 → 实际恒定只列本场端；于是正文说「先批两端时刻槽申请」、
                    // 清单只列一端，两句话自相矛盾，对端那张单子也从来没被提起来过。
                    sb.Append($"　　缺　{s.slotLabel} {s.dep} 起飞（{route.origin} ⇄ {route.destination} 往返，"
                              + $"各停站各占一对其时刻）：\n");
                    var ends = RoutePermitLogic.WaveEndNeeds(route, s.dep);
                    if (ends.Count == 0)
                    {
                        string one = string.IsNullOrEmpty(s.slotAirport) ? route.origin : s.slotAirport;
                        sb.Append($"　　　{one}　已发战略部「申请时刻槽」邮件，批准后下季交割。\n");
                        continue;
                    }
                    for (int k = 0; k < ends.Count; k++)
                    {
                        var need = ends[k];
                        string st = RoutePermitLogic.EndStatusKey(gs, route, need);
                        string what = st == "grandfather" ? "祖父条款保住，无需再办"
                            : st != "missing" ? "已有持仓，无需再办"
                            : RoutePermitLogic.CanApplyEnd(gs, route, need)
                                ? "已发战略部「申请时刻槽」邮件，批准后下季交割"
                                : "该时段池已满，申请与购入都无门（已另发受阻汇报）";
                        sb.Append($"　　　{RoutePermitLogic.LegName(need)}　{need.airport} {RoutePermitLogic.LegClocks(need)}　{what}\n");
                    }
                }
            }
            if (slotGaps > 0)
                sb.Append(InboxLetter.Bullet("两阶段　先批**各停站**的时刻槽申请（下季交割）→ 各停站齐备后再下加密指令，运营部重排班次。本呈报已排班次可单独批准生效。"));
            else
                sb.Append(InboxLetter.Bullet("本线已排班次各停站槽位占用已对齐；退回呈报时按班次逐条退还。"));
            return sb.ToString();
        }

        static string BuildRingSection(GameState gs, RouteData route, Directive d, out string ringIdsCsv)
        {
            ringIdsCsv = "";
            if (!d.buildRing)
                return InboxLetter.Bullet("CEO 未要求编环，本航线按单段执飞排定，日利用率偏低。");

            string reg = route.registration;
            if (string.IsNullOrEmpty(reg))
                return InboxLetter.Bullet("尚无可用机号，未编环。");

            var sb = new StringBuilder();
            sb.Append(BuildDayCycleSection(gs, route));

            var partner = FindRingPartner(gs, route, out bool routeIsLegA);
            if (partner == null)
            {
                int rtTurn = (int)OpsEconomics.EstimateTurnMinutes(route.aircraftType, route.groundStaffAssigned) + 10;
                var rt = OpsEconomics.ValidateRoundTripRing(reg, route, rtTurn);
                SaveRing(gs, rt);
                ringIdsCsv = rt.ringId;
                sb.Append(InboxLetter.Bullet("跨航线连班：未找到同枢纽第二段，已按本航线往返环编入（去程+回程当日返航）。"));
                sb.Append(InboxLetter.Row("方案编号", rt.ringId));
                sb.Append(InboxLetter.Bullet(rt.valid ? "校验结论　往返环合格。" : "校验结论　往返环未通过，需调整过站/地勤。"));
                if (!string.IsNullOrEmpty(rt.validationNote))
                    sb.Append(rt.validationNote).Append('\n');
                return sb.ToString();
            }

            RouteData a = routeIsLegA ? route : partner;
            RouteData b = routeIsLegA ? partner : route;
            int turn = (int)OpsEconomics.EstimateTurnMinutes(a.aircraftType, a.groundStaffAssigned) + 10;
            var ring = OpsEconomics.ValidateRing(reg, a, b, turn);
            SaveRing(gs, ring);
            ringIdsCsv = ring.ringId;
            sb.Append(InboxLetter.Row("跨航线方案", ring.ringId));
            sb.Append(InboxLetter.Row("衔接", $"{a.origin}→{a.destination}（{a.flightNumber}）→ {b.origin}→{b.destination}（{b.flightNumber}）　过站 {ring.turnMinutes} 分"));
            sb.Append(InboxLetter.Bullet(ring.valid ? "校验结论　连班合格。" : "校验结论　未通过，需运营部调整衔接。"));
            if (!string.IsNullOrEmpty(ring.validationNote))
                sb.Append(ring.validationNote).Append('\n');

            partner.EnsureFlights();
            var f0 = partner.flights[0];
            if (string.IsNullOrEmpty(f0.registration))
            {
                f0.registration = reg;
                partner.SyncPrimaryFromFlights();
                sb.Append(InboxLetter.Bullet($"第二段已改由 {reg} 执飞。"));
            }
            return sb.ToString();
        }

        /// <summary>P4：按机号列出当日多段往返（落地+过站链式），不再只写两段环。</summary>
        static string BuildDayCycleSection(GameState gs, RouteData route)
        {
            var sb = new StringBuilder();
            var plan = new OpsRotationLogic.RotationPlan();
            plan.LoadFrom(gs != null ? gs.activeRoutes : null);
            route.EnsureFlights();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = 0;
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null || string.IsNullOrEmpty(f.registration) || !seen.Add(f.registration)) continue;
                n++;
                var wins = new List<OpsRotationLogic.TurnWindow>(plan.Of(f.registration));
                wins.Sort((x, y) => x.start.CompareTo(y.start));
                sb.Append(InboxLetter.Bullet($"日循环　{f.registration}　{wins.Count} 段往返　{plan.DutySummary(f.registration, route.aircraftType)}"));
                for (int k = 0; k < wins.Count; k++)
                {
                    var w = wins[k];
                    string tag = string.Equals(w.routeId, route.routeId, StringComparison.OrdinalIgnoreCase)
                        ? "本线" : OpsScheduleLogic.RouteTag(gs, w.routeId);
                    sb.Append($"　　{k + 1}. {OpsRotationLogic.FormatMinute(w.start)} 起飞 → {OpsRotationLogic.FormatMinute(w.end)} 回场　{tag}　过站 {w.turn} 分\n");
                }
            }
            if (n == 0)
                sb.Append(InboxLetter.Bullet("日循环　尚无机号，无法列出多段线路。"));
            return sb.ToString();
        }

        static RouteData FindRingPartner(GameState gs, RouteData route, out bool routeIsLegA)
        {
            routeIsLegA = true;
            if (gs?.activeRoutes == null) return null;

            // 优先「本线终点 = 对方起点」：出门再回家，方是标准往返环
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null || r == route) continue;
                if (!string.IsNullOrEmpty(route.destination)
                    && string.Equals(r.origin, route.destination, StringComparison.OrdinalIgnoreCase))
                    return r;
            }
            // 退而求其次：对方终点 = 本线起点（本线作为回程段）
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null || r == route) continue;
                if (!string.IsNullOrEmpty(route.origin)
                    && string.Equals(r.destination, route.origin, StringComparison.OrdinalIgnoreCase))
                {
                    routeIsLegA = false;
                    return r;
                }
            }
            return null;
        }

        static void SaveRing(GameState gs, FlightRing ring)
        {
            if (gs == null || ring == null) return;
            if (gs.flightRings == null) gs.flightRings = new List<FlightRing>();
            for (int i = 0; i < gs.flightRings.Count; i++)
            {
                if (gs.flightRings[i] != null && gs.flightRings[i].ringId == ring.ringId)
                {
                    gs.flightRings[i] = ring;
                    return;
                }
            }
            gs.flightRings.Add(ring);
        }

        static string BuildLinkedSection(GameState gs, RouteData route, int beforeStaff)
        {
            var sb = new StringBuilder();
            var demand = RouteGravityModel.EvaluateRoute(gs, route);
            int seats = WeeklySeats(gs, route);
            sb.Append(InboxLetter.Bullet(
                $"市场部　已按新运力同步运力投放与收益管理：周座位 {seats:N0} 座，周需求 {demand.weeklyPax:N0} 人次" +
                (seats > demand.weeklyPax * 1.05f ? "（座位略过剩，建议后续看折扣与班期）" : seats < demand.weeklyPax * 0.9f ? "（仍有需求缺口，可再议加密）" : "（供需基本匹配）")));

            int maint = 0;
            if (gs?.fleet != null)
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var ac = gs.fleet[i];
                    if (ac == null) continue;
                    if (ac.status == AircraftStatuses.Maintenance) maint++;
                }
            }
            sb.Append(InboxLetter.Bullet($"机务工程部　已核对检修窗口与适航状态：{maint} 架在检修中未纳入排班，执行机号适航可用。"));

            float maintCost = 0f, leaseCost = 0f;
            route.EnsureFlights();
            var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null || string.IsNullOrEmpty(f.registration)) continue;
                if (!counted.Add(f.registration)) continue;
                var ac = FindAircraft(gs, f.registration);
                if (ac == null) continue;
                maintCost += FleetEconomics.MonthlyMaintenance(ac.age);
                leaseCost += ac.monthlyLeaseWan;
            }
            sb.Append(InboxLetter.Bullet(
                $"财务部　已按新运力更新预算占用：执行机月维护约 {maintCost:F1} 万" +
                (leaseCost > 0.01f ? $"，月租金约 {leaseCost:F1} 万" : "（自有，无租金）") + "。"));
            sb.Append(InboxLetter.Bullet(
                $"地勤与地面保障　已按机型配齐 {(route.groundStaffAssigned >= beforeStaff ? "并增加" : "并优化")} 至 {route.groundStaffAssigned} 人，过站按新班次重排。"));
            return sb.ToString();
        }

        static string BuildImpactSection(GameState gs, RouteData route, float beforeProfit)
        {
            var sb = new StringBuilder();
            sb.Append(InboxLetter.Row("日利润", $"{beforeProfit:F1} 万 → {route.dailyProfit:F1} 万"));
            sb.Append(InboxLetter.Row("飞机空中时间", AircraftUtilLine(gs, route)));
            var m = OpsEconomics.Evaluate(
                route.distanceKm > 0 ? route.distanceKm : 1000f,
                route.weeklyFlights > 0 ? route.weeklyFlights : 7,
                route.aircraftType, route.groundStaffAssigned,
                gs != null ? gs.onTimePercent : 88f);
            // 日利用率:明说口径 + 与目标比 + 代价（效率乘数），因为本线的班次现在由 CEO 定，
            // 超目标的代价必须写在同一行，否则「能排」与「划算」两件事会分家。
            float target = EconomyBalanceParams.TargetDailyUtilizationHours;
            float delta = m.dailyUtilizationHours - target;
            string deltaText = Math.Abs(delta) < 0.05f ? "正好等于目标"
                             : delta > 0f ? $"超目标 {delta / target * 100f:0}%"
                             : $"低于目标 {Math.Abs(delta) / target * 100f:0}%";
            sb.Append(InboxLetter.Row("日利用率（周均）",
                $"{m.dailyUtilizationHours:F1} 小时/日（一圈 {OpsEconomics.RoundTripSectors} 段口径；"
                + $"目标 {target:0.#}h → {deltaText}）· 效率乘数 {m.scheduleEfficiency:0.##} · 过站 {m.turnMinutes:F0} 分"));
            sb.Append(InboxLetter.Row("机组需求", CrewDemandLine(gs, route)));
            sb.Append(InboxLetter.Row("航班", route.flightNumber ?? "—"));
            return sb.ToString();
        }

        /// <summary>
        /// 本线「每日 N 段」的机组组数与两岗人数（口径见 <see cref="OpsRotationLogic.CrewSetsForDaily"/>）。
        ///
        /// ★2026-10-14 第十次问询：一班飞机能不能一周七趟，机组是**第三根轴** ——
        /// 外站换组的线「每日 1 段」要 3 组（每组 3 名飞行员 = 9 名），两组撑不起来；
        /// 排班器不管这件事（它只看本机当日值勤结构），所以必须在这里如实报给 CEO。
        /// ★第十三次问询：机组 = 驾驶舱 + 客舱；**组数两岗共用，每组人数各算各的**
        /// （驾驶舱看值勤、客舱按座位 1/50），故本行两个人数都报。
        /// 供运力呈报与运营部答复件共用。
        /// </summary>
        public static string CrewDemandLine(GameState gs, RouteData route)
        {
            if (route == null) return "—";
            route.EnsureFlights();
            int trips = Math.Max(1, route.FlightCount);
            var need = OpsRotationLogic.RouteCrewNeed(route, trips);
            int sets = need.sets, pilots = need.pilots, cabin = need.cabin;
            var shape = need.shape;
            int block = OpsRotationLogic.BlockOf(route);
            int perDuty = OpsRotationLogic.PilotsPerDuty(block);
            int perDutyCabin = OpsRotationLogic.CabinPerDuty(route.aircraftType);
            string basis = shape == OpsRotationLogic.RoundTripShape.CrewSwap
                ? $"外站换组：每日 {trips} 段 = {trips * 2} 个值勤期/天，一组每 3 天最多 2 段 → 需 {sets} 组"
                : $"同组往返：每日 {trips} 段，一组一天最多 {OpsRotationLogic.RoundTripsPerCrewSet(route.aircraftType, LiveClockLogic.ParseMinutes(route.depTime), block, OpsRotationLogic.TurnOf(route))} 段 → 需 {sets} 组";
            // ★2026-10-14 第十三次问询：机组 = 驾驶舱 + 客舱。组数两岗共用，每组人数各算各的
            // （驾驶舱看值勤、客舱看座位 1/50），两个人数都报 —— 只报飞行员会让 CEO 以为机组没乘务员。
            string have = $"，每组 驾驶舱 {perDuty} 名（共 {pilots} 名）· 客舱 {perDutyCabin} 名（共 {cabin} 名）";
            // 机组口径（★2026-10-14 第十一次问询）：与排班闸门 CrewCapacityLogic 同一套算术，
            // 报的是「本线可用」（全公司在册/编制 − 其它航线占用），不是公司在册总数 ——
            // 否则 A 线看着够、B 线也看着够，两条线一起加密就撞车。
            string verdict = "";
            if (gs != null)
            {
                int pBudget = CrewCapacityLogic.BudgetFor(gs, route, out int pAvail, out int pOther, out bool pByOnDuty);
                int cBudget = CrewCapacityLogic.CabinBudgetFor(gs, route, out int cAvail, out int cOther, out bool cByOnDuty);
                string pLabel = pByOnDuty ? "在职" : "编制";
                string cLabel = cByOnDuty ? "在职" : "编制";
                string role = CrewCapacityLogic.BindingRoles(gs, route, trips);
                verdict = role.Length == 0
                    ? $"　机组够排（驾驶舱 本线可用 {pBudget} 名 = {pLabel} {pAvail} 名 − 其它航线占用 {pOther} 名；"
                      + $"客舱 本线可用 {cBudget} 名 = {cLabel} {cAvail} 名 − 其它航线占用 {cOther} 名）。"
                    : $"　**机组不够（先卡住的是 {role}）**：驾驶舱 本线可用 {pBudget} 名（{pLabel} {pAvail} 名 − 其它航线占用 {pOther} 名）"
                      + $" vs 需 {pilots} 名；客舱 本线可用 {cBudget} 名（{cLabel} {cAvail} 名 − 其它航线占用 {cOther} 名）"
                      + $" vs 需 {cabin} 名 —— 请先到 人力 → 招聘 补 {role}（未按人建机组时加机抬编制），或降低每日班次。";
            }
            return basis + have + verdict;
        }

        /// <summary>按飞机汇总空中时间（跨航线多段），避免「一机三段」被算成三条航线各一段。</summary>
        static string AircraftUtilLine(GameState gs, RouteData route)
        {
            if (route == null) return "—";
            var plan = new OpsRotationLogic.RotationPlan();
            plan.LoadFrom(gs != null ? gs.activeRoutes : null);
            route.EnsureFlights();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parts = new List<string>();
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null || string.IsNullOrEmpty(f.registration) || !seen.Add(f.registration)) continue;
                var wins = plan.Of(f.registration);
                int air = 0;
                for (int k = 0; k < wins.Count; k++) air += wins[k].block * 2;
                parts.Add($"{f.registration} {air / 60f:0.#}h / {wins.Count} 段");
            }
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }

        /// <summary>呈报邮件正文。</summary>
        public static string FormatReportBody(GameState gs, RouteData route, Report rep)
        {
            if (route == null || rep == null) return "运力调整呈报不可用。";
            var sb = new StringBuilder();
            sb.Append($"运营部呈报：{route.origin}—{route.destination} 运力调整与航班环\n\n");
            sb.Append(InboxLetter.Section("CEO 指令"));
            sb.Append(InboxLetter.Bullet(string.IsNullOrWhiteSpace(rep.directiveRaw) ? "（未记录）" : rep.directiveRaw.Trim()));
            if (!string.IsNullOrEmpty(rep.analysis))
                sb.Append(InboxLetter.Row("系统解析", rep.analysis));

            sb.Append(InboxLetter.Section("调整后运力"));
            sb.Append(rep.capacitySection);

            if (!string.IsNullOrEmpty(rep.sourcingSection))
            {
                sb.Append(InboxLetter.Section("运力来源"));
                sb.Append(rep.sourcingSection);
            }

            if (!string.IsNullOrEmpty(rep.shortageSection))
            {
                sb.Append(InboxLetter.Section("运力缺口"));
                sb.Append(rep.shortageSection);
            }

            if (!string.IsNullOrEmpty(rep.slotSection))
            {
                sb.Append(InboxLetter.Section("时刻槽申请"));
                sb.Append(rep.slotSection);
            }

            if (!string.IsNullOrEmpty(rep.crewSection))
            {
                sb.Append(InboxLetter.Section("飞行机组"));
                sb.Append(rep.crewSection);
            }

            sb.Append(InboxLetter.Section("航班环"));
            sb.Append(rep.ringSection);

            sb.Append(InboxLetter.Section("联动部门"));
            sb.Append(rep.linkedSection);

            sb.Append(InboxLetter.Section("影响"));
            sb.Append(rep.impactSection);

            sb.Append(InboxLetter.Section("请批复"));
            if (rep.crewFirst)
            {
                sb.Append(InboxLetter.Bullet("第一阶段　请先补机组：到 人力 → 招聘 补飞行员（本档若未按人建机组，则加机抬编制）。"
                    + "机组不够，班次排出来也没人飞 —— 这是与时刻槽并列的一道闸门。"));
                sb.Append(InboxLetter.Bullet("第二阶段　机组到位后，在本航线再下加密指令，运营部按新机组产能重排满班次。"));
                sb.Append(InboxLetter.Bullet("批准　仅确认已经排定的班次与日循环生效（不视为缺机组时段已解决）。"));
            }
            else if (rep.slotFirst)
            {
                sb.Append(InboxLetter.Bullet("第一阶段　请先批准战略部「申请时刻槽」邮件（下季扣款交割）。缺槽时段本呈报无法加密。"));
                sb.Append(InboxLetter.Bullet("第二阶段　槽到位后，在本航线再下加密指令，运营部按新持仓重排班次。"));
                sb.Append(InboxLetter.Bullet("批准　仅确认已经排定的班次与日循环生效（不视为缺槽时段已解决）。"));
            }
            else
            {
                sb.Append(InboxLetter.Bullet("批准　运力与航班环按上述执行。"));
            }
            sb.Append(InboxLetter.Bullet("提出修改　在下框写下新的调令（如「再加一班红眼」「减到每天 2 班」），运营部重编再报。"));
            sb.Append(InboxLetter.Bullet("退回　恢复到指令前的运力与航班环，并退还本次占用的时刻槽。"));
            return sb.ToString();
        }

        /// <summary>
        /// 复核报告（指令**未能执行**时发的汇报件）正文。
        ///
        /// 为什么失败也要发信（2026-10-13 四次报障）：单航线页底部那行提示写的就是
        /// 「输入一句指令后按 Enter 下达；**结果以邮件呈报**」—— 失败时若不发信，CEO 按提示走一趟
        /// 收件箱却什么都找不到，只能当系统没反应（报障原话：「发布增加每日一段往返的指令，
        /// 收件箱没有收到反馈邮件」）。与「无机可派 → 发运力核对汇报」（见 §16.2）同一口径：
        /// **办不成也要把原因与出路回一封**，只不过这是汇报件，不占待办、无须批复。
        /// </summary>
        public static string FormatFailureBody(GameState gs, RouteData route, string raw, string note)
        {
            if (route == null) return "运力复核报告不可用。";
            route.EnsureFlights();
            var sb = new StringBuilder();
            sb.Append($"运营部复核：{route.origin}—{route.destination} 的运力指令未获执行，本线维持原状\n\n");
            sb.Append(InboxLetter.Section("您的指令"));
            sb.Append(InboxLetter.Bullet(string.IsNullOrWhiteSpace(raw) ? "（未记录原话）" : raw.Trim()));

            sb.Append(InboxLetter.Section("未执行的原因"));
            sb.Append(InboxLetter.Bullet(string.IsNullOrWhiteSpace(note) ? "运营部未能排出满足该指令的班次。" : note.Trim()));

            sb.Append(InboxLetter.Section("本线现状（未作任何改动）"));
            sb.Append(InboxLetter.Row("班期", route.daysOfWeek));
            sb.Append(InboxLetter.Row("每日班次", $"{route.FlightCount} 段往返"));
            sb.Append(InboxLetter.Row("每周班次", $"{route.WeeklyFlightsDeparture()} 班（离港日口径）"));
            sb.Append(InboxLetter.Row("执飞机号", RegsOf(route)));
            sb.Append(InboxLetter.Row("机型", route.aircraftType));
            sb.Append(InboxLetter.Row("单班日利", $"{route.dailyProfit:F2} 万"));
            // 回复正文里不出现本线现状的**日利用率/值勤**细节：那是「换季报告」与「排班咨询」的口径，
            // 这里只需要让 CEO 认出「我的线没被动过」。

            sb.Append(InboxLetter.Section("可以怎么办"));
            sb.Append(InboxLetter.Bullet("派机/换机型　说「安排一架合适的飞机执飞」（运营部提名 2–3 架候选含投影日利，您选定才落班）。"));
            sb.Append(InboxLetter.Bullet("加机　先到机队引进、或召回干租/湿租/包机在外的飞机，运营部再按新运力重排。"));
            sb.Append(InboxLetter.Bullet("先申请时刻槽　若卡在槽上，运营部会另发「申请时刻槽」件（批准后下季交割）再排班。"));
            sb.Append(InboxLetter.Bullet("维持现状　本线按现有班期继续飞，日利与机号不变。"));
            sb.Append("本函为复核汇报：运营部未改动本线运力与机号，也无需您批复。\n");
            return sb.ToString();
        }

        // ———————————————— 批复回执的裁定（★2026-10-16 第四十二次修订）————————————————
        //
        // 病（CEO 第十八次问询原话）：「当时刻槽还没交割，CEO 再次发出调整运力的指令，运营部继续维持
        // 现有运力，却发出了运力调整已经生效。当运力没有调整时，为什么要发出运力已经调整的邮件？」
        //
        // ★旧回执的裁定词是**常量**：`ApproveCapacityDirective` 无条件写「运力调整已生效」，而呈报
        //   自己那句「批准　仅确认已经排定的班次与日循环生效（不视为缺槽时段已解决）」已经挑明了这
        //   不是「缺的波都补上了」。两句话当场打架 —— CEO 批的是「先批时刻槽」的呈报（1 班 → 1 班、
        //   缺 1 波），回执却说「已生效」，读起来像加密成功了；更要命的是**重复下同一条令**时，
        //   第二次的回执仍写「已生效」（槽还没交割，班次一个都没动），他自然要问「为什么」。
        //
        // ★正解：**批复成立 ≠ 调令落地**。回执按「目标 vs 实际」裁三档，并把缺口与在办的单子照抄出来。

        /// <summary>批复回执的裁定分档：看的是「目标班次到没到」，不是「这封函批没批」。</summary>
        public enum CapVerdict
        {
            /// <summary>旧档：呈报当年没记目标 → 只能说「已确认」，不敢说「已生效」。</summary>
            Unknown,
            /// <summary>目标已达成。</summary>
            Done,
            /// <summary>只排出一部分（缺槽 / 缺机组 / 缺机）。</summary>
            Partial,
            /// <summary>与指令前完全一致 —— 一班没动。</summary>
            Unchanged,
            /// <summary>指令本身就是「维持现状」。</summary>
            Keep
        }

        /// <summary>
        /// 裁定：<paramref name="targetWaves"/> 为 **-1 = 指令要求维持现状**、**0 = 旧档未记录**。
        ///
        /// ★判据顺序是刻意的：**「有没有动」优先于「到没到位」**。班次一个都没动，就不许说「生效」——
        /// CEO 的分辨点正是这一句（十八次问询的原话：「当运力没有调整时，为什么要发出运力已经调整的邮件？」）。
        /// 削减方向单独判：「减到每天 N 班」的达成是 ≤ N，不是 ≥ N。
        /// </summary>
        public static CapVerdict VerdictOf(RouteData route, int targetWaves, int beforeWaves)
        {
            int now = route != null ? route.FlightCount : 0;
            if (targetWaves < 0) return CapVerdict.Keep;               // 指令本身就是维持现状
            if (targetWaves == 0) return CapVerdict.Unknown;           // 旧档：没有目标可比，不敢断言
            if (beforeWaves > 0 && now == beforeWaves) return CapVerdict.Unchanged;   // 一班没动
            bool reduce = beforeWaves > 0 && targetWaves < beforeWaves;
            bool reached = reduce ? now <= targetWaves : now >= targetWaves;
            return reached ? CapVerdict.Done : CapVerdict.Partial;
        }

        /// <summary>banner 之外的裁定词（标题与正文共用同一判据：标题说「已生效」而正文写「未变动」是最坏的一种漂移）。</summary>
        public static string VerdictWord(CapVerdict v)
        {
            switch (v)
            {
                case CapVerdict.Done: return "已生效";
                case CapVerdict.Partial: return "部分生效";
                case CapVerdict.Unchanged: return "未变动";
                case CapVerdict.Keep: return "维持现况";
                default: return "已确认";
            }
        }

        /// <summary>回执标题（收件箱列表上唯一能看见的一行，必须自带裁定词）。</summary>
        public static string ReceiptSubject(string origin, string dest, CapVerdict v, int now, int target)
        {
            string head = $"{origin}—{dest} 运力";
            switch (v)
            {
                case CapVerdict.Done: return head + "调整已生效";
                case CapVerdict.Partial: return $"{head}部分生效（{now}/{target} 段）";
                case CapVerdict.Unchanged: return $"{head}未变动（维持 {now} 段）";
                case CapVerdict.Keep: return head + "维持现况（按指令）";
                default: return head + "调整已确认";
            }
        }

        /// <summary>
        /// 批复回执正文：现状 + 目标 + 裁定 + 缺口 + 在办的时刻槽单 + 航班环。
        ///
        /// <paramref name="gapSection"/> 由调用方照抄呈报的「运力缺口」段（<see cref="Report.shortageSection"/>），
        /// <paramref name="slotSection"/> 由调用方列出本线在途的槽单 —— 两者都是**复核用的真话**，
        /// 裁定为「未变动 / 部分生效」时缺了它们，回执就只剩一句空话。
        /// </summary>
        public static string FormatReceiptBody(GameState gs, RouteData route, string ringIdsCsv,
                                               int targetWaves, int beforeWaves,
                                               string gapSection, string slotSection)
        {
            if (route == null) return "运力回执不可用。";
            route.EnsureFlights();
            var v = VerdictOf(route, targetWaves, beforeWaves);
            int now = route.FlightCount;
            var sb = new StringBuilder();

            sb.Append(InboxLetter.Section("本次裁定"));
            switch (v)
            {
                case CapVerdict.Done:
                    sb.Append(InboxLetter.Bullet($"本次调令**全部落地**：每日 {now} 段往返，目标已达成。"));
                    break;
                case CapVerdict.Partial:
                    sb.Append(InboxLetter.Bullet($"本次调令**只落地一部分**：目标每日 {targetWaves} 段，实际排定 {now} 段，"
                                                 + $"还有 {targetWaves - now} 段排不出来（原因见下）。"));
                    break;
                case CapVerdict.Unchanged:
                    sb.Append(InboxLetter.Bullet($"本次调令**一班未动**：目标每日 {targetWaves} 段，实际仍是 {now} 段 —— "
                        + "与指令前完全一致" + (string.IsNullOrEmpty(gapSection) ? "。" : "（原因见下）。")));
                    break;
                case CapVerdict.Keep:
                    sb.Append(InboxLetter.Bullet($"按您的指令**维持现状**：运营部未改动班次，每日 {now} 段往返。"));
                    break;
                default:
                    sb.Append(InboxLetter.Bullet($"已确认本线运力：每日 {now} 段往返。"));
                    break;
            }
            if ((v == CapVerdict.Partial || v == CapVerdict.Unchanged) && !string.IsNullOrEmpty(gapSection))
                sb.Append(InboxLetter.Bullet("★本次批复**只确认已经排定的班次**生效，不视为缺口时段已解决；"
                                             + "缺口补上后，运营部会另行呈报（不必重下同一条指令 —— 该办的单子已经在办）。"));

            sb.Append(InboxLetter.Section("班次"));
            sb.Append(InboxLetter.Row("每日班次", beforeWaves > 0 ? $"{beforeWaves} 班 → {now} 班" : $"{now} 班"));
            sb.Append(InboxLetter.Row("每周班次（离港日）",
                $"{route.WeeklyFlightsDeparture()} 班（{FlightScheduleFormat.DaysSummary(route.WeekdayOpsDeparture())}）"));
            sb.Append(InboxLetter.Row("每周班次（值勤日）",
                $"{route.WeeklyFlightsDuty()} 班（{FlightScheduleFormat.DaysSummary(route.WeekdayOpsDuty())}）"));
            if (targetWaves > 0) sb.Append(InboxLetter.Row("目标班次", $"每日 {targetWaves} 段往返"));
            else if (v == CapVerdict.Keep) sb.Append(InboxLetter.Row("目标班次", "维持现状（按您的指令）"));
            sb.Append(InboxLetter.Row("执飞机号", $"{RegsOf(route)} · {route.aircraftType}"));

            if (!string.IsNullOrEmpty(gapSection))
            {
                sb.Append(InboxLetter.Section("未排出的时段（本批复不视为已解决）"));
                sb.Append(gapSection);
            }

            if (!string.IsNullOrEmpty(slotSection))
            {
                sb.Append(InboxLetter.Section("已开出的时刻槽单（在办）"));
                sb.Append(slotSection);
            }

            sb.Append(InboxLetter.Section("航班环"));
            sb.Append(InboxLetter.Bullet(string.IsNullOrEmpty(ringIdsCsv)
                ? "航班环未变（本航线单段执飞）。"
                : $"航班环 {ringIdsCsv} 已生效，机号 {route.registration} 按两段连班执行。"));
            return sb.ToString();
        }

        /// <summary>退回呈报：恢复指令前排班并撤销本次建立的航班环。</summary>
        public static bool Revert(GameState gs, RouteData route, string ringIdsCsv, out string message)
        {
            message = "";
            if (route == null) { message = "未找到对应航线。"; return false; }
            int released = RoutePermitLogic.ReleaseByRoute(gs, route.routeId);
            bool restored = route.RestoreScheduleFromBackup();
            route.directivePending = false;
            RoutePermitLogic.BindRouteFlights(gs, route);
            int removed = RemoveRings(gs, ringIdsCsv);
            if (restored)
            {
                // 负基准＝真实亏损，必须原样透传并照实重算（原先 `baseline > 0.01f` 的门槛使亏损航线
                // 在退回时**根本不重算**，于是指令期写进去的盈利基准留在原地，退回也救不回来）
                float baseline = OpsEconomics.ResolvedSingleFlightBaseline(route);
                if (gs != null)
                    OpsEconomics.ApplyToRoute(route, baseline, gs.onTimePercent);
                message = $"已退回运力调整：恢复到指令前排班（{route.DualWeeklyLine()}）";
                if (released > 0) message += $"，并退还本次 {released} 个时刻槽";
                message += removed > 0 ? $"，撤销本次 {removed} 个航班环。" : "。";
            }
            else
            {
                message = "已退回呈报（无可回退的排班快照）。";
            }
            if (gs != null) gs.AddDecisionLog("运营：" + message);
            return true;
        }

        static int RemoveRings(GameState gs, string ringIdsCsv)
        {
            if (gs?.flightRings == null || string.IsNullOrEmpty(ringIdsCsv)) return 0;
            var ids = ringIdsCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            int n = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i].Trim();
                if (id.Length == 0) continue;
                for (int j = gs.flightRings.Count - 1; j >= 0; j--)
                {
                    if (gs.flightRings[j] != null && gs.flightRings[j].ringId == id)
                    {
                        gs.flightRings.RemoveAt(j);
                        n++;
                    }
                }
            }
            return n;
        }

        // ———————————————————————— 运力体检（供单航线页提示） ————————————————————————

        /// <summary>运力体检：周座位 vs 周需求，并给出一句建议（CEO 据此决定是否下指令）。</summary>
        public static string HealthCheck(GameState gs, RouteData route, out string suggest)
        {
            suggest = "";
            if (route == null) return "";
            var demand = RouteGravityModel.EvaluateRoute(gs, route);
            int seats = WeeklySeats(gs, route);
            string verdict;
            if (seats < demand.weeklyPax * 0.9f)
            {
                verdict = "运力偏紧";
                suggest = "建议加密班次（如「早班到红眼都排上」）";
            }
            else if (seats > demand.weeklyPax * 1.15f)
            {
                verdict = "运力偏松";
                suggest = "建议压缩班次（如「减到每天 2 班」）";
            }
            else
            {
                verdict = "供需匹配";
                suggest = "可维持现有运力";
            }
            return $"运力体检　周座位 {seats:N0} 座 / 周需求 {demand.weeklyPax:N0} 人次 · {verdict}";
        }

        static int WeeklySeats(GameState gs, RouteData route)
        {
            if (route == null) return 0;
            route.EnsureFlights();
            int total = 0;
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                var ac = FindAircraft(gs, f.registration);
                int seats = ac != null ? ac.TotalSeats : 180;
                total += seats * Math.Max(1, f.weeklyFlights);
            }
            return total;
        }

        static Aircraft FindAircraft(GameState gs, string reg)
        {
            if (gs?.fleet == null || string.IsNullOrEmpty(reg)) return null;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var ac = gs.fleet[i];
                if (ac != null && string.Equals(ac.registration, reg, StringComparison.OrdinalIgnoreCase))
                    return ac;
            }
            return null;
        }

        public static string RegsOf(RouteData route)
        {
            if (route == null) return "—";
            route.EnsureFlights();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null || string.IsNullOrEmpty(f.registration)) continue;
                if (seen.Add(f.registration)) list.Add(f.registration);
            }
            if (list.Count == 0) return string.IsNullOrEmpty(route.registration) ? "未分配" : route.registration;
            return string.Join("、", list);
        }

        // ———————————————————————— 小工具 ————————————————————————

        static bool ContainsAny(string s, params string[] keys)
        {
            if (string.IsNullOrEmpty(s) || keys == null) return false;
            for (int i = 0; i < keys.Length; i++)
                if (s.IndexOf(keys[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static string[] SplitDeps(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return null;
            var parts = csv.Split(new[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length >= 4 && p.IndexOf(':') > 0) list.Add(p);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        static string ReadJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return "";
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return "";
            int colon = json.IndexOf(':', i);
            if (colon < 0) return "";
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return "";
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return "";
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        static bool TryReadJsonInt(string json, string key, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return false;
            int colon = json.IndexOf(':', i);
            if (colon < 0) return false;
            int p = colon + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\t')) p++;
            int start = p;
            while (p < json.Length && char.IsDigit(json[p])) p++;
            if (p == start) return false;
            return int.TryParse(json.Substring(start, p - start), out value);
        }

        static bool TryReadJsonBool(string json, string key, out bool value)
        {
            value = false;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return false;
            int colon = json.IndexOf(':', i);
            if (colon < 0) return false;
            string rest = json.Substring(colon + 1).TrimStart();
            if (rest.StartsWith("true", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
            if (rest.StartsWith("false", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
            return false;
        }
    }
}
