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
    /// n104 补条件表单（L5）：来源议题摘要 + 条件清单（可完成标记）+ 机会成本预估 + 提交/取消。
    /// </summary>
    public class SupplementConditionPanel : BasePanel
    {
        Transform _content;
        TextMeshProUGUI _summaryLabel;
        TextMeshProUGUI _estimateLabel;
        TextMeshProUGUI _toast;

        public static SupplementConditionPanel Build()
        {
            var root = UIFactory.Panel(null, "SupplementCondition", UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.10f, 0.86f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Warning, new Vector2(1.5f, -1.5f));
            var cg = card.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(card.transform, "Title", "补条件表单", 21,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.90f), new Vector2(0.60f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.90f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            var back = UIFactory.Button(card.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.18f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var summary = UIFactory.Label(card.transform, "Summary", "", 14,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.79f), new Vector2(0.96f, 0.89f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            summary.textWrappingMode = TextWrappingModes.Normal;

            // 左侧：条件清单
            var listLabel = UIFactory.Label(card.transform, "ListLabel", "条件清单（点击行标记完成）", 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.72f), new Vector2(0.60f, 0.78f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            var content = UIFactory.Panel(card.transform, "Content",
                new UIFactory.Anchor(new Vector2(0.04f, 0.12f), new Vector2(0.60f, 0.71f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;

            // 右侧：机会成本 / 连锁影响
            var estimate = UIFactory.ScrollableLabel(card.transform, "Estimate",
                new UIFactory.Anchor(new Vector2(0.62f, 0.12f), new Vector2(0.96f, 0.71f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted, 13);

            // 底部操作
            var submitBtn = UIFactory.Button(card.transform, "Submit", "提交补条件",
                new UIFactory.Anchor(new Vector2(0.30f, 0.02f), new Vector2(0.50f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.Success, () => { }, 14);
            var cancelBtn = UIFactory.Button(card.transform, "Cancel", "取消",
                new UIFactory.Anchor(new Vector2(0.52f, 0.02f), new Vector2(0.70f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);
            var templateBtn = UIFactory.Button(card.transform, "Template", "模板库",
                new UIFactory.Anchor(new Vector2(0.72f, 0.02f), new Vector2(0.88f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 12);

            var toast = UIFactory.Label(card.transform, "Toast", "", 12,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.20f, 0.005f), new Vector2(0.80f, 0.03f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            var panel = root.AddComponent<SupplementConditionPanel>();
            panel._content = content.transform;
            panel._summaryLabel = summary;
            panel._estimateLabel = estimate;
            panel._toast = toast;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n104_补条件表单", "补条件表单");

            submitBtn.onClick.RemoveAllListeners();
            submitBtn.onClick.AddListener(panel.Submit);
            cancelBtn.onClick.RemoveAllListeners();
            cancelBtn.onClick.AddListener(panel.CancelForm);
            templateBtn.onClick.RemoveAllListeners();
            templateBtn.onClick.AddListener(panel.LoadTemplate);
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
            if (_content == null)
            {
                var c = transform.Find("Card/Content");
                if (c != null) _content = c;
            }
            if (_summaryLabel == null)
            {
                var s = transform.Find("Card/Summary");
                if (s != null) _summaryLabel = s.GetComponent<TextMeshProUGUI>();
            }
            if (_estimateLabel == null)
            {
                var e = transform.Find("Card/Estimate_Viewport/Estimate_Content");
                if (e != null) _estimateLabel = e.GetComponent<TextMeshProUGUI>();
            }
            if (_toast == null)
            {
                var t = transform.Find("Card/Toast");
                if (t != null) _toast = t.GetComponent<TextMeshProUGUI>();
            }
        }

        SupplementForm Form => DecisionMeetingManager.Instance?.ActiveSupplement;

        public void RefreshBody()
        {
            ResolveRefs();
            var form = Form;
            if (form == null)
            {
                if (_summaryLabel != null) _summaryLabel.text = "暂无待补条件的议题。";
                if (_content != null) Clear(_content);
                if (_estimateLabel != null) _estimateLabel.text = "—";
                return;
            }

            if (_summaryLabel != null)
                _summaryLabel.text = $"来源议题：{form.topic}\n暂缓原因：{form.reason}\n发起部门：{form.sourceDept}";

            if (_content != null)
            {
                Clear(_content);
                BuildConditionRows(form);
            }

            if (_estimateLabel != null)
                _estimateLabel.text = BuildEstimateText(form);
        }

        void BuildConditionRows(SupplementForm form)
        {
            float y = 1f;
            float step = 1f / Mathf.Max(1, form.conditions.Count);
            for (int i = 0; i < form.conditions.Count; i++)
            {
                var c = form.conditions[i];
                var row = UIFactory.Panel(_content, "Cond_" + c.id,
                    new UIFactory.Anchor(new Vector2(0f, y - step), new Vector2(1f, y), Vector2.zero, Vector2.zero),
                    c.done ? new Color(0.06f, 0.16f, 0.10f, 1f) : UITheme.PanelDeep);

                string mark = c.done ? "✓" : "○";
                var btn = UIFactory.Button(row.transform, "Toggle", mark,
                    new UIFactory.Anchor(new Vector2(0.01f, 0.3f), new Vector2(0.06f, 0.92f), Vector2.zero, Vector2.zero),
                    c.done ? UITheme.Success : UITheme.Panel, () => Toggle(c.id), 14);

                UIFactory.Label(row.transform, "Desc", c.description, 13,
                    TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.08f, 0.44f), new Vector2(0.98f, 0.92f), Vector2.zero, Vector2.zero),
                    c.done ? UITheme.TextMuted : UITheme.TextPrimary);

                UIFactory.Label(row.transform, "Meta",
                    $"{DepartmentCatalog.DisplayName(c.ownerDept)} · {DecisionEconomics.PriorityLabel(c.priority)} · 截止 {c.deadline}",
                    11, TextAlignmentOptions.TopLeft,
                    new UIFactory.Anchor(new Vector2(0.08f, 0.02f), new Vector2(0.98f, 0.42f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);

                y -= step;
            }
        }

        string BuildEstimateText(SupplementForm form)
        {
            int remaining = 0;
            for (int i = 0; i < form.conditions.Count; i++)
                if (!form.conditions[i].done) remaining++;

            var fake = new DecisionProposal { topic = form.topic, category = form.category };
            string loss = DecisionEconomics.DeferLossText(fake);

            return "■ 条件完成度预估\n" +
                   $"  剩余 {remaining}/{form.conditions.Count} 项未完成\n" +
                   $"  预计会议重开：约 {Mathf.Max(1, remaining)} 个季度\n\n" +
                   "■ 机会成本 / 延期损失\n" +
                   $"  {loss}\n\n" +
                   "■ 连锁影响\n" +
                   "  若条件逾期：上游运力/市场份额受限 → 现金流承压 → 董事会满意度下降。\n\n" +
                   "提交后：按责任部门发送收件箱任务；全部完成后自动提醒「可重新召集会议」。";
        }

        void Toggle(string conditionId)
        {
            var mgr = DecisionMeetingManager.Instance;
            if (mgr == null) return;
            mgr.ToggleCondition(Form, conditionId);
            RefreshBody();
            Toast(mgr.AllDone(Form) ? "所有条件已完成，可重新召集会议。" : "");
        }

        void Submit()
        {
            var mgr = DecisionMeetingManager.Instance;
            if (mgr == null) return;
            var form = Form;
            if (form == null) { BackOne(); return; }
            bool allDone = mgr.AllDone(form);
            GameState.Instance?.AddDecisionLog($"补条件已提交：{form.topic}（完成 {DoneCount(form)}/{form.conditions.Count} 项）。");
            GameState.Instance?.NotifyChanged();
            Toast(allDone ? "已提交 · 条件齐备，可重新召集会议。" : "已提交 · 条件分发到责任部门。");
            // 提交后关闭表单，回上一会议页（状态变为「条件补充中」）
            Invoke(nameof(BackOneAfterSubmit), 0.4f);
        }

        void BackOneAfterSubmit()
        {
            DecisionMeetingManager.Instance?.ClearSupplement();
            BackOne();
        }

        void CancelForm()
        {
            DecisionMeetingManager.Instance?.ClearSupplement();
            BackOne();
        }

        /// <summary>右上角 ×：只退本表单层，不得清空导航栈（否则底层会议/邮件页被一并弹出，出现空白页）。</summary>
        public override void CloseOverlay()
        {
            DecisionMeetingManager.Instance?.ClearSupplement();
            BackOne();
            var router = AirlineCEO.UI.Navigation.NavigationRouter.Instance;
            if (router == null) return;
            bool stillOnForm = router.CurrentPageId != null && router.CurrentPageId.StartsWith("n104");
            if (router.StackDepth == 0 || stillOnForm)
                Object.FindFirstObjectByType<PrefabNavIntegration>()?.SelectTab("n7_收件箱");
        }

        void LoadTemplate()
        {
            var form = Form;
            if (form == null) return;
            // 重新载入该类别默认模板（作为「模板库」快捷入口的简化实现）
            form.conditions = DecisionEconomics.ConditionTemplates(form.category);
            GameState.Instance?.NotifyChanged();
            RefreshBody();
            Toast("已从模板库载入默认条件模板。");
        }

        int DoneCount(SupplementForm form)
        {
            int n = 0;
            for (int i = 0; i < form.conditions.Count; i++)
                if (form.conditions[i].done) n++;
            return n;
        }

        void Toast(string msg)
        {
            if (_toast != null) _toast.text = msg ?? "";
        }

        void Clear(Transform host)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
                Destroy(host.GetChild(i).gameObject);
        }
    }
}
