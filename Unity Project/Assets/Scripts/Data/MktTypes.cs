using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Data
{
    /// <summary>舱位标识（n45/n49 票价矩阵）。</summary>
    public static class CabinClasses
    {
        public const string First = "头等";
        public const string Business = "商务";
        public const string PremiumEconomy = "高端经济";
        public const string Economy = "经济";

        public static readonly string[] All = { First, Business, PremiumEconomy, Economy };
    }

    /// <summary>单航线定价覆盖：各舱位票价乘数（相对基准）与折扣（n45/n49）。</summary>
    [Serializable]
    public class RouteFareOverride
    {
        public string routeId;
        public float firstMult = 1f;
        public float businessMult = 1f;
        public float premiumEconomyMult = 1f;
        public float economyMult = 1f;
        /// <summary>预售/折扣率 0~1（0 无折扣）。</summary>
        public float discountRate;
        public string updatedBy = "CEO";
    }

    /// <summary>动态定价算法设置（n46）。</summary>
    [Serializable]
    public class DynamicPricingSettings
    {
        public bool enabled = true;
        public float competitorWeight = 0.5f;   // 竞对票价权重 0~1
        public float loadWeight = 0.5f;         // 航班负载权重 0~1
        public int holidaySensitivity = 1;      // 0低 1中 2高
        public float minFareFloorPercent = 90f; // 最低票价保护（不得低于成本的%）
        public int maxAdjustsPerDay = 3;        // 每日最多调价次数
    }

    /// <summary>辅助收入设置（n48）。金额单位为美元。</summary>
    [Serializable]
    public class AncillarySettings
    {
        public float baggageFeeUsd = 15f;   // 每件托运行李
        public float seatFeeUsd = 8f;       // 选座溢价
        public float changeFeeUsd = 30f;    // 退改签手续费
        public float onboardSalesUsd = 6f;  // 机上销售/人
        public float nonAirShare = 0.2f;    // 联名卡/保险/酒店佣金占辅助收入比
    }

    /// <summary>FFP 常旅客设置（n33）。</summary>
    [Serializable]
    public class FfpSettings
    {
        public int earnRatePerYuan = 10;    // 每消费 1 元累计里程
        public int redeemRatePerYuan = 100; // 每 1 元兑换所需里程
        public bool coBrandApproved;
        public int members = 120000;
        public float activeRate = 0.35f;    // 活跃率
    }

    /// <summary>产品工坊打包产品（n34）。</summary>
    [Serializable]
    public class ProductDesign
    {
        public string id;
        public string name;
        public string bundle;               // 机票+行李 / +贵宾厅 等
        public float priceUsd;
        public float expectedConversion;    // 0~1 预估转化率
        public bool launched;
    }

    /// <summary>旺季策略包（n57，四维度：运力/定价/服务/平季延伸）。</summary>
    [Serializable]
    public class PeakSeasonPackage
    {
        public string festival;             // 春节 / 暑运 / 国庆 …
        public int capacityChoice;          // 0保持 1加密 2窄转宽 3湿租/加班
        public int pricingChoice;           // 0无 1早鸟折扣 2旺季溢价 3舱位开放 4差旅收紧 5打包
        public int serviceChoice;           // 0无 1旅游航线 2节日体验 3特殊旅客服务
        public bool extendShoulder;         // 平季延伸
        public int extendDays;              // 延伸天数
        public float extendPricingMult = 1f;
        public bool applied;
    }

    /// <summary>市场销售部聚合状态（Phase 2 Week 7 · MKT 批次 A + Week 8 · 批次 B）。</summary>
    [Serializable]
    public class MktState
    {
        // —— 批次 A（收益/定价/季节） ——
        public List<RouteFareOverride> fareOverrides = new List<RouteFareOverride>();
        public DynamicPricingSettings dynamicPricing = new DynamicPricingSettings();
        public AncillarySettings ancillary = new AncillarySettings();
        public FfpSettings ffp = new FfpSettings();
        public List<ProductDesign> products = new List<ProductDesign>();
        public List<PeakSeasonPackage> seasonPackages = new List<PeakSeasonPackage>();

        // —— 批次 B（竞争/渠道/品牌） ——
        public ChannelBudget channelBudget = new ChannelBudget();
        public List<KeyAccount> keyAccounts = new List<KeyAccount>();
        public BrandCrisis brand = new BrandCrisis();
    }

    // ============================================================
    // 批次 B（竞争 / 渠道 / 品牌）
    // ============================================================

    /// <summary>渠道预算与分销成本设置（n68/n69/n70/n71）。货币「万美元」，费用「美元」。</summary>
    [Serializable]
    public class ChannelBudget
    {
        public float annualBudgetWan = 200f;     // 年度渠道总预算
        public float usedWan = 120f;             // 已使用
        public float directRatio = 0.35f;        // 直销占比 0~1（提直降代目标）
        public float gdsFeePerSegment = 8f;      // GDS 每航段费（美元）
        public float otaCommissionRate = 0.12f;  // OTA 佣金率 0~1
        public float tmcSegmentFee = 6f;         // TMC 每航段费（美元）
        public float agencyRebateRate = 0.05f;   // 旅行社返点率 0~1
        public bool surchargeEnabled = true;     // GDS 附加费开关
        public float surchargeUsd = 3f;          // GDS 附加费（美元）
    }

    /// <summary>大客户合同（n67）。</summary>
    [Serializable]
    public class KeyAccount
    {
        public string id;
        public string name;
        public int tier;               // 0全球 1全国 2地区
        public float annualSpendWan;   // 年度差旅采购额（万美元）
        public float frontDiscount;    // 前返：购票固定折扣 0~1（0.8=8折）
        public float backRewardRate;   // 后返：期满返还比例 0~1
        public int expiryYear;         // 到期年份
        public bool autoRenew;         // 自动续约
        public bool signed;            // 是否已签约
    }

    /// <summary>舆情条目（n72/n73）。</summary>
    [Serializable]
    public class SentimentEntry
    {
        public string platform;   // 微博/抖音/小红书/知乎/新闻…
        public string text;
        public int sentiment;     // 1正 0中 -1负
        public int heat;          // 热度 0~100
    }

    /// <summary>整改措施（n75 品牌修复）。</summary>
    [Serializable]
    public class RepairMeasure
    {
        public string name;
        public string dept;       // 责任部门
        public bool done;
    }

    /// <summary>品牌危机状态（n72/n74/n75）。</summary>
    [Serializable]
    public class BrandCrisis
    {
        public float trustIndex = 72f;   // 品牌信任指数 0~100
        public int nps = 20;             // 净推荐值 -100~100
        public int crisisLevel;          // 0一般 1重要 2重大
        public bool active;              // 是否处于危机状态
        public int negativeVolume;       // 负面声量
        public int responseStage;        // 0研判 1应对 2处置 3善后
        public string statementTemplate = "道歉"; // 道歉/澄清/行动/善后
        public List<SentimentEntry> feed = new List<SentimentEntry>();
        public List<RepairMeasure> repairMeasures = new List<RepairMeasure>();
    }

    [Serializable]
    public class MktSave
    {
        public MktState state;
    }
}
