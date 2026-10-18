using System;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// Phase 2 Week 7 · MKT 市场销售部数值引擎（纯 C#，Unity 与 tools/balance 共用）。
    /// 覆盖：需求价格弹性 / 票价-客座率 / RASK-CASK / 季节与节日曲线 / 旺季策略 ROI / 辅助收入 / FFP 里程负债。
    /// 单位约定：货币「万美元」；票价与费用「美元」；百分比按 0~1。
    /// </summary>
    public static class MktEconomics
    {
        // —— 需求价格弹性 ——
        public const float LeisureElasticity = -1.5f;
        public const float BusinessElasticity = -0.8f;

        public static float ElasticityFor(string passengerType) =>
            string.Equals(passengerType, "business", StringComparison.OrdinalIgnoreCase)
                ? BusinessElasticity
                : LeisureElasticity;

        /// <summary>调价后客座率：baseLoad × (1 + ε × (mult−1))，软钳到 [0.4, 0.98]。</summary>
        public static float LoadFactorAfterPrice(float baseLoad, float priceMult, string passengerType)
        {
            float e = ElasticityFor(passengerType);
            float load = baseLoad * (1f + e * (priceMult - 1f));
            return Clamp(load, 0.4f, 0.98f);
        }

        /// <summary>调价后单班日收入（万美元）：座位 × 客座率 × 平均票价（美元）→ 万。</summary>
        public static float DailyRevenueAfterPrice(int seats, float baseLoad, float baseFareUsd,
            float priceMult, string passengerType)
        {
            float load = LoadFactorAfterPrice(baseLoad, priceMult, passengerType);
            float pax = seats * load;
            return pax * (baseFareUsd * priceMult) / 10000f;
        }

        /// <summary>给定票价乘数下的单班日利润（万美元），可变成本近似不变。</summary>
        public static float ProfitAfterPrice(int seats, float baseLoad, float baseFareUsd, float baseCostWan,
            float priceMult, string passengerType)
        {
            return DailyRevenueAfterPrice(seats, baseLoad, baseFareUsd, priceMult, passengerType) - baseCostWan;
        }

        /// <summary>最优票价乘数（一维搜索）：在 [0.7, 1.5] 内使单班日利润最大。</summary>
        public static float OptimalPriceMult(int seats, float baseLoad, float baseFareUsd, float baseCostWan,
            string passengerType)
        {
            float best = 1f;
            float bestProfit = float.NegativeInfinity;
            for (float m = 0.7f; m <= 1.5001f; m += 0.05f)
            {
                float p = ProfitAfterPrice(seats, baseLoad, baseFareUsd, baseCostWan, m, passengerType);
                if (p > bestProfit)
                {
                    bestProfit = p;
                    best = m;
                }
            }
            return Round2(best);
        }

        // —— RASK / CASK ——
        /// <summary>ASK（可用座公里）= seats × distance × 周班次。</summary>
        public static float Ask(int seats, float distanceKm, int weeklyFlights) =>
            seats * distanceKm * Math.Max(1, weeklyFlights);

        /// <summary>RASK（美元/座公里）= 周收入(万→美元) / ASK。</summary>
        public static float RaskUsd(float weeklyRevenueWan, float ask)
        {
            if (ask <= 0f) return 0f;
            return weeklyRevenueWan * 10000f / ask;
        }

        /// <summary>CASK（美元/座公里）= 周成本(万→美元) / ASK。</summary>
        public static float CaskUsd(float weeklyCostWan, float ask)
        {
            if (ask <= 0f) return 0f;
            return weeklyCostWan * 10000f / ask;
        }

        /// <summary>盈亏平衡客座率（近似）：currentLoad × cost / revenue。</summary>
        public static float BreakevenLoad(float weeklyCostWan, float weeklyRevenueWan, float loadFactor)
        {
            if (weeklyRevenueWan <= 0f) return 1f;
            return Clamp(loadFactor * weeklyCostWan / weeklyRevenueWan, 0f, 1f);
        }

        // —— 季节曲线 ——
        /// <summary>月需求系数（0.5~1.6）：春节/暑运/国庆峰，4-5月与11月谷。</summary>
        public static float MonthSeasonalCoefficient(int month)
        {
            switch (month)
            {
                case 1: return 1.25f;  // 春运
                case 2: return 1.45f;  // 春节
                case 3: return 1.05f;
                case 4: return 0.90f;
                case 5: return 0.95f;
                case 6: return 1.05f;
                case 7: return 1.35f;  // 暑运
                case 8: return 1.55f;  // 暑运峰
                case 9: return 1.10f;
                case 10: return 1.40f; // 国庆
                case 11: return 0.85f;
                case 12: return 1.05f; // 圣诞元旦
                default: return 1f;
            }
        }

        /// <summary>节日峰值系数与曲线形态（n55 需求曲线）。</summary>
        public static (string name, string shape, float peak) HolidayInfo(string holiday)
        {
            switch (holiday)
            {
                case "春节": return ("春节", "V型", 3.0f);
                case "国庆": return ("国庆", "V型", 2.6f);
                case "暑运": return ("暑运", "高原型", 2.2f);
                case "清明": return ("清明", "脉冲型", 1.6f);
                case "端午": return ("端午", "脉冲型", 1.5f);
                case "五一": return ("五一", "脉冲型", 1.9f);
                case "中秋": return ("中秋", "脉冲型", 1.7f);
                case "圣诞元旦": return ("圣诞元旦", "平缓型", 1.5f);
                default: return (holiday, "平缓型", 1.3f);
            }
        }

        /// <summary>常见节假日顺序（n55 列表）。</summary>
        public static readonly string[] Holidays =
        {
            "春节", "清明", "五一", "端午", "暑运", "中秋", "国庆", "圣诞元旦"
        };

        // —— 旺季策略 ROI ——
        /// <summary>旺季策略包的预估增量：收入增幅系数与成本增幅系数。</summary>
        public static (float revenueMult, float costMult) PeakSeasonEffect(PeakSeasonPackage pkg)
        {
            if (pkg == null) return (1f, 1f);
            float revMult = 1f;
            float costMult = 1f;
            switch (pkg.capacityChoice)
            {
                case 1: revMult += 0.10f; costMult += 0.12f; break; // 加密
                case 2: revMult += 0.18f; costMult += 0.22f; break; // 窄转宽
                case 3: revMult += 0.25f; costMult += 0.30f; break; // 湿租/加班
            }
            switch (pkg.pricingChoice)
            {
                case 1: revMult += 0.08f; break; // 早鸟折扣
                case 2: revMult += 0.06f; break; // 旺季溢价
                case 3: revMult += 0.04f; break; // 舱位开放
                case 4: revMult += 0.03f; break; // 差旅收紧
                case 5: revMult += 0.07f; break; // 打包产品
            }
            switch (pkg.serviceChoice)
            {
                case 1: revMult += 0.03f; costMult += 0.02f; break; // 旅游航线
                case 2: revMult += 0.04f; costMult += 0.04f; break; // 节日体验
                case 3: revMult += 0.02f; costMult += 0.03f; break; // 特殊旅客
            }
            if (pkg.extendShoulder)
            {
                revMult += 0.05f;
                costMult += 0.03f;
            }
            return (revMult, costMult);
        }

        /// <summary>旺季策略包 ROI = (增量收入 − 增量成本) / 增量成本。</summary>
        public static float PeakSeasonRoi(float baseRevenue, float baseCost, PeakSeasonPackage pkg)
        {
            var e = PeakSeasonEffect(pkg);
            float incRev = baseRevenue * (e.revenueMult - 1f);
            float incCost = baseCost * (e.costMult - 1f);
            if (incCost <= 0f) return incRev > 0f ? 999f : 0f;
            return (incRev - incCost) / incCost;
        }

        /// <summary>旺季策略包预测影响（n59 弹窗）。</summary>
        public static (float profitDelta, float loadDelta, float costDelta) PeakSeasonImpact(
            float baseRevenue, float baseCost, float baseLoad, PeakSeasonPackage pkg)
        {
            var e = PeakSeasonEffect(pkg);
            float incRev = baseRevenue * (e.revenueMult - 1f);
            float incCost = baseCost * (e.costMult - 1f);
            float loadDelta = baseLoad * (e.revenueMult - 1f) * 0.6f;
            return (incRev - incCost, loadDelta, incCost);
        }

        // —— 辅助收入 ——
        /// <summary>单客辅助收入（美元）：行李 + 选座 + 机上销售 + 退改分摊。</summary>
        public static float AuxRevenuePerPaxUsd(AncillarySettings s) =>
            s == null ? 0f : s.baggageFeeUsd + s.seatFeeUsd + s.onboardSalesUsd + s.changeFeeUsd * 0.2f;

        /// <summary>航线周辅助收入（万美元）。</summary>
        public static float WeeklyAuxRevenueWan(int seats, float loadFactor, int weeklyFlights, AncillarySettings s)
        {
            float paxPerWeek = seats * loadFactor * Math.Max(1, weeklyFlights);
            return paxPerWeek * AuxRevenuePerPaxUsd(s) / 10000f;
        }

        // —— FFP ——
        /// <summary>里程负债估算（万美元）：活跃会员 × 年均累计里程 × 单里程成本。</summary>
        public static float FfpLiabilityWan(FfpSettings f, float costPerMileUsd = 0.008f)
        {
            if (f == null) return 0f;
            float activeMembers = f.members * f.activeRate;
            const float milesPerMember = 12000f;
            return activeMembers * milesPerMember * costPerMileUsd / 10000f;
        }

        /// <summary>FFP 品牌净值联动：会员增长 → 品牌认知提升。</summary>
        public static float FfpBrandLift(FfpSettings f) =>
            f == null ? 0f : Clamp(f.members / 100000f * 2f, 0f, 5f);

        // —— 高铁替代 ——
        /// <summary>≤1000km 高铁替代威胁等级（n50）。</summary>
        public static string HighSpeedRailThreat(float distanceKm)
        {
            if (distanceKm <= 500f) return "高";
            if (distanceKm <= 1000f) return "中";
            return "低";
        }

        // —— 竞对票价排名 ——
        /// <summary>我司票价相对竞对：高 / 中 / 低。</summary>
        public static string FarePositionLabel(float myFare, float competitorFare)
        {
            if (competitorFare <= 0f) return "—";
            float ratio = myFare / competitorFare;
            if (ratio >= 1.08f) return "高";
            if (ratio <= 0.92f) return "低";
            return "中";
        }

        static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        static float Round2(float v) => (float)Math.Round(v, 2);
    }
}
