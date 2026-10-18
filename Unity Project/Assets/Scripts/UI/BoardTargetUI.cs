using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// n20 董事会（L1）：年度目标追踪 + 董事成员卡（派系/满意度）+ 派系分布 + 投票/结算入口。
    /// </summary>
    public class BoardTargetUI : MonoBehaviour
    {
        private Transform _listRoot;
        private Transform _memberRoot;
        private TextMeshProUGUI _headerText;
        private TextMeshProUGUI _summaryText;
        private TextMeshProUGUI _factionText;
        private readonly List<GameObject> _rows = new List<GameObject>();

        public static BoardTargetUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Board", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));

            var ui = panel.AddComponent<BoardTargetUI>();

            ui._headerText = UIFactory.Label(panel.transform, "Title", "董事会", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.9f), new Vector2(0.6f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            ui._summaryText = UIFactory.Label(panel.transform, "Summary",
                "第 1 财年目标清单。完成率在 Q4 结束后结算。", 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.82f), new Vector2(0.6f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            // 左侧：目标清单
            var listHost = UIFactory.Panel(panel.transform, "TargetList",
                new UIFactory.Anchor(new Vector2(0.03f, 0.04f), new Vector2(0.6f, 0.80f), Vector2.zero, Vector2.zero),
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

            // 右侧：成员卡 + 派系 + 入口
            UIFactory.Label(panel.transform, "MemberTitle", "董事会成员", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.62f, 0.9f), new Vector2(0.97f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            var memberHost = UIFactory.Panel(panel.transform, "MemberList",
                new UIFactory.Anchor(new Vector2(0.62f, 0.34f), new Vector2(0.97f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            ui._memberRoot = memberHost.transform;

            ui._factionText = UIFactory.Label(panel.transform, "Faction", "", 12,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.62f, 0.24f), new Vector2(0.97f, 0.32f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            UIFactory.Button(panel.transform, "BudgetVote", "预算投票",
                new UIFactory.Anchor(new Vector2(0.62f, 0.12f), new Vector2(0.79f, 0.21f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => OpenPage("n22_年度预算编制_审批"), 13);
            UIFactory.Button(panel.transform, "YearEnd", "财年结算 / 续聘",
                new UIFactory.Anchor(new Vector2(0.80f, 0.12f), new Vector2(0.97f, 0.21f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => OpenPage("n26_财年结算_续聘_解雇"), 13);

            UIFactory.Label(panel.transform, "VoteHint", "预算投票 → 财务审批　·　财年结算 → 续聘/解雇", 11,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.62f, 0.06f), new Vector2(0.97f, 0.11f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            return ui;
        }

        private static void OpenPage(string pageId)
        {
            var nav = Object.FindFirstObjectByType<PrefabNavIntegration>();
            if (nav != null) nav.OpenSubPage(pageId);
        }

        public void Refresh()
        {
            var gs = GameState.Instance;
            if (BoardManager.Instance != null)
                BoardManager.Instance.RefreshTargetProgress();

            RebuildTargets();
            RebuildMembers();
        }

        private void RebuildTargets()
        {
            var gs = GameState.Instance;
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
                _rows.Add(BuildTargetRow(gs.currentTargets[i]));
        }

        private GameObject BuildTargetRow(BoardTarget target)
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

        private void RebuildMembers()
        {
            if (_memberRoot == null) return;
            for (int i = _memberRoot.childCount - 1; i >= 0; i--)
                Destroy(_memberRoot.GetChild(i).gameObject);

            var members = DataBase.Instance != null ? DataBase.Instance.BoardMembers : null;
            if (members == null || members.Count == 0)
            {
                UIFactory.Label(_memberRoot, "Empty", "暂无董事数据。", 13, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), UITheme.TextMuted);
                return;
            }

            var factionCounts = new Dictionary<string, int>();
            for (int i = 0; i < members.Count; i++)
            {
                string f = members[i].faction ?? "其他";
                factionCounts.TryGetValue(f, out int c);
                factionCounts[f] = c + 1;
            }

            float y = 1f;
            float rowH = 1f / members.Count;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                float sat = 50f;
                if (BoardManager.Instance != null && BoardManager.Instance.memberSatisfaction.TryGetValue(m.id, out float v))
                    sat = v;

                var row = UIFactory.Panel(_memberRoot, "Member_" + m.id,
                    new UIFactory.Anchor(new Vector2(0f, y - rowH), new Vector2(1f, y), Vector2.zero, Vector2.zero),
                    new Color(0, 0, 0, 0), rounded: false);
                row.GetComponent<Image>().raycastTarget = false;

                UIFactory.Label(row.transform, "Name", $"{m.id} {m.fullName}", 12,
                    TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.02f, 0.42f), new Vector2(0.62f, 0.95f), Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary);
                UIFactory.Label(row.transform, "Faction", m.faction, 10,
                    TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.62f, 0.45f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);

                var track = UIFactory.Panel(row.transform, "Track",
                    new UIFactory.Anchor(new Vector2(0.64f, 0.28f), new Vector2(0.97f, 0.72f), Vector2.zero, Vector2.zero),
                    new Color(0.05f, 0.08f, 0.12f, 1f));
                var fillGo = UIFactory.Panel(track.transform, "Fill", UIFactory.Fill, SatColor(sat));
                var fill = fillGo.GetComponent<Image>();
                fill.sprite = UIFactory.RoundedSprite;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillAmount = Mathf.Clamp01(sat / 100f);

                y -= rowH;
            }

            if (_factionText != null)
            {
                var sb = new System.Text.StringBuilder("派系分布：");
                foreach (var kv in factionCounts)
                    sb.Append($" {kv.Key} {kv.Value}席");
                _factionText.text = sb.ToString();
            }
        }

        private static Color SatColor(float sat)
        {
            if (sat >= 70f) return UITheme.Success;
            if (sat >= 40f) return UITheme.Warning;
            return UITheme.Danger;
        }
    }
}
