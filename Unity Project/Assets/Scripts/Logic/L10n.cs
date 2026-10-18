using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Logic
{
    /// <summary>语言（GDB 33.7：首发简体中文 + 英语(美)，后续扩日/韩/法/西/德）。</summary>
    public enum Language
    {
        ZhCN = 0,
        EnUS = 1
    }

    /// <summary>
    /// 文本本地化（纯 C#，GDB 33.7）。
    /// 键约定 LOC_ 前缀；T(key) 取当前语言文本，缺失回退到 key 本身（无翻译不报错）。
    /// 内置双语词典覆盖「设置/无障碍/遥测/Mod」新面板；全局 UI 硬编码中文字符串后续按需抽到此处。
    /// 类名用 Loc 而非 L10n，避免与 UnityEditor.L10n 冲突。
    /// </summary>
    public static class Loc
    {
        public static event Action<Language> OnLanguageChanged;

        static readonly Dictionary<string, string[]> _dict = new Dictionary<string, string[]>(StringComparer.Ordinal);

        public static Language Current { get; private set; } = Language.ZhCN;

        static Loc()
        {
            // —— 设置 / 通用 ——
            R("LOC_SETTINGS_TITLE", "设置 / 存档", "Settings / Save");
            R("LOC_SETTINGS_SAVE", "存档 (F5)", "Save (F5)");
            R("LOC_SETTINGS_LOAD", "读档 (F9)", "Load (F9)");
            R("LOC_SETTINGS_BACK", "返回游戏", "Back to Game");
            R("LOC_LANGUAGE", "语言 Language", "Language");
            R("LOC_COLORBLIND", "色盲模式", "Colorblind Mode");
            R("LOC_FONTSCALE", "字体缩放", "Font Scale");
            R("LOC_TELEMETRY", "数据遥测", "Telemetry");
            R("LOC_MODS", "Mod 管理", "Mods");
            R("LOC_DISPLAY", "显示模式", "Display Mode");
            R("LOC_DISPLAY_FULLSCREEN", "全屏", "Fullscreen");
            R("LOC_DISPLAY_WINDOW", "窗口", "Windowed");
            R("LOC_DEFAULT_SPEED", "默认速度", "Default Speed");
            R("LOC_CLEAR_SAVE", "清档（保留 API Key）", "Clear Save (keep API key)");
            R("LOC_CLEAR_SAVE_CONFIRM", "已清除游戏进度，请返回主菜单重新开局。", "Game progress cleared. Return to main menu to start over.");

            // —— 色盲模式 ——
            R("LOC_CB_STANDARD", "标准", "Standard");
            R("LOC_CB_HIGHCONTRAST", "高对比度", "High Contrast");
            R("LOC_CB_DEUTERANOPIA", "红绿色盲", "Deuteranopia");
            R("LOC_CB_TRITANOPIA", "蓝黄色盲", "Tritanopia");

            // —— 字体缩放 ——
            R("LOC_FONT_STANDARD", "标准", "Standard");
            R("LOC_FONT_LARGE", "大", "Large");
            R("LOC_FONT_XLARGE", "特大", "X-Large");

            // —— 遥测 ——
            R("LOC_TELE_ON", "已开启（匿名）", "On (anonymous)");
            R("LOC_TELE_OFF", "已关闭（退出）", "Off (opted out)");

            // —— Mod ——
            R("LOC_MODS_NONE", "未加载 Mod", "No mods loaded");
            R("LOC_MODS_COUNT", "已加载 {0} 个 Mod · 冲突 {1} 处", "{0} mod(s) loaded · {1} conflict(s)");

            // —— 外部信息源（现实提供内容，游戏提供时间轴）——
            R("LOC_EXT_TITLE", "外部信息源", "External News Source");
            R("LOC_EXT_ENABLED", "启用自动获取", "Auto Fetch");
            R("LOC_EXT_INTERVAL", "获取间隔（现实）", "Fetch Interval (real time)");
            R("LOC_EXT_MAXPER", "每轮采纳条数", "Max Items per Fetch");
            R("LOC_EXT_FILTER", "关键词过滤（只留民航相关）", "Keyword Filter (aviation only)");
            R("LOC_EXT_URL", "数据源地址（多个用 ; 分隔）", "Source URL(s), separate with ;");
            R("LOC_EXT_URL_HINT", "https://… 或 file://…（多个用 ; 分隔）", "https://… or file://… (separate with ;)");
            R("LOC_EXT_FETCH_NOW", "立即获取一轮", "Fetch Now");
            R("LOC_EXT_DEFAULT", "恢复默认源", "Restore Defaults");
            R("LOC_EXT_CLEAR", "清空缓存", "Clear Cache");
            R("LOC_EXT_INJECT", "离线自测投放", "Offline Test Inject");
            R("LOC_EXT_BACK", "返回", "Back");
            R("LOC_ON", "已开启", "On");
            R("LOC_OFF", "已关闭", "Off");
            R("LOC_EXT_NEVER", "尚未获取", "Not fetched yet");
            R("LOC_EXT_OFF_LABEL", "关闭（仅手动）", "Off (manual only)");
            R("LOC_EXT_EVERY_MIN", "每 {0} 分钟", "every {0} min");
            R("LOC_EXT_EVERY_HOUR", "每 {0} 小时", "every {0} h");
            R("LOC_EXT_ITEMS", "{0} 条", "{0} item(s)");
            R("LOC_EXT_SUM_ON", "已开启 · {0}", "On · {0}");
            R("LOC_EXT_SUM_OFF", "已关闭（仅手动）", "Off (manual only)");
            R("LOC_EXT_FETCHING", "获取中……", "Fetching…");
            R("LOC_EXT_QUEUE", "队列：待生效 {0} 条 · 库内 {1} 条", "Queue: {0} pending · {1} stored");

            // —— 行情源（油价；现实报价作参考基准，季度最多靠拢 ±8%）——
            R("LOC_QUOTE_TITLE", "行情源 · 油价", "Market Data · Fuel");
            R("LOC_QUOTE_OPEN", "行情源 ▸", "Market Data ▸");
            R("LOC_QUOTE_OPEN_ON", "行情源 开 ▸", "Market Data On ▸");
            R("LOC_QUOTE_OPEN_OFF", "行情源 关 ▸", "Market Data Off ▸");
            R("LOC_QUOTE_ENABLED", "油价行情", "Fuel Quote");
            R("LOC_QUOTE_INTERVAL", "报价间隔（现实）", "Quote Interval (real time)");
            R("LOC_QUOTE_CRACK", "裂解价差（美元/吨）", "Crack Spread (USD/ton)");
            R("LOC_QUOTE_URL", "行情地址（返回含 price 字段的 JSON）", "Quote URL (JSON with a price field)");
            R("LOC_QUOTE_URL_HINT", "https://…（留空用默认布伦特合约）", "https://… (blank = default Brent contract)");
            R("LOC_QUOTE_FETCH", "立即获取报价", "Fetch Quote");
            R("LOC_QUOTE_INJECT", "离线自测报价", "Offline Test Quote");
            R("LOC_QUOTE_CURRENT", "当前报价", "Current Quote");
            R("LOC_QUOTE_NEVER", "尚无报价", "No quote yet");
            R("LOC_QUOTE_CALC", "换算", "Converted");
            R("LOC_QUOTE_GAME", "游戏油价", "In-game Price");
            R("LOC_QUOTE_NEXT", "本季收敛后", "After Quarterly Converge");
            R("LOC_QUOTE_NO_CONVERGE", "已足够接近，本季不动", "Close enough, no change this quarter");
            R("LOC_QUOTE_EVERY_DAY", "每 {0} 小时", "every {0} h");
            R("LOC_QUOTE_SUM_ON", "已开启 · {0}", "On · {0}");
            R("LOC_QUOTE_SUM_OFF", "未开启（按游戏自身宏观走）", "Off (game macro only)");
        }

        static void R(string key, string zh, string en) => _dict[key] = new[] { zh, en };

        public static void SetLanguage(Language lang)
        {
            if (Current == lang) return;
            Current = lang;
            OnLanguageChanged?.Invoke(lang);
        }

        /// <summary>取当前语言文本；键缺失回退键名；当前语言无值回退中文。</summary>
        public static string T(string key)
        {
            if (key == null) return "";
            if (_dict.TryGetValue(key, out var arr) && arr != null)
            {
                int i = (int)Current;
                if (i >= 0 && i < arr.Length && !string.IsNullOrEmpty(arr[i]))
                    return arr[i];
                if (!string.IsNullOrEmpty(arr[0]))
                    return arr[0];
            }
            return key;
        }

        public static string T(string key, params object[] args)
        {
            string fmt = T(key);
            try { return args != null && args.Length > 0 ? string.Format(fmt, args) : fmt; }
            catch (FormatException) { return fmt; }
        }

        public static void Register(string key, string zh, string en) => _dict[key] = new[] { zh, en };

        /// <summary>批量注册：CSV "key,zh,en"（首行表头跳过；字段内不应含逗号）。返回注册条数。</summary>
        public static int RegisterCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return 0;
            int n = 0;
            var lines = csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("key", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Split(',');
                if (parts.Length < 3) continue;
                Register(parts[0].Trim(), parts[1].Trim(), parts[2].Trim());
                n++;
            }
            return n;
        }

        public static string LanguageName(Language l) => l == Language.ZhCN ? "中文" : "English";

        public static string CurrentLanguageName => LanguageName(Current);
    }
}
