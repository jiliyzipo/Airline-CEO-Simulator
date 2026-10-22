using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
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
            card.metaText = UIFactory.Label(root.transform, "Meta", "第 1 年 Q1 开航 · 7 班/周 · 周需（人次/周）商务 1,200 · 超经 900 · 经济 3,400　｜　货邮 4.0 吨/周", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.08f), new Vector2(0.96f, 0.48f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            return card;
        }

        public void Bind(RouteData route)
        {
            if (route == null) return;

            // 无机可派（未指派 / 已退役转售 / 停场 / 已投入过渡业务）：该线不产生航班，也照实标出来，
            // 免得 CEO 在列表里只看到「日利 0.0 万」却找不到原因（旧弊端：机号照旧显示、幽灵航班照飞）。
            var ground = FlightGroundLogic.RouteOf(GameState.Instance, route);
            bool grounded = FlightGroundLogic.IsPersistent(ground);

            if (titleText != null)
            {
                // 一条航线是一条双向的往返班：去程 + 过站 + 回程。计划类页面（本卡片、周总计划、
                // 单班页）一律用 ⇄；单向 → 会让人以为只飞这一头（2026-10-19 CEO 报障）。
                string od = AirportCatalog.OdRoundTrip(route.origin, route.destination);
                titleText.text = grounded
                    ? $"{od}　⚠ {FlightGroundLogic.Label(ground)}"
                    : od;
                titleText.color = grounded ? UITheme.Warning : UITheme.TextPrimary;
            }
            if (profitText != null)
            {
                // 停飞线不得在列表里照报「日利 X 万」：那正是幽灵收入的界面版（季结也按同口径剔除），
                // 故此处直接换成停飞提示，潜在日利请进详情页看（详情页保留设计口径并标注不计营收）。
                if (grounded)
                {
                    profitText.text = "停飞不计营收";
                    profitText.color = UITheme.Warning;
                }
                else
                {
                    profitText.text = $"日利 {route.dailyProfit:F1} 万";
                    profitText.color = route.dailyProfit < 0f ? UITheme.Danger : UITheme.Success;
                }
            }
            if (metaText != null)
            {
                var g = RouteGravityModel.EvaluateRoute(GameState.Instance, route);
                // 开航时间与每周班次都走 RouteData 的一行式（哨兵值「接管前」不得当日期用；
                // 离港/值勤两侧恒等时只报一个数字，见 RouteData.WeeklyLine）。
                metaText.text =
                    $"{RouteData.OpenedText(route.openedDate)} · {route.WeeklyLine()} · {g.FormatOneLine()}";
            }
            if (accentBar != null)
            {
                accentBar.color = grounded
                    ? UITheme.Warning
                    : route.rating switch
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
