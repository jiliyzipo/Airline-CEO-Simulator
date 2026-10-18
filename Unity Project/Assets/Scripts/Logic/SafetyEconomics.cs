using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// Phase 2 Week 10 · SAF 安全质量部数值引擎（纯 C#，Unity 与 tools/balance 共用）。
    /// 覆盖：评级带、调查成本/周期、安全文化影响、局方关系改善。
    /// </summary>
    public static class SafetyEconomics
    {
        /// <summary>评级带：分数 → 字母（A≥90 B70-89 C50-69 D&lt;50）。</summary>
        public static char RatingBand(float score) =>
            score >= 90f ? 'A' : score >= 70f ? 'B' : score >= 50f ? 'C' : 'D';

        /// <summary>调查成本（万美元）：深度 × 严重度。</summary>
        public static float InvestigationCostWan(int depth, int severity)
        {
            float depthCost = depth switch { 0 => 5f, 1 => 20f, _ => 60f };
            float sevMult = 1f + severity * 0.6f;
            return depthCost * sevMult;
        }

        /// <summary>调查周期（天）。</summary>
        public static int InvestigationDays(int depth) => depth switch { 0 => 3, 1 => 7, _ => 14 };

        /// <summary>安全文化隐藏风险倍率：惩罚型隐瞒风险更高。</summary>
        public static float CultureHiddenRisk(int cultureType) => cultureType == 0 ? 1.3f : 0.7f;

        /// <summary>安全文化报告率倍率：鼓励报告型报告率更高。</summary>
        public static float CultureReportRate(int cultureType) => cultureType == 0 ? 0.5f : 1.4f;

        /// <summary>局方关系改善成本（万美元）。</summary>
        public static float RegulatorImproveCostWan(int status) => status switch
        {
            0 => 200f,
            1 => 120f,
            2 => 60f,
            _ => 0f
        };

        /// <summary>D 级触发局方接管风险。</summary>
        public static bool RegulatorTakeoverRisk(float score) => score < 50f;

        /// <summary>局方检查应对：0全力配合 1重点整改 2申请延期。</summary>
        public static void InspectionChoice(int choice, out float costWan, out float ratingDelta, out int regulatorDelta, out string label)
        {
            switch (choice)
            {
                case 0:
                    costWan = 15f; ratingDelta = 3f; regulatorDelta = 1; label = "全力配合";
                    break;
                case 1:
                    costWan = 35f; ratingDelta = 1f; regulatorDelta = 0; label = "重点整改后迎检";
                    break;
                default:
                    costWan = 8f; ratingDelta = -2.5f; regulatorDelta = -1; label = "申请延期";
                    break;
            }
        }

        /// <summary>调查深度名。</summary>
        public static string DepthName(int depth) => depth switch { 0 => "快速调查", 1 => "标准调查", _ => "深度调查" };

        /// <summary>严重度名。</summary>
        public static string SeverityName(int severity) => severity switch
        {
            0 => "事故征候",
            1 => "一般事故",
            2 => "重大事故",
            _ => "特大空难"
        };
    }
}
