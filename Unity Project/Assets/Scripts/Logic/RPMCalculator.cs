using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航线盈利计算器（Phase 1.3 校准版）。
    /// 收入按 RPK×Yield；成本拆油费/机场费/机组/可变维修。
    /// fuelPricePerTon：美元/吨；输出货币：万美元。
    /// </summary>
    public static class RPMCalculator
    {
        public struct RPMInput
        {
            public float distance;          // 航程（公里）
            public float fuelPricePerTon;   // 当前油价（美元/吨）
            public float competitorSupply;  // 竞对运力占比 (0.0~1.0)
            public float demandMultiplier;  // 季节/宏观系数
            public float brandEquity;       // 品牌净值 (0~100)
            /// <summary>可选：座位数；≤0 则按机型默认。</summary>
            public int seats;
            /// <summary>可选：客座率；≤0 用默认。</summary>
            public float loadFactor;
            /// <summary>可选：机型，影响油耗/默认座位。</summary>
            public string aircraftType;
            /// <summary>leisure / business，影响舱位结构。</summary>
            public string passengerType;
            /// <summary>疲劳 0~1，抬机组成本。</summary>
            public float fatigue01;
            /// <summary>收益乘数（低成本票价折价等）；≤0 视为 1.0（不变）。</summary>
            public float yieldMultiplier;
            /// <summary>
            /// 可选：本线**市场需求**单班上限（人次/班，单向）。≤0 = 不设限（沿用旧口径：收入只受座位数约束）。
            ///
            /// 旧的收入口径是纯供给驱动的（客座率 × 座位数），没有任何需求封顶 —— 于是「座位越多收入越高」，
            /// 大炮打蚊子不亏钱，「合适的飞机」在模型里无从体现。传入本值后，`客座率 × 座位数` 与
            /// 「市场能给的人次」取小：座位装不下则溢出（少拉少赚），座位多于需求则封顶（多出的座卖不满）。
            /// 定价：<see cref="AirlineCEO.Logic.CapacityEconomics.MarketPaxPerFlight"/>。
            /// </summary>
            public float demandPaxPerFlight;
        }

        public struct RPMOutput
        {
            public float dailyRevenue;      // 万美元/日（按 1 班/日基准）
            public float dailyCost;
            public float dailyProfit;
            public float fuelCostWan;
            public float airportCostWan;
            public float crewCostWan;
            public float maintVarWan;
            public int score;
            public string rating;
        }

        public static RPMOutput Calculate(RPMInput input)
        {
            string acType = string.IsNullOrEmpty(input.aircraftType) ? "A320neo" : input.aircraftType;
            int seats = input.seats > 0 ? input.seats : EconomyBalanceParams.DefaultSeatsFor(acType);
            float loadFactor = input.loadFactor > 0.05f
                ? Clamp(input.loadFactor, 0.4f, 0.98f)
                : EconomyBalanceParams.DefaultLoadFactor;

            float competitor = Clamp(input.competitorSupply, 0f, 1f);
            float season = Clamp(input.demandMultiplier, 0.5f, 3f);
            float brand = Clamp(input.brandEquity, 0f, 100f);
            float fuelPriceUsdPerTon = Math.Max(0f, input.fuelPricePerTon);
            float fatigue = Clamp(input.fatigue01, 0f, 1f);

            float distanceKm = Math.Max(input.distance, 100f);
            float distanceInThousandKm = distanceKm / 1000f;
            float blockHours = Math.Max(0.75f, distanceKm / EconomyBalanceParams.CruiseSpeedKmh);

            // —— 舱位结构 ——
            bool businessLean = string.Equals(input.passengerType, "business", StringComparison.OrdinalIgnoreCase);
            float bizShare = businessLean
                ? Math.Min(0.28f, EconomyBalanceParams.DefaultBusinessSeatShare + 0.10f)
                : EconomyBalanceParams.DefaultBusinessSeatShare;
            float pax = seats * loadFactor * season;
            // 需求封顶（≤0 表示不设限，旧口径）：市场能给多少就只能卖多少。
            // 与下面「需求过热时客座率软顶」是两条不同的约束 —— 那条防的是模型自己的季节乘数放大，
            // 这条防的是**飞机比市场大**（座位多于需求，多出来的座位是纯浪费）。
            if (input.demandPaxPerFlight > 0f && pax > input.demandPaxPerFlight)
                pax = input.demandPaxPerFlight;
            // 需求过热时客座率软顶，避免无限放大收入
            pax = Math.Min(pax, seats * 0.95f);
            float bizPax = pax * bizShare;
            float ecoPax = pax - bizPax;

            // —— 收入：RPK × Yield（美元）→ 万美元 ——
            float rpkBiz = bizPax * distanceKm;
            float rpkEco = ecoPax * distanceKm;
            float revenueUsd =
                rpkEco * EconomyBalanceParams.YieldEconomyUsdPerRpk +
                rpkBiz * EconomyBalanceParams.YieldBusinessUsdPerRpk;
            float brandPremium = 1f + (brand / 100f) * EconomyBalanceParams.BrandPremiumMax;
            float competitorDiscount = 1f - competitor * EconomyBalanceParams.CompetitorDiscountMax;
            float yieldMult = input.yieldMultiplier > 0f ? input.yieldMultiplier : 1f;
            float dailyRevenue = (revenueUsd / 10000f) * yieldMult * brandPremium * competitorDiscount;

            // —— 成本 ——
            float burn = EconomyBalanceParams.FuelBurnPerThousandKm(acType);
            float fuelTons = burn * distanceInThousandKm;
            float fuelCost = fuelTons * (fuelPriceUsdPerTon / 10000f);

            float airport =
                EconomyBalanceParams.AirportFeeBaseWan +
                EconomyBalanceParams.AirportFeePerThousandKmWan * distanceInThousandKm +
                EconomyBalanceParams.AirportAncillaryWan;

            float crew =
                EconomyBalanceParams.CrewCostPerBlockHourWan * blockHours *
                (1f + fatigue * EconomyBalanceParams.FatigueCrewCostMax);

            float maintVar = EconomyBalanceParams.MaintPerFlightHourWan * blockHours;

            float dailyCost = fuelCost + airport + crew + maintVar;
            float dailyProfit = dailyRevenue - dailyCost;

            // —— 评分：按校准后利润量级（日利润 ±2 万美元为主要区间）——
            float profitScore = Clamp(50f + (dailyProfit / 2f) * 25f, 0f, 100f);
            float margin = dailyRevenue > 0.01f ? dailyProfit / dailyRevenue : 0f;
            float marginScore = Clamp(50f + (margin / 0.15f) * 25f, 0f, 100f);
            float distanceBonus = Math.Min(distanceInThousandKm / 25f, 8f);
            float competitorPenalty = competitor * 12f;
            float rawScore = profitScore * 0.50f + marginScore * 0.30f + distanceBonus - competitorPenalty;
            int finalScore = (int)Clamp(rawScore, 0f, 100f);

            return new RPMOutput
            {
                dailyRevenue = Round2(dailyRevenue),
                dailyCost = Round2(dailyCost),
                dailyProfit = Round2(dailyProfit),
                fuelCostWan = Round2(fuelCost),
                airportCostWan = Round2(airport),
                crewCostWan = Round2(crew),
                maintVarWan = Round2(maintVar),
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
