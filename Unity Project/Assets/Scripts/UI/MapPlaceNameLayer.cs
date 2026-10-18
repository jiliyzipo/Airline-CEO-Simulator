using UnityEngine;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 旧地名层（大洲/国家/城市）已停用。
    /// 地图标注改由 MapWidget 机场标记绘制：城市名 + 机场名 + IATA。
    /// 保留空壳以免场景/旧引用报缺类。
    /// </summary>
    public class MapPlaceNameLayer : MonoBehaviour
    {
        public static MapPlaceNameLayer Build(Transform tile) => null;

        public void SetZoom(float userZoom, float viewScale) { }
    }
}
