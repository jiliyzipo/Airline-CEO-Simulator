using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 非航收入与临空商业生态（GDB 26.2 · P2 长线运营）。
    /// 纯 C#，Unity 与 tools/balance 共用。
    /// 核心设定：
    ///   · 解锁：枢纽年旅客吞吐达 500万 / 1000万 / 3000万人次，逐级解锁商业开发档位。
    ///   · 收入构成：免税店 40%、广告 20%、贵宾厅 20%、货运站租赁 20%（4:2:2:2）。
    ///   · 投资回报率：6%~12%/年，随吞吐档位提升；回收期 8~16 年，属「长期博弈」。
    ///   · 战略价值：非航收入不随航空周期波动，是衰退/萧条期的「安全垫」。
    /// 单位约定：货币「万美元」；吞吐「万人次」；百分比按 0~1。
    /// </summary>
    public static class AirportCommerceEconomics
    {
        // ———— 枢纽吞吐解锁档位（万人次） ————
        public const float UnlockTier1Wan = 500f;   // 解锁基础档（L1）
        public const float UnlockTier2Wan = 1000f;  // 解锁标准档（L2）
        public const float UnlockTier3Wan = 3000f;  // 解锁高端档（L3）

        /// <summary>设施种类：0 免税店 / 1 广告位 / 2 贵宾厅 / 3 货运站租赁。</summary>
        public const int DutyFree = 0;
        public const int Advertising = 1;
        public const int Lounge = 2;
        public const int CargoTerminal = 3;

        public static string FacilityName(int kind) => kind switch
        {
            DutyFree => "免税店",
            Advertising => "广告位",
            Lounge => "贵宾厅",
            CargoTerminal => "货运站租赁",
            _ => "商业设施"
        };

        /// <summary>收入构成权重（GDB 26.2：免税店 40%、其余各 20%）。</summary>
        public static float RevenueWeight(int kind) => kind == DutyFree ? 0.40f : 0.20f;

        /// <summary>
        /// 单设施升级至某档位所需的累计投资（万美元）。
        /// 免税店为其余三类的 2 倍，使四类满配时的收入比例恰为 4:2:2:2。
        /// 档位系数：L1×1、L2×3、L3×6。
        /// </summary>
        public static float InvestmentWan(int kind, int level)
        {
            if (level <= 0) return 0f;
            float baseWan = kind == DutyFree ? 300f : 150f;
            float factor = level switch { 1 => 1f, 2 => 3f, 3 => 6f, _ => 0f };
            return baseWan * factor;
        }

        /// <summary>年化投资回报率（6%~12%），随枢纽吞吐档位提升。</summary>
        public static float AnnualRoi(float throughputWan)
        {
            if (throughputWan >= UnlockTier3Wan) return 0.12f;
            if (throughputWan >= UnlockTier2Wan) return 0.10f;
            if (throughputWan >= UnlockTier1Wan) return 0.08f;
            return 0.06f;
        }

        /// <summary>当前枢纽吞吐对应的解锁档位（0 未解锁 / 1 基础 / 2 标准 / 3 高端）。</summary>
        public static int UnlockedTier(float throughputWan)
        {
            if (throughputWan >= UnlockTier3Wan) return 3;
            if (throughputWan >= UnlockTier2Wan) return 2;
            if (throughputWan >= UnlockTier1Wan) return 1;
            return 0;
        }

        /// <summary>设施在给定档位与吞吐下的年收入（万美元）= 投资 × 年化 ROI。</summary>
        public static float FacilityAnnualRevenueWan(int kind, int level, float throughputWan)
        {
            if (level <= 0) return 0f;
            return InvestmentWan(kind, level) * AnnualRoi(throughputWan);
        }

        /// <summary>设施季度收入（万美元）= 年收入 / 4。</summary>
        public static float FacilityQuarterlyRevenueWan(int kind, int level, float throughputWan)
        {
            return FacilityAnnualRevenueWan(kind, level, throughputWan) / 4f;
        }

        /// <summary>回收期（年）= 投资 / 年收入；收入为 0 时返回 0。</summary>
        public static float PaybackYears(int kind, int level, float throughputWan)
        {
            float invest = InvestmentWan(kind, level);
            float annual = FacilityAnnualRevenueWan(kind, level, throughputWan);
            if (annual <= 0f) return 0f;
            return invest / annual;
        }

        /// <summary>
        /// 枢纽年旅客吞吐估算（万人次）= 周班次 × 52 周 × 平均座位 × 客座率 / 10000。
        /// </summary>
        public static float HubAnnualThroughputWan(int weeklyDepartures, int avgSeats, float loadFactor)
        {
            int dep = Math.Max(0, weeklyDepartures);
            int seats = Math.Max(1, avgSeats);
            float lf = Math.Clamp(loadFactor, 0f, 1f);
            return dep * 52f * seats * lf / 10000f;
        }

        /// <summary>档位中文名。</summary>
        public static string TierName(int tier) => tier switch
        {
            3 => "高端档（L3）",
            2 => "标准档（L2）",
            1 => "基础档（L1）",
            _ => "未解锁"
        };

        /// <summary>下一档位所需吞吐（万人次）；已达最高档返回 0。</summary>
        public static float NextTierThresholdWan(int tier) => tier switch
        {
            0 => UnlockTier1Wan,
            1 => UnlockTier2Wan,
            2 => UnlockTier3Wan,
            _ => 0f
        };
    }
}
