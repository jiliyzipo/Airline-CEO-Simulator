using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;

namespace SlotReport
{
    /// <summary>
    /// 81 个协调机场的「逐刻时刻槽容量 / 占用」只读报表。
    ///
    /// ★2026-11-08 二次（用户：重写表，要详细到每个时刻槽，并注明跑道数）。
    ///
    /// 数据全部取自游戏源码，不复制任何公式：
    /// <list type="bullet">
    /// <item>逐刻容量 / 已占 / 可申请 / 持有方 —— <see cref="IndustryAirlineLogic.ChipsOf"/>，
    ///       与游戏内「机场时刻」卡片、时刻详情浮窗同一份铺排。</item>
    /// <item>跑道数 —— 运行时取 <c>AirportRunways.cs</c>（= 每刻槽容量），配置文本取
    ///       `runways.tsv`（源自 docs/handoff/2026-11-06_机场时刻容量对照表.md，逐条可查，
    ///       含 CAAC / 公开评估 / 按跑道推算三种来源标注）。两处数值不一致会告警。</item>
    /// </list>
    ///
    /// 我司持仓按 0 条计：本报表离线运行，拿不到存档；本局页头即「我司持有 0 个」。
    /// </summary>
    public static class Program
    {
        static readonly string[] BandShort = { "早", "中", "晚", "夜" };

        /// <summary>
        /// 时段区间**按代码真实归属**写（<see cref="ScheduleSlots.BandOfDep"/>：小时 &lt; 10 都归早段）。
        ///
        /// ★不能照抄游戏卡片上的「早 06:00–09:45」那段文案：卡片文案假设 00–05 时全网容量为 0，
        /// 而实际数据里 61 个机场在 00:00–05:45 确有公布容量（DXB / DOH 各 24 个刻）。
        /// 报表里若也写 06:00 起，就会出现「标着 06:00 起的段里躺着 00:15」这种自相矛盾。
        /// </summary>
        static readonly string[] BandRange = { "00:00–09:45", "10:00–14:45", "15:00–19:45", "20:00–23:45" };

        const int GridTicksPerDay = 96;   // 24h × 4（15 分钟一格）

        sealed class ClockRow
        {
            public string code, city, tierLabel;
            public int tier, runway;
            public string runwayCfg, runwaySrc;
            public string clock;
            public int band;
            public int depCap, depHeld, depMine, depFree;
            public int arrCap, arrHeld, arrFree;
            public string holders;
        }

        sealed class AirportRow
        {
            public string code, city, tierLabel, coord, curfew;
            public int tier, runway;
            public string runwayCfg, runwaySrc;
            public int[] cap = new int[4], dep = new int[4], arr = new int[4];
            public int[] open = new int[4];
            public int ticks;          // 已公布刻度数
            public int capTotal, depTotal, arrTotal, openTotal;
            public int usagePct;
        }

        public static int Main(string[] args)
        {
            string outDir = args != null && args.Length > 0 ? args[0] : Path.Combine("out", "reports");
            Directory.CreateDirectory(outDir);

            var runways = LoadRunways();

            var overview = new List<AirportRow>();
            var detail = new List<ClockRow>();

            foreach (var ap in AirportCatalog.Airports)
            {
                if (!ap.coordinated) continue;
                if (!AirportCatalog.IsCoordinated(ap.code)) continue;

                var o = new AirportRow
                {
                    code = ap.code,
                    city = ap.nameZh,
                    tier = ap.tier,
                    tierLabel = AirportCatalog.TierLabel(ap.tier),
                    coord = AirportCatalog.CoordLabel(ap.code),
                    curfew = AirportCatalog.CurfewLabel(ap.code),
                };
                // ★2026-11-09：跑道数已是**玩法参数**（每刻槽容量 = 跑道数），运行时副本在
                //   AirportRunways.cs；runways.tsv 只提供配置文本。
                //   两种读法分开对账：tsv 对的是**物理跑道**，容量看的是**等效跑道**
                //   （申报超额，如 LHR/DXB 的混合运行），两者不等是**正常**的、不是分叉。
                o.runway = AirportCatalog.RunwayCount(ap.code);
                int physical = AirportCatalog.PhysicalRunwayCount(ap.code);
                string overReason = AirportCatalog.EffectiveRunwayReason(ap.code);
                if (runways.TryGetValue(ap.code, out var rw))
                {
                    o.runwayCfg = rw.cfg;
                    o.runwaySrc = rw.src;
                    if (rw.count != physical)
                        Console.WriteLine($"[警告] {ap.code} 物理跑道数不一致：runways.tsv={rw.count} / AirportRunways.cs={physical}（以代码为准）");
                    if (o.runway != physical)
                    {
                        o.runwayCfg += $"（等效跑道 {o.runway}：{overReason}）";
                        o.runwaySrc += " · 等效";
                    }
                }
                else
                {
                    o.runwayCfg = "（表内无此机场，待补）";
                    o.runwaySrc = "缺";
                    Console.WriteLine($"[警告] {ap.code} 在 runways.tsv 里没有配置文本（容量按代码的 {o.runway} 条跑道算）");
                }

                int published = 0;
                for (int b = 0; b < 4; b++)
                {
                    // 与卡片同源：ChipsOf 只吐「已公布且可持有」的刻，容量 0 的刻不出现。
                    var chips = IndustryAirlineLogic.ChipsOf(null, ap.code, b, out _, out _);
                    for (int i = 0; i < chips.Count; i++)
                    {
                        var c = chips[i];
                        published++;

                        var d = new ClockRow
                        {
                            code = ap.code,
                            city = ap.nameZh,
                            tier = ap.tier,
                            tierLabel = o.tierLabel,
                            runway = o.runway,
                            runwayCfg = o.runwayCfg,
                            runwaySrc = o.runwaySrc,
                            clock = c.time,
                            band = b,
                            depCap = c.capacity,
                            depHeld = c.held,
                            depMine = c.mineHeld,
                            depFree = c.freeLeft,
                            arrCap = c.arrCapacity,
                            arrHeld = c.arrHeldTotal,
                            arrFree = c.arrFreeLeft,
                            holders = c.holderLine,
                        };
                        detail.Add(d);

                        o.cap[b] += d.depCap;
                        o.dep[b] += d.depHeld;
                        o.arr[b] += d.arrHeld;
                        o.open[b] += d.depFree;
                    }
                }

                o.ticks = published;
                for (int b = 0; b < 4; b++)
                {
                    o.capTotal += o.cap[b];
                    o.depTotal += o.dep[b];
                    o.arrTotal += o.arr[b];
                    o.openTotal += o.open[b];
                }
                o.usagePct = o.capTotal > 0 ? (int)Math.Round(o.depTotal * 100.0 / o.capTotal) : 0;

                // 自检：逐刻容量之和必须等于该段的池容量（不等说明铺排与池脱节，报表就别信了）
                for (int b = 0; b < 4; b++)
                {
                    int pool = RoutePermitLogic.SlotPoolCapacity(ap.code, b);
                    if (o.cap[b] != pool)
                        Console.WriteLine($"[警告] {ap.code} 段{b} 逐刻容量合计 {o.cap[b]} ≠ 池容量 {pool}");
                }

                overview.Add(o);
            }

            overview.Sort((a, b) => a.tier != b.tier ? a.tier.CompareTo(b.tier) : string.CompareOrdinal(a.code, b.code));
            detail.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.code, b.code);
                if (c != 0) return c;
                return a.band != b.band ? a.band.CompareTo(b.band) : string.CompareOrdinal(a.clock, b.clock);
            });

            string stamp = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string pOverview = Path.Combine(outDir, "机场时刻容量与占用_总览_" + stamp + ".csv");
            string pDetail = Path.Combine(outDir, "机场时刻容量与占用_逐刻_" + stamp + ".csv");
            string pHtml = Path.Combine(outDir, "机场时刻容量与占用_" + stamp + ".html");

            File.WriteAllText(pOverview, CsvOverview(overview), new UTF8Encoding(true));
            File.WriteAllText(pDetail, CsvDetail(detail), new UTF8Encoding(true));
            File.WriteAllText(pHtml, Html(overview, detail, stamp), new UTF8Encoding(false));

            int capSum = 0, depSum = 0, arrSum = 0, openSum = 0;
            for (int i = 0; i < overview.Count; i++)
            {
                capSum += overview[i].capTotal;
                depSum += overview[i].depTotal;
                arrSum += overview[i].arrTotal;
                openSum += overview[i].openTotal;
            }

            Console.WriteLine($"协调机场 {overview.Count} 个 · 已公布时刻槽 {detail.Count} 条");
            Console.WriteLine($"离港池：容量 {capSum} · 已占 {depSum} · 可申请 {openSum}");
            Console.WriteLine($"到港池：容量 {capSum} · 已占 {arrSum} · 可申请 {capSum - arrSum}");
            Console.WriteLine("总览 " + Path.GetFullPath(pOverview));
            Console.WriteLine("逐刻 " + Path.GetFullPath(pDetail));
            Console.WriteLine("HTML " + Path.GetFullPath(pHtml));
            return 0;
        }

        // ==================== 跑道表 ====================

        sealed class Runway { public int count; public string cfg, src; }

        static Dictionary<string, Runway> LoadRunways()
        {
            var map = new Dictionary<string, Runway>(StringComparer.OrdinalIgnoreCase);
            string path = FindData("runways.tsv");
            if (path == null)
            {
                Console.WriteLine("[警告] 找不到 runways.tsv，跑道列将全部标注「缺」");
                return map;
            }
            foreach (var raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (raw[0] == '#') continue;
                var p = raw.Split('\t');
                if (p.Length < 4) continue;
                if (!int.TryParse(p[1].Trim(), out int n)) continue;
                map[p[0].Trim()] = new Runway { count = n, cfg = p[2].Trim(), src = p[3].Trim() };
            }
            return map;
        }

        static string FindData(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, name);
                if (File.Exists(candidate)) return candidate;
                var nested = Path.Combine(dir.FullName, "tools", "slotreport", name);
                if (File.Exists(nested)) return nested;
                dir = dir.Parent;
            }
            return File.Exists(name) ? name : null;
        }

        // ==================== CSV ====================

        static string CsvOverview(List<AirportRow> rows)
        {
            var sb = new StringBuilder();
            var head = new List<string>
            {
                "机场代码", "城市", "等级", "跑道数", "跑道配置", "跑道数据来源", "协调时段", "宵禁", "已公布刻数",
            };
            for (int b = 0; b < 4; b++)
            {
                head.Add(BandShort[b] + "_" + BandRange[b] + "_容量");
                head.Add(BandShort[b] + "_离港已占");
                head.Add(BandShort[b] + "_到港已占");
                head.Add(BandShort[b] + "_离港可申请");
            }
            head.Add("全天_单池容量");
            head.Add("全天_离港已占");
            head.Add("全天_到港已占");
            head.Add("全天_离港可申请");
            head.Add("离港池利用率%");
            sb.AppendLine(string.Join(",", Esc(head)));

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var cells = new List<string>
                {
                    r.code, r.city, r.tierLabel,
                    r.runway > 0 ? r.runway.ToString(CultureInfo.InvariantCulture) : "—",
                    r.runwayCfg, r.runwaySrc, r.coord, r.curfew,
                    r.ticks.ToString(CultureInfo.InvariantCulture),
                };
                for (int b = 0; b < 4; b++)
                {
                    cells.Add(r.cap[b].ToString(CultureInfo.InvariantCulture));
                    cells.Add(r.dep[b].ToString(CultureInfo.InvariantCulture));
                    cells.Add(r.arr[b].ToString(CultureInfo.InvariantCulture));
                    cells.Add(r.open[b].ToString(CultureInfo.InvariantCulture));
                }
                cells.Add(r.capTotal.ToString(CultureInfo.InvariantCulture));
                cells.Add(r.depTotal.ToString(CultureInfo.InvariantCulture));
                cells.Add(r.arrTotal.ToString(CultureInfo.InvariantCulture));
                cells.Add(r.openTotal.ToString(CultureInfo.InvariantCulture));
                cells.Add(r.usagePct.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(string.Join(",", Esc(cells)));
            }
            return sb.ToString();
        }

        static string CsvDetail(List<ClockRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Esc(new List<string>
            {
                "机场代码", "城市", "等级", "跑道数", "跑道配置", "时段", "时刻",
                "离港_容量", "离港_已占", "离港_其中我司", "离港_可申请",
                "到港_容量", "到港_已占", "到港_可申请", "离港持有方",
            })));

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                sb.AppendLine(string.Join(",", Esc(new List<string>
                {
                    r.code, r.city, r.tierLabel,
                    r.runway > 0 ? r.runway.ToString(CultureInfo.InvariantCulture) : "—",
                    r.runwayCfg,
                    BandShort[r.band] + " " + BandRange[r.band],
                    r.clock,
                    r.depCap.ToString(CultureInfo.InvariantCulture),
                    r.depHeld.ToString(CultureInfo.InvariantCulture),
                    r.depMine.ToString(CultureInfo.InvariantCulture),
                    r.depFree.ToString(CultureInfo.InvariantCulture),
                    r.arrCap.ToString(CultureInfo.InvariantCulture),
                    r.arrHeld.ToString(CultureInfo.InvariantCulture),
                    r.arrFree.ToString(CultureInfo.InvariantCulture),
                    r.holders ?? "",
                })));
            }
            return sb.ToString();
        }

        static List<string> Esc(List<string> cells)
        {
            var o = new List<string>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                string s = cells[i] ?? "";
                o.Add(s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
                    ? s
                    : "\"" + s.Replace("\"", "\"\"") + "\"");
            }
            return o;
        }

        // ==================== HTML ====================

        static string Html(List<AirportRow> rows, List<ClockRow> clocks, string stamp)
        {
            int capSum = 0, depSum = 0, arrSum = 0, openSum = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                capSum += rows[i].capTotal;
                depSum += rows[i].depTotal;
                arrSum += rows[i].arrTotal;
                openSum += rows[i].openTotal;
            }

            var sb = new StringBuilder(2 * 1024 * 1024);
            sb.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>机场时刻槽 · 逐刻容量与占用 · ").Append(stamp).Append("</title>");
            sb.Append(Style).Append("</head><body>");

            sb.Append("<h1>机场时刻槽 · 逐刻容量与占用</h1>");
            sb.Append("<p class=\"meta\">生成 ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
              .Append(" · 数据取自游戏源码 <code>IndustryAirlineLogic.ChipsOf</code>（与「机场时刻」卡片同一份铺排）")
              .Append(" · 我司持有按 0 条计</p>");

            sb.Append("<div class=\"cards\">");
            Card(sb, "协调机场", rows.Count.ToString(CultureInfo.InvariantCulture), "个", false);
            Card(sb, "已公布时刻槽", clocks.Count.ToString("N0", CultureInfo.InvariantCulture), "条", false);
            Card(sb, "离港池容量", capSum.ToString("N0", CultureInfo.InvariantCulture), "条", false);
            Card(sb, "离港已占", depSum.ToString("N0", CultureInfo.InvariantCulture), "条", false);
            Card(sb, "离港可申请", openSum.ToString("N0", CultureInfo.InvariantCulture), "条", true);
            Card(sb, "到港已占", arrSum.ToString("N0", CultureInfo.InvariantCulture), "条", false);
            Card(sb, "到港可申请", (capSum - arrSum).ToString("N0", CultureInfo.InvariantCulture), "条", true);
            sb.Append("</div>");

            sb.Append("<details class=\"note\"><summary>口径说明（先读这段）</summary><ul>");
            sb.Append("<li><b>一行 = 一个机场 × 一个公布的 15 分钟时刻</b>。全天共 96 个刻，容量 0 的刻（协调时段外 / 宵禁）不出现在表里 —— ")
              .Append("它们不是「被占满」，而是「这里本来就没有槽可持有」。所以每机场的行数（已公布刻数）会少于 96，汇总见总览表的「已公布刻」列。</li>");
            sb.Append("<li><b>早段含 00:00–05:45</b>：时段归属按代码的 <code>BandOfDep</code>（小时 &lt; 10 都归早段）。")
              .Append("★2026-11-09 起容量按跑道数平铺，只有宵禁机场（PEK / EEA / NRT 等）在这段为 0，其余机场 40 个早段刻全部公布。")
              .Append("游戏内卡片把早段文案写成「06:00–09:45」，与这批数据不符 —— 本表按真实归属写 00:00–09:45。</li>");
            sb.Append("<li><b>一条槽 = 一次停留</b>：占一次落地（到港池）+ 一次起飞（离港池）。两池容量同数，各算一端。</li>");
            sb.Append("<li><b>离港可申请</b> = 该刻容量 − 该刻已占，再与整段余量取小（池满时逐刻再空也拿不到）。")
              .Append("<b>到港可申请</b>同理。</li>");
            sb.Append("<li><b>跑道数就是每刻容量</b>（★2026-11-09 用户裁决）：协调时段内、且不在宵禁中的每一个 15 分钟刻，")
              .Append("放出「跑道数」条槽 —— 全天同一个数，**没有峰谷**。宵禁刻记 0（界面显示「不开放」）。")
              .Append("默认「跑道数」= 物理跑道条数；少数申报容量高于跑道数的机场（混合起降把同一组跑道用到极限）")
              .Append("记**等效跑道**，表内「跑道」列会标「· 等效」并在悬停提示里写明理由。")
              .Append("数据来自 <code>docs/handoff/2026-11-06_机场时刻容量对照表.md</code> 与运行时副本 ")
              .Append("<code>AirportRunways.cs</code>，列内标了来源：CAAC（官方）/ 公开评估 / 按跑道推算。</li>");
            sb.Append("<li>本表按「我司持有 0 条」出，与本局存档一致；购买了槽之后，游戏内卡片会多出我司那一条，本表不会自动变。</li>");
            sb.Append("</ul></details>");

            // —— 总览 ——
            sb.Append("<h2>一、机场总览（含跑道）</h2>");
            sb.Append("<div class=\"wrap\"><table id=\"ov\"><thead><tr>");
            sb.Append("<th rowspan=\"2\" class=\"sticky\">机场</th><th rowspan=\"2\">城市</th>")
              .Append("<th rowspan=\"2\">等级</th><th rowspan=\"2\" class=\"num\">跑道</th>")
              .Append("<th rowspan=\"2\">协调</th><th rowspan=\"2\" class=\"num\">已公布刻</th>");
            for (int b = 0; b < 4; b++)
                sb.Append("<th colspan=\"3\" class=\"band\">").Append(BandShort[b]).Append(" ").Append(BandRange[b]).Append("</th>");
            sb.Append("<th colspan=\"4\" class=\"band total\">全天合计（单池）</th>");
            sb.Append("</tr><tr>");
            for (int b = 0; b < 4; b++)
                sb.Append("<th class=\"sub\">容量</th><th class=\"sub\">离港占</th><th class=\"sub\">可申请</th>");
            sb.Append("<th class=\"sub total\">容量</th><th class=\"sub total\">离港占</th>")
              .Append("<th class=\"sub total\">可申请</th><th class=\"sub total\">利用率</th>");
            sb.Append("</tr></thead><tbody>");

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                sb.Append("<tr><td class=\"sticky code\">").Append(r.code).Append("</td>");
                sb.Append("<td class=\"city\">").Append(r.city).Append("</td>");
                sb.Append("<td><span class=\"tier t").Append(r.tier).Append("\">").Append(r.tierLabel).Append("</span></td>");
                sb.Append("<td class=\"num\"><span class=\"rwy\" title=\"").Append(Attr(r.runwayCfg))
                  .Append(" · 来源 ").Append(Attr(r.runwaySrc)).Append("\">")
                  .Append(r.runway > 0 ? r.runway.ToString(CultureInfo.InvariantCulture) : "—")
                  .Append("</span></td>");
                sb.Append("<td class=\"coord\">").Append(r.coord).Append("</td>");
                sb.Append("<td class=\"num\">").Append(r.ticks).Append("</td>");
                for (int b = 0; b < 4; b++)
                {
                    sb.Append("<td>").Append(r.cap[b]).Append("</td>");
                    sb.Append("<td>").Append(r.dep[b]).Append("</td>");
                    sb.Append("<td class=\"open\">").Append(r.open[b]).Append("</td>");
                }
                sb.Append("<td class=\"total\">").Append(r.capTotal).Append("</td>");
                sb.Append("<td class=\"total\">").Append(r.depTotal).Append("</td>");
                sb.Append("<td class=\"total open\">").Append(r.openTotal).Append("</td>");
                sb.Append("<td class=\"total\"><div class=\"bar\"><i style=\"width:").Append(r.usagePct).Append("%\"></i></div><span>")
                  .Append(r.usagePct).Append("%</span></td></tr>");
            }

            sb.Append("<tr class=\"sum\"><td class=\"sticky code\">合计</td><td class=\"city\">").Append(rows.Count)
              .Append(" 个机场</td><td></td><td class=\"num\">—</td><td></td><td class=\"num\">").Append(clocks.Count).Append("</td>");
            for (int b = 0; b < 4; b++)
            {
                int c = 0, d = 0, o = 0;
                for (int i = 0; i < rows.Count; i++) { c += rows[i].cap[b]; d += rows[i].dep[b]; o += rows[i].open[b]; }
                sb.Append("<td>").Append(c).Append("</td><td>").Append(d).Append("</td><td>").Append(o).Append("</td>");
            }
            sb.Append("<td>").Append(capSum).Append("</td><td>").Append(depSum).Append("</td><td>").Append(openSum).Append("</td>");
            sb.Append("<td>").Append(capSum > 0 ? Math.Round(depSum * 100.0 / capSum) + "%" : "—").Append("</td></tr>");
            sb.Append("</tbody></table></div>");
            sb.Append("<p class=\"hint\">鼠标停在「跑道」上可看跑道配置与数据来源。</p>");

            // —— 逐刻明细 ——
            sb.Append("<h2>二、逐刻明细（每机场 × 每个公布的 15 分钟时刻）</h2>");
            sb.Append("<div class=\"filters\">");
            sb.Append("<input id=\"q\" type=\"search\" placeholder=\"过滤：机场代码 / 城市 / 时刻（如 PVG 或 07:30）\">");
            sb.Append("<select id=\"band\"><option value=\"\">全部时段</option>");
            for (int b = 0; b < 4; b++)
                sb.Append("<option value=\"").Append(b).Append("\">").Append(BandShort[b]).Append(" ").Append(BandRange[b]).Append("</option>");
            sb.Append("</select>");
            sb.Append("<label class=\"chk\"><input id=\"onlyOpen\" type=\"checkbox\"> 只看离港还可申请</label>");
            sb.Append("<span id=\"count\" class=\"cnt\"></span>");
            sb.Append("</div>");

            sb.Append("<div class=\"wrap tall\"><table id=\"dt\"><thead><tr>");
            sb.Append("<th rowspan=\"2\" class=\"sticky\">机场</th><th rowspan=\"2\" class=\"sticky s2\">跑道</th>")
              .Append("<th rowspan=\"2\" class=\"sub\">#</th>")
              .Append("<th rowspan=\"2\">时刻</th><th rowspan=\"2\">时段</th>")
              .Append("<th colspan=\"3\" class=\"grp\">离港池（起飞）</th>")
              .Append("<th colspan=\"3\" class=\"grp arr\">到港池（落地）</th>")
              .Append("<th rowspan=\"2\">离港持有方</th>");
            sb.Append("</tr><tr>");
            sb.Append("<th class=\"sub\">容量</th><th class=\"sub\">已占</th><th class=\"sub\">可申请</th>")
              .Append("<th class=\"sub arr\">容量</th><th class=\"sub arr\">已占</th><th class=\"sub arr\">可申请</th>");
            sb.Append("</tr></thead><tbody>");

            for (int i = 0; i < clocks.Count; i++)
            {
                var r = clocks[i];
                sb.Append("<tr data-b=\"").Append(r.band).Append("\" data-o=\"")
                  .Append(r.depFree > 0 ? 1 : 0)
                  .Append("\" data-k=\"").Append(Attr((r.code + " " + r.city + " " + r.clock + " " + BandShort[r.band]).ToLowerInvariant()))
                  .Append("\">");
                sb.Append("<td class=\"sticky code\">").Append(r.code).Append("</td>");
                sb.Append("<td class=\"sticky s2 num\">").Append(r.runway > 0 ? r.runway.ToString(CultureInfo.InvariantCulture) : "—").Append("</td>");
                sb.Append("<td class=\"num\">").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("</td>");
                sb.Append("<td class=\"num clock\">").Append(r.clock).Append("</td>");
                sb.Append("<td class=\"bandc\">").Append(BandShort[r.band]).Append("</td>");
                sb.Append("<td class=\"num\">").Append(r.depCap).Append("</td>");
                sb.Append("<td class=\"num").Append(r.depMine > 0 ? " mine" : "").Append("\">").Append(r.depHeld).Append("</td>");
                sb.Append("<td class=\"num open").Append(r.depFree > 0 ? "" : " low").Append("\">").Append(r.depFree).Append("</td>");
                sb.Append("<td class=\"num arr\">").Append(r.arrCap).Append("</td>");
                sb.Append("<td class=\"num arr\">").Append(r.arrHeld).Append("</td>");
                sb.Append("<td class=\"num arr open").Append(r.arrFree > 0 ? "" : " low").Append("\">").Append(r.arrFree).Append("</td>");
                sb.Append("<td class=\"holders\">").Append(Enc(r.holders)).Append("</td>");
                sb.Append("</tr>");
            }

            sb.Append("</tbody></table></div>");
            sb.Append("<p class=\"hint\">同目录下两份 CSV：<code>_总览_</code>（每机场一行，含跑道）与 <code>_逐刻_</code>（每机场每时刻一行）。Excel 可直接打开。</p>");
            sb.Append(Script);
            sb.Append("</body></html>");
            return sb.ToString();
        }

        static void Card(StringBuilder sb, string label, string value, string unit, bool green)
        {
            sb.Append("<div class=\"card").Append(green ? " green" : "").Append("\"><span>").Append(label)
              .Append("</span><b>").Append(value).Append("</b><i>").Append(unit).Append("</i></div>");
        }

        static string Attr(string s) => Enc(s).Replace("\"", "&quot;");
        static string Enc(string s) => string.IsNullOrEmpty(s) ? ""
            : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        const string Style = @"
<style>
:root{--bg:#0d1b2f;--panel:#142943;--line:#22364f;--fg:#dbe6f4;--mute:#8aa3bf;--green:#4ce39e;--red:#e4a0a7;--blue:#4a86e8}
*{box-sizing:border-box}
body{margin:0;padding:26px 30px 64px;background:var(--bg);color:var(--fg);
  font:13px/1.5 -apple-system,'Segoe UI','Microsoft YaHei',sans-serif}
h1{font-size:20px;margin:0 0 6px;letter-spacing:.3px}
h2{font-size:15px;margin:30px 0 10px;color:#cfe0f5}
.meta{color:var(--mute);font-size:12px;margin:0 0 18px}
code{background:#1b3350;padding:1px 5px;border-radius:3px;font-size:12px;color:#a8c8ea}
.cards{display:flex;gap:10px;flex-wrap:wrap;margin-bottom:16px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:9px 15px;min-width:118px}
.card span{display:block;color:var(--mute);font-size:11px;margin-bottom:2px}
.card b{font-size:19px;font-weight:600}
.card i{font-style:normal;color:var(--mute);font-size:11px;margin-left:3px}
.card.green b{color:var(--green)}
.note{background:#122238;border:1px solid var(--line);border-radius:8px;padding:9px 14px;margin-bottom:14px}
.note summary{cursor:pointer;color:#a8c8ea;font-size:12.5px}
.note ul{margin:9px 0 2px;padding-left:20px;color:#b7c9dd;font-size:12.5px}
.note li{margin:4px 0}
.note b{color:#e6eefa}
.hint{color:var(--mute);font-size:11.5px;margin:6px 0 0}
.filters{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin-bottom:8px}
.filters input[type=search]{background:#142943;border:1px solid var(--line);border-radius:6px;
  color:var(--fg);padding:6px 10px;font-size:12.5px;width:340px;outline:none}
.filters input[type=search]:focus{border-color:#2f5f9c}
.filters select{background:#142943;border:1px solid var(--line);border-radius:6px;color:var(--fg);
  padding:6px 8px;font-size:12.5px}
.chk{color:#b7c9dd;font-size:12.5px;display:flex;align-items:center;gap:5px;cursor:pointer}
.cnt{color:var(--mute);font-size:11.5px;margin-left:auto}
.wrap{overflow:auto;max-height:66vh;border:1px solid var(--line);border-radius:8px}
.wrap.tall{max-height:72vh}
table{border-collapse:separate;border-spacing:0;width:100%;font-variant-numeric:tabular-nums}
thead th{position:sticky;top:0;z-index:2;background:#192f50;color:#cddff4;font-weight:600;font-size:11.5px;
  padding:6px 8px;border-bottom:1px solid var(--line);white-space:nowrap}
thead tr:nth-child(2) th{top:31px}
th.sub{font-weight:500;color:#9fb8d4;font-size:11px;background:#152840}
th.band{background:#1e3a5f;color:#dbe9fb}
th.grp{background:#1b3455}
th.grp.arr{background:#1a3a44;color:#bfe6e0}
tbody td{padding:4px 8px;border-bottom:1px solid #1a2c44;text-align:right;white-space:nowrap}
tbody td.city,tbody td.coord,tbody td.holders,tbody td.bandc{text-align:left;color:#c3d6ea}
tbody td.holders{font-size:11.5px;color:#9fb8d4;max-width:320px;overflow:hidden;text-overflow:ellipsis}
tbody tr:nth-child(even) td{background:#111f33}
tbody tr:hover td{background:#1a3049}
.sticky{position:sticky;left:0;z-index:1;background:#142943!important}
.sticky.s2{left:52px}
thead .sticky{z-index:3;background:#192f50!important}
td.code{text-align:left;font-weight:600;color:#e6eefa;letter-spacing:.4px;border-right:1px solid var(--line);width:52px}
td.num{text-align:right}
td.clock{color:#dbe9fb;font-weight:600}
td.open{color:var(--green)}
td.open.low{color:#e4a0a7}
td.total{background:#16293f!important;font-weight:600}
tr.sum td{background:#1b3455!important;font-weight:700;border-top:2px solid #2d4a6d}
.tier{font-size:10.5px;padding:1px 6px;border-radius:3px;background:#1d3a5c;color:#a9c6e6}
.tier.t0{background:#214a78;color:#cfe4ff}
.rwy{border-bottom:1px dashed #4a6f96;cursor:help}
.bar{display:inline-block;vertical-align:middle;width:44px;height:6px;border-radius:3px;background:#22364f;margin-right:6px;overflow:hidden}
.bar i{display:block;height:100%;background:linear-gradient(90deg,#2f6fd0,#4a86e8)}
</style>";

        const string Script = @"
<script>
(function(){
  var q = document.getElementById('q');
  var band = document.getElementById('band');
  var onlyOpen = document.getElementById('onlyOpen');
  var cnt = document.getElementById('count');
  var rows = Array.prototype.slice.call(document.querySelectorAll('#dt tbody tr'));
  function apply(){
    var s = (q.value || '').trim().toLowerCase();
    var b = band.value;
    var open = onlyOpen.checked;
    var shown = 0;
    for (var i = 0; i < rows.length; i++){
      var r = rows[i];
      var ok = true;
      if (s && r.dataset.k.indexOf(s) < 0) ok = false;
      if (ok && b !== '' && r.dataset.b !== b) ok = false;
      if (ok && open && r.dataset.o !== '1') ok = false;
      r.style.display = ok ? '' : 'none';
      if (ok) shown++;
    }
    cnt.textContent = '显示 ' + shown + ' / ' + rows.length + ' 行';
  }
  q.addEventListener('input', apply);
  band.addEventListener('change', apply);
  onlyOpen.addEventListener('change', apply);
  apply();
})();
</script>";
    }
}
