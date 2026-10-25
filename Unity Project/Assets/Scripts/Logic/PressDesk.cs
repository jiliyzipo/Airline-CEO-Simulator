using System.Text;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 新闻官：外部来文的**唯一转呈人**。
    ///
    /// 为什么要有这一层：收件箱原先直接拿「发布单位」当发件人（中央气象台、发改委、路透社…），
    /// 读起来像公司自己订阅了一排机构的邮箱。真实的公司不是这样运转的 —— 外部世界只有一个入口：
    /// 新闻官把各家来文原文整理后转呈 CEO。改动后，发件人固定是新闻官，
    /// 原来那位发布单位退到正文（原文照登，落款仍是它），正文抬头一句交代「谁整理的」。
    ///
    /// 与「顾问」的分工：顾问（<see cref="AirlineCEO.Systems.InboxManager"/> 里的航空顾问 / AO）
    /// 是**公司内部人**，会替 CEO 研判、给建议、写结论；新闻官只有姓名，没有任何资料、不做判断，
    /// 只负责「转呈原文」。所以两者不能合并：把外部原文塞进顾问的信笺，就等于让顾问替局方说话。
    ///
    /// 具名规则（T05.x）：同一存档恒定、不同存档不同名 —— 姓名由开局时写入的身份字段派生，
    /// 不进人事名单（<see cref="Data.PersonnelData"/>），不占编制、不领薪、不可任命、无履历。
    /// 因此不需要存档字段，也不会因为读档而换人。
    /// </summary>
    public static class PressDesk
    {
        /// <summary>对外身份（列表与角色行的前缀词）。</summary>
        public const string Title = "新闻官";

        /// <summary>收件箱头像字。</summary>
        public const string Glyph = "闻";

        /// <summary>身份字段全空时的兜底姓名（只会在裸 <see cref="GameState"/> 的测试里出现）。</summary>
        public const string FallbackName = "周文渊";

        /// <summary>新闻官的姓名。同一存档永远同名；不同开局（公司名 / 二字码 / 基地 / CEO 不同）则不同名。</summary>
        public static string Name(GameState gs)
        {
            // 不用 `gs == null`：UnityEngine.Object 的 == 重载在进程外恒为 true，
            // 离线探针会把每个存档都判成「没有存档」，于是永远测到兜底名。
            if (ReferenceEquals(gs, null)) return FallbackName;
            return NameOf(SaveKey(gs), NationOf(gs));
        }

        /// <summary>纯函数核心（离线探针可直接调用）：同一 key 必得同一姓名。</summary>
        public static string NameOf(string saveKey, string nation)
        {
            string k = (saveKey ?? "").Trim();
            if (k.Length == 0) return FallbackName;
            return PersonnelGenerator.SingleName(nation, StableHash(k));
        }

        /// <summary>这个发件人是不是本存档的新闻官（读档后姓名不变，故可直接比对）。</summary>
        public static bool IsPressOfficer(GameState gs, string sender)
        {
            string s = (sender ?? "").Trim();
            if (s.Length == 0) return false;
            if (s == Title) return true;                       // 旧存档 / 手工信件的兜底写法
            return s == Name(gs);
        }

        /// <summary>转呈件的角色行：「新闻官 · 预警通报」—— 保留原文种，让 CEO 一眼看出这是什么文。</summary>
        public static string RoleLine(string docLabel)
        {
            string l = (docLabel ?? "").Trim();
            return l.Length == 0 ? Title : Title + " · " + l;
        }

        /// <summary>
        /// 正文抬头：交代「这是谁整理的」。原文（含原机关落款）紧随其后，一字不改。
        /// </summary>
        public static string ForwardHead(string source, string officer)
        {
            string from = (source ?? "").Trim();
            if (from.Length == 0) from = "有关方面";
            string who = Title;
            if (!string.IsNullOrEmpty(officer))
            {
                string o = officer.Trim();
                // 中文名连写（新闻官沈其昌），西文名留空格（新闻官 James Smith）
                who += (o.Length > 0 && o[0] < 0x2E80 ? " " : "") + o;
            }
            return $"<color=#7C8FA6>以下为{from}来文原文，由{who}整理转呈。</color>\n\n";
        }

        /// <summary>
        /// 姓名池按公司所在地选：新闻官是总部聘的人，与 CEO 同籍（沿用人事生成器的国别表）。
        /// </summary>
        static string NationOf(GameState gs)
        {
            string n = ReferenceEquals(gs, null) ? "" : (gs.ceoNationality ?? "");
            return n.Trim();
        }

        /// <summary>
        /// 存档标识：只取**开局时一次写入、之后不再变**的身份字段。
        /// 刻意不取 year / quarter / simDay（会随时间变，名字就会随月份换人）。
        /// </summary>
        static string SaveKey(GameState gs)
        {
            var sb = new StringBuilder();
            sb.Append(Clean(gs.companyNameZh)).Append('|');
            sb.Append(Clean(gs.companyNameEn)).Append('|');
            sb.Append(Clean(gs.companyIata)).Append('|');
            sb.Append(Clean(gs.companyIcao)).Append('|');
            sb.Append(Clean(gs.hubIata)).Append('|');
            sb.Append(Clean(gs.ceoDisplayName)).Append('|');
            sb.Append(Clean(gs.ceoNationality)).Append('|');
            sb.Append(gs.ceoBirthYear);
            return sb.ToString();
        }

        static string Clean(string s) => (s ?? "").Trim().ToUpperInvariant();

        /// <summary>FNV-1a：不能用 string.GetHashCode（.NET 各进程随机化，读档就会换名）。</summary>
        static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619u;
                }
                return (int)(h & 0x7FFFFFFF);
            }
        }
    }
}
