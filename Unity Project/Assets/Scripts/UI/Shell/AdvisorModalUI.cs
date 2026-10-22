using System.Collections;
using System.Collections.Generic;
using AirlineCEO.AI;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// FleetUI 在 AirlineCEO.UI 根命名空间
using FleetUIType = AirlineCEO.UI.FleetUI;

namespace AirlineCEO.UI.Shell
{
    /// <summary>
    /// 顶栏「顾问」弹窗 = 开局引导线宿主（S0–S6，《航空顾问开局引导线》v1.1）。
    /// </summary>
    public class AdvisorModalUI : MonoBehaviour
    {
        // ── 底部停靠几何（画布参考分辨率 1920×1080，单位=设计像素） ──
        //
        // 收起态 = 右下角气泡图标本身；展开态 = 同一块矩形向左上伸展成面板。
        // 四条边都插值，所以视觉上是「图标长成弹窗 / 弹窗缩回图标」，不是旁边另开一扇窗。
        const float PanelWidth = 1100f;
        const float PanelHeight = 815f;
        const float IconSize = 52f;
        const float DefaultIconRight = 10f;
        // 停在「a 读数 + c 命令台」那条的正上方：84（底栏上缘）+ 42（那条高）+ 8 间隙。
        // 落在这条之上而不是压在条里 —— 否则图标正好盖住命令台右端，点不到输入框。
        const float DefaultIconBottom = 84f + BottomDockView.BarHeight + 8f;
        /// <summary>停靠下缘不得低于这条带：老存档里存过 86 的用户，读档时也要被抬上来。</summary>
        const float MinIconBottom = 84f + BottomDockView.BarHeight + 4f;
        const float DockMargin = 8f;
        const float DragClickSlop = 10f;
        const string PrefRight = "ACEO_AdvisorIconRight";
        const string PrefBottom = "ACEO_AdvisorIconBottom";
        const float OpenAnimSeconds = 0.22f;
        const float CloseAnimSeconds = 0.16f;

        GameObject _panel;
        CanvasGroup _contentGroup;
        CanvasGroup _glyphGroup;
        Button _iconHit;
        bool _open;
        bool _dockVisible = true;
        float _iconPadRight = DefaultIconRight;
        float _iconPadBottom = DefaultIconBottom;
        bool _dockDragging;
        bool _dockMoved;
        Vector2 _dockPressScreen;
        float _revealK;
        Coroutine _revealAnim;
        static readonly Color IconBg = new Color(0.06f, 0.16f, 0.30f, 0.92f);
        TextMeshProUGUI _title;
        TextMeshProUGUI _body;
        TextMeshProUGUI _extra;
        // ScrollableLabel 返回的是 Content 上的文本，外层视口（带拦截点击的底图）要单独握着才好整体隐藏
        GameObject _bodyHost;
        GameObject _extraHost;
        Transform _btnRow;
        Button _primary;
        Button _secondary;
        Button _tertiary;
        TextMeshProUGUI _primaryLabel;
        TextMeshProUGUI _secondaryLabel;
        TextMeshProUGUI _tertiaryLabel;
        Coroutine _flightAnim;

        // 排班编辑器（S4）
        GameObject _schedEditor;
        TextMeshProUGUI _schedPreview;
        int _freqIdx;
        int _slotIdx;
        readonly List<Button> _freqBtns = new List<Button>();
        readonly List<Button> _slotBtns = new List<Button>();

        /// <summary>面板是否处于展开态（动画途中即为目标态，避免开合连点错乱）。</summary>
        public bool IsOpen => _open;

        public static AdvisorModalUI Create(Transform canvasParent)
        {
            var host = new GameObject("AdvisorModal", typeof(RectTransform));
            host.transform.SetParent(canvasParent, false);
            UIFactory.ApplyAnchor(host.GetComponent<RectTransform>(), UIFactory.Fill);
            var ui = host.AddComponent<AdvisorModalUI>();
            ui.Build();
            // 首帧必须已收起：走 HideImmediate 而不是 Hide()，否则会看到一次全高面板的闪现
            ui.HideImmediate();
            return ui;
        }

        /// <summary>收起态：气泡图标本身的矩形（右下锚点，位置可拖）。</summary>
        UIFactory.Anchor IconAnchor()
            => new UIFactory.Anchor(new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-_iconPadRight - IconSize, _iconPadBottom),
                new Vector2(-_iconPadRight, _iconPadBottom + IconSize));

        /// <summary>
        /// 展开态：以当前气泡为角点，朝空档更大的一侧长出面板（左下气泡向右上，右下气泡向左上）。
        /// 只有会出画布时才整体平移，不再锁回右下旧位。
        /// </summary>
        UIFactory.Anchor OpenAnchor()
        {
            float w = HostWidth();
            float h = HostHeight();
            float iconRight = -_iconPadRight;
            float iconLeft = iconRight - IconSize;
            bool growRight = _iconPadRight >= w - _iconPadRight - IconSize;

            float xMin, xMax;
            if (growRight)
            {
                xMin = iconLeft;
                xMax = xMin + PanelWidth;
            }
            else
            {
                xMax = iconRight;
                xMin = xMax - PanelWidth;
            }

            float minX = -w + DockMargin;
            float maxX = -DockMargin;
            if (xMax > maxX) { float d = xMax - maxX; xMax -= d; xMin -= d; }
            if (xMin < minX) { float d = minX - xMin; xMin += d; xMax += d; }

            float yMin = _iconPadBottom;
            float yMax = yMin + PanelHeight;
            if (yMax > h - DockMargin) { float d = yMax - (h - DockMargin); yMax -= d; yMin -= d; }
            if (yMin < DockMargin) { float d = DockMargin - yMin; yMin += d; yMax += d; }

            return new UIFactory.Anchor(new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(xMin, yMin), new Vector2(xMax, yMax));
        }

        static UIFactory.Anchor LerpAnchor(UIFactory.Anchor a, UIFactory.Anchor b, float t)
            => new UIFactory.Anchor(
                Vector2.Lerp(a.min, b.min, t),
                Vector2.Lerp(a.max, b.max, t),
                Vector2.Lerp(a.offsetMin, b.offsetMin, t),
                Vector2.Lerp(a.offsetMax, b.offsetMax, t));

        void Build()
        {
            // 同一块面板：收起时就是图标，展开时就是弹窗。RectMask2D 裁掉伸展途中溢出的正文。
            _panel = UIFactory.Panel(transform, "Panel", IconAnchor(), IconBg);
            UIFactory.EnsureSortingCanvas(_panel, UITheme.SortAdvisor);
            UIFactory.AddBorder(_panel, UITheme.Border, new Vector2(1.5f, -1.5f));
            _panel.AddComponent<RectMask2D>();

            // 收起时可点整块打开；展开后关掉，避免挡住正文点击
            _iconHit = _panel.AddComponent<Button>();
            var hitColors = _iconHit.colors;
            hitColors.normalColor = Color.white;
            hitColors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            hitColors.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            hitColors.fadeDuration = 0.08f;
            _iconHit.colors = hitColors;
            _iconHit.targetGraphic = _panel.GetComponent<Image>();
            _iconHit.onClick.AddListener(() =>
            {
                if (_open || _dockMoved) return;
                Show();
            });
            var grip = _panel.AddComponent<AdvisorDockGrip>();
            grip.Bind(this);

            LoadDockPrefs();

            var glyph = new GameObject("IconGlyph", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            glyph.transform.SetParent(_panel.transform, false);
            UIFactory.ApplyAnchor(glyph.GetComponent<RectTransform>(), UIFactory.Fill);
            var gImg = glyph.GetComponent<Image>();
            gImg.sprite = UIFactory.ChatSprite;
            gImg.type = Image.Type.Simple;
            gImg.preserveAspect = true;
            gImg.color = new Color(0.72f, 0.92f, 1f, 1f);
            gImg.raycastTarget = false;
            _glyphGroup = glyph.GetComponent<CanvasGroup>();
            _glyphGroup.blocksRaycasts = false;

            var content = UIFactory.Panel(_panel.transform, "Content", UIFactory.Fill, new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;
            _contentGroup = content.AddComponent<CanvasGroup>();
            var contentTf = content.transform;

            _title = UIFactory.Label(contentTf, "Title", AdvisorTutorialScript.AdvisorTitle, 20,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(24f, -56f), new Vector2(-24f, -16f)),
                UITheme.TextPrimary, FontStyles.Bold);

            // 顾问头像：固定 104×104 正方形（原来用归一化锚点，面板变窄后会变成竖长椭圆）
            var avatar = UIFactory.Panel(contentTf, "Avatar",
                new UIFactory.Anchor(new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(24f, -168f), new Vector2(128f, -64f)),
                UITheme.Accent);
            var avImg = avatar.GetComponent<Image>();
            avImg.sprite = UIFactory.CircleSprite;
            avImg.type = Image.Type.Simple;
            UIFactory.Label(avatar.transform, "Glyph", "航", 28, TextAlignmentOptions.Center,
                UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);

            // 正文窗口 = 标题以下、按钮以上的整块（设计稿黄框）。
            // Extra 贴按钮上方做情报/纪要条；Body 吃掉中间全部高度，不再在半空截断。
            const float extraH = 160f;
            const float extraBottom = 88f;   // 按钮顶 72 + 16 间距
            const float bodyBottom = extraBottom + extraH + 12f;

            _body = UIFactory.ScrollableLabel(contentTf, "Body",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(148f, bodyBottom), new Vector2(-24f, -64f)),
                UITheme.TextPrimary, 15);
            _bodyHost = contentTf.Find("Body_Viewport")?.gameObject;

            _extra = UIFactory.ScrollableLabel(contentTf, "Extra",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(24f, extraBottom), new Vector2(-24f, extraBottom + extraH)),
                UITheme.TextMuted, 14);
            _extraHost = contentTf.Find("Extra_Viewport")?.gameObject;

            _btnRow = UIFactory.Panel(contentTf, "BtnRow",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(24f, 16f), new Vector2(-24f, 72f)),
                new Color(0, 0, 0, 0), rounded: false).transform;
            _btnRow.GetComponent<Image>().raycastTarget = false;

            _primary = UIFactory.Button(_btnRow, "Primary", "继续",
                new UIFactory.Anchor(new Vector2(0.62f, 0.05f), new Vector2(0.99f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => { }, 15);
            _secondary = UIFactory.Button(_btnRow, "Secondary", "稍后再说",
                new UIFactory.Anchor(new Vector2(0.32f, 0.05f), new Vector2(0.60f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);
            _tertiary = UIFactory.Button(_btnRow, "Tertiary", "",
                new UIFactory.Anchor(new Vector2(0.01f, 0.05f), new Vector2(0.30f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.Danger, () => { }, 14);
            _primaryLabel = _primary.GetComponentInChildren<TextMeshProUGUI>();
            _secondaryLabel = _secondary.GetComponentInChildren<TextMeshProUGUI>();
            _tertiaryLabel = _tertiary.GetComponentInChildren<TextMeshProUGUI>();
            _tertiary.gameObject.SetActive(false);

            BuildScheduleEditor();
            ApplyReveal(0f);
        }

        float HostWidth()
        {
            var rt = transform as RectTransform;
            return rt != null && rt.rect.width > 1f ? rt.rect.width : 1920f;
        }

        float HostHeight()
        {
            var rt = transform as RectTransform;
            return rt != null && rt.rect.height > 1f ? rt.rect.height : 1080f;
        }

        void LoadDockPrefs()
        {
            _iconPadRight = PlayerPrefs.GetFloat(PrefRight, DefaultIconRight);
            _iconPadBottom = PlayerPrefs.GetFloat(PrefBottom, DefaultIconBottom);
            ClampDock();
        }

        void SaveDockPrefs()
        {
            PlayerPrefs.SetFloat(PrefRight, _iconPadRight);
            PlayerPrefs.SetFloat(PrefBottom, _iconPadBottom);
            PlayerPrefs.Save();
        }

        void ClampDock()
        {
            float w = HostWidth();
            float h = HostHeight();
            _iconPadRight = Mathf.Clamp(_iconPadRight, DockMargin, Mathf.Max(DockMargin, w - DockMargin - IconSize));
            // 下缘不低于 a+c 那条 —— 老存档存过 86（旧状态栏高度），读进来会正好压在命令台上
            _iconPadBottom = Mathf.Clamp(_iconPadBottom, MinIconBottom, Mathf.Max(MinIconBottom, h - DockMargin - IconSize));
        }

        internal bool CanDockDrag() => _dockVisible && !_open && _revealK < 0.08f;

        internal void OnDockPointerDown(PointerEventData e)
        {
            _dockDragging = false;
            _dockMoved = false;
            _dockPressScreen = e.position;
        }

        internal void OnDockDrag(PointerEventData e)
        {
            if (!CanDockDrag()) return;
            if ((e.position - _dockPressScreen).sqrMagnitude >= DragClickSlop * DragClickSlop)
                _dockMoved = true;
            if (!_dockMoved) return;
            _dockDragging = true;
            MoveDockTo(e);
        }

        internal void OnDockPointerUp()
        {
            if (_dockDragging)
                SaveDockPrefs();
            _dockDragging = false;
        }

        void MoveDockTo(PointerEventData e)
        {
            var host = transform as RectTransform;
            if (host == null) return;
            Camera cam = null;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera != null ? canvas.worldCamera : e.pressEventCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(host, e.position, cam, out var local))
                return;
            var r = host.rect;
            float cx = Mathf.Clamp(local.x - r.xMin, DockMargin + IconSize * 0.5f, r.width - DockMargin - IconSize * 0.5f);
            float cy = Mathf.Clamp(local.y - r.yMin, DockMargin + IconSize * 0.5f, r.height - DockMargin - IconSize * 0.5f);
            _iconPadRight = r.width - (cx + IconSize * 0.5f);
            _iconPadBottom = cy - IconSize * 0.5f;
            ClampDock();
            ApplyReveal(0f);
        }

        sealed class AdvisorDockGrip : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
        {
            AdvisorModalUI _owner;

            public void Bind(AdvisorModalUI owner) => _owner = owner;

            public void OnPointerDown(PointerEventData e) => _owner?.OnDockPointerDown(e);
            public void OnBeginDrag(PointerEventData e) => _owner?.OnDockDrag(e);
            public void OnDrag(PointerEventData e) => _owner?.OnDockDrag(e);
            public void OnEndDrag(PointerEventData e) => _owner?.OnDockPointerUp();
            public void OnPointerUp(PointerEventData e) => _owner?.OnDockPointerUp();
        }

        public void Show()
        {
            if (!_dockVisible) return;
            bool wasOpen = _open;
            gameObject.SetActive(true);
            if (_panel != null) _panel.SetActive(true);
            if (_schedEditor != null) _schedEditor.SetActive(false);
            SetContentVisible(true);
            transform.SetAsLastSibling();
            RefreshStep();
            _open = true;
            if (wasOpen)
            {
                AbortReveal();
                ApplyReveal(1f);
            }
            else StartReveal(1f);
        }

        public void Hide()
        {
            if (!_open) return;
            _open = false;
            if (_panel != null) _panel.SetActive(true);
            StartReveal(0f);
        }

        /// <summary>立刻收成图标（不播动画）：开局流、进人事页等需要马上让位时用。</summary>
        public void HideImmediate()
        {
            _open = false;
            AbortReveal();
            ApplyReveal(0f);
            if (_panel != null) _panel.SetActive(_dockVisible);
        }

        /// <summary>
        /// 停靠区整体可用性。开局向导期间关掉，避免浮在向导上；
        /// 向导结束后由 MVPBootstrap 打开。关掉时一并收成图标。
        /// </summary>
        public void SetDockVisible(bool visible)
        {
            _dockVisible = visible;
            if (!visible)
            {
                HideImmediate();
                if (_panel != null) _panel.SetActive(false);
            }
            else
            {
                if (_panel != null) _panel.SetActive(true);
                if (!_open) ApplyReveal(0f);
            }
        }

        /// <summary>
        /// 把停靠区提到同级最前。
        /// 页面内容都挂在 Phase1_ContentHost 下，而那个宿主是 PrefabNavIntegration.Attach()
        /// 才建的（晚于本停靠区），同级顺序上会盖住常驻的气泡图标；
        /// 原实现只在 Show() 里提一次，那时面板是全屏、无所谓，现在图标常驻就必须显式提。
        /// </summary>
        public void BringToFront()
        {
            transform.SetAsLastSibling();
            if (_panel != null) _panel.transform.SetAsLastSibling();
        }

        void AbortReveal()
        {
            if (_revealAnim == null) return;
            StopCoroutine(_revealAnim);
            _revealAnim = null;
        }

        /// <summary>
        /// k=0 图标尺寸 + 气泡图案；k=1 全局面板 + 正文。
        /// 矩形四边同时插值，气泡图案淡出、正文淡入，形成「图标伸展成弹窗」。
        /// </summary>
        void ApplyReveal(float k)
        {
            float kk = Mathf.Clamp01(k);
            _revealK = kk;
            if (_panel != null)
            {
                UIFactory.ApplyAnchor(_panel.GetComponent<RectTransform>(), LerpAnchor(IconAnchor(), OpenAnchor(), kk));
                var img = _panel.GetComponent<Image>();
                if (img != null) img.color = Color.Lerp(IconBg, UITheme.Panel, kk);
            }

            if (_glyphGroup != null)
                _glyphGroup.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.42f, kk));

            if (_contentGroup != null)
            {
                float ca = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 1f, kk));
                _contentGroup.alpha = ca;
                _contentGroup.interactable = kk > 0.95f;
                _contentGroup.blocksRaycasts = kk > 0.95f;
            }

            if (_iconHit != null)
                _iconHit.interactable = kk < 0.08f && _dockVisible && !_open;
        }

        void StartReveal(float target)
        {
            AbortReveal();
            float from = _revealK;
            float span = Mathf.Abs(target - from);
            if (span < 0.001f)
            {
                ApplyReveal(target);
                return;
            }
            float dur = (target >= from ? OpenAnimSeconds : CloseAnimSeconds) * Mathf.Max(0.35f, span);
            _revealAnim = StartCoroutine(RevealRoutine(from, target, dur));
        }

        IEnumerator RevealRoutine(float from, float target, float dur)
        {
            float t = 0f;
            ApplyReveal(from);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                // 展开 ease-out（快出慢收），收拢 ease-in
                float e = target >= 1f ? 1f - (1f - k) * (1f - k) : k * k;
                ApplyReveal(Mathf.LerpUnclamped(from, target, e));
                yield return null;
            }
            ApplyReveal(target);
            _revealAnim = null;
        }

        void AbortFlightAnim()
        {
            if (_flightAnim == null) return;
            StopCoroutine(_flightAnim);
            _flightAnim = null;
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                AbortFlightAnim();
                Hide();
            }
            else Show();
        }

        /// <summary>开局完成后延迟唤起（默认约 0.5 秒，避免空等太久）。</summary>
        public void TryAutoStartAfterDelay(float seconds = 0.5f)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            if (gs.takeoverMode && !gs.takeoverBriefingShown)
            {
                StartCoroutine(AutoOpenTakeoverRoutine(seconds));
                return;
            }
            if (gs.advisorTutorialCompleted) return;
            if (gs.advisorTutorialDeferred) return;
            StartCoroutine(AutoOpenRoutine(seconds));
        }

        IEnumerator AutoOpenTakeoverRoutine(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            var gs = GameState.Instance;
            if (gs == null || gs.takeoverBriefingShown) yield break;
            // 延时期间玩家可能已经「退出到开始界面」并被回滚成出厂状态：
            // 这时 takeoverMode 已经为 false，再弹就会在开始界面上盖一层局内引导。
            if (!AirlineCEO.UI.Setup.SetupFlowController.InGameplaySession) yield break;
            if (!IsOpen) Show();
        }

        IEnumerator AutoOpenRoutine(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            var gs = GameState.Instance;
            if (gs == null || gs.advisorTutorialCompleted || gs.advisorTutorialDeferred) yield break;
            if (!AirlineCEO.UI.Setup.SetupFlowController.InGameplaySession) yield break;
            if (!IsOpen) Show();
        }

        AdvisorTutorialStep CurrentStep()
        {
            var gs = GameState.Instance;
            int s = gs != null ? gs.advisorTutorialStep : 0;
            if (gs != null && gs.advisorTutorialCompleted) return AdvisorTutorialStep.Done;
            return (AdvisorTutorialStep)Mathf.Clamp(s, 0, (int)AdvisorTutorialStep.Done);
        }

        void SetStep(AdvisorTutorialStep step)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            gs.advisorTutorialStep = (int)step;
            gs.advisorTutorialDeferred = false;
            if (step == AdvisorTutorialStep.Done)
            {
                gs.advisorTutorialCompleted = true;
                gs.advisorTutorialAwaitPage = AdvisorTutorialScript.AwaitNone;
            }
            gs.NotifyChanged();
            SaveManager.Instance?.Save();
        }

        void RefreshStep()
        {
            var gs = GameState.Instance;
            if (gs != null && gs.takeoverMode && !gs.takeoverBriefingShown)
            {
                ShowTakeoverBriefing();
                return;
            }
            if (gs != null && gs.takeoverMode && gs.advisorTutorialCompleted)
            {
                ShowTakeoverStandby();
                return;
            }
            if (gs != null && !gs.advisorTutorialCompleted)
            {
                if (gs.advisorTutorialAwaitPage == AdvisorTutorialScript.AwaitFleet)
                {
                    ShowAwaitFleetHint();
                    return;
                }
            }

            var step = CurrentStep();
            if (step == AdvisorTutorialStep.Done)
            {
                ShowStandby();
                return;
            }

            switch (step)
            {
                case AdvisorTutorialStep.S0_Opportunity: ShowS0(); break;
                case AdvisorTutorialStep.S1_RpmCard: ShowS1(); break;
                case AdvisorTutorialStep.S2_Meeting: ShowS2(); break;
                case AdvisorTutorialStep.S3_FleetGuide: ShowS3(); break;
                case AdvisorTutorialStep.S4_Schedule: ShowS4(); break;
                case AdvisorTutorialStep.S5_FlightResult: ShowS5(); break;
                case AdvisorTutorialStep.S6_NextHook: ShowS6(); break;
            }
        }

        void ShowTakeoverBriefing()
        {
            var gs = GameState.Instance;
            _title.text = AdvisorTutorialScript.AdvisorTitle + " · 接管简报";
            if (_body != null)
                _body.text = "您已签署接管协议。航司正在运营，收入与支出即刻发生。\n\n" + LegacyIssueManager.AdvisorText(gs);
            if (_extra != null) _extra.text = "遗留问题按紧急程度排列，处理后顾问会提示下一项。";
            WireTakeoverActions();
        }

        void ShowTakeoverStandby()
        {
            var next = LegacyIssueManager.Next(GameState.Instance);
            _title.text = AdvisorTutorialScript.AdvisorTitle + " · 待命";
            if (_body != null)
                _body.text = next == null
                    ? "遗留问题已处理完毕。各部门请示仍会发到收件箱。"
                    : LegacyIssueManager.AdvisorText(GameState.Instance);
            WireTakeoverActions();
        }

        void WireTakeoverActions()
        {
            var gs = GameState.Instance;
            var next = LegacyIssueManager.Next(gs);
            if (next != null && !string.IsNullOrEmpty(next.actionLink))
            {
                ConfigureButtons(
                    "前往处理", () =>
                    {
                        if (gs != null) gs.takeoverBriefingShown = true;
                        Hide();
                        var nav = UnityEngine.Object.FindFirstObjectByType<AirlineCEO.UI.PrefabNavIntegration>();
                        nav?.OpenGameplayPage(next.actionLink);
                    },
                    "稍后", () =>
                    {
                        if (gs != null) gs.takeoverBriefingShown = true;
                        Hide();
                    },
                    null, null);
            }
            else
            {
                ConfigureButtons("知道了", () =>
                {
                    if (gs != null) gs.takeoverBriefingShown = true;
                    Hide();
                }, null, null, null, null);
            }
        }

        void ShowStandby()
        {
            _title.text = AdvisorTutorialScript.AdvisorTitle + " · 待命";
            _body.text = AdvisorTutorialScript.Fmt(
                "航空顾问：\n\n“[玩家姓名]，我在这里待命。需要航线评估或决策建议时，随时点顶栏「顾问」。”");
            _extra.text = "引导已完成。可自由探索底栏各部门。";
            ConfigureButtons("关闭", () => Hide(), null, null, null, null);
        }

        void ShowS0()
        {
            _title.text = AdvisorTutorialScript.AdvisorTitle + " · 市场机会";
            _body.text = AdvisorTutorialScript.Fmt(
                "航空顾问：\n\n" +
                "“[玩家姓名]，您好。我是您的航空顾问，负责在您运营过程中提供信息分析和决策建议。”\n\n" +
                "“刚才市场部送来一份紧急情报——深圳至北京航线近期需求激增，日均 O&D 客流超过 2.5 万人次" +
                "（O&D 就是‘起点—终点’直飞客流，指每天从深圳出发、直达北京的旅客数量）。" +
                "这条线是目前国内最赚钱的商务干线之一，竞对客座率已达到 89%。”\n\n" +
                "“我初步测算了一下，如果我们现在切入，预估日利润在 15～18 万元之间，投资回收期约 8～10 个月。”\n\n" +
                "“您要看一下详细评估吗？”");
            _extra.text = "情报来源：市场部 · 航线：深圳 → 北京";
            ConfigureButtons(
                "查看详细评估", () => GoToStep(AdvisorTutorialStep.S1_RpmCard),
                "稍后再说", DeferOrHide,
                null, null);
        }

        void ShowS1()
        {
            _title.text = AdvisorTutorialScript.AdvisorTitle + " · RPM 评估";
            _body.text = AdvisorTutorialScript.Fmt(
                "航空顾问：\n\n" +
                "“这是 RPM 系统给出的综合评估。——RPM 的全称是『航线盈利预测模型』，简单说，就是系统综合了客流、成本、竞争等因素后，" +
                "自动算出一条航线值不值得开的评分，您把它理解成『开线推荐指数』就行。”\n\n" +
                $"“{AdvisorTutorialScript.RouteLabel}”\n\n" +
                "“需求热度：高（日均客流 2.5 万，旺季系数 1.3）\n" +
                "成本压力：中等（航程 2.5 小时，燃油成本可控）\n" +
                "准入壁垒：中等（国航、南航已运营，但仍有时刻窗口）”\n\n" +
                "“财务部、运营部、市场部已经初步交换过意见。要不要召集一个简短的决策会议，听听各部门的具体看法？”");
            _extra.text =
                "【RPM 评估卡】\n" +
                "综合评分　86 分（绿色，强烈推荐）\n" +
                "预估日利润　16.2 万元\n" +
                "预估首年客座率　81%\n\n" +
                "下一步：召开决策会议，汇总各部门意见。";
            ConfigureButtons(
                "发起决策会议", () =>
                {
                    LeaveMeetingMailRecord();
                    GoToStep(AdvisorTutorialStep.S2_Meeting);
                },
                "暂缓", DeferOrHide,
                null, null);
        }

        void ShowS2()
        {
            _title.text = "决策会议 · 深圳—北京";
            _body.text = AdvisorTutorialScript.Fmt(
                "发件人：战略发展部\n主题：【紧急提案】关于开通深圳—北京首都航线的建议\n\n" +
                "航空顾问：\n“[玩家姓名]，各部门已经提交了意见。财务部、运营部、市场部都来了，我给您快速过一遍——”");
            var cash = GameState.Instance != null ? GameState.Instance.cash : 500f;
            _extra.text =
                "财务部 · 审慎\n" +
                $"“当前现金储备 {cash:F0} 万，开航需 300 万启动资金。占比较高，建议审慎评估回本周期。”\n" +
                "启动资金 300 万 / 回本周期约 8 个月\n\n" +
                "运营部 · 支持\n" +
                "“现有机队有闲置窗口，可覆盖该航线时刻，无需新增运力。”\n\n" +
                "市场部 · 强烈支持\n" +
                "“预估首年客座率 81%，日利润 16.2 万，建议立即切入。”\n\n" +
                AdvisorTutorialScript.Fmt(
                    "航空顾问总结：运营和市场强烈支持，财务建议审慎。最终决定权在您手上，[玩家姓名]。");

            bool canApprove = GameState.Instance == null || GameState.Instance.cash >= ActionExecutor.OpenRouteStartupCost;
            ConfigureButtons(
                canApprove ? "批准开航" : "现金不足",
                canApprove ? (System.Action)(() => GoToStep(AdvisorTutorialStep.S3_FleetGuide)) : null,
                "暂缓", DeferOrHide,
                "拒绝", () =>
                {
                    SetStep(AdvisorTutorialStep.Done);
                    Hide();
                });
            if (!canApprove && _primary != null)
                _primary.interactable = false;
        }

        void ShowS3()
        {
            _title.text = AdvisorTutorialScript.AdvisorTitle + " · 分配飞机";
            string ac = AdvisorTutorialScript.RecommendedAircraftLabel();
            _body.text = AdvisorTutorialScript.Fmt(
                "航空顾问：\n\n" +
                "“决策已生效，[玩家姓名]。航线已进入待执行列表。”\n\n" +
                $"“下一步需要为该航线分配一架飞机。我推荐使用闲置的 {ac}，它目前有足够的日利用率窗口。”\n\n" +
                "“请前往【机队】页面，点击推荐飞机的「分配至深圳—北京」后再确认排班。”");
            _extra.text = "将跳转到底栏「机队」；请在机队页完成分配操作。";
            ConfigureButtons(
                "去机队", () =>
                {
                    BeginAwaitPageAction(AdvisorTutorialScript.AwaitFleet, "n9_机队");
                },
                null, null, null, null);
        }

        void ShowAwaitFleetHint()
        {
            string ac = AdvisorTutorialScript.RecommendedAircraftLabel();
            _title.text = AdvisorTutorialScript.AdvisorTitle + " · 机队";
            _body.text = AdvisorTutorialScript.Fmt(
                "航空顾问：\n\n" +
                $"“请在「机队」为推荐飞机 {ac} 点击「分配至深圳—北京」。\n" +
                "分配完成后我会打开排班确认。”");
            _extra.text = "等待操作：机队 → 分配至深圳—北京";
            ConfigureButtons(
                "回到机队", () =>
                {
                    Hide();
                    Object.FindFirstObjectByType<PrefabNavIntegration>()?.SelectTab("n9_机队");
                },
                null, null, null, null);
        }

        /// <summary>关闭弹窗并等待目标页操作；操作完成后再弹出下一步。</summary>
        void BeginAwaitPageAction(int awaitKind, string pageId)
        {
            var gs = GameState.Instance;
            if (gs != null)
            {
                gs.advisorTutorialAwaitPage = awaitKind;
                gs.advisorTutorialDeferred = false;
                gs.NotifyChanged();
                SaveManager.Instance?.Save();
            }
            Hide();
            Object.FindFirstObjectByType<PrefabNavIntegration>()?.SelectTab(pageId);
            StartCoroutine(PrepareAwaitedPage(awaitKind));
        }

        IEnumerator PrepareAwaitedPage(int awaitKind)
        {
            yield return null;
            yield return null;
            if (awaitKind == AdvisorTutorialScript.AwaitFleet)
            {
                var fleet = Object.FindFirstObjectByType<FleetUIType>(FindObjectsInactive.Include);
                fleet?.Refresh();
            }
        }

        /// <summary>S1「发起决策会议」：在收件箱留一份深圳—北京提案邮件记录，随后直接进入决策会议。</summary>
        void LeaveMeetingMailRecord()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            gs.advisorTutorialMailLeft = true;
            gs.NotifyChanged();
            SaveManager.Instance?.Save();

            var inbox = Object.FindFirstObjectByType<InboxPanelUI>(FindObjectsInactive.Include);
            inbox?.EnsureTutorialMailRecord();
        }

        /// <summary>机队点「分配至深圳—北京」后调用 → 弹出排班确认。</summary>
        public void ContinueFromFleetAssign()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.advisorTutorialAwaitPage != AdvisorTutorialScript.AwaitFleet) return;
            gs.advisorTutorialAwaitPage = AdvisorTutorialScript.AwaitNone;
            SaveManager.Instance?.Save();
            SetStep(AdvisorTutorialStep.S4_Schedule);
            Show();
        }

        void ShowS4()
        {
            _title.text = "排班确认";
            string ac = AdvisorTutorialScript.RecommendedAircraftLabel();
            var gs = GameState.Instance;
            string days = string.IsNullOrEmpty(gs?.tutorialDaysOfWeek) ? "每日" : gs.tutorialDaysOfWeek;
            int weekly = gs != null && gs.tutorialWeeklyFlights > 0 ? gs.tutorialWeeklyFlights : 7;
            string dep = string.IsNullOrEmpty(gs?.tutorialDepTime) ? "08:00" : gs.tutorialDepTime;
            string arr = string.IsNullOrEmpty(gs?.tutorialArrTime) ? "10:30" : gs.tutorialArrTime;
            _body.text =
                "航空顾问：\n\n" +
                "“飞机已选定！运营部已根据该飞机的闲置窗口，自动生成了排班方案：”\n\n" +
                "航线：深圳 → 北京首都\n" +
                $"执飞机型：{ac}\n" +
                $"班次：{days}（每周 {weekly} 班）\n" +
                $"起飞时刻：{dep}（深圳）→ {arr}（北京）\n\n" +
                "“您可以直接确认，也可以点「调整班次」修改班次频率与起飞时刻。”";
            _extra.text = "确认后将扣除启动资金并开通航线，播放首航结算。";
            ConfigureButtons(
                "确认排班", () =>
                {
                    SetStep(AdvisorTutorialStep.S5_FlightResult);
                    AbortFlightAnim();
                    _flightAnim = StartCoroutine(FirstFlightRoutine());
                },
                "调整班次", ShowScheduleEditor,
                null, null);
        }

        void ShowS5()
        {
            _title.text = "首航进行中…";
            string ac = AdvisorTutorialScript.RecommendedAircraftLabel();
            _body.text = AdvisorTutorialScript.Fmt($"航空顾问：\n\n“{ac} 正在飞往北京首都…请稍候。”");
            _extra.text = "① 起飞 → ② 巡航 → ③ 降落";
            ConfigureButtons(null, null, null, null, null, null);
        }

        void ShowS5Result()
        {
            _title.text = "首航成功";
            _body.text = AdvisorTutorialScript.Fmt(
                "航空顾问：\n\n" +
                "“首航成功，[玩家姓名]！航班已安全降落在北京首都国际机场。”\n\n" +
                "“首航表现超出预期！客座率 89%，净利润 6.2 万元，董事会满意度上升 5 点。”\n\n" +
                "“您已经成功完成了第一条航线的全流程——从发现机会到决策、执行、收获成果。”\n\n" +
                "“感觉如何？要不要趁热打铁，再开一条线？”");
            _extra.text =
                "航线收入　18.4 万\n航线成本　12.2 万\n净利润　6.2 万\n客座率　89%\n董事会满意度　+5\n现金跑道　+0.3 月";
            ConfigureButtons(
                "再开一条线！", () => GoToStep(AdvisorTutorialStep.S6_NextHook),
                "先看看其他系统", () => { SetStep(AdvisorTutorialStep.Done); Hide(); },
                null, null);
        }

        // ── 排班编辑器（S4「调整班次」） ──

        void BuildScheduleEditor()
        {
            var contentTf = _panel != null ? _panel.transform.Find("Content") : null;
            _schedEditor = UIFactory.Panel(contentTf != null ? contentTf : _panel.transform, "SchedEditor",
                new UIFactory.Anchor(new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(_schedEditor, UITheme.Border, new Vector2(1.2f, -1.2f));

            UIFactory.Label(_schedEditor.transform, "Title", "调整班次 · 深圳 → 北京", 20,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.86f), new Vector2(0.97f, 0.97f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Label(_schedEditor.transform, "FreqLabel", "班次频率", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.74f), new Vector2(0.3f, 0.84f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var freqs = AdvisorTutorialScript.FreqPresets;
            for (int i = 0; i < freqs.Length; i++)
            {
                int idx = i;
                float x0 = 0.03f + i * 0.32f;
                var b = UIFactory.Button(_schedEditor.transform, "Freq_" + i, freqs[i].Label,
                    new UIFactory.Anchor(new Vector2(x0, 0.62f), new Vector2(x0 + 0.30f, 0.72f), Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, () => { _freqIdx = idx; RefreshScheduleButtons(); }, 14);
                _freqBtns.Add(b);
            }

            UIFactory.Label(_schedEditor.transform, "SlotLabel", "起飞时刻", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.50f), new Vector2(0.3f, 0.60f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var slots = AdvisorTutorialScript.SlotPresets;
            for (int i = 0; i < slots.Length; i++)
            {
                int idx = i;
                float x0 = 0.03f + i * 0.24f;
                var b = UIFactory.Button(_schedEditor.transform, "Slot_" + i, $"{slots[i].Label}\n{slots[i].Dep}",
                    new UIFactory.Anchor(new Vector2(x0, 0.38f), new Vector2(x0 + 0.22f, 0.48f), Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, () => { _slotIdx = idx; RefreshScheduleButtons(); }, 13);
                _slotBtns.Add(b);
            }

            UIFactory.Button(_schedEditor.transform, "Auto", "自动排班（运营部建议）",
                new UIFactory.Anchor(new Vector2(0.03f, 0.26f), new Vector2(0.38f, 0.35f), Vector2.zero, Vector2.zero),
                UITheme.Accent, AutoSchedule, 13);

            _schedPreview = UIFactory.Label(_schedEditor.transform, "Preview", "", 14,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.10f), new Vector2(0.97f, 0.25f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _schedPreview.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.Button(_schedEditor.transform, "Apply", "应用调整",
                new UIFactory.Anchor(new Vector2(0.56f, 0.01f), new Vector2(0.98f, 0.08f), Vector2.zero, Vector2.zero),
                UITheme.Success, ApplySchedule, 15);
            UIFactory.Button(_schedEditor.transform, "Cancel", "取消",
                new UIFactory.Anchor(new Vector2(0.28f, 0.01f), new Vector2(0.52f, 0.08f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, HideScheduleEditor, 14);

            _schedEditor.SetActive(false);
        }

        void ShowScheduleEditor()
        {
            if (_schedEditor == null) return;
            var gs = GameState.Instance;
            _freqIdx = FindFreqIndex(gs != null ? gs.tutorialWeeklyFlights : 7);
            _slotIdx = FindSlotIndex(gs != null ? gs.tutorialTimeSlot : "早班");
            RefreshScheduleButtons();
            _schedEditor.SetActive(true);
            SetContentVisible(false);   // 标题也收掉：编辑器自带「调整班次」标题
        }

        void HideScheduleEditor()
        {
            if (_schedEditor != null) _schedEditor.SetActive(false);
            SetContentVisible(true);
            RefreshStep();
        }

        /// <summary>正文/卡片/按钮行整组显隐（排班编辑器接管面板时用）。</summary>
        void SetContentVisible(bool visible)
        {
            if (_bodyHost != null) _bodyHost.SetActive(visible);
            if (_extraHost != null) _extraHost.SetActive(visible);
            if (_btnRow != null) _btnRow.gameObject.SetActive(visible);
            if (_title != null) _title.gameObject.SetActive(visible);
        }

        void RefreshScheduleButtons()
        {
            var freqs = AdvisorTutorialScript.FreqPresets;
            for (int i = 0; i < _freqBtns.Count && i < freqs.Length; i++)
            {
                var img = _freqBtns[i] != null ? _freqBtns[i].GetComponent<Image>() : null;
                if (img != null)
                {
                    img.color = i == _freqIdx ? UITheme.Accent : UITheme.PanelDeep;
                    _freqBtns[i].GetComponent<UIHoverBrighten>()?.RefreshBase();
                }
            }
            var slots = AdvisorTutorialScript.SlotPresets;
            for (int i = 0; i < _slotBtns.Count && i < slots.Length; i++)
            {
                var img = _slotBtns[i] != null ? _slotBtns[i].GetComponent<Image>() : null;
                if (img != null)
                {
                    img.color = i == _slotIdx ? UITheme.Accent : UITheme.PanelDeep;
                    _slotBtns[i].GetComponent<UIHoverBrighten>()?.RefreshBase();
                }
            }
            UpdateSchedulePreview();
        }

        void UpdateSchedulePreview()
        {
            if (_schedPreview == null) return;
            var f = AdvisorTutorialScript.FreqPresets[ClampIdx(_freqIdx, AdvisorTutorialScript.FreqPresets.Length)];
            var s = AdvisorTutorialScript.SlotPresets[ClampIdx(_slotIdx, AdvisorTutorialScript.SlotPresets.Length)];
            float profit = AdvisorTutorialScript.EstimateDailyProfit(s.Demand, f.Revenue);
            _schedPreview.text =
                $"班次：{f.Days}（每周 {f.Weekly} 班 · 每日 1 班）\n" +
                $"时刻：{s.Label} {s.Dep} → {s.Arr}\n" +
                $"预估日利润：约 {profit:F1} 万元\n" +
                "提示：频率越高收益越高；黄金时段客座率更高，红眼需求低但能提高飞机白日利用率。";
        }

        void AutoSchedule()
        {
            var freqs = AdvisorTutorialScript.FreqPresets;
            var slots = AdvisorTutorialScript.SlotPresets;
            int bestF = 0, bestS = 0;
            float best = float.MinValue;
            for (int f = 0; f < freqs.Length; f++)
                for (int s = 0; s < slots.Length; s++)
                {
                    float p = AdvisorTutorialScript.EstimateDailyProfit(slots[s].Demand, freqs[f].Revenue);
                    if (p > best) { best = p; bestF = f; bestS = s; }
                }
            _freqIdx = bestF;
            _slotIdx = bestS;
            RefreshScheduleButtons();
            _schedPreview.text += "\n\n已按「预估日利润最高」自动选中（每日 + 晚班黄金时段）。";
        }

        void ApplySchedule()
        {
            var gs = GameState.Instance;
            var f = AdvisorTutorialScript.FreqPresets[ClampIdx(_freqIdx, AdvisorTutorialScript.FreqPresets.Length)];
            var s = AdvisorTutorialScript.SlotPresets[ClampIdx(_slotIdx, AdvisorTutorialScript.SlotPresets.Length)];
            if (gs != null)
            {
                gs.tutorialWeeklyFlights = f.Weekly;
                gs.tutorialDaysOfWeek = f.Days;
                gs.tutorialTimeSlot = s.Label;
                gs.tutorialDepTime = s.Dep;
                gs.tutorialArrTime = s.Arr;
                gs.NotifyChanged();
                SaveManager.Instance?.Save();
            }
            HideScheduleEditor();
        }

        int FindFreqIndex(int weekly)
        {
            var freqs = AdvisorTutorialScript.FreqPresets;
            for (int i = 0; i < freqs.Length; i++)
                if (freqs[i].Weekly == weekly) return i;
            return 0;
        }

        int FindSlotIndex(string label)
        {
            var slots = AdvisorTutorialScript.SlotPresets;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Label == label) return i;
            return 0;
        }

        static int ClampIdx(int v, int count)
        {
            if (v < 0) return 0;
            if (v >= count) return count - 1;
            return v;
        }

        void ShowS6()
        {
            _title.text = AdvisorTutorialScript.AdvisorTitle + " · 下一程";
            _body.text = AdvisorTutorialScript.Fmt(
                "航空顾问：\n\n" +
                "“太好了，[玩家姓名]！我刚扫描了一下市场，发现成都至上海航线也有不错的机会。”\n\n" +
                "“预估日利润：12.8 万元\n综合评分：79 分（绿色，推荐）\n竞对情况：目前只有 2 家航司运营，时刻窗口充足。”\n\n" +
                "“这一次，您可以独立完成全部决策流程——我会在顶栏随时待命，但不会主动弹出建议了。”\n\n" +
                "“需要我先打开市场评估页面吗？”");
            _extra.text = "引导即将结束 · 顾问进入待命";
            ConfigureButtons(
                "打开 RPM 评估", () =>
                {
                    SetStep(AdvisorTutorialStep.Done);
                    Hide();
                    // n14 的「RPM 评估」页签已移除（与 n35 重复），统一打开 n35 评估台。
                    StrategyPanelUI.OpenStrategyPage("n35_新航线_RPM_评估台");
                },
                "先熟悉一下界面", () => { SetStep(AdvisorTutorialStep.Done); Hide(); },
                null, null);
        }

        IEnumerator FirstFlightRoutine()
        {
            // 先展示「进行中」画面，避免确认排班后瞬间闪过
            ShowS5();
            yield return new WaitForSecondsRealtime(0.35f);

            if (!ApplyOpenRouteTutorial())
            {
                _title.text = "开航未完成";
                _body.text = "航空顾问：\n\n“开航未能执行。请检查现金是否≥300 万、机队是否有闲置适航飞机，然后点顶栏「顾问」重试。”";
                _extra.text = "可返回排班步骤后再次确认。";
                SetStep(AdvisorTutorialStep.S4_Schedule);
                ConfigureButtons("返回排班", () => RefreshStep(), "稍后再说", DeferOrHide, null, null);
                _flightAnim = null;
                yield break;
            }

            // 文档：起飞 1.5s + 巡航 2.0s + 降落 1.5s ≈ 5s
            string[] phases =
            {
                "① 起飞：深圳宝安",
                "② 巡航：云层上方",
                "③ 降落：北京首都"
            };
            float[] holds = { 1.5f, 2.0f, 1.5f };
            for (int i = 0; i < phases.Length; i++)
            {
                _title.text = "首航进行中…";
                _extra.text = phases[i];
                _body.text = AdvisorTutorialScript.Fmt(
                    i < 2
                        ? "航空顾问：\n\n“首航进行中…”"
                        : "航空顾问：\n\n“首航成功，[玩家姓名]！航班已安全降落在北京首都国际机场。”");
                yield return new WaitForSecondsRealtime(holds[i]);
            }

            ApplyFirstFlightRewards();
            _flightAnim = null;
            ShowS5Result();
        }

        bool ApplyOpenRouteTutorial()
        {
            if (ActionExecutor.Instance == null)
            {
                var go = new GameObject("ActionExecutor");
                go.AddComponent<ActionExecutor>();
            }

            var advice = new AdvisorResponse
            {
                advisor = AdvisorTutorialScript.AdvisorTitle,
                message = "开局引导批准开航",
                suggestedAction = "open_route",
                data = new AdvisorDataPayload
                {
                    origin = AdvisorTutorialScript.Origin,
                    destination = AdvisorTutorialScript.Dest,
                    profit = 16.2f
                }
            };
            var result = ActionExecutor.Instance.Execute(SuggestedAction.OpenRoute, advice);
            if (result == null || !result.success)
            {
                Debug.LogWarning("[AdvisorTutorial] 开航失败：" + (result != null ? result.message : "无结果"));
                if (_extra != null && result != null)
                    _extra.text = result.message;
                return false;
            }
            ApplyScheduleToOpenedRoute();
            return true;
        }

        /// <summary>把 S4 调整的班次写回刚开通的深圳—北京航线。</summary>
        void ApplyScheduleToOpenedRoute()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.activeRoutes == null || gs.activeRoutes.Count == 0) return;

            RouteData route = null;
            for (int i = gs.activeRoutes.Count - 1; i >= 0; i--)
            {
                var r = gs.activeRoutes[i];
                if (r != null && r.origin == "SZX" && r.destination == "PEK") { route = r; break; }
            }
            if (route == null) return;

            route.weeklyFlights = gs.tutorialWeeklyFlights > 0 ? gs.tutorialWeeklyFlights : 7;
            route.daysOfWeek = string.IsNullOrEmpty(gs.tutorialDaysOfWeek) ? "每日" : gs.tutorialDaysOfWeek;
            route.depTime = string.IsNullOrEmpty(gs.tutorialDepTime) ? "08:00" : gs.tutorialDepTime;
            route.arrTime = string.IsNullOrEmpty(gs.tutorialArrTime) ? "10:30" : gs.tutorialArrTime;

            var f = AdvisorTutorialScript.FreqPresets[ClampIdx(
                FindFreqIndex(route.weeklyFlights), AdvisorTutorialScript.FreqPresets.Length)];
            var s = AdvisorTutorialScript.SlotPresets[ClampIdx(
                FindSlotIndex(gs.tutorialTimeSlot ?? "早班"), AdvisorTutorialScript.SlotPresets.Length)];
            float daily = AdvisorTutorialScript.EstimateDailyProfit(s.Demand, f.Revenue);
            route.dailyProfit = daily;
            route.baseDailyProfit = daily;
            route.SyncFlightsFromPrimary(); // 单班排班：flights 与平面字段一致
            gs.NotifyChanged();
        }

        void ApplyFirstFlightRewards()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            // 文档结算：净利润示意 +6.2、满意度 +5（开航已扣 300，此处补演示收益）
            gs.cash += 6.2f;
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + 5f, 0f, 100f);
            gs.firstFlightCompleted = true;
            gs.AddDecisionLog("开局引导：深圳—北京首航结算，净利润 +6.2，满意度 +5。");
            gs.NotifyChanged();
            SaveManager.Instance?.Save();
        }

        void DeferOrHide()
        {
            AbortFlightAnim();
            var gs = GameState.Instance;
            if (gs != null && !gs.advisorTutorialCompleted)
                gs.advisorTutorialDeferred = true;
            Hide();
        }

        void SkipTutorial()
        {
            AbortFlightAnim();
            var gs = GameState.Instance;
            if (gs != null)
                gs.advisorTutorialAwaitPage = AdvisorTutorialScript.AwaitNone;
            SetStep(AdvisorTutorialStep.Done);
            Hide();
        }

        /// <summary>
        /// 换步刷新。调用方须已通过 ConfigureButtons 延后到下一帧，
        /// 避免同一次点击里重绑 onClick 连跳（典型为 S1 / S3）。
        /// </summary>
        void GoToStep(AdvisorTutorialStep step)
        {
            SetStep(step);
            RefreshStep();
        }

        Coroutine _buttonInvoke;
        bool _clickBusy;

        void ConfigureButtons(
            string primary, System.Action onPrimary,
            string secondary, System.Action onSecondary,
            string tertiary, System.Action onTertiary)
        {
            _clickBusy = false;
            if (_buttonInvoke != null)
            {
                StopCoroutine(_buttonInvoke);
                _buttonInvoke = null;
            }

            void Wire(Button btn, TextMeshProUGUI label, string text, System.Action act)
            {
                if (btn == null) return;
                bool show = !string.IsNullOrEmpty(text) && act != null;
                btn.gameObject.SetActive(show || (!string.IsNullOrEmpty(text) && act == null && text.Contains("不足")));
                btn.onClick.RemoveAllListeners();
                if (label != null) label.text = text ?? "";
                btn.interactable = act != null;
                if (act == null) return;

                // 延后一帧执行，避免同一次点击里重绑 onClick 连跳；用忙锁而非永久禁用按钮
                // （否则「调整班次」点完后「确认排班」会一直点不动）
                btn.onClick.AddListener(() =>
                {
                    if (_clickBusy) return;
                    _clickBusy = true;
                    _buttonInvoke = StartCoroutine(InvokeNextFrame(act));
                });
            }

            Wire(_primary, _primaryLabel, primary, onPrimary);
            Wire(_secondary, _secondaryLabel, secondary, onSecondary);
            Wire(_tertiary, _tertiaryLabel, tertiary, onTertiary);
        }

        IEnumerator InvokeNextFrame(System.Action act)
        {
            yield return null;
            try
            {
                act?.Invoke();
            }
            finally
            {
                _clickBusy = false;
                _buttonInvoke = null;
            }
        }
    }
}
