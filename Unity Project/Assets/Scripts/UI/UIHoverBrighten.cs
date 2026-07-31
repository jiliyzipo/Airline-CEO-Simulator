using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>按钮悬停时略微提亮底色（航空驾驶舱反馈）。</summary>
    [RequireComponent(typeof(Image))]
    public class UIHoverBrighten : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Image _image;
        private Color _base;
        private bool _inited;

        private void Awake()
        {
            _image = GetComponent<Image>();
            CacheBase();
        }

        private void OnEnable()
        {
            if (_image == null) _image = GetComponent<Image>();
            CacheBase();
        }

        private void CacheBase()
        {
            if (_image == null) return;
            _base = _image.color;
            _inited = true;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_inited) CacheBase();
            if (_image == null) return;
            _image.color = Color.Lerp(_base, Color.white, 0.22f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_image == null) return;
            _image.color = _base;
        }

        /// <summary>外部改色后刷新基准（如导航选中态）。</summary>
        public void RefreshBase()
        {
            CacheBase();
        }
    }
}
