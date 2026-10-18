using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 收件箱送达节奏：1× 下与公历同步，按真实办公习惯留出撰写、会商、局方回执时间。
    /// 加速时按游戏时钟等比缩短。
    /// </summary>
    public static class InboxCadence
    {
        /// <summary>部门会商并写成请示（工作日）。</summary>
        public const int StudyBusinessDays = 2;
        /// <summary>向局方递交后拿到受理回执（工作日）。</summary>
        public const int AdmitBusinessDays = 1;
        /// <summary>决策后部门起草回执/进度函（小时）。</summary>
        public const int ReplyHours = 4;
        /// <summary>节点进度汇报落笔（小时）。</summary>
        public const int ProgressHours = 6;

        public static DateTime AfterBusinessDays(DateTime utc, int days)
        {
            if (days <= 0) return utc;
            var local = utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;
            int n = 0;
            while (n < days)
            {
                local = local.AddDays(1);
                if (local.DayOfWeek != DayOfWeek.Saturday && local.DayOfWeek != DayOfWeek.Sunday)
                    n++;
            }
            return DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        }

        public static DateTime AfterHours(DateTime utc, int hours)
            => utc.AddHours(Math.Max(0, hours));

        public static DateTime StudyReady(DateTime utc) => AfterBusinessDays(utc, StudyBusinessDays);

        public static DateTime AdmitReady(DateTime utc) => AfterBusinessDays(utc, AdmitBusinessDays);

        public static DateTime ReplyReady(DateTime utc) => AfterHours(utc, ReplyHours);

        public static DateTime ProgressReady(DateTime utc) => AfterHours(utc, ProgressHours);
    }
}
