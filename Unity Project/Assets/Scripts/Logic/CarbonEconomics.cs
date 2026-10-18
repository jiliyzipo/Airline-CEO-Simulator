using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 碳排放与碳配额数值引擎（GDB 15.13 · P2 长线运营 · 财务细分）。
    /// 纯 C#，Unity 与 tools/balance 共用。
    /// EU ETS（欧洲内部航线，免费配额逐年递减）+ CORSIA（国际航线，2027 强制）。
    /// 单位：排放「吨 CO2」；碳价「美元/吨」；货币「万美元」。
    /// </summary>
    public static class CarbonEconomics
    {
        /// <summary>喷气燃料每吨 CO2 排放系数（GDB 15.13.2：3.15 吨 CO2 / 吨燃油）。</summary>
        public const float Co2PerTonFuel = 3.15f;

        /// <summary>CORSIA 抵消至 2019 年水平的比例（GDB 15.13.3：85%）。</summary>
        public const float CorsiaOffsetRatio = 0.85f;

        /// <summary>碳配额机制锚定的起始日历年（游戏财年 1 = 2024）。</summary>
        public const int StartCalendarYear = 2024;

        /// <summary>游戏财年 → 日历年（碳配额 EU ETS / CORSIA 时间轴）。</summary>
        public static int CalendarYear(int fiscalYear) =>
            StartCalendarYear + (Math.Max(1, fiscalYear) - 1);

        /// <summary>EU ETS 免费配额比例（GDB 15.13.2）。</summary>
        public static float EtsFreeQuotaRatio(int gameYear)
        {
            if (gameYear <= 2024) return 0.75f;
            if (gameYear == 2025) return 0.50f;
            return 0f; // 2026 起取消免费配额
        }

        /// <summary>CORSIA 强制期（GDB 15.13.3：2027 起对大部分 ICAO 成员国强制）。</summary>
        public static bool CorsiaMandatory(int gameYear) => gameYear >= 2027;

        /// <summary>碳市场区域标识。</summary>
        public enum CarbonRegime
        {
            None = 0,   // 国内 / 无
            Ets = 1,    // EU ETS（欧洲内部航线）
            Corsia = 2  // CORSIA（国际航线）
        }

        /// <summary>
        /// 航线碳市场归类：两端都在 EEA → EU ETS；跨国（国际）→ CORSIA；国内 → 无。
        /// </summary>
        public static CarbonRegime ClassifyRoute(string origin, string destination)
        {
            bool oEea = AirportRegions.IsEea(origin);
            bool dEea = AirportRegions.IsEea(destination);
            if (oEea && dEea) return CarbonRegime.Ets;

            bool international = !SameCountry(origin, destination);
            return international ? CarbonRegime.Corsia : CarbonRegime.None;
        }

        /// <summary>是否为同一国家（简化为：中国机场之间为国内；其余默认跨国）。</summary>
        static bool SameCountry(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return true;
            bool aCn = AirportRegions.IsChina(a);
            bool bCn = AirportRegions.IsChina(b);
            return aCn && bCn;
        }

        /// <summary>燃油量（吨）→ CO2 排放（吨）。</summary>
        public static float EmissionsTons(float fuelTons) =>
            Math.Max(0f, fuelTons) * Co2PerTonFuel;

        /// <summary>单航线年燃油量（吨）。</summary>
        public static float RouteAnnualFuelTons(float distanceKm, string aircraftType, int weeklyFlights)
        {
            float burnPerKm = EconomyBalanceParams.FuelBurnPerThousandKm(aircraftType) / 1000f;
            float fuelPerFlight = Math.Max(0f, distanceKm) * burnPerKm;
            return fuelPerFlight * Math.Max(1, weeklyFlights) * 52f;
        }

        /// <summary>单航线年排放（吨 CO2）。</summary>
        public static float RouteAnnualEmissionsTons(float distanceKm, string aircraftType, int weeklyFlights) =>
            EmissionsTons(RouteAnnualFuelTons(distanceKm, aircraftType, weeklyFlights));

        /// <summary>EU ETS 需购配额（吨）= 排放 × (1 − 免费配额比例)。</summary>
        public static float EtsRequiredTons(float emissionsTons, int gameYear) =>
            Math.Max(0f, emissionsTons) * (1f - EtsFreeQuotaRatio(gameYear));

        /// <summary>CORSIA 需购配额（吨）= 强制期 ? 排放 × (1 − 85%) : 0。</summary>
        public static float CorsiaRequiredTons(float emissionsTons, int gameYear) =>
            CorsiaMandatory(gameYear)
                ? Math.Max(0f, emissionsTons) * (1f - CorsiaOffsetRatio)
                : 0f;

        /// <summary>配额成本（万美元）= 配额吨 × 碳价（美元/吨）/ 10000。</summary>
        public static float QuotaCostWan(float tons, float carbonPriceUsdPerTon) =>
            Math.Max(0f, tons) * Math.Max(0f, carbonPriceUsdPerTon) / 10000f;
    }
}
