#if UNITY_EDITOR
using System.IO;
using System.Text;
using AirlineCEO.UI.Navigation;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    public static class Phase1VerifyMenu
    {
        [MenuItem("AirlineCEO/Phase 1/Verify Prefab Shell")]
        public static void Verify()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Phase 1 Prefab Shell Check ===");

            CheckPrefab(sb, UIPrefabCatalog.PanelsFolder + "/" + UIPrefabCatalog.PanelBase + ".prefab");
            CheckPrefab(sb, UIPrefabCatalog.PanelsFolder + "/" + UIPrefabCatalog.PanelScrollList + ".prefab");
            CheckPrefab(sb, UIPrefabCatalog.ModalsFolder + "/" + UIPrefabCatalog.ModalDecision + ".prefab");
            CheckPrefab(sb, UIPrefabCatalog.WidgetsFolder + "/" + UIPrefabCatalog.WidgetStatRow + ".prefab");
            CheckPrefab(sb, UIPrefabCatalog.WidgetsFolder + "/" + UIPrefabCatalog.WidgetProgressBar + ".prefab");
            CheckPrefab(sb, UIPrefabCatalog.WidgetsFolder + "/" + UIPrefabCatalog.WidgetTabGroup + ".prefab");
            CheckPrefab(sb, UIPrefabCatalog.TemplatesFolder + "/" + UIPrefabCatalog.TemplateListItem + ".prefab");
            CheckPrefab(sb, UIPrefabCatalog.PanelsFolder + "/" + UIPrefabCatalog.ShellN0 + ".prefab");

            int deptOk = 0;
            foreach (var d in UIPrefabCatalog.DepartmentL1)
            {
                string path = UIPrefabCatalog.PanelsFolder + "/" + d.fileName + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) deptOk++;
                else sb.AppendLine("[!!] missing " + path);
            }
            sb.AppendLine(deptOk == UIPrefabCatalog.DepartmentL1.Length
                ? $"[OK] Department L1 prefabs: {deptOk}"
                : $"[!!] Department L1 prefabs: {deptOk}/{UIPrefabCatalog.DepartmentL1.Length}");

            var configs = Resources.LoadAll<AirlineCEO.Data.UIConfig>(UIPrefabCatalog.ResourcesConfigFolder);
            int withPrefab = 0;
            if (configs != null)
            {
                for (int i = 0; i < configs.Length; i++)
                    if (configs[i] != null && configs[i].prefab != null) withPrefab++;
            }
            sb.AppendLine($"[i] Resources/UIConfig: {(configs?.Length ?? 0)} assets, {withPrefab} with prefab");

            if ((configs?.Length ?? 0) < 8)
                sb.AppendLine("[!!] 请运行：AirlineCEO → Phase 1 → Create Framework & L1 Prefabs");

            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("Phase 1 Verify", sb.ToString(), "确定");
        }

        static void CheckPrefab(StringBuilder sb, string path)
        {
            sb.AppendLine(AssetDatabase.LoadAssetAtPath<GameObject>(path) != null
                ? "[OK] " + Path.GetFileName(path)
                : "[!!] missing " + path);
        }
    }
}
#endif
