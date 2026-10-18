using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Data
{
    /// <summary>消息优先级（GDB 29.1：红/黄/蓝/绿四级）。</summary>
    public enum MessagePriority
    {
        Emergency = 0,  // 🔴 紧急：AOG/现金<2月/重大事故/大规模罢工
        Warning = 1,    // 🟡 警告：油价骤升/工会满意度降/竞对扩张
        Info = 2,       // 🔵 信息：季度报告/新航线/董事满意度变化
        Success = 3     // 🟢 成功：目标达成/融资获批/新机交付
    }

    /// <summary>消息中心单条消息（GDB 29.1，保留最近 200 条）。</summary>
    [Serializable]
    public class GameMessage
    {
        public int id;              // 自增 id（聚合与去重用）
        public int priority;        // MessagePriority
        public string deptId;       // 关联部门 id（如 "n15_财务部"），可为空
        public string deptName;     // 部门名
        public string title;        // 标题
        public string body;         // 正文
        public string timeLabel;    // 时间标签（"第 1 年 Q1"）
        public int year;
        public int quarter;
        public long stamp;          // 单调递增时间戳（聚合窗口用）
        public bool read;
    }

    /// <summary>消息中心存档包装（JsonUtility 序列化）。</summary>
    [Serializable]
    public class MessageSave
    {
        public List<GameMessage> items = new List<GameMessage>();
    }
}
