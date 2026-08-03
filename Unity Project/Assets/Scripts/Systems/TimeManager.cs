using System;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// Phase 2：财年心跳——按季度推进，结算收入/烧钱，并可触发随机事件。
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance { get; private set; }

        public event Action<QuarterResult> OnQuarterAdvanced;
        public event Action<YearEndResult> OnYearEnded;

        [Header("随机事件")]
        [Range(0f, 1f)] public float oilEventChance = 0.55f;
        public float oilShockMin = -0.12f; // -12%
        public float oilShockMax = 0.18f;  // +18%

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>推进一个季度并结算。成功时更新 GameState。</summary>
        public QuarterResult AdvanceQuarter()
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                return new QuarterResult
                {
                    success = false,
                    message = "游戏状态未就绪，无法推进季度。"
                };
            }

            if (gs.gameOver)
            {
                return new QuarterResult
                {
                    success = false,
                    message = "游戏已结束：" + (gs.gameOverReason ?? "董事会已解雇 CEO。")
                };
            }

            // GDD 7.2 / 7.7：确保 Manager；触发标记在推进后写入 result
            EnsureAOGManager();
            EnsureExpansionManager();

            int fromYear = gs.year;
            int fromQuarter = gs.quarter;

            // 2.1 / 2.2 季度收入与支出（含机队月维护）
            float quarterIncome = gs.monthlyRevenue * 3f;
            float quarterExpense = (gs.monthlyBurnRate + gs.FleetMonthlyMaintenance) * 3f;
            float net = quarterIncome - quarterExpense;

            // 2.3 更新现金 + 本财年净利润累计
            gs.cash += net;
            gs.yearNetProfit += net;

            if (BoardManager.Instance != null)
                BoardManager.UpdateSimulatedKpis(gs);

            // Phase 9：常规油价 ±5%，再叠加偶发大波动；随后对冲结算 / 融资还款
            ApplyMildFuelFluctuation(gs);
            string eventText = TryOilEvent(gs);
            if (HedgeManager.Instance != null)
                HedgeManager.Instance.PushPriceHistory(gs.fuelPricePerTon);

            string hedgeText = "";
            if (HedgeManager.Instance != null)
                hedgeText = HedgeManager.Instance.SettleQuarter(gs.fuelPricePerTon);
            if (!string.IsNullOrEmpty(hedgeText))
                eventText = string.IsNullOrEmpty(eventText) ? hedgeText : eventText + "\n" + hedgeText;

            string loanText = "";
            if (LoanManager.Instance != null)
                loanText = LoanManager.Instance.ProcessQuarterRepayments();
            if (!string.IsNullOrEmpty(loanText))
                eventText = string.IsNullOrEmpty(eventText) ? loanText : eventText + "\n" + loanText;

            LoanManager.ClearCrisisBurnIfRecovered(gs);

            string safetyText = "";
            if (SafetyEventManager.Instance != null)
                safetyText = SafetyEventManager.Instance.TryRollQuarterEvent(gs);
            else
            {
                var go = new GameObject("SafetyEventManager");
                go.AddComponent<SafetyEventManager>();
                safetyText = SafetyEventManager.Instance.TryRollQuarterEvent(gs);
            }

            if (!string.IsNullOrEmpty(safetyText))
                eventText = string.IsNullOrEmpty(eventText) ? safetyText : eventText + "\n" + safetyText;

            // 2.4 推进季度
            bool yearEnded = false;
            gs.quarter += 1;
            if (gs.quarter > 4)
            {
                gs.quarter = 1;
                gs.year += 1;
                yearEnded = true;
            }

            string summary =
                $"季度结算：收入 {quarterIncome:F0} 万 − 支出 {quarterExpense:F0} 万 = 净额 {(net >= 0 ? "+" : "")}{net:F0} 万。" +
                $" 时间 {Label(fromYear, fromQuarter)} → {gs.DateLabel}。";

            gs.AddDecisionLog(summary + (string.IsNullOrEmpty(eventText) ? "" : " " + eventText));

            var result = new QuarterResult
            {
                success = true,
                fromYear = fromYear,
                fromQuarter = fromQuarter,
                toYear = gs.year,
                toQuarter = gs.quarter,
                quarterIncome = quarterIncome,
                quarterExpense = quarterExpense,
                netChange = net,
                cashAfter = gs.cash,
                eventText = eventText,
                yearEnded = yearEnded,
                message = summary + (string.IsNullOrEmpty(eventText) ? "" : "\n" + eventText)
            };

            // 破产检测（现金为负）
            if (gs.cash < 0f)
            {
                float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
                result.isBankruptWarning = true;
                result.message +=
                    $"\n⚠ 破产警告：现金已为负（{gs.cash:F0} 万）！" +
                    $"{CashRunnelCalculator.FormatPlayerLabel(runway)}，{CashRunnelCalculator.FormatWarningPlain(runway)}。";
            }

            OnQuarterAdvanced?.Invoke(result);

            // Phase 8：季末评估部门 KPI（达标则下季效率 +5%）
            if (DepartmentManager.Instance != null)
                DepartmentManager.Instance.OnQuarterEndEvaluateKpi();

            // 2.5 必须先 OnYearEnd（用旧目标结算），再 NotifyChanged，避免中途重生目标、清零 yearNetProfit
            if (yearEnded)
            {
                var yearResult = OnYearEnd(gs, fromYear);
                result.yearEnd = yearResult;
                result.toYear = gs.year;
                result.toQuarter = gs.quarter;
                result.message += "\n" + yearResult.message;
                OnYearEnded?.Invoke(yearResult);

                // Phase 8：财年末忠诚度结算（可能触发离职）
                if (DepartmentManager.Instance != null)
                    DepartmentManager.Instance.OnYearEndLoyalty();

                // Phase 10：机龄 +1
                if (FleetManager.Instance != null)
                    FleetManager.Instance.OnYearEndAgeFleet();
            }

            gs.NotifyChanged();

            // GDD 7.2：进入第 1 年 Q2/Q3 后标记待触发 AOG（Q2 未触发则 Q3 强制）
            if (AOGManager.Instance != null && AOGManager.Instance.ShouldTriggerAOG())
            {
                result.pendingAOG = true;
                result.message += "\n⚠ AOG 危机待处置（飞机突发故障）。";
            }

            // GDD 7.7：第 4 财年 Q1（或 Q2 强制）战略性扩张提案
            if (ExpansionProposalManager.Instance != null && ExpansionProposalManager.Instance.ShouldTriggerProposal())
            {
                result.pendingExpansionProposal = true;
                result.message += "\n🌏 战略发展部提交了扩张提案。";
            }

            // 现金流吃紧：跑道 < 3 月 → 通知 UI/AI 推送 + 解锁紧急融资
            float months = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            if (months < 3f && gs.cash >= 0f && !gs.gameOver)
            {
                result.needsFinanceWarning = true;
                result.financeWarning =
                    $"财务顾问：账上钱不多了（{CashRunnelCalculator.FormatPlayerLabel(months)}），" +
                    "建议打开「财务」使用紧急融资，或削减成本、暂缓扩张。";
            }

            return result;
        }

        private static void ApplyMildFuelFluctuation(GameState gs)
        {
            if (gs == null) return;
            float fluctuation = UnityEngine.Random.Range(-0.05f, 0.05f);
            gs.fuelPricePerTon *= 1f + fluctuation;
            gs.fuelPricePerTon = Mathf.Max(500f, gs.fuelPricePerTon);
        }

        /// <summary>财年末：先用本财年目标快照结算，再生成新年目标。</summary>
        public YearEndResult OnYearEnd(GameState gs, int finishedYear)
        {
            if (BoardManager.Instance != null)
                BoardManager.Instance.RefreshTargetProgress();

            // 快照，避免结算过程中列表被替换
            var snapshot = gs.currentTargets != null
                ? new System.Collections.Generic.List<BoardTarget>(gs.currentTargets)
                : new System.Collections.Generic.List<BoardTarget>();

            var eval = YearEndEvaluator.Evaluate(
                snapshot,
                finishedYear,
                gs != null ? gs.difficulty : DifficultyMode.Normal);

            float delta = YearEndEvaluator.SatisfactionDelta(eval);
            if (gs.cash < 0f) delta -= 5f;

            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + delta, 0f, 100f);

            if (BoardManager.Instance != null)
                BoardManager.Instance.ApplySatisfactionDeltaToMembers(delta * 0.5f);

            if (eval.outcome == VoteOutcome.Fired)
            {
                gs.consecutiveFailedVotes++;
                gs.gameOver = true;
                gs.gameOverReason = eval.body;
                gs.year = finishedYear;
                gs.quarter = 4;
            }
            else
            {
                if (eval.outcome == VoteOutcome.ProbationContinue)
                    gs.probationProtectionsUsed++;
                else
                    gs.consecutiveFailedVotes = 0;

                if (BoardManager.Instance != null)
                    BoardManager.Instance.OnNewFiscalYearStarted(gs.year);
            }

            string msg =
                $"年度结算（第 {finishedYear} 年）：完成率 {eval.completionRate:F0}%（{eval.targetsCompleted}/{eval.targetsTotal}）。" +
                $"【{eval.title}】{eval.body}";

            gs.AddDecisionLog(msg);

            return new YearEndResult
            {
                finishedYear = finishedYear,
                satisfactionDelta = delta,
                satisfactionAfter = gs.boardSatisfaction,
                targetsTotal = eval.targetsTotal,
                targetsCompleted = eval.targetsCompleted,
                completionRate = eval.completionRate,
                reappointmentThreshold = eval.reappointmentThreshold,
                message = msg,
                showSettlementPopup = true,
                voteOutcome = eval.outcome,
                voteTitle = eval.title,
                voteBody = eval.body,
                gameOver = eval.outcome == VoteOutcome.Fired,
                shouldResetTargets = eval.outcome != VoteOutcome.Fired
            };
        }

        /// <summary>季度净利计入本财年累计（在推进前由外部调用，或在本方法内计入）。</summary>
        public void AccrueYearNetProfit(GameState gs, float quarterNet)
        {
            if (gs == null) return;
            gs.yearNetProfit += quarterNet;
        }

        private string TryOilEvent(GameState gs)
        {
            if (UnityEngine.Random.value > oilEventChance)
                return "";

            float shock = UnityEngine.Random.Range(oilShockMin, oilShockMax);
            float before = gs.fuelPricePerTon;
            gs.fuelPricePerTon = Mathf.Max(400f, before * (1f + shock));
            string dir = shock >= 0 ? "上涨" : "下跌";
            return $"事件：航油价格{dir} {Mathf.Abs(shock) * 100f:F0}%（{before:F0}→{gs.fuelPricePerTon:F0} 美元/吨）。";
        }

        private static void EnsureAOGManager()
        {
            if (AOGManager.Instance != null) return;
            var go = new GameObject("AOGManager");
            go.AddComponent<AOGManager>();
        }

        private static void EnsureExpansionManager()
        {
            if (ExpansionProposalManager.Instance != null) return;
            var go = new GameObject("ExpansionProposalManager");
            go.AddComponent<ExpansionProposalManager>();
        }

        private static string Label(int y, int q) => $"第 {y} 年 Q{q}";
    }

    [Serializable]
    public class QuarterResult
    {
        public bool success;
        public string message;
        public int fromYear;
        public int fromQuarter;
        public int toYear;
        public int toQuarter;
        public float quarterIncome;
        public float quarterExpense;
        public float netChange;
        public float cashAfter;
        public string eventText;
        public bool yearEnded;
        public bool isBankruptWarning;
        public bool needsFinanceWarning;
        public string financeWarning;
        public YearEndResult yearEnd;
        /// <summary>GDD 7.2：本季推进后应弹出 AOG 危机。</summary>
        public bool pendingAOG;
        /// <summary>GDD 7.7：本季推进后应弹出战略性扩张提案。</summary>
        public bool pendingExpansionProposal;
    }

    [Serializable]
    public class YearEndResult
    {
        public int finishedYear;
        public float satisfactionDelta;
        public float satisfactionAfter;
        public string message;
        public int targetsTotal;
        public int targetsCompleted;
        public float completionRate;
        public float reappointmentThreshold;
        public bool showSettlementPopup;

        // Phase 6 续聘投票
        public VoteOutcome voteOutcome;
        public string voteTitle;
        public string voteBody;
        public bool gameOver;
        public bool shouldResetTargets;
    }
}