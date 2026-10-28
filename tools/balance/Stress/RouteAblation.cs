using AirlineCEO.Balance.Economy;
using AirlineCEO.Balance.Models;
using AirlineCEO.Balance.Scenarios;
using AirlineCEO.Balance.Sim;

namespace AirlineCEO.Balance.Stress;

/// <summary>
/// T04.8 遗留数值疑点的单因子对照实验（`docs/handoff/残余七项审计_2026-10-24.md` §8.7 第 5 条）。
///
/// **疑点**：60 组合矩阵里，低成本航司在滞胀/衰退下（净利 −1122 / −504）比支线（−805 / −457）
/// 还差，而支线的品牌权益更高、单机座位更少、周班更少。按常理 LCC 应该更抗压。
///
/// 70 家低成本航司亏得比 56 家支线多，这件事本身不必然是缺陷 —— 也可能是这套最小航司假设里
/// LCC 的某个输入过于苛刻。但**「可能」不是结论**，要把它变成结论必须做单因子对照：
/// 一次只动一个旋钮，看净利往哪边走、走多少。
///
/// 备选归因（README §压测口径 第 5 条列的）：
///   ① `yieldMultiplier = 0.85`（支线是 1.00）—— 收入侧，直接把每客公里收益打八五折；
///   ② `CompetitorSupply = 0.60~0.70`（支线是 0.40~0.60）—— 需求侧，竞对供给挤压 RPM；
///   ③ 周班 70 vs 56 —— 频率侧，多飞不但不赚钱还多烧可变成本。
///
/// 本实验只回答「①和②谁是主因」。③ 只有在 ① ② 都解释不了时才需要继续查。
/// </summary>
public static class RouteAblation
{
    public sealed class Variant
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        /// <summary>相对 baseline 只改了哪一项；`—` 表示基准行本身。</summary>
        public required string Changed { get; init; }
        public required StressTestRunner.AirlineType Airline { get; init; }
    }

    public sealed class Row
    {
        public required string VariantKey { get; init; }
        public required string Scenario { get; init; }
        public float Revenue { get; init; }
        public float Cost { get; init; }
        public float NetProfit { get; init; }
        public float ClosingCash { get; init; }
        public float Runway { get; init; }
        public string Flag { get; init; } = "";
        /// <summary>相对该情景 baseline 的净利差（万美元）。</summary>
        public float DeltaVsBaseline { get; init; }
    }

    private const float BaseCashWan = 500f;

    /// <summary>本实验锁定标准难度：难度只改期初现金，不参与归因。</summary>
    private static StressTestRunner.Difficulty StandardDifficulty
        => StressTestRunner.Difficulties.First(d => d.Key == "normal");

    private static StressTestRunner.AirlineType Lcc()
        => StressTestRunner.AirlineTypes.First(a => a.Key == "lcc");

    private static StressTestRunner.AirlineType Regional()
        => StressTestRunner.AirlineTypes.First(a => a.Key == "regional");

    /// <summary>重建航线集合，只改一处。</summary>
    private static IReadOnlyList<RouteTemplate> Remap(
        IReadOnlyList<RouteTemplate> src,
        float? yieldDelta = null,
        float? supplyDelta = null,
        float? yieldAbsolute = null)
    {
        var list = new List<RouteTemplate>(src.Count);
        foreach (var r in src)
        {
            float supply = Math.Clamp(r.CompetitorSupply + (supplyDelta ?? 0f), 0f, 0.95f);
            float yield = yieldAbsolute ?? (r.YieldMultiplier + (yieldDelta ?? 0f));
            list.Add(new RouteTemplate
            {
                RouteId = r.RouteId,
                Origin = r.Origin,
                Destination = r.Destination,
                DistanceKm = r.DistanceKm,
                CompetitorSupply = supply,
                PassengerType = r.PassengerType,
                WeeklyFlights = r.WeeklyFlights,
                AircraftType = r.AircraftType,
                Seats = r.Seats,
                LoadFactor = r.LoadFactor,
                YieldMultiplier = yield,
                GroundStaffAssigned = r.GroundStaffAssigned,
            });
        }
        return list;
    }

    public static IReadOnlyList<Variant> Variants()
    {
        var lcc = Lcc();
        return new[]
        {
            new Variant
            {
                Key = "lcc_base",
                Name = "低成本（基准）",
                Changed = "—",
                Airline = lcc,
            },
            new Variant
            {
                Key = "lcc_yield",
                Name = "低成本 · 仅提收益",
                Changed = "yieldMultiplier 0.85 → 0.90",
                Airline = new StressTestRunner.AirlineType
                {
                    Key = "lcc_yield",
                    Name = "低成本 · 仅提收益",
                    BrandEquity = lcc.BrandEquity,
                    Fleet = lcc.Fleet,
                    Routes = Remap(lcc.Routes, yieldDelta: 0.05f),
                },
            },
            new Variant
            {
                Key = "lcc_supply",
                Name = "低成本 · 仅降竞对供给",
                Changed = "CompetitorSupply −0.10",
                Airline = new StressTestRunner.AirlineType
                {
                    Key = "lcc_supply",
                    Name = "低成本 · 仅降竞对供给",
                    BrandEquity = lcc.BrandEquity,
                    Fleet = lcc.Fleet,
                    Routes = Remap(lcc.Routes, supplyDelta: -0.10f),
                },
            },
            new Variant
            {
                Key = "lcc_both",
                Name = "低成本 · 两者同调",
                Changed = "yield 0.90 + supply −0.10",
                Airline = new StressTestRunner.AirlineType
                {
                    Key = "lcc_both",
                    Name = "低成本 · 两者同调",
                    BrandEquity = lcc.BrandEquity,
                    Fleet = lcc.Fleet,
                    Routes = Remap(lcc.Routes, yieldDelta: 0.05f, supplyDelta: -0.10f),
                },
            },
            new Variant
            {
                Key = "lcc_yield_to_1",
                Name = "低成本 · 收益拉平支线",
                Changed = "yieldMultiplier 0.85 → 1.00",
                Airline = new StressTestRunner.AirlineType
                {
                    Key = "lcc_yield_to_1",
                    Name = "低成本 · 收益拉平支线",
                    BrandEquity = lcc.BrandEquity,
                    Fleet = lcc.Fleet,
                    Routes = Remap(lcc.Routes, yieldAbsolute: 1f),
                },
            },
            new Variant
            {
                Key = "regional_base",
                Name = "支线（参照）",
                Changed = "—（参照组，不是消融）",
                Airline = Regional(),
            },
            new Variant
            {
                Key = "lcc_5ac",
                Name = "低成本 · 减 1 架（保住航线）",
                Changed = "机队 6 → 5 架（航线不变，利用率 14.0）",
                Airline = new StressTestRunner.AirlineType
                {
                    Key = "lcc_5ac",
                    Name = "低成本 · 减 1 架（保住航线）",
                    BrandEquity = lcc.BrandEquity,
                    Fleet = lcc.Fleet.Take(5).ToArray(),
                    Routes = lcc.Routes,
                },
            },
            new Variant
            {
                Key = "lcc_4ac",
                Name = "低成本 · 减 2 架（利用率平支线）",
                Changed = "机队 6 → 4 架（航线不变，利用率 17.5）",
                Airline = new StressTestRunner.AirlineType
                {
                    Key = "lcc_4ac",
                    Name = "低成本 · 减 2 架（利用率平支线）",
                    BrandEquity = lcc.BrandEquity,
                    Fleet = lcc.Fleet.Take(4).ToArray(),
                    Routes = lcc.Routes,
                },
            },
        };
    }

    /// <summary>只跑压力情景：繁荣期三家都赚，看不出差别。</summary>
    private static readonly string[] PressureScenarios = { "recession", "oil_shock", "stagflation" };

    public static IReadOnlyList<Row> Run()
    {
        var rows = new List<Row>();
        var diff = StandardDifficulty;

        foreach (var variant in Variants())
        foreach (var scenarioName in PressureScenarios)
        {
            var scenario = StressTestRunner.Scenarios.First(s => s.Key == scenarioName);
            var macro = new MacroEconomyEngine(
                initialGdp: scenario.Gdp,
                initialPhase: scenario.Phase,
                initialFuelUsdPerTon: scenario.Fuel,
                seed: 42);
            var sim = new AirlineYearSimulator(
                macro: macro,
                fleet: variant.Airline.Fleet,
                routes: variant.Airline.Routes,
                openingCash: BaseCashWan * diff.CashMult,
                brandEquity: variant.Airline.BrandEquity,
                irropsSeverity: diff.EventSev,
                verbose: false);
            var s = sim.RunFiscalYear(1);

            rows.Add(new Row
            {
                VariantKey = variant.Key,
                Scenario = scenarioName,
                Revenue = s.TotalRevenue,
                Cost = s.TotalCost,
                NetProfit = s.TotalNetProfit,
                ClosingCash = s.ClosingCash,
                Runway = s.FinalRunwayMonths,
                Flag = Classify(s),
            });
        }

        // 填相对基准的差值：同一情景下 lcc_base 的净利就是基准。
        var baseline = rows.Where(r => r.VariantKey == "lcc_base")
            .ToDictionary(r => r.Scenario, r => r.NetProfit);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.VariantKey == "lcc_base" || !baseline.TryGetValue(r.Scenario, out float b))
            {
                rows[i] = Copy(r, 0f);
                continue;
            }
            rows[i] = Copy(r, r.NetProfit - b);
        }
        return rows;
    }

    private static Row Copy(Row r, float delta) => new()
    {
        VariantKey = r.VariantKey,
        Scenario = r.Scenario,
        Revenue = r.Revenue,
        Cost = r.Cost,
        NetProfit = r.NetProfit,
        ClosingCash = r.ClosingCash,
        Runway = r.Runway,
        Flag = r.Flag,
        DeltaVsBaseline = delta,
    };

    /// <summary>与 `StressTestRunner.Classify` 同口径；现金桥不影响判定。</summary>
    private static string Classify(YearSummary s)
    {
        if (s.MinCash < 0f) return "BANKRUPT";
        if (s.FinalRunwayMonths < 2f) return "RED";
        if (s.FinalRunwayMonths < 3f) return "YELLOW";
        if (s.TotalNetProfit > s.OpeningCash * 4f) return "RUNAWAY";
        return "GREEN";
    }

    public sealed class CostBreakdown
    {
        public required string Label { get; init; }
        public required string Key { get; init; }
        public float Fuel { get; init; }
        public float LandingCrew { get; init; }
        public float FleetMaint { get; init; }
        public float FixedOpex { get; init; }
        public float Labor { get; init; }
        public float RouteFixed { get; init; }
        public float Carbon { get; init; }
        public float Total { get; init; }
        /// <summary>周离港班次（各航线 WeeklyFlights 之和）。</summary>
        public int WeeklyDepartures { get; init; }
        /// <summary>在役机数。</summary>
        public int ActiveAircraft { get; init; }
        /// <summary>每架每周飞几班 —— 利用率。低于 14 基本等于「八架养一班组的半天」。</summary>
        public float RotationsPerAircraftWeek => ActiveAircraft > 0 ? WeeklyDepartures / (float)ActiveAircraft : 0f;
        /// <summary>每周班次分摊的年成本（万美元/周班）。</summary>
        public float CostPerWeeklyDeparture => WeeklyDepartures > 0 ? Total / WeeklyDepartures : 0f;
        public float RevenueWan { get; init; }
    }

    /// <summary>
    /// 成本拆解：回答「低成本多花的这 2300 万里，有多少是随班次走的、多少是随机队走的」。
    ///
    /// 这决定了该往哪边改：随班次的（燃油/起降/机组）说明是**飞太多**；
    /// 随机队的（机队维护/固定开销/人力）说明是**规模太重**，靠调收益倍率治不了。
    /// </summary>
    public static IReadOnlyList<CostBreakdown> CostBreakdownFor(string scenarioKey, bool onlyReferencePair = true)
    {
        var scenario = StressTestRunner.Scenarios.First(s => s.Key == scenarioKey);
        var diff = StandardDifficulty;
        var list = new List<CostBreakdown>();

        foreach (var variant in Variants())
        {
            if (onlyReferencePair && variant.Key is not ("lcc_base" or "regional_base")) continue;

            var macro = new MacroEconomyEngine(
                initialGdp: scenario.Gdp,
                initialPhase: scenario.Phase,
                initialFuelUsdPerTon: scenario.Fuel,
                seed: 42);
            var sim = new AirlineYearSimulator(
                macro: macro,
                fleet: variant.Airline.Fleet,
                routes: variant.Airline.Routes,
                openingCash: BaseCashWan * diff.CashMult,
                brandEquity: variant.Airline.BrandEquity,
                irropsSeverity: diff.EventSev,
                verbose: false);
            var s = sim.RunFiscalYear(1);

            list.Add(new CostBreakdown
            {
                Label = variant.Name,
                Key = variant.Key,
                Fuel = s.Months.Sum(m => m.FuelCostWan),
                LandingCrew = s.Months.Sum(m => m.LandingAndCrewWan),
                FleetMaint = s.Months.Sum(m => m.FleetMaintWan),
                FixedOpex = s.Months.Sum(m => m.FixedOpexWan),
                Labor = s.Months.Sum(m => m.LaborCostWan),
                // 航线周期固定成本**没有**进 MonthlyPnL 的明细字段（只进了 TotalCostWan），
                // 所以只能从年汇总取。这是本次拆解时踩到的坑，见 §成本明细缺口。
                RouteFixed = s.RouteFixedCostWan,
                Carbon = s.Months.Sum(m => m.CarbonCostWan),
                Total = s.TotalCost,
                RevenueWan = s.TotalRevenue,
                WeeklyDepartures = variant.Airline.Routes.Sum(r => r.WeeklyFlights),
                ActiveAircraft = variant.Airline.Fleet.Count(a => a.IsActive),
            });
        }
        return list;
    }

    public sealed record InvariantCheck(string Id, bool Ok, string Detail);

    /// <summary>
    /// 对照实验自身的两条不变量 —— 没有它们，上面的归因表就只是一次「看起来对」的读数。
    ///
    /// ① **成本明细可加**：七个成本项之和必须等于 `TotalCostWan`。若某项被塞进合计却没被拆出来，
    ///    表里就会留下一笔「看不见的钱」，而这正是 T04 要消灭的东西。
    /// ② **减机不改营收**：`AirlineYearSimulator` 的营收只由航线决定、不看机队，
    ///    所以「减 2 架」那一行的 +615 万**全部来自成本**。这条既是归因成立的前提，
    ///    也是模拟器能力边界的书面记录 —— 若哪天运力约束接进来了，这条会红，
    ///    提醒后来者「+615 不能再当成可行性上界」。
    /// </summary>
    public static IReadOnlyList<InvariantCheck> CheckInvariants()
    {
        var checks = new List<InvariantCheck>();
        var bd = CostBreakdownFor("stagflation", onlyReferencePair: false);
        var baseRevenue = bd.First(c => c.Key == "lcc_base").RevenueWan;

        foreach (var c in bd)
        {
            float sum = c.Fuel + c.LandingCrew + c.FleetMaint + c.FixedOpex + c.Labor + c.RouteFixed + c.Carbon;
            float gap = c.Total - sum;
            checks.Add(new InvariantCheck(
                $"cost-itemization:{c.Key}",
                Math.Abs(gap) <= 1f,
                $"明细项之和 {sum:F1} vs 合计 {c.Total:F1}（差 {gap:+0.00;-0.00;0}，容差 1.0）"));
        }

        foreach (var key in new[] { "lcc_5ac", "lcc_4ac" })
        {
            float rev = bd.First(c => c.Key == key).RevenueWan;
            checks.Add(new InvariantCheck(
                $"fleet-only-no-revenue:{key}",
                rev == baseRevenue,
                $"营收 {rev:F1} vs 基准 {baseRevenue:F1}（减机只许动成本）"));
        }
        return checks;
    }

    public static int RunAndPrint()
    {
        var rows = Run();
        var variants = Variants();

        Console.WriteLine("=== T04.8 单因子对照实验：低成本航司为何在压力情景下比支线还差 ===");
        Console.WriteLine("固定：标准难度（期初 500 万）、seed=42、一个财年、每年 6 月一次 IRROPS。");
        Console.WriteLine("只列压力情景（衰退 / 油价冲击 / 滞胀）—— 繁荣期三家都赚，看不出归因。");
        Console.WriteLine();

        foreach (var scenario in PressureScenarios)
        {
            Console.WriteLine($"--- {scenario} ---");
            Console.WriteLine("| 变体 | 改了哪一项 | 营收 | 成本 | 净利 | Δ净利 | 跑道 | 判定 |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|");
            foreach (var v in variants)
            {
                var r = rows.First(x => x.VariantKey == v.Key && x.Scenario == scenario);
                string delta = v.Key == "lcc_base" ? "—"
                    : v.Key == "regional_base" ? "（参照）"
                    : $"{r.DeltaVsBaseline:+0;-0;0}";
                Console.WriteLine($"| {v.Name} | {v.Changed} | {r.Revenue:F0} | {r.Cost:F0} | {r.NetProfit:F0} | {delta} | {r.Runway:F1} | {r.Flag} |");
            }
            Console.WriteLine();
        }

        Console.WriteLine("--- 归因读数（滞胀，净利相对基准的改善量）---");
        var stag = rows.Where(r => r.Scenario == "stagflation").ToList();
        float yieldGain = stag.First(r => r.VariantKey == "lcc_yield").DeltaVsBaseline;
        float supplyGain = stag.First(r => r.VariantKey == "lcc_supply").DeltaVsBaseline;
        float bothGain = stag.First(r => r.VariantKey == "lcc_both").DeltaVsBaseline;
        float fullYieldGain = stag.First(r => r.VariantKey == "lcc_yield_to_1").DeltaVsBaseline;
        float lccNet = stag.First(r => r.VariantKey == "lcc_base").NetProfit;
        float regionalNet = stag.First(r => r.VariantKey == "regional_base").NetProfit;

        Console.WriteLine($"  仅提收益 +0.05      : {yieldGain:+0.0;-0.0;0}");
        Console.WriteLine($"  仅降供给 −0.10      : {supplyGain:+0.0;-0.0;0}");
        Console.WriteLine($"  两者同调            : {bothGain:+0.0;-0.0;0}  （单因子之和 {(yieldGain + supplyGain):F1}，交互项 {(bothGain - yieldGain - supplyGain):+0.0;-0.0;0}）");
        Console.WriteLine($"  收益拉平支线 (1.00) : {fullYieldGain:+0.0;-0.0;0}");
        float fiveAc = stag.First(r => r.VariantKey == "lcc_5ac").DeltaVsBaseline;
        float fourAc = stag.First(r => r.VariantKey == "lcc_4ac").DeltaVsBaseline;
        Console.WriteLine($"  减 1 架（航线不变）  : {fiveAc:+0.0;-0.0;0}");
        Console.WriteLine($"  减 2 架（利用率平支线）: {fourAc:+0.0;-0.0;0}");
        Console.WriteLine($"  低成本 {lccNet:F0} vs 支线 {regionalNet:F0}，差 {lccNet - regionalNet:+0.0;-0.0;0}");
        Console.WriteLine();

        // 归因结论由数字本身给出，不写死 —— 系数一改，这句话就得跟着变。
        float totalGap = Math.Abs(lccNet - regionalNet);
        string verdict;
        if (totalGap < 1f)
            verdict = "两家已无差距，疑点消失。";
        else
        {
            float y = Math.Abs(yieldGain);
            float sp = Math.Abs(supplyGain);
            string main = y > sp * 1.5f ? "收益倍率（yieldMultiplier）"
                : sp > y * 1.5f ? "竞对供给（CompetitorSupply）"
                : "两者份额接近，无单一主因";
            float covered = Math.Abs(fullYieldGain) / totalGap;
            // 超过 100% 说明「收益一项就足够跨过差额，并且还会反超」——
            // 那不是「解释力刚好」，而是两家在**不止一个轴**上不同，不能把差额全记在收益头上。
            string coverage = covered >= 1f
                ? $"把收益拉平到支线的 1.00 可收回 {fullYieldGain:+0.0;-0.0;0} 万，" +
                  $"**超过**差额本身（净利变为 {lccNet + fullYieldGain:F0} 万，反而好于支线 {regionalNet:F0} 万）" +
                  "—— 说明两家的差距不止收益一个轴，差额不能被「收益差」单独解释完。"
                : $"把收益拉平到支线的 1.00 可收回 {fullYieldGain:+0.0;-0.0;0} 万，" +
                  $"相当于该差额的 {covered * 100:F0}%。";
            verdict =
                $"主因是 {main}：收益 +0.05 贡献 {yieldGain:+0.0;-0.0;0} 万、供给 −0.10 贡献 {supplyGain:+0.0;-0.0;0} 万。" +
                coverage;
        }
        Console.WriteLine($"[结论] {verdict}");
        Console.WriteLine();

        // 第二问：光调收益倍率够不够？够不够的标准是「能不能离开 BANKRUPT」，不是「有没有改善」。
        float bestTweak = Math.Max(Math.Max(yieldGain, supplyGain), bothGain);
        float bestTweakNet = lccNet + bestTweak;
        Console.WriteLine($"  最强的参数微调是「两者同调」：净利 {bestTweakNet:F0} 万 —— " +
                          (bestTweakNet < 0
                              ? "**仍然亏损**，也就是说调系数治不好这个组合。"
                              : "已转正，调系数即可。"));
        if (fourAc > 0)
            Console.WriteLine($"  减到 4 架（利用率 {70f / 4:F1} ≈ 支线 {56f / 3:F1}）：净利 {lccNet + fourAc:F0} 万 —— " +
                              (lccNet + fourAc > 0 ? "转正。" : "仍未转正，但缺口收窄。"));
        Console.WriteLine();

        Console.WriteLine("--- 成本拆解（滞胀，万美元/年）---");
        Console.WriteLine("| 航司 | 燃油 | 起降+机组 | 机队维护 | 固定开销 | 人力 | 航权维持 | 碳 | 合计 |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
        var bd = CostBreakdownFor("stagflation");
        foreach (var c in bd)
            Console.WriteLine($"| {c.Label} | {c.Fuel:F0} | {c.LandingCrew:F0} | {c.FleetMaint:F0} | {c.FixedOpex:F0} | {c.Labor:F0} | {c.RouteFixed:F0} | {c.Carbon:F0} | {c.Total:F0} |");
        if (bd.Count == 2)
        {
            var a = bd[0];
            var b = bd[1];
            float volumeDriven = (a.Fuel + a.LandingCrew) - (b.Fuel + b.LandingCrew);
            float scaleDriven = (a.FleetMaint + a.FixedOpex + a.Labor + a.RouteFixed) - (b.FleetMaint + b.FixedOpex + b.Labor + b.RouteFixed);
            Console.WriteLine();
            Console.WriteLine($"  低成本比支线多花 {a.Total - b.Total:F0} 万：随班次的（燃油+起降机组）{volumeDriven:+0.0;-0.0;0} 万，" +
                              $"随机队/规模的（维护+固定+人力+航权维持）{scaleDriven:+0.0;-0.0;0} 万。");
            // 明细项之和是否等于合计：不等说明还有成本塞进了 TotalCostWan 却没被拆出来，
            // 「逐项解释」就会在这一步断掉。宁可在这里报出来，也不要让读者自己减。
            float sumA = a.Fuel + a.LandingCrew + a.FleetMaint + a.FixedOpex + a.Labor + a.RouteFixed + a.Carbon;
            float sumB = b.Fuel + b.LandingCrew + b.FleetMaint + b.FixedOpex + b.Labor + b.RouteFixed + b.Carbon;
            Console.WriteLine($"  明细项之和 vs 合计：低成本 {sumA:F1} / {a.Total:F1}（差 {a.Total - sumA:+0.00;-0.00;0}），" +
                              $"支线 {sumB:F1} / {b.Total:F1}（差 {b.Total - sumB:+0.00;-0.00;0}）");
            Console.WriteLine();
            Console.WriteLine("--- 机队利用率（这才是「为什么比支线还差」的根）---");
            Console.WriteLine("| 航司 | 周班 | 机数 | 每机每周班次 | 单位周班成本 | 单位周班营收 |");
            Console.WriteLine("|---|---|---|---|---|---|");
            foreach (var c in bd)
                Console.WriteLine($"| {c.Label} | {c.WeeklyDepartures} | {c.ActiveAircraft} | {c.RotationsPerAircraftWeek:F1} | " +
                                  $"{c.CostPerWeeklyDeparture:F1} | {c.RevenueWan / Math.Max(1, c.WeeklyDepartures):F1} |");
            Console.WriteLine();
            Console.WriteLine($"  低成本每机每周 {a.RotationsPerAircraftWeek:F1} 班、支线 {b.RotationsPerAircraftWeek:F1} 班 —— " +
                              $"飞机同样在折旧、在养机组，飞得越少，单位班次背的固定负担越重。");
        }
        return 0;
    }
}
