using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// K16 · 中国境内超短航段（高铁走廊）—— 引擎侧守卫。
    ///
    /// 裁定（2026-10-23）：**中国境内两端 &lt; 250 km 不作定期客运**。这条判据落在
    /// <see cref="RouteGeography.IsChinaDomesticTooShort"/>，同一个函数同时管三处
    /// （目的地池 / 种子剪枝 / 开线判定），所以这里必须正反两面都钉住：
    ///
    /// · **拦得住**：WUX–PVG 136 / NGB–PVG 150 / ZUH–CAN 154 / PVG–HGH 166 / TFU–CKG 222 /
    ///   NKG–SHA 242 —— 这些现实中都是高铁直达、没有定期客运。
    /// · **别误伤**：LAX–SAN 176、YVR–SEA 205 是真实短途（美国境内有密集定期航班），
    ///   CAN–HKG 136 属内地—港澳跨境，两者都不该被这条**境内**判据带走；PVG–CAN 1203 更不必说。
    ///   第一版把 `MinPassengerKm` 从 100 抬到 250 就正好会误杀 LAX–SAN —— 所以阈值必须是
    ///   「中国境内」的条件判据，不是全球一刀切。
    /// · **网络真的干净**：接管局种子网络里不允许再出现中国境内 &lt; 250 km 的航线。
    ///   只看规则函数不够 —— `BuildDestinationPool` 走的是同一条判据，但种子池、剪枝、批次
    ///   目的地优先级任何一处漏接，规则正确也会漏进网络。
    ///
    /// 为什么这条值得单独一个回归（而不是只在离线报告里数一遍）：接管局的种子日利润与航距无关
    /// （`0.6 + (i%5)×0.15` 万/日），一条 136 km 的线按 11 班/周能赚 18~36 万/月，而玩家新开
    /// 同一条线走 RPM 口径是单班 −0.43 万 —— 同一条线「既有赚钱、新开亏钱」，符号相反。
    /// 这类失真只有引擎侧能不能生成出这条航线才拦得住。
    /// </summary>
    public static class ShortHaulRegression
    {
        /// <summary>受影响的接管样本：四个具名预设 + 三条被删短线所在的 hub 航司。</summary>
        static readonly string[] Networks =
        {
            PresetAirlines.Startup, PresetAirlines.Regional,
            PresetAirlines.Legacy, PresetAirlines.Distressed,
            PresetAirlines.HubPrefix + "ZUH",
            PresetAirlines.HubPrefix + "WUX",
            PresetAirlines.HubPrefix + "NGB",
        };

        public static bool Run()
        {
            var sb = new StringBuilder();
            sb.Append("K16 中国境内超短航段 → ");

            // —— ① 判据正反两面 ——
            bool rejected = true, kept = true;
            var detail = new StringBuilder();

            // 亚洲/中国境内：必须被拦（航距由调用方传入，与底表坐标解耦，避免坐标改动掩盖判据缺陷）
            var mustReject = new (string A, string B, float Km)[]
            {
                ("WUX", "PVG", 136f), ("NGB", "PVG", 150f), ("ZUH", "CAN", 154f),
                ("PVG", "HGH", 166f), ("TFU", "CKG", 222f), ("NKG", "SHA", 242f),
                ("TSN", "PEK", 125f),
            };
            foreach (var c in mustReject)
            {
                if (RouteGeography.IsPlausiblePassengerOd(c.A, c.B, c.Km)) continue;
                bool ruleHit = RouteGeography.IsChinaDomesticTooShort(c.A, c.B, c.Km);
                string why = RouteGeography.RejectReason(c.A, c.B, c.Km);
                bool sayHsr = why.Contains("高铁");
                if (!ruleHit || !sayHsr)
                {
                    rejected = false;
                    detail.Append($"\n　{c.A}–{c.B} {c.Km:0}km 被拒但判据={ruleHit}/文案含高铁={sayHsr}（{why}）");
                }
            }

            // 阈值本身要有效：同一条线抬到 262 km 必须放行（「拒绝」不能是永久封死）
            if (!RouteGeography.IsPlausiblePassengerOd("WUX", "PVG", 262f)) rejected = false;

            // 不得误伤：真实短途 / 内地—港澳 / 正常干线
            var mustKeep = new (string A, string B, float Km, string Why)[]
            {
                ("LAX", "SAN", 176f, "美国境内真实短途"),
                ("YVR", "SEA", 205f, "加拿大/美国境内真实短途"),
                ("CAN", "HKG", 136f, "内地—港澳属跨境，不在境内判据内"),
                ("PVG", "CAN", 1203f, "国内干线对照"),
            };
            foreach (var c in mustKeep)
            {
                bool keptOk = RouteGeography.IsPlausiblePassengerOd(c.A, c.B, c.Km)
                              && !RouteGeography.IsChinaDomesticTooShort(c.A, c.B, c.Km);
                if (keptOk) continue;
                kept = false;
                detail.Append($"\n　{c.A}–{c.B} {c.Km:0}km 被误伤（{c.Why}）");
            }

            // —— ② 种子网络里不得再有中国境内 <250 km 的航线 ——
            bool networkClean = true;
            int scanned = 0;
            var offenders = new StringBuilder();
            foreach (string id in Networks)
            {
                var p = TakeoverWorldBuilder.BuildPreview(id);
                if (p?.routes == null)
                {
                    networkClean = false;
                    detail.Append($"\n　{id} 预览为空 —— 无法验证网络");
                    continue;
                }
                for (int i = 0; i < p.routes.Count; i++)
                {
                    var r = p.routes[i];
                    if (r == null) continue;
                    scanned++;
                    if (!RouteGeography.IsChinaDomesticTooShort(r.origin, r.destination, r.distanceKm)) continue;
                    networkClean = false;
                    offenders.Append($"{id}:{r.origin}–{r.destination} {r.distanceKm:0}km; ");
                }
            }

            bool ok = rejected && kept && networkClean;

            sb.Append("境内超短被拦=").Append(rejected)
              .Append(" 真实短途未误伤=").Append(kept)
              .Append(" 种子网络干净=").Append(networkClean)
              .Append("（扫描=").Append(scanned).Append(" 条）")
              .Append('　').Append(ok ? "✓" : "✗");
            sb.Append(detail);
            if (offenders.Length > 0) sb.Append("\n　残留境内超短航线：").Append(offenders.ToString().Trim());

            Debug.Log(sb.ToString());
            return ok;
        }
    }
}
