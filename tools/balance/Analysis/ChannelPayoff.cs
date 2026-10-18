using AirlineCEO.Logic;

namespace AirlineCEO.Balance.Analysis;

/// <summary>
/// 招聘渠道收益差分析（tools/balance · --channels）。
///
/// 口径说明（重要）：本分析全部数值取自游戏侧权威实现 HrEconomics / PersonnelGenerator.Make
/// 的「专员」基准档，不另立一套参数，避免与游戏分叉。
///
/// 分两部分：
///   Part A「现状」= 今天真正生效的差异（一次性招聘费、到岗周期、成功率），可复现、可验证；
///   Part B「持续成本」= 薪酬 rollup 与忠诚→流失接入后的 3 年 TCO / 单位战力成本。
///                  其中「薪酬 rollup」与「机组战力乘数」两条**已在游戏侧接通**（见文末注），
///                  故 Part B 的薪酬列是真实生效值；流失列仍是显式假设的投影。
///
/// 为什么需要 Part B：渠道雇来的人 isOperational=true，被 DeptStaffLift 排除，
/// 而 DeptStaffLift 曾是他们 efficiencyBonus 的唯一消费点；baseSalary 也从不被薪酬 rollup 汇总
///（laborCostWan 是存量字段，只被调薪决策 ×1.035 修改）。若维持原状，渠道之间「贵 93% 的猎头」
/// 在持续成本上与校招完全等价——属性差是死的。现已把薪酬与战力接回经营结果。
/// </summary>
internal static class ChannelPayoff
{
    // ---- 目标编制：以「6 名飞行员」为统一样本，便于横向比较 ----
    const int Target = 6;

    // ---- 专员基准档（PersonnelGenerator.Make，rank=="专员"）----
    const float BasePay = 8f;        // 万/年
    const float BaseLoyalty = 68f;   // 生成时 +n%12 抖动，取基准值
    const float BaseEff = 0.48f;
    const float BasePot = 0.88f;

    /// <summary>渠道编号：0 校招 1 猎头 2 海外 3 内部（与 HrEconomics.SourceTag 一致）。</summary>
    static readonly int[] Channels = { 0, 1, 2, 3 };

    public static void Run(float shortage)
    {
        Console.WriteLine("=== Airline CEO · 招聘渠道收益差分析（--channels） ===");
        Console.WriteLine($"样本：{Target} 名「飞行员」 · 人才紧缺指数 {shortage:F0} · 成本系数 ×{HrEconomics.ShortageCostFactor(shortage):F2}");
        Console.WriteLine("数据源：HrEconomics（游戏与模拟共用权威值）· 专员基准档 薪酬 8 万 / 忠诚 68 / 战力 0.48 / 潜力 0.88");
        Console.WriteLine();

        var rows = new List<Row>();
        foreach (int ch in Channels)
            rows.Add(Evaluate(ch, shortage));

        PrintPartA(rows, shortage);
        PrintAttributes(rows);
        PrintPartB(rows);
        PrintVerdict(rows, shortage);
    }

    // ============================================================
    // Part A · 现状：一次性成本 + 周期 + 成功率
    // ============================================================

    /// <summary>
    /// 模拟 HrManager.LaunchRecruitment → TickDay → RecruitNow 的真实循环，
    /// 直到目标人数补齐。完全按游戏公式：
    ///   预付费 = needed × RecruitCostPerHire(ch, shortage) / 10000  （万美元）
    ///   落池   = room 人，等 RecruitLeadWeeks(ch) × 7 天
    ///   到岗   = clamp(ceil(room × RecruitSuccessRate(ch, shortage)), 1, room)
    /// </summary>
    static Row Evaluate(int ch, float shortage)
    {
        float costPerUsd = HrEconomics.RecruitCostPerHire(ch, shortage);
        float leadWeeks = HrEconomics.RecruitLeadWeeks(ch);
        float rate = ch == 3 ? 1f : HrEconomics.RecruitSuccessRate(ch, shortage);

        string tag = HrEconomics.SourceTag(ch);
        var r = new Row
        {
            Channel = ch,
            Name = HrEconomics.ChannelName(ch),
            Tag = tag,                              // machine tag（headhunt 等），属性折算必须用它
            SourceDisplay = HrEconomics.SourceName(tag),
            CostPerUsd = costPerUsd,
            LeadWeeks = leadWeeks,
            Rate = rate
        };

        if (ch == 3)
        {
            // 内部转岗：无外部招聘费、即时办理，代价是调薪 +20% 与 14 天交接期战力 ×0.7，
            // 且原部门编制出现缺口（由 StaffRecruitment 自动重开）。
            r.Launches = 1;
            r.Arrived = Target;
            r.UpfrontWan = 0f;
            r.TotalDays = 0;
            r.CostPerArrivalWan = 0f;
            return r;
        }

        int room = Target;
        int arrived = 0;
        float paid = 0f;
        double days = 0;
        int launches = 0;

        // 按游戏行为迭代：每轮发起 room 人，等一个渠道周期，按成功率到岗
        for (int guard = 0; guard < 50 && arrived < Target; guard++)
        {
            paid += room * costPerUsd / 10000f;
            days += leadWeeks * 7.0;
            launches++;
            int hired = (int)Math.Ceiling(room * (double)rate);
            if (hired < 1) hired = 1;
            if (hired > room) hired = room;
            arrived += hired;
            room = Target - arrived;
        }

        r.Launches = launches;
        r.Arrived = arrived;
        r.UpfrontWan = paid;
        r.TotalDays = days;
        r.CostPerArrivalWan = arrived > 0 ? paid / arrived : 0f;
        return r;
    }

    static void PrintPartA(List<Row> rows, float shortage)
    {
        Console.WriteLine("── Part A · 现状：今天真正生效的差异 ────────────────────────");
        Console.WriteLine();
        Console.WriteLine($"{"渠道",-10}{"人均招聘费",10}{"周期(周)",9}{"成功率",8}{"发起轮次",9}{"累计成本(万)",12}{"总耗时(天)",11}{"实际成本/人(万)",14}");
        Console.WriteLine(new string('-', 88));
        foreach (var r in rows)
        {
            // CostPerUsd 已含紧缺系数（LaunchRecruitment 的扣款口径）
            string fee = r.Channel == 3 ? "0（内转）" : $"{r.CostPerUsd:F0} USD";
            string rate = r.Channel == 3 ? "即时" : $"{r.Rate * 100:F0}%";
            string lead = r.Channel == 3 ? "0" : $"{r.LeadWeeks:F0}";
            Console.WriteLine(
                $"{r.Name,-10}{Trunc(fee),10}{lead,9}{rate,8}{r.Launches,9}" +
                $"{r.UpfrontWan,12:F2}{r.TotalDays,11:F0}{r.CostPerArrivalWan,14:F3}");
        }
        Console.WriteLine();

        var ext = rows.Where(x => x.Channel != 3).OrderBy(x => x.UpfrontWan).ToList();
        var fast = rows.Where(x => x.Channel != 3).OrderBy(x => x.TotalDays).ToList();
        Console.WriteLine($"· 最省：{ext[0].Name} {ext[0].UpfrontWan:F2} 万（但耗时 {ext[0].TotalDays:F0} 天）");
        Console.WriteLine($"· 最快：{fast[0].Name} {fast[0].TotalDays:F0} 天（代价 {fast[0].UpfrontWan:F2} 万）");
        float spread = ext[^1].UpfrontWan - ext[0].UpfrontWan;
        Console.WriteLine($"· 补齐 {Target} 人的成本极差：{spread:F2} 万（{ext[^1].Name} vs {ext[0].Name}，×{ext[^1].UpfrontWan / Math.Max(0.0001f, ext[0].UpfrontWan):F1}）");
        Console.WriteLine($"· 时间极差：{fast[^1].TotalDays - fast[0].TotalDays:F0} 天（{fast[^1].Name} vs {fast[0].Name}）");
        Console.WriteLine();
        Console.WriteLine("注：RecruitNow 用 ceil(room×成功率) 且下限 1，故小批量发起的成功率惩罚被显著摊薄；");
        Console.WriteLine("    猎头 35% 成功率在 6 人规模下仍需 3 轮才能补齐，这就是它贵的真正原因。");
        Console.WriteLine();
    }

    // ============================================================
    // 落池人物属性（游戏 PersonnelGenerator.ApplySourceProfile 的真实产物）
    // ============================================================

    static void PrintAttributes(List<Row> rows)
    {
        Console.WriteLine("── 落池人物属性（ApplySourceProfile 实际产物，含 clamp）───────");
        Console.WriteLine();
        Console.WriteLine($"{"渠道",-10}{"潜力",8}{"年薪(万)",10}{"忠诚",8}{"战力",8}   备注");
        Console.WriteLine(new string('-', 78));
        foreach (var r in rows)
        {
            HrEconomics.ApplySourceStats(r.Tag, BasePot, BasePay, BaseLoyalty, BaseEff,
                out float pot, out float pay, out float loy, out float eff);

            // 内部转岗另有 +20% 调薪（HrManager.TransferStaff / FillByInternalTransfer）
            if (r.Channel == 3) pay *= 1f + HrEconomics.InternalTransferRaise;

            string note = "";
            if (pot >= HrEconomics.PotentialMax - 0.001f) note = "潜力触顶 clamp";
            if (r.Channel == 3) note = (note.Length > 0 ? note + " · " : "") + "交接期 14 天战力 ×0.70";

            r.Pay = pay; r.Loyalty = loy; r.Eff = eff; r.Potential = pot;

            Console.WriteLine($"{r.Name,-10}{pot,8:F3}{pay,10:F2}{loy,8:F1}{eff,8:F3}   {note}");
        }
        Console.WriteLine();

        var hh = rows.First(x => x.Channel == 1);
        var cp = rows.First(x => x.Channel == 0);
        Console.WriteLine($"· 猎头战力 {hh.Eff:F3} vs 校招 {cp.Eff:F3}（+{(hh.Eff / cp.Eff - 1f) * 100:F0}%），年薪 {hh.Pay:F2} vs {cp.Pay:F2}（+{(hh.Pay / cp.Pay - 1f) * 100:F0}%）");
        Console.WriteLine($"· 校招忠诚 {cp.Loyalty:F1} vs 猎头 {hh.Loyalty:F1}（−{(1f - hh.Loyalty / cp.Loyalty) * 100:F0}%）—— 但两者都远高于 SuccessionLogic 的离职预警线 45");
        Console.WriteLine($"· 猎头潜力 {hh.Potential:F3} 已被 PotentialMax={HrEconomics.PotentialMax} 截断（原始 {BasePot * 1.15f:F3}），溢价部分浪费");
        Console.WriteLine();
    }

    // ============================================================
    // Part B · 投影：若把属性接入下游
    // ============================================================

    /// <summary>
    /// 接通情况（重要，决定各列是「真实生效」还是「投影假设」）：
    ///   A1 薪酬 rollup —— **已接通**（GameState.OperationalPayrollMonthlyWan →
    ///      LiveClockLogic.DailyFixedCostWan 逐日扣现金 + TimeManager 季结轧差）。
    ///      故「3年薪酬」列是游戏里真实会扣掉的钱。
    ///      注意内部转岗者被排除：他们的薪酬本已在 laborCostWan 聚合口径内，再算一遍是双扣。
    ///   A2 忠诚→流失 —— 仍为**投影**：年离职概率 = clamp(0.30 − loyalty/100 × 0.25, 0.02, 0.30)。
    ///      今天 AttritionWatch 只计数、不触发离职，故此列不代表当前行为。
    ///   A3 离职后按同渠道补员，补员成本 = Part A 的实际成本/人（随 A2 一并属投影）。
    ///   A4 观察期 3 年。
    ///   A5 战力乘数 —— **已接通**（GameState.CrewQualityMultiplier → 航线日利润），
    ///      直接取 HrEconomics.CrewQualityMultiplier，与游戏同一条公式，非投影。
    /// </summary>
    static void PrintPartB(List<Row> rows)
    {
        const int years = 3;

        Console.WriteLine($"── Part B · 持续成本与运营后果（{years} 年 TCO · 接通情况见文末）──");
        Console.WriteLine();
        Console.WriteLine($"{"渠道",-10}{"3年薪酬(万)",12}{"战力乘数",10}{"预期在职(年)",12}{"3年离职(人次)",13}{"补员成本(万)",12}{"3年TCO(万)",12}{"万/战力点",11}");
        Console.WriteLine(new string('-', 102));

        foreach (var r in rows)
        {
            float annualPay = r.Pay;
            float attrition = AttritionFromLoyalty(r.Loyalty);
            float tenure = 1f / attrition;
            float departures = years * attrition;
            float replace = departures * r.CostPerArrivalWan;
            float payroll = annualPay * years;
            float tco = payroll + replace + r.UpfrontWan;

            r.Payroll3y = payroll;
            r.TenureYears = tenure;
            r.Departures3y = departures;
            r.ReplaceCost = replace;
            r.Tco3y = tco;
            r.WanPerEff = tco / Math.Max(0.001f, r.Eff * years);
            // 战力乘数：直接调游戏同一条纯函数（filled>0 按战力折算，未建机组取 CrewUnstaffedMultiplier）
            r.CrewMult = HrEconomics.CrewQualityMultiplier(Target, r.Eff);

            Console.WriteLine(
                $"{r.Name,-10}{payroll,12:F2}{r.CrewMult,10:F3}{tenure,12:F1}{departures,13:F2}{replace,12:F2}{tco,12:F2}{r.WanPerEff,11:F2}");
        }
        Console.WriteLine();

        var byTco = rows.OrderBy(x => x.Tco3y).ToList();
        var byEff = rows.OrderBy(x => x.WanPerEff).ToList();
        Console.WriteLine($"· 3 年 TCO 最低：{byTco[0].Name} {byTco[0].Tco3y:F2} 万 · 最高：{byTco[^1].Name} {byTco[^1].Tco3y:F2} 万（差 {byTco[^1].Tco3y - byTco[0].Tco3y:F2} 万）");
        Console.WriteLine($"· 单位战力成本最低：{byEff[0].Name} {byEff[0].WanPerEff:F2} 万/点 · 最高：{byEff[^1].Name} {byEff[^1].WanPerEff:F2} 万/点");

        var byMult = rows.OrderByDescending(x => x.CrewMult).ToList();
        Console.WriteLine($"· 战力乘数（已接通，直接作用于航线日利润）：{string.Join(" > ", byMult.Select(x => $"{x.Name} {x.CrewMult:F3}"))}");
        Console.WriteLine($"  达标线 {HrEconomics.CrewAdequacyBaseline:F2} → 乘数 1.000；夹在 [{HrEconomics.CrewQualityMin:F2}, {HrEconomics.CrewQualityMax:F2}] 内。");

        // 反向激励不变量：未建机组必须严格劣于任何已建机组，否则「永不招人」会成为免费最优解。
        float unstaffed = HrEconomics.CrewQualityMultiplier(0, 0f);
        float worstCrew = byMult[^1].CrewMult;
        Console.WriteLine();
        Console.WriteLine($"· 反向激励校验（不招人 vs 招人）：");
        Console.WriteLine($"    未建机组（在职 0 人）      乘数 {unstaffed:F3}  ← 航线日利润打 {unstaffed * 100:F0} 折");
        Console.WriteLine($"    最差的已建机组（{worstCrew:F3}）   仍优于未建机组 {((worstCrew / unstaffed - 1f) * 100):F1}%");
        bool ok = unstaffed < worstCrew && unstaffed < HrEconomics.CrewQualityMin;
        Console.WriteLine($"    不变量「未建机组 < CrewQualityMin({HrEconomics.CrewQualityMin:F2}) <= 任何已建机组」：{(ok ? "成立 ✅" : "被破坏 ❌")}");
        Console.WriteLine("    含义：招任何渠道的机组都优于一个人不招，且校招可通过培训把战力养到达标线以上。");
        Console.WriteLine("    注意：开局若有机队却无建模机组，航线利润即按上述折损计（不再是「一字不变」的旧基线）。");
        Console.WriteLine();
        Console.WriteLine("接通情况：A1 薪酬 rollup【已接通】· A5 战力乘数【已接通】· A2/A3 忠诚→流失与补员【仍为投影】；");
        Console.WriteLine($"      A2 年离职率 = clamp(0.30 − 忠诚/100×0.25, 0.02, 0.30)；A3 离职按同渠道补员（成本取 Part A 实际值）；");
        Console.WriteLine($"      A4 观察期 {years} 年；内部转岗另计 +20% 调薪，但其薪酬已在 laborCostWan 聚合口径内，故不计入增量薪酬。");
        Console.WriteLine();
    }

    static float AttritionFromLoyalty(float loyalty)
        => Math.Clamp(0.30f - loyalty / 100f * 0.25f, 0.02f, 0.30f);

    // ============================================================
    // 结论
    // ============================================================

    static void PrintVerdict(List<Row> rows, float shortage)
    {
        Console.WriteLine("── 结论 ──────────────────────────────────────────────────");
        Console.WriteLine();

        var cp = rows.First(x => x.Channel == 0);
        var hh = rows.First(x => x.Channel == 1);
        var os = rows.First(x => x.Channel == 2);

        Console.WriteLine("1) 现状下渠道选择的收益差 = 「钱 vs 时间 vs 资质」三重权衡：");
        Console.WriteLine($"   · 校招 {cp.UpfrontWan:F2} 万 / {cp.TotalDays:F0} 天 —— 最省，但 {cp.LeadWeeks:F0} 周周期救不了急");
        Console.WriteLine($"   · 猎头 {hh.UpfrontWan:F2} 万 / {hh.TotalDays:F0} 天 —— 贵 {hh.UpfrontWan / Math.Max(0.0001f, cp.UpfrontWan):F1}×，快 {cp.TotalDays / Math.Max(1, hh.TotalDays):F1}×");
        Console.WriteLine($"   · 海外 {os.UpfrontWan:F2} 万 / {os.TotalDays:F0} 天 —— 单看成本/周期被猎头支配，但它是国际航线资质的唯一来源（见第 6 条）");
        Console.WriteLine($"   · 内转 0 万 / 即时 —— 但只在「已有在编同职级员工」时可用，且代价是原部门缺口 + 14 天交接期战力打折");
        Console.WriteLine();
        Console.WriteLine("2) 属性差曾完全是死的（本轮已接通）：");
        Console.WriteLine("   · 旧状：渠道雇来的人 isOperational=true → 被 TrainingGrowthLogic.DeptStaffLift 排除，");
        Console.WriteLine("     而 DeptStaffLift 曾是 efficiencyBonus 的唯一聚合消费点；baseSalary 也从不进薪酬 rollup。");
        Console.WriteLine("     → 猎头战力 +38%、年薪 +93% 对经营结果零影响，玩家多付的钱买不到任何东西。");
        Console.WriteLine("   · 已接通 ①：薪酬 rollup（GameState.OperationalPayrollMonthlyWan）——渠道年薪差现在真实扣现金：");
        Console.WriteLine($"     6 名 {hh.Name}年薪合计 {hh.Pay * Target:F1} 万/年 vs 校招 {cp.Pay * Target:F1} 万/年（+{(hh.Pay / cp.Pay - 1f) * 100:F0}%），");
        Console.WriteLine("     逐日计入 DailyFixedCostWan，季结在 TimeManager 轧差，并计入 EffectiveMonthlyBurn 影响跑道与信用。");
        Console.WriteLine("   · 已接通 ②：机组战力乘数（GameState.CrewQualityMultiplier → 航线日利润）——战力差现在有收益后果：");
        Console.WriteLine($"     {hh.Name} {hh.CrewMult:F3} vs 校招 {cp.CrewMult:F3}（见 Part B 战力乘数列，与游戏同一条公式）。");
        Console.WriteLine($"   · 仍为投影：忠诚差（校招 {cp.Loyalty:F1} vs 猎头 {hh.Loyalty:F1}）——两者都高于离职预警线 45，");
        Console.WriteLine("     且 AttritionWatch 只计数、不触发离职，故 Part B 的流失/补员列尚未生效。");
        Console.WriteLine();
        Console.WriteLine("3) 接通后渠道才形成真正的取舍（Part B）：");
        var byEff = rows.OrderBy(x => x.WanPerEff).ToList();
        Console.WriteLine($"   · 单位战力成本：{string.Join(" < ", byEff.Select(x => $"{x.Name} {x.WanPerEff:F1}"))}");
        Console.WriteLine($"   · 校招在「长期低成本养兵」上最优，猎头在「即战力 + 快速补位」上值回票价；");
        Console.WriteLine($"   · 海外引进在成本/战力两个口径下都不占优 → 已给它专属生态位（见第 6 条），否则是劣势选项。");
        Console.WriteLine();
        Console.WriteLine("4) 合并管线前必须知道的两个结构性事实（已核实）：");
        Console.WriteLine("   · 两条管线的岗位空间是【互不相交】的：");
        Console.WriteLine("     - StaffRecruitment 补的是编制席（StaffEstablishment.Roles：飞行运行主管/签派经理/客舱经理…）；");
        Console.WriteLine("     - HrManager.vacancies 只招「飞行员」——RecruitPosition 在全代码库仅有一处声明，从无赋值点，");
        Console.WriteLine("       且「飞行员」并不在 StaffEstablishment.Roles 的任何编制席里。");
        Console.WriteLine("     → 因此今天【不存在】「同一岗位被两条线重复招募」的冲突（旧方案文档第 318 行的推测不成立）。");
        Console.WriteLine("   · 唯一的真实耦合点是内部转岗：TransferStaff / FillByInternalTransfer 会把人搬离编制席，");
        Console.WriteLine("     随即调 StaffRecruitment.EnsureVacancies 让该席自动重开招聘案 —— 这正是「原部门编制缺口」的代价实现。");
        Console.WriteLine();
        Console.WriteLine("5) 两处旧缺口（本轮已修复）：");
        Console.WriteLine("   · ✅ HireCandidate 曾在 VacancyFor 返回 null 时不做任何配额校验就 ConfirmHire + totalHeadcount++；");
        Console.WriteLine("     现已改为按岗位兜底匹配招聘案（VacancyForPosition），两条路径都强制校验 hired<needed。");
        Console.WriteLine("   · ✅ 「飞行员」曾无编制上限，needed 可无限累加（推高 UnionCostWan 与 TrainingRoi）；");
        Console.WriteLine("     现已按机队规模推导编制（HrEconomics.OperationalEstablishment），LaunchRecruitment 按");
        Console.WriteLine("     「编制 − 在职 − 在途」封顶 needed，扩机队后编制自动上调。");
        Console.WriteLine();
        Console.WriteLine("6) 海外引进的专属生态位（已接通）：");
        Console.WriteLine("   · RoutePermitLogic.MissingPrerequisite 新增缺口码 2：国际/地区航线发证要求至少 1 名");
        Console.WriteLine("     「海外引进」渠道到岗的运行序列人员（GameState.OverseasCrewCount / HasOverseasQualification）。");
        Console.WriteLine("   · 校招/猎头/内转都不提供该资质 → 海外引进从「被猎头支配的劣势选项」变为「国际航线的必需渠道」。");
        Console.WriteLine();
    }

    static string Trunc(string s) => s;

    class Row
    {
        public int Channel;
        public string Name = "";
        /// <summary>机器可读来源标签（campus / headhunt / …），属性折算的唯一合法入参。</summary>
        public string Tag = "";
        /// <summary>中文来源名（校招 / 猎头挖角 / …），仅用于展示。</summary>
        public string SourceDisplay = "";
        public float CostPerUsd;
        public float LeadWeeks;
        public float Rate;
        public int Launches;
        public int Arrived;
        public float UpfrontWan;
        public double TotalDays;
        public float CostPerArrivalWan;

        // 属性
        public float Potential, Pay, Loyalty, Eff;

        // Part B
        public float Payroll3y, TenureYears, Departures3y, ReplaceCost, Tco3y, WanPerEff;
        /// <summary>
        /// 机组战力乘数（HrEconomics.CrewQualityMultiplier，已在游戏侧接通航线日利润）。
        /// 取游戏同一条纯函数，故非投影值。
        /// </summary>
        public float CrewMult;
    }
}
