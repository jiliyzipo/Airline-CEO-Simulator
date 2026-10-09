using AirlineCEO.Balance.Economy;
using AirlineCEO.Balance.Models;
using AirlineCEO.Balance.Sim;
using AirlineCEO.Logic;

namespace AirlineCEO.Balance.Stress;

/// <summary>
/// Phase 4 · Week 13 全流程数值压测：
/// 难度（Easy/Normal/Hard） × 航司类型（FSC/LCC/Regional/Charter） × 宏观情景
/// （boom/recovery/recession/oil_shock/stagflation）= 60 组合批量扫参。
/// 复用 <see cref="AirlineYearSimulator"/> 跑一年，输出财务健康度矩阵与平衡报告。
/// </summary>
public sealed class StressTestRunner
{
    public sealed record Difficulty(string Key, string Name, float CashMult, float EventSev, int ReapptThreshold);

    public sealed class MacroScenario
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public float Gdp { get; init; }
        public string Phase { get; init; } = "recovery";
        public float Fuel { get; init; } = 850f;
    }

    public sealed class AirlineType
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public float BrandEquity { get; init; }
        public IReadOnlyList<SimAircraft> Fleet { get; init; } = Array.Empty<SimAircraft>();
        public IReadOnlyList<RouteTemplate> Routes { get; init; } = Array.Empty<RouteTemplate>();
    }

    public sealed class Result
    {
        public required string Difficulty { get; init; }
        public required string AirlineType { get; init; }
        public required string Scenario { get; init; }
        public float OpeningCash { get; init; }
        public float NetProfit { get; init; }
        public float ClosingCash { get; init; }
        public float MinCash { get; init; }
        public float Runway { get; init; }
        public string Credit { get; init; } = "";
        public float OtpClose { get; init; }
        public float Revenue { get; init; }
        public float Cost { get; init; }
        public float Wacc { get; init; }
        public string Flag { get; init; } = "GREEN";
    }

    private const float BaseCashWan = 500f;
    private const int Seed = 42;

    public static IReadOnlyList<Difficulty> Difficulties { get; } = new[]
    {
        new Difficulty("easy", "简单", 1.25f, 0.80f, 50),
        new Difficulty("normal", "标准", 1.00f, 1.00f, 60),
        new Difficulty("hard", "困难", 0.80f, 1.25f, 75),
    };

    public static IReadOnlyList<MacroScenario> Scenarios { get; } = new[]
    {
        new MacroScenario { Key = "boom", Name = "繁荣期", Gdp = 4.5f, Phase = "boom", Fuel = 850f },
        new MacroScenario { Key = "recovery", Name = "复苏期", Gdp = 2.0f, Phase = "recovery", Fuel = 800f },
        new MacroScenario { Key = "recession", Name = "衰退期", Gdp = -2.0f, Phase = "recession", Fuel = 700f },
        new MacroScenario { Key = "oil_shock", Name = "油价冲击", Gdp = 2.0f, Phase = "recovery", Fuel = 1400f },
        new MacroScenario { Key = "stagflation", Name = "滞胀", Gdp = -0.5f, Phase = "recession", Fuel = 1300f },
    };

    public static IReadOnlyList<AirlineType> AirlineTypes { get; } = new[]
    {
        new AirlineType
        {
            Key = "fsc", Name = "全服务", BrandEquity = 60f,
            Fleet = new[]
            {
                new SimAircraft { Registration = "B-1001", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 1 },
                new SimAircraft { Registration = "B-1002", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 2 },
                new SimAircraft { Registration = "B-1003", Type = "B737-800", PurchasePriceWan = 9600f, AgeYears = 4 },
                new SimAircraft { Registration = "B-1004", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 0 },
                new SimAircraft { Registration = "B-1005", Type = "B787-9", PurchasePriceWan = 25000f, AgeYears = 3 },
            },
            Routes = new[]
            {
                Route("PEK-SHA", 1080f, 0.35f, "leisure", 14, "A320neo", 180, 0.82f),
                Route("PEK-ICN", 950f, 0.45f, "business", 7, "A320neo", 180, 0.78f),
                Route("PEK-LHR", 8150f, 0.65f, "business", 4, "B787-9", 290, 0.80f),
            }
        },
        new AirlineType
        {
            Key = "lcc", Name = "低成本", BrandEquity = 40f,
            Fleet = new[]
            {
                new SimAircraft { Registration = "L-2001", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 0 },
                new SimAircraft { Registration = "L-2002", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 0 },
                new SimAircraft { Registration = "L-2003", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 1 },
                new SimAircraft { Registration = "L-2004", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 1 },
                new SimAircraft { Registration = "L-2005", Type = "B737-800", PurchasePriceWan = 9600f, AgeYears = 3 },
                new SimAircraft { Registration = "L-2006", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 2 },
            },
            Routes = new[]
            {
                Route("PEK-SHA", 1080f, 0.70f, "leisure", 21, "A320neo", 189, 0.78f, 0.85f),
                Route("PEK-CAN", 1960f, 0.65f, "leisure", 21, "A320neo", 189, 0.76f, 0.85f),
                Route("PEK-CTU", 1550f, 0.63f, "leisure", 14, "A320neo", 189, 0.77f, 0.85f),
                Route("PEK-HGH", 1130f, 0.60f, "leisure", 14, "A320neo", 189, 0.79f, 0.85f),
            }
        },
        new AirlineType
        {
            Key = "regional", Name = "支线", BrandEquity = 45f,
            Fleet = new[]
            {
                new SimAircraft { Registration = "R-3001", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 2 },
                new SimAircraft { Registration = "R-3002", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 3 },
                new SimAircraft { Registration = "R-3003", Type = "B737-800", PurchasePriceWan = 9600f, AgeYears = 5 },
            },
            Routes = new[]
            {
                Route("PEK-SHA", 1080f, 0.60f, "leisure", 14, "A320neo", 150, 0.80f),
                Route("PEK-CAN", 1960f, 0.55f, "leisure", 14, "A320neo", 150, 0.78f),
                Route("PEK-DLC", 440f, 0.40f, "leisure", 14, "A320neo", 150, 0.82f),
                Route("PEK-TAO", 530f, 0.42f, "leisure", 14, "A320neo", 150, 0.80f),
            }
        },
        new AirlineType
        {
            Key = "charter", Name = "包机", BrandEquity = 35f,
            Fleet = new[]
            {
                new SimAircraft { Registration = "C-4001", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 4 },
                new SimAircraft { Registration = "C-4002", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 5 },
                new SimAircraft { Registration = "C-4003", Type = "B787-9", PurchasePriceWan = 25000f, AgeYears = 6 },
            },
            Routes = new[]
            {
                Route("PEK-SHA", 1080f, 0.50f, "leisure", 7, "A320neo", 180, 0.74f),
                Route("PEK-SYX", 2500f, 0.45f, "leisure", 7, "A320neo", 180, 0.72f),
                Route("PEK-BKK", 3300f, 0.55f, "leisure", 4, "B787-9", 290, 0.75f),
            }
        },
    };

    private static RouteTemplate Route(
        string id, float dist, float supply, string paxType, int weekly,
        string ac, int seats, float load, float yield = 1f) => new RouteTemplate
    {
        RouteId = id,
        Origin = id.Split('-')[0],
        Destination = id.Split('-')[1],
        DistanceKm = dist,
        CompetitorSupply = supply,
        PassengerType = paxType,
        WeeklyFlights = weekly,
        AircraftType = ac,
        Seats = seats,
        LoadFactor = load,
        YieldMultiplier = yield,
        GroundStaffAssigned = EconomyBalanceParams.IsWidebody(ac)
            ? EconomyBalanceParams.RecommendedGroundStaffWide
            : EconomyBalanceParams.RecommendedGroundStaffNarrow
    };

    public static IReadOnlyList<Result> Run()
    {
        var results = new List<Result>();
        foreach (var diff in Difficulties)
        foreach (var airline in AirlineTypes)
        foreach (var scenario in Scenarios)
        {
            var macro = new MacroEconomyEngine(
                initialGdp: scenario.Gdp,
                initialPhase: scenario.Phase,
                initialFuelUsdPerTon: scenario.Fuel,
                seed: Seed);

            float openingCash = BaseCashWan * diff.CashMult;
            var sim = new AirlineYearSimulator(
                macro: macro,
                fleet: airline.Fleet,
                routes: airline.Routes,
                openingCash: openingCash,
                brandEquity: airline.BrandEquity,
                irropsSeverity: diff.EventSev);

            var s = sim.RunFiscalYear(1);
            results.Add(new Result
            {
                Difficulty = diff.Name,
                AirlineType = airline.Name,
                Scenario = scenario.Name,
                OpeningCash = s.OpeningCash,
                NetProfit = s.TotalNetProfit,
                ClosingCash = s.ClosingCash,
                MinCash = s.MinCash,
                Runway = s.FinalRunwayMonths,
                Credit = s.CreditRating,
                OtpClose = s.ClosingOnTimePercent,
                Revenue = s.TotalRevenue,
                Cost = s.TotalCost,
                Wacc = s.WaccPercent,
                Flag = Classify(s)
            });
        }
        return results;
    }

    private static string Classify(YearSummary s)
    {
        if (s.MinCash < 0f) return "BANKRUPT";
        if (s.FinalRunwayMonths < 2f) return "RED";
        if (s.FinalRunwayMonths < 3f) return "YELLOW";
        if (s.TotalNetProfit > s.OpeningCash * 4f) return "RUNAWAY";
        return "GREEN";
    }
}
