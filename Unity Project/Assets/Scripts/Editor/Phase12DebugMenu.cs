#if UNITY_EDITOR
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    public static class Phase12DebugMenu
    {
        [MenuItem("Debug/Force Random Event (Typhoon)", false, 270)]
        public static void ForceTyphoon()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("随机事件", "请在 Play 模式下使用。", "确定");
                return;
            }
            Ensure();
            string msg = RandomEventManager.Instance.ForceTriggerNow("EVT_TYPHOON_01", startChain: false);
            EditorUtility.DisplayDialog("随机事件", msg, "确定");
        }

        [MenuItem("Debug/Force Random Event (Fuel Spike)", false, 271)]
        public static void ForceFuel()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("随机事件", "请在 Play 模式下使用。", "确定");
                return;
            }
            Ensure();
            string msg = RandomEventManager.Instance.ForceTriggerNow("EVT_FUEL_SPIKE");
            EditorUtility.DisplayDialog("随机事件", msg, "确定");
        }

        [MenuItem("Debug/Force Random Event (Demand Surge)", false, 272)]
        public static void ForceDemand()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("随机事件", "请在 Play 模式下使用。", "确定");
                return;
            }
            Ensure();
            string msg = RandomEventManager.Instance.ForceTriggerNow("EVT_DEMAND_SURGE");
            EditorUtility.DisplayDialog("随机事件", msg, "确定");
        }

        [MenuItem("Debug/Force Chain (Typhoon Root)", false, 273)]
        public static void ForceChain()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("随机事件", "请在 Play 模式下使用。", "确定");
                return;
            }
            Ensure();
            string msg = RandomEventManager.Instance.ForceChainNow("EVT_TYPHOON_01");
            EditorUtility.DisplayDialog("随机事件 · 连锁",
                msg + "\n\n已预约后续连锁。请再推进若干季度观察跑道积压→保险→疲劳→公关。", "确定");
        }

        [MenuItem("Debug/List Random Event Catalog", false, 274)]
        public static void ListCatalog()
        {
            var all = RandomEventCatalog.All;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"事件池共 {all.Count} 条：");
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null) continue;
                sb.AppendLine($"{e.id} | {e.category} | w={e.weight:F2} | {e.title}");
            }
            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("事件池", $"共 {all.Count} 条，详情见 Console。", "确定");
        }

        private static void Ensure()
        {
            if (RandomEventManager.Instance == null)
            {
                var go = new GameObject("RandomEventManager");
                go.AddComponent<RandomEventManager>();
            }
        }
    }
}
#endif
