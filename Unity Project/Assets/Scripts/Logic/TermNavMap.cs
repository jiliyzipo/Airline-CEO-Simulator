using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 邮件关键词 / 顾问术语 → pageId（及可选航线上下文）。
    /// 白名单：只有表内词可点；邮件与顾问共用。
    /// </summary>
    public static class TermNavMap
    {
        public readonly struct Hit
        {
            public readonly string pageId;
            public readonly string routeKey; // 如 PEK-SHA，可空
            public Hit(string pageId, string routeKey = null)
            {
                this.pageId = pageId;
                this.routeKey = routeKey;
            }
        }

        /// <summary>更长的词优先匹配，避免 PEK 吃掉 PEK-SHA。</summary>
        static readonly (string term, string linkId)[] Terms =
        {
            ("PEK–SHA", "route:PEK-SHA"),
            ("PEK-SHA", "route:PEK-SHA"),
            ("SZX–PEK", "route:SZX-PEK"),
            ("SZX-PEK", "route:SZX-PEK"),
            ("深圳宝安", "city:SZX"),
            ("北京首都", "city:PEK"),
            ("上海虹桥", "city:SHA"),
            ("伦敦希思罗", "city:LHR"),
            ("洛杉矶", "city:LAX"),
            ("新加坡樟宜", "city:SIN"),
            ("航班周总计划", "page:n42_航班周总计划"),
            ("航班详细计划", "page:n43_航班详细计划"),
            ("航班计划", "page:n43_航班详细计划"),
            ("RPM 评估", "page:n35_新航线_RPM_评估台"),
            ("RPM", "page:n35_新航线_RPM_评估台"),
            ("董事会", "page:n20_董事会"),
            ("愿景", "page:n16_战略发展部"),
            ("工会谈判", "page:n37_工会谈判桌"),
            ("集体协议", "page:n37_工会谈判桌"),
            ("航权与时刻槽", "page:n77_航权与时刻槽"),
            ("时刻槽", "page:n77_航权与时刻槽"),
            ("航班异常", "page:n24_危机_IRROPS_弹窗"),
            ("IRROPS", "page:n24_危机_IRROPS_弹窗"),
            ("紧急融资", "page:n25_紧急融资_破产保护"),
            ("燃油对冲", "page:n31_对冲工作台"),
            ("对冲工作台", "page:n31_对冲工作台"),
            ("条款确认", "page:n103_条款确认"),
            ("破产保护", "page:n25_紧急融资_破产保护"),
            ("重组条件", "page:n106_重组条件勾选"),
            ("绩效与薪酬", "page:n88_绩效与薪酬"),
            ("年度调薪", "page:n88_绩效与薪酬"),
            ("奖金池", "page:n88_绩效与薪酬"),
            ("订单确认", "page:n105_订单确认"),
            ("检查应对", "page:n100_检查应对弹窗"),
            ("局方与法规事务", "page:n95_局方与法规事务"),
            ("局方检查", "page:n100_检查应对弹窗"),
            ("战略投资与并购", "page:n79_战略投资与并购"),
            ("投资并购", "page:n79_战略投资与并购"),
            ("代码共享", "page:n102_谈判弹窗"),
            ("星空联盟", "page:n78_联盟与战略合作"),
            ("消息中心", "page:n7_收件箱"),
            ("收件箱", "page:n7_收件箱"),
            ("快讯", "page:n7_收件箱"),
            ("运力规划", "page:n76_机队与运力规划"),
            ("运力缺口", "page:n76_机队与运力规划"),
            ("购机会议", "page:n97_购机决策会议"),
            ("新机购买", "page:n9_机队"),
            ("继任计划", "page:n91_人才档案与继任"),
            ("人才档案", "page:n91_人才档案与继任"),
            ("CEO资料", "page:n110_CEO资料"),
            ("个人资料", "page:n110_CEO资料"),
            ("CEO聘任合同", "page:n111_CEO合同"),
            ("CEO合同", "page:n111_CEO合同"),
            ("聘任合同", "page:n111_CEO合同"),
            ("干租合同", "page:n117_过渡业务"),
            ("湿租合同", "page:n117_过渡业务"),
            ("包机合同", "page:n117_过渡业务"),
            ("培训合同", "page:n117_过渡业务"),
            ("过渡业务", "page:n117_过渡业务"),
            ("安全仪表盘", "page:n19_安全质量部"),
            ("适航", "page:n17_机务工程部"),
            ("职业介绍所", "page:n112_职业介绍所"),
            ("员工档案", "page:n114_员工信息"),
            ("外部信息源", "page:n115_外部信息源"),
            ("外部消息", "page:n115_外部信息源"),
            ("数据源", "page:n115_外部信息源"),
            ("行情源", "page:n116_行情源"),
            ("油价报价", "page:n116_行情源"),
            ("副手", "page:n112_职业介绍所"),
        };

        static string[] _iataCodes;

        static (string term, string linkId)[] _cityNames;

        static (string term, string linkId)[] CityNameTerms()
        {
            if (_cityNames != null) return _cityNames;
            var map = new Dictionary<string, string>();
            void Add(string name, string iata)
            {
                if (string.IsNullOrEmpty(name) || name.Length < 2 || string.IsNullOrEmpty(iata)) return;
                if (!map.ContainsKey(name)) map[name] = iata.ToUpperInvariant();
            }
            var cities = CityEconomyCatalog.All;
            if (cities != null)
                for (int i = 0; i < cities.Length; i++)
                    Add(cities[i].cityZh, cities[i].iata);
            var airports = AirportCatalog.Airports;
            if (airports != null)
                for (int i = 0; i < airports.Count; i++)
                {
                    Add(airports[i].nameZh, airports[i].code);
                    Add(airports[i].nameEn, airports[i].code);
                }
            var list = new List<(string term, string linkId)>(map.Count);
            foreach (var kv in map)
                list.Add((kv.Key, "city:" + kv.Value));
            list.Sort((a, b) => b.term.Length.CompareTo(a.term.Length));
            _cityNames = list.ToArray();
            return _cityNames;
        }

        static string[] IataCodes()
        {
            if (_iataCodes != null) return _iataCodes;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cities = CityEconomyCatalog.All;
            if (cities != null)
                for (int i = 0; i < cities.Length; i++)
                    if (!string.IsNullOrEmpty(cities[i].iata))
                        set.Add(cities[i].iata.ToUpperInvariant());
            var airports = AirportCatalog.Airports;
            if (airports != null)
                for (int i = 0; i < airports.Count; i++)
                    if (!string.IsNullOrEmpty(airports[i].code))
                        set.Add(airports[i].code.ToUpperInvariant());
            var list = new List<string>(set);
            list.Sort((a, b) => b.Length.CompareTo(a.Length));
            _iataCodes = list.ToArray();
            return _iataCodes;
        }

        public static bool TryResolve(string linkId, out Hit hit)
        {
            hit = default;
            if (string.IsNullOrEmpty(linkId)) return false;

            if (linkId.StartsWith("page:", StringComparison.Ordinal))
            {
                hit = new Hit(linkId.Substring(5));
                return !string.IsNullOrEmpty(hit.pageId);
            }

            if (linkId.StartsWith("route:", StringComparison.Ordinal))
            {
                string key = linkId.Substring(6);
                hit = new Hit("n41_单航线", key);
                return true;
            }

            if (linkId.StartsWith("city:", StringComparison.Ordinal))
            {
                hit = new Hit("n12_航线网络", linkId.Substring(5));
                return true;
            }

            if (linkId.StartsWith("airline:", StringComparison.Ordinal))
            {
                hit = new Hit("n61_竞对详情", linkId.Substring(8));
                return true;
            }

            if (linkId.StartsWith("aircraft:", StringComparison.Ordinal))
            {
                hit = new Hit("n10_飞机详情_履历", linkId.Substring(9));
                return true;
            }

            if (linkId.StartsWith("ac:", StringComparison.Ordinal))
            {
                hit = new Hit("n10_飞机详情_履历", linkId.Substring(3));
                return true;
            }

            return false;
        }

        static readonly Regex RxOdPair = new Regex(
            @"(?<![A-Za-z0-9])([A-Za-z]{3})\s*[–—−\-－]\s*([A-Za-z]{3})(?![A-Za-z0-9])",
            RegexOptions.Compiled);

        /// <summary>把正文里的白名单词包成 TMP 链接（已含 link 的片段不重复处理）。</summary>
        public static string Decorate(string body)
        {
            if (string.IsNullOrEmpty(body)) return body ?? "";

            string s = WrapOdPairs(body);
            s = WrapTerms(s, Terms);
            s = WrapTerms(s, AirlineNameTerms());
            s = WrapAirportNames(s, CityNameTerms());
            var codes = IataCodes();
            var iataTerms = new (string term, string linkId)[codes.Length];
            for (int i = 0; i < codes.Length; i++)
                iataTerms[i] = (codes[i], "city:" + codes[i]);
            return WrapAircraftRegs(WrapAirportCodes(s, iataTerms));
        }

        /// <summary>飞机注册号关键词（点进飞机详情）。</summary>
        public static string AircraftLink(string registration)
        {
            if (string.IsNullOrEmpty(registration) || registration == "未分配")
                return registration ?? "";
            return Markup("aircraft:" + registration, registration);
        }

        static string WrapOdPairs(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return RxOdPair.Replace(s, m =>
            {
                if (InsideMarkup(s, m.Index)) return m.Value;
                string o = m.Groups[1].Value.ToUpperInvariant();
                string d = m.Groups[2].Value.ToUpperInvariant();
                if (!IsKnownIata(o) || !IsKnownIata(d)) return m.Value;
                string shown = AirportCatalog.Label(o) + "–" + AirportCatalog.Label(d);
                return Markup("route:" + o + "-" + d, shown);
            });
        }

        static bool IsKnownIata(string code)
        {
            var codes = IataCodes();
            for (int i = 0; i < codes.Length; i++)
                if (string.Equals(codes[i], code, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static readonly Regex RxAircraftReg = new Regex(
            @"(?<![A-Za-z0-9])(B-\d{3,5})(?![A-Za-z0-9])",
            RegexOptions.Compiled);

        static string WrapAircraftRegs(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return RxAircraftReg.Replace(s, m =>
            {
                if (InsideMarkup(s, m.Index)) return m.Value;
                return Markup("aircraft:" + m.Groups[1].Value, m.Groups[1].Value);
            });
        }

        static string Markup(string id, string shown) =>
            $"<color=#7EB6FF><u><link={id}>{shown}</link></u></color>";

        static (string term, string linkId)[] AirlineNameTerms()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            void Add(string name, string id)
            {
                if (string.IsNullOrEmpty(name) || name.Length < 2 || string.IsNullOrEmpty(id)) return;
                if (!map.ContainsKey(name)) map[name] = "airline:" + id;
            }
            Add("新加坡航空", "AI-SQ");
            Add("云翼航空", "AI-01");
            Add("青鸾航空", "AI-02");
            Add("银燕航空", "AI-03");
            Add("北辰货运", "AI-04");
            var gs = GameState.Instance;
            if (gs?.aiAirlines != null)
            {
                for (int i = 0; i < gs.aiAirlines.Count; i++)
                {
                    var a = gs.aiAirlines[i];
                    if (a != null) Add(a.name, a.id);
                }
            }
            var list = new List<(string term, string linkId)>(map.Count);
            foreach (var kv in map)
                list.Add((kv.Key, kv.Value));
            list.Sort((a, b) => b.term.Length.CompareTo(a.term.Length));
            return list.ToArray();
        }

        static string WrapAirportNames(string s, (string term, string linkId)[] terms)
        {
            if (string.IsNullOrEmpty(s) || terms == null) return s ?? "";
            for (int i = 0; i < terms.Length; i++)
            {
                string term = terms[i].term;
                string id = terms[i].linkId;
                if (string.IsNullOrEmpty(term) || !id.StartsWith("city:", StringComparison.Ordinal)) continue;
                string code = id.Substring(5);
                int at = 0;
                while (at < s.Length)
                {
                    int idx = s.IndexOf(term, at, StringComparison.Ordinal);
                    if (idx < 0) break;
                    if (InsideMarkup(s, idx)) { at = idx + term.Length; continue; }
                    int end = idx + term.Length;
                    int p = end;
                    if (p < s.Length && (s[p] == ' ' || s[p] == '·')) p++;
                    if (p + code.Length <= s.Length &&
                        string.Compare(s, p, code, 0, code.Length, StringComparison.OrdinalIgnoreCase) == 0)
                        end = p + code.Length;
                    string shown = AirportCatalog.Label(code);
                    if (string.IsNullOrEmpty(shown)) shown = s.Substring(idx, end - idx);
                    string wrap = Markup(id, shown);
                    s = s.Substring(0, idx) + wrap + s.Substring(end);
                    at = idx + wrap.Length;
                }
            }
            return s;
        }

        static string WrapAirportCodes(string s, (string term, string linkId)[] terms)
        {
            if (string.IsNullOrEmpty(s) || terms == null) return s ?? "";
            for (int i = 0; i < terms.Length; i++)
            {
                string code = terms[i].term;
                string id = terms[i].linkId;
                if (string.IsNullOrEmpty(code)) continue;
                int at = 0;
                while (at < s.Length)
                {
                    int idx = IndexOfIata(s, code, at);
                    if (idx < 0) break;
                    if (InsideMarkup(s, idx)) { at = idx + code.Length; continue; }
                    int start = idx;
                    var info = AirportCatalog.FindByCode(code);
                    string name = info.nameZh;
                    if (!string.IsNullOrEmpty(name) && idx >= name.Length)
                    {
                        int ns = idx - name.Length;
                        if (string.CompareOrdinal(s, ns, name, 0, name.Length) == 0)
                            start = ns;
                        else if (idx > name.Length && (s[idx - 1] == ' ' || s[idx - 1] == '·')
                                 && string.CompareOrdinal(s, idx - 1 - name.Length, name, 0, name.Length) == 0)
                            start = idx - 1 - name.Length;
                    }
                    int end = idx + code.Length;
                    string shown = AirportCatalog.Label(code);
                    string wrap = Markup(id, shown);
                    s = s.Substring(0, start) + wrap + s.Substring(end);
                    at = start + wrap.Length;
                }
            }
            return s;
        }

        static string WrapTerms(string s, (string term, string linkId)[] terms, bool iataBoundary = false)
        {
            if (string.IsNullOrEmpty(s) || terms == null) return s ?? "";
            for (int i = 0; i < terms.Length; i++)
            {
                string term = terms[i].term;
                string id = terms[i].linkId;
                if (string.IsNullOrEmpty(term)) continue;
                int at = 0;
                while (at < s.Length)
                {
                    int idx = iataBoundary
                        ? IndexOfIata(s, term, at)
                        : s.IndexOf(term, at, StringComparison.Ordinal);
                    if (idx < 0) break;
                    if (InsideMarkup(s, idx)) { at = idx + term.Length; continue; }
                    string shown = s.Substring(idx, term.Length);
                    string wrap = Markup(id, shown);
                    s = s.Substring(0, idx) + wrap + s.Substring(idx + term.Length);
                    at = idx + wrap.Length;
                }
            }
            return s;
        }

        static bool InsideMarkup(string s, int idx)
        {
            if (idx <= 0 || idx >= s.Length) return false;
            int lt = s.LastIndexOf('<', idx);
            int gt = s.LastIndexOf('>', idx);
            // ★只有一个「像标签」的 '<' 才算标记开始（见 IsTagStart）：裸的小于号不算。
            if (lt > gt && IsTagStart(s, lt)) return true;
            int open = LastIndexOfOrdinal(s, "<link", idx);
            int close = LastIndexOfOrdinal(s, "</link>", idx);
            return open >= 0 && open > close;
        }

        /// <summary>
        /// 这个 `&lt;` 是富文本标签的开头，还是正文里写的**小于号**？
        ///
        /// 2026-10-13 第五次报障（「红框中的飞机注册号没有和 B-2000 一样点击跳转飞机详情页」）：
        /// 旧式只比「最近一个 `&lt;`」与「最近一个 `&gt;`」的先后，而运营部那封「逐机归因」正文里
        /// 写着比较式「减载余量 50 km &lt; 本线 7409 km」——全文是纯文本、一个 `&gt;` 都没有（gt = -1），
        /// 于是**第一个小于号之后的所有位置**都被判成「已在标记里」而不再包链接：
        /// 一份八架飞机的名单，只有排在最前面、位于第一个小于号之前的那一架可点。
        /// 一句小于号废掉其后所有链接，不报错也不告警 —— 与 §17.9「机场对只认一种键」同一类病：
        /// **页面看着完全正常，只是点不动。**
        ///
        /// 判据改为**看形状**：`&lt;` 之后必须是 `/` 或 ASCII 字母（`&lt;color=` / `&lt;/link` / `&lt;u`），
        /// 且后面真有闭合的 `&gt;`。「50 km &lt; 本线」后跟的是空格，当场排除。
        /// </summary>
        static bool IsTagStart(string s, int lt)
        {
            if (lt < 0 || lt + 1 >= s.Length) return false;
            char c = s[lt + 1];
            if (c != '/' && !IsAsciiLetter(c)) return false;
            return s.IndexOf('>', lt) >= 0;
        }

        static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        static int LastIndexOfOrdinal(string s, string needle, int idx)
        {
            if (idx <= 0) return -1;
            int max = Math.Min(idx, s.Length - 1);
            return s.LastIndexOf(needle, max, StringComparison.OrdinalIgnoreCase);
        }

        static int IndexOfIata(string s, string code, int start)
        {
            int len = code.Length;
            for (int i = start; i + len <= s.Length; i++)
            {
                if (i > 0 && char.IsLetterOrDigit(s[i - 1])) continue;
                if (i + len < s.Length && char.IsLetterOrDigit(s[i + len])) continue;
                if (string.Compare(s, i, code, 0, len, StringComparison.OrdinalIgnoreCase) == 0)
                    return i;
            }
            return -1;
        }
    }
}
