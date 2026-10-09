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
            public int waves;                   // 目标每日班次，0=自动
            public string[] deps;               // 指定离港时刻，空=按班次自动铺
            public string days;                 // 班期，空=默认
            public bool buildRing = true;       // 是否构建航班环
            public bool maximize;               // 挖满运力（全天时段）
            public bool fromAi;                 // 是否经分析服务解析
            public string analysis = "";        // 系统解析说明（人类可读）
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
            public List<OpsScheduleLogic.WaveShortage> shortages; // 缺口明细（供 UI 摘要）
            public string ringIdsCsv = "";      // 本次建立的航班环（退回时撤销）
            public float profitBefore;
            public float profitAfter;
        }

        // ———————————————————————— 指令解析 ————————————————————————

        // 班次口径：CEO 口中的「班 / 班次 / 段往返 / 往返 / 趟 / 轮」都是每日往返次数
        static readonly Regex RxWaves = new Regex(@"(\d+)\s*(?:班|个?班次|段(?:往返)?|个?往返|趟|轮)", RegexOptions.Compiled);
        /// <summary>「增加到每日 4 段」「加密到每天 3 班」「减到 1 班」——取动词后的目标数字，而不是现状。</summary>
        static readonly Regex RxTargetWaves = new Regex(
            @"(?:增加(?:到|至)?|加密(?:到|至)?|调(?:整)?到|改(?:到|成|为)|升到|提到|加到|减到|减少到|压缩到|砍到)[^0-9]{0,20}(\d+)\s*(?:班|个?班次|段(?:往返)?|个?往返|趟|轮)",
            RegexOptions.Compiled);

        public static string BuildParseSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.Append("你是航空公司运营部调度。用户是 CEO，用一句话下达运力调整指令。只输出 JSON：");
            sb.Append("{\"intent\":\"increase|reduce|keep\",\"waves\":3,\"depsCsv\":\"08:00,12:00,18:00,22:00\",");
            sb.Append("\"days\":\"每日\",\"buildRing\":true,\"rationale\":\"一句中文说明\"}。");
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

            bool keep = ContainsAny(s, "保持", "维持", "不变", "不动", "暂不调整", "先不改");
            bool reduce = !keep && ContainsAny(s, "削减", "减少", "减班", "压缩", "砍", "收缩", "降", "少排", "收回");
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
            string intent = ReadJsonString(json, "intent");
            if (!string.IsNullOrEmpty(intent))
            {
                intent = intent.Trim().ToLowerInvariant();
                if (intent == "increase" || intent == "reduce" || intent == "keep") d.intent = intent;
            }

            if (TryReadJsonInt(json, "waves", out int waves) && waves > 0)
            {
                int ai = Math.Min(waves, MaxWavesPerDay);
                int kw = ReadWaves(d.raw);
                // 增加运力时：关键词已读出的目标不可被模型误填成「现状 1 班」覆盖掉
                if (d.intent == "reduce") d.waves = kw > 0 ? Math.Min(ai, kw) : ai;
                else d.waves = Math.Max(ai, kw);
            }

            string depsCsv = ReadJsonString(json, "depsCsv");
            string[] deps = SplitDeps(depsCsv);
            if (deps != null && deps.Length > 0) d.deps = deps;

            string days = ReadJsonString(json, "days");
            if (!string.IsNullOrEmpty(days)) d.days = days;

            if (TryReadJsonBool(json, "buildRing", out bool buildRing)) d.buildRing = buildRing;

            d.fromAi = true;
            d.analysis = DescribeAnalysis(d);
            string rationale = ReadJsonString(json, "rationale");
            if (!string.IsNullOrEmpty(rationale))
                d.analysis += "　" + rationale.Trim();
        }

        static int ReadWaves(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;

            var t = RxTargetWaves.Match(s);
            if (t.Success && int.TryParse(t.Groups[1].Value, out int target))
                return ClampWaves(target);

            var matches = RxWaves.Matches(s);
            if (matches.Count == 0) return 0;
            // 「由每日 1 段往返…每日 4 段往返」：后一个数字才是目标
            var m = matches[matches.Count - 1];
            if (!int.TryParse(m.Groups[1].Value, out int n)) return 0;
            return ClampWaves(n);
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
            return "";
        }

        static string DescribeAnalysis(Directive d)
        {
            var sb = new StringBuilder();
            sb.Append(d.intent == "reduce" ? "意图　压缩运力" : d.intent == "keep" ? "意图　维持运力" : "意图　增加运力");
            if (d.maximize) sb.Append("（挖满全天时段）");
            sb.Append("；目标每日 ");
            if (d.waves > 0) sb.Append(d.waves).Append(" 段往返");
            else sb.Append(d.deps != null ? d.deps.Length + " 段往返" : "自动定班（在现有基础上加 1 段往返）");
            if (d.deps != null && d.deps.Length > 0) sb.Append("（" + string.Join("、", d.deps) + "）");
            sb.Append("；班期 " + (string.IsNullOrEmpty(d.days) ? "按航线默认" : d.days));
            sb.Append("；航班环 " + (d.buildRing ? "编连班环（优先跨航线同枢纽，其次本线往返）" : "不编环（单段执飞）"));
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
                    rep.note = "未能从指令中解析出目标班次。";
                    return rep;
                }
                requestedWaves = deps.Length;

                // 先留快照：呈报被退回时回退到指令前的排班
                route.BackupScheduleForDirective();
                if (!OpsScheduleLogic.ApplyWaves(route, gs.fleet, gs.activeRoutes, deps, d.days, out shortages, gs))
                {
                    route.RestoreScheduleFromBackup();
                    rep.note = "运力不足：没有可用飞机（在干租/湿租/包机中的飞机须先召回，或先引进运力）。呈报未发出。";
                    if (shortages != null && shortages.Count > 0 && shortages[0] != null)
                        rep.note += $"\n{shortages[0].slotLabel} {shortages[0].dep}　{shortages[0].reason}";
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

            rep.capacitySection = keepAsIs
                ? InboxLetter.Bullet($"CEO 要求维持现有运力，运营部未改动班次：{route.DualWeeklyLine()} · {RegsOf(route)}。")
                : BuildCapacitySection(route, beforeWaves, beforeWeekly, beforeStaff, beforeRegs, oldType, requestedWaves);
            rep.sourcingSection = BuildSourcingSection(gs, route, beforeThis, beforeAny, beforeWaves, beforeProfit, requestedWaves, shortages, keepAsIs);
            rep.ringSection = BuildRingSection(gs, route, d, out string ringIds);
            rep.ringIdsCsv = ringIds;
            rep.slotSection = BuildSlotSection(gs, route, shortages);
            rep.slotFirst = HasSlotShortage(shortages) && requestedWaves > route.FlightCount;
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
            else n = Math.Min(MaxWavesPerDay, Math.Max(1, route.FlightCount + 1));
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
                sb.Append(InboxLetter.Bullet($"班次 {i + 1}　{f.registration}　{slot} {f.depTime} → {f.arrTime}　{f.daysOfWeek} / 周 {f.weeklyFlights} 班"));
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
                if (OpsScheduleLogic.RangeKm(a.type) + 0.01f < route.distanceKm) continue;
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

        /// <summary>按缺口类型给出对症的处置建议（宵禁 / 值勤 / 时刻槽 / 无运力，处置各不相同）。</summary>
        static string RemedyLine(List<OpsScheduleLogic.WaveShortage> shortages)
        {
            bool curfew = false, duty = false, slot = false, dutySector = false;
            for (int i = 0; i < shortages.Count; i++)
            {
                var s = shortages[i];
                if (s == null) continue;
                if (s.kind == "curfew") curfew = true;
                else if (s.kind == "duty") duty = true;
                else if (s.kind == "no_slot") slot = true;
                if (s.reason != null && s.reason.Contains("增援航段上限")) dutySector = true;
            }

            var tips = new List<string>();
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
                string apt = string.IsNullOrEmpty(s.slotAirport) ? route.origin : s.slotAirport;
                if (!string.IsNullOrEmpty(apt) && !ContainsIgnoreCase(shortApts, apt)) shortApts.Add(apt);
            }
            if (shortApts.Count == 0) return "";

            gs = gs != null ? gs : GameState.Instance;
            var pend = InboxManager.PendingSlotSettlements(gs);
            var bands = new List<string>();
            var apts = new List<string>();
            int qty = 0, y = 0, q = 0;
            if (pend != null)
            {
                for (int i = 0; i < pend.Count; i++)
                {
                    var m = pend[i];
                    if (m == null) continue;
                    if (!ContainsIgnoreCase(shortApts, m.origin)) continue;
                    string band = StrategyManager.SlotBandName(StrategyManager.ParseSlotBand(m.dest));
                    if (!bands.Contains(band)) bands.Add(band);
                    if (!string.IsNullOrEmpty(m.origin) && !ContainsIgnoreCase(apts, m.origin)) apts.Add(m.origin);
                    qty += InboxManager.SlotQtyOf(m);
                    y = m.effectYear; q = m.effectQuarter;
                }
            }
            if (qty > 0)
                return $"时刻槽办理　{string.Join("、", apts.ToArray())} 已批准待交割 {qty} 个（{string.Join("、", bands.ToArray())}），"
                       + $"第{y}年 Q{q} 入账后方可排该时段；在此之前该波属「缺时刻槽」，不是缺运力。";
            return "时刻槽办理　受限波是「缺时刻槽」：往返两端（去程离港 + 回程离港）各需 1 个对应时段持仓，"
                   + "请批准收件箱的批量申请（下季交割），或改排两端都已有持仓的时段。";
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

        /// <summary>P4：本航线已排班次的槽位占用（往返两端）+ 缺槽申请说明。</summary>
        static string BuildSlotSection(GameState gs, RouteData route, List<OpsScheduleLogic.WaveShortage> shortages)
        {
            var sb = new StringBuilder();
            if (route == null) return "";
            var needsAll = RoutePermitLogic.SlotNeeds(route);
            if (needsAll.Count == 0)
            {
                sb.Append(InboxLetter.Bullet($"{route.origin} ⇄ {route.destination} 两端均为非协调机场，离港班次无需时刻槽。"));
                return sb.ToString();
            }

            route.EnsureFlights();
            sb.Append(InboxLetter.Bullet("口径　一个往返班次在两端各占 1 个对应时段槽：去程在离港机场，回程在对端机场。"));
            if (RoutePermitLogic.NeedsDepartureSlot(route.origin))
                sb.Append(InboxLetter.Bullet($"去程端 {route.origin}（{AirportCatalog.TierLabelOf(route.origin)}，协调机场）"));
            else
                sb.Append(InboxLetter.Bullet($"去程端 {route.origin} 非协调机场，无需槽"));
            if (RoutePermitLogic.NeedsDepartureSlot(route.destination))
                sb.Append(InboxLetter.Bullet($"回程端 {route.destination}（{AirportCatalog.TierLabelOf(route.destination)}，协调机场）"));
            else
                sb.Append(InboxLetter.Bullet($"回程端 {route.destination} 非协调机场，无需槽"));

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
                    string end = string.IsNullOrEmpty(s.slotAirport)
                        ? route.origin
                        : s.slotAirport + (s.slotInbound ? "（回程离港）" : "（去程离港）");
                    sb.Append($"　　缺　{s.slotLabel} {s.dep}　{end}　已发战略部「申请时刻槽」邮件，批准后下季交割。\n");
                }
            }
            if (slotGaps > 0)
                sb.Append(InboxLetter.Bullet("两阶段　先批两端时刻槽申请（下季交割）→ 两端齐备后再下加密指令，运营部重排班次。本呈报已排班次可单独批准生效。"));
            else
                sb.Append(InboxLetter.Bullet("本线已排班次两端槽位占用已对齐；退回呈报时按班次逐条退还。"));
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
            sb.Append(InboxLetter.Row("飞机日利用率", AircraftUtilLine(gs, route)));
            var m = OpsEconomics.Evaluate(
                route.distanceKm > 0 ? route.distanceKm : 1000f,
                route.weeklyFlights > 0 ? route.weeklyFlights : 7,
                route.aircraftType, route.groundStaffAssigned,
                gs != null ? gs.onTimePercent : 88f);
            sb.Append(InboxLetter.Row("航线轮挡口径", $"{m.dailyUtilizationHours:F1} 小时/日（按本线班次，供对照）· 过站 {m.turnMinutes:F0} 分"));
            sb.Append(InboxLetter.Row("航班", route.flightNumber ?? "—"));
            return sb.ToString();
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

            sb.Append(InboxLetter.Section("航班环"));
            sb.Append(rep.ringSection);

            sb.Append(InboxLetter.Section("联动部门"));
            sb.Append(rep.linkedSection);

            sb.Append(InboxLetter.Section("影响"));
            sb.Append(rep.impactSection);

            sb.Append(InboxLetter.Section("请批复"));
            if (rep.slotFirst)
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
