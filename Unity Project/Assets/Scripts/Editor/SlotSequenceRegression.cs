using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 批次一（C0 容量服务抽取 / 机场索引）的**完整申请序列**回归。
    ///
    /// 为什么需要它 —— `CapacityServiceRegression` 只证明「19,968 格容量数字一字不差」，
    /// 那仍然是**判据层**。Codex 2026-11-09 裁决明确：判据层全等不能顶替申请序列全等，
    /// 必须走**真实调用链**看批准/拒绝、持仓时刻与方向、资金流水与余额。
    ///
    /// 本回归刻意**不新建**任何业务逻辑，只按玩家会走的路调用现有入口：
    /// <list type="number">
    /// <item>提案：`InboxManager.QueueSlotDecision`（收件箱那张待批单）；</item>
    /// <item>批准：`InboxManager.Approve`（只排期，不立刻扣款）；</item>
    /// <item>交割：`InboxManager.SettleDueEffects`（到期结算 → `StrategyManager.AcquireSlotsAt`）；</item>
    /// <item>驳回：同一 `AcquireSlotsAt` 的驳回分支（满刻 / 宵禁 / 现金不足 / 余量不足）。</item>
    /// </list>
    ///
    /// 判定方式（★2026-11-09 按 Codex 审查意见收紧）：
    /// <list type="bullet">
    /// <item>**状态自洽**：批准交割后的持仓/扣款，必须与「该刻真实可申请 &gt; 0」这一前提推出的结果一致，
    ///   不写死机场数码与金额 —— 换了容量口径也能指出「哪一步与前提不符」。</item>
    /// <item>**必测场景 N/A 即失败**：九项场景（①–⑨）逐项登记覆盖标记；
    ///   任一场景因前提缺失无法执行，整体判失败 —— 旧版只统计 fail，N/A 会被静默跳过。</item>
    /// <item>**方向各管各的**：C0 只保证**容量**在 DEP/ARR 上同值；两方向的**占用与余量**用各自的
    ///   哈希盐独立计算，不保证相等，也不得拿相等当断言。这里逐个方向核对
    ///   「段余 = Σ 逐刻余量」这一真实口径，并把两方向的数记录下来供前后比对。</item>
    /// </list>
    ///
    /// 输出格式 `SLOTSEQ|key=value`：前后版本各跑一次，把 `SLOTSEQ|` 行逐字 diff 即可。
    /// </summary>
    public static class SlotSequenceRegression
    {
        const string P = "SLOTSEQ|";

        static readonly StringBuilder Sb = new StringBuilder();
        static readonly List<string> Checks = new List<string>();
        static readonly HashSet<string> Covered = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>本批**必测**场景。任一项没被执行（N/A 或被条件跳过），整体即失败。</summary>
        static readonly string[] RequiredScenarios = { "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨" };

        static void L(string key, object v)
            => Sb.Append(P).Append(key).Append('=').Append(Render(v)).Append('\n');

        static void Chk(string what, bool ok)
            => Checks.Add((ok ? "PASS" : "FAIL") + " ; " + what);

        /// <summary>标记某个必测场景**确实执行过**（与它通过与否无关）。</summary>
        static void Covered_(string scenario) => Covered.Add(scenario);

        /// <summary>必测场景无法执行 —— 按 Codex 裁决，记 FAIL 而不是记 N/A。</summary>
        static void Na(string what, string why)
            => Checks.Add("FAIL ; 必测场景无法执行（N/A）—— " + what + "：" + why);

        static string Render(object v)
        {
            if (v == null) return "<null>";
            if (v is bool b) return b ? "true" : "false";
            if (v is float f) return f.ToString("F1", CultureInfo.InvariantCulture);
            if (v is double d) return d.ToString("F2", CultureInfo.InvariantCulture);
            if (v is IFormattable fo) return fo.ToString(null, CultureInfo.InvariantCulture);
            return v.ToString();
        }

        static string F2(float v) => v.ToString("F2", CultureInfo.InvariantCulture);

        /// <summary>诊断文本单行化：前后比对不允许换行把一行拆成两行。</summary>
        static string One(string s, int max = 200)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        /// <summary>某机场某段里第一个「公布且有剩量」的刻；找不到返回 null。</summary>
        static string FirstOpenClock(GameState gs, string airport, int band, string kind)
        {
            int step = ScheduleSlots.SlotGridStepMinutes;
            for (int m = 0; m < 1440; m += step)
            {
                string clk = ScheduleSlots.FormatClock(m);
                if (ScheduleSlots.BandOfClock(clk) != band) continue;
                if (RoutePermitLogic.QuarterCapacityAt(airport, clk) <= 0) continue;
                if (RoutePermitLogic.SlotsAvailableAt(gs, airport, clk, kind) > 0) return clk;
            }
            return null;
        }

        /// <summary>某机场某段里第一个「有容量但已被占满」的刻；找不到返回 null。</summary>
        static string FirstFullClock(GameState gs, string airport, int band, string kind)
        {
            int step = ScheduleSlots.SlotGridStepMinutes;
            for (int m = 0; m < 1440; m += step)
            {
                string clk = ScheduleSlots.FormatClock(m);
                if (ScheduleSlots.BandOfClock(clk) != band) continue;
                if (RoutePermitLogic.QuarterCapacityAt(airport, clk) <= 0) continue;
                if (RoutePermitLogic.SlotsAvailableAt(gs, airport, clk, kind) <= 0) return clk;
            }
            return null;
        }

        /// <summary>按段顺序找第一个满足条件的段（返回段号，找不到 -1）。</summary>
        static int FirstBandWhere(Func<int, bool> ok)
        {
            for (int band = 0; band < 4; band++) if (ok(band)) return band;
            return -1;
        }

        /// <summary>该段「段余」的逐刻合计 —— 与 <see cref="RoutePermitLogic.SlotsAvailable(GameState,string,int,string)"/> 同口径。</summary>
        static int SumOpenByClock(GameState gs, string airport, int band, string kind)
        {
            var grid = ScheduleSlots.BandGridLabels(band);
            int total = 0;
            for (int i = 0; i < grid.Count; i++)
            {
                int cap = RoutePermitLogic.QuarterCapacityAt(airport, grid[i]);
                if (cap <= 0) continue;
                int used = RoutePermitLogic.PoolUseAt(gs, airport, grid[i], kind)
                           + IndustryAirlineLogic.IndustryAtClock(gs, airport, grid[i], kind);
                int left = cap - used;
                if (left > 0) total += left;
            }
            return total;
        }

        static void DumpOpen(GameState gs, string key, string airport, int band, string kind)
        {
            L(key + ".band", band);
            L(key + ".poolCap", RoutePermitLogic.SlotPoolCapacity(airport, band, kind));
            L(key + ".ourUse", RoutePermitLogic.PoolUse(gs, airport, band, kind));
            L(key + ".industry", IndustryAirlineLogic.IndustryHeld(gs, airport, band, kind));
            L(key + ".available", RoutePermitLogic.SlotsAvailable(gs, airport, band, kind));
            L(key + ".sumOpenByClock", SumOpenByClock(gs, airport, band, kind));
        }

        static void DumpHoldings(string key, GameState gs)
        {
            var slots = gs.stg != null ? gs.stg.slots : null;
            L(key + ".count", slots == null ? 0 : slots.Count);
            if (slots == null) return;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null) { L(key + "[" + i + "]", "<null>"); continue; }
                L(key + "[" + i + "]",
                    s.airport + "|" + s.slotTime + "|" + SlotHolding.KindOf(s)
                    + "|arr=" + (string.IsNullOrEmpty(s.arrTime) ? "-" : s.arrTime)
                    + "|cost=" + F2(s.acquisitionCostWan)
                    + "|inUse=" + s.inUse
                    + "|expQ=" + s.expiresQuarter);
            }
        }

        static void DumpMails(string key, GameState gs)
        {
            if (gs.inbox == null) { L(key + ".count", 0); return; }
            L(key + ".count", gs.inbox.Count);
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null) { L(key + "[" + i + "]", "<null>"); continue; }
                L(key + "[" + i + "]",
                    (m.id ?? "-") + "|" + (m.action ?? "-") + "|" + (m.status ?? "-")
                    + "|" + (m.tag ?? "-") + "|" + One(m.subject, 60)
                    + "|body=" + One(m.body, 240));
            }
        }

        static InboxMail FindMail(GameState gs, string id)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && string.Equals(gs.inbox[i].id, id, StringComparison.Ordinal))
                    return gs.inbox[i];
            return null;
        }

        static string StatusOf(GameState gs, string id)
        {
            var m = FindMail(gs, id);
            return m == null ? "<missing>" : (m.status ?? "<null>");
        }

        public static bool Run()
        {
            Sb.Clear();
            Checks.Clear();
            Covered.Clear();
            var go = new GameObject("__slot_sequence__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.inbox = new List<InboxMail>();
                gs.hubIata = "PEK";
                gs.year = 1;
                gs.quarter = 1;
                gs.cash = 6000f;                    // 万美元
                // 固定游戏时刻：不依赖真实 UTC，前后两次跑一定同一天。
                gs.SetSimUtc(new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc));
                StrategyManager.EnsureState(gs);

                const string APT = "PEK";
                string KIND_DEP = SlotHolding.SlotKindDeparture;
                string KIND_ARR = SlotHolding.SlotKindArrival;

                L("setup.apt", APT);
                L("setup.tier", AirportCatalog.TierOfCode(APT));
                L("setup.runways", AirportCatalog.RunwayCount(APT));
                L("setup.dayCapacity", AirportCatalog.DayCapacity(APT));
                L("setup.cash", gs.cash);
                L("setup.clock", gs.GameUtcClockLabel);
                L("setup.holdings", gs.stg.slots.Count);

                /* ---------- ① 合法申请：提案 → 批准 → 到期交割 ---------- */

                bool q1 = InboxManager.QueueSlotDecision(gs, APT, "09:00", "m_seq_buy", out string note1);
                L("prop1.ok", q1);
                L("prop1.note", One(note1));
                L("prop1.inbox", gs.inbox.Count);
                L("prop1.status", StatusOf(gs, "m_seq_buy"));

                var mail1 = FindMail(gs, "m_seq_buy");
                string clock1 = mail1 == null ? "" : (mail1.slotClock ?? mail1.dest ?? "");
                string kind1 = mail1 != null && !string.IsNullOrEmpty(mail1.slotKind) ? mail1.slotKind : KIND_DEP;
                int avail1 = string.IsNullOrEmpty(clock1) ? 0
                    : RoutePermitLogic.SlotsAvailableAt(gs, APT, clock1, kind1);
                float price1 = string.IsNullOrEmpty(clock1) ? 0f : StrategyEconomics.SlotValueWanAt(APT, clock1);
                L("prop1.clock", clock1);
                L("prop1.avail", avail1);
                L("prop1.price", price1);

                float cashBefore1 = gs.cash;
                int holdBefore1 = gs.stg.slots.Count;
                string amsg1 = "";
                bool a1 = mail1 != null && InboxManager.Approve(gs, mail1, out amsg1);
                L("approve1.ok", a1);
                L("approve1.msg", One(amsg1));
                L("approve1.status", StatusOf(gs, "m_seq_buy"));
                // 批准**只排期**：这一刻不得扣款、不得落持仓（交割在到期结算里）。
                float cashAfterApprove = gs.cash;
                int holdAfterApprove = gs.stg.slots.Count;
                L("approve1.cashDelta", cashAfterApprove - cashBefore1);
                L("approve1.holdDelta", holdAfterApprove - holdBefore1);
                Chk("①批准只排期：不动现金", Mathf.Abs(cashAfterApprove - cashBefore1) < 0.001f);
                Chk("①批准只排期：不落持仓", holdAfterApprove == holdBefore1);

                InboxManager.SettleDueEffects(gs, gs.SimUtc.AddDays(90));
                float cashAfter1 = gs.cash;
                L("settle1.status", StatusOf(gs, "m_seq_buy"));
                L("settle1.cashDelta", cashAfter1 - cashBefore1);
                L("settle1.holdDelta", gs.stg.slots.Count - holdBefore1);
                DumpHoldings("settle1.hold", gs);
                DumpMails("settle1.mail", gs);

                Chk("①提案被受理（收件箱有待批单）", q1 && mail1 != null);
                bool settled1 = gs.stg.slots.Count - holdBefore1 == 1;
                if (avail1 <= 0)
                {
                    Na("①合法申请交割", "提案刻已被他航占满（提案被改刻），见 prop1.clock");
                }
                else
                {
                    Covered_("①");
                    Chk("①有剩量时交割落 1 条持仓", settled1);
                    if (settled1)
                    {
                        var h = gs.stg.slots[gs.stg.slots.Count - 1];
                        L("check1.holding", h.airport + "|" + h.slotTime + "|" + SlotHolding.KindOf(h)
                                            + "|cost=" + F2(h.acquisitionCostWan));
                        Chk("①持仓时刻与提案时刻一致（" + clock1 + " → " + h.slotTime + "）",
                            string.Equals(h.slotTime, clock1, StringComparison.Ordinal));
                        Chk("①持仓单价与该刻报价一致（" + F2(price1) + " → " + F2(h.acquisitionCostWan) + "）",
                            Mathf.Abs(h.acquisitionCostWan - price1) < 0.51f);
                        Chk("①扣款等于该刻报价（前 " + F2(cashBefore1) + " → 后 " + F2(cashAfter1) + "）",
                            Mathf.Abs(cashBefore1 - cashAfter1 - price1) < 0.51f);
                    }
                }

                /* ---------- ② 满刻驳回：不得改持仓、不得动现金 ---------- */

                string full = FirstFullClock(gs, APT, 1, KIND_DEP);
                L("full.clock", full ?? "<none>");
                if (full == null)
                {
                    Na("②满刻驳回", "中班段每个开放刻都有余量，构造不出满刻场景");
                }
                else
                {
                    Covered_("②");
                    float c2 = gs.cash;
                    int h2 = gs.stg.slots.Count;
                    bool r2 = StrategyManager.AcquireSlotsAt(gs, APT, full, 1, false, out string msg2);
                    L("full.ok", r2);
                    L("full.msg", One(msg2));
                    L("full.cashDelta", gs.cash - c2);
                    L("full.holdDelta", gs.stg.slots.Count - h2);
                    Chk("②满刻被驳回（" + full + "）", !r2);
                    Chk("②满刻驳回不动钱", Mathf.Abs(gs.cash - c2) < 0.001f);
                    Chk("②满刻驳回不动持仓", gs.stg.slots.Count == h2);
                }

                /* ---------- ③ 宵禁驳回 ---------- */

                {
                    Covered_("③");
                    float c3 = gs.cash;
                    int h3 = gs.stg.slots.Count;
                    bool r3 = StrategyManager.AcquireSlotsAt(gs, APT, "02:00", 1, true, out string msg3);
                    L("curfew.ok", r3);
                    L("curfew.msg", One(msg3));
                    L("curfew.cashDelta", gs.cash - c3);
                    Chk("③宵禁刻（02:00）被驳回", !r3);
                    Chk("③宵禁驳回不动钱", Mathf.Abs(gs.cash - c3) < 0.001f);
                    Chk("③宵禁驳回不动持仓", gs.stg.slots.Count == h3);
                }

                /* ---------- ④ 现金不足驳回（免费渠道不收费，故必须走二级市场） ---------- */

                {
                    int band4 = FirstBandWhere(b => FirstOpenClock(gs, APT, b, KIND_DEP) != null);
                    string open4 = band4 < 0 ? null : FirstOpenClock(gs, APT, band4, KIND_DEP);
                    L("short.clock", open4 ?? "<none>");
                    if (open4 == null)
                    {
                        Na("④现金不足驳回", "没有任何段存在可申请的离港刻");
                    }
                    else
                    {
                        Covered_("④");
                        float keep = gs.cash;
                        gs.cash = 1f;                       // 1 万美元，买不起任何一条
                        int h4 = gs.stg.slots.Count;
                        bool r4 = StrategyManager.AcquireSlotsAt(gs, APT, open4, 1, false, out string msg4);
                        L("short.ok", r4);
                        L("short.msg", One(msg4));
                        L("short.cash", gs.cash);
                        L("short.holdDelta", gs.stg.slots.Count - h4);
                        Chk("④现金不足被驳回", !r4);
                        Chk("④现金不足不扣款", Mathf.Abs(gs.cash - 1f) < 0.001f);
                        Chk("④现金不足不落持仓", gs.stg.slots.Count == h4);
                        gs.cash = keep;
                    }
                }

                /* ---------- ⑤ 协调池（免费）+ 到港方向 ---------- */

                {
                    int band5 = FirstBandWhere(b => FirstOpenClock(gs, APT, b, KIND_ARR) != null);
                    string open5 = band5 < 0 ? null : FirstOpenClock(gs, APT, band5, KIND_ARR);
                    L("arr.clock", open5 ?? "<none>");
                    if (open5 == null)
                    {
                        Na("⑤到港持仓", "没有任何段存在可申请的到港刻");
                    }
                    else
                    {
                        Covered_("⑤");
                        float c5 = gs.cash;
                        int h5 = gs.stg.slots.Count;
                        bool r5 = StrategyManager.AcquireSlotsAt(gs, APT, open5, 1, true,
                            out string msg5, out var assigned5, KIND_ARR, null);
                        L("arr.ok", r5);
                        L("arr.msg", One(msg5));
                        L("arr.assigned", assigned5 == null ? "-" : string.Join(",", assigned5));
                        L("arr.cashDelta", gs.cash - c5);
                        L("arr.holdDelta", gs.stg.slots.Count - h5);
                        Chk("⑤免费渠道（协调池）不收费", Mathf.Abs(gs.cash - c5) < 0.001f);
                        if (r5 && gs.stg.slots.Count > h5)
                        {
                            var h = gs.stg.slots[gs.stg.slots.Count - 1];
                            Chk("⑤到港方向入账（kind=" + SlotHolding.KindOf(h) + "）",
                                string.Equals(SlotHolding.KindOf(h), KIND_ARR, StringComparison.Ordinal));
                            Chk("⑤只到港持仓没有配对离港", string.IsNullOrEmpty(h.arrTime));
                        }
                        else Na("⑤到港入账", "取得失败：" + One(msg5, 60));
                    }
                }

                /* ---------- ⑥ 一槽一对：离港持仓必须带上配对到港时刻 ---------- */

                {
                    // 同一段内要**两端都有**可申请刻，才谈得上「一对」。
                    int band6 = FirstBandWhere(b => FirstOpenClock(gs, APT, b, KIND_DEP) != null
                                                    && FirstOpenClock(gs, APT, b, KIND_ARR) != null);
                    string open6 = band6 < 0 ? null : FirstOpenClock(gs, APT, band6, KIND_DEP);
                    string arr6 = band6 < 0 ? null : FirstOpenClock(gs, APT, band6, KIND_ARR);
                    L("pair.clock", open6 ?? "<none>");
                    L("pair.arrClock", arr6 ?? "<none>");
                    if (open6 == null || arr6 == null)
                    {
                        Na("⑥配对入账", "找不到同一段内两端都可申请的刻");
                    }
                    else
                    {
                        Covered_("⑥");
                        int h6 = gs.stg.slots.Count;
                        bool r6 = StrategyManager.AcquireSlotsAt(gs, APT, open6, 1, true,
                            out string msg6, out var assigned6, KIND_DEP, arr6);
                        L("pair.ok", r6);
                        L("pair.msg", One(msg6));
                        L("pair.holdDelta", gs.stg.slots.Count - h6);
                        if (r6 && gs.stg.slots.Count > h6)
                        {
                            var h = gs.stg.slots[gs.stg.slots.Count - 1];
                            L("pair.holding", h.slotTime + "|arr=" + h.arrTime);
                            Chk("⑥配对到港时刻入账（" + arr6 + " → "
                                + (string.IsNullOrEmpty(h.arrTime) ? "-" : h.arrTime) + "）",
                                string.Equals(h.arrTime, arr6, StringComparison.Ordinal));
                            Chk("⑥离港持仓 kind 为空串", string.IsNullOrEmpty(h.kind));
                        }
                        else Na("⑥配对入账", "取得失败：" + One(msg6, 60));
                    }
                }

                /* ---------- ⑦ 余量不足（池口径）驳回 ---------- */

                {
                    Covered_("⑦");
                    float c7 = gs.cash;
                    int h7 = gs.stg.slots.Count;
                    bool r7 = StrategyManager.AcquireSlotsAt(gs, APT, "09:00", 999, true, out string msg7);
                    L("pool.ok", r7);
                    L("pool.msg", One(msg7));
                    L("pool.cashDelta", gs.cash - c7);
                    L("pool.holdDelta", gs.stg.slots.Count - h7);
                    Chk("⑦超余量（999 条）被驳回", !r7);
                    Chk("⑦超余量不落持仓", gs.stg.slots.Count == h7);
                }

                /* ---------- ⑧ 同一 id 重复提案 ---------- */

                {
                    Covered_("⑧");
                    bool q8 = InboxManager.QueueSlotDecision(gs, APT, "09:00", "m_seq_buy", out string note8);
                    L("dup.ok", q8);
                    L("dup.note", One(note8));
                    Chk("⑧同一 id 重复提案被挡", !q8);
                }

                /* ---------- ⑨ 背景占用与申请余量（**两个方向各自**自洽） ---------- */

                {
                    Covered_("⑨");
                    const int band = 1;
                    // C0 只承诺「容量」在两个方向同值 —— 这是本批的规则，钉住它。
                    L("occ.poolCapDep", RoutePermitLogic.SlotPoolCapacity(APT, band, KIND_DEP));
                    L("occ.poolCapArr", RoutePermitLogic.SlotPoolCapacity(APT, band, KIND_ARR));
                    Chk("⑨ C0 两方向池容量同值",
                        RoutePermitLogic.SlotPoolCapacity(APT, band, KIND_DEP)
                        == RoutePermitLogic.SlotPoolCapacity(APT, band, KIND_ARR));
                    // 逐刻容量与方向无关（kind 只贯通、不改返回值）—— 同一条规则的另一面。
                    Chk("⑨ 逐刻容量不分方向（DEP=ARR）",
                        CapacityService.Quarter(APT, 9 * 60, KIND_DEP)
                        == CapacityService.Quarter(APT, 9 * 60, KIND_ARR));

                    // 占用与余量**按方向各算一份**：两个方向用独立哈希盐，数值本就不必相等。
                    // 这里核对的是真实口径「段余 = Σ 逐刻 max(0, 容量 − 我司 − 他航)」，
                    // 并把两方向的数逐项记录，供前后版本比对 —— 不拿「两方向相等」当断言。
                    DumpOpen(gs, "occ.dep", APT, band, KIND_DEP);
                    DumpOpen(gs, "occ.arr", APT, band, KIND_ARR);
                    Chk("⑨ 离港段余 = 逐刻余量之和",
                        RoutePermitLogic.SlotsAvailable(gs, APT, band, KIND_DEP)
                        == SumOpenByClock(gs, APT, band, KIND_DEP));
                    Chk("⑨ 到港段余 = 逐刻余量之和",
                        RoutePermitLogic.SlotsAvailable(gs, APT, band, KIND_ARR)
                        == SumOpenByClock(gs, APT, band, KIND_ARR));
                    // 09:00 这一刻两方向的余量**各自**要与「该刻容量 − 各自占用」一致。
                    L("occ.clocks09dep", RoutePermitLogic.SlotsAvailableAt(gs, APT, "09:00", KIND_DEP));
                    L("occ.clocks09arr", RoutePermitLogic.SlotsAvailableAt(gs, APT, "09:00", KIND_ARR));
                    Chk("⑨ 09:00 离港余量 = 容量 − 我司 − 他航",
                        RoutePermitLogic.SlotsAvailableAt(gs, APT, "09:00", KIND_DEP)
                        == Mathf.Max(0, RoutePermitLogic.QuarterCapacityAt(APT, "09:00")
                                        - RoutePermitLogic.PoolUseAt(gs, APT, "09:00", KIND_DEP)
                                        - IndustryAirlineLogic.IndustryAtClock(gs, APT, "09:00", KIND_DEP)));
                    Chk("⑨ 09:00 到港余量 = 容量 − 我司 − 他航",
                        RoutePermitLogic.SlotsAvailableAt(gs, APT, "09:00", KIND_ARR)
                        == Mathf.Max(0, RoutePermitLogic.QuarterCapacityAt(APT, "09:00")
                                        - RoutePermitLogic.PoolUseAt(gs, APT, "09:00", KIND_ARR)
                                        - IndustryAirlineLogic.IndustryAtClock(gs, APT, "09:00", KIND_ARR)));
                }

                DumpHoldings("final.hold", gs);
                L("final.cash", gs.cash);
                L("final.inbox", gs.inbox == null ? 0 : gs.inbox.Count);

                // 覆盖标记：九项必测场景缺一不可。旧版只看 fail，N/A 会被静默算过。
                for (int i = 0; i < RequiredScenarios.Length; i++)
                    Chk("场景 " + RequiredScenarios[i] + " 已实际执行", Covered.Contains(RequiredScenarios[i]));

                int pass = 0, fail = 0;
                for (int i = 0; i < Checks.Count; i++)
                {
                    string line = Checks[i];
                    if (line.StartsWith("PASS", StringComparison.Ordinal)) pass++;
                    else fail++;
                    Sb.Append(P).Append("check.").Append(i.ToString("00", CultureInfo.InvariantCulture))
                      .Append('=').Append(line).Append('\n');
                }
                int covered = Covered.Count;
                int required = RequiredScenarios.Length;
                bool all = fail == 0 && covered == required;
                Sb.Append(P).Append("check.summary=").Append("pass=").Append(pass)
                  .Append(" fail=").Append(fail)
                  .Append(" covered=").Append(covered).Append("/").Append(required).Append('\n');
                Sb.Append(P).Append("check.all=").Append(all ? "true" : "false").Append('\n');

                Debug.Log("[SLOTSEQ] 申请序列回归\n" + Sb);
                return all;
            }
            catch (Exception e)
            {
                Debug.LogError("[SLOTSEQ] 抛异常：" + e);
                Debug.Log("[SLOTSEQ] 已完成部分\n" + Sb);
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
