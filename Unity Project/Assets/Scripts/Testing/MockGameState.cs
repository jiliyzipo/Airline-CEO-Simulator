using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;

namespace AirlineCEO.Testing
{
    /// <summary>
    /// 可注入的轻量游戏状态，不依赖场景 / MonoBehaviour。
    /// 供 EditMode / PlayMode 测试与离线管线使用。
    /// </summary>
    [Serializable]
    public class MockGameState
    {
        public float cash = 500f;
        public float monthlyBurnRate = 80f;
        public float financeCrisisBurnBonus;
        public int year = 1;
        public int quarter = 1;
        public float fuelPricePerTon = 850f;
        public float brandEquity = 55f;
        public float boardSatisfaction = 50f;
        public float gdpGrowthRate = 3f;
        public string economicPhase = "boom";
        public List<RouteData> activeRoutes = new List<RouteData>();
        public List<Aircraft> fleet = new List<Aircraft>();

        public float MonthlyRevenue
        {
            get
            {
                float sum = 0f;
                if (activeRoutes == null) return 0f;
                for (int i = 0; i < activeRoutes.Count; i++)
                    sum += activeRoutes[i].dailyProfit * 30f;
                return sum;
            }
        }

        public float FleetMonthlyMaintenance
        {
            get
            {
                if (fleet == null) return 0f;
                int n = 0;
                for (int i = 0; i < fleet.Count; i++)
                {
                    var a = fleet[i];
                    if (a != null && a.isActive && a.status != AircraftStatuses.Retired)
                        n++;
                }
                return n * EconomyBalanceParams.FleetMonthlyMaintWan;
            }
        }

        public float EffectiveMonthlyBurn
        {
            get
            {
                float net = monthlyBurnRate + FleetMonthlyMaintenance + Math.Max(0f, financeCrisisBurnBonus) - MonthlyRevenue;
                return net > 0f ? net : 0f;
            }
        }

        public float GetRunwayMonths() =>
            CashRunnelCalculator.GetRunwayMonths(cash, EffectiveMonthlyBurn);

        public static MockGameState CreateMinimalAirline()
        {
            var gs = new MockGameState
            {
                cash = 500f,
                monthlyBurnRate = EconomyBalanceParams.DefaultFixedMonthlyOpexWan,
                fuelPricePerTon = 850f,
                brandEquity = 55f,
                fleet = new List<Aircraft>
                {
                    new Aircraft { registration = "B-1001", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy },
                    new Aircraft { registration = "B-1002", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy },
                    new Aircraft { registration = "B-1003", type = "B737-800", isActive = true, status = AircraftStatuses.Airworthy },
                    new Aircraft { registration = "B-1004", type = "A320neo", isActive = true, status = AircraftStatuses.Airworthy },
                    new Aircraft { registration = "B-1005", type = "B787-9", isActive = true, status = AircraftStatuses.Airworthy },
                }
            };

            void AddRoute(string id, string o, string d, float km, float competitor, float demand,
                string acType, int seats, string paxType)
            {
                var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
                {
                    distance = km,
                    fuelPricePerTon = gs.fuelPricePerTon,
                    competitorSupply = competitor,
                    demandMultiplier = demand,
                    brandEquity = gs.brandEquity,
                    aircraftType = acType,
                    seats = seats,
                    passengerType = paxType,
                    loadFactor = EconomyBalanceParams.DefaultLoadFactor
                });
                gs.activeRoutes.Add(new RouteData
                {
                    routeId = id,
                    origin = o,
                    destination = d,
                    dailyProfit = rpm.dailyProfit,
                    score = rpm.score,
                    rating = rpm.rating,
                    openedDate = "第 1 年 Q1",
                    distanceKm = km,
                    aircraftType = acType,
                    baseDailyProfit = rpm.dailyProfit
                });
            }

            AddRoute("PEK-SHA", "PEK", "SHA", 1080f, 0.35f, 1.15f, "A320neo", 180, "leisure");
            AddRoute("PEK-ICN", "PEK", "ICN", 950f, 0.45f, 1.25f, "A320neo", 180, "business");
            AddRoute("PEK-LHR", "PEK", "LHR", 8150f, 0.65f, 1.20f, "B787-9", 290, "business");

            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                r.weeklyFlights = r.distanceKm >= 5000f ? 4 : (i == 0 ? 14 : 7);
                r.groundStaffAssigned = OpsEconomics.RecommendedGroundStaff(r.aircraftType);
                // 与真实建线同一套块分配（每条航线 8 个号），否则测试世界的号会互撞（2026-10-13 报障）
                r.flightNumber = $"NV{AirlineCEO.Logic.FlightScheduleFormat.BaseNumberOf(i)}";
                r.registration = gs.fleet[Math.Min(i, gs.fleet.Count - 1)].registration;
                float baseline = r.dailyProfit;
                OpsEconomics.ApplyToRoute(r, baseline, onTimePercent: 88f);
            }
            return gs;
        }
    }
}
