using System;

namespace AirlineCEO.Data
{
    /// <summary>安全评级 A/B/C/D（GDD 7.6 · Phase 7）。</summary>
    public enum SafetyRating
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3
    }

    /// <summary>安全事件严重度。</summary>
    public enum SafetyEventSeverity
    {
        Mild = 0,
        Moderate = 1
    }

    /// <summary>一条安全事件日志。</summary>
    [Serializable]
    public class SafetyEventLog
    {
        public string id;
        public string dateLabel;
        public SafetyEventSeverity severity;
        public string title;
        public string detail;
        public SafetyRating ratingBefore;
        public SafetyRating ratingAfter;
    }
}
