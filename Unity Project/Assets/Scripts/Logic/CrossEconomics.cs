using System;

namespace AirlineCEO.Logic
{
    /// <summary>六部门 KPI 健康度（0～100，供跨部门协同指数计算）。</summary>
    public struct CrossDeptHealth
    {
        public float opsOtp;            // 准点率 0~100
        public float finRunwayScore;    // 现金跑道健康 0~100
        public float mktBrand;          // 品牌信任 0~100
        public float hrSatisfaction;    // 员工满意度 0~100
        public float safRatingScore;    // 安全评级分 0~100
        public float mntAvailability;   // 机队可用率 0~100
    }

    /// <summary>
    /// 跨部门协同指数（纯 C#，供 tools/balance 离线闭环）。
    /// 统一模型：把六部门 KPI 汇总为 0～100 的协同指数——加权平均 + 短板惩罚，
    /// 鼓励「六边形战士」式均衡发展，而非单点突出但存在明显短板。
    /// </summary>
    public static class CrossEconomics
    {
        // 权重：OPS/FIN 各 20%，MKT/HR/SAF/MNT 各 15%
        public static float CrossConsistency(CrossDeptHealth h)
        {
            float wAvg = h.opsOtp * 0.20f
                       + h.finRunwayScore * 0.20f
                       + h.mktBrand * 0.15f
                       + h.hrSatisfaction * 0.15f
                       + h.safRatingScore * 0.15f
                       + h.mntAvailability * 0.15f;

            float worst = Min(h.opsOtp, Min(h.finRunwayScore,
                Min(h.mktBrand, Min(h.hrSatisfaction, Min(h.safRatingScore, h.mntAvailability)))));

            return wAvg * 0.6f + worst * 0.4f;
        }

        /// <summary>现金跑道月数 → 0～100 健康分（12 个月以上视为满分）。</summary>
        public static float RunwayScore(float runwayMonths)
        {
            return Clamp01(runwayMonths / 12f) * 100f;
        }

        /// <summary>协同指数 → 档位标签。</summary>
        public static string ConsistencyBand(float score)
        {
            if (score >= 85f) return "高度协同";
            if (score >= 70f) return "协同";
            if (score >= 55f) return "一般";
            return "失衡";
        }

        static float Min(float a, float b) => a < b ? a : b;
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
