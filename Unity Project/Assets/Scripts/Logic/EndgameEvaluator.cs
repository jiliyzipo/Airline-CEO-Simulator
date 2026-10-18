using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>软性失败 / 终局失败类型（GDB 4.2）。</summary>
    public enum DefeatKind
    {
        None = 0,
        HostileTakeover = 1,   // 敌意收购
        RegulatoryTakeover = 2 // 监管接管
    }

    /// <summary>软性失败判定结果。</summary>
    public class DefeatCheckResult
    {
        public DefeatKind kind;
        public string title;
        public string body;
    }

    /// <summary>
    /// P0 遗留：软性失败条件判定（敌意收购 / 监管接管）。
    /// 每季度在 TimeManager 推进时调用一次。
    /// </summary>
    public static class EndgameEvaluator
    {
        /// <summary>敌意收购：大股东满意度跌破该阈值。</summary>
        public const float HostileTakeoverSatisfaction = 20f;

        /// <summary>监管接管：安全评级连续维持 D 级的季度数。</summary>
        public const int RegulatoryDQuarters = 2;

        /// <summary>
        /// 每季度判定一次软性失败。若触发返回结果；否则返回 null。
        /// 副作用：维护「连续 D 级季度」计数器。
        /// </summary>
        public static DefeatCheckResult CheckDefeat(GameState gs)
        {
            if (gs == null || gs.gameOver) return null;

            // 1) 监管接管：安全评级长期 D 级
            if (gs.safetyRating == SafetyRating.D)
                gs.safetyConsecutiveDQuarters++;
            else
                gs.safetyConsecutiveDQuarters = 0;

            // 见习期软保护（第 3 财年）：安全评级低于阈值时不触发局方停飞
            if (gs.safetyConsecutiveDQuarters >= RegulatoryDQuarters
                && !ProgressionGate.SkipRegulatoryTakeover(gs.year))
            {
                return new DefeatCheckResult
                {
                    kind = DefeatKind.RegulatoryTakeover,
                    title = "监管接管",
                    body = "安全评级连续多个季度维持在 D 级，局方宣布强制接管公司、吊销运营资质。游戏结束。"
                };
            }

            // 2) 敌意收购：大股东满意度 < 20% 且经营亏损（股价低迷）
            if (gs.boardSatisfaction < HostileTakeoverSatisfaction && gs.yearNetProfit < 0f)
            {
                return new DefeatCheckResult
                {
                    kind = DefeatKind.HostileTakeover,
                    title = "敌意收购",
                    body = "大股东满意度跌破 20% 且经营持续亏损，外部资本发动敌意收购，你被迫出局。游戏结束。"
                };
            }

            return null;
        }

        /// <summary>将失败结果写入 GameState（gameOver + reason + kind）。</summary>
        public static void ApplyDefeat(GameState gs, DefeatCheckResult defeat)
        {
            if (gs == null || defeat == null) return;
            gs.gameOver = true;
            gs.gameOverVictory = false;
            gs.lastDefeatKind = (int)defeat.kind;
            gs.gameOverReason = $"{defeat.title}：{defeat.body}";
        }
    }
}
