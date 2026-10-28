using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 大修 → 停场 → 出厂 这条链路的隔离回归（2026-10-27）。
    ///
    /// 事故现场（用户点飞机详情里的 `安排大修` 时问「安排大修是维护吗？」）：
    /// 那条链路把飞机置为 `维护中` 是没问题的，但**配套的三件事都是假的**：
    ///
    /// ① **`停场 N 天` 从未生效**。`days` 只拼进日志和文案，`Aircraft` 上没有任何字段承载窗口，
    ///   所以飞机进维护后**永远不会自己出厂**；唯一的出口是财年末 `RefreshStatus`，
    ///   而它还会把「机龄 &gt; 15」的飞机**永久钉在维护中**。于是「停场 2 天」与「停场十年」
    ///   在状态机上是同一件事。
    /// ② **「不可预见费(+12%)」只显示、不扣**。n107 列了三行钱，`ScheduleOverhaul` 只校验、
    ///   只扣预估成本那一行 —— 玩家拿屏幕上的合计去对现金，永远差 12%。
    /// ③ **「写入机务维修甘特图」不存在**。全仓只有那一句 Note 里有「甘特」二字。
    ///
    /// 本回归钉住十四条，其中 ②③④⑥⑪ 是**反例**（旧实现必红），不是补充说明：
    /// ① 扣款含不可预见费，且严格大于预估成本；
    /// ② 现金恰好卡在「够预估、不够合计」时必须被拒 —— 旧实现会放行并扣款；
    /// ③ 排程写入的窗口 == 排程时刻 + 停场天数（按 UTC ticks 精确相等）；
    /// ④ 窗口未到期不复飞，即便财年末 `RefreshStatus` 刷过一遍（旧实现会提前放出）；
    /// ⑤ 窗口到期复飞一次，状态回适航、窗口清零、写决策日志；
    /// ⑥ 复飞**不得**二次结算机况（避免同一次大修被算两遍）；
    /// ⑦ 维护中的飞机再次排大修被拒，且不扣钱；
    /// ⑧ 已退役的飞机不得排大修；
    /// ⑨ `MaintenanceState.groundedCount` 是派生量：维修中 / 不可用算停飞，退役不算；
    /// ⑩ 机龄超 15 且机况不足门槛 → 财年末停场（窗口为 0，不给「到期复飞」的假承诺）；
    /// ⑪ 同上但机况被大修抬过门槛 → 继续适航（旧实现恒为维护中，大修对老飞机毫无作用）；
    /// ⑫ 边界：机龄 **等于** 上限仍放行（判定必须是 &gt; 上限）；
    /// ⑬ 年结预判口径必须含 −2 点磨损（否则 51% 的飞机会被 UI 写成「达标」却在年结停场）；
    /// ⑭ 列表徽标判据：超龄且不达标才挂、抬过门槛撤掉、停场窗口内不叠、退役与年轻机不提示。
    /// </summary>
    public static class OverhaulMaintenanceRegression
    {
        const string Reg = "B-TEST-OVERHAUL";

        public static bool Run()
        {
            var go = new GameObject("__overhaul_maintenance__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.fleet = new List<Aircraft>();
                gs.activeRoutes = new List<RouteData>();
                gs.inbox = new List<InboxMail>();
                gs.cash = 1000f;
                gs.year = 2026;
                gs.quarter = 4;
                // 固定时钟：窗口断言要按 ticks 精确相等，不能跟着 DateTime.UtcNow 漂
                var t0 = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);
                gs.SetSimUtc(t0);

                var ac = new Aircraft
                {
                    registration = Reg,
                    type = "B737-800",          // 窄体，不乘宽体系数，成本可手算
                    isActive = true,
                    status = AircraftStatuses.Airworthy,
                    age = 5,
                    condition = 60f,
                    purchasePrice = 9600f,
                    economySeats = 180,
                };
                gs.fleet.Add(ac);

                var sb = new StringBuilder();
                bool ok = true;
                void Check(string id, bool pass, string detail)
                {
                    if (!pass) ok = false;
                    sb.Append($"\n　{(pass ? "✓" : "✗")} {id}：{detail}");
                }

                const string kind = FleetEconomics.OverhaulA;   // 10 万 / 停场 2 天
                float cost = FleetEconomics.OverhaulCost(kind, false);
                float contingency = FleetEconomics.OverhaulContingency(kind, false);
                float expectCash = FleetEconomics.OverhaulCashCost(kind, false);
                int days = FleetEconomics.OverhaulDays(kind);

                // ② 现金恰好够预估、不够合计：旧实现（只校验 cost）会放行并扣款
                gs.cash = cost + 1f;
                bool bought = FleetManager.ScheduleOverhaul(gs, ac, kind, out string cashMsg);
                Check("②现金按合计校验", !bought && Mathf.Abs(gs.cash - (cost + 1f)) <= 0.0001f,
                    bought ? $"旧行为：只够预估 {cost:F1} 万也放行（应拒绝）"
                           : $"正确拒绝且未扣款，现金仍 {gs.cash:F2} 万：「{cashMsg}」");

                // ① 扣款含不可预见费
                gs.cash = 1000f;
                bool scheduled = FleetManager.ScheduleOverhaul(gs, ac, kind, out string msg);
                float paid = 1000f - gs.cash;
                Check("①扣款含不可预见费",
                    scheduled && Mathf.Abs(paid - expectCash) <= 0.0001f && paid > cost + 0.0001f,
                    $"实扣 {paid:F2} 万 vs 预估 {cost:F1} + 不可预见费 {contingency:F2} = {expectCash:F2} 万"
                    + $"（旧实现只扣 {cost:F1} 万）");

                // ③ 窗口精确等于 排程时刻 + 停场天数
                long expectTicks = t0.AddDays(days).Ticks;
                Check("③窗口写入", ac.maintenanceUntilUtcTicks == expectTicks && ac.status == AircraftStatuses.Maintenance,
                    $"状态 {ac.status}，窗口 == t0+{days} 天（差 {ac.maintenanceUntilUtcTicks - expectTicks} ticks）");

                // ④ 未到期不复飞，且财年末那一刷也不能提前放出
                int early = FleetManager.ReleaseCompletedOverhauls(gs, t0.AddDays(days - 1));
                FleetManager.RefreshStatus(ac, t0.AddDays(days - 1));      // 模拟财年末机龄刷新
                Check("④未到期不放车",
                    early == 0 && ac.status == AircraftStatuses.Maintenance && ac.maintenanceUntilUtcTicks == expectTicks,
                    $"提前 1 天：复飞 {early} 架；RefreshStatus 后状态 {ac.status}（应为 维护中）");

                // ⑥ 复飞不得二次结算机况
                float condBefore = ac.condition;
                int released = FleetManager.ReleaseCompletedOverhauls(gs, t0.AddDays(days));
                Check("⑥复飞不重算机况", Mathf.Abs(ac.condition - condBefore) <= 0.0001f,
                    $"出厂前后机况 {condBefore:F1} → {ac.condition:F1}（大修的 +25 只在排程时结算一次）");

                // ⑤ 到期复飞：状态、窗口、日志
                Check("⑤到期复飞",
                    released == 1 && ac.status == AircraftStatuses.Airworthy && ac.maintenanceUntilUtcTicks == 0,
                    $"复飞 {released} 架，状态 {ac.status}，窗口 {ac.maintenanceUntilUtcTicks}；"
                    + $"日志末条「{(gs.decisionLog.Count > 0 ? gs.decisionLog[gs.decisionLog.Count - 1] : "（空）")}」");

                // ⑦ 维护中重复排程被拒且不扣钱
                float cashBefore = gs.cash;
                FleetManager.ScheduleOverhaul(gs, ac, kind, out _);          // 排一次
                float cashAfterFirst = gs.cash;
                bool again = FleetManager.ScheduleOverhaul(gs, ac, kind, out string againMsg);
                Check("⑦维护中不重复排程",
                    !again && Mathf.Abs(gs.cash - cashAfterFirst) <= 0.0001f
                    && Mathf.Abs(cashAfterFirst - cashBefore + expectCash) <= 0.0001f,
                    again ? "第二次排程被放行（应拒绝）" : $"拒绝且未二次扣款：「{againMsg}」");

                // ⑧ 退役机不得排大修
                ac.status = AircraftStatuses.Retired;
                ac.maintenanceUntilUtcTicks = 0;
                bool retiredOk = FleetManager.ScheduleOverhaul(gs, ac, kind, out string retiredMsg);
                Check("⑧退役机不排大修", !retiredOk, $"被拒绝：「{retiredMsg}」");

                // ⑨ groundedCount 是派生量
                gs.fleet.Clear();
                gs.fleet.Add(new Aircraft { registration = "B-G1", type = "B737-800", isActive = true, status = AircraftStatuses.Maintenance });
                gs.fleet.Add(new Aircraft { registration = "B-G2", type = "B737-800", isActive = false, status = AircraftStatuses.Airworthy });
                gs.fleet.Add(new Aircraft { registration = "B-G3", type = "B737-800", isActive = true, status = AircraftStatuses.Airworthy });
                gs.fleet.Add(new Aircraft { registration = "B-G4", type = "B737-800", isActive = false, status = AircraftStatuses.Retired });
                var mnt = MaintenanceManager.EnsureState(gs);
                Check("⑨停飞架数是派生量",
                    MaintenanceManager.GroundedCount(gs) == 2 && mnt.groundedCount == 2,
                    $"维修中 1 + 不可用 1 = 2，退役不算；EnsureState 同步后字段 {mnt.groundedCount}"
                    + $"（旧实现恒为 0 —— 该字段全仓无赋值点）");

                // ⑩⑪⑫ 机龄 > 15 的延寿规则（2026-10-27 用户裁定「让大修能延寿」）
                // 旧实现是 `age > 15 → 维护中`，且没有任何恢复路径（LiveClockSimulator 只处理
                // InFlight → 适航），于是 16 年的飞机做完大修也救不回来。现改为按机况放行。
                int limAge = FleetEconomics.LifeExtensionAgeLimit;
                float minCond = FleetEconomics.LifeExtensionMinCondition;
                var aged = new Aircraft
                {
                    registration = "B-AGED", type = "B737-800", isActive = true,
                    status = AircraftStatuses.Airworthy, age = limAge + 1, condition = minCond - 10f,
                };
                gs.fleet.Add(aged);
                FleetManager.RefreshStatus(aged, t0);
                Check("⑩超龄且机况不足要停场",
                    aged.status == AircraftStatuses.Maintenance && aged.maintenanceUntilUtcTicks == 0,
                    $"机龄 {aged.age} 年 / 机况 {aged.condition:F0}% < 门槛 {minCond:F0}% → {aged.status}"
                    + $"（窗口 {aged.maintenanceUntilUtcTicks}，即没有「到期复飞」的假承诺）");

                // 大修把机况抬过门槛后必须放行 —— 旧实现这一条必红
                aged.condition = minCond + 5f;
                aged.status = AircraftStatuses.Maintenance;   // 前置：先处在停场
                FleetManager.RefreshStatus(aged, t0);
                Check("⑪大修延寿后继续适航",
                    aged.status == AircraftStatuses.Airworthy
                    && FleetEconomics.CanExtendLife(aged.age, aged.condition),
                    $"机况抬到 {aged.condition:F0}% ≥ 门槛 {minCond:F0}% → {aged.status}"
                    + $"（旧实现恒为维护中，大修对 16 年的飞机毫无作用）");

                var young = new Aircraft
                {
                    registration = "B-YOUNG", type = "B737-800", isActive = true,
                    status = AircraftStatuses.Airworthy, age = limAge, condition = minCond - 20f,
                };
                gs.fleet.Add(young);
                FleetManager.RefreshStatus(young, t0);
                Check("⑫边界：未超龄不受机况限制",
                    young.status == AircraftStatuses.Airworthy,
                    $"机龄 {young.age} 年（= 上限 {limAge}）/ 机况 {young.condition:F0}% → {young.status}"
                    + "（判定必须是 > 上限，等于上限仍放行）");

                // ⑬ 年结口径必须把 −2 点磨损算进去，否则卡在门槛上下的飞机会被 UI 预告成「达标」
                bool at51 = FleetEconomics.WillStayAirworthyAtYearEnd(limAge + 1, minCond + 1f);
                bool at53 = FleetEconomics.WillStayAirworthyAtYearEnd(limAge + 1, minCond + 3f);
                Check("⑬年结口径含磨损",
                    !at51 && at53,
                    $"机况 {minCond + 1:F0}%（年结后 {minCond + 1 - FleetEconomics.AnnualConditionDecay:F0}%）→ 预告{(at51 ? "达标（错）" : "停场")}；"
                    + $"机况 {minCond + 3:F0}%（年结后 {minCond + 3 - FleetEconomics.AnnualConditionDecay:F0}%）→ 预告{(at53 ? "达标" : "停场（错）")}");

                // ⑭ 列表徽标判据（与详情页同源；机队列表长什么样见 FleetUI.CreateRow）
                aged.condition = minCond - 10f;             // 16 年 / 40% → 年结必停场
                bool badgeOn = FleetManager.NeedsLifeExtensionWarning(aged);
                int riskCount = FleetManager.CountLifeExtensionRisk(gs);
                aged.condition = minCond + 5f;              // 抬到 55% → 可延寿
                bool badgeOff = !FleetManager.NeedsLifeExtensionWarning(aged);
                aged.condition = minCond - 10f;
                aged.maintenanceUntilUtcTicks = t0.AddDays(3).Ticks;   // 已在停场窗口内
                bool badgeSuppressed = !FleetManager.NeedsLifeExtensionWarning(aged);
                aged.maintenanceUntilUtcTicks = 0;
                var oldRetired = new Aircraft { registration = "B-OLD-R", type = "B737-800", age = 30, condition = 5f, status = AircraftStatuses.Retired };
                var youngFine = new Aircraft { registration = "B-YF", type = "B737-800", age = 8, condition = 10f };
                bool retiredQuiet = !FleetManager.NeedsLifeExtensionWarning(oldRetired);
                bool youngQuiet = !FleetManager.NeedsLifeExtensionWarning(youngFine);
                Check("⑭列表徽标判据",
                    badgeOn && badgeOff && badgeSuppressed && retiredQuiet && youngQuiet && riskCount >= 1,
                    $"16年/40%→{(badgeOn ? "挂徽标" : "无（错）")}；抬到55%→{(badgeOff ? "撤徽标" : "仍挂（错）")}；"
                    + $"停场窗口内→{(badgeSuppressed ? "不叠提示" : "仍提示（错）")}；"
                    + $"退役30年→{(retiredQuiet ? "不提示" : "仍提示（错）")}；"
                    + $"8年/10%→{(youngQuiet ? "不提示" : "仍提示（错）")}；全队预警 {riskCount} 架");

                Debug.Log($"[大修停场链路] 扣款/窗口/复飞/延寿{sb}");
                if (!ok) Debug.LogError("[大修停场链路] 存在未通过断言，见上");
                return ok;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
