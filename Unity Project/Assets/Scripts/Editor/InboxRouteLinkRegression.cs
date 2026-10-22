#if UNITY_EDITOR
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 收件箱底栏「详见 · 此航线」的认线回归（2026-10-19）。
    ///
    /// 背景：底栏要按「这封邮件讲哪条航线」动态加按钮、摘掉「董事会」，
    /// 而 routeId 在各发信方存在不同字段里，且部分单子的 origin 其实是**机场三字码**。
    /// 错认的后果不是少个按钮，而是把 CEO 送到一条不存在或不相干的航线上。
    /// </summary>
    public static class InboxRouteLinkRegression
    {
        public static void Run()
        {
            var go = new GameObject("__inbox_route_link_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1; gs.quarter = 1;
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                var route = new RouteData
                {
                    routeId = "R-PEK-SHA-LINK", origin = "PEK", destination = "SHA", distanceKm = 1077f,
                    aircraftType = "A320neo", flightNumber = "NS342", registration = "B-3000",
                    depTime = "06:30", arrTime = "07:46", daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                };
                route.SyncFlightsFromPrimary();
                gs.activeRoutes = new List<RouteData> { route };

                // ① 运力呈报：origin 存 routeId → 认得出本线
                var report = new InboxMail
                {
                    id = "m_cap_R-PEK-SHA-LINK_1", sender = "运营部", role = "运营部负责人",
                    action = InboxManager.CapacityDirectiveAction, origin = route.routeId
                };
                bool reportOk = InboxManager.RouteIdOf(gs, report) == route.routeId;

                // ② 时刻槽单：routeId 在 slotRouteId，origin 是机场三字码 —— 必须认 slotRouteId
                var slot = new InboxMail
                {
                    id = "m_slot_1", sender = "战略发展部", action = InboxManager.ApplySlotAction,
                    origin = "PEK", dest = "12", slotRouteId = route.routeId
                };
                bool slotOk = InboxManager.RouteIdOf(gs, slot) == route.routeId;

                // ③ buy_slot / apply_right：origin 是机场三字码，不得被当成航线
                var buySlot = new InboxMail
                {
                    id = "m_buy_1", sender = "战略发展部", action = "buy_slot", origin = "PEK", dest = "1",
                    subject = "关于 PEK 时刻槽采购", body = "请批准向协调池申请 12:00 时刻槽。"
                };
                bool buySlotOk = InboxManager.RouteIdOf(gs, buySlot) == "";

                // ③b 字段全空的回执（早期存档最坏情况）：靠正文里的「PEK—PVG」认出本线
                var textOnly = new InboxMail
                {
                    id = "fx_m_legacy_11", sender = "运营部", role = "执行回执", status = "approved",
                    subject = "关于 PEK—PVG 运力调整（增幅 1 段）",
                    body = "我司现就「北京首都 PEK—上海浦东 PVG」运力变动，向您汇报。"
                };
                bool textScanOk = InboxManager.RouteIdOf(gs, textOnly) == route.routeId;

                // ③c 正文提到另一条无关航线：不得被误认成本线
                bool noFalsePositive = InboxManager.RouteIdOf(gs, new InboxMail
                {
                    id = "m_other_1", sender = "财务部", subject = "燃油对冲开仓",
                    body = "本次开仓与 PEK—CAN 无关，仅按布伦特曲线执行。"
                }) == "";

                // ③d 往返写法 ⇄ 与单向箭头 → 也要认得出（仓库里这两种写法都用）
                bool arrowOk = InboxManager.RouteIdOf(gs, new InboxMail
                {
                    id = "m_arrow_1", sender = "运营部", subject = "本季加班计划",
                    body = $"拟在 {route.origin}⇄{route.destination} 上加开 2 班。"
                }) == route.routeId
                && InboxManager.RouteIdOf(gs, new InboxMail
                {
                    id = "m_arrow_2", sender = "运营部", subject = "排班说明",
                    body = $"去程 {route.origin}→{route.destination} 维持 1 班。"
                }) == route.routeId;

                // ④ 旧档回执没存 origin：按 fx_<源件 id>_<年><季> 回填
                var legacy = new InboxMail
                {
                    id = "fx_" + report.id + "_11", sender = "运营部", role = "执行回执",
                    status = "approved", tag = "回执"
                };
                // ④b 运力调令回执的前缀是 fx_cap_（不是 fx_）：只剥 fx_ 会导致永远匹配不上源件
                var legacyCap = new InboxMail
                {
                    id = "fx_cap_" + report.id + "_11", sender = "运营部", role = "执行回执",
                    status = "approved", tag = "回执"
                };
                gs.inbox.Add(report);
                gs.inbox.Add(legacy);
                gs.inbox.Add(legacyCap);
                InboxManager.BackfillReceiptRouteRefs(gs);
                bool backfillOk = InboxManager.RouteIdOf(gs, legacy) == route.routeId;
                bool backfillCapOk = InboxManager.RouteIdOf(gs, legacyCap) == route.routeId;

                // ⑤ 幂等 + 不越界：再跑一次不会改动已填的值，也不会给无源件的回执编一条线
                var orphan = new InboxMail
                {
                    id = "fx_m_unknown_11", sender = "运营部", role = "执行回执", status = "approved"
                };
                gs.inbox.Add(orphan);
                string before = legacy.origin;
                InboxManager.BackfillReceiptRouteRefs(gs);
                bool idempotent = legacy.origin == before && string.IsNullOrEmpty(orphan.origin);

                // ⑥ 运力 / 时刻槽一路的邮件都要摘「董事会」；财务、治理类不受影响
                bool opsFlags = InboxManager.IsCapacityOrSlotMail(report)
                                && InboxManager.IsCapacityOrSlotMail(slot)
                                && InboxManager.IsCapacityOrSlotMail(legacy)
                                && !InboxManager.IsCapacityOrSlotMail(
                                    new InboxMail { action = InboxManager.AnnualBudgetAction, role = "财务" });

                bool all = reportOk && slotOk && buySlotOk && backfillOk && backfillCapOk && idempotent && opsFlags
                           && textScanOk && noFalsePositive && arrowOk;
                Debug.Log($"底栏认线 → 呈报={reportOk}（期望 True）　槽单={slotOk}（期望 True）"
                          + $"　机场码不当航线={buySlotOk}（期望 True）　旧回执回填={backfillOk}（期望 True）"
                          + $"　运力回执 fx_cap_ 前缀={backfillCapOk}（期望 True）"
                          + $"　正文认线={textScanOk}（期望 True）　箭头/往返写法={arrowOk}（期望 True）"
                          + $"　无关航线不误认={noFalsePositive}（期望 True）"
                          + $"　幂等/不越界={idempotent}（期望 True）　运力槽类摘董事会={opsFlags}（期望 True）"
                          + $"　{(all ? "✓" : "✗")}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }
    }
}
#endif
