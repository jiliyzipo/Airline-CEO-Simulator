using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>FIN 子页运行时工厂：无 Prefab 绑定时仍可打开白板页。</summary>
    public static class FinanceRuntimeFactory
    {
        public static GameObject TryCreate(string pageId, Transform parent)
        {
            if (string.IsNullOrEmpty(pageId)) return null;

            string kind = null;
            string title = null;
            if (pageId.StartsWith("n22") || pageId.Contains("预算"))
            {
                kind = "n22";
                title = "年度预算编制 / 审批";
            }
            else if (pageId.StartsWith("n31") || pageId.Contains("对冲"))
            {
                kind = "n31";
                title = "对冲工作台";
            }
            else if (pageId.StartsWith("n32") || pageId.Contains("碳"))
            {
                kind = "n32";
                title = "碳配额管理";
            }
            else if (pageId.StartsWith("n25") || pageId.Contains("融资") || pageId.Contains("破产保护"))
            {
                kind = "n25";
                title = "紧急融资 / 破产保护";
            }
            else if (pageId.StartsWith("n103") || pageId.Contains("条款"))
            {
                kind = "n103";
                title = "条款确认";
            }
            else if (pageId.StartsWith("n106") || pageId.Contains("重组"))
            {
                kind = "n106";
                title = "重组条件勾选";
            }

            if (kind == null) return null;

            var panel = FinanceSubpagePanel.Build(kind, title);
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
