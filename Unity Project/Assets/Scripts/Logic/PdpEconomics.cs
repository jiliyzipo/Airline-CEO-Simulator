using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 机队融资 PDP 预付款机制（GDB 15.6 · P2 长线运营 · 财务细分）。
    /// 纯 C#，Unity 与 tools/balance 共用。货币「万美元」。
    /// 付款节奏：下单定金 15% + 交付前24月 10% + 交付前12月 15% + 交付时尾款 60% = 100%。
    /// 交付时间轴按机型 2~5 年（窄体 2 / 宽体 3 / 货机 4 / 其他 3）。
    /// </summary>
    public static class PdpEconomics
    {
        /// <summary>下单定金比例。</summary>
        public const float SigningDepositRatio = 0.15f;
        /// <summary>交付前 24 月预付款比例。</summary>
        public const float Pdp24Ratio = 0.10f;
        /// <summary>交付前 12 月预付款比例。</summary>
        public const float Pdp12Ratio = 0.15f;
        /// <summary>交付时尾款比例。</summary>
        public const float DeliveryBalanceRatio = 0.60f;

        /// <summary>取消违约金比例下限（GDB 20%~30%）。</summary>
        public const float CancellationPenaltyMin = 0.20f;
        /// <summary>取消违约金比例上限。</summary>
        public const float CancellationPenaltyMax = 0.30f;

        /// <summary>按机型估算交付年数（窄体 2 / 宽体 3 / 货机 4 / 其他 3）。</summary>
        public static int DeliveryYears(string aircraftType)
        {
            string t = (aircraftType ?? "").ToUpperInvariant();
            if (t.Contains("F") || t.Contains("CARGO") || t.Contains("货"))
                return 4;
            if (EconomyBalanceParams.IsWidebody(t))
                return 3;
            return 2;
        }

        /// <summary>交付季度数 = 交付年数 × 4。</summary>
        public static int DeliveryQuarters(string aircraftType) =>
            DeliveryYears(aircraftType) * 4;

        /// <summary>下单定金（万美元）。</summary>
        public static float SigningDepositWan(float totalWan) =>
            Math.Max(0f, totalWan) * SigningDepositRatio;

        /// <summary>交付前 24 月预付款（万美元）。</summary>
        public static float Pdp24Wan(float totalWan) =>
            Math.Max(0f, totalWan) * Pdp24Ratio;

        /// <summary>交付前 12 月预付款（万美元）。</summary>
        public static float Pdp12Wan(float totalWan) =>
            Math.Max(0f, totalWan) * Pdp12Ratio;

        /// <summary>交付时尾款（万美元）。</summary>
        public static float DeliveryBalanceWan(float totalWan) =>
            Math.Max(0f, totalWan) * DeliveryBalanceRatio;

        /// <summary>
        /// 取消违约金比例（20%~30%，随已付比例线性上浮）。
        /// paidRatio = 已付总额 / 订单总额。
        /// </summary>
        public static float CancellationPenaltyRate(float paidRatio)
        {
            float p = paidRatio < 0f ? 0f : (paidRatio > 1f ? 1f : paidRatio);
            return CancellationPenaltyMin + (CancellationPenaltyMax - CancellationPenaltyMin) * p;
        }

        /// <summary>取消违约金（万美元）= 订单总额 × 违约金比例。</summary>
        public static float CancellationPenaltyWan(float totalWan, float paidRatio) =>
            Math.Max(0f, totalWan) * CancellationPenaltyRate(paidRatio);
    }
}
