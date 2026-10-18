using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AirlineCEO.UI.Navigation;

namespace AirlineCEO.UI.Framework
{
    /// <summary>
    /// Prefab 面板基类：背景 / 标题 / 关闭。Phase 0.1 冻结 API，Phase 1 挂到 Prefab。
    /// </summary>
    public class BasePanel : MonoBehaviour
    {
        [SerializeField] protected RectTransform root;
        [SerializeField] protected TextMeshProUGUI titleLabel;
        [SerializeField] protected Button closeButton;
        [SerializeField] protected CanvasGroup canvasGroup;

        public string PageId { get; private set; }
        public bool IsVisible => gameObject.activeSelf;

        public event Action<BasePanel> Closed;

        protected virtual void Awake()
        {
            if (root == null) root = transform as RectTransform;
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(CloseOverlay);
                closeButton.gameObject.SetActive(false);
            }
            HideCloseXButtons(transform);
        }

        /// <summary>
        /// 右上角 ×：一键关掉当前弹窗层。
        /// 有 NavigationRouter 栈时清空整栈；否则 Hide。
        /// 「← 返回」应调用 BackOne，勿与 × 混用。
        /// </summary>
        public virtual void CloseOverlay()
        {
            if (NavigationRouter.Instance != null && NavigationRouter.Instance.StackDepth > 0)
                NavigationRouter.Instance.CloseOverlay();
            else
                Hide();
        }

        /// <summary>← 返回：只退一层（子页 → 父页）。</summary>
        public virtual void BackOne()
        {
            if (NavigationRouter.Instance != null && NavigationRouter.Instance.StackDepth > 0)
                NavigationRouter.Instance.Back();
            else
                Hide();
        }

        /// <summary>把 Card 下的「← 返回」按钮绑到 BackOne（运行时构建的面板用它补线）。</summary>
        protected void WireBackButton()
        {
            var t = transform.Find("Card/Back");
            var btn = t != null ? t.GetComponent<Button>() : null;
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(BackOne);
        }

        public void AssignRefs(RectTransform rootRef, TextMeshProUGUI titleRef, Button closeRef, CanvasGroup groupRef)
        {
            root = rootRef;
            titleLabel = titleRef;
            closeButton = closeRef;
            canvasGroup = groupRef;
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(CloseOverlay);
                closeButton.gameObject.SetActive(false);
            }
            HideCloseXButtons(transform);
        }

        /// <summary>右上角 ×、页内「← 返回」一律隐藏，改用底栏 &lt;。</summary>
        public static void HideCloseXButtons(Transform root)
        {
            if (root == null) return;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                string n = c.name;
                if (n == "Close" || n == "X" || n == "CloseAdvisor" || n == "Back")
                    c.gameObject.SetActive(false);
                HideCloseXButtons(c);
            }
        }

        public virtual void BindPage(string pageId, string title = null)
        {
            PageId = pageId;
            if (titleLabel != null && !string.IsNullOrEmpty(title))
                titleLabel.text = title;
        }

        public virtual void Show()
        {
            gameObject.SetActive(true);
            HideCloseXButtons(transform);
            if (closeButton != null)
                closeButton.gameObject.SetActive(false);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
        }

        public virtual void Hide()
        {
            gameObject.SetActive(false);
            Closed?.Invoke(this);
        }
    }
}
