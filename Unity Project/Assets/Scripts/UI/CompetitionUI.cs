using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>Phase 11 · 市场面板「竞争态势」。</summary>
    public class CompetitionUI : MonoBehaviour
    {
        private TextMeshProUGUI _summaryText;
        private TextMeshProUGUI _logText;
        private Transform _rowsRoot;
        private GameObject _codeShareBar;
        private TextMeshProUGUI _codeShareMsg;

        private static readonly Color[] AiPalette =
        {
            new Color(0.85f, 0.45f, 0.35f),
            new Color(0.4f, 0.7f, 0.85f),
            new Color(0.75f, 0.7f, 0.35f),
            new Color(0.55f, 0.55f, 0.75f),
            new Color(0.5f, 0.75f, 0.55f)
        };

        public static CompetitionUI Build(Transform parent, UIFactory.Anchor anchor)
        {
            var root = UIFactory.Panel(parent, "CompetitionUI", anchor, UITheme.PanelDeep);
            var ui = root.AddComponent<CompetitionUI>();
            ui.BuildInternal(root.transform);
            return ui;
        }

        private void OnEnable()
        {
            if (CompetitionManager.Instance != null)
            {
                CompetitionManager.Instance.OnCompetitionUpdated += Refresh;
                CompetitionManager.Instance.OnCodeShareProposed += OnProposal;
            }
            if (AIManager.Instance != null)
                AIManager.Instance.OnAiChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (CompetitionManager.Instance != null)
            {
                CompetitionManager.Instance.OnCompetitionUpdated -= Refresh;
                CompetitionManager.Instance.OnCodeShareProposed -= OnProposal;
            }
            if (AIManager.Instance != null)
                AIManager.Instance.OnAiChanged -= Refresh;
        }

        private void BuildInternal(Transform t)
        {
            UIFactory.Label(t, "CompTitle", "竞争态势", 18, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.92f), new Vector2(0.5f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Button(t, "RefreshBtn", "刷新份额",
                new UIFactory.Anchor(new Vector2(0.72f, 0.92f), new Vector2(0.98f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () =>
                {
                    if (CompetitionManager.Instance != null)
                        CompetitionManager.Instance.RecalculateMarketShares(GameState.Instance);
                    Refresh();
                }, 13);

            _summaryText = UIFactory.Label(t, "ShareSummary", "市场份额计算中…", 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.84f), new Vector2(0.7f, 0.91f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            // 统一行列表：左份额条 + 右详情，同一行一一对齐
            _rowsRoot = UIFactory.Panel(t, "CompRows",
                new UIFactory.Anchor(new Vector2(0.02f, 0.28f), new Vector2(0.98f, 0.83f), Vector2.zero, Vector2.zero),
                new Color(0.08f, 0.12f, 0.14f, 0.6f)).transform;

            _logText = UIFactory.Label(t, "CompLog", "季度竞争日志将显示在此。", 13,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.48f, 0.26f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            _codeShareBar = UIFactory.Panel(t, "CodeShareBar",
                new UIFactory.Anchor(new Vector2(0.5f, 0.02f), new Vector2(0.98f, 0.26f), Vector2.zero, Vector2.zero),
                new Color(0.12f, 0.18f, 0.14f, 0.95f));
            _codeShareMsg = UIFactory.Label(_codeShareBar.transform, "Msg", "", 13,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.38f), new Vector2(0.97f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            UIFactory.Button(_codeShareBar.transform, "AcceptCS", "接受",
                new UIFactory.Anchor(new Vector2(0.03f, 0.05f), new Vector2(0.32f, 0.34f), Vector2.zero, Vector2.zero),
                UITheme.Success, () =>
                {
                    CompetitionManager.Instance?.AcceptCodeShare();
                    Refresh();
                }, 13);
            UIFactory.Button(_codeShareBar.transform, "RejectCS", "拒绝",
                new UIFactory.Anchor(new Vector2(0.35f, 0.05f), new Vector2(0.64f, 0.34f), Vector2.zero, Vector2.zero),
                UITheme.Warning, () =>
                {
                    CompetitionManager.Instance?.RejectCodeShare();
                    Refresh();
                }, 13);
            UIFactory.Button(_codeShareBar.transform, "BetrayCS", "撕毁协议",
                new UIFactory.Anchor(new Vector2(0.67f, 0.05f), new Vector2(0.97f, 0.34f), Vector2.zero, Vector2.zero),
                UITheme.Danger, () =>
                {
                    CompetitionManager.Instance?.BetrayCodeShare();
                    Refresh();
                }, 12);
            _codeShareBar.SetActive(false);
        }

        private void OnProposal(CodeShareProposal p)
        {
            if (p == null) return;
            Refresh();
        }

        public void Refresh()
        {
            var gs = GameState.Instance;
            if (gs == null || _summaryText == null) return;

            if (CompetitionManager.Instance != null)
                CompetitionManager.Instance.RecalculateMarketShares(gs);

            float brand = gs.brandAwareness;
            string cs = gs.codeShareActive
                ? $"代码共享中（+{gs.codeShareLoadBonus:F0}% 客座）"
                : "无代码共享";
            _summaryText.text =
                $"你的份额 {gs.playerMarketShare:F1}%  ·  品牌认知 {brand:F0}  ·  {cs}";

            if (_logText != null)
                _logText.text = string.IsNullOrEmpty(gs.lastCompetitionLog)
                    ? "推进季度后更新竞争日志。"
                    : gs.lastCompetitionLog;

            RebuildAlignedRows(gs);
            UpdateCodeShareBar(gs);
        }

        private void RebuildAlignedRows(GameState gs)
        {
            if (_rowsRoot == null) return;
            for (int i = _rowsRoot.childCount - 1; i >= 0; i--)
                Destroy(_rowsRoot.GetChild(i).gameObject);

            var rows = new List<(string title, float share, Color color, string detail, bool isPlayer)>();
            rows.Add((
                "你",
                gs.playerMarketShare,
                UITheme.Accent,
                "本司市场地位（ASK 估算）。开通更多航线 / 扩充机队可提升份额。",
                true));

            if (gs.aiAirlines != null)
            {
                int pi = 0;
                for (int i = 0; i < gs.aiAirlines.Count; i++)
                {
                    var ai = gs.aiAirlines[i];
                    if (ai == null || !ai.isAlive) continue;
                    string predator = ai.isPredator ? " · 掠食者" : "";
                    string hate = ai.hatesPlayer ? " · 结怨" : "";
                    string detail =
                        $"{AIAirlineTypes.DisplayName(ai.type)}{predator}{hate}\n" +
                        $"状态 {AIAirlineStates.DisplayName(ai.state)} · 现金 {ai.cash:F0}万 · " +
                        $"机队 {ai.fleetSize} · 均价 {ai.avgFare:F0} · 航线 {(ai.routes != null ? ai.routes.Length : 0)}";
                    rows.Add((ai.name, ai.marketShare, AiPalette[pi % AiPalette.Length], detail, false));
                    pi++;
                }
            }

            int n = rows.Count;
            if (n == 0)
            {
                UIFactory.Label(_rowsRoot, "Empty", "暂无数据", 14, TextAlignmentOptions.Center,
                    UIFactory.Fill, UITheme.TextMuted);
                return;
            }

            for (int i = 0; i < n; i++)
            {
                float y1 = 1f - (i + 1f) / n;
                float y2 = 1f - i / (float)n;
                var pad = 0.015f;
                var rowBg = rows[i].isPlayer
                    ? new Color(0.1f, 0.18f, 0.22f, 0.9f)
                    : new Color(0.1f, 0.14f, 0.16f, 0.85f);
                var row = UIFactory.Panel(_rowsRoot, $"Row_{i}",
                    new UIFactory.Anchor(new Vector2(0.01f, y1 + pad), new Vector2(0.99f, y2 - pad), Vector2.zero, Vector2.zero),
                    rowBg);

                // 左：名称 + 份额条 + %
                UIFactory.Label(row.transform, "Name", rows[i].title, 13, TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.01f, 0.55f), new Vector2(0.18f, 0.95f), Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary, FontStyles.Bold);

                var track = UIFactory.Panel(row.transform, "Track",
                    new UIFactory.Anchor(new Vector2(0.01f, 0.12f), new Vector2(0.32f, 0.48f), Vector2.zero, Vector2.zero),
                    new Color(0.15f, 0.2f, 0.22f));
                float w = Mathf.Clamp01(rows[i].share / 100f);
                UIFactory.Panel(track.transform, "Fill",
                    new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(w, 1f), Vector2.zero, Vector2.zero),
                    rows[i].color);

                UIFactory.Label(row.transform, "Pct", $"{rows[i].share:F1}%", 12, TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.33f, 0.12f), new Vector2(0.42f, 0.48f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);

                // 右：详情（与左侧同一行）
                UIFactory.Label(row.transform, "Detail", rows[i].detail, 12, TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.44f, 0.08f), new Vector2(0.98f, 0.92f), Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary);
            }
        }

        private void UpdateCodeShareBar(GameState gs)
        {
            if (_codeShareBar == null) return;
            bool pending = !string.IsNullOrEmpty(gs.pendingCodeShareFromId);
            bool active = gs.codeShareActive;
            _codeShareBar.SetActive(pending || active);

            if (pending)
            {
                string name = gs.pendingCodeShareFromId;
                if (gs.aiAirlines != null)
                {
                    for (int i = 0; i < gs.aiAirlines.Count; i++)
                        if (gs.aiAirlines[i]?.id == gs.pendingCodeShareFromId)
                        {
                            name = gs.aiAirlines[i].name;
                            break;
                        }
                }
                _codeShareMsg.text = $"{name} 提议代码共享。接受获客座率加成；拒绝无惩罚；撕毁已生效协议会结怨。";
            }
            else if (active)
            {
                _codeShareMsg.text = "代码共享生效中。可「撕毁协议」背叛对方（将结怨并降价报复）。";
            }
        }
    }
}
