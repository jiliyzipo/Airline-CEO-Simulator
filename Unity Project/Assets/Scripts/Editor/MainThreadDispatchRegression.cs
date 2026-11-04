#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 主线程派发（R49）与「取数管道不得在线程池上碰引擎 API」的结构回归。
    ///
    /// 报障原貌（2026-11-04 独立复验 REV-20261104061859-ba39e0）：
    /// EditMode 133 项全绿、零跳过，Unity 却在 <c>Cleanup mono</c> 阶段崩溃，进程返回
    /// <c>1073741845</c>，于是整份交付被判失败。崩溃栈指向
    /// <c>ExternalDataFetcher.RequestRefresh</c> 的 <c>Task.Run</c> 续体里调 <c>JsonUtility.FromJson</c>：
    ///
    /// <code>
    /// JsonUtility_CUSTOM_FromJsonInternal → InitializeCoreScriptingClasses
    ///   → GetManagerFromContext(MonoManager) = NULL → Fatal Error → Crash
    /// </code>
    ///
    /// 所以本套回归钉两件事：
    /// <list type="number">
    /// <item><b>派发语义</b>：<c>Post</c> 的委托只在 <c>Drain</c> 时执行；退出中一律丢弃
    ///（退出时留着积压，就会在主线程已拆到一半时执行，正是本次崩溃的同类风险）。</item>
    /// <item><b>结构约束</b>：三个取数管道（ExternalDataFetcher / SigmetFeed / WeatherFeed）的
    /// <c>Task.Run</c> 体内**不得**出现解析与引擎 API —— 只允许下载。这条是唯一能挡住
    ///「以后有人顺手在后台线程里补一行 Parse」的机器判据。</item>
    /// </list>
    /// </summary>
    public static class MainThreadDispatchRegression
    {
        public static bool Run()
        {
            // ① 投递后不执行 —— 只有主线程排空才执行
            int ran = 0;
            int mark = 0;
            MainThreadDispatch.Post(() => { ran++; mark = 1; });
            bool postedDeferred = ran == 0;

            // ② 排空执行，并按 FIFO 顺序
            var order = new List<int>();
            MainThreadDispatch.Post(() => order.Add(1));
            MainThreadDispatch.Post(() => order.Add(2));
            MainThreadDispatch.Post(() => order.Add(3));
            int drained = MainThreadDispatch.Drain();
            bool fifo = order.Count == 3 && order[0] == 1 && order[1] == 2 && order[2] == 3;
            bool drainRan = ran == 1 && mark == 1 && drained >= 4;

            // ③ 单件抛异常不连累后续（一条坏稿不该让整轮取数停摆）
            int after = 0;
            MainThreadDispatch.Post(() => throw new InvalidOperationException("故意失败"));
            MainThreadDispatch.Post(() => after++);
            MainThreadDispatch.Drain();
            bool survivesThrow = after == 1;

            // ④ 退出中丢弃：Post 不接、Drain 清空
            //    退出标记是进程级静态量，测完必须复位；用 try/finally，断言失败也要复位。
            bool dropsOnQuit;
            try
            {
                MainThreadDispatch.MarkQuitting();
                int late = 0;
                MainThreadDispatch.Post(() => late++);
                MainThreadDispatch.Drain();
                dropsOnQuit = late == 0 && MainThreadDispatch.PendingCount == 0;
            }
            finally
            {
                MainThreadDispatch.ResetQuittingForTests();
            }

            // ⑤ 结构约束：Task.Run 体内只许下载
            var scan = ScanFetchPipelines();
            bool structure = scan.ok;

            // ⑥ 退出时掐断在飞下载：五条取数管道都要把 ShutdownToken 交给 HttpClient，
            //    否则退出时线程池上仍挂着 socket 回调 —— 那同样会脏退出码。
            bool quitsAbortNetwork = CheckShutdownTokenWiring(out string tokenWhy);

            bool all = postedDeferred && fifo && drainRan && survivesThrow && dropsOnQuit && structure
                       && quitsAbortNetwork;
            Debug.Log($"R49 主线程派发 → 投递不立即执行={postedDeferred}　排空按序={fifo}"
                      + $"　单件异常不连累={survivesThrow}　退出中丢弃={dropsOnQuit}"
                      + $"　取数管道只在线程池下载={structure}"
                      + (structure ? "" : $"（{scan.why}）")
                      + $"　退出掐断在飞下载={quitsAbortNetwork}"
                      + (quitsAbortNetwork ? "" : $"（{tokenWhy}）")
                      + $"　{(all ? "✓" : "✗")}");
            return all;
        }

        /// <summary>五条取数管道的下载函数都要把 <c>MainThreadDispatch.ShutdownToken</c> 传给 HttpClient。</summary>
        static bool CheckShutdownTokenWiring(out string why)
        {
            string[] files =
            {
                "Assets/Scripts/Logic/ExternalDataFetcher.cs",
                "Assets/Scripts/Logic/SigmetFeed.cs",
                "Assets/Scripts/Logic/WeatherFeed.cs",
                "Assets/Scripts/Logic/TyphoonFeed.cs",
                "Assets/Scripts/Logic/GlobalStormFeed.cs"
            };
            var sb = new StringBuilder();
            for (int i = 0; i < files.Length; i++)
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), files[i]);
                if (!File.Exists(path)) { sb.Append(files[i] + "=缺失 "); continue; }
                if (File.ReadAllText(path).IndexOf("MainThreadDispatch.ShutdownToken", StringComparison.Ordinal) < 0)
                    sb.Append(Path.GetFileName(files[i]) + " 没把退出令牌交给 HttpClient ");
            }
            why = sb.ToString().Trim();
            return why.Length == 0;
        }

        /// <summary>
        /// 扫三个取数管道的源码：每个 <c>Task.Run(</c> 的括号体内不得出现解析或引擎 API。
        /// 允许出现的只有下载调用（LoadTextAsync / DownloadAsync / DownloadTextAsync 之类）。
        /// 扫描前先去掉注释 —— 否则连"这里不能用 JsonUtility"这种说明本身都会命中。
        /// </summary>
        static (bool ok, string why) ScanFetchPipelines()
        {
            string[] files =
            {
                "Assets/Scripts/Logic/ExternalDataFetcher.cs",
                "Assets/Scripts/Logic/SigmetFeed.cs",
                "Assets/Scripts/Logic/WeatherFeed.cs"
            };
            // 线程池上不得出现的符号：JsonUtility 是引擎 API；Application/Resources/GameObject 同理。
            string[] forbidden = { "JsonUtility", "Application.", "Resources.", "GameObject" };

            var why = new StringBuilder();
            for (int f = 0; f < files.Length; f++)
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), files[f]);
                if (!File.Exists(path)) { why.Append(files[f] + "=缺失 "); continue; }
                string problem = ScanText(Path.GetFileName(files[f]), File.ReadAllText(path));
                if (problem.Length > 0) why.Append(problem);
            }

            // 负面控制：判据本身必须能抓到违规样本，否则它就是恒真的摆设。
            string bad = ScanText("样本", "void X() { Task.Run(async () => { var o = JsonUtility.FromJson<A>(t); }); }");
            string good = ScanText("样本", "void X() { Task.Run(async () => { var t = await Get(); MainThreadDispatch.Post(() => Adopt(t)); }); }");
            if (bad.Length == 0) why.Append("判据失灵：带 JsonUtility 的 Task.Run 样本没有被抓到 ");
            if (good.Length > 0) why.Append("判据误报：合法的 Task.Run 样本被判定为违规 ");

            return (why.Length == 0, why.ToString().Trim());
        }

        /// <summary>扫一份源码里的所有 <c>Task.Run(…)</c> 块，返回问题描述；空串表示合格。</summary>
        static string ScanText(string label, string raw)
        {
            // 线程池上不得出现的符号：JsonUtility 是引擎 API；Application/Resources/GameObject 同理。
            string[] forbidden = { "JsonUtility", "Application.", "Resources.", "GameObject" };
            string src = StripComments(raw);
            var why = new StringBuilder();
            int at = -1;
            while ((at = src.IndexOf("Task.Run(", at + 1, StringComparison.Ordinal)) >= 0)
            {
                int bodyStart = src.IndexOf('{', at);
                if (bodyStart < 0) break;
                int bodyEnd = MatchBrace(src, bodyStart);
                if (bodyEnd < 0) { why.Append(label + "=括号不配对 "); break; }
                string body = src.Substring(bodyStart, bodyEnd - bodyStart + 1);
                for (int k = 0; k < forbidden.Length; k++)
                {
                    if (body.IndexOf(forbidden[k], StringComparison.Ordinal) >= 0)
                        why.Append($"{label} 的 Task.Run 体内出现 {forbidden[k]}（会在线程池上碰引擎 API） ");
                }
                if (body.IndexOf("MainThreadDispatch.Post(", StringComparison.Ordinal) < 0)
                    why.Append($"{label} 的 Task.Run 体内没有 MainThreadDispatch.Post（下载结果没人接手） ");
            }
            return why.ToString();
        }

        /// <summary>去掉 // 与 /* */ 注释，保留字符串字面量里的内容（URL 里的斜杠不能被当成注释）。</summary>
        static string StripComments(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    sb.Append(c);
                    i++;
                    while (i < s.Length)
                    {
                        sb.Append(s[i]);
                        if (s[i] == '\\' && i + 1 < s.Length) { sb.Append(s[i + 1]); i += 2; continue; }
                        if (s[i] == quote) break;
                        i++;
                    }
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                    i++;
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        static int MatchBrace(string s, int openAt)
        {
            int depth = 0;
            for (int i = openAt; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
#endif
