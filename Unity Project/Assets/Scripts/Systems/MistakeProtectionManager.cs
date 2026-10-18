using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 失误保护 · 时间回溯（GDB 第28章 · P3 体验收口）。
    /// 见习期（第 1~3 财年）共 3 次保护，每财年最多 1 次；当季度结算后出现「重大失误」
    /// （净亏损超现金 15% / 满意度季度降幅 &gt; 20 点或 &lt; 30 / 安全事故）时，自动把
    /// 主存档回滚到本季度初快照，让玩家重新决策。
    /// 铁人模式（实时覆盖、无回退点）不启用本保护。
    /// </summary>
    public static class MistakeProtectionManager
    {
        /// <summary>季度初快照：记录判定用字段 + 克隆主存档到季度初槽（供回滚）。</summary>
        public static void CaptureQuarterStart(GameState gs)
        {
            if (gs == null) return;

            gs.quarterStartCash = gs.cash;
            gs.quarterStartSatisfaction = gs.boardSatisfaction;

            // 铁人模式禁用手动存档、实时覆盖，无回退点 → 不建快照
            if (gs.ironmanModeActive) return;

            // 把「季度初」的完整状态写进主存档并克隆到专用槽
            var sm = SaveManager.Instance;
            if (sm != null) sm.Save();
            SaveSlotManager.CaptureToSlot(SaveSlotManager.QuarterStartSlot);
        }

        /// <summary>
        /// 季度末检查。返回 null 表示无回滚；否则返回回滚提示文案（调用方应跳过后续自动存档）。
        /// </summary>
        public static string CheckAndMaybeRollback(GameState gs, bool safetyEventOccurred)
        {
            if (gs == null) return null;
            if (gs.ironmanModeActive) return null; // 铁人无保护
            if (gs.gameOver) return null;

            float quarterNet = gs.cash - gs.quarterStartCash;
            float drop = gs.quarterStartSatisfaction - gs.boardSatisfaction;
            var kind = ProgressionGate.CheckMistake(quarterNet, gs.quarterStartCash, drop, gs.boardSatisfaction, safetyEventOccurred);
            if (kind == MistakeKind.None) return null;

            // 保护可用性：见习期 + 剩余 > 0 + 本财年未用过
            if (!ProgressionGate.CanProtect(gs.year, gs.mistakeProtectionRemaining)) return null;
            if (gs.mistakeProtectionUsedYear >= gs.year) return null;

            int year = gs.year;
            int remainingAfter = Mathf.Max(0, gs.mistakeProtectionRemaining - 1);
            bool rolledBack = false;

            if (SaveSlotManager.SlotExists(SaveSlotManager.QuarterStartSlot))
            {
                SaveSlotManager.RestoreFromSlot(SaveSlotManager.QuarterStartSlot);
                var sm = SaveManager.Instance;
                if (sm != null) sm.Load();
                rolledBack = true;
            }

            // 回滚后重新写入扣减后的保护次数（Load 可能把字段覆盖回快照值）
            var gsNow = GameState.Instance;
            if (gsNow != null)
            {
                gsNow.mistakeProtectionRemaining = remainingAfter;
                gsNow.mistakeProtectionUsedYear = year;
            }

            var sm2 = SaveManager.Instance;
            if (sm2 != null) sm2.Save();

            string label = ProgressionGate.MistakeLabel(kind);
            string stage = ProgressionGate.StageName(year);
            if (rolledBack)
            {
                return $"⚠️ 检测到重大失误：{label}。\n" +
                       $"已自动触发「时间回溯」保护，回到本季度初重新决策。\n" +
                       $"剩余保护次数：{remainingAfter} / {ProgressionGate.MistakeProtectionTotal}（{stage}阶段）。";
            }

            return $"⚠️ 检测到重大失误：{label}。\n" +
                   $"本次未找到季度初快照，无法回滚，但保护次数已扣除（剩余 {remainingAfter}）。";
        }
    }
}
