using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// K12：**季末一次性项必须与月烧钱分开展示**，且预计区间要真的覆盖实测值。
    ///
    /// 事故背景（K10 第六轮 · 2026-10-21）：第 14 项「首季净变动 ≈ 预览×3」的残差
    /// −1.1% ~ −11.2% 全靠 ±25% 带宽吸收。逐项对账后确认**全部**来自季末一次性项：
    /// ★4 人民币单季升值 3.1% 产生汇兑收益 +271.6 万、★5 +405.2 万。
    /// 这些钱确实进现金，但不按月发生、方向不确定 —— 塞进「月均烧钱」就是第二种失真：
    /// ★4 跑道会从 4.0 月显示成 4.33 月，玩家按 4.33 月排资金就会踩空。
    ///
    /// 本回归钉住四件事：
    /// ① **区间必须覆盖实测值（含符号）**：用 K10 记录的净美元头寸与实测汇兑额，
    ///    逐档验证「敞口 × 单季最大变动」不小于实测值，且方向为正（人民币升值为收益）；
    /// ② **区间由模型常数推出，不是拍脑袋**：`BandRatio` 必须等于「现实靠拢 3% + 季度漂移上限」，
    ///    且与 `FxEconomics.RateConvergeBand` 同值 —— 这条专治「两处各写一个 3%」；
    /// ③ **文案必须写明不计入月烧钱**，否则玩家仍会把它加进跑道；
    /// ④ **实际分项与实际↔预计对照都要能出**（K12.2），且超区间时要报「超出」而不是静默通过。
    ///
    /// 注：本回归**不**校验跑道数字是否漂移 —— 那是 K11/`TakeoverSolvencyTests` 的职责，
    /// 在这里再测一遍只会得到一个「两处都会红」的重复断言。
    /// </summary>
    public static class QuarterlyOneOffRegression
    {
        /// <summary>K10 第六轮实测：净美元头寸与当季汇兑收益（汇率升值 3.1%）。</summary>
        static readonly (string Name, float ExposureWan, float MeasuredFxWan)[] K10Measured =
        {
            ("★2 创业 CAN", -213f, 6.6f),
            ("★2 枢纽 NGB", -1100f, 34.1f),
            ("★2 枢纽 ZUH", -1168f, 36.2f),
            ("★3 区域 PEK", -1645f, 51.0f),
            ("★4 老牌 PVG", -8781f, 271.6f),
            ("★5 困境 CTU", -13102f, 405.2f),
        };

        public static bool Run()
        {
            var sb = new StringBuilder();
            sb.Append("K12 季末一次性项 → ");

            // ① 常数同源：区间由模型常数推出，且与现实汇率靠拢带宽一致
            bool bandRatioOk = Mathf.Abs(QuarterlyOneOffLogic.BandRatio(1) - 0.057f) < 1e-4f
                            && Mathf.Abs(QuarterlyOneOffLogic.BandRatio(2) - 0.040f) < 1e-4f
                            && Mathf.Abs(QuarterlyOneOffLogic.BandRatio(4) - 0.040f) < 1e-4f;
            bool convergeSame = Mathf.Abs(QuarterlyOneOffLogic.ConvergeBand - FxEconomics.RateConvergeBand) < 1e-6f;

            // ② 逐档覆盖（含符号）：实测值落在 ±区间内，且方向为收益
            bool covered = true;
            var detail = new StringBuilder();
            foreach (var row in K10Measured)
            {
                float band = QuarterlyOneOffLogic.EstimateBandWan(row.ExposureWan, 0f, 1);
                float pnl = row.ExposureWan * -0.031f;          // 人民币升值 3.1% → 净美元负债方获益
                bool ok = pnl > 0f && Mathf.Abs(pnl - row.MeasuredFxWan) < 1.0f && Mathf.Abs(pnl) <= band + 0.05f;
                if (!ok) covered = false;
                detail.Append($"\n　{row.Name}　敞口 {row.ExposureWan:F0} 万　区间 ±{band:F0} 万"
                            + $"　实测 {row.MeasuredFxWan:F1} 万（重算 {pnl:F1}）{(ok ? " ✓" : " ✗")}");
            }

            // ③ 对冲按比例收窄：80% 对冲后区间应为未对冲的 20%
            float unhedged = QuarterlyOneOffLogic.EstimateBandWan(-1000f, 0f, 1);
            float hedged = QuarterlyOneOffLogic.EstimateBandWan(-1000f, 0.8f, 1);
            bool hedgeScales = Mathf.Abs(hedged - unhedged * 0.2f) < 0.01f;

            // ④ 文案：必须写明不计入月烧钱；敞口极小时不得印出一个凭空的大区间
            string normal = QuarterlyOneOffLogic.EstimateLine(-8781f, 0f, 1);
            string noExposure = QuarterlyOneOffLogic.EstimateLine(0f, 0f, 1);
            bool lineOk = normal.Contains("季末一次性项") && normal.Contains("±")
                          && normal.Contains("不计入月烧钱")
                          && noExposure.Contains("敞口极小")
                          && Mathf.Abs(QuarterlyOneOffLogic.EstimateBandWan(0f, 0f, 1)) < 1e-4f;

            // ⑤ 实际分项（K12.2）：五类齐全、合计等于分项和、正负号都在
            var tally = new OneOffTally();
            tally.AddFx(271.6f);
            tally.AddCargo(-37f);
            tally.AddFuel(-12.5f);
            tally.AddSafety(0f);
            tally.AddRandom(8.4f);
            string breakdown = QuarterlyOneOffLogic.Breakdown(tally);
            bool tallyOk = Mathf.Abs(tally.Total - (271.6f - 37f - 12.5f + 8.4f)) < 0.01f
                           && breakdown.Contains("汇兑") && breakdown.Contains("货运")
                           && breakdown.Contains("油价") && breakdown.Contains("险损")
                           && breakdown.Contains("随机事件")
                           && breakdown.Contains("+271.6") && breakdown.Contains("-37.0");

            // ⑥ 预计 vs 实际对照：区间内说「落在」，超区间必须说「超出」（反面也要钉）
            string inside = QuarterlyOneOffLogic.RetrospectiveLine(tally, -8781f, 0f, 1);
            var blown = new OneOffTally();
            blown.AddFx(900f);
            string outside = QuarterlyOneOffLogic.RetrospectiveLine(blown, -8781f, 0f, 1);
            bool retroOk = inside.Contains("预计 ±") && inside.Contains("落在预计区间内")
                           && outside.Contains("超出预计区间")
                           && inside.Contains("不计入月烧钱");

            // ⑦ 接管档案的净美元头寸已接上，且按规模单调（★2 敞口最小、★5 最大）
            var previews = new[]
            {
                TakeoverWorldBuilder.BuildPreview(PresetAirlines.Startup),
                TakeoverWorldBuilder.BuildPreview(PresetAirlines.Regional),
                TakeoverWorldBuilder.BuildPreview(PresetAirlines.Legacy),
                TakeoverWorldBuilder.BuildPreview(PresetAirlines.Distressed),
            };
            float e2 = previews[0].netUsdExposureWan;
            float e3 = previews[1].netUsdExposureWan;
            float e4 = previews[2].netUsdExposureWan;
            float e5 = previews[3].netUsdExposureWan;
            bool profileOk = e2 < 0f && e3 < 0f && e4 < 0f && e5 < 0f
                             && e2 > e3 && e3 > e4 && e4 > e5;

            // ⑧ 「国际/地区」两套判定必须同口径：档案敞口在接管前算（无 GameState，走 AirportRegions），
            //    接手后的汇兑敞口走 RoutePermitLogic —— 两者对同一条航线必须给同一个答案。
            //    这条专防「新机场只加进 AirportCatalog 没进 CityEconomyCatalog」：那会让预告把国际线
            //    当国内线，敞口小一截，而接手后实际照样吃汇率波动。
            bool classSame = true;
            int routesChecked = 0;
            string mismatch = "";
            foreach (var p in previews)
            {
                if (p?.routes == null) continue;
                for (int i = 0; i < p.routes.Count; i++)
                {
                    var r = p.routes[i];
                    if (r == null) continue;
                    bool byPermit = RoutePermitLogic.IsInternational(r.origin, r.destination);
                    bool byRegion = AirportRegions.IsInternational(r.origin, r.destination);
                    routesChecked++;
                    if (byPermit == byRegion) continue;
                    classSame = false;
                    if (mismatch.Length == 0)
                        mismatch = $"{p.iataCode} {r.origin}—{r.destination}"
                                 + $"（许可判定 {byPermit} / 档案判定 {byRegion}）";
                }
            }
            bool classOk = classSame && routesChecked > 0;

            bool all = bandRatioOk && convergeSame && covered && hedgeScales && lineOk
                       && tallyOk && retroOk && profileOk && classOk;

            sb.Append("带宽=").Append(bandRatioOk)
              .Append(" 常数同源=").Append(convergeSame)
              .Append(" 覆盖实测=").Append(covered)
              .Append(" 对冲收窄=").Append(hedgeScales)
              .Append(" 文案=").Append(lineOk)
              .Append(" 分项=").Append(tallyOk)
              .Append(" 预计对照=").Append(retroOk)
              .Append(" 档案头寸=").Append(profileOk)
              .Append(" 国际判定同源=").Append(classOk)
              .Append("　").Append(all ? "✓" : "✗");
            sb.Append(detail);
            sb.Append($"\n　单季带宽：Q1 ±{QuarterlyOneOffLogic.BandRatio(1) * 100f:F1}%"
                    + $"　Q2+ ±{QuarterlyOneOffLogic.BandRatio(2) * 100f:F1}%"
                    + $"　（现实靠拢 {FxEconomics.RateConvergeBand * 100f:F0}% + 季度漂移上限）");
            sb.Append($"\n　档案净美元头寸：★2 {e2:F0} · ★3 {e3:F0} · ★4 {e4:F0} · ★5 {e5:F0} 万");
            sb.Append($"\n　预计行：{normal}");
            if (!covered) sb.Append("\n　有档位的实测汇兑额超出预计区间（预期区间应覆盖而非贴近）");
            if (!profileOk) sb.Append("\n　接管档案 netUsdExposureWan 未接上或未按规模单调");
            if (!classOk)
                sb.Append($"\n　国际/地区判定不同源（{routesChecked} 条航线）：{mismatch}"
                        + " —— 新机场须同时进 CityEconomyCatalog / AirportRegions 中国名单");

            Debug.Log(sb.ToString());
            return all;
        }
    }
}
