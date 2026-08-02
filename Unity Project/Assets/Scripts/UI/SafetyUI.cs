using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>安全面板：评级 A/B/C/D + 事件日志（GDD 7.6 · Phase 7）。</summary>
    public class SafetyUI : MonoBehaviour
    {
        private TextMeshProUGUI _ratingLetter;
        private TextMeshProUGUI _ratingDesc;
        private TextMeshProUGUI _summaryText;
        private Transform _listRoot;
        private readonly List<GameObject> _rows = new List<GameObject>();

        public static SafetyUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Safety", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));

            var ui = panel.AddComponent<SafetyUI>();

            UIFactory.Label(panel.transform, "Title", "安全质量 · SAF", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.9f), new Vector2(0.55f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            ui._summaryText = UIFactory.Label(panel.transform, "Summary",
                "每季度约 10% 概率触发安全事件。评级影响航权与董事会考核。", 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.82f), new Vector2(0.7f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var badge = UIFactory.Panel(panel.transform, "RatingBadge",
                new UIFactory.Anchor(new Vector2(0.72f, 0.78f), new Vector2(0.96f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(badge, UITheme.Accent, new Vector2(1.5f, -1.5f));

            ui._ratingLetter = UIFactory.Label(badge.transform, "Letter", "B", 48,
                TextAlignmentOptions.Center, UIFactory.Fill, UITheme.Accent, FontStyles.Bold);
            var letterRt = ui._ratingLetter.GetComponent<RectTransform>();
            letterRt.offsetMin = new Vector2(0, 18);
            letterRt.offsetMax = new Vector2(0, -4);

            UIFactory.Label(badge.transform, "BadgeHint", "安全评级", 12, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.28f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            ui._ratingDesc = UIFactory.Label(panel.transform, "Desc", "", 15,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.68f), new Vector2(0.7f, 0.82f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            UIFactory.Label(panel.transform, "LogTitle", "事件日志", 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.6f), new Vector2(0.5f, 0.68f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var listHost = UIFactory.Panel(panel.transform, "EventList",
                new UIFactory.Anchor(new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.6f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            var vlg = listHost.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 10;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            ui._listRoot = listHost.transform;

            return ui;
        }

        public void Refresh()
        {
            var gs = GameState.Instance;
            SafetyRating rating = gs != null ? gs.safetyRating : SafetyRating.B;

            if (_ratingLetter != null)
            {
                _ratingLetter.text = SafetyEventManager.Letter(rating);
                _ratingLetter.color = SafetyEventManager.ColorFor(rating);
            }

            if (_ratingDesc != null)
                _ratingDesc.text = SafetyEventManager.Describe(rating);

            if (_summaryText != null && gs != null)
            {
                int n = gs.safetyEventLog != null ? gs.safetyEventLog.Count : 0;
                _summaryText.text =
                    $"当前评级 {SafetyEventManager.Letter(rating)} · 累计事件 {n} 次 · 安全指数 {gs.safetyIndex:F0}";
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null) Destroy(_rows[i]);
            }
            _rows.Clear();

            if (gs == null || gs.safetyEventLog == null || gs.safetyEventLog.Count == 0)
            {
                var empty = UIFactory.Label(_listRoot, "Empty", "暂无安全事件。继续运营，每季度约有一成概率触发。", 15,
                    TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(0, 0.6f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
                _rows.Add(empty.gameObject);
                return;
            }

            // 新事件在上
            for (int i = gs.safetyEventLog.Count - 1; i >= 0; i--)
                _rows.Add(BuildRow(gs.safetyEventLog[i]));
        }

        private GameObject BuildRow(SafetyEventLog log)
        {
            var row = UIFactory.Panel(_listRoot, "Evt_" + log.id,
                new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                UITheme.Panel);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0, 72);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 72;
            le.flexibleWidth = 1;

            string sev = log.severity == SafetyEventSeverity.Moderate ? "中度" : "轻度";
            Color sevColor = log.severity == SafetyEventSeverity.Moderate ? UITheme.Warning : UITheme.Accent;
            UIFactory.Label(row.transform, "Sev", sev, 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.65f), new Vector2(0.2f, 0.95f), Vector2.zero, Vector2.zero),
                sevColor);

            UIFactory.Label(row.transform, "Date", log.dateLabel ?? "", 12, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.55f, 0.65f), new Vector2(0.97f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Label(row.transform, "Title", log.title ?? "", 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.32f), new Vector2(0.97f, 0.68f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            string trail =
                $"{SafetyEventManager.Letter(log.ratingBefore)} → {SafetyEventManager.Letter(log.ratingAfter)}";
            UIFactory.Label(row.transform, "Trail", trail, 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.32f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            return row;
        }
    }
}
