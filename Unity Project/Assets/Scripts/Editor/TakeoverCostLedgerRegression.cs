using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// K17 / D4：接管成本继承的粒度回归。
    ///
    /// 裁定（2026-10-23）：**四段式保留为成本驱动口径，部门预算逐项继承** —— 二者由
    /// <see cref="TakeoverCostLedger"/> 一份账本生成，不得各算一套。
    ///
    /// 本回归钉住六件事：
    /// ① 四段式金额逐位不变（K10 的 8/6/4/3 跑道断言不受影响，见 ②）；
    /// ② 四个具名预设的跑道路标 8.00 / 6.01 / 4.00 / 3.00（±0.02 月）；
    /// ③ 部门账本与四段式同账：Σ部门 = Σ四段（≤0.05 万/月），且 8 个部门都有归属；
    /// ④ 部门占比归一到 1；
    /// ⑤ 接管预算总量仍等于董事会规则（现金 × 50%），部门比例与账本同源；
    /// ⑥ 季度超支问责与预算同尺：按额度占比分摊，Σspent = 季度支出，比例与额度一致。
    ///
    /// 为什么单列回归而不并进 `--routeinventory`：后者只在离线工具里跑，
    /// 而**接管落盘**（`TakeoverWorldBuilder.Apply` → `budgetItems`）只有 Unity 里才走得到。
    /// 只测纯账本会被「账本写好了但 Apply 没接进去」骗过 —— 这正是 K19 那条「函数写好了没接进信件」的教训。
    /// </summary>
    public static class TakeoverCostLedgerRegression
    {
        public static bool Run()
        {
            var sb = new StringBuilder("K17 接管成本继承粒度 → ");
            bool all = true;

            // 四个具名预设：跑道路标 + 账本同账。
            var presetIds = new[] { PresetAirlines.Startup, PresetAirlines.Regional,
                                    PresetAirlines.Legacy, PresetAirlines.Distressed };
            var targets = new[] { 8f, 6f, 4f, 3f };
            float[] runways = new float[presetIds.Length];
            for (int i = 0; i < presetIds.Length; i++)
            {
                var p = TakeoverWorldBuilder.BuildPreview(presetIds[i]);
                runways[i] = p.cashRunwayMonths;
                bool runwayOk = !p.cashRunwayUnbounded
                                && Mathf.Abs(p.cashRunwayMonths - targets[i]) <= 0.02f;
                bool ledgerOk = CheckLedger(p, out string why);
                if (!runwayOk || !ledgerOk)
                {
                    all = false;
                    sb.Append($"\n　✗ {presetIds[i]} 跑道 {p.cashRunwayMonths:F2}（目标 {targets[i]:F0}）"
                              + $"　账本 {ledgerOk}{(ledgerOk ? "" : "：" + why)}");
                }
            }
            sb.Append($"预设跑道 {runways[0]:F2}/{runways[1]:F2}/{runways[2]:F2}/{runways[3]:F2}"
                      + "（期望 8.00/6.01/4.00/3.00，K10 不漂移）");

            // ③④ 全池抽查：部门账本与四段式逐家同账、占比归一。
            int bad = 0, missing = 0;
            var samples = new List<string>();
            foreach (var seed in PresetAirlines.AllSelectable())
            {
                if (seed == null) continue;
                var p = TakeoverWorldBuilder.BuildPreview(seed.id);
                if (p?.costLedger == null) { missing++; continue; }
                if (CheckLedger(p, out string why)) continue;
                bad++;
                if (samples.Count < 4) samples.Add($"{p.hubAirport}：{why}");
            }
            bool poolOk = bad == 0 && missing == 0;
            if (!poolOk) all = false;
            sb.Append($"\n　全池账本：缺账本 {missing} 家 / 不同账 {bad} 家"
                      + (samples.Count > 0 ? "（" + string.Join("；", samples) + "）" : ""));

            // ⑤⑥ 落盘与问责：真的建一个 GameState 走 Apply。
            var go = new GameObject("__k17_ledger__");
            try
            {
                var gs = go.AddComponent<GameState>();
                // EnsureDefaultBudget 是实例方法（生产里由 FinancePanelUI / InboxManager 经
                // `FinanceManager.Instance?.` 调用）。**EditMode 里 AddComponent 不跑 Awake**，
                // 所以 `Instance` 仍是 null —— 用 `Instance.` 会直接 NRE（2026-10-24 预检实测）。
                // 这里拿到组件引用本身调用：方法体只读 `gs`，与生产同一条代码路径。
                var finance = go.AddComponent<FinanceManager>();
                gs.inbox = new List<InboxMail>();
                gs.fleet = new List<Aircraft>();
                gs.activeRoutes = new List<RouteData>();
                gs.activeLoans = new List<Loan>();
                gs.budgetItems = new List<BudgetItem>();

                var template = TakeoverWorldBuilder.BuildPreview(PresetAirlines.Regional);
                TakeoverWorldBuilder.Apply(gs, template);

                var ledger = template.costLedger;
                float budgetRule = FinanceEconomics.RecommendedBudget(gs.cash);
                float budgetSum = FinanceManager.BudgetTotal(gs);
                bool seeded = gs.budgetItems != null
                              && gs.budgetItems.Count == FinanceEconomics.DeptShares.Length
                              && Mathf.Abs(budgetSum - budgetRule) <= 0.05f;
                // 部门比例必须与账本同源（容差 1 个百分点；总额规则不变，只换了摊分键）。
                bool sameKey = seeded;
                if (sameKey)
                {
                    var shares = ledger.DeptShares();
                    for (int i = 0; i < gs.budgetItems.Count; i++)
                    {
                        float want = budgetRule * shares[i];
                        if (Mathf.Abs(gs.budgetItems[i].amountWan - want) > Mathf.Max(0.5f, budgetRule * 0.01f))
                        {
                            sameKey = false;
                            sb.Append($"\n　✗ {gs.budgetItems[i].deptName} 额度 {gs.budgetItems[i].amountWan:F1}"
                                      + $" ≠ 账本份额 {want:F1}");
                            break;
                        }
                    }
                }
                if (!seeded || !sameKey) all = false;

                // 季度问责：按额度占比分摊 → Σspent 恰好等于季度支出，且各部门比例与额度一致。
                const float quarterExpense = 900f;
                FinanceManager.AccrueBudgetSpend(gs, quarterExpense);
                float spentSum = 0f;
                for (int i = 0; i < gs.budgetItems.Count; i++) spentSum += gs.budgetItems[i].spentWan;
                bool accrueTotal = Mathf.Abs(spentSum - quarterExpense) <= 0.05f;
                bool accrueRatio = true;
                for (int i = 0; i < gs.budgetItems.Count && accrueRatio; i++)
                {
                    var b = gs.budgetItems[i];
                    float want = quarterExpense * (budgetSum > 0.01f ? b.amountWan / budgetSum : 0f);
                    if (Mathf.Abs(b.spentWan - want) > 0.5f) accrueRatio = false;
                }
                if (!accrueTotal || !accrueRatio) all = false;

                sb.Append($"\n　落盘：部门 {gs.budgetItems.Count} 个 · 预算合计 {budgetSum:F0} 万"
                          + $"（董事会规则 {budgetRule:F0} 万）· 部门比例同源={sameKey}");
                sb.Append($"\n　问责：季度支出 {quarterExpense:F0} 万 → Σ已执行 {spentSum:F0} 万"
                          + $"（总额一致={accrueTotal}，比例同源={accrueRatio}）");

                // ⑦ 旧存档迁移：只迁「旧默认指纹」，不碰手改额度 / 已有执行数 / 非接管局。
                //    这条钉的是 §24.5-4 的缺口 —— 不迁的话，老玩家与新开一局的部门额度是两套分母。
                var legacy = new List<BudgetItem>();
                for (int i = 0; i < FinanceEconomics.DeptShares.Length; i++)
                {
                    var d = FinanceEconomics.DeptShares[i];
                    legacy.Add(new BudgetItem
                    {
                        deptId = d.id, deptName = d.name,
                        amountWan = budgetRule * d.share, spentWan = 0f
                    });
                }
                float ratioTol = Mathf.Max(0.5f, budgetRule * 0.01f);
                var ledgerShares = ledger.DeptShares();
                int migOk = 0, migTotal = 0;

                // a) 空预算（从未打开财务页）→ 迁
                gs.budgetItems = new List<BudgetItem>();
                migTotal++;
                bool migEmpty = FinanceManager.MigrateLegacyBudget(gs);
                bool migEmptyOk = migEmpty
                                  && gs.budgetItems.Count == FinanceEconomics.DeptShares.Length
                                  && Mathf.Abs(FinanceManager.BudgetTotal(gs) - budgetRule) <= 0.05f;
                for (int i = 0; migEmptyOk && i < gs.budgetItems.Count; i++)
                {
                    // 迁移后的比例必须与账本同源（顺序与 DeptShares 一致）。
                    if (Mathf.Abs(gs.budgetItems[i].amountWan - budgetRule * ledgerShares[i]) > ratioTol)
                        migEmptyOk = false;
                }
                if (migEmptyOk) migOk++;

                // b) 旧默认指纹（8 部门 × 全行业固定份额）→ 迁
                gs.budgetItems = CloneItems(legacy);
                migTotal++;
                bool migLegacy = FinanceManager.MigrateLegacyBudget(gs);
                bool migLegacyOk = migLegacy
                                   && Mathf.Abs(gs.budgetItems[0].amountWan - budgetRule * ledgerShares[0]) <= ratioTol
                                   && Mathf.Abs(FinanceManager.BudgetTotal(gs) - budgetRule) <= 0.05f;
                if (migLegacyOk) migOk++;

                // c) 玩家改过额度 → 不迁，原样保留
                var edited = CloneItems(legacy);
                edited[0].amountWan += 50f;
                gs.budgetItems = edited;
                migTotal++;
                bool migEdited = FinanceManager.MigrateLegacyBudget(gs);
                bool migEditedOk = !migEdited
                                   && gs.budgetItems.Count == edited.Count
                                   && Mathf.Abs(gs.budgetItems[0].amountWan - edited[0].amountWan) <= 0.01f;
                if (migEditedOk) migOk++;

                // d) 已有执行数（spentWan > 0）→ 不迁，否则历史会记到错的部门
                var spentItems = CloneItems(legacy);
                spentItems[1].spentWan = 1f;
                gs.budgetItems = spentItems;
                migTotal++;
                bool migSpent = FinanceManager.MigrateLegacyBudget(gs);
                bool migSpentOk = !migSpent && Mathf.Abs(gs.budgetItems[1].spentWan - 1f) <= 0.01f;
                if (migSpentOk) migOk++;

                // e) 非接管局 → 不迁
                gs.budgetItems = new List<BudgetItem>();
                gs.takeoverMode = false;
                migTotal++;
                bool migPlain = FinanceManager.MigrateLegacyBudget(gs);
                bool migPlainOk = !migPlain && gs.budgetItems.Count == 0;
                if (migPlainOk) migOk++;
                gs.takeoverMode = true;

                // f) 现金为 0 的老档：既有总额必须**原样保留**，只换比例 —— 不能摊成 8 个 0。
                //    这条钉的是 2026-10-23 实机复现的缺陷：老档 250 万台账（旧指纹）在现金 0 时
                //    被「当前现金 × 50%」重算成 0，玩家本来看得见的预算被抹掉。
                float cashBackup = gs.cash;
                gs.cash = 0f;
                var zeroCash = new List<BudgetItem>();
                for (int i = 0; i < FinanceEconomics.DeptShares.Length; i++)
                {
                    var d = FinanceEconomics.DeptShares[i];
                    zeroCash.Add(new BudgetItem
                    {
                        deptId = d.id, deptName = d.name,
                        amountWan = 250f * d.share, spentWan = 0f
                    });
                }
                gs.budgetItems = zeroCash;
                migTotal++;
                bool migZeroCash = FinanceManager.MigrateLegacyBudget(gs);
                bool migZeroCashOk = migZeroCash
                                     && Mathf.Abs(FinanceManager.BudgetTotal(gs) - 250f) <= 0.05f;                // 比例必须换成账本口径（末项取残差，故只看前 7 项 + 总额）。
                for (int i = 0; migZeroCashOk && i < gs.budgetItems.Count - 1; i++)
                {
                    if (Mathf.Abs(gs.budgetItems[i].amountWan - 250f * ledgerShares[i]) > 0.5f)
                        migZeroCashOk = false;
                }
                // 反证：全 0 台账 + 现金 0 → 没有可摊的总量，不该写出一份全 0 台账。
                gs.budgetItems = new List<BudgetItem>();
                for (int i = 0; i < FinanceEconomics.DeptShares.Length; i++)
                    gs.budgetItems.Add(new BudgetItem
                    {
                        deptId = FinanceEconomics.DeptShares[i].id,
                        deptName = FinanceEconomics.DeptShares[i].name,
                        amountWan = 0f, spentWan = 0f
                    });
                bool migEmptyZeroCash = FinanceManager.MigrateLegacyBudget(gs);
                bool migEmptyZeroCashOk = !migEmptyZeroCash;
                if (migZeroCashOk && migEmptyZeroCashOk) migOk++;
                gs.cash = cashBackup;

                // g) 退化台账（全 0、无执行数）在现金恢复后必须能重建；
                //    有执行数或有人填过 > 0 额度时不许被覆盖。
                gs.budgetItems = new List<BudgetItem>();
                for (int i = 0; i < FinanceEconomics.DeptShares.Length; i++)
                    gs.budgetItems.Add(new BudgetItem
                    {
                        deptId = FinanceEconomics.DeptShares[i].id,
                        deptName = FinanceEconomics.DeptShares[i].name,
                        amountWan = 0f, spentWan = 0f
                    });
                finance.EnsureDefaultBudget(gs);
                bool reseedOk = Mathf.Abs(FinanceManager.BudgetTotal(gs) - budgetRule) <= 0.05f
                                && gs.budgetItems.Count == FinanceEconomics.DeptShares.Length;

                var spentKeep = CloneItems(legacy);
                spentKeep[0].spentWan = 3f;
                gs.budgetItems = spentKeep;
                finance.EnsureDefaultBudget(gs);
                bool spentKept = Mathf.Abs(gs.budgetItems[0].spentWan - 3f) <= 0.01f
                                 && Mathf.Abs(FinanceManager.BudgetTotal(gs) - budgetRule) <= 0.05f;

                var realBudget = CloneItems(legacy);
                realBudget[0].amountWan += 10f;
                gs.budgetItems = realBudget;
                float realTotal = FinanceManager.BudgetTotal(gs);
                finance.EnsureDefaultBudget(gs);
                bool realKept = Mathf.Abs(FinanceManager.BudgetTotal(gs) - realTotal) <= 0.05f;
                bool reseedGroupOk = reseedOk && spentKept && realKept;
                migTotal++;
                if (reseedGroupOk) migOk++;
                gs.budgetItems = CloneItems(legacy);

                bool migAll = migOk == migTotal;
                if (!migAll) all = false;
                sb.Append($"\n　旧档迁移：{migOk}/{migTotal}（空档迁={migEmptyOk} 旧指纹迁={migLegacyOk}"
                          + $" 手改不迁={migEditedOk} 有执行数不迁={migSpentOk} 非接管不迁={migPlainOk}"
                          + $" 零现金保总额={migZeroCashOk} 空账不写0={migEmptyZeroCashOk}"
                          + $" 退化可重建={reseedOk} 有执行数不被覆盖={spentKept} 真预算不被覆盖={realKept}）");

                // 账本逐部门读数（诊断用；也让「哪个部门分到多少」有据可查）。
                var sharesOut = ledger.DeptShares();
                var line = new StringBuilder("　部门月成本：");
                for (int i = 0; i < FinanceEconomics.DeptShares.Length; i++)
                {
                    var d = FinanceEconomics.DeptShares[i];
                    line.Append($"{d.name} {ledger.DeptMonthlyWan(d.id):F1}({sharesOut[i]:P0}) ");
                }
                sb.Append("\n").Append(line.ToString().TrimEnd());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            sb.Append($"\n　结论 {(all ? "全部符合期望 ✓" : "★有不符合期望的项")}");
            Debug.Log(sb.ToString());
            return all;
        }

        /// <summary>深拷贝预算项（回归里要分别构造「手改过」「有执行数」两种旧档）。</summary>
        static List<BudgetItem> CloneItems(List<BudgetItem> src)
        {
            var list = new List<BudgetItem>(src.Count);
            for (int i = 0; i < src.Count; i++)
            {
                list.Add(new BudgetItem
                {
                    deptId = src[i].deptId, deptName = src[i].deptName,
                    amountWan = src[i].amountWan, spentWan = src[i].spentWan
                });
            }
            return list;
        }

        /// <summary>账本自洽：8 个部门齐全、Σ部门 = Σ四段、占比归一。</summary>
        static bool CheckLedger(AirlineProfile p, out string why)        {
            why = "";
            var ledger = p.costLedger;
            if (ledger == null) { why = "无账本"; return false; }
            float deptSum = 0f;
            int missingDept = 0;
            for (int i = 0; i < FinanceEconomics.DeptShares.Length; i++)
            {
                float wan = ledger.DeptMonthlyWan(FinanceEconomics.DeptShares[i].id);
                if (ledger.TotalMonthlyWan > 0.01f && wan <= 0f) missingDept++;
                deptSum += wan;
            }
            float fourSeg = p.monthlyCost + p.monthlyFixedCost + p.monthlyDebtService + p.monthlyInsurance;
            float shareSum = 0f;
            var shares = ledger.DeptShares();
            for (int i = 0; i < shares.Length; i++) shareSum += shares[i];
            if (missingDept > 0) { why = $"{missingDept} 个部门分不到钱"; return false; }
            if (Mathf.Abs(deptSum - fourSeg) > 0.05f)
            {
                why = $"Σ部门 {deptSum:F2} ≠ Σ四段 {fourSeg:F2}";
                return false;
            }
            if (Mathf.Abs(shareSum - 1f) > 0.0001f) { why = $"占比合计 {shareSum:F4}"; return false; }
            return true;
        }
    }
}
