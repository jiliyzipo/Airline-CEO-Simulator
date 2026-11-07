using System;
using System.Collections.Generic;
using System.Linq;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.PlayMode
{
    public class DevelopmentSlotWorkflowTests : IsolatedPlayModeFixture
    {
        GameState Fixture(out RouteData route)
        {
            if (Application.companyName != "AirlineCEORegression") Assert.Ignore("Isolated baseline only.");
            var gs = new GameObject("SlotWorkflowGS").AddComponent<GameState>();
            gs.year = 1; gs.quarter = 1; gs.cash = 100000f;
            gs.SetSimUtc(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
            gs.fleet.Clear(); gs.activeRoutes.Clear(); gs.inbox.Clear(); gs.pendingInbox.Clear();
            gs.stg = new StrategyState();
            for (int i = 0; i < 3; i++) gs.fleet.Add(new Aircraft { registration = "B-31" + i,
                type = "A320neo", status = AircraftStatuses.Airworthy, isActive = true, condition = 100 });
            route = new RouteData { routeId = "PIPELINE-PEK-SHA", origin = "PEK", destination = "SHA",
                aircraftType = "A320neo", registration = "B-310", flightNumber = "NG310",
                distanceKm = 1077, depTime = "08:00", arrTime = "09:16", daysOfWeek = "每日", weeklyFlights = 7,
                baseDailyProfit = 12, dailyProfit = 12, groundStaffAssigned = 3 };
            route.SyncFlightsFromPrimary(); gs.activeRoutes.Add(route);
            return gs;
        }

        /// <summary>
        /// 把模拟时钟推到该单的**结算到期时刻**，再跑一次结算。
        ///
        /// ★2026-11-02 CEO 裁决「审批按天、结算按季」：许可与时刻改为按绝对时刻
        /// （<see cref="InboxMail.dueUtcTicks"/>）到期，逐日比对。只要季度往前走、时钟不动，
        /// 结算就**不会**发生 —— 这正是本文件几条用例原来红掉的原因（靠 `gs.quarter = 2` 触发季末结算，
        /// 验的是一套已经废止的刻度）。这里推进时钟而不是改断言：交割日之后必须真结算。
        /// </summary>
        static void SettleByClock(GameState gs, InboxMail mail)
        {
            Assert.IsNotNull(mail, "结算对象必须存在");
            Assert.AreNotEqual(0L, mail.dueUtcTicks, "批准后必须排定结算到期时刻");
            gs.SetSimUtc(new DateTime(mail.dueUtcTicks, DateTimeKind.Utc));
            InboxManager.ProcessPendingEffects(gs);
        }

        [Test]
        public void PartialSettlement_WaitsForOtherStation_ThenProducesCeoAssignment()
        {
            var gs = Fixture(out var route);
            // ★2026-11-07：这条用例原来走**免费协调池**（申请 → 审批 → 交割 → 现金不变）。
            // 但用户裁决把他航占用抬到 85–92%、且逐刻计容量之后，每个时段池实际只剩 1–2 条免费槽
            // （`IndustryAirlineLogic.MustLeaveOpen` 硬留的那一条），而一个往返要**两个钟点同时空着**
            // —— 枢纽机场（PEK/SHA）几乎不可能凑出来，这是「四端凑不齐」在免费渠道上的表现。
            // 用户 2026-11-07 的裁决是：能等就等、不能等就走二级市场，办法由部门算。
            // 故本用例改验**交割语义本身**：一端交割后仍排不了、两端齐了才排得了，
            // 通道换成当时唯一还能成交的那条（二级市场，当场扣款）。
            // 免费渠道的交割另由 `TimeChange_SettlesOnlyWhenCoordinatorHasCapacity` 覆盖（那块池子是空的）。
            string dep = SlotFixturePick.ObtainableDeparture(gs, route);
            Assert.IsNotNull(dep, "PEK—SHA 全天没有一刻两端都还买得到（既无空槽、也无持有者可买）。");
            var needs = RoutePermitLogic.ClockNeeds(route, dep);
            Assert.AreEqual(2, needs.Count);
            Assert.IsTrue(SlotFixturePick.Claim(gs, needs[0]), "第一站交割失败：空槽与二级市场都没拿到。");
            Assert.IsFalse(RoutePermitLogic.CanScheduleClock(gs, route, dep, true));
            Assert.AreEqual(1, route.FlightCount);
            Assert.IsTrue(SlotFixturePick.Claim(gs, needs[1]), "第二站交割失败：空槽与二级市场都没拿到。");
            Assert.IsTrue(RoutePermitLogic.CanScheduleClock(gs, route, dep, true));
            Assert.AreEqual(1, route.FlightCount, "Settlement itself does not impersonate CEO approval.");
            Assert.IsTrue(InboxManager.QueueAssignFlight(gs, route, "B-311", dep, out string assignWhy), assignWhy);
            int holdings = gs.stg.slots.Count;
            InboxManager.ProcessPendingEffects(gs);
            Assert.AreEqual(holdings, gs.stg.slots.Count, "Settled effects must not replay.");
            var assignment = gs.inbox.Find(x => x.action == InboxManager.AssignFlightAction && InboxManager.IsOpen(x));
            Assert.IsTrue(InboxManager.ApproveAssignFlight(gs, assignment, out string execution), execution);
            Assert.Greater(route.FlightCount, 1, execution);
            DateTime departure = new DateTime(2026, 1, 5, 0, 0, 0)
                .AddMinutes(ScheduleSlots.ParseClock(dep)) - GameState.LocalUtcOffset;
            gs.SetSimUtc(departure.AddHours(6));
            LiveClockSimulator.Advance(gs, departure.AddMinutes(-1), gs.SimUtc);
            Assert.Greater(gs.liveQuarterIncome, 0f, "The newly approved flight must actually operate and earn revenue.");
            Assert.IsTrue(gs.fleet.Any(a => a.flightCycles > 0));
        }

        [Test]
        public void RejectDirective_RestoresBaseline_WithoutChargingOrAcquiringSlots()
        {
            var gs = Fixture(out var route); float cash = gs.cash;
            var report = CapacityDirectiveLogic.Execute(gs, route, CapacityDirectiveLogic.Parse("增加到每日3段往返"));
            // delayHours: 0 = 立即入箱。线上路径按 InboxCadence.ReportHours 延后投递（2026-10-19 改），
            // 本用例断言的是「驳回能不能把方案退回去」，所以必须直接拿箱里那封，而不是等计时器。
            Assert.IsTrue(InboxManager.QueueCapacityReport(gs, route, report, delayHours: 0));
            var mail = gs.inbox.Find(x => x.action == InboxManager.CapacityDirectiveAction && InboxManager.IsOpen(x));
            Assert.IsNotNull(mail, "呈报应已入箱（delayHours=0）；否则驳回路径拿到 null 会直接 NRE。");
            Assert.IsTrue(InboxManager.RejectCapacityDirective(gs, mail, out _));
            Assert.AreEqual("rejected", mail.status);
            Assert.AreEqual(1, route.FlightCount);
            Assert.AreEqual(cash, gs.cash);
            Assert.IsFalse(route.directivePending);
            Assert.IsFalse(gs.inbox.Any(x => x.action == InboxManager.ApplySlotAction && x.effectPending));
            Assert.AreEqual(0, gs.stg.slots.Count);
        }

        [TestCase("pending")]
        [TestCase("approved")]
        [TestCase("assignment")]
        [TestCase("airborne")]
        public void Application_SaveLoad_PreservesStageAndExactClocks(string stage)
        {
            var gs = Fixture(out var route);
            // ★2026-11-07：本用例验的是「开单 → 交割 → 存档 → 读档」这条链上的字段保真
            // （时刻、单号、到期刻），不依赖峰点是否有免费槽：
            // 开单只看时段池余量（`QueueApplySlots` 的判据），交割阶段改走「空槽或买入」
            // （`SlotFixturePick.Claim`）—— 他航占 85–92% 后，18:00 这种峰点只能买。
            string dep = SlotFixturePick.ObtainableDeparture(gs, route);
            Assert.IsNotNull(dep, "PEK—SHA 全天没有一刻两端都还办得下来（既无空槽、也无持有者可买）。");
            Assert.IsTrue(InboxManager.QueueApplySlots(gs, "PEK", dep, 1, "save_slot", out _,
                routeId: route.routeId, flightClock: dep));
            if (stage != "pending") Assert.IsTrue(InboxManager.Approve(gs, gs.inbox[0], out _));
            if (stage == "assignment")
            {
                foreach (var need in RoutePermitLogic.ClockNeeds(route, dep))
                {
                    // ★2026-11-05：15 分钟粒度下同一刻只有 0/1/2 条容量，不再允许
                    // 「同一机场同一刻买第二条兜底」——已持有的就跳过，别把铺开模型当堆叠模型。
                    //
                    // 且申请时刻必须先过 `SlotRequestClock` 吸附到 15 分钟网格：`ClockNeeds` 给的是
                    // 轮挡推出的**实际到场时刻**（5 分钟粒度，如 19:40），而槽的身份是网格时刻（19:45）。
                    // 生产路径（`InboxManager` 开单/交割、`SlotCandidateLogic`）都先吸附，用例照同一口径走，
                    // 否则测的是一条线上根本不存在的「按 19:40 买 19:45 的槽」。
                    string want = RoutePermitLogic.SlotRequestClock(need.time);
                    bool held = gs.stg.slots.Any(s => s != null
                        && string.Equals(s.airport, need.airport, StringComparison.OrdinalIgnoreCase)
                        && ScheduleSlots.ClockDiff(s.slotTime, want) <= ScheduleSlots.SlotToleranceMinutes);
                    if (held) continue;
                    // 先走协调池（免费），拿不到再走二级市场 —— 与运营部/候选引擎同一条路。
                    Assert.IsTrue(SlotFixturePick.Claim(gs, need), need.airport + " " + want);
                }
                Assert.IsTrue(InboxManager.QueueAssignFlight(gs, route, "B-311", dep, out _));
            }
            if (stage == "airborne")
            {
                DateTime departure = new DateTime(2026, 1, 5, 8, 0, 0) - GameState.LocalUtcOffset;
                gs.SetSimUtc(departure.AddMinutes(1));
                LiveClockSimulator.Advance(gs, departure.AddMinutes(-1), gs.SimUtc);
                Assert.IsTrue(route.flights[0].airborne, "The flight must actually take off before saving.");
            }
            var save = new GameObject("SlotWorkflowSave").AddComponent<SaveManager>();
            long dueTicks = gs.inbox.Find(x => x.id == "save_slot").dueUtcTicks;
            save.Save();
            gs.inbox.Clear(); gs.cash = -123;
            save.Load();
            var restored = gs.inbox.Find(x => x.id == "save_slot");
            Assert.IsNotNull(restored);
            Assert.AreEqual(stage != "pending", restored.effectPending);
            Assert.AreEqual(stage == "pending" ? "pending" : "approved", restored.status);
            Assert.AreEqual("PEK", restored.origin);
            Assert.AreEqual(dep, restored.slotClock);
            Assert.AreEqual(route.routeId, restored.slotRouteId);
            if (stage != "pending")
            {
                Assert.AreNotEqual(0L, dueTicks, "批准后必须排定结算到期时刻");
                Assert.AreEqual(dueTicks, restored.dueUtcTicks, "存档必须原样带回结算时刻（天级口径）");
            }
            if (stage == "assignment") Assert.IsTrue(gs.inbox.Any(x => x.action == InboxManager.AssignFlightAction && InboxManager.IsOpen(x)));
            if (stage == "airborne") Assert.IsTrue(gs.activeRoutes[0].flights[0].airborne);
        }

        [Test]
        public void QuestionAndCrewShortage_DoNotSilentlyAddFlights()
        {
            var gs = Fixture(out var route);
            var before = JsonUtility.ToJson(route);
            var answer = OpsQueryLogic.Build(gs, route, "为什么不能增加班次？");
            Assert.AreEqual(before, JsonUtility.ToJson(route));
            gs.staffPool = new List<PersonnelData> {
                new PersonnelData { id = "one-pilot", department = "OPS", jobTitle = "飞行员", isOperational = true, onMarket = false },
                new PersonnelData { id = "one-cabin", department = "OPS", jobTitle = "乘务员", isOperational = true, onMarket = false }
            };
            CapacityDirectiveLogic.Execute(gs, route, CapacityDirectiveLogic.Parse("增加到每日3段往返"));
            Assert.AreEqual(1, route.FlightCount, "No new wave may bypass either crew role.");
            gs.fleet[0].status = AircraftStatuses.Retired;
            Assert.AreNotEqual(GroundReason.None, FlightGroundLogic.RouteOf(gs, route));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TimeChange_SettlesOnlyWhenCoordinatorHasCapacity(bool fillTargetPool)
        {
            var gs = Fixture(out var route);

            // ★2026-11-07：容量放大 + 他航占 85–92% 后，写死的 09:30/12:00 不再保证一个空一个满。
            // 夹具改为按生产闸门找一组「原刻办得下、目标刻改得过去」的时刻：
            // 先找一条能办下的离港槽（免费或二级市场），再找同机场另一刻且 `SlotChangeRoom` 放行。
            string from = null, to = null;
            foreach (int m in ScheduleSlots.SlotGrid24())
            {
                string c = ScheduleSlots.FormatClock(m);
                if (AirportCatalog.InCurfew("PEK", m)) continue;
                if (!(StrategyManager.AcquireSlotsAt(gs, "PEK", c, 1, true, out _, out _)
                      || SlotMarketLogic.BuyClock(gs, "PEK", c, SlotHolding.SlotKindDeparture, null, out _)))
                    continue;
                foreach (int m2 in ScheduleSlots.SlotGrid24())
                {
                    string t = ScheduleSlots.FormatClock(m2);
                    if (AirportCatalog.InCurfew("PEK", m2)) continue;
                    if (ScheduleSlots.ClockDiff(c, t) <= ScheduleSlots.SlotToleranceMinutes) continue;
                    // 目标刻本身必须有公布容量（`QuarterSplit` 里 0 条的刻不是槽位），
                    // 否则「填满反例」无从谈起。
                    if (RoutePermitLogic.SlotsAvailableAt(gs, "PEK", t, SlotHolding.SlotKindDeparture) <= 0) continue;
                    if (!RoutePermitLogic.SlotChangeRoom(gs, "PEK", c, t)) continue;
                    from = c; to = t; break;
                }
                if (to != null) break;
            }
            Assert.IsNotNull(to, "PEK 找不到一组「原刻办得下 + 目标刻改得过去」的改时对，夹具失效。");

            Assert.IsTrue(InboxManager.QueueChangeSlot(gs, "PEK", from, to, 1, "change-test", out string qm), qm);
            var mail = gs.inbox.Find(x => x.id == "change-test");
            Assert.IsTrue(InboxManager.Approve(gs, mail, out _));
            if (fillTargetPool)
            {
                // ★2026-11-07：先前用 `AcquireSlotsAt(to, cap)` 一次买整段，但 n>1 会**铺到同段邻近刻**
                // （`QuarterOrder`），目标刻自身未必被填满 —— 改时闸门照样放行，反例白设。
                // 这里逐条按「就要这一刻」买（n=1 不挪刻），一直买到目标刻没有余量为止。
                int guard = 0;
                while (RoutePermitLogic.SlotsAvailableAt(gs, "PEK", to, SlotHolding.SlotKindDeparture) > 0
                       && guard++ < 8)
                {
                    if (!StrategyManager.AcquireSlotsAt(gs, "PEK", to, 1, true, out _, out _)) break;
                }
                Assert.IsFalse(RoutePermitLogic.SlotChangeRoom(gs, "PEK", from, to),
                    "反例夹具失效：目标刻已无余量，改时闸门仍放行。");
            }
            float cash = gs.cash;
            SettleByClock(gs, mail);
            bool oldHolding = gs.stg.slots.Any(s => s != null && s.airport == "PEK" && !s.inUse
                && ScheduleSlots.ClockDiff(s.slotTime, from) <= ScheduleSlots.SlotToleranceMinutes);
            Assert.AreEqual(fillTargetPool, oldHolding, "Rejected change must preserve original holding; successful change replaces it.");
            Assert.AreEqual(cash, gs.cash, "Coordinator time changes are free.");
            Assert.IsFalse(mail.effectPending);
        }

        [Test]
        public void Stopover_RequiresBothDirectionsAtIntermediateStation()
        {
            var gs = Fixture(out var route);
            route.destination = "SZX"; route.stopover = "SHA"; route.distanceKm = 1900;

            var shape = RoutePermitLogic.ClockNeeds(route, "18:00");
            Assert.AreEqual(4, shape.Count, "Origin, destination and two separate intermediate calls are required.");
            Assert.AreEqual(2, shape.Count(x => x.airport == "SHA"));
            Assert.IsTrue(shape.Any(x => x.airport == "SHA" && x.inbound));
            Assert.IsTrue(shape.Any(x => x.airport == "SHA" && !x.inbound));

            // ★2026-11-05：容量改由小时容量派生后，**并非每个刻钟都是公布槽位**
            //（容量 3 的时段只公布 3 个刻），而经停端/对端时刻是由轮挡推出来的，
            // 某个去程时刻可能天生落不到公布刻上 —— 那与「经停端要两向槽」是两件事。
            // 故先在候选去程时刻里找一组**四端都办得下**的，再验经停判据本身。
            string dep = null;
            List<RoutePermitLogic.SlotNeed> needs = null;
            // ★2026-11-07：候选从「晚高峰 16 个刻」扩到**全天 15 分钟网格**，且每端先走协调池、
            // 拿不到再走二级市场（`SlotFixturePick.Claim`）。他航占 85–92% 之后晚峰四端同时空着
            // 的概率已接近 0，仍只盯晚峰等于在验「那天恰好有空」，而不是验经停两向的判据。
            foreach (int candMin in ScheduleSlots.SlotGrid24())
            {
                string cand = ScheduleSlots.FormatClock(candMin);
                var ns = RoutePermitLogic.ClockNeeds(route, cand);
                if (ns.Count != 4) continue;
                int mark = gs.stg.slots.Count;
                float cashMark = gs.cash;
                bool all = true;
                for (int i = 0; i < ns.Count && all; i++) all = SlotFixturePick.Claim(gs, ns[i]);
                if (all && RoutePermitLogic.CanScheduleClock(gs, route, cand, true))
                {
                    dep = cand; needs = ns; break;
                }
                if (gs.stg.slots.Count > mark) gs.stg.slots.RemoveRange(mark, gs.stg.slots.Count - mark);
                gs.cash = cashMark;
            }
            Assert.IsNotNull(dep, "候选去程时刻里没有一组四端都能办下槽的：容量表与经停推导可能已不相容。");

            Assert.IsTrue(RoutePermitLogic.CanScheduleClock(gs, route, dep, true), "四端齐备时该去程必须可排。");

            // 撤掉经停端回程那一条 —— 一趟往返在经停站停两次，只办一半不能排。
            var last = needs[needs.Count - 1];
            string lastClock = RoutePermitLogic.SlotRequestClock(last.time);
            gs.stg.slots.RemoveAll(s => s != null
                && string.Equals(s.airport, last.airport, StringComparison.OrdinalIgnoreCase)
                && ScheduleSlots.ClockDiff(s.slotTime, lastClock) <= ScheduleSlots.SlotToleranceMinutes);
            Assert.IsFalse(RoutePermitLogic.CanScheduleClock(gs, route, dep, true),
                "只办去程一半不能排 —— 经停端两向都要槽。");
            Assert.IsTrue(SlotFixturePick.Claim(gs, last), last.airport + " " + lastClock);
            Assert.IsTrue(RoutePermitLogic.CanScheduleClock(gs, route, dep, true));
        }

        [TestCase("飞行员")]
        [TestCase("乘务员")]
        public void EachCrewRoleIndependentlyConstrainsCapacity_AndRecoversAfterStaffing(string scarceRole)
        {
            var gs = Fixture(out var route);
            gs.staffPool = new List<PersonnelData>();
            foreach (var role in new[] { "飞行员", "乘务员" })
                for (int i = 0; i < (role == scarceRole ? 1 : 100); i++)
                    gs.staffPool.Add(new PersonnelData { id = role + i, jobTitle = role,
                        isOperational = true, department = "OPS", source = "market" });
            gs.InvalidateCrewCache();
            Assert.IsFalse(CrewCapacityLogic.CanCrew(gs, route, 3, out _));
            for (int i = 1; i < 100; i++) gs.staffPool.Add(new PersonnelData { id = scarceRole + i,
                jobTitle = scarceRole, isOperational = true, department = "OPS", source = "market" });
            gs.InvalidateCrewCache();
            Assert.IsTrue(CrewCapacityLogic.CanCrew(gs, route, 3, out string why), why);
            Assert.AreEqual(1, route.FlightCount, "Checking staffing cannot delete the existing service.");
        }
    }

    /// <summary>
    /// 测试夹具共用的「此刻真能办到一条槽」工具（★2026-11-07）。
    ///
    /// 背景：他航占用提到 85–92%、且**逐刻计入容量**之后，峰点（PEK 18:00 之类）在协调池里
    /// 已经开不出申请单 —— 这是容量表要如实反映的事实，不是缺陷。但若干流程用例把峰点写死了，
    /// 于是它们失败在「那天恰好没空」上，而不是失败在被验的流程上。
    ///
    /// 这里不另写一套「测试以为能申请」的规则：判据直接用生产闸门
    /// <see cref="RoutePermitLogic.CanApplyEnd"/> 与 <see cref="StrategyManager.AcquireSlotsAt"/> /
    /// <see cref="SlotMarketLogic.BuyClock"/>，夹具挑中的时刻在生产里就是真能办到的时刻。
    /// </summary>
    internal static class SlotFixturePick
    {
        /// <summary>
        /// 当天第一个「两端**总办得下来**」的去程时刻：该刻要么还有空槽（免费），
        /// 要么被他航持有（可买）。判据与 <see cref="Claim"/> 的两条渠道同源。
        ///
        /// 为什么不能只要求「空槽」：他航占 85–92% 后每波只剩 1–2 个空刻，
        /// 一个往返要两个钟点同时空着，枢纽机场基本凑不出 —— 这正是要验「买」的原因。
        /// </summary>
        public static string ObtainableDeparture(GameState gs, RouteData route)
        {
            foreach (int m in ScheduleSlots.SlotGrid24())
            {
                string c = ScheduleSlots.FormatClock(m);
                // ★2026-11-07：容量按真实曲线放大后，宵禁机场的夜间刻也有容量值（如 PEK 00:00）。
                // 那些刻**排不进班**（`OpsRotationLogic` 两端都拦宵禁），夹具不能挑它 ——
                // 否则「挑到了却飞不了」会被误报成流程缺陷。
                if (AirportCatalog.InCurfew(route.origin, m)) continue;
                if (AirportCatalog.InCurfew(route.destination, m)) continue;
                var needs = RoutePermitLogic.ClockNeeds(route, c);
                if (needs.Count == 0) continue;
                bool all = true;
                for (int i = 0; i < needs.Count && all; i++)
                {
                    string clk = RoutePermitLogic.SlotRequestClock(RoutePermitLogic.TimeOf(needs[i]));
                    string kind = RoutePermitLogic.NormalizeKind(needs[i].kind);
                    int nm = ScheduleSlots.ParseClock(clk);
                    if (AirportCatalog.InCurfew(needs[i].airport, nm)) { all = false; break; }
                    all = RoutePermitLogic.QuarterCapacityAt(needs[i].airport, clk) > 0
                          && (RoutePermitLogic.SlotsAvailableAt(gs, needs[i].airport, clk, kind) > 0
                              || IndustryAirlineLogic.IndustryAtClock(gs, needs[i].airport, clk, kind) > 0);
                }
                if (all) return c;
            }
            return null;
        }

        /// <summary>
        /// 给某一端办下一条槽：先走协调池（免费），拿不到再走二级市场（付费）。
        /// 两条都是生产渠道 —— 「池满就只能等」不成立，现实里还可以从持有者手里买。
        /// 配对到港时刻只用于**记账**（写进持仓、供浮窗显示），不参与这里的可行性判定：
        /// 到港端的把关在 <see cref="RoutePermitLogic.CanApplyStop"/>，申请阶段已经过了一遍。
        /// </summary>
        public static bool Claim(GameState gs, RoutePermitLogic.SlotNeed need)
        {
            string clk = RoutePermitLogic.SlotRequestClock(RoutePermitLogic.TimeOf(need));
            string arr = RoutePermitLogic.ArrTimeOf(need);
            if (StrategyManager.AcquireSlotsAt(gs, need.airport, clk, 1, true, out _, out _, need.kind, arr))
                return true;
            if (SlotMarketLogic.BuyClock(gs, need.airport, clk, need.kind, arr, out _))
                return true;
            // 到港端无货也不该让整条夹具失败：持仓不记落地时刻时按「通配」处理（旧档同口径）。
            if (StrategyManager.AcquireSlotsAt(gs, need.airport, clk, 1, true, out _, out _, need.kind, null))
                return true;
            return SlotMarketLogic.BuyClock(gs, need.airport, clk, need.kind, null, out _);
        }
    }
}
