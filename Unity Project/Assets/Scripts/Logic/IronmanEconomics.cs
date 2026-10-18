using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 铁人模式规则（GDB 第31章 · P2 长线运营 · 存档模式）。
    /// 纯 C#，Unity 与 tools/balance 共用。
    /// 核心设定：
    ///   · 铁人模式开局开启后，禁用手动存档（F5），仅 1 个自动存档实时覆盖。
    ///   · 作为风险补偿，遗产评分 ×1.2 系数（封顶 100）。
    /// </summary>
    public static class IronmanEconomics
    {
        /// <summary>铁人模式遗产评分加成系数（GDB 31.1）。</summary>
        public const float ScoreMultiplier = 1.2f;

        /// <summary>评分封顶（0~100）。</summary>
        public const float MaxScore = 100f;

        /// <summary>铁人模式是否启用评分加成。</summary>
        public static bool ShouldBoost(bool ironmanActive) => ironmanActive;

        /// <summary>铁人模式下调整遗产总分：×1.2 后封顶 100。</summary>
        public static float ApplyIronmanScore(float baseTotal)
        {
            if (baseTotal <= 0f) return baseTotal;
            return Math.Min(MaxScore, baseTotal * ScoreMultiplier);
        }

        /// <summary>UI 说明文案。</summary>
        public static string Describe() =>
            "铁人模式：禁用手动存档，季度自动存档实时覆盖；作为补偿，遗产评分 ×1.2。";
    }
}
