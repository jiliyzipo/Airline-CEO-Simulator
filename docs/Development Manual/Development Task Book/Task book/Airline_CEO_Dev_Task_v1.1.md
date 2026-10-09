# ✈️ Airline CEO Simulator · 技术任务书 v1.1
**（面向 Cursor + DeepSeek API 的 AI 编码实施蓝图）**

> ⚠️ **已归档：** 现行请改用同目录 **[Airline_CEO_Dev_Task_v1.2.md](./Airline_CEO_Dev_Task_v1.2.md)**（含通俗顾问文案与「综合评定 XX 分」等更新）。

**适用对象：** 独立开发者（无编程基础，依赖 AI 编程）  
**核心目标：** 将 GDD v1.3 的策划案转化为 Unity C# 可执行代码的精确指令。  
**工程根目录：** `Unity Project/`

---

## 文档修订记录

| 版本 | 说明 |
| :--- | :--- |
| v1.0 | 初版施工图（伪代码为主） |
| v1.1 | 写入已验证的 `RPMCalculator` 完整实现 |
| v1.1（进度同步） | 第 1～4 章已落地；补全 `DataBase` 21+12 名单 |
| **v1.1（第 5 章）** | DeepSeek `AIChatManager` + 3 秒超时 + 本地容灾 + RPM 真实数强制附带 |

---

## 开发进度总览（对照本任务书）

| 章节 | 内容 | 状态 | 工程路径 |
| :--- | :--- | :---: | :--- |
| 第 1 章 | 五文件夹 + 命名空间 | ✅ 完成 | `Assets/Scripts/{Data,Logic,Systems,UI,AI}/` |
| 第 2 章 | Aircraft / Executive / BoardMember | ✅ 完成 | `Assets/Scripts/Data/*.cs` |
| 第 3.1 | RPMCalculator | ✅ 完成并自测 | `Assets/Scripts/Logic/RPMCalculator.cs` |
| 第 3.2 | CashRunnelCalculator | ✅ 完成并自测 | `Assets/Scripts/Logic/CashRunnelCalculator.cs` |
| 第 3.3 | ChemistryMatcher | ✅ 完成并自测 | `Assets/Scripts/Logic/ChemistryMatcher.cs` |
| 第 4 章 | DataBase 硬编码名单 | ✅ 完成并 Play 验证 | `Assets/Scripts/Data/DataBase.cs` |
| 第 5 章 | DeepSeek / AIChatManager | ✅ 完成 | `Assets/Scripts/AI/` |
| 第 6 章 | MVP 首航 UI + 存档 | ✅ 完成 | 见下方 6.4 |
| 辅助 | 批量建 SO / 逻辑自测 / Setup MVP | ✅ 完成 | `Assets/Scripts/Editor/` |

**自测菜单（Unity 顶部）：**
- `AirlineCEO → Run Logic Self Tests`
- `AirlineCEO → Batch Create Data Assets`

**DataBase 验证：** 场景挂载 `DataBase` 组件后 Play，Console 出现  
`[DataBase] 已加载负责人 21 人，董事 12 人。`

---

## 第 1 章：项目工程结构（文件夹规划）✅

> 状态：已完成。无需再执行「建立文件夹」指令。

| 文件夹路径 | 命名空间 | 存放内容 |
| :--- | :--- | :--- |
| `Data/` | `AirlineCEO.Data` | 数据模型 + DataBase |
| `Logic/` | `AirlineCEO.Logic` | RPM / 现金跑道 / 化学反应 |
| `Systems/` | `AirlineCEO.Systems` | 时间、存档、事件（待实现） |
| `UI/` | `AirlineCEO.UI` | 界面控制器（待实现） |
| `AI/` | `AirlineCEO.AI` | DeepSeek / 关键词（待实现） |

建议资产目录（可选，编辑器批量生成）：  
`Assets/ScriptableObjects/{Aircraft,Executives,BoardMembers}/`

---

## 第 2 章：核心数据模型定义（Data Models）✅

> 状态：已完成。Unity 右键菜单：`Create → AirlineCEO → Data → …`

### 2.1 飞机数据（AircraftData）
文件：`Assets/Scripts/Data/AircraftData.cs`
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
文件：`Assets/Scripts/Data/ExecutiveData.cs`
```csharp
public class ExecutiveData : ScriptableObject {
    public string id;              // "OPS-01"
    public string fullName;
    public int age;
    public string nationality;
    public string[] traits;
    public float baseSalary;       // 年薪（万）
    public float loyalty;          // 0-100
    public float efficiencyBonus;  // 0.7~1.3
    public string department;      // "OPS", "MKT", "FIN"...
}
```

### 2.3 董事成员数据（BoardMemberData）
文件：`Assets/Scripts/Data/BoardMemberData.cs`
```csharp
public class BoardMemberData : ScriptableObject {
    public string id;              // "A", "B"...
    public string fullName;
    public string faction;
    public string seatType;
    public float satisfaction;     // 初始 50
    public string targetTag;
}
```

---

## 第 3 章：核心计算逻辑（纯函数类）✅

> 状态：已完成。不依赖 MonoBehaviour。用菜单 `AirlineCEO → Run Logic Self Tests` 验证。

### 3.1 航线盈利计算器（RPMCalculator）✅
> 对应 GDD 第 25 章；评分 ≥70 绿 / 40~69 黄 / &lt;40 红。  
> 文件：`Assets/Scripts/Logic/RPMCalculator.cs`  
> **完整源码以工程文件为准**（本任务书 v1.1 首次收录后，与工程保持一致）。

要点：
- `fuelPricePerTon` = **美元/吨**（内部 ÷10000 → 万美元）
- 评分以**日利润 + 利润率**为核心，长航线有利润不易误判红
- 自测参考：PEK-LHR → score≈70 绿；短途 → score≈67 黄

```csharp
// 调用示例
var output = RPMCalculator.Calculate(new RPMCalculator.RPMInput {
    distance = 8150f,
    fuelPricePerTon = 850f,
    competitorSupply = 0.65f,
    demandMultiplier = 1.2f,
    brandEquity = 55f
});
// output.score / output.rating / output.dailyProfit
```

### 3.2 现金跑道计算器（CashRunnelCalculator）✅
文件：`Assets/Scripts/Logic/CashRunnelCalculator.cs`
```csharp
namespace AirlineCEO.Logic
{
    public static class CashRunnelCalculator
    {
        public const float InfiniteRunwayMonths = 99f;

        public static float GetRunwayMonths(float currentCash, float monthlyBurnRate)
        {
            if (monthlyBurnRate <= 0f) return InfiniteRunwayMonths;
            if (currentCash <= 0f) return 0f;
            return currentCash / monthlyBurnRate;
        }

        // GDD：<3 月黄，<2 月红
        public static string GetRunwayWarning(float runwayMonths)
        {
            if (runwayMonths < 2f) return "红";
            if (runwayMonths < 3f) return "黄";
            return "绿";
        }
    }
}
```

### 3.3 化学反应匹配器（ChemistryMatcher）✅
> 对应 GDD 第 7 章 B-01~B-23（任务书原写「8 种」已扩展为完整规则集）。  
> 文件：`Assets/Scripts/Logic/ChemistryMatcher.cs`  
> 优先级：冲突 > 隐藏 > 多人≥4 > 三人 > 双人/单人；最多返回 6 个。

```csharp
// 调用示例（后续 UI / Systems 使用）
var ids = DataBase.Instance.GetBoardMemberIds();
List<string> triggered = ChemistryMatcher.Match(ids);
// 例：["B-16", "B-01", "B-09"]
```

自测参考：
- `[A,B,H,K]` → `B-16, B-01, B-09`
- `[A,G]` → `B-18`（有 G 时不触发 B-01）

---

## 第 4 章：数据表（内存硬编码 DataBase）✅

> 状态：已完成。跳过 Excel；在 `Awake()` 中初始化到内存。  
> 文件：`Assets/Scripts/Data/DataBase.cs`

### 4.0 使用说明（给开发者 / Cursor）

1. 场景中创建空物体，命名 `DataBase`，挂载 `DataBase` 组件。  
2. Play 后 Console 应显示：`已加载负责人 21 人，董事 12 人。`  
3. 其他脚本通过单例读取（**写代码时用，玩家无需手动操作**）：

```csharp
// 读列表
var execs = DataBase.Instance.Executives;       // 21 人
var board  = DataBase.Instance.BoardMembers;    // 12 人

// 按 ID 查找
var ops01 = DataBase.Instance.GetExecutive("OPS-01");
var directorA = DataBase.Instance.GetBoardMember("A");

// 交给化学反应匹配器
var chem = ChemistryMatcher.Match(DataBase.Instance.GetBoardMemberIds());
```

说明：`ExecutiveData` / `BoardMemberData` 是 ScriptableObject，运行时用 `ScriptableObject.CreateInstance` 创建，**不能**写成 `new ExecutiveData { ... }`。

### 4.1 21 名负责人清单（全量，每部门 3 人）

| ID | 姓名 | 年龄 | 国籍 | 年薪(万) | 忠诚 | 效率 | 部门 |
| :--- | :--- | ---: | :--- | ---: | ---: | ---: | :--- |
| OPS-01 | 马格努斯·林德格伦 | 54 | 瑞典 | 290 | 80 | 1.10 | OPS |
| OPS-02 | 西蒂·拉赫玛 | 39 | 马来西亚 | 180 | 75 | 1.00 | OPS |
| OPS-03 | 科菲·门萨 | 49 | 加纳 | 220 | 65 | 0.95 | OPS |
| MNT-01 | 吕克·杜布瓦 | 54 | 比利时 | 270 | 82 | 1.15 | MNT |
| MNT-02 | 齐亚德·马赫卢夫 | 47 | 黎巴嫩 | 240 | 81 | 1.10 | MNT |
| MNT-03 | 阿米塔·阿南德 | 41 | 斯里兰卡 | 190 | 69 | 0.95 | MNT |
| MKT-01 | 英格丽德·哈根 | 48 | 丹麦 | 290 | 75 | 1.10 | MKT |
| MKT-02 | 塔里克·本·阿里 | 44 | 阿尔及利亚 | 220 | 74 | 1.00 | MKT |
| MKT-03 | 努尔·法蒂玛 | 39 | 巴基斯坦 | 190 | 81 | 1.05 | MKT |
| FIN-01 | 延斯·莫滕森 | 55 | 芬兰 | 320 | 80 | 1.15 | FIN |
| FIN-02 | 法蒂玛·扎赫拉·阿拉维 | 44 | 约旦 | 270 | 78 | 1.05 | FIN |
| FIN-03 | 拉吉夫·维克拉马辛格 | 41 | 孟加拉国 | 200 | 66 | 0.95 | FIN |
| HR-01 | 伊丽莎白·林德 | 47 | 爱沙尼亚 | 220 | 79 | 1.05 | HR |
| HR-02 | 莱拉·本·艾哈迈德 | 43 | 突尼斯 | 200 | 80 | 1.05 | HR |
| HR-03 | 穆罕默德·巴希尔 | 50 | 阿富汗 | 270 | 77 | 1.00 | HR |
| SAF-01 | 拉尔斯·彼得森 | 56 | 冰岛 | 300 | 84 | 1.20 | SAF |
| SAF-02 | 让-皮埃尔·勒加尔 | 48 | 毛里求斯 | 240 | 78 | 1.05 | SAF |
| SAF-03 | 穆斯塔法·纳布里 | 44 | 阿尔巴尼亚 | 220 | 67 | 0.95 | SAF |
| STG-01 | 迭戈·贝莱斯 | 53 | 乌拉圭 | 280 | 82 | 1.10 | STG |
| STG-02 | 费萨尔·阿勒萨尼 | 47 | 卡塔尔 | 350 | 62 | 1.00 | STG |
| STG-03 | 马利克·迪亚洛 | 44 | 塞内加尔 | 230 | 81 | 1.05 | STG |

### 4.2 12 名董事清单（A–L）

| ID | 姓名 | 派系 | 席位 | 满意度 | 目标标签 |
| :--- | :--- | :--- | :--- | ---: | :--- |
| A | 埃里克·索尔伯格 | 国家资本 | 非执行董事 | 50 | T-求稳长线 |
| B | 陈慧玲 | 机构投资 | 执行董事 | 50 | T-成本市值 |
| C | 卡洛斯·费尔南德斯 | 战略合作 | 非执行董事 | 50 | T-新机型 |
| D | 娜奥米·坦纳 | 独立 | 独立董事 | 50 | T-碳积分 |
| E | 伊万·索科洛夫 | 独立 | 独立董事 | 50 | T-安全守门 |
| F | 玛丽亚·桑托斯 | 独立 | 职工董事 | 50 | T-工会代表 |
| G | 乔纳森·布莱克 | 机构投资 | 执行董事 | 50 | T-资本压力 |
| H | 伊莎贝拉·罗西 | 独立 | 独立董事 | 50 | T-服务体验 |
| I | 阿米特·辛格 | 国家资本 | 非执行董事 | 50 | T-政策资源 |
| J | 金秀贤 | 机构投资 | 非执行董事 | 50 | T-股息刚性 |
| K | 马库斯·温特 | 机构投资 | 非执行董事 | 50 | T-资产配置 |
| L | 穆罕默德·拉希德 | 战略合作 | 非执行董事 | 50 | T-货运腹舱 |

---

## 第 5 章：智能指挥中枢 API 契约（DeepSeek 接入）✅

> 状态：已完成。无 Key 或超时/失败时自动回落本地话术库（`NeuralCommandMatcher`）。

### 5.1 请求结构 ✅
- **System Prompt：** *“你是航空公司的战略顾问。你的回答必须基于以下‘游戏数据快照’中的具体数值，不要编造。回答控制在 50 字以内，用中文。”*（另要求只输出 JSON）
- **User Prompt：** 玩家输入 + `GameDataSnapshot`（现金、日期、流动性、首航状态、北京-伦敦 RPM 真实数）

### 5.2 响应结构（必须 JSON）✅
```json
{
    "advisor": "战略顾问",
    "emotion": "谨慎",
    "message": "老板，这条线日均客流虽好，但英航占据65%时刻，月利润仅60万。",
    "suggestedAction": "open_route",
    "data": { "profit": 60, "risk": "high" }
}
```
`suggestedAction` 支持：`open_route` / `delay` / `reject` / `check_cash` / `first_flight` / `none`

### 5.3 容灾 ✅
1. **3 秒超时**（`UnityWebRequest.timeout`）  
2. API 失败 / 无 Key / 解析失败 → **本地预置话术库**  
3. 回复强制附带 **RPMCalculator** 真实数值（评分/灯色/日利润）  

### 5.4 工程文件与配置
| 文件 | 说明 |
| :--- | :--- |
| `Assets/Scripts/AI/AIChatManager.cs` | DeepSeek 调用 + 容灾 |
| `Assets/Scripts/AI/DeepSeekConfig.cs` | 端点/模型/超时/System Prompt |
| `Assets/Scripts/AI/GameDataSnapshot.cs` | 游戏数据快照 |
| `Assets/Scripts/AI/AdvisorModels.cs` | JSON 响应模型 |
| `Assets/Scripts/AI/NeuralCommandMatcher.cs` | 本地话术库 |
| `Assets/Resources/DeepSeekConfig.asset` | 配置资产（菜单可生成） |

**如何配置 Key（本机，不进 Git）：**
1. 菜单 **AirlineCEO → Create DeepSeek Config Asset**（若尚无）  
2. 菜单 **AirlineCEO → Set DeepSeek API Key** → 粘贴 Key → 保存  
3. **AirlineCEO → Setup MVP Scene Objects**  
4. Play → Neural Command 输入「现金」或「分析航线」  

未配置 Key 时也会立刻走本地话术，并提示「未配置 API Key」。  

---

## 第 6 章：MVP 垂直切片（第一财年）✅

> 状态：已完成。运行时自动搭建 UGUI，无需手拖控件。

### 6.1 P0 功能
1. **主界面**：日期（第 1 年 Q1）、现金、现金跑道（油量表 UI） ✅  
2. **首航任务**：按钮「确认首航」→ 约 5 秒进度 → 结算赚 15 万 ✅  
3. **RPM 查询**：右侧输入「北京」「伦敦」→ 调用 `RPMCalculator` → 绿/黄/红 ✅  
4. **Neural Command**：输入文字 → 关键词匹配 → **假 AI 对话框** ✅  
5. 其他按钮（机队/董事会/融资/市场）→ 弹窗「开发中」 ✅  

### 6.2 存档 ✅
- `SaveManager`：`PlayerPrefs` 存现金、年/季、董事会满意度、首航是否完成  
- **F5** 存档，**F9** 读档  

### 6.3 复用模块
- 现金跑道 ← `CashRunnelCalculator`  
- RPM 灯色 ← `RPMCalculator`  
- 假对话 ← `NeuralCommandMatcher`（含 RPM/现金真实数）  
- `DataBase` 由 `MVPBootstrap` 自动确保存在  

### 6.4 工程文件与操作步骤
| 文件 | 说明 |
| :--- | :--- |
| `Assets/Scripts/Systems/GameState.cs` | 全局状态 |
| `Assets/Scripts/Systems/SaveManager.cs` | F5/F9 |
| `Assets/Scripts/AI/NeuralCommandMatcher.cs` | 假 AI 关键词 |
| `Assets/Scripts/UI/MVPBootstrap.cs` | 自动搭界面 + 绑定逻辑 |
| `Assets/Scripts/Editor/SetupMVPScene.cs` | 一键挂载场景组件 |

**如何 Play：**
1. 打开场景（如 `Assets/MVP V1.0.unity` 或任意场景）  
2. 菜单 **AirlineCEO → Setup MVP Scene Objects**  
3. **Ctrl+S** 保存场景  
4. 点 **Play**  
5. 试玩：确认首航 → RPM 分析北京/伦敦 → 聊天输入「现金」→ F5/F9  

初始现金 500 万美元，月烧 80 万；首航奖励 +15 万。

---

## 第 7 章：给 Cursor 的提示词模板

| 步骤 | 状态 | 话术 |
| :--- | :---: | :--- |
| 第一步 骨架 | ✅ | （已完成，勿重复） |
| 第二步 数据类 | ✅ | （已完成，勿重复） |
| 第三步 计算器 | ✅ | （已完成，勿重复） |
| 第四步 DataBase | ✅ | （已完成，勿重复） |
| 第五步 界面 | ✅ | （已完成，勿重复） |
| 第六步 存档 | ✅ | （已完成，勿重复） |
| **第七步 AI API** | ✅ | （已完成；用 Set DeepSeek API Key 配置后验证） |

---

## 附录：开发排期建议

| 阶段 | 耗时 | 核心产出 | 状态 |
| :--- | :--- | :--- | :---: |
| 数据层搭建 | 2h | Data 类 + DataBase | ✅ |
| 逻辑层验证 | 3h | RPM / Cash / Chemistry 自测通过 | ✅ |
| 核心 UI 搭建 | 4h | 现金跑道 + 首航流程 | ✅ |
| **AI 接口调通** | 3h | DeepSeek JSON | ✅ |
| 垂直切片整合 | 4h | 接真 API 后闭环打磨 | ⬜ 可选 |

---

**最后建议：**  
第 1～6 章（含 DeepSeek）已落地。请先 **Set DeepSeek API Key** 后 Play 验证；无 Key 时应看到本地容灾回复。后续可打磨 UI 或扩展 `suggestedAction` 执行逻辑。
