using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 税务与政府补贴数值引擎（GDB 15.4 / 15.10 · P2 长线运营 · 财务细分）。
    /// 纯 C#，Unity 与 tools/balance 共用。
    /// 单位约定：货币「万美元」；税率为小数（0.25 = 25%）。
    /// 简化说明：企业所得税按枢纽国税率对「税前年净利」征税；
    /// 跨国子公司/转移定价仅以风险概率呈现，不做完整税务架构。
    /// </summary>
    public static class TaxEconomics
    {
        /// <summary>中国企业所得税率（默认枢纽国）。</summary>
        public const float ChinaTaxRate = 0.25f;
        /// <summary>欧盟企业所得税率。</summary>
        public const float EeaTaxRate = 0.21f;
        /// <summary>美国企业所得税率。</summary>
        public const float UsTaxRate = 0.21f;
        /// <summary>阿联酋企业所得税率（0%）。</summary>
        public const float UaeTaxRate = 0f;
        /// <summary>新加坡企业所得税率。</summary>
        public const float SingaporeTaxRate = 0.17f;

        /// <summary>转移定价调查基础概率（GDB 15.10）。</summary>
        public const float TransferPricingBaseRisk = 0.05f;

        /// <summary>按枢纽 IATA 取企业所得税率。</summary>
        public static float CorporateTaxRateForHub(string hubIata)
        {
            if (string.IsNullOrEmpty(hubIata)) return ChinaTaxRate;
            if (AirportRegions.IsEea(hubIata)) return EeaTaxRate;
            if (AirportRegions.IsUs(hubIata)) return UsTaxRate;
            if (AirportRegions.IsUae(hubIata)) return UaeTaxRate;
            if (AirportRegions.IsSingapore(hubIata)) return SingaporeTaxRate;
            return ChinaTaxRate; // 中国 / 其余默认
        }

        /// <summary>企业所得税（万美元）= max(0, 税前年净利) × 税率。亏损不征税。</summary>
        public static float CorporateTaxWan(float preTaxProfitWan, float taxRate)
        {
            if (preTaxProfitWan <= 0f) return 0f;
            return preTaxProfitWan * ClampRate(taxRate);
        }

        /// <summary>
        /// 转移定价调查概率 = 基础 5% × 激进系数 × (1 − 合规投入)。
        /// </summary>
        public static float TransferPricingAuditRisk(float aggressiveness, float compliance)
        {
            float agg = Clamp01(aggressiveness);
            float comp = Clamp01(compliance);
            return Clamp01(TransferPricingBaseRisk * agg * (1f - comp));
        }

        /// <summary>
        /// 补贴依赖判定：剔除补贴后该航线是否亏损。
        /// routeProfitWan 为含补贴后的航线净利；subsidyWan 为该航线当年补贴额。
        /// </summary>
        public static bool IsSubsidyDependent(float routeProfitWan, float subsidyWan)
        {
            return (routeProfitWan - subsidyWan) < 0f;
        }

        static float ClampRate(float r) => r < 0f ? 0f : (r > 1f ? 1f : r);
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
