using System;
using System.Text.RegularExpressions;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 现实时间 → 游戏时间：只保留相对间隔，接收瞬间打上游戏时间戳。
    /// 公式：gameEffectiveTime = gameReceivedTime + relativeOffsetHours
    /// （「次日零时」先把偏移算到下一游戏日 00:00，再写入 relativeOffsetHours）。
    /// </summary>
    public static class ExternalTimeMapper
    {
        static readonly Regex RxYmd = new Regex(
            @"((?:20|19)\d{2})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日",
            RegexOptions.Compiled);
        static readonly Regex RxMd = new Regex(
            @"(?<!\d)(\d{1,2})\s*月\s*(\d{1,2})\s*日",
            RegexOptions.Compiled);
        static readonly Regex RxDays = new Regex(
            @"(?:预计)?\s*(\d+)\s*天\s*后",
            RegexOptions.Compiled);
        static readonly Regex RxHours = new Regex(
            @"(?:预计)?\s*(\d+)\s*小时\s*后",
            RegexOptions.Compiled);
        static readonly Regex RxImmediate = new Regex(
            @"即刻|立即|即日起|即刻生效|马上生效",
            RegexOptions.Compiled);
        // 去掉原文日期两旁的残留空格（如「需在 21天后零时 前完成」）
        static readonly Regex RxSpaceBeforePhrase = new Regex(
            @"(?<=[\u4e00-\u9fff，。；：、])\s+(?=\d+(?:天|小时)后)",
            RegexOptions.Compiled);
        static readonly Regex RxSpaceAfterPhrase = new Regex(
            @"(?<=\d+(?:天|小时)后(?:零时)?)\s+(?=[\u4e00-\u9fff，。；：、])",
            RegexOptions.Compiled);

        public struct MapResult
        {
            public float offsetHours;
            public string relativeDesc;
            public DateTime effective;
            public string title;
            public string content;
        }

        /// <summary>该日期写法是否处在「时间表述」语境里（自…起 / …零时 / 于…施行），而非名称中的日期。</summary>
        static readonly Regex RxTimeAfter = new Regex(
            @"^\s*(零时|时整|时起|点起|起|开始|生效|施行|实施|执行|当天|当日|上午|下午|凌晨|晚)",
            RegexOptions.Compiled);
        static readonly Regex RxTimeBefore = new Regex(
            @"(自|于|从|定于|拟于|在)\s*$",
            RegexOptions.Compiled);
        /// <summary>确实指向「零时」的写法：只有这些才适合整段替换成「明日零时起」。</summary>
        static readonly Regex RxMidnightAfter = new Regex(
            @"^\s*(零时|时整|时起|点起|起|开始|生效|施行|实施|执行)",
            RegexOptions.Compiled);

        static bool IsTimeExpression(string text, Match m)
        {
            int after = m.Index + m.Length;
            string tail = after < text.Length ? text.Substring(after, Math.Min(8, text.Length - after)) : "";
            if (RxTimeAfter.IsMatch(tail)) return true;
            string head = text.Substring(Math.Max(0, m.Index - 3), Math.Min(3, m.Index));
            return RxTimeBefore.IsMatch(head);
        }

        /// <summary>
        /// 提取相对间隔（小时），只认「相对时间关系」：
        /// 「N 天后」→ N×24h；「N 小时后」→ N 小时；「即刻」→ 0；
        /// 时间表述型日期（如「自9月12日零时起」）按日粒度落在游戏本地零时；
        /// 其余（无明确时间、或日期只出现在名称里）→ 次日本地零时。
        /// 现实绝对时刻（publishedAt / realEffectiveAt）只用于判断日期差，不做小时级换算。
        /// </summary>
        public static float ExtractOffsetHours(string text, DateTime? publishedAt,
            DateTime? realEffectiveAt, DateTime gameReceived, bool expired)
        {
            string raw;
            return ExtractOffsetHours(text, publishedAt, realEffectiveAt, gameReceived, expired, out raw);
        }

        /// <summary>同上，并回传驱动了偏移的那段原始日期文本（供正文去现实日期时定位）。</summary>
        public static float ExtractOffsetHours(string text, DateTime? publishedAt,
            DateTime? realEffectiveAt, DateTime gameReceived, bool expired, out string phraseDateRaw)
        {
            phraseDateRaw = "";
            if (expired) return 0f;
            string t = text ?? "";
            if (RxImmediate.IsMatch(t)) return 0f;

            var days = RxDays.Match(t);
            if (days.Success && int.TryParse(days.Groups[1].Value, out int d) && d >= 0)
                return d * 24f;

            var hours = RxHours.Match(t);
            if (hours.Success && int.TryParse(hours.Groups[1].Value, out int hr) && hr >= 0)
                return hr;

            DateTime anchor = publishedAt.HasValue ? EnsureUtc(publishedAt.Value) : EnsureUtc(gameReceived);
            DateTime target = default;
            bool dated = false;
            var ymd = RxYmd.Match(t);
            if (ymd.Success && IsTimeExpression(t, ymd) && TryDate(ymd, anchor.Year, out target))
            {
                dated = true;
                phraseDateRaw = ymd.Value;
            }
            else
            {
                var md = RxMd.Match(t);
                if (md.Success && IsTimeExpression(t, md) && TryMd(md, anchor, out target))
                {
                    dated = true;
                    phraseDateRaw = md.Value;
                }
            }
            if (dated)
            {
                int dayDelta = (int)Math.Round((target.Date - anchor.Date).TotalDays);
                if (dayDelta < 0) dayDelta = 0;
                return HoursUntilNextMidnight(gameReceived) + Math.Max(0, dayDelta - 1) * 24f;
            }

            return HoursUntilNextMidnight(gameReceived);
        }

        /// <summary>措辞（不带「起」），用于替换正文里的现实日期，如「明日零时」「3天后」。</summary>
        public static string Adverbial(float offsetHours, DateTime received, DateTime effective)
        {
            if (offsetHours <= 0.01f) return "即刻";
            var r = EnsureUtc(received);
            var e = EnsureUtc(effective);
            if (IsLocalMidnight(e))
            {
                int n = (int)Math.Round((Local(e).Date - Local(r).Date).TotalDays);
                if (n <= 0) return "今日零时";
                if (n == 1) return "明日零时";
                return $"{n}天后零时";
            }
            if (offsetHours >= 20f && Math.Abs(offsetHours % 24f) < 0.2f)
            {
                int n = (int)Math.Round(offsetHours / 24f);
                return n <= 0 ? "即刻" : $"{n}天后";
            }
            if (offsetHours < 24f) return $"{Math.Max(1, (int)Math.Round(offsetHours))}小时后";
            int whole = (int)Math.Round(offsetHours / 24f);
            return $"{whole}天后";
        }

        /// <summary>措辞（带「起」），用于公文「施行时间」行。</summary>
        public static string Describe(float offsetHours, DateTime received, DateTime effective)
        {
            string a = Adverbial(offsetHours, received, effective);
            return a == "即刻" ? "即刻生效" : a + "起";
        }

        /// <summary>
        /// 隐去现实日期，只留相对时间关系。
        /// <para>
        /// 时间表述型日期（「自9月12日零时起」）→ 替换为相对措辞（「自明日零时起」）；
        /// 名称型日期（「9月12日全国航空天气预报」）→ 替换为「今日 / 昨日 / N天前」等相对日词，
        /// 既不泄漏现实日期，也不会被硬塞成「即刻」这类读不通的措辞。
        /// </para>
        /// </summary>
        public static string StripRealDates(string text, string relativePhrase, DateTime anchor, string phraseDateRaw)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string phrase = string.IsNullOrEmpty(relativePhrase) ? "即将" : relativePhrase;
            string raw = phraseDateRaw ?? "";
            var a = EnsureUtc(anchor);

            string s = RxYmd.Replace(text, m => ReplaceDate(text, m, phrase, raw, a, true));
            s = RxMd.Replace(s, m => ReplaceDate(text, m, phrase, raw, a, false));
            // 「自即刻零时起」这类叠加措辞做一次清理
            s = s.Replace("自即刻零时起", "即刻起")
                 .Replace("自即刻起", "即刻起")
                 .Replace("零时零时", "零时");
            // 原文日期两旁的空格不残留：如「需在 21天后零时 前完成」→「需在21天后零时前完成」
            s = RxSpaceBeforePhrase.Replace(s, "");
            s = RxSpaceAfterPhrase.Replace(s, "");
            return s.Trim();
        }

        static string ReplaceDate(string text, Match m, string phrase, string phraseDateRaw,
            DateTime anchor, bool withYear)
        {
            bool isPhraseDate = phraseDateRaw.Length > 0
                && SameDate(m.Value, phraseDateRaw);
            if (isPhraseDate && IsMidnightExpression(text, m)) return phrase;
            return DayWord(m.Value, anchor, withYear);
        }

        static bool IsMidnightExpression(string text, Match m)
        {
            int after = m.Index + m.Length;
            string tail = after < text.Length ? text.Substring(after, Math.Min(8, text.Length - after)) : "";
            if (RxMidnightAfter.IsMatch(tail)) return true;
            string head = text.Substring(Math.Max(0, m.Index - 3), Math.Min(3, m.Index));
            return RxTimeBefore.IsMatch(head);
        }

        static bool SameDate(string a, string b)
        {
            return Norm(a) == Norm(b);
        }

        static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace(" ", "").Replace("\u3000", "").Replace("年", "-").Replace("月", "-").Replace("日", "");
        }

        /// <summary>把游离的现实日期换成相对日词（以原文自身的发布日期为锚，保持内部一致性）。</summary>
        static string DayWord(string raw, DateTime anchor, bool withYear)
        {
            DateTime d;
            if (withYear)
            {
                int year;
                if (!TryDate2(raw, anchor.Year, out d, out year)) return "近日";
            }
            else
            {
                if (!TryMd2(raw, anchor, out d)) return "近日";
            }
            int delta = (int)Math.Round((d.Date - anchor.Date).TotalDays);
            if (delta == 0) return "今日";
            if (delta == 1) return "明日";
            if (delta == -1) return "昨日";
            if (delta == 2) return "后日";
            if (delta == -2) return "前日";
            if (delta > 2 && delta <= 10) return delta + "天后";
            if (delta < -2 && delta >= -30) return (-delta) + "天前";
            return delta > 0 ? "近日" : "此前";
        }

        static bool TryDate2(string raw, int fallbackYear, out DateTime value, out int year)
        {
            value = default;
            year = fallbackYear;
            var m = Regex.Match(raw, @"(\d{2,4})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日");
            if (!m.Success) return false;
            year = int.Parse(m.Groups[1].Value);
            if (year < 100) year += 2000;
            int mon = int.Parse(m.Groups[2].Value);
            int day = int.Parse(m.Groups[3].Value);
            return TryBuild(year, mon, day, out value);
        }

        static bool TryMd2(string raw, DateTime anchor, out DateTime value)
        {
            value = default;
            var m = Regex.Match(raw, @"(\d{1,2})\s*月\s*(\d{1,2})\s*日");
            if (!m.Success) return false;
            int mon = int.Parse(m.Groups[1].Value);
            int day = int.Parse(m.Groups[2].Value);
            return TryBuild(anchor.Year, mon, day, out value);
        }

        static bool TryBuild(int year, int mon, int day, out DateTime value)
        {
            value = default;
            try
            {
                value = new DateTime(year, mon, day);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static MapResult Map(ExternalEventPayload payload, DateTime gameReceived)
        {
            if (payload == null) payload = new ExternalEventPayload();
            var recv = EnsureUtc(gameReceived);
            string phraseDateRaw;
            float offset = ExtractOffsetHours(payload.content ?? payload.title,
                payload.publishedAt, payload.realEffectiveAt, recv, payload.expired, out phraseDateRaw);
            var effective = recv.AddHours(offset);
            if (effective < recv) { effective = recv; offset = 0f; }

            DateTime anchor = payload.publishedAt.HasValue ? EnsureUtc(payload.publishedAt.Value) : recv;
            string adv = Adverbial(offset, recv, effective);
            string desc = Describe(offset, recv, effective);
            string title = StripRealDates(payload.title ?? "", adv, anchor, phraseDateRaw);
            string content = StripRealDates(payload.content ?? "", adv, anchor, phraseDateRaw);
            if (string.IsNullOrEmpty(content)) content = title;

            return new MapResult
            {
                offsetHours = offset,
                relativeDesc = desc,
                effective = effective,
                title = title,
                content = content
            };
        }

        public static ExternalEvent ToEvent(ExternalEventPayload payload, DateTime gameReceived)
        {
            var m = Map(payload, gameReceived);
            return new ExternalEvent
            {
                id = string.IsNullOrEmpty(payload?.id) ? "ext_" + Guid.NewGuid().ToString("N").Substring(0, 8) : payload.id,
                category = payload?.category ?? "",
                title = m.title,
                content = m.content,
                source = string.IsNullOrEmpty(payload?.source) ? "通讯社" : payload.source,
                relativeOffsetHours = m.offsetHours,
                relativeDesc = m.relativeDesc,
                effectType = payload?.effectType ?? "",
                effectValue = payload?.effectValue ?? 0f,
                affectedRegion = payload?.affectedRegion ?? "",
                durationHours = payload?.durationHours ?? 0,
                chainId = payload?.chainId ?? "",
                generated = payload?.generated ?? false,
                inferred = payload?.inferred ?? false,
                impactNote = payload?.impactNote ?? "",
                GameReceivedTime = EnsureUtc(gameReceived),
                GameEffectiveTime = m.effective
            };
        }

        /// <summary>距下一「游戏本地」零时的小时数（顶栏显示的是本地时间，公文里的「零时」以本地为准）。</summary>
        public static float HoursUntilNextMidnight(DateTime received)
        {
            var r = EnsureUtc(received);
            var local = Local(r);
            var next = local.Date.AddDays(1);
            return (float)(next - local).TotalHours;
        }

        public static bool IsNextMidnight(DateTime received, DateTime effective)
        {
            return IsLocalMidnight(effective)
                && Local(effective).Date == Local(received).Date.AddDays(1);
        }

        /// <summary>UTC 瞬间 → 本机时区的墙上时间（与顶栏时钟同一口径）。</summary>
        public static DateTime Local(DateTime utc)
            => DateTime.SpecifyKind(EnsureUtc(utc) + AirlineCEO.Systems.GameState.LocalUtcOffset, DateTimeKind.Unspecified);

        /// <summary>该 UTC 瞬间在游戏本地时间上是否正好是零时整。</summary>
        public static bool IsLocalMidnight(DateTime utc)
        {
            var l = Local(utc);
            return l.Hour == 0 && l.Minute == 0;
        }

        public static DateTime EnsureUtc(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();
            if (dt.Kind == DateTimeKind.Unspecified)
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return dt;
        }

        public static string GameDateLabel(DateTime utc)
        {
            var t = Local(utc);
            return $"{t.Year}年{t.Month}月{t.Day}日";
        }

        public static string GameDateTimeLabel(DateTime utc)
        {
            var t = Local(utc);
            return $"{t.Year}年{t.Month:00}月{t.Day:00}日 {t.Hour:00}:{t.Minute:00}";
        }

        static bool TryDate(Match ymd, int fallbackYear, out DateTime dt)
        {
            dt = default;
            int y = int.TryParse(ymd.Groups[1].Value, out int yy) ? yy : fallbackYear;
            if (!int.TryParse(ymd.Groups[2].Value, out int mo)) return false;
            if (!int.TryParse(ymd.Groups[3].Value, out int d)) return false;
            if (mo < 1 || mo > 12) return false;
            int dim = DateTime.DaysInMonth(y, mo);
            if (d < 1 || d > dim) return false;
            dt = new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc);
            return true;
        }

        static bool TryMd(Match md, DateTime published, out DateTime dt)
        {
            dt = default;
            if (!int.TryParse(md.Groups[1].Value, out int mo)) return false;
            if (!int.TryParse(md.Groups[2].Value, out int d)) return false;
            int y = published.Year;
            if (mo < published.Month) y++;
            if (mo < 1 || mo > 12) return false;
            int dim = DateTime.DaysInMonth(y, mo);
            if (d < 1 || d > dim) return false;
            dt = new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc);
            return true;
        }
    }
}
