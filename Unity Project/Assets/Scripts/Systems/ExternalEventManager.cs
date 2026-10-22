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

        /// <summary>额度见底时滞留待投的外部消息（仅内存：新局由 <see cref="ResetForNewGame"/> 清空）。</summary>
        static readonly List<ExternalEventPayload> Deferred = new List<ExternalEventPayload>();

        /// <summary>新局 / 回开始界面时调用：丢弃上一局没投完的滞留稿与去重表。</summary>
        public static void ResetForNewGame()
        {
            Deferred.Clear();
            ExternalDataFetcher.Reset();
            WeatherFeed.Reset();
            TyphoonFeed.Reset();
            SigmetFeed.Reset();
            GlobalStormFeed.Reset();
        }

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
                ExternalDataFetcher.RequestFxRefresh();
                WeatherFeed.RequestRefresh(gs);
                TyphoonFeed.RequestRefresh(gs);
                SigmetFeed.RequestRefresh(gs);
                GlobalStormFeed.RequestRefresh(gs);
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
        /// 排空现实数据源缓存并入队（每游戏分钟由 Tick 调用；界面轮询也会调，见 ClockLoop）。
        /// 返回本轮入队条数；未配置源 / 断网 / 无新内容时返回 0。
        /// <para>
        /// 三处硬约束都在这里：① 每游戏日入队上限（含台风与气象实况），防止一条事件被多家转载刷屏；
        /// ② 关键词研判影响每季度最多 <see cref="ExternalImpactRules.MaxInferredPerQuarter"/> 条，
        /// 避免自动扰动把经济掀翻（台风按官方路径研判，不占该配额）；
        /// ③ 额度见底时新闻进滞留队列、台风/气象直接丢弃（时效性更强，补投反而失真）。
        /// </para>
        /// </summary>
        public static int DrainSource(GameState gs)
        {
            if (gs == null) return 0;

            int n = 0;
            // 运行风险优先于情报：额度有限时先投 SIGMET / 台风 / 风暴 / 气象，再投新闻。
            // 这几类都有时效（下一报覆盖上一报），额度不足就丢弃、不补投。
            n += Admit(gs, SigmetFeed.Drain(), allowInference: false, requeue: false);
            n += Admit(gs, TyphoonFeed.Drain(), allowInference: false, requeue: false);
            n += Admit(gs, GlobalStormFeed.Drain(), allowInference: false, requeue: false);
            n += Admit(gs, WeatherFeed.Drain(), allowInference: false, requeue: false);

            // 上一轮因「每游戏日上限」用尽而滞留的消息接着补投：额度按游戏日恢复，
            // 攒下的稿子不必再走一次网络，也不会因为当轮额度见底被静默丢弃。
            if (Deferred.Count > 0)
            {
                var retry = new List<ExternalEventPayload>(Deferred);
                Deferred.Clear();
                n += Admit(gs, retry, allowInference: true, requeue: true);
            }

            n += Admit(gs, ExternalDataFetcher.FetchEvents(), allowInference: true, requeue: true);
            return n;
        }

        /// <summary>
        /// 开局预热：立刻发起一轮现实取数（强制，不受间隔约束）。
        /// 只是把网络请求提前，结果仍按游戏时钟投递 —— 「打开就能收到国内新闻」不用等第一个游戏分钟。
        /// </summary>
        public static void WarmUp(GameState gs)
        {
            if (gs == null || !ReferenceEquals(gs, GameState.Instance)) return;
            ExternalDataFetcher.RequestRefresh(force: true);
            ExternalDataFetcher.RequestQuoteRefresh(force: true);
            ExternalDataFetcher.RequestFxRefresh(force: true);
            WeatherFeed.RequestRefresh(gs, force: true);
            TyphoonFeed.RequestRefresh(gs, force: true);
            SigmetFeed.RequestRefresh(gs, force: true);
            GlobalStormFeed.RequestRefresh(gs, force: true);
        }

        static int Admit(GameState gs, List<ExternalEventPayload> arrived, bool allowInference,
            bool requeue = false)
        {
            if (arrived == null || arrived.Count == 0) return 0;
            var list = Ensure(gs);
            if (list == null) return 0;

            int budget = Mathf.Max(0, ExternalDataFetcher.DailyBudget - ReceivedToday(gs));
            if (budget <= 0)
            {
                if (requeue) Requeue(arrived, 0);
                return 0;
            }

            var operated = OperatedAirports(gs);
            int n = 0;
            int i = 0;
            for (; i < arrived.Count && budget > 0; i++)
            {
                var p = arrived[i];
                if (p == null) continue;
                if (!string.IsNullOrEmpty(p.id) && IndexOfId(list, p.id) >= 0) continue; // 已有：更新不计额度

                if (allowInference && string.IsNullOrEmpty(p.effectType))
                    InferImpact(gs, p, operated);

                if (Receive(gs, p) == null) continue;
                n++;
                budget--;
            }

            if (budget <= 0 && requeue) Requeue(arrived, i);
            return n;
        }

        /// <summary>额度见底时把没排上的稿子暂存，等下一个游戏日再投（有界，先进先出）。</summary>
        static void Requeue(List<ExternalEventPayload> arrived, int from)
        {
            const int maxPending = 24;
            for (int i = from; i < arrived.Count; i++)
            {
                var p = arrived[i];
                if (p == null || string.IsNullOrEmpty(p.id)) continue;
                if (Deferred.Exists(x => x != null && x.id == p.id)) continue;
                Deferred.Add(p);
            }
            while (Deferred.Count > maxPending) Deferred.RemoveAt(0);
        }

        /// <summary>现实内容本身不带数值：按关键词研判成保守影响，并标注「公司研判」。</summary>
        static void InferImpact(GameState gs, ExternalEventPayload p, List<string> operated)
        {
            if (InferredRecently(gs) >= ExternalImpactRules.MaxInferredPerQuarter) return;
            if (HasSimilarTitle(gs, p.title)) return;
            if (!ExternalImpactRules.TryInfer(p.title, p.content, operated, out var impact)) return;

            p.effectType = impact.type;
            p.effectValue = impact.value;
            if (!string.IsNullOrEmpty(impact.region)) p.affectedRegion = impact.region;
            if (impact.hours > 0) p.durationHours = impact.hours;
            p.inferred = true;
            p.impactNote = impact.note;
        }

        /// <summary>近一个季度（滚动 92 游戏日）内已生效/待生效的研判影响条数。</summary>
        static int InferredRecently(GameState gs)
        {
            var list = gs.externalEvents;
            if (list == null) return 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var ev = list[i];
                if (ev == null || !ev.inferred) continue;
                // 来自官方/结构化数据源的影响（台风路径、SIGMET、全球风暴定位）不占「每季 3 条」的关键词研判配额
                if (IsStructuredSource(ev.source)) continue;
                if ((gs.SimUtc - ev.GameReceivedTime).TotalDays <= 92) n++;
            }
            return n;
        }

        /// <summary>
        /// 结构化数据源（中央气象台路径 / 国际 SIGMET / NASA 全球风暴）：影响由几何距离算出，
        /// 不是关键词猜的，因此不占「每季 3 条」的关键词研判配额，也不会被误标成「关键词研判」。
        /// </summary>
        public static bool IsStructuredSource(string source)
        {
            if (string.IsNullOrEmpty(source)) return false;
            return source.Contains("气象台")
                   || source.Contains("航行警告")
                   || source.Contains("风暴监测")
                   || source.Contains("气象实况");
        }

        static bool HasSimilarTitle(GameState gs, string title)
        {
            var list = gs.externalEvents;
            if (list == null || string.IsNullOrEmpty(title)) return false;
            string key = ExternalDataFetcher.TitleKey(title);
            if (key.Length == 0) return false;
            for (int i = 0; i < list.Count; i++)
            {
                var ev = list[i];
                if (ev == null) continue;
                if (string.Equals(ExternalDataFetcher.TitleKey(ev.title), key, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public static int ReceivedToday(GameState gs)
        {
            var list = gs.externalEvents;
            if (list == null) return 0;
            var today = ExternalTimeMapper.Local(gs.SimUtc).Date;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var ev = list[i];
                if (ev == null) continue;
                if (ExternalTimeMapper.Local(ev.GameReceivedTime).Date == today) n++;
            }
            return n;
        }

        static List<string> OperatedAirports(GameState gs)
        {
            var set = new List<string>();
            void Add(string code)
            {
                if (string.IsNullOrEmpty(code)) return;
                code = code.Trim().ToUpperInvariant();
                if (!set.Contains(code)) set.Add(code);
            }
            Add(gs.hubIata);
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    Add(r.origin);
                    Add(r.destination);
                    Add(r.stopover);
                }
            }
            return set;
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
            if (ev.inferred)
                body += InboxLetter.Bullet("研判依据：" + InferNote(ev));
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
            if (ev.inferred) body.Append(InboxLetter.Bullet("研判依据：" + InferNote(ev)));
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

        /// <summary>研判依据（让玩家看出这是推断而非官方通报）。</summary>
        static string InferNote(ExternalEvent ev)
        {
            string basis = string.IsNullOrEmpty(ev.impactNote) ? "关键词研判" : ev.impactNote;
            // 台风 / SIGMET / 全球风暴都走官方或结构化数据，不是关键词猜的，也不占关键词研判配额：
            // 两类来源必须分开说明，否则玩家会以为这些影响也是「每季最多 3 条」的抽样。
            if (IsStructuredSource(ev.source))
                return basis + "（依据官方路径 / 航行警告等结构化数据，非新闻关键词研判）";
            return basis + $"（公司研判，非来文原文；每季度最多 {ExternalImpactRules.MaxInferredPerQuarter} 条）";
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
