using System;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 地图上选中一架飞机后弹出的航班卡（对齐 Airline Sim 的底部航班条）。
    ///
    /// ★2026-10-30 CEO：「点击某个飞机图标后，图标变成选中状态和显示它的航线，然后旁边弹窗显示该航班的信息」。
    ///
    /// 为什么不做成 Tooltip：Tooltip 只能显示静态字符串、且跟随光标 —— 而这里要显示**会随游戏时钟走**
    /// 的进度条与到达时刻。做成常驻卡片还有一个好处：选中后地图上那架飞机在飞，卡片上的进度条跟着走，
    /// 玩家能直接看到「这一班还有多久落地」，不用反复点。
    ///
    /// 卡片只读 <see cref="GameState"/>，不改任何状态；关闭按钮只调 <see cref="MapSelection.Clear"/>。
    /// </summary>
    public class MapFlightCard : MonoBehaviour
    {
        static readonly Color CardBg = new Color(0.043f, 0.086f, 0.145f, 0.97f);

        RectTransform _root;
        TextMeshProUGUI _flightNo;
        TextMeshProUGUI _typeChip;
        TextMeshProUGUI _originCode, _originCity;
        TextMeshProUGUI _destCode, _destCity;
        TextMeshProUGUI _depTime, _arrTime;
        TextMeshProUGUI _phase, _elapsed, _reg;
        RectTransform _fill;
        Image _phaseDot;

        public static MapFlightCard Build(Transform parent)
        {
            var go = new GameObject("MapFlightCard", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            // 左下角：与顾问气泡（左下 0.02,0.06）错开，压在它上面
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(16f, 92f);
            rt.sizeDelta = new Vector2(392f, 132f);

            var bg = go.AddComponent<Image>();
            bg.color = CardBg;
            if (UIFactory.RoundedSprite != null) { bg.sprite = UIFactory.RoundedSprite; bg.type = Image.Type.Sliced; }
            bg.raycastTarget = true;   // 挡住穿透点击：点卡片不该顺带清掉选中

            UIFactory.AddBorder(go, UITheme.Border, new Vector2(1f, -1f));

            var card = go.AddComponent<MapFlightCard>();
            card._root = rt;
            card.BuildContent(rt);
            go.SetActive(false);
            return card;
        }

        void BuildContent(RectTransform rt)
        {
            // ── 第一行：航班号 + 机型胶囊 + 关闭 ──
            _flightNo = UIFactory.Label(rt, "FlightNo", "", 20, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.74f), new Vector2(0.42f, 1f),
                    new Vector2(16f, 0f), new Vector2(0f, -6f)),
                UITheme.TextPrimary, FontStyles.Bold);

            var chipGo = UIFactory.Panel(rt, "TypeChip",
                new UIFactory.Anchor(new Vector2(0.42f, 0.78f), new Vector2(0.42f, 0.96f),
                    new Vector2(0f, 0f), new Vector2(66f, 0f)),
                new Color(0.12f, 0.28f, 0.46f, 0.95f));
            _typeChip = UIFactory.Label(chipGo.transform, "Text", "", 12, TextAlignmentOptions.Center,
                UIFactory.Fill, new Color(0.72f, 0.90f, 1f, 1f), FontStyles.Bold);

            var close = UIFactory.Button(rt, "Close", "×",
                new UIFactory.Anchor(new Vector2(1f, 0.78f), new Vector2(1f, 1f),
                    new Vector2(-34f, -4f), new Vector2(-6f, 0f)),
                new Color(0.10f, 0.18f, 0.30f, 0.9f), () => MapSelection.Clear(), 18);
            close.name = "Close";

            // ── 第二行：出发/到达（三字码大字号 + 城市小字 + 时刻）──
            _originCode = UIFactory.Label(rt, "OriginCode", "", 22, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.42f), new Vector2(0.22f, 0.72f),
                    new Vector2(16f, 0f), Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            _originCity = UIFactory.Label(rt, "OriginCity", "", 11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.24f), new Vector2(0.28f, 0.44f),
                    new Vector2(16f, 0f), Vector2.zero),
                UITheme.TextMuted);

            _depTime = UIFactory.Label(rt, "DepTime", "", 15, TextAlignmentOptions.Midline,
                new UIFactory.Anchor(new Vector2(0.24f, 0.42f), new Vector2(0.38f, 0.72f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            UIFactory.Label(rt, "Arrow", "→", 16, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.37f, 0.42f), new Vector2(0.45f, 0.72f), Vector2.zero, Vector2.zero),
                new Color(0.55f, 0.72f, 0.92f, 1f));

            _destCode = UIFactory.Label(rt, "DestCode", "", 22, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.45f, 0.42f), new Vector2(0.66f, 0.72f),
                    new Vector2(8f, 0f), Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            _destCity = UIFactory.Label(rt, "DestCity", "", 11, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.45f, 0.24f), new Vector2(0.72f, 0.44f),
                    new Vector2(8f, 0f), Vector2.zero),
                UITheme.TextMuted);

            _arrTime = UIFactory.Label(rt, "ArrTime", "", 15, TextAlignmentOptions.Midline,
                new UIFactory.Anchor(new Vector2(0.68f, 0.42f), new Vector2(0.82f, 0.72f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            // 机号：右对齐，与「时刻」同一行的右侧
            _reg = UIFactory.Label(rt, "Reg", "", 12, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.76f, 0.42f), new Vector2(1f, 0.72f),
                    Vector2.zero, new Vector2(-16f, 0f)),
                UITheme.TextMuted);

            // ── 第三行：进度条 + 状态 + 已飞/总时长 ──
            var track = UIFactory.Panel(rt, "Track",
                new UIFactory.Anchor(new Vector2(0f, 0.14f), new Vector2(1f, 0.20f),
                    new Vector2(16f, 0f), new Vector2(-16f, 0f)),
                new Color(0.16f, 0.26f, 0.38f, 0.9f));
            track.GetComponent<Image>().raycastTarget = false;

            var fillGo = UIFactory.Panel(track.transform, "Fill", UIFactory.Fill,
                new Color(1f, 0.72f, 0.24f, 1f));
            fillGo.GetComponent<Image>().raycastTarget = false;
            _fill = fillGo.GetComponent<RectTransform>();
            _fill.anchorMin = new Vector2(0f, 0f);
            _fill.anchorMax = new Vector2(0f, 1f);   // 宽度靠 anchorMax.x 推
            _fill.offsetMin = Vector2.zero;
            _fill.offsetMax = Vector2.zero;

            _phase = UIFactory.Label(rt, "Phase", "", 12, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(0.5f, 0.14f),
                    new Vector2(16f, 2f), new Vector2(0f, 0f)),
                new Color(1f, 0.78f, 0.32f, 1f), FontStyles.Bold);

            _elapsed = UIFactory.Label(rt, "Elapsed", "", 12, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.5f, 0f), new Vector2(1f, 0.14f),
                    Vector2.zero, new Vector2(-16f, 2f)),
                UITheme.TextMuted);
        }

        /// <summary>按当前读档状态刷新卡片。航线找不到时自己隐藏 —— 不留一张空卡挂在屏幕上。</summary>
        public void Refresh()
        {
            if (_root == null) return;
            var gs = GameState.Instance;
            RouteData r = null;
            if (gs != null && gs.activeRoutes != null && MapSelection.Has)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var cand = gs.activeRoutes[i];
                    if (cand != null && MapSelection.IsSelected(cand.routeId)) { r = cand; break; }
                }
            }

            if (r == null) { if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false); return; }
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);

            r.EnsureFlights();
            int idx = MapSelection.FlightIndex;
            RouteFlight fl = (idx >= 0 && idx < r.flights.Count) ? r.flights[idx] : null;
            string reg = fl != null && !string.IsNullOrEmpty(fl.registration) ? fl.registration : r.registration;
            string type = MapWidget.TypeOf(gs, r, fl);

            DateTime localNow = gs != null ? gs.SimUtc + GameState.LocalUtcOffset : DateTime.Now;
            var snap = MapFlightState.Snapshot(localNow,
                fl != null ? fl.daysOfWeek : r.daysOfWeek,
                fl != null ? fl.weeklyFlights : r.weeklyFlights,
                fl != null ? fl.depTime : r.depTime,
                fl != null ? fl.arrTime : r.arrTime,
                r.distanceKm, r.aircraftType, r.groundStaffAssigned);

            // 回程/过站时「出发场」其实是到达场：不翻转的话，卡片会指着飞机不在的那个机场。
            bool reversed = snap.phase == MapFlightPhase.Inbound || snap.phase == MapFlightPhase.Turnaround;
            string from = reversed ? r.destination : r.origin;
            string to = reversed ? r.origin : r.destination;

            string no = string.IsNullOrEmpty(r.flightNumber) ? r.routeId : r.flightNumber;
            _flightNo.text = ChineseFontProvider.Sanitize((reversed ? "回 " : "") + no);
            _typeChip.text = ChineseFontProvider.Sanitize(string.IsNullOrEmpty(type) ? "—" : type);
            _originCode.text = from ?? "—";
            _originCity.text = ChineseFontProvider.Sanitize(MapWidget.AirportCityLabel(from));
            _destCode.text = to ?? "—";
            _destCity.text = ChineseFontProvider.Sanitize(MapWidget.AirportCityLabel(to));
            _depTime.text = Clock(snap.legDep);
            _arrTime.text = Clock(snap.legArr);
            _reg.text = ChineseFontProvider.Sanitize(string.IsNullOrEmpty(reg) ? "未配机" : reg);

            _phase.text = ChineseFontProvider.Sanitize(snap.PhaseLabel);
            _phase.color = snap.inAir ? new Color(1f, 0.78f, 0.32f, 1f)
                          : new Color(0.62f, 0.80f, 0.98f, 1f);

            // 进度：待起飞 0、过站 0（下一段还没开始）、已落地 1、空中按当前航段
            float p = Mathf.Clamp01(snap.legProgress01);
            _fill.anchorMax = new Vector2(p, 1f);

            _elapsed.text = ChineseFontProvider.Sanitize(ElapsedText(snap, localNow));
        }

        static string Clock(DateTime t) => t.ToString("HH:mm");

        static string ElapsedText(MapFlightSnapshot s, DateTime now)
        {
            int total = Mathf.Max(0, s.legMinutes);
            switch (s.phase)
            {
                case MapFlightPhase.Outbound:
                case MapFlightPhase.Inbound:
                {
                    int done = Mathf.Clamp((int)(now - s.legDep).TotalMinutes, 0, total);
                    return $"已飞 {done / 60}:{done % 60:00} / {total / 60}:{total % 60:00}";
                }
                case MapFlightPhase.Turnaround:
                    return $"本段已落地 · {Clock(s.legDep)} 回程起飞";
                case MapFlightPhase.Landed:
                    return $"本班已完成 · 航段 {total / 60}:{total % 60:00}";
                default:
                    return $"待起飞 · 航段 {total / 60}:{total % 60:00}";
            }
        }

        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }
    }
}
