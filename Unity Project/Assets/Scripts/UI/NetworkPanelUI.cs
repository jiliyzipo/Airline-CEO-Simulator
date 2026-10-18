using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// n12 航线网络（L1）：航线总览 + 航权与时刻槽 + 城市经济指数 + 运力缺口 + 开通新航线。
    /// 航权与时刻槽（n77）自「愿景」移入本模块：以页签形式内嵌 STG 子页面板，开航准入/持槽与航网同屏管理。
    /// </summary>
    public class NetworkPanelUI : MonoBehaviour
    {
        TextMeshProUGUI _kpiText;
        RectTransform _listRoot;
        readonly List<GameObject> _cards = new List<GameObject>();
        GameObject _overviewRoot;
        GameObject _economyRoot;
        RectTransform _economyList;
        readonly Button[] _tabs = new Button[5];
        int _tab;
        GameObject _gapRoot;
        TextMeshProUGUI _gapBody;
        GameObject _rightsRoot;
        StrategySubpagePanel _rightsPanel;
        GameObject _routeRoot;
        OpenRouteModalUI _routeEmbed;
        bool _showAllCities = true;
        string _expandedContinent = "";
        string _expandedCountry = "";
        bool _applyFocusExpand;
        ScrollRect _ecoScroll;

        public static NetworkPanelUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Network", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<NetworkPanelUI>();
            ui.BuildContent(panel.transform);
            ui.Refresh();
            return ui;
        }

        void BuildContent(Transform root)
        {
            UIFactory.Label(root, "Title", "航线", 20, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.94f), new Vector2(0.12f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            string[] labels = { "航线总览", "航权与时刻槽", "城市经济指数", "运力缺口", "开通新航线" };
            var bar = UIFactory.Panel(root, "NetTabs",
                new UIFactory.Anchor(new Vector2(0.13f, 0.935f), new Vector2(0.97f, 0.995f), Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.10f, 0.18f, 0.55f));
            bar.GetComponent<Image>().raycastTarget = false;
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                float x0 = i / 5f;
                float x1 = (i + 1) / 5f;
                _tabs[i] = UIFactory.Button(bar.transform, "NT" + i, labels[i],
                    new UIFactory.Anchor(new Vector2(x0 + 0.008f, 0.08f), new Vector2(x1 - 0.008f, 0.92f),
                        Vector2.zero, Vector2.zero),
                    i == 4 ? UITheme.Accent : UITheme.NavIdle, () => SetTab(idx), 13);
            }

            _overviewRoot = UIFactory.Panel(root, "Overview",
                new UIFactory.Anchor(new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.92f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var ovImg = _overviewRoot.GetComponent<Image>();
            if (ovImg != null) { ovImg.color = new Color(0, 0, 0, 0); ovImg.raycastTarget = false; }

            _kpiText = UIFactory.Label(_overviewRoot.transform, "Kpi", "统计加载中…", 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.86f), new Vector2(0.98f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _kpiText.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.Label(_overviewRoot.transform, "ListTitle", "已开通航线（点击查看详情）", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.78f), new Vector2(0.6f, 0.85f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            _listRoot = UIFactory.VerticalScrollArea(_overviewRoot.transform, "RouteList",
                new UIFactory.Anchor(new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.78f), Vector2.zero, Vector2.zero));
            var vlg = _listRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 28, 12, 12);
            vlg.spacing = 10;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            var fitter = _listRoot.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _rightsRoot = UIFactory.Panel(root, "Rights",
                new UIFactory.Anchor(new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.92f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var rrImg = _rightsRoot.GetComponent<Image>();
            if (rrImg != null) { rrImg.color = new Color(0, 0, 0, 0); rrImg.raycastTarget = false; }
            _rightsRoot.SetActive(false);

            _economyRoot = UIFactory.Panel(root, "Economy",
                new UIFactory.Anchor(new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.92f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var eImg = _economyRoot.GetComponent<Image>();
            if (eImg != null) { eImg.color = new Color(0, 0, 0, 0); eImg.raycastTarget = false; }
            _economyRoot.SetActive(false);

            _gapRoot = UIFactory.Panel(root, "Capacity",
                new UIFactory.Anchor(new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.92f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var gImg = _gapRoot.GetComponent<Image>();
            if (gImg != null) { gImg.color = new Color(0, 0, 0, 0); gImg.raycastTarget = false; }
            _gapBody = UIFactory.ScrollableLabel(_gapRoot.transform, "GapBody",
                new UIFactory.Anchor(new Vector2(0.03f, 0.16f), new Vector2(0.97f, 0.96f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, 15);
            UIFactory.Button(_gapRoot.transform, "SubmitGap", "将补强议题发到收件箱",
                new UIFactory.Anchor(new Vector2(0.30f, 0.03f), new Vector2(0.70f, 0.12f), Vector2.zero, Vector2.zero),
                UITheme.Accent, SubmitCapacityGap, 14);
            _gapRoot.SetActive(false);

            _routeRoot = UIFactory.Panel(root, "OpenRouteHost",
                new UIFactory.Anchor(new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.92f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var rImg = _routeRoot.GetComponent<Image>();
            if (rImg != null) { rImg.color = new Color(0, 0, 0, 0); rImg.raycastTarget = false; }
            _routeRoot.SetActive(false);
            SetTab(0);
        }

        public void ShowOpenRouteTab() => SetTab(4);

        void SetTab(int idx)
        {
            _tab = Mathf.Clamp(idx, 0, 4);
            var body = ModuleFrame.Body;
            if (body != null && body.IsChildOf(transform))
                ModuleFrame.ExitSubpage();
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                var img = _tabs[i].GetComponent<Image>();
                if (img != null)
                {
                    if (i == 4) img.color = i == _tab ? UITheme.Accent : UITheme.NavIdle;
                    else img.color = i == _tab ? UITheme.NavActive : UITheme.NavIdle;
                }
            }
            if (_overviewRoot != null) _overviewRoot.SetActive(_tab == 0);
            if (_rightsRoot != null) _rightsRoot.SetActive(_tab == 1);
            if (_economyRoot != null) _economyRoot.SetActive(_tab == 2);
            if (_gapRoot != null) _gapRoot.SetActive(_tab == 3);
            if (_routeRoot != null) _routeRoot.SetActive(_tab == 4);
            if (_tab == 1) EnsureRightsEmbed();
            if (_tab == 4) EnsureRouteEmbed();
            Refresh();
        }

        public void ShowEconomyForCity(string iata)
        {
            if (!string.IsNullOrEmpty(iata))
                FleetNetContext.SelectedCityIata = iata.Trim().ToUpperInvariant();
            FleetNetContext.OpenEconomyTab = false;
            _showAllCities = true;
            _applyFocusExpand = true;
            SetTab(2);
        }

        /// <summary>深链入口：切到「航权与时刻槽」页签（收件箱「详见」/ 终端导航 n77 用）。</summary>
        public void ShowRightsTab()
        {
            FleetNetContext.OpenRightsTab = false;
            SetTab(1);
        }

        void OnEnable()
        {
            if (FleetNetContext.OpenRightsTab)
                ShowRightsTab();
            else if (FleetNetContext.OpenEconomyTab)
            {
                FleetNetContext.OpenEconomyTab = false;
                ShowEconomyForCity(FleetNetContext.SelectedCityIata);
            }
            else
                Refresh();
            if (GameState.Instance != null)
                GameState.Instance.OnStateChanged += OnStateChanged;
        }

        void OnDisable()
        {
            if (GameState.Instance != null)
                GameState.Instance.OnStateChanged -= OnStateChanged;
        }

        void OnStateChanged()
        {
            if (_tab == 2) return;   // 城市经济指数自行重建
            Refresh();
        }

        public void Refresh()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            if (_tab == 1)
            {
                if (_rightsPanel != null) _rightsPanel.RefreshBody();
                return;
            }
            if (_tab == 2)
            {
                RebuildEconomy(gs);
                return;
            }
            if (_tab == 3)
            {
                RebuildCapacity(gs);
                return;
            }
            if (_tab == 4)
                return;

            if (gs.activeRoutes != null)
                RouteGeography.PruneImplausible(gs.activeRoutes, MapWidget.GreatCircleKm);
            int routes = gs.activeRoutes?.Count ?? 0;
            int airworthy = FleetManager.Instance != null ? FleetManager.Instance.CountAirworthy(gs) : 0;
            int gap = CapacityPlanning.Gap(routes, airworthy);
            float ratio = CapacityPlanning.GapRatio(routes, airworthy);
            bool red = ratio > CapacityPlanning.WarnRatio;
            if (_tabs != null && _tabs.Length > 3 && _tabs[3] != null)
            {
                // 页签文案固定为「运力缺口」，后面不带数字：
                // 与其余页签（航线总览 / 航权与时刻槽 / 城市经济指数 / 开通新航线）保持一致，
                // 且居中页签的文字宽度不会随缺口大小左右晃动。
                // 缺口数量在下方 KPI 行「缺口 N 架（X%）」给出，页签本身只用颜色提示严重度。
                var img = _tabs[3].GetComponent<Image>();
                if (img != null && _tab != 3)
                    img.color = red ? UITheme.Danger : (gap > 0 ? UITheme.Warning : UITheme.NavIdle);
            }
            var cities = new HashSet<string>();
            float totalDaily = 0f;
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    if (!string.IsNullOrEmpty(r.origin)) cities.Add(r.origin);
                    if (!string.IsNullOrEmpty(r.destination)) cities.Add(r.destination);
                    // 与 GameState.monthlyRevenue 同口径：停飞航线不进「网络日利润」，
                    // 否则这条 KPI 会把幽灵航班的收入又加回来一遍。
                    if (FlightGroundLogic.IsPersistent(FlightGroundLogic.RouteOf(gs, r))) continue;
                    totalDaily += r.dailyProfit;
                }
            }

            if (_kpiText != null)
            {
                _kpiText.text =
                    $"总航线 {routes} · 适航 {airworthy} 架 · 缺口 {gap} 架（{ratio * 100f:F0}%）{(red ? " · 红灯>20%" : "")} · 覆盖城市 {cities.Count} · 网络日利润 {totalDaily:F1} 万\n" +
                    $"准点率 {gs.onTimePercent:F0}% · 客座率 {gs.loadFactorPercent:F0}% · 平均航距见单航线详情";
            }

            RebuildCards(gs);
        }

        void RebuildEconomy(GameState gs)
        {
            if (_economyRoot == null) return;

            float keepScroll = 1f;
            if (_ecoScroll != null)
                keepScroll = _ecoScroll.verticalNormalizedPosition;

            if (_economyList == null)
            {
                UIFactory.Label(_economyRoot.transform, "Hint",
                    "近年水平约数（非上一年度精确官方数）· 开航与宏观周期会微调 · 不联网",
                    12, TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.02f, 0.90f), new Vector2(0.62f, 0.99f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);

                UIFactory.Button(_economyRoot.transform, "Scope", _showAllCities ? "仅枢纽+已开航" : "显示全部目录",
                    new UIFactory.Anchor(new Vector2(0.63f, 0.90f), new Vector2(0.80f, 0.99f), Vector2.zero, Vector2.zero),
                    UITheme.NavIdle, () =>
                    {
                        _showAllCities = !_showAllCities;
                        var t = _economyRoot.transform.Find("Scope");
                        var cap = t != null ? t.GetComponentInChildren<TextMeshProUGUI>() : null;
                        if (cap != null) cap.text = _showAllCities ? "仅枢纽+已开航" : "显示全部目录";
                        RebuildEconomy(gs);
                    }, 11);

                UIFactory.Button(_economyRoot.transform, "Reload", "重载离线表",
                    new UIFactory.Anchor(new Vector2(0.82f, 0.90f), new Vector2(0.98f, 0.99f), Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, () =>
                    {
                        CityEconomyLogic.ReloadOfflineTable(gs);
                        RebuildEconomy(gs);
                    }, 11);

                _economyList = UIFactory.VerticalScrollArea(_economyRoot.transform, "EcoScroll",
                    new UIFactory.Anchor(new Vector2(0.01f, 0.01f), new Vector2(0.99f, 0.88f), Vector2.zero, Vector2.zero));
                _ecoScroll = _economyList.GetComponentInParent<ScrollRect>();
            }
            else
            {
                for (int i = _economyList.childCount - 1; i >= 0; i--)
                    DestroyImmediate(_economyList.GetChild(i).gameObject);
                var scope = _economyRoot.transform.Find("Scope");
                var scopeCap = scope != null ? scope.GetComponentInChildren<TextMeshProUGUI>() : null;
                if (scopeCap != null)
                    scopeCap.text = _showAllCities ? "仅枢纽+已开航" : "显示全部目录";
            }

            var codes = CityEconomyLogic.VisibleCodes(gs, _showAllCities);
            string focus = FleetNetContext.SelectedCityIata;
            if (!string.IsNullOrEmpty(focus))
            {
                string up = focus.ToUpperInvariant();
                bool listed = false;
                for (int i = 0; i < codes.Count; i++)
                    if (string.Equals(codes[i], up, System.StringComparison.OrdinalIgnoreCase)) { listed = true; break; }
                if (!listed) codes.Add(up);
            }
            var views = new List<CityEconomyView>(codes.Count);
            for (int i = 0; i < codes.Count; i++)
                views.Add(CityEconomyLogic.Resolve(gs, codes[i]));
            bool jumpToFocus = _applyFocusExpand && !string.IsNullOrEmpty(focus);
            if (jumpToFocus)
            {
                for (int i = 0; i < views.Count; i++)
                {
                    if (!string.Equals(views[i].iata, focus, System.StringComparison.OrdinalIgnoreCase)) continue;
                    _expandedContinent = views[i].continentZh;
                    _expandedCountry = views[i].countryZh;
                    break;
                }
                _applyFocusExpand = false;
            }

            float y = 4f;
            float focusY = 0f;
            const float continentH = 42f;
            const float countryH = 38f;
            const float cityH = 72f;

            for (int c = 0; c < CityEconomyCatalog.ContinentOrder.Length; c++)
            {
                string continent = CityEconomyCatalog.ContinentOrder[c];
                int nInContinent = 0;
                for (int i = 0; i < views.Count; i++)
                    if (views[i].continentZh == continent) nInContinent++;
                if (nInContinent == 0) continue;

                bool openC = _expandedContinent == continent;
                string capC = continent + (openC ? "  ▲" : "  ▼") + $"  ·  {nInContinent} 城";
                string continentCap = continent;
                var btnC = UIFactory.Button(_economyList, "Cont" + c, capC, UIFactory.Fill,
                    openC ? UITheme.NavActive : UITheme.NavIdle, () =>
                    {
                        _expandedContinent = continentCap == _expandedContinent ? "" : continentCap;
                        _expandedCountry = "";
                        RebuildEconomy(gs);
                    }, 14);
                UIFactory.PinFromTop(btnC.gameObject, y, continentH, 0.01f, 0.96f);
                y += continentH + 6f;
                if (!openC) continue;

                var countries = new List<string>();
                for (int i = 0; i < views.Count; i++)
                {
                    if (views[i].continentZh != continent) continue;
                    if (!countries.Contains(views[i].countryZh))
                        countries.Add(views[i].countryZh);
                }

                for (int k = 0; k < countries.Count; k++)
                {
                    string country = countries[k];
                    int nInCountry = 0;
                    for (int i = 0; i < views.Count; i++)
                        if (views[i].continentZh == continent && views[i].countryZh == country)
                            nInCountry++;

                    bool openK = _expandedCountry == country;
                    string capK = "　　" + country + (openK ? "  ▲" : "  ▼") + $"  ·  {nInCountry}";
                    string countryCap = country;
                    var btnK = UIFactory.Button(_economyList, "Nat" + c + "_" + k, capK, UIFactory.Fill,
                        openK ? UITheme.PanelDeep : UITheme.NavIdle, () =>
                        {
                            _expandedCountry = countryCap == _expandedCountry ? "" : countryCap;
                            RebuildEconomy(gs);
                        }, 13);
                    UIFactory.PinFromTop(btnK.gameObject, y, countryH, 0.04f, 0.96f);
                    y += countryH + 5f;
                    if (!openK) continue;

                    for (int i = 0; i < views.Count; i++)
                    {
                        var v = views[i];
                        if (v.continentZh != continent || v.countryZh != country) continue;
                        bool hi = !string.IsNullOrEmpty(focus)
                                  && string.Equals(focus, v.iata, System.StringComparison.OrdinalIgnoreCase);
                        var card = UIFactory.Panel(_economyList, "C" + v.iata, UIFactory.Fill,
                            hi ? UITheme.NavActive : UITheme.PanelDeep);
                        UIFactory.PinFromTop(card, y, cityH, 0.07f, 0.96f);
                        if (hi) focusY = y;
                        string tag = v.isHub ? "枢纽" : (v.served ? "已开航" : "目录");
                        UIFactory.Label(card.transform, "N",
                            $"{v.iata}  ·  {v.cityZh}（{v.airportZh}）  ·  {tag}  ·  指数 {v.liveIndex:F0}",
                            14, TextAlignmentOptions.MidlineLeft,
                            new UIFactory.Anchor(new Vector2(0.02f, 0.52f), new Vector2(0.98f, 0.95f), Vector2.zero, Vector2.zero),
                            UITheme.TextPrimary, FontStyles.Bold);
                        UIFactory.Label(card.transform, "S",
                            $"城市生产总值 {v.gdpUsdBn * 10f:F0} 亿美元　　人口 {v.popMillion * 100f:F0} 万人　　旅客吞吐量 {v.paxMillion * 100f:F0} 万人次　　货邮吞吐量 {v.cargoWanTon:F0} 万吨　　起讫客流占比 {v.odIndex:F0}%",
                            12, TextAlignmentOptions.MidlineLeft,
                            new UIFactory.Anchor(new Vector2(0.02f, 0.08f), new Vector2(0.98f, 0.50f), Vector2.zero, Vector2.zero),
                            UITheme.TextMuted);
                        y += cityH + 6f;
                    }
                }
            }
            UIFactory.SetScrollContentHeight(_economyList, y + 12f);
            if (_ecoScroll != null)
            {
                float contentH = Mathf.Max(_economyList.sizeDelta.y, 1f);
                float viewH = _ecoScroll.viewport != null ? _ecoScroll.viewport.rect.height : 400f;
                if (jumpToFocus && focusY > 0f && contentH > viewH)
                    _ecoScroll.verticalNormalizedPosition = Mathf.Clamp01(1f - focusY / (contentH - viewH));
                else
                    _ecoScroll.verticalNormalizedPosition = Mathf.Clamp01(keepScroll);
            }
        }

        void RebuildCards(GameState gs)
        {
            for (int i = 0; i < _cards.Count; i++)
                if (_cards[i] != null) Destroy(_cards[i]);
            _cards.Clear();
            if (_listRoot == null || gs.activeRoutes == null)
            {
                FitRouteList();
                return;
            }

            var permits = RoutePermitLogic.InFlightPermits(gs);
            for (int i = 0; i < permits.Count; i++)
            {
                string line = "拟开航 · 准入办理中 · " + RoutePermitLogic.ProgressLine(permits[i])
                    + "\n进度见「航权与时刻槽」；异议或重申在收件箱处理";
                var pl = UIFactory.Label(_listRoot, "Permit" + i, line, 13,
                    TextAlignmentOptions.TopLeft,
                    new UIFactory.Anchor(new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero),
                    UITheme.Warning);
                pl.textWrappingMode = TextWrappingModes.Normal;
                pl.overflowMode = TextOverflowModes.Truncate;
                pl.lineSpacing = 2f;
                var ple = pl.gameObject.AddComponent<LayoutElement>();
                ple.minHeight = 56f;
                ple.preferredHeight = 56f;
                _cards.Add(pl.gameObject);
            }

            if (gs.activeRoutes.Count == 0)
            {
                var empty = UIFactory.Label(_listRoot, "Empty",
                    permits.Count > 0
                        ? "暂无在营航线 — 上方为在办准入许可，办妥发证后自动开航"
                        : "暂无航线 — 点「开通新航线」进行 RPM 评估与开线决策",
                    15, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(0, 0.6f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                empty.textWrappingMode = TextWrappingModes.Normal;
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
                _cards.Add(empty.gameObject);
                FitRouteList();
                return;
            }

            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var route = gs.activeRoutes[i];
                var card = RouteCard.BuildRuntime(_listRoot);
                card.Bind(route);
                var le = card.gameObject.GetComponent<LayoutElement>();
                if (le == null) le = card.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 72;
                le.flexibleWidth = 1;
                _cards.Add(card.gameObject);

                var btn = card.gameObject.GetComponent<Button>();
                if (btn == null) btn = card.gameObject.AddComponent<Button>();
                string detailId = route.routeId;
                btn.onClick.AddListener(() =>
                {
                    FleetNetContext.SelectedRouteId = detailId;
                    OpenNetPage("n41_单航线");
                });
            }
            FitRouteList();
        }

        void FitRouteList()
        {
            if (_listRoot == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_listRoot);
        }

        public static void OpenNetPage(string pageId)
        {
            var nav = FindFirstObjectByType<PrefabNavIntegration>();
            if (nav != null)
            {
                nav.OpenSubPage(pageId);
                return;
            }

            var router = AirlineCEO.UI.Navigation.NavigationRouter.Instance;
            if (router != null)
                router.Navigate(pageId, replaceCurrent: false);
            else
                Debug.LogWarning("[NET] NavigationRouter 未就绪：" + pageId);
        }

        void RebuildCapacity(GameState gs)
        {
            if (_gapBody == null || gs == null) return;
            int airworthy = FleetManager.Instance != null ? FleetManager.Instance.CountAirworthy(gs) : 0;
            int routes = gs.activeRoutes?.Count ?? 0;
            int gap = CapacityPlanning.Gap(routes, airworthy);
            float ratio = CapacityPlanning.GapRatio(routes, airworthy);
            _gapBody.text =
                $"适航飞机　{airworthy} 架\n" +
                $"已开通航线　{routes} 条\n" +
                $"运力缺口　{gap} 架（航线数 − 适航架数）\n" +
                $"缺口率　{ratio:P0}\n\n" +
                (gap > 0
                    ? "建议由机队补强。点下方按钮把补强议题发到收件箱，由您以 CEO 身份决策。"
                    : "当前适航运力可覆盖航网，暂不需要引进。");
        }

        void EnsureRouteEmbed()
        {
            if (_routeRoot == null) return;
            if (_routeEmbed == null)
                _routeEmbed = OpenRouteModalUI.MountIn(_routeRoot.transform);
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
            var flow = OpenRouteDecisionFlow.Ensure(canvas != null ? canvas.transform : transform);
            flow.Bind(onStateChanged: Refresh);
            _routeEmbed.Show(sel => flow.BeginAfterAirportPick(sel));
        }

        /// <summary>
        /// 内嵌「航权与时刻槽」（n77）：复用 STG 子页面板，挂在本模块的页签宿主下。
        /// 与从「愿景」打开是同一份渲染逻辑（StrategySubpagePanel），数据源同为 GameState，不产生第二套状态。
        /// </summary>
        void EnsureRightsEmbed()
        {
            if (_rightsRoot == null) return;
            if (_rightsPanel == null)
            {
                _rightsPanel = StrategySubpagePanel.Build("n77", "航权与时刻槽");
                _rightsPanel.transform.SetParent(_rightsRoot.transform, false);
                var rt = _rightsPanel.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    rt.localScale = Vector3.one;
                }
            }
            // 内容由 SetTab → Refresh()（_tab==1 分支）统一刷新，这里不重复 RefreshBody。
        }

        void SubmitCapacityGap()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            int airworthy = FleetManager.Instance != null ? FleetManager.Instance.CountAirworthy(gs) : 0;
            int routes = gs.activeRoutes?.Count ?? 0;
            int gap = CapacityPlanning.Gap(routes, airworthy);
            if (gap > 0)
            {
                InboxManager.QueueCapacityMail(gs, gap);
                gs.AddDecisionLog($"【航线网】运力缺口 {gap} 架（适航 {airworthy} / 航线 {routes}），已提交补强议题。");
            }
            else
                gs.AddDecisionLog($"【航线网】适航 {airworthy} 架覆盖航线 {routes} 条，暂无缺口。");
            gs.NotifyChanged();
            Refresh();
        }
    }
}
