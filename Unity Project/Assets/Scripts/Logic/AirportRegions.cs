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
    }
}
