# Cursor 补测回报 · 批次一：隔离 Unity EditMode + 完整申请序列

> **状态更新（2026-11-09 R2）**：本报告里唯一失败 `SlotCashHintEditModeTests.SlotProposalShowsCashRunway`
> 已按 Codex 裁决修正场景构造，序列测试也按要求收紧了 N/A 判定与方向断言。
> 结果见 [Cursor回报_批次一_测试修正与证据归档_2026-11-09.md](Cursor回报_批次一_测试修正与证据归档_2026-11-09.md)：
> 当前实现完整隔离 EditMode **147/147 通过、0 失败 0 跳过**，前后 SLOTSEQ **121 行逐行相同**，
> 证据与源码哈希已归档到 `out/analysis/batch1-evidence/batch1-r2/`。
> 本报告保留为当时的原始记录，其「pre 重建说明」已被 `tools/regression/reconstruct-pre-batch1.mjs` 脚本化取代。

日期：2026-11-09
对应裁决：[Codex裁决_工作流澄清与首批补测_2026-11-09.md](Codex裁决_工作流澄清与首批补测_2026-11-09.md)
性质：**开发自测**。不是独立验收，不声明「Codex 已通过」。未 stage-close、未生成 DEL、未 commit/push。

---

## 一、结论速览

| 项 | 结果 |
|:--|:--|
| 隔离 Unity EditMode（真实引擎、真实程序集） | **146 通过 / 1 失败（147 条）**；两个批次一入口全绿 |
| `CapacityServiceEditModeTests.ExtractionPreservesEveryCapacityCell` | **Passed**（0.16 s） |
| `SlotSequenceEditModeTests.ApplicationSequenceKeepsApprovalsHoldingsAndCash`（本批新增） | **Passed**（0.03 s，26/26 断言） |
| 唯一失败：`SlotCashHintEditModeTests.SlotProposalShowsCashRunway` | **与本批无关，且可证**：抽取前版本同机同数据跑同一条用例，**同样失败** |
| 前后版本申请序列逐字比对 | **99 行 transcript 完全一致（identical）** |
| 主工程 / 玩家数据 | 未改动（`companyName` 仍是 `DefaultCompany`；全程 0 个 Unity 进程常驻） |

---

## 二、隔离快照与命令

### 2.1 快照

| 用途 | 路径 | 文件数 |
|:--|:--|:--|
| 抽出后（**权威被测版本**，与当前工作区逐字一致） | `D:\Project\_snapshot_20261109_final` | 1459 |
| 抽出后（前一次导出，结果与 final 完全相同） | `D:\Project\_snapshot_20261109_post` | 1459 |
| 抽出前（对照版本，见 2.3） | `D:\Project\_snapshot_20261109_pre` | 1459 |
| 首轮只跑容量（诊断用，已被 final 取代） | `D:\Project\_snapshot_20261109_batch1` | 1457 |

导出均走项目既有 `tools/regression/export-snapshot.mjs`：复制过程带哈希校验、源变更自动重试，且**只在导出副本**里把 `ProjectSettings.asset` 的 `companyName` 改为 `AirlineCEORegression`、`productName` 改为 `IsolatedBaseline_<dir>`。主工程 `ProjectSettings.asset` 未改（复核：`companyName: DefaultCompany`）。

### 2.2 命令

```powershell
# 导出
node tools/regression/export-snapshot.mjs D:\Project\_snapshot_20261109_final

# 全量 EditMode
powershell -NoProfile -ExecutionPolicy Bypass -File tools/regression/RunUnityTests.ps1 `
  -ProjectPath "D:\Project\_snapshot_20261109_final\Unity Project" -Platform EditMode -RunName Batch1Final

# 单跑两条批次一入口
... -Filter "CapacityServiceEditModeTests"  -RunName Batch1CapSvc
... -Filter "SlotSequenceEditModeTests"     -RunName Batch1PostSeq
```

跑前 `Get-Process Unity` = 0；跑后 = 0（`RunUnityTests.ps1` 自己起编辑器、自己退出，没有杀进程、没有删锁）。

### 2.3 「抽取前」对照版本是怎么来的

`AirportCatalog.cs` 在本批之前已有 2026-11-09 容量口径改动（未提交，git 里没有可用旧版），因此**不能**用 `git show HEAD:` 当「抽取前」。做法是：在 post 快照的**一次性副本**里把 Data 层还原成抽取前的形状 ——

- `_indexByCode` 不再构建，`FindByCode` 回到 `StringComparison.OrdinalIgnoreCase` 线性扫描；
- `QuarterCapacity` / `HourCapacity` / `DayCapacity` / `slotCapacity` 回到 **AirportCatalog 内联**实现（`QuarterCapacityRaw` / `PoolsRaw`，C0 口径与抽取后同一套公式，只是不再经 `CapacityService`）；
- `CapacityService.cs` 等文件**保留但不被 Data 层调用**（仅为让 Editor 程序集编译通过；`rg "CapacityService\."` 在 pre 副本里只剩回归自身与注释）。

`dotnet build tools/semcheck/SemCheck.csproj -t:Rebuild` 在 pre 副本内 **errors=0**，说明还原版可编译、被测代码与 post 只差抽取本身。

> 诚实边界：这是「按抽取前形状还原」的对照版本，不是任何一个历史提交的原文件。

### 2.4 为什么不用仓库里已有的历史快照（补查结果）

裁决要求「固定抽取前实现或**可追溯的旧版本快照**，不用新实现重生成基线并自证」，因此专门查过全盘已有的 `AirportCatalog.cs` 副本：

| 副本 | 大小 | 是否含 `CapacityService` | 容量口径 |
|:--|:--|:--|:--|
| `out/collab/DEL-20261107143823-54ca1d/…` | 55,374 B | 否 | **旧「小时曲线 + QuarterSplit」**（`HourRateOf` + `QuarterSplit`） |
| `out/collab/DEL-20261107143041-b27e5d/…` | 55,374 B | 否 | 同上（与前一份字节相同） |
| `out/b17-local/…` | 55,374 B | 否 | 同上 |
| `Unity Project/Builds/Baseline-20261017-v2/…` | 20,158 B | 否 | 更早（2026-10-17） |
| `.collab-local/shadow/…` | 26,771 B | 否 | 分片影子副本，非完整文件 |

结论：**没有一个可追溯快照同时满足「跑道数平铺」与「未抽取」**。原因是这两次改动都发生在同一段未提交的工作区里 —— 11-09 上午先改容量口径（曲线 → 跑道数平铺），随后才是本批抽取，中间没有产生过 DEL 或提交。11-07 那批交付仍是旧曲线口径，拿它当基线会让 19,968 格**必然不同**（不是抽取造成的差异，而是口径差异），反而是错误的对照。

补强证据：从 11-07 快照里读到的抽取前代码形状，与 2.3 还原版**逐项一致** ——

```csharp
// out/collab/DEL-20261107143823-54ca1d:846（抽取前真实代码）
public static AirportInfo FindByCode(string code)
{
    for (int i = 0; i < All.Length; i++)
        if (string.Equals(All[i].code, code, StringComparison.OrdinalIgnoreCase))
            return All[i];
    return default;
}
// :393
public static int QuarterCapacity(string code, int minutes)
    => QuarterCapacityRaw(code, TierOfCode(code), CoordWindowOf(code), minutes);
// :476
public static int DayCapacity(string code)
{
    int n = 0;
    for (int h = 0; h < 24; h++) n += HourCapacity(code, h);
    return n;
}
```

即：还原版沿用了**历史上的真实签名与结构**（`QuarterCapacity → QuarterCapacityRaw(code, tier, coordWindow, minutes)`、线性 `FindByCode`、`DayCapacity` 逐小时累加），只把那两处曲线算法换成 11-09 已生效的平铺算法。它之所以能当基线，还因为容量输出受**冻结基线**校验（`d8436350…`，来自抽取前源码解析，见阶段记录第三节）；本报告没有用新实现重生成基线自证。

---

## 三、隔离 EditMode 实测

### 3.1 全量（post，147 条）

```
UNITY_EXIT=2（因为下面那 1 条既有失败）
NUNIT result=Failed(Child) passed=146 failed=1 skipped=0
```

请求的两条：

```
AirlineCEO.Tests.EditMode.CapacityServiceEditModeTests.ExtractionPreservesEveryCapacityCell => Passed
AirlineCEO.Tests.EditMode.SlotSequenceEditModeTests.ApplicationSequenceKeepsApprovalsHoldingsAndCash => Passed
```

日志/XML：`D:\Project\_snapshot_20261109_final\Unity Project\Logs\Tasks20261017_Batch1Final.xml`（同名 `.log`）

零测试不算通过：这两条在 XML 里各有独立 `test-case` 与 `result=Passed`，不是「发现 0 条」的假绿。

### 3.2 那 1 条失败：`SlotCashHintEditModeTests.SlotProposalShowsCashRunway`

失败断言是第 ⑤ 项「跑道 < 3 月时正文必须出现『风险 … 危机线』行」，不是现金口径本身（紧口径 / 紧文案 / 足额 / 差额 / 信件正文四项均 True）。

根因（诊断插桩实测，快照内临时加、不入库）：

```
[DIAG-K19] 报价=246.0万 该刻可申请=0 段池满=False 现金=326.0 烧钱=40.5 改报刻=10:45 跑道数=1 日容量=96
```

- 用例固定对 `TNA 12:00` 提提案，隐含前提「12:00 还能申请」。2026-11-09 容量改「跑道数平铺」后 TNA 1 条跑道 = 每刻 1 条，他航落位按 `HourDemandCoef` 优先填最旺的刻 → **12:00 被占满（该刻可申请 0）**。
- 于是 `QueueSlotDecision` 按既有规则改报 `10:45`；`10:45` 属 10 时（系数 0.59），报价 `300×0.59=177 万`，`(326−177)/40.5 = 3.68 月 ≥ 3` → **风险行本来就不该出现**。用例的前提过期了，实现没有错。

**为什么判定与本批无关**（两条独立证据）：

1. 逻辑上：本批只把算法换位置、把线性查找换字典，19,968 格与四段池在冻结基线上差异为 0；`SlotsAvailableAt` 的全部输入（容量、我司持仓、他航占用）都没变，输出必然不变。
2. 实证上：在**抽取前**的 pre 快照里单跑同一条用例 ——

```
Tasks20261017_Batch1PreK19.xml :: total=1 passed=0 failed=1   ← 同样失败，同样的断言
```

即：先有 2026-11-09 容量口径改动（上一轮，未提交），后有本批抽取；K19 的用例前提是前者打破的。

**本批不对它做任何处理**（改期望值＝改规则，超出范围，按裁决须先反馈 Codex）。

---

## 四、完整申请序列（本批新增回归）

新文件：

| 文件 | 作用 |
|:--|:--|
| `Unity Project/Assets/Scripts/Editor/SlotSequenceRegression.cs` | 走真实入口的序列回归，输出 `SLOTSEQ` 前缀的 `key=value` transcript |
| `Unity Project/Assets/Tests/EditMode/SlotSequenceEditModeTests.cs` | NUnit 入口（让隔离 EditMode 真的跑它） |
| `Editor/RegressionTriggerBridge.cs` | 白名单新增 `slot-sequence` |
| `Editor/LogicSelfTests.cs` | 全量自测里挂上 `SlotSequenceRegression.Run()`（与 `CapacityServiceRegression` 并列） |

**没有新建业务逻辑**，只按玩家路径调用现有入口：`InboxManager.QueueSlotDecision` → `InboxManager.Approve` → `InboxManager.SettleDueEffects`（到期结算，内部走 `StrategyManager.AcquireSlotsAt`）。

固定条件：`PEK`、`cash=6000 万`、`year=1 quarter=1`、`SimUtc=2026-03-02 09:00 UTC`、空收件箱、空持仓；他航占用由既有哈希函数决定，无随机源、无真实时间依赖。

覆盖与结果（post 与 pre 两版逐项相同，26/26 PASS、0 N/A）：

| # | 场景 | 断言 |
|:--|:--|:--|
| ① | 合法申请 → 批准 → 到期交割 | 批准只排期（现金/持仓不动）；交割落 1 条；持仓时刻=提案时刻（`09:45`）；单价=该刻报价（546.00）；扣款=546 万（6000 → 5454） |
| ② | 满刻驳回（`PEK 11:00`，该刻容量 3、余 0） | 驳回；现金 0 变化；持仓 0 变化；文案「该刻已被占满（… 所在中班池 52/60 …）」 |
| ③ | 宵禁驳回（`02:00`） | 驳回；现金/持仓均不变；文案「落在宵禁时段（23:00–06:00 宵禁）」 |
| ④ | 现金不足（现金=1 万，报价 354 万） | 驳回；**不扣款**；不落持仓；文案「现金不足…若走协调池申请则不收费」 |
| ⑤ | 协调池免费 + **到港方向**（ARR） | 成功；现金不变；`kind=ARR`；只到港持仓无配对离港 |
| ⑥ | **一槽一对**（DEP + 配对到港 `15:15`） | 成功；离港持仓 `arrTime=15:15`；离港持仓 `kind` 为空串 |
| ⑦ | 超余量（要 999 条） | 驳回；不落持仓；文案含池余量 |
| ⑧ | 同一 id 重复提案 | 驳回；「该时刻槽提案已提交过」 |
| ⑨ | 背景占用 / 余量 / 方向中性 | 池容量与同刻余量在 DEP/ARR 上同值；他航占用与他航池线一致 |

transcript（`SLOTSEQ` 行）示例（post，节选）：

```
prop1.note=已提交收件箱：09:00 已被他航占满，运营部改报 PEK 09:45；请批准购入该刻时刻槽，批准后即刻过户并扣款。
settle1.cashDelta=-546.0
settle1.hold[0]=PEK|09:45|DEP|arr=-|cost=546.00|inUse=False|expQ=3
full.msg=PEK 11:00 该刻已被占满（PEK 11:00 · 离港池 该刻容量 3 · 该刻余 0（已持 3 条）；所在中班池 52/60（我司 0 · 他航 52 · 余 8）· 协调 24h）。
check.summary=pass=26 fail=0 na=0
```

### 4.1 前后版本逐字比对

```powershell
# pre / final 各自把 SLOTSEQ 行抽出来做 Compare-Object
pre_lines=99 final_lines=99
DIFF: identical
```

XML：`...\_snapshot_20261109_pre\...\Logs\Tasks20261017_Batch1PreSeq.xml`、`...\_snapshot_20261109_final\...\Logs\Tasks20261017_Batch1Final.xml`
（同一 transcript 在 post / final 两次独立导出上也逐字相同，说明没有隐藏的跨运行状态。）

即：相同初始状态、相同输入序列下，抽取前后**批准/驳回文案、持仓（机场/时刻/方向/配对到港/单价/到期季）、资金流水与余额逐字相同**。

---

## 五、未覆盖 / 边界

| 项 | 状态 |
|:--|:--|
| PlayMode 定向用例 | **未跑**。本批序列在 EditMode 内完成（不依赖游戏循环）；按裁决「能在 EditMode 完成就不扩大」故未做。 |
| 完整经营 A/B（多季经营、随机事件） | 按裁决不做。 |
| B 布局启用（16/20/20/40）、`Place` 重构、ICAO/别名/`airportId` 写档 | 仍在暂缓，本批未动。 |
| `SlotCashHintEditModeTests` 的用例前提过期 | **留给 Codex 裁**：修期望值属改规则；另需决定是否把「提案被改刻」纳入该用例前提。 |

---

## 六、需要 Codex 判定的两件事

1. `SlotCashHintEditModeTests.SlotProposalShowsCashRunway` 的前提（`TNA 12:00` 尚可申请）已被 2026-11-09 容量口径打破。要么按「提案被改刻」重写该用例的场景构造，要么改成显式锁定报价与时刻。属改测试规则，Cursor 不自作主张。
2. 本批是否算「首批验证完成、可进入独立验收」。Cursor 只报事实：隔离 EditMode 全绿（除上述既有失败）、申请序列前后逐字相同、26/26 断言通过。

---

## 七、复现清单

```powershell
node tools/regression/export-snapshot.mjs D:\Project\_snapshot_20261109_final
powershell -NoProfile -ExecutionPolicy Bypass -File tools/regression/RunUnityTests.ps1 `
  -ProjectPath "D:\Project\_snapshot_20261109_final\Unity Project" -Platform EditMode -RunName Batch1Final
# 前后对照：把 final 快照复制成 pre 后，按 2.3 还原 Data 层，再跑 -Filter "SlotSequenceEditModeTests"
```
