using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 机场所属国家/区域判定（纯 C#，供税务 / 碳配额 EU ETS / CORSIA 共用）。
    /// 区域仅覆盖 MVP 机场目录中与税务、碳配额相关的关键市场。
    /// </summary>
    public static class AirportRegions
    {
        /// <summary>欧洲经济区（EEA）机场（EU ETS 适用范围，GDB 15.13.2）。</summary>
        static readonly string[] Eea =
        {
            "LHR", "CDG", "FRA", "AMS", "MAD", "BCN", "FCO", "MUC", "ZRH",
            "VIE", "CPH", "ARN", "HEL"
        };

        /// <summary>美国机场（企业所得税 21%，GDB 15.10）。</summary>
        static readonly string[] Us =
        {
            "LAX", "SFO", "JFK", "ORD", "ATL", "SEA",
            "DFW", "DEN", "CLT", "MIA", "PHX", "LAS", "MCO", "IAH", "BOS", "MSP", "DTW", "SAN"
        };

        /// <summary>中国机场（国内航线判定 / 默认枢纽国）。其余按 24 小时；夜间限制见 IsChinaCurfewAirport。</summary>
        static readonly string[] China =
        {
            "PEK", "PKX", "SHA", "PVG", "CAN", "SZX", "CTU", "TFU", "CKG", "XIY",
            "KMG", "HGH", "NKG", "WUH", "CSX", "XMN", "FOC", "TAO", "TSN", "DLC",
            "SHE", "HRB", "URC", "LHW", "SYX", "HAK",
            // 2026-10-21 批次 A：珠海金湾 / 无锡硕放 / 宁波栎社 / 温州龙湾
            "ZUH", "WUX", "NGB", "WNZ",
            // 2026-10-22 批次 B：南宁吴圩 / 贵阳龙洞堡 / 太原武宿 / 呼和浩特白塔 / 济南遥墙
            "NNG", "KWE", "TYN", "HET", "TNA",
            // 2026-10-22 批次 B'（K18 回补）：拉萨贡嘎。此前注释里写了它、名单里却没有 —— 名实不符，
            // 会让以后读代码的人以为 LXA 已被判定为国内机场。
            "LXA"
        };

        /// <summary>中国机场中单独实行夜间限制的机场（仅首都 PEK；PKX 及其余中国机场 24 小时）。</summary>
        static readonly string[] ChinaCurfew = { "PEK" };

        static bool In(string[] set, string iata)
        {
            if (string.IsNullOrEmpty(iata)) return false;
            for (int i = 0; i < set.Length; i++)
                if (string.Equals(set[i], iata, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public static bool IsEea(string iata) => In(Eea, iata);
        public static bool IsUs(string iata) => In(Us, iata);
        public static bool IsChina(string iata) => In(China, iata);
        public static bool IsChinaCurfewAirport(string iata) => In(ChinaCurfew, iata);

        /// <summary>是否为阿联酋（企业所得税 0%，GDB 15.10）。</summary>
        public static bool IsUae(string iata) =>
            string.Equals(iata, "DXB", StringComparison.OrdinalIgnoreCase);

        /// <summary>是否为新加坡（企业所得税 17%，GDB 15.10）。</summary>
        public static bool IsSingapore(string iata) =>
            string.Equals(iata, "SIN", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 机场所属国家（中文，与国家底表同源）。底表未收录时按 <see cref="IsChina"/> 名单兜底，
        /// 都没有则返回 ""（未知）—— 未知不等于「本国」，调用方必须显式处理。
        /// </summary>
        public static string CountryOf(string iata)
        {
            if (string.IsNullOrEmpty(iata)) return "";
            string c = iata.Trim().ToUpperInvariant();
            var eco = AirlineCEO.Data.CityEconomyCatalog.Find(c);
            if (eco != null && !string.IsNullOrEmpty(eco.countryZh)) return eco.countryZh.Trim();
            return IsChina(c) ? "中国" : "";
        }

        /// <summary>
        /// 航权自由度：相对**承运人本国**（<paramref name="homeCountry"/>）判定这一对航点属于哪种航权。
        ///
        /// ★2026-11-04 报障：「基地在巴黎，申请 PEK—SIN 第五航权是对的，CEO 又申请开通 CDG—SIN，
        /// 为什么会收到战略部发的申请 CDG—SIN 第五航权，而且从基地巴黎到新加坡也不是第五航权吧？」
        ///
        /// 旧口径只有「国际 = 要航权 = 第五航权」一条线：不管两端在哪，提案标题一律写「第五航权」、
        /// 一律扣 25 点。于是法国承运人回自己的基地 CDG 飞 SIN（第三/第四航权，只受法新双边协定管）
        /// 也被当成爬了第五航权，成本还按最难的那种收。这不只是措辞问题 —— 它让「本国 ↔ 对方」
        /// 这种最容易办的航权，和「他国 ↔ 他国」这种要三国点头的航权，在账上完全等价。
        ///
        /// 返回值：
        /// <list type="bullet">
        /// <item><b>0</b>　国内：两端同属本国，凭航线经营许可即可（不需要双边航权）；</item>
        /// <item><b>3</b>　第三/第四航权：本国 ↔ 他国。双边协定里最基础的一档，本国承运人的主业；</item>
        /// <item><b>5</b>　第五航权：他国 ↔ 他国。须接在本国始发的航段之后（如 CDG—PEK—SIN 的 PEK—SIN 段）；</item>
        /// <item><b>-1</b>　国内载权（他国境内两点之间）：任何协定都不给，承运人不得承运。</item>
        /// </list>
        /// </summary>
        public static int FreedomOf(string homeCountry, string origin, string dest)
        {
            string home = (homeCountry ?? "").Trim();
            string co = CountryOf(origin);
            string cd = CountryOf(dest);
            // 国家未知时退回旧口径「国际 = 第三/第四航权」，绝不默认成第五航权（那会多收 4 点关系）
            if (home.Length == 0 || co.Length == 0 || cd.Length == 0)
                return IsForeignEndpoint(origin) || IsForeignEndpoint(dest) ? 3 : 0;

            bool originHome = string.Equals(co, home, StringComparison.Ordinal);
            bool destHome = string.Equals(cd, home, StringComparison.Ordinal);
            if (originHome && destHome) return 0;       // 国内
            if (originHome || destHome) return 3;       // 本国 ↔ 他国
            if (string.Equals(co, cd, StringComparison.Ordinal)) return -1;   // 他国境内两点：国内载权
            return 5;                                   // 他国 ↔ 他国
        }

        /// <summary>航权自由度的展示名（提案标题、持仓列表共用一处，免得两处写两种说法）。</summary>
        public static string FreedomLabel(int freedom)
        {
            if (freedom <= 0) return "无需双边航权";
            if (freedom == 3) return "第三/第四航权";
            return $"第{freedom}航权";
        }

        /// <summary>
        /// 端点是否在中国内地以外（港澳台算境外）。**纯函数**，供 `tools/balance` 离线使用。
        ///
        /// 判定顺序与 <c>RoutePermitLogic.IsInternational</c> 的①相同：国家底表优先
        /// （<see cref="AirlineCEO.Data.CityEconomyCatalog"/>，写的是「中国 / 中国香港 / 韩国…」），
        /// 底表没收录时退回本类的 <see cref="IsChina"/> 名单。
        /// 选司界面要在**接管之前**算净美元敞口（K12 的「季末一次性项预计 ±X 万」），
        /// 而那时没有 <c>GameState</c>，故不能直接问 <c>RoutePermitLogic</c>。
        ///
        /// 两套判定的口径一致性由 `QuarterlyOneOffRegression` 断言：对每档预设航司的全部航线，
        /// 本函数与 <c>RoutePermitLogic.IsInternational</c> 必须给出相同答案 —— 新增机场只进了一边
        /// （例如只加进 AirportCatalog 而没进 CityEconomyCatalog）时回归会红，而不是默默把国际线
        /// 算成国内、让预告的敞口小一截。
        /// </summary>
        public static bool IsForeignEndpoint(string iata)
        {
            if (string.IsNullOrEmpty(iata)) return false;
            string c = iata.Trim().ToUpperInvariant();
            var eco = AirlineCEO.Data.CityEconomyCatalog.Find(c);
            if (eco != null && !string.IsNullOrEmpty(eco.countryZh))
                return !string.Equals(eco.countryZh.Trim(), "中国", StringComparison.Ordinal);
            return !IsChina(c);
        }

        /// <summary>航线是否国际/地区（任一端点在境外）。与 <c>RoutePermitLogic.IsInternational</c> 同口径，见上。</summary>
        public static bool IsInternational(string origin, string dest)
            => IsForeignEndpoint(origin) || IsForeignEndpoint(dest);
    }
}
