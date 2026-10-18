using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>MNT 子页运行时工厂：无 Prefab 绑定时仍可打开白板页。</summary>
    public static class MaintenanceRuntimeFactory
    {
        public static GameObject TryCreate(string pageId, Transform parent)
        {
            if (string.IsNullOrEmpty(pageId)) return null;

            string kind = null;
            string title = null;
            if (pageId.StartsWith("n81") || pageId.Contains("定检"))
            {
                kind = "n81";
                title = "航线与定检维修";
            }
            else if (pageId.StartsWith("n82") || pageId.Contains("工程"))
            {
                kind = "n82";
                title = "工程技术与计划";
            }
            else if (pageId.StartsWith("n83") || pageId.Contains("航材"))
            {
                kind = "n83";
                title = "航材与供应链";
            }
            else if (pageId.StartsWith("n84") || pageId.Contains("适航"))
            {
                kind = "n84";
                title = "安全质量与适航";
            }
            else if (pageId.StartsWith("n85") || pageId.Contains("培训"))
            {
                kind = "n85";
                title = "维修培训与资质";
            }
            else if (pageId.StartsWith("n105") || pageId.Contains("订单确认"))
            {
                kind = "n105";
                title = "订单确认";
            }

            if (kind == null) return null;

            var panel = MaintenanceSubpagePanel.Build(kind, title);
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
