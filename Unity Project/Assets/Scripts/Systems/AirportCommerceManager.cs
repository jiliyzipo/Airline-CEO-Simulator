using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 非航收入与临空商业生态系统（GDB 26.2 · P2 长线运营）。
    /// 由 TimeManager 每季调用：刷新枢纽吞吐 → 解锁档位 → 结算四类设施非航收入。
    /// 战略价值：非航收入不随航空景气周期波动，是衰退/萧条期的「安全垫」。
    /// </summary>
    public class AirportCommerceManager : MonoBehaviour
    {
        public static AirportCommerceManager Instance { get; private set; }

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

        /// <summary>确保 commerce 状态就绪（含四类设施占位）。</summary>
        public static AirportCommerceState EnsureState(GameState gs)
        {
            if (gs == null) return null;
            if (gs.commerce == null) gs.commerce = new AirportCommerceState();
            if (gs.commerce.facilities == null) gs.commerce.facilities = new System.Collections.Generic.List<CommerceFacility>();
            for (int kind = 0; kind < 4; kind++)
            {
                var f = gs.commerce.facilities.Find(x => x.kind == kind);
                if (f == null)
                {
                    gs.commerce.facilities.Add(new CommerceFacility { kind = kind, level = 0, investedWan = 0f });
                }
            }
            return gs.commerce;
        }

        /// <summary>取某设施当前状态（不存在返回 null）。</summary>
        public static CommerceFacility GetFacility(AirportCommerceState state, int kind)
        {
            if (state?.facilities == null) return null;
            return state.facilities.Find(x => x.kind == kind);
        }

        /// <summary>
        /// 刷新枢纽吞吐与解锁档位（不产生现金流，UI 与季度结算前调用）。
        /// 吞吐只统计以枢纽（hubIata）为起讫的航线。
        /// </summary>
        public static void Refresh(GameState gs)
        {
            if (gs == null) return;
            var state = EnsureState(gs);

            int weeklyDepartures = 0;
            if (gs.activeRoutes != null)
            {
                for (int r = 0; r < gs.activeRoutes.Count; r++)
                {
                    var route = gs.activeRoutes[r];
                    if (route == null) continue;
                    if (!IsHubRoute(gs, route)) continue;
                    route.EnsureFlights();
                    if (route.flights != null && route.flights.Count > 0)
                    {
                        for (int f = 0; f < route.flights.Count; f++)
                        {
                            if (route.flights[f] != null)
                                weeklyDepartures += Mathf.Max(0, route.flights[f].weeklyFlights);
                        }
                    }
                    else
                    {
                        weeklyDepartures += Mathf.Max(0, route.weeklyFlights);
                    }
                }
            }

            int avgSeats = AverageSeats(gs);
            float loadFactor = Mathf.Clamp01(gs.loadFactorPercent / 100f);
            state.hubAnnualThroughputWan = AirportCommerceEconomics.HubAnnualThroughputWan(weeklyDepartures, avgSeats, loadFactor);
            state.unlockedTier = AirportCommerceEconomics.UnlockedTier(state.hubAnnualThroughputWan);
        }

        static bool IsHubRoute(GameState gs, RouteData route)
        {
            if (string.IsNullOrEmpty(gs.hubIata)) return true;
            return string.Equals(route.origin, gs.hubIata, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(route.destination, gs.hubIata, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>机队客机平均座位（货机不计）。无客机时回退 180。</summary>
        static int AverageSeats(GameState gs)
        {
            if (gs.fleet == null || gs.fleet.Count == 0) return 180;
            int sum = 0, count = 0;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || !a.isActive || a.status == AircraftStatuses.Retired) continue;
                if (a.cargoTons > 0f) continue; // 货机不载客
                var cat = DataBase.Instance != null ? DataBase.Instance.GetAircraftType(a.type) : null;
                int seats = cat != null ? cat.maxPassengers : EconomyBalanceParams.DefaultSeatsFor(a.type);
                if (seats <= 0) continue;
                sum += seats;
                count++;
            }
            return count > 0 ? Mathf.RoundToInt((float)sum / count) : 180;
        }

        /// <summary>投资升级某类设施到目标档位（受现金与解锁档位约束）。</summary>
        public bool InvestFacility(int kind, int targetLevel, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            return InvestFacility(gs, kind, targetLevel, out message);
        }
        public static bool InvestFacility(GameState gs, int kind, int targetLevel, out string message)
        {
            message = "投资失败";
            if (gs == null) return false;
            var state = EnsureState(gs);
            var f = GetFacility(state, kind);
            if (f == null) { message = "设施不存在"; return false; }
            if (targetLevel <= f.level) { message = $"「{AirportCommerceEconomics.FacilityName(kind)}」已达该档位或更高。"; return false; }
            if (targetLevel > state.unlockedTier)
            {
                message = $"枢纽吞吐不足，暂未解锁 {AirportCommerceEconomics.TierName(targetLevel)}（当前 {AirportCommerceEconomics.TierName(state.unlockedTier)}）。";
                return false;
            }

            float cost = AirportCommerceEconomics.InvestmentWan(kind, targetLevel) - f.investedWan;
            if (cost <= 0f) cost = AirportCommerceEconomics.InvestmentWan(kind, targetLevel);
            if (gs.cash < cost) { message = $"现金不足，升级需再投入 {cost:F0} 万。"; return false; }

            gs.cash -= cost;
            f.investedWan = AirportCommerceEconomics.InvestmentWan(kind, targetLevel);
            f.level = targetLevel;
            state.cumulativeInvestmentWan += cost;

            float annual = AirportCommerceEconomics.FacilityAnnualRevenueWan(kind, targetLevel, state.hubAnnualThroughputWan);
            message = $"「{AirportCommerceEconomics.FacilityName(kind)}」升级至 {AirportCommerceEconomics.TierName(targetLevel)}（投入 {cost:F0} 万，年收入约 {annual:F0} 万）。";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>每季结算非航收入：写现金与年净利，更新 KPI，返回文案。</summary>
        public string OnQuarterTick()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.gameOver) return "";
            return SettleCommerce(gs);
        }

        /// <summary>核心结算（纯逻辑，供自测复用）。</summary>
        public string SettleCommerce(GameState gs)
        {
            if (gs == null) return "";
            var state = EnsureState(gs);
            Refresh(gs);

            float quarterRevenue = 0f;
            int built = 0;
            for (int kind = 0; kind < 4; kind++)
            {
                var f = GetFacility(state, kind);
                if (f == null || f.level <= 0) continue;
                built++;
                quarterRevenue += AirportCommerceEconomics.FacilityQuarterlyRevenueWan(kind, f.level, state.hubAnnualThroughputWan);
            }

            if (built == 0)
            {
                state.annualRevenueWan = 0f;
                return "";
            }

            gs.cash += quarterRevenue;
            gs.yearNetProfit += quarterRevenue;
            state.annualRevenueWan = quarterRevenue * 4f;
            state.cumulativeRevenueWan += quarterRevenue;

            var sb = new StringBuilder();
            sb.Append($"非航收入：枢纽吞吐 {state.hubAnnualThroughputWan:F0} 万人次 · {built} 类设施入账 {quarterRevenue:+#;-#;0} 万");
            sb.Append($"（年化 {state.annualRevenueWan:F0} 万，不受航空景气波动）。");
            gs.AddDecisionLog(sb.ToString());
            return sb.ToString();
        }
    }
}
