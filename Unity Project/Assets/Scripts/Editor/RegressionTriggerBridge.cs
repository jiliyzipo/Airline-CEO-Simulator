#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// AI 触发桥：让自动化在一个**已打开**的编辑器里跑指定的回归，而不必关掉 Unity。
    ///
    /// 为什么要这个 —— <c>tools/regression/RunUnityTests.ps1</c> 有两道硬闸：
    /// 只要有任何 Unity 进程存在就直接 throw，且只认 <c>companyName=AirlineCEORegression</c>
    /// 的隔离导出。所以 Unity 代码改完后，Cursor 侧要么等用户关掉编辑器，要么完全没有自测。
    /// 这座桥走的是第三条路：**在当前工程、当前代码上跑**，只做用户会手动点的那件事。
    ///
    /// 协议（请求方是 <c>tools/regression/ai-run.mjs</c>）：
    /// <list type="number">
    /// <item>把 <c>out/ai-trigger/&lt;id&gt;.json</c>（<c>{"id":..,"suite":..}</c>）放进去；</item>
    /// <item>桥轮询到后执行白名单里的静态入口，把结果写成
    ///   <c>out/ai-result/&lt;id&gt;.json</c>（另有同名 <c>.log</c> 存本次 Console 输出）；</item>
    /// <item>触发文件被移到 <c>out/ai-trigger/done/</c>，不会被重复执行。</item>
    /// <item>另有伪 suite <c>__refresh</c>：只让编辑器做一次 <c>AssetDatabase.Refresh()</c> 并立即回结果。
    ///   Unity 仅在编辑器重获焦点时才自动编译，自动化改完 .cs 后靠它替代"手动点一下窗口"。</item>
    /// </list>
    ///
    /// 安全边界（这些是硬性的，不是建议）：
    /// <list type="bullet">
    /// <item><b>只认白名单</b>。<see cref="Suites"/> 里没有名字一律拒绝——
    ///   桥不能变成"编辑器里执行任意代码"的后门。唯一的额外入口是 <c>__refresh</c>，
    ///   它只调 <c>AssetDatabase.Refresh()</c>，不执行任何被测代码。</item>
    /// <item><b>Play 模式下一律不跑</b>，只回一条 busy 结果。否则回归会在用户游玩途中
    ///   创建/销毁对象，把实时状态搅乱。</item>
    /// <item><b>编译/资源导入中不跑</b>，把触发文件留在原地下一轮再试，
    ///   避免在域重载半途执行。</item>
    /// <item><b>不写 PlayerPrefs、不碰存档槽位</b>。桥本身只调用被调方的静态方法。</item>
    /// <item><b>可用 <c>out/ai-trigger/OFF</c> 文件一键停用</b>，不必改代码或重编译。</item>
    /// </list>
    ///
    /// 已知副作用（如实写明）：回归会在**当前打开的场景**里临时创建 GameObject 并销毁，
    /// 可能把场景标记为已修改。这不损坏任何数据，但若你手上正有未保存的场景改动，
    /// 建议先保存再让自动化跑，免得分不清哪个改动是自己的。
    /// </summary>
    [InitializeOnLoad]
    public static class RegressionTriggerBridge
    {
        const double PollSeconds = 1.0;
        const int MaxLoggedLines = 3000;

        /// <summary>
        /// 伪 suite：只做一次 <c>AssetDatabase.Refresh()</c>，让编辑器把那批还没编译的 .cs 编译掉。
        /// 不是回归入口，也不在 <see cref="Suites"/> 里 —— 它不执行任何被测代码。
        /// </summary>
        internal const string RefreshSuite = "__refresh";

        /// <summary>
        /// 白名单：suite 名 → 静态入口。加一条就是加一行，别放飞。
        ///
        /// 这里的每一项都是 <c>LogicSelfTests</c> 与 EditMode 测试调用的同一个静态入口，
        /// 也就是说桥只做「用户本来会手动点的那件事」，没有新增任何执行能力。
        /// 名字与 <c>Tools/EditMode</c> 里的测试文件一一对应，方便互相对照。
        /// 注意：**不含** <c>LogicSelfTests.Run()</c> —— 那是全量跑，按协作规则不能拿它
        /// 顶替隔离回归，桥也不给它开这个口子。
        /// </summary>
        static readonly Dictionary<string, Func<bool>> Suites =
            new Dictionary<string, Func<bool>>(StringComparer.OrdinalIgnoreCase)
            {
                { "accounting-scenario", AccountingScenarioRegression.Run },
                { "airline-tier", AirlineTierRegression.Run },
                { "airport-catalog", AirportCatalogRegression.Run },
                { "command-log", CommandLogRegression.Run },
                { "inbox-status", InboxStatusRegression.Run },
                { "inbox-row-layout", InboxRowLayoutRegression.Run },
                { "network-pool", NetworkPoolVisibilityRegression.Run },
                { "open-route-study", OpenRouteStudyRegression.Run },
                { "overhaul-maintenance", OverhaulMaintenanceRegression.Run },
                { "press-desk", PressDeskRegression.Run },
                { "quarterly-one-off", QuarterlyOneOffRegression.Run },
                { "runway-debt", RunwayDebtRegression.Run },
                { "scenario-fixture", ScenarioFixtureRegression.Run },
                { "schedule-page", SchedulePageRegression.Run },
                { "short-haul", ShortHaulRegression.Run },
                { "slot-cash-hint", SlotCashHintRegression.Run },
                { "status-bar-reconcile", StatusBarReconcileRegression.Run },
                { "takeover-cost-ledger", TakeoverCostLedgerRegression.Run },
                { "tutorial-copy", TutorialCopyRegression.Run },
                { "write-command", WriteCommandRegression.Run },
            };

        /// <summary>一次跑全部白名单项，逐条记 ✗，用于「改完 Unity 代码先自己过一遍」。</summary>
        static bool RunAll(string[] order)        {
            int passed = 0;
            var failed = new List<string>();
            for (int i = 0; i < order.Length; i++)
            {
                string name = order[i];
                if (!Suites.TryGetValue(name, out var entry)) continue;
                bool one;
                try { one = entry(); }
                catch (Exception e)
                {
                    one = false;
                    Debug.LogError($"[AI 触发桥] {name} 抛异常：{e}");
                }
                if (one) passed++;
                else failed.Add(name);
            }
            Debug.Log($"[AI 触发桥] 全部 {passed + failed.Count} 项：通过 {passed}、失败 {failed.Count}"
                      + (failed.Count > 0 ? $"（{string.Join(", ", failed)}）" : ""));
            return failed.Count == 0;
        }

        static double _nextPoll;
        static bool _running;
        static readonly List<string> Buffer = new List<string>();

        static RegressionTriggerBridge()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        /* ---------------- 路径：从 Assets 反推仓库根，别硬编码盘符 ---------------- */

        static string RepoRoot
        {
            get
            {
                // Application.dataPath = <repo>/Unity Project/Assets
                string project = Path.GetDirectoryName(Application.dataPath);
                return Path.GetDirectoryName(project);
            }
        }

        static string TriggerDir => Path.Combine(RepoRoot, "out", "ai-trigger");
        static string DoneDir => Path.Combine(TriggerDir, "done");
        static string ResultDir => Path.Combine(RepoRoot, "out", "ai-result");
        static string OffFile => Path.Combine(TriggerDir, "OFF");

        public static bool Enabled => !File.Exists(OffFile);
        public static IEnumerable<string> SuiteNames => Suites.Keys;

        /* ---------------- 轮询 ---------------- */

        static void Tick()
        {
            if (_running) return;
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + PollSeconds;

            if (!Enabled) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!Directory.Exists(TriggerDir)) return;

            string[] files;
            try { files = Directory.GetFiles(TriggerDir, "*.json"); }
            catch (Exception) { return; }
            if (files.Length == 0) return;
            Array.Sort(files, StringComparer.Ordinal);

            var request = ReadRequest(files[0]);
            if (request == null)
            {
                // 读不动就别反复撞同一个文件：挪进 done 并留个说明。
                Consume(files[0], "unreadable");
                return;
            }
            Execute(request, files[0]);
        }

        sealed class Request
        {
            public string id;
            public string suite;
            public string requestedAt;
        }

        /// <summary>
        /// 手写解析而非 JsonUtility：请求体只有三个标量字段，JsonUtility 要求
        /// 精确的类形状，字段少一个就静默给出 null，反而更难排错。
        /// </summary>
        static Request ReadRequest(string file)
        {
            string text;
            try { text = File.ReadAllText(file, Encoding.UTF8); }
            catch (Exception) { return null; }
            var r = new Request
            {
                id = Field(text, "id"),
                suite = Field(text, "suite"),
                requestedAt = Field(text, "requestedAt"),
            };
            if (string.IsNullOrEmpty(r.id)) return null;
            if (string.IsNullOrEmpty(r.suite)) return null;
            return r;
        }

        static string Field(string json, string name)
        {
            string needle = "\"" + name + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            int colon = json.IndexOf(':', i + needle.Length);
            if (colon < 0) return null;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        /* ---------------- 执行 ---------------- */

        static void Execute(Request request, string triggerFile)
        {
            var sw = Stopwatch.StartNew();
            bool ok = false;
            string error = null;
            string refused = null;

            const string All = "all";

            // 刷新请求：不是回归，只是一个"请你现在编译一下"的信号。
            // 为什么需要 —— Unity 只在编辑器重获焦点时才自动编译脚本（Auto Refresh 开着也一样）。
            // 自动化改完 .cs 再立刻发请求，跑的其实是上一版程序集，结论看着是绿的却描述旧代码。
            // 这里替用户做掉"点一下窗口"这件事：先落盘再刷新，因为 Refresh 会触发域重载，
            // 之后的代码不保证还有机会执行。
            if (string.Equals(request.suite, RefreshSuite, StringComparison.OrdinalIgnoreCase))
            {
                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    refused = "编辑器处于 Play 模式，不在游玩途中重编译；退出 Play 后重试即可。";
                }
                else
                {
                    // 刷新分支不跑任何被测代码，日志必须是空的。Buffer 不清就会把上一轮的
                    // Console 行写进这次结果，让人以为刷新请求本身失败了（2026-10-28 实测踩到）。
                    Buffer.Clear();
                    WriteResult(request, "refreshed", true, null, 0);
                    Consume(triggerFile, "refreshed");
                    Debug.Log("[AI 触发桥] 已请求 AssetDatabase.Refresh，等待重编译完成");
                    EditorApplication.delayCall += AssetDatabase.Refresh;
                    return;
                }
            }

            if (!Suites.ContainsKey(request.suite) && !string.Equals(request.suite, All, StringComparison.OrdinalIgnoreCase))
                refused = $"suite 不在白名单：{request.suite}（可用：{string.Join(", ", Suites.Keys)}、{All}）";
            else if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                refused = "编辑器处于 Play 模式，桥不在游玩途中执行回归；退出 Play 后重试即可。";

            if (refused == null)
            {
                _running = true;
                Buffer.Clear();
                bool captured = false;
                Application.LogCallback onLog = null;
                try
                {
                    if (Buffer.Capacity < MaxLoggedLines) Buffer.Capacity = MaxLoggedLines;
                    onLog = (condition, stack, type) =>
                    {
                        if (Buffer.Count >= MaxLoggedLines) return;
                        Buffer.Add($"[{type}] {condition}");
                    };
                    captured = true;
                    Application.logMessageReceived += onLog;
                    if (string.Equals(request.suite, All, StringComparison.OrdinalIgnoreCase))
                    {
                        var order = new List<string>(Suites.Keys);
                        ok = RunAll(order.ToArray());
                    }
                    else
                    {
                        ok = Suites[request.suite]();
                    }
                }
                catch (Exception e)
                {
                    error = e.ToString();
                }
                finally
                {
                    if (captured) Application.logMessageReceived -= onLog;
                    _running = false;
                }
            }
            sw.Stop();

            string status = refused != null ? "refused" : (error != null ? "error" : (ok ? "passed" : "failed"));
            WriteResult(request, status, ok, refused ?? error, sw.ElapsedMilliseconds);
            Consume(triggerFile, status);
            if (refused == null)
                Debug.Log($"[AI 触发桥] {request.suite} → {status}（{sw.ElapsedMilliseconds} ms）");
        }

        static void WriteResult(Request request, string status, bool ok, string error, long durationMs)
        {
            try
            {
                Directory.CreateDirectory(ResultDir);
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                string json = BuildJson(request, status, ok, error, durationMs, scene);
                File.WriteAllText(Path.Combine(ResultDir, request.id + ".json"), json, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(ResultDir, request.id + ".log"), string.Join("\n", Buffer), new UTF8Encoding(false));
                // 稳定文件名，方便脚本或人直接看最近一次。
                File.WriteAllText(Path.Combine(ResultDir, "latest.json"), json, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI 触发桥] 结果写盘失败：{e.Message}");
            }
        }

        static string BuildJson(Request request, string status, bool ok, string error, long durationMs,
            UnityEngine.SceneManagement.Scene scene)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"id\": ").Append(Str(request.id)).Append(",\n");
            sb.Append("  \"suite\": ").Append(Str(request.suite)).Append(",\n");
            sb.Append("  \"status\": ").Append(Str(status)).Append(",\n");
            sb.Append("  \"ok\": ").Append(ok ? "true" : "false").Append(",\n");
            sb.Append("  \"error\": ").Append(error == null ? "null" : Str(error)).Append(",\n");
            sb.Append("  \"requestedAt\": ").Append(request.requestedAt == null ? "null" : Str(request.requestedAt)).Append(",\n");
            sb.Append("  \"finishedAt\": ").Append(Str(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))).Append(",\n");
            sb.Append("  \"durationMs\": ").Append(durationMs.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"unityVersion\": ").Append(Str(Application.unityVersion)).Append(",\n");
            sb.Append("  \"projectPath\": ").Append(Str(Path.GetDirectoryName(Application.dataPath) ?? "")).Append(",\n");
            sb.Append("  \"scenePath\": ").Append(Str(scene.path ?? "")).Append(",\n");
            sb.Append("  \"sceneDirty\": ").Append(scene.isDirty ? "true" : "false").Append(",\n");
            sb.Append("  \"logLines\": ").Append(Buffer.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("}\n");
            return sb.ToString();
        }

        static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        static void Consume(string triggerFile, string status)
        {
            try
            {
                Directory.CreateDirectory(DoneDir);
                string name = Path.GetFileNameWithoutExtension(triggerFile)
                              + "." + status + ".json";
                string target = Path.Combine(DoneDir, name);
                if (File.Exists(target)) File.Delete(target);
                File.Move(triggerFile, target);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI 触发桥] 触发文件归档失败：{e.Message}");
            }
        }

        /* ---------------- 菜单：看状态、别猜 ---------------- */

        [MenuItem("AirlineCEO/AI 触发桥/查看状态", false, 120)]
        public static void ShowStatus()
        {
            var sb = new StringBuilder();
            sb.Append("状态：").Append(Enabled ? "已启用" : "已停用（存在 OFF 文件）").Append('\n');
            sb.Append("仓库根：").Append(RepoRoot).Append('\n');
            sb.Append("收件目录：").Append(TriggerDir).Append('\n');
            sb.Append("结果目录：").Append(ResultDir).Append('\n');
            sb.Append("白名单：").Append(string.Join(", ", Suites.Keys)).Append("、all").Append("\n\n");
            sb.Append("请求方式：node tools/regression/ai-run.mjs --suite <name|all>\n");
            sb.Append("（程序集比源文件旧时，脚本会先自动发一次 __refresh 让编辑器重编译）\n");
            sb.Append("停用方式：在收件目录放一个名为 OFF 的空文件。");
            Debug.Log("[AI 触发桥]\n" + sb);
            EditorUtility.DisplayDialog("AI 触发桥", sb.ToString(), "确定");
        }

        [MenuItem("AirlineCEO/AI 触发桥/启用或停用", false, 121)]
        public static void Toggle()
        {
            try
            {
                if (File.Exists(OffFile))
                {
                    File.Delete(OffFile);
                    EditorUtility.DisplayDialog("AI 触发桥", "已启用。", "确定");
                }
                else
                {
                    Directory.CreateDirectory(TriggerDir);
                    File.WriteAllText(OffFile, "delete this file to re-enable the AI trigger bridge\n");
                    EditorUtility.DisplayDialog("AI 触发桥", "已停用（OFF 文件已创建）。", "确定");
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("AI 触发桥", "切换失败：" + e.Message, "确定");
            }
        }
    }
}
#endif
