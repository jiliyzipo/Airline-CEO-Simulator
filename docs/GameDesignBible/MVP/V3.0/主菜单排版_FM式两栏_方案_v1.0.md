# 主菜单排版 · FM 式两栏方案 v1.0

> 状态：**已落地（简易版 · 不含主视觉美术）**
> 日期：2026-10-10
> 定位：把 `n1_主菜单` 从「大标题 + 一段说明 + 底部一排横按钮」改成 Football Manager（下文 FM）式**两栏版面**：左边一整块主视觉、右边一条纵向菜单列。本次只为把入口层级与位置关系定下来，主视觉留空槽，后期再图像化。
> 关联：`UI/Setup/MainMenuUI.cs`（新增）· `UI/Setup/SetupFlowController.cs` · `Systems/SaveManager.cs` · `Tests/PlayMode/SetupFlowPlayModeTests.cs`
> 结论摘要：**入口本身有主次之分，而旧版式把 6 件事塞进同一行 15% 宽的格子里 —— 换版式的收益不在"好看"，在「玩家一眼看出先做哪件」。**

## 1. 旧版式的三处硬伤

| # | 现象 | 后果 |
|---|---|---|
| 1 | 6 个入口横排一行，每格仅占屏宽约 15% | 文案稍长即截断；且**主次被抹平**——「新游戏」和「退出」同宽同高 |
| 2 | 中间 `Form` 区在主菜单无事可填，留一大块空底 | 首屏看起来像"没做完"，也是玩家截图里最扎眼的那块 |
| 3 | 无存档时「继续」仍可点，点进去才被拒绝 | 点进一个必然失败的入口，说明文字还得靠一段正文去补救 |

## 2. FM 的解法（参照物）

FM 开始界面（截图：足球经理 2024）把**动作全部收在右侧一条竖列**里，左侧整块留给主视觉，列内自上而下分层：

```
logo/游戏名  →  最近保存卡片（一行信息）  →  纵向主按钮（开始新游戏 / 载入存档 / 加入联机）
             →  次级小字链接（偏好选择 · 用户手册 · 更多）  →  页脚（版本号 + 小号「退出」）
```

三条可复用的规律：

1. **主入口纵向排**：整行、等宽、字号一致，一次只读一件事；
2. **utility 降级**：设置类入口退到次级小字行，不与玩法入口同居一层；
3. **页脚固定**：版本号与「退出」钉在列底部，不随上方内容多少漂移。

## 3. 本作版面规格（简易版）

```
┌───────────────────────────────┬──────────────────────────────┐
│                               │ Airline CEO Simulator    ←品牌 │
│   ┌───────────────────────┐   │ ▬▬▬▬▬                  ←强调横条 │
│   │                       │   │ 接管一家航司 · 当十年 CEO       │
│   │   AIRLINE             │   │ ┌──────────────────────────┐ │
│   │   CEO SIMULATOR       │   │ │[存] 北辰快线              │ │ ←最近保存
│   │      （淡水印）        │   │ │ 财年 2 · Q3 · 现金 … · 董事 │ │
│   │                       │   │ └──────────────────────────┘ │
│   │  HeroSlot 主视觉占位    │   │ ┌──────────────────────────┐ │
│   │  后期替换为航司涂装      │   │ │      新游戏 · 接管        │ │ ←主入口
│   │  / 驾驶舱插画           │   │ ├──────────────────────────┤ │
│   └───────────────────────┘   │ │        从零创建           │ │
│                               │ ├──────────────────────────┤ │
│   （左：3%~55% 屏宽）           │ │      继续 / 读档（有档亮） │ │
│                               │ ├──────────────────────────┤ │
│                               │ │        历史回放           │ │
│                               │ └──────────────────────────┘ │
│                               │ [设置 / 存档]  F5 存档·F9 读档 │ ←次级行
│                               │ 提示行（随点击回写）           │
│                               │ MVP v1.0              [ 退出 ] │ ←页脚
└───────────────────────────────┴──────────────────────────────┘
```

| 区域 | 锚点（占屏比） | 内容 |
|---|---|---|
| `HeroSlot` 主视觉槽 | x 3%~55% · y 10%~90% | 淡水印 + 一行占位说明；**无任何交互**（美术接入点） |
| `Column` 菜单列 | x 58%~97% · y 8%~92% | 下列全部内容，宽度自适应 |
| `Brand` 品牌块 | 列内 y 0~64px | 游戏名 22pt 加粗 + 30% 宽强调横条 + 一句说明 |
| `SaveCard` 最近保存 | y +16px · 高 62px | 图标方块（存/新）+ 主行 + 次行 |
| `Entry_*` 主入口 ×4 | 每格高 44px · 间隔 7px | 新游戏·接管 / 从零创建 / 继续·读档 / 历史回放 |
| `LinkBar` 次级行 | 高 30px | 「设置 / 存档」按钮（占左 46%）+ 右侧快捷键提示 |
| `Hint` 提示行 | 高 46px | 随点击回写（读档组件未就绪等） |
| `VersionTag` / `Quit` | 钉列底部 | 版本号（左）+ 小号「退出」（右，占 62%~100%） |

## 4. 信息分层：谁进主列，谁降级

| 入口 | 位置 | 理由 |
|---|---|---|
| 新游戏 · 接管 | 主列第 1 | 唯一"从零开始一局"的正路，用强调色 `Accent` |
| 从零创建 | 主列第 2 | 已归档的白手起家方案，保留但不引导 |
| 继续 / 读档 | 主列第 3 | 有档时用成功色；**无档时 `interactable = false` 置灰**，不再让玩家点进必然失败的入口 |
| 历史回放 | 主列第 4 | 时间胶囊 / What-If 分支是玩法入口，不是 utility |
| 设置 / 存档 | 次级行 | utility，降级为小字按钮，不与玩法入口抢视觉 |
| 退出 | 页脚 | FM 同款：小号、右侧、独立于入口列 |

## 5. 「最近保存」卡片的数据口径

新增 `SaveManager.TryPeekSummary(out 公司名, out 财年, out 季度, out 现金, out 董事会满意度, out 是否终局)`：

- **只读 PlayerPrefs，不 `Load()`**：主菜单只展示摘要，不建 `GameState`、不触发任何读档副作用；
- 与 `HasSave()` **同一判据**（`KeyCash` 存在），但走静态路径，因此不依赖 `SaveManager.Instance` 是否已建（主菜单早于玩法系统初始化，旧代码的 `Instance != null` 判断在此处其实一直是 false）；
- 正文（公司名 / 财年 / 现金 / 满意度）由主菜单拼装，`MainMenuUI` 只收 `SaveCardInfo` 结构体，**不碰存档 API** —— 于是版面测试可以确定性地跑，不受本机存档影响。

## 6. 图像化接口（美术接入点）

后期替换主视觉时**不需要动右侧菜单列的任何几何**：

1. `HeroSlot`（锚点 3%~55% × 10%~90%，约屏宽 52% × 屏高 80%）就是主视觉画布，建议出图按该比例（1080p 下约 1000×864，2× 图按 2000×1728 出）；
2. 删除 `HeroSlot` 下的 `Watermark`（淡水印）与 `SlotNote`（占位说明）两个 Label，挂一张 `Image` 即可；若要保留标题锁字（key art 叠字），把 `Watermark` 换成 `Image` 之外的正式字标；
3. 若要做图形 logo，位置是 `Column/Brand`（列内 0~64px），现为纯文字 "Airline CEO Simulator"；
4. 主视觉**不要**覆盖 `x > 55%`：与菜单列是硬性两栏，`MainMenu_Layout_TwoColumnsAndVerticalEntries` 有断言（`hero.anchorMax.x < column.anchorMin.x`）。

## 7. 自测与验证

PlayMode 新增 `SetupFlowPlayModeTests.MainMenu_Layout_TwoColumnsAndVerticalEntries`，逐条钉住本次改动：

| 断言 | 对应症状 |
|---|---|
| `HeroSlot` 存在且与 `Column` 不重叠 | 两栏版面成立（图像化替换点在场） |
| 每个入口 `anchorMin.x = 0` / `anchorMax.x = 1` 且 y 依次递减 | **必须是纵向整行**，挡住退回横排一行 |
| 入口按 `Entry_*` 命名可寻址 | 排查/后续测试不靠位置猜 |
| `Interactable = false` 的条目 `Button.interactable` 为假 | 无存档时「继续 / 读档」置灰 |
| 次级行 `Settings`、页脚 `VersionTag` / `Quit`、`SaveCard` 均在 | 分层没有被后续改动悄悄拆掉 |
| 点击入口仅触发回调；`SetHint` 可回写 | 版面与玩法解耦 |

另：`node tools/semcheck/run.js` → `errors=0` + `tests-asm: errors=0` + `enc: errors=0`（含中文编码基线）。

### 7.1 一个必须记住的编译边界（CS0012）

本轮的版面容器最初把提示行写成 `public TextMeshProUGUI Hint`，于是测试里那句
`view.Hint.text` 在 Unity 里直接编译不过：

```
Assets\Tests\PlayMode\SetupFlowPlayModeTests.cs(241,42): error CS0012:
The type 'TextMeshProUGUI' is defined in an assembly that is not referenced.
You must add a reference to assembly 'Unity.TextMeshPro'.
```

原因不在代码本身，而在**程序集边界**：`AirlineCEO.PlayModeTests.asmdef` 只引用
`AirlineCEO.Runtime` + `UnityEngine.TestRunner`，拿不到 TMP 类型；而 `semcheck` 当时把
`Assets/Scripts` 与 `Assets/Tests` 塞进**同一个工程**、引用集是并集（含 TMPro），所以它
全绿 —— 这是个会让人误信「测试已被覆盖」的假象。

处置分两层：

1. **改 API（治根）**：版面句柄只对外给字符串（`HintText` 读 / `SetHint` 写），
   TMPro 类型留在 `internal BindHint` 里。布局构建器没必要把内部控件类型交出去。
2. **补检查（防复发）**：`tools/semcheck/gen.js` 改为生成两个工程，对应 Unity 的两条
   asmdef 边界；`semcheck_tests.csproj` 按测试 asmdef 的口径剔除 `Unity.TextMeshPro`，
   `run.js` 分别报告 `errors=` 与 `tests-asm: errors=`。实测该检查能逐字复现上面那条
   CS0012（连 `Version=0.0.0.0` 都一样），也能抓住「测试文件里根本没写 `TMPro` 字样、
   只是读了返回 `TMP_FontAsset` 的运行期属性」这种形状。

**写测试时的口径**：测试**只能**碰运行期的公开 API；一旦需要 TMP / 其他 `Unity.Xxx` 包类型，
应当改运行期 API 的暴露面（像本轮这样），而不是给测试 asmdef 添引用。

### 7.2 开始界面不得出现壳层 chrome（顶栏）

编辑器实测截图里，主菜单上方飘着一条完整的**局内顶栏**：航司 LOGO、`天际航空 / CEO CEO`、
搜索机场输入框、地图/图层按钮、`▶▶ ▶▶▶` 速度档、`2026年10月01日 星期四 08:00`。

结论是**它不该存在**，而且不是「暂时用不上」而是**错的**：

| 顶栏上的东西 | 向导阶段实际是什么 |
|---|---|
| 航司名 `天际航空` | `RefreshHud` 在 `companyNameZh` 为空时的**兜底字面量** —— 玩家还没选航司，看到的是别人家的名字 |
| `2026年10月01日` 时钟 | 默认 `GameState` 的财年 1 时钟，不是任何存档的进度 |
| `▶▶ ▶▶▶` 速度档 | **按钮是活的**：在菜单里点会真的改 `Time.timeScale` |
| 搜索机场输入框 | 回车会 `SelectTab("n6_主页_Dashboard")` —— **能直接捅穿开局闸门进主页** |

FM 这一类经营游戏在开始界面一律没有 HUD 顶栏：闸门没关，局内数据与操作就不该在场。

**成因有两层，都得治**：

1. **漏收**：`PrefabNavIntegration` 早已在 `!_gameplayReady` 时收起底栏 `_shell` 与状态栏
   `_statusBar`，**顶栏漏了**。现统一到 `ApplyChromeVisibility(...)`（静态版可被测试直接调用），
   三条一起收、`OpenHomeDashboard()` 后一起放。
2. **层级**：向导叠加层只在**创建那一刻** `SetAsLastSibling` 过一次，而壳层的
   `RaiseChrome()` 每次导航都把 `_topBar.SetAsLastSibling()` 上浮 —— 就算顶栏不隐藏，它也会
   压在主菜单上面。现 `RaiseChrome()` 只在 `_gameplayReady` 时上浮顶栏，
   且 `SetupFlowController.EnsureOverlay()` 每次进向导页都重新压回最上层（双保险：
   以后新增 chrome 忘了进 `ApplyChromeVisibility`，也压不住向导）。

PlayMode 新增两条断言：

| 用例 | 钉住的症状 |
|---|---|
| `PrefabNav_ChromeIsHiddenUntilGameplayReady` | 三条 chrome **一起**收/放；顶栏在列。以后有人把顶栏从这个方法摘出去会红 |
| `SetupOverlay_StaysAboveChrome` | 模拟 `RaiseChrome` 把顶栏上浮后再进一次向导页 → 叠加层必须回到最上层 |

**搜索框（`TopBar/SearchBox`）另加一层行为闸门**：它跟着顶栏一起隐藏，所以向导期看不见 ——
但「看不见」不等于「用不了」。回车走 `OnSearchSubmit` → `SelectTab("n6_主页_Dashboard")`，
这是一条**绕过整个开局向导直接进主页**的路子；搜索失败时弹的 `SearchToast` 也挂在顶栏里。
故 `OnSearchSubmit` 开头加硬闸门，口径与显隐同源：

```csharp
if (!AirportSearchAllowedFor(GetComponent<PrefabNavIntegration>())) return;
```

两条路径都汇到同一处，所以「开局**或**读档后才有搜索框」是同一个开关：

```
SetupFlowController.EnterGameplay(fromContinue)
   ├─ 新游戏：ShowMainMenu → … → EnterGameplay(false)
   └─ 继续/读档：SaveManager.Load() → EnterGameplay(true)
        └─ _nav.OpenHomeDashboard() → PrefabNavIntegration.EnterGameplay()
             ├─ _gameplayReady = true
             └─ ApplyChromeVisibility()  → 顶栏（含搜索框）上线
```

**导航 API 本身也加了同一道闸门**。上面每一条都是「入口不可达」型防护 —— 而搜索框证明了
「点不到」≠「进不去」：导航 API 是 public 的，脚本 / 将来新增的 UI 都能调。故
`PrefabNavIntegration` 补 `RejectIfNotGameplayReady(what)`，在 `SelectTab` / `OnTabSelected` /
`OpenSubPage` 三处入口原地返回并告警（口径同 `_gameplayReady`）。两个细节：

- `SelectTab` 的闸门放在 `Attach()` **之前**：向导期不仅不该导航，也不该顺手把整条壳层建起来。
- 编辑器菜单（`OpenCommercePanelMenu` / `OpenFinancePanelMenu` / `OpsManualTestMenu`）不受影响，
  它们都先 `EnsureInGameplay()`，那条路经 `SetupFlowController.EnterGameplay` → `OpenHomeDashboard`
  → `PrefabNavIntegration.EnterGameplay()`，同样打开闸门。
- `[SerializeField] openDefaultMarket` 的语义本就是「跳过向导直接进局内」，故该分支显式
  `EnterGameplay()` 后再 `OnTabSelected`，否则会被自己的闸门拦掉。

| 用例 | 钉住的症状 |
|---|---|
| `AirportSearch_OnlyAfterGameplayStarts` | 向导期（含 `nav == null`）拒绝搜索；`EnterGameplay()`（新开局与读档都经此）之后放开 |
| `Navigation_IsLockedUntilGameplayStarts` | 向导期 `SelectTab` / `OpenSubPage` 原地返回且**不建壳层**（`Router` 仍为 null）；`EnterGameplay()` 后放开 |

（两个用例用 `PrefabNavIntegration.SuppressAutoAttachForTests` 关掉 `Start()` 里的 `Attach()`：
闸门类用例只关心 `_gameplayReady` 一个布尔量，不该被「Attach 需要 Canvas / Resources」的副作用牵连。
口径同既有的 `SetupFlowController.BypassForTests`。）

## 8. 已知取舍与待办

| # | 事项 | 说明 |
|---|---|---|
| 1 | 列内是**像素栈**（`PinFromTop`），不是比例栈 | 1080p 富余很大，编辑器 1024×600 预览下栈高约 438px + 页脚 32px，仍有余量；窗口高再压到 ~500px 以下会压到页脚，属已知取舍（未加滚动区：菜单 7 行不该出现滚动条） |
| 2 | 主菜单不再用通用表单卡 | `ShowMainMenu` 隐藏 `Card`、`ClearForm()` 负责恢复（唯一知晓此事的两处，注释已写明）；主菜单版面挂在 `SetupOverlay` 上，随 `ClearForm()` 一并销毁 |
| 3 | 无档态的「设置 / 存档」页仍是简化文案 | 沿用原逻辑（F5/F9 提示 + 返回），显示模式/语言等待完整设置页 |
| 4 | 未做 | 主视觉美术（见 §6）；「最近保存」卡片暂无真实世界保存时间戳（存档未记录 `DateTime`，故卡片不显示"保存于 X"） |
| 5 | 测试程序的编译口径 | 测试只能碰运行期公开 API，见 §7.1；`tools/semcheck` 已按 asmdef 边界拆成两个工程，别再合并 |
| 6 | 向导期**一律**不出壳层 chrome | 顶栏/底栏/状态栏三条统一由 `PrefabNavIntegration.ApplyChromeVisibility` 收放，见 §7.2。新增任何常驻 chrome，必须同时进这个方法；否则会出现「开始界面飘着局内控件」，甚至像顶栏搜索框那样**捅穿开局闸门** |
| 7 | 向导期**导航 API 一律拒绝** | `_gameplayReady` 是唯一真值来源：显隐、搜索闸门、导航闸门三处同源。任何新的「跳页」入口都应经 `SelectTab` / `OpenSubPage`，不要绕过它们直接操作 `Router`，否则会漏掉这道闸门 |
