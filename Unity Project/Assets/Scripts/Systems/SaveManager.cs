using System;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AirlineCEO.Systems
{
    /// <summary>
    /// PlayerPrefs 存档：F5 保存，F9 读取（Phase 6 含目标清单与难度）。
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        private const string KeyCash = "ACEO_Cash";
        private const string KeyYear = "ACEO_Year";
        private const string KeyQuarter = "ACEO_Quarter";
        private const string KeySatisfaction = "ACEO_BoardSatisfaction";
        private const string KeyFirstFlight = "ACEO_FirstFlight";
        private const string KeyDifficulty = "ACEO_Difficulty";
        private const string KeyGameOver = "ACEO_GameOver";
        private const string KeyGameOverReason = "ACEO_GameOverReason";
        private const string KeyTargets = "ACEO_TargetsJson";
        private const string KeyTargetsYear = "ACEO_TargetsYear";
        private const string KeyYearNet = "ACEO_YearNet";
        private const string KeyProbationUsed = "ACEO_ProbationUsed";
        private const string KeyFailVotes = "ACEO_FailVotes";
        private const string KeySafetyRating = "ACEO_SafetyRating";
        private const string KeySafetyLog = "ACEO_SafetyLogJson";
        private const string KeyHasAOGTriggered = "ACEO_HasAOGTriggered";
        private const string KeyAOGChoice = "ACEO_AOGChoice";
        private const string KeyAOGAdvisor = "ACEO_AOGAdvisor";
        private const string KeyOnTime = "ACEO_OnTimePercent";
        private const string KeyHasExpansion = "ACEO_HasExpansionProposalTriggered";
        private const string KeyExpansionDelay = "ACEO_ExpansionDelayCount";
        private const string KeyExpansionAccepted = "ACEO_ExpansionAccepted";
        private const string KeyExpansionRejected = "ACEO_ExpansionRejected";
        private const string KeyGreatRoute = "ACEO_GreatRouteUnlocked";
        private const string KeyLastExpansionYear = "ACEO_LastExpansionOfferYear";
        private const string KeyDeptHeads = "ACEO_DeptHeads";
        private const string KeyFuelPrice = "ACEO_FuelPrice";
        private const string KeyHedgeContracts = "ACEO_HedgeContracts";
        private const string KeyActiveLoans = "ACEO_ActiveLoans";
        private const string KeyFuelHistory = "ACEO_FuelHistoryJson";
        private const string KeyLoanDefaults = "ACEO_LoanDefaults";
        private const string KeyHasSeenHedgeTutorial = "ACEO_HasSeenHedgeTutorial";

        [Serializable]
        private class FloatListSave
        {
            public float[] items;
        }

        public string LastStatus { get; private set; } = "尚未存档";

        [Serializable]
        private class TargetListSave
        {
            public BoardTarget[] items;
        }

        [Serializable]
        private class SafetyLogSave
        {
            public SafetyEventLog[] items;
        }

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

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f5Key.wasPressedThisFrame) Save();
                if (kb.f9Key.wasPressedThisFrame) Load();
            }
#else
            if (Input.GetKeyDown(KeyCode.F5))
                Save();
            if (Input.GetKeyDown(KeyCode.F9))
                Load();
#endif
        }

        public void Save()
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                LastStatus = "存档失败：无 GameState";
                Debug.LogWarning(LastStatus);
                return;
            }

            PlayerPrefs.SetFloat(KeyCash, gs.cash);
            PlayerPrefs.SetInt(KeyYear, gs.year);
            PlayerPrefs.SetInt(KeyQuarter, gs.quarter);
            PlayerPrefs.SetFloat(KeySatisfaction, gs.boardSatisfaction);
            PlayerPrefs.SetInt(KeyFirstFlight, gs.firstFlightCompleted ? 1 : 0);
            PlayerPrefs.SetInt(KeyDifficulty, (int)gs.difficulty);
            PlayerPrefs.SetInt(KeyGameOver, gs.gameOver ? 1 : 0);
            PlayerPrefs.SetString(KeyGameOverReason, gs.gameOverReason ?? "");
            PlayerPrefs.SetInt(KeyTargetsYear, gs.targetsFiscalYear);
            PlayerPrefs.SetFloat(KeyYearNet, gs.yearNetProfit);
            PlayerPrefs.SetInt(KeyProbationUsed, gs.probationProtectionsUsed);
            PlayerPrefs.SetInt(KeyFailVotes, gs.consecutiveFailedVotes);
            PlayerPrefs.SetInt(KeySafetyRating, (int)gs.safetyRating);
            PlayerPrefs.SetInt(KeyHasAOGTriggered, gs.hasAOGTriggered ? 1 : 0);
            PlayerPrefs.SetInt(KeyAOGChoice, gs.aogChoice);
            PlayerPrefs.SetInt(KeyAOGAdvisor, gs.aogAdvisorConsulted ? 1 : 0);
            PlayerPrefs.SetFloat(KeyOnTime, gs.onTimePercent);
            PlayerPrefs.SetInt(KeyHasExpansion, gs.hasExpansionProposalTriggered ? 1 : 0);
            PlayerPrefs.SetInt(KeyExpansionDelay, gs.expansionProposalDelayCount);
            PlayerPrefs.SetInt(KeyExpansionAccepted, gs.expansionAccepted ? 1 : 0);
            PlayerPrefs.SetInt(KeyExpansionRejected, gs.expansionRejected ? 1 : 0);
            PlayerPrefs.SetInt(KeyGreatRoute, gs.greatRouteUnlocked ? 1 : 0);
            PlayerPrefs.SetInt(KeyLastExpansionYear, gs.lastExpansionOfferYear);

            var blob = new TargetListSave
            {
                items = gs.currentTargets != null ? gs.currentTargets.ToArray() : Array.Empty<BoardTarget>()
            };
            PlayerPrefs.SetString(KeyTargets, JsonUtility.ToJson(blob));

            var safetyBlob = new SafetyLogSave
            {
                items = gs.safetyEventLog != null ? gs.safetyEventLog.ToArray() : Array.Empty<SafetyEventLog>()
            };
            PlayerPrefs.SetString(KeySafetyLog, JsonUtility.ToJson(safetyBlob));

            if (DepartmentManager.Instance != null)
                PlayerPrefs.SetString(KeyDeptHeads, DepartmentManager.Instance.ToJson());
            else if (!PlayerPrefs.HasKey(KeyDeptHeads))
                PlayerPrefs.SetString(KeyDeptHeads, "");

            PlayerPrefs.SetFloat(KeyFuelPrice, gs.fuelPricePerTon);
            PlayerPrefs.SetString(KeyHedgeContracts,
                HedgeManager.Instance != null
                    ? HedgeManager.Instance.ContractsToJson()
                    : JsonUtility.ToJson(new HedgeListSave { items = gs.hedgeContracts != null ? gs.hedgeContracts.ToArray() : Array.Empty<HedgeContract>() }));
            PlayerPrefs.SetString(KeyActiveLoans,
                LoanManager.Instance != null
                    ? LoanManager.Instance.LoansToJson()
                    : JsonUtility.ToJson(new LoanListSave { items = gs.activeLoans != null ? gs.activeLoans.ToArray() : Array.Empty<Loan>() }));
            PlayerPrefs.SetString(KeyFuelHistory, JsonUtility.ToJson(new FloatListSave
            {
                items = gs.fuelPriceHistory != null ? gs.fuelPriceHistory.ToArray() : Array.Empty<float>()
            }));
            if (LoanManager.Instance != null)
                PlayerPrefs.SetInt(KeyLoanDefaults, LoanManager.Instance.consecutiveDefaults);
            PlayerPrefs.SetInt(KeyHasSeenHedgeTutorial, gs.hasSeenHedgeTutorial ? 1 : 0);

            PlayerPrefs.Save();

            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            LastStatus = $"已存档 F5 · 现金 {gs.cash:F0} · {gs.DateLabel} · {CashRunnelCalculator.FormatPlayerLabel(runway)}";
            Debug.Log($"[SaveManager] {LastStatus}");
            gs.NotifyChanged();
        }

        public void Load()
        {
            if (!PlayerPrefs.HasKey(KeyCash))
            {
                LastStatus = "读档失败：尚无存档";
                Debug.LogWarning($"[SaveManager] {LastStatus}");
                return;
            }

            var gs = GameState.Instance;
            if (gs == null)
            {
                LastStatus = "读档失败：无 GameState";
                return;
            }

            gs.SetFromSave(
                PlayerPrefs.GetFloat(KeyCash),
                PlayerPrefs.GetInt(KeyYear, 1),
                PlayerPrefs.GetInt(KeyQuarter, 1),
                PlayerPrefs.GetFloat(KeySatisfaction, 50f),
                PlayerPrefs.GetInt(KeyFirstFlight, 0) == 1);

            gs.difficulty = (DifficultyMode)PlayerPrefs.GetInt(KeyDifficulty, (int)DifficultyMode.Normal);
            gs.gameOver = PlayerPrefs.GetInt(KeyGameOver, 0) == 1;
            gs.gameOverReason = PlayerPrefs.GetString(KeyGameOverReason, "");
            gs.targetsFiscalYear = PlayerPrefs.GetInt(KeyTargetsYear, gs.year);
            gs.yearNetProfit = PlayerPrefs.GetFloat(KeyYearNet, 0f);
            gs.probationProtectionsUsed = PlayerPrefs.GetInt(KeyProbationUsed, 0);
            gs.consecutiveFailedVotes = PlayerPrefs.GetInt(KeyFailVotes, 0);
            gs.safetyRating = (SafetyRating)PlayerPrefs.GetInt(KeySafetyRating, (int)SafetyRating.B);
            gs.SyncSafetyIndexFromRating();
            gs.hasAOGTriggered = PlayerPrefs.GetInt(KeyHasAOGTriggered, 0) == 1;
            gs.aogChoice = PlayerPrefs.GetInt(KeyAOGChoice, 0);
            gs.aogAdvisorConsulted = PlayerPrefs.GetInt(KeyAOGAdvisor, 0) == 1;
            gs.onTimePercent = PlayerPrefs.GetFloat(KeyOnTime, 88f);
            gs.hasExpansionProposalTriggered = PlayerPrefs.GetInt(KeyHasExpansion, 0) == 1;
            gs.expansionProposalDelayCount = PlayerPrefs.GetInt(KeyExpansionDelay, 0);
            gs.expansionAccepted = PlayerPrefs.GetInt(KeyExpansionAccepted, 0) == 1;
            gs.expansionRejected = PlayerPrefs.GetInt(KeyExpansionRejected, 0) == 1;
            gs.greatRouteUnlocked = PlayerPrefs.GetInt(KeyGreatRoute, 0) == 1;
            gs.lastExpansionOfferYear = PlayerPrefs.GetInt(KeyLastExpansionYear, 0);

            string json = PlayerPrefs.GetString(KeyTargets, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var blob = JsonUtility.FromJson<TargetListSave>(json);
                    gs.currentTargets = blob?.items != null
                        ? new System.Collections.Generic.List<BoardTarget>(blob.items)
                        : new System.Collections.Generic.List<BoardTarget>();
                }
                catch
                {
                    gs.currentTargets = new System.Collections.Generic.List<BoardTarget>();
                }
            }

            string safetyJson = PlayerPrefs.GetString(KeySafetyLog, "");
            if (!string.IsNullOrEmpty(safetyJson))
            {
                try
                {
                    var blob = JsonUtility.FromJson<SafetyLogSave>(safetyJson);
                    gs.safetyEventLog = blob?.items != null
                        ? new System.Collections.Generic.List<SafetyEventLog>(blob.items)
                        : new System.Collections.Generic.List<SafetyEventLog>();
                }
                catch
                {
                    gs.safetyEventLog = new System.Collections.Generic.List<SafetyEventLog>();
                }
            }
            else
            {
                gs.safetyEventLog = new System.Collections.Generic.List<SafetyEventLog>();
            }

            if (BoardManager.Instance != null)
                BoardManager.Instance.RefreshTargetProgress();

            if (DepartmentManager.Instance != null)
                DepartmentManager.Instance.FromJson(PlayerPrefs.GetString(KeyDeptHeads, ""));

            gs.fuelPricePerTon = PlayerPrefs.GetFloat(KeyFuelPrice, gs.fuelPricePerTon);
            if (HedgeManager.Instance != null)
                HedgeManager.Instance.ContractsFromJson(PlayerPrefs.GetString(KeyHedgeContracts, ""));
            else
            {
                try
                {
                    var blob = JsonUtility.FromJson<HedgeListSave>(PlayerPrefs.GetString(KeyHedgeContracts, ""));
                    gs.hedgeContracts = blob?.items != null
                        ? new System.Collections.Generic.List<HedgeContract>(blob.items)
                        : new System.Collections.Generic.List<HedgeContract>();
                }
                catch { gs.hedgeContracts = new System.Collections.Generic.List<HedgeContract>(); }
            }

            if (LoanManager.Instance != null)
            {
                LoanManager.Instance.LoansFromJson(PlayerPrefs.GetString(KeyActiveLoans, ""));
                LoanManager.Instance.consecutiveDefaults = PlayerPrefs.GetInt(KeyLoanDefaults, 0);
            }

            try
            {
                var hist = JsonUtility.FromJson<FloatListSave>(PlayerPrefs.GetString(KeyFuelHistory, ""));
                gs.fuelPriceHistory = hist?.items != null
                    ? new System.Collections.Generic.List<float>(hist.items)
                    : new System.Collections.Generic.List<float> { gs.fuelPricePerTon };
            }
            catch
            {
                gs.fuelPriceHistory = new System.Collections.Generic.List<float> { gs.fuelPricePerTon };
            }

            gs.hasSeenHedgeTutorial = PlayerPrefs.GetInt(KeyHasSeenHedgeTutorial, 0) == 1;

            LastStatus = gs.gameOver
                ? $"已读档 F9 · 游戏已结束 · {gs.DateLabel}"
                : $"已读档 F9 · 现金 {gs.cash:F0} · {gs.DateLabel}";
            Debug.Log($"[SaveManager] {LastStatus}");
            gs.NotifyChanged();
        }
    }
}
