namespace AirlineCEO.Logic
{
    /// <summary>
    /// 董事会数值（纯 C#，供 tools/balance 离线闭环）：
    /// 年度目标保底阈值、续聘线、目标达成率与满意度增减的单一权威来源。
    /// 收敛原先散落在 YearEndEvaluator / TargetGenerator / BoardManager 的硬编码。
    /// </summary>
    public static class BoardEconomics
    {
        /// <summary>董事满意度初始值（0～100）。</summary>
        public const float InitialSatisfaction = 50f;

        /// <summary>见习保护年限：第 1～N 财年未达标不直接解雇。</summary>
        public const int ProbationMaxYear = 3;

        // GDD 5.3 / 5.4：年度目标保底阈值
        public const float GuaranteeNetProfit = 50f;   // 万美元（验收示例）
        public const float GuaranteeLoadFactor = 75f;  // %
        public const float DefaultSafety = 80f;        // 安全指数门槛
        public const int DefaultRouteCount = 1;        // 至少开通 N 条

        /// <summary>续聘线（目标达成率阈值，%）。</summary>
        public static float ReappointmentThreshold(DifficultyMode difficulty)
        {
            return difficulty switch
            {
                DifficultyMode.Easy => 50f,
                DifficultyMode.Hard => 75f,
                _ => 60f
            };
        }

        /// <summary>目标达成率（0～100%）。</summary>
        public static float AchievementRate(int total, int done)
        {
            return total > 0 ? done * 100f / total : 0f;
        }

        /// <summary>投票结果 → 董事会满意度增减（GDD 4.2）。</summary>
        public static float SatisfactionDelta(VoteOutcome outcome, float completionRate)
        {
            return outcome switch
            {
                VoteOutcome.Reappointed => completionRate >= 80f ? 10f : 8f,
                VoteOutcome.ProbationContinue => -4f,
                VoteOutcome.Fired => -15f,
                VoteOutcome.Retired => 0f,
                _ => 0f
            };
        }
    }
}
