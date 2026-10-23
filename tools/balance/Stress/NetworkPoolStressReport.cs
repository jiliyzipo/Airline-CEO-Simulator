using System.Security.Cryptography;
using System.Text;
using AirlineCEO.Balance.Economy;
using AirlineCEO.Balance.Models;
using AirlineCEO.Balance.Sim;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;

namespace AirlineCEO.Balance.Stress;

/// <summary>
/// K14 · 压测口径补盲：把「机场池」接进压测，而不是只跑四套写死的模板航线。
///
/// 旧 <see cref="StressTestRunner"/> 的 60 组合用的是 <c>StressTestRunner.AirlineTypes</c> 里
/// 手写的 PEK-SHA / PEK-ICN / PEK-LHR 等固定网络，完全不读 <see cref="AirportCatalog"/> /
/// <see cref="CityEconomyCatalog"/>。于是「批次 A 加了 ZUH/WUX/NGB/WNZ」这种改动在压测里
/// 等于没发生 —— K05.3 与 §17 记录的正是这个问题。
///
/// 本报告换一条路：**拿真实的接管航司池做压测样本**。
/// <list type="number">
/// <item>机场池 → <c>PresetAirlines.AllSelectable()</c>（每座城市一家航司，D2 之后机队/航线/现金随客流连续变化）；</item>
/// <item>网络 → <see cref="TakeoverWorldBuilder.BuildPreview(string, ISet{string})"/> 用同一套
///       <c>BuildDestinationPool</c> 生成，只把目的地宇宙收窄到被测的机场池；</item>
/// <item>机组/机型/班次 → <c>RouteData</c> 原样转成 <c>RouteTemplate</c>（无机航线跳过，因为现实中它飞不了）。</item>
/// </list>
///
/// 扫的轴（K14.2）：**机场数 × 批次规模**。机场按任务书 §9b / K06 的批次累加：
/// 出厂池（无新增）→ +批次 A（4 座）→ +批次 B（5 座）→ +批次 B'（拉萨，1 座）。
///
/// 宏观跑**两条**（2026-10-22 实跑修正）：繁荣（GDP 3% / 油价 850）与滞胀（GDP −0.5% / 油价 1300），
/// 都用 seed=42。只用滞胀一条不够：接管样本里绝大多数是 2~5 机、1~3 条短途线的微型航司，
/// 滞胀下它们**无一例外**在年内破产（破产率 100%、平均跑道 0.00），指标饱和后就无法回答
/// 「加机场到底改变了什么」——旧版把「航线密度变化」当成了结果变化，而密度 ≈ 航线数/机队数，
/// 加城市必然改变，等于用同义反复凑绿。繁荣情景把样本拉开档位（部分枢纽盈利、部分仍在
/// 破产边界），破产率/跑道/单位经济才有区分度。两条情景都如实列出，不挑好看的。
///
/// 断言（K14.3，2026-10-22 实跑后按「先查模型、不改断言凑绿」原则重写判据）：
/// <list type="bullet">
/// <item><b>可见性</b>：去掉批次 A 四城后，可接管航司数 / 航线总数 / 网络指纹必须变化；</item>
/// <item><b>结果变化</b>：同一消融下，**至少一条情景**的破产率、平均跑道或 RASK/CASK 必须变化；
///       航线密度单独变化**不算**（它与「航线数/机队数」同源，必然跟着城市数走）；</item>
/// <item><b>饱和披露</b>：某条情景里破产率贴在 0% 或 100% 时，报告必须写明「该情景此指标无区分度」，
///       不得用它下结论；</item>
/// <item><b>批次映射</b>：每批新增城市必须能报出「新增枢纽航司 N 家 / 航线 M 条」。</item>
/// </list>
/// </summary>
public static class NetworkPoolStressReport
{
    // 批次取自任务书 §9b / K06（2026-10-22）。改批次时只改这三组，报告与断言自动跟随。
    public static readonly string[] BatchA = { "ZUH", "WUX", "NGB", "WNZ" };
    public static readonly string[] BatchB = { "NNG", "KWE", "TYN", "HET", "TNA" };
    public static readonly string[] BatchBPrime = { "LXA" };

    static readonly string[] NewAirports = BatchA.Concat(BatchB).Concat(BatchBPrime)
        .Select(s => s.ToUpperInvariant()).ToArray();

    /// <summary>压测情景。Key 只用于 CSV/报告，Make 每次新建引擎（模拟器会推进宏观状态，不能共用）。</summary>
    sealed class Scenario
    {
        public required string Key { get; init; }
        public required string Label { get; init; }
        public required Func<MacroEconomyEngine> Make { get; init; }
    }

    static readonly Scenario Boom = new()
    {
        Key = "boom",
        Label = "繁荣（GDP 3.0% / 油价 850 $/t）",
        Make = () => new MacroEconomyEngine(seed: 42),
    };

    static readonly Scenario Stagflation = new()
    {
        Key = "stagflation",
        Label = "滞胀（GDP −0.5% / 油价 1300 $/t）",
        Make = () => new MacroEconomyEngine(-0.5f, "recession", 1300f, 42),
    };

    static readonly Scenario[] Scenarios = { Boom, Stagflation };

    /// <summary>一条情景下的池级读数。单位：万美元 / 座公里·年。</summary>
    sealed class MacroResult
    {
        public required int AirlineCount { get; init; }
        public required int Bankrupt { get; init; }
        public required float AvgRunway { get; init; }
        public required float TotalRevenue { get; init; }
        public required float TotalCost { get; init; }
        public required double AskSeatKm { get; init; }
        public required float AvgNetProfit { get; init; }

        public float BankruptcyRate => AirlineCount > 0 ? Bankrupt / (float)AirlineCount : 0f;
        /// <summary>池级收入/成本比（RASK ÷ CASK 的等价写法，分母同是 ASK）。&gt;1 = 单位经济为正。</summary>
        public float RevenueCostRatio => TotalCost > 0f ? TotalRevenue / TotalCost : 0f;
        /// <summary>0 或 100% 时破产率没有区分度，用它下结论就是自欺。</summary>
        public bool RateSaturated => AirlineCount == 0 || Bankrupt == 0 || Bankrupt == AirlineCount;
    }

    sealed class PoolRow
    {
        public required string Label { get; init; }
        public required int BatchSize { get; init; }
        public required int AirportCount { get; init; }
        public required int AirlineCount { get; init; }
        public required int NetworkRoutes { get; init; }
        public required int FlyableRoutes { get; init; }
        public required int Aircraft { get; init; }
        public required int OutOfUniverse { get; init; }
        public required string Signature { get; init; }
        public required Dictionary<string, MacroResult> ByMacro { get; init; }

        public float Density => Aircraft > 0 ? FlyableRoutes / (float)Aircraft : 0f;
        public MacroResult this[Scenario s] => ByMacro[s.Key];
    }

    public static int Run()
    {
        var all = CityEconomyCatalog.All
            .Where(e => e != null && !string.IsNullOrEmpty(e.iata))
            .Select(e => e.iata.Trim().ToUpperInvariant())
            .ToHashSet();
        // 机场目录与城市经济表必须同集合（AirportCatalogRegression 已钉）；这里只防「静默空池」。
        var known = AirportCatalog.Airports
            .Where(a => !string.IsNullOrEmpty(a.code))
            .Select(a => a.code.Trim().ToUpperInvariant())
            .ToHashSet();

        var basePool = all.Where(c => !NewAirports.Contains(c)).ToHashSet();        var poolA = basePool.Concat(BatchA).ToHashSet();
        var poolAB = poolA.Concat(BatchB).ToHashSet();
        var poolFull = poolAB.Concat(BatchBPrime).ToHashSet();
        var poolFullNoA = poolFull.Where(c => !BatchA.Contains(c)).ToHashSet();

        var pools = new (string Label, int Batch, HashSet<string> Set)[]
        {
            ("出厂池（无新增批次）", 0, basePool),
            ("+批次 A（4 座）", 4, poolA),
            ("+批次 A+B（9 座）", 9, poolAB),
            ("+批次 A+B+B'（10 座，当前）", 10, poolFull),
            ("当前池 − 批次 A（消融）", 6, poolFullNoA),
        };

        Console.WriteLine("=== K14 · 网络池压测（真实机场池 × 真实接管网络）===");
        Console.WriteLine($"机场池轴：CityEconomyCatalog {all.Count} 场 · AirportCatalog {known.Count} 场"
                        + $" · 新增批次 {NewAirports.Length} 场");
        Console.WriteLine("情景：繁荣 / 滞胀，各一年，seed=42；样本为全部可接管航司");
        Console.WriteLine();

        // ① 默认路径零扰动（与 Unity 侧 `NetworkPoolVisibilityRegression` 同一条断言，离线也能执行）：
        //    `BuildPreview(id)` 与 `BuildPreview(id, 全目录)` 必须逐条航线相同，否则「加了个可选参数」
        //    就已经改了选司界面看到的网络。
        bool defaultSame = true;
        foreach (var seed in PresetAirlines.AllSelectable())
        {
            if (seed == null || string.IsNullOrEmpty(seed.id)) continue;
            string a = RouteKey(TakeoverWorldBuilder.BuildPreview(seed.id));
            string b = RouteKey(TakeoverWorldBuilder.BuildPreview(seed.id, all));
            if (a != b) { defaultSame = false; break; }
        }
        Console.WriteLine($"默认路径零扰动（BuildPreview(id) ≡ BuildPreview(id, 全目录)）：{defaultSame}");
        Console.WriteLine();

        var rows = new List<PoolRow>();
        foreach (var (label, batch, set) in pools)
        {
            var row = RunPool(label, batch, set);
            rows.Add(row);
            Console.WriteLine($"{label} 机场 {row.AirportCount} · 航司 {row.AirlineCount}"
                            + $" · 航线 {row.NetworkRoutes}（可执飞 {row.FlyableRoutes}）· 密度 {row.Density:F2}");
            foreach (var sc in Scenarios)
            {
                var r = row[sc];
                Console.WriteLine($"    {sc.Key,-12} 破产 {r.Bankrupt,3}/{r.AirlineCount,-3}"
                                + $"（{r.BankruptcyRate,6:P1}）· 平均跑道 {r.AvgRunway,6:F2} 月"
                                + $" · 收入/成本 {r.RevenueCostRatio,5:F3}"
                                + $" · 平均净利/家 {r.AvgNetProfit,8:F0} 万"
                                + (r.RateSaturated ? "  ← 破产率饱和，无区分度" : ""));
            }
        }
        Console.WriteLine();

        // —— K14.3 消融：当前池 vs 去掉批次 A ——
        var full = rows.First(r => r.Label.Contains("当前）"));
        var noA = rows.First(r => r.Label.Contains("消融"));
        bool poolChanged = full.AirlineCount != noA.AirlineCount
                        || full.NetworkRoutes != noA.NetworkRoutes
                        || full.Signature != noA.Signature;

        var econChanges = new List<(Scenario Sc, bool Rate, bool Runway, bool Unit)>();
        foreach (var sc in Scenarios)
        {
            var a = full[sc];
            var b = noA[sc];
            bool rate = !a.RateSaturated && !b.RateSaturated
                     && Math.Abs(a.BankruptcyRate - b.BankruptcyRate) > 0.005f;
            bool runway = Math.Abs(a.AvgRunway - b.AvgRunway) > 0.1f;
            bool unit = a.TotalCost > 0f && b.TotalCost > 0f
                     && Math.Abs(a.RevenueCostRatio - b.RevenueCostRatio) > 0.002f;
            econChanges.Add((sc, rate, runway, unit));
        }
        bool resultChanged = econChanges.Any(e => e.Rate || e.Runway || e.Unit);

        Console.WriteLine("--- K14.3 批次 A 消融 ---");
        Console.WriteLine($"  航司 {full.AirlineCount} → {noA.AirlineCount}（{full.AirlineCount - noA.AirlineCount:+0;-0}）"
                        + $" · 航线 {full.NetworkRoutes} → {noA.NetworkRoutes}（{full.NetworkRoutes - noA.NetworkRoutes:+0;-0}）");
        foreach (var e in econChanges)
        {
            var a = full[e.Sc];
            var b = noA[e.Sc];
            Console.WriteLine($"  [{e.Sc.Key}] 破产率 {a.BankruptcyRate:P1} → {b.BankruptcyRate:P1}"
                            + $"（{(e.Rate ? "变化" : a.RateSaturated || b.RateSaturated ? "饱和/未变" : "未变")}）"
                            + $" · 平均跑道 {a.AvgRunway:F2} → {b.AvgRunway:F2}（{(e.Runway ? "变化" : "未变")}）");
            Console.WriteLine($"  [{e.Sc.Key}] 收入/成本 {a.RevenueCostRatio:F3} → {b.RevenueCostRatio:F3}"
                            + $"（{(e.Unit ? "变化" : "未变")}）· 平均净利/家 {a.AvgNetProfit:F0} → {b.AvgNetProfit:F0} 万");
        }
        Console.WriteLine($"  可见性={poolChanged} 结果变化={resultChanged}"
                        + $"{(resultChanged ? "" : " ← 看见了网络却没进模型，压测仍然失明")}");
        Console.WriteLine("  注：航线密度不算判据 —— 它与航线数/机队数同源，城市数一变必然变。");
        Console.WriteLine();

        // —— 批次映射（验收口径：新增城市 → 航司 N 家 / 航线 M 条）——
        Console.WriteLine("--- 批次 → 网络映射 ---");
        var baseRow = rows[0];
        var aRow = rows[1];
        var abRow = rows[2];
        PrintBatch("批次 A", BatchA, baseRow, aRow);
        PrintBatch("批次 B", BatchB, aRow, abRow);
        PrintBatch("批次 B'", BatchBPrime, abRow, full);
        Console.WriteLine();

        bool catalogCovered = NewAirports.All(code => known.Contains(code));
        // 每座新增机场都必须能成为枢纽（可接管航司池的样本），否则「加城市 = 加一家航司」这条规则没生效。
        bool hubAirlines = NewAirports.All(code =>
            PresetAirlines.AllSelectable().Any(p => string.Equals(p.hubAirport, code, StringComparison.OrdinalIgnoreCase)));
        bool hasSamples = full.AirlineCount > 0 && full.FlyableRoutes > 0;
        // ② 收窄必须真的生效：任何池都不得生成宇宙外航线（否则「减城市」只是把参数收进来、没被目的地池读取）。
        bool noOutOfUniverse = rows.All(r => r.OutOfUniverse == 0);
        bool ok = poolChanged && resultChanged && catalogCovered && hubAirlines && hasSamples
                && defaultSame && noOutOfUniverse;

        Console.WriteLine($"默认零扰动={defaultSame} 无宇宙外航线={noOutOfUniverse}");
        Console.WriteLine(ok
            ? "结论：压测已读到机场三表；删/增城市会同时改变网络与压测结果。"
            : "结论：有断言未通过 —— 压测对机场池仍可能结构性失明，见上。");

        WriteArtifacts(rows, full, noA, econChanges, ok, defaultSame, noOutOfUniverse);
        return ok ? 0 : 1;
    }

    /// <summary>
    /// 诊断：把单个样本从「档案 → 可执飞网络 → 一年模拟」摊开，确认宏观轴是不是被打爆（全 100% 破产 = 指标饱和）。
    /// 只跑 <c>--netstress-diag</c>，不参与交付断言。
    /// </summary>
    public static int Diag()
    {
        var pool = CityEconomyCatalog.All
            .Where(e => e != null && !string.IsNullOrEmpty(e.iata))
            .Select(e => e.iata.Trim().ToUpperInvariant())
            .ToHashSet();

        var seeds = PresetAirlines.AllSelectable()
            .Where(p => p != null && !string.IsNullOrEmpty(p.hubAirport))
            .OrderBy(p => p.id, StringComparer.Ordinal)
            .Take(6)
            .ToList();

        Console.WriteLine("id          枢纽 现金   机队 航线  月收入  月固定  月租+债+险  净烧钱/月");
        foreach (var seed in seeds)
        {
            var p = TakeoverWorldBuilder.BuildPreview(seed.id, pool);
            if (p == null) continue;
            var fleet = ToSimFleet(p);
            var routes = ToSimRoutes(p);
            float lease = 0f;
            if (p.aircraft != null)
                for (int i = 0; i < p.aircraft.Count; i++)
                    if (p.aircraft[i] != null && p.aircraft[i].isActive)
                        lease += Math.Max(0f, p.aircraft[i].monthlyLeaseWan);
            Console.WriteLine($"{p.id,-11} {p.hubAirport,-4} {p.cash,7:F0} {fleet.Count,4} {routes.Count,4}"
                            + $" {p.monthlyRevenue,7:F0} {p.monthlyCost,7:F0} {lease + p.monthlyDebtService + p.monthlyInsurance,11:F0}"
                            + $" {p.monthlyNetBurn,10:F0}");
            if (p.routes != null)
            {
                for (int i = 0; i < p.routes.Count && i < 4; i++)
                {
                    var r = p.routes[i];
                    if (r == null) continue;
                    Console.WriteLine($"    route {r.routeId,-16} {r.distanceKm,6:F0}km {r.aircraftType,-10}"
                                    + $" 周{r.WeeklyFlightsDeparture(),2}班 日利润 {r.dailyProfit,6:F2} 万");
                }
            }
            foreach (var sc in Scenarios)
            {
                var sim = new AirlineYearSimulator(
                    macro: sc.Make(),
                    fleet: fleet,
                    routes: routes,
                    openingCash: p.cash,
                    fixedMonthlyOpex: FixedMonthlyOpexWan(p),
                    brandEquity: 55f,
                    irropsSeverity: 1f,
                    verbose: false);
                var s = sim.RunFiscalYear(1);
                Console.WriteLine($"    {sc.Key,-12} 收入 {s.TotalRevenue,7:F0} 成本 {s.TotalCost,7:F0}"
                                + $" 净利 {s.TotalNetProfit,8:F0} 最低 {s.MinCash,8:F0}"
                                + $" 期末 {s.ClosingCash,8:F0} 跑道 {s.FinalRunwayMonths,6:F2}");
            }
        }
        return 0;
    }

    static PoolRow RunPool(string label, int batch, HashSet<string> pool)
    {
        int airlines = 0, networkRoutes = 0, flyable = 0, aircraft = 0, outOfUniverse = 0;
        var sig = new StringBuilder();

        // 每情景一套累计器：同一批 seeds 只生成一次网络，省掉重复 BuildPreview。
        var acc = Scenarios.ToDictionary(s => s.Key, _ => new Accumulator());
        var asks = Scenarios.ToDictionary(s => s.Key, _ => 0d);

        var seeds = PresetAirlines.AllSelectable()
            .Where(p => p != null && !string.IsNullOrEmpty(p.hubAirport)
                        && pool.Contains(p.hubAirport.Trim().ToUpperInvariant()))
            .OrderBy(p => p.id, StringComparer.Ordinal)
            .ToList();

        foreach (var seed in seeds)
        {
            var p = TakeoverWorldBuilder.BuildPreview(seed.id, pool);
            if (p == null) continue;

            networkRoutes += p.routes?.Count ?? 0;
            if (p.routes != null)
            {
                for (int i = 0; i < p.routes.Count; i++)
                {
                    var r = p.routes[i];
                    if (r != null && !pool.Contains(r.destination?.Trim().ToUpperInvariant() ?? ""))
                        outOfUniverse++;
                }
            }
            var fleet = ToSimFleet(p);
            var routes = ToSimRoutes(p);
            aircraft += fleet.Count;
            flyable += routes.Count;
            airlines++;

            sig.Append(p.id).Append(':');
            for (int i = 0; i < routes.Count; i++) sig.Append(routes[i].RouteId).Append(',');
            sig.Append(';');

            foreach (var sc in Scenarios)
            {
                var a = acc[sc.Key];
                a.Airlines++;
                if (fleet.Count == 0 || routes.Count == 0)
                {
                    // 没有可执飞网络 = 年初即无收入；按破产计，而不是悄悄从分母里消失。
                    a.Bankrupt++;
                    continue;
                }

                var sim = new AirlineYearSimulator(
                    macro: sc.Make(),
                    fleet: fleet,
                    routes: routes,
                    openingCash: p.cash,
                    fixedMonthlyOpex: FixedMonthlyOpexWan(p),
                    brandEquity: 55f,
                    irropsSeverity: 1f,
                    verbose: false);
                var s = sim.RunFiscalYear(1);
                if (s.MinCash < 0f) a.Bankrupt++;
                a.RunwaySum += s.FinalRunwayMonths;
                a.Revenue += s.TotalRevenue;
                a.Cost += s.TotalCost;
                a.NetProfit += s.TotalNetProfit;
                a.RunwayCount++;

                for (int i = 0; i < routes.Count; i++)
                    asks[sc.Key] += MktEconomics.Ask(routes[i].Seats, routes[i].DistanceKm, routes[i].WeeklyFlights) * 52.0;
            }
        }

        var byMacro = new Dictionary<string, MacroResult>();
        foreach (var sc in Scenarios)
        {
            var a = acc[sc.Key];
            byMacro[sc.Key] = new MacroResult
            {
                AirlineCount = airlines,
                Bankrupt = a.Bankrupt,
                AvgRunway = a.RunwayCount > 0 ? a.RunwaySum / a.RunwayCount : 0f,
                TotalRevenue = a.Revenue,
                TotalCost = a.Cost,
                AskSeatKm = asks[sc.Key],
                AvgNetProfit = a.Airlines > 0 ? a.NetProfit / a.Airlines : 0f,
            };
        }

        return new PoolRow
        {
            Label = label,
            BatchSize = batch,
            AirportCount = pool.Count,
            AirlineCount = airlines,
            NetworkRoutes = networkRoutes,
            FlyableRoutes = flyable,
            Aircraft = aircraft,
            OutOfUniverse = outOfUniverse,
            Signature = Hash(sig.ToString()),
            ByMacro = byMacro,
        };
    }

    sealed class Accumulator
    {
        public int Airlines;
        public int Bankrupt;
        public int RunwayCount;
        public float RunwaySum;
        public float Revenue;
        public float Cost;
        public float NetProfit;
    }

    static void PrintBatch(string name, string[] cities, PoolRow before, PoolRow after)
    {
        int dAirlines = after.AirlineCount - before.AirlineCount;
        int dRoutes = after.NetworkRoutes - before.NetworkRoutes;
        int newHubs = cities.Count(c =>
            PresetAirlines.AllSelectable().Any(p => string.Equals(p.hubAirport, c, StringComparison.OrdinalIgnoreCase)));
        Console.WriteLine($"  {name,-6}（{string.Join(" / ", cities)}）：新增枢纽航司 {newHubs} 家"
                        + $" · 网络 +{dRoutes} 条（{before.NetworkRoutes} → {after.NetworkRoutes}）"
                        + $" · 航司 {before.AirlineCount} → {after.AirlineCount}（{dAirlines:+0;-0}）");
    }

    static List<SimAircraft> ToSimFleet(AirlineProfile p)
    {
        var list = new List<SimAircraft>();
        if (p?.aircraft == null) return list;
        for (int i = 0; i < p.aircraft.Count; i++)
        {
            var a = p.aircraft[i];
            if (a == null || !a.isActive || string.IsNullOrEmpty(a.type)) continue;
            list.Add(new SimAircraft
            {
                Registration = string.IsNullOrEmpty(a.registration) ? $"SIM{i:000}" : a.registration,
                Type = a.type,
                PurchasePriceWan = a.purchasePrice > 0f ? a.purchasePrice : 11000f,
                AgeYears = a.age,
            });
        }
        return list;
    }

    /// <summary>
    /// 真实网络 → 压测模板。**只收有执飞机的航线**：无机航线在局内处于停飞（`FlightGroundLogic`），
    /// 把它算进收入会凭空造出一块永不发生的现金流，压测就再也测不出「机队不足」的后果。
    /// </summary>
    static List<RouteTemplate> ToSimRoutes(AirlineProfile p)
    {
        var list = new List<RouteTemplate>();
        if (p?.routes == null) return list;
        for (int i = 0; i < p.routes.Count; i++)
        {
            var r = p.routes[i];
            if (r == null || string.IsNullOrEmpty(r.aircraftType) || string.IsNullOrEmpty(r.registration)) continue;
            string ac = r.aircraftType;
            list.Add(new RouteTemplate
            {
                RouteId = r.routeId,
                Origin = r.origin,
                Destination = r.destination,
                DistanceKm = r.distanceKm,
                // 竞对运力取中性 0.5：压测比的是「机场池变化前后」，不是绝对票价水平；
                // 需求侧仍由 RPMCalculator 按航距与城市经济算，故不是一套孤儿公式。
                CompetitorSupply = 0.5f,
                PassengerType = "leisure",
                WeeklyFlights = Math.Max(1, r.WeeklyFlightsDeparture()),
                AircraftType = ac,
                Seats = EconomyBalanceParams.DefaultSeatsFor(ac),
                LoadFactor = EconomyBalanceParams.DefaultLoadFactor,
                GroundStaffAssigned = EconomyBalanceParams.IsWidebody(ac)
                    ? EconomyBalanceParams.RecommendedGroundStaffWide
                    : EconomyBalanceParams.RecommendedGroundStaffNarrow,
            });
        }
        return list;
    }

    /// <summary>
    /// 把「接管档案的固定现金流出」折成 simulator 的 <c>fixedMonthlyOpex</c>：
    /// 总部/地面 + 月租 + 月均还本付息 + 月均保费。
    ///
    /// 不含机队维护与存量人力：这两项 simulator 会按同一批 <c>FleetEconomics</c> / <c>HrEconomics</c>
    /// 公式**自己再算一遍**，再传进来就是重复计提（K11 的跑道口径事故正是重复计提的另一种形态）。
    /// </summary>
    static float FixedMonthlyOpexWan(AirlineProfile p)
    {
        float lease = 0f;
        if (p?.aircraft != null)
            for (int i = 0; i < p.aircraft.Count; i++)
                if (p.aircraft[i] != null && p.aircraft[i].isActive)
                    lease += Math.Max(0f, p.aircraft[i].monthlyLeaseWan);
        return p == null ? 0f : p.monthlyCost + lease + p.monthlyDebtService + p.monthlyInsurance;
    }

    static string Hash(string text)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16];
    }

    /// <summary>航线集合指纹（按 routeId 排序拼接）：只比生成网络，不比顺序。</summary>
    static string RouteKey(AirlineProfile p)
    {
        if (p?.routes == null) return "";
        var ids = new List<string>(p.routes.Count);
        for (int i = 0; i < p.routes.Count; i++)
            if (p.routes[i] != null) ids.Add(p.routes[i].routeId);
        ids.Sort(StringComparer.Ordinal);
        return string.Join(",", ids);
    }

    static void WriteArtifacts(List<PoolRow> rows, PoolRow full, PoolRow noA,
        List<(Scenario Sc, bool Rate, bool Runway, bool Unit)> econChanges, bool ok,
        bool defaultSame, bool noOutOfUniverse)
    {
        Directory.CreateDirectory("out");
        var csv = new List<string>
        {
            "机场池,批次规模,机场数,可接管航司,网络航线,可执飞航线,机队,航线密度,情景,破产数,破产率,平均跑道(月),收入成本比,平均净利/家(万),网络指纹"
        };
        foreach (var r in rows)
        {
            foreach (var sc in Scenarios)
            {
                var m = r[sc];
                csv.Add($"{r.Label},{r.BatchSize},{r.AirportCount},{r.AirlineCount}"
                      + $",{r.NetworkRoutes},{r.FlyableRoutes},{r.Aircraft},{r.Density:F3}"
                      + $",{sc.Key},{m.Bankrupt},{m.BankruptcyRate:P2},{m.AvgRunway:F2}"
                      + $",{m.RevenueCostRatio:F3},{m.AvgNetProfit:F0},{r.Signature}");
            }
        }
        File.WriteAllLines(Path.Combine("out", "netstress_matrix.csv"), csv);

        var stamp = BaselineProvenance.Create(42, 1, "");
        var md = new List<string>
        {
            "# Airline CEO · 网络池压测（K14）",
            "",
            "> 生成：`tools/balance` · `dotnet run -- --netstress`（seed=42，可复现）",
            "",
            "## 出处",
            "",
            "| 项 | 值 |",
            "|---|---|",
            $"| 生成时间（UTC） | {stamp.GeneratedAt} |",
            $"| commit | `{(stamp.Commit.Length >= 8 ? stamp.Commit[..8] : stamp.Commit)}`（{stamp.Branch}）{(stamp.Dirty ? " · **工作区脏**" : "")} |",
            $"| 输入指纹 | `{stamp.SourceHash[..Math.Min(12, stamp.SourceHash.Length)]}`（{stamp.SourceFileCount} 个 .cs/.csproj） |",
            "| 情景 | 繁荣（GDP 3.0% / 油价 850）· 滞胀（GDP −0.5% / 油价 1300），各一年 |",
            "| 样本 | `PresetAirlines.AllSelectable()` 中**枢纽属于被测机场池**的全部航司 |",
            "",
            "## 口径",
            "",
            "- 网络由 `TakeoverWorldBuilder.BuildPreview(id, universe)` 生成：与接管界面同一套目的地池/选机/班次模型，只收窄目的地宇宙；",
            "- 只压测**有执飞机**的航线（无机航线在局内停飞，算收入会造假）；",
            "- `fixedMonthlyOpex` = 总部/地面 + 月租 + 月均还本付息 + 月均保费；机队维护与人力由 simulator 按同一公式自算，不重复传；",
            "- 破产 = 年内最低现金 < 0，或年初即无可执飞网络；跑道取 `FinalRunwayMonths`；",
            "- `收入/成本` = 池级 RASK ÷ CASK（分母同是 ASK），>1 表示单位经济为正；",
            "- **航线密度不是判据**：它与「航线数/机队数」同源，加城市必然改变；",
            "- **饱和披露**：某情景破产率贴在 0% 或 100% 时，该指标无区分度，不能用来下结论。",
            "",
            "## 结果",
            "",
            "| 机场池 | 批次规模 | 机场数 | 可接管航司 | 网络航线 | 可执飞 | 密度 | 宇宙外航线 | 情景 | 破产率 | 平均跑道 | 收入/成本 | 平均净利/家 |",
            "|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|",
        };
        foreach (var r in rows)
        {
            foreach (var sc in Scenarios)
            {
                var m = r[sc];
                md.Add($"| {r.Label} | {r.BatchSize} | {r.AirportCount} | {r.AirlineCount} | {r.NetworkRoutes}"
                     + $" | {r.FlyableRoutes} | {r.Density:F2} | {r.OutOfUniverse} | {sc.Key}"
                     + $" | {(m.RateSaturated ? "**饱和** " : "")}{m.BankruptcyRate:P1} ({m.Bankrupt}/{m.AirlineCount})"
                     + $" | {m.AvgRunway:F2} 月 | {m.RevenueCostRatio:F3} | {m.AvgNetProfit:F0} 万 |");
            }
        }
        md.Add("");
        md.Add("## 批次 A 消融（K14.3）");
        md.Add("");
        md.Add("| 情景 | 项 | 当前池 | 去掉批次 A | 变化 |");
        md.Add("|---|---|---:|---:|---|");
        foreach (var e in econChanges)
        {
            var a = full[e.Sc];
            var b = noA[e.Sc];
            md.Add($"| {e.Sc.Key} | 可接管航司 | {full.AirlineCount} | {noA.AirlineCount} | {full.AirlineCount - noA.AirlineCount:+0;-0} |");
            md.Add($"| {e.Sc.Key} | 网络航线 | {full.NetworkRoutes} | {noA.NetworkRoutes} | {full.NetworkRoutes - noA.NetworkRoutes:+0;-0} |");
            md.Add($"| {e.Sc.Key} | 破产率 | {a.BankruptcyRate:P2} | {b.BankruptcyRate:P2}"
                 + $" | {(e.Rate ? "变化" : a.RateSaturated || b.RateSaturated ? "饱和/未变" : "未变")} |");
            md.Add($"| {e.Sc.Key} | 平均跑道 | {a.AvgRunway:F2} | {b.AvgRunway:F2} | {(e.Runway ? "变化" : "未变")} |");
            md.Add($"| {e.Sc.Key} | 收入/成本 | {a.RevenueCostRatio:F3} | {b.RevenueCostRatio:F3} | {(e.Unit ? "变化" : "未变")} |");
            md.Add($"| {e.Sc.Key} | 平均净利/家 | {a.AvgNetProfit:F0} 万 | {b.AvgNetProfit:F0} 万 | {a.AvgNetProfit - b.AvgNetProfit:+0;-0} |");
        }
        md.Add($"| — | 网络指纹 | `{full.Signature}` | `{noA.Signature}` | {(full.Signature == noA.Signature ? "相同" : "不同")} |");
        md.Add("");
        md.Add("## 机制守卫（与 Unity 侧 `NetworkPoolVisibilityRegression` 同一条断言）");
        md.Add("");
        md.Add("| 断言 | 结果 |");
        md.Add("|---|---|");
        md.Add($"| 默认 `BuildPreview(id)` ≡ `BuildPreview(id, 全目录)`（零扰动） | {(defaultSame ? "通过" : "**失败**")} |");
        md.Add($"| 任一机场池都不生成宇宙外航线（收窄真被读到） | {(noOutOfUniverse ? "通过" : "**失败**")} |");
        md.Add("");
        md.Add("> 判定：网络指纹/航司数/航线数必须不同（证明读到三表）；**至少一条情景**的破产率/平均跑道/收入成本比");
        md.Add("> 必须不同（证明网络真的进了模型）。只满足前者不满足后者，等于「看见机场却没算进去」。");
        md.Add("> 航线密度不算判据；破产率饱和的情景必须如实标注，不得拿来做结论。");
        md.Add("");
        md.Add(ok
            ? "**结论：压测已读到机场三表；增删城市会同时改变网络与压测结果。**"
            : "**结论：有断言未通过 —— 压测对机场池仍可能结构性失明。**");
        File.WriteAllText("NETWORK_POOL_STRESS.md", string.Join("\n", md));
        Console.WriteLine($"CSV: {Path.GetFullPath(Path.Combine("out", "netstress_matrix.csv"))}");
        Console.WriteLine($"Report: {Path.GetFullPath("NETWORK_POOL_STRESS.md")}");
    }
}
