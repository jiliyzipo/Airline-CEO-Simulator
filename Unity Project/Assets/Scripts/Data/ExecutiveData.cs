using UnityEngine;

namespace AirlineCEO.Data
{
    [CreateAssetMenu(fileName = "ExecutiveData", menuName = "AirlineCEO/Data/Executive Data", order = 2)]
    public class ExecutiveData : ScriptableObject
    {
        public string id;              // "OPS-01"
        public string fullName;
        public int age;
        public string nationality;
        public string[] traits;        // 性格特质（如 "稳健", "数据驱动"）
        public float baseSalary;       // 年薪（万）
        public float loyalty;          // 初始忠诚度 (0-100)
        public float efficiencyBonus;  // 核心能力加成基数 (0.7~1.3)
        public string department;      // "OPS", "MKT", "FIN"...
    }
}
