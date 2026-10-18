using System;
using AirlineCEO.Logic;
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
        private static Sprite _globeSprite;
        private static Sprite _cloudSprite;

        public struct Anchor
        {
            public Vector2 min, max, offsetMin, offsetMax;
            public Anchor(Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
            {
                this.min = min; this.max = max; offsetMin = offMin; offsetMax = offMax;
            }
        }

        public static Anchor Fill => new Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        /// <summary>独立绘制顺序，避免同级页面后建而盖住常驻控件。</summary>
        public static void EnsureSortingCanvas(GameObject go, int sortingOrder)
        {
            if (go == null) return;
            var canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();
        }

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

        /// <summary>地球图标（Resources/UI/Icons/Globe.png，白底已转透明）。</summary>
        public static Sprite GlobeSprite
        {
            get
            {
                if (_globeSprite == null) _globeSprite = LoadIconSprite("UI/Icons/Globe");
                return _globeSprite;
            }
        }

        /// <summary>云图标（Resources/UI/Icons/Cloud.png，白底已转透明）。</summary>
        public static Sprite CloudSprite
        {
            get
            {
                if (_cloudSprite == null) _cloudSprite = LoadIconSprite("UI/Icons/Cloud");
                return _cloudSprite;
            }
        }

        // ———— 线性图标（程序化栅格化，白色形状 + alpha，靠 Image.color 上色）————

        static Sprite _gearSprite;
        static Sprite _searchSprite;
        static Sprite _chatSprite;
        static Sprite _tailDownSprite;
        static Sprite _clockSprite;
        static readonly Sprite[] _ffSprites = new Sprite[3]; // 1×/2×/3× 快进
        static Sprite _pauseSprite;
        static Sprite _helpSprite;
        static Sprite _playSprite;

        /// <summary>齿轮（设置）。</summary>
        public static Sprite GearSprite => _gearSprite ??= BuildGearSprite(64);

        /// <summary>放大镜（搜索）。</summary>
        public static Sprite SearchSprite => _searchSprite ??= BuildSearchSprite(64);

        /// <summary>对话气泡（顾问）。</summary>
        public static Sprite ChatSprite => _chatSprite ??= BuildChatSprite(64);

        /// <summary>
        /// 对话尾巴：尖端朝下的实心三角，底边贴满上沿。
        /// 贴到面板下沿并把尖端对准气泡图标，形成「从中弹出」的连接。
        /// </summary>
        public static Sprite TailDownSprite => _tailDownSprite ??= BuildTailDownSprite(64);

        /// <summary>时钟（日期时间）。</summary>
        public static Sprite ClockSprite => _clockSprite ??= BuildClockSprite(64);

        /// <summary>速度键：1×/2×/3× 快进箭头（n 个三角 + 数量小标）。</summary>
        public static Sprite FastForwardSprite(int arrows)
        {
            int i = Mathf.Clamp(arrows, 1, 3) - 1;
            return _ffSprites[i] ??= BuildFastForwardSprite(64, i + 1);
        }

        /// <summary>暂停（两条竖线）。</summary>
        public static Sprite PauseSprite => _pauseSprite ??= BuildPauseSprite(64);

        /// <summary>帮助（圆环 + ?，用文字叠加由调用方处理；此处为圆环底）。</summary>
        public static Sprite HelpSprite => _helpSprite ??= BuildHelpSprite(64);

        /// <summary>播放（单三角）。</summary>
        public static Sprite PlaySprite => _playSprite ??= BuildFastForwardSprite(64, 1);

        /// <summary>从 Resources 加载 PNG 并转 Sprite；缺失时回退 CircleSprite 并警告。</summary>
        public static Sprite LoadSprite(string resourcesPath)
        {
            var tex = Resources.Load<Texture2D>(resourcesPath);
            if (tex == null)
            {
                Debug.LogWarning($"[UIFactory] 图标资源缺失: {resourcesPath}（回退程序化圆）");
                return CircleSprite;
            }
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
        }

        static Sprite LoadIconSprite(string resourcesPath) => LoadSprite(resourcesPath);

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
            // 中文通常比英文大 2~4px；再乘无障碍「字体缩放」倍率（GDB 29.5）
            int adapted = Mathf.RoundToInt((size + 2) * Accessibility.FontMultiplier());
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            ApplyAnchor(go.GetComponent<RectTransform>(), a);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = ChineseFontProvider.FontAsset;
            string shown = ChineseFontProvider.Sanitize(content);
            ChineseFontProvider.EnsureCharacters(shown);
            text.text = shown;
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
            // 结构对齐 TMP 默认控件：Root → Text Area(viewport) → Text / Placeholder
            // Caret 在 OnEnable 时挂到 textViewport 下；赋值完成前 Enable 会导致无插入符。
            var go = Panel(parent, name, a, UITheme.PanelDeep);
            var rootImg = go.GetComponent<Image>();
            if (rootImg != null) rootImg.raycastTarget = true;

            var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            textArea.transform.SetParent(go.transform, false);
            ApplyAnchor(textArea.GetComponent<RectTransform>(),
                new Anchor(new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f), Vector2.zero, Vector2.zero));

            var text = Label(textArea.transform, "Text", "", 16, TextAlignmentOptions.MidlineLeft, Fill, UITheme.TextPrimary);
            text.raycastTarget = false; // 点击由根 Image 接收，避免抢焦点
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            text.horizontalAlignment = HorizontalAlignmentOptions.Left;
            text.margin = new Vector4(2f, 1f, 2f, 1f);

            var ph = Label(textArea.transform, "Placeholder", placeholder, 16, TextAlignmentOptions.MidlineLeft, Fill,
                new Color(UITheme.TextMuted.r, UITheme.TextMuted.g, UITheme.TextMuted.b, 0.55f));
            ph.raycastTarget = false;
            ph.textWrappingMode = TextWrappingModes.NoWrap;
            ph.overflowMode = TextOverflowModes.Overflow;
            ph.verticalAlignment = VerticalAlignmentOptions.Middle;
            ph.horizontalAlignment = HorizontalAlignmentOptions.Left;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = textArea.GetComponent<RectTransform>();
            input.textComponent = text;
            input.placeholder = ph;
            input.targetGraphic = rootImg;
            input.fontAsset = ChineseFontProvider.FontAsset;
            input.pointSize = 18;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.onFocusSelectAll = false;
            input.text = placeholder.Contains("…") ? "" : placeholder;

            // 关掉 TMP 自带 Caret 绘制，改由 InputCaretFallback（根节点、白 Sprite）自绘
            input.customCaretColor = true;
            input.caretColor = new Color(1f, 1f, 1f, 0f);
            input.caretWidth = 1;
            input.caretBlinkRate = 0f;
            input.selectionColor = new Color(0.18f, 0.45f, 0.95f, 0.45f);

            input.enabled = false;
            input.enabled = true;

            InputCaretFallback.Attach(input);

            input.onSelect.AddListener(_ =>
            {
                if (input == null || !input.isActiveAndEnabled) return;
                input.ActivateInputField();
                input.caretPosition = input.text != null ? input.text.Length : 0;
            });

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

        /// <summary>图标按钮：圆角面板 + 居中图标 sprite（无文字），点击触发 onClick。</summary>
        public static Button IconButton(Transform parent, string name, Sprite icon, Anchor a,
            Color bg, UnityEngine.Events.UnityAction onClick, Color? iconColor = null)
        {
            var go = Panel(parent, name, a, bg);
            // 图标 Image 铺满按钮（留 8% 内边距）
            var igo = new GameObject("Icon", typeof(RectTransform));
            igo.transform.SetParent(go.transform, false);
            var irt = igo.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.08f, 0.08f);
            irt.anchorMax = new Vector2(0.92f, 0.92f);
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
            var img = igo.AddComponent<Image>();
            img.sprite = icon;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.color = iconColor ?? UITheme.TextPrimary;
            img.raycastTarget = false;

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

        /// <summary>
        /// 圆形角标：挂在父级（图标按钮等）右上角，显示未读数。
        /// count<=0 时隐藏。返回根节点（含名为 Num 的 TMP 子标签）。
        /// </summary>
        public static GameObject Badge(Transform parent, string name, Vector2 offsetPx, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f); // 右上角
            rt.anchoredPosition = offsetPx;
            rt.sizeDelta = new Vector2(22f, 22f);
            var img = go.AddComponent<Image>();
            img.sprite = CircleSprite;
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            var num = Label(go.transform, "Num", "0", 11, TextAlignmentOptions.Center, Fill,
                Color.white, FontStyles.Bold);
            num.transform.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            num.transform.GetComponent<RectTransform>().anchorMax = Vector2.one;
            num.transform.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            num.transform.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            go.SetActive(false);
            return go;
        }

        /// <summary>更新角标数字；n<=0 隐藏。</summary>
        public static void SetBadge(GameObject badge, int n)
        {
            if (badge == null) return;
            badge.SetActive(n > 0);
            var num = badge.transform.Find("Num")?.GetComponent<TextMeshProUGUI>();
            if (num != null) num.text = n > 99 ? "99+" : n.ToString();
        }

        /// <summary>
        /// 可滚动文本区：文字不溢出父级，也不与底部按钮抢位。
        /// 返回 Content 上的 TMP（用于赋值）。
        /// </summary>
        public static TextMeshProUGUI ScrollableLabel(Transform parent, string name, Anchor viewportAnchor,
            Color textColor, int fontSize = 15, TextAlignmentOptions align = TextAlignmentOptions.TopLeft,
            bool showScrollbar = false)
        {
            var host = Panel(parent, name + "_Viewport", viewportAnchor, new Color(0, 0, 0, 0.01f));
            var hostImg = host.GetComponent<Image>();
            if (hostImg != null) hostImg.raycastTarget = true;

            GameObject clipHost = host;
            Scrollbar scrollbar = null;
            if (showScrollbar)
            {
                var inner = Panel(host.transform, "Clip",
                    new Anchor(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-16f, 0f)),
                    new Color(0, 0, 0, 0.01f), rounded: false);
                var innerImg = inner.GetComponent<Image>();
                if (innerImg != null) innerImg.raycastTarget = true;
                clipHost = inner;

                var bar = Panel(host.transform, "Scrollbar",
                    new Anchor(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-14f, 4f), new Vector2(-2f, -4f)),
                    new Color(0.05f, 0.10f, 0.18f, 0.95f), rounded: true);
                var handle = Panel(bar.transform, "Handle", Fill, new Color(0.35f, 0.62f, 0.95f, 0.85f), rounded: true);
                var handleRt = handle.GetComponent<RectTransform>();
                handleRt.offsetMin = new Vector2(2f, 2f);
                handleRt.offsetMax = new Vector2(-2f, -2f);
                scrollbar = bar.AddComponent<Scrollbar>();
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                scrollbar.handleRect = handleRt;
                scrollbar.targetGraphic = handle.GetComponent<Image>();
                scrollbar.transition = Selectable.Transition.None;
            }

            clipHost.AddComponent<RectMask2D>();

            var content = new GameObject(name + "_Content", typeof(RectTransform), typeof(ContentSizeFitter), typeof(TextMeshProUGUI));
            content.transform.SetParent(clipHost.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0f, 0f);
            crt.offsetMin = new Vector2(10f, crt.offsetMin.y);
            crt.offsetMax = new Vector2(-10f, crt.offsetMax.y);

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var text = content.GetComponent<TextMeshProUGUI>();
            text.font = ChineseFontProvider.FontAsset;
            text.fontSize = fontSize + 2;
            text.color = textColor;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow; // 高度由 ContentSizeFitter 撑开，视口裁剪
            text.raycastTarget = true;
            text.lineSpacing = 4f;
            var le = content.AddComponent<LayoutElement>();
            le.minWidth = 100f;

            var scroll = host.AddComponent<ScrollRect>();
            scroll.viewport = clipHost.GetComponent<RectTransform>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            if (scrollbar != null)
            {
                scroll.verticalScrollbar = scrollbar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
                scroll.verticalScrollbarSpacing = 2f;
            }

            return text;
        }

        /// <summary>纵向滚动视口：右侧常驻滚动条。返回 Content（从顶部往下挂子节点）。</summary>
        public static RectTransform VerticalScrollArea(Transform parent, string name, Anchor viewportAnchor)
        {
            var host = Panel(parent, name, viewportAnchor, new Color(0.02f, 0.06f, 0.12f, 0.35f), rounded: false);
            var hostImg = host.GetComponent<Image>();
            if (hostImg != null) hostImg.raycastTarget = false;

            var viewport = Panel(host.transform, "Viewport",
                new Anchor(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-16f, 0f)),
                new Color(0, 0, 0, 0.01f), rounded: false);
            viewport.AddComponent<RectMask2D>();
            var vpImg = viewport.GetComponent<Image>();
            if (vpImg != null) vpImg.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport.transform, false);
            var content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 8f);

            var bar = Panel(host.transform, "Scrollbar",
                new Anchor(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-14f, 4f), new Vector2(-2f, -4f)),
                new Color(0.05f, 0.10f, 0.18f, 0.95f), rounded: true);
            var handle = Panel(bar.transform, "Handle", Fill, new Color(0.35f, 0.62f, 0.95f, 0.85f), rounded: true);
            var handleRt = handle.GetComponent<RectTransform>();
            handleRt.offsetMin = new Vector2(2f, 2f);
            handleRt.offsetMax = new Vector2(-2f, -2f);

            var scrollbar = bar.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.transition = Selectable.Transition.None;

            var scroll = viewport.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 42f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarSpacing = 2f;
            return content;
        }

        public static void PinFromTop(GameObject go, float yFromTop, float height, float xMin = 0.01f, float xMax = 0.97f)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, 1f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, -yFromTop);
        }

        public static void SetScrollContentHeight(RectTransform content, float height)
        {
            if (content == null) return;
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(height, 8f));
        }

        /// <summary>弹窗主操作按钮色：亮蓝，避免灰。</summary>
        public static Color ModalButton => UITheme.Accent;

        /// <summary>Phase 1：按 Resources 路径加载 Prefab；失败返回 null（回退程序化构建）。</summary>
        public static GameObject TryLoadPrefab(string resourcesPath)
        {
            if (string.IsNullOrEmpty(resourcesPath)) return null;
            return Resources.Load<GameObject>(resourcesPath);
        }

        /// <summary>实例化 Prefab 并铺满父级；无资产时返回 null。</summary>
        public static GameObject TryInstantiatePrefab(Transform parent, string resourcesPath, string name = null)
        {
            var prefab = TryLoadPrefab(resourcesPath);
            if (prefab == null) return null;
            var go = UnityEngine.Object.Instantiate(prefab, parent, false);
            if (!string.IsNullOrEmpty(name)) go.name = name;
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
            }
            return go;
        }

        // ———— 线性图标光栅化：以 2×2 超采样画到 RGBA32 白色形状，得到抗锯齿 alpha ————

        /// <summary>创建画布 + 像素填充器。fill(px,py) 返回覆盖度 0~1（白色 + alpha）。</summary>
        static Sprite Rasterize(int size, System.Func<float, float, float> fill)
        {
            const int SS = 2; // 每像素 2×2 超采样
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var cols = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = 0f;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            // 像素中心归一化到 [-0.5, 0.5]（size 单位），再转 [0,1]
                            float px = (x + (sx + 0.5f) / SS) / size;
                            float py = (y + (sy + 0.5f) / SS) / size;
                            a += Mathf.Clamp01(fill(px, py));
                        }
                    a /= SS * SS;
                    cols[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(cols);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>点 → 有符号距离（<0 在线内）。thickness 为半宽（归一化单位）。</summary>
        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float len2 = dx * dx + dy * dy;
            float t = len2 <= 1e-9f ? 0f : Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / len2);
            float cx = ax + dx * t - px, cy = ay + dy * t - py;
            return Mathf.Sqrt(cx * cx + cy * cy);
        }

        /// <summary>线段描边覆盖度（1px 羽化）。</summary>
        static float Stroke(float px, float py, float ax, float ay, float bx, float by, float thickness)
            => Mathf.Clamp01(thickness + 0.008f - SegDist(px, py, ax, ay, bx, by));

        /// <summary>圆环描边覆盖度（cx,cy,r 归一化；thickness 半宽）。</summary>
        static float Ring(float px, float py, float cx, float cy, float r, float thickness)
        {
            float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            return Mathf.Clamp01(thickness + 0.008f - Mathf.Abs(d - r));
        }

        /// <summary>实心圆覆盖度（1px 羽化）。</summary>
        static float Disc(float px, float py, float cx, float cy, float r)
        {
            float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            return Mathf.Clamp01(r + 0.008f - d);
        }

        /// <summary>实心三角形（p1→p2→p3）覆盖度（硬边，靠超采样抗锯齿）。</summary>
        static float Tri(float px, float py, float x1, float y1, float x2, float y2, float x3, float y3)
        {
            float s1 = (x2 - x1) * (py - y1) - (y2 - y1) * (px - x1);
            float s2 = (x3 - x2) * (py - y2) - (y3 - y2) * (px - x2);
            float s3 = (x1 - x3) * (py - y3) - (y1 - y3) * (px - x3);
            bool neg = s1 < 0 || s2 < 0 || s3 < 0;
            bool pos = s1 > 0 || s2 > 0 || s3 > 0;
            return !(neg && pos) ? 1f : 0f;
        }

        static Sprite BuildGearSprite(int size)
        {
            const int teeth = 8;
            return Rasterize(size, (px, py) =>
            {
                float dx = px - 0.5f, dy = py - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);

                // 齿：半径方波（8 齿 = 4 个 cos 周期）
                float wave = Mathf.Abs(Mathf.Cos(ang * (teeth / 2)));
                float rOuter = 0.46f, rInner = 0.36f;
                float rTooth = wave > 0.55f ? rOuter : rInner;
                float body = Mathf.Clamp01(rTooth + 0.008f - d);

                // 中心孔挖空 + 加一圈内环描边
                float hole = Mathf.Clamp01(0.14f + 0.008f - d);
                float hub = Ring(px, py, 0.5f, 0.5f, 0.14f, 0.035f);
                return Mathf.Max(body - hole, hub);
            });
        }

        static Sprite BuildSearchSprite(int size)
            => Rasterize(size, (px, py) =>
            {
                float ring = Ring(px, py, 0.44f, 0.56f, 0.24f, 0.05f);
                float handle = Stroke(px, py, 0.61f, 0.39f, 0.82f, 0.18f, 0.055f);
                return Mathf.Max(ring, handle);
            });

        static Sprite BuildChatSprite(int size)
            => Rasterize(size, (px, py) =>
            {
                // 实心圆角气泡 + 左下尾巴（经典对话气泡）
                float x0 = 0.08f, x1 = 0.92f, y0 = 0.34f, y1 = 0.88f, r = 0.14f;
                float qx = Mathf.Max(x0 + r - px, px - (x1 - r), 0f);
                float qy = Mathf.Max(y0 + r - py, py - (y1 - r), 0f);
                float dist = Mathf.Sqrt(qx * qx + qy * qy);
                bool insideBox = px >= x0 && px <= x1 && py >= y0 && py <= y1;
                float body = (insideBox && dist <= r + 0.008f) ? 1f : 0f;
                float tail = Tri(px, py, 0.20f, 0.38f, 0.34f, 0.38f, 0.22f, 0.12f);
                return Mathf.Max(body, tail);
            });

        /// <summary>
        /// 对话尾巴（尖端朝下）：顶点在下边中点，底边贴满上沿。
        /// 上沿与面板下沿等宽相接、顶点指向气泡图标，构成「气泡从中弹出」的连接。
        /// </summary>
        static Sprite BuildTailDownSprite(int size)
            => Rasterize(size, (px, py) =>
                // 顶点略高于 0 并留出上沿满宽，避免拉伸到非等比矩形时尖端被啃掉
                Tri(px, py, 0.5f, 0.04f, 0.03f, 1f, 0.97f, 1f));

        static Sprite BuildClockSprite(int size)            => Rasterize(size, (px, py) =>
            {
                float ring = Ring(px, py, 0.5f, 0.5f, 0.38f, 0.05f);
                float hand1 = Stroke(px, py, 0.5f, 0.5f, 0.5f, 0.76f, 0.045f); // 分针（上）
                float hand2 = Stroke(px, py, 0.5f, 0.5f, 0.70f, 0.50f, 0.045f); // 时针（右）
                return Mathf.Max(ring, Mathf.Max(hand1, hand2));
            });

        static Sprite BuildFastForwardSprite(int size, int arrows)
            => Rasterize(size, (px, py) =>
            {
                float a = 0f;
                float w = 0.26f;
                float startX = 0.5f - arrows * w * 0.5f;
                for (int i = 0; i < arrows; i++)
                {
                    float x0 = startX + i * w;
                    a = Mathf.Max(a, Tri(px, py, x0, 0.22f, x0 + w * 0.9f, 0.5f, x0, 0.78f));
                }
                return a;
            });

        static Sprite BuildPauseSprite(int size)
            => Rasterize(size, (px, py) =>
                Mathf.Max(
                    Mathf.Clamp01(0.045f + 0.008f - Mathf.Abs(px - 0.38f)),
                    Mathf.Clamp01(0.045f + 0.008f - Mathf.Abs(px - 0.62f))));

        static Sprite BuildHelpSprite(int size)
            => Rasterize(size, (px, py) =>
            {
                // ? 上部：3/4 圆弧（开口朝下）
                float dx = px - 0.5f, dy = py - 0.60f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dx, -dy); // 0=正下，顺时针
                float arc = Mathf.Clamp01(0.05f + 0.008f - Mathf.Abs(d - 0.16f));
                bool inGap = ang > -0.6f && ang < 0.6f && d < 0.20f; // 底部开口
                // 中部竖钩
                float hook = Stroke(px, py, 0.5f, 0.44f, 0.5f, 0.32f, 0.05f);
                // 底部点
                float dot = Disc(px, py, 0.5f, 0.14f, 0.05f);
                return Mathf.Max(inGap ? 0f : arc, Mathf.Max(hook, dot));
            });

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

        // ———— 图标 sprite：外置 PNG（Resources/UI/Icons/），白色形状 + alpha，靠 Image.color 上色 ————

        // ———— 地图样式缩略图：外置 PNG（Resources/UI/Icons/MapDefault.png / MapSatellite.png）————

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
