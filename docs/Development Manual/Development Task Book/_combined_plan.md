# Airline CEO · 三轨并一开发流程计划

三轨：A. UI 补齐对齐 / B. 数值平衡调优 / C. Prefab UI 基础设施重构 → 统一成可执行的五阶段渐进交付路线。


## 现况基线（2026-08-12 更新）

| 维度 | 现状 | 差距 |
|---|---|---|
| 后台逻辑 | Phases 1–13 全链路无 TODO/stub，86 个 C# 脚本 ~13,700 行 | 三大系统（MacroEconomy/RandomEvent/AI 竞对）Runtime 数据源已接线；油价数据源已统一口径（对齐 `tools/balance`） |
| UI 覆盖 | 白板 **103** 页（L0–L5）；OPS / FIN / FLEET+NET / MKT A+B / STG / MNT / HR / SAF 子树金标准 ✅；BOARD / CROSS 多数仍占位；大量 L2–L5 无完整交互 | **交互开发必须覆盖全部层级，不能只做 L0/L1** |
| UI 架构 | Prefab 规范 + NavigationRouter + 部分运行时工厂；旧 UIFactory 兼容层仍在 | 复用组件需按部门继续扩展 |
| 数值 | Phase 1.3 RPM 校准 + OPS 年闭环 + FIN 年闭环 + FLEET/NET 年闭环 + MKT A/B 年闭环 + STG/MNT 年闭环 + HR/SAF 年闭环已入 `tools/balance` | 其余部门经济模型待闭环（BOARD/CROSS） |
| 测试 | EditMode + PlayMode 框架已有；OPS ≥5 + FIN ≥3 + FLEET/NET ≥4 + MKT A ≥3 + MKT B ≥3 + STG+MNT ≥4 + HR+SAF ≥4 条 PlayMode | 每部门需 3+ PlayMode |

**权威页面清单（随白板同步）：**  
[`_page_inventory.md`](_page_inventory.md) — 由交互白板 CATALOG 生成，含每页层级/归属/交互状态。


## 总体原则

1. **全量交互，不止 L0/L1**：凡白板 CATALOG 中出现的页面（L0 壳/开局、L1 一级、L2 子屏、L3 工具台、L4 弹窗、L5 确认表）都必须进入本计划并交付可点交互（入口、返回/×、主操作、数据回写）。禁止「只做底栏 Tab + L1 占位」即算部门完成。
2. **边建边跑**：每做完一个部门 UI（含其全部子页），立刻跑该部门数值模拟。
3. **Prefab 先于 UI 量产**：先组件库，再用 Prefab/`UIConfig`/`OpsRuntimeFactory` 类回退搭建页面。
4. **数据驱动 UI**：先 Model / ScriptableObject，UI 只渲染与派发操作。
5. **物理分流**：数值用 `tools/balance`；UI 在 Unity；可部分并行。
6. **白板即验收标准**：交互变更必须回写 [`Airline_CEO__UI交互白板 V1.0.html`](../Game%20UI/Interface%20Planning/Airline_CEO__UI交互白板%20V1.0.html)，并刷新 `_page_inventory.md`。


### UI 交互交付定义（每页）

一页算「交互完成」须同时满足：

| # | 要求 |
|---|---|
| 1 | 有稳定 `pageId` + `Resources/UIConfig`（或运行时 Register） |
| 2 | Prefab 或运行时工厂可打开；正文可用（非纯「占位」文案） |
| 3 | 白板 `clicks` 所列主跳转可达（含 ← 退一层 / × 关整层） |
| 4 | 关键操作写回 `GameState` / 对应 Manager，并可在决策日志或 UI 上看到结果 |
| 5 | 至少 1 条自动化测试覆盖该页导航或核心逻辑（部门合计 ≥3 条 PlayMode） |

部门金标准 = **该部门桶内全部页面**满足上表 + 数据模型 + 离线年闭环 + PlayMode≥3。


---

## Phase 0：基础对齐（第 1 周）

**目标**：冻结 Prefab 规范、铺平数据模型、建立自动化测试管道。  
**状态：✅ 已完成（2026-08-11）** — 数值校准留待 Phase 1.3。  
**复核（2026-08-12）**：四项主体 ✅；残留已清（`Widget_Badge.prefab` 落盘、`PREFAB_SPEC` 对齐 L0–L5、已删 `Fonts/Tests/Tests.asmdef`）。

| 子项 | 复核结论 | 证据 |
|---|---|---|
| **0.1** Prefab 规范 | ✅ | 四目录 · `PREFAB_SPEC.md` · Framework 脚本 · 五原子组件（4 `Widget_*.prefab` + `Template_ListItem.prefab`，含 `Widget_Badge`） |
| **0.2** 数据 Model | ✅ | `UIConfig` · `UiDomainModels` · `NavigationRouter` · SO ≥10 · `Phase 0/Verify Foundation` |
| **0.3** 离线模拟 | ✅ | `dotnet build` 0 error；`compare_baseline.ps1` → OK |
| **0.4** 测试框架 | ✅ | Runtime/Editor/EditMode/PlayMode asmdef · `MockGameState` · 基线 Edit/Play 测试 |

### 0.1 Prefab UI 组件库规范制定 → Track C ✅
- 目录：`Assets/Prefabs/UI/{Panels,Modals,Widgets,Templates}`
- 规范：`Assets/Prefabs/UI/PREFAB_SPEC.md`（目录按用途；层级由 `UIConfig.level` 表达 L0–L5）
- 脚本：`BasePanel` / `ScrollListPanel` / `DecisionModal` + Widget（含 `BadgeIconView`）
- Prefab：`Widget_Badge.prefab` 已落盘；Phase 1 菜单可重生

### 0.2 数据 Model 补齐 → Track A+C ✅
- `UIConfig` ScriptableObject；`NavigationRouter`；首批 UIConfig 菜单
- 域模型：`AircraftTemplate` / `RouteTemplate` / `StaffProfile` / `AuditDef` / `TermDef`（`UiDomainModels.cs`）

### 0.3 离线数值模拟框架 → Track B ✅
- `tools/balance/`（.NET 8，Link Unity Logic）；`compare_baseline.ps1`
- 复跑：`OK: summary matches baseline`

### 0.4 自动化测试框架 → Track B ✅
- EditMode / PlayMode asmdef；`LogicPipelineTests`；`NavigationAndTickPlayModeTests`


---

## Phase 1：核心导航骨架 + 开局流（第 2–3 周）

**目标**：L0/L1 **壳与导航**可用；开局 L0 流（n1–n5）完整交互；明确后续部门必须下钻 L2–L5。  
**状态（2026-08-12 复核 · 对齐白板）**：1.1 / 1.2 / 1.3 ✅；**1.4 🟡 最小可玩开局流已接通**（对照白板主路径；深度打磨续作）。  
**状态（2026-08-14 更新）**：**顾问开局引导线（S0–S6）已落地**——顶栏「顾问」由 `AdvisorModalUI` 承载全流程，收件箱双栏、机队分配等待态、S4 排班调整弹窗、公司中文名校验均已接通并回写白板。

> 壳层完成 ≠ 部门完成：底栏能进 L1 只算「入口」，该部门金标准仍要做完桶内全部 L2–L5。  
> **白板权威**：[`Airline_CEO__UI交互白板 V1.0.html`](../Game%20UI/Interface%20Planning/Airline_CEO__UI交互白板%20V1.0.html) SHELL 桶 11 页。

### 1.1 Prefab 组件库首批 → Track C ✅
- Framework Prefab + `Create Framework & L1 Prefabs` 菜单（含 `Widget_Badge`）

### 1.2 Shell + L1 占位 Prefab → Track A+C ✅（占位 ≠ 白板交互）
- `Panel_Shell_n0`（实为底栏）；七部 + n6/n7/n9/n12/n20 L1 占位 Prefab
- `PrefabNavIntegration` + `NavigationRouter` + `Resources/UIConfig`（壳/L1）
- **已修（1.4）**：默认不再开市场；`n12` 不再误映射运营；默认进 n6

### 1.3 收入/成本闭环首跑 → Track B ✅
- 校准 seed=42 年净利 ~1,423 万；见 `CALIBRATION_1.3.md`
- OPS 层叠加后 baseline 另见 `OPS_YEAR_LOOP.md`（~813 万）

### 1.4 开局与壳层深度交互 → Track A 🟡（2026-08-12 重启交付；2026-08-14 顾问开局引导线落地）

**已落地（最小可玩 · 对齐白板主路径）**

| 页 | 实现 | 说明 |
|---|---|---|
| `n1` | `SetupFlowController` 主菜单 | 新游戏 / 继续读档 / 设置 / 学院 / 退出 |
| `n2`–`n5` | 同控制器分步表单 | 资料→公司→董事会确认→目标确认→开始经营 |
| `n3` | 公司注册表单 | 中文名须为汉字 ≥2 个（可含 ·）；IATA/ICAO 代码校验 |
| `n0` | 顶栏速度 ▶/▶▶/▶▶▶/‖ + 顾问 + 设置；底栏 Shell | ×/← 仍由 Router/`BasePanel` |
| `n6` | `DashboardPanelUI` + `ShellRuntimeFactory` | 空壳舞台；顶栏「顾问」承载引导线 |
| `n7` | `InboxPanelUI` | 双栏列表+详情；发起会议/批准/暂缓/拒绝；引导提案 |
| `n8` | `AdvisorModalUI` + `AdvisorTutorialScript` | 顾问开局引导线 S0–S6 |
| `n40` | `SettingsPanelUI` | F5/F9 存读档（n39 飞行学院 / n50 术语词典已按精简决策移除 2026-08-31） |

**顾问开局引导线（《航空顾问开局引导线》v1.1 · 2026-08-14）**

新档进入主页约 0.5s 自动弹出（`MVPBootstrap.TryStartAdvisorTutorial(0.5f)`），进度持久化于 `GameState.advisorTutorialStep` / `advisorTutorialAwaitPage`：

| 步骤 | 内容 | 接线 |
|---|---|---|
| S0 市场机会 | 深圳—北京 O&D 客流激增 | 顾问弹窗「查看详细评估」 |
| S1 RPM 评估 | 评分 86（绿）/ 日利 16.2 万 / 客座率 81% | 「发起决策会议」→ 收件箱 |
| S2 决策会议 | 财务(审慎)·运营(支持)·市场(强烈支持) | 收件箱点「发起会议」后回弹 |
| S3 分配飞机 | 推荐闲置机型 | 「去机队」→ 机队「分配至深圳—北京」 |
| S4 排班确认 | 自动排班方案 | 「调整班次」→ 频率×时刻编辑弹窗 |
| S5 首航结算 | 起飞→巡航→降落 | 净利润 6.2 万 / 满意度 +5 |
| S6 下一程钩子 | 成都—上海机会 | 结束引导 |

- S4「调整班次」弹窗：班次频率（每日/每周5班/每周3班）× 起飞时刻（早/午/晚/红眼）+「自动排班」+ 预估日利润预览；结果经 `ApplyScheduleToOpenedRoute()` 写回开通航线
- 等待态：收件箱/机队操作完成后由 `ContinueFromInboxAction()` / `ContinueFromFleetAssign()` 回调继续，避免闪跳（修复「闪一下就跳过」）

关键脚本：`Scripts/UI/Setup/` · `Scripts/UI/Shell/{AdvisorModalUI,AdvisorTutorialScript,Dashboard,Inbox,Settings,FlightSchool}*` · `ShellRuntimeFactory` · `Scripts/UI/FleetUI`（引导分配）  
测试：`SetupFlowPlayModeTests`  
Play 入口：进 Play 先见主菜单（`SetupFlowController.BypassForTests` 可跳过）

**仍待加深（非阻塞下一部门）**
- ~~n4 真抽卡选座 / n6 地图微件 / n7 完整邮件模型 / n39 文章详情+XP / n40 显示与语言~~ ✅ 2026-08-20 已加深（见下）
- Prefab 资产化（现以运行时工厂为主；仍延期）

| 页 | 白板要点（主 clicks） | 现状 |
|---|---|---|
| `n0` | 底栏；速度；顾问；设置；×/← | 🟡 已通主路径 |
| `n1`–`n5` | 开局链 | 🟡 最小表单闸门（n3 中文名校验） |
| `n6`/`n7` | Dashboard / 收件箱 | ✅ 地图微件 / 完整邮件模型+期限拦截 |
| `n8`/`n40` | 顾问引导线 S0–S6 / 设置 | ✅ n8 引导线；设置显示/语言/速度默认（n39 学院已移除） |

**1.4 待加深已完成明细（2026-08-20）**
- `n40` 设置：显示模式（全屏/窗口）+ 默认速度（1×/2×/3×）+ 清档（`AppSettings` 新增 `KeyFullscreen`/`KeyDefaultSpeed`；`SettingsPanelUI` 新增两行 + 清档按钮；`SaveManager.ClearAllData`）
- `n6` 地图：`MapWidget`（等距圆柱投影世界地图 + 机场光点 + 枢纽光晕 + 玩家航线弧）；`DashboardPanelUI.BuildStage` 全幅地图舞台（cover 铺满 + 拖拽平移 + 滚轮/按钮缩放 + 三联瓦片横向无限循环）。**2026-08-31 视觉重做 v3（昼夜混合 + 地名层 + 高清化）**：8K 昼/夜双贴图（`earth_blue_marble.jpg` 8192×4096 Blue Marble + `earth_night_lights.jpg` 8192×4096 Black Marble 城市灯光，均 solarsystemscope NASA 衍生 CC-BY），`EarthDayNight.shader` 按 UTC 太阳直射点逐像素混合——**夜区非全黑**：夜光贴图城市灯光发亮 + 叠 16% 日间纹理保留陆地轮廓（对齐参考图）；地名层 `UI/MapPlaceNameLayer.cs`（Google Map 式）：大洲/国家/大城市 4 级 60+ 标签，反缩放（屏幕恒定大小）+ 随缩放分级显隐（大洲恒显 → 国家 1.6× → 大城市 2.6× → 小城市 4×）；清晰度：aniso 16 + 运行时 `_MipBias` 随缩放下压（-1→-4，`tex2Dbias` 采高分辨率 mip），放大不发糊。此前 v2（2026-08-30）：大圆弧三层发光航带（bloom/soft 径向渐变软光斑加色混合，芯线 2.2px，球面 Slerp）+ 光晕仅在航线起终点 + 航班箭标；`GameState.OnStateChanged` 时自动重建
- `n4` 抽卡：`SetupFlowController.RefreshBoardBatchUi` 12 人候选卡（头像色块 + 姓名/年龄/国籍），点选/取消至 8 席；`SetupCatalogs.BoardProfile`/`DetailFor`/`PickBoardBatch`；选中 ID 存 `GameState.boardMemberIds` → `BoardManager` 生成目标
- `n7` 收件箱：`InboxMail` + `InboxManager`（open/due/期限拦截）；`InboxPanelUI` 改用 `GameState.inbox` + 期限显示；`TimeManager.AdvanceQuarter` 检测到期邮件 → 暂停 + 紧急消息
- `n39` 学院：~~`FlightSchoolCatalog` + `FlightSchoolPanelUI`~~ **2026-08-31 按精简决策整体移除**（主菜单按钮、局内路由 `n39_`/`n50_`、词典面板、Tooltip 悬停、`advisorXp`/`schoolReadIds` 存档字段均一并清理）


---

## Phase 2：部门深度 UI（含全部 L2–L5）+ 数值闭环（第 4–10 周）

**策略**：按部门推进。**每周交付该部门在 `_page_inventory.md` 中的全部页面交互**，不得只交 L1。  
**状态**：Week 4 OPS ✅ 金标准（2026-08-12）· Week 5 FIN ✅ 金标准（2026-08-14）· Week 6 FLEET+NET ✅ 金标准（2026-08-14）· Week 7 MKT 批次 A ✅ 金标准（2026-08-15）· Week 8 MKT 批次 B ✅ 金标准（2026-08-15）· Week 9 STG+MNT ✅ 金标准（2026-08-15）· Week 10 HR+SAF ✅ 金标准（2026-08-15）

### 每部门金标准（强制）

1. 桶内 **全部** L1+L2+L3+L4+L5 页交互完成（见上表「UI 交互交付定义」）
2. Prefab / UIConfig 可生成或运行时工厂可打开
3. 部门数据模型完整
4. 离线模拟器跑通该部门年度闭环
5. PlayMode ≥3 条（建议覆盖：导航栈、主操作回写、×/←）
6. 白板 CATALOG / clicks / edges 与实装一致

### Week 4：OPS 运营部 ✅（金标准 · 7 页）

| 层级 | 页面 | 状态 |
|---|---|---|
| L1 | `n13` 运营部 | ✅ |
| L2 | `n66` 航季总纲 · `n42` 周总计划 · `n43` 飞行计划 | ✅ |
| L3 | `n36` 构建航班环 · `n98` 航线/航班环选择器 | ✅ |
| L4 | `n24` 航班异常弹窗 | ✅ |

- UI：`OpsPanelUI` / `OpsSubpagePanel` / `OpsRingBuilderUI` / `OpsRouteRingSelectorUI` / `OpsIrropsModalUI` / `OpenRouteDecisionFlow`
- 数值：`OpsEconomics` + `OpsIrropsLogic`；`tools/balance` OPS 年闭环
- 测试：`OpsEconomicsTests` + `OpsNavigationPlayModeTests`（≥5）
- 菜单：`AirlineCEO → Phase 2 → Create OPS Prefabs & UIConfig`

### Week 5：FIN 财务部 ✅（金标准 · 7 页 · L1–L5）

| 层级 | 页面 | 状态 |
|---|---|---|
| L1 | `n15` 财务部 | ✅ |
| L3 | `n22` 年度预算编制/审批 · `n31` 对冲工作台 · `n32` 碳配额管理 | ✅ |
| L4 | `n25` 紧急融资/破产保护 | ✅ |
| L5 | `n103` 条款确认 · `n106` 重组条件勾选 | ✅ |

- UI：`FinancePanelUI`（L1 总览：现金跑道/信用评级/WACC/预算状态/碳负债/PDP + 快捷入口按钮）· `FinanceSubpagePanel`（n22/n31/n32/n25/n103/n106 运行时构建）· `FinanceRuntimeFactory`
- 数值：`FinanceEconomics`（WACC/信用加点/年度预算与董事会投票/碳配额/融资条款/破产重组）· `FinanceManager`（预算/碳/卖资产/政府救助/重组回写 GameState）· `tools/balance` FIN 年闭环
- 测试：`FinancePlayModeTests`（导航工厂 / 预算提交回写 / 碳配额回写 / 数值公式 ≥3 PlayMode）
- 回写：白板 CATALOG 7 页 badge=金标准 · `_page_inventory.md` 同步 · 离线基准 `FIN_YEAR_LOOP.md`

### Week 6：FLEET 机队 + NET 航线网络 ✅（金标准 · 7 页 · L1–L5）

| 桶 | 层级 | 页面 | 状态 |
|---|---|---|---|
| FLEET | L1 | `n9` 机队 | ✅ |
| FLEET | L2 | `n10` 飞机详情 | ✅ |
| FLEET | L3 | `n11` 载客量配置 | ✅ |
| FLEET | L4 | `n97` 购机决策会议 | ✅ |
| FLEET | L5 | `n107` 大修预算确认 | ✅ |
| NET | L1 | `n12` 航线网络 | ✅ |
| NET | L2 | `n41` 单航线 | ✅ |

- UI：`FleetSubpagePanel`（n10/n11/n107 运行时构建）· `PurchaseDecisionPanel`（n97 购机会议：机型/数量/采购方式 + 五部门意见卡 + 批准/暂缓/拒绝）· `RouteDetailPanel`（n41）· `NetworkPanelUI`（n12 网络统计 + 航线列表）· `FleetNetRuntimeFactory` · `FleetUI`（整行点击进详情 · 购买→n97）
- 数值：`FleetEconomics`（残值/月维护/大修成本/舱位收益乘数/日利用率/回收期）· `FleetManager`（载客配置/大修排程/批量购机 `TryAcquireFleet` 回写 GameState）· `tools/balance` FLEET/NET 年闭环
- 测试：`FleetNetPlayModeTests`（导航工厂 / 数值公式 / 载客+大修回写 / 购机回写 ≥4 PlayMode）
- 回写：白板 CATALOG 7 页 badge=金标准 · `_page_inventory.md` 同步 · 离线基准 `FLEET_NET_YEAR_LOOP.md`
- 联动：机队「分配航线」→ `n98`（OPS 已有）；购机会议批准 `TryAcquireFleet`；`n12` 点击航线 → `n41` → 执飞注册号 → `n10`

### Week 7：MKT 市场销售部 · 批次 A（收益/定价/季节 · 15 页）✅（金标准）

| 层级 | 页面 | 状态 |
|---|---|---|
| L1 | `n14` 市场销售部 | ✅ |
| L2 | `n44` 航线收益分析 · `n49` 航班定价 · `n55` 季节性日历 | ✅ |
| L3 | `n45` 单航线定价 · `n46` 动态定价 · `n47` 收益仪表盘 · `n48` 辅助收入 · `n50` 外部环境 · `n56` 航季对比 · `n57` 旺季策略 · `n58` 历史趋势 | ✅ |
| L4 | `n59` 旺季影响预测 | ✅ |
| L3 | `n33` FFP 常旅客 · `n34` 产品工坊 | ✅ |

- UI：`MarketPanelUI`（n14 L1 总览：KPI + 航线收益排行 + 子页入口 + RPM 评估 / 竞争态势 Tab）· `MarketSubpagePanel`（14 子页运行时构建，含票价矩阵 stepper、动态定价滑条、辅助收入 stepper、FFP、产品工坊、旺季策略四维配置、影响预测）· `MktRuntimeFactory` · `MktContext`（跨页传参：选中航线/节日）
- 数值：`MktEconomics`（需求价格弹性 / 票价-客座率 / RASK-CASK / 月季节系数 / 节日曲线 / 旺季策略 ROI / 辅助收入 / FFP 里程负债 / 高铁替代）+ `MktUiMath`（UI 层航线收益聚合）；`MktManager`（票价覆盖 / AI 建议票价 / 动态定价 / 辅助收入 / FFP / 产品工坊 / 旺季策略回写 `GameState.mkt`）；`tools/balance` MKT 年闭环（季节系数 + 辅助收入 + RASK/CASK + 旺季 ROI）
- 测试：`MktPlayModeTests`（导航工厂 14 页 / 弹性-季节-旺季 ROI 数值 / 票价+旺季策略+产品回写 ≥3 PlayMode）
- 回写：白板 CATALOG 15 页 badge=金标准 · `_page_inventory.md` 同步 · 离线基准并入 `tools/balance` MKT 年闭环
- 联动：`n14` 排行/舱位看板 → `n44`/`n45`；`n49` → `n46`/`n47`/`n48`/`n33`/`n50`；`n55` → `n56`/`n57`/`n58`/`n59`

### Week 8：MKT 市场销售部 · 批次 B（竞争/渠道/品牌 · 16 页）✅（金标准）

| 层级 | 页面 | 状态 |
|---|---|---|
| L2 | `n60` 竞争雷达 · `n29` 竞争态势/AI航司 · `n67` 大客户合同 · `n68` 渠道预算 · `n72` 品牌危机公关 | ✅ |
| L3 | `n61` 竞对详情 · `n62` 航线网络对标 · `n63` 产品服务对标 · `n64` 运营成本对标 · `n65` 战略资本对标 · `n69` 分销渠道 · `n70` 直销渠道 · `n71` 渠道ROI · `n73` 舆情监测 · `n74` 危机响应 · `n75` 品牌修复 | ✅ |

- UI：`MarketSubpagePanel`（16 子页运行时构建：竞争雷达 7 维对标/竞对卡片/态势判断、竞对详情、四维对标、AI航司列表、大客户快速签约、渠道预算提直降代滑块+分销费用 stepper、分销/直销/渠道ROI、品牌危机三大风险看板+舆情信息流、舆情监测情感打分、危机响应 5 步流程+声明模板+推进阶段、品牌修复整改措施清单）· `MarketPanelUI`（n14 新增 竞争雷达/大客户合同/渠道预算/品牌危机公关 入口）· `MktRuntimeFactory` + `PrefabNavIntegration.EnsureMktConfig` 注册 16 页 · `MktContext.SelectedCompetitorId`（竞对卡片→n61 详情）
- 数值：`MktCompetitionEconomics`（7 维对标评分/综合竞争力/态势判断、渠道 ROI/CAC/单票分销成本/提直降代节省/渠道集中度、品牌信任指数/危机分级/情感打分/恢复预估/大客户价值）+ `MktUiMath.PlayerBench/AiBench`（竞对快照映射）；`MktManager`（渠道预算/大客户签约/品牌危机更新/整改措施回写 `GameState.mkt`）；`tools/balance` MKT B 年闭环（我司/竞对竞争力、直销占比、分销/直销 ROI、品牌信任、危机等级）
- 测试：`MktBPlayModeTests`（导航工厂 16 页 / 对标-渠道-危机数值 / 渠道+大客户+品牌回写 ≥3 PlayMode）
- 回写：白板 CATALOG 16 页 badge=金标准 · `_page_inventory.md` 同步（`_sync_mkt_b_20260815.py`）· 离线基准并入 `tools/balance` MKT B 年闭环
- 联动：`n60` 竞对卡片→`n61`；逐维 Tab→`n62`–`n65`；`n29`→`n60`；`n68`→`n69`/`n70`/`n71`；`n72`→`n73`/`n74`/`n75`；`n75`→`n60`/`n70`

### Week 9：STG 战略 + MNT 机务 ✅（金标准 · 15 页 · L1–L5）

**STG（8）**

| 层级 | 页面 | 状态 |
|---|---|---|
| L1 | `n16` 战略发展部 | ✅ |
| L2 | `n76` 机队运力规划 · `n77` 航权与时刻槽 · `n78` 联盟合作 · `n79` 投资并购 · `n80` 研究预案 | ✅ |
| L3 | `n35` RPM 评估台 | ✅ |
| L4 | `n102` 谈判弹窗 | ✅ |

**MNT（7）**

| 层级 | 页面 | 状态 |
|---|---|---|
| L1 | `n17` 机务工程部 | ✅ |
| L2 | `n81` 航线与定检 · `n82` 工程技术 · `n83` 航材供应链 · `n84` 安全质量与适航 · `n85` 维修培训 | ✅ |
| L5 | `n105` 航材订单确认 | ✅ |

- UI：`StrategyPanelUI`（n16 L1：战略仪表盘 + 六大功能入口 + 战略一致性/地缘风险 + 跨部门协同）· `StrategySubpagePanel`（7 子页运行时构建：n76 机队运力规划、n77 航权/时刻槽竞标评分+购买时刻槽+申请航权、n78 联盟升级/代码共享谈判/并购评估、n79 投资四维打分+NPV/IRR、n80 黑天鹅预案+战略豁免、n35 RPM 实时重算、n102 分成比例滑条谈判+签署回写）· `MaintenancePanelUI`（n17 L1：机务仪表 + 五大功能入口 + 维修策略三选一 + OEM 关系）· `MaintenanceSubpagePanel`（6 子页运行时构建：n81 定检排程+排 C/A/D 检、n82 OEM 升级+二手飞机/退租检、n83 航材库存+创建/紧急采购订单、n84 安全质量/适航/SMS、n85 培训/招聘联动、n105 订单确认下单入库）
- 数值：`StrategyEconomics`（航权竞标评分 0.4/0.25/0.2/0.15、时刻槽价值、联盟增量收入、投资四维分+IRR、战略一致性、地缘风险航路费加成）· `MaintenanceEconomics`（定检成本曲线 A/B/C/D、维修策略可用率/成本倍率、航材库存健康度、退租检成本）；`StrategyManager`/`MaintenanceManager` 回写 `GameState.stg/.mnt`；`tools/balance` STG/MNT 年闭环（战略一致性/政府关系/联盟/时刻槽 + 维修可用率/航材健康/定检）
- 测试：`StgMntPlayModeTests`（运行时工厂 13 子页 / 战略+维修数值公式 / StrategyManager 回写 / MaintenanceManager 回写 ≥4 PlayMode）
- 回写：白板 CATALOG 15 页 badge=金标准 · `_page_inventory.md` 同步（`_sync_stg_mnt_20260815.py`）· 离线基准并入 `tools/balance` STG/MNT 年闭环
- 联动：`n16`→`n76`/`n77`/`n78`/`n79`/`n80`/`n35`；`n78`→`n102`；`n17`→`n81`/`n82`/`n83`/`n84`/`n85`；`n83`→`n105`；`n77` 购 Slot 转航班环；`n82` 退租检→财务

### Week 10：HR 人力 + SAF 安全 ✅（金标准 · 17 页 · L1–L4）

**HR（9）**

| 层级 | 页面 | 状态 |
|---|---|---|
| L1 | `n18` 人力资源部 | ✅ |
| L2 | `n86` 组织规划与招聘 · `n87` 培训与发展 · `n88` 绩效与薪酬 · `n89` 员工关系与文化 · `n90` 合规与风险 | ✅ |
| L3 | `n37` 工会谈判桌 · `n91` 人才档案与继任 | ✅ |
| L4 | `n99` 招聘策略弹窗 | ✅ |

**SAF（8）**

| 层级 | 页面 | 状态 |
|---|---|---|
| L1 | `n19` 安全质量部 | ✅ |
| L2 | `n92` SMS与风险管理 · `n93` 安全监察与审核 · `n94` 事件调查与分析 · `n95` 局方与法规事务 · `n96` 安全文化与培训 | ✅ |
| L4 | `n100` 检查应对弹窗 · `n101` 策略切换确认弹窗 | ✅ |

- UI：`HrPanelUI`（n18 L1：人力仪表 + 人才紧缺指数 + 关键岗位空缺 + 员工满意度/流失率 + 培训ROI + 七大功能入口 + 工会罢工风险）· `HrSubpagePanel`（8 子页运行时构建：n86 组织招聘、n87 培训发展、n88 绩效薪酬、n89 员工关系、n90 合规风险、n37 工会谈判姿态三选一、n91 人才档案继任、n99 招聘策略弹窗）· `SafetyPanelUI`（n19 L1：安全仪表 + 安全评级A-D徽章 + 零事故天数 + 审计通过率 + 安全文化指数 + 五大功能入口 + 局方关系 + 安全预算）· `SafetySubpagePanel`（7 子页运行时构建：n92 SMS风险管理、n93 安全监察审核、n94 事件调查深度三选一、n95 局方法规事务、n96 安全文化培训、n100 检查应对弹窗、n101 策略切换确认弹窗）
- 数值：`HrEconomics`（招聘渠道成本/成功率/周期、人力成本占营收比、流失率、培训ROI、工会满意度/罢工风险）· `SafetyEconomics`（安全评级带 A/B/C/D、调查成本/周期、文化隐藏风险/报告率、局方接管风险）；`HrManager`/`SafetyManager` 回写 `GameState.hr/.saf`；`tools/balance` HR/SAF 年闭环（满意度/空缺率/人力成本比/培训ROI/罢工风险 + 安全评级/文化指数/审计通过率/整改率）
- [x] **（机组口径修复 · 2026-10-08）接管局继承机组入模 + 覆盖率插值**：接管流程 `TakeoverWorldBuilder.Apply` 此前只写机队/航线/贷款/遗留议题、**从不生成人员**，`GenerateAll` 只补管理序列关键岗（`isOperational=false`）→ `PilotFilled=0` → 已运营的 8 条航线吃满未建机组 ×0.85，而叙事层的 `crewVacancyPercent`（「机组人员短缺 15%」）只被写、从没被算。落地：`HrEconomics` 新增 `SourceInherited`（继承在册）与带编制的三参重载 `CrewQualityMultiplier(filled, establishment, avgEff)`（`乘数 = 0.85 + (质量乘数 − 0.85) × 覆盖率`，对人数单调不减、无陷阱区；两参重载保留为 est≤0 的纯质量口径，tools/balance 与全域扫描自测不受影响）· `PersonnelGenerator.SeedInheritedCrew`（按 `OperationalEstablishment × (1 − 缺员率)` 生成飞行员/乘务员/机务，`source=inherited` → 不进 `OperationalPayrollMonthlyWan` 防双计、不计入 `OverseasCrewCount` 保住国际准入生态位）· `GameState.ComputeCrewQualityMultiplier` 传 `PilotEstablishment` · `SaveManager.MigrateInheritedCrew` 旧档保守迁移（仅「接管局+有机队+0 名运行序列人员」时补）· `HrSubpagePanel` 警示拆分为缺编/战力两个抓手。实测（北辰快线 10 架窄体·缺员 15%）：在册 68/80 飞行员、乘数 0.85 → **0.9775**，季度少扣 91.5 万（同期盈利仅 44.58 万/季）。自测：`CrewQuality_CoverageInterpolatesWithoutTrapZone`（插值/单调/无陷阱）、`Takeover_InheritedCrewIsSeededAndNotDoubleCounted`（编制封顶 12 人缺口/薪酬不双计/不送海外资质）；`LogicSelfTests` 新增覆盖率插值日志块。
- [x] **（开线表单默认基地 · 2026-10-09）「开通新航线」的基地默认取玩家总部基地**：`OpenRouteModalUI` 的「基地名称」原硬编码预填 `PEK`，与玩家在开局/接管时选定的总部基地无关。后果不止「要多改一次」：表单默认值直接决定 `sel.originCode`，而 `RoutePermitLogic.FromHub` → `RouteRightManager.IsFromHomeBase` 是拿它与 `gs.hubIata` 比对的 —— 总部设在广州（CAN）的玩家若没改这一栏，`fromHub` 即为 false，于是**基地始发干线本应占用的时刻槽被静默跳过**（`NeedsSlot = 国际 || fromHub`），玩家在不知情下绕过了枢纽的时刻成本。落地：新增纯函数 `OpenRouteModalUI.DefaultEndpoints(hubIata, out baseCode, out destCode)` —— 基地取总部基地（`NormalizeBase` 去空白 + 转大写，缺失时回落 `FallbackBase = "PEK"`，保持旧行为），目的地默认虹桥、仅当基地本就是虹桥时改用首都（否则默认值自身会落在 `OnConfirm` 的「基地与目的地不能相同」拒绝分支）；`Build` 与 `Show` 统一走 `ApplyDefaults()`，故中途换总部后重开即生效。顺带修两处同源缺陷：① `AirportField.ResetToDefault` 在机场目录查不到该代码时会**留空**（必填字段看起来没填、`TryGetAirport` 必然失败而玩家看不出原因），现退化为显示原始代码；② 必填字段被清空后 `onEndEdit` 原先直接 `return` 把空框留在界面上（截图 1 中基地一栏正是此状态），现回落到字段默认值。另把 `Build`/`Show` 各存一份逐字相同的 Hint 字面量抽出为 `HintText()` 单一来源。未改 Hint 文案：提示框高度固定（anchor 0.16~0.28），现有两行已接近该宽度上限，再加行会溢出去压到下方确认按钮。自测：PlayMode `OpenRoute_BaseDefaultsToPlayerHub`（基地取自 hub、基地≠目的地、预填基地必须被 `IsFromHomeBase` 认定为枢纽始发，并留一条「总部 CAN 时 PEK 不是枢纽始发」的对照断言）+ `LogicSelfTests` 纯函数用例（空白/小写/null/全空白归一、基地=虹桥时目的地让开）。
- [x] **（人数口径对齐 · 2026-10-08）全公司总人数改为随机队派生**：`hr.totalHeadcount` 此前是默认 450 的**固定值**，与机队脱节；而兄弟口径 `LaborCostAnnualWan` 已是 `240 万/机/年 × 在役机队` → **成本随机队走、人数却钉死**，导致 `UnionCostWan`（按人头计费）在 20 架机队低报 4 倍（360 万 vs 应为 1440 万）、`TrainingRoi`（按人均预算）在大航司虚高。落地：`HrEconomics` 新增 `HeadcountPerAircraft = 90f`（锚点与 `DefaultAnnualLaborCostWan` 同源：5 机 ↔ 450 人 ↔ 1200 万/年）与 `EffectiveHeadcount(机队, 每机人数, 增量)`——与 `EffectiveAnnualLaborCostWan` **完全同构**（费率 × 规模 + 增量）· `HrState` 新增 `headcountPerAircraft`/`headcountAdditive`/`headcountMigrated`，`totalHeadcount` 降级为旧档迁移来源（与 `laborCostWan` → `laborCostPerAircraftWan` 同处理）· `HrManager.MigrateHeadcount` 与 `MigrateLaborRate` 逐行同构（含无机队保底分支）· `GameState.TotalHeadcount` 派生属性（与 `LaborCostAnnualWan` 相邻）· 三处消费者改读派生值（`TrainingRoi` ×2、`UnionCostWan`）· 到岗回写从 `totalHeadcount` 改累积进 `headcountAdditive`（继承在册机组不进增量，其薪酬在聚合内）· `HrSubpagePanel` 显示改带构成（`在编 1812 人（机队基准 1800 + 招聘增量 12）`）· `tools/balance` 硬编码的 `headcount = 450` 改为派生（5 机基线结果不变）。自测：`TotalHeadcount_ScalesWithFleetAndFeedsUnionAndTraining`（0/5/20 机、增量叠加、退役回落）、旧档迁移用例扩展（成本与人数**绝对值都延续**）、`LogicSelfTests` 新增人数派生日志块。
- **顺带修掉（同一轮）**：`gs.hr` **完全没有持久化**（`SaveManager` 无 `KeyHr`）→ `trainings`/`vacancies`/`trainingCenterLevel`/`union` 状态跨存档全部丢失，等于把上述培训与证照 UI 釜底抽薪。现补 `KeyHr = "ACEO_Hr"` + 写/读档（读档后经 `EnsureState` 跑迁移，旧档落 `new HrState()` 与改动前逐位一致）+ 登记进 `AllSaveKeys`；并把 `ClearGameSaveMenu` 从自维护硬编码键表改为直接消费 `SaveManager.AllSaveKeys`（此前漏清 19 个键：`ACEO_StaffPool`/`ACEO_StaffRecruits`/`ACEO_UsedMarket`/`ACEO_CityEconomy`/`ACEO_UsdCny` 等，清档后人事池与汇率带着上一局数据重新开局）。自测：`LogicSelfTests` 新增 `HrSave` 全字段往返核验（含 `List<string> memberIds`）与「空串必须抛异常」。详见 `职业介绍所人才渠道合并_方案_v1.0.md` §14.5。
- [x] **（航线周期固定成本 · 2026-10-09）加密能扭亏为盈的正当性：补上「按周期」固定成本，并铲掉四处捏造盈利基准的调用点**：CEO 给运营部下「加密」令后亏损航线**立刻**转盈——这不是模型算出来的，是**写出来的**。根因两层：① 成本模型只有按班变动成本（油/机场/机组/可变维修），一分钱周期固定成本都没有，于是 `日利 = 单班毛贡献 × 班次 × 调度乘数` 三个因子恒为正，**日利的符号与班次无关**，加密只能放大盈亏、永远不可能改向；② 四处调用点把亏损基准当成「未设置」（`baseDailyProfit > 0.01f ? base : before`，再补 `if (baseline <= 0.01f) baseline = 1f`），凭空生成 +1 万/班的盈利基准并**写回** `baseDailyProfit`，亏损航线一次调令即永久转盈。落地：`OpsEconomics` 新增 `RouteFixedDailyWan(航程, 机型)`（`RouteFixedBaseDailyWan 0.06 × (1 + 航程/6000) × 宽体 1.3`，万美元/日/线）、`RouteDailyProfit`（毛贡献 − 固定成本）与 `BreakEvenDailyFlights`（单班毛贡献非正时返回无穷大）；`ResolvedSingleFlightBaseline(route)` 成为**唯一**基准解析入口（负值原样透传，仅 |值| ≈ 0 才兜底），四处缺陷点（`CapacityDirectiveLogic`、`SeasonReviewLogic` ×2、`OpsPanelUI.RecalcAllRoutes`）全部改走它，并顺带收口三处同类漏网：`InboxManager` 批准排班、`OpsScheduleLogic.ApplyWaves`（负基准被写成 0）、`CapacityDirectiveLogic.Revert`（`baseline > 0.01f` 门槛使亏损航线退回时根本不重算）。不变量：`RouteFlight.dailyProfit` 存**毛贡献**，固定成本只在航线层级扣一次，`RouteData.RecalcDailyProfit()` 与 `ApplyToRoute` 口径一致且幂等。实测：F 档位 0.07（1080km 窄体）/ 0.18（8150km 宽体）万/日；单班毛贡献 0.08 万/班时 1 班/日 −0.01（亏）→ 3 班/日 +0.11（盈），盈亏平衡 1.25 班/日；单班毛贡献为负（−0.40）时周 7/14/21 班分别为 −0.35/−0.63/−0.97，**加密只放大亏损**。自测：EditMode `OpsEconomicsTests`（固定成本档位单调、摊薄翻转、负基准全频段不透传、不变量幂等）+ `LogicSelfTests` 三段日志（摊薄 / 护栏 / 不变量）+ `tools/balance --routefixed` 复核命令。平衡代价（seed=42）：年净利 1006 → 890.88 万、年末现金 1481.08 → 1365.88；60 组合压测 GREEN 47 → 43、BANKRUPT 8 → 14，危险组合 13 → 17——引入真实固定成本的必然代价，唯一杠杆是调小 `RouteFixedBaseDailyWan`。详见 `tools/balance/CALIBRATION_1.3.md`。
- [x] **（固定成本调参旋钮 · 2026-10-09）把 `RouteFixedBaseDailyWan` 从 `const` 变成可扫参的平衡旋钮**：上一轮引入周期固定成本后，复核分布要改常数、重编译，压测里无法扫参。落地：`EconomyBalanceParams` 拆成三层——`RouteFixedBaseDailyWanDefault = 0.06f`（出厂默认，回归锚点与文档口径的唯一权威来源，行为与旧 `const` 逐位一致）+ `static float RouteFixedBaseDailyWan` 属性（承载当前值）+ `SetRouteFixedBaseDailyWan / ResetRouteFixedBaseDailyWan / IsRouteFixedBaseOverridden`（覆盖、复位、是否偏离）。取数契约：仅接受 `(0, RouteFixedBaseDailyWanCeiling = 1]` 内的有限值，非法输入**不写入**并返回 `false`——**不做静默夹取**，否则扫参曲线会与请求值不符而得出错误结论。Unity 侧用 `#if UNITY_5_3_OR_NEWER` 包一个 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 钩子强制复位，避免关闭 Domain Reload 时静态字段跨 Play 会话残留（balance 工具不定义该宏，此块不参与编译，故共享源码仍可无 UnityEngine 编译）。`tools/balance` 新增 `--routefixed-base <万/日/线>`（全局覆盖，任何模式可用，非法值报错退出码 2）与 `--routefixed-sweep [csv]`（默认扫描集 0.02–0.12；上半张表看基准如何线性缩放各档 F，下半张表把单班毛贡献钉在出厂默认对应的边际航线上看平衡班次如何平移，结束自动复位）；`--routefixed` / `--stress` 表头与压测报告「范围」小节均标注当次取值，`summary_y1_seed42.txt` 新增 `routefixed_base` / `_default` / `_overridden` 三字段供回归比对。实测线性性：基准翻倍 → 平衡班次翻倍（0.06 → 1.25 班/日，0.12 → 2.50 班/日），故基准是唯一且单调可解释的难度旋钮；`--stress --routefixed-base 0.10` → GREEN 42 / BANKRUPT 15（基线 43 / 14），旋钮确能整体平移难度。**默认行为零漂移**：`dotnet run`（seed=42）仍为 net=890.88 万、cash=1365.9 万，`--stress` 仍为 GREEN 43 / BANKRUPT 14 / RED 1 / YELLOW 2，与上一轮基线逐项一致。自测：EditMode 新增 `RouteFixedBase_Tunable_ScalesFixedCostLinearly_AndResets`（线性缩放 + 复位回默认 + `IsRouteFixedBaseOverridden` 翻转）与 `RouteFixedBase_RejectsIllegalValues_KeepsPrevious`（0/负/NaN/Inf/超上限全部拒绝且不改当前值，上限本身接受），均以 `try/finally` 复位避免静态状态外溢。验证：`node tools/semcheck/run.js` → `errors=0`；`dotnet build` 三处（balance、`AirlineCEO.Runtime`、`AirlineCEO.EditModeTests`）全 `Build succeeded`。详见 `tools/balance/CALIBRATION_1.3.md`「调参旋钮」小节。
- [x] **（中文注释编码防呆 · 2026-10-09）给 semcheck 补一道「非 ASCII 静默丢失」拦截**：本轮编辑 `EconomyBalanceParams.cs` 时该文件**再次**被写成全文中文变 `?`——这是本仓库的老事故（`tools/recover/` 整个目录、`InboxManager.corrupted.bak.cs` 都是同源产物）。这类破坏的可怕之处在于**编译照过、semcheck 全绿**：`?` 是合法 ASCII，中文只在注释与 XML 文档里，丢了不影响任何语义，于是能一路静默到发布。因此新增 `tools/semcheck/enccheck.js`：① 硬错误——文件含 U+FFFD（U+FFFD 是替换字符，正常源码/文档不该出现）；② **基线比对**——`enc_baseline.json` 逐文件记录「中文字符数」，跌到 0 或跌超 20% 即报错。第二层才真正拦得住 `?` 替换（第 1 层看不见合法 ASCII）。`run.js` 已挂上该检查并纳入退出码：`node tools/semcheck/run.js` 现在同时输出 `errors=0 (raw=0)` 与 `enc: errors=0`，二者任一失败即 `exit 1`。扫描范围 `.cs` / `.md`（394 个文件），排除 `Library`/`obj`/`bin`/`node_modules`/`recover`（后者**故意**保存损坏样本，纳入会把已修复的证据当成新事故反复报警）。附带 `--update` 重建基线（有意增删中文后使用，且拒绝在仍有 U+FFFD 时更新）。实测拦截面：整文件破坏 → `中文全部丢失（基线 43 字 → 现在 0）——典型的 '?' 替换事故`；半量破坏 → `中文显著减少（基线 43 字 → 现在 22 字）`；集成到 `run.js` 后确认「编译 0 错误但 enc 报错 → 整体 exit 1」。基线现状 372 个含中文文件 / 262749 字。**修复说明**：被破坏文件的中文最终是从会话记录（transcript 保留了历次 `Write` 的完整 `contents`）取回逐字原文后用 Node 以 UTF-8 字节写回、并拼接新旋钮代码恢复的——破坏后 `?` 不可逆，能修回属侥幸，故这道防呆是必需的。
- [x] **（接管航司列表滚动位置 · 2026-10-09）点选/展开后列表不再跳回顶部**：接管开局「选择要接管的航司」是四层树（大洲 → 国家 → 城市 → 航司），点任意一行都要整表重绘；而 `ShowAirlineSelect()` 每轮都先 `ClearForm()` —— 它把 `_formRoot` 下的子节点**连同滚动区域一起**销毁，重绘时新建的 `ScrollRect` 里 `content.anchoredPosition` 回到 0（= 顶部），于是「往下翻到非洲·南非·约翰内斯堡点了 JNB，列表瞬间弹回亚洲」。落地：滚动区域改为**跨重绘复用** —— `AirlineSelectionUI` 拆出 `CreateArea(form, out ScrollRect)`（只建一次）与 `Draw(RectTransform content, …)`（只重画行，签名由 `Transform form` 改为内容节点），`SetupFlowController` 以 `_alContent`/`_alScroll` 持有、在 `ClearForm()` 中置空（表单真被清空时才重建），并在页面构建的**最后**一步复位滚动。滚动位置记的是**距顶部像素偏移**而非 `verticalNormalizedPosition`：列表不足一屏时后者会退化成 0（语义上等于「底部」），拿它当基准会在「展开第一个大洲、列表刚由短变长」的一瞬间把视图甩到列表末尾——换算收口为纯函数 `AirlineSelectionUI.NormalizedFor(topOffset, contentH, viewH)`（不足一屏 → 1）。清行用 `SetParent(null)` 之后再 `Destroy`：本页重绘由被点中的那一行自己触发（`onPick`），此刻它仍在自己的 `onClick` 调用栈里，不能 `DestroyImmediate`；而裸 `Destroy` 要到帧末才生效，旧行会与新行共存一帧。UI 语义对照 Unity 6000.3.21f1 的 `ScrollRect` 源码核过：`verticalNormalizedPosition` 的 getter 先 `UpdateBounds()`（读到的是新鲜值，可放心存），setter 直通 `SetNormalizedPosition`（无「边界为 0 就静默返回」的短路，故首次建立后立即赋值亦生效），`GetBounds()` 只取内容自身的世界四角（与子节点无关）。自测：PlayMode 新增 `AirlineSelect_Redraw_KeepsScrollAreaAndPosition`（展开亚洲 → 滚到 0.4 → 重绘：`ScrollRect` 实例数恒为 1、行数不堆积、内容高度不变、滚动位置保持；换成非洲后行数与全新区域逐数一致）与 `AirlineSelectScroll_NormalizedFor_DegenerateCaseGoesTop`（不足一屏回顶部 / 顶·中·底 / 越界夹取）。验证：`node tools/semcheck/run.js` → `errors=0` + `enc: errors=0`；另建一次性工程把 `Assets/Scripts` + `Assets/Tests/PlayMode` 合并编译 → `Build succeeded` 0 警告 0 错误（`semcheck` 只 glob `Assets/Scripts`，测试程序集不在其覆盖面内，故单列一次检查）。
- [x] **（主菜单 FM 式两栏排版 · 2026-10-10）`n1_主菜单` 从「标题 + 说明 + 一排横按钮」改为左主视觉槽 + 右纵向菜单列**：旧版式的三处硬伤——① 6 个入口横排一行、每格仅占屏宽约 15%，文案稍长即截断，且**主次被抹平**（「新游戏」与「退出」同宽同高）；② 中间 `Form` 区在主菜单无事可填，留一大块空底，首屏看起来像「没做完」；③ 无存档时「继续」仍可点，点进去才被拒绝。落地（参照 Football Manager 开始界面：主入口纵向整行 / utility 降级到次级小字行 / 页脚钉底）：新增 `UI/Setup/MainMenuUI.cs`——左侧 `HeroSlot`（锚点 3%~55% × 10%~90%，**只放淡水印 + 占位说明、零交互**，作为后期图像化的替换点）+ 右侧 `Column`（品牌块 22pt + 30% 宽强调横条 + 一句话 → 「最近保存」卡片 62px → 四条 44px 主入口 `Entry_New/Scratch/Continue/Archive` → 次级行「设置 / 存档」+ `F5/F9` 快捷键提示 → 提示行 → 页脚「MVP vX」+ 小号「退出」）；`SetupFlowController.ShowMainMenu()` 改为组装 `SaveCardInfo` + `MenuEntry` 列表后调用 `MainMenuUI.Build`，并**隐藏通用表单卡**（`ClearForm()` 负责恢复，两处注释互为指针），主菜单版面挂在 `SetupOverlay` 上随 `ClearForm()` 一并销毁。无存档时「继续 / 读档」置 `interactable=false`（FM 同款置灰），不再让玩家点进必然失败的入口。新增 `SaveManager.TryPeekSummary(out 公司名, 财年, 季度, 现金, 满意度, 是否终局)`：**只读 PlayerPrefs、不 `Load()`**，走静态路径故不依赖 `SaveManager.Instance`（主菜单早于玩法系统初始化，旧代码的 `Instance != null` 判断在此处其实恒为 false）；版面容器的 `SaveCardInfo` 由调用方填好，`MainMenuUI` 不碰存档 API，故版面测试可确定性运行。自测：PlayMode `SetupFlowPlayModeTests.MainMenu_Layout_TwoColumnsAndVerticalEntries`（左槽存在且与菜单列不重叠 / 每个入口等宽拉伸且 y 依次递减——**挡住退回横排一行** / `Interactable=false` 的条目按钮置灰 / 次级入口·页脚·保存卡都在 / 点击仅触发回调且提示行可回写）。已知取舍：列内为像素栈（`PinFromTop`），1024×600 编辑器预览下内容栈 ≈438px + 页脚 32px 仍在列内，更矮的窗口会压到页脚（不设滚动条：7 行菜单不该出现滚动条）。详见 `docs/GameDesignBible/MVP/V3.0/主菜单排版_FM式两栏_方案_v1.0.md`（含 §6 图像化接口：出图比例 / 替换步骤 / 不得越过 55% 的硬边界）。
- [x] **（semcheck 补程序集边界 · 2026-10-10）把「运行时代码」与「测试代码」拆成两个工程编译，拦下 Unity 才看得见的 CS0012**：上一轮把 `Assets/Tests/**` 加进 `semcheck.csproj` 的 glob，本意是「让测试编译错误也能被命令行拦住」，但**引用集是两者的并集**（含 `Unity.TextMeshPro`），于是「测试里碰到 TMP 类型」这类**程序集边界错误**在 semcheck 里全绿、Unity 一编译就红——本轮实例：新增的主菜单句柄把提示行写成 `public TextMeshProUGUI Hint`，测试读 `view.Hint.text`，Unity 报 `CS0012 The type 'TextMeshProUGUI' is defined in an assembly that is not referenced. You must add a reference to assembly 'Unity.TextMeshPro'`。更糟的是**假象**：加了测试 glob 之后，人会更相信「测试已被覆盖」。落地：`gen.js` 改为生成两个工程，一一对应 Unity 侧的两条 asmdef 边界——`semcheck.csproj`（`Assets/Scripts` ≈ `AirlineCEO.Runtime.asmdef`，`AssemblyName` 保持 `SemCheck` 以便 `tools/slotcheck` 的 `ProjectReference` 继续取运行期类型）+ `semcheck_tests.csproj`（`Assets/Tests` ≈ `AirlineCEO.PlayModeTests.asmdef`，`ProjectReference` 前者，并**按测试 asmdef 口径剔除 `Unity.TextMeshPro` / `Unity.TextMeshPro.Editor`**）；`run.js` 分两次构建、分别输出 `errors=N (raw=N)` 与 `tests-asm: errors=N (raw=N)`，任一非零即 `exit 1`，报告文件按 `[scripts]`/`[tests]` 前缀归属。**只封锁实测被 Unity 拒绝的那一支**，不顺手扩大名单（名单开错会把 Unity 本来允许的写法判成错误，假阳性比漏报更坏）。附带修掉根因：`MainMenuView` 不再外泄 `TextMeshProUGUI`，改为 `HintText`（字符串读）+ `SetHint`（写）+ `internal BindHint`；**版面句柄没必要把内部控件类型交出去**。验证：拆分后两工程均 `errors=0`（说明测试确实只用运行期的公开 API，与 Unity 的真实边界一致）；用临时探针复现同一形状两遍——`using TMPro; TextMeshProUGUI t = null;` → `tests-asm: CS0246`、`var font = ChineseFontProvider.FontAsset;`（返回 `TMP_FontAsset`，**测试文件里不写 "TMPro" 字样**）→ `tests-asm: CS0012 ... 'Unity.TextMeshPro, Version=0.0.0.0...'`，与 Unity 控制台逐字一致；探针已删，回归 `errors=0` + `tests-asm: errors=0` + `enc: errors=0`。新增的 `tools/semcheck/semcheck_tests.csproj` 是生成物，需与 `semcheck.csproj` 一并入库（`gen.js` 每次重写二者）。
- 测试：`HrSafPlayModeTests`（运行时工厂 15 子页 / 人力+安全数值公式 / HrManager 回写 / SafetyManager 回写 ≥4 PlayMode）
- 回写：白板 CATALOG 17 页 badge=金标准 · `_page_inventory.md` 同步（`_sync_hr_saf_20260815.py`）· 离线基准并入 `tools/balance` HR/SAF 年闭环
- 联动：`n18`→`n86`/`n87`/`n88`/`n89`/`n90`/`n37`/`n91`；`n86`→`n99`；`n19`→`n92`/`n93`/`n94`/`n95`/`n96`；`n95`→`n100`；`n96`→`n101`；顶栏离职事件 → 弹窗（`ShowPopup`）


---

## Phase 3：跨部门系统 + 董事会（第 11–12 周）

**目标**：BOARD + CROSS 桶全部交互；事件/决策/宏观/竞对打通。 ✅

### Week 11：BOARD + 决策流（含 L4/L5）✅

| 桶 | 页面 | 状态 |
|---|---|---|
| BOARD | `n20` 董事会（L1）· `n26` 财年结算/续聘/解雇（L4） | ✅ |
| CROSS | `n21` CEO 决策会议（L4）· `n23` 部门负责人任命（L4）· `n104` 补条件表单（L5） | ✅ |

- UI：`BoardTargetUI`（n20 L1：年度目标清单 + 进度条 + 董事成员卡（派系/满意度）+ 派系分布 + 「预算投票→n22」「财年结算/续聘→n26」入口）· `YearEndSettlementPanel`（n26 L4：目标完成率结算预览 + 续聘线/难度 + 保留本届/重新组建/重开 三分支）· `DecisionMeetingPanel`（n21 L4：顶栏议题 + 五部门意见卡红黄绿量化 + 批准/暂缓/拒绝/取消会议）· `SupplementConditionPanel`（n104 L5：来源议题摘要 + 条件清单可完成标记 + 机会成本/连锁影响 + 模板库 + 提交/取消）· `AppointmentPanel`（n23 L4：候选人 3 选 1 + 能力/薪酬/特质卡片 + 确认任命）
- 数值：`DecisionTypes`（DecisionProposal / DeptOpinion / DecisionSnapshot / SupplementForm / SupplementCondition / DecisionHistoryEntry / DecisionState）· `DecisionEconomics`（FIN/OPS/MKT/HR/SAF 五部门意见量化引擎 + 裁决 approve/defer/reject + 补条件模板 + 延期损失文案）；`DecisionMeetingManager` 回写 `GameState.decision`（历史 + 补条件表单）
- 存档：`GameState.decision` + `SaveManager`（KeyDecision → `DecisionSave`）
- 测试：`CrossBoardPlayModeTests`（运行时工厂 8 页 / 五部门意见 + 裁决 + 模板 / Manager 批准/暂缓/取消回写 ≥3 PlayMode）
- 联动：收件箱「发起会议」→ `n21`；`n21` 暂缓 → `n104`；`n20` 结算 → `n26`；购机/开线/招聘上游「暂缓」统一进 `n104`（`DecisionMeetingManager.DeferToSupplement`）

### Week 12：事件 / 宏观 / 历史 / 竞对收口 ✅

| 页面 | 层级 | 说明 |
|---|---|---|
| `n28` 宏观经济面板 | L2 | 阶段 + GDP/油价走势 + 四部门传导 + 展望 ✅ |
| `n30` 随机事件日志 | L2 | 倒序日志 + 效果/摘要 ✅ |
| `n38` 历史回放 | L2 | 决策日志 + 会议历史 + 里程碑 ✅ |
| `n29` 竞争总览 / AI 航司 | L2 | 玩家市占 + 竞对列表（类型/状态/掠夺者）+ 深层分析入口 ✅ |

- UI：`MacroPanelUI`（n28）· `EventLogPanelUI`（n30）· `HistoryReplayPanel`（n38）· `CompetitionOverviewPanel`（n29→`n60`）
- 导航：`ShellRuntimeFactory` 替换 n28/n29/n21 占位为真面板，新增 n104/n23/n26/n30/n38；`NavigationRouter.PreferShellRuntime` 与 `PrefabNavIntegration.EnsureShellCatalogExtras` 登记 Phase 3 全部页
- 运行：`MVPBootstrap.EnsureCoreSystems` 初始化 `DecisionMeetingManager` 单例


---

## Phase 4：硬核打磨（第 13–14 周）✅

### Week 13：全流程数值压测 ✅（2026-08-16）

**交付**：`tools/balance` 新增 `Stress/StressTestRunner.cs` + `Program.cs` 增加 `--stress` 入口；`AirlineYearSimulator` 增加 `irropsSeverity` 参数（把难度的事件烈度乘数接入 M6 IRROPS，默认 1.0 保持基线不变）。

- 组合：**难度（简单/标准/困难）× 航司类型（FSC/LCC/支线/包机）× 宏观情景（繁荣/复苏/衰退/油价冲击/滞胀）= 60 组合**，各跑一个财年，复用既有 OPS/FIN/FLEET/MKT/STG/MNT/HR/SAF 年闭环。
- 产物：`tools/balance/STRESS_BALANCE_REPORT.md`（全量矩阵 + 分维度透视 + 核心发现）+ `out/stress_matrix.csv`。
- 运行：`dotnet run --project AirlineCEO.Balance.csproj -- --stress`（seed=42 可复现）。

**核心发现（详见报告）**：

| 航司类型 | 组合 | 平均净利 | 破产 | 失控 | 结论 |
|---|---|---|---|---|---|
| 全服务 FSC | 15 | 646 万 | 0 | 0 | 梯度平滑，作为基线锚点 ✅ |
| 低成本 LCC | 15 | 1429 万 | 0 | 5 | 繁荣/复苏净利 2230 万 > 4×期初，偏松 |
| 支线 Regional | 15 | -1006 万 | 15 | 0 | 固定月开销 95 万对小规模过重，全破产 |
| 包机 Charter | 15 | -382 万 | 5 | 0 | 衰退/滞胀现金流断裂 |

**建议**：① 固定开销按机队规模分段（每机 8–12 万/月 + 基础 20 万）或给支线/包机单独低固定档；② LCC 短途 `CompetitorSupply` 0.45–0.55 → 0.65+ 或对高密度 189 座引入 yield 折价。数值调优留作后续 sprint（非阻塞）。

**数值调优已落地（2026-08-16）**：
- 固定开销分段：`EconomyBalanceParams.FixedMonthlyOpexFor(n) = 20 + 15×n`（5 机锚点 95 不变，回归基线不漂移）；3 机降 65、6 机升 110。
- LCC 折价：`RPMCalculator` 新增 `yieldMultiplier`（默认 1.0）+ `RouteTemplate.YieldMultiplier`，LCC 4 航线设 0.85；竞对运力 0.45–0.55 → 0.60–0.70。
- 支线校准：班次 7→14/wk、客座率 0.68–0.72 → 0.78–0.82。
- 结果：**RUNAWAY 5→0，告急 25→6**（全落「滞胀」最坏宏观，小/窄体航司脆弱属真实梯度），FSC 15/15 GREEN。

### Week 14：全 103 页交互一致性审计 ✅（2026-08-16）

**1. 页面状态审计**：`_page_inventory.md` 全 103 页已对齐实装——
- **92 页 ✅ 金标准**（FLEET/NET/OPS/FIN/MKT/STG/MNT/HR/SAF/BOARD/CROSS 全部桶）。
- **11 页 SHELL 桶**如实标注：`n8 顾问开局引导线` ✅ 金标准；`n0–n7/n39/n40` 均 🟡「最小可玩」并注明延期理由（地图微件/真抽卡/完整邮件模型/文章详情+XP/显示语言等）。
- 修复 `_gen_inventory.py` 漂移：补 `board_done`/`cross_done`/`shell_done` 集，`shell_partial` 改为「页→延期理由」映射，使生成器与实装一致（此前重跑会把 BOARD/CROSS 打回 ⬜）。

**2. 导航审计（100% 可达，无孤儿页）**：103 页全部有可达路径——

| 机制 | 页面 |
|---|---|
| `ShellBarView`（L0 常驻壳） | `n0` |
| `SetupFlowController`（开局流） | `n1`–`n5` |
| `ShellRuntimeFactory.TryCreate`（前缀） | `n6`/`n7`/`n8`/`n39`/`n40`/`n12` |
| `MVPBootstrap.MapShellPageToLegacyNav` | `n9`/`n13`–`n20`（9 个部门 L1） |
| `PrefabNavIntegration.Ensure*Config` + 子页工厂 | 其余 82 页 L2–L5 |

**3. Prefab 复用度 / 性能 / 存档**：
- Prefab 复用：当前以运行时工厂（`ShellRuntimeFactory` + 各部门 `*SubpagePanel.Build`/`Ensure*Config`）+ 少量 Widget Prefab 为主；**Prefab 资产化量产仍为延期项**（Phase 1.4 已注，非阻塞）。
- 存档体积：`SaveManager` 走 PlayerPrefs 多 Key JSON blob（fleet/mkt/stg/mnt/hr/saf/decision 等），预估 <100 KB，无体积风险；如需可改为单文件 JSON 存档。
- 性能 Profile：需在 Unity Editor Profiler 手测（离线不可复现），标记为 Unity 内验收项。


---

## 三条轨道的节奏（更新）

| 周 | Track A（UI 交互 · 含 L2–L5） | Track B（数值） | Track C（Prefab） |
|---|---|---|---|
| 1 | Model / UIConfig | balance + Tests | Prefab 规范 |
| 2–3 | Shell L1 占位 + 开局 n1–n5 | RPM 校准 | Framework Prefab |
| 4 | **OPS 全 7 页** ✅ | OPS 年闭环 | OPS Prefab |
| 5 | **FIN 全 7 页** ✅ | 财务三表/对冲 | 报表组件 |
| 6 | **FLEET+NET 全 7 页** ✅ | 机队/单航线 | 机队详情组件 |
| 7 | **MKT A 批 15 页** ✅ | 定价/季节 | 图表组件 |
| 8 | **MKT B 批 16 页** ✅ | 竞争/渠道/品牌 | 雷达/信息流 |
| 9 | **STG+MNT 全 15 页** ✅ | 时刻/维修/航材 | 日历/谈判 |
| 10 | **HR+SAF 全 17 页** ✅ | 人才/安全评级 | 检查单/表单 |
| 11 | **BOARD+决策 L4/L5** ✅ | 目标/续聘/五部门意见 | 投票/会议 |
| 12 | **CROSS 事件/宏观/历史/竞对** ✅ | 情景压测 | 事件弹窗 |
| 13–14 | 103 页审计 + Bug ✅ | 60 组合平衡报告 ✅ | 性能/复用（复用注记·Unity 内 Profile 待测） ✅ |


## 风险与缓解

| 风险 | 概率 | 缓解措施 |
|---|---|---|
| 只做 L1 占位就宣称部门完成 | 高 | 金标准强制对照 `_page_inventory.md` 桶内全页 |
| 数值与游戏 Tick 分叉 | 中 | Logic 源码 Link 进 balance；部门公式进同一目录 |
| 两套 UIConfig asset 漂移 | 中 | `Resources/UIConfig/`（运行时）与 `ScriptableObjects/UIConfig/`（可编辑副本）目前互补；须在 `PREFAB_SPEC.md` 写明「单一权威来源」，Phase 2 各新部门只落一份并同步 |
| Prefab 迁移破坏旧 UI | 中 | UIFactory 兼容 + 运行时工厂回退 |
| MKT 页数过多阻塞 | 中 | Week 7/8 拆两批；先收益定价后竞争品牌 |


## Sprint 清单（当前）

- [x] Phase 0 全项（2026-08-12 复核 ✅；Badge Prefab / PREFAB_SPEC / 多余 asmdef 已清）
- [x] Phase 1.1–1.3
- [x] Phase 1.2 Shell / L1 占位（**不等于**各部门金标准；≠ 白板 SHELL 交互完成）
- [x] Phase 1.4 SHELL 最小可玩开局流（n1→n5 闸门 + n0/n6/n7/n8/n39/n40 主路径）🟡 待加深
- [x] Phase 1.4 修：n12 勿映射运营；默认 Tab→n6；`SetupFlowPlayModeTests`
- [x] 顾问开局引导线 v1.1：S0–S6 全流程 + 收件箱双栏 + 机队「分配至深圳—北京」等待态 + S4「调整班次」弹窗（频率×时刻+自动排班）+ n3 中文名校验
- [x] Phase 2 Week 4 OPS 桶 7 页金标准
- [x] **（清理 · Phase 2 启动前）** 统一货币单位口径：确立「万美元」为唯一货币单位；`OPS_YEAR_LOOP.md` 补注单位说明 + 「万」→「万美元」；顶栏 `CashMeter` 与开局资金档 `SetupCatalogs` 显式「万美元」；`PREFAB_SPEC.md` 新增「货币与数值单位约定」冻结节
- [x] **（清理 · Phase 2 启动前）** 明确 UIConfig 单一权威来源：运行时唯一加载路径 = `Resources/UIConfig`；删除漂移的 `ScriptableObjects/UIConfig` 副本目录；`CreateUIConfigAssets` 移除双写、`Phase0VerifyMenu` 校验改指 Resources；`PREFAB_SPEC.md` 注明单一权威来源
- [x] **（Unity 内）** `Create OPS Prefabs & UIConfig` + 手测航班异常 / 航线分配：`CreateOpsPrefabs` 已生成 n42/n43/n66/n36/n98/n24 Prefab 并绑定 Resources/UIConfig（`Prefabs/UI/Panels/Panel_OPS_*.prefab` + `Resources/UIConfig/nXX_*.asset`）。新增 `Editor/OpsManualTestMenu.cs`（PlayMode 一键手测：① 航班异常 IRROPS 弹窗，轮换台风/停飞/流控/机组四类场景，点 A/B/C 验证处置回写现金/准点/董事会；② 航线分配 n98_航线航班环选择器；均带 + 截图版本存 `Screenshots/OpsIrrops.png`/`OpsAssign.png`）。
- [x] Phase 2 Week 5 FIN 桶 7 页（含 L3–L5）金标准（数值闭环 + PlayMode≥3 + 白板回写）
- [x] Phase 2 Week 6 FLEET+NET 桶 7 页（含 L1–L5）金标准（数值闭环 + PlayMode≥4 + 白板回写）
- [x] Phase 2 Week 7 MKT 批次 A 15 页（含 L1–L4）金标准（数值闭环 + PlayMode≥3 + 白板回写）
- [x] Phase 2 Week 8 MKT 批次 B 16 页（含 L2–L3）金标准（数值闭环 + PlayMode≥3 + 白板回写）
- [x] Phase 2 Week 9 STG+MNT 桶 15 页（含 L1–L5）金标准（数值闭环 + PlayMode≥4 + 白板回写）
- [x] Phase 2 Week 10 HR+SAF 桶 17 页（含 L1–L4）金标准（数值闭环 + PlayMode≥4 + 白板回写）
- [x] Phase 3 Week 11 BOARD+决策流（n20/n21/n23/n26/n104 L1–L5）
- [x] Phase 3 Week 12 事件/宏观/历史/竞对（n28/n29/n30/n38）
- [x] Phase 4 Week 13：60 组合数值压测（难度×航司类型×宏观情景）+ `STRESS_BALANCE_REPORT.md`（发现：支线全破产/包机衰退破产/LCC 失控，建议固定开销按规模缩放 + LCC 上调竞对运力）
- [x] Phase 4 Week 14：103 页一致性审计（92 ✅ 金标准 + 11 SHELL 如实标注最小可玩/延期理由）+ 导航审计 100% 可达 + `_gen_inventory.py` 修复 BOARD/CROSS 漂移
- [x] Phase 4 回归基线刷新：`baseline/summary_y1_seed42.txt` 更新到含 MKT/FIN/FLEET/STG/MNT/HR/SAF 年闭环的已知良好态（`compare_baseline.ps1` 恢复通过；此前基线停留在 OPS 时代，net 650→1306 漂移）
- [x] **（Phase 4 遗留 · 非阻塞）** 数值调优落地：固定开销按机队规模分段（`FixedMonthlyOpexFor`）+ LCC `yieldMultiplier` 折价 0.85 + 竞对运力上调 + 支线场景校准（依据 `STRESS_BALANCE_REPORT.md`，RUNAWAY 5→0、告急 25→6 全落滞胀）
- [x] **（Phase 4 遗留 · Unity 内）** Editor Profiler 性能 Profile + 手测存档体积：新增 `Editor/Phase4ProfileMenu.cs`。①「存档体积检测」——`SaveManager.Save()` 后遍历 `AllSaveKeys` 统计字节（字符串键计 UTF-8 字节、标量键计 4 字节），输出总量/键数/最大单键/Save 耗时；②「性能 Profile · 年闭环计时」——`Stopwatch` 计时连续 `TimeManager.AdvanceQuarter()` 4 季度（1 财年），输出总耗时/平均/峰值/逐季明细（含结算/消息/遥测/自动存档端到端开销）。
- [x] **（GDB v1.0 对齐 · P0 玩法闭环 · 核心胜利路径）** 第 4 章终局与遗产结算：光荣退休（65 岁/20 财年触发）+ 遗产评分 S/A/B/C（市值 40% + 安全飞行 30% + 航线覆盖 20% + 董事会满意度 10%）+ 结算 UI 区分胜利/失败语气 + 解锁奖励持久化（传奇模式/新游戏+/快速模式三枚标志落盘）。新增 `LegacyScoreCalculator`、`YearEndEvaluator` 光荣退休分支（`Retired`）、`GameState` 出生年份/胜利/解锁字段、`SaveManager` 持久化、`SetupFlowController` 写入出生年份。数值初值（PE=8 / 目标市值 5000/10000/20000 / 覆盖基准 30/10/5 / 飞行小时 100000 FH）标注 TODO 待进 `tools/balance` 校验。
- [x] **（GDB v1.0 对齐 · P0 遗留）** 软性失败 + 重大空难 + 三模式玩法入口：
  - **软性失败**：新增 `Logic/EndgameEvaluator.cs`（`DefeatKind` + `CheckDefeat`）。敌意收购 = 董事会满意度 < 20% 且本财年净利润为负；监管接管 = 安全评级连续 2 个季度维持 D 级（`GameState.safetyConsecutiveDQuarters` 计数器）。每季度在 `TimeManager.AdvanceQuarter` 末尾判定，触发即 `gameOver`（`gameOverVictory=false`），文案归入 `QuarterResult.defeatText`/`urgentEventText`。
  - **重大空难**：`SafetyEventManager` 新增 `fatalCrashChancePerQuarter=0.002`（0.2%）致命抽取，`TriggerFatalCrash` 把 `safetyIndex` 归零、`safetyRating` 落 D、写安全日志并立即 `gameOver`（硬性失败不可挽回）。
  - **三模式入口**：新增 `Data/GameMode.cs`（`GameMode` 枚举 + `GameModeInfo`）。主菜单「新游戏」改为先进入「选择玩法模式」页（`SetupFlowController.ShowModeSelect`），按 `SaveManager.UnlockedLegend/NewGamePlus/Fast` 读取 PlayerPrefs 显示解锁状态（未解锁置灰）。效果落地：传奇=难度锁困难 + `legendModeActive`（`CompetitionManager` 竞争压力 ×1.5）；新游戏+=品牌净值 +10、董事会满意度 +10；快速=`fastModeActive`（`RandomEventManager` 触发概率 ×0.5）+ 开局 2× 速度（`MVPBootstrap.ApplyStartingModeSpeed`）。`activeMode/legendModeActive/fastModeActive/safetyConsecutiveDQuarters/majorCrashOccurred/lastDefeatKind` 全部持久化，清档键同步补齐。**精简期更新：`ShowModeSelect` 页面已删除，「新游戏」直接进入玩家资料创建，默认标准模式开局；传奇/新游戏+/快速及铁人入口随之下线（`GameMode`/`GameModeInfo` 定义保留）。**
  - 自测已加：监管接管连续 D 计数 / 敌意收购需「低满意度+亏损」双条件 / 模式名称映射。
- [x] **（GDB v1.0 对齐 · P1 内容池）** 董事会卡池 12→17 人（补 M/N/O/P/Q + Agenda 文案，`DataBase`/`BatchCreateDataAssets`/`SetupCatalogs`/`BoardManager` 四处同步）；部门负责人候选池 21→70 人（7 部门×10，`DataBase`+`BatchCreateDataAssets` 全量补全，含 GDB 第11章姓名/国籍/特质/薪酬）；CEO 目标清单 T-01~T-23 编号体系（新增 `TargetCatalog` 23 条权威目录 + `BoardTarget.targetId/difficulty` + `TargetGenerator` 化学组合→T-xx 映射，`MapChemistryToType` 已删除）；随机事件池 30→122 事件（新增「人为事故」分类 + 坏运保护 Pity Timer `quartersSinceRedCrisis` + 同分类限流每财年≤2 次 `eventCategoryCounters`，随存档持久化）。自测已加：事件池计数 / 目标目录计数 / 负面判定 / 人为事故 / 化学组合 C-L-M-Q
- [x] **（GDB v1.0 对齐 · P2 长线运营 · 货运）** 货运业务独立核算（GDB 14.6）：新增 `Logic/CargoEconomics.cs`（腹舱=边际收益：收入 8~15元/kg、装卸 1.5元/kg → 归一为美元/吨；纯货机=独立成本中心：吨公里收入 1.2~2.5元/tkm + 折旧/操作/保险）、`Data/CargoTypes.cs`（`CargoState` 三项 KPI：腹舱利用率/货机日利用率/货运吨公里收入）、`Systems/CargoManager.cs`（每季结算腹舱+货机 P&L 并回写 KPI）、`GameState.cargo` 持久化（`SaveManager` KeyCargo + 清档键）。机型目录新增货机 `A330-200F`(65t)/`B777F`(100t)，`Aircraft.cargoTons` 区分客货机，购机流程同步写入。`CargoEconomics`/`CargoTypes` 已 Link 进 `tools/balance`（纯 C# 无 UnityEngine 依赖），自测已加：腹舱日净 / 货机吨公里·日净·盈利判定 / `CargoManager` 结算 KPI。
- [x] **（GDB v1.0 对齐 · P2 长线运营 · 财务细分）** 税务与补贴 / PDP 订单簿+交付时间轴 / 航空保险 / 燃油自动采购预警 / 碳配额 EU ETS 免费比例逐年降+CORSIA（GDB 15.4/15.6/15.10/15.11/15.12/15.13）。落地：`Logic/AirportRegions.cs`（EEA/美/中/阿联酋/新加坡机场集合）· `Logic/TaxEconomics.cs`（枢纽国企业所得税 25/21/17/0%、转移定价调查概率=5%×激进×(1−合规)、补贴依赖判定）· `Logic/PdpEconomics.cs`（交付时间轴窄体2/宽体3/货机4年；付款节奏定金15%+24月10%+12月15%+交付60%；取消违约金20~30%）· `Logic/InsuranceEconomics.cs`（安全A/B/C费率1.0/1.5/2.0%×全险/基本险/自保档位；理赔轻微50%/重大80%）· `Logic/FuelProcurementEconomics.cs`（30日均价近似、低价/高价/库存<7天/地缘/旺季五类预警、紧急溢价35%）· `Logic/CarbonEconomics.cs`（EEA 判定、EU ETS 免费配额 2024=75%/2025=50%/2026+=0%、CORSIA 2027 强制、排放系数 3.15、财年1=2024 锚定）。数据：`Data/FinanceTypes.cs` 新增 `RouteSubsidy`/`AircraftOrder`/`InsuranceCoverage`/`FuelProcurementState`/`CarbonComplianceState` 及 Save 包装；`GameState` 新增 `routeSubsidies`/`aircraftOrders`/`insuranceCoverage`/`fuelProcurement`/`carbonCompliance`/`cumulativeTaxPaidWan` 等字段。系统：`FinanceManager` 扩展 `OnQuarterTick()`（补贴入账+燃油预警+PDP里程碑+保险保费）与 `SettleAnnualFinance()`（年结所得税+碳配额+补贴年限递减），并接入 `TimeManager.AdvanceQuarter/OnYearEnd`；`FleetManager` 新增 `FleetTotalValue()`（保险计价）与静态 `DeliverAircraft()`（PDP 交付入列）。持久化：`SaveManager` 新增 7 键（KeySubsidies/KeyAircraftOrders/KeyInsurance/KeyFuelProc/KeyCarbonComp/KeyCumTaxPaid/KeyYearTaxPaid）+ 清档键。`tools/balance` 已 Link 全部 6 个纯 C# 引擎并编译 0 错误；自测已加：税率表/转移定价/补贴依赖/交付年/付款构成/取消违约金/保费费率/理赔/预警条数/紧急溢价/EEA 判定/航线归类/免费配额/CORSIA 强制/ETS·CORSIA 需购量。UI：`FinancePanelUI` 新增「财务细分」标签页（税务 / 航空保险 / PDP 订单簿 / 碳合规 2×2 四宫格 `ScrollableLabel` + 保险档位切换按钮，随 `Refresh()` 与 `ShowDetail()` 刷新）。
- [x] **（GDB v1.0 对齐 · P2 长线运营 · 非航）** 非航收入与临空商业（枢纽吞吐解锁免税店/广告/贵宾厅/货运站，GDB 26.2）。落地：`Logic/AirportCommerceEconomics.cs`（解锁档位 500万/1000万/3000万人次；四类设施投资免税店300/900/1800万 vs 广告/贵宾厅/货运站150/450/900万，收入构成 4:2:2:2；年化 ROI 6%~12% 随吞吐档位提升，回收期 8~16 年；枢纽年吞吐=周班次×52×平均座位×客座率/10000）· `Data/NonAeroTypes.cs`（`CommerceFacility`/`AirportCommerceState`）· `GameState.commerce` 挂载。系统：`AirportCommerceManager`（`InvestFacility` 投资升级受现金+解锁档位约束、`Refresh` 只统计枢纽起讫航线聚合吞吐、`OnQuarterTick` 每季结算非航收入入账现金与年净利，收入不随航空景气波动即「安全垫」），接入 `TimeManager.AdvanceQuarter`。持久化：`SaveManager` 新增 `KeyCommerce` + 清档键。UI：战略部新增第 7 个入口「临空商业·非航收入」（`n108_临空商业`，L2），`StrategySubpagePanel.RenderCommerce` 四设施投资行 + 升级按钮 + 吞吐/解锁档位/年化收入/回收期，`StrategyRuntimeFactory`/`PrefabNavIntegration.EnsureStrategyConfig` 登记 n108。`tools/balance` 已 Link 并编译 0 错误；自测已加：解锁档位/ROI/投资表/年收入权重 4:2/回收期/吞吐公式/设施名/收入权重；PlayMode 新增 `AirportCommerce_InvestAndRefresh`（投资回写+解锁限制+吞吐公式）。
- [x] **（GDB v1.0 对齐 · P2 长线运营 · 存档模式）** 铁人模式 / What-If 分支存档（GDB 第31章 + 26.3）。落地：
  - **铁人模式**：`Logic/IronmanEconomics.cs`（纯 C#：遗产评分 ×1.2 系数 + 封顶 100 + `ApplyIronmanScore`）。`SetupDraft.Ironman` + `SetupFlowController.ShowModeSelect` 新增「铁人模式 关/开」循环开关（与玩法模式正交叠加，选任意模式时读入 Draft）；`EnterGameplay` 写入 `GameState.ironmanModeActive`；`SaveManager.HandleQuickSave` F5 铁人禁用（提示文案）+ 持久化 `KeyIronmanActive`；`LegacyScoreCalculator.Calc` 应用 ×1.2 系数。**精简期更新：铁人开关随 `ShowModeSelect` 一并移除，`Draft.Ironman` 保持默认 false。**
  - **自动存档**：`SaveManager.AutoSave()`（季度结束由 `TimeManager.AdvanceQuarter` 末尾调用）。铁人模式 = 仅主存档实时覆盖单槽；普通模式 = 额外写滚动 12 节点（= 最近 3 财年，GDB 31.1「保留最近 3 财年」）。
  - **多槽位快照基座**：`Systems/SaveSlotManager.cs`（静态：`CaptureToSlot`/`RestoreFromSlot`/`SlotExists`/`ClearSlot`/`WriteRollingAutoSlot`/`ClearAllSlots`），原理 = 把主存档逐键克隆到前缀命名空间，复用 `SaveManager.AllSaveKeys`（新增：主存档 82 键全量登记，含 MacroEconomy 三拼接键），避免重写几百行逐字段序列化。
  - **时间胶囊**：`Data/SaveSlotTypes.cs`（`TimeCapsuleMeta`/`BranchSaveMeta`/列表包装）；`Systems/ArchiveManager.cs`（静态：`CaptureTimeCapsule` 每财年末由 `TimeManager` 财年末分支调用，记录现金/市值/目标完成/安全评级并克隆完整快照，永久保留上限 20）。
  - **What-If 分支**：`ArchiveManager.CreateBranch`（备份当前主线到 `ACEO_Mainline_` → 克隆胶囊为独立分支 → 载入分支「在该节点重新决策」）· `LoadBranch`/`LoadCapsuleIntoMain`/`ReturnToMainline`（返回主线恢复到创建分支前进度，满足「独立于主线不影响当前进度」）。
  - **UI**：主菜单新增「历史回放」入口（`ShowMainMenu` 6 键重排 + `ShowArchivePanel` 时间轴倒序胶囊列表 + 创建分支 / 回溯节点 / 返回主线三操作）。清档键同步补齐（ironman + 胶囊/分支/slot + 此前遗漏的 CompanyNameZh 等）。
  - `tools/balance` 已 Link `IronmanEconomics`/`SaveSlotTypes` 并编译 0 错误；自测已加：铁人评分 ×1.2 / 封顶 100 / 零分不放大。**差分隔离已完成**（后置优化落地）：`SaveManager.ActivePrefix` 命名空间重定向（`SetF/SetS/SetI/GetF/GetS/GetI/HasKey` 封装 + 全局标志主键豁免）+ `SaveSlotManager`/`ArchiveManager` 槽位感知，分支内游玩不再写主存档链。
- [x] **（GDB v1.0 对齐 · P3 体验收口 · 渐进式解锁 + 失误保护）** 渐进式解锁财年「功能闸门」+ 时间回溯保护 + 失误保护次数（GDB 第28章 28.1）。落地：
  - **功能闸门**：`Logic/ProgressionGate.cs`（纯 C#：`UnlockYearForDept` 精确前缀 `n13_` 等映射 → OPS/MKT/MNT 第1财年、FIN/HR 第2财年、STG/SAF 第3财年、第4财年起全解锁；`IsDeptUnlocked`/`StageName`/`LockHint`）。UI 拦截在 `PrefabNavIntegration.OnTabSelected`（未解锁弹「当前为 X 阶段，此功能需第 Y 财年解锁」），视觉锁在 `ShellBarView.RefreshLocks`（底栏部门 Tab 加 🔒 前缀 + `button.interactable=false`，由 `OpenHomeDashboard` + `GameState.OnStateChanged` 触发刷新）。
  - **失误保护 · 时间回溯**：`Systems/MistakeProtectionManager.cs`（静态：`CaptureQuarterStart` 季度初记录现金/满意度快照 + 克隆主存档到 `SaveSlotManager.QuarterStartSlot`；`CheckAndMaybeRollback` 季度末判定重大失误 → 恢复快照 + `Load()` + 扣减次数）。重大失误判定（`ProgressionGate.CheckMistake` 纯函数）：单季净亏损超现金 15% / 满意度季降幅 >20 点或 <30 / 发生安全事故。见习期共 3 次、每财年最多 1 次（`mistakeProtectionRemaining`/`mistakeProtectionUsedYear`）。铁人模式不启用（实时覆盖无回退点）。`TimeManager.AdvanceQuarter` 开头快照 + 季度末（AutoSave 前）检查回滚，回滚命中则 `result.mistakeRollbackText` 并跳过自动存档。
  - **持久化**：`GameState` 新增 `mistakeProtectionRemaining/UsedYear/quarterStartCash/quarterStartSatisfaction`；`SaveManager` 新增 2 key + 入 `AllSaveKeys`；`ClearGameSaveMenu` 补 2 key。
  - `tools/balance` 已 Link `ProgressionGate` 并编译 0 错误；自测已加：解锁财年映射 / 无闸门页 / 第1财年 FIN 未解锁第2财年解锁 / 四种失误判定 + 无失误 + CanProtect 见习期边界。
- [x] **（GDB v1.0 对齐 · P3 体验收口 · 见习期软保护）** 第 2/3 财年特殊保护机制（GDB 第28章 28.1「特殊保护机制」，第 4 财年起全部失效进入硬核）。落地（全部复用 `ProgressionGate` 纯 C# 软保护查询）：
  - **超支惩罚减半（第2财年）**：`ProgressionGate.OverrunPenalty`（Y2→−5，其余 −10）。新增轻量超支问责：`FinanceManager.AccrueBudgetSpend`（季度支出按 `FinanceEconomics.ShareOf` 部门份额分摊累计到 `BudgetItem.spentWan`）+ `FinanceManager.SettleBudgetOverrun`（财年末判定各部门 `spentWan > amountWan` → 扣满意度 + 归零 `spentWan` 新年重计）；`TimeManager.AdvanceQuarter` 季度累计 + `yearEnded` 块财年末问责。
  - **罢工倒计时延长 50%（第2财年）**：`ProgressionGate.StrikeCountdownShrinkScale`（Y2→0.5）；`HrManager.NegotiateUnion` 强硬/拖延的倒计时缩短 `−3` 改为 `(int)(−3×scale)`（Y2 仅 −1，缓冲翻倍）。
  - **燃油对冲亏损上限锁定 20%（第2财年）**：`ProgressionGate.HedgeLossCapRatio`（Y2→0.20，其余 0=不设限）；`HedgeManager.SettleQuarter` 单季亏损钳制在 `qQty×lockPrice×0.20`。
  - **战略豁免自动获批 1 项（第3财年）**：`ProgressionGate.WaiverLimit`（Y3 起 3，其余 2）；`StrategyManager.ApplyWaiver` 额度判断 + `StrategySubpagePanel` 显示 `/N` 动态。
  - **安全评级低于阈值不触发局方停飞（第3财年）**：`ProgressionGate.SkipRegulatoryTakeover`（Y3→true）；`EndgameEvaluator.CheckDefeat` 监管接管分支跳过（连续 D 级计数仍保留）。
  - `tools/balance` 已 Link（`ProgressionGate`/`FinanceEconomics` 均已在册）并编译 0 错误；自测已加 5 项软保护边界（超支/罢工/对冲/豁免额度/局方豁免）。
- [x] **（GDB v1.0 对齐 · P3 体验收口 · 教程/词典/提示/消息/速度）** 6 个强制教程硬锚点；内嵌术语词典条目填充；智能提示系统（四类幕僚主动提醒）；消息中心（四级优先级 + 聚合 + 200 条历史）；速度档位 1×/2×/3×。落地：
  - **术语词典（GDB 28.3，P0）**：~~原实现 `TermEntry`/`TermCatalog` + `TermDictionaryPanelUI`（n50）+ 悬停 Tooltip~~ **2026-08-31 按精简决策整体移除**（面板/按钮/路由注册/悬停 Tooltip/数据文件 `TermData.cs` 均删除；`Phase0VerifyMenu` 类型校验同步移除）。
  - **消息中心（GDB 29.1，P0）**：`Data/MessageTypes.cs`（`MessagePriority` Emergency/Warning/Info/Success + `GameMessage` + `MessageSave`）+ `Logic/MessageCenter.cs`（静态：`Push`/`MarkAllRead`/`Filter`/`Load`/`Snapshot`，同部门同优先级短窗口聚合为「×N」、封顶 200 条、`UnreadCount`、`Changed` 事件）。新增 `UI/Shell/MessageCenterPanelUI.cs`（`n51_消息中心`：优先级过滤按钮 + 滚动列表 + 全部已读）；顶栏「🔔」铃铛 + 未读角标（`MVPBootstrap`，`MessageCenter.Changed` 驱动刷新）。**精简期更新：顶栏「信」按钮与未读角标已删除，功能并入顶部快讯栏——整条快讯栏可点击打开消息中心，未读数显示在「快讯」前缀（有未读变红 `快讯(N)`）。**
  - **智能提示（GDB 28.4）**：`Logic/SmartAlertLogic.cs` 纯 C# 四类判定（现金跑道<4月 / 空缺率>15% / 战略一致性<50 / 准点率连续3季<70%）；`Systems/SmartAlertManager.cs` 季度末取数 + 推消息中心（Warning）+ 返回文案。`GameState` 新增 `onTimeLowQuarters`。
  - **速度档位（GDB 第30章）**：新增 `Systems/GameSpeed.cs` 静态控制器（`Set`/`Pause`/`Resume1x`/`OnLevelChanged`，驱动 `Time.timeScale`）。`MVPBootstrap` 顶栏速度键。**精简期更新：档位由 1×/2×/4×/8×/暂停 精简为 1×/2×/3×（`GameSpeed.L1/L2/L3`，`Scales={1f,2f,3f}`，默认 1×）；删除顶栏「8×」「‖」按钮及 `EnsureSpeedLockOverlay` 全屏锁定遮罩、`OnGameSpeedChanged`、`ShowOfflineBriefing` 离线简报整条 8× 逻辑。`Paused` 保留为「红色危机/到期邮件」的内部强制暂停。**
  - **6 强制教程硬锚点（GDB 28.2）**：`Logic/DeptTutorialCatalog.cs`（OPS/MKT/FIN/HR/STG/SAF 各 1 条，title + 3 步骤）+ `Systems/DeptTutorialManager.cs`（`ShouldShow`/`MarkSeen`）；`PrefabNavIntegration.OnTabSelected` 在闸门解锁后首次进入部门 → 弹 `MVPBootstrap.ShowInfoPopup`（新公开方法）。`GameState` 新增 `deptTutorialSeen`。**2026-10-08 按 CEO 裁定整体移除**：先停用「人员/财务/愿景」三个底栏模块的弹窗（`DeptTutorialCatalog.Suppressed`），随后确认彻底拆除——两个文件、`PrefabNavIntegration` 触发块、`GameState.deptTutorialSeen`、`MVPBootstrap.ShowInfoPopup`、`SaveManager.KeyDeptTutorialSeen`、`ClearGameSaveMenu` 对应 key、`tools/balance` 的 Link 与自测两项均已删除；`_Recovery/0.unity` 里残留的 `deptTutorialSeen: []` 亦已清除。**理由与后续：教程文案质量不达标，CEO 裁定「先删除清理，等游戏开发的最后阶段再统一重写教程」；换言之本阶段不做任何强制引导，教程属于收尾阶段的内容项。此举偏离 GDB 28.2 的「强制教程」规格，属有意取消。**
  - **红色危机强制暂停（GDB 30.2）**：`TimeManager` 季度末判定 AOG / 现金为负 / 跑道<2月 → 推紧急消息 + `GameSpeed.Pause()`；同处推季度结算信息消息 + `SmartAlertManager.OnQuarterEnd`。
  - **持久化**：`SaveManager` 新增 `KeyMessages`/`KeyDeptTutorialSeen`/`KeyOnTimeLowQuarters`（均入 `AllSaveKeys` + `StringListSave`）；`ClearGameSaveMenu` 补 3 key。**2026-10-08 部门教程移除后，`KeyDeptTutorialSeen` 常量/存档读写/`AllSaveKeys` 登记均已删除（`StringListSave` 保留，仍由 `KeyBoardMemberIds` 使用）；`ACEO_DeptTutorialSeen` 转为孤儿键，改由 `ClearGameSaveMenu.LegacyKeys` 做「一次性清理」（菜单项「清理遗留键 Clean Legacy Keys」，可独立执行、不动现存字段，每轮存档清完后即可整段删除）。**
  - `tools/balance` 已 Link `MessageTypes`/`MessageCenter`/`SmartAlertLogic`/`DeptTutorialCatalog` 并编译 0 错误；自测已加：部门教程目录数、四类智能提示边界、消息聚合/未读/过滤。**精简期更新：`TermData.cs` 从 tools/balance 的 Link 中一并移除。2026-10-08 部门教程移除后：`DeptTutorialCatalog` 的 Link 与「部门教程目录数」自测一并删除，沿用中的「四类智能提示边界」等断言保留。**
- [ ] **（GDB v1.0 对齐 · 资产/运营后置）** 可写代码部分已完成（文本本地化基础设施 / 无障碍色盲+键盘导航 / 数据遥测 / Mod 数据框架）；剩余纯资产项（董事头像美术、BGM/SFX、Lua 规则脚本 Lv.3、创意工坊/商店接入、可视化 Mod 编辑器）—— 发布/运营层可后置
  - **文本本地化（GDB 33.7）**：`Logic/L10n.cs` 纯 C#（`Language` 枚举 + `LOC_` 键约定 + 内置双语词典 20 条 + `T(key)`/`T(key,args)` 回退键名 + `Register`/`RegisterCsv` 批量导入 + `OnLanguageChanged`）。`Systems/AppSettings.cs` 持久化语言（PlayerPrefs，独立于存档槽）。设置面板语言开关即时重译。
  - **无障碍（GDB 29.5）**：`Logic/Accessibility.cs` 纯 C#（`ColorblindMode` 标准/高对比度/红绿盲/蓝黄盲 + `StatusHex()` 色盲安全调色板 + `FontScale` 三级 + `FontMultiplier()`）。`UITheme` 的 `Danger/Warning/Success` 改为动态 getter 读调色板（全游戏状态色即时重映射）；`UIFactory.Label` 字号乘 `FontMultiplier()`（全局字体缩放）；`Systems/KeyboardNav.cs` ESC 后退一层（输入框聚焦时跳过）。`AppSettings` 持久化色盲/字号。
  - **数据遥测（GDB 38）**：`Logic/Telemetry.cs` 纯 C#（匿名事件缓冲 `Track`/`Snapshot`/`Clear` + `OptOut` 退出 + 封顶 5000）。`Systems/TelemetryManager.cs` 接线（`game_start`/`quarter_advanced`/`dept_visited`/`mod_loaded`）+ `FlushToFile` 落盘 JSON（persistentDataPath/telemetry.json）。设置面板遥测开关；`Editor/PostProductionDebugMenu.cs` 落盘/清空。
  - **Mod 支持（GDB 第36章 Lv.1 数据 Mod）**：`Logic/ModFramework.cs` 纯 C#（`ModManifest`/`ModDataEntry`/`ModConflict` + `ModRegistry` 加载顺序 核心0→DLC1→社区2按id字母序 + 冲突检测 + `GetFloat`/`GetInt`/`GetParam` 覆盖查询）。`Systems/ModManager.cs` 扫 `persistentDataPath/Mods`+`dataPath/Mods` 读 `manifest.json`（JsonUtility）→ 注册 → `Rebuild`。设置面板 Mod 状态；调试菜单重载 Mod。
  - `tools/balance` 已 Link `L10n`/`Accessibility`/`Telemetry`/`ModFramework` 并编译 0 错误；自测已加：本地化回退/CSV、色盲调色板差异/字体倍率、遥测缓冲+退出静默、Mod 加载顺序/覆盖/冲突/缺省回退。
- [x] **（GDB v1.0 新增设计 · P2）** 「全球航空动态」无痕消息流：单一消息管道混流真实外部数据 + 程序生成事件，统一伪装为「收件箱邮件 / 顶部跑马灯」。落地：
  - **数据层**：`Data/DynamicFeedTypes.cs`（`DynamicFeedItem` + `FeedCategory` 市场/监管/行业/季节/竞对 + `FeedEffect` 燃油/客座率/准点率/现金/满意度 + `FeedSave`/`FeedMetaSave`）。
  - **事件发生器**：`Logic/EventGenerator.cs` 纯 C#，24 条「伪装新闻」模板（12 条恒可触发的「行业日报」+ 12 条条件压力事件），`Context` 注入世界快照 + 玩家状态，`seed` 决定挑选（离线可复现）；条件含油价平淡≥3季 / 连续盈利≥2季 / 无新闻≥3季 / 准点率<75 / 客座率<50 / 满意度<40 / 现金为负 / Q1春运 Q3暑运 Q4黄金周。
  - **数据输入 + 混合**：`Logic/ExternalDataFetcher.cs`（失败静默跳过）+ `Logic/MessageMerger.cs`（统一排序/同季度同标题去重/封顶）。
    - **两条独立管道**：新闻源（默认开启，Google News 中文航空主题 RSS，现实 30 分钟一轮）只提供「内容」；行情源（**默认开启**，可关，Yahoo 布伦特 `BZ=F`，现实 6 小时一轮）只提供「价格」= 原油 ×7.82 + 裂解价差 → 航油，游戏油价每季最多向它靠拢 ±8%（`MacroEconomyManager.ConvergeToRealQuote` / `QuoteConvergeForecast`）。新闻源的查询串刻意不含「油价」，避免财经期货新闻灌进收件箱。
    - **实时状态展示**：`ExternalDataFetcher.QuoteStatusLine(compact)` 为共享口径，出现在 n115 外部信息源（含「行情源 开/关」按钮态与本季收敛预告）、n116 行情源、财经页对冲工作台、宏观面板、运营面板共 5 处；未开启 / 断网 / 尚未取到时各给对应说明，不留空行。
    - **行情收敛发函**：现实报价每季收敛成功时，除进季报 `eventText` 外，另发一封「进度汇报：航油现货按现实行情收敛」（`InboxManager.NotifyFuelQuoteConverge`，财务总监 · 行情通报 · 无需批复 · 同季去重），与「航油价格波动」（游戏引擎自身波动）分开两封；新闻源则维持原有的「每条来文即一封公文」机制（`ExternalEventManager.PushReceiveMail`/`PushTriggerMail`）。
  - **季度钩子**：`Systems/DynamicFeedManager.cs` 每季度 `OnQuarterTick(gs, net)`：更新连续盈利计数 → 外部数据(空) → 生成 → 混合 → 应用影响（`fuelPricePerTon`/`loadFactorPercent`/`onTimePercent`/`cash`/`boardSatisfaction`）→ 落 `gs.dynamicFeed`（封顶 60 条）→ 推 `MessageCenter`（发件人=新闻社名，监管/满意度降级为 Warning，其余 Info）；并回写 `oilStableQuarters`/`quietQuarters`。
  - **顶部跑马灯**：`MVPBootstrap` 新增 `BuildNewsTicker`（顶栏下方 22px 薄条，`UITheme.TickerHeight`）+ `TickerLoop` 协程每 3.5s 滚动最新动态（📈市场/🟡监管/📅季节/🛫行业图标 + 来源 + 标题），中心内容区下移 22px。AO 口播预留 `DynamicFeedManager.GetAdvisorBriefing(gs)`。
  - **接线 + 持久化**：`TimeManager.AdvanceQuarter` 季度末（回滚检查后）注入 feed，摘要写入 `QuarterResult.feedText`；`GameState` 新增 `dynamicFeed`/`profitStreak`/`oilStableQuarters`/`quietQuarters`；`SaveManager` 新增 `KeyDynamicFeed`/`KeyFeedMeta`（入 `AllSaveKeys`）+ 读档 `RestoreStamp`；`ClearGameSaveMenu` 补 2 key。
  - `tools/balance` 已 Link `DynamicFeedTypes`/`EventGenerator`/`ExternalDataFetcher`/`MessageMerger` 并编译 0 错误；自测已加：模板数 24、平淡季/压力季生成数与条件命中数、合并去重计数。

- [x] **（技术债 / 架构后置 · 数值口径与存档隔离）** 五项技术债中可写代码部分全部落地（Prefab 资产化量产与美术/音频仍后置）：
  - **BOARD / CROSS 数值闭环入 tools/balance**：新增纯 C# `Logic/BoardEconomics.cs`（年度目标保底阈值 净利50/客座率75/安全80、续聘线 Easy50/Normal60/Hard75、见习保护 3 年、满意度增减）+ `Logic/CrossEconomics.cs`（六部门 KPI 加权平均 0.6 + 短板惩罚 0.4 → 协同指数/档位）。`tools/balance` 已 Link 并在 `AirlineYearSimulator` 年闭环计算 `Board*`/`Cross*` 指标入 `YearSummary`；Unity 侧 `YearEndEvaluator`/`TimeManager`/`TargetGenerator` 改用 `BoardEconomics` 单一源。
  - **Legacy 终局结算数值校验入 tools/balance**：`Logic/LegacyScoreCalculator.cs` 抽纯 C#（`LegacyInput` 快照 + 自定义 Clamp，去 UnityEngine/GameState 依赖）+ `LegacySnapshotBuilder.cs`（GameState→LegacyInput 映射）+ `GameplayEnums.cs`（`DifficultyMode`/`VoteOutcome` 纯枚举）。`tools/balance/Legacy/LegacyBalanceChecker.cs` 扫参验证市值可达性/覆盖基准/FH 基准/评级边界；「市值维度终局趋近保底满分」结论记录在案。
  - **What-If 分支完整差分隔离**：`SaveManager.ActivePrefix` 命名空间重定向（`P(key)`+`SetF/SetS/SetI/GetF/GetS/GetI/HasKey` 封装，~150 处 PlayerPrefs 调用机械替换；全局标志 KeySetupDone/KeyLegendMode/NewGamePlus/FastMode 主键豁免）；`SaveSlotManager.CaptureToSlot/RestoreFromSlot` 感知 ActivePrefix；`ArchiveManager.CreateBranch/LoadBranch/LoadCapsuleIntoMain/ReturnToMainline` 切换命名空间；`ClearAllData` 重置前缀。分支内游玩不再写主存档链。
  - **Runtime 数据源接线（MacroEconomy/RandomEvent/AI 竞对）**：三系统均已在 `TimeManager.AdvanceQuarter` 接线（`MacroEconomyManager.OnNewFiscalYear`/`ApplyFuelTick`、`RandomEventManager.OnQuarterTick`、`AIManager`+`CompetitionManager.OnQuarterTick`）。本次收敛 MacroEconomy 油价数据源：新增 `MacroEconomyManager.ApplyFuelTick`（财年 Q1 宏观倾向×0.25 + 噪声±4%、Q2–Q4 漂移±3%、clamp[400,1600]，口径对齐 `tools/balance` MacroEconomyEngine），删除 `TimeManager` 分散的 `ApplyMildFuelFluctuation`/`ApplyMacroOilTendency`。

> 差距依据：GDB v1.0（38 章）逐章对齐代码库（188 C# 脚本 + 103 页 UI 清单）。提取文本存于 `docs/GameDesignBible/_gdb_v1.0_extract.txt` 供后续检索。

---

## 待办设计：全球航空动态消息流（无痕伪装）

> 来源：2026-08-17 与 DeepSeek 网页版对话总结的混流设计。定位为 GDB 第 22 章（统一随机事件引擎）与第 29 章（通知/消息系统）的交叉补强，归属 P2 长线运营。

### 核心理念：单一「全球航空动态」消息流

移除「现实世界」标签与任何开关。所有外部数据（油价、汇率、新闻）与内部生成事件，都通过同一管道流入游戏，统一包装为「收件箱邮件」「AO 推送」「顶部滚动快讯」。

- 玩家感知：游戏世界有自己的「新闻社」和「情报网」，每天都有新情况，无法预判下一步。
- 来源混合：现实 API 数据与程序生成事件在代码层即混在一起，时间戳与措辞风格完全一致，玩家永远无法分辨。

### 事件发生器（伪装成现实消息的「替补」）

现实数据「平淡期」时，内置「动态事件发生器」自动接管，注入高拟真度「伪新闻」，确保每日/每周都有压力。

| 触发条件 | 生成事件示例（伪装风格） | 游戏内影响 |
|---|---|---|
| 现实油价连续 3 天无波动 | 「据路透社报道，OPEC+ 内部就减产配额产生分歧，市场预期供应收紧。燃油价格应声上涨 6%。」 | 下季度燃油成本 +6% |
| 现实汇率连续 5 天无变化 | 「中国人民银行今日宣布调整中间价定价机制，人民币对美元单日贬值 1.2%，进口飞机成本上升。」 | 购机价格 +1.2% |
| 无重大航空新闻超过 7 天 | 「突发：空客图卢兹工厂因电力故障停产 72 小时，A320neo 交付延迟至少 2 个月。」 | 新飞机到货推迟 2 个季度 |
| 玩家连续盈利 2 个季度（太安逸） | 「监管机构通知：因行业反垄断审查，您与某竞对的代码共享协议将被暂停 90 天。」 | 合作收益中断，客座率 -3% |
| 随机时间（保底） | 「国庆黄金周临近，民航局宣布全国热门机场将实施流量控制，准点率预期下降 10%。」 | 下月准点率承压 |

**伪装策略**：信息来源多样化（「路透社」「民航局」「波音公司」「气象局」等真实机构名）；措辞新闻化（「据……报道」「今日起」「经核实」，杜绝「系统提示」出戏表述）；数值精准嫁接（生成数值落在现实波动区间 ±20% 内）。

### 多周期触发（每天/每周/每月新鲜压力）

| 触发周期 | 消息来源策略（混合） | 玩家感知（统一呈现） |
|---|---|---|
| 每日首登 | 查现实油价/汇率 → 有变化直接推；无变化随机生成「行业日报」 | 每日 AO 必推「今日简报」 |
| 每周一 | 扫描上周游戏内客座率/准点率 + 外部市场趋势 → 「行业周报」 | 「上周全球航空业回顾：需求复苏强劲，但燃油成本承压。」 |
| 每月 1 日 | 基于上月表现 + 宏观趋势生成「月度挑战」（利润高→成本上升事件；利润低→补贴/纾困事件） | 「民航局发布上月行业运行数据，您的航司排名第 X 位……」 |
| 季节/节气 | 锚定中国农历与公历节日（本地日历，无需联网） | 「春运倒计时 30 天，订票系统已出现拥堵，建议提前部署加班航班。」 |

### 与游戏速度控制的联动

| 速度档位 | 消息推送策略 |
|---|---|
| 1× / 2× | 实时推送，「市场动态」逐条出现 |
| 3× | 消息聚合为「快讯摘要」，标注「有 X 条未读动态」；减速后 AO 口播补讲 |

### UI 呈现（完全无痕）

- **收件箱**：统一「新闻/公告」分类，主题不加「现实/模拟」前缀，发件人直接写「彭博社」「民航局」「天气中心」。
- **AO 推送**：措辞「老板，看到新闻了没？油价又涨了……」而非「系统检测到现实数据变化」。
- **顶部跑马灯**：滚动显示「📈 燃油价格 +5% | 🛫 北京首都机场发布流控预警」，绝不标注来源类型。

### 落地清单（快速落地）

1. **数据输入层**：`ExternalDataFetcher` 拉取油价/汇率，失败则静默跳过。
2. **事件发生器**：`EventGenerator`，内置 20+ 条预设「伪装新闻模板」，按条件触发。
3. **消息混合器**：`MessageMerger`，两类数据统一格式化，写入 `GameState.dynamicFeed`。
4. **AO 读表**：AO 直接读取 `dynamicFeed` 最新条并转化为口语化推送。

### 维护命令

更新页面清单（白板 CATALOG 变更后执行）：

```powershell
python "docs/Development Manual/Development Task Book/_gen_inventory.py"
```
