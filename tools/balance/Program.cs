using AirlineCEO.Balance.Economy;
using AirlineCEO.Balance.Export;
using AirlineCEO.Balance.Legacy;
using AirlineCEO.Balance.Scenarios;
using AirlineCEO.Balance.Sim;
using AirlineCEO.Balance.Stress;
using AirlineCEO.Data;
using AirlineCEO.Logic;

namespace AirlineCEO.Balance;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!ApplyRouteFixedBaseOverride(args))
            return 2;

        if (args.Contains("--stress"))
            return RunStressTest();

        if (args.Contains("--legacy"))
            return RunLegacyCheck();

        if (args.Contains("--routefixed-sweep"))
            return RunRouteFixedBaseSweep(args);

        if (args.Contains("--routefixed"))
            return RunRouteFixedCostReport();

        if (args.Contains("--freq"))
            return FrequencyReport.Run();

        if (args.Contains("--frequniverse"))
            return UniverseFrequencyReport.Run();

        if (args.Contains("--routeinventory"))
            return RouteInventoryReport.Run();

        if (args.Contains("--presets"))
            return PresetPreviewReport.Run();

        if (args.Contains("--fleetcheck"))
            return FleetNetworkReport.Run();

        if (args.Contains("--channels"))
        {
            float shortage = ParseFloat(args, "--shortage", 60f);
            Analysis.ChannelPayoff.Run(shortage);
            return 0;
        }

        int seed = ParseInt(args, "--seed", 42);
        int year = ParseInt(args, "--year", 1);
        string outDir = ParseString(args, "--out", Path.Combine("out"));

        Console.WriteLine("=== Airline CEO · Balance Simulator (Phase 2 · MKT) ===");
        Console.WriteLine($"seed={seed}  year={year}  out={Path.GetFullPath(outDir)}");
        Console.WriteLine();

        // 冒烟：与 Unity LogicSelfTests 同输入对齐
        SmokeCheckRpm();

        var macro = new MacroEconomyEngine(seed: seed);
        var sim = new AirlineYearSimulator(macro);
        var summary = sim.RunFiscalYear(year);

        PrintSummary(summary);

        Directory.CreateDirectory(outDir);
        string csvPath = Path.Combine(outDir, $"pnl_y{year}_seed{seed}.csv");
        string written = CsvExporter.WriteMonthlyPnL(csvPath, summary);
        Console.WriteLine();
        Console.WriteLine($"CSV written: {written}");

        // 基准摘要（便于回归对比）
        string summaryPath = Path.Combine(outDir, $"summary_y{year}_seed{seed}.txt");
        File.WriteAllText(summaryPath, BuildSummaryText(summary, seed, year));
        Console.WriteLine($"Summary: {Path.GetFullPath(summaryPath)}");

        return 0;
    }

    private static int RunLegacyCheck()
    {
        LegacyBalanceChecker.Run();
        return 0;
    }

    private static int RunStressTest()
    {
        Console.WriteLine("=== Airline CEO · Phase 4 Week 13 全流程数值压测 ===");
        Console.WriteLine("难度 × 航司类型 × 宏观情景 = 3 × 4 × 5 = 60 组合");
        Console.WriteLine($"航线周期固定成本基准 = {DescribeRouteFixedBase()}");
        Console.WriteLine();

        var results = StressTestRunner.Run();

        Directory.CreateDirectory("out");
        string csvPath = Path.Combine("out", "stress_matrix.csv");
        File.WriteAllLines(csvPath, BuildStressCsv(results));
        Console.WriteLine($"CSV written: {Path.GetFullPath(csvPath)}");

        string report = BuildStressReport(results);
        string reportPath = "STRESS_BALANCE_REPORT.md";
        File.WriteAllText(reportPath, report);
        Console.WriteLine($"Report: {Path.GetFullPath(reportPath)}");
        Console.WriteLine();

        // 控制台摘要
        var groups = results.GroupBy(r => r.Flag).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine("--- 财务健康度分布 ---");
        foreach (var kv in groups.OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kv.Key,-10} : {kv.Value} 组合");

        int bad = results.Count(r => r.Flag is "BANKRUPT" or "RED" or "YELLOW");
        int runaway = results.Count(r => r.Flag == "RUNAWAY");
        Console.WriteLine();
        Console.WriteLine($"危险组合(BANKRUPT+RED+YELLOW) : {bad}");
        Console.WriteLine($"失控组合(RUNAWAY)             : {runaway}");
        Console.WriteLine(bad == 0
            ? "结论：无破产/告急组合，数值安全。"
            : $"结论：{bad} 个告急组合分布为 " + string.Join("、", results
                .Where(r => r.Flag is "BANKRUPT" or "RED" or "YELLOW")
                .GroupBy(r => r.Scenario).Select(g => $"{g.Key} {g.Count()}")) + "；需逐情景判断，不自动视为健康。");
        return bad == 0 ? 0 : 1;
    }

    private static IEnumerable<string> BuildStressCsv(IReadOnlyList<StressTestRunner.Result> results)
    {
        yield return "难度,航司类型,宏观情景,期初现金,营收,成本,净利,期末现金,最低现金,跑道(月),信用评级,准点率,净利率";
        foreach (var r in results)
        {
            float margin = r.Revenue > 0f ? r.NetProfit / r.Revenue : 0f;
            yield return $"{r.Difficulty},{r.AirlineType},{r.Scenario},{r.OpeningCash:F0},{r.Revenue:F0},{r.Cost:F0},{r.NetProfit:F0},{r.ClosingCash:F0},{r.MinCash:F0},{r.Runway:F1},{r.Credit},{r.OtpClose:F1},{margin:P1}";
        }
    }

    private static string BuildStressReport(IReadOnlyList<StressTestRunner.Result> results)
    {
        var lines = new List<string>
        {
            "# Airline CEO · 平衡压测报告（Phase 4 Week 13）",
            "",
            "> 生成：`tools/balance` · `dotnet run -- --stress`（seed=42，可复现）",
            "",
            "## 范围",
            "",
            "- 难度：简单（现金×1.25 / 事件×0.8）/ 标准（×1.0）/ 困难（现金×0.8 / 事件×1.25）",
            "- 航司类型：全服务 FSC / 低成本 LCC / 支线 Regional / 包机 Charter（机队 + 航线 + 品牌净值不同）",
            "- 宏观情景：繁荣 / 复苏 / 衰退 / 油价冲击（1400$/t）/ 滞胀（1300$/t + 衰退）",
            "- 共 60 组合，各跑一个财年，复用 `AirlineYearSimulator`（含 OPS/FIN/FLEET/MKT/STG/MNT/HR/SAF 年闭环）",
            $"- 航线周期固定成本基准：{DescribeRouteFixedBase()}（本报告全量数值均基于该取值，复现时须一并指定）",
            "",
            "## 判定口径",
            "",
            "| 标签 | 判定 |",
            "|---|---|",
            "| BANKRUPT | 年内最低现金 < 0（资不抵债） |",
            "| RED | 年末跑道 < 2 个月 |",
            "| YELLOW | 年末跑道 < 3 个月 |",
            "| RUNAWAY | 年净利 > 4× 期初现金（过于简单） |",
            "| GREEN | 健康 |",
            "",
            "## 健康度分布",
            ""
        };

        var groups = results.GroupBy(r => r.Flag).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kv in groups.OrderByDescending(k => k.Value))
            lines.Add($"- **{kv.Key}**: {kv.Value}");

        lines.Add("");
        lines.Add("## 全量矩阵（60 组合）");
        lines.Add("");
        lines.Add("| 难度 | 航司 | 宏观 | 期初 | 净利 | 期末 | 最低现金 | 跑道 | 信用 | 判定 |");
        lines.Add("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in results)
            lines.Add($"| {r.Difficulty} | {r.AirlineType} | {r.Scenario} | {r.OpeningCash:F0} | {r.NetProfit:F0} | {r.ClosingCash:F0} | {r.MinCash:F0} | {r.Runway:F1} | {r.Credit} | {r.Flag} |");

        lines.Add("");
        lines.Add("## 分维度透视");
        lines.Add("");
        AddPivot(lines, "难度", results.GroupBy(r => r.Difficulty));
        AddPivot(lines, "航司类型", results.GroupBy(r => r.AirlineType));
        AddPivot(lines, "宏观情景", results.GroupBy(r => r.Scenario));

        lines.Add("");
        lines.Add("## 核心发现");
        lines.Add("");

        int bad = results.Count(r => r.Flag is "BANKRUPT" or "RED" or "YELLOW");
        int runaway = results.Count(r => r.Flag == "RUNAWAY");
        int bankrupt = results.Count(r => r.Flag == "BANKRUPT");

        var byType = results.GroupBy(r => r.AirlineType)
            .ToDictionary(g => g.Key, g => g);
        foreach (var g in byType.OrderBy(g => g.Key))
        {
            int b = g.Value.Count(r => r.Flag == "BANKRUPT");
            int rw = g.Value.Count(r => r.Flag == "RUNAWAY");
            float avg = g.Value.Average(r => r.NetProfit);
            lines.Add($"- **{g.Key}**：{g.Value.Count()} 组合 · 平均净利 {avg:F0} 万 · 破产 {b} · 失控 {rw}");
        }

        lines.Add("");
        lines.Add("## 沿用的历史参数基线（Phase 4 数值落地）");
        lines.Add("");
        lines.Add("以下为既有参数；本报告不表示本轮实施了这些调参，也不代表 Runtime 的逐班执飞、机组与存档模型已覆盖。");
        lines.Add("");
        lines.Add("- **固定开销按机队规模分段**：新增 `EconomyBalanceParams.FixedMonthlyOpexFor(n) = 20 + 15×n`。5 机基准仍为 95 万/月（回归基线不变），3 机降到 65、6 机升到 110，修复「小机队被固定开销压垮」。");
        lines.Add("- **低成本 yield 折价**：`RouteTemplate.YieldMultiplier` 接入 `RPMCalculator`（新增 `yieldMultiplier` 字段，默认 1.0 不变）。LCC 4 条航线设 0.85，即低成本票价较全服务低约 15%。");
        lines.Add("- **低成本竞对运力上调**：LCC 短途航线 `CompetitorSupply` 0.45–0.55 → 0.60–0.70。");
        lines.Add("- **支线场景校准**：班次 7→14 班/周、客座率 0.68–0.72 → 0.78–0.82（真实高频摆渡画像）。");
        lines.Add("");
        lines.Add("## 剩余告急与判断");
        lines.Add("");
        lines.Add($"- 告急（BANKRUPT/RED/YELLOW）{bad} 组合 · 破产 {bankrupt} 组合 · 失控（RUNAWAY）{runaway} 组合。");

        bool onlyStagflation = results
            .Where(r => r.Flag is "BANKRUPT" or "RED" or "YELLOW")
            .All(r => r.Scenario == "滞胀");
        if (bad > 0 && onlyStagflation)
            lines.Add("- 剩余告急全部落在**滞胀**（衰退 + 油价 1300$/t）情景，为最坏宏观组合，属真实行业梯度而非参数缺陷。");

        bool fscHealthy = results.Any(r => r.AirlineType == "全服务")
            && !results.Any(r => r.AirlineType == "全服务" && r.Flag is "BANKRUPT" or "RED" or "YELLOW" or "RUNAWAY");
        if (fscHealthy)
            lines.Add("- **全服务 15/15 全 GREEN**：繁荣期净利 1319 万 → 滞胀 -208 万的梯度平滑，作为基线难度锚点合理。");

        if (bad == 0 && runaway == 0)
            lines.Add("- 60 组合全部落于绿色区间，数值梯度健康，无需调参。");
        else if (runaway == 0)
            lines.Add("- **结论**：本次无 RUNAWAY，但仍有告急。情景分布：" + string.Join("、", results
                .Where(r => r.Flag is "BANKRUPT" or "RED" or "YELLOW")
                .GroupBy(r => r.Scenario).Select(g => $"{g.Key} {g.Count()}"))
                + "。需逐项分析，不能据此宣布平衡验收通过。");

        return string.Join("\n", lines);
    }

    private static void AddPivot(List<string> lines, string title, IEnumerable<IGrouping<string, StressTestRunner.Result>> groups)
    {
        lines.Add($"### 按{title}");
        lines.Add("");
        lines.Add($"| {title} | 组合数 | 平均净利(万) | 平均跑道(月) | 告急数 | 破产数 |");
        lines.Add("|---|---|---|---|---|---|");
        foreach (var g in groups)
        {
            int danger = g.Count(r => r.Flag is "BANKRUPT" or "RED" or "YELLOW");
            int bankrupt = g.Count(r => r.Flag == "BANKRUPT");
            lines.Add($"| {g.Key} | {g.Count()} | {g.Average(r => r.NetProfit):F0} | {g.Average(r => r.Runway):F1} | {danger} | {bankrupt} |");
        }
        lines.Add("");
    }

    /// <summary>
    /// 应用 `--routefixed-base &lt;万美元/日/线&gt;` 覆盖。未给出时保持出厂默认。
    /// 取值非法直接报错退出（返回 false）——**不静默回退**，否则会让人误以为扫的是请求的参数。
    /// </summary>
    private static bool ApplyRouteFixedBaseOverride(string[] args)
    {
        if (!args.Contains("--routefixed-base")) return true;

        float v = ParseFloat(args, "--routefixed-base", float.NaN);
        if (!EconomyBalanceParams.SetRouteFixedBaseDailyWan(v))
        {
            Console.Error.WriteLine(
                $"--routefixed-base 取值非法：{v}。须为 (0, {EconomyBalanceParams.RouteFixedBaseDailyWanCeiling}] 内的有限数。");
            return false;
        }

        Console.WriteLine($"[tuning] RouteFixedBaseDailyWan 覆盖为 {EconomyBalanceParams.RouteFixedBaseDailyWan:F4} 万/日/线"
                          + $"（出厂默认 {EconomyBalanceParams.RouteFixedBaseDailyWanDefault:F4}）");
        Console.WriteLine();
        return true;
    }

    /// <summary>人类可读的旋钮当前值，供各模式表头与报告引用（覆盖时标注出厂默认以便复现）。</summary>
    private static string DescribeRouteFixedBase() =>
        EconomyBalanceParams.IsRouteFixedBaseOverridden
            ? $"{EconomyBalanceParams.RouteFixedBaseDailyWan:F4} 万/日/线（覆盖，出厂默认 {EconomyBalanceParams.RouteFixedBaseDailyWanDefault:F4}）"
            : $"{EconomyBalanceParams.RouteFixedBaseDailyWan:F4} 万/日/线（出厂默认）";

    /// <summary>
    /// 航线周期固定成本基准扫参（`--routefixed-sweep 0.02,0.04,0.06`，省略则用默认扫描集）。
    ///
    /// 上半张表：基准如何移动各档航段的 F（航程/机型档位不变，只缩放基准）。
    /// 下半张表：**单班毛贡献被钉在出厂默认对应的边际航线**上（故各行可比），看基准如何平移
    /// 摊薄曲线与盈亏平衡班次——线性缩放：基准翻倍，平衡班次翻倍。
    /// 结束后必定复位为出厂默认，避免污染后续调用。
    /// </summary>
    private static int RunRouteFixedBaseSweep(string[] args)
    {
        const float dist = 1080f;
        const string ac = "A320neo";
        const int staff = 3;
        const float otp = 90f;

        float[] values = ParseSweepValues(ParseString(args, "--routefixed-sweep", string.Empty));

        var samples = new (string Label, float Dist, string Type)[]
        {
            ("1080km窄体", 1080f, "A320neo"),
            ("1950km窄体", 1950f, "A320neo"),
            ("8150km宽体", 8150f, "B787-9"),
        };

        Console.WriteLine("=== RouteFixedBaseDailyWan 扫参 ===");
        Console.WriteLine($"出厂默认 {EconomyBalanceParams.RouteFixedBaseDailyWanDefault:F4} 万/日/线　"
                          + $"允许上限 {EconomyBalanceParams.RouteFixedBaseDailyWanCeiling:F2}");
        Console.WriteLine();
        Console.WriteLine("  F 档位（万美元/日/线）—— 随基准线性缩放");
        Console.WriteLine("  基准      " + string.Join("  ", samples.Select(s => $"{s.Label,-11}")));
        foreach (float v in values)
        {
            EconomyBalanceParams.SetRouteFixedBaseDailyWan(v);
            var cells = samples.Select(s => $"{OpsEconomics.RouteFixedDailyWan(s.Dist, s.Type),-11:F4}");
            Console.WriteLine($"  {v,-9:F4} " + string.Join("  ", cells));
        }

        // 毛贡献锚点：钉在「出厂默认下的边际航线」（日净利 ≈ −20% 固定成本），使各行可直接横向比较。
        EconomyBalanceParams.ResetRouteFixedBaseDailyWan();
        var m7 = OpsEconomics.Evaluate(dist, 7, ac, staff, otp);
        float baselineDerived = 0.8f * OpsEconomics.RouteFixedDailyWan(dist, ac) / m7.profitMultiplier;

        Console.WriteLine();
        Console.WriteLine($"=== 摊薄与盈亏平衡（{dist:F0}km {ac} · 单班毛贡献固定 {baselineDerived:F3} 万/班 · 日净利） ===");
        Console.WriteLine("  基准      1班/日   2班/日   3班/日   7班/日   平衡班次");
        foreach (float v in values)
        {
            EconomyBalanceParams.SetRouteFixedBaseDailyWan(v);
            float fix = OpsEconomics.RouteFixedDailyWan(dist, ac);
            var cells = new[] { 7, 14, 21, 49 }
                .Select(w => OpsEconomics.RouteDailyProfit(baselineDerived, w, m7, fix));
            float be = OpsEconomics.BreakEvenDailyFlights(baselineDerived, m7, fix);
            Console.WriteLine($"  {v,-9:F4} " + string.Join("  ", cells.Select(c => $"{c,7:F2}"))
                              + $"   {be,8:F2}");
        }

        EconomyBalanceParams.ResetRouteFixedBaseDailyWan();
        Console.WriteLine();
        Console.WriteLine($"[tuning] 已复位为出厂默认 {EconomyBalanceParams.RouteFixedBaseDailyWan:F4} 万/日/线。");
        return 0;
    }

    /// <summary>解析逗号分隔扫描集；逐项校验范围，非法项跳过并告警，全空则回退默认扫描集。</summary>
    private static float[] ParseSweepValues(string csv)
    {
        float[] fallback = { 0.02f, 0.04f, 0.06f, 0.08f, 0.10f, 0.12f };
        if (string.IsNullOrWhiteSpace(csv)) return fallback;

        var list = new List<float>();
        foreach (string part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!float.TryParse(part, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v)
                || v <= 0f || v > EconomyBalanceParams.RouteFixedBaseDailyWanCeiling)
            {
                Console.Error.WriteLine($"  跳过非法扫描值：'{part}'（须为 (0, {EconomyBalanceParams.RouteFixedBaseDailyWanCeiling}]）");
                continue;
            }
            list.Add(v);
        }

        if (list.Count == 0)
        {
            Console.Error.WriteLine("  无有效扫描值，回退为默认扫描集。");
            return fallback;
        }
        return list.ToArray();
    }

    /// <summary>
    /// 航线周期固定成本自检（<c>--routefixed</c>）：打印 F 档位表，并演示
    /// 「加密摊薄固定成本 → 1 班亏 / 3 班赚」、盈亏平衡班次，以及负基准不得被洗白的回归护栏。
    /// </summary>
    private static int RunRouteFixedCostReport()
    {
        Console.WriteLine("=== 航线周期固定成本（与班次无关；加密即摊薄） ===");
        Console.WriteLine($"  基准 RouteFixedBaseDailyWan = {DescribeRouteFixedBase()}");
        Console.WriteLine("  航段                      机型       F(万/日)");
        var samples = new (string Label, float Dist, string Type)[]
        {
            ("PVG–SHA  ~1080 km", 1080f, "A320neo"),
            ("PVG–SZX  ~1200 km", 1200f, "A320neo"),
            ("PVG–CTU  ~1700 km", 1700f, "A320neo"),
            ("SZX–PEK  ~1950 km", 1950f, "A320neo"),
            ("PEK–LHR  ~8150 km", 8150f, "B787-9")
        };
        foreach (var s in samples)
            Console.WriteLine($"  {s.Label,-24} {s.Type,-9} {OpsEconomics.RouteFixedDailyWan(s.Dist, s.Type),8:F2}");

        const float dist = 1080f;
        const string ac = "A320neo";
        const int staff = 3;
        const float otp = 90f;
        float fixedDaily = OpsEconomics.RouteFixedDailyWan(dist, ac);
        var m7 = OpsEconomics.Evaluate(dist, 7, ac, staff, otp);
        // 边际毛贡献：取盈亏平衡的 0.8 倍 → 1 班/日必亏、3 班/日必盈（不写死常数，便于调参复核）
        float baselineDerived = 0.8f * fixedDaily / m7.profitMultiplier;

        Console.WriteLine();
        Console.WriteLine($"=== 加密摊薄：单班毛贡献约 {baselineDerived:F2} 万/班的边际航线（{dist:F0} km · {ac}） ===");
        Console.WriteLine("  周班次  日均班次  毛贡献    固定成本   日净利");
        foreach (int weekly in new[] { 7, 14, 21, 28 })
        {
            var m = OpsEconomics.Evaluate(dist, weekly, ac, staff, otp);
            float gross = OpsEconomics.ToDailyProfit(baselineDerived, weekly, m);
            float net = OpsEconomics.RouteDailyProfit(baselineDerived, weekly, m, fixedDaily);
            string tag = net < 0f ? "亏损" : "盈利";
            Console.WriteLine(
                $"  {weekly,6}  {weekly / 7f,8:F0}  {gross,7:F2}  {fixedDaily,9:F2}  {net,7:F2}  {tag}");
        }

        Console.WriteLine();
        Console.WriteLine($"  盈亏平衡日均班次（毛贡献 {baselineDerived:F2}）= "
                          + $"{OpsEconomics.BreakEvenDailyFlights(baselineDerived, m7, fixedDaily):F2} 班/日");
        Console.WriteLine($"  毛贡献为负（-0.40）时 → "
                          + $"{OpsEconomics.BreakEvenDailyFlights(-0.40f, m7, fixedDaily)}（无解：加密救不回来）");

        Console.WriteLine();
        Console.WriteLine("=== 回归护栏：负基准必须原样透传（历史缺陷：<= 0.01f 被当成「未设置」→ 洗白为 +1 万/班） ===");
        foreach (int weekly in new[] { 7, 14, 21 })
        {
            var route = new RouteData
            {
                routeId = "R-GUARD",
                origin = "PVG",
                destination = "SZX",
                distanceKm = dist,
                aircraftType = ac,
                weeklyFlights = weekly,
                groundStaffAssigned = staff,
                baseDailyProfit = -0.40f,
                dailyProfit = -0.40f
            };
            float resolved = OpsEconomics.ResolvedSingleFlightBaseline(route);
            OpsEconomics.ApplyToRoute(route, resolved, otp);
            Console.WriteLine($"  周 {weekly,2} 班 → 解析基准 {resolved:F2}（原样）　日净利 {route.dailyProfit,6:F2}"
                              + $"　{(route.dailyProfit < 0f ? "仍为亏损 ✓" : "错误转盈 ✗")}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 不变量：各班存毛贡献，固定成本只在航线层级扣一次（Recalc 必须幂等） ===");
        var inv = new RouteData
        {
            routeId = "R-INV",
            origin = "PVG",
            destination = "SZX",
            distanceKm = dist,
            aircraftType = ac,
            weeklyFlights = 7,
            groundStaffAssigned = staff,
            baseDailyProfit = 2f,
            dailyProfit = 2f
        };
        OpsEconomics.ApplyToRoute(inv, 2f, otp);
        inv.SyncFlightsFromPrimary();
        float applied = inv.dailyProfit;
        inv.RecalcDailyProfit();
        bool idempotent = Math.Abs(applied - inv.dailyProfit) < 0.01f;
        bool flightsAreGross = Math.Abs(inv.flights[0].dailyProfit - inv.GrossDailyProfit) < 0.01f;
        Console.WriteLine($"  ApplyToRoute {applied:F2} → RecalcDailyProfit {inv.dailyProfit:F2}　幂等={idempotent}");
        Console.WriteLine($"  单班字段 {inv.flights[0].dailyProfit:F2} == 毛贡献 {inv.GrossDailyProfit:F2}　固定成本 {inv.FixedDailyCostWan:F2}　"
                          + $"{(idempotent && flightsAreGross ? "不变量成立 ✓" : "不变量被破坏 ✗")}");

        return 0;
    }

    private static void SmokeCheckRpm()
    {
        var rpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
        {
            distance = 8150f,
            fuelPricePerTon = 850f,
            competitorSupply = 0.65f,
            demandMultiplier = 1.2f,
            brandEquity = 55f,
            seats = 290,
            aircraftType = "B787-9",
            passengerType = "business"
        });
        Console.WriteLine(
            $"[smoke] RPM PEK-LHR → score={rpm.score} {rpm.rating} " +
            $"rev={rpm.dailyRevenue} cost={rpm.dailyCost} profit={rpm.dailyProfit} 万/日 " +
            $"(fuel={rpm.fuelCostWan})");

        float runway = CashRunnelCalculator.GetRunwayMonths(300f, 80f);
        Console.WriteLine(
            $"[smoke] CashRunway 300/80 → {runway:F2} 月 ({CashRunnelCalculator.GetRunwayWarning(runway)})");
        Console.WriteLine();
    }

    private static void PrintSummary(Models.YearSummary s)
    {
        Console.WriteLine("--- Fiscal Year Summary (万美元) ---");
        Console.WriteLine($"Opening cash : {s.OpeningCash:F1}");
        Console.WriteLine($"Closing cash : {s.ClosingCash:F1}");
        Console.WriteLine($"Cash range   : [{s.MinCash:F1} .. {s.MaxCash:F1}]");
        Console.WriteLine($"Revenue      : {s.TotalRevenue:F1}");
        Console.WriteLine($"Cost         : {s.TotalCost:F1}");
        Console.WriteLine($"Net profit   : {s.TotalNetProfit:F1}");
        Console.WriteLine($"Avg monthly  : {s.AvgMonthlyProfit:F1}");
        Console.WriteLine($"Final runway : {s.FinalRunwayMonths:F1} 月 ({CashRunnelCalculator.GetRunwayWarning(s.FinalRunwayMonths)})");
        Console.WriteLine();
        Console.WriteLine("--- OPS Year Loop ---");
        Console.WriteLine($"Schedule eff : {s.AvgScheduleEfficiency:F2}");
        Console.WriteLine($"Avg delay P  : {s.AvgDelayProbability:F2}");
        Console.WriteLine($"OTP          : {s.OpeningOnTimePercent:F1} → {s.ClosingOnTimePercent:F1}");
        Console.WriteLine($"IRROPS cash  : {s.IrropsCashDeltaWan:F1}  ({s.IrropsNote})");
        Console.WriteLine();
        Console.WriteLine("--- FIN Year Loop ---");
        Console.WriteLine($"Credit       : {s.CreditRating}  ·  WACC {s.WaccPercent:F1}%");
        Console.WriteLine($"Budget       : {s.BudgetWan:F1} 万  ·  董事会 {s.BudgetVotes}/7 票");
        Console.WriteLine($"Carbon       : 排放 {s.CarbonEmissionsTons:F1} 吨 · 负债 {s.CarbonLiabilityWan:F1} 万 · 年成本 {s.CarbonCostWan:F1} 万");
        Console.WriteLine($"Financing    : 贷款 {s.LoanPrincipalWan:F1} 万 · 已付利息 {s.LoanInterestPaidWan:F1} 万");
        Console.WriteLine();
        Console.WriteLine("--- FLEET/NET Year Loop ---");
        Console.WriteLine($"Fleet        : {s.FleetCount} 架 · 均龄 {s.AvgFleetAge:F1} 年 · 年维护 {s.FleetAnnualMaintWan:F1} 万 · 残值 {s.FleetResidualWan:F0} 万");
        Console.WriteLine($"Seat mix     : ×{s.SeatMixMult:F3}（默认舱位 → 1.0）");
        Console.WriteLine($"Network      : {s.NetworkRouteCount} 条航线 · 网络日利 {s.NetworkDailyProfitWan:F1} 万 · 平均航距 {s.AvgRouteDistanceKm:F0} km");
        Console.WriteLine();
        Console.WriteLine("--- MKT Year Loop ---");
        Console.WriteLine($"Aux revenue  : {s.MktAuxRevenueWan:F1} 万");
        Console.WriteLine($"RASK/CASK    : ${s.AvgRaskUsd:F3} / ${s.AvgCaskUsd:F3} 每座公里");
        Console.WriteLine($"Peak season  : {s.PeakSeasonFestival} · ROI {s.PeakSeasonRoi:F2}");
        Console.WriteLine();
        Console.WriteLine("--- MKT B Year Loop (竞争/渠道/品牌) ---");
        Console.WriteLine($"竞争力       : 我司 {s.PlayerBenchScore:F0} ｜ 最强竞对 {s.BestRivalScore:F0} → {s.SituationalVerdict}");
        Console.WriteLine($"渠道         : 直销占比 {s.DirectRatio * 100:F0}% · 分销 ROI {s.DistChannelRoi:F2} · 直销 ROI {s.DirectChannelRoi:F2}");
        Console.WriteLine($"品牌         : 信任指数 {s.BrandTrustIndex:F0} · 危机等级 {s.BrandCrisisLevel}");
        Console.WriteLine();
        Console.WriteLine("--- STG Year Loop (战略/航权/联盟) ---");
        Console.WriteLine($"战略         : 一致性 {s.StrategicConsistency:F0} · 政府关系 {s.GovernmentRelation:F0} · 地缘风险 {s.GeoRiskIndex}");
        Console.WriteLine($"联盟         : 等级 {s.AllianceTier} · 增量收入 {s.AllianceRevenueLiftWan:F1} 万");
        Console.WriteLine($"航权/时刻槽  : 获批航权 {s.ApprovedRights} · 时刻槽 {s.SlotHoldings}");
        Console.WriteLine();
        Console.WriteLine("--- MNT Year Loop (维修/航材) ---");
        Console.WriteLine($"维修         : 策略 {s.MaintenanceStrategy} · 可用率 {s.FleetAvailability:F1}% · 成本倍率 {s.MaintenanceCostMultiplier:F2}");
        Console.WriteLine($"航材         : 库存健康 {s.PartsStockHealth:F0} · 缺料 {s.PartsShortageCount} · 定检 {s.ScheduledChecks} 项");
        Console.WriteLine();
        Console.WriteLine("--- HR Year Loop (人力/招聘/培训/工会) ---");
        Console.WriteLine($"人力         : 满意度 {s.EmployeeSatisfaction:F0} · 总人数 {s.TotalHeadcount} · 空缺率 {s.VacancyRate * 100:F0}%");
        Console.WriteLine($"成本/培训    : 人力成本占营收 {s.LaborCostRatio * 100:F0}% · 培训ROI {s.TrainingRoi:F2} · 罢工风险 {s.StrikeRisk:F0}");
        Console.WriteLine();
        Console.WriteLine("--- SAF Year Loop (安全评级/文化/审计) ---");
        Console.WriteLine($"安全         : 评级 {s.SafetyBand} ({s.SafetyRatingScore:F0}) · 文化指数 {s.SafetyCultureIndex:F0} · 文化类型 {s.SafetyCultureType}");
        Console.WriteLine($"审计/整改    : 通过率 {s.AuditPassRate:F0}% · 隐患整改率 {s.RectificationRate:F0}%");
        Console.WriteLine();
        Console.WriteLine("--- BOARD Year Loop (年度目标/满意度) ---");
        Console.WriteLine($"董事会       : 目标达成 {s.BoardTargetDone}/{s.BoardTargetTotal} · 达成率 {s.BoardAchievementRate:F0}% · 满意度增减 {s.BoardSatisfactionDelta:+0;-0;0} · 年末 {s.BoardSatisfaction:F0}");
        Console.WriteLine();
        Console.WriteLine("--- CROSS Year Loop (跨部门协同) ---");
        Console.WriteLine($"协同指数     : {s.CrossConsistency:F0}/100 ({s.CrossBand})");
        Console.WriteLine("Month | Phase      | Fuel  | Revenue | Cost   | Net    | Carbon | Cash  | OTP");
        Console.WriteLine("------+------------+-------+---------+--------+--------+--------+-------+------");
        foreach (var m in s.Months)
        {
            Console.WriteLine(
                $"  {m.Month,2}  | {MacroEconomyEngine.PhaseLabel(m.EconomicPhase),-10} | " +
                $"{m.FuelPriceUsdPerTon,5:F0} | {m.RevenueWan,7:F1} | {m.TotalCostWan,6:F1} | " +
                $"{m.NetProfitWan,6:F1} | {m.CarbonCostWan,6:F2} | {m.CashEndWan,6:F1} | {m.OnTimePercent,5:F1}");
        }
    }

    private static string BuildSummaryText(Models.YearSummary s, int seed, int year)
    {
        return
            $"seed={seed}\n" +
            $"year={year}\n" +
            $"opening={s.OpeningCash:F2}\n" +
            $"closing={s.ClosingCash:F2}\n" +
            $"revenue={s.TotalRevenue:F2}\n" +
            $"cost={s.TotalCost:F2}\n" +
            $"net={s.TotalNetProfit:F2}\n" +
            $"avg_monthly={s.AvgMonthlyProfit:F2}\n" +
            $"min_cash={s.MinCash:F2}\n" +
            $"max_cash={s.MaxCash:F2}\n" +
            $"runway={s.FinalRunwayMonths:F2}\n" +
            $"fleet={MinimalAirline.CreateFleet().Count}\n" +
            $"routes={MinimalAirline.CreateRoutes().Count}\n" +
            $"ops_eff={s.AvgScheduleEfficiency:F2}\n" +
            $"ops_delay={s.AvgDelayProbability:F2}\n" +
            $"otp_open={s.OpeningOnTimePercent:F2}\n" +
            $"otp_close={s.ClosingOnTimePercent:F2}\n" +
            $"irrops_cash={s.IrropsCashDeltaWan:F2}\n" +
            $"irrops_note={s.IrropsNote}\n" +
            $"fin_credit={s.CreditRating}\n" +
            $"fin_wacc={s.WaccPercent:F2}\n" +
            $"fin_budget={s.BudgetWan:F2}\n" +
            $"fin_budget_votes={s.BudgetVotes}\n" +
            $"fin_carbon_emissions={s.CarbonEmissionsTons:F2}\n" +
            $"fin_carbon_liability={s.CarbonLiabilityWan:F2}\n" +
            $"fin_carbon_cost={s.CarbonCostWan:F2}\n" +
            $"fin_loan={s.LoanPrincipalWan:F2}\n" +
            $"fin_interest={s.LoanInterestPaidWan:F2}\n" +
            $"fleet_count={s.FleetCount}\n" +
            $"fleet_avg_age={s.AvgFleetAge:F2}\n" +
            $"fleet_annual_maint={s.FleetAnnualMaintWan:F2}\n" +
            $"fleet_residual={s.FleetResidualWan:F2}\n" +
            $"fleet_seat_mix={s.SeatMixMult:F3}\n" +
            $"net_routes={s.NetworkRouteCount}\n" +
            $"net_daily_profit={s.NetworkDailyProfitWan:F2}\n" +
            $"net_avg_distance={s.AvgRouteDistanceKm:F2}\n" +
            $"net_route_fixed_cost={s.RouteFixedCostWan:F2}\n" +
            $"routefixed_base={EconomyBalanceParams.RouteFixedBaseDailyWan:F4}\n" +
            $"routefixed_base_default={EconomyBalanceParams.RouteFixedBaseDailyWanDefault:F4}\n" +
            $"routefixed_base_overridden={(EconomyBalanceParams.IsRouteFixedBaseOverridden ? 1 : 0)}\n" +
            $"mkt_aux_revenue={s.MktAuxRevenueWan:F2}\n" +
            $"mkt_rask={s.AvgRaskUsd:F3}\n" +
            $"mkt_cask={s.AvgCaskUsd:F3}\n" +
            $"mkt_peak_roi={s.PeakSeasonRoi:F2}\n" +
            $"mkt_peak_festival={s.PeakSeasonFestival}\n" +
            $"mktb_player_bench={s.PlayerBenchScore:F2}\n" +
            $"mktb_best_rival={s.BestRivalScore:F2}\n" +
            $"mktb_verdict={s.SituationalVerdict}\n" +
            $"mktb_direct_ratio={s.DirectRatio:F2}\n" +
            $"mktb_dist_roi={s.DistChannelRoi:F2}\n" +
            $"mktb_direct_roi={s.DirectChannelRoi:F2}\n" +
            $"mktb_brand_trust={s.BrandTrustIndex:F2}\n" +
            $"mktb_brand_level={s.BrandCrisisLevel}\n" +
            $"stg_gov_rel={s.GovernmentRelation:F2}\n" +
            $"stg_consistency={s.StrategicConsistency:F2}\n" +
            $"stg_geo_risk={s.GeoRiskIndex}\n" +
            $"stg_alliance_tier={s.AllianceTier}\n" +
            $"stg_alliance_lift={s.AllianceRevenueLiftWan:F2}\n" +
            $"stg_slots={s.SlotHoldings}\n" +
            $"stg_rights={s.ApprovedRights}\n" +
            $"mnt_strategy={s.MaintenanceStrategy}\n" +
            $"mnt_availability={s.FleetAvailability:F2}\n" +
            $"mnt_cost_mult={s.MaintenanceCostMultiplier:F2}\n" +
            $"mnt_parts_health={s.PartsStockHealth:F2}\n" +
            $"mnt_parts_shortage={s.PartsShortageCount}\n" +
            $"mnt_grounded={s.GroundedCount}\n" +
            $"mnt_checks={s.ScheduledChecks}\n" +
            $"hr_satisfaction={s.EmployeeSatisfaction:F2}\n" +
            $"hr_headcount={s.TotalHeadcount}\n" +
            $"hr_vacancy_rate={s.VacancyRate:F2}\n" +
            $"hr_labor_ratio={s.LaborCostRatio:F2}\n" +
            $"hr_training_roi={s.TrainingRoi:F2}\n" +
            $"hr_strike_risk={s.StrikeRisk:F2}\n" +
            $"saf_rating={s.SafetyRatingScore:F2}\n" +
            $"saf_band={s.SafetyBand}\n" +
            $"saf_culture_index={s.SafetyCultureIndex:F2}\n" +
            $"saf_culture_type={s.SafetyCultureType}\n" +
            $"saf_audit={s.AuditPassRate:F2}\n" +
            $"saf_rectification={s.RectificationRate:F2}\n" +
            $"board_done={s.BoardTargetDone}\n" +
            $"board_total={s.BoardTargetTotal}\n" +
            $"board_rate={s.BoardAchievementRate:F2}\n" +
            $"board_delta={s.BoardSatisfactionDelta:F2}\n" +
            $"board_sat={s.BoardSatisfaction:F2}\n" +
            $"cross_score={s.CrossConsistency:F2}\n" +
            $"cross_band={s.CrossBand}\n";
    }

    private static int ParseInt(string[] args, string key, int fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == key && int.TryParse(args[i + 1], out int v))
                return v;
        return fallback;
    }

    private static float ParseFloat(string[] args, string key, float fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == key && float.TryParse(args[i + 1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v))
                return v;
        return fallback;
    }

    private static string ParseString(string[] args, string key, string fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == key)
                return args[i + 1];
        return fallback;
    }
}
