# ✈️ Airline CEO Simulator · 开发路线图与完成清单 v1.4

> ⚠️ **历史存档。** 现行请改用 **[`Airline_CEO_Dev_Roadmap_v1.5.md`](./Airline_CEO_Dev_Roadmap_v1.5.md)**。

**基于 MVP GDD v1.3 与技术任务书 v1.4**  
更新日期：2026-08-01（AOG + 战略性扩张提案 Play 验收打勾）

---

## 一、MVP 开发总览（按 GDD 核心循环）

| 玩家体验节点 | 对应 GDD 章节 | 核心功能 | 完成状态 |
| :--- | :--- | :--- | :---: |
| 0～15分钟（首航体验） | 第3章（首航任务）<br>第7.1～7.4（OPS/MKT/FIN） | 首航任务、现金跑道、基础定价、RPM评估 | ✅ 完成 |
| 15～30分钟（危机应对） | 第7.2（AOG危机） | 脚本化 AOG 危机（故障处置） | ✅ 已验收 |
| 30～60分钟（首次考核） | 第5章（董事会目标）<br>第4章（年度结算） | 董事会目标生成、进度追踪、年度结算与续聘投票 | ✅ 完成（Phase 5～6） |
| 60～120分钟（战略纵深） | 第7.6（SAF）<br>第7.7（STG） | 安全事件、航权审批、扩张提案 | ✅ 已验收（Phase 7 + 扩张提案） |

---

## 二、技术实现阶段清单（Dev Task v1.4）

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
| **AOG 危机** | **脚本化 AOG（GDD 7.2）** | **AOGManager, AOGUI** | **✅ 已验收** |
| **扩张提案** | **战略性扩张（GDD 7.7）** | **ExpansionProposalManager, ExpansionProposalUI** | **✅ 已验收** |
| Phase 8+ | 化学反应可视化 / 融资机队 / 美术精修 | 待定 | ⬜ |

---

## 三、详细功能对照（GDD 章节 ↔ 开发状态）

| GDD 功能点 | 实现方式 | 对应 Phase | 状态 |
| :--- | :--- | :---: | :---: |
| 首航任务（第3.5节） | 一键确认 + 动画 + 收益结算 | 核心 UI | ✅ |
| 现金跑道仪表盘（第7.4节） | 油量表 + 白话说明 | Phase 3 | ✅ |
| RPM 航线盈利评估（第7.7节） | RPMCalculator + 三色灯 | 逻辑层 | ✅ |
| AI 智能指挥中枢（第13章） | DeepSeek + 本地话术 | AI 接口 | ✅ |
| AI 执行建议（第13.3节） | ActionExecutor | Phase 1 | ✅ |
| 季度推进（第2章核心循环） | TimeManager | Phase 2 | ✅ |
| 董事会目标生成（第5.4节） | BoardManager + TargetGenerator | Phase 5 | ✅ |
| 年度结算与续聘（第4章） | YearEndEvaluator + VoteUI | Phase 6 | ✅ |
| 安全评级与事件（第7.6节） | SafetyEventManager + SafetyUI | Phase 7 | ✅ |
| 航权审批（第7.7节） | RouteRightManager | Phase 7 | ✅ |
| 脚本化 AOG 危机（第7.2节） | AOGManager + AOGUI | AOG | ✅ 已验收 |
| 战略性扩张提案（第7.7节） | ExpansionProposalManager + UI | 扩张提案 | ✅ 已验收 |
| 部门管理 / 任命（第6章） | （规划中） | Phase 8+ | ⬜ |
| 燃油对冲 / 融资（第7.4节） | （规划中） | Phase 8+ | ⬜ |

---

## 四、里程碑概览（按玩家体验时间线）

- **0～15分钟**：首航 + 现金跑道 + RPM 查询 ✅
- **15～30分钟**：AOG 危机 ✅（2026-08-01 Play 验收）
- **30～60分钟**：董事会目标 + 年度结算 ✅
- **60～120分钟**：安全事件 + 航权审批 + 扩张提案 ✅（2026-08-01 Play 验收：暂缓→次年再弹→拒绝）
- **120分钟以后**：部门管理、融资、化学反应等（Phase 8+） ⬜

---

**图例：** ✅ 已完成并验收 ｜ ⬜ 待开发

生成日期：2026-07-31 · 归档更新：2026-08-01  
现行路线图：`Airline_CEO_Dev_Roadmap_v1.5.md`
