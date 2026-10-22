using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;

namespace AirlineCEO.Balance;

/// <summary>
/// 全航司**航线级**清单（<c>--routeinventory</c>）：逐家列出 枢纽→目的地 / 航距 / 周班次 / 机型。
///
/// 与 <see cref="UniverseFrequencyReport"/> 的分工：那边只出**分布**（直方图、分带占比），
/// 回答「班次是否都挤在 7 班」；这里出**明细**，回答「某座机场入表后，到底哪些航司的网络变了」。
///
/// 为什么需要它：<see cref="TakeoverWorldBuilder.BuildDestinationPool"/> 的目的地池来自
/// **整张** <see cref="CityEconomyCatalog"/>，所以往目录里加一座城市，连带影响的不只是
/// 「多一家 hub_ 航司」——既有航司的候选池也变了。只比较航司总数与航线总数，看不出
/// 「老航司的某条线被新城市顶掉了」这种替换；必须逐条比对目的地集合。
///
/// 判定口径（本工具输出的用途）：
/// 若某座新机场**只出现在它自己的 hub 航司**的航线里，则既有航司的网络成员未变
/// （池按需求降序取前 N，落选即未挤入前 N，也就没有顶掉任何既有目的地）。
/// </summary>
internal static class RouteInventoryReport
{
    /// <summary>
    /// 本次关注的新入表机场：批次 A（2026-10-21）+ 批次 B（2026-10-22）。
    /// 两批都留着 —— 断言是「既有航司网络没被这些新面孔顶掉」，批次 A 的口径仍然要守。
    /// </summary>
    static readonly string[] Watched = { "ZUH", "WUX", "NGB", "WNZ", "NNG", "KWE", "TYN", "HET", "TNA", "LXA" };

    /// <summary>
    /// 已接受的「新入表机场被跨境外站航司选为目的地」基线，值是**接受理由**。
    ///
    /// 2026-10-22 D2（★4/★5 分界从 IATA 哈希改为客流阈值）的连带结果：
    /// 羽田 HND（78M）由 ★4 升到 ★5，航线 25→39 条，候选池变大后新增 HND→NGB / HND→WNZ / HND→TNA。
    /// 这三条**合法**（两端皆门户，K18 的 IsGateway 断言已过），只是路网变了，故记为基线。
    /// 改前/改后逐条比对过：DXB、HND 各 +15 条，DEN −11 条，IST −15 条，净 +4 条（763→767）。
    ///
    /// 增删这个字典时请在值里写清原因与日期 —— 它是这条守卫唯一的记忆。
    /// </summary>
    static readonly Dictionary<string, (int count, string reason)> AcceptedCrossBorderDest = new()
    {
        ["NGB"] = (1, "HND ★4→★5 扩容（2026-10-22 D2）"),
        ["WNZ"] = (1, "HND ★4→★5 扩容（2026-10-22 D2）"),
        ["TNA"] = (1, "HND ★4→★5 扩容（2026-10-22 D2）"),
    };

    public static int Run()
    {
        var airlines = new List<AirlineProfile>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in PresetAirlines.AllSelectable())
        {
            if (seed == null) continue;
            var p = TakeoverWorldBuilder.BuildPreview(seed.id);
            // 枢纽里的 4 个具名预设会解析回 preset_* id，需去重（与 --frequniverse 同口径）。
            if (p == null || !seenIds.Add(p.id)) continue;
            airlines.Add(p);
        }

        Console.WriteLine("=== 全航司航线清单（接管预览口径）===");
        Console.WriteLine($"（{airlines.Count} 家可接管航司）");
        Console.WriteLine();
        Console.WriteLine("#hub\tdest\tkm\tweekly\taircraft");

        int routeTotal = 0;
        var destUse = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        // 新入表机场被「外站航司」当成目的地的次数 —— 这才是既有网络被动过的证据：
        // 枢纽自身永远是 origin，不会出现在自家 dest 列，故「没人以 ZUH 为目的地」本身不等于异常。
        var foreignDest = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // 其中**跨境**的那部分：断言只看这一列。
        // 2026-10-22 批次 B 的实测教训：成都把贵阳 / 济南吸进池子是正常国内线（CTU→KWE、CTU→TNA），
        // 拿「有人飞进来」当失败会让断言对正常现象永久报红；真正该拦的是**跨境**——
        // 拉萨 LXA 当初就是被孟买 / 伊斯坦布尔选成目的地（BOM→LXA、IST→LXA），还顶掉了 BOM→SIN、IST→BOS。
        var crossBorderDest = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ownDepartures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // K18 护栏：全表里每一条跨境航线的两端都必须有资格（tier ≤ 1）。
        // 只看新机场不够 —— 规则一旦失效，最先坏的可能是既有枢纽飞支线（如 IST→某支线）。
        int intlViolations = 0;
        var intlSamples = new List<string>();
        for (int i = 0; i < Watched.Length; i++)
        {
            foreignDest[Watched[i]] = 0;
            crossBorderDest[Watched[i]] = 0;
            ownDepartures[Watched[i]] = 0;
        }

        foreach (var p in airlines)
        {
            if (p.routes == null) continue;
            foreach (var r in p.routes)
            {
                if (r == null) continue;
                routeTotal++;
                Console.WriteLine($"{r.origin}\t{r.destination}\t{r.distanceKm:F0}\t{r.weeklyFlights}\t{r.aircraftType}");

                // K18：跨境航线两端都必须是门户机场。判据**只**认 RouteGeography.IsGateway，
                // 不在这里另写一份（本工具曾按 tier≤1 自查，结果与游戏规则各说各话）。
                {
                    string co = CountryOf(r.origin), cd = CountryOf(r.destination);
                    if (co.Length > 0 && cd.Length > 0 && !string.Equals(co, cd, StringComparison.Ordinal))
                    {
                        bool okO = RouteGeography.IsGateway(r.origin);
                        bool okD = RouteGeography.IsGateway(r.destination);
                        if (!okO || !okD)
                        {
                            intlViolations++;
                            if (intlSamples.Count < 8)
                                intlSamples.Add($"{r.origin}(t{AirportCatalog.TierOfCode(r.origin)})→"
                                                + $"{r.destination}(t{AirportCatalog.TierOfCode(r.destination)})");
                        }
                    }
                }

                if (!destUse.TryGetValue(r.destination, out var users))
                {
                    users = new List<string>();
                    destUse[r.destination] = users;
                }
                users.Add(p.hubAirport);

                for (int k = 0; k < Watched.Length; k++)
                {
                    string w = Watched[k];
                    if (string.Equals(r.origin, w, StringComparison.OrdinalIgnoreCase))
                        ownDepartures[w]++;
                    if (string.Equals(r.destination, w, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(p.hubAirport, w, StringComparison.OrdinalIgnoreCase))
                    {
                        foreignDest[w]++;
                        string carrier = CountryOf(p.hubAirport);
                        string target = CountryOf(w);
                        if (carrier.Length > 0 && target.Length > 0 && !string.Equals(carrier, target, StringComparison.Ordinal))
                            crossBorderDest[w]++;
                    }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"#航司 {airlines.Count} 家 · 航线 {routeTotal} 条 · 不同目的地 {destUse.Count} 座");
        Console.WriteLine();
        Console.WriteLine("=== 新入表机场的网络归属 ===");
        foreach (var w in Watched)
        {
            int own = ownDepartures.TryGetValue(w, out int o) ? o : 0;
            int foreign = foreignDest.TryGetValue(w, out int f) ? f : 0;
            int cross = crossBorderDest.TryGetValue(w, out int c) ? c : 0;
            Console.WriteLine($"  {w}: 本场航司出港 {own} 条 · 被外站航司当目的地 {foreign} 条"
                              + $"（其中跨境 {cross} 条）");
            // 跨境飞进来 = 既有航司的候选池里它挤掉了别人，而且挤出来的是国际线，必须逐条比对改前清单。
            // 本国内部被吸收（如 CTU→KWE）属正常网络扩张，只报数不判失败。
        }
        // 跨境流入的判据：**机场集合与条数都必须恰好等于已接受基线**。
        // 不用「必须为 0」——路网正经变化（分档修正就会变）时它会永久报红，久而久之就被无视；
        // 也不用「只要在名单里就放过」——那样基线过期了没人知道。
        // 集合与条数都要卡：多出一个机场（漏入）翻红、基线里少一个机场（路网又变了）翻红、
        // 同一机场的跨境条数变了也翻红 —— 只比集合会漏掉「第二家外站航司也挤进来」这种情况。
        var crossMissing = new List<string>();
        var crossExtra = new List<string>();
        var crossDrift = new List<string>();
        foreach (var w in Watched)
        {
            int actual = crossBorderDest.TryGetValue(w, out int c) ? c : 0;
            bool inBase = AcceptedCrossBorderDest.TryGetValue(w, out var basis);
            if (actual > 0 && !inBase) crossExtra.Add(w);
            if (actual == 0 && inBase) crossMissing.Add(w);
            if (actual > 0 && inBase && actual != basis.count)
                crossDrift.Add($"{w} {basis.count}→{actual} 条");
        }

        // —— 现金跑道口径（2026-10-21 修正）——
        // 旧实现 `cash / Mathf.Max(1f, cost − 0.35*revenue)` 在收入覆盖成本时把分母夹成 1，
        // 跑道退化成现金原值（★3 报 1200.0 月、★4 报 2000.0 月）。现在必须二选一：
        // 要么「无净烧钱」，要么一个与净烧钱自洽的有限月数。
        int runwayBad = 0;
        foreach (var p in airlines)
        {
            bool ok = p.cashRunwayUnbounded
                ? p.monthlyNetBurn <= 0.01f
                : p.monthlyNetBurn > 0.01f
                  && Math.Abs(p.cashRunwayMonths - p.cash / p.monthlyNetBurn) < 0.05f;
            if (!ok) runwayBad++;
        }

        Console.WriteLine();
        Console.WriteLine("=== 断言 ===");
        int failures = 0;
        Assert(ref failures, routeTotal > 0, $"航线 {routeTotal} 条（应 > 0）");
        Assert(ref failures, destUse.Count > 0, $"不同目的地 {destUse.Count} 座（应 > 0）");
        Assert(ref failures, crossExtra.Count == 0 && crossMissing.Count == 0 && crossDrift.Count == 0,
            "跨境流入目的地与已接受基线一致（新增漏入 " + crossExtra.Count + " 处"
            + (crossExtra.Count > 0 ? "：" + string.Join("、", crossExtra) : "")
            + "；基线中已消失 " + crossMissing.Count + " 处"
            + (crossMissing.Count > 0 ? "：" + string.Join("、", crossMissing) : "")
            + "；条数漂移 " + crossDrift.Count + " 处"
            + (crossDrift.Count > 0 ? "：" + string.Join("、", crossDrift) : "")
            + "；基线来源见 AcceptedCrossBorderDest 注释）");
        Assert(ref failures, intlViolations == 0,
            $"跨境航线两端都是门户机场（越界 {intlViolations} 条"
            + (intlSamples.Count > 0 ? "：`" + string.Join("`、`", intlSamples) + "`" : "")
            + "；判据见 RouteGeography.IsGateway）");
        for (int i = 0; i < Watched.Length; i++)
        {
            string w = Watched[i];
            int own = ownDepartures.TryGetValue(w, out int o) ? o : 0;
            Assert(ref failures, own > 0, $"{w} 的本场航司展开了出港航线（{own} 条，应为 > 0）");
        }
        Assert(ref failures, runwayBad == 0,
            $"现金跑道与净烧钱自洽（不自洽 {runwayBad} 家；无上限时必须是「无净烧钱」，不得印现金原值当月数）");

        // —— 星级必须是客流的单调函数（2026-10-22 D2 修复的回归）——
        // 修复前 ★4/★5 用 IATA 字符串哈希分界（h % 5 == 0 ? 5 : 4）：
        // 87M 的迪拜被分成 ★4，76M 的伊斯坦布尔、77.8M 的丹佛反而是 ★5。
        // 当时那条「更高星级必须更紧」的断言是**循环的**——现金本就按档位反解出来，
        // 所以它只能发现「烧钱口径改了但现金没重解」，永远不可能发现档位分错。
        // 这条断言与档位无关，只看「按客流排序后档位是否逆序」，档位分错立刻翻红。
        var curatedHubs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CAN", "PEK", "PVG", "CTU",   // 四个具名预设按设计强制指定档位（§18.1 待裁定项 3）
        };
        var ranked = new List<(string hub, int star, float pax)>();
        foreach (var p in airlines)
        {
            if (p == null || curatedHubs.Contains(p.hubAirport)) continue;
            var e = CityEconomyCatalog.Find(p.hubAirport);
            if (e == null) continue;
            ranked.Add((p.hubAirport, p.difficulty, e.paxMillion));
        }
        ranked.Sort((a, b) => a.pax.CompareTo(b.pax));
        int inversionTotal = 0;
        var inversions = new List<string>();
        for (int i = 1; i < ranked.Count; i++)
        {
            if (ranked[i].star >= ranked[i - 1].star) continue;
            inversionTotal++;
            // 样例只留前 5 条，但**计数照全量**——否则报「逆序 5 条」会掩盖真实规模。
            if (inversions.Count < 5)
                inversions.Add($"{ranked[i - 1].hub}({ranked[i - 1].pax:F0}M ★{ranked[i - 1].star})"
                               + $" > {ranked[i].hub}({ranked[i].pax:F0}M ★{ranked[i].star})");
        }
        Assert(ref failures, inversionTotal == 0,
            $"星级随客流单调不减（逆序 {inversionTotal} 处"
            + (inversionTotal > 0 ? "，样例：" + string.Join("、", inversions) : "")
            + "；具名预设 CAN/PEK/PVG/CTU 按设计豁免，另由下一条单独校验）");

        // —— 具名预设之间必须自洽（2026-10-22 D2 第三步）——
        // 上面那条豁免了四个具名预设（它们是手写旗舰示例，允许与「客流→档位」规则差一档），
        // 但**豁免不等于不管**。这个豁免正是让「广州★2 / 成都★5」颠倒长期存活的漏洞：
        // 两端都跳过检查，于是没有任何断言能发现四个预设之间的顺序反了。
        // 现在的口径：四个预设不参与「对齐规则」的检查，但**四个之间的档位必须随客流严格递增**。
        {
            var curated = new List<(string hub, int star, float pax)>();
            foreach (var p in airlines)
            {
                if (p == null || !curatedHubs.Contains(p.hubAirport)) continue;
                var ce = CityEconomyCatalog.Find(p.hubAirport);
                if (ce == null) continue;
                curated.Add((p.hubAirport, p.difficulty, ce.paxMillion));
            }
            curated.Sort((a, b) => a.pax.CompareTo(b.pax));
            var ladderBad = new List<string>();
            for (int i = 1; i < curated.Count; i++)
                if (curated[i].star <= curated[i - 1].star)
                    ladderBad.Add($"{curated[i - 1].hub}({curated[i - 1].pax:F0}M ★{curated[i - 1].star})"
                                  + $" 未低于 {curated[i].hub}({curated[i].pax:F0}M ★{curated[i].star})");
            Assert(ref failures, curated.Count == 4 && ladderBad.Count == 0,
                $"具名预设按客流升序档位严格递增（{curated.Count} 家"
                + (curated.Count > 0
                    ? "：" + string.Join("、", curated.ConvertAll(x => $"{x.hub}★{x.star}"))
                    : "")
                + (ladderBad.Count > 0 ? "；违例：" + string.Join("、", ladderBad) : "")
                + "）");

            // —— 预设的「枢纽归属」必须只有一处真源（2026-10-22 D2 第三步）——
            // 坑：具名预设的枢纽其实来自 Base(...) 里写死的 hub，而 FromHub 的分支表只决定
            // 「哪座城市被替换成哪个预设」。两处错位时不会有任何编译/运行错误：
            // BuildPreview("preset_startup") 仍会返回 Base 里的那座城市，
            // 而选司列表里 CTU 那一格却显示成 Startup —— 报表读的是前者，于是断言全绿、界面却错。
            // （本次修复中我正好踩了这个坑：只改分支表不改 Base，报表读数毫无变化。）
            // 这里逐城核对：凡 FromHub 返回具名预设的城市，必须等于该预设 Base 里写死的枢纽
            //（对照 Template(id)，也就是 BuildPreview(id) 真正读取的那一份）。
            // 不能拿 hp.hubAirport 比 —— Homed 会把它改成请求的城市，那样是拿自己和自己比，永远通过。
            var homeBad = new List<string>();
            foreach (var ce in CityEconomyCatalog.All)
            {
                if (ce == null || string.IsNullOrEmpty(ce.iata)) continue;
                var hp = PresetAirlines.FromHub(ce.iata);
                if (hp == null || hp.id == null || !hp.id.StartsWith("preset_", StringComparison.Ordinal)) continue;
                var canonical = PresetAirlines.Template(hp.id);
                string canonicalHub = canonical != null ? canonical.hubAirport : "?";
                if (!string.Equals(canonicalHub, ce.iata, StringComparison.OrdinalIgnoreCase))
                    homeBad.Add($"{ce.iata}→{hp.id}(Base 在 {canonicalHub})");
            }
            Assert(ref failures, homeBad.Count == 0,
                "具名预设的分支表与 Base 枢纽一致（错位 " + homeBad.Count + " 处"
                + (homeBad.Count > 0 ? "：" + string.Join("、", homeBad) : "") + "）");
        }


        // —— 接管界面的「难度梯度」读数：新入表城市继承 ★2 预设（青禾航空），属最容易的一档。
        //    这里把每档的家数 / 机队 / 航线 / 现金 / 现金跑道摆出来，便于判断加城市是否把
        //    「简单开局」堆得过多——只数总家数看不出难度分布的变化。
        Console.WriteLine();
        Console.WriteLine("=== 接管预览关键读数（按星级）===");
        Console.WriteLine("星级  家数  机队均  航线均  现金均  净烧钱均  跑道");
        var byStar = new SortedDictionary<int, (int n, float fleet, float routes, float cash, float burn, float runway, int unbounded)>();
        var byStarComponents = new SortedDictionary<int, (float hq, float maint, float lease, float labor, float debt, float ins, float route, float trans, float burn, float dr)>();
        foreach (var p in airlines)
        {
            byStar.TryGetValue(p.difficulty, out var agg);
            agg.n++;
            agg.fleet += p.fleetCount;
            agg.routes += p.routeCount;
            agg.cash += p.cash;
            agg.burn += p.monthlyNetBurn;
            // 无上限的跑道不能参与平均：拿 0 拉低均值、拿现金原值又回到旧缺陷。
            if (p.cashRunwayUnbounded) agg.unbounded++;
            else agg.runway += p.cashRunwayMonths;
            byStar[p.difficulty] = agg;

            float trans = 0f;
            if (p.transitionBusinesses != null)
                for (int i = 0; i < p.transitionBusinesses.Count; i++)
                {
                    var c0 = p.transitionBusinesses[i];
                    if (c0 == null) continue;
                    // 与 TransitionBusinessLogic.MonthlyIncome 同口径：按剩余档期封顶，
                    // 不把 5 天包机（2.6 万/日）外推成永续月入 78 万。
                    int left = Math.Min(30, Math.Max(0, c0.totalDays - c0.daysUsed));
                    trans += Math.Max(0f, c0.EffectiveDailyCash) * left;
                }
            byStarComponents.TryGetValue(p.difficulty, out var c);
            c.hq += p.monthlyCost;
            // monthlyFixedCost = 维护 + 月租 + 人力；这里拆开印，别把月租算进维护列（会重复计入）。
            c.maint += p.monthlyFixedCost - LaborOf(p) - LeaseOf(p);
            c.lease += LeaseOf(p);
            c.labor += LaborOf(p);
            c.debt += p.monthlyDebtService;
            c.ins += p.monthlyInsurance;
            c.dr += p.debtRatio;
            c.route += p.monthlyRevenue - trans;
            c.trans += trans;
            c.burn += p.monthlyNetBurn;
            byStarComponents[p.difficulty] = c;
        }

        // —— 星级 = 难度 = 寿命（K09）——
        // 逐档比较「实际平均跑道」与 TakeoverWorldBuilder.TargetRunwayMonths：
        // ① 带宽 ±20%：现金是按目标反推的，成本模型一改就会掉出带外，必须立刻发现；
        // ② 逐档递减：★2 必须比 ★3 宽，★3 比 ★4 宽……否则「星级」只是个装饰数字。
        float prevRunway = float.MaxValue;
        foreach (var kv in byStar)
        {
            var a = kv.Value;
            int finite = a.n - a.unbounded;
            float avg = finite > 0 ? a.runway / finite : 0f;
            float target = TakeoverWorldBuilder.TargetRunwayMonths(kv.Key);
            Assert(ref failures, a.unbounded == 0 && avg >= target * 0.8f && avg <= target * 1.2f,
                $"★{kv.Key} 平均跑道 {avg:F1} 月落在目标 {target:F0} 月 ±20% 带内（无上限 {a.unbounded} 家）");
            Assert(ref failures, avg < prevRunway,
                $"★{kv.Key} 平均跑道 {avg:F1} 月 < 上一档 "
                + (prevRunway >= float.MaxValue ? "（首档，跳过比较）" : $"{prevRunway:F1} 月")
                + "（更高星级必须更紧）");
            prevRunway = avg;
        }

        // —— ★2 连续标度：个案跑道不得漂移，且「克隆体」必须真的消失（2026-10-22 D2 第二步）——
        // 均值断言（上面那条 ±20%）挡不住离散度：★2 的 59 家若一半 4 月、一半 12 月，
        // 均值照样 8.0 落在带内。故这里逐家卡 ±15%。
        // 第二个断言针对「克隆」本身：修复前 59 家的 (机队, 航线) 只有 1 种组合，
        // 现在必须随客流展开出多种；若有人把 ScaleStartup 改回去，这条会立刻翻红。
        {
            float s2min = float.MaxValue, s2max = 0f;
            var combos = new HashSet<string>();
            foreach (var p in airlines)
            {
                if (p.difficulty != 2 || p.cashRunwayUnbounded) continue;
                if (p.cashRunwayMonths < s2min) s2min = p.cashRunwayMonths;
                if (p.cashRunwayMonths > s2max) s2max = p.cashRunwayMonths;
                combos.Add(p.fleetCount + "x" + p.routeCount);
            }
            float t2 = TakeoverWorldBuilder.TargetRunwayMonths(2);
            // 带宽取 ±12%，不是 ±15%：实测「停用标度」（即修复前状态）的个案区间是 6.80~8.01，
            // 恰好落在 ±15% 内侧（下限 6.80 = 8×0.85），±15% 根本挡不住它。
            // 现行标度实测 7.14~8.86（±10.8%），±12% 既有判定力、也留约 1.2pt 余量。
            Assert(ref failures, s2min >= t2 * 0.88f && s2max <= t2 * 1.12f,
                $"★2 逐家跑道落在目标 {t2:F0} 月 ±12% 带内（实测 {s2min:F2}~{s2max:F2} 月；"
                + "只卡均值会漏掉「一半 4 月一半 12 月、均值仍 8」的离散）");
            Assert(ref failures, combos.Count >= 3,
                $"★2 规模不再克隆（机队×航线 组合数 {combos.Count}，应 ≥ 3；"
                + "修复前 59 家共用一套参数只有 1 种）");
        }
        foreach (var kv in byStar)
        {
            var a = kv.Value;
            int finite = a.n - a.unbounded;
            // 与 UI 同一把尺：超过 120 月一律「120 月以上」，不印 1230.8 这种没有决策意义的数。
            string runway = finite > 0
                ? (a.runway / finite >= 120f ? "120 月以上" : $"{a.runway / finite:F1} 月")
                : "—";
            if (a.unbounded > 0) runway += $"（另 {a.unbounded} 家无净烧钱）";
            Console.WriteLine($"  ★{kv.Key}  {a.n,3}  {a.fleet / a.n,6:F1}  {a.routes / a.n,6:F1}  {a.cash / a.n,6:F0}"
                              + $"  {a.burn / a.n,8:F1}  {runway}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 净烧钱的构成（按星级，单位万美元/月；航线收入已含 0.85 机组折）===");
        Console.WriteLine("星级  总部  机队维护  月租  人力  还本付息    保费  航线收入  过渡收入  净烧钱  现金均");
        foreach (var kv in byStarComponents)
        {
            var c = kv.Value;
            int n = byStar[kv.Key].n;
            Console.WriteLine($"  ★{kv.Key}  {c.hq / n,5:F0}  {c.maint / n,7:F1}  {c.lease / n,5:F1}"
                              + $"  {c.labor / n,5:F0}  {c.debt / n,9:F1}  {c.ins / n,6:F1}  {c.route / n,8:F1}  {c.trans / n,7:F1}"
                              + $"  {c.burn / n,7:F1}  {byStar[kv.Key].cash / n,6:F0}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 现金反推（按目标跑道，闭式解；改成本模型后照这里回填 PresetAirlines）===");
        // 月供本身是现金的函数：principal = 现金×负债率×2.2，月供 = principal×2.8%。
        // 于是 runway = cash / (B + k×cash) 是含反馈的方程，闭式解为
        //     cash = T×B / (1 − T×k)，k = 负债率×2.2×0.028，
        // B 取运营口径（不含还本付息——它已由 k×cash 表达）。
        Console.WriteLine("星级  目标  运营烧钱B  负债率  反馈k    建议现金  当前现金  当前跑道");
        foreach (var kv in byStarComponents)
        {
            var c = kv.Value;
            int n = byStar[kv.Key].n;
            float b = (c.burn - c.debt) / n;
            float dr = c.dr / n;
            float k = dr * 2.2f * 0.028f;
            float target = TakeoverWorldBuilder.TargetRunwayMonths(kv.Key);
            float denom = 1f - target * k;
            float cash = denom > 0.01f ? target * b / denom : float.NaN;
            float cur = byStar[kv.Key].cash / n;
            float runwayNow = b + k * cur > 0.01f ? cur / (b + k * cur) : -1f;
            Console.WriteLine($"  ★{kv.Key}  {target,4:F0}  {b,9:F1}  {dr,6:P0}  {k,7:F4}  {cash,9:F0}  {cur,8:F0}  {runwayNow,7:F2}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 新入表枢纽的接管读数 ===");
        foreach (var p in airlines)
        {
            for (int i = 0; i < Watched.Length; i++)
            {
                if (!string.Equals(p.hubAirport, Watched[i], StringComparison.OrdinalIgnoreCase)) continue;
                float trans = 0f;
                if (p.transitionBusinesses != null)
                    for (int k = 0; k < p.transitionBusinesses.Count; k++)
                    {
                        var c0 = p.transitionBusinesses[k];
                        if (c0 == null) continue;
                        int left = Math.Min(30, Math.Max(0, c0.totalDays - c0.daysUsed));
                        trans += Math.Max(0f, c0.EffectiveDailyCash) * left;
                    }
                float labor = LaborOf(p), lease = LeaseOf(p);
                Console.WriteLine($"  {p.hubAirport} ★{p.difficulty} {p.nameCN}  机队 {p.fleetCount} · 航线 {p.routeCount}"
                                  + $" · 现金 {p.cash:F0} 万 · 负债率 {p.debtRatio:P0}"
                                  + $" · 成本 总部 {p.monthlyCost:F0} + 维护 {p.monthlyFixedCost - labor - lease:F0}"
                                  + $" + 租金 {lease:F0} + 人力 {labor:F0} + 还本付息 {p.monthlyDebtService:F0}"
                                  + $" + 保费 {p.monthlyInsurance:F0}"
                                  + $" = {p.monthlyCost + p.monthlyFixedCost + p.monthlyDebtService + p.monthlyInsurance:F0}"
                                  + $" · 收入 {p.monthlyRevenue:F0}（过渡 {trans:F0}）"
                                  + $" · 净烧钱 {p.monthlyNetBurn:F1} 万/月 · 现金跑道 {TakeoverWorldBuilder.RunwayText(p)}"
                                  + $" · 机组缺口 {p.crewVacancyPercent}% · 遗留问题 {p.legacyIssues?.Count ?? 0} 项");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "全部通过 ✓" : $"失败 {failures} 项 ✗");
        return failures == 0 ? 0 : 1;
    }

    static float LaborOf(AirlineProfile p) =>
        AirlineCEO.Logic.HrEconomics.EffectiveAnnualLaborCostWan(p.fleetCount, 0f, 0f) / 12f;

    /// <summary>机场所属国家（中文名，取自城市经济底表）；查不到返回空串 —— 空串一律不计跨境。</summary>
    static string CountryOf(string iata)
    {
        var e = CityEconomyCatalog.Find(iata);
        return e != null && !string.IsNullOrEmpty(e.countryZh) ? e.countryZh : "";
    }

    static float LeaseOf(AirlineProfile p)
    {
        float s = 0f;
        if (p.aircraft == null) return s;
        for (int i = 0; i < p.aircraft.Count; i++)
            if (p.aircraft[i] != null) s += Math.Max(0f, p.aircraft[i].monthlyLeaseWan);
        return s;
    }

    static void Assert(ref int failures, bool ok, string label)
    {
        if (!ok) failures++;
        Console.WriteLine($"  [{(ok ? " OK " : "FAIL")}] {label}");
    }
}
