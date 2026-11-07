using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// Phase 2 Week 9 · STG 战略发展部数值引擎（纯 C#，Unity 与 tools/balance 共用）。
    /// 覆盖：航权竞标评分 / 时刻槽价值 / 联盟收益 / 投资 NPV-IRR / 战略一致性 / 地缘风险。
    /// 单位约定：货币「万美元」；百分比按 0~1。
    /// </summary>
    public static class StrategyEconomics
    {
        // —— 航权竞标评分（0.4经济 + 0.25投资承诺 + 0.2政府关系 + 0.15战略契合） ——
        public static float RouteRightBidScore(float economy, float investmentPromise, float governmentRelation, float strategicFit)
        {
            float e = Clamp01(economy / 100f);
            float i = Clamp01(investmentPromise / 100f);
            float g = Clamp01(governmentRelation / 100f);
            float s = Clamp01(strategicFit / 100f);
            return (0.4f * e + 0.25f * i + 0.2f * g + 0.15f * s) * 100f;
        }

        /// <summary>
        /// 时刻槽的**小时需求系数**（下标 = 小时，0–23）。
        ///
        /// ★2026-11-05（用户裁决 B）：槽价不再按「早/中/晚/夜」四档取价，改为按**小时**取价 ——
        /// 四个需求波只是需求系数，价格随每个小时的真实旺淡起伏。形状依据：
        /// 早峰（07–08 时）最挤、晚高峰（17–19 时）需求最高、夜班（20 时以后）最淡、
        /// 波间低谷（10 / 15 时）与 00–05 时最便宜。
        ///
        /// 绝对值锚定**单槽价**（★2026-11-07 用户裁决 A 修订）：
        /// 2026-11-05 时这条曲线的绝对值是按「枢纽满池 56 槽总价 ≈ 27,300 万」反推的；
        /// 2026-11-07 容量改按「真实全天曲线 × 1/3.5」放大（枢纽日容量 56 → 约 169），
        /// 用户明确裁决：**单槽价不降** —— 机场是扩建释放配额，黄金时刻的单价仍由需求决定，
        /// 不能因为多放出槽就把时刻做成白菜价。故本表不再承担「买下整个机场总价不变」的职责，
        /// 总额随容量增长是预期行为；回归只钉「单价 = 等级 × 小时」与峰/夜形状。
        /// 现值：枢纽 08:00 = 630 万、22:00 = 300 万；干线 08:00 = 315 万。
        /// </summary>
        public static readonly float[] HourDemandCoef =
        {
            0.27f, 0.27f, 0.27f, 0.27f, 0.27f, 0.27f,   // 00–05：宵禁/无容量，仅备取值
            0.77f, 1.00f, 1.05f, 0.91f,                  // 06–09：早峰（08 时最挤）
            0.59f, 0.73f, 0.82f, 0.77f, 0.68f, 0.55f,    // 10–15：午峰（12 时）与波间低谷
            0.73f, 0.95f, 1.00f, 0.91f,                  // 16–19：晚高峰（18 时，全天最旺）
            0.64f, 0.55f, 0.50f, 0.36f                   // 20–23：夜
        };

        /// <summary>某一**小时**的槽价（万美元）。小时越旺越贵，波间低谷最便宜。</summary>
        public static float SlotValueWanAtHour(int airportTier, int hour)
        {
            float baseVal = airportTier switch { 0 => 600f, 1 => 300f, _ => 120f };
            int h = hour < 0 ? 0 : hour > 23 ? 23 : hour;
            return baseVal * HourDemandCoef[h];
        }

        /// <summary>
        /// 某一**具体时刻**的槽价（万美元）—— 单条槽的唯一正解报价。
        /// 能拿到时刻的地方（买入/交割/挂牌/申请单正文）一律用这个，不要用时段口径。
        /// </summary>
        public static float SlotValueWanAt(string airportCode, string clock)
        {
            int m = ScheduleSlots.ParseClock(clock);
            if (m >= 0) return SlotValueWanAtHour(AirlineCEO.Data.AirportCatalog.TierOfCode(airportCode), m / 60);
            return SlotValueWan(airportCode, 1);
        }

        /// <summary>时段代表时刻（`ScheduleSlots.DepTimes`）的小时数。</summary>
        static int BandHour(int slotBand) => slotBand switch { 0 => 8, 1 => 12, 2 => 18, _ => 22 };

        /// <summary>
        /// 时刻槽价值（万美元）：机场等级 × **该波代表时刻**的价。
        ///
        /// ★2026-11-05：这是**概览/比价**口径（代表时刻 08:00 / 12:00 / 18:00 / 22:00），
        /// 不是单条槽的报价 —— 同一波内 06:30 与 09:00 价格不同（见 <see cref="HourDemandCoef"/>）。
        /// 单条槽的扣款与文案必须用 <see cref="SlotValueWanAt"/>，否则会出现
        /// 「邮件写 600 万、扣款 492 万」这种自相矛盾。
        /// </summary>
        public static float SlotValueWan(int airportTier, int slotBand)
            => SlotValueWanAtHour(airportTier, BandHour(slotBand));

        /// <summary>按机场实际等级取时刻槽价值（等级由底表推导，见 AirportCatalog）。</summary>
        public static float SlotValueWan(string airportCode, int slotBand)
            => SlotValueWan(AirlineCEO.Data.AirportCatalog.TierOfCode(airportCode), slotBand);

        /// <summary>联盟增量收入（万美元/年）：等级越高、航线越多增益越大。仅代码共享、尚未入盟时也有小额分成。</summary>
        public static float AllianceRevenueLiftWan(int tier, int routeCount, int codeSharePartners)
        {
            float cs = codeSharePartners * 15f;
            if (tier <= 0) return cs * 0.8f;
            float baseLift = tier * 40f;
            float net = routeCount * 4f;
            return baseLift + net + cs;
        }

        /// <summary>投资综合分（四维加权 0~100）。</summary>
        public static float InvestmentScore(float businessValue, float resourceFit, float barrier, float strategicFit)
        {
            return 0.3f * businessValue + 0.25f * resourceFit + 0.2f * barrier + 0.25f * strategicFit;
        }

        /// <summary>投资 IRR 近似（%）：NPV 由现金流与 WACC 推导的简化回报。</summary>
        public static float InvestmentIrr(float npvWan, float capitalWan, int years)
        {
            if (capitalWan <= 0f || years <= 0) return 0f;
            // IRR ≈ 使 NPV≈0 的贴现率；简化：NPV = Σ CF/(1+r)^t - I，CF 均摊
            float cf = (npvWan + capitalWan) / years;
            // 迭代近似
            float r = 0.10f;
            for (int k = 0; k < 40; k++)
            {
                float pv = 0f;
                for (int t = 1; t <= years; t++)
                    pv += cf / (float)Math.Pow(1.0 + r, t);
                float f = pv - capitalWan;
                float df = 0f;
                for (int t = 1; t <= years; t++)
                    df += -t * cf / (float)Math.Pow(1.0 + r, t + 1);
                if (Math.Abs(df) < 1e-6f) break;
                r -= f / df;
                if (r < 0f) { r = 0f; break; }
                if (r > 1f) { r = 1f; break; }
            }
            return r * 100f;
        }

        /// <summary>战略一致性评分（0~100）：由优先级覆盖与部门对齐推导。</summary>
        public static float StrategicConsistency(float[] priorityCoverage, float boardSatisfaction)
        {
            float cover = 0f;
            if (priorityCoverage != null && priorityCoverage.Length > 0)
            {
                float sum = 0f;
                for (int i = 0; i < priorityCoverage.Length; i++)
                    sum += Clamp01(priorityCoverage[i]);
                cover = sum / priorityCoverage.Length;
            }
            else cover = 0.7f;
            float board = Clamp01(boardSatisfaction / 100f);
            return Clamp(30f + cover * 45f + board * 25f, 0f, 100f);
        }

        /// <summary>地缘风险导致的航路费加成（%）。riskIndex 0~100。</summary>
        public static float GeoRiskSurcharge(int riskIndex) =>
            riskIndex >= 70 ? 0.20f : riskIndex >= 40 ? 0.10f : 0f;

        static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        static float Clamp01(float v) => Clamp(v, 0f, 1f);
    }

    /// <summary>
    /// Phase 2 Week 9 · MNT 机务工程部数值引擎（纯 C#，Unity 与 tools/balance 共用）。
    /// 覆盖：定检成本与间隔 / 维修策略可用率 / 航材库存健康 / 采购订单总价 / 机队可用率。
    /// </summary>
    public static class MaintenanceEconomics
    {
        /// <summary>定检成本（万美元）：A<B<C<D，随飞机规模缩放。</summary>
        public static float CheckCostWan(string checkType, int fleetSize)
        {
            float baseCost = checkType switch
            {
                "A" => 8f,
                "B" => 40f,
                "C" => 300f,
                "D" => 900f,
                _ => 8f
            };
            float scale = 1f + Math.Min(1.5f, fleetSize * 0.02f);
            return baseCost * scale;
        }

        /// <summary>定检间隔（月）。</summary>
        public static int CheckIntervalMonths(string checkType) => checkType switch
        {
            "A" => 2,
            "B" => 7,
            "C" => 21,
            "D" => 84,
            _ => 2
        };

        /// <summary>维修策略可用率（%）。0全面预防 1最低合规 2智能预测。</summary>
        public static float AvailabilityByStrategy(int strategy)
        {
            switch (strategy)
            {
                case 0: return 97.5f;
                case 1: return 93f;
                case 2: return 96.5f;
                default: return 95f;
            }
        }

        /// <summary>维修策略成本倍率（相对基准）。</summary>
        public static float CostMultiplierByStrategy(int strategy) => strategy switch
        {
            0 => 1.25f,
            1 => 0.75f,
            _ => 1.0f
        };

        /// <summary>航材库存健康度（0~100）：由缺料与安全库存覆盖推导。</summary>
        public static float StockHealth(int totalQuantity, int totalSafetyStock, int shortageCount)
        {
            float cover = totalSafetyStock <= 0 ? 1f : Math.Min(1f, (float)totalQuantity / totalSafetyStock);
            float penalty = shortageCount * 0.12f;
            return Clamp((cover * 100f) - penalty, 0f, 100f);
        }

        /// <summary>采购订单总价（万美元）。</summary>
        public static float OrderTotalWan(int quantity, float unitCostUsd) =>
            quantity * unitCostUsd / 10000f;

        /// <summary>退租检成本区间（万美元）。</summary>
        public static float RedeliveryCheckCostWan(int fleetAge) =>
            Clamp(500f + fleetAge * 60f, 500f, 2000f);

        static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
