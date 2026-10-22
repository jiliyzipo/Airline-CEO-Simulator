# P2 命令台回归 + 机场批次 A + 接管档案经济（2026-10-21）

> 角色口径：Cursor 只写“已实现，待检测”。本文不含任何“已验收 / Codex 已通过”的结论。

## 1. P2 引擎内回归（K01.1 / K01.3）

用户在本机 Unity 菜单执行 `AirlineCEO → Run Logic Self Tests`，控制台汇总行：

```text
CEO 命令台 → 页面映射=True　可行性=True（PEK—JFK）　缺机场拒答=True　……　同句去重=True（1/1）　汇报件无需批复=True　✓
UnityEngine.Debug:Log (object)
AirlineCEO.EditorTools.CeoCommandRegression:Run () (at Assets/Scripts/Editor/CeoCommandRegression.cs:410)
```

- 58 项断言全部 True（含 `答复件=True`、`同句只更新=True`、`逐班号=True（NV2102）`、`公司整体→顾问=True`）。
- 未留痕：K01.4～K01.7 四条手测路径（关线三步 / 运力调令 / 问句引导 / 人事页问公司整体）。K01 总项因此**不勾选**。
- 本轮之前修掉的两个口径错误（已在本行验证）：
  1. 断言读 `gs.inbox[Count-1]` 取到的是最旧一封；`InboxManager.Add` 是 `Insert(0)`，改为 `gs.inbox[0]`。
  2. 单波航线取“末波航班号”会退回基础班号；新增 2 波夹具 `R-WAVE-1`（NV2100 / NV2102）。

## 2. 机场批次 A（K03 / K04）

新增四座国内机场，三表 + 中国区归属同步：

| IATA | 中文名 | pax（百万人次） | cargo（万吨） | gdpUsdBn | popMillion | odIndex | 坐标（WGS-84） | tier |
|---|---|---|---|---|---|---|---|---|
| ZUH | 珠海金湾 | 12.97 | 3.43 | 62 | 2.5 | 68 | 113.3756, 22.0064 | 2 支线 |
| WUX | 无锡硕放 | 10.50 | 17.35 | 233 | 7.5 | 74 | 120.4294, 31.4944 | 2 支线 |
| NGB | 宁波栎社 | 14.73 | 16.86 | 252 | 9.7 | 76 | 121.4619, 29.8267 | 2 支线 |
| WNZ | 温州龙湾 | 12.53 | 12.93 | 135 | 9.8 | 70 | 120.8522, 27.9122 | 2 支线 |

- **数据来源**：CAAC《2024 年全国民用运输机场生产统计公报》（2025-03-14 发布）。`paxMillion` / `cargoWanTon` 为公报实际值（万吨以下四舍五入到两位）；`gdpUsdBn` / `popMillion` / `odIndex` 为量级值，口径与既有城市条目可比，已在 `CityEconomyCatalog` 内注释标明。
- 推导结果（独立探针实测）：四场均 `tier=2 支线`、`coordinated=false`、`curfew=""`、槽池 `{4,4,4,2}`。
- 三表规模：机场 98 / 城市经济 98 / 坐标 98。
- 航距未回退 `UnknownKm`（2200km）：PEK–ZUH 2032.4 / PEK–WUX 1015.4 / PEK–NGB 1223.1 / PEK–WNZ 1408.5 km。
- 连带影响：`PresetAirlines.AllSelectable()` 按经济目录建司，故新增 4 家 `hub_ZUH/WUX/NGB/WNZ`（2 星、城市「通航」）。`NextCode` 按 `used` 集合去重，IATA/ICAO 由构造保证不撞号；该性质已写成断言（见下），**尚未在引擎内执行**。

## 3. 目录外反例迁移（K04.1）

`珠海金湾` 入表后，原先“目录外机场”夹具会静默变成正例。两处夹具已改为仍未收录的 `佛山沙堤`（FUO）：

- `Unity Project/Assets/Scripts/Editor/CeoCommandRegression.cs`（⑤b + ⑧b 公函正文）
- `tools/cmdcheck/Program.cs`（`目录外关线`）

`cmdcheck` 实测：`PASS 目录外关线 text=北京大兴-佛山沙堤`。

## 4. 新增隔离回归

`Unity Project/Assets/Scripts/Editor/AirportCatalogRegression.cs`，已挂到 `LogicSelfTests.Run()`（紧随 `CeoCommandRegression`）。断言：

1. 三表 IATA 集合双向相等、组内无重复；
2. 每座机场都有坐标且在合法范围，中国机场落在东亚包围盒（防经纬写反）；
3. 批次 A 四场：中文名、`tier=2`、非协调、无宵禁、槽长 4、pax/cargo 与公报一致、按码/中文名/城市名三种写法都能解析、PEK 航距落在预期区间；
4. 反例 `佛山沙堤` / `FUO` 仍在目录外（三表皆无）；
5. `hub_*` 接管航司存在、司码 IATA/ICAO 唯一、选司数 == 经济表条数。

**状态：2026-10-21 已在引擎内执行通过。**

```text
机场底表三表贯通 → 机场=98 城市经济=98 坐标=98　码无重复=True　三表同集合=True　坐标可用=True　批次A入表=True（ZUH/WUX/NGB/WNZ）　目录外反例=True（佛山沙堤）　接管航司=98　司码唯一=True　新枢纽有司=True　✓
AirlineCEO.EditorTools.AirportCatalogRegression:Run () (at Assets/Scripts/Editor/AirportCatalogRegression.cs:180)
AirlineCEO.EditorTools.LogicSelfTests:Run () (at Assets/Scripts/Editor/LogicSelfTests.cs:1860)
```

## 4b. 新增离线工具 `--routeinventory`

`tools/balance/RouteInventoryReport.cs`，入口 `dotnet run --project tools/balance/AirlineCEO.Balance.csproj -- --routeinventory`。

`--frequniverse` 只出**分布**（直方图、分带占比），答不了“某座机场入表后到底哪些航司的网络变了”。新工具出**航线级明细**（`#hub\tdest\tkm\tweekly\taircraft`），并断言：

- 新机场**未被外站航司**纳入目的地（= 既有航司的候选池没有把别人顶掉）；
- 每座新机场的**本场**航司确实展开了出港航线（> 0，防“入表但没生成网络”）；
- 附「按星级」的接管读数（家数 / 机队 / 航线 / 现金 / 现金跑道），用于看难度梯度是否被加厚。

这是长期能力：批次 B 换个 `Watched` 名单即可复用。

## 4c. 界面手测反馈（2026-10-21，4 项已修）

用户首轮手测报了 4 条，均已改：

| # | 报障 | 根因 | 修法 |
|---|---|---|---|
| 1 | 只有点「选择」才出下拉，输入机场名不出；列表过宽 | `RebuildList` 只在「列表已展开」时才跟随 `onValueChanged`；锚点 x 铺满整行（输入框只占 0–0.72） | 输入非空即 `OpenPicker()`；宽度等于输入框实际宽度；回填路径 `_suppress` 抑制误触发 |
| 2 | 输入 `NGB` 出现「重庆江北 CKG」 | `nameEn.IndexOf` — `Chongqing Jia(ngb)ei` 内含 `ngb` | `AirportCatalog.MatchRank/Filter`：英文只按词首，中文允许包含，代码前缀优先；`TryResolve` 与下拉同源 |
| 3 | 下拉被下方输入框覆盖 | 列表是字段子节点，兄弟字段后建即盖住 | 列表挂到卡片、展开 `SetAsLastSibling`，位置按输入框矩形现算（空间不足向上展开） |
| 4 | 邮件「国内不涉航权」与「先拿下航权」矛盾 | 时刻槽不足的结论文案不分国内/国际 | 按 `international` 分岔 |

离线护栏（已跑）：`cmdcheck` **53 PASS / ALL PASS**（新增 7 条：NGB 不串 CKG、英文词首、中文、代码前缀、尾码 `GB`、空查询、无匹配）；`semcheck` `errors=0 / tests-asm=0 / enc=0`；`balance` 0 错误。
引擎内护栏：`AirportCatalogRegression` 增 `搜NGB不串CKG=...`；`CeoCommandRegression` 增 `国内线不提航权=...`。

**未复验**：界面观感（下拉位置/宽度/遮挡）只能人眼确认，等用户下一轮截图。

## 4d. 接管选司读数：现金跑道（K08，2026-10-21 修复）

**缺陷**：`TakeoverWorldBuilder.FillWorld` 用
`cash / Mathf.Max(1f, monthlyCost − monthlyRevenue × 0.35)` 算跑道。收入可抵部分覆盖成本时，分母被夹成 1，
`cash / 1` 就是现金数本身 —— 于是★3 报 **1200.0 月**、★4 报 **2000.0 月**。玩家在选司界面据此比难度，比的实际是「谁现金多」。

**落地**：

- `AirlineProfile` 增 `monthlyNetBurn` / `cashRunwayUnbounded`；后者为 true 时 `cashRunwayMonths` 不作数字读（注释写明）。
- `TakeoverWorldBuilder.RunwayText(p)` 成为唯一展示口径：`无净烧钱` / `120 月以上` / `X.X 月`。
- `AirlineOverviewUI.Metrics` 与 `BuildIssues` 改用它 / 加守卫（不烧钱的航司不再报「现金跑道告急」）。
- `--routeinventory` 增断言：无上限 ⇔ 净烧钱 ≤ 0；有上限 ⇒ `cash / netBurn` 与读数一致。

**实测（98 家）**：

| 星级 | 家数 | 净烧钱均（万/月） | 跑道 |
|---|---|---|---|
| ★2 | 53 | 20.0 | 24.9 月 |
| ★3 | 33 | 1.0 | 120 月以上 |
| ★4 | 6 | −27.3 | 无净烧钱 |
| ★5 | 6 | 61.5 | 8.4 月 |

**遗留（未改，属平衡裁定）**：★3 净烧钱仅 1.0 万/月、★4 全体为负，是档案模型（`monthlyCost = max(cash×0.08, 机队×4.5 + 航线×2)`、收入按 35% 抵减）的粗糙所致，不是跑道公式的错。要让星级真对应燃钱速度，得重定成本与抵减系数。

> 该裁定已在 K09 执行（见 §4e），本节数值仅作修复过程记录。

## 4e. 星级分档重定（K09，2026-10-21）

**结论**：预览净烧钱与账本不同源是根因，K08 只是把它显形了。现改为账本同源公式，并按「目标跑道」反推各档开局现金。

| 星级 | 家数 | 现金（万） | 净烧钱（万/月） | 跑道 | 改动前 |
|---|---|---|---|---|---|
| ★2 | 53 | 1120（原 500） | 138.8 | 8.1 月 | 3.6 月 |
| ★3 | 33 | 1770（原 1200） | 284.0 | 6.2 月 | 4.1 月 |
| ★4 | 6 | 3140（原 2000） | 750.9 | 4.2 月 | 2.6 月 |
| ★5 | 6 | 4400（原 500） | 1418.4 | 3.1 月 | **0.3 月** |

**公式**：`净烧钱 = (80 + 机队×2) + Σ单机维护 + Σ月租 + 机队×20 − Σ航线日利×30×0.85 − Σ过渡日收入×30`
（`(80+机队×2)` 即 `HqOverhead`，会被 `Apply` 原样写进 `gs.monthlyBurnRate`）

**顺带修掉的口径分叉**：`FleetManager.MonthlyMaintOf` 用机龄 5%/年，`FleetEconomics` 与 UI 用 6%/年 ——
同一架飞机在 FleetUI / FleetSubpagePanel 印两个数、账本按第三个数扣钱。已收拢到
`FleetEconomics.MonthlyMaintenance`，费率取 5%（以账本为准，不动经济数值）。

**需要 Codex 重点看的两点**

1. 各档开局现金从 500/1200/2000/500 上调为 1120/1770/3140/4400 —— 这是**难度改动**，不是 bug 修复；
   依据是「★N 的目标跑道」表（`TakeoverWorldBuilder.TargetRunwayMonths`）。若设计上不接受，只需改回现金，
   断言会立刻以「跑道掉出 ±20% 带」报红。
2. `--fleetcheck` / `--frequniverse` / `--routeinventory` 全过，但 `tools/balance/baseline/summary_y1_seed42.txt`
   （README 记 2026-08-12）在本轮**之前**就已过期（closing 1365.88 vs 现 1707.90），本轮没有动它。

## 4f. K10 · 接管局跨季度 Runtime 用例（2026-10-21，第二版待运行）

**文件**：`Unity Project/Assets/Tests/PlayMode/TakeoverSolvencyTests.cs`

**为什么需要它**：`--routeinventory` 与 `FillWorld` 用的是同一份公式；若 `Apply` 落盘写错字段、
或 `GameState` 的派生属性（机组乘数 / 停飞剔除 / 过渡业务日结）与预览分叉，离线断言看不见。

**第一版教训（必读）**：首版在 `DaysPerQuarter×1440` 之后取现金，而这一段包含**季末结算**
（还本付息 / 外部事件 / 险损都在最后一天一次性发生），于是 6 用例 5 红。
「每月烧 X 万」只能对着**季内逐日计提**去量。现分两段：

| 段 | 长度 | 含什么 | 断言 |
|---|---|---|---|
| 季中 | 89 天 | 只含逐日计提 | 实扣 = 经营烧钱 × 89/30（±10%） |
| 季末 | 1 天 | 还本付息、外部事件、险损、联盟 | 不 NaN、跨季成功、不即死；差额与新增日志全部打印 |

其余断言：经营烧钱 ≤ 预览 × 1.02（预览不得比账本乐观）、≥ × 0.70（口径不得分叉）、
预览月均还本付息 = 落盘贷款月供（±0.5）、`cashRunwayMonths = cash / monthlyNetBurn`。
每个场景另打印每条航线的**执飞资格**（机号 / 班次 / 班期 / 是否持续性停飞），
用来区分「航线根本没飞」与「钱没按账扣」。

**首轮查出的真实缺陷：跑道漏掉还本付息**

| 档 | 现金（旧） | 月供 | 操作跑道 | 含债务跑道 | 偏差 |
|---|---|---|---|---|---|
| ★2 | 1120 | 24.1 | 8.1 月 | 6.9 月 | −15% |
| ★3 | 1770 | 70.9 | 6.2 月 | 5.0 月 | −20% |
| ★4 | 3140 | 150.9 | 4.2 月 | 3.5 月 | −17% |
| ★5 | 4400 | 249.4 | 3.1 月 | 2.6 月 | −15% |

★5 的 2.6 月压在「跑道 < 3 月触发危机」线以下却毫无预警。用户裁定：并入跑道并反推现金。
新增 `AirlineProfile.monthlyDebtService` + `TakeoverWorldBuilder.SumDebtServiceMonth`
（排除 equity 型与已还清），现金按不动点
`cash = 目标月数 × 操作烧钱 / (1 − 0.0616 × 目标月数 × 负债率)` 重解为
**1342 / 2243 / 3719 / 5127 万**，跑道精确回到 8.0 / 6.0 / 4.0 / 3.0。
偿付项**不**并进 `monthlyCost`/`monthlyFixedCost`：那两个会被 `Apply` 写进 `gs.monthlyBurnRate`，
并进去就与 `LoanManager` 的季末扣款重复计一次。

**只压两季而非四季的理由**：★5 的设计跑道就是 3 个月，无动作活不过第三季是难度本身。
用「4 季存活」当验收会逼着把难度调平，目标错误。本用例钉的是**读数可信度**。

**状态**：`tests-asm: errors=0`；`--routeinventory` 16 项断言全 PASS、`cmdcheck` ALL PASS。
**未运行第二版**（需 Test Runner → PlayMode；Unity 由用户占用，未启动第二实例）。

## 5. K05 · 批次 A 的平衡影响（前后对比）

工具：`tools/balance`（离线，与接管界面同源：`PresetAirlines.AllSelectable()` + `TakeoverWorldBuilder.BuildPreview()`）。

| 指标 | 改动前 | 改动后 | 差异 |
|---|---|---|---|
| 可接管航司 | 94 | 98 | +4 |
| 展开航线 | 742 | 750 | +8 |
| 中国境内枢纽 | 26 | 30 | +4 |
| ★2（最易档） | 49 | 53 | +4（+8.2%） |
| ★3 / ★4 / ★5 | 33 / 6 / 6 | 33 / 6 / 6 | 0 |

- **既有航司未被顶替**：`BuildDestinationPool` 按需求降序取前 N，新城市未进入任何**外站**航司的前 N，因此没有挤掉既有目的地。新增断言（`--routeinventory`）直接钉这条：带外站航司飞入即为 FAIL。
- **四座新枢纽的接管读数**：★2 · 机队 3 · 航线 2 · 现金 500 万 · 负债率 35% · 现金跑道 24.9 月 · 机组缺口 5% · 遗留 3 项。
  > ⚠ 该行是**批次 A 当轮**（K08 之前）的读数，现金/跑道口径已被 K09 与 K10 两次修正。
  > 现值：现金 **1342 万** · 净烧钱 167–169 万/月 · 跑道 **8.0 月**（含还本付息 29 万/月）。见 §4f。
- 展开航线：

```text
ZUH→CAN 154km 10班/周 A320neo        ZUH→PVG 1316km  9班/周 B737-800
WUX→PVG 136km 11班/周 A320neo        WUX→ICN  862km  9班/周 B737-800
NGB→PVG 150km 13班/周 A320neo        NGB→ICN  965km 10班/周 B737-800
WNZ→PVG 371km 11班/周 A320neo        WNZ→ICN 1183km  9班/周 B737-800
```

### 需要裁定（未擅自改）

1. **★2 档被持续加厚**：`StarsFor` 只按 pax 40 / 70 百万分档，国内二线机场（10–15 百万）全部落 ★2。批次 A 把最易档从 49 推到 53；批次 B 若再加 8 座同类城市将到 61/106（58%）。不影响数值平衡，影响选司界面的难度分布观感。
2. **超短航段**：ZUH–CAN 154 km / WUX–PVG 136 km / NGB–PVG 150 km 过了 `MinPassengerKm = 100f`，但现实靠高铁、无定期客运。同类样本此前已有（TSN–PEK 125 km、SHA–HGH 138 km、MFM–CAN 141 km）；批次 A 把 <250 km 的航线从 11 条增到 14 条。是否抬到 200 km 或加高铁走廊排除，属设计裁定。
3. **现金跑道显示退化**（既有问题，非本次引入）：★3 报 `1200.0` 月、★4 报 `2000.0` 月，等于现金原值——`Mathf.Max(1f, 月成本 − 0.35×月收入)` 在收入覆盖成本时把分母压成 1。

### 一项重要口径说明

60 组合压测（`--stress`）结果与 10-17 基线同值（45/13/2/0），**但这不能当作批次 A 健康的证据**：`StressTestRunner` / `AirlineYearSimulator` 全仓不引用 `AirportCatalog` / `CityEconomyCatalog` / `PresetAirlines`，对“加机场”结构性不可见。本次真正有效的是 `--frequniverse` / `--fleetcheck` / `--routeinventory`。

### 未覆盖

接管局**连续跨季度 Runtime** 经营（现金跑道、利润、运力、异常状态）需 Unity PlayMode，离线工具无此入口。本轮只记录了接管初始读数。

---

## 6. 本轮验证记录

| 命令 | 结果 |
|---|---|
| `node tools/semcheck/run.js` | errors=0（raw=0）· tests-asm errors=0 · enc errors=0 |
| `dotnet build tools/semcheck/semcheck.csproj` | 0 警告 0 错误 |
| `dotnet run --project tools/cmdcheck/cmdcheck.csproj` | ALL PASS（53 条，含 8 条机场搜索/批次 A 断言） |
| `dotnet run --project tools/balance/AirlineCEO.Balance.csproj` | 可运行，seed 42 年循环正常 |
| Unity `Run Logic Self Tests`（第 1 次） | `CEO 命令台 → … ✓`（58 项） |
| Unity `Run Logic Self Tests`（第 2 次） | `机场底表三表贯通 → … ✓`（原文见 §4） |
| `dotnet run -- --frequniverse` | 98 家 / 750 条 · 直方图 28 档 · 断言全 OK |
| `dotnet run -- --routeinventory`（新增） | 98 家 / 750 条 / 87 座目的地 · 断言全 OK（含现金跑道口径） |
| `dotnet run -- --fleetcheck` | exit 0，无机可配 0 条、机型飞得到航距 |
| `dotnet run -- --stress` | GREEN 45 / BANKRUPT 13 / RED 2 / RUNAWAY 0，exit 1（见 §5 口径说明：该压测不读机场表） |

---

## 7. 未做 / 待接手

1. **开线下拉与手输、地图标签**三处对新机场的界面手测（数据层与命令台已验，界面未点）。
2. **接管局连续跨季度 Runtime**（现金跑道 / 利润 / 运力 / 异常状态）：需 Unity PlayMode，离线工具无此入口，K05.2 只记录了初始读数。
3. **K06 的三条前置裁定**：批次规模、是否接受“一城一司”、以及 §5「需要裁定」里的 ★2 分层与超短航段两根刺。
4. **K02 阶段收口**（`stage-close` / DEL / Codex 独立检测）——等用户明确指令，未自动执行。
5. 现金跑道显示退化（`Mathf.Max(1f, …)` 分母压成 1）——既有问题，建议单开一项。
6. 未 commit / 未 push。
