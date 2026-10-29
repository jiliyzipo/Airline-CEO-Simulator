using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>
    /// 覆盖式弹窗的「返回」登记簿：让底栏 &lt; 能关掉不归 NavigationRouter 管的弹窗。
    ///
    /// 底栏 &lt; 是本项目唯一的返回入口（面板自带的「← 返回」/右上角 × 一律隐藏，见
    /// <c>BasePanel.HideCloseXButtons</c>）。它能不能点，由
    /// <c>PrefabNavIntegration.RefreshHistoryButtons</c> 决定：
    /// <c>浏览历史 CanBack || 路由栈顶不是底栏 L1</c>。弹窗不进路由栈，也不写浏览历史，
    /// 于是主页地图上弹出「开通航线」之后 &lt; 仍是禁用态 —— 点它没有任何反应。
    ///
    /// 而开通航线弹窗在「已提交」态下把表单、确认按钮、RPM 入口全部隐藏，页内没有第二个出口，
    /// 蒙层又是全屏 raycast 目标。两件事叠起来的后果不是「返回键不好用」，而是**软锁**：
    /// 提交后除重启没有别的出路。
    ///
    /// 这里只做一件最小的事：登记若干个「能被返回键关掉」的对象，由底栏 &lt; 优先消费。
    /// 顺序按后进先出 —— 上层弹窗先关，关完才轮到下层。
    /// </summary>
    public static class ModalBackStack
    {
        class Entry
        {
            public UnityEngine.Object owner;
            public Func<bool> dismiss;
        }

        static readonly List<Entry> _entries = new List<Entry>();

        /// <summary>栈内容变化（登记 / 注销 / 被返回键消费）。底栏据此刷新 &lt; 的可用态。</summary>
        public static event Action Changed;

        public static int Count
        {
            get { Prune(); return _entries.Count; }
        }

        public static bool HasOpen => Count > 0;

        /// <summary>
        /// 登记一个可被返回键关闭的弹窗。同一个 owner 重复登记只保留最后一次，
        /// 于是重复打开同一个弹窗不会在栈里堆出多层「关闭」。
        /// </summary>
        public static void Push(UnityEngine.Object owner, Func<bool> dismiss)
        {
            if (dismiss == null) return;
            RemoveOwner(owner);
            _entries.Add(new Entry { owner = owner, dismiss = dismiss });
            Changed?.Invoke();
        }

        /// <summary>注销（弹窗自行关闭时调用）。未登记过则无操作。</summary>
        public static void Pop(UnityEngine.Object owner)
        {
            if (RemoveOwner(owner)) Changed?.Invoke();
        }

        /// <summary>
        /// 关掉栈顶弹窗。返回 true 表示这次返回已被弹窗消费，调用方不必再退页面；
        /// 返回 false（栈空）则交还给浏览历史 / 路由栈。
        /// </summary>
        public static bool TryDismissTop()
        {
            Prune();
            if (_entries.Count == 0) return false;
            int top = _entries.Count - 1;
            var entry = _entries[top];
            // 先摘再关：关闭过程里弹窗自己还会调 Pop，重复摘会误删别的条目。
            _entries.RemoveAt(top);
            bool handled = entry.dismiss != null && entry.dismiss();
            Changed?.Invoke();
            return handled;
        }

        /// <summary>清空登记（场景切换 / 测试收尾）。不触发任何弹窗关闭。</summary>
        public static void Clear()
        {
            if (_entries.Count == 0) return;
            _entries.Clear();
            Changed?.Invoke();
        }

        static bool RemoveOwner(UnityEngine.Object owner)
        {
            bool removed = false;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_entries[i].owner, owner)) continue;
                _entries.RemoveAt(i);
                removed = true;
            }
            return removed;
        }

        /// <summary>丢掉 owner 已被销毁的条目 —— 对象直接 Destroy 时不会走到 Pop。</summary>
        static void Prune()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var owner = _entries[i].owner;
                if (!ReferenceEquals(owner, null) && owner == null) _entries.RemoveAt(i);
            }
        }
    }
}
