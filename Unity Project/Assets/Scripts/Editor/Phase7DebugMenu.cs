#if UNITY_EDITOR
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    public static class Phase7DebugMenu
    {
        [MenuItem("AirlineCEO/Debug/Force Mild Safety Event", false, 200)]
        public static void ForceMild()
        {
            EnsureMgr();
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("提示", "请先 Play，再强制触发安全事件。", "确定");
                return;
            }

            string msg = SafetyEventManager.Instance.ForceEvent(SafetyEventSeverity.Mild);
            Debug.Log("[AirlineCEO] " + msg);
            EditorUtility.DisplayDialog("安全事件（轻度）", msg, "确定");
        }

        [MenuItem("AirlineCEO/Debug/Force Moderate Safety Event", false, 201)]
        public static void ForceModerate()
        {
            EnsureMgr();
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("提示", "请先 Play，再强制触发安全事件。", "确定");
                return;
            }

            string msg = SafetyEventManager.Instance.ForceEvent(SafetyEventSeverity.Moderate);
            Debug.Log("[AirlineCEO] " + msg);
            EditorUtility.DisplayDialog("安全事件（中度）", msg, "确定");
        }

        [MenuItem("AirlineCEO/Debug/Force AOG Crisis", false, 205)]
        public static void ForceAog()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("提示", "请先 Play，再强制触发 AOG。", "确定");
                return;
            }

            if (AOGManager.Instance == null)
            {
                var go = new GameObject("AOGManager");
                go.AddComponent<AOGManager>();
            }

            AOGManager.Instance.DebugForceShow();
            EditorUtility.DisplayDialog("AOG", "已强制弹出 AOG 危机（需场景中有 AOGUI）。", "确定");
        }

        [MenuItem("AirlineCEO/Debug/Force Expansion Proposal", false, 206)]
        public static void ForceExpansion()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("提示", "请先 Play，再强制触发扩张提案。", "确定");
                return;
            }

            if (ExpansionProposalManager.Instance == null)
            {
                var go = new GameObject("ExpansionProposalManager");
                go.AddComponent<ExpansionProposalManager>();
            }

            // 确保现金足够试「接受」
            if (GameState.Instance.cash < 500f)
                GameState.Instance.cash = 800f;

            ExpansionProposalManager.Instance.DebugForceShow();
            EditorUtility.DisplayDialog("扩张提案", "已强制弹出战略性扩张提案（并确保现金≥500 便于测试接受）。", "确定");
        }

        [MenuItem("AirlineCEO/Debug/移除北京-伦敦航线（复测航权）", false, 210)]
        public static void RemovePekLhr()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("提示", "请先 Play，再移除航线。", "确定");
                return;
            }

            var gs = GameState.Instance;
            int removed = 0;
            for (int i = gs.activeRoutes.Count - 1; i >= 0; i--)
            {
                var r = gs.activeRoutes[i];
                string o = r.origin ?? "";
                string d = r.destination ?? "";
                bool match =
                    (o.Contains("北京") && d.Contains("伦敦")) ||
                    (o.Contains("伦敦") && d.Contains("北京")) ||
                    (o.ToLowerInvariant().Contains("beijing") && d.ToLowerInvariant().Contains("london"));
                if (match)
                {
                    gs.activeRoutes.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                gs.AddDecisionLog($"调试：移除北京-伦敦航线 {removed} 条，可重新申请航权。");
                gs.NotifyChanged();
                EditorUtility.DisplayDialog(
                    "已移除",
                    "已移除北京-伦敦。请再让顾问分析航线，然后反复点「执行建议」测试航权通过/被拒。",
                    "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("提示", "当前没有北京-伦敦航线。", "确定");
            }
        }

        private static void EnsureMgr()
        {
            if (SafetyEventManager.Instance != null) return;
            if (!Application.isPlaying) return;
            var go = new GameObject("SafetyEventManager");
            go.AddComponent<SafetyEventManager>();
        }
    }
}
#endif
