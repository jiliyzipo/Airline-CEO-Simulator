using System;
using System.Collections.Generic;
using AirlineCEO.Logic;

namespace AirlineCEO.Data
{
    [Serializable]
    public struct AirportInfo
    {
        public string code;   // IATA
        public string nameZh;
        public string nameEn;

        // —— P1 时刻资源属性（由底表推导，见 AirportCatalog 静态构造）——
        /// <summary>0 枢纽 / 1 干线 / 2 支线（数值小 = 更繁忙）。</summary>
        public int tier;
        /// <summary>是否协调机场（需时刻槽）。</summary>
        public bool coordinated;
        /// <summary>早/中/晚/夜 各时段可分配的时刻数。</summary>
        public int[] slotCapacity;
        /// <summary>宵禁窗口 "23:00-06:00"；空 = 无限制。</summary>
        public string curfew;

        public string Display => $"{nameZh} {code}";
    }

    /// <summary>MVP 机场目录（代码 / 中文 / 英文），供开通航线下拉与输入解析。</summary>
    public static class AirportCatalog
    {
        /// <summary>统一宵禁窗口（EEA 与另行列明的机场）。</summary>
        public const string CurfewWindow = "23:00-06:00";

        /// <summary>另有夜间限制、但不属 EEA / 中国单列的机场（真实宵禁）。</summary>
        static readonly string[] NightCurfewExtra = { "NRT", "HND", "SYD", "TPE" };

        static readonly AirportInfo[] All =
        {
            new AirportInfo { code = "PEK", nameZh = "北京首都", nameEn = "Beijing Capital" },
            new AirportInfo { code = "PKX", nameZh = "北京大兴", nameEn = "Beijing Daxing" },
            new AirportInfo { code = "SHA", nameZh = "上海虹桥", nameEn = "Shanghai Hongqiao" },
            new AirportInfo { code = "PVG", nameZh = "上海浦东", nameEn = "Shanghai Pudong" },
            new AirportInfo { code = "CAN", nameZh = "广州白云", nameEn = "Guangzhou Baiyun" },
            new AirportInfo { code = "SZX", nameZh = "深圳宝安", nameEn = "Shenzhen Baoan" },
            new AirportInfo { code = "CTU", nameZh = "成都双流", nameEn = "Chengdu Shuangliu" },
            new AirportInfo { code = "TFU", nameZh = "成都天府", nameEn = "Chengdu Tianfu" },
            new AirportInfo { code = "CKG", nameZh = "重庆江北", nameEn = "Chongqing Jiangbei" },
            new AirportInfo { code = "XIY", nameZh = "西安咸阳", nameEn = "Xian Xianyang" },
            new AirportInfo { code = "KMG", nameZh = "昆明长水", nameEn = "Kunming Changshui" },
            new AirportInfo { code = "HGH", nameZh = "杭州萧山", nameEn = "Hangzhou Xiaoshan" },
            new AirportInfo { code = "NKG", nameZh = "南京禄口", nameEn = "Nanjing Lukou" },
            new AirportInfo { code = "WUH", nameZh = "武汉天河", nameEn = "Wuhan Tianhe" },
            new AirportInfo { code = "CSX", nameZh = "长沙黄花", nameEn = "Changsha Huanghua" },
            new AirportInfo { code = "XMN", nameZh = "厦门高崎", nameEn = "Xiamen Gaoqi" },
            new AirportInfo { code = "FOC", nameZh = "福州长乐", nameEn = "Fuzhou Changle" },
            new AirportInfo { code = "TAO", nameZh = "青岛胶东", nameEn = "Qingdao Jiaodong" },
            new AirportInfo { code = "TSN", nameZh = "天津滨海", nameEn = "Tianjin Binhai" },
            new AirportInfo { code = "DLC", nameZh = "大连周水子", nameEn = "Dalian Zhoushuizi" },
            new AirportInfo { code = "SHE", nameZh = "沈阳桃仙", nameEn = "Shenyang Taoxian" },
            new AirportInfo { code = "HRB", nameZh = "哈尔滨太平", nameEn = "Harbin Taiping" },
            new AirportInfo { code = "URC", nameZh = "乌鲁木齐地窝堡", nameEn = "Urumqi Diwopu" },
            new AirportInfo { code = "LHW", nameZh = "兰州中川", nameEn = "Lanzhou Zhongchuan" },
            new AirportInfo { code = "SYX", nameZh = "三亚凤凰", nameEn = "Sanya Phoenix" },
            new AirportInfo { code = "HAK", nameZh = "海口美兰", nameEn = "Haikou Meilan" },
            new AirportInfo { code = "ICN", nameZh = "首尔仁川", nameEn = "Seoul Incheon" },
            new AirportInfo { code = "NRT", nameZh = "东京成田", nameEn = "Tokyo Narita" },
            new AirportInfo { code = "HND", nameZh = "东京羽田", nameEn = "Tokyo Haneda" },
            new AirportInfo { code = "KIX", nameZh = "大阪关西", nameEn = "Osaka Kansai" },
            new AirportInfo { code = "HKG", nameZh = "香港国际", nameEn = "Hong Kong" },
            new AirportInfo { code = "MFM", nameZh = "澳门国际", nameEn = "Macau" },
            new AirportInfo { code = "TPE", nameZh = "台北桃园", nameEn = "Taipei Taoyuan" },
            new AirportInfo { code = "SIN", nameZh = "新加坡樟宜", nameEn = "Singapore Changi" },
            new AirportInfo { code = "BKK", nameZh = "曼谷素万那普", nameEn = "Bangkok Suvarnabhumi" },
            new AirportInfo { code = "KUL", nameZh = "吉隆坡国际", nameEn = "Kuala Lumpur" },
            new AirportInfo { code = "LHR", nameZh = "伦敦希思罗", nameEn = "London Heathrow" },
            new AirportInfo { code = "CDG", nameZh = "巴黎戴高乐", nameEn = "Paris CDG" },
            new AirportInfo { code = "FRA", nameZh = "法兰克福", nameEn = "Frankfurt" },
            new AirportInfo { code = "AMS", nameZh = "阿姆斯特丹", nameEn = "Amsterdam" },
            new AirportInfo { code = "LAX", nameZh = "洛杉矶", nameEn = "Los Angeles" },
            new AirportInfo { code = "SFO", nameZh = "旧金山", nameEn = "San Francisco" },
            new AirportInfo { code = "JFK", nameZh = "纽约肯尼迪", nameEn = "New York JFK" },
            new AirportInfo { code = "SYD", nameZh = "悉尼", nameEn = "Sydney" },
            new AirportInfo { code = "DXB", nameZh = "迪拜", nameEn = "Dubai" },
            new AirportInfo { code = "DOH", nameZh = "多哈", nameEn = "Doha" },
            new AirportInfo { code = "IST", nameZh = "伊斯坦布尔", nameEn = "Istanbul" },
            new AirportInfo { code = "MAD", nameZh = "马德里", nameEn = "Madrid" },
            new AirportInfo { code = "BCN", nameZh = "巴塞罗那", nameEn = "Barcelona" },
            new AirportInfo { code = "FCO", nameZh = "罗马菲乌米奇诺", nameEn = "Rome Fiumicino" },
            new AirportInfo { code = "MUC", nameZh = "慕尼黑", nameEn = "Munich" },
            new AirportInfo { code = "ZRH", nameZh = "苏黎世", nameEn = "Zurich" },
            new AirportInfo { code = "VIE", nameZh = "维也纳", nameEn = "Vienna" },
            new AirportInfo { code = "CPH", nameZh = "哥本哈根", nameEn = "Copenhagen" },
            new AirportInfo { code = "ARN", nameZh = "斯德哥尔摩", nameEn = "Stockholm Arlanda" },
            new AirportInfo { code = "HEL", nameZh = "赫尔辛基", nameEn = "Helsinki" },
            new AirportInfo { code = "SVO", nameZh = "莫斯科谢列梅捷沃", nameEn = "Moscow Sheremetyevo" },
            new AirportInfo { code = "DEL", nameZh = "德里", nameEn = "Delhi" },
            new AirportInfo { code = "BOM", nameZh = "孟买", nameEn = "Mumbai" },
            new AirportInfo { code = "CGK", nameZh = "雅加达", nameEn = "Jakarta" },
            new AirportInfo { code = "MNL", nameZh = "马尼拉", nameEn = "Manila" },
            new AirportInfo { code = "ORD", nameZh = "芝加哥奥黑尔", nameEn = "Chicago O'Hare" },
            new AirportInfo { code = "ATL", nameZh = "亚特兰大", nameEn = "Atlanta" },
            new AirportInfo { code = "SEA", nameZh = "西雅图", nameEn = "Seattle" },
            new AirportInfo { code = "YVR", nameZh = "温哥华", nameEn = "Vancouver" },
            new AirportInfo { code = "YYZ", nameZh = "多伦多", nameEn = "Toronto" },
            new AirportInfo { code = "DFW", nameZh = "达拉斯沃斯堡", nameEn = "Dallas Fort Worth" },
            new AirportInfo { code = "DEN", nameZh = "丹佛", nameEn = "Denver" },
            new AirportInfo { code = "CLT", nameZh = "夏洛特道格拉斯", nameEn = "Charlotte Douglas" },
            new AirportInfo { code = "MIA", nameZh = "迈阿密", nameEn = "Miami" },
            new AirportInfo { code = "PHX", nameZh = "凤凰城天港", nameEn = "Phoenix Sky Harbor" },
            new AirportInfo { code = "LAS", nameZh = "拉斯维加斯哈里里德", nameEn = "Las Vegas Harry Reid" },
            new AirportInfo { code = "MCO", nameZh = "奥兰多", nameEn = "Orlando" },
            new AirportInfo { code = "IAH", nameZh = "休斯敦洲际", nameEn = "Houston Intercontinental" },
            new AirportInfo { code = "BOS", nameZh = "波士顿洛根", nameEn = "Boston Logan" },
            new AirportInfo { code = "MSP", nameZh = "明尼阿波利斯圣保罗", nameEn = "Minneapolis St Paul" },
            new AirportInfo { code = "DTW", nameZh = "底特律都会韦恩", nameEn = "Detroit Metro Wayne" },
            new AirportInfo { code = "SAN", nameZh = "圣迭戈", nameEn = "San Diego" },
            new AirportInfo { code = "GRU", nameZh = "圣保罗", nameEn = "Sao Paulo" },
            new AirportInfo { code = "EZE", nameZh = "布宜诺斯艾利斯", nameEn = "Buenos Aires" },
            new AirportInfo { code = "BOG", nameZh = "波哥大埃尔多拉多", nameEn = "Bogota El Dorado" },
            new AirportInfo { code = "SCL", nameZh = "圣地亚哥阿图罗梅里诺", nameEn = "Santiago Arturo Merino" },
            new AirportInfo { code = "LIM", nameZh = "利马豪尔赫查韦斯", nameEn = "Lima Jorge Chavez" },
            new AirportInfo { code = "JNB", nameZh = "约翰内斯堡", nameEn = "Johannesburg" },
            new AirportInfo { code = "CAI", nameZh = "开罗", nameEn = "Cairo" },
            new AirportInfo { code = "ADD", nameZh = "亚的斯亚贝巴博莱", nameEn = "Addis Ababa Bole" },
            new AirportInfo { code = "ACC", nameZh = "阿克拉科托卡", nameEn = "Accra Kotoka" },
            new AirportInfo { code = "CPT", nameZh = "开普敦", nameEn = "Cape Town" },
            new AirportInfo { code = "LOS", nameZh = "拉各斯穆尔塔拉穆罕默德", nameEn = "Lagos Murtala Muhammed" },
            new AirportInfo { code = "NBO", nameZh = "内罗毕乔莫肯雅塔", nameEn = "Nairobi Jomo Kenyatta" },
            new AirportInfo { code = "AKL", nameZh = "奥克兰", nameEn = "Auckland" },
            new AirportInfo { code = "MEL", nameZh = "墨尔本", nameEn = "Melbourne" },
            new AirportInfo { code = "BNE", nameZh = "布里斯班", nameEn = "Brisbane" },
            new AirportInfo { code = "PER", nameZh = "珀斯", nameEn = "Perth" },
        };

        // —— P1：等级 / 协调 / 槽池容量 / 宵禁 全部由底表推导，不手工录入 ——
        static AirportCatalog()
        {
            for (int i = 0; i < All.Length; i++)
            {
                var a = All[i];
                var eco = CityEconomyCatalog.Find(a.code);
                float pax = eco != null ? eco.paxMillion : 0f;
                float cargo = eco != null ? eco.cargoWanTon : 0f;

                a.tier = TierOf(pax, cargo);
                a.coordinated = a.tier <= 1;
                a.slotCapacity = CapacityOf(a.tier);
                a.curfew = CurfewWindowOf(a.code);
                All[i] = a;
            }
        }

        // 客流级：年旅客吞吐量（百万人次）
        static int PaxTier(float pax) => pax >= 45f ? 0 : pax >= 20f ? 1 : 2;

        // 货邮级：年货邮吞吐量（万吨）
        static int CargoTier(float cargo) => cargo >= 200f ? 0 : cargo >= 50f ? 1 : 2;

        /// <summary>等级取客流/货邮二者较优（避免纯客流误判 HKG、NRT 这类货运枢纽）。</summary>
        public static int TierOf(float paxMillion, float cargoWanTon)
            => Math.Min(PaxTier(paxMillion), CargoTier(cargoWanTon));

        /// <summary>越繁忙的机场，时隙池越大（单位时隙的竞争由 slot 价值体现）。</summary>
        static int[] CapacityOf(int tier) => tier switch
        {
            0 => new[] { 12, 12, 14, 8 },   // 枢纽
            1 => new[] { 8, 8, 8, 4 },      // 干线
            _ => new[] { 4, 4, 4, 2 },      // 支线
        };

        static string CurfewWindowOf(string code)
        {
            if (AirportRegions.IsEea(code)) return CurfewWindow;
            // 中国机场单独判定：仅 PEK 宵禁，其余（含 PKX）24 小时
            if (AirportRegions.IsChina(code))
                return AirportRegions.IsChinaCurfewAirport(code) ? CurfewWindow : "";
            for (int i = 0; i < NightCurfewExtra.Length; i++)
                if (string.Equals(NightCurfewExtra[i], code, StringComparison.OrdinalIgnoreCase))
                    return CurfewWindow;
            return "";
        }

        /// <summary>机场等级：0 枢纽 / 1 干线 / 2 支线（未知机场按支线）。</summary>
        public static int TierOfCode(string code)
        {
            var a = FindByCode(code);
            return string.IsNullOrEmpty(a.code) ? 2 : a.tier;
        }

        public static bool IsCoordinated(string code)
        {
            var a = FindByCode(code);
            return !string.IsNullOrEmpty(a.code) && a.coordinated;
        }

        /// <summary>该机场早/中/晚/夜四时段的可分配时刻数（下标 0–3）。</summary>
        public static int[] SlotCapacity(string code)
        {
            var a = FindByCode(code);
            if (a.slotCapacity != null) return a.slotCapacity;
            return CapacityOf(2);
        }

        /// <summary>指定时段的时刻池容量；band 越界按夜处理。</summary>
        public static int SlotCapacity(string code, int band)
        {
            var cap = SlotCapacity(code);
            int i = ClampBand(band);
            return cap != null && cap.Length > i ? cap[i] : 0;
        }

        public static int ClampBand(int band) => band < 0 ? 0 : band > 3 ? 3 : band;

        /// <summary>等级名称：0 枢纽 / 1 干线 / 2 支线。</summary>
        public static string TierLabel(int tier) => tier switch
        {
            0 => "枢纽",
            1 => "干线",
            _ => "支线"
        };

        /// <summary>某机场的等级名称。</summary>
        public static string TierLabelOf(string code) => TierLabel(TierOfCode(code));

        /// <summary>宵禁窗口原文（空 = 24 小时无限制）。</summary>
        public static string CurfewOf(string code)
        {
            var a = FindByCode(code);
            return a.curfew ?? "";
        }

        /// <summary>解析宵禁窗口为分钟数；无宵禁或格式非法返回 false。</summary>
        public static bool TryCurfew(string code, out int startMin, out int endMin)
        {
            startMin = endMin = 0;
            string c = CurfewOf(code);
            if (string.IsNullOrEmpty(c)) return false;
            int dash = c.IndexOf('-');
            if (dash <= 0) return false;
            string s = c.Substring(0, dash).Trim();
            string e = c.Substring(dash + 1).Trim();
            if (!TryParseHm(s, out int sm) || !TryParseHm(e, out int em)) return false;
            startMin = sm;
            endMin = em;
            return true;
        }

        /// <summary>某时刻（当天分钟）是否落在宵禁内（正确处理跨午夜窗口）。</summary>
        public static bool InCurfew(string code, int minutes)
        {
            if (!TryCurfew(code, out int s, out int e)) return false;
            int m = ((minutes % 1440) + 1440) % 1440;
            if (s == e) return false;
            return s < e ? (m >= s && m < e) : (m >= s || m < e);
        }

        /// <summary>宵禁的可读说明（如「23:00–06:00 宵禁」）；无宵禁返回空。</summary>
        public static string CurfewLabel(string code)
            => TryCurfew(code, out int s, out int e)
                ? $"{s / 60:00}:{s % 60:00}–{e / 60:00}:{e % 60:00} 宵禁"
                : "";

        static bool TryParseHm(string hm, out int minutes)
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

        public static IReadOnlyList<AirportInfo> Airports => All;

        public static bool TryResolve(string input, out AirportInfo airport)
        {
            airport = default;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string q = input.Trim();

            for (int i = 0; i < All.Length; i++)
            {
                var a = All[i];
                if (string.Equals(a.code, q, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.nameZh, q, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.nameEn, q, StringComparison.OrdinalIgnoreCase) ||
                    a.Display.Equals(q, StringComparison.OrdinalIgnoreCase))
                {
                    airport = a;
                    return true;
                }
            }

            // 模糊：包含中文名 / 英文片段 / 代码
            string qu = q.ToUpperInvariant();
            for (int i = 0; i < All.Length; i++)
            {
                var a = All[i];
                if ((!string.IsNullOrEmpty(a.nameZh) && a.nameZh.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(a.nameEn) && a.nameEn.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    a.code.IndexOf(qu, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    airport = a;
                    return true;
                }
            }

            // 兼容「北京」「上海」等简称
            if (q.Contains("北京") || qu == "PEK" || qu == "PKX")
            {
                airport = FindByCode("PEK");
                return true;
            }
            if (q.Contains("上海") || qu == "SHA" || qu == "PVG")
            {
                airport = FindByCode("SHA");
                return true;
            }
            if (q.Contains("广州") || qu == "CAN") { airport = FindByCode("CAN"); return true; }
            if (q.Contains("深圳") || qu == "SZX") { airport = FindByCode("SZX"); return true; }
            if (q.Contains("伦敦") || qu == "LHR") { airport = FindByCode("LHR"); return true; }
            if (q.Contains("首尔") || qu == "ICN") { airport = FindByCode("ICN"); return true; }

            return false;
        }

        public static AirportInfo FindByCode(string code)
        {
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].code, code, StringComparison.OrdinalIgnoreCase))
                    return All[i];
            return default;
        }

        /// <summary>展示用：机场中文名紧跟 IATA，如「北京首都 PEK」。</summary>
        public static string Label(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            var a = FindByCode(code);
            if (string.IsNullOrEmpty(a.code) || string.IsNullOrEmpty(a.nameZh))
                return code.ToUpperInvariant();
            return a.nameZh + " " + a.code;
        }

        public static string Od(string origin, string dest, string via = null)
        {
            if (string.IsNullOrEmpty(via))
                return Label(origin) + " → " + Label(dest);
            return Label(origin) + " → " + Label(via) + " → " + Label(dest);
        }

        public static List<string> DropdownOptions(bool includeNone)
        {
            var list = new List<string>(All.Length + 1);
            if (includeNone) list.Add("无经停");
            for (int i = 0; i < All.Length; i++)
                list.Add(All[i].Display);
            return list;
        }
    }
}
