using AirlineCEO.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.EditorTools
{
    /// <summary>Phase 3：生成 CashMeter / RouteCard / ChatBubble Prefab 到 Resources/UI。</summary>
    public static class CreateUIPrefabs
    {
        private const string Dir = "Assets/Resources/UI";

        [MenuItem("AirlineCEO/Create UI Prefabs (Phase 3)")]
        public static void CreateAll()
        {
            EnsureDir();
            ChineseFontProvider.WarmupSampleChinese();

            CreateCashMeterPrefab();
            CreateRouteCardPrefab();
            CreateChatBubblePrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[AirlineCEO] 已创建 Prefab：CashMeter / RouteCard / ChatBubble → " + Dir);
        }

        private static void EnsureDir()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets/Resources", "UI");
        }

        private static void CreateCashMeterPrefab()
        {
            var host = new GameObject("CashMeterHost", typeof(RectTransform));
            var meter = CashMeter.BuildRuntime(host.transform, UIFactory.Fill);
            meter.gameObject.name = "CashMeter";
            meter.transform.SetParent(null, false);
            Object.DestroyImmediate(host);
            SavePrefab(meter.gameObject, Dir + "/CashMeter.prefab");
        }

        private static void CreateRouteCardPrefab()
        {
            var host = new GameObject("RouteCardHost", typeof(RectTransform));
            var card = RouteCard.BuildRuntime(host.transform);
            card.gameObject.name = "RouteCard";
            card.transform.SetParent(null, false);
            Object.DestroyImmediate(host);

            var le = card.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = card.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 72;
            le.flexibleWidth = 1;

            SavePrefab(card.gameObject, Dir + "/RouteCard.prefab");
        }

        private static void CreateChatBubblePrefab()
        {
            var host = new GameObject("ChatHost", typeof(RectTransform));
            var bubble = ChatBubble.BuildRuntime(host.transform, UIFactory.Fill);
            bubble.gameObject.name = "ChatBubble";
            bubble.transform.SetParent(null, false);
            Object.DestroyImmediate(host);
            SavePrefab(bubble.gameObject, Dir + "/ChatBubble.prefab");
        }

        private static void SavePrefab(GameObject go, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                AssetDatabase.DeleteAsset(path);

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }
    }
}
