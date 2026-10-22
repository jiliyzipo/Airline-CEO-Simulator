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
    /// n42 周计划 / n43 详细 / n66 航季 / n36 构建航班环。
    /// Prefab 无法持久化 C# lambda，Awake/OnEnable 重绑；计划页用表格，环页用交互构建器。
    /// </summary>
    public class OpsSubpagePanel : BasePanel
    {
        static readonly float[] WeekWeights = { 1.2f, 0.9f, 0.8f, 0.45f, 0.8f, 0.95f, 0.95f, 0.7f };
        static readonly string[] WeekHeaders = { "航线", "航班号", "机型", "周班", "班期", "去程", "回程", "日利(万)" };
        static readonly float[] SeasonWeights = { 1.2f, 2.8f };
        static readonly float[] DetailWeights = { 0.9f, 2.1f, 0.9f, 2.1f };
        /// <summary>单班页紧凑行高：9 行 + 标题 + 脚注要在一屏内放完（720p 也不出滚动条）。</summary>
        const float DetailRowHeight = 38f;

        [SerializeField] TextMeshProUGUI bodyLabel;
        [SerializeField] string pageKind;

        TextMeshProUGUI _body;
        string _pageKind;
        Transform _tableContent;
        Transform _bodyViewport;

        public static OpsSubpagePanel Build(string pageKind, string title)
        {
            var root = UIFactory.Panel(null, "OpsSub_" + pageKind, UIFactory.Fill,
                new Color(0.02f, 0.05f, 0.1f, 0.72f), rounded: false);
            var card = UIFactory.Panel(root.transform, "Card",
                new UIFactory.Anchor(new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.Panel);
            UIFactory.AddBorder(card, UITheme.Border, new Vector2(1.2f, -1.2f));
            var cg = card.AddComponent<CanvasGroup>();

            var titleLabel = UIFactory.Label(card.transform, "Title", title, 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.9f), new Vector2(0.75f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(card.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.9f, 0.9f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);

            var body = UIFactory.ScrollableLabel(card.transform, "Body",
                new UIFactory.Anchor(new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, 15);

            UIFactory.Button(card.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.04f, 0.04f), new Vector2(0.22f, 0.12f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            string kind = NormalizeKind(pageKind);
            if (kind == "n66")
            {
                UIFactory.Button(card.transform, "ToWeek", "生成周总计划",
                    new UIFactory.Anchor(new Vector2(0.26f, 0.04f), new Vector2(0.5f, 0.12f), Vector2.zero, Vector2.zero),
                    UITheme.Accent, () => { }, 13);
            }

            var panel = root.AddComponent<OpsSubpagePanel>();
            panel.pageKind = kind;
            panel.bodyLabel = body;
            panel._pageKind = kind;
            panel._body = body;
            panel.AssignRefs(root.GetComponent<RectTransform>(), titleLabel, close, cg);
            panel.BindPage(pageKind, title);
            panel.RefreshBody();
            return panel;
        }

        protected override void Awake()
        {
            if (root == null) root = transform as RectTransform;
            ResolveRefs();
            WireButtons();
        }

        void OnEnable()
        {
            ResolveRefs();
            WireButtons();
            RefreshBody();
        }

        public override void BindPage(string pageId, string title = null)
        {
            string kind = NormalizeKind(pageId);
            if (kind == "n36" && string.IsNullOrEmpty(title))
                title = "构建航班环";
            if (kind == "n43" && string.IsNullOrEmpty(title))
                title = "航班飞行计划";
            if (kind == "n98" && string.IsNullOrEmpty(title))
                title = "航线/航班环选择器";
            base.BindPage(pageId, title);
            _pageKind = kind;
            pageKind = _pageKind;
            ApplyPanelTitle(null, null, -1);
        }

        public override void Show()
        {
            base.Show();
            ResolveRefs();
            WireButtons();
            RefreshBody();
        }

        void ResolveRefs()
        {
            if (_body == null) _body = bodyLabel;
            if (_body == null)
            {
                var content = transform.Find("Card/Body_Viewport/Body_Content");
                if (content != null) _body = content.GetComponent<TextMeshProUGUI>();
            }

            _bodyViewport = transform.Find("Card/Body_Viewport");

            if (string.IsNullOrEmpty(_pageKind))
                _pageKind = NormalizeKind(!string.IsNullOrEmpty(pageKind) ? pageKind : PageId);
            if (string.IsNullOrEmpty(_pageKind))
                _pageKind = NormalizeKind(gameObject.name);
        }

        void WireButtons()
        {
            var close = FindBtn("Close") ?? closeButton;
            if (close != null)
            {
                close.onClick.RemoveAllListeners();
                close.onClick.AddListener(CloseOverlay);
                closeButton = close;
            }

            BindBtn("ToWeek", () => OpsPanelUI.OpenOpsPage("n42_航班周总计划"));

            var backBtn = FindBtn("Back");
            if (backBtn != null) backBtn.gameObject.SetActive(false);

            // 旧 Prefab：隐藏已废弃按钮
            var oldValidate = FindBtn("ValidateRing");
            if (oldValidate != null) oldValidate.gameObject.SetActive(false);
            var oldOpenDetail = FindBtn("OpenDetail");
            if (oldOpenDetail != null) oldOpenDetail.gameObject.SetActive(false);
        }

        void BindBtn(string childName, UnityEngine.Events.UnityAction action)
        {
            var btn = FindBtn(childName);
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
        }

        Button FindBtn(string childName)
        {
            var t = transform.Find("Card/" + childName);
            if (t == null)
            {
                var card = transform.Find("Card");
                if (card != null)
                {
                    for (int i = 0; i < card.childCount; i++)
                    {
                        var c = card.GetChild(i);
                        if (c.name == childName) { t = c; break; }
                    }
                }
            }
            return t != null ? t.GetComponent<Button>() : null;
        }

        void OpenFlightPlan(string routeId, int flightIndex)
        {
            OpsSubpageContext.SelectFlight(routeId, flightIndex);
            OpsPanelUI.OpenOpsPage("n43_航班详细计划");
        }

        void ApplyPanelTitle(RouteData selected, RouteFlight flight, int flightIndex)
        {
            if (titleLabel == null) return;
            switch (_pageKind)
            {
                case "n36":
                    titleLabel.text = "构建航班环";
                    break;
                case "n98":
                    titleLabel.text = "航线/航班环选择器";
                    break;
                case "n42":
                    titleLabel.text = "航班周总计划";
                    break;
                case "n66":
                    titleLabel.text = "航季总纲";
                    break;
                case "n43":
                    // 标题只写这一个班次自己的去程号 —— 旧版写的是航线首班的号，
                    // 于是「NS108 航班飞行计划」下面却是整条线的数据。
                    if (selected != null && flight != null)
                    {
                        FlightScheduleFormat.Numbers(selected, flightIndex < 0 ? 0 : flightIndex,
                            out string noA, out _);
                        titleLabel.text = $"{noA} 航班飞行计划";
                    }
                    else
                    {
                        titleLabel.text = "航班飞行计划";
                    }
                    break;
            }
        }

        public void RefreshBody()
        {
            ResolveRefs();
            var gs = GameState.Instance;
            var card = transform.Find("Card");

            if (_pageKind == "n36")
            {
                SetBodyVisible(false);
                SetTableVisible(false);
                HideSelector();
                if (card != null) OpsRingBuilderUI.Ensure(card);
                ApplyPanelTitle(null, null, -1);
                return;
            }

            if (_pageKind == "n98")
            {
                SetBodyVisible(false);
                SetTableVisible(false);
                var ring = GetComponentInChildren<OpsRingBuilderUI>(true);
                if (ring != null) ring.gameObject.SetActive(false);
                if (card != null) OpsRouteRingSelectorUI.Ensure(card);
                ApplyPanelTitle(null, null, -1);
                return;
            }

            var ringUi = GetComponentInChildren<OpsRingBuilderUI>(true);
            if (ringUi != null) ringUi.gameObject.SetActive(false);
            HideSelector();

            if (_pageKind == "n42" || _pageKind == "n43" || _pageKind == "n66")
            {
                SetBodyVisible(false);
                EnsureTableHost();
                SetTableVisible(true);
                FillPlanTable(gs);
                return;
            }

            SetTableVisible(false);
            SetBodyVisible(true);
            if (_body != null)
            {
                _body.lineSpacing = 10f;
                _body.text = "OPS 子页：" + (_pageKind ?? "?");
            }
        }

        void SetBodyVisible(bool on)
        {
            if (_bodyViewport != null) _bodyViewport.gameObject.SetActive(on);
            else if (_body != null) _body.gameObject.SetActive(on);
        }

        void SetTableVisible(bool on)
        {
            var host = transform.Find("Card/PlanTable");
            if (host != null) host.gameObject.SetActive(on);
        }

        void EnsureTableHost()
        {
            var card = transform.Find("Card");
            if (card == null) return;
            var existing = card.Find("PlanTable");
            if (existing != null)
            {
                var scroll = existing.GetComponent<ScrollRect>();
                _tableContent = scroll != null ? scroll.content : existing.Find("TableContent");
                return;
            }

            _tableContent = UiTable.CreateHost(card,
                "PlanTable",
                // 下沿从 0.18 放到 0.06：计划页底部只有一条「← 返回」（实际已被 WireButtons 隐藏），
                // 旧锚点白留 12% 高度，加上表体 40px 行距，单班页在 720p 下就必须滚动。
                new UIFactory.Anchor(new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero));
        }

        void FillPlanTable(GameState gs)
        {
            if (_tableContent == null) return;
            UiTable.Clear(_tableContent);

            switch (_pageKind)
            {
                case "n42":
                    FillWeekTable(gs);
                    break;
                case "n43":
                    FillDetailTable(gs);
                    break;
                case "n66":
                    FillSeasonTable(gs);
                    break;
            }
        }

        /// <summary>
        /// 航班周总计划：**一个班次一行**。
        ///
        /// 旧版一条航线只印一行，且时刻/航班号都取 <c>flights[0]</c>：
        /// 一天 3 波的航线（如 ICN 21 班/周）在表里只露出第一波的 08:00-11:10 和第一个号，
        /// 读起来就是「每天一班」—— 页面上看不出还有第二、第三波，等于没列。
        /// 故按班次展开：每班各自的航班号、班期、周班、往返窗口、日利。
        ///
        /// 日利用 <see cref="RouteData.FlightNetDailyProfit"/>（净额均摊，Σ 各行 == 航线日利），
        /// 与航线总览卡片、单航线页的「日利」同口径；直接用班次的毛贡献份额会让整列偏大一截。
        /// </summary>
        void FillWeekTable(GameState gs)
        {
            var routes = gs?.activeRoutes;
            int n = routes?.Count ?? 0;
            ApplyPanelTitle(null, null, -1);

            int waves = 0;
            if (routes != null)
            {
                for (int i = 0; i < n; i++)
                {
                    if (routes[i] == null) continue;
                    routes[i].EnsureFlights();
                    waves += routes[i].FlightCount;
                }
            }

            UiTable.AddCaption(_tableContent,
                $"航季 {(n > 0 ? routes[0].season ?? "夏秋" : "夏秋")} · 共 {n} 条航线 / {waves} 个班次");
            UiTable.AddHeader(_tableContent, WeekHeaders, WeekWeights);

            if (n == 0)
            {
                UiTable.AddNote(_tableContent, "暂无航班。请先在运营部点「开通航线」。");
                return;
            }

            for (int ri = 0; ri < n; ri++)
            {
                var r = routes[ri];
                if (r == null) continue;
                r.EnsureFlights();
                int count = r.flights != null ? r.flights.Count : 0;
                string daily = r.FlightNetDailyProfit.ToString("F2");
                for (int fi = 0; fi < count; fi++)
                {
                    var f = r.flights[fi];
                    if (f == null) continue;
                    string routeId = r.routeId;
                    int index = fi;
                    FlightScheduleFormat.Numbers(r, fi, out string noA, out string noB);
                    // 去程 / 回程**分开印**：旧版只印 `WindowLabel`（一次往返的总占窗，08:00-11:26），
                    // 被读成单程起降时刻，CEO 当场问「返程时间呢」。两列各自给出起降，跨日标 +1。
                    FlightScheduleFormat.LegClock(r, f, out string outbound, out string inbound);
                    int weekly = FlightScheduleFormat.WeeklyOf(f);
                    UiTable.AddRow(_tableContent, new[]
                    {
                        // 一班 = 去程 + 过站 + 回程，航线是双向的，故用 ⇄ 而不是单向 →
                        AirportCatalog.OdRoundTrip(r.origin, r.destination),
                        $"{noA}/{noB}",
                        AircraftTypeOf(r, f),
                        weekly.ToString(),
                        FlightScheduleFormat.DaysSummary(f.daysOfWeek),
                        outbound,
                        inbound,
                        daily
                    }, WeekWeights, header: false, onClick: () => OpenFlightPlan(routeId, index));
                }
            }

            UiTable.AddNote(_tableContent,
                "同一航线有多个班次时连续排列；「去程 / 回程」各是一次航段的起降时刻，跨日标 +1。"
                + "「日利」为扣航线固定成本后的净额，同一航线各行之和 = 该航线日利。点任一行查看该班次的飞行计划。");
            UiTable.FillVertically(_tableContent);
        }

        /// <summary>
        /// 该班次的机型：班次只记机号，机型要回机队查（航班环可以给同一航线的不同波次派不同机型）。
        /// 查不到（退役转售 / 未指派）时退回航线机型，但**无机可派**时不得报机型 ——
        /// 否则会出现「运力不足 · 待复航 A320neo」这种自相矛盾的行（见 <see cref="RouteData.AircraftLabel"/>）。
        /// </summary>
        static string AircraftTypeOf(RouteData r, RouteFlight f)
        {
            if (f == null || string.IsNullOrEmpty(f.registration)) return RouteData.UnassignedAircraftLabel;
            var fm = FleetManager.Instance;
            var ac = fm != null ? fm.GetAircraftByRegistration(f.registration) : null;
            if (ac != null && !string.IsNullOrEmpty(ac.type)) return ac.type;
            return string.IsNullOrEmpty(r.aircraftType) ? "—" : r.aircraftType;
        }


        /// <summary>
        /// 航班飞行计划（n43）：**一个班次一页**。
        ///
        /// 旧版虽然标题写着「NS100 航班飞行计划」，取的却是**航线级**字段（r.daysOfWeek / r.weeklyFlights /
        /// r.dailyProfit / flights[0] 的时刻），多波次航线于是出现「NS108 航班飞行计划」下面写
        /// 「每日 · 21 班/周」这种把航线挂在单个班次号上的错位。
        /// 现在班次由 <see cref="OpsSubpageContext.SelectedFlightIndex"/> 指定：标题、班期、周班、
        /// 时刻、机号、槽位、日利全部取该班次自己的值，航线级的只留距离/机型基准/开通作背景。
        /// </summary>
        void FillDetailTable(GameState gs)
        {
            RouteData r = null;
            if (gs?.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    if (gs.activeRoutes[i]?.routeId == OpsSubpageContext.SelectedRouteId)
                    {
                        r = gs.activeRoutes[i];
                        break;
                    }
                }
                if (r == null && gs.activeRoutes.Count > 0) r = gs.activeRoutes[0];
            }

            if (r == null)
            {
                ApplyPanelTitle(null, null, -1);
                UiTable.AddNote(_tableContent, "未选中航班。请先开通航线，再从周计划点选班次。");
                return;
            }

            r.EnsureFlights();
            int count = r.flights != null ? r.flights.Count : 0;
            // 下标越界一律钳到第 0 班（换航线/加密后旧下标可能失效），并在标题里如实标出第几班。
            int index = OpsSubpageContext.FlightIndexOf(r.routeId);
            if (index < 0 || index >= count) index = 0;
            RouteFlight f = count > 0 ? r.flights[index] : null;
            if (f == null)
            {
                ApplyPanelTitle(r, null, -1);
                UiTable.AddNote(_tableContent, "该航线暂无班次。请返回周计划或单航线页下达运力指令。");
                return;
            }

            ApplyPanelTitle(r, f, index);
            FlightScheduleFormat.Numbers(r, index, out string noA, out string noB);

            var m = OpsEconomics.Evaluate(r.distanceKm, FlightScheduleFormat.WeeklyOf(f), AircraftTypeOf(r, f),
                r.groundStaffAssigned, gs?.onTimePercent ?? 88f);
            var trip = FlightScheduleFormat.Trip(r, f);
            FlightScheduleFormat.LegClock(r, f, out string outbound, out string inbound);
            string reg = string.IsNullOrEmpty(f.registration) ? RouteData.UnassignedAircraftLabel : f.registration;
            string slot = string.IsNullOrEmpty(f.slotLabel) ? "—" : f.slotLabel;
            string occ = RoutePermitLogic.SlotStatusOf(gs, r, index);
            string type = AircraftTypeOf(r, f);

            // ———— 版面：一屏看完，不滚动 ————
            // 旧版 18 行「字段 / 数值」，到 1080p 也只能露出 13 行，顶部「航线 / 航班号 / 班次」
            // 直接被挤出可视区（CEO 截图里红框以上是空的，实为已滚掉）。
            // 现在：航线身份（城对 / 航班号 / 航季 / 开通）提到标题行，体例改成 4 列 × 9 行，
            // 一行两组「字段 – 数值」。行高压到 34 px、表体下沿放到 0.06，总高约
            // 9×34 + 32 + 44 + 行距 ≈ 400 px —— 720p 的可视区也能一次放完，不再出滚动条。
            UiTable.AddCaption(_tableContent,
                (string.IsNullOrEmpty(r.stopover)
                    ? AirportCatalog.OdRoundTrip(r.origin, r.destination)
                    : AirportCatalog.OdRoundTrip(r.origin, r.destination, r.stopover))
                + $"　·　{noA}/{noB}　·　{r.season}季　·　开通 {r.openedDate ?? "—"}");

            void Pair(string k1, string v1, string k2, string v2)
                => UiTable.AddRow(_tableContent, new[] { k1, v1, k2, v2 }, DetailWeights, rowHeight: DetailRowHeight);

            Pair("班次", $"第 {index + 1} / {count} 班", "槽位", $"{slot}（{occ}）");
            Pair("机型", type, "注册号", reg);
            Pair("班期", $"{FlightScheduleFormat.DaysSummary(f.daysOfWeek)} · {FlightScheduleFormat.WeeklyOf(f)} 班/周",
                 "距离", $"{r.distanceKm:F0} km");
            Pair("去程", outbound, "过站", $"{m.turnMinutes:F0} 分（在 {r.destination}）");
            Pair("回程", inbound, "轮挡", FlightScheduleFormat.Duration(trip.blockMinutes));
            Pair("地勤", $"{r.groundStaffAssigned} 人（建议 {OpsEconomics.RecommendedGroundStaff(type)}）",
                 "日利用率", $"{m.dailyUtilizationHours:F1} h");
            Pair("调度效率", $"{m.scheduleEfficiency:F2}",
                 "延误概率", $"{m.delayProbability * 100f:F0}%");
            Pair("单班基准", $"{r.baseDailyProfit:F2} 万", "单班日利", $"{r.FlightNetDailyProfit:F2} 万");
            Pair("航线日利", $"{r.dailyProfit:F2} 万（{count} 班合计）", "评定", $"{r.score}（{r.rating}）");

            UiTable.AddNote(_tableContent,
                $"本页只讲 {noA} 这一班 · 单班日利 = 航线日净利 ÷ 班次数，各班相加 = 航线日利；"
                + "本线全部班次的周排班去单航线页点该班次。");
            // 只有 9 行 + 标题 + 脚注，可视区上下会剩一大截：让数据行吸收余量，其余上下均分。
            UiTable.FillVertically(_tableContent);
        }

        void FillSeasonTable(GameState gs)
        {
            int routes = gs?.activeRoutes?.Count ?? 0;
            int fleet = gs?.fleet?.Count ?? 0;
            ApplyPanelTitle(null, null, -1);
            UiTable.AddCaption(_tableContent, "夏秋航季（示意）");
            // 无「项目 / 内容」表头行
            void Row(string k, string v) => UiTable.AddRow(_tableContent, new[] { k, v }, SeasonWeights);
            Row("当前航季", "夏秋");
            Row("航网规模", $"已开通 {routes} 条航线");
            Row("机队规模", $"{fleet} 架");
            Row("航线与时刻", "始发/目的地机场与计划起降时刻");
            Row("执行班期", "一周中执飞日");
            Row("机型座位", "执飞机型与座位配置");
            Row("航班号", "唯一航班编号");
            Row("起止日期", "航季内运营区间");
            Row("航班性质", "正班 / 加班 / 包机");
            Row("运力调整", "投放与网络优化");
            UiTable.AddNote(_tableContent, "下一步：点「生成周总计划」。× 关闭整层；← 返回只退一层。");
        }

        void HideSelector()
        {
            var sel = GetComponentInChildren<OpsRouteRingSelectorUI>(true);
            if (sel != null) sel.gameObject.SetActive(false);
        }

        static string NormalizeKind(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (id.StartsWith("n42")) return "n42";
            if (id.StartsWith("n43")) return "n43";
            if (id.StartsWith("n66")) return "n66";
            if (id.StartsWith("n36")) return "n36";
            if (id.StartsWith("n98")) return "n98";
            if (id.Contains("周总")) return "n42";
            if (id.Contains("详细") || id.Contains("飞行计划")) return "n43";
            if (id.Contains("航季")) return "n66";
            if (id.Contains("选择器")) return "n98";
            if (id.Contains("航班环") || id.Contains("构建航班环")) return "n36";
            return id;
        }
    }
}
