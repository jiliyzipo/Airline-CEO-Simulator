using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>STG 子页运行时工厂：无 Prefab 绑定时仍可打开白板页。</summary>
    public static class StrategyRuntimeFactory
    {
        public static GameObject TryCreate(string pageId, Transform parent)
        {
            if (string.IsNullOrEmpty(pageId)) return null;

            string kind = null;
            string title = null;
            if (pageId.StartsWith("n76") || pageId.Contains("运力规划"))
            {
                kind = "n76";
                title = "机队与运力规划";
            }
            else if (pageId.StartsWith("n77") || pageId.Contains("航权") || pageId.Contains("时刻槽"))
            {
                kind = "n77";
                title = "航权与时刻槽";
            }
            else if (pageId.StartsWith("n78") || pageId.Contains("联盟"))
            {
                kind = "n78";
                title = "联盟与战略合作";
            }
            else if (pageId.StartsWith("n79") || pageId.Contains("投资并购") || pageId.Contains("战略投资"))
            {
                kind = "n79";
                title = "战略投资与并购";
            }
            else if (pageId.StartsWith("n80") || pageId.Contains("研究") || pageId.Contains("预案"))
            {
                kind = "n80";
                title = "战略研究与预案";
            }
            else if (pageId.StartsWith("n35") || pageId.Contains("RPM"))
            {
                kind = "n35";
                title = "新航线 RPM 评估台";
            }
            else if (pageId.StartsWith("n102"))
            {
                kind = "n102";
                title = "谈判弹窗";
            }
            else if (pageId.StartsWith("n108") || pageId.Contains("临空") || pageId.Contains("非航"))
            {
                kind = "n108";
                title = "临空商业 · 非航收入";
            }

            if (kind == null) return null;

            var panel = StrategySubpagePanel.Build(kind, title);
            panel.BindPage(pageId, title);
            Parent(panel.gameObject, parent);
            return panel.gameObject;
        }

        static void Parent(GameObject go, Transform parent)
        {
            if (go == null || parent == null) return;
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }
    }
}
