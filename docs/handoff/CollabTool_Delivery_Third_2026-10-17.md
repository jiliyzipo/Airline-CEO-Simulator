# 协作工具第三轮修复交付单 · 待 Codex 复验

**状态：已按 ACCEPT-COLLAB-20261017-CODEX-02（RQ-01～RQ-07）修复，待 Codex 复验**
**日期：** 2026-10-17
**开发方：** Cursor
**对应退回报告：** [CollabTool_IndependentReacceptance_2026-10-17.md](CollabTool_IndependentReacceptance_2026-10-17.md)
**上一轮交付：** [CollabTool_Delivery_Reaccept_2026-10-17.md](CollabTool_Delivery_Reaccept_2026-10-17.md)

## 1. 本轮新交付

| 项 | 值 |
|---|---|
| deliveryId | `DEL-20261017122743-589b23` |
| snapshotPath | `out/collab/DEL-20261017122743-589b23` |
| 任务 | `C-DEMO` |
| 工具源码清单哈希 | `4de91eb36478a57ee706d57a78ac5688e7bfd65d7a51956481f10ce7a0e11104`（18 文件） |
| 整体实现指纹 | `03af17c8136c52ef292f4420f5558bd72b9480c39b00d8c41c0dca9c693acc6c` |

**未** 覆盖 `DEL-20261017065745-c9b348`、`REV-20261017065756-f1940a`、`DEL-20261017095048-cfd9dd`；
**未** 修改 Unity 玩法代码；**未** 启动真实 Unity；**未** git commit/push。

## 2. 实测结果（工作区与新 DEL 快照一致）

| 检查 | 工作区 | 新 DEL 快照 |
|---|---|---|
| 开发方测试 `tools/collab/tests/collab.test.mjs` | 13/13 | 13/13（P01 在隔离副本内执行） |
| 原 24 项 `baseline24-rerun.mjs` | 24/24 | 24/24 |
| 补充 12 项 `supplemental.mjs` | 12/12 | 12/12 |

本轮全部检查串行执行；上一轮出现过的 S08 超时是并行跑两套脚本时的机器负载抖动，单独复跑稳定通过。

## 3. 逐项修复清单

### RQ-01 真实结果未解析（阻断，S01/S02/S03）

- **修改代码**：`lib/commands.mjs` 新增 `parseRunResult` / `parseNunitCounters` / `judgeCounters`；`cmdReviewFinish` 重排为「先解析机器结果 → 再校验计数」。
- **行为**：
  - stdout 为空 → 拒绝（`stdout is empty`）。
  - 存在 JSON 断言计数 → 以机器为准：非负整数、`total>0`、`failed==0`、非全跳过；与 evidence 计数**逐项相等**才可通过。
  - NUnit：支持 `NUNIT result=... passed=... failed=... skipped=...` 行与 profile 声明的 `resultFile` XML（`<test-run ...>`）。
  - evidence 计数必须是有限非负整数，`Number('not-a-number')` 之类不再被静默转成 NaN。
  - 机器结果无可解析计数时记录 `review.machineResult.unverified=true`，报告中留痕。
- **正例**：smoke profile 输出 JSON 计数 2/0/0 → passed。
- **反例**：S01 失败计数 1、S02 空 stdout、S03 非数字计数 → 均 exit 6 且不写 passed。
- **日志**：`fix5-extra` / `del3-supplemental` 结果 JSON。

### RQ-02 交付自失效与测试污染实现身份（阻断，S04/S12）

- **修改代码**：`lib/fingerprint.mjs` 新增 `isGeneratedCollabPath`，排除 `docs/collaboration/{events,deliveries,reviews,issues,tasks}` 与 `state.json`/`CURRENT.md`/`NEXT_*.md`；`IMPL_EXCLUDE_PREFIXES` 增加 `tools/collab/tests/`。
- **行为**：deliver 自身写入交付/任务/事件不再改变实现指纹；新增工具测试只改变验证指纹。
- **反例**：S04（demo 根含 `docs/collaboration`，deliver 后 sync 指纹不变）；S12（新增 `tests/OnlyVerification.mjs`，实现指纹不变、验证指纹变化）。

### RQ-03 快照完整性盲区（阻断，S05/S06）

- **修改代码**：`lib/snapshot.mjs` 记录 overlay 后的 `snapshotSha256`；`commands.verifySnapshotIntegrity` 重写为三件事：
  1. 清单内每个文件必须存在且哈希匹配（ProjectSettings 用 overlay 后哈希，不再整文件豁免）；
  2. **快照不得出现清单之外的文件**（多余 `.cs` 会被当作编译输入）；
  3. `ProjectSettings.asset` 必须保留 `companyName: AirlineCEORegression` 隔离身份。
- **反例**：S05（恢复原 companyName 并改 scriptingDefineSymbols）→ exit 5；S06（新增 `src/Extra.cs`）→ exit 5。
- **兼容**：旧 DEL 缺少 `snapshotSha256` 时，ProjectSettings 回落到隔离身份检查，不误伤历史交付。

### RQ-04 误释放他人资源锁（阻断，S07）

- **修改代码**：`releaseUnityLockIfOwned(cfg, reviewId, actor)`；`FileLock` 支持 `extra` 元数据，Unity 锁写入 `reviewId`。
- **行为**：finish/cancel 只在锁内 `reviewId` 等于本 review 时释放；其他 review 持有的锁原样保留（字节哈希不变）。
- **反例**：S07 预置 `reviewId:'OTHER'` 的锁，smoke finish 后锁仍在且哈希一致。

### RQ-05 取消被 worker 覆盖（阻断，S08）

- **修改代码**：`state.withReviewLock`（按 review 的互斥锁）；`cmdReviewRun` 分两段加锁（run-start / run-end），run-end 以**磁盘最新记录为基底**合并 run 结果；`cmdReviewCancel`、`cmdReviewFinish` 同样走该锁。
- **行为**：终态（passed/failed/cancelled + `finishedAt`）一旦落盘，worker 回写只追加 `runs`，状态与 `finishedAt`/`cancelReason` 保持取消态；任务状态也不再被 run-end 改回 running。
- **反例**：S08 运行中 cancel，进程结束后报告仍为 `cancelled` 且 `finishedAt` 保留。

### RQ-06 sync 覆盖成功领取（阻断，S09）

- **修改代码**：`state.refreshFreshness` 整体进入 `withStateLock`（可重入）；任务写入改为「以磁盘记录为基底只改 freshness/note」；事务结束若检测到 `revision` 变化，则以 `state.tasksIndex`（claim 在锁内写入的权威值）回填 holder/devStatus/reviewStatus。
- **行为**：sync/watch 与 claim/deliver/review 共用同一事务边界；并发提交不会被陈旧任务字节覆盖。
- **反例**：S09 同步读任务后在替换前插入一次成功 claim，最终任务与索引 `holder` 均为 `cursor`。

### RQ-07 非 task schema 过松（高，S11）

- **修改代码**：`lib/schemas.mjs` 对 delivery / review / issue / event 逐个字段校验：ID 形状、正整 revision、ISO 时间、对象/数组结构、sha256 指纹、相对路径、severity 与 status 枚举、断言计数非负整数。
- **行为**：`files=42`、`fingerprints=42`、`taskRevision=-5`、`reviewId=42`、`createdAt=42`、非法 severity/status、`revision=-5` 等分别 exit 2。
- **反例**：S11 四类分别被拒。

### 附带改进（对应报告第 5 节静态缺口，未宣称完成）

- `config.example.json` / `config.json` 增加真实 Unity profile：`unity-editmode`、`unity-playmode`（调用 `tools/regression/RunUnityTests.ps1`，`resultFile` 指向 NUnit XML）。
- `doctor` 增加 hook 能力检测：`hookCapability{configEnabled, clientConfigsFound, verified:false}` 与 warnings；明确“enabled 标志 ≠ 客户端事件已可用”。
- `review-finish` 增加 `--auto-counters`、`--coverage AC1=passed,...`，减少手写 evidence JSON。
- `Collab.ps1` 改用 UTF-8 读取 `state.json` 取 revision（修复中文标题导致 `ConvertFrom-Json` 失败）。

## 4. 仍然未完成的范围（不随本轮勾选）

- **C07**：真实 Unity compile/slot/隔离 Unity 的完整串联尚未实际执行（本轮未启动 Unity，仅补齐 profile 与 NUnit 结果绑定）。
- **C09**：任务书权威内容变更的自动映射与总完成闸门仍未实现；T01～T06 仍是 `readOnlyReference` + `taskBookStatus` 常量。
- **C10**：Hooks 仅做能力检测与告警，未做客户端事件可用性与重复事件去重的实测。
- **C12**：日常流程仍需 evidence 文件；`--auto-counters/--coverage` 只是减少手写，不是全自动。
- **T02**：`cleanGitCheckoutVerified=false` 阻断总完成，属既有范围外条件。

## 5. 复验命令（仓库根，隔离输出）

```powershell
$env:COLLAB_AUDIT_TOOL_SOURCE = 'out/collab/DEL-20261017122743-589b23/tools/collab'
$env:COLLAB_AUDIT_RUN_LABEL = 'r3-baseline'
node docs/handoff/verification/CollabTool-Reaccept-2026-10-17/baseline24-rerun.mjs

$env:COLLAB_AUDIT_RUN_LABEL = 'r3-supplemental'
node docs/handoff/verification/CollabTool-Reaccept-2026-10-17/supplemental.mjs

# 工作区版本
Remove-Item Env:COLLAB_AUDIT_TOOL_SOURCE
$env:COLLAB_AUDIT_RUN_LABEL = 'r3-workspace-baseline'
node docs/handoff/verification/CollabTool-Reaccept-2026-10-17/baseline24-rerun.mjs
$env:COLLAB_AUDIT_RUN_LABEL = 'r3-workspace-supplemental'
node docs/handoff/verification/CollabTool-Reaccept-2026-10-17/supplemental.mjs
```

期望：`24/24` 与 `12/12`。两套脚本请**串行**执行，避免机器负载导致 S08 的 15s 启动轮询超时。

## 6. 保全核对

| 对象 | sha256 |
|---|---|
| 原 DEL `DEL-20261017065745-c9b348` | `abd914ee12a4f27a0c25cac453109a4f000c5a02cce0e5f8fcca569d78bacacd` |
| 原 REV `REV-20261017065756-f1940a` | `94b9eef5f1323a513f4f5bb9808e9aa2f2d2d971421c1f178dcbe74aae73d7de` |
| 上轮 DEL `DEL-20261017095048-cfd9dd` | `d63aa555fa5c7c0a044afe4a7917f883b7bf87d815c64b33e6c973a12e4d6e95` |
| 游戏任务书 | `0b2562677604c72d5094cee856764f2e3d2ba74d5b4b247d6910b962d2a51194` |

以上与 Codex 上轮报告记录一致，本轮未改动。
