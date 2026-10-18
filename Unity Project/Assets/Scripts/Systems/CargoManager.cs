using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 货运业务独立核算（GDB 14.6 · P2 长线运营）。
    /// 每季由 TimeManager 调用：腹舱按活跃航线捎货（边际收益），
    /// 纯货机按机队独立成本中心结算，并回写三项货运 KPI。
    /// </summary>
    public class CargoManager : MonoBehaviour
    {
        public static CargoManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>每季结算货运 P&L：写现金与年净利，更新 KPI，返回文案。</summary>
        public string OnQuarterTick()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.gameOver) return "";
            return SettleCargo(gs);
        }

        /// <summary>核心结算（纯逻辑，供自测复用）。</summary>
        public string SettleCargo(GameState gs)
        {
            if (gs == null) return "";
            var cs = gs.cargo ?? new CargoState();
            gs.cargo = cs;

            // —— 腹舱：按活跃航线各班的机型容量捎货 ——
            float bellyDailyRev = 0f, bellyDailyCost = 0f;
            if (cs.bellyEnabled && gs.activeRoutes != null)
            {
                for (int r = 0; r < gs.activeRoutes.Count; r++)
                {
                    var route = gs.activeRoutes[r];
                    if (route == null) continue;
                    route.EnsureFlights();
                    for (int f = 0; f < route.flights.Count; f++)
                    {
                        var flight = route.flights[f];
                        if (flight == null) continue;
                        var ac = FindAircraft(gs, flight.registration);
                        float tons = BellyTonsFor(gs, ac);
                        bellyDailyRev += CargoEconomics.BellyDailyRevenueWan(tons, cs.bellyUtilization, flight.weeklyFlights);
                        bellyDailyCost += CargoEconomics.BellyDailyCostWan(tons, cs.bellyUtilization, flight.weeklyFlights);
                    }
                }
            }

            // —— 纯货机：独立成本中心 ——
            float freightDailyRev = 0f, freightDailyCost = 0f, freightTonKmDaily = 0f;
            int freighterCount = 0;
            if (gs.fleet != null)
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var a = gs.fleet[i];
                    if (a == null || !a.isActive || a.status == AircraftStatuses.Retired) continue;
                    if (a.cargoTons <= 0f) continue;
                    freighterCount++;
                    float utilRatio = Mathf.Clamp01(cs.freighterDailyUtilization / CargoEconomics.FreighterDailyUtilizationTarget);
                    freightDailyRev += CargoEconomics.FreighterDailyRevenueWan(a.cargoTons) * utilRatio;
                    freightDailyCost += CargoEconomics.FreighterDailyCostWan(a.cargoTons, a.purchasePrice);
                    freightTonKmDaily += CargoEconomics.FreighterDailyTonKm(a.cargoTons) * utilRatio;
                }
            }

            // —— 腹舱吨公里（供 KPI，按航线距离） ——
            float bellyTonKmDaily = 0f;
            if (gs.activeRoutes != null)
            {
                for (int r = 0; r < gs.activeRoutes.Count; r++)
                {
                    var route = gs.activeRoutes[r];
                    if (route == null) continue;
                    route.EnsureFlights();
                    for (int f = 0; f < route.flights.Count; f++)
                    {
                        var flight = route.flights[f];
                        if (flight == null) continue;
                        var ac = FindAircraft(gs, flight.registration);
                        float tons = BellyTonsFor(gs, ac) * Mathf.Clamp01(cs.bellyUtilization);
                        bellyTonKmDaily += tons * Mathf.Max(0f, route.distanceKm) * (flight.weeklyFlights / 7f);
                    }
                }
            }

            // 无任何货运活动时静默清零，避免每季刷屏决策日志
            if (freighterCount == 0 && bellyDailyRev < 0.0001f)
            {
                cs.bellyRevenueWanMonthly = 0f;
                cs.bellyCostWanMonthly = 0f;
                cs.freighterRevenueWanMonthly = 0f;
                cs.freighterCostWanMonthly = 0f;
                cs.cargoTonKmMonthly = 0f;
                cs.tkmRevenueWanMonthly = 0f;
                cs.freighterCount = 0;
                return "";
            }

            // —— 回写 KPI（月口径：日 × 30） ——
            cs.bellyRevenueWanMonthly = bellyDailyRev * 30f;
            cs.bellyCostWanMonthly = bellyDailyCost * 30f;
            cs.freighterRevenueWanMonthly = freightDailyRev * 30f;
            cs.freighterCostWanMonthly = freightDailyCost * 30f;
            cs.cargoTonKmMonthly = (bellyTonKmDaily + freightTonKmDaily) * 30f;
            cs.tkmRevenueWanMonthly = freightDailyRev * 30f; // 吨公里收入以货机段计（吨公里定价）
            cs.freighterCount = freighterCount;

            // —— 现金流：季度净额 = (月净) × 3 ——
            float monthlyNet = (bellyDailyRev - bellyDailyCost) + (freightDailyRev - freightDailyCost);
            float quarterNet = monthlyNet * 3f;
            gs.cash += quarterNet;
            gs.yearNetProfit += quarterNet;

            var sb = new StringBuilder();
            sb.Append($"货运结算：腹舱净 {(bellyDailyRev - bellyDailyCost) * 3f:+#;-#;0} 万");
            if (freighterCount > 0)
                sb.Append($"，货机 {freighterCount} 架净 {(freightDailyRev - freightDailyCost) * 3f:+#;-#;0} 万");
            sb.Append($"（吨公里 {cs.cargoTonKmMonthly:F0} / 季）。");
            gs.AddDecisionLog(sb.ToString());

            return sb.ToString();
        }

        private static Aircraft FindAircraft(GameState gs, string registration)
        {
            if (gs.fleet == null || string.IsNullOrEmpty(registration)) return null;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a != null && string.Equals(a.registration, registration, System.StringComparison.OrdinalIgnoreCase))
                    return a;
            }
            return null;
        }

        private static float BellyTonsFor(GameState gs, Aircraft ac)
        {
            if (ac == null) return CargoEconomics.NarrowbodyBellyTons;
            if (ac.cargoTons > 0f) return 0f; // 货机不参与腹舱捎货
            var cat = DataBase.Instance != null ? DataBase.Instance.GetAircraftType(ac.type) : null;
            return CargoEconomics.BellyCapacityTons(cat != null ? cat.maxPassengers : 180);
        }
    }
}
