using System;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 座位 → 收入 的运力匹配口径（2026-10-13 CEO 裁定：选机要按「收益最大」排序，就得先有这条通路）。
    ///
    /// 为什么需要这个类：收入模型（<see cref="RPMCalculator"/>）此前是**纯供给驱动**的 ——
    /// `客座率 × 座位数 × 航距 × 收益品质`，没有任何一处看市场需求。于是「座位越多收入越高」，
    /// 一架 A380 摆在 300 人/周的支线上也能满座赚钱，而「合适」二字在模型里没有落点：
    /// 大炮打蚊子不亏钱，小机飞大市场也不缺钱，选机只能靠航程一条硬门槛。
    ///
    /// 本类补上两条**对称**的约束：
    /// <list type="bullet">
    /// <item>**座位装不下** → 溢出客流（市场大于运力，e.g. 窄体飞大市场）：靠 <see cref="RPMCalculator"/>
    /// 新增的 `demandPaxPerFlight` 封顶，座位多则拉得多、座位少只能少拉，收益随座位增长直到封顶；</item>
    /// <item>**市场装不满座位** → 空座（运力大于市场，e.g. 宽体飞薄市场）：<see cref="GaugeFit"/> 按
    /// 「市场单班人次 ÷ (座位 × 软顶上座率)」折减收益，越是大炮打蚊子折得越狠。</item>
    /// </list>
    ///
    /// 口径纪律（与 <c>OpsEconomics</c> 同源）：**已设过基准的航线不得被改派重算改写量级与符号**。
    /// 接管局的机队-网络是叙事配平的（有刻意留下的亏损线），若「指派一架飞机」就把整线利润按 RPM
    /// 重估一遍，一次调令即可把亏损线永久转盈（这正是 <c>CapacityDirectiveLogic</c> 记录过的历史缺陷）。
    /// 故已有基准的航线走**增量口径**：新利润 = 现利润 + (新机毛贡献 − 现机毛贡献) − (新机固定成本 − 现机固定成本)。
    /// </summary>
    public static class CapacityEconomics
    {
        /// <summary>软顶上座率：市场再大，一班也拉不满 95% 以上（留出改签/占座/机组位）。</summary>
        public const float MaxLoadFactor = 0.88f;

        /// <summary>市场单班人次的兜底下限（人次/班，单向）：需求模型缺数据时不至于把收入折成 0。</summary>
        const float MinDemandPerFlight = 20f;

        /// <summary>该机座级：舱位配置优先，未配则按目录典型座级。</summary>
        public static int SeatsOf(Aircraft a)
        {
            if (a == null) return 0;
            int s = a.TotalSeats;
            if (s > 0) return s;
            var c = AircraftCatalog.Find(a.type);
            return c != null ? c.maxPassengers : 0;
        }

        public static int SeatsOfType(string type)
        {
            var c = AircraftCatalog.Find(type);
            return c != null ? c.maxPassengers : 0;
        }

        /// <summary>本线每周单向班次（离港口径；无班次时按航线字段）。</summary>
        public static int WeeklyFlightsOf(RouteData route)
        {
            if (route == null) return 1;
            int n = route.WeeklyFlightsDeparture();
            return n > 0 ? n : Math.Max(1, route.weeklyFlights);
        }

        /// <summary>
        /// 市场单班能给的上限（人次/班，单向）：周客流 ÷ 每周班次。
        /// 引力模型的周客流是「这条线一周能有多少人」；把班次摊上去，就是每班最多能拉多少。
        /// 「班次越多、每班能拉的人越少」这一层也由此自动体现（加密摊薄）。
        /// </summary>
        public static float MarketPaxPerFlight(GameState gs, RouteData route, int weeklyFlights = 0)
        {
            if (route == null) return 0f;
            float wf = weeklyFlights > 0 ? weeklyFlights : WeeklyFlightsOf(route);
            float weekly = 0f;
            if (gs != null)
            {
                var d = RouteGravityModel.EvaluateRoute(gs, route);
                weekly = d.weeklyPax;
            }
            if (weekly <= 0.01f)
            {
                // 需求模型缺席（离线/极早期）：按「本线现有座级 × 软顶上座率」反推一个市场，
                // 避免把收入折成 0；此时座级比较退化为「同等市场」，不会凭数据缺失乱排序。
                return 0f;
            }
            return Math.Max(MinDemandPerFlight, weekly / Math.Max(1f, wf));
        }

        /// <summary>
        /// 大炮打蚊子的折减系数（0..1]：市场装不满该机座位时按比例折减，装得满则为 1（不放大）。
        /// </summary>
        public static float GaugeFit(float marketPaxPerFlight, int seats)
        {
            if (marketPaxPerFlight <= 0f || seats <= 0) return 1f;
            float capacity = seats * MaxLoadFactor;
            if (capacity <= 0.01f) return 1f;
            return (float)Math.Min(1.0, marketPaxPerFlight / capacity);
        }

        /// <summary>该机在本线的折减系数（供排序与文案）。</summary>
        public static float GaugeFit(GameState gs, RouteData route, Aircraft ac, int weeklyFlights = 0)
            => GaugeFit(MarketPaxPerFlight(gs, route, weeklyFlights), SeatsOf(ac));

        /// <summary>按 RPM 口径算「该机执飞本线」的单班毛贡献（万美元/班/日），已含市场需求封顶。</summary>
        public static float RpmGrossPerFlight(GameState gs, RouteData route, Aircraft ac)
        {
            if (route == null || ac == null) return 0f;
            int seats = SeatsOf(ac);
            if (seats <= 0) return 0f;
            var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = route.distanceKm > 0f ? route.distanceKm : 1000f,
                fuelPricePerTon = gs != null ? gs.fuelPricePerTon : 0f,
                competitorSupply = 0.4f,
                demandMultiplier = 1.1f,
                brandEquity = gs != null ? gs.brandAwareness : 50f,
                aircraftType = ac.type,
                seats = seats,
                loadFactor = gs != null && gs.loadFactorPercent > 5f ? gs.loadFactorPercent / 100f : 0f,
                passengerType = "leisure",
                demandPaxPerFlight = MarketPaxPerFlight(gs, route)
            });
            return rpm.dailyProfit;
        }

        /// <summary>
        /// 「该机执飞本线」的航线日净利投影（万美元/日，含航线周期固定成本）—— **提名排序与邮件里的
        /// 「批准后日利」都用这一个数**，避免两处各算一套（改派之前的 B-2006/B-2007 就是栽在这上面）。
        ///
        /// 口径：
        /// <list type="bullet">
        /// <item>本线**已有执飞机**且现机 RPM 口径有效 → 增量口径：现利润 + 毛贡献变化 × 班次 − 固定成本变化；</item>
        /// <item>本线**无机**（待复航）或现机数据缺失 → RPM 口径整体重估。</item>
        /// </list>
        /// </summary>
        public static float ProjectDailyProfit(GameState gs, RouteData route, Aircraft ac)
        {
            if (route == null || ac == null) return 0f;
            int wf = WeeklyFlightsOf(route);
            float dailyFlights = wf / 7f;
            float fixedNew = OpsEconomics.RouteFixedDailyWan(route.distanceKm, ac.type);
            float rpmNew = RpmGrossPerFlight(gs, route, ac);

            var current = FlightGroundLogic.FindAircraft(gs, route.registration);
            float rpmOld = current != null ? RpmGrossPerFlight(gs, route, current) : 0f;
            if (current == null || rpmOld <= 0.01f)
            {
                float gross = rpmNew * dailyFlights;
                return (float)Math.Round(gross - fixedNew, 2);
            }

            float fixedOld = OpsEconomics.RouteFixedDailyWan(route.distanceKm, current.type);
            float delta = (rpmNew - rpmOld) * dailyFlights - (fixedNew - fixedOld);
            return (float)Math.Round(route.dailyProfit + delta, 2);
        }

        /// <summary>该机执飞本线的收益增量（换掉现机后的日利变化，万美元/日）。</summary>
        public static float ProjectDelta(GameState gs, RouteData route, Aircraft ac)
        {
            if (route == null) return 0f;
            return (float)Math.Round(ProjectDailyProfit(gs, route, ac) - route.dailyProfit, 2);
        }

        /// <summary>一句式运力匹配文案（邮件与界面共用，避免两处口径漂移）。</summary>
        public static string FitLine(GameState gs, RouteData route, Aircraft ac)
        {
            if (ac == null) return "";
            int seats = SeatsOf(ac);
            float market = MarketPaxPerFlight(gs, route);
            if (market <= 0f)
                return $"{seats} 座（需求数据不足，未作运力匹配判定）";
            float fit = GaugeFit(market, seats);
            string verdict = fit >= 0.999f
                ? (seats * MaxLoadFactor >= market * 1.2f ? "座级够用" : "座级贴合市场")
                : $"座级偏大（市场仅需 {market:F0} 人次/班，满座需 {market / MaxLoadFactor:F0} 座）";
            return $"{seats} 座　市场 {market:F0} 人次/班（单向）　上座需求 {fit * 100f:F0}%　{verdict}";
        }

        /// <summary>本线需要的「最小够用座级」参考值（供无机航线汇报件给出引进建议）。</summary>
        public static int RecommendedSeats(GameState gs, RouteData route)
        {
            float market = MarketPaxPerFlight(gs, route);
            if (market <= 0f) return 0;
            return Math.Max(1, (int)Math.Ceiling(market / MaxLoadFactor));
        }

        /// <summary>
        /// 在册目录里最大的客机座级（≈ A380 级别）。**用它给「座级 ≥ N 座」这句话做下限校验** ——
        /// 若算出来的 N 比它还大，那句话就没有意义（世上没有这种飞机），真正该说的是「缺班次」。
        /// </summary>
        public static int MaxSeatsInCatalog()
        {
            int best = 0;
            var all = AirlineCEO.Data.AircraftCatalog.All;
            if (all == null) return 0;
            for (int i = 0; i < all.Length; i++)
            {
                var e = all[i];
                if (e == null || e.isFreighter) continue;
                if (e.maxPassengers > best) best = e.maxPassengers;
            }
            return best;
        }
    }
}
