using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 排班轮转的纯数值常量与纯函数（无 UnityEngine / GameState / Aircraft 依赖）。
    ///
    /// 存在理由：`RouteData`（Data 层，被 tools/balance 离线链接）需要这些成员，
    /// 而 `OpsRotationLogic` 依赖 `Aircraft` 与 `GameState`，无法离线编译。
    /// 把纯数值抽到这里，游戏与离线模拟器共用同一份权威值，避免双写漂移；
    /// `OpsRotationLogic` 中的同名成员一律转发到此处。
    /// </summary>
    public static class OpsRotationParams
    {
        /// <summary>一天的分钟数（分钟轴周期）。</summary>
        public const int MinutesPerDay = 24 * 60;

        /// <summary>报到提前量（首段起飞前）。</summary>
        public const int ReportBeforeMinutes = 60;

        /// <summary>
        /// **经停站的停站时间**（分钟）：飞机在经停机场落地后再起飞之间的地面时间。
        ///
        /// ★2026-10-16：现实里经停（traffic stop）要上下客、清舱、补油并重新放行，
        /// 干线窄体约 45–60 分、宽体约 60–90 分；本作取一个固定值作为简化口径。
        /// 它**加进该航线的班期跨度**（即 `arrTime` 已含它），因此回程离港时刻
        /// （去程起飞 + 轮挡 + 过站）不用再单独加一遍 —— 见 `RoutePermitLogic.RoundTripOf`。
        /// </summary>
        public const int StopoverGroundMinutes = 60;

        /// <summary>班期跨度与物理轮挡的绝对容差（分钟；另按轮挡 25% 放宽）。</summary>
        public const int ScheduleSpanToleranceMinutes = 25;

        /// <summary>班期跨度与物理轮挡的相对容差。</summary>
        public const float ScheduleSpanToleranceRatio = 0.25f;

        /// <summary>
        /// 单程轮挡（分钟）：按航程物理推算，下限 45 分钟。
        /// 班期自带的「起飞→到达」只有落在容差内才视为真实班期。
        /// </summary>
        public static int BlockMinutes(float distanceKm)
        {
            float km = distanceKm > 0f ? distanceKm : 1000f;
            int b = (int)Math.Round(OpsEconomics.BlockHours(km) * 60.0);
            return b < 45 ? 45 : b;
        }

        // ================= R4 跨夜班归属（双口径） =================
        // 「一天能飞几段」由报到时刻 + 值勤上限决定，与午夜无关。因此跨夜 ≠ 新的一天：
        //   · 值勤 / 机组（排班用）→ 按「值勤期起始日」（= 报到时刻所在日）归组
        //   · 航班量统计（weeklyFlights）→ 按「计划起飞时刻所在日」归组
        // 两者仅在「00:00–00:59 离港且报到在前一日 23:xx」时才分歧。

        /// <summary>分钟轴上的「日偏移」：0 = 当日，1 = 次日，-1 = 前一日。</summary>
        public static int DayOf(int minutes) => (int)Math.Floor(minutes / (double)MinutesPerDay);

        /// <summary>值勤日偏移（按报到时刻）—— 整个值勤期都归这一天。</summary>
        public static int DutyDayOf(int reportMin) => DayOf(reportMin);

        /// <summary>
        /// 把「本条值勤期内」的离港时刻放到以报到时刻为原点的连续轴上：
        /// 早于报到时刻的钟点（如报到 22:30、离港 00:30）自动接续到次日，而不是被午夜切回当天。
        /// </summary>
        public static int LegOnDutyAxis(int reportMin, int depMin)
        {
            int d = depMin - reportMin;
            if (d < 0) d += MinutesPerDay;
            return reportMin + d;
        }

        /// <summary>离港日偏移（按计划起飞时刻，相对同一值勤期的报到时刻）—— 航班量按这一天统计。</summary>
        public static int DepartureDayOf(int reportMin, int depMin) => DayOf(LegOnDutyAxis(reportMin, depMin));

        /// <summary>两套口径是否同属一天（跨夜班为 false）。</summary>
        public static bool SameAttributionDay(int reportMin, int depMin)
            => DutyDayOf(reportMin) == DepartureDayOf(reportMin, depMin);
    }
}
