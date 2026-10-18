namespace AirlineCEO.Logic
{
    /// <summary>
    /// 现金跑道计算器（GDD 第 15 / 7.4 节）。
    /// 返回当前现金可支撑的月数；月烧钱 ≤ 0 时视为近乎无限（99）。
    /// 命名保留任务书中的 CashRunnel（Runway 笔误）。
    /// </summary>
    public static class CashRunnelCalculator
    {
        public const float InfiniteRunwayMonths = 99f;

        /// <param name="currentCash">当前现金（万美元）</param>
        /// <param name="monthlyBurnRate">月净烧钱（万美元/月，正数表示净流出）</param>
        public static float GetRunwayMonths(float currentCash, float monthlyBurnRate)
        {
            if (monthlyBurnRate <= 0f)
                return InfiniteRunwayMonths;

            if (currentCash <= 0f)
                return 0f;

            return currentCash / monthlyBurnRate;
        }

        /// <summary>黄预警：&lt; 3 个月；红预警：&lt; 2 个月（GDD 15.7）。</summary>
        public static string GetRunwayWarning(float runwayMonths)
        {
            if (runwayMonths < 2f) return "红";
            if (runwayMonths < 3f) return "黄";
            return "绿";
        }

        /// <summary>玩家可读文案：现金跑道，流动性还可以撑 X 个月。</summary>
        public static string FormatPlayerLabel(float runwayMonths)
        {
            if (runwayMonths >= InfiniteRunwayMonths - 0.1f)
                return "现金跑道：账上的钱很宽裕，短期内不用担心花光";
            return $"现金跑道：按现在开支，流动性还可以撑 {runwayMonths:F1} 个月";
        }

        public static string FormatWarningPlain(float runwayMonths)
        {
            string level = GetRunwayWarning(runwayMonths);
            return level switch
            {
                "红" => "偏危险，得尽快想办法进钱或省钱",
                "黄" => "有点紧，建议开始留意现金流",
                _ => "目前还算稳妥"
            };
        }
    }
}
