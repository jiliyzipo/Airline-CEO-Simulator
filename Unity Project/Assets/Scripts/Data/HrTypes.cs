using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Data
{
    // ============================================================
    // HR 人力资源部（Phase 2 Week 10）
    // ============================================================

    /// <summary>全球航空人才紧缺指数（n18/n112）。</summary>
    [Serializable]
    public class TalentIndex
    {
        public float pilotShortage = 60f;      // 飞行员紧缺度 0~100
        public float engineerShortage = 55f;   // 机务工程师紧缺度 0~100
        public float cabinShortage = 40f;      // 乘务员紧缺度 0~100
    }

    /// <summary>关键岗位空缺（n86/n112）。</summary>
    [Serializable]
    public class Vacancy
    {
        public string position;    // 岗位名称
        public int needed;         // 所需人数
        public int hired;          // 已招人数
        public int urgency;        // 0常规(绿) 1计划(黄) 2紧急(红)
        public int channel;        // 0批量校招 1猎头挖角 2海外引进 3内部转岗
        /// <summary>本期招募发起时刻（UTC ticks）。</summary>
        public long startUtcTicks;
        /// <summary>本期候选人到位时刻（UTC ticks）= 发起 + 渠道周期（周）。旧档为 0，视作立即到位。</summary>
        public long dueUtcTicks;
        /// <summary>已落池、尚待 CEO 决定的候选人数（避免重复生成）。</summary>
        public int inPool;
    }

    /// <summary>培训项目（n87）。</summary>
    [Serializable]
    public class TrainingProgram
    {
        public string name;          // 项目名
        public int enrolled;         // 在培人数（== memberIds.Count；旧档无名单时保留原值）
        public float completionRate; // 完成率 0~1
        public float costWan;        // 成本（万美元）
        /// <summary>people / maint / safety / type_rating / cert</summary>
        public string track = "people";
        public bool graduated;
        /// <summary>
        /// 在培人员名单。此前只有 enrolled 这个整数，玩家既看不到谁在培训，
        /// 也无法指定个人——选人发生在结业那一刻（TrainingGrowthLogic.PickTargets）。
        /// 现改为入册时选人并记录在此；为空表示旧档记录，结业时仍走旧逻辑。
        /// </summary>
        public List<string> memberIds = new List<string>();
        /// <summary>track == "cert" 时：本次要考取的证照 id（见 CertificationCatalog）。</summary>
        public string certId;
        /// <summary>入册时刻（UTC ticks），用于「在培 N 天」展示。</summary>
        public long enrolledTicks;
        /// <summary>
        /// 计划季度数；&gt;0 时每季完成率增量固定为 1/durationQuarters（证照考取按学时折算），
        /// 0 表示沿用中心等级的默认速度（TrainingProgressDelta）。
        /// </summary>
        public int durationQuarters;
    }

    /// <summary>工会与劳资关系（n37）。</summary>
    [Serializable]
    public class UnionState
    {
        public float satisfaction = 60f;  // 工会满意度 0~100
        public int strikeCountdown = 9;   // 罢工倒计时（月），<=0 触发罢工
        public int cbaExpiresIn = 12;     // 集体谈判协议到期（月）
    }

    /// <summary>人力资源部聚合状态（Phase 2 Week 10 · HR）。</summary>
    [Serializable]
    public class HrState
    {
        public float employeeSatisfaction = 72f;  // 员工满意度 0~100
        /// <summary>
        /// 【遗留】旧版固定总人数锚点（5 机基准 450 人）。已不参与任何计算，仅作旧档迁移来源：
        /// 首次 <see cref="AirlineCEO.Systems.HrManager.EnsureState"/> 时按当时在役机队折算为每机人数。
        /// 现值请读 <see cref="AirlineCEO.Systems.GameState.TotalHeadcount"/>。
        /// </summary>
        public int totalHeadcount = AirlineCEO.Logic.HrEconomics.DefaultHeadcount;
        /// <summary>
        /// 每架在役飞机的人数（默认 90）。总人数 = 本值 × 在役机队 + <see cref="headcountAdditive"/>，
        /// 与「人力成本 = 每机费率 × 在役机队」同构，故两者随机队同步缩放。
        /// </summary>
        public float headcountPerAircraft = AirlineCEO.Logic.HrEconomics.HeadcountPerAircraft;
        /// <summary>
        /// 一次性人数增量（渠道招聘到岗者）。继承在册机组已含在每机人数口径内，故不计入此处。
        /// </summary>
        public int headcountAdditive;
        /// <summary>旧档迁移标记：把遗留 <see cref="totalHeadcount"/> 折算为每机人数后置 true。</summary>
        public bool headcountMigrated;
        public float vacancyRate = 0.08f;          // 空缺率 0~1
        public TalentIndex talentIndex = new TalentIndex();
        public float strikeRisk = 15f;             // 罢工风险指数 0~100
        public float trainingRoi = 1.8f;           // 培训ROI（绩效增幅倍数）
        /// <summary>
        /// 【遗留】旧版存量人力成本锚点（万美元/年）。已不参与现金口径，仅作旧档迁移来源：
        /// 首次 <see cref="AirlineCEO.Systems.HrManager.EnsureState"/> 时按当时在役机队折算成每机费率。
        /// </summary>
        public float laborCostWan = AirlineCEO.Logic.HrEconomics.DefaultAnnualLaborCostWan;
        /// <summary>
        /// 每架在役飞机的人力费率（万美元/机/年，默认 240）。年度人力成本 =
        /// 本费率 × 在役机队 + <see cref="laborCompAdditiveWan"/>，随在役机队自动缩放。
        /// 年度调薪按比例放大本值（而非锁定总额），这样扩张后的新飞机也按加薪后的费率计提。
        /// </summary>
        public float laborCostPerAircraftWan = AirlineCEO.Logic.HrEconomics.LaborCostPerAircraftWan;
        /// <summary>一次性加薪的年度增量（万美元/年），如高管长期激励。</summary>
        public float laborCompAdditiveWan;
        /// <summary>旧档迁移标记：把遗留 <see cref="laborCostWan"/> 折算为每机费率后置 true。</summary>
        public bool laborRateMigrated;
        public float trainingBudgetWan = 500f;     // 年度培训预算（万美元）
        /// <summary>已建成并生效的培训中心等级（0外包 1自建 2综合基地 3航司大学）。施工中的等级不在此。</summary>
        public int trainingCenterLevel;            // 0外包 1自建 2综合基地 3航司大学
        /// <summary>在建目标等级；0 表示当前没有工程。完工时写入 <see cref="trainingCenterLevel"/>。</summary>
        public int trainingCenterBuildLevel;
        /// <summary>在建工程完工时刻（UTC ticks）。<see cref="trainingCenterBuildLevel"/> 为 0 时无意义。</summary>
        public long trainingCenterBuildDoneTicks;
        public List<Vacancy> vacancies = new List<Vacancy>();
        public List<TrainingProgram> trainings = new List<TrainingProgram>();
        public UnionState union = new UnionState();
        /// <summary>最近一期结业回写摘要（培训页 / 人员 KPI）。</summary>
        public string lastTrainingNote = "";
        /// <summary>最近一次薪酬方案说明（n88）。</summary>
        public string lastCompNote = "";
    }

    [Serializable]
    public class HrSave
    {
        public HrState state;
    }
}
