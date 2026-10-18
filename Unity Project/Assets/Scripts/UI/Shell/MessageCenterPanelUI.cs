using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI.Shell
{
    /// <summary>n51 消息中心（L2）：红黄蓝绿四级优先级 + 历史记录（GDB 29.1）。</summary>
    public class MessageCenterPanelUI : BasePanel
    {
        int _filterPriority = -1; // -1 = 全部
        TextMeshProUGUI _count;
        Transform _listContent;

        public static MessageCenterPanelUI Build()
        {
            var root = UIFactory.Panel(null, "n51_消息中心", UIFactory.Fill, UITheme.Panel);
            var cg = root.AddComponent<CanvasGroup>();
            var title = UIFactory.Label(root.transform, "Title", "🔔 消息中心", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.92f), new Vector2(0.6f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(root.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.92f, 0.92f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);

            var count = UIFactory.Label(root.transform, "Count", "", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.84f), new Vector2(0.6f, 0.90f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            var listHost = UIFactory.Panel(root.transform, "ListHost",
                new UIFactory.Anchor(new Vector2(0.03f, 0.10f), new Vector2(0.97f, 0.82f), Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.08f, 0.14f, 0.6f), rounded: false);
            var content = UiTable.CreateHost(listHost.transform, "List",
                new UIFactory.Anchor(new Vector2(0.01f, 0.01f), new Vector2(0.99f, 0.99f), Vector2.zero, Vector2.zero));

            var back = UIFactory.Button(root.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.03f, 0.02f), new Vector2(0.16f, 0.08f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var panel = root.AddComponent<MessageCenterPanelUI>();
            panel._count = count;
            panel._listContent = content;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n51_消息中心", "消息中心");

            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(panel.BackOne);

            // 过滤按钮：全部 / 紧急 / 警告 / 信息 / 成功
            BuildFilterButton(root.transform, panel, "全部", -1, 0.62f);
            BuildFilterButton(root.transform, panel, "🔴 紧急", (int)MessagePriority.Emergency, 0.68f);
            BuildFilterButton(root.transform, panel, "🟡 警告", (int)MessagePriority.Warning, 0.74f);
            BuildFilterButton(root.transform, panel, "🔵 信息", (int)MessagePriority.Info, 0.80f);
            BuildFilterButton(root.transform, panel, "🟢 成功", (int)MessagePriority.Success, 0.86f);

            UIFactory.Button(root.transform, "ReadAll", "全部已读",
                new UIFactory.Anchor(new Vector2(0.83f, 0.84f), new Vector2(0.97f, 0.90f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { MessageCenter.MarkAllRead(); panel.Rebuild(); }, 12);

            panel.Rebuild();
            return panel;
        }

        static void BuildFilterButton(Transform root, MessageCenterPanelUI panel, string label, int priority, float xMax)
        {
            var btn = UIFactory.Button(root, "Filter_" + label, label,
                new UIFactory.Anchor(new Vector2(xMax, 0.84f), new Vector2(xMax + 0.055f, 0.90f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { panel._filterPriority = priority; panel.Rebuild(); }, 11);
            // 颜色略做区分，避免全灰
            var img = btn.GetComponent<Image>();
            if (img != null && priority == (int)MessagePriority.Emergency) img.color = new Color(0.45f, 0.15f, 0.18f, 1f);
            if (img != null && priority == (int)MessagePriority.Success) img.color = new Color(0.12f, 0.35f, 0.2f, 1f);
        }

        public void Rebuild()
        {
            if (_listContent == null) return;
            UiTable.Clear(_listContent);

            var items = MessageCenter.Filter(_filterPriority, null);
            if (_count != null)
                _count.text = $"共 {items.Count} 条 · 未读 {MessageCenter.UnreadCount} 条";

            if (items.Count == 0)
            {
                UiTable.AddNote(_listContent, "暂无消息。季度结算、目标达成、安全事件等会在此汇总。");
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var m = items[i];
                UiTable.AddNote(_listContent,
                    $"{PriorityIcon(m.priority)} <b>{m.title}</b>　{m.timeLabel}" +
                    (string.IsNullOrEmpty(m.deptName) ? "" : $" · {m.deptName}") +
                    "\n" + m.body);
            }
        }

        public static string PriorityIcon(int priority) => priority switch
        {
            (int)MessagePriority.Emergency => "🔴",
            (int)MessagePriority.Warning => "🟡",
            (int)MessagePriority.Info => "🔵",
            (int)MessagePriority.Success => "🟢",
            _ => "·"
        };

        public static Color PriorityColor(int priority) => priority switch
        {
            (int)MessagePriority.Emergency => UITheme.Danger,
            (int)MessagePriority.Warning => UITheme.Warning,
            (int)MessagePriority.Info => UITheme.Accent,
            (int)MessagePriority.Success => UITheme.Success,
            _ => UITheme.TextMuted
        };
    }
}
