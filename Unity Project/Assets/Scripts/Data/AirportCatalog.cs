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
        /// <summary>早/中/晚/夜 各时段可分配的时刻数（由小时容量派生，见 <see cref="QuarterCapacity"/>）。</summary>
        public int[] slotCapacity;
        /// <summary>宵禁窗口 "23:00-06:00"；空 = 无限制。</summary>
        public string curfew;
        /// <summary>
        /// 协调时段（★2026-11-05 新增）：`"24h"` = 全天协调；`"07:00-23:00"` = 仅该时段内需时刻槽；
        /// 空 = 非协调机场（不设时刻槽）。
        ///
        /// 现实依据：《民航航班时刻管理办法》—— 主协调机场采用**24 小时全时段协调**；
        /// 部分辅协调机场只在高需求时段协调（珠海 07:00–23:00、贵阳 06:00–次日 02:00）。
        /// 与 <see cref="curfew"/> 是两层：协调 = 许可（没槽不能飞），宵禁 = 物理禁止（有槽也不能飞）。
        /// 非协调时段免槽，但宵禁照样拦。
        /// </summary>
        public string coordWindow;

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
            // 2024 年旅客吞吐量 1297 万 / 货邮 3.4 万吨（CAAC 2024 公报）
            new AirportInfo { code = "ZUH", nameZh = "珠海金湾", nameEn = "Zhuhai Jinwan" },
            // 1050 万 / 17.3 万吨
            new AirportInfo { code = "WUX", nameZh = "无锡硕放", nameEn = "Wuxi Shuofang" },
            // 1473 万 / 16.9 万吨
            new AirportInfo { code = "NGB", nameZh = "宁波栎社", nameEn = "Ningbo Lishe" },
            // 1253 万 / 12.9 万吨
            new AirportInfo { code = "WNZ", nameZh = "温州龙湾", nameEn = "Wenzhou Longwan" },
            // —— 2026-10-22 批次 B：五座「无超短航距」国内枢纽（CAAC 2024 公报实际值）——
            // 2024 年旅客吞吐量 1401 万 / 货邮 20.3 万吨
            new AirportInfo { code = "NNG", nameZh = "南宁吴圩", nameEn = "Nanning Wuxu" },
            // 2231 万 / 10.7 万吨
            new AirportInfo { code = "KWE", nameZh = "贵阳龙洞堡", nameEn = "Guiyang Longdongbao" },
            // 1478 万 / 6.2 万吨
            new AirportInfo { code = "TYN", nameZh = "太原武宿", nameEn = "Taiyuan Wusu" },
            // 1317 万 / 5.9 万吨
            new AirportInfo { code = "HET", nameZh = "呼和浩特白塔", nameEn = "Hohhot Baita" },
            // 2001 万 / 15.4 万吨
            new AirportInfo { code = "TNA", nameZh = "济南遥墙", nameEn = "Jinan Yaoqiang" },
            // —— 2026-10-22 批次 B'（K18 回补）：拉萨贡嘎 ——
            // 公报量级 550 万 / 4.4 万吨。**等级对数值不敏感**：客流离 2000 万、货邮离 50 万吨
            // 都差一个量级，5–6 百万之间取任何值都推导为支线（tier 2）—— 所以这里只要量级正确，
            // 不必逐位对齐公报；协调属性 / 槽池 / 宵禁都由静态构造推导，不受这里影响。
            new AirportInfo { code = "LXA", nameZh = "拉萨贡嘎", nameEn = "Lhasa Gonggar" },
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

                a.tier = TierWithPromotion(a.code, TierOf(pax, cargo));
                a.coordinated = a.tier <= 1;
                a.coordWindow = CoordWindowFor(a.code, a.tier);
                a.slotCapacity = new[]
                {
                    BandCapacity(a.code, a.coordWindow, a.tier, 0),
                    BandCapacity(a.code, a.coordWindow, a.tier, 1),
                    BandCapacity(a.code, a.coordWindow, a.tier, 2),
                    BandCapacity(a.code, a.coordWindow, a.tier, 3),
                };
                a.curfew = CurfewWindowOf(a.code);
                All[i] = a;
            }
        }

        // ————————————————— 容量：小时容量 → 15 分钟容量（★2026-11-05）—————————————————
        //
        // 现实锚点：《民航航班时刻管理办法》第七条 —— 协调参数是「机场特定 1 小时或者
        // 15 分钟内可提供的航班时刻数量」。故容量先按**小时**定义，再按确定性规则分配到
        // 该小时的 4 个 15 分钟窗口；两套读数出自同一份数据，不会互相打脸。
        //
        // 分配规则（对小时容量 r，见 <see cref="QuarterSplit"/>）：
        //   r=1 → :00 1 条；r=2 → :00 :15 各 1 条；r=3 → :00 2 条 + :15 1 条；
        //   r=4 → :00 2 + :15 :30 各 1；r=5 → :00 2 + 其余各 1；
        //   r=6 → :00 :15 各 2 + :30 :45 各 1；r≥7 → 均分 base=r/4，余数优先给靠前的刻。
        //   r=0 → 该小时无槽。
        //
        // 数值标定（★2026-11-07 改口径）：旧版是「峰值维持、新增只落在肩峰与夜间」的两档死表
        // （枢纽日合计 56、干线 33），摊到四刻必然每刻 1 条，正是用户报的「容量恒为 1」。
        // 现改为逐场真实全天曲线 × 1/3.5（见 <see cref="AirportHourRates"/>）：枢纽日容量
        // 升至 169–313 条，峰刻 3–6 条；干线 74–104 条，峰刻 2–3 条。

        /// <summary>枢纽每小时可分配槽数（下标 = 小时）。日合计 56。</summary>
        static readonly int[] HubHourRate =
        {
            0, 0, 0, 0, 0, 0,                        // 00–05：宵禁/无需求
            3, 4, 4, 4,                              // 06–09：早高峰
            1, 3, 5, 5, 2, 1,                        // 10–15：午高峰 + 午后低谷
            4, 4, 4, 4,                              // 16–19：晚高峰（全天最旺）
            3, 2, 2, 1                               // 20–23：夜
        };

        /// <summary>干线每小时可分配槽数。日合计 33。</summary>
        static readonly int[] MainHourRate =
        {
            0, 0, 0, 0, 0, 0,                        // 00–05
            2, 2, 3, 2,                              // 06–09
            1, 2, 3, 3, 1, 1,                        // 10–15
            2, 2, 3, 2,                              // 16–19
            2, 1, 1, 0                               // 20–23
        };

        /// <summary>
        /// **逐场**每小时可分配槽数（单方向，下标 = 小时）。★2026-11-07。
        ///
        /// 为什么不再用「枢纽 / 干线两张 24 格表」：那两张表是手工调的，峰谷比 5 倍，
        /// 而现实机场 7–22 时的峰谷比只有 1.28；更要命的是日总量只有 56 / 33 条，
        /// 摊到四个 15 分钟刻就必然「每刻 1 条」—— 玩家看到的「容量 1 条」就是这么来的。
        ///
        /// 本表的数 = 各地机场**现实离港架次 ÷ 3.5**，形状取 CAAC 逐小时实测
        /// （中国 23 场见对照表 §5.1 / §5.2）或逐场公开曲线（境外 61 场见 §10.5）。
        /// 用户 2026-11-06 裁决：走「真实全天曲线 + 统一压缩比 1/3.5」。
        ///
        /// 表里没有的机场（如 MFM / SHE / LHW）回落到等级曲线 × <see cref="LegacyScale"/>，
        /// 保证「有协调属性就一定有槽」。
        /// </summary>
        static readonly Dictionary<string, int[]> AirportHourRates = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
        // —— 中国主协调机场（CAAC 实测全天曲线 × 1/3.5，来源见对照表 §5.1 / §5.2）——
            { "CAN", new[] { 5, 3, 1, 1, 1, 1, 4, 9, 10, 11, 11, 11, 12, 11, 11, 11, 12, 12, 11, 12, 11, 11, 10, 8 } },
            { "CKG", new[] { 3, 2, 1, 1, 1, 1, 3, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 7, 7, 6, 5 } },
            { "CSX", new[] { 2, 1, 1, 0, 0, 0, 2, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 4, 5, 4, 3 } },
            { "CTU", new[] { 2, 1, 1, 0, 1, 1, 2, 4, 4, 4, 5, 5, 5, 4, 5, 4, 5, 5, 4, 5, 4, 4, 4, 3 } },
            { "DLC", new[] { 1, 0, 0, 0, 0, 0, 1, 3, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2, 2 } },
            { "FOC", new[] { 1, 1, 0, 0, 0, 0, 1, 2, 2, 2, 2, 2, 3, 2, 3, 2, 3, 3, 2, 3, 2, 2, 2, 2 } },
            { "HAK", new[] { 2, 1, 1, 0, 1, 1, 2, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3 } },
            { "HGH", new[] { 3, 2, 1, 1, 1, 1, 3, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 7, 7, 6, 5 } },
            { "KMG", new[] { 3, 2, 1, 1, 1, 1, 3, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 7, 7, 6, 5 } },
            { "NKG", new[] { 2, 1, 1, 1, 1, 1, 2, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 4, 4 } },
            { "PEK", new[] { 5, 3, 2, 2, 2, 2, 4, 8, 9, 9, 9, 10, 9, 9, 10, 9, 9, 9, 9, 9, 9, 8, 8, 6 } },
            { "PKX", new[] { 3, 2, 1, 1, 1, 1, 1, 5, 7, 8, 7, 7, 7, 7, 7, 7, 8, 8, 7, 8, 6, 7, 6, 5 } },
            { "PVG", new[] { 7, 4, 3, 2, 3, 3, 5, 8, 10, 12, 11, 10, 11, 11, 11, 11, 11, 11, 11, 11, 10, 11, 10, 8 } },
            { "SHA", new[] { 3, 1, 1, 1, 1, 1, 2, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 5, 5 } },
            { "SYX", new[] { 1, 1, 0, 0, 0, 0, 1, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 } },
            { "SZX", new[] { 5, 2, 1, 1, 1, 1, 3, 8, 8, 9, 9, 9, 10, 9, 9, 9, 10, 10, 9, 10, 9, 9, 8, 7 } },
            { "TAO", new[] { 2, 1, 1, 0, 0, 0, 1, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3, 3 } },
            { "TFU", new[] { 4, 2, 1, 1, 1, 1, 3, 7, 8, 8, 8, 8, 9, 8, 8, 8, 9, 9, 8, 9, 8, 8, 7, 6 } },
            { "TSN", new[] { 2, 1, 1, 0, 1, 0, 1, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 3, 3, 3, 3 } },
            { "URC", new[] { 2, 1, 1, 0, 1, 1, 2, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3, 3 } },
            { "WUH", new[] { 2, 1, 1, 0, 1, 0, 2, 4, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 4, 4, 4, 3 } },
            { "XIY", new[] { 3, 2, 1, 1, 1, 1, 3, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 7, 7, 6, 5 } },
            { "XMN", new[] { 2, 1, 0, 0, 0, 0, 2, 3, 4, 4, 4, 4, 5, 4, 4, 4, 4, 4, 4, 5, 4, 4, 4, 4 } },

        // —— 境外机场（逐场实测/推算曲线 × 1/3.5，来源见对照表 §10.5）——
            { "ADD", new[] { 1, 1, 0, 0, 0, 1, 2, 4, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 5, 5, 4, 3, 2 } },
            { "AMS", new[] { 0, 0, 0, 0, 0, 0, 3, 10, 11, 12, 12, 12, 12, 12, 11, 12, 12, 12, 12, 11, 11, 11, 7, 4 } },
            { "ARN", new[] { 0, 0, 0, 0, 0, 0, 1, 5, 5, 6, 6, 6, 5, 6, 5, 5, 6, 6, 5, 5, 5, 5, 3, 2 } },
            { "ATL", new[] { 4, 2, 1, 1, 1, 5, 12, 18, 21, 19, 17, 18, 20, 18, 17, 19, 21, 19, 18, 20, 17, 13, 8, 4 } },
            { "BCN", new[] { 0, 0, 0, 0, 0, 0, 2, 7, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 7, 5, 3 } },
            { "BKK", new[] { 3, 2, 1, 1, 1, 1, 4, 7, 8, 9, 8, 8, 8, 8, 8, 8, 8, 8, 9, 8, 8, 7, 5, 4 } },
            { "BNE", new[] { 0, 0, 0, 0, 0, 0, 2, 5, 6, 7, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 5, 4, 2, 0 } },
            { "BOG", new[] { 1, 0, 0, 0, 0, 0, 2, 5, 6, 6, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 7, 6, 4, 3 } },
            { "BOM", new[] { 2, 1, 0, 0, 0, 1, 2, 4, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 5, 4, 3, 2 } },
            { "BOS", new[] { 2, 1, 1, 0, 1, 3, 7, 11, 13, 12, 10, 11, 12, 11, 10, 12, 13, 12, 11, 12, 11, 8, 5, 3 } },
            { "CAI", new[] { 2, 1, 0, 0, 0, 1, 3, 5, 7, 8, 8, 8, 8, 7, 7, 7, 7, 8, 7, 7, 6, 5, 4, 2 } },
            { "CDG", new[] { 0, 0, 0, 0, 0, 0, 3, 9, 11, 12, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 10, 7, 4 } },
            { "CGK", new[] { 3, 2, 1, 1, 1, 1, 4, 7, 9, 9, 9, 8, 8, 8, 8, 8, 9, 9, 9, 9, 8, 7, 5, 4 } },
            { "CLT", new[] { 3, 1, 1, 0, 1, 4, 9, 14, 16, 14, 13, 14, 15, 13, 12, 14, 16, 14, 13, 15, 13, 10, 6, 3 } },
            { "CPH", new[] { 0, 0, 0, 0, 0, 0, 2, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 4, 2 } },
            { "DEL", new[] { 2, 1, 1, 0, 0, 1, 3, 7, 9, 10, 10, 10, 10, 9, 9, 9, 9, 10, 10, 9, 8, 7, 5, 3 } },
            { "DEN", new[] { 3, 1, 1, 1, 1, 4, 10, 16, 18, 16, 15, 16, 17, 15, 14, 16, 18, 17, 16, 17, 15, 11, 7, 4 } },
            { "DFW", new[] { 4, 2, 1, 1, 1, 4, 11, 17, 20, 18, 16, 17, 19, 16, 16, 18, 19, 18, 17, 19, 16, 12, 8, 4 } },
            { "DOH", new[] { 8, 7, 6, 5, 5, 5, 7, 10, 11, 12, 12, 13, 12, 12, 11, 12, 12, 12, 12, 12, 11, 10, 9, 9 } },
            { "DTW", new[] { 3, 1, 1, 0, 1, 3, 8, 13, 15, 14, 12, 13, 15, 13, 12, 14, 15, 14, 13, 14, 12, 10, 6, 3 } },
            { "DXB", new[] { 9, 8, 6, 5, 5, 5, 7, 10, 12, 13, 13, 14, 13, 13, 12, 13, 13, 13, 13, 13, 12, 11, 10, 9 } },
            { "EZE", new[] { 1, 0, 0, 0, 0, 0, 2, 4, 5, 5, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 6, 5, 4, 2 } },
            { "FCO", new[] { 0, 0, 0, 0, 0, 0, 2, 6, 7, 8, 8, 8, 8, 8, 7, 8, 8, 8, 8, 7, 7, 7, 5, 2 } },
            { "FRA", new[] { 0, 0, 0, 0, 0, 0, 3, 9, 10, 11, 11, 11, 10, 11, 10, 10, 11, 11, 10, 10, 10, 10, 7, 3 } },
            { "GRU", new[] { 2, 1, 0, 0, 0, 0, 3, 6, 7, 8, 8, 8, 8, 9, 9, 9, 9, 9, 10, 10, 9, 7, 5, 3 } },
            { "HEL", new[] { 0, 0, 0, 0, 0, 0, 1, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 4, 4, 3, 1 } },
            { "HKG", new[] { 3, 2, 1, 1, 1, 1, 4, 8, 9, 9, 9, 8, 8, 8, 8, 8, 9, 9, 9, 9, 8, 7, 6, 4 } },
            { "HND", new[] { 4, 2, 1, 1, 1, 2, 5, 10, 12, 13, 12, 11, 11, 11, 11, 11, 12, 12, 12, 12, 11, 10, 8, 5 } },
            { "IAH", new[] { 3, 1, 1, 0, 1, 4, 9, 14, 17, 15, 13, 14, 16, 14, 13, 15, 16, 15, 14, 15, 13, 10, 6, 3 } },
            { "ICN", new[] { 3, 2, 1, 1, 1, 2, 5, 9, 11, 11, 11, 10, 10, 10, 10, 10, 11, 11, 11, 11, 10, 9, 7, 5 } },
            { "IST", new[] { 7, 6, 5, 4, 4, 4, 6, 8, 10, 10, 10, 11, 10, 10, 10, 10, 10, 11, 10, 10, 10, 9, 8, 7 } },
            { "JFK", new[] { 2, 1, 1, 0, 1, 3, 7, 11, 13, 11, 10, 11, 12, 10, 10, 11, 12, 11, 11, 12, 10, 8, 5, 3 } },
            { "JNB", new[] { 1, 1, 0, 0, 0, 1, 2, 4, 6, 6, 7, 7, 6, 6, 6, 6, 6, 6, 6, 6, 5, 4, 3, 2 } },
            { "KIX", new[] { 2, 1, 1, 0, 0, 1, 3, 5, 6, 7, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 5, 4, 3 } },
            { "KUL", new[] { 3, 2, 1, 1, 1, 1, 4, 7, 9, 9, 9, 8, 8, 8, 8, 8, 9, 9, 9, 9, 8, 7, 5, 4 } },
            { "LAS", new[] { 6, 4, 2, 1, 1, 2, 5, 10, 12, 14, 15, 15, 15, 14, 14, 14, 14, 15, 14, 14, 13, 11, 9, 7 } },
            { "LAX", new[] { 6, 4, 2, 1, 1, 1, 5, 9, 12, 13, 14, 15, 14, 14, 13, 13, 14, 14, 14, 13, 12, 10, 8, 7 } },
            { "LHR", new[] { 0, 0, 0, 0, 0, 0, 3, 10, 11, 12, 12, 12, 12, 12, 11, 12, 12, 12, 12, 11, 11, 11, 7, 4 } },
            { "LIM", new[] { 1, 0, 0, 0, 0, 0, 2, 4, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 5, 3, 2 } },
            { "MAD", new[] { 0, 0, 0, 0, 0, 0, 3, 8, 9, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 9, 9, 6, 3 } },
            { "MCO", new[] { 6, 3, 2, 1, 1, 1, 4, 8, 11, 12, 13, 13, 13, 12, 12, 12, 12, 12, 12, 12, 11, 9, 8, 6 } },
            { "MEL", new[] { 0, 0, 0, 0, 0, 0, 2, 6, 7, 7, 7, 7, 7, 6, 7, 7, 7, 7, 7, 7, 6, 5, 3, 0 } },
            { "MIA", new[] { 5, 3, 1, 1, 1, 1, 4, 8, 10, 11, 12, 12, 12, 11, 11, 11, 11, 12, 11, 11, 10, 9, 7, 6 } },
            { "MNL", new[] { 2, 1, 1, 0, 0, 1, 2, 5, 5, 6, 5, 5, 5, 5, 5, 5, 5, 5, 6, 5, 5, 4, 3, 2 } },
            { "MSP", new[] { 3, 1, 1, 0, 1, 3, 8, 12, 14, 13, 11, 12, 13, 12, 11, 13, 14, 13, 12, 13, 11, 9, 5, 3 } },
            { "MUC", new[] { 0, 0, 0, 0, 0, 0, 2, 6, 7, 8, 8, 8, 7, 8, 7, 7, 8, 8, 7, 7, 7, 7, 5, 2 } },
            { "NRT", new[] { 2, 1, 1, 0, 0, 1, 3, 5, 6, 6, 6, 5, 5, 5, 5, 5, 6, 6, 6, 6, 5, 5, 4, 3 } },
            { "ORD", new[] { 4, 2, 1, 1, 1, 5, 11, 18, 21, 18, 16, 18, 20, 17, 16, 18, 20, 19, 17, 19, 17, 13, 8, 4 } },
            { "PHX", new[] { 5, 3, 1, 1, 1, 1, 4, 8, 10, 11, 12, 12, 12, 11, 11, 11, 11, 12, 11, 11, 10, 9, 7, 6 } },
            { "SAN", new[] { 2, 1, 1, 0, 0, 1, 2, 4, 5, 5, 5, 6, 6, 5, 5, 5, 5, 5, 5, 5, 5, 4, 3, 3 } },
            { "SCL", new[] { 1, 0, 0, 0, 0, 0, 2, 4, 5, 5, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 6, 5, 4, 2 } },
            { "SEA", new[] { 2, 1, 0, 0, 1, 3, 6, 10, 11, 10, 9, 10, 11, 9, 9, 10, 11, 10, 10, 11, 9, 7, 4, 2 } },
            { "SFO", new[] { 2, 1, 0, 0, 0, 2, 5, 8, 10, 9, 8, 8, 9, 8, 8, 9, 9, 9, 8, 9, 8, 6, 4, 2 } },
            { "SIN", new[] { 3, 2, 1, 1, 1, 2, 5, 9, 10, 11, 10, 10, 10, 10, 10, 10, 10, 10, 11, 10, 9, 8, 7, 5 } },
            { "SVO", new[] { 0, 0, 0, 0, 0, 0, 2, 5, 6, 7, 7, 7, 6, 7, 6, 6, 7, 7, 6, 6, 6, 6, 4, 2 } },
            { "SYD", new[] { 0, 0, 0, 0, 0, 0, 3, 7, 8, 9, 8, 8, 8, 7, 8, 8, 8, 8, 8, 8, 7, 6, 3, 1 } },
            { "TPE", new[] { 2, 1, 1, 0, 0, 1, 3, 5, 6, 6, 6, 5, 5, 5, 5, 5, 6, 6, 6, 6, 5, 5, 4, 3 } },
            { "VIE", new[] { 0, 0, 0, 0, 0, 0, 2, 5, 6, 7, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 4, 2 } },
            { "YVR", new[] { 4, 2, 1, 1, 0, 1, 3, 6, 7, 8, 9, 9, 9, 9, 8, 8, 9, 9, 9, 8, 7, 6, 5, 4 } },
            { "YYZ", new[] { 2, 1, 0, 0, 1, 2, 6, 9, 11, 10, 9, 9, 10, 9, 8, 10, 11, 10, 9, 10, 9, 7, 4, 2 } },
            { "ZRH", new[] { 0, 0, 0, 0, 0, 0, 2, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 6, 4, 2 } },
        };

        /// <summary>表里没有的机场：等级曲线 × 3（≈ 1/3.5 的量级）。</summary>
        const int LegacyScale = 3;

        /// <summary>逐场小时容量；缺表时按等级回落。</summary>
        static int HourRateOf(string code, int tier, int hour)
        {
            if (hour < 0 || hour > 23) return 0;
            if (!string.IsNullOrEmpty(code) && AirportHourRates.TryGetValue(code, out var rate))
                return rate[hour];
            return HourRate(tier, hour) * LegacyScale;
        }

        /// <summary>某机场某小时的可分配槽数（单方向）。</summary>
        public static int HourRate(string code, int hour)
            => HourRateOf(code, TierOfCode(code), hour);

        /// <summary>
        /// 把一小时容量 `r` 分配到四个 15 分钟窗口。★2026-11-07 起**允许刻内不均匀**：
        /// 用户 2026-11-06 裁决 `r=3 → 2/1/0/0`、`r=4 → 2/1/1/0`，
        /// 宁可让 :30 / :45 空着，也要让最小的机场出现「同刻两家」。
        /// r ≥ 7 时按 `base = r/4` 均分，余数优先给靠前的刻（大机场每刻 5–7 条是常态）。
        /// </summary>
        static void QuarterSplit(int r, out int q0, out int q1, out int q2, out int q3)
        {
            q0 = q1 = q2 = q3 = 0;
            if (r <= 0) return;
            switch (r)
            {
                case 1: q0 = 1; return;
                case 2: q0 = 1; q1 = 1; return;
                case 3: q0 = 2; q1 = 1; return;
                case 4: q0 = 2; q1 = 1; q2 = 1; return;
                case 5: q0 = 2; q1 = q2 = q3 = 1; return;
                case 6: q0 = q1 = 2; q2 = q3 = 1; return;
            }
            int b = r / 4, rem = r % 4;
            q0 = b + (rem > 0 ? 1 : 0);
            q1 = b + (rem > 1 ? 1 : 0);
            q2 = b + (rem > 2 ? 1 : 0);
            q3 = b;
        }

        /// <summary>某等级某小时的容量（支线 = 0：非协调，不设时刻槽）。</summary>
        public static int HourRate(int tier, int hour)
        {
            if (hour < 0 || hour > 23) return 0;
            if (tier == 0) return HubHourRate[hour];
            if (tier == 1) return MainHourRate[hour];
            return 0;
        }

        /// <summary>
        /// 某时刻所在的 **15 分钟窗口容量**（0..7 条）。非协调时段返回 0。
        /// 这是「该刻最多放几条槽」的唯一判据。
        ///
        /// ★2026-11-07：容量改由**逐场小时容量**派生（见 <see cref="AirportHourRates"/>），
        /// 并允许刻内不均匀 —— 大机场峰值刻会出现 5–7 条，不再全场「每刻 1 条」。
        /// </summary>
        public static int QuarterCapacity(string code, int minutes)
        {
            return QuarterCapacityRaw(code, TierOfCode(code), CoordWindowOf(code), minutes);
        }

        /// <summary>
        /// 某时刻是否需要时刻槽：协调机场 **且** 该时刻落在协调时段内。
        /// 注意与「该刻有没有余量」是两件事 —— 24h 协调机场的 02:00 需要槽，
        /// 只是小时容量为 0（排不到），故这里不看 <see cref="QuarterCapacity"/>。
        /// </summary>
        public static bool IsCoordinatedAt(string code, int minutes)
            => !string.IsNullOrEmpty(CoordWindowOf(code)) && InCoordWindow(code, minutes);

        // ————————————————— 刻钟的四种状态（★2026-11-05）—————————————————
        //
        // 用户问询「为什么会有无槽时刻」，查下来是**三种完全不同的东西被塞进同一个灰块**：
        //   ① 协调时段外 —— 该刻**不需要**槽（可以飞），画成「无槽」等于误报禁飞；
        //   ② 该小时容量 0 —— 机场该小时不分配时刻（如干线 23:00），这是真信息；
        //   ③ 小时容量 1–3 时未落选的刻 —— 只是「这一小时只有 r 条、铺不满四个刻」的
        //      派生副产品，本身**没有独立含义**（枢纽 7/51、干线 23/51 都是它）。
        // 裁决：③ 不再画（卡片只呈现机场公布的槽位表），①② 各自单列一态并写明文字。

        /// <summary>该刻是机场公布的槽位（可以申请）。</summary>
        public const int QuarterPublished = 0;
        /// <summary>协调时段内、但该小时容量为 0：机场这一小时不分配时刻。</summary>
        public const int QuarterClosed = 1;
        /// <summary>协调时段外：该刻不需要时刻槽。</summary>
        public const int QuarterExempt = 2;
        /// <summary>协调时段内、该小时有余量，但这一分钟不是公布的槽位（不画）。</summary>
        public const int QuarterUnpublished = 3;

        /// <summary>某时刻属于哪一种刻钟状态（见上方四态的说明）。</summary>
        public static int QuarterState(string code, int minutes)
        {
            if (!IsCoordinatedAt(code, minutes)) return QuarterExempt;
            if (HourCapacity(code, minutes / 60) <= 0) return QuarterClosed;
            return QuarterCapacity(code, minutes) > 0 ? QuarterPublished : QuarterUnpublished;
        }

        /// <summary>四态的可读名（界面与邮件共用，避免两处各写一套措辞）。</summary>
        public static string QuarterStateLabel(int state)
            => state == QuarterClosed ? "不开放"
             : state == QuarterExempt ? "免槽"
             : state == QuarterUnpublished ? "未公布"
             : "已公布";


        // 客流级：年旅客吞吐量（百万人次）
        static int PaxTier(float pax) => pax >= 45f ? 0 : pax >= 20f ? 1 : 2;

        // 货邮级：年货邮吞吐量（万吨）
        static int CargoTier(float cargo) => cargo >= 200f ? 0 : cargo >= 50f ? 1 : 2;

        /// <summary>等级取客流/货邮二者较优（避免纯客流误判 HKG、NRT 这类货运枢纽）。</summary>
        public static int TierOf(float paxMillion, float cargoWanTon)
            => Math.Min(PaxTier(paxMillion), CargoTier(cargoWanTon));

        /// <summary>越繁忙的机场，时隙池越大（单位时隙的竞争由 slot 价值体现）。</summary>
        /// <remarks>
        /// ★2026-11-05：容量不再按四个需求波给死表，改由 <see cref="HourRate"/> 的小时容量
        /// 派生（见上方「容量：小时容量 → 15 分钟容量」）。四个波的总量仍保持
        /// 枢纽 12/12/14/8、干线 8/8/8/4（回归 R50 对账），只是多了肩峰与夜间的槽。
        /// </remarks>
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

        /// <summary>该机场一天可分配的时刻总数（逐场小时容量之和；非协调机场 0）。</summary>
        public static int DayCapacity(string code)
        {
            int n = 0;
            for (int h = 0; h < 24; h++) n += HourCapacity(code, h);
            return n;
        }

        /// <summary>某机场某小时的容量（支线 = 0）。</summary>
        public static int HourCapacity(string code, int hour)
            => HourRate(code, hour);

        /// <summary>是否协调机场（需时刻槽）。</summary>
        public static bool IsCoordinated(string code)
        {
            var a = FindByCode(code);
            return !string.IsNullOrEmpty(a.code) && !string.IsNullOrEmpty(a.coordWindow);
        }

        /// <summary>该机场早/中/晚/夜四时段的可分配时刻数（下标 0–3，由小时容量派生）。</summary>
        public static int[] SlotCapacity(string code)
        {
            var a = FindByCode(code);
            if (a.slotCapacity != null) return a.slotCapacity;
            return new[] { 0, 0, 0, 0 };
        }

        /// <summary>指定时段的时刻池容量；band 越界按夜处理。</summary>
        public static int SlotCapacity(string code, int band)
        {
            var cap = SlotCapacity(code);
            int i = ClampBand(band);
            return cap != null && cap.Length > i ? cap[i] : 0;
        }

        /// <summary>
        /// 指定时段**某一类**时刻池的容量（★2026-11-06：到港 / 离港分池）。
        ///
        /// 两个池取**同一个数**，这是有意的、不是偷懒：
        /// 本表的容量本来按「起降合计架次 ÷ 2」定标，一条槽 = 一进一出，
        /// 所以「÷2」算出来的就是**每一端**的容量。换句话说，拆成两池之后
        /// 每池的数值不变、总量也不变 —— 一个停站各取一条，能飞多少班与拆分前一致。
        ///
        /// 为什么不拆成两个不同的数：公开资料几乎只发布起降合计，逐时「到达容量 / 离场容量」
        /// 只有 LHR / JFK / SFO 这类极端场有定性描述（见 docs/handoff 第 13 节）。
        /// 没有实测就把总数按比例劈成两个不同值，等于**发明数据** —— 与「按现实走」冲突。
        /// 待拿到分端实测再逐场覆盖，接口已经留好。
        /// </summary>
        public static int SlotCapacity(string code, int band, string kind)
        {
            // 目前两端同数：kind 只用于区分「这是哪个池」，不改变数量。
            // 传 `"ARR"` 与传空串结果相同，调用点因此可以自由按端取值。
            _ = kind;
            return SlotCapacity(code, band);
        }

        // ————————————————— 协调时段（★2026-11-05）—————————————————

        /// <summary>`"24h"` 表示全天协调。</summary>
        public const string CoordAllDay = "24h";

        /// <summary>限时协调的例外名单（照搬宵禁那种写法，逐条可查）。</summary>
        static readonly string[] LimitedCoordAirports = { "ZUH", "KWE" };
        static readonly string[] LimitedCoordWindows = { "07:00-23:00", "06:00-02:00" };

        /// <summary>
        /// 等级例外名单（★2026-11-05）：底表由客货吞吐量推导等级，个别机场需要人工提级，
        /// 否则**写在别的名单里也永远不生效**。
        ///
        /// 现实锚点：珠海金湾 2024 年 1297 万人次，按客流/货邮公式落到「支线」，
        /// 而支线在本作里是非协调机场 —— 于是 <see cref="LimitedCoordWindows"/> 里那条
        /// 「ZUH 07:00–23:00」成了**死配置**：`CoordWindowFor` 一开头就因 `tier > 1` 返回空。
        /// 1297 万人次已经不适宜当支线看，故按用户裁决提为干线，让限时协调窗口真正生效。
        /// </summary>
        static readonly string[] PromotedAirports = { "ZUH" };
        const int PromotedTier = 1;

        static int TierWithPromotion(string code, int derived)
        {
            for (int i = 0; i < PromotedAirports.Length; i++)
                if (string.Equals(PromotedAirports[i], code, StringComparison.OrdinalIgnoreCase))
                    return PromotedTier;
            return derived;
        }

        static string CoordWindowFor(string code, int tier)
        {
            if (tier > 1) return "";                        // 支线：非协调
            for (int i = 0; i < LimitedCoordAirports.Length; i++)
                if (string.Equals(LimitedCoordAirports[i], code, StringComparison.OrdinalIgnoreCase))
                    return LimitedCoordWindows[i];
            return CoordAllDay;                             // 默认 24 小时全时段协调
        }

        /// <summary>协调时段原文（`"24h"` / `"07:00-23:00"` / 空 = 非协调）。</summary>
        public static string CoordWindowOf(string code)
        {
            return FindByCode(code).coordWindow ?? "";
        }

        /// <summary>协调时段的可读说明，供界面与邮件使用。</summary>
        public static string CoordLabel(string code)
        {
            string w = CoordWindowOf(code);
            if (string.IsNullOrEmpty(w)) return "非协调 · 不设时刻槽";
            if (w == CoordAllDay) return "协调 24h";
            return $"协调 {w}";
        }

        /// <summary>某时刻是否落在协调时段内（正确处理跨午夜窗口）。</summary>
        public static bool InCoordWindow(string code, int minutes)
        {
            string w = CoordWindowOf(code);
            if (string.IsNullOrEmpty(w)) return false;
            if (w == CoordAllDay) return true;

            int dash = w.IndexOf('-');
            if (dash <= 0) return true;                     // 写坏了按全天处理，不静默变非协调
            if (!TryParseHm(w.Substring(0, dash).Trim(), out int s)) return true;
            if (!TryParseHm(w.Substring(dash + 1).Trim(), out int e)) return true;

            int m = ((minutes % 1440) + 1440) % 1440;
            if (s == e) return true;                        // 零长窗口视为全天
            return s < e ? (m >= s && m < e) : (m >= s || m < e);
        }

        /// <summary>
        /// 某时段的池容量 = 该时段**整点区间**内各刻的 15 分钟容量之和。
        ///
        /// ★2026-11-06（用户裁决「只要能申请的时刻都要显示出来」）：覆盖面由需求波窗口
        /// 改为 <see cref="AirlineCEO.Logic.ScheduleSlots.BandGrid"/>（按 BandOfDep 归段的全天网格）。
        /// 旧口径只数窗口内的刻，于是 06:00 / 09:45 / 10:00–11:15 / 14:15–16:15 这些
        /// 机场确实公布、日容量也计入的刻，既不显示也取不到 —— 枢纽 56 条日容量只有 46 条可达。
        /// 改后四段之和 == <see cref="DayCapacity"/>（枢纽 15/16/17/8、干线 9/10/10/4）。
        /// </summary>
        static int BandCapacity(string code, string coordWindow, int tier, int band)
        {
            if (string.IsNullOrEmpty(coordWindow)) return 0;
            int total = 0;
            var grid = AirlineCEO.Logic.ScheduleSlots.BandGrid(band);
            for (int i = 0; i < grid.Count; i++)
            {
                total += QuarterCapacityRaw(code, tier, coordWindow, grid[i]);
            }
            return total;
        }

        /// <summary>不依赖 <c>AirportInfo</c> 的容量算法本体（静态构造期只能调它）。</summary>
        static int QuarterCapacityRaw(string code, int tier, string coordWindow, int minutes)
        {
            if (string.IsNullOrEmpty(coordWindow)) return 0;
            int m = ((minutes % 1440) + 1440) % 1440;
            if (coordWindow != CoordAllDay)
            {
                int dash = coordWindow.IndexOf('-');
                bool inWindow = true;
                if (dash > 0
                    && TryParseHm(coordWindow.Substring(0, dash).Trim(), out int s)
                    && TryParseHm(coordWindow.Substring(dash + 1).Trim(), out int e)
                    && s != e)
                {
                    inWindow = s < e ? (m >= s && m < e) : (m >= s || m < e);
                }
                if (!inWindow) return 0;
            }

            int r = HourRateOf(code, tier, m / 60);
            if (r <= 0) return 0;
            QuarterSplit(r, out int q0, out int q1, out int q2, out int q3);
            int q = (m % 60) / AirlineCEO.Logic.ScheduleSlots.SlotGridStepMinutes;
            return q switch
            {
                0 => q0,
                1 => q1,
                2 => q2,
                _ => q3
            };
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

        /// <summary>
        /// 列表匹配名次（越小越靠前），-1 = 不匹配。
        ///
        /// 英文**只按词首**匹配，不做任意子串匹配 —— 这是 2026-10-21 的实测缺陷：
        /// 搜 "NGB" 时，`nameEn.IndexOf` 会在 "Chongqing Jia**ngb**ei" 里命中，
        /// 于是「宁波」下面顶着一条「重庆江北」。中文没有音节边界问题，仍允许包含匹配。
        /// </summary>
        public static int MatchRank(in AirportInfo a, string query)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(a.code)) return -1;
            string q = query.Trim();

            // ① 代码精确 → 中文名精确 → 英文名精确（"ZUH" / "珠海金湾" / "Zhuhai Jinwan"）
            if (a.code.Equals(q, StringComparison.OrdinalIgnoreCase)) return 0;
            if (!string.IsNullOrEmpty(a.nameZh) && a.nameZh.Equals(q, StringComparison.OrdinalIgnoreCase)) return 1;
            if (!string.IsNullOrEmpty(a.nameEn) && a.nameEn.Equals(q, StringComparison.OrdinalIgnoreCase)) return 1;
            if (a.Display.Equals(q, StringComparison.OrdinalIgnoreCase)) return 1;

            // ② 代码前缀（"ZU" → ZUH；"N" → NGB/NKG…）
            if (a.code.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 2;

            // ③ 中文名前缀 / 英文**词首**前缀（"珠海" → 珠海金湾；"Zhuhai" / "Jin" → 珠海金湾）
            if (!string.IsNullOrEmpty(a.nameZh) && a.nameZh.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 3;
            if (WordPrefix(a.nameEn, q)) return 3;

            // ④ 中文名包含（"金湾" → 珠海金湾）。英文不做包含匹配，理由见方法注释。
            if (!string.IsNullOrEmpty(a.nameZh) && a.nameZh.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return 4;

            // ⑤ 代码包含（只做尾码容错，如 "GB" → NGB）。这一档不会把 CKG 拉进来：
            //    CKG 的三字码里没有 "NGB"，事故来自英文名的音节巧合，与代码无关。
            if (a.code.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return 5;

            return -1;
        }

        /// <summary>是否有某个英文单词以 <paramref name="query"/> 开头（按空格/连字符切词）。</summary>
        static bool WordPrefix(string nameEn, string query)
        {
            if (string.IsNullOrEmpty(nameEn) || string.IsNullOrEmpty(query)) return false;
            int i = 0, n = nameEn.Length;
            while (i < n)
            {
                while (i < n && (nameEn[i] == ' ' || nameEn[i] == '-' || nameEn[i] == '/')) i++;
                int start = i;
                while (i < n && nameEn[i] != ' ' && nameEn[i] != '-' && nameEn[i] != '/') i++;
                int len = i - start;
                if (len > 0 && len >= query.Length
                    && string.Compare(nameEn, start, query, 0, query.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 下拉/搜索用：按 <see cref="MatchRank"/> 过滤并按名次、代码排序。空查询返回空列表
        /// （不返回整表 —— 98 行一次铺开会顶满屏幕，且与「边打边筛」的交互逻辑矛盾）。
        /// </summary>
        public static List<AirportInfo> Filter(string query, int limit = 40)
        {
            var list = new List<AirportInfo>();
            if (string.IsNullOrWhiteSpace(query)) return list;

            var ranked = new List<KeyValuePair<int, AirportInfo>>();
            for (int i = 0; i < All.Length; i++)
            {
                int r = MatchRank(All[i], query);
                if (r >= 0) ranked.Add(new KeyValuePair<int, AirportInfo>(r, All[i]));
            }
            ranked.Sort((x, y) =>
            {
                int c = x.Key.CompareTo(y.Key);
                if (c != 0) return c;
                return string.CompareOrdinal(x.Value.code, y.Value.code);
            });
            for (int i = 0; i < ranked.Count && list.Count < limit; i++) list.Add(ranked[i].Value);
            return list;
        }

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

            // 模糊：按共享匹配器（代码前缀 / 中文包含 / 英文词首）取**名次最好**的一条。
            // 旧实现在 All 里取第一条命中即返回，配合英文的任意子串匹配，会把
            // 「NGB」解析成 Chongqing Jia(ngb)ei —— 同一个查询在输入框和下拉列表里给出不同机场。
            var candidates = Filter(q, limit: 1);
            if (candidates.Count > 0)
            {
                airport = candidates[0];
                return true;
            }

            // 兼容「北京」「上海」等简称
            string qu = q.ToUpperInvariant();
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

        /// <summary>
        /// 往返城对：`北京首都 PEK ⇄ 上海浦东 PVG`。
        ///
        /// 计划类页面（周总计划 / 单班页 / 航线卡片）列的是**一条航线的两个方向**：
        /// 一班 = 去程 + 过站 + 回程，用单向箭头 `→` 会被读成「只飞这一头」，
        /// 与同一行的「回程时刻」「去程/回程」两列自相矛盾。故这些页面一律用 ⇄，
        /// 单向 `→` 只留给真正单程的语境（如航线提案、地图航迹）。
        /// </summary>
        public static string OdRoundTrip(string origin, string dest, string via = null)
        {
            if (string.IsNullOrEmpty(via))
                return Label(origin) + " ⇄ " + Label(dest);
            return Label(origin) + " ⇄ " + Label(via) + " ⇄ " + Label(dest);
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
