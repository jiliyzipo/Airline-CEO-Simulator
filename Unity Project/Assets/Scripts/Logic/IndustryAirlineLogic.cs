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
            // ★2026-11-07 性能：原来用 StringBuilder 拼出完整字符串再 Hash ——
            // 每次调用 1 个数组 + 1 个 StringBuilder + 1 个 string。这个函数在
            // 「机场时刻」页每次刷新要被调几十万次，仅分配就吃掉几秒。
            // 改为**增量 FNV**：对「parts 用 '|' 连接」这个字符序列逐个喂哈希，
            // 与 Hash(joined) 逐位等价（含空串、单元素的分支）。
            return UnitHash(parts) / 10000f;
        }

        /// <summary>两段式，免去 <c>params</c> 数组分配（热路径用）。</summary>
        public static float Unit(string a, string b)
        {
            uint h = 2166136261u;
            Fnv(ref h, a);
            h ^= '|'; h *= 16777619u;
            Fnv(ref h, b);
            return (h % 10000u) / 10000f;
        }

        /// <summary>三段式，免去 <c>params</c> 数组分配（热路径用）。</summary>
        public static float Unit(string a, string b, string c)
        {
            uint h = 2166136261u;
            Fnv(ref h, a);
            h ^= '|'; h *= 16777619u;
            Fnv(ref h, b);
            h ^= '|'; h *= 16777619u;
            Fnv(ref h, c);
            return (h % 10000u) / 10000f;
        }

        static uint UnitHash(string[] parts)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (parts == null) return h;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) { h ^= '|'; h *= 16777619u; }
                    Fnv(ref h, parts[i]);
                }
                return h % 10000u;
            }
        }

        static void Fnv(ref uint h, string s)
        {
            unchecked
            {
                if (string.IsNullOrEmpty(s)) return;
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
            }
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
            => IndustryHeldOfKind(gs, airport, band, Data.SlotHolding.SlotKindDeparture);

        /// <summary>
        /// 同上，但按**池**算（★2026-11-06）。
        ///
        /// 为什么两个池要各算一份：一次停留从离港池取 1 条、从到港池取 1 条，
        /// 两池容量数相同 —— 所以「他航占了几条」本身可以同值。
        /// 但**不能永远同值**：真实机场到港与离港的拥挤度并不对称（早到波、宵禁前落地），
        /// 若两个池拿到同一个随机数，游戏里就永远碰不到「离港还有余量、到港已满」这种
        /// 真实存在、也正是玩家最需要被教会去处理的情况。故到港池用一条**独立哈希**，
        /// 离港池保持旧键不变（既有回归的数值一字不动）。
        /// </summary>
        public static int IndustryHeld(GameState gs, string airport, int band, string kind)
            => IndustryHeldOfKind(gs, airport, band, Logic.RoutePermitLogic.NormalizeKind(kind));

        static int IndustryHeldOfKind(GameState gs, string airport, int band, string kind)
        {
            int cap = RoutePermitLogic.SlotPoolCapacity(airport, band);
            if (cap <= 0) return 0;
            int ours = RoutePermitLogic.PoolUse(gs, airport, band, kind);
            int room = cap - ours;
            if (room <= 0) return 0;

            // 占用率 85%~92%（用户 2026-11-06 裁决 ②：扩量 3.5 倍后必须靠高占用保住稀缺感）。
            //
            // 旧口径 45%~80% 是「扩量前」的标定：那时池子本身很小，随便占一占就满了。
            // 容量按 1/3.5 扩张之后若仍按 45%~80% 占，池子会长期留出 2~6 条免费槽，
            // 二级市场就退化成「赶时间才用」的装饰 —— 而现实里 LHR 一对时刻能拍到天价，
            // 靠的正是「池子基本满、想进只能买」。所以占用率必须跟着容量一起抬。
            //
            // 仍保留 <see cref="MustLeaveOpen"/>：再挤也至少留 1 条，
            // 否则新玩家在枢纽连第一条线都开不出来，那不是稀缺而是死结。
            // 离港池沿用旧键（既有回归的数值一字不动）；到港池用独立键，见三参重载的说明。
            string salt = string.Equals(kind, Data.SlotHolding.SlotKindArrival, System.StringComparison.Ordinal)
                ? "occ:ARR" : "occ";
            float rate = 0.85f + 0.05f * Unit(airport, band.ToString(), salt);
            if (AirportCatalog.TierOfCode(airport) == 0) rate += 0.02f;   // 枢纽再挤一点，上限 92%
            int want = (int)Math.Floor(cap * rate);

            int hardCap = cap - MustLeaveOpen;
            if (hardCap < 0) hardCap = 0;
            if (want > hardCap) want = hardCap;
            if (want > room) want = room;          // 我司买多了，他航只能让位
            return want < 0 ? 0 : want;
        }

        /// <summary>
        /// 同上，但可限定**某一类**池（★2026-11-06）。
        /// <summary>本时段还能申请几条（= 池容量 − 我司 − 他航）。与玩法口径一致。</summary>
        public static int OpenSlots(GameState gs, string airport, int band)
        {
            int cap = RoutePermitLogic.SlotPoolCapacity(airport, band);
            if (cap <= 0) return 0;
            int left = cap - RoutePermitLogic.PoolUse(gs, airport, band, Data.SlotHolding.SlotKindDeparture)
                       - IndustryHeld(gs, airport, band);
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

            int ours = RoutePermitLogic.PoolUse(gs, airport, band, Data.SlotHolding.SlotKindDeparture);
            if (ours > cap) ours = cap;
            for (int i = 0; i < ours; i++) list.Add("我司");

            int industry = IndustryHeld(gs, airport, band);
            for (int k = 0; k < industry; k++) list.Add(PickAirline(airport, band, k));

            while (list.Count < cap) list.Add("");
            return list;
        }

        /// <summary>
        /// 某机场某刻「愿意出让的时刻」是谁在卖（★2026-11-07，A 方案按需报价用）。
        /// 与卡片上「他航已占」的持有人同源：先看那一刻实际铺排到的航司，
        /// 铺排里没人（理论上不会发生）时退回权重法挑一家。
        /// </summary>
        public static string SellerAt(GameState gs, string airport, string clock, int year, int quarter)
        {
            var chip = ChipAt(gs, airport, ScheduleSlots.BandOfClock(clock), clock);
            if (!string.IsNullOrEmpty(chip.ownerFull) && !chip.mine) return chip.ownerFull;
            if (!string.IsNullOrEmpty(chip.owner) && !chip.mine) return FullNameOf(chip.owner);
            return FullNameOf(PickAirline(airport, ScheduleSlots.BandOfClock(clock), 0));
        }

        /// <summary>按权重确定性挑一家他航填第 <paramref name="k"/> 个槽。</summary>
        static string PickAirline(string airport, int band, int k)
        {
            var set = WeightsFor(airport);
            if (set.cands.Length == 0)
            {
                // 该机场没有任何他航「经营」，但槽仍被点掉了 —— 不可能，放宽到全名单兜底。
                var any = IndustryAirlines.All[(int)(Hash(airport + "|" + band) % (uint)IndustryAirlines.All.Length)];
                return any != null ? any.Display : "他航";
            }

            float r = Unit(airport, band.ToString(), k.ToString(), "slot") * set.total;
            for (int i = 0; i < set.cands.Length; i++)
            {
                r -= set.weights[i];
                if (r <= 0f) return set.cands[i].Display;
            }
            return set.cands[set.cands.Length - 1].Display;
        }

        /// <summary>
        /// 某机场的**候选航司 + 权重 + 权重和**（★2026-11-07 性能）。
        ///
        /// 这三样只取决于机场（<see cref="Participates"/>、枢纽加成、本国加成、
        /// <c>Unit(code, airport, "w")</c> 抖动都与 band / k 无关），而旧版**每次填一条槽**
        /// 都要把 38 家航司重算一遍 —— 一个机场一个时段要填几十条槽，等于几千次重算，
        /// 再乘 80 个协调机场 × 4 段。「点机场时刻卡 40 秒」的另一半就在这里。
        ///
        /// 缓存是**永久**的：入参只有机场代码，而航司名单与 <c>Unit</c> 都是纯函数常量。
        /// </summary>
        sealed class AirlineWeightSet
        {
            public IndustryAirline[] cands;
            public float[] weights;
            public float total;
        }

        static readonly Dictionary<string, AirlineWeightSet> _weightCache =
            new Dictionary<string, AirlineWeightSet>(StringComparer.OrdinalIgnoreCase);

        static AirlineWeightSet WeightsFor(string airport)
        {
            string key = airport ?? "";
            if (_weightCache.TryGetValue(key, out var cached)) return cached;

            var cands = new List<IndustryAirline>();
            var weights = new List<float>();
            float total = 0f;
            string country = AirportRegions.CountryOf(airport);
            for (int i = 0; i < IndustryAirlines.All.Length; i++)
            {
                var a = IndustryAirlines.All[i];
                if (!Participates(a, airport)) continue;
                float w = a.tier == 0 ? 3f : a.tier == 1 ? 2f : 1f;
                if (string.Equals(a.hub, airport, StringComparison.OrdinalIgnoreCase)) w += 5f;
                if (!string.IsNullOrEmpty(country) && string.Equals(country, a.region, StringComparison.Ordinal))
                    w += 1f;
                w += Unit(a.code, airport, "w");       // 轻微抖动，免得份额永远是整齐的整数
                cands.Add(a);
                weights.Add(w);
                total += w;
            }

            var set = new AirlineWeightSet { cands = cands.ToArray(), weights = weights.ToArray(), total = total };
            _weightCache[key] = set;
            return set;
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
            /// <summary>
            /// 持有者的**全名**（他航取 <see cref="IndustryAirlines"/> 的 <c>name</c>）。
            /// 方块太窄只放得下短名，点开浮动提示时才需要全名 —— ★2026-11-06 用户要求
            /// 「点击色块弹出持仓的航司名称」。
            /// </summary>
            public string ownerFull;
            /// <summary>
            /// 持有者的 **IATA 两字码**（`CA` / `MU`）。★2026-11-07 用户要求：
            /// 灰块上直接写占用方缩写，玩家就不必点开浮窗才知道「被谁占了」。
            /// 我司 / 空闲为空串。
            /// </summary>
            public string ownerCode;
            /// <summary>该时刻的系列数（≥1）；&gt;1 说明这一时刻有多个系列。</summary>
            public int series;
            /// <summary>同一时刻除首个持有者外的**其他家数**（用于「+1」后缀）。</summary>
            public int others;
            /// <summary>我司持有。</summary>
            public bool mine;
            /// <summary>无人持有**且本刻还有余量** —— 绿块，点了能申请。</summary>
            public bool free;

            /// <summary>该刻的容量上限（0/1/2…）。浮窗要回答「这一格本来能放几条」。</summary>
            public int capacity;
            /// <summary>该刻已被持有的条数（含我司与他航）。</summary>
            public int held;
            /// <summary>该刻我司已持条数。</summary>
            public int mineHeld;
            /// <summary>
            /// 该刻**还能申请**几条。
            ///
            /// ★2026-11-06 三次（用户裁决）：口径就是浮窗那一行字本身 —— **容量 − 已占**。
            /// 「已占」含我司与他航，取两者与实际闸门 <c>RoutePermitLogic.SlotsAvailableAt</c>
            /// 同一份铺排；再与时段池 <c>SlotsAvailable</c> 取较小值（池满时逐刻再空也拿不到）。
            ///
            /// 曾经（2026-11-05）刻意**只扣我司**，让「他航让位」保住枢纽的可申请性；
            /// 代价是浮窗写成「容量 1 · 已占 1（汉莎）· 可申请 1」—— 三个数自己打自己，
            /// 用户当场指为错误：容量 1 被占 1，可申请就必须是 0。等让位、走二级市场、
            /// 还是改刻，是战略部/运营部该算清楚再报 CEO 的事，不能靠表格含糊过去。
            /// </summary>
            public int freeLeft;
            /// <summary>
            /// 该刻持有方明细，已按「全名 ×条数」聚合，形如 `汉莎航空 ×1、我司 ×1`。
            /// ★2026-11-06 用户要求：点击色块要能看出**各是哪些航司**占着，而不只是「有主」。
            /// </summary>
            public string holderLine;
            /// <summary>
            /// 同上，但**只留名字**、去掉「×N」：`汉莎航空、我司`。
            /// 浮窗按「离港容量 1 条 · 已占 1 条 · 新加坡航空」的样板写，条数已由「已占」讲过，
            /// 名字后面再挂一个 ×1 是重复计数，读起来像「一条槽被占了两遍」。
            /// </summary>
            public string holderNames;

            /// <summary>该刻**到港池**的容量（与离港池同值，见 `SlotCapacity(kind)` 的「同值双池」口径）。</summary>
            public int arrCapacity;
            /// <summary>该刻到港池已被占用的条数（含配对持仓的落地时刻与只到港持仓）。</summary>
            public int arrHeld;
            /// <summary>
            /// 该刻还能申请几条**到港**时刻。
            ///
            /// ★2026-11-06 用户要求 ②：机场要有到港时刻。看板主体仍是离港（一条槽的身份是离港时刻），
            /// 但「这一刻落地还能不能排进来」必须查得到 —— 否则玩家会拿着起飞权落不下去，
            /// 而界面上一片绿。判据与 <c>RoutePermitLogic.SlotsAvailableAt(..., ARR)</c> 同源。
            /// </summary>
            public int arrFreeLeft;
            /// <summary>该刻**我司**持有到港时刻（配对持仓的落地钟点，或只到港持仓）。左半块据此判蓝。</summary>
            public bool arrMine;
            /// <summary>
            /// 到港侧已占满时，占用方的 **IATA 两字码**（左半灰块上写「谁在落地」）。
            /// ★2026-11-07：右半（离港）此前已写码上色，左半却一律写「满」——
            /// 两头同一种「死灰」，玩家仍分不清「被占了」还是「坏了」。取码规则与离港侧相同
            /// （他航铺排的确定性选家），我司占用 / 空闲时为空串。
            /// </summary>
            public string arrOwnerCode;
            /// <summary>该刻到港池已占条数（我司 + 他航推导值）。浮窗「已占」用它。</summary>
            public int arrHeldTotal;
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
        /// 某机场某时段的**公布槽位**：按网格顺序给出每一个可申请时刻的方块。
        ///
        /// 三个建模决定，都是被回归测试与用户问询逼出来的：
        ///
        /// ① 以「网格时刻」而不是「槽」为单位。池容量常大于网格点数（枢纽晚班 14 槽 / 11 个点），
        ///    按槽铺方块会出现几块一模一样的「18:00」，看起来像重复渲染；以时刻为单位、
        ///    用「×N」标系列数，才是协调人公布时刻表的样子。
        ///
        /// ② 占用**均匀铺开到整个时段**，不是从时段开头往后堆。
        ///    首版按 `k % grid.Count` 顺序填，结果容量 14 的时段把前 8 个时刻全占满、
        ///    后 6 条槽无处分摊 —— 界面上表现为「整段全满」而旁边写着「余 6」，
        ///    正是 CEO 最不能接受的自相矛盾。改成按 `k * usable / occupied` 均匀映射后，
        ///    占用多于可容纳数时每个时刻都有多家（正常），少于时空缺**散落**在整段里。
        ///
        /// ③ **只出公布且可持有的槽位**（★2026-11-05 用户两次裁决）：
        ///    第一次只砍掉「未公布」的刻（小时容量 1–3 时派生规则只用到前 r 个刻，其余
        ///    不是「没余量」而是「没公布」，画灰块等于凭空制造 45% 的噪声）。
        ///    第二次（「时刻表里只需要展示三种时刻：可申请 / 我司 / 他航，不显示其它类型」）
        ///    把「不开放」与「免槽」也一并撤出卡片 —— 卡片只回答一件事：
        ///    **这条公布出来的槽现在归谁、能不能申请**。
        ///    注意：撤出的是**显示**，不是规则 —— 「窗口外不需要槽」仍由
        ///    <c>AirportCatalog.QuarterState</c> 与 <c>RoutePermitLogic.NeedsDepartureSlot</c> 管着。
        /// </summary>
        /// <summary>逐刻容量（0 = 该刻没有可持有的槽：未公布 / 该小时不分配 / 协调时段外）。</summary>
        static int[] ClockCaps(string airport, List<string> grid)
        {
            var capAt = new int[grid.Count];
            for (int i = 0; i < grid.Count; i++)
            {
                int m = ScheduleSlots.ParseClock(grid[i]);
                capAt[i] = m < 0 ? 0 : AirlineCEO.Data.AirportCatalog.QuarterCapacity(airport, m);
            }
            return capAt;
        }

        /// <summary>
        /// 离港池的**逐刻**占用（★2026-11-06 三次修订：浮窗三个数必须自洽）。
        ///
        /// 我司按**真实持仓时刻**落位（不铺排）—— 表上的蓝块必须就是那条槽所在的刻，
        /// 否则「我司持 12:00」会被画到 06:00，玩家照着表格排班必然对不上。
        /// 他航是推导值（<see cref="IndustryHeld"/> 给出总量），只在**剩余容量**上按
        /// <see cref="Place"/> 均匀铺开 —— 于是恒有「我司 + 他航 ≤ 容量」，
        /// 而「可申请 = 容量 − 已占」与闸门 <see cref="RoutePermitLogic.SlotsAvailableAt"/>
        /// 用的是同一份铺排，表上的数与玩法不会分家。
        /// </summary>
        static List<List<string>> DepLayout(GameState gs, string airport, int band, out int[] capAt)
        {
            var grid = ScheduleSlots.BandGridLabels(band);
            capAt = ClockCaps(airport, grid);
            var perQ = OursLayout(gs, airport, band, Data.SlotHolding.SlotKindDeparture, grid, capAt);

            int industry = IndustryHeld(gs, airport, band);
            FillWave(perQ, capAt, grid, airport, industry, k => PickAirline(airport, band, k));
            return perQ;
        }

        /// <summary>
        /// 他航按**航班波**铺排（★2026-11-07，用户要求）。
        ///
        /// 旧算法 `target = k * usable / industry` 是把槽从头到尾等距摊开 ——
        /// 于是 06:00 起每隔几刻就有一家，玩家看到的是一排「机械的点」，
        /// 既没有早高峰，也没有午后低谷。
        ///
        /// 新算法把刻按**需求权重**排序（权重 = 该小时容量 ÷ 该机场峰值小时容量），
        /// 先把高峰刻填满，权重最低的刻（午间 12–14、凌晨 23–06）自然留空。
        /// 总量仍由 <see cref="IndustryHeld"/> 决定（85–92%），所以「留空」不是额外少占，
        /// 而是把同样的占用量**重新分布**到有需求的波上。
        /// </summary>
        static void FillWave(List<List<string>> perQ, int[] capAt, List<string> grid,
            string airport, int count, Func<int, string> who)
        {
            if (count <= 0) return;
            int n = capAt.Length;
            var weight = new float[n];
            var order = new int[n];
            // ★2026-11-07 性能：峰值小时容量按**机场**算一次即可。
            //   旧版逐刻调 DemandWeight，每次都要把 24 小时容量表重扫一遍找峰值 ——
            //   一段 13 个刻就是 13 遍，而答案对同一机场永远相同。
            int peak = PeakHourCapacity(airport);
            for (int i = 0; i < n; i++)
            {
                weight[i] = WeightAt(airport, grid[i], peak);
                order[i] = i;
            }
            Array.Sort(order, (a, b) =>
            {
                int c = weight[b].CompareTo(weight[a]);
                return c != 0 ? c : a.CompareTo(b);
            });

            int cursor = 0;
            for (int k = 0; k < count; k++)
            {
                int pick = -1;
                for (int t = 0; t < n; t++)
                {
                    int idx = order[cursor % n];
                    cursor++;
                    if (perQ[idx].Count < capAt[idx]) { pick = idx; break; }
                }
                if (pick < 0) break;                       // 容量已满：不为凑数而超发
                perQ[pick].Add(who(k));
            }
        }

        /// <summary>
        /// 某一刻的需求权重（0..1）：该小时容量 ÷ 该机场全天峰值小时容量。
        ///
        /// 用容量曲线本身当权重，比另写一份「早高峰 07–09、晚高峰 18–20」的硬编码更好：
        /// 大兴的高峰在 09 时、厦门在 19 时、迪拜整夜都忙 —— 一份硬编码照顾不到这些差异，
        /// 而现实曲线本来就已经把「哪一小时忙」写清楚了。
        /// </summary>
        public static float DemandWeight(string airport, string clock)
        {
            return WeightAt(airport, clock, PeakHourCapacity(airport));
        }

        /// <summary>
        /// 该机场**全天峰值小时容量**。★2026-11-07 性能：由 <see cref="DemandWeight"/> 拆出来，
        /// 让同一机场的一段刻只算一次峰值（旧版每个刻都要重扫 24 小时）。
        /// 容量表 <c>AirportCatalog.AirportHourRates</c> 是只读常量，故这个值在运行期不变。
        /// </summary>
        static int PeakHourCapacity(string airport)
        {
            int peak = 0;
            for (int h = 0; h < 24; h++)
            {
                int v = AirportCatalog.HourCapacity(airport, h);
                if (v > peak) peak = v;
            }
            return peak;
        }

        /// <summary>给定峰值时的需求权重（与 <see cref="DemandWeight"/> 逐位一致）。</summary>
        static float WeightAt(string airport, string clock, int peak)
        {
            if (peak <= 0) return 0f;
            int hour = ScheduleSlots.ParseClock(clock) / 60;
            return (float)AirportCatalog.HourCapacity(airport, hour) / peak;
        }

        /// <summary>
        /// 我司在每一刻的落位：**一条持仓一格**，优先落在自己的钟点上；该刻已被自己前一条
        /// 占满时，按 <see cref="Place"/> 就近找还有余量的刻。
        ///
        /// 为什么逐条铺而不是「按刻数一遍」：`PoolUseAt` 是 ±容差命中，两条持仓若分别
        /// 12:20 / 12:22，会同时被 12:15 与 12:30 数到，Σ 逐刻就会大于池占用，
        /// R48 的「系列合计 = 已占用」立刻失败。逐条落位保证 Σ ≡ <see cref="RoutePermitLogic.PoolUse"/>。
        /// </summary>
        static List<List<string>> OursLayout(GameState gs, string airport, int band, string kind,
            List<string> grid, int[] capAt)
        {
            var perQ = new List<List<string>>(grid.Count);
            for (int i = 0; i < grid.Count; i++) perQ.Add(new List<string>(2));

            var clocks = RoutePermitLogic.PoolClocksAt(gs, airport, band, kind);
            for (int i = 0; i < clocks.Count; i++)
            {
                int idx = NearestGridIndex(grid, clocks[i]);
                if (idx < 0) continue;
                if (!Place(perQ, capAt, idx, "我司")) break;   // 真放不下：不为凑数而超发
            }
            return perQ;
        }

        /// <summary>离 `<paramref name="clock"/>` 最近的网格下标（网格按 15 分钟递增，先到先得）。</summary>
        static int NearestGridIndex(List<string> grid, string clock)
        {
            int best = -1, bestD = int.MaxValue;
            for (int i = 0; i < grid.Count; i++)
            {
                int d = ScheduleSlots.ClockDiff(grid[i], clock);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// 到港池的**逐刻**占用：我司按真实持仓（配对持仓取落地钟点、只到港持仓取自身钟点），
        /// 他航同样只铺在剩余容量上。
        /// </summary>
        static void ArrLayout(GameState gs, string airport, int band, int[] capAt,
            out int[] arrOurs, out int[] arrIndustry, out List<List<string>> arrNames)
        {
            var grid = ScheduleSlots.BandGridLabels(band);
            var perQ = OursLayout(gs, airport, band, Data.SlotHolding.SlotKindArrival, grid, capAt);
            arrOurs = new int[grid.Count];
            for (int i = 0; i < grid.Count; i++) arrOurs[i] = perQ[i].Count;

            // ★2026-11-07：到港池也记**名字**（此前只记条数），左半灰块才能写出占用方缩写。
            // 铺排算法与 <see cref="FillWaveCounts"/> 完全一致（同一权重排序、同一 cursor 规则），
            // 故条数不变，闸门 `IndustryClockCounts(ARR)` 与表上数字仍同源。
            int total = IndustryHeld(gs, airport, band, Data.SlotHolding.SlotKindArrival);
            FillWave(perQ, capAt, grid, airport, total, k => PickAirline(airport, band, k));

            arrIndustry = new int[grid.Count];
            for (int i = 0; i < grid.Count; i++)
            {
                int n = 0;
                for (int k = 0; k < perQ[i].Count; k++)
                    if (!string.Equals(perQ[i][k], "我司", StringComparison.Ordinal)) n++;
                arrIndustry[i] = n;
            }
            arrNames = perQ;
        }

        /// <summary>计数版的 <see cref="FillWave"/>：到港池不记名字，只记每刻条数。</summary>
        static void FillWaveCounts(int[] counts, int[] ours, int[] capAt, List<string> grid,
            string airport, int count)
        {
            if (count <= 0) return;
            int n = capAt.Length;
            var weight = new float[n];
            var order = new int[n];
            int peak = PeakHourCapacity(airport);   // 同上：峰值只算一次
            for (int i = 0; i < n; i++)
            {
                weight[i] = WeightAt(airport, grid[i], peak);
                order[i] = i;
            }
            Array.Sort(order, (a, b) =>
            {
                int c = weight[b].CompareTo(weight[a]);
                return c != 0 ? c : a.CompareTo(b);
            });

            int cursor = 0;
            for (int k = 0; k < count; k++)
            {
                int pick = -1;
                for (int t = 0; t < n; t++)
                {
                    int idx = order[cursor % n];
                    cursor++;
                    if (ours[idx] + counts[idx] < capAt[idx]) { pick = idx; break; }
                }
                if (pick < 0) break;
                counts[pick]++;
            }
        }

        /// <summary>
        /// 该机场该时段**他航**在每一个 15 分钟刻上占了几条（与卡片同一份铺排，★2026-11-06）。
        ///
        /// 闸门 <see cref="RoutePermitLogic.SlotsAvailableAt"/> 用它扣掉他航占用：
        /// 浮窗写「已占 1 条（汉莎）」就必须同时写「可申请 0 条」——
        /// 表上说满、玩法却放行，是玩家最不能接受的那种不一致。
        /// </summary>
        public static int[] IndustryClockCounts(GameState gs, string airport, int band, string kind)
        {
            string k = RoutePermitLogic.NormalizeKind(kind);
            bool arr = string.Equals(k, Data.SlotHolding.SlotKindArrival, StringComparison.Ordinal);
            var grid = ScheduleSlots.BandGridLabels(band);
            if (!arr)
            {
                var perQ = DepLayout(gs, airport, band, out _);
                var counts = new int[grid.Count];
                for (int i = 0; i < grid.Count; i++)
                    for (int j = 0; j < perQ[i].Count; j++)
                        if (!string.Equals(perQ[i][j], "我司", StringComparison.Ordinal)) counts[i]++;
                return counts;
            }

            var caps = ClockCaps(airport, grid);
            ArrLayout(gs, airport, band, caps, out _, out int[] ind, out _);
            return ind;
        }

        /// <summary>某一具体时刻被他航占了几条（非公布刻一律 0）。</summary>
        public static int IndustryAtClock(GameState gs, string airport, string clock, string kind)
        {
            int m = ScheduleSlots.ParseClock(clock);
            if (m < 0) return 0;
            string at = ScheduleSlots.FormatClock(m);
            int band = ScheduleSlots.BandOfClock(at);
            var grid = ScheduleSlots.BandGridLabels(band);
            int idx = -1;
            for (int i = 0; i < grid.Count; i++)
                if (string.Equals(grid[i], at, StringComparison.Ordinal)) { idx = i; break; }
            if (idx < 0) return 0;   // 非公布刻：本来就没有槽，谈不上他航占用
            var counts = IndustryClockCounts(gs, airport, band, kind);
            return idx < counts.Length ? counts[idx] : 0;
        }

        public static List<SlotChip> ChipsOf(GameState gs, string airport, int band)
            => ChipsOf(gs, airport, band, out _, out _);

        /// <summary>
        /// 同上，并顺手带回本段两个池的**可申请余量**（★2026-11-07 性能）。
        ///
        /// 这两个数本来就已经在本函数里算好了（<c>bandFree</c> / <c>bandFreeArr</c>），
        /// 调用方（纯文字表、将来的新 UI）若还要写「本段余 N」，直接用返回值即可 ——
        /// 否则就得再调一次 <see cref="RoutePermitLogic.SlotsAvailable"/>，
        /// 而它内部要把整段他航铺排重做一遍。80 个机场 × 4 段 × 2 个池就是 640 次重复铺排。
        /// </summary>
        public static List<SlotChip> ChipsOf(GameState gs, string airport, int band,
            out int bandFreeDep, out int bandFreeArr)
        {
            bandFreeDep = 0;
            bandFreeArr = 0;
            var chips = new List<SlotChip>();
            var grid = ScheduleSlots.BandGridLabels(band);
            if (grid.Count == 0) return chips;

            var perQ = DepLayout(gs, airport, band, out int[] capAt);
            ArrLayout(gs, airport, band, capAt, out int[] arrOurs, out int[] arrIndustry,
                out var arrNames);

            // ★2026-11-07 性能（用户报障「点机场时刻卡 40 秒」）：
            // 段余与「本段是否已满」属于**整段**的量，与逐刻无关，必须在循环外算一次。
            // 旧版把 `SlotsAvailable`（连同它内部的整段他航铺排）写在循环里，
            // 一段 13 个刻就重算 13 次；离港 / 到港各一遍，一段 26 次整段铺排。
            // 这里直接把上面已经铺好的 perQ / arrIndustry 交给重载复用 —— 判据一字不改。
            var depCounts = new int[grid.Count];
            for (int i = 0; i < grid.Count && i < perQ.Count; i++)
            {
                int n = 0;
                var lst = perQ[i];
                for (int j = 0; j < lst.Count; j++)
                    if (!string.Equals(lst[j], "我司", StringComparison.Ordinal)) n++;
                depCounts[i] = n;
            }
            int openDep = RoutePermitLogic.SlotsAvailable(gs, airport, band,
                Data.SlotHolding.SlotKindDeparture, depCounts);
            int openArr = RoutePermitLogic.SlotsAvailable(gs, airport, band,
                Data.SlotHolding.SlotKindArrival, arrIndustry);
            bandFreeDep = openDep;
            bandFreeArr = openArr;

            for (int i = 0; i < grid.Count; i++)
            {
                // ★2026-11-05 三次（用户：只展示 可申请 / 我司 / 他航 三种）：
                // 容量 0 的刻一律不出块 —— 未公布、该小时不分配、协调时段外，都属于「这里没有可持有的槽」，
                // 卡片不回答这些问题，只回答「这条公布的槽现在归谁」。
                if (capAt[i] <= 0) continue;
                int cap = capAt[i];
                var list = perQ[i];
                int mineCount = 0;
                for (int k = 0; k < list.Count; k++)
                    if (string.Equals(list[k], "我司", StringComparison.Ordinal)) mineCount++;

                // 「还能申请几条」必须与真实取得判据同源，否则表上的数与玩法分家：
                //   ★2026-11-06 三次：已占 = 我司 + 他航（同一份铺排），可申请 = 容量 − 已占。
                //   旧版只扣我司，于是「容量 1 · 已占 1（汉莎）· 可申请 1」三数打架 ——
                //   玩家一眼就看出这是错的，且闸门确实会放行，等于表在骗人。
                int clockFree = cap - list.Count;
                if (clockFree < 0) clockFree = 0;
                int freeLeft = clockFree < openDep ? clockFree : openDep;
                if (freeLeft < 0) freeLeft = 0;

                var c = new SlotChip
                {
                    time = grid[i],
                    series = list.Count,
                    capacity = cap,
                    held = list.Count,
                    mineHeld = mineCount,
                    freeLeft = freeLeft,
                    holderLine = HolderLineOf(gs, list),
                    holderNames = HolderNamesOf(gs, list)
                };

                // ———— 到港池：同一刻的「落地」还剩几条（★2026-11-06）————
                // 到港侧不走 SlotHolders（他航的落地时刻是推导值，画成方块会与离港块混在一起），
                // 只给容量/已占/可申请三个数，供浮窗显示与「只申请到港」的闸门使用。
                //
                // ★2026-11-06 二次：左半块要上色，就必须知道「这一端是不是我司持有」，
                // 故补 arrMine / arrHeldTotal。他航到港占用按池推导、按可用刻均匀铺开
                // （与离港侧 Place 同一套映射），否则「已占 + 可申请」会大于容量 —— 那正是
                // 玩家最不能接受的「表上的数与玩法分家」。
                c.arrCapacity = cap;
                c.arrHeld = arrOurs[i];
                c.arrMine = arrOurs[i] > 0;
                int arrHeldShown = arrOurs[i] + arrIndustry[i];
                c.arrHeldTotal = arrHeldShown > cap ? cap : arrHeldShown;
                int arrHere = RoutePermitLogic.SlotsAvailableAt(gs, airport, grid[i],
                    Data.SlotHolding.SlotKindArrival, arrIndustry[i]);
                c.arrFreeLeft = arrHere < openArr ? arrHere : openArr;
                if (c.arrFreeLeft < 0) c.arrFreeLeft = 0;
                c.arrOwnerCode = c.arrMine ? "" : FirstIndustryCodeAt(arrNames, i);

                if (list.Count == 0)
                {
                    c.free = true;               // 整刻空着，可申请
                    chips.Add(c);
                    continue;
                }

                int mineIdx = list.IndexOf("我司");
                c.mine = mineIdx >= 0;
                c.owner = c.mine ? "我司" : list[0];
                c.ownerFull = c.mine ? "我司" : FullNameOf(c.owner);
                c.ownerCode = c.mine ? "" : CodeOf(c.owner);

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

        /// <summary>
        /// 把一个时刻的持有方列表聚合成一行明细，形如 `汉莎航空 ×1、我司 ×1`。
        ///
        /// 为什么在这里聚合而不是交给 UI：持有方列表是「一家一条」的原始形态，
        /// 而同刻容量最多也就 2 条，真正需要的是**去重后的家数与全名**。
        /// 放在数据侧，浮窗与将来的「时刻明细」页可以共用同一份口径。
        /// </summary>
        static string HolderLineOf(GameState gs, List<string> holders)
        {
            if (holders == null || holders.Count == 0) return "";
            var names = new List<string>();
            var counts = new List<int>();
            for (int i = 0; i < holders.Count; i++)
            {
                string raw = holders[i];
                if (string.IsNullOrEmpty(raw)) continue;
                string shown = string.Equals(raw, "我司", StringComparison.Ordinal)
                    ? MineLabel(gs) : FullNameOf(raw);
                int at = names.IndexOf(shown);
                if (at >= 0) counts[at]++;
                else { names.Add(shown); counts.Add(1); }
            }
            var sb = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (sb.Length > 0) sb.Append('、');
                sb.Append(names[i]);
                if (counts[i] > 1) sb.Append(" ×").Append(counts[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 只要名字、不带「×N」的持有方明细：`新加坡航空` / `汉莎航空、我司`。
        ///
        /// 用户的浮窗样板是「离港容量 1 条 · 已占 1 条 · 新加坡航空」——
        /// 条数已经由「已占 N 条」说过一遍，名字后再挂 ×1 会读成「一条槽被占了两遍」。
        /// </summary>
        static string HolderNamesOf(GameState gs, List<string> holders)
        {
            if (holders == null || holders.Count == 0) return "";
            var names = new List<string>(holders.Count);
            for (int i = 0; i < holders.Count; i++)
            {
                string raw = holders[i];
                if (string.IsNullOrEmpty(raw)) continue;
                string shown = string.Equals(raw, "我司", StringComparison.Ordinal)
                    ? MineLabel(gs) : FullNameOf(raw);
                if (names.IndexOf(shown) < 0) names.Add(shown);
            }
            var sb = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (sb.Length > 0) sb.Append('、');
                sb.Append(names[i]);
            }
            return sb.ToString();
        }

        /// <summary>「我司」在浮窗里的显示名：优先公司全名，拿不到就退回「我司」。</summary>
        static string MineLabel(GameState gs)
            => gs != null && !string.IsNullOrEmpty(gs.companyNameZh) ? gs.companyNameZh : "我司";

        /// <summary>把一条持有者放到「距 <paramref name="target"/> 最近的、仍有容量的时刻」上。</summary>
        static bool Place(List<List<string>> perQ, int[] capAt, int target, string name)
        {
            int n = perQ.Count;
            for (int d = 0; d < n; d++)
            {
                int a = target - d, b = target + d;
                if (a >= 0 && a < n && perQ[a].Count < capAt[a]) { perQ[a].Add(name); return true; }
                if (b >= 0 && b < n && b != a && perQ[b].Count < capAt[b]) { perQ[b].Add(name); return true; }
            }
            return false;
        }

        /// <summary>
        /// 计数版的 <see cref="Place"/>：把一条**他航到港占用**放到最近的、还有余地的刻上。
        /// 「余地」= 该刻容量 − 我司到港占用 − 已铺开的他航占用，故铺完后逐刻
        /// 「我司 + 他航 ≤ 容量」必然成立，浮窗里的「已占」不会超过容量。
        /// </summary>
        static bool PlaceCount(int[] dst, int[] ours, int[] capAt, int target)
        {
            int n = dst.Length;
            for (int d = 0; d < n; d++)
            {
                int a = target - d, b = target + d;
                if (a >= 0 && a < n && dst[a] + ours[a] < capAt[a]) { dst[a]++; return true; }
                if (b >= 0 && b < n && b != a && dst[b] + ours[b] < capAt[b]) { dst[b]++; return true; }
            }
            return false;
        }

        /// <summary>某机场某时段的某一刻的方块（找不到返回 default）。</summary>
        public static SlotChip ChipAt(GameState gs, string airport, int band, string clock)
        {
            var chips = ChipsOf(gs, airport, band);
            for (int i = 0; i < chips.Count; i++)
                if (string.Equals(chips[i].time, clock, StringComparison.Ordinal)) return chips[i];
            return default;
        }

        /// <summary>航司短名 → 全名（他航查 <see cref="IndustryAirlines.All"/>，查不到原样返回）。</summary>
        public static string FullNameOf(string display)
        {
            if (string.IsNullOrEmpty(display)) return "";
            for (int i = 0; i < IndustryAirlines.All.Length; i++)
            {
                var a = IndustryAirlines.All[i];
                if (a != null && string.Equals(a.Display, display, StringComparison.Ordinal))
                    return string.IsNullOrEmpty(a.name) ? display : a.name;
            }
            return display;
        }

        /// <summary>
        /// 航司短名 → **IATA 两字码**（`国航` → `CA`）。★2026-11-07 用户要求：
        /// 灰块上写占用方的两字码，让玩家一眼看出「这是被别人占了，不是游戏坏了」。
        /// 查不到的（例如我司）返回空串，由调用方决定退回什么。
        /// </summary>
        public static string CodeOf(string display)
        {
            if (string.IsNullOrEmpty(display)) return "";
            for (int i = 0; i < IndustryAirlines.All.Length; i++)
            {
                var a = IndustryAirlines.All[i];
                if (a == null) continue;
                if (string.Equals(a.Display, display, StringComparison.Ordinal)
                    || string.Equals(a.name, display, StringComparison.Ordinal)
                    || string.Equals(a.code, display, StringComparison.Ordinal))
                    return a.code ?? "";
            }
            return "";
        }

        /// <summary>到港逐刻铺排里，第一个**他航**的两字码（无则空串）。★2026-11-07：左半灰块写谁在落地。</summary>
        static string FirstIndustryCodeAt(List<List<string>> perQ, int index)
        {
            if (perQ == null || index < 0 || index >= perQ.Count) return "";
            var names = perQ[index];
            for (int k = 0; k < names.Count; k++)
            {
                if (string.IsNullOrEmpty(names[k])) continue;
                if (string.Equals(names[k], "我司", StringComparison.Ordinal)) continue;
                return CodeOf(names[k]);
            }
            return "";
        }

        // ———— 过滤 / 排序（工具栏） ————

        public const int FilterAll = 0;
        public const int FilterMine = 1;
        public const int FilterHub = 2;
        public const int FilterFull = 3;

        public static readonly string[] FilterLabels = { "全部机场", "我司航网", "只看枢纽", "只看满池" };

        /// <summary>
        /// 机场时刻页的筛选条件（★2026-11-06：工具栏那排按钮改成 FM 式过滤器菜单后，
        /// 条件不再是「五选一的下标」，而是三组**可共存**的独立条件）。
        ///
        /// 为什么必须换模型：原来的 `filter` 是单值下标，选了「只看枢纽」就选不了「我司航网」；
        /// 而玩家想找的是「我司航网里还有空位的枢纽」这种**组合**。单值下标在数据结构上
        /// 就表达不了组合条件，改菜单也是假的。
        /// </summary>
        public struct SlotFilterQuery
        {
            /// <summary>机场等级：-1 = 不限；0 = 只看枢纽；1 = 只看干线。</summary>
            public int tier;
            /// <summary>只看我司航网（有持仓，或是我司航线端点）。</summary>
            public bool mineOnly;
            /// <summary>余量：0 = 不限；1 = 仍有空位；2 = 池已满。</summary>
            public int openMode;
            /// <summary>true = 按可申请余量降序；false = 按我司持有降序。</summary>
            public bool sortByOpen;
            /// <summary>搜索词（机场代码 / 中英文名，子串匹配）。</summary>
            public string query;

            public static SlotFilterQuery Default
            {
                get { return new SlotFilterQuery { tier = -1, openMode = 0, query = "" }; }
            }

            /// <summary>是否等于默认视图（决定「筛选」按钮是否点亮）。</summary>
            public bool IsDefault
            {
                get { return tier < 0 && !mineOnly && openMode == 0; }
            }

            /// <summary>工具栏上那句「筛选：枢纽 · 我司航网 · 按余量」。</summary>
            public string Describe()
            {
                var parts = new List<string>(4);
                if (tier == 0) parts.Add("枢纽");
                else if (tier == 1) parts.Add("干线");
                if (mineOnly) parts.Add("我司航网");
                if (openMode == 1) parts.Add("仍有空位");
                else if (openMode == 2) parts.Add("池已满");
                parts.Add(sortByOpen ? "按余量排序" : "按我司持有排序");
                return string.Join(" · ", parts);
            }
        }

        /// <summary>兼容旧签名的单值筛选（回归测试仍按常量调用）。</summary>
        public static List<AirportInfo> FilteredAirports(GameState gs, int filter, bool sortByOpen, string query)
        {
            var q = SlotFilterQuery.Default;
            q.sortByOpen = sortByOpen;
            q.query = query;
            if (filter == FilterMine) q.mineOnly = true;
            else if (filter == FilterHub) q.tier = 0;
            else if (filter == FilterFull) q.openMode = 2;
            return FilteredAirports(gs, q);
        }

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
        /// <see cref="SlotFilterQuery.sortByOpen"/> 为真时改按可申请余量降序 ——
        /// 想找哪里还进得去时，这是唯一有用的顺序。
        /// </summary>
        public static List<AirportInfo> FilteredAirports(GameState gs, SlotFilterQuery f)
        {
            var rows = new List<AirportInfo>();
            var airports = AirportCatalog.Airports;
            string query = f.query;
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
                if (f.tier >= 0 && ap.tier != f.tier) continue;
                if (f.mineOnly && !InMyNetwork(gs, ap.code)) continue;
                var t = TotalsOf(gs, ap.code);
                if (f.openMode == 1 && t.open <= 0) continue;
                if (f.openMode == 2 && t.open > 0) continue;
                rows.Add(ap);
            }

            rows.Sort((x, y) =>
            {
                if (f.sortByOpen)
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

        /// <summary>
        /// 「机场时刻」的纯文字表（★2026-11-07 用户裁决：该页 UI 全部删除、版式回到纯文字，
        /// 等新的 UI 设计稿；数据与判据一条不动）。
        ///
        /// 一个机场一段：
        /// <code>
        /// AMS 阿姆斯特丹 · 干线 · 池 213 条 · 我司 0 · 他航 213 · 余 0 · 23:00–00:00 宵禁
        ///   早 06:00–09:30 · 离港余 0 · 到港余 0
        ///     06:00 荷兰皇家航空 ×2 · 06:15 — · 06:30 中国东方航空
        /// </code>
        ///
        /// 口径与卡片版、浮窗完全同源：<see cref="ChipsOf"/> 只吐「已公布且可持有」的刻
        /// （不公布 / 免槽 / 协调时段外一律不出现），故表上「—」就是此刻真能申请的槽；
        /// 四个时段各给一行「离港余 / 到港余」，到港池的逐刻明细留给新 UI。
        /// </summary>
        public static string SlotsTableText(GameState gs)
        {
            if (gs == null) return "";
            var sb = new StringBuilder(96 * 1024);
            var list = AirportCatalog.Airports;
            for (int i = 0; i < list.Count; i++)
            {
                var ap = list[i];
                if (!ap.coordinated) continue;

                var t = TotalsOf(gs, ap.code);
                sb.Append(ap.code).Append(' ').Append(ap.nameZh)
                  .Append(" · ").Append(AirportCatalog.TierLabel(ap.tier))
                  .Append(" · 池 ").Append(t.cap).Append(" 条 · 我司 ").Append(t.own)
                  .Append(" · 他航 ").Append(t.other).Append(" · 余 ").Append(t.open);
                string curfew = AirportCatalog.CurfewLabel(ap.code);
                if (!string.IsNullOrEmpty(curfew)) sb.Append(" · ").Append(curfew);
                sb.Append('\n');

                for (int b = 0; b < 4; b++)
                {
                    var chips = ChipsOf(gs, ap.code, b, out int depOpen, out int arrOpen);

                    sb.Append("  ").Append(ScheduleSlots.SlotBandName(b)).Append(' ')
                      .Append(ScheduleSlots.FormatClock(ScheduleSlots.WindowStartOf(b)))
                      .Append('–')
                      .Append(ScheduleSlots.FormatClock(ScheduleSlots.WindowEndOf(b)))
                      .Append(" · 离港余 ").Append(depOpen)
                      .Append(" · 到港余 ").Append(arrOpen)
                      .Append('\n');

                    sb.Append("    ");
                    for (int k = 0; k < chips.Count; k++)
                    {
                        if (k > 0) sb.Append(" · ");
                        var c = chips[k];
                        sb.Append(c.time).Append(' ');
                        sb.Append(string.IsNullOrEmpty(c.holderLine) ? "—" : c.holderLine);
                    }
                    sb.Append('\n');
                }
                sb.Append('\n');
            }
            return sb.ToString();
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
