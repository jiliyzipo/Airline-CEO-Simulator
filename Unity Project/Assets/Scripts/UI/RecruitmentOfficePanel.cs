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
    /// n112 职业介绍所：候选人名单 + 招聘发起，**同一页完成**。
    ///
    /// CEO 裁定 2026-10-08：原 n99「招聘策略弹窗」的全部功能并入本页，不再跳转；
    /// 并以「过滤器」形态呈现（参照 Football Manager 球探搜索的「过滤」下拉面板）——
    /// 分组标题 + 复选芯片 + 计数 + 底部动作。列表在左、过滤器覆盖在右。
    ///
    /// 条件折叠（CEO 裁定）：默认只显示待聘总监；校招/猎头/海外/内部转岗四个渠道的候选人
    /// 只有玩家主动点选对应来源筛选后才出现，且非总监行不提供 CEO 直聘，只能转人力办理。
    /// 这样既保护 CEO 注意力（列表不会变成招聘网站），又让「渠道」真正可被检视与比较。
    /// </summary>
    public class RecruitmentOfficePanel : BasePanel
    {
        // ---- 来源筛选（同时就是「招聘渠道」选择器）----
        const int TabDirector = 0;   // 待聘总监（默认，保持原有行为）
        const int TabCampus = 1;
        const int TabHeadhunt = 2;
        const int TabOverseas = 3;
        const int TabInternal = 4;
        const int TabCount = 5;

        static readonly string[] TabLabels = { "待聘总监", "校招", "猎头挖角", "海外引进", "内部转岗" };

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

        const int RowsPerPage = 8;

        // 会话内保持选择：面板会随导航重建，用 static 才不会每次跳回第一页
        static int _tab = TabDirector;
        static int _page;
        /// <summary>① 岗位：既筛左侧列表，也是招聘目标（不限 = 只看池子、不发起招聘）。</summary>
        static int _posFilter = PosAll;
        static int _natFilter = FilterAny;
        static int _ageFilter = FilterAny;
        static int _sortMode = FilterAny;
        /// <summary>招聘过滤面板是否展开。</summary>
        static bool _filterOpen;

        Transform _content;
        TextMeshProUGUI _toast;

        /// <summary>
        /// 外部入口（人力职能 hub / 培训页）请求展开招聘过滤面板。
        /// 只置标志位——随后 navigate 到本页会走 Show()/OnEnable() → Refresh() 读到它。
        /// </summary>
        public static void RequestOpenFilter() => _filterOpen = true;

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

            L("Hint", HintFor(_tab), 0.00f, 0.955f, 1.00f, 1.00f, 11,
                _tab == TabDirector ? UITheme.TextMuted : UITheme.Warning);

            if (gs != null) DrawKpiStrip(gs, mgr);
            DrawToolbar(rows.Total);

            Col("部门", 0.000f, 0.145f);
            Col("空缺职位", 0.145f, 0.300f);
            Col("姓名", 0.300f, 0.450f);
            Col("国籍", 0.450f, 0.565f);
            Col("期望年薪", 0.565f, 0.675f);
            Col("来源", 0.675f, 0.775f);

            if (rows.Total == 0)
                L("Empty", EmptyFor(_tab), 0.00f, 0.36f, 1.00f, 0.48f, 14, UITheme.Warning);
            else
            {
                // 先夹紧页号再画行，否则录用/退回使总数下降后会画出空白页
                int pages = Mathf.Max(1, Mathf.CeilToInt(rows.Total / (float)RowsPerPage));
                _page = Mathf.Clamp(_page, 0, pages - 1);

                int start = _page * RowsPerPage;
                for (int i = 0; i < RowsPerPage; i++)
                {
                    int idx = start + i;
                    if (idx >= rows.Items.Count) break;
                    DrawRow(i, rows.Items[idx], mgr);
                }
                DrawPager(rows.Total);
            }

            // 过滤器最后画 → 覆盖在列表之上（FM「过滤」下拉的层级关系）
            if (_filterOpen && gs != null) DrawFilterPanel(gs);
        }

        // ================= KPI 条（对标 FM 球探搜索顶部信息卡）=================

        void DrawKpiStrip(GameState gs, DepartmentManager mgr)
        {
            var hr = HrManager.EnsureState(gs);

            int listed = 0, channel = 0;
            if (gs.staffPool != null)
            {
                for (int i = 0; i < gs.staffPool.Count; i++)
                {
                    var p = gs.staffPool[i];
                    if (p == null || !p.onMarket) continue;
                    if (mgr != null && mgr.IsOccupied(p.id)) continue;
                    listed++;
                    if (HrEconomics.NormalizeSource(p.source) != HrEconomics.SourceMarket) channel++;
                }
            }

            int inFlight = 0;
            if (hr?.vacancies != null)
                for (int i = 0; i < hr.vacancies.Count; i++)
                    if (hr.vacancies[i] != null && hr.vacancies[i].hired < hr.vacancies[i].needed) inFlight++;

            int gap = 0;
            for (int i = 0; i < Positions.Length; i++)
                if (HrManager.RecruitRoomLeft(gs, Positions[i], out _, out _, out _, out int room) && room > 0)
                    gap += room;

            float vacancy = hr != null ? hr.vacancyRate : 0f;

            KpiCard(0, "介绍所待聘", $"{listed} 人（渠道 {channel}）", UITheme.TextPrimary);
            KpiCard(1, "在途招聘", $"{inFlight} 项", inFlight > 0 ? UITheme.Accent : UITheme.TextMuted);
            KpiCard(2, "运行岗缺口", $"{gap} 人", gap > 0 ? UITheme.Warning : UITheme.Success);
            KpiCard(3, "空缺率", $"{vacancy * 100f:F0}%", vacancy > 0.15f ? UITheme.Warning : UITheme.Success);
            KpiCard(4, "人力成本", $"{gs.LaborCostAnnualWan:F0} 万/年", UITheme.TextPrimary);
        }

        void KpiCard(int i, string title, string value, Color valueColor)
        {
            float x0 = 0.002f + i * 0.2f;
            var card = UIFactory.Panel(_content, "K" + i,
                new UIFactory.Anchor(new Vector2(x0, 0.872f), new Vector2(x0 + 0.196f, 0.948f),
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
            public string dept;        // 展示用部门（内部转岗为目标部门）
            public string role;        // 展示用职位
            public string salary;      // 展示用年薪文案
            public string sourceName;
            public Color sourceColor;
            public bool canAppoint;    // true = CEO 可直接任命/调岗
            public string transferDept;
            public string transferRole;
        }

        struct RowSet
        {
            public List<Row> Items;
            public int Total;
        }

        RowSet CollectRows(GameState gs, DepartmentManager mgr)
        {
            var set = new RowSet { Items = new List<Row>(), Total = 0 };
            if (gs?.staffPool == null) return set;

            if (_tab == TabInternal)
            {
                // 内部转岗按规则实时推导（不新增持久标志位，避免旧档状态漂移）
                var elig = HrManager.InternalCandidates(gs);
                for (int i = 0; i < elig.Count; i++)
                {
                    var p = elig[i];
                    if (!HrManager.TryFindTransferTarget(gs, p, out string td, out string tr)) continue;
                    if (!PassesScoutFilter(gs, p)) continue;
                    set.Items.Add(new Row
                    {
                        person = p,
                        dept = td,
                        role = tr + "（调入）",
                        salary = $"+{p.baseSalary * 0.2f:F1} 万",
                        sourceName = TagShortName(HrEconomics.SourceInternal),
                        sourceColor = TagColor(HrEconomics.SourceInternal),
                        canAppoint = true,
                        transferDept = td,
                        transferRole = tr
                    });
                }
                SortRows(set.Items);
                set.Total = set.Items.Count;
                return set;
            }

            // 待聘总监视图收总监（负责人）级；渠道视图收该渠道落池的任何职级。
            //
            // 此前两种视图都套了 CanCeoHire（rank == "负责人"）闸门，而渠道候选人由
            // HrManager.RankForPosition 一律产出为「专员」，于是校招/猎头/海外三个页签
            // 永远为空——渠道实体化整条链路在 UI 上等于没接上。判定口径现收敛到
            // StaffRecruitment.AcceptedInSourceView，并有 LogicSelfTests 兜底。
            bool directorView = _tab == TabDirector;
            var wantCat = _posFilter >= 0 && _posFilter < Positions.Length
                ? HrEconomics.OperationalCategory(Positions[_posFilter])
                : HrEconomics.OpsCategory.None;

            string want = TagOfTab(_tab);
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (!StaffRecruitment.AcceptedInSourceView(p, want)) continue;
                if (mgr != null && mgr.IsOccupied(p.id)) continue;

                // 岗位筛选只在渠道视图生效：待聘总监是管理岗（jobTitle = 总监），
                // 无运行岗类别，套用会把待聘总监列表直接清空。
                if (!directorView && wantCat != HrEconomics.OpsCategory.None
                    && HrEconomics.OperationalCategory(p.jobTitle) != wantCat) continue;
                if (!PassesScoutFilter(gs, p)) continue;

                set.Items.Add(new Row
                {
                    person = p,
                    dept = p.department,
                    role = StaffEstablishment.DisplayTitle(p.jobTitle, p.rank),
                    salary = p.baseSalary > 0 ? $"{p.baseSalary:F0} 万" : "—",
                    sourceName = TagShortName(want),
                    sourceColor = TagColor(want),
                    canAppoint = directorView
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

        static string TagOfTab(int tab) => tab switch
        {
            TabCampus => HrEconomics.SourceCampus,
            TabHeadhunt => HrEconomics.SourceHeadhunt,
            TabOverseas => HrEconomics.SourceOverseas,
            TabInternal => HrEconomics.SourceInternal,
            _ => HrEconomics.SourceMarket
        };

        /// <summary>页签 → 渠道号（0校招 1猎头 2海外 3内部）；待聘总监无渠道，返回 -1。</summary>
        static int ChannelForTab(int tab) => tab >= TabCampus && tab <= TabInternal ? tab - 1 : -1;

        static string TagShortName(string tag) => tag switch
        {
            HrEconomics.SourceCampus => "校招",
            HrEconomics.SourceHeadhunt => "猎头",
            HrEconomics.SourceOverseas => "海外",
            HrEconomics.SourceInternal => "内部转岗",
            _ => "市场"
        };

        static Color TagColor(string tag) => tag switch
        {
            HrEconomics.SourceCampus => UITheme.TextMuted,      // 24 周：慢而廉
            HrEconomics.SourceHeadhunt => UITheme.Danger,       // 4 周：快而贵
            HrEconomics.SourceOverseas => UITheme.Warning,      // 12 周
            HrEconomics.SourceInternal => UITheme.Success,      // 即时，但有编制代价
            _ => UITheme.TextPrimary
        };

        static string HintFor(int tab) => tab switch
        {
            TabDirector => "此处只列出总监候选人。您只负责总监的录用与解聘；其余岗位由人力招聘后呈请审批。",
            TabCampus => "批量校招候选人（周期最长、成本最低、潜力偏低但忠诚度高）。点选姓名可查看档案。",
            TabHeadhunt => "猎头挖角候选人（到岗最快、薪资期望最高、忠诚度偏低）。点选姓名可查看档案。",
            TabOverseas => "海外引进候选人（稀缺岗位来源，薪资偏高并有适应期）。点选姓名可查看档案。",
            _ => "内部转岗：由在编员工调入缺口岗位。不花外部招聘费，但原部门会留下编制缺口（人力将重新招聘），且有 14 天交接期打折。"
        };

        static string EmptyFor(int tab) => tab switch
        {
            TabDirector => "当前没有待聘候选人。",
            TabCampus => "暂无校招候选人。点右侧「过滤 · 招聘条件」发起批量校招，候选人将按周期到位。",
            TabHeadhunt => "暂无猎头候选人。点右侧「过滤 · 招聘条件」发起猎头挖角（约 4 周到位）。",
            TabOverseas => "暂无海外候选人。点右侧「过滤 · 招聘条件」发起海外引进（约 12 周到位）。",
            _ => "暂无可调岗员工（需在编、非总监、未占管理岗，且存在同职级空缺）。"
        };

        // ================= 工具栏：来源页签 + 计数 + 过滤开关 =================

        void DrawToolbar(int total)
        {
            const float w = 0.125f;
            for (int i = 0; i < TabCount; i++)
            {
                int idx = i;
                bool sel = _tab == i;
                float x0 = i * w;
                UIFactory.Button(_content, "Tab" + i, TabLabels[i],
                    new UIFactory.Anchor(new Vector2(x0 + 0.002f, 0.790f), new Vector2(x0 + w - 0.002f, 0.862f),
                        Vector2.zero, Vector2.zero),
                    sel ? UITheme.NavActive : UITheme.NavIdle, () => SelectTab(idx), 12);
            }

            string cnt = $"{TabLabels[_tab]} · {total} 人";
            if (_posFilter != PosAll && _tab != TabDirector) cnt += $"（岗位：{Positions[_posFilter]}）";
            L("Cnt", cnt, 0.628f, 0.790f, 0.775f, 0.862f, 11, UITheme.TextMuted);

            // 过滤开关：面板展开时高亮；有任一筛选在生效时带 ● 提示
            bool active = _posFilter != PosAll || _natFilter != FilterAny
                || _ageFilter != FilterAny || _sortMode != FilterAny;
            B("Filter", _filterOpen ? "收起过滤 ✕" : (active ? "过滤 ● ▾" : "过滤 · 招聘条件 ▾"),
                0.780f, 0.786f, 1.000f, 0.866f,
                _filterOpen || active ? UITheme.Accent : UITheme.PanelDeep,
                () => { _filterOpen = !_filterOpen; Refresh(); }, 12);
        }

        void SelectTab(int tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            _page = 0;
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

            // 点面板以外的任意处 = 收起（FM 下拉的行为）
            UIFactory.Button(_content, "Scrim", "",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(0.573f, 1f), Vector2.zero, Vector2.zero),
                new Color(0f, 0f, 0f, 0.28f),
                () => { _filterOpen = false; Refresh(); }, 1);
            host.SetAsLastSibling();

            int channel = ChannelForTab(_tab);

            L("FH", "过滤 · 招聘条件", 0.04f, 0.943f, 0.78f, 0.992f, 14, UITheme.Accent, true, host);
            L("FSub", $"渠道：{(channel < 0 ? "未选（待聘总监）" : HrEconomics.ChannelName(channel))}　·　切换请点上方来源页签",
                0.04f, 0.897f, 0.80f, 0.929f, 10, UITheme.TextMuted, false, host);
            B("FClose", "✕", 0.86f, 0.936f, 0.96f, 0.992f, UITheme.PanelDeep,
                () => { _filterOpen = false; Refresh(); }, 14, host);

            // ① 岗位：唯一岗位选择器 —— 既筛左侧列表，也是本次招聘的目标岗位。
            // （此前 ① 与 ② 各放了一排同样的三个岗位，重复且互相矛盾，已合并。）
            L("F1H", "① 岗位（筛列表 + 定招聘目标）", 0.04f, 0.846f, 0.96f, 0.886f, 12, UITheme.Accent, true, host);
            ChipRow(host, "Pos", null, PosChoices, _posFilter + 1, 0.786f, 0.04f, 0.96f, i =>
            {
                _posFilter = i - 1;
                // 选定具体岗位时同步招聘目标（省一次点击）；「不限」只筛看不改招聘条件
                if (i > 0) HrContext.PresetPosition(Positions[i - 1]);
                _page = 0;
                Refresh();
            });

            // ② 候选人筛选（FM 球探搜索的筛选条件；只影响左侧列表）
            L("F2H", "② 候选人筛选（只影响名单）", 0.04f, 0.730f, 0.96f, 0.770f, 12, UITheme.Accent, true, host);
            ChipRow(host, "Nat", "国籍", NatChoices, _natFilter + 1, 0.670f, 0.04f, 0.96f, i =>
            { _natFilter = i - 1; _page = 0; Refresh(); });
            ChipRow(host, "Age", "年龄", AgeChoices, _ageFilter + 1, 0.612f, 0.04f, 0.96f, i =>
            { _ageFilter = i - 1; _page = 0; Refresh(); });
            ChipRow(host, "Sort", "排序", SortChoices, _sortMode + 1, 0.554f, 0.04f, 0.96f, i =>
            { _sortMode = i - 1; _page = 0; Refresh(); });

            // ③ 招聘条件（原 n99 的紧迫度与需求人数；岗位已在 ① 选定）
            L("F3H", "③ 招聘条件", 0.04f, 0.498f, 0.96f, 0.538f, 12, UITheme.Accent, true, host);
            L("UrgL", "紧迫度", 0.04f, 0.438f, 0.15f, 0.488f, 11, UITheme.TextMuted, false, host);
            for (int i = 0; i < UrgLabels.Length; i++)
            {
                int idx = i;
                bool sel = HrContext.RecruitUrgency == i;
                float x0 = 0.16f + i * 0.14f;
                B("Urg" + i, UrgLabels[i], x0, 0.438f, x0 + 0.13f, 0.488f,
                    sel ? UrgColors[i] : UITheme.PanelDeep,
                    () => { HrContext.RecruitUrgency = idx; Refresh(); }, 11, host);
            }
            // 「需求人数」此前没有行首标签，只有孤零零的「− 12 人 +」，无从得知在调什么。
            L("NeedL", "需求人数", 0.62f, 0.438f, 0.75f, 0.488f, 11, UITheme.TextMuted, false, host);
            B("NeedM", "−", 0.75f, 0.438f, 0.81f, 0.488f, UITheme.PanelDeep, () => StepNeed(-1), 14, host);
            L("NeedV", $"{HrContext.RecruitNeeded} 人", 0.81f, 0.438f, 0.90f, 0.488f, 12,
                UITheme.TextPrimary, true, host);
            B("NeedP", "＋", 0.90f, 0.438f, 0.96f, 0.488f, UITheme.PanelDeep, () => StepNeed(1), 14, host);

            // ④ 编制与成本预估（「确认到岗」随在途管道同排 —— 它是该管道的收尾动作）
            L("F4H", "④ 编制与成本预估", 0.04f, 0.382f, 0.96f, 0.422f, 12, UITheme.Accent, true, host);

            bool posChosen = _posFilter != PosAll;
            if (!posChosen)
            {
                L("Cap", "未选定招聘目标 —— 请先在 ① 选定具体岗位（「不限」只用于查看名单）",
                    0.04f, 0.330f, 0.96f, 0.372f, 11, UITheme.Warning, false, host);
            }
            else if (HrManager.RecruitRoomLeft(gs, HrContext.RecruitPosition, out int est, out int onDuty,
                    out int inFlight, out int room))
            {
                L("Cap", $"招聘目标「{HrContext.RecruitPosition}」· 编制 {est} 人 · 在职 {onDuty} · 在途 {inFlight} · 缺口 {room} 人",
                    0.04f, 0.330f, 0.96f, 0.372f, 11,
                    room > 0 ? UITheme.TextPrimary : UITheme.Warning, false, host);
            }
            else
            {
                L("Cap", $"招聘目标「{HrContext.RecruitPosition}」· 无在役机队 → 编制按 0 计（扩机队后自动上调）",
                    0.04f, 0.330f, 0.96f, 0.372f, 11, UITheme.Warning, false, host);
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
                0.04f, 0.272f, 0.74f, 0.316f, 10,
                canArrive ? UITheme.TextPrimary : UITheme.TextMuted, false, host);
            B("Arrive", "确认到岗", 0.76f, 0.268f, 0.96f, 0.318f,
                canArrive ? UITheme.Accent : UITheme.PanelDeep, ConfirmArrival, 11, host);

            if (!posChosen)
            {
                L("Cost", "成本预估随岗位显示：单价 · 合计 · 到岗周期 · 成功率",
                    0.04f, 0.214f, 0.96f, 0.256f, 11, UITheme.TextMuted, false, host);
                L("Mkt", "选定岗位后即可呈请招聘。",
                    0.04f, 0.162f, 0.96f, 0.204f, 11, UITheme.TextMuted, false, host);
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
                L("Cost", costLine, 0.04f, 0.214f, 0.96f, 0.256f, 11, UITheme.TextPrimary, false, host);
                L("Mkt", $"人才紧缺指数 {shortage:F0} · 成本系数 ×{HrEconomics.ShortageCostFactor(shortage):F2}",
                    0.04f, 0.162f, 0.96f, 0.204f, 11, UITheme.TextMuted, false, host);
            }
            else
            {
                L("Cost", "待聘总监不是招聘渠道 → 请先在上方来源页签选一个渠道",
                    0.04f, 0.214f, 0.96f, 0.256f, 11, UITheme.Warning, false, host);
                L("Mkt", "总监席由 CEO 直接在名单里任命，不产生招聘费。",
                    0.04f, 0.162f, 0.96f, 0.204f, 11, UITheme.TextMuted, false, host);
            }

            // 分隔线：以上是「筛选条件 + 成本预估」，以下是「动作」，避免两者视觉糊在一起。
            var div = UIFactory.Panel(host, "FDiv",
                new UIFactory.Anchor(new Vector2(0.04f, 0.144f), new Vector2(0.96f, 0.146f), Vector2.zero, Vector2.zero),
                UITheme.Border, rounded: false);
            var divImg = div.GetComponent<Image>();
            if (divImg != null) divImg.raycastTarget = false;   // 纯装饰，不吃点击

            // 动作：主操作只剩「呈请」；「重置条件」降为次要操作置于右端，用描边色与禁用的深底区分。
            // 「暂缓」已删除 —— 它与 ✕ / 点面板外 / 「收起过滤」三者完全等效（都只做 _filterOpen=false ；
            // 条件并不存在面板里，而是存 HrContext 的 static 字段，本就不会丢）。它既多占一个主操作位，
            // 又在未呈请时暗示「有草稿待存」，属纯冗余。
            bool canSubmit = channel >= 0 && posChosen;
            B("Submit", "呈请 CEO 批复招聘", 0.04f, 0.086f, 0.66f, 0.140f,
                canSubmit ? UITheme.Success : UITheme.PanelDeep, SubmitRecruit, 13, host);
            B("Reset", "重置条件", 0.68f, 0.086f, 0.96f, 0.140f, UITheme.Border,
                ResetConditions, 11, host);

            L("FTip", canSubmit
                    ? "本页即招聘发起处。呈请后候选人按渠道周期落池，届时在左侧名单逐人处置；已落池的走「确认到岗」。"
                    : channel < 0
                        ? "要发起招聘：先点上方「校招 / 猎头挖角 / 海外引进 / 内部转岗」任一页签，再在 ① 选定岗位。"
                        : "要发起招聘：请在 ① 选定具体岗位（「不限」只用于查看名单，不能作为招聘目标）。",
                0.04f, 0.012f, 0.96f, 0.072f, 10,
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
            const float h = 0.050f;
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
            _posFilter = PosAll;
            _natFilter = FilterAny;
            _ageFilter = FilterAny;
            _sortMode = FilterAny;
            HrContext.RecruitUrgency = 1;
            HrContext.PresetPosition(HrContext.RecruitPosition);
            _page = 0;
            Refresh();
        }

        /// <summary>呈请 CEO 批复招聘（原 n99 的主操作，现在就在本页）。</summary>
        void SubmitRecruit()
        {
            int channel = ChannelForTab(_tab);
            if (channel < 0)
            {
                Toast("请先选择招聘渠道：点上方「校招 / 猎头挖角 / 海外引进 / 内部转岗」任一页签。");
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

        // ================= 列表与行 =================

        void DrawPager(int total)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(total / (float)RowsPerPage));
            if (_page >= pages) _page = pages - 1;
            if (pages <= 1)
            {
                L("Pg", $"共 {total} 人", 0.00f, 0.015f, 0.60f, 0.082f, 11, UITheme.TextMuted);
                return;
            }

            B("Prev", "◀ 上一页", 0.00f, 0.015f, 0.12f, 0.082f,
                _page > 0 ? UITheme.PanelDeep : UITheme.NavIdle,
                () => { if (_page > 0) { _page--; Refresh(); } }, 11);
            L("Pg", $"第 {_page + 1} / {pages} 页 · 共 {total} 人", 0.13f, 0.015f, 0.62f, 0.082f, 11, UITheme.TextMuted);
            B("Next", "下一页 ▶", 0.63f, 0.015f, 0.75f, 0.082f,
                _page < pages - 1 ? UITheme.PanelDeep : UITheme.NavIdle,
                () => { if (_page < pages - 1) { _page++; Refresh(); } }, 11);
            B("ToHr", "人力职能 →", 0.76f, 0.015f, 0.90f, 0.082f, UITheme.PanelDeep,
                () => HrPanelUI.OpenHrPage("n86_组织规划与招聘"), 11);
        }

        void Col(string text, float x0, float x1)
        {
            L("C" + x0, text, x0, 0.728f, x1, 0.782f, 11, UITheme.TextMuted, true);
        }

        void DrawRow(int row, Row r, DepartmentManager mgr)
        {
            float y1 = 0.715f - row * 0.0765f;
            float y0 = y1 - 0.070f;
            var p = r.person;
            if (p == null) return;

            L("D" + row, DepartmentCatalog.DisplayName(r.dept), 0.000f, y0, 0.145f, y1, 11, UITheme.TextPrimary);
            L("R" + row, r.role, 0.145f, y0, 0.300f, y1, 11, UITheme.TextPrimary);

            string sid = p.id;
            var btn = UIFactory.Button(_content, "N" + row, p.fullName,
                new UIFactory.Anchor(new Vector2(0.300f, y0), new Vector2(0.450f, y1), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0.01f), () =>
                {
                    HrContext.SelectedStaffId = sid;
                    HrContext.SelectedStaffIsExec = false;
                    HrPanelUI.OpenHrPage("n114_员工信息");
                }, 11);
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (lbl != null)
            {
                lbl.alignment = TextAlignmentOptions.MidlineLeft;
                lbl.color = UITheme.Accent;
            }

            L("Nat" + row, p.nationality ?? "—", 0.450f, y0, 0.565f, y1, 11, UITheme.TextMuted);
            L("S" + row, r.salary, 0.565f, y0, 0.675f, y1, 11, UITheme.TextPrimary);
            L("Src" + row, r.sourceName, 0.675f, y0, 0.775f, y1, 11, r.sourceColor, true);

            if (_tab == TabInternal)
            {
                string td = r.transferDept, tr = r.transferRole;
                B("Go" + row, "调岗", 0.820f, y0, 1.000f, y1, UITheme.Success,
                    () => DoTransfer(sid, td, tr), 11);
                return;
            }

            if (r.canAppoint)
            {
                // 总监：CEO 直接任命（行为与改动前一致）
                B("H" + row, "聘请", 0.820f, y0, 1.000f, y1, UITheme.Accent,
                    () => DoAppoint(mgr, sid, p.fullName), 11);
                return;
            }

            // 渠道候选人：CEO 只确认人选，由人力办理入职；不提供管理岗直聘
            B("H" + row, "录用", 0.780f, y0, 0.888f, y1, UITheme.Accent,
                () => DoHireCandidate(sid), 11);
            B("X" + row, "退回", 0.892f, y0, 1.000f, y1, UITheme.PanelDeep,
                () => DoRejectCandidate(sid), 11);
        }

        void DoAppoint(DepartmentManager mgr, string id, string name)
        {
            if (mgr == null) return;
            if (mgr.HireFromMarket(id)) Toast("已任命总监 " + name);
            else Toast("无法任命：该部门总监席可能已有人。");
            Refresh();
        }

        void DoHireCandidate(string id)
        {
            string msg = "录用失败";
            if (HrManager.Instance != null) HrManager.Instance.HireCandidate(id, out msg);
            else msg = "HrManager 未就绪。";
            Toast(msg);
            Refresh();
        }

        void DoRejectCandidate(string id)
        {
            string msg = "退回失败";
            if (HrManager.Instance != null) HrManager.Instance.RejectCandidate(id, out msg);
            else msg = "HrManager 未就绪。";
            Toast(msg);
            Refresh();
        }

        void DoTransfer(string id, string toDept, string toRole)
        {
            string msg = "调岗失败";
            if (HrManager.Instance != null) HrManager.Instance.TransferStaff(id, toDept, toRole, out msg);
            else msg = "HrManager 未就绪。";
            Toast(msg);
            Refresh();
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
