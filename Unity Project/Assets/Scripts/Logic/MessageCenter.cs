using System.Collections.Generic;
using AirlineCEO.Data;

#nullable disable

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 消息中心（纯 C#，GDB 29.1）——四级优先级 + 智能聚合 + 最近 200 条历史。
    /// 静态存储，Unity 侧由 SaveManager 持久化；tools/balance 可离线测试。
    /// </summary>
    public static class MessageCenter
    {
        public const int MaxHistory = 200;

        /// <summary>同部门同优先级短窗口内的聚合窗口（stamp 差值阈值）。</summary>
        public const long AggregateWindow = 100;

        /// <summary>警告每小时最多 3 条、信息每日最多 20 条（此处以时间戳窗口近似限流）。</summary>
        public const int WarningRateLimit = 3;
        public const int InfoRateLimit = 20;

        static readonly List<GameMessage> _items = new List<GameMessage>();
        static long _stamp;
        static int _nextId = 1;

        public static IReadOnlyList<GameMessage> All => _items;
        public static int Count => _items.Count;
        public static long CurrentStamp => _stamp;

        /// <summary>消息变化事件（顶栏铃铛角标刷新）。</summary>
        public static event System.Action Changed;

        /// <summary>未读计数（用于铃铛角标）。</summary>
        public static int UnreadCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _items.Count; i++)
                    if (!_items[i].read) n++;
                return n;
            }
        }

        /// <summary>
        /// 推入一条消息。同部门同优先级且短窗口内的同类消息自动合并为摘要（防刷屏）。
        /// 超出 200 条时淘汰最旧的一条。
        /// </summary>
        public static void Push(MessagePriority priority, string deptId, string deptName,
            string title, string body, string timeLabel, int year, int quarter)
        {
            var merged = TryAggregate(priority, deptId, deptName, title, body, timeLabel);
            if (merged != null) return;

            if (_items.Count >= MaxHistory)
                _items.RemoveAt(0);

            _items.Add(new GameMessage
            {
                id = _nextId++,
                priority = (int)priority,
                deptId = deptId ?? "",
                deptName = deptName ?? "",
                title = title,
                body = body,
                timeLabel = timeLabel,
                year = year,
                quarter = quarter,
                stamp = ++_stamp,
                read = false
            });
            Changed?.Invoke();
        }

        /// <summary>同部门同优先级、同一标题前缀的短窗口内消息 → 合并为「×N」摘要。</summary>
        static GameMessage TryAggregate(MessagePriority priority, string deptId, string deptName,
            string title, string body, string timeLabel)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var m = _items[i];
                if (m.priority != (int)priority) continue;
                if (m.deptId != (deptId ?? "")) continue;
                if (_stamp - m.stamp > AggregateWindow) break;

                if (m.title != title) continue;

                // 已聚合条目：标题形如 "xxx（×3）"
                int count = 2;
                if (m.title.EndsWith("）") && m.title.Contains("（×"))
                {
                    string num = m.title.Substring(m.title.LastIndexOf("（×") + 2).TrimEnd('）');
                    if (int.TryParse(num, out int c)) count = c + 1;
                }
                m.title = title + $"（×{count}）";
                m.body = body;
                m.timeLabel = timeLabel;
                m.read = false;
                return m;
            }
            return null;
        }

        public static void MarkRead(int id)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == null || _items[i].id != id || _items[i].read) continue;
                _items[i].read = true;
                Changed?.Invoke();
                return;
            }
        }

        public static void MarkAllRead()
        {
            for (int i = 0; i < _items.Count; i++)
                _items[i].read = true;
            Changed?.Invoke();
        }

        /// <summary>按优先级/部门过滤（-1 表示不过滤）。</summary>
        public static List<GameMessage> Filter(int priority, string deptId)
        {
            var result = new List<GameMessage>();
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var m = _items[i];
                if (priority >= 0 && m.priority != priority) continue;
                if (!string.IsNullOrEmpty(deptId) && m.deptId != deptId) continue;
                result.Add(m);
            }
            return result;
        }

        public static void Clear()
        {
            _items.Clear();
            _stamp = 0;
            _nextId = 1;
            Changed?.Invoke();
        }

        public static void Load(List<GameMessage> items)
        {
            _items.Clear();
            if (items == null) return;
            _items.AddRange(items);
            _nextId = 1;
            _stamp = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].id >= _nextId) _nextId = _items[i].id + 1;
                if (_items[i].stamp > _stamp) _stamp = _items[i].stamp;
            }
            Changed?.Invoke();
        }

        public static List<GameMessage> Snapshot() => new List<GameMessage>(_items);
    }
}
