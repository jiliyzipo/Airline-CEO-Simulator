using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.Balance;

/// <summary>
/// K12 离线校验：季末一次性项的预计区间是否覆盖 K10 实测值，以及四个预设的净美元头寸有没有接上。
///
/// 为什么要在离线工具里再验一遍：K12.3 的断言本质是**算式**（敞口 × 波动带 ≥ 实测汇兑额），
/// 不需要 Unity。放在这里，Codex 不关不启引擎也能跑；放在 Unity 里则每次改一个常数都要开工程。
/// 两侧都有不是重复：Unity 侧还测文案与接管档案接线，这一侧只钉数字。
/// </summary>
internal static class QuarterlyOneOffReport
{
    /// <summary>K10 第六轮实测（2026-10-21，人民币单季升值 3.1%）。改动这组数等于改写历史，禁止。</summary>
    static readonly (string Name, float ExposureWan, float MeasuredFxWan)[] K10Measured =
    {
        ("★2 创业 CAN", -213f, 6.6f),
        ("★2 枢纽 NGB", -1100f, 34.1f),
        ("★2 枢纽 ZUH", -1168f, 36.2f),
        ("★3 区域 PEK", -1645f, 51.0f),
        ("★4 老牌 PVG", -8781f, 271.6f),
        ("★5 困境 CTU", -13102f, 405.2f),
    };

    public static int Run()
    {
        bool all = true;
        Console.WriteLine("=== K12 季末一次性项：预计区间 vs K10 实测 ===");
        Console.WriteLine($"单季带宽：Q1 ±{QuarterlyOneOffLogic.BandRatio(1) * 100f:F1}%"
                        + $" · Q2+ ±{QuarterlyOneOffLogic.BandRatio(2) * 100f:F1}%"
                        + $"（现实靠拢 {QuarterlyOneOffLogic.ConvergeBand * 100f:F0}% + 季度漂移上限）");
        Console.WriteLine();
        Console.WriteLine("档位            敞口      区间      实测      重算    判定");

        foreach (var row in K10Measured)
        {
            float band = QuarterlyOneOffLogic.EstimateBandWan(row.ExposureWan, 0f, 1);
            float pnl = row.ExposureWan * -0.031f;   // 人民币升值 3.1% → 净美元负债方获益
            bool signOk = pnl > 0f;
            bool covers = Mathf.Abs(pnl) <= band + 0.05f;
            bool same = Mathf.Abs(pnl - row.MeasuredFxWan) < 5f;
            bool ok = signOk && covers && same;
            all &= ok;
            Console.WriteLine($"{row.Name,-14} {row.ExposureWan,8:F0} {band,8:F0} {row.MeasuredFxWan,9:F1} {pnl,9:F1}    {(ok ? "PASS" : "FAIL")}");
        }

        Console.WriteLine();
        Console.WriteLine("接管预设的净美元头寸（由 TakeoverWorldBuilder 预览档案算出）：");
        var presets = new (string Id, string Name)[]
        {
            (PresetAirlines.Startup, "★2 创业 CTU"),
            (PresetAirlines.Regional, "★3 区域 PEK"),
            (PresetAirlines.Legacy, "★4 老牌 CAN"),
            (PresetAirlines.Distressed, "★5 困境 PVG"),
        };

        float prev = float.PositiveInfinity;
        bool monotonic = true;
        foreach (var (id, name) in presets)
        {
            var p = TakeoverWorldBuilder.BuildPreview(id);
            float exposure = p.netUsdExposureWan;
            float band = QuarterlyOneOffLogic.EstimateBandWan(exposure, 0f, 1);
            bool negative = exposure < 0f;
            bool decreasing = exposure < prev;   // 规模越大，净美元负债越多（更负）
            all &= negative;
            if (prev != float.PositiveInfinity) monotonic &= decreasing;
            prev = exposure;
            Console.WriteLine($"  {name,-12} 敞口 {exposure,9:F0} 万 · 预计 ±{band,6:F0} 万"
                            + $"　{(negative ? "" : "（应为负：国内航司负债 > 国际收入）")}");
        }
        all &= monotonic;
        Console.WriteLine();
        Console.WriteLine($"敞口按规模单调递减：{monotonic}");
        Console.WriteLine(all ? "结论：预计区间覆盖 K10 实测值，且四个预设的净美元头寸已接上。" 
                              : "结论：有断言未通过，见上表。");
        return all ? 0 : 1;
    }
}
