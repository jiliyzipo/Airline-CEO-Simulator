using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// MVP 全局游戏状态（现金、日期、董事会满意度、航线列表等）。
    /// </summary>
    public class GameState : MonoBehaviour
    {
        public static GameState Instance { get; private set; }

        [Header("初始状态")]
        public float cash = 500f;              // 万美元
        public float monthlyBurnRate = 80f;    // 万美元/月（固定开销）
        public int year = 1;
        public int quarter = 1;                // 1~4
        public float boardSatisfaction = 50f;  // 0~100
        public bool firstFlightCompleted;

        [Header("市场（Phase 2）")]
        public float fuelPricePerTon = 850f;   // 美元/吨，季度事件可波动

        [Header("航线（Phase 1）")]
        public List<RouteData> activeRoutes = new List<RouteData>();

        [Header("董事会目标（Phase 5）")]
        public List<BoardTarget> currentTargets = new List<BoardTarget>();
        public int targetsFiscalYear;
        public float yearNetProfit;          // 本财年累计净利润（万美元）
        public float yearStartCash;
        public float loadFactorPercent = 55f;
        public float safetyIndex = 82f;          // 与 safetyRating 同步，供董事会目标用
        public SafetyRating safetyRating = SafetyRating.B; // Phase 7：初始 B
        public List<SafetyEventLog> safetyEventLog = new List<SafetyEventLog>();
        public bool boardTargetsAnnounced;

        [Header("难度与终局（Phase 6）")]
        public DifficultyMode difficulty = DifficultyMode.Normal;
        public bool gameOver;
        public string gameOverReason = "";
        public int consecutiveFailedVotes;
        public int probationProtectionsUsed;

        [Header("AOG 危机（GDD 7.2）")]
        /// <summary>是否已触发过脚本化 AOG（生命周期内仅一次）。</summary>
        public bool hasAOGTriggered;
        /// <summary>玩家最终选择：0 未选 / 1 调机备援 / 2 延误维修 / 3 仅咨询过顾问（最终会落为 1 或 2）。</summary>
        public int aogChoice;
        /// <summary>是否咨询过机务顾问（选项 3）。</summary>
        public bool aogAdvisorConsulted;
        /// <summary>准点率示意（延误维修会下降）。</summary>
        public float onTimePercent = 88f;

        [Header("人事 / 部门任命（GDD 第6章 · Phase 8）")]
        /// <summary>CEO 国籍，用于文化圈适配（效率公式）。</summary>
        public string ceoNationality = "中国";

        [Header("燃油对冲与融资（GDD 7.4 · Phase 9）")]
        public List<HedgeContract> hedgeContracts = new List<HedgeContract>();
        public List<Loan> activeLoans = new List<Loan>();
        /// <summary>近若干季油价，供财务面板简易走势。</summary>
        public List<float> fuelPriceHistory = new List<float>();
        /// <summary>Debug/危机用的临时月烧钱加成；跑道恢复后清零。</summary>
        public float financeCrisisBurnBonus;
        /// <summary>是否已看过燃油对冲新手引导（财务顾问弹窗）。</summary>
        public bool hasSeenHedgeTutorial;

        [Header("机队（Phase 10）")]
        public List<Aircraft> fleet = new List<Aircraft>();
        /// <summary>下一架注册号序号（B-1001 起，值为 1,2,3…）。</summary>
        public int nextRegistrationNumber = 1;

        [Header("战略性扩张提案（GDD 7.7）")]
        /// <summary>提案流程是否已终结（接受或拒绝后为 true；暂缓不置 true）。</summary>
        public bool hasExpansionProposalTriggered;
        /// <summary>暂缓次数（最多 3）。</summary>
        public int expansionProposalDelayCount;
        public bool expansionAccepted;
        public bool expansionRejected;
        /// <summary>启动资金（万美元）。</summary>
        public float expansionProposalCashDeduction = 500f;
        /// <summary>「伟大航路」成就。</summary>
        public bool greatRouteUnlocked;
        /// <summary>本财年是否已处理过提案（暂缓后同年不再弹）。</summary>
        public int lastExpansionOfferYear;

        /// <summary>所有开通航线日利润 × 30。</summary>
        public float monthlyRevenue
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < activeRoutes.Count; i++)
                    sum += activeRoutes[i].dailyProfit * 30f;
                return sum;
            }
        }

        /// <summary>机队月维护费（万美元）。</summary>
        public float FleetMonthlyMaintenance =>
            FleetManager.Instance != null ? FleetManager.Instance.CalcMonthlyMaintenance(this) : 0f;

        /// <summary>净月烧钱（开销 + 机队维护 − 航线月收入，下限 0 便于跑道计算）。</summary>
        public float EffectiveMonthlyBurn
        {
            get
            {
                float net = monthlyBurnRate + FleetMonthlyMaintenance + Mathf.Max(0f, financeCrisisBurnBonus) - monthlyRevenue;
                return net > 0f ? net : 0f;
            }
        }

        public readonly List<string> decisionLog = new List<string>();

        public event Action OnStateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (activeRoutes == null)
                activeRoutes = new List<RouteData>();
            if (currentTargets == null)
                currentTargets = new List<BoardTarget>();
            if (safetyEventLog == null)
                safetyEventLog = new List<SafetyEventLog>();
            if (hedgeContracts == null)
                hedgeContracts = new List<HedgeContract>();
            if (activeLoans == null)
                activeLoans = new List<Loan>();
            if (fuelPriceHistory == null)
                fuelPriceHistory = new List<float>();
            if (fuelPriceHistory.Count == 0)
                fuelPriceHistory.Add(fuelPricePerTon);
            if (fleet == null)
                fleet = new List<Aircraft>();
            if (nextRegistrationNumber < 1)
                nextRegistrationNumber = 1;
            SyncSafetyIndexFromRating();
            yearStartCash = cash;
        }

        /// <summary>字母评级 → 数值指数（董事会安全目标）。</summary>
        public void SyncSafetyIndexFromRating()
        {
            safetyIndex = safetyRating switch
            {
                SafetyRating.A => 92f,
                SafetyRating.B => 82f,
                SafetyRating.C => 65f,
                SafetyRating.D => 45f,
                _ => 82f
            };
        }

        public void AddSafetyEventLog(SafetyEventLog entry)
        {
            if (safetyEventLog == null)
                safetyEventLog = new List<SafetyEventLog>();
            safetyEventLog.Add(entry);
            if (safetyEventLog.Count > 40)
                safetyEventLog.RemoveAt(0);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public string DateLabel => $"第 {year} 年 Q{quarter}";

        public void NotifyChanged() => OnStateChanged?.Invoke();

        public void AddCash(float amount)
        {
            cash += amount;
            NotifyChanged();
        }

        public void AddDecisionLog(string entry)
        {
            decisionLog.Add($"[{DateLabel}] {entry}");
            if (decisionLog.Count > 50)
                decisionLog.RemoveAt(0);
        }

        public bool HasRoute(string origin, string destination)
        {
            string o = origin?.Trim() ?? "";
            string d = destination?.Trim() ?? "";
            for (int i = 0; i < activeRoutes.Count; i++)
            {
                if (string.Equals(activeRoutes[i].origin, o, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(activeRoutes[i].destination, d, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void SetFromSave(float cashValue, int yearValue, int quarterValue, float satisfaction, bool firstFlight)
        {
            cash = cashValue;
            year = yearValue;
            quarter = quarterValue;
            boardSatisfaction = satisfaction;
            firstFlightCompleted = firstFlight;
            NotifyChanged();
        }
    }
}
