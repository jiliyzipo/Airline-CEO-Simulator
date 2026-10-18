using System;

#nullable disable

namespace AirlineCEO.Logic
{
    /// <summary>色盲模式（GDB 29.5）。</summary>
    public enum ColorblindMode
    {
        Standard = 0,
        HighContrast = 1,
        Deuteranopia = 2,   // 红绿色盲
        Tritanopia = 3      // 蓝黄色盲
    }

    /// <summary>字体缩放三级（GDB 29.5）。</summary>
    public enum FontScale
    {
        Standard = 0,
        Large = 1,
        XLarge = 2
    }

    /// <summary>
    /// 无障碍（纯 C#，GDB 29.5）。
    /// 提供状态色调色板（红/黄/绿）的色盲安全变体 + 字体缩放倍率。
    /// UITheme 读取 StatusHex() 重映射 Danger/Warning/Success；UIFactory 读取 FontMultiplier() 缩放字号。
    /// </summary>
    public static class Accessibility
    {
        public static event Action OnChanged;

        public static ColorblindMode Colorblind { get; private set; } = ColorblindMode.Standard;
        public static FontScale FontScaleLevel { get; private set; } = FontScale.Standard;

        public static void SetColorblind(ColorblindMode m)
        {
            if (Colorblind == m) return;
            Colorblind = m;
            OnChanged?.Invoke();
        }

        public static void SetFontScale(FontScale f)
        {
            if (FontScaleLevel == f) return;
            FontScaleLevel = f;
            OnChanged?.Invoke();
        }

        /// <summary>
        /// 状态色 hex（不含 #）：(危险红, 警告黄, 成功绿)。
        /// 色盲安全色基于 Paul Tol / Okabe-Ito 色板：
        /// 红绿盲用 橙/黄/蓝 区分；蓝黄盲用 红/琥珀/天蓝 区分；高对比度提亮。
        /// </summary>
        public static (string danger, string warning, string success) StatusHex() => Colorblind switch
        {
            ColorblindMode.HighContrast => ("FF3B30", "FFCC00", "00E676"),
            ColorblindMode.Deuteranopia => ("D55E00", "F0E442", "0072B2"),
            ColorblindMode.Tritanopia => ("CC0000", "FFB000", "56B4E9"),
            _ => ("D50000", "FFD600", "00C853")
        };

        public static float FontMultiplier() => FontScaleLevel switch
        {
            FontScale.Large => 1.15f,
            FontScale.XLarge => 1.30f,
            _ => 1f
        };

        public static string ColorblindLabel(ColorblindMode m) => m switch
        {
            ColorblindMode.HighContrast => "高对比度",
            ColorblindMode.Deuteranopia => "红绿色盲",
            ColorblindMode.Tritanopia => "蓝黄色盲",
            _ => "标准"
        };

        public static string FontScaleLabel(FontScale f) => f switch
        {
            FontScale.Large => "大",
            FontScale.XLarge => "特大",
            _ => "标准"
        };
    }
}
