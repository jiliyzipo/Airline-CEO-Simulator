# OPS 离线年闭环（Phase 2 · Week 4 金标准）

> 单位约定：本报告货币一律为「万美元」（与 `EconomyBalanceParams` / `GameState.cash` 口径一致）。

`AirlineYearSimulator` 在 Phase 1.3 RPM/宏观月结之上叠加运营调度层，形成可回归的年度闭环。

## 闭环链路

```
RPM 日基准
  × 周班次/7 × 30 日
  × OpsEconomics.profitMultiplier（利用率 / 过站 / 延误）
  → 月收入与变动成本
  → 准点率按 AvgDelayProbability 投影
  → M6 注入一次 IRROPS（台风暴雨·方案 A）
  → 年末现金 / OTP / 调度效率摘要
```

公式源码 **Link** 自 Unity：

- `OpsEconomics.cs`
- `OpsIrropsLogic.cs`
- `RPMCalculator.cs` / `CashRunnelCalculator.cs`

## 运行

```powershell
cd tools/balance
dotnet run -- --seed 42 --year 1 --out out
.\compare_baseline.ps1
```

## seed=42 基准（OPS 层开启）

| 指标 | 值 |
|---|---|
| 年净利 | ~813 万美元 |
| 年末现金 | ~1288 万美元 |
| 调度效率均值 | 0.74 |
| 延误概率均值 | 0.13 |
| OTP | 88.0 → ~87.0 |
| IRROPS（M6） | −25 万美元（全网延误缓冲） |

相对 Phase 1.3「纯 RPM」基准（年净利 ~1423 万美元），OPS 层把排班效率与延误惩罚写进 P&L，并计入一次运行扰动，故年净利更保守——这是预期行为，不是回归失败。

## 关闭 OPS 层（对照）

构造时传 `applyOpsLayer: false, injectIrrops: false` 即可回到接近 1.3 的纯 RPM 年结（仍可能因代码演进略有差异）。
