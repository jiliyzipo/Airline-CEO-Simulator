using AirlineCEO.Logic;
using AirlineCEO.Systems;

namespace AirlineCEO.AI
{
    /// <summary>
    /// 本地话术库（DeepSeek 失败时容灾）。面向新手，大白话。
    /// </summary>
    public static class NeuralCommandMatcher
    {
        public static string Reply(string userInput)
        {
            string text = userInput?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                return "战略顾问：你可以问问「手里还有多少钱」或「北京到伦敦划不划算」。";

            var gs = GameState.Instance;
            float cash = gs != null ? gs.cash : 0f;
            float burn = gs != null ? gs.monthlyBurnRate : 80f;
            float runway = CashRunnelCalculator.GetRunwayMonths(cash, burn);

            if (ContainsAny(text, "北京", "伦敦", "航线", "rpm", "RPM", "分析", "划算", "开通"))
            {
                var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
                {
                    distance = 8150f,
                    fuelPricePerTon = 850f,
                    competitorSupply = 0.65f,
                    demandMultiplier = 1.2f,
                    brandEquity = GameState.Instance != null ? GameState.Instance.brandAwareness : 55f
                });
                // 注意：本地话术也会被 BuildFallback 包成 AdvisorResponse；
                // suggestedAction 在 GuessAction 里设为 open_route。
                return "战略顾问：北京飞伦敦大概每天能赚 "
                       + $"{rpm.dailyProfit:F1} 万美元，{GameDataSnapshot.ToPlainRating(rpm.rating)}。"
                       + "不过对手航班已经很多，开通的话要盯紧抢客情况。"
                       + $"综合评定 {rpm.score} 分。";
            }

            if (ContainsAny(text, "现金", "跑道", "烧钱", "破产", "流动性", "钱"))
            {
                return $"财务顾问：账上大约 {cash:F0} 万美元。"
                       + $"{CashRunnelCalculator.FormatPlayerLabel(runway)}，"
                       + $"{CashRunnelCalculator.FormatWarningPlain(runway)}。";
            }

            if (ContainsAny(text, "首航", "起飞", "开航", "开通航线"))
            {
                int routes = gs != null ? gs.activeRoutes.Count : 0;
                return routes > 0
                    ? $"战略顾问：已开通 {routes} 条航线。可去运营部编周计划，或构建航班环串联机班。"
                    : "战略顾问：点运营部「开通航线」，或在市场分析后执行开通建议。启动资金约 300 万。";
            }

            if (ContainsAny(text, "董事", "董事会", "化学"))
            {
                return "人力顾问：董事会成员的脾气和站队已经准备好了，完整开会界面还在做，稍等等。";
            }

            return $"战略顾问：收到「{Truncate(text, 12)}」。"
                   + $"你现在大约有 {cash:F0} 万美元，"
                   + $"{CashRunnelCalculator.FormatPlayerLabel(runway)}。"
                   + "更复杂的操作还在开发中。";
        }

        private static bool ContainsAny(string text, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (text.IndexOf(keys[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }
    }
}
