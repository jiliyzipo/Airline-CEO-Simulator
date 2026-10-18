using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>MKT 子页运行时工厂：无 Prefab 绑定时仍可打开白板页。</summary>
    public static class MktRuntimeFactory
    {
        public static GameObject TryCreate(string pageId, Transform parent)
        {
            if (string.IsNullOrEmpty(pageId)) return null;

            string kind = null;
            string title = null;
            if (pageId.StartsWith("n44") || pageId.Contains("收益分析"))
            {
                kind = "n44";
                title = "航线收益分析";
            }
            else if (pageId.StartsWith("n49") || pageId.Contains("航班定价"))
            {
                kind = "n49";
                title = "航班定价";
            }
            else if (pageId.StartsWith("n45") || pageId.Contains("单航线定价"))
            {
                kind = "n45";
                title = "单航线定价详情";
            }
            else if (pageId.StartsWith("n46") || pageId.Contains("动态定价"))
            {
                kind = "n46";
                title = "动态定价算法面板";
            }
            else if (pageId.StartsWith("n47") || pageId.Contains("仪表盘"))
            {
                kind = "n47";
                title = "收益管理仪表盘";
            }
            else if (pageId.StartsWith("n48") || pageId.Contains("辅助收入"))
            {
                kind = "n48";
                title = "辅助收入管理";
            }
            else if (pageId.StartsWith("n50_外部环境") || pageId.Contains("外部环境"))
            {
                kind = "n50";
                title = "外部环境分析";
            }
            else if (pageId.StartsWith("n55") || pageId.Contains("季节性"))
            {
                kind = "n55";
                title = "季节性日历";
            }
            else if (pageId.StartsWith("n56") || pageId.Contains("航季对比"))
            {
                kind = "n56";
                title = "航季对比详情";
            }
            else if (pageId.StartsWith("n57") || pageId.Contains("旺季策略"))
            {
                kind = "n57";
                title = "旺季策略配置器";
            }
            else if (pageId.StartsWith("n58") || pageId.Contains("历史趋势"))
            {
                kind = "n58";
                title = "季节性历史趋势";
            }
            else if (pageId.StartsWith("n59") || pageId.Contains("影响预测"))
            {
                kind = "n59";
                title = "旺季影响预测";
            }
            else if (pageId.StartsWith("n33") || pageId.Contains("FFP") || pageId.Contains("常旅客"))
            {
                kind = "n33";
                title = "FFP 常旅客管理";
            }
            else if (pageId.StartsWith("n34") || pageId.Contains("产品工坊"))
            {
                kind = "n34";
                title = "产品工坊";
            }
            else if (pageId.StartsWith("n60") || pageId.Contains("竞争雷达"))
            {
                kind = "n60";
                title = "竞争雷达";
            }
            else if (pageId.StartsWith("n61") || pageId.Contains("竞对详情"))
            {
                kind = "n61";
                title = "竞对详情";
            }
            else if (pageId.StartsWith("n62") || pageId.Contains("航线网络对标"))
            {
                kind = "n62";
                title = "竞争 · 航线网络对标";
            }
            else if (pageId.StartsWith("n63") || pageId.Contains("产品服务对标"))
            {
                kind = "n63";
                title = "竞争 · 产品服务对标";
            }
            else if (pageId.StartsWith("n64") || pageId.Contains("运营成本对标"))
            {
                kind = "n64";
                title = "竞争 · 运营成本对标";
            }
            else if (pageId.StartsWith("n65") || pageId.Contains("战略资本对标"))
            {
                kind = "n65";
                title = "竞争 · 战略资本对标";
            }
            else if (pageId.StartsWith("n67") || pageId.Contains("大客户合同"))
            {
                kind = "n67";
                title = "大客户合同";
            }
            else if (pageId.StartsWith("n68") || pageId.Contains("渠道预算"))
            {
                kind = "n68";
                title = "渠道预算";
            }
            else if (pageId.StartsWith("n69") || pageId.Contains("分销渠道"))
            {
                kind = "n69";
                title = "分销渠道管理";
            }
            else if (pageId.StartsWith("n70") || pageId.Contains("直销渠道"))
            {
                kind = "n70";
                title = "直销渠道管理";
            }
            else if (pageId.StartsWith("n71") || pageId.Contains("渠道ROI"))
            {
                kind = "n71";
                title = "渠道ROI分析";
            }
            else if (pageId.StartsWith("n72") || pageId.Contains("品牌危机公关"))
            {
                kind = "n72";
                title = "品牌危机公关";
            }
            else if (pageId.StartsWith("n73") || pageId.Contains("舆情监测"))
            {
                kind = "n73";
                title = "舆情监测中心";
            }
            else if (pageId.StartsWith("n74") || pageId.Contains("危机响应"))
            {
                kind = "n74";
                title = "危机响应指挥部";
            }
            else if (pageId.StartsWith("n75") || pageId.Contains("品牌修复"))
            {
                kind = "n75";
                title = "品牌修复计划";
            }

            if (kind == null) return null;

            var panel = MarketSubpagePanel.Build(kind, title);
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
