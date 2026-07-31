using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace AirlineCEO.UI
{
    /// <summary>
    /// Phase 4：全局中文 TMP 字体。
    /// 主字体 NotoSansSC；✈ 使用项目内 SegoeUISymbol.ttf 作 Fallback
    /// （系统 CreateDynamicFontFromOSFont 无法给 TMP 提供 Font Data）。
    /// </summary>
    public static class ChineseFontProvider
    {
        public const string AirplaneGlyph = "\u2708"; // ✈
        public const string AirplaneFallbackText = "航"; // Noto 必有，符号字体失败时用

        private static TMP_FontAsset _fontAsset;
        private static TMP_FontAsset _symbolFallback;
        private static bool _logged;
        private static bool _airplaneReady;

        public static TMP_FontAsset FontAsset
        {
            get
            {
                if (_fontAsset != null) return _fontAsset;
                _fontAsset = Resources.Load<TMP_FontAsset>("Fonts/NotoSansSC-SDF");
                if (_fontAsset != null)
                    LogOnce("已加载 Resources/Fonts/NotoSansSC-SDF");
                else
                    _fontAsset = CreateDynamicFromSource();

                EnsureSymbolFallback(_fontAsset);
                return _fontAsset;
            }
        }

        public static TMP_FontAsset Font => FontAsset;

        /// <summary>公司名用的飞机前缀：有 ✈ 字形则用 ✈，否则用「航」。</summary>
        public static string CompanyPlanePrefix =>
            _airplaneReady ? AirplaneGlyph : AirplaneFallbackText;

        public static void WarmupSampleChinese()
        {
            var fa = FontAsset;
            if (fa == null) return;
            const string sample =
                "天际航空驾驶舱运营市场财务顾问北京伦敦马格努斯林德格伦" +
                "综合评定万美元现金跑道季度执行建议首航开通" +
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
                "·→—…【】（）Q航";
            fa.TryAddCharacters(sample);
            EnsureSymbolFallback(fa);
        }

        private static void EnsureSymbolFallback(TMP_FontAsset primary)
        {
            if (primary == null) return;

            if (_symbolFallback == null)
                _symbolFallback = CreateSymbolFallbackFromProjectFont();

            if (_symbolFallback != null)
            {
                // searchFallbacks=false, tryAddCharacter=true：从源字体栅格化 ✈
                _airplaneReady = _symbolFallback.HasCharacter(AirplaneGlyph[0], false, true);

                if (primary.fallbackFontAssetTable == null)
                    primary.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!primary.fallbackFontAssetTable.Contains(_symbolFallback))
                    primary.fallbackFontAssetTable.Add(_symbolFallback);

                Debug.Log(_airplaneReady
                    ? "[ChineseFontProvider] 已挂载 SegoeUISymbol，✈ 可用"
                    : "[ChineseFontProvider] SegoeUISymbol 未含 ✈，公司名改用「航」");
            }
            else
            {
                _airplaneReady = false;
                Debug.LogWarning("[ChineseFontProvider] 未找到 Assets/Fonts/SegoeUISymbol.ttf，公司名改用「航」");
            }
        }

        private static TMP_FontAsset CreateSymbolFallbackFromProjectFont()
        {
            UnityEngine.Font source = null;

#if UNITY_EDITOR
            source = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Font>("Assets/Fonts/SegoeUISymbol.ttf");
            if (source == null)
                source = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Font>("Assets/Resources/Fonts/SegoeUISymbol.ttf");
#endif
            if (source == null)
                source = Resources.Load<UnityEngine.Font>("Fonts/SegoeUISymbol");

            if (source == null)
                return null;

            var asset = TMP_FontAsset.CreateFontAsset(
                source,
                90,
                9,
                GlyphRenderMode.SDFAA,
                512,
                512,
                AtlasPopulationMode.Dynamic,
                true);

            if (asset != null)
                asset.name = "SegoeUISymbol-Fallback";

            return asset;
        }

        private static TMP_FontAsset CreateDynamicFromSource()
        {
            UnityEngine.Font source = null;
            string sourceName = "";

#if UNITY_EDITOR
            source = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Font>("Assets/Fonts/NotoSansSC-Regular.otf");
            if (source != null) sourceName = "Assets/Fonts/NotoSansSC-Regular.otf";
#endif
            if (source == null)
            {
                source = Resources.Load<UnityEngine.Font>("Fonts/NotoSansSC-Regular");
                if (source != null) sourceName = "Resources/Fonts/NotoSansSC-Regular";
            }

            if (source == null)
            {
                source = UnityEngine.Font.CreateDynamicFontFromOSFont(
                    new[]
                    {
                        "Noto Sans SC",
                        "Microsoft YaHei UI",
                        "Microsoft YaHei",
                        "SimHei"
                    },
                    64);
                sourceName = "OS Font";
            }

            if (source == null)
            {
                Debug.LogError("[ChineseFontProvider] 无法创建中文字体。");
                return TMP_Settings.defaultFontAsset;
            }

            var asset = TMP_FontAsset.CreateFontAsset(
                source,
                90,
                9,
                GlyphRenderMode.SDFAA,
                2048,
                2048,
                AtlasPopulationMode.Dynamic,
                true);

            if (asset != null)
            {
                asset.name = "NotoSansSC-Dynamic";
                LogOnce($"已动态创建中文 TMP 字体（来源：{sourceName}）");
            }

            return asset != null ? asset : TMP_Settings.defaultFontAsset;
        }

        private static void LogOnce(string msg)
        {
            if (_logged) return;
            _logged = true;
            Debug.Log("[ChineseFontProvider] " + msg);
        }
    }
}
