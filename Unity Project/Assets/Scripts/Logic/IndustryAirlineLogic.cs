using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 行业情报：他航持有哪些航权、各机场的时刻被谁占着。
    ///
    /// ★2026-11-05（用户要求）：n77 加两张表 ——
    /// ① 所有航司的航权；② 所有机场的时刻及持有航司（空闲不标，即可申请）。
    ///
    /// 为什么他航的持仓是**推导**出来的而不是存盘状态：
    /// <list type="bullet">
    /// <item>存档里只有我司一家。给 38 家他航建持仓表，等于凭空多出一份要随季度演进、
    ///   要处理旧档迁移、还要防止与「时刻池容量」打架的账 —— 收益只是让表格好看一点。</item>
    /// <item>推导可以做到**确定性**：同一存档、同一季度、同一机场，看到的是同一张表。
    ///   用 <c>System.Random</c> 每帧重摇会让 CEO 以为这机场一刷新就换了一批航司。</item>
    /// </list>
    ///
    /// 关键约束（否则表格与玩法会互相打脸）：
    /// <b>池容量 = 我司持仓 + 他航持仓 + 空闲</b>，三者永不越界，
    /// 且 <see cref="OpenSlots"/> 与 <c>RoutePermitLogic.SlotsAvailable</c> 是同一个数。
    /// 也就是说表上写着「空闲 3」，游戏里就确实还能申请 3 条 —— 这是这张表唯一的价值。
    /// </summary>
    public static class IndustryAirlineLogic
    {
        /// <summary>他航最少给新进入者留几条空槽：池子再挤，也必须留得下 1 条。</summary>
        public const int MustLeaveOpen = 1;

        // ————————————————— 确定性哈希 —————————————————

        /// <summary>FNV-1a 32 位。**不能用 string.GetHashCode** —— 它跨进程不保证稳定，
        /// 会让同一张表在重启后变样。</summary>
        public static uint Hash(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (string.IsNullOrEmpty(s)) return h;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619u;
                }
                return h;
            }
        }

        /// <summary>把一组键混成一个 [0,1) 的确定性随机数。</summary>
        public static float Unit(params string[] parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(parts[i]);
            }
            return (Hash(sb.ToString()) % 10000u) / 10000f;
        }

        // ————————————————— 时刻占用 —————————————————

        /// <summary>该航司是否在本机场经营（基地必然经营；其余按档位与同国关系给概率）。</summary>
        public static bool Participates(IndustryAirline a, string airport)
        {
            if (a == null || string.IsNullOrEmpty(airport)) return false;
            if (string.Equals(a.hub, airport, StringComparison.OrdinalIgnoreCase)) return true;
            float p = a.tier == 0 ? 0.62f : a.tier == 1 ? 0.34f : 0.16f;
            string country = AirportRegions.CountryOf(airport);
            if (!string.IsNullOrEmpty(country) && string.Equals(country, a.region, StringComparison.Ordinal))
                p += 0.30f;   // 本国航司遍布国内航点
            return Unit(a.code, airport, "part") < p;
        }

        /// <summary>本机场本时段**他航**占用的槽数（已扣掉我司持仓，且保证不把池子占满）。</summary>
        public static int IndustryHeld(GameState gs, string airport, int band)
        {
            int cap = RoutePermitLogic.SlotPoolCapacity(airport, band);
            if (cap <= 0) return 0;
            int ours = RoutePermitLogic.SlotsHeld(gs, airport, band);
            int room = cap - ours;
            if (room <= 0) return 0;

            // 占用率 45%~80%，枢纽再挤一点 —— 现实里协调机场的高峰时段几乎全满，
            // 但游戏必须始终留一条缝，否则「开航缺槽」会变成死结。
            float rate = 0.45f + 0.35f * Unit(airport, band.ToString(), "occ");
            if (AirportCatalog.TierOfCode(airport) == 0) rate += 0.05f;
            int want = (int)Math.Floor(cap * rate);

            int hardCap = cap - MustLeaveOpen;
            if (hardCap < 0) hardCap = 0;
            if (want > hardCap) want = hardCap;
            if (want > room) want = room;          // 我司买多了，他航只能让位
            return want < 0 ? 0 : want;
        }

        /// <summary>本时段还能申请几条（= 池容量 − 我司 − 他航）。与玩法口径一致。</summary>
        public static int OpenSlots(GameState gs, string airport, int band)
        {
            int cap = RoutePermitLogic.SlotPoolCapacity(airport, band);
            if (cap <= 0) return 0;
            int left = cap - RoutePermitLogic.SlotsHeld(gs, airport, band) - IndustryHeld(gs, airport, band);
            return left < 0 ? 0 : left;
        }

        /// <summary>
        /// 本机场本时段**逐个槽**的持有者：长度 = 池容量，元素为空串表示空闲（可申请）。
        /// 顺序是「我司在前、他航在后、空闲垫底」—— 这样表格里我司的槽总在同一侧，好找。
        /// </summary>
        public static List<string> SlotHolders(GameState gs, string airport, int band)
        {
            int cap = RoutePermitLogic.SlotPoolCapacity(airport, band);
            var list = new List<string>(cap);
            if (cap <= 0) return list;

            int ours = RoutePermitLogic.SlotsHeld(gs, airport, band);
            if (ours > cap) ours = cap;
            for (int i = 0; i < ours; i++) list.Add("我司");

            int industry = IndustryHeld(gs, airport, band);
            for (int k = 0; k < industry; k++) list.Add(PickAirline(airport, band, k));

            while (list.Count < cap) list.Add("");
            return list;
        }

        /// <summary>按权重确定性挑一家他航填第 <paramref name="k"/> 个槽。</summary>
        static string PickAirline(string airport, int band, int k)
        {
            var cands = new List<IndustryAirline>();
            var weights = new List<float>();
            float total = 0f;
            for (int i = 0; i < IndustryAirlines.All.Length; i++)
            {
                var a = IndustryAirlines.All[i];
                if (!Participates(a, airport)) continue;
                float w = a.tier == 0 ? 3f : a.tier == 1 ? 2f : 1f;
                if (string.Equals(a.hub, airport, StringComparison.OrdinalIgnoreCase)) w += 5f;
                string country = AirportRegions.CountryOf(airport);
                if (!string.IsNullOrEmpty(country) && string.Equals(country, a.region, StringComparison.Ordinal))
                    w += 1f;
                w += Unit(a.code, airport, "w");       // 轻微抖动，免得份额永远是整齐的整数
                cands.Add(a);
                weights.Add(w);
                total += w;
            }
            if (cands.Count == 0)
            {
                // 该机场没有任何他航「经营」，但槽仍被点掉了 —— 不可能，放宽到全名单兜底。
                var any = IndustryAirlines.All[(int)(Hash(airport + "|" + band) % (uint)IndustryAirlines.All.Length)];
                return any != null ? any.Display : "他航";
            }

            float r = Unit(airport, band.ToString(), k.ToString(), "slot") * total;
            for (int i = 0; i < cands.Count; i++)
            {
                r -= weights[i];
                if (r <= 0f) return cands[i].Display;
            }
            return cands[cands.Count - 1].Display;
        }

        // ————————————————— 航权清单 —————————————————

        /// <summary>
        /// 他航持有的航权条目（`"PEK—LHR"`, 自由度, 备注）。
        ///
        /// 口径与现实对齐：从基地出发的对外航点即第三/第四航权（本国 ↔ 他国），
        /// 全球网络型航司另有若干**第五航权**（他国 ↔ 他国，须接在基地始发航段之后，
        /// 如 EK 的 SIN—MEL 段）。自由度一律交给 <see cref="AirportRegions.FreedomOf"/> 判，
        /// 不在这里另写一套 —— 免得表格说「第五航权」、提案信里说「第三/第四」。
        /// </summary>
        public static List<string[]> RightsOf(IndustryAirline a)
        {
            var list = new List<string[]>();
            if (a == null || string.IsNullOrEmpty(a.hub)) return list;

            var codes = new List<string>();
            var airports = AirportCatalog.Airports;
            for (int i = 0; i < airports.Count; i++)
            {
                string c = airports[i].code;
                if (string.IsNullOrEmpty(c)) continue;
                if (string.Equals(c, a.hub, StringComparison.OrdinalIgnoreCase)) continue;
                codes.Add(c);
            }
            if (codes.Count == 0) return list;

            // 航点按下表加权抽取（★2026-11-05 二次调整）：
            // 首版是均匀抽，结果英航会开「LHR—MFM（澳门）」、达美会开「DFW—SHA」——
            // 推导逻辑没错，是**分布不像航司网络**。现实里全球承运人的航点集中在枢纽与干线，
            // 支线只出现在本国（做国内摆渡）。故给等级加权、同国加权，支线权重压到最低。
            var weights = new float[codes.Count];
            float totalW = 0f;
            for (int i = 0; i < codes.Count; i++)
            {
                int tier = AirportCatalog.TierOfCode(codes[i]);
                float w = tier == 0 ? 6f : tier == 1 ? 4f : 1f;
                string country = AirportRegions.CountryOf(codes[i]);
                if (!string.IsNullOrEmpty(country) && string.Equals(country, a.region, StringComparison.Ordinal))
                    w += 4f;
                weights[i] = w;
                totalW += w;
            }

            int want = 4 + (int)(Hash(a.code + "|n") % 7u);   // 4~10 条
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int k = 0; k < codes.Count && list.Count < want; k++)
            {
                string dest = PickWeighted(codes, weights, totalW, a.code + "|r" + k);
                if (string.IsNullOrEmpty(dest)) continue;
                string key = a.hub + "-" + dest;
                if (!used.Add(key)) continue;
                int fd = AirportRegions.FreedomOf(a.region, a.hub, dest);
                if (fd <= 0) continue;                          // 国内航段不是航权
                list.Add(new[] { $"{a.hub}—{dest}", AirportRegions.FreedomLabel(fd), Status(a, dest) });
            }

            // 全球网络型补 1~2 条第五航权（他国 ↔ 他国），这是现实里最能体现「网络型」的一档
            if (a.tier == 0)
            {
                int extra = 1 + (int)(Hash(a.code + "|f") % 2u);
                for (int k = 0; k < codes.Count && extra > 0; k++)
                {
                    string o = PickWeighted(codes, weights, totalW, a.code + "|f1" + k);
                    string d = PickWeighted(codes, weights, totalW, a.code + "|f2" + k);
                    if (string.IsNullOrEmpty(o) || string.IsNullOrEmpty(d)) continue;
                    if (string.Equals(o, d, StringComparison.Ordinal)) continue;
                    int fd = AirportRegions.FreedomOf(a.region, o, d);
                    if (fd != 5) continue;
                    if (!used.Add(o + "-" + d)) continue;
                    list.Add(new[] { $"{o}—{d}", AirportRegions.FreedomLabel(fd), Status(a, d) });
                    extra--;
                }
            }
            return list;
        }

        /// <summary>按权重抽一个航点：用确定性随机数落在累计权重上，同一键永远同一结果。</summary>
        static string PickWeighted(List<string> codes, float[] weights, float totalW, string salt)
        {
            if (codes == null || codes.Count == 0 || totalW <= 0f) return "";
            float r = Unit(salt, "dst") * totalW;
            for (int i = 0; i < codes.Count; i++)
            {
                r -= weights[i];
                if (r <= 0f) return codes[i];
            }
            return codes[codes.Count - 1];
        }

        static string Status(IndustryAirline a, string dest)
            => Unit(a.code, dest, "st") < 0.72f ? "使用中" : "储备";

        // ————————————————— 卡片视图的数据（2026-11-05 二次改版） —————————————————
        //
        // 首版把整张表拼成一段长文本塞进滚动区，用户反馈「太乱了」——
        // 一段 700 行的等宽文字里，机场、时段、航司名混在一起，眼睛没有落点。
        // 改版按参考稿走**卡片 + 槽位块**：一个机场一张卡，卡内四个时段各一行，
        // 每个时刻一个小方块（我司蓝 / 空闲绿 / 他航灰）。数据侧因此从「拼字符串」
        // 换成「给结构」，排版交给 UI。

        /// <summary>一个时刻的槽位状态（卡片里的一个小方块）。</summary>
        public struct SlotChip
        {
            /// <summary>系列时刻 `"07:30"`。</summary>
            public string time;
            /// <summary>持有者。「我司」/ 航司名 / 空串（空闲）。</summary>
            public string owner;
            /// <summary>该时刻的系列数（≥1）；&gt;1 说明这一时刻有多个系列。</summary>
            public int series;
            /// <summary>同一时刻除首个持有者外的**其他家数**（用于「+1」后缀）。</summary>
            public int others;
            /// <summary>我司持有。</summary>
            public bool mine;
            /// <summary>无人持有**且本段还有可申请余量** —— 绿块，点了能申请。</summary>
            public bool free;
            /// <summary>
            /// 该刻无人持有、但本段**已经排满**（池容量小于该时段的时刻点数时会出现，
            /// 如干线机场夜班 4 槽 / 7 个时刻）。
            ///
            /// 这种块不能标成绿色「可申请」—— 段里一条余量都没有，标绿就是骗人；
            /// 也不能留白 —— 留白会被读成「渲染漏了」。故单列一态，画成灰「—」。
            /// </summary>
            public bool vacant;
        }

        /// <summary>机场的池况合计（卡片头部那一行）。</summary>
        public struct AirportTotals
        {
            public int cap;      // 池容量（四时段合计）
            public int own;      // 我司持有
            public int other;    // 他航持有
            public int open;     // 可申请余量
            public int ownBands; // 我司有持仓的时段数
        }

        public static AirportTotals TotalsOf(GameState gs, string airport)
        {
            var t = new AirportTotals();
            for (int b = 0; b < 4; b++)
            {
                int cap = RoutePermitLogic.SlotPoolCapacity(airport, b);
                int own = RoutePermitLogic.SlotsHeld(gs, airport, b);
                int other = IndustryHeld(gs, airport, b);
                t.cap += cap;
                t.own += own;
                t.other += other;
                if (own > 0) t.ownBands++;
            }
            t.open = t.cap - t.own - t.other;
            if (t.open < 0) t.open = 0;
            return t;
        }

        /// <summary>
        /// 某机场某时段的**逐个系列时刻**：按网格顺序给出每一个时刻的方块。
        ///
        /// 两个建模决定，都是被回归测试逼出来的：
        ///
        /// ① 以「网格时刻」而不是「槽」为单位。池容量常大于网格点数（枢纽晚班 14 槽 / 8 个点），
        ///    按槽铺方块会出现几块一模一样的「18:00」，看起来像重复渲染；以时刻为单位、
        ///    用「×N」标系列数，才是协调人公布时刻表的样子。
        ///
        /// ② 占用**均匀铺开到整个时段**，不是从时段开头往后堆。
        ///    首版按 `k % grid.Count` 顺序填，结果容量 14 的时段把前 8 个时刻全占满、
        ///    后 6 条槽无处分摊 —— 界面上表现为「整段全满」而旁边写着「余 6」，
        ///    正是 CEO 最不能接受的自相矛盾。改成按 `k * grid / occupied` 均匀映射后，
        ///    占用多于时刻时每个时刻都有多家（正常），少于时刻时空缺**散落**在整段里。
        /// </summary>
        public static List<SlotChip> ChipsOf(GameState gs, string airport, int band)
        {
            var chips = new List<SlotChip>();
            var grid = ScheduleSlots.SlotGridLabels(band);
            if (grid.Count == 0) return chips;

            var holders = SlotHolders(gs, airport, band);
            var names = new List<string>(holders.Count);
            for (int i = 0; i < holders.Count; i++)
                if (!string.IsNullOrEmpty(holders[i])) names.Add(holders[i]);

            var perMoment = new List<List<string>>(grid.Count);
            for (int i = 0; i < grid.Count; i++) perMoment.Add(new List<string>(2));

            int occupied = names.Count;
            for (int k = 0; k < occupied; k++)
            {
                int m = occupied <= grid.Count
                    ? (int)((long)k * grid.Count / occupied)   // 均匀散开，留下的空位彼此隔开
                    : k % grid.Count;                          // 比时刻多：每个时刻都有多家
                if (m >= grid.Count) m = grid.Count - 1;
                perMoment[m].Add(names[k]);
            }

            int open = OpenSlots(gs, airport, band);
            for (int i = 0; i < grid.Count; i++)
            {
                var list = perMoment[i];
                var c = new SlotChip { time = grid[i], series = list.Count };
                if (list.Count == 0)
                {
                    c.free = open > 0;
                    c.vacant = !c.free;
                    c.series = 0;
                    chips.Add(c);
                    continue;
                }

                // 「我司」优先当代表：CEO 扫一眼先看到自己的槽（蓝块），再看别人的。
                int mineIdx = list.IndexOf("我司");
                c.mine = mineIdx >= 0;
                c.owner = c.mine ? "我司" : list[0];

                int distinct = 0;
                for (int k = 0; k < list.Count; k++)
                {
                    bool dup = false;
                    for (int j = 0; j < k; j++)
                        if (string.Equals(list[j], list[k], StringComparison.Ordinal)) { dup = true; break; }
                    if (!dup) distinct++;
                }
                c.others = distinct - 1;
                chips.Add(c);
            }
            return chips;
        }

        // ———— 过滤 / 排序（工具栏） ————

        public const int FilterAll = 0;
        public const int FilterMine = 1;
        public const int FilterHub = 2;
        public const int FilterFull = 3;

        public static readonly string[] FilterLabels = { "全部机场", "我司航网", "只看枢纽", "只看满池" };

        /// <summary>该机场是否属我司航网（有持仓，或是我司在飞/待飞航线的端点）。</summary>
        public static bool InMyNetwork(GameState gs, string airport)
        {
            if (gs == null || string.IsNullOrEmpty(airport)) return false;
            for (int b = 0; b < 4; b++)
                if (RoutePermitLogic.SlotsHeld(gs, airport, b) > 0) return true;
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    if (string.Equals(r.origin, airport, StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(r.destination, airport, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 工具栏过滤 + 搜索 + 排序后的协调机场清单。
        ///
        /// 排序默认是「我司有持仓 → 枢纽 → 干线 → 代码」：CEO 最常看自己飞的地方；
        /// <paramref name="sortByOpen"/> 为真时改按可申请余量降序 ——
        /// 想找哪里还进得去时，这是唯一有用的顺序。
        /// </summary>
        public static List<AirportInfo> FilteredAirports(GameState gs, int filter, bool sortByOpen, string query)
        {
            var rows = new List<AirportInfo>();
            var airports = AirportCatalog.Airports;
            string q = string.IsNullOrEmpty(query) ? "" : query.Trim().ToUpperInvariant();

            for (int i = 0; i < airports.Count; i++)
            {
                var ap = airports[i];
                if (!ap.coordinated) continue;
                if (q.Length > 0)
                {
                    bool hit = (ap.code != null && ap.code.ToUpperInvariant().Contains(q))
                               || (ap.nameZh != null && ap.nameZh.ToUpperInvariant().Contains(q))
                               || (ap.nameEn != null && ap.nameEn.ToUpperInvariant().Contains(q));
                    if (!hit) continue;
                }
                var t = TotalsOf(gs, ap.code);
                if (filter == FilterMine && !InMyNetwork(gs, ap.code)) continue;
                if (filter == FilterHub && ap.tier != 0) continue;
                if (filter == FilterFull && t.open > 0) continue;
                rows.Add(ap);
            }

            rows.Sort((x, y) =>
            {
                if (sortByOpen)
                {
                    int ox = TotalsOf(gs, x.code).open, oy = TotalsOf(gs, y.code).open;
                    if (ox != oy) return oy.CompareTo(ox);
                }
                else
                {
                    int ox = TotalsOf(gs, x.code).own, oy = TotalsOf(gs, y.code).own;
                    if (ox != oy) return oy.CompareTo(ox);
                }
                if (x.tier != y.tier) return x.tier.CompareTo(y.tier);
                return string.CompareOrdinal(x.code, y.code);
            });
            return rows;
        }


        /// <summary>
        /// 全行业航权表：我司在最前（用真实持仓），其后按档位排他航。
        /// 他航的条目是推导值 —— 表头写明「他航为行业口径」，不要把推导值当成存档事实。
        /// </summary>
        public static string RightsTableText(GameState gs)
        {
            var sb = new StringBuilder();
            var stg = StrategyManager.EnsureState(gs);

            int mine = stg?.rights != null ? stg.rights.Count : 0;
            // ★2026-11-05：本国取**基地机场所在国**，不再硬写「中国」——
            // 接管他国航司时（如基地在巴黎），硬写会让整张表的航权口径从第一行就错。
            string home = gs != null ? AirportRegions.CountryOf(gs.hubIata) : "";
            if (string.IsNullOrEmpty(home)) home = "—";
            sb.Append("我司 ").Append(gs != null ? gs.companyNameZh : "我司")
              .Append("（").Append(gs != null ? gs.hubIata : "—").Append("）· ").Append(home)
              .Append(" · 已入账 ").Append(mine).Append(" 条");
            if (mine == 0)
            {
                sb.Append("\n   暂无入账航权 —— 准入办结并交割后计入此处。");
            }
            else
            {
                for (int i = 0; i < mine; i++)
                {
                    var r = stg.rights[i];
                    sb.Append("\n   ").Append(r.origin).Append('—').Append(r.destination)
                      .Append("　").Append(AirportRegions.FreedomLabel(r.freedom))
                      .Append("　").Append(StatusLabel(r.status));
                }
            }

            var order = new List<IndustryAirline>(IndustryAirlines.All);
            order.Sort((x, y) =>
            {
                if (x.tier != y.tier) return x.tier.CompareTo(y.tier);
                return string.CompareOrdinal(x.code, y.code);
            });

            for (int i = 0; i < order.Count; i++)
            {
                var a = order[i];
                var rights = RightsOf(a);
                sb.Append("\n\n").Append(a.name).Append("（").Append(a.code).Append('）')
                  .Append(" · 基地 ").Append(a.hub)
                  .Append(" · ").Append(IndustryAirline.TierLabel(a.tier))
                  .Append(" · ").Append(rights.Count).Append(" 条");
                for (int k = 0; k < rights.Count; k++)
                {
                    sb.Append("\n   ").Append(rights[k][0])
                      .Append("　").Append(rights[k][1])
                      .Append("　").Append(rights[k][2]);
                }
            }

            sb.Append("\n\n注：他航条目为行业口径（按航司档位与基地推导），代表「这一对航点已有承运人在飞」，"
                      + "不代表我司不能申请 —— 双边航权额度对多家承运人开放，我司能否取得看我司与对方民航当局的谈判。");
            return sb.ToString();
        }

        static string StatusLabel(string status)
        {
            if (string.IsNullOrEmpty(status)) return "—";
            return status switch
            {
                "approved" => "已入账",
                "negotiating" => "洽谈中",
                "denied" => "未获批",
                _ => status
            };
        }

        /// <summary>表头统计：我司航权 / 他航航权 / 协调机场 / 我司时刻 / 可申请余量。</summary>
        public static string SlotsSummary(GameState gs)
        {
            int airports = 0, own = 0, open = 0, other = 0;
            var list = AirportCatalog.Airports;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].coordinated) continue;
                airports++;
                var t = TotalsOf(gs, list[i].code);
                own += t.own;
                other += t.other;
                open += t.open;
            }
            return $"协调机场 {airports} 个 · 我司持有 {own} 条 · 他航占用 {other} 条 · 可申请余量 {open} 条";
        }

        /// <summary>表头统计：航司家数 / 航权条目总数。</summary>
        public static string RightsSummary(GameState gs)
        {
            var stg = StrategyManager.EnsureState(gs);
            int mine = stg?.rights != null ? stg.rights.Count : 0;
            int total = 0;
            for (int i = 0; i < IndustryAirlines.All.Length; i++)
                total += RightsOf(IndustryAirlines.All[i]).Count;
            return $"{IndustryAirlines.All.Length + 1} 家航司 · 我司 {mine} 条 · 他航 {total} 条";
        }
    }
}
