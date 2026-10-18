using System.Collections.Generic;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航线时段预设（早/午/晚/红眼）。供航线分配（n98）、排班调整（S4）共用。
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
        public static readonly int[] WindowStart = { 6 * 60 + 30, 11 * 60 + 30, 16 * 60 + 30, 20 * 60 };
        public static readonly int[] WindowEnd = { 9 * 60 + 30, 14 * 60, 20 * 60, 23 * 60 };

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

        // ————————————————— 时刻槽的时间网格（★2026-10-15）—————————————————
        //
        // 现实锚点：WASG 1.7.2(d)「航司不得故意在明显不同于获批时刻的时间运行」；1.7.2(e)
        // 「一个系列 = 同一（或约同一）时刻、同一星期几、整季规律分布的 ≥5 个时刻」。
        // 所以「买哪个时段」这种问法在现实里根本不存在 —— 协调人分配的是**具体时刻**。
        // 本作把它落成一张 30 分钟步长、落在需求波窗口内的网格：早班即
        // 06:30 / 07:00 / 07:30 / 08:00 / 08:30 / 09:00 / 09:30。

        /// <summary>时刻槽网格的步长（分钟）。</summary>
        public const int SlotGridStepMinutes = 30;

        /// <summary>
        /// 一条持仓可服务的班次，其离港时刻与持仓时刻的最大偏差（分钟）。
        /// 排班器按 5 分钟向上取整产出时刻，故容差要能吃掉这点抖动；取 15 分钟同时保证
        /// 「06:30 的槽接不了 07:00 的班」——这正是「一槽一班」在时刻上的量化。
        /// </summary>
        public const int SlotToleranceMinutes = 15;

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

        /// <summary>该窗口内的全部可选时刻（字符串 "HH:mm"）。</summary>
        public static List<string> SlotGridLabels(int band)
        {
            var grid = SlotGrid(band);
            var list = new List<string>(grid.Count);
            for (int i = 0; i < grid.Count; i++) list.Add(FormatClock(grid[i]));
            return list;
        }

        /// <summary>
        /// 把任意时刻吸附到**最近的系列时刻**：四个需求波窗口 30 分钟网格的并集
        /// （06:30/07:00/…/09:30、11:30/…/14:00、16:30/…/20:00、20:00/…/23:00）。
        ///
        /// 为什么需要它：**经停端/对端的离场时刻是推导出来的**（去程 + 轮挡 + 过站），例如 12:00
        /// 去程落回后回程是 17:33 —— 17:33 不在任何网格点上，而时刻槽的身份必须是网格上的
        /// 系列时刻（机场从不公布 17:33 的系列）。现实里航司是把过站时刻按获批槽时刻**排定**
        /// （槽批的是 17:30，回程就排 17:30），本函数即这件事的算法：吸附到 17:30 只需延后 3 分钟。
        ///
        /// 落在需求波之间的低谷（如 15:00）时取最近的窗口边缘 —— 旧口径是把整个时段并进
        /// 该时段池（粒度更粗），吸附后粒度细到 30 分钟，仍是同一件事，只是不再含糊。
        /// 入参已是网格点则原样返回（幂等）。
        /// </summary>
        public static string NearestSeries(string clock)
        {
            int m = ParseClock(clock);
            return m < 0 ? clock : FormatClock(NearestSeriesOf(m));
        }

        /// <summary>同上，返回分钟数。</summary>
        public static int NearestSeriesOf(int minutes)
        {
            int m = ((minutes % 1440) + 1440) % 1440;
            int best = -1, bestD = int.MaxValue;
            for (int b = 0; b < SlotBands.Length; b++)
            {
                var grid = SlotGrid(b);
                for (int k = 0; k < grid.Count; k++)
                {
                    int d = grid[k] > m ? grid[k] - m : m - grid[k];
                    if (d > 720) d = 1440 - d;
                    if (d < bestD) { bestD = d; best = grid[k]; }
                }
            }
            return best < 0 ? m : best;
        }

        /// <summary>`"HH:mm"` → 分钟数；解析不出返回 −1（不抛异常，脏存档不炸）。</summary>
        public static int ParseClock(string clock)
        {
            if (string.IsNullOrEmpty(clock)) return -1;
            int colon = clock.IndexOf(':');
            if (colon < 1) return -1;
            if (!int.TryParse(clock.Substring(0, colon), out int h)) return -1;
            if (!int.TryParse(clock.Substring(colon + 1), out int m)) return -1;
            if (h < 0 || h > 23 || m < 0 || m > 59) return -1;
            return h * 60 + m;
        }

        /// <summary>网格中**最接近** minutes 的时刻（用于把实际离港时刻归到某个系列）。</summary>
        public static int SnapToGrid(int band, int minutes)
        {
            var grid = SlotGrid(band);
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

        /// <summary>网格中**不小于** minutes 的最早时刻；窗口内没有则返回 −1。</summary>
        public static int SnapUpToGrid(int band, int minutes)
        {
            var grid = SlotGrid(band);
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
            return $"{m / 60:00}:{m % 60:00}";
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
