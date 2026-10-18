using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 自绘闪烁插入符：挂在 Input 根节点（Text Area/RectMask2D 之外），
    /// 用 characterInfo 世界坐标定位，避免被遮罩裁掉或坐标换算错误。
    /// </summary>
    [DisallowMultipleComponent]
    public class InputCaretFallback : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        const float BlinkPeriod = 1.06f;
        const float TypingHoldSeconds = 0.55f;

        TMP_InputField _input;
        RectTransform _caretRt;
        Image _caretImg;
        Color _placeholderColor = new Color(1f, 1f, 1f, 0.55f);
        bool _havePlaceholderColor;
        bool _selected;
        float _blink;
        float _holdUntil;
        string _lastText;
        int _lastCaret = int.MinValue;

        static Sprite _whiteSprite;

        public static void Attach(TMP_InputField input)
        {
            if (input == null) return;
            if (input.GetComponent<InputCaretFallback>() != null) return;
            var fb = input.gameObject.AddComponent<InputCaretFallback>();
            fb._input = input;
        }

        void Awake()
        {
            if (_input == null) _input = GetComponent<TMP_InputField>();
            if (_input != null)
                _input.onValueChanged.AddListener(OnValueChanged);
        }

        void OnDestroy()
        {
            if (_input != null)
                _input.onValueChanged.RemoveListener(OnValueChanged);
        }

        void OnValueChanged(string _) => KeepVisibleAfterEdit();

        void OnDisable()
        {
            _selected = false;
            RestorePlaceholder();
            SetCaretVisible(false);
        }

        public void OnSelect(BaseEventData eventData)
        {
            _selected = true;
            KeepVisibleAfterEdit();
            EnsureCaretVisual();
            SyncPlaceholderForFocus();
            LayoutCaret(forceMesh: true);
            SetCaretVisible(true);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _selected = false;
            RestorePlaceholder();
            SetCaretVisible(false);
        }

        void LateUpdate()
        {
            if (!IsActivelyEditing())
            {
                RestorePlaceholder();
                SetCaretVisible(false);
                return;
            }

            EnsureCaretVisual();
            SyncPlaceholderForFocus();

            int caret = _input.stringPosition;
            string text = _input.text ?? "";
            if (caret != _lastCaret || text != _lastText)
            {
                _lastCaret = caret;
                _lastText = text;
                KeepVisibleAfterEdit();
                LayoutCaret(forceMesh: true);
            }
            else
            {
                // 文本未变也跟随（防止布局延迟一帧）
                LayoutCaret(forceMesh: false);
            }

            if (Time.unscaledTime < _holdUntil)
            {
                SetCaretVisible(true);
                return;
            }

            _blink += Time.unscaledDeltaTime;
            SetCaretVisible((_blink % BlinkPeriod) < BlinkPeriod * 0.5f);
        }

        bool IsActivelyEditing()
        {
            if (_input == null || !_input.isActiveAndEnabled) return false;
            var es = EventSystem.current;
            bool selectedByEs = es != null && es.currentSelectedGameObject == _input.gameObject;
            return _selected || _input.isFocused || selectedByEs;
        }

        void KeepVisibleAfterEdit()
        {
            _holdUntil = Time.unscaledTime + TypingHoldSeconds;
            _blink = 0f;
        }

        void SetCaretVisible(bool on)
        {
            if (_caretImg != null)
                _caretImg.enabled = on;
        }

        void SyncPlaceholderForFocus()
        {
            if (_input == null || _input.placeholder == null) return;

            var g = _input.placeholder;
            if (!_havePlaceholderColor)
            {
                _placeholderColor = g.color;
                _havePlaceholderColor = true;
            }

            // 空串聚焦时隐藏占位符（不用 CanvasGroup，避免 MissingComponentException）
            bool hide = IsActivelyEditing() && string.IsNullOrEmpty(_input.text);
            var c = _placeholderColor;
            c.a = hide ? 0f : _placeholderColor.a;
            g.color = c;
        }

        void RestorePlaceholder()
        {
            if (_input == null || _input.placeholder == null || !_havePlaceholderColor) return;
            _input.placeholder.color = _placeholderColor;
        }

        void EnsureCaretVisual()
        {
            if (_input == null) return;
            // 挂在 Input 根上，不进 Text Area，不被 RectMask2D 裁切
            var parent = _input.transform as RectTransform;
            if (parent == null) return;

            if (_caretRt != null)
            {
                if (_caretRt.parent != parent)
                    _caretRt.SetParent(parent, false);
                _caretRt.SetAsLastSibling();
                return;
            }

            var go = new GameObject("FallbackCaret", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.transform.SetAsLastSibling();

            _caretRt = go.GetComponent<RectTransform>();
            _caretRt.anchorMin = new Vector2(0.5f, 0.5f);
            _caretRt.anchorMax = new Vector2(0.5f, 0.5f);
            _caretRt.pivot = new Vector2(0.5f, 0.5f);
            _caretRt.localRotation = Quaternion.identity;
            _caretRt.localScale = Vector3.one;
            _caretRt.sizeDelta = new Vector2(2.5f, 20f);

            _caretImg = go.GetComponent<Image>();
            _caretImg.sprite = WhiteSprite();
            _caretImg.type = Image.Type.Simple;
            _caretImg.color = Color.white;
            _caretImg.raycastTarget = false;
            _caretImg.enabled = false;
        }

        static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = Texture2D.whiteTexture;
            _whiteSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
            return _whiteSprite;
        }

        void LayoutCaret(bool forceMesh)
        {
            if (_caretRt == null || _input == null || _input.textComponent == null) return;

            var tmp = _input.textComponent;
            var root = _input.transform as RectTransform;
            if (root == null) return;

            if (forceMesh)
                tmp.ForceMeshUpdate(true);

            string content = _input.text ?? "";
            int caret = Mathf.Clamp(_input.stringPosition, 0, content.Length);
            int charCount = tmp.textInfo != null ? tmp.textInfo.characterCount : 0;

            float localX;
            float localY;
            float h = Mathf.Max(18f, _input.pointSize + 4f);

            if (content.Length > 0 && charCount > 0)
            {
                if (caret <= 0)
                {
                    var first = tmp.textInfo.characterInfo[0];
                    localX = first.origin;
                    localY = (first.ascender + first.descender) * 0.5f;
                    h = Mathf.Max(14f, Mathf.Abs(first.ascender - first.descender));
                }
                else
                {
                    int idx = Mathf.Min(caret, content.Length, charCount) - 1;
                    idx = Mathf.Clamp(idx, 0, charCount - 1);
                    var info = tmp.textInfo.characterInfo[idx];
                    localX = info.xAdvance;
                    localY = (info.ascender + info.descender) * 0.5f;
                    h = Mathf.Max(14f, Mathf.Abs(info.ascender - info.descender));
                }
            }
            else
            {
                // 空串：文本左缘 + 边距
                var rect = tmp.rectTransform.rect;
                localX = rect.xMin + tmp.margin.x + 2f;
                localY = (rect.yMin + rect.yMax) * 0.5f;
            }

            Vector3 world = tmp.rectTransform.TransformPoint(new Vector3(localX, localY, 0f));
            Vector3 localInRoot = root.InverseTransformPoint(world);

            _caretRt.localPosition = localInRoot;
            _caretRt.sizeDelta = new Vector2(2.5f, h);
        }
    }
}
