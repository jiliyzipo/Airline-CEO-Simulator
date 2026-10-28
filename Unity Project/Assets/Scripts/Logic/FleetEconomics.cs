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

        /// <summary>单机月维护（万美元）：基础 + 机龄 5%/年；&gt;20 年翻倍（对齐账本口径）。</summary>
        public static float MonthlyMaintenance(int age)
        {
            float cost = EconomyBalanceParams.FleetMonthlyMaintWan
                         * (1f + age * EconomyBalanceParams.MaintAgeUpliftPerYear);
            if (age > 20) cost *= 2f;
            return cost;
        }

        /// <summary>
        /// 单机月维护（含来源差异）= 账本唯一口径。旧版 FleetManager.MonthlyMaintOf 自己写了一套
        /// 「机龄 5%/年 + 二手 1.35」的公式，而 UI 的 FleetSubpagePanel / 运力测算当时写死 6%/年，
        /// 同一架飞机在两个面板上印出两个数、账本按第三个数扣钱。此处收拢为一处，双方都调这里。
        /// 定案取 **5%**（= 账本历史口径，见 <c>EconomyBalanceParams.MaintAgeUpliftPerYear</c>）。
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

        /// <summary>
        /// 大修不可预见费比例（相对预估成本）。
        ///
        /// ★2026-10-27 修：n107 页面一直列着「不可预见费(+12%)」这一行，但
        /// <c>FleetManager.ScheduleOverhaul</c> 只校验、只扣 `OverhaulCost`，
        /// 这笔钱**从来没有被收过** —— 典型的「文案里有、账上没有」伪数字。
        /// 现在比例收成常量，显示与扣费共用它，不许再各写一个 0.12f。
        /// </summary>
        public const float OverhaulContingencyRate = 0.12f;

        /// <summary>大修实际扣款 = 预估成本 × (1 + 不可预见费)。显示与扣费必须都走这里。</summary>
        public static float OverhaulCashCost(string overhaulType, bool widebody)
            => OverhaulCost(overhaulType, widebody) * (1f + OverhaulContingencyRate);

        /// <summary>大修不可预见费本身（万美元），仅用于预算页逐项展示。</summary>
        public static float OverhaulContingency(string overhaulType, bool widebody)
            => OverhaulCost(overhaulType, widebody) * OverhaulContingencyRate;

        /// <summary>
        /// 机龄超过此值必须靠大修延寿，否则财年末停场。
        ///
        /// ★2026-10-27 第二次修（用户裁定「让大修能延寿」）：原规则是
        /// <c>age &gt; 15 → 永久维护中</c>，且**没有任何恢复路径** ——
        /// `LiveClockSimulator` 只处理 `InFlight → 适航`，落地时飞机已不在飞，
        /// 于是老飞机一旦被财年末那一刷判下来，就只能售卖/退租。
        /// 这与「大修」的存在直接冲突：花 10 万做 A 检、甚至花 2000 万做 D 检，
        /// 都救不回一架 16 年的飞机，而现实里重检正是延寿手段。
        ///
        /// 现改为：机龄超限时**看机况**放行。机况是飞行磨损（0.04/小时）与财年末（−2/年）
        /// 持续消耗的资源，而大修是唯一的回补渠道（+25/次，且每次都要停场 N 天），
        /// 于是「老飞机能不能继续飞」变成一道真实的成本题，而不是一道死刑判决。
        /// </summary>
        public const int LifeExtensionAgeLimit = 15;

        /// <summary>
        /// 每次大修抬升的机况点数。
        /// ★2026-10-27 提到常量：此前 25 只写在 <c>FleetManager.ScheduleOverhaul</c> 里，
        /// 而「大修预算确认」屏要预告「定检后机况 X% → Y%」—— 两处各写一个 25 就会重演
        /// 不可预见费那次「屏幕数字 ≠ 账面数字」。故实扣与预告共用本常量。
        /// </summary>
        public const float OverhaulConditionGain = 25f;

        /// <summary>
        /// 延寿要求的最低机况。取 50 的依据：大修每次 +25，最差的 5% 机况要做两轮定检才够
        /// （A 检 10 万 × 2 + 两次停场），而飞行磨损会持续吃掉它 —— 让延寿有代价、但不是不可及。
        /// </summary>
        public const float LifeExtensionMinCondition = 50f;

        /// <summary>
        /// 该机的机龄-机况是否允许继续适航：机龄未超限一律放行；超限则要求机况达标。
        /// 财年末 <c>RefreshStatus</c> 与本规则的判定必须共用本函数，不许各写一份。
        /// </summary>
        public static bool CanExtendLife(int age, float condition)
            => age <= LifeExtensionAgeLimit || condition >= LifeExtensionMinCondition;

        /// <summary>是否「机龄已超限且机况不足以延寿」——即按规则该停场。</summary>
        public static bool IsGroundedByAge(int age, float condition)
            => !CanExtendLife(age, condition);

        /// <summary>
        /// 按「财年末」口径预判还算不算适航：机况先扣年度磨损再判。
        /// UI 预告别直接用当前机况 —— 否则 50% 的飞机会被写成「达标」，年结当天却停场。
        /// </summary>
        public static bool WillStayAirworthyAtYearEnd(int age, float conditionNow)
            => CanExtendLife(age, Math.Max(AnnualConditionFloor, conditionNow - AnnualConditionDecay));

        /// <summary>
        /// 财年末的机况自然磨损（点/年）。
        /// ★2026-10-27 提到常量：UI 要预告「年结后还够不够延寿」，若 UI 用当前机况判、
        /// 而 <c>OnYearEndAgeFleet</c> 先扣 2 点再判，那么卡在 50%~52% 的飞机会
        /// 被屏幕上写成「达标」却在年结当天停场 —— 又是一次「界面说谎」。故两边共用一个数。
        /// </summary>
        public const float AnnualConditionDecay = 2f;

        /// <summary>年结后机况的下限（与 <c>OnYearEndAgeFleet</c> 的 Clamp 一致）。</summary>
        public const float AnnualConditionFloor = 20f;

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
