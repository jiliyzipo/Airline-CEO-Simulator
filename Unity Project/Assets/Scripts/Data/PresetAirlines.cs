using System.Collections.Generic;
using UnityEngine;

namespace AirlineCEO.Data
{
    /// <summary>可接管航司：四家具名预设 + 按城市经济目录为每座枢纽生成一家。</summary>
    public static class PresetAirlines
    {
        public const string Startup = "preset_startup";
        public const string Regional = "preset_regional";
        public const string Legacy = "preset_legacy";
        public const string Distressed = "preset_distressed";
        public const string HubPrefix = "hub_";

        public static readonly string[] AllIds = { Startup, Regional, Legacy, Distressed };

        static List<AirlineProfile> _selectable;

        public static AirlineProfile Template(string id)
        {
            return Resolve(id);
        }

        public static AirlineProfile Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) id = Regional;
            if (id.StartsWith(HubPrefix))
            {
                string hub = id.Substring(HubPrefix.Length);
                return FromHub(hub);
            }
            switch (id)
            {
                // 现金不是随手填的数：它是「目标开局跑道 × 实测净烧钱」的**不动点解**，
                // 而净烧钱里含月均还本付息 + 月均全险保费（口径见 TakeoverWorldBuilder.BuildPreview）。
                // 贷款本金又按现金反推（principal = 现金 × 负债率 × 2.2），所以现金涨、
                // 月供也涨——不能简单乘一下，必须解 cash / (操作烧钱 + 0.0616×负债率×cash) = 目标月数，
                // 即 cash = 目标月数 × 操作烧钱 / (1 − 0.0616 × 目标月数 × 负债率)。
                // 2026-10-21 K10：过渡收入改为按剩余档期封顶、保费并入跑道、航线固定成本改为逐段实扣，
                // 三个口径都变了，遂用 `--routeinventory` 的「现金反推」表整批重解（8/6/4/3 月）。
                // K10 第 6 轮：航线月收入改按**实际班期**折算执飞天数（2~3 班/周的亏损线不再按 30 天计提），
                // 亏损线占比高的 ★4/★5 净烧钱随之下降，故按新烧钱重解现金：1220.3/1985.1 万·月。
                case Regional:
                    return StampGeo(Base("preset_regional", "北辰快线", "North Star Express", "NS", "NSX", "PEK", 3,
                        10, 8, 3097f, 0.65f, 12, "区域网络已成型，扩张与亏损航线并存。"));
                case Legacy:
                    return StampGeo(Base("preset_legacy", "东海联合", "East Sea United", "EU", "ESU", "CAN", 4,
                        30, 25, 4861f, 0.78f, 18, "老牌全服务航司，机队老化、成本高企。"));
                case Distressed:
                    return StampGeo(Base("preset_distressed", "天枢航空", "Sky Pivot Air", "SP", "SPV", "PVG", 5,
                        50, 40, 5870f, 0.92f, 22, "现金告急，网络过大，董事会要求立刻止血。"));
                default:
                    return StampGeo(Base("preset_startup", "青禾航空", "Green Harvest Air", "GH", "GHA", "CTU", 2,
                        3, 2, 1434f, 0.35f, 6, "刚完成首航，规模小、包袱轻。"));
            }
        }

        public static List<AirlineProfile> AllSelectable()
        {
            if (_selectable != null) return _selectable;
            var list = new List<AirlineProfile>();
            var usedIata = new HashSet<string> { "GH", "NS", "EU", "SP" };
            var all = CityEconomyCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                var e = all[i];
                if (e == null || string.IsNullOrEmpty(e.iata)) continue;
                list.Add(FromHub(e.iata, usedIata));
            }
            _selectable = list;
            return list;
        }

        public static AirlineProfile FromHub(string hub)
        {
            var used = new HashSet<string> { "GH", "NS", "EU", "SP" };
            return FromHub(hub, used);
        }

        static AirlineProfile FromHub(string hub, HashSet<string> usedIata)
        {
            if (string.IsNullOrEmpty(hub)) hub = "PEK";
            hub = hub.ToUpperInvariant();
            // 四个具名预设 = 四座最大的中国枢纽，**一档一座、按客流升序**（2026-10-22 D2 第三步）：
            //   成都 30M → ★2 青禾（最小）  北京 52M → ★3 北辰  广州 63M → ★4 东海  上海 76M → ★5 天枢
            //
            // 修复前的映射是 广州★2 / 北京★3 / 上海★4 / 成都★5，其中两处逆序：
            // 成都（30M）★5 比北京（52M）★3 更难，广州（63M）★2 比北京★3 更容易 ——
            // 排序后是 CTU(30M★5) > PEK(52M★3) > CAN(63M★2)，与「客流越大越难」完全相反。
            // 玩家看到的是「广州青禾 ★2（3 架）」与「成都天枢 ★5（50 架）」，广州是现实三大枢纽之一、
            // 客流是成都的两倍，档位却颠倒。
            //
            // 为什么是「换枢纽」而不是「换档位」：档位与机型/机队/现金/负债率是成套调的
            // （Startup 3 架 1434 万 / Distressed 50 架 5870 万），改档位等于把四套参数全部重做；
            // 而四个枢纽都是中国城市，换枢纽不会出现「中国名航司驻外」的国名矛盾。
            // 调整后档位与客流严格同序，且四座恰好是目录里最大的四座中国枢纽 —— 规则可一句话说清。
            if (hub == "CTU") return StampGeo(Homed(Resolve(Startup), hub));
            if (hub == "PEK") return StampGeo(Homed(Resolve(Regional), hub));
            if (hub == "CAN") return StampGeo(Homed(Resolve(Legacy), hub));
            if (hub == "PVG") return StampGeo(Homed(Resolve(Distressed), hub));

            var e = CityEconomyCatalog.Find(hub);
            int stars = StarsFor(e);
            string seedId = stars <= 2 ? Startup : stars == 3 ? Regional : stars == 4 ? Legacy : Distressed;
            var t = Resolve(seedId);
            // ★2 连续标度（2026-10-22 D2 第二步）：删掉「59 家共用一套 Startup 参数」的克隆。
            // 修复前 ★2 的 59 家机队恒为 3、航线恒为 2、现金恒为 1434 —— 选司只换枢纽和那两条航线。
            // 现在机队/航线/现金随枢纽客流连续变化，但**档位与目标跑道不变**（仍是 difficulty=2、8 个月），
            // 所以 K10 的 8/6/4/3 跑道带、以及 TakeoverWorldBuilder 里所有按 difficulty 分支的逻辑都不受影响。
            if (stars <= 2) ScaleStartup(t, e);
            t.id = HubPrefix + hub;
            t.hubAirport = hub;
            StampGeo(t);
            string city = t.cityZh ?? hub;
            t.nameCN = stars <= 2 ? city + "通航" : stars == 3 ? city + "快线" : stars == 4 ? city + "航空" : city + "联合";
            t.nameEN = (t.cityZh ?? hub) + " Air";
            t.iataCode = NextCode(hub, usedIata, 2);
            t.icaoCode = NextCode(hub + "X", usedIata, 3);
            t.blurb = stars <= 2
                ? "枢纽规模较小，包袱轻，适合稳健起步。"
                : stars == 3
                    ? "区域网络已成型，扩张与亏损航线并存。"
                    : stars == 4
                        ? "全服务骨干航司，成本与机队老化需正视。"
                        : "现金紧张、网络过大，董事会要求立刻止血。";
            return t;
        }

        static AirlineProfile StampGeo(AirlineProfile p)
        {
            if (p == null) return null;
            var e = CityEconomyCatalog.Find(p.hubAirport);
            if (e != null)
            {
                p.continentZh = e.continentZh;
                p.countryZh = e.countryZh;
                p.cityZh = e.cityZh;
            }
            return p;
        }

        /// <summary>
        /// 客流门槛（百万人/年）。**档位必须是客流的单调函数** —— 这一条比具体阈值更重要。
        ///
        /// 2026-10-22 D2 修复：原先 ★4/★5 用 IATA 字符串哈希分界
        /// （<c>h % 5 == 0 ? 5 : 4</c>，h 由三字码逐字符 <c>h*31+c</c> 得到），
        /// 结果 87M 的迪拜（DXB）被分成 ★4，而 76M 的伊斯坦布尔（IST）、77.8M 的丹佛（DEN）是 ★5；
        /// 78M 的羽田（HND）低于 77.8M 的丹佛。分界与规模无关，玩家看不出规律，我们也解释不了。
        /// 更糟的是当时的「更高星级必须更紧」断言是循环的：现金本就按档位反解，
        /// 所以它只能发现「烧钱改了但现金没重解」，**不可能**发现档位分错。
        ///
        /// 现改为纯阈值，档位随客流单调不减，并加了独立的单调性回归（见 tools/balance 的
        /// 「星级必须是客流的单调函数」断言），档位分错会直接翻红。
        ///
        /// 阈值取 {40, 70, 78}：40/70 沿用原状；78 是**为了让 ★5 家数维持修复前的 5 家**
        /// （ATL 104 / DXB 87 / DFW 81.8 / LHR 79 / HND 78），
        /// 避免一改动就把 K10 的 8/6/4/3 跑道带重新解一遍。
        /// 这不是从第一原理推出来的数，是「先纠错、不顺手改平衡」的取舍，如实记在此处。
        /// </summary>
        public const float Star5PaxMillion = 78f;
        public const float Star4PaxMillion = 70f;
        public const float Star3PaxMillion = 40f;

        /// <summary>
        /// 把具名预设落到请求的枢纽上 —— **分支表是唯一权威**。
        ///
        /// 为什么不直接 <c>Resolve(Startup)</c>：那返回的是 <see cref="Base"/> 里写死的 hubAirport，
        /// 于是「分支表说 CTU→Startup、Base 里 Startup 却写着 CAN」这种错位不会有任何编译或运行错误，
        /// 只会**静默**返回一家枢纽与请求不符的航司（本次修 D2 时就亲手踩了一次：
        /// 只改分支表不改 Base，断言照旧全绿，因为断言读的是 profile 自带的 hubAirport）。
        /// 现在由这里统一覆盖，Base 的 hub 只在直接调用 <see cref="Template"/> 时当默认值。
        /// </summary>
        static AirlineProfile Homed(AirlineProfile p, string hub)
        {
            if (p == null) return null;
            p.hubAirport = hub;
            return p;
        }

        static int StarsFor(CityEconomyEntry e)
        {
            float pax = e != null ? e.paxMillion : 20f;
            if (pax >= Star5PaxMillion) return 5;
            if (pax >= Star4PaxMillion) return 4;
            if (pax >= Star3PaxMillion) return 3;
            return 2;
        }

        /// <summary>
        /// ★2 的连续标度：按枢纽客流推导机队 / 航线 / 现金（2026-10-22 D2 第二步）。
        ///
        /// **为什么需要它**：修复前 ★2 的 59 家全部复用 <c>Startup</c> 模板，
        /// 机队恒为 3、航线恒为 2、现金恒为 1434、净烧 183.2、跑道 7.8 —— 是精确的整数，不是巧合。
        /// 玩家在 57% 的名单里换航司，换掉的只有枢纽和那两条航线，规模与压力一模一样。
        /// 而档内客流从 2.8M（ACC）到 38.2M（MSP）差 13.6 倍，这个跨度原本没有任何体现。
        ///
        /// **为什么不拆成 ★2a/★2b/★2c**：这个游戏的「档位」就是跑道长度
        /// （<see cref="Systems.TakeoverWorldBuilder.TargetRunwayMonths"/> = 8/6/4/3 月）。
        /// 若子档只改规模不改跑道，几个子档的时间压力完全相同，那只是给同一难度贴三个标签；
        /// 若子档连跑道一起改，`difficulty` 就要扩到 6~7 个值，
        /// 而 `TakeoverWorldBuilder` 里 `&lt;=2 / ==3 / ==4 / &gt;=4` 这类分支有 8 处（机组缺员、
        /// 四种过渡业务、宽体比例、机队机型、利率），每一处都要重新裁定语义。
        /// 故采用连续标度：**规模连续变、档位与跑道不动**，克隆感消失而 K10 跑道带零风险。
        ///
        /// 映射（客流 3M→40M 线性，两端夹住）：
        /// 机队 2→8 架、航线 1→5 条，并保证 `机队 ≥ 航线 + 1`（过渡业务要抽 1 架，见 BuildFleet）。
        /// 现金不是随手给的，是**按目标 8 个月跑道反推**的：
        /// <c>现金 = 1434 × (机队/3)^p</c>，p 由 <c>--routeinventory</c> 实测拟合（见下方常量注释）。
        /// </summary>
        static void ScaleStartup(AirlineProfile t, CityEconomyEntry e)
        {
            if (t == null) return;
            float pax = e != null ? e.paxMillion : 20f;
            // 归一化到 0..1：3M 以下算最小支线，40M（★3 下界）算 ★2 的上限。
            float u = Mathf.Clamp01((pax - 3f) / (Star3PaxMillion - 3f));
            int fleet = 2 + Mathf.RoundToInt(u * 6f);
            int routes = 1 + Mathf.RoundToInt(u * 4f);
            // 过渡业务会从机队尾部抽 1 架（difficulty=2 → DryLeaseCount=1），
            // 剩下的必须够配航线，否则会生成「无机可配」的航线（旧版踩过这个坑）。
            if (fleet < routes + 1) fleet = routes + 1;
            t.fleetCount = fleet;
            t.routeCount = routes;
            // 现金与**净烧钱**挂钩，而不是与机队线性挂钩。
            // 第一版用 `1434 × (机队/3)^1`，实测跑道从 5.54 宽到 11.16（均值 9.10）——
            // 因为机队 2→8 只让烧钱涨 2.07 倍（168.6→348.9），现金却涨了 4 倍。
            // 拟合出的关系是「烧钱 ≈ 截距 + 斜率×机队」：总部开销 80 万固定、
            // 保费与租赁按机队摊，所以存在固定截距；再按闭式解
            // `现金 = 目标跑道 × 运营烧钱 / (1 − 0.0616 × 目标跑道 × 负债率)`（★2：分母 0.8275）反推。
            t.cash = StartupCashBaseWan + StartupCashPerFleetWan * fleet;
        }

        /// <summary>
        /// ★2 现金拟合常量（万元）：<c>现金 = 1044 + 193 × 机队</c>。
        ///
        /// 由 <c>--routeinventory</c> 逐家实测反推，不是拍脑袋：
        /// 实测「运营烧钱 B」(= 净烧钱 − 还本付息) 与机队近似线性
        /// （机队 2→148 / 3→166 / 4→186 / 5→194 / 6→231 / 7→268），
        /// 再按目标 8 月跑道、负债率 35% 的闭式解 <c>cash = 8B/0.8275 ≈ 9.67B</c> 折算。
        ///
        /// 取「机队」而不是「客流」当自变量，是因为客流要先过 <see cref="ScaleStartup"/> 的取整映射，
        /// 机队才是真正决定成本与收入的那个量；用客流会多做一次无意义的换算。
        ///
        /// **改任何成本项（维护 / 租赁 / 人力 / 保费 / 总部开销）后必须重跑拟合**，
        /// 否则 ★2 的 59 家会整体漂出 8 月跑道带。
        /// </summary>
        const float StartupCashBaseWan = 1044f;
        const float StartupCashPerFleetWan = 193f;

        static string NextCode(string seed, HashSet<string> used, int len)
        {
            string raw = (seed ?? "XX").ToUpperInvariant();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < raw.Length && sb.Length < len; i++)
            {
                char c = raw[i];
                if (c >= 'A' && c <= 'Z') sb.Append(c);
            }
            while (sb.Length < len) sb.Append('X');
            string code = sb.ToString();
            if (used == null)
            {
                return code;
            }
            if (used.Add(code)) return code;
            for (int n = 0; n < 26 * 26; n++)
            {
                char a = (char)('A' + (n % 26));
                char b = (char)('A' + ((n / 26) % 26));
                string alt = len == 2 ? "" + a + b : "" + a + b + 'A';
                if (used.Add(alt)) return alt;
            }
            used.Add(code);
            return code;
        }

        static AirlineProfile Base(string id, string cn, string en, string iata, string icao, string hub,
            int stars, int fleet, int routes, float cash, float debt, int avgAge, string blurb)
        {
            return new AirlineProfile
            {
                id = id,
                nameCN = cn,
                nameEN = en,
                iataCode = iata,
                icaoCode = icao,
                hubAirport = hub,
                difficulty = stars,
                fleetCount = fleet,
                routeCount = routes,
                cash = cash,
                debtRatio = debt,
                avgFleetAge = avgAge,
                blurb = blurb,
                departmentHeads = new List<DepartmentHeadSnapshot>
                {
                    new DepartmentHeadSnapshot { dept = "运营", name = "林启航", title = "运营总监" },
                    new DepartmentHeadSnapshot { dept = "机务", name = "周衡", title = "机务工程总监" },
                    new DepartmentHeadSnapshot { dept = "市场", name = "陈昭", title = "市场销售总监" },
                    new DepartmentHeadSnapshot { dept = "人力", name = "吴宁", title = "人力资源总监" },
                    new DepartmentHeadSnapshot { dept = "财务", name = "赵衡书", title = "财务总监" },
                    new DepartmentHeadSnapshot { dept = "安全", name = "孙安", title = "安全质量总监" }
                }
            };
        }
    }
}
