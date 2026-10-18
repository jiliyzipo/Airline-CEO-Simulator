using System.Globalization;
using System.Text;
using AirlineCEO.Balance.Models;

namespace AirlineCEO.Balance.Export;

public static class CsvExporter
{
    public static string WriteMonthlyPnL(string path, YearSummary summary)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",",
            "year", "month", "phase", "gdp_pct", "fuel_usd_t", "demand_avg",
            "revenue_wan", "fuel_cost_wan", "landing_crew_wan", "fleet_maint_wan",
            "fixed_opex_wan", "labor_cost_wan", "total_cost_wan", "net_profit_wan", "cash_end_wan",
            "runway_months", "aircraft", "routes"));

        foreach (var m in summary.Months)
        {
            sb.AppendLine(string.Join(",",
                m.Year,
                m.Month,
                m.EconomicPhase,
                F(m.GdpGrowthPct),
                F(m.FuelPriceUsdPerTon),
                F(m.DemandMultiplier),
                F(m.RevenueWan),
                F(m.FuelCostWan),
                F(m.LandingAndCrewWan),
                F(m.FleetMaintWan),
                F(m.FixedOpexWan),
                F(m.LaborCostWan),
                F(m.TotalCostWan),
                F(m.NetProfitWan),
                F(m.CashEndWan),
                F(m.RunwayMonths),
                m.ActiveAircraft,
                m.ActiveRoutes));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return Path.GetFullPath(path);
    }

    private static string F(float v) =>
        v.ToString("0.##", CultureInfo.InvariantCulture);
}
