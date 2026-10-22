using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 现实新闻 → 经营影响（研判层）。
    /// <para>
    /// 现实订阅源只给「内容」，本来不带数值；这一层用关键词把内容映射成**保守**的
    /// <c>effectType / effectValue</c>，让「收到国内新闻」真的能扰动经营数据
    /// （航油、客座率、准点率、现金、满意度、机场冻结）。
    /// </para>
    /// <para>
    /// 三条自我约束，避免自动扰动把经济掀翻：
    /// ① 幅度小（航油 ±2.5%、客座率 ±1.5pt、准点率 −5pt），且每个季度最多采纳
    ///    <see cref="MaxInferredPerQuarter"/> 条研判影响（由 <c>ExternalEventManager</c> 把关）；
    /// ② 只认「强关键词」，宁可不判也不乱判；判不出机场时绝不冻结全域，只降准点率；
    /// ③ 邮件里明确标注「公司研判（非原文）」，玩家能看出这是推断而不是官方通报。
    /// </para>
    /// </summary>
    public static class ExternalImpactRules
    {
        /// <summary>每季度最多几条「关键词研判」影响生效（按生效时间所在季度计）。</summary>
        public const int MaxInferredPerQuarter = 3;

        public struct Impact
        {
            public string type;
            public float value;
            public string region;
            public int hours;
            public string note;
        }

        // —— 关键词表 ——

        static readonly Regex RxAtc = new Regex(
            @"空域(?:关闭|限制|管制|调整)|流量控制|航空管制|空管(?:管制|限制)|禁飞|"
            + @"军事演习|军事活动|临时空域|航路(?:关闭|调整)|时刻(?:收紧|削减)",
            RegexOptions.Compiled);

        static readonly Regex RxTyphoon = new Regex(@"台风|飓风|热带风暴|强热带风暴", RegexOptions.Compiled);

        static readonly Regex RxSevere = new Regex(@"登陆|停飞|取消|关闭|停航|影响|逼近", RegexOptions.Compiled);

        static readonly Regex RxSevereWeather = new Regex(
            @"大雾|浓雾|低能见度|暴雪|大雪|冻雨|雷暴|强对流|冰雹|沙尘暴|强风|大风", RegexOptions.Compiled);

        static readonly Regex RxAirportWord = new Regex(@"机场|航班|跑道|航站楼|起降", RegexOptions.Compiled);

        static readonly Regex RxCloseWord = new Regex(@"关闭|停飞|停航|取消|暂停|中断", RegexOptions.Compiled);

        static readonly Regex RxFuelWord = new Regex(
            @"航油|航空煤油|航空燃油|燃油附加费|燃油成本|油价", RegexOptions.Compiled);
        static readonly Regex RxUpWord = new Regex(
            @"上调|上涨|提价|涨价|攀升|创新高|走高|增加", RegexOptions.Compiled);
        static readonly Regex RxDownWord = new Regex(
            @"下调|下跌|降价|回落|走低|新低|下降", RegexOptions.Compiled);

        static readonly Regex RxDemandWord = new Regex(
            @"客流|旅客吞吐量|客座率|出行需求|旅游|公商务出行", RegexOptions.Compiled);
        static readonly Regex RxDemandUp = new Regex(
            @"创新高|增长|回暖|激增|旺盛|恢复|反弹", RegexOptions.Compiled);
        static readonly Regex RxDemandDown = new Regex(
            @"下降|下滑|骤降|萎缩|低迷|减少", RegexOptions.Compiled);

        static readonly Regex RxCompetitor = new Regex(
            @"罢工|停飞|破产|倒闭|退出|削减运力|取消订单", RegexOptions.Compiled);
        static readonly Regex RxAirlineWord = new Regex(
            @"航司|航空公司|航空集团|航空有限公司", RegexOptions.Compiled);

        static readonly Regex RxAccident = new Regex(
            @"坠机|空难|事故|紧急撤离|发动机起火|中断起飞|严重事故征候", RegexOptions.Compiled);

        static readonly Regex RxPenalty = new Regex(
            @"罚款|处罚|约谈|整改|吊销|暂停运营|限制经营|行政处理", RegexOptions.Compiled);

        /// <summary>股市/理财噪声的二次兜底：这类稿子即使命中「油价」也不该扰动经营。</summary>
        static readonly Regex RxNoise = new Regex(
            @"涨停|跌停|主力资金|个股|A股|港股|美股|股指|基金|ETF|券商|研报|目标价|市值|市值管理",
            RegexOptions.Compiled);

        /// <summary>城市名 → 候选机场（同一城市有多个机场时优先玩家在飞的）。</summary>
        static readonly (string city, string[] codes)[] Cities =
        {
            ("北京", new[] { "PEK", "PKX" }), ("上海", new[] { "SHA", "PVG" }),
            ("广州", new[] { "CAN" }), ("深圳", new[] { "SZX" }),
            ("成都", new[] { "CTU", "TFU" }), ("重庆", new[] { "CKG" }),
            ("西安", new[] { "XIY" }), ("昆明", new[] { "KMG" }),
            ("杭州", new[] { "HGH" }), ("南京", new[] { "NKG" }),
            ("武汉", new[] { "WUH" }), ("长沙", new[] { "CSX" }),
            ("厦门", new[] { "XMN" }), ("福州", new[] { "FOC" }),
            ("青岛", new[] { "TAO" }), ("天津", new[] { "TSN" }),
            ("大连", new[] { "DLC" }), ("沈阳", new[] { "SHE" }),
            ("哈尔滨", new[] { "HRB" }), ("乌鲁木齐", new[] { "URC" }),
            ("兰州", new[] { "LHW" }), ("三亚", new[] { "SYX" }),
            ("海口", new[] { "HAK" }), ("香港", new[] { "HKG" }),
            ("澳门", new[] { "MFM" }), ("台北", new[] { "TPE" }),
            ("东京", new[] { "NRT", "HND" }), ("首尔", new[] { "ICN" }),
            ("新加坡", new[] { "SIN" }), ("曼谷", new[] { "BKK" }),
            ("伦敦", new[] { "LHR" }), ("巴黎", new[] { "CDG" }),
            ("法兰克福", new[] { "FRA" }), ("阿姆斯特丹", new[] { "AMS" }),
            ("洛杉矶", new[] { "LAX" }), ("旧金山", new[] { "SFO" }),
            ("纽约", new[] { "JFK" }), ("悉尼", new[] { "SYD" }),
            ("迪拜", new[] { "DXB" }), ("多哈", new[] { "DOH" })
        };

        static readonly Regex RxIata = new Regex(@"(?<![A-Za-z])([A-Z]{3})(?![A-Za-z])", RegexOptions.Compiled);

        /// <summary>
        /// 尝试从标题/正文研判一条经营影响。
        /// <paramref name="operatedCodes"/> = 玩家当前在飞机场（用于把城市名映射到「自己真会受影响」的机场）。
        /// </summary>
        public static bool TryInfer(string title, string content, ICollection<string> operatedCodes, out Impact impact)
        {
            impact = default;
            string hay = (title ?? "") + " " + (content ?? "");
            if (hay.Trim().Length < 6) return false;
            if (RxNoise.IsMatch(hay)) return false;

            string region = ResolveRegion(hay, operatedCodes);

            // ① 空管 / 空域限制：命中即优先（对运行影响最直接）
            if (RxAtc.IsMatch(hay))
            {
                if (!string.IsNullOrEmpty(region))
                {
                    impact = Make("freezeFlights", 0f, region, 12, "空域/管制关键词命中，按 12 小时运行限制处理");
                    return true;
                }
                impact = Make("onTime", -6f, "", 0, "空域/管制关键词命中，但未能定位机场：只降准点率");
                return true;
            }

            // ② 台风：有登陆/停飞语义才判，且必须定位到机场
            if (RxTyphoon.IsMatch(hay) && RxSevere.IsMatch(hay))
            {
                if (!string.IsNullOrEmpty(region))
                {
                    impact = Make("freezeFlights", 0f, region, 36, "台风 + 停飞/关闭语义，按 36 小时运行限制处理");
                    return true;
                }
                impact = Make("onTime", -5f, "", 0, "台风影响但未定位机场：只降准点率");
                return true;
            }

            // ③ 极端天气 + 机场语境
            if (RxSevereWeather.IsMatch(hay) && RxAirportWord.IsMatch(hay))
            {
                if (!string.IsNullOrEmpty(region) && RxCloseWord.IsMatch(hay))
                {
                    impact = Make("freezeFlights", 0f, region, 8, "极端天气 + 关闭/停飞语义，按 8 小时运行限制处理");
                    return true;
                }
                impact = Make("onTime", -5f, "", 0, "极端天气命中，按准点率 −5 个百分点处理");
                return true;
            }

            // ④ 航油价格（百分比口径，作用在游戏现行油价上）
            if (RxFuelWord.IsMatch(hay))
            {
                bool up = RxUpWord.IsMatch(hay);
                bool down = RxDownWord.IsMatch(hay);
                if (up != down)
                {
                    impact = Make("fuelPrice", up ? 2.5f : -2.5f, "", 0,
                        up ? "航油/燃油附加费上涨语义，按 +2.5% 处理" : "航油/燃油附加费下调语义，按 −2.5% 处理");
                    return true;
                }
            }

            // ⑤ 需求 / 客座率
            if (RxDemandWord.IsMatch(hay))
            {
                bool up = RxDemandUp.IsMatch(hay);
                bool down = RxDemandDown.IsMatch(hay);
                if (up != down)
                {
                    impact = Make("loadFactor", up ? 1.5f : -2f, "", 0,
                        up ? "出行需求走强语义，按客座率 +1.5 个百分点处理" : "出行需求走弱语义，按客座率 −2 个百分点处理");
                    return true;
                }
            }

            // ⑥ 竞对收缩 → 运力外溢
            if (RxCompetitor.IsMatch(hay) && RxAirlineWord.IsMatch(hay))
            {
                impact = Make("loadFactor", 1f, "", 0, "竞对收缩语义，按客座率 +1 个百分点处理");
                return true;
            }

            // ⑦ 安全事件 → 董事会满意度
            if (RxAccident.IsMatch(hay))
            {
                impact = Make("satisfaction", -2f, "", 0, "安全事件语义，按董事会满意度 −2 点处理");
                return true;
            }

            // ⑧ 监管处罚 → 现金（万美元）
            if (RxPenalty.IsMatch(hay))
            {
                impact = Make("cash", -50f, "", 0, "监管处罚语义，按一次性支出 50 万美元处理");
                return true;
            }

            return false;
        }

        static Impact Make(string type, float value, string region, int hours, string note)
        {
            return new Impact
            {
                type = type,
                value = value,
                region = region ?? "",
                hours = hours,
                note = note
            };
        }

        /// <summary>把正文里的城市/机场名映射成 IATA；定位不到返回空串（调用方据此降级，不冻结全域）。</summary>
        public static string ResolveRegion(string text, ICollection<string> operatedCodes)
        {
            if (string.IsNullOrEmpty(text)) return "";

            for (int i = 0; i < Cities.Length; i++)
            {
                if (text.IndexOf(Cities[i].city, StringComparison.Ordinal) < 0) continue;
                string[] codes = Cities[i].codes;
                for (int j = 0; j < codes.Length; j++)
                    if (Operated(operatedCodes, codes[j])) return codes[j];
                return codes[0];
            }

            // 机场全名（如「广州白云」「上海浦东」）直接命中
            var all = AirportCatalog.Airports;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (string.IsNullOrEmpty(a.nameZh)) continue;
                if (text.IndexOf(a.nameZh, StringComparison.Ordinal) < 0) continue;
                return a.code;
            }

            // 三字码（如「PEK 大面积延误」）
            var m = RxIata.Match(text);
            if (m.Success && !string.IsNullOrEmpty(AirportCatalog.FindByCode(m.Groups[1].Value).code))
                return m.Groups[1].Value.ToUpperInvariant();

            return "";
        }

        static bool Operated(ICollection<string> operatedCodes, string code)
        {
            if (operatedCodes == null || operatedCodes.Count == 0) return false;
            foreach (var c in operatedCodes)
                if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
