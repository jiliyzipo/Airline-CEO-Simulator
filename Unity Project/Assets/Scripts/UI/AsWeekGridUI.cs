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
    /// Airline Simulator 式周排班：左轴 00:00–24:00，上轴周一至周日。
    ///
    /// 一趟往返画成**三段**：去程段（上为本场、下为对方站）→ **过站段**（橙条，在对方站）
    /// → 回程段（上为对方站、下为本场）。旧版把整趟往返画成**一整条 18 小时的长条**，
    /// 于是「过站 50 分」在图上无处可寻，屏幕上唯一看得见的空档是本场停场（5 小时多），
    /// 看着就像「在对方站过站、而且远超 50 分」—— 2026-10-14 第八次报障。
    /// </summary>
    public static class AsWeekGridUI
    {
        public class Block
        {
            public int startMin;      // 去程起飞
            public int outArrMin;     // 去程落地（对方站）
            public int retDepMin;     // 回程起飞（= 去程落地 + 过站），图上橙条的起止
            public int endMin;        // 回程落地（可 > 1440 = 次日）
            public string from;       // 去程出发地（本场）
            public string to;         // 去程到达地（对方站）
            public bool[] days;
            public bool highlight;

            /// <summary>过站分钟（对方站）。</summary>
            public int TurnMinutes => retDepMin - outArrMin;
        }

        static readonly string[] DayEn = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        const int DayMinutes = 24 * 60;
        const float Gutter = 0.10f;
        const float Header = 0.075f;
        /// <summary>矮于这个高度的方块不写字（写上去也只会糊成一团）。</summary>
        const float TextMin = 0.038f;

        public static List<Block> Collect(GameState gs, string registration,
            RouteData focusRoute, int focusIndex)
        {
            var list = new List<Block>();
            if (gs?.activeRoutes == null) return list;
            bool byReg = !string.IsNullOrEmpty(registration);
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                r.EnsureFlights();
                if (r.flights == null) continue;
                for (int k = 0; k < r.flights.Count; k++)
                {
                    var f = r.flights[k];
                    if (f == null) continue;
                    if (byReg)
                    {
                        if (string.IsNullOrEmpty(f.registration)) continue;
                        if (!string.Equals(f.registration, registration, System.StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    else if (focusRoute == null || r != focusRoute || k != focusIndex)
                        continue;

                    // 一趟往返取四个时刻：画成「去程段 / 过站段 / 回程段」，过站才看得见
                    FlightScheduleFormat.WindowTimes(r, f, out int t0, out int t1, out int t2, out int t3);
                    bool hit = focusRoute != null && r == focusRoute && k == focusIndex;
                    list.Add(new Block
                    {
                        startMin = t0,
                        outArrMin = t1,
                        retDepMin = t2,
                        endMin = t3,
                        from = r.origin,
                        to = r.destination,
                        days = FlightScheduleFormat.GridRowDays(f),
                        highlight = hit
                    });
                }
            }
            return list;
        }

        public static void Draw(Transform host, List<Block> blocks)
        {
            if (host == null) return;

            float colW = (1f - Gutter) / 7f;
            var colBg = new Color(0.07f, 0.10f, 0.16f, 0.92f);
            var line = new Color(0.18f, 0.24f, 0.32f, 0.55f);
            var blk = new Color(0.16f, 0.20f, 0.28f, 0.98f);
            var blkHi = new Color(0.14f, 0.30f, 0.48f, 0.98f);

            for (int d = 0; d < 7; d++)
            {
                float x0 = Gutter + d * colW;
                float x1 = x0 + colW - 0.006f;
                UIFactory.Label(host, "Dh" + d, DayEn[d], 11, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(x0, 1f - Header), new Vector2(x1, 1f), Vector2.zero, Vector2.zero),
                    d >= 5 ? UITheme.Warning : UITheme.TextMuted, FontStyles.Bold);
                var col = UIFactory.Panel(host, "Col" + d,
                    new UIFactory.Anchor(new Vector2(x0, 0f), new Vector2(x1, 1f - Header), Vector2.zero, Vector2.zero),
                    colBg);
                var img = col.GetComponent<Image>();
                if (img != null) img.raycastTarget = false;
            }

            for (int h = 0; h <= 24; h += 2)
            {
                float y = YOf(h * 60);
                UIFactory.Label(host, "H" + h, $"{h:00}:00", 9, TextAlignmentOptions.MidlineRight,
                    new UIFactory.Anchor(new Vector2(0f, y - 0.018f), new Vector2(Gutter - 0.012f, y + 0.018f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                if (h == 24) continue;
                var hr = UIFactory.Panel(host, "Ln" + h,
                    new UIFactory.Anchor(new Vector2(Gutter, y - 0.0015f), new Vector2(1f, y + 0.0015f), Vector2.zero, Vector2.zero),
                    line);
                var himg = hr.GetComponent<Image>();
                if (himg != null) himg.raycastTarget = false;
            }

            if (blocks == null || blocks.Count == 0)
            {
                UIFactory.Label(host, "Empty", "暂无定期航班。", 13, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(Gutter, 0.4f), new Vector2(1f, 0.55f), Vector2.zero, Vector2.zero),
                    UITheme.Warning);
                return;
            }

            int n = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b == null || b.days == null) continue;
                for (int d = 0; d < 7; d++)
                {
                    // ★只在**执飞日**起画整趟往返；跨过 24:00 的部分由各段自己落到次日列
                    //（见 DrawSpan）。旧版先按「执飞日 + 次日」预标 7 天，再对每个标记日
                    // 整天重绘整趟往返 → 7 列全画满，看着像「每日一班」（第九次报障）。
                    if (!b.days[d]) continue;
                    // 去程段（上本场、下对方站）
                    DrawSpan(host, d, b.startMin, b.outArrMin, b.from, b.to,
                        false, b.highlight, colW, blk, blkHi, ref n);
                    // 过站段（橙条，在对方站；先不画，等两段航程画完再压上去，免得被航段最小高度吃掉）
                    DrawSpan(host, d, b.outArrMin, b.retDepMin, b.to, "",
                        true, b.highlight, colW, blk, blkHi, ref n);
                    // 回程段（上对方站、下本场 —— 方向与去程相反，旧版两块都写「本场在上」）
                    DrawSpan(host, d, b.retDepMin, b.endMin, b.to, b.from,
                        false, b.highlight, colW, blk, blkHi, ref n);
                }
            }
        }

        /// <summary>
        /// 画一段（航程或过站），按**它自己落在哪一天**切开：当天画一份，跨过 24:00 的部分
        /// 落到次日列；若这段整段都在次日（红眼波的过站与回程），整体后移一天。
        /// 这样「那天飞不飞」完全由时刻算出来，不靠事先预标星期。
        /// </summary>
        static void DrawSpan(Transform host, int day, int start, int end,
            string top, string bottom, bool turn, bool highlight, float colW,
            Color blk, Color blkHi, ref int n)
        {
            if (end <= start) return;
            FlightScheduleFormat.SplitWindow(start, end, out int off, out int ms, out int me, out int te);
            if (me > ms)
            {
                if (turn) TurnStrip(host, (day + off) % 7, ms, me, end - start, colW, ref n);
                else PlaceDay(host, top, bottom, highlight, (day + off) % 7, ms, me, colW, blk, blkHi, ref n);
            }
            if (te > 0)
            {
                if (turn) TurnStrip(host, (day + off + 1) % 7, 0, te, end - start, colW, ref n);
                else PlaceDay(host, top, bottom, highlight, (day + off + 1) % 7, 0, te, colW, blk, blkHi, ref n);
            }
        }

        /// <summary>过站条：50 分只有几个像素高，故给一个最小高度保证看得见；够高才写字。</summary>
        static void TurnStrip(Transform host, int day, int s, int e, int minutes, float colW, ref int n)
        {
            if (e <= s) return;
            float x0 = Gutter + day * colW + 0.006f;
            float x1 = Gutter + (day + 1) * colW - 0.010f;
            float y1 = YOf(s);
            float y0 = YOf(e);
            if (y1 - y0 < 0.014f) y1 = y0 + 0.014f;

            var go = UIFactory.Panel(host, "T" + n,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                TurnFace);
            n++;
            UIFactory.AddBorder(go, TurnEdge, new Vector2(1f, -1f));
            var img = go.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            if (y1 - y0 >= 0.05f)
                UIFactory.Label(go.transform, "A", $"过站 {minutes} 分", 9, TextAlignmentOptions.Center,
                    UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);
        }

        /// <summary>过站条的配色（橙）：与航段的蓝灰区分开，一眼能认出「这是地面停留」。</summary>
        static readonly Color TurnFace = new Color(0.70f, 0.42f, 0.12f, 0.98f);
        static readonly Color TurnEdge = new Color(0.95f, 0.68f, 0.28f, 1f);

        static void PlaceDay(Transform host, string top, string bottom, bool highlight, int day,
            int s, int e, float colW, Color blk, Color blkHi, ref int n)
        {
            if (e <= s) return;
            float x0 = Gutter + day * colW + 0.006f;
            float x1 = Gutter + (day + 1) * colW - 0.010f;
            float y1 = YOf(s);
            float y0 = YOf(e);
            if (y1 - y0 < 0.035f) y0 = y1 - 0.035f;

            var go = UIFactory.Panel(host, "B" + n,
                new UIFactory.Anchor(new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero),
                highlight ? blkHi : blk);
            n++;
            UIFactory.AddBorder(go, highlight ? UITheme.Accent : UITheme.Border, new Vector2(1f, -1f));
            var img = go.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            // 矮块不写字：46 分的回程尾段只有几个像素，硬写只会糊成一团、还盖住相邻航段
            if (y1 - y0 < TextMin) return;

            string a = string.IsNullOrEmpty(top) ? "—" : top;
            string c = string.IsNullOrEmpty(bottom) ? "—" : bottom;
            bool tall = (y1 - y0) >= 0.07f;
            if (tall)
            {
                UIFactory.Label(go.transform, "A", a, 11, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(0.04f, 0.52f), new Vector2(0.96f, 0.94f), Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary, FontStyles.Bold);
                UIFactory.Label(go.transform, "B", c, 11, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.48f), Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary, FontStyles.Bold);
            }
            else
            {
                UIFactory.Label(go.transform, "A", a + "\n" + c, 10, TextAlignmentOptions.Center,
                    UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);
            }
        }

        static float YOf(int minutes)
        {
            float t = Mathf.Clamp01(minutes / (float)DayMinutes);
            return (1f - Header) * (1f - t);
        }
    }
}
