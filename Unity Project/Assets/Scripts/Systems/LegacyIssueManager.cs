using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Systems
{
    public static class LegacyIssueManager
    {
        public static void Ensure(GameState gs)
        {
            if (gs == null) return;
            if (gs.legacyIssues == null) gs.legacyIssues = new List<LegacyIssue>();
        }

        public static List<LegacyIssue> SortedOpen(GameState gs)
        {
            Ensure(gs);
            var list = new List<LegacyIssue>();
            for (int i = 0; i < gs.legacyIssues.Count; i++)
            {
                var x = gs.legacyIssues[i];
                if (x != null && !x.isResolved) list.Add(x);
            }
            list.Sort((a, b) => b.urgency.CompareTo(a.urgency));
            return list;
        }

        public static LegacyIssue Next(GameState gs)
        {
            var open = SortedOpen(gs);
            return open.Count > 0 ? open[0] : null;
        }

        public static bool MarkResolved(GameState gs, string id, out string message)
        {
            message = "";
            Ensure(gs);
            for (int i = 0; i < gs.legacyIssues.Count; i++)
            {
                var x = gs.legacyIssues[i];
                if (x == null || x.id != id) continue;
                if (x.isResolved)
                {
                    message = "该项已处理。";
                    return false;
                }
                x.isResolved = true;
                gs.cash += 8f;
                gs.AddDecisionLog($"遗留问题已处理：{x.title}（奖励现金 +8 万）");
                gs.NotifyChanged();
                message = $"已记录处理「{x.title}」。现金 +8 万。";
                return true;
            }
            message = "未找到该遗留问题。";
            return false;
        }

        public static string AdvisorText(GameState gs)
        {
            var n = Next(gs);
            if (n == null)
                return "遗留问题已清完。日常经营与收件箱请示由各部门继续呈报。";
            return $"当前最急：【{LegacyIssue.UrgencyLabel(n.urgency)}】{n.title}\n{n.description}\n建议前往对应模块处理。";
        }
    }
}
