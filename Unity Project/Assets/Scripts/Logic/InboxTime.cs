using System;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>收件箱按本地公历日分组、显示时分（对齐顶栏时钟）。</summary>
    public static class InboxTime
    {
        static readonly string[] WeekdayZh =
            { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

        public static DateTime ToLocal(InboxMail m)
        {
            long ticks = m != null ? m.receivedUtcTicks : 0;
            DateTime utc;
            if (ticks > 0)
                utc = new DateTime(ticks, DateTimeKind.Utc);
            else
                utc = DateTime.UtcNow;
            utc = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
            return utc + DateTimeOffset.Now.Offset;
        }

        public static string DayKey(InboxMail m)
        {
            var d = ToLocal(m);
            return $"{d.Year:0000}-{d.Month:00}-{d.Day:00}";
        }

        public static string DayHeader(InboxMail m)
        {
            var d = ToLocal(m);
            return $"{d.Year}年{d.Month:00}月{d.Day:00}日 {WeekdayZh[(int)d.DayOfWeek]}";
        }

        public static string Clock(InboxMail m)
        {
            var d = ToLocal(m);
            return $"{d.Hour:00}:{d.Minute:00}";
        }

        /// <summary>
        /// `2026年11月24日` —— 信笺落款用（不带星期）。
        /// 落款必须是**游戏内日历**上的到件日，不能读系统时钟。
        /// </summary>
        public static string DateText(InboxMail m)
        {
            var d = ToLocal(m);
            return $"{d.Year}年{d.Month:00}月{d.Day:00}日";
        }

        public static string FullStamp(InboxMail m) => DayHeader(m) + "  " + Clock(m);

        /// <summary>
        /// 办公时段（本地，分钟）。部门在这段时间里写函、盖章、发信 —— 落箱时刻落在这条带上才像人干的。
        /// 边界取 08:30–18:30：既覆盖早班、午休后的会商，也给晚班留出下班前的最后一封。
        /// </summary>
        public const int OfficeStartMinute = 8 * 60 + 30;
        public const int OfficeEndMinute = 18 * 60 + 30;

        /// <summary>
        /// 把「落箱时刻」调成一个像人写出来的时刻：非办公时段（凌晨 0 点、深夜）挪到当日办公时段内，
        /// 且不晚于 <paramref name="nowUtc"/>（游戏时钟）。
        ///
        /// 病根：邮件按 **计划工期** 排队，天级工期一律落在 00:00，玩家一次推进好几天，
        /// 到期的信就在同一瞬间投递、同一分钟落箱（2026-11-03 报障：「经常几封邮件同时到达」）。
        /// 这里只做「时刻合理化」，同分钟去重由 <c>InboxManager.SpreadMinute</c> 负责 ——
        /// 两件事分开是因为前者是纯函数（可单测），后者要读收件箱。
        /// </summary>
        public static DateTime OfficeMoment(DateTime utc, DateTime nowUtc, string id)
        {
            var offset = DateTimeOffset.Now.Offset;
            var local = utc + offset;
            var nowLocal = nowUtc + offset;
            if (nowLocal < local) local = nowLocal;                  // 不得晚于「现在」

            int minute = local.Hour * 60 + local.Minute;
            if (minute >= OfficeStartMinute && minute <= OfficeEndMinute) return ToUtc(local, offset);

            // 可用窗口 = [上班, min(下班, 现在)]。散列落在窗口内的某一分钟。
            // 现在还没上班（凌晨/清晨）时窗口在前一天 —— 「昨天下班前发的信，今早才看到」，
            // 这比把十几封信都写成 00:00 更像人干的。
            int upper = Math.Min(OfficeEndMinute, nowLocal.Hour * 60 + nowLocal.Minute);
            int dayShift = 0;
            if (upper <= OfficeStartMinute + 15)
            {
                dayShift = -1;
                upper = OfficeEndMinute;
            }
            int span = Math.Max(1, upper - OfficeStartMinute + 1);
            int pick = OfficeStartMinute + (int)(StableHash(id) % (uint)span);
            var day = local.Date.AddDays(dayShift).AddMinutes(pick);
            return ToUtc(day, offset);
        }

        static DateTime ToUtc(DateTime local, TimeSpan offset)
            => DateTime.SpecifyKind(local.AddMinutes(-offset.TotalMinutes), DateTimeKind.Utc);

        /// <summary>
        /// 邮件 id → 稳定散列（FNV-1a 32bit）。**不能用 string.GetHashCode**：
        /// 它在不同运行时/版本上取值不同，会让同一份存档每次打开给出不同的落箱时刻。
        /// </summary>
        public static uint StableHash(string s)
        {
            uint h = 2166136261u;
            if (string.IsNullOrEmpty(s)) return h;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= 16777619u;
            }
            return h;
        }
    }
}
