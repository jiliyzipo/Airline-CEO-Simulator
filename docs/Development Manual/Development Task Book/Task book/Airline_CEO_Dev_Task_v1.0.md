# ✈️ Airline CEO Simulator · 技术任务书 v1.0
**（面向 Cursor + DeepSeek API 的 AI 编码实施蓝图）**

**适用对象：** 独立开发者（无编程基础，依赖 AI 编程）
**核心目标：** 将 GDD v1.3 的策划案转化为 Unity C# 可执行代码的精确指令。

---

## 第 1 章：项目工程结构（文件夹规划）

> 给 Cursor 的指令：
> 请在 Unity 项目的 `Assets/Scripts/` 下，建立以下 5 个核心文件夹，并生成对应的命名空间（Namespace）：

| 文件夹路径 | 命名空间 | 存放内容 |
| :--- | :--- | :--- |
| `Data/` | `AirlineCEO.Data` | 所有游戏数据模型（飞机、机场、角色属性） |
| `Logic/` | `AirlineCEO.Logic` | 核心计算引擎（RPM、现金流、化学反应匹配） |
| `Systems/` | `AirlineCEO.Systems` | 游戏循环管理器（时间、存档、事件触发） |
| `UI/` | `AirlineCEO.UI` | 界面控制器（按钮、弹窗、仪表盘更新） |
| `AI/` | `AirlineCEO.AI` | 智能指挥中枢（关键词匹配 + DeepSeek API 调用） |

---

## 第 2 章：核心数据模型定义（Data Models）

> 给 Cursor 的指令：
> 以下是我需要的核心 C# 类（ScriptableObject）。请为每个类生成完整的代码，并标注 `[CreateAssetMenu]`，方便我在 Unity 中右键创建配置文件。

### 2.1 飞机数据（AircraftData）
```csharp
public class AircraftData : ScriptableObject {
    public string aircraftID;      // 如 "A320neo"
    public int maxPassengers;      // 载客量
    public float maxRange;         // 最大航程（公里）
    public float basePrice;        // 采购价格（万美元）
    public float fuelBurnPerHour;  // 每小时油耗（吨）
}
```

### 2.2 部门负责人数据（ExecutiveData）
> *（对应 GDD 第 6.3 节的 21 人名单）*
```csharp
public class ExecutiveData : ScriptableObject {
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
```

### 2.3 董事成员数据（BoardMemberData）
> *（对应 GDD 第 5.2 节的 12 人名单）*
```csharp
public class BoardMemberData : ScriptableObject {
    public string id;              // "A", "B"...
    public string fullName;
    public string faction;         // "国家资本", "机构投资", "独立"...
    public string seatType;        // "执行董事", "非执行董事"...
    public float satisfaction;     // 初始满意度 (50)
    public string targetTag;       // 关联的目标标签（如 "T-01"）
}
```

---

## 第 3 章：核心计算逻辑（纯函数类）

> 给 Cursor 的指令：
> 请生成以下静态类（Static Class），里面只包含数学计算，不依赖 MonoBehaviour，方便我做单元测试。

### 3.1 航线盈利计算器（RPMCalculator）
> *（对应 GDD 第 7.7 节的 RPM 简化模型）*
```csharp
public static class RPMCalculator {
    // 输入参数
    public struct RPMInput {
        public float distance;          // 航程（公里）
        public float fuelPricePerTon;   // 当前油价
        public float competitorSupply;  // 竞对运力占比 (0.0~1.0)
        public float demandMultiplier;  // 季节系数 (0.8~2.5)
        public float brandEquity;       // 品牌净值 (0~100)
    }

    // 输出结果
    public struct RPMOutput {
        public float dailyRevenue;      // 日收入（万美元）
        public float dailyCost;         // 日成本（万美元）
        public float dailyProfit;       // 日利润（万美元）
        public int score;               // 综合评分 (0~100)
        public string rating;           // "绿", "黄", "红"
    }

    public static RPMOutput Calculate(RPMInput input) {
        // 请根据公式实现：
        // 评分 = (预估客流 × 季节性) - (燃油成本 + 起降费) - (竞对压迫系数)
        // ...
    }
}
```

### 3.2 现金跑道计算器（CashRunnelCalculator）
> *（对应 GDD 第 7.4 节）*
```csharp
public static class CashRunnelCalculator {
    public static float GetRunwayMonths(float currentCash, float monthlyBurnRate) {
        if (monthlyBurnRate <= 0) return 99f; // 无限
        return currentCash / monthlyBurnRate;
    }
}
```

### 3.3 化学反应匹配器（ChemistryMatcher）
> *（对应 GDD 第 5.3 节：8 种化学反应）*
```csharp
public static class ChemistryMatcher {
    // 输入：当前董事会的 ID 列表（如 ["A", "B", "C"...]）
    // 输出：触发成功的化学组合列表（如 ["B-01", "B-16"]）
    public static List<string> Match(List<string> boardIDs) {
        // 硬编码规则：
        // B-01: 如果包含 "A" 且不包含 "G"
        // B-16: 如果同时包含 "B" 和 "H"（冲突类优先）
        // ...
        // 注意：返回列表按优先级排序（冲突类 > 多人 > 双人）
    }
}
```

---

## 第 4 章：数据表（CSV 格式硬编码）

> 给 Cursor 的指令：
> 为了跳过复杂的 Excel 读取，请在 `Data/` 文件夹中创建 `DataBase.cs` 脚本，把这些列表直接写在 `Start()` 或 `Awake()` 里，初始化到内存中。我只复制以下内容给你。

### 4.1 21 名负责人清单（部分示例，请写全所有 21 人）
```csharp
void LoadExecutives() {
    var list = new List<ExecutiveData>();
    // OPS
    list.Add(new ExecutiveData{ id="OPS-01", name="马格努斯·林德格伦", age=54, nationality="瑞典", baseSalary=290, loyalty=80, department="OPS" });
    list.Add(new ExecutiveData{ id="OPS-02", name="科菲·门萨", age=49, nationality="加纳", baseSalary=220, loyalty=65, department="OPS" });
    // ...（后续 MNT, MKT, FIN, HR, SAF, STG 共 21 条）
}
```

### 4.2 12 名董事清单
```csharp
void LoadBoardMembers() {
    var list = new List<BoardMemberData>();
    list.Add(new BoardMemberData{ id="A", name="埃里克·索尔伯格", faction="国家资本", seatType="非执行董事" });
    list.Add(new BoardMemberData{ id="B", name="陈慧玲", faction="机构投资", seatType="执行董事" });
    // ...（共 12 条）
}
```

---

## 第 5 章：智能指挥中枢 API 契约（DeepSeek 接入）

> 给 Cursor 的指令：
> 我需要一个 `AIChatManager` 类。它不直接生成数据，而是把玩家的文字**转换成固定的指令枚举**，并调用 DeepSeek API 生成顾问对话文本。

### 5.1 请求结构（请严格按照这个格式调用 API）
- **System Prompt（系统设定）**：*“你是航空公司的战略顾问。你的回答必须基于以下‘游戏数据快照’中的具体数值，不要编造。回答控制在 50 字以内，用中文。”*
- **User Prompt（用户输入）**：玩家输入的文本 + 当前游戏数据快照（比如当前现金、机队规模）。

### 5.2 响应结构（告诉 AI 必须返回 JSON）
```json
{
    "advisor": "战略顾问",  // 或 "财务顾问", "人力顾问"
    "emotion": "谨慎",      // 或 "激进", "平和"
    "message": "老板，这条线日均客流虽好，但英航占据65%时刻，月利润仅60万。",
    "suggestedAction": "open_route", // 或 "delay", "reject"
    "data": { "profit": 60, "risk": "high" }
}
```

### 5.3 容灾机制（防止断网或 API 报错）
```csharp
// 给 Cursor 的代码要求：
// 1. 设置 3 秒超时。
// 2. 如果 API 无响应，自动使用本地“预置话术库”回复。
// 3. 回复内容必须包含从 RPMCalculator 算出来的真实数值。
```

---

## 第 6 章：MVP 垂直切片（第一财年实现清单）

> 给 Cursor 的指令：
> 为了让我 15 分钟看到可玩内容，请**暂时忽略**其他复杂系统，只生成以下 **“首航体验”** 场景。其他按钮点击后只弹窗提示“开发中”。

### 6.1 必须实现的功能（P0 级）
1. **主界面**：显示当前日期（第 1 年 Q1）、现金总额、现金跑道（油量表 UI）。
2. **首航任务**：点击唯一亮起的按钮“确认首航”，播放 5 秒起飞动画（或进度条），结算窗口弹出显示赚了 15 万。
3. **RPM 查询**：在右侧面板输入“北京”和“伦敦”，点击分析，调用 RPMCalculator 显示绿/黄/红灯。
4. **Neural Command 输入框**：输入文字，触发关键词匹配，弹出一个假的 AI 对话框（先写死一个回复，等后面再接入 API）。

### 6.2 数据存储（存档）
- 要求 Cursor 实现 `SaveManager`，使用 `PlayerPrefs` 保存当前现金、当前日期和当前董事会满意度。按 F5 存档，按 F9 读档。

---

## 第 7 章：给 Cursor 的提示词模板

> 把你接下来要复制给 Cursor 的话术直接给你：

- **第一步（生成骨架）：** *“请根据我提供的‘技术任务书’，在 Unity 中生成完整的项目结构。包含 Data, Logic, Systems, UI, AI 五个文件夹，并生成对应的命名空间。”*
- **第二步（生成数据类）：** *“请复制‘第 2 章’的类定义，生成 3 个 ScriptableObject 脚本，并确保它们在 Unity 右键菜单中可以创建。”*
- **第三步（生成计算器）：** *“请复制‘第 3 章’的 RPMCalculator，用 C# 实现公式。随便给我写几个测试用例，确保编译通过。”*
- **第四步（生成界面）：** *“请用 Unity UGUI 创建一个 Canvas，画一个顶部状态栏（显示现金和日期），中间一个地图区域，右下角一个聊天输入框。”*

---

## 附录：开发排期建议（单人或 AI 辅助）

| 阶段 | 耗时（小时） | 核心产出 |
| :--- | :--- | :--- |
| **数据层搭建** | 2h | 生成所有 Data 类，编译通过 |
| **逻辑层验证** | 3h | RPM 和 Cash 计算器通过单元测试 |
| **核心 UI 搭建** | 4h | 现金跑道和首航任务流程跑通 |
| **AI 接口调通** | 3h | DeepSeek API 成功返回 JSON 数据 |
| **垂直切片整合** | 4h | 首航 + AI 对话 + 存档 完整循环 |

---

**最后建议：**
你现在手里这份不是“策划文档”，而是 **“AI 程序员的施工图”**。你不用读懂代码，只要照着这个任务书，分段给 Cursor 下达指令。遇到报错，直接把红色报错信息复制给 Kimi-3，它会帮你分析改哪里。