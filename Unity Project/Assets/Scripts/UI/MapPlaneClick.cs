using AirlineCEO.Logic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 地图上某架飞机的点击受体。挂在 <c>PlaneMark</c> 上，由 <see cref="MapWidget"/> 在每次重建时绑定数据。
    ///
    /// 为什么挂在父节点而不是图标本身：图标的 <see cref="UnityEngine.UI.Image"/> 只负责被射线打中，
    /// 事件会沿层级向上冒泡，所以处理器可以放在 PlaneMark 上；这样一来「光圈 + 描边 + 图标」
    /// 三层任意一层被点中都算点中这架飞机，不用给每层各挂一个组件。
    /// </summary>
    public class MapPlaneClick : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>这条航线在 <c>GameState.activeRoutes</c> 里的 routeId。</summary>
        public string routeId;
        /// <summary>该航线上的第几班（flights 下标）。</summary>
        public int flightIndex = -1;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            if (string.IsNullOrEmpty(routeId)) return;
            MapSelection.Select(routeId, flightIndex);
            if (eventData != null) eventData.Use();   // 别让这次点击再落到地图拖拽层上
        }
    }
}
