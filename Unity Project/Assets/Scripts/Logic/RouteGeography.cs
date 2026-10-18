using System;
using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>客运航线是否符合现实：禁止同城双场互飞、过短航段。</summary>
    public static class RouteGeography
    {
        /// <summary>已知坐标下短于该距离不作为定期客运（大兴—首都约 50 km）。</summary>
        public const float MinPassengerKm = 100f;

        public static bool SameMetro(string origin, string dest)
        {
            if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(dest)) return false;
            if (string.Equals(origin, dest, StringComparison.OrdinalIgnoreCase)) return true;
            var a = CityEconomyCatalog.Find(origin);
            var b = CityEconomyCatalog.Find(dest);
            if (a == null || b == null) return false;
            if (string.IsNullOrEmpty(a.cityZh) || a.cityZh != b.cityZh) return false;
            if (!string.IsNullOrEmpty(a.countryZh) && !string.IsNullOrEmpty(b.countryZh)
                && a.countryZh != b.countryZh)
                return false;
            return true;
        }

        /// <param name="km">大圆航距；未知坐标时调用方常传入约 2200，不得当真实短航。</param>
        public static bool IsPlausiblePassengerOd(string origin, string dest, float km)
        {
            if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(dest)) return false;
            if (string.Equals(origin, dest, StringComparison.OrdinalIgnoreCase)) return false;
            if (SameMetro(origin, dest)) return false;
            if (km >= 0.5f && km < MinPassengerKm && km < 1500f) return false;
            return true;
        }

        public static string RejectReason(string origin, string dest, float km)
        {
            if (string.Equals(origin, dest, StringComparison.OrdinalIgnoreCase))
                return "基地与目的地不能相同。";
            if (SameMetro(origin, dest))
                return "同城机场之间没有定期客运航线（例如北京大兴与北京首都）。";
            if (km >= 0.5f && km < MinPassengerKm && km < 1500f)
                return $"航距仅 {km:F0} 公里，过短，不符合现实客运航线。";
            return "";
        }

        public static int PruneImplausible(List<RouteData> routes, Func<string, string, float> distanceKm)
        {
            if (routes == null || distanceKm == null) return 0;
            int n = 0;
            for (int i = routes.Count - 1; i >= 0; i--)
            {
                var r = routes[i];
                if (r == null) { routes.RemoveAt(i); n++; continue; }
                float km = distanceKm(r.origin, r.destination);
                if (IsPlausiblePassengerOd(r.origin, r.destination, km)) continue;
                routes.RemoveAt(i);
                n++;
            }
            return n;
        }
    }
}
