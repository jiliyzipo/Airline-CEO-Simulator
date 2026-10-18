using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 执飞飞机提名（2026-10-13 CEO 裁定）。
    ///
    /// 背景：单航线页**没有**「分配执飞飞机」按钮，机号只能靠一句话调令产生。而原来的运力指令
    /// 只认「增/减/维持」三种意图 —— CEO 在输入框写「请安排合适的飞机执飞此航班」，解析层读不出方向，
    /// 于是回一句「没读懂这条指令想增班还是减班」就作罢（`CapacityDirectiveLogic.NeedsConfirm`）。
    /// 可这条线的症结根本不是班次，而是**没有执飞机**（`GroundReason.NoAssignment`，整线停飞不计营收）。
    ///
    /// 本类补上「指派/改派执飞机」这条路，并做成**两段式**（与加机呈报同源）：
    /// 运营部按「硬门槛 → 收益排序」提名 2–3 架候选（各带优缺点）→ 一封呈报邮件 → **CEO 在邮件里挑一架**
    /// → 批准那一刻才写进航线。退回即等于没提过，在飞航线毫发无伤。
    ///
    /// 硬门槛（任一条不过即出局，且必须给得出理由，不再默默少报）：
    /// 在役 / 非退役退租 / 非停场检修 / 未投入过渡业务 / **航程够（目录真值，含余量）** /
    /// 现有班次的时段不冲突 / 值勤与休息不超限 / 宵禁合规 / **各停站**（本场端 / 经停端 / 对端）时刻槽齐备。
    /// 排序：**投影日净利**（<see cref="CapacityEconomics.ProjectDailyProfit"/>，含「座位↔市场」双向约束）。
    /// </summary>
    public static class AircraftNominationLogic
    {
        /// <summary>呈报邮件 action（收件箱按此识别「执飞飞机提名」）。</summary>
        public const string Action = "assign_aircraft";

        /// <summary>提名上限：多了 CEO 反而挑不动（2–3 架是「有比较、不啰嗦」的区间）。</summary>
        public const int MaxCandidates = 3;

        const char FieldSep = '|';
        const char ItemSep = ',';

        /// <summary>一架候选机（邮件里逐项可读的那个）。</summary>
        public class Candidate
        {
            public string reg;
            public string type;
            public int seats;
            public float rangeKm;
            public float profitWan;   // 该机执飞本线后的航线日净利（万美元/日）
            public float deltaWan;    // 相对现状的变化
            public string pros = "";  // 优点，换行分隔
            public string cons = "";  // 代价，换行分隔

            public string Headline()
                => $"{reg}　{AircraftCatalog.Title(AircraftCatalog.Find(type) ?? new AircraftCatalogEntry { typeId = type })}";
        }

        // ———————————————————————— 硬门槛 ————————————————————————

        /// <summary>
        /// 该机能否执飞本线**现有班次**（不含排序，只看能不能飞）。
        /// why 为不可执飞的原因；本线无机时按「单班草案」判（无非是查航程/状态/过渡业务）。
        /// </summary>
        public static bool Eligible(GameState gs, RouteData route, Aircraft a, out string why)
            => Eligible(gs, route, a, out why, out _);

        /// <summary>同上，并回传否决原因的分类（汇报件按此归因，见 <see cref="OpsRotationLogic.BlockKind"/>）。</summary>
        public static bool Eligible(GameState gs, RouteData route, Aircraft a, out string why,
            out OpsRotationLogic.BlockKind kind)
        {
            why = "";
            kind = OpsRotationLogic.BlockKind.None;
            if (gs == null || route == null || a == null) { why = "参数缺失"; kind = OpsRotationLogic.BlockKind.Missing; return false; }

            if (!a.isActive) { why = "已停用"; kind = OpsRotationLogic.BlockKind.Status; return false; }
            if (a.status == AircraftStatuses.Retired) { why = $"已{a.ExitLabel}"; kind = OpsRotationLogic.BlockKind.Status; return false; }
            if (a.status == AircraftStatuses.Maintenance) { why = "停场检修中"; kind = OpsRotationLogic.BlockKind.Status; return false; }
            if (TransitionBusinessLogic.IsBusy(gs, a.registration))
            {
                why = "已投入过渡业务或已预约机位（可先召回，再行指派）";
                kind = OpsRotationLogic.BlockKind.Transition;
                return false;
            }
            if (!OpsScheduleLogic.RangeKnown(a.type))
            {
                why = $"机型不在目录（{a.type}），航程无法核实";
                kind = OpsRotationLogic.BlockKind.Range;
                return false;
            }
            if (!OpsScheduleLogic.RangeCovers(a.type, route))
            {
                why = $"航程不足（{OpsScheduleLogic.RangeKm(a.type):F0} km"
                       + $"＋减载余量 {OpsScheduleLogic.RangeToleranceKm:F0} km < 本线 {route.distanceKm:F0} km）";
                kind = OpsRotationLogic.BlockKind.Range;
                return false;
            }
            if (CapacityEconomics.SeatsOf(a) <= 0)
            {
                why = "座位配置缺失（请在机队页补齐舱位）";
                kind = OpsRotationLogic.BlockKind.Status;
                return false;
            }
            if (FlightGroundLogic.RegistrationOf(route, null) == a.registration
                && route.FlightCount > 0 && AllFlightsCarriedBy(route, a.registration))
            {
                why = "本线现由该机执飞";
                kind = OpsRotationLogic.BlockKind.Conflict;
                return false;
            }

            // 现有班次逐班核时段/值勤/宵禁（若本线还没班次，只需航程与状态即可 —— 排班时再核）
            route.EnsureFlights();
            if (route.flights.Count == 0) return true;

            var plan = new OpsRotationLogic.RotationPlan();
            plan.LoadFrom(gs.activeRoutes, route);      // 排除本线自身：本线的占用不算冲突
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                string dep = string.IsNullOrEmpty(f.depTime) ? ScheduleSlots.DepTimes[0] : f.depTime;
                if (!OpsRotationLogic.CanTake(route, plan, a, dep, gs, false, out string blockWhy, out var blockKind))
                {
                    why = $"班次 {i + 1}（{f.slotLabel} {dep}）排不进：{blockWhy}";
                    kind = blockKind;
                    return false;
                }
            }
            return true;
        }

        static bool AllFlightsCarriedBy(RouteData route, string reg)
        {
            if (route == null || route.flights == null || route.flights.Count == 0) return false;
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                if (!string.Equals(f.registration, reg, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        // ———————————————————————— 加机路径的硬门槛（★2026-10-15） ————————————————————————

        /// <summary>
        /// 该机能否在**某一具体时刻**为本线再吃一趟往返 —— 加机路径（n98 点时刻加机 /
        /// <c>FlightProposalLogic.Apply</c> / 槽交割后的自动重排）的硬门槛。
        ///
        /// 与 <see cref="Eligible"/> 的分工是**问的问题不同**，门槛是同一套：
        /// <list type="bullet">
        /// <item><see cref="Eligible"/>＝「该机能否接管本线**现有的全部班次**」（执飞飞机提名 / 改派用）；
        ///   <see cref="CanOperateClock"/>＝「该机能否再吃下**这一刻**的往返」（加机 / 交割后重排用）。</item>
        /// </list>
        ///
        /// 为什么必须有：加机路径过去**只校验时刻槽、不校验飞机** —— 一架航程不够、值勤排不下、
        /// 或与本线机型不符的飞机照样能被排上班次，界面上那行「⚠ 机型可能不匹配」只是一句话，
        /// 批准时也没有 <see cref="Apply"/> 那套复验兜底。现在这里挡下，并在**批准那一刻再挡一次**
        /// （呈报可能在收件箱里躺了好几季，飞机可能已退役/停场/被租出）。
        /// </summary>
        public static bool CanOperateClock(GameState gs, RouteData route, string reg, string clock,
            out string why, out OpsRotationLogic.BlockKind kind)
        {
            why = "";
            kind = OpsRotationLogic.BlockKind.None;
            if (gs == null || route == null) { why = "参数缺失"; kind = OpsRotationLogic.BlockKind.Missing; return false; }
            var a = FlightGroundLogic.FindAircraft(gs, reg);
            if (a == null)
            {
                why = $"机号 {(string.IsNullOrEmpty(reg) ? "（空）" : reg)} 不在机队（退役/转售/退租）";
                kind = OpsRotationLogic.BlockKind.Status;
                return false;
            }
            return CanOperateClock(gs, route, a, clock, out why, out kind);
        }

        /// <summary>同上，直接给机（供已持有 <see cref="Aircraft"/> 的调用方复用，免得按机号再查一遍）。</summary>
        public static bool CanOperateClock(GameState gs, RouteData route, Aircraft a, string clock,
            out string why, out OpsRotationLogic.BlockKind kind)
        {
            why = "";
            kind = OpsRotationLogic.BlockKind.None;
            if (gs == null || route == null || a == null) { why = "参数缺失"; kind = OpsRotationLogic.BlockKind.Missing; return false; }
            if (!a.isActive) { why = "已停用"; kind = OpsRotationLogic.BlockKind.Status; return false; }
            if (a.status == AircraftStatuses.Retired) { why = $"已{a.ExitLabel}"; kind = OpsRotationLogic.BlockKind.Status; return false; }
            if (a.status == AircraftStatuses.Maintenance) { why = "停场检修中"; kind = OpsRotationLogic.BlockKind.Status; return false; }
            if (TransitionBusinessLogic.IsBusy(gs, a.registration))
            {
                why = "已投入过渡业务或已预约机位（可先召回，再行指派）";
                kind = OpsRotationLogic.BlockKind.Transition;
                return false;
            }
            if (!OpsScheduleLogic.RangeKnown(a.type))
            {
                why = $"机型不在目录（{a.type}），航程无法核实";
                kind = OpsRotationLogic.BlockKind.Range;
                return false;
            }
            if (!OpsScheduleLogic.RangeCovers(a.type, route))
            {
                why = $"航程不足（{OpsScheduleLogic.RangeKm(a.type):F0} km"
                       + $"＋减载余量 {OpsScheduleLogic.RangeToleranceKm:F0} km < 本线 {route.distanceKm:F0} km）";
                kind = OpsRotationLogic.BlockKind.Range;
                return false;
            }
            if (CapacityEconomics.SeatsOf(a) <= 0)
            {
                why = "座位配置缺失（请在机队页补齐舱位）";
                kind = OpsRotationLogic.BlockKind.Status;
                return false;
            }
            if (ScheduleSlots.ParseClock(clock) < 0)
            {
                why = $"时刻无效（{clock}）";
                kind = OpsRotationLogic.BlockKind.Missing;
                return false;
            }

            // 剩下的交给排班闸门本体（时段冲突 / 值勤 FDP / 过站 / 宵禁 / 机型），
            // 口径与排班器完全同源 —— 绝不在这里另写一套判据。
            // 机型一律**要求与本线一致**：本线的成本与收益模型是按 `route.aircraftType` 定的，
            // 混入异型机会让单班基准与固定成本口径当场分叉。
            var plan = new OpsRotationLogic.RotationPlan();
            plan.LoadFrom(gs.activeRoutes);          // 含本线自身：本线既有的班次也是该机的占用
            return OpsRotationLogic.CanTake(route, plan, a, RoutePermitLogic.SlotRequestClock(clock),
                                            gs, true, out why, out kind);
        }

        /// <summary>一架落选机的归因（机号 + 机型 + 原因 + **原因分类**）。</summary>
        public struct Rejection
        {
            public string reg;
            public string type;
            public string why;
            public OpsRotationLogic.BlockKind kind;

            public string Line()
                => $"{(string.IsNullOrEmpty(reg) ? "（无机号）" : reg)}　{type}　{why}";
        }

        /// <summary>逐机落选原因（到具体机号，便于 CEO 追溯「为什么不是它」）。</summary>
        public static List<string> Rejections(GameState gs, RouteData route, int max = 6)
        {
            var list = new List<string>();
            var details = RejectionDetails(gs, route, max);
            for (int i = 0; i < details.Count; i++) list.Add(details[i].Line());
            return list;
        }

        /// <summary>同上，带上原因分类 —— 汇报件靠它算「拦因分布」与「真正拦因」。</summary>
        public static List<Rejection> RejectionDetails(GameState gs, RouteData route, int max = 6)
        {
            var list = new List<Rejection>();
            if (gs?.fleet == null || route == null) return list;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null) continue;
                if (Eligible(gs, route, a, out string why, out var kind)) continue;
                list.Add(new Rejection
                {
                    reg = a.registration,
                    type = a.type,
                    why = why,
                    kind = kind
                });
                if (list.Count >= max) break;
            }
            return list;
        }

        /// <summary>拦因的中文短名（用于「拦因分布」与处置建议分派）。</summary>
        public static string KindLabel(OpsRotationLogic.BlockKind k)
        {
            switch (k)
            {
                case OpsRotationLogic.BlockKind.Missing: return "参数缺失";
                case OpsRotationLogic.BlockKind.Status: return "停用/检修/退役";
                case OpsRotationLogic.BlockKind.Transition: return "过渡业务占用";
                case OpsRotationLogic.BlockKind.TypeMismatch: return "机型不符";
                case OpsRotationLogic.BlockKind.Range: return "航程不足";
                case OpsRotationLogic.BlockKind.Conflict: return "时段冲突";
                case OpsRotationLogic.BlockKind.Duty: return "值勤超限";
                case OpsRotationLogic.BlockKind.SectorCap: return "增援航段上限";
                case OpsRotationLogic.BlockKind.Curfew: return "宵禁";
                default: return "其他";
            }
        }

        // ———————————————————————— 提名（排序） ————————————————————————

        /// <summary>
        /// 按「硬门槛 → 投影日净利降序」提名候选（最多 <see cref="MaxCandidates"/> 架）。
        /// 排序依据是**真金白银的收益投影**：航程与值勤只是资格，选哪一架看它执飞后这条线赚多少。
        /// </summary>
        public static List<Candidate> Nominate(GameState gs, RouteData route)
        {
            var result = new List<Candidate>();
            if (gs?.fleet == null || route == null) return result;

            var plan = new OpsRotationLogic.RotationPlan();
            plan.LoadFrom(gs.activeRoutes, route);

            var pool = new List<Candidate>();
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || string.IsNullOrEmpty(a.registration)) continue;
                if (!Eligible(gs, route, a, out _)) continue;
                pool.Add(BuildCandidate(gs, route, a, plan));
            }

            pool.Sort((x, y) =>
            {
                int c = y.profitWan.CompareTo(x.profitWan);          // 收益优先
                if (c != 0) return c;
                c = x.seats.CompareTo(y.seats);                       // 同收益取座级小者（省固定成本）
                if (c != 0) return c;
                return string.CompareOrdinal(x.reg, y.reg);           // 保证可复现
            });
            for (int i = 0; i < pool.Count && i < MaxCandidates; i++) result.Add(pool[i]);
            return result;
        }

        static Candidate BuildCandidate(GameState gs, RouteData route, Aircraft a, OpsRotationLogic.RotationPlan plan)
        {
            var c = new Candidate
            {
                reg = a.registration,
                type = a.type,
                seats = CapacityEconomics.SeatsOf(a),
                rangeKm = OpsScheduleLogic.RangeKm(a.type),
                profitWan = CapacityEconomics.ProjectDailyProfit(gs, route, a)
            };
            c.deltaWan = (float)Math.Round(c.profitWan - route.dailyProfit, 2);

            var pros = new List<string>();
            var cons = new List<string>();

            float need = OpsScheduleLogic.RequiredRangeKm(route);
            float margin = c.rangeKm + OpsScheduleLogic.RangeToleranceKm - need;
            pros.Add($"航程 {c.rangeKm:F0} km（含减载余量 {OpsScheduleLogic.RangeToleranceKm:F0} km）"
                     + $"，覆盖本线 {route.distanceKm:F0} km（余量 {margin:F0} km）");
            if (margin < 600f) cons.Add($"航程余量仅 {margin:F0} km，顶风/改航时较紧");

            string fit = CapacityEconomics.FitLine(gs, route, a);
            float market = CapacityEconomics.MarketPaxPerFlight(gs, route);
            if (market > 0f && a.TotalSeats * CapacityEconomics.MaxLoadFactor + 0.5f < market)
                cons.Add($"{fit}　→ 市场装不下，会溢出客流（加密可缓解）");
            else
                pros.Add(fit);

            int same = plan.SameOriginCount(a.registration, route.origin);
            int turns = plan.TurnCount(a.registration);
            if (turns == 0) pros.Add($"当日无任务，可整日投入本线（基地 {route.origin}）");
            else if (same > 0) pros.Add($"当日已在 {route.origin} 执飞 {same} 段，可串飞衔接（合计 {turns} 段）");
            else cons.Add($"当日已在其它基地执飞 {turns} 段，异基地串飞会降低日利用率");

            var current = FlightGroundLogic.FindAircraft(gs, route.registration);
            if (current != null)
            {
                if (string.Equals(current.type, a.type, StringComparison.OrdinalIgnoreCase))
                    pros.Add($"与现机同型（{a.type}），转机型成本与机组资质风险最低");
                else
                    cons.Add($"与现机异型（现为 {current.type}），需换机型与机组排班");
            }
            if (a.monthlyLeaseWan > 0.01f) cons.Add($"经营租赁，月租 {a.monthlyLeaseWan:F0} 万");
            else pros.Add("自有飞机，无月租");
            if (a.age >= 18) cons.Add($"机龄 {a.age} 年，状况 {a.condition:F0}，故障与油耗风险偏高");
            else if (a.age <= 8) pros.Add($"机龄 {a.age} 年，状况 {a.condition:F0}");

            c.pros = string.Join("\n", pros.ToArray());
            c.cons = cons.Count > 0 ? string.Join("\n", cons.ToArray()) : "";
            return c;
        }

        // ———————————————————————— 编解码（存在邮件里） ————————————————————————

        public static string Encode(IList<Candidate> list)
        {
            if (list == null || list.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || string.IsNullOrEmpty(c.reg)) continue;
                if (sb.Length > 0) sb.Append(ItemSep);
                sb.Append(c.reg).Append(FieldSep).Append(c.type).Append(FieldSep).Append(c.seats)
                  .Append(FieldSep).Append(c.profitWan.ToString("F2")).Append(FieldSep)
                  .Append(c.deltaWan.ToString("F2"));
            }
            return sb.ToString();
        }

        public static List<Candidate> Decode(string csv)
        {
            var list = new List<Candidate>();
            if (string.IsNullOrEmpty(csv)) return list;
            string[] items = csv.Split(new[] { ItemSep }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < items.Length; i++)
            {
                string[] f = items[i].Split(new[] { FieldSep }, StringSplitOptions.None);
                if (f.Length < 2) continue;
                var c = new Candidate { reg = f[0].Trim(), type = f[1].Trim() };
                if (string.IsNullOrEmpty(c.reg)) continue;
                if (f.Length > 2) int.TryParse(f[2], out c.seats);
                if (f.Length > 3) float.TryParse(f[3], out c.profitWan);
                if (f.Length > 4) float.TryParse(f[4], out c.deltaWan);
                list.Add(c);
            }
            return list;
        }

        public static int IndexOf(IList<Candidate> list, string reg)
        {
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && string.Equals(list[i].reg, reg, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        // ———————————————————————— 文案 ————————————————————————

        public static string Subject(RouteData route, bool blocked)
            => blocked
                ? $"关于 {route.origin}—{route.destination} 执飞飞机：机队无合适运力（汇报）"
                : $"关于 {route.origin}—{route.destination} 执飞飞机提名（请选定一架）";

        /// <summary>呈报正文：为什么提这条 → 候选（含优缺点）→ 同是合格的落选机与原因 → 批准后的动作。</summary>
        public static string FormatBody(GameState gs, RouteData route, IList<Candidate> cands, List<string> rejected)
        {
            if (route == null) return "航线不可用。";
            var sb = new StringBuilder();
            string current = route.HasAircraft ? route.registration : RouteData.UnassignedAircraftLabel;
            sb.Append($"运营部就 {route.origin}—{route.destination}（{route.flightNumber}，{route.distanceKm:F0} km）"
                      + $"的执飞飞机给出提名。现状：{current}　{route.DualWeeklyLine()}　日利 {route.dailyProfit:F2} 万。\n");
            if (!route.HasAircraft)
                sb.Append("本线当前**未指派执飞机**，已按规则停飞、本季不计营收；指派后即可复航。\n");
            sb.Append('\n');

            for (int i = 0; i < cands.Count; i++)
            {
                var c = cands[i];
                if (c == null) continue;
                string mark = i == 0 ? "（收益最优）" : "";
                sb.Append($"候选 {i + 1}　{c.reg}　{c.type}　{c.seats} 座{mark}\n");
                sb.Append($"　投影日利　{c.profitWan:F2} 万");
                sb.Append(c.deltaWan >= 0 ? $"（较现状 +{c.deltaWan:F2} 万）" : $"（较现状 {c.deltaWan:F2} 万）");
                sb.Append('\n');
                if (!string.IsNullOrEmpty(c.pros)) sb.Append("　有利　" + c.pros.Replace("\n", "\n　　　　") + "\n");
                if (!string.IsNullOrEmpty(c.cons)) sb.Append("　代价　" + c.cons.Replace("\n", "\n　　　　") + "\n");
            }

            if (rejected != null && rejected.Count > 0)
            {
                sb.Append("\n其余在册飞机不可用（逐机归因）：\n");
                for (int i = 0; i < rejected.Count; i++) sb.Append("　·　").Append(rejected[i]).Append('\n');
            }

            sb.Append("\n筛选口径（逐条硬门槛，任一条不过即出局）：\n");
            sb.Append("　① 在役可飞　非停用、非退役退租、非停场检修、未投入过渡业务\n");
            sb.Append("　② 航程足　按飞机目录真值（另给 ")
              .Append(OpsScheduleLogic.RangeToleranceKm.ToString("F0"))
              .Append(" km 减载余量）覆盖本线 ").Append(route.distanceKm.ToString("F0")).Append(" km\n");
            sb.Append("　③ 座位齐备　舱位配置完整，且按市场单班需求校核（座位过剩会拉低单价，见投影日利）\n");
            sb.Append("　④ 时段不冲突　与本人当日既有班次的值勤期、过站窗口互不重叠\n");
            sb.Append("　⑤ 值勤合规　FDP 上限随航段数递减 + 两值勤期间最低休息（超限即出局）\n");
            sb.Append("　⑥ 宵禁合规　两端机场夜间禁起降时段不得起降\n");
            sb.Append("　⑦ 时刻槽齐备　协调机场**各停站**（本场端 / 经停端 / 对端）各有对应时刻持仓方可排班\n");
            sb.Append("排序口径　投影日净利（市场单班需求 × 客座 × 票价 − 变动 − 固定）降序；同收益取座级小者（省固定成本）。\n");

            sb.Append("\n批准后的动作：写入选定机号 → 按各停站需求占用时刻槽 → 重新按该机型核定本线收益 → 复航执行。\n");
            sb.Append("退回则不指派，本线维持现状（无机则继续停飞）。");
            return sb.ToString();
        }

        /// <summary>同上，接受带分类的落选清单（收件箱用这个签名，免得再转一趟字符串）。</summary>
        public static string FormatBody(GameState gs, RouteData route, IList<Candidate> cands, List<Rejection> rejected)
        {
            var lines = new List<string>();
            if (rejected != null)
                for (int i = 0; i < rejected.Count; i++) lines.Add(rejected[i].Line());
            return FormatBody(gs, route, cands, lines);
        }

        /// <summary>无机可派时的汇报件正文：缺什么、差多少、**真正卡在哪一条**、下一步能往哪走。
        ///
        /// ★2026-10-13 重写（CEO 报障触发）：旧版把两件事说错了 ——
        /// ① 座级写成「座级 ≥ 26088 座」。那个数是算出来的（周客流 81,732 ÷ 4 班/周 ÷ 0.88），
        ///    但**没有 2.6 万座的飞机**：这句真正要表达的是「本线需求远大于现有班次」，
        ///    故改为需求口径 + 「按最大座级也需 ≈ N 班/周」。
        /// ② 更糟的是把原因一律归成航程：FRA–ATL 那封写着「机队里没有能飞本线的飞机」并建议
        ///    「采购/租赁满足航程的机型」，可机队最长航程本就富余 4,391 km —— 真正的拦因是
        ///    **值勤超限**。照那封函办，等于花钱买飞机却依然飞不了。现按 <see cref="RejectionDetails"/>
        ///    的分类统计**拦因分布**，再据此给处置建议。
        /// </summary>
        public static string FormatBlockedBody(GameState gs, RouteData route, List<Rejection> rejected)
        {
            var sb = new StringBuilder();
            float need = OpsScheduleLogic.RequiredRangeKm(route);
            float best = 0f;
            string bestReg = "";
            if (gs?.fleet != null)
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var a = gs.fleet[i];
                    if (a == null) continue;
                    float r = OpsScheduleLogic.RangeKm(a.type);
                    if (r > best) { best = r; bestReg = a.registration + " " + a.type; }
                }
            }

            // —— 本线的真正拦因：只看「航程这一关过得去」的飞机 ——
            // 归因口径落在 BlockerSummary 一处，免得跟答复件漂成两套说法。
            string blockers = BlockerSummary(gs, route, rejected, out _, out _);

            sb.Append($"运营部按「安排合适飞机执飞 {route.origin}—{route.destination}」逐机核对，**机队里没有能飞本线的飞机**，故无法提名。\n\n");
            sb.Append($"本线要求　航程 ≥ {need:F0} km（机型侧另给 ")
              .Append(OpsScheduleLogic.RangeToleranceKm.ToString("F0")).Append(" km 减载余量）\n");
            sb.Append(DemandLine(gs, route));
            sb.Append('\n');
            if (best > 0f)
            {
                float slack = best + OpsScheduleLogic.RangeToleranceKm - need;
                sb.Append($"机队现状　最长航程 {best:F0} km（{bestReg}），加余量后 {best + OpsScheduleLogic.RangeToleranceKm:F0} km，");
                sb.Append(slack >= 0f
                    ? $"**航程富余 {slack:F0} km —— 航程不是本线的拦因**。\n"
                    : $"距本线要求尚差 {(-slack):F0} km。\n");
            }
            else sb.Append("机队现状　在册机型的航程数据缺失，无法核比。\n");

            if (rejected != null && rejected.Count > 0)
            {
                sb.Append("\n逐机归因：\n");
                for (int i = 0; i < rejected.Count; i++) sb.Append("　·　").Append(rejected[i].Line()).Append('\n');
                sb.Append('\n').Append(blockers);      // 拦因分布 + 结论 + 处置（与答复件同一份口径）
            }
            else
            {
                sb.Append("\n可选处置（按本线真实拦因给出）：").Append(Advice(OpsRotationLogic.BlockKind.Range)).Append('\n');
            }
            sb.Append("本函为汇报，无需批复。");
            return sb.ToString();
        }

        /// <summary>兼容旧签名（无分类时按文字猜拦因）——供既有调用点与用例平滑过渡。</summary>
        public static string FormatBlockedBody(GameState gs, RouteData route, List<string> rejected)
        {
            var list = new List<Rejection>();
            if (rejected != null)
                for (int i = 0; i < rejected.Count; i++)
                    list.Add(new Rejection { why = rejected[i], kind = GuessKind(rejected[i]) });
            return FormatBlockedBody(gs, route, list);
        }

        static OpsRotationLogic.BlockKind GuessKind(string why)
        {
            if (string.IsNullOrEmpty(why)) return OpsRotationLogic.BlockKind.None;
            if (why.Contains("航程不足") || why.Contains("不在目录")) return OpsRotationLogic.BlockKind.Range;
            if (why.Contains("值勤超限") || why.Contains("增援航段上限")) return OpsRotationLogic.BlockKind.Duty;
            if (why.Contains("时段冲突")) return OpsRotationLogic.BlockKind.Conflict;
            if (why.Contains("宵禁")) return OpsRotationLogic.BlockKind.Curfew;
            if (why.Contains("过渡业务")) return OpsRotationLogic.BlockKind.Transition;
            if (why.Contains("检修") || why.Contains("停用") || why.Contains("退役") || why.Contains("退租")) return OpsRotationLogic.BlockKind.Status;
            if (why.Contains("机型不符")) return OpsRotationLogic.BlockKind.TypeMismatch;
            return OpsRotationLogic.BlockKind.None;
        }

        static string KindTally(Dictionary<OpsRotationLogic.BlockKind, int> counts)
        {
            if (counts == null || counts.Count == 0) return "（无）";
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"{KindLabel(kv.Key)} {kv.Value} 架");
            return string.Join("　｜　", parts.ToArray());
        }

        /// <summary>
        /// 本线**真正拦因**的分布与结论（一处实现，两个出口共用）。
        ///
        /// 出口一：无机可派时的汇报件（<see cref="FormatBlockedBody"/>）；
        /// 出口二：CEO 问「为什么这条线不飞」时的答复件（<see cref="OpsQueryLogic"/>）。
        /// 之所以抽出来共用：旧版这两个出口各写一遍归因，一处把航程当唯一拦因、
        /// 另一处只看值勤 —— 同一件事两套口径，CEO 到底信哪封函？
        /// </summary>
        public static string BlockerSummary(GameState gs, RouteData route, List<Rejection> rejected,
            out OpsRotationLogic.BlockKind primary, out string tally)
        {
            // —— 只看「航程这一关过得去」的飞机 ——
            // 航程不够的飞机本来就没资格谈别的，把它们算进分布会把结论带偏（正是旧版的病）。
            var counts = new Dictionary<OpsRotationLogic.BlockKind, int>();
            primary = OpsRotationLogic.BlockKind.Range;
            int capable = 0;
            if (rejected != null)
            {
                for (int i = 0; i < rejected.Count; i++)
                {
                    var r = rejected[i];
                    if (!counts.ContainsKey(r.kind)) counts[r.kind] = 0;
                    counts[r.kind]++;
                    if (r.kind == OpsRotationLogic.BlockKind.Range) continue;
                    capable++;
                }
                if (capable > 0)
                {
                    int top = -1;
                    foreach (var kv in counts)
                    {
                        if (kv.Key == OpsRotationLogic.BlockKind.Range) continue;
                        if (kv.Value > top) { top = kv.Value; primary = kv.Key; }
                    }
                }
            }

            tally = KindTally(counts);
            var sb = new StringBuilder();
            sb.Append($"拦因分布　{tally}\n");
            sb.Append(Verdict(primary, route)).Append('\n');
            sb.Append($"可选处置　{Advice(primary)}\n");
            return sb.ToString();
        }

        /// <summary>同上，不取拦因分类（答复件用）。</summary>
        public static string BlockerSummary(GameState gs, RouteData route, List<Rejection> rejected)
            => BlockerSummary(gs, route, rejected, out _, out _);

    
        /// <summary>本线需求口径：市场单班人次 → 座级是否装得下 → 装不下要的是班次。</summary>
        static string DemandLine(GameState gs, RouteData route)
        {
            float perFlight = CapacityEconomics.MarketPaxPerFlight(gs, route);
            if (perFlight <= 0f) return "本线需求　需求数据不足，未作运力匹配判定。";
            int wf = CapacityEconomics.WeeklyFlightsOf(route);
            float weekly = RouteGravityModel.EvaluateRoute(gs, route).weeklyPax;
            return $"本线需求　市场 ≈ {perFlight:N0} 人次/班（周客流 {weekly:N0} ÷ {wf} 班/周，单向）"
                   + SeatDemandNote(perFlight, weekly);
        }

        /// <summary>
        /// 座位需求那句话（纯函数，便于离线钉住）。旧版直接写「座级 ≥ {RecommendedSeats} 座」，
        /// 于是 FRA–ATL 那封函印出了「座级 ≥ 26088 座」——**世上没有这种飞机**，这句话等于让 CEO
        /// 去引进不可能的东西。真意是「本线需求远大于现有班次」：按最大座级排也还差班次。
        /// </summary>
        public static string SeatDemandNote(float marketPerFlight, float weeklyPax)
        {
            if (marketPerFlight <= 0f) return "";
            int seats = Math.Max(1, (int)Math.Ceiling(marketPerFlight / CapacityEconomics.MaxLoadFactor));
            int maxSeats = CapacityEconomics.MaxSeatsInCatalog();
            if (maxSeats > 0 && seats > maxSeats)
            {
                int needFlights = (int)Math.Ceiling(weeklyPax / Math.Max(1f, maxSeats * CapacityEconomics.MaxLoadFactor));
                return $"\n　　　　　·　单班座级需 ≈ {seats:N0} 座，已超出在册最大座级（{maxSeats} 座）→ "
                     + $"**不是缺大飞机，是缺班次**：即按最大座级排，也需 ≈ {needFlights} 班/周。";
            }
            return $"　·　单班座级 ≥ {seats} 座（按上座软顶 {CapacityEconomics.MaxLoadFactor * 100f:F0}% 反推）";
        }

        /// <summary>把「真正拦因」翻成一句 CEO 能据此下决定的话。</summary>
        static string Verdict(OpsRotationLogic.BlockKind k, RouteData route)
        {
            switch (k)
            {
                case OpsRotationLogic.BlockKind.Duty:
                    return $"★ 本线真正拦因＝**值勤超限**：航程够的飞机也飞不完这个往返 —— 单段 "
                         + $"{OpsRotationLogic.BlockOf(route) / 60f:0.#}h，去+回连过站 "
                         + $"{(OpsRotationLogic.BlockOf(route) * 2 + OpsRotationLogic.TurnOf(route)) / 60f:0.#}h，"
                         + "超过机组值勤上限。**采购更远航程或更大座级的机型都解决不了**，"
                         + "要改的是「谁在什么时候飞」（错开当日已排班次 / 让当日空闲的飞机来飞）。";
                case OpsRotationLogic.BlockKind.Conflict:
                    return "★ 本线真正拦因＝**时段冲突**：能飞的飞机当日已被别的航线占用 —— 到那些线下达减班释放，或改本线时段。";
                case OpsRotationLogic.BlockKind.Curfew:
                    return "★ 本线真正拦因＝**宵禁**：两端机场的夜间禁起降窗口挡住了班次 —— 改时段即可，与机型无关。";
                case OpsRotationLogic.BlockKind.Transition:
                    return "★ 本线真正拦因＝**机位被过渡业务/预约占着**：先召回或取消预约，再行指派。";
                case OpsRotationLogic.BlockKind.SectorCap:
                    return "★ 本线真正拦因＝**增援航段上限**：宽体增援最多 3 航段，本线现有班次叠加后超了。";
                case OpsRotationLogic.BlockKind.Status:
                    return "★ 本线真正拦因＝**飞机状态**（停用/检修/退役）：修好或换一架。";
                case OpsRotationLogic.BlockKind.TypeMismatch:
                    return "★ 本线真正拦因＝**机型不符**：本线要求同型机。";
                default:
                    return $"★ 本线真正拦因＝**航程**：连机队最长航程的机型都够不着 {OpsScheduleLogic.RequiredRangeKm(route):F0} km。";
            }
        }

        /// <summary>处置建议按拦因分派 —— 不再给一份「买更远航程的飞机」的通用清单。</summary>
        public static string Advice(OpsRotationLogic.BlockKind k)
        {
            switch (k)
            {
                case OpsRotationLogic.BlockKind.Duty:
                    return "① 让**当日尚无任务**的飞机来飞本线（本机当日已排班时，往返会并入同一值勤期而超限）；"
                         + "② 减少本线班次或错开时段，别把长往返压进同一个值勤期；"
                         + "③ 远程线的当天往返装不进一个值勤期时，模型按**外站换组**处理（回程由对方站驻站机组执飞、"
                         + "去程与回程各算一个值勤期）—— 但这要求该机当日无其他任务，故仍可优先选当日空闲的飞机；"
                         + "④ 暂缓本线，维持停飞。";
                case OpsRotationLogic.BlockKind.Conflict:
                    return "① 在占用本线的航线上减班释放运力；② 改本线时段避开冲突；③ 暂缓本线，维持停飞。";
                case OpsRotationLogic.BlockKind.Curfew:
                    return "① 改本线时段，避开两端宵禁窗口；② 暂缓本线，维持停飞。";
                case OpsRotationLogic.BlockKind.Transition:
                    return "① 召回投入过渡业务/已预约机位的飞机，再行指派；② 暂缓本线，维持停飞。";
                case OpsRotationLogic.BlockKind.Status:
                    return "① 等检修完成 / 启用状态正常的飞机；② 暂缓本线，维持停飞。";
                case OpsRotationLogic.BlockKind.SectorCap:
                    return "① 减少本线单机往返段数（宽体增援硬性 ≤ 3 航段）；② 暂缓本线，维持停飞。";
                default:
                    return "① 采购/租赁满足航程的机型（机队页）；② 先湿租或二手引进同级别运力；"
                         + "③ 若本线由其它航线占用机型所致，可先在该线下达减班指令释放宽体；④ 暂缓本线，维持停飞。";
            }
        }

        // ———————————————————————— 落班（CEO 批准后调用） ————————————————————————

        /// <summary>
        /// 批准落班：把选定机号写进本线全部班次，重新核定收益并占用两端时刻槽。
        ///
        /// 批准时**重新校验**硬门槛：呈报可能在收件箱里等了好几个季度，飞机可能已退役/停场/被租出，
        /// 或航程口径已被改动。不合格就如实拒落（并说明原因），绝不落一个飞不起来的机号。
        /// </summary>
        public static bool Apply(GameState gs, RouteData route, string reg, out string message)
        {
            message = "";
            if (gs == null || route == null) { message = "航线不可用。"; return false; }
            if (string.IsNullOrEmpty(reg)) { message = "未选定机号。"; return false; }

            var ac = FlightGroundLogic.FindAircraft(gs, reg);
            if (ac == null) { message = $"机号 {reg} 已不在机队（退役/转售/退租）。"; return false; }
            if (!Eligible(gs, route, ac, out string why))
            {
                message = $"机号 {reg} 现已不可执飞本线：{why}。";
                return false;
            }

            route.EnsureFlights();
            if (route.flights.Count == 0)
            {
                // 本线还没有班次：按单班草案落一班，再由后续运力指令加密
                if (!OpsScheduleLogic.ApplyDraft(route, gs.fleet, gs.activeRoutes, route.opsPlanRevision))
                {
                    message = "本线尚无班次，且运营部无法编出单班草案。";
                    return false;
                }
                route.flights[0].registration = reg;
            }
            else
            {
                for (int i = 0; i < route.flights.Count; i++)
                {
                    var f = route.flights[i];
                    if (f == null) continue;
                    f.registration = reg;
                }
            }

            string oldType = route.aircraftType;
            route.aircraftType = ac.type;
            route.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff(ac.type);
            route.schedulePending = false;
            route.directivePending = false;

            RecomputeProfits(gs, route, ac);
            route.SyncPrimaryFromFlights();
            int bound = RoutePermitLogic.BindRouteFlights(gs, route);

            // 槽没绑上必须当场说清 —— 否则会留下「有执飞机、却缺时刻槽」的半截状态：
            // 航线看似复航，n77 那边报缺槽，玩家要到下一个环节才发现（§15.4 记的就是这类旧账）。
            int slotEnds = RoutePermitLogic.SlotNeeds(route).Count;
            string slotNote = "";
            if (slotEnds > bound)
                slotNote = $"；其中 {slotEnds - bound} 个协调端未能绑定时刻槽（本场该时段无空闲持仓），"
                         + "请到本航线页对应时段呈报申请（§15.8），交割后再点该时段落班";

            message = $"已批准：{reg}（{ac.type}，{CapacityEconomics.SeatsOf(ac)} 座）执飞 "
                      + $"{route.origin}—{route.destination} 共 {route.flights.Count} 班"
                      + (string.Equals(oldType, ac.type, StringComparison.OrdinalIgnoreCase) ? "" : $"，机型由 {oldType} 改为 {ac.type}")
                      + $"；本线日利 {route.dailyProfit:F2} 万，已占时刻槽 {bound} 条{slotNote}。";
            return true;
        }

        /// <summary>
        /// 改派后的收益重算。
        ///
        /// 机型变了，成本与收益口径都变（宽体固定成本系数、座级↔市场匹配），所以本线的**单班毛贡献基准**
        /// 必须一并更新 —— 否则下一次运力指令会拿旧基准把这里刚算出来的收益覆盖掉（两处口径互相打架）。
        /// 量级取自 <see cref="CapacityEconomics.ProjectDailyProfit"/>（已有基准的航线走增量口径，
        /// 保住接管局给出的盈亏量级与符号，不会被「指派一架飞机」凭空翻成盈利）。
        /// </summary>
        public static void RecomputeProfits(GameState gs, RouteData route, Aircraft ac)
        {
            if (gs == null || route == null || ac == null) return;
            route.EnsureFlights();
            int n = Math.Max(1, route.flights.Count);
            float fixedDaily = OpsEconomics.RouteFixedDailyWan(route.distanceKm, ac.type);
            float projected = CapacityEconomics.ProjectDailyProfit(gs, route, ac);
            float gross = projected + fixedDaily;          // 各班 dailyProfit 存毛贡献，固定成本只在航线层级扣
            float share = gross / n;
            for (int i = 0; i < route.flights.Count; i++)
            {
                if (route.flights[i] != null) route.flights[i].dailyProfit = share;
            }
            route.baseDailyProfit = share;                  // 单班基准随机型更新（与上面同源，杜绝两处口径）
            route.RecalcDailyProfit();
        }
    }
}
