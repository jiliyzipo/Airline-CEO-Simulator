#if UNITY_EDITOR
using AirlineCEO.AI;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    public static class SetupMVPScene
    {
        [MenuItem("AirlineCEO/Setup MVP Scene Objects")]
        public static void Setup()
        {
            EnsureConfigAsset();
            Ensure<GameState>("GameState");
            Ensure<SaveManager>("SaveManager");
            Ensure<DataBase>("DataBase");
            EnsureAIChatManager();
            Ensure<MVPBootstrap>("MVPBootstrap");

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog(
                "MVP 场景就绪",
                "已添加核心组件。\n配置 Key：AirlineCEO → Set DeepSeek API Key",
                "确定");
        }

        [MenuItem("AirlineCEO/Set DeepSeek API Key")]
        public static void SetApiKey()
        {
            DeepSeekKeyWindow.Open();
        }

        [MenuItem("AirlineCEO/Create DeepSeek Config Asset")]
        public static void EnsureConfigAsset()
        {
            const string dir = "Assets/Resources";
            const string path = "Assets/Resources/DeepSeekConfig.asset";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var existing = AssetDatabase.LoadAssetAtPath<DeepSeekConfig>(path);
            if (existing != null)
                return;

            var cfg = ScriptableObject.CreateInstance<DeepSeekConfig>();
            AssetDatabase.CreateAsset(cfg, path);
            AssetDatabase.SaveAssets();
            Debug.Log("[AirlineCEO] 已创建 " + path);
        }

        private static void EnsureAIChatManager()
        {
            var existing = Object.FindObjectOfType<AIChatManager>();
            var cfg = AssetDatabase.LoadAssetAtPath<DeepSeekConfig>("Assets/Resources/DeepSeekConfig.asset");
            if (existing != null)
            {
                BindConfig(existing, cfg);
                return;
            }

            var go = new GameObject("AIChatManager");
            var mgr = go.AddComponent<AIChatManager>();
            BindConfig(mgr, cfg);
            Undo.RegisterCreatedObjectUndo(go, "Create AIChatManager");
        }

        private static void BindConfig(AIChatManager mgr, DeepSeekConfig cfg)
        {
            if (mgr == null || cfg == null) return;
            var so = new SerializedObject(mgr);
            var prop = so.FindProperty("config");
            if (prop != null)
            {
                prop.objectReferenceValue = cfg;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void Ensure<T>(string name) where T : Component
        {
            var existing = Object.FindObjectOfType<T>();
            if (existing != null) return;
            var go = new GameObject(name);
            go.AddComponent<T>();
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        }
    }

    public class DeepSeekKeyWindow : EditorWindow
    {
        private string _key = "";

        public static void Open()
        {
            var win = GetWindow<DeepSeekKeyWindow>(true, "DeepSeek API Key", true);
            win.minSize = new Vector2(460, 160);
            win._key = PlayerPrefs.GetString(DeepSeekConfig.PlayerPrefsKey, "");
            win.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "粘贴 DeepSeek API Key。仅存本机 PlayerPrefs，不会进 Git。\n留空并点「清除」可只用本地话术。",
                MessageType.Info);
            _key = EditorGUILayout.PasswordField("API Key", _key);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("清除", GUILayout.Height(28)))
                {
                    PlayerPrefs.DeleteKey(DeepSeekConfig.PlayerPrefsKey);
                    PlayerPrefs.Save();
                    _key = "";
                    EditorUtility.DisplayDialog("已清除", "之后 Neural Command 将使用本地话术库。", "确定");
                }

                if (GUILayout.Button("保存", GUILayout.Height(28)))
                {
                    string trimmed = (_key ?? "").Trim();
                    if (string.IsNullOrEmpty(trimmed))
                    {
                        EditorUtility.DisplayDialog("提示", "Key 为空。若要清除请点「清除」。", "确定");
                        return;
                    }

                    PlayerPrefs.SetString(DeepSeekConfig.PlayerPrefsKey, trimmed);
                    PlayerPrefs.Save();
                    EditorUtility.DisplayDialog("已保存", "Play 后将优先调用 DeepSeek（3 秒超时，失败自动本地容灾）。", "确定");
                    Close();
                }
            }
        }
    }
}
#endif
