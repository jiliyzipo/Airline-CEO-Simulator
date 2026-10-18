using System;
using UnityEngine;

namespace AirlineCEO.AI
{
    /// <summary>DeepSeek / 本地顾问统一响应结构（任务书 5.2）。</summary>
    [Serializable]
    public class AdvisorResponse
    {
        public string advisor = "战略顾问";
        public string emotion = "平和";
        public string message = "";
        public string suggestedAction = "none";
        public AdvisorDataPayload data = new AdvisorDataPayload();

        public bool fromFallback;
        public string errorHint;

        public string ToDisplayText()
        {
            string prefix = string.IsNullOrEmpty(advisor) ? "顾问" : advisor;
            string mood = string.IsNullOrEmpty(emotion) ? "" : $"（{emotion}）";
            string fallback = fromFallback ? "\n[本地话术]" : "";
            return $"{prefix}{mood}：{message}{fallback}";
        }
    }

    [Serializable]
    public class AdvisorDataPayload
    {
        public float profit;
        public string risk = "medium";
        public string origin;       // 可选：建议航线起点
        public string destination;  // 可选：建议航线终点
        public string stopover;     // 可选：经停
    }

    /// <summary>玩家指令枚举（任务书：把文字转换成固定指令）。</summary>
    public enum SuggestedAction
    {
        None,
        OpenRoute,
        Delay,
        Reject,
        CheckCash,
        FirstFlight
    }

    public static class SuggestedActionParser
    {
        public static SuggestedAction Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return SuggestedAction.None;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "open_route": return SuggestedAction.OpenRoute;
                case "delay": return SuggestedAction.Delay;
                case "reject": return SuggestedAction.Reject;
                case "check_cash": return SuggestedAction.CheckCash;
                case "first_flight": return SuggestedAction.FirstFlight;
                case "none": return SuggestedAction.None;
                default: return SuggestedAction.None;
            }
        }
    }
}
