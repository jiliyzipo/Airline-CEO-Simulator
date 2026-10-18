using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>n30 随机事件日志（L2）：已触发事件按时间倒序展示。</summary>
    public class EventLogPanelUI : BasePanel
    {
        TextMeshProUGUI _body;

        public static EventLogPanelUI Build()
        {
            var root = UIFactory.Panel(null, "EventLogPanel", UIFactory.Fill, UITheme.Panel);
            var cg = root.AddComponent<CanvasGroup>();
            var title = UIFactory.Label(root.transform, "Title", "随机事件日志", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.92f), new Vector2(0.6f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(root.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.92f, 0.92f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            var back = UIFactory.Button(root.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.03f, 0.02f), new Vector2(0.16f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var body = UIFactory.ScrollableLabel(root.transform, "Body",
                new UIFactory.Anchor(new Vector2(0.03f, 0.10f), new Vector2(0.97f, 0.90f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted, 15);

            var panel = root.AddComponent<EventLogPanelUI>();
            panel._body = body;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n30_随机事件日志", "随机事件日志");

            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(panel.BackOne);
            return panel;
        }

        protected override void Awake()
        {
            base.Awake();
            ResolveRefs();
        }

        void OnEnable() { ResolveRefs(); RefreshBody(); }
        public override void Show() { base.Show(); ResolveRefs(); RefreshBody(); }

        void ResolveRefs()
        {
            if (_body == null)
            {
                var b = transform.Find("Body_Viewport/Body_Content");
                if (b != null) _body = b.GetComponent<TextMeshProUGUI>();
            }
        }

        public void RefreshBody()
        {
            ResolveRefs();
            var gs = GameState.Instance;
            if (gs == null || _body == null) return;

            if (gs.randomEventLog == null || gs.randomEventLog.Count == 0)
            {
                _body.text = "暂无随机事件记录。\n\n随机事件每个季度结算时触发，会带来收益/成本波动，请在「随机事件」弹窗中及时决策。";
                return;
            }

            var sb = new System.Text.StringBuilder();
            for (int i = gs.randomEventLog.Count - 1; i >= 0; i--)
            {
                var e = gs.randomEventLog[i];
                sb.AppendLine($"<b>第 {e.year} 年 Q{e.quarter}　{e.category}</b>");
                sb.AppendLine($"　{e.title}");
                if (!string.IsNullOrEmpty(e.effectSummary))
                    sb.AppendLine($"　效果：{e.effectSummary}");
                if (!string.IsNullOrEmpty(e.summary))
                    sb.AppendLine($"　{e.summary}");
                sb.AppendLine();
            }

            _body.text = sb.ToString();
        }
    }
}
