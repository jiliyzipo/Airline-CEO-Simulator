using System;

namespace AirlineCEO.Data
{
    /// <summary>已开通航线运行时记录（Phase 1）。</summary>
    [Serializable]
    public class RouteData
    {
        public string routeId;
        public string origin;
        public string destination;
        public float dailyProfit;   // 万美元/日
        public string openedDate;   // 如「第 1 年 Q1」
        public int score;
        public string rating;
    }
}
