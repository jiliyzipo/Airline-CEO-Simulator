using System;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航线班次模型（唯一权威口径）：由「两端市场需求 × 枢纽规模 × 机型座级 × 航距衰减 × 机型日利用率上限」
    /// 推出每周班次（<see cref="EconomyBalanceParams.FreqMinWeekly"/>–<see cref="EconomyBalanceParams.FreqMaxWeekly"/>），
    /// 并给出与之自洽的班期字符串。
    ///
    /// 取代历史上散落各处的硬编码 <c>km &gt;= 5000 ? 4 : 7</c>：
    ///   · 接管/预设种子航线（TakeoverWorldBuilder.BuildRoutes）
    ///   · 排班草案与多波重排（OpsScheduleLogic.ApplyDraft / ApplyWaves）
    ///   · 游戏中新开航线（ActionExecutor / ExpansionProposalManager）
    ///
    /// 设计要点：班次不再由一条 5000 km 硬阈值一刀切，而是
    /// <list type="number">
    /// <item>需求侧：两端吞吐几何均值 × OD 吸引力 × 距离衰减 → 单向日客流；</item>
    /// <item>供给侧：座级 × 目标客座率 → 所需日班次；</item>
    /// <item>物理约束：单机日利用率目标 ÷ 单次往返耗时 → 单机日班次上限。</item>
    /// </list>
    /// 两者取小后量化到「1–28 班」的可用班期阶梯，因此短程干线可得 14/21 班，超远程只得 1–3 班，
    /// 且不会再出现「同一批远程线里只有一条是 7 班」这类边界怪象。
    ///
    /// 纯 C#（不依赖 UnityEngine），Unity 与 tools/balance 可共用同一份实现。
    /// </summary>
    public static class RouteFrequencyModel
    {
        /// <summary>班次测算结果。</summary>
        public struct Frequency
        {
            /// <summary>每周班次（1–28）。</summary>
            public int weeklyFlights;
            /// <summary>与 <see cref="weeklyFlights"/> 自洽的班期（各波次并集）。</summary>
            public string daysOfWeek;
            /// <summary>每日波次数量（1–4）。</summary>
            public int waves;
            /// <summary>诊断用：单向日客流需求（人）。</summary>
            public float demandPaxPerDay;
            /// <summary>诊断用：满足需求所需的日班次。</summary>
            public float flightsByDemand;
            /// <summary>诊断用：单机日利用率允许的日班次上限。</summary>
            public float flightsByAircraft;

            public bool valid => weeklyFlights > 0;
        }

        /// <summary>单程轮挡（分钟）。超远程按 6 小时下限兜底（与排班口径一致）。</summary>
        public static int BlockMinutes(float distanceKm, string aircraftType)
        {
            float block = OpsEconomics.BlockHours(distanceKm);
            if (distanceKm >= 5000f) block = Math.Max(block, 6f);
            return (int)Math.Round(block * 60f);
        }

        /// <summary>按航线字段测算班次。</summary>
        public static Frequency ForRoute(RouteData route)
            => route == null
                ? default
                : Compute(route.origin, route.destination,
                    route.distanceKm > 0f ? route.distanceKm : 1000f, route.aircraftType);

        /// <summary>
        /// 核心测算：需求侧与供给侧取小，再量化到可用班期阶梯。
        /// </summary>
        public static Frequency Compute(string origin, string dest, float distanceKm, string aircraftType)
        {
            float d = distanceKm > 0f ? distanceKm : 1000f;

            // —— 1. 需求侧 ——
            float demandPaxPerDay = DemandPaxPerDay(origin, dest, d);

            int seats = Math.Max(1, EconomyBalanceParams.DefaultSeatsFor(aircraftType));
            float load = Math.Max(0.1f, EconomyBalanceParams.DefaultLoadFactor);
            float flightsByDemand = demandPaxPerDay / (seats * load);

            // —— 2. 供给侧：单机日利用率目标 ÷ 单次往返耗时 ——
            float roundTripHours = (2f * BlockMinutes(d, aircraftType) + EconomyBalanceParams.BaseTurnMinutes) / 60f;
            float flightsByAircraft = EconomyBalanceParams.TargetDailyUtilizationHours / Math.Max(0.5f, roundTripHours);

            float daily = Math.Min(flightsByDemand, flightsByAircraft);
            int weekly = ClampWeekly((int)Math.Round(daily * 7f, MidpointRounding.AwayFromZero));

            var f = new Frequency
            {
                weeklyFlights = weekly,
                waves = WavesOf(weekly).Length,
                demandPaxPerDay = (float)Math.Round(demandPaxPerDay, 1),
                flightsByDemand = (float)Math.Round(flightsByDemand, 3),
                flightsByAircraft = (float)Math.Round(flightsByAircraft, 3)
            };
            f.daysOfWeek = DaysForTotal(weekly);
            return f;
        }

        /// <summary>
        /// 需求侧：单向日客流 = √(吞吐A × 吞吐B) × OD 吸引力 × 距离衰减 × 强度系数。
        /// 目录缺失时用 <see cref="EconomyBalanceParams.FreqUnknownPaxMillion"/> 等默认值兜底。
        /// </summary>
        public static float DemandPaxPerDay(string origin, string dest, float distanceKm)
        {
            var a = CityEconomyCatalog.Find(origin);
            var b = CityEconomyCatalog.Find(dest);

            float paxA = Usable(a != null ? a.paxMillion : 0f, EconomyBalanceParams.FreqUnknownPaxMillion);
            float paxB = Usable(b != null ? b.paxMillion : 0f, EconomyBalanceParams.FreqUnknownPaxMillion);
            float odA = Usable(a != null ? a.odIndex : 0f, EconomyBalanceParams.FreqUnknownOdIndex);
            float odB = Usable(b != null ? b.odIndex : 0f, EconomyBalanceParams.FreqUnknownOdIndex);

            float market = (float)Math.Sqrt(paxA * paxB);              // 两地市场规模（百万吞吐几何均值）
            float appeal = (odA + odB) * 0.5f / 100f;                   // 0–1：OD 吸引力
            float decay = 1f / (1f + distanceKm / EconomyBalanceParams.FreqDemandDecayKm);

            return market * appeal * decay * EconomyBalanceParams.FreqDemandPaxScale;
        }

        static float Usable(float v, float fallback) => v > 0f ? v : fallback;

        /// <summary>把任意班次夹到 1–28。</summary>
        public static int ClampWeekly(int weekly)
        {
            if (weekly < EconomyBalanceParams.FreqMinWeekly) return EconomyBalanceParams.FreqMinWeekly;
            if (weekly > EconomyBalanceParams.FreqMaxWeekly) return EconomyBalanceParams.FreqMaxWeekly;
            return weekly;
        }

        /// <summary>
        /// 每周班次 → 各波次每周班次（每波 ≤ 7，Σ = 总班次）。
        /// 例：7 → [7]；10 → [7,3]；14 → [7,7]；28 → [7,7,7,7]；
        /// 班次不足 7 时单波承载（3 → [3]）。
        /// </summary>
        public static int[] WavesOf(int weekly)
        {
            int total = ClampWeekly(weekly);
            int full = total / 7;
            int rem = total % 7;
            var waves = new int[Math.Max(1, full + (rem > 0 ? 1 : 0))];
            int i = 0;
            for (int k = 0; k < full; k++) waves[i++] = 7;
            if (rem > 0) waves[i] = rem;
            return waves;
        }

        /// <summary>某一波次的班期字符串（每日满 7 时返回「每日」）。</summary>
        public static string DaysForWaveCount(int count)
        {
            switch (count)
            {
                case 1: return "周一";
                case 2: return "周二/五";
                case 3: return "周一/三/五";
                case 4: return "周一/三/五/日";
                // 注意：FlightScheduleFormat.DaysOf 只按单个「日」字扫描，不支持「一至五」这类区间写法，
                // 必须逐日列出，否则 5/6 天会被误判成 2 天。
                case 5: return "周一/二/三/四/五";
                case 6: return "周一/二/三/四/五/六";
                default: return "每日";
            }
        }

        /// <summary>整条航线的班期（各波次并集）。</summary>
        public static string DaysForTotal(int weekly)
        {
            var waves = WavesOf(weekly);
            if (waves.Length == 1) return DaysForWaveCount(waves[0]);
            var union = new bool[7];
            for (int i = 0; i < waves.Length; i++)
            {
                var days = FlightScheduleFormat.DaysOf(DaysForWaveCount(waves[i]), waves[i]);
                for (int k = 0; k < 7; k++) union[k] |= days[k];
            }
            return FlightScheduleFormat.DaysSummary(union);
        }

        /// <summary>班期字符串对应的每周天数（「每日」= 7）。</summary>
        public static int DayCount(string daysOfWeek)
        {
            var days = FlightScheduleFormat.DaysOf(daysOfWeek, 0);
            int n = 0;
            for (int i = 0; i < days.Length; i++) if (days[i]) n++;
            return n;
        }

        /// <summary>
        /// 尊重调用方给定的班期：天数与目标周班次一致时原样保留，否则回落到规范班期字符串。
        /// 供「CEO 已选班期」的场景不被模型覆盖。
        /// </summary>
        public static string ReconcileDays(string daysOfWeek, int weeklyCount)
        {
            if (!string.IsNullOrEmpty(daysOfWeek) && DayCount(daysOfWeek) == weeklyCount)
                return daysOfWeek;
            return DaysForWaveCount(weeklyCount);
        }

        /// <summary>
        /// 把总班次摊到实际可用的波次上（每波 1–7 班），供排班器按「实际排得出的波数」收敛。
        /// </summary>
        public static int PerWaveWeekly(int totalWeekly, int waves)
        {
            if (waves <= 0) return 0;
            int each = (int)Math.Round(ClampWeekly(totalWeekly) / (float)waves, MidpointRounding.AwayFromZero);
            if (each < 1) each = 1;
            if (each > 7) each = 7;
            return each;
        }

        /// <summary>第 i 个波次的建议起飞时刻（0–3 循环早/午/晚/红眼）。</summary>
        public static string WaveDeparture(int waveIndex)
        {
            var deps = ScheduleSlots.DepTimes;
            if (deps == null || deps.Length == 0) return "08:00";
            int i = ((waveIndex % deps.Length) + deps.Length) % deps.Length;
            return deps[i];
        }

        /// <summary>
        /// 按模型写回航线的班次字段，并把 <c>flights</c> 物化成与班次、班期自洽的波次列表。
        /// 单波且已有班期/时刻时保持原有值；多波时按早/午/晚/红眼铺开。
        /// </summary>
        public static Frequency Materialize(RouteData route)
        {
            if (route == null) return default;

            var f = ForRoute(route);
            int block = BlockMinutes(route.distanceKm > 0f ? route.distanceKm : 1000f, route.aircraftType);
            var waves = WavesOf(f.weeklyFlights);

            // 毛贡献均摊到各波次：保证 Σflights.dailyProfit == GrossDailyProfit，
            // 使 RecalcDailyProfit() 幂等（固定成本只在航线层级扣一次）。
            float share = route.GrossDailyProfit / Math.Max(1, waves.Length);

            var list = new System.Collections.Generic.List<RouteFlight>(waves.Length);
            for (int i = 0; i < waves.Length; i++)
            {
                string dep = waves.Length == 1 && !string.IsNullOrEmpty(route.depTime)
                    ? route.depTime
                    : WaveDeparture(i);
                int depMin = LiveClockLogic.ParseMinutes(dep);
                int arrMin = (depMin + block) % (24 * 60);

                list.Add(new RouteFlight
                {
                    registration = route.registration,
                    depTime = dep,
                    arrTime = ScheduleSlots.FormatClock(arrMin),
                    slotLabel = ScheduleSlots.LabelForDep(dep),
                    daysOfWeek = DaysForWaveCount(waves[i]),
                    weeklyFlights = waves[i],
                    // R4 双口径：报到时刻按「起飞 − 报到提前量」，与单班路径同一口径。
                    dutyStart = depMin - OpsRotationParams.ReportBeforeMinutes,
                    dailyProfit = share
                });
            }

            route.flights = list;
            route.weeklyFlights = f.weeklyFlights;
            route.daysOfWeek = f.daysOfWeek;
            route.depTime = list[0].depTime;
            route.arrTime = list[0].arrTime;
            return f;
        }

        /// <summary>一行式诊断文案，供工具/邮件展示模型推算依据。</summary>
        public static string Explain(string origin, string dest, float distanceKm, string aircraftType)
        {
            var f = Compute(origin, dest, distanceKm, aircraftType);
            return $"{origin}-{dest} {distanceKm:F0}km {aircraftType}：" +
                   $"需求 {f.demandPaxPerDay:F1} 人/日 → 需 {f.flightsByDemand:F2} 班/日；" +
                   $"单机上限 {f.flightsByAircraft:F2} 班/日 → 每周 {f.weeklyFlights} 班（{f.daysOfWeek}，{f.waves} 波）";
        }
    }
}
