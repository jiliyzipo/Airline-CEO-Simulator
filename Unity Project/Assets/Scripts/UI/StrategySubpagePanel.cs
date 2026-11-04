using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>STG 子页共享上下文。</summary>
    public static class StrategyContext
    {
        public static string NegotiatePartner = "新加坡航空";
        public static float NegotiateSplit = 0.5f;   // 我方分成
    }

    /// <summary>
    /// STG 子页：n76 机队与运力规划 / n77 航权与时刻槽 / n78 联盟与战略合作 /
    /// n79 战略投资与并购 / n80 战略研究与预案 / n35 RPM 评估台 / n102 谈判弹窗。
    /// </summary>
    public class StrategySubpagePanel : BasePanel
    {
        [SerializeField] string pageKind;

        string _pageKind;
        Transform _content;
        /// <summary>
        /// 当前渲染宿主：默认就是 <see cref="_content"/>；n77 页在顶部让出一条二级标签后
        /// 把正文挂到下面的子面板上，于是正文自己的坐标仍然从 0 到 1 算，不必逐个减偏移。
        /// </summary>
        Transform _bodyHost;
        TextMeshProUGUI _toast;
        float _rpmFreq = 7f;
        int _slotAirportIndex = -1;   // n77：时刻槽目标机场下标（第一层，−1 = 未选）
        int _slotClockIndex;          // n77：该机场下的具体时刻下标（第二层）
        string _mkSel = "";           // n77/二级市场：选中的一行（"mkt:<挂牌号>" / "own:<持仓下标>" / "mine:<挂牌号>"）

        /// <summary>n77 的二级标签：0 = 持仓与在办，1 = 时刻二级市场。</summary>
        public int SubTab { get; set; }

        /// <summary>
        /// 构建子页。
        /// </summary>
        /// <param name="embedded">
        /// true = **嵌进模块页签**（航线模块内嵌 n77）：不再套一层半透明遮罩与卡片边框，
        /// 正文直接铺满调用方给的区域。原先两层内边距（模块 0.01 + 卡片 0.06）叠起来只剩
        /// 约 84% 的可用面积，等于把模块页签本身就有的边框又画了一遍 —— 用户要求
        /// 「框架放大到最大红框」，指的就是去掉这一层。
        /// </param>
        public static StrategySubpagePanel Build(string pageKind, string title, bool embedded = false)
        {
            GameObject root;
            GameObject card;
            Transform contentParent;
            UIFactory.Anchor contentAnchor;
            CanvasGroup cg;

            if (embedded)
            {
                // 底色透明：模块页签（NetworkPanelUI）已经是一块带边框的深色板，
                // 再糊一层半透明遮罩会让整页发灰，也吃掉可用面积。
                root = UIFactory.Panel(null, "StgSub_" + pageKind, UIFactory.Fill,
                    new Color(0, 0, 0, 0), rounded: false);
                var rootImg = root.GetComponent<Image>();
                if (rootImg != null) rootImg.raycastTarget = false;
                contentParent = root.transform;
                contentAnchor = new UIFactory.Anchor(new Vector2(0.012f, 0.012f), new Vector2(0.988f, 0.988f),
                    Vector2.zero, Vector2.zero);
                card = root;
                cg = root.AddComponent<CanvasGroup>();
            }
            else
            {
                root = UIFactory.Panel(null, "StgSub_" + pageKind, UIFactory.Fill,
                    new Color(0.02f, 0.05f, 0.1f, 0.72f), rounded: false);
                card = UIFactory.Panel(root.transform, "Card",
                    new UIFactory.Anchor(new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero),
                    UITheme.Panel);
                UIFactory.AddBorder(card, UITheme.Border, new Vector2(1.2f, -1.2f));
                cg = card.AddComponent<CanvasGroup>();
                contentParent = card.transform;
                contentAnchor = new UIFactory.Anchor(new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.88f),
                    Vector2.zero, Vector2.zero);
            }

            TextMeshProUGUI titleLabel = null;
            Button close = null;
            if (!embedded)
            {
                titleLabel = UIFactory.Label(card.transform, "Title", title, 22,
                    TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.04f, 0.90f), new Vector2(0.78f, 0.98f), Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary, FontStyles.Bold);
                close = UIFactory.Button(card.transform, "Close", "×",
                    new UIFactory.Anchor(new Vector2(0.90f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, () => { }, 20);
                UIFactory.Button(card.transform, "Back", "← 返回",
                    new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.20f, 0.10f), Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, () => { }, 14);
            }

            var content = UIFactory.Panel(contentParent, "Content", contentAnchor,
                new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;

            // Toast 挂在**内容之外**：正文每次 Refresh 都会 Clear(_content)，
            // 提示条若在里面，刷新一次就没了（也会被下一次 Clear 删掉）。
            // 内嵌模式下它落在二级标签条最右侧（那里正好是空位），与标签、面包屑互不侵占；
            // 独立卡片模式下仍留在卡片底部。
            var toast = UIFactory.Label(card.transform, "Toast", "", 12,
                embedded ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Center,
                embedded
                    ? new UIFactory.Anchor(new Vector2(0.735f, 0.921f), new Vector2(0.985f, 0.983f), Vector2.zero, Vector2.zero)
                    : new UIFactory.Anchor(new Vector2(0.20f, 0.005f), new Vector2(0.80f, 0.03f), Vector2.zero, Vector2.zero),
                UITheme.Warning);
            if (embedded) UIFactory.ClampSingleLine(toast);
            // Toast 是后建的兄弟节点，但正文宿主 Content 也是后建的 —— 不抬到最上层
            // 就会被标签条的半透明底衬盖住（看起来像提示发灰）。
            toast.transform.SetAsLastSibling();

            var panel = root.AddComponent<StrategySubpagePanel>();
            panel.pageKind = pageKind;
            panel._pageKind = pageKind;
            panel._content = content.transform;
            panel._toast = toast;
            panel.AssignRefs(root.GetComponent<RectTransform>(), titleLabel, close, cg);
            panel.BindPage(pageKind, title);
            panel.RefreshBody();
            return panel;
        }

        public override void BindPage(string pageId, string title = null)
        {
            base.BindPage(pageId, title);
            _pageKind = NormalizeKind(pageId);
            pageKind = _pageKind;
            if (titleLabel != null && !string.IsNullOrEmpty(title))
                titleLabel.text = title;
        }

        protected override void Awake()
        {
            if (root == null) root = transform as RectTransform;
            ResolveRefs();
            WireBackButton();
        }

        void OnEnable()
        {
            ResolveRefs();
            WireBackButton();
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
            if (_toast == null)
            {
                var t = transform.Find("Card/Toast");
                if (t != null) _toast = t.GetComponent<TextMeshProUGUI>();
            }
            if (string.IsNullOrEmpty(_pageKind))
                _pageKind = NormalizeKind(!string.IsNullOrEmpty(pageKind) ? pageKind : PageId);
        }

        public void RefreshBody()
        {
            ResolveRefs();
            if (_content == null) return;
            _bodyHost = null;            // 每个子页重建时先回到默认宿主
            Clear(_content);

            switch (_pageKind)
            {
                case "n76": RenderFleetPlan(); break;
                case "n77": RenderRightsHub(); break;
                case "n78": RenderAlliance(); break;
                case "n79": RenderInvestment(); break;
                case "n80": RenderResearch(); break;
                case "n35": RenderRpm(); break;
                case "n102": RenderNegotiate(); break;
                case "n108": RenderCommerce(); break;
                default: RenderFallback(); break;
            }
            _bodyHost = null;
        }

        static string NormalizeKind(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return "";
            if (pageId.StartsWith("n76")) return "n76";
            if (pageId.StartsWith("n77")) return "n77";
            if (pageId.StartsWith("n78")) return "n78";
            if (pageId.StartsWith("n79")) return "n79";
            if (pageId.StartsWith("n80")) return "n80";
            if (pageId.StartsWith("n35")) return "n35";
            if (pageId.StartsWith("n102")) return "n102";
            if (pageId.StartsWith("n108")) return "n108";
            // n111 是一号两名：二级市场（已并入 n77 的二级标签）与 CEO 聘任合同。
            // 只把二级市场那一支并进来；合同页有自己的面板，误并会把它渲成航线页。
            if (pageId.StartsWith("n111") && (pageId.Contains("二级市场") || pageId.Contains("时刻"))) return "n77";
            return pageId;
        }

        void Toast(string msg) { if (_toast != null) _toast.text = msg ?? ""; }

        /// <summary>当前渲染宿主（n77 让出二级标签后指向正文子面板，其余页就是内容区）。</summary>
        Transform Host => _bodyHost != null ? _bodyHost : _content;

        void Clear(Transform host)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
                Destroy(host.GetChild(i).gameObject);
        }

        TextMeshProUGUI L(string name, string text, float x0, float y0, float x1, float y1,
            int size, Color? color = null, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            return UIFactory.Label(Host, name, text, size, align,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.TextPrimary, bold ? FontStyles.Bold : FontStyles.Normal);
        }

        Button B(string name, string text, float x0, float y0, float x1, float y1,
            Color? color, UnityEngine.Events.UnityAction action, int size = 13)
        {
            return UIFactory.Button(Host, name, text,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color ?? UITheme.Accent, action, size);
        }

        void RenderFallback()
        {
            L("F", "该子页规格尚未展开。", 0.0f, 0.5f, 1.0f, 0.7f, 16, UITheme.TextMuted);
        }

        // ================= n76 机队与运力规划 =================
        void RenderFleetPlan()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            int fleet = gs.fleet != null ? gs.fleet.Count : 0;
            int airworthy = FleetManager.Instance != null ? FleetManager.Instance.CountAirworthy(gs) : 0;
            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int gap = CapacityPlanning.Gap(routes, airworthy);
            float ratio = CapacityPlanning.GapRatio(routes, airworthy);
            bool red = ratio > CapacityPlanning.WarnRatio;
            L("H", "机队与运力规划 · 飞机全生命周期管理", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Kpi", $"机队 {fleet} 架 · 在役 {airworthy} · 航线 {routes} · 缺口 {gap} 架（{ratio * 100f:F0}%）{(red ? " · 红灯>20%" : "")}",
                0.0f, 0.82f, 1.0f, 0.90f, 13, red ? UITheme.Danger : UITheme.Success, true);

            L("S1", "五年机队规划：缺口由「在营航线 − 在役架数」计算。在役 = 已分配航线的飞机；未分配的在册飞机不计入（即便机队页每行写着「适航」）。补强提案在收件箱批准后，到机队页开会引进。", 0.0f, 0.68f, 1.0f, 0.78f, 12, UITheme.TextMuted);
            L("S2", "实际买新机 / 二手 / 租赁在底栏「机队」完成。", 0.0f, 0.60f, 1.0f, 0.68f, 12, UITheme.TextMuted);
            L("S3", "OEM 供应商关系与退租检仍由机务页承接。", 0.0f, 0.52f, 1.0f, 0.60f, 12, UITheme.TextMuted);
            L("S4", gap > 0 ? $"当前建议引进 {gap} 架，否则客座与准点承压。" : "当前运力覆盖航网，备份运力请到机队引进。",
                0.0f, 0.44f, 1.0f, 0.52f, 12, UITheme.TextPrimary);

            if (gap > 0)
            {
                B("Buy", "提交运力补强决策", 0.02f, 0.30f, 0.32f, 0.40f, UITheme.Accent,
                    () =>
                    {
                        InboxManager.QueueCapacityMail(gs, gap);
                        Toast("运力补强提案已送收件箱。");
                    }, 13);
                B("Fleet", "去机队引进", 0.36f, 0.30f, 0.66f, 0.40f, UITheme.PanelDeep,
                    () => FleetUI.OpenPurchaseMeeting(), 12);
            }
            else
            {
                B("Fleet", "去机队引进", 0.02f, 0.30f, 0.48f, 0.40f, UITheme.Accent,
                    () => FleetUI.OpenPurchaseMeeting(), 13);
            }
            B("Finance", "财务影响预测 → 财务", 0.70f, 0.30f, 0.98f, 0.40f, UITheme.PanelDeep,
                () => FinancePanelUI.OpenFinancePage("n22_年度预算编制_审批"), 12);
        }

        // ================= n77 航权与时刻槽 =================
        /// <summary>
        /// n77 中心页：顶部一条二级标签「持仓与在办 / 时刻二级市场」，正文挂在下方子面板。
        ///
        /// ★2026-11-02（用户裁定）：二级市场原先占模块顶部一整个页签，但它与协调池申请
        /// 是**同一件事的两条取得渠道** —— 分在模块级两个页签里，玩家得先记住「哪个页签买、
        /// 哪个页签申请」。收进 n77 的二级标签后，两条渠道同屏可切、来源关系一眼可见。
        /// </summary>
        void RenderRightsHub()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var stg = StrategyManager.EnsureState(gs);

            int openOffers = 0;
            var mkt = SlotMarketLogic.Ensure(gs);
            if (mkt?.offers != null)
            {
                for (int i = 0; i < mkt.offers.Count; i++)
                    if (mkt.offers[i] != null && mkt.offers[i].open) openOffers++;
            }
            int pendingCount = InboxManager.PendingSlotSettlements(gs).Count
                               + InboxManager.PendingRightSettlements(gs).Count;
            string marketLabel = openOffers > 0 ? $"时刻二级市场  ●{openOffers}" : "时刻二级市场";
            string heldLabel = pendingCount > 0 ? $"持仓与在办  ●{pendingCount}" : "持仓与在办";

            var bar = UIFactory.Panel(_content, "RightsTabs",
                new UIFactory.Anchor(new Vector2(0f, 0.925f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.10f, 0.18f, 0.55f));
            var barImg = bar.GetComponent<Image>();
            if (barImg != null) barImg.raycastTarget = false;

            // 两个二级标签各占一条，选中态用强调色 —— 与模块页签的配色规则一致。
            // 三个区域从左到右：标签（0–0.40）/ 当前位置说明（0.41–0.72）/ 操作回执 Toast（0.735–0.995，见 Build）。
            UIFactory.Button(bar.transform, "RightsTab0", heldLabel,
                new UIFactory.Anchor(new Vector2(0.0f, 0.12f), new Vector2(0.195f, 0.88f), Vector2.zero, Vector2.zero),
                SubTab == 0 ? UITheme.NavActive : UITheme.NavIdle,
                () => { SubTab = 0; _mkSel = ""; RefreshBody(); }, 13);
            UIFactory.Button(bar.transform, "RightsTab1", marketLabel,
                new UIFactory.Anchor(new Vector2(0.205f, 0.12f), new Vector2(0.400f, 0.88f), Vector2.zero, Vector2.zero),
                SubTab == 1 ? UITheme.NavActive : UITheme.NavIdle,
                () => { SubTab = 1; RefreshBody(); }, 13);

            // 右侧补一句当前位置说明 —— 二级标签把导航藏了一层，玩家需要知道自己在哪。
            UIFactory.ClampSingleLine(UIFactory.Label(bar.transform, "RightsCrumb",
                SubTab == 0 ? "取得时刻槽：协调池（免费·排队）" : "取得时刻槽：二级市场（付费·航司间转让）",
                11, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.410f, 0.12f), new Vector2(0.725f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted));

            var body = UIFactory.Panel(_content, "RightsBody",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0.912f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false);
            var bodyImg = body.GetComponent<Image>();
            if (bodyImg != null) bodyImg.raycastTarget = false;

            _bodyHost = body.transform;
            if (SubTab == 1) RenderSlotMarket(); else RenderRouteRight();
        }

        void RenderRouteRight()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var stg = StrategyManager.EnsureState(gs);
            var permits = RoutePermitLogic.InFlightPermits(gs);
            var pendingSlots = InboxManager.PendingSlotSettlements(gs);
            var pendingRights = InboxManager.PendingRightSettlements(gs);
            var openSlots = OpenMails(gs, "buy_slot", InboxManager.ApplySlotAction, InboxManager.ChangeSlotAction);
            var openRights = OpenMails(gs, "apply_right");
            int slotN = stg.slots != null ? stg.slots.Count : 0;
            int rightN = stg.rights != null ? stg.rights.Count : 0;
            int legacySlotN = LegacySlotHoldings(gs, out string legacySlots);
            int legacyRightN = LegacyRightHoldings(gs, out string legacyRights);
            int slotPend = 0;
            for (int i = 0; i < pendingSlots.Count; i++)
                slotPend += InboxManager.SlotQtyOf(pendingSlots[i]);
            int rightProg = permits.Count + pendingRights.Count + openRights.Count;
            int slotProg = openSlots.Count + pendingSlots.Count;

            // ———— 纵向节奏（2026-11-02 二次重排）————
            // 用户裁定：删掉「先证后槽」说明窗（多此一举），正文区因此整块回收到四段：
            // 状态行 / 持仓双卡 / 在办双卡 / 选择器条。
            // 说明文字删掉后，此前为它预留的 0.13–0.30 全部还给两张卡 —— 卡片直接长高，
            // 列表能多显示两三行，信息密度回到应有水平。选择器条仍独占底部 0–0.075 并带底衬。
            L("Gov", $"政府关系  {stg.governmentRelation:F0} / {StrategyManager.GovernmentRelationCap:F0}"
                + $" · 每季 +{StrategyManager.GovernmentRelationPerQuarter:F0}",
                0.0f, 0.935f, 0.60f, 1.0f, 13, UITheme.Success, true);
            L("Doing", rightProg + slotProg > 0 ? $"在办  {rightProg + slotProg} 项" : "在办  无",
                0.60f, 0.935f, 1.0f, 1.0f, 13,
                rightProg + slotProg > 0 ? UITheme.Warning : UITheme.TextMuted, true, TextAlignmentOptions.MidlineRight);

            HoldingCard("RightHeld", 0f, 0.505f, 0.48f, 0.925f, "已入账航权",
                $"{rightN + legacyRightN} 项", rightN + legacyRightN > 0, HeldRightsText(stg, legacyRights));
            HoldingCard("SlotHeld", 0.52f, 0.505f, 1f, 0.925f, "已入账时刻槽",
                $"{slotN + legacySlotN} 个", slotN + legacySlotN > 0, HeldSlotsText(stg, legacySlots));

            ProgressCard("RightProg", 0f, 0.135f, 0.48f, 0.485f, "航权准入办理进度",
                rightProg > 0 ? $"{rightProg} 项" : "无",
                rightProg > 0,
                list => FillRightProgress(list, permits, pendingRights, openRights));
            ProgressCard("SlotProg", 0.52f, 0.135f, 1f, 0.485f, "时刻槽办理进度",
                slotProg > 0 ? $"{slotPend} 个待交割" + (openSlots.Count > 0 ? $" · {openSlots.Count} 待批复" : "") : "无",
                slotProg > 0,
                list => FillSlotProgress(list, pendingSlots, openSlots));

            // ★2026-11-01（用户裁定）：撤掉「申请航权」「提交航权竞标」两颗按钮。
            // 它们本来就是同一个硬编码 stub（都固定递 PEK—SIN 第五航权，没有真正的竞标对手），
            // 而航权真正的来源是**准入流水线**：在「航线 → 开通新航线」递件后，
            // 许可审查发现缺这一对额度时，运营部会自动把申请送进收件箱
            // （InboxManager.AdvanceRoutePermit → QueueRightDecision）。
            // 本页因此改为纯持仓/在办只读页 —— 与「周计划及航班环」同一套口径：
            // 指令走收件箱批复与命令台，不在业务页手动点。

            // 时刻槽购入：**每一个停站**都要有槽，故先选目标机场 + **具体时刻**再提申请。
            // ★2026-10-15：候选从「机场×时段」改为「机场×时刻」—— 时刻槽精确到时刻，
            // 06:30 与 08:00 是两条槽，选「早班」这种说法在协调人那里根本不存在。
            // ★2026-10-16：候选机场补上**经停站**（它有自己的一对槽，且一趟往返两条）。
            // ★2026-11-02：改成**两层选择**（机场 / 时刻各自一对 ◀▶）。
            // 旧版把「机场 → 时段 → 时刻」平铺成一条长表，一个十来条线的航网就有上百个目标，
            // 找某个机场要连点几十下、还看不出还要点多少下 —— 选目标成了操作负担本身。
            var airports = SlotAirports(gs);
            if (_slotAirportIndex < 0 || _slotAirportIndex >= airports.Count) _slotAirportIndex = 0;
            bool hasAirport = airports.Count > 0;
            string slotAirport = hasAirport ? airports[_slotAirportIndex] : null;

            var clocks = hasAirport ? SlotClocksAt(gs, slotAirport) : new List<string>();
            if (_slotClockIndex < 0 || _slotClockIndex >= clocks.Count) _slotClockIndex = 0;
            bool hasTarget = hasAirport && clocks.Count > 0;
            string slotClock = hasTarget ? clocks[_slotClockIndex] : null;

            string clockText = hasTarget ? slotClock : "无可选时刻";
            string poolText = hasTarget
                ? $"余 {RoutePermitLogic.SlotsAvailable(gs, slotAirport, ScheduleSlots.BandOfDep(slotClock))}"
                  + $" · {StrategyManager.SlotBandName(ScheduleSlots.BandOfDep(slotClock))}班"
                  + (AirportCatalog.IsCoordinated(slotAirport) ? " · 协调机场" : " · 非协调")
                : "池均满或无协调机场";

            // ★2026-11-01（用户裁定）：补上**协调池（免费）**入口。
            // 原先这页只有「购入时刻槽」一条路，而提案正文却写着「不愿付费：改走协调池申请」——
            // CEO 想省钱时无处可点，只能等运营部在缺槽时递单进来。
            // 现实里这是两条**不同**的渠道，不只是折扣：协调池由 WASG 协调人按序分配、不收费；
            // 二级市场向持槽航司买、按价扣款（插队）。故并排给两颗，共用同一组选中目标。
            // 选择器独占底部一条（0–0.115），条内再留 ~0.014 的上下内边距 ——
            // 旧版控件直接贴 0–0.095，且没有任何背景，和上面的说明文字在视觉上糊成一块。
            var slotBar = UIFactory.Panel(Host, "SlotBar",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0.115f), Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.10f, 0.18f, 0.55f));
            var slotBarImg = slotBar.GetComponent<Image>();
            if (slotBarImg != null) slotBarImg.raycastTarget = false;

            B("SlotFree", "协调池申请", 0.010f, 0.014f, 0.170f, 0.101f, UITheme.Accent,
                () =>
                {
                    if (!hasTarget) { Toast("当前没有可申请的时刻槽目标（池均满或无协调机场）。"); return; }
                    InboxManager.QueueApplySlots(gs, slotAirport, slotClock, 1, null, out string msg);
                    Toast(msg);
                }, 12);

            // 第一层：机场（◀ ▶ 只在机场间跳一个机场，不再穿过几十个时刻）
            B("SlotAptPrev", "◀", 0.185f, 0.014f, 0.225f, 0.101f, UITheme.PanelDeep,
                () => { if (airports.Count > 0) { _slotAirportIndex = (_slotAirportIndex - 1 + airports.Count) % airports.Count; _slotClockIndex = 0; RefreshBody(); } }, 12);
            UIFactory.ClampSingleLine(L("SlotAirport", hasAirport ? slotAirport : "—",
                0.228f, 0.014f, 0.352f, 0.101f, 13,
                hasAirport ? UITheme.TextPrimary : UITheme.TextMuted, true, TextAlignmentOptions.Center));
            B("SlotAptNext", "▶", 0.355f, 0.014f, 0.395f, 0.101f, UITheme.PanelDeep,
                () => { if (airports.Count > 0) { _slotAirportIndex = (_slotAirportIndex + 1) % airports.Count; _slotClockIndex = 0; RefreshBody(); } }, 12);

            // 第二层：该机场的具体时刻
            B("SlotPrev", "◀", 0.412f, 0.014f, 0.452f, 0.101f, UITheme.PanelDeep,
                () => { if (clocks.Count > 0) { _slotClockIndex = (_slotClockIndex - 1 + clocks.Count) % clocks.Count; RefreshBody(); } }, 12);
            UIFactory.ClampSingleLine(L("SlotTarget", clockText, 0.455f, 0.014f, 0.579f, 0.101f, 13,
                hasTarget ? UITheme.TextPrimary : UITheme.TextMuted, true, TextAlignmentOptions.Center));
            B("SlotNext", "▶", 0.582f, 0.014f, 0.622f, 0.101f, UITheme.PanelDeep,
                () => { if (clocks.Count > 0) { _slotClockIndex = (_slotClockIndex + 1) % clocks.Count; RefreshBody(); } }, 12);

            // 池况：单独一句、右对齐、强制单行 —— 长了就省略，绝不折行压到别的控件上。
            UIFactory.ClampSingleLine(L("SlotPool", poolText, 0.640f, 0.014f, 0.995f, 0.101f, 11,
                hasTarget ? UITheme.TextMuted : UITheme.Danger, false, TextAlignmentOptions.MidlineRight));
        }

        // ================= n77 · 二级标签「时刻二级市场」 =================
        /// <summary>
        /// 时刻二级市场（2026-11-02）。
        ///
        /// 左「他航待售」、右「我司待售」并排。两侧操作都是「点一行 → 点按钮」——
        /// 它们本来就是同一件事的两个方向（买 / 卖），结构对称，玩家不必学两套动作。
        ///
        /// ★2026-11-02 用户裁决：右侧标题由「我司时刻槽」改为「我司待售」；
        /// 同排第四颗「去协调池申请（免费）」按钮删除 —— 取得时刻槽的入口留在
        /// 「持仓与在办」页签里，本页只做买卖，不再挂指路按钮。
        ///
        /// 与同页「持仓与在办」的分工必须说清：协调池申请**免费、排队、受池容量硬约束**；
        /// 本标签花钱插队，**不受池容量约束**（航司间转让，机场总量不变）。
        /// 两者是同一件事的两条渠道，故收在同一个二级标签条里，而不是模块级两个页签。
        /// </summary>
        void RenderSlotMarket()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var stg = StrategyManager.EnsureState(gs);
            var mkt = SlotMarketLogic.Ensure(gs);
            if (stg == null || mkt == null) return;

            // 选中项可能已被买走 / 撤回 / 成交换了列表，先失效，避免「点了按钮却提示已下架」。
            if (!SelectionAlive(mkt, stg)) _mkSel = "";

            int openOffers = 0;
            for (int i = 0; i < mkt.offers.Count; i++)
                if (mkt.offers[i] != null && mkt.offers[i].open) openOffers++;

            // ———— 顶部摘要（不再重复标题：二级标签已写着「时刻二级市场」）————
            // ★2026-11-02：现金读数放大并置顶 —— 买槽是否扣钱，一眼就能对上。
            // 右侧三个计数保持小字号：它们是信息，不是账目。
            L("Stat", $"现金 {gs.cash:F0} 万", 0f, 0.930f, 0.22f, 1f, 16, UITheme.Success, true);
            L("Counts", $"他航挂牌 {mkt.listings.Count} 条 · 我司挂牌 {mkt.mine.Count} 条 · 待答复意向 {openOffers}",
                0.23f, 0.935f, 0.72f, 1f, 12, UITheme.TextPrimary);
            L("Cash", openOffers > 0 ? $"有 {openOffers} 条购买意向待处理 → 收件箱" : "他航有意向会以邮件送达",
                0.72f, 0.935f, 1f, 1f, 12, openOffers > 0 ? UITheme.Warning : UITheme.TextMuted,
                openOffers > 0, TextAlignmentOptions.MidlineRight);

            L("ColL", "他航待售（点一行选中 → 购买时刻槽）", 0f, 0.872f, 0.47f, 0.925f, 12, UITheme.TextPrimary, true);
            L("ColR", "我司待售（点一行选中 → 挂牌 / 撤回）", 0.52f, 0.872f, 1f, 0.925f, 12, UITheme.TextPrimary, true);

            // 说明条删除后，两侧货架从 0.315–0.862 长到 0.200–0.862 —— 多出约两行。
            var left = UIFactory.VerticalScrollArea(Host, "MkShelf",
                new UIFactory.Anchor(new Vector2(0f, 0.200f), new Vector2(0.47f, 0.862f), Vector2.zero, Vector2.zero));
            FillShelf(left, mkt);
            var right = UIFactory.VerticalScrollArea(Host, "MkMine",
                new UIFactory.Anchor(new Vector2(0.52f, 0.200f), new Vector2(1f, 0.862f), Vector2.zero, Vector2.zero));
            FillOwnSlots(right, mkt, stg);

            bool isMkt = _mkSel.StartsWith("mkt:");
            bool isOwn = _mkSel.StartsWith("own:");
            bool isMine = _mkSel.StartsWith("mine:");

            // 选中项的叫价 / 建议价直接写进按钮：玩家在点之前就能看到「要花多少、花完还剩多少」。
            // ★2026-11-02（用户两次报「买了没扣现金」）：成交确实扣了钱（页顶「现金」与底栏
            // 新增的「现金:」都会跟着变），但扣款前页面上没有任何「这一步要花多少」的预告，
            // 玩家只能靠事后对比余额判断，于是把没动的那三个「季度…」当成现金读。
            SlotMarketListing sel = isMkt ? SlotMarketLogic.Find(gs, _mkSel.Substring(4)) : null;
            float selAsk = sel != null ? sel.askWan : 0f;
            bool canAfford = sel != null && gs.cash >= selAsk;

            string buyLabel = sel == null
                ? "购买时刻槽（先点左侧一行）"
                : canAfford
                    ? $"购买时刻槽（{selAsk:F0} 万 · 买后 {gs.cash - selAsk:F0} 万）"
                    : $"购买时刻槽（{selAsk:F0} 万 · 现金不足）";

            string askLabel = "挂牌时刻槽（先点右侧一行）";
            if (isOwn && int.TryParse(_mkSel.Substring(4), out int ownIdx)
                && stg.slots != null && ownIdx >= 0 && ownIdx < stg.slots.Count && stg.slots[ownIdx] != null)
            {
                var oh = stg.slots[ownIdx];
                string ownClock = RoutePermitLogic.SlotRequestClock(oh.slotTime);
                askLabel = oh.inUse
                    ? "挂牌时刻槽（使用中，不可挂牌）"
                    : $"挂牌时刻槽（建议 {SlotMarketLogic.SuggestedAskWan(oh.airport, ownClock):F0} 万）";
            }

            // 动作行：三颗按钮平分一条底衬（原先第四颗「去协调池申请」已按用户要求删除 ——
            // 取得时刻槽的入口在「持仓与在办」页签里，二级市场页不该再挂一条指路按钮）。
            // 旧版把说明文字压在按钮下面（0.020–0.170），溢出即盖住按钮。
            // 现在按钮独占 0.075–0.180，说明整块删掉。
            B("MkBuy", buyLabel, 0.000f, 0.075f, 0.320f, 0.180f,
                sel == null ? UITheme.NavIdle : canAfford ? UITheme.Accent : UITheme.Danger, () =>
                {
                    if (!_mkSel.StartsWith("mkt:")) { Toast("先在左侧「他航待售」里点一行。"); return; }
                    string id = _mkSel.Substring(4);
                    bool ok = SlotMarketLogic.Buy(gs, id, out string msg);
                    if (ok) _mkSel = "";
                    Toast(msg);
                    RefreshBody();
                }, 13);

            B("MkList", askLabel, 0.340f, 0.075f, 0.660f, 0.180f,
                isOwn ? UITheme.Accent : UITheme.NavIdle, () =>
                {
                    if (!_mkSel.StartsWith("own:")) { Toast("先在右侧点一条闲置的时刻槽。"); return; }
                    if (!int.TryParse(_mkSel.Substring(4), out int idx)) return;
                    bool ok = SlotMarketLogic.ListOwn(gs, idx, out string msg);
                    if (ok) _mkSel = "";
                    Toast(msg);
                    RefreshBody();
                }, 13);

            B("MkWithdraw", "撤回挂牌", 0.680f, 0.075f, 1.000f, 0.180f,
                isMine ? UITheme.PanelDeep : UITheme.NavIdle, () =>
                {
                    if (!_mkSel.StartsWith("mine:")) { Toast("先在右侧点一条已挂牌的时刻槽。"); return; }
                    SlotMarketLogic.Withdraw(gs, _mkSel.Substring(5), out string msg);
                    _mkSel = "";
                    Toast(msg);
                    RefreshBody();
                }, 12);
        }

        void FillShelf(RectTransform list, SlotMarketState mkt)
        {
            if (mkt?.listings == null || mkt.listings.Count == 0)
            {
                var empty = UIFactory.Label(list, "Empty",
                    "本季他航没有挂牌。\n货架每季换新；急用可切上方「持仓与在办」走协调池申请（免费，排队）。",
                    12, TextAlignmentOptions.TopLeft, UIFactory.Fill, UITheme.TextMuted);
                UIFactory.PinFromTop(empty.gameObject, 8f, 64f, 0.02f, 0.98f);
                UIFactory.SetScrollContentHeight(list, 84f);
                return;
            }
            float y = 4f;
            for (int i = 0; i < mkt.listings.Count; i++)
            {
                var l = mkt.listings[i];
                if (l == null) continue;
                string line = $"{l.airport} {l.clock}　{l.seller}　叫价 {l.askWan:F0} 万　（市价 {l.fairWan:F0}）";
                string id = l.id;
                bool sel = _mkSel == "mkt:" + id;
                MkRow(list, "MK" + i, y, 24f, line, sel, () => { _mkSel = "mkt:" + id; RefreshBody(); });
                y += 26f;
            }
            UIFactory.SetScrollContentHeight(list, y + 8f);
        }

        void FillOwnSlots(RectTransform list, SlotMarketState mkt, StrategyState stg)
        {
            float y = 4f;
            int shown = 0;
            if (stg?.slots != null)
            {
                for (int i = 0; i < stg.slots.Count; i++)
                {
                    var h = stg.slots[i];
                    if (h == null) continue;
                    string clock = RoutePermitLogic.SlotRequestClock(h.slotTime);
                    var listed = FindMyListing(mkt, h.airport, clock);
                    string key;
                    string line;
                    if (listed != null)
                    {
                        int offers = CountOpenOffers(mkt, listed.id);
                        line = $"{h.airport} {clock}　已挂牌 {listed.askWan:F0} 万"
                               + (offers > 0 ? $"　· {offers} 份意向待批复" : "　· 等待询价");
                        key = "mine:" + listed.id;
                    }
                    else
                    {
                        float ask = SlotMarketLogic.SuggestedAskWan(h.airport, clock);
                        line = $"{h.airport} {clock}　{(h.inUse ? "使用中（不可挂牌）" : "闲置")}"
                               + $"　建议挂牌 {ask:F0} 万";
                        key = "own:" + i;
                    }
                    bool sel = _mkSel == key;
                    MkRow(list, "MY" + i, y, 24f, line, sel, () => { _mkSel = key; RefreshBody(); });
                    y += 26f;
                    shown++;
                }
            }
            if (shown == 0)
            {
                var empty = UIFactory.Label(list, "Empty",
                    "我司还没有时刻槽。\n先去「航权与时刻槽」走协调池申请（免费），或在本页左侧买一条。",
                    12, TextAlignmentOptions.TopLeft, UIFactory.Fill, UITheme.TextMuted);
                UIFactory.PinFromTop(empty.gameObject, 8f, 64f, 0.02f, 0.98f);
                y = 84f;
            }
            UIFactory.SetScrollContentHeight(list, y + 8f);
        }

        static SlotMarketListing FindMyListing(SlotMarketState mkt, string airport, string clock)
        {
            if (mkt?.mine == null) return null;
            for (int i = 0; i < mkt.mine.Count; i++)
            {
                var l = mkt.mine[i];
                if (l == null) continue;
                if (!string.Equals(l.airport, airport, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.ClockDiff(l.clock, clock) > ScheduleSlots.SlotToleranceMinutes) continue;
                return l;
            }
            return null;
        }

        static int CountOpenOffers(SlotMarketState mkt, string listingId)
        {
            if (mkt?.offers == null) return 0;
            int n = 0;
            for (int i = 0; i < mkt.offers.Count; i++)
            {
                var o = mkt.offers[i];
                if (o != null && o.open && o.listingId == listingId) n++;
            }
            return n;
        }

        bool SelectionAlive(SlotMarketState mkt, StrategyState stg)
        {
            if (string.IsNullOrEmpty(_mkSel)) return false;
            if (_mkSel.StartsWith("mkt:"))
            {
                string id = _mkSel.Substring(4);
                for (int i = 0; i < mkt.listings.Count; i++)
                    if (mkt.listings[i] != null && mkt.listings[i].id == id) return true;
                return false;
            }
            if (_mkSel.StartsWith("mine:"))
            {
                string id = _mkSel.Substring(5);
                for (int i = 0; i < mkt.mine.Count; i++)
                    if (mkt.mine[i] != null && mkt.mine[i].id == id) return true;
                return false;
            }
            if (_mkSel.StartsWith("own:"))
            {
                return int.TryParse(_mkSel.Substring(4), out int idx)
                       && stg?.slots != null && idx >= 0 && idx < stg.slots.Count;
            }
            return false;
        }

        /// <summary>可点击的一行（二级市场两侧共用）：整行都是按钮，不必精确点到文字上。</summary>
        static void MkRow(RectTransform host, string name, float y, float h, string text, bool selected,
            UnityEngine.Events.UnityAction onClick)
        {
            var row = UIFactory.Panel(host, name, UIFactory.Fill,
                selected ? UITheme.NavActive : new Color(0.04f, 0.10f, 0.18f, 0.85f));
            UIFactory.PinFromTop(row, y, h, 0.01f, 0.99f);
            var lab = UIFactory.Label(row.transform, "L", text, 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            lab.raycastTarget = false;   // 让点击穿透到行本身
            var img = row.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;
            var btn = row.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
        }

        /// <summary>
        /// 时刻槽目标机场（第一层选择）：本基地 + 已开航线的对端与**经停**机场，
        /// 只保留至少有一个时刻还留着余量的。
        /// ★2026-11-02：从「机场×时刻平铺」拆出这一层 —— 目标是「先找对机场，再挑时刻」，
        /// 而不是在上百个目标里滚。顺序按「基地优先、再按航线序」，与玩家找机场的直觉一致。
        /// </summary>
        static List<string> SlotAirports(GameState gs)
        {
            var list = new List<string>();
            if (gs == null) return list;
            var apts = new List<string>();
            if (!string.IsNullOrEmpty(gs.hubIata)) apts.Add(gs.hubIata);
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    AddAirport(apts, r.origin);
                    AddAirport(apts, r.destination);
                    AddAirport(apts, RoutePermitLogic.StopoverOf(r));
                }
            }
            for (int i = 0; i < apts.Count; i++)
                if (SlotClocksAt(gs, apts[i]).Count > 0) list.Add(apts[i]);
            return list;
        }

        /// <summary>第二层：该机场还有余量的**具体时刻**（池内该时段未满才算）。</summary>
        static List<string> SlotClocksAt(GameState gs, string airport)
        {
            var list = new List<string>();
            if (gs == null || string.IsNullOrEmpty(airport)) return list;
            for (int b = 0; b < ScheduleSlots.SlotBands.Length; b++)
            {
                if (RoutePermitLogic.SlotsAvailable(gs, airport, b) <= 0) continue;
                var grid = ScheduleSlots.SlotGridLabels(b);
                if (grid == null) continue;
                for (int k = 0; k < grid.Count; k++) list.Add(grid[k]);
            }
            return list;
        }

        /// <summary>
        /// 时刻槽目标的两层候选（2026-11-02）。
        ///
        /// ★2026-10-15：由「机场 × 时段（早/中/晚/夜）」改为「机场 × **具体时刻**（HH:mm）」——
        /// 买的就是 06:30 这条槽，而不是笼统的「早班时段」。
        /// ★2026-10-16：补上经停站 —— 经停站的槽同样要买，漏了它就会「经停线永远办不成」。
        /// ★2026-11-01：候选**两条渠道共用**（协调池申请 / 二级市场购入）——
        /// 渠道只决定「花钱还是排队」，不影响「哪一刻有槽」。
        /// ★2026-11-02：由「一张平铺长表」拆成 <see cref="SlotAirports"/>（机场）+
        /// <see cref="SlotClocksAt"/>（时刻）两层。原来的平铺表在十来条线的航网上有上百项，
        /// 想找某个机场要连点几十下，且看不到「还有几个机场」—— 选择器本身成了负担。
        /// 旧的 <c>SlotBuyTargets</c> / <c>SlotBuyTarget</c> 随之删除：留着就是第二份口径，
        /// 迟早会和新页面各说各话。
        /// </summary>
        static void AddAirport(List<string> list, string apt)
        {
            if (string.IsNullOrEmpty(apt)) return;
            if (!RoutePermitLogic.NeedsDepartureSlot(apt)) return;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], apt, System.StringComparison.OrdinalIgnoreCase)) return;
            list.Add(apt.ToUpperInvariant());
        }

        GameObject Card(string name, float x0, float y0, float x1, float y1, Color bg)
        {
            return UIFactory.Panel(Host, name,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                bg);
        }

        void HoldingCard(string name, float x0, float y0, float x1, float y1,
            string title, string countText, bool highlight, string body)
        {
            var go = Card(name, x0, y0, x1, y1, UITheme.PanelDeep);
            UIFactory.Label(go.transform, "T", title, 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.78f), new Vector2(0.62f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            UIFactory.Label(go.transform, "N", countText, 14, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.62f, 0.78f), new Vector2(0.96f, 0.96f), Vector2.zero, Vector2.zero),
                highlight ? UITheme.Success : UITheme.TextMuted, FontStyles.Bold);
            var bodyL = UIFactory.ScrollableLabel(go.transform, "B",
                new UIFactory.Anchor(new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.76f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted, 10, TextAlignmentOptions.TopLeft, showScrollbar: true);
            bodyL.text = body;
        }

        void ProgressCard(string name, float x0, float y0, float x1, float y1,
            string title, string countText, bool highlight, System.Action<RectTransform> fill)
        {
            var go = Card(name, x0, y0, x1, y1, UITheme.PanelDeep);
            UIFactory.Label(go.transform, "T", title, 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.82f), new Vector2(0.58f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            UIFactory.Label(go.transform, "N", countText, 12, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.58f, 0.82f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero),
                highlight ? UITheme.Warning : UITheme.TextMuted, FontStyles.Bold);
            var list = UIFactory.VerticalScrollArea(go.transform, "Scroll",
                new UIFactory.Anchor(new Vector2(0.02f, 0.04f), new Vector2(0.98f, 0.80f), Vector2.zero, Vector2.zero));
            fill?.Invoke(list);
        }

        static List<InboxMail> OpenMails(GameState gs, params string[] actions)
        {
            var list = new List<InboxMail>();
            if (gs?.inbox == null || actions == null) return list;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || !InboxManager.IsOpen(m)) continue;
                for (int a = 0; a < actions.Length; a++)
                {
                    if (string.Equals(m.action, actions[a], System.StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(m);
                        break;
                    }
                }
            }
            return list;
        }

        static string HeldRightsText(StrategyState stg, string legacy = "")
        {
            int n = stg != null && stg.rights != null ? stg.rights.Count : 0;
            var sb = new System.Text.StringBuilder();
            if (n > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    var r = stg.rights[i];
                    if (i > 0) sb.Append('\n');
                    sb.Append(r.origin).Append('—').Append(r.destination)
                        .Append("  第").Append(r.freedom).Append("航权  · ").Append(r.status);
                }
            }
            if (!string.IsNullOrEmpty(legacy))
            {
                if (sb.Length > 0) sb.Append('\n').Append('\n');
                sb.Append(legacy);
            }
            if (sb.Length == 0) sb.Append("暂无入账航权——准入办结并交割后计入此处，可据此开航。");
            return sb.ToString();
        }

        /// <summary>
        /// 祖父条款航权：接管时已在飞、且属国际/地区的航网，无需再走准入。
        /// 航权额度是既成事实，不占配额池、不可转让，单独成组展示以免与「已入账」混淆。
        /// </summary>
        static int LegacyRightHoldings(GameState gs, out string text)
        {
            text = "";
            if (gs?.activeRoutes == null) return 0;
            var seen = new List<string>();
            var seenKeys = new HashSet<string>();
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                if (!RoutePermitLogic.IsInternational(r.origin, r.destination)) continue;
                if (StrategyManager.HasRouteRight(gs, r.origin, r.destination)) continue;
                string key = r.origin.ToUpperInvariant() + "|" + r.destination.ToUpperInvariant();
                if (!seenKeys.Add(key)) continue;
                seen.Add($"{r.origin}—{r.destination}");
            }
            if (seen.Count == 0) return 0;
            var sb = new System.Text.StringBuilder("既有（祖父条款，接管即持有；不占航权额度池）");
            for (int i = 0; i < seen.Count; i++) sb.Append('\n').Append(seen[i]).Append("  双边航权 · 使用中");
            text = sb.ToString();
            return seen.Count;
        }

        static string HeldSlotsText(StrategyState stg, string legacy = "")
        {
            int n = stg != null && stg.slots != null ? stg.slots.Count : 0;
            var sb = new System.Text.StringBuilder();
            if (n > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    var s = stg.slots[i];
                    if (i > 0) sb.Append('\n');
                    // ★2026-10-15：一条一行完整写清「机场 · 具体时刻（时段）· 状态 · 绑定班次」。
                    // 旧版只写「FRA 早」这种时段名，CEO 根本看不出自己买的到底是 06:30 还是 09:00 ——
                    // 而这两条槽在排班上是完全不同的资产（06:30 的槽接不了 07:00 的班）。
                    sb.Append(s.airport).Append("  ").Append(ScheduleSlots.BandNameToClock(s.slotTime))
                      .Append("（").Append(StrategyManager.SlotBandName(ScheduleSlots.BandOfClock(s.slotTime))).Append("班）")
                      .Append("  ").Append(s.inUse ? "使用中" : "闲置");
                    if (s.inUse && !string.IsNullOrEmpty(s.usedByFlightNo))
                        sb.Append(" · ").Append(s.usedByFlightNo);
                    if (s.acquisitionCostWan > 0f)
                        sb.Append(" · 取得价 ").Append(s.acquisitionCostWan.ToString("F0")).Append(" 万");
                }
            }
            if (!string.IsNullOrEmpty(legacy))
            {
                if (sb.Length > 0) sb.Append('\n').Append('\n');
                sb.Append(legacy);
            }
            string usage = SlotUsageText(GameState.Instance);
            if (!string.IsNullOrEmpty(usage))
            {
                if (sb.Length > 0) sb.Append('\n').Append('\n');
                sb.Append(usage);
            }
            if (sb.Length == 0)
                sb.Append("暂无入账时刻槽——购入或经协调池分配并交割后计入此处，可供对应时刻的班次排班。");
            return sb.ToString();
        }

        /// <summary>
        /// 时刻槽使用率考核块（IATA WASG 80/20）：判季、分系列（机场 × 时段 × 星期几）、
        /// HBD 起计。挂「已入账时刻槽」卡内是刻意的 —— 使用率考核的对象就是持仓本身，
        /// 单独开一张卡会让 CEO 以为它是另一类资产。
        /// </summary>
        static string SlotUsageText(GameState gs)
        {
            if (gs == null) return "";
            var series = SlotUsageLogic.Series(gs);
            var lost = SlotUsageLogic.LostSeries(gs);
            var risk = SlotUsageLogic.AtRisk(gs);
            if (series.Count == 0 && lost.Count == 0) return "";

            var sb = new System.Text.StringBuilder("时刻槽使用率考核（IATA 80/20）");
            sb.Append('\n').Append(SlotUsageLogic.FormatSeasonLine(gs));

            if (series.Count > 0)
            {
                sb.Append("\n\n—— 本季系列（机场 × 具体时刻 × 星期）——");
                int shown = 0;
                for (int i = 0; i < series.Count && shown < 12; i++)
                {
                    sb.Append('\n').Append(SlotUsageLogic.FormatSeriesLine(series[i]));
                    shown++;
                }
                if (series.Count > shown) sb.Append($"\n…另有 {series.Count - shown} 个系列");
            }

            if (risk.Count > 0)
            {
                sb.Append("\n\n—— 有掉出风险（WASG 8.6.3 主动提示）——");
                int shown = 0;
                for (int i = 0; i < risk.Count && shown < 6; i++)
                {
                    var x = risk[i];
                    int need = UnityEngine.Mathf.CeilToInt(x.due * SlotUsageLogic.Threshold) - x.operated;
                    sb.Append('\n').Append(SlotUsageLogic.SeriesLabel(x.airport, x.time, x.weekday))
                      .Append($"　{x.operated}/{x.due} = {x.operated / (float)x.due * 100f:F0}%")
                      .Append($"（本季还须再飞 {need} 班才够 80%）");
                    shown++;
                }
            }

            if (lost.Count > 0)
            {
                sb.Append("\n\n—— 已失格（下一等价航季释放持仓，须重新申请/购买）——");
                int shown = 0;
                for (int i = 0; i < lost.Count && shown < 6; i++)
                {
                    var x = lost[i];
                    sb.Append('\n').Append(SlotUsageLogic.SeriesLabel(x.airport, x.time, x.weekday))
                      .Append(x.lastUsage >= 0f ? $"　{x.operated}/{x.due} = {x.lastUsage * 100f:F0}%" : "")
                      .Append("　✗ 历史优先权已终止");
                    shown++;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 祖父条款时刻槽：接管时已在飞的班次所在的协调机场停留——**逐个停站都算**
        /// （本场端 1、对端 1；经停站一趟往返 2）。
        /// 它不是持仓池里的资产（不可买卖、不可挪去开新航线），只保证本航线该时刻继续飞，
        /// 因此单独成组展示，避免 CEO 误把它当成可用于新航线申请的库存。
        /// </summary>
        static int LegacySlotHoldings(GameState gs, out string text)
        {
            text = "";
            if (gs?.activeRoutes == null) return 0;
            var count = new Dictionary<string, int>();
            var order = new List<string>();
            var labelOf = new Dictionary<string, string>();
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                r.EnsureFlights();
                for (int f = 0; f < r.flights.Count; f++)
                {
                    var fl = r.flights[f];
                    if (fl == null) continue;
                    // ★2026-10-16：改按到站时刻表逐站取（经停站两条各自成行），
                    // 不再只写「本场 + 对端」两行。
                    var calls = RoutePermitLogic.StationCalls(r, fl.depTime, fl.arrTime);
                    for (int k = 0; k < calls.Count; k++)
                    {
                        var c = calls[k];
                        if (!RoutePermitLogic.NeedsDepartureSlot(c.airport)) continue;
                        int m = ((c.dep % (24 * 60)) + 24 * 60) % (24 * 60);
                        string key = (c.airport ?? "").ToUpperInvariant() + "|" + ScheduleSlots.FormatClock(m);
                        if (!count.ContainsKey(key))
                        {
                            count[key] = 0; order.Add(key);
                            labelOf[key] = RoutePermitLogic.LegName(c.station, c.inbound);
                        }
                        count[key]++;
                    }
                }
            }
            if (order.Count == 0) return 0;
            var sb = new System.Text.StringBuilder("既有（祖父条款，接管即在飞；不占持仓）");
            for (int i = 0; i < order.Count; i++)
            {
                string[] parts = order[i].Split('|');
                // parts[1] 是该停站的**离场时刻**（HH:mm），不再是时段下标
                sb.Append('\n').Append(parts[0]).Append(' ').Append(parts[1])
                    .Append('（').Append(labelOf[order[i]]).Append("）· ")
                    .Append(count[order[i]]).Append(" 条航线在飞");
            }
            text = sb.ToString();
            return order.Count;
        }

        void FillRightProgress(RectTransform list, List<InboxMail> permits,
            List<InboxMail> pending, List<InboxMail> open)
        {
            float y = 4f;
            int shown = 0;
            if (permits != null)
            {
                for (int i = 0; i < permits.Count; i++)
                {
                    PermitProgressRow(list, permits[i], y, 72f);
                    y += 80f;
                    shown++;
                }
            }
            y = AppendLineBlock(list, y, "待交割", pending, InboxManager.FormatPendingRight, ref shown);
            y = AppendLineBlock(list, y, "待批复", open, m =>
            {
                string o = string.IsNullOrEmpty(m.origin) ? "—" : m.origin;
                string d = string.IsNullOrEmpty(m.dest) ? "—" : m.dest;
                // 档位按这一对重算（本国 ↔ 对方 = 第三/第四；他国 ↔ 他国 = 第五），
                // 别在持仓列表里把所有申请都写成「第五航权」。
                int fd = RoutePermitLogic.FreedomFor(GameState.Instance, o, d);
                if (fd <= 0) fd = 3;
                return $"{o}—{d}  {AirportRegions.FreedomLabel(fd)}　待批复";
            }, ref shown);
            if (shown == 0)
            {
                // ★2026-11-01：撤掉本页两颗「申请航权」按钮后，这里是玩家唯一会问
                // 「那航权去哪办」的地方 —— 空态必须把来源讲清楚，否则就是拆了入口不给指路。
                // ★2026-11-02：文案收成三行 —— 进度卡本季改成 0.29 高，五行的空态在
                // 86px 的视口里会被裁掉一半，「指路」反而看不见。
                var empty = UIFactory.Label(list, "Empty",
                    "暂无在办。航权不在这里点：\n"
                    + "把国际线递进「航线 → 开通新航线」，许可审查缺那一对额度时，运营部会自动把申请送进收件箱。\n"
                    + $"第三/第四航权（本国 ↔ 对方）约 {StrategyManager.RightCost(3):F0} 点政府关系，"
                    + $"第五航权（他国 ↔ 他国）约 {StrategyManager.RightCost(5):F0} 点；每季回升 "
                    + $"{StrategyManager.GovernmentRelationPerQuarter:F0} 点（上限 {StrategyManager.GovernmentRelationCap:F0}）。",
                    11, TextAlignmentOptions.TopLeft, UIFactory.Fill, UITheme.TextMuted);
                UIFactory.PinFromTop(empty.gameObject, 4f, 82f, 0.02f, 0.98f);
                y = 92f;
            }
            UIFactory.SetScrollContentHeight(list, y + 8f);
        }

        void FillSlotProgress(RectTransform list, List<InboxMail> pending, List<InboxMail> open)
        {
            float y = 4f;
            int shown = 0;
            y = AppendLineBlock(list, y, "待批复", open, m =>
            {
                string apt = string.IsNullOrEmpty(m.origin) ? "HUB" : m.origin;
                string clk = InboxManager.SlotMailClock(m);
                return $"{apt} {clk} {InboxManager.SlotQtyOf(m)} 条　待批复";
            }, ref shown);
            y = AppendLineBlock(list, y, "待交割", pending, InboxManager.FormatPendingSlot, ref shown);
            if (shown == 0)
            {
                var empty = UIFactory.Label(list, "Empty",
                    "暂无在办。购入或向协调池申请获批后，待交割会显示在这里。",
                    12, TextAlignmentOptions.TopLeft, UIFactory.Fill, UITheme.TextMuted);
                UIFactory.PinFromTop(empty.gameObject, 8f, 64f, 0.02f, 0.98f);
                y = 80f;
            }
            UIFactory.SetScrollContentHeight(list, y + 8f);
        }

        static float AppendLineBlock(RectTransform list, float y, string head,
            List<InboxMail> mails, System.Func<InboxMail, string> line, ref int shown)
        {
            if (mails == null || mails.Count == 0) return y;
            var h = UIFactory.Label(list, "H" + head + y, head, 11,
                TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextMuted, FontStyles.Bold);
            UIFactory.PinFromTop(h.gameObject, y, 20f, 0.02f, 0.98f);
            y += 22f;
            for (int i = 0; i < mails.Count; i++)
            {
                if (mails[i] == null) continue;
                var lab = UIFactory.Label(list, "L" + shown, line(mails[i]), 12,
                    TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextPrimary);
                UIFactory.PinFromTop(lab.gameObject, y, 22f, 0.03f, 0.98f);
                y += 24f;
                shown++;
            }
            return y;
        }

        static void PermitProgressRow(RectTransform host, InboxMail mail, float y, float h)
        {
            var row = UIFactory.Panel(host, "P" + (mail != null ? mail.id : "x"), UIFactory.Fill,
                new Color(0.04f, 0.10f, 0.18f, 0.85f));
            UIFactory.PinFromTop(row, y, h, 0.01f, 0.99f);

            string line = RoutePermitLogic.ProgressLine(mail,
                AirlineCEO.Systems.GameState.Instance != null
                    ? AirlineCEO.Systems.GameState.Instance.SimUtc
                    : System.DateTime.UtcNow);
            UIFactory.Label(row.transform, "L", line, 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.58f), new Vector2(0.98f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            int node = RoutePermitLogic.DisplayNode(mail);
            bool denied = mail != null && mail.permitStage == RoutePermitLogic.StageDenied;
            Color fill = denied ? UITheme.Danger : UITheme.Accent;
            // ★2026-11-04：九关（原为六关），段宽按关数均分 —— 写死 0.16 步长会让第 7~9 段溢出右边界。
            int steps = RoutePermitLogic.NodeLast;
            float segW = 0.96f / steps;
            for (int s = 1; s <= steps; s++)
            {
                float x0 = 0.02f + (s - 1) * segW;
                float x1 = x0 + segW * 0.92f;
                bool on = s <= node;
                var seg = UIFactory.Panel(row.transform, "S" + s,
                    new UIFactory.Anchor(new Vector2(x0, 0.14f), new Vector2(x1, 0.42f), Vector2.zero, Vector2.zero),
                    on ? fill : UITheme.NavIdle);
                var img = seg.GetComponent<Image>();
                if (img != null) img.raycastTarget = false;
                UIFactory.Label(seg.transform, "N", RoutePermitLogic.NodeShort(s), 10,
                    TextAlignmentOptions.Center,
                    UIFactory.Fill, on ? UITheme.TextPrimary : UITheme.TextMuted);
            }
        }

        // ================= n78 联盟与战略合作 =================
        void RenderAlliance()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var stg = StrategyManager.EnsureState(gs);
            L("H", "联盟与战略合作 · 全球航空联盟", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            string tier = StrategyManager.TierName(stg.alliance.tier);
            L("Tier", $"当前等级：{tier} · 年增量收入约 {stg.alliance.revenueLiftWan:F0} 万", 0.0f, 0.80f, 0.6f, 0.90f, 13, UITheme.Success, true);
            L("Check", "准入检查：机队 ≥20 架 · 安全评级 ≥B · 入盟费准备", 0.0f, 0.70f, 0.6f, 0.80f, 12, UITheme.TextMuted);
            L("Partners", $"代码共享伙伴：{stg.alliance.codeSharePartners} · 联营航线组：{stg.alliance.jvRouteGroups}", 0.0f, 0.60f, 0.6f, 0.70f, 12);

            L("Alliances", "三大联盟对比：\n  星空联盟：网络最广\n  天合联盟：欧洲强\n  寰宇一家：北美强", 0.62f, 0.50f, 1.0f, 0.78f, 12, UITheme.TextMuted);

            B("Upgrade", "入盟申请 / 升级", 0.02f, 0.34f, 0.30f, 0.44f, UITheme.Accent,
                () =>
                {
                    string msg = "升级失败";
                    if (StrategyManager.Instance != null)
                        StrategyManager.Instance.UpgradeAlliance(out msg);
                    Toast(msg); RefreshBody();
                }, 13);
            B("Neg", "代码共享 → 谈判", 0.34f, 0.34f, 0.62f, 0.44f, UITheme.PanelDeep,
                () => StrategyPanelUI.OpenStrategyPage("n102_谈判弹窗"), 12);
            B("Mna", "并购目标评估", 0.66f, 0.34f, 0.98f, 0.44f, UITheme.PanelDeep,
                () =>
                {
                    string msg = "评估失败";
                    if (StrategyManager.Instance != null)
                        StrategyManager.Instance.ProposeInvestment(StrategyManager.CatalogMna(), out msg);
                    Toast(msg);
                }, 12);
        }

        // ================= n79 战略投资与并购 =================
        void RenderInvestment()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var stg = StrategyManager.EnsureState(gs);
            L("H", "战略投资与并购 · 资本配置与产权管理", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Hint", "立项只送收件箱；CEO 批准后下季度扣资本，项目才进入执行。", 0.0f, 0.82f, 1.0f, 0.90f, 12, UITheme.TextMuted);

            float y = 0.70f;
            if (stg.investments.Count == 0)
            {
                L("None", "（暂无投资项目）", 0.0f, 0.70f, 0.5f, 0.80f, 13, UITheme.TextMuted);
            }
            else
            {
                for (int i = 0; i < stg.investments.Count && i < 5; i++)
                {
                    var p = stg.investments[i];
                    float score = StrategyEconomics.InvestmentScore(p.businessValue, p.resourceFit, p.barrier, p.strategicFit);
                    float irr = StrategyEconomics.InvestmentIrr(p.npvWan, p.capitalWan < 1f ? 280f : p.capitalWan, 5);
                    string flag = p.approved ? "已投入" : p.stage;
                    L("I" + i, $"{p.name} · {flag} · 综合 {score:F0} 分 · 资本 {p.capitalWan:F0} 万 · NPV {p.npvWan:F0} 万 · IRR {irr:F0}%",
                        0.0f, y, 1.0f, y + 0.07f, 13);
                    y -= 0.09f;
                }
            }

            B("Add", "立项「第五航权枢纽」", 0.02f, 0.30f, 0.34f, 0.40f, UITheme.Accent,
                () =>
                {
                    string msg = "立项失败";
                    if (StrategyManager.Instance != null)
                        StrategyManager.Instance.ProposeInvestment(StrategyManager.CatalogHub(), out msg);
                    Toast(msg); RefreshBody();
                }, 12);
            B("Feasibility", "可行性分析 → 财务联审", 0.38f, 0.30f, 0.70f, 0.40f, UITheme.PanelDeep,
                () => FinancePanelUI.OpenFinancePage("n22_年度预算编制_审批"), 12);
            B("FiveYear", "五年规划 → 董事会", 0.74f, 0.30f, 0.98f, 0.40f, UITheme.PanelDeep,
                () => { Toast("五年战略规划已提交董事会审批。"); }, 12);
        }

        // ================= n80 战略研究与预案 =================
        void RenderResearch()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var stg = StrategyManager.EnsureState(gs);
            L("H", "战略研究与预案 · 情报分析与风险管理", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);

            float c = stg.strategicConsistency;
            L("Consistency", $"战略一致性评分：{c:F0}（{(c > 80 ? "绿色·效率加成+10%" : c >= 60 ? "黄色" : "红色·董事会质疑")}）",
                0.0f, 0.78f, 1.0f, 0.88f, 13, c > 80 ? UITheme.Success : c >= 60 ? UITheme.Warning : UITheme.Danger, true);
            L("Geo", $"地缘政治风险指数：{stg.geoRiskIndex} · 航路费加成 +{StrategyEconomics.GeoRiskSurcharge(stg.geoRiskIndex) * 100:F0}%",
                0.0f, 0.68f, 1.0f, 0.78f, 12);
            L("Waiver", $"战略豁免申请：本财年已用 {stg.waiverUsed}/{ProgressionGate.WaiverLimit(gs.year)} 项", 0.0f, 0.58f, 1.0f, 0.68f, 12);

            var sb = new System.Text.StringBuilder("黑天鹅应急预案：\n");
            if (stg.contingencyPlans.Count == 0) sb.Append("  （暂无）");
            foreach (var p in stg.contingencyPlans) sb.Append($"  {p.trigger} → {p.autoMeasure}（损失降低 {p.lossReduction * 100:F0}%）\n");
            L("Plans", sb.ToString().TrimEnd('\n'), 0.0f, 0.30f, 1.0f, 0.54f, 12);

            B("Preset", "预设黑天鹅预案", 0.02f, 0.18f, 0.32f, 0.27f, UITheme.Accent,
                () =>
                {
                    string msg = "预设失败";
                    if (StrategyManager.Instance != null)
                        StrategyManager.Instance.SaveContingency(new ContingencyPlan
                        { trigger = "油价暴涨 >150", autoMeasure = "锁定燃油套保+削减高油耗航班", lossReduction = 0.35f }, out msg);
                    Toast(msg); RefreshBody();
                }, 12);
            B("WaiverBtn", "申请战略豁免", 0.36f, 0.18f, 0.64f, 0.27f, UITheme.PanelDeep,
                () =>
                {
                    string msg = "申请失败";
                    if (StrategyManager.Instance != null)
                        StrategyManager.Instance.ApplyWaiver(out msg);
                    Toast(msg); RefreshBody();
                }, 12);
            B("Report", "战略季报 → 董事会", 0.68f, 0.18f, 0.98f, 0.27f, UITheme.PanelDeep,
                () => { Toast("战略季报已生成并提交董事会。"); }, 12);
        }

        // ================= n35 RPM 评估台 =================
        // 与「航线 → 开通新航线」共用同一个 RPMCalculator 和同一套取数口径：
        // 距离＝大圆距离，需求＝城市经济乘数，品牌＝品牌净值，油价＝当前油价，机型＝按航程自动（或手动指定）。
        // 这样页面上的数字能和开线预览对上，不再是「班次 × 品牌」拼出来的假分。
        static readonly string[] RpmAircraft = { "自动", "A320neo", "A321", "B737-800", "B787-9", "A330-300" };
        string _rpmOrigin = "PEK";
        string _rpmDest = "LHR";
        int _rpmAcIndex;                     // 0 = 按航程自动
        float _rpmCompetitor = 0.4f;         // 竞对运力占比
        RPMCalculator.RPMOutput _rpmOut;     // RefreshBody 会重建内容层，结果得缓存
        bool _rpmHasResult;
        float _rpmDist;
        float _rpmDemandMul;
        string _rpmAircraftUsed;
        int _rpmSeats;
        string _rpmError;
        TMP_InputField _rpmOriginInput;
        TMP_InputField _rpmDestInput;

        void RenderRpm()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            if (!_rpmHasResult) EvaluateRpm(gs, toastOnError: false);

            // 从「航线 → 开通新航线」跳进来时，出发地 / 目的地已由开线页填好，直接沿用。
            if (RpmPreview.TryConsumeSeed(out string seedOrigin, out string seedDest))
            {
                _rpmOrigin = seedOrigin;
                _rpmDest = seedDest;
                _rpmOriginInput = null;
                _rpmDestInput = null;
                EvaluateRpm(gs, toastOnError: false);
            }

            float score = _rpmOut.score;
            Color band = score >= 70 ? UITheme.Success : score >= 40 ? UITheme.Warning : UITheme.Danger;

            L("H", "新航线 RPM 评估台 · 航线盈利预测模型", 0.0f, 0.94f, 1.0f, 1.0f, 15, UITheme.Accent, true);

            // —— 航线输入 ——
            L("OL", "出发", 0.0f, 0.85f, 0.045f, 0.915f, 12, UITheme.TextMuted);
            _rpmOriginInput = UIFactory.Input(_content, "RpmOrigin", _rpmOrigin,
                new UIFactory.Anchor(new Vector2(0.05f, 0.85f), new Vector2(0.18f, 0.915f), Vector2.zero, Vector2.zero));
            L("DL", "到达", 0.19f, 0.85f, 0.235f, 0.915f, 12, UITheme.TextMuted);
            _rpmDestInput = UIFactory.Input(_content, "RpmDest", _rpmDest,
                new UIFactory.Anchor(new Vector2(0.24f, 0.85f), new Vector2(0.37f, 0.915f), Vector2.zero, Vector2.zero));

            B("Swap", "⇄ 对调", 0.38f, 0.85f, 0.445f, 0.915f, UITheme.PanelDeep,
                () =>
                {
                    CaptureRpmInputs();
                    string t = _rpmOrigin; _rpmOrigin = _rpmDest; _rpmDest = t;
                    _rpmOriginInput = null; _rpmDestInput = null;
                    _rpmHasResult = false;
                    RefreshBody();
                }, 12);
            B("Calc", "重新评估", 0.455f, 0.85f, 0.555f, 0.915f, UITheme.Accent,
                () => { CaptureRpmInputs(); EvaluateRpm(gs, toastOnError: true); RefreshBody(); }, 12);
            B("GoOpen", "前往开通新航线 →", 0.565f, 0.85f, 0.735f, 0.915f, UITheme.PanelDeep,
                () =>
                {
                    BackOne();
                    OpsPanelUI.OpenShellTab("n12_航线网络");
                    FindFirstObjectByType<NetworkPanelUI>()?.ShowOpenRouteTab();
                }, 12);
            L("Tip", "支持三字码或城市名", 0.745f, 0.85f, 1.0f, 0.915f, 11, UITheme.TextMuted);

            // —— 参数（点一下就重算）——
            string acLabel = string.IsNullOrEmpty(_rpmAircraftUsed) ? "—" : _rpmAircraftUsed;
            if (_rpmAcIndex == 0 && !string.IsNullOrEmpty(_rpmAircraftUsed)) acLabel += "（自动）";
            L("AcVal", $"机型　{acLabel}", 0.0f, 0.775f, 0.33f, 0.835f, 12, UITheme.TextPrimary, true);
            L("FreqVal", $"周班　{_rpmFreq:F0} 班/周", 0.34f, 0.775f, 0.56f, 0.835f, 12, UITheme.TextPrimary, true);
            L("CompVal", $"竞对运力　{_rpmCompetitor * 100f:F0}%", 0.57f, 0.775f, 0.78f, 0.835f, 12, UITheme.TextPrimary, true);
            L("DemVal", $"需求系数　{_rpmDemandMul:F2}", 0.79f, 0.775f, 1.0f, 0.835f, 12, UITheme.TextPrimary, true);

            B("Ac-", "机型 ◀", 0.0f, 0.695f, 0.105f, 0.765f, UITheme.PanelDeep,
                () => { CaptureRpmInputs(); CycleRpmAircraft(-1); }, 11);
            B("Ac+", "机型 ▶", 0.11f, 0.695f, 0.215f, 0.765f, UITheme.PanelDeep,
                () => { CaptureRpmInputs(); CycleRpmAircraft(+1); }, 11);
            B("Freq-", "周班 −", 0.23f, 0.695f, 0.325f, 0.765f, UITheme.PanelDeep,
                () => { CaptureRpmInputs(); _rpmFreq = Mathf.Max(1f, _rpmFreq - 1f); EvaluateRpm(gs, false); RefreshBody(); }, 11);
            B("Freq+", "周班 +", 0.33f, 0.695f, 0.425f, 0.765f, UITheme.PanelDeep,
                () => { CaptureRpmInputs(); _rpmFreq = Mathf.Min(21f, _rpmFreq + 1f); EvaluateRpm(gs, false); RefreshBody(); }, 11);
            B("Comp-", "竞对 −", 0.44f, 0.695f, 0.535f, 0.765f, UITheme.PanelDeep,
                () => { CaptureRpmInputs(); _rpmCompetitor = Mathf.Max(0f, _rpmCompetitor - 0.05f); EvaluateRpm(gs, false); RefreshBody(); }, 11);
            B("Comp+", "竞对 +", 0.54f, 0.695f, 0.635f, 0.765f, UITheme.PanelDeep,
                () => { CaptureRpmInputs(); _rpmCompetitor = Mathf.Min(1f, _rpmCompetitor + 0.05f); EvaluateRpm(gs, false); RefreshBody(); }, 11);
            B("Reset", "恢复默认", 0.65f, 0.695f, 0.76f, 0.765f, UITheme.PanelDeep,
                () =>
                {
                    _rpmOrigin = "PEK"; _rpmDest = "LHR"; _rpmAcIndex = 0; _rpmCompetitor = 0.4f; _rpmFreq = 7f;
                    _rpmOriginInput = null; _rpmDestInput = null;
                    EvaluateRpm(gs, false); RefreshBody();
                }, 11);

            // —— 结果 ——
            L("Od", _rpmError == null
                    ? $"{AirportCatalog.Od(_rpmOrigin, _rpmDest)}　·　{_rpmDist:F0} km　·　单班（单向）口径"
                    : "航线未识别",
                0.0f, 0.615f, 1.0f, 0.685f, 13, UITheme.TextPrimary, true);

            L("Score", _rpmError == null ? $"综合评分 {score:F0}（{_rpmOut.rating} · {RpmRatingWord(score)}）" : "—",
                0.0f, 0.545f, 0.55f, 0.615f, 15, band, true);
            L("Profit", _rpmError == null ? $"单班日利润 {_rpmOut.dailyProfit:F2} 万美元" : "—",
                0.55f, 0.545f, 1.0f, 0.615f, 15,
                _rpmOut.dailyProfit >= 0f ? UITheme.Success : UITheme.Danger, true);

            var box = UIFactory.Panel(_content, "RpmBox",
                new UIFactory.Anchor(new Vector2(0.0f, 0.02f), new Vector2(1.0f, 0.53f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            var body = UIFactory.Label(box.transform, "RpmBody", _rpmError ?? BuildRpmReport(), 13,
                TextAlignmentOptions.TopLeft, UIFactory.Fill, _rpmError != null ? UITheme.Warning : UITheme.TextPrimary);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.rectTransform.offsetMin = new Vector2(14f, 10f);
            body.rectTransform.offsetMax = new Vector2(-14f, -10f);
        }

        /// <summary>把输入框里的文字存回字段：RefreshBody 会连同输入框一起销毁。</summary>
        void CaptureRpmInputs()
        {
            if (_rpmOriginInput != null && !string.IsNullOrWhiteSpace(_rpmOriginInput.text))
                _rpmOrigin = _rpmOriginInput.text.Trim();
            if (_rpmDestInput != null && !string.IsNullOrWhiteSpace(_rpmDestInput.text))
                _rpmDest = _rpmDestInput.text.Trim();
        }

        void CycleRpmAircraft(int step)
        {
            int n = RpmAircraft.Length;
            _rpmAcIndex = ((_rpmAcIndex + step) % n + n) % n;
            EvaluateRpm(GameState.Instance, false);
            RefreshBody();
        }

        void EvaluateRpm(GameState gs, bool toastOnError)
        {
            _rpmError = null;

            if (!AirportCatalog.TryResolve(_rpmOrigin, out var o) || !AirportCatalog.TryResolve(_rpmDest, out var d))
            {
                _rpmHasResult = false;
                _rpmError = "机场未识别：请输入三字码（PEK / LHR）或城市名（北京 / 伦敦）。";
                if (toastOnError) Toast(_rpmError);
                return;
            }
            if (string.Equals(o.code, d.code, System.StringComparison.OrdinalIgnoreCase))
            {
                _rpmHasResult = false;
                _rpmError = "出发与到达是同一个机场，请修改其中一端。";
                if (toastOnError) Toast(_rpmError);
                return;
            }

            _rpmOrigin = o.code;
            _rpmDest = d.code;

            var plan = RpmPreview.Build(gs, o.code, d.code,
                _rpmAcIndex <= 0 ? null : RpmAircraft[_rpmAcIndex],
                _rpmCompetitor);

            _rpmOut = plan.output;
            _rpmDist = plan.distanceKm;
            _rpmAircraftUsed = plan.aircraftType;
            _rpmSeats = plan.seats;
            _rpmDemandMul = plan.demandMultiplier;
            _rpmHasResult = true;
            if (toastOnError) Toast("已按当前油价 / 品牌 / 竞对重算。");
        }

        static string RpmRatingWord(float score)
        {
            if (score >= 70f) return "绿色 · 可开线";
            if (score >= 40f) return "黄色 · 待观察";
            return "红色 · 不建议";
        }

        string BuildRpmReport()
        {
            var gs = GameState.Instance;
            float weeklyRevenue = RpmPreview.WeeklyFromDaily(_rpmOut.dailyRevenue, _rpmFreq);
            float weeklyProfit = RpmPreview.WeeklyFromDaily(_rpmOut.dailyProfit, _rpmFreq);
            float margin = _rpmOut.dailyRevenue > 0.01f ? _rpmOut.dailyProfit / _rpmOut.dailyRevenue : 0f;
            float fuel = gs != null ? gs.fuelPricePerTon : 850f;
            float brand = gs != null ? gs.brandAwareness : 55f;

            var sb = new System.Text.StringBuilder();
            sb.Append("单班口径（1 班/日）\n");
            sb.Append($"　日收入 {_rpmOut.dailyRevenue:F2}　日成本 {_rpmOut.dailyCost:F2}　日利润 {_rpmOut.dailyProfit:F2} 万美元　毛利率 {margin * 100f:F1}%\n");
            sb.Append($"　成本拆分　油费 {_rpmOut.fuelCostWan:F2}　机场费 {_rpmOut.airportCostWan:F2}　机组 {_rpmOut.crewCostWan:F2}　可变维修 {_rpmOut.maintVarWan:F2}\n");
            sb.Append("\n按周班折算\n");
            sb.Append($"　{_rpmFreq:F0} 班/周 → 周收入 {weeklyRevenue:F1}　周利润 {weeklyProfit:F1} 万美元\n");
            sb.Append("\n收益口径\n");
            sb.Append($"　{_rpmAircraftUsed} · {_rpmSeats} 座 · 客座率 {EconomyBalanceParams.DefaultLoadFactor * 100f:F0}% · 商务舱 {EconomyBalanceParams.DefaultBusinessSeatShare * 100f:F0}%\n");
            sb.Append($"　航程 {_rpmDist:F0} km　需求系数 {_rpmDemandMul:F2}　竞对运力 {_rpmCompetitor * 100f:F0}%　品牌净值 {brand:F0}　油价 {fuel:F0} 美元/吨\n");
            sb.Append("\n评分口径\n");
            sb.Append("　利润分 50% ＋ 毛利率分 30% ＋ 航程加分 − 竞对扣分，满分 100；≥70 绿 / ≥40 黄 / 其余红。\n");
            sb.Append("　口径与「航线 → 开通新航线」的预览一致；实际开航还要过航权、时刻槽与现金门槛。");
            return sb.ToString();
        }

        // ================= n102 谈判弹窗 =================
        void RenderNegotiate()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            L("H", "谈判弹窗 · 代码共享 / 联营条款", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);
            L("Partner", $"对方航司：{StrategyContext.NegotiatePartner} · 议题：代码共享 · 合作基础评分：航线互补度 78%",
                0.0f, 0.80f, 1.0f, 0.90f, 13);

            float split = StrategyContext.NegotiateSplit;
            L("Split", $"分成比例：我方 {split * 100:F0}% vs 对方 {(1 - split) * 100:F0}%", 0.0f, 0.68f, 0.6f, 0.78f, 13, UITheme.Success, true);
            L("Scope", "合作范围：亚洲航线组 · 协议期限：3年 · 排他条款：否 · 品牌联名：是", 0.0f, 0.58f, 1.0f, 0.68f, 12, UITheme.TextMuted);

            B("Split-", "我方 −5%", 0.02f, 0.44f, 0.24f, 0.54f, UITheme.PanelDeep,
                () => { StrategyContext.NegotiateSplit = Mathf.Max(0.3f, StrategyContext.NegotiateSplit - 0.05f); RefreshBody(); }, 12);
            B("Split+", "我方 +5%", 0.28f, 0.44f, 0.50f, 0.54f, UITheme.PanelDeep,
                () => { StrategyContext.NegotiateSplit = Mathf.Min(0.7f, StrategyContext.NegotiateSplit + 0.05f); RefreshBody(); }, 12);

            L("Estimate", "预期增量收入（年）：约 260 万 · 客座率提升 +3% · 联盟等级贡献 +1 级", 0.0f, 0.32f, 1.0f, 0.42f, 12, UITheme.TextMuted);

            B("Sign", "确认协议条款（签署）", 0.02f, 0.18f, 0.32f, 0.27f, UITheme.Accent,
                () =>
                {
                    var stg = StrategyManager.EnsureState(gs);
                    stg.alliance.codeSharePartners++;
                    stg.alliance.revenueLiftWan = StrategyEconomics.AllianceRevenueLiftWan(
                        stg.alliance.tier, gs.activeRoutes != null ? gs.activeRoutes.Count : 0, stg.alliance.codeSharePartners);
                    gs.NotifyChanged();
                    Toast($"已与 {StrategyContext.NegotiatePartner} 签署代码共享（伙伴 +1）。");
                }, 12);
            B("Approve", "提交 CEO 审批", 0.36f, 0.18f, 0.64f, 0.27f, UITheme.PanelDeep,
                () => { Toast("重大合作已提交 CEO 决策会议。"); }, 12);
            B("Abort", "终止谈判", 0.68f, 0.18f, 0.98f, 0.27f, UITheme.PanelDeep,
                () => BackOne(), 12);
        }

        // ================= n108 临空商业（非航收入） =================
        void RenderCommerce()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            var state = AirportCommerceManager.EnsureState(gs);
            AirportCommerceManager.Refresh(gs);

            L("H", "临空商业 · 非航收入生态（枢纽空间规划）", 0.0f, 0.92f, 1.0f, 0.98f, 15, UITheme.Accent, true);

            float throughput = state.hubAnnualThroughputWan;
            int tier = state.unlockedTier;
            string tierLabel = AirportCommerceEconomics.TierName(tier);
            float nextThreshold = AirportCommerceEconomics.NextTierThresholdWan(tier);
            string nextInfo = nextThreshold > 0f
                ? $"下一档需 {nextThreshold:F0} 万人次（当前 {throughput:F0} 万，差 {Mathf.Max(0f, nextThreshold - throughput):F0} 万）"
                : "已达最高档";
            L("Kpi", $"枢纽年吞吐：{throughput:F0} 万人次 · 解锁档位：{tierLabel}", 0.0f, 0.82f, 0.7f, 0.90f, 13, UITheme.Success, true);
            L("Next", nextInfo, 0.72f, 0.82f, 1.0f, 0.90f, 11, UITheme.TextMuted);

            // 四类设施行
            float[] ys = { 0.66f, 0.53f, 0.40f, 0.27f };
            for (int kind = 0; kind < 4; kind++)
            {
                var f = AirportCommerceManager.GetFacility(state, kind);
                int level = f != null ? f.level : 0;
                float invested = f != null ? f.investedWan : 0f;
                float annual = AirportCommerceEconomics.FacilityAnnualRevenueWan(kind, level, throughput);
                float payback = AirportCommerceEconomics.PaybackYears(kind, level, throughput);
                float weight = AirportCommerceEconomics.RevenueWeight(kind) * 100f;

                string line = level <= 0
                    ? $"· {AirportCommerceEconomics.FacilityName(kind)}（占 {weight:F0}%）—— 未建"
                    : $"· {AirportCommerceEconomics.FacilityName(kind)}（占 {weight:F0}%）—— L{level} · 投资 {invested:F0} 万 · 年收入 {annual:F0} 万 · 回收 {payback:F1} 年";
                L("F" + kind, line, 0.0f, ys[kind], 0.62f, ys[kind] + 0.10f, 12, level <= 0 ? UITheme.TextMuted : UITheme.TextPrimary);

                if (level < 3)
                {
                    int target = level + 1;
                    bool reachable = target <= tier;
                    float cost = AirportCommerceEconomics.InvestmentWan(kind, target) - invested;
                    if (cost <= 0f) cost = AirportCommerceEconomics.InvestmentWan(kind, target);
                    string btnLabel = reachable ? $"升至 L{target}（{cost:F0} 万）" : $"🔒 L{target}（未解锁）";
                    B("Up" + kind, btnLabel, 0.64f, ys[kind] + 0.015f, 0.98f, ys[kind] + 0.095f,
                        reachable ? UITheme.Accent : UITheme.PanelDeep,
                        () =>
                        {
                            string msg = "投资失败";
                            if (AirportCommerceManager.Instance != null)
                                AirportCommerceManager.Instance.InvestFacility(kind, target, out msg);
                            else
                                AirportCommerceManager.InvestFacility(gs, kind, target, out msg);
                            Toast(msg); RefreshBody();
                        }, 11);
                }
                else
                {
                    L("Max" + kind, "已达最高档", 0.64f, ys[kind] + 0.015f, 0.98f, ys[kind] + 0.095f, 11, UITheme.Success);
                }
            }

            float totalAnnual = 0f;
            for (int kind = 0; kind < 4; kind++)
            {
                var f = AirportCommerceManager.GetFacility(state, kind);
                totalAnnual += AirportCommerceEconomics.FacilityAnnualRevenueWan(kind, f != null ? f.level : 0, throughput);
            }
            L("Sum", $"非航收入合计：年化 {totalAnnual:F0} 万 · 累计收入 {state.cumulativeRevenueWan:F0} 万 · 累计投资 {state.cumulativeInvestmentWan:F0} 万",
                0.0f, 0.16f, 1.0f, 0.24f, 12, UITheme.Success, true);
            L("Hint", "非航收入不随航空景气波动，是衰退/萧条期的「安全垫」。由战略部与市场部共同规划，回报周期较长（回收 8~16 年）。",
                0.0f, 0.06f, 1.0f, 0.15f, 11, UITheme.TextMuted);
        }
    }
}
