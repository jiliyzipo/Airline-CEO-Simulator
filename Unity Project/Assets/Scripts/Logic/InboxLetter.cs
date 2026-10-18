using System;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 收件箱信笺规范（Phase 18.1）：让部门邮件读起来像一份公函，而不是文字堆砌。
    ///
    /// 统一结构：
    ///   尊敬的{CEO姓名}：                          ← 称谓
    ///   您好！                                      ← 礼貌语
    ///   我是{部门}{职位}{姓名}，现就「{事由}」向您汇报并请示。   ← 自我介绍
    ///   …正文（分节 / 要点）…                       ← 核心内容
    ///   {部门} · {职位} {姓名}                       ← 落款
    ///   {公历日期}
    /// </summary>
    public static class InboxLetter
    {
        struct Person { public string dept; public string post; }

        /// <summary>非部门发件机构的固定署名职务（无姓名，避免与人员名单冲突）。</summary>
        static readonly Person[] OrgRoster =
        {
            new Person { dept = "董事会",  post = "董事会秘书" },
            new Person { dept = "局方",    post = "主任监察员" },
            new Person { dept = "民航局",  post = "主任监察员" },
            new Person { dept = "AO",      post = "战略顾问" },
        };

        public static string CeoName(GameState gs)
        {
            string n = gs != null ? (gs.ceoDisplayName ?? "") : "";
            return n.Trim();
        }

        /// <summary>称谓行：「尊敬的阿斯芬总裁：」；无姓名时退化为「尊敬的CEO：」。</summary>
        public static string Salutation(GameState gs)
        {
            string n = CeoName(gs);
            if (string.IsNullOrEmpty(n) || n.Equals("CEO", StringComparison.OrdinalIgnoreCase))
                return "尊敬的CEO：";
            return $"尊敬的{n}总裁：";
        }

        /// <summary>发件部门名 → 部门代码（OPS/MKT/…）；非部门返回 null。</summary>
        public static string DeptCodeOf(string sender)
        {
            if (string.IsNullOrEmpty(sender)) return null;
            string s = NormalizeSender(sender);
            if (s == "市场部") return "MKT";
            if (s == "财务总监") return "FIN";
            var codes = AirlineCEO.Data.DepartmentCatalog.Codes;
            for (int i = 0; i < codes.Length; i++)
                if (AirlineCEO.Data.DepartmentCatalog.DisplayName(codes[i]) == s) return codes[i];
            return null;
        }

        /// <summary>
        /// 联署发件人归一到单一部门名：「人力资源部 / 人力资源部」→「人力资源部」。
        /// 不然自我介绍会变成「我是人力资源部 / 人力资源部招聘请示」。
        /// </summary>
        public static string NormalizeSender(string sender)
        {
            if (string.IsNullOrEmpty(sender)) return "";
            string s = sender.Trim();
            if (s.IndexOf('/') < 0 && s.IndexOf('／') < 0) return s;
            var parts = s.Split(new[] { '/', '／' }, StringSplitOptions.RemoveEmptyEntries);
            string first = "";
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (string.IsNullOrEmpty(p)) continue;
                if (first.Length == 0) first = p;
                if (IsDeptDisplayName(p)) return p;
            }
            return first.Length > 0 ? first : s;
        }

        static bool IsDeptDisplayName(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s == "市场部") return true;
            var codes = AirlineCEO.Data.DepartmentCatalog.Codes;
            for (int i = 0; i < codes.Length; i++)
                if (AirlineCEO.Data.DepartmentCatalog.DisplayName(codes[i]) == s) return true;
            return false;
        }

        /// <summary>
        /// 推断「部门 / 职位 / 姓名」：部门邮件取「人员 → 管理团队」的现任负责人或副手，
        /// 保证邮件署名与人事名单完全一致；负责人空缺时依次退到副手、在职主管/专员。
        /// </summary>
        public static void Identify(GameState gs, string sender, string role,
            out string dept, out string post, out string name)
        {
            dept = NormalizeSender(sender);
            if (string.IsNullOrEmpty(dept)) dept = "公司";
            post = "";
            name = "";

            string code = DeptCodeOf(dept);
            if (code != null)
            {
                var mgr = DepartmentManager.Instance;
                var head = SafeHead(mgr, code);
                if (head != null && !string.IsNullOrEmpty(head.fullName))
                {
                    post = StaffEstablishment.DisplayTitle(head.jobTitle, "负责人");
                    name = head.fullName;
                    return;
                }

                var deputy = SafeDeputy(mgr, code);
                if (deputy != null && !string.IsNullOrEmpty(deputy.fullName))
                {
                    post = StaffEstablishment.DisplayTitle(deputy.jobTitle, "副手");
                    name = deputy.fullName;
                    return;
                }

                // 负责人与副手都空缺：退到部门在职人员，保证自我介绍仍有职位+姓名
                var staff = StaffEstablishment.ListEmployed(code, gs, mgr);
                for (int r = 0; r < staff.Count; r++)
                {
                    var p = staff[r];
                    if (p == null || string.IsNullOrEmpty(p.fullName)) continue;
                    post = StaffEstablishment.DisplayTitle(p.jobTitle, p.rank);
                    name = p.fullName;
                    return;
                }

                post = StaffEstablishment.HeadTitleFor(code);
                return;
            }

            for (int i = 0; i < OrgRoster.Length; i++)
            {
                if (OrgRoster[i].dept == dept) { post = OrgRoster[i].post; return; }
            }

            if (LooksLikePersonName(role)) { name = role.Trim(); post = "负责人"; return; }
            post = string.IsNullOrEmpty(role) ? "负责人" : role.Trim();
        }

        static PersonnelData SafeHead(DepartmentManager mgr, string code)
        {
            if (mgr == null) return null;
            try { return mgr.GetHead(code); } catch { return null; }
        }

        static PersonnelData SafeDeputy(DepartmentManager mgr, string code)
        {
            if (mgr == null) return null;
            try { return mgr.GetDeputy(code); } catch { return null; }
        }

        /// <summary>角色字段若是人名（而非「准入办理」这类事务名）则采用之。</summary>
        static bool LooksLikePersonName(string role)
        {
            if (string.IsNullOrEmpty(role)) return false;
            string r = role.Trim();
            if (r.Length > 8) return false;
            if (r.IndexOf('·') > 0) return true;              // 外籍姓名：马格努斯·林德格伦
            if (r.Length < 2 || r.Length > 4) return false;   // 中文姓名通常 2–3 字（少数 4 字）
            const string affair = "部处科组单会室办站队班台局司所提请告说明通知计划审议决策预案报表总结汇报意见提案准入办理";
            for (int i = 0; i < r.Length; i++)
                if (affair.IndexOf(r[i]) >= 0) return false;
            return true;
        }

        /// <summary>把「【提案】请决策：开通 PEK → SHA（各部门研究会）」压成事由短语。</summary>
        public static string Topic(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return "本部门工作事项";
            string s = subject.Trim();
            int end = s.IndexOf('】');
            if (s.StartsWith("【", StringComparison.Ordinal) && end > 0 && end + 1 < s.Length)
                s = s.Substring(end + 1).Trim();
            int paren = s.LastIndexOf('（');
            if (paren > 0 && s.EndsWith("）", StringComparison.Ordinal)) s = s.Substring(0, paren).Trim();
            int colon = s.IndexOf('：');
            if (colon >= 0 && colon + 1 < s.Length) s = s.Substring(colon + 1).Trim();
            return string.IsNullOrEmpty(s) ? subject.Trim() : s;
        }

        /// <summary>分节标题（TMP 富文本）。</summary>
        public static string Section(string title) => $"\n<b><color=#8FB6E8>▎{title}</color></b>\n";

        /// <summary>要点行。</summary>
        public static string Bullet(string text) => $"· {text}\n";

        /// <summary>标签对齐行：中文按两格宽补齐（用全角空格），保证冒号/数值对齐。</summary>
        public static string Row(string label, string value, int width = 12)
        {
            int w = DisplayWidth(label);
            int pad = width > w ? width - w : 1;
            var sb = new StringBuilder(label);
            int full = pad / 2;
            for (int i = 0; i < full; i++) sb.Append('\u3000');
            if (pad % 2 == 1) sb.Append(' ');
            sb.Append(value).Append('\n');
            return sb.ToString();
        }

        static int DisplayWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            for (int i = 0; i < s.Length; i++)
                w += s[i] < 0x2E80 ? 1 : 2;
            return w;
        }

        /// <summary>落款：部门 · 职位 姓名 / 日期。</summary>
        public static string Sign(GameState gs, string sender, string role, string dateText = null)
        {
            Identify(gs, sender, role, out string dept, out string post, out string name);
            string title = string.IsNullOrEmpty(name) ? $"{dept} · {post}" : $"{dept} · {post} {name}";
            string date = string.IsNullOrEmpty(dateText) ? DateTime.Now.ToString("yyyy年MM月dd日") : dateText;
            return $"{title}\n{date}";
        }

        /// <summary>组装完整信笺：称谓 + 自我介绍 + 正文 + 落款。</summary>
        /// <param name="needDecision">false 时为通报/回执，不写「请示／作出决策」。</param>
        public static string Frame(GameState gs, string sender, string role, string subject, string core,
            string requestLine = null, string dateText = null, bool needDecision = true)
        {
            Identify(gs, sender, role, out string dept, out string post, out string name);
            // 自我介绍必须是「职位 + 姓名」；姓名缺失时才退化为「部门 + 职位」
            string who;
            if (!string.IsNullOrEmpty(name)) who = dept + post + " " + name;
            else if (!string.IsNullOrEmpty(post) && post.Contains(dept)) who = post;  // 董事会 · 董事会秘书
            else who = dept + post;
            var sb = new StringBuilder();
            sb.Append(Salutation(gs)).Append('\n');
            sb.Append("您好！\n\n");
            sb.Append(needDecision
                ? $"我是{who}，现就「{Topic(subject)}」向您汇报并请示。\n\n"
                : $"我是{who}，现就「{Topic(subject)}」向您汇报。\n\n");
            sb.Append(Flatten(core));
            sb.Append("\n\n");
            if (needDecision)
                sb.Append(string.IsNullOrEmpty(requestLine) ? "请您审阅后作出决策。" : requestLine);
            else
                sb.Append(string.IsNullOrEmpty(requestLine) ? "本封为通报，无需批复。" : requestLine);
            sb.Append("\n\n");
            sb.Append(Sign(gs, sender, role, dateText));
            return sb.ToString();
        }

        /// <summary>正文去首尾空行，避免信笺里出现大片空白。</summary>
        static string Flatten(string core)
        {
            if (string.IsNullOrEmpty(core)) return "";
            return core.Replace("\r\n", "\n").Trim('\n', ' ', '\t');
        }

        /// <summary>
        /// 显示时的兜底装封：老邮件若没有称谓，就地补上称谓与落款。
        /// 快讯 / 媒体 / 告警类不装封（它们不是部门报给 CEO 的公函）。
        /// </summary>
        public static string FrameIfNeeded(GameState gs, InboxMail mail, string body)
        {
            if (mail == null || string.IsNullOrEmpty(body)) return body ?? "";
            if (mail.action == "news" || mail.kind == "media" || mail.kind == "alert") return body;
            string head = body.TrimStart();
            if (head.StartsWith("尊敬的", StringComparison.Ordinal)) return body;
            if (body.IndexOf("此致", StringComparison.Ordinal) >= 0) return body;
            bool decision = mail.status == "pending" || mail.status == "deferred";
            if (mail.action == "permit_progress" || mail.action == "news"
                || mail.action == "finance_progress" || mail.action == "transition_progress")
                decision = false;
            return Frame(gs, mail.sender, mail.role, mail.subject, body, null, InboxTime.DayHeader(mail), decision);
        }
    }
}
