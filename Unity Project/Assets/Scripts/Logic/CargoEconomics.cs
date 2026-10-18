using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 货运业务独立核算（GDB 14.6）。腹舱为边际收益（收入−装卸成本），
    /// 纯货机为独立成本中心（折旧 + 操作 + 保险），并产出三项货运 KPI。
    /// 数值单位：货币一律「万美元」；腹舱单价以「美元/公斤」呈现换算参考。
    /// 换算约定：1 美元 = 7 元（GDB 原表以「元/公斤」「元/吨公里」计）。
    /// TODO: 全部单价/容量/寿命参数待进 tools/balance 校验。
    /// </summary>
    public static class CargoEconomics
    {
        // ———— 腹舱（每班边际收益） ————
        /// <summary>腹舱收入：GDB 8~15 元/公斤 → 1.14~2.14 美元/公斤，取 1.6 美元/公斤。</summary>
        public const float BellyRevenuePerKgUsd = 1.6f;
        /// <summary>腹舱装卸成本：GDB 1.5 元/公斤 → 0.21 美元/公斤。</summary>
        public const float BellyHandlingCostPerKgUsd = 0.21f;

        /// <summary>窄体机腹舱容量（吨）。</summary>
        public const float NarrowbodyBellyTons = 4f;
        /// <summary>宽体机腹舱容量（吨）。</summary>
        public const float WidebodyBellyTons = 15f;
        /// <summary>腹舱默认利用率 0~1。</summary>
        public const float BellyUtilizationDefault = 0.55f;

        // ———— 纯货机（独立成本中心 · 吨公里口径） ————
        /// <summary>货运吨公里收入：GDB 1.2~2.5 元/吨公里 → 1.8 元 → 0.257 美元 → 万美元。</summary>
        public const float TkmRevenueWan = 0.0000257f;
        /// <summary>吨公里操作/装卸/保险成本（万美元）。</summary>
        public const float TkmOpsWan = 0.000006f;
        /// <summary>货机折旧寿命（天，≈20 年）。</summary>
        public const float FreighterUsefulLifeDays = 7300f;
        /// <summary>货机巡航日里程（km，≈12 小时 × 780 km/h）。</summary>
        public const float FreighterCruiseKmPerDay = 9360f;
        /// <summary>货机日利用率目标（小时/天）。</summary>
        public const float FreighterDailyUtilizationTarget = 12f;

        /// <summary>每吨腹舱收入（万美元/吨）= 收入单价 × 1000 kg / 10000。</summary>
        public static float BellyRevenuePerTonWan => BellyRevenuePerKgUsd * 1000f / 10000f;
        /// <summary>每吨腹舱装卸成本（万美元/吨）。</summary>
        public static float BellyHandlingPerTonWan => BellyHandlingCostPerKgUsd * 1000f / 10000f;
        /// <summary>每吨腹舱净边际（万美元/吨）= 收入 − 装卸成本。</summary>
        public static float BellyNetPerTonWan => BellyRevenuePerTonWan - BellyHandlingPerTonWan;

        /// <summary>按机型座位数估算腹舱容量（吨）：≤200 座窄体，>200 座宽体。</summary>
        public static float BellyCapacityTons(int maxPassengers)
        {
            return maxPassengers > 200 ? WidebodyBellyTons : NarrowbodyBellyTons;
        }

        /// <summary>
        /// 某班腹舱日净收益（万美元/日）：
        /// 容量 × 利用率 × 净边际 × (周班数 / 7)。
        /// </summary>
        public static float BellyDailyNetWan(float capacityTons, float utilization, int weeklyFlights)
        {
            float util = Math.Clamp(utilization, 0f, 1f);
            int weekly = Math.Max(1, weeklyFlights);
            return capacityTons * util * BellyNetPerTonWan * (weekly / 7f);
        }

        /// <summary>某班腹舱日收入（万美元/日，KPI 用）。</summary>
        public static float BellyDailyRevenueWan(float capacityTons, float utilization, int weeklyFlights)
        {
            float util = Math.Clamp(utilization, 0f, 1f);
            int weekly = Math.Max(1, weeklyFlights);
            return capacityTons * util * BellyRevenuePerTonWan * (weekly / 7f);
        }

        /// <summary>某班腹舱日装卸成本（万美元/日，KPI 用）。</summary>
        public static float BellyDailyCostWan(float capacityTons, float utilization, int weeklyFlights)
        {
            float util = Math.Clamp(utilization, 0f, 1f);
            int weekly = Math.Max(1, weeklyFlights);
            return capacityTons * util * BellyHandlingPerTonWan * (weekly / 7f);
        }

        /// <summary>货机日吨公里 = 载重吨 × 巡航日里程。</summary>
        public static float FreighterDailyTonKm(float payloadTons)
        {
            return Math.Max(0f, payloadTons) * FreighterCruiseKmPerDay;
        }

        /// <summary>货机日收入（万美元）= 吨公里 × 吨公里收入。</summary>
        public static float FreighterDailyRevenueWan(float payloadTons)
        {
            return FreighterDailyTonKm(payloadTons) * TkmRevenueWan;
        }

        /// <summary>货机日成本（万美元）= 吨公里操作成本 + 折旧（购价 / 寿命）。</summary>
        public static float FreighterDailyCostWan(float payloadTons, float purchasePriceWan)
        {
            float ops = FreighterDailyTonKm(payloadTons) * TkmOpsWan;
            float dep = Math.Max(0f, purchasePriceWan) / FreighterUsefulLifeDays;
            return ops + dep;
        }

        /// <summary>货机日净收益（万美元）= 收入 − 成本。</summary>
        public static float FreighterDailyNetWan(float payloadTons, float purchasePriceWan)
        {
            return FreighterDailyRevenueWan(payloadTons) - FreighterDailyCostWan(payloadTons, purchasePriceWan);
        }

        /// <summary>货机是否盈利。</summary>
        public static bool FreighterProfitable(float payloadTons, float purchasePriceWan)
        {
            return FreighterDailyNetWan(payloadTons, purchasePriceWan) > 0f;
        }
    }
}
