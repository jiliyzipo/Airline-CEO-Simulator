using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;

namespace AirlineCEO.Balance;

/// <summary>
/// 接管局「机队 ↔ 网络」自洽体检（--fleetcheck）：对每一家可接管航司，用真实
/// <see cref="TakeoverWorldBuilder.BuildPreview"/> 展开后核对三件事：
/// <list type="number">
/// <item><b>每条航线都要有自己的飞机</b>——旧实现里 ★3 每家固定漏 2 条、全量 72 条，
/// 且因目的地池按近→中→远生成，漏掉的全是洲际线（界面因此写成「未分配 A320neo」）；</item>
/// <item><b>机队规模 ≥ 航线数 + 过渡业务占机数</b>——这是上一条得以成立的前提；</item>
/// <item><b>配到的机型飞得到该航距</b>（同时报「真飞不到」与「5% 余量不足」两档）。</item>
/// </list>
///
/// 存在意义：机队规模（<c>fleetCount</c>）与过渡业务占机数（干租/湿租/包机/培训）
/// 是两处独立配置，历史上各算各的；而选机侧又强制「一架飞机只配一条航线」，
/// 两者一撞就生成出没有飞机的航线。本报告把这条不变量变成可回归的断言。
/// </summary>
internal static class FleetNetworkReport
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

        Console.WriteLine("=== 接管局「机队 ↔ 网络」自洽体检（TakeoverWorldBuilder）===");
        Console.WriteLine($"（{airlines.Count} 家可接管航司；机队规模 = max(设定值, 航线数 + 过渡业务占机数)）");
        Console.WriteLine();

        int noPlane = 0, unfit = 0, noReserve = 0, fleetShort = 0, busyNotTail = 0;
        var problems = new List<string>();
        var unfitList = new List<string>();
        var noReserveList = new List<string>();
        var wideShort = new List<string>();
        var byStar = new Dictionary<int, (int fleet, int busy, int routes, int wide)>();

        foreach (var p in airlines)
        {
            if (p.routes == null || p.routes.Count == 0) continue;

            var busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (p.transitionBusinesses != null)
                foreach (var t in p.transitionBusinesses)
                    if (t != null) busy.Add(t.aircraftReg);

            // 过渡业务必须抽机队尾部（BuildTransitions 从末尾 idx-- 取；BuildFleet 据此补足规模）
            int firstBusy = int.MaxValue;
            for (int i = 0; i < p.aircraft.Count; i++)
                if (busy.Contains(p.aircraft[i].registration)) { firstBusy = i; break; }
            if (firstBusy < p.aircraft.Count - busy.Count) busyNotTail++;

            int wide = 0;
            for (int i = 0; i < p.aircraft.Count; i++)
                if (!busy.Contains(p.aircraft[i].registration) && AircraftCatalog.IsWidebody(p.aircraft[i].type)) wide++;

            byStar.TryGetValue(p.difficulty, out var agg);
            byStar[p.difficulty] = (agg.fleet + p.aircraft.Count, agg.busy + busy.Count,
                                    agg.routes + p.routes.Count, agg.wide + wide);

            if (p.aircraft.Count < p.routes.Count + busy.Count) fleetShort++;

            int airlineNoPlane = 0, airlineUnfit = 0;
            var detail = new List<string>();
            foreach (var r in p.routes)
            {
                if (r == null) continue;
                if (string.IsNullOrEmpty(r.registration))
                {
                    noPlane++; airlineNoPlane++;
                    detail.Add($"{r.origin}→{r.destination}({r.distanceKm:F0}km·无机)");
                    continue;
                }
                var c = AircraftCatalog.Find(r.aircraftType);
                float range = c != null ? c.maxRangeKm : 0f;
                if (range < r.distanceKm)
                {
                    unfit++; airlineUnfit++;
                    unfitList.Add($"{p.hubAirport} {r.origin}→{r.destination} {r.distanceKm:F0}km 由 {r.aircraftType}({range:F0}km)");
                }
                else if (range < r.distanceKm * 1.05f)
                {
                    noReserve++;
                    noReserveList.Add($"{p.hubAirport} {r.origin}→{r.destination} {r.distanceKm:F0}km {r.aircraftType}({range:F0}km)");
                }
            }
            if (airlineUnfit > 0)
            {
                // 归因：需要宽体的远程条数 vs 可用宽体架数（差额即兜底配到窄体的条数）
                int farNeed = 0;
                foreach (var r in p.routes)
                    if (r != null && r.distanceKm > EconomyBalanceParams.FreqBandMidKm) farNeed++;
                wideShort.Add($"{p.hubAirport,-4} ★{p.difficulty} 远程线 {farNeed,3} 条 · 可用宽体 {wide,3} 架 → 缺 {Math.Max(0, farNeed - wide)} 架");
            }
            if (airlineNoPlane > 0 || airlineUnfit > 0) problems.Add(
                $"{p.hubAirport,-4} ★{p.difficulty} 机队 {p.aircraft.Count,3}（过渡 {busy.Count,2}·可用 {p.aircraft.Count - busy.Count,3}）" +
                $" 航线 {p.routes.Count,3}  无机 {airlineNoPlane}  飞不到 {airlineUnfit}   {string.Join("、", detail)}");
        }

        if (problems.Count > 0)
        {
            Console.WriteLine("存在问题的枢纽：");
            foreach (var line in problems) Console.WriteLine("  " + line);
            Console.WriteLine();
        }

        Console.WriteLine("按难度汇总（机队 / 过渡占用 / 可用 / 航线 / 可用宽体）：");
        var keys = new List<int>(byStar.Keys);
        keys.Sort();
        foreach (int k in keys)
        {
            var v = byStar[k];
            Console.WriteLine($"  ★{k}: {v.fleet,4} 架 − 过渡 {v.busy,3} = 可用 {v.fleet - v.busy,4} 架" +
                              $" · 航线 {v.routes,4} 条 · 其中可用宽体 {v.wide,4} 架");
        }

        Console.WriteLine();
        if (unfit > 0)
        {
            Console.WriteLine($"机型飞不到航距的条件（逐条，共 {unfit} 条）：");
            foreach (var x in wideShort) Console.WriteLine("  宽体缺口 → " + x);
            foreach (var x in unfitList) Console.WriteLine("  " + x);
            Console.WriteLine();
        }
        if (noReserve > 0)
        {
            // 5% 运行余量不足 —— 物理上飞得到，只是裕度偏薄；PickAircraft 的启发式允许，
            // 故只作提示，不硬断言（目的地池的「远程兜底」已由可用机队航程钳住，见 §12.8）。
            Console.WriteLine($"[ 注 ] 余量不足 5% 的航线 {noReserve} 条（航程 ≥ 航距，但裕度 < 5%）：");
            foreach (var x in noReserveList) Console.WriteLine("        " + x);
            Console.WriteLine();
        }

        Console.WriteLine("=== 断言 ===");
        int failures = 0;
        Assert(ref failures, noPlane == 0,
            $"无机可配的航线 {noPlane} 条（应为 0；旧缺陷：★3 每家 2 条、全量 72 条，且全是洲际线）");
        Assert(ref failures, fleetShort == 0,
            $"机队规模 ≥ 航线数 + 过渡占机数的航司（违反 {fleetShort} 家）");
        Assert(ref failures, busyNotTail == 0,
            $"过渡业务只从机队尾部抽飞机（违反 {busyNotTail} 家；机队规模补足依赖此约定）");
        Assert(ref failures, unfit == 0,
            $"机型飞得到航距（飞不到 {unfit} 条；旧缺陷：ATL/DFW/DEN 有 10 条洲际线兜底配到 A320neo）");
        Assert(ref failures, byStar.Count > 0 && keys[keys.Count - 1] >= 5,
            $"覆盖全部难度档（最高 ★{keys[keys.Count - 1]}）");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "全部通过 ✓" : $"失败 {failures} 项 ✗");
        return failures == 0 ? 0 : 1;
    }

    static void Assert(ref int failures, bool ok, string label)
    {
        if (!ok) failures++;
        Console.WriteLine($"  [{(ok ? " OK " : "FAIL")}] {label}");
    }
}
