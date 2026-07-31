using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航线盈利计算器（RPM 简化模型）。
    /// 评分以日利润与利润率为核心，长航线有利润时不再被轻易判红。
    /// fuelPricePerTon 单位：美元/吨（内部换算为万美元）。
    /// </summary>
    public static class RPMCalculator
    {
        public struct RPMInput
        {
            public float distance;          // 航程（公里）
            public float fuelPricePerTon;   // 当前油价（美元/吨）
            public float competitorSupply;  // 竞对运力占比 (0.0~1.0)
            public float demandMultiplier;  // 季节系数 (0.8~2.5)
            public float brandEquity;       // 品牌净值 (0~100)
        }

        public struct RPMOutput
        {
            public float dailyRevenue;      // 日收入（万美元）
            public float dailyCost;         // 日成本（万美元）
            public float dailyProfit;       // 日利润（万美元）
            public int score;               // 综合评分 (0~100)
            public string rating;           // "绿", "黄", "红"
        }

        public static RPMOutput Calculate(RPMInput input)
        {
            // ========== 1. 基础参数（行业基准） ==========
            const float baseRevenuePerKm = 0.035f;        // 万美元 / 座位 / 千公里
            const float fuelBurnPerThousandKm = 2.8f;     // 吨 / 千公里
            const float fixedCostPerFlight = 1.2f;        // 万美元 / 班次

            // ========== 2. 假设机型（B787-9 典型值） ==========
            const int seats = 290;
            const float loadFactor = 0.82f;

            float competitor = Clamp(input.competitorSupply, 0f, 1f);
            float season = Clamp(input.demandMultiplier, 0.5f, 3f);
            float brand = Clamp(input.brandEquity, 0f, 100f);
            float fuelPriceUsdPerTon = Math.Max(0f, input.fuelPricePerTon);

            // ========== 3. 距离（千公里） ==========
            float distanceInThousandKm = Math.Max(input.distance, 0f) / 1000f;
            if (distanceInThousandKm < 0.1f)
                distanceInThousandKm = 0.1f;

            // ========== 4. 收入 ==========
            float baseRevenue = baseRevenuePerKm * distanceInThousandKm * seats * loadFactor;
            float brandPremium = 1f + (brand / 100f) * 0.10f;
            float competitorDiscount = 1f - competitor * 0.30f;
            float dailyRevenue = baseRevenue * season * brandPremium * competitorDiscount;

            // ========== 5. 成本 ==========
            // 油价按美元/吨传入，换算为万美元：÷10000
            float fuelCost = fuelBurnPerThousandKm * distanceInThousandKm * (fuelPriceUsdPerTon / 10000f);
            float landingFee = fixedCostPerFlight * (1f + distanceInThousandKm * 0.08f);
            float crewAndMaint = 0.8f + distanceInThousandKm * 0.20f;
            float dailyCost = fuelCost + landingFee + crewAndMaint;

            // ========== 6. 日利润 ==========
            float dailyProfit = dailyRevenue - dailyCost;

            // ========== 7. 综合评分：利润为核心 ==========
            // 7.1 利润评分：日利润 0 = 50 分；±10 万美元调整 ±25 分
            float profitScore = Clamp(50f + (dailyProfit / 10f) * 25f, 0f, 100f);

            // 7.2 利润率评分：0% = 50 分；±20% 调整 ±25 分
            float margin = dailyRevenue > 0.01f ? dailyProfit / dailyRevenue : 0f;
            float marginScore = Clamp(50f + (margin / 0.20f) * 25f, 0f, 100f);

            // 7.3 长航线战略加分（最多 10）
            float distanceBonus = Math.Min(distanceInThousandKm / 20f, 10f);

            // 7.4 竞对惩罚（最多 15）
            float competitorPenalty = competitor * 15f;

            // 7.5 加权：利润 50% + 利润率 30% + 距离 - 竞对
            float rawScore = profitScore * 0.50f + marginScore * 0.30f + distanceBonus - competitorPenalty;
            int finalScore = (int)Clamp(rawScore, 0f, 100f);

            return new RPMOutput
            {
                dailyRevenue = Round2(dailyRevenue),
                dailyCost = Round2(dailyCost),
                dailyProfit = Round2(dailyProfit),
                score = finalScore,
                rating = ToRating(finalScore)
            };
        }

        public static string ToRating(int score)
        {
            if (score >= 70) return "绿";
            if (score >= 40) return "黄";
            return "红";
        }

        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static float Round2(float v) => (float)Math.Round(v, 2);
    }
}
