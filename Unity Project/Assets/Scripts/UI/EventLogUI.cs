using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>Phase 12 · 最近随机事件日志（运营中心入口）。</summary>
    public class EventLogUI : MonoBehaviour
    {
        private TextMeshProUGUI _body;
        private GameObject _root;

        public static EventLogUI Build(Transform parent, UIFactory.Anchor anchor)
        {
            var panel = UIFactory.Panel(parent, "EventLogUI", anchor, UITheme.PanelDeep);
            UIFactory.AddBorder(panel, UITheme.Accent, new Vector2(2f, -2f));
            var ui = panel.AddComponent<EventLogUI>();
            ui._root = panel;
            ui.BuildInternal(panel.transform);
            return ui;
        }

        private void OnEnable()
        {
            if (RandomEventManager.Instance != null)
                RandomEventManager.Instance.OnEventLogChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (RandomEventManager.Instance != null)
                RandomEventManager.Instance.OnEventLogChanged -= Refresh;
        }

        private void BuildInternal(Transform t)
        {
            UIFactory.Label(t, "Title", "事件日志 · 近 20 条", 18, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.9f), new Vector2(0.62f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Button(t, "Close", "关闭",
                new UIFactory.Anchor(new Vector2(0.72f, 0.9f), new Vector2(0.97f, 0.98f), Vector2.zero, Vector2.zero),
                UIFactory.ModalButton, () =>
                {
                    if (_root != null) _root.SetActive(false);
                }, 13);

            // 可滚动正文，不溢出窗框
            _body = UIFactory.ScrollableLabel(t, "Body",
                new UIFactory.Anchor(new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted, 13);
        }

        public void Show()
        {
            if (_root != null)
            {
                _root.SetActive(true);
                _root.transform.SetAsLastSibling();
            }
            Refresh();
        }

        public void Refresh()
        {
            if (_body == null) return;
            var gs = GameState.Instance;
            if (gs?.randomEventLog == null || gs.randomEventLog.Count == 0)
            {
                string pending = "";
                if (gs != null && !string.IsNullOrEmpty(gs.pendingChainEventId))
                    pending = $"\n\n预约连锁：{gs.pendingChainEventId}（{gs.pendingChainQuarters} 季后）";
                _body.text = "暂无随机事件记录。推进季度后会出现。" + pending;
                ResetScroll();
                return;
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(gs.pendingChainEventId))
                sb.AppendLine($"⏳ 预约连锁：{gs.pendingChainEventId}（{gs.pendingChainQuarters} 季后）\n");

            for (int i = gs.randomEventLog.Count - 1; i >= 0; i--)
            {
                var e = gs.randomEventLog[i];
                if (e == null) continue;
                sb.AppendLine($"Y{e.year}Q{e.quarter} · {e.category} · {e.title}");
                if (!string.IsNullOrEmpty(e.effectSummary))
                    sb.AppendLine($"  → {e.effectSummary}");
                sb.AppendLine();
            }
            _body.text = sb.ToString().TrimEnd();
            ResetScroll();
        }

        private void ResetScroll()
        {
            var scroll = _body != null ? _body.GetComponentInParent<ScrollRect>() : null;
            if (scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 1f;
            }
        }
    }
}
