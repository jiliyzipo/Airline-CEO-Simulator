# Phase 1.3 数值校准报告

日期：2026-08-12  
基准种子：`seed=42`  
公式源：`EconomyBalanceParams` + `RPMCalculator`（Unity / balance 共用）

## 问题（校准前）

| 指标 | 旧值 | 问题 |
|---|---|---|
| PEK-LHR 日利润 | ~62.8 万美元 | 单班利润夸张 |
| 最小航司年净利 | ~19,788 万美元 | 开局 500 → 年末 2 亿，无经营压力 |
| 座位假设 | 全线固定 290 | 短途窄体也被当宽体 |

## 校准手段

1. **收入**：RPK × Yield（经济舱 $0.072/RPK，商务舱 $0.145/RPK）
2. **油耗**：窄体 2.6 t/千公里，宽体 5.4 t/千公里
3. **机场费**：基础 + 航程斜率 + 杂费
4. **机组**：按 block hour × 疲劳加成
5. **可变维修**：按飞行小时；机队月维护随机龄上浮
6. **固定开销**：95 万美元/月（原 80）

## 校准后（seed=42）

| 指标 | 新值 |
|---|---|
| PEK-LHR 日收入 / 成本 / 利润 | 17.69 / 6.24 / **11.45** 万美元 |
| 年收入 | 5,155 万美元 |
| 年成本 | 3,732 万美元 |
| **年净利** | **1,423 万美元** |
| 月均净利 | ~119 万美元 |
| 年末现金 | 1,923 万美元（开局 500） |

相对行业粗标（5 机支线/区域航司年利润数百万～一两千万美元量级）：  
1,423 万美元 ≈ **$14.2M**，落在可接受学习游戏区间（目标偏差 &lt; ±30% 的宽松带内偏乐观，但仍有现金压力空间）。

## 回归

```powershell
cd tools/balance
dotnet run -- --seed 42 --year 1 --out out
# 对比 baseline/summary_y1_seed42.txt
```

改 `EconomyBalanceParams` 后若有意接受新结果，覆盖 `baseline/` 即可。

## 后续修订 · 存量人力成本接入（2026-10-07）

成本模型新增一条**存量人力成本**（此前只作 HR 展示/比率，从不扣现金）：

```
7. 人力成本：HrEconomics.LaborCostPerAircraftWan(240) × 在役机队，逐月计入 total_cost
```

- 机组（第 4 项，按 block hour 可变）与人力（第 7 项，按人固定）互补，**不是**同一只桶。
- 每机在役固定开销 `FixedOpexPerAircraftWan` 15 → **0**：其「每机固定负担」已由人力这条更真实的线承担，保留会重复计提。
- 回归锚点漂移（seed=42）：年净利 1306 → **1006 万**（人力 1200，其中 900 由归并的每机开销抵消）；`pnl.csv` 新增 `labor_cost_wan` 列。
- 60 组合压测：GREEN 54→47、BANKRUPT 4→8、YELLOW 2、RED 3；危险组合仍全落「滞胀」最坏宏观。
- 基线已刷新：`baseline/summary_y1_seed42.txt`、`baseline/pnl_y1_seed42.csv`。

## 后续修订 · 航线周期固定成本接入（2026-10-09）

**问题**：成本模型只有**按班**变动成本（油/机场/机组/可变维修，`RPMCalculator`），
一分钱「按周期」的航线固定成本都没有。于是日利 = 单班毛贡献 乘 班次 乘 调度乘数，
三个因子恒为正 → **日利的符号与班次无关**，加密只能放大盈亏，永远不可能把亏损转成盈利。

**真实基线缺陷**（同一问题的人为放大器）：四处调用点把亏损基准当成「未设置」，
写成 `baseDailyProfit > 0.01f ? base : before` 再补 `if (baseline <= 0.01f) baseline = 1f`，
凭空生成 +1 万/班的盈利基准并写回 `baseDailyProfit` → 亏损航线**一次调令即永久转盈**。
四处：`CapacityDirectiveLogic`、`SeasonReviewLogic`（2 处）、`OpsPanelUI.RecalcAllRoutes`；
同类漏网：`InboxManager`（批准排班）、`OpsScheduleLogic.ApplyWaves`（负基准被写成 0）、
`CapacityDirectiveLogic.Revert`（`baseline > 0.01f` 门槛使亏损航线退回时根本不重算）。
现统一走 `OpsEconomics.ResolvedSingleFlightBaseline(route)`：负值原样透传，仅 |值| ≈ 0 才兜底。

**新增成本项**：

```
8. 航线周期固定成本：OpsEconomics.RouteFixedDailyWan(航程, 机型) 万/日/线，与班次无关
   = RouteFixedBaseDailyWan(0.06) * (1 + 航程/6000) * (宽体 1.3)
日净利 = 单班毛贡献 * (周班次/7) * 调度乘数 - 周期固定成本
```

- 与按班成本互补、**不重复计提**：只承担「航线存在即发生」的外站场站/本地销售分销/航线管理与航权时刻维持。
- 因末项与班次无关，加密可**改变盈亏方向**：`BreakEvenDailyFlights = F / (单班毛贡献 * 调度乘数)`。
  单班毛贡献非正时返回无穷大 —— 亏损航线加密救不回来，与旧缺陷正好相反。
- 量级档位：PVG–SHA(1080km 窄体) 0.07 万/日；PEK–LHR(8150km 宽体) 0.18 万/日。
- 复核命令：`dotnet run -- --routefixed`（F 档位表 + 摊薄演示 + 盈亏平衡班次 + 负基准护栏）。
- 不变量：各班次 `RouteFlight.dailyProfit` 存**毛贡献**（不含固定成本），
  固定成本只在航线层级扣一次；`RouteData.RecalcDailyProfit()` 与 `ApplyToRoute` 口径一致且幂等。

**调参旋钮（2026-10-09 追加）**：`RouteFixedBaseDailyWan` 已由 `const` 改为**可运行期覆盖的 static**，
默认值固化在 `RouteFixedBaseDailyWanDefault = 0.06f`（回归锚点不变），因此**扫参无需重新编译**：

```
dotnet run -- --routefixed-sweep                 # 默认扫描集 0.02/0.04/0.06/0.08/0.10/0.12
dotnet run -- --routefixed-sweep 0.04,0.06,0.09  # 自定义
dotnet run -- --routefixed --routefixed-base 0.10   # 任意模式套用覆盖值
dotnet run -- --stress --routefixed-base 0.10       # 直接扫 60 组合压测
dotnet run -- --routefixed-base 2                   # 非法值 → 报错退出码 2，不静默回退
```

- 取数契约：`SetRouteFixedBaseDailyWan` 仅接受 `(0, RouteFixedBaseDailyWanCeiling = 1]` 内的有限值，
  非法输入**不写入**并返回 `false`——不做静默夹取，避免扫参拿到与请求不符的曲线。
- 线性性：基准翻倍 → 各档 F 翻倍 → 盈亏平衡班次翻倍（`--routefixed-sweep` 表可直接读出，
  实测 0.06 → 1.25 班/日、0.12 → 2.50 班/日），故「基准」是唯一且单调可解释的难度旋钮。
- 复位：`ResetRouteFixedBaseDailyWan()`，`--routefixed-sweep` 结束后自动复位。Unity 侧另有
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 钩子，在关闭 Domain Reload 时保证
  每次进入 Play 都从出厂默认开始（以 `#if UNITY_5_3_OR_NEWER` 包住，balance 工具不定义该宏故不参与编译）。
- 复现性：`summary_y1_seed42.txt` 新增 `routefixed_base` / `routefixed_base_default` /
  `routefixed_base_overridden` 三个字段；压测报告「范围」小节也会标注当次所用取值。

**扫参实测（1080km 窄体；单班毛贡献钉在 0.081 万/班 = 出厂默认下的边际航线，故各行可比）**：

| 基准(万/日/线) | 1班/日 | 2班/日 | 3班/日 | 7班/日 | 平衡班次 |
|---|---|---|---|---|---|
| 0.02 | +0.04 | +0.09 | +0.15 | +0.37 | 0.36 |
| **0.06（默认）** | **−0.01** | +0.04 | **+0.10** | +0.32 | **1.25** |
| 0.10 | −0.06 | −0.01 | +0.05 | +0.27 | 2.14 |
| 0.12 | −0.08 | −0.03 | +0.03 | +0.25 | 2.50 |

默认行即「1 班亏 / 3 班赚」的招牌演示；0.12 时连 2 班/日都救不回来，机制更硬。
压力侧：0.10 → GREEN 42 / BANKRUPT 15（基线 43 / 14），旋钮确实能整体平移难度。

**回归锚点漂移（seed=42）**：年净利 1006 → **890.88** 万；年末现金 1481.08 → 1365.88；
新增 `net_route_fixed_cost=115.20`（万/年，全网合计）；`net_daily_profit` 7.62 → 7.31。

**60 组合压测**：GREEN 47 → 43、BANKRUPT 8 → 14、RED 3 → 1、YELLOW 2，危险组合 13 → 17。
这是引入真实固定成本的**必然代价**（最坏宏观下边际组合被固定成本推过盈亏线）。
若需回到旧分布，唯一杠杆是调小 `EconomyBalanceParams.RouteFixedBaseDailyWanDefault`
（扫参命令见上方「调参旋钮」；实测 0.04 → 危险 17、0.02 → 危险 14；但 0.02 时翻转带只覆盖单班毛贡献 &lt; 0.024 万/班，机制在游戏中近乎不可见）。

- 基线已刷新：`baseline/summary_y1_seed42.txt`、`baseline/pnl_y1_seed42.csv`。
- Unity 侧自检见 `LogicSelfTests`（周期固定成本 摊薄 / 护栏 / 不变量三段日志），
  EditMode 用例见 `OpsEconomicsTests`（固定成本档位、摊薄翻转、负基准不透传、不变量幂等）。
