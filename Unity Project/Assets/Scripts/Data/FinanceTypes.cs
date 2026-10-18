using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Data
{
    /// <summary>燃油对冲合约（GDD 7.4 · Phase 9）。</summary>
    [Serializable]
    public class HedgeContract
    {
        public float lockPrice;       // 美元/吨（fixed / call 行权价）
        public float quantity;        // 总对冲量（吨）
        public float hedgeRatio;      // 0.25 / 0.5
        public int termMonths;        // 3 / 6 / 12
        public int monthsRemaining;
        public string createdAt;
        public float premiumRate;     // fixed 溢价率；call 为权利金率参数
        /// <summary>fixed / collar / call（空则视为 fixed，兼容旧档）</summary>
        public string lockType = "fixed";
        public float floorPrice;      // collar 下限
        public float capPrice;        // collar 上限
        public float premiumPaid;     // call 已付权利金（万美元，开仓时扣）
        public float floorOffset;     // 记录 UI：如 0.05 = -5%
        public float capOffset;       // 记录 UI：如 0.05 = +5%
    }

    public static class HedgeLockTypes
    {
        public const string Fixed = "fixed";
        public const string Collar = "collar";
        public const string Call = "call";

        public static string DisplayName(string t) => t switch
        {
            Collar => "区间震荡保护",
            Call => "期权保险",
            _ => "固定价锁死"
        };
    }

    /// <summary>融资贷款（GDD 7.4 · Phase 9）。</summary>
    [Serializable]
    public class Loan
    {
        public string id;
        public string type;           // bank / bridge / equity
        public float principal;       // 万美元
        public float remainingPrincipal;
        public float interestRate;    // 年利率
        public int termMonths;
        public int monthsRemaining;
        public float monthlyPayment;  // 万美元/月
        public string createdAt;
    }

    [Serializable]
    public class HedgeListSave
    {
        public HedgeContract[] items;
    }

    [Serializable]
    public class LoanListSave
    {
        public Loan[] items;
    }

    /// <summary>简化信用评级档位。</summary>
    public enum CreditRating
    {
        D = 0,
        C = 1,
        B = 2,
        BB = 3,
        BBB = 4,
        A = 5,
        AA = 6,
        AAA = 7
    }

    /// <summary>紧急融资方式（GDD 15.9 · Phase 2 Week 5）。</summary>
    public enum FinancingKind
    {
        Bank = 0,
        Bridge = 1,
        Equity = 2,
        SellAsset = 3,
        GovBailout = 4
    }

    /// <summary>年度预算单项（部门维度）。金额单位为万美元。</summary>
    [Serializable]
    public class BudgetItem
    {
        public string deptId;
        public string deptName;
        public float amountWan;   // 本财年预算上限
        public float spentWan;    // 已执行/已使用
    }

    [Serializable]
    public class BudgetListSave
    {
        public BudgetItem[] items;
    }

    /// <summary>融资方案条款（n103 条款确认页展示与确认）。</summary>
    [Serializable]
    public class FinancingTerm
    {
        public int kind;
        public string label;
        public float amountWan;
        public float annualRate;       // 年利率；equity/sell 为 0
        public int termMonths;
        public float dilutionPercent;  // 股东增资股权稀释比例
        public string repayment;       // 等额本息 / 到期还本 / —
        public string collateral;      // 抵押物清单 / 出售标的
        public string condition;       // 附加条件 / 监管要求
        public string riskNote;        // 醒目风险提示
    }

    /// <summary>碳配额状态（n32）。排放/配额单位为吨 CO2，碳价为美元/吨。</summary>
    [Serializable]
    public class CarbonState
    {
        public float emissionsTons;       // 累计排放（吨）
        public float purchasedQuotaTons;  // 已购配额（吨）
        public float carbonPricePerTon = 85f;
    }

    /// <summary>破产保护 / Chapter 11 重组状态（n25/n106）。</summary>
    [Serializable]
    public class RestructuringState
    {
        public bool filed;                 // 已申请破产保护
        public bool inProgress;            // 重组进行中
        public int protectionMonthsLeft;   // 现金保护期剩余月
        public int confirmedConditions;    // 已确认强制条件数
        public float cashRecoveredWan;     // 资产处置回笼
        public float routesCutPercent;     // 亏损航线削减比例
        public float fleetCutPercent;      // 机队削减比例
        public float brandLoss;            // 品牌净值损失
    }

    [Serializable]
    public class CarbonSave
    {
        public float emissionsTons;
        public float purchasedQuotaTons;
        public float carbonPricePerTon;
    }

    [Serializable]
    public class RestructuringSave
    {
        public bool filed;
        public bool inProgress;
        public int protectionMonthsLeft;
        public int confirmedConditions;
        public float cashRecoveredWan;
        public float routesCutPercent;
        public float fleetCutPercent;
        public float brandLoss;
    }

    // ============================================================
    // P2 长线运营 · 财务细分（GDB 15.4 / 15.6 / 15.11 / 15.12 / 15.13）
    // ============================================================

    /// <summary>航线补贴记录（GDB 15.4：航线补贴入账 + 补贴依赖警示）。</summary>
    [Serializable]
    public class RouteSubsidy
    {
        public string routeId;
        public float annualAmountWan;   // 年度补贴额（万美元）
        public int yearsRemaining;      // 剩余补贴年数
        public string note;
    }

    [Serializable]
    public class SubsidyListSave
    {
        public RouteSubsidy[] items;
    }

    /// <summary>飞机采购订单（GDB 15.6：PDP 订单簿 + 2~5 年交付时间轴）。</summary>
    [Serializable]
    public class AircraftOrder
    {
        public string orderId;
        public string typeId;
        public int count;
        public float unitPriceWan;
        public float totalWan;
        public int orderYear;
        public int orderQuarter;
        public int deliveryYear;
        public int deliveryQuarter;
        public int deliveryQuartersTotal;   // 下单→交付总季度数

        // 付款里程碑（已付，万美元）
        public float depositPaidWan;    // 下单定金 15%
        public float pdp24PaidWan;      // 交付前 24 月 10%
        public float pdp12PaidWan;      // 交付前 12 月 15%
        public float balancePaidWan;    // 交付时尾款 60%

        public bool delivered;
        public bool cancelled;
    }

    [Serializable]
    public class AircraftOrderListSave
    {
        public AircraftOrder[] items;
    }

    /// <summary>航空保险覆盖档位（GDB 15.11）。</summary>
    [Serializable]
    public enum InsuranceCoverage
    {
        Full = 0,
        Basic = 1,
        SelfInsured = 2
    }

    [Serializable]
    public class InsuranceSave
    {
        public int coverage;
        public float cumulativePremiumWan;
        public float cumulativeClaimWan;
    }

    /// <summary>燃油采购状态（GDB 15.12：库存 + 最低安全线）。</summary>
    [Serializable]
    public class FuelProcurementState
    {
        public float inventoryDays = 90f;       // 当前库存可支撑天数（约 3 季缓冲）
        public float safetyDays = 7f;           // 最低安全线
        public float lastAvgPrice;              // 最近一次计算均价
        public float lastInventoryWarningQuarter; // 上次库存预警季度（去重用）
    }

    [Serializable]
    public class FuelProcurementSave
    {
        public FuelProcurementState state;
    }

    /// <summary>碳合规状态（GDB 15.13：EU ETS + CORSIA 拆分）。</summary>
    [Serializable]
    public class CarbonComplianceState
    {
        public float etsEmissionsTons;     // 欧洲内部航线年排放
        public float corsiaEmissionsTons;  // 国际航线年排放
        public float etsRequiredTons;      // EU ETS 需购配额
        public float corsiaRequiredTons;   // CORSIA 需购配额
        public float etsCostWan;           // EU ETS 配额成本
        public float corsiaCostWan;        // CORSIA 配额成本
    }

    [Serializable]
    public class CarbonComplianceSave
    {
        public CarbonComplianceState state;
    }
}
