# FIN 离线年闭环（Phase 2 · Week 5 金标准）

`AirlineYearSimulator` 在 OPS 年闭环之上叠加财务层，形成「预算 → 碳成本 → 信用/WACC → 融资 → 年末现金流」的可回归闭环。

## 闭环链路

```
OPS 年闭环（RPM × 调度效率 × IRROPS）
  → 每月叠加碳成本（FinanceEconomics.CarbonCostPerRouteYearWan / 12）
  → 跑道告急（< 3 月）触发一次银行贷款（等额本息，利息/本金逐月回写）
  → 财年初：年度预算建议 + 董事会投票（EstimateBoardVotes）
  → 财年末：信用评级（EstimateCreditRating）→ WACC（Wacc）
  → 碳负债（CarbonLiabilityWan，未购配额 × 碳价）
```

公式源码 **Link** 自 Unity：

- `FinanceEconomics.cs`（纯 C#，与 `FinanceManager` / `FinanceSubpagePanel` 共用）
- `CashRunnelCalculator.cs`

## 运行

```powershell
cd tools/balance
dotnet run -- --seed 42 --year 1 --out out
.\compare_baseline.ps1
```

## seed=42 基准（OPS + FIN 层开启）

| 指标 | 值 |
|---|---|
| 信用评级 | AAA |
| WACC | 5.5% |
| 年度预算 | 250 万（董事会 5/7 票，建议区间） |
| 年碳排放 | ~28819 吨 CO₂ |
| 碳负债 | ~245 万（0 已购配额 × $85/吨） |
| 年碳成本 | ~162 万（入 P&L） |
| 紧急融资 | 0 次（跑道始终 > 3 月） |
| 年净利 | ~650 万（OPS ~813 万 − 碳 ~162 万） |

相对 OPS 年闭环基准（年净利 ~813 万），FIN 层把碳成本写入 P&L，故年净利下降 ~162 万——这是预期行为，不是回归失败。

## 关闭 FIN 层（对照）

构造时传 `applyFinanceLayer: false` 即可回到 OPS 层年结（年净利 ~813 万）。
