using System;
using System.Collections;
using System.Collections.Generic;
using AirlineCEO.AI;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using AirlineCEO.UI.Framework;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI.Shell
{
    /// <summary>
    /// n7 收件箱 · 参照例图：左侧邮件列表 + 右侧详情（头像/发件人/时间/状态）。
    /// 数据源为 GameState.inbox（持久化），含「7 日（季度）截止」：待办邮件到期标红、剩余季度倒计时。
    /// </summary>
    public class InboxPanelUI : BasePanel
    {
        readonly List<GameObject> _rows = new List<GameObject>();
        readonly HashSet<string> _collapsedDays = new HashSet<string>();
        bool _dayFoldInit;
        Transform _listRoot;
        InboxMail _selected;
        ScrollRect _listScroll;
        bool _rebuilding;
        int _lastUnread = -1;
        /// <summary>
        /// 上一次把正文滚动条复位到顶部的那封信的 id。
        ///
        /// ★2026-10-19：收件箱每来一封新信都会走 <see cref="OnInboxChanged"/> → <see cref="RenderDetail"/>，
        /// 而 RenderDetail 过去无条件调 RefreshBodyScroll 把正文滚回顶部 —— CEO 正在读的那封被顶回第一行。
        /// 换成同 id 时保留位置、换了信才回顶。
        /// </summary>
        string _bodyScrollMailId;

        TextMeshProUGUI _listCount;
        Button _chipOpen;
        Button _chipUnread;
        int _filter;   // 0 = 全部 · 1 = 仅待处理 · 2 = 仅未读
        TextMeshProUGUI _detailSender;
        TextMeshProUGUI _detailRole;
        TextMeshProUGUI _detailTime;
        TextMeshProUGUI _detailTitle;
        TextMeshProUGUI _detailBody;
        RectTransform _bodyViewport;
        ScrollRect _bodyScroll;
        TextMeshProUGUI _detailAvatarGlyph;
        Image _detailAvatar;
        Image _detailTagImg;
        TextMeshProUGUI _detailTag;
        TextMeshProUGUI _detailDeadline;
        TextMeshProUGUI _hint;
        TMP_InputField _ceoReply;
        bool _revising;
        Button _meetBtn;
        Button _approveBtn;
        Button _deferBtn;
        Button _rejectBtn;
        Transform _detailLinks;

        List<InboxMail> Mails => GameState.Instance != null ? GameState.Instance.inbox : null;

        public static InboxPanelUI Build()
        {
            var root = UIFactory.Panel(null, "n7_收件箱", UIFactory.Fill, UITheme.Panel);
            var cg = root.AddComponent<CanvasGroup>();
            var title = UIFactory.Label(root.transform, "Title", "收件箱", 18,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(16f, -40f), new Vector2(160f, -6f)),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(root.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-40f, -36f), new Vector2(-8f, -8f)),
                UITheme.PanelDeep, () => { }, 16);
            close.gameObject.SetActive(false);

            var panel = root.AddComponent<InboxPanelUI>();
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BuildLayout(root.transform);
            panel.EnsureSampleMails();
            panel.EnsureTutorialMailRecord();
            panel.RebuildList();
            var first = panel.Mails;
            if (first != null && first.Count > 0)
                panel.SelectMail(first[0]);
            panel.RefreshActionButtons();
            return panel;
        }

        void BuildLayout(Transform root)
        {
            const float titleH = 40f;
            const float split = 0.30f;

            _listCount = UIFactory.Label(root, "Count", "", 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(168f, -40f), new Vector2(330f, -6f)),
                UITheme.TextMuted);

            // 「待处理 / 未读」筛选筹码：点击切换列表过滤（再点或点另一个切换，点回即恢复全部）。
            _chipOpen = UIFactory.Button(root, "ChipOpen", "待处理 0",
                new UIFactory.Anchor(new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(336f, -36f), new Vector2(424f, -8f)),
                UITheme.PanelDeep, () => SetFilter(1), 12);
            _chipOpen.transition = Selectable.Transition.None;
            _chipUnread = UIFactory.Button(root, "ChipUnread", "未读 0",
                new UIFactory.Anchor(new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(432f, -36f), new Vector2(508f, -8f)),
                UITheme.PanelDeep, () => SetFilter(2), 12);
            _chipUnread.transition = Selectable.Transition.None;

            var listPanel = UIFactory.Panel(root, "ListPanel",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(split, 1f),
                    new Vector2(0f, 0f), new Vector2(0f, -titleH)),
                new Color(0, 0, 0, 0), rounded: false);
            var listImg = listPanel.GetComponent<Image>();
            if (listImg != null) { listImg.color = new Color(0, 0, 0, 0); listImg.raycastTarget = false; }

            _listRoot = UIFactory.VerticalScrollArea(listPanel.transform, "ListScroll",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(8f, 4f), new Vector2(-8f, -4f)));
            _listScroll = _listRoot != null ? _listRoot.GetComponentInParent<ScrollRect>() : null;
            var vlg = _listRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 2, 2, 2);
            vlg.spacing = 2;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            var fitter = _listRoot.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var splitLine = UIFactory.Panel(root, "Split",
                new UIFactory.Anchor(new Vector2(split, 0f), new Vector2(split, 1f),
                    new Vector2(0f, 8f), new Vector2(1.5f, -titleH)),
                UITheme.Border, rounded: false);
            var splitImg = splitLine.GetComponent<Image>();
            if (splitImg != null) splitImg.raycastTarget = false;

            var detail = UIFactory.Panel(root, "Detail",
                new UIFactory.Anchor(new Vector2(split, 0f), new Vector2(1f, 1f),
                    new Vector2(12f, 0f), new Vector2(0f, -titleH)),
                new Color(0, 0, 0, 0), rounded: false);
            var detailImg = detail.GetComponent<Image>();
            if (detailImg != null) { detailImg.color = new Color(0, 0, 0, 0); detailImg.raycastTarget = false; }

            var avatar = UIFactory.Panel(detail.transform, "Avatar",
                new UIFactory.Anchor(new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(12f, -44f), new Vector2(44f, -12f)),
                UITheme.Accent);
            _detailAvatar = avatar.GetComponent<Image>();
            _detailAvatar.sprite = UIFactory.CircleSprite;
            _detailAvatar.type = Image.Type.Simple;
            _detailAvatar.raycastTarget = false;
            _detailAvatarGlyph = UIFactory.Label(avatar.transform, "Glyph", "", 22,
                TextAlignmentOptions.Center, UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);

            _detailSender = UIFactory.Label(detail.transform, "Sender", "", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(54f, -30f), new Vector2(-200f, -8f)),
                UITheme.TextPrimary, FontStyles.Bold);
            _detailRole = UIFactory.Label(detail.transform, "Role", "", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(54f, -48f), new Vector2(-200f, -30f)),
                UITheme.TextMuted);

            var tag = UIFactory.Panel(detail.transform, "Tag",
                new UIFactory.Anchor(new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-200f, -36f), new Vector2(-120f, -12f)),
                UITheme.Warning);
            _detailTagImg = tag.GetComponent<Image>();
            _detailTag = UIFactory.Label(tag.transform, "TagLabel", "", 12,
                TextAlignmentOptions.Center, UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);
            _detailTime = UIFactory.Label(detail.transform, "Time", "", 12,
                TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-112f, -36f), new Vector2(-12f, -12f)),
                UITheme.TextMuted);

            _detailTitle = UIFactory.Label(detail.transform, "Title", "", 16,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(12f, -74f), new Vector2(-12f, -50f)),
                UITheme.TextPrimary, FontStyles.Bold);
            _detailTitle.textWrappingMode = TextWrappingModes.Normal;
            _detailTitle.overflowMode = TextOverflowModes.Truncate;

            _detailBody = UIFactory.ScrollableLabel(detail.transform, "Body",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(12f, 50f), new Vector2(-12f, -80f)),
                UITheme.TextPrimary, 15, TextAlignmentOptions.TopLeft, showScrollbar: true);
            _detailBody.raycastTarget = true;
            _detailBody.richText = true;
            _bodyScroll = _detailBody.GetComponentInParent<ScrollRect>();
            _bodyViewport = _bodyScroll != null
                ? _bodyScroll.transform as RectTransform
                : _detailBody.transform.parent as RectTransform;
            var linker = _detailBody.gameObject.AddComponent<InboxLinkClick>();
            linker.text = _detailBody;
            linker.onLink = OnKeywordClicked;

            _detailLinks = UIFactory.Panel(detail.transform, "Links",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(12f, 78f), new Vector2(-12f, 106f)),
                new Color(0, 0, 0, 0), rounded: false).transform;
            _detailLinks.GetComponent<Image>().raycastTarget = false;

            _detailDeadline = UIFactory.Label(detail.transform, "Deadline", "", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(12f, 46f), new Vector2(-420f, 72f)),
                UITheme.TextMuted);
            _detailDeadline.overflowMode = TextOverflowModes.Truncate;

            _hint = UIFactory.Label(detail.transform, "Hint", "", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(12f, 46f), new Vector2(-420f, 72f)),
                UITheme.Warning);
            _hint.textWrappingMode = TextWrappingModes.Normal;
            _hint.overflowMode = TextOverflowModes.Truncate;

            _ceoReply = UIFactory.Input(detail.transform, "CeoReply",
                "回复运营部：此航线客货流很大，要从早班排到红眼，最大能力挖掘潜力…",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(12f, 110f), new Vector2(-12f, 174f)));
            _ceoReply.lineType = TMP_InputField.LineType.MultiLineNewline;
            if (_ceoReply.textComponent != null)
            {
                _ceoReply.textComponent.textWrappingMode = TextWrappingModes.Normal;
                _ceoReply.textComponent.verticalAlignment = VerticalAlignmentOptions.Top;
            }
            _ceoReply.gameObject.SetActive(false);

            _meetBtn = UIFactory.Button(detail.transform, "Meet", "发起会议",
                new UIFactory.Anchor(new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-404f, 8f), new Vector2(-308f, 42f)),
                UITheme.Accent, () => OpenMeeting(), 13);
            _approveBtn = UIFactory.Button(detail.transform, "Approve", "批准",
                new UIFactory.Anchor(new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-300f, 8f), new Vector2(-204f, 42f)),
                UITheme.Success, () => Approve(), 13);
            _deferBtn = UIFactory.Button(detail.transform, "Defer", "暂缓",
                new UIFactory.Anchor(new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-196f, 8f), new Vector2(-104f, 42f)),
                UITheme.PanelDeep, () => Mark("deferred"), 13);
            _rejectBtn = UIFactory.Button(detail.transform, "Reject", "拒绝",
                new UIFactory.Anchor(new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-96f, 8f), new Vector2(-12f, 42f)),
                UITheme.Danger, () => Mark("rejected"), 13);
        }

        public override void Show()
        {
            base.Show();
            RebuildList();
            RenderDetail();   // 回到收件箱时按最新邮件状态重绘详情（含底部决策按钮）
        }

        void OnEnable()
        {
            InboxManager.Changed -= OnInboxChanged;
            InboxManager.Changed += OnInboxChanged;
            MessageCenter.Changed -= OnFeedChanged;
            MessageCenter.Changed += OnFeedChanged;
            InboxManager.SyncFeedIntoInbox(GameState.Instance);
            // 旧档回填：早期回执没记航线，开箱时按源件 id 补一次（幂等）。
            InboxManager.BackfillReceiptRouteRefs(GameState.Instance);
            TransitionBusinessLogic.Ensure(GameState.Instance);
            EnsureTutorialMailRecord();
            InboxManager.MaybeOfferAlliance(GameState.Instance);
            InboxManager.MaybeOfferRightsAndSlots(GameState.Instance);
            InboxManager.MaybeOfferUnion(GameState.Instance);
            InboxManager.MaybeOfferFinanceCrisis(GameState.Instance);
            InboxManager.MaybeOfferFuelHedge(GameState.Instance);
            InboxManager.MaybeOfferAnnualBudget(GameState.Instance);
            InboxManager.MaybeOfferCarbonBuy(GameState.Instance);
            InboxManager.MaybeOfferOpsSchedule(GameState.Instance);
            InboxManager.MaybeOfferInvestment(GameState.Instance);
            InboxManager.MaybeOfferPartsOrder(GameState.Instance);
            InboxManager.MaybeOfferInspection(GameState.Instance);
            InboxManager.MaybeOfferCompensation(GameState.Instance);
            RebuildList();
            RenderDetail();   // 重新启用时按最新邮件状态重绘详情（含底部决策按钮）
            RefreshActionButtons();
        }

        void OnDisable()
        {
            InboxManager.Changed -= OnInboxChanged;
            MessageCenter.Changed -= OnFeedChanged;
        }

        void OnFeedChanged()
        {
            InboxManager.SyncFeedIntoInbox(GameState.Instance);
        }

        void OnInboxChanged()
        {
            if (!isActiveAndEnabled) return;
            // 报价过有效期的询价函自动作废：取消「待处理」标签与底部决策按钮
            TransitionBusinessLogic.ExpireStaleInquiryMails(GameState.Instance);
            RebuildList();
            RefreshDecisionButtons();
            RenderDetail();
        }

        void EnsureSampleMails()
        {
            var mails = Mails;
            if (mails == null || mails.Count > 0) return;

            AddMail(new InboxMail
            {
                id = "m_route",
                sender = "市场部",
                role = "航线提案",
                glyph = "市",
                kind = "report",
                action = "open_route",
                origin = "PEK",
                dest = "SHA",
                subject = "建议开通 PEK–SHA：竞对已压价",
                body =
                    InboxLetter.Section("提案要点") +
                    InboxLetter.Bullet("航线　北京首都（PEK）— 上海虹桥（SHA），直飞") +
                    InboxLetter.Bullet("定价　经济舱对标竞对，预计日利润约 16 万美元") +
                    InboxLetter.Bullet("启动资金　300 万美元") +
                    InboxLetter.Section("时点") +
                    InboxLetter.Bullet("批准：转入局方准入办理（许可 / 航权 / 公示），取证后再办时刻；开航时才扣启动资金") +
                    InboxLetter.Bullet("取证后自动排班开航——批准不等于可以飞") +
                    InboxLetter.Section("建议") +
                    InboxLetter.Bullet("先点下方关键词查阅航线网络 或 RPM 评估，再在本邮件批准"),
                tag = "提案"
            });
            AddMail(new InboxMail
            {
                id = "m_board",
                sender = "董事会",
                role = "考核提醒",
                glyph = "董",
                kind = "report",
                subject = "本季考核提醒",
                body = "请关注年度目标进度，并核对本人合同与个人资料。可在董事会页查看详情。",
                status = "approved",   // 告知类：无需批复，不占待办
                tag = ""
            });
            InboxManager.MaybeOfferAlliance(GameState.Instance);
            InboxManager.MaybeOfferRightsAndSlots(GameState.Instance);
            InboxManager.MaybeOfferUnion(GameState.Instance);
            InboxManager.MaybeOfferFinanceCrisis(GameState.Instance);
            InboxManager.MaybeOfferFuelHedge(GameState.Instance);
            InboxManager.MaybeOfferAnnualBudget(GameState.Instance);
            InboxManager.MaybeOfferCarbonBuy(GameState.Instance);
            InboxManager.MaybeOfferOpsSchedule(GameState.Instance);
            InboxManager.MaybeOfferInvestment(GameState.Instance);
            InboxManager.MaybeOfferPartsOrder(GameState.Instance);
            InboxManager.MaybeOfferInspection(GameState.Instance);
            InboxManager.MaybeOfferCompensation(GameState.Instance);
            InboxManager.MaybeOfferAnnualGoals(GameState.Instance);
            InboxManager.MaybeOfferCapacity(GameState.Instance);
        }

        InboxMail AddMail(InboxMail m)
        {
            var gs = GameState.Instance;
            if (gs == null) return m;
            m.receivedYear = gs.year;
            m.receivedQuarter = gs.quarter;
            m.deadlineQuarters = m.deadlineQuarters > 0 ? m.deadlineQuarters : InboxManager.DefaultDeadlineQuarters;
            InboxManager.Add(gs, m);
            return m;
        }

        /// <summary>引导已「发起决策会议」后，收件箱留一份深圳—北京提案邮件作为记录。</summary>
        public void EnsureTutorialMailRecord()
        {
            var gs = GameState.Instance;
            if (gs == null || !gs.advisorTutorialMailLeft) return;

            EnsureSampleMails();
            var mails = Mails;
            if (mails == null) return;
            int idx = mails.FindIndex(m => m != null && m.id == AdvisorTutorialScript.TutorialMailId);
            if (idx < 0)
            {
                AddMail(new InboxMail
                {
                    id = AdvisorTutorialScript.TutorialMailId,
                    sender = "战略发展部",
                    role = "开航提议",
                    glyph = "战",
                    kind = "report",
                    action = "open_route",
                    origin = "SZX",
                    dest = "PEK",
                    subject = "关于开通深圳—北京首都航线的建议",
                    body =
                        InboxLetter.Section("提案要点") +
                        InboxLetter.Bullet("航线　深圳宝安（SZX）→ 北京首都（PEK），即 SZX-PEK") +
                        InboxLetter.Bullet("评估　RPM 综合评分 86 分（绿色）") +
                        InboxLetter.Bullet("预估日利润　16.2 万美元；启动资金 300 万美元") +
                        InboxLetter.Section("建议") +
                        InboxLetter.Bullet("尽快启动开航程序；批准后转入局方准入办理，取证后开航"),
                    tag = "紧急"
                });
                idx = 0;
            }
            RebuildList();
            if (idx >= 0 && mails != null && idx < mails.Count)
                SelectMail(mails[idx]);
            if (_hint != null) _hint.text = "";
            RefreshActionButtons();
        }

        /// <summary>「发起会议」是离开引导后的自由玩法功能：引导期间隐藏。工会邮件在本页直接拍板，不走会议。</summary>
        void RefreshActionButtons()
        {
            var gs = GameState.Instance;
            bool outsideTutorial = gs == null || gs.advisorTutorialCompleted;
            bool union = _selected != null && _selected.action == "union_talk";
            bool inquiry = _selected != null && _selected.action == TransitionBusinessLogic.InquiryAction;
            // 已上会批准/拒绝：无需再发起会议（暂缓=补条件中，按钮改为「重新召集」，见 RefreshDecisionButtons）
            string outcome = InboxManager.MeetingOutcomeOf(_selected);
            bool decidedFinal = outcome == "approved" || outcome == "rejected";
            // 无需批复的邮件（回执/进度/通报/已结案）：不显示任何决策按钮，含「发起会议」
            // （会议暂缓件除外：保留「重新召集」入口，见 RefreshDecisionButtons）
            bool meetDeferred = outcome == "deferred";
            bool noDecision = _selected != null && !meetDeferred && !InboxManager.RequiresDecision(_selected);
            if (_meetBtn != null)
                _meetBtn.gameObject.SetActive(outsideTutorial && !union && !inquiry && !decidedFinal && !noDecision);
            LayoutReplyBox();
        }

        void RebuildList()
        {
            if (_listRoot == null || _rebuilding) return;
            _rebuilding = true;
            try
            {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i] != null) DestroyImmediate(_rows[i]);
            _rows.Clear();

            var mailsAll = Mails;
            int countAll = mailsAll != null ? mailsAll.Count : 0;
            int unread = InboxManager.UnreadCount(GameState.Instance);
            int open = InboxManager.OpenCount(GameState.Instance);
            bool unreadGrew = _lastUnread >= 0 && unread > _lastUnread;
            _lastUnread = unread;
            if (_listCount != null)
            {
                string suffix = _filter == 1 ? " · 筛选：待处理" : _filter == 2 ? " · 筛选：未读" : "";
                // T05.2：在途送达提示（`pendingInbox` 以前没有任何 UI 读取）
                string inflight = InboxManager.InflightLine(GameState.Instance);
                _listCount.text = $"邮件列表（{countAll}）{suffix}"
                                  + (string.IsNullOrEmpty(inflight) ? "" : "　·　" + inflight);
            }
            PaintChips(open, unread);

            var gs = GameState.Instance;
            if (mailsAll != null)
                for (int i = 0; i < mailsAll.Count; i++)
                    InboxManager.StampReceived(gs, mailsAll[i], i);

            // 筛选：待处理 = 待批复/暂缓；未读 = 未点开。其余显示全部。
            var mails = mailsAll;
            if (_filter == 1 || _filter == 2)
            {
                mails = new List<InboxMail>();
                if (mailsAll != null)
                    for (int i = 0; i < mailsAll.Count; i++)
                    {
                        var mm = mailsAll[i];
                        if (mm == null) continue;
                        bool keep = _filter == 1 ? InboxManager.IsOpen(mm) : !mm.read;
                        if (keep) mails.Add(mm);
                    }
            }
            int count = mails != null ? mails.Count : 0;

            if (mails == null || count == 0)
            {
                if (_filter == 1) CreateEmptyHint("当前没有待处理邮件");
                else if (_filter == 2) CreateEmptyHint("当前没有未读邮件");
                return;
            }

            var groups = new List<(string key, string header, List<InboxMail> items)>();
            var map = new Dictionary<string, List<InboxMail>>();
            for (int i = 0; i < mails.Count; i++)
            {
                var m = mails[i];
                if (m == null) continue;
                string key = InboxTime.DayKey(m);
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<InboxMail>();
                    map[key] = list;
                    groups.Add((key, InboxTime.DayHeader(m), list));
                }
                list.Add(m);
            }
            groups.Sort((a, b) => string.CompareOrdinal(b.key, a.key));

            if (!_dayFoldInit)
            {
                for (int g = 1; g < groups.Count; g++)
                    _collapsedDays.Add(groups[g].key);
                _dayFoldInit = true;
            }
            if (unreadGrew && groups.Count > 0)
                _collapsedDays.Remove(groups[0].key);

            for (int g = 0; g < groups.Count; g++)
            {
                var grp = groups[g];
                bool collapsed = _collapsedDays.Contains(grp.key);
                CreateDayHeader(grp.key, grp.header, grp.items.Count, collapsed);
                if (collapsed) continue;
                for (int i = 0; i < grp.items.Count; i++)
                    CreateRow(grp.items[i]);
            }

            var listRt = _listRoot as RectTransform;
            if (listRt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(listRt);
            if (unreadGrew && _listScroll != null)
                _listScroll.verticalNormalizedPosition = 1f;
            }
            finally
            {
                _rebuilding = false;
            }
        }

        /// <summary>切换列表筛选：0 全部 / 1 待处理 / 2 未读；点已激活的筹码恢复全部。</summary>
        void SetFilter(int f)
        {
            _filter = _filter == f ? 0 : f;
            RebuildList();
        }

        void PaintChips(int open, int unread)
        {
            if (_chipOpen != null)
            {
                var t = _chipOpen.GetComponentInChildren<TextMeshProUGUI>(true);
                if (t != null) t.text = $"待处理 {open}";
                var img = _chipOpen.GetComponent<Image>();
                if (img != null) img.color = _filter == 1 ? UITheme.NavActive : UITheme.PanelDeep;
            }
            if (_chipUnread != null)
            {
                var t = _chipUnread.GetComponentInChildren<TextMeshProUGUI>(true);
                if (t != null) t.text = $"未读 {unread}";
                var img = _chipUnread.GetComponent<Image>();
                if (img != null) img.color = _filter == 2 ? UITheme.NavActive : UITheme.PanelDeep;
            }
        }

        void CreateEmptyHint(string text)
        {
            var row = UIFactory.Panel(_listRoot, "EmptyHint", UIFactory.Fill,
                new Color(0.04f, 0.07f, 0.12f, 1f));
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = 40f;
            le.preferredHeight = 40f;
            le.flexibleHeight = 0f;
            UIFactory.Label(row.transform, "T", text, 12, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _rows.Add(row);
        }

        void CreateDayHeader(string dayKey, string header, int n, bool collapsed)
        {
            var row = UIFactory.Panel(_listRoot, "Day_" + dayKey, UIFactory.Fill,
                new Color(0.04f, 0.07f, 0.12f, 1f));
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = 24f;
            le.preferredHeight = 24f;
            le.flexibleHeight = 0f;
            UIFactory.Label(row.transform, "Day",
                $"{(collapsed ? "▸" : "▾")}  {header}（{n}条）",
                12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted, FontStyles.Bold);

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            string key = dayKey;
            btn.onClick.AddListener(() => ToggleDay(key));
            _rows.Add(row);
        }

        void ToggleDay(string dayKey)
        {
            if (string.IsNullOrEmpty(dayKey)) return;
            if (!_collapsedDays.Add(dayKey))
                _collapsedDays.Remove(dayKey);
            RebuildList();
        }

        void CreateRow(InboxMail m)
        {
            bool sel = _selected == m;
            bool due = GameState.Instance != null
                && InboxManager.IsDue(m, GameState.Instance.year, GameState.Instance.quarter);
            var row = UIFactory.Panel(_listRoot, "Row_" + m.id,
                UIFactory.Fill,
                sel ? new Color(0.14f, 0.22f, 0.38f, 1f)
                    : due ? new Color(0.38f, 0.14f, 0.14f, 0.9f)
                    : new Color(0.07f, 0.12f, 0.21f, 0.75f));
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = 52f;
            le.preferredHeight = 52f;
            le.flexibleHeight = 0f;
            if (sel) UIFactory.AddBorder(row, UITheme.Warning, new Vector2(1.4f, -1.4f));
            else if (due) UIFactory.AddBorder(row, UITheme.Danger, new Vector2(1.4f, -1.4f));

            bool unread = m != null && !m.read;
            Color titleCol = unread ? UITheme.TextPrimary : new Color(0.40f, 0.48f, 0.56f, 1f);
            Color subCol = unread ? UITheme.TextMuted : new Color(0.32f, 0.38f, 0.46f, 1f);
            var titleStyle = unread ? FontStyles.Bold : FontStyles.Normal;

            // 头像（固定像素方形，圆形显示）
            var avatar = UIFactory.Panel(row.transform, "Avatar",
                new UIFactory.Anchor(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(6f, -12f), new Vector2(30f, 12f)),
                AvatarColor(m.sender));
            var avImg = avatar.GetComponent<Image>();
            avImg.sprite = UIFactory.CircleSprite;
            avImg.type = Image.Type.Simple;
            avImg.raycastTarget = false; // 不拦截整行点击
            UIFactory.Label(avatar.transform, "Glyph", Glyph(m), 13,
                TextAlignmentOptions.Center, UIFactory.Fill, titleCol, titleStyle);

            // 发件人（左上）+ 时间（右上）
            UIFactory.Label(row.transform, "Sender", SenderText(m), 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.10f, 0.55f), new Vector2(0.68f, 0.95f), Vector2.zero, Vector2.zero),
                titleCol, titleStyle);
            UIFactory.Label(row.transform, "Time", InboxTime.Clock(m), 12,
                TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.68f, 0.52f), new Vector2(0.97f, 0.95f), Vector2.zero, Vector2.zero),
                subCol);

            // 主题摘要（下）
            UIFactory.Label(row.transform, "Subject",
                $"[{InboxManager.KindLabel(m.kind)}] {m.subject}", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.10f, 0.08f), new Vector2(0.97f, 0.55f), Vector2.zero, Vector2.zero),
                subCol, unread ? FontStyles.Bold : FontStyles.Normal);

            // 整行可点
            var btn = row.AddComponent<Button>();
            var bc = btn.colors;
            bc.normalColor = Color.white;
            bc.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            bc.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            bc.selectedColor = Color.white;
            bc.fadeDuration = 0.06f;
            btn.colors = bc;
            btn.targetGraphic = row.GetComponent<Image>();
            var mail = m;
            btn.onClick.AddListener(() => SelectMail(mail));

            _rows.Add(row);
        }

        /// <summary>
        /// 列表的发件人字样：外部来文统一写成「新闻官 沈其昌」。
        /// 只写姓名的话，CEO 在列表上认不出这是谁 —— 这个身份只在详情页的角色行里出现一次。
        /// </summary>
        static string SenderText(InboxMail m)
        {
            string s = InboxLetter.NormalizeSender(m != null ? m.sender : null);
            if (string.IsNullOrEmpty(s)) return s;
            if (PressDesk.IsPressOfficer(GameState.Instance, s)) return PressDesk.Title + " " + s;
            return s;
        }

        /// <summary>详情页角色行：部门邮件显示「事由 · 总监 姓名」，与「人员 → 管理团队」名单一致。</summary>
        string RoleLine(InboxMail mail)
        {
            string role = mail != null ? (mail.role ?? "") : "";
            if (mail == null) return role;
            if (mail.action == "news" || mail.kind == "media" || mail.kind == "alert") return role;
            InboxLetter.Identify(GameState.Instance, mail.sender, mail.role,
                out _, out string post, out string name);
            if (string.IsNullOrEmpty(post)) return role;
            string sig = string.IsNullOrEmpty(name) ? post : $"{post} {name}";
            if (!string.IsNullOrEmpty(role) && role.IndexOf(sig, System.StringComparison.Ordinal) >= 0) return role;
            return string.IsNullOrEmpty(role) ? sig : $"{role} · {sig}";
        }

        void SelectMail(InboxMail mail)
        {
            if (mail == null) return;
            // 批复结果只挂在当时那一封上；换信必须清掉，否则「已批准申请航权」会贴在所有邮件底栏
            if (_hint != null && (_selected == null || _selected.id != mail.id))
                _hint.text = "";
            _selected = mail;
            InboxManager.Normalize(_selected);
            InboxManager.MarkRead(GameState.Instance, _selected.id);
            if (_selected.action == "news" && _selected.id != null && _selected.id.StartsWith("msg_")
                && int.TryParse(_selected.id.Substring(4), out int mid))
                MessageCenter.MarkRead(mid);
            RebuildList();
            RenderDetail();
        }

        /// <summary>右侧详情整块渲染（头像/抬头/正文/链接/按钮/状态标签/截止提示）。</summary>
        void RenderDetail()
        {
            if (_selected == null) return;
            if (_detailAvatar != null) _detailAvatar.color = AvatarColor(_selected.sender);
            if (_detailAvatarGlyph != null) _detailAvatarGlyph.text = Glyph(_selected);
            if (_detailSender != null) _detailSender.text = InboxLetter.NormalizeSender(_selected.sender);
            if (_detailRole != null) _detailRole.text = RoleLine(_selected);
            if (_detailTime != null) _detailTime.text = InboxTime.FullStamp(_selected);
            if (_detailTitle != null)
            {
                string prefix = string.IsNullOrEmpty(_selected.tag) ? "" : $"【{_selected.tag}】 ";
                _detailTitle.text = prefix + _selected.subject;
            }
            if (_detailBody != null)
            {
                string body = _selected.body;
                if (_selected.action == "union_talk" && InboxManager.IsOpen(_selected))
                    body = InboxManager.FormatUnionMailBody(GameState.Instance);
                body = InboxLetter.FrameIfNeeded(GameState.Instance, _selected, body);
                if (!string.IsNullOrEmpty(_selected.id) && !string.IsNullOrEmpty(body)
                    && body.IndexOf(_selected.id, StringComparison.Ordinal) >= 0)
                    body = body.Replace(_selected.id, RoutePermitLogic.CaseNo(_selected));
                // 同一封信的重绘（新信到达、状态刷新、布局重排）不得把正在读的正文顶回第一行；
                // 只有换了信才从顶部开始。判定用 id，不用对象引用 —— 读档/重建会换对象但仍是「同一封」。
                bool sameMail = _selected != null && !string.IsNullOrEmpty(_selected.id)
                               && _selected.id == _bodyScrollMailId;
                _detailBody.text = TermNavMap.Decorate(body);
                _detailBody.ForceMeshUpdate();
                RefreshBodyScroll(preservePosition: sameMail);
                if (_selected != null) _bodyScrollMailId = _selected.id;
            }
            RebuildLinkButtons();
            RefreshDecisionButtons();
            RefreshActionButtons();
            LayoutReplyBox();

            // 右上角状态标签（中文）
            if (_detailTagImg != null && _detailTag != null)
            {
                _detailTag.gameObject.SetActive(true);
                if (_selected.action == "news")
                {
                    _detailTag.text = "快讯";
                    _detailTagImg.color = UITheme.Accent;
                }
                else if (IsExpiredVoid(_selected))
                {
                    // 报价过有效期：不再显示「待处理」，改标「已作废」
                    _detailTag.text = "已作废";
                    _detailTagImg.color = UITheme.TextMuted;
                }
                else
                {
                    // T05.2：「已批准」拆成「已批准·待交割」（还在等交割季）与「已批准」（已办结）
                    _detailTag.text = InboxManager.StatusLabel(GameState.Instance, _selected);
                    _detailTagImg.color = StatusColor(_selected.status);
                }
            }

            // 截止提示（7 日/季度）；回执/通报类说明无需批复
            if (_detailDeadline != null)
            {
                var gs = GameState.Instance;
                if (IsExpiredVoid(_selected))
                {
                    _detailDeadline.color = UITheme.TextMuted;
                    _detailDeadline.text = "本函报价已过有效期，自动作废，无需批复。";
                }
                else if (InboxManager.MeetingOutcomeOf(_selected) == "deferred")
                {
                    _detailDeadline.color = UITheme.Warning;
                    _detailDeadline.text = "CEO 决策会议已暂缓：补条件中，条件齐备后可点「重新召集」再次上会。";
                }
                else if (!InboxManager.IsOpen(_selected))
                {
                    _detailDeadline.color = UITheme.TextMuted;
                    string mo = InboxManager.MeetingOutcomeOf(_selected);
                    // T05.2：走统一状态行 —— 「已办结」与「已批准·待交割」必须各自说清在等什么
                    _detailDeadline.text = mo == "approved"
                        ? "决策会议已批准，无需再批。　" + InboxManager.StatusLine(gs, _selected)
                        : mo == "rejected"
                            ? "决策会议已拒绝，无需再批。　" + InboxManager.StatusLine(gs, _selected)
                            : InboxManager.StatusLine(gs, _selected);
                }
                else if (gs != null)
                {
                    int rem = InboxManager.RemainingQuarters(_selected, gs.year, gs.quarter);
                    if (rem < 0)
                    {
                        _detailDeadline.text = _selected.action == "union_talk"
                            ? "已到期！请立即选择让步 / 强硬 / 拖延（否则时间无法推进）。"
                            : "已到期！请立即批准或拒绝（否则时间无法推进）。";
                        _detailDeadline.color = UITheme.Danger;
                    }
                    else
                    {
                        // T05.2：统一状态行（在等什么 · 何时推进 · 是否需 CEO）
                        _detailDeadline.text = InboxManager.StatusLine(gs, _selected);
                        _detailDeadline.color = UITheme.TextMuted;
                    }
                }
            }
        }

        /// <summary>
        /// 告知类邮件（办理回执、进度汇报、执行回执等）：只读，不显示批复按钮。
        /// ★T05.2：详情页底注改走 <see cref="InboxManager.StatusLine"/>，本判定仍供按钮层使用。
        /// </summary>
        static bool IsReceiptMail(InboxMail m)
            => m != null && m.action != "news" && !InboxManager.RequiresDecision(m);

        /// <summary>报价过有效期而自动作废的询价函：标签显示「已作废」，不显示批复按钮。</summary>
        static bool IsExpiredVoid(InboxMail m)
            => m != null && m.action == TransitionBusinessLogic.InquiryAction
               && !InboxManager.IsOpen(m)
               && !string.IsNullOrEmpty(m.body)
               && m.body.IndexOf(TransitionBusinessLogic.ExpiredNotice, System.StringComparison.Ordinal) >= 0;

        static string StatusLabel(string status) => InboxManager.StatusLabel(status);

        static Color StatusColor(string status)
        {
            switch (status)
            {
                case "approved": return UITheme.Success;
                case "deferred": return UITheme.TextMuted;
                case "rejected": return UITheme.Danger;
                case InboxManager.SupersededStatus: return UITheme.TextMuted;   // 已作废：中性灰，不是「已批准」的绿
                default: return UITheme.Warning;
            }
        }

        static Color AvatarColor(string key)
        {
            if (string.IsNullOrEmpty(key)) return UITheme.Success;
            int h = 0;
            for (int i = 0; i < key.Length; i++) h = h * 31 + key[i];
            float hue = ((h % 12) + 12) % 12 / 12f;
            return Color.HSVToRGB(hue, 0.5f, 0.82f);
        }

        static string Glyph(InboxMail m)
        {
            if (!string.IsNullOrEmpty(m.glyph)) return m.glyph;
            return string.IsNullOrEmpty(m.sender) ? "?" : m.sender.Substring(0, 1);
        }

        /// <summary>底栏一个跳转按钮（收集后统一排版，见 <see cref="RebuildLinkButtons"/>）。</summary>
        sealed class LinkEntry
        {
            public string Label;
            public string PageId;
            public string RouteKey;
            public LinkEntry(string label, string pageId, string routeKey)
            {
                Label = label; PageId = pageId; RouteKey = routeKey;
            }
        }

        void RebuildLinkButtons()
        {
            if (_detailLinks == null) return;
            for (int i = _detailLinks.childCount - 1; i >= 0; i--)
                DestroyImmediate(_detailLinks.GetChild(i).gameObject);
            if (_selected == null) return;

            var links = new List<LinkEntry>();
            BuildLinkEntries(links);

            // 「选定机号 / 选定时刻」这类多选题是**独占一栏**的：BuildLinkEntries 已经把候选
            // 按钮直接铺满底栏并选择了「只给候选、不给跳转」。此时再补跳转按钮会与候选叠字，
            // 故以「底栏是否已有子按钮」判断本栏是否已被占满。
            if (_detailLinks.childCount == 0)
            {
                // 2026-10-19（要求 1 / 2）：**以某条航线为主题的邮件**，底栏必须有一个
                // 「详见 · 此航线」直达该线；运力 / 时刻槽这一路的邮件（含回执）一律不挂
                // 「详见 · 董事会」—— 董事会页回答不了「这条线现在几班、槽卡在哪一步」，
                // 占了位置反而把人带离主题。
                string routeId = InboxManager.RouteIdOf(_selected);
                // 工会谈判件是**三选一立场**，底栏刻意留空（见 BuildLinkEntries 的早退），
                // 正文里即便提到某条线也不该插跳转按钮。
                bool unionTalk = _selected.action == "union_talk";
                if (!unionTalk && (!string.IsNullOrEmpty(routeId) || InboxManager.IsCapacityOrSlotMail(_selected)))
                    links.RemoveAll(l => l.Label == BoardLinkLabel);

                // 「此航线」放在最前面：CEO 读完这封最想做的就是照着去看这条线。
                // 已有指向同一条线的按钮就复用（改措辞），避免出现两个都叫「航线」的入口。
                if (!unionTalk && !string.IsNullOrEmpty(routeId))
                {
                    int at = links.FindIndex(l => IsRouteLink(l, routeId));
                    if (at >= 0)
                    {
                        links[at].Label = RouteLinkLabel;
                        var hit = links[at];
                        links.RemoveAt(at);
                        links.Insert(0, hit);
                    }
                    else links.Insert(0, new LinkEntry(RouteLinkLabel, "n41_单航线", routeId));
                }

                // 收集完再排版：按钮数随邮件变化，继续手写坐标会留空缝或叠字。
                int n = links.Count;
                float gap = n <= 2 ? 0.04f : 0.02f;
                for (int i = 0; i < n; i++)
                {
                    var l = links[i];
                    float x0 = (float)i / n;
                    float x1 = (float)(i + 1) / n - (i == n - 1 ? 0f : gap);
                    UIFactory.Button(_detailLinks, "L_" + l.Label, l.Label,
                        new UIFactory.Anchor(new Vector2(x0, 0f), new Vector2(x1, 1f), Vector2.zero, Vector2.zero),
                        UITheme.PanelDeep, () => JumpTo(l.PageId, l.RouteKey), 12);
                }
            }
        }

        const string RouteLinkLabel = "详见 · 此航线";
        const string BoardLinkLabel = "详见 · 董事会";

        /// <summary>该底栏按钮是否就是「进 n41 并选中这条航线」（不认航线提案那种 IATA 组合键）。</summary>
        static bool IsRouteLink(LinkEntry l, string routeId)
            => l.PageId != null && l.PageId.StartsWith("n41", StringComparison.Ordinal)
               && !string.IsNullOrEmpty(l.RouteKey)
               && string.Equals(l.RouteKey, routeId, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 按邮件类型把底栏候选按钮填进 <paramref name="links"/>（只收集，不排版）。
        /// 结果还会经过「此航线 / 董事会」的统一增删，见 <see cref="RebuildLinkButtons"/>。
        ///
        /// 传入列表而不是返回新列表：本方法有十几处「这一类邮件只有这两个入口」的早退分支，
        /// 用 void 方法原地追加，每处就能保持一句 return，不必把已收集的列表再抄一遍。
        /// </summary>
        void BuildLinkEntries(List<LinkEntry> links)
        {
            void AddLink(string label, string pageId, string routeKey)
                => links.Add(new LinkEntry(label, pageId, routeKey));

            void AddAction(float x0, float x1, string label, UnityEngine.Events.UnityAction act)
                => UIFactory.Button(_detailLinks, "L_" + label, label,
                    new UIFactory.Anchor(new Vector2(x0, 0f), new Vector2(x1, 1f), Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, act, 12);

            if (_selected.action == "union_talk")
                return;

            if (_selected.action == TransitionBusinessLogic.InquiryAction)
            {
                var offers = TransitionBusinessLogic.OffersOnMail(GameState.Instance, _selected);
                if (InboxManager.IsOpen(_selected) && offers.Count > 0 && offers.Count <= 3)
                {
                    float slot = 1f / offers.Count;
                    for (int i = 0; i < offers.Count; i++)
                    {
                        var o = offers[i];
                        if (o == null) continue;
                        string oid = o.id;
                        string lab = $"签约 · {TransitionKinds.Label(o.kind)}";
                        float x0 = i * slot;
                        float x1 = (i + 1) * slot - 0.02f;
                        AddAction(x0, Mathf.Max(x0 + 0.2f, x1), lab, () => SignInquiryOffer(oid));
                    }
                }
                return;
            }

            if (_selected.action == "transition_guide" || _selected.action == TransitionBusinessLogic.ProgressAction)
            {
                // 2026-10-30（CEO 要求）：过渡业务的邮件只留「前往 · 机队过渡业务」。
                // 摘掉「前往 · 人员编制」—— 这封信讲的是机队/合同交割（机号、期限、定金），
                // 人员编制页回答不了它，摆在那里只会把人带离主题。
                AddAction(0f, 1f, "前往 · 机队过渡业务", () => FleetUI.OpenTransitionTab());
                return;
            }

            if (_selected.action == InboxManager.AssignAircraftAction)
            {
                // 「合适的飞机」是这套流程里少见的**多选题**：运营部提名 2–3 架，各有利弊。
                // 把候选摊在这一排按钮上让 CEO 直接点选，而不是让他记住机号再回批复框里打一遍。
                var cands = AircraftNominationLogic.Decode(_selected.payloadCsv);
                if (InboxManager.IsOpen(_selected) && cands.Count > 0)
                {
                    float slot = 1f / cands.Count;
                    for (int i = 0; i < cands.Count; i++)
                    {
                        var c = cands[i];
                        if (c == null || string.IsNullOrEmpty(c.reg)) continue;
                        string reg = c.reg;
                        string lab = $"选定 {reg}（{c.deltaWan:+0.0;-0.0;0.0} 万/日）";
                        float x0 = i * slot;
                        float x1 = (i + 1) * slot - 0.015f;
                        AddAction(x0, Mathf.Max(x0 + 0.2f, x1), lab, () => PickNomination(reg));
                    }
                    return;
                }
                AddLink("详见 · 此航线", "n41_单航线", _selected.origin);
                AddLink("详见 · 机队", "n9_机队", null);
                return;
            }

            if (_selected.action == InboxManager.SlotSelectAction)
            {
                // 「备选时刻」与「执飞飞机提名」同源：都是运营部摆出的**多选题**。
                // 把候选摊在按钮上让 CEO 直接点定（第 1 条即推荐），而不是让他把时刻抄进批复框里再点批准 ——
                // 抄错一位数字就是买错一条槽、错一整季。
                var opts = SlotCandidateLogic.Decode(_selected.payloadCsv);
                if (InboxManager.IsOpen(_selected) && opts.Count > 0)
                {
                    float slot = 1f / opts.Count;
                    for (int i = 0; i < opts.Count; i++)
                    {
                        var o = opts[i];
                        if (o == null || string.IsNullOrEmpty(o.depClock)) continue;
                        string clock = o.depClock;
                        string tag = o.readyNow ? "即刻可排" : (o.aircraftOk ? "需申请" : "无机可派");
                        string lab = $"{i + 1}. {clock}　{tag}";
                        float x0 = i * slot;
                        float x1 = (i + 1) * slot - 0.015f;
                        AddAction(x0, Mathf.Max(x0 + 0.2f, x1), lab, () => PickSlotCandidate(clock));
                    }
                    return;
                }
                AddLink("详见 · 此航线", "n41_单航线", _selected.origin);
                AddLink("详见 · 航权与时刻槽", "n77_航权与时刻槽", null);
                return;
            }

            if (_selected.action == InboxManager.FuelHedgeAction
                || _selected.action == InboxManager.FinanceProgressAction)
            {
                AddLink("详见 · 对冲工作台", "n31_对冲工作台", null);
                AddLink("详见 · 财务总览", "n15_财务部", null);
                return;
            }
            if (_selected.action == InboxManager.AnnualBudgetAction)
            {
                AddLink("详见 · 年度预算", "n22_年度预算编制_审批", null);
                AddLink("详见 · 财务总览", "n15_财务部", null);
                return;
            }
            if (_selected.action == InboxManager.CarbonBuyAction)
            {
                AddLink("详见 · 碳配额", "n32_碳配额管理", null);
                AddLink("详见 · 财务总览", "n15_财务部", null);
                return;
            }
            if (_selected.action == InboxManager.FinanceLoanAction)
            {
                AddLink("详见 · 紧急融资", "n25_紧急融资_破产保护", null);
                AddLink("详见 · 财务总览", "n15_财务部", null);
                return;
            }
            if (_selected.action == InboxManager.FleetBuyAction)
            {
                AddLink("详见 · 购机会议", "n97_购机决策会议", null);
                AddLink("详见 · 机队", "n9_机队", null);
                return;
            }
            if (_selected.action == InboxManager.HrRecruitAction)
            {
                AddLink("详见 · 招聘", "n86_组织规划与招聘", null);
                return;
            }

            if (_selected.action == "open_route" && !string.IsNullOrEmpty(_selected.origin))
            {
                AddLink("详见 · 航线总览", "n12_航线网络", _selected.origin);
                AddLink("详见 · 航线提案", "n41_单航线", $"{_selected.origin}-{_selected.dest}");
                AddLink("详见 · RPM", "n35_新航线_RPM_评估台", null);
            }
            else if (_selected.action == "permit_progress" || _selected.action == "permit_objection"
                || _selected.action == "permit_retry")
            {
                AddLink("详见 · 航权时刻槽", "n77_航权与时刻槽", null);
                AddLink("详见 · 航线总览", "n12_航线网络", null);
                if (!string.IsNullOrEmpty(_selected.origin))
                    AddLink("详见 · 航线提案", "n41_单航线", $"{_selected.origin}-{_selected.dest}");
            }
            else if (_selected.action == "irrops")
            {
                AddLink("详见 · 航线总览", "n12_航线网络", null);
                AddLink("详见 · 航班异常", "n24_危机_IRROPS_弹窗", null);
            }
            else if (_selected.action == "annual_goals")
            {
                AddLink("详见 · 年度目标", "n5_年度目标确认", null);
                AddLink("详见 · 董事会", "n20_董事会", null);
            }
            else if (_selected.action == "capacity_plan")
            {
                AddLink("详见 · 运力规划", "n76_机队与运力规划", null);
                AddLink("详见 · 购机会议", "n97_购机决策会议", null);
            }
            else if (_selected.action == "news")
            {
                if (!string.IsNullOrEmpty(_selected.origin) && _selected.origin.StartsWith("n"))
                    AddLink("详见 · 相关部门", _selected.origin, null);
            }
            else if (_selected.action == "comp_review")
            {
                AddLink("详见 · 绩效薪酬", "n88_绩效与薪酬", null);
                AddLink("详见 · 员工关系", "n89_员工关系与文化", null);
            }
            else if (_selected.action == "parts_order")
            {
                AddLink("详见 · 航材", "n83_航材与供应链", null);
                AddLink("详见 · 订单确认", "n105_订单确认", null);
            }
            else if (_selected.action == "reg_inspect")
            {
                AddLink("详见 · 检查应对", "n100_检查应对弹窗", null);
                AddLink("详见 · 局方法规", "n95_局方与法规事务", null);
            }
            else if (_selected.action == "investment")
            {
                AddLink("详见 · 投资并购", "n79_战略投资与并购", null);
                AddLink("详见 · 财务预算", "n22_年度预算编制_审批", null);
                AddLink("详见 · 董事会", "n20_董事会", null);
            }
            else if (_selected.action == "codeshare" || _selected.action == "alliance_join")
            {
                AddLink("详见 · 联盟合作", "n78_联盟与战略合作", null);
                AddLink("详见 · 谈判", "n102_谈判弹窗", null);
            }
            else if (_selected.action == "buy_slot" || _selected.action == InboxManager.ApplySlotAction
                     || _selected.action == InboxManager.ChangeSlotAction)
            {
                AddLink("详见 · 航线总览", "n12_航线网络",
                    string.IsNullOrEmpty(_selected.origin) ? null : _selected.origin);
                AddLink("详见 · 航权时刻槽", "n77_航权与时刻槽", null);
                // 时刻槽单的 origin/dest 存的是「机场 / 时段序号」，不是航线两端 ——
                // 若照旧拼成 n41 的航线号就会跳到一个不存在的航线；交割后要办的事是回机队加机。
                AddLink("交割后 · 机队加机", "n9_机队", null);
            }
            else if (_selected.action == "apply_right")
            {
                AddLink("详见 · 航线总览", "n12_航线网络",
                    string.IsNullOrEmpty(_selected.origin) ? null : _selected.origin);
                AddLink("详见 · 航权时刻槽", "n77_航权与时刻槽", null);
                if (!string.IsNullOrEmpty(_selected.origin) && !string.IsNullOrEmpty(_selected.dest))
                    AddLink("详见 · 航线提案", "n41_单航线", $"{_selected.origin}-{_selected.dest}");
            }
            else if (_selected.action == "finance_crisis")
            {
                AddLink("详见 · 紧急融资", "n25_紧急融资_破产保护", null);
                AddLink("详见 · 条款确认", "n103_条款确认", null);
                AddLink("详见 · 重组", "n106_重组条件勾选", null);
            }
            else if (_selected.action == "ops_schedule")
            {
                AddLink("详见 · 周计划", "n42_航班周总计划", null);
                AddLink("详见 · 飞行计划", "n43_航班详细计划", null);
            }
            else if (_selected.action == InboxManager.AssignFlightAction)
            {
                AddLink("详见 · 此航线", "n41_单航线", _selected.origin);
                AddLink("详见 · 周计划", "n42_航班周总计划", null);
                AddLink("详见 · 机队", "n9_机队", null);
            }
            else if (_selected.action == InboxManager.CapacityDirectiveAction)
            {
                AddLink("详见 · 周计划", "n42_航班周总计划", null);
                AddLink("详见 · 此航线", "n41_单航线", _selected.origin);
                AddLink("详见 · 机队", "n9_机队", null);
            }
            else if (_selected.role == InboxManager.OpsQueryRole)
            {
                // 排班咨询答复：CEO 接过这封信最想核对的就是「本线班期 / 谁在飞 / 时刻槽」，
                // 故三个按钮都指回**本线** —— 给「航线总览 / 董事会」那对兜底按钮等于让他自己再找一遍。
                AddLink("详见 · 此航线", "n41_单航线", _selected.origin);
                AddLink("详见 · 周计划", "n42_航班周总计划", null);
                // n77 已内嵌进「航线」模块：JumpTo 会改投 n12 并直开「航权与时刻槽」页签。
                // 这里**不能**自己写 n12 + 本线键 —— n12 的键是城市三字码，塞 routeId 进去会污染城市经济页。
                AddLink("详见 · 航权时刻槽", "n77_航权与时刻槽", null);
            }
            else if (_selected.action == InboxManager.SeasonReviewAction)
            {
                AddLink("详见 · 机队", "n9_机队", null);
                AddLink("详见 · 周计划", "n42_航班周总计划", null);
                AddLink("详见 · 航线总览", "n12_航线网络", null);
            }
            else
            {
                AddLink("详见 · 航线总览", "n12_航线网络", null);
                AddLink("详见 · 董事会", "n20_董事会", null);
            }
        }

        void RefreshDecisionButtons()
        {
            bool news = _selected != null && _selected.action == "news";
            bool open = InboxManager.IsOpen(_selected) && !news;
            bool union = _selected != null && _selected.action == "union_talk";
            bool opsSked = _selected != null && _selected.action == "ops_schedule";
            bool capDir = _selected != null && _selected.action == InboxManager.CapacityDirectiveAction;
            bool season = _selected != null && _selected.action == InboxManager.SeasonReviewAction;
            bool inspect = _selected != null && _selected.action == "reg_inspect";
            bool inquiry = _selected != null && _selected.action == TransitionBusinessLogic.InquiryAction;
            bool assignAc = _selected != null && _selected.action == InboxManager.AssignAircraftAction;
            bool slotSel = _selected != null && _selected.action == InboxManager.SlotSelectAction;
            // ★2026-10-16：改时单（本线在该站已有槽、只是时刻滑点）—— 退回键的措辞要说明「不改时」，
            // 而不是笼统的「拒绝」（CEO 想留下那条槽、只是不想改到这一刻时，读起来才不像在退掉整件事）。
            bool changeSlot = _selected != null && _selected.action == InboxManager.ChangeSlotAction;
            string topReg = assignAc ? TopNominationReg(_selected) : "";
            string topSlot = slotSel ? TopSlotCandidateClock(_selected) : "";
            // 会议暂缓（补条件中）：批准/暂缓/拒绝隐藏，仅保留「重新召集」入口
            bool meetDeferred = InboxManager.MeetingOutcomeOf(_selected) == "deferred";
            SetBtnCaption(_approveBtn, union ? "让步" : inspect ? "全力配合" : inquiry ? "签约闲置机"
                : assignAc ? (string.IsNullOrEmpty(topReg) ? "批准" : $"选定 {topReg}（收益最优）")
                : slotSel ? (string.IsNullOrEmpty(topSlot) ? "批准" : $"选定 {topSlot}（推荐）") : "批准");
            SetBtnCaption(_deferBtn,
                union ? "强硬" : (opsSked || capDir || season) ? "提出修改" : inspect ? "重点整改" : "暂缓");
            SetBtnCaption(_rejectBtn,
                union ? "拖延" : (opsSked || capDir || season) ? "退回" : inspect ? "申请延期"
                : inquiry ? "本次不接" : assignAc ? "退回（不指派）"
                : slotSel ? "退回（本季不加班）" : changeSlot ? "退回（不改时）" : "拒绝");
            SetBtnColor(_approveBtn, UITheme.Success);
            SetBtnColor(_deferBtn, union ? UITheme.Danger : UITheme.PanelDeep);
            SetBtnColor(_rejectBtn, union ? UITheme.Warning : UITheme.Danger);
            if (_approveBtn != null) _approveBtn.interactable = open && !meetDeferred;
            if (_deferBtn != null) _deferBtn.interactable = open && !_revising && !meetDeferred;
            if (_rejectBtn != null) _rejectBtn.interactable = open && !meetDeferred;

            // 无需决策的邮件（办理回执/进度/通报，以及已批、已拒的旧件）不显示批复按钮
            bool need = InboxManager.RequiresDecision(_selected);
            if (_approveBtn != null) _approveBtn.gameObject.SetActive(need);
            // 执飞飞机提名没有「提出修改」这个中间态（改派要重新提名，不是改一句话的事）：
            // 选定在某架候选上，改主意就退回重下指令 —— 故本动作只留「选定 / 退回」两键。
            // 时刻备选同理：「暂缓」在时刻上没有中间态（不能既申请 07:30 又申请 08:00 占两条槽）。
            if (_deferBtn != null) _deferBtn.gameObject.SetActive(need && !assignAc && !slotSel);
            if (_rejectBtn != null) _rejectBtn.gameObject.SetActive(need);
            // 会议暂缓的邮件：「发起会议」按钮改为「重新召集」（条件齐备后再次上会）
            if (_meetBtn != null)
            {
                _meetBtn.onClick.RemoveAllListeners();
                if (meetDeferred)
                {
                    _meetBtn.onClick.AddListener(ReconveneMeeting);
                    _meetBtn.gameObject.SetActive(true);
                    SetBtnCaption(_meetBtn, "重新召集");
                }
                else
                {
                    _meetBtn.onClick.AddListener(OpenMeeting);
                    SetBtnCaption(_meetBtn, "发起会议");
                }
            }
            LayoutReplyBox();
        }

        void LayoutReplyBox()
        {
            bool ops = _selected != null && InboxManager.IsOpen(_selected)
                && (_selected.action == "ops_schedule"
                    || _selected.action == InboxManager.CapacityDirectiveAction
                    || _selected.action == InboxManager.SeasonReviewAction);
            if (_ceoReply != null) _ceoReply.gameObject.SetActive(ops);
            bool bar = (_approveBtn != null && _approveBtn.gameObject.activeSelf)
                || (_meetBtn != null && _meetBtn.gameObject.activeSelf);
            float footer = bar ? 48f : 8f;
            float replyH = ops ? 56f : 0f;
            float linksH = 24f;
            float bodyBottom = footer + replyH + linksH + 4f;
            if (_bodyViewport != null)
            {
                _bodyViewport.anchorMin = Vector2.zero;
                _bodyViewport.anchorMax = Vector2.one;
                _bodyViewport.offsetMin = new Vector2(12f, bodyBottom);
                _bodyViewport.offsetMax = new Vector2(-12f, -80f);
            }
            var linkRt = _detailLinks as RectTransform;
            if (linkRt != null)
            {
                float y0 = footer + replyH + 2f;
                linkRt.anchorMin = new Vector2(0f, 0f);
                linkRt.anchorMax = new Vector2(1f, 0f);
                linkRt.offsetMin = new Vector2(12f, y0);
                linkRt.offsetMax = new Vector2(-12f, y0 + linksH);
            }
            if (_ceoReply != null)
            {
                var rr = _ceoReply.GetComponent<RectTransform>();
                if (rr != null)
                {
                    rr.anchorMin = new Vector2(0f, 0f);
                    rr.anchorMax = new Vector2(1f, 0f);
                    rr.offsetMin = new Vector2(12f, footer + 2f);
                    rr.offsetMax = new Vector2(-12f, footer + replyH);
                }
            }
            float noteRight = -16f;
            if (_meetBtn != null && _meetBtn.gameObject.activeSelf) noteRight = -412f;
            else if (_approveBtn != null && _approveBtn.gameObject.activeSelf) noteRight = -308f;
            PlaceFooterNote(_detailDeadline, noteRight, footer);
            PlaceFooterNote(_hint, noteRight, footer);
            SyncFooterNotes();
            // 布局类调用不得动阅读位置（新信到达也会走到这里）：保留当前滚动。
            RefreshBodyScroll(preservePosition: true);
        }

        static void PlaceFooterNote(TextMeshProUGUI label, float right, float footer)
        {
            if (label == null) return;
            var rt = label.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = new Vector2(12f, 8f);
            rt.offsetMax = new Vector2(right, footer - 4f);
        }

        void SyncFooterNotes()
        {
            bool hasHint = _hint != null && !string.IsNullOrEmpty(_hint.text);
            if (_hint != null) _hint.gameObject.SetActive(hasHint);
            if (_detailDeadline != null) _detailDeadline.gameObject.SetActive(!hasHint);
        }

        static void SetBtnCaption(Button btn, string text)
        {
            if (btn == null) return;
            var t = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (t != null) t.text = text;
        }

        static void SetBtnColor(Button btn, Color color)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = color;
        }

        /// <summary>提名件里投影日利最优的机号（候选首位）；无候选返回空串。</summary>
        static string TopNominationReg(InboxMail m)
        {
            var c = AircraftNominationLogic.Decode(m != null ? m.payloadCsv : null);
            return c.Count > 0 && c[0] != null ? c[0].reg : "";
        }

        /// <summary>备选件里推荐的那一刻（候选首位，排序见 SlotCandidateLogic.Compare）；无候选返回空串。</summary>
        static string TopSlotCandidateClock(InboxMail m)
        {
            var o = SlotCandidateLogic.Decode(m != null ? m.payloadCsv : null);
            return o.Count > 0 && o[0] != null ? o[0].depClock : "";
        }

        /// <summary>CEO 在提名件里点选某一架候选：批准那一刻才写进航线（退回＝没提过）。</summary>
        void PickNomination(string reg)
        {
            if (_selected == null) return;
            InboxManager.ApproveAssignAircraft(GameState.Instance, _selected, reg, out string msg);
            if (_hint != null) _hint.text = msg ?? "";
            RebuildList();
            RefreshDecisionButtons();
            if (_detailBody != null && _selected != null)
                _detailBody.text = TermNavMap.Decorate(_selected.body);
        }

        /// <summary>
        /// CEO 在备选件里点定某一刻（去程离港时刻）：**点定那一刻才去开申请单**，退回＝没提过。
        /// 与 <see cref="PickNomination"/> 同源 —— 选定动作都必须落在「点了哪一个」上，
        /// 而不是靠 CEO 去批复框里把时刻抄一遍（抄错一位就是买错一条槽）。
        /// </summary>
        void PickSlotCandidate(string clock)
        {
            if (_selected == null) return;
            InboxManager.ApproveSlotSelect(GameState.Instance, _selected, clock, out string msg);
            if (_hint != null) _hint.text = msg ?? "";
            RebuildList();
            RefreshDecisionButtons();
            if (_detailBody != null && _selected != null)
                _detailBody.text = TermNavMap.Decorate(_selected.body);
        }

        void OnKeywordClicked(string linkId)
        {
            if (!TermNavMap.TryResolve(linkId, out var hit)) return;
            // 航班号链接带班次下标：n43 是单班页，不把下标写回去就会停在「上一次看的那一班」。
            // 必须在 JumpTo 之前写 —— JumpTo 的 routeKey 分支会另行覆盖 SelectedRouteId。
            if (hit.pageId != null && hit.pageId.StartsWith("n43", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(hit.routeKey))
            {
                OpsSubpageContext.SelectFlight(hit.routeKey, hit.flightIndex);
            }
            if (hit.pageId == "n41_单航线" && string.IsNullOrEmpty(hit.routeKey)
                && _selected != null && !string.IsNullOrEmpty(_selected.origin))
            {
                // 邮件里只存着陆点（origin 多为 routeId，老件/open_route 存机场三字码）。
                // 先拿 origin 当键试解析，解析不出再按「出发-到达」拼机场对；两种都认不出时
                // 才交给 n41 如实说「未选择航线」—— 绝不替 CEO 挑第一条航线（见 RouteDetailPanel.CurrentRoute）。
                string key = _selected.origin;
                if (RouteData.FindByKey(GameState.Instance?.activeRoutes, key) == null
                    && !string.IsNullOrEmpty(_selected.dest))
                    key = $"{_selected.origin}-{_selected.dest}";
                JumpTo(hit.pageId, key);
            }
            else
                JumpTo(hit.pageId, hit.routeKey);
        }

        void JumpTo(string pageId, string routeKey)
        {
            // n77 航权与时刻槽已内嵌进「航线」模块页签：深链统一改投 n12 并直开该页签。
            // 意图必须在这里先记下来：n12 页面若尚未建过，NetworkPanelUI.OnEnable 会消费掉
            // OpenRightsTab，随后 BuildContent 末尾的 SetTab(0) 又把页签抹回总览，
            // 只靠下面那句 `&& OpenRightsTab` 就会漏掉这次深链（首次进 n12 停错页签）。
            bool openRightsTab = pageId != null && pageId.StartsWith("n77", StringComparison.Ordinal);
            if (openRightsTab)
            {
                FleetNetContext.OpenRightsTab = true;
                pageId = "n12_航线网络";
            }
            if (pageId != null && pageId.StartsWith("n37") && _selected != null && _selected.action == "union_talk")
            {
                HrSubpagePanel.PendingUnionMailId = _selected.id;
            }
            if (_selected != null && _selected.action == "finance_crisis"
                && pageId != null && (pageId.StartsWith("n25") || pageId.StartsWith("n103") || pageId.StartsWith("n106")))
            {
                FinanceContext.PendingFinanceMailId = _selected.id;
                if (!InboxManager.IsFinanceRestructure(_selected) && pageId.StartsWith("n103"))
                    FinanceContext.FinancingKind = InboxManager.ParseFinanceKind(_selected);
            }
            if (_selected != null && _selected.action == "reg_inspect"
                && pageId != null && (pageId.StartsWith("n100") || pageId.StartsWith("n95")))
            {
                SafetyContext.PendingInspectMailId = _selected.id;
            }
            if (_selected != null && (_selected.action == "ops_schedule"
                || _selected.action == InboxManager.AssignFlightAction
                || _selected.action == InboxManager.AssignAircraftAction
                || _selected.action == InboxManager.CapacityDirectiveAction
                // 备选件的 origin 存的是 routeId（与加机/派机同源）。**不**含 apply_slot/buy_slot：
                // 那两类单子的 origin 是机场三字码，塞进 SelectedRouteId 会让 n41 落到一条不存在的航线。
                || _selected.action == InboxManager.SlotSelectAction))
            {
                OpsSubpageContext.SelectedRouteId = _selected.origin;
            }
            if (pageId != null && pageId.StartsWith("n24") && _selected != null && _selected.action == "irrops")
            {
                OpsIrropsModalUI.PendingMailId = _selected.id;
                OpsPanelUI.OpenIrrops(InboxManager.ParseIrropsKind(_selected));
                return;
            }
            if (pageId != null && pageId.StartsWith("n97") && _selected != null && _selected.action == "capacity_plan")
            {
                FleetUI.OpenPurchaseMeeting();
                return;
            }
            if (pageId != null && (pageId.StartsWith("n117") || pageId.Contains("过渡业务")))
            {
                FleetUI.OpenTransitionTab();
                return;
            }
            if (!string.IsNullOrEmpty(routeKey))
            {
                if (pageId != null && pageId.StartsWith("n61"))
                {
                    AIManager.Instance?.EnsureAiGenerated();
                    MktContext.SelectedCompetitorId = routeKey;
                }
                else if (pageId != null && pageId.StartsWith("n12"))
                {
                    FleetNetContext.SelectedCityIata = routeKey;
                    FleetNetContext.OpenEconomyTab = true;
                }
                else if (pageId != null && pageId.StartsWith("n10"))
                {
                    FleetNetContext.SelectedRegistration = routeKey;
                    OpsSubpageContext.SelectedRegistration = routeKey;
                }
                else
                {
                    FleetNetContext.SelectedRouteId = routeKey;
                    OpsSubpageContext.SelectedRouteId = routeKey;
                }
            }

            var nav = FindFirstObjectByType<PrefabNavIntegration>();
            if (nav == null) return;

            if (IsShellTabPage(pageId))
                nav.SelectTab(pageId);
            else
                nav.OpenSubPage(pageId);

            if (pageId != null && pageId.StartsWith("n12") && !string.IsNullOrEmpty(routeKey))
            {
                var net = FindFirstObjectByType<NetworkPanelUI>(FindObjectsInactive.Include);
                net?.ShowEconomyForCity(routeKey);
            }
            else if (pageId != null && pageId.StartsWith("n12") && (openRightsTab || FleetNetContext.OpenRightsTab))
            {
                // n12 已是激活页签时 OnEnable 不会重发；首次构建时 OnEnable 又会被 BuildContent
                // 末尾的 SetTab(0) 覆盖 —— 两种情况都靠这里按入口意图再切一次，口径与
                // PrefabNavIntegration.OpenGameplayPage 的 n77 分支一致。
                var net = FindFirstObjectByType<NetworkPanelUI>(FindObjectsInactive.Include);
                net?.ShowRightsTab();
            }
        }

        void SignInquiryOffer(string offerId)
        {
            if (_selected == null) return;
            TransitionBusinessLogic.SignOneFromInbox(GameState.Instance, _selected, offerId, out string msg);
            if (_hint != null) _hint.text = msg ?? "";
            var stay = _selected;
            RebuildList();
            if (stay != null) SelectMail(stay);
            else RefreshDecisionButtons();
        }

        void Approve()
        {
            if (_selected == null) return;
            if (_selected.action == "irrops")
            {
                OpsIrropsModalUI.PendingMailId = _selected.id;
                var kind = InboxManager.ParseIrropsKind(_selected);
                OpsPanelUI.OpenIrrops(kind);
                if (_hint != null) _hint.text = "请在航班异常弹窗中选择 A / B / C。";
                return;
            }
            if (_selected.action == "union_talk")
            {
                InboxManager.ApplyUnion(GameState.Instance, _selected, 0, out string umsg);
                if (_hint != null) _hint.text = umsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == "finance_crisis")
            {
                FinanceContext.PendingFinanceMailId = _selected.id;
                if (InboxManager.IsFinanceRestructure(_selected))
                {
                    FinancePanelUI.OpenFinancePage("n106_重组条件勾选");
                    if (_hint != null) _hint.text = "请在重组页确认条款后生效。";
                }
                else
                {
                    FinanceContext.FinancingKind = InboxManager.ParseFinanceKind(_selected);
                    FinancePanelUI.OpenFinancePage("n103_条款确认");
                    if (_hint != null) _hint.text = "请在条款确认页签署后生效。";
                }
                return;
            }
            if (_selected.action == "reg_inspect")
            {
                InboxManager.ApplyInspection(GameState.Instance, _selected, 0, out string rmsg);
                if (_hint != null) _hint.text = rmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == "ops_schedule")
            {
                InboxManager.ApproveOpsSchedule(GameState.Instance, _selected, out string omsg);
                if (_hint != null) _hint.text = omsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == InboxManager.CapacityDirectiveAction)
            {
                InboxManager.ApproveCapacityDirective(GameState.Instance, _selected, out string cmsg);
                if (_hint != null) _hint.text = cmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == InboxManager.AssignAircraftAction)
            {
                // 「批准」＝选定收益最优的那架（首位候选）；要挑别的请点候选行上的「选定 · 机号」。
                InboxManager.ApproveAssignAircraft(GameState.Instance, _selected, null, out string acmsg);
                if (_hint != null) _hint.text = acmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == InboxManager.SlotSelectAction)
            {
                // 「批准」＝采用运营部推荐的那一刻（备选表首条）；要挑别的请点候选行上的「N. 时刻」。
                // 不替 CEO 瞎猜一个时刻去买：推荐条已在正文里写明了到底省不省钱、排不排得下机。
                InboxManager.ApproveSlotSelect(GameState.Instance, _selected, null, out string sselmsg);
                if (_hint != null) _hint.text = sselmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == InboxManager.SeasonReviewAction)
            {
                InboxManager.ApproveSeasonReview(GameState.Instance, _selected, out string smsg);
                if (_hint != null) _hint.text = smsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == InboxManager.SlotUsageAction)
            {
                InboxManager.ApproveSlotUsage(GameState.Instance, _selected, out string jmsg);
                if (_hint != null) _hint.text = jmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            bool approved = InboxManager.Approve(GameState.Instance, _selected, out string msg);
            if (!approved && _selected != null && InboxManager.IsOpen(_selected))
            {
                // 处理器无法执行（如前置缺失）：直接结案，避免决策按钮残留、邮件永久挂起
                InboxManager.Mark(GameState.Instance, _selected.id, "approved");
                if (string.IsNullOrEmpty(msg)) msg = "已批准。";
            }
            if (_selected.action == "open_route")
            {
                if (_hint != null) _hint.text = "";
                var follow = FindAdmitFollowUp(_selected);
                RebuildList();
                if (follow != null)
                    SelectMail(follow);
                else
                    RefreshDecisionButtons();
                return;
            }
            if (_hint != null) _hint.text = msg ?? "";
            RebuildList();
            RefreshDecisionButtons();
        }

        InboxMail FindAdmitFollowUp(InboxMail src)
        {
            var mails = Mails;
            if (mails == null || src == null) return null;
            string o = src.origin ?? "";
            string d = src.dest ?? "";
            for (int i = 0; i < mails.Count; i++)
            {
                var m = mails[i];
                if (m == null || m.action != "permit_progress") continue;
                if (m.id == null || m.id.IndexOf("admit", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!string.Equals(m.origin, o, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(m.dest, d, StringComparison.OrdinalIgnoreCase)) continue;
                return m;
            }
            return null;
        }

        static bool IsShellTabPage(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return false;
            var tabs = AirlineCEO.UI.Navigation.UIPrefabCatalog.ShellTabs;
            for (int i = 0; i < tabs.Length; i++)
            {
                if (pageId == tabs[i].pageId) return true;
                if (pageId.StartsWith(tabs[i].pageId, StringComparison.Ordinal)) return true;
            }
            return pageId.StartsWith("n12", StringComparison.Ordinal);
        }

        /// <summary>
        /// 重排正文并设定滚动位置。
        /// <paramref name="preservePosition"/> = true 时保留当前阅读位置（同一封信重绘）；
        /// false 时回到顶部（换了信）。默认 false 只服务真正的「换信」路径，
        /// 布局类调用（如 <see cref="LayoutReplyBox"/>）必须传 true 或走默认保留。
        /// </summary>
        void RefreshBodyScroll(bool preservePosition = false)
        {
            if (_detailBody == null) return;
            float keep = _bodyScroll != null ? Mathf.Clamp01(_bodyScroll.verticalNormalizedPosition) : 1f;
            _detailBody.ForceMeshUpdate();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_detailBody.rectTransform);
            if (_bodyViewport != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_bodyViewport);
            Canvas.ForceUpdateCanvases();
            if (_bodyScroll != null)
                _bodyScroll.verticalNormalizedPosition = preservePosition ? keep : 1f;
        }

        void Mark(string status)
        {
            if (_selected == null) return;
            if (status == "rejected" && _selected.action == "irrops")
            {
                InboxManager.RejectIrrops(GameState.Instance, _selected, out string msg);
                if (_hint != null) _hint.text = msg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == "union_talk")
            {
                int choice = status == "deferred" ? 1 : 2;
                InboxManager.ApplyUnion(GameState.Instance, _selected, choice, out string umsg);
                if (_hint != null) _hint.text = umsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (status == "rejected" && _selected.action == InboxManager.FuelHedgeAction)
            {
                InboxManager.RejectFuelHedge(GameState.Instance, _selected, out string hmsg);
                if (_hint != null) _hint.text = hmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (status == "rejected" && _selected.action == "finance_crisis")
            {
                InboxManager.RejectFinanceCrisis(GameState.Instance, _selected, out string fmsg);
                if (_hint != null) _hint.text = fmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (status == "rejected" && _selected.action == "investment")
            {
                InboxManager.RejectInvestment(GameState.Instance, _selected, out string imsg);
                if (_hint != null) _hint.text = imsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == "reg_inspect")
            {
                int choice = status == "deferred" ? 1 : 2;
                InboxManager.ApplyInspection(GameState.Instance, _selected, choice, out string rmsg);
                if (_hint != null) _hint.text = rmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (status == "rejected" && _selected.action == StaffRecruitment.HireAction)
            {
                StaffRecruitment.RejectOffer(GameState.Instance, _selected, out string hmsg);
                if (_hint != null) _hint.text = hmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (status == "rejected" && _selected.action == "parts_order")
            {
                InboxManager.RejectPartsOrder(GameState.Instance, _selected, out string pmsg);
                if (_hint != null) _hint.text = pmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (status == "rejected" && _selected.action == "comp_review")
            {
                InboxManager.RejectCompensation(GameState.Instance, _selected, out string cmsg);
                if (_hint != null) _hint.text = cmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (status == "rejected" && _selected.action == "annual_goals")
            {
                InboxManager.RejectAnnualGoals(GameState.Instance, _selected, out string ag);
                if (_hint != null) _hint.text = ag ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (status == "rejected" && _selected.action == "capacity_plan")
            {
                InboxManager.RejectCapacity(GameState.Instance, _selected, out string cap);
                if (_hint != null) _hint.text = cap ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == "ops_schedule")
            {
                if (status == "deferred")
                {
                    StartCoroutine(ReviseOpsFromCeoNote());
                    return;
                }
                InboxManager.RejectOpsSchedule(GameState.Instance, _selected, out string omsg);
                if (_hint != null) _hint.text = omsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == InboxManager.CapacityDirectiveAction)
            {
                if (status == "deferred")
                {
                    StartCoroutine(ReviseOpsFromCeoNote());
                    return;
                }
                InboxManager.RejectCapacityDirective(GameState.Instance, _selected, out string cmsg);
                if (_hint != null) _hint.text = cmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == InboxManager.SeasonReviewAction)
            {
                if (status == "deferred")
                {
                    StartCoroutine(ReviseOpsFromCeoNote());
                    return;
                }
                InboxManager.RejectSeasonReview(GameState.Instance, _selected, out string smsg);
                if (_hint != null) _hint.text = smsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == InboxManager.SlotUsageAction)
            {
                // 豁免申请的「暂缓」没有中间态（不能既申请又不申请）——一律按驳回处置，
                // 与「到期未处理视同驳回」同一口径，避免出现第三分支。
                InboxManager.RejectSlotUsage(GameState.Instance, _selected, out string jmsg);
                if (_hint != null) _hint.text = jmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (status == "rejected" && _selected.action == TransitionBusinessLogic.InquiryAction)
            {
                TransitionBusinessLogic.DeclineInquiry(GameState.Instance, _selected, out string dmsg);
                if (_hint != null) _hint.text = dmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (status == "rejected" && _selected.action == InboxManager.AssignFlightAction)
            {
                // 加机呈报的退回是「什么都不发生」——班次从未落过，故无需撤销任何东西。
                InboxManager.RejectAssignFlight(GameState.Instance, _selected, out string amsg);
                if (_hint != null) _hint.text = amsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == InboxManager.AssignAircraftAction)
            {
                // 同源：机号从未写进航线，退回即等于没提过（无机航线继续停飞）。
                InboxManager.RejectAssignAircraft(GameState.Instance, _selected, out string acrmsg);
                if (_hint != null) _hint.text = acrmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            if (_selected.action == InboxManager.SlotSelectAction)
            {
                // 时刻从未去申请过，退回即等于没提过（不申请、不花钱、班次维持现状）。
                // 「暂缓」也走这条：时刻上没有中间态，挂着既占一封待批也拦不住时间。
                InboxManager.RejectSlotSelect(GameState.Instance, _selected, out string slrmsg);
                if (_hint != null) _hint.text = slrmsg ?? "";
                RebuildList();
                RefreshDecisionButtons();
                return;
            }
            InboxManager.Mark(GameState.Instance, _selected.id, status);
            RebuildList();
        }

        IEnumerator ReviseOpsFromCeoNote()
        {
            if (_revising || _selected == null) yield break;
            bool capacity = _selected.action == InboxManager.CapacityDirectiveAction;
            bool season = _selected.action == InboxManager.SeasonReviewAction;
            string note = _ceoReply != null ? (_ceoReply.text ?? "").Trim() : "";
            if (string.IsNullOrEmpty(note))
            {
                if (_hint != null)
                    _hint.text = capacity
                        ? "请先写下新的调令，例如：再加一班红眼，并保证同机连班。"
                        : season
                            ? "请先写下评审意见，例如：本季只整合日循环，不要加密；或把亏损航线减到 1 班。"
                            : "请先写下给运营部的批复，例如：客货流很大，要从早班排到红眼，挖满潜力。";
                yield break;
            }

            _revising = true;
            RefreshDecisionButtons();
            if (_hint != null)
                _hint.text = capacity ? "运营部正在按新调令重编运力与航班环…"
                    : season ? "运营部正在按您的意见重编换季方案…"
                    : "运营部正在按您的批复改航班计划…";

            // 「改稿」框里写的是疑问句：不必再问模型，也不必改稿（2026-10-13 与单航线页同源）。
            // 这里先挡一道只是为了**省掉一次模型调用**；真正的拦截在 ReviseCapacityDirective 里。
            bool askOnly = capacity && CapacityDirectiveLogic.LooksLikeQuestion(note);

            string aiJson = null;
            bool done = false;
            var ai = AIChatManager.Instance;
            if (ai != null && !season && !askOnly)
            {
                string origin = _selected.origin;
                string dest = _selected.dest;
                RouteData route = capacity ? CurrentRouteFor(_selected) : null;
                string sys = capacity
                    ? CapacityDirectiveLogic.BuildParseSystemPrompt()
                    : "你是航空公司运营部调度。只输出 JSON："
                      + "{\"coverAllSlots\":true,\"maximize\":true,\"days\":\"每日\",\"depsCsv\":\"08:00,12:00,18:00,22:00\",\"rationale\":\"一句中文说明\"}。"
                      + "depsCsv 只能从 08:00、12:00、18:00、22:00 中选。CEO 要求早班到红眼或挖潜力时 coverAllSlots 与 maximize 为 true。";
                string user = capacity
                    ? CapacityDirectiveLogic.BuildParseUserPrompt(GameState.Instance, route, note)
                    : $"航线 {origin}-{dest}。CEO 批复：{note}";
                ai.AskRawJson(sys, user, j => { aiJson = j; done = true; });
                float t = 0f;
                while (!done && t < 5f)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            else
            {
                done = true;
            }

            var mail = _selected;
            string msg;
            bool ok;
            if (capacity)
            {
                ok = InboxManager.ReviseCapacityDirective(GameState.Instance, mail, note, aiJson, out string cmsg);
                msg = cmsg;
            }
            else if (season)
            {
                ok = InboxManager.ReviseSeasonReview(GameState.Instance, mail, note, out string smsg);
                msg = smsg;
            }
            else
            {
                ok = InboxManager.ReviseOpsSchedule(GameState.Instance, mail, note, aiJson, out string omsg);
                msg = omsg;
            }
            _revising = false;
            // 失败时**保留**CEO 写的原话：失败原因通常是「没读懂/运力不足」，要改的只是措辞或班次数量，
            // 把整段话清掉等于罚他重写一遍（而且他还得先想起来自己写过什么）。
            if (_ceoReply != null && ok) _ceoReply.text = "";
            if (_hint != null) _hint.text = msg ?? "";
            RebuildList();
            RefreshDecisionButtons();
            LayoutReplyBox();
            if (_detailBody != null && mail != null)
                _detailBody.text = TermNavMap.Decorate(mail.body);
        }

        static RouteData CurrentRouteFor(InboxMail mail)
        {
            var gs = GameState.Instance;
            if (gs?.activeRoutes == null || mail == null) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r != null && r.routeId == mail.origin) return r;
            }
            return null;
        }

        void OpenMeeting()
        {
            // 「发起会议」仅在离开引导后的自由玩法中提供（引导期间按钮已隐藏）。
            // 以当前选中邮件为议题来源：会议裁决后回写该函，回收件箱不再显示决策按钮。
            var mgr = DecisionMeetingManager.Instance;
            if (mgr != null && _selected != null)
            {
                var p = mgr.PrepareDefault(_selected.id);
                if (!string.IsNullOrEmpty(_selected.subject)) p.topic = _selected.subject;
                mgr.OpenMeeting(p);
            }
            FindFirstObjectByType<PrefabNavIntegration>()?.OpenSubPage("n21_CEO_决策会议");
        }

        /// <summary>会议暂缓（补条件中）的邮件：条件齐备后由此重新召集会议，再次上会裁决。</summary>
        void ReconveneMeeting()
        {
            if (_selected == null) return;
            _selected.meetingOutcome = "";   // 重新上会：清除上次「暂缓」裁决，恢复可决策态
            var mgr = DecisionMeetingManager.Instance;
            if (mgr != null)
            {
                var p = mgr.PrepareDefault(_selected.id);
                if (!string.IsNullOrEmpty(_selected.subject)) p.topic = _selected.subject;
                mgr.OpenMeeting(p);
            }
            InboxManager.NotifyChanged();
            FindFirstObjectByType<PrefabNavIntegration>()?.OpenSubPage("n21_CEO_决策会议");
        }
    }
}
