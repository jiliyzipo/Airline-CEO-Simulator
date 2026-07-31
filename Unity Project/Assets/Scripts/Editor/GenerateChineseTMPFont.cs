using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace AirlineCEO.EditorTools
{
    /// <summary>Phase 4：从 NotoSansSC-Regular.otf 生成动态 TMP 中文字体资源。</summary>
    public static class GenerateChineseTMPFont
    {
        private const string SourcePath = "Assets/Fonts/NotoSansSC-Regular.otf";
        private const string OutputDir = "Assets/Resources/Fonts";
        private const string OutputPath = OutputDir + "/NotoSansSC-SDF.asset";

        [MenuItem("AirlineCEO/Generate Chinese TMP Font Asset")]
        public static void GenerateMenu()
        {
            if (TryGenerate(out string msg, showDialogs: true))
                Debug.Log("[AirlineCEO] " + msg);
            else
                Debug.LogError("[AirlineCEO] " + msg);
        }

        /// <summary>Setup 场景时静默确保字体资源存在。</summary>
        public static void EnsureExists()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath) != null)
                return;
            TryGenerate(out _, showDialogs: false);
        }

        public static bool TryGenerate(out string message, bool showDialogs)
        {
            var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(SourcePath);
            if (source == null)
            {
                message = $"未找到：{SourcePath}。请将 NotoSansSC-Regular.otf 放入 Assets/Fonts/";
                if (showDialogs)
                    EditorUtility.DisplayDialog("缺少字体文件", message, "确定");
                return false;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/Resources", "Fonts");

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath) != null)
                AssetDatabase.DeleteAsset(OutputPath);

            var fontAsset = TMP_FontAsset.CreateFontAsset(
                source,
                90,
                9,
                GlyphRenderMode.SDFAA,
                2048,
                2048,
                AtlasPopulationMode.Dynamic,
                true);

            if (fontAsset == null)
            {
                message = "TMP_FontAsset.CreateFontAsset 返回 null";
                if (showDialogs)
                    EditorUtility.DisplayDialog("失败", message, "确定");
                return false;
            }

            fontAsset.name = "NotoSansSC-SDF";
            AssetDatabase.CreateAsset(fontAsset, OutputPath);

            if (fontAsset.material != null)
            {
                fontAsset.material.name = "NotoSansSC-SDF Atlas Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            if (fontAsset.atlasTextures != null)
            {
                for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
                {
                    var tex = fontAsset.atlasTextures[i];
                    if (tex == null) continue;
                    tex.name = $"NotoSansSC-SDF Atlas {i}";
                    AssetDatabase.AddObjectToAsset(tex, fontAsset);
                }
            }

            const string sample =
                "天际航空驾驶舱运营市场财务顾问北京伦敦马格努斯林德格伦瑞典" +
                "综合评定万美元现金跑道季度执行建议首航开通分析发送关闭确认" +
                "流动性还可以撑个月偏危险有点紧目前还算稳妥暂缓扩张董事会满意度" +
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
                "·→—…【】（）：，。！？Q¥$%";
            fontAsset.TryAddCharacters(sample);

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                var prop = so.FindProperty("m_defaultFontAsset");
                if (prop != null)
                {
                    prop.objectReferenceValue = fontAsset;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }
            }

            message = $"已生成中文 TMP 字体：{OutputPath}";
            if (showDialogs)
            {
                EditorUtility.DisplayDialog("完成", "已生成 NotoSansSC-SDF（Dynamic）。\n请 Play 验收中文是否正常显示。", "确定");
                Selection.activeObject = fontAsset;
            }

            return true;
        }
    }
}
