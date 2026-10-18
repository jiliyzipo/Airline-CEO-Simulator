using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Data
{
    /// <summary>安全评级 A/B/C/D（GDD 7.6 · Phase 7）。</summary>
    public enum SafetyRating
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3
    }

    /// <summary>安全事件严重度。</summary>
    public enum SafetyEventSeverity
    {
        Mild = 0,
        Moderate = 1
    }

    /// <summary>一条安全事件日志。</summary>
    [Serializable]
    public class SafetyEventLog
    {
        public string id;
        public string dateLabel;
        public SafetyEventSeverity severity;
        public string title;
        public string detail;
        public SafetyRating ratingBefore;
        public SafetyRating ratingAfter;
    }

    // ============================================================
    // SAF 安全质量部（Phase 2 Week 10）
    // ============================================================

    /// <summary>局方/监管机构关系（n95/n100）。</summary>
    [Serializable]
    public class RegulatorRelation
    {
        public string regulator;  // CAAC / FAA / EASA
        public int status;        // 0红 1橙 2黄 3绿
    }

    /// <summary>安全事件调查（n94）。</summary>
    [Serializable]
    public class SafetyInvestigation
    {
        public string id;
        public int severity;      // 0事故征候 1一般 2重大 3特大
        public int depth;         // 0快速 1标准 2深度
        public string status = "调查中"; // 调查中 / 已结案
    }

    /// <summary>安全质量部聚合状态（Phase 2 Week 10 · SAF）。</summary>
    [Serializable]
    public class SafetyState
    {
        public float ratingScore = 82f;       // 安全评级分 0~100（A≥90 B70-89 C50-69 D<50）
        public int zeroAccidentDays = 210;    // 零重大事故天数
        public float auditPassRate = 95f;     // 审计通过率%
        public float cultureIndex = 68f;      // 安全文化指数 0~100
        public int cultureType;               // 0惩罚型 1鼓励报告型
        public int riskRed;                   // 不可接受风险数
        public int riskOrange;                // 高风险数
        public int riskYellow;                // 中风险数
        public int riskBlue;                  // 低风险数
        public float rectificationRate = 88f; // 隐患整改率%
        public List<RegulatorRelation> regulators = new List<RegulatorRelation>();
        public List<SafetyInvestigation> investigations = new List<SafetyInvestigation>();
        public float safetyBudgetWan = 400f;   // 年度安全预算（万美元）
        public float safetyBudgetUsedWan = 250f;
    }

    [Serializable]
    public class SafetySave
    {
        public SafetyState state;
    }
}
