using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>模块页内容区：子页嵌在本页框架内，不盖住标题与子标签。</summary>
    public static class ModuleFrame
    {
        public static RectTransform Body { get; private set; }

        public static RectTransform EnsureSubHost(Transform panel, float yMax = 0.93f)
        {
            if (panel == null) return null;
            var existing = panel.Find("ModuleSubHost");
            GameObject go;
            if (existing != null)
                go = existing.gameObject;
            else
            {
                go = UIFactory.Panel(panel, "ModuleSubHost",
                    new UIFactory.Anchor(new Vector2(0.01f, 0.02f), new Vector2(0.99f, yMax),
                        Vector2.zero, Vector2.zero),
                    UITheme.Panel);
                UIFactory.AddBorder(go, UITheme.Border, new Vector2(1f, -1f));
                go.SetActive(false);
            }

            go.transform.SetAsLastSibling();
            Body = go.transform as RectTransform;
            return Body;
        }

        public static void Unbind(Transform panel)
        {
            if (panel == null || Body == null) return;
            if (Body.parent == panel)
                Body = null;
        }

        /// <summary>底栏切换 L1 时松开子页宿主，避免旧模块（如收件箱）一直显示。</summary>
        public static void ReleaseHost()
        {
            ExitSubpage();
            Body = null;
        }

        public static void EnterSubpage()
        {
            if (Body == null) return;
            Body.gameObject.SetActive(true);
            Body.SetAsLastSibling();
        }

        public static void ExitSubpage()
        {
            if (Body != null)
                Body.gameObject.SetActive(false);
        }

        public static void Flatten(GameObject go)
        {
            if (go == null) return;
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
                UIFactory.ApplyAnchor(rt, UIFactory.Fill);
            var img = go.GetComponent<Image>();
            if (img != null)
            {
                img.color = new Color(0, 0, 0, 0);
                img.raycastTarget = false;
            }
            var card = go.transform.Find("Card") as RectTransform;
            if (card != null)
                UIFactory.ApplyAnchor(card, UIFactory.Fill);
        }
    }
}
