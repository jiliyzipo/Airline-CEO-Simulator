using System;
using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.UI.Setup
{
    /// <summary>董事候选人的非 ScriptableObject 扩展画像（年龄/国籍/性格/机构背景），n4 抽卡卡片展示用。</summary>
    public struct BoardProfile
    {
        public int age;
        public string gender;
        public string nationality;
        public string personality;
        public string institution;
    }

    /// <summary>开局表单用静态目录：国籍、资金档、董事主张文案。</summary>
    public static class SetupCatalogs
    {
        public static readonly string[] Genders = { "男", "女" };

        public static readonly string[] Educations = { "民航管理", "金融财务", "工学机械", "法律政治", "其他" };
        public static readonly string[] Careers = { "航司运营", "咨询投行", "军方/民航局", "创业", "其他行业" };
        public static readonly string[] Traits = { "稳健", "扩张", "成本克星", "安全优先", "公关能手" };

        /// <summary>
        /// 启动资金档位（万美元）：
        /// 3,000 万（3000 万美元）/ 7,000 万（7000 万美元）/ 10,000 万（1 亿）/ 15,000 万（1.5 亿）。
        /// 注意：实际到手金额还会乘难度系数（简易 ×1.25 / 标准 ×1 / 困难 ×0.8，见 ScriptableObjects/Setup/Difficulty_*.asset）。
        /// </summary>
        public static readonly float[] CashTiersWan = { 3000f, 7000f, 10000f, 15000f };

        public static readonly string[] CashTierLabels =
        {
            "精简起步 · 3,000 万美元",
            "标准起步 · 7,000 万美元",
            "富裕起步 · 1 亿美元",
            "财团注资 · 1.5 亿美元"
        };

        /// <summary>常见主权国家/地区（中文名，供国籍下拉）。</summary>
        public static readonly string[] Countries =
        {
            "中国", "美国", "英国", "日本", "韩国", "德国", "法国", "意大利", "西班牙", "葡萄牙",
            "荷兰", "比利时", "瑞士", "奥地利", "瑞典", "挪威", "丹麦", "芬兰", "冰岛", "爱尔兰",
            "波兰", "捷克", "匈牙利", "希腊", "土耳其", "俄罗斯", "乌克兰", "罗马尼亚", "保加利亚", "塞尔维亚",
            "加拿大", "墨西哥", "巴西", "阿根廷", "智利", "哥伦比亚", "秘鲁", "委内瑞拉", "古巴", "巴拿马",
            "澳大利亚", "新西兰", "印度", "巴基斯坦", "孟加拉国", "斯里兰卡", "尼泊尔", "印度尼西亚", "马来西亚", "新加坡",
            "泰国", "越南", "菲律宾", "柬埔寨", "老挝", "缅甸", "文莱", "蒙古", "哈萨克斯坦", "乌兹别克斯坦",
            "阿联酋", "沙特阿拉伯", "卡塔尔", "科威特", "巴林", "阿曼", "以色列", "埃及", "南非", "尼日利亚",
            "肯尼亚", "埃塞俄比亚", "摩洛哥", "阿尔及利亚", "突尼斯", "加纳", "塞内加尔", "安哥拉", "坦桑尼亚", "乌干达",
            "中国香港", "中国澳门", "中国台湾", "朝鲜"
        };

        public static int CurrentCalendarYear => DateTime.Now.Year;

        public static int[] BuildBirthYears(int minAge = 22, int maxAge = 75)
        {
            int yNow = CurrentCalendarYear;
            int from = yNow - maxAge;
            int to = yNow - minAge;
            var years = new int[to - from + 1];
            for (int i = 0; i < years.Length; i++)
                years[i] = to - i; // 新→旧，便于默认选中较年轻档
            return years;
        }

        public static int AgeFromBirthYear(int birthYear)
        {
            return Math.Max(18, CurrentCalendarYear - birthYear);
        }

        public static string AgendaFor(BoardMemberData m)
        {
            if (m == null) return "（无主张资料）";
            string tag = m.targetTag ?? "";
            string agenda = tag switch
            {
                "T-求稳长线" => "主张稳健扩张，优先现金流与长期股东回报，反对高杠杆赌局。",
                "T-成本市值" => "主张严控单位成本、提升市值与资本效率，关注票价与辅营收入。",
                "T-新机型" => "主张机队现代化与新机型导入，以降低油耗、提升产品竞争力。",
                "T-碳积分" => "主张绿色转型与碳配额合规，要求披露减排路径。",
                "T-安全守门" => "主张安全一票否决，任何扩张不得牺牲适航与培训投入。",
                "T-工会代表" => "主张保障机组与地服权益，反对过度外包与疲劳排班。",
                "T-资本压力" => "主张加快资产周转与分红，对亏损航线零容忍。",
                "T-服务体验" => "主张提升客舱与地面服务体验，品牌净值优先于短期降本。",
                "T-政策资源" => "主张深耕政府关系与航权时刻，善用政策资源打开市场。",
                "T-股息刚性" => "主张刚性分红与资产负债表纪律，限制大规模资本开支。",
                "T-资产配置" => "主张优化机队与枢纽资产配置，必要时出售非核心资产。",
                "T-货运腹舱" => "主张强化腹舱货运与联运网络，平衡客货收入结构。",
                "T-中转枢纽" => "主张开辟第五/第六航权航线，延长经停时间换取补贴，做大枢纽中转流量。",
                "T-合规审查" => "主张重大采购协议与航权交换需合规审查，反对未完全开放航权的新航线。",
                "T-数据复盘" => "主张每季度复盘亏损航线，座公里收益低于行业平均 15% 即建议停飞。",
                "T-新兴市场" => "主张押注新兴经济体空中走廊，反对过度集中在欧美成熟市场。",
                "T-临空经济" => "主张参与临空经济区投资开发，反对纯过站式运营。",
                _ => string.IsNullOrEmpty(tag) ? "主张尚待披露。" : $"关注议题：{tag}。"
            };
            // 白板：隐藏派系；主张可见
            return $"{m.fullName}（{m.seatType}）\n\n{agenda}";
        }

        // —— n4 抽卡扩展画像（按董事 id，17 人全量，数据源 GDB 第6章董事会角色卡池） ——
        static readonly Dictionary<string, BoardProfile> _profiles = new Dictionary<string, BoardProfile>
        {
            ["A"] = new BoardProfile { age = 47, gender = "男", nationality = "挪威", personality = "保守派老干部", institution = "北域极光主权基金 · 国家资本" },
            ["B"] = new BoardProfile { age = 43, gender = "女", nationality = "新加坡", personality = "Excel狂人", institution = "金狮资本控股集团 · 机构投资" },
            ["C"] = new BoardProfile { age = 53, gender = "男", nationality = "巴西", personality = "技术发烧友", institution = "泛美航空工业集团 · 战略合作" },
            ["D"] = new BoardProfile { age = 39, gender = "女", nationality = "澳大利亚", personality = "理想主义实干派", institution = "蔚蓝星球环境倡议组织 · 独立" },
            ["E"] = new BoardProfile { age = 56, gender = "男", nationality = "俄罗斯", personality = "不怒自威", institution = "国际航空安全联合会 · 独立" },
            ["F"] = new BoardProfile { age = 49, gender = "女", nationality = "巴西", personality = "员工大家长", institution = "全球民航劳工联盟 · 职工" },
            ["G"] = new BoardProfile { age = 61, gender = "男", nationality = "美国", personality = "只看季度财报", institution = "猎鹰全球资产管理 · 机构投资" },
            ["H"] = new BoardProfile { age = 41, gender = "女", nationality = "意大利", personality = "惯坏了的高净值代表", institution = "银月奢华品牌咨询集团 · 独立" },
            ["I"] = new BoardProfile { age = 52, gender = "男", nationality = "印度", personality = "文件不离手", institution = "南亚复兴投资局 · 国家资本" },
            ["J"] = new BoardProfile { age = 40, gender = "男", nationality = "韩国", personality = "极度厌恶风险", institution = "汉江养老信托基金 · 机构投资" },
            ["K"] = new BoardProfile { age = 48, gender = "男", nationality = "德国", personality = "资产精算师", institution = "阿尔卑斯再保险集团 · 机构投资" },
            ["L"] = new BoardProfile { age = 45, gender = "男", nationality = "阿联酋", personality = "货运逻辑脑", institution = "沙丘国际港口与物流集团 · 战略合作" },
            ["M"] = new BoardProfile { age = 57, gender = "男", nationality = "法国", personality = "枢纽拥趸", institution = "塞纳机场管理公司 · 战略合作" },
            ["N"] = new BoardProfile { age = 50, gender = "男", nationality = "美国", personality = "说话引法条", institution = "常春藤航空法律研究中心 · 独立" },
            ["O"] = new BoardProfile { age = 42, gender = "女", nationality = "日本", personality = "不凭感觉只认数据", institution = "旭日航空数据咨询公司 · 独立" },
            ["P"] = new BoardProfile { age = 51, gender = "男", nationality = "南非", personality = "慢条斯理", institution = "金合欢战略控股集团 · 国家资本" },
            ["Q"] = new BoardProfile { age = 44, gender = "女", nationality = "肯尼亚", personality = "戴黑框眼镜画示意图", institution = "赤道国际机场投资集团 · 战略合作" },
        };

        public static BoardProfile ProfileFor(BoardMemberData m)
        {
            if (m == null) return new BoardProfile { age = 50, gender = "—", nationality = "—", personality = "—", institution = "—" };
            if (_profiles.TryGetValue(m.id, out var p)) return p;
            return new BoardProfile { age = 50, gender = "—", nationality = "—", personality = "—", institution = "—" };
        }

        /// <summary>仅个人观点正文，不含姓名、席位、派系。</summary>
        public static string ViewFor(BoardMemberData m)
        {
            if (m == null) return "主张尚待披露。";
            string full = AgendaFor(m);
            int nl = full.IndexOf('\n');
            string body = nl >= 0 ? full.Substring(nl).Trim() : full;
            return string.IsNullOrEmpty(body) ? "主张尚待披露。" : body;
        }

        /// <summary>候选卡片详情：机构 / 性格 / 年龄 / 国籍 / 主张（隐藏派系，白板 n4）。</summary>
        public static string DetailFor(BoardMemberData m)
        {
            if (m == null) return "（无资料）";
            var p = ProfileFor(m);
            string agenda = AgendaFor(m);
            // 去掉 AgendaFor 首行的「姓名（seatType）」，避免与卡片头部重复
            int nl = agenda.IndexOf('\n');
            string agendaBody = nl >= 0 ? agenda.Substring(nl).Trim() : agenda;
            return $"{m.fullName} · {m.seatType}\n" +
                   $"年龄 {p.age} · 国籍 {p.nationality}\n" +
                   $"机构：{p.institution}\n" +
                   $"性格：{p.personality}\n\n" +
                   $"主张：{agendaBody}";
        }

        public static List<BoardMemberData> PickBoardBatch(IReadOnlyList<BoardMemberData> pool, int seed, int count = 9)
        {
            var list = new List<BoardMemberData>();
            if (pool == null || pool.Count == 0) return list;
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null) list.Add(pool[i]);

            var rng = new Random(seed);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }

            if (list.Count > count)
                list.RemoveRange(count, list.Count - count);
            return list;
        }
    }
}
