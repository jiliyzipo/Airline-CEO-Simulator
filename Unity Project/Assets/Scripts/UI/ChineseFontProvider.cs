using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace AirlineCEO.UI
{
    /// <summary>
    /// Phase 4：全局中文 TMP 字体。
    /// 主字体 NotoSansSC；✈ 使用项目内 SegoeUISymbol.ttf 作 Fallback。
    /// 运行时使用 Instantiate 副本，避免脏写 Resources 里的 SDF 资源触发 NativeFontImporter 警告。
    /// </summary>
    public static class ChineseFontProvider
    {
        public const string AirplaneGlyph = "\u2708"; // ✈
        public const string AirplaneFallbackText = "航";

        private static TMP_FontAsset _fontAsset;
        private static TMP_FontAsset _symbolFallback;
        private static bool _loggedLoad;
        private static bool _loggedFallback;
        private static bool _airplaneReady;

        /// <summary>底栏/机队高频缺字。静态 SDF 常含「度」却不含这些字，探针必须覆盖它们。</summary>
        const string EssentialChinese = "维护财务愿景机队适航租赁折旧选择注册总部基地";

        public static TMP_FontAsset FontAsset
        {
            get
            {
                if (_fontAsset != null) return _fontAsset;

                // 烘焙 SDF 图集常把「选择」等后补字映射错（开局页按钮显示成乱码）。
                // 优先从 OTF / 雅黑重建动态字体；烘焙资源只作最后兜底。
                _fontAsset = CreateDynamicFromSource();
                if (_fontAsset == null || !CanRenderEssentialChinese(_fontAsset))
                {
                    var baked = Resources.Load<TMP_FontAsset>("Fonts/NotoSansSC-SDF");
                    if (baked != null)
                    {
                        var clone = Object.Instantiate(baked);
                        clone.name = baked.name + " (Runtime)";
                        clone.hideFlags = HideFlags.HideAndDontSave;
                        clone.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                        clone.isMultiAtlasTexturesEnabled = true;
                        if (CanRenderEssentialChinese(clone))
                        {
                            if (_fontAsset != null && _fontAsset != clone)
                                Object.Destroy(_fontAsset);
                            _fontAsset = clone;
                            LogLoad("动态字体缺「选择」等字，回退 NotoSansSC-SDF 运行时副本");
                        }
                        else
                            Object.Destroy(clone);
                    }
                }

                if (_fontAsset != null)
                    _fontAsset.TryAddCharacters(EssentialChinese);

                EnsureSymbolFallback(_fontAsset);
                return _fontAsset;
            }
        }

        public static TMP_FontAsset Font => FontAsset;

        public static string CompanyPlanePrefix =>
            _airplaneReady ? AirplaneGlyph : AirplaneFallbackText;

        /// <summary>
        /// 彩色 Emoji 全集（Segoe UI Emoji 是 COLR 彩色字体，TMP SDF 无法提取字形，
        /// 这些 astral 平面字符必触发「字体缺失」警告）。统一去掉图标（映射为空串），
        /// 只保留有功能语义的「锁」标记由 ShellBarView 单独处理（不用此表）。
        /// 覆盖：用「非 BMP 字符扫描」全量枚举 Assets/Scripts 内出现的全部彩色 Emoji。
        /// </summary>
        static readonly Dictionary<string, string> EmojiMap = new Dictionary<string, string>
        {
            ["🆘"] = "", ["🌉"] = "", ["🌍"] = "", ["🌏"] = "", ["🌐"] = "",
            ["🎁"] = "", ["🎉"] = "", ["🎓"] = "", ["🎫"] = "", ["🎬"] = "",
            ["🎮"] = "", ["🏆"] = "", ["🏗"] = "", ["🏛"] = "", ["🏦"] = "",
            ["🏬"] = "", ["🏭"] = "", ["👀"] = "", ["👍"] = "", ["👤"] = "",
            ["👥"] = "", ["💀"] = "", ["💡"] = "", ["💬"] = "", ["💰"] = "",
            ["💳"] = "", ["💸"] = "", ["💹"] = "", ["📁"] = "", ["📅"] = "",
            ["📈"] = "", ["📉"] = "", ["📊"] = "", ["📋"] = "", ["📍"] = "",
            ["📐"] = "", ["📖"] = "", ["📜"] = "", ["📡"] = "", ["📦"] = "",
            ["📰"] = "", ["🔄"] = "", ["🔍"] = "", ["🔒"] = "", ["🔔"] = "",
            ["🔬"] = "", ["🔴"] = "", ["🔵"] = "", ["🗓"] = "", ["🗺"] = "",
            ["🚀"] = "", ["🚄"] = "", ["🚨"] = "", ["🛡"] = "", ["🛢"] = "",
            ["🛩"] = "", ["🛫"] = "", ["🛰"] = "", ["🟡"] = "", ["🟢"] = "",
            ["🤖"] = "", ["🤝"] = "", ["🧪"] = "", ["🪑"] = "",
        };

        /// <summary>把 SDF 无法渲染的彩色 Emoji 去掉（含紧随其后的一个空格），消除「字体缺失」黄警告。</summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // 剥离 emoji/文本变体选择符（VS16 U+FE0F / VS15 U+FE0E），避免其自身触发缺字警告
            text = text.Replace("\uFE0F", "").Replace("\uFE0E", "");
            foreach (var key in EmojiMap.Keys)
            {
                text = text.Replace(key + " ", ""); // 前缀图标：连后面空格一起删
                text = text.Replace(key, "");       // 孤立图标兜底
            }
            return text;
        }

        /// <summary>把将要显示的字补进动态图集，避免方框缺字。</summary>
        public static void EnsureCharacters(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var fa = FontAsset;
            if (fa == null) return;
            fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fa.isMultiAtlasTexturesEnabled = true;
            fa.TryAddCharacters(text);
        }

        /// <summary>读档后把存档里的中文科目补进图集，避免航线/收件箱突然缺字或错字。</summary>
        public static void RefreshAfterLoad()
        {
            var fa = FontAsset;
            if (fa != null)
            {
                fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                fa.isMultiAtlasTexturesEnabled = true;
            }
            WarmupSampleChinese();
            var gs = AirlineCEO.Systems.GameState.Instance;
            if (gs?.inbox == null) return;
            var sb = new System.Text.StringBuilder(512);
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null) continue;
                sb.Append(m.subject).Append(m.sender).Append(m.role).Append(m.tag)
                    .Append(m.permitNote).Append(m.permitGate);
            }
            EnsureCharacters(sb.ToString());
        }

        public static void WarmupSampleChinese()
        {
            var fa = FontAsset;
            if (fa == null) return;
            const string sample =
                "度万开场财务维护愿景搜索机场代码名称回车定位" +
                "年度目标确认董事会满意度万美元现金跑道季度执行建议开通航线打开完整预览结算" +
                "天际航空驾驶舱运营市场财务顾问北京伦敦马格努斯林德格伦" +
                "综合评定构建航班环基地目的地经停选择确认取消" +
                "收入支出盈利机队适航仪表盘人员培训安全文化总览" +
                "预算对冲碳配额融资宏观审批" +
                "上海虹桥浦东广州深圳成都重庆西安昆明杭州南京武汉长沙厦门福州青岛天津大连沈阳哈尔滨乌鲁木齐兰州三亚海口" +
                "首尔东京大阪香港澳门台北新加坡曼谷吉隆坡巴黎法兰克福阿姆斯特丹洛杉矶旧金山纽约悉尼迪拜" +
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
                "·→—…【】（）：，。！？Q¥$%≥≤" +
                "埃里克索尔伯格陈慧玲卡洛斯费尔南德斯娜奥米坦纳伊万索科洛夫玛丽亚桑托斯" +
                "乔纳森布莱克伊莎贝拉罗西阿米特辛格金秀贤马库斯温特穆罕默德拉希德" +
                "拟开航准入办理进度航权时刻槽异议重申材料运力审查节点通过率预计受阻公示批复发证核准";
            fa.TryAddCharacters(sample);
            fa.TryAddCharacters(EssentialChinese);
            EnsureSymbolFallback(fa);

            if (!CanRenderEssentialChinese(fa))
            {
                Debug.LogWarning("[ChineseFontProvider] 预热后仍缺「维护/财务」，改用系统中文动态字体");
                var replacement = CreateDynamicFromSource();
                if (replacement != null && replacement != fa)
                {
                    _fontAsset = replacement;
                    replacement.TryAddCharacters(sample);
                    replacement.TryAddCharacters(EssentialChinese);
                    EnsureSymbolFallback(replacement);
                }
            }
        }

        static bool CanRenderEssentialChinese(TMP_FontAsset fa)
        {
            if (fa == null) return false;
            fa.TryAddCharacters(EssentialChinese);
            for (int i = 0; i < EssentialChinese.Length; i++)
            {
                if (!fa.HasCharacter(EssentialChinese[i], searchFallbacks: false, tryAddCharacter: true))
                    return false;
            }
            return true;
        }

        private static void EnsureSymbolFallback(TMP_FontAsset primary)
        {
            if (primary == null) return;

            if (_symbolFallback == null)
                _symbolFallback = CreateSymbolFallbackFromProjectFont();

            if (_symbolFallback != null)
            {
                _airplaneReady = _symbolFallback.HasCharacter(AirplaneGlyph[0], false, true);

                if (primary.fallbackFontAssetTable == null)
                    primary.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!primary.fallbackFontAssetTable.Contains(_symbolFallback))
                    primary.fallbackFontAssetTable.Add(_symbolFallback);

                if (!_loggedFallback)
                {
                    _loggedFallback = true;
                    Debug.Log(_airplaneReady
                        ? "[ChineseFontProvider] 已挂载 SegoeUISymbol，✈ 可用"
                        : "[ChineseFontProvider] SegoeUISymbol 未含 ✈，公司名改用「航」");
                }
            }
            else
            {
                _airplaneReady = false;
                if (!_loggedFallback)
                {
                    _loggedFallback = true;
                    Debug.LogWarning("[ChineseFontProvider] 未找到 Assets/Fonts/SegoeUISymbol.ttf，公司名改用「航」");
                }
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
            {
                asset.name = "SegoeUISymbol-Fallback";
                asset.hideFlags = HideFlags.HideAndDontSave;
            }

            return asset;
        }

        private static TMP_FontAsset CreateDynamicFromSource()
        {
            var fromFile = CreateFromFontFile(
#if UNITY_EDITOR
                Application.dataPath + "/Fonts/NotoSansSC-Regular.otf",
                Application.dataPath + "/Resources/Fonts/NotoSansSC-Regular.otf",
#endif
                Application.dataPath + "/../Fonts/NotoSansSC-Regular.otf",
                System.IO.Path.Combine(Application.streamingAssetsPath, "NotoSansSC-Regular.otf"),
                @"C:\Windows\Fonts\msyh.ttc",
                @"C:\Windows\Fonts\msyh.ttf",
                @"C:\Windows\Fonts\msyhl.ttc",
                @"C:\Windows\Fonts\simhei.ttf",
                @"C:\Windows\Fonts\simsun.ttc"
            );
            if (fromFile != null)
            {
                fromFile.hideFlags = HideFlags.HideAndDontSave;
                if (CanRenderEssentialChinese(fromFile))
                {
                    fromFile.name = "CJK-Dynamic-File";
                    LogLoad("已从本地/系统字体文件创建中文 TMP（可补维护/财务）");
                    return fromFile;
                }
                Object.Destroy(fromFile);
            }

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
                        "Microsoft YaHei UI",
                        "Microsoft YaHei",
                        "微软雅黑",
                        "Noto Sans SC",
                        "Source Han Sans SC",
                        "SimHei",
                        "SimSun"
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
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.TryAddCharacters(EssentialChinese);
                LogLoad($"已动态创建中文 TMP 字体（来源：{sourceName}）");
                if (!CanRenderEssentialChinese(asset))
                    Debug.LogWarning("[ChineseFontProvider] OS 字体仍缺关键汉字，请把 NotoSansSC-Regular.otf 放回 Assets/Fonts/");
            }

            return asset != null ? asset : TMP_Settings.defaultFontAsset;
        }

        static TMP_FontAsset CreateFromFontFile(params string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;
                var asset = TMP_FontAsset.CreateFontAsset(
                    path, 0, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048);
                if (asset == null) continue;
                asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                asset.isMultiAtlasTexturesEnabled = true;
                return asset;
            }
            return null;
        }

        private static void LogLoad(string msg)
        {
            if (_loggedLoad) return;
            _loggedLoad = true;
            Debug.Log("[ChineseFontProvider] " + msg);
        }
    }
}
