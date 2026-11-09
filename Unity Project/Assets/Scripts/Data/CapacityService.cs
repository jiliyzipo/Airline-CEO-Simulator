using System;
using AirlineCEO.Logic;

namespace AirlineCEO.Data
{
    /// <summary>
    /// 容量口径档位。**只增不改**：每一档都是对某一版「怎么算容量」的完整命名，
    /// 便于存档 / 报表 / 回归各自声明自己按哪一档读取。
    /// </summary>
    public enum CapacityProfile
    {
        /// <summary>
        /// C0 · 跑道数平铺（2026-11-09 用户裁决）。
        /// 协调时段内、且不在宵禁中的每个 15 分钟刻 = <see cref="CapacityService.SlotsPerRunwayPerQuarter"/> × 等效跑道数。
        /// **无峰谷**，全天同值。
        /// </summary>
        C0_FlatRunway = 0,
    }

    /// <summary>
    /// 时刻容量的**唯一来源**。
    ///
    /// 为什么要有这一层（2026-11-09 批次一首批，用户与 ChatGPT 双方确认的边界）：
    /// 容量此前散落在 <see cref="AirportCatalog"/> 的 `QuarterCapacityRaw` / `BandCapacity` /
    /// `HourCapacity` / `DayCapacity` 四处，各自摸同一套底表。日后要加「航季 / 事件 / 第二旋钮」
    /// 时，任何一个调用点漏改都会让表格与玩法分叉（浮窗显示能申请、闸门却拒绝）。
    /// 抽取后**所有**容量数字都从这里出，AirportCatalog 只做兼容转发与缓存。
    ///
    /// ★本批（C0）的硬约束 —— 改动此处即等于改玩法，必须走验收：
    ///   ① 参数固定：跑道数 × 1，不引入除数、不引入初始占用率；
    ///   ② 布局固定：时段划分仍由 <see cref="ScheduleSlots.BandOfDep"/> 决定（早/中/晚/夜）；
    ///   ③ `kind` 只贯通、**不参与返回值**：DEP 与 ARR 同数，这是现行语义，不是遗漏；
    ///   ④ 输出必须与抽取前逐格全等：104 机场 × 96 刻 × 2 方向 = 19,968 格，
    ///      由 `CapacityServiceRegression` 用**抽取前的冻结公式**逐格比对。
    ///
    /// 为什么 <c>Quarter</c> 有一个显式收 tier / coordWindow 的重载：
    /// <see cref="AirportCatalog"/> 的静态构造期需要算容量，而那时 `AirportInfo.tier` /
    /// `coordWindow` 还在一行一行写入数组，<c>FindByCode</c> 读到的是**半成品**。
    /// 所以构造期必须走「把 tier / coordWindow 明着传进来」的原始入口，不能靠反查。
    /// </summary>
    public static class CapacityService
    {
        /// <summary>
        /// 一条跑道每 15 分钟放出几条槽（到港池同数）。
        ///
        /// ★2026-11-09 用户裁决为 `1`：跑道数就是每刻容量。注意**不要靠调小本值来压缩池子**
        /// （1 → 0 会让全表归零）；真要收紧，应改成「每 N 条跑道 1 条槽」的取整式。
        /// </summary>
        public const int SlotsPerRunwayPerQuarter = 1;

        /// <summary>当前生效的容量口径档位。C0 是本批唯一的合法值。</summary>
        public const CapacityProfile Active = CapacityProfile.C0_FlatRunway;

        // ————————————————— 唯一算法本体 —————————————————

        /// <summary>
        /// 原始入口：不反查机场目录，tier / coordWindow 由调用方给。
        /// **静态构造期只能调这一个**（见类注释）。
        /// </summary>
        public static int Quarter(string code, int tier, string coordWindow, int minutes)
        {
            if (string.IsNullOrEmpty(coordWindow)) return 0;        // 非协调：不设时刻槽
            int m = ((minutes % 1440) + 1440) % 1440;
            if (!AirportCatalog.InCoordWindowText(coordWindow, m)) return 0;   // 协调时段外：免槽
            if (AirportCatalog.InCurfewWindow(code, m)) return 0;    // 宵禁：物理禁飞，记 0

            // C0：每刻 = 等效跑道数 × 每跑道槽数。未来航季 / 事件生效应在此处按
            //      「上下文（游戏时间 + 容量版本）」分支，而不是散回各调用点。
            return AirportCatalog.RunwayCountFor(code, tier) * SlotsPerRunwayPerQuarter;
        }

        /// <summary>常规入口：按机场代码反查等级与协调时段。</summary>
        public static int Quarter(string code, int minutes)
            => Quarter(code, AirportCatalog.TierOfCode(code), AirportCatalog.CoordWindowOf(code), minutes);

        /// <summary>
        /// 某机场早/中/晚/夜四个时段池的容量（下标 0–3）。
        ///
        /// 池容量 = 该时段**全天网格**上各刻容量之和，网格由
        /// <see cref="ScheduleSlots.BandGrid"/> 给出（与「这一刻从哪个池扣量」同源）。
        /// 因此四段之和必然等于 <see cref="Day"/>。
        /// </summary>
        public static int[] Pools(string code, int tier, string coordWindow)
        {
            if (string.IsNullOrEmpty(coordWindow)) return new[] { 0, 0, 0, 0 };
            var r = new int[4];
            for (int band = 0; band < 4; band++)
            {
                int total = 0;
                var grid = ScheduleSlots.BandGrid(band);
                for (int i = 0; i < grid.Count; i++)
                    total += Quarter(code, tier, coordWindow, grid[i]);
                r[band] = total;
            }
            return r;
        }

        /// <summary>同上，按机场代码反查。</summary>
        public static int[] Pools(string code)
            => Pools(code, AirportCatalog.TierOfCode(code), AirportCatalog.CoordWindowOf(code));

        /// <summary>
        /// 某时刻**某一类**时刻池（DEP / ARR）的逐刻容量。
        ///
        /// ★`kind` 目前只贯通、不改变数量：两个池取同一个数，这是现行语义。
        /// 起点是「一条槽 = 一进一出」，公开资料只发布起降合计，没有分端实测就按比例劈开
        /// 等于发明数据。接口留在此处，等拿到分端实测再逐场覆盖 —— 届时只改本方法，
        /// 调用点（<see cref="AirportCatalog.SlotCapacity(string,int,string)"/> 等）无需变动。
        /// </summary>
        public static int Quarter(string code, int minutes, string kind)
        {
            _ = kind;   // C0：方向不分池，DEP 与 ARR 同数
            return Quarter(code, minutes);
        }

        /// <summary>某小时的容量 = 该小时四个刻之和（非协调 / 全宵禁小时为 0）。</summary>
        public static int Hour(string code, int hour)
        {
            if (hour < 0 || hour > 23) return 0;
            int step = ScheduleSlots.SlotGridStepMinutes;
            int n = 0;
            for (int q = 0; q < 60; q += step) n += Quarter(code, hour * 60 + q);
            return n;
        }

        /// <summary>该机场一天的容量（24 小时容量之和；非协调机场 0）。</summary>
        public static int Day(string code)
        {
            int n = 0;
            for (int h = 0; h < 24; h++) n += Hour(code, h);
            return n;
        }

        /// <summary>该机场一天能放槽的刻数（容量 > 0 的 15 分钟刻）。</summary>
        public static int OpenQuarterCount(string code)
        {
            int n = 0;
            for (int m = 0; m < 1440; m += ScheduleSlots.SlotGridStepMinutes)
                if (Quarter(code, m) > 0) n++;
            return n;
        }
    }
}
