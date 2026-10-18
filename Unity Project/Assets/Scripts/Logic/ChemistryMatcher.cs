using System;
using System.Collections.Generic;
using System.Linq;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 董事会化学反应匹配器（GDD 第 7 章 B-01~B-23）。
    /// 纯静态规则，不依赖 MonoBehaviour。
    /// 返回已触发组合 ID，按优先级排序：冲突 &gt; 隐藏 &gt; 多人(&gt;=4) &gt; 三人 &gt; 双人/单人，最多 6 个。
    /// </summary>
    public static class ChemistryMatcher
    {
        private const int MaxResults = 6;
        private const int MaxNormalCombosPerRole = 2;

        private enum Priority
        {
            Conflict = 1,
            Hidden = 2,
            Multi4Plus = 3,
            Triple = 4,
            DualOrSingle = 5
        }

        private sealed class ChemRule
        {
            public string Id;
            public Priority PriorityLevel;
            public string[] RequiredAll;
            public string[] RequiredAny;
            public string[] Forbidden;
            public string[] SideA;
            public string[] SideB;
            public string[] CountPool;
            public int MinCount;
        }

        private static readonly ChemRule[] Rules =
        {
            // 冲突类
            Make("B-16", Priority.Conflict, all: new[] { "B", "H" }),
            Make("B-17", Priority.Conflict, all: new[] { "C", "K" }),
            Make("B-18", Priority.Conflict, sideA: new[] { "A", "P", "I" }, sideB: new[] { "G", "O" }),
            Make("B-19", Priority.Conflict, sideA: new[] { "D" }, sideB: new[] { "G", "L" }),
            Make("B-20", Priority.Conflict, sideA: new[] { "F" }, sideB: new[] { "G", "B" }),

            // 隐藏类
            Make("B-21", Priority.Hidden, countPool: new[] { "D", "E", "N", "F", "O" }, minCount: 4),
            Make("B-22", Priority.Hidden, all: new[] { "A", "P", "I", "B" }),
            Make("B-23", Priority.Hidden, all: new[] { "C", "L", "M", "Q" }),

            // 多人 ≥4
            Make("B-03", Priority.Multi4Plus, all: new[] { "A", "K", "M", "H" }),
            Make("B-04", Priority.Multi4Plus, all: new[] { "B", "J", "O", "I" }),
            Make("B-13", Priority.Multi4Plus, all: new[] { "B", "G", "J", "K" }),
            Make("B-14", Priority.Multi4Plus, all: new[] { "C", "L", "M", "Q" }),
            Make("B-15", Priority.Multi4Plus, all: new[] { "D", "E", "N", "F", "O" }),

            // 三人
            Make("B-02", Priority.Triple, all: new[] { "P", "I", "C" }),
            Make("B-06", Priority.Triple, all: new[] { "G", "N", "C" }),
            Make("B-07", Priority.Triple, all: new[] { "E", "C", "K" }),
            Make("B-08", Priority.Triple, all: new[] { "D", "E", "F" }),
            Make("B-12", Priority.Triple, all: new[] { "A", "P", "I" }),

            // 双人 / 单人（B-01 采用任务书：含 A 且不含 G）
            Make("B-01", Priority.DualOrSingle, all: new[] { "A" }, forbidden: new[] { "G" }),
            Make("B-05", Priority.DualOrSingle, all: new[] { "L" }),
            Make("B-09", Priority.DualOrSingle, all: new[] { "B", "K" }),
            Make("B-10", Priority.DualOrSingle, all: new[] { "O", "G" }),
            Make("B-11", Priority.DualOrSingle, all: new[] { "H", "Q" }),
        };

        /// <param name="boardIDs">当前董事会成员 ID，如 "A","B","C"</param>
        /// <returns>触发的化学组合 ID，已按优先级排序</returns>
        public static List<string> Match(List<string> boardIDs)
        {
            if (boardIDs == null || boardIDs.Count == 0)
                return new List<string>();

            var board = new HashSet<string>(
                boardIDs
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim().ToUpperInvariant()),
                StringComparer.Ordinal);

            var matched = new List<(ChemRule rule, string[] participants)>();
            foreach (var rule in Rules.OrderBy(r => (int)r.PriorityLevel))
            {
                if (!IsTriggered(rule, board))
                    continue;
                matched.Add((rule, GetParticipants(rule, board)));
            }

            var normalParticipation = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new List<string>();

            foreach (var (rule, participants) in matched)
            {
                bool isExempt = rule.PriorityLevel == Priority.Conflict
                                || rule.PriorityLevel == Priority.Hidden;

                if (!isExempt)
                {
                    bool overLimit = false;
                    foreach (var p in participants)
                    {
                        if (normalParticipation.TryGetValue(p, out int n) && n >= MaxNormalCombosPerRole)
                        {
                            overLimit = true;
                            break;
                        }
                    }
                    if (overLimit)
                        continue;
                }

                result.Add(rule.Id);

                if (!isExempt)
                {
                    foreach (var p in participants)
                    {
                        normalParticipation.TryGetValue(p, out int n);
                        normalParticipation[p] = n + 1;
                    }
                }

                if (result.Count >= MaxResults)
                    break;
            }

            return result;
        }

        private static bool IsTriggered(ChemRule rule, HashSet<string> board)
        {
            if (rule.RequiredAll != null)
            {
                foreach (var id in rule.RequiredAll)
                {
                    if (!board.Contains(id)) return false;
                }
            }

            if (rule.Forbidden != null)
            {
                foreach (var id in rule.Forbidden)
                {
                    if (board.Contains(id)) return false;
                }
            }

            if (rule.RequiredAny != null && rule.RequiredAny.Length > 0)
            {
                bool any = false;
                foreach (var id in rule.RequiredAny)
                {
                    if (board.Contains(id)) { any = true; break; }
                }
                if (!any) return false;
            }

            if (rule.SideA != null && rule.SideB != null)
            {
                bool hasA = false;
                bool hasB = false;
                foreach (var id in rule.SideA)
                {
                    if (board.Contains(id)) { hasA = true; break; }
                }
                foreach (var id in rule.SideB)
                {
                    if (board.Contains(id)) { hasB = true; break; }
                }
                if (!(hasA && hasB)) return false;
            }

            if (rule.CountPool != null)
            {
                int count = 0;
                foreach (var id in rule.CountPool)
                {
                    if (board.Contains(id)) count++;
                }
                if (count < rule.MinCount) return false;
            }

            return true;
        }

        private static string[] GetParticipants(ChemRule rule, HashSet<string> board)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            AddPresent(set, board, rule.RequiredAll);
            AddPresent(set, board, rule.RequiredAny);
            AddPresent(set, board, rule.SideA);
            AddPresent(set, board, rule.SideB);
            AddPresent(set, board, rule.CountPool);
            var arr = new string[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        private static void AddPresent(HashSet<string> set, HashSet<string> board, string[] ids)
        {
            if (ids == null) return;
            foreach (var id in ids)
            {
                if (board.Contains(id))
                    set.Add(id);
            }
        }

        private static ChemRule Make(
            string id,
            Priority priority,
            string[] all = null,
            string[] any = null,
            string[] forbidden = null,
            string[] sideA = null,
            string[] sideB = null,
            string[] countPool = null,
            int minCount = 0)
        {
            return new ChemRule
            {
                Id = id,
                PriorityLevel = priority,
                RequiredAll = all,
                RequiredAny = any,
                Forbidden = forbidden,
                SideA = sideA,
                SideB = sideB,
                CountPool = countPool,
                MinCount = minCount
            };
        }
    }
}
