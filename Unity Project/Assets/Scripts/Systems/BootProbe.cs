using System.Collections.Generic;
using System.Diagnostics;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 开局启动耗时的**分段计时探针**（★2026-11-07）。
    ///
    /// 为什么要它：用户报「点 Play 到进游戏 13 秒」，但这个数字是
    /// 「域重载 + 场景重载 + 脚本 Awake + 第一帧」的总和，光看总时长没法定位。
    /// 这里把 <c>MVPBootstrap.Awake</c> 切成若干段，一行日志打出来，
    /// 谁在几秒上一目了然。**只在编辑器与开发版生效**，发布包是空方法。
    ///
    /// 用法：<c>BootProbe.Reset()</c> 起，阶段之间 <c>BootProbe.Mark("名字")</c>，
    /// 收尾 <c>BootProbe.Flush("MVPBootstrap.Awake")</c>。Mark 记的是**与上一段之间的增量**，
    /// Flush 记总时长。日志前缀固定为 <c>[boot]</c>，方便筛选。
    /// </summary>
    public static class BootProbe
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static readonly Stopwatch _sw = Stopwatch.StartNew();
        static readonly List<string> _steps = new List<string>(16);
        static double _last;

        public static void Reset()
        {
            _steps.Clear();
            _last = 0d;
            _sw.Restart();
        }

        /// <summary>记「距上一段」的毫秒数。</summary>
        public static void Mark(string name)
        {
            double now = _sw.Elapsed.TotalMilliseconds;
            _steps.Add(name + "=" + (now - _last).ToString("0") + "ms");
            _last = now;
        }

        /// <summary>打一行汇总并清空；<paramref name="tag"/> 标明是谁的开局。</summary>
        public static void Flush(string tag)
        {
            double total = _sw.Elapsed.TotalMilliseconds;
            UnityEngine.Debug.Log("[boot] " + tag + " total=" + total.ToString("0") + "ms | "
                + string.Join(" ", _steps));
            _steps.Clear();
            _last = 0d;
        }
#else
        public static void Reset() { }
        public static void Mark(string name) { }
        public static void Flush(string tag) { }
#endif
    }
}
