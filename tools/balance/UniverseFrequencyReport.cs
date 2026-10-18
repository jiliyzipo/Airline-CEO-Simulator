using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;

namespace AirlineCEO.Balance;

/// <summary>
/// 全航司班次分布报告（--frequniverse）：对 <see cref="PresetAirlines"/> 展开出的每一家可接管航司，
/// 用真实 <see cref="TakeoverWorldBuilder.BuildPreview"/> 构建航线网，统计每周班次分布。
///
/// 存在意义：回答「是不是所有航司的航线都是一周 7 班」。旧实现把班次写死成
/// <c>km &gt;= 5000 ? 4 : 7</c>，结果只剩 4/7 两档；这里给出可分化的证据与回归护栏。
/// </summary>
internal static class UniverseFrequencyReport
{
    public static int Run()
    {
        var airlines = new List<AirlineProfile>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in PresetAirlines.AllSelectable())
        {
            if (seed == null) continue;
            var p = TakeoverWorldBuilder.BuildPreview(seed.id);
            // 枢纽里的 4 个具名预设（CAN/PEK/PVG/CTU）会解析回 preset_* id，需去重。
            if (p == null || !seenIds.Add(p.id)) continue;
            airlines.Add(p);
        }

        Console.WriteLine("=== 全航司班次分布（每周班次，RouteFrequencyModel）===");
        Console.WriteLine($"（{airlines.Count} 家可接管航司 = 4 家具名预设 + 其余按城市经济目录为枢纽生成）");
        Console.WriteLine();

        var histogram = new Dictionary<int, int>();
        var longHaul = new List<int>();   // >= 5000 km
        var shortHaul = new List<int>();  // < 2000 km
        int routeTotal = 0, allSeven = 0, carriersWithHigh = 0, carriersWithLow = 0;
        int outOfRange = 0;
        int nearTotal = 0, midTotal = 0, farTotal = 0;
        var losers = new List<string>();
        var noNear = new List<string>();

        foreach (var p in airlines)
        {
            if (p.routes == null || p.routes.Count == 0) continue;

            var perAirline = new List<int>();
            bool everySeven = true;
            int nearN = 0, midN = 0, farN = 0;
            foreach (var r in p.routes)
            {
                if (r == null) continue;
                int w = r.weeklyFlights;
                perAirline.Add(w);
                routeTotal++;
                histogram[w] = (histogram.TryGetValue(w, out int c) ? c : 0) + 1;
                if (w != 7) everySeven = false;
                if (w < EconomyBalanceParams.FreqMinWeekly || w > EconomyBalanceParams.FreqMaxWeekly) outOfRange++;
                if (r.distanceKm > 0f && r.distanceKm <= EconomyBalanceParams.FreqBandNearKm) nearN++;
                else if (r.distanceKm <= EconomyBalanceParams.FreqBandMidKm) midN++;
                else farN++;
                if (r.distanceKm >= 5000f) longHaul.Add(w);
                else if (r.distanceKm > 0f && r.distanceKm < 2000f) shortHaul.Add(w);
            }
            if (perAirline.Count == 0) continue;

            perAirline.Sort();
            int min = perAirline[0], max = perAirline[perAirline.Count - 1];
            float median = perAirline.Count % 2 == 1
                ? perAirline[perAirline.Count / 2]
                : 0.5f * (perAirline[perAirline.Count / 2 - 1] + perAirline[perAirline.Count / 2]);
            if (everySeven) allSeven++;
            if (max > 7) carriersWithHigh++;
            if (min < 7) carriersWithLow++;
            if (everySeven) losers.Add(p.nameCN + "(" + p.hubAirport + ")");
            if (nearN == 0) noNear.Add(p.nameCN + "(" + p.hubAirport + ")");
            nearTotal += nearN;
            midTotal += midN;
            farTotal += farN;

            Console.WriteLine(
                $"{p.hubAirport,-4} ★{p.difficulty} {perAirline.Count,3}条  " +
                $"班次 {min,2}–{max,-2} 中位{median,4:F0}  近{nearN,3}/中{midN,3}/远{farN,3}  " +
                $"{Clip(p.routes[0].daysOfWeek, 16),-18} {p.nameCN}");
        }

        Console.WriteLine();
        Console.WriteLine($"航司 {airlines.Count} 家 · 航线 {routeTotal} 条");
        Console.WriteLine();
        Console.WriteLine($"航距分带：近程(≤{EconomyBalanceParams.FreqBandNearKm:F0}km) {nearTotal} 条 / " +
                          $"中程(≤{EconomyBalanceParams.FreqBandMidKm:F0}km) {midTotal} 条 / 远程 {farTotal} 条");
        Console.WriteLine();
        Console.WriteLine("=== 每周班次直方图 ===");
        var keys = new List<int>(histogram.Keys);
        keys.Sort();
        int maxCount = 0;
        foreach (int k in keys) maxCount = Math.Max(maxCount, histogram[k]);
        foreach (int k in keys)
        {
            int n = histogram[k];
            int bar = Math.Max(1, (int)Math.Round(n * 40.0 / maxCount));
            Console.WriteLine($"  {k,3} 班 n={n,4}  {new string('#', bar)}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 断言 ===");
        int failures = 0;

        Assert(ref failures, outOfRange == 0, $"所有航线周班次落在 {EconomyBalanceParams.FreqMinWeekly}–{EconomyBalanceParams.FreqMaxWeekly}（越界 {outOfRange}）");
        Assert(ref failures, routeTotal > 0, $"展开出航线 {routeTotal} 条（应 > 0）");
        Assert(ref failures, keys.Count > 2, $"班次档位 {keys.Count} 档（> 2 说明不再是「4/7 两档硬编码」）");
        Assert(ref failures, allSeven * 2 < airlines.Count,
            $"「清一色 7 班」的航司 {allSeven}/{airlines.Count}（应 < 半数）");
        Assert(ref failures, carriersWithHigh > 0, $"存在含 > 7 班的航司 {carriersWithHigh} 家（需求大的干线会加密）");
        Assert(ref failures, carriersWithLow > 0, $"存在含 < 7 班的航司 {carriersWithLow} 家（长航线/小市场会减班）");

        float nearShare = routeTotal > 0 ? (float)nearTotal / routeTotal : 0f;
        Assert(ref failures, nearShare >= 0.30f,
            $"近程航线占比 {nearShare:P0} ≥ 30%（短程高频网是枢纽的骨干）");

        float avgLong = Average(longHaul);
        float avgShort = Average(shortHaul);
        Assert(ref failures, avgLong > 0f && avgShort > 0f && avgLong < avgShort,
            $"≥5000km 平均 {avgLong:F2} 班 < <2000km 平均 {avgShort:F2} 班（航距越远班次越少）");

        if (losers.Count > 0)
            Console.WriteLine($"  [ 注 ] 仍是全 7 班的航司：{string.Join("、", losers)}");
        // 没有近程航线的枢纽 = 城市经济目录里缺少该区域可飞的目的地（数据覆盖问题，非模型问题）。
        if (noNear.Count > 0)
            Console.WriteLine($"  [ 注 ] 无近程航线的枢纽（目录缺少该区域目的地）：{string.Join("、", noNear)}");

        // —— 双口径（离港日 / 值勤日）体检 ——
        // 复现接管界面航线卡片的口径：接管流程会把波次列表压成 1 个条目
        // （RouteData.SyncFlightsFromPrimary()，见 TakeoverWorldBuilder 的克隆路径）。
        // 旧实现的值勤口径按「每天 +1」计、无视 weeklyFlights，25 班/周 被报成 7，
        // 卡片于是并写成「离港25/值勤7班/周」这种量纲不等的数字。
        Console.WriteLine();
        Console.WriteLine("=== 双口径体检（压扁单条目后重算，即接管界面航线卡片的口径）===");
        int dualMismatch = 0, distMismatch = 0, sampleShown = 0;
        foreach (var p in airlines)
        {
            if (p.routes == null) continue;
            foreach (var r in p.routes)
            {
                if (r == null) continue;
                var copy = new RouteData
                {
                    routeId = r.routeId, origin = r.origin, destination = r.destination,
                    weeklyFlights = r.weeklyFlights, daysOfWeek = r.daysOfWeek,
                    depTime = r.depTime, arrTime = r.arrTime, distanceKm = r.distanceKm,
                    aircraftType = r.aircraftType, openedDate = r.openedDate
                };
                copy.SyncFlightsFromPrimary();

                int dep = copy.WeeklyFlightsDeparture();
                int duty = copy.WeeklyFlightsDuty();
                if (dep != duty) dualMismatch++;
                var dist = copy.WeekdayOpsDeparture();
                int sum = 0;
                for (int i = 0; i < dist.Length; i++) sum += dist[i];
                if (sum != dep) distMismatch++;

                if (sampleShown < 3 && dep > 7)
                {
                    sampleShown++;
                    Console.WriteLine($"  {p.nameCN}({p.hubAirport}) {r.origin}→{r.destination}  " +
                                      $"{copy.CardHeadline()}  日分布[{string.Join(",", dist)}]");
                }
            }
        }
        Assert(ref failures, dualMismatch == 0,
            $"压扁单条目后离港周与值勤周恒等（不一致 {dualMismatch} 条）");
        Assert(ref failures, distMismatch == 0,
            $"7 日班次分布合计 == 每周班次（不一致 {distMismatch} 条）");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "全部通过 ✓" : $"失败 {failures} 项 ✗");
        return failures == 0 ? 0 : 1;
    }

    static float Average(List<int> values)
    {
        if (values == null || values.Count == 0) return 0f;
        float s = 0f;
        for (int i = 0; i < values.Count; i++) s += values[i];
        return s / values.Count;
    }

    static string Clip(string s, int len)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= len ? s : s.Substring(0, len);
    }

    static void Assert(ref int failures, bool ok, string label)
    {
        if (!ok) failures++;
        Console.WriteLine($"  [{(ok ? " OK " : "FAIL")}] {label}");
    }
}
