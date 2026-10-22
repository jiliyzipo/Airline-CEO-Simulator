using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 新开航线可行性口径（RPM 模型的**唯一入口**）。
    ///
    /// 为什么要单独一个类：RPM 评估台（n35）与「CEO 命令台」的可行性答复必须同源。
    /// 之前这一段算式只写在 UI 的 <c>RpmPreview</c> 里，逻辑层（部门答复、命令台）拿不到，
    /// 谁想复用就只能抄一遍 —— 抄完两边就会各算各的（本项目已经吃过「同一职业两个口径」的亏）。
    /// 现在算式落在这里，<c>RpmPreview</c> 只负责把它包装成 UI 的 Plan。
    /// </summary>
    public static class RouteFeasibility
    {
        public const string WideBodyType = "B787-9";
        public const string NarrowBodyType = "A320neo";
        /// <summary>≥ 该航程自动选宽体（与开线页的选型习惯一致）。</summary>
        public const float WideBodyThresholdKm = 5000f;

        /// <summary>按航程自动选机型：≥5000 km 用宽体，否则窄体。</summary>
        public static string AutoAircraft(float distanceKm)
            => distanceKm >= WideBodyThresholdKm ? WideBodyType : NarrowBodyType;

        public struct Plan
        {
            public string originCode;
            public string destCode;
            public float distanceKm;
            public float demandMultiplier;
            public string aircraftType;
            public int seats;
            public RPMCalculator.RPMOutput output;
        }

        /// <param name="aircraftType">空 = 按航程自动（<see cref="AutoAircraft"/>）。</param>
        /// <param name="distanceKm">≤0 = 取大圆距离；经停航线由调用方传入分段之和。</param>
        public static Plan Build(GameState gs, string originCode, string destCode,
            string aircraftType = null, float competitorSupply = 0.4f, float distanceKm = 0f)
        {
            var plan = new Plan
            {
                originCode = originCode,
                destCode = destCode,
                distanceKm = distanceKm > 0f ? distanceKm : AirportGeoCatalog.GreatCircleKm(originCode, destCode)
            };
            plan.aircraftType = string.IsNullOrEmpty(aircraftType)
                ? AutoAircraft(plan.distanceKm)
                : aircraftType;
            plan.seats = EconomyBalanceParams.DefaultSeatsFor(plan.aircraftType);
            plan.demandMultiplier = CityEconomyLogic.OdDemandMultiplier(gs, originCode, destCode);

            plan.output = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = plan.distanceKm,
                fuelPricePerTon = gs != null ? gs.fuelPricePerTon : 850f,
                competitorSupply = competitorSupply,
                demandMultiplier = plan.demandMultiplier,
                brandEquity = gs != null ? gs.brandAwareness : 55f,
                aircraftType = plan.aircraftType,
                seats = plan.seats,
                passengerType = "leisure"
            });

            return plan;
        }
    }
}
