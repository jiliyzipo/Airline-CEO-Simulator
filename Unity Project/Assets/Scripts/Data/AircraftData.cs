using UnityEngine;

namespace AirlineCEO.Data
{
    [CreateAssetMenu(fileName = "AircraftData", menuName = "AirlineCEO/Data/Aircraft Data", order = 1)]
    public class AircraftData : ScriptableObject
    {
        public string aircraftID;      // 如 "A320neo"
        public int maxPassengers;      // 载客量
        public float maxRange;         // 最大航程（公里）
        public float basePrice;        // 采购价格（万美元）
        public float fuelBurnPerHour;  // 每小时油耗（吨）
    }
}
