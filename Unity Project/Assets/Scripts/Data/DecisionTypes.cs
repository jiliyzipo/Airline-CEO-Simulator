using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Data
{
    /// <summary>部门意见灯色（n21 CEO 决策会议五部门卡片）。</summary>
    public enum DeptOpinionColor
    {
        Red = 0,
        Yellow = 1,
        Green = 2
    }

    /// <summary>决策议题类别（决定补条件模板与意见口径）。</summary>
    public static class DecisionCategories
    {
        public const string Purchase = "purchase";   // 购机/重大采购
        public const string Route = "route";         // 开线/航线运营
        public const string Recruit = "recruit";     // 招聘/关键岗位
        public const string Investment = "investment"; // 战略投资/并购
        public const string Generic = "generic";
    }

    /// <summary>
    /// 一次 CEO 决策会议的议题（纯 C#，可序列化，Unity 与 tools/balance 共享）。
    /// </summary>
    [Serializable]
    public class DecisionProposal
    {
        public string id;
        public string topic;            // 顶栏议题，如「引进 1 架 A320neo」
        public string category;         // DecisionCategories
        public float costWan;           // 涉及金额（万美元），0 表示无直接现金
        public string sourceLabel;      // 发起部门/来源
        public string sourcePageId;     // 批准后应返回的页
        /// <summary>议题来源的收件箱邮件 id；会议裁决后回写该函（隐藏收件箱底部决策按钮）。</summary>
        public string sourceMailId;
        // 可选的议题专有字段（用于意见量化）
        public string aircraftType;
        public int count = 1;
        public string routeOd;
        public string routePath;
        public float expectedDailyProfit;
    }

    /// <summary>部门意见（红黄绿 + 量化意见）。</summary>
    [Serializable]
    public class DeptOpinion
    {
        public string deptCode;         // OPS/MKT/FIN/HR/SAF/MNT/STG
        public DeptOpinionColor color;
        public string headline;         // 一句话结论
        public string detail;           // 量化说明
        public float score;             // 0~100，用于排序/汇总
    }

    /// <summary>供决策经济学使用的 KPI 快照（纯 C#，避免依赖 GameState）。</summary>
    [Serializable]
    public class DecisionSnapshot
    {
        public float cash;
        public float monthlyBurn;
        public float runwayMonths;
        public float loadFactor;
        public float onTime;
        public float safetyIndex;
        public int activeRoutes;
        public float yearNetProfit;
        public float monthlyRevenue;
        public string gdpPhase;         // boom/recession/recovery
        public float boardSatisfaction;
    }

    /// <summary>n104 补条件表单中的一条条件。</summary>
    [Serializable]
    public class SupplementCondition
    {
        public string id;
        public string description;      // 可编辑描述
        public string ownerDept;        // 责任部门/人
        public int priority;            // 0=建议 1=必要 2=强制
        public string deadline;         // 截止日期（字符串，如「Q2」）
        public string criteria;         // 完成标准（可量化）
        public bool done;
    }

    /// <summary>一次「暂缓 → 补条件」表单。</summary>
    [Serializable]
    public class SupplementForm
    {
        public string proposalId;
        public string topic;            // 来源议题摘要
        public string category;         // DecisionCategories（用于机会成本与模板）
        public string reason;           // 暂缓原因
        public string sourceDept;       // 发起部门
        public List<SupplementCondition> conditions = new List<SupplementCondition>();
    }

    /// <summary>决策历史条目（n38 历史回放 / n21 记录）。</summary>
    [Serializable]
    public class DecisionHistoryEntry
    {
        public int year;
        public int quarter;
        public string topic;
        public string category;
        public string outcome;          // approved / deferred / rejected
        public string opinionSummary;   // 如「绿3 黄2 红0」
    }

    /// <summary>
    /// 跨部门决策流聚合状态（Phase 3 Week 11），挂到 GameState.decision。
    /// </summary>
    [Serializable]
    public class DecisionState
    {
        public List<DecisionHistoryEntry> history = new List<DecisionHistoryEntry>();
        public List<SupplementForm> supplementForms = new List<SupplementForm>();
        /// <summary>当前待决策的议题（运行时；不进存档）。</summary>
        [NonSerialized] public DecisionProposal pending;
    }

    [Serializable]
    public class DecisionSave
    {
        public DecisionState state = new DecisionState();
    }
}
