using AirlineCEO.Logic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AirlineCEO.Systems
{
    /// <summary>
    /// PlayerPrefs 存档：F5 保存，F9 读取。
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        private const string KeyCash = "ACEO_Cash";
        private const string KeyYear = "ACEO_Year";
        private const string KeyQuarter = "ACEO_Quarter";
        private const string KeySatisfaction = "ACEO_BoardSatisfaction";
        private const string KeyFirstFlight = "ACEO_FirstFlight";

        public string LastStatus { get; private set; } = "尚未存档";

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

            LastStatus = $"已读档 F9 · 现金 {gs.cash:F0} · {gs.DateLabel}";
            Debug.Log($"[SaveManager] {LastStatus}");
        }
    }
}
