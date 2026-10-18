# FLEET/NET 离线年闭环（Phase 2 · Week 6 金标准）

`AirlineYearSimulator` 在 OPS + FIN 年闭环之上叠加机队/航线网络层，形成「机队残值/维护 → 舱位收益乘数 → 网络日利/平均航距」的可回归闭环。

## 闭环链路

```
OPS 年闭环（RPM × 调度效率 × IRROPS）
  → FIN 年闭环（碳成本 + 融资 + 信用/WACC）
  → FLEET 层：机队规模 / 均龄 / 年维护（FleetEconomics.MonthlyMaintenance × 12）
  → 机队残值（FleetEconomics.ResidualValue：直线折旧 + 机龄折价，下限 10%）
  → 舱位收益乘数（FleetEconomics.SeatMixRevenueMultiplier：默认 12% 商务 → ×1.0）
  → NET 层：航线数 / 网络日利（Σ 月度航线贡献 / 365）/ 平均航距
```

公式源码 **Link** 自 Unity：

- `FleetEconomics.cs`（纯 C#，与 `FleetManager` / `FleetSubpagePanel` / `PurchaseDecisionPanel` 共用）

## 运行

```powershell
cd tools/balance
dotnet run -- --seed 42 --year 1 --out out
```

## seed=42 基准（OPS + FIN + FLEET/NET 层开启）

| 指标 | 值 |
|---|---|
| 机队规模 | 5 架（最小航司：A320neo ×3 / B737-800 ×1 / B787-9 ×1） |
| 平均机龄 | 2.0 年 |
| 年维护 | ~336 万（5 万/机/月 × 机龄 6%/年上浮） |
| 机队残值 | ~63,208 万（直线折旧，机龄折价 3%/年） |
| 舱位收益乘数 | ×1.000（默认 12% 商务舱占比 = 基准） |
| 航线数 | 3 条（PEK-SHA / PEK-ICN / PEK-LHR） |
| 网络日利 | ~5.8 万（Σ 航线月度贡献 / 365） |
| 平均航距 | ~3,393 km |

机队残值显著高于现金（500 万开局现金），是因为残值反映资产规模而非现金流——这是预期行为，仅作为资产侧指标回归比对。

## 关闭 FLEET/NET 层（对照）

FLEET/NET 层为**只读汇总**（不改变 P&L 或现金流），因此关闭与否不影响年净利；其价值在于把机队/航线网络的资产与网络维度纳入同一份离线基准，供后续部门（MKT/MNT/HR/SAF/STG）叠加时统一回归。
