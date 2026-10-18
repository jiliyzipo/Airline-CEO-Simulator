using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 顾问浮窗：可带右下角圆形图标，或仅由顶栏「顾问」按钮展开/收拢（无浮标）。
    /// </summary>
    public class FloatingPanelChrome : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float IconSize = 72f;
        private const float DragClickSlop = 10f;

        private RectTransform _hostRt;
        private RectTransform _parentRt;
        private Canvas _canvas;
        private GameObject _panelGo;
        private RectTransform _panelRt;
        private GameObject _iconGo;
        private Image _hostBg;
        private bool _showFloatingIcon = true;

        private bool _expanded;
        private float _expandedWidth;
        private float _expandedHeight;
        private Vector2 _pressScreen;
        private bool _dragMoved;
        private readonly Dictionary<GameObject, bool> _contentWasActive = new Dictionary<GameObject, bool>();

        public bool IsCollapsed => !_expanded;
        public bool IsExpanded => _expanded;

        /// <summary>把已有对话面板挂进「图标 + 可展开窗」结构。</summary>
        public static FloatingPanelChrome Attach(GameObject panel, string title = "战略顾问", bool showFloatingIcon = true)
        {
            if (panel == null) return null;

            var parent = panel.transform.parent;
            var host = new GameObject("AdvisorDock", typeof(RectTransform), typeof(Image));
            host.transform.SetParent(parent, false);
            var hostRt = host.GetComponent<RectTransform>();
            hostRt.anchorMin = new Vector2(1f, 0f);
            hostRt.anchorMax = new Vector2(1f, 0f);
            hostRt.pivot = new Vector2(1f, 0f);
            hostRt.sizeDelta = new Vector2(IconSize, IconSize);
            hostRt.anchoredPosition = new Vector2(-24f, 24f);

            var hostImg = host.GetComponent<Image>();
            hostImg.color = new Color(0, 0, 0, 0);
            hostImg.raycastTarget = false;

            panel.transform.SetParent(host.transform, false);
            panel.name = "ChatPanel";

            var chrome = host.GetComponent<FloatingPanelChrome>();
            if (chrome == null) chrome = host.AddComponent<FloatingPanelChrome>();
            chrome._showFloatingIcon = showFloatingIcon;
            chrome.Setup(panel, title);
            return chrome;
        }

        private void Setup(GameObject panel, string title)
        {
            _hostRt = GetComponent<RectTransform>();
            _parentRt = _hostRt.parent as RectTransform;
            _canvas = GetComponentInParent<Canvas>();
            _panelGo = panel;
            _panelRt = panel.GetComponent<RectTransform>();
            _hostBg = GetComponent<Image>();

            float parentW = _parentRt != null ? Mathf.Max(400f, _parentRt.rect.width) : 1920f;
            float parentH = _parentRt != null ? Mathf.Max(300f, _parentRt.rect.height) : 1080f;
            _expandedWidth = Mathf.Clamp(parentW * 0.36f, 420f, 560f);
            _expandedHeight = Mathf.Clamp(parentH * 0.32f, 260f, 360f);

            _panelRt.anchorMin = new Vector2(1f, 0f);
            _panelRt.anchorMax = new Vector2(1f, 0f);
            _panelRt.pivot = new Vector2(1f, 0f);
            _panelRt.sizeDelta = new Vector2(_expandedWidth, _expandedHeight);
            _panelRt.anchoredPosition = new Vector2(0f, _showFloatingIcon ? IconSize + 10f : 12f);

            EnsureHeader(title);
            if (_showFloatingIcon)
                BuildIconButton();
            SetExpanded(false);
        }

        private void EnsureHeader(string title)
        {
            var header = _panelGo.transform.Find("HeaderBar");
            if (header == null)
            {
                UIFactory.Panel(_panelGo.transform, "HeaderBar",
                    new UIFactory.Anchor(new Vector2(0, 0.88f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                    UITheme.Accent, rounded: false);
            }

            var titleTf = _panelGo.transform.Find("Title");
            TextMeshProUGUI titleTmp = titleTf != null ? titleTf.GetComponent<TextMeshProUGUI>() : null;
            if (titleTmp == null)
            {
                titleTmp = UIFactory.Label(_panelGo.transform, "Title", title, 14,
                    TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.04f, 0.88f), new Vector2(0.82f, 1f), Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary, FontStyles.Bold);
            }
            else
            {
                UIFactory.ApplyAnchor(titleTmp.GetComponent<RectTransform>(),
                    new UIFactory.Anchor(new Vector2(0.04f, 0.88f), new Vector2(0.82f, 1f), Vector2.zero, Vector2.zero));
                titleTmp.text = title;
            }

            var oldCollapse = _panelGo.transform.Find("CollapseBtn");
            if (oldCollapse != null)
                oldCollapse.gameObject.SetActive(false);

            var closeAdvisor = _panelGo.transform.Find("CloseAdvisor");
            if (closeAdvisor != null)
                closeAdvisor.gameObject.SetActive(false);

            var headerGo = _panelGo.transform.Find("HeaderBar")?.gameObject;
            if (headerGo != null)
            {
                var zone = headerGo.GetComponent<FloatingPanelDragZone>();
                if (zone == null) zone = headerGo.AddComponent<FloatingPanelDragZone>();
                zone.Bind(this);
            }

            var titleDrag = titleTmp.gameObject.GetComponent<FloatingPanelDragZone>();
            if (titleDrag == null) titleDrag = titleTmp.gameObject.AddComponent<FloatingPanelDragZone>();
            titleDrag.Bind(this);
            titleTmp.raycastTarget = true;
        }

        private void BuildIconButton()
        {
            _iconGo = new GameObject("AdvisorIcon", typeof(RectTransform), typeof(Image), typeof(Button));
            _iconGo.transform.SetParent(transform, false);
            var rt = _iconGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(IconSize, IconSize);
            rt.anchoredPosition = Vector2.zero;

            var img = _iconGo.GetComponent<Image>();
            img.sprite = UIFactory.CircleSprite;
            img.type = Image.Type.Simple;
            img.color = UITheme.Accent;
            img.raycastTarget = true;
            img.preserveAspect = true;

            var ring = UIFactory.Panel(_iconGo.transform, "Ring",
                new UIFactory.Anchor(new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.1f, 0.2f, 0.55f));
            var ringImg = ring.GetComponent<Image>();
            ringImg.sprite = UIFactory.CircleSprite;
            ringImg.type = Image.Type.Simple;
            ringImg.raycastTarget = false;

            UIFactory.Label(_iconGo.transform, "Glyph", "AI", 20, TextAlignmentOptions.Center,
                UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);

            var btn = _iconGo.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;

            var drag = _iconGo.AddComponent<FloatingPanelDragZone>();
            drag.Bind(this, toggleOnClick: true);

            _iconGo.AddComponent<UIHoverBrighten>();
        }

        public void ToggleCollapse()
        {
            if (_dragMoved) return;
            SetExpanded(!_expanded);
        }

        /// <summary>顶栏「顾问」入口：展开/收拢对话面板。</summary>
        public void ToggleFromToolbar()
        {
            gameObject.SetActive(true);
            SetExpanded(!_expanded);
        }

        public void OpenFromToolbar()
        {
            gameObject.SetActive(true);
            SetExpanded(true);
        }

        public void SetCollapsed(bool collapsed) => SetExpanded(!collapsed);

        public void CollapseIfExpanded()
        {
            if (_expanded) SetExpanded(false);
        }

        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;

            if (_panelGo != null)
            {
                if (expanded)
                    RestorePanelContentVisibility();
                _panelGo.SetActive(expanded);
            }

            if (_hostRt != null)
            {
                if (expanded)
                {
                    float bottomPad = _showFloatingIcon ? IconSize + 10f : 12f;
                    _hostRt.sizeDelta = new Vector2(_expandedWidth, _expandedHeight + bottomPad);
                    if (_panelRt != null)
                    {
                        _panelRt.sizeDelta = new Vector2(_expandedWidth, _expandedHeight);
                        _panelRt.anchoredPosition = new Vector2(0f, bottomPad);
                    }
                }
                else if (_showFloatingIcon)
                {
                    _hostRt.sizeDelta = new Vector2(IconSize, IconSize);
                }
                else
                {
                    // 无浮标：收拢后占位为零，不挡点击
                    _hostRt.sizeDelta = Vector2.zero;
                }
            }

            if (_iconGo != null)
            {
                _iconGo.SetActive(_showFloatingIcon);
                if (_showFloatingIcon)
                    _iconGo.transform.SetAsLastSibling();
            }

            if (expanded)
                transform.SetAsLastSibling();
            ClampToParent();
        }

        private void RestorePanelContentVisibility()
        {
            for (int i = 0; i < _panelGo.transform.childCount; i++)
            {
                var child = _panelGo.transform.GetChild(i).gameObject;
                if (child.name == "CollapseBtn")
                {
                    child.SetActive(false);
                    continue;
                }

                if (child.name == "ExecuteAdviceButton" || child.name == "ExecuteHint")
                {
                    if (_contentWasActive.TryGetValue(child, out var was))
                        child.SetActive(was);
                    continue;
                }

                child.SetActive(true);
            }
        }

        public void RememberExecuteAdviceState(GameObject go, bool active)
        {
            if (go != null) _contentWasActive[go] = active;
        }

        #region Drag

        public void OnBeginDrag(PointerEventData eventData)
        {
            _pressScreen = eventData.position;
            _dragMoved = false;
            transform.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_hostRt == null || _parentRt == null) return;
            if ((eventData.position - _pressScreen).sqrMagnitude > DragClickSlop * DragClickSlop)
                _dragMoved = true;

            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _hostRt.anchoredPosition += eventData.delta / scale;
            ClampToParent();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            ClampToParent();
            if (_dragMoved)
                StartCoroutine(ClearDragFlagNextFrame());
        }

        private System.Collections.IEnumerator ClearDragFlagNextFrame()
        {
            yield return null;
            _dragMoved = false;
        }

        private void ClampToParent()
        {
            if (_hostRt == null || _parentRt == null) return;
            Vector2 pos = _hostRt.anchoredPosition;
            float w = Mathf.Max(8f, _hostRt.sizeDelta.x);
            float h = Mathf.Max(8f, _hostRt.sizeDelta.y);
            float maxX = -8f;
            float minX = -_parentRt.rect.width + w + 8f;
            float minY = 8f;
            float maxY = _parentRt.rect.height - h - 8f;
            if (maxY < minY) maxY = minY;
            pos.x = Mathf.Clamp(pos.x, Mathf.Min(minX, maxX), Mathf.Max(minX, maxX));
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            _hostRt.anchoredPosition = pos;
        }

        #endregion
    }

    /// <summary>图标/标题栏拖动转发；图标可额外响应点击展开/收拢。</summary>
    public class FloatingPanelDragZone : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private FloatingPanelChrome _chrome;
        private bool _toggleOnClick;

        public void Bind(FloatingPanelChrome chrome, bool toggleOnClick = false)
        {
            _chrome = chrome;
            _toggleOnClick = toggleOnClick;
        }

        public void OnBeginDrag(PointerEventData eventData) => _chrome?.OnBeginDrag(eventData);
        public void OnDrag(PointerEventData eventData) => _chrome?.OnDrag(eventData);
        public void OnEndDrag(PointerEventData eventData) => _chrome?.OnEndDrag(eventData);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_toggleOnClick || _chrome == null) return;
            if (eventData.dragging) return;
            _chrome.ToggleCollapse();
        }
    }
}
