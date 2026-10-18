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
    /// <summary>构建航班环：按起飞顺序点选同一架飞机当日多段，或一键按值勤预算自动串联。</summary>
    public class OpsRingBuilderUI : MonoBehaviour
    {
        Transform _listRoot;
        TextMeshProUGUI _selectionText;
        TextMeshProUGUI _resultText;
        readonly List<string> _picked = new List<string>();
        readonly List<GameObject> _rowGos = new List<GameObject>();

        public static OpsRingBuilderUI Ensure(Transform card)
        {
            if (card == null) return null;
            var existing = card.GetComponentInChildren<OpsRingBuilderUI>(true);
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

            var root = UIFactory.Panel(card, "RingBuilder",
                new UIFactory.Anchor(new Vector2(0.04f, 0.14f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(0, 0, 0, 0.01f), rounded: false);
            var img = root.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            var ui = root.AddComponent<OpsRingBuilderUI>();
            ui.Build(root.transform);
            ui.Refresh();
            return ui;
        }

        void Build(Transform root)
        {
            UIFactory.Label(root, "Intro",
                "按起飞顺序点选同一架飞机当日要串的多段（最多 6 段）。自动串联取最忙一架机的日循环。",
                13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.9f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            _selectionText = UIFactory.Label(root, "Selection", "未选段", 14,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.82f), new Vector2(1f, 0.9f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            UIFactory.Button(root, "BtnAuto", "自动串联",
                new UIFactory.Anchor(new Vector2(0f, 0.72f), new Vector2(0.24f, 0.8f), Vector2.zero, Vector2.zero),
                UITheme.Accent, AutoChain, 13);
            UIFactory.Button(root, "BtnClear", "清空选择",
                new UIFactory.Anchor(new Vector2(0.26f, 0.72f), new Vector2(0.48f, 0.8f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, ClearSelection, 13);
            UIFactory.Button(root, "BtnValidate", "校验连班",
                new UIFactory.Anchor(new Vector2(0.5f, 0.72f), new Vector2(0.74f, 0.8f), Vector2.zero, Vector2.zero),
                UITheme.Accent, Validate, 13);

            var listHost = UIFactory.Panel(root, "RoutePickList",
                new UIFactory.Anchor(new Vector2(0f, 0.28f), new Vector2(1f, 0.7f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            var scroll = listHost.AddComponent<ScrollRect>();
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
            scroll.content = crt;
            scroll.viewport = listHost.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.vertical = true;
            _listRoot = content.transform;

            _resultText = UIFactory.ScrollableLabel(root, "RingResult",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(1f, 0.26f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, 13);
            _resultText.text = "校验结果将显示在这里。";
        }

        public void Refresh()
        {
            RebuildList();
            UpdateSelectionLabel();
        }

        void RebuildList()
        {
            for (int i = 0; i < _rowGos.Count; i++)
                if (_rowGos[i] != null) Destroy(_rowGos[i]);
            _rowGos.Clear();
            if (_listRoot == null) return;

            var gs = GameState.Instance;
            Debug.Log($"[n36] RebuildList routes={(gs != null ? gs.activeRoutes?.Count ?? -1 : -1)}");
            if (gs?.activeRoutes == null || gs.activeRoutes.Count == 0)
            {
                var empty = UIFactory.Label(_listRoot, "Empty", "暂无已开通航线。请先在运营部点「开通航线」。", 13,
                    TextAlignmentOptions.Center, UIFactory.Fill, UITheme.TextMuted);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
                _rowGos.Add(empty.gameObject);
                return;
            }

            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var route = gs.activeRoutes[i];
                if (route == null) continue;
                route.EnsureFlights();
                for (int f = 0; f < route.flights.Count; f++)
                {
                    var fl = route.flights[f];
                    if (fl == null) continue;
                    string id = LegKey(route.routeId, f);
                    string label =
                        $"{route.origin}→{route.destination}  {fl.depTime}  {fl.registration}  {route.flightNumber}";
                    int order = _picked.IndexOf(id);
                    string tag = order >= 0 ? $"〔{order + 1}〕 " : "";
                    var btn = UIFactory.Button(_listRoot, "Pick_" + i + "_" + f, tag + label,
                        new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                        order >= 0 ? UITheme.Accent : UITheme.PanelDeep,
                        () => OnPick(id), 12);
                    var le = btn.gameObject.AddComponent<LayoutElement>();
                    le.preferredHeight = 36;
                    le.flexibleWidth = 1;
                    var rt = btn.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0, 1);
                    rt.anchorMax = new Vector2(1, 1);
                    rt.pivot = new Vector2(0.5f, 1);
                    rt.sizeDelta = new Vector2(0, 36);
                    _rowGos.Add(btn.gameObject);
                }
            }
        }

        static string LegKey(string routeId, int index) => (routeId ?? "") + "#" + index;

        void OnPick(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            int at = _picked.IndexOf(id);
            if (at >= 0) _picked.RemoveAt(at);
            else if (_picked.Count < 6) _picked.Add(id);
            Refresh();
        }

        void ClearSelection()
        {
            _picked.Clear();
            if (_resultText != null) _resultText.text = "已清空选择。";
            Refresh();
        }

        void AutoChain()
        {
            var gs = GameState.Instance;
            if (gs?.activeRoutes == null || gs.activeRoutes.Count == 0)
            {
                if (_resultText != null) _resultText.text = "暂无已开通航线。";
                return;
            }

            var plan = new OpsRotationLogic.RotationPlan();
            plan.LoadFrom(gs.activeRoutes);
            string best = null;
            int bestN = 0;
            var regs = plan.Registrations();
            for (int i = 0; i < regs.Count; i++)
            {
                int n = plan.TurnCount(regs[i]);
                if (n > bestN) { bestN = n; best = regs[i]; }
            }
            _picked.Clear();
            if (string.IsNullOrEmpty(best) || bestN <= 0)
            {
                if (_resultText != null) _resultText.text = "机队当日尚无已排班次，无法自动串联。";
                Refresh();
                return;
            }

            var wins = new List<OpsRotationLogic.TurnWindow>(plan.Of(best));
            wins.Sort((x, y) => x.start.CompareTo(y.start));
            for (int w = 0; w < wins.Count && _picked.Count < 6; w++)
            {
                string key = FindKey(gs, wins[w].routeId, wins[w].start);
                if (!string.IsNullOrEmpty(key) && !_picked.Contains(key)) _picked.Add(key);
            }
            if (_resultText != null)
                _resultText.text = $"已按 {best} 的日循环自动点选 {_picked.Count} 段（值勤：{plan.DutySummary(best, null)}）。请点「校验连班」。";
            Refresh();
        }

        static string FindKey(GameState gs, string routeId, int startMin)
        {
            if (gs?.activeRoutes == null) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null || r.routeId != routeId) continue;
                r.EnsureFlights();
                for (int f = 0; f < r.flights.Count; f++)
                {
                    var fl = r.flights[f];
                    if (fl == null) continue;
                    if (LiveClockLogic.ParseMinutes(fl.depTime) == startMin)
                        return LegKey(r.routeId, f);
                }
            }
            return null;
        }

        void Validate()
        {
            var gs = GameState.Instance;
            if (_picked.Count < 1)
            {
                if (_resultText != null) _resultText.text = "请先点选至少一段，或使用自动串联。";
                return;
            }

            var sb = new System.Text.StringBuilder();
            OpsRotationLogic.TurnWindow? prev = null;
            string reg = "";
            bool ok = true;
            for (int i = 0; i < _picked.Count; i++)
            {
                ParseKey(_picked[i], out string rid, out int idx);
                var r = FindRoute(gs, rid);
                if (r == null) { ok = false; sb.AppendLine($"第 {i + 1} 段找不到航线。"); continue; }
                r.EnsureFlights();
                if (idx < 0 || idx >= r.flights.Count) { ok = false; continue; }
                var fl = r.flights[idx];
                if (string.IsNullOrEmpty(reg)) reg = fl.registration;
                var w = OpsRotationLogic.WindowOf(r, fl.depTime);
                sb.AppendLine($"{i + 1}. {OpsRotationLogic.FormatMinute(w.start)}–{OpsRotationLogic.FormatMinute(w.end)}  {r.origin}⇄{r.destination}  {fl.registration}");
                if (prev.HasValue)
                {
                    if (OpsRotationLogic.Overlaps(prev.Value, w, OpsRotationLogic.RotationGapMinutes))
                    {
                        ok = false;
                        sb.AppendLine($"   与上一段冲突（需落地后过站 {w.turn} 分 + 裕度 {OpsRotationLogic.RotationGapMinutes} 分）。");
                    }
                }
                prev = w;
            }

            var ring = new FlightRing
            {
                ringId = "DAY-" + (string.IsNullOrEmpty(reg) ? "X" : reg),
                registration = reg,
                turnMinutes = 50,
                valid = ok,
                validationNote = sb.ToString()
            };
            if (_picked.Count >= 2)
            {
                ParseKey(_picked[0], out string a, out _);
                ParseKey(_picked[_picked.Count - 1], out string b, out _);
                ring.legARouteId = a;
                ring.legBRouteId = b;
            }
            if (_resultText != null)
                _resultText.text = (ok ? "结论：日循环可以通过\n" : "结论：不能通过\n") + sb;

            if (gs.flightRings == null) gs.flightRings = new List<FlightRing>();
            int found = -1;
            for (int i = 0; i < gs.flightRings.Count; i++)
                if (gs.flightRings[i] != null && gs.flightRings[i].ringId == ring.ringId) { found = i; break; }
            if (found >= 0) gs.flightRings[found] = ring;
            else gs.flightRings.Add(ring);
            OpsSubpageContext.SelectedRingNote = _resultText != null ? _resultText.text : ring.validationNote;
            gs.NotifyChanged();
        }

        static void ParseKey(string key, out string routeId, out int index)
        {
            routeId = key ?? "";
            index = 0;
            int h = routeId.LastIndexOf('#');
            if (h < 0) return;
            routeId = key.Substring(0, h);
            int.TryParse(key.Substring(h + 1), out index);
        }

        void UpdateSelectionLabel()
        {
            if (_selectionText == null) return;
            _selectionText.text = _picked.Count == 0
                ? "未选段"
                : $"已选 {_picked.Count} 段（按点选顺序串成日循环）";
        }

        static RouteData FindRoute(GameState gs, string id)
        {
            if (gs?.activeRoutes == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
                if (gs.activeRoutes[i] != null && gs.activeRoutes[i].routeId == id)
                    return gs.activeRoutes[i];
            return null;
        }
    }
}
