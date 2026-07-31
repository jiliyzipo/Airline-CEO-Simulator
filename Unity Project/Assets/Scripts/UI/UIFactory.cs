using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>运行时 UGUI 工厂：圆角精灵、面板、TMP 按钮/文字（中文字体）。</summary>
    public static class UIFactory
    {
        private static Sprite _roundedSprite;
        private static Sprite _circleSprite;

        public struct Anchor
        {
            public Vector2 min, max, offsetMin, offsetMax;
            public Anchor(Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
            {
                this.min = min; this.max = max; offsetMin = offMin; offsetMax = offMax;
            }
        }

        public static Anchor Fill => new Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        public static Sprite RoundedSprite
        {
            get
            {
                if (_roundedSprite == null)
                    _roundedSprite = BuildRoundedSprite(64, 12);
                return _roundedSprite;
            }
        }

        public static Sprite CircleSprite
        {
            get
            {
                if (_circleSprite == null)
                    _circleSprite = BuildRoundedSprite(32, 16);
                return _circleSprite;
            }
        }

        public static void ApplyAnchor(RectTransform rt, Anchor a)
        {
            rt.anchorMin = a.min;
            rt.anchorMax = a.max;
            rt.offsetMin = a.offsetMin;
            rt.offsetMax = a.offsetMax;
            rt.localScale = Vector3.one;
        }

        public static GameObject Panel(Transform parent, string name, Anchor a, Color color, bool rounded = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            ApplyAnchor(go.GetComponent<RectTransform>(), a);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            if (rounded)
            {
                img.sprite = RoundedSprite;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1.2f;
            }
            return go;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string content, int size,
            TextAlignmentOptions align, Anchor a, Color color, FontStyles style = FontStyles.Normal)
        {
            // 中文通常比英文大 2~4px
            int adapted = size + 2;
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            ApplyAnchor(go.GetComponent<RectTransform>(), a);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = ChineseFontProvider.FontAsset;
            text.text = content;
            text.fontSize = adapted;
            text.color = color;
            text.alignment = align;
            text.fontStyle = style;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.lineSpacing = 8f;
            return text;
        }

        public static TMP_InputField Input(Transform parent, string name, string placeholder, Anchor a)
        {
            var go = Panel(parent, name, a, UITheme.PanelDeep);
            var text = Label(go.transform, "Text", "", 16, TextAlignmentOptions.MidlineLeft,
                new Anchor(new Vector2(0.04f, 0), new Vector2(0.96f, 1), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            text.raycastTarget = true;

            var ph = Label(go.transform, "Placeholder", placeholder, 16, TextAlignmentOptions.MidlineLeft,
                new Anchor(new Vector2(0.04f, 0), new Vector2(0.96f, 1), Vector2.zero, Vector2.zero),
                new Color(UITheme.TextMuted.r, UITheme.TextMuted.g, UITheme.TextMuted.b, 0.55f));

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = go.GetComponent<RectTransform>();
            input.textComponent = text;
            input.placeholder = ph;
            input.fontAsset = ChineseFontProvider.FontAsset;
            input.pointSize = 18;
            input.text = placeholder.Contains("…") ? "" : placeholder;
            return input;
        }

        public static Button Button(Transform parent, string name, string label, Anchor a,
            Color color, UnityEngine.Events.UnityAction onClick, int fontSize = 18)
        {
            var go = Panel(parent, name, a, color);
            Label(go.transform, "Label", label, fontSize, TextAlignmentOptions.Center, Fill,
                UITheme.TextPrimary, FontStyles.Bold);
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            btn.targetGraphic = go.GetComponent<Image>();
            btn.onClick.AddListener(onClick);
            go.AddComponent<UIHoverBrighten>();
            return btn;
        }

        public static Outline AddBorder(GameObject go, Color color, Vector2 distance)
        {
            var outline = go.GetComponent<Outline>();
            if (outline == null) outline = go.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
            return outline;
        }

        private static Sprite BuildRoundedSprite(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float r = radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = CornerAlpha(x, y, size, r);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply(false, true);
            return Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        private static float CornerAlpha(int x, int y, int size, float r)
        {
            float dx = 0f, dy = 0f;
            if (x < r && y < r) { dx = r - x; dy = r - y; }
            else if (x >= size - r && y < r) { dx = x - (size - r - 1); dy = r - y; }
            else if (x < r && y >= size - r) { dx = r - x; dy = y - (size - r - 1); }
            else if (x >= size - r && y >= size - r) { dx = x - (size - r - 1); dy = y - (size - r - 1); }
            else return 1f;

            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(r + 0.5f - dist);
        }
    }
}
