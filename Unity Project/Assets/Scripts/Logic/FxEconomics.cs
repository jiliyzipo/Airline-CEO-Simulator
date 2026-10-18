using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 汇率与汇兑损益（Phase 20）。
    /// <para>
    /// 口径说明：本作账本以「万美元」记账，收入与成本同为美元，因此**不存在**「把美元支出按汇率换算一遍」这回事
    /// （那样换过去再换回来恒等，不会产生任何影响）。要让汇率真正影响公司，只有一条符合现实的路径：
    /// 按**美元净头寸重估**产生汇兑损益 —— 这正是国内航司财报里波动最大的那一行。
    /// </para>
    /// <para>
    /// 净美元头寸（万美元）= 美元收入 − 美元负债：
    /// ① 美元收入：以美元结算的国际航线季度净收入（客货合计，用航线日利润汇总近似）；
    /// ② 美元负债：购机应付款 <see cref="GameState.pdpPayableWan"/> + 融资余额（<see cref="LoanManager.TotalDebt"/>）
    /// + 一年经营租赁承诺（按当前月租金年化估算）。
    /// 国内航司通常负债远大于国际收入，故净头寸为负：**人民币贬值（汇率上行）即汇兑损失**。
    /// </para>
    /// <para>
    /// 结算顺序刻意与油价对冲对齐：先按**漂移前**汇率结算本季汇兑损益，再让汇率漂移，
    /// 保证「结算用的价」与「漂移后的价」不互相污染（同 <c>HedgeManager</c> 的 quarterEndPrice 口径）。
    /// </para>
    /// </summary>
    public static class FxEconomics
    {
        /// <summary>开局汇率（1 美元 = N 元）。</summary>
        public const float DefaultUsdCny = 7.10f;
        public const float MinUsdCny = 5.50f;
        public const float MaxUsdCny = 9.50f;

        /// <summary>对冲成本：每季约 0.25% 的点数（年化约 1%），按被对冲的头寸计费。</summary>
        const float HedgeCostPerQuarter = 0.0025f;
        /// <summary>季度汇率噪声上限（非财年首季）：±1.0%。</summary>
        const float QuarterlyNoise = 0.010f;
        /// <summary>财年首季：与宏观阶段挂钩的方向性漂移 + 更大的噪声。</summary>
        const float YearOpenNoise = 0.015f;
        /// <summary>历史保留季数。</summary>
        const int HistoryMax = 12;

        /// <summary>当前汇率；未初始化/损坏（≤0）时回落到默认值，越界则裁到区间内。</summary>
        public static float RateOf(GameState gs)
        {
            if (gs == null || gs.usdCny <= 0f) return DefaultUsdCny;
            return Mathf.Clamp(gs.usdCny, MinUsdCny, MaxUsdCny);
        }

        /// <summary>美元负债（万美元）：购机应付款 + 融资余额 + 一年租赁承诺（估算）。</summary>
        public static float UsdDebtWan(GameState gs)
        {
            if (gs == null) return 0f;
            float pdp = Mathf.Max(0f, gs.pdpPayableWan);
            float loans = LoanManager.TotalDebt(gs);
            float leaseCommitment = Mathf.Max(0f, gs.FleetMonthlyLease) * 12f;
            return pdp + loans + leaseCommitment;
        }

        /// <summary>美元收入（万美元/季）：以美元结算的国际航线净收入。</summary>
        public static float UsdIncomeWan(GameState gs)
        {
            if (gs?.activeRoutes == null) return 0f;
            float daily = 0f;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                if (!RoutePermitLogic.IsInternational(r.origin, r.destination)) continue;
                // 停飞（无机可派）的国际线不产生美元收入，也就不该拿它去重估汇兑损益：
                // 否则会出现「一班没飞、却按该线收入吃汇率波动」的幽灵敞口。
                if (FlightGroundLogic.IsPersistent(FlightGroundLogic.RouteOf(gs, r))) continue;
                daily += r.dailyProfit;
            }
            // 航线日利润按 30 天 × 3 个月折算到季度
            return daily * 30f * 3f;
        }

        /// <summary>
        /// 净美元头寸（万美元）：正 = 净多头（人民币升值受损），负 = 净空头（人民币贬值受损）。
        /// 国内航司通常是负值。
        /// </summary>
        public static float NetUsdExposureWan(GameState gs)
        {
            return UsdIncomeWan(gs) - UsdDebtWan(gs);
        }

        /// <summary>被 <see cref="GameState.fxHedgeRatio"/> 覆盖后的敞口（万美元）。</summary>
        public static float HedgedExposureWan(GameState gs)
        {
            float hedge = Mathf.Clamp(gs != null ? gs.fxHedgeRatio : 0f, 0f, 0.8f);
            return NetUsdExposureWan(gs) * (1f - hedge);
        }

        /// <summary>
        /// 本季预计汇兑损益（万美元，正 = 收益）。按给定汇率变动率估算，供财务面板预览。
        /// </summary>
        public static float EstimatePnlWan(GameState gs, float rateChangeRatio)
        {
            float hedge = Mathf.Clamp(gs != null ? gs.fxHedgeRatio : 0f, 0f, 0.8f);
            float exposure = NetUsdExposureWan(gs);
            float pnl = exposure * rateChangeRatio * (1f - hedge);
            float cost = Mathf.Abs(exposure) * hedge * HedgeCostPerQuarter;
            return pnl - cost;
        }

        /// <summary>
        /// 本季汇率结算：先按漂移前汇率结算汇兑损益，再漂移汇率。返回可并入季报的一句话。
        /// </summary>
        public static string SettleAndDrift(GameState gs)
        {
            if (gs == null) return "";
            float before = RateOf(gs);
            return ApplyRate(gs, NextRate(gs, before), before);
        }

        /// <summary>
        /// 按给定目标汇率结算本季汇兑损益并落账（自测用 / 将来做剧本化汇率事件也用这里）。
        /// <paramref name="rateBefore"/> 传 0 表示取当前汇率。
        /// </summary>
        public static string ApplyRate(GameState gs, float rateAfter, float rateBefore = 0f)
        {
            if (gs == null) return "";

            float before = rateBefore > 0f ? rateBefore : RateOf(gs);
            float after = rateAfter > 0f ? Mathf.Clamp(rateAfter, MinUsdCny, MaxUsdCny) : DefaultUsdCny;
            gs.usdCny = after;

            float exposure = NetUsdExposureWan(gs);
            // 正 = 人民币贬值：同一笔美元头寸折回美元后变薄（净负债方受损、净资产方受益）
            float deltaRatio = (after - before) / after;
            float hedge = Mathf.Clamp(gs.fxHedgeRatio, 0f, 0.8f);

            float pnl = exposure * deltaRatio * (1f - hedge);
            float cost = Mathf.Abs(exposure) * hedge * HedgeCostPerQuarter;
            float net = pnl - cost;

            gs.cash += net;
            gs.yearNetProfit += net;
            gs.cumulativeFxPnlWan += net;
            PushHistory(gs, after);

            string dir = deltaRatio > 0.0005f ? "人民币贬值" : deltaRatio < -0.0005f ? "人民币升值" : "汇率基本持平";
            string mood = net >= 0f ? "汇兑收益" : "汇兑损失";
            string hedgeNote = hedge > 0.001f
                ? $"已对冲 {hedge * 100f:F0}%（成本 {cost:F1} 万）"
                : "未对冲";

            return $"汇率 {before:F2}→{after:F2}（{dir} {Mathf.Abs(deltaRatio) * 100f:F1}%）：{mood} {Mathf.Abs(net):F1} 万"
                 + $"（净美元头寸 {exposure:F0} 万 · {hedgeNote}）";
        }

        /// <summary>季度汇率漂移：财年首季跟宏观阶段同向，其余季度小幅噪声。</summary>
        static float NextRate(GameState gs, float current)
        {
            float drift;
            if (gs.quarter == 1)
            {
                // 衰退期资本外流 → 人民币贬值压力；繁荣期相反
                drift = gs.economicPhase switch
                {
                    "recession" => 0.012f,
                    "boom" => -0.010f,
                    _ => 0f
                };
                drift += Random.Range(-YearOpenNoise, YearOpenNoise);
            }
            else
            {
                drift = Random.Range(-QuarterlyNoise, QuarterlyNoise);
            }
            return Mathf.Clamp(current * (1f + drift), MinUsdCny, MaxUsdCny);
        }

        static void PushHistory(GameState gs, float rate)
        {
            if (gs.usdCnyHistory == null) gs.usdCnyHistory = new System.Collections.Generic.List<float>();
            gs.usdCnyHistory.Add(rate);
            while (gs.usdCnyHistory.Count > HistoryMax)
                gs.usdCnyHistory.RemoveAt(0);
        }

        /// <summary>面板用的一行摘要，如「7.18 · 净头寸 -3,800 万 · 已对冲 40%」。</summary>
        public static string Summary(GameState gs)
        {
            if (gs == null) return "";
            float hedge = Mathf.Clamp(gs.fxHedgeRatio, 0f, 0.8f);
            return $"{RateOf(gs):F2} · 净头寸 {NetUsdExposureWan(gs):F0} 万 · 对冲 {hedge * 100f:F0}%";
        }
    }
}
