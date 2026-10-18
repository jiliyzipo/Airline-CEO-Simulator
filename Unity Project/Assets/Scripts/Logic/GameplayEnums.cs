namespace AirlineCEO.Logic
{
    /// <summary>游戏难度（GDD 4.2 · 续聘线阈值不同）。</summary>
    public enum DifficultyMode
    {
        Easy = 0,   // 续聘线 50%
        Normal = 1, // 60%
        Hard = 2    // 75%
    }

    /// <summary>财年末董事会投票结果。</summary>
    public enum VoteOutcome
    {
        Reappointed,       // 续聘成功
        ProbationContinue, // 见习保护：再给一年
        Fired,             // 解雇 → Game Over
        Retired            // 光荣退休 → 胜利通关（GDB 第4章）
    }
}
