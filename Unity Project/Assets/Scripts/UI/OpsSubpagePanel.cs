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
        static readonly float[] WeekWeights = { 1.2f, 0.9f, 1f, 0.7f, 0.9f, 1.3f, 0.8f };
        static readonly string[] WeekHeaders = { "航线", "航班号", "机型", "周班", "班期", "时刻", "日利(万)" };
        static readonly float[] SeasonWeights = { 1.2f, 2.8f };
        static readonly float[] DetailWeights = { 1f, 2.5f };

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
            ApplyPanelTitle(null);
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

        void OpenFlightPlan(string routeId)
        {
            OpsSubpageContext.SelectedRouteId = routeId;
            OpsPanelUI.OpenOpsPage("n43_航班详细计划");
        }

        void ApplyPanelTitle(RouteData selected)
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
                    if (selected != null && !string.IsNullOrEmpty(selected.flightNumber))
                        titleLabel.text = $"{selected.flightNumber} 航班飞行计划";
                    else
                        titleLabel.text = "航班飞行计划";
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
                ApplyPanelTitle(null);
                return;
            }

            if (_pageKind == "n98")
            {
                SetBodyVisible(false);
                SetTableVisible(false);
                var ring = GetComponentInChildren<OpsRingBuilderUI>(true);
                if (ring != null) ring.gameObject.SetActive(false);
                if (card != null) OpsRouteRingSelectorUI.Ensure(card);
                ApplyPanelTitle(null);
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
                new UIFactory.Anchor(new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero));
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

        void FillWeekTable(GameState gs)
        {
            int n = gs?.activeRoutes?.Count ?? 0;
            ApplyPanelTitle(null);
            UiTable.AddCaption(_tableContent, $"航季 {(n > 0 ? gs.activeRoutes[0].season ?? "夏秋" : "夏秋")} · 共 {n} 条");
            UiTable.AddHeader(_tableContent, WeekHeaders, WeekWeights);

            if (n == 0)
            {
                UiTable.AddNote(_tableContent, "暂无航班。请先在运营部点「开通航线」。");
                return;
            }

            foreach (var r in gs.activeRoutes)
            {
                if (r == null) continue;
                string routeId = r.routeId;
                r.EnsureFlights();
                // ★时刻列印**一次往返的真实占窗**（去程起飞 → 回程落地，跨夜标 +1），
                // 与单航线页抬头同一口径。旧版印 r.depTime-r.arrTime（去程落地），
                // 于是同一条线在周计划是「06:30-15:13」、在单航线是「06:30-00:46」—— 2026-10-14 报障。
                string clock = r.flights != null && r.flights.Count > 0
                    ? AirlineCEO.Logic.FlightScheduleFormat.WindowLabel(r, r.flights[0])
                    : $"{r.depTime}-{r.arrTime}";
                UiTable.AddRow(_tableContent, new[]
                {
                    $"{AirportCatalog.Od(r.origin, r.destination)}",
                    r.flightNumber ?? "—",
                    r.aircraftType ?? "—",
                    r.weeklyFlights.ToString(),
                    r.daysOfWeek ?? "—",
                    clock,
                    r.dailyProfit.ToString("F1")
                }, WeekWeights, header: false, onClick: () => OpenFlightPlan(routeId));
            }

            UiTable.AddNote(_tableContent, "提示：点击表格中的航线行，查看该航班飞行计划。");
        }

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

            ApplyPanelTitle(r);
            // 无「字段/数值」表头；无表内「航班详细计划」小标题（标题在面板顶栏）

            if (r == null)
            {
                UiTable.AddNote(_tableContent, "未选中航班。请先开通航线，再从周计划点选航线。");
                return;
            }

            var m = OpsEconomics.Evaluate(r.distanceKm, r.weeklyFlights, r.aircraftType,
                r.groundStaffAssigned, gs?.onTimePercent ?? 88f);

            void Row(string k, string v) => UiTable.AddRow(_tableContent, new[] { k, v }, DetailWeights);
            Row("航线", string.IsNullOrEmpty(r.stopover)
                ? AirportCatalog.Od(r.origin, r.destination)
                : AirportCatalog.Od(r.origin, r.destination, r.stopover));
            Row("距离", $"{r.distanceKm:F0} km");
            Row("机型", r.aircraftType);
            Row("注册号", r.registration ?? "—");
            Row("班期", $"{r.daysOfWeek} · {r.weeklyFlights} 班/周");
            // 与周计划/单航线页同一口径：一次往返的真实占窗（跨夜标 +1）
            r.EnsureFlights();
            Row("时刻", r.flights != null && r.flights.Count > 0
                ? AirlineCEO.Logic.FlightScheduleFormat.WindowLabel(r, r.flights[0])
                : $"{r.depTime} → {r.arrTime}");
            Row("地勤", $"{r.groundStaffAssigned} 人（建议 {OpsEconomics.RecommendedGroundStaff(r.aircraftType)}）");
            Row("过站 / 利用率", $"{m.turnMinutes:F0} 分 / {m.dailyUtilizationHours:F1} h");
            Row("调度效率", $"{m.scheduleEfficiency:F2}");
            Row("延误概率", $"{m.delayProbability * 100f:F0}%");
            Row("单班基准", $"{r.baseDailyProfit:F2} 万");
            Row("日利润", $"{r.dailyProfit:F2} 万");
            Row("评定", $"{r.score}（{r.rating}）");
            Row("开通", r.openedDate ?? "—");
        }

        void FillSeasonTable(GameState gs)
        {
            int routes = gs?.activeRoutes?.Count ?? 0;
            int fleet = gs?.fleet?.Count ?? 0;
            ApplyPanelTitle(null);
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
