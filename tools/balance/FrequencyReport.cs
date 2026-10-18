using AirlineCEO.Logic;

namespace AirlineCEO.Balance;

/// <summary>
/// 班次模型报告（--freq）：用真实 <see cref="RouteFrequencyModel"/> 打印「需求 → 日班次 → 周班次 → 班期」的响应曲线，
/// 并做一组不变量断言。取代此前散落各处的硬编码 km&gt;=5000 ? 4 : 7。
///
/// 说明：航距由本报告直接给定（大致大圆航距），模型本身只吃 (origin, dest, km, aircraft)，
/// 因此这里验证的是「模型响应曲线是否合理」，而非目的地选取（那部分在 TakeoverWorldBuilder）。
/// </summary>
internal static class FrequencyReport
{
    private readonly struct Case
    {
        public readonly string Code;
        public readonly float Km;
        public readonly bool ExpectHigh;
        public readonly bool ExpectLow;

        public Case(string code, float km, bool expectHigh = false, bool expectLow = false)
        {
            Code = code;
            Km = km;
            ExpectHigh = expectHigh;
            ExpectLow = expectLow;
        }
    }

    private static readonly Case[] Routes =
    {
        new Case("PEK-SHA",  1080f, expectHigh: true),   // 黄金干线
        new Case("CAN-HGH",  1050f, expectHigh: true),
        new Case("PEK-SZX",  1950f),
        new Case("PEK-URC",  2400f),
        new Case("SYX-HRB",  3400f),
        new Case("PVG-SIN",  3800f),
        new Case("PEK-SIN",  4450f),
        new Case("PEK-DXB",  5850f),
        new Case("CTU-LHR",  8300f, expectLow: true),
        new Case("PEK-JFK", 11000f, expectLow: true),
    };

    private static readonly string[] Aircraft = { "A320neo", "B787-9" };

    public static int Run()
    {
        int failures = 0;

        Console.WriteLine("=== 班次模型响应曲线（RouteFrequencyModel）===");
        Console.WriteLine("航线        航距km  机型      需求人/日  需班/日  单机上限/日  → 周班次  班期            波次");
        foreach (var c in Routes)
        {
            foreach (var ac in Aircraft)
            {
                var f = RouteFrequencyModel.Compute(
                    c.Code.Substring(0, 3), c.Code.Substring(4), c.Km, ac);
                Console.WriteLine(
                    $"{c.Code,-11}{c.Km,7:F0}  {ac,-9}{f.demandPaxPerDay,10:F1}{f.flightsByDemand,9:F2}{f.flightsByAircraft,13:F2}" +
                    $"  →{f.weeklyFlights,7}  {f.daysOfWeek,-15}{f.waves,3}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== 不变量断言 ===");

        // 1) 班次恒在 1–28
        foreach (var c in Routes)
            foreach (var ac in Aircraft)
            {
                int w = RouteFrequencyModel.Compute(c.Code.Substring(0, 3), c.Code.Substring(4), c.Km, ac).weeklyFlights;
                if (w < EconomyBalanceParams.FreqMinWeekly || w > EconomyBalanceParams.FreqMaxWeekly)
                {
                    Console.WriteLine($"  [FAIL] {c.Code} {ac} 周班次 {w} 越界");
                    failures++;
                }
            }
        Report(ref failures, "周班次恒在 1–28", failures == 0);

        // 2) 短程干线应高班，超远程应低班（不再是一律 4/7 两档）
        int high = RouteFrequencyModel.Compute("PEK", "SHA", 1080f, "A320neo").weeklyFlights;
        int low = RouteFrequencyModel.Compute("PEK", "JFK", 11000f, "B787-9").weeklyFlights;
        Assert(ref failures, high >= 7, $"短程干线 PEK-SHA 周班次 {high} 应 ≥ 7");
        Assert(ref failures, low <= 7, $"超远程 PEK-JFK 周班次 {low} 应 ≤ 7");
        Assert(ref failures, high > low, $"短程 {high} 应高于超远程 {low}（阈值不再一刀切）");

        // 3) 同航距下宽体座位多 → 需求所需班次更少
        float nb = RouteFrequencyModel.Compute("PEK", "URC", 2400f, "A320neo").flightsByDemand;
        float wb = RouteFrequencyModel.Compute("PEK", "URC", 2400f, "B787-9").flightsByDemand;
        Assert(ref failures, wb < nb, $"同航距宽体所需班次 {wb:F2} 应少于窄体 {nb:F2}");

        // 4) 阶梯自洽：Σ波次 == 总班次，且每波 1–7
        for (int w = EconomyBalanceParams.FreqMinWeekly; w <= EconomyBalanceParams.FreqMaxWeekly; w++)
        {
            var waves = RouteFrequencyModel.WavesOf(w);
            int sum = 0;
            foreach (int x in waves) { sum += x; if (x < 1 || x > 7) { Console.WriteLine($"  [FAIL] 周{w} 波次 {x} 越界"); failures++; } }
            if (sum != w) { Console.WriteLine($"  [FAIL] 周{w} 波次和 {sum} != {w}"); failures++; }
        }
        Assert(ref failures, failures == 0, "1–28 全阶梯：Σ波次 == 总班次且每波 1–7");

        // 5) 班期与班次自洽：≤7 时天数 == 班次；>7 时为每日
        int mismatch = 0;
        for (int w = EconomyBalanceParams.FreqMinWeekly; w <= EconomyBalanceParams.FreqMaxWeekly; w++)
        {
            int days = RouteFrequencyModel.DayCount(RouteFrequencyModel.DaysForTotal(w));
            int expect = w <= 7 ? w : 7;
            if (days != expect) { Console.WriteLine($"  [FAIL] 周{w} 班期天数 {days} != {expect}"); mismatch++; }
        }
        Assert(ref failures, mismatch == 0, "班期与班次自洽（≤7 天数相等，>7 每日）");

        // 6) Materialize 的毛贡献不变量：Σ班次毛贡献 == 航线毛贡献
        var route = new AirlineCEO.Data.RouteData
        {
            routeId = "PEK-SHA-inv",
            origin = "PEK",
            destination = "SHA",
            distanceKm = 1080f,
            aircraftType = "A320neo",
            registration = "B-1001",
            dailyProfit = -0.40f,
            baseDailyProfit = -0.40f
        };
        RouteFrequencyModel.Materialize(route);
        float sumGross = 0f;
        foreach (var fl in route.flights) sumGross += fl.dailyProfit;
        bool grossOk = Math.Abs(sumGross - route.GrossDailyProfit) < 0.01f;
        Assert(ref failures, grossOk,
            $"Σ班次毛贡献 {sumGross:F2} == 航线毛贡献 {route.GrossDailyProfit:F2}");
        route.RecalcDailyProfit();
        Assert(ref failures, Math.Abs(route.dailyProfit - (-0.40f)) < 0.01f,
            $"RecalcDailyProfit 幂等（{route.dailyProfit:F2}，期望 -0.40）");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "全部通过 ✓" : $"失败 {failures} 项 ✗");
        return failures == 0 ? 0 : 1;
    }

    private static void Assert(ref int failures, bool ok, string label)
    {
        if (!ok) failures++;
        Console.WriteLine($"  [{(ok ? " OK " : "FAIL")}] {label}");
    }

    private static void Report(ref int failures, string label, bool ok) => Assert(ref failures, ok, label);
}
