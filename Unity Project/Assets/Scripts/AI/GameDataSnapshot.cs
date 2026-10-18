using System.Text;
using AirlineCEO.Logic;
using AirlineCEO.Systems;

namespace AirlineCEO.AI
{
    /// <summary>组装发给 DeepSeek 的游戏数据快照（含 RPM 真实数值）。</summary>
    public static class GameDataSnapshot
    {
        public static RPMCalculator.RPMOutput PekLhrRpm { get; private set; }

        public static string Build()
        {
            var gs = GameState.Instance;
            float cash = gs != null ? gs.cash : 0f;
            float burn = gs != null ? gs.monthlyBurnRate : 80f;
            float effectiveBurn = gs != null ? gs.EffectiveMonthlyBurn : burn;
            float runway = CashRunnelCalculator.GetRunwayMonths(cash, effectiveBurn);
            string date = gs != null ? gs.DateLabel : "第 1 年 Q1";
            float satisfaction = gs != null ? gs.boardSatisfaction : 50f;
            var macro = MacroEconomyManager.Instance;

            PekLhrRpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = 8150f,
                fuelPricePerTon = gs != null ? gs.fuelPricePerTon : 850f,
                competitorSupply = 0.65f,
                demandMultiplier = (macro != null ? macro.GetDemandMultiplier("leisure") : 1f) * 1.2f,
                brandEquity = gs != null ? gs.brandAwareness : 55f
            });

            var sb = new StringBuilder();
            sb.AppendLine("【游戏数据快照】");
            sb.AppendLine($"日期：{date}");
            sb.AppendLine($"现金：{cash:F0} 万美元");
            sb.AppendLine($"月固定开销：{burn:F0} 万美元");
            sb.AppendLine($"航线月收入：{(gs != null ? gs.monthlyRevenue : 0f):F0} 万美元");
            sb.AppendLine($"已开通航线数：{(gs != null ? gs.activeRoutes.Count : 0)}");
            sb.AppendLine($"{CashRunnelCalculator.FormatPlayerLabel(runway)}");
            sb.AppendLine($"董事会满意度：{satisfaction:F0}");
            sb.AppendLine($"已开通航线：{(gs != null && gs.activeRoutes != null && gs.activeRoutes.Count > 0 ? "是" : "否")}");
            sb.AppendLine($"机队规模（MVP）：1（示意）");
            sb.AppendLine(
                $"北京-伦敦航线评估：综合分 {PekLhrRpm.score}（{ToPlainRating(PekLhrRpm.rating)}），" +
                $"大概每天赚 {PekLhrRpm.dailyProfit:F1} 万美元" +
                $"（收入 {PekLhrRpm.dailyRevenue:F1}，成本 {PekLhrRpm.dailyCost:F1}）；" +
                $"对手已占约 65% 运力，竞争较激烈。");
            sb.AppendLine("写作要求：给新手看的大白话；少用术语；结尾必须写「综合评定XX分」（用上面的综合分）。");
            sb.AppendLine("只输出 JSON：advisor, emotion, message, suggestedAction, data.profit, data.risk。message≤50字。");
            return sb.ToString();
        }

        public static string ToPlainRating(string rating)
        {
            return rating switch
            {
                "绿" => "比较划算，可以考虑开通",
                "黄" => "一般，先观望或谨慎开通",
                "红" => "不太划算，建议暂缓",
                _ => rating
            };
        }

        /// <summary>附在顾问回复末尾的通俗数据注脚（含综合评定分数）。</summary>
        public static string FormatRpmFootnote(RPMCalculator.RPMOutput rpm)
        {
            return $"综合评定 {rpm.score} 分（{ToPlainRating(rpm.rating)}；北京-伦敦大约每天能赚 {rpm.dailyProfit:F1} 万美元）";
        }

        public static string BuildUserPrompt(string playerInput)
        {
            return $"玩家输入：{playerInput}\n\n{Build()}";
        }
    }
}
