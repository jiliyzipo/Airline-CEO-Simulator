using System;
using System.Collections.Generic;
using System.Text;

namespace AirlineCEO.Logic
{
    /// <summary>流程节点的办理状态。</summary>
    public enum FlowState
    {
        /// <summary>已办完。</summary>
        Done,
        /// <summary>正在办（当前节点）。</summary>
        Active,
        /// <summary>还没轮到。</summary>
        Pending,
        /// <summary>办不下来（被驳、未获批）。</summary>
        Failed,
        /// <summary>本事项不需要这一步（如非协调机场无需时刻）。</summary>
        Skipped
    }

    /// <summary>流程图上的一个节点。</summary>
    public struct FlowNode
    {
        /// <summary>节点名。</summary>
        public string name;
        public FlowState state;
        /// <summary>时点短语：「11月08日」「预计 11月20日」「还剩 6 天」；空串表示不写。</summary>
        public string when;

        public FlowNode(string name, FlowState state, string when = "")
        {
            this.name = name ?? "";
            this.state = state;
            this.when = when ?? "";
        }
    }

    /// <summary>
    /// 事项办理的**流程节点图**（2026-11-02 CEO 要求）。
    ///
    /// 起因：进度类邮件只写「已办到第 3 关」，CEO 看不出这条事项一共几关、还差几关、
    /// 哪一关在谁手上。旧正文里那串「1. 受理 → 2. 审查 → …」是**静态目录**，
    /// 不带状态也不带日期，读完仍不知道现在在哪。本类把它做成一张带状态的链图：
    ///
    /// <code>
    /// ① 受理立案      ✓ 已完成 · 11月03日
    ///  │
    /// ② 材料与运力审查 ✓ 已完成 · 11月08日
    ///  │
    /// ③ 航权与时刻核验 ▶ 进行中 · 11月14日（还剩 6 天）
    ///  │
    /// ④ 局方公示      ○ 待办 · 预计 11月20日
    /// </code>
    ///
    /// 为什么用竖排而不是横排：收件箱阅读区只有二十来个汉字宽，横排六节点必然折行，
    /// 折行后「→」落在行首反而看不出走向；竖排每节点独占一行，状态标记永远与节点名同行。
    ///
    /// 字形只取 <c>NotoSansSC-Regular.otf</c> 实有的字符（①、✓、▶、○、✗、│、—），
    /// 不写 emoji —— 缺字会渲染成方框，比不画更糟。
    /// </summary>
    public static class ProcessFlow
    {
        /// <summary>节点名所占显示宽度（半角为单位）：最长节点名「航权额度与时刻核验」= 18。</summary>
        const int NameColumn = 18;

        public static string Mark(FlowState state) => state switch
        {
            FlowState.Done => "✓",
            FlowState.Active => "▶",
            FlowState.Failed => "✗",
            FlowState.Skipped => "—",
            _ => "○"
        };

        public static string StateText(FlowState state) => state switch
        {
            FlowState.Done => "已完成",
            FlowState.Active => "进行中",
            FlowState.Failed => "未办成",
            FlowState.Skipped => "无需办理",
            _ => "待办"
        };

        /// <summary>渲染整张图。标题为分节标题，图上方另给一行「共几关、已办几关」的统计。</summary>
        public static string Render(string title, IList<FlowNode> nodes)
        {
            if (nodes == null || nodes.Count == 0) return "";

            int done = 0, active = 0, pending = 0, failed = 0, skipped = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                switch (nodes[i].state)
                {
                    case FlowState.Done: done++; break;
                    case FlowState.Active: active++; break;
                    case FlowState.Failed: failed++; break;
                    case FlowState.Skipped: skipped++; break;
                    default: pending++; break;
                }
            }

            var sb = new StringBuilder();
            sb.Append(InboxLetter.Section(string.IsNullOrEmpty(title) ? "办理流程" : title));
            sb.Append(InboxLetter.Bullet(CountLine(nodes.Count, done, active, pending, failed, skipped)));

            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                string tail = StateText(n.state);
                if (!string.IsNullOrEmpty(n.when)) tail += " · " + n.when;
                sb.Append(' ').Append(IndexLabel(i)).Append(' ')
                  .Append(Pad(n.name, NameColumn)).Append(' ')
                  .Append(Mark(n.state)).Append(' ').Append(tail).Append('\n');
                // 末节点不再画连接线：链尾挂一条竖线会读成「下面还有」。
                if (i < nodes.Count - 1) sb.Append(" │\n");
            }
            return sb.ToString();
        }

        /// <summary>「共 6 关：已完成 2 · 进行中 1 · 待办 3」。</summary>
        static string CountLine(int total, int done, int active, int pending, int failed, int skipped)
        {
            var sb = new StringBuilder();
            sb.Append($"共 {total} 个节点：已完成 {done}");
            if (active > 0) sb.Append($" · 进行中 {active}");
            if (pending > 0) sb.Append($" · 待办 {pending}");
            if (failed > 0) sb.Append($" · 未办成 {failed}");
            if (skipped > 0) sb.Append($" · 无需办理 {skipped}");
            return sb.ToString();
        }

        /// <summary>“①”…“⑳”；超过 20 个节点退回「21)」这种写法（本作流程最长 9 关）。</summary>
        static string IndexLabel(int i)
        {
            if (i >= 0 && i < 20) return ((char)(0x2460 + i)).ToString();
            return (i + 1) + ")";
        }

        /// <summary>中文按两格宽补齐，保证各节点的状态标记落在同一列。</summary>
        static string Pad(string s, int width)
        {
            int w = Width(s ?? "");
            if (w >= width) return s ?? "";
            int padCount = width - w;
            var sb = new StringBuilder(s);
            for (int i = 0; i < padCount / 2; i++) sb.Append('\u3000');
            if (padCount % 2 == 1) sb.Append(' ');
            return sb.ToString();
        }

        static int Width(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            for (int i = 0; i < s.Length; i++) w += s[i] < 0x2E80 ? 1 : 2;
            return w;
        }
    }
}
