#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>Phase 0 验收清单（打开 Unity 后点一次）。</summary>
    public static class Phase0VerifyMenu
    {
        [MenuItem("AirlineCEO/Phase 0/Verify Foundation")]
        public static void Verify()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Phase 0 Foundation Check ===");

            CheckDir(sb, "Assets/Prefabs/UI/Panels");
            CheckDir(sb, "Assets/Prefabs/UI/Modals");
            CheckDir(sb, "Assets/Prefabs/UI/Widgets");
            CheckDir(sb, "Assets/Prefabs/UI/Templates");
            CheckDir(sb, "Assets/Resources/UIConfig");
            CheckDir(sb, "Assets/Tests/EditMode");
            CheckDir(sb, "Assets/Tests/PlayMode");

            CheckAsset(sb, "Assets/Prefabs/UI/Widgets/Widget_Badge.prefab");
            CheckAsset(sb, "Assets/Prefabs/UI/Widgets/Widget_StatRow.prefab");
            CheckAsset(sb, "Assets/Prefabs/UI/PREFAB_SPEC.md");

            CheckType(sb, "AirlineCEO.Data.UIConfig");
            CheckType(sb, "AirlineCEO.Data.AircraftTemplate");
            CheckType(sb, "AirlineCEO.Data.RouteTemplate");
            CheckType(sb, "AirlineCEO.UI.Navigation.NavigationRouter");
            CheckType(sb, "AirlineCEO.UI.Framework.BasePanel");
            CheckType(sb, "AirlineCEO.Testing.MockGameState");
            CheckType(sb, "AirlineCEO.Logic.TickExecutionPipeline");

            int uiConfigs = CountAssets("Assets/Resources/UIConfig", "t:UIConfig");
            sb.AppendLine(uiConfigs >= 10
                ? $"[OK] UIConfig assets: {uiConfigs}"
                : $"[!!] UIConfig assets: {uiConfigs}（请运行 AirlineCEO → UI → Create Starter UIConfig Assets）");

            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("Phase 0 Verify", sb.ToString(), "确定");
        }

        static void CheckDir(StringBuilder sb, string path)
        {
            sb.AppendLine(AssetDatabase.IsValidFolder(path) || Directory.Exists(path)
                ? $"[OK] {path}"
                : $"[!!] missing {path}");
        }

        static void CheckAsset(StringBuilder sb, string path)
        {
            sb.AppendLine(File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null
                ? $"[OK] {path}"
                : $"[!!] missing {path}");
        }

        static void CheckType(StringBuilder sb, string fullName)
        {
            var t = System.Type.GetType(fullName + ", AirlineCEO.Runtime")
                    ?? System.Type.GetType(fullName);
            // 遍历已加载程序集
            if (t == null)
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(fullName);
                    if (t != null) break;
                }
            }
            sb.AppendLine(t != null ? $"[OK] type {fullName}" : $"[!!] type {fullName}");
        }

        static int CountAssets(string folder, string filter)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return 0;
            var guids = AssetDatabase.FindAssets(filter, new[] { folder });
            return guids?.Length ?? 0;
        }
    }
}
#endif
