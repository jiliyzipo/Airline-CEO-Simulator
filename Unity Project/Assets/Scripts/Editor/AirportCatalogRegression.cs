#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 机场底表「三表贯通」回归（2026-10-21，配合批次 A 入表）。
    ///
    /// 为什么单独一条：`CityEconomyCatalog` 不只喂需求系数，它还**自动生成接管航司**
    /// （<see cref="PresetAirlines.AllSelectable"/> 按城市经济目录逐条建 `hub_&lt;IATA&gt;`）。
    /// 只要有人往经济表里加一行而忘了机场目录或坐标表，就会出现：
    /// · 选司列表里多一家「某某通航」，点进去机场名解析不出来；
    /// · 地图上少一个点 / 航距回退到 <see cref="AirportGeoCatalog.UnknownKm"/>，
    ///   于是在开线预览里「虹桥—某地」的分数与真实航程无关 —— 而且**不报错**。
    /// 这类缺陷不会自己冒出来，所以此处把「三条表必须同集合、同坐标可用」钉死。
    ///
    /// 另钉一条反例（<c>佛山沙堤</c>）：它是「目录外机场仍要被认出来」这条断言的生存前提。
    /// 一旦 FUO 被收进目录，<see cref="CeoCommandRegression"/> 里那条反例会静默变成正例。
    /// </summary>
    public static class AirportCatalogRegression
    {
        /// <summary>
        /// 批次 A/B 收入的国内机场（pax / cargo 取 CAAC《2024 年全国民用运输机场生产统计公报》实际值）。
        /// **tier 不手填**：底表由 `AirportCatalog` 静态构造按 pax/cargo 推导，这里只断言推导结果，
        /// 免得日后改了客流却没人发现等级 / 协调属性跟着变了。
        /// </summary>
        struct NewAirport
        {
            public string code;
            public string nameZh;
            public float pax;      // 百万人次
            public float cargo;    // 万吨
            public int tier;       // 期望推导值：0 枢纽 / 1 干线 / 2 支线
            public float distanceFromPekMinKm;
            public float distanceFromPekMaxKm;
        }

        static readonly NewAirport[] BatchA =
        {
            // ★2026-11-05：ZUH 期望等级由 2 改 **1**（用户裁决）。客流 1297 万按客货公式推导仍是支线，
            // 而支线非协调 —— 于是「珠海 07:00–23:00 限时协调」那条例外永远不生效（死配置）。
            // 本作对 ZUH 走 `AirportCatalog.PromotedAirports` 人工提级，故此处期望值随之改写；
            // 其余三座仍是纯推导得到的支线。
            new NewAirport { code = "ZUH", nameZh = "珠海金湾", pax = 12.97f, cargo = 3.43f, tier = 1, distanceFromPekMinKm = 1900f, distanceFromPekMaxKm = 2200f },
            new NewAirport { code = "WUX", nameZh = "无锡硕放", pax = 10.50f, cargo = 17.35f, tier = 2, distanceFromPekMinKm = 900f, distanceFromPekMaxKm = 1200f },
            new NewAirport { code = "NGB", nameZh = "宁波栎社", pax = 14.73f, cargo = 16.86f, tier = 2, distanceFromPekMinKm = 1100f, distanceFromPekMaxKm = 1400f },
            new NewAirport { code = "WNZ", nameZh = "温州龙湾", pax = 12.53f, cargo = 12.93f, tier = 2, distanceFromPekMinKm = 1250f, distanceFromPekMaxKm = 1600f },
        };

        /// <summary>
        /// 批次 B（2026-10-22 裁定：5 座）。
        /// 选址口径两条：① 填补地理空白；② 到任何既有机场都 ≥ 245 km，不给 K16（超短航段经济模型）
        /// 再加新的假短程线。原候选里的拉萨 LXA 实测会把孟买 / 伊斯坦布尔吸成目的地并顶掉
        /// BOM→SIN、IST→BOS，已撤出（见任务书 K06）。
        /// KWE / TNA 客流已过 2000 万，按 <see cref="AirportCatalog.TierOf"/> 推导为**干线 / 协调机场**；
        /// 批次 A 里除 ZUH（2026-11-05 人工提为干线，为让「珠海 07:00–23:00」限时协调真正生效）外全是支线，
        /// 所以这两座是第一次让「枢纽航司的基地本身是协调机场」，
        /// 时刻槽走祖父条款（见 ⑦ 的基站协调断言与手测项）。
        /// </summary>
        static readonly NewAirport[] BatchB =
        {
            new NewAirport { code = "NNG", nameZh = "南宁吴圩", pax = 14.01f, cargo = 20.32f, tier = 2, distanceFromPekMinKm = 2000f, distanceFromPekMaxKm = 2200f },
            new NewAirport { code = "KWE", nameZh = "贵阳龙洞堡", pax = 22.31f, cargo = 10.66f, tier = 1, distanceFromPekMinKm = 1700f, distanceFromPekMaxKm = 1850f },
            new NewAirport { code = "TYN", nameZh = "太原武宿", pax = 14.78f, cargo = 6.20f, tier = 2, distanceFromPekMinKm = 400f, distanceFromPekMaxKm = 550f },
            new NewAirport { code = "HET", nameZh = "呼和浩特白塔", pax = 13.17f, cargo = 5.92f, tier = 2, distanceFromPekMinKm = 380f, distanceFromPekMaxKm = 500f },
            new NewAirport { code = "TNA", nameZh = "济南遥墙", pax = 20.01f, cargo = 15.36f, tier = 1, distanceFromPekMinKm = 300f, distanceFromPekMaxKm = 450f },
        };

        /// <summary>
        /// 批次 B'（K18 回补）：拉萨贡嘎。
        /// 它原先被批次 B 剔除，理由不是数据错，而是目的地池没有「门户门槛」—— 一入池就被
        /// 孟买 / 伊斯坦布尔选成目的地（BOM→LXA、IST→LXA），顶掉 BOM→SIN、IST→BOS。
        /// K18 在 <see cref="RouteGeography"/> 里补了「跨境航线两端都必须是枢纽/干线」之后，
        /// 支线机场不再可能出现在国际线端点，LXA 才回补。
        ///
        /// pax/cargo 按公报量级（550 万 / 4.4 万吨）录入：离 2000 万 / 50 万吨门槛都差一个量级，
        /// 取值在 5–6 百万之间不影响推导等级，故这里断言的是**等级**而不是精确吞吐量。
        ///
        /// **顺序有讲究**：本字段必须声明在 <see cref="AllNew"/> 之前 —— C# 静态字段按文本顺序
        /// 初始化，倒过来会让 AllNew 拿到一个 null 数组（编译通过、运行时 NullReference）。
        /// </summary>
        static readonly NewAirport[] BatchBPrime =
        {
            new NewAirport { code = "LXA", nameZh = "拉萨贡嘎", pax = 5.50f, cargo = 4.40f, tier = 2, distanceFromPekMinKm = 2400f, distanceFromPekMaxKm = 2700f },
        };

        static readonly NewAirport[] AllNew = Join(BatchA, BatchB, BatchBPrime);

        static NewAirport[] Join(params NewAirport[][] sets)
        {
            int n = 0;
            for (int i = 0; i < sets.Length; i++) n += sets[i].Length;
            var r = new NewAirport[n];
            int k = 0;
            for (int i = 0; i < sets.Length; i++)
                for (int j = 0; j < sets[i].Length; j++) r[k++] = sets[i][j];
            return r;
        }

        /// <summary>刻意留在目录外的机场（佛山沙堤 FUO）：反例断言的锚点，不要收录。</summary>
        const string OffCatalogName = "佛山沙堤";
        const string OffCatalogCode = "FUO";

        /// <summary>
        /// 跑全部断言，返回是否全绿。
        /// 返回 bool 而不是只写日志：编辑器菜单 `AirlineCEO/Run Logic Self Tests` 要一条条跑下去，
        /// 不能中途抛异常中断；而 NUnit（<c>AirportCatalogEditModeTests</c>）需要能变红 ——
        /// 只 `Debug.Log` 的回归在 CI 里是**静默通过**，等于没有护栏。
        /// </summary>
        public static bool Run()
        {
            var airports = AirportCatalog.Airports;

            // ① 三条表同集合：机场目录 / 城市经济 / 经纬度，一一对应
            var apCodes = new HashSet<string>();
            bool apNoDup = true;
            for (int i = 0; i < airports.Count; i++)
                if (!apCodes.Add(airports[i].code)) apNoDup = false;

            var ecoCodes = new HashSet<string>();
            var ecoList = CityEconomyCatalog.All;
            bool ecoNoDup = true;
            for (int i = 0; i < ecoList.Length; i++)
            {
                if (ecoList[i] == null || string.IsNullOrEmpty(ecoList[i].iata)) continue;
                if (!ecoCodes.Add(ecoList[i].iata)) ecoNoDup = false;
            }

            var geoCodes = new HashSet<string>();
            var geoList = AirportGeoCatalog.All;
            for (int i = 0; i < geoList.Length; i++) geoCodes.Add(geoList[i].code);

            bool sameSet = airports.Count == ecoList.Length && airports.Count == geoList.Length
                           && apCodes.SetEquals(ecoCodes) && apCodes.SetEquals(geoCodes);

            string missingInEco = "", missingInGeo = "", missingInAp = "";
            foreach (var code in apCodes)
            {
                if (!ecoCodes.Contains(code)) missingInEco += code + " ";
                if (!geoCodes.Contains(code)) missingInGeo += code + " ";
            }
            foreach (var code in ecoCodes) if (!apCodes.Contains(code)) missingInAp += code + " ";

            // ② 坐标可用：每座机场都能查到大圆坐标，且落在合法经纬度范围
            int badCoord = 0;
            for (int i = 0; i < airports.Count; i++)
            {
                string code = airports[i].code;
                if (!AirportGeoCatalog.TryLonLat(code, out float lon, out float lat))
                {
                    badCoord++;
                    continue;
                }
                if (lon < -180f || lon > 180f || lat < -90f || lat > 90f) badCoord++;
                // 中国机场必须在东亚包围盒内（防止经纬写反 / 抄错正负号）
                else if (AirportRegions.IsChina(code) && (lon < 73f || lon > 136f || lat < 3f || lat > 54f)) badCoord++;
            }
            bool geoUsable = badCoord == 0;

            // ③ 批次 A / B 机场：等级与协调属性按 pax/cargo 推导、无宵禁、中英文与城市名都能解析
            var batch = new StringBuilder();
            bool batchAOk = true, batchBOk = true, batchBPrimeOk = true;
            int batchBEnd = BatchA.Length + BatchB.Length;
            for (int i = 0; i < AllNew.Length; i++)
            {
                var b = AllNew[i];
                var a = AirportCatalog.FindByCode(b.code);
                var eco = CityEconomyCatalog.Find(b.code);
                bool entryOk = !string.IsNullOrEmpty(a.code)
                               && a.nameZh == b.nameZh
                               && a.tier == b.tier
                               && a.coordinated == (b.tier <= 1)
                               && string.IsNullOrEmpty(a.curfew ?? "")
                               && a.slotCapacity != null && a.slotCapacity.Length == 4;
                bool ecoOk = eco != null
                             && Mathf.Abs(eco.paxMillion - b.pax) < 0.051f
                             && Mathf.Abs(eco.cargoWanTon - b.cargo) < 0.051f
                             && !string.IsNullOrEmpty(eco.cityZh);

                bool resolveCode = AirportCatalog.TryResolve(b.code, out var byCode) && byCode.code == b.code;
                bool resolveName = AirportCatalog.TryResolve(b.nameZh, out var byName) && byName.code == b.code;
                string city = eco != null ? eco.cityZh : "";
                bool resolveCity = !string.IsNullOrEmpty(city)
                                   && AirportCatalog.TryResolve(city, out var byCity) && byCity.code == b.code;

                float km = AirportGeoCatalog.GreatCircleKm("PEK", b.code);
                bool distOk = km >= b.distanceFromPekMinKm && km <= b.distanceFromPekMaxKm;

                bool ok = entryOk && ecoOk && resolveCode && resolveName && resolveCity && distOk;
                // 分批累计：两批共用同一个 bool 时，A 全绿而 B 有一座红，日志会把两批都打成 False，
                // 证据行反而看不出是哪一批坏了（失败明细 `batch` 里有代码，但汇总行会误导）。
                if (i < BatchA.Length) batchAOk &= ok;
                else if (i < batchBEnd) batchBOk &= ok;
                else batchBPrimeOk &= ok;
                if (!ok)
                {
                    batch.Append($"{b.code}:entry={entryOk} eco={ecoOk} 码={resolveCode} 名={resolveName}"
                                 + $" 市={resolveCity}({city}) 距={km:0}km; ");
                }
            }

            // ④ 反例必须仍是反例：佛山沙堤 不在目录，且三表都没有 FUO
            bool offCatalog = !AirportCatalog.TryResolve(OffCatalogName, out _)
                              && string.IsNullOrEmpty(AirportCatalog.FindByCode(OffCatalogCode).code)
                              && CityEconomyCatalog.Find(OffCatalogCode) == null
                              && !geoCodes.Contains(OffCatalogCode);

            // ⑤ 接管航司随经济目录自动长出：新机场必须有 hub 航司，且 IATA/ICAO 不得撞号
            //    （选司界面上两家同名代码的航司，玩家会以为是同一个 bug）
            var selectable = PresetAirlines.AllSelectable();
            var usedIata = new HashSet<string>();
            var usedIcao = new HashSet<string>();
            bool codeUnique = true;
            for (int i = 0; i < selectable.Count; i++)
            {
                var p = selectable[i];
                if (string.IsNullOrEmpty(p.iataCode) || !usedIata.Add(p.iataCode)) codeUnique = false;
                if (string.IsNullOrEmpty(p.icaoCode) || !usedIcao.Add(p.icaoCode)) codeUnique = false;
            }
            bool takeoverCount = selectable.Count == ecoList.Length;

            bool hubExists = true;
            var hubMissing = new StringBuilder();
            for (int i = 0; i < AllNew.Length; i++)
            {
                string hubId = PresetAirlines.HubPrefix + AllNew[i].code;
                bool hit = false;
                for (int k = 0; k < selectable.Count && !hit; k++)
                    if (selectable[k].id == hubId && selectable[k].hubAirport == AllNew[i].code) hit = true;
                if (!hit) { hubExists = false; hubMissing.Append(hubId + " "); }
            }

            // ⑥ 搜索/下拉匹配：英文只按词首，不做任意子串。
            //    实测缺陷（2026-10-21 截图）：输入 NGB，列表里除了「宁波栎社 NGB」还挂着
            //    「重庆江北 CKG」—— 因为 "Chongqing Jia(ngb)ei" 里恰好连着一个 "ngb"。
            //    这里把三条口径都钉住：码命中、中文包含命中、英文词首命中；音节内部的巧合必须落选。
            var fNgb = AirportCatalog.Filter("NGB");
            bool ngbOnly = fNgb.Count > 0 && fNgb[0].code == "NGB";
            bool noCkg = true;
            for (int i = 0; i < fNgb.Count; i++) if (fNgb[i].code == "CKG") noCkg = false;

            var fJiangbei = AirportCatalog.Filter("Jiangbei");      // 词首 → 重庆江北
            bool wordPrefixOk = fJiangbei.Count > 0 && fJiangbei[0].code == "CKG";

            var fCityZh = AirportCatalog.Filter("宁波");             // 中文前缀 → 宁波栎社
            bool zhPrefixOk = fCityZh.Count > 0 && fCityZh[0].code == "NGB";

            var fZhuhai = AirportCatalog.Filter("珠海");             // 批次 A 新城市按中文可查
            bool newCityOk = fZhuhai.Count > 0 && fZhuhai[0].code == "ZUH";

            // 代码前缀仍要能查（"ZU" → ZUH），但空查询不得返回整表
            var fPrefix = AirportCatalog.Filter("ZU");
            bool codePrefixOk = fPrefix.Count > 0 && fPrefix[0].code == "ZUH"
                                && AirportCatalog.Filter("").Count == 0
                                && AirportCatalog.Filter("   ").Count == 0;

            // 输入框与下拉必须是同一套判定：TryResolve("NGB") 只可能得到宁波
            bool resolveSame = AirportCatalog.TryResolve("NGB", out var rNgb) && rNgb.code == "NGB";

            bool searchOk = ngbOnly && noCkg && wordPrefixOk && zhPrefixOk && newCityOk && codePrefixOk && resolveSame;

            // ⑦ 批次 B 的选址口径要能被回归守住：新机场到**任何**既有/新增机场都不得小于 245 km。
            //    为什么钉这条：`MinPassengerKm` 只拦 100 km 以内的「同城距离」，而中国境内
            //    100–250 km 是高铁的绝对主场 —— 批次 A 因此留下 3 条（ZUH–CAN 154、WUX–PVG 136、
            //    NGB–PVG 150）。**K16 已于 2026-10-23 裁定并落地**：`RouteGeography.MinChinaDomesticKm`
            //    = 250，境内两端 100–250 km 一律不生成（判据与正反两面见 `ShortHaulRegression`）。
            //    本段保留为**选址**护栏：批次 B/B' 的名单是按「不加重这个坑」选的，日后若有人
            //    往这批里塞惠州/泉州/烟台之类，这里先红 —— 与 K16 的航线判据是两道不同的闸。
            const float UltraShortGuardKm = 245f;
            bool noUltraShort = true;
            var ultra = new StringBuilder();
            var guarded = Join(BatchB, BatchBPrime);   // B' 同样是「不许加重超短坑」的批次
            for (int i = 0; i < guarded.Length; i++)
            {
                float min = float.MaxValue;
                string near = "";
                for (int k = 0; k < airports.Count; k++)
                {
                    string other = airports[k].code;
                    if (string.Equals(other, guarded[i].code)) continue;
                    float km = AirportGeoCatalog.GreatCircleKm(guarded[i].code, other);
                    if (km > 0f && km < min) { min = km; near = other; }
                }
                if (min < UltraShortGuardKm)
                {
                    noUltraShort = false;
                    ultra.Append($"{guarded[i].code}→{near} {min:0}km; ");
                }
            }

            // ⑧ 跨境门户门槛（K18）：支线机场不得出现在国际/地区航线的任一端，
            //    但「本国唯一门户」不能连坐 —— 第一版只按等级一刀切，实测掉了 43 条真实航线
            //    和 8 座城市的目的地（亚的斯亚贝巴 / 内罗毕 / 拉各斯 / 阿克拉 / 奥克兰 /
            //    赫尔辛基 / 布宜诺斯艾利斯 / 温哥华）。所以这里正反两面都钉：
            //    ① 拉萨（客流 550 万、中国有 36 座机场）必须被拦；
            //    ② 阿克拉（280 万但是加纳唯一机场）必须放行，否则规则就是过度收紧。
            bool gatewayRuleOk = !RouteGeography.IsGateway("LXA")            // 支线且非唯一：不够格
                                 && RouteGeography.IsGateway("ACC")          // 仅 280 万，但加纳唯一
                                 && RouteGeography.IsGateway("NBO")          // 仅 800 万，但肯尼亚唯一
                                 && RouteGeography.IsGateway("ADD")
                                 && RouteGeography.IsGateway("CTU")          // 干线：够格
                                 && RouteGeography.IsGateway("PEK")          // 枢纽：够格
                                 && RouteGeography.IsCrossBorder("CTU", "LXA") == false
                                 && RouteGeography.IsCrossBorder("CAN", "SIN");   // 对照：真跨境
            bool intlBlocked = !RouteGeography.IsPlausiblePassengerOd("BOM", "LXA", 2500f)
                               && !RouteGeography.IsPlausiblePassengerOd("LXA", "DEL", 2200f)
                               && !RouteGeography.IsPlausiblePassengerOd("IST", "LXA", 4800f);
            bool domesticKept = RouteGeography.IsPlausiblePassengerOd("CTU", "LXA", 1263f)
                                && RouteGeography.IsPlausiblePassengerOd("PEK", "LXA", 2560f);
            bool noOvershoot = RouteGeography.IsPlausiblePassengerOd("LHR", "ACC", 5100f)
                               && RouteGeography.IsPlausiblePassengerOd("SYD", "AKL", 2160f)
                               && RouteGeography.IsPlausiblePassengerOd("LOS", "LHR", 5000f);
            bool gatewayOk = gatewayRuleOk && intlBlocked && domesticKept && noOvershoot;

            // ⑨ 时刻容量（★2026-11-09 用户裁决改口径）：
            //    「机场有多少条跑道就释放多少条时刻容量，不要设置峰刻。」
            //    容量现在只有一条规则：协调时段内、且不在宵禁中的每个 15 分钟刻 = 跑道数。
            //    这里把四件事钉死，免得日后有人又把峰谷曲线塞回来：
            //      ① 日容量 = 跑道数 × 该场可放刻数（宵禁 / 协调窗口外记 0）；
            //      ② 任一刻容量 == 跑道数（不再是「峰刻 / 谷刻」两套数）；
            //      ③ 24 小时刻钟求和 == 日容量（两条读数不会互相打脸）；
            //      ④ 每个协调机场的日容量 > 0，且跑道数在表内（不落回等级默认值）。
            bool capacityOk = true;
            var capCases = new[]
            {
                // code, 日容量, 抽查刻, 该刻期望容量（= 跑道数）
                ("PEK", 204, 8 * 60, 3),   // 3 跑道，23:00–06:00 宵禁 → 17h × 4 × 3
                ("PVG", 384, 8 * 60, 4),   // 4 跑道，无宵禁 → 96 × 4
                ("ATL", 480, 8 * 60, 5),   // 5 跑道
                ("XMN", 96, 8 * 60, 1),    // 单跑道：每刻恒 1，与「跑道决定容量」一致
                ("CAN", 480, 8 * 60, 5),
                ("CKG", 384, 8 * 60, 4),
                ("KMG", 288, 8 * 60, 3),
                ("WUH", 288, 8 * 60, 3),
                ("URC", 288, 8 * 60, 3),
                ("PKX", 384, 8 * 60, 4),
                ("CDG", 272, 8 * 60, 4),   // EEA 宵禁 → 68 刻 × 4
            };
            var capLog = new StringBuilder();
            for (int i = 0; i < capCases.Length; i++)
            {
                var (code, day, peakMin, peakCap) = capCases[i];
                int gotDay = AirportCatalog.DayCapacity(code);
                int gotPeak = AirportCatalog.QuarterCapacity(code, peakMin);
                int sum = 0;
                bool uniform = true;
                for (int m = 0; m < 1440; m += ScheduleSlots.SlotGridStepMinutes)
                {
                    int q = AirportCatalog.QuarterCapacity(code, m);
                    sum += q;
                    // 「不要设峰刻」的硬判据：凡是放了槽的刻，条数必须一模一样。
                    if (q != 0 && q != peakCap) uniform = false;
                }
                if (gotDay != day || gotPeak != peakCap || sum != gotDay || !uniform
                    || gotPeak != AirportCatalog.RunwayCount(code))
                {
                    capacityOk = false;
                    capLog.Append($"{code} 日={gotDay}(应{day}) 抽查刻={gotPeak}(应{peakCap}) 刻合计={sum}"
                                  + $" 刻内均匀={uniform} 跑道数={AirportCatalog.RunwayCount(code)}; ");
                }
            }
            for (int i = 0; i < airports.Count; i++)
            {
                var a = airports[i];
                if (!a.coordinated) continue;
                if (AirportCatalog.DayCapacity(a.code) <= 0)
                {
                    capacityOk = false;
                    capLog.Append($"{a.code} 是协调机场却日容量 0; ");
                }
                // 跑道数必须**显式**在 AirportRunways 表里：落回「按等级猜」会悄悄
                // 把容量改成 3/2/1，而报表仍显示 tsv 里的真值 —— 这类分叉最难查。
                if (!AirportCatalog.HasRunwayData(a.code))
                {
                    capacityOk = false;
                    capLog.Append($"{a.code} 缺跑道数（已落回等级默认值）; ");
                }
            }

            // ⑩ 「申报超额」表的三条硬约束（★2026-11-09 用户模型：「跑道数 = 容量」是基线，
            //    LHR / DXB 靠混合起降把申报容量做到高于跑道数）。例外最容易腐化，故逐条验：
            //      ① 理由非空 —— 玩家问「凭什么它比跑道多」时要有答案；
            //      ② 等效 ≥ 物理 —— 想比跑道少请改跑道表，不许在这里做减法；
            //      ③ 等效 ≤ 物理 × 2 —— 超过两倍不是混合运行，是数据填错。
            bool overridesOk = true;
            var overCodes = AirportCatalog.EffectiveRunwayOverrideCodes();
            for (int i = 0; i < overCodes.Length; i++)
            {
                string code = overCodes[i];
                int eff = AirportCatalog.RunwayCount(code);
                int phy = AirportCatalog.PhysicalRunwayCount(code);
                string reason = AirportCatalog.EffectiveRunwayReason(code);
                if (string.IsNullOrWhiteSpace(reason))
                {
                    overridesOk = false;
                    capLog.Append($"{code} 申报超额但没写理由; ");
                }
                if (eff < phy || eff > phy * 2)
                {
                    overridesOk = false;
                    capLog.Append($"{code} 等效跑道 {eff} 越界（物理 {phy}，允许 {phy}..{phy * 2}）; ");
                }
                // 超额表里的机场必须真的存在，否则就是拼错代码后静默失效。
                if (AirportCatalog.FindByCode(code).code.Length == 0)
                {
                    overridesOk = false;
                    capLog.Append($"{code} 申报超额的代码在机场目录里不存在; ");
                }
            }

            bool all = apNoDup && ecoNoDup && sameSet && geoUsable && batchAOk && batchBOk && batchBPrimeOk
                       && offCatalog && takeoverCount && codeUnique && hubExists && searchOk
                       && noUltraShort && gatewayOk && capacityOk && overridesOk;

            string tail = all ? "✓" : "✗";
            if (!all)
            {
                tail += $"（重复码={!apNoDup} 经济表重复={!ecoNoDup} 三表同集合={sameSet} 缺经济={missingInEco.Trim()} 缺坐标={missingInGeo.Trim()}"
                        + $" 多在机场目录={missingInAp.Trim()} 坐标可用={geoUsable}({badCoord})"
                        + $" 批次A={batchAOk} 批次B={batchBOk} 批次B'={batchBPrimeOk} 反例仍在目录外={offCatalog}"
                        + $" 接管航司数={takeoverCount}({selectable.Count}/{ecoList.Length}) 码唯一={codeUnique}"
                        + $" 新枢纽有司={hubExists} 缺={hubMissing.ToString().Trim()}"
                        + $" 搜索匹配={searchOk}(NGB首位={ngbOnly} 无CKG={noCkg} 英文词首={wordPrefixOk}"
                        + $" 中文前缀={zhPrefixOk} 新城市={newCityOk} 码前缀={codePrefixOk} 解析一致={resolveSame})"
                        + $" 批次B无超短={noUltraShort}({ultra.ToString().Trim()})"
                        + $" 跨境门户={gatewayOk}(够格判定={gatewayRuleOk} 支线被拦={intlBlocked} 国内照常={domesticKept} 未连坐真实门户={noOvershoot})"
                        + $" 时刻容量={capacityOk}({capLog.ToString().Trim()})"
                        + $" 申报超额={overridesOk}({overCodes.Length} 场)）{batch}";            }

            Debug.Log($"机场底表三表贯通 → 机场={airports.Count} 城市经济={ecoList.Length} 坐标={geoList.Length}"
                      + $"　码无重复={apNoDup && ecoNoDup}　三表同集合={sameSet}　坐标可用={geoUsable}"
                      + $"　批次A入表={batchAOk}（ZUH/WUX/NGB/WNZ）　批次B入表={batchBOk}（NNG/KWE/TYN/HET/TNA）"
                      + $"　批次B'入表={batchBPrimeOk}（LXA）"
                      + $"　目录外反例={offCatalog}（{OffCatalogName}）"
                      + $"　批次B无超短航段={noUltraShort}"
                      + $"　跨境门户门槛={gatewayOk}"
                      + $"　接管航司={selectable.Count}　司码唯一={codeUnique}　新枢纽有司={hubExists}"
                      + $"　搜NGB不串CKG={searchOk}"
                      + $"　{tail}");
            return all;
        }
    }
}
#endif
