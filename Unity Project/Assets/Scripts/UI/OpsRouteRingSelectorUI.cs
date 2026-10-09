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
    /// n98 航线/航班环选择器：给选定飞机分配航线（可在一条航线上加多班、选时段），
    /// 或查看已校验航班环并跳转构建器。
    /// </summary>
    public class OpsRouteRingSelectorUI : MonoBehaviour
    {
        Transform _listRoot;
        Transform _aircraftStrip;
        TextMeshProUGUI _header;
        TextMeshProUGUI _hint;
        bool _ringMode;
        string _selectedReg;
        readonly List<GameObject> _rows = new List<GameObject>();
        readonly List<GameObject> _chips = new List<GameObject>();

        // 加航班时段选择器
        GameObject _slotRoot;
        string _slotRouteId;

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

            UIFactory.Button(root, "TabRoute", "按航线分配",
                new UIFactory.Anchor(new Vector2(0f, 0.77f), new Vector2(0.32f, 0.85f), Vector2.zero, Vector2.zero),
                UITheme.Accent, () => { _ringMode = false; Refresh(); }, 13);
            UIFactory.Button(root, "TabRing", "按航班环分配",
                new UIFactory.Anchor(new Vector2(0.34f, 0.77f), new Vector2(0.68f, 0.85f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { _ringMode = true; Refresh(); }, 13);
            UIFactory.Button(root, "OpenBuilder", "打开航班环构建器",
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
                new UIFactory.Anchor(new Vector2(0.08f, 0.20f), new Vector2(0.92f, 0.64f), Vector2.zero, Vector2.zero),
                new Color(0.03f, 0.07f, 0.13f, 0.96f));
            UIFactory.AddBorder(_slotRoot, UITheme.Accent, new Vector2(1.2f, -1.2f));
            UIFactory.Label(_slotRoot.transform, "SlotTitle", "选择时段 · 添加航班", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.82f), new Vector2(0.7f, 0.94f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            for (int i = 0; i < ScheduleSlots.Labels.Length; i++)
            {
                string label = ScheduleSlots.Labels[i];
                float y0 = 0.58f - i * 0.14f;
                UIFactory.Button(_slotRoot.transform, "Slot_" + label,
                    $"{label} · {ScheduleSlots.DepTimes[i]}（需求 ×{ScheduleSlots.Demands[i]:0.00}）",
                    new UIFactory.Anchor(new Vector2(0.06f, y0), new Vector2(0.80f, y0 + 0.11f), Vector2.zero, Vector2.zero),
                    UITheme.Accent, () => ConfirmAddFlight(label), 13);
            }
            UIFactory.Button(_slotRoot.transform, "SlotCancel", "取消",
                new UIFactory.Anchor(new Vector2(0.06f, 0.04f), new Vector2(0.30f, 0.14f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, HideSlotPicker, 13);
            _slotRoot.SetActive(false);
        }

        void ResolveSelection(GameState gs)
        {
            // 外部显式指定（如机队「分配航线」）优先；否则沿用上次选择；再回退到首架
            string ext = OpsSubpageContext.SelectedRegistration;
            if (!string.IsNullOrEmpty(ext))
                _selectedReg = ext;
            if (string.IsNullOrEmpty(_selectedReg) && gs?.fleet != null && gs.fleet.Count > 0)
                _selectedReg = gs.fleet[0].registration;

            // 退役 / 已投入过渡业务（干租/湿租/包机/培训）的飞机不可排班，自动切到第一架可用飞机
            if (IsSelectable(gs, _selectedReg)) return;
            if (gs?.fleet == null) return;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || !IsSelectable(gs, a.registration)) continue;
                _selectedReg = a.registration;
                OpsSubpageContext.SelectedRegistration = _selectedReg;
                return;
            }
        }

        /// <summary>该机号能否排班：在册、未退役、未被过渡业务占用。</summary>
        static bool IsSelectable(GameState gs, string reg)
        {
            if (string.IsNullOrEmpty(reg) || gs?.fleet == null) return false;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || !string.Equals(a.registration, reg, StringComparison.OrdinalIgnoreCase)) continue;
                if (!a.isActive || a.status == AircraftStatuses.Retired) return false;
                if (TransitionBusinessLogic.IsBusy(gs, reg)) return false;
                return true;
            }
            return false;
        }

        public void Refresh()
        {
            var gs = GameState.Instance;
            Debug.Log($"[n98] Refresh routes={(gs != null ? gs.activeRoutes?.Count ?? -1 : -1)} fleet={(gs != null ? gs.fleet?.Count ?? -1 : -1)} reg={_selectedReg}");
            if (FleetManager.Instance != null) FleetManager.Instance.EnsureStarterFleet();
            ResolveSelection(gs);

            if (_header != null)
                _header.text = $"航线/航班环选择器 · 机号 {_selectedReg}";
            if (_hint != null)
            {
                _hint.text = _ringMode
                    ? "选择已校验的航班环，将两段航线绑定到该机；也可打开构建器新建环。"
                    : "点飞机切换执飞飞机；点航线「＋加航班」选时段，把该机排进当天该时段。";
            }

            RebuildAircraftStrip(gs);
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i] != null) Destroy(_rows[i]);
            _rows.Clear();
            if (_listRoot == null) return;

            if (_ringMode) FillRings(gs, _selectedReg);
            else FillRoutes(gs, _selectedReg);
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

            // 当前选中的飞机已不可用（如刚被签入过渡业务）时，ResolveSelection 已自动切换
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
                    selected && !locked ? UITheme.Success : UITheme.PanelDeep,
                    () => { _selectedReg = reg; OpsSubpageContext.SelectedRegistration = reg; Refresh(); },
                    locked ? 10 : 12);
                if (locked) chip.interactable = false;
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
                    : $"1 班（{r.flights[0].slotLabel} {r.flights[0].depTime}）";
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
                AddRow(label, "+ 加航班", () => OpenSlotPicker(routeId));
            }
        }

        static string JoinSlots(RouteData r)
        {
            var set = new HashSet<string>();
            for (int i = 0; i < r.flights.Count; i++)
            {
                string s = string.IsNullOrEmpty(r.flights[i].slotLabel) ? "—" : r.flights[i].slotLabel;
                set.Add(s + " " + r.flights[i].depTime);
            }
            return string.Join(" · ", set);
        }

        void FillRings(GameState gs, string reg)
        {
            int n = gs?.flightRings?.Count ?? 0;
            if (n == 0)
            {
                AddNote("尚无已保存的航班环。请先打开「构建航班环」校验并保存。");
                return;
            }

            for (int i = 0; i < gs.flightRings.Count; i++)
            {
                var ring = gs.flightRings[i];
                if (ring == null) continue;
                string ringId = ring.ringId;
                string note = ring.valid ? "合格" : "未通过";
                string label =
                    $"{ring.ringId}\n" +
                    $"{ring.legARouteId} ↔ {ring.legBRouteId} · 过站 {ring.turnMinutes} 分 · {note}\n" +
                    $"原机号 {ring.registration ?? "—"}";
                AddRow(label, "绑定", () => AssignRing(gs, ringId, reg));
            }
        }

        void OpenSlotPicker(string routeId)
        {
            _slotRouteId = routeId;
            if (_slotRoot != null) _slotRoot.SetActive(true);
        }

        void HideSlotPicker()
        {
            _slotRouteId = null;
            if (_slotRoot != null) _slotRoot.SetActive(false);
        }

        void ConfirmAddFlight(string slotLabel)
        {
            var gs = GameState.Instance;
            if (gs == null || string.IsNullOrEmpty(_selectedReg) || string.IsNullOrEmpty(_slotRouteId))
            {
                HideSlotPicker();
                return;
            }

            RouteData r = null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
                if (gs.activeRoutes[i] != null && gs.activeRoutes[i].routeId == _slotRouteId)
                    r = gs.activeRoutes[i];
            if (r == null) { HideSlotPicker(); return; }
            r.EnsureFlights();

            // 同机同时段冲突
            for (int i = 0; i < r.flights.Count; i++)
            {
                var f = r.flights[i];
                if (string.Equals(f.registration, _selectedReg, System.StringComparison.OrdinalIgnoreCase)
                    && f.slotLabel == slotLabel)
                {
                    _hint.text = $"该机已在 {r.origin}-{r.destination} 的{slotLabel}执飞，未重复添加。";
                    HideSlotPicker();
                    return;
                }
            }

            string dep = ScheduleSlots.DepOf(slotLabel);
            float block = OpsEconomics.BlockHours(r.distanceKm > 0 ? r.distanceKm : 1000f);
            string arr = ArrAfter(dep, block);
            float single = OpsEconomics.ResolvedSingleFlightBaseline(r);
            var m = OpsEconomics.Evaluate(
                r.distanceKm > 0 ? r.distanceKm : 1000f, 7, r.aircraftType,
                r.groundStaffAssigned, gs.onTimePercent);
            float flightProfit = OpsEconomics.ToDailyProfit(single * ScheduleSlots.DemandOf(slotLabel), 7, m);

            r.flights.Add(new RouteFlight
            {
                registration = _selectedReg,
                depTime = dep,
                arrTime = arr,
                daysOfWeek = "每日",
                weeklyFlights = 7,
                slotLabel = slotLabel,
                dailyProfit = flightProfit
            });
            r.RecalcDailyProfit();
            r.SyncPrimaryFromFlights();
            gs.AddDecisionLog($"机号 {_selectedReg} 加入航线 {r.origin}-{r.destination} 的{slotLabel}（{dep}），新增日利约 {flightProfit:F1} 万。");
            gs.NotifyChanged();
            _hint.text = $"已添加：{_selectedReg} → {r.origin}-{r.destination} {slotLabel} {dep}";
            HideSlotPicker();
            Refresh();
        }

        void AssignRing(GameState gs, string ringId, string reg)
        {
            if (gs?.flightRings == null) return;
            FlightRing ring = null;
            for (int i = 0; i < gs.flightRings.Count; i++)
                if (gs.flightRings[i] != null && gs.flightRings[i].ringId == ringId)
                    ring = gs.flightRings[i];
            if (ring == null) return;

            ring.registration = reg;
            BindRegistrationToRoute(gs, ring.legARouteId, reg);
            BindRegistrationToRoute(gs, ring.legBRouteId, reg);
            gs.AddDecisionLog($"机号 {reg} 绑定航班环 {ring.ringId}。");
            if (_hint != null)
                _hint.text = $"已绑定航班环 {ring.ringId} 到 {reg}";
            Refresh();
        }

        /// <summary>航班环绑定：把该机写入目标航线首班（若无则新增一班）。</summary>
        void BindRegistrationToRoute(GameState gs, string routeId, string reg)
        {
            if (gs?.activeRoutes == null || string.IsNullOrEmpty(routeId) || string.IsNullOrEmpty(reg)) return;
            RouteData target = null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r != null && r.routeId == routeId) target = r;
            }
            if (target == null) return;
            target.EnsureFlights();
            if (target.flights.Count > 0)
                target.flights[0].registration = reg;
            else
                target.flights.Add(new RouteFlight { registration = reg, depTime = target.depTime, arrTime = target.arrTime, slotLabel = ScheduleSlots.LabelForDep(target.depTime), dailyProfit = target.dailyProfit });
            target.SyncPrimaryFromFlights();
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
                UITheme.Accent, onClick, 13);
            _rows.Add(row);
        }

        static string ArrAfter(string dep, float hours)
        {
            int total = ParseMinutes(dep) + Mathf.RoundToInt(Mathf.Max(0f, hours) * 60f);
            int m = total % 1440;
            return $"{m / 60:00}:{m % 60:00}";
        }

        static int ParseMinutes(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Length < 5) return 0;
            int h = 0, mm = 0;
            if (t.Length >= 2 && int.TryParse(t.Substring(0, 2), out int ph)) h = ph;
            if (t.Length >= 5 && int.TryParse(t.Substring(3, 2), out int pm)) mm = pm;
            return h * 60 + mm;
        }
    }
}
