using AirlineCEO.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>已开通航线信息卡片。</summary>
    public class RouteCard : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI profitText;
        [SerializeField] private TextMeshProUGUI metaText;
        [SerializeField] private Image accentBar;

        public static RouteCard BuildRuntime(Transform parent)
        {
            var root = UIFactory.Panel(parent, "RouteCard",
                new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0, 72);
            rt.anchoredPosition = Vector2.zero;

            UIFactory.AddBorder(root, UITheme.Border, new Vector2(1.2f, -1.2f));

            var accent = UIFactory.Panel(root.transform, "Accent",
                new UIFactory.Anchor(new Vector2(0, 0), new Vector2(0.015f, 1), Vector2.zero, Vector2.zero),
                UITheme.Accent, rounded: false);

            var card = root.AddComponent<RouteCard>();
            card.accentBar = accent.GetComponent<Image>();
            card.titleText = UIFactory.Label(root.transform, "Title", "北京 → 伦敦", 16,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.48f), new Vector2(0.7f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            card.profitText = UIFactory.Label(root.transform, "Profit", "日利 62.8 万", 14,
                TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.55f, 0.48f), new Vector2(0.96f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.Success);
            card.metaText = UIFactory.Label(root.transform, "Meta", "开通：第 1 年 Q1 · 综合 70", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.08f), new Vector2(0.96f, 0.48f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            return card;
        }

        public void Bind(RouteData route)
        {
            if (route == null) return;
            if (titleText != null)
                titleText.text = $"{route.origin} → {route.destination}";
            if (profitText != null)
                profitText.text = $"日利 {route.dailyProfit:F1} 万";
            if (metaText != null)
                metaText.text = $"开通：{route.openedDate} · 综合评定 {route.score} 分（{route.rating}）";
            if (accentBar != null)
            {
                accentBar.color = route.rating switch
                {
                    "绿" => UITheme.Success,
                    "黄" => UITheme.Warning,
                    "红" => UITheme.Danger,
                    _ => UITheme.Accent
                };
            }
        }
    }
}
