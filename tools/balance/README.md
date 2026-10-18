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
| `--freq` | — | 班次模型响应曲线 + 6 项不变量（`Logic/RouteFrequencyModel.cs`） |
| `--frequniverse` | — | 全 94 家可接管航司的班次直方图 + 航距分带（近/中/远）+ 双口径体检 + 10 项断言（链接真实 `TakeoverWorldBuilder`） |
| `--fleetcheck` | — | 全 94 家可接管航司的「机队 ↔ 网络」自洽体检：无机航线 / 机型飞不到 / 机队规模下限 + 5 项断言 |
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
3. 回归：固定 `--seed 42`，对比 `summary_*.txt` 是否变化。
4. **OPS 年闭环**：见 `OPS_YEAR_LOOP.md`（调度乘数 + 准点投影 + M6 IRROPS）。

## 回归对比

```powershell
cd "C:\Users\jiliy\Airline-CEO-Simulator\tools\balance"
.\compare_baseline.ps1              # 与 baseline/ 对比，不一致则 exit 1
.\compare_baseline.ps1 -UpdateBaseline   # 接受当前结果为新基准
```

基准文件：`baseline/summary_y1_seed42.txt`（含 OPS 年闭环字段，2026-08-12 更新）。

## 校准结果（seed=42）

| 指标 | Phase 1.3（纯 RPM） | +OPS 年闭环 |
|---|---|---|
| PEK-LHR 日利润 | **~11.5 万** | 同（smoke 未变） |
| 年净利 | ~1,423 万 | **~813 万** |
| 年末现金 | ~1,923 万 | **~1,288 万** |

详见 `CALIBRATION_1.3.md` 与 `OPS_YEAR_LOOP.md`。

## 后续（计划内）

- ✅ Phase 4 Week 13：60 组合（难度×航司类型×宏观情景）批量扫参 —— `dotnet run -- --stress`，报告见 `STRESS_BALANCE_REPORT.md`
- ✅ Phase 4 数值调优落地（依据压测报告）：固定开销按机队规模分段（`EconomyBalanceParams.FixedMonthlyOpexFor(n)=20+15×n`）；LCC 竞对运力上调 + `yieldMultiplier` 折价（0.85）；支线场景校准。结果：RUNAWAY 5→0、告急 25→6（全落「滞胀」最坏宏观），回归基线不变
- ✅ 存量人力成本接入（2026-10-07）：`HrEconomics.LaborCostPerAircraftWan(240万/机/年)` 计入 `total_cost`；`FixedOpexPerAircraftWan` 15→0（每机负担改由人力承担，避免重复计提）。年净利 1306→1006 万；压测 GREEN 54→47、BANKRUPT 4→8（危险组合仍全落「滞胀」）。基线已刷新，详见 `CALIBRATION_1.3.md`
