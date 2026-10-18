using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>SAF 子页运行时工厂：无 Prefab 绑定时仍可打开白板页。</summary>
    public static class SafetyRuntimeFactory
    {
        public static GameObject TryCreate(string pageId, Transform parent)
        {
            if (string.IsNullOrEmpty(pageId)) return null;

            string kind = null;
            string title = null;
            if (pageId.StartsWith("n92") || pageId.Contains("SMS"))
            {
                kind = "n92";
                title = "SMS与风险管理";
            }
            else if (pageId.StartsWith("n93") || pageId.Contains("监察") || pageId.Contains("审核"))
            {
                kind = "n93";
                title = "安全监察与审核";
            }
            else if (pageId.StartsWith("n94") || pageId.Contains("调查"))
            {
                kind = "n94";
                title = "事件调查与分析";
            }
            else if (pageId.StartsWith("n95") || pageId.Contains("局方") || pageId.Contains("法规"))
            {
                kind = "n95";
                title = "局方与法规事务";
            }
            else if (pageId.StartsWith("n96") || pageId.Contains("文化"))
            {
                kind = "n96";
                title = "安全文化与培训";
            }
            else if (pageId.StartsWith("n100") || pageId.Contains("检查应对"))
            {
                kind = "n100";
                title = "检查应对弹窗";
            }
            else if (pageId.StartsWith("n101") || pageId.Contains("策略切换"))
            {
                kind = "n101";
                title = "策略切换确认弹窗";
            }

            if (kind == null) return null;

            var panel = SafetySubpagePanel.Build(kind, title);
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
