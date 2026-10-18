using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;

namespace AirlineCEO.UI.Shell
{
    /// <summary>n8 航空事务官 AO · Phase 1.4 最小版（预设跳转 + 关闭）。</summary>
    public class AoPanelUI : BasePanel
    {
        public static AoPanelUI Build()
        {
            var root = UIFactory.Panel(null, "n8_航空事务官_AO", UIFactory.Fill, UITheme.Panel);
            var cg = root.AddComponent<CanvasGroup>();
            var title = UIFactory.Label(root.transform, "Title", "💬 航空事务官（AO）· 小航", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.90f), new Vector2(0.75f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(root.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.92f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 22);

            var panel = root.AddComponent<AoPanelUI>();
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);

            var gs = GameState.Instance;
            string greet = gs != null
                ? $"CEO {gs.ceoDisplayName}，欢迎执掌 {gs.companyNameZh}。我是 AO「小航」。建议不会自动执行，需你确认。"
                : "我是 AO「小航」。建议不会自动执行，需你确认。";

            UIFactory.Label(root.transform, "Chat", greet + "\n\n状态：绿（待命）\n选择预设，或关闭返回。", 15,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.35f), new Vector2(0.97f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            float x = 0.03f;
            void Preset(string label, string pageId)
            {
                UIFactory.Button(root.transform, "P_" + label, label,
                    new UIFactory.Anchor(new Vector2(x, 0.08f), new Vector2(x + 0.18f, 0.20f), Vector2.zero, Vector2.zero),
                    UITheme.Accent, () =>
                    {
                        var nav = Object.FindFirstObjectByType<PrefabNavIntegration>();
                        NavigationRouter.Instance?.CloseOverlay();
                        nav?.SelectTab(pageId);
                    }, 13);
                x += 0.19f;
            }

            Preset("开航线", "n13_运营部");
            Preset("买飞机", "n9_机队");
            Preset("看报表", "n15_财务部");
            Preset("收件箱", "n7_收件箱");

            return panel;
        }
    }
}
