#if UNITY_EDITOR
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// n35「新航线 RPM 评估台」的口径回归（2026-10-19）。
    ///
    /// 背景：这一页此前是空壳 —— 分数由 `55 + 班次×2 + 品牌×0.15` 拼出来，
    /// 完全不经过 <see cref="RPMCalculator"/>；同一条航线，n35 与「航线 → 开通新航线」
    /// 的预览给出两个毫无关系的数。现在两处都走 <see cref="RpmPreview"/>，
    /// 这组断言钉住三件事：
    /// · 自动机型按航程分档（≥5000 km 宽体 / 否则窄体）；
    /// · 竞对运力上调必须压低日利（说明参数真进了计算，不是摆设）；
    /// · 周班折算用「日利 × 班次 / 7」，不是直接乘班次（1 班/日 才是 RPM 的基准）。
    ///
    /// 与 <see cref="InboxRouteLinkRegression"/> 一样是隔离回归：只碰静态函数和内存 GameState。
    /// </summary>
    public static class RpmPreviewRegression
    {
        public static void Run()
        {
            var go = new GameObject("__rpm_preview_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1; gs.quarter = 1;
                gs.fuelPricePerTon = 850f;
                gs.brandAwareness = 55f;

                // ① 长航线：自动宽体，且距离取大圆距离（PEK–LHR ≈ 8150 km）
                var longHaul = RpmPreview.Build(gs, "PEK", "LHR");
                bool longHaulAuto = longHaul.aircraftType == "B787-9"
                                    && Mathf.Abs(longHaul.distanceKm - 8150f) < 200f
                                    && longHaul.seats == EconomyBalanceParams.DefaultSeatsFor("B787-9");

                // ② 短航线：自动窄体
                var shortHaul = RpmPreview.Build(gs, "PEK", "PVG");
                bool shortHaulAuto = shortHaul.aircraftType == "A320neo"
                                     && Mathf.Abs(shortHaul.distanceKm - 1080f) < 150f;

                // ③ 显式机型覆盖自动选择（人工指定必须生效，否则 n35 的「机型 ◀ ▶」是假的）
                var manual = RpmPreview.Build(gs, "PEK", "PVG", aircraftType: "A330-300");
                bool manualOverride = manual.aircraftType == "A330-300"
                                      && manual.seats == EconomyBalanceParams.DefaultSeatsFor("A330-300")
                                      && manual.seats != shortHaul.seats;

                // ④ 评分与红绿灯同源：rating 必须等于 ToRating(score)
                bool ratingConsistent = shortHaul.output.rating == RPMCalculator.ToRating(shortHaul.output.score)
                                        && longHaul.output.rating == RPMCalculator.ToRating(longHaul.output.score);

                // ⑤ 竞对运力上调 → 日收入与日利都必须下降（参数真的进了计算）
                var lowComp = RpmPreview.Build(gs, "PEK", "LHR", competitorSupply: 0.1f);
                var highComp = RpmPreview.Build(gs, "PEK", "LHR", competitorSupply: 0.8f);
                bool competitorBites = highComp.output.dailyRevenue < lowComp.output.dailyRevenue
                                       && highComp.output.dailyProfit < lowComp.output.dailyProfit;

                // ⑥ 显式传距离（经停航线由调用方给分段和）不得改变口径
                var explicitDist = RpmPreview.Build(gs, "PEK", "LHR", distanceKm: longHaul.distanceKm);
                bool explicitDistSame = Mathf.Abs(explicitDist.output.dailyProfit - longHaul.output.dailyProfit) < 0.001f
                                        && explicitDist.aircraftType == longHaul.aircraftType;

                // ⑦ 周班折算：1 班/日 = 日利本身；7 班/周 = 日利；14 班/周 = 2×日利
                float daily = longHaul.output.dailyProfit;
                bool weeklyMath = Mathf.Abs(RpmPreview.WeeklyFromDaily(daily, 7f) - daily) < 0.001f
                                  && Mathf.Abs(RpmPreview.WeeklyFromDaily(daily, 14f) - daily * 2f) < 0.001f
                                  && Mathf.Abs(RpmPreview.WeeklyFromDaily(daily, 1f) - daily / 7f) < 0.001f;

                // ⑧ 需求系数落在城市经济逻辑声明的区间内（0.55~2.4），不是随手写的常数
                bool demandInRange = longHaul.demandMultiplier >= 0.55f && longHaul.demandMultiplier <= 2.4f
                                     && shortHaul.demandMultiplier >= 0.55f && shortHaul.demandMultiplier <= 2.4f;

                // ⑨ 开线页预填：种子必须一次性消费。
                //    常驻种子的坏处是「上一次开线页填的机场」会盖掉以后从收件箱直接进评估台的航线。
                RpmPreview.ClearSeed();
                bool noSeed = !RpmPreview.TryConsumeSeed(out _, out _);

                RpmPreview.Seed(" pek ", "sha");
                bool seeded = RpmPreview.TryConsumeSeed(out string seedO, out string seedD)
                              && seedO == "PEK" && seedD == "SHA";
                bool consumedOnce = !RpmPreview.TryConsumeSeed(out _, out _);

                // ⑩ 半截种子（只有出发地）不得当作有效预填
                RpmPreview.Seed("PEK", null);
                bool partialIgnored = !RpmPreview.TryConsumeSeed(out _, out _);
                RpmPreview.ClearSeed();

                bool all = longHaulAuto && shortHaulAuto && manualOverride && ratingConsistent
                           && competitorBites && explicitDistSame && weeklyMath && demandInRange
                           && noSeed && seeded && consumedOnce && partialIgnored;

                Debug.Log($"RPM 评估台口径 → 长航线自动机型={longHaulAuto}（{longHaul.aircraftType} {longHaul.distanceKm:F0}km，期望 B787-9）"
                          + $"　短航线自动机型={shortHaulAuto}（{shortHaul.aircraftType} {shortHaul.distanceKm:F0}km，期望 A320neo）"
                          + $"　手动机型={manualOverride}（{manual.aircraftType} {manual.seats} 座）"
                          + $"　评分/红绿灯同源={ratingConsistent}　竞对压低利润={competitorBites}"
                          + $"　显式距离同口径={explicitDistSame}　周班折算={weeklyMath}"
                          + $"　需求系数在区间={demandInRange}"
                          + $"　预填种子一次消费={seeded && consumedOnce}　半截种子忽略={partialIgnored}"
                          + $"　{(all ? "✓" : "✗")}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }
    }
}
#endif
