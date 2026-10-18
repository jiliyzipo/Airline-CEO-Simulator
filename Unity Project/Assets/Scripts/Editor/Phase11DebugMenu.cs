#if UNITY_EDITOR
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    public static class Phase11DebugMenu
    {
        [MenuItem("Debug/Force AI Expansion", false, 260)]
        public static void ForceExpand()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("AI 竞对", "请在 Play 模式下使用。", "确定");
                return;
            }

            EnsureManagers();
            AIManager.Instance.DebugForceExpanding();
            CompetitionManager.Instance.RecalculateMarketShares(GameState.Instance);
            EditorUtility.DisplayDialog("AI 竞对", "已强制一家掠食者进入「进攻」并抬高现金。\n推进一季可看到其对玩家航线扩运力。", "确定");
        }

        [MenuItem("Debug/Force AI Dying", false, 261)]
        public static void ForceDying()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("AI 竞对", "请在 Play 模式下使用。", "确定");
                return;
            }

            EnsureManagers();
            AIManager.Instance.DebugForceDying();
            EditorUtility.DisplayDialog("AI 竞对", "已强制一家 AI 进入「垂死」。\n再推进一季将破产清算。", "确定");
        }

        [MenuItem("Debug/Kill AI Competitor", false, 262)]
        public static void KillAi()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("AI 竞对", "请在 Play 模式下使用。", "确定");
                return;
            }

            EnsureManagers();
            AIManager.Instance.DebugKill();
            CompetitionManager.Instance.RecalculateMarketShares(GameState.Instance);
            EditorUtility.DisplayDialog("AI 竞对", "已立即清算一家存活 AI。\n约 2 年后会有新竞对入场。", "确定");
        }

        [MenuItem("Debug/Propose Code Share Now", false, 263)]
        public static void ProposeCodeShare()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("AI 竞对", "请在 Play 模式下使用。", "确定");
                return;
            }

            EnsureManagers();
            var gs = GameState.Instance;
            AIManager.Instance.EnsureAiGenerated();
            AIAirline partner = null;
            if (gs.aiAirlines != null)
            {
                for (int i = 0; i < gs.aiAirlines.Count; i++)
                {
                    if (gs.aiAirlines[i] != null && gs.aiAirlines[i].isAlive)
                    {
                        partner = gs.aiAirlines[i];
                        break;
                    }
                }
            }
            if (partner == null)
            {
                EditorUtility.DisplayDialog("AI 竞对", "没有存活 AI。", "确定");
                return;
            }

            gs.codeShareActive = false;
            gs.pendingCodeShareFromId = partner.id;
            Debug.Log($"[Phase11] 代码共享提案：{partner.name}");
            EditorUtility.DisplayDialog("AI 竞对",
                $"已生成来自「{partner.name}」的代码共享提案。\n打开「市场 → 竞争态势」可接受/拒绝。", "确定");
        }

        private static void EnsureManagers()
        {
            if (AIManager.Instance == null)
            {
                var go = new GameObject("AIManager");
                go.AddComponent<AIManager>();
            }
            if (CompetitionManager.Instance == null)
            {
                var go = new GameObject("CompetitionManager");
                go.AddComponent<CompetitionManager>();
            }
            AIManager.Instance.EnsureAiGenerated();
        }
    }
}
#endif
