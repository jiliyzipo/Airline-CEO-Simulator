# ✈️ Airline CEO Simulator · 技术任务书 v1.3
**（面向 Cursor + DeepSeek API 的 AI 编码实施蓝图）**

> ⚠️ **历史存档。** 现行施工图请改用 **[`Airline_CEO_Dev_Task_v1.4.md`](./Airline_CEO_Dev_Task_v1.4.md)**（第 8～9 章含 Phase 7 安全与航权）。

**适用对象：** 独立开发者（无编程基础，依赖 AI 编程）  
**核心目标：** 将 GDD 策划案转化为 Unity C# 可执行代码的精确指令。  
**工程根目录：** `Unity Project/`  
**文档状态：** v1.3 已归档；请以 **v1.4** 为准

---

## 文档修订记录

| 版本 | 说明 |
| :--- | :--- |
| v1.0 | 初版施工图（伪代码为主） |
| v1.1 | RPM 完整实现；第 1～6 章落地；DeepSeek 接入 |
| v1.2 | 玩家文案通俗化；综合评定；Phase 1：ActionExecutor |
| **v1.3** | **第 7 章**：体验扩展 Phase 1～6（续聘投票）；Phase 7 草稿曾写入 7.7，正式版见 v1.4 第 9 章 |
| v1.4 | 第 8～9 章：清档/Debug；Phase 7 安全事件与航权（已验收） |

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
| **第 7 章** | **体验扩展 Phase 1～7** | ✅ **完成** | 见本章 |
| 辅助 | 批量建 SO / 逻辑自测 / Setup MVP / 字体 / Prefab | ✅ 完成 | `Assets/Scripts/Editor/` |

**Unity 顶部菜单（AirlineCEO）：**
- `Run Logic Self Tests`
- `Batch Create Data Assets`
- `Setup MVP Scene Objects`（会尝试生成中文 TMP 字体）
- `Create DeepSeek Config Asset`
- `Set DeepSeek API Key`
- `Create UI Prefabs (Phase 3)`
- `Generate Chinese TMP Font Asset`

**DataBase 验证：** Play 后 Console：`[DataBase] 已加载负责人 21 人，董事 12 人。`  
**中文字体验证：** Console：`[ChineseFontProvider] 已加载 Resources/Fonts/NotoSansSC-SDF`；顶栏可见 ✈ 或「航」+「天际航空」。

---

## 第 1 章：项目工程结构（文件夹规划）✅

| 文件夹路径 | 命名空间 | 存放内容 | MVP 状态 |
| :--- | :--- | :--- | :--- |
| `Data/` | `AirlineCEO.Data` | 数据模型 + DataBase + RouteData | ✅ |
| `Logic/` | `AirlineCEO.Logic` | RPM / 现金跑道 / 化学反应 | ✅ |
| `Systems/` | `AirlineCEO.Systems` | GameState、SaveManager、**ActionExecutor**、**TimeManager** | ✅ |
| `UI/` | `AirlineCEO.UI` | 驾驶舱 HUD、TMP、油量表等 | ✅ |
| `AI/` | `AirlineCEO.AI` | DeepSeek + 本地话术 | ✅ |

建议资产目录：`Assets/ScriptableObjects/{Aircraft,Executives,BoardMembers}/`  
字体目录：`Assets/Fonts/`、`Assets/Resources/Fonts/`

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

### 2.4 RouteData · `Assets/Scripts/Data/RouteData.cs`（v1.3 / Phase 1）
```csharp
public class RouteData {
    public string routeId;
    public string origin;          // 如 "北京"
    public string destination;     // 如 "伦敦"
    public float dailyProfit;      // 万美元/日
    public string openedDate;      // 如「第 1 年 Q1」
    public int score;
    public string rating;          // 绿/黄/红
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

### 3.2 CashRunnelCalculator ✅
文件：`Assets/Scripts/Logic/CashRunnelCalculator.cs`

**玩家可见叫法（勿只写「跑道 X 月」）：**
- 标题：**现金跑道**
- 说明：**按现在开支，流动性还可以撑 X 个月**
- 状态白话：稳妥 / 有点紧 / 偏危险

```csharp
float months = CashRunnelCalculator.GetRunwayMonths(cash, effectiveBurn);
string label = CashRunnelCalculator.FormatPlayerLabel(months);
string plain = CashRunnelCalculator.FormatWarningPlain(months); // 稳妥/有点紧/偏危险
```

### 3.3 ChemistryMatcher ✅
文件：`Assets/Scripts/Logic/ChemistryMatcher.cs`  
B-01~B-23；优先级：冲突 > 隐藏 > 多人≥4 > 三人 > 双人/单人；最多 6 个。

```csharp
List<string> triggered = ChemistryMatcher.Match(DataBase.Instance.GetBoardMemberIds());
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

> 负责人 / 董事完整名单见 **v1.2 存档** 或工程内 `DataBase.cs`（OPS-01 马格努斯·林德格伦 等）。验收中文时务必能显示此类姓名。

---

## 第 5 章：智能指挥中枢 API 契约（DeepSeek 接入）✅

> 无 Key / 超时 / 失败 → 本地话术库 `NeuralCommandMatcher`。  
> 顾问面向新手——大白话 + 量化「综合评定 XX 分」。

### 5.1 请求结构 ✅

**System Prompt（`DeepSeekConfig.PlainLanguageSystemPrompt`）：**
- 面向游戏新手；只用快照真实数字，禁止编造  
- 大白话；专业词必须括号解释  
- **message 结尾必须带：`综合评定XX分`**  
- ≤50 字中文；只输出 JSON  

**User Prompt：** 玩家输入 + `GameDataSnapshot`

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
2. 失败 → **本地话术**  
3. `EnsureRpmInMessage` / `FormatRpmFootnote`：自动补「综合评定 XX 分」  
4. **禁止**对玩家展示 `RPM70/绿/日利62.8` 类黑话  

### 5.4 工程文件
| 文件 | 说明 |
| :--- | :--- |
| `AI/AIChatManager.cs` | DeepSeek + 容灾 + 综合评定注脚 |
| `AI/DeepSeekConfig.cs` | 端点/模型/超时；通俗 System Prompt |
| `AI/GameDataSnapshot.cs` | 快照 + `FormatRpmFootnote` / `ToPlainRating` |
| `AI/AdvisorModels.cs` | JSON 模型 |
| `AI/NeuralCommandMatcher.cs` | 本地通俗话术 |
| `Resources/DeepSeekConfig.asset` | 配置资产 |

**配置 Key：** `AirlineCEO → Set DeepSeek API Key`（本机 PlayerPrefs，不进 Git）

---

## 第 6 章：MVP 垂直切片（第一财年）✅

### 6.1 P0 功能
1. 主界面：日期、现金、**现金跑道** ✅  
2. 确认首航 → 约 5 秒 → +15 万 ✅  
3. RPM：北京/伦敦 → 绿黄红 ✅  
4. Neural Command → DeepSeek / 本地容灾 ✅  
5. （v1.3）驾驶舱侧栏切换运营/市场/财务/AI ✅  

### 6.2 存档 ✅
`SaveManager`：`PlayerPrefs`；**F5** 存、**F9** 读  

### 6.3 Play 流程
1. 打开场景 → `Setup MVP Scene Objects` → 保存 → Play  
2. 首次：`Generate Chinese TMP Font Asset`（若尚未生成）  
3. 试玩：首航 → 分析航线 → 执行建议 → 执行季度运营 → F5/F9  

| 文件 | 说明 |
| :--- | :--- |
| `Systems/GameState.cs` | 现金 500、月烧 80、航线列表、油价等 |
| `Systems/SaveManager.cs` | F5/F9 |
| `UI/MVPBootstrap.cs` | 运行时搭驾驶舱 UI + 绑定 |

---

## 第 7 章：体验扩展路线图（Phase 1～6）✅

> **v1.3 核心章节。** 第 1～6 章骨架已稳；本章记录已验收的玩法与表现层扩展（至 Phase 6 续聘投票）。  
> 原则：先里后外、先功能后美观；AI 建议须玩家点「执行建议」才改状态。

### 7.0 Phase 总览

| Phase | 名称 | 状态 | 一句话 |
| :---: | :--- | :---: | :--- |
| 1 | AI 说话算话 | ✅ | 「执行建议」真实改现金/航线 |
| 2 | 财年心跳 | ✅ | 每季结算收入/支出，推进 Q1→Q2 |
| 3 | 航空驾驶舱 UI | ✅ | 深空蓝 HUD + 现金油量表 |
| 4 | 中文 TMP | ✅ | NotoSansSC，无方框；✈ 符号 Fallback |
| 5 | 董事会目标生成器 | ✅ | 年度考核清单 + Q4 结算弹窗 |
| 6 | 续聘投票 / 见习保护 | ✅ | VoteUI + YearEndEvaluator |
| 7+ | 融资 / 化学反应面板 / 美术 | ⬜ | 待开工 |

---

### 7.1 Phase 1：让 AI「说话算话」✅

**目标：** 点击「执行建议」后，现金 / 航线列表 / 满意度发生真实变化（**不自动执行**，需二次确认）。

| 文件 | 说明 |
| :--- | :--- |
| `Systems/ActionExecutor.cs` | 执行 open_route / delay / reject / check_cash；事件 `OnExecuted` |
| `Data/RouteData.cs` | 已开通航线记录 |
| `Systems/GameState.cs` | `activeRoutes`、`monthlyRevenue`、`EffectiveMonthlyBurn` |
| `UI/MVPBootstrap.cs` | 「执行建议」按钮 + 红字失败提示 |
| `AI/AIChatManager.cs` | `LastAdvice` |

**规则摘要：**
| 动作 | 效果 |
| :--- | :--- |
| `open_route` | 现金 ≥ **300** 万 → 扣启动费 → 加入 `activeRoutes`（默认北京-伦敦，日利取 `data.profit`）→ HUD 刷新 |
| 现金不足 | 红字：「现金不足，无法执行…」 |
| `delay` / `reject` | 决策日志 + 董事会满意度 **+2** |
| `first_flight` | 提示使用「确认首航」按钮 |
| `check_cash` | 回报现金、跑道白话、航线数 |

**验收数字（参考）：** 开通后现金 **500−300=215**；北京-伦敦月入约 **日利×30**（约 1883）；运营区出现航线卡片。

**验收步骤：** 聊天「分析航线」→ 点「执行建议」→ 现金 −300，出现已开通航线，顶部现金立刻变化。

---

### 7.2 Phase 2：建立「财年心跳」✅

**目标：** 每季度推进一次，自动结算收入与烧钱；可触发油价事件；现金吃紧时财务顾问警告。

| 文件 | 说明 |
| :--- | :--- |
| `Systems/TimeManager.cs` | `AdvanceQuarter()`、`OnYearEnd()`、油价随机事件 |
| `GameState.fuelPricePerTon` | 默认 850；可被事件改动 |
| `UI/MVPBootstrap.cs` | 「执行季度运营」、沙漏动画、现金跳动、破产弹窗 |

**结算公式：**
```
季度收入 = monthlyRevenue × 3
季度支出 = monthlyBurnRate × 3   // 机队维护、工资等固定烧钱
现金    += 收入 − 支出
季度    ：Q1 → Q2 → Q3 → Q4 → 下一年 OnYearEnd()
```

**其它规则：**
- 现金 &lt; 0 → **破产警告**弹窗  
- 现金跑道 &lt; 3 月 → 聊天区自动推送财务顾问警告，可点「执行建议」暂缓（delay）  

**验收：** 点「执行季度运营」→ 弹窗显示收入/支出/净额 → 日期 Q1 变 Q2（可连点至 Q4）→ 有航线时现金明显上升。

---

### 7.3 Phase 3：UI 打磨（航空驾驶舱）✅

**目标：** 从灰白测试块变为深色航空 HUD（对齐 GDD **33.6**）。

#### 视觉规范（节选）
| 用途 | 色值 |
| :--- | :--- |
| 主背景 | `#0B1A33` |
| 面板 | `#152A4A` / `#1E3A5F` |
| 强调 | `#1A5CFF` |
| 成功 / 警告 / 危险 | `#00C853` / `#FFD600` / `#D50000` |
| 主文本 / 次文本 | `#F0F4F8` / `#A0B4C8` |

#### 布局
1. **顶栏：** 公司名（含飞机符）· 日期季度 ·「执行季度运营」· **现金油量表（指针）**  
2. **左侧导航：** 运营 / 市场 / 财务 / AI 顾问（半透明毛玻璃）  
3. **中央：** 默认「市场 · RPM 航线分析」；运营页显示航线卡片与首航  
4. **右下：** Neural Command 聊天气泡  

| 文件 | 说明 |
| :--- | :--- |
| `UI/UITheme.cs` | 配色常量 |
| `UI/UIFactory.cs` | 圆角面板、TMP 控件、悬停提亮 |
| `UI/UIHoverBrighten.cs` | 按钮悬停变亮 |
| `UI/CashMeter.cs` | 横向油量表 + 指针动画 |
| `UI/RouteCard.cs` | 航线信息卡 |
| `UI/ChatBubble.cs` | AI 气泡外观 |
| `UI/MVPBootstrap.cs` | 组装驾驶舱 |
| `Editor/CreateUIPrefabs.cs` | 可选生成 Prefab → `Resources/UI/` |

**验收：** 启动即为深空蓝驾驶舱；首航或推进季度时，油量表填充与指针随现金移动。

---

### 7.4 Phase 4：中文字体与本地化（TMP）✅

**目标：** TextMeshPro 完美显示中文；姓名、城市、顾问回复无方框 □。

| 文件 / 资产 | 说明 |
| :--- | :--- |
| `Assets/Fonts/NotoSansSC-Regular.otf` | 思源黑体 / Noto Sans SC（**OFL**，可进 Git） |
| `Resources/Fonts/NotoSansSC-SDF.asset` | Dynamic TMP 字体资源 |
| `UI/ChineseFontProvider.cs` | 加载 SDF；符号 Fallback；公司名前缀 ✈ /「航」 |
| `UI/UIFactory.cs` | 全面 `TextMeshProUGUI` / `TMP_InputField`；字号相对英文 +2 |
| `Editor/GenerateChineseTMPFont.cs` | 菜单生成 SDF 并尝试设为 TMP 默认 |
| `Editor/SymbolFontImportSettings.cs` | 符号字体导入时 Include Font Data |

**重要说明（飞机符 ✈）：**  
Noto Sans SC **不含** U+2708。运行时用本机拷贝的 `SegoeUISymbol.ttf` 作 TMP Fallback（**微软字体，勿商业分发、默认不强制进公开仓库**）。若 Fallback 失败，顶栏自动显示 **「航 天际航空 · CEO」**，保证无方框。

**菜单：** `AirlineCEO → Generate Chinese TMP Font Asset`

**动态文案：** `GameDataSnapshot.FormatRpmFootnote()` 等中文经 TMP 渲染。

**验收：**
1. 「马格努斯·林德格伦」「北京 / 伦敦」清晰  
2. 顾问回复与「综合评定 XX 分」清晰  
3. 顶栏飞机符或「航」字正常，无 □  
4. 左侧「AI 顾问」可见字体检收样例行  

---

### 7.5 Phase 5：董事会目标生成器 ✅

**目标：** 第 1 财年下达 3～4 项年度考核目标；董事会页可追踪进度；Q4 结束后弹出年度结算（完成率）。

| 文件 | 说明 |
| :--- | :--- |
| `Data/BoardTarget.cs` | 目标数据（净利润 / 客座率 / 安全 / 航线扩张） |
| `Logic/TargetGenerator.cs` | ChemistryMatcher + 保底目标，生成 3～4 项 |
| `Systems/BoardManager.cs` | 12 董事满意度；Q1 生成目标；年末考核 |
| `UI/BoardTargetUI.cs` | 左侧「董事会」页：清单 + 红/黄/绿进度条 |
| `Systems/TimeManager.cs` | `OnYearEnd` 调用考核；累计 `yearNetProfit` |
| `Systems/GameState.cs` | `currentTargets`、客座率/安全指数等 |

**规则摘要：**
- 开局（第 1 年 Q1）生成目标，写入 `GameState.currentTargets`
- 保底：净利润 ≥ 50 万、客座率 ≥ 75%（不足时补安全 / 扩张）
- 进入 **Q2** 时弹窗「董事会发来年度目标清单」（强化 30～60 分钟节点感）
- 完成率 = 已完成数 / 总数；≥60% 视为续聘达标线

**验收：**
1. 第 1 年 Q1，左侧「董事会」可见 3～4 项目标，如「净利润 ≥ 50 万（当前 X 万）」
2. Q4 结束后弹出「年度结算」，显示完成率（如 67%）

### 7.6 Phase 6：年度结算与续聘投票 ✅

**目标：** 财年末按完成率续聘投票；见习期（第 1～3 年）未达标再给一年；第 4 年起未达标解雇 Game Over。

| 文件 | 说明 |
| :--- | :--- |
| `Logic/YearEndEvaluator.cs` | 完成率、续聘线（普通 60%）、投票结果 |
| `UI/VoteUI.cs` | 续聘成功 / 见习保护 / 解雇弹窗 |
| `Systems/TimeManager.cs` | `OnYearEnd` 调用 Evaluator，再重置目标或结束 |
| `Systems/SaveManager.cs` | 存难度、目标 JSON、gameOver 等 |
| `Systems/GameState.cs` | `difficulty`、`gameOver`、见习保护计数 |

**规则：**
| 条件 | 结果 |
| :--- | :--- |
| 完成率 ≥ 续聘线（普通 60%） | **续聘成功** → 重置目标，进入下一财年 |
| 未达标 且 结束年 ≤ 3 | **见习保护**：文案「董事会决定再给一年机会」→ 继续下一财年 |
| 未达标 且 结束年 ≥ 4 | **董事会解雇** → Game Over，停止季度推进 |

**验收：**
1. 完成率 ≥ 60% → VoteUI「续聘成功」，进入新年  
2. 第 1～3 年未达标 →「再给一年机会（见习保护）」  
3. 第 4 年起未达标 →「董事会解雇」并结束  

### 7.7 Phase 7：安全事件与航权系统 ✅

**对应：** MVP GDD 7.6（SAF）/ 7.7（STG）· `docs/GameDesignBible/MVP/Phase 7.docx`

| 文件 | 职责 |
| :--- | :--- |
| `Data/SafetyTypes.cs` | `SafetyRating` A/B/C/D、事件日志结构 |
| `Systems/SafetyEventManager.cs` | 每季 **10%** 概率触发轻度/中度事件，降低 `safetyRating` |
| `UI/SafetyUI.cs` | 左侧「安全」面板：评级 + 事件日志 |
| `Systems/RouteRightManager.cs` | 长航线航权审批：通过率 = **70% − 竞对占比 × 30%** |
| `Systems/ActionExecutor.cs` | 开通前调用航权；失败提示「航权被拒，暂无法开通」 |
| `Systems/TimeManager.cs` | 季度结算时掷安全事件，写入 `eventText` |
| `Systems/GameState.cs` | `safetyRating`（初始 **B**）、`safetyEventLog` |

**航权公式（北京-伦敦竞对约 65%）：**  
`passRate = clamp01(0.70 − 0.65 × 0.30) ≈ 50.5%`；安全评级 C/D 再 −10%。

**验收：**
1. 开局左侧「安全」显示评级 **B**；推进多个季度后可能降至 **C**（结算弹窗/事件日志可见）
2. AI「执行建议」开通北京-伦敦：有一定概率失败并提示 **「航权被拒，暂无法开通」**（现金不扣）
3. 清档用 **AirlineCEO → 清档 Clear Save**（含 `ACEO_Safety*`）

### 7.8 Phase 8+（下一阶段 · 未开工）⬜

候选：化学反应可视化、融资/机队可交互、美术精修、事故调查深度选项。

---

## 附录 A：玩家文案规范

| 场景 | 推荐说法 | 避免 |
| :--- | :--- | :--- |
| 现金可撑多久 | 现金跑道：按现在开支，流动性还可以撑 X 个月 | 只说「跑道 6.3 月」 |
| 资金状态 | 目前还算稳妥 / 有点紧 / 偏危险 | 只甩绿黄红字母给新手 |
| 航线评价 | 比较划算 / 先观望 / 暂缓 + **综合评定 XX 分** | `RPM70/绿/日利62.8` |
| 竞争 | 对手航班已经很多 | 竞对运力压迫系数 |

---

## 附录 B：开发排期

| 阶段 | 核心产出 | 状态 |
| :--- | :--- | :---: |
| 数据层 | Data + DataBase | ✅ |
| 逻辑层 | RPM / Cash / Chemistry | ✅ |
| 核心 UI | 首航 + 流动性 | ✅ |
| AI 接口 | DeepSeek + 容灾 | ✅ |
| 文案打磨 | 通俗化 + 综合评定 | ✅ |
| Phase 1 | ActionExecutor / 执行建议 | ✅ |
| Phase 2 | TimeManager / 季度结算 | ✅ |
| Phase 3 | 驾驶舱 HUD / CashMeter | ✅ |
| Phase 4 | NotoSansSC TMP / 符号 Fallback | ✅ |
| Phase 5 | 董事会目标 / 年度结算 | ✅ |
| Phase 6 | 续聘投票 / 见习保护 / GameOver | ✅ |
| Phase 7 | 安全事件 / 航权审批 / 安全面板 | ✅ |
| Phase 8+ | 化学反应面板 / 融资机队 / 美术 | ⬜ |

---

## 附录 C：给 Cursor 的续作提示词模板

```text
请严格按 docs/Development Manual/Airline_CEO_Dev_Task_v1.3.md 第 7.8 节开工。
不要重复实现 Phase 1～7。工程根目录是 Unity Project/。
完成后更新任务书附录 B 状态，并说明验收步骤。
```

---

**最后建议：**  
v1.3 已归档。请改用 **v1.4**。遇报错把 Console 红字发给 Cursor / Kimi。
