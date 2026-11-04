using System;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 准入许可与时刻办理的**天级时间表**（2026-11-02 CEO 裁决）。
    ///
    /// 起因：用户问「申请航权和时刻槽要等到下一年的 Q2 才批，这样符合现实吗」，
    /// 结论是「现实中确实有航季，但绝不会让 CEO 干等一年」。查下来本作的病根不是数值，
    /// 而是**季度是唯一的推进刻度**：<c>TimeManager.AdvanceQuarterCore</c> 里才调
    /// <c>InboxManager.ProcessPendingEffects</c>，于是任何申请最少 90 天、国际重申两次 = 360 天。
    ///
    /// 本类只放**数字**，不放流程：所有工期、节点偏移、文案格式都从这里取，
    /// 免得「邮件正文写 21 天、进度表算 3 周、结算判 1 季」三处各说一套。
    ///
    /// 分工原则（用户裁决）：
    /// <list type="bullet">
    /// <item><b>结算按季</b> —— 财务、使用率考核、换季清算仍以季度为单位，不动。</item>
    /// <item><b>审批按天</b> —— 许可与时刻的每一道关都用绝对时刻（UTC ticks）到期，逐日推进。</item>
    /// </list>
    /// </summary>
    public static class PermitTimeline
    {
        // ———— 许可总工期（日历天，含审查、核验、公示）————
        /// <summary>国内、非协调机场（不涉时刻）：受理 + 审查 + 批复。</summary>
        public const int DomesticNoSlotDays = 12;
        /// <summary>国内、协调机场（须配时刻）：多一道运力与时刻匹配审查。</summary>
        public const int DomesticSlotDays = 21;
        /// <summary>国际/地区：多一道双边航权额度核验与对方许可。</summary>
        public const int InternationalDays = 49;

        /// <summary>被否后重申的惩罚：每次 +7 天（旧版每次 +1 季 = 90 天，这是「等一年」的主因）。</summary>
        public const int RetryPenaltyDays = 7;
        /// <summary>重申惩罚的封顶次数（第 4 次起不再叠）。</summary>
        public const int MaxRetries = 3;
        /// <summary>公示期被竞对异议后的顺延（旧版顺延一季）。</summary>
        public const int ObjectionDeferDays = 7;

        // ———— 时刻交割（旧版一律「下一季度」，最少 90 天）————
        /// <summary>协调池申请 · 换季窗口内（正在协调期，批得快）。</summary>
        public const int SlotPoolWindowDays = 7;
        /// <summary>协调池申请 · 窗口外（只能捡余量，协调人逐条核）。</summary>
        public const int SlotPoolOffWindowDays = 18;
        /// <summary>时刻变更（改时）：协调人只核「是否同一段服务」。</summary>
        public const int SlotChangeDays = 7;
        /// <summary>二级市场买断：现券交易，**当场过户**（0 天）。这条本来就已经是即时的，保留。</summary>
        public const int SlotPurchaseDays = 0;
        /// <summary>临时/加班航班时刻：先到先得，比常规申请快（C 批启用）。</summary>
        public const int SlotExtraFlightDays = 5;
        /// <summary>
        /// 夜间时段（22:00–06:00）的时刻：机场在这几个小时里余量最多，协调人随手就核（C 批）。
        /// 现实里夜间时刻正是货运与调机的去处 —— 白天让给客运黄金波，夜里没人抢。
        /// </summary>
        public const int SlotNightCargoDays = 3;

        /// <summary>夜间时段判定：22:00（含）到次日 06:00（含）。</summary>
        public static bool IsNightClock(string clock)
        {
            int mm = AirlineCEO.Logic.ScheduleSlots.ParseClock(clock);
            if (mm < 0) return false;
            return mm >= 22 * 60 || mm <= 6 * 60;
        }

        // ———— 并行与加急（B / C 批）————
        /// <summary>
        /// 先占时刻后补航权的宽限期：约 14 个工作日（现实 WASG 口径）。
        ///
        /// ★2026-11-02 报障修：「11 月 4 日受理、次日就买下时刻，24 日收到退槽信、26 日才发证」——
        /// 差的正是**一天**。旧值 20 天比国内协调机场的许可工期（<see cref="DomesticSlotDays"/> = 21 天）
        /// 还短：**受理当天就把槽买下来**这条最自然的打法，注定在发证前一天被退槽、扣局方关系，
        /// 玩家怎么打都输，而且看不出是自己算错还是系统坑人。
        /// 现在给 4 天余量：国内（含协调机场）的单子按正常节奏办得完；
        /// 国际线的 49 天、被否重申 +7 天、异议顺延 +7 天仍然撑不过 —— 那些才是真的赌。
        /// </summary>
        public const int SlotPreHoldDays = DomesticSlotDays + 4;
        /// <summary>加急后工期系数（压缩 40%）。</summary>
        public const float RushFactor = 0.6f;
        /// <summary>加急消耗的政府关系点数。</summary>
        public const float RushCostPoint = 10f;
        /// <summary>
        /// 加急请示的**发出下限**：付不起成本就不发（发了也批不成，只是噪声）。
        ///
        /// ★2026-11-02 报障修：「11 月那单航权，从来没见过加急请示」。
        /// 旧值钉的是 60 点，可开局只有 50 点、每季度自然回升 2 点 ——
        /// 等于**头五个季度（一年多）这条通道对玩家根本不存在**，而加急本身只要 10 点。
        /// 判据改成「付得起」：成本就是下限，余量够不够花由 CEO 在信里自己权衡
        /// （请示已经把「批后余 N 点」写清楚了）。
        /// </summary>
        public const float RushMinRelation = RushCostPoint;

        /// <summary>
        /// 许可总工期（天）。<paramref name="attempt"/> 从 1 起算，每次重申 +7 天，最多叠 3 次。
        /// </summary>
        public static int ReviewDays(bool international, bool needsSlot, int attempt)
        {
            int baseDays = international
                ? InternationalDays
                : (needsSlot ? DomesticSlotDays : DomesticNoSlotDays);
            int extra = Mathf.Clamp(attempt - 1, 0, MaxRetries) * RetryPenaltyDays;
            return baseDays + extra;
        }

        /// <summary>加急后的工期（供文案与结算共用；不在这里扣点数）。</summary>
        public static int Rushed(int days) => days <= 0 ? 0 : Mathf.Max(1, Mathf.RoundToInt(days * RushFactor));

        /// <summary>
        /// 某个办理节点落在**工期内的第几天**。
        /// 节点按真实办理节奏前疏后密：受理当天立案，审查在 15% 处，航权核验在 40%，
        /// 公示在 80%，发证即总工期。这样即便总工期只有 12 天，也能排出「2 / 5 / 10 / 12」四个可读的日期。
        /// </summary>
        public static int NodeDayOffset(int node, int totalDays)
        {
            int t = Mathf.Max(1, totalDays);
            switch (node)
            {
                case RoutePermitLogic.NodeAccept: return 0;
                case RoutePermitLogic.NodeReview: return Mathf.Clamp(Mathf.RoundToInt(t * 0.15f), 1, t);
                case RoutePermitLogic.NodeRights: return Mathf.Clamp(Mathf.RoundToInt(t * 0.40f), 1, t);
                case RoutePermitLogic.NodeNotice: return Mathf.Clamp(Mathf.RoundToInt(t * 0.80f), 1, t);
                case RoutePermitLogic.NodeGrant: return t;
                default: return t;      // 时刻节点在发证之后，由调用方另加
            }
        }

        /// <summary>
        /// 截至 <paramref name="elapsedDays"/> 天，应当**已经办完**的最高节点。
        /// 未到任何中间节点时返回 <see cref="RoutePermitLogic.NodeAccept"/>。
        /// </summary>
        public static int NodeReached(int elapsedDays, int totalDays)
        {
            int reached = RoutePermitLogic.NodeAccept;
            for (int n = RoutePermitLogic.NodeReview; n <= RoutePermitLogic.NodeGrant; n++)
                if (elapsedDays >= NodeDayOffset(n, totalDays)) reached = n;
            return reached;
        }

        /// <summary>下一个未到的节点（已达发证则返回 <see cref="RoutePermitLogic.NodeGrant"/>）。</summary>
        public static int NextNodeAfter(int doneNode, int totalDays)
        {
            for (int n = RoutePermitLogic.NodeReview; n <= RoutePermitLogic.NodeGrant; n++)
                if (n > doneNode && NodeDayOffset(n, totalDays) > 0) return n;
            return RoutePermitLogic.NodeGrant;
        }

        /// <summary>把「工期内的第几天」折成日历日期（UTC 入，本地日期出）。</summary>
        public static DateTime DayAt(DateTime acceptUtc, int dayOffset)
            => acceptUtc.AddDays(Mathf.Max(0, dayOffset));

        /// <summary>`"11月23日"`。邮件正文与页面明细共用，免得一处写「11-23」一处写「11月23日」。</summary>
        public static string DateLabel(DateTime utc)
        {
            var d = utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;
            return $"{d.Month}月{d.Day}日";
        }

        /// <summary>
        /// `"11月23日（还剩 21 天）"`；已到期写「今天到期」，过期写「已超期 N 天」。
        /// 玩家永远看得到**还剩几天**，而不是「第3年 Q2」这种要自己换算的说法。
        /// </summary>
        public static string DateWithCountdown(DateTime dueUtc, DateTime nowUtc)
        {
            int day = (int)Math.Floor((dueUtc.Date - nowUtc.Date).TotalDays);
            if (day > 0) return $"{DateLabel(dueUtc)}（还剩 {day} 天）";
            if (day == 0) return $"{DateLabel(dueUtc)}（今天到期）";
            return $"{DateLabel(dueUtc)}（已超期 {-day} 天）";
        }

        /// <summary>还剩多少天（向上取整到日；已过期返回 0）。</summary>
        public static int DaysLeft(DateTime dueUtc, DateTime nowUtc)
        {
            int day = (int)Math.Floor((dueUtc.Date - nowUtc.Date).TotalDays);
            return day < 0 ? 0 : day;
        }

        /// <summary>时刻交割工期：按渠道给天数（协调池看窗口，二级市场即时）。</summary>
        public static int SlotSettleDays(bool freeChannel, bool inWindow)
        {
            if (!freeChannel) return SlotPurchaseDays;
            return inWindow ? SlotPoolWindowDays : SlotPoolOffWindowDays;
        }

        /// <summary>
        /// 当前是否处在**换季集中申请窗**内：换季前 2 周 + 换季后 2 周。
        ///
        /// 换季日取 Q2 / Q4 首日（对应现实 3 月底夏秋、10 月底冬春）。
        /// 与 <see cref="AirlineCEO.Data.SlotUsageState.countingKey"/> 的航季口径一致：
        /// 夏秋 = Q2+Q3、冬春 = Q4+Q1 —— 考核与分配从此用同一个「季」，不再是两套说法。
        /// </summary>
        public static bool InSeasonWindow(int quarter, int simDay, out int daysToNextWindow)
        {
            const int windowDays = 14;
            int day = Mathf.Clamp(simDay, 1, GameState.DaysPerQuarter);
            // 换季后的 2 周：Q2 / Q4 的第 1~14 天
            bool afterSwitch = (quarter == 2 || quarter == 4) && day <= windowDays;
            // 换季前的 2 周：Q1 的第 76~90 天（→ Q2）、Q3 的第 76~90 天（→ Q4）
            bool beforeSwitch = (quarter == 1 || quarter == 3) && day > GameState.DaysPerQuarter - windowDays;
            if (afterSwitch) { daysToNextWindow = 0; return true; }
            if (beforeSwitch) { daysToNextWindow = 0; return true; }

            int left;
            if (quarter == 1) left = (GameState.DaysPerQuarter - windowDays) - day;      // 距 Q1 末窗口
            else if (quarter == 2) left = (GameState.DaysPerQuarter - windowDays) - day + GameState.DaysPerQuarter; // → Q3 末
            else if (quarter == 3) left = (GameState.DaysPerQuarter - windowDays) - day;
            else left = (GameState.DaysPerQuarter - windowDays) - day + GameState.DaysPerQuarter;
            daysToNextWindow = Mathf.Max(1, left);
            return false;
        }
    }
}
