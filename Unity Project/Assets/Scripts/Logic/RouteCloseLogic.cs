using System;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 关停一条在营航线：**唯一**写台账的入口。
    ///
    /// 分两步，绝不合并：
    /// · <see cref="Build"/> 只读 —— 算出「关掉会发生什么」，给命令台弹确认卡；
    /// · <see cref="Execute"/> 才动手 —— 且**重新取航线、重新算数**，
    ///   不信任确认卡生成时的那份快照（CS 期间可能又过了一个季度、航线可能已被别处撤掉）。
    ///
    /// 为什么关线要单独一个类：它要同时退时刻槽、撤航班环、终止航线补贴、
    /// 从 activeRoutes 摘掉对象、写决策日志。散在 UI 里迟早漏掉其中一步 ——
    /// 漏掉时刻槽 = 白占一条槽，漏掉补贴 = 关线之后还在白拿钱。
    /// </summary>
    public static class RouteCloseLogic
    {
        /// <summary>确认卡要展示的全部事实。</summary>
        public class Plan
        {
            public bool ok;
            public string block;          // ok=false 时的原因

            public string routeId;
            public string origin;
            public string dest;
            public string stopover;

            public string originLabel;
            public string destLabel;

            public float dailyProfit;
            public int weeklyFlights;
            public int flightsPerDay;
            public string aircraft;       // 机号 + 机型；无机可派时为「运力不足」
            public string flightNumber;

            public int slotsToRelease;
            public int ringsToRemove;

            public bool hasSubsidy;
            public float subsidyAnnualWan;
            public int subsidyYears;

            public int routesAfter;
            public bool lastRoute;        // 关掉之后一条航线都不剩
            public string blockNote;      // 提示性风险（不阻止执行）

            public string RouteLabel => $"{origin} — {dest}";
        }

        /// <summary>执行后的实际结果（数字全部取自执行那一刻的台账）。</summary>
        public class Outcome
        {
            public string routeId;
            public string origin;
            public string dest;
            public string originLabel;
            public string destLabel;
            public float dailyProfit;
            public int flightsPerDay;
            public int weeklyFlights;
            public string aircraft;
            public string flightNumber;

            public int slotsReleased;
            public int ringsRemoved;
            public float subsidyAnnualWan;
            public int subsidyYears;

            public int routesBefore;
            public int routesAfter;
            public float networkDailyBefore;
            public float networkDailyAfter;

            public string RouteLabel => $"{origin} — {dest}";
            public float NetworkDailyDelta => networkDailyAfter - networkDailyBefore;
        }

        // ———————————————————————————— 只读：算清楚 ————————————————————————————

        public static Plan Build(GameState gs, RouteData route)
        {
            var p = new Plan();
            if (route == null) { p.block = "未找到这条在营航线。"; return p; }
            if (gs == null) { p.block = "游戏尚未就绪。"; return p; }

            p.ok = true;
            p.routeId = route.routeId;
            p.origin = route.origin;
            p.dest = route.destination;
            p.stopover = route.stopover;
            p.originLabel = AirportCatalog.Label(route.origin);
            p.destLabel = AirportCatalog.Label(route.destination);
            p.flightNumber = route.flightNumber;

            route.EnsureFlights();
            p.dailyProfit = route.dailyProfit;
            p.flightsPerDay = route.FlightCount;
            p.weeklyFlights = route.WeeklyFlightsDeparture();
            p.aircraft = string.IsNullOrEmpty(route.registration)
                ? RouteData.UnassignedAircraftLabel
                : route.registration + (string.IsNullOrEmpty(route.aircraftType) ? "" : " " + route.aircraftType);

            p.slotsToRelease = CountSlots(gs, route.routeId);
            p.ringsToRemove = CountRings(gs, route.routeId);

            var sub = FindSubsidy(gs, route.routeId);
            if (sub != null && sub.yearsRemaining > 0 && sub.annualAmountWan > 0f)
            {
                p.hasSubsidy = true;
                p.subsidyAnnualWan = sub.annualAmountWan;
                p.subsidyYears = sub.yearsRemaining;
            }

            int total = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            p.routesAfter = Math.Max(0, total - 1);
            p.lastRoute = p.routesAfter == 0;
            if (p.lastRoute)
                p.blockNote = "这是最后一条在营航线：关掉之后公司没有可售航班，现金只出不进。";
            else if (p.dailyProfit < 0f)
                p.blockNote = $"本线当前日亏 {Math.Abs(p.dailyProfit):N1} 万美元 —— 关掉会立刻止血。";
            else if (p.flightsPerDay <= 0)
                p.blockNote = "本线已无机可派（长期停飞），关掉只是结清账面。";

            return p;
        }

        // ———————————————————————————— 写：一次办完 ————————————————————————————

        /// <summary>
        /// 执行关线。<paramref name="routeId"/> 来自确认卡；这里**重新取航线**，
        /// 取不到就当作确认作废（不能拿旧快照去删一个已经不在的 index）。
        /// </summary>
        public static bool Execute(GameState gs, string routeId, out Outcome outcome, out string message)
        {
            outcome = null;
            message = "";
            if (gs == null) { message = "游戏尚未就绪。"; return false; }
            if (string.IsNullOrEmpty(routeId)) { message = "缺少航线标识，无法关线。"; return false; }

            var route = FindByRouteId(gs, routeId);
            if (route == null)
            {
                message = "这条航线已不在运营，本次确认作废（未改动任何台账）。";
                return false;
            }

            var o = new Outcome
            {
                routeId = route.routeId,
                origin = route.origin,
                dest = route.destination,
                originLabel = AirportCatalog.Label(route.origin),
                destLabel = AirportCatalog.Label(route.destination),
                dailyProfit = route.dailyProfit,
                flightsPerDay = route.FlightCount,
                weeklyFlights = route.WeeklyFlightsDeparture(),
                aircraft = string.IsNullOrEmpty(route.registration) ? "" : route.registration,
                flightNumber = route.flightNumber,
                routesBefore = gs.activeRoutes != null ? gs.activeRoutes.Count : 0
            };
            o.networkDailyBefore = NetworkDaily(gs);

            // ① 时刻槽：本线占用的全部退还（漏了这步就是白占一条槽，且不会有人提醒）
            o.slotsReleased = RoutePermitLogic.ReleaseByRoute(gs, route.routeId);

            // ② 航班环：两段里任一段是本线的，整个环作废（连班失去一半就没有意义）
            o.ringsRemoved = RemoveRings(gs, route.routeId);

            // ③ 航线补贴：线没了就不该继续拿钱
            var sub = FindSubsidy(gs, route.routeId);
            if (sub != null && sub.annualAmountWan > 0f && sub.yearsRemaining > 0)
            {
                o.subsidyAnnualWan = sub.annualAmountWan;
                o.subsidyYears = sub.yearsRemaining;
                gs.routeSubsidies.RemoveAll(s => s == null
                    || (s.routeId != null && string.Equals(s.routeId, route.routeId, StringComparison.OrdinalIgnoreCase)));
            }

            // ④ 摘掉航线本体
            gs.activeRoutes.Remove(route);
            o.routesAfter = gs.activeRoutes.Count;
            o.networkDailyAfter = NetworkDaily(gs);

            gs.AddDecisionLog("运营：" + Summary(o));
            gs.NotifyChanged();
            message = Summary(o);
            outcome = o;
            return true;
        }

        /// <summary>一行收尾（决策日志与命令台回执共用，避免两处各写一套数字）。</summary>
        public static string Summary(Outcome o)
        {
            if (o == null) return "";
            var sb = new StringBuilder();
            sb.Append($"已停飞 {o.origin}—{o.dest}（日利 {o.dailyProfit:N1} 万美元）");
            if (o.slotsReleased > 0) sb.Append($"，退还时刻槽 {o.slotsReleased} 个");
            if (o.ringsRemoved > 0) sb.Append($"，撤销航班环 {o.ringsRemoved} 个");
            if (o.subsidyAnnualWan > 0f) sb.Append($"，终止补贴 {o.subsidyAnnualWan:N0} 万/年（余 {o.subsidyYears} 年）");
            sb.Append($"；在营航线 {o.routesBefore} → {o.routesAfter}");
            return sb.ToString();
        }

        // ———————————————————————————— 工具 ————————————————————————————

        public static RouteData FindByRouteId(GameState gs, string routeId)
        {
            if (gs?.activeRoutes == null || string.IsNullOrEmpty(routeId)) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r != null && string.Equals(r.routeId, routeId, StringComparison.OrdinalIgnoreCase))
                    return r;
            }
            return null;
        }

        static int CountSlots(GameState gs, string routeId)
        {
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.slots == null || string.IsNullOrEmpty(routeId)) return 0;
            int n = 0;
            for (int i = 0; i < stg.slots.Count; i++)
            {
                var s = stg.slots[i];
                if (s == null) continue;
                // 判据必须与 ReleaseByRoute 完全一致：确认卡说「退 2 个」而实际只退 1 个，
                // 比不说数字更糟 —— 数字是承诺。
                if (string.Equals(s.usedByRouteId, routeId, StringComparison.OrdinalIgnoreCase)) n++;
            }
            return n;
        }

        static int CountRings(GameState gs, string routeId)
        {
            if (gs?.flightRings == null || string.IsNullOrEmpty(routeId)) return 0;
            int n = 0;
            for (int i = 0; i < gs.flightRings.Count; i++)
            {
                var ring = gs.flightRings[i];
                if (ring == null) continue;
                if (Touches(ring, routeId)) n++;
            }
            return n;
        }

        static bool Touches(FlightRing ring, string routeId)
        {
            return (ring.legARouteId != null && string.Equals(ring.legARouteId, routeId, StringComparison.OrdinalIgnoreCase))
                || (ring.legBRouteId != null && string.Equals(ring.legBRouteId, routeId, StringComparison.OrdinalIgnoreCase));
        }

        static int RemoveRings(GameState gs, string routeId)
        {
            if (gs?.flightRings == null || string.IsNullOrEmpty(routeId)) return 0;
            int removed = 0;
            for (int i = gs.flightRings.Count - 1; i >= 0; i--)
            {
                var ring = gs.flightRings[i];
                if (ring == null) { gs.flightRings.RemoveAt(i); removed++; continue; }
                if (!Touches(ring, routeId)) continue;
                gs.flightRings.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        static RouteSubsidy FindSubsidy(GameState gs, string routeId)
        {
            if (gs?.routeSubsidies == null || string.IsNullOrEmpty(routeId)) return null;
            for (int i = 0; i < gs.routeSubsidies.Count; i++)
            {
                var s = gs.routeSubsidies[i];
                if (s == null || string.IsNullOrEmpty(s.routeId)) continue;
                if (string.Equals(s.routeId, routeId, StringComparison.OrdinalIgnoreCase)) return s;
            }
            return null;
        }

        static float NetworkDaily(GameState gs)
        {
            if (gs?.activeRoutes == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r != null) sum += r.dailyProfit;
            }
            return sum;
        }
    }
}
