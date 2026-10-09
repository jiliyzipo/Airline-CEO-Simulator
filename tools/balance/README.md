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

产物：

- `out/pnl_y{year}_seed{seed}.csv` — 12 个月利润表
- `out/summary_y{year}_seed{seed}.txt` — 关键指标（便于回归 diff）

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
