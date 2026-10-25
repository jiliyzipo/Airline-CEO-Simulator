using System;
using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// K17 / D4：接管成本的**部门账本**——把四段式成本（总部/地面、机队/人力、还本付息、保费）
    /// 逐项摊到 8 个真实部门，作为「接管时继承什么」的唯一依据。
    ///
    /// 为什么要有这一层（任务书 §18 D4）：
    /// 预览算账一直是**四段式**（`monthlyCost` + `monthlyFixedCost` + `monthlyDebtService` +
    /// `monthlyInsurance`），而玩家在财务页看到的是**逐部门预算**（`GameState.budgetItems`，
    /// 8 个部门）。两套账此前各说各话：
    /// ① 预览的四段式没有任何部门归属，玩家无法回答「钱花在哪个部门」；
    /// ② 接管的部门预算由 `FinanceManager.EnsureDefaultBudget` 按「现金 × 50% × 固定份额」凭空生成，
    ///    与该航司真实的成本结构（老机队维修重、租赁多、贷款多）完全无关；
    /// ③ 季度超支问责按**固定份额**（运营 20% / 机务 18% …）分摊实际支出，
    ///    与继承下来的部门额度不是同一把尺子 —— 超支判定从一开始就是错位的。
    ///
    /// 本类**不改总量**，只改粒度：
    /// - 四段式的金额原样保留（进位/求和顺序与旧实现逐位一致），故 K10 的 8/6/4/3 跑道断言不受影响；
    /// - 每个部门的月成本 = 四段式按「谁在花钱」摊下来的份额（见 <see cref="HqAllocation"/> 等）；
    /// - 档位预算总量仍按董事会的规则（`FinanceEconomics.RecommendedBudget(cash)` = 现金的 50%），
    ///   **只是部门之间的比例改为继承该航司的成本结构**，不再用全行业同一套固定份额。
    ///
    /// 为什么总量不改成「12 × 月成本」：那会让 ★5 的年度预算（约 2.5 亿）远远超过现金，
    /// 董事会投票（`EstimateBoardVotes` 按 total / cash 分档）必定只剩 2 票，
    /// 接管当天「提交预算」永远失败 —— 这是把一个口径问题换成一个可玩性问题。
    /// 继承的是**结构**，不是把董事会规则也一起改掉。
    /// </summary>
    [Serializable]
    public class TakeoverCostLedger
    {
        public const string SegmentHq = "hq";               // 总部 / 地面固定开销
        public const string SegmentFleet = "fleet";         // 机队维护 + 月租 + 存量人力
        public const string SegmentDebt = "debt";           // 存量贷款还本付息（月均）
        public const string SegmentInsurance = "insurance"; // 全险保费（月均）

        /// <summary>一段成本摊到某部门的金额（万美元/月）。保留 segment 便于逐段对账。</summary>
        [Serializable]
        public class Line
        {
            public string segment;
            public string deptId;
            public string deptName;
            public float monthlyWan;
        }

        public float hqMonthlyWan;
        public float fleetMonthlyWan;
        public float debtMonthlyWan;
        public float insuranceMonthlyWan;

        public List<Line> lines = new List<Line>();

        /// <summary>四段式合计（万美元/月）。求和顺序与 <c>TakeoverWorldBuilder</c> 旧实现完全一致。</summary>
        public float TotalMonthlyWan
            => hqMonthlyWan + fleetMonthlyWan + debtMonthlyWan + insuranceMonthlyWan;

        /// <summary>某部门的月成本（万美元/月）。未知部门返回 0。</summary>
        public float DeptMonthlyWan(string deptId)
        {
            if (string.IsNullOrEmpty(deptId) || lines == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l != null && l.deptId == deptId) sum += l.monthlyWan;
            }
            return sum;
        }

        /// <summary>
        /// 部门占比（0~1，Σ=1）。四段式合计 ≤ 0 时退回 <see cref="FinanceEconomics.DeptShares"/> 的固定份额，
        /// 因为「按比例摊一个 0 元的总量」没有定义，而部门列表不能因此变成 8 个 0。
        /// </summary>
        public float[] DeptShares()
        {
            var shares = new float[FinanceEconomics.DeptShares.Length];
            float total = TotalMonthlyWan;
            if (total <= 0.01f)
            {
                for (int i = 0; i < shares.Length; i++) shares[i] = FinanceEconomics.DeptShares[i].share;
                return shares;
            }
            for (int i = 0; i < shares.Length; i++)
            {
                float wan = DeptMonthlyWan(FinanceEconomics.DeptShares[i].id);
                shares[i] = wan > 0f ? wan / total : 0f;
            }
            return shares;
        }

        /// <summary>
        /// 按继承下来的部门比例，把**董事会批准的年度预算总量**摊成 8 个部门的额度。
        ///
        /// 总量由调用方给（接管 = <see cref="FinanceEconomics.RecommendedBudget"/>(现金)），
        /// 本方法只负责粒度；最后一项取残差，保证 Σ 恰好等于入参（不因浮点分配多出/少掉 1 分钱）。
        /// </summary>
        public List<BudgetItem> ToBudgetItems(float totalAnnualWan)
        {
            var items = new List<BudgetItem>();
            var shares = DeptShares();
            float total = Math.Max(0f, totalAnnualWan);
            float remaining = total;
            for (int i = 0; i < FinanceEconomics.DeptShares.Length; i++)
            {
                var d = FinanceEconomics.DeptShares[i];
                bool last = i == FinanceEconomics.DeptShares.Length - 1;
                // 末项取残差，保证 Σ 恰好等于董事会批准的总额；若前面因浮点误差多摊了，
                // 末项夹到 0，不写负额度（负预算是玩家看不懂的状态）。
                float amount = last ? Math.Max(0f, remaining) : total * shares[i];
                remaining -= amount;
                items.Add(new BudgetItem
                {
                    deptId = d.id,
                    deptName = d.name,
                    amountWan = amount,
                    spentWan = 0f
                });
            }
            return items;
        }

        // —— 四段式 → 部门的归属表 ——
        //
        // 归属原则：谁对这笔钱负责，就记在谁头上；一笔钱只记一次（每段权重合计 1.00）。
        // 不把「维护费」塞进财务部、也不把「贷款利息」摊给运营部 —— 那样部门账能对上总数，
        // 却答不出「该找谁降本」，等于把粒度问题换了个壳。

        /// <summary>总部/地面：运行控制与地面站（运营）、销售与渠道（市场）、规划（战略）、
        /// 资金与税（财务）、治理（董事会）、安全监督（安全质量）。</summary>
        static readonly (string id, float share)[] HqAllocation =
        {
            ("n13_运营部", 0.45f),
            ("n14_市场销售部", 0.10f),
            ("n16_战略发展部", 0.10f),
            ("n15_财务部", 0.10f),
            ("n20_董事会", 0.15f),
            ("n19_安全质量部", 0.10f)
        };

        /// <summary>机队维护/月租/存量人力：工程（机务）、在册人力（人力）、排班与运行（运营）。</summary>
        static readonly (string id, float share)[] FleetAllocation =
        {
            ("n17_机务工程部", 0.55f),
            ("n18_人力资源部", 0.25f),
            ("n13_运营部", 0.20f)
        };

        /// <summary>还本付息：资金成本，整笔记财务部。</summary>
        static readonly (string id, float share)[] DebtAllocation =
        {
            ("n15_财务部", 1.00f)
        };

        /// <summary>保费：风险与合规，整笔记安全质量部。</summary>
        static readonly (string id, float share)[] InsuranceAllocation =
        {
            ("n19_安全质量部", 1.00f)
        };

        /// <summary>
        /// 由四段式金额生成部门账本。入参与 <c>AirlineProfile</c> 的四个字段同源，
        /// 合计因此与 <c>monthlyNetBurn</c> 的分母逐位一致（见 <see cref="TotalMonthlyWan"/>）。
        /// </summary>
        public static TakeoverCostLedger Build(float hqWan, float fleetWan, float debtWan, float insuranceWan)
        {
            var ledger = new TakeoverCostLedger
            {
                hqMonthlyWan = hqWan,
                fleetMonthlyWan = fleetWan,
                debtMonthlyWan = debtWan,
                insuranceMonthlyWan = insuranceWan
            };
            if (ledger.lines == null) ledger.lines = new List<Line>();
            AddSegment(ledger.lines, SegmentHq, HqAllocation, hqWan);
            AddSegment(ledger.lines, SegmentFleet, FleetAllocation, fleetWan);
            AddSegment(ledger.lines, SegmentDebt, DebtAllocation, debtWan);
            AddSegment(ledger.lines, SegmentInsurance, InsuranceAllocation, insuranceWan);
            return ledger;
        }

        static void AddSegment(List<Line> lines, string segment, (string id, float share)[] allocation, float totalWan)
        {
            float remaining = totalWan;
            for (int i = 0; i < allocation.Length; i++)
            {
                bool last = i == allocation.Length - 1;
                // 末项取残差：各段金额之和恰好等于该段总额（不因 0.55/0.25/0.20 的浮点乘法丢分）。
                float amount = last ? remaining : totalWan * allocation[i].share;
                remaining -= amount;
                lines.Add(new Line
                {
                    segment = segment,
                    deptId = allocation[i].id,
                    deptName = DeptNameOf(allocation[i].id),
                    monthlyWan = amount
                });
            }
        }

        /// <summary>部门 id → 中文名（与 <see cref="FinanceEconomics.DeptShares"/> 同表，避免两处写死）。</summary>
        public static string DeptNameOf(string deptId)
        {
            for (int i = 0; i < FinanceEconomics.DeptShares.Length; i++)
                if (FinanceEconomics.DeptShares[i].id == deptId) return FinanceEconomics.DeptShares[i].name;
            return deptId ?? "";
        }
    }
}
