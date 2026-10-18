using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 外部事件：接收 → 映射到游戏时间轴 → 入队 → 按游戏时钟触发。
    /// 与现实流速完全解耦；快进只加快游戏时间，事件跟着游戏时间走。
    /// </summary>
    public static class ExternalEventManager
    {
        public const int MaxStored = 80;
        public const int AbsenceThreshold = 5;

        public static event Action Changed;

        public static List<ExternalEvent> Ensure(GameState gs)
        {
            if (gs == null) return null;
            if (gs.externalEvents == null) gs.externalEvents = new List<ExternalEvent>();
            return gs.externalEvents;
        }

        /// <summary>接入一条外部事件：打游戏时间戳，隐去现实日期，按生效时间入队。</summary>
        public static ExternalEvent Receive(GameState gs, ExternalEventPayload payload)
        {
            if (gs == null || payload == null) return null;
            var list = Ensure(gs);
            var now = ExternalTimeMapper.EnsureUtc(gs.SimUtc);
            var ev = ExternalTimeMapper.ToEvent(payload, now);
            MarkIsolated(list, ev);

            int exist = IndexOfId(list, ev.id);
            bool first = exist < 0;
            if (exist >= 0)
            {
                var old = list[exist];
                if (old.isTriggered && !old.isResolved)
                    ev.isTriggered = true;
                list[exist] = ev;
            }
            else
            {
                list.Add(ev);
            }

            SortByEffective(list);
            while (list.Count > MaxStored)
                list.RemoveAt(0);

            if (first)
                PushReceiveMail(gs, ev);
            Changed?.Invoke();
            gs.NotifyChanged();
            return ev;
        }

        public static int ReceiveMany(GameState gs, List<ExternalEventPayload> payloads)
        {
            if (payloads == null || payloads.Count == 0) return 0;
            int n = 0;
            for (int i = 0; i < payloads.Count; i++)
                if (Receive(gs, payloads[i]) != null) n++;
            return n;
        }

        /// <summary>游戏时钟从 from→to 时检查到期事件并触发。</summary>
        public static int Tick(GameState gs, DateTime fromUtc, DateTime toUtc)
        {
            if (gs == null) return 0;
            var list = Ensure(gs);
            var from = ExternalTimeMapper.EnsureUtc(fromUtc);
            var to = ExternalTimeMapper.EnsureUtc(toUtc);
            if (to < from) return 0;

            MaybeAbsenceBriefing(gs, from, to, list);
            ExpireFreeze(gs, to);

            int fired = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var ev = list[i];
                if (ev == null || ev.isTriggered) continue;
                var due = ev.GameEffectiveTime;
                if (due > to) continue;
                Trigger(gs, ev);
                fired++;
            }

            // 现实数据源：按现实节拍后台拉取（受间隔约束），再把结果并入队列
            // （仅真实局；自测用的临时 GameState 不触发网络，保证自测无副作用）
            if (ReferenceEquals(gs, GameState.Instance))
            {
                ExternalDataFetcher.RequestRefresh();
                ExternalDataFetcher.RequestQuoteRefresh();
                DrainSource(gs);
            }

            if (fired > 0)
            {
                SortByEffective(list);
                Changed?.Invoke();
                gs.NotifyChanged();
            }
            return fired;
        }

        /// <summary>
        /// 排空现实数据源缓存并入队（每游戏分钟由 Tick 调用）。
        /// 返回本轮入队条数；未配置源 / 断网 / 无新内容时返回 0。
        /// </summary>
        public static int DrainSource(GameState gs)
        {
            if (gs == null) return 0;
            var arrived = ExternalDataFetcher.FetchEvents();
            if (arrived == null || arrived.Count == 0) return 0;
            return ReceiveMany(gs, arrived);
        }

        public static List<ExternalEvent> Pending(GameState gs)
        {
            var result = new List<ExternalEvent>();
            var list = Ensure(gs);
            if (list == null) return result;
            for (int i = 0; i < list.Count; i++)
            {
                var ev = list[i];
                if (ev != null && !ev.isTriggered) result.Add(ev);
            }
            return result;
        }

        /// <summary>AO 口语化简报：只谈游戏内相对时间。</summary>
        public static string GetAdvisorBriefing(GameState gs)
        {
            var pending = Pending(gs);
            if (pending.Count > 0)
            {
                var ev = pending[0];
                string when = ev.relativeDesc;
                if (string.IsNullOrEmpty(when)) when = "即将";
                string where = string.IsNullOrEmpty(ev.affectedRegion) ? "" : $"，影响 {ev.affectedRegion}";
                return $"老板，{ev.source}刚来消息——{ev.title}。{when}{where}。{Short(ev.content, 80)}";
            }
            if (gs?.dynamicFeed != null && gs.dynamicFeed.Count > 0)
            {
                var it = gs.dynamicFeed[gs.dynamicFeed.Count - 1];
                return $"老板，看到新闻了没？{it.source}刚报道——{it.headline}。";
            }
            return "";
        }

        /// <summary>主页跑马灯：待生效 + 刚生效，全部是游戏时间措辞。</summary>
        public static string TickerText(GameState gs)
        {
            var list = Ensure(gs);
            if (list == null || list.Count == 0) return "";
            var sb = new StringBuilder();
            int shown = 0;
            for (int i = 0; i < list.Count && shown < 4; i++)
            {
                var ev = list[list.Count - 1 - i];
                if (ev == null) continue;
                if (sb.Length > 0) sb.Append("　　·　　");
                string tag = ev.isTriggered ? "已生效" : ev.relativeDesc;
                sb.Append(ev.title).Append("（").Append(tag).Append("）");
                shown++;
            }
            return sb.ToString();
        }

        public static bool IsAirportFrozen(GameState gs, string iata)
        {
            if (gs == null || string.IsNullOrEmpty(iata)) return false;
            if (gs.externalFreezeUntilTicks <= 0) return false;
            if (gs.SimUtc.Ticks >= gs.externalFreezeUntilTicks) return false;
            if (string.IsNullOrEmpty(gs.externalFreezeRegion)) return true;
            return string.Equals(gs.externalFreezeRegion, iata, StringComparison.OrdinalIgnoreCase);
        }

        public static void Restore(GameState gs, ExternalEventListSave save)
        {
            if (gs == null) return;
            gs.externalEvents = new List<ExternalEvent>();
            if (save?.items != null) gs.externalEvents.AddRange(save.items);
            gs.externalFreezeRegion = save?.freezeRegion ?? "";
            gs.externalFreezeUntilTicks = save?.freezeUntilTicks ?? 0;
        }

        public static ExternalEventListSave Snapshot(GameState gs)
        {
            return new ExternalEventListSave
            {
                items = gs?.externalEvents != null
                    ? new List<ExternalEvent>(gs.externalEvents) : new List<ExternalEvent>(),
                freezeRegion = gs?.externalFreezeRegion ?? "",
                freezeUntilTicks = gs?.externalFreezeUntilTicks ?? 0
            };
        }

        static void Trigger(GameState gs, ExternalEvent ev)
        {
            ev.isTriggered = true;
            ApplyEffect(gs, ev);
            if (ev.durationHours <= 0) ev.isResolved = true;
            else
            {
                var until = ev.GameEffectiveTime.AddHours(ev.durationHours);
                if (gs.SimUtc >= until) ev.isResolved = true;
            }

            AppendFeed(gs, ev);
            // 无公司侧影响的事件（如真实订阅源的消息稿）不再补一封「已施行」确认函，避免只用内容刷满收件箱
            if (HasImpact(ev))
                PushTriggerMail(gs, ev);
            gs.AddDecisionLog($"外部事件生效：{ev.title}（{ev.relativeDesc}）");
        }

        static void ApplyEffect(GameState gs, ExternalEvent ev)
        {
            string t = (ev.effectType ?? "").Trim();
            float v = ev.effectValue;
            if (t.Equals("fuelPrice", StringComparison.OrdinalIgnoreCase)
                || t.Equals("FuelCost", StringComparison.OrdinalIgnoreCase))
            {
                gs.fuelPricePerTon = Mathf.Max(400f, gs.fuelPricePerTon * (1f + v / 100f));
                PushFuelHistory(gs);
            }
            else if (t.Equals("fuelAbs", StringComparison.OrdinalIgnoreCase))
            {
                gs.fuelPricePerTon = Mathf.Max(400f, gs.fuelPricePerTon + v);
                PushFuelHistory(gs);
            }
            else if (t.Equals("loadFactor", StringComparison.OrdinalIgnoreCase))
                gs.loadFactorPercent = Mathf.Clamp(gs.loadFactorPercent + v, 0f, 100f);
            else if (t.Equals("onTime", StringComparison.OrdinalIgnoreCase))
                gs.onTimePercent = Mathf.Clamp(gs.onTimePercent + v, 0f, 100f);
            else if (t.Equals("cash", StringComparison.OrdinalIgnoreCase))
                gs.cash += v;
            else if (t.Equals("satisfaction", StringComparison.OrdinalIgnoreCase))
                gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + v, 0f, 100f);
            else if (t.Equals("freezeFlights", StringComparison.OrdinalIgnoreCase))
            {
                int hours = ev.durationHours > 0 ? ev.durationHours : 48;
                gs.externalFreezeRegion = ev.affectedRegion ?? "";
                gs.externalFreezeUntilTicks = ev.GameEffectiveTime.AddHours(hours).Ticks;
            }
            else if (t.Equals("compliance", StringComparison.OrdinalIgnoreCase))
            {
                MessageCenter.Push(MessagePriority.Warning, "n19_安全质量部", ev.source,
                    ev.title, ev.content, ExternalTimeMapper.GameDateLabel(gs.SimUtc),
                    gs.year, gs.quarter);
            }
        }

        static void PushFuelHistory(GameState gs)
        {
            if (gs.fuelPriceHistory == null) gs.fuelPriceHistory = new List<float>();
            gs.fuelPriceHistory.Add(gs.fuelPricePerTon);
            while (gs.fuelPriceHistory.Count > 12)
                gs.fuelPriceHistory.RemoveAt(0);
        }

        static void ExpireFreeze(GameState gs, DateTime now)
        {
            if (gs.externalFreezeUntilTicks > 0 && now.Ticks >= gs.externalFreezeUntilTicks)
            {
                gs.externalFreezeRegion = "";
                gs.externalFreezeUntilTicks = 0;
            }
            var list = gs.externalEvents;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var ev = list[i];
                if (ev == null || ev.isResolved || !ev.isTriggered || ev.durationHours <= 0) continue;
                if (now >= ev.GameEffectiveTime.AddHours(ev.durationHours))
                    ev.isResolved = true;
            }
        }

        static void AppendFeed(GameState gs, ExternalEvent ev)
        {
            if (gs.dynamicFeed == null) gs.dynamicFeed = new List<DynamicFeedItem>();
            gs.dynamicFeed.Add(new DynamicFeedItem
            {
                id = ev.id.GetHashCode() & 0x7fffffff,
                category = CategoryOf(ev.category),
                effect = EffectOf(ev.effectType),
                effectValue = ev.effectValue,
                source = ev.source,
                headline = ev.title,
                body = ev.content,
                timeLabel = ExternalTimeMapper.GameDateTimeLabel(ev.GameEffectiveTime),
                year = gs.year,
                quarter = gs.quarter,
                stamp = ev.gameEffectiveTicks,
                generated = ev.generated
            });
            if (gs.dynamicFeed.Count > DynamicFeedManager.MaxFeedItems)
                gs.dynamicFeed.RemoveAt(0);
        }

        static void PushReceiveMail(GameState gs, ExternalEvent ev)
        {
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string date = ExternalTimeMapper.GameDateLabel(ev.GameReceivedTime);
            string role = OfficialBulletin.RoleLabel(ev.source, ev.category);
            string body = OfficialBulletin.Compose(ev, date, triggered: false);
            if (ev.isolated)
                body += "\n\n" + InboxLetter.Bullet("（未见前置来文，本条按孤立事件归档。）");
            if (HasImpact(ev))
                body += "\n\n" + AnnotLine + "\n"
                    + InboxLetter.Section("对公司的影响（预判）")
                    + InboxLetter.Bullet(ImpactLine(ev, forecast: true));
            InboxManager.Add(gs, new InboxMail
            {
                id = "ext_r_" + ev.id + "_" + ev.gameReceivedTicks,
                sender = ev.source,
                role = role,
                glyph = "讯",
                kind = KindOf(ev.source),
                action = "news",
                origin = ev.affectedRegion ?? "",
                subject = $"{date} · {ev.title}",
                body = body,
                status = "approved",
                tag = ev.relativeOffsetHours <= 0.01f
                    ? (OfficialBulletin.StyleOf(ev.source) == OfficialBulletin.Style.News ? "快讯" : "即时")
                    : "预告",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        static void PushTriggerMail(GameState gs, ExternalEvent ev)
        {
            string date = ExternalTimeMapper.GameDateLabel(ev.GameEffectiveTime);
            string role = OfficialBulletin.StyleOf(ev.source) == OfficialBulletin.Style.News
                ? "后续报道"
                : "施行通报";
            var body = new StringBuilder();
            body.Append(OfficialBulletin.Compose(ev, date, triggered: true));
            body.Append("\n\n").Append(AnnotLine).Append('\n');
            body.Append(InboxLetter.Section("对公司的影响"));
            body.Append(InboxLetter.Bullet(ImpactLine(ev, forecast: false)));
            InboxManager.Add(gs, new InboxMail
            {
                id = "ext_t_" + ev.id + "_" + ev.gameEffectiveTicks,
                sender = ev.source,
                role = role,
                glyph = "讯",
                kind = KindOf(ev.source),
                action = "news",
                origin = ev.affectedRegion ?? "",
                subject = $"{date} · {ev.title}（已施行）",
                body = body.ToString(),
                status = "approved",
                tag = "生效",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        /// <summary>来文分类：通讯社报道=媒体，其余（政府机关、制造商、企业）=来文。</summary>
        static string KindOf(string source)
            => OfficialBulletin.StyleOf(source) == OfficialBulletin.Style.News ? "media" : "official";

        /// <summary>公司侧批注分隔语（来文正文之外的内部标注，避免与公文体例混读）。</summary>
        const string AnnotLine = "<color=#7C8FA6>——以下为公司内部标注，非来文内容——</color>";

        /// <summary>是否有可量化的公司侧影响（决定是否附加内部标注）。</summary>
        static bool HasImpact(ExternalEvent ev)
        {
            string t = (ev.effectType ?? "").Trim();
            return t.Length > 0
                && !t.Equals("None", StringComparison.OrdinalIgnoreCase)
                && !t.Equals("none", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>公司侧影响一句话（航司口径，供 CEO 直读）；forecast=来文为预告时用「预计」。</summary>
        static string ImpactLine(ExternalEvent ev, bool forecast)
        {
            string head = forecast ? "预计" : "";
            string t = (ev.effectType ?? "").Trim();
            if (t.Equals("fuelPrice", StringComparison.OrdinalIgnoreCase)
                || t.Equals("FuelCost", StringComparison.OrdinalIgnoreCase))
                return $"{head}航油成本 {(ev.effectValue >= 0 ? "+" : "")}{ev.effectValue:F0}%，后续航班油料支出随之调整。";
            if (t.Equals("fuelAbs", StringComparison.OrdinalIgnoreCase))
                return $"{head}航油价格每吨 {(ev.effectValue >= 0 ? "+" : "")}{ev.effectValue:F0} 美元，后续航班油料支出随之调整。";
            if (t.Equals("loadFactor", StringComparison.OrdinalIgnoreCase))
                return $"{head}客座率 {(ev.effectValue >= 0 ? "+" : "")}{ev.effectValue:F0} 个百分点。";
            if (t.Equals("onTime", StringComparison.OrdinalIgnoreCase))
                return $"{head}准点率 {(ev.effectValue >= 0 ? "+" : "")}{ev.effectValue:F0} 个百分点。";
            if (t.Equals("cash", StringComparison.OrdinalIgnoreCase))
                return $"{head}账上现金 {(ev.effectValue >= 0 ? "+" : "")}{ev.effectValue:F0} 万美元。";
            if (t.Equals("satisfaction", StringComparison.OrdinalIgnoreCase))
                return $"{head}董事会满意度 {(ev.effectValue >= 0 ? "+" : "")}{ev.effectValue:F0} 点。";
            if (t.Equals("freezeFlights", StringComparison.OrdinalIgnoreCase))
                return forecast
                    ? $"{(string.IsNullOrEmpty(ev.affectedRegion) ? "相关机场" : ev.affectedRegion)} 起降可能受限，届时受影响班次不予放行，请预留备份运力。"
                    : $"{(string.IsNullOrEmpty(ev.affectedRegion) ? "相关机场" : ev.affectedRegion)} 起降受限，受影响班次在恢复正常前不予放行。";
            if (t.Equals("compliance", StringComparison.OrdinalIgnoreCase))
                return forecast ? "预计将接到合规检查要求，请留意后续整改事项。"
                                : "安全质量部已接到合规检查要求，请留意后续整改事项。";
            return forecast ? "本条为行业动态，暂无直接财务影响，请留意趋势变化。"
                            : "本条为行业动态，暂不影响公司运行数据。";
        }

        static void MaybeAbsenceBriefing(GameState gs, DateTime from, DateTime to,
            List<ExternalEvent> list)
        {
            if ((to - from).TotalHours < 36) return;
            int pending = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !list[i].isTriggered) pending++;
            if (pending < AbsenceThreshold) return;
            if (HasMailId(gs, "ext_brief_" + gs.year + "q" + gs.quarter + "d" + gs.simDay))
                return;

            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section("离席简报"));
            sb.Append(InboxLetter.Bullet($"您离开期间，游戏日历已推进至 {ExternalTimeMapper.GameDateTimeLabel(to)}。"));
            sb.Append(InboxLetter.Bullet($"待生效动态 {pending} 条，已按公司日历排队："));
            int n = 0;
            for (int i = 0; i < list.Count && n < 8; i++)
            {
                var ev = list[i];
                if (ev == null || ev.isTriggered) continue;
                sb.Append(InboxLetter.Bullet($"{ev.title}　{ev.relativeDesc}"));
                n++;
            }
            InboxManager.Add(gs, new InboxMail
            {
                id = "ext_brief_" + gs.year + "q" + gs.quarter + "d" + gs.simDay,
                sender = "AO",
                role = "离席简报",
                glyph = "顾",
                kind = "report",
                action = "news",
                subject = $"{ExternalTimeMapper.GameDateLabel(to)} · 离席简报",
                body = InboxLetter.Frame(gs, "AO", "离席简报", "离席简报", sb.ToString(),
                    "本封为告知，无需批复。"),
                status = "approved",
                tag = "简报",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        static bool HasMailId(GameState gs, string id)
        {
            if (gs?.inbox == null) return false;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && gs.inbox[i].id == id) return true;
            return false;
        }

        static void MarkIsolated(List<ExternalEvent> list, ExternalEvent ev)
        {
            if (string.IsNullOrEmpty(ev.chainId)) return;
            for (int i = 0; i < list.Count; i++)
            {
                var x = list[i];
                if (x != null && x.chainId == ev.chainId && x.id != ev.id)
                    return;
            }
            ev.isolated = true;
        }

        static int IndexOfId(List<ExternalEvent> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].id == id) return i;
            return -1;
        }

        static void SortByEffective(List<ExternalEvent> list)
        {
            list.Sort((a, b) =>
            {
                long ta = a != null ? a.gameEffectiveTicks : 0;
                long tb = b != null ? b.gameEffectiveTicks : 0;
                return ta.CompareTo(tb);
            });
        }

        static string Short(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        static int CategoryOf(string c)
        {
            if (string.IsNullOrEmpty(c)) return (int)FeedCategory.Market;
            if (c.IndexOf("监管", StringComparison.Ordinal) >= 0
                || c.Equals("Regulation", StringComparison.OrdinalIgnoreCase))
                return (int)FeedCategory.Regulation;
            if (c.IndexOf("季节", StringComparison.Ordinal) >= 0)
                return (int)FeedCategory.Seasonal;
            if (c.IndexOf("竞", StringComparison.Ordinal) >= 0)
                return (int)FeedCategory.Competitor;
            return (int)FeedCategory.Market;
        }

        static int EffectOf(string t)
        {
            if (string.IsNullOrEmpty(t)) return (int)FeedEffect.None;
            if (t.IndexOf("fuel", StringComparison.OrdinalIgnoreCase) >= 0
                || t.Equals("FuelCost", StringComparison.OrdinalIgnoreCase))
                return (int)FeedEffect.FuelCost;
            if (t.IndexOf("load", StringComparison.OrdinalIgnoreCase) >= 0)
                return (int)FeedEffect.LoadFactor;
            if (t.IndexOf("onTime", StringComparison.OrdinalIgnoreCase) >= 0)
                return (int)FeedEffect.OnTime;
            if (t.IndexOf("cash", StringComparison.OrdinalIgnoreCase) >= 0)
                return (int)FeedEffect.Cash;
            if (t.IndexOf("satis", StringComparison.OrdinalIgnoreCase) >= 0)
                return (int)FeedEffect.Satisfaction;
            return (int)FeedEffect.None;
        }
    }
}
