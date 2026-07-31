using System;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// MVP 全局游戏状态（现金、日期、董事会满意度等）。
    /// </summary>
    public class GameState : MonoBehaviour
    {
        public static GameState Instance { get; private set; }

        [Header("初始状态")]
        public float cash = 500f;              // 万美元
        public float monthlyBurnRate = 80f;    // 万美元/月
        public int year = 1;
        public int quarter = 1;                // 1~4
        public float boardSatisfaction = 50f;  // 0~100
        public bool firstFlightCompleted;

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
