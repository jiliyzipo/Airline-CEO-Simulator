using System;
using System.Collections;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// T04.3「8 场景现金对账」里此前**没有任何实时窗口用例**的五项：S1 停飞 / S2 无机 /
    /// S3 缺员 / S6 转入过渡业务 / S8 季度中途业务变化。
    ///
    /// 判定口径沿用 <c>docs/handoff/T04_3_账务验证矩阵_2026-10-18.md</c> §4：
    /// <code>
    /// cash_end = cash_start + Σ(应计桶)      ← 本文件对每条用例都断言这一恒等式
    /// </code>
    /// 只断言「状态变了」不算对账；只 <c>Debug.Log</c> 打印期望值更不算。
    /// 预期值一律从权威函数现算（<see cref="LiveClockLogic.DailyFixedCostWan"/>、
    /// <c>RouteData.dailyProfit</c>、<see cref="GameState.CrewQualityMultiplier"/>、
    /// <see cref="TransitionBusinessLogic.RecallFeeWan"/>），不另写一套常量。
    ///
    /// **已知偏差（不假装相等）**：`TimeManager` 里的 `deptQuarterExpense` 仍是
    /// 「季末当时费率 × 3」，不含工资/人力/过渡。所以部门预算执行 ≠ `liveQuarterExpense`，
    /// 尤其在中途增删机队时必然分叉。本文件只对账**现金与季度桶**，不对账部门预算；
    /// 那条属问责分摊口径（矩阵 §6.4 已登记），需另裁。
    /// </summary>
    public class DevelopmentAccountingScenarioTests : IsolatedPlayModeFixture
    {
        /// <summary>万元口径容差：吸收 float 累积误差，放过的是尾差不是缺陷。</summary>
        const float Tol = 0.05f;
        /// <summary>30 天/月的日摊系数与 <see cref="LiveClockLogic.DailyFixedCostWan"/> 同源。</summary>
        const float ProfitPerDay = 12f;

        static DateTime Local(int dayOffset, int hour = 0, int minute = 0)
            => new DateTime(2026, 1, 1, hour, minute, 0).AddDays(dayOffset);

        static DateTime Utc(DateTime local) => local - GameState.LocalUtcOffset;

        static GameState State(string name, float burn = 30f)
        {
            if (Application.companyName != "AirlineCEORegression") Assert.Ignore("Isolated baseline only.");
            var gs = new GameObject(name).AddComponent<GameState>();
            gs.cash = 10000f;
            gs.yearNetProfit = 0f;
            gs.liveQuarterIncome = 0f;
            gs.liveQuarterExpense = 0f;
            gs.liveQuarterTransitionIncome = 0f;
            gs.year = 1; gs.quarter = 1; gs.simDay = 1;
            gs.monthlyBurnRate = burn;
            if (gs.activeRoutes == null) gs.activeRoutes = new List<RouteData>();
            if (gs.fleet == null) gs.fleet = new List<Aircraft>();
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            gs.activeRoutes.Clear();
            gs.fleet.Clear();
            gs.inbox.Clear();
            gs.SetSimUtc(Utc(Local(0)));
            return gs;
        }

        /// <summary>日固定成本的唯一权威口。测试不得自己拼一份加法。</summary>
        static float DailyFixed(GameState gs) => LiveClockLogic.DailyFixedCostWan(
            gs.monthlyBurnRate, gs.FleetMonthlyMaintenance, gs.FleetMonthlyLease,
            gs.CeoMonthlyPayWan, gs.financeCrisisBurnBonus,
            gs.OperationalPayrollMonthlyWan, gs.LaborCostMonthlyWan);

        /// <summary>部门可管固定成本（问责口径）的唯一权威口，裁定 R-A / A2。</summary>
        static float DeptDaily(GameState gs) => LiveClockLogic.DeptDailyFixedCostWan(
            gs.monthlyBurnRate, gs.FleetMonthlyMaintenance, gs.FleetMonthlyLease, gs.CeoMonthlyPayWan);

        /// <summary>
        /// 推进 N 个游戏日：**逐日调用**，不得一次性跨多日。
        ///
        /// <see cref="LiveClockSimulator.Advance"/> 有追赶上限
        /// （<c>LiveClockLogic.MaxCatchUpMinutes = 1440</c>，即 1 天）：一次传 3 天窗口会被
        /// 悄悄截成 1 天，日固定只记一天、航班只跑一天。本文件第一版就踩了这个坑——
        /// 断言期望 3 天成本，实测只有 1 天，看起来像「账错了」，实际是「窗口被截了」。
        /// 生产侧每次 tick 推进量远小于此上限，故这里按同样的粒度逐日推进才等价。
        /// </summary>
        static void Advance(GameState gs, int days)
        {
            for (int i = 0; i < days; i++)
            {
                var from = gs.SimUtc;
                var to = from.AddDays(1);
                gs.SetSimUtc(to);
                LiveClockSimulator.Advance(gs, from, to);
            }
        }

        static RouteData MakeRoute(string id, string registration, float dailyProfit = ProfitPerDay)
        {
            var r = new RouteData
            {
                routeId = id, origin = "PEK", destination = "SHA", distanceKm = 1100f,
                aircraftType = "A320neo", flightNumber = "NG100", registration = registration,
                depTime = "08:00", arrTime = "10:30", daysOfWeek = "每日", weeklyFlights = 7,
                baseDailyProfit = dailyProfit, dailyProfit = dailyProfit, groundStaffAssigned = 2
            };
            r.SyncFlightsFromPrimary();
            return r;
        }

        static Aircraft MakeAircraft(string registration)
            => new Aircraft
            {
                registration = registration, type = "A320neo", isActive = true,
                status = AircraftStatuses.Airworthy, age = 3, condition = 100f, economySeats = 180
            };

        // ─────────────────────────────────────────────────────────────────────
        // S4 维护（定检预付 + 月维护日摊）—— 实时窗口
        // ─────────────────────────────────────────────────────────────────────
        [UnityTest]
        public IEnumerator S4_MaintenanceCheckIsPrepaidOnce_AndMonthlyMaintAccruesDaily()
        {
            var gs = State("S4GS");
            gs.fleet.Add(MakeAircraft("B-MNT"));
            gs.activeRoutes.Add(MakeRoute("S4-ROUTE", "B-MNT"));
            var mnt = new GameObject("S4Mnt").AddComponent<MaintenanceManager>();
            // 月维护口径挂在 FleetManager.Instance 上（GameState.FleetMonthlyMaintenance 只做转发），
            // 少了它 daily 里就没有维护项，这条用例会退化成「只测烧钱率」。
            new GameObject("S4Fleet").AddComponent<FleetManager>();

            float daily = DailyFixed(gs);
            Assert.Greater(gs.FleetMonthlyMaintenance, 0.01f, "前置：在役机必须计月维护，否则日摊无从证明");

            float cash0 = gs.cash;
            Advance(gs, 3);

            float mult = gs.CrewQualityMultiplier;
            Assert.AreEqual(3f * ProfitPerDay * mult, gs.liveQuarterIncome, Tol, "三日营收按机组乘数入桶");
            Assert.AreEqual(3f * daily, gs.liveQuarterExpense, Tol,
                "月维护必须按日摊进季度支出桶（季末一次性入账会让中途退租/加机对不上）");
            AssertCashEqualsBuckets(gs, cash0, 0f, 0f, 0f, "S4 飞行三日");

            // —— 定检：当天扣现金，但不进桶、不冲损益 ——
            // 进桶 = 季末再计一次（双扣）；冲损益 = 与现金重复计一次。
            float cost = MaintenanceEconomics.CheckCostWan("C", gs.fleet.Count);
            Assert.Greater(cost, 0.01f, "前置：C 检报价必须为正");
            float cashBeforeCheck = gs.cash;
            float expBeforeCheck = gs.liveQuarterExpense;
            float profitBeforeCheck = gs.yearNetProfit;
            int checksBefore = MaintenanceManager.EnsureState(gs).checks.Count;

            Assert.IsTrue(mnt.ScheduleCheck(gs, "B-MNT", "C", 1, out string msg), "排检被拒：" + msg);
            Assert.AreEqual(cashBeforeCheck - cost, gs.cash, Tol, "排检必须当天扣报价全额");
            Assert.AreEqual(expBeforeCheck, gs.liveQuarterExpense, Tol,
                "定检预付不得进季度支出桶 —— 否则季末按桶再结算一次就是双扣");
            Assert.AreEqual(profitBeforeCheck, gs.yearNetProfit, Tol, "定检预付不得直接冲损益");
            Assert.AreEqual(checksBefore + 1, MaintenanceManager.EnsureState(gs).checks.Count, "排检必须留一条记录");

            // —— 跨过到期月：不得再扣第二次，也不得凭空多出一条记录 ——
            float cashAfterCheck = gs.cash;
            float expMark = gs.liveQuarterExpense;
            float incMark = gs.liveQuarterIncome;
            float transMark = gs.liveQuarterTransitionIncome;
            Advance(gs, 32);
            float expDelta = gs.liveQuarterExpense - expMark;
            Assert.AreEqual(32f * daily, expDelta, Tol, "跨月后日固定照常逐日入桶");
            // 这 32 天里航线照飞、落地照入营收，所以现金净变化不再是「−32×日成本」的纯支出，
            // 只能改用「Δ现金 = Σ桶」的恒等式：定检费若在到期月被二次扣除，现金会比台账少
            // 一整个报价，恒等式立刻失败（既证桶外无扣款，也证没有重复计一次）。
            AssertCashEqualsBuckets(gs, cashAfterCheck, incMark, expMark, transMark,
                "S4 跨到期月（定检费已在排检当日扣清，桶外不得再扣）");
            Assert.AreEqual(checksBefore + 1, MaintenanceManager.EnsureState(gs).checks.Count,
                "定检记录不得被重复登记或悄悄清掉");
            yield return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // S5 退租／退役（自有回收残值、租赁交还出租人）—— 实时窗口
        // ─────────────────────────────────────────────────────────────────────
        [UnityTest]
        public IEnumerator S5_RetireOwnedAndReturnLeased_HitCashNotBuckets_AndStopMonthlyCharges()
        {
            var gs = State("S5GS");
            var owned = OwnedAircraft("B-OWN", 1000f, 5);
            var leased = OwnedAircraft("B-LEASE", 0f, 3);
            leased.source = FleetEconomics.SourceOperatingLease;
            leased.monthlyLeaseWan = 6f;
            gs.fleet.Add(owned);
            gs.fleet.Add(leased);
            gs.activeRoutes.Add(MakeRoute("S5-ROUTE", "B-OWN"));

            var fm = new GameObject("S5Fleet").AddComponent<FleetManager>();
            float daily0 = DailyFixed(gs);
            float cash0 = gs.cash;
            Advance(gs, 2);
            Assert.AreEqual(2f * daily0, gs.liveQuarterExpense, Tol, "两日日固定入桶");
            AssertCashEqualsBuckets(gs, cash0, 0f, 0f, 0f, "S5 处置前");

            // ① 自有机退役：现金 += 残值，损益与桶都不动（残值是处置现金，不是经营收入）
            float residual = FleetManager.CalcResidualValue(owned);
            Assert.Greater(residual, 0.01f, "前置：5 年自有机残值必须为正");
            float cashBeforeOwned = gs.cash;
            float profitBeforeOwned = gs.yearNetProfit;
            float expBeforeOwned = gs.liveQuarterExpense;
            Assert.IsTrue(fm.TryRetireAircraft(gs, "B-OWN", out string msgOwned), msgOwned);
            Assert.AreEqual(cashBeforeOwned + residual, gs.cash, Tol, "退役必须回收残值到现金");
            Assert.AreEqual(profitBeforeOwned, gs.yearNetProfit, Tol,
                "残值不得进损益 —— 进了就是「卖飞机算经营利润」，年报与现金流会长期打架");
            Assert.AreEqual(expBeforeOwned, gs.liveQuarterExpense, Tol, "残值不得进支出桶（负数支出语义是错的）");

            float dailyAfterOwned = DailyFixed(gs);
            Assert.Less(dailyAfterOwned, daily0 - 0.01f, "退役后该机月维护必须停止（否则退了还在扣维护）");

            // ② 经营租赁退租：违约金 0 → 现金不动、不回收残值（产权在出租人）
            float cashBeforeLease = gs.cash;
            float profitBeforeLease = gs.yearNetProfit;
            Assert.IsTrue(fm.TryRetireAircraft(gs, "B-LEASE", out string msgLease), msgLease);
            Assert.AreEqual(cashBeforeLease, gs.cash, Tol,
                "经营租赁退租不得回收残值 —— 零成本租进、按目录价卖出等于凭空造钱");
            Assert.AreEqual(profitBeforeLease, gs.yearNetProfit, Tol, "退租不得改损益");

            float dailyAfterLease = DailyFixed(gs);
            Assert.Less(dailyAfterLease, dailyAfterOwned - 0.01f, "退租后月租必须停止");

            // ③ 处置后再走两日：执飞机已退役 → 无营收，只扣新的日固定
            float cashAfterBoth = gs.cash;
            float incMark = gs.liveQuarterIncome;
            float expMark = gs.liveQuarterExpense;
            Advance(gs, 2);
            Assert.AreEqual(incMark, gs.liveQuarterIncome, Tol, "机号已退役不得继续产生营收（幽灵收入）");
            Assert.AreEqual(expMark + 2f * dailyAfterLease, gs.liveQuarterExpense, Tol, "处置后日固定按新费率入桶");
            Assert.AreEqual(cashAfterBoth - 2f * dailyAfterLease, gs.cash, Tol, "处置后现金只减新日固定");

            // 恒等式要**显式**写上残值这一项：它是一次性处置现金，不属于任何经营桶。
            // 若哪天有人把残值塞进收入桶，这条会立刻翻红（那就是双计）。
            float expectedCash = cash0
                                 + (gs.liveQuarterIncome - 0f)
                                 + (gs.liveQuarterTransitionIncome - 0f)
                                 - (gs.liveQuarterExpense - 0f)
                                 + residual;
            Assert.AreEqual(expectedCash, gs.cash, Tol,
                "Δ现金 == Σ经营桶 + 处置残值（残值不得隐含在任何桶里）");
            yield return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // S7 贷款偿还（季末一次扣 3 个月本息；本金不动损益）—— 实时窗口
        // ─────────────────────────────────────────────────────────────────────
        [UnityTest]
        public IEnumerator S7_QuarterEndRepayment_TakesPrincipalFromCash_OnlyInterestFromProfit()
        {
            var gs = State("S7GS");
            gs.activeLoans = new List<Loan>
            {
                new Loan
                {
                    id = "S7-LOAN", type = "bank", principal = 120f, remainingPrincipal = 120f,
                    interestRate = 0.12f, monthlyPayment = 11.2f, termMonths = 12, monthsRemaining = 12
                }
            };
            new GameObject("S7Loans").AddComponent<LoanManager>();
            var loanMgr = LoanManager.Instance;
            var clock = new GameObject("S7Clock").AddComponent<TimeManager>();
            clock.oilEventChance = 0;

            const float due = 33.6f;          // 11.2 × 3
            const float interest = 3.6f;      // 120 × 0.12 / 12 × 3
            const float principal = 30f;      // 33.6 − 3.6

            // ── 1) 口径：现金扣本息全额、损益只扣利息 ──
            // 单独调一次还款，把季末那些**不属于贷款**的一次性项（FX 汇兑、油价对冲、
            // 安全罚金……）排除在外 —— 它们会双向变动现金，混进来就分辨不出本金去了哪。
            float cash0 = gs.cash;
            float profit0 = gs.yearNetProfit;
            string receipt = loanMgr.ProcessQuarterRepayments();

            Assert.AreEqual(cash0 - due, gs.cash, Tol, "本息必须全额从现金扣（本金也是真金白银）");
            Assert.AreEqual(profit0 - interest, gs.yearNetProfit, Tol,
                "损益只承担利息：本金偿还是融资现金流出，不是经营亏损");
            StringAssert.Contains("融资本息", receipt, "还款必须留下可读回执（财务流水）");
            Assert.AreEqual(1, gs.activeLoans.Count, "未还清的贷款必须留在册");
            Assert.AreEqual(120f - principal, gs.activeLoans[0].remainingPrincipal, 0.001f, "本金余额必须减 30");
            Assert.AreEqual(9, gs.activeLoans[0].monthsRemaining, "剩余期数必须减 3");

            // ── 2) 时间边界：季度推进必须自动触发还款，不用玩家点按钮 ──
            float cash1 = gs.cash;
            var result = clock.AdvanceQuarter();
            Assert.IsTrue(result.success, "季度推进失败：" + result.message);
            // 二期利息按**上期余额** 90 计（不是原始本金 120），所以本金部分比首期多 0.9：
            // 33.6 − 90×1%×3 = 30.9。按等额本息摊销，余额越还越少、利息越还越少。
            float interest2 = 90f * 0.12f / 12f * 3f;
            float principal2 = due - interest2;
            Assert.AreEqual(90f - principal2, gs.activeLoans[0].remainingPrincipal, 0.001f,
                "季结必须再自动扣一期本金（窗口没触发，贷款就会被无限拖下去）");
            Assert.AreEqual(6, gs.activeLoans[0].monthsRemaining, "季结必须再自动扣 3 期");
            Assert.Less(gs.cash, cash1, "季结现金必须下降（经营成本 + 本息；一次性项可能双向，故不锁死金额）");
            Assert.AreEqual(0f, gs.liveQuarterExpense, Tol, "季末桶必须清零（否则下一季重复分摊）");
            yield return null;
        }

        static Aircraft OwnedAircraft(string registration, float purchasePrice, int age)
            => new Aircraft
            {
                registration = registration, type = "A320neo", isActive = true,
                status = AircraftStatuses.Airworthy, condition = 100f, economySeats = 180,
                purchasePrice = purchasePrice, purchaseYear = 1, age = age
            };

        /// <summary>现金恒等式：Δ现金必须等于 Σ季度桶的增量（没有桶外项目时才成立）。</summary>
        static void AssertCashEqualsBuckets(GameState gs, float cash0, float income0, float expense0,
            float transition0, string what)
        {
            float expected = cash0
                             + (gs.liveQuarterIncome - income0)
                             + (gs.liveQuarterTransitionIncome - transition0)
                             - (gs.liveQuarterExpense - expense0);
            Assert.AreEqual(expected, gs.cash, Tol,
                what + "：Δ现金必须等于 Σ应计桶（现金与台账同源，禁止桶外挂账）");
        }

        // ─────────────────────────────────────────────────────────────────────
        // S1 停飞（有班但无执飞机，持续性）
        // ─────────────────────────────────────────────────────────────────────
        [UnityTest]
        public IEnumerator S1_GroundedRoute_BooksOnlyDailyFixedCost_AndReportsZeroIncome()
        {
            var gs = State("S1GS");
            gs.activeRoutes.Add(MakeRoute("S1-ROUTE", ""));
            float daily = DailyFixed(gs);
            float cash0 = gs.cash;

            Advance(gs, 3);

            Assert.AreEqual(0f, gs.liveQuarterIncome, 1e-4f, "停飞线不得有任何落地收入");
            Assert.AreEqual(3f * daily, gs.liveQuarterExpense, Tol,
                "停飞期间日固定成本必须照记（不是「停飞=不花钱」）");
            Assert.AreEqual(-3f * daily, gs.yearNetProfit, Tol, "损益必须与实付日固定同额减少");
            Assert.AreEqual(cash0 - 3f * daily, gs.cash, Tol, "现金只应减少日固定成本");
            Assert.AreEqual(0f, gs.monthlyRevenue, 1e-4f, "停飞线必须退出计划收入口径（否则季末按预测补付）");
            Assert.AreEqual(GroundReason.NoAssignment, FlightGroundLogic.RouteOf(gs, gs.activeRoutes[0]));
            AssertCashEqualsBuckets(gs, cash0, 0f, 0f, 0f, "S1 停飞");
            yield return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // S2 无机（机号不在册 / 已退役）—— 不允许「幽灵收入」从现金侧漏出来
        // ─────────────────────────────────────────────────────────────────────
        [UnityTest]
        public IEnumerator S2_MissingThenRetiredAircraft_LeaksNoRevenueIntoCash()
        {
            var gs = State("S2GS");
            var route = MakeRoute("S2-ROUTE", "B-OLD");
            gs.activeRoutes.Add(route);
            float daily = DailyFixed(gs);
            float cash0 = gs.cash;

            // ① 机号不在机队
            Advance(gs, 2);
            Assert.AreEqual(GroundReason.Missing, FlightGroundLogic.RouteOf(gs, route));
            Assert.AreEqual(0f, gs.liveQuarterIncome, 1e-4f, "机号不在册不得产生营收");
            Assert.AreEqual(cash0 - 2f * daily, gs.cash, Tol, "只应扣日固定成本");
            AssertCashEqualsBuckets(gs, cash0, 0f, 0f, 0f, "S2 机号不在册");

            // ② 机号在册但已退役（老存档典型残留）
            gs.fleet.Add(new Aircraft
            {
                registration = "B-OLD", type = "A320neo", isActive = false,
                status = AircraftStatuses.Retired, condition = 100f
            });
            float cash1 = gs.cash;
            // 退役机不计月租、不进维护口径，故日固定必须不变；不变是后面对账的前提。
            Assert.AreEqual(daily, DailyFixed(gs), Tol, "退役机不得继续产生月租/维护（否则退租后还在扣钱）");

            Advance(gs, 2);
            Assert.AreEqual(GroundReason.Retired, FlightGroundLogic.RouteOf(gs, route));
            Assert.AreEqual(0f, gs.liveQuarterIncome, 1e-4f, "退役机不得计入营收");
            Assert.AreEqual(cash1 - 2f * daily, gs.cash, Tol, "退役机同样只应扣日固定成本");
            yield return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // S3 缺员（68/80 vs 80/80）—— 缺的是「覆盖率折损」，不是「固定成本少扣」
        // ─────────────────────────────────────────────────────────────────────
        [UnityTest]
        public IEnumerator S3_CrewShortage_ScalesFlightContribution_NotFixedCost()
        {
            const int days = 2;
            var shortCrew = CrewState("S3GS-Short", vacancyPercent: 15);
            var fullCrew = CrewState("S3GS-Full", vacancyPercent: 0);

            // 前置：两档只差覆盖率，其余（机队/航线/战力/日固定）逐项相同
            Assert.AreEqual(80, shortCrew.PilotEstablishment, "10 架 A320neo → 飞行员编制 80");
            Assert.AreEqual(68, shortCrew.PilotFilled, "缺员 15% → 68/80");
            Assert.AreEqual(80, fullCrew.PilotFilled, "满编 → 80/80");
            Assert.AreEqual(0f, shortCrew.OperationalPayrollMonthlyWan, 1e-4f,
                "继承机组薪酬已在聚合口径内，不得计入运行岗增量（否则两档日固定不可比）");
            Assert.AreEqual(DailyFixed(shortCrew), DailyFixed(fullCrew), Tol,
                "两档日固定成本必须相同，否则差额里混进了成本而不是乘数");
            Assert.AreEqual(0.9775f, shortCrew.CrewQualityMultiplier, 0.001f, "68/80 覆盖率插值");
            Assert.AreEqual(1f, fullCrew.CrewQualityMultiplier, 0.001f, "80/80 满覆盖率");

            float profit = ProfitPerDay;
            float multShort = shortCrew.CrewQualityMultiplier;
            float multFull = fullCrew.CrewQualityMultiplier;

            float shortCash0 = shortCrew.cash;
            float fullCash0 = fullCrew.cash;
            Advance(shortCrew, days);
            Advance(fullCrew, days);

            // 每趟往返两段，各段净贡献 = 航线日净利 / 2，再乘机组乘数 —— 故一日往返 = 日净利 × 乘数。
            Assert.AreEqual(days * profit * multShort, shortCrew.liveQuarterIncome, Tol,
                "缺员窗口的收入必须正好被机组乘数折损");
            Assert.AreEqual(days * profit * multFull, fullCrew.liveQuarterIncome, Tol,
                "满编窗口的收入必须是不折损口径");
            Assert.AreEqual(days * DailyFixed(shortCrew), shortCrew.liveQuarterExpense, Tol,
                "缺员只折损收入，不得少扣固定成本");
            Assert.AreEqual(days * DailyFixed(fullCrew), fullCrew.liveQuarterExpense, Tol,
                "满编日固定照扣");
            Assert.AreEqual(shortCrew.liveQuarterExpense, fullCrew.liveQuarterExpense, Tol,
                "两窗口的季度成本桶必须逐位相同");

            Assert.AreEqual(days * profit * (multFull - multShort),
                fullCrew.liveQuarterIncome - shortCrew.liveQuarterIncome, Tol,
                "两窗口的收入差 = 日净利 × 天数 × (满编乘数 − 缺员乘数)");
            Assert.AreEqual(fullCrew.cash - shortCrew.cash, fullCrew.liveQuarterIncome - shortCrew.liveQuarterIncome,
                Tol, "成本相同 ⇒ 现金差必须全部来自收入差");

            AssertCashEqualsBuckets(shortCrew, shortCash0, 0f, 0f, 0f, "S3 缺员 68/80");
            AssertCashEqualsBuckets(fullCrew, fullCash0, 0f, 0f, 0f, "S3 满编 80/80");
            yield return null;
        }

        /// <summary>S3 夹具：10 架在役机（制造 80 人飞行员编制）+ 一架执飞一条日班航线。</summary>
        static GameState CrewState(string name, int vacancyPercent)
        {
            var gs = State(name);
            for (int i = 0; i < 10; i++) gs.fleet.Add(MakeAircraft("B-CR" + i));
            PersonnelGenerator.SeedInheritedCrew(gs, vacancyPercent);
            // 把每人战力压到达标线：两档只差**覆盖率**，不差个人质量（随机种子不同会让质量项漂移）。
            for (int i = 0; i < gs.staffPool.Count; i++)
                if (gs.staffPool[i] != null) gs.staffPool[i].efficiencyBonus = HrEconomics.CrewAdequacyBaseline;
            gs.InvalidateCrewCache();
            gs.activeRoutes.Add(MakeRoute("S3-ROUTE", "B-CR0"));
            return gs;
        }

        // ─────────────────────────────────────────────────────────────────────
        // S6 转入过渡业务：定金入账 / 逐日入账 / 召回违约金
        // ─────────────────────────────────────────────────────────────────────
        [UnityTest]
        public IEnumerator S6_TransitionDepositDailyAndRecallPenalty_HitTheRightBuckets()
        {
            var gs = State("S6GS");
            gs.fleet.Add(MakeAircraft("B-TR"));
            float daily = DailyFixed(gs);

            gs.transitionOffers = new List<TransitionOffer>
            {
                new TransitionOffer
                {
                    id = "to_s6", kind = TransitionKinds.Dry, client = "极地科考队",
                    rateWanPerDay = TransitionBusinessLogic.DryRate, totalDays = TransitionBusinessLogic.LeaseDays
                }
            };

            float cash0 = gs.cash, profit0 = gs.yearNetProfit, trans0 = gs.liveQuarterTransitionIncome;
            Assert.IsTrue(TransitionBusinessLogic.Sign(gs, "to_s6", "B-TR", out string signMsg,
                refreshInquiry: false), "干租无门槛，必须能签：" + signMsg);

            var c = gs.transitionContracts[0];
            // 三段付款必须自洽：定金 + 逐日 × 天数 + 尾款 == 合同总额
            Assert.AreEqual(c.TotalWan, c.depositWan + c.dailyCashWan * c.totalDays + c.balanceWan, 0.1f,
                "定金/逐日/尾款拆分必须等于合同总额（拆分丢钱或造钱都会在这里露出来）");
            Assert.Greater(c.depositWan, 0f, "签约当日必须收定金");

            Assert.AreEqual(cash0 + c.depositWan, gs.cash, Tol, "签约只应增加定金这一笔现金");
            Assert.AreEqual(profit0 + c.depositWan, gs.yearNetProfit, Tol, "定金必须同时进损益");
            Assert.AreEqual(trans0 + c.depositWan, gs.liveQuarterTransitionIncome, Tol,
                "定金必须进过渡业务季度桶");
            AssertCashEqualsBuckets(gs, cash0, 0f, 0f, trans0, "S6 签约");

            // —— 逐日入账 2 天：日收进桶，日固定照扣 ——
            float cashAfterSign = gs.cash;
            float transAfterSign = gs.liveQuarterTransitionIncome;
            float expAfterSign = gs.liveQuarterExpense;
            Advance(gs, 2);

            Assert.AreEqual(2f * c.EffectiveDailyCash, gs.liveQuarterTransitionIncome - transAfterSign, Tol,
                "过渡业务逐日租金必须进季度桶");
            Assert.AreEqual(2f * daily, gs.liveQuarterExpense - expAfterSign, Tol,
                "出租期间日固定成本照扣（飞机租出去不等于公司不烧钱）");
            Assert.AreEqual(cashAfterSign + 2f * c.EffectiveDailyCash - 2f * daily, gs.cash, Tol,
                "现金 = 定金 + 两日租金 − 两日日固定");
            AssertCashEqualsBuckets(gs, cash0, 0f, 0f, trans0, "S6 逐日");

            // —— 提前召回：3 天通知期，期满付剩余租金 30% 违约金 ——
            Assert.IsTrue(TransitionBusinessLogic.Recall(gs, "B-TR", out string recallMsg), recallMsg);
            float fee = c.recallFeeWan;
            Assert.AreEqual(Mathf.Round(c.DaysLeft * c.EffectiveDailyCash * 0.30f * 10f) / 10f, fee, 0.001f,
                "违约金必须等于剩余租金 × 30%（与预览同一函数）");
            Assert.Greater(fee, 0f, "刚签约就召回，剩余租金必然大于 0");

            float cashBeforeNotice = gs.cash;
            float profitBeforeNotice = gs.yearNetProfit;
            float transBeforeNotice = gs.liveQuarterTransitionIncome;
            float expBeforeNotice = gs.liveQuarterExpense;
            Advance(gs, TransitionBusinessLogic.RecallNoticeDays);

            Assert.AreEqual(0, TransitionBusinessLogic.ActiveContracts(gs).Count,
                "通知期满必须交机出册");
            // 通知期损益 = 租金 − 日固定成本 − 违约金（违约金进损益，否则年报与现金流长期偏离）
            float noticeDays = TransitionBusinessLogic.RecallNoticeDays;
            float profitDrop = profitBeforeNotice - gs.yearNetProfit;
            Assert.AreEqual(noticeDays * daily - noticeDays * c.EffectiveDailyCash + fee, profitDrop, Tol,
                "召回违约金必须进损益（不能只动现金，否则年报与现金流长期偏离）");
            Assert.AreEqual(cashBeforeNotice
                            + noticeDays * c.EffectiveDailyCash
                            - noticeDays * daily - fee, gs.cash, Tol,
                "通知期内租金照付，期满另扣违约金");

            // R-B / B2（2026-10-25 裁定）：违约金记入 liveQuarterExpense（支出桶），
            // 不进 liveQuarterTransitionIncome（那是收入栏，塞负数语义是错的）。
            // 这样「Δ现金 == Σ应计桶」在召回当期也成立 —— 否则现金少了却没有任何桶记这笔钱，
            // 跨季对账时它「凭空消失」。
            Assert.Greater(gs.liveQuarterTransitionIncome, transBeforeNotice, "通知期内租金照常进桶");
            Assert.AreEqual(TransitionBusinessLogic.RecallNoticeDays * daily + fee,
                gs.liveQuarterExpense - expBeforeNotice,
                Tol, "通知期支出 = 日固定 + 违约金（违约金必须进季度支出桶，否则恒等式留例外）");
            AssertCashEqualsBuckets(gs, cash0, 0f, 0f, trans0, "S6 召回交机");
            yield return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // S8 季度中途业务变化：第 46 天加机加线，只按变更后的实际天数入账
        // ─────────────────────────────────────────────────────────────────────
        [UnityTest]
        public IEnumerator S8_MidQuarterRouteAddition_SplitsAccrualAtTheActualDay()
        {
            const int preDays = 45;
            const int postDays = 44;

            var gs = State("S8GS");
            float daily = DailyFixed(gs);
            float deptBefore = DeptDaily(gs);     // 变更前费率（加机前必须取，加机后就变了）
            float cash0 = gs.cash;

            Advance(gs, preDays);
            float cashMid = gs.cash;
            Assert.AreEqual(0f, gs.liveQuarterIncome, 1e-4f, "变更前没有可执飞航线，不应有任何收入");
            Assert.AreEqual(cash0 - preDays * daily, cashMid, Tol, "变更前只扣日固定");

            // —— R-A / A2：部门预算问责的分母必须**逐日累计**，不是「季末费率 ×3」 ——
            Assert.AreEqual(preDays * deptBefore, gs.liveQuarterDeptExpense, Tol,
                "变更前 45 天的部门口径分母应按当日费率累计（旧实现只会取季末费率 ×3）");

            // —— 第 46 天中途加一机一线 ——
            gs.fleet.Add(MakeAircraft("B-MID"));
            gs.activeRoutes.Add(MakeRoute("S8-ROUTE", "B-MID"));
            // R-A / A2 的前置：为了让「逐日累计 ≠ 季末费率 ×3」可被证伪，这里**主动**改一个部门口径项
            // （烧钱率 +30 万/月）。只靠加机不够 —— 加机抬升机队维护的同时，烧钱率会按机队规模重算，
            // 四项合计可能几乎不动（实测 +1 架只让部门口径动了 −0.0001 万/日），那样加权和退化成乘法，
            // 断言就分辨不出新旧实现。
            gs.monthlyBurnRate += 30f;
            // 加机后日固定**会变**：在役机要计月维护（本夹具里 1 架 A320neo 约 20 万/月）。
            // 所以变更后的成本必须按变更后费率现算，不能沿用变更前的 daily —— 这正是
            // 矩阵 §6.4「部门预算按季末费率 ×3 必然与实扣分叉」的同一个坑，只是这里我们自己不自欺。
            float dailyAfter = DailyFixed(gs);
            Assert.Greater(dailyAfter, daily + 1e-4f, "前置：新增在役机必须使日固定成本上移");
            float deptAfter = DeptDaily(gs);
            Assert.Greater(deptAfter, deptBefore + 0.9f,
                "前置：烧钱率 +30 万/月 → 部门口径日成本至少上移约 1 万/日（否则加权和分辨不出新旧实现）");

            float incMid = gs.liveQuarterIncome;
            float expMid = gs.liveQuarterExpense;
            Advance(gs, postDays);

            float mult = gs.CrewQualityMultiplier;      // 未建机组 → 保底乘数
            float expectedIncome = postDays * ProfitPerDay * mult;

            Assert.Greater(expectedIncome, 0f, "前置：未建机组也应有正贡献（乘数只折损不归零）");
            Assert.AreEqual(expectedIncome, gs.liveQuarterIncome - incMid, Tol,
                "只按变更后的实际天数入账，绝不追溯补记变更前的 45 天");
            Assert.AreEqual(postDays * dailyAfter, gs.liveQuarterExpense - expMid, Tol,
                "变更后日固定按**变更后**费率照扣（含新增机的月维护）");

            // —— R-A / A2：加机只影响之后的日数，前 45 天不得被按新费率重算 ——
            Assert.AreEqual(preDays * deptBefore + postDays * deptAfter, gs.liveQuarterDeptExpense, Tol,
                "部门口径分母 = 变更前日数 × 变更前费率 + 变更后日数 × 变更后费率（中途加机不追溯）");
            Assert.AreEqual(cashMid + expectedIncome - postDays * dailyAfter, gs.cash, Tol,
                "现金 = 变更前余额 + 变更后应计");
            AssertCashEqualsBuckets(gs, cash0, 0f, 0f, 0f, "S8 中途变更");
            yield return null;
        }
    }
}
