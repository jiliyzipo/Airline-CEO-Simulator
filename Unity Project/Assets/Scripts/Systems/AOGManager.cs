using System;
using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// GDD 7.2 · 脚本化 AOG 危机（第 1 财年 Q2/Q3，首航后仅触发一次）。
    /// </summary>
    public class AOGManager : MonoBehaviour
    {
        public static AOGManager Instance { get; private set; }

        public const float CostFerry = 20f;       // 调机备援（万）
        public const float CostRepair = 5f;       // 维修费
        public const float CostDelayComp = 10f;   // 延误赔偿
        public const float CostDelayTotal = CostRepair + CostDelayComp; // 15
        public const float DelaySatisfactionPenalty = 3f;
        public const float DelayOnTimePenalty = 8f;

        [SerializeField] private AOGUI ui;

        private float _timeScaleBeforePause = 1f;
        private bool _isShowing;

        /// <summary>危机结束后（玩家选定 1 或 2）。</summary>
        public event Action<int> OnAOGResolved;

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

        public void BindUI(AOGUI aogUi) => ui = aogUi;

        #endregion

        #region Trigger rules

        /// <summary>
        /// 检查是否应触发 AOG。
        /// 前提：首航完成（firstFlightCompleted）、未触发过、第 1 财年、季度为 Q2 或 Q3。
        /// Q2 开始时检查；若因首航未完成等原因未触发，则 Q3 开始时强制触发。
        /// </summary>
        public bool ShouldTriggerAOG()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.gameOver) return false;
            if (gs.hasAOGTriggered) return false;
            if (!gs.firstFlightCompleted) return false; // 规格 firstFlightDone
            if (gs.year != 1) return false;
            if (gs.quarter != 2 && gs.quarter != 3) return false;
            return true;
        }

        /// <summary>暂停游戏并显示危机弹窗。</summary>
        public void TriggerAOG()
        {
            if (!ShouldTriggerAOG()) return;
            if (_isShowing) return;

            _isShowing = true;
            _timeScaleBeforePause = Time.timeScale <= 0f ? 1f : Time.timeScale;
            Time.timeScale = 0f;

            if (ui == null)
                ui = FindFirstObjectByType<AOGUI>(FindObjectsInactive.Include);

            if (ui != null)
            {
                ui.ShowCrisis();
                Debug.Log("[AOGManager] 已触发 AOG 危机弹窗。");
            }
            else
            {
                Debug.LogError("[AOGManager] AOGUI 未找到，无法显示危机。已恢复时间。");
                ResumeTime();
                _isShowing = false;
            }
        }

        /// <summary>供 TimeManager / Bootstrap：条件满足则触发。</summary>
        public bool TryTriggerAOG()
        {
            if (!ShouldTriggerAOG()) return false;
            TriggerAOG();
            return true;
        }

        #endregion

        #region Choices

        /// <summary>
        /// 玩家选择。1=调机 2=延误维修 3=顾问（仅说明，再选 1/2）。
        /// 选定 1/2 后：恢复时间、标记已触发（终身不再弹）、自动存档。
        /// </summary>
        public void OnAOGChoiceMade(int choice)
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                ForceResumeTime();
                return;
            }

            if (choice == 3)
            {
                gs.aogAdvisorConsulted = true;
                gs.aogChoice = 3;
                gs.AddDecisionLog("AOG：呼叫机务顾问，听取建议后再决策。");
                if (ui != null)
                    ui.ShowAdvisorThenChoices();
                // 顾问不恢复时间、不标记完成，须再选 1 或 2
                return;
            }

            if (choice != 1 && choice != 2) return;

            // 先落旗，避免结算异常时下季度再次弹出
            gs.hasAOGTriggered = true;
            gs.aogChoice = choice;

            try
            {
                ApplyChoice(gs, choice);
            }
            finally
            {
                if (ui != null)
                    ui.Hide();

                ForceResumeTime();
                _isShowing = false;
                gs.NotifyChanged();

                if (SaveManager.Instance != null)
                    SaveManager.Instance.Save();

                OnAOGResolved?.Invoke(choice);
                Debug.Log($"[AOGManager] AOG 已解决，选择={choice}，现金={gs.cash:F0}，hasAOGTriggered=true，timeScale={Time.timeScale}");
            }
        }

        private static void ApplyChoice(GameState gs, int choice)
        {
            if (choice == 1)
            {
                gs.cash -= CostFerry;
                gs.yearNetProfit -= CostFerry;
                gs.AddDecisionLog(
                    $"AOG【调机备援】现金 -{CostFerry:F0} 万。航班准点，无旅客赔偿，董事会满意度不变。");
            }
            else // 2
            {
                gs.cash -= CostDelayTotal;
                gs.yearNetProfit -= CostDelayTotal;
                gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction - DelaySatisfactionPenalty, 0f, 100f);
                gs.onTimePercent = Mathf.Clamp(gs.onTimePercent - DelayOnTimePenalty, 40f, 100f);
                gs.AddDecisionLog(
                    $"AOG【延误维修】现金 -{CostDelayTotal:F0} 万（维修 {CostRepair:F0}+赔偿 {CostDelayComp:F0}）。" +
                    $"准点率下降，董事会满意度 -{DelaySatisfactionPenalty:F0}。");
            }
        }

        private void ResumeTime() => ForceResumeTime();

        private void ForceResumeTime()
        {
            // 选完必须恢复；不依赖暂停前快照，避免卡在 0
            Time.timeScale = 1f;
            _timeScaleBeforePause = 1f;
        }

        #endregion

#if UNITY_EDITOR
        /// <summary>编辑器调试：忽略季度限制强制弹出（仍要求未触发）。</summary>
        public void DebugForceShow()
        {
            var gs = GameState.Instance;
            if (gs == null) return;
            gs.firstFlightCompleted = true;
            gs.year = 1;
            if (gs.quarter < 2) gs.quarter = 2;
            gs.hasAOGTriggered = false;
            TriggerAOG();
        }
#endif
    }
}
