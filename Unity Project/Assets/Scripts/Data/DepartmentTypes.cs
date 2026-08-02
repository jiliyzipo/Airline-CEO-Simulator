using System;

namespace AirlineCEO.Data
{
    /// <summary>七大部门定义与 KPI（GDD 6.2 · Phase 8）。</summary>
    public static class DepartmentCatalog
    {
        public static readonly string[] Codes =
        {
            "OPS", "MNT", "MKT", "FIN", "HR", "SAF", "STG"
        };

        public static string DisplayName(string code)
        {
            return code switch
            {
                "OPS" => "运营部",
                "MNT" => "机务工程部",
                "MKT" => "市场销售部",
                "FIN" => "财务部",
                "HR" => "人力资源部",
                "SAF" => "安全质量部",
                "STG" => "战略发展部",
                _ => code
            };
        }

        public static string[] KpiLabels(string code)
        {
            return code switch
            {
                "OPS" => new[] { "准点率 ≥ 85%", "航班完成率 ≥ 95%", "单班成本可控" },
                "MNT" => new[] { "机队可用率 ≥ 90%", "维修预算不超支", "AOG 次数少" },
                "MKT" => new[] { "客座率 ≥ 75%", "票价策略有效", "品牌口碑稳定" },
                "FIN" => new[] { "现金不为负", "跑道 ≥ 3 个月", "成本可控" },
                "HR" => new[] { "员工满意度", "关键岗位满编", "罢工风险低" },
                "SAF" => new[] { "安全评级 ≥ B", "重大事故为 0", "审计通过" },
                "STG" => new[] { "新开航线进展", "航权成功率", "战略一致性" },
                _ => new[] { "KPI-1", "KPI-2", "KPI-3" }
            };
        }
    }

    [Serializable]
    public class DepartmentHeadSave
    {
        public string deptCode;
        public string headId;
        public float loyalty;
        public float salary;
        public float efficiency;
        public bool kpiMetLastQuarter;
        public float kpiBonus; // 0 或 0.05
    }

    [Serializable]
    public class DepartmentHeadsBlob
    {
        public DepartmentHeadSave[] items;
        public string[] removedIds;
        public string[] activeChemistry;
    }
}
