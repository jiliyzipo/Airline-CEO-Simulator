using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// Phase 2 Week 10 · HR 人力资源部数值引擎（纯 C#，Unity 与 tools/balance 共用）。
    /// 覆盖：招聘成本/成功率/周期、人力成本占比、流失率、培训ROI、工会谈判。
    /// 单位约定：货币「万美元」；百分比按 0~1（0.08 表示 8%）。
    /// </summary>
    public static class HrEconomics
    {
        // ---- 渠道来源标签（写入 PersonnelData.source，供职业介绍所筛选）----

        public const string SourceMarket = "market";
        public const string SourceCampus = "campus";
        public const string SourceHeadhunt = "headhunt";
        public const string SourceOverseas = "overseas";
        public const string SourceInternal = "internal";
        /// <summary>
        /// 接管局继承的在册机组（由 PersonnelGenerator.SeedInheritedCrew 生成）。
        /// 语义与 internal 同族：**薪酬已含在「存量人力成本」聚合口径内**（该聚合按 240 万/机/年
        /// 覆盖 450 人规模的既有组织），故不得再进 OperationalPayrollMonthlyWan。
        /// 同时它**不是**海外引进渠道，故不计入 OverseasCrewCount（国际航线准入仍须海外引进或考取 intl_ops）。
        /// </summary>
        public const string SourceInherited = "inherited";

        /// <summary>渠道编号 → 来源标签。</summary>
        public static string SourceTag(int channel) => channel switch
        {
            0 => SourceCampus,
            1 => SourceHeadhunt,
            2 => SourceOverseas,
            _ => SourceInternal
        };

        /// <summary>
        /// 来源标签 → 渠道编号；**非渠道来源**（人才市场 / 继承在册）返回 -1。
        /// 与 <see cref="SourceTag"/> 在 0..3 上严格互逆，是「来源筛选 = 招聘渠道」的唯一换算点。
        ///
        /// 此前 UI 用「页签下标 − 1」推渠道号（`ChannelForTab`），把渠道编号**绑死在页签顺序**上：
        /// 页签一增删就与渠道号静默错位（如把「待聘总监」从下表挪走，校招就会变成 −1）。
        /// 现在页签已改为「来源」芯片，换算收敛到这里。
        /// </summary>
        public static int ChannelOfTag(string tag) => NormalizeSource(tag) switch
        {
            SourceCampus => 0,
            SourceHeadhunt => 1,
            SourceOverseas => 2,
            SourceInternal => 3,
            _ => -1
        };

        /// <summary>来源标签 → 中文名（职业介绍所筛选栏 / 来源列）。</summary>
        public static string SourceName(string tag) => NormalizeSource(tag) switch
        {
            SourceCampus => "校招",
            SourceHeadhunt => "猎头挖角",
            SourceOverseas => "海外引进",
            SourceInternal => "内部转岗",
            SourceInherited => "继承在册",
            _ => "人才市场"
        };

        /// <summary>旧档 source 为 null/空时按「人才市场」处理。</summary>
        public static string NormalizeSource(string tag)
            => string.IsNullOrEmpty(tag) ? SourceMarket : tag;

        // ============================================================
        // 渠道属性分层（纯 C#）
        // 唯一权威来源：游戏 PersonnelGenerator.ApplySourceProfile 与
        // tools/balance 渠道收益分析共用同一批倍率，杜绝分叉。
        // 倍率语义：相对「人才市场」基准（专员：潜力 0.88 / 忠诚 68 / 战力 0.48 / 年薪 8 万）。
        // ============================================================

        /// <summary>
        /// 取某来源的属性倍率（潜力 / 年薪 / 忠诚 / 战力）。
        /// 人才市场与内部转岗返回全 1.0（内部转岗不改变人的能力，只改变归属与交接状态）。
        /// </summary>
        public static void SourceStatMultipliers(string tag, out float pot, out float pay, out float loy, out float eff)
        {
            switch (NormalizeSource(tag))
            {
                case SourceCampus:   pot = 0.85f; pay = 0.75f; loy = 1.10f; eff = 0.80f; break;  // 便宜、慢、要养
                case SourceHeadhunt: pot = 1.15f; pay = 1.45f; loy = 0.90f; eff = 1.10f; break;  // 快、贵、易被再挖
                case SourceOverseas: pot = 1.05f; pay = 1.30f; loy = 0.95f; eff = 1.10f; break;  // 稀缺来源，有适应期
                default:             pot = 1.00f; pay = 1.00f; loy = 1.00f; eff = 1.00f; break;  // 市场 / 内部转岗：基准
            }
        }

        /// <summary>专员级基准属性（PersonnelGenerator.Make 的专员档），供离线模拟复现落池人物。</summary>
        public const float BaseSpecialistPotential = 0.88f;
        public const float BaseSpecialistSalary = 8f;
        public const float BaseSpecialistLoyalty = 68f;
        public const float BaseSpecialistEfficiency = 0.48f;

        /// <summary>属性 clamp 上下限（与 PersonnelGenerator 保持一致）。</summary>
        public const float PotentialMin = 0.30f, PotentialMax = 0.98f;
        public const float LoyaltyMin = 20f, LoyaltyMax = 100f;
        public const float EfficiencyMin = 0.30f, EfficiencyMax = 1.35f;

        /// <summary>
        /// 按来源倍率折算某人的一组属性（纯函数，无副作用）。
        /// 游戏侧与离线模拟侧都调它，保证「同一个来源 = 同一种人」。
        /// </summary>
        public static void ApplySourceStats(string tag,
            float potIn, float payIn, float loyIn, float effIn,
            out float potOut, out float payOut, out float loyOut, out float effOut)
        {
            SourceStatMultipliers(tag, out float mp, out float my, out float ml, out float me);
            potOut = Clamp(potIn * mp, PotentialMin, PotentialMax);
            payOut = Math.Max(1f, payIn * my);
            loyOut = Clamp(loyIn * ml, LoyaltyMin, LoyaltyMax);
            effOut = Clamp(effIn * me, EfficiencyMin, EfficiencyMax);
        }

        /// <summary>内部转岗交接期（天）与战力系数（与 PersonnelData 常量对齐）。</summary>
        public const int HandoverDays = 14;
        public const float HandoverEfficiencyScale = 0.7f;
        /// <summary>内部转岗调岗薪资上浮（表格「期望年薪」显示的增量来源）。</summary>
        public const float InternalTransferRaise = 0.20f;

        /// <summary>
        /// 人才紧缺指数 → 招聘成本系数。让 n112「过滤 · 招聘条件」的「成本 ×系数」文案真正生效
        /// （此前 RecruitCostPerHire 不接收紧缺度，该文案是装饰性的）。
        /// </summary>
        public static float ShortageCostFactor(float shortageIndex) =>
            shortageIndex >= 70f ? 1.25f : shortageIndex >= 40f ? 1.10f : 0.90f;

        /// <summary>
        /// 招聘渠道人均成本（美元/人），按人才紧缺度上浮/下浮。
        /// 内部转岗不收外部招聘费（0）：其代价是原部门编制缺口 + 交接期战力打折，见 HrManager.TransferStaff。
        /// </summary>
        public static float RecruitCostPerHire(int channel, float shortageIndex = 0f)
        {
            float baseCost = channel switch
            {
                0 => 2000f,   // 批量校招
                1 => 12000f,  // 猎头挖角
                2 => 9000f,   // 海外引进
                _ => 0f       // 内部转岗：无外部费用，代价转移到编制缺口与交接期
            };
            return baseCost * ShortageCostFactor(shortageIndex);
        }

        /// <summary>招聘成功率（0~1）：渠道基线 − 紧缺惩罚。</summary>
        public static float RecruitSuccessRate(int channel, float shortageIndex)
        {
            float baseRate = channel switch { 0 => 0.78f, 1 => 0.50f, 2 => 0.40f, _ => 0.85f };
            float penalty = Clamp01(shortageIndex / 100f) * 0.25f;
            return Clamp(baseRate - penalty, 0.05f, 0.95f);
        }

        /// <summary>
        /// 招聘到岗周期（周）。现由 HrManager.TickDay 用来决定候选人落池时刻
        /// （此前仅用于 n112 招聘按钮文案，不参与任何时间推进）。
        /// </summary>
        public static float RecruitLeadWeeks(int channel) => channel switch { 0 => 24f, 1 => 4f, 2 => 12f, _ => 2f };

        // ============================================================
        // 运行岗编制（渠道招聘岗位的配额上限）
        //
        // 为什么需要：「飞行员」等运行岗不在 StaffEstablishment.Roles 里，
        // 因此 Vacancy.needed 完全由玩家口径决定且可无限累加，与机队规模毫无挂钩。
        // 编制按机队规模推导后，「缺口」才成为招聘的真实驱动力。
        // ============================================================

        /// <summary>窄体机每架所需飞行员（4 组 × 2 人，含轮休/备份）。</summary>
        public const float PilotsPerAircraft = 8f;
        /// <summary>宽体机额外飞行员（长程增援机组为 4 人制，见 OpsRotationLogic）。</summary>
        public const float WidebodyExtraPilots = 2f;
        /// <summary>每架飞机所需机务（航线维护 + 定检）。</summary>
        public const float EngineersPerAircraft = 6f;
        /// <summary>每 50 座每航班所需乘务员，再按 4 组轮班展开。</summary>
        public const float CabinCrewPerFiftySeats = 4f;
        public const int SeatsPerCabinCrewUnit = 50;
        /// <summary>每架飞机的备份乘务（宽体客舱服务标准更高）。</summary>
        public const float CabinCrewPerAircraft = 4f;

        /// <summary>
        /// 运行岗编制（该岗位应配人数）。岗位名用关键字判定，与 HrManager.DeptForPosition 同口径。
        /// </summary>
        /// <param name="position">岗位名（飞行员 / 乘务员 / 机务工程师 …）</param>
        /// <param name="activeFleet">在役机队架数</param>
        /// <param name="widebodyCount">其中宽体机架数</param>
        /// <param name="totalSeats">机队总座位数</param>
        public static int OperationalEstablishment(string position, int activeFleet, int widebodyCount, int totalSeats)
        {
            if (activeFleet < 0) activeFleet = 0;
            if (widebodyCount < 0) widebodyCount = 0;
            if (totalSeats < 0) totalSeats = 0;
            if (activeFleet == 0) return 0;

            switch (OperationalCategory(position))
            {
                case OpsCategory.Pilot:
                    return (int)Math.Round(activeFleet * PilotsPerAircraft + widebodyCount * WidebodyExtraPilots);
                case OpsCategory.CabinCrew:
                    return (int)Math.Round(totalSeats / (float)SeatsPerCabinCrewUnit * CabinCrewPerFiftySeats
                                            + activeFleet * CabinCrewPerAircraft);
                case OpsCategory.Engineer:
                    return (int)Math.Round(activeFleet * EngineersPerAircraft);
                default:
                    return 0;   // 其余岗位不设运行编制（走 StaffEstablishment 编制席管线）
            }
        }

        /// <summary>运行岗类别（由岗位名关键字判定）。</summary>
        public enum OpsCategory
        {
            None = 0,
            Pilot,
            CabinCrew,
            Engineer
        }

        /// <summary>
        /// 岗位名 → 运行岗类别。编制推导与在职计数必须共用这一个判定，
        /// 否则两边关键字不一致会让「编制」与「实到」对不上（配额封顶失效或误封）。
        /// </summary>
        public static OpsCategory OperationalCategory(string position)
        {
            string p = position ?? "";
            if (p.Contains("飞行") || p.Contains("机长")) return OpsCategory.Pilot;
            if (p.Contains("乘务") || p.Contains("客舱服务")) return OpsCategory.CabinCrew;
            if (p.Contains("机务") || p.Contains("维修") || p.Contains("工程")) return OpsCategory.Engineer;
            return OpsCategory.None;
        }

        /// <summary>是否运行编制岗（有编制上限，需按机队规模封顶招募）。</summary>
        public static bool IsOperationalEstablishment(string position) =>
            OperationalCategory(position) != OpsCategory.None;

        // 注意：本文件被 tools/balance 以纯 C# 方式链接，不得引用 StaffEstablishment
        //（它会拖入 GameState / DepartmentManager 等 Unity 运行时依赖）。
        // 「岗位名是否编制席」的剔除判定放在运行时层 HrManager.IsCrewPost。

        /// <summary>
        /// 运行岗薪酬 rollup（万美元/年）：把渠道雇来的运行序列人员实发年薪汇总。
        ///
        /// 这是**增量**，不是替换：GameState.LaborCostAnnualWan（每机费率 × 在役机队，5 机基准 1200 万/年，见
        /// <see cref="DefaultAnnualLaborCostWan"/>）是既有聚合口径，
        /// 覆盖了未被逐人建模的全体员工；运行岗是聚合之外的新增编制，故只能叠加。
        /// 若不叠加，猎头年薪比校招高 93% 就毫无后果——多付的钱买不到任何东西。
        /// </summary>
        public static float OperationalPayrollWan(System.Collections.Generic.IList<float> annualSalariesWan)
        {
            if (annualSalariesWan == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < annualSalariesWan.Count; i++)
            {
                float v = annualSalariesWan[i];
                if (v > 0f) sum += v;
            }
            return sum;
        }

        /// <summary>年薪（万美元）→ 月成本（万美元）。</summary>
        public static float AnnualToMonthlyWan(float annualWan) => annualWan < 0f ? 0f : annualWan / 12f;

        /// <summary>
        /// 每架在役飞机分摊的存量人力成本（万美元/机/年）。人力随规模走：机队越大，
        /// 机组/乘务/地面/机务编制越多。锚点 240 → 5 机基准 1200 万/年，
        /// 对 5 机基准航司年营收 ~4660 万即约 26%（贴着「&lt;25%」目标）；
        /// 3 机支线/包机约 21~30%，同样落在真实区间。
        /// </summary>
        public const float LaborCostPerAircraftWan = 240f;

        /// <summary>
        /// 全公司存量人力成本默认值（万美元/年，5 机基准）：覆盖未被逐人建模的员工。
        ///
        /// 量级锚点：设计目标「人力成本占营收 &lt;25%」。5 机基准航司年营收约 4660 万，
        /// 1200 万 ≈ 25.8%，落在目标附近。字段口径为「万美元」——早期默认 12000 系量级笔误
        /// （会让占比显示约 258%，且该字段从不扣现金），现修正为 1200 并真实计入固定成本。
        /// </summary>
        public const float DefaultAnnualLaborCostWan = LaborCostPerAircraftWan * 5f;

        /// <summary>
        /// 存量聚合口径的每机标准人数（90 人/机）。锚点与 <see cref="DefaultAnnualLaborCostWan"/> 同源：
        /// 5 机基准 ↔ 450 人 ↔ 1200 万美元/年（人均约 2.67 万美元/年）。
        /// </summary>
        public const float HeadcountPerAircraft = 90f;

        /// <summary>旧档遗留的总人数锚点（5 机基准 450 人），仅作 <c>HrState.totalHeadcount</c> 的字段初值。</summary>
        public const int DefaultHeadcount = 450;

        /// <summary>
        /// 年度人力成本（万美元）→ 月度现金成本（万美元/月）。
        /// 与 <see cref="OperationalPayrollWan"/> 互补、不重叠：后者只算渠道雇来的运行序列**增量**编制。
        /// </summary>
        public static float MonthlyLaborCostWan(float annualLaborWan) => AnnualToMonthlyWan(annualLaborWan);

        /// <summary>按在役机队规模推导的存量人力成本（万美元/年）。</summary>
        public static float EstablishmentLaborCostWan(int activeAircraftCount)
        {
            if (activeAircraftCount < 0) activeAircraftCount = 0;
            return LaborCostPerAircraftWan * activeAircraftCount;
        }

        /// <summary>
        /// 存量人力成本（万美元/年）：每机费率 × 在役机队 + 一次性增量。
        ///
        /// 机队扩张/退役时自动随之缩放；年度调薪通过放大 <paramref name="perAircraftWan"/> 生效
        /// （而非锁定总额），故补偿决策不会因机队变动而丢失，新飞机也按加薪后的费率计提。
        /// </summary>
        public static float EffectiveAnnualLaborCostWan(int activeAircraftCount, float perAircraftWan, float additiveWan)
        {
            if (activeAircraftCount < 0) activeAircraftCount = 0;
            float rate = perAircraftWan > 0f ? perAircraftWan : LaborCostPerAircraftWan;
            float total = rate * activeAircraftCount + (additiveWan > 0f ? additiveWan : 0f);
            return total < 0f ? 0f : total;
        }

        /// <summary>
        /// 全公司总人数（派生）= 每机人数 × 在役机队 + 一次性增量。
        ///
        /// 与 <see cref="EffectiveAnnualLaborCostWan"/> **同一套口径形状**（费率 × 规模 + 增量），
        /// 故人数与人力成本随机队同步缩放，不会出现「成本随规模走、人数却钉死」的分叉。
        /// 此前 <c>hr.totalHeadcount</c> 是默认 450 的固定值，扩机队后人数不涨，
        /// 使 <see cref="TrainingRoi"/>（按人均预算算）与 <see cref="UnionCostWan"/>（按人头计费）
        /// 在大小航司之间严重失真：20 架机队的「让步」成本会被低报约 4 倍。
        /// </summary>
        /// <param name="perAircraftHeads">每机人数；&lt;=0 时回退到 <see cref="HeadcountPerAircraft"/>。</param>
        public static int EffectiveHeadcount(int activeAircraftCount, float perAircraftHeads, int additiveHeadcount)
        {
            if (activeAircraftCount < 0) activeAircraftCount = 0;
            float rate = perAircraftHeads > 0f ? perAircraftHeads : HeadcountPerAircraft;
            int baseN = (int)System.Math.Round(rate * activeAircraftCount);
            int add = additiveHeadcount > 0 ? additiveHeadcount : 0;
            int total = baseN + add;
            return total < 0 ? 0 : total;
        }

        // ============================================================
        // 运行岗战力 → 运营后果（让 efficiencyBonus 差真正生效）
        //
        // 现状：渠道到岗者 isOperational=true 被 DeptStaffLift 排除，而 DeptStaffLift 是
        // efficiencyBonus 的唯一聚合消费点 → 各渠道战力差（猎头 0.528 vs 校招 0.384）毫无后果，
        // 玩家多付 7.5× 的猎头费买不到任何东西。
        //
        // 设计取舍（重要，2026-10-07 修订）：
        //
        // 曾试图用「不招 = 1.00 中性」来保持开局数值基线不变，但这造成了一个反向激励漏洞：
        //   0 名机组 → 1.000（免费满利润）
        //   校招机组 → 0.952（战力 0.384 低于达标线 0.48，被判折损）
        // 于是「永不雇机组」严格支配「雇校招机组」——所有渠道都是净亏损，渠道系统形同虚设。
        // （tools/balance --channels 的 Part B 把这个倒挂跑了出来，见 channel_report.txt。）
        //
        // 修订后的不变量（由 HrSafPlayModeTests 锁定）：
        //   未建机组的乘数  <  任何已建机组的乘数
        // 即 CrewUnstaffedMultiplier < CrewQualityMin <= CrewQualityMultiplier(filled>0, anyEff)。
        // 这条不等式是**结构性成立**的，不依赖调参运气：
        //   EfficiencyMin = 0.30 → 最烂机组 delta = -0.18 → 乘数 0.91，仍高于 CrewQualityMin 0.88。
        // 于是「招任何人」都优于「不招人」，同时「招好渠道」仍优于「招差渠道」——
        // 两级激励（要不要招 / 招哪个渠道）都成立，且不存在「招 1 人反而更亏」的陷阱区。
        //
        // 刻意**不**把编制覆盖率作为**惩罚因子乘**在质量上：
        //   若乘数 = 覆盖率因子 × 质量因子，则低覆盖 + 低战力（校招）会出现
        //   「招几个人比一个人不招更亏」的陷阱区（需覆盖 >37% 才回本）。
        //
        // 2026-10-08 修订（接管局口径修复）：覆盖率改为**在保底与质量之间线性插值**：
        //   乘数 = CrewUnstaffedMultiplier + (质量乘数 − CrewUnstaffedMultiplier) × 覆盖率
        // 它同样没有陷阱区（对人数单调不减，且 filled>0 一律严格优于 0），却让
        // crewVacancyPercent（「机组人员短缺 X%」）这个此前纯装饰的字段有了真实代价——
        // 此前接管局继承的 8 条航线被当成「一架机组都没有」，无条件吃满 0.85 折损，
        // 而叙事层却写着「机组短缺 15%」（两套口径分叉，见 TakeoverWorldBuilder）。
        // ============================================================

        /// <summary>机组战力达标线（= 专员基准战力）。达此值乘数为 1.00，即中性。</summary>
        public const float CrewAdequacyBaseline = BaseSpecialistEfficiency;
        /// <summary>战力每高于达标线 0.10 带来的利润增益。</summary>
        public const float CrewQualityGainPerTenth = 0.04f;
        /// <summary>战力每低于达标线 0.10 带来的利润折损（略重于增益，抑制纯低价堆人）。</summary>
        public const float CrewQualityLossPerTenth = 0.05f;
        /// <summary>乘数上下限，防止极端属性把航线利润打飞。</summary>
        public const float CrewQualityMin = 0.88f;
        public const float CrewQualityMax = 1.10f;

        /// <summary>
        /// 未建立机组时的乘数（航线日利润折损）。
        ///
        /// 必须严格小于 CrewQualityMin，这样「招任何人」都优于「一个人不招」——
        /// 否则「不招人」会成为免费满利润的最优解，渠道系统形同虚设（见上方设计取舍）。
        /// 该不等式由 HrSafPlayModeTests.CrewQuality_UnstaffedIsStrictlyWorstThanAnyCrew 锁定。
        /// </summary>
        public const float CrewUnstaffedMultiplier = 0.85f;

        /// <summary>
        /// 机组战力就绪度（0~1）：实到 / 编制。编制为 0 时视作齐备（该岗位无需求）。
        /// 仅用于 UI 呈现缺口，不参与惩罚。
        /// </summary>
        public static float CrewCoverage(int filled, int establishment)
        {
            if (establishment <= 0) return 1f;
            return Clamp01(filled / (float)establishment);
        }

        /// <summary>编制缺口人数（>=0），供 UI 与招聘建议呈现。</summary>
        public static int CrewGapHeadcount(int filled, int establishment)
        {
            int gap = establishment - filled;
            return gap < 0 ? 0 : gap;
        }

        /// <summary>
        /// 机组战力质量乘数（施加于航线日利润）。
        ///
        /// filled &lt;= 0 → CrewUnstaffedMultiplier（&lt; CrewQualityMin）：未建机组是**最差**情形，
        /// 而非免费的中性基线，否则「永不招人」会严格支配「招校招机组」（见上方设计取舍）。
        /// 否则以 avgEfficiencyBonus 相对达标线的偏离折算增益/折损，夹在
        /// [CrewQualityMin, CrewQualityMax] 内。
        ///
        /// 本重载**不含覆盖率**（等价于编制齐备），故返回值对任意 filled&gt;0 都
        /// &gt;= CrewQualityMin &gt; CrewUnstaffedMultiplier —— 「招任何人」严格优于「不招人」
        /// 由末尾的 Clamp 结构性保证，与战力取值无关。
        /// 若要考虑缺编，用三参重载：那时乘数会落在
        /// (CrewUnstaffedMultiplier, 质量乘数] 区间内，但**严格大于**保底值这一条不变。
        /// </summary>
        /// <param name="filled">在职机组人数</param>
        /// <param name="avgEfficiencyBonus">在职机组平均战力（efficiencyBonus，含交接期打折）</param>
        public static float CrewQualityMultiplier(int filled, float avgEfficiencyBonus)
            => CrewQualityMultiplier(filled, 0, avgEfficiencyBonus);

        /// <summary>
        /// 带编制覆盖率的机组乘数（GameState 用此重载）。
        ///
        /// 语义：<c>乘数 = CrewUnstaffedMultiplier + (质量乘数 − CrewUnstaffedMultiplier) × 覆盖率</c>。
        /// 三个性质缺一不可：
        ///   ① filled=0 → 恰好 CrewUnstaffedMultiplier（保底最劣）；
        ///   ② filled&gt;0 → 严格大于 CrewUnstaffedMultiplier（覆盖率&gt;0），即「招任何人都不亏」；
        ///   ③ 对 filled 单调不减 —— 因此**不存在**「招几个人比不招更亏」的陷阱区。
        ///
        /// 这正是覆盖率**可以**参与乘数的前提：文档否决的是「覆盖率作为独立惩罚因子**乘**在质量上」
        /// （那会让覆盖率 &lt;37% 时多招人反而更亏）；此处是**在保底与质量之间插值**，
        /// 补员方向永远单向变好。于是「机组人员短缺 X%」这个此前纯装饰的字段终于有真实代价。
        ///
        /// establishment &lt;= 0（该岗位无编制需求，或未指定）→ 覆盖率视作 1，退化为纯质量口径，
        /// 与两参重载完全一致。
        /// </summary>
        /// <param name="filled">在职机组人数</param>
        /// <param name="establishment">按在役机队推导的编制人数</param>
        /// <param name="avgEfficiencyBonus">在职机组平均战力（efficiencyBonus，含交接期打折）</param>
        public static float CrewQualityMultiplier(int filled, int establishment, float avgEfficiencyBonus)
        {
            if (filled <= 0) return CrewUnstaffedMultiplier;
            float quality = CrewQualityAt(avgEfficiencyBonus);
            float coverage = CrewCoverage(filled, establishment);
            return CrewUnstaffedMultiplier + (quality - CrewUnstaffedMultiplier) * coverage;
        }

        /// <summary>纯质量口径（不含覆盖率）：以平均战力相对达标线的偏离折算，夹在上下限内。</summary>
        static float CrewQualityAt(float avgEfficiencyBonus)
        {
            // 战力为 NaN（坏档）时按最烂在职机组处理，绝不退回中性：
            // 中性值会重新开出一个「有人但数据坏掉 = 白拿满利润」的漏洞。
            // avgEfficiencyBonus <= 0 无需特判：delta 极负 → mult 被末尾 Clamp 收到 CrewQualityMin。
            if (float.IsNaN(avgEfficiencyBonus)) return CrewQualityMin;

            float delta = avgEfficiencyBonus - CrewAdequacyBaseline;
            float per = delta >= 0f ? CrewQualityGainPerTenth : CrewQualityLossPerTenth;
            float mult = 1f + delta / 0.10f * per;   // delta<0 时自然成为折损
            return Clamp(mult, CrewQualityMin, CrewQualityMax);
        }

        /// <summary>人力成本占营收比（目标 &lt;25%）。</summary>
        public static float LaborCostRatio(float laborCostWan, float revenueWan) =>
            revenueWan <= 0f ? 1f : laborCostWan / revenueWan;

        /// <summary>员工满意度 → 年流失率（0~1）。满意度越高流失越低。</summary>
        public static float AttritionRate(float satisfaction) =>
            Clamp(0.18f - satisfaction / 100f * 0.15f, 0.01f, 0.3f);

        /// <summary>培训ROI（倍数）：人均培训预算越高ROI越高，边际递减。</summary>
        public static float TrainingRoi(float trainingBudgetWan, int headcount)
        {
            float perCapita = headcount <= 0 ? 0f : trainingBudgetWan / headcount;
            return 1f + Math.Min(2f, perCapita / 1.5f);
        }

        /// <summary>每季培训完成率增量。按「已建成」的中心等级取，施工中的等级不参与。</summary>
        public static float TrainingProgressDelta(int centerLevel) =>
            Clamp(0.50f + centerLevel * 0.12f, 0.45f, 0.85f);

        /// <summary>结业后效率加成增量：高潜成长更快。</summary>
        public static float TrainingEfficiencyGain(float potential, int centerLevel) =>
            (0.028f + Clamp01(potential) * 0.075f) * (1f + centerLevel * 0.12f);

        /// <summary>结业后潜力增量（高潜略快，上限由调用方 clamp）。</summary>
        public static float TrainingPotentialGain(float potential) =>
            0.012f + Clamp01(potential) * 0.018f;

        /// <summary>
        /// 专员晋升主管的门槛。
        /// <paramref name="trainingGain"/> 必须是「培训累积增益」= efficiencyBonus − 1，
        /// 而不是 efficiencyBonus 本身：后者的基准是 1.0（乘数），拿它跟 0.55 比会恒真，
        /// 门槛形同虚设（此前的缺陷）。
        /// </summary>
        public static bool CanPromoteFromTraining(string rank, float potential, float trainingGain) =>
            rank == "专员" && potential >= 0.90f && trainingGain >= PromotionTrainingGain;
        /// <summary>晋升所需的培训累积增益（约 3 次结业）。</summary>
        public const float PromotionTrainingGain = 0.20f;

        // ===================== 培训中心（等级 / 造价 / 工期）=====================
        //
        // 等级语义：0 级外包 → 3 级航司大学。此前实现是 `level switch { 1 => 800f, 2 => 1500f, _ => 0f }`，
        // 于是 2 级之后每次升级收费 0 且等级无上限，可无限点击刷 TrainingEfficiencyGain 的
        // (1 + level × 0.12) 乘数——已修：本表在 3 级封顶，越界一律返回正无穷，
        // 使 `gs.cash < cost` 天然拦下，杜绝将来再加分支时重新引入免费升级。

        public const int TrainingCenterMaxLevel = 3;

        /// <summary>中心等级名称。0 级为外包（无自有设施）。</summary>
        public static string TrainingCenterName(int level) => level switch
        {
            0 => "外包培训",
            1 => "自建培训中心",
            2 => "综合培训基地",
            _ => "航司大学"
        };

        /// <summary>升到 <paramref name="level"/> 的建设资金（万美元）。越界返回 +∞，不可支付。</summary>
        public static float TrainingCenterCostWan(int level) => level switch
        {
            1 => 800f,
            2 => 1500f,
            3 => 3000f,
            _ => float.PositiveInfinity
        };

        /// <summary>升到 <paramref name="level"/> 的建设工期（天）。此前的实现完全没有工期。</summary>
        public static int TrainingCenterBuildDays(int level) => level switch
        {
            1 => DaysPerQuarter * 2,
            2 => DaysPerQuarter * 3,
            3 => DaysPerQuarter * 4,
            _ => 0
        };

        /// <summary>
        /// 每季天数。本文件是纯 C#（游戏与 tools/balance 共用），不能引用 GameState，
        /// 故此处复写一份；一致性由 LogicSelfTests 断言（与 GameState.DaysPerQuarter 对齐）。
        /// </summary>
        public const int DaysPerQuarter = 90;

        /// <summary>该等级的培训费折扣（越高越便宜）：外包全额，航司大学七折。</summary>
        public static float TrainingCostFactor(int centerLevel) => centerLevel switch
        {
            1 => 0.90f,
            2 => 0.80f,
            3 => 0.70f,
            _ => 1.00f
        };

        /// <summary>
        /// 该培训轨道是否已解锁。只为**经 HR 页启动**的轨道把关：
        /// 机务 / 安全的培训另走 MNT / SAF 页的独立入口（0 级「外包培训」也能办，
        /// 自建中心的意义是折扣与证照），因此不在此设闸——否则 UI 承诺与代码会分叉。
        /// 证照考取的门槛逐证设定，见 <see cref="CertificationCatalog"/> 的 minCenterLevel
        /// （基础证 1 级、进阶证 2 级、国际运行资质 3 级）。
        /// </summary>
        public static bool TrainingTrackUnlocked(string track, int centerLevel) => track switch
        {
            "type_rating" => centerLevel >= 2,
            "cert" => centerLevel >= 1,
            _ => true          // people / maint / safety
        };

        /// <summary>
        /// 入册门槛：组织一次岗位培训时，只收潜力达标的人。
        /// 定在 0.60 是为了覆盖开局的副总监/主管档（基准潜力 0.62）——
        /// 若按人才档案的 0.75，开局会出现「无人可训」而使入口失效。
        /// </summary>
        public const float TrainingEnrollMinPotential = 0.60f;

        /// <summary>
        /// 建设资金是否是一笔可支付的真实报价。等级越界时 <see cref="TrainingCenterCostWan"/>
        /// 返回 +∞，本判定为 false——用作「免费升级」漏洞的第二道闸门。
        /// </summary>
        public static bool IsUpgradeCostPayable(float costWan) =>
            !float.IsInfinity(costWan) && !float.IsNaN(costWan) && costWan > 0f;

        /// <summary>工会谈判成本（万美元）：0让步 1强硬 2拖延。</summary>
        public static float UnionCostWan(int choice, int headcount) => choice switch
        {
            0 => headcount * 0.8f,
            1 => headcount * 0.15f,
            _ => headcount * 0.4f
        };

        /// <summary>工会谈判 → 满意度变化。</summary>
        public static float UnionSatisfactionDelta(int choice) => choice switch { 0 => 12f, 1 => -18f, _ => -4f };

        /// <summary>工会谈判 → 罢工风险变化。</summary>
        public static float UnionStrikeRiskDelta(int choice) => choice switch { 0 => -20f, 1 => 22f, _ => 6f };

        /// <summary>年度调薪现金（万美元）：约 3.5% 薪酬池。</summary>
        public static float RaisePackageWan(float laborCostWan) =>
            Clamp(laborCostWan * 0.035f, 80f, 600f);

        /// <summary>年度奖金池（万美元）。</summary>
        public static float BonusPoolWan(float laborCostWan) =>
            Clamp(laborCostWan * 0.018f, 40f, 280f);

        /// <summary>高管长期激励一次性成本（万美元）。</summary>
        public static float ExecLtiWan() => 90f;

        /// <summary>招聘渠道名。</summary>
        public static string ChannelName(int channel) => channel switch
        {
            0 => "批量校招",
            1 => "猎头挖角",
            2 => "海外引进",
            _ => "内部转岗"
        };

        static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        static float Clamp01(float v) => Clamp(v, 0f, 1f);
    }
}
