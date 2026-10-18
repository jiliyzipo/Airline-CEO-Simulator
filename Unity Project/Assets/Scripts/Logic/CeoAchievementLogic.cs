using System.Collections.Generic;
using System.Text;

namespace AirlineCEO.Logic
{
    public static class CeoAchievementLogic
    {
        public static string FormatHistory(IList<string> decisionLog, int year, int routes)
        {
            var sb = new StringBuilder();
            sb.Append($"第 {year} 财年任职 · 在营航线 {routes} 条");
            if (decisionLog == null || decisionLog.Count == 0)
                return sb.ToString();
            int from = decisionLog.Count - 2;
            if (from < 0) from = 0;
            sb.Append(" · ");
            for (int i = decisionLog.Count - 1; i >= from; i--)
            {
                if (i < decisionLog.Count - 1) sb.Append("；");
                string line = decisionLog[i] ?? "";
                if (line.Length > 28) line = line.Substring(0, 28) + "…";
                sb.Append(line);
            }
            return sb.ToString();
        }

        public static string FormatAchievements(
            bool greatRoute, bool legend, bool ngp, bool fast,
            bool victory, string legacyGrade, int routes)
        {
            var parts = new List<string>();
            if (greatRoute) parts.Add("伟大航路");
            if (victory) parts.Add("光荣退休");
            if (!string.IsNullOrEmpty(legacyGrade)) parts.Add("遗产 " + legacyGrade);
            if (legend) parts.Add("传奇模式解锁");
            if (ngp) parts.Add("新游戏+解锁");
            if (fast) parts.Add("快速模式解锁");
            if (routes >= 3) parts.Add($"航网 {routes} 条");
            if (parts.Count == 0) return "起步经营（开通航线、完成目标后解锁）";
            return string.Join(" · ", parts);
        }
    }
}
