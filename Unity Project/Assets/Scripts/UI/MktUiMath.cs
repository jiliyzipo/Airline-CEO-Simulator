using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.UI
{
    /// <summary>MKT 页面共享的航线收益估算（依赖 GameState/宏观，故置于 UI 层）。</summary>
    public static class MktUiMath
    {
        public struct RouteEcon
        {
            public float weeklyRevenueWan;
            public float weeklyCostWan;
            public float weeklyProfitWan;
            public float loadFactor;
            public float raskUsd;
            public float caskUsd;
            public int totalWeeklyFlights;
            public int seats;
            public string passengerType;
        }

        /// <summary>航线总周班次（多班次求和）。</summary>
        public static int TotalWeekly(RouteData r)
        {
            if (r == null) return 7;
            if (r.flights != null && r.flights.Count > 0)
            {
                int s = 0;
                for (int i = 0; i < r.flights.Count; i++)
                    s += Mathf.Max(1, r.flights[i].weeklyFlights);
                return Mathf.Max(1, s);
            }
            return Mathf.Max(1, r.weeklyFlights);
        }

        /// <summary>长航线倾向商务，短航线休闲。</summary>
        public static string PassengerType(RouteData r) =>
            r != null && r.distanceKm >= 3000f ? "business" : "leisure";

        public static RouteEcon Analyze(RouteData r, GameState gs)
        {
            var e = new RouteEcon();
            if (r == null) return e;
            e.passengerType = PassengerType(r);
            e.seats = EconomyBalanceParams.DefaultSeatsFor(r.aircraftType);
            e.totalWeeklyFlights = TotalWeekly(r);

            var macro = MacroEconomyManager.Instance;
            float demand = macro != null ? macro.GetDemandMultiplier(e.passengerType) : 1.2f;
            float fuel = gs != null ? gs.fuelPricePerTon : 850f;
            float brand = gs != null ? gs.brandAwareness : 55f;

            var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = r.distanceKm,
                fuelPricePerTon = fuel,
                competitorSupply = 0.4f,
                demandMultiplier = demand,
                brandEquity = brand,
                seats = e.seats,
                loadFactor = 0f,
                aircraftType = r.aircraftType,
                passengerType = e.passengerType,
                fatigue01 = 0.15f
            });

            e.weeklyRevenueWan = rpm.dailyRevenue * e.totalWeeklyFlights;
            e.weeklyCostWan = rpm.dailyCost * e.totalWeeklyFlights;
            e.weeklyProfitWan = e.weeklyRevenueWan - e.weeklyCostWan;
            e.loadFactor = Mathf.Clamp(EconomyBalanceParams.DefaultLoadFactor * demand, 0.4f, 0.98f);

            float ask = MktEconomics.Ask(e.seats, r.distanceKm, e.totalWeeklyFlights);
            e.raskUsd = MktEconomics.RaskUsd(e.weeklyRevenueWan, ask);
            e.caskUsd = MktEconomics.CaskUsd(e.weeklyCostWan, ask);
            return e;
        }

        // —— 竞争对标（批次 B） ——

        /// <summary>玩家 7 维对标快照。</summary>
        public static MktCompetitionEconomics.BenchInput PlayerBench(GameState gs)
        {
            var c = new MktCompetitionEconomics.BenchInput();
            if (gs == null) return c;
            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int fleet = gs.fleet != null ? gs.fleet.Count : 0;
            var mkt = MktManager.EnsureState(gs);
            float auxPerPax = MktEconomics.AuxRevenuePerPaxUsd(mkt?.ancillary);
            float macro = MacroEconomyManager.Instance != null
                ? MacroEconomyManager.Instance.GetDemandMultiplier("leisure") * 45f
                : 55f;
            c.routes = routes;
            c.marketShare = gs.playerMarketShare;
            c.avgFare = routes > 0 ? EstimatePlayerAvgFare(gs) : 900f;
            c.reputation = gs.brandAwareness;
            float debt = LoanManager.TotalDebt(gs);
            float assets = Mathf.Max(1f, gs.cash + 300f);
            c.debtRatio = Mathf.Clamp01(debt / (assets + debt));
            c.cash = gs.cash;
            c.fleetSize = fleet;
            c.ancillaryScore = Mathf.Clamp(30f + auxPerPax * 2f, 0f, 100f);
            c.macroScore = Mathf.Clamp(macro, 0f, 100f);
            return c;
        }

        static float EstimatePlayerAvgFare(GameState gs)
        {
            float sum = 0f;
            int n = 0;
            if (gs.activeRoutes != null)
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    sum += MktManager.EstimateBaseFareUsd(r);
                    n++;
                }
            return n > 0 ? sum / n : 900f;
        }

        /// <summary>AI 竞对 7 维对标快照。</summary>
        public static MktCompetitionEconomics.BenchInput AiBench(AIAirline ai)
        {
            var c = new MktCompetitionEconomics.BenchInput();
            if (ai == null) return c;
            c.routes = ai.routes != null ? ai.routes.Length : 0;
            c.marketShare = ai.marketShare;
            c.avgFare = ai.avgFare;
            c.reputation = ai.reputation;
            c.debtRatio = ai.debtRatio;
            c.cash = ai.cash;
            c.fleetSize = ai.fleetSize;
            c.ancillaryScore = ai.type == AIAirlineTypes.FullService ? 60f : 45f;
            c.macroScore = ai.state == AIAirlineStates.Expanding ? 70f : 55f;
            return c;
        }
    }
}
