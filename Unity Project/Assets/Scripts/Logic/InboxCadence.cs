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

        /// <summary>
        /// 运营部受理**运力调令**后的研究期（工作日）。
        ///
        /// ★2026-11-04 用户裁定：「在命令台下达调整航线运力指令后，2 个工作日回复邮件时间长，改成 1 个工作日」。
        /// 与开航研究会（<see cref="StudyBusinessDays"/>）分开，是因为两者性质不同：
        /// 开航要算引力模型、跑可行性、跟局方对口径；运力调令只是在本线重排班、组环、联动四个部门，
        /// 现实里一个工作日内出方案是常态。绑成同一个常量，调其中一个就会顺手改掉另一个。
        /// </summary>
        public const int CapacityStudyBusinessDays = 1;
        /// <summary>向局方递交后拿到受理回执（工作日）。</summary>
        public const int AdmitBusinessDays = 1;
        /// <summary>决策后部门起草回执/进度函（小时）。</summary>
        public const int ReplyHours = 4;
        /// <summary>节点进度汇报落笔（小时）。</summary>
        public const int ProgressHours = 6;
        /// <summary>
        /// 市场开通通告之后，**另函**送询价请示还要再隔的小时数。
        ///
        /// ★2026-11-03 报障：「应该先收到开通包机、湿租、培训的业务邮件，再收到询价的邮件，
        /// 而且询价邮件需要隔几个小时再收到」。旧版两封都由 `ProgressReady`（同一 +6 小时）投递，
        /// 于是通告与询价落在同一分钟，收件箱里四封并列，看不出「先开会通告、再谈买卖」的顺序。
        /// </summary>
        public const int InquiryHours = 4;
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

        /// <summary>运力调令的研究期到点（见 <see cref="CapacityStudyBusinessDays"/>）。</summary>
        public static DateTime CapacityStudyReady(DateTime utc) => AfterBusinessDays(utc, CapacityStudyBusinessDays);

        public static DateTime AdmitReady(DateTime utc) => AfterBusinessDays(utc, AdmitBusinessDays);

        public static DateTime ReplyReady(DateTime utc) => AfterHours(utc, ReplyHours);

        public static DateTime ProgressReady(DateTime utc) => AfterHours(utc, ProgressHours);

        /// <summary>
        /// 询价请示落笔：**晚于**市场开通通告若干小时（见 <see cref="InquiryHours"/>）。
        /// 通告讲「这个市场开了」，询价讲「有一单生意，签不签」—— 现实里后者要先去问客户，
        /// 本就是两封信、两个时点。
        /// </summary>
        public static DateTime InquiryReady(DateTime utc) => AfterHours(utc, ProgressHours + InquiryHours);

        /// <summary>运力呈报落笔（见 <see cref="ReportHours"/>）。</summary>
        public static DateTime ReportReady(DateTime utc) => AfterHours(utc, ReportHours);
    }
}
