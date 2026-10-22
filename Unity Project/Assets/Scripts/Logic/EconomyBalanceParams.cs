namespace AirlineCEO.Logic
{
    /// <summary>
    /// Phase 1.3 经济平衡参数表（纯 C#，供 Unity 与 tools/balance 共用）。
    /// 单位约定：货币字段凡未注明均为「万美元」；油价为「美元/吨」。
    /// </summary>
    public static class EconomyBalanceParams
    {
        // —— 票价 / 需求 ——
        /// <summary>经济舱 Yield（美元 / RPK）。行业约 0.05–0.10。</summary>
        public const float YieldEconomyUsdPerRpk = 0.072f;
        /// <summary>商务舱 Yield（美元 / RPK）。</summary>
        public const float YieldBusinessUsdPerRpk = 0.145f;
        /// <summary>默认商务舱座位占比。</summary>
        public const float DefaultBusinessSeatShare = 0.12f;
        /// <summary>品牌净值每 100 点带来的溢价上限。</summary>
        public const float BrandPremiumMax = 0.08f;
        /// <summary>竞对运力对票价/客流的最大折价。</summary>
        public const float CompetitorDiscountMax = 0.22f;

        // —— 油耗（吨 / 千公里，按机型粗分）——
        public const float FuelBurnNarrowbodyPerThousandKm = 2.6f;  // A320 / B737 级
        public const float FuelBurnWidebodyPerThousandKm = 5.4f;    // B787 / A350 级

        // —— 机场与机组 ——
        /// <summary>起降+停场基础费（万美元/班）。</summary>
        public const float AirportFeeBaseWan = 0.35f;
        /// <summary>随航程增加的机场费斜率（万美元 / 千公里）。</summary>
        public const float AirportFeePerThousandKmWan = 0.06f;
        /// <summary>廊桥/行李等杂费（万美元/班）。</summary>
        public const float AirportAncillaryWan = 0.08f;
        /// <summary>机组成本（万美元 / 飞行小时）。</summary>
        public const float CrewCostPerBlockHourWan = 0.12f;
        /// <summary>巡航速度近似（公里/小时），用于由距离推飞行小时。</summary>
        public const float CruiseSpeedKmh = 850f;
        /// <summary>疲劳惩罚：需求乘数额外打满后，对机组成本的加成系数上限。</summary>
        public const float FatigueCrewCostMax = 0.15f;

        // —— 维修 ——
        /// <summary>每飞行小时可变维修（万美元）。</summary>
        public const float MaintPerFlightHourWan = 0.045f;
        /// <summary>机队月度基础适航维护（万美元/机），对齐 FleetManager。</summary>
        public const float FleetMonthlyMaintWan = 5f;
        /// <summary>
        /// 机龄每年对月维护的加成。取 5% 与旧 <c>FleetManager.MonthlyMaintOf</c> 的硬编码系数一致：
        /// 账本一直按 5% 扣钱，本参数原写 6% 使 UI（FleetSubpagePanel）与运力测算印出另一套数。
        /// 统一时以**账本**为准（玩家每天看到的是账本），否则改的是经济数值而不是口径。
        /// </summary>
        public const float MaintAgeUpliftPerYear = 0.05f;

        // —— 默认机型 ——
        public const int DefaultNarrowbodySeats = 180;
        public const int DefaultWidebodySeats = 290;
        public const float DefaultLoadFactor = 0.80f;

        // —— 固定开销（总部/租金等，万美元/月）——

        /// <summary>
        /// 固定开销的**旧锚点**（5 机 = 95 万/月）。现仅 `MockGameState` 测试替身引用；
        /// 权威口径已改为 `FixedMonthlyOpexFor(n)`（总部/后台）＋ `HrEconomics.LaborCostPerAircraftWan`
        /// （人力，20 万/机/月）——见 2026-10-07 存量人力成本接入。
        /// </summary>
        public const float DefaultFixedMonthlyOpexWan = 95f;

        // Phase 4：固定开销按机队规模分段（修复「小机队被固定开销压垮」）。
        // 2026-10-07：每机项（15 万/机/月）已置 0，改由显式人力成本承担（见 FixedOpexPerAircraftWan 注释）。
        /// <summary>固定开销基础值（总部/后台，与机队规模无关，万美元/月）。</summary>
        public const float FixedOpexBaseWan = 20f;
        /// <summary>
        /// 每架在役飞机的间接固定开销（万美元/机/月）。
        ///
        /// 2026-10-07：置 0。原先的 15 万/机/月（机位/备件/行政分摊）在人力成本
        /// （HrEconomics.LaborCostPerAircraftWan，20 万/机/月）显式接入后属**重复计提**——
        /// 「每机固定负担」现已由人力这条更真实的线承担，若保留会让小机队双计。
        /// 回归锚点：5 机仍为 20 + 0 乘 5 = 20（总部/后台），加上人力 100 得 120 万/月。
        /// </summary>
        public const float FixedOpexPerAircraftWan = 0f;

        /// <summary>按在役机队规模计算的固定月开销（万美元/月）。</summary>
        public static float FixedMonthlyOpexFor(int activeAircraftCount)
        {
            if (activeAircraftCount < 0) activeAircraftCount = 0;
            return FixedOpexBaseWan + FixedOpexPerAircraftWan * activeAircraftCount;
        }

        // —— 航线周期固定成本（与班次无关；「加密摊薄固定成本」的唯一来源）——
        //
        // 2026-10-09：此前模型只有**按班**变动成本（油/机场/机组/可变维修，见 RPMCalculator），
        // 没有一分钱「按周期」的航线固定成本。后果：日利 = 单班贡献 乘 班次 乘 调度乘数，
        // 三个因子恒为正 → 日利的**符号与班次无关**，加密只能放大盈亏、永远不可能把亏损转成盈利。
        // 现实中亏损航线加密确实可能转盈：外站场站、本地销售分销、航线管理与航权/时刻维持
        // 这些**不随班次增加**的成本被摊薄了。故补上这一项，使「1 班亏 / 3 班赚」成为可能。
        //
        // 口径：航线周期固定成本 = 基准 乘 航程档 乘 机型档（万美元/日）。
        // 仅承担「航线存在即发生」的那部分；按班发生的部分仍留在 RPMCalculator，两者不重复计提。
        // 量级已按 tools/balance 压测（60 组合）校准：见 `dotnet run -- --routefixed`。
        /// <summary>
        /// 航线周期固定成本基准的**出厂默认值**（万美元/日，短程窄体）。全网量级约 0.06~0.15 万/日/线。
        ///
        /// 这是回归锚点与文档口径的唯一权威来源；运行期覆盖见 <see cref="RouteFixedBaseDailyWan"/>。
        /// </summary>
        public const float RouteFixedBaseDailyWanDefault = 0.06f;

        /// <summary>
        /// 覆盖值的合理上限（万美元/日/线）。超过即视为误操作——0.5 已足以让全网航线收不抵支。
        /// </summary>
        public const float RouteFixedBaseDailyWanCeiling = 1f;

        static float _routeFixedBaseDailyWan = RouteFixedBaseDailyWanDefault;

        /// <summary>
        /// 航线周期固定成本基准（万美元/日，短程窄体）。默认为 <see cref="RouteFixedBaseDailyWanDefault"/>。
        ///
        /// **调参入口**：默认行为与出厂常量逐位一致；仅在调参/扫参时经
        /// <see cref="SetRouteFixedBaseDailyWan"/> 临时覆盖，经 <see cref="ResetRouteFixedBaseDailyWan"/> 复位。
        /// 之所以由 `static` 而非 `const` 承载，是为了让 `tools/balance` 能在**不重新编译**的前提下扫参数
        /// （`--routefixed-base` / `--routefixed-sweep`），并把同一个旋钮直接喂给 60 组合压测。
        /// </summary>
        public static float RouteFixedBaseDailyWan => _routeFixedBaseDailyWan;

        /// <summary>当前是否偏离出厂默认（可用于日志/UI 提示「本条结论基于非标准参数」）。</summary>
        public static bool IsRouteFixedBaseOverridden =>
            System.Math.Abs(_routeFixedBaseDailyWan - RouteFixedBaseDailyWanDefault) > 1e-6f;

        /// <summary>
        /// 覆盖航线周期固定成本基准。仅接受 (0, <see cref="RouteFixedBaseDailyWanCeiling"/>] 内的有限值；
        /// 非法输入**不写入**并返回 <c>false</c>——不做静默夹取，以免扫参时拿到与请求不符的曲线。
        /// </summary>
        public static bool SetRouteFixedBaseDailyWan(float wanPerDay)
        {
            if (float.IsNaN(wanPerDay) || float.IsInfinity(wanPerDay)) return false;
            if (wanPerDay <= 0f || wanPerDay > RouteFixedBaseDailyWanCeiling) return false;
            _routeFixedBaseDailyWan = wanPerDay;
            return true;
        }

        /// <summary>恢复出厂默认值。测试与一切非扫参调用方都应在结束时复位，避免静态状态外溢到后续用例。</summary>
        public static void ResetRouteFixedBaseDailyWan()
        {
            _routeFixedBaseDailyWan = RouteFixedBaseDailyWanDefault;
        }

#if UNITY_5_3_OR_NEWER
        // Unity 关闭 Domain Reload 时静态字段会跨 Play 会话残留，这里在子系统注册阶段强制复位，
        // 保证「进游戏」永远从出厂默认值开始。balance 工具不定义该宏，此块不参与编译。
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRouteFixedBaseOnUnityLoad()
        {
            ResetRouteFixedBaseDailyWan();
        }
#endif

        /// <summary>航程对周期固定成本的斜率：每 6000 km 再加一个基准（外站与分销投入随航程上升）。</summary>
        public const float RouteFixedDistanceDivisorKm = 6000f;
        /// <summary>宽体的外站/分销档位加成（客流量更大 → 场站与销售投入更高）。</summary>
        public const float RouteFixedWidebodyGauge = 1.3f;
        /// <summary>周期固定成本的航程下限锚（距航程缺失时按 1000 km 计，与调用方兜底一致）。</summary>
        public const float RouteFixedMinDistanceKm = 200f;
        /// <summary>周期固定成本的航程上限锚（超远程不再线性加码）。</summary>
        public const float RouteFixedMaxDistanceKm = 12000f;

        // —— Phase 2 OPS：调度 / 延误 / 地勤 ——
        /// <summary>日利用率目标（块时/可用日）。</summary>
        public const float TargetDailyUtilizationHours = 10f;
        /// <summary>利用率每偏离目标 1 小时对效率的影响。</summary>
        public const float UtilizationEfficiencyPerHour = 0.04f;
        /// <summary>周班次相对 7 班的规模效率弹性（边际递减）。</summary>
        public const float WeeklyFrequencyElasticity = 0.85f;

        // —— 班次模型（RouteFrequencyModel）：由 需求 × 枢纽规模 × 机型 × 航距 推每周班次 1–28 ——
        /// <summary>客源强度放大系数：把「两端吞吐几何均值 × OD 吸引力 × 距离衰减」映射为单向日客流人数。</summary>
        public const float FreqDemandPaxScale = 10f;
        /// <summary>距离衰减尺度（km）：航距每增加该值，需求约按 1/(1+d/尺度) 递减。</summary>
        public const float FreqDemandDecayKm = 3500f;
        /// <summary>城市经济目录缺失时的默认年吞吐（百万），避免需求侧凭空归零。</summary>
        public const float FreqUnknownPaxMillion = 20f;
        /// <summary>城市经济目录缺失时的默认 OD 吸引力（0–100）。</summary>
        public const float FreqUnknownOdIndex = 60f;
        /// <summary>目的地分带：此航距以内视为「短程」（本洲高频网）。</summary>
        public const float FreqBandNearKm = 3000f;
        /// <summary>目的地分带：此航距以内视为「中程」，以上为「远程」（洲际低频）。</summary>
        public const float FreqBandMidKm = 6000f;
        /// <summary>每周班次下限。</summary>
        public const int FreqMinWeekly = 1;
        /// <summary>每周班次上限（4 波/日 × 7 日）。</summary>
        public const int FreqMaxWeekly = 28;
        /// <summary>基准过站时间（分钟）。</summary>
        public const float BaseTurnMinutes = 50f;
        /// <summary>地勤每缺 1 人增加的过站分钟。</summary>
        public const float TurnPenaltyPerMissingStaffMin = 12f;
        /// <summary>推荐地勤人数（窄体）。</summary>
        public const int RecommendedGroundStaffNarrow = 3;
        /// <summary>推荐地勤人数（宽体）。</summary>
        public const int RecommendedGroundStaffWide = 5;
        /// <summary>延误概率基准。</summary>
        public const float BaseDelayProbability = 0.12f;
        /// <summary>高利用率对延误的敏感度。</summary>
        public const float DelayFromUtilization = 0.35f;
        /// <summary>准点率每低 10 个百分点增加的延误概率。</summary>
        public const float DelayFromOtpGap = 0.08f;
        /// <summary>延误发生时对日利润的惩罚比例上限。</summary>
        public const float DelayProfitPenaltyMax = 0.28f;
        /// <summary>地勤充足时对机场杂费的折扣。</summary>
        public const float GroundStaffAirportDiscount = 0.12f;

        public static bool IsWidebody(string aircraftType)
        {
            if (string.IsNullOrEmpty(aircraftType)) return false;
            string t = aircraftType.ToUpperInvariant();
            return t.Contains("787") || t.Contains("777") || t.Contains("350") ||
                   t.Contains("330") || t.Contains("747") || t.Contains("380") ||
                   t.Contains("340") || t.Contains("767") || t.Contains("WIDE");
        }

        public static int DefaultSeatsFor(string aircraftType) =>
            IsWidebody(aircraftType) ? DefaultWidebodySeats : DefaultNarrowbodySeats;

        public static float FuelBurnPerThousandKm(string aircraftType) =>
            IsWidebody(aircraftType) ? FuelBurnWidebodyPerThousandKm : FuelBurnNarrowbodyPerThousandKm;
    }
}
