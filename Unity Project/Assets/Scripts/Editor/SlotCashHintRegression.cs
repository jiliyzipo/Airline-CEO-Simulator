using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// K19：**时刻槽提案必须写出现金跑道后果**。
    ///
    /// 事故背景（2026-10-22 实测）：批次 B 把济南 / 贵阳（客流过 2000 万，属协调机场）带进 ★2 档，
    /// tier 1 机场一条午间时刻槽报价 300 万，而 ★2 **开局现金 1434 万、净烧 179.5 万/月**（8 月跑道）。
    /// 玩家把现金花到 300 万上下时收到一封「建议购入 TNA 12:00 时刻槽 · 估价 300 万」的信 ——
    /// 信里从头到尾没有一个字提「买完之后还剩多少钱」。玩家点「批准」时其实是在决定自己的生死，
    /// 却看不到任何跑道信息。更糟的是交割期 <c>StrategyManager.AcquireSlotsAt</c> 会在
    /// 现金不足时直接失败，CEO 等到下季度才发现钱不够。
    ///
    /// 注：早期记录曾写「该档开局现金约 320 万 / 现金 325.5 万」，那是把底栏的
    /// **季度支出 3,255,000 元**（= 325.5 万）读成了现金 —— `StatusBarView` 只输出
    /// 三项财务数与航线/机队，**没有现金字段**。按实测应为 1434 万。
    /// （2026-10-27 起三项标签改为「季度净贡献 / 季度支出 / 季度盈亏」，并把支出改成与跑道
    /// 同源的九项成本 —— 见 `StatusBarReconcileRegression`。）
    ///
    /// 本回归钉住五件事（判据本身不变，2026-11-09 只修场景构造）：
    /// ① 数字口径：扣款后现金 / 跑道必须等于 `(cash − cost) / EffectiveMonthlyBurn`；
    /// ② 文案必须出现「个月」与扣款后现金流；
    /// ③ 现金不足以支付时要明说「不足以支付」并指向免费的协调池申请（不是沉默扣款失败）；
    /// ④ **真实提案正文**必须带上这一行 —— 只测纯函数会被「函数写好了但没接进信件」骗过；
    /// ⑤ **风险行正反两面**：跑道 &lt; 3 月必须出现，跑道充足不得出现（否则「无脑追加一行」也能过）。
    ///
    /// ★2026-11-09（Codex 裁决「修场景，不改规则」）：④⑤ 原来把「请求 TNA 12:00」当成
    /// 必然成交的时刻，并用写死的 300 万报价反推现金。容量改「跑道数平铺」后，他航按需求系数
    /// 优先占最旺的刻，12:00 常被占满 → 运营部按既有规则改刻 → 报价从 300 万掉到 177 万、
    /// 跑道从 0.64 月变成 3.68 月。那是**旧前提过期**，不是风险规则坏了。
    /// 现在改为：先探针问出「真的会成交在哪一刻、报价多少」，再按该报价构造
    /// 「报价 + 2 个月净烧钱」（正例，付完 &lt; 3 月）与「报价 + 10 个月净烧钱」（反例），
    /// 并**先断言前提成立**（现金够付、跑道确实落在断言区间）再断言正文与渲染文本。
    /// 风险阈值、文案、生产规则一律未改动。
    /// </summary>
    public static class SlotCashHintRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__slot_cash_hint__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.inbox = new List<InboxMail>();
                gs.hubIata = "TNA";
                // 复现事故现场：★2 开局现金与月净烧钱的量级，槽价 300 万。
                // 现金取整避免 .5 的四舍五入把断言变成「考运算符」而不是「考口径」。
                // 注意**不是** 325.5 —— 那个数历史上是把底栏「支出 3,255,000 元」读成了现金。
                const float cashTight = 326f;
                const float cost = 300f;
                gs.cash = cashTight;
                gs.monthlyBurnRate = 40f;      // 基准开销；EffectiveMonthlyBurn 还会加上 CEO 薪酬等 → 实测 41
                float burn = gs.EffectiveMonthlyBurn;

                string tight = InboxManager.FormatSlotCashImpact(gs, cost, out bool canPay, out float runwayAfter);
                float expectRunway = (gs.cash - cost) / burn;
                bool tightMath = canPay && burn > 0f
                                 && Mathf.Abs(runwayAfter - expectRunway) < 0.01f
                                 && runwayAfter < 1f;          // 26 万现金 ÷ 约 41 万/月 ≈ 0.64 月：必须显出「撑不到一个月」
                                                               // （EffectiveMonthlyBurn 含 CEO 薪酬等，故比 monthlyBurnRate=40 略高；
                                                               //   断言一律用算出来的 burn，不写死 40，否则加一项开支就翻红）
                bool tightText = tight.Contains("个月") && tight.Contains("跑道")
                                 && tight.Contains("26")               // 扣款后现金 26 万
                                 && tight.Contains("万/月")            // 引用了净烧钱
                                 && !tight.Contains("不足以支付");

                // ② 现金充足：8 个月现金、报价 100 万 → 剩 7 个月左右，且不得出现「不足以支付」
                gs.cash = 800f;
                string ok = InboxManager.FormatSlotCashImpact(gs, 100f, out bool okPay, out float okRunway);
                bool okMath = okPay && Mathf.Abs(okRunway - (800f - 100f) / burn) < 0.01f;
                bool okText = ok.Contains("个月") && !ok.Contains("不足以支付");

                // ③ 现金不足：326 万现金买 400 万的槽 → 必须说清差多少 + 给出免费替代
                gs.cash = 326f;
                string shortfall = InboxManager.FormatSlotCashImpact(gs, 400f, out bool shortPay, out float shortRunway);
                bool shortOk = !shortPay && shortRunway == 0f
                               && shortfall.Contains("不足以支付")
                               && shortfall.Contains("74")            // 400 − 326 = 74
                               && shortfall.Contains("协调池");

                // ④⑤ 真实信件 + 风险行正反两面：**按最终实际时刻与报价**构造。
                //   探针只为问出「本机场此刻真的会成交在哪一刻、报价多少」，不影响任何生产规则。
                string probedClock = FirstProposableClock(gs, "TNA");
                bool probed = !string.IsNullOrEmpty(probedClock);
                string finalClock = "";
                float quote = 0f;
                bool posPre = false, posBody = false, posRisk = false, posPainted = false;
                bool negPre = false, negBody = false, negHidden = false;
                string probeNote = "", posNote = "", negNote = "";
                string body1 = "", body2 = "", painted1 = "";

                if (probed)
                {
                    gs.inbox.Clear();
                    gs.cash = 10000f;                             // 探针只看时刻与报价，不看钱
                    InboxManager.QueueSlotDecision(gs, "TNA", probedClock, "m_k19_probe", out probeNote);
                    var probe = FindMail(gs, "m_k19_probe");
                    finalClock = probe != null ? (probe.slotClock ?? "") : "";
                    if (!string.IsNullOrEmpty(finalClock) && ScheduleSlots.ParseClock(finalClock) >= 0)
                        quote = StrategyEconomics.SlotValueWanAt("TNA", finalClock);
                }

                if (probed && quote > 0f)
                {
                    // 正例：报价 + 2 个月净烧钱 → 付得起，且付完只剩约 2 个月跑道（< 3 月危机线）。
                    gs.inbox.Clear();
                    gs.cash = quote + burn * 2f;
                    float posRunway = (gs.cash - quote) / burn;
                    bool q1 = InboxManager.QueueSlotDecision(gs, "TNA", finalClock, "m_k19_pos", out posNote);
                    var m1 = FindMail(gs, "m_k19_pos");
                    body1 = m1 != null ? (m1.body ?? "") : "";
                    painted1 = m1 != null
                        ? TermNavMap.Decorate(InboxLetter.FrameIfNeeded(gs, m1, body1)) : "";
                    // **先断言前提**：现金买得起、扣款后跑道确实 ≈ 2 个月且 < 3 月。
                    // 不这样钉的话，「正例偶然落在 3 月边界附近」也会让断言失去意义。
                    posPre = q1 && m1 != null && gs.cash >= quote
                             && Mathf.Abs(posRunway - 2f) < 0.01f && posRunway < 3f;
                    posBody = body1.Contains("现金") && body1.Contains("个月") && body1.Contains("跑道");
                    posRisk = body1.Contains("风险") && body1.Contains("危机线");
                    // 渲染层：正文里写了不等于玩家看得见 —— 走一遍真链路
                    //（正文 → InboxLetter.FrameIfNeeded → TermNavMap.Decorate）。
                    posPainted = painted1.Contains("风险") && painted1.Contains("危机线");

                    // 反例：报价 + 10 个月净烧钱 → 付完约 10 个月，**不得**出现风险行。
                    gs.inbox.Clear();
                    gs.cash = quote + burn * 10f;
                    float negRunway = (gs.cash - quote) / burn;
                    bool q2 = InboxManager.QueueSlotDecision(gs, "TNA", finalClock, "m_k19_neg", out negNote);
                    var m2 = FindMail(gs, "m_k19_neg");
                    body2 = m2 != null ? (m2.body ?? "") : "";
                    negPre = q2 && m2 != null
                             && Mathf.Abs(negRunway - 10f) < 0.01f && negRunway >= 3f;
                    negBody = body2.Contains("个月") && body2.Contains("跑道");
                    negHidden = body2.Length > 0 && !body2.Contains("风险");
                }

                bool all = tightMath && tightText && okMath && okText && shortOk
                           && posPre && posBody && posRisk && posPainted
                           && negPre && negBody && negHidden;

                var sb = new StringBuilder();
                sb.Append("K19 时刻槽提案现金跑道 → ")
                  .Append("紧口径=").Append(tightMath)
                  .Append(" 紧文案=").Append(tightText)
                  .Append(" 足额=").Append(okMath && okText)
                  .Append(" 差额=").Append(shortOk)
                  .Append(" 前提成立=").Append(posPre && negPre)
                  .Append(" 信件正文=").Append(posBody)
                  .Append(" 风险行=").Append(posRisk)
                  .Append(" 风险行渲染后仍可见=").Append(posPainted)
                  .Append(" 风险行不滥发=").Append(negBody && negHidden)
                  .Append("　").Append(all ? "✓" : "✗")
                  .Append($"\n　烧钱={burn:F2} 万/月　纯函数 {cashTight:F0}−{cost:F0} 剩 {runwayAfter:F2} 月（期望 {expectRunway:F2}）");
                sb.Append($"\n　真实信件：请求={probedClock ?? "<无可用刻>"} 最终时刻={finalClock} 报价={quote:F1} 万"
                          + $" 正例现金={quote + burn * 2f:F1}（跑道 2.00 月）"
                          + $" 反例现金={quote + burn * 10f:F1}（跑道 10.00 月）");
                if (!probed)
                    sb.Append($"\n　本机场此刻没有任何可递交的刻（探针={One(probeNote)}）—— 场景前提缺失，判失败");
                else if (quote <= 0f)
                    sb.Append($"\n　探针未能拿到最终时刻（{One(probeNote)}）—— 场景前提缺失，判失败");
                if (probed && !posPre) sb.Append($"\n　正例前提不成立：提交结果=「{One(posNote)}」");
                if (probed && !posRisk) sb.Append($"\n　跑道 2 月的信件里没有「风险 … 危机线」行；正文\n{body1}");
                if (probed && !negHidden) sb.Append($"\n　跑道 10 月的信件里多出了「风险」行（应只在 <3 月时出现）；正文\n{body2}");
                Debug.Log(sb.ToString());
                return all;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>诊断文本单行化，避免一次失败的正文把日志撑成几十行。</summary>
        static string One(string s, int max = 160)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        /// <summary>
        /// 本机场此刻**真的能递交提案**、且需求最旺的第一刻：该刻有容量、有剩量，且所在段池未满。
        /// 与 `InboxManager.QueueSlotDecision` 的三条前置判据同源 —— 用它挑刻，
        /// 才不会挑到一个「提案必然被改刻或被驳回」的时刻，把场景题变成运气题。
        /// 之所以取「需求系数最高」而不是扫描到的第一个刻：K19 记的事故是**午间黄金时刻**
        /// 的报价，挑最旺的刻才与事故同形；否则会挑到凌晨 04:30 这种冷门刻，
        /// 现金条件虽然也对，但远离了要防的那种「CEO 看不见后果就签字」的场景。
        /// </summary>
        static string FirstProposableClock(GameState gs, string apt)
        {
            string best = null;
            float bestWeight = -1f;
            int step = ScheduleSlots.SlotGridStepMinutes;
            for (int m = 0; m < 1440; m += step)
            {
                string clk = ScheduleSlots.FormatClock(m);
                if (RoutePermitLogic.QuarterCapacityAt(apt, clk) <= 0) continue;
                if (RoutePermitLogic.SlotsAvailableAt(gs, apt, clk) <= 0) continue;
                if (RoutePermitLogic.SlotPoolFull(gs, apt, ScheduleSlots.BandOfClock(clk))) continue;
                float w = StrategyEconomics.HourDemandCoef[m / 60];
                if (w > bestWeight) { bestWeight = w; best = clk; }
            }
            return best;
        }

        static InboxMail FindMail(GameState gs, string id)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && string.Equals(gs.inbox[i].id, id, System.StringComparison.Ordinal))
                    return gs.inbox[i];
            return null;
        }
    }
}
