using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
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
    /// 收入/支出/盈利/航线/机队，**没有现金字段**。按实测应为 1434 万。
    ///
    /// 本回归钉住五件事：
    /// ① 数字口径：扣款后现金 / 跑道必须等于 `(cash − cost) / EffectiveMonthlyBurn`；
    /// ② 文案必须出现「个月」与扣款后现金流；
    /// ③ 现金不足以支付时要明说「不足以支付」并指向免费的协调池申请（不是沉默扣款失败）；
    /// ④ **真实提案正文**必须带上这一行 —— 只测纯函数会被「函数写好了但没接进信件」骗过；
    /// ⑤ **风险行正反两面**：跑道 < 3 月必须出现，跑道充足不得出现（否则「无脑追加一行」也能过）。
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

                // ④ 真实信件：正文里必须有「现金」这一行，且与纯函数同一口径
                gs.cash = 326f;
                bool queued = InboxManager.QueueSlotDecision(gs, "TNA", "12:00", "m_k19_test", out string note);
                string body = queued && gs.inbox.Count > 0 ? (gs.inbox[0].body ?? "") : "";
                bool bodyOk = queued && body.Contains("现金") && body.Contains("个月")
                              && body.Contains("跑道");

                // ⑤ 风险行：**两面都要钉**。
                //    正面：能付得起、但扣款后跑道 < 3 月 → 必须多一条「风险 … 已跌破 3 个月危机线」。
                //    反面：跑道充足 → **不得**出现风险行（只钉正面的话，一个「无脑追加一行」的实现也能过）。
                //    跑道刻意按 burn 反推（cost + burn × 10），这样断言不依赖烧钱的绝对值。
                bool riskShown = bodyOk && body.Contains("风险") && body.Contains("危机线");
                gs.inbox.Clear();
                gs.cash = cost + burn * 10f;                     // 扣款后恰剩 10 个月跑道
                bool queued2 = InboxManager.QueueSlotDecision(gs, "TNA", "12:00", "m_k19_test2", out _);
                string body2 = queued2 && gs.inbox.Count > 0 ? (gs.inbox[gs.inbox.Count - 1].body ?? "") : "";
                bool riskHidden = queued2 && body2.Contains("个月") && !body2.Contains("风险");

                bool all = tightMath && tightText && okMath && okText && shortOk && bodyOk
                           && riskShown && riskHidden;

                var sb = new StringBuilder();
                sb.Append("K19 时刻槽提案现金跑道 → ")
                  .Append("紧口径=").Append(tightMath)
                  .Append(" 紧文案=").Append(tightText)
                  .Append(" 足额=").Append(okMath && okText)
                  .Append(" 差额=").Append(shortOk)
                  .Append(" 信件正文=").Append(bodyOk)
                  .Append(" 风险行=").Append(riskShown)
                  .Append(" 风险行不滥发=").Append(riskHidden)
                  .Append("　").Append(all ? "✓" : "✗")
                  .Append($"\n　烧钱={burn:F0} 万/月　{cashTight:F0}→{cost:F0} 剩 {runwayAfter:F2} 月（期望 {expectRunway:F2}）");
                if (!bodyOk)
                    sb.Append($"\n　提交结果=「{note}」　信件正文\n{body}");
                if (!riskShown) sb.Append("\n　跑道 <3 月的信件里没有「风险 … 危机线」行");
                if (!riskHidden) sb.Append("\n　跑道 10 月的信件里多出了「风险」行（应只在 <3 月时出现）");
                Debug.Log(sb.ToString());
                return all;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
