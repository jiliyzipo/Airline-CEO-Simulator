using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 全球航空动态消息流（GDB 22/29 交叉补强 · P2 无痕）。
    /// 每季度：外部数据（离线占位）→ 事件发生器 → 消息混合器 → 应用影响 → 落库 + 推消息中心。
    /// 玩家看到的发件人统一为新闻社/机构名，无法分辨程序生成与真实数据。
    /// </summary>
    public static class DynamicFeedManager
    {
        public const int MaxFeedItems = 60;   // GameState.dynamicFeed 历史保留条数
        public const int PerQuarterCap = 3;   // 每季度最多注入条数
        static long _stamp;

        /// <summary>读档后恢复单调计数，避免 id/stamp 碰撞。</summary>
        public static void RestoreStamp(int maxStamp)
        {
            if (maxStamp > _stamp) _stamp = maxStamp;
        }

        /// <summary>季度钩子：生成并注入一批动态消息，返回展示文案。</summary>
        public static string OnQuarterTick(GameState gs, float quarterNet)
        {
            if (gs == null) return "";
            if (gs.dynamicFeed == null) gs.dynamicFeed = new List<DynamicFeedItem>();

            // 连续盈利季度计数（先更新，供「太安逸」类事件判定）
            gs.profitStreak = quarterNet > 0f ? gs.profitStreak + 1 : 0;

            // 1) 外部事件（现实内容 + 相对时间）→ 映射到游戏时间轴后入队，不立刻改数值
            var extEvents = ExternalDataFetcher.FetchEvents();
            ExternalEventManager.ReceiveMany(gs, extEvents);

            var externalNews = ExternalDataFetcher.FetchNews();

            // 2) 事件发生器（伪新闻，同样只提供内容，时间由游戏轴决定）
            var ctx = new EventGenerator.Context
            {
                oilPrice = gs.fuelPricePerTon,
                oilStableQuarters = gs.oilStableQuarters,
                quietQuarters = gs.quietQuarters,
                profitStreak = gs.profitStreak,
                onTimePercent = gs.onTimePercent,
                loadFactorPercent = gs.loadFactorPercent,
                boardSatisfaction = gs.boardSatisfaction,
                cash = gs.cash,
                year = gs.year,
                quarter = gs.quarter,
                seed = gs.year * 100L + gs.quarter
            };
            var generated = EventGenerator.Generate(ctx, PerQuarterCap, out int conditionalCount);

            // 3) 混合（统一排序/去重/封顶）后转入游戏时间队列
            var merged = MessageMerger.Merge(externalNews, generated, PerQuarterCap);

            var sb = new System.Text.StringBuilder();
            bool fuelFired = false;
            foreach (var item in merged)
            {
                _stamp++;
                item.id = (int)(_stamp & 0x7fffffff);
                item.stamp = _stamp;
                item.year = gs.year;
                item.quarter = gs.quarter;

                var payload = ToPayload(item);
                var ev = ExternalEventManager.Receive(gs, payload);
                if (ev != null)
                    item.timeLabel = ExternalTimeMapper.GameDateLabel(ev.GameReceivedTime);
                else
                    item.timeLabel = gs.DateLabel;

                if (item.effect == (int)FeedEffect.FuelCost) fuelFired = true;
                sb.AppendLine($"【{item.source}】{item.headline}");
            }

            DateTime now = gs.SimUtc;
            ExternalEventManager.Tick(gs, now, now);

            // 5) 稳定性计数（供下一季触发：油价平淡 / 无重大新闻）
            gs.oilStableQuarters = fuelFired ? 0 : gs.oilStableQuarters + 1;
            gs.quietQuarters = conditionalCount > 0 ? 0 : gs.quietQuarters + 1;

            return sb.ToString().TrimEnd();
        }

        /// <summary>供 AO（航空事务官）口播：优先读游戏时间轴上的待生效事件。</summary>
        public static string GetAdvisorBriefing(GameState gs)
            => ExternalEventManager.GetAdvisorBriefing(gs);

        static ExternalEventPayload ToPayload(DynamicFeedItem item)
        {
            return new ExternalEventPayload
            {
                id = "feed_" + item.id,
                category = ((FeedCategory)item.category).ToString(),
                title = item.headline,
                content = item.body,
                source = item.source,
                effectType = EffectName((FeedEffect)item.effect),
                effectValue = item.effectValue,
                generated = item.generated
            };
        }

        static string EffectName(FeedEffect e) => e switch
        {
            FeedEffect.FuelCost => "FuelCost",
            FeedEffect.LoadFactor => "loadFactor",
            FeedEffect.OnTime => "onTime",
            FeedEffect.Cash => "cash",
            FeedEffect.Satisfaction => "satisfaction",
            _ => ""
        };

        static void ApplyEffect(GameState gs, DynamicFeedItem item)
        {
            switch ((FeedEffect)item.effect)
            {
                case FeedEffect.FuelCost:
                    gs.fuelPricePerTon = Mathf.Max(400f, gs.fuelPricePerTon * (1f + item.effectValue / 100f));
                    break;
                case FeedEffect.LoadFactor:
                    gs.loadFactorPercent = Mathf.Clamp(gs.loadFactorPercent + item.effectValue, 0f, 100f);
                    break;
                case FeedEffect.OnTime:
                    gs.onTimePercent = Mathf.Clamp(gs.onTimePercent + item.effectValue, 0f, 100f);
                    break;
                case FeedEffect.Cash:
                    gs.cash += item.effectValue;
                    break;
                case FeedEffect.Satisfaction:
                    gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + item.effectValue, 0f, 100f);
                    break;
            }
        }
    }
}
