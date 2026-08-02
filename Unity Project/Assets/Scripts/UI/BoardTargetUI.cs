using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>董事会页面：年度目标清单 + 红/黄/绿进度条。</summary>
    public class BoardTargetUI : MonoBehaviour
    {
        private Transform _listRoot;
        private TextMeshProUGUI _headerText;
        private TextMeshProUGUI _summaryText;
        private readonly List<GameObject> _rows = new List<GameObject>();

        public static BoardTargetUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Board", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));

            var ui = panel.AddComponent<BoardTargetUI>();

            ui._headerText = UIFactory.Label(panel.transform, "Title", "董事会 · 年度考核目标", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.9f), new Vector2(0.7f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            ui._summaryText = UIFactory.Label(panel.transform, "Summary",
                "第 1 财年目标清单。完成率在 Q4 结束后结算。", 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.82f), new Vector2(0.97f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var listHost = UIFactory.Panel(panel.transform, "TargetList",
                new UIFactory.Anchor(new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.8f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            var vlg = listHost.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 12;
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
            if (BoardManager.Instance != null)
                BoardManager.Instance.RefreshTargetProgress();

            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null) Destroy(_rows[i]);
            }
            _rows.Clear();

            if (gs == null || gs.currentTargets == null || gs.currentTargets.Count == 0)
            {
                if (_summaryText != null)
                    _summaryText.text = "尚未下达年度目标。";
                var empty = UIFactory.Label(_listRoot, "Empty", "等待董事会下达考核清单…", 16,
                    TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(0, 0.7f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
                _rows.Add(empty.gameObject);
                return;
            }

            int done = 0;
            for (int i = 0; i < gs.currentTargets.Count; i++)
                if (gs.currentTargets[i].completed) done++;

            if (_summaryText != null)
            {
                _summaryText.text =
                    $"第 {gs.targetsFiscalYear} 年 · 共 {gs.currentTargets.Count} 项目标" +
                    $"（已完成 {done}）· 整体满意度 {gs.boardSatisfaction:F0}";
            }

            for (int i = 0; i < gs.currentTargets.Count; i++)
                _rows.Add(BuildRow(gs.currentTargets[i]));
        }

        private GameObject BuildRow(BoardTarget target)
        {
            var row = UIFactory.Panel(_listRoot, "Target_" + target.id,
                new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                UITheme.Panel);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0, 88);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 88;
            le.flexibleWidth = 1;

            string badge = target.isGuarantee ? "保底" : (target.sourceNote ?? "化学");
            UIFactory.Label(row.transform, "Badge", badge, 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.7f), new Vector2(0.2f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.Accent);

            var status = target.completed ? "已完成" : "进行中";
            var statusColor = target.completed ? UITheme.Success : UITheme.TextMuted;
            UIFactory.Label(row.transform, "Status", status, 13, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.7f, 0.7f), new Vector2(0.97f, 0.95f), Vector2.zero, Vector2.zero),
                statusColor);

            UIFactory.Label(row.transform, "Title", target.ProgressLabel, 16, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.38f), new Vector2(0.97f, 0.72f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            // 进度条轨道
            var track = UIFactory.Panel(row.transform, "Track",
                new UIFactory.Anchor(new Vector2(0.03f, 0.12f), new Vector2(0.97f, 0.32f), Vector2.zero, Vector2.zero),
                new Color(0.05f, 0.08f, 0.12f, 1f));
            var fillGo = UIFactory.Panel(track.transform, "Fill", UIFactory.Fill, ProgressColor(target));
            var fill = fillGo.GetComponent<Image>();
            fill.sprite = UIFactory.RoundedSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = Mathf.Clamp01(target.Progress01);

            return row;
        }

        private static Color ProgressColor(BoardTarget t)
        {
            if (t.completed) return UITheme.Success;
            float p = t.Progress01;
            if (p < 0.4f) return UITheme.Danger;
            if (p < 0.75f) return UITheme.Warning;
            return UITheme.Success;
        }
    }
}
