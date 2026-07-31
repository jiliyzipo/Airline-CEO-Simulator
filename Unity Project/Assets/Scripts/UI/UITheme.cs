using UnityEngine;

namespace AirlineCEO.UI
{
    /// <summary>GDD 33.6 配色与 HUD 视觉常量。</summary>
    public static class UITheme
    {
        public static readonly Color Bg = Hex("0B1A33");
        public static readonly Color Panel = Hex("152A4A");
        public static readonly Color PanelDeep = Hex("1E3A5F");
        public static readonly Color Accent = Hex("1A5CFF");
        public static readonly Color AccentHover = Hex("3D78FF");
        public static readonly Color Success = Hex("00C853");
        public static readonly Color Warning = Hex("FFD600");
        public static readonly Color Danger = Hex("D50000");
        public static readonly Color TextPrimary = Hex("F0F4F8");
        public static readonly Color TextMuted = Hex("A0B4C8");
        public static readonly Color Border = Hex("2A4A6A");
        public static readonly Color Glass = new Color(0.082f, 0.165f, 0.29f, 0.78f);
        public static readonly Color TopBarTop = Hex("0E2448");
        public static readonly Color TopBarBottom = Hex("152A4A");
        public static readonly Color NavIdle = new Color(0.12f, 0.22f, 0.38f, 0.92f);
        public static readonly Color NavActive = Hex("1A5CFF");

        public const float TopBarHeight = 88f;
        public const float LeftNavWidth = 168f;

        public static Color Hex(string hex)
        {
            if (ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out var c))
                return c;
            return Color.magenta;
        }

        /// <summary>按跑道月数取绿→黄→红。</summary>
        public static Color RunwayColor(float months)
        {
            if (months < 2f) return Danger;
            if (months < 3f) return Warning;
            // 2~6 月之间从黄过渡到绿
            float t = Mathf.InverseLerp(3f, 6f, months);
            return Color.Lerp(Warning, Success, Mathf.Clamp01(t));
        }
    }
}
