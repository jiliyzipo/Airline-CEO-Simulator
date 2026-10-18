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
    /// n21 CEO 决策会议（L4 · 全屏覆盖）：
    /// 顶栏议题 + 五部门意见卡（红黄绿 + 量化意见）+ 批准/暂缓/拒绝。
    /// </summary>
    public class DecisionMeetingPanel : BasePanel
    {
        Transform _content;
        TextMeshProUGUI _topicLabel;
        TextMeshProUGUI _verdictLabel;

        public static DecisionMeetingPanel Build()
        {
            var root = UIFactory.Panel(null, "DecisionMeeting", UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.10f, 0.86f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Accent, new Vector2(1.5f, -1.5f));
            var cg = card.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(card.transform, "Title", "CEO 决策会议", 21,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.90f), new Vector2(0.60f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.90f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            var back = UIFactory.Button(card.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.18f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var topic = UIFactory.Label(card.transform, "Topic", "", 16,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.82f), new Vector2(0.96f, 0.89f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            topic.textWrappingMode = TextWrappingModes.Normal;

            var verdict = UIFactory.Label(card.transform, "Verdict", "", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.74f), new Vector2(0.96f, 0.81f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted, FontStyles.Bold);

            var content = UIFactory.Panel(card.transform, "Content",
                new UIFactory.Anchor(new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.73f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;

            // 底部操作
            var approveBtn = UIFactory.Button(card.transform, "Approve", "批准", 
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.28f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.Success, () => { }, 15);
            var deferBtn = UIFactory.Button(card.transform, "Defer", "暂缓（补条件）",
                new UIFactory.Anchor(new Vector2(0.30f, 0.02f), new Vector2(0.56f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.Warning, () => { }, 14);
            var rejectBtn = UIFactory.Button(card.transform, "Reject", "拒绝",
                new UIFactory.Anchor(new Vector2(0.58f, 0.02f), new Vector2(0.78f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.Danger, () => { }, 14);
            var closeMeetingBtn = UIFactory.Button(card.transform, "CloseBtn", "取消会议",
                new UIFactory.Anchor(new Vector2(0.80f, 0.02f), new Vector2(0.96f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 13);

            var panel = root.AddComponent<DecisionMeetingPanel>();
            panel._content = content.transform;
            panel._topicLabel = topic;
            panel._verdictLabel = verdict;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n21_CEO_决策会议", "CEO 决策会议");

            approveBtn.onClick.RemoveAllListeners();
            approveBtn.onClick.AddListener(panel.Approve);
            deferBtn.onClick.RemoveAllListeners();
            deferBtn.onClick.AddListener(panel.Defer);
            rejectBtn.onClick.RemoveAllListeners();
            rejectBtn.onClick.AddListener(panel.Reject);
            closeMeetingBtn.onClick.RemoveAllListeners();
            closeMeetingBtn.onClick.AddListener(panel.CloseMeeting);
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
            if (_topicLabel == null)
            {
                var t = transform.Find("Card/Topic");
                if (t != null) _topicLabel = t.GetComponent<TextMeshProUGUI>();
            }
            if (_verdictLabel == null)
            {
                var v = transform.Find("Card/Verdict");
                if (v != null) _verdictLabel = v.GetComponent<TextMeshProUGUI>();
            }
        }

        public void RefreshBody()
        {
            ResolveRefs();
            var mgr = DecisionMeetingManager.Instance;
            if (mgr == null) return;

            if (mgr.CurrentProposal == null)
                mgr.OpenMeeting(mgr.PrepareDefault());
            else
                mgr.RefreshOpinions();

            var p = mgr.CurrentProposal;
            var opinions = mgr.CurrentOpinions;

            if (_topicLabel != null)
                _topicLabel.text = $"议题：{p.topic}　·　发起：{p.sourceLabel}";

            if (_verdictLabel != null && opinions != null)
            {
                string verdict = DecisionEconomics.Verdict(opinions);
                string label = DecisionEconomics.VerdictLabel(verdict);
                Color vc = verdict switch
                {
                    "approve" => UITheme.Success,
                    "defer" => UITheme.Warning,
                    "reject" => UITheme.Danger,
                    _ => UITheme.TextMuted
                };
                _verdictLabel.text = $"{DecisionEconomics.OpinionSummary(opinions)} · 裁决：{label}";
                _verdictLabel.color = vc;
            }

            if (_content == null) return;
            Clear(_content);

            if (opinions == null) return;
            float y = 1f;
            float step = 0.19f;
            for (int i = 0; i < opinions.Length; i++)
            {
                BuildOpinionRow(opinions[i], y - step, y);
                y -= step;
            }
        }

        void BuildOpinionRow(DeptOpinion o, float y0, float y1)
        {
            Color dot = o.color switch
            {
                DeptOpinionColor.Green => UITheme.Success,
                DeptOpinionColor.Yellow => UITheme.Warning,
                _ => UITheme.Danger
            };
            string dotGlyph = o.color switch
            {
                DeptOpinionColor.Green => "●",
                DeptOpinionColor.Yellow => "●",
                _ => "●"
            };

            var row = UIFactory.Panel(_content, "Opinion_" + o.deptCode,
                new UIFactory.Anchor(new Vector2(0f, y0), new Vector2(1f, y1), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);

            UIFactory.Label(row.transform, "Dept", $"{DepartmentCatalog.DisplayName(o.deptCode)}", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.4f), new Vector2(0.26f, 0.92f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.Label(row.transform, "Dot", dotGlyph, 16,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.27f, 0.3f), new Vector2(0.30f, 0.9f), Vector2.zero, Vector2.zero),
                dot);

            UIFactory.Label(row.transform, "Headline", o.headline, 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.32f, 0.42f), new Vector2(0.98f, 0.90f), Vector2.zero, Vector2.zero),
                o.color == DeptOpinionColor.Red ? UITheme.Danger : UITheme.TextPrimary, FontStyles.Bold);

            var detail = UIFactory.Label(row.transform, "Detail", o.detail, 12,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.32f, 0.04f), new Vector2(0.98f, 0.46f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            detail.textWrappingMode = TextWrappingModes.Normal;
        }

        void Approve()
        {
            var mgr = DecisionMeetingManager.Instance;
            if (mgr == null) return;
            mgr.Approve();
            BackOne();
        }

        void Defer()
        {
            var mgr = DecisionMeetingManager.Instance;
            if (mgr == null) return;
            mgr.Defer();
            // 暂缓 → 打开补条件表单
            Object.FindFirstObjectByType<PrefabNavIntegration>()?.OpenSubPage("n104_补条件表单");
        }

        void Reject()
        {
            var mgr = DecisionMeetingManager.Instance;
            if (mgr == null) return;
            mgr.Reject();
            BackOne();
        }

        void CloseMeeting()
        {
            // 取消：不落任何决策记录，回到发起页（收件箱邮件页）
            CancelAndReturn();
        }

        /// <summary>右上角 × 与「取消会议」同义：只退本会议层，不得清空导航栈（否则底层邮件页被一并弹出，出现空白页）。</summary>
        public override void CloseOverlay()
        {
            CancelAndReturn();
        }

        void CancelAndReturn()
        {
            DecisionMeetingManager.Instance?.Cancel();
            BackOne();
            var router = AirlineCEO.UI.Navigation.NavigationRouter.Instance;
            if (router == null) return;
            bool stillOnMeeting = router.CurrentPageId != null && router.CurrentPageId.StartsWith("n21");
            if (router.StackDepth == 0 || stillOnMeeting)
            {
                // 兜底：栈被清空或未能退栈时，显式回到收件箱邮件页
                Object.FindFirstObjectByType<PrefabNavIntegration>()?.SelectTab("n7_收件箱");
            }
        }

        void Clear(Transform host)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
                Destroy(host.GetChild(i).gameObject);
        }
    }
}
