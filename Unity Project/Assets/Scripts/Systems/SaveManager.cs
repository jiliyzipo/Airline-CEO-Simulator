using System;
using System.Collections.Generic;
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
        private const string KeySpeedLevel = "ACEO_SpeedLevel";
        private const string KeySimDay = "ACEO_SimDay";
        private const string KeySimHour = "ACEO_SimHour";
        private const string KeySimMinute = "ACEO_SimMinute";
        private const string KeyWallYear = "ACEO_WallYear";
        private const string KeyWallMonth = "ACEO_WallMonth";
        private const string KeyWallDay = "ACEO_WallDay";
        private const string KeySatisfaction = "ACEO_BoardSatisfaction";
        private const string KeyFirstFlight = "ACEO_FirstFlight";
        private const string KeyDifficulty = "ACEO_Difficulty";
        private const string KeyGameOver = "ACEO_GameOver";
        private const string KeyGameOverReason = "ACEO_GameOverReason";
        private const string KeyTargets = "ACEO_TargetsJson";
        private const string KeyTargetsYear = "ACEO_TargetsYear";
        private const string KeyAnnualGoalsConfirmedYear = "ACEO_AnnualGoalsConfirmedYear";
        private const string KeyYearNet = "ACEO_YearNet";
        private const string KeyLiveQIncome = "ACEO_LiveQIncome";
        private const string KeyLiveQExpense = "ACEO_LiveQExpense";
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
        private const string KeyStaffPool = "ACEO_StaffPool";
        private const string KeyStaffRecruits = "ACEO_StaffRecruits";
        /// <summary>人力资源部聚合状态（培训/证照/招聘在途/培训中心/工会）。</summary>
        private const string KeyHr = "ACEO_Hr";
        private const string KeyUsedMarket = "ACEO_UsedMarket";
        private const string KeyCityEconomy = "ACEO_CityEconomy";
        private const string KeyFuelPrice = "ACEO_FuelPrice";
        private const string KeyHedgeContracts = "ACEO_HedgeContracts";
        private const string KeyActiveLoans = "ACEO_ActiveLoans";
        private const string KeyFuelHistory = "ACEO_FuelHistoryJson";
        private const string KeyLoanDefaults = "ACEO_LoanDefaults";
        private const string KeyHasSeenHedgeTutorial = "ACEO_HasSeenHedgeTutorial";
        private const string KeyBudget = "ACEO_Budget";
        private const string KeyBudgetYear = "ACEO_BudgetYear";
        private const string KeyBudgetSubmitted = "ACEO_BudgetSubmitted";
        private const string KeyBudgetApproved = "ACEO_BudgetApproved";
        private const string KeyCarbon = "ACEO_Carbon";
        private const string KeyRestructuring = "ACEO_Restructuring";
        private const string KeyPdpPayable = "ACEO_PdpPayable";
        private const string KeyFxHedgeRatio = "ACEO_FxHedgeRatio";
        private const string KeyUsdCny = "ACEO_UsdCny";
        private const string KeyUsdCnyHistory = "ACEO_UsdCnyHistoryJson";
        private const string KeyCumFxPnl = "ACEO_CumFxPnlWan";
        private const string KeyMkt = "ACEO_Mkt";
        private const string KeyCargo = "ACEO_Cargo";
        private const string KeyCommerce = "ACEO_Commerce";
        private const string KeySubsidies = "ACEO_RouteSubsidies";
        private const string KeyTransition = "ACEO_TransitionBusiness";
        private const string KeyTransitionIntro = "ACEO_TransitionIntroSent";
        private const string KeyTransitionQIncome = "ACEO_TransitionQIncome";
        private const string KeyAircraftOrders = "ACEO_AircraftOrders";
        private const string KeyInsurance = "ACEO_Insurance";
        private const string KeyFuelProc = "ACEO_FuelProc";
        private const string KeyCarbonComp = "ACEO_CarbonComp";
        private const string KeyCumTaxPaid = "ACEO_CumTaxPaid";
        private const string KeyYearTaxPaid = "ACEO_YearTaxPaid";
        private const string KeyDecision = "ACEO_Decision";
        private const string KeyFleet = "ACEO_Fleet";
        private const string KeyActiveRoutes = "ACEO_ActiveRoutes";
        private const string KeyFlightRings = "ACEO_FlightRings";
        private const string KeyTakeover = "ACEO_Takeover";
        private const string KeyLegacyIssues = "ACEO_LegacyIssues";
        private const string KeyAiAirlines = "ACEO_AIAirlines";
        private const string KeyRandomEvents = "ACEO_RandomEvents";
        private const string KeyMacroEconomy = "ACEO_MacroEconomy";
        private const string KeyCompanyName = "ACEO_CompanyNameZh";
        private const string KeyCompanyNameEn = "ACEO_CompanyNameEn";
        private const string KeyCeoName = "ACEO_CeoDisplayName";
        private const string KeyCompanyIcao = "ACEO_CompanyIcao";
        private const string KeyCompanyIata = "ACEO_CompanyIata";
        private const string KeyHubIata = "ACEO_HubIata";
        private const string KeyCompanyBases = "ACEO_CompanyBases";
        private const string KeySetupDone = "ACEO_SetupCompleted";
        private const string KeyAdvisorTutStep = "ACEO_AdvisorTutStep";
        private const string KeyAdvisorTutMail = "ACEO_AdvisorTutMailLeft";
        private const string KeyAdvisorTutDeferred = "ACEO_AdvisorTutDeferred";
        private const string KeyAdvisorTutDone = "ACEO_AdvisorTutDone";
        private const string KeyAdvisorTutAwait = "ACEO_AdvisorTutAwait";
        private const string KeyCeoBirthYear = "ACEO_CeoBirthYear";
        private const string KeyCeoDossier = "ACEO_CeoDossier";
        private const string KeyGameOverVictory = "ACEO_GameOverVictory";
        private const string KeyLegendMode = "ACEO_LegendMode";
        private const string KeyNewGamePlus = "ACEO_NewGamePlus";
        private const string KeyFastMode = "ACEO_FastMode";
        private const string KeyLegacyScore = "ACEO_LegacyScore";
        private const string KeyLegacyGrade = "ACEO_LegacyGrade";
        private const string KeyActiveMode = "ACEO_ActiveMode";
        private const string KeyLegendActive = "ACEO_LegendActive";
        private const string KeyFastActive = "ACEO_FastActive";
        private const string KeySafetyDQuarters = "ACEO_SafetyDQuarters";
        private const string KeyMajorCrash = "ACEO_MajorCrash";
        private const string KeyLastDefeatKind = "ACEO_LastDefeatKind";
        private const string KeyIronmanActive = "ACEO_IronmanActive";
        private const string KeyMistakeProtectionRemaining = "ACEO_MistakeProtectionRemaining";
        private const string KeyMistakeProtectionUsedYear = "ACEO_MistakeProtectionUsedYear";
        private const string KeyMessages = "ACEO_Messages";
        private const string KeyOnTimeLowQuarters = "ACEO_OnTimeLowQuarters";
        private const string KeyBoardMemberIds = "ACEO_BoardMemberIds";
        private const string KeyInbox = "ACEO_Inbox";
        private const string KeyPendingInbox = "ACEO_PendingInbox";
        private const string KeyDynamicFeed = "ACEO_DynamicFeed";
        private const string KeyFeedMeta = "ACEO_FeedMeta";
        private const string KeyExternalEvents = "ACEO_ExternalEvents";

        /// <summary>
        /// 主存档全部 PlayerPrefs 键（供多槽位快照克隆：时间胶囊 / 分支存档 / 季度自动存档）。
        /// 新增存档字段时必须同步登记到此处，否则该字段不会被快照。
        /// </summary>
        public static readonly string[] AllSaveKeys =
        {
            KeyCash, KeyCompanyName, KeyCompanyNameEn, KeyCeoName, KeyCompanyIcao, KeyCompanyIata, KeyHubIata, KeyCompanyBases,
            KeySetupDone, KeyAdvisorTutStep, KeyAdvisorTutMail, KeyAdvisorTutDeferred,
            KeyAdvisorTutDone, KeyAdvisorTutAwait, KeyYear, KeyQuarter, KeySpeedLevel,
            KeySimDay, KeySimHour, KeySimMinute, KeyWallYear, KeyWallMonth, KeyWallDay, KeySatisfaction,
            KeyFirstFlight, KeyDifficulty, KeyGameOver, KeyGameOverReason, KeyCeoBirthYear, KeyCeoDossier,
            KeyGameOverVictory, KeyLegendMode, KeyNewGamePlus, KeyFastMode, KeyLegacyScore,
            KeyLegacyGrade, KeyActiveMode, KeyLegendActive, KeyFastActive, KeyIronmanActive,
            KeyMistakeProtectionRemaining, KeyMistakeProtectionUsedYear,
            KeyMessages, KeyOnTimeLowQuarters,
            KeyBoardMemberIds, KeyInbox, KeyPendingInbox,
            KeyDynamicFeed, KeyFeedMeta, KeyExternalEvents,
            KeySafetyDQuarters, KeyMajorCrash, KeyLastDefeatKind, KeyTargetsYear, KeyAnnualGoalsConfirmedYear, KeyYearNet,
            KeyLiveQIncome, KeyLiveQExpense,
            KeyProbationUsed, KeyFailVotes, KeySafetyRating, KeyHasAOGTriggered, KeyAOGChoice,
            KeyAOGAdvisor, KeyOnTime, KeyHasExpansion, KeyExpansionDelay, KeyExpansionAccepted,
            KeyExpansionRejected, KeyGreatRoute, KeyLastExpansionYear, KeyTargets, KeySafetyLog,
            KeyDeptHeads, KeyStaffPool, KeyStaffRecruits, KeyHr, KeyUsedMarket, KeyCityEconomy, KeyFuelPrice, KeyHedgeContracts, KeyActiveLoans, KeyFuelHistory,
            KeyLoanDefaults, KeyHasSeenHedgeTutorial, KeyBudget, KeyBudgetYear, KeyBudgetSubmitted,
            KeyBudgetApproved, KeyCarbon, KeyRestructuring, KeyPdpPayable, KeyFxHedgeRatio,
            KeyUsdCny, KeyUsdCnyHistory, KeyCumFxPnl,
            KeyMkt, KeyCargo, KeyCommerce, KeySubsidies, KeyTransition, KeyAircraftOrders, KeyInsurance,
            KeyFuelProc, KeyCarbonComp, KeyCumTaxPaid, KeyYearTaxPaid, KeyDecision, KeyFleet,
            KeyActiveRoutes, KeyFlightRings, KeyTakeover, KeyLegacyIssues,
            KeyTransitionIntro, KeyTransitionQIncome,
            KeyAiAirlines, KeyRandomEvents,
            KeyMacroEconomy + "_GDP", KeyMacroEconomy + "_Phase", KeyMacroEconomy + "_Duration"
        };

        private const string ActivePrefixKey = "ACEO_ActivePrefix";

        /// <summary>
        /// 当前活动命名空间（What-If 分支差分隔离，GDB 第31章后置优化）。
        /// 空字符串 = 主线；"ACEO_Branch_{id}_" = 某分支。所有存档读写经此前缀重定向，
        /// 使分支内游玩不再污染主线主存档链。自动持久化，退出重进后仍记得所在分支。
        /// </summary>
        public static string ActivePrefix
        {
            get => PlayerPrefs.GetString(ActivePrefixKey, "");
            set { PlayerPrefs.SetString(ActivePrefixKey, value ?? ""); PlayerPrefs.Save(); }
        }

        static string P(string key) => ActivePrefix + key;
        static void SetF(string key, float v) => PlayerPrefs.SetFloat(P(key), v);
        static void SetS(string key, string v) => PlayerPrefs.SetString(P(key), v);
        static void SetI(string key, int v) => PlayerPrefs.SetInt(P(key), v);
        static float GetF(string key, float def) => PlayerPrefs.GetFloat(P(key), def);
        static string GetS(string key, string def) => PlayerPrefs.GetString(P(key), def);
        static int GetI(string key, int def) => PlayerPrefs.GetInt(P(key), def);
        static bool HasKey(string key) => PlayerPrefs.HasKey(P(key));

        [Serializable]
        private class FloatListSave
        {
            public float[] items;
        }

        [Serializable]
        private class StringListSave
        {
            public string[] items;
        }

        [Serializable]
        private class TakeoverSave
        {
            public bool mode;
            public string airlineId;
            public bool briefingShown;
            public int crewVacancyPercent;
            public string education;
            public string career;
            public string trait;
        }

        public string LastStatus { get; private set; } = "尚未存档";

        /// <summary>通关解锁标志（模式选择界面直接读 PlayerPrefs）。</summary>
        public bool UnlockedLegend => PlayerPrefs.GetInt(KeyLegendMode, 0) == 1;
        public bool UnlockedNewGamePlus => PlayerPrefs.GetInt(KeyNewGamePlus, 0) == 1;
        public bool UnlockedFast => PlayerPrefs.GetInt(KeyFastMode, 0) == 1;

        public bool HasSave() => HasKey(KeyCash);

        /// <summary>
        /// 「最近保存」摘要（主菜单存档卡片用）：**只读 PlayerPrefs**，不 Load()、不改动任何运行时状态。
        /// 与 <see cref="HasSave"/> 同一判据（KeyCash 是否存在），但走静态路径，
        /// 因此不依赖 <c>SaveManager.Instance</c> 是否已建（主菜单早于玩法初始化）。
        /// </summary>
        /// <param name="companyZh">存档里的公司中文名（老存档可能为空 → 调用方自行兜底文案）。</param>
        /// <param name="year">财年编号。</param>
        /// <param name="quarter">财年内季度 1~4。</param>
        /// <param name="cashWan">现金（万美元）。</param>
        /// <param name="satisfaction">董事会满意度。</param>
        /// <param name="gameOver">该存档是否已终局（被解雇 / 通关）。</param>
        /// <returns>是否存在存档。</returns>
        public static bool TryPeekSummary(out string companyZh, out int year, out int quarter,
            out float cashWan, out float satisfaction, out bool gameOver)
        {
            companyZh = GetS(KeyCompanyName, "");
            year = GetI(KeyYear, 1);
            quarter = GetI(KeyQuarter, 1);
            cashWan = GetF(KeyCash, 0f);
            satisfaction = GetF(KeySatisfaction, 0f);
            gameOver = GetI(KeyGameOver, 0) == 1;
            return PlayerPrefs.HasKey(P(KeyCash));
        }

        /// <summary>
        /// 运行时清档（n40「清档 · 保留 API Key」）：遍历主存档全部键 + 多槽位快照删除，
        /// 保留解锁标志（传奇/新游戏+/快速三枚通关奖励）与 AppSettings 玩家偏好。
        /// 通关解锁标志用 KeyLegendMode/KeyNewGamePlus/KeyFastMode 单独读，不在 AllSaveKeys 内，故不额外处理。
        /// </summary>
        public static void ClearAllData()
        {
            ActivePrefix = "";
            for (int i = 0; i < AllSaveKeys.Length; i++)
                PlayerPrefs.DeleteKey(AllSaveKeys[i]);
            PlayerPrefs.DeleteKey(KeySetupDone);
            SaveSlotManager.ClearAllSlots();
            // 外部数据源的缓存与去重表属于运行时状态（源地址等配置保留）
            AirlineCEO.Logic.ExternalDataFetcher.Reset();
            AirlineCEO.Logic.WeatherFeed.Reset();
            PlayerPrefs.Save();
        }

        public bool HasCompletedSetup() => PlayerPrefs.GetInt(KeySetupDone, 0) == 1;

        public void MarkSetupCompleted()
        {
            PlayerPrefs.SetInt(KeySetupDone, 1);
            PlayerPrefs.Save();
        }

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
            // 开始界面 / 开局向导里不响应存档热键：那时 GameState 还是出厂值
            // （刚「退出到开始界面」时更是被显式重置过），按一下 F5 就会把空局写进存档，
            // 等于静默清档。F6/F7 是局内开发工具，同理只在局内生效。
            if (!AirlineCEO.UI.Setup.SetupFlowController.InGameplaySession) return;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f5Key.wasPressedThisFrame) HandleQuickSave();
                if (kb.f9Key.wasPressedThisFrame) Load();
                if (kb.f6Key.wasPressedThisFrame) DevTogglePause();
                if (kb.f7Key.wasPressedThisFrame) DevAdvanceQuarter();
            }
#else
            if (Input.GetKeyDown(KeyCode.F5))
                HandleQuickSave();
            if (Input.GetKeyDown(KeyCode.F9))
                Load();
            // 开发工具：F6 暂停/继续 · F7 推进一季（非正式玩法控件）
            if (Input.GetKeyDown(KeyCode.F6))
                DevTogglePause();
            if (Input.GetKeyDown(KeyCode.F7))
                DevAdvanceQuarter();
#endif
        }

        /// <summary>开发工具：切换暂停。</summary>
        public void DevTogglePause()
        {
            if (GameSpeed.IsPaused) GameSpeed.Resume();
            else GameSpeed.Pause();
            LastStatus = GameSpeed.IsPaused ? "开发工具：已暂停" : $"开发工具：已恢复 {GameSpeed.CurrentLabel}";
            Debug.Log($"[DevTools] {LastStatus}");
        }

        /// <summary>开发工具：强制推进一个季度。</summary>
        public void DevAdvanceQuarter()
        {
            var tm = TimeManager.Instance;
            if (tm == null)
            {
                LastStatus = "开发工具：无 TimeManager";
                return;
            }
            var result = tm.AdvanceQuarter();
            LastStatus = result != null && result.success
                ? $"开发工具：已推进季度 → {GameState.Instance?.DateLabel}"
                : $"开发工具：推进失败 · {result?.message}";
            Debug.Log($"[DevTools] {LastStatus}");
        }

        /// <summary>F5 快速存档：铁人模式禁用（GDB 31.1）。</summary>
        void HandleQuickSave()
        {
            var gs = GameState.Instance;
            if (gs != null && gs.ironmanModeActive)
            {
                LastStatus = "铁人模式禁用手动存档（仅季度自动存档实时覆盖）。";
                Debug.Log($"[SaveManager] {LastStatus}");
                return;
            }
            Save();
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

            SetF(KeyCash, gs.cash);
            SetS(KeyCompanyName, gs.companyNameZh ?? "");
            SetS(KeyCompanyNameEn, gs.companyNameEn ?? "");
            SetS(KeyCeoName, gs.ceoDisplayName ?? "");
            SetS(KeyCompanyIcao, gs.companyIcao ?? "");
            SetS(KeyCompanyIata, gs.companyIata ?? "");
            SetS(KeyHubIata, gs.hubIata ?? "");
            SetS(KeyCompanyBases, JsonUtility.ToJson(new StringListSave
            {
                items = gs.companyBases != null ? gs.companyBases.ToArray() : Array.Empty<string>()
            }));
            PlayerPrefs.SetInt(KeySetupDone, 1);
            SetI(KeyAdvisorTutStep, gs.advisorTutorialStep);
            SetI(KeyAdvisorTutMail, gs.advisorTutorialMailLeft ? 1 : 0);
            SetI(KeyAdvisorTutDeferred, gs.advisorTutorialDeferred ? 1 : 0);
            SetI(KeyAdvisorTutDone, gs.advisorTutorialCompleted ? 1 : 0);
            SetI(KeyAdvisorTutAwait, gs.advisorTutorialAwaitPage);
            SetI(KeyYear, gs.year);
            SetI(KeyQuarter, gs.quarter);
            SetI(KeySpeedLevel, Mathf.Clamp(gs.speedLevel, GameSpeed.L1, GameSpeed.L3));
            SetI(KeySimDay, Mathf.Clamp(gs.simDay, 1, GameState.DaysPerQuarter));
            SetI(KeySimHour, Mathf.Clamp(gs.simHour, 0, 23));
            SetI(KeySimMinute, Mathf.Clamp(gs.simMinute, 0, 59));
            if (gs.wallYear <= 0)
                gs.AlignClockToRealWorld();
            SetI(KeyWallYear, gs.wallYear);
            SetI(KeyWallMonth, gs.wallMonth);
            SetI(KeyWallDay, gs.wallDay);
            SetF(KeySatisfaction, gs.boardSatisfaction);
            SetI(KeyFirstFlight, gs.firstFlightCompleted ? 1 : 0);
            SetI(KeyDifficulty, (int)gs.difficulty);
            SetI(KeyGameOver, gs.gameOver ? 1 : 0);
            SetS(KeyGameOverReason, gs.gameOverReason ?? "");
            SetI(KeyCeoBirthYear, gs.ceoBirthYear);
            SetS(KeyCeoDossier, JsonUtility.ToJson(new CeoDossierSave
            {
                gender = gs.ceoGender ?? "男",
                nationality = gs.ceoNationality ?? "中国",
                contractYears = gs.ceoContractYears,
                contractStartYear = gs.ceoContractStartYear,
                weeklyPayWan = gs.ceoWeeklyPayWan,
                bonusWan = gs.ceoBonusWan
            }));
            SetI(KeyGameOverVictory, gs.gameOverVictory ? 1 : 0);
            PlayerPrefs.SetInt(KeyLegendMode, gs.unlockedLegendMode ? 1 : 0);
            PlayerPrefs.SetInt(KeyNewGamePlus, gs.unlockedNewGamePlus ? 1 : 0);
            PlayerPrefs.SetInt(KeyFastMode, gs.unlockedFastMode ? 1 : 0);
            SetF(KeyLegacyScore, gs.lastLegacyScore);
            SetS(KeyLegacyGrade, gs.lastLegacyGrade ?? "");
            SetI(KeyActiveMode, gs.activeMode);
            SetI(KeyLegendActive, gs.legendModeActive ? 1 : 0);
            SetI(KeyFastActive, gs.fastModeActive ? 1 : 0);
            SetI(KeySafetyDQuarters, gs.safetyConsecutiveDQuarters);
            SetI(KeyMajorCrash, gs.majorCrashOccurred ? 1 : 0);
            SetI(KeyLastDefeatKind, gs.lastDefeatKind);
            SetI(KeyIronmanActive, gs.ironmanModeActive ? 1 : 0);
            SetI(KeyMistakeProtectionRemaining, gs.mistakeProtectionRemaining);
            SetI(KeyMistakeProtectionUsedYear, gs.mistakeProtectionUsedYear);
            SetI(KeyTargetsYear, gs.targetsFiscalYear);
            SetI(KeyAnnualGoalsConfirmedYear, gs.annualGoalsConfirmedYear);
            SetF(KeyYearNet, gs.yearNetProfit);
            SetF(KeyLiveQIncome, gs.liveQuarterIncome);
            SetF(KeyLiveQExpense, gs.liveQuarterExpense);
            SetI(KeyProbationUsed, gs.probationProtectionsUsed);
            SetI(KeyFailVotes, gs.consecutiveFailedVotes);
            SetI(KeySafetyRating, (int)gs.safetyRating);
            SetI(KeyHasAOGTriggered, gs.hasAOGTriggered ? 1 : 0);
            SetI(KeyAOGChoice, gs.aogChoice);
            SetI(KeyAOGAdvisor, gs.aogAdvisorConsulted ? 1 : 0);
            SetF(KeyOnTime, gs.onTimePercent);
            SetI(KeyHasExpansion, gs.hasExpansionProposalTriggered ? 1 : 0);
            SetI(KeyExpansionDelay, gs.expansionProposalDelayCount);
            SetI(KeyExpansionAccepted, gs.expansionAccepted ? 1 : 0);
            SetI(KeyExpansionRejected, gs.expansionRejected ? 1 : 0);
            SetI(KeyGreatRoute, gs.greatRouteUnlocked ? 1 : 0);
            SetI(KeyLastExpansionYear, gs.lastExpansionOfferYear);

            var blob = new TargetListSave
            {
                items = gs.currentTargets != null ? gs.currentTargets.ToArray() : Array.Empty<BoardTarget>()
            };
            SetS(KeyTargets, JsonUtility.ToJson(blob));

            var safetyBlob = new SafetyLogSave
            {
                items = gs.safetyEventLog != null ? gs.safetyEventLog.ToArray() : Array.Empty<SafetyEventLog>()
            };
            SetS(KeySafetyLog, JsonUtility.ToJson(safetyBlob));

            if (DepartmentManager.Instance != null)
                SetS(KeyDeptHeads, DepartmentManager.Instance.ToJson());
            else if (!HasKey(KeyDeptHeads))
                SetS(KeyDeptHeads, "");

            SetS(KeyStaffPool, JsonUtility.ToJson(new PersonnelListSave
            {
                items = gs.staffPool != null ? gs.staffPool.ToArray() : Array.Empty<PersonnelData>(),
                bootstrapped = gs.staffBootstrapped
            }));
            SetS(KeyStaffRecruits, JsonUtility.ToJson(new StaffRecruitListSave
            {
                items = gs.staffRecruits != null ? gs.staffRecruits.ToArray() : Array.Empty<StaffRecruitCase>()
            }));
            // 人力资源（Phase 2 Week 10 · HR）：在培项目与名单、证照、招聘在途、培训中心等级与在建工期、
            // 工会满意度/罢工倒计时/CBA 到期、员工满意度/trainingRoi/薪酬说明。
            // 此前 HrState 从不落盘，上述状态跨存档全部丢失（且两条旧档迁移路径永不触发）。
            SetS(KeyHr, JsonUtility.ToJson(new HrSave
            {
                state = gs.hr ?? new HrState()
            }));
            SetS(KeyUsedMarket, JsonUtility.ToJson(new UsedMarketSave
            {
                items = gs.usedMarket != null ? gs.usedMarket.ToArray() : Array.Empty<UsedListing>(),
                year = gs.usedMarketYear,
                quarter = gs.usedMarketQuarter
            }));
            SetS(KeyCityEconomy, JsonUtility.ToJson(new CityEconomySave
            {
                items = gs.cityEconomyDeltas != null ? gs.cityEconomyDeltas.ToArray() : Array.Empty<CityEconomyDelta>(),
                tableRevision = gs.cityEconomyRevision
            }));

            SetF(KeyFuelPrice, gs.fuelPricePerTon);
            SetS(KeyHedgeContracts,
                HedgeManager.Instance != null
                    ? HedgeManager.Instance.ContractsToJson()
                    : JsonUtility.ToJson(new HedgeListSave { items = gs.hedgeContracts != null ? gs.hedgeContracts.ToArray() : Array.Empty<HedgeContract>() }));
            SetS(KeyActiveLoans,
                LoanManager.Instance != null
                    ? LoanManager.Instance.LoansToJson()
                    : JsonUtility.ToJson(new LoanListSave { items = gs.activeLoans != null ? gs.activeLoans.ToArray() : Array.Empty<Loan>() }));
            SetS(KeyFuelHistory, JsonUtility.ToJson(new FloatListSave
            {
                items = gs.fuelPriceHistory != null ? gs.fuelPriceHistory.ToArray() : Array.Empty<float>()
            }));
            if (LoanManager.Instance != null)
                SetI(KeyLoanDefaults, LoanManager.Instance.consecutiveDefaults);
            SetI(KeyHasSeenHedgeTutorial, gs.hasSeenHedgeTutorial ? 1 : 0);

            // FIN（Phase 2 Week 5）：预算 / 碳配额 / 重组 / PDP / 汇率对冲
            SetS(KeyBudget,
                FinanceManager.Instance != null
                    ? FinanceManager.Instance.BudgetToJson()
                    : JsonUtility.ToJson(new BudgetListSave { items = gs.budgetItems != null ? gs.budgetItems.ToArray() : Array.Empty<BudgetItem>() }));
            SetI(KeyBudgetYear, gs.budgetFiscalYear);
            SetI(KeyBudgetSubmitted, gs.budgetSubmitted ? 1 : 0);
            SetI(KeyBudgetApproved, gs.budgetApproved ? 1 : 0);
            SetS(KeyCarbon,
                FinanceManager.Instance != null
                    ? FinanceManager.Instance.CarbonToJson()
                    : JsonUtility.ToJson(new CarbonSave { emissionsTons = gs.carbonEmissionsTons, purchasedQuotaTons = gs.carbonPurchasedQuotaTons, carbonPricePerTon = gs.carbonPricePerTon }));
            SetS(KeyRestructuring,
                FinanceManager.Instance != null
                    ? FinanceManager.Instance.RestructuringToJson()
                    : JsonUtility.ToJson(new RestructuringSave()));
            SetF(KeyPdpPayable, gs.pdpPayableWan);
            SetF(KeyFxHedgeRatio, gs.fxHedgeRatio);
            SetF(KeyUsdCny, gs.usdCny);
            SetF(KeyCumFxPnl, gs.cumulativeFxPnlWan);
            SetS(KeyUsdCnyHistory, JsonUtility.ToJson(new FloatListSave
            {
                items = gs.usdCnyHistory != null ? gs.usdCnyHistory.ToArray() : Array.Empty<float>()
            }));

            // MKT（Phase 2 Week 7）：定价/辅助收入/FFP/产品/旺季策略
            SetS(KeyMkt, JsonUtility.ToJson(new MktSave
            {
                state = gs.mkt ?? new MktState()
            }));

            // 货运（GDB 14.6 · P2 长线运营）
            SetS(KeyCargo, JsonUtility.ToJson(new CargoSave
            {
                state = gs.cargo ?? new CargoState()
            }));

            // 非航收入与临空商业（GDB 26.2 · P2 长线运营）
            SetS(KeyCommerce, JsonUtility.ToJson(new AirportCommerceSave
            {
                state = gs.commerce ?? new AirportCommerceState()
            }));

            // 财务细分（GDB 第15章 · P2 长线运营）
            SetS(KeySubsidies, JsonUtility.ToJson(new SubsidyListSave
            {
                items = gs.routeSubsidies != null ? gs.routeSubsidies.ToArray() : Array.Empty<RouteSubsidy>()
            }));
            SetS(KeyAircraftOrders, JsonUtility.ToJson(new AircraftOrderListSave
            {
                items = gs.aircraftOrders != null ? gs.aircraftOrders.ToArray() : Array.Empty<AircraftOrder>()
            }));

            // 过渡业务（开局三架飞机的盈利设计）
            SetS(KeyTransition, JsonUtility.ToJson(new TransitionSave
            {
                contracts = gs.transitionContracts != null ? gs.transitionContracts.ToArray() : Array.Empty<TransitionContract>(),
                offers = gs.transitionOffers != null ? gs.transitionOffers.ToArray() : Array.Empty<TransitionOffer>(),
                crewMailSent = gs.transitionCrewMailSent,
                instructorMailSent = gs.transitionInstructorMailSent
            }));
            SetI(KeyTransitionIntro, gs.transitionIntroSent ? 1 : 0);
            SetF(KeyTransitionQIncome, gs.liveQuarterTransitionIncome);
            SetS(KeyInsurance, JsonUtility.ToJson(new InsuranceSave
            {
                coverage = (int)gs.insuranceCoverage,
                cumulativePremiumWan = gs.cumulativeInsurancePremiumWan,
                cumulativeClaimWan = gs.cumulativeInsuranceClaimWan
            }));
            SetS(KeyFuelProc, JsonUtility.ToJson(new FuelProcurementSave
            {
                state = gs.fuelProcurement ?? new FuelProcurementState()
            }));
            SetS(KeyCarbonComp, JsonUtility.ToJson(new CarbonComplianceSave
            {
                state = gs.carbonCompliance ?? new CarbonComplianceState()
            }));
            SetF(KeyCumTaxPaid, gs.cumulativeTaxPaidWan);
            SetF(KeyYearTaxPaid, gs.yearTaxPaidWan);

            // CROSS（Phase 3 Week 11）：CEO 决策会议历史 + 补条件表单
            SetS(KeyDecision, JsonUtility.ToJson(new DecisionSave
            {
                state = gs.decision ?? new DecisionState()
            }));

            if (FleetManager.Instance != null)
                SetS(KeyFleet, FleetManager.Instance.ToJson());
            else
            {
                SetS(KeyFleet, JsonUtility.ToJson(new FleetListSave
                {
                    items = gs.fleet != null ? gs.fleet.ToArray() : Array.Empty<Aircraft>(),
                    nextRegistrationNumber = gs.nextRegistrationNumber
                }));
            }

            SetS(KeyActiveRoutes, JsonUtility.ToJson(new RouteListSave
            {
                items = gs.activeRoutes != null ? gs.activeRoutes.ToArray() : Array.Empty<RouteData>()
            }));
            SetS(KeyFlightRings, JsonUtility.ToJson(new FlightRingListSave
            {
                items = gs.flightRings != null ? gs.flightRings.ToArray() : Array.Empty<FlightRing>()
            }));
            SetS(KeyTakeover, JsonUtility.ToJson(new TakeoverSave
            {
                mode = gs.takeoverMode,
                airlineId = gs.takeoverAirlineId ?? "",
                briefingShown = gs.takeoverBriefingShown,
                crewVacancyPercent = gs.crewVacancyPercent,
                education = gs.ceoEducation ?? "",
                career = gs.ceoCareer ?? "",
                trait = gs.ceoTrait ?? ""
            }));
            SetS(KeyLegacyIssues, JsonUtility.ToJson(new LegacyIssueListSave
            {
                items = gs.legacyIssues != null ? gs.legacyIssues.ToArray() : Array.Empty<LegacyIssue>(),
                airlineId = gs.takeoverAirlineId ?? "",
                briefingShown = gs.takeoverBriefingShown
            }));

            if (AIManager.Instance != null)
                SetS(KeyAiAirlines, AIManager.Instance.ToJson());
            else
            {
                SetS(KeyAiAirlines, JsonUtility.ToJson(new AIAirlineListSave
                {
                    items = gs.aiAirlines != null ? gs.aiAirlines.ToArray() : Array.Empty<AIAirline>(),
                    playerMarketShare = gs.playerMarketShare,
                    brandAwareness = gs.brandAwareness,
                    codeShareLoadBonus = gs.codeShareLoadBonus,
                    codeShareActive = gs.codeShareActive,
                    codeSharePartnerId = gs.codeSharePartnerId,
                    lastCodeShareYear = gs.lastCodeShareYear,
                    pendingCodeShareFromId = gs.pendingCodeShareFromId,
                    lastCompetitionLog = gs.lastCompetitionLog,
                    playerShareHistory = gs.playerShareHistory != null ? gs.playerShareHistory.ToArray() : Array.Empty<float>(),
                    nextAiSpawnYear = gs.nextAiSpawnYear
                }));
            }

            if (RandomEventManager.Instance != null)
                SetS(KeyRandomEvents, RandomEventManager.Instance.ToJson());
            else
            {
                SetS(KeyRandomEvents, JsonUtility.ToJson(new RandomEventsSave
                {
                    oneShotFiredIds = gs.randomEventOneShots != null ? gs.randomEventOneShots.ToArray() : Array.Empty<string>(),
                    log = gs.randomEventLog != null ? gs.randomEventLog.ToArray() : Array.Empty<RandomEventLogEntry>(),
                    pendingChainEventId = gs.pendingChainEventId,
                    pendingChainQuarters = gs.pendingChainQuarters,
                    lastTriggeredSummary = gs.lastRandomEventSummary ?? ""
                }));
            }

            // Phase 13：宏观经济存档
            var gsMacro = GameState.Instance;
            SetF(KeyMacroEconomy + "_GDP", gsMacro != null ? gsMacro.gdpGrowthRate : 3f);
            SetS(KeyMacroEconomy + "_Phase", gsMacro != null ? gsMacro.economicPhase : "boom");
            SetI(KeyMacroEconomy + "_Duration", gsMacro != null ? gsMacro.phaseDuration : 0);

            // P3 体验收口：消息中心 / 准点率连续低季度
            SetS(KeyMessages, JsonUtility.ToJson(new MessageSave
            {
                items = AirlineCEO.Logic.MessageCenter.Snapshot()
            }));
            SetI(KeyOnTimeLowQuarters, gs.onTimeLowQuarters);
            SetS(KeyBoardMemberIds, JsonUtility.ToJson(new StringListSave
            {
                items = gs.boardMemberIds != null ? gs.boardMemberIds.ToArray() : Array.Empty<string>()
            }));
            SetS(KeyInbox, JsonUtility.ToJson(new InboxMailListSave
            {
                items = gs.inbox != null ? gs.inbox.ToArray() : Array.Empty<InboxMail>()
            }));
            SetS(KeyPendingInbox, JsonUtility.ToJson(new PendingInboxListSave
            {
                items = gs.pendingInbox != null ? gs.pendingInbox.ToArray() : Array.Empty<PendingInboxMail>()
            }));

            // P2 无痕：全球航空动态消息流 + 稳定性计数
            SetS(KeyDynamicFeed, JsonUtility.ToJson(new FeedSave
            {
                items = gs.dynamicFeed != null ? gs.dynamicFeed : new System.Collections.Generic.List<AirlineCEO.Data.DynamicFeedItem>()
            }));
            SetS(KeyFeedMeta, JsonUtility.ToJson(new FeedMetaSave
            {
                profitStreak = gs.profitStreak,
                oilStableQuarters = gs.oilStableQuarters,
                quietQuarters = gs.quietQuarters
            }));
            SetS(KeyExternalEvents, JsonUtility.ToJson(ExternalEventManager.Snapshot(gs)));

            PlayerPrefs.Save();

            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            // 顶栏空间有限：不写长白话跑道文案（完整说明在现金油量表）
            LastStatus = $"已存档 F5 · 现金 {gs.cash:F0} · {gs.DateLabel} · 跑道约 {runway:F1} 月";
            Debug.Log($"[SaveManager] {LastStatus} · {CashRunnelCalculator.FormatPlayerLabel(runway)}");
            gs.NotifyChanged();
        }

        /// <summary>
        /// 季度结束自动存档（GDB 31.1）。
        /// 铁人模式：仅主存档实时覆盖，不生成滚动节点；普通模式：额外写滚动 12 节点。
        /// </summary>
        public void AutoSave()
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                LastStatus = "自动存档失败：无 GameState";
                Debug.LogWarning($"[SaveManager] {LastStatus}");
                return;
            }

            Save(); // 完整写主存档（含 ironmanModeActive）

            if (gs.ironmanModeActive)
            {
                LastStatus = $"铁人模式已自动存档（实时覆盖）· {gs.DateLabel}";
            }
            else
            {
                int slot = SaveSlotManager.WriteRollingAutoSlot();
                LastStatus = $"已自动存档（滚动节点 {slot + 1}/12）· {gs.DateLabel}";
            }

            Debug.Log($"[SaveManager] {LastStatus}");
        }

        /// <summary>
        /// 旧档迁移（接管局机组口径修复，2026-10-08）：接管前的存档里，航司的机组从未入模，
        /// 于是 8 条已运营航线被当成「一架机组都没有」，无条件吃满 0.85 折损，
        /// 而叙事层写着「机组人员短缺 X%」。此处按 crewVacancyPercent 补建在册机组。
        ///
        /// 判据刻意保守：仅当「接管局 + 在役机队 &gt;0 + 声明了缺员率 + 运行序列一个人都没有」时才补。
        /// 只要玩家在旧档里通过任意渠道招过哪怕一名机组，就不补——尊重既有投入，
        /// 避免在玩家头上凭空多出 68 个飞行员。
        /// </summary>
        static void MigrateInheritedCrew(GameState gs)
        {
            if (gs == null || !gs.takeoverMode) return;
            if (gs.crewVacancyPercent <= 0) return;
            if (gs.staffPool == null || gs.staffPool.Count == 0) return;
            gs.GetFleetStaffing(out int activeFleet, out _, out _);
            if (activeFleet <= 0) return;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p != null && p.isOperational && !p.onMarket) return;   // 已有运行序列人员 → 不动
            }
            int seeded = AirlineCEO.Logic.PersonnelGenerator.SeedInheritedCrew(gs, gs.crewVacancyPercent);
            if (seeded > 0)
                Debug.Log($"[SaveManager] 接管局旧档迁移：按缺员 {gs.crewVacancyPercent}% 补建在册机组 {seeded} 人");
        }

        public void Load()
        {
            if (!HasKey(KeyCash))
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

            gs.simDay = Mathf.Clamp(GetI(KeySimDay, 1), 1, GameState.DaysPerQuarter);
            gs.simHour = Mathf.Clamp(GetI(KeySimHour, 0), 0, 23);
            gs.simMinute = Mathf.Clamp(GetI(KeySimMinute, 0), 0, 59);
            int wy = GetI(KeyWallYear, 0);
            if (wy > 0)
            {
                gs.wallYear = wy;
                gs.wallMonth = Mathf.Clamp(GetI(KeyWallMonth, 1), 1, 12);
                int dim = DateTime.DaysInMonth(gs.wallYear, gs.wallMonth);
                gs.wallDay = Mathf.Clamp(GetI(KeyWallDay, 1), 1, dim);
            }
            // 旧档没有公历字段：只补日期，时分沿用上面读出的存档值
            else if (gs.wallYear <= 0)
            {
                var utc = DateTime.UtcNow;
                gs.wallYear = utc.Year;
                gs.wallMonth = utc.Month;
                gs.wallDay = utc.Day;
            }
            gs.SetFromSave(
                GetF(KeyCash, 0f),
                GetI(KeyYear, 1),
                GetI(KeyQuarter, 1),
                GetF(KeySatisfaction, 50f),
                GetI(KeyFirstFlight, 0) == 1);
            if (TimeManager.Instance != null)
                TimeManager.Instance.BeginPlaySession();
            else
            {
                gs.speedLevel = GameSpeed.L1;
                GameSpeed.Set(GameSpeed.L1);
            }

            gs.companyNameZh = GetS(KeyCompanyName, gs.companyNameZh);
            gs.companyNameEn = GetS(KeyCompanyNameEn, gs.companyNameEn);
            gs.ceoDisplayName = GetS(KeyCeoName, gs.ceoDisplayName);
            gs.companyIcao = GetS(KeyCompanyIcao, gs.companyIcao);
            gs.companyIata = GetS(KeyCompanyIata, gs.companyIata);
            gs.hubIata = GetS(KeyHubIata, gs.hubIata);
            gs.companyBases = new List<string>();
            string basesJson = GetS(KeyCompanyBases, "");
            if (!string.IsNullOrEmpty(basesJson))
            {
                try
                {
                    var blob = JsonUtility.FromJson<StringListSave>(basesJson);
                    if (blob?.items != null) gs.companyBases.AddRange(blob.items);
                }
                catch (Exception e) { Debug.LogWarning("[SaveManager] companyBases 反序列化失败：" + e.Message); }
            }
            gs.advisorTutorialStep = GetI(KeyAdvisorTutStep, gs.advisorTutorialStep);
            gs.advisorTutorialMailLeft = GetI(KeyAdvisorTutMail, 0) == 1;
            gs.advisorTutorialDeferred = GetI(KeyAdvisorTutDeferred, 0) == 1;
            gs.advisorTutorialCompleted = GetI(KeyAdvisorTutDone, 0) == 1;
            gs.advisorTutorialAwaitPage = GetI(KeyAdvisorTutAwait, 0);
            gs.difficulty = (DifficultyMode)GetI(KeyDifficulty, (int)DifficultyMode.Normal);
            gs.gameOver = GetI(KeyGameOver, 0) == 1;
            gs.gameOverReason = GetS(KeyGameOverReason, "");
            gs.ceoBirthYear = GetI(KeyCeoBirthYear, 1985);
            string dossierJson = GetS(KeyCeoDossier, "");
            if (!string.IsNullOrEmpty(dossierJson))
            {
                try
                {
                    var d = JsonUtility.FromJson<CeoDossierSave>(dossierJson);
                    if (d != null)
                    {
                        gs.ceoGender = string.IsNullOrEmpty(d.gender) ? gs.ceoGender : d.gender;
                        gs.ceoNationality = string.IsNullOrEmpty(d.nationality) ? gs.ceoNationality : d.nationality;
                        gs.ceoContractYears = d.contractYears > 0 ? d.contractYears : gs.ceoContractYears;
                        gs.ceoContractStartYear = d.contractStartYear > 0 ? d.contractStartYear : gs.ceoContractStartYear;
                        gs.ceoWeeklyPayWan = d.weeklyPayWan > 0f ? d.weeklyPayWan : gs.ceoWeeklyPayWan;
                        gs.ceoBonusWan = d.bonusWan;
                    }
                }
                catch (Exception e) { Debug.LogWarning("[SaveManager] CEO 资料反序列化失败：" + e.Message); }
            }
            gs.EnsureCeoContractDefaults();
            gs.gameOverVictory = GetI(KeyGameOverVictory, 0) == 1;
            gs.unlockedLegendMode = PlayerPrefs.GetInt(KeyLegendMode, 0) == 1;
            gs.unlockedNewGamePlus = PlayerPrefs.GetInt(KeyNewGamePlus, 0) == 1;
            gs.unlockedFastMode = PlayerPrefs.GetInt(KeyFastMode, 0) == 1;
            gs.lastLegacyScore = GetF(KeyLegacyScore, 0f);
            gs.lastLegacyGrade = GetS(KeyLegacyGrade, "");
            gs.activeMode = GetI(KeyActiveMode, 0);
            gs.legendModeActive = GetI(KeyLegendActive, 0) == 1;
            gs.fastModeActive = GetI(KeyFastActive, 0) == 1;
            gs.safetyConsecutiveDQuarters = GetI(KeySafetyDQuarters, 0);
            gs.majorCrashOccurred = GetI(KeyMajorCrash, 0) == 1;
            gs.lastDefeatKind = GetI(KeyLastDefeatKind, 0);
            gs.ironmanModeActive = GetI(KeyIronmanActive, 0) == 1;
            gs.mistakeProtectionRemaining = GetI(KeyMistakeProtectionRemaining, ProgressionGate.MistakeProtectionTotal);
            gs.mistakeProtectionUsedYear = GetI(KeyMistakeProtectionUsedYear, 0);
            gs.targetsFiscalYear = GetI(KeyTargetsYear, gs.year);
            gs.annualGoalsConfirmedYear = GetI(KeyAnnualGoalsConfirmedYear, 0);
            gs.yearNetProfit = GetF(KeyYearNet, 0f);
            gs.liveQuarterIncome = GetF(KeyLiveQIncome, 0f);
            gs.liveQuarterExpense = GetF(KeyLiveQExpense, 0f);
            gs.probationProtectionsUsed = GetI(KeyProbationUsed, 0);
            gs.consecutiveFailedVotes = GetI(KeyFailVotes, 0);
            gs.safetyRating = (SafetyRating)GetI(KeySafetyRating, (int)SafetyRating.B);
            gs.SyncSafetyIndexFromRating();
            gs.hasAOGTriggered = GetI(KeyHasAOGTriggered, 0) == 1;
            gs.aogChoice = GetI(KeyAOGChoice, 0);
            gs.aogAdvisorConsulted = GetI(KeyAOGAdvisor, 0) == 1;
            gs.onTimePercent = GetF(KeyOnTime, 88f);
            gs.hasExpansionProposalTriggered = GetI(KeyHasExpansion, 0) == 1;
            gs.expansionProposalDelayCount = GetI(KeyExpansionDelay, 0);
            gs.expansionAccepted = GetI(KeyExpansionAccepted, 0) == 1;
            gs.expansionRejected = GetI(KeyExpansionRejected, 0) == 1;
            gs.greatRouteUnlocked = GetI(KeyGreatRoute, 0) == 1;
            gs.lastExpansionOfferYear = GetI(KeyLastExpansionYear, 0);

            string json = GetS(KeyTargets, "");
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

            string safetyJson = GetS(KeySafetyLog, "");
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
                DepartmentManager.Instance.FromJson(GetS(KeyDeptHeads, ""));

            gs.staffPool = new System.Collections.Generic.List<PersonnelData>();
            gs.staffBootstrapped = false;
            string staffJson = GetS(KeyStaffPool, "");
            if (!string.IsNullOrEmpty(staffJson))
            {
                try
                {
                    var blob = JsonUtility.FromJson<PersonnelListSave>(staffJson);
                    if (blob?.items != null) gs.staffPool.AddRange(blob.items);
                    gs.staffBootstrapped = blob != null && blob.bootstrapped;
                }
                catch (Exception e) { Debug.LogWarning("[SaveManager] staffPool 反序列化失败：" + e.Message); }
            }
            PersonnelGenerator.EnsurePool(gs);
            MigrateInheritedCrew(gs);
            // 老档迁移：旧档的池子里从来没有渠道候选人（TalentMarket 于 2026-10-11 才存在），
            // 播种一次让老档读进来就有可挑的人；已在池里的新档不会被重复补（全空才补）。
            TalentMarket.EnsureSeeded(gs);
            gs.staffRecruits = new System.Collections.Generic.List<StaffRecruitCase>();
            string recruitJson = GetS(KeyStaffRecruits, "");
            if (!string.IsNullOrEmpty(recruitJson))
            {
                try
                {
                    var rec = JsonUtility.FromJson<StaffRecruitListSave>(recruitJson);
                    if (rec?.items != null) gs.staffRecruits.AddRange(rec.items);
                }
                catch (Exception e) { Debug.LogWarning("[SaveManager] staffRecruits 反序列化失败：" + e.Message); }
            }

            // 人力资源（Phase 2 Week 10 · HR）读档。
            // 旧档没有本键 → FromJson 抛异常 / state 为 null → 落到 new HrState()，
            // 再经 EnsureState 跑旧档迁移（legacy laborCostWan → 每机费率、legacy totalHeadcount → 每机人数）。
            try
            {
                var hrBlob = JsonUtility.FromJson<HrSave>(GetS(KeyHr, ""));
                gs.hr = hrBlob?.state;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveManager] hr 反序列化失败：" + e.Message);
                gs.hr = null;
            }
            HrManager.EnsureState(gs);
            if (DepartmentManager.Instance != null)
                DepartmentManager.Instance.RecalcAllEfficiencies();
            gs.usedMarket = new List<UsedListing>();
            gs.usedMarketYear = 0;
            gs.usedMarketQuarter = 0;
            string usedJson = GetS(KeyUsedMarket, "");
            if (!string.IsNullOrEmpty(usedJson))
            {
                try
                {
                    var um = JsonUtility.FromJson<UsedMarketSave>(usedJson);
                    if (um?.items != null) gs.usedMarket.AddRange(um.items);
                    gs.usedMarketYear = um != null ? um.year : 0;
                    gs.usedMarketQuarter = um != null ? um.quarter : 0;
                }
                catch (Exception e) { Debug.LogWarning("[SaveManager] usedMarket 反序列化失败：" + e.Message); }
            }
            UsedAircraftMarket.Ensure(gs);

            gs.cityEconomyDeltas = new List<CityEconomyDelta>();
            gs.cityEconomyRevision = 0;
            string cityJson = GetS(KeyCityEconomy, "");
            if (!string.IsNullOrEmpty(cityJson))
            {
                try
                {
                    var ce = JsonUtility.FromJson<CityEconomySave>(cityJson);
                    if (ce?.items != null) gs.cityEconomyDeltas.AddRange(ce.items);
                    gs.cityEconomyRevision = ce != null ? ce.tableRevision : 0;
                }
                catch (Exception e) { Debug.LogWarning("[SaveManager] cityEconomy 反序列化失败：" + e.Message); }
            }

            gs.fuelPricePerTon = GetF(KeyFuelPrice, gs.fuelPricePerTon);
            if (HedgeManager.Instance != null)
                HedgeManager.Instance.ContractsFromJson(GetS(KeyHedgeContracts, ""));
            else
            {
                try
                {
                    var blob = JsonUtility.FromJson<HedgeListSave>(GetS(KeyHedgeContracts, ""));
                    gs.hedgeContracts = blob?.items != null
                        ? new System.Collections.Generic.List<HedgeContract>(blob.items)
                        : new System.Collections.Generic.List<HedgeContract>();
                }
                catch { gs.hedgeContracts = new System.Collections.Generic.List<HedgeContract>(); }
            }

            if (LoanManager.Instance != null)
            {
                LoanManager.Instance.LoansFromJson(GetS(KeyActiveLoans, ""));
                LoanManager.Instance.consecutiveDefaults = GetI(KeyLoanDefaults, 0);
            }

            try
            {
                var hist = JsonUtility.FromJson<FloatListSave>(GetS(KeyFuelHistory, ""));
                gs.fuelPriceHistory = hist?.items != null
                    ? new System.Collections.Generic.List<float>(hist.items)
                    : new System.Collections.Generic.List<float> { gs.fuelPricePerTon };
            }
            catch
            {
                gs.fuelPriceHistory = new System.Collections.Generic.List<float> { gs.fuelPricePerTon };
            }

            gs.hasSeenHedgeTutorial = GetI(KeyHasSeenHedgeTutorial, 0) == 1;

            // FIN（Phase 2 Week 5）读档
            gs.budgetFiscalYear = GetI(KeyBudgetYear, gs.year);
            gs.budgetSubmitted = GetI(KeyBudgetSubmitted, 0) == 1;
            gs.budgetApproved = GetI(KeyBudgetApproved, 0) == 1;
            if (FinanceManager.Instance != null)
            {
                FinanceManager.Instance.BudgetFromJson(GetS(KeyBudget, ""));
                FinanceManager.Instance.CarbonFromJson(GetS(KeyCarbon, ""));
                FinanceManager.Instance.RestructuringFromJson(GetS(KeyRestructuring, ""));
            }
            gs.pdpPayableWan = GetF(KeyPdpPayable, 0f);
            gs.fxHedgeRatio = GetF(KeyFxHedgeRatio, 0f);
            gs.usdCny = GetF(KeyUsdCny, AirlineCEO.Logic.FxEconomics.DefaultUsdCny);
            gs.cumulativeFxPnlWan = GetF(KeyCumFxPnl, 0f);
            try
            {
                var fxHist = JsonUtility.FromJson<FloatListSave>(GetS(KeyUsdCnyHistory, ""));
                gs.usdCnyHistory = fxHist?.items != null && fxHist.items.Length > 0
                    ? new System.Collections.Generic.List<float>(fxHist.items)
                    : new System.Collections.Generic.List<float> { gs.usdCny };
            }
            catch
            {
                gs.usdCnyHistory = new System.Collections.Generic.List<float> { gs.usdCny };
            }

            // MKT（Phase 2 Week 7）读档
            try
            {
                var mktBlob = JsonUtility.FromJson<MktSave>(GetS(KeyMkt, ""));
                gs.mkt = mktBlob?.state ?? new MktState();
            }
            catch
            {
                gs.mkt = new MktState();
            }

            // 货运（GDB 14.6 · P2 长线运营）读档
            try
            {
                var cargoBlob = JsonUtility.FromJson<CargoSave>(GetS(KeyCargo, ""));
                gs.cargo = cargoBlob?.state ?? new CargoState();
            }
            catch
            {
                gs.cargo = new CargoState();
            }

            // 非航收入与临空商业（GDB 26.2 · P2 长线运营）读档
            try
            {
                var commerceBlob = JsonUtility.FromJson<AirportCommerceSave>(GetS(KeyCommerce, ""));
                gs.commerce = commerceBlob?.state ?? new AirportCommerceState();
            }
            catch
            {
                gs.commerce = new AirportCommerceState();
            }

            // 财务细分（GDB 第15章 · P2 长线运营）读档
            try
            {
                var subBlob = JsonUtility.FromJson<SubsidyListSave>(GetS(KeySubsidies, ""));
                gs.routeSubsidies = subBlob?.items != null ? new List<RouteSubsidy>(subBlob.items) : new List<RouteSubsidy>();
            }
            catch { gs.routeSubsidies = new List<RouteSubsidy>(); }
            try
            {
                var ordBlob = JsonUtility.FromJson<AircraftOrderListSave>(GetS(KeyAircraftOrders, ""));
                gs.aircraftOrders = ordBlob?.items != null ? new List<AircraftOrder>(ordBlob.items) : new List<AircraftOrder>();
            }
            catch { gs.aircraftOrders = new List<AircraftOrder>(); }

            // 过渡业务（开局三架飞机的盈利设计）读档
            try
            {
                var trBlob = JsonUtility.FromJson<TransitionSave>(GetS(KeyTransition, ""));
                gs.transitionContracts = trBlob?.contracts != null
                    ? new List<TransitionContract>(trBlob.contracts) : new List<TransitionContract>();
                gs.transitionOffers = trBlob?.offers != null
                    ? new List<TransitionOffer>(trBlob.offers) : new List<TransitionOffer>();
                gs.transitionCrewMailSent = trBlob != null && trBlob.crewMailSent;
                gs.transitionInstructorMailSent = trBlob != null && trBlob.instructorMailSent;
            }
            catch
            {
                gs.transitionContracts = new List<TransitionContract>();
                gs.transitionOffers = new List<TransitionOffer>();
            }
            gs.transitionIntroSent = GetI(KeyTransitionIntro, 0) == 1;
            gs.liveQuarterTransitionIncome = GetF(KeyTransitionQIncome, 0f);
            try
            {
                var insBlob = JsonUtility.FromJson<InsuranceSave>(GetS(KeyInsurance, ""));
                gs.insuranceCoverage = (InsuranceCoverage)(insBlob != null ? insBlob.coverage : 0);
                gs.cumulativeInsurancePremiumWan = insBlob?.cumulativePremiumWan ?? 0f;
                gs.cumulativeInsuranceClaimWan = insBlob?.cumulativeClaimWan ?? 0f;
            }
            catch { gs.insuranceCoverage = InsuranceCoverage.Full; }
            try
            {
                var fpBlob = JsonUtility.FromJson<FuelProcurementSave>(GetS(KeyFuelProc, ""));
                gs.fuelProcurement = fpBlob?.state ?? new FuelProcurementState();
            }
            catch { gs.fuelProcurement = new FuelProcurementState(); }
            try
            {
                var ccBlob = JsonUtility.FromJson<CarbonComplianceSave>(GetS(KeyCarbonComp, ""));
                gs.carbonCompliance = ccBlob?.state ?? new CarbonComplianceState();
            }
            catch { gs.carbonCompliance = new CarbonComplianceState(); }
            gs.cumulativeTaxPaidWan = GetF(KeyCumTaxPaid, 0f);
            gs.yearTaxPaidWan = GetF(KeyYearTaxPaid, 0f);

            // CROSS（Phase 3 Week 11）读档：决策会议历史 + 补条件表单
            try
            {
                var decBlob = JsonUtility.FromJson<DecisionSave>(GetS(KeyDecision, ""));
                gs.decision = decBlob?.state ?? new DecisionState();
            }
            catch
            {
                gs.decision = new DecisionState();
            }

            if (FleetManager.Instance != null)
                FleetManager.Instance.FromJson(GetS(KeyFleet, ""));
            else
            {
                try
                {
                    var blob = JsonUtility.FromJson<FleetListSave>(GetS(KeyFleet, ""));
                    gs.fleet = blob?.items != null
                        ? new System.Collections.Generic.List<Aircraft>(blob.items)
                        : new System.Collections.Generic.List<Aircraft>();
                    gs.nextRegistrationNumber = blob != null && blob.nextRegistrationNumber > 0
                        ? blob.nextRegistrationNumber
                        : 1;
                }
                catch
                {
                    gs.fleet = new System.Collections.Generic.List<Aircraft>();
                    gs.nextRegistrationNumber = 1;
                }
            }

            try
            {
                var rblob = JsonUtility.FromJson<RouteListSave>(GetS(KeyActiveRoutes, ""));
                gs.activeRoutes = rblob?.items != null
                    ? new System.Collections.Generic.List<RouteData>(rblob.items)
                    : new System.Collections.Generic.List<RouteData>();
                if (gs.activeRoutes != null)
                {
                    for (int i = 0; i < gs.activeRoutes.Count; i++)
                    {
                        var r = gs.activeRoutes[i];
                        if (r == null) continue;
                        r.EnsureFlights();
                        r.schedulePending = false;
                    }
                }
            }
            catch
            {
                gs.activeRoutes = new System.Collections.Generic.List<RouteData>();
            }
            try
            {
                // 航班环：运力指令可自动建立航班环，须随档保存，退回呈报时才能撤销
                var fring = JsonUtility.FromJson<FlightRingListSave>(GetS(KeyFlightRings, ""));
                gs.flightRings = fring?.items != null
                    ? new System.Collections.Generic.List<FlightRing>(fring.items)
                    : new System.Collections.Generic.List<FlightRing>();
            }
            catch
            {
                gs.flightRings = new System.Collections.Generic.List<FlightRing>();
            }
            try
            {
                var tblob = JsonUtility.FromJson<TakeoverSave>(GetS(KeyTakeover, ""));
                gs.takeoverMode = tblob != null && tblob.mode;
                gs.takeoverAirlineId = tblob?.airlineId ?? "";
                gs.takeoverBriefingShown = tblob != null && tblob.briefingShown;
                gs.crewVacancyPercent = tblob != null ? tblob.crewVacancyPercent : 0;
                gs.ceoEducation = tblob?.education ?? "";
                gs.ceoCareer = tblob?.career ?? "";
                gs.ceoTrait = tblob?.trait ?? "";
            }
            catch
            {
                gs.takeoverMode = false;
            }
            try
            {
                var iblob = JsonUtility.FromJson<LegacyIssueListSave>(GetS(KeyLegacyIssues, ""));
                gs.legacyIssues = iblob?.items != null
                    ? new System.Collections.Generic.List<LegacyIssue>(iblob.items)
                    : new System.Collections.Generic.List<LegacyIssue>();
            }
            catch
            {
                gs.legacyIssues = new System.Collections.Generic.List<LegacyIssue>();
            }

            if (AIManager.Instance != null)
                AIManager.Instance.FromJson(GetS(KeyAiAirlines, ""));
            else
            {
                try
                {
                    var blob = JsonUtility.FromJson<AIAirlineListSave>(GetS(KeyAiAirlines, ""));
                    gs.aiAirlines = blob?.items != null
                        ? new System.Collections.Generic.List<AIAirline>(blob.items)
                        : new System.Collections.Generic.List<AIAirline>();
                    gs.playerMarketShare = blob != null ? blob.playerMarketShare : 0f;
                    gs.brandAwareness = blob != null ? blob.brandAwareness : 70f;
                    gs.codeShareLoadBonus = blob != null ? blob.codeShareLoadBonus : 0f;
                    gs.codeShareActive = blob != null && blob.codeShareActive;
                    gs.codeSharePartnerId = blob?.codeSharePartnerId;
                    gs.lastCodeShareYear = blob != null ? blob.lastCodeShareYear : 0;
                    gs.pendingCodeShareFromId = blob?.pendingCodeShareFromId;
                    gs.lastCompetitionLog = blob?.lastCompetitionLog;
                    gs.nextAiSpawnYear = blob != null ? blob.nextAiSpawnYear : 0;
                    gs.playerShareHistory = blob?.playerShareHistory != null
                        ? new System.Collections.Generic.List<float>(blob.playerShareHistory)
                        : new System.Collections.Generic.List<float>();
                }
                catch
                {
                    gs.aiAirlines = new System.Collections.Generic.List<AIAirline>();
                }
            }

            if (RandomEventManager.Instance != null)
                RandomEventManager.Instance.FromJson(GetS(KeyRandomEvents, ""));
            else
            {
                try
                {
                    var blob = JsonUtility.FromJson<RandomEventsSave>(GetS(KeyRandomEvents, ""));
                    gs.randomEventOneShots = blob?.oneShotFiredIds != null
                        ? new System.Collections.Generic.List<string>(blob.oneShotFiredIds)
                        : new System.Collections.Generic.List<string>();
                    gs.randomEventLog = blob?.log != null
                        ? new System.Collections.Generic.List<RandomEventLogEntry>(blob.log)
                        : new System.Collections.Generic.List<RandomEventLogEntry>();
                    gs.pendingChainEventId = blob?.pendingChainEventId;
                    gs.pendingChainQuarters = blob != null ? blob.pendingChainQuarters : 0;
                    gs.lastRandomEventSummary = blob?.lastTriggeredSummary ?? "";
                }
                catch
                {
                    gs.randomEventLog = new System.Collections.Generic.List<RandomEventLogEntry>();
                    gs.randomEventOneShots = new System.Collections.Generic.List<string>();
                }
            }

            // Phase 13：宏观经济读档
            gs.gdpGrowthRate = GetF(KeyMacroEconomy + "_GDP", 3f);
            gs.economicPhase = GetS(KeyMacroEconomy + "_Phase", "boom");
            gs.phaseDuration = GetI(KeyMacroEconomy + "_Duration", 0);

            // P3 体验收口：消息中心 / 准点率连续低季度
            gs.onTimeLowQuarters = GetI(KeyOnTimeLowQuarters, 0);

            string boardIdsJson = GetS(KeyBoardMemberIds, "");
            gs.boardMemberIds = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(boardIdsJson))
            {
                try
                {
                    var ids = JsonUtility.FromJson<StringListSave>(boardIdsJson);
                    if (ids?.items != null)
                        gs.boardMemberIds.AddRange(ids.items);
                }
                catch (System.Exception e) { Debug.LogWarning($"[SaveManager] boardMemberIds 反序列化失败：{e.Message}"); }
            }
            gs.inbox = new System.Collections.Generic.List<InboxMail>();
            string inboxJson = GetS(KeyInbox, "");
            if (!string.IsNullOrEmpty(inboxJson))
            {
                try
                {
                    var mails = JsonUtility.FromJson<InboxMailListSave>(inboxJson);
                    if (mails?.items != null) gs.inbox.AddRange(mails.items);
                }
                catch (System.Exception e) { Debug.LogWarning($"[SaveManager] inbox 反序列化失败：{e.Message}"); }
            }
            gs.pendingInbox = new System.Collections.Generic.List<PendingInboxMail>();
            string pendingJson = GetS(KeyPendingInbox, "");
            if (!string.IsNullOrEmpty(pendingJson))
            {
                try
                {
                    var pending = JsonUtility.FromJson<PendingInboxListSave>(pendingJson);
                    if (pending?.items != null) gs.pendingInbox.AddRange(pending.items);
                }
                catch (System.Exception e) { Debug.LogWarning($"[SaveManager] pendingInbox 反序列化失败：{e.Message}"); }
            }
            InboxManager.BackfillReceiptRouteRefs(gs);
            string msgJson = GetS(KeyMessages, "");
            if (!string.IsNullOrEmpty(msgJson))
            {
                try
                {
                    var msgs = JsonUtility.FromJson<MessageSave>(msgJson);
                    AirlineCEO.Logic.MessageCenter.Load(msgs?.items);
                }
                catch (System.Exception e) { Debug.LogWarning($"[SaveManager] 消息中心反序列化失败：{e.Message}"); }
            }

            // P2 无痕：全球航空动态消息流 + 稳定性计数
            gs.dynamicFeed = new System.Collections.Generic.List<AirlineCEO.Data.DynamicFeedItem>();
            string feedJson = GetS(KeyDynamicFeed, "");
            if (!string.IsNullOrEmpty(feedJson))
            {
                try
                {
                    var feed = JsonUtility.FromJson<FeedSave>(feedJson);
                    if (feed?.items != null) gs.dynamicFeed.AddRange(feed.items);
                }
                catch (System.Exception e) { Debug.LogWarning($"[SaveManager] 动态消息流反序列化失败：{e.Message}"); }
            }
            string feedMetaJson = GetS(KeyFeedMeta, "");
            gs.profitStreak = 0; gs.oilStableQuarters = 0; gs.quietQuarters = 0;
            if (!string.IsNullOrEmpty(feedMetaJson))
            {
                try
                {
                    var meta = JsonUtility.FromJson<FeedMetaSave>(feedMetaJson);
                    if (meta != null)
                    {
                        gs.profitStreak = meta.profitStreak;
                        gs.oilStableQuarters = meta.oilStableQuarters;
                        gs.quietQuarters = meta.quietQuarters;
                    }
                }
                catch (System.Exception e) { Debug.LogWarning($"[SaveManager] 动态消息流元数据反序列化失败：{e.Message}"); }
            }
            int maxFeedStamp = 0;
            for (int i = 0; i < gs.dynamicFeed.Count; i++)
                if ((int)gs.dynamicFeed[i].stamp > maxFeedStamp) maxFeedStamp = (int)gs.dynamicFeed[i].stamp;
            AirlineCEO.Systems.DynamicFeedManager.RestoreStamp(maxFeedStamp);

            string extJson = GetS(KeyExternalEvents, "");
            if (!string.IsNullOrEmpty(extJson))
            {
                try
                {
                    ExternalEventManager.Restore(gs, JsonUtility.FromJson<AirlineCEO.Data.ExternalEventListSave>(extJson));
                }
                catch (System.Exception e) { Debug.LogWarning($"[SaveManager] 外部事件队列反序列化失败：{e.Message}"); }
            }
            else
            {
                gs.externalEvents = new System.Collections.Generic.List<AirlineCEO.Data.ExternalEvent>();
                gs.externalFreezeRegion = "";
                gs.externalFreezeUntilTicks = 0;
            }

            // ★航班号迁移（2026-10-13 第六次报障）：旧档的航线基号是连号分配的
            // （`{航司码}{100 + i}`），而回程号 = 去程号 + 1、每个波次再 +2 ——
            // 于是第 i 条线要占 8 个号，第 i+1 条线的基号当场落进去，两条线抢同一个航班号。
            // 这里按块重编一次（幂等：已经互不撞号的档原样不动、不写日志），并连带重绑时刻槽。
            int renumbered = AirlineCEO.Logic.FlightNumberLedger.Normalize(gs);
            if (renumbered > 0)
                Debug.Log($"[SaveManager] 航班号迁移：{renumbered} 条航线重编（旧档连号分配会互撞）");

            LastStatus = gs.gameOver
                ? (gs.gameOverVictory
                    ? $"已读档 F9 · 已通关（遗产 {gs.lastLegacyGrade}） · {gs.DateLabel}"
                    : $"已读档 F9 · 游戏已结束 · {gs.DateLabel}")
                : $"已读档 F9 · 现金 {gs.cash:F0} · {gs.DateLabel}";
            Debug.Log($"[SaveManager] {LastStatus}");
            AirlineCEO.UI.ChineseFontProvider.RefreshAfterLoad();
            gs.NotifyChanged();
        }
    }
}
