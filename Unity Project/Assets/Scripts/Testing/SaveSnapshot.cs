using System;
using AirlineCEO.Data;
using AirlineCEO.Testing;
using UnityEngine;

namespace AirlineCEO.Testing
{
    /// <summary>
    /// 存档快照往返（JsonUtility），不依赖 PlayerPrefs / 场景。
    /// </summary>
    [Serializable]
    public class SaveSnapshot
    {
        public float cash;
        public int year;
        public int quarter;
        public float fuelPricePerTon;
        public float boardSatisfaction;
        public string economicPhase;
        public float gdpGrowthRate;
        public RouteData[] routes;
        public Aircraft[] fleet;

        public static SaveSnapshot FromMock(MockGameState gs)
        {
            return new SaveSnapshot
            {
                cash = gs.cash,
                year = gs.year,
                quarter = gs.quarter,
                fuelPricePerTon = gs.fuelPricePerTon,
                boardSatisfaction = gs.boardSatisfaction,
                economicPhase = gs.economicPhase,
                gdpGrowthRate = gs.gdpGrowthRate,
                routes = gs.activeRoutes?.ToArray() ?? Array.Empty<RouteData>(),
                fleet = gs.fleet?.ToArray() ?? Array.Empty<Aircraft>()
            };
        }

        public MockGameState ToMock()
        {
            return new MockGameState
            {
                cash = cash,
                year = year,
                quarter = quarter,
                fuelPricePerTon = fuelPricePerTon,
                boardSatisfaction = boardSatisfaction,
                economicPhase = economicPhase ?? "boom",
                gdpGrowthRate = gdpGrowthRate,
                activeRoutes = routes != null
                    ? new System.Collections.Generic.List<RouteData>(routes)
                    : new System.Collections.Generic.List<RouteData>(),
                fleet = fleet != null
                    ? new System.Collections.Generic.List<Aircraft>(fleet)
                    : new System.Collections.Generic.List<Aircraft>()
            };
        }

        public string ToJson() => JsonUtility.ToJson(this);

        public static SaveSnapshot FromJson(string json) =>
            JsonUtility.FromJson<SaveSnapshot>(json);
    }
}
