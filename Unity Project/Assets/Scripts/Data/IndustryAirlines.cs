using System;

namespace AirlineCEO.Data
{
    /// <summary>
    /// 行业航司（他航）静态目录 —— 只为「航权全景 / 机场时刻」两张情报表提供对手方名单。
    ///
    /// ★2026-11-05（用户要求）：n77 此前只有我司的航权与时刻，CEO 看不到「这个机场还有谁在飞、
    /// 这条航权还有谁握着」。两张行业表需要一个**他航名单**做底表，而且必须是**静态**的 ——
    /// 航司不是在游戏里随季生灭的实体，名单每季变一次，同一张表前后就对不上，
    /// CEO 也就没法判断「这机场是不是越来越挤」。
    ///
    /// 这里只写「是谁、基地在哪、属于哪一档」，**不写**它持有哪些航权、占了多少时刻：
    /// 那些由 <c>IndustryAirlineLogic</c> 按机场/时段确定性推导（见该类的注释）。
    /// 写死在底表里看似省事，实则把「世界规模」和「航司名单」耦在一起，
    /// 以后想加一家航司就得手工补它全部的航权与时刻。
    /// </summary>
    public class IndustryAirline
    {
        /// <summary>中文名（与收件箱发信人、二级市场卖方同一套称呼）。</summary>
        public string name;
        /// <summary>
        /// 短名（「国航」「美联航」「伊比利亚」）—— ★2026-11-05 二次改版新增。
        ///
        /// 卡片视图里每个时刻是一个 107px 宽的小方块，塞不下「中国国际航空」六个字。
        /// 不做自动缩写（「中国国际航空」→「中国国」这种读起来像打错字），
        /// 改在底表里显式写一遍自己的简称 —— 与真实航司自称一致，也便于以后加航司。
        /// </summary>
        public string shortName;
        /// <summary>IATA 两字码。</summary>
        public string code;
        /// <summary>主基地机场 IATA。基地航司在本场占的槽与航权都更重。</summary>
        public string hub;
        /// <summary>0 全球网络型 / 1 区域型 / 2 本地型 —— 决定它在别国机场出现的概率与份额。</summary>
        public int tier;
        /// <summary>所属国家（与 <c>AirportRegions.CountryOf</c> 同口径，航权自由度按它判）。</summary>
        public string region;

        /// <summary>展示用名：有简称用简称，否则回落到全名。</summary>
        public string Display => string.IsNullOrEmpty(shortName) ? name : shortName;

        public static string TierLabel(int tier) => tier switch
        {
            0 => "全球网络型",
            1 => "区域型",
            _ => "本地型"
        };
    }

    /// <summary>他航名单底表（39 家，覆盖中国三大航 + 主要外航 + 若干本地承运人）。</summary>
    public static class IndustryAirlines
    {
        public static readonly IndustryAirline[] All =
        {
            // —— 全球网络型 ——
            new IndustryAirline { name = "中国国际航空", shortName = "国航", code = "CA", hub = "PEK", tier = 0, region = "中国" },
            new IndustryAirline { name = "中国东方航空", shortName = "东航", code = "MU", hub = "PVG", tier = 0, region = "中国" },
            new IndustryAirline { name = "中国南方航空", shortName = "南航", code = "CZ", hub = "CAN", tier = 0, region = "中国" },
            new IndustryAirline { name = "国泰航空",     shortName = "国泰", code = "CX", hub = "HKG", tier = 0, region = "中国香港" },
            new IndustryAirline { name = "汉莎航空",     shortName = "汉莎", code = "LH", hub = "FRA", tier = 0, region = "德国" },
            new IndustryAirline { name = "法国航空",     shortName = "法航", code = "AF", hub = "CDG", tier = 0, region = "法国" },
            new IndustryAirline { name = "荷兰皇家航空", shortName = "荷航", code = "KL", hub = "AMS", tier = 0, region = "荷兰" },
            new IndustryAirline { name = "英国航空",     shortName = "英航", code = "BA", hub = "LHR", tier = 0, region = "英国" },
            new IndustryAirline { name = "阿联酋航空",   shortName = "阿联酋", code = "EK", hub = "DXB", tier = 0, region = "阿联酋" },
            new IndustryAirline { name = "卡塔尔航空",   shortName = "卡航", code = "QR", hub = "DOH", tier = 0, region = "卡塔尔" },
            new IndustryAirline { name = "新加坡航空",   shortName = "新航", code = "SQ", hub = "SIN", tier = 0, region = "新加坡" },
            new IndustryAirline { name = "全日空",       shortName = "全日空", code = "NH", hub = "HND", tier = 0, region = "日本" },
            new IndustryAirline { name = "日本航空",     shortName = "日航", code = "JL", hub = "NRT", tier = 0, region = "日本" },
            new IndustryAirline { name = "大韩航空",     shortName = "大韩", code = "KE", hub = "ICN", tier = 0, region = "韩国" },
            new IndustryAirline { name = "达美航空",     shortName = "达美", code = "DL", hub = "ATL", tier = 0, region = "美国" },
            new IndustryAirline { name = "美国联合航空", shortName = "美联航", code = "UA", hub = "ORD", tier = 0, region = "美国" },
            new IndustryAirline { name = "美国航空",     shortName = "美航", code = "AA", hub = "DFW", tier = 0, region = "美国" },
            new IndustryAirline { name = "土耳其航空",   shortName = "土航", code = "TK", hub = "IST", tier = 0, region = "土耳其" },
            new IndustryAirline { name = "澳洲航空",     shortName = "澳航", code = "QF", hub = "SYD", tier = 0, region = "澳大利亚" },

            // —— 区域型 ——
            new IndustryAirline { name = "海南航空",     shortName = "海航", code = "HU", hub = "HAK", tier = 1, region = "中国" },
            new IndustryAirline { name = "厦门航空",     shortName = "厦航", code = "MF", hub = "XMN", tier = 1, region = "中国" },
            new IndustryAirline { name = "深圳航空",     shortName = "深航", code = "ZH", hub = "SZX", tier = 1, region = "中国" },
            new IndustryAirline { name = "四川航空",     shortName = "川航", code = "3U", hub = "CTU", tier = 1, region = "中国" },
            new IndustryAirline { name = "长荣航空",     shortName = "长荣", code = "BR", hub = "TPE", tier = 1, region = "中国台湾" },
            new IndustryAirline { name = "泰国国际航空", shortName = "泰航", code = "TG", hub = "BKK", tier = 1, region = "泰国" },
            new IndustryAirline { name = "马来西亚航空", shortName = "马航", code = "MH", hub = "KUL", tier = 1, region = "马来西亚" },
            new IndustryAirline { name = "北欧航空",     shortName = "北欧", code = "SK", hub = "CPH", tier = 1, region = "丹麦" },
            new IndustryAirline { name = "芬兰航空",     shortName = "芬航", code = "AY", hub = "HEL", tier = 1, region = "芬兰" },
            new IndustryAirline { name = "西班牙国家航空", shortName = "伊比利亚", code = "IB", hub = "MAD", tier = 1, region = "西班牙" },
            new IndustryAirline { name = "瑞士国际航空", shortName = "瑞航", code = "LX", hub = "ZRH", tier = 1, region = "瑞士" },
            new IndustryAirline { name = "俄罗斯航空",   shortName = "俄航", code = "SU", hub = "SVO", tier = 1, region = "俄罗斯" },
            new IndustryAirline { name = "印度航空",     shortName = "印航", code = "AI", hub = "DEL", tier = 1, region = "印度" },
            new IndustryAirline { name = "埃塞俄比亚航空", shortName = "埃塞", code = "ET", hub = "ADD", tier = 1, region = "埃塞俄比亚" },
            new IndustryAirline { name = "南非航空",     shortName = "南非", code = "SA", hub = "JNB", tier = 1, region = "南非" },

            // —— 本地型 ——
            new IndustryAirline { name = "华夏航空",     shortName = "华夏", code = "G5", hub = "CKG", tier = 2, region = "中国" },
            new IndustryAirline { name = "天津航空",     shortName = "天航", code = "GS", hub = "TSN", tier = 2, region = "中国" },
            new IndustryAirline { name = "首都航空",     shortName = "首都", code = "JD", hub = "PKX", tier = 2, region = "中国" },
            new IndustryAirline { name = "山东航空",     shortName = "山航", code = "SC", hub = "TNA", tier = 2, region = "中国" },
            new IndustryAirline { name = "乌鲁木齐航空", shortName = "乌航", code = "UQ", hub = "URC", tier = 2, region = "中国" }
        };

        /// <summary>按两字码找他航（找不到返回 null）。</summary>
        public static IndustryAirline FindByCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].code, code, StringComparison.OrdinalIgnoreCase)) return All[i];
            return null;
        }
    }
}
