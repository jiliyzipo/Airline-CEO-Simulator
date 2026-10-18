using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>n38 历史回放（L2）：决策日志 + 决策会议历史 时间线。</summary>
    public class HistoryReplayPanel : BasePanel
    {
        TextMeshProUGUI _body;

        public static HistoryReplayPanel Build()
        {
            var root = UIFactory.Panel(null, "HistoryReplayPanel", UIFactory.Fill, UITheme.Panel);
            var cg = root.AddComponent<CanvasGroup>();
            var title = UIFactory.Label(root.transform, "Title", "历史回放", 22,
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

            var panel = root.AddComponent<HistoryReplayPanel>();
            panel._body = body;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n38_历史回放", "历史回放");

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

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>■ 决策日志</b>");
            if (gs.decisionLog == null || gs.decisionLog.Count == 0)
            {
                sb.AppendLine("  暂无记录。");
            }
            else
            {
                int start = Mathf.Max(0, gs.decisionLog.Count - 40);
                for (int i = start; i < gs.decisionLog.Count; i++)
                    sb.AppendLine("  · " + gs.decisionLog[i]);
            }

            sb.AppendLine();
            sb.AppendLine("<b>■ 决策会议历史</b>");
            var decision = gs.decision;
            if (decision == null || decision.history == null || decision.history.Count == 0)
            {
                sb.AppendLine("  尚无 CEO 决策会议记录。");
            }
            else
            {
                for (int i = 0; i < decision.history.Count; i++)
                {
                    var h = decision.history[i];
                    sb.AppendLine($"  · [{h.year} Q{h.quarter}] {h.topic} — {h.outcome}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("<b>■ 里程碑</b>");
            sb.AppendLine($"  当前：第 {gs.year} 年 Q{gs.quarter} · 现金 {gs.cash:F0} 万 · 满意度 {gs.boardSatisfaction:F0}");

            _body.text = ChineseFontProvider.Sanitize(sb.ToString());
        }
    }
}
