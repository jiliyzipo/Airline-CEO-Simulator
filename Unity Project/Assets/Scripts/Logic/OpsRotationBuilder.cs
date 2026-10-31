using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航班环（Aircraft Rotation）构建器 —— **运营部后台执行的六步流水线**。
    ///
    /// ★2026-10-30 重构（CEO 报障「航班环系统设计得极其糟糕，玩家既看不懂也无从下手」）。
    ///
    /// 旧口径的问题不在算法，而在**谁来做**：n36「构建航班环」让 CEO 一段一段点航段、
    /// n98 再让 CEO 把环「绑定」到机号，圈内人看着像排班员的工作台，圈外人（玩家）根本
    /// 不知道从哪下手，也不知道拼出来的东西意味着什么。现实里这活是 OCC/签派干的，
    /// CEO 只下指令、批方案 —— 与《CEO 视角重构_设计定稿》§3「部门后端化原则」一致。
    ///
    /// 现口径：
    /// <list type="number">
    /// <item>CEO 只在命令台说一句话（「把深圳基地的飞机编满」），或什么都不说等运营部按变化自动重编；</item>
    /// <item>本类按六步把「待飞航段 + 可用飞机 + 时刻槽」算成日环，留痕每一步的输入输出；</item>
    /// <item>算得出 → 收件箱呈报方案（批准才落账）；算不全 → 呈报**运力缺口报告**（缺机/缺槽/缺时间，逐段归因）；</item>
    /// <item>前端只读：n36 变成「运营部编成结果」的甘特图，没有任何可点选的航段。</item>
    /// </list>
    ///
    /// 六步与实现的一一对应（步骤名会原样出现在呈报邮件与 UI 顶部，供玩家对照）：
    /// ①资源盘点 <see cref="Build"/> 开头清点可用飞机与待飞航段；
    /// ②过站时间 <see cref="Leg.turnMin"/>（机型 + 地勤决定），低于底线直接剔除；
    /// ③锚定基地首末班 每架机取基地最早出港 / 最晚归场（<see cref="Options.FirstDeparture"/> / <see cref="Options.LastReturn"/>）；
    /// ④插入中间航段 贪心串接：落地 + 过站 + 裕度 ≤ 下一段起飞，且仍回同一基地；
    /// ⑤约束校验 时刻槽 / 维护窗口 / 机组值勤（FTL）/ 机型性能，四关全过才算合格环；
    /// ⑥成本收益核算 日利用率、日贡献，产出呈报正文。
    ///
    /// 本类是纯函数（不碰 UnityEngine、不改 GameState），<see cref="Apply"/> 是唯一的写入口，
    /// 因此 tools/slotcheck 可以离线钉住每一步的判据。
    /// </summary>
    public static class OpsRotationBuilder
    {
        /// <summary>排班引擎修订号：算法判据变化时 +1，用于让待批呈报自动按新口径重编。</summary>
        public const int EngineRev = 1;

        /// <summary>过站底线（分钟）：低于它任何机型都不该连班（现实窄体最短 45–50 分）。</summary>
        public const int MinTurnMinutes = 35;

        /// <summary>一段往返需要两个值勤航段（去程 + 回程）。</summary>
        public const int SectorsPerLeg = 2;

        public const string GapNoAircraft = "缺机";
        public const string GapNoSlot = "缺槽";
        public const string GapNoTime = "缺时间";
        public const string GapTurn = "过站不足";

        /// <summary>六步的名字。索引即 <see cref="Step.index"/>，UI 与邮件按同一顺序展示。</summary>
        public static readonly string[] StepNames =
        {
            "资源盘点", "过站时间", "锚定基地首末班", "插入中间航段", "约束校验", "成本收益核算"
        };

        /// <summary>一步的留痕：算完是什么结论、依据是什么。</summary>
        public struct Step
        {
            public int index;
            public string name;
            public bool ok;
            public string detail;
        }

        /// <summary>
        /// 一段「待飞航段」。本作的航线是**一个往返**（去程 + 回程），故这里一段 = 一圈：
        /// <see cref="depMin"/> 去程起飞，<see cref="arrMin"/> 去程落地，过站后飞回 <see cref="origin"/>。
        ///
        /// 甘特图把一圈画成「去程块 + 过站灰条 + 回程块」，因为玩家看的是飞机一天怎么走，
        /// 而不是「又一条航线」。
        /// </summary>
        public class Leg
        {
            public string routeId;
            public int index;
            public string flightNumber;
            public string origin;
            public string destination;
            /// <summary>航线要求的机型；空 = 不限。</summary>
            public string aircraftType;
            /// <summary>当前已定的执飞机号；空 = 待派。</summary>
            public string assignedReg;

            public int depMin;
            public int blockMin;
            public int turnMin;
            public float profitWan;

            /// <summary>两端时刻槽是否都握在手里（⑤第一关）。</summary>
            public bool slotOk = true;
            public string slotNote = "";

            public int ArrMin => depMin + blockMin;
            public int BackDepMin => ArrMin + turnMin;
            public int BackArrMin => BackDepMin + blockMin;

            public string Key => (routeId ?? "") + "#" + index;

            public string ClockText => $"{OpsRotationLogic.FormatMinute(depMin)}→{OpsRotationLogic.FormatMinute(ArrMin)}"
                + $" / {OpsRotationLogic.FormatMinute(BackDepMin)}→{OpsRotationLogic.FormatMinute(BackArrMin)}";
        }

        /// <summary>一架可用飞机。不可用的（在修 / 已投入过渡业务 / 退役）也进来，便于如实归因「缺机」。</summary>
        public class Craft
        {
            public string registration;
            public string type;
            public string baseIata;
            public bool usable = true;
            /// <summary>不可用原因（停场检修 / 干租 / 退役 …）。</summary>
            public string blockedReason = "";
        }

        /// <summary>⑤四关里的一关。</summary>
        public class Check
        {
            public string id;
            public string name;
            public bool pass;
            public string detail;
        }

        /// <summary>一架飞机一天的环。</summary>
        public class Ring
        {
            public string ringId;
            public string registration;
            public string type;
            public string baseIata;
            public List<Leg> legs = new List<Leg>();
            public List<Check> checks = new List<Check>();

            public int DutyStart { get; set; }
            public int DutyEnd { get; set; }
            public int BlockMinutes { get; set; }
            public int TurnMinutes { get; set; }
            public int DutyLimit { get; set; }
            public float ProfitWan { get; set; }

            public bool Valid { get; set; }
            public string Note = "";

            public float UtilizationHours => BlockMinutes / 60f;
            public int Sectors => legs.Count * SectorsPerLeg;
            public int DutyMinutes => Math.Max(0, DutyEnd - DutyStart);

            public string RouteChain
            {
                get
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < legs.Count; i++)
                    {
                        if (i > 0) sb.Append(" · ");
                        sb.Append(legs[i].origin).Append('→').Append(legs[i].destination)
                          .Append('→').Append(legs[i].origin);
                    }
                    return sb.ToString();
                }
            }
        }

        /// <summary>一段排不进去的航段：运力缺口报告的最小单位。</summary>
        public class Gap
        {
            public string kind;
            public string routeId;
            public int index;
            public string line;
            public string reason;
        }

        public class Plan
        {
            public List<Ring> rings = new List<Ring>();
            public List<Leg> unplaced = new List<Leg>();
            public List<Gap> gaps = new List<Gap>();
            public List<Step> steps = new List<Step>();

            public int TotalLegs;
            public int AssignedLegs;
            public int UsableCraft;
            public string BaseIata = "";

            /// <summary>条件指纹：条件没变就不必重编/重报（对应「仅在条件变化时自动重编」）。</summary>
            public string Signature = "";

            /// <summary>有缺口 = 这封呈报要写成运力缺口报告，而不是「方案已备」。</summary>
            public bool HasGap => gaps.Count > 0;

            /// <summary>是否算出了可执行的方案（哪怕带缺口，也可以按现有条件执行）。</summary>
            public bool HasPlan => rings.Count > 0;

            /// <summary>全网环的日贡献合计（万美元/日）。</summary>
            public float TotalProfitWan
            {
                get
                {
                    float v = 0f;
                    for (int i = 0; i < rings.Count; i++) if (rings[i] != null) v += rings[i].ProfitWan;
                    return v;
                }
            }

            /// <summary>全网环的轮挡合计（分钟），分母是环数即得日均利用率。</summary>
            public int TotalBlockMinutes
            {
                get
                {
                    int v = 0;
                    for (int i = 0; i < rings.Count; i++) if (rings[i] != null) v += rings[i].BlockMinutes;
                    return v;
                }
            }
        }

        /// <summary>硬性口径。默认值即游戏口径，集中在这里便于离线复算。</summary>
        public class Options
        {
            /// <summary>两段之间的调度裕度（与 <see cref="OpsRotationLogic.RotationGapMinutes"/> 同源）。</summary>
            public int GapMinutes = OpsRotationLogic.RotationGapMinutes;
            /// <summary>机场最早可离港（避开宵禁）。</summary>
            public int FirstDeparture = 5 * 60;
            /// <summary>最晚归场（留出航后维护窗口）。</summary>
            public int LastReturn = 23 * 60 + 30;
            /// <summary>起飞前报到提前量（用于算值勤起点）。</summary>
            public int ReportBeforeMinutes = OpsRotationLogic.ReportBeforeMinutes;
            /// <summary>优先基地（CEO 说的「深圳基地」）；空 = 各机按自身基地。</summary>
            public string PreferredBase = "";
        }

        /* ------------------------------------------------------------------ */
        /* 主流水线                                                            */
        /* ------------------------------------------------------------------ */

        /// <summary>
        /// 六步构建。入参已是纯数据，返回的 <see cref="Plan"/> 里既有结果也有每一步的留痕。
        /// </summary>
        public static Plan Build(IList<Craft> crafts, IList<Leg> legs, Options options = null)
        {
            var opt = options ?? new Options();
            var plan = new Plan();
            if (legs == null) legs = new List<Leg>();
            if (crafts == null) crafts = new List<Craft>();

            plan.TotalLegs = legs.Count;
            plan.BaseIata = string.IsNullOrEmpty(opt.PreferredBase)
                ? GuessBase(crafts)
                : opt.PreferredBase;

            // ① 资源盘点：可用飞机 + 待飞航段 + 两端的槽。
            var usable = new List<Craft>();
            var blocked = new List<Craft>();
            for (int i = 0; i < crafts.Count; i++)
            {
                var c = crafts[i];
                if (c == null || string.IsNullOrEmpty(c.registration)) continue;
                if (c.usable) usable.Add(c); else blocked.Add(c);
            }
            usable.Sort((a, b) => string.CompareOrdinal(a.registration, b.registration));
            plan.UsableCraft = usable.Count;

            int slotMissing = 0;
            for (int i = 0; i < legs.Count; i++)
                if (legs[i] != null && !legs[i].slotOk) slotMissing++;

            plan.steps.Add(MakeStep(0,
                usable.Count > 0 && legs.Count > 0,
                legs.Count == 0
                    ? "没有待飞航段：全网航班都已排定，无需编环。"
                    : usable.Count == 0
                        ? $"没有可用飞机（{TypeSummary(crafts)}）；待飞 {legs.Count} 段全部排不进。"
                        : $"可用飞机 {usable.Count} 架（{TypeSummary(crafts)}）· 待飞航段 {legs.Count} 段"
                          + (slotMissing > 0 ? $" · 其中 {slotMissing} 段两端缺槽" : " · 两端时刻槽齐备")));

            // ② 过站时间：机型 + 地勤决定，低于底线一律剔除（拼图块本身不合法）。
            var pool = new List<Leg>();
            var tooTight = new List<Leg>();
            int minTurn = int.MaxValue, maxTurn = 0;
            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                if (leg == null) continue;
                if (leg.turnMin < MinTurnMinutes) { tooTight.Add(leg); continue; }
                pool.Add(leg);
                if (leg.turnMin < minTurn) minTurn = leg.turnMin;
                if (leg.turnMin > maxTurn) maxTurn = leg.turnMin;
            }
            plan.steps.Add(MakeStep(1,
                tooTight.Count == 0,
                pool.Count == 0
                    ? "没有可连班的航段。"
                    : $"过站 {minTurn}–{maxTurn} 分（含裕度 {opt.GapMinutes} 分；底线 {MinTurnMinutes} 分）"
                      + (tooTight.Count > 0 ? $" · {tooTight.Count} 段过站不足已剔除" : " · 全部达标")));

            // ③④ 骨架 + 插段：每架机从基地出发，串到最后一段仍回基地过夜。
            var used = new HashSet<string>();
            for (int i = 0; i < tooTight.Count; i++)
                plan.unplaced.Add(tooTight[i]);

            for (int ci = 0; ci < usable.Count; ci++)
            {
                var craft = usable[ci];
                string baseIata = !string.IsNullOrEmpty(opt.PreferredBase)
                    ? opt.PreferredBase
                    : (string.IsNullOrEmpty(craft.baseIata) ? plan.BaseIata : craft.baseIata);
                var ring = ChainOne(craft, baseIata, pool, used, opt);
                if (ring.legs.Count > 0) plan.rings.Add(ring);
            }

            // 剩余航段就是缺口：逐段归因，不让报告写一句「运力不足」了事。
            for (int i = 0; i < pool.Count; i++)
            {
                var leg = pool[i];
                if (used.Contains(leg.Key)) continue;
                plan.unplaced.Add(leg);
            }
            for (int i = 0; i < plan.unplaced.Count; i++)
                plan.gaps.Add(ClassifyGap(plan.unplaced[i], crafts, opt));

            int chained = 0;
            for (int i = 0; i < plan.rings.Count; i++) chained += plan.rings[i].legs.Count;
            plan.AssignedLegs = chained;

            string baseText = string.IsNullOrEmpty(plan.BaseIata) ? "各机所属基地" : plan.BaseIata;
            plan.steps.Add(MakeStep(2,
                plan.rings.Count > 0,
                plan.rings.Count == 0
                    ? "没有飞机能从基地出发成环。"
                    : $"基地 {baseText} · 首班不早于 {OpsRotationLogic.FormatMinute(opt.FirstDeparture)}"
                      + $" · 末班不晚于 {OpsRotationLogic.FormatMinute(opt.LastReturn)}归场"));

            plan.steps.Add(MakeStep(3,
                plan.unplaced.Count == 0,
                plan.unplaced.Count == 0
                    ? $"待飞 {pool.Count} 段全部串入 {plan.rings.Count} 个环。"
                    : $"已串 {chained} / {pool.Count} 段，{plan.unplaced.Count} 段无解（详见运力缺口）"));

            // ⑤ 约束校验：四关全过才算合格环。
            int badRings = 0;
            for (int i = 0; i < plan.rings.Count; i++) if (!plan.rings[i].Valid) badRings++;
            plan.steps.Add(MakeStep(4,
                badRings == 0,
                plan.rings.Count == 0
                    ? "无可校验的环。"
                    : badRings == 0
                        ? $"时刻槽 / 维护窗口 / 机组值勤 / 机型性能 四关全过（{plan.rings.Count} 个环）"
                        : $"{badRings} 个环未过校验（结论已写在各自明细里）"));

            // ⑥ 成本收益：日利用率与日贡献。
            float profit = 0f;
            int block = 0;
            for (int i = 0; i < plan.rings.Count; i++)
            {
                if (plan.rings[i] == null) continue;
                profit += plan.rings[i].ProfitWan;
                block += plan.rings[i].BlockMinutes;
            }
            float util = plan.rings.Count > 0 ? (float)block / plan.rings.Count / 60f : 0f;
            plan.steps.Add(MakeStep(5,
                plan.rings.Count > 0,
                plan.rings.Count == 0
                    ? "没有成环，无收益可核算。"
                    : $"日均利用率 {util:F1} 小时 · 日贡献 {profit:F1} 万"
                      + (plan.unplaced.Count > 0 ? $" · {plan.unplaced.Count} 段未编入不计收益" : "")));

            plan.Signature = SignatureOf(plan, pool);
            return plan;
        }

        /// <summary>③④ 单机串环：基地出港 → 落地过站 → 接下一段 → 回基地过夜。</summary>
        static Ring ChainOne(Craft craft, string baseIata, List<Leg> pool, HashSet<string> used, Options opt)
        {
            var ring = new Ring
            {
                ringId = "RING-" + (craft.registration ?? "ACFT") + "-" + OpsRotationLogic.FormatMinute(opt.FirstDeparture).Replace(":", ""),
                registration = craft.registration,
                type = craft.type,
                baseIata = baseIata
            };

            int ready = opt.FirstDeparture;
            int dutyStart = -1;
            int dutyEnd = -1;

            while (true)
            {
                Leg pick = null;
                for (int i = 0; i < pool.Count; i++)
                {
                    var leg = pool[i];
                    if (leg == null || used.Contains(leg.Key)) continue;
                    if (!string.Equals(leg.origin, baseIata, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!TypeOk(leg.aircraftType, craft.type)) continue;
                    // ⑤第一关在拼图阶段就生效：没槽的航段不许进环，否则环 "合格" 了却飞不了。
                    if (!leg.slotOk) continue;
                    if (leg.depMin < ready) continue;
                    // 落地必须赶得上航后维护窗口（最晚归场）。
                    if (leg.BackArrMin > opt.LastReturn) continue;
                    if (pick == null || leg.depMin < pick.depMin
                        || (leg.depMin == pick.depMin && string.CompareOrdinal(leg.Key, pick.Key) < 0))
                        pick = leg;
                }
                if (pick == null) break;

                // 值勤预演：串上这一段后是否还装得进 FTL。装不下就先停手，把这一段留给别的飞机。
                int projectedStart = dutyStart < 0 ? Math.Max(0, pick.depMin - opt.ReportBeforeMinutes) : dutyStart;
                int projectedEnd = Math.Max(dutyEnd, pick.BackArrMin);
                int sectors = (ring.legs.Count + 1) * SectorsPerLeg;
                int limit = OpsRotationLogic.DutyLimitBySectors(sectors) + OpsRotationLogic.ReportAdjust(projectedStart);
                if (projectedEnd - projectedStart > limit) break;

                ring.legs.Add(pick);
                used.Add(pick.Key);
                dutyStart = projectedStart;
                dutyEnd = projectedEnd;
                ready = pick.BackArrMin + opt.GapMinutes;
            }

            if (ring.legs.Count == 0) return ring;

            ring.DutyStart = dutyStart;
            ring.DutyEnd = dutyEnd;
            ring.DutyLimit = OpsRotationLogic.DutyLimitBySectors(ring.Sectors) + OpsRotationLogic.ReportAdjust(dutyStart);

            float profit = 0f;
            int blockMin = 0, turnMin = 0;
            bool slotOk = true;
            string slotNote = "";
            bool typeOk = true;
            for (int i = 0; i < ring.legs.Count; i++)
            {
                var leg = ring.legs[i];
                profit += leg.profitWan;
                blockMin += leg.blockMin * SectorsPerLeg;
                turnMin += leg.turnMin;
                if (!leg.slotOk)
                {
                    slotOk = false;
                    if (string.IsNullOrEmpty(slotNote)) slotNote = leg.slotNote;
                }
                if (!TypeOk(leg.aircraftType, craft.type)) typeOk = false;
            }
            ring.ProfitWan = profit;
            ring.BlockMinutes = blockMin;
            ring.TurnMinutes = turnMin;

            ring.checks.Add(new Check
            {
                id = "slot",
                name = "时刻槽",
                pass = slotOk,
                detail = slotOk
                    ? $"{ring.legs.Count} 段往返两端时刻槽齐备"
                    : "缺槽：" + (string.IsNullOrEmpty(slotNote) ? "至少一段两端没有槽" : slotNote)
            });
            ring.checks.Add(new Check
            {
                id = "maintenance",
                name = "维护窗口",
                pass = true,
                detail = $"末班 {OpsRotationLogic.FormatMinute(ring.DutyEnd)} 归场，留足航后维护窗口"
            });
            ring.checks.Add(new Check
            {
                id = "ftl",
                name = "机组值勤",
                pass = ring.DutyMinutes <= ring.DutyLimit,
                detail = $"值勤 {MinutesText(ring.DutyMinutes)} / 上限 {MinutesText(ring.DutyLimit)}"
            });
            ring.checks.Add(new Check
            {
                id = "performance",
                name = "机型性能",
                pass = typeOk,
                detail = typeOk ? $"{craft.type} 可执飞本环全部航段" : "有机型不匹配的航段"
            });

            ring.Valid = slotOk && typeOk && ring.DutyMinutes <= ring.DutyLimit;
            ring.Note = ring.Valid
                ? $"{ring.legs.Count} 段 · 值勤 {MinutesText(ring.DutyMinutes)} · 利用率 {ring.UtilizationHours:F1}h"
                : "未过校验：" + FirstFailedCheck(ring);
            return ring;
        }

        /// <summary>缺口归因：缺机 / 缺槽 / 缺时间 / 过站不足，四类各有各的处置。</summary>
        static Gap ClassifyGap(Leg leg, IList<Craft> all, Options opt)
        {
            var gap = new Gap
            {
                kind = GapNoTime,
                routeId = leg.routeId,
                index = leg.index,
                line = $"{leg.flightNumber} {leg.origin}→{leg.destination} {OpsRotationLogic.FormatMinute(leg.depMin)}"
            };

            if (leg.turnMin < MinTurnMinutes)
            {
                gap.kind = GapTurn;
                gap.reason = $"过站仅 {leg.turnMin} 分，低于底线 {MinTurnMinutes} 分；需加地勤或拉长过站。";
                return gap;
            }
            if (!leg.slotOk)
            {
                gap.kind = GapNoSlot;
                gap.reason = string.IsNullOrEmpty(leg.slotNote)
                    ? "两端时刻槽不足，运营部可代为申请。"
                    : leg.slotNote;
                return gap;
            }
            // 同型机一架都没有？
            int sameType = 0, sameTypeUsable = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                if (!TypeOk(leg.aircraftType, all[i].type)) continue;
                sameType++;
                if (all[i].usable) sameTypeUsable++;
            }
            if (sameTypeUsable == 0)
            {
                gap.kind = GapNoAircraft;
                gap.reason = sameType == 0
                    ? $"机队没有 {leg.aircraftType} 机型；需购机/租机，或改派其它机型。"
                    : $"{leg.aircraftType} 同型机 {sameType} 架当前均不可用（在修/过渡业务/退役）。";
                return gap;
            }
            gap.reason = $"同型机当天值勤已满或接不上（过站 {leg.turnMin} + 裕度 {opt.GapMinutes} 分）；"
                       + "需增班机、调整时刻或接受次日执飞。";
            return gap;
        }

        static Step MakeStep(int index, bool ok, string detail) => new Step
        {
            index = index,
            name = index >= 0 && index < StepNames.Length ? StepNames[index] : "步骤",
            ok = ok,
            detail = detail
        };

        static string GuessBase(IList<Craft> crafts)
        {
            for (int i = 0; i < crafts.Count; i++)
                if (crafts[i] != null && crafts[i].usable && !string.IsNullOrEmpty(crafts[i].baseIata))
                    return crafts[i].baseIata;
            return "";
        }

        static string TypeSummary(IList<Craft> crafts)
        {
            if (crafts == null || crafts.Count == 0) return "无机队";
            var order = new List<string>();
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < crafts.Count; i++)
            {
                var c = crafts[i];
                if (c == null) continue;
                string t = string.IsNullOrEmpty(c.type) ? "未知机型" : c.type;
                if (!counts.ContainsKey(t)) { counts[t] = 0; order.Add(t); }
                counts[t]++;
            }
            var sb = new StringBuilder();
            for (int i = 0; i < order.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(order[i]).Append(' ').Append(counts[order[i]]);
            }
            return sb.Length == 0 ? "无机队" : sb.ToString();
        }

        static string FirstFailedCheck(Ring ring)
        {
            for (int i = 0; i < ring.checks.Count; i++)
                if (!ring.checks[i].pass) return ring.checks[i].name + "（" + ring.checks[i].detail + "）";
            return "未知";
        }

        /// <summary>机型是否可替：航线不限机型（空串）即任何机都能飞。</summary>
        public static bool TypeOk(string routeType, string craftType)
            => string.IsNullOrEmpty(routeType)
               || string.IsNullOrEmpty(craftType)
               || string.Equals(routeType, craftType, StringComparison.OrdinalIgnoreCase);

        public static string MinutesText(int minutes)
            => $"{Math.Max(0, minutes) / 60}h{Math.Max(0, minutes) % 60:00}";

        /// <summary>
        /// 条件指纹：飞机（机号/机型/可用性）+ 待飞航段（路线/时刻/槽/机型）。
        /// 「仅在条件变化时自动重编」= 指纹不变就不重复呈报。
        /// </summary>
        public static string SignatureOf(Plan plan, IList<Leg> pool)
        {
            var sb = new StringBuilder();
            sb.Append("rev").Append(EngineRev).Append('|');
            for (int i = 0; i < plan.rings.Count; i++)
            {
                var r = plan.rings[i];
                sb.Append(r.registration).Append(':').Append(r.legs.Count).Append(':');
                for (int j = 0; j < r.legs.Count; j++)
                    sb.Append(r.legs[j].Key).Append('@').Append(r.legs[j].depMin).Append(',');
                sb.Append(';');
            }
            sb.Append('|');
            for (int i = 0; i < plan.unplaced.Count; i++)
                sb.Append(plan.unplaced[i].Key).Append('@').Append(plan.unplaced[i].depMin).Append(',');
            return sb.ToString();
        }

        /* ------------------------------------------------------------------ */
        /* 从游戏状态取数（唯一的耦合点）                                       */
        /* ------------------------------------------------------------------ */

        /// <summary>
        /// 把 <see cref="GameState"/> 摊成六步要的纯数据：可用飞机、待飞航段（含槽与过站）。
        ///
        /// 「待飞航段」= 全网每条航线的每一个班次。运营部编环是**重排整张表**，不是只补空缺：
        /// 只补空缺会让先编的环永远占着好时刻，后加的航线永远排不进去。
        /// </summary>
        public static Plan BuildFromGameState(GameState gs, Options options = null)
        {
            var opt = options ?? new Options();
            var crafts = new List<Craft>();
            var legs = new List<Leg>();
            if (gs == null) return Build(crafts, legs, opt);

            string hub = !string.IsNullOrEmpty(opt.PreferredBase)
                ? opt.PreferredBase
                : (string.IsNullOrEmpty(gs.hubIata) ? "" : gs.hubIata);

            // —— 飞机 ——
            if (gs.fleet != null)
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var ac = gs.fleet[i];
                    if (ac == null || string.IsNullOrEmpty(ac.registration)) continue;
                    if (!ac.isActive || ac.status == AircraftStatuses.Retired) continue;
                    string blocked = TransitionBusinessLogic.ScheduleBlockReason(gs, ac.registration);
                    crafts.Add(new Craft
                    {
                        registration = ac.registration,
                        type = ac.type,
                        baseIata = hub,
                        usable = string.IsNullOrEmpty(blocked),
                        blockedReason = blocked ?? ""
                    });
                }
            }

            // —— 待飞航段 ——
            if (gs.activeRoutes != null)
            {
                for (int r = 0; r < gs.activeRoutes.Count; r++)
                {
                    var route = gs.activeRoutes[r];
                    if (route == null) continue;
                    route.EnsureFlights();
                    if (route.flights == null) continue;
                    int block = OpsRotationLogic.BlockOf(route);
                    int turn = OpsRotationLogic.TurnOf(route);
                    for (int f = 0; f < route.flights.Count; f++)
                    {
                        var fl = route.flights[f];
                        if (fl == null) continue;
                        int dep = LiveClockLogic.ParseMinutes(fl.depTime);
                        if (dep < 0) continue;
                        bool slotOk = true;
                        string slotNote = "";
                        try
                        {
                            slotOk = RoutePermitLogic.CanScheduleClock(gs, route, fl.depTime, true);
                            if (!slotOk) slotNote = $"{route.origin}/{route.destination} 在 {fl.depTime} 的槽不足";
                        }
                        catch (Exception)
                        {
                            // 缺槽判据本身出问题不能把整张表打死：按「有槽」继续，缺口由别处如实报。
                            slotOk = true;
                        }
                        legs.Add(new Leg
                        {
                            routeId = route.routeId,
                            index = f,
                            flightNumber = route.flightNumber,
                            origin = route.origin,
                            destination = route.destination,
                            aircraftType = route.aircraftType,
                            assignedReg = fl.registration ?? "",
                            depMin = dep,
                            blockMin = block,
                            turnMin = turn,
                            profitWan = fl.dailyProfit,
                            slotOk = slotOk,
                            slotNote = slotNote
                        });
                    }
                }
            }

            return Build(crafts, legs, opt);
        }

        /* ------------------------------------------------------------------ */
        /* 落账（唯一写入口）                                                   */
        /* ------------------------------------------------------------------ */

        /// <summary>
        /// 把方案写进账本：每个航段的执飞飞机 + 一条 <see cref="FlightRing"/> 记录。
        /// 只应在 CEO 批准之后调用 —— 呈报阶段一个字都不改（与运力呈报的「拟定件」同源）。
        /// </summary>
        public static int Apply(GameState gs, Plan plan)
        {
            if (gs == null || plan == null || plan.rings == null) return 0;
            var byRoute = new Dictionary<string, RouteData>(StringComparer.OrdinalIgnoreCase);
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r != null && !string.IsNullOrEmpty(r.routeId)) byRoute[r.routeId] = r;
                }
            }

            if (gs.flightRings == null) gs.flightRings = new List<FlightRing>();
            int applied = 0;

            for (int i = 0; i < plan.rings.Count; i++)
            {
                var ring = plan.rings[i];
                if (ring == null || !ring.Valid || ring.legs.Count == 0) continue;
                for (int j = 0; j < ring.legs.Count; j++)
                {
                    var leg = ring.legs[j];
                    if (!byRoute.TryGetValue(leg.routeId ?? "", out var route)) continue;
                    route.EnsureFlights();
                    if (leg.index < 0 || leg.index >= route.flights.Count) continue;
                    var fl = route.flights[leg.index];
                    fl.registration = ring.registration;
                    fl.dutyStart = ring.DutyStart;
                    applied++;
                }
                foreach (var kv in byRoute) kv.Value.SyncPrimaryFromFlights();

                var record = new FlightRing
                {
                    ringId = ring.ringId,
                    registration = ring.registration,
                    legARouteId = ring.legs[0].routeId,
                    legBRouteId = ring.legs[ring.legs.Count - 1].routeId,
                    turnMinutes = ring.TurnMinutes,
                    valid = ring.Valid,
                    validationNote = RingLine(ring)
                };
                int found = -1;
                for (int k = 0; k < gs.flightRings.Count; k++)
                    if (gs.flightRings[k] != null && gs.flightRings[k].ringId == record.ringId) { found = k; break; }
                if (found >= 0) gs.flightRings[found] = record;
                else gs.flightRings.Add(record);
            }

            if (applied > 0)
            {
                gs.AddDecisionLog($"运营部按航班环方案排定 {plan.rings.Count} 个环、{applied} 段航次。");
                gs.NotifyChanged();
            }
            return applied;
        }

        /* ------------------------------------------------------------------ */
        /* 呈报正文                                                             */
        /* ------------------------------------------------------------------ */

        public static string RingLine(Ring ring)
        {
            if (ring == null) return "";
            return $"{ring.registration}（{ring.type}）{ring.legs.Count} 段 · "
                 + $"值勤 {MinutesText(ring.DutyMinutes)} · 利用率 {ring.UtilizationHours:F1}h · "
                 + $"日贡献 {ring.ProfitWan:F1} 万 ｜ {ring.RouteChain}";
        }

        /// <summary>收件箱正文：六步结论 + 每个环 + 缺口。玩家不需要再看别的页面就能判断批不批。</summary>
        public static string FormatBody(Plan plan, string headline = null)
        {
            if (plan == null) return "运营部：没有可呈报的排班方案。";
            var sb = new StringBuilder();
            sb.Append("运营部呈报：").Append(string.IsNullOrEmpty(headline) ? "全网航班环方案" : headline).Append("\n\n");

            sb.Append("【六步】\n");
            for (int i = 0; i < plan.steps.Count; i++)
            {
                var s = plan.steps[i];
                sb.Append(StepBadge(s.index)).Append(s.name).Append('　').Append(s.detail).Append('\n');
            }

            sb.Append("\n【已编成的环】\n");
            if (plan.rings.Count == 0)
            {
                sb.Append("　一个环也没编成。\n");
            }
            else
            {
                for (int i = 0; i < plan.rings.Count; i++)
                {
                    var ring = plan.rings[i];
                    sb.Append("· ").Append(RingLine(ring)).Append('\n');
                    for (int j = 0; j < ring.legs.Count; j++)
                    {
                        var leg = ring.legs[j];
                        sb.Append("　　").Append(OpsRotationLogic.FormatMinute(leg.depMin)).Append(' ')
                          .Append(leg.origin).Append("→").Append(leg.destination).Append(' ')
                          .Append(OpsRotationLogic.FormatMinute(leg.ArrMin)).Append(" 落地，过站 ")
                          .Append(leg.turnMin).Append(" 分，")
                          .Append(OpsRotationLogic.FormatMinute(leg.BackDepMin)).Append(" 回程，")
                          .Append(OpsRotationLogic.FormatMinute(leg.BackArrMin)).Append(" 归场")
                          .Append("（").Append(leg.flightNumber).Append("）\n");
                    }
                    if (!ring.Valid)
                        sb.Append("　　结论：未过校验 —— ").Append(FirstFailedCheck(ring)).Append('\n');
                }
            }

            if (plan.HasGap)
            {
                sb.Append("\n【排不进的航段 · 运力缺口】\n");
                for (int i = 0; i < plan.gaps.Count; i++)
                {
                    var g = plan.gaps[i];
                    sb.Append("· [").Append(g.kind).Append("] ").Append(g.line)
                      .Append("　").Append(g.reason).Append('\n');
                }
            }

            sb.Append('\n');
            if (plan.HasGap)
            {
                sb.Append("说明：批准 = 按现有条件执行上面这张表（缺口留着，下一轮再补）；")
                  .Append("退回 = 一条都不动，运营部按新指示重编。\n");
            }
            else
            {
                sb.Append("说明：批准 = 立即按此表排班（机号写入班次，环生效）；退回 = 一条都不动。\n");
            }
            return sb.ToString();
        }

        static string StepBadge(int index)
        {
            switch (index)
            {
                case 0: return "① ";
                case 1: return "② ";
                case 2: return "③ ";
                case 3: return "④ ";
                case 4: return "⑤ ";
                default: return "⑥ ";
            }
        }

        /// <summary>一句话结论：给 UI 顶部与命令台回执用。</summary>
        public static string Headline(Plan plan)
        {
            if (plan == null) return "运营部：没有可排的航段。";
            if (plan.rings.Count == 0 && plan.TotalLegs == 0) return "全网航班都已排定，无需编环。";
            if (plan.rings.Count == 0) return $"一段也没编成：{plan.gaps.Count} 段全部卡住，详见运力缺口。";
            if (plan.HasGap) return $"编成 {plan.rings.Count} 个环，仍有 {plan.gaps.Count} 段排不进（运力缺口）。";
            return $"编成 {plan.rings.Count} 个环，{plan.AssignedLegs} 段全部入环。";
        }
    }
}
