using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// Phase 2 Week 8 · MKT 批次 B 数值引擎（纯 C#，Unity 与 tools/balance 共用）。
    /// 覆盖：竞争 7 维对标评分 / 综合竞争力 / 渠道 ROI 与 CAC / 提直降代节省 / 品牌信任与危机分级。
    /// 单位约定：货币「万美元」；费用「美元」；百分比按 0~1。
    /// </summary>
    public static class MktCompetitionEconomics
    {
        // —— 7 维对标 ——
        public const int DimCount = 7;
        public static readonly string[] DimNames =
            { "航线网络", "定价收益", "产品服务", "运营成本", "辅助收入", "外部环境", "战略资本" };
        public static readonly float[] DimWeights =
            { 0.18f, 0.18f, 0.14f, 0.14f, 0.10f, 0.10f, 0.16f };

        /// <summary>竞对/航司指标快照（与具体数据模型解耦，保持纯 C#）。</summary>
        public struct BenchInput
        {
            public int routes;          // 航线数
            public float marketShare;   // 市场份额 %
            public float avgFare;       // 平均票价（美元）
            public float reputation;    // 声誉 0~100
            public float debtRatio;     // 资产负债率 0~1
            public float cash;          // 现金（万美元）
            public int fleetSize;       // 机队规模
            public float ancillaryScore;// 辅助收入强度 0~100（玩家由设置推导）
            public float macroScore;    // 外部环境适配 0~100
        }

        /// <summary>7 维评分（0~100）。</summary>
        public static float[] DimensionScores(BenchInput c)
        {
            var d = new float[DimCount];
            d[0] = ScoreClamp(NetworkScore(c.routes));
            d[1] = ScoreClamp(PricingScore(c.avgFare, c.marketShare));
            d[2] = ScoreClamp(c.reputation);
            d[3] = ScoreClamp(CostScore(c.debtRatio, c.fleetSize));
            d[4] = ScoreClamp(c.ancillaryScore);
            d[5] = ScoreClamp(c.macroScore);
            d[6] = ScoreClamp(CapitalScore(c.cash, c.debtRatio));
            return d;
        }

        static float NetworkScore(int routes) => Math.Min(100f, routes * 9f);           // ~11 条满
        static float PricingScore(float avgFare, float share) => Math.Min(100f, 40f + share * 1.5f + Math.Max(0, avgFare - 600f) * 0.05f);
        static float CostScore(float debtRatio, int fleet) => 100f - debtRatio * 60f + Math.Min(10f, fleet * 0.8f);
        static float CapitalScore(float cash, float debtRatio) => Math.Min(100f, cash * 0.05f + (1f - debtRatio) * 40f);
        static float ScoreClamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);

        /// <summary>综合竞争力（加权 0~100）。</summary>
        public static float OverallScore(float[] dims)
        {
            if (dims == null || dims.Length == 0) return 0f;
            float s = 0f;
            int n = Math.Min(dims.Length, DimCount);
            for (int i = 0; i < n; i++) s += dims[i] * DimWeights[i];
            return s;
        }

        /// <summary>一句话态势判断：比较玩家与最强竞对。</summary>
        public static string SituationalVerdict(float playerScore, float bestRivalScore)
        {
            float gap = playerScore - bestRivalScore;
            if (gap >= 5f) return "领先：综合竞争力高于最强竞对，可巩固优势并主动出击。";
            if (gap <= -5f) return "落后：综合竞争力弱于最强竞对，建议聚焦短板维度补强。";
            return "胶着：与最强竞对势均力敌，差异化是关键。";
        }

        // —— 渠道 ——
        /// <summary>渠道 ROI = (收入 − 成本) / 成本。</summary>
        public static float ChannelRoi(float revenueWan, float costWan) =>
            costWan <= 0f ? 0f : (revenueWan - costWan) / costWan;

        /// <summary>客均获客成本 CAC（万美元/千名新客）。</summary>
        public static float Cac(float spendWan, int newCustomers) =>
            newCustomers <= 0 ? 0f : spendWan * 1000f / newCustomers;

        /// <summary>单票平均分销成本（美元）：GDS 费 + OTA 佣金（按均价）+ TMC 费加权。</summary>
        public static float AvgDistributionCostUsd(float gdsFee, float otaRate, float avgFare, float tmcFee, float agencyRebate, float avgFare2)
        {
            float ota = otaRate * avgFare;
            return (gdsFee + ota + tmcFee * 0.4f + agencyRebate * avgFare2) / 2.4f;
        }

        /// <summary>提直降代节省（万美元）：直销占比每提升 1% 所省分销佣金。</summary>
        public static float DirectShiftSavingsWan(float totalRevenueWan, float otaRate, float directDelta)
        {
            return totalRevenueWan * otaRate * Math.Max(0f, directDelta);
        }

        /// <summary>渠道集中度（赫芬达尔，0~1）：越高越依赖单一渠道。</summary>
        public static float ChannelConcentration(float[] shares)
        {
            if (shares == null || shares.Length == 0) return 0f;
            float h = 0f;
            for (int i = 0; i < shares.Length; i++) h += shares[i] * shares[i];
            return h;
        }

        // —— 品牌危机 ——
        /// <summary>危机分级：0一般 1重要 2重大。</summary>
        public static int CrisisLevel(float negativeRatio, int negativeVolume)
        {
            if (negativeRatio >= 0.5f || negativeVolume >= 500) return 2;
            if (negativeRatio >= 0.3f || negativeVolume >= 200) return 1;
            return 0;
        }

        /// <summary>品牌信任指数（0~100）：由 NPS 与负面声量合成。</summary>
        public static float TrustIndex(float nps, float negativeRatio)
        {
            float baseScore = 55f + nps * 0.4f;
            return Clamp(baseScore - negativeRatio * 40f, 5f, 100f);
        }

        /// <summary>危机恢复后信任恢复预估（0~100）。</summary>
        public static float RecoveredTrust(float trustIndex, float repairCompletion) =>
            Clamp(trustIndex + repairCompletion * 25f, 5f, 100f);

        /// <summary>情感打分（-100~+100）：由正/负占比合成。</summary>
        public static float SentimentScore(float positiveRatio, float negativeRatio) =>
            Clamp((positiveRatio - negativeRatio) * 100f, -100f, 100f);

        /// <summary>大客户年度价值（万美元）：采购额 × (1 − 前返折扣) × 战略系数。</summary>
        public static float KeyAccountValueWan(float annualSpendWan, float frontDiscount, int tier)
        {
            float strategic = tier switch { 0 => 1.3f, 1 => 1.15f, _ => 1.05f };
            return annualSpendWan * (1f - frontDiscount) * strategic;
        }

        static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
