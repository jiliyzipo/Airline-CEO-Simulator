using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Shell;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>Phase 10 · 机队列表面板。</summary>
    public class FleetUI : MonoBehaviour
    {
        private TextMeshProUGUI _summaryText;
        private TextMeshProUGUI _toastText;
        private Transform _listRoot;
        private FleetPurchaseUI _purchaseUi;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private GameObject _overviewRoot;
        private Transform _marketRoot;
        private RectTransform _marketList;
        private int _tab;
        private readonly Button[] _tabs = new Button[5];
        private GameObject _transitionRoot;
        private RectTransform _transitionList;

        /// <summary>由其他面板（如收件箱指引邮件）请求在下一次刷新时切到的标签页；-1 表示无请求。</summary>
        public static int PendingTab = -1;

        private const float ColIcon0 = 0.01f, ColIcon1 = 0.06f;
        private const float ColReg0 = 0.06f, ColReg1 = 0.14f;
        private const float ColType0 = 0.14f, ColType1 = 0.24f;
        private const float ColSrc0 = 0.24f, ColSrc1 = 0.33f;
        private const float ColAge0 = 0.33f, ColAge1 = 0.39f;
        private const float ColMaint0 = 0.39f, ColMaint1 = 0.47f;
        private const float ColLease0 = 0.47f, ColLease1 = 0.55f;
        private const float ColStatus0 = 0.55f, ColStatus1 = 0.64f;
        private const float ColAction0 = 0.64f, ColAction1 = 0.99f;
        int _oemIndex;
        string _expandedFamily = "";

        public static FleetUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_Fleet", UIFactory.Fill, UITheme.Panel);
            UIFactory.AddBorder(panel, UITheme.Border, new Vector2(1f, -1f));
            var ui = panel.AddComponent<FleetUI>();
            ui.BuildContent(panel.transform);
            return ui;
        }

        public void BindPurchase(FleetPurchaseUI purchase) => _purchaseUi = purchase;

        /// <summary>先切到底栏机队，再打开购机决策会议。</summary>
        public static void OpenPurchaseMeeting(string typeId = null)
        {
            if (!string.IsNullOrEmpty(typeId))
                FleetNetContext.PurchaseTypeId = typeId;
            var nav = FindFirstObjectByType<PrefabNavIntegration>();
            if (nav != null)
                nav.SelectTab("n9_机队");
            OpenFleetPage("n97_购机决策会议");
        }

        public static void OpenFleetPage(string pageId)
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
                Debug.LogWarning("[FLEET] NavigationRouter 未就绪：" + pageId);
        }

        private void BuildContent(Transform panel)
        {
            UIFactory.Label(panel, "Title", "机队", 20, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.94f), new Vector2(0.12f, 0.995f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            string[] labels = { "机队总览", "新机购买", "二手市场", "租赁", "过渡业务" };
            var bar = UIFactory.Panel(panel, "FleetTabs",
                new UIFactory.Anchor(new Vector2(0.13f, 0.935f), new Vector2(0.90f, 0.995f), Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.10f, 0.18f, 0.55f));
            bar.GetComponent<Image>().raycastTarget = false;
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                float x0 = i / (float)labels.Length;
                float x1 = (i + 1) / (float)labels.Length;
                _tabs[i] = UIFactory.Button(bar.transform, "FT" + i, labels[i],
                    new UIFactory.Anchor(new Vector2(x0 + 0.01f, 0.08f), new Vector2(x1 - 0.01f, 0.92f),
                        Vector2.zero, Vector2.zero),
                    UITheme.NavIdle, () => SetTab(idx), 13);
            }

            _overviewRoot = UIFactory.Panel(panel, "Overview",
                new UIFactory.Anchor(new Vector2(0.01f, 0.04f), new Vector2(0.99f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var ovImg = _overviewRoot.GetComponent<Image>();
            if (ovImg != null) { ovImg.color = new Color(0, 0, 0, 0); ovImg.raycastTarget = false; }

            _summaryText = UIFactory.Label(_overviewRoot.transform, "Summary", "", 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.90f), new Vector2(0.72f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            UIFactory.Button(_overviewRoot.transform, "BuyMeet", "购机决策会议",
                new UIFactory.Anchor(new Vector2(0.74f, 0.90f), new Vector2(0.99f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => OpenPurchaseMeeting(), 13);

            var headerBar = UIFactory.Panel(_overviewRoot.transform, "HeaderBar",
                new UIFactory.Anchor(new Vector2(0.01f, 0.82f), new Vector2(0.99f, 0.89f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false).gameObject;
            headerBar.GetComponent<Image>().raycastTarget = false;
            AddColumnLabel(headerBar.transform, "HIcon", "图标", ColIcon0, ColIcon1);
            AddColumnLabel(headerBar.transform, "HReg", "注册号", ColReg0, ColReg1);
            AddColumnLabel(headerBar.transform, "HType", "机型", ColType0, ColType1);
            AddColumnLabel(headerBar.transform, "HSrc", "来源", ColSrc0, ColSrc1);
            AddColumnLabel(headerBar.transform, "HAge", "机龄", ColAge0, ColAge1);
            AddColumnLabel(headerBar.transform, "HMaint", "月维护", ColMaint0, ColMaint1);
            AddColumnLabel(headerBar.transform, "HLease", "月租金", ColLease0, ColLease1);
            AddColumnLabel(headerBar.transform, "HStatus", "状态", ColStatus0, ColStatus1);
            AddColumnLabel(headerBar.transform, "HAction", "操作", ColAction0, ColAction1);

            var listHost = UIFactory.VerticalScrollArea(_overviewRoot.transform, "ListHost",
                new UIFactory.Anchor(new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.82f), Vector2.zero, Vector2.zero));
            _listRoot = listHost;

            var marketGo = UIFactory.Panel(panel, "Market",
                new UIFactory.Anchor(new Vector2(0.01f, 0.04f), new Vector2(0.99f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var mImg = marketGo.GetComponent<Image>();
            if (mImg != null) { mImg.color = new Color(0, 0, 0, 0); mImg.raycastTarget = false; }
            _marketRoot = marketGo.transform;
            marketGo.SetActive(false);

            // ———— 过渡业务（开局三架飞机的盈利设计）————
            var transGo = UIFactory.Panel(panel, "Transition",
                new UIFactory.Anchor(new Vector2(0.01f, 0.04f), new Vector2(0.99f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0));
            var tImg = transGo.GetComponent<Image>();
            if (tImg != null) { tImg.color = new Color(0, 0, 0, 0); tImg.raycastTarget = false; }
            _transitionRoot = transGo;
            _transitionList = UIFactory.VerticalScrollArea(transGo.transform, "TransitionScroll",
                new UIFactory.Anchor(new Vector2(0.005f, 0.005f), new Vector2(0.995f, 0.995f), Vector2.zero, Vector2.zero));
            transGo.SetActive(false);

            _toastText = UIFactory.Label(panel, "Toast", "", 12, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.05f, 0.0f), new Vector2(0.95f, 0.035f), Vector2.zero, Vector2.zero),
                UITheme.Warning);
            SetTab(0);
        }

        void SetTab(int idx)
        {
            // 机队子页（飞机详情/座位配置/大修预算）正压在机队页上时先退出，
            // 否则切了标签也看不见（子页仍覆盖内容区，表现为「点了没反应」）。
            CloseModuleSubpage();
            _tab = Mathf.Clamp(idx, 0, 4);
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                var img = _tabs[i].GetComponent<Image>();
                if (img != null) img.color = i == _tab ? UITheme.NavActive : UITheme.NavIdle;
            }
            if (_overviewRoot != null) _overviewRoot.SetActive(_tab == 0);
            if (_marketRoot != null) _marketRoot.gameObject.SetActive(_tab > 0 && _tab < 4);
            if (_transitionRoot != null) _transitionRoot.SetActive(_tab == 4);
            Refresh();
        }

        /// <summary>退出机队页上的模块子页：关掉子页宿主并把导航栈弹回机队 L1。</summary>
        static void CloseModuleSubpage()
        {
            var router = AirlineCEO.UI.Navigation.NavigationRouter.Instance;
            if (router != null && router.StackDepth > 1)
                router.PopOverlaysKeepBase();
            AirlineCEO.UI.Navigation.ModuleFrame.ExitSubpage();
        }

        /// <summary>外侧入口：切到底栏「机队」并直达「过渡业务」页。</summary>
        public static void OpenTransitionTab()
        {
            PendingTab = 4;
            var nav = FindFirstObjectByType<PrefabNavIntegration>();
            if (nav != null) nav.SelectTab("n9_机队");
            var ui = FindFirstObjectByType<FleetUI>();
            if (ui != null) { PendingTab = -1; ui.SetTab(4); }
        }

        private static void AddColumnLabel(Transform parent, string name, string text, float x0, float x1)
        {
            UIFactory.Label(parent, name, text, 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(x0, 0f), new Vector2(x1, 1f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
        }

        public void Refresh()
        {
            if (FleetManager.Instance != null)
                FleetManager.Instance.EnsureStarterFleet();
            var gs = GameState.Instance;
            UsedAircraftMarket.Ensure(gs);

            if (PendingTab >= 0) { int t = PendingTab; PendingTab = -1; SetTab(t); return; }

            if (_tab == 0)
                RefreshOverview(gs);
            else if (_tab == 4)
                RebuildTransition(gs);
            else
                RebuildMarket(gs);
        }

        void RefreshOverview(GameState gs)
        {
            var fm = FleetManager.Instance;
            if (_summaryText != null && fm != null)
            {
                string tip = AdvisorTutorialScript.IsAwaitingFleet()
                    ? " ｜ 引导：请点击推荐飞机的「分配至深圳—北京」"
                    : "";
                // 预警数放在最前：一排 12 架里只要 1 架有徽标也容易被漏看，
                // 顶栏给出总数才能让玩家知道「要不要往下翻」。
                int ageRisk = FleetManager.CountLifeExtensionRisk(gs);
                string ageRiskPart = ageRisk > 0 ? $" ｜ ⚠ 延寿预警 {ageRisk} 架" : "";
                // 「在役」而非「适航」：`CountAirworthy` 的口径是 isActive 且适航，
                // 未分配航线的在册飞机不计入 —— 即便它们每行都写着「适航」。
                // 写「适航」会出现「总数 12 ｜ 适航 0」与 12 行绿色「适航」同屏打脸的画面。
                _summaryText.text =
                    $"总数 {fm.CountTotal(gs)} ｜ 在役 {fm.CountAirworthy(gs)} ｜ 平均机龄 {fm.AverageAge(gs):F1} 年" +
                    $" ｜ 月维护 {fm.CalcMonthlyMaintenance(gs):F1} 万 ｜ 月租金 {gs?.FleetMonthlyLease:F1} 万 ｜ 现金 {gs?.cash:F0} 万{ageRiskPart}{tip}";
                _summaryText.color = ageRisk > 0 ? UITheme.Warning : UITheme.TextMuted;
            }

            ClearRows();
            if (gs?.fleet == null || _listRoot == null) return;

            const float rowH = 56f;
            float y = 4f;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var ac = gs.fleet[i];
                if (ac == null) continue;
                CreateRow(ac, y, rowH);
                y += rowH + 6f;
            }
            UIFactory.SetScrollContentHeight(_listRoot as RectTransform, y + 8f);
        }

        void RebuildMarket(GameState gs)
        {
            if (_marketRoot == null) return;
            for (int i = _marketRoot.childCount - 1; i >= 0; i--)
                Destroy(_marketRoot.GetChild(i).gameObject);

            string hint = _tab == 1
                ? "全新飞机 · 按制造商选择系列与主力机型，一次性付清目录价。"
                : _tab == 2
                    ? $"二手货架 · 第 {gs?.year} 年 Q{gs?.quarter} 每季刷新 · 折扣 30–60% · 高龄可能是维修陷阱。"
                    : "融资租赁：10% 首付 + 月租 1.2%。经营租赁：无首付、月租 2.2%。";
            UIFactory.Label(_marketRoot, "H", hint, 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.01f, 0.91f), new Vector2(0.99f, 1.00f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var oems = AircraftCatalog.Oems;
            _oemIndex = Mathf.Clamp(_oemIndex, 0, oems.Length - 1);

            // 二手市场：在售货源分散在各制造商下。若当前制造商货架为空，自动落到有货的制造商页，
            // 免得打开二手市场只看到空列表。须在绘制制造商页签之前决定，保证高亮与内容一致。
            bool usedTab = _tab == 2;
            var onSale = usedTab ? UsedTypeIdsOnSale(gs) : null;
            if (usedTab && onSale.Count > 0 && !OemHasOnSale(oems[_oemIndex], onSale))
            {
                for (int i = 0; i < oems.Length; i++)
                {
                    if (!OemHasOnSale(oems[i], onSale)) continue;
                    _oemIndex = i;
                    _expandedFamily = "";
                    break;
                }
            }

            for (int i = 0; i < oems.Length; i++)
            {
                int idx = i;
                float x0 = i / (float)oems.Length;
                float x1 = (i + 1) / (float)oems.Length;
                UIFactory.Button(_marketRoot, "OEM" + i, oems[i],
                    new UIFactory.Anchor(new Vector2(x0 + 0.002f, 0.82f), new Vector2(x1 - 0.002f, 0.90f),
                        Vector2.zero, Vector2.zero),
                    i == _oemIndex ? UITheme.NavActive : UITheme.NavIdle, () =>
                    {
                        _oemIndex = idx;
                        _expandedFamily = "";
                        Refresh();
                    }, 10);
            }

            _marketList = UIFactory.VerticalScrollArea(_marketRoot, "CatalogScroll",
                new UIFactory.Anchor(new Vector2(0.01f, 0.01f), new Vector2(0.99f, 0.80f), Vector2.zero, Vector2.zero));

            string oem = oems[_oemIndex];
            var families = AircraftCatalog.FamiliesOf(oem);

            float y = 4f;
            const float famH = 40f;

            // 二手市场：货架上没有在售飞机的系列与机型整条不显示（只列在售货源）
            if (usedTab && onSale.Count == 0)
            {
                var empty = UIFactory.Label(_marketList, "UsedEmpty",
                    "本季二手货架暂无在售飞机。货架每季刷新一次，届时会补入新的一批货源。", 12,
                    TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextMuted);
                UIFactory.PinFromTop(empty.gameObject, y, 34f, 0.02f, 0.96f);
                UIFactory.SetScrollContentHeight(_marketList, y + 44f);
                return;
            }

            for (int f = 0; f < families.Count; f++)
            {
                string fam = families[f];
                var models = AircraftCatalog.ModelsOf(oem, fam);
                if (usedTab && !AnyOnSale(models, onSale)) continue;

                bool open = fam == _expandedFamily;
                string cap = fam + (open ? "  ▲" : "  ▼");
                string famCap = fam;
                var famBtn = UIFactory.Button(_marketList, "Fam" + f, cap,
                    UIFactory.Fill,
                    open ? UITheme.PanelDeep : UITheme.NavIdle, () =>
                    {
                        _expandedFamily = famCap == _expandedFamily ? "" : famCap;
                        Refresh();
                    }, 13);
                UIFactory.PinFromTop(famBtn.gameObject, y, famH, 0.01f, 0.96f);
                y += famH + 6f;
                if (!open) continue;

                for (int m = 0; m < models.Count; m++)
                {
                    var model = models[m];
                    if (usedTab && !onSale.Contains(model.typeId)) continue;
                    y = DrawModelCard(gs, model, y);
                }
            }
            UIFactory.SetScrollContentHeight(_marketList, y + 12f);
        }

        /// <summary>当前货架上仍有在售（未售出）二手机的机型 id 集合。</summary>
        static HashSet<string> UsedTypeIdsOnSale(GameState gs)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (gs?.usedMarket == null) return set;
            for (int i = 0; i < gs.usedMarket.Count; i++)
            {
                var lot = gs.usedMarket[i];
                if (lot == null || lot.sold) continue;
                if (!string.IsNullOrEmpty(lot.typeId)) set.Add(lot.typeId);
            }
            return set;
        }

        static bool AnyOnSale(List<AircraftCatalogEntry> models, HashSet<string> onSale)
        {
            if (models == null || onSale == null) return false;
            for (int i = 0; i < models.Count; i++)
            {
                if (models[i] != null && onSale.Contains(models[i].typeId)) return true;
            }
            return false;
        }

        /// <summary>该制造商名下是否有任何在售二手货源。</summary>
        static bool OemHasOnSale(string oem, HashSet<string> onSale)
        {
            if (string.IsNullOrEmpty(oem) || onSale == null || onSale.Count == 0) return false;
            var families = AircraftCatalog.FamiliesOf(oem);
            for (int f = 0; f < families.Count; f++)
            {
                if (AnyOnSale(AircraftCatalog.ModelsOf(oem, families[f]), onSale)) return true;
            }
            return false;
        }

        float DrawModelCard(GameState gs, AircraftCatalogEntry t, float yTop)
        {
            if (_tab == 2) return DrawUsedLots(gs, t, yTop);

            float h = _tab == 3 ? 84f : 72f;
            var card = UIFactory.Panel(_marketList, "M" + t.typeId + yTop,
                UIFactory.Fill, UITheme.PanelDeep);
            UIFactory.PinFromTop(card, yTop, h, 0.02f, 0.96f);
            UIFactory.Label(card.transform, "N", AircraftCatalog.Title(t), 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.52f), new Vector2(0.70f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            string seats = t.isFreighter ? $"货 {t.cargoTons:F0} t" : $"载客 {t.maxPassengers}";
            UIFactory.Label(card.transform, "S",
                $"航程 {t.maxRangeKm:N0} km　　{seats}　　目录价 {t.priceWan:F0} 万",
                12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.02f, 0.08f), new Vector2(0.58f, 0.50f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            string tid = t.typeId;
            if (_tab == 1)
            {
                UIFactory.Button(card.transform, "Buy", "提交购机会议",
                    new UIFactory.Anchor(new Vector2(0.72f, 0.18f), new Vector2(0.97f, 0.82f), Vector2.zero, Vector2.zero),
                    UITheme.Accent, () => OpenPurchaseMeeting(tid), 13);
            }
            else
            {
                UIFactory.Button(card.transform, "Fin", "融资租赁",
                    new UIFactory.Anchor(new Vector2(0.50f, 0.18f), new Vector2(0.73f, 0.82f), Vector2.zero, Vector2.zero),
                    UITheme.Accent, () =>
                    {
                        if (FleetManager.Instance != null &&
                            FleetManager.Instance.TryLeaseNew(tid, "融资租赁", out string msg))
                            Toast(msg);
                        Refresh();
                    }, 11);
                UIFactory.Button(card.transform, "Op", "经营租赁",
                    new UIFactory.Anchor(new Vector2(0.75f, 0.18f), new Vector2(0.97f, 0.82f), Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, () =>
                    {
                        if (FleetManager.Instance != null &&
                            FleetManager.Instance.TryLeaseNew(tid, "经营租赁", out string msg))
                            Toast(msg);
                        Refresh();
                    }, 11);
            }
            return yTop + h + 6f;
        }

        float DrawUsedLots(GameState gs, AircraftCatalogEntry t, float yTop)
        {
            var lots = new List<UsedListing>();
            if (gs?.usedMarket != null)
            {
                for (int i = 0; i < gs.usedMarket.Count; i++)
                {
                    var lot = gs.usedMarket[i];
                    if (lot == null || lot.sold) continue;   // 已售出即不在售，不再显示
                    if (lot.typeId == t.typeId) lots.Add(lot);
                }
            }

            // 无在售货源：不显示任何占位行（系列/机型已在上层被过滤掉）
            if (lots.Count == 0) return yTop;

            float y = yTop;
            for (int i = 0; i < lots.Count; i++)
            {
                var lot = lots[i];
                float h = 64f;
                var card = UIFactory.Panel(_marketList, lot.id,
                    UIFactory.Fill, UITheme.PanelDeep);
                UIFactory.PinFromTop(card, y, h, 0.02f, 0.96f);
                string trap = lot.trap ? "  ⚠陷阱" : "";
                UIFactory.Label(card.transform, "N",
                    $"{AircraftCatalog.Title(t)}  机龄 {lot.age} 年  寿命剩 {lot.remainingLife} 年  折扣 {lot.discount * 100:F0}%{trap}",
                    13, TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.02f, 0.45f), new Vector2(0.70f, 0.95f), Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary);
                UIFactory.Label(card.transform, "P", $"索价 {lot.askPriceWan:F0} 万",
                    12, TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.02f, 0.05f), new Vector2(0.50f, 0.45f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                string lid = lot.id;
                UIFactory.Button(card.transform, "Buy", "买入",
                    new UIFactory.Anchor(new Vector2(0.74f, 0.18f), new Vector2(0.97f, 0.82f), Vector2.zero, Vector2.zero),
                    UITheme.Accent, () =>
                    {
                        if (FleetManager.Instance != null &&
                            FleetManager.Instance.TryBuyUsed(lid, out string msg))
                            Toast(msg);
                        Refresh();
                    }, 13);
                y += h + 6f;
            }
            return y;
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null) Destroy(_rows[i]);
            }
            _rows.Clear();
        }

        private void CreateRow(Aircraft ac, float yMin, float height)
        {
            var row = UIFactory.Panel(_listRoot, "Row_" + ac.registration,
                UIFactory.Fill,
                new Color(0.08f, 0.14f, 0.24f, 0.85f));
            UIFactory.PinFromTop(row, yMin, height, 0.01f, 0.96f);
            _rows.Add(row);

            Color statusColor = ac.status switch
            {
                AircraftStatuses.Airworthy => UITheme.Success,
                AircraftStatuses.Maintenance => UITheme.Warning,
                _ => UITheme.TextMuted
            };

            var dot = UIFactory.Panel(row.transform, "Dot",
                new UIFactory.Anchor(new Vector2(0.01f, 0.28f), new Vector2(0.035f, 0.72f), Vector2.zero, Vector2.zero),
                statusColor, rounded: true);
            dot.GetComponent<Image>().raycastTarget = false;

            // Phase 14：机型图标
            var icon = UIFactory.Panel(row.transform, "Icon",
                new UIFactory.Anchor(new Vector2(ColIcon0, 0.10f), new Vector2(ColIcon1, 0.90f), Vector2.zero, Vector2.zero),
                Color.white, rounded: false);
            var iconImg = icon.GetComponent<Image>();
            iconImg.sprite = AircraftIcon.ForType(ac.type ?? "");
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            UIFactory.Label(row.transform, "Reg", ac.registration ?? "—", 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(ColReg0, 0.1f), new Vector2(ColReg1, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            string typeLabel = AircraftCatalog.Find(ac.type)?.displayName ?? ac.type ?? "—";
            UIFactory.Label(row.transform, "Type", typeLabel, 11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(ColType0, 0.1f), new Vector2(ColType1, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            UIFactory.Label(row.transform, "Src", string.IsNullOrEmpty(ac.source) ? "自有" : ac.source, 11,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(ColSrc0, 0.1f), new Vector2(ColSrc1, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            UIFactory.Label(row.transform, "Age", $"{ac.age} 年", 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(ColAge0, 0.1f), new Vector2(ColAge1, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            UIFactory.Label(row.transform, "Maint", $"{FleetManager.MonthlyMaintOf(ac):F1} 万", 11,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(ColMaint0, 0.1f), new Vector2(ColMaint1, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            // 已退役 / 已退租的飞机月租同步停止（see GameState.FleetMonthlyLease），列里就别再挂个数字
            bool disposed = !string.IsNullOrEmpty(ac.ExitLabel);
            UIFactory.Label(row.transform, "Lease",
                !disposed && ac.monthlyLeaseWan > 0.01f ? $"{ac.monthlyLeaseWan:F1} 万" : "—",
                11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(ColLease0, 0.1f), new Vector2(ColLease1, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            string transitionLabel = TransitionBusinessLogic.BusyLabel(GameState.Instance, ac.registration);
            string statusText = !string.IsNullOrEmpty(transitionLabel)
                ? transitionLabel
                : disposed ? "已" + ac.ExitLabel : (ac.status ?? "—");
            Color transitionColor = string.IsNullOrEmpty(transitionLabel) ? statusColor : UITheme.Warning;

            // ★2026-10-27：机龄-机况预警徽标。
            // 加它之前，`B-2005`（20 年）/`B-2006`（24 年）在列表里是清一色绿色的 `适航`，
            // 延寿规则只在点开详情页时才露面 —— 玩家得一架架点过去才知道年结会停哪几架。
            // 判据走 FleetManager.NeedsLifeExtensionWarning，与详情页同源，不在这里另写一套。
            bool ageRisk = FleetManager.NeedsLifeExtensionWarning(ac);
            bool statusStacked = ageRisk && string.IsNullOrEmpty(transitionLabel);
            UIFactory.Label(row.transform, "Status", statusText, 12, TextAlignmentOptions.MidlineLeft,
                statusStacked
                    // 双行：状态在上、徽标在下。纵向下沉到 0.45 是为了给徽标留出完整一行，
                    // 单行时仍用满格高度，避免普通飞机看起来比预警飞机矮一截。
                    ? new UIFactory.Anchor(new Vector2(ColStatus0, 0.46f), new Vector2(ColStatus1 + 0.06f, 0.94f), Vector2.zero, Vector2.zero)
                    : new UIFactory.Anchor(new Vector2(ColStatus0, 0.1f), new Vector2(ColStatus1 + 0.06f, 0.9f), Vector2.zero, Vector2.zero),
                transitionColor);
            if (statusStacked)
            {
                UIFactory.Label(row.transform, "AgeRisk", "⚠ 年结停场", 11, TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(ColStatus0, 0.06f), new Vector2(ColStatus1 + 0.06f, 0.46f), Vector2.zero, Vector2.zero),
                    UITheme.Warning);
            }

            // 左半区整行点击 → 飞机详情 n10（覆盖在操作按钮之下，不抢 Assign/Retire）
            string detailReg = ac.registration;
            var detailBtn = row.AddComponent<Button>();
            detailBtn.targetGraphic = row.GetComponent<Image>();
            detailBtn.onClick.AddListener(() =>
            {
                FleetNetContext.SelectedRegistration = detailReg;
                OpsSubpageContext.SelectedRegistration = detailReg;
                OpenFleetPage("n10_飞机详情_履历");
            });

            bool canRetire = ac.status != AircraftStatuses.Retired;
            string reg0 = ac.registration;
            if (!string.IsNullOrEmpty(transitionLabel))
            {
                // 在租的飞机：操作位改为「召回」，避免误分配航线
                var contract = TransitionBusinessLogic.ContractFor(GameState.Instance, reg0);
                bool recallable = contract != null && TransitionKinds.IsLease(contract.kind) && !contract.Recalling;
                if (recallable)
                {
                    UIFactory.Button(row.transform, "Recall", "召回", 
                        new UIFactory.Anchor(new Vector2(ColAction0, 0.12f), new Vector2(ColAction0 + 0.20f, 0.88f), Vector2.zero, Vector2.zero),
                        UITheme.Danger, () => OnRecall(reg0), 11);
                    UIFactory.Label(row.transform, "RecallHint", "交机 3 天 · 违约金 30%", 10, TextAlignmentOptions.MidlineLeft,
                        new UIFactory.Anchor(new Vector2(ColAction0 + 0.215f, 0.1f), new Vector2(ColAction1, 0.9f), Vector2.zero, Vector2.zero),
                        UITheme.TextMuted);
                }
                else
                {
                    UIFactory.Label(row.transform, "Busy", contract != null && contract.Recalling ? "交机流程中" : "任务执行中，不可召回",
                        11, TextAlignmentOptions.MidlineLeft,
                        new UIFactory.Anchor(new Vector2(ColAction0, 0.1f), new Vector2(ColAction1, 0.9f), Vector2.zero, Vector2.zero),
                        UITheme.TextMuted);
                }
            }
            else if (canRetire)
            {
                string reg = ac.registration;
                // 租赁机是「退租」（交还出租人、不回收残值），自有 / 二手才是「退役」（转售回收残值）——
                // 按钮上的数字与 FleetManager 入账用同一口径 LeaseReturnCost，不许分别算。
                bool leased = FleetManager.IsLeased(ac);
                float residual = FleetManager.CalcResidualValue(ac);
                float exitFee = leased ? FleetManager.LeaseReturnCost(GameState.Instance, ac) : 0f;
                string exitLabel = leased
                    ? (exitFee > 0.01f ? $"退租(−{exitFee:F0})" : "退租")
                    : $"退役(+{residual:F0})";
                bool tutorialTarget = AdvisorTutorialScript.IsAwaitingFleet()
                    && string.Equals(reg, AdvisorTutorialScript.RecommendedRegistration(), System.StringComparison.OrdinalIgnoreCase);

                string assignLabel = tutorialTarget ? "分配至深圳—北京" : "分配航线";
                Color assignColor = tutorialTarget ? UITheme.Success : UITheme.Accent;

                UIFactory.Button(row.transform, "Assign", assignLabel,
                    new UIFactory.Anchor(new Vector2(ColAction0, 0.12f), new Vector2(ColAction0 + 0.17f, 0.88f), Vector2.zero, Vector2.zero),
                    assignColor, () =>
                    {
                        if (AdvisorTutorialScript.IsAwaitingFleet())
                        {
                            OpsSubpageContext.SelectedRegistration = reg;
                            FindFirstObjectByType<AdvisorModalUI>()?.ContinueFromFleetAssign();
                            return;
                        }
                        OpsSubpageContext.SelectedRegistration = reg;
                        OpsPanelUI.OpenOpsPage("n98_航线航班环选择器");
                    }, 10);
                UIFactory.Button(row.transform, "Retire", exitLabel,
                    new UIFactory.Anchor(new Vector2(ColAction0 + 0.18f, 0.12f), new Vector2(ColAction1, 0.88f), Vector2.zero, Vector2.zero),
                    UITheme.Danger, () => OnRetire(reg), 10);
            }
            else
            {
                UIFactory.Label(row.transform, "Done", "已" + ac.ExitLabel, 12, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(ColAction0, 0.1f), new Vector2(ColAction1, 0.9f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
            }
        }

        private void OnRetire(string registration)
        {
            if (FleetManager.Instance == null) return;
            FleetManager.Instance.TryRetireAircraft(registration, out string msg);
            Toast(msg);
            Refresh();
        }

        private void OnRecall(string registration)
        {
            TransitionBusinessLogic.Recall(GameState.Instance, registration, out string msg);
            Toast(msg);
            Refresh();
        }

        private void OnSign(string offerId, string registration, long startTicks = 0)
        {
            TransitionBusinessLogic.Sign(GameState.Instance, offerId, registration, out string msg, true, startTicks);
            Toast(msg);
            Refresh();
        }

        private void OnCancelReservation(string contractId)
        {
            TransitionBusinessLogic.CancelReservation(GameState.Instance, contractId, out string msg);
            Toast(msg);
            Refresh();
        }

        // ————————————————————————————————————————————————
        // 过渡业务页：开局三架飞机在等许可证 / 等机组期间的盈利面板
        // ————————————————————————————————————————————————
        void RebuildTransition(GameState gs)
        {
            if (_transitionList == null) return;
            for (int i = _transitionList.childCount - 1; i >= 0; i--)
                DestroyImmediate(_transitionList.GetChild(i).gameObject);
            if (gs == null) return;

            TransitionBusinessLogic.Ensure(gs);

            float y = 4f;
            float daily = TransitionBusinessLogic.DailyIncome(gs);

            // ── 概要 ──
            var active = TransitionBusinessLogic.ActiveContracts(gs);
            var reserved = TransitionBusinessLogic.ReservedContracts(gs);
            float resTotal = TransitionBusinessLogic.ReservedTotalWan(gs);
            float resDeposit = TransitionBusinessLogic.ReservedDepositWan(gs);

            string tip = (active.Count + reserved.Count) == 0
                ? "　｜　还没有飞机出租：先把运力租出去止血"
                : "";
            var sum = UIFactory.Label(_transitionList, "TSum",
                $"过渡业务　在租 {active.Count} 架（日进 {daily:F2} 万）　·　预租 {reserved.Count} 份（总额 {resTotal:F0} 万，定金已收 {resDeposit:F1} 万）　·　" +
                $"净月烧钱 {gs.EffectiveMonthlyBurn:F0} 万　·　现金 {gs.cash:F0} 万{tip}",
                12, TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.Accent, FontStyles.Bold);
            UIFactory.PinFromTop(sum.gameObject, y, 26f, 0.01f, 0.98f);
            y += 30f;

            var hint = UIFactory.Label(_transitionList, "THint",
                "签约即收定金（20%），执行期逐日入账（60%），交机结算尾款（20%）。可把合同预约到未来日期（预租），当日先收定金、到期自动转入在租。",
                11, TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextMuted);
            UIFactory.PinFromTop(hint.gameObject, y, 20f, 0.01f, 0.98f);
            y += 26f;

            // ── 在租（执行中）表格 ──
            y = SectionHeader("在租（执行中）",
                active.Count > 0 ? $"{active.Count} 架占用中，日进 {daily:F2} 万" : "暂无 —— 下方挑一家客户签下", y);
            if (active.Count == 0)
            {
                y = EmptyRow("还没有飞机投入执行。三架飞机的维护费每天都在发生，闲置即是净亏损。", y);
            }
            else
            {
                y = TableHeader("ActHdr", new[] { "注册", "业务", "客户", "期限", "进度", "已收/待收尾款", "操作" },
                    ActiveCols, y);
                for (int i = 0; i < active.Count; i++)
                    y = ActiveRow(active[i], y, i % 2 == 1);
            }

            // ── 预租（已签约待生效）表格 ──
            y += 6f;
            y = SectionHeader("预租（已签约 · 待生效）",
                reserved.Count > 0 ? $"{reserved.Count} 份预约，定金已收 {resDeposit:F1} 万" : "暂无 —— 可把合同预约到今天以后的日期", y);
            if (reserved.Count == 0)
            {
                y = EmptyRow("没有未来日期的预约。预约机位可先锁定客户与定金，到期自动转入在租执行。", y);
            }
            else
            {
                y = TableHeader("ResHdr", new[] { "注册", "业务", "客户", "起始日", "天数", "总额", "定金（已收）", "尾款", "操作" },
                    ReservedCols, y);
                for (int i = 0; i < reserved.Count; i++)
                    y = ReservedRow(reserved[i], y, i % 2 == 1);
            }

            // ── 待接单 ──
            y += 6f;
            int offerCount = gs.transitionOffers != null ? gs.transitionOffers.Count : 0;
            y = SectionHeader("待接单", offerCount > 0 ? $"{offerCount} 个机会（包机/培训报价过期即作废）" : "暂无可接报价", y);

            if (offerCount == 0)
                y = EmptyRow("客户报价会在游戏时间推进中陆续出现（每天刷新一次）。", y);
            else
            {
                for (int i = 0; i < gs.transitionOffers.Count; i++)
                {
                    var o = gs.transitionOffers[i];
                    if (o == null) continue;
                    y = OfferCard(gs, o, y);
                }
            }

            // ── 解锁进度 ──
            y += 6f;
            y = SectionHeader("机组解锁进度", "", y);
            var unlock = UIFactory.Label(_transitionList, "TUnlock",
                TransitionBusinessLogic.UnlockChecklist(gs), 12, TextAlignmentOptions.TopLeft, UIFactory.Fill,
                UITheme.TextPrimary);
            UIFactory.PinFromTop(unlock.gameObject, y, 56f, 0.02f, 0.98f);
            y += 62f;

            UIFactory.SetScrollContentHeight(_transitionList, y + 10f);
        }

        // 表格列位（xMin/xMax 成对）
        /// <summary>预约（预租）按钮底色：中性灰，区别于「即日」的蓝色签约按钮。</summary>
        static readonly Color ReserveBtnGray = new Color(0.30f, 0.34f, 0.40f, 0.95f);

        static readonly float[] ActiveCols =
        {
            0.012f, 0.105f,   // 注册
            0.105f, 0.160f,   // 业务
            0.160f, 0.345f,   // 客户
            0.345f, 0.520f,   // 期限
            0.520f, 0.650f,   // 进度
            0.650f, 0.820f,   // 已收/尾款
            0.820f, 0.985f    // 操作
        };

        static readonly float[] ReservedCols =
        {
            0.012f, 0.100f,   // 注册
            0.100f, 0.155f,   // 业务
            0.155f, 0.360f,   // 客户
            0.360f, 0.480f,   // 起始日
            0.480f, 0.550f,   // 天数
            0.550f, 0.660f,   // 总额
            0.660f, 0.760f,   // 定金
            0.760f, 0.875f,   // 尾款
            0.875f, 0.985f    // 操作
        };

        /// <summary>表格表头：按列位铺出列名，底色略深以示分隔。</summary>
        float TableHeader(string key, string[] cols, float[] x, float yTop)
        {
            const float h = 22f;
            var band = UIFactory.Panel(_transitionList, "TH_" + key + yTop, UIFactory.Fill,
                new Color(0.05f, 0.10f, 0.19f, 0.85f));
            UIFactory.PinFromTop(band, yTop, h, 0.012f, 0.976f);
            var img = band.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
            int n = Mathf.Min(cols.Length, x.Length / 2);
            for (int i = 0; i < n; i++)
                UIFactory.Label(band.transform, "c" + i, cols[i], 10, TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(x[i * 2], 0f), new Vector2(x[i * 2 + 1], 1f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted, FontStyles.Bold);
            return yTop + h + 3f;
        }

        void Cell(Transform parent, string name, string text, float x0, float x1, float y0, float y1,
            Color color, int size = 11, FontStyles style = FontStyles.Normal,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            UIFactory.Label(parent, name, text, size, align,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero), color, style);
        }

        /// <summary>在租表一行。</summary>
        float ActiveRow(TransitionContract c, float yTop, bool zebra)
        {
            const float h = 46f;
            var row = UIFactory.Panel(_transitionList, "AR" + c.id, UIFactory.Fill,
                zebra ? new Color(0.09f, 0.16f, 0.27f, 0.9f) : UITheme.PanelDeep);
            UIFactory.PinFromTop(row, yTop, h, 0.012f, 0.976f);
            var bg = row.GetComponent<Image>();
            if (bg != null) bg.raycastTarget = false;

            float[] x = ActiveCols;
            Cell(row.transform, "Reg", c.aircraftReg, x[0], x[1], 0.48f, 0.95f, UITheme.TextPrimary, 12, FontStyles.Bold);
            Cell(row.transform, "Kind", TransitionKinds.Label(c.kind),
                x[2], x[3], 0.48f, 0.95f, UITheme.Accent, 12, FontStyles.Bold);
            Cell(row.transform, "Client", c.client, x[4], x[5], 0.48f, 0.95f, UITheme.TextPrimary);

            string state = c.Recalling
                ? $"召回中 · {c.recallDaysLeft} 天后交机"
                : TransitionBusinessLogic.FormatTermRange(GameState.Instance, c);
            Cell(row.transform, "Term", state, x[6], x[7], 0.48f, 0.95f,
                c.Recalling ? UITheme.Warning : UITheme.TextMuted);
            Cell(row.transform, "Rate", $"{c.rateWanPerDay:F2} 万/天　{c.daysUsed}/{c.totalDays} 天",
                x[6], x[7], 0.06f, 0.46f, UITheme.TextMuted, 10);

            // 进度条
            var track = UIFactory.Panel(row.transform, "Track",
                new UIFactory.Anchor(new Vector2(x[8], 0.30f), new Vector2(x[9] - 0.01f, 0.66f), Vector2.zero, Vector2.zero),
                new Color(0.10f, 0.16f, 0.26f, 0.95f));
            var trackImg = track.GetComponent<Image>();
            if (trackImg != null) trackImg.raycastTarget = false;
            float w = Mathf.Clamp01(c.Progress01);
            if (w > 0.001f)
            {
                var fill = UIFactory.Panel(track.transform, "Fill",
                    new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(w, 1f), Vector2.zero, Vector2.zero),
                    c.Recalling ? UITheme.Warning : UITheme.Success);
                var fillImg = fill.GetComponent<Image>();
                if (fillImg != null) fillImg.raycastTarget = false;
            }
            Cell(row.transform, "Pct", $"{Mathf.RoundToInt(c.Progress01 * 100f)}%",
                x[8], x[9] - 0.01f, 0.68f, 0.98f, UITheme.TextMuted, 10);

            Cell(row.transform, "Money", $"收 {c.ReceivedWan:F1}　尾 {c.PendingBalanceWan:F1}",
                x[10], x[11], 0.48f, 0.95f, UITheme.TextMuted, 10);
            Cell(row.transform, "Deposit", $"定金 {c.depositWan:F1}（{(c.depositPaid ? "已收" : "待收")}）",
                x[10], x[11], 0.06f, 0.46f, UITheme.TextMuted, 10);

            bool recallable = TransitionKinds.IsLease(c.kind) && !c.Recalling;
            if (recallable)
                UIFactory.Button(row.transform, "Recall", "提前召回",
                    new UIFactory.Anchor(new Vector2(x[12], 0.18f), new Vector2(x[13], 0.82f), Vector2.zero, Vector2.zero),
                    UITheme.Danger, () => OnRecall(c.aircraftReg), 10);
            else
                Cell(row.transform, "NoRecall",
                    TransitionKinds.IsLease(c.kind) ? "交机流程中" : "任务不可召回",
                    x[12], x[13], 0f, 1f, UITheme.TextMuted, 10, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            return yTop + h + 3f;
        }

        /// <summary>预租表一行。</summary>
        float ReservedRow(TransitionContract c, float yTop, bool zebra)
        {
            const float h = 44f;
            var row = UIFactory.Panel(_transitionList, "RR" + c.id, UIFactory.Fill,
                zebra ? new Color(0.09f, 0.16f, 0.27f, 0.9f) : UITheme.PanelDeep);
            UIFactory.PinFromTop(row, yTop, h, 0.012f, 0.976f);
            var bg = row.GetComponent<Image>();
            if (bg != null) bg.raycastTarget = false;

            float[] x = ReservedCols;
            Cell(row.transform, "Reg", c.aircraftReg, x[0], x[1], 0.48f, 0.95f, UITheme.TextPrimary, 12, FontStyles.Bold);
            Cell(row.transform, "Kind", TransitionKinds.Label(c.kind),
                x[2], x[3], 0.48f, 0.95f, UITheme.Accent, 12, FontStyles.Bold);
            Cell(row.transform, "Client", c.client, x[4], x[5], 0.48f, 0.95f, UITheme.TextPrimary);

            long now = GameState.Instance != null ? GameState.Instance.SimUtc.Ticks : 0;
            int daysTo = c.startUtcTicks > now
                ? (int)Math.Ceiling((c.startUtcTicks - now) / (double)TimeSpan.TicksPerDay) : 0;
            Cell(row.transform, "Start", $"{TransitionBusinessLogic.DateFormat(GameState.Instance, c.startUtcTicks)}（{daysTo} 天后）",
                x[6], x[7], 0.48f, 0.95f, UITheme.Warning);
            Cell(row.transform, "Days", $"{c.totalDays} 天", x[8], x[9], 0.48f, 0.95f, UITheme.TextMuted);
            Cell(row.transform, "Total", $"{c.TotalWan:F0} 万", x[10], x[11], 0.48f, 0.95f, UITheme.TextPrimary);
            Cell(row.transform, "Deposit", $"{c.depositWan:F1} 万", x[12], x[13], 0.48f, 0.95f,
                c.depositPaid ? UITheme.Success : UITheme.TextMuted, 11, FontStyles.Bold);
            Cell(row.transform, "Balance", $"{c.balanceWan:F1} 万", x[14], x[15], 0.48f, 0.95f, UITheme.TextMuted);
            Cell(row.transform, "RateNote", $"{c.rateWanPerDay:F2} 万/天　签约 {TransitionBusinessLogic.DateFormat(GameState.Instance, c.signedUtcTicks)}",
                x[14], x[17], 0.06f, 0.46f, UITheme.TextMuted, 10);

            UIFactory.Button(row.transform, "Cancel", "取消预约",
                new UIFactory.Anchor(new Vector2(x[16], 0.18f), new Vector2(x[17], 0.82f), Vector2.zero, Vector2.zero),
                UITheme.Danger, () => OnCancelReservation(c.id), 10);

            return yTop + h + 3f;
        }

        float SectionHeader(string title, string note, float yTop)
        {
            var band = UIFactory.Panel(_transitionList, "Sec" + title + yTop, UIFactory.Fill,
                new Color(0.06f, 0.12f, 0.22f, 0.75f));
            UIFactory.PinFromTop(band, yTop, 26f, 0.01f, 0.98f);
            var img = band.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
            UIFactory.Label(band.transform, "T", title, 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.01f, 0f), new Vector2(0.30f, 1f), Vector2.zero, Vector2.zero),
                UITheme.Accent, FontStyles.Bold);
            if (!string.IsNullOrEmpty(note))
                UIFactory.Label(band.transform, "N", note, 11, TextAlignmentOptions.MidlineRight,
                    new UIFactory.Anchor(new Vector2(0.30f, 0f), new Vector2(0.99f, 1f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
            return yTop + 30f;
        }

        float EmptyRow(string text, float yTop)
        {
            var lab = UIFactory.Label(_transitionList, "Empty" + yTop, text, 11,
                TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextMuted);
            UIFactory.PinFromTop(lab.gameObject, yTop, 30f, 0.03f, 0.97f);
            return yTop + 34f;
        }

        float OfferCard(GameState gs, TransitionOffer o, float yTop)
        {
            const float h = 84f;
            var card = UIFactory.Panel(_transitionList, "O" + o.id, UIFactory.Fill,
                new Color(0.07f, 0.13f, 0.22f, 0.9f));
            UIFactory.PinFromTop(card, yTop, h, 0.015f, 0.975f);
            var bg = card.GetComponent<Image>();
            if (bg != null) bg.raycastTarget = false;

            string gate = TransitionBusinessLogic.GateReason(gs, o.kind);
            bool locked = !string.IsNullOrEmpty(gate);
            int days = TransitionKinds.IsLease(o.kind) ? TransitionBusinessLogic.LeaseDays : o.totalDays;

            UIFactory.Label(card.transform, "Glyph", TransitionKinds.Glyph(o.kind), 15,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.01f, 0.55f), new Vector2(0.05f, 0.95f), Vector2.zero, Vector2.zero),
                locked ? UITheme.TextMuted : UITheme.Accent, FontStyles.Bold);

            UIFactory.Label(card.transform, "Who",
                $"{TransitionKinds.Label(o.kind)} · {o.client}", 13, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.48f), new Vector2(0.46f, 0.98f), Vector2.zero, Vector2.zero),
                locked ? UITheme.TextMuted : UITheme.TextPrimary, FontStyles.Bold);

            float baseTotal = o.rateWanPerDay * days;
            TransitionBusinessLogic.SplitTotal(baseTotal, out float deposit, out float balance, out _);
            string validity = o.expireUtcTicks > 0 ? $"报价剩 {o.DaysLeftUtc(gs.SimUtc.Ticks)} 天" : "长期有效";
            string term = TransitionBusinessLogic.FormatTermRange(gs, days, 0, 0);
            UIFactory.Label(card.transform, "Terms",
                $"{o.rateWanPerDay:F2} 万/天基准　期限 {term}　·　总额 {baseTotal:F0} 万（定金 {deposit:F1} / 尾款 {balance:F1}）　·　{validity}",
                10, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.06f, 0.08f), new Vector2(0.52f, 0.46f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            if (locked)
            {
                UIFactory.Label(card.transform, "Lock", $"🔒 {gate}", 12, TextAlignmentOptions.MidlineRight,
                    new UIFactory.Anchor(new Vector2(0.52f, 0.10f), new Vector2(0.99f, 0.90f), Vector2.zero, Vector2.zero),
                    UITheme.Warning);
                return yTop + h + 5f;
            }

            UIFactory.Label(card.transform, "PickLabel", "选机位签约（按钮下方为该机实际日费率）", 10,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.52f, 0.66f), new Vector2(0.99f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var opts = TransitionBusinessLogic.BookingOptions(gs, days);
            if (opts.Count == 0)
            {
                UIFactory.Label(card.transform, "NoAc", "无可承接机位：需先从航线抽机或等合同交机", 11,
                    TextAlignmentOptions.MidlineRight,
                    new UIFactory.Anchor(new Vector2(0.50f, 0.08f), new Vector2(0.99f, 0.62f), Vector2.zero, Vector2.zero),
                    UITheme.Warning);
                return yTop + h + 5f;
            }

            int show = Mathf.Min(opts.Count, 4);
            float x0 = 0.52f;
            float x1 = 0.99f;
            float step = (x1 - x0) / show;
            for (int i = 0; i < show; i++)
            {
                var op = opts[i];
                string reg = op.aircraft.registration;
                string oid = o.id;
                long startTicks = op.startTicks;
                float rate = TransitionBusinessLogic.RateFor(o, op.aircraft);
                // 两行：上行「机号 · 起租日」下行「该机实际日费率」，避免数字被误读为编号或日期
                string when = op.future ? $"{TransitionBusinessLogic.ShortDate(gs, startTicks)}起" : "即日";
                string cap = $"{reg} · {when}\n{rate:F2} 万/天";
                UIFactory.Button(card.transform, "Sign" + reg + startTicks,
                    cap,
                    new UIFactory.Anchor(new Vector2(x0 + i * step + 0.004f, 0.10f),
                        new Vector2(x0 + (i + 1) * step - 0.004f, 0.62f), Vector2.zero, Vector2.zero),
                    op.future ? ReserveBtnGray : UITheme.Accent, () => OnSign(oid, reg, startTicks), 10);
            }
            return yTop + h + 5f;
        }

        private void Toast(string msg)
        {
            if (_toastText != null) _toastText.text = msg ?? "";
            Debug.Log("[FleetUI] " + msg);
        }
    }
}
