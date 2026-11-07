using System.Collections.Generic;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航线时段预设（早/午/晚/红眼）。供命令台加班与运力调令、排班调整（S4）共用。
    /// 纯 C#，Unity 与 tools/balance 可复用。
    ///
    /// ★2026-10-15（CEO 问询后）**时段与时刻是两套东西**，本类把这件事写清楚：
    /// - **需求波窗口**（`WindowStart`/`WindowEnd`）：早 06:30–09:30 这样的**时间容器**，
    ///   只用来把班次归进四个需求波，**不是**时刻槽的粒度。
    /// - **时刻槽**：现实里一条槽 = **某日某时刻的一次离港**（WASG 1.7.2d 明写「不得故意在
    ///   明显不同于获批时刻的时间运行」），所以槽要精确到 `HH:mm`；同一窗口内 06:30/07:00/07:30/08:00
    ///   是**四条槽**，不是一条槽用四次。本类提供窗口内的**时刻网格**与吸附/解析纯函数。
    /// </summary>
    public static class ScheduleSlots
    {
        public const string Morning = "早班";
        public const string Noon = "午班";
        public const string Evening = "晚班";
        public const string RedEye = "红眼";

        public static readonly string[] Labels = { Morning, Noon, Evening, RedEye };
        public static readonly string[] DepTimes = { "08:00", "12:00", "18:00", "22:00" };
        /// <summary>时段需求系数（晚班最高，红眼最低）。</summary>
        public static readonly float[] Demands = { 1.00f, 0.90f, 1.10f, 0.80f };

        /// <summary>
        /// 时段名（早/中/晚/夜）。**只用于池容量与需求波归集**，不再作为时刻槽的身份 ——
        /// 时刻槽的身份是具体时刻 `SlotHolding.slotTime = "HH:mm"`（见类注释）。
        /// </summary>
        public static readonly string[] SlotBands = { "早", "中", "晚", "夜" };

        // —— R7 需求波窗口（三峰一谷）——
        // DepTimes 仍作为窗口内的「峰点/代表时刻」，但不再限制唯一取值：
        // 排班可在窗口内生成其它时刻（见 WindowStart/WindowEnd 与 SnapIntoWindow）。
        //   早高峰 06:30–09:30（峰点 08:00）· 午高峰 11:30–14:00（峰点 12:00）
        //   晚高峰 16:30–20:00（峰点 18:00，全天最旺）· 夜间 20:00–23:00（峰点 22:00）
        //
        // ★2026-11-05（用户裁决）：**需求波只管需求系数与定价**，不再决定「槽存不存在」。
        // 槽的存在范围改由「机场协调时段」决定（见 AirportCatalog.CoordWindowOf），
        // 现实依据：《民航航班时刻管理办法》—— 主协调机场 24 小时全时段协调。
        // 因此晚班窗口末刻从 20:00 收到 **19:45**：20:00 在 15 分钟网格上只能有一个身份，
        // 而 BandOfDep("20:00") 判给夜班，若晚班仍含 20:00，晚班那张表会印出一个
        // 属于夜班池的槽（改动前就存在这个错，只是被 30 分钟粒度掩盖着）。
        public static readonly int[] WindowStart = { 6 * 60 + 30, 11 * 60 + 30, 16 * 60 + 30, 20 * 60 };
        public static readonly int[] WindowEnd = { 9 * 60 + 30, 14 * 60, 19 * 60 + 45, 23 * 60 };

        public static int IndexOf(string label)
        {
            if (string.IsNullOrEmpty(label)) return 0;
            for (int i = 0; i < Labels.Length; i++)
                if (Labels[i] == label) return i;
            return 0;
        }

        /// <summary>时段下标 → 时刻槽时段名（早/中/晚/夜）。</summary>
        public static string SlotBandName(int band)
        {
            int i = band < 0 ? 0 : band > 3 ? 3 : band;
            return SlotBands[i];
        }

        /// <summary>由起飞时刻推断时段下标（0 早 / 1 中 / 2 晚 / 3 夜）。</summary>
        public static int BandOfDep(string dep)
        {
            int h = 0;
            if (!string.IsNullOrEmpty(dep) && dep.Length >= 2)
                int.TryParse(dep.Substring(0, 2), out h);
            if (h < 10) return 0;
            if (h < 15) return 1;
            if (h < 20) return 2;
            return 3;
        }

        public static int WindowStartOf(int band) => WindowStart[Clamp(band)];
        public static int WindowEndOf(int band) => WindowEnd[Clamp(band)];

        // ————————————————— 时刻槽的时间网格（★2026-10-15，★2026-11-05 改 15 分钟）—————————————————
        //
        // 现实锚点：WASG 1.7.2(d)「航司不得故意在明显不同于获批时刻的时间运行」；1.7.2(e)
        // 「一个系列 = 同一（或约同一）时刻、同一星期几、整季规律分布的 ≥5 个时刻」。
        // 所以「买哪个时段」这种问法在现实里根本不存在 —— 协调人分配的是**具体时刻**。
        //
        // ★2026-11-05（用户裁决）粒度由 30 分钟改为 **15 分钟**：
        // 《民航航班时刻管理办法》第七条把协调参数定义为「机场特定 1 小时或者 15 分钟内
        // 可提供的航班时刻数量」—— 官方申请单位就是 15 分钟。30 分钟在国际语境里是
        // **运行偏离宽容度**（见 SlotOffsetToleranceMinutes），不是申请单位，此前把两个
        // 概念揉在同一个常量里了。

        /// <summary>时刻槽网格的步长（分钟）。★2026-11-05 由 30 改为 15。</summary>
        public const int SlotGridStepMinutes = 15;

        /// <summary>
        /// 一条持仓可服务的班次，其离港时刻与持仓时刻的最大偏差（分钟）。
        ///
        /// ★2026-11-05：由 15 改为 **10**。硬约束：**必须小于 <see cref="SlotGridStepMinutes"/>(15)**，
        /// 否则网格上相邻两刻互为「同一刻」—— 玩家拿了两条相邻的 15 分钟槽，排班器却认为
        /// 它们是一条（`ClockNear` 会把后一刻的班判成「本线已在那一刻飞着一班」而跳过）。
        /// 为什么不是 7（= 半步长）：推导出来的离港时刻要四舍五入到最近的刻钟，
        /// 最大误差 7 分钟，而部分调用点用 `SnapUpToGrid`（向上取整），误差可达 8 分钟 ——
        /// 取 7 会让「刚买到的槽」在临近判据里差一分钟匹配不上（实测令 3 条 PlayMode 用例红）。
        /// 取 10 既覆盖取整误差，又小于步长。该约束由回归 R52 钉住。
        /// </summary>
        public const int SlotToleranceMinutes = 10;

        /// <summary>
        /// 运行**偏离宽容度**（分钟）—— 与申请粒度是两件事。
        ///
        /// 现实口径（IATA WASG 与欧洲协调惯例）：航班实际运行时刻偏离获批时刻 30 分钟以内
        /// 通常不判违规。本作据此只做**记账与提示**（准点率），不做处罚：
        /// 排班实际离港与持仓时刻差在 (SlotToleranceMinutes, 30] 之间时，
        /// 仍可用这条槽，但记为「偏离」。
        /// </summary>
        public const int SlotOffsetToleranceMinutes = 30;

        /// <summary>该需求波窗口内的全部可选时刻（分钟数，升序，含窗口末刻）。</summary>
        public static List<int> SlotGrid(int band)
        {
            int i = Clamp(band);
            var list = new List<int>();
            int start = WindowStart[i];
            int end = WindowEnd[i];
            for (int m = start; m <= end; m += SlotGridStepMinutes) list.Add(m);
            // 窗口长度不是步长整数倍时，末刻单独补上（排班允许贴边起飞，槽也应可选）
            if (list.Count == 0 || list[list.Count - 1] != end) list.Add(end);
            return list;
        }

        /// <summary>
        /// 全天 15 分钟网格（96 个点，00:00–23:45）。
        ///
        /// ★2026-11-05 新增：槽的存在范围不再由 4 个需求波决定，而由**机场协调时段**决定，
        /// 而主协调机场是 24 小时全时段协调。故槽的身份一律落在这张全天网格上；
        /// 某机场能用哪些点，由 <c>RoutePermitLogic.QuartersOf</c> 按协调时段裁剪。
        /// </summary>
        public static List<int> SlotGrid24()
        {
            var list = new List<int>(96);
            for (int m = 0; m < 1440; m += SlotGridStepMinutes) list.Add(m);
            return list;
        }

        /// <summary>该窗口内的全部可选时刻（字符串 "HH:mm"）。</summary>
        public static List<string> SlotGridLabels(int band)
        {
            var grid = SlotGrid(band);
            var list = new List<string>(grid.Count);
            for (int i = 0; i < grid.Count; i++) list.Add(FormatClock(grid[i]));
            return list;
        }

        // ——————————— 时段（池口径）覆盖的整点区间（★2026-11-06 用户裁决）———————————
        //
        // 「需求波窗口」与「时段池的覆盖范围」是两件事，此前被当成了同一件：
        // 池容量按需求波窗口（早 06:30–09:30 等）算，于是**窗口外、但机场确实公布了容量**的刻
        // （06:00 / 06:15 / 09:45 / 10:00–11:15 / 14:15–16:15 …）既不显示、也取不到，
        // 而日容量把它们算在内 —— 枢纽 56 条日容量里只有 46 条拿得着。
        //
        // 用户原话：「只要能申请的时刻都要显示出来。」所以池的覆盖范围改按
        // **BandOfDep 的整点归属**（早 = 0–9 时、中 = 10–14 时、晚 = 15–19 时、夜 = 20–23 时），
        // 与 `BandOfClock` 完全同源：凡是能归到某个时段的刻，就在那个时段的池里，也就该显示。
        // 需求波窗口仍只用于排班偏好与需求系数（见类头注释）。

        /// <summary>该时段的起始小时（含）。</summary>
        /// <remarks>
        /// 早段写 06 而不是 0：00–05 时的小时容量在全网恒为 0（枢纽/干线表都是 0），
        /// 那里既没有公布槽也取不到，写成「早班 00:00–09:45」会让玩家以为半夜的刻被藏起来了。
        /// 时段归属仍由 <see cref="BandOfDep"/> 判（00–05 归早段，只是容量为 0、不出块），
        /// 本函数只负责**文案上的区间**。
        /// </remarks>
        public static int BandHourStart(int band)
            => band <= 0 ? 6 : band == 1 ? 10 : band == 2 ? 15 : 20;

        /// <summary>该时段的结束小时（含）。</summary>
        public static int BandHourEnd(int band)
            => band <= 0 ? 9 : band == 1 ? 14 : band == 2 ? 19 : 23;

        /// <summary>
        /// 全天网格里属于该时段的时刻（分钟数，升序）。
        /// 判据与 <see cref="BandOfDep"/> 同源，因此「这一块属于哪个时段」与「它从哪个池扣量」必然一致。
        /// </summary>
        public static List<int> BandGrid(int band)
        {
            int b = Clamp(band);
            // ★2026-11-07 性能：网格只取决于 band（常量表），旧版每次调用都要遍历
            // 96 个 15 分钟刻度、每格 FormatClock 拼一个字符串再比时段 ——
            // 「机场时刻」页一次刷新要调上千次，纯属重复劳动。这里按 band 缓存一份，
            // 返回**副本**以保证调用方可以随意改（既有语义不变）。
            var cache = _bandGridCache;
            if (cache == null) cache = BuildBandGridCache();
            return new List<int>(cache[b]);
        }

        static List<int>[] _bandGridCache;

        static List<int>[] BuildBandGridCache()
        {
            var cache = new List<int>[4];
            for (int b = 0; b < 4; b++)
            {
                var list = new List<int>();
                for (int m = 0; m < 1440; m += SlotGridStepMinutes)
                    if (BandOfDep(FormatClock(m)) == b) list.Add(m);
                cache[b] = list;
            }
            _bandGridCache = cache;
            return cache;
        }

        /// <summary>同上，字符串口径。</summary>
        public static List<string> BandGridLabels(int band)
        {
            int b = Clamp(band);
            var cache = _bandLabelCache;
            if (cache == null) cache = BuildBandLabelCache();
            return new List<string>(cache[b]);
        }

        static List<string>[] _bandLabelCache;

        static List<string>[] BuildBandLabelCache()
        {
            var cache = new List<string>[4];
            for (int b = 0; b < 4; b++)
            {
                var minutes = BandGrid(b);
                var list = new List<string>(minutes.Count);
                for (int i = 0; i < minutes.Count; i++) list.Add(FormatClock(minutes[i]));
                cache[b] = list;
            }
            _bandLabelCache = cache;
            return cache;
        }

        /// <summary>
        /// 该时段里**这个机场确实公布了容量**的时刻（`AirportCatalog.QuarterCapacity &gt; 0`）。
        ///
        /// 全天网格含 00:00–05:45，而容量表在这些小时恒为 0（不出槽）。夹具若拿 `grid[0]`
        /// 当「随便找一个时刻」，就会挑到 00:00：递单时池里有余量所以能过，交割时却取不到那条槽 ——
        /// 报出来像玩法坏了，其实是夹具挑了一个根本不存在的时刻。
        /// 卡片侧不用这个（`IndustryAirlineLogic.ChipsOf` 自己按容量过滤）；这是给夹具用的。
        /// </summary>
        public static List<string> BandGridClaimableLabels(string airport, int band)
        {
            var grid = BandGrid(band);
            var list = new List<string>(grid.Count);
            for (int i = 0; i < grid.Count; i++)
                if (AirlineCEO.Data.AirportCatalog.QuarterCapacity(airport, grid[i]) > 0)
                    list.Add(FormatClock(grid[i]));
            return list;
        }

        /// <summary>
        /// 把任意时刻吸附到**最近的系列时刻**。
        ///
        /// ★2026-11-05：口径改为**全天 15 分钟网格的最近点**（即四舍五入到最近的刻钟）。
        /// 改动前是「四个需求波窗口网格的并集里最近的」—— 于是 10:00 这种落在波间低谷的
        /// 时刻会被吸到 09:30，而 10:05 会被吸到 11:30（跨了一个半小时）。现实里主协调机场
        /// 24 小时全时段协调，10:00 本来就是一条合法系列时刻，没有理由被搬走。
        ///
        /// 为什么需要吸附：**经停端/对端的离场时刻是推导出来的**（去程 + 轮挡 + 过站），
        /// 例如 12:00 去程落回后回程是 17:33 —— 17:33 不在网格上，而时刻槽的身份必须是
        /// 网格上的系列时刻（机场从不公布 17:33 的系列）。现实里航司是把过站时刻按获批槽
        /// 时刻**排定**（槽批的是 17:30，回程就排 17:30），本函数即这件事的算法。
        /// 入参已是网格点则原样返回（幂等）。
        /// </summary>
        public static string NearestSeries(string clock)
        {
            int m = ParseClock(clock);
            return m < 0 ? clock : FormatClock(NearestSeriesOf(m));
        }

        /// <summary>同上，返回分钟数（四舍五入到最近的 15 分钟点，24 小时内环状取近）。</summary>
        public static int NearestSeriesOf(int minutes)
        {
            int m = ((minutes % 1440) + 1440) % 1440;
            int step = SlotGridStepMinutes;
            int down = m / step * step;
            int up = down + step;
            int dDown = m - down;
            int dUp = up - m;
            // up 到了 1440 表示次日 00:00，环状距离与 down 比
            if (up >= 1440) return dDown <= 1440 - up ? down : 0;
            return dUp < dDown ? up : down;
        }

        /// <summary>`"HH:mm"` → 分钟数；解析不出返回 −1（不抛异常，脏存档不炸）。</summary>
        public static int ParseClock(string clock)
        {
            if (string.IsNullOrEmpty(clock)) return -1;
            int colon = clock.IndexOf(':');
            if (colon < 1) return -1;
            // ★2026-11-07 性能：旧版 `int.TryParse(clock.Substring(...))` 每次调用分配两个子串。
            // 这个函数在「机场时刻」页一次刷新里要被调上百万次（逐刻 × 逐条持仓），
            // 光是子串分配就是几百 MB 的垃圾。改为原地解析，语义与 TryParse 一致：
            // 允许前导空白与正负号，尾部出现非空白非数字即判失败。
            int h = ParseIntRange(clock, 0, colon);
            if (h < 0) return -1;
            int m = ParseIntRange(clock, colon + 1, clock.Length);
            if (m < 0) return -1;
            if (h > 23 || m > 59) return -1;
            return h * 60 + m;
        }

        /// <summary>解析 <c>s</c> 的 [from, to) 区间为整数；不合法返回 −1。不分配任何中间字符串。</summary>
        static int ParseIntRange(string s, int from, int to)
        {
            int i = from;
            while (i < to && (s[i] == ' ' || s[i] == '\t')) i++;
            int sign = 1;
            if (i < to && (s[i] == '+' || s[i] == '-'))
            {
                if (s[i] == '-') sign = -1;
                i++;
            }
            if (i >= to) return -1;
            long v = 0;
            int digits = 0;
            while (i < to)
            {
                char c = s[i];
                if (c < '0' || c > '9') break;
                v = v * 10 + (c - '0');
                if (v > int.MaxValue) return -1;
                digits++;
                i++;
            }
            if (digits == 0) return -1;
            while (i < to && (s[i] == ' ' || s[i] == '\t')) i++;
            if (i < to) return -1;          // 尾部有杂质：与 TryParse 一样判失败
            return (int)(v * sign);
        }

        /// <summary>网格中**最接近** minutes 的时刻（用于把实际离港时刻归到某个系列）。</summary>
        public static int SnapToGrid(int band, int minutes)
        {
            // ★2026-11-06：网格与卡片、池容量同源（`BandGrid`，按整点归属铺满全天）。
            var grid = BandGrid(band);
            if (grid.Count == 0) return minutes;
            int m = ((minutes % 1440) + 1440) % 1440;
            int best = grid[0], bestD = int.MaxValue;
            for (int i = 0; i < grid.Count; i++)
            {
                int d = grid[i] > m ? grid[i] - m : m - grid[i];
                if (d < bestD) { bestD = d; best = grid[i]; }
            }
            return best;
        }

        /// <summary>网格中**不小于** minutes 的最早时刻；时段内没有则返回 −1。</summary>
        public static int SnapUpToGrid(int band, int minutes)
        {
            // ★2026-11-06：同上，`BandGrid`。
            var grid = BandGrid(band);
            int m = ((minutes % 1440) + 1440) % 1440;
            for (int i = 0; i < grid.Count; i++)
                if (grid[i] >= m) return grid[i];
            return -1;
        }

        /// <summary>该时刻与其所属窗口的时段下标（字符串口径；解析不出按早班）。</summary>
        public static int BandOfClock(string clock)
        {
            int m = ParseClock(clock);
            return m < 0 ? 0 : BandOfDep(FormatClock(m));
        }

        /// <summary>两个时刻的偏差（分钟，取绝对值的环状差）。</summary>
        public static int ClockDiff(string a, string b)
        {
            int ma = ParseClock(a), mb = ParseClock(b);
            if (ma < 0 || mb < 0) return int.MaxValue;
            int d = ma > mb ? ma - mb : mb - ma;
            return d > 720 ? 1440 - d : d;
        }

        /// <summary>「机场 时刻」一行式（时刻槽的一切文案都走它，避免各处手拼）。</summary>
        public static string SlotLabel(string airport, string clock)
            => string.IsNullOrEmpty(clock) ? (airport ?? "") : $"{airport} {clock}";

        /// <summary>「机场 时刻（早班）」——只在需要点时段的场合用（如池容量说明）。</summary>
        public static string SlotLabelWithBand(string airport, string clock)
        {
            int m = ParseClock(clock);
            if (m < 0) return airport ?? "";
            return $"{airport} {FormatClock(m)}（{SlotBandName(BandOfDep(FormatClock(m)))}班）";
        }

        /// <summary>
        /// 旧档迁移/口径折算：把「早/中/晚/夜」这类时段名折成该时段网格上的一个具体时刻。
        /// 取窗口的**峰点**（08:00/12:00/18:00/22:00）—— 与 `DepTimes` 同源，不与旧排班口径分叉。
        /// 传进来的若已是 `HH:mm` 则原样返回（幂等）。
        /// </summary>
        public static string BandNameToClock(string bandName)
        {
            if (string.IsNullOrEmpty(bandName)) return DepTimes[0];
            if (ParseClock(bandName) >= 0) return bandName;      // 已是时刻
            for (int i = 0; i < SlotBands.Length; i++)
                if (SlotBands[i] == bandName) return DepTimes[i];
            for (int i = 0; i < Labels.Length; i++)
                if (Labels[i] == bandName) return DepTimes[i];    // 早班/午班/晚班/**红眼**
            if (bandName.Length >= 2 && bandName[bandName.Length - 1] == '班')
            {
                string head = bandName.Substring(0, bandName.Length - 1);
                for (int i = 0; i < Labels.Length; i++)
                    if (Labels[i] == bandName || Labels[i].Substring(0, 1) == head) return DepTimes[i];
            }
            return DepTimes[0];
        }

        /// <summary>
        /// 入参是不是「能用」的时刻或时段名（`"07:00"` / `"早班"` / `"早"`）。
        /// ★2026-10-15：`BandNameToClock` 对脏输入会静默折成 08:00 —— 界面误传时那是静默错排班，
        /// 所以入口处先用本函数把非法入参挡下（存档解码这类「脏也得读出个结果」的地方才允许兜底）。
        /// </summary>
        public static bool IsKnownTimeOrBand(string timeOrLabel)
        {
            if (string.IsNullOrEmpty(timeOrLabel)) return false;
            if (ParseClock(timeOrLabel) >= 0) return true;
            for (int i = 0; i < SlotBands.Length; i++)
                if (SlotBands[i] == timeOrLabel) return true;
            if (timeOrLabel.Length >= 2 && timeOrLabel[timeOrLabel.Length - 1] == '班')
            {
                string head = timeOrLabel.Substring(0, timeOrLabel.Length - 1);
                for (int i = 0; i < Labels.Length; i++)
                    if (Labels[i] == timeOrLabel || Labels[i].Substring(0, 1) == head) return true;
            }
            return false;
        }

        /// <summary>某时刻是否落在指定需求波窗口内。</summary>
        public static bool InWindow(int band, int minutes)
        {
            int i = Clamp(band);
            int m = ((minutes % 1440) + 1440) % 1440;
            return m >= WindowStart[i] && m <= WindowEnd[i];
        }

        /// <summary>把时刻收进指定窗口（超出则贴边），供编表时保证落在需求波内。</summary>
        public static int SnapIntoWindow(int band, int minutes)
        {
            int i = Clamp(band);
            int m = ((minutes % 1440) + 1440) % 1440;
            if (m < WindowStart[i]) return WindowStart[i];
            if (m > WindowEnd[i]) return WindowEnd[i];
            return m;
        }

        /// <summary>分钟数 → HH:mm。</summary>
        public static string FormatClock(int minutes)
        {
            int m = ((minutes % 1440) + 1440) % 1440;
            // ★2026-11-07 性能：旧版用插值字符串格式化，每次调用分配两个装箱 + 一个 string。
            // 「机场时刻」页（4 段 × 13 刻 × 80 机场，还要乘上排班/闸门里的调用）把它推到
            // 十万级调用量。1440 个刻度是有限的常量集合，直接查表，零分配。
            var table = _clockText;
            if (table == null) table = BuildClockText();
            return table[m];
        }

        static string[] _clockText;

        static string[] BuildClockText()
        {
            var table = new string[1440];
            for (int i = 0; i < 1440; i++) table[i] = $"{i / 60:00}:{i % 60:00}";
            _clockText = table;
            return table;
        }

        static int Clamp(int band) => band < 0 ? 0 : band > 3 ? 3 : band;

        public static float DemandOf(string label)
        {
            int i = IndexOf(label);
            return i >= 0 && i < Demands.Length ? Demands[i] : 1f;
        }

        /// <summary>
        /// 需求乘数的**时刻口径**（★2026-10-15）：传 `"HH:mm"` 时先归到所属需求波再取系数，
        /// 传时段名时按旧口径取。混用两套口径会让「07:00 的班」一律吃到早班峰点的系数。
        /// </summary>
        public static float DemandOfTime(string timeOrLabel)
        {
            int m = ParseClock(timeOrLabel);
            if (m < 0) return DemandOf(timeOrLabel);
            int b = BandOfDep(FormatClock(m));
            return b >= 0 && b < Demands.Length ? Demands[b] : 1f;
        }

        public static string DepOf(string label)
        {
            int i = IndexOf(label);
            return i >= 0 && i < DepTimes.Length ? DepTimes[i] : "08:00";
        }

        /// <summary>由起飞时刻推断时段标签（用于回填）。</summary>
        public static string LabelForDep(string dep)
        {
            if (string.IsNullOrEmpty(dep)) return Morning;
            for (int i = 0; i < DepTimes.Length; i++)
                if (DepTimes[i] == dep) return Labels[i];
            int h = 0;
            if (dep.Length >= 2 && int.TryParse(dep.Substring(0, 2), out int parsed)) h = parsed;
            if (h < 10) return Morning;
            if (h < 15) return Noon;
            if (h < 20) return Evening;
            return RedEye;
        }
    }
}
