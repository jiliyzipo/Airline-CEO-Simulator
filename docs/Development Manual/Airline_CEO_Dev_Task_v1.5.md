# ✈️ Airline CEO Simulator · 技术任务书 v1.5
**（面向 Cursor + DeepSeek API 的 AI 编码实施蓝图）**

**适用对象：** 独立开发者（无编程基础，依赖 AI 编程）  
**核心目标：** 将 GDD 策划案转化为 Unity C# 可执行代码的精确指令。  
**工程根目录：** `Unity Project/`  
**现行版本：** 请以 **v1.5** 为准（v1.0～v1.4 仅作历史存档）  
**章节对齐：** 与 [`Airline_CEO_Dev_Roadmap_v1.5.md`](../GameDesignBible/MVP/Airline_CEO_Dev_Roadmap_v1.5.md) **一～四** 同序同名

---

## 文档修订记录

| 版本 | 说明 |
| :--- | :--- |
| v1.0 | 初版施工图（伪代码为主） |
| v1.1 | RPM 完整实现；数据/逻辑/AI/首航落地 |
| v1.2 | 玩家文案通俗化；综合评定；Phase 1：ActionExecutor |
| v1.3 | 体验扩展 Phase 1～6（续聘投票） |
| v1.4 | Phase 7 / AOG / 章节对齐路线图；AOG Play 验收 |
| **v1.5** | **战略性扩张提案（§2.13）Play 验收 ✅**；AOG+扩张均已打勾；现行施工图 |

---

## 章节对照（任务书 ↔ 路线图）

| 任务书 | 路线图 | 内容 |
| :--- | :--- | :--- |
| **第 1 章** | 一、MVP 开发总览 | 按玩家体验时间线 / GDD 核心循环 |
| **第 2 章** | 二、技术实现阶段清单 | 数据层 → … → Phase 7 → Phase 8+（施工细节） |
| **第 3 章** | 三、详细功能对照 | GDD 功能点 ↔ Phase ↔ 状态 |
| **第 4 章** | 四、里程碑概览 | 0～120 分钟节点摘要 |
| 附录 A～C | — | 文案规范 / Cursor 提示词 / 菜单与 Play |

**图例：** ✅ 已完成并验收 ｜ ⬜ 待开发

---

## 第 1 章：MVP 开发总览（按 GDD 核心循环）

> 对应路线图 **「一」**。按玩家体验节点组织；实现细节见第 2 章。

| 玩家体验节点 | 对应 GDD 章节 | 核心功能 | 完成状态 | 任务书施工节 |
| :--- | :--- | :--- | :---: | :--- |
| 0～15分钟（首航体验） | 第3章（首航任务）<br>第7.1～7.4（OPS/MKT/FIN） | 首航任务、现金跑道、基础定价、RPM评估 | ✅ 完成 | §2.1～2.4、§2.7～2.8 |
| 15～30分钟（危机应对） | 第7.2（AOG危机） | 脚本化 AOG 危机（故障处置） | ✅ 已验收 | §2.12 |
| 30～60分钟（首次考核） | 第5章（董事会目标）<br>第4章（年度结算） | 董事会目标生成、进度追踪、年度结算与续聘投票 | ✅ 完成（Phase 5～6） | §2.9～2.10 |
| 60～120分钟（战略纵深） | 第7.6（SAF）<br>第7.7（STG） | 安全事件、航权审批、扩张提案 | ✅ 完成（Phase 7 + 扩张提案） | §2.11、§2.13 |

---

## 第 2 章：技术实现阶段清单

> 对应路线图 **「二」**。下表为总览；其后各节为可施工细节（顺序与路线图一致）。

| 阶段 | 核心产出 | 对应文件 / 系统 | 状态 | 本节 |
| :--- | :--- | :--- | :---: | :---: |
| 数据层 | 数据模型 + DataBase | AircraftData, ExecutiveData, BoardMemberData, DataBase.cs | ✅ | 2.1 |
| 逻辑层 | RPM / CashRunnel / Chemistry | RPMCalculator, CashRunnelCalculator, ChemistryMatcher | ✅ | 2.2 |
| 核心 UI | 首航 + 流动性显示 | MVPBootstrap, 基础 HUD | ✅ | 2.3 |
| AI 接口 | DeepSeek + 本地容灾 | AIChatManager, NeuralCommandMatcher | ✅ | 2.4 |
| Phase 1 | AI 执行建议（ActionExecutor） | ActionExecutor, RouteData, 执行按钮 | ✅ | 2.5 |
| Phase 2 | 财年心跳（季度结算） | TimeManager, 季度推进按钮 | ✅ | 2.6 |
| Phase 3 | 航空驾驶舱 UI | UITheme, CashMeter, 深色 HUD | ✅ | 2.7 |
| Phase 4 | 中文字体 (TMP) | ChineseFontProvider, NotoSansSC | ✅ | 2.8 |
| Phase 5 | 董事会目标生成器 | BoardManager, TargetGenerator, BoardTargetUI | ✅ | 2.9 |
| Phase 6 | 年度结算与续聘投票 | YearEndEvaluator, VoteUI, 见习保护 | ✅ | 2.10 |
| Phase 7 | 安全事件与航权系统 | SafetyEventManager, RouteRightManager, SafetyUI | ✅ | 2.11 |
| **AOG 危机** | **脚本化 AOG（GDD 7.2）** | **AOGManager, AOGUI** | **✅ 已验收** | **2.12** |
| **扩张提案** | **战略性扩张提案（GDD 7.7）** | **ExpansionProposalManager, ExpansionProposalUI** | **✅ 已验收** | **2.13** |
| Phase 8+ | 化学反应可视化 / 融资机队 / 美术精修 | 待定 | ⬜ | 2.14 |

**工程骨架（五文件夹）：**

| 文件夹路径 | 命名空间 | 存放内容 | MVP 状态 |
| :--- | :--- | :--- | :--- |
| `Data/` | `AirlineCEO.Data` | 数据模型 + DataBase + RouteData + SafetyTypes | ✅ |
| `Logic/` | `AirlineCEO.Logic` | RPM / 现金跑道 / 化学反应 / Target / YearEnd | ✅ |
| `Systems/` | `AirlineCEO.Systems` | GameState、SaveManager、ActionExecutor、TimeManager、BoardManager、Safety / RouteRight / AOG / ExpansionProposal | ✅ |
| `UI/` | `AirlineCEO.UI` | 驾驶舱 HUD、TMP、SafetyUI、VoteUI、AOGUI、ExpansionProposalUI 等 | ✅ |
| `AI/` | `AirlineCEO.AI` | DeepSeek + 本地话术 | ✅ |

建议资产目录：`Assets/ScriptableObjects/{Aircraft,Executives,BoardMembers}/`  
字体目录：`Assets/Fonts/`、`Assets/Resources/Fonts/`

原则：先里后外、先功能后美观；AI 建议须玩家点「执行建议」才改状态。

---

### 2.1 数据层 ✅

> Unity 右键：`Create → AirlineCEO → Data → …`

#### AircraftData · `Assets/Scripts/Data/AircraftData.cs`
```csharp
public class AircraftData : ScriptableObject {
    public string aircraftID;      // 如 "A320neo"
    public int maxPassengers;
    public float maxRange;         // 公里
    public float basePrice;        // 万美元
    public float fuelBurnPerHour;  // 吨/小时
}
```

#### ExecutiveData · `Assets/Scripts/Data/ExecutiveData.cs`
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

#### BoardMemberData · `Assets/Scripts/Data/BoardMemberData.cs`
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

#### RouteData · `Assets/Scripts/Data/RouteData.cs`（Phase 1）
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

#### DataBase · `Assets/Scripts/Data/DataBase.cs`
`Awake()` 初始化；ScriptableObject 用 `CreateInstance`，不能 `new`。

```csharp
DataBase.Instance.Executives;          // 21
DataBase.Instance.BoardMembers;        // 12
DataBase.Instance.GetBoardMemberIds(); // 给 ChemistryMatcher
```

> 负责人 / 董事完整名单见工程内 `DataBase.cs`。验收中文时务必能显示「马格努斯·林德格伦」等姓名。  
> Console：`[DataBase] 已加载负责人 21 人，董事 12 人。`

---

### 2.2 逻辑层 ✅

> 菜单：`AirlineCEO → Run Logic Self Tests`

#### RPMCalculator ✅
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
```

#### CashRunnelCalculator ✅
文件：`Assets/Scripts/Logic/CashRunnelCalculator.cs`

**玩家可见叫法（勿只写「跑道 X 月」）：**
- 标题：**现金跑道**
- 说明：**按现在开支，流动性还可以撑 X 个月**
- 状态白话：稳妥 / 有点紧 / 偏危险

```csharp
float months = CashRunnelCalculator.GetRunwayMonths(cash, effectiveBurn);
string label = CashRunnelCalculator.FormatPlayerLabel(months);
string plain = CashRunnelCalculator.FormatWarningPlain(months);
```

#### ChemistryMatcher ✅
文件：`Assets/Scripts/Logic/ChemistryMatcher.cs`  
B-01~B-23；优先级：冲突 > 隐藏 > 多人≥4 > 三人 > 双人/单人；最多 6 个。

```csharp
List<string> triggered = ChemistryMatcher.Match(DataBase.Instance.GetBoardMemberIds());
```

---

### 2.3 核心 UI（首航 + 流动性）✅

对应玩家节点 **0～15 分钟**。

#### P0 功能
1. 主界面：日期、现金、**现金跑道** ✅  
2. 确认首航 → 约 5 秒 → +15 万 ✅  
3. RPM：北京/伦敦 → 绿黄红 ✅  
4. Neural Command → DeepSeek / 本地容灾 ✅  
5. 驾驶舱侧栏：运营 / 市场 / 董事会 / 安全 / 财务 / AI ✅  

#### 存档 ✅
`SaveManager`：`PlayerPrefs`；**F5** 存、**F9** 读  

#### Play 流程
1. 打开场景 → `Setup MVP Scene Objects` → 保存 → Play  
2. 首次：`Generate Chinese TMP Font Asset`（若尚未生成）  
3. 试玩：首航 → 分析航线 → 执行建议 → 执行季度运营 → F5/F9  

| 文件 | 说明 |
| :--- | :--- |
| `Systems/GameState.cs` | 现金 500、月烧 80、航线列表、油价等 |
| `Systems/SaveManager.cs` | F5/F9 |
| `UI/MVPBootstrap.cs` | 运行时搭驾驶舱 UI + 绑定 |

---

### 2.4 AI 接口（DeepSeek + 本地容灾）✅

> 无 Key / 超时 / 失败 → 本地话术库 `NeuralCommandMatcher`。  
> 顾问面向新手——大白话 + 量化「综合评定 XX 分」。

#### 请求结构 ✅

**System Prompt（`DeepSeekConfig.PlainLanguageSystemPrompt`）：**
- 面向游戏新手；只用快照真实数字，禁止编造  
- 大白话；专业词必须括号解释  
- **message 结尾必须带：`综合评定XX分`**  
- ≤50 字中文；只输出 JSON  

**User Prompt：** 玩家输入 + `GameDataSnapshot`

#### 响应结构（必须 JSON）✅
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

#### 容灾与强制注脚 ✅
1. **3 秒超时**  
2. 失败 → **本地话术**  
3. `EnsureRpmInMessage` / `FormatRpmFootnote`：自动补「综合评定 XX 分」  
4. **禁止**对玩家展示 `RPM70/绿/日利62.8` 类黑话  

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

### 2.5 Phase 1：AI 执行建议（ActionExecutor）✅

**目标：** 点击「执行建议」后，现金 / 航线列表 / 满意度发生真实变化（**不自动执行**，需二次确认）。

| 文件 | 说明 |
| :--- | :--- |
| `Systems/ActionExecutor.cs` | 执行 open_route / delay / reject / check_cash；事件 `OnExecuted` |
| `Data/RouteData.cs` | 已开通航线记录 |
| `Systems/GameState.cs` | `activeRoutes`、`monthlyRevenue`、`EffectiveMonthlyBurn` |
| `UI/MVPBootstrap.cs` | 「执行建议」按钮 + 失败/说明提示 |
| `AI/AIChatManager.cs` | `LastAdvice` |

**规则摘要：**
| 动作 | 效果 |
| :--- | :--- |
| `open_route` | 现金 ≥ **300** 万 →（长航线先航权，见 §2.11）→ 扣启动费 → 加入 `activeRoutes` → HUD 刷新 |
| 现金不足 | 「现金不足，无法执行…」 |
| `delay` / `reject` | 决策日志 + 董事会满意度 **+2** |
| `first_flight` | 提示使用「确认首航」按钮 |
| `check_cash` | 回报现金、跑道白话、航线数 |

**验收：** 聊天「分析航线」→「执行建议」→ 现金 −300（或航权被拒不扣钱），顶部现金立刻变化。

---

### 2.6 Phase 2：财年心跳（季度结算）✅

**目标：** 每季度推进一次，自动结算收入与烧钱；可触发油价 / 安全事件；现金吃紧时财务顾问警告。

| 文件 | 说明 |
| :--- | :--- |
| `Systems/TimeManager.cs` | `AdvanceQuarter()`、`OnYearEnd()`、油价随机事件、调用安全事件 |
| `GameState.fuelPricePerTon` | 默认 850；可被事件改动 |
| `UI/MVPBootstrap.cs` | 「执行季度运营」、沙漏动画、现金跳动、破产弹窗 |

**结算公式：**
```
季度收入 = monthlyRevenue × 3
季度支出 = monthlyBurnRate × 3
现金    += 收入 − 支出
季度    ：Q1 → Q2 → Q3 → Q4 → 下一年 OnYearEnd()
```

**其它规则：**
- 现金 &lt; 0 → **破产警告**弹窗  
- 现金跑道 &lt; 3 月 → 聊天区自动推送财务顾问警告  

**验收：**「执行季度运营」→ 显示收入/支出/净额 → 日期推进；有航线时现金明显上升。

---

### 2.7 Phase 3：航空驾驶舱 UI ✅

**目标：** 深色航空 HUD（对齐 GDD **33.6**）。

#### 视觉规范（节选）
| 用途 | 色值 |
| :--- | :--- |
| 主背景 | `#0B1A33` |
| 面板 | `#152A4A` / `#1E3A5F` |
| 强调 | `#1A5CFF` |
| 成功 / 警告 / 危险 | `#00C853` / `#FFD600` / `#D50000` |
| 主文本 / 次文本 | `#F0F4F8` / `#A0B4C8` |

#### 布局
1. **顶栏：** 公司名 · 日期季度 ·「执行季度运营」· **现金油量表**  
2. **左侧导航：** 运营 / 市场 / 董事会 / 安全 / 财务 / AI 顾问  
3. **中央：** 各面板；默认「市场 · RPM」  
4. **右下：** Neural Command 聊天气泡  

| 文件 | 说明 |
| :--- | :--- |
| `UI/UITheme.cs` | 配色常量 |
| `UI/UIFactory.cs` | 圆角面板、TMP 控件、悬停提亮 |
| `UI/CashMeter.cs` | 横向油量表 + 指针动画 |
| `UI/RouteCard.cs` | 航线信息卡 |
| `UI/ChatBubble.cs` | AI 气泡外观 |
| `UI/MVPBootstrap.cs` | 组装驾驶舱 |
| `Editor/CreateUIPrefabs.cs` | 可选 Prefab → `Resources/UI/` |

**验收：** 启动即为深空蓝驾驶舱；现金变化时油量表指针移动。

---

### 2.8 Phase 4：中文字体 (TMP) ✅

**目标：** TextMeshPro 完美显示中文；姓名、城市、顾问回复无方框 □。

| 文件 / 资产 | 说明 |
| :--- | :--- |
| `Assets/Fonts/NotoSansSC-Regular.otf` | Noto Sans SC（**OFL**，可进 Git） |
| `Resources/Fonts/NotoSansSC-SDF.asset` | Dynamic TMP 字体资源 |
| `UI/ChineseFontProvider.cs` | 加载 SDF；符号 Fallback；公司名前缀 ✈ /「航」 |
| `Editor/GenerateChineseTMPFont.cs` | 菜单生成 SDF |

**飞机符 ✈：** Noto Sans SC 不含 U+2708；运行时用本机 `SegoeUISymbol.ttf` Fallback（**勿商业分发**）。失败则显示「航 天际航空 · CEO」。

**验收：**「马格努斯·林德格伦」「北京 / 伦敦」「综合评定 XX 分」清晰；顶栏无 □。  
Console：`[ChineseFontProvider] 已加载 Resources/Fonts/NotoSansSC-SDF`

---

### 2.9 Phase 5：董事会目标生成器 ✅

对应玩家节点 **30～60 分钟**（考核前半）。

**目标：** 财年下达年度考核目标；董事会页追踪进度。

| 文件 | 说明 |
| :--- | :--- |
| `Data/BoardTarget.cs` | 净利润 / 客座率 / 安全 / 航线扩张 |
| `Logic/TargetGenerator.cs` | ChemistryMatcher + 保底目标 |
| `Systems/BoardManager.cs` | 董事满意度；生成目标；年末考核 |
| `UI/BoardTargetUI.cs` | 左侧「董事会」：清单 + 红/黄/绿进度条 |
| `Systems/GameState.cs` | `currentTargets`、客座率/安全指数等 |

**规则摘要：**
- 开局（第 1 年 Q1）生成目标（固定 **3** 项）
- 保底：净利润 ≥ 50 万、客座率 ≥ 75%（不足时补安全 / 扩张）
- 进入 **Q2** 时弹窗「董事会发来年度目标清单」
- 完成率 = 已完成数 / 总数；≥60% 为续聘达标线（见 Phase 6）

**验收：** 左侧「董事会」可见目标与进度。

---

### 2.10 Phase 6：年度结算与续聘投票 ✅

对应玩家节点 **30～60 分钟**（考核后半）。

**目标：** 财年末按完成率续聘；见习期（1～3 年）保护；第 4 年起未达标 Game Over。

| 文件 | 说明 |
| :--- | :--- |
| `Logic/YearEndEvaluator.cs` | 完成率、续聘线（普通 60%）、投票结果 |
| `UI/VoteUI.cs` | 续聘成功 / 见习保护 / 解雇弹窗 |
| `Systems/TimeManager.cs` | `OnYearEnd` 先结算再重置目标 |
| `Systems/SaveManager.cs` | 存难度、目标 JSON、gameOver 等 |
| `Systems/GameState.cs` | `difficulty`、`gameOver`、见习保护计数 |

| 条件 | 结果 |
| :--- | :--- |
| 完成率 ≥ 续聘线（普通 60%） | **续聘成功** → 进入下一财年 |
| 未达标 且 结束年 ≤ 3 | **见习保护**：「再给一年机会」 |
| 未达标 且 结束年 ≥ 4 | **董事会解雇** → Game Over |

**验收：** ≥60% →「续聘成功」；1～3 年未达标 → 见习保护；≥4 年未达标 → 解雇。

---

### 2.11 Phase 7：安全事件与航权系统 ✅

对应玩家节点 **60～120 分钟**。  
GDD **7.6 SAF** / **7.7 STG** · `docs/GameDesignBible/MVP/Phase 7.docx`

#### 目标与验收

| 序号 | 验收项 | 结果 |
| :---: | :--- | :---: |
| 1 | 左侧「安全」开局评级 **B** | ✅ |
| 2 | 事件后评级可降至 **C/D**，日志可见 | ✅ |
| 3 | 开通前掷航权；失败提示 **「航权被拒，暂无法开通」**（现金不扣） | ✅ |
| 4 | 通过后出现航线；再执行提示已开通 | ✅ |

#### 任务清单

| 序号 | 任务 | 文件 | 说明 |
| :---: | :--- | :--- | :--- |
| 7.1 | SafetyEventManager | `Systems/SafetyEventManager.cs` | 每季 **10%** 轻度/中度事件 |
| 7.2 | SafetyUI | `UI/SafetyUI.cs` | 评级 A/B/C/D + 事件历史 |
| 7.3 | RouteRightManager | `Systems/RouteRightManager.cs` | 通过率公式见下 |
| 7.4 | ActionExecutor 接入 | `Systems/ActionExecutor.cs` | 开通前审批 |

#### 安全评级（SAF）

| 项 | 规则 |
| :--- | :--- |
| 初始 | `safetyRating = B`，`safetyIndex` 同步（B→82） |
| 触发 | `TimeManager` 调用 `TryRollQuarterEvent` |
| 轻度 / 中度 | 降 1 级；中度偶发 B→D，满意度 −3 |
| 下限 | 不低于 **D** |
| 存档 | `ACEO_SafetyRating`、`ACEO_SafetyLogJson` |

数据：`Data/SafetyTypes.cs`

#### 航权审批（STG）

```
passRate = clamp01(0.70 − competitorShare × 0.30)
```

| 航线 | 竞对占比 | 约通过率 |
| :--- | :---: | :---: |
| 北京-伦敦 | 65% | **≈ 50.5%**（UI 约 51%） |
| 深圳-洛杉矶 | 55% | ≈ 53.5% |

- `safetyRating ≥ C` 时通过率再 **−10%**  
- 失败可立刻再点「执行建议」重掷；**已开通不再掷航权**  
- 复测：`AirlineCEO → Debug → 移除北京-伦敦航线（复测航权）`

#### 工程接线

| 文件 | 要点 |
| :--- | :--- |
| `GameState.cs` | `safetyRating`、`safetyEventLog` |
| `BoardManager.cs` | 安全指数跟随评级 |
| `TimeManager.cs` | 拼接安全事件文案 |
| `SaveManager.cs` | 存读评级与日志 |
| `MVPBootstrap.cs` | 左导航「安全」 |
| `SetupMVPScene.cs` | Ensure 两 Manager |

**扩张提案（GDD 7.7）：** ✅ 见 §2.13

---

### 2.12 脚本化 AOG 危机（GDD 7.2）✅ 已验收

对应玩家节点 **15～30 分钟**。首航完成后，第 1 财年 **Q2** 开始检查；若未触发则 **Q3** 强制触发；生命周期仅一次。

> **Play 验收（2026-08-01）：** Q2 触发弹窗 → 选择调机（选项 1）→ `hasAOGTriggered=true`、`timeScale=1`、自动存档；危机不再重复出现。

| 文件 | 说明 |
| :--- | :--- |
| `Systems/GameState.cs` | `hasAOGTriggered`、`aogChoice`、`aogAdvisorConsulted`、`onTimePercent` |
| `Systems/AOGManager.cs` | `ShouldTriggerAOG` / `TriggerAOG` / `OnAOGChoiceMade`；`Time.timeScale=0` |
| `UI/AOGUI.cs` | 遮罩 + 三按钮；选项 3 顾问说明后只留 1/2 |
| `Systems/TimeManager.cs` | 推进后 `pendingAOG`；Bootstrap 结算动画结束后 `TriggerAOG` |
| `Systems/SaveManager.cs` | `ACEO_HasAOGTriggered` / `ACEO_AOGChoice` 等 |
| `UI/MVPBootstrap.cs` | 创建 AOGUI 并注入 AOGManager |

| 选项 | 成本 | 效果 | 按钮色 |
| :---: | :--- | :--- | :--- |
| 1 调机备援 | −20 万 | 准点，满意度不变 | 橙 |
| 2 延误维修 | −15 万 | 准点率↓，满意度 −3 | 黄 |
| 3 呼叫顾问 | 0 | 说明后必须再选 1 或 2 | 蓝 |

**验收标准：** 首航 → 推进至 Q2（或 Q3）→ 弹出 AOG → 三选一生效；F5/F9 保留触发标志。调试：`AirlineCEO → Debug → Force AOG Crisis`。  
**验收结果：** ✅ 已打勾（2026-08-01）

---

### 2.13 战略性扩张提案（GDD 7.7）✅ 已验收

对应玩家节点 **60～120 分钟**。第 **4** 财年 **Q1** 触发（错过则 **Q2** 强制）；暂缓最多 **3** 次（下年 Q1 再弹）。

> **Play 验收（2026-08-01）：** 第 4 年 Q1 弹出 → **暂缓**（delay=1）→ 第 5 年 Q1 再次弹出 → **拒绝**（rejected=true，不再触发）。

| 文件 | 说明 |
| :--- | :--- |
| `Systems/GameState.cs` | `hasExpansionProposalTriggered`、`expansionProposalDelayCount`、`expansionAccepted` / `Rejected`、`greatRouteUnlocked` |
| `Systems/ExpansionProposalManager.cs` | 触发 / 接受扣 500 万开通北京→纽约 / 暂缓 / 拒绝 |
| `UI/ExpansionProposalUI.cs` | 蓝金机遇弹窗 + 三列数据卡 + 绿/黄/红按钮 |
| `Systems/TimeManager.cs` | `pendingExpansionProposal` |
| `Systems/SaveManager.cs` | `ACEO_HasExpansionProposalTriggered` 等 |
| `UI/MVPBootstrap.cs` | 挂载 UI 与 Manager |

| 选项 | 效果 |
| :---: | :--- |
| 1 接受 | −500 万，开通北京→纽约（日利 8.5），满意度 +10，解锁「伟大航路」 |
| 2 暂缓 | 不扣钱，满意度 −3，下年 Q1 再弹（≤3 次） |
| 3 拒绝 | 不扣钱，满意度 −8，不再提案 |

**验收标准：** 调试菜单 `AirlineCEO → Debug → Force Expansion Proposal`；或推进至第 4 年 Q1。现金不足时接受会提示并保持弹窗。  
**验收结果：** ✅ 已打勾（2026-08-01）

---

### 2.14 Phase 8+（下一阶段 · 未开工）⬜

| 候选 | 对应路线图 / GDD | 状态 |
| :--- | :--- | :---: |
| 部门管理 / 任命 | GDD 第6章 | ⬜ |
| 燃油对冲 / 融资 | GDD 7.4 | ⬜ |
| 化学反应可视化 | ChemistryMatcher UI | ⬜ |
| 机队可交互 / 美术精修 | — | ⬜ |
| 事故调查深度选项 | SAF 快速/标准/深度 | ⬜ |

---

### 2.15 编辑器工具与清档 ✅

> 辅助节（非路线图阶段表行项）。避免 `Edit → Clear All PlayerPrefs` 误删 API Key。

| 菜单 | 文件 | 说明 |
| :--- | :--- | :--- |
| `AirlineCEO/清档 Clear Save` | `Editor/ClearGameSaveMenu.cs` | 只删 `ACEO_*`，**保留** API Key |
| `Debug/Force Mild|Moderate Safety Event` | `Editor/Phase7DebugMenu.cs` | Play 中强制安全事件 |
| `Debug/移除北京-伦敦航线（复测航权）` | 同上 | 复测航权被拒 |
| `Setup MVP Scene Objects` | `Editor/SetupMVPScene.cs` | 含 Safety / RouteRight |
| `Set DeepSeek API Key` | `SetupMVPScene.cs` | 本机 Key |
| `Generate Chinese TMP Font Asset` | `GenerateChineseTMPFont.cs` | 中文 SDF |
| `Run Logic Self Tests` | `LogicSelfTests.cs` | RPM/跑道自测 |
| `Batch Create Data Assets` | `BatchCreateDataAssets.cs` | 批量 SO |

---

## 第 3 章：详细功能对照（GDD 章节 ↔ 开发状态）

> 对应路线图 **「三」**。

| GDD 功能点 | 实现方式 | 对应 Phase | 状态 | 任务书节 |
| :--- | :--- | :---: | :---: | :---: |
| 首航任务（第3.5节） | 一键确认 + 动画 + 收益结算 | 核心 UI | ✅ | 2.3 |
| 现金跑道仪表盘（第7.4节） | 油量表 + 白话说明 | Phase 3 | ✅ | 2.7 |
| RPM 航线盈利评估（第7.7节） | RPMCalculator + 三色灯 | 逻辑层 | ✅ | 2.2 |
| AI 智能指挥中枢（第13章） | DeepSeek + 本地话术 | AI 接口 | ✅ | 2.4 |
| AI 执行建议（第13.3节） | ActionExecutor | Phase 1 | ✅ | 2.5 |
| 季度推进（第2章核心循环） | TimeManager | Phase 2 | ✅ | 2.6 |
| 董事会目标生成（第5.4节） | BoardManager + TargetGenerator | Phase 5 | ✅ | 2.9 |
| 年度结算与续聘（第4章） | YearEndEvaluator + VoteUI | Phase 6 | ✅ | 2.10 |
| 安全评级与事件（第7.6节） | SafetyEventManager + SafetyUI | Phase 7 | ✅ | 2.11 |
| 航权审批（第7.7节） | RouteRightManager | Phase 7 | ✅ | 2.11 |
| 脚本化 AOG 危机（第7.2节） | AOGManager + AOGUI | AOG | ✅ 已验收 | 2.12 |
| 战略性扩张提案（第7.7节） | ExpansionProposalManager + UI | 扩张提案 | ✅ 已验收 | 2.13 |
| 部门管理 / 任命（第6章） | （规划中） | Phase 8+ | ⬜ | 2.14 |
| 燃油对冲 / 融资（第7.4节） | （规划中） | Phase 8+ | ⬜ | 2.14 |

---

## 第 4 章：里程碑概览（按玩家体验时间线）

> 对应路线图 **「四」**。

- **0～15分钟**：首航 + 现金跑道 + RPM 查询 ✅（§2.1～2.4、§2.7～2.8）
- **15～30分钟**：AOG 危机 ✅ 已验收（§2.12 · 2026-08-01）
- **30～60分钟**：董事会目标 + 年度结算 ✅（§2.9～2.10）
- **60～120分钟**：安全事件 + 航权审批 + 扩张提案 ✅ 已验收（§2.11、§2.13 · 2026-08-01）
- **120分钟以后**：部门管理、融资、化学反应等（Phase 8+） ⬜（§2.14）

---

## 附录 A：玩家文案规范

| 场景 | 推荐说法 | 避免 |
| :--- | :--- | :--- |
| 现金可撑多久 | 现金跑道：按现在开支，流动性还可以撑 X 个月 | 只说「跑道 6.3 月」 |
| 资金状态 | 目前还算稳妥 / 有点紧 / 偏危险 | 只甩绿黄红字母给新手 |
| 航线评价 | 比较划算 / 先观望 / 暂缓 + **综合评定 XX 分** | `RPM70/绿/日利62.8` |
| 竞争 | 对手航班已经很多 | 竞对运力压迫系数 |

---

## 附录 B：给 Cursor 的续作提示词模板

```text
请严格按 docs/Development Manual/Airline_CEO_Dev_Task_v1.5.md 第 2.14 节（Phase 8+）开工，
并对照 docs/GameDesignBible/MVP/Airline_CEO_Dev_Roadmap_v1.5.md。
优先建议：部门任命或融资。
不要重复实现第 2.1～2.13 节已完成内容（含 AOG / 扩张提案）。工程根目录是 Unity Project/。
完成后同步更新路线图与本任务书第 1 / 3 / 4 章状态，并说明验收步骤。
```

---

## 附录 C：Unity 菜单速查

- `Run Logic Self Tests`
- `Batch Create Data Assets`
- `Setup MVP Scene Objects`
- `Create DeepSeek Config Asset` / `Set DeepSeek API Key`
- `Create UI Prefabs (Phase 3)` / `Generate Chinese TMP Font Asset`
- `清档 Clear Save`
- `Debug/Force Mild|Moderate Safety Event`
- `Debug/Force AOG Crisis`
- `Debug/Force Expansion Proposal`
- `Debug/移除北京-伦敦航线（复测航权）`

---

**最后建议：**  
以 **v1.5** 为现行施工图；章节顺序以路线图 **一～四** 为准。遇报错把 Console 红字发给 Cursor / Kimi。
