using System;
using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>遗产结算四维评分结果（终局 UI 展示用）。</summary>
    [Serializable]
    public class LegacyScoreResult
    {
        public float total;               // 0~100 总分
        public string grade = "C";        // S / A / B / C
        public float marketCapWan;        // 简化市值（万美元）
        public float marketCapScore;      // 0~100
        public float safetyScore;         // 0~100
        public float coverageScore;       // 0~100
        public float satisfactionScore;   // 0~100
        public int coveredAirports;
        public int coveredCountries;
        public int coveredContinents;
        public float totalFlightHours;
    }

    /// <summary>
    /// 遗产评分输入快照（纯 C#，无 UnityEngine / GameState 依赖，供 tools/balance 离线校验）。
    /// 由 Unity 侧（YearEndEvaluator）从 GameState 汇总得到。
    /// </summary>
    [Serializable]
    public struct LegacyInput
    {
        public float cash;               // 万美元
        public float yearNetProfit;      // 本财年累计净利润（万美元）
        public float boardSatisfaction;  // 0~100
        public SafetyRating safetyRating;
        public DifficultyMode difficulty;
        public bool ironman;
        public float fleetResidual;      // 机队残值合计（万美元）
        public float totalFlightHours;   // 累计飞行小时合计
        public float totalDebt;          // 非股权贷款剩余本金合计（万美元）
        public List<string> routeIatas;  // 全部航线的 origin/destination 展开
    }

    /// <summary>
    /// GDB 第4章 · 终局与遗产结算：光荣退休评分。
    /// 四维加权：市值 40% + 安全飞行 30% + 航线覆盖 20% + 董事会满意度 10%。
    /// 市值用简化公式（现金 + 机队残值 + 年净利×PE − 总负债），无需真实股价模型。
    /// 纯 C#：接收 LegacyInput 快照，不依赖 UnityEngine / GameState。
    /// </summary>
    public static class LegacyScoreCalculator
    {
        public const float PeMultiplier = 8f;              // 市盈率（初值，待数值调优）
        public const float FlightHourFull = 100000f;       // 安全飞行满分基准（FH）
        public const int CoverageAirportFull = 30;         // 覆盖机场满分基准
        public const int CoverageCountryFull = 10;         // 覆盖国家满分基准
        public const int CoverageContinentFull = 5;        // 覆盖大洲满分基准

        private struct Region
        {
            public string Country;
            public string Continent;
            public Region(string country, string continent) { Country = country; Continent = continent; }
        }

        /// <summary>IATA → 国家/大洲。直接取城市经济目录，避免与 CityEconomyCatalog 各存一份而漂移。</summary>
        static bool TryRegion(string iata, out Region region)
        {
            region = default;
            var e = CityEconomyCatalog.Find(iata);
            if (e == null) return false;
            region = new Region(e.countryZh, e.continentZh);
            return true;
        }

        public static float TargetMarketCapFor(DifficultyMode difficulty)
        {
            return difficulty switch
            {
                DifficultyMode.Easy => 5000f,
                DifficultyMode.Hard => 20000f,
                _ => 10000f
            };
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        /// <summary>简化市值：现金 + 机队残值 + max(0, 年净利)×PE − 总负债。</summary>
        public static float MarketCap(LegacyInput input)
        {
            float earnings = Math.Max(0f, input.yearNetProfit) * PeMultiplier;
            return Math.Max(0f, input.cash + input.fleetResidual + earnings - input.totalDebt);
        }

        public static float SafetyScore(LegacyInput input)
        {
            float rating = input.safetyRating switch
            {
                SafetyRating.A => 100f,
                SafetyRating.B => 80f,
                SafetyRating.C => 50f,
                SafetyRating.D => 0f,
                _ => 50f
            };

            float hourScore = Clamp01(input.totalFlightHours / FlightHourFull) * 100f;
            return 0.5f * rating + 0.5f * hourScore;
        }

        public static float CoverageScore(LegacyInput input)
        {
            return CoverageDetail(input).score;
        }

        private static (float score, int airports, int countries, int continents) CoverageDetail(LegacyInput input)
        {
            var airports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var continents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (input.routeIatas != null)
            {
                for (int i = 0; i < input.routeIatas.Count; i++)
                    Collect(airports, countries, continents, input.routeIatas[i]);
            }

            float airportPart = Clamp01(airports.Count / (float)CoverageAirportFull) * 60f;
            float countryPart = Clamp01(countries.Count / (float)CoverageCountryFull) * 25f;
            float continentPart = Clamp01(continents.Count / (float)CoverageContinentFull) * 15f;
            return (airportPart + countryPart + continentPart, airports.Count, countries.Count, continents.Count);
        }

        private static void Collect(HashSet<string> airports, HashSet<string> countries,
            HashSet<string> continents, string iata)
        {
            if (string.IsNullOrWhiteSpace(iata)) return;
            string code = iata.Trim();
            airports.Add(code);
            if (TryRegion(code, out var region))
            {
                countries.Add(region.Country);
                continents.Add(region.Continent);
            }
        }

        /// <summary>计算四维加权总分并映射 S/A/B/C。</summary>
        public static LegacyScoreResult Calc(LegacyInput input)
        {
            var r = new LegacyScoreResult();

            r.marketCapWan = MarketCap(input);
            float target = TargetMarketCapFor(input.difficulty);
            r.marketCapScore = Clamp01(target > 0f ? r.marketCapWan / target : 0f) * 100f;

            r.safetyScore = SafetyScore(input);

            var cov = CoverageDetail(input);
            r.coverageScore = cov.score;
            r.coveredAirports = cov.airports;
            r.coveredCountries = cov.countries;
            r.coveredContinents = cov.continents;

            r.totalFlightHours = input.totalFlightHours;
            r.satisfactionScore = Clamp(input.boardSatisfaction, 0f, 100f);

            r.total = 0.4f * r.marketCapScore
                    + 0.3f * r.safetyScore
                    + 0.2f * r.coverageScore
                    + 0.1f * r.satisfactionScore;

            // GDB 31.1：铁人模式遗产评分 ×1.2（封顶 100）
            if (input.ironman)
                r.total = IronmanEconomics.ApplyIronmanScore(r.total);

            r.grade = GradeFor(r.total);
            return r;
        }

        public static string GradeFor(float total)
        {
            if (total >= 90f) return "S";
            if (total >= 75f) return "A";
            if (total >= 60f) return "B";
            return "C";
        }
    }
}
