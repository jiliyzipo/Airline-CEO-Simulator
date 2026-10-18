using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 单个遥测事件（GDB 38）。匿名：仅存事件名/分类/时间戳/键值对，不含任何 PII。
    /// 用并列 key/value 列表而非 Dictionary，保证 UnityEngine.JsonUtility 可序列化。
    /// </summary>
    [Serializable]
    public class TelemetryEvent
    {
        public string name;
        public string category;
        public long stamp;
        public List<string> keys = new List<string>();
        public List<string> vals = new List<string>();
    }

    /// <summary>
    /// 数据分析与遥测（纯 C#，GDB 38）。
    /// 客户端事件缓冲：Track 采集、Snapshot 读取、FlushToFile 落盘（Unity 侧 JsonUtility 序列化）。
    /// 支持退出选项（OptOut），遵循匿名化/透明化原则。
    /// </summary>
    public static class Telemetry
    {
        public const int MaxBuffer = 5000;

        static readonly List<TelemetryEvent> _buffer = new List<TelemetryEvent>();
        static long _stamp;

        /// <summary>玩家在设置中「退出数据采集」时为 true，Track 静默跳过。</summary>
        public static bool OptOut { get; private set; }

        public static event Action OnTracked;

        public static void SetOptOut(bool optOut) => OptOut = optOut;

        public static int Count => _buffer.Count;
        public static long TotalTracked { get; private set; }

        public static IReadOnlyList<TelemetryEvent> Snapshot => _buffer;

        public static void Track(string name, string category, Dictionary<string, string> props = null)
        {
            if (string.IsNullOrEmpty(name) || OptOut) return;

            var e = new TelemetryEvent { name = name, category = category, stamp = ++_stamp };
            if (props != null)
            {
                foreach (var kv in props)
                {
                    e.keys.Add(kv.Key);
                    e.vals.Add(kv.Value ?? "");
                }
            }

            _buffer.Add(e);
            TotalTracked++;
            if (_buffer.Count > MaxBuffer) _buffer.RemoveAt(0);

            OnTracked?.Invoke();
        }

        public static void Clear()
        {
            _buffer.Clear();
            _stamp = 0;
        }
    }
}
