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

        /// <summary>
        /// 代码 → <see cref="All"/> 下标。★2026-11-09 批次一（M1）新增。
        ///
        /// 此前 `FindByCode` 是 104 次逐个 <c>string.Equals(OrdinalIgnoreCase)</c> 的线性扫描，
        /// 而它在「机场时刻」页一次刷新里会被调上千次（<see cref="Label"/> / <see cref="SlotCapacity"/>
        /// 都经它转发）。改成字典后是 O(1)，**对外语义一字未变**：
        /// 大小写不敏感、找不到仍返回 `default(AirportInfo)`、`All` 数组与存档写出内容都没动。
        ///
        /// 比较器必须仍是 <see cref="StringComparer.OrdinalIgnoreCase"/> —— 用不变文化比较器
        /// 会在土耳其语 "I/ı" 这类环境下把 ICN 判错，属于典型的「换机器才炸」。
        /// </summary>
        static readonly Dictionary<string, int> _indexByCode =
            new Dictionary<string, int>(All.Length, StringComparer.OrdinalIgnoreCase);

        // —— P1：等级 / 协调 / 槽池容量 / 宵禁 全部由底表推导，不手工录入 ——
        static AirportCatalog()
        {
            // ★2026-11-09 批次一（M1）：先建代码索引再逐个推导。
            // 顺序有讲究 —— 索引只依赖 `code`，而 `code` 在字段初始化器里就已全部写入；
            // 等级 / 协调时段则要等循环逐行推导，所以索引可以早建、属性必须晚用。
            for (int i = 0; i < All.Length; i++) _indexByCode[All[i].code] = i;

            for (int i = 0; i < All.Length; i++)
            {
                var a = All[i];
                var eco = CityEconomyCatalog.Find(a.code);
                float pax = eco != null ? eco.paxMillion : 0f;
                float cargo = eco != null ? eco.cargoWanTon : 0f;

                a.tier = TierWithPromotion(a.code, TierOf(pax, cargo));
                a.coordinated = a.tier <= 1;
                a.coordWindow = CoordWindowFor(a.code, a.tier);
                // 池容量是**派生缓存**，唯一算法在 CapacityService（C0：跑道数平铺）。
                a.slotCapacity = CapacityService.Pools(a.code, a.tier, a.coordWindow);
                a.curfew = CurfewWindowOf(a.code);
                All[i] = a;
            }
        }

        // ————————————————— 容量：每条跑道每刻 1 条槽（★2026-11-09 用户裁决）—————————————————
        //
        // 用户 2026-11-09：「为什么时刻槽要设置峰刻容量？机场有多少条跑道就释放多少条时刻容量，
        // 不要设置峰刻。」—— 此前那套「逐场现实小时曲线 × 1/3.5 → 拆到四个 15 分钟刻」整段作废。
        // 旧口径让同一机场的容量随钟点起伏（大机场早高峰 4–5 条、凌晨 1 条），玩家看到的是
        // 「跑道一条没变，容量却在变」，与「跑道决定容量」的直觉正面冲突。
        //
        // 新口径只有一条：
        //     **协调时段内、且不在宵禁中的每一个 15 分钟刻 = 跑道数 × 每跑道槽数 条槽。**
        //     「每跑道槽数」这一旋钮 2026-11-09 批次一已随算法迁到
        //     <see cref="CapacityService.SlotsPerRunwayPerQuarter"/>，此处不再保留第二份。
        //
        // 折损在哪：一条跑道现实小时容量约 30–40 架次（起降合计），折成单向约 15–20，
        // 即每刻 4–5 条。本作取 1，等于对跑道吞吐做约 4 倍压缩 —— 与旧版 1/3.5 同量级，
        // 但**不再随机场、随时段漂移**。
        //
        // 保留的两层限制（都不是「峰刻」）：
        //   ① 协调时段 —— 辅协调机场只在窗口内需要槽（如珠海 07:00–23:00）。
        //   ② 宵禁 —— 物理禁飞，容量记 0（PEK / EEA / NRT 等），界面仍显示「不开放」。
        //
        // ★「跑道数」有两种读法，别混：
        //   · **物理跑道**（AirportRunways.Counts）= 建成投运了几条，报表与它对账；
        //   · **等效跑道**（见下方 Overrides）= 每刻实际放几条槽，容量只看这个。
        //   默认两者相等；申报容量超过跑道数的机场（用户模型：LHR / DXB 靠混合起降）
        //   在 Overrides 里记等效值 + 一句理由，其余机场不开口子。
        //
        // 后果（2026-11-09 实测，等效表为空时）：81 场跑道合计 248 条 → 单池日容量 22,260
        // （旧曲线口径 12,126）。多跑道枢纽变大（ORD 8 → 768/日、CAN 5 → 480/日），
        // 双跑道的繁忙机场变小（DXB / BKK / SIN → 192/日）。详细对照见
        // docs/handoff/2026-11-09_时刻容量改为跑道数.md。

        // ★2026-11-09 批次一：每跑道槽数常量已迁到 CapacityService.SlotsPerRunwayPerQuarter。
        // 此处不再保留第二份 —— 「唯一来源」不是口号：留一个同名副本，
        // 日后两处不一致就会让表格与玩法分叉。

        /// <summary>
        /// 该机场**物理跑道数**（建成并投入运行的条数；含交叉跑道）。
        /// 表内没有的机场按等级回落，保证「有协调属性就一定有槽」。
        /// </summary>
        static int PhysicalRunwayCountOf(string code, int tier)
        {
            if (!string.IsNullOrEmpty(code) && AirportRunways.Counts.TryGetValue(code, out int n)) return n;
            return tier <= 0 ? 3 : tier == 1 ? 2 : 1;
        }

        /// <summary>
        /// **等效跑道**：申报容量高于物理跑道数的机场。
        ///
        /// ★2026-11-09 用户提出的模型（确认采用）：
        /// 「原本所有机场的跑道数 = 时刻容量，但 LHR / DXB 通过**混合起降**
        ///   （一条起飞、一条落地，还能按需反转）把容量做到了高于跑道数。」
        ///
        /// 这正好是现实里航班时刻协调的算法：容量是机场自己**申报**的每 15 分钟可提供
        /// 的架次，不是由跑道条数算出来的。跑道条数只是**默认基线**；某些场靠运行方式
        /// 把同一个跑道组合的吞吐抬上去，申报值就会超过「一条跑道一条槽」。
        ///
        /// 三条硬约束（回归会逐条验，见 AirportCatalogRegression ⑨）：
        ///   ① <c>reason</c> 必须非空 —— 每个例外都要能对玩家讲清「为什么它比跑道多」；
        ///   ② 等效值必须 **≥** 物理跑道数 —— 想比跑道少，去改跑道表，不许在这里做减法；
        ///   ③ 等效值 **≤** 物理跑道数 × 2 —— 超过两倍就不是「混合运行」，是数据填错了。
        ///
        /// 当前为空表：用户尚未裁决具体数值，故行为与「纯跑道数」完全一致。
        /// 填表示例（待裁决，不要擅自填）：
        ///     { "LHR", new EffectiveRunways { value = 4, reason = "两条跑道混合起降（一条起一条降可反转）" } },
        ///     { "DXB", new EffectiveRunways { value = 4, reason = "两条宽间距平行跑道混合运行" } },
        /// </summary>
        struct EffectiveRunways
        {
            public int value;
            public string reason;
        }

        static readonly Dictionary<string, EffectiveRunways> Overrides =
            new Dictionary<string, EffectiveRunways>(StringComparer.OrdinalIgnoreCase)
            {
            };

        /// <summary>
        /// 该机场**每刻放几条槽用的跑道数**：有申报超额就用等效值，否则等于物理跑道数。
        /// 界面显示「跑道」、报表算容量，都只看这一个数。
        /// </summary>
        public static int RunwayCount(string code)
            => RunwayCountOf(code, TierOfCode(code));

        static int RunwayCountOf(string code, int tier)
        {
            if (!string.IsNullOrEmpty(code) && Overrides.TryGetValue(code, out var ov)) return ov.value;
            return PhysicalRunwayCountOf(code, tier);
        }

        /// <summary>
        /// 供 <see cref="CapacityService"/> 使用的跑道数入口（显式给 tier，静态构造期可调）。
        /// ★2026-11-09 批次一新增：容量算法迁出后仍只认这一处跑道口径，避免两套读数。
        /// </summary>
        public static int RunwayCountFor(string code, int tier) => RunwayCountOf(code, tier);

        /// <summary>该机场**物理**跑道数（报表用来与 runways.tsv 对账，不参与容量）。</summary>
        public static int PhysicalRunwayCount(string code)
            => PhysicalRunwayCountOf(code, TierOfCode(code));

        /// <summary>该机场为什么比物理跑道多放槽；空串 = 没有超额（走跑道数基线）。</summary>
        public static string EffectiveRunwayReason(string code)
            => !string.IsNullOrEmpty(code) && Overrides.TryGetValue(code, out var ov) ? (ov.reason ?? "") : "";

        /// <summary>该机场的容量是否来自「申报超额」（界面据此显示「混合运行」标记）。</summary>
        public static bool HasEffectiveRunwayOverride(string code)
            => !string.IsNullOrEmpty(code) && Overrides.ContainsKey(code);

        /// <summary>所有「申报超额」的机场代码（回归逐条校验理由与数值区间，报表逐条加注）。</summary>
        public static string[] EffectiveRunwayOverrideCodes()
        {
            var keys = new string[Overrides.Count];
            Overrides.Keys.CopyTo(keys, 0);
            return keys;
        }

        /// <summary>
        /// 表内是否有该机场的**显式**跑道数（true = 用的是实测值，false = 落回等级默认值）。
        /// 回归用它在「某个协调机场悄悄掉回默认值」时报警。
        /// </summary>
        public static bool HasRunwayData(string code)
            => !string.IsNullOrEmpty(code) && AirportRunways.Counts.ContainsKey(code);

        /// <summary>
        /// 判某分钟是否落在 `"23:00-06:00"` 这类窗口内（正确处理跨午夜）。
        ///
        /// 窗口为空返回 false；窗口写坏时返回 <paramref name="fallback"/> ——
        /// 协调时段「写坏按全天」、宵禁「写坏按不禁」，两边都不让一条脏数据
        /// 让整个机场静默失去槽。
        /// </summary>
        public static bool InWindowText(string window, int minutes, bool fallback)
        {
            if (string.IsNullOrEmpty(window)) return false;
            int m = ((minutes % 1440) + 1440) % 1440;
            int dash = window.IndexOf('-');
            if (dash <= 0) return fallback;
            if (!TryParseHm(window.Substring(0, dash).Trim(), out int s)) return fallback;
            if (!TryParseHm(window.Substring(dash + 1).Trim(), out int e)) return fallback;
            if (s == e) return false;
            return s < e ? (m >= s && m < e) : (m >= s || m < e);
        }

        /// <summary>协调时段判定（24h 常量单独走快路径）。</summary>
        public static bool InCoordWindowText(string window, int m)
        {
            if (string.IsNullOrEmpty(window)) return false;
            if (window == CoordAllDay) return true;
            return InWindowText(window, m, true);
        }

        /// <summary>宵禁判定：命中即物理禁飞，容量 0。</summary>
        public static bool InCurfewWindow(string code, int m)
            => InWindowText(CurfewWindowOf(code), m, false);

        /// <summary>
        /// 某时刻所在的 **15 分钟窗口容量**（条数）。非协调时段 / 宵禁返回 0。
        /// 这是「该刻最多放几条槽」的唯一判据。
        ///
        /// ★2026-11-09：容量 = **该机场跑道数**（见 <see cref="RunwayCount"/>），
        /// 全天同一数值，不再有峰谷曲线。
        /// ★2026-11-09 批次一：算法本体迁到 <see cref="CapacityService.Quarter(string,int)"/>。
        /// </summary>
        public static int QuarterCapacity(string code, int minutes)
            => CapacityService.Quarter(code, minutes);

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
        //   ② 该小时容量 0 —— 机场该小时不分配时刻（★2026-11-09 起只有宵禁会触发），这是真信息；
        //   ③ 小时容量不足以铺满四个刻时未落选的刻 —— 旧曲线口径的派生副产品
        //      （枢纽 7/51、干线 23/51 都是它）。★2026-11-09 容量改为「跑道数平铺」后，
        //      每刻都等于跑道数，**这一类已不可能出现**；保留常量只为兼容旧存档与界面分支。
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

        /// <summary>
        /// 宵禁窗口：EEA 全境、PEK、以及 NRT / HND / SYD / TPE 真实宵禁，其余为空。
        ///
        /// ★2026-11-09：宵禁**直接压容量**（见 <see cref="CapacityService.Quarter(string,int)"/>，命中即 0），
        /// 界面因此把这些刻显示成「不开放」，与物理禁飞一致。
        /// </summary>
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
            => CapacityService.Day(code);

        /// <summary>某机场某小时的容量（支线 = 0）。</summary>
        /// <summary>
        /// 某机场某小时的容量 = 该小时四个 15 分钟刻之和。
        /// ★2026-11-09 起由 <see cref="QuarterCapacity"/> 反推，两者永不互相打脸。
        /// ★2026-11-09 批次一：算法迁到 <see cref="CapacityService.Hour"/>。
        /// </summary>
        public static int HourCapacity(string code, int hour)
            => CapacityService.Hour(code, hour);

        /// <summary>是否协调机场（需时刻槽）。</summary>
        public static bool IsCoordinated(string code)
        {
            var a = FindByCode(code);
            return !string.IsNullOrEmpty(a.code) && !string.IsNullOrEmpty(a.coordWindow);
        }

        /// <summary>
        /// 该机场早/中/晚/夜四时段的可分配时刻数（下标 0–3）。
        /// ★2026-11-09 起 = 跑道数 × 该段刻数（宵禁刻与协调时段外的刻记 0）。
        /// </summary>
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

        /// <summary>
        /// 按 IATA 代码查机场（大小写不敏感）。找不到返回 `default(AirportInfo)`。
        ///
        /// ★2026-11-09 批次一（M1）：由线性扫描改为字典查表，O(N) → O(1)。
        /// 返回值、比较语义、未命中行为全部不变 —— `FindByCode("")` 仍返回默认值，
        /// `FindByCode("pek")` 仍命中 PEK。
        /// </summary>
        public static AirportInfo FindByCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return default;
            return _indexByCode.TryGetValue(code, out int i) ? All[i] : default;
        }

        /// <summary>
        /// 目录内的机场总数（★2026-11-09 批次一新增，供回归与索引自检使用）。
        /// </summary>
        public static int Count => All.Length;

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
