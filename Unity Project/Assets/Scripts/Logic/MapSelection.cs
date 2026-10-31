using System;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 地图上「选中的那一架飞机」。**不依赖 UnityEngine**，因此可以在离线工具里断言状态机。
    ///
    /// ★2026-10-30 CEO 要求：「点击某个飞机图标后，图标变成选中状态和显示它的航线，旁边弹窗显示该航班的信息」。
    ///
    /// 为什么单独抽出来：地图每刷新一次（时钟推进、缩放、换图层）都会把飞机节点整个重建，
    /// 选中状态如果挂在 GameObject 上，一刷新就丢 —— 玩家点了飞机、画面一跳就取消选中，
    /// 看起来像「点了没反应」。状态必须在重建之外存活，且只有这一个出处。
    /// </summary>
    public static class MapSelection
    {
        /// <summary>选中航线；空 = 没有选中。</summary>
        public static string RouteId { get; private set; }

        /// <summary>该航线上的第几班（<see cref="Data.RouteData.flights"/> 下标）；−1 = 未指定。</summary>
        public static int FlightIndex { get; private set; } = -1;

        public static bool Has => !string.IsNullOrEmpty(RouteId);

        /// <summary>
        /// 选中变化（含取消）。订阅方：地图（重绘高亮）与主页（开关信息卡）。
        /// 是「变化」而不是「点击」——重复点同一架不该触发重绘，否则每点一下整张图重建一次，看着像闪屏。
        /// </summary>
        public static event Action Changed;

        public static void Select(string routeId, int flightIndex)
        {
            if (string.IsNullOrEmpty(routeId)) { Clear(); return; }
            if (Has && RouteId == routeId && FlightIndex == flightIndex) return;
            RouteId = routeId;
            FlightIndex = flightIndex;
            Changed?.Invoke();
        }

        public static void Clear()
        {
            if (!Has && FlightIndex == -1) return;
            RouteId = null;
            FlightIndex = -1;
            Changed?.Invoke();
        }

        /// <summary>某条航线是否是当前选中的（给绘制层判高亮用）。</summary>
        public static bool IsSelected(string routeId)
            => Has && !string.IsNullOrEmpty(routeId) && string.Equals(RouteId, routeId, StringComparison.OrdinalIgnoreCase);

        /// <summary>读档/回主页时复位。**不复位的话**：上一个存档选中的 routeId 在新存档里不存在，
        /// 地图会画一条不存在的高亮，信息卡则显示空数据。</summary>
        public static void Reset()
        {
            RouteId = null;
            FlightIndex = -1;
        }
    }
}
