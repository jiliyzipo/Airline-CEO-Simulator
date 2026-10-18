using AirlineCEO.UI.Shell;
using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>SHELL / 开局相关页运行时工厂（无 Prefab 时）。</summary>
    public static class ShellRuntimeFactory
    {
        public static GameObject TryCreate(string pageId, Transform parent)
        {
            if (string.IsNullOrEmpty(pageId)) return null;

            GameObject go = null;
            if (pageId.StartsWith("n110") || pageId.Contains("CEO资料"))
                go = CeoDossierPanel.BuildProfile().gameObject;
            else if (pageId.StartsWith("n111") || pageId.Contains("CEO合同"))
                go = CeoDossierPanel.BuildContract().gameObject;
            else if (pageId.StartsWith("n20") || (pageId.Contains("董事会") && !pageId.Contains("财年")))
                go = BoardTargetUI.Build(null).gameObject;
            else if (pageId.StartsWith("n6_") || pageId.Contains("主页") || pageId.Contains("Dashboard"))
                go = DashboardPanelUI.Build().gameObject;
            else if (pageId.StartsWith("n7_") || pageId.Contains("收件箱"))
                go = InboxPanelUI.Build().gameObject;
            else if (pageId.StartsWith("n8_") || pageId.Contains("航空事务官") || pageId.Contains("_AO"))
                go = AoPanelUI.Build().gameObject;
            else if (pageId.StartsWith("n40_") || pageId.Contains("设置"))
                go = SettingsPanelUI.Build().gameObject;
            else if (pageId.StartsWith("n115") || pageId.Contains("外部信息源"))
                go = ExternalSourcePanelUI.Build().gameObject;
            else if (pageId.StartsWith("n116") || pageId.Contains("行情源"))
                go = QuoteSourcePanelUI.Build().gameObject;
            else if (pageId.StartsWith("n28_") || pageId.Contains("宏观经济"))
                go = MacroPanelUI.Build().gameObject;
            else if (pageId.StartsWith("n21_") || pageId.Contains("决策会议"))
                go = DecisionMeetingPanel.Build().gameObject;
            else if (pageId.StartsWith("n104_") || pageId.Contains("补条件"))
                go = SupplementConditionPanel.Build().gameObject;
            else if (pageId.StartsWith("n23_") || pageId.Contains("部门负责人任命"))
                go = AppointmentPanel.Build().gameObject;
            else if (pageId.StartsWith("n26_") || pageId.Contains("财年结算"))
                go = YearEndSettlementPanel.Build().gameObject;
            else if (pageId.StartsWith("n30_") || pageId.Contains("随机事件日志"))
                go = EventLogPanelUI.Build().gameObject;
            else if (pageId.StartsWith("n38_") || pageId.Contains("历史回放"))
                go = HistoryReplayPanel.Build().gameObject;
            else if (pageId.StartsWith("n51_") || pageId.Contains("消息中心"))
                go = InboxPanelUI.Build().gameObject;
            else if (pageId.StartsWith("n12_") || pageId.Contains("航线网络"))
                go = NetworkPanelUI.Build(null).gameObject;

            if (go == null) return null;
            Parent(go, parent);
            return go;
        }

        static GameObject BuildSimpleInfo(string pageId, string title, string body)
        {
            var root = UIFactory.Panel(null, pageId, UIFactory.Fill, UITheme.Panel);
            var cg = root.AddComponent<CanvasGroup>();
            var titleLabel = UIFactory.Label(root.transform, "Title", title, 22,
                TMPro.TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.90f), new Vector2(0.7f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, TMPro.FontStyles.Bold);
            var close = UIFactory.Button(root.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.92f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 22);
            var panel = root.AddComponent<AirlineCEO.UI.Framework.BasePanel>();
            panel.AssignRefs(root.GetComponent<RectTransform>(), titleLabel, close, cg);
            panel.BindPage(pageId, title);
            UIFactory.Label(root.transform, "Body", body, 15, TMPro.TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.12f), new Vector2(0.97f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            return root;
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
