#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 收件箱「落箱时刻」孤立回归（2026-11-03 报障）。
    ///
    /// 报障原文：「邮件的收件时间有问题，经常几封邮件同时到达，这样很不真实。」
    ///
    /// 病根不是某一条投递路径，而是**投递模型本身**：
    /// ① 天级工期（<c>InboxCadence.StudyReady</c>、准入节点、合同节点…）算出来的到达时刻是本地 00:00；
    /// ② 玩家一次推进好几天，所有到期信在同一个 SimUtc 瞬间 <c>DeliverDue</c>；
    /// ③ <c>StampReceived</c> 一律拿「现在」当落箱时刻 —— 于是十几封并列 00:00，
    ///    或者并列在玩家点「推进」的那一分钟，看起来像系统一口气群发。
    ///
    /// 修法（两步，都在 <c>StampReceived</c> 里）：
    /// ① <see cref="InboxTime.OfficeMoment"/>：非办公时段挪进当日 08:30–18:30，按 id 派生到具体分钟；
    /// ② <see cref="InboxManager.SpreadMinute"/>：与已有邮件撞同一分钟的，按 id 派生 3–9 分钟错开。
    /// 两步都不用 <c>Random</c>/<c>GetHashCode</c> —— 同一份存档反复打开必须给出同一组时刻。
    ///
    /// 本回归只钉**数据口径**：落箱时刻的唯一性、办公时段、确定性、不动期限字段。
    /// 「列表上看着像不像真的」仍是界面手测，本文档不宣称已目视确认。
    /// </summary>
    public static class InboxArrivalRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__inbox_arrival__");
            try
            {
                bool all = true;

                // ———— ① 办公时段 ————
                // 本地 00:05 落到收件箱 → 必须被挪进 08:30–18:30。
                var gs = Fresh();
                gs.SetSimUtc(ToUtc(new DateTime(2026, 11, 5, 0, 5, 0, DateTimeKind.Unspecified)));

                var night = new InboxMail { id = "arr-night", sender = "运营部", subject = "凌晨到件" };
                InboxManager.Add(gs, night);
                var nightLocal = InboxTime.ToLocal(night);
                int nightMinute = nightLocal.Hour * 60 + nightLocal.Minute;
                bool office = nightMinute >= InboxTime.OfficeStartMinute
                              && nightMinute <= InboxTime.OfficeEndMinute;
                all &= office;
                if (!office)
                    Debug.LogError($"落箱-① 凌晨到件没进办公时段：{InboxTime.Clock(night)}（期望 08:30–18:30）");

                // ①b 不得制造「未来时间」：现在就是 09:10，挪出来的时刻不能晚于它
                var gsEarly = Fresh();
                gsEarly.SetSimUtc(ToUtc(new DateTime(2026, 11, 5, 9, 10, 0, DateTimeKind.Unspecified)));
                var early = new InboxMail { id = "arr-early", sender = "战略发展部", subject = "清晨到件" };
                InboxManager.Add(gsEarly, early);
                bool notFuture = InboxTime.ToLocal(early)
                                 <= gsEarly.SimUtc + DateTimeOffset.Now.Offset;
                all &= notFuture;
                if (!notFuture)
                    Debug.LogError($"落箱-①b 落箱时刻晚于游戏时钟：{InboxTime.FullStamp(early)}，"
                                   + $"而现在是 {(gsEarly.SimUtc + DateTimeOffset.Now.Offset):HH:mm}");

                // ———— ② 同分钟去重（端到端：走 Add 的整条路径） ————
                // 6 封信在同一瞬间到件（典型的「推进一天」场景）→ 6 个不同的时分。
                var burst = BurstRun(out var gsBurst, out string firstPass);
                bool distinct = Distinct(burst);
                all &= distinct;
                if (!distinct)
                    Debug.LogError("落箱-② 同一瞬间到件的 6 封仍有重分钟：" + Clocks(burst));

                // ②b 错开只动时分，不改日期分组：6 封必须还落在同一天
                string day0 = InboxTime.DayKey(burst[0]);
                bool sameDay = true;
                for (int i = 1; i < burst.Count; i++)
                    if (InboxTime.DayKey(burst[i]) != day0) sameDay = false;
                all &= sameDay;
                if (!sameDay) Debug.LogError("落箱-②b 错开时刻把信挪到了别的日期分组");

                // ———— ③ 确定性 ————
                // 同一份「六封同时到」的输入跑两遍，必须给出同一组时分（散列而非随机）。
                var burst2 = BurstRun(out _, out string secondPass);
                bool deterministic = secondPass == firstPass;
                all &= deterministic;
                if (!deterministic)
                    Debug.LogError($"落箱-③ 两次同样的输入给出了不同时刻：{firstPass} vs {secondPass}");

                // ———— ④ 不动判据 ————
                // 错开的只是显示时刻，收件季度 / 状态 / 期限一律原样。
                var judge = new InboxMail
                {
                    id = "arr-judge", sender = "运营部", subject = "判据不变",
                    status = "pending", deadlineQuarters = 2
                };
                InboxManager.Add(gsBurst, judge);
                bool judgeKept = judge.status == "pending" && judge.deadlineQuarters == 2
                                 && judge.receivedYear == gsBurst.year
                                 && judge.receivedQuarter == gsBurst.quarter;
                all &= judgeKept;
                if (!judgeKept)
                    Debug.LogError("落箱-④ 落箱时刻的错开动了状态/期限/收件季度（只该动时分）");

                // ———— ⑤ 错开函数本身 + canary ————
                // 真实现必须让 6 封互不相同；「原样返回」这个已知错误实现必须被判不通过。
                bool realSpread = SpreadAvoidsSameMinute(
                    (g, utc, id) => InboxManager.SpreadMinute(g, utc, id), expectPass: true, "真实现");
                all &= realSpread;
                bool canary = SpreadAvoidsSameMinute(
                    (g, utc, id) => utc, expectPass: false, "canary-原样返回");
                all &= canary;

                // ———— ⑥ 落款日期 ————
                // 2026-11-03 报障：「邮件落款日期是 11 月 3 日，这是明显的错误」——
                // 那一格原先取 DateTime.Now（玩家电脑的系统日期），与游戏日历毫无关系。
                // 现在写信只留占位符，落箱时才兑成**到件那一天**。
                var gsSeal = Fresh();
                gsSeal.SetSimUtc(ToUtc(new DateTime(2027, 3, 12, 10, 0, 0, DateTimeKind.Unspecified)));
                var sealMail = new InboxMail
                {
                    id = "arr-seal", sender = "运营部", role = "开航研究会", subject = "落款测试",
                    body = InboxLetter.Frame(gsSeal, "运营部", "开航研究会", "落款测试", "正文", null)
                };
                bool tokenOnly = sealMail.body.Contains(InboxLetter.DateToken);
                all &= tokenOnly;
                if (!tokenOnly) Debug.LogError("落款-⑥ 写信时就把日期写死了（应留占位，落箱再兑）");

                InboxManager.Add(gsSeal, sealMail);
                string wantDate = "2027年03月12日";
                string todayText = DateTime.Now.ToString("yyyy年MM月dd日");
                bool notSystemClock = todayText == wantDate || !sealMail.body.Contains(todayText);
                bool sealedOk = !sealMail.body.Contains(InboxLetter.DateToken)
                                && sealMail.body.Contains(wantDate)
                                && notSystemClock;
                all &= sealedOk;
                if (!sealedOk)
                    Debug.LogError($"落款-⑥b 落箱后落款不是到件日「{wantDate}」："
                                   + $"残留下载符={sealMail.body.Contains(InboxLetter.DateToken)}");

                // ———— ⑦ 转呈件落款：文日 ≠ 转呈日 ————
                // 2026-11-04 报障：「左侧归在 11 月 3 日的邮件，落款写着 11 月 4 日」。
                // 病根是转呈落款直接用了来文的**文日 / 生效日**（可以是未来的日子），
                // 而落款该写「这封信递到 CEO 桌上那天」。用凌晨推进的存档最容易撞上：
                // 落箱会被挪进前一天办公时段，文日却还是当天 —— 两者必然分叉。
                var gsNews = Fresh();
                gsNews.dynamicFeed = new List<DynamicFeedItem>();
                gsNews.externalEvents = new List<AirlineCEO.Data.ExternalEvent>();
                gsNews.staffPool = new List<AirlineCEO.Data.PersonnelData>();
                gsNews.ceoNationality = "中国";
                gsNews.companyNameZh = "天际航空";
                gsNews.companyIata = "NV";
                gsNews.hubIata = "PEK";
                gsNews.ceoDisplayName = "阿斯芬";
                gsNews.SetSimUtc(ToUtc(new DateTime(2030, 5, 11, 4, 0, 0, DateTimeKind.Unspecified)));

                var payload = ExternalDataFetcher.DemoPayloads()[1];
                ExternalEventManager.Receive(gsNews, payload);
                var newsMail = gsNews.inbox.Count > 0 ? gsNews.inbox[0] : null;
                bool newsSent = newsMail != null && newsMail.action == "news";

                string tailDate = newsSent ? LastNonEmptyLine(newsMail.body) : "";
                string wantNewsDate = newsSent ? InboxTime.DateText(newsMail) : "";
                bool signUsesArrival = newsSent && tailDate == wantNewsDate;
                // canary：落款若仍在用文日（当天 11 日），就会被下面这条抓出来
                bool notDocDate = newsSent && tailDate != "2030年05月11日"
                                  && tailDate != "2030年5月11日";
                // 抬头那格仍要来文文日 —— 别为了修落款把原文日期也改掉（GameDateLabel 不补零）
                bool headKeepsDocDate = newsSent && newsMail.body.Contains("2030年5月11日");

                all &= signUsesArrival && notDocDate && headKeepsDocDate;
                if (!signUsesArrival || !notDocDate || !headKeepsDocDate)
                    Debug.LogError($"落款-⑦ 转呈件落款不按到件日：落款「{tailDate}」/"
                                   + $"到件日「{wantNewsDate}」/ 抬头留文日={headKeepsDocDate}"
                                   + $" / 正文头 80 字「{(newsSent ? newsMail.body.Substring(0, Math.Min(80, newsMail.body.Length)) : "")}」");

                // ———— ⑧ 旧档兜底 ————
                // 已经落进存档的转呈件正文不再重写（不覆盖历史），但显示时要把末行日期纠正到到件日；
                // 否则玩家打开旧档，看到的还是「11 月 3 日收到的信，落款 11 月 4 日」。
                var gsLegacy = Fresh();
                gsLegacy.SetSimUtc(ToUtc(new DateTime(2030, 5, 11, 4, 0, 0, DateTimeKind.Unspecified)));
                var legacy = new InboxMail
                {
                    id = "arr-legacy-news", sender = "新闻官 卢曦", role = "新闻官 · 预警通报",
                    kind = "report", action = "news", subject = "台风预警", status = "approved",
                    body = "正文\n\n" + OfficialBulletin.RightAligned("新闻官 卢曦") + "\n"
                           + OfficialBulletin.RightAligned("2030年5月11日")
                };
                InboxManager.Add(gsLegacy, legacy);
                string legacyShown = InboxLetter.FrameIfNeeded(gsLegacy, legacy, legacy.body);
                bool legacyFixed = LastNonEmptyLine(legacyShown) == InboxTime.DateText(legacy);
                all &= legacyFixed;
                if (!legacyFixed)
                    Debug.LogError($"落款-⑧ 旧档转呈日期未被纠正到到件日：末行「{LastNonEmptyLine(legacyShown)}」"
                                   + $"/ 到件日「{InboxTime.DateText(legacy)}」");

                if (all)
                    Debug.Log("收件箱落箱时刻：办公时段 + 同分钟错开 + 确定性 + 判据不变 + 落款取到件日"
                              + " + 转呈落款不取文日 + 旧档兜底，8 组口径全部通过");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static GameState Fresh()
        {
            var host = new GameObject("__inbox_arrival_gs__");
            var gs = host.AddComponent<GameState>();
            gs.inbox = new List<InboxMail>();
            gs.pendingInbox = new List<PendingInboxMail>();
            gs.year = 1;
            gs.quarter = 1;
            return gs;
        }

        static List<InboxMail> BurstRun(out GameState gs, out string clocks)
        {
            gs = Fresh();
            gs.SetSimUtc(ToUtc(new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Unspecified)));
            var list = new List<InboxMail>();
            for (int i = 0; i < 6; i++)
            {
                var m = new InboxMail { id = $"arr-burst-{i}", sender = "运营部", subject = $"批量到件 {i}" };
                InboxManager.Add(gs, m);
                list.Add(m);
            }
            clocks = Clocks(list);
            return list;
        }

        /// <summary>
        /// 6 封同一瞬间的信交给 <paramref name="spread"/> 定落箱时刻，看是否互不相同。
        /// 接受委托是为了 canary：把「原样返回」喂进来，它必须判 fail。
        /// </summary>
        static bool SpreadAvoidsSameMinute(Func<GameState, DateTime, string, DateTime> spread,
            bool expectPass, string label)
        {
            var gs = Fresh();
            var baseUtc = ToUtc(new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Unspecified));
            gs.SetSimUtc(baseUtc);
            var seen = new HashSet<string>();
            for (int i = 0; i < 6; i++)
            {
                string id = $"spread-{i}";
                var m = new InboxMail { id = id, sender = "运营部", subject = "撞车测试" };
                m.receivedUtcTicks = spread(gs, baseUtc, id).Ticks;
                m.time = InboxTime.Clock(m);
                gs.inbox.Insert(0, m);
                seen.Add(InboxTime.Clock(m));
            }
            bool pass = seen.Count == 6;
            if (pass != expectPass)
            {
                Debug.LogError($"落箱-⑤「{label}」判定不符：6 封得到 {seen.Count} 个不同时分"
                               + $"（期望 {(expectPass ? "6 个" : "判不通过")}）");
                return false;
            }
            return true;
        }

        /// <summary>正文最后一行非空文字（转呈件落款就写在末行）。</summary>
        static string LastNonEmptyLine(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            var lines = body.Replace("\r\n", "\n").Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string l = lines[i].Trim();
                if (l.Length > 0) return l;
            }
            return "";
        }

        static bool Distinct(List<InboxMail> list)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
                if (!seen.Add(InboxTime.Clock(list[i]))) return false;
            return true;
        }

        /// <summary>把「本地时刻」换算成游戏时钟用的 UTC（与 InboxTime.ToLocal 同一套偏移）。</summary>
        static DateTime ToUtc(DateTime local)
            => DateTime.SpecifyKind(local.AddMinutes(-DateTimeOffset.Now.Offset.TotalMinutes), DateTimeKind.Utc);

        static string Clocks(List<InboxMail> list)        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(InboxTime.Clock(list[i]));
            }
            return sb.ToString();
        }
    }
}
#endif
