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
        /// <summary>
        /// 运营部受理运力调令后，编班、组环、联动市场/机务/财务/地勤并写成呈报（小时）。
        ///
        /// ★2026-10-19 报障：「CEO 下达指令后，邮箱立即收到汇报，应该是过几小时才把呈报送到收件箱」。
        /// 旧版 `QueueCapacityReport` 直接 `Add`，指令刚敲完呈报就躺在收件箱里，
        /// 会让人以为「运营部不用干活」；且呈报是待批件，立刻出现等于把审批压在一瞬间。
        ///
        /// ★2026-10-29：线上路径已改为**研究期**（<see cref="StudyBusinessDays"/> 个工作日），
        /// 见 <c>QueueCapacityReport(..., deferred: true)</c>。本常量只留给**非拟定**的老口径
        /// （旧档迁移重编、回归用例里的「先排后报」路径）作「部门受理到落笔」的短延迟。
        /// </summary>
        public const int ReportHours = 2;

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

        /// <summary>运力呈报落笔（见 <see cref="ReportHours"/>）。</summary>
        public static DateTime ReportReady(DateTime utc) => AfterHours(utc, ReportHours);
    }
}
