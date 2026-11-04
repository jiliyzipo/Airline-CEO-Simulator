using AirlineCEO.Data;
using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>OPS 子页运行时工厂：无 Prefab 绑定时仍可打开白板页。</summary>
    public static class OpsRuntimeFactory
    {
        public static GameObject TryCreate(string pageId, Transform parent)
        {
            if (string.IsNullOrEmpty(pageId)) return null;

            if (pageId.StartsWith("n24") || pageId.Contains("IRROPS") || pageId.Contains("危机"))
            {
                var modal = OpsIrropsModalUI.Build();
                Parent(modal.gameObject, parent);
                return modal.gameObject;
            }

            string kind = null;
            string title = null;
            if (pageId.StartsWith("n42") || pageId.Contains("周总"))
            {
                kind = "n42";
                title = "航班周总计划";
            }
            else if (pageId.StartsWith("n43") || pageId.Contains("详细") || pageId.Contains("飞行计划"))
            {
                kind = "n43";
                title = "航班飞行计划";
            }
            else if (pageId.StartsWith("n66") || pageId.Contains("航季"))
            {
                kind = "n66";
                title = "航季总纲";
            }
            // ★2026-11-01：n98「航线分配」与 n36「航班环构建器」两页都已删除。
            //   n98 的逐机航线计划与飞机详情重复；n36 的看板搬进了「机队 › 周计划及航班环」。
            //   故 OPS 侧只剩 n42 / n43 / n66 三张表页，不再有「航班环」运行时页。

            if (kind == null) return null;

            var panel = OpsSubpagePanel.Build(kind, title);
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
