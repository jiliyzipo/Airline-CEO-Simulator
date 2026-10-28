using AirlineCEO.Balance.Economy;
using AirlineCEO.Balance.Models;
using AirlineCEO.Balance.Scenarios;
using AirlineCEO.Balance.Sim;

namespace AirlineCEO.Balance.Stress;

/// <summary>
/// 夹具利用率审计（T04.8 对照实验的后续）。
///
/// `--ablate` 证明了「低成本比支线还差」的根因是机队利用率（11.7 vs 18.7 班/机/周），
/// 但那只是把病指出来 —— 四类航司的利用率到底差多少、改完矩阵会怎么变，还没有量过。
/// 本模块做两件事：
///
/// 1. **量**：四类航司的 班/机/周、单位班次成本与营收，以及是否落在合理带内；
/// 2. **算**：按「减机到带内」做一次**营收中性**的重跑（营收只由航线决定、不看机队，
///    所以减机不改营收、只减成本），把 60 组合的破产/告急分布与现货矩阵并排比出来。
///
/// **本模块不写默认矩阵、不重录基线。** 理由写在报告末段：减机虽然营收中性，
/// 但它同时意味着「剩下的飞机飞不完原有的班表」，而 `AirlineYearSimulator` 没有运力约束、
/// 不会报错。是否采纳为正式夹具是设计裁定，不是开发能单方面定的。
/// </summary>
public static class UtilizationAudit
{
    /// <summary>
    /// 合理带（班/机/周）。下限 12 对应「每机每周飞不满 12 班」，那意味着飞机大部分时间趴着；
    /// 上限 22 对应「每机每周 22 班」，约合每天 3 班、按窄体 2.7 小时/圈算约 59 小时/周，仍在维修余量内。
    /// </summary>
    public const float BandLow = 12f;
    public const float BandHigh = 22f;

    private const float BaseCashWan = 500f;
    private const int Seed = 42;

    public sealed class TypeReading
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public required int WeeklyDepartures { get; init; }
        public required int Aircraft { get; init; }
        public required int SizedAircraft { get; init; }
        public float PerAircraftWeek => Aircraft > 0 ? WeeklyDepartures / (float)Aircraft : 0f;
        public float SizedPerAircraftWeek => SizedAircraft > 0 ? WeeklyDepartures / (float)SizedAircraft : 0f;
        public bool InBand => PerAircraftWeek >= BandLow && PerAircraftWeek <= BandHigh;
        /// <summary>减到带内所需的最少机数；非法输入时退回原机数。</summary>
        public static int FitAircraft(int weekly, int current)
        {
            if (weekly <= 0) return Math.Max(1, current);
            int need = (int)Math.Ceiling(weekly / BandHigh);
            return Math.Clamp(need, 1, Math.Max(1, current));
        }
    }

    public static IReadOnlyList<TypeReading> Readings()
    {
        var list = new List<TypeReading>();
        foreach (var t in StressTestRunner.AirlineTypes)
        {
            int weekly = t.Routes.Sum(r => r.WeeklyFlights);
            int aircraft = t.Fleet.Count(a => a.IsActive);
            list.Add(new TypeReading
            {
                Key = t.Key,
                Name = t.Name,
                WeeklyDepartures = weekly,
                Aircraft = aircraft,
                SizedAircraft = TypeReading.FitAircraft(weekly, aircraft),
            });
        }
        return list;
    }

    /// <summary>把某一类航司按目标机数裁机，返回改后的类型（航线不动）。</summary>
    private static StressTestRunner.AirlineType Sized(StressTestRunner.AirlineType t, int aircraft)
        => new()
        {
            Key = t.Key,
            Name = t.Name,
            BrandEquity = t.BrandEquity,
            Fleet = t.Fleet.Take(aircraft).ToArray(),
            Routes = t.Routes,
        };

    public sealed class ComboResult
    {
        public required string AirlineType { get; init; }
        public required string Scenario { get; init; }
        public required string Difficulty { get; init; }
        public float NetProfit { get; init; }
        public float Cost { get; init; }
        public float Revenue { get; init; }
        public float Runway { get; init; }
        public string Flag { get; init; } = "";
    }

    /// <summary>用指定机队跑 60 组合。</summary>
    private static IReadOnlyList<ComboResult> RunMatrix(
        Func<StressTestRunner.AirlineType, StressTestRunner.AirlineType> transform)
    {
        var rows = new List<ComboResult>();
        foreach (var diff in StressTestRunner.Difficulties)
        foreach (var airlineRaw in StressTestRunner.AirlineTypes)
        {
            var airline = transform(airlineRaw);
            foreach (var scenario in StressTestRunner.Scenarios)
            {
                var macro = new MacroEconomyEngine(
                    initialGdp: scenario.Gdp,
                    initialPhase: scenario.Phase,
                    initialFuelUsdPerTon: scenario.Fuel,
                    seed: Seed);
                var sim = new AirlineYearSimulator(
                    macro: macro,
                    fleet: airline.Fleet,
                    routes: airline.Routes,
                    openingCash: BaseCashWan * diff.CashMult,
                    brandEquity: airline.BrandEquity,
                    irropsSeverity: diff.EventSev,
                    verbose: false);
                var s = sim.RunFiscalYear(1);
                rows.Add(new ComboResult
                {
                    AirlineType = airlineRaw.Name,
                    Scenario = scenario.Name,
                    Difficulty = diff.Name,
                    NetProfit = s.TotalNetProfit,
                    Cost = s.TotalCost,
                    Revenue = s.TotalRevenue,
                    Runway = s.FinalRunwayMonths,
                    Flag = Classify(s),
                });
            }
        }
        return rows;
    }

    private static string Classify(YearSummary s)
    {
        if (s.MinCash < 0f) return "BANKRUPT";
        if (s.FinalRunwayMonths < 2f) return "RED";
        if (s.FinalRunwayMonths < 3f) return "YELLOW";
        if (s.TotalNetProfit > s.OpeningCash * 4f) return "RUNAWAY";
        return "GREEN";
    }

    public static int RunAndPrint()
    {
        var readings = Readings();

        Console.WriteLine("=== 夹具利用率审计（班/机/周）===");
        Console.WriteLine($"合理带：[{BandLow:F0}, {BandHigh:F0}] 班/机/周。营收只由航线决定、不看机队，故「减机到带内」是营收中性的。");
        Console.WriteLine();
        Console.WriteLine("| 航司 | 周班 | 现机数 | 班/机/周 | 在带内 | 减到带内机数 | 改后班/机/周 |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        foreach (var r in readings)
            Console.WriteLine($"| {r.Name} | {r.WeeklyDepartures} | {r.Aircraft} | {r.PerAircraftWeek:F1} | {(r.InBand ? "是" : "**否**")} | {r.SizedAircraft} | {r.SizedPerAircraftWeek:F1} |");
        Console.WriteLine();

        var asIs = RunMatrix(t => t);
        var sized = RunMatrix(t =>
        {
            var reading = Readings().First(x => x.Key == t.Key);
            return Sized(t, reading.SizedAircraft);
        });

        Console.WriteLine("--- 60 组合：现货 vs 减机到带内 ---");
        Console.WriteLine("| 指标 | 现货 | 减机到带内 |");
        Console.WriteLine("|---|---|---|");
        foreach (var flag in new[] { "GREEN", "YELLOW", "RED", "BANKRUPT", "RUNAWAY" })
        {
            int a = asIs.Count(r => r.Flag == flag);
            int b = sized.Count(r => r.Flag == flag);
            Console.WriteLine($"| {flag} | {a} | {b} |");
        }
        float sumA = asIs.Sum(r => r.NetProfit);
        float sumB = sized.Sum(r => r.NetProfit);
        Console.WriteLine($"| 净利合计 | {sumA:F0} | {sumB:F0} |");
        Console.WriteLine();

        Console.WriteLine("--- 每类航司的净利变化（60 组合里该类 15 个情景之年合计）---");
        Console.WriteLine("| 航司 | 现货合计 | 减机合计 | 差 | 说明 |");
        Console.WriteLine("|---|---|---|---|---|");
        foreach (var r in readings)
        {
            float a = asIs.Where(x => x.AirlineType == r.Name).Sum(x => x.NetProfit);
            float b = sized.Where(x => x.AirlineType == r.Name).Sum(x => x.NetProfit);
            string note = r.SizedAircraft == r.Aircraft ? "未改（本来就在带内）"
                : $"机队 {r.Aircraft} → {r.SizedAircraft}";
            Console.WriteLine($"| {r.Name} | {a:F0} | {b:F0} | {b - a:+0;-0;0} | {note} |");
        }
        Console.WriteLine();

        // 异常是否消失：滞胀、标准难度下低成本 vs 支线。
        Console.WriteLine("--- 原异常（滞胀 / 标准难度）---");
        Console.WriteLine("| 口径 | 低成本 | 支线 | 差 |");
        Console.WriteLine("|---|---|---|---|");
        foreach (var (label, rows) in new[] { ("现货", asIs), ("减机到带内", sized) })
        {
            var lcc = rows.First(x => x.AirlineType == "低成本" && x.Scenario == "滞胀" && x.Difficulty == "标准");
            var reg = rows.First(x => x.AirlineType == "支线" && x.Scenario == "滞胀" && x.Difficulty == "标准");
            Console.WriteLine($"| {label} | {lcc.NetProfit:F0} | {reg.NetProfit:F0} | {lcc.NetProfit - reg.NetProfit:+0;-0;0} |");
        }
        Console.WriteLine();

        Directory.CreateDirectory("out");
        File.WriteAllLines(Path.Combine("out", "stress_matrix_sized.csv"), SizedCsv(sized));
        Console.WriteLine($"CSV written: {Path.GetFullPath(Path.Combine("out", "stress_matrix_sized.csv"))}");
        Console.WriteLine();
        Console.WriteLine("[注意] 本模式**不覆盖** STRESS_BALANCE_REPORT.md 与 out/stress_matrix.csv，也不重录基线。");
        Console.WriteLine("       减机是营收中性的，但它同时意味着剩下的飞机飞不完原班表 ——");
        Console.WriteLine("       AirlineYearSimulator 没有运力约束、不会报错，所以这个「可行」是模拟器意义上的，");
        Console.WriteLine("       不是运行意义上的。是否采纳为正式夹具属设计裁定。");
        return 0;
    }

    private static IEnumerable<string> SizedCsv(IReadOnlyList<ComboResult> rows)
    {
        yield return "难度,航司类型,宏观情景,营收,成本,净利,跑道,判定";
        foreach (var r in rows)
            yield return $"{r.Difficulty},{r.AirlineType},{r.Scenario},{r.Revenue:F0},{r.Cost:F0},{r.NetProfit:F0},{r.Runway:F1},{r.Flag}";
    }
}
