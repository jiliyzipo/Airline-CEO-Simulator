using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// n26 财年结算 / 续聘 / 解雇（L4 · 节奏节点）：
    /// 目标完成率结算预览 + 续聘/重新组建/重开 分支。
    /// </summary>
    public class YearEndSettlementPanel : BasePanel
    {
        TextMeshProUGUI _body;

        public static YearEndSettlementPanel Build()
        {
            var root = UIFactory.Panel(null, "YearEndSettlement", UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.10f, 0.86f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.16f, 0.12f), new Vector2(0.84f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Success, new Vector2(1.5f, -1.5f));
            var cg = card.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(card.transform, "Title", "财年结算 / 续聘 / 解雇", 21,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.05f, 0.90f), new Vector2(0.80f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.90f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            var back = UIFactory.Button(card.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.05f, 0.02f), new Vector2(0.20f, 0.10f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var body = UIFactory.ScrollableLabel(card.transform, "Body",
                new UIFactory.Anchor(new Vector2(0.05f, 0.22f), new Vector2(0.95f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, 15);

            var keepBtn = UIFactory.Button(card.transform, "Keep", "保留本届董事会",
                new UIFactory.Anchor(new Vector2(0.22f, 0.02f), new Vector2(0.46f, 0.10f), Vector2.zero, Vector2.zero),
                UITheme.Success, () => { }, 13);
            var rebuildBtn = UIFactory.Button(card.transform, "Rebuild", "重新组建",
                new UIFactory.Anchor(new Vector2(0.48f, 0.02f), new Vector2(0.70f, 0.10f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 13);
            var restartBtn = UIFactory.Button(card.transform, "Restart", "重新开始 / 读档",
                new UIFactory.Anchor(new Vector2(0.72f, 0.02f), new Vector2(0.95f, 0.10f), Vector2.zero, Vector2.zero),
                UITheme.Danger, () => { }, 12);

            var panel = root.AddComponent<YearEndSettlementPanel>();
            panel._body = body;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n26_财年结算_续聘_解雇", "财年结算 / 续聘 / 解雇");

            keepBtn.onClick.RemoveAllListeners();
            keepBtn.onClick.AddListener(panel.Keep);
            rebuildBtn.onClick.RemoveAllListeners();
            rebuildBtn.onClick.AddListener(panel.Rebuild);
            restartBtn.onClick.RemoveAllListeners();
            restartBtn.onClick.AddListener(panel.Restart);
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(panel.BackOne);
            return panel;
        }

        protected override void Awake()
        {
            base.Awake();
            ResolveRefs();
        }

        void OnEnable()
        {
            ResolveRefs();
            RefreshBody();
        }

        public override void Show()
        {
            base.Show();
            ResolveRefs();
            RefreshBody();
        }

        void ResolveRefs()
        {
            if (_body == null)
            {
                var b = transform.Find("Card/Body_Viewport/Body_Content");
                if (b != null) _body = b.GetComponent<TextMeshProUGUI>();
            }
        }

        public void RefreshBody()
        {
            ResolveRefs();
            var gs = GameState.Instance;
            if (gs == null) return;

            if (BoardManager.Instance != null)
                BoardManager.Instance.RefreshTargetProgress();

            var eval = YearEndEvaluator.Evaluate(gs.currentTargets, gs.year, gs.difficulty, gs.ceoBirthYear, gs);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"第 {gs.year} 财年结算预览");
            sb.AppendLine();
            sb.AppendLine("■ 目标完成率");
            sb.AppendLine($"  完成 {eval.targetsCompleted}/{eval.targetsTotal} 项 · 完成率 {eval.completionRate:F0}%");
            sb.AppendLine($"  续聘线：{eval.reappointmentThreshold:F0}%（{DifficultyLabel(eval.difficulty)}）");
            sb.AppendLine();
            sb.AppendLine("■ 目标明细");
            if (gs.currentTargets != null)
            {
                for (int i = 0; i < gs.currentTargets.Count; i++)
                {
                    var t = gs.currentTargets[i];
                    string mark = t.completed ? "✓" : "✗";
                    sb.AppendLine($"  {mark} {t.title}（实际 {BoardTarget.FormatValue(t.currentValue)}{t.unit}）");
                }
            }
            sb.AppendLine();
            sb.AppendLine("■ 判定");
            string outcomeLabel = eval.outcome switch
            {
                VoteOutcome.Reappointed => "续聘成功",
                VoteOutcome.ProbationContinue => "见习保护（再给一年）",
                VoteOutcome.Fired => "解雇 · Game Over",
                VoteOutcome.Retired => "光荣退休",
                _ => "—"
            };
            sb.AppendLine($"  {eval.title}：{outcomeLabel}");
            sb.AppendLine();
            sb.AppendLine($"董事会满意度：{gs.boardSatisfaction:F0}");
            sb.AppendLine();
            sb.AppendLine("说明：正式结算在 Q4 结束后自动触发。此处可预览结果并选择后续分支。");

            if (_body != null) _body.text = sb.ToString();
        }

        static string DifficultyLabel(DifficultyMode d) => d switch
        {
            DifficultyMode.Easy => "简单",
            DifficultyMode.Hard => "困难",
            _ => "标准"
        };

        void Keep()
        {
            var gs = GameState.Instance;
            if (gs != null && BoardManager.Instance != null)
                BoardManager.Instance.GenerateTargetsForYear(gs.year); // 保留本届，刷新目标
            BackOne();
        }

        void Rebuild()
        {
            var gs = GameState.Instance;
            if (gs != null && BoardManager.Instance != null)
            {
                gs.currentTargets?.Clear();
                BoardManager.Instance.GenerateTargetsForYear(gs.year);
            }
            RefreshBody();
        }

        void Restart()
        {
            // 读档 / 重开由顶层处理；此处仅提示
            GameState.Instance?.AddDecisionLog("选择重新开始 / 读档（请使用 F9 读档或主菜单）。");
        }
    }
}
