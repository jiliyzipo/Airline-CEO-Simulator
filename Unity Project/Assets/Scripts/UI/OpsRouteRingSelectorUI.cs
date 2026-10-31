using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// n98 航线分配：给选定飞机分配航线（可在一条航线上加多班、选时段）。
    ///
    /// ★2026-10-30：原先还有「按航班环分配」页签，让 CEO 把已校验的环手工绑到机号。
    /// 航班环改为运营部后台编成（<see cref="OpsRotationBoardUI"/>），这一侧不再保留任何拼环/绑环入口：
    /// 「哪些飞机跑哪些环」由运营部按条件算，CEO 只批收件箱里的方案。
    /// </summary>
    public class OpsRouteRingSelectorUI : MonoBehaviour
    {
        Transform _listRoot;
        Transform _aircraftStrip;
        TextMeshProUGUI _header;
        TextMeshProUGUI _hint;
        string _selectedReg;
        readonly List<GameObject> _rows = new List<GameObject>();
        readonly List<GameObject> _chips = new List<GameObject>();

        // 加航班时刻选择器（★2026-10-15：由「点时段」升级为「选波次 → 点具体时刻」）
        GameObject _slotRoot;
        TextMeshProUGUI _slotTitle;
        Transform _slotClockRoot;
        int _slotBand;
        readonly List<GameObject> _slotClockButtons = new List<GameObject>();
        string _slotRouteId;
        /// <summary>一次性的操作结果提示（在 Refresh 里显示一次即清），用于「已呈报 / 已在待批 / 缺槽」回显。</summary>
        string _notice;

        public static OpsRouteRingSelectorUI Ensure(Transform card)
        {
            if (card == null) return null;
            var existing = card.GetComponentInChildren<OpsRouteRingSelectorUI>(true);
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                if (existing._listRoot == null)
                {
                    // 旧 Prefab 烘焙组件：运行时字段未初始化，清掉旧子节点后重建
                    for (int i = existing.transform.childCount - 1; i >= 0; i--)
                        Destroy(existing.transform.GetChild(i).gameObject);
                    existing.Build(existing.transform);
                }
                existing.Refresh();
                return existing;
            }

            var root = UIFactory.Panel(card, "RouteRingSelector",
                new UIFactory.Anchor(new Vector2(0.04f, 0.14f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0.01f), rounded: false);
            var img = root.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            var ui = root.AddComponent<OpsRouteRingSelectorUI>();
            ui.Build(root.transform);
            ui.Refresh();
            return ui;
        }

        void Build(Transform root)
        {
            _header = UIFactory.Label(root, "Header", "分配航线 / 航班环", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.93f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            _aircraftStrip = UIFactory.Panel(root, "AircraftStrip",
                new UIFactory.Anchor(new Vector2(0f, 0.86f), new Vector2(1f, 0.93f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0), rounded: false).transform;
            var stripImg = _aircraftStrip.GetComponent<Image>();
            if (stripImg != null) stripImg.raycastTarget = false;

            UIFactory.Label(root, "TabRoute", "按航线分配 · 选机号后为它加航班（呈报 CEO 批准后落班）", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.77f), new Vector2(0.66f, 0.85f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            UIFactory.Button(root, "OpenBuilder", "查看航班环编成",
                new UIFactory.Anchor(new Vector2(0.7f, 0.77f), new Vector2(1f, 0.85f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => OpsPanelUI.OpenOpsPage("n36_航班环构建器"), 12);

            _hint = UIFactory.Label(root, "Hint", "", 13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.69f), new Vector2(1f, 0.77f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            _hint.textWrappingMode = TextWrappingModes.Normal;

            var listHost = UIFactory.Panel(root, "PickList",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0.68f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            listHost.AddComponent<ScrollRect>();
            listHost.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(listHost.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 6;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = listHost.GetComponent<ScrollRect>();
            scroll.content = crt;
            scroll.viewport = listHost.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.vertical = true;
            _listRoot = content.transform;

            BuildSlotPicker(root);
        }

        void BuildSlotPicker(Transform root)
        {
            _slotRoot = UIFactory.Panel(root, "SlotPicker",
                new UIFactory.Anchor(new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.70f), Vector2.zero, Vector2.zero),
                new Color(0.03f, 0.07f, 0.13f, 0.96f));
            UIFactory.AddBorder(_slotRoot, UITheme.Accent, new Vector2(1.2f, -1.2f));
            _slotTitle = UIFactory.Label(_slotRoot.transform, "SlotTitle",
                "选择时刻 · 呈报加机（CEO 批准后落班）", 15, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.90f), new Vector2(0.97f, 0.99f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            // 第一层：四个需求波（早/午/晚/夜）—— 只作**筛选**，不是槽的身份。
            // ★2026-10-15：时刻槽精确到时刻，所以这里点完波次还要点具体时刻，见下方时刻网格。
            for (int i = 0; i < ScheduleSlots.Labels.Length; i++)
            {
                int b = i;
                float x0 = 0.03f + i * 0.235f;
                UIFactory.Button(_slotRoot.transform, "Band_" + b,
                    $"{ScheduleSlots.Labels[b]} {ScheduleSlots.FormatClock(ScheduleSlots.WindowStart[b])}-"
                    + $"{ScheduleSlots.FormatClock(ScheduleSlots.WindowEnd[b])}",
                    new UIFactory.Anchor(new Vector2(x0, 0.80f), new Vector2(x0 + 0.22f, 0.89f), Vector2.zero, Vector2.zero),
                    UITheme.PanelDeep, () => { _slotBand = b; FillSlotClocks(); }, 12);
            }

            // 第二层：该波 30 分钟网格上的**每一个时刻**，各自带「能不能排」的判据。
            var host = UIFactory.Panel(_slotRoot.transform, "ClockList",
                new UIFactory.Anchor(new Vector2(0.03f, 0.10f), new Vector2(0.97f, 0.78f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            host.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(host.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;
            var grid = content.GetComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(6, 6, 6, 6);
            grid.spacing = new Vector2(6, 6);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.cellSize = new Vector2(150, 34);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _slotClockRoot = content.transform;

            UIFactory.Button(_slotRoot.transform, "SlotCancel", "取消",
                new UIFactory.Anchor(new Vector2(0.03f, 0.01f), new Vector2(0.24f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, HideSlotPicker, 13);
            _slotRoot.SetActive(false);
        }

        /// <summary>按当前波次重建「时刻 → 可用性」按钮网格（每次打开选择器/切换波次时重建）。</summary>
        void FillSlotClocks()
        {
            if (_slotClockRoot == null) return;
            for (int i = _slotClockButtons.Count - 1; i >= 0; i--)
                if (_slotClockButtons[i] != null) Destroy(_slotClockButtons[i]);
            _slotClockButtons.Clear();

            var gs = GameState.Instance;
            var route = FindRoute(gs, _slotRouteId);
            if (route != null) route.EnsureFlights();
            var clocks = ScheduleSlots.SlotGridLabels(_slotBand);
            for (int i = 0; i < clocks.Count; i++)
            {
                string clock = clocks[i];
                bool flying = route != null && FlightProposalLogic.HasFlight(route, _selectedReg, clock);
                bool can = route != null && RoutePermitLogic.CanScheduleClock(gs, route, clock, true);
                string mark = flying ? "该机已执飞"
                    : can ? "可排（两端有槽）"
                    : RoutePermitLogic.SlotPoolFull(gs, route != null ? route.origin : gs?.hubIata, ScheduleSlots.BandOfClock(clock))
                        ? "缺槽 · 池满"
                        : "缺槽 · 运营部可申请";
                var btn = UIFactory.Button(_slotClockRoot, "Clock_" + clock.Replace(":", ""),
                    $"{clock}　{mark}",
                    new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                    flying ? UITheme.PanelDeep : can ? UITheme.Accent : UITheme.NavIdle,
                    () => ConfirmAddFlight(clock), 12);
                _slotClockButtons.Add(btn.gameObject);
            }
            if (_slotTitle != null)
            {
                string bandText = $"{ScheduleSlots.Labels[_slotBand]} "
                    + $"{ScheduleSlots.FormatClock(ScheduleSlots.WindowStart[_slotBand])}-{ScheduleSlots.FormatClock(ScheduleSlots.WindowEnd[_slotBand])}";
                _slotTitle.text = $"选择时刻 · 呈报加机（CEO 批准后落班）　{bandText}　"
                    + $"（{clocks.Count} 个时刻，各自独立占槽）";
            }
        }

        static RouteData FindRoute(GameState gs, string routeId)
        {
            if (gs?.activeRoutes == null || string.IsNullOrEmpty(routeId)) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
                if (gs.activeRoutes[i] != null && gs.activeRoutes[i].routeId == routeId) return gs.activeRoutes[i];
            return null;
        }

        void ResolveSelection(GameState gs)
        {
            // 外部显式指定（如机队「分配航线」）优先；否则沿用上次选择；再回退到首架
            string ext = OpsSubpageContext.SelectedRegistration;
            if (!string.IsNullOrEmpty(ext))
                _selectedReg = ext;
            if (string.IsNullOrEmpty(_selectedReg) && gs?.fleet != null && gs.fleet.Count > 0)
                _selectedReg = gs.fleet[0].registration;

            // 这里**只**修「选中的机号根本不在机队里」（老存档残留 / 已退役退租后的陈旧引用），
            // 不再顺手把「被过渡业务占用的飞机」偷偷换成别的。
            // 旧写法一旦发现选中机不可排班就静默切到第一架可用机，于是玩家点 B-2006 时
            // 机号条还停在 B-2006（高亮作废）、下面却是另一架飞机的航线，或干脆什么都不动 ——
            // 点击"没反应"的真相是：选择被改掉了，而玩家不知道。
            if (TransitionBusinessLogic.FindFleetAircraft(gs, _selectedReg) != null) return;
            if (gs?.fleet == null) return;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || !string.IsNullOrEmpty(a.ExitLabel)) continue;
                _selectedReg = a.registration;
                OpsSubpageContext.SelectedRegistration = _selectedReg;
                return;
            }
        }

        public void Refresh()
        {
            var gs = GameState.Instance;
            Debug.Log($"[n98] Refresh routes={(gs != null ? gs.activeRoutes?.Count ?? -1 : -1)} fleet={(gs != null ? gs.fleet?.Count ?? -1 : -1)} reg={_selectedReg}");
            if (FleetManager.Instance != null) FleetManager.Instance.EnsureStarterFleet();
            ResolveSelection(gs);

            string blocked = TransitionBusinessLogic.ScheduleBlockReason(gs, _selectedReg);
            if (_header != null)
                _header.text = $"航线分配 · 机号 {_selectedReg}";
            if (_hint != null)
            {
                // 顺序：一次性操作结果（呈报成功 / 已在待批 / 缺槽被拦）> 不可排班原因 > 常驻说明。
                // 不可排班原因必须优先于常驻说明，否则「为什么点不动」会被下方那句通用文案盖掉。
                _hint.text = !string.IsNullOrEmpty(_notice) ? _notice
                    : !string.IsNullOrEmpty(blocked) ? blocked + "　下方为该机占用详情；要给定期航班排班，请另选一架在册飞机。"
                    : "点飞机切换执飞飞机；点航线「＋加航班」**选具体时刻**（如 06:30 / 07:00，各占一条槽），"
                      + "由运营部呈报 CEO，批准后在收件箱落班。整机成环由运营部在后台编成，本页不管拼环。";
                _notice = null;
            }

            // 换机 / 选中不可排班的飞机时，之前打开的时段选择器要收起来：
            // 它记着上一条航线的 routeId，留着会让下一班的归属变成谜。
            HideSlotPicker();

            RebuildAircraftStrip(gs);
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i] != null) Destroy(_rows[i]);
            _rows.Clear();
            if (_listRoot == null) return;

            if (!string.IsNullOrEmpty(blocked))
            {
                AddBlockedCard("不可排班 · " + blocked, TransitionBusinessLogic.ScheduleBlockDetail(gs, _selectedReg));
                return;
            }

            FillRoutes(gs, _selectedReg);
        }

        void RebuildAircraftStrip(GameState gs)
        {
            for (int i = 0; i < _chips.Count; i++)
                if (_chips[i] != null) Destroy(_chips[i]);
            _chips.Clear();
            if (_aircraftStrip == null) return;

            var fleet = gs != null ? gs.fleet : null;
            if (fleet == null || fleet.Count == 0)
            {
                var empty = UIFactory.Label(_aircraftStrip, "NoAc", "暂无飞机（请先购买飞机）", 12,
                    TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                _chips.Add(empty.gameObject);
                return;
            }

            // 退役与已投入过渡业务（干租/湿租/包机/培训）的飞机不可排班
            var usable = new List<Aircraft>();
            for (int i = 0; i < fleet.Count; i++)
            {
                var a = fleet[i];
                if (a == null || !a.isActive || a.status == AircraftStatuses.Retired) continue;
                usable.Add(a);
            }
            if (usable.Count == 0)
            {
                var empty = UIFactory.Label(_aircraftStrip, "NoAc", "暂无可用飞机", 12,
                    TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                _chips.Add(empty.gameObject);
                return;
            }

            // 当前选中的飞机不可排班（退役 / 已投入过渡业务）时也照样留在条上并可点击 ——
            // 点它是为了看「为什么不能排班」，禁用按钮只会让点击毫无反馈（CEO 2026-10-13 反馈）。
            int n = Mathf.Min(usable.Count, 8);
            for (int i = 0; i < n; i++)
            {
                var ac = usable[i];
                if (ac == null) continue;
                string reg = ac.registration;
                float x0 = i * (1f / n);
                float x1 = (i + 1) * (1f / n) - 0.004f;
                bool selected = string.Equals(reg, _selectedReg, System.StringComparison.OrdinalIgnoreCase);
                string busy = TransitionBusinessLogic.BusyLabel(gs, reg);
                bool locked = !string.IsNullOrEmpty(busy);
                var chip = UIFactory.Button(_aircraftStrip, "Chip_" + reg,
                    locked ? reg + " " + busy : reg + (selected ? " ●" : ""),
                    new UIFactory.Anchor(new Vector2(x0, 0.06f), new Vector2(x1, 0.94f), Vector2.zero, Vector2.zero),
                    selected ? (locked ? UITheme.Warning : UITheme.Success) : UITheme.PanelDeep,
                    () => { _selectedReg = reg; OpsSubpageContext.SelectedRegistration = reg; Refresh(); },
                    locked ? 10 : 12);
                _chips.Add(chip.gameObject);
            }
        }

        void FillRoutes(GameState gs, string reg)
        {
            int n = gs?.activeRoutes?.Count ?? 0;
            if (n == 0)
            {
                AddNote("暂无航线。请先在运营部「开通航线」（或点右上「开通航线」→ 决策批准）。");
                return;
            }

            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                r.EnsureFlights();
                string routeId = r.routeId;
                string flightInfo = r.FlightCount > 1
                    ? $"{r.FlightCount} 班（" + JoinSlots(r) + "）"
                    : $"1 班（{r.flights[0].depTime}）";
                bool typeOk = true;
                if (gs.fleet != null)
                {
                    for (int f = 0; f < gs.fleet.Count; f++)
                    {
                        if (gs.fleet[f] != null && string.Equals(gs.fleet[f].registration, reg, System.StringComparison.OrdinalIgnoreCase))
                        {
                            typeOk = string.IsNullOrEmpty(r.aircraftType) ||
                                     string.Equals(gs.fleet[f].type, r.aircraftType, StringComparison.OrdinalIgnoreCase);
                            break;
                        }
                    }
                }

                string label =
                    $"{r.origin}→{r.destination}  {r.flightNumber}  {r.aircraftType}\n" +
                    $"{flightInfo} · {(typeOk ? "机型匹配" : "⚠ 机型可能不匹配")} · 日利 {r.dailyProfit:F1} 万";
                AddRow(label, "＋加航班（报批）", () => OpenSlotPicker(routeId));
            }
        }

        /// <summary>一条航线现有班次的「时刻」一览（★2026-10-15：只写时刻，不写「早班」这类笼统说法）。</summary>
        static string JoinSlots(RouteData r)
        {
            var set = new HashSet<string>();
            for (int i = 0; i < r.flights.Count; i++)
            {
                string s = string.IsNullOrEmpty(r.flights[i].depTime)
                    ? ScheduleSlots.BandNameToClock(r.flights[i].slotLabel)
                    : r.flights[i].depTime;
                set.Add(s);
            }
            return string.Join(" · ", set);
        }

        void OpenSlotPicker(string routeId)
        {
            _slotRouteId = routeId;
            if (_slotRoot != null) _slotRoot.SetActive(true);
            // 默认落在「该线已有班次所属的波次」上：CEO 想加密哪一波，多半就是现在有班的这一波。
            var route = FindRoute(GameState.Instance, routeId);
            _slotBand = route != null && route.flights != null && route.flights.Count > 0
                ? ScheduleSlots.BandOfClock(route.flights[0].depTime)
                : 0;
            FillSlotClocks();
        }

        void HideSlotPicker()
        {
            _slotRouteId = null;
            if (_slotRoot != null) _slotRoot.SetActive(false);
        }

        /// <summary>
        /// 点**时刻** = 让运营部就此呈报 CEO，**不在这里落班**（2026-10-13 裁定）。
        /// 落班发生在收件箱批准之后（InboxManager.ApproveAssignFlight → FlightProposalLogic.Apply），
        /// 故本函数只负责校验与呈报，航线的班次表在这一步不动。
        /// ★2026-10-15：参数由时段名改为**具体时刻** `"HH:mm"`（如 06:30 / 07:00 —— 各是各的槽）。
        /// </summary>
        void ConfirmAddFlight(string clock)
        {
            var gs = GameState.Instance;
            if (gs == null || string.IsNullOrEmpty(_selectedReg) || string.IsNullOrEmpty(_slotRouteId))
            {
                HideSlotPicker();
                return;
            }

            // 兜底：呈报前复核该机此刻是否仍可排班（期间可能刚签入过渡业务），
            // 免得把「一架不能飞的飞机」报给 CEO 批准。
            string blocked = TransitionBusinessLogic.ScheduleBlockReason(gs, _selectedReg);
            if (!string.IsNullOrEmpty(blocked))
            {
                HideSlotPicker();
                _notice = "未呈报：" + blocked;
                Refresh();
                return;
            }

            var r = FindRoute(gs, _slotRouteId);
            if (r == null) { HideSlotPicker(); return; }

            HideSlotPicker();
            InboxManager.QueueAssignFlight(gs, r, _selectedReg, clock, out string msg);
            _notice = msg;
            Refresh();
        }

        void AddNote(string text)
        {
            var label = UIFactory.Label(_listRoot, "Note", text, 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.verticalAlignment = VerticalAlignmentOptions.Top;
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
            _rows.Add(label.gameObject);
        }

        /// <summary>不可排班时的说明卡：标题 + 明细。行数决定高度，免得长明细被压成一行。</summary>
        void AddBlockedCard(string title, string detail)
        {
            var card = UIFactory.Panel(_listRoot, "Blocked",
                new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                new Color(0.11f, 0.08f, 0.05f, 0.85f));
            UIFactory.AddBorder(card, UITheme.Warning, new Vector2(1f, -1f));

            int lines = 1;
            for (int i = 0; i < detail.Length; i++) if (detail[i] == '\n') lines++;
            card.AddComponent<LayoutElement>().preferredHeight = 30 + lines * 20 + 16;

            var t = UIFactory.Label(card.transform, "T", title, 14, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.66f), new Vector2(0.97f, 0.95f), Vector2.zero, Vector2.zero),
                UITheme.Warning, FontStyles.Bold);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.verticalAlignment = VerticalAlignmentOptions.Top;

            var b = UIFactory.Label(card.transform, "B", detail, 12, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.64f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            b.textWrappingMode = TextWrappingModes.Normal;
            b.verticalAlignment = VerticalAlignmentOptions.Top;
            _rows.Add(card);
        }

        void AddRow(string text, string actionLabel, UnityEngine.Events.UnityAction onClick)
        {
            var row = UIFactory.Panel(_listRoot, "Row",
                new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                UITheme.Panel);
            row.AddComponent<LayoutElement>().preferredHeight = 78;
            var label = UIFactory.Label(row.transform, "Txt", text, 13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.14f), new Vector2(0.74f, 0.86f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.verticalAlignment = VerticalAlignmentOptions.Top;
            UIFactory.Button(row.transform, "Assign", actionLabel,
                new UIFactory.Anchor(new Vector2(0.78f, 0.28f), new Vector2(0.98f, 0.72f), Vector2.zero, Vector2.zero),
                UITheme.Accent, onClick, 12);
            _rows.Add(row);
        }
    }
}
