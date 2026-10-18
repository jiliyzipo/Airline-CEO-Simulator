using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI.Framework
{
    /// <summary>带 ScrollRect 的列表面板。</summary>
    public class ScrollListPanel : BasePanel
    {
        [SerializeField] ScrollRect scrollRect;
        [SerializeField] RectTransform contentRoot;

        public RectTransform ContentRoot => contentRoot;

        public void AssignScrollRefs(ScrollRect scroll, RectTransform content)
        {
            scrollRect = scroll;
            contentRoot = content;
        }

        public void ClearItems()
        {
            if (contentRoot == null) return;
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
                Destroy(contentRoot.GetChild(i).gameObject);
        }

        public Transform SpawnItem(GameObject template)
        {
            if (contentRoot == null || template == null) return null;
            var go = Instantiate(template, contentRoot, false);
            go.SetActive(true);
            return go.transform;
        }

        public void ScrollToTop()
        {
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
        }
    }
}
