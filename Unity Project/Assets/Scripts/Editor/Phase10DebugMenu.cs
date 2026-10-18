#if UNITY_EDITOR
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    public static class Phase10DebugMenu
    {
        [MenuItem("Debug/Grant Fleet Cash +30000", false, 250)]
        public static void GrantFleetCash()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("机队", "请在 Play 模式下使用。", "确定");
                return;
            }

            GameState.Instance.cash += 30000f;
            GameState.Instance.NotifyChanged();
            Debug.Log("[Phase10] 现金 +30000 → " + GameState.Instance.cash);
            EditorUtility.DisplayDialog("机队", $"已注入 30000 万采购资金。\n当前现金 {GameState.Instance.cash:F0} 万。", "确定");
        }

        [MenuItem("Debug/Add Random Aircraft", false, 251)]
        public static void AddRandomAircraft()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("机队", "请在 Play 模式下使用。", "确定");
                return;
            }

            if (FleetManager.Instance == null)
            {
                var go = new GameObject("FleetManager");
                go.AddComponent<FleetManager>();
            }

            FleetManager.Instance.EnsureStarterFleet();
            var types = DataBase.Instance != null ? DataBase.Instance.AircraftTypes : null;
            string typeId = "A320neo";
            if (types != null && types.Count > 0)
                typeId = types[Random.Range(0, types.Count)].typeId;

            // 临时补足现金再购买，避免 Debug 因现金失败
            var catalog = DataBase.Instance?.GetAircraftType(typeId);
            float need = catalog != null ? catalog.priceWan : 11000f;
            if (GameState.Instance.cash < need)
                GameState.Instance.cash += need;

            bool ok = FleetManager.Instance.TryBuyAircraft(typeId, out string msg);
            Debug.Log("[Phase10] " + msg);
            EditorUtility.DisplayDialog("机队", ok ? msg : ("失败：" + msg), "确定");
        }

        [MenuItem("Debug/Force Fleet Age +1 Year", false, 252)]
        public static void ForceAge()
        {
            if (!Application.isPlaying || FleetManager.Instance == null)
            {
                EditorUtility.DisplayDialog("机队", "请在 Play 模式下使用。", "确定");
                return;
            }

            FleetManager.Instance.OnYearEndAgeFleet();
            GameState.Instance?.NotifyChanged();
            EditorUtility.DisplayDialog("机队", "已强制全体非退役飞机机龄 +1。", "确定");
        }
    }
}
#endif
