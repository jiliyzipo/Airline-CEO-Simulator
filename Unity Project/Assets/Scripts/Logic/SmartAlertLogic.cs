using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Logic
{
    /// <summary>智能提示类型（GDB 28.4 四类幕僚主动提醒）。</summary>
    public enum SmartAlertType
    {
        None = 0,
        CashRunway = 1,   // 现金跑道 < 4 个月（财务顾问）
        PilotShortage = 2, // 关键岗位空缺率 > 15%（人力资源顾问）
        StrategyDrift = 3, // 战略一致性 < 50（战略顾问）
        OnTimeDecline = 4  // 准点率连续 3 季 < 70%（行政助理）
    }

    /// <summary>单条智能提示。</summary>
    public class SmartAlert
    {
        public SmartAlertType type;
        public string advisor;   // 关联顾问
        public string title;
        public string body;
        public string deptId;
        public string deptName;
    }

    /// <summary>
    /// 智能提示系统数值判定（纯 C#，GDB 28.4）。
    /// 输入为原始指标，输出触发列表，供 Unity（SmartAlertManager）与 tools/balance 共用。
    /// </summary>
    public static class SmartAlertLogic
    {
        /// <summary>现金跑道告急阈值（月）。</summary>
        public const float CashRunwayMonths = 4f;

        /// <summary>飞行员/关键岗位空缺率阈值（0~1）。</summary>
        public const float VacancyThreshold = 0.15f;

        /// <summary>战略一致性下限。</summary>
        public const float StrategyConsistencyFloor = 50f;

        /// <summary>准点率恶化阈值（%）。</summary>
        public const float OnTimeFloor = 70f;

        /// <summary>准点率连续低于阈值的季度数。</summary>
        public const int OnTimeLowQuarters = 3;

        public static List<SmartAlert> EvaluateAll(
            float runwayMonths, float vacancyRate, float strategyConsistency,
            float onTimePercent, int onTimeLowQuarters)
        {
            var list = new List<SmartAlert>();

            if (runwayMonths >= 0f && runwayMonths < CashRunwayMonths)
            {
                list.Add(new SmartAlert
                {
                    type = SmartAlertType.CashRunway,
                    advisor = "财务顾问",
                    title = "现金储备告急",
                    body = $"老板，账上现金只够撑 {runwayMonths:F1} 个月了。建议立刻削减非核心航线运力，或启动过桥贷款谈判。",
                    deptId = "n15_财务部",
                    deptName = "财务部"
                });
            }

            if (vacancyRate > VacancyThreshold)
            {
                list.Add(new SmartAlert
                {
                    type = SmartAlertType.PilotShortage,
                    advisor = "人力资源顾问",
                    title = "飞行员严重短缺",
                    body = $"机长缺口已达 {vacancyRate * 100f:F0}%。若再不启动外部招聘或加速副驾晋升，下季度至少 10% 的航班将无人执飞。",
                    deptId = "n18_人力资源部",
                    deptName = "人力资源部"
                });
            }

            if (strategyConsistency < StrategyConsistencyFloor)
            {
                list.Add(new SmartAlert
                {
                    type = SmartAlertType.StrategyDrift,
                    advisor = "战略顾问",
                    title = "战略严重偏离",
                    body = $"公司各部门执行已经四分五裂（战略一致性 {strategyConsistency:F0} 分）。建议立刻召开战略校准会议，否则董事会将质疑您的领导力。",
                    deptId = "n16_战略发展部",
                    deptName = "战略发展部"
                });
            }

            if (onTimeLowQuarters >= OnTimeLowQuarters)
            {
                list.Add(new SmartAlert
                {
                    type = SmartAlertType.OnTimeDecline,
                    advisor = "行政助理",
                    title = "准点率持续恶化",
                    body = $"OPS 部门效率在崩溃边缘（准点率已连续 {onTimeLowQuarters} 季低于 {OnTimeFloor:F0}%）。连续延误正在严重消耗品牌净值，建议动用 CEO 权限干预排班。",
                    deptId = "n13_运营部",
                    deptName = "运营部"
                });
            }

            return list;
        }
    }
}
