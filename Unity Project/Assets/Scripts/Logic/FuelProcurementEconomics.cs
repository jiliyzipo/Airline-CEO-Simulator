using System;
using System.Collections.Generic;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 燃油自动采购预警数值引擎（GDB 15.12.3 · P2 长线运营 · 财务细分）。
    /// 纯 C#，Unity 与 tools/balance 共用。
    /// 油价「美元/吨」；库存「天」；紧急采购溢价以比例计。
    /// 说明：游戏按季度推进，故「30 日均价」以近期价格历史（约 2~4 季）的均值近似。
    /// </summary>
    public static class FuelProcurementEconomics
    {
        /// <summary>油价低位预警：低于近期均价 ≥10% → 建议采购。</summary>
        public const float LowPriceThreshold = 0.10f;
        /// <summary>油价上涨预警：高于近期均价 ≥15% → 建议锁定。</summary>
        public const float HighPriceThreshold = 0.15f;
        /// <summary>库存不足预警：库存不足以支撑 ≤7 天 → 紧急采购。</summary>
        public const float EmergencyInventoryDays = 7f;
        /// <summary>默认最低库存安全线（天）。</summary>
        public const float DefaultSafetyDays = 7f;
        /// <summary>紧急采购溢价（20%~50%，取 35%）。</summary>
        public const float EmergencyPricePremium = 0.35f;

        public enum FuelWarningKind
        {
            None = 0,
            LowPrice = 1,     // 🟡 建议采购
            HighPrice = 2,    // 🟡 建议锁定
            LowInventory = 3, // 🔴 紧急采购
            Geopolitical = 4, // 🟡 建议对冲
            SeasonPeak = 5    // 🔵 信息提示
        }

        public struct FuelWarning
        {
            public FuelWarningKind kind;
            public string text;
        }

        /// <summary>近期均价（美元/吨）：取历史末尾最多 4 个样本。</summary>
        public static float RecentAveragePrice(IReadOnlyList<float> priceHistory)
        {
            if (priceHistory == null || priceHistory.Count == 0) return 0f;
            int start = Math.Max(0, priceHistory.Count - 4);
            float sum = 0f;
            int n = 0;
            for (int i = start; i < priceHistory.Count; i++)
            {
                sum += priceHistory[i];
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>紧急采购单价（美元/吨）= 当前价 × (1 + 溢价)。</summary>
        public static float EmergencyPrice(float currentPrice) =>
            Math.Max(0f, currentPrice) * (1f + EmergencyPricePremium);

        /// <summary>建议采购量（吨）：按未来 7 天日耗量估算，dailyConsumptionTons 为日均油耗。</summary>
        public static float SuggestedProcurementTons(float dailyConsumptionTons, float targetDays) =>
            Math.Max(0f, dailyConsumptionTons) * Math.Max(0f, targetDays);

        /// <summary>
        /// 逐项检查并返回燃油采购预警列表。
        /// </summary>
        /// <param name="currentPrice">当前区域基准油价（美元/吨）。</param>
        /// <param name="avgPrice">近期均价（美元/吨）。</param>
        /// <param name="inventoryDays">当前燃油库存可支撑天数。</param>
        /// <param name="geopoliticalRisk">产油区地缘政治紧张度 0~1。</param>
        /// <param name="seasonPeak">是否进入旺季（暑运/春节等）。</param>
        public static List<FuelWarning> CheckWarnings(
            float currentPrice, float avgPrice, float inventoryDays,
            float geopoliticalRisk, bool seasonPeak)
        {
            var list = new List<FuelWarning>(4);

            if (inventoryDays <= EmergencyInventoryDays && inventoryDays >= 0f)
            {
                list.Add(new FuelWarning
                {
                    kind = FuelWarningKind.LowInventory,
                    text = $"燃油库存仅剩 {inventoryDays:F0} 天，低于 {EmergencyInventoryDays:F0} 天安全线，需紧急采购（溢价约 {EmergencyPricePremium * 100f:F0}%）。"
                });
            }
            else if (avgPrice > 0f)
            {
                float delta = currentPrice / avgPrice - 1f;
                if (delta <= -LowPriceThreshold)
                {
                    list.Add(new FuelWarning
                    {
                        kind = FuelWarningKind.LowPrice,
                        text = $"油价低位：当前 {currentPrice:F0} 较近期均价 {avgPrice:F0} 低 {Math.Abs(delta) * 100f:F0}%，建议逢低采购锁定成本。"
                    });
                }
                else if (delta >= HighPriceThreshold)
                {
                    list.Add(new FuelWarning
                    {
                        kind = FuelWarningKind.HighPrice,
                        text = $"油价上涨：当前 {currentPrice:F0} 较近期均价 {avgPrice:F0} 高 {delta * 100f:F0}%，建议锁定或增加对冲。"
                    });
                }
            }

            if (geopoliticalRisk >= 0.5f)
            {
                list.Add(new FuelWarning
                {
                    kind = FuelWarningKind.Geopolitical,
                    text = "地缘政治风险上升，产油区供应不确定性增大，建议提高燃油对冲比例。"
                });
            }

            if (seasonPeak)
            {
                list.Add(new FuelWarning
                {
                    kind = FuelWarningKind.SeasonPeak,
                    text = "旺季临近，建议提前备足燃油库存以应对需求高峰。"
                });
            }

            return list;
        }
    }
}
