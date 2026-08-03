using System.Collections.Generic;
using UnityEngine;

namespace AirlineCEO.Data
{
    /// <summary>
    /// 运行时内存数据库：在 Awake 中硬编码初始化 21 名负责人 + 12 名董事。
    /// 用法：在场景中建空物体挂本脚本；其他系统通过 DataBase.Instance 读取。
    /// </summary>
    public class DataBase : MonoBehaviour
    {
        public static DataBase Instance { get; private set; }

        public IReadOnlyList<ExecutiveData> Executives => _executives;
        public IReadOnlyList<BoardMemberData> BoardMembers => _boardMembers;
        public IReadOnlyList<AircraftCatalogEntry> AircraftTypes => _aircraftTypes;

        private readonly List<ExecutiveData> _executives = new List<ExecutiveData>(21);
        private readonly List<BoardMemberData> _boardMembers = new List<BoardMemberData>(12);
        private readonly List<AircraftCatalogEntry> _aircraftTypes = new List<AircraftCatalogEntry>(3);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadExecutives();
            LoadBoardMembers();
            LoadAircraftTypes();

            Debug.Log($"[DataBase] 已加载负责人 {_executives.Count} 人，董事 {_boardMembers.Count} 人，机型 {_aircraftTypes.Count} 种。");
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public ExecutiveData GetExecutive(string id)
        {
            for (int i = 0; i < _executives.Count; i++)
            {
                if (_executives[i].id == id)
                    return _executives[i];
            }
            return null;
        }

        public List<ExecutiveData> GetExecutivesByDepartment(string department)
        {
            var list = new List<ExecutiveData>();
            for (int i = 0; i < _executives.Count; i++)
            {
                var e = _executives[i];
                if (e != null && string.Equals(e.department, department, System.StringComparison.OrdinalIgnoreCase))
                    list.Add(e);
            }
            return list;
        }

        public BoardMemberData GetBoardMember(string id)
        {
            for (int i = 0; i < _boardMembers.Count; i++)
            {
                if (_boardMembers[i].id == id)
                    return _boardMembers[i];
            }
            return null;
        }

        public AircraftCatalogEntry GetAircraftType(string typeId)
        {
            for (int i = 0; i < _aircraftTypes.Count; i++)
            {
                if (string.Equals(_aircraftTypes[i].typeId, typeId, System.StringComparison.OrdinalIgnoreCase))
                    return _aircraftTypes[i];
            }
            return null;
        }

        public List<string> GetBoardMemberIds()
        {
            var ids = new List<string>(_boardMembers.Count);
            for (int i = 0; i < _boardMembers.Count; i++)
                ids.Add(_boardMembers[i].id);
            return ids;
        }

        private void LoadAircraftTypes()
        {
            _aircraftTypes.Clear();
            _aircraftTypes.Add(new AircraftCatalogEntry
            {
                typeId = "A320neo", maxPassengers = 180, maxRangeKm = 6300f, priceWan = 11000f
            });
            _aircraftTypes.Add(new AircraftCatalogEntry
            {
                typeId = "B737-800", maxPassengers = 189, maxRangeKm = 5400f, priceWan = 9600f
            });
            _aircraftTypes.Add(new AircraftCatalogEntry
            {
                typeId = "B787-9", maxPassengers = 290, maxRangeKm = 14000f, priceWan = 25000f
            });
        }

        private void LoadExecutives()
        {
            _executives.Clear();

            // OPS（3）
            _executives.Add(MakeExecutive("OPS-01", "马格努斯·林德格伦", 54, "瑞典",
                new[] { "稳健", "数据驱动" }, 290f, 80f, 1.10f, "OPS"));
            _executives.Add(MakeExecutive("OPS-02", "西蒂·拉赫玛", 39, "马来西亚",
                new[] { "人脉型", "细致" }, 180f, 75f, 1.00f, "OPS"));
            _executives.Add(MakeExecutive("OPS-03", "科菲·门萨", 49, "加纳",
                new[] { "激进", "开拓" }, 220f, 65f, 0.95f, "OPS"));

            // MNT（3）
            _executives.Add(MakeExecutive("MNT-01", "吕克·杜布瓦", 54, "比利时",
                new[] { "稳健", "细节控" }, 270f, 82f, 1.15f, "MNT"));
            _executives.Add(MakeExecutive("MNT-02", "齐亚德·马赫卢夫", 47, "黎巴嫩",
                new[] { "数据驱动", "冷静" }, 240f, 81f, 1.10f, "MNT"));
            _executives.Add(MakeExecutive("MNT-03", "阿米塔·阿南德", 41, "斯里兰卡",
                new[] { "人脉型", "创新" }, 190f, 69f, 0.95f, "MNT"));

            // MKT（3）
            _executives.Add(MakeExecutive("MKT-01", "英格丽德·哈根", 48, "丹麦",
                new[] { "数据驱动", "冷静" }, 290f, 75f, 1.10f, "MKT"));
            _executives.Add(MakeExecutive("MKT-02", "塔里克·本·阿里", 44, "阿尔及利亚",
                new[] { "人脉型", "进取" }, 220f, 74f, 1.00f, "MKT"));
            _executives.Add(MakeExecutive("MKT-03", "努尔·法蒂玛", 39, "巴基斯坦",
                new[] { "稳健", "务实" }, 190f, 81f, 1.05f, "MKT"));

            // FIN（3）
            _executives.Add(MakeExecutive("FIN-01", "延斯·莫滕森", 55, "芬兰",
                new[] { "稳健", "数据流" }, 320f, 80f, 1.15f, "FIN"));
            _executives.Add(MakeExecutive("FIN-02", "法蒂玛·扎赫拉·阿拉维", 44, "约旦",
                new[] { "人脉型", "冷静" }, 270f, 78f, 1.05f, "FIN"));
            _executives.Add(MakeExecutive("FIN-03", "拉吉夫·维克拉马辛格", 41, "孟加拉国",
                new[] { "激进", "进取" }, 200f, 66f, 0.95f, "FIN"));

            // HR（3）
            _executives.Add(MakeExecutive("HR-01", "伊丽莎白·林德", 47, "爱沙尼亚",
                new[] { "稳健", "数据流" }, 220f, 79f, 1.05f, "HR"));
            _executives.Add(MakeExecutive("HR-02", "莱拉·本·艾哈迈德", 43, "突尼斯",
                new[] { "人脉型", "灵活" }, 200f, 80f, 1.05f, "HR"));
            _executives.Add(MakeExecutive("HR-03", "穆罕默德·巴希尔", 50, "阿富汗",
                new[] { "激进", "稳健" }, 270f, 77f, 1.00f, "HR"));

            // SAF（3）
            _executives.Add(MakeExecutive("SAF-01", "拉尔斯·彼得森", 56, "冰岛",
                new[] { "稳健", "细节控" }, 300f, 84f, 1.20f, "SAF"));
            _executives.Add(MakeExecutive("SAF-02", "让-皮埃尔·勒加尔", 48, "毛里求斯",
                new[] { "人脉型", "冷静" }, 240f, 78f, 1.05f, "SAF"));
            _executives.Add(MakeExecutive("SAF-03", "穆斯塔法·纳布里", 44, "阿尔巴尼亚",
                new[] { "激进", "务实" }, 220f, 67f, 0.95f, "SAF"));

            // STG（3）
            _executives.Add(MakeExecutive("STG-01", "迭戈·贝莱斯", 53, "乌拉圭",
                new[] { "稳健", "务实" }, 280f, 82f, 1.10f, "STG"));
            _executives.Add(MakeExecutive("STG-02", "费萨尔·阿勒萨尼", 47, "卡塔尔",
                new[] { "激进", "人脉型" }, 350f, 62f, 1.00f, "STG"));
            _executives.Add(MakeExecutive("STG-03", "马利克·迪亚洛", 44, "塞内加尔",
                new[] { "数据驱动", "冷静" }, 230f, 81f, 1.05f, "STG"));
        }

        private void LoadBoardMembers()
        {
            _boardMembers.Clear();

            _boardMembers.Add(MakeBoard("A", "埃里克·索尔伯格", "国家资本", "非执行董事", 50f, "T-求稳长线"));
            _boardMembers.Add(MakeBoard("B", "陈慧玲", "机构投资", "执行董事", 50f, "T-成本市值"));
            _boardMembers.Add(MakeBoard("C", "卡洛斯·费尔南德斯", "战略合作", "非执行董事", 50f, "T-新机型"));
            _boardMembers.Add(MakeBoard("D", "娜奥米·坦纳", "独立", "独立董事", 50f, "T-碳积分"));
            _boardMembers.Add(MakeBoard("E", "伊万·索科洛夫", "独立", "独立董事", 50f, "T-安全守门"));
            _boardMembers.Add(MakeBoard("F", "玛丽亚·桑托斯", "独立", "职工董事", 50f, "T-工会代表"));
            _boardMembers.Add(MakeBoard("G", "乔纳森·布莱克", "机构投资", "执行董事", 50f, "T-资本压力"));
            _boardMembers.Add(MakeBoard("H", "伊莎贝拉·罗西", "独立", "独立董事", 50f, "T-服务体验"));
            _boardMembers.Add(MakeBoard("I", "阿米特·辛格", "国家资本", "非执行董事", 50f, "T-政策资源"));
            _boardMembers.Add(MakeBoard("J", "金秀贤", "机构投资", "非执行董事", 50f, "T-股息刚性"));
            _boardMembers.Add(MakeBoard("K", "马库斯·温特", "机构投资", "非执行董事", 50f, "T-资产配置"));
            _boardMembers.Add(MakeBoard("L", "穆罕默德·拉希德", "战略合作", "非执行董事", 50f, "T-货运腹舱"));
        }

        private static ExecutiveData MakeExecutive(
            string id, string fullName, int age, string nationality,
            string[] traits, float baseSalary, float loyalty, float efficiencyBonus, string department)
        {
            // ScriptableObject 不能用 new，运行时用 CreateInstance 建内存实例
            var e = ScriptableObject.CreateInstance<ExecutiveData>();
            e.id = id;
            e.fullName = fullName;
            e.age = age;
            e.nationality = nationality;
            e.traits = traits;
            e.baseSalary = baseSalary;
            e.loyalty = loyalty;
            e.efficiencyBonus = efficiencyBonus;
            e.department = department;
            e.name = id;
            return e;
        }

        private static BoardMemberData MakeBoard(
            string id, string fullName, string faction, string seatType,
            float satisfaction, string targetTag)
        {
            var b = ScriptableObject.CreateInstance<BoardMemberData>();
            b.id = id;
            b.fullName = fullName;
            b.faction = faction;
            b.seatType = seatType;
            b.satisfaction = satisfaction;
            b.targetTag = targetTag;
            b.name = $"Board_{id}";
            return b;
        }
    }
}
