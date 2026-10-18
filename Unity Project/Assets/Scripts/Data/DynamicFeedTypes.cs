using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Data
{
    /// <summary>消息分类（GDB 22/29 交叉补强 · 全球航空动态消息流）。</summary>
    public enum FeedCategory
    {
        Market = 0,      // 市场：油价 / 汇率 / 行业供需
        Regulation = 1,  // 监管：民航局 / 反垄断 / 安全新规
        Industry = 2,    // 行业：制造商 / 交付 / 竞对
        Seasonal = 3,    // 季节：春运 / 黄金周 / 暑运
        Competitor = 4   // 竞对动态
    }

    /// <summary>事件对游戏状态的影响类型。</summary>
    public enum FeedEffect
    {
        None = 0,
        FuelCost = 1,     // 燃油成本 %（作用于 fuelPricePerTon）
        LoadFactor = 2,   // 客座率百分点（作用于 loadFactorPercent）
        OnTime = 3,       // 准点率百分点（作用于 onTimePercent）
        Cash = 4,         // 现金一次性（万美元）
        Satisfaction = 5  // 董事会满意度点（作用于 boardSatisfaction）
    }

    /// <summary>
    /// 单条「全球航空动态」消息。来源统一为新闻社/机构名（无痕伪装），
    /// 玩家无法分辨程序生成事件与真实外部数据。
    /// </summary>
    [Serializable]
    public class DynamicFeedItem
    {
        public int id;
        public int category;      // FeedCategory
        public int effect;        // FeedEffect
        public float effectValue; // 影响数值（% / 万 / 点）
        public string source;     // 新闻社（路透社 / 民航局 / 波音公司…）
        public string headline;   // 标题
        public string body;       // 正文
        public string timeLabel;  // 时间标签
        public int year;
        public int quarter;
        public long stamp;
        public bool generated;    // true=程序生成，false=外部数据（仅内部区分，不外显）
    }

    /// <summary>持久化容器。</summary>
    [Serializable]
    public class FeedSave
    {
        public List<DynamicFeedItem> items = new List<DynamicFeedItem>();
    }

    /// <summary>喂流元数据（连续盈利 / 油价平淡 / 无新闻季度计数）。</summary>
    [Serializable]
    public class FeedMetaSave
    {
        public int profitStreak;
        public int oilStableQuarters;
        public int quietQuarters;
    }
}
