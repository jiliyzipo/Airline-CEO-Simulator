using System;
using System.Text;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 非航司机构的来文格式：政府机关按公文（标题 → 正文 → 施行时间 → 特此… → 机关落款），
    /// 通讯社/制造商按消息稿（标题 → 正文 → 电头）。不使用公司内部信笺的称谓与自我介绍。
    /// 全文只出现游戏时间。
    /// </summary>
    public static class OfficialBulletin
    {
        const string Indent = "\u3000\u3000";        // 公文本段缩进（两格全角）
        const int RightColumns = 30;                 // 落款右对齐的版心宽度（显示列）

        public enum Style { Government, News, Corporate, Advisory }

        /// <summary>按发件机构判断体例。</summary>
        public static Style StyleOf(string source)
        {
            string s = source ?? "";
            if (Has(s, "顾问") || Has(s, "研究所") || Has(s, "研究院") || Has(s, "协会")
                || Has(s, "咨询") || Has(s, "评级") || Has(s, "标普") || Has(s, "穆迪")
                || Has(s, "惠誉") || Has(s, "中心")) return Style.Advisory;
            if (Has(s, "社") || Has(s, "通讯社") || Has(s, "报") || Has(s, "周刊")
                || Has(s, "电视台") || Has(s, "网") || Has(s, "杂志")) return Style.News;
            if (Has(s, "公司") || Has(s, "集团") || Has(s, "制造商") || Has(s, "波音")
                || Has(s, "空客") || Has(s, "商飞") || Has(s, "工业")) return Style.Corporate;
            return Style.Government;
        }

        /// <summary>文种：通知 / 公告 / 通报 / 预警通报。</summary>
        public static string DocType(string source, string category)
        {
            string s = source ?? "";
            if (Has(s, "气象") || Has(s, "台")) return "预警通报";
            if (Has(s, "银行") || Has(s, "央行")) return "公告";
            if (Has(s, "局") || Has(s, "署") || Has(s, "部") || Has(s, "委员会") || Has(s, "发改委"))
                return "通知";
            if (Has(category, "监管")) return "公告";
            return "通报";
        }

        /// <summary>收件箱头部角色行用词：通知 / 公告 / 通报 / 预警通报 / 消息 / 提示。</summary>
        public static string RoleLabel(string source, string category)
        {
            switch (StyleOf(source))
            {
                case Style.News: return "消息";
                case Style.Corporate: return "公告";
                case Style.Advisory: return "提示";
                default: return DocType(source, category);
            }
        }

        /// <summary>主旨短语：去掉标题里已出现的机关名，避免「发改委关于发改委…」。</summary>
        public static string TopicOf(string source, string title)
        {
            string t = (title ?? "").Trim();
            if (t.Length == 0) return "有关事项";
            string s = (source ?? "").Trim();
            for (int len = s.Length; len >= 2; len--)
            {
                if (t.Length <= len) continue;
                if (!t.StartsWith(s.Substring(0, len), StringComparison.Ordinal)) continue;
                t = t.Substring(len).TrimStart(' ', '：', ':', '，', ',');
                break;
            }
            return string.IsNullOrEmpty(t) ? "有关事项" : t;
        }

        /// <summary>公文标题取短语：逗号后的补充说明不进标题（「关于A，B的通知」→「关于A的通知」）。</summary>
        public static string ShortTopic(string topic)
        {
            string t = (topic ?? "").Trim();
            int cut = t.IndexOfAny(new[] { '，', ',', '；', ';', '。' });
            if (cut > 0) t = t.Substring(0, cut).Trim();
            return string.IsNullOrEmpty(t) ? "有关事项" : t;
        }

        /// <summary>把事件里的相对措辞还原成时间状语（去掉「起 / 生效」），用于「自…起施行」。</summary>
        public static string TimeAdverbial(string relativeDesc)
        {
            string s = (relativeDesc ?? "").Trim();
            if (s.EndsWith("生效", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 2);
            if (s.EndsWith("起", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 1);
            s = s.Trim();
            return string.IsNullOrEmpty(s) ? "发布之时" : s;
        }

        /// <summary>组装来文正文（不含信笺称谓）。triggered=已到游戏生效时刻。</summary>
        public static string Compose(ExternalEvent ev, string gameDate, bool triggered)
        {
            if (ev == null) return "";
            var style = StyleOf(ev.source);
            string topic = TopicOf(ev.source, ev.title);
            string when = TimeAdverbial(ev.relativeDesc);
            bool now = when == "即刻";
            var sb = new StringBuilder();

            if (style == Style.Government)
            {
                string doc = DocType(ev.source, ev.category);
                sb.Append($"关于{ShortTopic(topic)}的{doc}\n\n");
                AppendBody(sb, LeadText(ev, topic));
                if (triggered) sb.Append($"{Indent}本{doc}已自{gameDate}起施行。\n");
                else if (now) sb.Append($"{Indent}本{doc}自发布之时起施行。\n");
                else sb.Append($"{Indent}本{doc}自{when}起施行，届时将另行通报。\n");
                sb.Append('\n');
                sb.Append($"{Indent}特此{ClosingWord(doc)}。\n");
                sb.Append('\n');
                sb.Append(Right(ev.source)).Append('\n');
                sb.Append(Right(gameDate));
                return sb.ToString();
            }

            if (style == Style.News)
            {
                sb.Append($"【{ev.source}】{topic}\n\n");
                AppendBody(sb, LeadText(ev, topic));
                if (triggered) sb.Append($"{Indent}（{ev.source} {gameDate} 电）本条已生效。\n");
                else if (now) sb.Append($"{Indent}（{ev.source} {gameDate} 电）即日起生效。\n");
                else sb.Append($"{Indent}（{ev.source} {gameDate} 电）据报将于{when}生效。\n");
                return sb.ToString();
            }

            // 制造商/企业：公告体
            if (style == Style.Corporate)
            {
                sb.Append($"{ev.source}关于{topic}的公告\n\n");
                AppendBody(sb, LeadText(ev, topic));
                if (triggered) sb.Append($"{Indent}本公告自{gameDate}起生效。\n");
                else if (now) sb.Append($"{Indent}本公告自发布之日起生效。\n");
                else sb.Append($"{Indent}本公告自{when}起生效。\n");
                sb.Append('\n');
                sb.Append(Right(ev.source)).Append('\n');
                sb.Append(Right(gameDate));
                return sb.ToString();
            }

            // 顾问 / 研究 / 评级机构：提示函体（不冠「公告」）
            sb.Append($"{ev.source}：{topic}\n\n");
            AppendBody(sb, LeadText(ev, topic));
            if (triggered) sb.Append($"{Indent}上述情况自{gameDate}起显现。\n");
            else if (now) sb.Append($"{Indent}上述情况即时显现。\n");
            else sb.Append($"{Indent}上述情况预计于{when}显现。\n");
            sb.Append('\n');
            sb.Append($"{Indent}以上情况，请予关注。\n");
            sb.Append('\n');
            sb.Append(Right(ev.source)).Append('\n');
            sb.Append(Right(gameDate));
            return sb.ToString();
        }

        /// <summary>公文结束语用词：特此通知 / 特此公告 / 特此通报。</summary>
        static string ClosingWord(string docType)
        {
            if (docType == "通知") return "通知";
            if (docType == "公告") return "公告";
            return "通报";
        }

        /// <summary>
        /// 正文段：正文与标题同源（订阅源常只给标题）时不重复排版，交给「电头 / 施行时间」收尾。
        /// </summary>
        static string LeadText(ExternalEvent ev, string topic)
        {
            string c = ev?.content ?? "";
            if (string.IsNullOrEmpty(c)) return "";
            if (SameText(c, topic) || SameText(c, ev?.title)) return "";
            return Paragraphs(c);
        }

        /// <summary>正文段落后留一空行；无正文（订阅源常只给标题）则不留空行。</summary>
        static void AppendBody(StringBuilder sb, string body)
        {
            if (string.IsNullOrEmpty(body)) return;
            sb.Append(body).Append("\n\n");
        }

        static bool SameText(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string x = Condense(a);
            string y = Condense(b);
            return x.Length > 0 && x.Equals(y, StringComparison.Ordinal);
        }

        static string Condense(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (char.IsWhiteSpace(ch) || ch == '\u3000') continue;
                if ("，,。；;：:、！!？?\"'“”‘’（）()【】[]《》—-_·".IndexOf(ch) >= 0) continue;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>正文按段缩进两格（贴合公文排版）。</summary>
        static string Paragraphs(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            var parts = content.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) { sb.Append('\n'); continue; }
                sb.Append(Indent).Append(p).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>右对齐落款（全角空格补齐，避免依赖富文本排版）。</summary>
        static string Right(string text)
        {
            string t = (text ?? "").Trim();
            int w = DisplayWidth(t);
            int pad = RightColumns - w;
            if (pad < 0) pad = 0;
            var sb = new StringBuilder();
            for (int i = 0; i < pad / 2; i++) sb.Append('\u3000');
            if (pad % 2 == 1) sb.Append(' ');
            sb.Append(t);
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

        static bool Has(string s, string token)
            => !string.IsNullOrEmpty(s) && s.IndexOf(token, StringComparison.Ordinal) >= 0;
    }
}
