# Prefab UI 组件库规范（Phase 0.1）

冻结日期：2026-08-11  
修订：2026-08-12（对齐 L0–L5 全量交互；补齐 `Widget_Badge`）  
适用范围：`Unity Project/Assets/Prefabs/UI/` + `Assets/Scripts/UI/Framework/`

## 目录约定

```
Assets/Prefabs/UI/
  Panels/      ← 全屏/半屏业务面板（按 pageId 命名，层级见 UIConfig.level）
  Modals/      ← 模态/叠层窗（决策、危机、确认等）
  Widgets/     ← 可复用原子控件
  Templates/   ← 列表项等可实例化模板
```

**层级归属不由目录决定**：白板 L0–L5 一律用 `UIConfig.level` + `pageId` 表达。  
约定：常规业务屏 → `Panels/`；需遮罩/打断操作的叠层 → `Modals/`（含多数 L4/L5）。

所有新 UI Prefab **必须**放在上述目录；禁止散落在 `Resources/` 或场景内临时物体当最终形态。

## 面板继承层级

```
BasePanel
  ├─ ScrollListPanel      # 带标题 + ScrollRect 列表
  └─ DecisionModal        # 模态遮罩 + 确认/取消（继承 BasePanel）
```

| 类型 | 脚本 | Prefab 命名 | 职责 |
|---|---|---|---|
| BasePanel | `AirlineCEO.UI.Framework.BasePanel` | `Panel_*.prefab` | 背景、标题、关闭、Show/Hide |
| ScrollListPanel | `…ScrollListPanel` | `Panel_ScrollList.prefab` / `Panel_*.prefab` | 内容区 VerticalLayout + 列表刷新 |
| DecisionModal | `…DecisionModal` | `Modal_*.prefab` | 半透明遮罩、主/次按钮、结果回调 |

## 原子组件（Widgets）

| 组件 | 脚本 | Prefab |
|---|---|---|
| StatRow | `StatRowView` | `Widget_StatRow.prefab` |
| ProgressBarWithLabel | `ProgressBarView` | `Widget_ProgressBar.prefab` |
| BadgeIcon | `BadgeIconView` | `Widget_Badge.prefab` |
| ClickableListItem | `ClickableListItemView` | `Template_ListItem.prefab`（放 Templates/） |
| TabGroup | `TabGroupView` | `Widget_TabGroup.prefab` |

## 与 UIConfig / NavigationRouter 的关系

1. 每个白板页面有一份 `UIConfig` ScriptableObject（`pageId` 主键）。
2. `UIConfig.prefab`（可选）指向 `Panels/` 或 `Modals/` 下的 Prefab。
3. `NavigationRouter.Navigate(pageId)`：查表 → 实例化 Prefab → 压栈；`Back()` 弹栈；叠层 × 清栈约定见白板 n0。
4. Framework Prefab 由菜单生成/刷新：`AirlineCEO → Phase 1 → Create Framework & L1 Prefabs`（含 `Widget_Badge`）。

### UIConfig 单一权威来源（2026-08-17 冻结）

- **运行时唯一加载路径**：`NavigationRouter` / `PrefabNavIntegration` 通过 `Resources.LoadAll<UIConfig>(UIPrefabCatalog.ResourcesConfigFolder)` 读取，即 **`Assets/Resources/UIConfig/`**。
- **`Assets/ScriptableObjects/UIConfig/` 已弃用并删除**（历史可编辑副本，曾与 Resources 副本互补漂移）。新页 **只落一份** 到 `Assets/Resources/UIConfig/`，禁止再双写。
- 生成菜单统一走 `Resources/UIConfig`：`AirlineCEO → Phase 1 → Bind UIConfig Prefabs (Resources)`、`AirlineCEO → Phase 2 → Create OPS Prefabs & UIConfig`。

## 货币与数值单位约定（2026-08-17 冻结）

- **货币唯一单位 = 「万美元」**（`Wan USD`）。底层 `GameState.cash`、`EconomyBalanceParams`、所有 `*Wan` 字段均为此单位；油价/碳价等费率为「美元」计价（美元/吨、美元/RPK）。
- UI 紧凑文案允许简写「万」，但**顶栏现金、开局资金档、年度结算摘要等关键可见位置必须显式「万美元」**，杜绝「万」裸写的币种歧义。
- 单价/费率类（座位费、GDS 航段费、辅助收入）单位为「美元」，不得与「万美元」混用。

## 编码约定

- 命名空间：`AirlineCEO.UI.Framework`（组件）、`AirlineCEO.UI.Navigation`（路由）
- 禁止在 Framework 脚本里直接 `new GameObject` 拼完整业务页（那是旧 UIFactory 路径）；Framework 只绑定序列化引用。
- UIFactory 作为兼容层保留：无 Prefab 时回退程序化生成；有 Prefab 时优先实例化。

## 验收（Phase 0）

- [x] 四子目录存在
- [x] BasePanel / ScrollListPanel / DecisionModal / 五个 Widget 脚本存在
- [x] `Widget_Badge.prefab` 落盘（与其余 Widget 一并可由 Phase 1 菜单重生）

## 验收（Phase 1）

- [x] 生成菜单：`AirlineCEO → Phase 1 → Create Framework & L1 Prefabs`
- [x] Framework / Shell / L1 Prefab 已落盘（含 Badge）
- [x] Shell `Panel_Shell_n0` + 七部 L1（模板=`n13_运营部`）
- [x] `NavigationRouter` + `PrefabNavIntegration`
