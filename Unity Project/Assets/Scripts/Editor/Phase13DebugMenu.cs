using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.Editor
{
    /// <summary>
    /// Phase 13 · 宏观经济 Debug 菜单：强制切换经济阶段。
    /// </summary>
    public static class Phase13DebugMenu
    {
        [MenuItem("Debug/Force Boom (繁荣期)", false, 3101)]
        private static void ForceBoom()
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                Debug.LogWarning("[Phase13 Debug] 无 GameState，请先 Play。");
                return;
            }
            gs.gdpGrowthRate = 5f;
            gs.economicPhase = "boom";
            gs.phaseDuration = 0;
            gs.NotifyChanged();
            Debug.Log("[Phase13 Debug] 经济阶段强制设为：繁荣期 (GDP +5%)");
        }

        [MenuItem("Debug/Force Recession (衰退期)", false, 3102)]
        private static void ForceRecession()
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                Debug.LogWarning("[Phase13 Debug] 无 GameState，请先 Play。");
                return;
            }
            gs.gdpGrowthRate = -3f;
            gs.economicPhase = "recession";
            gs.phaseDuration = 0;
            gs.NotifyChanged();
            Debug.Log("[Phase13 Debug] 经济阶段强制设为：衰退期 (GDP −3%)");
        }

        [MenuItem("Debug/Force Recovery (复苏期)", false, 3103)]
        private static void ForceRecovery()
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                Debug.LogWarning("[Phase13 Debug] 无 GameState，请先 Play。");
                return;
            }
            gs.gdpGrowthRate = 2f;
            gs.economicPhase = "recovery";
            gs.phaseDuration = 0;
            gs.NotifyChanged();
            Debug.Log("[Phase13 Debug] 经济阶段强制设为：复苏期 (GDP +2%)");
        }
    }
}
