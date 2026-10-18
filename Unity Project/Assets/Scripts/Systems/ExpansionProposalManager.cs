using System;
using AirlineCEO.Data;
using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// GDD 7.7 · 战略性扩张提案（第 4 财年 Q1，暂缓可延至下年，最多 3 次）。
    /// </summary>
    public class ExpansionProposalManager : MonoBehaviour
    {
        public static ExpansionProposalManager Instance { get; private set; }

        public const string Origin = "北京";
        public const string Destination = "纽约";
        public const float DailyProfit = 8.5f;
        public const float DefaultStartupCost = 500f;
        public const int MaxDelays = 3;
        public const float DelaySatisfactionPenalty = 3f;
        public const float RejectSatisfactionPenalty = 8f;
        public const float AcceptSatisfactionBonus = 10f;

        [SerializeField] private ExpansionProposalUI ui;

        private bool _isShowing;

        public event Action<int> OnProposalResolved;

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
            if (Instance == this)
                Instance = null;
        }

        public void BindUI(ExpansionProposalUI proposalUi) => ui = proposalUi;

        #endregion

        #region Trigger

        /// <summary>
        /// 第 4 财年起、首航完成、未接受/未拒绝；
        /// Q1 触发，若本财年 Q1 错过则 Q2 强制；暂缓后次年 Q1 再弹（最多暂缓 3 次）。
        /// </summary>
        public bool ShouldTriggerProposal()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.gameOver) return false;
            if (gs.expansionAccepted || gs.expansionRejected) return false;
            if (gs.hasExpansionProposalTriggered) return false;
            if (!gs.firstFlightCompleted) return false;
            if (gs.year < 4) return false;
            if (gs.lastExpansionOfferYear == gs.year) return false; // 本年已暂缓过

            if (gs.quarter == 1) return true;
            // Q2 强制：本财年尚未出示过
            if (gs.quarter == 2 && gs.lastExpansionOfferYear < gs.year) return true;
            return false;
        }

        public void TriggerProposal()
        {
            if (!ShouldTriggerProposal()) return;
            if (_isShowing) return;

            _isShowing = true;
            GameSpeed.Pause();

            if (ui == null)
                ui = FindFirstObjectByType<ExpansionProposalUI>(FindObjectsInactive.Include);

            if (ui != null)
            {
                bool allowDelay = GameState.Instance != null
                    && GameState.Instance.expansionProposalDelayCount < MaxDelays;
                ui.ShowProposal(allowDelay);
                Debug.Log("[ExpansionProposal] 已弹出战略性扩张提案。");
            }
            else
            {
                Debug.LogError("[ExpansionProposal] UI 未找到。");
                ForceResumeTime();
                _isShowing = false;
            }
        }

        public bool TryTriggerProposal()
        {
            if (!ShouldTriggerProposal()) return false;
            TriggerProposal();
            return true;
        }

        #endregion

        #region Choices

        /// <summary>1=接受 2=暂缓 3=拒绝</summary>
        public void OnProposalChoiceMade(int choice)
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                ForceResumeTime();
                return;
            }

            switch (choice)
            {
                case 1:
                    if (!ApplyExpansion(gs))
                        return; // 现金不足，保持弹窗
                    FinishAndClose(gs, 1);
                    break;
                case 2:
                    ApplyDelay(gs);
                    FinishAndClose(gs, 2);
                    break;
                case 3:
                    ApplyReject(gs);
                    FinishAndClose(gs, 3);
                    break;
            }
        }

        public bool ApplyExpansion(GameState gs)
        {
            float cost = gs.expansionProposalCashDeduction > 0f
                ? gs.expansionProposalCashDeduction
                : DefaultStartupCost;

            if (gs.cash < cost)
            {
                if (ui != null)
                    ui.ShowCashShortage(cost, gs.cash);
                Debug.LogWarning($"[ExpansionProposal] 现金不足：需 {cost:F0}，现有 {gs.cash:F0}");
                return false;
            }

            // Phase 15：扩张前检查机队是否有可用飞机
            if (FleetManager.Instance != null)
            {
                FleetManager.Instance.EnsureStarterFleet();
                if (!FleetManager.Instance.HasSpareAircraftForNewRoute(gs))
                {
                    Debug.LogWarning("[ExpansionProposal] 机队无可用飞机，扩张被阻止。");
                    return false;
                }
            }

            gs.cash -= cost;
            gs.yearNetProfit -= cost;
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + AcceptSatisfactionBonus, 0f, 100f);
            gs.expansionAccepted = true;
            gs.hasExpansionProposalTriggered = true;
            gs.greatRouteUnlocked = true;

            if (!gs.HasRoute(Origin, Destination))
            {
                gs.activeRoutes.Add(new RouteData
                {
                    routeId = $"{Origin}-{Destination}-great",
                    origin = Origin,
                    destination = Destination,
                    dailyProfit = DailyProfit,
                    openedDate = gs.DateLabel,
                    score = 78,
                    rating = "绿"
                });
            }

            gs.AddDecisionLog(
                $"战略性扩张【接受】开通 {Origin}-{Destination}，启动资金 -{cost:F0} 万，" +
                $"日利润约 {DailyProfit:F1} 万，满意度 +{AcceptSatisfactionBonus:F0}。解锁「伟大航路」。");
            return true;
        }

        public void ApplyDelay(GameState gs)
        {
            gs.expansionProposalDelayCount = Mathf.Min(gs.expansionProposalDelayCount + 1, MaxDelays);
            gs.lastExpansionOfferYear = gs.year;
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction - DelaySatisfactionPenalty, 0f, 100f);
            // 暂缓：不置 hasExpansionProposalTriggered，以便下年再弹
            gs.AddDecisionLog(
                $"战略性扩张【暂缓】第 {gs.expansionProposalDelayCount}/{MaxDelays} 次。" +
                $"满意度 -{DelaySatisfactionPenalty:F0}。将在下一年 Q1 再次提案。");
        }

        public void ApplyReject(GameState gs)
        {
            gs.expansionRejected = true;
            gs.hasExpansionProposalTriggered = true;
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction - RejectSatisfactionPenalty, 0f, 100f);
            gs.AddDecisionLog(
                $"战略性扩张【拒绝】专注现有市场。满意度 -{RejectSatisfactionPenalty:F0}。不再提案。");
        }

        private void FinishAndClose(GameState gs, int choice)
        {
            try
            {
                if (ui != null)
                    ui.Hide();
            }
            finally
            {
                ForceResumeTime();
                _isShowing = false;
                gs.NotifyChanged();
                if (SaveManager.Instance != null)
                    SaveManager.Instance.Save();
                OnProposalResolved?.Invoke(choice);
                Debug.Log(
                    $"[ExpansionProposal] 已处理 choice={choice}，accepted={gs.expansionAccepted}，" +
                    $"rejected={gs.expansionRejected}，delay={gs.expansionProposalDelayCount}，timeScale={Time.timeScale}");
            }
        }

        private static void ForceResumeTime()
        {
            GameSpeed.Resume();
        }

        #endregion

#if UNITY_EDITOR
        public void DebugForceShow()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            gs.firstFlightCompleted = true;
            gs.year = Mathf.Max(gs.year, 4);
            if (gs.quarter < 1) gs.quarter = 1;
            gs.expansionAccepted = false;
            gs.expansionRejected = false;
            gs.hasExpansionProposalTriggered = false;
            gs.lastExpansionOfferYear = 0;
            TriggerProposal();
        }
#endif
    }
}
