using UnityEngine;

namespace AirlineCEO.Data
{
    [CreateAssetMenu(fileName = "BoardMemberData", menuName = "AirlineCEO/Data/Board Member Data", order = 3)]
    public class BoardMemberData : ScriptableObject
    {
        public string id;              // "A", "B"...
        public string fullName;
        public string faction;         // "国家资本", "机构投资", "独立"...
        public string seatType;        // "执行董事", "非执行董事"...
        public float satisfaction;     // 初始满意度 (50)
        public string targetTag;       // 关联的目标标签（如 "T-01"）
    }
}
