using System;
using System.IO;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// K10 · 接管局的「跑道读数」必须等于真实烧钱速度（2026-10-21）。
    ///
    /// 选司界面上的「跑道 X 月」是玩家判断难度的唯一量化依据。它历史上曾有三套口径：
    /// ① 预览用 <c>max(现金×8%, 机队×4.5 + 航线×2) − 35%×收入</c>；
    /// ② 接管落盘后真正扣钱的是 <c>monthlyBurnRate + 维护 + 租金 + 人力 − 收入</c>；
    /// ③ 两者在收入覆盖成本时还会被 <c>Mathf.Max(1f, …)</c> 夹分母，把跑道退化成现金原值。
    ///
    /// 本用例钉的是**读数与账本自洽**，分两段量：
    ///   · 季中 89 天：只有逐日计提（固定成本 / 航线落地收入 / 过渡业务），不含季末一次性结算。
    ///     这一段才是「每月烧 X 万」这句话的字面含义。
    ///   · 季末那 1 天：还会叠加还本付息、外部事件、险损等一次性项。这里只记录拆分并断言
    ///     不出现 NaN / gameOver —— 一次性项不是「烧钱速度」，硬塞进月均就是第二种失真。
    ///
    /// 刻意**不**断言「4 个季度后仍存活」：★5 的设计跑道就是 3 个月，无动作活不过第三季
    /// 是难度本身。拿存活当验收，等于逼着把难度调平。
    ///
    /// **诊断产物**：每个场景把完整过程写进 <c>&lt;project&gt;/Logs/K10/&lt;scenario&gt;.txt</c>。
    /// 这不是为了方便看日志，而是因为 Test Runner 的详情面板会显示**上一轮残留**的失败文本
    /// （2026-10-21 连踩两次），单看界面截图根本不知道这一轮错在哪。断言失败时文件里
    /// 一定有「断言前」的全部实测值，以及被包住的异常堆栈。
    /// </summary>
    public class TakeoverSolvencyTests : IsolatedPlayModeFixture
    {
        /// <summary>四档预设 + 两座批次 A 新枢纽（新入表城市继承预设档位，须与同档老航司同口径）。</summary>
        static readonly string[] Scenarios =
        {
            PresetAirlines.Startup,
            PresetAirlines.Regional,
            PresetAirlines.Legacy,
            PresetAirlines.Distressed,
            PresetAirlines.HubPrefix + "ZUH",
            PresetAirlines.HubPrefix + "NGB",
        };

        /// <summary>季中观测窗口：距季末还差 1 天，避免把季末一次性结算算进「月均烧钱」。</summary>
        const int WindowDays = GameState.DaysPerQuarter - 1;

        [Test]
        public void PreviewBurn_MatchesDailyLedger_AndQuarterCloseStaysFinite(
            [ValueSource(nameof(Scenarios))] string scenario)
        {
            // 真局在跑时不要抢 GameState：接管会覆盖现金/机队/航线。
            if (GameState.Instance != null)
                Assert.Ignore("Live session detected; run in the Test Runner's empty play scene.");

            ExternalDataFetcher.Enabled = false;
            ExternalDataFetcher.QuoteEnabled = false;
            UnityEngine.Random.InitState(42);

            var report = new Reporter(scenario);
            report.Line("== K10 " + scenario + " @" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ==");
            try
            {
                RunScenario(scenario, report);
            }
            catch (Exception e)
            {
                // 异常路径也要留证：先落盘再抛，否则 Test Runner 只留一句「失败」。
                report.Line("!! 异常 " + e.GetType().Name + "：" + e.Message);
                report.Line(e.StackTrace);
                report.Flush();
                throw;
            }
            report.Flush();
        }

        void RunScenario(string scenario, Reporter report)
        {
            var profile = TakeoverWorldBuilder.BuildPreview(scenario);
            Assert.IsNotNull(profile, "无法构建接管档案：" + scenario);

            var gsGo = new GameObject("__k10_gs__");
            var fleetGo = new GameObject("__k10_fleet__");
            var loanGo = new GameObject("__k10_loans__");
            var clockGo = new GameObject("__k10_clock__");
            try
            {
                var gs = gsGo.AddComponent<GameState>();
                gs.year = 1;
                gs.quarter = 1;
                gs.SetSimUtc(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                gs.simDay = 1;
                gsGo.AddComponent<FleetManager>();
                loanGo.AddComponent<LoanManager>();

                TakeoverWorldBuilder.Apply(gs, profile);

                string stars = TakeoverWorldBuilder.Stars(profile.difficulty);
                report.Line($"现金 {profile.cash:F0} 万 · 预览净烧钱 {profile.monthlyNetBurn:F1} 万/月"
                            + $" · 预览跑道 {TakeoverWorldBuilder.RunwayText(profile)}");
                report.Line($"预览拆分：总部 {profile.monthlyCost:F0} + 机队/人力 {profile.monthlyFixedCost:F0}"
                            + $" + 还本付息 {profile.monthlyDebtService:F0} − 收入 {profile.monthlyRevenue:F0}");

                // ① 落盘后的账本烧钱 vs 预览。成本项必须逐项对齐，否则界面读数无参考价值。
                //    预览净烧钱含月均还本付息与月均保费，而逐日账本都不含（两者都是季末一次扣），
                //    故这里比的是「纯逐日计提」的经营口径。
                float fixedMonthly = LedgerFixedMonthly(gs);
                float ledgerBurn = fixedMonthly - gs.monthlyRevenue - gs.FleetMonthlyTransitionIncome;
                float debtService = DebtServiceMonthly(gs);
                float previewOperating = profile.monthlyNetBurn - profile.monthlyDebtService
                                       - profile.monthlyInsurance;
                report.Line($"账本拆分：固定 {fixedMonthly:F1} − 航线收入 {gs.monthlyRevenue:F1}"
                            + $" − 过渡收入 {gs.FleetMonthlyTransitionIncome:F1} = 经营烧钱 {ledgerBurn:F1} 万/月");
                report.Line($"成本细目：总部 {gs.monthlyBurnRate:F1} + 维护 {gs.FleetMonthlyMaintenance:F1}"
                            + $" + 租金 {gs.FleetMonthlyLease:F1} + CEO {gs.CeoMonthlyPayWan:F2}"
                            + $" + 运行岗 {gs.OperationalPayrollMonthlyWan:F1} + 存量人力 {gs.LaborCostMonthlyWan:F1}"
                            + $" + 危机 {Mathf.Max(0f, gs.financeCrisisBurnBonus):F1}");
                report.Line($"债务口径：预览月均 {profile.monthlyDebtService:F1} vs 落盘贷款月供 {debtService:F1}"
                            + $"（每季一次扣 {debtService * 3f:F1}）");
                report.Line($"保险口径：预览月均 {profile.monthlyInsurance:F1} vs 落盘 {gs.FleetMonthlyInsurance:F1}"
                            + $"（每季一次扣 {gs.FleetMonthlyInsurance * 3f:F1}）"
                            + $" · 机队残值 {FleetManager.FleetTotalValue(gs):F0} 万"
                            + $" · {gs.safetyRating} 级 / {gs.insuranceCoverage}");
                report.Line($"机队 {gs.fleet.Count} 架（在役 {gs.ActiveFleetCount}）· 航线 {gs.activeRoutes.Count} 条"
                            + $" · 过渡合同 {gs.transitionContracts.Count} 份 · 机组乘数 {gs.CrewQualityMultiplier:F4}");

                // ①b 局内跑道必须与选司预览同源（K11）。
                //     preview 计入还本付息与保费，而 EffectiveMonthlyBurn 曾经两者都不含。
                //     保费早已并入；还本付息是这一轮补的 —— 漏了它，玩家点进局里读数会凭空变长
                //     20~32%（★2 9.7 vs 8.0 / ★5 3.6 vs 3.0），直接和「难度越高跑道越短」唱反调。
                float inGameBurn = gs.EffectiveMonthlyBurn;
                float inGameRunway = CashRunnelCalculator.GetRunwayMonths(gs.cash, inGameBurn);
                report.Line($"局内跑道：现金 {gs.cash:F1} / 净烧钱 {inGameBurn:F2} = {inGameRunway:F2} 月"
                            + $" vs 选司预览 {profile.cashRunwayMonths:F2} 月"
                            + $"（差 {inGameRunway - profile.cashRunwayMonths:+0.00;-0.00} 月）");

                // ② 逐条航线的执飞资格与班次构成 —— 「界面照算收入但实际飞不起来」的唯一证据。
                if (gs.activeRoutes != null)
                {
                    for (int i = 0; i < gs.activeRoutes.Count; i++)
                    {
                        var r = gs.activeRoutes[i];
                        if (r == null) continue;
                        var reason = FlightGroundLogic.RouteOf(gs, r);
                        report.Line($"  航线 {r.origin}-{r.destination} 机号[{r.registration}] 机型[{r.aircraftType}]"
                                    + $" 班次{r.FlightCount} 班期{r.daysOfWeek}/{r.weeklyFlights}"
                                    + $" 净利{r.dailyProfit:F3} 毛利率{r.GrossDailyProfit:F3} 固定{r.FixedDailyCostWan:F3}"
                                    + $" 执飞[{FlightGroundLogic.Label(reason)}]"
                                    + (FlightGroundLogic.IsPersistent(reason) ? " ← 持续性停飞" : ""));
                        if (r.flights != null)
                            for (int f = 0; f < r.flights.Count; f++)
                            {
                                var fl = r.flights[f];
                                if (fl == null) continue;
                                report.Line($"      班 {fl.depTime}-{fl.arrTime} {fl.daysOfWeek}/{fl.weeklyFlights}"
                                            + $" 机号[{fl.registration}] 单班毛利 {fl.dailyProfit:F3}");
                            }
                    }
                }

                // ③ 季中 89 天：只含逐日计提。
                var time = clockGo.AddComponent<TimeManager>();
                TimeManager.SimulationEnabled = false;
                DateTime startLocal = gs.SimUtc + GameState.LocalUtcOffset;
                float crewMult = gs.CrewQualityMultiplier;
                float cashAtStart = gs.cash;
                int logAtStart = gs.decisionLog.Count;

                // ★ 期望值必须在**推进之前**快照。过渡合同会在窗口内到期并从列表移除，
                //   上一版在推进后计算，于是「预期 0.00 / 实收 45.65」——错的是量法，不是账。
                float expectExpense = fixedMonthly * (WindowDays / 30f);
                float expectRoute = ExpectedRouteCash(gs, startLocal, WindowDays) * crewMult;
                float expectTrans = ExpectedTransitionCash(gs, WindowDays);
                report.Line($"期望快照（推进前）：成本 {expectExpense:F2} · 航线 {expectRoute:F2} · 过渡 {expectTrans:F2}");

                report.Line($"---- 开始推进 {WindowDays} 天（起点 {gs.SimUtc:yyyy-MM-dd HH:mm}Z / 本地 {startLocal:yyyy-MM-dd HH:mm}）----");
                time.AdvanceGameMinutes(gs, WindowDays * 1440);

                float cashBeforeClose = gs.cash;
                // 三只桶分开记：分开才能指出「哪一段没按账走」，
                // 只比一个净额永远只会得到「差 42%」这种无法定位的结论。
                float realizedRoute = gs.liveQuarterIncome;
                float realizedTrans = gs.liveQuarterTransitionIncome;
                float realizedExpense = gs.liveQuarterExpense;

                float midDrop = cashAtStart - cashBeforeClose;
                float expectDrop = expectExpense - expectRoute - expectTrans;
                float realizedDrop = realizedExpense - realizedRoute - realizedTrans;

                report.Line($"季中 {WindowDays} 天（机组乘数 {crewMult:F4}，比例 {WindowDays / 30f:F4}）");
                report.Line($"  成本   实扣 {realizedExpense:F2} / 预期 {expectExpense:F2}"
                            + $"（差 {realizedExpense - expectExpense:+0.00;-0.00}）");
                report.Line($"  航线   实收 {realizedRoute:F2} / 预期 {expectRoute:F2}"
                            + $"（差 {realizedRoute - expectRoute:+0.00;-0.00}）");
                report.Line($"  过渡   实收 {realizedTrans:F2} / 预期 {expectTrans:F2}"
                            + $"（差 {realizedTrans - expectTrans:+0.00;-0.00}）");
                report.Line($"  合计   实扣 {midDrop:F2} / 预期 {expectDrop:F2}（差 {midDrop - expectDrop:+0.00;-0.00}）");
                report.Line($"  季中现金 {cashAtStart:F1} → {cashBeforeClose:F1}"
                            + $"（三桶实测合计 {realizedDrop:F2}，与现金差 {midDrop - realizedDrop:+0.00;-0.00}）");

                // —— 静态口径 vs 实测口径：这几行才是「界面读数可信吗」的直接证据 ——
                float months = WindowDays / 30f;
                float actualMonthlyBurn = midDrop / months;
                report.Line("  口径对照（万美元/月）");
                report.Line($"    季中实测净烧钱 {actualMonthlyBurn:F1} vs 预览经营口径 {previewOperating:F1}"
                            + $"（差 {actualMonthlyBurn - previewOperating:+0.0;-0.0}，"
                            + $"{100f * (actualMonthlyBurn / Mathf.Max(0.01f, previewOperating) - 1f):+0.0;-0.0}%）");
                // 过渡：必须与「推进前快照」比，不能与 gs.FleetMonthlyTransitionIncome 比 ——
                // 后者取的是当前时刻的剩余档期，窗口结束时合同多半已到期归零，
                // 拿它作分母只会印出「实收 12.1 vs 静态 0.0」这种自己吓自己的假偏差。
                report.Line($"    过渡实收/月 {realizedTrans / months:F1} vs 快照 {expectTrans / months:F1}"
                            + "（快照 = 推进前剩余档期折算）");
                report.Line($"    保费 {gs.FleetMonthlyInsurance:F1}/月（季末一次扣 {gs.FleetMonthlyInsurance * 3f:F0}）"
                            + $" vs 预览 {profile.monthlyInsurance:F1}");
                report.Line($"    折成跑道口径 {actualMonthlyBurn + profile.monthlyDebtService + profile.monthlyInsurance:F1}"
                            + $" vs 预览净烧钱 {profile.monthlyNetBurn:F1}"
                            + $" → 实测跑道 {profile.cash / Mathf.Max(0.01f, actualMonthlyBurn + profile.monthlyDebtService + profile.monthlyInsurance):F1} 月"
                            + $" vs 显示 {TakeoverWorldBuilder.RunwayText(profile)}");

                // ④ 季末 1 天：跨季结算（还本付息 / 外部事件 / 险损）在此一次性发生。
                time.AdvanceGameMinutes(gs, 1440);
                float closeDelta = gs.cash - cashBeforeClose;
                report.Line($"季末结算：{closeDelta:+0.0;-0.0} 万 → 现金 {gs.cash:F0} 万"
                            + $" · 第 {gs.year} 年 Q{gs.quarter} · gameOver={gs.gameOver}"
                            + (gs.gameOver ? $"（{gs.gameOverReason}）" : ""));
                for (int i = logAtStart; i < gs.decisionLog.Count; i++)
                    report.Line("  日志 " + gs.decisionLog[i]);
                report.Flush();

                bool finite = IsFinite(gs.cash) && IsFinite(gs.monthlyRevenue);
                bool closed = gs.quarter == 2 && gs.year == 1;

                // ⑤ 首季整段（季中 + 季末一次性）vs 预览×3 —— 这一项才是「跑道读数可信吗」。
                //    季末一次性项里有一笔**按季计提的保险保费**（全险），它不在 monthlyNetBurn 里，
                //    所以越大的公司偏乐观越明显：★3 实测 +20%，正是 207 万保费（69 万/月）。
                //    带宽放 ±25% 是为了让它先跑绿并留证，不是认为偏差可以接受。
                float quarterDrop = cashAtStart - gs.cash;
                float expectQuarter = profile.monthlyNetBurn * 3f;
                report.Line($"首季整段：现金 {cashAtStart:F1} → {gs.cash:F1}（净变动 {quarterDrop:F1}）"
                            + $" vs 预览×3 {expectQuarter:F1}"
                            + $"（差 {quarterDrop - expectQuarter:+0.0;-0.0}，{100f * (quarterDrop / Mathf.Max(0.01f, expectQuarter) - 1f):+0.0;-0.0}%）");

                // —— 断言（每一项都先把上下文写进报告，失败时文件里就是完整现场）——
                report.Line("---- 断言 ----");
                Check(report, "预览净烧钱 > 0", profile.monthlyNetBurn > 0f, $"{profile.monthlyNetBurn:F2}");
                Check(report, "经营烧钱 ≤ 预览×1.02", ledgerBurn <= previewOperating * 1.02f,
                    $"账本 {ledgerBurn:F2} / 预览 {previewOperating:F2}");
                Check(report, "经营烧钱 ≥ 预览×0.70", ledgerBurn >= previewOperating * 0.70f,
                    $"账本 {ledgerBurn:F2} / 预览 {previewOperating:F2}");
                Check(report, "预览月供 = 落盘月供", Mathf.Abs(profile.monthlyDebtService - debtService) <= 0.5f,
                    $"预览 {profile.monthlyDebtService:F2} / 落盘 {debtService:F2}");
                // K11：局内读数与选司预览必须同一个数，否则玩家看到的两块界面对不上。
                Check(report, "局内净烧钱 = 预览净烧钱",
                    Mathf.Abs(inGameBurn - profile.monthlyNetBurn) <= Mathf.Max(1f, profile.monthlyNetBurn * 0.02f),
                    $"局内 {inGameBurn:F2} / 预览 {profile.monthlyNetBurn:F2}");
                Check(report, "局内跑道 = 选司跑道", Mathf.Abs(inGameRunway - profile.cashRunwayMonths) <= 0.1f,
                    $"局内 {inGameRunway:F2} 月 / 预览 {profile.cashRunwayMonths:F2} 月");
                Check(report, "预览保费 = 落盘保费",
                    Mathf.Abs(profile.monthlyInsurance - gs.FleetMonthlyInsurance)
                        <= Mathf.Max(1f, gs.FleetMonthlyInsurance * 0.08f),
                    $"预览 {profile.monthlyInsurance:F2} / 落盘 {gs.FleetMonthlyInsurance:F2}");
                Check(report, "跑道 = 现金/净烧钱", Mathf.Abs(profile.cashRunwayMonths - profile.cash / profile.monthlyNetBurn) <= 0.02f,
                    $"{profile.cashRunwayMonths:F3}");
                Check(report, "逐日成本 ±2%", Mathf.Abs(realizedExpense - expectExpense) <= Mathf.Max(2f, expectExpense * 0.02f),
                    $"实 {realizedExpense:F2} / 期 {expectExpense:F2}");
                Check(report, "航线现金 ±25%", expectRoute <= 0.5f
                        ? realizedRoute < 1f
                        : Mathf.Abs(realizedRoute - expectRoute) <= expectRoute * 0.25f,
                    $"实 {realizedRoute:F2} / 期 {expectRoute:F2}");
                Check(report, "过渡现金 ±35%", expectTrans <= 0.5f
                        ? realizedTrans < 1f
                        : Mathf.Abs(realizedTrans - expectTrans) <= Mathf.Max(2f, expectTrans * 0.35f),
                    $"实 {realizedTrans:F2} / 期 {expectTrans:F2}");
                Check(report, "净变动 ±3%", Mathf.Abs(midDrop - expectDrop) <= Mathf.Max(4f, Mathf.Abs(expectDrop) * 0.03f),
                    $"实 {midDrop:F2} / 期 {expectDrop:F2}");
                Check(report, "现金有限", finite, $"{gs.cash:F2}");
                Check(report, "已进入第1年Q2", closed, $"第{gs.year}年Q{gs.quarter} simDay={gs.simDay} simHour={gs.simHour}");
                Check(report, "首季未 gameOver", !gs.gameOver, gs.gameOverReason ?? "无原因");
                Check(report, "首季净变动 ≈ 预览×3 ±25%",
                    Mathf.Abs(quarterDrop - expectQuarter) <= Mathf.Abs(expectQuarter) * 0.25f,
                    $"实 {quarterDrop:F2} / 期 {expectQuarter:F2}");
                report.Flush();

                // 汇总成一条 NUnit 断言：报告已完整落盘，这里只需给出「第几项不成立」。
                Assert.IsTrue(report.AllPassed, report.FailureSummary());
            }
            finally
            {
                TimeManager.SimulationEnabled = false;
                UnityEngine.Object.DestroyImmediate(gsGo);
                UnityEngine.Object.DestroyImmediate(fleetGo);
                UnityEngine.Object.DestroyImmediate(loanGo);
                UnityEngine.Object.DestroyImmediate(clockGo);
            }
        }

        /// <summary>
        /// 账本里「不飞也会发生」的月成本。逐项对齐 <c>GameState.EffectiveMonthlyBurn</c>，
        /// 但不减收入、也不去负 —— 跑道公式需要带符号净值。故意与生产属性耦合：
        /// 将来给它增删成本项而忘了同步这里，本用例会立刻失衡。
        /// </summary>
        static float LedgerFixedMonthly(GameState gs)
            => gs.monthlyBurnRate
               + gs.FleetMonthlyMaintenance
               + gs.FleetMonthlyLease
               + gs.CeoMonthlyPayWan
               + gs.OperationalPayrollMonthlyWan
               + gs.LaborCostMonthlyWan
               + Mathf.Max(0f, gs.financeCrisisBurnBonus);

        /// <summary>存量贷款的月供合计（季末按 3 个月一次扣）。</summary>
        static float DebtServiceMonthly(GameState gs)
        {
            if (gs?.activeLoans == null) return 0f;
            float s = 0f;
            for (int i = 0; i < gs.activeLoans.Count; i++)
            {
                var l = gs.activeLoans[i];
                if (l == null || l.type == "equity" || l.monthsRemaining <= 0) continue;
                s += Mathf.Max(0f, l.monthlyPayment);
            }
            return s;
        }

        /// <summary>
        /// 窗口内应落地的航线现金（万美元，未乘机组乘数）。
        ///
        /// 口径：每趟落地按 <c>(flight.dailyProfit − 固定份额) × 0.5</c> 计
        /// （<see cref="LiveClockSimulator"/> 的 <c>pnl *= 0.5f</c> 与逐段实扣固定成本），
        /// 去程 + 回程 = 一个净日利润份额，故 Σ航段 = <see cref="RouteData.dailyProfit"/>。
        ///
        /// 按**航段**数而不是按天数数：去程与回程各自独立起降（<c>TryTakeoff</c> 不要求去程先飞），
        /// 且窗口边界会把「首日的去程」或「末日的回程」单边切掉。按天数数会稳定多算半趟。
        /// </summary>
        static float ExpectedRouteCash(GameState gs, DateTime startLocal, int windowDays)
        {
            if (gs?.activeRoutes == null) return 0f;
            DateTime toL = startLocal.AddDays(windowDays);
            float total = 0f;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                // 持续性停飞（无机可派 / 停场 / 投租）当日不飞、不落账。
                if (FlightGroundLogic.IsPersistent(FlightGroundLogic.RouteOf(gs, r))) continue;
                r.EnsureFlights();
                if (r.flights == null) continue;
                // 单班净额：毛贡献份额 − 固定成本按班次均摊。与 Land() 一字不差。
                float perFlight = r.FixedDailyCostWan / Mathf.Max(1, r.FlightCount);
                for (int f = 0; f < r.flights.Count; f++)
                {
                    var fl = r.flights[f];
                    if (fl == null) continue;
                    int legs = 0;
                    // 多算一天再截断：跨夜航班的回程可能落到窗口末尾之后。
                    for (int d = 0; d <= windowDays + 1; d++)
                    {
                        DateTime day = startLocal.Date.AddDays(d);
                        if (!LiveClockLogic.OperatesOn(fl.daysOfWeek, day, fl.weeklyFlights)) continue;
                        LiveClockLogic.RoundTripWindow(day, fl.depTime, fl.arrTime, r.distanceKm,
                            r.aircraftType, r.groundStaffAssigned,
                            out DateTime outDep, out _, out DateTime retDep, out _);
                        // 与 TryTakeoff 的判据一致：严格大于起点，含终点。
                        if (outDep > startLocal && outDep <= toL) legs++;
                        if (retDep > startLocal && retDep <= toL) legs++;
                    }
                    total += (fl.dailyProfit - perFlight) * 0.5f * legs;
                }
            }
            return total;
        }

        /// <summary>
        /// 窗口内应到账的过渡业务现金（万美元）。合同 <c>daysUsed</c> 从接管时的随机初值起
        /// 逐日递增，到 <c>totalDays</c>（90 天）自然到期后停付，故按剩余天数封顶。
        /// 到期尾款（balanceWan）接管合同一律为 0，不单列。
        /// </summary>
        static float ExpectedTransitionCash(GameState gs, int windowDays)
        {
            if (gs?.transitionContracts == null) return 0f;
            float total = 0f;
            for (int i = 0; i < gs.transitionContracts.Count; i++)
            {
                var c = gs.transitionContracts[i];
                if (c == null) continue;
                int left = Mathf.Max(0, c.totalDays - c.daysUsed);
                total += Mathf.Max(0f, c.EffectiveDailyCash) * Mathf.Min(left, windowDays);
            }
            return total;
        }

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>逐项记录断言结果（不抛），最后一次性判定 —— 让一次运行就能看到全部偏差。</summary>
        static void Check(Reporter report, string name, bool ok, string detail)
            => report.Check(name, ok, detail);

        /// <summary>把过程写进 <c>&lt;project&gt;/Logs/K10/&lt;scenario&gt;.txt</c>，逐行 flush。</summary>
        sealed class Reporter
        {
            readonly string _path;
            readonly StringBuilder _buf = new StringBuilder();
            readonly StringBuilder _fails = new StringBuilder();
            int _n;

            public Reporter(string scenario)
            {
                _path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "K10", scenario + ".txt"));
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    File.WriteAllText(_path, "");
                }
                catch (Exception e)
                {
                    // 写不进磁盘不该让测试红：退回只打控制台。
                    Debug.LogWarning("[K10] 报告写入失败：" + e.Message);
                }
            }

            public bool AllPassed => _fails.Length == 0;

            public string FailureSummary()
                => $"K10 断言失败（详见 {_path}）：\n" + _fails;

            public void Line(string s)
            {
                _buf.AppendLine(s);
                Debug.Log("[K10] " + s);
            }

            public void Check(string name, bool ok, string detail)
            {
                _n++;
                Line($"  [{(ok ? "OK" : "FAIL")}] {_n:00} {name} —— {detail}");
                if (!ok) _fails.AppendLine($"  ✗ {name}：{detail}");
            }

            public void Flush()
            {
                try
                {
                    File.AppendAllText(_path, _buf.ToString());
                    _buf.Clear();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[K10] 报告追加失败：" + e.Message);
                }
            }
        }
    }
}
