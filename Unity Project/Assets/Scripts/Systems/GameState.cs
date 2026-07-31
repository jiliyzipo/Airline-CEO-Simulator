using System;
using System.Collections.Generic;
using AirlineCEO.Data;
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

        /// <summary>净月烧钱（开销 − 航线月收入，下限 0 便于跑道计算）。</summary>
        public float EffectiveMonthlyBurn
        {
            get
            {
                float net = monthlyBurnRate - monthlyRevenue;
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
