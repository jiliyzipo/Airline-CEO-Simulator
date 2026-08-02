using System;

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
}
