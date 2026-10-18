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
    /// <summary>
    /// n112 职业介绍所：候选人名单（只读）+ 招聘发起，**同一页完成**。
    ///
    /// CEO 裁定 2026-10-08：原 n99「招聘策略弹窗」的全部功能并入本页，不再跳转；
    /// 并以「过滤器」形态呈现（参照 Football Manager 球探搜索的「过滤」下拉面板）——
    /// 分组标题 + 复选芯片 + 计数 + 底部动作。列表在左、过滤器覆盖在右。
    ///
    /// CEO 裁定 2026-10-10（三条）：
    /// ① 名单右侧那一列动作（聘请 / 录用 / 退回 / 调岗）整体搬进人员页（`n114`），本页变只读；
    /// ② 取消「校招 / 猎头挖角 / 海外引进 / 内部转岗」四个独立视图，改为顶栏一排「来源」芯片
    ///   （「全部」= 原五视图并集），它同时是「呈请招聘」的渠道选择器；
    /// ③ 非在职候选人的人员页不再显示继任计划 / 部门职位 / 合同 / 培训。
    ///
    /// CEO 裁定 2026-10-11（两条）：
    /// ① 顶栏那排「来源」芯片取消，来源筛选移进「来源 · 过滤 · 招聘条件」面板的 ①
    ///   （与岗位选择器同组——两者共同决定呈请的落点与渠道）；
    /// ② 待聘名单不再只有总监：人力按渠道半月一次释放候选人进池（见 TalentMarket），
    ///   「来源」列因此真正产生信息量，且非运行岗的候选人也能从本页录用
    ///   （HrManager.HireCandidate 改为按编制缺口放行，不再要求先呈请）。
    ///
    /// CEO 裁定 2026-10-11 晚（两条，均针对「点开过滤器」这件事太重）：
    /// ① 顶栏读数**自己**带 6 档来源小下拉（末尾 ▾ 表明可点），换来源不再需要进入过滤面板；
    /// ② 过滤面板**去掉那层半透明黑遮罩** —— 它既压暗名单又吃掉点击，
    ///   而来源筛选搬进面板后，玩家本来就要「一边看名单一边调条件」。
    ///
    /// CEO 裁定 2026-10-12（四条，本页做减法）：
    /// ① 名单不再翻页，改右侧滚动条（73 人要翻 10 页，翻页既费手也无法扫读）；
    /// ② 底部「人力职能 →」按钮删除 —— 顶栏已有同名标签页，同一入口不必给两个；
    /// ③ 列义改为 FM 职员搜索的口径：删掉「部门」「空缺职位」，换成「现状」（待聘 / 在职）
    ///   与「偏好工作」（部门 · 岗位）。旧两列把「这个人是谁」写成了「我们会把他放进哪个空缺」，
    ///   待聘者并没有坑位，于是每行都像已被编入某部门某席。
    /// ④ 「偏好工作」挪到「国籍」之后 —— 列序：现状 / 姓名 / 国籍 / 偏好工作 / 期望年薪 / 来源。
    /// </summary>
    public class RecruitmentOfficePanel : BasePanel
    {
        // ---- 来源筛选（同时就是「招聘渠道」选择器）----
        //
        // 原先这里是 5 个来源**页签**（待聘总监 / 校招 / 猎头挖角 / 海外引进 / 内部转岗），
        // 后收敛为顶栏一排「来源」芯片，2026-10-11 再收进过滤面板 ①（顶栏仅留读数 + 下拉）。
        // 「来源」既筛名单，也决定「呈请招聘」落哪个渠道。
        //
        // 芯片下标 → 来源标签（`null` = 全部 = 原五视图的并集）。渠道号由
        // `HrEconomics.ChannelOfTag(tag)` 换算，不再由下标推导。
        const int SrcAll = 0;
        // 首档叫「全部来源」而不是「全部」：KPI 卡标题是「全池 N 人」（介绍所池，不限来源），
        // 值行是当前筛选。两者若一个叫「全池」一个叫「全部」，在同一张卡上只差一个字。
        // 6 个芯片在过滤面板里均分 0.96 − 0.105（行首标签）的宽度，4 字标签在 10 号字下仍有余量。
        static readonly string[] SrcChips =
            { "全部来源", "人才市场", "校招", "猎头挖角", "海外引进", "内部转岗" };
        static readonly string[] SrcTags =
        {
            null,
            HrEconomics.SourceMarket,
            HrEconomics.SourceCampus,
            HrEconomics.SourceHeadhunt,
            HrEconomics.SourceOverseas,
            HrEconomics.SourceInternal
        };

        // ---- 招聘岗位（原 n99 的岗位选择器）----
        const int PosAll = -1;
        static readonly string[] Positions = { "飞行员", "乘务员", "机务工程师" };
        /// <summary>需求人数预设档：比逐格点更省事。</summary>
        static readonly int[] NeedPresets = { 1, 2, 3, 4, 6, 8, 10, 12 };
        static readonly string[] UrgLabels = { "常规", "计划", "紧急" };
        static readonly Color[] UrgColors = { UITheme.Success, UITheme.Warning, UITheme.Danger };

        // ---- 候选人筛选（FM 球探搜索式；只作用到左侧列表，不改变招聘经济口径）----
        //
        // 约定：芯片下标 i → 取值 i - 1，取值 -1 表示「不限 / 默认」。
        // 单一约定让 ChipRow 能统一处理三组互斥筛选，不必每个都写一套映射。
        const int FilterAny = -1;
        static readonly string[] PosChoices = { "不限", "飞行员", "乘务员", "机务工程师" };
        static readonly string[] NatChoices = { "不限", "本国", "外籍" };
        static readonly string[] AgeChoices = { "不限", "≤30", "31-40", "41+" };
        /// <summary>排序：-1 默认 / 0 潜力↓ / 1 年薪↑ / 2 忠诚↓。</summary>
        static readonly string[] SortChoices = { "默认", "潜力↓", "年薪↑", "忠诚↓" };

        // ---- 列表几何（表头与滚动区必须共用同一套宽度，故写成常量）----
        //
        // CEO 裁定 2026-10-12：名单不再翻页，改为右侧滚动条。原先 8 行/页的固定排布
        // （`RowsPerPage` + `第 x / y 页`）随之取消 —— 73 人要翻 10 页，翻页既不直观
        // 也无法与「来源切换后对比」这类扫读行为共存。
        //
        // 为什么要把「右边界」与「让给滚动条的宽度」写成常量而不是各写一遍：
        // 滚动区的视口会被 `UIFactory.VerticalScrollArea` 从右侧内缩 16px 给滚动条让位，
        // 若表头仍按满宽画，右端几列就会与数据行错开十几像素（行高 11 号字，看得见）。
        // 做法：表头容器与滚动区各自按同一组常量定位，两处算出来的可用宽度**恒等**。
        const float ListXMax = 0.961f;   // 表格右边界（「来源」列右端，与撤离的动作列留白口径一致）
        const float BarWidthPx = 16f;    // 滚动条占位：从表格右边界往内让出，故滚动条整体落在数据区之外
        const float ListYMin = 0.010f;   // 滚动区下沿（原翻页行的高度让给列表）
        const float ListYMax = 0.719f;   // 滚动区上沿（紧贴固定表头下沿）
        const float RowH = 40f;          // 行高（像素）——滚动列表按像素排布，不再按比例拉伸

        // ---- 列边界（0..1，**表头与数据行共用**）----
        //
        // 为什么单列一张表：这些数字原先在 DrawListHeaders 与 DrawRow 里各写一遍。
        // 2026-10-12 一天之内就挪了两次列（删「部门/空缺职位」、再把「偏好工作」挪到「国籍」后），
        // 每挪一次都得同步改两处 —— 漏一处就是表头与数据错列。现在只有这一处是权威。
        //
        // 索引即列号，按 CEO 裁定的阅读顺序：先身份（现状 / 姓名 / 国籍），再适配与代价
        // （偏好工作 / 期望年薪），最后来源。前四个是「这个人是谁、适合什么、要多少钱」，
        // 来源是元数据，故压在最后。
        const int CStatus = 0, CName = 1, CNat = 2, CPreferred = 3, CSalary = 4, CSource = 5;
        static readonly float[] ColX = { 0.000f, 0.180f, 0.366f, 0.509f, 0.701f, 0.837f, ListXMax };

        // 会话内保持选择：面板会随导航重建，用 static 才不会每次跳回默认筛选
        static int _src = SrcAll;
        /// <summary>① 岗位：既筛左侧列表，也是招聘目标（不限 = 只看池子、不发起招聘）。</summary>
        static int _posFilter = PosAll;
        static int _natFilter = FilterAny;
        static int _ageFilter = FilterAny;
        static int _sortMode = FilterAny;
        /// <summary>招聘过滤面板是否展开。</summary>
        static bool _filterOpen;
        /// <summary>
        /// 顶栏「来源」读数的小下拉是否展开（CEO 裁定 2026-10-11 晚）。
        /// 与 <see cref="_filterOpen"/> 互斥：两者都能改 `_src`，同开会让玩家看不出「选中的是哪一处」。
        /// </summary>
        static bool _srcDropOpen;

        Transform _content;
        TextMeshProUGUI _toast;

        /// <summary>
        /// 外部入口（培训页「招机务工程师 →」等）：预置招聘岗位并展开过滤面板。
        ///
        /// 必须**同时**写 `_posFilter` 与 `HrContext` —— 面板判「是否选定招聘目标」读的是
        /// `_posFilter`，只写 HrContext 时按钮文案已经点名了岗位、面板却仍显示「未选定招聘目标」
        /// 并禁用「呈请」（2026-10-10 修）。
        /// </summary>
        public static void RequestRecruit(string position)
        {
            int idx = System.Array.IndexOf(Positions, position);
            _posFilter = idx >= 0 ? idx : PosAll;
            HrContext.PresetPosition(position);
            _filterOpen = true;
            _srcDropOpen = false;
        }

        public static RecruitmentOfficePanel Build()
        {
            var root = UIFactory.Panel(null, "n112_职业介绍所", UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.1f, 0.72f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1.2f, -1.2f));
            var cg = card.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(card.transform, "Title", "职业介绍所", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.91f), new Vector2(0.78f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.90f, 0.91f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            UIFactory.Button(card.transform, "Back", "← 返回", 
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.18f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var content = UIFactory.Panel(card.transform, "Content",
                new UIFactory.Anchor(new Vector2(0.03f, 0.11f), new Vector2(0.97f, 0.90f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false);
            content.GetComponent<Image>().raycastTarget = false;

            var toast = UIFactory.Label(card.transform, "Toast", "", 12, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.22f, 0.02f), new Vector2(0.88f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.Warning);

            var panel = root.AddComponent<RecruitmentOfficePanel>();
            panel._content = content.transform;
            panel._toast = toast;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n112_职业介绍所", "职业介绍所");
            return panel;
        }

        protected override void Awake()
        {
            base.Awake();
            WireBackButton();
        }

        public override void Show()
        {
            base.Show();
            // 瞬时浮层不跨导航存活：从本页去人员页再回来，下拉该是收起的
            // （否则会留一块盖在名单上的浮层，且玩家已经忘了它是谁弹出来的）。
            _srcDropOpen = false;
            Refresh();
        }

        void OnEnable() => Refresh();

        void Refresh()
        {
            if (_content == null)
            {
                var c = transform.Find("Card/Content");
                if (c != null) _content = c;
            }
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            var mgr = DepartmentManager.Instance;
            var gs = GameState.Instance;
            PersonnelGenerator.EnsurePool(gs);

            var rows = CollectRows(gs, mgr);
            // 全池统计只算一遍，工具栏读数用它——两处各算一次必然在某次改动后漂移。
            var pool = ComputePoolStats(gs, mgr);

            // 提示行一律用弱色：六个来源现在是平权的筛选器，不再有「默认视图 / 越界视图」之分，
            // 原先给非默认页签上的警告色会把正常的渠道筛选渲染成异常状态。
            L("Hint", HintFor(_src), 0.00f, 0.955f, 1.00f, 1.00f, 11, UITheme.TextMuted);

            if (gs != null) DrawKpiStrip(gs);
            DrawToolbar(rows.Total, pool);

            DrawListHeaders();
            if (rows.Total == 0)
                L("Empty", EmptyFor(_src), 0.00f, 0.36f, 1.00f, 0.48f, 14, UITheme.Warning);
            else
                DrawList(rows.Items);

            // 过滤器最后画 → 覆盖在列表之上（FM「过滤」下拉的层级关系）
            if (_filterOpen && gs != null) DrawFilterPanel(gs);
            else if (_srcDropOpen) DrawSrcDropdown();
        }

        // ================= KPI 条（对标 FM 球探搜索顶部信息卡）=================

        /// <summary>
        /// 介绍所全池统计：待聘人数（不限职级/来源）与其中来自招聘渠道的候选人数。
        /// 口径与 <see cref="CollectRows"/> 不同——后者是「当前来源筛选后的结果」。
        /// 两者必须分别标注，否则玩家会把全池数当成下表总数（2026-10-10 修）。
        /// </summary>
        struct PoolStats
        {
            public int Listed;
            public int ChannelCandidates;
        }

        static PoolStats ComputePoolStats(GameState gs, DepartmentManager mgr)
        {
            var pool = default(PoolStats);
            if (gs?.staffPool == null) return pool;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || !p.onMarket) continue;
                if (mgr != null && mgr.IsOccupied(p.id)) continue;
                pool.Listed++;
                if (HrEconomics.NormalizeSource(p.source) != HrEconomics.SourceMarket)
                    pool.ChannelCandidates++;
            }
            return pool;
        }

        /// <summary>
        /// 全局 KPI 条（对标 FM 顶部信息卡）。
        ///
        /// CEO 裁定 2026-10-12 晚：原先的首卡「全池 / 当前来源」整张删除。
        /// 它的数值与工具栏计数是**同一个值** —— 同一次 `Refresh()` 把 `rows.Total` 传给两处，
        /// 拼出来的串只差一个分隔符（`全部来源 73 人` vs `全部来源 · 73 人`），
        /// 这种「只差一个符号」的重复比原样重复更坏：它会让人去找两者的差别，而差别并不存在。
        ///
        /// 更根本的是位置：本条其余四张卡都是**全局**口径（在途招聘 / 运行岗缺口 / 空缺率 /
        /// 人力成本），只有首卡随筛选变化。决定筛选数的控件在工具栏，所以那类数字该待在工具栏，
        /// 与本条是否「全局」这一点保持一致。全池与渠道候选因此并入工具栏读数。
        /// </summary>
        void DrawKpiStrip(GameState gs)
        {
            var hr = HrManager.EnsureState(gs);

            int inFlight = 0;
            if (hr?.vacancies != null)
                for (int i = 0; i < hr.vacancies.Count; i++)
                    if (hr.vacancies[i] != null && hr.vacancies[i].hired < hr.vacancies[i].needed) inFlight++;

            int gap = 0;
            for (int i = 0; i < Positions.Length; i++)
                if (HrManager.RecruitRoomLeft(gs, Positions[i], out _, out _, out _, out int room) && room > 0)
                    gap += room;

            float vacancy = hr != null ? hr.vacancyRate : 0f;

            // 四张卡一律全局口径：没有一张随「来源」筛选变化。
            // 全池 / 渠道候选 / 当前来源人数已移进工具栏读数（见 DrawToolbar），
            // 理由写在 DrawKpiStrip 的注释里。
            KpiCard(0, "在途招聘", $"{inFlight} 项", inFlight > 0 ? UITheme.Accent : UITheme.TextMuted);
            KpiCard(1, "运行岗缺口", $"{gap} 人", gap > 0 ? UITheme.Warning : UITheme.Success);
            KpiCard(2, "空缺率", $"{vacancy * 100f:F0}%", vacancy > 0.15f ? UITheme.Warning : UITheme.Success);
            KpiCard(3, "人力成本", $"{gs.LaborCostAnnualWan:F0} 万/年", UITheme.TextPrimary);
        }

        /// <summary>KPI 卡数：由 <see cref="DrawKpiStrip"/> 的卡片数决定，间距按它摊满整条。</summary>
        const int KpiCount = 4;

        void KpiCard(int i, string title, string value, Color valueColor)
        {
            // 由 5 卡减到 4 卡后必须重算间距：沿用 0.2 的档距会在右端留出一张卡的空洞，
            // 四张卡挤在左边、右边空一块，看着像有一张卡加载失败。
            const float pad = 0.002f;                       // 左右留白（原值）
            const float gap = 0.054f;                       // 卡间距
            float w = (1f - 2f * pad - (KpiCount - 1) * gap) / KpiCount;
            float x0 = pad + i * (w + gap);
            var card = UIFactory.Panel(_content, "K" + i,
                new UIFactory.Anchor(new Vector2(x0, 0.872f), new Vector2(x0 + w, 0.948f),
                    Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.Label(card.transform, "T", title, 10, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.50f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            UIFactory.Label(card.transform, "V", value, 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.02f), new Vector2(0.96f, 0.52f), Vector2.zero, Vector2.zero),
                valueColor, FontStyles.Bold);
        }

        // ================= 数据装配（条件折叠的核心）=================

        struct Row
        {
            public PersonnelData person;
            /// <summary>现状：只有「待聘」/「在职」两值（CEO 裁定 2026-10-12）。</summary>
            public string status;
            /// <summary>偏好工作：该人对应的「部门 · 岗位」；内部转岗者为可调入的目标。</summary>
            public string preferred;
            public string salary;      // 展示用年薪文案
            public string sourceName;
            public Color sourceColor;
        }

        struct RowSet
        {
            public List<Row> Items;
            public int Total;
        }

        /// <summary>
        /// 「偏好工作」文案：部门 · 岗位。
        ///
        /// 这是 2026-10-12 取代旧「部门」+「空缺职位」两列的**单一**列。旧表头把「这个人是谁」
        /// 写成了「我们会把他放进哪个空缺」——待聘者并没有坑位，于是每行都像已被编入某部门某席，
        /// 读起来就是「这个人对应一个空缺」，CEO 判定为怪。改为 FM 职员搜索的列义：
        /// 左边一列说身份（现状），一列说他适配的工作。
        ///
        /// 部门和岗位各自都可能缺（旧档渠道候选人可能没 department；总监的 DisplayTitle() 是"总监"，
        /// 单看不知哪个部），所以两边都空/缺失时退化成只打印另半边，绝不留「 · 岗位」这种
        /// 以分隔符开头的怪串。
        /// </summary>
        static string PreferredText(string dept, string jobTitle, string rank)
        {
            string role = StaffEstablishment.DisplayTitle(jobTitle, rank);
            string dn = DepartmentCatalog.DisplayName(dept);
            if (string.IsNullOrEmpty(dn)) return role ?? "";
            if (string.IsNullOrEmpty(role)) return dn;
            return dn + " · " + role;
        }

        /// <summary>当前「来源」筛选的标签；`null` = 全部（原五视图并集）。</summary>
        static string CurrentTag() =>
            SrcTags[Mathf.Clamp(_src, 0, SrcTags.Length - 1)];

        /// <summary>当前「来源」对应的招聘渠道号；未指向具体渠道（全部 / 人才市场）返回 -1。</summary>
        static int CurrentChannel() => HrEconomics.ChannelOfTag(CurrentTag());

        /// <summary>顶栏与页码行共用的来源标签文案（「全部」/「人才市场」/…）。</summary>
        static string SrcLabel => SrcChips[Mathf.Clamp(_src, 0, SrcChips.Length - 1)];

        RowSet CollectRows(GameState gs, DepartmentManager mgr)
        {
            var set = new RowSet { Items = new List<Row>(), Total = 0 };
            if (gs?.staffPool == null) return set;

            string want = CurrentTag();

            // 内部转岗不是人才池里的一个来源标记，而是「在编可调岗者」的实时推导
            // （HrManager.InternalCandidates），与人才池（onMarket）互斥，故单独并入。
            // 它在「全部」与「内部转岗」两个视图下出现。
            if (want == null || want == HrEconomics.SourceInternal)
            {
                var elig = HrManager.InternalCandidates(gs);
                for (int i = 0; i < elig.Count; i++)
                {
                    var p = elig[i];
                    if (!HrManager.TryFindTransferTarget(gs, p, out string td, out string tr)) continue;
                    if (!PassesScoutFilter(gs, p)) continue;
                    set.Items.Add(new Row
                    {
                        person = p,
                        status = "在职",
                        // 在职者「现状 = 在职」，所以「他现在在哪把椅子」只能靠本列表达：
                        // 这里写的是**可调入的目标**（TryFindTransferTarget 的结论），与
                        // 市场候选人写「他自己那一档」相对 —— 两者读法统一为「他会坐哪把椅子」。
                        preferred = PreferredText(td, tr, null) + "（可调入）",
                        salary = $"+{p.baseSalary * 0.2f:F1} 万",
                        sourceName = TagShortName(HrEconomics.SourceInternal),
                        sourceColor = TagColor(HrEconomics.SourceInternal)
                    });
                }
                if (want == HrEconomics.SourceInternal)
                {
                    SortRows(set.Items);
                    set.Total = set.Items.Count;
                    return set;
                }
            }

            // 人才市场视图收总监（负责人）级；渠道视图收该渠道落池的任何职级。
            //
            // 此前两种视图都套了 CanCeoHire（rank == "负责人"）闸门，而渠道候选人由
            // HrManager.RankForPosition 一律产出为「专员」，于是校招/猎头/海外三个视图
            // 永远为空——渠道实体化整条链路在 UI 上等于没接上。判定口径收敛到
            // StaffRecruitment.AcceptedInSourceView / AcceptedInAnySourceView，并有 LogicSelfTests 兜底。
            bool directorView = want == HrEconomics.SourceMarket;
            var wantCat = _posFilter >= 0 && _posFilter < Positions.Length
                ? HrEconomics.OperationalCategory(Positions[_posFilter])
                : HrEconomics.OpsCategory.None;

            // 岗位类别筛选只对有具体运行岗的渠道视图生效：待聘总监与内部转岗都是管理岗
            // （jobTitle = 总监 / 编制席职务），无运行岗类别，套用会把名单直接清空。
            bool applyCat = !directorView && want != HrEconomics.SourceInternal && want != null;

            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                bool accept = want == null
                    ? StaffRecruitment.AcceptedInAnySourceView(p)
                    : StaffRecruitment.AcceptedInSourceView(p, want);
                if (!accept) continue;
                if (mgr != null && mgr.IsOccupied(p.id)) continue;

                if (applyCat && wantCat != HrEconomics.OpsCategory.None
                    && HrEconomics.OperationalCategory(p.jobTitle) != wantCat) continue;
                if (!PassesScoutFilter(gs, p)) continue;

                set.Items.Add(new Row
                {
                    person = p,
                    // 池内候选人一律「待聘」——包括猎头挖到的在职者：他是**别家**的人，
                    // 对玩家而言仍是「可以谈」的待聘状态，不是本公司员工。
                    status = "待聘",
                    preferred = PreferredText(p.department, p.jobTitle, p.rank),
                    salary = p.baseSalary > 0 ? $"{p.baseSalary:F0} 万" : "—",
                    // 来源列显示**这个人自己的来源**（不再显示当前视图名）：
                    // 「全部」视图下各行来源不同，若统一打印视图名，整列就没有信息量了。
                    sourceName = TagShortName(p.source),
                    sourceColor = TagColor(p.source)
                });
            }

            SortRows(set.Items);
            set.Total = set.Items.Count;
            return set;
        }

        /// <summary>
        /// 候选人筛选（国籍 / 年龄）。岗位另按运行岗类别单判，因为总监视图要豁免。
        /// 只筛「看谁」，不参与任何招聘经济口径。
        /// </summary>
        bool PassesScoutFilter(GameState gs, PersonnelData p)
        {
            if (p == null) return false;

            if (_natFilter != FilterAny)
            {
                bool foreign = PersonnelGenerator.IsForeign(p, gs);
                if (_natFilter == 0 ? foreign : !foreign) return false;
            }

            if (_ageFilter == 0 && p.age > 30) return false;
            if (_ageFilter == 1 && (p.age < 31 || p.age > 40)) return false;
            if (_ageFilter == 2 && p.age < 41) return false;

            return true;
        }

        /// <summary>排序（FM 球探搜索表头排序的简化版）。<see cref="FilterAny"/> 保持原顺序。</summary>
        void SortRows(List<Row> items)
        {
            if (_sortMode == FilterAny || items == null || items.Count < 2) return;
            items.Sort((a, b) =>
            {
                var x = a.person;
                var y = b.person;
                if (x == null || y == null) return 0;
                switch (_sortMode)
                {
                    case 0: return y.potential.CompareTo(x.potential);
                    case 1: return x.baseSalary.CompareTo(y.baseSalary);
                    case 2: return y.loyalty.CompareTo(x.loyalty);
                    default: return 0;
                }
            });
        }

        /// <summary>来源标签 → 表格「来源」列的短名。</summary>
        static string TagShortName(string tag) => HrEconomics.NormalizeSource(tag) switch
        {
            HrEconomics.SourceCampus => "校招",
            HrEconomics.SourceHeadhunt => "猎头",
            HrEconomics.SourceOverseas => "海外",
            HrEconomics.SourceInternal => "内部转岗",
            HrEconomics.SourceInherited => "继承在册",
            _ => "市场"
        };

        static Color TagColor(string tag) => HrEconomics.NormalizeSource(tag) switch
        {
            HrEconomics.SourceCampus => UITheme.TextMuted,      // 24 周：慢而廉
            HrEconomics.SourceHeadhunt => UITheme.Danger,       // 4 周：快而贵
            HrEconomics.SourceOverseas => UITheme.Warning,      // 12 周
            HrEconomics.SourceInternal => UITheme.Success,      // 即时，但有编制代价
            _ => UITheme.TextPrimary
        };

        // 「处置动作在人员页」这句话在每个来源视图下都要说：
        // 列表右侧的动作列已整体搬进 n114，不说明的话玩家会在名单里找按钮（2026-10-10）。
        const string ActionWhere = "点姓名进入人员页，在那里聘请／录用／退回／调岗。";

        static string HintFor(int src) => src switch
        {
            0 => "介绍所全部候选人 = 人才市场总监 + 各渠道落池者 + 内部可调岗员工。" + ActionWhere,
            1 => "人才市场：只列出总监候选人。您只负责总监的录用与解聘；其余岗位由人力招聘后呈请审批。" + ActionWhere,
            2 => "批量校招候选人（周期最长、成本最低、潜力偏低但忠诚度高）。" + ActionWhere,
            3 => "猎头挖角候选人（到岗最快、薪资期望最高、忠诚度偏低）。" + ActionWhere,
            4 => "海外引进候选人（稀缺岗位来源，薪资偏高并有适应期）。" + ActionWhere,
            _ => "内部转岗：由在编员工调入缺口岗位。不花外部招聘费，但原部门会留下编制缺口（人力将重新招聘），且有 14 天交接期打折。" + ActionWhere
        };

        static string EmptyFor(int src) => src switch
        {
            0 => "介绍所当前没有任何候选人（人才市场总监与各渠道候选人都已处置完毕，也无在编可调岗员工）。",
            1 => "当前没有待聘总监候选人。",
            2 => "暂无校招候选人。半月一次的人力释放还没补上；也可在 ① 选定「校招」后呈请批量招聘。",
            3 => "暂无猎头挖角候选人。半月一次的人力释放还没补上；也可在 ① 选定「猎头挖角」后呈请招聘。",
            4 => "暂无海外引进候选人。半月一次的人力释放还没补上；也可在 ① 选定「海外引进」后呈请招聘。",
            _ => "暂无可调岗员工（需在编、非总监、未占管理岗，且存在同职级空缺）。"
        };

        // ================= 工具栏：来源读数 + 计数 + 过滤开关 =================
        //
        // CEO 裁定 2026-10-11：顶栏的 6 个「来源」芯片取消，来源筛选移进
        // 「来源 · 过滤 · 招聘条件」面板的 ①（与岗位选择器同组，因为来源同时是呈请的渠道）。
        //
        // CEO 裁定 2026-10-11 晚（本段）：读数**自己**带一个 6 档小下拉。
        // 上一版把读数做成「点开整个过滤面板」的入口，两个问题：
        //   ① 读数长得像标签，「点了会弹东西」无从预期 —— 末尾加 ▾ 才把这件事说出来；
        //   ② 只想换个来源却盖住半屏名单 + 蒙一层灰，代价与意图不成比例（见 DrawFilterPanel 注）。
        // 现在读数 = 快速换来源（1 击展开、1 击选择），过滤面板 = 调全部条件（2 击进入）。
        void DrawToolbar(int total, PoolStats pool)
        {
            bool all = _src == SrcAll;
            B("SrcReadout", $"来源：{(all ? "全部" : SrcChips[_src])} ▾",
                0.000f, 0.786f, 0.170f, 0.866f,
                _srcDropOpen ? UITheme.Accent : (all ? UITheme.PanelDeep : UITheme.NavActive),
                () =>
                {
                    _srcDropOpen = !_srcDropOpen;
                    if (_srcDropOpen) _filterOpen = false;   // 互斥：同开会让「选中的是哪一处」读不出来
                    Refresh();
                }, 11);

            // 读数一段话里放**两种口径**，而且要挨着「来源」下拉（那是改变它的控件）：
            //   前段 = 当前来源筛出多少人（随筛选变）
            //   后段 = 池子本身多大（全局：全池待聘 / 其中渠道候选）
            // 2026-10-10 曾把后段放在 KPI 首卡、把前段放在本行，于是界面上出现两个只差一个
            // 「·」的串（`全部来源 73 人` / `全部来源 · 73 人`）。两段现已并到同一行，
            // 并且各自带自己的标签 —— 默认视图下两个数相等（全池 = 未筛的池）也不会被读成重复，
            // 因为「全部来源」与「全池」是两个不同的口径名（§15 早已辨析过这对关系）。
            // 分隔符有意分两级：组内用「 · 」，两组之间用全角空格（`　`）留出更宽的间隔，
            // 这样「当前筛选」与「池子规模」这两段不会被读成一串并列项。
            string cnt = $"{SrcLabel} · {total} 人　全池 {pool.Listed} 人 · 渠道候选 {pool.ChannelCandidates} 人";
            if (_posFilter != PosAll && CurrentChannel() >= 0) cnt += $"（岗位：{Positions[_posFilter]}）";
            L("Cnt", cnt, 0.185f, 0.790f, 0.740f, 0.862f, 11, UITheme.TextMuted);

            // 过滤开关：面板展开时高亮；有任一筛选在生效时带 ● 提示
            bool active = _src != SrcAll || _posFilter != PosAll || _natFilter != FilterAny
                || _ageFilter != FilterAny || _sortMode != FilterAny;
            B("Filter", _filterOpen ? "收起过滤 ✕" : (active ? "来源 / 过滤 ● ▾" : "来源 / 过滤 · 招聘条件 ▾"),
                0.780f, 0.786f, 1.000f, 0.866f,
                _filterOpen || active ? UITheme.Accent : UITheme.PanelDeep,
                () =>
                {
                    _filterOpen = !_filterOpen;
                    if (_filterOpen) _srcDropOpen = false;   // 互斥：同开会让「选中的是哪一处」读不出来
                    Refresh();
                }, 12);
        }

        // ================= 来源小下拉（顶栏读数的弹出层）=================

        /// <summary>
        /// 顶栏「来源：…  ▾」的 6 档下拉：全部来源 / 人才市场 / 校招 / 猎头挖角 / 海外引进 / 内部转岗。
        ///
        /// 与过滤面板 ① 里的「来源」一行是**同一份状态** <see cref="_src"/>（不各存一份），
        /// 两处只是入口快慢之分：这里 1 击换来源，那里连带调岗位 / 国籍 / 年龄 / 排序。
        /// 下拉从读数正下方展开（x 与读数齐平，仅略宽一点），贴着触发它的控件 —— 玩家一眼能追出父子关系。
        /// </summary>
        void DrawSrcDropdown()
        {
            // 行数直接取自 SrcChips —— 不写死 6：下拉的格子数与来源档数必须同源，
            // 日后增删来源（如「重启招聘」）时这里不会漏改一半（上面的行高除法也用它）。
            int rows = SrcChips.Length;
            const float rowH = 0.034f;
            float top = 0.786f;               // 读数下沿
            float bottom = top - rows * rowH; // 6 档 → 0.786 → 0.582

            // 点面板外 = 收起。画在选项之前 → 层级更低，不会盖住选项本身。
            //
            // 这里**可以**吃掉这一击（与过滤面板相反，理由见 DrawFilterPanel 的那段注）：
            // 下拉是瞬时控件，「点别处＝取消」是通用预期，代价只是「想点人名得点第二下」，
            // 而收益是它一定收得掉 —— 不会留下一块挡着名单的浮层。
            UIFactory.Button(_content, "SrcScrim", "",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                new Color(0f, 0f, 0f, 0f),
                () => { _srcDropOpen = false; Refresh(); }, 1);

            var pnl = UIFactory.Panel(_content, "SrcDrop",
                new UIFactory.Anchor(new Vector2(0.000f, bottom), new Vector2(0.192f, top), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(pnl, UITheme.Accent, new Vector2(1.2f, -1.2f));
            var host = pnl.transform;

            for (int i = 0; i < rows; i++)
            {
                int idx = i;
                // 局部归一坐标：面板高 = rows * rowH，故每行正好占 1/rows。
                float y1 = 1f - i / (float)rows;
                float y0 = 1f - (i + 1) / (float)rows;
                B("SrcOpt" + i, SrcChips[i], 0.03f, y0, 0.97f, y1,
                    _src == i ? UITheme.NavActive : UITheme.PanelDeep,
                    () => { SelectSource(idx); _srcDropOpen = false; Refresh(); }, 11, host);
            }
        }

        void SelectSource(int src)
        {
            if (_src == src) return;
            _src = src;
            Refresh();
        }

        // ================= 过滤面板（原 n99「招聘策略弹窗」+ FM 球探搜索过滤样式）=================

        void DrawFilterPanel(GameState gs)
        {
            var hr = HrManager.EnsureState(gs);
            var pnl = UIFactory.Panel(_content, "FilterPanel",
                new UIFactory.Anchor(new Vector2(0.575f, 0.03f), new Vector2(1.000f, 0.97f),
                    Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(pnl, UITheme.Accent, new Vector2(1.4f, -1.4f));
            var host = pnl.transform;

            // 本面板**不设** Scrim（CEO 裁定 2026-10-11 晚，原为半透明黑遮罩 + 点外收起）。
            //
            // 旧实现用一层 `(0,0,0,0.28)` 的按钮盖住左侧 0–0.573：既压暗名单，又吃掉点击。
            // 那在「点开即看一眼」时无妨，但来源筛选搬进本面板之后，玩家要**一边看名单一边调条件** ——
            // 而名单被压暗且点不动（连点人名开档案都不行），正好挡住了本面板存在的理由。
            //
            // 为什么不保留一层「透明但可点」的 Scrim 来换回点外收起：UGUI 的点击要求
            // pointerPress == pointerUpHandler，Scrim 若吃掉 pointerDown，抬起时手指已落在
            // 新画出来的行上，两者不相等 → 那一击必然失效。也就是说「点外收起」与
            // 「外点还能穿透到名单」在 UGUI 里不可兼得，二者只能取一，这里取后者（CEO 明确要保留点人名）。
            // 与投影仪式的对照：小下拉（DrawSrcDropdown）相反地**保留**了 Scrim —— 它是瞬时控件，
            // 点外=取消是通用预期，吃掉那一击没有代价。
            int channel = CurrentChannel();

            // 「渠道候选」的口径写在行内：它数的是校招/猎头/海外/内部转岗**落池**的人，
            // 自然人才池不计 —— 否则「渠道 0」会被读成渠道**编号**（而 0 恰好是校招）。
            // 「切换」提示已可在本面板内完成（来源已移入 ①），故不再要求"先关闭面板"（2026-10-11）。
            L("FH", "来源 · 过滤 · 招聘条件", 0.04f, 0.943f, 0.80f, 0.992f, 14, UITheme.Accent, true, host);
            L("FSub", channel < 0
                    ? $"来源＝{SrcLabel}　·　未指向招聘渠道（择一渠道后「呈请招聘」才有落点）"
                    : $"来源＝{SrcLabel}　·　招聘渠道：{HrEconomics.ChannelName(channel)}",
                0.04f, 0.897f, 0.92f, 0.929f, 10, UITheme.TextMuted, false, host);
            B("FClose", "✕", 0.86f, 0.936f, 0.96f, 0.992f, UITheme.PanelDeep,
                () => { _filterOpen = false; Refresh(); }, 14, host);

            // ① 来源 + 岗位：两块选择器同组，因为它们是同一件事的两面 ——
            // 既筛左侧列表，也共同决定「呈请招聘」的落点与渠道（岗位定要招谁，来源定走哪个渠道）。
            // 来源原先是顶栏一排 6 个芯片；CEO 裁定 2026-10-11 收进本面板，顶栏只留读数。
            L("F1H", "① 来源与岗位（筛列表 + 定招聘目标）", 0.04f, 0.848f, 0.96f, 0.886f, 12,
                UITheme.Accent, true, host);
            ChipRow(host, "Src", "来源", SrcChips, _src, 0.786f, 0.04f, 0.96f, i => SelectSource(i));
            ChipRow(host, "Pos", "岗位", PosChoices, _posFilter + 1, 0.728f, 0.04f, 0.96f, i =>
            {
                _posFilter = i - 1;
                // 选定具体岗位时同步招聘目标（省一次点击）；「不限」只筛看不改招聘条件
                if (i > 0) HrContext.PresetPosition(Positions[i - 1]);
                Refresh();
            });

            // ② 候选人筛选（FM 球探搜索的筛选条件；只影响左侧列表）
            L("F2H", "② 候选人筛选（只影响名单）", 0.04f, 0.676f, 0.96f, 0.714f, 12, UITheme.Accent, true, host);
            ChipRow(host, "Nat", "国籍", NatChoices, _natFilter + 1, 0.618f, 0.04f, 0.96f, i =>
            { _natFilter = i - 1; Refresh(); });
            ChipRow(host, "Age", "年龄", AgeChoices, _ageFilter + 1, 0.560f, 0.04f, 0.96f, i =>
            { _ageFilter = i - 1; Refresh(); });
            ChipRow(host, "Sort", "排序", SortChoices, _sortMode + 1, 0.502f, 0.04f, 0.96f, i =>
            { _sortMode = i - 1; Refresh(); });

            // ③ 招聘条件（原 n99 的紧迫度与需求人数；岗位已在 ① 选定）
            L("F3H", "③ 招聘条件", 0.04f, 0.448f, 0.96f, 0.486f, 12, UITheme.Accent, true, host);
            L("UrgL", "紧迫度", 0.04f, 0.390f, 0.15f, 0.438f, 11, UITheme.TextMuted, false, host);
            for (int i = 0; i < UrgLabels.Length; i++)
            {
                int idx = i;
                bool sel = HrContext.RecruitUrgency == i;
                float x0 = 0.16f + i * 0.14f;
                B("Urg" + i, UrgLabels[i], x0, 0.390f, x0 + 0.13f, 0.438f,
                    sel ? UrgColors[i] : UITheme.PanelDeep,
                    () => { HrContext.RecruitUrgency = idx; Refresh(); }, 11, host);
            }
            // 「需求人数」此前没有行首标签，只有孤零零的「− 12 人 +」，无从得知在调什么。
            L("NeedL", "需求人数", 0.62f, 0.390f, 0.75f, 0.438f, 11, UITheme.TextMuted, false, host);
            B("NeedM", "−", 0.75f, 0.390f, 0.81f, 0.438f, UITheme.PanelDeep, () => StepNeed(-1), 14, host);
            L("NeedV", $"{HrContext.RecruitNeeded} 人", 0.81f, 0.390f, 0.90f, 0.438f, 12,
                UITheme.TextPrimary, true, host);
            B("NeedP", "＋", 0.90f, 0.390f, 0.96f, 0.438f, UITheme.PanelDeep, () => StepNeed(1), 14, host);

            // ④ 编制与成本预估（「确认到岗」随在途管道同排 —— 它是该管道的收尾动作）
            L("F4H", "④ 编制与成本预估", 0.04f, 0.334f, 0.96f, 0.372f, 12, UITheme.Accent, true, host);

            bool posChosen = _posFilter != PosAll;
            if (!posChosen)
            {
                L("Cap", "未选定招聘目标 —— 请先在 ① 选定具体岗位（「不限」只用于查看名单）",
                    0.04f, 0.284f, 0.96f, 0.324f, 11, UITheme.Warning, false, host);
            }
            else if (HrManager.RecruitRoomLeft(gs, HrContext.RecruitPosition, out int est, out int onDuty,
                    out int inFlight, out int room))
            {
                L("Cap", $"招聘目标「{HrContext.RecruitPosition}」· 编制 {est} 人 · 在职 {onDuty} · 在途 {inFlight} · 缺口 {room} 人",
                    0.04f, 0.284f, 0.96f, 0.324f, 11,
                    room > 0 ? UITheme.TextPrimary : UITheme.Warning, false, host);
            }
            else
            {
                L("Cap", $"招聘目标「{HrContext.RecruitPosition}」· 无在役机队 → 编制按 0 计（扩机队后自动上调）",
                    0.04f, 0.284f, 0.96f, 0.324f, 11, UITheme.Warning, false, host);
            }

            // 在途管道：原 n86「在途招聘管道」明细（含渠道周期倒计时）。
            // 「确认到岗」原与「呈请」并列在动作行 —— 但那是**两个阶段**（发起 / 收尾），混排易被读成
            // 两个并列选项。现收进这一行做行内操作：仍按当前 ① 选定岗位执行，非 vacancies[0]。
            var vac = posChosen
                ? hr.vacancies.Find(v => v != null && v.position == HrContext.RecruitPosition)
                : null;
            bool canArrive = vac != null && vac.hired < vac.needed;
            L("Pipe", posChosen
                    ? (vac == null
                        ? "在途：暂无进行中的招聘（点下方「呈请 CEO 批复招聘」发起）"
                        : "在途：" + HrManager.VacancyStatusLine(vac, gs.SimUtc))
                    : "在途：选定岗位后显示该岗位管道",
                0.04f, 0.238f, 0.74f, 0.282f, 10,
                canArrive ? UITheme.TextPrimary : UITheme.TextMuted, false, host);
            B("Arrive", "确认到岗", 0.76f, 0.234f, 0.96f, 0.284f,
                canArrive ? UITheme.Accent : UITheme.PanelDeep, ConfirmArrival, 11, host);

            if (!posChosen)
            {
                L("Cost", "成本预估随岗位显示：单价 · 合计 · 到岗周期 · 成功率",
                    0.04f, 0.180f, 0.96f, 0.224f, 11, UITheme.TextMuted, false, host);
                L("Mkt", "选定岗位后即可呈请招聘。",
                    0.04f, 0.126f, 0.96f, 0.168f, 11, UITheme.TextMuted, false, host);
            }
            else if (channel >= 0)
            {
                float shortage = ShortageOf(hr, HrContext.RecruitPosition);
                float per = HrEconomics.RecruitCostPerHire(channel, shortage);
                float weeks = HrEconomics.RecruitLeadWeeks(channel);
                float rate = HrEconomics.RecruitSuccessRate(channel, shortage);
                string eta = channel == 3 ? "即时" : $"约 {weeks:F0} 周";
                string costLine = channel == 3
                    ? $"免外部费用 · 到岗 {eta} · 成功率 {rate * 100:F0}%"
                    : $"单价 ${per:F0}/人 · 合计 ≈ {HrContext.RecruitNeeded * per / 10000f:F2} 万 · 到岗 {eta} · 成功率 {rate * 100:F0}%";
                L("Cost", costLine, 0.04f, 0.180f, 0.96f, 0.224f, 11, UITheme.TextPrimary, false, host);
                L("Mkt", $"人才紧缺指数 {shortage:F0} · 成本系数 ×{HrEconomics.ShortageCostFactor(shortage):F2}",
                    0.04f, 0.126f, 0.96f, 0.168f, 11, UITheme.TextMuted, false, host);
            }
            else
            {
                L("Cost", "当前「来源」未指向招聘渠道 → 请先在 ① 里选定一个来源（校招 / 猎头挖角 / 海外引进 / 内部转岗）",
                    0.04f, 0.180f, 0.96f, 0.224f, 11, UITheme.Warning, false, host);
                L("Mkt", "人才市场总监由 CEO 直接在人员页聘请，不产生招聘费。",
                    0.04f, 0.126f, 0.96f, 0.168f, 11, UITheme.TextMuted, false, host);
            }

            // 分隔线：以上是「筛选条件 + 成本预估」，以下是「动作」，避免两者视觉糊在一起。
            var div = UIFactory.Panel(host, "FDiv",
                new UIFactory.Anchor(new Vector2(0.04f, 0.110f), new Vector2(0.96f, 0.112f), Vector2.zero, Vector2.zero),
                UITheme.Border, rounded: false);
            var divImg = div.GetComponent<Image>();
            if (divImg != null) divImg.raycastTarget = false;   // 纯装饰，不吃点击

            // 动作：主操作只剩「呈请」；「重置条件」降为次要操作置于右端，用描边色与禁用的深底区分。
            // 「暂缓」已删除 —— 它与 ✕ / 点面板外 / 「收起过滤」三者完全等效（都只做 _filterOpen=false ；
            // 条件并不存在面板里，而是存 HrContext 的 static 字段，本就不会丢）。它既多占一个主操作位，
            // 又在未呈请时暗示「有草稿待存」，属纯冗余。
            bool canSubmit = channel >= 0 && posChosen;
            B("Submit", "呈请 CEO 批复招聘", 0.04f, 0.054f, 0.66f, 0.108f,
                canSubmit ? UITheme.Success : UITheme.PanelDeep, SubmitRecruit, 13, host);
            B("Reset", "重置条件", 0.68f, 0.054f, 0.96f, 0.108f, UITheme.Border,
                ResetConditions, 11, host);

            L("FTip", canSubmit
                    ? "本页即招聘发起处。呈请后候选人按渠道周期落池，届时在左侧名单点姓名进人员页处置；已落池的走「确认到岗」。"
                    : channel < 0
                        ? "要发起招聘：先在 ① 里选定来源（校招 / 猎头挖角 / 海外引进 / 内部转岗），再在 ① 选定岗位。"
                        : "要发起招聘：请在 ① 选定具体岗位（「不限」只用于查看名单，不能作为招聘目标）。",
                0.04f, 0.000f, 0.96f, 0.046f, 10,
                canSubmit ? UITheme.TextMuted : UITheme.Warning, false, host);
        }

        /// <summary>确认到岗：按成功率把已落池候选人转为在编（原 n86「推进管道」按钮，随管道移交至此）。</summary>
        void ConfirmArrival()
        {
            var gs = GameState.Instance;
            if (gs == null) { Toast("无 GameState。"); return; }
            if (HrManager.Instance == null) { Toast("HrManager 未就绪。"); return; }
            var hr = HrManager.EnsureState(gs);
            var vac = _posFilter == PosAll
                ? null
                : hr.vacancies.Find(v => v != null && v.position == HrContext.RecruitPosition);
            if (vac == null)
            {
                Toast("该岗位暂无进行中的招聘 —— 先在 ① 选定岗位并呈请招聘。");
                return;
            }
            string msg;
            HrManager.Instance.RecruitNow(HrContext.RecruitPosition, out msg);
            Toast(msg);
            Refresh();
        }

        /// <summary>
        /// 一行筛选芯片：可选标签 + 互斥选项，下标 i 对应取值 i - 1（-1 = 不限 / 默认）。
        /// <paramref name="label"/> 为 null 时不画行首标签（选项占满整行）。
        /// </summary>
        void ChipRow(Transform host, string rowName, string label, string[] labels, int selected,
            float y, float x0, float x1, UnityEngine.Events.UnityAction<int> onPick)
        {
            // 0.044（原 0.050）：① 里多出一行「来源」后，本面板上段共 1 heading + 2 ChipRow +
            // 1 heading + 3 ChipRow，必须收窄行高才装得下（详见 DrawFilterPanel 的坐标推导）。
            const float h = 0.044f;
            float cx0 = x0;
            if (!string.IsNullOrEmpty(label))
            {
                L(rowName + "L", label, x0, y, x0 + 0.10f, y + h, 10, UITheme.TextMuted, false, host);
                cx0 = x0 + 0.105f;
            }

            float w = (x1 - cx0) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                B(rowName + i, labels[i], cx0 + i * w + 0.002f, y, cx0 + (i + 1) * w - 0.002f, y + h,
                    selected == i ? UITheme.NavActive : UITheme.PanelDeep, () => onPick(idx), 10, host);
            }
        }

        static void StepNeed(int dir)
        {
            int cur = HrContext.RecruitNeeded;
            int idx = System.Array.IndexOf(NeedPresets, cur);
            if (idx < 0)
            {
                // PresetPosition 给的是「编制缺口」原值，不在预设档里 → 取最接近的一档再走
                idx = 0;
                for (int i = 1; i < NeedPresets.Length; i++)
                    if (Mathf.Abs(NeedPresets[i] - cur) < Mathf.Abs(NeedPresets[idx] - cur)) idx = i;
            }
            idx = Mathf.Clamp(idx + dir, 0, NeedPresets.Length - 1);
            HrContext.RecruitNeeded = NeedPresets[idx];
        }

        void ResetConditions()
        {
            _src = SrcAll;
            _posFilter = PosAll;
            _natFilter = FilterAny;
            _ageFilter = FilterAny;
            _sortMode = FilterAny;
            HrContext.RecruitUrgency = 1;
            HrContext.PresetPosition(HrContext.RecruitPosition);
            Refresh();
        }

        /// <summary>呈请 CEO 批复招聘（原 n99 的主操作，现在就在本页）。</summary>
        void SubmitRecruit()
        {
            int channel = CurrentChannel();
            if (channel < 0)
            {
                Toast("请先在上方「来源」选定招聘渠道（校招 / 猎头挖角 / 海外引进 / 内部转岗）。");
                return;
            }
            if (_posFilter == PosAll)
            {
                Toast("请在 ① 岗位里选定具体岗位（「不限」只用于查看名单，不能作为招聘目标）。");
                return;
            }
            var gs = GameState.Instance;
            if (gs == null) { Toast("游戏未就绪。"); return; }

            bool ok = InboxManager.OfferHrRecruit(gs, HrContext.RecruitPosition,
                Mathf.Max(1, HrContext.RecruitNeeded), HrContext.RecruitUrgency, channel, out string msg);
            Toast(msg);
            if (ok) _filterOpen = false;   // 呈请成功后收起，给列表腾出空间
            Refresh();
        }

        static float ShortageOf(HrState hr, string position)
        {
            if (hr?.talentIndex == null) return 30f;
            var t = hr.talentIndex;
            if (position.Contains("飞行")) return t.pilotShortage;
            if (position.Contains("机务")) return t.engineerShortage;
            if (position.Contains("乘务")) return t.cabinShortage;
            return 30f;
        }

        // ================= 列表与行（滚动，不翻页）=================

        /// <summary>
        /// 固定表头：画在 `_content` 上（**不**在滚动区内），故滚动时列名始终可见。
        ///
        /// 容器右侧按 <see cref="BarWidthPx"/> 内缩，使它的可用宽度与滚动视口**完全一致**
        /// —— 这是表头与数据行列能对齐的原因（见列表几何常量的注释）。
        /// </summary>
        void DrawListHeaders()
        {
            var hdr = UIFactory.Panel(_content, "ListHeader",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(ListXMax, 1f),
                    Vector2.zero, new Vector2(-BarWidthPx, 0f)),
                new Color(0, 0, 0, 0), rounded: false);
            var img = hdr.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;   // 表头只读：挡点击会让表头下方的行点不到
            var host = hdr.transform;

            // 列名走固定名字（H*）而不是按 x0 拼：表头是测试与视觉核对会去点的节点，
            // 名字不该随列宽一起变（原先叫 "C0.18" 这种，挪一次列宽测试就找不到）。
            Col(host, "HStatus", "现状", CStatus);
            Col(host, "HName", "姓名", CName);
            Col(host, "HNat", "国籍", CNat);
            Col(host, "HPreferred", "偏好工作", CPreferred);
            Col(host, "HSalary", "期望年薪", CSalary);
            Col(host, "HSource", "来源", CSource);
        }

        /// <summary>
        /// 候选人滚动列表。全部行一次性建出（几十行量级，无需虚拟化），
        /// 由 `ScrollRect` + 右常驻滚动条代替原「上一页 / 下一页」。
        /// </summary>
        void DrawList(List<Row> items)
        {
            var list = UIFactory.VerticalScrollArea(_content, "ListScroll",
                new UIFactory.Anchor(new Vector2(0f, ListYMin), new Vector2(ListXMax, ListYMax),
                    Vector2.zero, Vector2.zero));

            const float padTop = 4f;
            UIFactory.SetScrollContentHeight(list, padTop + items.Count * RowH + 8f);
            for (int i = 0; i < items.Count; i++)
                DrawRow(list, padTop + i * RowH, items[i], i);
        }

        void Col(Transform host, string name, string text, int col)
        {
            L(name, text, ColX[col], 0.728f, ColX[col + 1], 0.782f, 11, UITheme.TextMuted, true, host);
        }

        /// <summary>
        /// 一行单元格：滚动区内的行按「距顶像素」排布，列宽取 <see cref="ColX"/>（与表头同一张表，
        /// 故两者恒等 —— 这是表头与数据能对齐的原因）。
        /// </summary>
        void Cell(Transform host, string name, string text, float y, float h,
            int col, int size, Color color, bool bold = false)
        {
            var lab = UIFactory.Label(host, name, text, size, TextAlignmentOptions.MidlineLeft,
                UIFactory.Fill, color, bold ? FontStyles.Bold : FontStyles.Normal);
            UIFactory.PinFromTop(lab.gameObject, y, h, ColX[col], ColX[col + 1]);
        }

        /// <summary>
        /// 名单行。本页**只读**：右侧动作列（聘请 / 录用 / 退回 / 调岗）已整体搬进人员页
        /// （`n114`），这里只保留「点姓名 → 打开人员页」这一个入口（CEO 裁定 2026-10-10）。
        /// 列宽因此从 0.775 摊到 0.961 —— 撤掉一列后若不动宽度，右端会留一块空白。
        /// </summary>
        void DrawRow(Transform host, float y, Row r, int row)
        {
            var p = r.person;
            if (p == null) return;

            const float cellH = RowH - 6f;   // 行内内容高：留 6px 行距，避免相邻两行贴在一起
            float cy = y + 3f;

            // 现状 / 姓名 / 国籍 / 偏好工作 / 期望年薪 / 来源（列序见 ColX 的注释）。
            // 现状只区分「他是不是本公司的人」，这正是下一跳该走哪条流程的判据：
            // 在职 → 内部转岗；待聘 → 聘请 / 录用。弱色是因为它是一句状态，不是数据。
            Cell(host, "St" + row, r.status, cy, cellH, CStatus, 11, UITheme.TextMuted);

            string sid = p.id;
            var btn = UIFactory.Button(host, "N" + row, p.fullName, UIFactory.Fill,
                new Color(0, 0, 0, 0.01f), () =>
                {
                    HrContext.SelectedStaffId = sid;
                    HrContext.SelectedStaffIsExec = false;
                    HrPanelUI.OpenHrPage("n114_员工信息");
                }, 11);
            UIFactory.PinFromTop(btn.gameObject, cy, cellH, ColX[CName], ColX[CName + 1]);
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (lbl != null)
            {
                lbl.alignment = TextAlignmentOptions.MidlineLeft;
                lbl.color = UITheme.Accent;
            }

            Cell(host, "Nat" + row, p.nationality ?? "—", cy, cellH, CNat, 11, UITheme.TextMuted);
            Cell(host, "P" + row, r.preferred, cy, cellH, CPreferred, 11, UITheme.TextPrimary);
            Cell(host, "S" + row, r.salary, cy, cellH, CSalary, 11, UITheme.TextPrimary);
            Cell(host, "Src" + row, r.sourceName, cy, cellH, CSource, 11, r.sourceColor, true);
        }

        void Toast(string m)
        {
            if (_toast != null) _toast.text = m ?? "";
        }

        TextMeshProUGUI L(string name, string text, float x0, float y0, float x1, float y1,
            int size, Color color, bool bold = false, Transform host = null)
        {
            return UIFactory.Label(host != null ? host : _content, name, text, size,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color, bold ? FontStyles.Bold : FontStyles.Normal);
        }

        void B(string name, string text, float x0, float y0, float x1, float y1,
            Color color, UnityEngine.Events.UnityAction act, int size = 12, Transform host = null)
        {
            UIFactory.Button(host != null ? host : _content, name, text,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                color, act, size);
        }
    }
}
