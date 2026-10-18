#if UNITY_EDITOR
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// P3 体验收口验证：渐进式解锁「功能闸门」+ 失误保护「时间回溯」。
    /// 菜单：AirlineCEO → 渐进解锁 / 失误保护（自测）
    /// </summary>
    public static class ProgressionGateDebugMenu
    {
        [MenuItem("AirlineCEO/P3 功能闸门 · 显示当前状态", false, 30)]
        public static void ShowGateStatus()
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                EditorUtility.DisplayDialog("功能闸门", "请在 Play 模式下使用（GameState 尚未就绪）。", "确定");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"当前：第 {gs.year} 财年 · {ProgressionGate.StageName(gs.year)}阶段");
            var usedThisYear = gs.mistakeProtectionUsedYear >= gs.year;
            sb.AppendLine($"失误保护剩余：{gs.mistakeProtectionRemaining} / {ProgressionGate.MistakeProtectionTotal}（本财年{(usedThisYear ? "已使用过" : "未使用")}）");
            sb.AppendLine();
            string[] depts = { "n13_运营部", "n14_市场销售部", "n17_机务工程部", "n15_财务部", "n18_人力资源部", "n16_战略发展部", "n19_安全质量部" };
            foreach (var d in depts)
            {
                int unlock = ProgressionGate.UnlockYearForDept(d);
                bool unlocked = ProgressionGate.IsDeptUnlocked(d, gs.year);
                sb.AppendLine($"  {(unlocked ? "✅" : "🔒")} {d} → 第 {unlock} 财年解锁");
            }

            Debug.Log("[AirlineCEO]\n" + sb);
            EditorUtility.DisplayDialog("功能闸门状态", sb.ToString(), "确定");
        }

        [MenuItem("AirlineCEO/P3 失误保护 · 模拟重大失误触发时间回溯", false, 31)]
        public static void ForceMistakeRollback()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("失误保护", "请在 Play 模式下使用（需要 GameState 与 SaveManager）。", "确定");
                return;
            }

            var gs = GameState.Instance;
            if (gs == null)
            {
                EditorUtility.DisplayDialog("失误保护", "GameState 尚未就绪。", "确定");
                return;
            }

            // 1. 记录「季度初」快照（回滚目标点）
            MistakeProtectionManager.CaptureQuarterStart(gs);
            float cashBefore = gs.cash;
            float satBefore = gs.boardSatisfaction;
            int remainingBefore = gs.mistakeProtectionRemaining;

            // 2. 模拟季度末重大失误：满意度跌破 30 下限（必然命中 Safety/Floor/NetLoss 之一）
            gs.boardSatisfaction = 20f;

            // 3. 执行季度末检查 → 触发时间回溯
            string rollback = MistakeProtectionManager.CheckAndMaybeRollback(gs, safetyEventOccurred: false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"触发前：现金 {cashBefore:F0} 万 · 满意度 {satBefore:F0} · 保护剩余 {remainingBefore}");
            sb.AppendLine($"模拟后满意度：20（跌破下限 30，命中重大失误）");
            sb.AppendLine();
            sb.AppendLine(rollback ?? "(未触发回滚：可能铁人模式 / 保护次数已用尽 / 非见习期)");
            sb.AppendLine();
            sb.AppendLine($"回滚后：满意度 {gs.boardSatisfaction:F0} · 保护剩余 {gs.mistakeProtectionRemaining}");

            Debug.Log("[AirlineCEO]\n" + sb);
            EditorUtility.DisplayDialog("失误保护 · 时间回溯", sb.ToString(), "确定");
        }
    }
}
#endif
