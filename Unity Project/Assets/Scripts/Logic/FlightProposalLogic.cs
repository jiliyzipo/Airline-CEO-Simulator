using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航班时刻加机提案（2026-10-13 CEO 裁定，2026-10-15 改口径）。
    ///
    /// 背景：n98「航线/航班环选择器」原来点一个时段就直接 <c>route.flights.Add</c>，飞机当场排进去，
    /// CEO（玩家）本人既是指令下发者又是批准者，等于没有批准环节。现改为**两段式**：
    /// 点时刻 → 运营部把提案整理成一封呈报投进收件箱 → CEO 批准后才真的落班。
    ///
    /// 为什么提案存在邮件里、而不是「先落班再等批复」：
    /// 「拒绝」必须等于什么都没发生过。若先写进 route.flights 再等批复，退回时就得撤销班次；
    /// 而撤销/改动在飞班次会连带 <see cref="RouteData.schedulePending"/> 那套冻结语义
    /// （LiveClockSimulator 直接跳过 schedulePending 的航线），为一班连夜波及整条在飞航线。
    /// 因此：**提案只存在于邮件里，批准那一刻才落班**，退回即在飞航线毫发无伤。
    ///
    /// ★2026-10-15 改口径（CEO 问询「0630 的槽能不能再排 0700」）：
    /// 提案的身份从「机号 × 时段（早班/晚班）」改为「机号 × **具体时刻** `HH:mm`」。
    /// 理由：时刻槽在现实里精确到时刻（WASG 1.7.2d/e），06:30 与 07:00 是两条槽、两个班次；
    /// 若仍按时段去重，同一机号在同一需求波里就永远只能报一班，等于把「同段多波」在提案环节砍掉。
    /// 时段名仅作展示（`slotLabel`）与需求系数取用，界面上仍会带上，避免玩家看不懂 07:00 属于哪一波。
    ///
    /// 本类只做提案的编解码、口径换算与落班，不改邮件状态；状态推进在 InboxManager。
    /// </summary>
    public static class FlightProposalLogic
    {
        /// <summary>一条待批的加机提案：某机号排进某航线某**具体时刻**。</summary>
        [Serializable]
        public struct Proposal
        {
            public string reg;        // 机号
            public string slotLabel;  // 所属时段名（早班 / 午班 / 晚班 / 红眼）—— **仅供展示**，不是身份
            public string dep;        // 离港**具体时刻** "HH:mm" —— 这才是提案的身份
            public string arr;        // 到港时刻（按轮挡与机型推算）
        }

        // 编解码沿用本工程既有惯例（见 SlotUsageLogic.EncodeSeries）：条目间 ','，字段间 '|'。
        // 四个字段（机号 / 时段名 / HH:mm / HH:mm）都不含这两个字符，无需转义。
        const char FieldSep = '|';
        const char ItemSep = ',';

        const int WeeklyFlights = 7;
        const string DaysEvery = "每日";

        /// <summary>
        /// 参数归一：调用点可能给「时段名」（旧界面 / 旧存档）或「具体时刻」。
        /// 时段名折成该波的**峰点**（`ScheduleSlots.BandNameToClock`），已是时刻则原样返回（幂等）。
        /// </summary>
        public static string ClockOf(string timeOrLabel) => ScheduleSlots.BandNameToClock(timeOrLabel);

        /// <summary>去重键：同一机号 + 同一**具体时刻**在一封呈报里只该出现一次。</summary>
        public static string KeyOf(string reg, string timeOrLabel)
            => (reg ?? "").Trim() + FieldSep + ClockOf(timeOrLabel);

        public static string KeyOf(Proposal p) => KeyOf(p.reg, p.dep);

        public static string Encode(IList<Proposal> list)
        {
            if (list == null || list.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (string.IsNullOrEmpty(p.reg) || string.IsNullOrEmpty(p.dep)) continue;
                if (sb.Length > 0) sb.Append(ItemSep);
                sb.Append(p.reg).Append(FieldSep).Append(p.slotLabel).Append(FieldSep)
                  .Append(p.dep).Append(FieldSep).Append(p.arr);
            }
            return sb.ToString();
        }

        /// <summary>解码 <see cref="Encode"/>；解析不出的片段跳过（存档脏数据不抛异常）。</summary>
        public static List<Proposal> Decode(string csv)
        {
            var list = new List<Proposal>();
            if (string.IsNullOrEmpty(csv)) return list;
            string[] items = csv.Split(new[] { ItemSep }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < items.Length; i++)
            {
                string[] f = items[i].Split(new[] { FieldSep }, StringSplitOptions.None);
                if (f.Length < 2) continue;
                var p = new Proposal
                {
                    reg = f[0].Trim(),
                    slotLabel = f[1].Trim(),
                    dep = f.Length > 2 ? f[2].Trim() : "",
                    arr = f.Length > 3 ? f[3].Trim() : ""
                };
                if (string.IsNullOrEmpty(p.reg)) continue;
                // 旧档口径：只有时段名没有时刻 → 补成该波峰点；反之只有时刻没时段名 → 由时刻反推。
                if (string.IsNullOrEmpty(p.dep)) p.dep = ClockOf(p.slotLabel);
                if (string.IsNullOrEmpty(p.slotLabel)) p.slotLabel = ScheduleSlots.LabelForDep(p.dep);
                if (string.IsNullOrEmpty(p.dep) && string.IsNullOrEmpty(p.slotLabel)) continue;
                if (string.IsNullOrEmpty(p.arr)) p.arr = ArrAfter(p.dep, 0f);
                list.Add(p);
            }
            return list;
        }

        public static int IndexOf(IList<Proposal> list, string reg, string timeOrLabel)
        {
            if (list == null) return -1;
            string key = KeyOf(reg, timeOrLabel);
            for (int i = 0; i < list.Count; i++)
                if (KeyOf(list[i]) == key) return i;
            return -1;
        }

        /// <summary>
        /// 该机号是否已在航线上执飞该**时刻**（± 网格容差，见 `ScheduleSlots.SlotToleranceMinutes`）。
        /// 用容差而非字符串相等：排班器产出的时刻是 5 分钟粒度，06:32 与 06:30 是同一班，不该被当成两班。
        /// </summary>
        public static bool HasFlight(RouteData route, string reg, string timeOrLabel)
        {
            if (route == null) return false;
            string clock = ClockOf(timeOrLabel);
            route.EnsureFlights();
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                if (!string.Equals(f.registration, reg, StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.ClockDiff(f.depTime, clock) <= ScheduleSlots.SlotToleranceMinutes) return true;
            }
            return false;
        }

        /// <summary>
        /// 按航线的轮挡算这一班的离港 / 到港时刻（呈报怎么算，落班就怎么写，两者共用）。
        /// <paramref name="timeOrLabel"/> 传具体时刻即按该时刻排，传时段名则取该波峰点。
        /// </summary>
        public static Proposal Make(RouteData route, string reg, string timeOrLabel)
        {
            string dep = ClockOf(timeOrLabel);
            var p = new Proposal
            {
                reg = reg,
                dep = dep,
                slotLabel = ScheduleSlots.LabelForDep(dep)
            };
            p.arr = ArrAfter(p.dep, OpsEconomics.BlockHours(DistanceOf(route)));
            return p;
        }

        /// <summary>
        /// 本班单班日利（万美元/日）：单班基准 × 时刻需求乘数 × 调度乘数。
        /// 与旧版界面内联公式同源，抽到这里是为了让「呈报里写的预估」与「落班后写进班次的」必然一致。
        /// 乘数按**时刻**取（`DemandOfTime`）：06:30 与 09:00 同属早班，但吃的是同一波系数，
        /// 不会因为写成 09:00 就掉到别的波的系数上去。
        /// </summary>
        public static float DailyProfitOf(GameState gs, RouteData route, string timeOrLabel)
        {
            if (route == null) return 0f;
            string clock = ClockOf(timeOrLabel);
            float single = OpsEconomics.ResolvedSingleFlightBaseline(route);
            var m = OpsEconomics.Evaluate(DistanceOf(route), WeeklyFlights, route.aircraftType,
                route.groundStaffAssigned, gs != null ? gs.onTimePercent : 88f);
            return OpsEconomics.ToDailyProfit(single * ScheduleSlots.DemandOfTime(clock), WeeklyFlights, m);
        }

        static float DistanceOf(RouteData route)
            => route != null && route.distanceKm > 0f ? route.distanceKm : 1000f;

        public static string ArrAfter(string dep, float hours)
        {
            int total = ParseMinutes(dep) + Mathf.RoundToInt(Mathf.Max(0f, hours) * 60f);
            int m = ((total % 1440) + 1440) % 1440;
            return $"{m / 60:00}:{m % 60:00}";
        }

        static int ParseMinutes(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Length < 5) return 0;
            int h = 0, mm = 0;
            if (int.TryParse(t.Substring(0, 2), out int ph)) h = ph;
            if (int.TryParse(t.Substring(3, 2), out int pm)) mm = pm;
            return h * 60 + mm;
        }

        /// <summary>一行的可读描述（邮件正文与回执共用）：**时刻在前**，时段名括注在后。</summary>
        public static string Line(GameState gs, RouteData route, Proposal p)
        {
            float profit = DailyProfitOf(gs, route, p.dep);
            string band = string.IsNullOrEmpty(p.slotLabel) ? ScheduleSlots.LabelForDep(p.dep) : p.slotLabel;
            return $"{p.reg}　{p.dep} → {p.arr}（{band}）　{DaysEvery} / 周{WeeklyFlights}班　预估日利 {profit:F1} 万";
        }

        /// <summary>呈报正文：待批条目 + 落班后果 + 现有班次对照。</summary>
        public static string FormatBody(GameState gs, RouteData route, IList<Proposal> list)
        {
            if (route == null) return "航线不可用。";
            route.EnsureFlights();
            var sb = new System.Text.StringBuilder();
            sb.Append($"运营部拟为 {route.origin}—{route.destination}（{route.flightNumber}）增加以下班次，" +
                      "<b>批准后落班执行</b>；退回则不作任何改动、本航线照常按现有班次飞行。\n\n");
            for (int i = 0; i < list.Count; i++)
                sb.Append($"{i + 1}) {Line(gs, route, list[i])}\n");
            sb.Append("\n说明：每条班次都钉在**具体时刻**上（如 07:00），不是笼统的「早班」——" +
                      "时刻槽按时刻占用，同一需求波里 06:30 / 07:00 / 07:30 各需各的槽。\n");

            sb.Append("\n现有班次：");
            if (route.flights.Count == 0) sb.Append("暂无");
            else
            {
                for (int i = 0; i < route.flights.Count; i++)
                {
                    var f = route.flights[i];
                    if (f == null) continue;
                    if (i > 0) sb.Append("　|　");
                    sb.Append($"{f.registration} {f.depTime}");
                }
            }
            sb.Append('\n');

            // 时刻槽：批准后落班会按去程/回程两端各占 1 条（协调机场）。先在呈报里说清，免得批完才发现缺槽。
            // 逐条按**该班的具体时刻**展开（ClockNeeds），再按「机场 × 时刻」去重：
            // 同一封呈报里 06:30 与 07:00 两班缺的是两条不同的槽，合并成「早班 1 条」会误导 CEO。
            var slotLines = new List<string>();
            var seen = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var needs = RoutePermitLogic.ClockNeeds(route, list[i].dep);
                for (int k = 0; k < needs.Count; k++)
                {
                    var need = needs[k];
                    string key = need.airport + FieldSep + RoutePermitLogic.TimeOf(need);
                    if (!seen.Add(key)) continue;
                    slotLines.Add("　·　" + RoutePermitLogic.EndStatusText(gs, route, need));
                }
            }
            if (slotLines.Count > 0)
            {
                sb.Append("\n时刻槽（批准后按此占用）：\n");
                for (int i = 0; i < slotLines.Count; i++) sb.Append(slotLines[i]).Append('\n');
            }

            sb.Append("\n建议：批准后本班次立即进入航班的周总计划与详细计划。");
            return sb.ToString();
        }

        /// <summary>
        /// 落班（CEO 批准后调用）：逐条写进班次表，并按两端需求占用时刻槽。
        ///
        /// 批准时**重新校验**两项，因为呈报可能在收件箱里等了好几个季度：
        /// ① 同一「机号 × 时刻」可能已被别的途径占用 → 跳过（`skipped`），绝不静默重复落班；
        /// ② 该时刻的时刻槽可能已易手 → 不落班（`blocked`）。第 ② 条是关键：
        ///    呈报时卡的「缺槽不受理」闸门若不在批准时复验，就得靠「批准时刚好还没丢槽」这种运气，
        ///    一旦丢槽就落下一个占不到槽的班次 —— 等于绕过整套 80/20 考核在协调机场硬飞。
        /// </summary>
        /// <summary>
        /// 落班（CEO 批准后调用）：逐条写进班次表，并按两端需求占用时刻槽。
        ///
        /// 批准时**重新校验三项**，因为呈报可能在收件箱里等了好几个季度：
        /// ① 同一「机号 × 时刻」可能已被别的途径占用 → 跳过（`skipped`），绝不静默重复落班；
        /// ② 该时刻的时刻槽可能已易手 → 不落班（`blocked`）。第 ② 条是关键：
        ///    呈报时卡的「缺槽不受理」闸门若不在批准时复验，就得靠「批准时刚好还没丢槽」这种运气，
        ///    一旦丢槽就落下一个占不到槽的班次 —— 等于绕过整套 80/20 考核在协调机场硬飞。
        /// ③ ★2026-10-15：这架机**此刻还排不排得进**（<see cref="AircraftNominationLogic.CanOperateClock"/>：
        ///    在役状态、航程覆盖、值勤上限、过站/串飞冲突、宵禁、机型匹配）→ 不落班（`ineligible`）。
        ///    第 ③ 条堵的是与第 ② 条同源的口子：呈报时挑得出的机，等两三季后可能已调走、退役或值勤已满，
        ///    此时照落就是一个飞不了的班次（账面有、实际开不出去）。它与缺槽是**两件事**，
        ///    故单列一档而不混进 `blocked` —— 缺槽要开申请单，无机要腾运力，处置方向完全不同。
        /// </summary>
        public static int Apply(GameState gs, RouteData route, IList<Proposal> list,
            out List<Proposal> applied, out List<Proposal> skipped, out List<Proposal> blocked,
            out List<Proposal> ineligible, out int slotsBound)
        {
            applied = new List<Proposal>();
            skipped = new List<Proposal>();
            blocked = new List<Proposal>();
            ineligible = new List<Proposal>();
            slotsBound = 0;
            if (gs == null || route == null || list == null) return 0;
            route.EnsureFlights();

            var newIndexes = new List<int>();
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                string dep = ClockOf(p.dep);
                if (HasFlight(route, p.reg, dep)) { skipped.Add(p); continue; }

                if (!RoutePermitLogic.CanScheduleClock(gs, route, dep, true))
                {
                    blocked.Add(p);
                    continue;
                }

                // 第 ③ 条（机队硬门槛）：判序刻意排在占槽校验**之后**，与 QueueAssignFlight 一致 ——
                // 先判「这一刻有没有槽」再判「这架机飞不飞得了」，两处给出的拦因才不会互相打架。
                if (!AircraftNominationLogic.CanOperateClock(gs, route, p.reg, dep, out string _, out var _))
                {
                    ineligible.Add(p);
                    continue;
                }

                var f = new RouteFlight
                {
                    registration = p.reg,
                    depTime = dep,
                    arrTime = p.arr,
                    daysOfWeek = DaysEvery,
                    weeklyFlights = WeeklyFlights,
                    slotLabel = string.IsNullOrEmpty(p.slotLabel) ? ScheduleSlots.LabelForDep(dep) : p.slotLabel,
                    dailyProfit = DailyProfitOf(gs, route, dep)
                };
                if (string.IsNullOrEmpty(f.arrTime)) f.arrTime = ArrAfter(dep, 0f);
                route.flights.Add(f);
                newIndexes.Add(route.flights.Count - 1);
                applied.Add(p);
            }

            if (applied.Count == 0) return 0;

            route.RecalcDailyProfit();
            route.SyncPrimaryFromFlights();

            // 占槽：只为本轮新落的班次占（不重扫全航线，免得顺带改动别人班次的槽位归属）
            for (int i = 0; i < newIndexes.Count; i++)
            {
                var needs = RoutePermitLogic.FlightNeeds(route, newIndexes[i]);
                for (int k = 0; k < needs.Count; k++)
                {
                    var need = needs[k];
                    if (RoutePermitLogic.ReserveSlot(gs, need.airport, RoutePermitLogic.TimeOf(need),
                        need.routeId, need.flightNo) >= 0)
                        slotsBound++;
                }
            }
            return applied.Count;
        }

        /// <summary>
        /// 旧签名（不看机队硬门槛那一档）：既有调用点/旧自测不关心 `ineligible` 时用这个，
        /// 语义与改前完全一致 —— 新增的第 ③ 档被丢弃，但落班结果不再出现「飞不了的班次」。
        /// </summary>
        public static int Apply(GameState gs, RouteData route, IList<Proposal> list,
            out List<Proposal> applied, out List<Proposal> skipped, out List<Proposal> blocked, out int slotsBound)
            => Apply(gs, route, list, out applied, out skipped, out blocked, out _, out slotsBound);
    }
}
