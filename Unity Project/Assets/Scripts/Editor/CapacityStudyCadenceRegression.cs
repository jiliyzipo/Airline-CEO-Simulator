#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 运力调令的**研究期**节奏与「重下同一条令」的处置（2026-11-04 报障）。
    ///
    /// 用户原话：
    /// <list type="number">
    /// <item>「在命令台下达调整航线运力指令后，2 个工作日回复邮件时间长，改成 1 个工作日回复。」</item>
    /// <item>「1 号输入增加某航线运力指令，提示 2 个工作日后回复邮件，在第二天又输入增加这条航线的运力指令，
    ///   又提示 2 个工作日后回复邮件。这是个 BUG 吗？」</item>
    /// </list>
    ///
    /// 第二条原来是**真 bug**：同一航线重下同一句令，`InboxManager.Schedule` 会把同一封待发呈报的
    /// `deliverUtcTicks` 改到「新令 + 研究期」，研究期被一次次重置 —— 越催越等不到。
    /// 修法见 `QueueCapacityReport` 的「同一目标不重排期」分支。
    ///
    /// 本回归钉四件事：
    /// <list type="number">
    /// <item>运力研究期 = 1 个工作日（且与开航研究会的 2 个工作日**分开**，改一个不会串另一个）；</item>
    /// <item>同目标重下：不重排期、不出第二封，`alreadyStudying` 为 true；</item>
    /// <item>改目标重下：必须重新排期（新方案的到件时间当然要重算）；</item>
    /// <item>回执话术能拿到「原令预计 X 送到」，而不是又报一遍「1 个工作日后」。</item>
    /// </list>
    ///
    /// 隔离手段与其它回归相同：内存 GameState + 静态入口，不起壳层、不碰 UI、不写存档。
    /// </summary>
    public static class CapacityStudyCadenceRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__cap_study__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.year = 1;
                gs.quarter = 1;
                // 周一 09:00 UTC：工作日推进一眼可数（+1 工作日 = 周二）
                var t0 = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
                gs.SetSimUtc(t0);
                gs.cash = 50000f;
                gs.onTimePercent = 88f;
                gs.stg = new StrategyState();
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.flightRings = new List<FlightRing>();
                gs.fleet = new List<Aircraft>
                {
                    new Aircraft { registration = "B-3000", type = "A320neo", isActive = true,
                                   status = AircraftStatuses.Airworthy, age = 4 }
                };

                var route = new RouteData
                {
                    routeId = "R-PEK-SHA-CAPSTUDY", origin = "PEK", destination = "SHA",
                    distanceKm = 1077f, aircraftType = "A320neo", flightNumber = "NS242",
                    registration = "B-3000", depTime = "06:30", arrTime = "07:46",
                    daysOfWeek = "每日", weeklyFlights = 7,
                    baseDailyProfit = 10f, dailyProfit = 10f, groundStaffAssigned = 3
                };
                route.SyncFlightsFromPrimary();
                gs.activeRoutes = new List<RouteData> { route };

                // ———— ① 研究期 = 1 个工作日，且与开航研究会分开 ————
                var ready = InboxCadence.CapacityStudyReady(t0);
                var readyLocal = ready.ToLocalTime();
                bool cadenceOk = InboxCadence.CapacityStudyBusinessDays == 1
                                 && InboxCadence.StudyBusinessDays == 2
                                 && readyLocal.DayOfWeek == DayOfWeek.Tuesday
                                 && readyLocal.Date == new DateTime(2026, 11, 3).Date;
                Debug.Log($"R47 ① 运力研究期 {InboxCadence.CapacityStudyBusinessDays} 个工作日（开航研究会仍是 "
                          + $"{InboxCadence.StudyBusinessDays} 个）　周一下达 → {readyLocal:MM-dd ddd HH:mm}（应为周二）"
                          + $"　{cadenceOk.ToString().Replace("True", "✓").Replace("False", "✗")}");

                // ———— ② 同目标重下：不重排期、不出第二封 ————
                var d1 = CapacityDirectiveLogic.Parse("增加到每日3段往返");
                CapacityDirectiveLogic.ResolveStatedTarget(route, d1);
                var rep1 = CapacityDirectiveLogic.Plan(gs, route, d1);
                if (rep1 == null || !rep1.ok)
                    throw new Exception("R47 夹具失效：试算未通过 → " + (rep1?.note ?? "null"));

                bool q1 = InboxManager.QueueCapacityReport(gs, route, rep1, out bool studying1, -1, true);
                long firstAt = PendingCapacityTicks(gs, route.routeId);
                int firstCount = PendingCapacityCount(gs, route.routeId);

                // 第二天再下同一条令（同一目标，原话可以换一种说法）
                gs.SetSimUtc(t0.AddDays(1));
                var d2 = CapacityDirectiveLogic.Parse("加密到每天三班");
                CapacityDirectiveLogic.ResolveStatedTarget(route, d2);
                var rep2 = CapacityDirectiveLogic.Plan(gs, route, d2);
                bool q2 = InboxManager.QueueCapacityReport(gs, route, rep2, out bool studying2, -1, true);
                long secondAt = PendingCapacityTicks(gs, route.routeId);
                int secondCount = PendingCapacityCount(gs, route.routeId);
                string waitText = InboxManager.CapacityStudyWaitText(gs, route);

                bool sameTargetOk = q1 && q2 && !studying1 && studying2
                                    && firstCount == 1 && secondCount == 1
                                    && firstAt > 0 && secondAt == firstAt;
                Debug.Log($"R47 ② 同目标重下 → 首次受理={q1}　重下标记已研究={studying2}（应 True）"
                          + $"　待发呈报 {firstCount} → {secondCount} 封（应 1 → 1）"
                          + $"　到件时刻 {TicksText(firstAt)} → {TicksText(secondAt)}（应不变：研究期不得被重置）"
                          + $"　回执话术=「{waitText}」"
                          + $"　{sameTargetOk.ToString().Replace("True", "✓").Replace("False", "✗")}");
                if (!sameTargetOk)
                    Debug.LogError("R47 ② 同一条调令重下后研究期被顺延（旧 bug 复发）：CEO 越催越等不到回音。");

                // ———— ②b 负面控制：直接走旧路径（Schedule 重排），判定必须能看见时刻被推走 ————
                // 这一条证明「同目标不重排」不是摆设 —— 底层机制本来就**会**把到件时刻改到新令 + 研究期，
                // 只是 QueueCapacityReport 的守卫把它挡住了。若哪天守卫被删，②的 firstAt==secondAt 必然转红。
                var canaryEntry = FindPendingCapacity(gs, route.routeId);
                bool canaryMoved = false;
                if (canaryEntry?.mail != null)
                {
                    var canaryAt = InboxCadence.CapacityStudyReady(gs.SimUtc);
                    InboxManager.Schedule(gs, canaryEntry.mail, canaryAt);
                    canaryMoved = canaryEntry.deliverUtcTicks != firstAt
                                  && canaryEntry.deliverUtcTicks == Ticks(canaryAt);
                    // 复原：让后面的 ③ 仍以「原令到件时刻」为基线
                    canaryEntry.deliverUtcTicks = firstAt;
                }
                if (!canaryMoved)
                    Debug.LogError("R47 ②b canary 失效：直接重排同一封待发件竟没改变到件时刻 —— "
                                   + "那么 ②（同目标不重排）即便全绿也证明不了任何事。");
                Debug.Log($"R47 ②b 负面控制 → 直接重排（旧路径）会把到件时刻推走={canaryMoved}（应 True）"
                          + $"　即「守卫」是唯一拦住顺延的那道闸"
                          + $"　{canaryMoved.ToString().Replace("True", "✓").Replace("False", "✗")}");

                // ———— ③ 改目标重下：必须重新排期 ————
                var t1 = gs.SimUtc;
                var d3 = CapacityDirectiveLogic.Parse("减少到每日1段往返");
                CapacityDirectiveLogic.ResolveStatedTarget(route, d3);
                var rep3 = CapacityDirectiveLogic.Plan(gs, route, d3);
                bool q3 = InboxManager.QueueCapacityReport(gs, route, rep3, out bool studying3, -1, true);
                long thirdAt = PendingCapacityTicks(gs, route.routeId);
                int thirdCount = PendingCapacityCount(gs, route.routeId);
                long expectThird = Ticks(InboxCadence.CapacityStudyReady(t1));
                bool changeTargetOk = q3 && !studying3 && thirdCount == 1
                                      && Math.Abs(thirdAt - expectThird) <= TimeSpan.TicksPerMinute;
                Debug.Log($"R47 ③ 改目标重下 → 受理={q3}　误判为已研究={studying3}（应 False）"
                          + $"　待发呈报 {thirdCount} 封（应 1：旧方案作废，新方案另起）"
                          + $"　新到件 {TicksText(thirdAt)}（应 {TicksText(expectThird)}）"
                          + $"　{changeTargetOk.ToString().Replace("True", "✓").Replace("False", "✗")}");

                bool all = cadenceOk && sameTargetOk && changeTargetOk && canaryMoved;
                Debug.Log($"R47 运力研究期与重复调令 → 研究期 1 工作日={cadenceOk}　同目标不重排={sameTargetOk}"
                          + $"　改目标重排={changeTargetOk}　负面控制={canaryMoved}"
                          + $"　{all.ToString().Replace("True", "✓").Replace("False", "✗")}");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static int PendingCapacityCount(GameState gs, string routeId)
        {
            if (gs?.pendingInbox == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.pendingInbox.Count; i++)
            {
                var m = gs.pendingInbox[i]?.mail;
                if (m == null || m.action != InboxManager.CapacityDirectiveAction) continue;
                if (!string.Equals(m.origin, routeId, StringComparison.OrdinalIgnoreCase)) continue;
                n++;
            }
            return n;
        }

        static long PendingCapacityTicks(GameState gs, string routeId)
            => FindPendingCapacity(gs, routeId)?.deliverUtcTicks ?? 0;

        /// <summary>本线在待发队列里的那封运力呈报（没有则 null）。</summary>
        static PendingInboxMail FindPendingCapacity(GameState gs, string routeId)
        {
            if (gs?.pendingInbox == null) return null;
            for (int i = 0; i < gs.pendingInbox.Count; i++)
            {
                var p = gs.pendingInbox[i];
                var m = p?.mail;
                if (m == null || m.action != InboxManager.CapacityDirectiveAction) continue;
                if (!string.Equals(m.origin, routeId, StringComparison.OrdinalIgnoreCase)) continue;
                return p;
            }
            return null;
        }

        static long Ticks(DateTime utc) => utc.ToUniversalTime().Ticks;

        static string TicksText(long ticks) => ticks <= 0
            ? "无"
            : new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("MM-dd HH:mm");
    }
}
#endif
