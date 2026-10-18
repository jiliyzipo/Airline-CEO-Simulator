using UnityEngine;

namespace AirlineCEO.UI
{
    /// <summary>
    /// Phase 14 · 机型图标生成器（运行时程序化生成 3 种飞机图标）。
    /// </summary>
    public static class AircraftIcon
    {
        private const int Size = 64;

        private static Sprite _a320;
        private static Sprite _b737;
        private static Sprite _b787;

        public static Sprite ForType(string typeId)
        {
            string key = (typeId ?? "").ToUpperInvariant();
            if (key.Contains("A320"))
            {
                if (_a320 == null) _a320 = BuildNarrow("A320", new Color(0.2f, 0.55f, 0.85f));
                return _a320;
            }
            if (key.Contains("737") || key.Contains("B737"))
            {
                if (_b737 == null) _b737 = BuildNarrow("737", new Color(0.25f, 0.6f, 0.35f));
                return _b737;
            }
            if (key.Contains("787") || key.Contains("B787"))
            {
                if (_b787 == null) _b787 = BuildWide("787", new Color(0.7f, 0.3f, 0.25f));
                return _b787;
            }
            // 默认：窄体通用
            if (_a320 == null) _a320 = BuildNarrow("?", new Color(0.4f, 0.4f, 0.5f));
            return _a320;
        }

        private static Sprite BuildNarrow(string label, Color bodyColor)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color[Size * Size];

            // 清背景
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            // 窄体机：短机身 + 翼尖小翼
            // 机身（水平长条）：y=28-36, x=8-56
            FillRect(pixels, 8, 52, 24, 36, bodyColor);
            // 机头（三角）：x=52-58, y=26-38
            FillRect(pixels, 48, 58, 26, 38, bodyColor * 0.8f);
            FillTriangle(pixels, 56, 32, 64, 32, 60, 28, bodyColor);
            // 垂尾：x=16-22, y=36-50
            FillRect(pixels, 16, 22, 36, 52, bodyColor * 0.7f);
            // 翼尖小翼：机身前后两端各一个小竖线
            FillRect(pixels, 10, 14, 36, 42, bodyColor * 0.5f); // 后翼
            FillRect(pixels, 48, 52, 36, 42, bodyColor * 0.5f); // 前翼
            // 引擎：机身下两个小方块
            FillRect(pixels, 20, 24, 22, 26, new Color(0.5f, 0.5f, 0.55f));
            FillRect(pixels, 40, 44, 22, 26, new Color(0.5f, 0.5f, 0.55f));

            // 机型标签
            WriteLabel(pixels, label);

            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
        }

        private static Sprite BuildWide(string label, Color bodyColor)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color[Size * Size];

            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            // 宽体机：更长机身 + 大翼展
            FillRect(pixels, 6, 56, 20, 36, bodyColor);
            // 机头
            FillRect(pixels, 52, 60, 21, 35, bodyColor * 0.8f);
            FillTriangle(pixels, 58, 30, 64, 30, 60, 26, bodyColor);
            // 垂尾
            FillRect(pixels, 14, 20, 36, 54, bodyColor * 0.7f);
            // 主翼（宽体大翼展）
            FillRect(pixels, 26, 42, 32, 40, bodyColor * 0.55f);
            FillRect(pixels, 26, 42, 28, 34, bodyColor * 0.55f);
            FillRect(pixels, 26, 42, 38, 44, bodyColor * 0.55f);
            // 引擎（双发，较宽）
            FillRect(pixels, 18, 24, 18, 22, new Color(0.5f, 0.5f, 0.55f));
            FillRect(pixels, 38, 44, 18, 22, new Color(0.5f, 0.5f, 0.55f));

            WriteLabel(pixels, label);

            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
        }

        private static void FillRect(Color[] pixels, int x0, int x1, int y0, int y1, Color c)
        {
            x0 = Mathf.Clamp(x0, 0, Size - 1);
            x1 = Mathf.Clamp(x1, x0, Size);
            y0 = Mathf.Clamp(y0, 0, Size - 1);
            y1 = Mathf.Clamp(y1, y0, Size);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    pixels[y * Size + x] = c;
        }

        private static void FillTriangle(Color[] pixels, int x1, int y1, int x2, int y2, int x3, int y3, Color c)
        {
            int minX = Mathf.Clamp(Mathf.Min(x1, x2, x3), 0, Size - 1);
            int maxX = Mathf.Clamp(Mathf.Max(x1, x2, x3), 0, Size - 1);
            int minY = Mathf.Clamp(Mathf.Min(y1, y2, y3), 0, Size - 1);
            int maxY = Mathf.Clamp(Mathf.Max(y1, y2, y3), 0, Size - 1);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (PointInTriangle(x, y, x1, y1, x2, y2, x3, y3))
                        pixels[y * Size + x] = c;
        }

        private static bool PointInTriangle(int px, int py, int ax, int ay, int bx, int by, int cx, int cy)
        {
            float d1 = Sign(px, py, ax, ay, bx, by);
            float d2 = Sign(px, py, bx, by, cx, cy);
            float d3 = Sign(px, py, cx, cy, ax, ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static float Sign(int px, int py, int ax, int ay, int bx, int by)
        {
            return (px - bx) * (ay - by) - (ax - bx) * (py - by);
        }

        private static void WriteLabel(Color[] pixels, string label)
        {
            // 简单的像素文字：在图标右下角写浅色型号
            int baseX = Size - label.Length * 6 - 2;
            int baseY = 4;
            for (int ci = 0; ci < label.Length; ci++)
            {
                char ch = label[ci];
                // 极简像素字：仅画数字的竖线部分
                int cx = baseX + ci * 6;
                if (ch == '3') { HLine(pixels, cx, cx + 3, baseY); HLine(pixels, cx, cx + 3, baseY + 3); HLine(pixels, cx, cx + 3, baseY + 6); VLine(pixels, cx + 3, baseY, baseY + 6); }
                else if (ch == '2') { HLine(pixels, cx, cx + 3, baseY); HLine(pixels, cx, cx + 3, baseY + 3); HLine(pixels, cx, cx + 3, baseY + 6); VLine(pixels, cx + 3, baseY, baseY + 3); VLine(pixels, cx, baseY + 3, baseY + 6); }
                else if (ch == '7') { HLine(pixels, cx, cx + 3, baseY); VLine(pixels, cx + 3, baseY, baseY + 6); }
                else if (ch == 'A') { VLine(pixels, cx, baseY, baseY + 6); VLine(pixels, cx + 3, baseY, baseY + 6); HLine(pixels, cx, cx + 3, baseY); HLine(pixels, cx, cx + 3, baseY + 3); }
                else if (ch == 'B') { VLine(pixels, cx, baseY, baseY + 6); HLine(pixels, cx, cx + 2, baseY); HLine(pixels, cx, cx + 2, baseY + 3); HLine(pixels, cx, cx + 2, baseY + 6); VLine(pixels, cx + 2, baseY, baseY + 3); VLine(pixels, cx + 2, baseY + 3, baseY + 6); }
                else if (ch != ' ') HLine(pixels, cx, cx + 3, baseY + 6);
            }
        }

        private static void HLine(Color[] pixels, int x0, int x1, int y)
        {
            for (int x = x0; x <= x1 && x < Size; x++)
                if (x >= 0 && y >= 0 && y < Size)
                    pixels[y * Size + x] = new Color(0.85f, 0.85f, 0.9f, 0.5f);
        }

        private static void VLine(Color[] pixels, int x, int y0, int y1)
        {
            for (int y = y0; y <= y1 && y < Size; y++)
                if (x >= 0 && x < Size && y >= 0)
                    pixels[y * Size + x] = new Color(0.85f, 0.85f, 0.9f, 0.5f);
        }
    }
}
