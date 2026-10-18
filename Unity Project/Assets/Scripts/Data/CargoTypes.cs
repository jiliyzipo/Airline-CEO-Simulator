using System;

#nullable disable

namespace AirlineCEO.Data
{
    /// <summary>
    /// 货运业务独立核算状态（GDB 14.6 · P2 长线运营）。
    /// 腹舱货运为边际收益；纯货机为独立成本中心；三项 KPI 汇总于此。
    /// </summary>
    [Serializable]
    public class CargoState
    {
        /// <summary>是否开展腹舱货运（随客机腹舱捎货）。</summary>
        public bool bellyEnabled = true;

        /// <summary>腹舱利用率 0~1（KPI）。</summary>
        public float bellyUtilization = 0.55f;

        /// <summary>货机日利用率（小时/天，KPI，目标 ≥12）。</summary>
        public float freighterDailyUtilization = 12f;

        // —— 回显 KPI（由 CargoManager 每季重算写入） ——
        public float bellyRevenueWanMonthly;      // 腹舱月收入（万美元）
        public float bellyCostWanMonthly;         // 腹舱月装卸成本（万美元）
        public float freighterRevenueWanMonthly;  // 货机月收入（万美元）
        public float freighterCostWanMonthly;     // 货机月成本（折旧+操作+保险，万美元）
        public float cargoTonKmMonthly;           // 月货运吨公里（腹舱+货机）
        public float tkmRevenueWanMonthly;        // 货运吨公里收入（万美元）

        /// <summary>货机数量（本季）。</summary>
        public int freighterCount;
    }

    [Serializable]
    public class CargoSave
    {
        public CargoState state;
    }
}
