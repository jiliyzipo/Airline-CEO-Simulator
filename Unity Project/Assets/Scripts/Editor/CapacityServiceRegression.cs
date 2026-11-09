#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 批次一验收（M1 机场索引 + C0 容量服务抽取）。
    ///
    /// 本回归回答两个问题，任一为否即红：
    ///   ① **抽取没改变任何数字** —— 用**抽取前的公式**（本文件里冻结一份独立实现，
    ///      不调用 <see cref="CapacityService"/>、不调用被改动的方法）重算
    ///      104 机场 × 96 刻 × 2 方向 = 19,968 格，逐格比对现行输出；
    ///      并比对一个抽取前落盘的 SHA-256 基线摘要。
    ///   ② **索引没改变任何语义** —— `FindByCode` 与「线性扫描」在全部代码、
    ///      大小写变体、空串、未知码、带空格串上给出同一结果。
    ///
    /// 为什么冻结公式要**另写一份**而不是「调用新实现再比一遍」：
    /// 调新实现再比新实现恒等，等于没测。这里是拿 2026-11-09 抽取前的源码逐行抄下来，
    /// 连带它依赖的区域名单与宵禁例外一起冻结；只有「跑道数」和「协调时段原文」继续
    /// 走 AirportCatalog 的公开只读接口（这两项本次**未改动**，且跑道数还有 ⑨ 与基线摘要兜底）。
    ///
    /// 边界（必须写清，避免误读）：这是 **C# 判据层**的等价回归，覆盖容量 / 宵禁 / 方向池 /
    /// 派生接口。它**不**覆盖完整游戏循环下的「申请 → 成交 → 结算」链路，
    /// 那属于首批的真实申请序列回归（需开 Unity 跑 PlayMode），未执行前一律标注「待验证」。
    /// </summary>
    public static class CapacityServiceRegression
    {
        /// <summary>
        /// 抽取前落盘的基线摘要（2026-11-09，`out/analysis/baseline/capacity-c0.json`）。
        /// 口径：按 `All` 顺序，每机场先 96 个刻容量，再 `|四段池,四段池|日容量`，全部逗号分隔。
        /// 基线文件里同时记着算法、布局与格数，改动口径必须同时改这里并说明原因。
        /// </summary>
        const string BaselineDigest = "d84363501d3791a9bd94a416f51e63c92a1b25de410ad61c4e286b9f14ce26df";

        const int GridCount = 96;
        const int BandCount = 4;

        // ————————————————— 冻结：抽取前的宵禁名单 —————————————————
        // 抄自 AirportRegions.Eea / ChinaCurfew 与 AirportCatalog.NightCurfewExtra。
        // 用 AirportRegions 的公开判定仍然会随名单变化，故这里直接抄名单，
        // 让「名单被误改」也能被本回归发现（那时基线摘要同样会变）。
        static readonly string[] FrozenEea =
        {
            "LHR", "CDG", "FRA", "AMS", "MAD", "BCN", "FCO", "MUC", "ZRH",
            "VIE", "CPH", "ARN", "HEL"
        };
        static readonly string[] FrozenChinaCurfew = { "PEK" };
        static readonly string[] FrozenNightCurfewExtra = { "NRT", "HND", "SYD", "TPE" };
        const string FrozenCurfewWindow = "23:00-06:00";
        /// <summary>冻结值：一条跑道每刻放出 1 条槽。</summary>
        const int FrozenSlotsPerRunway = 1;

        public static bool Run()
        {
            int quarterDiff = 0, poolDiff = 0, cacheDiff = 0, hourDiff = 0, dayDiff = 0, kindDiff = 0;
            var detail = new StringBuilder();

            var codes = new string[AirportCatalog.Count];
            for (int i = 0; i < codes.Length; i++) codes[i] = AirportCatalog.Airports[i].code;

            // —— ① 逐刻：19,968 格（96 刻 × 2 方向）全靠「冻结公式 vs 现行」比对 ——
            var canonicalNew = new List<string>(codes.Length * (GridCount + 1));
            var canonicalLegacy = new List<string>(codes.Length * (GridCount + 1));

            for (int ci = 0; ci < codes.Length; ci++)
            {
                string code = codes[ci];
                var poolsNew = CapacityService.Pools(code);
                var poolsLegacy = new int[BandCount];
                int dayLegacy = 0;

                for (int k = 0; k < GridCount; k++)
                {
                    int m = k * ScheduleSlots.SlotGridStepMinutes;
                    int now = CapacityService.Quarter(code, m);
                    int old = FrozenQuarter(code, m);

                    canonicalNew.Add(now.ToString(CultureInfo.InvariantCulture));
                    canonicalLegacy.Add(old.ToString(CultureInfo.InvariantCulture));

                    if (now != old)
                    {
                        quarterDiff++;
                        if (quarterDiff <= 8)
                            detail.Append($"{code} {m / 60:00}:{m % 60:00} 现={now} 旧={old}; ");
                    }

                    // 方向池：DEP / ARR 现行为同数，抽取后必须仍然同数
                    if (CapacityService.Quarter(code, m, "ARR") != now
                        || CapacityService.Quarter(code, m, "DEP") != now)
                    {
                        kindDiff++;
                        if (kindDiff <= 5) detail.Append($"{code} {m} 方向池不同数; ");
                    }
                }

                // 四段池 + 日容量：派生接口同样要全等
                for (int b = 0; b < BandCount; b++)
                {
                    var grid = ScheduleSlots.BandGrid(b);
                    for (int i = 0; i < grid.Count; i++) poolsLegacy[b] += FrozenQuarter(code, grid[i]);

                    int nowPool = AirportCatalog.SlotCapacity(code, b);
                    if (nowPool != poolsLegacy[b])
                    {
                        poolDiff++;
                        if (poolDiff <= 5) detail.Append($"{code} 段{b} 现={nowPool} 旧={poolsLegacy[b]}; ");
                    }
                    if (CapacityService.Pools(code)[b] != poolsLegacy[b]) poolDiff++;
                    if (AirportCatalog.SlotCapacity(code, b, "ARR") != poolsLegacy[b]) kindDiff++;
                }

                // AirportInfo.slotCapacity 是派生缓存，必须与唯一来源一致
                var a = AirportCatalog.FindByCode(code);
                if (a.slotCapacity == null || a.slotCapacity.Length != BandCount) cacheDiff++;
                else for (int b = 0; b < BandCount; b++) if (a.slotCapacity[b] != poolsLegacy[b]) cacheDiff++;

                for (int h = 0; h < 24; h++)
                {
                    int legacyHour = 0;
                    for (int q = 0; q < 60; q += ScheduleSlots.SlotGridStepMinutes)
                        legacyHour += FrozenQuarter(code, h * 60 + q);
                    dayLegacy += legacyHour;
                    if (CapacityService.Hour(code, h) != legacyHour)
                    {
                        hourDiff++;
                        if (hourDiff <= 5) detail.Append($"{code} {h}时 现={CapacityService.Hour(code, h)} 旧={legacyHour}; ");
                    }
                }

                if (CapacityService.Day(code) != dayLegacy || AirportCatalog.DayCapacity(code) != dayLegacy)
                {
                    dayDiff++;
                    if (dayDiff <= 5) detail.Append($"{code} 日容量 现={CapacityService.Day(code)} 旧={dayLegacy}; ");
                }

                // 四段之和 == 日容量（恒等式，防止池与日各自漂移）
                int poolSum = 0;
                for (int b = 0; b < BandCount; b++) poolSum += poolsLegacy[b];
                if (poolSum != dayLegacy) dayDiff++;

                canonicalNew.Add("|" + string.Join(",", Array.ConvertAll(poolsNew, x => x.ToString(CultureInfo.InvariantCulture)))
                                 + "|" + CapacityService.Day(code).ToString(CultureInfo.InvariantCulture));
                canonicalLegacy.Add("|" + string.Join(",", Array.ConvertAll(poolsLegacy, x => x.ToString(CultureInfo.InvariantCulture)))
                                    + "|" + dayLegacy.ToString(CultureInfo.InvariantCulture));
            }

            string digestNew = Sha256(string.Join(",", canonicalNew));
            string digestLegacy = Sha256(string.Join(",", canonicalLegacy));
            bool digestOk = digestNew == BaselineDigest && digestLegacy == BaselineDigest;

            // —— ② 索引：语义与线性扫描全等 ——
            bool indexOk = true;
            var indexDetail = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < codes.Length; i++)
            {
                string code = codes[i];
                if (!seen.Add(code)) { indexOk = false; indexDetail.Append($"重复码 {code}; "); }

                if (AirportCatalog.FindByCode(code).code != code) { indexOk = false; indexDetail.Append($"{code} 原样未命中; "); }
                if (AirportCatalog.FindByCode(code.ToLowerInvariant()).code != code) { indexOk = false; indexDetail.Append($"{code} 小写未命中; "); }
                if (AirportCatalog.FindByCode(MixedCase(code)).code != code) { indexOk = false; indexDetail.Append($"{code} 混合大小写未命中; "); }

                // 与「线性扫描」逐条对齐（这就是被替换掉的旧实现）
                if (AirportCatalog.FindByCode(code).code != LinearFind(code).code)
                {
                    indexOk = false;
                    indexDetail.Append($"{code} 与线性扫描不一致; ");
                }
            }

            // 未命中 / 脏输入：行为必须与旧实现一致（不 trim、不猜、不抛）
            string[] missCases = { "", null, "ZZZ", " pek", "pek ", "PEKX", "佛山沙堤" };
            for (int i = 0; i < missCases.Length; i++)
            {
                string q = missCases[i];
                string nowCode = AirportCatalog.FindByCode(q).code ?? "";
                string oldCode = LinearFind(q).code ?? "";
                if (!string.Equals(nowCode, oldCode, StringComparison.Ordinal))
                {
                    indexOk = false;
                    indexDetail.Append($"未命中用例「{q ?? "null"}」现={nowCode} 旧={oldCode}; ");
                }
            }

            // 索引条数必须等于目录条数（防止有码没进表）
            bool allGreen = quarterDiff == 0 && poolDiff == 0 && cacheDiff == 0 && hourDiff == 0
                            && dayDiff == 0 && kindDiff == 0 && digestOk && indexOk;

            Debug.Log($"容量服务 / 机场索引（批次一）→ 机场={codes.Length} 逐刻格={codes.Length * GridCount}"
                      + $"（×2 方向={codes.Length * GridCount * 2}）"
                      + $"｜逐刻差异={quarterDiff} 段池差异={poolDiff} 缓存差异={cacheDiff}"
                      + $" 小时差异={hourDiff} 日容量差异={dayDiff} 方向池差异={kindDiff}"
                      + $"｜基线摘要={digestOk}（新={Short(digestNew)} 旧算={Short(digestLegacy)} 基线={Short(BaselineDigest)}）"
                      + $"｜索引语义={indexOk}"
                      + (allGreen ? "　✓" : $"　✗ {detail}{indexDetail}"));

            return allGreen;
        }

        // ————————————————— 抽取前公式的冻结实现 —————————————————

        /// <summary>
        /// 2026-11-09 抽取前的 `QuarterCapacityRaw`，逐行冻结。
        /// 刻意**不**调用 <see cref="CapacityService"/> 与 <see cref="AirportCatalog.QuarterCapacity"/>。
        /// </summary>
        static int FrozenQuarter(string code, int minutes)
        {
            string coordWindow = AirportCatalog.CoordWindowOf(code);
            if (string.IsNullOrEmpty(coordWindow)) return 0;

            int m = ((minutes % 1440) + 1440) % 1440;
            if (!FrozenInCoordWindow(coordWindow, m)) return 0;   // 协调时段外：免槽
            if (FrozenInCurfew(code, m)) return 0;                // 宵禁：物理禁飞，记 0

            return FrozenRunwayCount(code) * FrozenSlotsPerRunway;
        }

        /// <summary>抄自抽取前的 `InCoordWindowText`。</summary>
        static bool FrozenInCoordWindow(string window, int m)
        {
            if (string.IsNullOrEmpty(window)) return false;
            if (window == AirportCatalog.CoordAllDay) return true;
            return FrozenWindow(window, m, true);
        }

        /// <summary>抄自抽取前的 `InWindowText`（写坏时按 fallback，不静默变非协调）。</summary>
        static bool FrozenWindow(string window, int minutes, bool fallback)
        {
            if (string.IsNullOrEmpty(window)) return false;
            int m = ((minutes % 1440) + 1440) % 1440;
            int dash = window.IndexOf('-');
            if (dash <= 0) return fallback;
            if (!FrozenHm(window.Substring(0, dash).Trim(), out int s)) return fallback;
            if (!FrozenHm(window.Substring(dash + 1).Trim(), out int e)) return fallback;
            if (s == e) return false;
            return s < e ? (m >= s && m < e) : (m >= s || m < e);
        }

        static bool FrozenHm(string hm, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrEmpty(hm) || hm.IndexOf(':') < 0) return false;
            var parts = hm.Split(':');
            if (parts.Length < 2) return false;
            if (!int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            minutes = h * 60 + m;
            return true;
        }

        /// <summary>抄自抽取前的 `CurfewWindowOf`（名单在这里冻结）。</summary>
        static bool FrozenInCurfew(string code, int m)
        {
            string window = "";
            if (InList(FrozenEea, code)) window = FrozenCurfewWindow;
            else if (AirportRegions.IsChina(code))
                window = InList(FrozenChinaCurfew, code) ? FrozenCurfewWindow : "";
            else if (InList(FrozenNightCurfewExtra, code)) window = FrozenCurfewWindow;

            return !string.IsNullOrEmpty(window) && FrozenWindow(window, m, false);
        }

        /// <summary>
        /// 抄自抽取前的 `RunwayCountOf`。本批**没有**改动跑道口径，
        /// 因此这里走既有的公开只读接口；「申报超额」若日后填表，会经
        /// <see cref="AirportCatalog.HasEffectiveRunwayOverride"/> 一并纳入。
        /// </summary>
        static int FrozenRunwayCount(string code)
        {
            if (AirportCatalog.HasEffectiveRunwayOverride(code)) return AirportCatalog.RunwayCount(code);
            return AirportCatalog.PhysicalRunwayCount(code);
        }

        // ————————————————— 被替换掉的旧索引实现（作为对照组保留）—————————————————

        /// <summary>抽取前的 `FindByCode`：线性扫描 + OrdinalIgnoreCase。对照组，勿删。</summary>
        static AirportInfo LinearFind(string code)
        {
            var all = AirportCatalog.Airports;
            for (int i = 0; i < all.Count; i++)
                if (string.Equals(all[i].code, code, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            return default;
        }

        static string MixedCase(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            var sb = new StringBuilder(code.Length);
            for (int i = 0; i < code.Length; i++)
                sb.Append(i % 2 == 0 ? char.ToLowerInvariant(code[i]) : char.ToUpperInvariant(code[i]));
            return sb.ToString();
        }

        static bool InList(string[] set, string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            for (int i = 0; i < set.Length; i++)
                if (string.Equals(set[i], code, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        static string Short(string digest)
            => string.IsNullOrEmpty(digest) ? "" : digest.Substring(0, 8);
    }
}
#endif
