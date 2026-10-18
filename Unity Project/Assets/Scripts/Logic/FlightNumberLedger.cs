using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 航班号台账：把全公司所有班次的航班号摊平，检查**唯一性**。
    ///
    /// 为什么要有这个文件（2026-10-13 第六次报障）：
    /// 周总计划里 FRA—ATL 是 FR106、FRA—ORD 是 FR107，而 FRA—ATL 的单航线页抬头写着
    /// 「FR106 / FR107」——同一份计划里，一个号被两条线同时用着。
    ///
    /// 根因是**两条各自都对的规则叠在一起**：
    /// ① `FlightScheduleFormat.Numbers`：回程号 = 去程号 + 1，第 index 个波次再 +2（一个波次要两个号）；
    /// ② 建线时**基号连号分配**（`TakeoverWorldBuilder` 的 `{航司码}{100 + i}`）。
    /// 于是第 i 条线要占 `base … base+7`，而第 i+1 条线的基号 `base+1` 正好落进去 ——
    /// 一条线只要 1 个波次也会撞；周班次 ≥ 8 的线（如 26 班/周 → 4 个波次）占满 8 个号，整块重叠。
    /// 界面白板 n42/n43 明写「航班号：唯一识别代码」，故这里把「唯一」做成**可检查的不变量**，
    /// 而不是靠每处调用点自觉。
    ///
    /// 提供三件事：
    /// - `Collect`   摊平全部班次的航班号（供审计与自测）
    /// - `Conflicts` 找出撞号（同一个号被两个班次占用）
    /// - `Report`    给人看的一页清单
    /// - `Normalize` 把已经撞号的世界修好（迁移旧档用；幂等）
    /// </summary>
    public static class FlightNumberLedger
    {
        public struct Leg
        {
            public string routeId;
            public string od;        // FRA—ATL
            public int wave;         // 第几班（从 1 起，给人看）
            public bool inbound;     // true = 回程
            public string number;

            /// <summary>一眼能读懂的一行：「FRA—ATL 第1班回程 FR107」。</summary>
            public string Text => $"{od} 第{wave}班{(inbound ? "回程" : "去程")} {number}";
        }

        public struct Conflict
        {
            public string number;
            public string keeper;    // 先占到的人
            public string taker;     // 撞上来的人
        }

        /// <summary>摊平全公司的航班号（按航线顺序、班次顺序；去程在前、回程在后）。</summary>
        public static List<Leg> Collect(IList<RouteData> routes)
        {
            var list = new List<Leg>();
            if (routes == null) return list;
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (r == null) continue;
                r.EnsureFlights();
                string od = $"{r.origin}—{r.destination}";
                int n = r.flights != null ? r.flights.Count : 0;
                for (int w = 0; w < n; w++)
                {
                    FlightScheduleFormat.Numbers(r, w, out string outNo, out string inNo);
                    list.Add(new Leg { routeId = r.routeId, od = od, wave = w + 1, inbound = false, number = outNo });
                    list.Add(new Leg { routeId = r.routeId, od = od, wave = w + 1, inbound = true, number = inNo });
                }
            }
            return list;
        }

        /// <summary>撞号的航班号（同一号被两个班次占用）。空 = 全部唯一。</summary>
        public static List<Conflict> Conflicts(IList<RouteData> routes)
        {
            var legs = Collect(routes);
            var first = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<Conflict>();
            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                if (string.IsNullOrEmpty(leg.number)) continue;
                if (!first.TryGetValue(leg.number, out string keeper))
                {
                    first[leg.number] = leg.Text;
                    continue;
                }
                list.Add(new Conflict { number = leg.number, keeper = keeper, taker = leg.Text });
            }
            return list;
        }

        /// <summary>审计清单（一页给人看）：全部号 + 撞号明细。</summary>
        public static string Report(IList<RouteData> routes)
        {
            var legs = Collect(routes);
            var conflicts = Conflicts(routes);
            var sb = new StringBuilder();
            sb.Append($"航班号审计　共 {legs.Count} 个号（{routes?.Count ?? 0} 条航线）");
            sb.Append(conflicts.Count == 0 ? "　**全部唯一**\n" : $"　★撞号 {conflicts.Count} 处\n");
            for (int i = 0; i < legs.Count; i++) sb.Append("　·　").Append(legs[i].Text).Append('\n');
            for (int i = 0; i < conflicts.Count; i++)
                sb.Append($"　★ {conflicts[i].number}：{conflicts[i].keeper} ↔ {conflicts[i].taker}\n");
            return sb.ToString();
        }

        /// <summary>
        /// 把已经撞号的世界修好：按「现有基号升序 → routeId」定序，重新按
        /// <see cref="FlightScheduleFormat.BlockStride"/> 给每条航线分块，各线互不重叠。
        ///
        /// 定序用**现有基号**而不是列表顺序，是为了保住玩家已经认得的先后（周总计划的排法不变）。
        /// 号变了要**连带重绑时刻槽**：持仓里写着 `usedByFlightNo`（航线 × 时段 × 本线 × 本航班号
        /// 四项匹配），号一改那条持仓就认不出来 —— 故先退回本航线的持仓，再按新号重绑。
        ///
        /// 幂等：全公司已经互不撞号时**原样返回 0**，不动任何数据、不写任何日志。
        /// </summary>
        public static int Normalize(GameState gs)
        {
            var routes = gs?.activeRoutes;
            if (routes == null || routes.Count == 0) return 0;
            if (Conflicts(routes).Count == 0) return 0;

            var order = new List<RouteData>();
            for (int i = 0; i < routes.Count; i++) if (routes[i] != null) order.Add(routes[i]);
            order.Sort((a, b) =>
            {
                int c = FlightScheduleFormat.NumberOf(a.flightNumber)
                    .CompareTo(FlightScheduleFormat.NumberOf(b.flightNumber));
                return c != 0 ? c : string.CompareOrdinal(a.routeId ?? "", b.routeId ?? "");
            });

            // 前缀取航司两字码：连号世界里各线前缀一致，取第一条线的前缀只为兜底
            string prefix = !string.IsNullOrEmpty(gs.companyIata)
                ? gs.companyIata
                : FlightScheduleFormat.PrefixOf(order[0].flightNumber);
            if (string.IsNullOrEmpty(prefix)) prefix = "NV";

            int changed = 0;
            for (int i = 0; i < order.Count; i++)
            {
                var r = order[i];
                string want = $"{prefix}{FlightScheduleFormat.BaseNumberOf(i)}";
                if (string.Equals(r.flightNumber, want, StringComparison.OrdinalIgnoreCase)) continue;
                RoutePermitLogic.ReleaseByRoute(gs, r.routeId);   // 旧号写的持仓先退回
                r.flightNumber = want;
                RoutePermitLogic.BindRouteFlights(gs, r);         // 再按新号重绑
                changed++;
            }
            if (changed > 0)
                gs.AddDecisionLog($"航班号重编：{changed} 条航线的航班号改为互不撞号（每条航线预留 "
                                  + $"{FlightScheduleFormat.BlockStride} 个号；时刻槽已按新号重绑）。");
            return changed;
        }
    }
}
