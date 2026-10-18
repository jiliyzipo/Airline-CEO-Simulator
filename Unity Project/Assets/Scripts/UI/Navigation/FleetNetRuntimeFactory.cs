using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>FLEET / NET 子页运行时工厂：无 Prefab 绑定时仍可打开白板页。</summary>
    public static class FleetNetRuntimeFactory
    {
        public static GameObject TryCreate(string pageId, Transform parent)
        {
            if (string.IsNullOrEmpty(pageId)) return null;

            GameObject go = null;

            if (pageId.StartsWith("n10_") || pageId.Contains("飞机详情") || pageId.Contains("履历"))
            {
                var panel = FleetSubpagePanel.Build("n10", "✈️ 飞机详情（履历）");
                panel.BindPage(pageId, "✈️ 飞机详情（履历）");
                go = panel.gameObject;
            }
            else if (PageIdMatch.Is(pageId, "n11") || pageId.Contains("座位") || pageId.Contains("载客量"))
            {
                var panel = FleetSubpagePanel.Build("n11", "🪑 载客量配置");
                panel.BindPage(pageId, "🪑 载客量配置");
                go = panel.gameObject;
            }
            else if (pageId.StartsWith("n107") || pageId.Contains("大修") || pageId.Contains("预算确认"))
            {
                var panel = FleetSubpagePanel.Build("n107", "💰 预算确认");
                panel.BindPage(pageId, "💰 预算确认");
                go = panel.gameObject;
            }
            else if (pageId.StartsWith("n97") || pageId.Contains("购机"))
            {
                var panel = PurchaseDecisionPanel.Build("🛩 购机决策会议");
                go = panel.gameObject;
            }
            else if (pageId.StartsWith("n41") || pageId.Contains("单航线"))
            {
                var panel = RouteDetailPanel.Build("📍 单航线");
                go = panel.gameObject;
            }

            if (go == null) return null;
            Parent(go, parent);
            return go;
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
