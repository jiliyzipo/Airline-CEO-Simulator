#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 「点一次 Play 到底要多久」的**可复现测量器**（★2026-11-07）。
    ///
    /// 用户报「点 Play 到进游戏 13 秒」，但这个数字包含域重载、场景重载、
    /// 脚本 Awake、首帧四段，靠肉眼点一次只能得到一个总数，改完也没法比较。
    /// 这里用一次批处理把同一件事重放 N 遍，每遍输出两行：
    /// <list type="number">
    /// <item><c>[playboot] 第N次 进入 Play = X ms</c> —— 请求 → 场景装载完（含域重载 + Awake）</item>
    /// <item><c>[playboot] 第N次 跑满 30 帧 = Y ms</c> —— 含首帧渲染与 SetupFlow 首屏</item>
    /// </list>
    /// 配合 <c>Editor.log</c> 的 <c>Domain Reload Profiling</c> 与运行时 <c>[boot]</c>（Awake 分段）
    /// 就能把总时长拆到每一段。
    ///
    /// 为什么支持「连跑两遍」（<see cref="RunTwice"/>）：这是**禁用域重载**的唯一有效验证方式。
    /// 第一遍干净，第二遍会在「静态字段没复位」时才暴露问题 —— 而域重载开着时，
    /// 这个问题在编辑器里根本复现不出来（进 Play 会重载脚本域，静态全被重置）。
    /// 所以每次跑完还会统计本轮期间的 Error / Exception 条数，第二遍结尾一句
    /// <c>[playboot] 稳定性：第2次 收到 N 条错误</c> 就是判据。
    ///
    /// 安全边界：**只在批处理中运行**（<see cref="Application.isBatchMode"/>），
    /// 人开着编辑器时调用会直接返回，绝不会在玩家游玩途中重开一局。
    /// 状态经 <see cref="SessionState"/> 跨越域重载（播放模式入场会重载脚本域）。
    /// </summary>
    [InitializeOnLoad]
    public static class PlayBootTimingRunner
    {
        const string PendingKey = "AirlineCEO.PlayBootTiming.Pending";
        const string StartKey = "AirlineCEO.PlayBootTiming.StartUtc";
        const string RoundKey = "AirlineCEO.PlayBootTiming.Round";
        const string RoundsKey = "AirlineCEO.PlayBootTiming.Rounds";
        const string WaitingEditKey = "AirlineCEO.PlayBootTiming.WaitingEdit";
        const int WarmupFrames = 30;

        static int _frames;
        static bool _enteredLogged;
        static readonly List<string> _errors = new List<string>();

        static PlayBootTimingRunner()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        /// <summary>入口：跑一遍。<c>-executeMethod …PlayBootTimingRunner.Run</c></summary>
        public static void Run() => Start(1);

        /// <summary>入口：连跑两遍（验证「禁用域重载」是否把状态带脏）。</summary>
        public static void RunTwice() => Start(2);

        static void Start(int rounds)
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("[playboot] 只在批处理中运行；人工开着编辑器时不会重开 Play。");
                return;
            }

            var scene = EditorBuildSettings.scenes.Length > 0 ? EditorBuildSettings.scenes[0] : null;
            if (scene == null || !scene.enabled)
            {
                Debug.LogError("[playboot] 构建列表里没有启用的场景");
                EditorApplication.Exit(2);
                return;
            }

            EditorSceneManager.OpenScene(scene.path);
            SessionState.SetInt(RoundsKey, rounds);
            SessionState.SetInt(RoundKey, 1);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(WaitingEditKey, false);
            Debug.Log("[playboot] 请求进入 Play（共 " + rounds + " 次），场景 " + scene.path);
            BeginRound();
        }

        static void BeginRound()
        {
            _frames = 0;
            _enteredLogged = false;
            _errors.Clear();
            SessionState.SetString(StartKey, DateTime.UtcNow.ToString("O"));
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (change != PlayModeStateChange.EnteredEditMode) return;
            if (!SessionState.GetBool(WaitingEditKey, false)) return;

            SessionState.SetBool(WaitingEditKey, false);
            int round = SessionState.GetInt(RoundKey, 1);
            int rounds = SessionState.GetInt(RoundsKey, 1);
            if (round >= rounds)
            {
                SessionState.SetBool(PendingKey, false);
                EditorApplication.Exit(0);
                return;
            }

            SessionState.SetInt(RoundKey, round + 1);
            Debug.Log("[playboot] 第 " + round + " 次已退出，准备第 " + (round + 1) + " 次");
            BeginRound();
        }

        static void Tick()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;

            int round = SessionState.GetInt(RoundKey, 1);
            if (!_enteredLogged)
            {
                _enteredLogged = true;
                Debug.Log("[playboot] 第 " + round + " 次 进入 Play = " + ElapsedMs().ToString("0") + " ms");
                LogSceneShape(round);
            }

            // 等画面真的跑起来几帧（Awake 之后还有 Start、SetupFlow 的首屏、TMP 首次排版）。
            if (++_frames < WarmupFrames) return;

            Debug.Log("[playboot] 第 " + round + " 次 跑满 " + WarmupFrames + " 帧 = "
                + ElapsedMs().ToString("0") + " ms · 本轮错误 " + _errors.Count + " 条");
            if (_errors.Count > 0)
            {
                int show = Math.Min(3, _errors.Count);
                for (int i = 0; i < show; i++)
                    Debug.LogError("[playboot] 第 " + round + " 次 错误#" + (i + 1) + "："
                        + (_errors[i].Length > 300 ? _errors[i].Substring(0, 300) : _errors[i]));
            }

            int rounds = SessionState.GetInt(RoundsKey, 1);
            if (round >= rounds)
            {
                SessionState.SetBool(PendingKey, false);
                EditorApplication.Exit(0);
                return;
            }

            SessionState.SetBool(WaitingEditKey, true);
            EditorApplication.ExitPlaymode();
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (!SessionState.GetBool(PendingKey, false)) return;
            _errors.Add(message);
        }

        /// <summary>
        /// 每局开头把「会累积的静态量」和场景规模打出来。
        /// 第 1 次与第 2 次对不上（对象变多、消息不为 0、弹窗栈不为 0），
        /// 就是静态状态没清干净 —— 这正是关闭域重载最容易埋的坑。
        /// </summary>
        static void LogSceneShape(int round)
        {
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            var eventSystems = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            var texts = UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Debug.Log("[playboot] 第 " + round + " 次 场景形状 Canvas=" + canvases
                + " EventSystem=" + eventSystems
                + " TMP=" + texts
                + " 消息=" + AirlineCEO.Logic.MessageCenter.Count
                + " 弹窗栈=" + AirlineCEO.UI.Navigation.ModalBackStack.Count
                + " 倍速=" + AirlineCEO.Systems.GameSpeed.Level);
            LogIconHealth(round);
        }

        /// <summary>
        /// 图标健康度（★2026-11-07 二轮 Play 报障「顾问气泡 / 航线 / 设置 / 倍速的符号变成纯色方块」）。
        ///
        /// 那几个图标是运行时 <c>Sprite.Create</c> 出来的，静态字段缓存；关闭域重载后静态字段跨局存活，
        /// 而退出 Play 时 Unity 销毁这些运行时对象 —— 留下的引用是「假 null」。缓存判定一旦用 C# null
        /// 语义（<c>??=</c> / <c>is null</c>），第二局就会把已销毁的精灵交给 Image，画成实心块。
        /// 这里按 Unity 判空逐个点名，并统计场景里「Image 有 sprite 字段但精灵已销毁」的个数，
        /// 让这种跨局退化在批处理日志里直接暴露，而不是等人去看画面。
        /// </summary>
        static void LogIconHealth(int round)
        {
            string State(UnityEngine.Sprite s) => s == null ? "已销毁/空" : "正常";

            var gear = AirlineCEO.UI.UIFactory.GearSprite;
            var chat = AirlineCEO.UI.UIFactory.ChatSprite;
            var route = AirlineCEO.UI.UIFactory.RouteSprite;
            var ff1 = AirlineCEO.UI.UIFactory.FastForwardSprite(1);

            int dead = 0;
            string deadPaths = "";
            var images = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Image>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var img in images)
            {
                var sp = img.sprite;
                if ((object)sp != null && sp == null) // 引用在、对象已销毁
                {
                    dead++;
                    if (dead <= 3)
                    {
                        string path = img.transform.name;
                        for (var t = img.transform.parent; t != null; t = t.parent)
                            path = t.name + "/" + path;
                        deadPaths += (deadPaths.Length > 0 ? "；" : "") + path;
                    }
                }
            }

            Debug.Log("[playboot] 第 " + round + " 次 图标 齿轮=" + State(gear)
                + " 顾问气泡=" + State(chat)
                + " 航线=" + State(route)
                + " 倍速=" + State(ff1)
                + " 场景内已销毁精灵引用=" + dead + " 个"
                + (deadPaths.Length > 0 ? "（" + deadPaths + "）" : ""));
        }

        /// <summary>从「请求进入 Play」到此刻的毫秒数；起点存在 SessionState 里，能跨域重载。</summary>
        static double ElapsedMs()
        {
            string raw = SessionState.GetString(StartKey, "");
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var when)) return 0d;
            return (DateTime.UtcNow - when).TotalMilliseconds;
        }
    }
}
#endif
