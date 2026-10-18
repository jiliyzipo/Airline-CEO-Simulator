using System;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航空保险数值引擎（GDB 15.11 · P2 长线运营 · 财务细分）。
    /// 纯 C#，Unity 与 tools/balance 共用。货币「万美元」。
    /// 保费 = 机队总价值 × 费率（安全 A 1.0% / B 1.5% / C 2.0%）；覆盖档位调节。
    /// </summary>
    public static class InsuranceEconomics
    {
        /// <summary>按安全评级的基础年费率（A 1.0% / B 1.5% / C/D 2.0%）。</summary>
        public static float BaseAnnualRate(SafetyRating rating) => rating switch
        {
            SafetyRating.A => 0.010f,
            SafetyRating.B => 0.015f,
            _ => 0.020f
        };

        /// <summary>覆盖档位系数：全险 1.0 / 基本险 0.6 / 自保 0。</summary>
        public static float CoverageMultiplier(InsuranceCoverage coverage) => coverage switch
        {
            InsuranceCoverage.Full => 1.0f,
            InsuranceCoverage.Basic => 0.6f,
            _ => 0f
        };

        /// <summary>年费率 = 基础费率 × 覆盖系数。</summary>
        public static float AnnualRate(SafetyRating rating, InsuranceCoverage coverage) =>
            BaseAnnualRate(rating) * CoverageMultiplier(coverage);

        /// <summary>年保费（万美元）= 机队总价值 × 年费率。</summary>
        public static float AnnualPremiumWan(float fleetValueWan, SafetyRating rating, InsuranceCoverage coverage) =>
            Math.Max(0f, fleetValueWan) * AnnualRate(rating, coverage);

        /// <summary>季度保费（万美元）= 年保费 / 4。</summary>
        public static float QuarterlyPremiumWan(float fleetValueWan, SafetyRating rating, InsuranceCoverage coverage) =>
            AnnualPremiumWan(fleetValueWan, rating, coverage) / 4f;

        /// <summary>事故严重度（轻微 / 重大，GDB 15.11）。</summary>
        public enum LossSeverity
        {
            Minor = 0,   // 轻微：1 月赔 50%
            Major = 1    // 重大：6 月赔 80%
        }

        /// <summary>理赔比例（轻微 50% / 重大 80%）。</summary>
        public static float ClaimRatio(LossSeverity severity) => severity switch
        {
            LossSeverity.Major => 0.80f,
            _ => 0.50f
        };

        /// <summary>覆盖档位理赔系数：全险 1.0 / 基本险 0.6 / 自保 0。</summary>
        public static float ClaimCoverageMultiplier(InsuranceCoverage coverage) => coverage switch
        {
            InsuranceCoverage.Full => 1.0f,
            InsuranceCoverage.Basic => 0.6f,
            _ => 0f
        };

        /// <summary>理赔金额（万美元）= 损失 × 理赔比例 × 覆盖系数。</summary>
        public static float ClaimPayoutWan(float lossWan, LossSeverity severity, InsuranceCoverage coverage) =>
            Math.Max(0f, lossWan) * ClaimRatio(severity) * ClaimCoverageMultiplier(coverage);
    }
}
