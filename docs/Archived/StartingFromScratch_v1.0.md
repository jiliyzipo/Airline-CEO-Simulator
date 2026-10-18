# 从零创建航司 · 开局方案归档（v1.0）

归档日期：2026-09-14  
状态：**已备份，仍可作为「从零创建」入口进入**。默认新游戏改为 Football Manager 式「接管现有航司」。

---

## 原开局流程

```
主菜单 → 玩家资料 → 公司注册 → 董事会抽卡选座 → 年度目标确认 → 进入主页 → 顾问深圳—北京引导
```

| 步骤 | 实现 | 说明 |
| :--- | :--- | :--- |
| 玩家资料 | `SetupFlowController.ShowPlayerProfile` | 姓名、性别、出生年、国籍 |
| 公司注册 | `ShowCompanyRegister` | 中英文名、IATA/ICAO、总部、启动资金档 |
| 董事会 | `ShowBoardDraft` | 从董事库抽 12 人，选出 8 席 |
| 年度目标 | `ShowAnnualGoals` | `BoardManager.EnsureAnnualTargets` 后确认 |
| 进入经营 | `EnterGameplay` | 写 `GameState`、清空过渡业务、启动顾问教程 |

## 开局经济与空窗

- 默认现金由资金档决定（3,000～15,000 万美元），难度系数另乘。
- 机队为空或仅后续购买；航线需走许可/航权/排班邮件。
- 过渡业务：`TransitionBusinessLogic.Reset` + `Bootstrap` 投递《开航前闲置运力过渡经营的请示》。
- 顾问：`AdvisorModalUI` S0–S6（深圳—北京首航）。

## 相关代码

- `Unity Project/Assets/Scripts/UI/Setup/SetupFlowController.cs`
- `Unity Project/Assets/Scripts/UI/Setup/SetupDraft.cs`
- `Unity Project/Assets/Scripts/UI/Setup/SetupCatalogs.cs`
- `Unity Project/Assets/Scripts/Logic/TransitionBusinessLogic.cs`
- `Unity Project/Assets/Scripts/UI/Shell/AdvisorModalUI.cs`

## 如何再走从零创建

主菜单点「从零创建」，仍走本方案全流程。
