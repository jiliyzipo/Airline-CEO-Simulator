using System;
using System.Collections.Generic;
using AirlineCEO.Logic;

namespace AirlineCEO.Data
{
    /// <summary>
    /// 持证记录：某人持有的某一本证照。此前全项目没有按人的资质概念
    /// （唯一的「国际运行资质」是二元判定，见 RoutePermitLogic.HasOverseasQualification）。
    /// </summary>
    [Serializable]
    public class CertHeld
    {
        /// <summary>证照 id，见 CertificationCatalog。</summary>
        public string id;
        /// <summary>发证时刻（UTC ticks）。</summary>
        public long issuedTicks;
        /// <summary>到期时刻（UTC ticks）；0 表示长期有效。</summary>
        public long expiresTicks;
    }

    /// <summary>
    /// 统一人事档案：负责人与员工同构（rank 区分）。负责人 ScriptableObject 可映射到此结构。
    /// </summary>
    [Serializable]
    public class PersonnelData
    {
        public string id;
        public string fullName;
        public int age;
        public string nationality;
        public string traitsCsv;
        public float baseSalary;
        public float loyalty = 70f;
        public float efficiencyBonus = 1f;
        public string department;
        /// <summary>实习生 / 专员 / 主管 / 副总监 / 总监</summary>
        public string rank = "专员";
        public string jobTitle = "";
        public float potential = 0.5f;
        /// <summary>在职业介绍所待聘（非本公司在职管理岗）。</summary>
        public bool onMarket;
        /// <summary>
        /// 来源渠道：market / campus / headhunt / overseas / internal。
        /// 旧档读出可能为 null/空，一律按 market（人才市场）处理，见 HrEconomics.NormalizeSource。
        /// </summary>
        public string source = "market";
        /// <summary>内部转岗交接期截止（UTC ticks）。期间效率打折，见 EfficiencyAt。</summary>
        public long handoverUntilTicks;
        /// <summary>
        /// 运行/职能序列的批量招聘人员（渠道候选人或到岗者）。
        /// 他们不占编制表名额，也不参与 DeptStaffLift 的「在职关键岗平均效率」统计——
        /// 否则批量招飞行员会通过稀释均值把部门 KPI 附加拉低，改变改动前的数值行为。
        /// </summary>
        public bool isOperational;

        /// <summary>
        /// 持证记录（飞行员执照 / 体检合格证 / 机务执照 / 国际运行资质 …）。
        /// 旧档读出为 null，一律经 <see cref="CertificationLogic.EnsureCerts"/> 归一为空表。
        /// </summary>
        public List<CertHeld> certs = new List<CertHeld>();

        /// <summary>内部转岗交接期时长（天）。权威值在 HrEconomics，此处转发避免双写漂移。</summary>
        public const int HandoverDays = HrEconomics.HandoverDays;
        /// <summary>交接期内战力系数（原部门编制缺口的对价）。</summary>
        public const float HandoverEfficiencyScale = HrEconomics.HandoverEfficiencyScale;

        /// <summary>按指定时刻折算的效率加成：交接期内打折。</summary>
        public float EfficiencyAt(long nowTicks)
            => handoverUntilTicks > nowTicks ? efficiencyBonus * HandoverEfficiencyScale : efficiencyBonus;

        public static PersonnelData FromExecutive(ExecutiveData e)
        {
            if (e == null) return null;
            string traits = "";
            if (e.traits != null && e.traits.Length > 0)
                traits = string.Join("、", e.traits);
            float pot = 0.62f + (e.efficiencyBonus - 1f) * 0.25f;
            if (pot < 0.45f) pot = 0.45f;
            if (pot > 0.95f) pot = 0.95f;
            return new PersonnelData
            {
                id = e.id,
                fullName = e.fullName,
                age = e.age,
                nationality = e.nationality,
                traitsCsv = traits,
                baseSalary = e.baseSalary,
                loyalty = e.loyalty,
                efficiencyBonus = e.efficiencyBonus,
                department = e.department,
                rank = "负责人",
                jobTitle = "总监",
                potential = pot,
                onMarket = true
            };
        }
    }

    [Serializable]
    public class PersonnelListSave
    {
        public PersonnelData[] items;
        public bool bootstrapped;
    }
}
