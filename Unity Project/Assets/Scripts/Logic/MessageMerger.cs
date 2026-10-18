using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 消息混合器（GDB 29 · 全球航空动态消息流）。
    /// 将外部现实数据与程序生成事件统一排序、去重、封顶，玩家无法分辨来源。
    /// </summary>
    public static class MessageMerger
    {
        /// <summary>合并两类数据：按 stamp 升序 + 同季度同标题去重 + 保留最新 maxItems。</summary>
        public static List<DynamicFeedItem> Merge(
            List<DynamicFeedItem> external, List<DynamicFeedItem> generated, int maxItems)
        {
            var all = new List<DynamicFeedItem>();
            if (external != null) all.AddRange(external);
            if (generated != null) all.AddRange(generated);

            all.Sort((a, b) => a.stamp.CompareTo(b.stamp));

            var seen = new HashSet<string>();
            var result = new List<DynamicFeedItem>();
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                string key = it.year + "_" + it.quarter + "_" + it.source + "_" + it.headline;
                if (seen.Add(key))
                    result.Add(it);
            }

            if (maxItems > 0 && result.Count > maxItems)
                result.RemoveRange(0, result.Count - maxItems);
            return result;
        }
    }
}
