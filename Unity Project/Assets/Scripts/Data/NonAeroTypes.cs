using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Data
{
    /// <summary>
    /// 非航收入与临空商业生态状态（GDB 26.2 · P2 长线运营）。
    /// 由战略部与市场部共同规划枢纽商业空间，投资四类设施产生非航收入。
    /// </summary>

    /// <summary>单类商业设施的投资状态。</summary>
    [Serializable]
    public class CommerceFacility
    {
        /// <summary>设施种类：0 免税店 / 1 广告位 / 2 贵宾厅 / 3 货运站租赁。</summary>
        public int kind;
        /// <summary>当前档位：0 未建 / 1 基础 / 2 标准 / 3 高端。</summary>
        public int level;
        /// <summary>累计投资（万美元，含历史升级投入）。</summary>
        public float investedWan;
    }

    /// <summary>非航收入与临空商业聚合状态。</summary>
    [Serializable]
    public class AirportCommerceState
    {
        /// <summary>枢纽年旅客吞吐（万人次，每季由 Manager 重算写入）。</summary>
        public float hubAnnualThroughputWan;
        /// <summary>当前解锁档位 0~3（随吞吐刷新）。</summary>
        public int unlockedTier;
        /// <summary>四类商业设施投资状态。</summary>
        public List<CommerceFacility> facilities = new List<CommerceFacility>();
        /// <summary>当前年化非航收入（万美元/年，= 最新季度收入 × 4）。</summary>
        public float annualRevenueWan;
        /// <summary>累计非航收入（万美元，跨财年）。</summary>
        public float cumulativeRevenueWan;
        /// <summary>累计非航投资（万美元）。</summary>
        public float cumulativeInvestmentWan;
    }

    [Serializable]
    public class AirportCommerceSave
    {
        public AirportCommerceState state;
    }
}
