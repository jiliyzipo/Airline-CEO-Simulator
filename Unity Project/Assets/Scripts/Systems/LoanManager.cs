using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>紧急融资与还款（GDD 7.4 / 27.2 · Phase 9）。</summary>
    public class LoanManager : MonoBehaviour
    {
        public static LoanManager Instance { get; private set; }

        public int consecutiveDefaults;

        #region Lifecycle

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
            if (Instance == this) Instance = null;
        }

        #endregion

        #region Credit / Unlock

        public static bool IsFinanceUnlocked(GameState gs)
        {
            if (gs == null) return false;
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            return runway < 3f;
        }

        public static float CreditScore(GameState gs)
        {
            if (gs == null) return 40f;
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            float debt = TotalDebt(gs);
            float assets = Mathf.Max(1f, gs.cash + 300f);
            float debtRatio = debt / (assets + debt);

            // 以现金与跑道为主，避免 Debug 临时抬高 burn 后信用永久锁死在 D
            float cashFactor = Mathf.Clamp01(gs.cash / 500f) * 28f;
            float runwayFactor = Mathf.Clamp(runway / 6f, 0f, 1.5f) * 28f;
            float satFactor = Mathf.Clamp01(gs.boardSatisfaction / 100f) * 14f;
            float debtPenalty = debtRatio * 22f;
            return Mathf.Clamp(35f + cashFactor + runwayFactor + satFactor - debtPenalty, 0f, 100f);
        }

        public static CreditRating GetCreditRating(GameState gs)
        {
            float s = CreditScore(gs);
            if (s >= 80f) return CreditRating.AAA;
            if (s >= 70f) return CreditRating.AA;
            if (s >= 60f) return CreditRating.A;
            if (s >= 50f) return CreditRating.BBB;
            if (s >= 40f) return CreditRating.BB;
            if (s >= 30f) return CreditRating.B;
            if (s >= 20f) return CreditRating.C;
            return CreditRating.D;
        }

        public static string CreditLabel(CreditRating r) => r.ToString();

        public static float TotalDebt(GameState gs)
        {
            if (gs?.activeLoans == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < gs.activeLoans.Count; i++)
            {
                var L = gs.activeLoans[i];
                if (L == null || L.type == "equity") continue;
                sum += Mathf.Max(0f, L.remainingPrincipal);
            }
            return sum;
        }

        #endregion

        #region Take Loan

        public bool TryBankLoan(out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (!IsFinanceUnlocked(gs))
            {
                message = "现金跑道 ≥ 3 个月，暂未解锁紧急融资。";
                return false;
            }

            var rating = GetCreditRating(gs);
            if (rating < CreditRating.BBB)
            {
                message = $"信用评级 {CreditLabel(rating)} 不足 BBB，银行拒贷。可考虑高息过桥。";
                return false;
            }

            float monthsCash = UnityEngine.Random.Range(3f, 6f);
            float principal = Mathf.Max(80f, gs.monthlyBurnRate * monthsCash);
            float rate = UnityEngine.Random.Range(0.06f, 0.10f);
            int term = 12;
            return CreateAmortizingLoan("bank", principal, rate, term, out message);
        }

        public bool TryBridgeLoan(out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (!IsFinanceUnlocked(gs))
            {
                message = "现金跑道 ≥ 3 个月，暂未解锁紧急融资。";
                return false;
            }

            float monthsCash = UnityEngine.Random.Range(1f, 3f);
            float principal = Mathf.Max(40f, gs.monthlyBurnRate * monthsCash);
            float rate = UnityEngine.Random.Range(0.20f, 0.28f);
            int term = 6;
            return CreateAmortizingLoan("bridge", principal, rate, term, out message);
        }

        public bool TryEquityRaise(out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (!IsFinanceUnlocked(gs))
            {
                message = "现金跑道 ≥ 3 个月，暂未解锁紧急融资。";
                return false;
            }

            // 简化董事会投票：满意度 ≥ 40 视为通过
            if (gs.boardSatisfaction < 40f)
            {
                message = $"董事会否决增资（满意度 {gs.boardSatisfaction:F0} < 40）。";
                return false;
            }

            float monthsCash = UnityEngine.Random.Range(6f, 12f);
            float principal = Mathf.Max(120f, gs.monthlyBurnRate * monthsCash);
            gs.cash += principal;
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction - 8f, 0f, 100f);
            ClearCrisisBurnIfRecovered(gs);

            if (gs.activeLoans == null)
                gs.activeLoans = new List<Loan>();
            gs.activeLoans.Add(new Loan
            {
                id = "EQ-" + (gs.activeLoans.Count + 1),
                type = "equity",
                principal = principal,
                remainingPrincipal = 0f,
                interestRate = 0f,
                termMonths = 0,
                monthsRemaining = 0,
                monthlyPayment = 0f,
                createdAt = gs.DateLabel
            });

            message =
                $"股东增资通过：现金 +{principal:F0} 万（约 {monthsCash:F0} 个月开支），" +
                $"股权稀释，董事会满意度 −8 → {gs.boardSatisfaction:F0}。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>融资到账或现金回升后，清掉 Debug 危机烧钱加成。</summary>
        public static void ClearCrisisBurnIfRecovered(GameState gs)
        {
            if (gs == null || gs.financeCrisisBurnBonus <= 0f) return;
            // 先清加成再量跑道，避免自我锁死
            float bonus = gs.financeCrisisBurnBonus;
            gs.financeCrisisBurnBonus = 0f;
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            if (runway < 3.5f)
                gs.financeCrisisBurnBonus = bonus; // 仍危急则保留
        }

        private bool CreateAmortizingLoan(string type, float principal, float annualRate, int termMonths, out string message)
        {
            var gs = GameState.Instance;
            if (gs.activeLoans == null)
                gs.activeLoans = new List<Loan>();

            // 等额本息近似：月供 = 本金/期数 + 本金×月利率
            float monthlyRate = annualRate / 12f;
            float monthlyPay = principal / termMonths + principal * monthlyRate;

            var loan = new Loan
            {
                id = type.ToUpperInvariant() + "-" + (gs.activeLoans.Count + 1),
                type = type,
                principal = principal,
                remainingPrincipal = principal,
                interestRate = annualRate,
                termMonths = termMonths,
                monthsRemaining = termMonths,
                monthlyPayment = monthlyPay,
                createdAt = gs.DateLabel
            };
            gs.activeLoans.Add(loan);
            gs.cash += principal;
            ClearCrisisBurnIfRecovered(gs);

            string typeName = type == "bank" ? "银行贷款" : "高息过桥贷款";
            message =
                $"{typeName}到账 +{principal:F0} 万 · 年利率 {annualRate * 100:F1}% · " +
                $"{termMonths} 个月 · 月供约 {monthlyPay:F1} 万。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region Repay

        /// <summary>季度还款（扣 3 个月月供）。不足则违约：满意度 −10。</summary>
        public string ProcessQuarterRepayments()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.activeLoans == null || gs.activeLoans.Count == 0)
                return "";

            float due = 0f;
            for (int i = 0; i < gs.activeLoans.Count; i++)
            {
                var L = gs.activeLoans[i];
                if (L == null || L.type == "equity") continue;
                if (L.monthsRemaining <= 0) continue;
                int months = Mathf.Min(3, L.monthsRemaining);
                due += L.monthlyPayment * months;
            }

            if (due <= 0.01f)
            {
                CleanupEquityOnly();
                return "";
            }

            var sb = new StringBuilder();
            if (gs.cash + 0.01f < due)
            {
                consecutiveDefaults++;
                gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction - 10f, 0f, 100f);
                sb.AppendLine(
                    $"⚠ 融资违约：应还 {due:F0} 万，现金不足。董事会满意度 −10 → {gs.boardSatisfaction:F0}。" +
                    $"（连续违约 {consecutiveDefaults} 次）");
                gs.AddDecisionLog("财务：" + sb.ToString().Trim());

                if (consecutiveDefaults >= 2)
                {
                    sb.AppendLine("连续两季无法还款，进入破产预警。");
                    // 与现有破产提示对齐：现金逼近耗尽
                    if (gs.cash > 0f)
                        gs.cash = Mathf.Min(gs.cash, 1f);
                }

                gs.NotifyChanged();
                return sb.ToString().TrimEnd();
            }

            consecutiveDefaults = 0;
            gs.cash -= due;
            gs.yearNetProfit -= due;

            var keep = new List<Loan>();
            for (int i = 0; i < gs.activeLoans.Count; i++)
            {
                var L = gs.activeLoans[i];
                if (L == null) continue;
                if (L.type == "equity")
                {
                    // 增资记录仅作历史展示一期后可清
                    continue;
                }

                int months = Mathf.Min(3, L.monthsRemaining);
                float pay = L.monthlyPayment * months;
                float interestPart = L.remainingPrincipal * (L.interestRate / 12f) * months;
                float principalPart = Mathf.Max(0f, pay - interestPart);
                L.remainingPrincipal = Mathf.Max(0f, L.remainingPrincipal - principalPart);
                L.monthsRemaining -= months;
                if (L.monthsRemaining > 0 && L.remainingPrincipal > 0.5f)
                    keep.Add(L);
                else
                    sb.AppendLine($"贷款 {L.id} 已还清。");
            }

            gs.activeLoans = keep;
            sb.Insert(0, $"本季偿还融资本息 {due:F1} 万。\n");
            gs.AddDecisionLog("财务：本季偿还融资本息 " + due.ToString("F1") + " 万。");
            gs.NotifyChanged();
            return sb.ToString().TrimEnd();
        }

        private void CleanupEquityOnly()
        {
            var gs = GameState.Instance;
            if (gs?.activeLoans == null) return;
            var keep = new List<Loan>();
            for (int i = 0; i < gs.activeLoans.Count; i++)
            {
                var L = gs.activeLoans[i];
                if (L != null && L.type != "equity" && L.monthsRemaining > 0)
                    keep.Add(L);
            }
            gs.activeLoans = keep;
        }

        #endregion

        #region Save helpers

        public string LoansToJson()
        {
            var gs = GameState.Instance;
            var blob = new LoanListSave
            {
                items = gs?.activeLoans != null ? gs.activeLoans.ToArray() : System.Array.Empty<Loan>()
            };
            return JsonUtility.ToJson(blob);
        }

        public void LoansFromJson(string json)
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            gs.activeLoans = new List<Loan>();
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var blob = JsonUtility.FromJson<LoanListSave>(json);
                if (blob?.items != null)
                    gs.activeLoans = new List<Loan>(blob.items);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[LoanManager] 读档失败: " + e.Message);
            }
        }

        #endregion
    }
}
