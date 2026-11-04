#if UNITY_EDITOR
using System.IO;
using AirlineCEO.Data;
using AirlineCEO.UI;
using AirlineCEO.UI.Navigation;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>Phase 2 Week 4：生成 OPS L2/L3/L4 Prefab 并写入 Resources/UIConfig。</summary>
    public static class CreateOpsPrefabs
    {
        [MenuItem("AirlineCEO/Phase 2/Create OPS Prefabs & UIConfig")]
        public static void CreateAll()
        {
            ChineseFontProvider.WarmupSampleChinese();
            Ensure("Assets", "Prefabs");
            Ensure("Assets/Prefabs", "UI");
            Ensure(UIPrefabCatalog.PrefabRoot, "Panels");
            Ensure(UIPrefabCatalog.PrefabRoot, "Modals");
            Ensure("Assets", "Resources");
            Ensure("Assets/Resources", "UIConfig");

            SaveOpsPage("n42_航班周总计划", "n42", "航班周总计划");
            SaveOpsPage("n43_航班详细计划", "n43", "航班飞行计划");
            SaveOpsPage("n66_航季总纲", "n66", "航季总纲");
            // ★2026-11-01：n36「航班环构建器」整页删除 —— 那块看板已搬进
            //   「机队 › 周计划及航班环」页签，不再单独出一页。
            SaveIrrops();

            BindConfigs();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Phase 2 OPS",
                "已生成 n42/n43/n66/n24 Prefab 并绑定 Resources/UIConfig。", "确定");
        }

        static void SaveOpsPage(string pageId, string kind, string title)
        {
            var panel = OpsSubpagePanel.Build(kind, title);
            panel.BindPage(pageId, title);
            string path = UIPrefabCatalog.PanelsFolder + "/Panel_OPS_" + kind + ".prefab";
            var go = panel.gameObject;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        static void SaveIrrops()
        {
            var panel = OpsIrropsModalUI.Build();
            string path = UIPrefabCatalog.ModalsFolder + "/Modal_OPS_n24_IRROPS.prefab";
            var go = panel.gameObject;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        static void BindConfigs()
        {
            BindOne("n42_航班周总计划", "航班周总计划", "L2", "n13_运营部", "n42", false);
            BindOne("n43_航班详细计划", "航班飞行计划", "L2", "n42_航班周总计划", "n43", false);
            BindOne("n66_航季总纲", "航季总纲", "L2", "n13_运营部", "n66", false);
            // ★2026-11-01：n36 页已删除，不再绑定 UIConfig（看板改在机队页签内）。
            BindIrrops();
        }

        static void BindIrrops()
        {
            string pageId = "n24_危机_IRROPS_弹窗";
            string safe = pageId;
            foreach (var c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            string path = $"Assets/Resources/UIConfig/{safe}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UIConfig>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<UIConfig>();
                AssetDatabase.CreateAsset(asset, path);
            }
            asset.pageId = pageId;
            asset.title = "航班异常";
            asset.level = "L4";
            asset.parentId = "n13_运营部";
            asset.tabGroup = "ops";
            asset.badge = "Phase2";
            asset.meta = "OPS L4";
            asset.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                UIPrefabCatalog.ModalsFolder + "/Modal_OPS_n24_IRROPS.prefab");
            EditorUtility.SetDirty(asset);
        }

        static void BindOne(string pageId, string title, string level, string parentId, string kind, bool modal)
        {
            string safe = pageId;
            foreach (var c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            string path = $"Assets/Resources/UIConfig/{safe}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UIConfig>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<UIConfig>();
                AssetDatabase.CreateAsset(asset, path);
            }
            asset.pageId = pageId;
            asset.title = title;
            asset.level = level;
            asset.parentId = parentId;
            asset.tabGroup = "ops";
            asset.badge = "Phase2";
            asset.meta = "OPS " + level;
            string prefabPath = modal
                ? UIPrefabCatalog.ModalsFolder + "/Modal_OPS_" + kind + ".prefab"
                : UIPrefabCatalog.PanelsFolder + "/Panel_OPS_" + kind + ".prefab";
            asset.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            EditorUtility.SetDirty(asset);
        }

        static void Ensure(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
