# ✈️ Airline CEO Simulator · 技术任务书 v1.2
**（面向 Cursor + DeepSeek API 的 AI 编码实施蓝图）**

**适用对象：** 独立开发者（无编程基础，依赖 AI 编程）  
**核心目标：** 将 GDD v1.3 的策划案转化为 Unity C# 可执行代码的精确指令。  
**工程根目录：** `Unity Project/`  
**现行版本：** 请以 **v1.2** 为准（v1.0 / v1.1 仅作历史存档）

---

## 文档修订记录

| 版本 | 说明 |
| :--- | :--- |
| v1.0 | 初版施工图（伪代码为主） |
| v1.1 | RPM 完整实现；第 1～6 章落地；DeepSeek 接入 |
| **v1.2** | **玩家文案通俗化**：现金跑道/流动性表述；顾问大白话；强制「综合评定 XX 分」；同步工程现状与菜单 |

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
| 第 5 章 | DeepSeek / AIChatManager（通俗话术 + 综合评定） | ✅ 完成 | `Assets/Scripts/AI/` |
| 第 6 章 | MVP 首航 UI + 存档 | ✅ 完成 | `Assets/Scripts/UI/MVPBootstrap.cs` 等 |
| 辅助 | 批量建 SO / 逻辑自测 / Setup MVP / Set API Key | ✅ 完成 | `Assets/Scripts/Editor/` |

**Unity 顶部菜单：**
- `AirlineCEO → Run Logic Self Tests`
- `AirlineCEO → Batch Create Data Assets`
- `AirlineCEO → Setup MVP Scene Objects`
- `AirlineCEO → Create DeepSeek Config Asset`
- `AirlineCEO → Set DeepSeek API Key`

**DataBase 验证：** Play 后 Console：`[DataBase] 已加载负责人 21 人，董事 12 人。`

---

## 第 1 章：项目工程结构（文件夹规划）✅

| 文件夹路径 | 命名空间 | 存放内容 | MVP 状态 |
| :--- | :--- | :--- | :--- |
| `Data/` | `AirlineCEO.Data` | 数据模型 + DataBase | ✅ |
| `Logic/` | `AirlineCEO.Logic` | RPM / 现金跑道 / 化学反应 | ✅ |
| `Systems/` | `AirlineCEO.Systems` | GameState、SaveManager | ✅ MVP |
| `UI/` | `AirlineCEO.UI` | MVPBootstrap 首航界面 | ✅ MVP |
| `AI/` | `AirlineCEO.AI` | DeepSeek + 本地话术 | ✅ |

建议资产目录：`Assets/ScriptableObjects/{Aircraft,Executives,BoardMembers}/`

---

## 第 2 章：核心数据模型定义（Data Models）✅

> Unity 右键：`Create → AirlineCEO → Data → …`

### 2.1 AircraftData · `Assets/Scripts/Data/AircraftData.cs`
```csharp
public class AircraftData : ScriptableObject {
    public string aircraftID;      // 如 "A320neo"
    public int maxPassengers;
    public float maxRange;         // 公里
    public float basePrice;        // 万美元
    public float fuelBurnPerHour;  // 吨/小时
}
```

### 2.2 ExecutiveData · `Assets/Scripts/Data/ExecutiveData.cs`
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

### 2.3 BoardMemberData · `Assets/Scripts/Data/BoardMemberData.cs`
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

> 菜单：`AirlineCEO → Run Logic Self Tests`

### 3.1 RPMCalculator ✅
文件：`Assets/Scripts/Logic/RPMCalculator.cs`（**完整源码以工程为准**）

- `fuelPricePerTon` = **美元/吨**（内部 ÷10000 → 万美元）
- 评分以**日利润 + 利润率**为核心；≥70 绿 / 40~69 黄 / &lt;40 红
- 自测：PEK-LHR → score≈70 绿；短途 → score≈67 黄

```csharp
var output = RPMCalculator.Calculate(new RPMCalculator.RPMInput {
    distance = 8150f,
    fuelPricePerTon = 850f,
    competitorSupply = 0.65f,
    demandMultiplier = 1.2f,
    brandEquity = 55f
});
// output.score / output.rating / output.dailyProfit
```

### 3.2 CashRunnelCalculator ✅（v1.2 玩家文案）
文件：`Assets/Scripts/Logic/CashRunnelCalculator.cs`

**玩家可见叫法（勿只写「跑道 X 月」）：**
- 标题：**现金跑道**
- 说明：**按现在开支，流动性还可以撑 X 个月**
- 状态白话：稳妥 / 有点紧 / 偏危险

```csharp
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
    public static string GetRunwayWarning(float runwayMonths) { /* 红/黄/绿 */ }

    // 玩家文案
    public static string FormatPlayerLabel(float runwayMonths)
    {
        // 例：现金跑道：按现在开支，流动性还可以撑 6.3 个月
    }

    public static string FormatWarningPlain(float runwayMonths)
    {
        // 红→偏危险…；黄→有点紧…；绿→目前还算稳妥
    }
}
```

### 3.3 ChemistryMatcher ✅
文件：`Assets/Scripts/Logic/ChemistryMatcher.cs`  
B-01~B-23；优先级：冲突 > 隐藏 > 多人≥4 > 三人 > 双人/单人；最多 6 个。

```csharp
List<string> triggered = ChemistryMatcher.Match(DataBase.Instance.GetBoardMemberIds());
// 例：[A,B,H,K] → B-16, B-01, B-09
```

---

## 第 4 章：数据表（内存硬编码 DataBase）✅

文件：`Assets/Scripts/Data/DataBase.cs`  
`Awake()` 初始化；ScriptableObject 用 `CreateInstance`，不能 `new`。

```csharp
DataBase.Instance.Executives;          // 21
DataBase.Instance.BoardMembers;        // 12
DataBase.Instance.GetBoardMemberIds(); // 给 ChemistryMatcher
```

### 4.1 21 名负责人（每部门 3 人）

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

### 4.2 12 名董事（A–L）

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

> 无 Key / 超时 / 失败 → 本地话术库 `NeuralCommandMatcher`。  
> **v1.2：顾问面向新手——大白话 + 量化「综合评定 XX 分」。**

### 5.1 请求结构 ✅

**System Prompt（代码内置通俗版，`DeepSeekConfig.PlainLanguageSystemPrompt`）：**
- 面向游戏新手；只用快照真实数字，禁止编造  
- 大白话；专业词必须括号解释  
- 忌只说「竞对运力/RPM」→ 说「对手航班太多 / 划不划算」  
- **message 结尾必须带：`综合评定XX分`**  
- ≤50 字中文；只输出 JSON  

**User Prompt：** 玩家输入 + `GameDataSnapshot`（现金、日期、流动性白话、首航、北京-伦敦评估真值）

### 5.2 响应结构（必须 JSON）✅
```json
{
    "advisor": "战略顾问",
    "emotion": "谨慎",
    "message": "账上的钱大概还能撑半年多，北京飞伦敦还算划算，但对手航班不少。综合评定70分",
    "suggestedAction": "open_route",
    "data": { "profit": 62.8, "risk": "medium" }
}
```
`suggestedAction`：`open_route` / `delay` / `reject` / `check_cash` / `first_flight` / `none`

### 5.3 容灾与强制注脚 ✅
1. **3 秒超时**  
2. 失败 → **本地话术**（同样通俗）  
3. `EnsureRpmInMessage`：若缺少分数，自动追加注脚，例如：  
   `综合评定 70 分（比较划算，可以考虑开通；北京-伦敦大约每天能赚 62.8 万美元）`  
4. **禁止**对玩家展示黑话注脚如 `RPM70/绿/日利62.8`

### 5.4 工程文件
| 文件 | 说明 |
| :--- | :--- |
| `AI/AIChatManager.cs` | DeepSeek + 容灾 + 综合评定注脚 |
| `AI/DeepSeekConfig.cs` | 端点/模型/超时；默认通俗 System Prompt |
| `AI/GameDataSnapshot.cs` | 快照 + `FormatRpmFootnote` / `ToPlainRating` |
| `AI/AdvisorModels.cs` | JSON 模型 |
| `AI/NeuralCommandMatcher.cs` | 本地通俗话术 |
| `Resources/DeepSeekConfig.asset` | 配置资产 |

**配置 Key：** `AirlineCEO → Set DeepSeek API Key`（本机 PlayerPrefs，不进 Git）

---

## 第 6 章：MVP 垂直切片（第一财年）✅

### 6.1 P0 功能
1. 主界面：日期、现金、**现金跑道 / 流动性还可以撑 X 个月** ✅  
2. 确认首航 → 约 5 秒 → +15 万 ✅  
3. RPM：北京/伦敦 → 绿黄红 ✅  
4. Neural Command → **DeepSeek（优先）/ 本地容灾** ✅  
5. 机队/董事会/融资/市场 → 「开发中」 ✅  

### 6.2 存档 ✅
`SaveManager`：`PlayerPrefs`；**F5** 存、**F9** 读（现金、年/季、满意度、首航完成）

### 6.3 工程与 Play
| 文件 | 说明 |
| :--- | :--- |
| `Systems/GameState.cs` | 全局状态（初始现金 500，月烧 80） |
| `Systems/SaveManager.cs` | F5/F9 |
| `UI/MVPBootstrap.cs` | 运行时搭 UI + 绑定 |
| `Editor/SetupMVPScene.cs` | 一键挂载 |

1. 打开场景 → `Setup MVP Scene Objects` → 保存 → Play  
2. 试玩：首航 → RPM → 聊天「现金」「分析航线」→ F5/F9  

---

## 第 7 章：给 Cursor 的提示词模板

| 步骤 | 状态 | 说明 |
| :--- | :---: | :--- |
| 1～7 骨架/数据/逻辑/库/UI/存档/DeepSeek | ✅ | 勿重复生成；以工程与 **v1.2** 为准 |
| **后续建议** | ⬜ | 扩展 `suggestedAction` 真正改游戏状态；打磨 UI；补中文 TMP 字体等 |

示例后续指令：  
*“请按技术任务书 v1.2，让 suggestedAction=open_route 时自动高亮 RPM 面板，并写入一条操作日志。”*

---

## 附录 A：玩家文案规范（v1.2 新增）

| 场景 | 推荐说法 | 避免 |
| :--- | :--- | :--- |
| 现金可撑多久 | 现金跑道：按现在开支，流动性还可以撑 X 个月 | 只说「跑道 6.3 月」 |
| 资金状态 | 目前还算稳妥 / 有点紧 / 偏危险 | 只甩绿黄红字母给新手 |
| 航线评价 | 比较划算 / 先观望 / 暂缓 + **综合评定 XX 分** | `RPM70/绿/日利62.8` |
| 竞争 | 对手航班已经很多 | 竞对运力压迫系数 |

---

## 附录 B：开发排期

| 阶段 | 耗时 | 核心产出 | 状态 |
| :--- | :--- | :--- | :---: |
| 数据层 | 2h | Data + DataBase | ✅ |
| 逻辑层 | 3h | RPM / Cash / Chemistry | ✅ |
| 核心 UI | 4h | 首航 + 流动性条 | ✅ |
| AI 接口 | 3h | DeepSeek + 容灾 | ✅ |
| 文案打磨 | 1h | 通俗化 + 综合评定 | ✅ v1.2 |
| 体验打磨 | — | suggestedAction 落地、UI 美化 | ⬜ 可选 |

---

**最后建议：**  
以 **`Airline_CEO_Dev_Task_v1.2.md`** 为现行施工图。第 1～6 章已在工程落地；顾问必须**好懂 + 带综合评定分数**。遇报错把 Console 红字原文发给 Cursor / Kimi。
