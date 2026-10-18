using AirlineCEO.Logic;
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

        // 状态色改为动态 getter：随无障碍「色盲模式」重映射（GDB 29.5）。
        // 红(危险)/黄(警告)/绿(成功) 三色在色盲模式下换成可区分的调色板，全游戏即时生效。
        static Color _danger = Hex("D50000");
        static Color _warning = Hex("FFD600");
        static Color _success = Hex("00C853");
        static ColorblindMode _cachedColorblind = (ColorblindMode)(-1);

        public static Color Danger { get { RefreshStatusColors(); return _danger; } }
        public static Color Warning { get { RefreshStatusColors(); return _warning; } }
        public static Color Success { get { RefreshStatusColors(); return _success; } }

        static void RefreshStatusColors()
        {
            var cb = Accessibility.Colorblind;
            if (cb == _cachedColorblind) return;
            _cachedColorblind = cb;
            var (d, w, s) = Accessibility.StatusHex();
            _danger = Hex(d);
            _warning = Hex(w);
            _success = Hex(s);
        }

        public static readonly Color TextPrimary = Hex("F0F4F8");
        public static readonly Color TextMuted = Hex("A0B4C8");
        public static readonly Color Border = Hex("2A4A6A");
        public static readonly Color Glass = new Color(0.082f, 0.165f, 0.29f, 0.78f);
        public static readonly Color TopBarTop = Hex("0E2448");
        public static readonly Color TopBarBottom = Hex("152A4A");
        public static readonly Color NavIdle = new Color(0.12f, 0.22f, 0.38f, 0.92f);
        public static readonly Color NavActive = Hex("1A5CFF");
        /// <summary>收件箱（核心入口）未选中：暖色底，和普通部门 tab 区分。</summary>
        public static readonly Color NavCoreIdle = new Color(0.28f, 0.20f, 0.08f, 0.95f);
        /// <summary>收件箱选中：琥珀金。</summary>
        public static readonly Color NavCoreActive = Hex("C9A227");

        public const float TopBarHeight = 88f;
        public const float LeftNavWidth = 168f;
        /// <summary>顶部「全球航空动态」跑马灯高度（紧贴顶栏下方）。</summary>
        public const float TickerHeight = 22f;

        /// <summary>主驾驶舱 Canvas。</summary>
        public const int SortMain = 10;
        /// <summary>顾问气泡：高于航线/机队等内容页，低于阻断弹窗。</summary>
        public const int SortAdvisor = 50;
        /// <summary>续聘 / AOG / 终局 / 开局向导等必须挡住操作的层。</summary>
        public const int SortBlockingModal = 100;

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
