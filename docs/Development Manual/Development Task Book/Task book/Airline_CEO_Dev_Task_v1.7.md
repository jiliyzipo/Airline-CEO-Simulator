# ✈️ Airline CEO Simulator · 技术任务书 v1.7
**（面向 Cursor + DeepSeek API 的 AI 编码实施蓝图）**

**适用对象：** 独立开发者（无编程基础，依赖 AI 编程）  
**核心目标：** 将 GDD 策划案转化为 Unity C# 可执行代码的精确指令。  
**工程根目录：** `Unity Project/`  
**现行版本：** 请以 **v1.7** 为准（v1.0～v1.6 仅作历史存档）  
**章节对齐：** 与 [`Airline_CEO_Dev_Roadmap_v1.8.md`](../GameDesignBible/MVP/V1.0/Airline_CEO_Dev_Roadmap_v1.8.md) **一～四** 同序同名  
**更新日期：** 2026-08-05

---

## 文档修订记录

| 版本 | 说明 |
| :--- | :--- |
| v1.0～v1.4 | 历史施工图（见仓库存档） |
| v1.5 | Phase 8～10 验收；Phase 11 施工细节初稿 |
| v1.6 | Phase 11 AI 竞对 Play ✅ 2026-08-03；Phase 12 随机事件 Play ✅ 2026-08-04；Phase 13 宏观 Play ✅ 2026-08-04；Phase 14 UI/UX 施工中 |
| **v1.7** | **Phase 14 UI/UX Play ✅ 2026-08-04**；**Phase 15 跨部门联动 ✅ 2026-08-04**；现行施工图；下一方向 **MVP 1.0 完善 → V2.0**；发布准备 ⏸️ 待定 |

> Phase 1～10 的逐文件伪代码与长表仍保留在 [`Airline_CEO_Dev_Task_v1.5.md`](Airline_CEO_Dev_Task_v1.5.md)。本版以**总览 + Phase 11～15 定稿**为主；发布准备暂不排期。

**修订 §17（2026-10-11，人力 · 职业介绍所）**：新增投机候选池「TalentMarket」 —— 候选人依编制表岗位规格生成、每月 1/16 日按「校招=专员档 / 猎头=主管副手 / 海外=运行岗+稀缺主管」投放渠道，待聘名单不再只有总监档；「录用」闸门由「必须先有呈请案」放宽为「有编制缺口即可直录」（无案时经 `HrManager.OpeningFor` 判缺口），并修掉编制席被错标 `isOperational=true` 的「在编却不算在编」；职业介绍所顶栏四个来源芯片取消、收进过滤抽屉第 ① 段「来源与岗位」。详见 [`职业介绍所人才渠道合并_方案_v1.0.md`](../../../GameDesignBible/MVP/V3.0/职业介绍所人才渠道合并_方案_v1.0.md)。

---

## 章节对照（任务书 ↔ 路线图）

| 任务书 | 路线图 v1.8 | 内容 |
| :--- | :--- | :--- |
| **第 1 章** | 一、MVP 开发总览 | 按玩家体验时间线 / GDD 核心循环 |
| **第 2 章** | 二、技术实现阶段清单 | 已完成摘要 + Phase 11～15 定稿；发布准备待定 |
| **第 3 章** | 三、详细功能对照 | GDD 功能点 ↔ Phase ↔ 状态 |
| **第 4 章** | 四、里程碑概览 | 0～120 分钟节点摘要 |
| 附录 A～C | — | 文案规范 / Cursor 提示词 / 菜单与 Play |

**图例：** ✅ 已完成并验收 ｜ ⬜ 待开发 ｜ 🔜 规划中 ｜ ⏸️ 待定（暂不排期）

---

## 第 1 章：MVP 开发总览（按 GDD 核心循环）

> 对应路线图 **「一」**。

| 玩家体验节点 | 对应 GDD 章节 | 核心功能 | 完成状态 | 任务书节 |
| :--- | :--- | :--- | :---: | :--- |
| 0～15分钟（首航体验） | 第3章 + 第7.1～7.4 | 首航、现金跑道、定价、RPM | ✅ | v1.5 §2.1～2.4、2.7～2.8 |
| 15～30分钟（危机应对） | 第7.2 | 脚本化 AOG | ✅ 2026-08-01 | v1.5 §2.12 |
| 30～60分钟（首次考核） | 第5、4章 | 董事会目标、年度结算、续聘 | ✅ | v1.5 §2.9～2.10 |
| 60～120分钟（战略纵深） | 第7.6、7.7 | 安全事件、航权、扩张提案 | ✅ 2026-08-01 | v1.5 §2.11、2.13 |
| 120分钟以后 | 第6章 + 7.4 + 11/16 + 9.2 + 9.3 | 人事 / 对冲 / 机队 / AI竞对 / 随机事件 | ✅ 2026-08-04 | v1.5 §2.14～2.16；**本版 §2.17、2.20** |
| MVP 后期 | 第8～12章 | 宏观 / UI / 联动 | ✅ Phase 13～15 | **本版 §2.21～2.23** |
| 后续规划 | — | MVP 1.0 完善 → V2.0；发布待定 | 🔜 / ⏸️ | **§2.24 待定** |

---

## 第 2 章：技术实现阶段清单

> 对应路线图 **「二」**。

| 阶段 | 核心产出 | 对应文件 / 系统 | 状态 | 本节 |
| :--- | :--- | :--- | :---: | :---: |
| 数据层～Phase 7 | 见 v1.5 | 数据 / 逻辑 / UI / AI / 董事会 / 安全航权 | ✅ | v1.5 2.1～2.11 |
| AOG / 扩张 | 脚本化危机 + 战略性扩张 | AOGManager、ExpansionProposal* | ✅ 已验收 | v1.5 2.12～2.13 |
| Phase 8 | 部门任命 | DepartmentManager、HRUI | ✅ | v1.5 2.14 |
| Phase 9 | 对冲 + 融资 + 引导 | Hedge*、Loan*、Finance*、HedgeTutorial | ✅ | v1.5 2.15 |
| Phase 10 | 机队 | FleetManager、FleetUI、FleetPurchaseUI | ✅ | v1.5 2.16 |
| **Phase 11** | **AI 竞争对手** | **AIManager、CompetitionManager、CompetitionUI** | **✅ 已验收** | **2.17** |
| 编辑器 / 清档 | 清档与 Debug 菜单 | ClearSave、各 Phase Debug | ✅ | 2.19 |
| **Phase 12** | **随机事件引擎扩展** | **RandomEventManager、Catalog、EventLogUI** | **✅ 已验收** | **2.20** |
| Phase 13 | 宏观经济引擎 | MacroEconomyManager | ✅ 已验收 | 2.21 |
| Phase 14 | UI/UX 精修 | 术语 Tooltip / 弹窗动画 / 机型图标 / API修复 | ✅ 已验收 | 2.22 |
| **Phase 15** | **跨部门联动测试** | **时序 / 品牌传导 / 现金保护 / 扩张前置** | **✅ 已验收** | **2.23** |
| 发布准备 | Steam / 素材 / 测试计划 | 文档与素材清单 | ⏸️ 待定 | 2.24 |

**工程骨架（五文件夹）：**

| 文件夹 | 命名空间 | MVP 状态 |
| :--- | :--- | :--- |
| `Data/` | `AirlineCEO.Data` | ✅（含 `AIAirline.cs`、`TermData.cs`） |
| `Logic/` | `AirlineCEO.Logic` | ✅ |
| `Systems/` | `AirlineCEO.Systems` | ✅（含 `AIManager`、`CompetitionManager`、`TooltipManager`） |
| `UI/` | `AirlineCEO.UI` | ✅（含 `CompetitionUI`、`TermTooltip`、`AircraftIcon`） |
| `AI/` | `AirlineCEO.AI` | ✅ |

原则：先里后外、先功能后美观；AI 建议须玩家点「执行建议」才改状态。

---

### 2.17 Phase 11 · AI 竞争对手系统 ✅ 已验收

> 对应 GDD 第 9.2 节。市场面板「竞争态势」：统一行（份额条 + 详情对齐）、代码共享、页签高亮。

| 子项 | 说明 | 文件 |
| :--- | :--- | :--- |
| 11.1 数据 | `AIAirline`；类型 full_service / low_cost / regional / cargo；状态 stable / expanding / defensive / dying | `Data/AIAirline.cs` |
| 11.2 状态机 | 扩张打玩家 Top 航线；防御削线/抛机；垂死一季后破产；约 2 年新 AI 入场 | `Systems/AIManager.cs` |
| 11.3 竞争 | ASK 份额合计≈100%；重叠航线压客座/品牌；代码共享接受/拒绝/撕毁 | `CompetitionManager.cs` |
| 11.4 UI | 市场「航线分析 / 竞争态势」页签；选中 Accent 高亮；行内左右对齐 | `CompetitionUI.cs` · `MVPBootstrap` |
| 季度钩子 | `TimeManager.AdvanceQuarter` 调 AI + Competition tick | `TimeManager.cs` |
| 存档 | `ACEO_AIAirlines`；清档含此键 | `SaveManager` · `ClearGameSaveMenu` |
| 场景 | Setup 含 AIManager / CompetitionManager | `SetupMVPScene.cs` |
| 调试 | Force Expansion / Dying / Kill / Propose Code Share | `Phase11DebugMenu.cs` |

**初始 AI（4 家）：** 云翼（全服务）· 青鸾（低成本）· 银燕（区域）· 北辰货运。  

**验收步骤（Play）：** 清档 → Play → 市场→竞争态势见 4 家与份额 → 页签变亮蓝 → Debug Force Expansion → 推进一季 → Force Dying → 再推进破产 → Propose Code Share → F5/F9。  
**验收结果：** ✅ 已打勾（2026-08-03 Play）：Console 无红字；份额行对齐与页签高亮已修。

---

### 2.19 编辑器工具与清档 ✅

| 菜单 | 文件 | 说明 |
| :--- | :--- | :--- |
| `AirlineCEO/清档 Clear Save` | `ClearGameSaveMenu.cs` | 删 `ACEO_*`（含 `ACEO_AIAirlines`），保留 API Key |
| `AirlineCEO/Setup MVP Scene Objects` | `SetupMVPScene.cs` | 含机队 + AI 竞对 + 宏观 + Tooltip Manager |
| `Debug/Force AI Expansion` 等 | `Phase11DebugMenu.cs` | 竞对调试 |
| `Debug/Force Random Event` 等 | `Phase12DebugMenu.cs` | 随机事件调试 |
| 其余 Phase 7～10 Debug | 见附录 C | — |

---

### 2.20 Phase 12 · 随机事件引擎扩展 ✅ 已验收

> 对应 GDD 第 9.3 节。可扩展事件池 + 连锁；与航油大事件互斥；不侵入 SafetyEventManager。

| 子项 | 说明 | 文件 |
| :--- | :--- | :--- |
| 12.1 数据 | `RandomEventDef` / `EventEffect` / 日志与存档结构 | `Data/RandomEventDef.cs` |
| 12.2 池 | 内置约 30 条（自然/市场/内部/政策/供应链）；台风五级连锁 | `Systems/RandomEventCatalog.cs` |
| 12.3 引擎 | 每季约 55% 抽 0～1；加权；一次性事件；跑道<3 月负面效果×0.6 | `RandomEventManager.cs` |
| 12.4 连锁 | `chainEventId` + `chainDelayQuarters`；支持 A→B→C… | 同 Manager |
| 12.5 UI | 季度结算弹窗带事件文案；运营「事件日志」近 20 条 | `EventLogUI.cs` · `MVPBootstrap` |
| 12.6 接入 | `TimeManager`：随机事件后若未占油价槽再 `TryOilEvent` | `TimeManager.cs` |
| 存档 | `ACEO_RandomEvents` | `SaveManager` / 清档 |
| 调试 | Typhoon / Fuel / Demand / Force Chain / List Catalog | `Phase12DebugMenu.cs` |

**验收步骤（Play）：** 清档 → Play → 多推进几季看结算文案 → Debug Force Typhoon 看现金/准点 → Force Chain 再推进看连锁 → 运营→事件日志 → Force Fuel Spike 后同季不应再叠 TryOil 大油价 → F5/F9。  
**验收结果：** ✅ 已验收（2026-08-04 Play）：Console 无红字；事件文案可滚动、不挡按钮；结算弹窗分区分色；油价互斥正确；F5/F9 恢复。

---

### 2.21 Phase 13 · 宏观经济引擎完善 ✅ 已验收（2026-08-04）
> 对应 GDD 第 9.1 节。

| 子项 | 说明 | 文件 |
| :--- | :--- | :--- |
| 13.1 周期 | 三阶段：扩张 / 滞胀或放缓 / 衰退；每年更新 GDP 增速 | `MacroEconomyManager.cs` |
| 13.2 传导 | 周期 → 需求（客座/收入系数）→ 油价倾向 → 竞对进攻欲 → 贷款/事件权重 | 同 Manager |
| 13.3 UI | 财务面板「宏观经济」页签 + 顶部经济指示灯 | `FinancePanelUI.cs` · `MacroEconomyUI.cs` · `MVPBootstrap.cs` |
| 13.4 季度 | Q1 结算弹窗显示《宏观经济展望》报告 | `TimeManager.cs` · `MVPBootstrap.cs` |
| 13.5 存档 | `ACEO_MacroEconomy_GDP/Phase/Duration` | `SaveManager.cs` · `ClearGameSaveMenu.cs` |
| 13.6 调试 | Force Boom / Recession / Recovery | `Phase13DebugMenu.cs` |

**验收结果：** ✅ 已验收（2026-08-04 Play）。

---

### 2.22 Phase 14 · UI/UX 打磨与美术精修 ✅ 已验收（2026-08-04）

> 对应 GDD 第 10、11 章。

| 子项 | 说明 | 文件 |
| :--- | :--- | :--- |
| 14.1 术语 Tooltip | 悬停 0.5 秒弹出白话解释；支持现金跑道、RASK、客座率等 10 条术语 | `TooltipManager.cs` · `TermTooltip.cs` · `Data/TermData.cs` |
| 14.2 弹窗动画 | 季度结算 + 年度投票弹窗淡入+上滑动效；页签 Crossfade 过渡 | `MVPBootstrap.cs` · `VoteUI.cs` |
| 14.3 机型图标 | `AircraftIcon` 程序化生成 A320（绿）/ B737（蓝）/ B787（橙）三种彩色图标 | `AircraftIcon.cs` · `FleetUI.cs` |
| 14.4 弃用 API 修复 | 全项目 `enableWordWrapping` → `textWrappingMode`（共 9 处，消除黄色警告） | `CashMeter.cs` · `HedgeTutorialUI.cs` · `FinancePanelUI.cs` · `TooltipManager.cs` · `UIFactory.cs` · `MVPBootstrap.cs` |
| 14.5 化学反应 | `ChemistryMatcher` 仅后台运作，不向玩家展示（玩家自行探索规律） | `ChemistryMatcher.cs`（已有） |

**验收步骤（Play）：** 清档 → Play → 光标悬停 CashMeter 绿条 → tooltip 弹出「现金跑道」解释 → 推进一季，结算弹窗有淡入动效 → 市场页签切换有过渡 → 机队列表三种机型图标颜色不同 → Console 无黄色警告。  
**验收结果：** ✅ 已验收（2026-08-04 Play）。

---

### 2.23 Phase 15 · 跨部门联动测试与优化 ✅ 已验收（2026-08-04）

> 对应 GDD 第 8 章。清单式全链路：开通航线 → 机队运力 → 竞对压力 → 对冲/融资 → 董事会目标 → 安全事件。修数值平衡与文案矛盾。  
> 详细修复记录：[`Phase15_FixLog.md`](Phase15_FixLog.md)

| 子项 | 说明 | 文件 |
| :--- | :--- | :--- |
| 15.1 对冲时序 | `SettleQuarter` / `PushPriceHistory` 移到油价波动**之前**，按季度末价结算 | `TimeManager.cs` |
| 15.2 对冲现金保护 | 结算后 `cash < 0` 归零并打日志 | `HedgeManager.cs` |
| 15.3 品牌侵蚀传导 | RPM 三处改读 `GameState.brandAwareness`（fallback 55） | `MVPBootstrap` · `GameDataSnapshot` · `NeuralCommandMatcher` |
| 15.4 AOG 现金保护 | 现金不足 CostFerry 时自动转延误维修，不重复扣款 | `AOGManager.cs` |
| 15.5 遣散费保护 | 辞退现金不足时按剩余归零 | `DepartmentManager.cs` |
| 15.6 随机事件现金 | 负 cash 效果后归零 | `RandomEventManager.cs` |
| 15.7 跑道警告 | 移除 `cash >= 0` 条件，危急时仍警告 | `TimeManager.cs` |
| 15.8 扩张前置 | 接受扩张前检查 `HasSpareAircraftForNewRoute` | `ExpansionProposalManager.cs` |

**数值平衡摘要：** 初始现金 500 万 / 月固定 80 万 / 开航 300 万 / 续聘线 60% / 对冲溢价 3–8% / 品牌侵蚀 1–6 点/季 —— 均合理。  

**已知未修限制（刻意保留）：** 航线 `dailyProfit` 开航时一次性计算（宏观不自动重算）；董事会满意度对任免仅为展示；机队全退役时 seats=180 fallback。  

**验收结果：** ✅ 已验收（2026-08-04）：8 项联动 Bug 修复 + 数值平衡验证 + 文案确认通过。

---

### 2.24 发布准备 ⏸️ 待定

Steam 商店页文案与截图清单、宣传素材目录、内测/公测测试计划。  

**排期说明：** ⏸️ **暂不列入近期计划**。当前优先完善 **MVP 1.0**，并准备开发 **V2.0**；待 MVP 足够完善后再考虑启动发布准备。

---

## 第 3 章：详细功能对照（GDD 章节 ↔ 开发状态）

> 对应路线图 **「三」**。

| GDD 功能点 | 实现方式 | 对应 Phase | 状态 | 任务书节 |
| :--- | :--- | :---: | :---: | :---: |
| 首航～AI 执行建议等 | 见 v1.5 | 核心～Phase 1 | ✅ | v1.5 |
| 季度推进 | TimeManager | Phase 2 | ✅ | v1.5 2.6 |
| 董事会 / 续聘 | Board* / VoteUI | Phase 5～6 | ✅ | v1.5 |
| 安全与航权 | Safety* / RouteRight* | Phase 7 | ✅ | v1.5 2.11 |
| AOG / 扩张 | AOG* / Expansion* | — | ✅ | v1.5 2.12～2.13 |
| 部门任命 | Department* / HRUI | Phase 8 | ✅ | v1.5 2.14 |
| 对冲 / 融资 / 引导 | Hedge* / Loan* / Tutorial | Phase 9 | ✅ | v1.5 2.15 |
| 机队管理 | Fleet* | Phase 10 | ✅ | v1.5 2.16 |
| **AI 竞争对手（第9.2节）** | **AIManager + Competition*** | **Phase 11** | **✅ 2026-08-03** | **2.17** |
| 随机事件引擎（第9.3节） | RandomEventManager + Catalog + EventLogUI | Phase 12 | ✅ 2026-08-04 | 2.20 |
| 宏观经济（第9.1节） | MacroEconomyManager | Phase 13 | ✅ 2026-08-04 | 2.21 |
| UI/UX（第10章） | Tooltip / 动画 / 机型图标 / API修复 | Phase 14 | ✅ 2026-08-04 | 2.22 |
| **跨部门联动（第8章）** | **8 项联动修复 + 数值平衡** | **Phase 15** | **✅ 2026-08-04** | **2.23** |
| 发布准备（第12章） | Steam / 测试计划 | 发布准备 | ⏸️ 待定 | 2.24 |

---

## 第 4 章：里程碑概览（按玩家体验时间线）

> 对应路线图 **「四」**。

- **0～15分钟**：首航 + 现金跑道 + RPM ✅  
- **15～30分钟**：AOG ✅（2026-08-01）  
- **30～60分钟**：董事会 + 年度结算 ✅  
- **60～120分钟**：安全 + 航权 + 扩张 ✅（2026-08-01）  
- **120分钟以后**：人事 ✅；对冲融资 + 引导 ✅；机队 ✅；AI 竞对 ✅（2026-08-03）；随机事件 ✅（2026-08-04）  
- **MVP 后期**：宏观 ✅（2026-08-04）；UI/UX ✅（2026-08-04）；跨部门联动 ✅（2026-08-04）  
- **后续**：完善 MVP 1.0 → 开发 V2.0 🔜；发布准备 ⏸️ 待定

---

## 附录 A：玩家文案规范

| 场景 | 推荐说法 | 避免 |
| :--- | :--- | :--- |
| 现金可撑多久 | 现金跑道：按现在开支，流动性还可以撑 X 个月 | 只说「跑道 6.3 月」 |
| 资金状态 | 目前还算稳妥 / 有点紧 / 偏危险 | 只甩绿黄红字母给新手 |
| 航线评价 | 比较划算 / 先观望 / 暂缓 + **综合评定 XX 分** | `RPM70/绿/日利62.8` |
| 竞争 | 对手航班已经很多；份额条用「你 / 航司名 + %」 | 竞对运力压迫系数、ASK 裸术语无解释 |
| 随机事件 | 白话标题 + 一句后果 | 内部 id、系数堆砌 |

---

## 附录 B：给 Cursor 的续作提示词模板

```text
请先阅读 docs/Development Manual/Airline_CEO_Dev_Task_v1.7.md 与
docs/GameDesignBible/MVP/V1.0/Airline_CEO_Dev_Roadmap_v1.8.md。
当前 Phase 1～15 已验收；发布准备（§2.24）⏸️ 待定，不要开工。
下一方向：完善 MVP 1.0，并按用户指定范围准备 / 实施 V2.0。
工程根目录是 Unity Project/。完成后同步更新路线图与本任务书状态。
```

---

## 附录 C：Unity 菜单速查

- `Run Logic Self Tests`
- `Batch Create Data Assets`
- `Setup MVP Scene Objects`
- `Create DeepSeek Config Asset` / `Set DeepSeek API Key`
- `Create UI Prefabs (Phase 3)` / `Generate Chinese TMP Font Asset`
- `清档 Clear Save`
- `Debug/Force Mild|Moderate Safety Event`
- `Debug/Force AOG Crisis`
- `Debug/Force Expansion Proposal`
- `Debug/HR Force Resign OPS` / `Debug/HR Run Year-End Loyalty`
- `Debug/Force Cash Crisis` / `Debug/Force Fuel Spike +20%`
- `Debug/Grant Fleet Cash +30000` / `Add Random Aircraft` / `Force Fleet Age +1 Year`
- `Debug/Force AI Expansion` / `Force AI Dying` / `Kill AI Competitor` / `Propose Code Share Now`
- `Debug/Force Random Event (Typhoon|Fuel Spike|Demand Surge)` / `Force Chain (Typhoon Root)` / `List Random Event Catalog`
- `Debug/Force Boom` / `Force Recession` / `Force Recovery`
- `Debug/移除北京-伦敦航线（复测航权）`

---

**最后建议：**  
以 **v1.7** 为现行施工图；对照路线图 **v1.8**。Phase 1～15 均已验收；下一方向 **完善 MVP 1.0 → V2.0**；**发布准备（§2.24）⏸️ 待定**。遇报错把 Console 红字发给 Cursor / Kimi。

本地路径：`docs/Development Manual/Airline_CEO_Dev_Task_v1.7.md`
