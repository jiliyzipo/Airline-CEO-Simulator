using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// Phase 2 Week 6 · 机队 / 航线网络数值公式（纯 C#，Unity 与 tools/balance 共用）。
    /// 单位：货币「万美元」；利用率为小时或百分比。
    /// </summary>
    public static class FleetEconomics
    {
        // —— 大修类型标签（n107）——
        public const string OverhaulA = "A 检（航线轻检）";
        public const string OverhaulB = "B 检（中度定检）";
        public const string OverhaulC = "C 检（深度定检）";
        public const string OverhaulD = "D 检（大修翻新）";
        public const string OverhaulEngine = "发动机大修";
        public const string OverhaulGear = "起落架大修";
        public const string OverhaulCabin = "客舱翻新";

        public static readonly string[] OverhaulKinds =
        {
            OverhaulA, OverhaulB, OverhaulC, OverhaulD,
            OverhaulEngine, OverhaulGear, OverhaulCabin
        };

        // —— 来源口径（与 Aircraft.source 的四个取值一一对应，避免各处再写字符串字面量）——
        public const string SourceOwned = "自有";
        public const string SourceFinanceLease = "融资租赁";
        public const string SourceOperatingLease = "经营租赁";
        public const string SourceUsed = "二手引进";

        /// <summary>退出方式：退役（自有 / 二手引进 → 转售拆解，回收残值）。</summary>
        public const string DisposalRetired = "退役";

        /// <summary>退出方式：退租（租赁机 → 归还出租人，产权不属于公司，故无残值收入）。</summary>
        public const string DisposalReturned = "退租";

        /// <summary>融资租赁名义租期（月）：10 年（设计定稿「低首付 + 月租 + 买断」）。经营租赁无固定租期。</summary>
        public const int FinanceLeaseTermMonths = 120;

        /// <summary>提前解约违约金比例：剩余租期租金 × 30%（与过渡业务「提前召回」同一口径）。</summary>
        public const float LeaseEarlyExitRate = 0.30f;

        /// <summary>是否租赁机（融资租赁 / 经营租赁）：产权在出租人，退出只能「退租」。</summary>
        public static bool IsLeasedSource(string source) =>
            string.Equals(source, SourceFinanceLease, StringComparison.Ordinal)
            || string.Equals(source, SourceOperatingLease, StringComparison.Ordinal);

        /// <summary>是否融资租赁（含买断条款，提前退出属违约，要付违约金）。</summary>
        public static bool IsFinanceLease(string source) =>
            string.Equals(source, SourceFinanceLease, StringComparison.Ordinal);

        /// <summary>已付租期（月）：按财年粗算（当年 − 引进年）× 12，年内不细分。</summary>
        public static int LeaseMonthsPaid(int purchaseYear, int currentYear) =>
            Math.Max(0, currentYear - purchaseYear) * 12;

        /// <summary>
        /// 退租代价（万美元）：经营租赁 0（设计稿定为「灵活退租」）；融资租赁按剩余租期租金 × 30% 计违约金。
        /// 租赁机产权在出租人，退租**不回收残值** —— 旧口径把租赁机当自有飞机「退役」并发放残值现金，
        /// 等于零成本租进一架飞机再照目录价卖掉（凭空造钱），此处是该破口的收口点。
        /// </summary>
        public static float LeaseReturnCostWan(string source, float monthlyLeaseWan, int monthsPaid)
        {
            if (!IsFinanceLease(source)) return 0f;
            int remain = Math.Max(0, FinanceLeaseTermMonths - Math.Max(0, monthsPaid));
            return remain * Math.Max(0f, monthlyLeaseWan) * LeaseEarlyExitRate;
        }

        /// <summary>残值（万美元）：直线折旧 + 机龄折价，下限 10%（对齐 FleetManager）。</summary>
        public static float ResidualValue(float purchasePrice, int age)
        {
            if (purchasePrice <= 0f) return 0f;
            float pct = (100f - age * 3f) / 100f;
            pct = Math.Max(0.10f, pct);
            return purchasePrice * pct;
        }

        /// <summary>单机月维护（万美元）：基础 + 机龄 6%/年；&gt;20 年翻倍（对齐 FleetManager）。</summary>
        public static float MonthlyMaintenance(int age)
        {
            float cost = EconomyBalanceParams.FleetMonthlyMaintWan
                         * (1f + age * EconomyBalanceParams.MaintAgeUpliftPerYear);
            if (age > 20) cost *= 2f;
            return cost;
        }

        /// <summary>
        /// 单机月维护（含来源差异）= 账本唯一口径。旧版 FleetManager.MonthlyMaintOf 自己写了一套
        /// 「机龄 5%/年 + 二手 1.35」的公式，而 UI 的 FleetSubpagePanel / 运力测算走的是本类 6%/年，
        /// 同一架飞机在两个面板上印出两个数、账本按第三个数扣钱。此处收拢为一处，双方都调这里。
        /// </summary>
        public static float MonthlyMaintenance(AirlineCEO.Data.Aircraft a)
        {
            if (a == null || a.status == AirlineCEO.Data.AircraftStatuses.Retired) return 0f;
            float cost = MonthlyMaintenance(a.age);
            if (string.Equals(a.source, SourceUsed, StringComparison.Ordinal) && a.age >= 12) cost *= 1.35f;
            return cost;
        }

        /// <summary>大修预估成本（万美元），宽体 ×1.6。</summary>
        public static float OverhaulCost(string overhaulType, bool widebody)
        {
            float w = widebody ? 1.6f : 1f;
            switch (overhaulType)
            {
                case OverhaulA: return 10f * w;
                case OverhaulB: return 35f * w;
                case OverhaulC: return 300f * w;
                case OverhaulD: return 2000f * w;
                case OverhaulEngine: return 500f * w;
                case OverhaulGear: return 150f * w;
                case OverhaulCabin: return 80f * w;
                default: return 100f * w;
            }
        }

        /// <summary>大修停场天数。</summary>
        public static int OverhaulDays(string overhaulType)
        {
            switch (overhaulType)
            {
                case OverhaulA: return 2;
                case OverhaulB: return 5;
                case OverhaulC: return 10;
                case OverhaulD: return 30;
                case OverhaulEngine: return 14;
                case OverhaulGear: return 10;
                case OverhaulCabin: return 7;
                default: return 7;
            }
        }

        /// <summary>舱位配置收益乘数：高端舱位占比高于基准时溢价，反之折价。头等权重 2×，商务 1×。</summary>
        public static float SeatMixRevenueMultiplier(int first, int business, int economy)
        {
            int total = Math.Max(1, first + business + economy);
            float premiumShare = (first * 2f + business) / total;
            const float baseShare = 0.12f; // 基准：默认 12% 商务
            float delta = (premiumShare - baseShare) / 0.10f * 0.015f;
            return Clamp(1f + delta, 0.85f, 1.15f);
        }

        /// <summary>日利用率估算（小时）：周班次（**往返圈数**）× 每段轮挡 × 2 段 / 7。口径同 <see cref="OpsEconomics"/>。</summary>
        public static float DailyUtilizationHours(int weeklyFlights, float avgBlockHours)
        {
            return weeklyFlights * avgBlockHours * OpsEconomics.RoundTripSectors / 7f;
        }

        /// <summary>盈亏平衡利用率（%）：月固定成本 / (单位小时收益 × 月可用小时)。</summary>
        public static float BreakevenUtilization(float monthlyFixedCostWan, float revenuePerHourWan, float availableHoursPerMonth)
        {
            if (revenuePerHourWan <= 0f || availableHoursPerMonth <= 0f) return 0f;
            return Math.Min(1.2f, monthlyFixedCostWan / (revenuePerHourWan * availableHoursPerMonth));
        }

        /// <summary>购机投资回收期（月）；无法回本返回 999。</summary>
        public static float PaybackMonths(float priceWan, float monthlyProfitWan)
        {
            if (monthlyProfitWan <= 0f) return 999f;
            return priceWan / monthlyProfitWan;
        }

        /// <summary>航线日利润估算（万美元/日）：由单班日基准 × 周班次/7 折算。</summary>
        public static float DailyProfitFromWeekly(float baseDailyProfit, int weeklyFlights)
        {
            if (weeklyFlights <= 0) return 0f;
            return baseDailyProfit * weeklyFlights / 7f;
        }

        static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
