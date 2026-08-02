# ✈️ Airline CEO Simulator · 开发路线图与完成清单 v1.5

**基于 MVP GDD v1.3 与技术任务书 v1.5**  
更新日期：2026-08-02  
**现行版本：** 请以 **v1.5** 为准（v1.4 及更早为历史存档）

---

## 一、MVP 开发总览（按 GDD 核心循环）

| 玩家体验节点 | 对应 GDD 章节 | 核心功能 | 完成状态 |
| :--- | :--- | :--- | :---: |
| 0～15分钟（首航体验） | 第3章（首航任务）<br>第7.1～7.4（OPS/MKT/FIN） | 首航任务、现金跑道、基础定价、RPM评估 | ✅ 完成 |
| 15～30分钟（危机应对） | 第7.2（AOG危机） | 脚本化 AOG 危机（故障处置） | ✅ 已验收 |
| 30～60分钟（首次考核） | 第5章（董事会目标）<br>第4章（年度结算） | 董事会目标生成、进度追踪、年度结算与续聘投票 | ✅ 完成（Phase 5～6） |
| 60～120分钟（战略纵深） | 第7.6（SAF）<br>第7.7（STG） | 安全事件、航权审批、扩张提案 | ✅ 已验收（Phase 7 + 扩张提案） |
| 120分钟以后（人事 / 财务） | 第6章 + 第7.4节 | 部门任命；燃油对冲与紧急融资；财务顾问新手引导 | ✅ 已验收（Phase 8～9） |

---

## 二、技术实现阶段清单（Dev Task v1.5）

| 阶段 | 核心产出 | 对应文件 / 系统 | 状态 |
| :--- | :--- | :--- | :---: |
| 数据层 | 数据模型 + DataBase | AircraftData, ExecutiveData, BoardMemberData, DataBase.cs | ✅ |
| 逻辑层 | RPM / CashRunnel / Chemistry | RPMCalculator, CashRunnelCalculator, ChemistryMatcher | ✅ |
| 核心 UI | 首航 + 流动性显示 | MVPBootstrap, 基础 HUD | ✅ |
| AI 接口 | DeepSeek + 本地容灾 | AIChatManager, NeuralCommandMatcher | ✅ |
| Phase 1 | AI 执行建议（ActionExecutor） | ActionExecutor, RouteData, 执行按钮 | ✅ |
| Phase 2 | 财年心跳（季度结算） | TimeManager, 季度推进按钮 | ✅ |
| Phase 3 | 航空驾驶舱 UI | UITheme, CashMeter, 深色 HUD | ✅ |
| Phase 4 | 中文字体 (TMP) | ChineseFontProvider, NotoSansSC | ✅ |
| Phase 5 | 董事会目标生成器 | BoardManager, TargetGenerator, BoardTargetUI | ✅ |
| Phase 6 | 年度结算与续聘投票 | YearEndEvaluator, VoteUI, 见习保护 | ✅ |
| Phase 7 | 安全事件与航权系统 | SafetyEventManager, RouteRightManager, SafetyUI | ✅ |
| AOG 危机 | 脚本化 AOG（GDD 7.2） | AOGManager, AOGUI | ✅ 已验收 |
| 扩张提案 | 战略性扩张（GDD 7.7） | ExpansionProposalManager, ExpansionProposalUI | ✅ 已验收 |
| Phase 8 | 部门管理 / 任命（GDD 第6章） | DepartmentManager, HRUI, ACEO_DeptHeads | ✅ 已验收 |
| Phase 9 | 燃油对冲与融资（GDD 7.4）+ 锁价方式 | HedgeManager, LoanManager, FinancePanelUI | ✅ 已验收 |
| Phase 9 UX | 财务顾问引导 / 对冲新手体验 | HedgeTutorialUI、三套预设、ACEO_HasSeenHedgeTutorial | ✅ 已验收 |
| Phase 8+ | 机队 / 美术精修 等 | 待定 | ⬜ |

---

## 三、详细功能对照（GDD 章节 ↔ 开发状态）

| GDD 功能点 | 实现方式 | 对应 Phase | 状态 |
| :--- | :--- | :---: | :---: |
| 首航任务（第3.5节） | 一键确认 + 动画 + 收益结算 | 核心 UI | ✅ |
| 现金跑道仪表盘（第7.4节） | 油量表 + 白话说明 | Phase 3 | ✅ |
| RPM 航线盈利评估（第7.7节） | RPMCalculator + 三色灯 | 逻辑层 | ✅ |
| AI 智能指挥中枢（第13章） | DeepSeek + 本地话术；圆形可拖 AI 图标展开/收拢 | AI 接口 | ✅ |
| AI 执行建议（第13.3节） | ActionExecutor | Phase 1 | ✅ |
| 季度推进（第2章核心循环） | TimeManager | Phase 2 | ✅ |
| 董事会目标生成（第5.4节） | BoardManager + TargetGenerator | Phase 5 | ✅ |
| 年度结算与续聘（第4章） | YearEndEvaluator + VoteUI | Phase 6 | ✅ |
| 安全评级与事件（第7.6节） | SafetyEventManager + SafetyUI | Phase 7 | ✅ |
| 航权审批（第7.7节） | RouteRightManager | Phase 7 | ✅ |
| 脚本化 AOG 危机（第7.2节） | AOGManager + AOGUI | AOG | ✅ 已验收 |
| 战略性扩张提案（第7.7节） | ExpansionProposalManager + UI | 扩张提案 | ✅ 已验收 |
| 部门管理 / 任命（第6章） | DepartmentManager + HRUI | Phase 8 | ✅ 已验收 |
| 燃油对冲 / 融资（第7.4节） | 对冲（固定/区间/期权）+ 紧急融资 | Phase 9 | ✅ 已验收 |
| 财务顾问引导 / 对冲新手体验 | 首次进财务弹窗 + 三套预设 +「问问财务顾问」 | Phase 9 UX | ✅ 已验收 |

---

## 四、里程碑概览（按玩家体验时间线）

- **0～15分钟**：首航 + 现金跑道 + RPM 查询 ✅
- **15～30分钟**：AOG 危机 ✅（2026-08-01 Play 验收）
- **30～60分钟**：董事会目标 + 年度结算 ✅
- **60～120分钟**：安全事件 + 航权审批 + 扩张提案 ✅（2026-08-01：Y4Q1 暂缓 → Y5Q1 再弹 → 拒绝）
- **120分钟以后**：部门管理 / 任命 ✅（Phase 8）；燃油对冲与融资（含锁价方式 + **财务顾问新手引导**）✅ 已验收（Phase 9 / 9 UX · 2026-08-02）；其余 ⬜

---

## 五、v1.5 相对 v1.4 的变更摘要

| 项 | 说明 |
| :--- | :--- |
| AOG | Play 验收打勾（触发 / 处置 / 不再重复） |
| 扩张提案 | Play 验收打勾（暂缓跨年再弹、拒绝终结） |
| Phase 8 部门任命 | Play 验收打勾 |
| Phase 9 对冲融资 | Play 验收打勾（基础融资 + 锁价方式：固定/区间/期权 · 2026-08-02） |
| Phase 9 UX 新手引导 | Play 验收打勾（财务顾问弹窗 + 三套预设 + 存档不重复 · 2026-08-02） |
| 下一优先 | Phase 8+：机队或美术精修 |

---

**图例：** ✅ 已完成并验收 ｜ ⬜ 待开发

生成日期：2026-08-02  
对照任务书：`docs/Development Manual/Airline_CEO_Dev_Task_v1.5.md`
