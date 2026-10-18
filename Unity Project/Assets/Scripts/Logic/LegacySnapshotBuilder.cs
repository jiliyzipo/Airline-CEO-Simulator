using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// GameState → LegacyInput 纯快照映射（Unity 侧，依赖 GameState/MonoBehaviour，不进 tools/balance）。
    /// 机队残值/飞行小时/负债/航线展开在此汇总，供 LegacyScoreCalculator（纯 C#）评分。
    /// </summary>
    public static class LegacySnapshotBuilder
    {
        public static LegacyInput FromGameState(GameState gs)
        {
            var input = new LegacyInput();
            if (gs == null) return input;

            input.cash = gs.cash;
            input.yearNetProfit = gs.yearNetProfit;
            input.boardSatisfaction = gs.boardSatisfaction;
            input.safetyRating = gs.safetyRating;
            input.difficulty = gs.difficulty;
            input.ironman = gs.ironmanModeActive;

            float residual = 0f, hours = 0f;
            if (gs.fleet != null)
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var a = gs.fleet[i];
                    if (a == null) continue;
                    residual += FleetEconomics.ResidualValue(a.purchasePrice, a.age);
                    hours += a.flightHours;
                }
            }
            input.fleetResidual = residual;
            input.totalFlightHours = hours;

            float debt = 0f;
            if (gs.activeLoans != null)
            {
                for (int i = 0; i < gs.activeLoans.Count; i++)
                {
                    var L = gs.activeLoans[i];
                    if (L == null || L.type == "equity") continue;
                    debt += System.Math.Max(0f, L.remainingPrincipal);
                }
            }
            input.totalDebt = debt;

            var iatas = new List<string>();
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    iatas.Add(r.origin);
                    iatas.Add(r.destination);
                }
            }
            input.routeIatas = iatas;
            return input;
        }
    }
}
