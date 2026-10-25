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
    /// T04.3「8 场景现金对账」的隔离回归（2026-10-24）。
    ///
    /// 为什么要有这个文件：T04.3 的八个场景里，**退租**此前在 <c>Assets/Tests/**</c> 零命中 ——
    /// 唯一的覆盖是 <see cref="LogicSelfTests"/> 的 R22，而那是 <c>[MenuItem]</c>，
    /// 全文 0 个 <c>Assert.*</c>，所有「期望 True」都只是 Console 散文。
    /// 等于「静默失败不会报警」，比没写更危险。
    ///
    /// 这里把 R22 的三组数搬成**真断言**，并顺带钉住 T04.3 的两条口径：
    ///
    /// · **S5 退租**（此前零断言）
    ///   ① 自有/二手退役 → 现金 +残值（产权在本公司，能卖）；
    ///   ② 经营租赁退租 → 现金 **Δ=0**（产权在出租人，无残值可回收）；旧口径在此白送整机目录价，是造钱口子；
    ///   ③ 融资租赁提前退租 → 现金 **−剩余租期租金 × 30%**（含买断条款，提前退属违约）；
    ///   ④ 期满退租 → 违约金 0（10 年租期走完就是普通退租）；
    ///   ⑤ 护栏：重复处置不得二次入账；现金不够付违约金时宁可退不成；
    ///   ⑥ 退出后月租与月维护**同步停计**，在册数不再含它（记录仍留档备查）。
    ///
    /// · **S4 维护定检**（此前只验了 <c>checks.Count</c>，没验钱）
    ///   排定检的扣款必须**逐分等于** <see cref="MaintenanceEconomics.CheckCostWan"/>，
    ///   且**不进损益**（现金减、<c>yearNetProfit</c> 不动）—— 这正是 T04.3 要的「现金半边 vs 应计半边」。
    ///
    /// 判定规则（同 T04.3 审计 §7.1）：只认「现金增量 / 应计桶 vs 期望值」，
    /// 不认「状态变了」和「Console 打印了期望值」。
    /// </summary>
    public static class AccountingScenarioRegression
    {
        public static bool Run()
        {
            bool s5 = RunLeaseReturn();
            bool s4 = RunMaintenanceCheck();
            bool ok = s5 && s4;
            Debug.Log($"T04.3 账务场景 → 退租对账={s5}　定检对账={s4}　总计={ok}");
            return ok;
        }

        /// <summary>S5 · 退租 / 退役的现金对账。</summary>
        static bool RunLeaseReturn()
        {
            var go = new GameObject("__acct_s5_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                var fm = go.AddComponent<FleetManager>();
                gs.fleet = new List<Aircraft>();
                gs.year = 1;
                gs.quarter = 1;
                gs.cash = 5000f;

                bool all = true;

                // ① 自有：退役回收残值（这条旧口径本来就对，不能被租赁口径的修改带偏）
                var owned = new Aircraft
                {
                    registration = "B-9001", type = "A320neo", source = FleetEconomics.SourceOwned,
                    status = AircraftStatuses.Airworthy, purchaseYear = 1, purchasePrice = 1000f, age = 5
                };
                gs.fleet.Add(owned);
                float residual = FleetEconomics.ResidualValue(1000f, 5);
                float cash0 = gs.cash;
                bool okOwned = fm.TryRetireAircraft(gs, "B-9001", out string msgOwned);
                float ownedDelta = gs.cash - cash0;
                all &= okOwned
                       && Mathf.Abs(ownedDelta - residual) < 0.01f
                       && owned.IsDisposed
                       && owned.disposal == FleetEconomics.DisposalRetired
                       && owned.ExitLabel == FleetEconomics.DisposalRetired
                       && FleetManager.InServiceCount(gs) == 0
                       && gs.fleet.Count == 1                       // 留档仍可查
                       && gs.FleetMonthlyLease <= 0.001f
                       && fm.CalcMonthlyMaintenance(gs) <= 0.001f    // 同 ② 的理由：不用依赖单例的属性
                       && msgOwned.Contains("残值");
                if (!all) Debug.LogError($"S5-① 自有退役不符：ok={okOwned} Δ={ownedDelta:F2} 期望 {residual:F2} msg={msgOwned}");

                // ② 经营租赁：退租 = 交还出租人 → 现金 Δ=0
                //    旧口径在这一点上白送整机目录价（租进当天「退役」即可套现），故这里同时断言「本来会白送多少」> 0
                var op = new Aircraft
                {
                    registration = "B-9002", type = "A320neo", source = FleetEconomics.SourceOperatingLease,
                    status = AircraftStatuses.Airworthy, purchaseYear = 1, purchasePrice = 1090f,
                    monthlyLeaseWan = 24f, age = 0
                };
                gs.fleet.Add(op);
                float leaseBefore = gs.FleetMonthlyLease;
                // 不走 gs.FleetMonthlyMaintenance：那个属性依赖 FleetManager.Instance 单例，
                // 而隔离用例里上一个 GameObject 已被 DestroyImmediate（Unity 的「假 null」），
                // 属性会静默返回 0，于是「月维护变少」退化成 0 < 0 —— 断言恒失败且原因看不出。
                float maintBefore = fm.CalcMonthlyMaintenance(gs);
                float opMaint = FleetManager.MonthlyMaintOf(op);
                float opCash0 = gs.cash;
                bool okOp = fm.TryRetireAircraft(gs, "B-9002", out string msgOp);
                float opDelta = gs.cash - opCash0;
                float maintAfter = fm.CalcMonthlyMaintenance(gs);
                bool opOk = okOp
                            && Mathf.Abs(opDelta) < 0.01f
                            && op.IsDisposed
                            && op.disposal == FleetEconomics.DisposalReturned
                            && op.ExitLabel == FleetEconomics.DisposalReturned     // 叫「退租」，不叫「退役」
                            && FleetManager.CalcResidualValue(op) > 1f             // 旧口径下这笔钱会被白付
                            && Mathf.Abs(gs.FleetMonthlyLease - (leaseBefore - 24f)) < 0.001f
                            && opMaint > 0.01f                                     // 前提：这架原本确实在计维护
                            && Mathf.Abs(maintAfter - (maintBefore - opMaint)) < 0.001f  // 退出即停计，且只停它那一份
                            && msgOp.Contains("交还出租人");
                all &= opOk;
                if (!opOk)
                    Debug.LogError($"S5-② 经营租赁不符：ok={okOp} Δ={opDelta:F2} 期望 0"
                        + $" 月租 {leaseBefore:F1}→{gs.FleetMonthlyLease:F1}"
                        + $" 月维护 {maintBefore:F2}→{maintAfter:F2}（该机 {opMaint:F2}）"
                        + $" 处置={op.disposal}/{op.ExitLabel} 残值={FleetManager.CalcResidualValue(op):F0} msg={msgOp}");

                // ③ 融资租赁：提前退租 = 剩余租期租金 × 30%
                var fin = new Aircraft
                {
                    registration = "B-9003", type = "B737-800", source = FleetEconomics.SourceFinanceLease,
                    status = AircraftStatuses.Airworthy, purchaseYear = 1, purchasePrice = 1000f,
                    monthlyLeaseWan = 12f, age = 0
                };
                gs.fleet.Add(fin);
                float feeFin = FleetManager.LeaseReturnCost(gs, fin);
                float feeExpect = FleetEconomics.LeaseReturnCostWan(FleetEconomics.SourceFinanceLease, 12f, 0);
                float finCash0 = gs.cash;
                bool okFin = fm.TryRetireAircraft(gs, "B-9003", out string msgFin);
                float finDelta = gs.cash - finCash0;
                bool finOk = okFin
                             && feeExpect > 0.01f
                             && Mathf.Abs(feeFin - feeExpect) < 0.01f
                             && Mathf.Abs(finDelta + feeExpect) < 0.01f
                             && fin.disposal == FleetEconomics.DisposalReturned
                             && msgFin.Contains("违约金");
                all &= finOk;
                if (!finOk)
                    Debug.LogError($"S5-③ 融资租赁不符：ok={okFin} 违约金 {feeFin:F2}（期望 {feeExpect:F2}）Δ={finDelta:F2} msg={msgFin}");

                // ④ 期满退租：10 年租期走完 → 违约金 0（不该再罚）
                var mature = new Aircraft
                {
                    registration = "B-9004", type = "B737-800", source = FleetEconomics.SourceFinanceLease,
                    status = AircraftStatuses.Airworthy, purchaseYear = 1, purchasePrice = 1000f,
                    monthlyLeaseWan = 12f, age = 9
                };
                gs.fleet.Add(mature);
                gs.year = 12;   // 已付 132 期 > 120 期
                float feeMature = FleetManager.LeaseReturnCost(gs, mature);
                float matCash0 = gs.cash;
                bool okMature = fm.TryRetireAircraft(gs, "B-9004", out string msgMature);
                float matDelta = gs.cash - matCash0;
                gs.year = 1;
                bool matureOk = okMature
                                && Mathf.Abs(feeMature) < 0.01f
                                && Mathf.Abs(matDelta) < 0.01f
                                && msgMature.Contains("无违约金");
                all &= matureOk;
                if (!matureOk)
                    Debug.LogError($"S5-④ 期满退租不符：ok={okMature} 违约金 {feeMature:F2}（期望 0）Δ={matDelta:F2} msg={msgMature}");

                // ⑤ 护栏 1：重复处置不得二次入账
                float dupCash0 = gs.cash;
                bool dup = fm.TryRetireAircraft(gs, "B-9001", out string msgDup);
                bool dupOk = !dup && Mathf.Abs(gs.cash - dupCash0) < 0.01f;
                all &= dupOk;
                if (!dupOk) Debug.LogError($"S5-⑤ 重复处置不符：dup={dup} Δ={gs.cash - dupCash0:F2} msg={msgDup}");

                // ⑥ 护栏 2：现金不够付违约金 → 退不成，飞机仍适航，现金不动
                var poor = new Aircraft
                {
                    registration = "B-9005", type = "A320neo", source = FleetEconomics.SourceFinanceLease,
                    status = AircraftStatuses.Airworthy, purchaseYear = 1, purchasePrice = 1000f,
                    monthlyLeaseWan = 12f, age = 0
                };
                gs.fleet.Add(poor);
                gs.cash = 100f;   // 违约金远超现金
                float poorCash0 = gs.cash;
                bool poorFail = fm.TryRetireAircraft(gs, "B-9005", out string msgPoor);
                bool poorOk = !poorFail
                              && Mathf.Abs(gs.cash - poorCash0) < 0.01f
                              && poor.status == AircraftStatuses.Airworthy
                              && poor.isActive
                              && msgPoor.Contains("不足");
                all &= poorOk;
                if (!poorOk)
                    Debug.LogError($"S5-⑥ 欠款退租不符：ok={poorFail} 现金 {poorCash0:F0}→{gs.cash:F0} 状态={poor.status} msg={msgPoor}");

                Debug.Log($"T04.3-S5 退租对账 → 自有+{residual:F0}={okOwned}　经营租赁Δ0={opOk}（旧口径会白送 {FleetManager.CalcResidualValue(op):F0} 万）"
                          + $"　融资租赁−{feeExpect:F0}={finOk}　期满0={matureOk}　护栏(重复/欠款)={dupOk && poorOk}　合计={all}");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>S4 · 定检扣款的「现金半边 vs 应计半边」对账。</summary>
        static bool RunMaintenanceCheck()
        {
            var go = new GameObject("__acct_s4_gs__");
            try
            {
                var gs = go.AddComponent<GameState>();
                var mnt = go.AddComponent<MaintenanceManager>();
                gs.fleet = new List<Aircraft>
                {
                    new Aircraft { registration = "B-9101", type = "A320neo", status = AircraftStatuses.Airworthy, purchaseYear = 1, age = 3 },
                    new Aircraft { registration = "B-9102", type = "A320neo", status = AircraftStatuses.Airworthy, purchaseYear = 1, age = 4 },
                };
                gs.cash = 1000f;
                gs.yearNetProfit = 0f;

                float cost = MaintenanceEconomics.CheckCostWan("C", gs.fleet.Count);
                float cash0 = gs.cash;
                float profit0 = gs.yearNetProfit;
                bool ok = mnt.ScheduleCheck(gs, "B-9101", "C", 7, out string msg);

                var state = MaintenanceManager.EnsureState(gs);
                bool cashHalf = ok
                                && cost > 0.01f
                                && Mathf.Abs((cash0 - gs.cash) - cost) < 0.01f          // 现金减的正好是报价
                                && state.checks.Count == 1
                                && Mathf.Abs(state.checks[0].costWan - cost) < 0.01f;   // 排程卡片上的数 = 实际扣的数
                bool accrualHalf = Mathf.Abs(gs.yearNetProfit - profit0) < 0.01f;      // 不进损益（T04.3 的应计半边）
                bool guard = true;
                gs.cash = 1f;                                                          // 现金不足
                float poorCash0 = gs.cash;
                bool poor = mnt.ScheduleCheck(gs, "B-9102", "C", 7, out string msgPoor);
                guard = !poor
                        && Mathf.Abs(gs.cash - poorCash0) < 0.01f
                        && state.checks.Count == 1;

                bool all = cashHalf && accrualHalf && guard;
                Debug.Log($"T04.3-S4 定检对账 → 扣款 −{cost:F2}={cashHalf}（现金 {cash0:F0}→{cash0 - cost:F0}）"
                          + $"　不进损益={accrualHalf}（yearNetProfit {profit0:F2}→{gs.yearNetProfit:F2}）"
                          + $"　现金不足不排={guard}　合计={all}"
                          + $"\n　{msg}");
                if (!all) Debug.LogError($"S4 定检对账不符：cashHalf={cashHalf} accrualHalf={accrualHalf} guard={guard}（{msgPoor}）");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
#endif
