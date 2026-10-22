using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// D2 / K15：**航司星级必须是客流的单调函数**。
    ///
    /// 事故背景（2026-10-22 D2 实测）：<c>PresetAirlines.StarsFor</c> 在 <c>pax >= 70</c> 时用
    /// IATA 三字码的字符串哈希分界（<c>h % 5 == 0 ? 5 : 4</c>，h 由逐字符 <c>h*31+c</c> 得到）。
    /// 结果是 87M 客流的迪拜（DXB）被分成 ★4，而 76M 的伊斯坦布尔（IST）、77.8M 的丹佛（DEN）是 ★5，
    /// 78M 的羽田（HND）又低于 77.8M 的丹佛。分界与规模无关，玩家看不出规律。
    ///
    /// 更难发现的是：当时那条「更高星级必须更紧」的跑道断言**是循环的** ——
    /// 链条是「档位 → 选 seed 预设 → 现金/机队/航线」，现金本就按目标跑道反解出来，
    /// 之后再断言「跑道符合该档目标」。所以它只能发现「烧钱口径改了但现金没重解」，
    /// **不可能**发现「迪拜被分成了 ★4」。这条循环断言给了我们一种虚假的覆盖感。
    ///
    /// 本回归钉住三件事，且**与档位无关**（不读现金、不读跑道）：
    /// ① 按客流升序排列后，星级不得逆序（客流大的档位不得更低）；
    /// ② 每一档的客流区间必须是**连续**的（★4 的下界 == ★5 的上界那种阶梯，不允许互相嵌套）；
    /// ③ 档位必须落在 2..5 且四档都有人 —— 防止阈值改动把某一档清空后「全员同档」还能过。
    ///
    /// 豁免：四个具名预设（CAN/PEK/PVG/CTU）按设计强制指定档位，见任务书 §18.1 待裁定项 3。
    /// 它们是手写旗舰示例（青禾 / 北辰 / 东海 / 天枢），不参与单调性检查，但**必须显式列出**，
    /// 不能靠「跳过所有 preset_*」糊过去 —— 那样将来新增预设会悄悄逃过检查。
    /// </summary>
    public static class AirlineTierRegression
    {
        /// <summary>四个具名预设的枢纽。改动此表必须同时改任务书 §18.1。</summary>
        static readonly string[] CuratedHubs = { "CAN", "PEK", "PVG", "CTU" };

        public static bool Run()
        {
            var profiles = PresetAirlines.AllSelectable();
            if (profiles == null || profiles.Count == 0)
            {
                Debug.LogError("D2 星级单调 ⛔ 取不到可接管航司列表");
                return false;
            }

            // 收集「枢纽 → 客流 / 星级」，四个具名预设的枢纽整条跳过（按设计豁免）。
            var rows = new List<(string hub, int star, float pax)>();
            int skippedCurated = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                if (p == null || string.IsNullOrEmpty(p.hubAirport)) continue;
                bool curated = false;
                for (int k = 0; k < CuratedHubs.Length; k++)
                {
                    if (!string.Equals(p.hubAirport, CuratedHubs[k], System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    curated = true;
                    break;
                }
                if (curated) { skippedCurated++; continue; }

                var e = CityEconomyCatalog.Find(p.hubAirport);
                if (e == null) continue;
                rows.Add((p.hubAirport.ToUpperInvariant(), p.difficulty, e.paxMillion));
            }

            rows.Sort((a, b) => a.pax.CompareTo(b.pax));

            // ① 单调不减：排序后相邻不得逆序。
            var inversions = new StringBuilder();
            int inversionCount = 0;
            for (int i = 1; i < rows.Count; i++)
            {
                if (rows[i].star >= rows[i - 1].star) continue;
                inversionCount++;
                if (inversionCount <= 5)
                {
                    if (inversions.Length > 0) inversions.Append('、');
                    inversions.Append(rows[i - 1].hub).Append('(').Append(rows[i - 1].pax.ToString("F0"))
                        .Append("M ★").Append(rows[i - 1].star).Append(") > ")
                        .Append(rows[i].hub).Append('(').Append(rows[i].pax.ToString("F0"))
                        .Append("M ★").Append(rows[i].star).Append(')');
                }
            }
            bool monotone = inversionCount == 0;

            // ② 每档的客流区间必须连续：同一档内不得混进比更高档还大的机场（已由①保证），
            //    同时**每一档的客流范围不得与另一档重叠**（重叠说明阈值被写成了区间交集）。
            var minPax = new Dictionary<int, float>();
            var maxPax = new Dictionary<int, float>();
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                counts.TryGetValue(r.star, out int c);
                counts[r.star] = c + 1;
                if (!minPax.ContainsKey(r.star) || r.pax < minPax[r.star]) minPax[r.star] = r.pax;
                if (!maxPax.ContainsKey(r.star) || r.pax > maxPax[r.star]) maxPax[r.star] = r.pax;
            }
            bool noOverlap = true;
            var overlapNote = new StringBuilder();
            for (int lo = 2; lo <= 5; lo++)
            {
                for (int hi = lo + 1; hi <= 5; hi++)
                {
                    if (!minPax.ContainsKey(lo) || !minPax.ContainsKey(hi)) continue;
                    // 低档的最大客流不得 >= 高档的最小客流（允许相等：边界值同客流归哪档由阈值决定）
                    if (maxPax[lo] < minPax[hi]) continue;
                    noOverlap = false;
                    if (overlapNote.Length > 0) overlapNote.Append("；");
                    overlapNote.Append('★').Append(lo).Append(" 最高 ").Append(maxPax[lo].ToString("F0"))
                        .Append("M ≥ ★").Append(hi).Append(" 最低 ").Append(minPax[hi].ToString("F0")).Append('M');
                }
            }

            // ③ 四档都非空，且档位只可能是 2..5。
            bool allTiersPresent = true;
            var missing = new StringBuilder();
            for (int s = 2; s <= 5; s++)
            {
                int c = counts.TryGetValue(s, out int n) ? n : 0;
                if (c > 0) continue;
                allTiersPresent = false;
                if (missing.Length > 0) missing.Append('、');
                missing.Append('★').Append(s);
            }
            bool validRange = true;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].star < 2 || rows[i].star > 5) { validRange = false; break; }

            // ④ ★2 规模不再克隆（2026-10-22 D2 第二步）：
            //    修复前 59 家的 (机队, 航线) 只有 1 种组合；现在必须随客流展开出多种。
            //    这条与难度无关，只钉「生成器是否为每座枢纽造出不同规模的开局」。
            var combos = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                if (p == null || p.difficulty != 2) continue;
                combos.Add(p.fleetCount + "x" + p.routeCount);
            }
            bool notCloned = combos.Count >= 3;

            // ⑤ 具名预设之间必须自洽（2026-10-22 D2 第三步）：
            //    ① 把它们排除在「对齐客流规则」之外是对的（手写旗舰示例，允许与规则差一档），
            //    但**豁免不等于不管** —— 正是这个豁免让「广州★2 / 成都★5」颠倒长期存活：
            //    两端都跳过检查，于是没有任何断言能发现四个预设之间的顺序反了。
            //    现在的口径：不参与对齐规则的检查，但四个之间档位必须随客流**严格递增**。
            var ladderRows = new List<(string hub, int star, float pax)>();
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                if (p == null || string.IsNullOrEmpty(p.hubAirport)) continue;
                bool isCurated = false;
                for (int k = 0; k < CuratedHubs.Length; k++)
                    if (string.Equals(p.hubAirport, CuratedHubs[k], System.StringComparison.OrdinalIgnoreCase))
                    { isCurated = true; break; }
                if (!isCurated) continue;
                var ce = CityEconomyCatalog.Find(p.hubAirport);
                if (ce == null) continue;
                ladderRows.Add((p.hubAirport.ToUpperInvariant(), p.difficulty, ce.paxMillion));
            }
            ladderRows.Sort((a, b) => a.pax.CompareTo(b.pax));
            var ladderBad = new StringBuilder();
            int ladderBadCount = 0;
            for (int i = 1; i < ladderRows.Count; i++)
            {
                if (ladderRows[i].star > ladderRows[i - 1].star) continue;
                ladderBadCount++;
                if (ladderBad.Length > 0) ladderBad.Append('、');
                ladderBad.Append(ladderRows[i - 1].hub).Append('(').Append(ladderRows[i - 1].pax.ToString("F0"))
                    .Append("M ★").Append(ladderRows[i - 1].star).Append(") 未低于 ")
                    .Append(ladderRows[i].hub).Append('(').Append(ladderRows[i].pax.ToString("F0"))
                    .Append("M ★").Append(ladderRows[i].star).Append(')');
            }
            bool ladderOk = ladderRows.Count == CuratedHubs.Length && ladderBadCount == 0;

            bool ok = monotone && noOverlap && allTiersPresent && validRange && notCloned && ladderOk;

            var sb = new StringBuilder();
            sb.Append("D2 星级随客流单调 → 逆序=").Append(monotone ? "True" : ("False（" + inversionCount + " 处：" + inversions + "）"))
              .Append(" 档间不重叠=").Append(noOverlap ? "True" : ("False（" + overlapNote + "）"))
              .Append(" 四档齐全=").Append(allTiersPresent ? "True" : ("False（缺 " + missing + "）"))
              .Append(" 档位合法=").Append(validRange ? "True" : "False")
              .Append(" ★2不克隆=").Append(notCloned ? "True（" + combos.Count + " 种规模）" : ("False（仅 " + combos.Count + " 种）"))
              .Append(" 预设阶梯=").Append(ladderOk
                  ? "True（" + string.Join("、", ladderRows.ConvertAll(x => $"{x.hub}★{x.star}")) + "）"
                  : ("False（" + (ladderRows.Count != CuratedHubs.Length
                      ? "只取到 " + ladderRows.Count + " 家预设"
                      : ladderBadCount + " 处逆序：" + ladderBad) + "）"))
              .Append(" 豁免预设=").Append(skippedCurated)
              .Append(" 参与比较=").Append(rows.Count);
            for (int s = 2; s <= 5; s++)
            {
                if (!counts.TryGetValue(s, out int c)) continue;
                sb.Append(" | ★").Append(s).Append(' ').Append(c).Append(" 家 客流 ")
                  .Append(minPax[s].ToString("F1")).Append('~').Append(maxPax[s].ToString("F1")).Append('M');
            }
            if (ok) Debug.Log(sb.ToString());
            else Debug.LogError(sb.ToString());
            return ok;
        }
    }
}
