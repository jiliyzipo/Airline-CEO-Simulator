using System;
using AirlineCEO.Data;

#nullable disable

namespace AirlineCEO.Logic
{
    /// <summary>
    /// Phase 2 OPS：调度效率、延误概率、地勤与航班环校验（纯 C#）。
    /// </summary>
    public static class OpsEconomics
    {
        /// <summary>
        /// 一个「班次」包含的航段数：本作所有航线都是**一个往返**（去程 + 回程），故为 2。
        /// 「周班次」「日班次」一律指往返圈数 —— 日利用率、效率乘数、盈亏平衡都必须按 2 段计。
        /// </summary>
        public const int RoundTripSectors = 2;

        public struct ScheduleMetrics
        {
            public float blockHoursPerFlight;
            public float dailyUtilizationHours;
            public float scheduleEfficiency; // 约 0.7~1.15
            public float delayProbability;   // 0~1
            public float turnMinutes;
            public float profitMultiplier;   // 施加于「单班基准 × 日均班次」之后
        }

        /// <summary>由距离推 block hours。</summary>
        public static float BlockHours(float distanceKm) =>
            Math.Max(0.75f, Math.Max(distanceKm, 100f) / EconomyBalanceParams.CruiseSpeedKmh);

        public static int RecommendedGroundStaff(string aircraftType) =>
            EconomyBalanceParams.IsWidebody(aircraftType)
                ? EconomyBalanceParams.RecommendedGroundStaffWide
                : EconomyBalanceParams.RecommendedGroundStaffNarrow;

        public static float EstimateTurnMinutes(string aircraftType, int groundStaffAssigned)
        {
            int need = RecommendedGroundStaff(aircraftType);
            int missing = Math.Max(0, need - Math.Max(0, groundStaffAssigned));
            return EconomyBalanceParams.BaseTurnMinutes +
                   missing * EconomyBalanceParams.TurnPenaltyPerMissingStaffMin;
        }

        public static ScheduleMetrics Evaluate(
            float distanceKm,
            int weeklyFlights,
            string aircraftType,
            int groundStaffAssigned,
            float onTimePercent,
            float fatigue01 = 0.1f)
        {
            float block = BlockHours(distanceKm);
            float dailyFlights = Math.Max(0.1f, weeklyFlights / 7f);
            // 日利用率按「一圈 = 去 + 回两段」计（★2026-10-14 第十次问询统一口径）。
            // 旧版只算一段轮挡，于是同一条往返航线出现两个「日利用率」：
            //   班次模型（一圈 2 段 ÷ 7 天）→ FRA–ATL 4 班/周 = 9.96h/日（贴着 10h 目标）
            //   本函数（单段 × 班次 ÷ 7）  → 同一条线 = 4.98h/日（效率乘数因此被压在 0.80）
            // 两处用同一个 `TargetDailyUtilizationHours` 常数却差一倍，故统一为「一圈 2 段」。
            float utilHours = block * RoundTripSectors * dailyFlights;
            float turn = EstimateTurnMinutes(aircraftType, groundStaffAssigned);

            // 利用率相对目标的效率：过低浪费，过高挤兑
            float utilDelta = utilHours - EconomyBalanceParams.TargetDailyUtilizationHours;
            float utilFactor = 1f - Math.Abs(utilDelta) * EconomyBalanceParams.UtilizationEfficiencyPerHour;
            utilFactor = Clamp(utilFactor, 0.70f, 1.12f);

            // 过站偏长 → 效率下降
            float turnFactor = Clamp(
                1f - (turn - EconomyBalanceParams.BaseTurnMinutes) / 120f,
                0.75f, 1.05f);

            // 周班次规模：相对 7 班的幂次弹性
            float freqNorm = weeklyFlights / 7f;
            float freqFactor = (float)Math.Pow(Math.Max(0.25f, freqNorm),
                EconomyBalanceParams.WeeklyFrequencyElasticity);

            float efficiency = Clamp(utilFactor * turnFactor, 0.65f, 1.15f);

            float otp = Clamp(onTimePercent, 0f, 100f);
            float otpGap = Math.Max(0f, 85f - otp) / 10f;
            float utilStress = Clamp(
                (utilHours - EconomyBalanceParams.TargetDailyUtilizationHours) /
                EconomyBalanceParams.TargetDailyUtilizationHours,
                0f, 1.5f);

            float delayProb = EconomyBalanceParams.BaseDelayProbability
                              + utilStress * EconomyBalanceParams.DelayFromUtilization
                              + otpGap * EconomyBalanceParams.DelayFromOtpGap
                              + Clamp(fatigue01, 0f, 1f) * 0.10f;
            delayProb = Clamp(delayProb, 0.05f, 0.75f);

            float delayPenalty = delayProb * EconomyBalanceParams.DelayProfitPenaltyMax;
            float profitMult = efficiency * (1f - delayPenalty);

            // 地勤充足略降成本侧（通过利润乘数体现小幅增益）
            int need = RecommendedGroundStaff(aircraftType);
            if (groundStaffAssigned >= need)
                profitMult *= 1f + EconomyBalanceParams.GroundStaffAirportDiscount * 0.25f;

            return new ScheduleMetrics
            {
                blockHoursPerFlight = Round2(block),
                dailyUtilizationHours = Round2(utilHours),
                scheduleEfficiency = Round2(efficiency),
                delayProbability = Round2(delayProb),
                turnMinutes = Round2(turn),
                profitMultiplier = Round2(Clamp(profitMult, 0.45f, 1.25f))
            };
        }

        /// <summary>
        /// 单班 RPM 日基准 → 考虑周班次与调度后的**毛贡献**（不含航线周期固定成本）。
        /// 仅用于「单班/单时段」这一层级的分摊展示；航线级净利请用 <see cref="RouteDailyProfit"/>。
        /// </summary>
        public static float ToDailyProfit(float singleFlightDailyBaseline, int weeklyFlights, ScheduleMetrics m)
        {
            float dailyFlights = Math.Max(0.1f, weeklyFlights / 7f);
            return Round2(singleFlightDailyBaseline * dailyFlights * m.profitMultiplier);
        }

        /// <summary>
        /// 航线周期固定成本（万美元/日）：外站场站常设、本地销售与分销、航线管理与航权/时刻维持。
        /// **与班次无关**——加密不增加这项成本，故班次越密分摊到每班越薄，
        /// 这正是亏损航线加密后可能合法转盈的来源（也是它与其他成本项的区别）。
        ///
        /// 基准值取自 <see cref="EconomyBalanceParams.RouteFixedBaseDailyWan"/>，出厂默认为
        /// <see cref="EconomyBalanceParams.RouteFixedBaseDailyWanDefault"/>，可运行期覆盖以扫参。
        /// </summary>
        public static float RouteFixedDailyWan(float distanceKm, string aircraftType)
        {
            float d = Clamp(distanceKm > 0f ? distanceKm : 1000f,
                EconomyBalanceParams.RouteFixedMinDistanceKm,
                EconomyBalanceParams.RouteFixedMaxDistanceKm);
            float distScale = 1f + d / EconomyBalanceParams.RouteFixedDistanceDivisorKm;
            float gauge = EconomyBalanceParams.IsWidebody(aircraftType)
                ? EconomyBalanceParams.RouteFixedWidebodyGauge
                : 1f;
            return Round2(EconomyBalanceParams.RouteFixedBaseDailyWan * distScale * gauge);
        }

        /// <summary>
        /// 航线级日净利 = 单班毛贡献 × 日均班次 × 调度乘数 − 周期固定成本。
        /// 因末项与班次无关，加密可以**改变盈亏方向**（毛贡献为正时存在盈亏平衡班次）。
        /// </summary>
        public static float RouteDailyProfit(
            float singleFlightDailyBaseline, int weeklyFlights, ScheduleMetrics m, float fixedDailyWan)
            => Round2(ToDailyProfit(singleFlightDailyBaseline, weeklyFlights, m) - fixedDailyWan);

        /// <summary>
        /// 覆盖周期固定成本所需的<strong>日均班次</strong>（含调度乘数）。
        /// 返回 <see cref="float.PositiveInfinity"/> 表示单班毛贡献非正 —— 无论加密到多少班都救不回来。
        /// </summary>
        public static float BreakEvenDailyFlights(
            float singleFlightDailyBaseline, ScheduleMetrics m, float fixedDailyWan)
        {
            float perFlightNet = singleFlightDailyBaseline * m.profitMultiplier;
            if (perFlightNet <= 0.0001f) return float.PositiveInfinity;
            return fixedDailyWan / perFlightNet;
        }

        /// <summary>
        /// 取航线的「单班基准」（单班毛贡献，万美元/班/日）。
        ///
        /// 语义（务必遵守，这是历史缺陷的根因）：
        /// · 已设过（含**负值＝真实亏损基准**）→ 原样返回，绝不改写符号；
        /// · 仅当从未设过（|值| ≈ 0）才用当前日利反推兜底。
        ///
        /// 曾经四处调用点写成 `baseDailyProfit &gt; 0.01f ? base : before`，再补一句
        /// `if (baseline &lt;= 0.01f) baseline = 1f` —— 把亏损基准一并当成「未设」，
        /// 于是凭空生成 +1 万/班的盈利基准并写回 RouteData，亏损航线一次调令即永久转盈。
        /// </summary>
        public static float ResolvedSingleFlightBaseline(RouteData route)
        {
            if (route == null) return 0f;
            if (Math.Abs(route.baseDailyProfit) > 0.01f) return route.baseDailyProfit;

            // 未设过：用当前日利反推单班毛贡献（加回已计提的固定成本，再摊到日均班次）
            float dailyFlights = Math.Max(0.1f, (route.weeklyFlights > 0 ? route.weeklyFlights : 7) / 7f);
            return (route.dailyProfit + route.FixedDailyCostWan) / dailyFlights;
        }

        public static void ApplyToRoute(
            RouteData route,
            float singleFlightBaseline,
            float onTimePercent,
            float fatigue01 = 0.1f)
        {
            if (route == null) return;
            route.baseDailyProfit = singleFlightBaseline;
            var m = Evaluate(
                route.distanceKm > 0 ? route.distanceKm : 1000f,
                route.weeklyFlights > 0 ? route.weeklyFlights : 7,
                route.aircraftType,
                route.groundStaffAssigned,
                onTimePercent,
                fatigue01);
            route.dailyProfit = RouteDailyProfit(
                singleFlightBaseline,
                route.weeklyFlights,
                m,
                RouteFixedDailyWan(route.distanceKm, route.aircraftType));
        }

        /// <summary>两段连班：过站是否够用（简化宵禁/FTL 未建模）。</summary>
        public static FlightRing ValidateRing(string registration, RouteData a, RouteData b, int turnMinutes)
        {
            string legA = a?.routeId ?? "A";
            string legB = b?.routeId ?? "B";
            string reg = string.IsNullOrEmpty(registration) ? "ACFT" : registration;
            // 稳定 ID：同机同两段重复校验不变化
            var ring = new FlightRing
            {
                ringId = $"RING-{reg}-{legA}-{legB}",
                registration = reg,
                legARouteId = a?.routeId,
                legBRouteId = b?.routeId,
                turnMinutes = turnMinutes
            };

            if (a == null || b == null)
            {
                ring.valid = false;
                ring.validationNote =
                    "【无法连班】需要两段航线。\n" +
                    "影响：不能组成航班环，该机只能单段执飞，日利用率偏低。";
                return ring;
            }

            bool hubMatch =
                string.Equals(a.destination, b.origin, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeCity(a.destination), NormalizeCity(b.origin), StringComparison.OrdinalIgnoreCase);

            if (!hubMatch)
            {
                // 两段不在同一机场衔接（如都从 PEK 出发）→ 弱衔接
                bool turnOk = turnMinutes >= EconomyBalanceParams.BaseTurnMinutes;
                ring.valid = turnOk;
                if (turnOk)
                {
                    ring.validationNote =
                        "【勉强合格 · 枢纽衔接较弱】\n" +
                        $"第一段落到 {a.destination}，第二段却从 {b.origin} 起飞，飞机需额外调机/空飞衔接。\n" +
                        "影响（后续结算会体现）：\n" +
                        "· 过站与调机拉长，日利用率下降约 5%～12%\n" +
                        "· 延误概率升高，准点率更易被拖低\n" +
                        "· 仍可执飞，但单机利润与机组疲劳差于「真·同枢纽连班」\n" +
                        "建议：优先选「上一段终点 = 下一段起点」的航线对（如 PEK→SHA 再 SHA→PEK）。";
                }
                else
                {
                    ring.validationNote =
                        "【无法连班 · 过站不足】\n" +
                        $"枢纽不衔接，且过站仅 {turnMinutes} 分，低于底线 {EconomyBalanceParams.BaseTurnMinutes} 分。\n" +
                        "影响：航班环被拒，该机不能按此方案排班；请加长过站或换衔接航线。";
                }
                return ring;
            }

            int needTurn = (int)EstimateTurnMinutes(a.aircraftType, a.groundStaffAssigned);
            ring.valid = turnMinutes >= needTurn;
            if (ring.valid)
            {
                ring.validationNote =
                    "【连班合格】\n" +
                    $"同枢纽衔接（{a.destination}），过站 {turnMinutes} 分 ≥ 需要 {needTurn} 分。\n" +
                    "影响：可写入航班环；日利用率与准点率按正常调度效率结算，无额外惩罚。";
            }
            else
            {
                ring.validationNote =
                    "【无法连班 · 过站不足】\n" +
                    $"过站 {turnMinutes} 分 < 需要 {needTurn} 分（机型/地勤决定最短过站）。\n" +
                    "影响：环方案无效，强行排班会推高延误与旅客投诉；请增加地勤或拉长过站后再校验。";
            }
            return ring;
        }

        /// <summary>
        /// 本航线往返环（去程 + 回程，同一架飞机当日连班）：
        /// 回程段由引擎按 RoundTripWindow 在同一班次内执行（RouteFlight.inboundLeg），
        /// 不需要第二条航线，因此单航线也能构成有效航班环。
        /// </summary>
        public static FlightRing ValidateRoundTripRing(string registration, RouteData route, int turnMinutes)
        {
            string leg = route?.routeId ?? "RT";
            string reg = string.IsNullOrEmpty(registration) ? "ACFT" : registration;
            var ring = new FlightRing
            {
                ringId = $"RING-{reg}-{leg}-RT",
                registration = reg,
                legARouteId = route?.routeId,
                legBRouteId = route?.routeId,     // 同一航线：去程 + 回程
                turnMinutes = turnMinutes
            };

            if (route == null)
            {
                ring.valid = false;
                ring.validationNote = "【无法编环】缺少航线。";
                return ring;
            }

            int needTurn = (int)EstimateTurnMinutes(route.aircraftType, route.groundStaffAssigned);
            ring.valid = turnMinutes >= needTurn;
            ring.validationNote = ring.valid
                ? "【往返环合格】\n" +
                  $"同一架飞机当日连班：{route.origin}→{route.destination} 去程，落地 {route.destination} 过站 {turnMinutes} 分 " +
                  $"≥ 需要 {needTurn} 分，再执行 {route.destination}→{route.origin} 回程。\n" +
                  "影响：回程不空飞、日利用率按正常调度效率结算；无需第二条航线即可成环。"
                : "【往返环不合格 · 过站不足】\n" +
                  $"过站 {turnMinutes} 分 < 需要 {needTurn} 分（机型/地勤决定最短过站）。\n" +
                  "影响：回程衔接过紧易推高延误，请增加地勤或拉长过站。";
            return ring;
        }

        /// <summary>由延误概率回写准点率示意（季度结算用）。</summary>
        public static float ProjectOnTimePercent(float currentOtp, float avgDelayProb)
        {
            float target = 100f * (1f - Clamp(avgDelayProb, 0f, 0.6f));
            return Round2(Clamp(currentOtp * 0.6f + target * 0.4f, 55f, 98f));
        }

        static string NormalizeCity(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Trim().Replace(" ", "").ToUpperInvariant();
        }

        static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        static float Round2(float v) => (float)Math.Round(v, 2);
    }
}
