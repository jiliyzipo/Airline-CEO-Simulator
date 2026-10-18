using System;
using UnityEngine;

namespace AirlineCEO.Data
{
    public enum BoardTargetType
    {
        NetProfit,
        LoadFactor,
        Safety,
        RouteExpansion
    }

    /// <summary>董事会年度考核目标（GDD 5.3 / 5.4 · Phase 5）。</summary>
    [Serializable]
    public class BoardTarget
    {
        public string id;
        public BoardTargetType type;
        public string title;
        public float threshold;
        public float currentValue;
        public string unit;
        public bool isGuarantee;
        public bool completed;
        public string sourceNote;
        /// <summary>CEO 目标清单编号（T-01~T-23），保底目标为空。</summary>
        public string targetId;
        /// <summary>目标难度（中/高/极高…），来自 TargetCatalog。</summary>
        public string difficulty;

        public float Progress01
        {
            get
            {
                if (threshold <= 0f) return completed ? 1f : 0f;
                return Mathf.Clamp01(currentValue / threshold);
            }
        }

        public string ProgressLabel =>
            $"{title}（当前 {FormatValue(currentValue)}{unit}）";

        public static string FormatValue(float v)
        {
            if (Mathf.Abs(v % 1f) < 0.05f) return v.ToString("F0");
            return v.ToString("F1");
        }
    }
}
