using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 参与现实取数的机场集合：基地 + 当前在飞航线两端（含经停）。
    /// <para>
    /// 四条外部管道（气象实况 / 台风 / 国际 SIGMET / 全球风暴）共用同一份集合，
    /// 保证「同一个机场在各管道里都被认成同一个点」——分开各写一份，迟早会出现
    /// 一边取到 CAN、另一边漏掉经停的情况。
    /// </para>
    /// </summary>
    public static class NetworkAirports
    {
        public static List<string> Pick(GameState gs, int max)
        {
            var set = new List<string>();
            if (gs == null || max <= 0) return set;

            void Add(string code)
            {
                if (string.IsNullOrEmpty(code) || set.Count >= max) return;
                code = code.Trim().ToUpperInvariant();
                if (set.Contains(code)) return;
                // 没有坐标的机场无法参与距离/多边形判定，直接跳过
                if (!AirportGeoCatalog.TryLonLat(code, out _, out _)) return;
                set.Add(code);
            }

            Add(gs.hubIata);
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    Add(r.origin);
                    Add(r.destination);
                    Add(r.stopover);
                    if (set.Count >= max) break;
                }
            }
            return set;
        }
    }
}
