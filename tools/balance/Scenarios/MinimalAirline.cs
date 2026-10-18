using AirlineCEO.Balance.Models;
using AirlineCEO.Logic;

namespace AirlineCEO.Balance.Scenarios;

/// <summary>
/// Phase 1.3 最小航司：5 机 / 3 航线；开销与机型对齐 EconomyBalanceParams。
/// </summary>
public static class MinimalAirline
{
    public const float OpeningCashWan = 500f;
    public const float BrandEquity = 55f;
    public static float MaintPerAircraftWan => EconomyBalanceParams.FleetMonthlyMaintWan;

    public static IReadOnlyList<SimAircraft> CreateFleet() => new[]
    {
        new SimAircraft { Registration = "B-1001", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 1 },
        new SimAircraft { Registration = "B-1002", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 2 },
        new SimAircraft { Registration = "B-1003", Type = "B737-800", PurchasePriceWan = 9600f, AgeYears = 4 },
        new SimAircraft { Registration = "B-1004", Type = "A320neo", PurchasePriceWan = 11000f, AgeYears = 0 },
        new SimAircraft { Registration = "B-1005", Type = "B787-9", PurchasePriceWan = 25000f, AgeYears = 3 },
    };

    public static IReadOnlyList<RouteTemplate> CreateRoutes() => new[]
    {
        new RouteTemplate
        {
            RouteId = "PEK-SHA",
            Origin = "PEK",
            Destination = "SHA",
            DistanceKm = 1080f,
            CompetitorSupply = 0.35f,
            PassengerType = "leisure",
            WeeklyFlights = 14,
            AircraftType = "A320neo",
            Seats = 180,
            LoadFactor = 0.82f,
            GroundStaffAssigned = 3
        },
        new RouteTemplate
        {
            RouteId = "PEK-ICN",
            Origin = "PEK",
            Destination = "ICN",
            DistanceKm = 950f,
            CompetitorSupply = 0.45f,
            PassengerType = "business",
            WeeklyFlights = 7,
            AircraftType = "A320neo",
            Seats = 180,
            LoadFactor = 0.78f,
            GroundStaffAssigned = 3
        },
        new RouteTemplate
        {
            RouteId = "PEK-LHR",
            Origin = "PEK",
            Destination = "LHR",
            DistanceKm = 8150f,
            CompetitorSupply = 0.65f,
            PassengerType = "business",
            WeeklyFlights = 4,
            AircraftType = "B787-9",
            Seats = 290,
            LoadFactor = 0.80f,
            GroundStaffAssigned = 5
        },
    };

    public static float CalcFleetMonthlyMaint(IReadOnlyList<SimAircraft> fleet)
    {
        float sum = 0f;
        foreach (var a in fleet)
        {
            if (a == null || !a.IsActive) continue;
            float uplift = 1f + a.AgeYears * EconomyBalanceParams.MaintAgeUpliftPerYear;
            sum += EconomyBalanceParams.FleetMonthlyMaintWan * uplift;
        }
        return sum;
    }
}
