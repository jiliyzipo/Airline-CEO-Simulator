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

        public static string FullStamp(InboxMail m) => DayHeader(m) + "  " + Clock(m);
    }
}
