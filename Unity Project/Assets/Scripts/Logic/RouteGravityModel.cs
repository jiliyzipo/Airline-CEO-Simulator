using System;
using System.Globalization;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航线客货运引力模型（对齐玩家「航线需求自动计算器」）：
    /// G = (gdpA×gdpB) / (km/1000)^1.2
    /// P = (popA×popB)^0.3
    /// H = (枢纽A×枢纽B/10000) × (起讫占比A×起讫占比B/10000)
    /// 周客流 = G×P×H×K
    /// 三舱（旅游并入经济舱）：基准 商务 20% / 超经 15% / 经济 65%，再乘指数。
    /// 商务舱 = 周客流 × 0.20 × (商务指数/100)
    /// 超级经济舱 = 周客流 × 0.15 × ((商务+旅游)/2/100)
    /// 经济舱 = 周客流 × 0.65 × (旅游指数/100)
    /// 货邮吨/周 = ((gdpA+gdpB)/100) × (km/5000)^0.8 × 0.5
    /// GDP 单位：十亿美元；人口：百万；起讫占比：0–100。
    /// </summary>
    public static class RouteGravityModel
    {
        public const float BalanceK = 3.5f;

        public struct Result
        {
            public string origin;
            public string dest;
            public float distanceKm;
            public float gravityG;
            public float popFactorP;
            public float hubOdH;
            public float weeklyPax;
            public float weeklyBusiness;
            public float weeklyLeisure;
            public float weeklyY;
            public float weeklyW;
            public float weeklyC;
            public float weeklyF;
            public float weeklyCargoTons;
            public float bizIndex;
            public float tourIndex;
            public float originBiz;
            public float originTour;
            public float destBiz;
            public float destTour;

            public string FormatBlock()
            {
                return
                    $"航程 {distanceKm:F0} km\n" +
                    $"商务指数 {originBiz:F1} / {destBiz:F1}　　旅游指数 {originTour:F1} / {destTour:F1}\n" +
                    $"周客流合计　{N(weeklyPax)} 人次/周\n" +
                    $"　商务舱　　{N(weeklyC)} 人次/周\n" +
                    $"　超级经济舱　{N(weeklyW)} 人次/周\n" +
                    $"　经济舱　　{N(weeklyY)} 人次/周\n" +
                    $"货邮需求　　{weeklyCargoTons:F1} 吨/周";
            }

        public string FormatOneLine()
        {
            return $"周需（人次/周）商务 {N(weeklyC)} · 超经 {N(weeklyW)} · 经济 {N(weeklyY)}　｜　货邮 {weeklyCargoTons:F1} 吨/周";
        }

            static string N(float v) =>
                Mathf.RoundToInt(v).ToString("N0", CultureInfo.InvariantCulture);
        }

        public static Result Evaluate(GameState gs, string origin, string dest, float distanceKm = 0f)
        {
            var a = CityEconomyLogic.Resolve(gs, origin);
            var b = CityEconomyLogic.Resolve(gs, dest);
            float km = distanceKm > 10f ? distanceKm : FallbackDistance(origin, dest);
            km = Mathf.Max(km, 10f);

            float distThousand = km / 1000f;
            float G = (a.gdpUsdBn * b.gdpUsdBn) / Mathf.Pow(distThousand, 1.2f);
            float P = Mathf.Pow(Mathf.Max(a.popMillion * b.popMillion, 0.01f), 0.3f);
            float H = (a.liveIndex * b.liveIndex / 10000f) * (a.odIndex * b.odIndex / 10000f);
            float total = G * P * H * BalanceK;

            float bizIdx = (a.businessIndex + b.businessIndex) * 0.5f;
            float tourIdx = (a.tourismIndex + b.tourismIndex) * 0.5f;
            float mixIdx = (bizIdx + tourIdx) * 0.5f;
            float business = total * 0.20f * (bizIdx / 100f);
            float premium = total * 0.15f * (mixIdx / 100f);
            float economy = total * 0.65f * (tourIdx / 100f);

            return new Result
            {
                origin = a.iata,
                dest = b.iata,
                distanceKm = km,
                gravityG = G,
                popFactorP = P,
                hubOdH = H,
                weeklyPax = total,
                weeklyBusiness = business,
                weeklyLeisure = economy,
                weeklyY = economy,
                weeklyW = premium,
                weeklyC = business,
                weeklyF = 0f,
                weeklyCargoTons = ((a.gdpUsdBn + b.gdpUsdBn) / 100f) * Mathf.Pow(km / 5000f, 0.8f) * 0.5f,
                bizIndex = bizIdx,
                tourIndex = tourIdx,
                originBiz = a.businessIndex,
                originTour = a.tourismIndex,
                destBiz = b.businessIndex,
                destTour = b.tourismIndex
            };
        }

        public static Result EvaluateRoute(GameState gs, RouteData r)
        {
            if (r == null) return default;
            return Evaluate(gs, r.origin, r.destination, r.distanceKm);
        }

        static float FallbackDistance(string origin, string dest)
        {
            return AirlineCEO.UI.MapWidget.GreatCircleKm(origin, dest);
        }
    }
}
