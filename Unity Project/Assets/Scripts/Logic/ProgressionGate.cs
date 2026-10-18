using System;

namespace AirlineCEO.Logic
{
    /// <summary>重大失误类型（触发「时间回溯 / 失误保护」的判定依据，GDB 28.1）。</summary>
    public enum MistakeKind
    {
        None = 0,
        /// <summary>单季度净亏损超过当前现金储备的 15%。</summary>
        NetLoss = 1,
        /// <summary>董事会满意度在一个季度内下降超过 20 点。</summary>
        SatisfactionDrop = 2,
        /// <summary>董事会满意度降至 30 点以下。</summary>
        SatisfactionFloor = 3,
        /// <summary>发生任何等级的安全事故。</summary>
        SafetyEvent = 4
    }

    /// <summary>
    /// 渐进式解锁与失误保护（GDB 第28章 · P3 体验收口）。
    /// 纯 C#，Unity 与 tools/balance 共用。
    ///   · 功能闸门：七大部门按财年渐进解锁（第1财年 OPS/MKT/MNT，第2财年 FIN/HR，第3财年 STG/SAF，第4财年起全部）。
    ///   · 失误保护：见习期（第1~3财年）共 3 次保护，每财年最多 1 次；触发重大失误时自动「时间回溯」到季度初。
    /// </summary>
    public static class ProgressionGate
    {
        public const int ProbationMaxYear = 3;          // 见习期：第 1~3 财年
        public const int MistakeProtectionTotal = 3;    // 失误保护总次数（共 3 次）
        public const int MistakeProtectionPerYear = 1;  // 每财年最多触发 1 次

        /// <summary>
        /// 精简开发旁路：为 true 时所有部门功能闸门一律视为已解锁（底栏可点、导航不拦截）。
        /// 用字段而非 const，避免编译器把后续逻辑标为 CS0162 不可达代码。恢复渐进解锁时改回 false。
        /// </summary>
        public static bool BypassDeptGate = true;

        // —— 重大失误判定阈值（GDB 28.1）——
        public const float NetLossRatio = 0.15f;              // 净亏损 > 现金储备 15%
        public const float SatisfactionDropThreshold = 20f;   // 满意度季度降幅 > 20 点
        public const float SatisfactionFloor = 30f;           // 满意度下限 30 点

        /// <summary>
        /// 部门功能闸门：返回该部门解锁所需的最早财年；0 表示无闸门（始终可用）。
        /// 使用精确前缀（n13_ 而非 n13）避免误吞 n13x / n15x 等相邻编号。
        /// </summary>
        public static int UnlockYearForDept(string pageId)
        {
            if (BypassDeptGate) return 0;
            if (string.IsNullOrEmpty(pageId)) return 0;

            // 第 1 财年：运营 / 市场 / 机务·维护
            if (pageId.StartsWith("n13_", StringComparison.Ordinal) || pageId.Contains("运营")) return 1;
            if (pageId.StartsWith("n14_", StringComparison.Ordinal) || pageId.Contains("市场")) return 1;
            if (pageId.StartsWith("n17_", StringComparison.Ordinal) || pageId.Contains("机务") || pageId.Contains("维护")) return 1;

            // 第 2 财年：财务 / 人员 / 培训
            if (pageId.StartsWith("n15_", StringComparison.Ordinal) || pageId.Contains("财务")) return 2;
            if (pageId.StartsWith("n18_", StringComparison.Ordinal) || pageId.Contains("人力") || pageId.Contains("人员")) return 2;
            if (pageId.StartsWith("n87_", StringComparison.Ordinal) || pageId.Contains("培训")) return 2;

            // 第 3 财年：战略·愿景 / 安全
            if (pageId.StartsWith("n16_", StringComparison.Ordinal) || pageId.Contains("战略") || pageId.Contains("愿景")) return 3;
            if (pageId.StartsWith("n19_", StringComparison.Ordinal) || pageId.Contains("安全")) return 3;

            return 0; // 无闸门
        }

        /// <summary>部门是否已解锁（fiscalYear 达到解锁财年即解锁）。</summary>
        public static bool IsDeptUnlocked(string pageId, int fiscalYear)
        {
            if (BypassDeptGate) return true;
            int unlockYear = UnlockYearForDept(pageId);
            if (unlockYear <= 0) return true;
            return fiscalYear >= unlockYear;
        }

        /// <summary>阶段名（见习生/初级经理/部门总监/CEO）。</summary>
        public static string StageName(int fiscalYear) => fiscalYear switch
        {
            1 => "见习生",
            2 => "初级经理",
            3 => "部门总监",
            _ => "CEO"
        };

        /// <summary>锁提示文案。</summary>
        public static string LockHint(int unlockYear) => unlockYear switch
        {
            1 => "第 1 财年解锁",
            2 => "第 2 财年解锁",
            3 => "第 3 财年解锁",
            _ => ""
        };

        /// <summary>
        /// 重大失误判定（纯函数）。判定顺序：安全事故 > 净亏损 > 满意度降幅 > 满意度下限。
        /// </summary>
        public static MistakeKind CheckMistake(
            float quarterNet,
            float quarterStartCash,
            float satisfactionDrop,
            float satisfactionAfter,
            bool safetyEventOccurred)
        {
            if (safetyEventOccurred) return MistakeKind.SafetyEvent;
            if (quarterNet < 0f && -quarterNet > Math.Max(1f, quarterStartCash) * NetLossRatio)
                return MistakeKind.NetLoss;
            if (satisfactionDrop > SatisfactionDropThreshold) return MistakeKind.SatisfactionDrop;
            if (satisfactionAfter < SatisfactionFloor) return MistakeKind.SatisfactionFloor;
            return MistakeKind.None;
        }

        /// <summary>保护是否可用：见习期内 + 剩余次数 > 0。</summary>
        public static bool CanProtect(int fiscalYear, int remaining)
        {
            return fiscalYear <= ProbationMaxYear && remaining > 0;
        }

        /// <summary>失误类型中文名（UI 提示用）。</summary>
        public static string MistakeLabel(MistakeKind kind) => kind switch
        {
            MistakeKind.NetLoss => "单季度净亏损超过现金储备 15%",
            MistakeKind.SatisfactionDrop => "董事会满意度季度降幅超过 20 点",
            MistakeKind.SatisfactionFloor => "董事会满意度降至 30 点以下",
            MistakeKind.SafetyEvent => "发生安全事故",
            _ => ""
        };

        // ============================================================
        // 见习期软保护（GDB 28.1「特殊保护机制」，仅第 2/3 财年有效）
        // 第 4 财年起全部失效 → 进入硬核模式。
        // ============================================================

        /// <summary>超支问责的满意度惩罚（第 2 财年减半：−5，其余 −10）。</summary>
        public static float OverrunPenalty(int fiscalYear) => fiscalYear == 2 ? -5f : -10f;

        /// <summary>罢工倒计时缩短倍率（第 2 财年减半 → 倒计时延长 50% 缓冲）。</summary>
        public static float StrikeCountdownShrinkScale(int fiscalYear) => fiscalYear == 2 ? 0.5f : 1f;

        /// <summary>
        /// 燃油对冲单季亏损上限（占单季对冲名义量的比例）。
        /// 第 2 财年锁定 20%；其余财年 0 = 不设上限（硬核）。
        /// </summary>
        public static float HedgeLossCapRatio(int fiscalYear) => fiscalYear == 2 ? 0.20f : 0f;

        /// <summary>战略亏损豁免额度（每财年上限）。第 3 财年自动获批 1 项 → 上限 3。</summary>
        public static int WaiverLimit(int fiscalYear) => fiscalYear >= 3 ? 3 : 2;

        /// <summary>安全评级低于阈值时是否豁免局方停飞（第 3 财年 true）。</summary>
        public static bool SkipRegulatoryTakeover(int fiscalYear) => fiscalYear == 3;
    }
}
