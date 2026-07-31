#if UNITY_EDITOR
using System.IO;
using AirlineCEO.Data;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 批量生成 Aircraft / Executive / BoardMember 三类 ScriptableObject。
    /// 菜单：AirlineCEO → Batch Create Data Assets
    /// </summary>
    public static class BatchCreateDataAssets
    {
        private const string AircraftFolder = "Assets/ScriptableObjects/Aircraft";
        private const string ExecutiveFolder = "Assets/ScriptableObjects/Executives";
        private const string BoardFolder = "Assets/ScriptableObjects/BoardMembers";

        [MenuItem("AirlineCEO/Batch Create Data Assets")]
        public static void CreateAll()
        {
            EnsureFolders();

            int aircraftCount = CreateAircrafts();
            int executiveCount = CreateExecutives();
            int boardCount = CreateBoardMembers();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "批量创建完成",
                $"飞机：{aircraftCount} 个\n负责人：{executiveCount} 个\n董事：{boardCount} 个\n\n路径：Assets/ScriptableObjects/",
                "确定");
        }

        [MenuItem("AirlineCEO/Batch Create/Aircraft Only")]
        public static void CreateAircraftOnly()
        {
            EnsureFolders();
            int n = CreateAircrafts();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("完成", $"已创建/更新 {n} 个飞机资产", "确定");
        }

        [MenuItem("AirlineCEO/Batch Create/Executives Only")]
        public static void CreateExecutivesOnly()
        {
            EnsureFolders();
            int n = CreateExecutives();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("完成", $"已创建/更新 {n} 个负责人资产", "确定");
        }

        [MenuItem("AirlineCEO/Batch Create/Board Members Only")]
        public static void CreateBoardOnly()
        {
            EnsureFolders();
            int n = CreateBoardMembers();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("完成", $"已创建/更新 {n} 个董事资产", "确定");
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "ScriptableObjects");
            EnsureFolder("Assets/ScriptableObjects", "Aircraft");
            EnsureFolder("Assets/ScriptableObjects", "Executives");
            EnsureFolder("Assets/ScriptableObjects", "BoardMembers");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static int CreateAircrafts()
        {
            var list = new[]
            {
                new { id = "A320neo", pax = 180, range = 6300f, price = 11000f, burn = 2.4f },
                new { id = "A321neo", pax = 220, range = 7400f, price = 13000f, burn = 2.7f },
                new { id = "B737-8", pax = 178, range = 6570f, price = 10500f, burn = 2.3f },
                new { id = "A350-900", pax = 325, range = 15000f, price = 31700f, burn = 5.8f },
                new { id = "B787-9", pax = 296, range = 14140f, price = 29200f, burn = 5.4f },
            };

            int count = 0;
            foreach (var a in list)
            {
                string path = $"{AircraftFolder}/{a.id}.asset";
                var asset = LoadOrCreate<AircraftData>(path);
                asset.aircraftID = a.id;
                asset.maxPassengers = a.pax;
                asset.maxRange = a.range;
                asset.basePrice = a.price;
                asset.fuelBurnPerHour = a.burn;
                EditorUtility.SetDirty(asset);
                count++;
            }
            return count;
        }

        private static int CreateExecutives()
        {
            // MVP：每部门取候选池前 3 人，共 21 人
            var list = new[]
            {
                Ex("OPS-01", "马格努斯·林德格伦", 54, "瑞典", new[] { "稳健", "数据驱动" }, 290f, 80f, 1.10f, "OPS"),
                Ex("OPS-02", "西蒂·拉赫玛", 39, "马来西亚", new[] { "人脉型", "细致" }, 180f, 75f, 1.00f, "OPS"),
                Ex("OPS-03", "科菲·门萨", 49, "加纳", new[] { "激进", "开拓" }, 220f, 65f, 0.95f, "OPS"),

                Ex("MNT-01", "吕克·杜布瓦", 54, "比利时", new[] { "稳健", "细节控" }, 270f, 82f, 1.15f, "MNT"),
                Ex("MNT-02", "齐亚德·马赫卢夫", 47, "黎巴嫩", new[] { "数据驱动", "冷静" }, 240f, 81f, 1.10f, "MNT"),
                Ex("MNT-03", "阿米塔·阿南德", 41, "斯里兰卡", new[] { "人脉型", "创新" }, 190f, 69f, 0.95f, "MNT"),

                Ex("MKT-01", "英格丽德·哈根", 48, "丹麦", new[] { "数据驱动", "冷静" }, 290f, 75f, 1.10f, "MKT"),
                Ex("MKT-02", "塔里克·本·阿里", 44, "阿尔及利亚", new[] { "人脉型", "进取" }, 220f, 74f, 1.00f, "MKT"),
                Ex("MKT-03", "努尔·法蒂玛", 39, "巴基斯坦", new[] { "稳健", "务实" }, 190f, 81f, 1.05f, "MKT"),

                Ex("FIN-01", "延斯·莫滕森", 55, "芬兰", new[] { "稳健", "数据流" }, 320f, 80f, 1.15f, "FIN"),
                Ex("FIN-02", "法蒂玛·扎赫拉·阿拉维", 44, "约旦", new[] { "人脉型", "冷静" }, 270f, 78f, 1.05f, "FIN"),
                Ex("FIN-03", "拉吉夫·维克拉马辛格", 41, "孟加拉国", new[] { "激进", "进取" }, 200f, 66f, 0.95f, "FIN"),

                Ex("HR-01", "伊丽莎白·林德", 47, "爱沙尼亚", new[] { "稳健", "数据流" }, 220f, 79f, 1.05f, "HR"),
                Ex("HR-02", "莱拉·本·艾哈迈德", 43, "突尼斯", new[] { "人脉型", "灵活" }, 200f, 80f, 1.05f, "HR"),
                Ex("HR-03", "穆罕默德·巴希尔", 50, "阿富汗", new[] { "激进", "稳健" }, 270f, 77f, 1.00f, "HR"),

                Ex("SAF-01", "拉尔斯·彼得森", 56, "冰岛", new[] { "稳健", "细节控" }, 300f, 84f, 1.20f, "SAF"),
                Ex("SAF-02", "让-皮埃尔·勒加尔", 48, "毛里求斯", new[] { "人脉型", "冷静" }, 240f, 78f, 1.05f, "SAF"),
                Ex("SAF-03", "穆斯塔法·纳布里", 44, "阿尔巴尼亚", new[] { "激进", "务实" }, 220f, 67f, 0.95f, "SAF"),

                Ex("STG-01", "迭戈·贝莱斯", 53, "乌拉圭", new[] { "稳健", "务实" }, 280f, 82f, 1.10f, "STG"),
                Ex("STG-02", "费萨尔·阿勒萨尼", 47, "卡塔尔", new[] { "激进", "人脉型" }, 350f, 62f, 1.00f, "STG"),
                Ex("STG-03", "马利克·迪亚洛", 44, "塞内加尔", new[] { "数据驱动", "冷静" }, 230f, 81f, 1.05f, "STG"),
            };

            int count = 0;
            foreach (var e in list)
            {
                string path = $"{ExecutiveFolder}/{e.id}.asset";
                var asset = LoadOrCreate<ExecutiveData>(path);
                asset.id = e.id;
                asset.fullName = e.fullName;
                asset.age = e.age;
                asset.nationality = e.nationality;
                asset.traits = e.traits;
                asset.baseSalary = e.baseSalary;
                asset.loyalty = e.loyalty;
                asset.efficiencyBonus = e.efficiencyBonus;
                asset.department = e.department;
                EditorUtility.SetDirty(asset);
                count++;
            }
            return count;
        }

        private static int CreateBoardMembers()
        {
            // 技术任务书：12 名董事 A–L（来自董事会角色卡池）
            var list = new[]
            {
                Bd("A", "埃里克·索尔伯格", "国家资本", "非执行董事", 50f, "T-求稳长线"),
                Bd("B", "陈慧玲", "机构投资", "执行董事", 50f, "T-成本市值"),
                Bd("C", "卡洛斯·费尔南德斯", "战略合作", "非执行董事", 50f, "T-新机型"),
                Bd("D", "娜奥米·坦纳", "独立", "独立董事", 50f, "T-碳积分"),
                Bd("E", "伊万·索科洛夫", "独立", "独立董事", 50f, "T-安全守门"),
                Bd("F", "玛丽亚·桑托斯", "独立", "职工董事", 50f, "T-工会代表"),
                Bd("G", "乔纳森·布莱克", "机构投资", "执行董事", 50f, "T-资本压力"),
                Bd("H", "伊莎贝拉·罗西", "独立", "独立董事", 50f, "T-服务体验"),
                Bd("I", "阿米特·辛格", "国家资本", "非执行董事", 50f, "T-政策资源"),
                Bd("J", "金秀贤", "机构投资", "非执行董事", 50f, "T-股息刚性"),
                Bd("K", "马库斯·温特", "机构投资", "非执行董事", 50f, "T-资产配置"),
                Bd("L", "穆罕默德·拉希德", "战略合作", "非执行董事", 50f, "T-货运腹舱"),
            };

            int count = 0;
            foreach (var b in list)
            {
                string path = $"{BoardFolder}/Board_{b.id}.asset";
                var asset = LoadOrCreate<BoardMemberData>(path);
                asset.id = b.id;
                asset.fullName = b.fullName;
                asset.faction = b.faction;
                asset.seatType = b.seatType;
                asset.satisfaction = b.satisfaction;
                asset.targetTag = b.targetTag;
                EditorUtility.SetDirty(asset);
                count++;
            }
            return count;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
                return existing;

            var asset = ScriptableObject.CreateInstance<T>();
            string dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                // 兜底：父目录应已由 EnsureFolders 创建
            }

            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static (string id, string fullName, int age, string nationality, string[] traits, float baseSalary, float loyalty, float efficiencyBonus, string department)
            Ex(string id, string fullName, int age, string nationality, string[] traits, float baseSalary, float loyalty, float efficiencyBonus, string department)
            => (id, fullName, age, nationality, traits, baseSalary, loyalty, efficiencyBonus, department);

        private static (string id, string fullName, string faction, string seatType, float satisfaction, string targetTag)
            Bd(string id, string fullName, string faction, string seatType, float satisfaction, string targetTag)
            => (id, fullName, faction, seatType, satisfaction, targetTag);
    }
}
#endif
