#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 进入 / 退出 Play 的**耗时分解**探针（★2026-11-07）。
    ///
    /// 用户报「点 Play 到进游戏 13 秒」。这个数字由四段拼起来，光看总时长分不清是
    /// 域重载、场景重载、脚本 Awake，还是首帧渲染：
    /// <list type="number">
    /// <item>请求 → 域重载开始（备份场景、序列化）</item>
    /// <item>域重载（Editor.log 里有 Domain Reload Profiling，本探针不重复算）</item>
    /// <item>场景重载 + 脚本 Awake（到 <c>EnteredPlayMode</c>）</item>
    /// <item>到第一帧渲染（<c>EditorApplication.update</c> 首次回调）</item>
    /// </list>
    ///
    /// 输出前缀固定 <c>[playtiming]</c>，与 <c>[boot]</c>（Awake 分段）、
    /// Editor.log 的 <c>Domain Reload Profiling</c> 对齐着看即可。
    /// 只在编辑器生效，发布包不参与编译。
    /// </summary>
    [InitializeOnLoad]
    static class PlayModeTimingProbe
    {
        static double _requestedAt = -1d;
        static double _enteredAt = -1d;
        static double _exitRequestedAt = -1d;
        static bool _waitingFirstFrame;

        static PlayModeTimingProbe()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            double now = EditorApplication.timeSinceStartup;
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    _requestedAt = now;
                    Debug.Log("[playtiming] 收到进入 Play 请求");
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    _enteredAt = now;
                    _waitingFirstFrame = true;
                    Debug.Log("[playtiming] 已进入 Play：请求后 "
                        + ((now - _requestedAt) * 1000d).ToString("0")
                        + " ms（含域重载 + 场景重载 + 脚本 Awake）");
                    EditorApplication.update -= OnFirstUpdate;
                    EditorApplication.update += OnFirstUpdate;
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    _exitRequestedAt = now;
                    _waitingFirstFrame = false;
                    EditorApplication.update -= OnFirstUpdate;
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    if (_exitRequestedAt > 0d)
                        Debug.Log("[playtiming] 已回到编辑模式：退出请求后 "
                            + ((now - _exitRequestedAt) * 1000d).ToString("0") + " ms");
                    break;
            }
        }

        static void OnFirstUpdate()
        {
            if (!_waitingFirstFrame) return;
            _waitingFirstFrame = false;
            EditorApplication.update -= OnFirstUpdate;
            double now = EditorApplication.timeSinceStartup;
            Debug.Log("[playtiming] 首帧：进入 Play 后 "
                + ((now - _enteredAt) * 1000d).ToString("0")
                + " ms · 请求后合计 "
                + ((now - _requestedAt) * 1000d).ToString("0") + " ms");
        }
    }
}
#endif
