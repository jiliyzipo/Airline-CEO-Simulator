using System;
using System.Collections.Generic;

namespace AirlineCEO.Data
{
    /// <summary>单次效果（Phase 12 · GDD 9.3）。</summary>
    [Serializable]
    public class EventEffect
    {
        /// <summary>cash / brand / satisfaction / fuelPrice / loadFactor / onTime / safetyIndex / monthlyBurn</summary>
        public string target;
        public float value;
        public bool isPercent;
    }

    /// <summary>随机事件定义（目录条目，可序列化）。</summary>
    [Serializable]
    public class RandomEventDef
    {
        public string id;
        public string title;
        public string description;
        public float weight = 1f;
        public string category;
        public EventEffect[] effects = Array.Empty<EventEffect>();
        public string chainEventId;
        public int chainDelayQuarters = 1;
        public bool isOneShot;
        /// <summary>是否占用本季「大油价事件」名额（与 TryOilEvent 互斥）。</summary>
        public bool consumesOilSlot;

        /// <summary>是否为负面事件（红色危机/警告级），用于坏运保护与同分类限流。</summary>
        public bool IsNegative()
        {
            if (effects == null) return false;
            for (int i = 0; i < effects.Length; i++)
            {
                var fx = effects[i];
                if (fx == null) continue;
                switch (fx.target)
                {
                    case "cash":
                    case "brand":
                    case "satisfaction":
                    case "onTime":
                    case "loadFactor":
                    case "safetyIndex":
                        if (fx.value < 0f) return true;
                        break;
                    case "fuelPrice":
                    case "monthlyBurn":
                        if (fx.value > 0f) return true;
                        break;
                }
            }
            return false;
        }
    }

    /// <summary>已触发事件日志条目。</summary>
    [Serializable]
    public class RandomEventLogEntry
    {
        public int year;
        public int quarter;
        public string eventId;
        public string title;
        public string category;
        public string summary;
        public string effectSummary;
    }

    [Serializable]
    public class RandomEventsSave
    {
        public string[] oneShotFiredIds;
        public RandomEventLogEntry[] log;
        public string pendingChainEventId;
        public int pendingChainQuarters;
        public string lastTriggeredId;
        public string lastTriggeredSummary;
        public int quartersSinceRedCrisis;
        public EventCategoryCounter[] categoryCounters;
    }

    public static class RandomEventCategories
    {
        public const string Natural = "自然灾害";
        public const string Human = "人为事故";
        public const string Market = "市场冲击";
        public const string Internal = "内部危机";
        public const string Policy = "政策变化";
        public const string Supply = "供应链";
    }

    /// <summary>单分类单财年触发计数（同分类限流用）。</summary>
    [Serializable]
    public class EventCategoryCounter
    {
        public string category;
        public int fiscalYear;
        public int count;
    }
}
