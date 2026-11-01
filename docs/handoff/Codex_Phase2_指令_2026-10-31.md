# Phase 2 指令 · 巨文件解耦（2026-10-31）

> 前置：S46 流程演练已通过独立复验（`REV-20261031133752-c22bc2`，EditMode 59/59），
> 源码提交 `2718e59`、台账提交 `f4546e0`。S46 定位为"目标为零、仅验证流程"，不计指标。

---

## 1. Phase 2 的唯一指标

**`Unity Project/Assets` 下 ≥1000 行的 `.cs` 文件从 25 个降到 ≤12 个。**

当前实测（2026-10-31 21:45，`git ls-files` 统计）：

| # | 行数 | 文件 | 类型 |
|---|---|---|---|
| 1 | 6595 | `Systems/InboxManager.cs` | MonoBehaviour |
| 2 | 5555 | `Editor/LogicSelfTests.cs` | static（Editor-only） |
| 3 | 2442 | `Logic/CapacityDirectiveLogic.cs` | static |
| 4 | 2169 | `UI/MapWidget.cs` | MonoBehaviour |
| 5 | 2037 | `UI/Shell/InboxPanelUI.cs` | MonoBehaviour |
| 6 | 1881 | `Logic/TransitionBusinessLogic.cs` | static |
| 7 | 1790 | `Logic/ExternalDataFetcher.cs` | static |
| 8 | 1631 | `UI/MVPBootstrap.cs` | MonoBehaviour |
| 9 | 1620 | `Logic/RoutePermitLogic.cs` | static |
| 10 | 1573 | `Logic/DeptReportLogic.cs` | static |
| 11 | 1549 | `UI/MarketSubpagePanel.cs` | MonoBehaviour |
| 12 | 1489 | `Systems/SaveManager.cs` | MonoBehaviour |
| 13 | 1422 | `Tests/PlayMode/T038UiAcceptancePlayModeTests.cs` | 测试 |
| 14 | 1421 | `UI/PrefabNavIntegration.cs` | MonoBehaviour |
| 15 | 1415 | `Tests/PlayMode/HrSafPlayModeTests.cs` | 测试 |
| 16 | 1297 | `UI/Shell/AdvisorModalUI.cs` | MonoBehaviour |
| 17 | 1244 | `Logic/OpsRotationLogic.cs` | static |
| 18 | 1239 | `Systems/HrManager.cs` | MonoBehaviour |
| 19 | 1157 | `UI/StrategySubpagePanel.cs` | MonoBehaviour |
| 20 | 1140 | `Systems/GameState.cs` | MonoBehaviour（存档根） |
| 21 | 1077 | `Logic/CeoCommandRouter.cs` | static |
| 22 | 1063 | `UI/RecruitmentOfficePanel.cs` | MonoBehaviour |
| 23 | 1046 | `Systems/FinanceManager.cs` | MonoBehaviour |
| 24 | 1044 | `UI/FleetUI.cs` | MonoBehaviour |
| 25 | 1041 | `UI/MapWeatherTileLayer.cs` | MonoBehaviour |

要达标必须处理 **13 个文件**。这是 7–13 个交付批次的工作量，不是一批能完成的。
**不要试图一个 DEL 里堆 13 个文件**——`stage-close` 的文件上限与独立复验成本都不允许。

### 1.1 关于这个指标的诚实说明

`LogicSelfTests.cs`、两个 PlayMode 测试文件本身是**测试资产**。把它们拆小会改变
"≥1000 行文件数"，但不产生运行时收益。你在 Phase 0 §1 已正确指出"partial 拆分不减编译量、
不减总行数"，这个判断依然成立。本指标的真实价值只有一条：**降低单次编辑的上下文体积**，
从而让 AI 与人工都更容易定位改动。不要在任何汇报里宣称它提升了导入速度或运行性能。

---

## 2. 分批顺序（按风险从低到高，不得跳序）

**原则：先纯静态逻辑，再 MonoBehaviour，最后存档根与测试。**

- **第 2 批（S47）**：`Logic/DeptReportLogic.cs`（1573 行，纯 static）
  这是最小风险的首个真实目标：无 MonoBehaviour 生命周期、无序列化字段、无 Unity 事件绑定，
  且输出是确定性文本投影，等价比对可以逐字节做。
- **第 3 批**：`Logic/RoutePermitLogic.cs`（1620）+ `Logic/OpsRotationLogic.cs`（1244）
- **第 4 批**：`Logic/CapacityDirectiveLogic.cs`（2442，可能需要再拆两批）
- **第 5 批起**：`Logic/TransitionBusinessLogic.cs`、`Logic/CeoCommandRouter.cs`、`Logic/ExternalDataFetcher.cs`
- **MonoBehaviour 组**（`MapWidget`、`InboxPanelUI`、`MVPBootstrap`、`MarketSubpagePanel`、
  `PrefabNavIntegration`、`AdvisorModalUI`、`HrManager`、`StrategySubpagePanel`、
  `RecruitmentOfficePanel`、`FinanceManager`、`FleetUI`、`MapWeatherTileLayer`）：每批最多 1 个。
- **`Systems/GameState.cs`（1140）**：**建议不拆**。它是存档根，字段被 JsonUtility 按名绑定，
  拆分带来的收益远小于误改字段名的风险。若最终仍需拆，必须单独一批 + 旧存档往返验证。
- **`Editor/LogicSelfTests.cs`（5555）与两个 PlayMode 测试文件**：放在最后。
  前置条件是你必须先说明 `Run()` 的局部函数捕获与断言顺序如何保持不变。

---

## 3. 第 2 批（S47）具体范围

**目标文件**：`Unity Project/Assets/Scripts/Logic/DeptReportLogic.cs`（1573 行）
**目标**：拆到主文件 <1000 行（建议 <600）。
**上限**：新增文件 ≤5 个、1 个 DEL、≤1 个批次。

已知结构（Cursor 实测，可直接用作起点）：

| 行 | 成员 |
|---|---|
| 21 | `public static class DeptReportLogic` |
| 69 | `Build(GameState, CeoAsk)` → 转调三参重载 |
| 75 | `Build(GameState, CeoAsk, string raw)` ← 40 行，**纯分派器**，不是 684 行巨型方法 |
| 757 | `ExecuteClose(...)` |
| 959 | `RunCapacityDirective(...)` |
| 1077 | `RunPending(...)` |
| 1272 | `RunRouteQuery(...)` |
| 1418 | `RouteNameResolver(GameState)` |
| 1524 | `SenderOf(string dept)` |
| 1540 | `RoleOf(string dept)` |

**关键发现（这改变了建议切法）**：`Build` 三个重载合计不到 50 行，它只是按
`ask.topic` 与 `ask.deptCode` 分派到 62 个具名方法。文件大是因为方法多、不是方法长
（最大的是 `Feasibility` 121 行、`RunCapacityDirective` 83 行、`CompanyOverview` 80 行）。

因此**不建议**按我最初设想的"只抽 SenderOf/RoleOf 文本投影"下手——那只有约 50 行，
远远不够。正确切法是**按职责分组搬整方法**，这是纯位移，等价性最容易证明：

| 新文件 | 建议收纳的方法（原行号） | 约计行数 |
|---|---|---|
| `DeptReportLogic.cs`（保留） | `Build`×2、`Fail`、少量共享辅助 | 主文件目标 <600 |
| `DeptReportLogic.Departments.cs` | `CompanyVerdict` 195、`CompanyAdvice` 208、`CrewGapTotal` 226、`Finance` 240、`HumanResources` 274、`Fleet` 311、`Operations` 355、`Market` 405、`Maintenance` 426、`Strategy` 448、`CompanyOverview` 469 | ≈450 |
| `DeptReportLogic.Directives.cs` | `Directive` 1042、`OpenRouteDirective` 1052、`FleetBuyDirective` 1056、`HrRecruitDirective` 1059、`FinanceDirective` 1062、`FinanceKindName` 1065、`ChannelName` 1068、`RunPending` 1077、`RunOpenRoute` 1097、`RunFleetBuy` 1115、`RunHrRecruit` 1122、`RunFinanceRequest` 1129、`CapacityDirective` 912、`RotationDirective` 876 | ≈400 |
| `DeptReportLogic.Capacity.cs` | `Feasibility` 469、`RunCapacityDirective` 959 | ≈300 |
| `DeptReportLogic.Close.cs` | `CloseRouteReview` 610、`CloseAmbiguous` 675、`CloseConfirm` 724、`ExecuteClose` 757、`CloseDoneLetter` 799、`CloseNetworkLetter` 835、`SamePair` 714、`CountRoutePair` 705、`CountAirworthy` 590 | ≈400 |
| `DeptReportLogic.Text.cs` | `RouteQuery` 1242、`RunRouteQuery` 1272、`DescribeMissingRoute` 1294、`ExistingPairList` 1330、`FlightNumbersOf` 1345、`RouteNumberLabel` 1366、`FirstFlightNumberOf` 1378、`ExistingFlightList` 1397、`RouteNameResolver` 1418、`EndpointHit` 1439、`FindRoute` 1455、`FindRouteByName` 1472、`NameMatches` 1487、`IndexIgnoreCase` 1219、`RouteLabelOf` 1224、`TryResolveRoute` 1136、`FlightNumberIn` 1192、`FindRouteByFlightNumber` 1160、`Answer` 1506、`ProfitText` 1504、`Fail` 1522、`SenderOf` 1524、`RoleOf` 1540、`GlyphOf` 1556 | ≈500 |

以上分组是**建议**，不是命令。你若认为某方法该换组，给出理由即可——但每组必须是
"整方法搬走"，不允许在方法内部切片。这是让"逐字节一致"可验证的前提。

硬约束：`Build` 的分派顺序（`if topic ==` 链 + `switch (deptCode)` 的 case 顺序）必须原样保留，
包括 `default: return Operations(gs);` 的位置。

---

## 4. 硬规则（违反即整批退回）

1. **partial 必须沿用仓库既有形态**：
   - 已存在先例：`Logic/QuarterlyOneOffLogic.cs` + `Logic/QuarterlyOneOffLogic.State.cs`、
     `Systems/TakeoverWorldBuilder.cs` + `Systems/TakeoverWorldBuilder.Apply.cs`。
   - 主文件类声明改为 `partial`，新增文件 `<类名>.<职责>.cs`，声明 `public static partial class <类名>`。
   - 同命名空间、同 asmdef。
2. **每个新增 `.cs` 必须同时提交配对的 `.meta`，GUID 全新唯一。**
   S43 曾因缺 `.meta` 导致快照预检报"未列文件"而整批回退。先例：
   `QuarterlyOneOffLogic.cs.meta` 与 `QuarterlyOneOffLogic.State.cs.meta` 各自独立 GUID。
   若 Unity 未运行无法自动生成，手工写标准 `.cs.meta` 模板，`fileFormatVersion: 2` +
   32 位十六进制 GUID，并确认 `git grep` 无重复 GUID。
3. **MonoBehaviour 文件**（本批不涉及，但后续批次必须遵守）：
   保留原文件名与原 `.meta` 不动，只把类声明改 `partial`。**不得重命名主文件**——
   Unity 依赖 `m_Script` GUID 与文件名的双重绑定。
4. **不得删除或重排任何测试断言、`Debug.Log` 文案、日志字段顺序。**
5. **不得改变浮点运算顺序、字符串拼接顺序、部门枚举顺序、字典遍历顺序。**
6. **不得动 `[Serializable]` 字段名、类型名、`JsonUtility` 绑定路径。**
7. **不得"顺手"格式化**：`CeoCommandRegression.cs` 里 `Trim(string s, int max)        {`
   这种原有畸形空格必须原样保留，否则 diff 失去可审性。新增文件用正常缩进即可。

---

## 5. 每批必须附带的证据

1. **改动清单**：文件 → 搬了什么 → 为什么是纯位移（不是改写）。
2. **等价性论证（逐条，不接受"测试绿灯"代替）**：
   - 被搬方法的完整清单（方法名 + 原行号 + 新文件行号），并证明**方法体逐字节一致**
     （可用去空白归一化比对，像 S46 那样）。
   - 若 `Build` 内部被切分：说明分支顺序、字符串拼接顺序、早退条件如何保持不变。
3. **机器证据**：`semcheck`、`slotcheck`、`bridgecheck`（60 组合分布必须完全一致、
   现金/净利差 ≤0.01 万）、EditMode、相关 PlayMode。计数与当轮 XML 一致。
4. **`.meta` 检查**：新增文件数与新增 `.meta` 数相等；GUID 无重复。

---

## 6. 不许做的事

- 不许为了把行数压到 1000 以下而**删代码、删注释、压缩格式**。判定标准是"文件变小"
  且"内容只是搬了位置"，不是"删到 999 行"。
- 不许把多个巨文件的拆分塞进同一个 DEL。
- 不许在拆分同时改业务逻辑、修 bug、改文案、改数值。
- 不许动 `Systems/GameState.cs` 的字段（无论是否拆分）。
- 不许声称本指标提升了导入速度或运行时性能。

---

## 7. 请先回答，再动手

请给出第 2 批（S47，`DeptReportLogic.cs`）的**具体拆分方案**供确认：

1. 新增几个文件、各自文件名与职责边界（可直接采用上面表格，也可给出你的调整与理由）；
2. 每个要迁移的方法名清单 + 原行号 + 目标文件；
3. 主文件拆分后预计行数；
4. `Build` 的分派链你如何保证原样（`if topic ==` 顺序、`switch (deptCode)` 的 case 顺序、
   `default: return Operations(gs);` 位置）；
5. 你打算如何证明每个方法体逐字节一致（建议：迁移前后取方法全文，去空白归一化后比对，
   像 S46 的 `Trim` 那样给出 `IDENTICAL: true`）；
6. 新增 `.cs` 与 `.meta` 的配对方式（GUID 来源、如何确认无重复）。

确认前不要改代码。

---

## 8. 补充实测（2026-10-31 22:05，Cursor）——在收到你的 S47 方案前必读

### 8.1 这个指标不降行数，也不降导入时间

| 项 | 数值 |
|---|---|
| Assets `.cs` 合计 | 130,027 行 / 437 文件 |
| ≥1000 行文件 | 25 个，合计 45,937 行 |
| 要降到 ≤12 必须处理 | 13 个，合计 31,753 行（占 Assets 24.4%） |
| 13 批全部做完后的行数变化 | **−0 行**；因新增 `partial` 声明、`using`、空行，预计净 **+78~156 行** |

**导入/编译时间零收益，原因不是猜测**：`Assets/Scripts/AirlineCEO.Runtime.asmdef`
把这 115,000 行全部装在一个程序集里。Unity 的编译单位是**程序集**，改任何一个 `.cs`
都会重编译整个程序集。拆成多少个 partial 文件都不改变这一点。你在 Phase 0 §1 写的
"partial 分文件不会减少编译的代码总量，也不会减少总行数" —— 这两句都已被实测确认。

**所以 S47 的真实收益只有一条**：单文件上下文从 1573 行降到几百行，降低单次编辑的
读入体积。汇报时只能写这一条，**不得**写成"降低导入时间"或"减少代码量"。

### 8.2 真正指向"导入慢 / 编译慢"的杠杆：拆程序集，不是拆文件

`Assets/Scripts` 按目录实测：

| 目录 | 文件 | 行数 |
|---|---|---|
| `UI` | 126 | 41,676 |
| `Logic` | 105 | 34,054 |
| `Systems` | 47 | 23,761 |
| `Editor` | 60 | 15,721（已是独立 asmdef） |
| `Data` | 42 | 5,343 |
| `AI` | 6 | 740 |
| `Testing` | 2 | 204 |

若能把 Runtime 拆成 `Data ← Logic ← Systems ← UI` 四个程序集，改一个 UI 文件就只需
重编译 41,676 行而非 115,000 行，增量编译才有意义。这是**一个到两个批次**的工作量，
而不是 13 批。

**但这是未经可行性验证的假设，不要当成既定方案。** 前置探测至少包括：
1. 依赖方向是否真的是单向的（`Data` 不引用 `Logic`/`Systems`/`UI`；有无反向 using）；
2. 有无循环依赖（拆了会直接编译失败）；
3. MonoBehaviour 跨程序集引用/prefab 的 `m_Script` GUID 是否受影响（GUID 不变即安全，
   但 `MonoBehaviour` 类型所在程序集变化会影响序列化引用解析，必须先用一个最小样本验证）；
4. 拆完后 `EditMode`/`PlayMode` 测试的 asmdef 引用是否仍成立。

这一项**不在本批范围**，需用户另行裁决后单独开任务。你在 S47 里不要顺手动它。

### 8.3 S47 仍按 §3 执行

上面的结论不改变 S47 的拆分方案要求，但请在你的方案里明确写一句：
"本批收益为单文件体积下降，行数与导入时间均为零收益。"

---

## 9. Explicit 用例口径（**用户裁决 2026-11-01，立即生效**）

### 9.1 裁决内容

`T057OneXPlayModeTests` 的两个 `[Explicit]` 用例：

- **默认不跑。**
- **仅当本批改动触及时间系统时**才显式补跑。

### 9.2 依据

实测（`docs/handoff/迭代成本实测_2026-11-01.md`）：

| 套件 | 用例 | 耗时 |
|---|---:|---:|
| PlayMode 默认 | 173 | 67.5 s |
| PlayMode Explicit | 2 | **510.1 s** |

这 2 个用例占一次交付闭环机器时间的 **73%**。它们慢是设计使然：验证 1× 下
"现实 1 分钟 = 游戏 1 分钟"，必须真的等满现实时间，加速即失去意义。

### 9.3 "触及时间系统"的判定范围

判据不是"改了哪些文件"，而是**本批 diff 是否可能改变时钟推进、倍速或停表行为**。
下列任一成立即算触及：

1. 改动文件属于时间系统本体：
   - `Systems/TimeManager.cs`
   - `Systems/GameSpeed.cs`
   - `Systems/LiveClockSimulator.cs`
   - `Logic/LiveClockLogic.cs`
   - `Logic/InboxTime.cs`
   - `Logic/ExternalTimeMapper.cs`
2. 改动涉及 `Time.timeScale`、`Time.unscaledDeltaTime`、`Time.deltaTime`、
   `GameSpeed.Set` / `GameSpeed.IsPaused`、`TimeManager.SimulationEnabled`。
3. 改动新增或修改了会**开/关表**的逻辑（例如到期强制待办、弹窗阻塞、暂停菜单）。
4. 改动调整了 tick 频率、调度间隔或时间累积方式。
5. 改动影响了 `TimeManager` 的序列化字段。

**判定由开发者在交付摘要里显式声明**：写 `touchesTimeSystem: true|false` 并给出理由。
判定为 `true` 而没跑，独立复验应直接判该批证据不足；判定为 `false` 但实际改了时钟行为，
属于误报，按缺陷登记。

### 9.4 汇报口径随之调整

- 删掉"无剩余未执行项"这个说法——它会把默认不跑的 Explicit 变成必跑。
- 改为：`PlayMode 默认 <n>/<n> 通过；Explicit 2 项按 §9 口径 <已跑|未跑（未触及时间系统）>`。
- 原始 XML 不要改写 Explicitness 标记，也不要为了凑"全绿"去把 Explicit 转成常规用例。

### 9.5 与指标的关系

这条裁决不改变 §1 的 25→12 指标，也不改变 §2 的分批顺序；
它只削减每批的机器等待时间，不改变任何验收判据的严格度。
