using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// K14 · 压测口径补盲（Unity 侧守卫）。
    ///
    /// 离线压测 `--netstress` 用 <c>TakeoverWorldBuilder.BuildPreview(id, universe)</c> 把「机场池」
    /// 收窄成被测样本。这个重载是**为压测新开的生产代码缝**，所以必须在引擎侧钉两件事：
    /// ① **默认路径零扰动**：`BuildPreview(id)` 与 `BuildPreview(id, 全目录)` 必须给出完全相同的航线集合
    ///    —— 否则「加个可选参数」就已经改了选司界面看到的网络；
    /// ② **收窄真的生效**：从宇宙里拿掉若干真实目的地后，生成网络里不得再出现它们，且至少一家预设的
    ///    航线集合发生变化 —— 少了这条，压测「减城市」可能只是把池子传进去、却没被目的地池读取。
    ///
    /// 不在本回归里写死批次 A/B 的城市清单：那是任务书 §9b / K06 的批次事实，唯一真源在
    /// `tools/balance/Stress/NetworkPoolStressReport.cs` 的 `BatchA/BatchB/BatchBPrime`。
    /// 本回归只验**机制**（收窄是否被读到、默认是否不变），批次事实由离线报告自己断言。
    /// </summary>
    public static class NetworkPoolVisibilityRegression
    {
        static readonly string[] Presets =
        {
            PresetAirlines.Startup, PresetAirlines.Regional,
            PresetAirlines.Legacy, PresetAirlines.Distressed,
        };

        public static bool Run()
        {
            var sb = new StringBuilder();
            sb.Append("K14 网络池可见性 → ");

            var full = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var catalog = CityEconomyCatalog.All;
            for (int i = 0; i < catalog.Length; i++)
            {
                var e = catalog[i];
                if (e != null && !string.IsNullOrEmpty(e.iata))
                    full.Add(e.iata.Trim().ToUpperInvariant());
            }

            bool defaultSame = true;   // ① 默认路径 = 全目录（逐条航线 id 相同）
            bool restrictedOk = true;  // ② 收窄后不得再飞被移除的城市，且网络确实变了
            bool insideUniverse = true;
            bool everChanged = false;
            var detail = new StringBuilder();

            foreach (var id in Presets)
            {
                var plain = TakeoverWorldBuilder.BuildPreview(id);
                var explicitFull = TakeoverWorldBuilder.BuildPreview(id, full);
                string a = RouteKey(plain);
                string b = RouteKey(explicitFull);
                bool same = a == b;
                defaultSame &= same;

                // 取本航司真实用到的前 4 个目的地做「移除样本」——用真实目的地，
                // 才能保证被移除的城市本来真的会被选中（否则「没变化」说明不了任何事）。
                var removed = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                if (plain.routes != null)
                {
                    for (int i = 0; i < plain.routes.Count && removed.Count < 4; i++)
                    {
                        var r = plain.routes[i];
                        if (r == null || string.IsNullOrEmpty(r.destination)) continue;
                        if (string.Equals(r.destination, plain.hubAirport, System.StringComparison.OrdinalIgnoreCase)) continue;
                        removed.Add(r.destination);
                    }
                }

                var narrow = new System.Collections.Generic.HashSet<string>(full, System.StringComparer.OrdinalIgnoreCase);
                foreach (var code in removed) narrow.Remove(code);
                narrow.Add(plain.hubAirport);   // 枢纽本身不在目的地池里，但保留以表达「机场池含枢纽」

                var cut = TakeoverWorldBuilder.BuildPreview(id, narrow);
                bool routesChanged = RouteKey(cut) != a;
                if (removed.Count > 0) everChanged |= routesChanged;

                if (cut.routes != null)
                {
                    for (int i = 0; i < cut.routes.Count; i++)
                    {
                        var r = cut.routes[i];
                        if (r == null) continue;
                        if (removed.Contains(r.destination))
                        {
                            restrictedOk = false;
                            detail.Append($"\n　{id} 收窄后仍生成 {r.routeId}（目的地 {r.destination} 已被移除）");
                        }
                        if (!narrow.Contains(r.origin) || !narrow.Contains(r.destination))
                        {
                            insideUniverse = false;
                            detail.Append($"\n　{id} 生成了宇宙外航线 {r.routeId}");
                        }
                    }
                }

                detail.Append($"\n　{id}：默认=全目录 {same} · 移除 {removed.Count} 城后航线变化 {routesChanged}");
            }

            bool ok = defaultSame && restrictedOk && insideUniverse && everChanged;

            sb.Append("默认零扰动=").Append(defaultSame)
              .Append(" 收窄生效=").Append(restrictedOk)
              .Append(" 不越界=").Append(insideUniverse)
              .Append(" 网络变化=").Append(everChanged)
              .Append("　").Append(ok ? "✓" : "✗");
            sb.Append(detail);
            if (!defaultSame) sb.Append("\n　默认 BuildPreview 与显式全目录不一致 —— 可选参数改动了生产网络");
            if (!everChanged) sb.Append("\n　移除真实目的地后网络完全没变 —— 收窄没有被目的地池读取（K14 仍失明）");

            Debug.Log(sb.ToString());
            return ok;
        }

        static string RouteKey(AirlineProfile p)
        {
            if (p?.routes == null) return "";
            var ids = new System.Collections.Generic.List<string>(p.routes.Count);
            for (int i = 0; i < p.routes.Count; i++)
                if (p.routes[i] != null) ids.Add(p.routes[i].routeId);
            ids.Sort(System.StringComparer.Ordinal);
            return string.Join(",", ids);
        }
    }
}
