using System.Collections.Generic;
using UnityEngine;

namespace AirlineCEO.Data
{
    /// <summary>
    /// 运行时内存数据库：在 Awake 中硬编码初始化 70 名负责人 + 17 名董事。
    /// 用法：在场景中建空物体挂本脚本；其他系统通过 DataBase.Instance 读取。
    /// </summary>
    public class DataBase : MonoBehaviour
    {
        public static DataBase Instance { get; private set; }

        public IReadOnlyList<ExecutiveData> Executives => _executives;
        public IReadOnlyList<BoardMemberData> BoardMembers => _boardMembers;
        public IReadOnlyList<AircraftCatalogEntry> AircraftTypes => _aircraftTypes;

        private readonly List<ExecutiveData> _executives = new List<ExecutiveData>(70);
        private readonly List<BoardMemberData> _boardMembers = new List<BoardMemberData>(17);
        private readonly List<AircraftCatalogEntry> _aircraftTypes = new List<AircraftCatalogEntry>(160);

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
            var all = AircraftCatalog.All;
            for (int i = 0; i < all.Length; i++)
                _aircraftTypes.Add(all[i]);
        }

        private void LoadExecutives()
        {
            _executives.Clear();

            // OPS（10）
            _executives.Add(MakeExecutive("OPS-01", "马格努斯·林德格伦", 54, "瑞典",
                new[] { "稳健", "数据驱动" }, 290f, 80f, 1.10f, "OPS"));
            _executives.Add(MakeExecutive("OPS-02", "西蒂·拉赫玛", 39, "马来西亚",
                new[] { "人脉型", "细致" }, 180f, 75f, 1.00f, "OPS"));
            _executives.Add(MakeExecutive("OPS-03", "科菲·门萨", 49, "加纳",
                new[] { "激进", "开拓" }, 220f, 65f, 0.95f, "OPS"));
            _executives.Add(MakeExecutive("OPS-04", "鲁斯塔姆·卡里莫夫", 45, "乌兹别克斯坦",
                new[] { "稳健", "保守" }, 210f, 72f, 0.95f, "OPS"));
            _executives.Add(MakeExecutive("OPS-05", "莱拉·纳赛尔", 41, "阿曼",
                new[] { "冷静", "数据流" }, 200f, 78f, 1.10f, "OPS"));
            _executives.Add(MakeExecutive("OPS-06", "巴勃罗·希门尼斯", 53, "西班牙",
                new[] { "激进", "冒险" }, 260f, 60f, 1.15f, "OPS"));
            _executives.Add(MakeExecutive("OPS-07", "克劳德·贝朗热", 56, "加拿大",
                new[] { "人脉型", "务实" }, 300f, 82f, 1.05f, "OPS"));
            _executives.Add(MakeExecutive("OPS-08", "霍内·图帕伊", 44, "新西兰",
                new[] { "创新", "细节控" }, 230f, 70f, 1.10f, "OPS"));
            _executives.Add(MakeExecutive("OPS-09", "达里乌什·科瓦尔斯基", 47, "波兰",
                new[] { "数据驱动", "冷静" }, 240f, 76f, 1.10f, "OPS"));
            _executives.Add(MakeExecutive("OPS-10", "卢西亚·费尔南德斯", 43, "阿根廷",
                new[] { "激进", "人脉型" }, 210f, 66f, 1.05f, "OPS"));

            // MNT（10）
            _executives.Add(MakeExecutive("MNT-01", "吕克·杜布瓦", 54, "比利时",
                new[] { "稳健", "细节控" }, 270f, 82f, 1.15f, "MNT"));
            _executives.Add(MakeExecutive("MNT-02", "齐亚德·马赫卢夫", 47, "黎巴嫩",
                new[] { "数据驱动", "冷静" }, 240f, 81f, 1.10f, "MNT"));
            _executives.Add(MakeExecutive("MNT-03", "阿米塔·阿南德", 41, "斯里兰卡",
                new[] { "人脉型", "创新" }, 190f, 69f, 0.95f, "MNT"));
            _executives.Add(MakeExecutive("MNT-04", "帕特里夏·帕拉西奥斯", 52, "秘鲁",
                new[] { "稳健", "务实" }, 260f, 74f, 1.00f, "MNT"));
            _executives.Add(MakeExecutive("MNT-05", "瓦西利斯·索蒂里乌", 49, "希腊",
                new[] { "激进", "冒险" }, 250f, 62f, 1.15f, "MNT"));
            _executives.Add(MakeExecutive("MNT-06", "阿贝贝·塔德斯", 43, "埃塞俄比亚",
                new[] { "数据驱动", "冷静" }, 210f, 77f, 1.10f, "MNT"));
            _executives.Add(MakeExecutive("MNT-07", "奥塔·什韦德茨", 55, "格鲁吉亚",
                new[] { "细节控", "保守" }, 290f, 80f, 0.95f, "MNT"));
            _executives.Add(MakeExecutive("MNT-08", "范氏梅", 38, "越南",
                new[] { "激进", "进取" }, 180f, 68f, 1.20f, "MNT"));
            _executives.Add(MakeExecutive("MNT-09", "哈维尔·桑切斯", 50, "智利",
                new[] { "稳健", "数据流" }, 260f, 75f, 1.05f, "MNT"));
            _executives.Add(MakeExecutive("MNT-10", "卡塔琳·纳吉", 46, "罗马尼亚",
                new[] { "人脉型", "灵活" }, 230f, 72f, 1.00f, "MNT"));

            // MKT（10）
            _executives.Add(MakeExecutive("MKT-01", "英格丽德·哈根", 48, "丹麦",
                new[] { "数据驱动", "冷静" }, 290f, 75f, 1.10f, "MKT"));
            _executives.Add(MakeExecutive("MKT-02", "塔里克·本·阿里", 44, "阿尔及利亚",
                new[] { "人脉型", "进取" }, 220f, 74f, 1.00f, "MKT"));
            _executives.Add(MakeExecutive("MKT-03", "努尔·法蒂玛", 39, "巴基斯坦",
                new[] { "稳健", "务实" }, 190f, 81f, 1.05f, "MKT"));
            _executives.Add(MakeExecutive("MKT-04", "安娜·克里斯蒂娜·科斯塔", 51, "安哥拉",
                new[] { "数据驱动", "细节控" }, 280f, 76f, 1.05f, "MKT"));
            _executives.Add(MakeExecutive("MKT-05", "彼得·科瓦奇", 46, "斯洛伐克",
                new[] { "激进", "冒险" }, 230f, 63f, 1.15f, "MKT"));
            _executives.Add(MakeExecutive("MKT-06", "苏钦达·普拉塞特", 42, "泰国",
                new[] { "人脉型", "灵活" }, 210f, 73f, 1.00f, "MKT"));
            _executives.Add(MakeExecutive("MKT-07", "莱昂纳多·费雷拉", 53, "委内瑞拉",
                new[] { "稳健", "保守" }, 260f, 78f, 0.95f, "MKT"));
            _executives.Add(MakeExecutive("MKT-08", "艾哈迈德·拉扎", 40, "伊拉克",
                new[] { "激进", "数据流" }, 180f, 66f, 1.20f, "MKT"));
            _executives.Add(MakeExecutive("MKT-09", "叶卡捷琳娜·阿列克谢耶娃", 49, "哈萨克斯坦",
                new[] { "稳健", "人脉型" }, 250f, 74f, 1.05f, "MKT"));
            _executives.Add(MakeExecutive("MKT-10", "迭戈·埃尔南德斯", 45, "哥斯达黎加",
                new[] { "创新", "细节控" }, 240f, 71f, 1.10f, "MKT"));

            // FIN（10）
            _executives.Add(MakeExecutive("FIN-01", "延斯·莫滕森", 55, "芬兰",
                new[] { "稳健", "数据流" }, 320f, 80f, 1.15f, "FIN"));
            _executives.Add(MakeExecutive("FIN-02", "法蒂玛·扎赫拉·阿拉维", 44, "约旦",
                new[] { "人脉型", "冷静" }, 270f, 78f, 1.05f, "FIN"));
            _executives.Add(MakeExecutive("FIN-03", "拉吉夫·维克拉马辛格", 41, "孟加拉国",
                new[] { "激进", "进取" }, 200f, 66f, 0.95f, "FIN"));
            _executives.Add(MakeExecutive("FIN-04", "玛丽亚·费尔南达·雷耶斯", 48, "哥伦比亚",
                new[] { "数据驱动", "保守" }, 280f, 77f, 1.00f, "FIN"));
            _executives.Add(MakeExecutive("FIN-05", "斯特凡诺·比安奇", 52, "奥地利",
                new[] { "细节控", "稳健" }, 300f, 79f, 1.00f, "FIN"));
            _executives.Add(MakeExecutive("FIN-06", "恩戈齐·埃泽莫", 43, "尼日利亚",
                new[] { "激进", "人脉型" }, 230f, 64f, 1.10f, "FIN"));
            _executives.Add(MakeExecutive("FIN-07", "季米塔尔·格奥尔基耶夫", 50, "北马其顿",
                new[] { "务实", "冷静" }, 250f, 75f, 1.05f, "FIN"));
            _executives.Add(MakeExecutive("FIN-08", "黄青梅", 39, "缅甸",
                new[] { "稳健", "进取" }, 190f, 70f, 1.15f, "FIN"));
            _executives.Add(MakeExecutive("FIN-09", "迭戈·阿尔梅达", 46, "巴拿马",
                new[] { "数据驱动", "灵活" }, 260f, 73f, 1.10f, "FIN"));
            _executives.Add(MakeExecutive("FIN-10", "雅库布·诺瓦克", 54, "捷克",
                new[] { "细节控", "冒险" }, 310f, 76f, 1.05f, "FIN"));

            // HR（10）
            _executives.Add(MakeExecutive("HR-01", "伊丽莎白·林德", 47, "爱沙尼亚",
                new[] { "稳健", "数据流" }, 220f, 79f, 1.05f, "HR"));
            _executives.Add(MakeExecutive("HR-02", "莱拉·本·艾哈迈德", 43, "突尼斯",
                new[] { "人脉型", "灵活" }, 200f, 80f, 1.05f, "HR"));
            _executives.Add(MakeExecutive("HR-03", "穆罕默德·巴希尔", 50, "阿富汗",
                new[] { "激进", "稳健" }, 270f, 77f, 1.00f, "HR"));
            _executives.Add(MakeExecutive("HR-04", "阿什利·福斯特", 41, "牙买加",
                new[] { "人脉型", "创新" }, 230f, 72f, 1.10f, "HR"));
            _executives.Add(MakeExecutive("HR-05", "卡特琳·帕多瓦", 48, "萨尔瓦多",
                new[] { "数据驱动", "细节控" }, 240f, 76f, 1.05f, "HR"));
            _executives.Add(MakeExecutive("HR-06", "塔玛拉·季瓦泽", 44, "阿塞拜疆",
                new[] { "稳健", "保守" }, 250f, 78f, 0.95f, "HR"));
            _executives.Add(MakeExecutive("HR-07", "杰特·库尔马", 52, "拉脱维亚",
                new[] { "人脉型", "务实" }, 260f, 75f, 1.00f, "HR"));
            _executives.Add(MakeExecutive("HR-08", "索姆奇·沃拉冯", 39, "老挝",
                new[] { "激进", "灵活" }, 180f, 67f, 1.10f, "HR"));
            _executives.Add(MakeExecutive("HR-09", "维提·科罗", 46, "斐济",
                new[] { "稳健", "人脉型" }, 210f, 74f, 1.00f, "HR"));
            _executives.Add(MakeExecutive("HR-10", "亚历山大·武科维奇", 54, "黑山",
                new[] { "细节控", "保守" }, 290f, 77f, 0.95f, "HR"));

            // SAF（10）
            _executives.Add(MakeExecutive("SAF-01", "拉尔斯·彼得森", 56, "冰岛",
                new[] { "稳健", "细节控" }, 300f, 84f, 1.20f, "SAF"));
            _executives.Add(MakeExecutive("SAF-02", "让-皮埃尔·勒加尔", 48, "毛里求斯",
                new[] { "人脉型", "冷静" }, 240f, 78f, 1.05f, "SAF"));
            _executives.Add(MakeExecutive("SAF-03", "穆斯塔法·纳布里", 44, "阿尔巴尼亚",
                new[] { "激进", "务实" }, 220f, 67f, 0.95f, "SAF"));
            _executives.Add(MakeExecutive("SAF-04", "卡洛斯·格里哈尔瓦", 52, "巴拉圭",
                new[] { "数据驱动", "保守" }, 260f, 77f, 1.00f, "SAF"));
            _executives.Add(MakeExecutive("SAF-05", "玛丽亚姆·德巴什", 41, "亚美尼亚",
                new[] { "冷静", "细节控" }, 200f, 75f, 1.05f, "SAF"));
            _executives.Add(MakeExecutive("SAF-06", "阿约·奥蒂诺", 47, "肯尼亚",
                new[] { "人脉型", "激进" }, 250f, 65f, 1.10f, "SAF"));
            _executives.Add(MakeExecutive("SAF-07", "贾马尔·阿尔-萨巴赫", 55, "科威特",
                new[] { "稳健", "保守" }, 310f, 79f, 0.95f, "SAF"));
            _executives.Add(MakeExecutive("SAF-08", "内利亚·席尔瓦", 43, "危地马拉",
                new[] { "数据驱动", "冷静" }, 220f, 76f, 1.05f, "SAF"));
            _executives.Add(MakeExecutive("SAF-09", "桑卡·阿基诺", 49, "多米尼加",
                new[] { "人脉型", "灵活" }, 260f, 72f, 1.00f, "SAF"));
            _executives.Add(MakeExecutive("SAF-10", "马克·迈尔", 58, "卢森堡",
                new[] { "稳健", "细节控" }, 330f, 80f, 1.10f, "SAF"));

            // STG（10）
            _executives.Add(MakeExecutive("STG-01", "迭戈·贝莱斯", 53, "乌拉圭",
                new[] { "稳健", "务实" }, 280f, 82f, 1.10f, "STG"));
            _executives.Add(MakeExecutive("STG-02", "费萨尔·阿勒萨尼", 47, "卡塔尔",
                new[] { "激进", "人脉型" }, 350f, 62f, 1.00f, "STG"));
            _executives.Add(MakeExecutive("STG-03", "马利克·迪亚洛", 44, "塞内加尔",
                new[] { "数据驱动", "冷静" }, 230f, 81f, 1.05f, "STG"));
            _executives.Add(MakeExecutive("STG-04", "斯特凡·约万诺维奇", 50, "塞尔维亚",
                new[] { "稳健", "保守" }, 270f, 78f, 0.95f, "STG"));
            _executives.Add(MakeExecutive("STG-05", "帕特里夏·坎波斯", 41, "玻利维亚",
                new[] { "激进", "创新" }, 210f, 66f, 1.15f, "STG"));
            _executives.Add(MakeExecutive("STG-06", "巴特图勒嘎·巴亚尔", 48, "蒙古",
                new[] { "人脉型", "务实" }, 260f, 73f, 1.00f, "STG"));
            _executives.Add(MakeExecutive("STG-07", "明道加斯·扬考斯卡斯", 54, "立陶宛",
                new[] { "数据驱动", "稳健" }, 290f, 76f, 1.05f, "STG"));
            _executives.Add(MakeExecutive("STG-08", "阿卜杜勒·贾巴尔·拉维", 46, "也门",
                new[] { "激进", "冒险" }, 240f, 62f, 1.15f, "STG"));
            _executives.Add(MakeExecutive("STG-09", "费德里科·加西亚", 52, "古巴",
                new[] { "人脉型", "灵活" }, 270f, 70f, 1.00f, "STG"));
            _executives.Add(MakeExecutive("STG-10", "谢尔盖·梅尔尼丘克", 43, "摩尔多瓦",
                new[] { "数据驱动", "冷静" }, 250f, 75f, 1.10f, "STG"));
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
            _boardMembers.Add(MakeBoard("M", "皮埃尔·杜布瓦", "战略合作", "非执行董事", 50f, "T-中转枢纽"));
            _boardMembers.Add(MakeBoard("N", "大卫·陈", "独立", "独立董事", 50f, "T-合规审查"));
            _boardMembers.Add(MakeBoard("O", "田中由美", "独立", "执行董事", 50f, "T-数据复盘"));
            _boardMembers.Add(MakeBoard("P", "塔博·恩科西", "国家资本", "非执行董事", 50f, "T-新兴市场"));
            _boardMembers.Add(MakeBoard("Q", "阿玛拉·奥蒂诺", "战略合作", "非执行董事", 50f, "T-临空经济"));
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
