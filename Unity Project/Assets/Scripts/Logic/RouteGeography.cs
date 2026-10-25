using System;
using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>客运航线是否符合现实：禁止同城双场互飞、过短航段、支线机场开国际线。</summary>
    public static class RouteGeography
    {
        /// <summary>已知坐标下短于该距离不作为定期客运（大兴—首都约 50 km）。</summary>
        public const float MinPassengerKm = 100f;

        /// <summary>
        /// 中国境内两端的定期客运最短航距（K16 裁定，2026-10-23）。
        ///
        /// 为什么单列一条而不是把 <see cref="MinPassengerKm"/> 抬到 250：那个常量是**全球**口径，
        /// 真实现实里 176 km 的 LAX–SAN、205 km 的 YVR–SEA 都有密集定期航班；按 250 一刀切会把
        /// 它们一起删掉。中国境内的 100–250 km 则完全被高铁覆盖 —— 实测 823 条接管航线里有 11 条
        /// 落在这个带内（TSN–PEK 125 / WUX–PVG 136 / SHA–HGH 138 / PVG–NGB 150 / ZUH–CAN 154 /
        /// PVG–HGH 166 / TFU–CKG 222 / NKG–SHA 242），现实中都没有定期客运。
        ///
        /// 不拦的代价不只是「多几条假线」：接管局的种子日利润与航距无关（`TakeoverWorldBuilder`
        /// 里是 `0.6 + (i%5)×0.15` 万/日），一条 136 km 的线在账上按 11 班/周能赚 18~36 万/月；
        /// 而玩家新开同一条线走 RPM 口径是**单班 −0.43 万**（136 km，实测）。同一条航线
        /// 「既有赚钱、新开亏钱」符号相反 —— 这才是 K16 真正要修的失真。
        ///
        /// 港澳台不在本判据内（`AirportRegions.IsChina` 只含内地）：内地—港澳属跨境，
        /// 由 <see cref="IsCrossBorder"/> + 门户门槛管，是否另立规则留给后续裁定。
        /// </summary>
        public const float MinChinaDomesticKm = 250f;

        /// <summary>
        /// 跨境航线的**门户门槛**：两端都必须是「本国口岸机场」。
        ///
        /// 为什么要这一条：目的地池对整个 <see cref="CityEconomyCatalog"/> 一视同仁，
        /// 于是拉萨这类支线机场会被孟买 / 伊斯坦布尔选成好目的地（实测 `BOM→LXA`、`IST→LXA`，
        /// 还顶掉了 `BOM→SIN`、`IST→BOS`）—— 现实中外国航司不飞中国支线机场，
        /// 这类航线是「需求模型算得出、现实里不存在」的假线。
        ///
        /// **为什么不能只用等级**（第一版的教训，实测掉了 43 条真实航线和 8 座城市的目的地）：
        /// `tier` 衡量的是**绝对规模**（客流 ≥ 2000 万 / 货邮 ≥ 50 万吨），而「是不是口岸」
        /// 是**相对本国**的概念。按等级一刀切会把亚的斯亚贝巴（1400 万）、内罗毕、拉各斯、
        /// 阿克拉、奥克兰、赫尔辛基、布宜诺斯艾利斯全判死 —— 这些恰恰是各自国家唯一的国际门户，
        /// 现实中 `LHR–ACC`、`IST–ADD`、`SYD–AKL` 都在飞。一刀切还连累温哥华、珀斯、大连、沈阳。
        ///
        /// 现在三层判据（任一满足即算门户）：
        /// ① 推导等级 ≤ <see cref="MinGatewayTier"/>（枢纽 / 干线）；
        /// ② 年客流 ≥ <see cref="MinGatewayPaxMillion"/>（1000 万，够得着区域门户）；
        /// ③ 该国在底表里只有这一座机场，且客流 ≥ <see cref="MinSoleAirportPaxMillion"/>
        ///    （阿克拉 280 万也放行 —— 它是加纳唯一口岸）。
        /// 拉萨 550 万、中国有 36 座机场，三条都不满足 → 仍被拦在跨境线之外，但国内线照飞。
        /// </summary>
        public const int MinGatewayTier = 1;

        /// <summary>跨境航线的绝对客流门槛（万人次/年）。低于此且非「本国唯一机场」者不算门户。</summary>
        public const float MinGatewayPaxMillion = 10f;

        /// <summary>「本国唯一机场」也要有个下限，免得把几万人的通用机场当成国际口岸。</summary>
        public const float MinSoleAirportPaxMillion = 2f;

        public static bool SameMetro(string origin, string dest)
        {
            if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(dest)) return false;
            if (string.Equals(origin, dest, StringComparison.OrdinalIgnoreCase)) return true;
            var a = CityEconomyCatalog.Find(origin);
            var b = CityEconomyCatalog.Find(dest);
            if (a == null || b == null) return false;
            if (string.IsNullOrEmpty(a.cityZh) || a.cityZh != b.cityZh) return false;
            if (!string.IsNullOrEmpty(a.countryZh) && !string.IsNullOrEmpty(b.countryZh)
                && a.countryZh != b.countryZh)
                return false;
            return true;
        }

        /// <summary>是否跨境（国家/地区不同，含内地—港澳台）。任一端查不到国家即视为「不能判定」→ 不算跨境。</summary>
        public static bool IsCrossBorder(string origin, string dest)
        {
            if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(dest)) return false;
            if (string.Equals(origin, dest, StringComparison.OrdinalIgnoreCase)) return false;
            var a = CityEconomyCatalog.Find(origin);
            var b = CityEconomyCatalog.Find(dest);
            if (a == null || b == null) return false;
            if (string.IsNullOrEmpty(a.countryZh) || string.IsNullOrEmpty(b.countryZh)) return false;
            return !string.Equals(a.countryZh, b.countryZh, StringComparison.Ordinal);
        }

        /// <summary>该机场够不够格当跨境航线的端点：等级够高、客流够大，或是本国唯一口岸。</summary>
        public static bool IsGateway(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            if (AirportCatalog.TierOfCode(code) <= MinGatewayTier) return true;

            var eco = CityEconomyCatalog.Find(code);
            if (eco == null) return false;           // 底表没有 → 不判定为门户（宁可保守）
            if (eco.paxMillion >= MinGatewayPaxMillion) return true;
            if (eco.paxMillion < MinSoleAirportPaxMillion) return false;

            // 一国只有这一座机场 → 它就是该国唯一国际口岸，客流小也得算
            //（阿克拉、内罗毕、拉各斯、奥克兰、赫尔辛基、布宜诺斯艾利斯…）。
            return AirportsInCountry(eco.countryZh) <= 1;
        }

        /// <summary>底表里属于同一国家/地区的机场数量。</summary>
        static int AirportsInCountry(string countryZh)
        {
            if (string.IsNullOrEmpty(countryZh)) return 0;
            var all = CityEconomyCatalog.All;
            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var e = all[i];
                if (e == null || string.IsNullOrEmpty(e.iata)) continue;
                if (string.Equals(e.countryZh, countryZh, StringComparison.Ordinal)) n++;
            }
            return n;
        }

        /// <summary>
        /// 中国境内（两端同属内地）且航距短于 <see cref="MinChinaDomesticKm"/> 的航段：
        /// 高铁覆盖，现实中没有定期客运。`km &lt; 0.5` 视为坐标缺失，不做判定。
        /// </summary>
        public static bool IsChinaDomesticTooShort(string origin, string dest, float km)
        {
            if (km < 0.5f || km >= MinChinaDomesticKm) return false;
            return AirportRegions.IsChina(origin) && AirportRegions.IsChina(dest);
        }

        /// <param name="km">大圆航距；未知坐标时调用方常传入约 2200，不得当真实短航。</param>
        public static bool IsPlausiblePassengerOd(string origin, string dest, float km)
        {
            if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(dest)) return false;
            if (string.Equals(origin, dest, StringComparison.OrdinalIgnoreCase)) return false;
            if (SameMetro(origin, dest)) return false;
            if (km >= 0.5f && km < MinPassengerKm && km < 1500f) return false;
            // 中国境内 250 km 以内：高铁覆盖，现实无定期客运（K16）。
            if (IsChinaDomesticTooShort(origin, dest, km)) return false;
            // 跨境航线两端都得是门户机场：支线机场的国际线不是「不好」，是现实里没有。
            if (IsCrossBorder(origin, dest) && !(IsGateway(origin) && IsGateway(dest))) return false;
            return true;
        }

        public static string RejectReason(string origin, string dest, float km)
        {
            if (string.Equals(origin, dest, StringComparison.OrdinalIgnoreCase))
                return "基地与目的地不能相同。";
            if (SameMetro(origin, dest))
                return "同城机场之间没有定期客运航线（例如北京大兴与北京首都）。";
            if (km >= 0.5f && km < MinPassengerKm && km < 1500f)
                return $"航距仅 {km:F0} 公里，过短，不符合现实客运航线。";
            if (IsChinaDomesticTooShort(origin, dest, km))
                return $"航距仅 {km:F0} 公里：中国境内 {MinChinaDomesticKm:F0} 公里以内的城市之间，"
                       + "现实中没有定期客运（高铁直达覆盖），请改用其他目的地。";
            if (IsCrossBorder(origin, dest) && !(IsGateway(origin) && IsGateway(dest)))
            {
                string weak = !IsGateway(origin) ? origin : dest;
                return $"{weak} 不是跨境口岸机场（{AirportCatalog.TierLabelOf(weak)}，"
                       + $"年客流未达 {MinGatewayPaxMillion:F0} 百万且非该国唯一机场），"
                       + "现实中不承接国际/地区定期客运；跨境航线请改用枢纽或干线机场。";
            }
            return "";
        }

        public static int PruneImplausible(List<RouteData> routes, Func<string, string, float> distanceKm)
        {
            if (routes == null || distanceKm == null) return 0;
            int n = 0;
            for (int i = routes.Count - 1; i >= 0; i--)
            {
                var r = routes[i];
                if (r == null) { routes.RemoveAt(i); n++; continue; }
                float km = distanceKm(r.origin, r.destination);
                if (IsPlausiblePassengerOd(r.origin, r.destination, km)) continue;
                routes.RemoveAt(i);
                n++;
            }
            return n;
        }
    }
}
