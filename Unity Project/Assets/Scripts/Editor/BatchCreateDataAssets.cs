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
            // GDB 第11章：七部门 × 10 人 = 70 人
            var list = new[]
            {
                Ex("OPS-01", "马格努斯·林德格伦", 54, "瑞典", new[] { "稳健", "数据驱动" }, 290f, 80f, 1.10f, "OPS"),
                Ex("OPS-02", "西蒂·拉赫玛", 39, "马来西亚", new[] { "人脉型", "细致" }, 180f, 75f, 1.00f, "OPS"),
                Ex("OPS-03", "科菲·门萨", 49, "加纳", new[] { "激进", "开拓" }, 220f, 65f, 0.95f, "OPS"),
                Ex("OPS-04", "鲁斯塔姆·卡里莫夫", 45, "乌兹别克斯坦", new[] { "稳健", "保守" }, 210f, 72f, 0.95f, "OPS"),
                Ex("OPS-05", "莱拉·纳赛尔", 41, "阿曼", new[] { "冷静", "数据流" }, 200f, 78f, 1.10f, "OPS"),
                Ex("OPS-06", "巴勃罗·希门尼斯", 53, "西班牙", new[] { "激进", "冒险" }, 260f, 60f, 1.15f, "OPS"),
                Ex("OPS-07", "克劳德·贝朗热", 56, "加拿大", new[] { "人脉型", "务实" }, 300f, 82f, 1.05f, "OPS"),
                Ex("OPS-08", "霍内·图帕伊", 44, "新西兰", new[] { "创新", "细节控" }, 230f, 70f, 1.10f, "OPS"),
                Ex("OPS-09", "达里乌什·科瓦尔斯基", 47, "波兰", new[] { "数据驱动", "冷静" }, 240f, 76f, 1.10f, "OPS"),
                Ex("OPS-10", "卢西亚·费尔南德斯", 43, "阿根廷", new[] { "激进", "人脉型" }, 210f, 66f, 1.05f, "OPS"),

                Ex("MNT-01", "吕克·杜布瓦", 54, "比利时", new[] { "稳健", "细节控" }, 270f, 82f, 1.15f, "MNT"),
                Ex("MNT-02", "齐亚德·马赫卢夫", 47, "黎巴嫩", new[] { "数据驱动", "冷静" }, 240f, 81f, 1.10f, "MNT"),
                Ex("MNT-03", "阿米塔·阿南德", 41, "斯里兰卡", new[] { "人脉型", "创新" }, 190f, 69f, 0.95f, "MNT"),
                Ex("MNT-04", "帕特里夏·帕拉西奥斯", 52, "秘鲁", new[] { "稳健", "务实" }, 260f, 74f, 1.00f, "MNT"),
                Ex("MNT-05", "瓦西利斯·索蒂里乌", 49, "希腊", new[] { "激进", "冒险" }, 250f, 62f, 1.15f, "MNT"),
                Ex("MNT-06", "阿贝贝·塔德斯", 43, "埃塞俄比亚", new[] { "数据驱动", "冷静" }, 210f, 77f, 1.10f, "MNT"),
                Ex("MNT-07", "奥塔·什韦德茨", 55, "格鲁吉亚", new[] { "细节控", "保守" }, 290f, 80f, 0.95f, "MNT"),
                Ex("MNT-08", "范氏梅", 38, "越南", new[] { "激进", "进取" }, 180f, 68f, 1.20f, "MNT"),
                Ex("MNT-09", "哈维尔·桑切斯", 50, "智利", new[] { "稳健", "数据流" }, 260f, 75f, 1.05f, "MNT"),
                Ex("MNT-10", "卡塔琳·纳吉", 46, "罗马尼亚", new[] { "人脉型", "灵活" }, 230f, 72f, 1.00f, "MNT"),

                Ex("MKT-01", "英格丽德·哈根", 48, "丹麦", new[] { "数据驱动", "冷静" }, 290f, 75f, 1.10f, "MKT"),
                Ex("MKT-02", "塔里克·本·阿里", 44, "阿尔及利亚", new[] { "人脉型", "进取" }, 220f, 74f, 1.00f, "MKT"),
                Ex("MKT-03", "努尔·法蒂玛", 39, "巴基斯坦", new[] { "稳健", "务实" }, 190f, 81f, 1.05f, "MKT"),
                Ex("MKT-04", "安娜·克里斯蒂娜·科斯塔", 51, "安哥拉", new[] { "数据驱动", "细节控" }, 280f, 76f, 1.05f, "MKT"),
                Ex("MKT-05", "彼得·科瓦奇", 46, "斯洛伐克", new[] { "激进", "冒险" }, 230f, 63f, 1.15f, "MKT"),
                Ex("MKT-06", "苏钦达·普拉塞特", 42, "泰国", new[] { "人脉型", "灵活" }, 210f, 73f, 1.00f, "MKT"),
                Ex("MKT-07", "莱昂纳多·费雷拉", 53, "委内瑞拉", new[] { "稳健", "保守" }, 260f, 78f, 0.95f, "MKT"),
                Ex("MKT-08", "艾哈迈德·拉扎", 40, "伊拉克", new[] { "激进", "数据流" }, 180f, 66f, 1.20f, "MKT"),
                Ex("MKT-09", "叶卡捷琳娜·阿列克谢耶娃", 49, "哈萨克斯坦", new[] { "稳健", "人脉型" }, 250f, 74f, 1.05f, "MKT"),
                Ex("MKT-10", "迭戈·埃尔南德斯", 45, "哥斯达黎加", new[] { "创新", "细节控" }, 240f, 71f, 1.10f, "MKT"),

                Ex("FIN-01", "延斯·莫滕森", 55, "芬兰", new[] { "稳健", "数据流" }, 320f, 80f, 1.15f, "FIN"),
                Ex("FIN-02", "法蒂玛·扎赫拉·阿拉维", 44, "约旦", new[] { "人脉型", "冷静" }, 270f, 78f, 1.05f, "FIN"),
                Ex("FIN-03", "拉吉夫·维克拉马辛格", 41, "孟加拉国", new[] { "激进", "进取" }, 200f, 66f, 0.95f, "FIN"),
                Ex("FIN-04", "玛丽亚·费尔南达·雷耶斯", 48, "哥伦比亚", new[] { "数据驱动", "保守" }, 280f, 77f, 1.00f, "FIN"),
                Ex("FIN-05", "斯特凡诺·比安奇", 52, "奥地利", new[] { "细节控", "稳健" }, 300f, 79f, 1.00f, "FIN"),
                Ex("FIN-06", "恩戈齐·埃泽莫", 43, "尼日利亚", new[] { "激进", "人脉型" }, 230f, 64f, 1.10f, "FIN"),
                Ex("FIN-07", "季米塔尔·格奥尔基耶夫", 50, "北马其顿", new[] { "务实", "冷静" }, 250f, 75f, 1.05f, "FIN"),
                Ex("FIN-08", "黄青梅", 39, "缅甸", new[] { "稳健", "进取" }, 190f, 70f, 1.15f, "FIN"),
                Ex("FIN-09", "迭戈·阿尔梅达", 46, "巴拿马", new[] { "数据驱动", "灵活" }, 260f, 73f, 1.10f, "FIN"),
                Ex("FIN-10", "雅库布·诺瓦克", 54, "捷克", new[] { "细节控", "冒险" }, 310f, 76f, 1.05f, "FIN"),

                Ex("HR-01", "伊丽莎白·林德", 47, "爱沙尼亚", new[] { "稳健", "数据流" }, 220f, 79f, 1.05f, "HR"),
                Ex("HR-02", "莱拉·本·艾哈迈德", 43, "突尼斯", new[] { "人脉型", "灵活" }, 200f, 80f, 1.05f, "HR"),
                Ex("HR-03", "穆罕默德·巴希尔", 50, "阿富汗", new[] { "激进", "稳健" }, 270f, 77f, 1.00f, "HR"),
                Ex("HR-04", "阿什利·福斯特", 41, "牙买加", new[] { "人脉型", "创新" }, 230f, 72f, 1.10f, "HR"),
                Ex("HR-05", "卡特琳·帕多瓦", 48, "萨尔瓦多", new[] { "数据驱动", "细节控" }, 240f, 76f, 1.05f, "HR"),
                Ex("HR-06", "塔玛拉·季瓦泽", 44, "阿塞拜疆", new[] { "稳健", "保守" }, 250f, 78f, 0.95f, "HR"),
                Ex("HR-07", "杰特·库尔马", 52, "拉脱维亚", new[] { "人脉型", "务实" }, 260f, 75f, 1.00f, "HR"),
                Ex("HR-08", "索姆奇·沃拉冯", 39, "老挝", new[] { "激进", "灵活" }, 180f, 67f, 1.10f, "HR"),
                Ex("HR-09", "维提·科罗", 46, "斐济", new[] { "稳健", "人脉型" }, 210f, 74f, 1.00f, "HR"),
                Ex("HR-10", "亚历山大·武科维奇", 54, "黑山", new[] { "细节控", "保守" }, 290f, 77f, 0.95f, "HR"),

                Ex("SAF-01", "拉尔斯·彼得森", 56, "冰岛", new[] { "稳健", "细节控" }, 300f, 84f, 1.20f, "SAF"),
                Ex("SAF-02", "让-皮埃尔·勒加尔", 48, "毛里求斯", new[] { "人脉型", "冷静" }, 240f, 78f, 1.05f, "SAF"),
                Ex("SAF-03", "穆斯塔法·纳布里", 44, "阿尔巴尼亚", new[] { "激进", "务实" }, 220f, 67f, 0.95f, "SAF"),
                Ex("SAF-04", "卡洛斯·格里哈尔瓦", 52, "巴拉圭", new[] { "数据驱动", "保守" }, 260f, 77f, 1.00f, "SAF"),
                Ex("SAF-05", "玛丽亚姆·德巴什", 41, "亚美尼亚", new[] { "冷静", "细节控" }, 200f, 75f, 1.05f, "SAF"),
                Ex("SAF-06", "阿约·奥蒂诺", 47, "肯尼亚", new[] { "人脉型", "激进" }, 250f, 65f, 1.10f, "SAF"),
                Ex("SAF-07", "贾马尔·阿尔-萨巴赫", 55, "科威特", new[] { "稳健", "保守" }, 310f, 79f, 0.95f, "SAF"),
                Ex("SAF-08", "内利亚·席尔瓦", 43, "危地马拉", new[] { "数据驱动", "冷静" }, 220f, 76f, 1.05f, "SAF"),
                Ex("SAF-09", "桑卡·阿基诺", 49, "多米尼加", new[] { "人脉型", "灵活" }, 260f, 72f, 1.00f, "SAF"),
                Ex("SAF-10", "马克·迈尔", 58, "卢森堡", new[] { "稳健", "细节控" }, 330f, 80f, 1.10f, "SAF"),

                Ex("STG-01", "迭戈·贝莱斯", 53, "乌拉圭", new[] { "稳健", "务实" }, 280f, 82f, 1.10f, "STG"),
                Ex("STG-02", "费萨尔·阿勒萨尼", 47, "卡塔尔", new[] { "激进", "人脉型" }, 350f, 62f, 1.00f, "STG"),
                Ex("STG-03", "马利克·迪亚洛", 44, "塞内加尔", new[] { "数据驱动", "冷静" }, 230f, 81f, 1.05f, "STG"),
                Ex("STG-04", "斯特凡·约万诺维奇", 50, "塞尔维亚", new[] { "稳健", "保守" }, 270f, 78f, 0.95f, "STG"),
                Ex("STG-05", "帕特里夏·坎波斯", 41, "玻利维亚", new[] { "激进", "创新" }, 210f, 66f, 1.15f, "STG"),
                Ex("STG-06", "巴特图勒嘎·巴亚尔", 48, "蒙古", new[] { "人脉型", "务实" }, 260f, 73f, 1.00f, "STG"),
                Ex("STG-07", "明道加斯·扬考斯卡斯", 54, "立陶宛", new[] { "数据驱动", "稳健" }, 290f, 76f, 1.05f, "STG"),
                Ex("STG-08", "阿卜杜勒·贾巴尔·拉维", 46, "也门", new[] { "激进", "冒险" }, 240f, 62f, 1.15f, "STG"),
                Ex("STG-09", "费德里科·加西亚", 52, "古巴", new[] { "人脉型", "灵活" }, 270f, 70f, 1.00f, "STG"),
                Ex("STG-10", "谢尔盖·梅尔尼丘克", 43, "摩尔多瓦", new[] { "数据驱动", "冷静" }, 250f, 75f, 1.10f, "STG"),
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
            // 技术任务书：17 名董事 A–Q（来自董事会角色卡池）
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
                Bd("M", "皮埃尔·杜布瓦", "战略合作", "非执行董事", 50f, "T-中转枢纽"),
                Bd("N", "大卫·陈", "独立", "独立董事", 50f, "T-合规审查"),
                Bd("O", "田中由美", "独立", "执行董事", 50f, "T-数据复盘"),
                Bd("P", "塔博·恩科西", "国家资本", "非执行董事", 50f, "T-新兴市场"),
                Bd("Q", "阿玛拉·奥蒂诺", "战略合作", "非执行董事", 50f, "T-临空经济"),
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
