using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using AirlineCEO.UI.Shell;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// T03.8 逐屏（2026-10-27）：**底栏三个钱数必须与跑道同一把尺子，且标签不得说谎**。
    ///
    /// 事故现场（用户实机截图，NS156/NS157 · PEK⇄LHR · 8 航线 12 架）：
    /// 底栏 `收入:2,455,650　支出:5,685,000　盈利:-3,229,350`。三个数互相减得通，
    /// 所以肉眼验算不会报错，但两处口径都是错的：
    ///
    /// ① **「收入」其实是净利**。`gs.monthlyRevenue` 累计的是各航线 `dailyProfit`
    ///    （`OpsEconomics.RouteDailyProfit` 已扣完航线变动成本与周期固定成本）乘经营天数。
    ///    玩家看到「收入 245 万 / 支出 568 万」，自然会读成「毛利 −131%」；
    ///    真实故事是「航线每月净赚 82 万、固定开销 190 万压着」—— 完全不同的经营判断。
    /// ② **「支出」只算了两项**：`(monthlyBurnRate + FleetMonthlyMaintenance) × 3`。
    ///    跑道用的 `EffectiveMonthlyBurn` 含九项（另加租赁 / CEO / 运行岗 / 存量人力 / 保费 /
    ///    还本付息 / 危机加成）。于是同一屏上「盈利」与「跑道」推出两条不同的烧钱速度。
    ///
    /// 本回归钉住六件事：
    /// ① 成本基数完整：`MonthlyOperatingCostWan` 等于九项逐项之和；
    /// ② 与跑道互算：`EffectiveMonthlyBurn == max(0, 成本 − 收入)`，逐位相等；
    /// ③ 底栏三项自洽：`盈亏 == 收入 − 支出`，且支出 == 成本 × 3；
    /// ④ **旧口径会漏钱**：六项里任一项非零时，新支出必须**严格大于**旧的「两项 × 3」——
    ///    这条才是能抓住 2026-10-27 那个缺陷的断言，只钉 ①②③ 的话旧实现也能过；
    /// ⑤ 标签不说谎：前缀必须含「季度」，且**不得**出现「收入」「盈利」二字；
    /// ⑥ 亏损时 `盈亏 == −跑道 × 3`：玩家可以从底栏直接反推跑道，不再两套数；
    /// ⑦ null 安全：`QuarterSnapshot(null)` 不抛异常（`RefreshStats` 的 gs==null 分支走它）。
    /// </summary>
    public static class StatusBarReconcileRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__status_bar_reconcile__");
            try
            {
                var gs = go.AddComponent<GameState>();

                // —— 造一个「六项里只有一项非零」的最小局面 ——
                // 不铺航线、不建机队：收入侧为 0，成本侧只留固定开销 + 一项可控的 CEO 薪酬。
                // 这样「漏项」的差额就等于那一项本身，断言不会因为别的项恰好奇偶抵消而假绿。
                gs.activeRoutes = new List<RouteData>();
                gs.fleet = new List<Aircraft>();
                gs.inbox = new List<InboxMail>();
                gs.monthlyBurnRate = 80f;
                gs.ceoWeeklyPayWan = 13f;        // 13 × 52 / 12 ≈ 56.33 万/月
                gs.financeCrisisBurnBonus = 7f;

                var sb = new StringBuilder();
                bool ok = true;
                void Check(string id, bool pass, string detail)
                {
                    if (!pass) ok = false;
                    sb.Append($"\n　{(pass ? "✓" : "✗")} {id}：{detail}");
                }

                float cost = gs.MonthlyOperatingCostWan;
                float income = gs.MonthlyOperatingIncomeWan;
                float burn = gs.EffectiveMonthlyBurn;

                // ① 成本基数完整：九项逐项重算（顺序与 GameState 内一致，别在这里「优化」成求和再减）
                float ceoMonth = Mathf.Max(0f, gs.ceoWeeklyPayWan) * 52f / 12f;
                float expectCost = gs.monthlyBurnRate + gs.FleetMonthlyMaintenance + gs.FleetMonthlyLease + ceoMonth
                    + gs.OperationalPayrollMonthlyWan
                    + gs.LaborCostMonthlyWan
                    + gs.FleetMonthlyInsurance
                    + AirlineCEO.Systems.LoanManager.MonthlyDebtService(gs)
                    + Mathf.Max(0f, gs.financeCrisisBurnBonus);
                Check("①成本基数九项", Mathf.Abs(cost - expectCost) <= 0.0001f,
                    $"口径 {cost:F4} vs 逐项 {expectCost:F4}（CEO {ceoMonth:F4} + 危机 {gs.financeCrisisBurnBonus:F1}）");

                // ② 与跑道互算
                float expectBurn = Mathf.Max(0f, cost - income);
                Check("②跑道可互算", Mathf.Abs(burn - expectBurn) <= 0.0001f,
                    $"实际 {burn:F4} vs max(0, {cost:F4} − {income:F4}) = {expectBurn:F4}");

                // ③ 底栏三项自洽
                StatusBarView.QuarterSnapshot(gs, out float qIn, out float qEx, out float qProfit);
                Check("③底栏三项自洽",
                    Mathf.Abs(qProfit - (qIn - qEx)) <= 0.0001f
                    && Mathf.Abs(qEx - cost * 3f) <= 0.0001f
                    && Mathf.Abs(qIn - income * 3f) <= 0.0001f,
                    $"盈亏 {qProfit:F2} = 收入 {qIn:F2} − 支出 {qEx:F2}；支出 == 成本×3（{cost * 3f:F2}）");

                // ④ 旧口径会漏钱 —— 这条专门盯 2026-10-27 的缺陷
                float legacyExpense = (gs.monthlyBurnRate + gs.FleetMonthlyMaintenance) * 3f;
                Check("④旧两项口径会漏钱", qEx > legacyExpense + 1f,
                    $"新支出 {qEx:F2} 必须严格大于旧两项×3 {legacyExpense:F2}（差 {qEx - legacyExpense:F2} = 六项之和×3）");

                // ⑤ 标签不说谎
                string incomeLabel = StatusBarView.IncomePrefix;
                string expenseLabel = StatusBarView.ExpensePrefix;
                string profitLabel = StatusBarView.ProfitPrefix;
                bool noLie = !incomeLabel.Contains("收入") && !incomeLabel.Contains("盈利")
                    && !expenseLabel.Contains("收入") && !profitLabel.Contains("盈利");
                bool hasPeriod = incomeLabel.Contains("季度") && expenseLabel.Contains("季度") && profitLabel.Contains("季度");
                Check("⑤标签不说谎", noLie && hasPeriod,
                    $"「{incomeLabel}」「{expenseLabel}」「{profitLabel}」——需含「季度」且不得写「收入/盈利」");

                // ⑥ 亏损时可直接反推跑道
                Check("⑥可从底栏反推跑道", Mathf.Abs(qProfit + burn * 3f) <= 0.0001f,
                    $"盈亏 {qProfit:F2} == −跑道 {burn:F2} × 3 = {-burn * 3f:F2}");

                // ⑦ null 安全：RefreshStats 在 gs 为空时会走这条分支，不能抛异常
                bool threw = false;
                try { StatusBarView.QuarterSnapshot(null, out _, out _, out _); }
                catch { threw = true; }
                Check("⑦null 安全", !threw, "QuarterSnapshot(null) 不抛异常");

                // 说明：**没有**测「盈利局面」的下限夹取。营收侧走 `monthlyRevenue`，
                // 而它对「未指派飞机的航线」按停飞跳过（`FlightGroundLogic.IsPersistent`），
                // 在 EditMode 里凑一条真正在飞的航线要连飞机与波次一起铺，
                // 收益抵不上一条「测夹具而不是测口径」的断言。亏损侧的 ⑥ 才是本次缺陷的主战场。

                Debug.Log($"[T03.8 底栏口径] 底栏三项与跑道同源{sb}");
                if (!ok) Debug.LogError("[T03.8 底栏口径] 存在未通过断言，见上");
                return ok;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
