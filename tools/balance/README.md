# Airline CEO · Balance Simulator

离线数值平衡工具（Phase 0.3）。**直接 Link Unity 工程里的 `RPMCalculator` / `CashRunnelCalculator` 源码**，保证公式不与游戏分叉。

## 路径

```
tools/balance/          ← 本项目（仓库已有 tools/，小写）
  AirlineCEO.Balance.csproj
  Program.cs
  Economy/              ← 宏观引擎（对齐 MacroEconomyManager，无 Unity 依赖）
  Scenarios/            ← 最小航司：5 机 / 3 航线
  Sim/                  ← 一年月度结算
  Export/               ← CSV 输出
  out/                  ← 运行产物（gitignore）
```

Unity 项目仍在：`Unity Project/`（不新建 Unity 工程）。

## 前置

- .NET 8 SDK（`dotnet --version` ≥ 8）

## 运行

```powershell
cd "C:\Users\jiliy\Airline-CEO-Simulator\tools\balance"
dotnet run -- --seed 42 --year 1 --out out
```

参数：

| 参数 | 默认 | 说明 |
|---|---|---|
| `--seed` | 42 | 宏观随机种子（可复现） |
| `--year` | 1 | 财年编号（写入 CSV） |
| `--out` | `out` | 输出目录 |
| `--stress` | — | Phase 4 Week 13 全流程压测（60 组合扫参，输出 `STRESS_BALANCE_REPORT.md` + `out/stress_matrix.csv`） |
| `--netstress` | — | **K14 网络池压测**：接真实机场三表与接管网络，扫「机场数 × 批次规模」× 繁荣/滞胀两条宏观，输出 `NETWORK_POOL_STRESS.md` + `out/netstress_matrix.csv`；含批次 A 消融断言与「默认路径零扰动 / 无宇宙外航线」机制守卫 |
| `--netstress-diag` | — | K14 诊断：摊开前 6 家样本的档案 → 网络 → 一年模拟（现金/机队/航线/月收入/月固定/净烧钱 + 逐情景损益），用来判断宏观轴是否被打爆、指标是否饱和 |
| `--freq` | — | 班次模型响应曲线 + 6 项不变量（`Logic/RouteFrequencyModel.cs`） |
| `--frequniverse` | — | 全 98 家可接管航司的班次直方图 + 航距分带（近/中/远）+ 双口径体检 + 10 项断言（链接真实 `TakeoverWorldBuilder`） |
| `--routeinventory` | — | 全航司**航线级**清单（hub→dest/km/周班/机型）+ 新入表机场归属断言 + 按星级的接管读数（家数/机队/航线/现金/现金跑道） |
| `--presets` | — | 四个**具名预设**（CAN/PEK/PVG/CTU）的接管读数与「建议现金」，回填 `PresetAirlines` 用这一档，不要用星级平均 |
| `--fleetcheck` | — | 全 98 家可接管航司的「机队 ↔ 网络」自洽体检：无机航线 / 机型飞不到 / 机队规模下限 + 5 项断言 |
| `--routefixed` / `--routefixed-sweep` | — | 航线周期固定成本档位表 / 扫参 |
| `--routefixed-base` | 出厂值 | 覆盖固定成本基准（万/日/线） |
| `--channels` / `--legacy` | — | 渠道收益 / 旧版余额复核（`--shortage` 默认 60） |

产物：

- `out/pnl_y{year}_seed{seed}.csv` — 12 个月利润表
- `out/summary_y{year}_seed{seed}.txt` — 关键指标（便于回归 diff）

## 班次模型（`--freq` / `--frequniverse`）

每周班次由 `Logic/RouteFrequencyModel.cs` 统一给出：需求（城市吞吐 × OD 吸引力 / 距离衰减）与物理上限（单机日利用率 ÷ 往返耗时）取小，量化到 1–28 班，再拆成每波 ≤ 7 班的班期。

取代了此前的 `weeklyFlights = km >= 5000f ? 4 : 7`（全局只有 4/7 两档）。

为了在无 Unity Editor 的情况下复核「全航司班次分布」，本工具额外链接了：

- `Data/AirportGeoCatalog.cs` —— 经纬度表与大圆航距的**唯一真源**（`MapWidget` 已改为引用它，地图与经济不再各存一份坐标）；
- `Systems/TakeoverWorldBuilder.cs` + `TakeoverWorldBuilder.Apply.cs` —— 前者是纯数据的种子构建（可离线编译），后者写入 `GameState` 故不链接；
- `Shims/UnityEngineShim.cs` —— 仅 `Mathf` / `MapWidget.GreatCircleKm` / `DataBase` 三处垫片。

因此 `--frequniverse` 打印的就是接管界面里的真实航线班次。

### 航线级清单（`--routeinventory`）

`--frequniverse` 只出**分布**（直方图、分带占比），答不了「某座机场入表后，到底哪些航司的网络变了」。
往 `CityEconomyCatalog` 加一座城市，`BuildDestinationPool` 的候选池对**所有**航司都变了；
只比航司总数与航线总数，看不出「老航司的某条线被新城市顶掉」这种替换。本报告逐条打印
`hub → dest / km / 周班 / 机型`，并断言：

- 新机场**未被外站航司**纳入目的地（池按需求降序取前 N，落选即未挤入前 N，也就没顶掉任何既有目的地）；
- 每座新机场的**本场**航司确实展开了出港航线（> 0）；
- 另附按星级的接管读数（家数 / 机队 / 航线 / 现金 / 现金跑道），用于判断加城市是否把最易档堆厚。

换批次时改 `RouteInventoryReport.Watched` 即可。

### 目的地池（`TakeoverWorldBuilder.BuildDestinationPool`）

班次模型的**输入端**是目的地池。旧池写死为三张清单（国内 24 场 → 区域 9 场 → 远程 8 场），
无论基地在哪都按这个顺序取，于是伦敦/迪拜/洛杉矶基地的整条网络都是洲际远程，班次清一色 2–5。

现按**航距分带**（≤ `FreqBandNearKm` = 3000 km → ≤ `FreqBandMidKm` = 6000 km → 远程）取候选，
带内按模型自身的需求（含距离衰减）降序，并按难度决定洲际占比（★1–2 = 0%／★3 = 20%／★4 = 30%／★5 = 35%）；
同时以**机队航程上限**过滤（A320neo 6,300 km 飞不了 PEK–JFK），选机侧 `PickAircraft` 取「满足航程的最小座级」。
**远程带另有一条硬上限**：条数不得超过「能飞该航距的可用飞机数」（见下方 `--fleetcheck`），
因为第 4 步兜底只按 `routeCount` 顶数时，机队里的宽体并不会跟着变多。

实测：近程航线占比 64%（478/742），hub_CDG 中位班次由 3 → **20**，hub_ICN 由 6 → **13**。
残余缺口（美国/南美/非洲机场太少）见设计文档 §12.7。

### 双口径体检（`--frequniverse` 末章）

每周班次有两套口径：**离港日**（按计划起飞日）与**值勤日**（按机组报到日，跨夜班归前一日）。
两者只是把同一批班次归到不同星期，**周合计恒等**，差异只在星期分布。

接管流程会把波次列表压成 1 个条目（`RouteData.SyncFlightsFromPrimary()`），旧实现的值勤侧
「每天只 +1」、无视 `weeklyFlights`，于是 25 班/周 · 每日 被报成 7，航线卡片并写成
「离港25/值勤7班/周」（全量 452/752 条命中）。本报告对每一家航司的每一条航线做同样的压扁
重算，断言 `离港周 ≡ 值勤周` 且 `7 日分布合计 == 每周班次`，并打印 3 条样例头行
（如 `接管前已开航 · 20 班/周  日分布[3,3,3,3,3,3,2]`）。详见设计文档 §7.2b-1。

### 机队 ↔ 网络自洽（`--fleetcheck`）

班次与网络都自洽了，接管局仍可能**有航线没飞机**：机队规模与过渡业务占机数（干租 / 湿租 / 包机 / 培训）
是两处独立配置，而供机侧强制「一架飞机只配一条航线」。旧口径的缺口是确定性的：

```
未分配条数 = max(0, 航线数 − (机队数 − 过渡业务占机数))      # 全量实测 72 条，且全是洲际线
```

界面于是并写出「未分配 A320neo」—— 机型字段的兜底值，而 A320neo 本来就飞不了 7,409 km 的 FRA–ATL。

本报告对每一家可接管航司核对：① 无机航线数；② 机队规模 ≥ 航线数 + 过渡占机数；
③ 过渡业务只从机队尾部抽飞机（机队规模补足依赖这条约定）；④ 机型航程 ≥ 航距（硬断言）；
另外把「航程 ≥ 航距但裕度 < 5%」的少数几条（LHR→DTW 6,045 km、SYD→SIN 6,295 km）列成 `[ 注 ]`
—— 物理上飞得到，只是没留足运行余量。详见设计文档 §12.8。

> 本报告只管**种子生成**（航线是否配到合适的飞机）。执行层「无机航线不得起飞」的闸门在
> `Logic/FlightGroundLogic.cs` + `LiveClockSimulator.TryTakeoff`：离线工具拿不到 `GameState`
> （MonoBehaviour），故这一条由编辑器自测 **R17** 兜底（见设计文档 §12.9），`--fleetcheck`
> 只能证明「种子里不再有无机航线」，证明不了「跑起来也不会幽灵起飞」。
>
> 另注**收入口径**：季结走 `TimeManager` 的 `quarterIncome = GameState.monthlyRevenue × 3`
> （实时落地只是预支，季末按 `max(0, 计划 − 已入账)` 轧差），因此 `monthlyRevenue` 也必须剔除
> 持续性停飞的航线，否则「幽灵收入」只是改在季末结账 —— 见设计文档 §12.9 第 6 条。
> 本工具按出厂值跑通全航司（种子内 0 条无机航线），故该口径变化不影响 `compare_baseline` 的基准。

### 网络池压测（`--netstress`，K14）

旧 `--stress` 的 60 组合用的是 `StressTestRunner.AirlineTypes` 里**手写**的三四条航线，
不读 `AirportCatalog` / `CityEconomyCatalog`。因此「批次 A 加了 ZUH/WUX/NGB/WNZ」这类改动
在压测里等于没发生 —— 加机场只会改变**接管界面**的网络，不会改变压测的破产率。

`--netstress` 换用真实航司池做样本：`PresetAirlines.AllSelectable()` 里枢纽属于被测机场池的
**全部航司**，网络由 `TakeoverWorldBuilder.BuildPreview(id, universe)` 生成（与选司界面同一套
目的地池/选机/班次模型，只收窄目的地宇宙），再逐家跑一年 `AirlineYearSimulator`。

- 宏观固定**滞胀**（GDP −0.5% / 油价 1300 $/t / seed=42）：只有它能把这批按 8/6/4/3 月跑道
  配好的现金压到破产边界；换繁荣期人人绿档，「破产率变化」会退化成 `0 = 0` 的无信息断言。
- 只压**有执飞机**的航线（无机航线局内停飞，算收入会造假）；
- `fixedMonthlyOpex` = 总部/地面 + 月租 + 月均还本付息 + 月均保费，机队维护与人力由 simulator
  按同一批公式自算，不重复传。

断言（K14.3）：去掉批次 A 四城后，① 航司数/航线数/网络指纹必须变化（证明读到三表）；
② 破产率 / 平均跑道 / 航线密度至少一项变化（证明网络真的进了模型）。
报告同时给出每一批的「新增枢纽航司 N 家 / 网络 +M 条」映射。

> 与 `--stress` 的分工：`--stress` 是**档位 × 宏况**的粗网格（回答「哪一档最危险」），
> `--netstress` 是**机场池**的消融实验（回答「加/删城市会不会改变结论」）。两者不可互相替代。

## 压测口径（`--stress`）

> 这一节是 2026-10-24 设计裁定（A + 「难度=起始现金」）的落地文字，改报告前先读它。

**1 · 这张矩阵是「不作为基线」，不是玩家存活率。**
`--stress` 跑的是「CEO 一整年不做任何决策」的自动年循环（OPS/FIN/FLEET/MKT/STG/MNT/HR/SAF）。
所以「破产」的真实含义是：在给定的期初现金、机队与网络下，什么都不做会被最恶劣的宏观情景打到
资不抵债。它是难度设计的锚点，任何把破产率读成「玩家存活率」的解读都是错的。

**2 · 难度只改期初现金，不改损益。**
60 组合实际是 **20 个损益情景 × 3 个起始现金**（625 / 500 / 400）。同一（航司, 宏观）在三档难度下
净利逐位相同；`StressTestRunner` 传给 `AirlineYearSimulator` 的 `irropsSeverity`（0.80 / 1.00 / 1.25）
只作用于一次性 IRROPS 现金冲击（全年 ±6 万量级），相对 ±125 万的期初现金差可忽略。
**这是设计意图**（「难度 = 起始现金」），不是漏接的旋钮。

**3 · 净利 ≠ 期末 − 期初。**
`TotalNetProfit` 不含 IRROPS 现金冲击与跑道告急贷款，故同情景三档净利相同、期末各差 100 万。
这张矩阵是**情景投影，不是台账**，不能当现金对账的 oracle。

**4 · 全服务锚点在滞胀下只剩约 2 月跑道（标准难度）—— 保留，不改。**
2026-10-24 裁定取 **A（保留硬度、改口径）**：承认「标准 + 滞胀 + 一整年不操作 ≈ 2 月」属设计张力，
不为把它拉回 GREEN 而回调需求/油价系数（那会把 25% 告急抹平成 0，失去压测意义）。
配套动作：报告抬头印「不作为基线」；`Program.cs` 里原先那条恒假的「全服务 15/15 全 GREEN」自检
已改为**按实际结果如实印**（全服务一旦告急就点名是哪一个组合、几分跑道），不再静默消失。
裁定全文：`docs/handoff/T04_8_告急组合裁定_2026-10-24.md` §3.2、§4。

**5 · 低成本在滞胀/衰退下比支线、包机都差（−1122 / −504）—— 待查，未定案。**
这是本矩阵唯一仍挂着的数值疑点（`T04_8…md` §3.1）。定案前需要一次对照实验：
单独调 `yieldMultiplier` 0.85→0.90、再单独调 `CompetitorSupply` 回 0.55，看哪个是主因。
**在没做这个对照前，不得宣称这条已「验证」。** 改系数后必须重跑矩阵 + 重录 `BASELINE_SOURCE.json`。

## 最小航司假设

| 项 | 值 | 来源 |
|---|---|---|
| 初始现金 | 500 万美元 | `GameState.cash` |
| 固定月开销 | 80 万美元 | `GameState.monthlyBurnRate` |
| 机队 | 5 架（4×窄体 + 1×B787） | 扩展自 `FleetManager` 初始 3 架 |
| 机队维护 | 5 万/机/月 | `FleetManager.BaseMaintPerAircraft` |
| 航线 | PEK-SHA / PEK-ICN / PEK-LHR | 短/中/长样本 |
| 品牌净值 | 55 | 对齐 `LogicSelfTests` |
| 日利润引擎 | `RPMCalculator` | **Link Unity 源码** |

## 与 Unity 的同步约定

1. 改 `Unity Project/Assets/Scripts/Logic/RPMCalculator.cs`、`CashRunnelCalculator.cs`、`OpsEconomics.cs`、`OpsIrropsLogic.cs` 后，直接 `dotnet run` 即用新公式，无需复制。
2. 宏观倾向公式在 `Economy/MacroEconomyEngine.cs`，与 `MacroEconomyManager` 对齐；若 Unity 侧改动，请同步此处并注明版本。
3. 回归：固定 `--seed 42`，用 `.\compare_baseline.ps1` 对比（见下节；**不要**只肉眼比 `summary_*.txt`）。
4. **OPS 年闭环**：见 `OPS_YEAR_LOOP.md`（调度乘数 + 准点投影 + M6 IRROPS）。

## 回归对比与基线出处（K13）

```powershell
cd "C:\Users\jiliy\Airline-CEO-Simulator\tools\balance"
.\compare_baseline.ps1                    # 先验出处，再比数值；不可比则 exit 3
.\compare_baseline.ps1 -Tolerance 0.05    # 放宽到 ±5%（默认 ±3%）
.\compare_baseline.ps1 -UpdateBaseline    # 重录基线并写下来源指纹
```

### 退出码

| 码 | 含义 | 该怎么办 |
|---|---|---|
| 0 | 与基线一致（容差内） | 无 |
| 1 | 输入没变，但数值漂了 | 这是真回归，查改动 |
| 2 | 缺基线 / 缺指纹 / seed-year 不符 | 先 `-UpdateBaseline` 重录 |
| 3 | **输入已变，基线不可比** | 不要读成百分比；确认改动符合预期后重录 |

### 基线随哪一版生效

`baseline/BASELINE_SOURCE.json` 是基线的身份证，由 `--baseline-write` 自动生成：

| 字段 | 作用 |
|---|---|
| `Commit` / `Branch` / `Dirty` | 人读的坐标：这条基线是在哪个提交、什么分支上录的；`Dirty=true` 表示录制时工作区有未提交改动 |
| `SourceHash` / `SourceFileCount` | **机器判据**：参与计算的 86 个 `.cs`/`.csproj`（工具自身 + csproj 链入的 Unity 逻辑源码）的内容哈希 |
| `SummarySha256` | 基线摘要自身的哈希，用于核对文件没被手改 |
| `GeneratedAt` / `Seed` / `Year` | 时间与参数 |

**能不能比，只看 `SourceHash`，不看 commit。** 三种组合的实际含义：

- 文档/README 改了、模型没动 → commit 变了，`SourceHash` 不变 → **可比**；
- 模型改了但没提交 → commit 没变，`SourceHash` 变了 → **不可比**（`Dirty=true` 时尤其容易踩）；
- 改了 `EconomyBalanceParams` 一类参数常量 → `SourceHash` 变 → **不可比**。

所以「基线是 2026-08-12 更新的」这种手写日期不再需要，也不可信；出处一律从 JSON 读。

### 当前基线

- 文件：`baseline/summary_y1_seed42.txt` + `baseline/pnl_y1_seed42.csv`
- 录制于：**2026-10-24**（T04.8 设计裁定 A 落地轮：只改 `Program.cs` 报告文字与 `README.md` 口径，
  年跑读数与上一版逐字节相同；更早五次为 2026-10-22 K13 首录、2026-10-23 K12 展示层接线后、
  K16 超短航段过滤后、K17 接管成本账本落地后、`FleetEconomics` 注释 6%→5%），
  commit `bb8db09f`、工作区脏（`Dirty=true`）、
  `Note` = `T04.8 ruling A: report wording only; annual numbers byte-identical`
- 输入指纹：`1b6f5c1b210d`（87 文件）· seed=42 year=1（上一版为 `d97477691576`）
- 关键读数：`closing=1707.90`、`net=1232.90`、`runway=99.00`（与 K13/K14/K16/K17 录制逐位相同）
- K16 只删「中国境内 <250 km」的国内航段（11 删 11 换、总数 823 不变），不改月度收支公式，
  故重录前后 `closing` / `net` / `runway` 仍逐位相同 —— 这是「K16 不影响年跑基线」的数值证据。
- 本轮（残余缺口收口）只改了 `Logic/FleetEconomics.cs` 的**两处注释**（机龄 6%/年 → 5%/年，与常量
  `MaintAgeUpliftPerYear` 对齐），外加 Unity 侧的存档迁移与两条 PlayMode 用例；公式与常量一个没动。
  注释在哈希覆盖范围内，故指纹 `bb18c1b9eabd` → `d97477691576`，而 `closing` / `net` / `runway`
  逐位不变 —— 与 K16/K17 同一套证据逻辑：**指纹变只说明输入变了，读数不变才说明数值没变**。
- K17 只新增 `Logic/TakeoverCostLedger.cs`（四段式 → 8 部门预算的**归属映射**）并让接管落地时按
  该映射写入 `budgetItems`；四段式总额、净烧钱与跑道公式一字未动，故 86 → 87 文件的哈希变化
  只说明「输入变了」，`closing` / `net` / `runway` 逐位相同说明**年跑数值没变**。
- K12 只新增了「季末一次性项」的展示与档案字段（`netUsdExposureWan`），不参与月度收支；
  K14 只新增压测可选参数与报告代码（`BuildPreview(id, universe)`，生产调用点不传）。两者都不改
  月度收支，故重录前后 `closing` / `net` / `runway` 逐位相同 —— 这也是「一次性项不得进月烧钱」
  与「压测缝零扰动」的数值证据。
- 与旧基线的差：旧基线记录 `closing=1365.88`（2026-10-09），差 **+25.0%**。
  这段差值来自 K08–K11 与 D2 的口径修正（还本付息、全险保费、过渡收入封顶、
  机组缺员乘数、`×30` → 按实际执飞天数、星级与预设重排），**不是**本轮引入的回归。
  旧基线因此作废（无法分辨「口径变了」与「代码坏了」），只作历史留存。

### 交付版本与基线的关系

基线与 DEL 不是一一绑定：一个 DEL 可能只改文档，也可能只改一个不影响模型的 UI 文件。
需要回答「这条基线是哪一版生成的」时，读 `BASELINE_SOURCE.json` 的 `Commit`，
再用 `git branch --contains <commit>` / 对应 DEL 的 `git` 字段反查交付编号。
`--baseline-write --delivery DEL-xxx` 可以直接把交付号写进 `Delivery` 字段（可选）。

## 校准结果（seed=42）

| 指标 | Phase 1.3（纯 RPM） | +OPS 年闭环 |
|---|---|---|
| PEK-LHR 日利润 | **~11.5 万** | 同（smoke 未变） |
| 年净利 | ~1,423 万 | **~813 万** |
| 年末现金 | ~1,923 万 | **~1,288 万** |

详见 `CALIBRATION_1.3.md` 与 `OPS_YEAR_LOOP.md`。

> 上表是 Phase 1.3 的历史校准读数，**不是**当前基线；当前读数见 `baseline/summary_y1_seed42.txt`。

## 后续（计划内）

- ✅ Phase 4 Week 13：60 组合（难度×航司类型×宏观情景）批量扫参 —— `dotnet run -- --stress`，报告见 `STRESS_BALANCE_REPORT.md`
- ✅ Phase 4 数值调优落地（依据压测报告）：固定开销按机队规模分段（`EconomyBalanceParams.FixedMonthlyOpexFor(n)=20+15×n`）；LCC 竞对运力上调 + `yieldMultiplier` 折价（0.85）；支线场景校准。结果：RUNAWAY 5→0、告急 25→6（全落「滞胀」最坏宏观），回归基线不变
- ✅ 存量人力成本接入（2026-10-07）：`HrEconomics.LaborCostPerAircraftWan(240万/机/年)` 计入 `total_cost`；`FixedOpexPerAircraftWan` 15→0（每机负担改由人力承担，避免重复计提）。年净利 1306→1006 万；压测 GREEN 54→47、BANKRUPT 4→8（危险组合仍全落「滞胀」）。基线已刷新，详见 `CALIBRATION_1.3.md`
