# C07/C09/C10/C12 收口独立补验与 Codex 修复

日期：2026-10-18。编号：ACCEPT-COLLAB-20261018-CODEX-06。

**结论：不能直接认可“四项均已完成”。C07 的既有工程运行证据核对成立，XML 缺陷已进入交付并通过复核；但原交付在 C09 闸门和 C10 并发去重上仍有可复现缺陷。Codex 已按用户授权直接修复工作区并自测，待新交付复核。真实客户端 Hook 触发仍缺证据。**

## 1. 对象与本轮边界

- 受检交付：[DEL-20261018035835-766dc7](../collaboration/deliveries/DEL-20261018035835-766dc7.json)。开发方所提供复核：[REV-20261018035841-c4d71c](../collaboration/reviews/REV-20261018035841-c4d71c.json)。
- 开发声明：[C07/C09/C10/C12 完成记录](CollabTool_C07_C09_C10_C12_Completion_2026-10-18.md)。其历史文字和记录保留；本报告是后续独立判定。
- 新独立补验：[REV-20261018041820-9f4b26](../collaboration/reviews/REV-20261018041820-9f4b26.json)，结论 failed；关联新缺陷 [ISSUE-20261018041847-27c5bf](../collaboration/issues/ISSUE-20261018041847-27c5bf.json)。
- 开始时 sync/ack revision 165。没有启动 Unity、关闭用户进程、修改游戏代码/游戏任务书，没有 commit/push/重置/清理。原交付 25 个文件哈希全部匹配，未列文件 0，保持不变。
- 原交付工具文件清单哈希：`afd87407dc243ae1550733c7f8dbbdbdab262c5f0200b0d7d31478156547d4a7`。修复后工作区工具文件清单哈希：`6c75a968252f697908dd7e87cf820dde0a4d9d8b1c1b1b4eca2f14a1f44ebff2`。

根项目真实 smoke 已执行，退出 0、2/2，通过证据绑定核对。独立最终 failed 来自下述额外审计，不将 smoke 成功伪写为失败，见[判定 evidence](verification/CollabTool-Closure-Independent-2026-10-18/review-evidence.json)。

## 2. C07 与 XML：哪些证据可以确认

逐一核对了三份工程 REV 的实际 stdout/stderr 文件、字节数与 SHA256；全部匹配。Unity XML 的 SHA256 也匹配，before.exists=false、producedByRun=true。

- Unity：`REV-20261018033542-0f4993` / `DEL-20261018033132-39a1e3`，EditMode 25/25、退出 0，实际运行在 2026-10-18 11:35:49～11:39:12（Asia/Shanghai）。
- compile：`REV-20261018033359-8f89f1` / `DEL-20261018033139-af5cca`，3 闸门通过、退出 0。
- slot：`REV-20261018033157-6550bc` / `DEL-20261018033145-e77a66`，371/371、退出 0。

详见[逐项证据核对](verification/CollabTool-Closure-Independent-2026-10-18/engineering-evidence.json)。这些是各自工程交付的既有真实执行记录；不说成当前修复工作区或最新 C-DEMO DEL 又重跑了 Unity。占用演练的原记录也保留，本轮没有再启动模拟占用进程。

从受检 DEL 导入模块，XML 专项 26/26、结果闸门审计 12/12；修复工作区再次检查仍为 26/26、12/12，见[原 DEL XML 审计](verification/CollabTool-Closure-Independent-2026-10-18/xml-original.results.json)、[工作区最终 XML 审计](verification/CollabTool-Closure-Independent-2026-10-18/xml-final.results.json)。第五轮 XML 缺陷没有重现，其原关闭记录不重开。

## 3. C09：原交付的具体问题、原因及已实施修复

新增测试：[closure.test.mjs](../../tools/collab/tests/closure.test.mjs)。相同脚本选择原 DEL 为 **3/13**，选择修复工作区为 **13/13**。其中 3 项是通过正例，避免把“闸门永远 false”当作修复。

### CG-01：没有基线仍允许完成（G01）

所有子项已勾选，但未执行 recordBaseline。原 computeTaskGate() 在 `clause-baseline` 和 `clause-unchanged` 上使用 `!baseline || ...`，将“没有比较依据”当成通过；随后可放行总任务。

已改为：映射任务必须有当前 scheme 的基线；缺失明确阻断，旧 scheme 仍提示重录而不谎称条款文本被改。G09 证明登记基线后正常任务仍可通过。

### CG-02：任务带依赖时内部报错（G02/G03）

computeTaskGate() 构造了 nextSeen，但 finishTaskGate() 在另一个函数作用域引用该名字，变量未传入。只要 dependencies 非空就报 `nextSeen is not defined`，循环检测也无法工作。

已将访问集合显式传入共享尾部和递归调用：已满足的依赖可以通过，环形成明确 blocker，不抛内部异常。

### CG-03：条款文件缺失或条款在错误章节仍会“绿灯”（G04/G05）

readToolClauses() 对缺文件返回 error，但 computeOverallGate() 忽略该 error，空条款集合被当成“没有未勾选项”。读取存在的文件时又扫描全篇，未使用 sectionHeading，因此别处同名复选框也能冒充 §14。

已强制核验配置文档存在、目标章节唯一，并只解析该章节；C01～C12 各出现一次，缺失、重复或异常都阻断。overall 输出 `tool:document` 诊断。G10 证明正确的 12 条全勾选仍可通过。另修复缺失任务条款被统计为“已勾选”的摘要计数。

### CG-04：新失败被旧 passed 掩盖，旧交付 profile 被借用（G06/G07）

原 gate 对最新交付只寻找“曾经有一份 passed”，即使同交付后来的 REV failed 也可能放行；requiresProfiles 又搜索同任务所有历史交付，导致 DEL1 的 slot 可以填补 DEL2 缺失的 slot。

已要求最新交付的最新复核为 passed 且 freshness=current；每个必需 profile 的最新结论必须来自**同一个最新 deliveryId**。同 profile 后来的失败不会被旧 passed 覆盖。G11 证明同交付的完整 profile 集合仍可通过。

新增 G13 还发现：listReviews/listDeliveries 原按文件名排序，但 ID 的秒级时间后面是随机后缀，同一秒内顺序不等于创建顺序。已在共享列表入口按 createdAt 排序；原交付会把较早 passed 当最新，修复后正确选到较晚 failed。测试明确使用同一秒内不同毫秒的记录，而不是只比较任意 ID 大小。

### CG-05：并发修改两个缺陷会丢失已成功的状态（G12）

cmdIssueUpdate() 在锁外读取共享 state，再整体写回，没有给 saveState() 传 expectedRevision。两个不同 issue 的更新可以互相覆盖；某进程还能在提交后刷新时返回 locked，让调用方误以为尚未提交。

受控双进程复现中，A 返回成功，但最终 issueStatus 只剩 B。原始进程输出和重试前后状态见[原交付结果 G12](verification/CollabTool-Closure-Independent-2026-10-18/original-recorded.results.json)。这不是只有理论上的竞态。

已将读状态、角色允许的状态迁移、CAS 写入、事件和刷新放进同一 withStateLock；竞争方在提交前得到 locked，之后可重试，最终 A/B 状态均保留。原 immutable issue 文件不改写，单一共享状态方案保留。

## 4. C10：并发去重已修，但客户端接线不能由计数证明

### CH-01：两个进程同一个 Hook 会执行两次（G08）

原 runHook() 顺序执行“读 seen → apply → 写 seen”，没有跨进程互斥。两个进程使用相同 idempotencyKey，在第一次 apply 尚未结束时启动第二次，两个回调都执行，计数为 2。

已增加独立 hooks.lock，覆盖去重读取、apply 及结果记录整个临界区。竞争方可收到退出 4，重试会 deduped=true；回调只执行 1 次。支持关闭 Hook 时的手动流程不变。

### 仍缺的客户端证据

当前 applied=5/deduped=2 只能证明 CLI 收到并处理过这些事件，不证明它们来自 Cursor/Codex 的原生事件。项目适配示例仍标为 Example，直接调用 sync，没有进入 hook 命令的去重层。本轮没有找到足以复核“两端原生事件→项目适配→hook CLI→状态”的实际配置与触发关联。

故 C10 只能确认 CLI、BOM、顺序/并发去重和手动兜底，不认可“客户端 Hook 端到端已完成”。下一步应提供实际客户端版本/支持情况、项目级配置、原生触发与事件日志的对应证据；如果客户端不支持，应明确走不支持+手动兜底分支，不能把模拟 CLI 调用称为原生接线完成。本轮不擅自安装或修改用户全局客户端配置。

## 5. C12 与文档勾选更正

中文手册、PowerShell 包装和历史演练记录存在。核对发现权限矩阵将 `--actor user` 标为可 claim/deliver/review-*，实际角色闸门均返回 7。已修正 [中文用户手册](../collaboration/OPERATOR_MANUAL_ZH.md)，区分“人可以授权代理”和“CLI user actor 本身有命令权限”。未放开角色权限来迎合错误文档。

按独立验收回填职责，将工具手册 §14 **C09/C10 恢复未勾选**，附后续更正说明并保留 §14.1 原回填依据。C07/C12 原有勾选保留其已核对的工程证据/操作材料范围；这不意味着整个工具通过。当前 checked 应为 2，不再是 4。

游戏任务书、T01～T06 及 cleanGitCheckoutVerified=false 没有改变。全项目 gate=false 仍是正确的未完成提示；本次拒绝收口的依据是上述可复现缺陷和缺证据，不是简单因为全项目 gate=false。

## 6. 自测与测试契约调整

- 新增收口回归：原交付 3/13，修复后 **13/13**，含并发、循环、记录时间顺序及 3 个通过正例。[原结果](verification/CollabTool-Closure-Independent-2026-10-18/original-chronology.results.json)、[修复结果](verification/CollabTool-Closure-Independent-2026-10-18/repaired-chronology.results.json)。较早的 8/11/12 项探索及复跑结果均保留。
- 修复后完整套件：开发方 **38/38**、baseline **24/24**、supplemental **12/12**、boundary **8/8**，见[最终汇总](verification/CollabTool-Closure-Independent-2026-10-18/evidence/repaired-final-chronology/summary.json)。
- XML 专项 **26/26**，XML 闸门 **12/12**。未重跑 Unity。

原历史 baseline 的 N14、boundary 的 T06 在初次原样复跑中分别为 23/24、7/8：现在 run 已提前将失败写成不可变终态，finish 返回 3，而旧用例只接受 6 或 5/6。检查确认并未发生错误通过。

因此保留原脚本，另建[baseline 测试副本](verification/CollabTool-Closure-Independent-2026-10-18/baseline24-terminal.mjs)和[boundary 测试副本](verification/CollabTool-Closure-Independent-2026-10-18/edge8-terminal.mjs)。只有满足“失败状态+finishedAt+报告字节不变”，且快照场景先实际返回完整性错误时，才接受终态冲突 3；不接受任意 code=3，不降低通过闸门。原始失败输出保留在 `evidence/codex-closure-original` 和 `evidence/codex-closure-repaired` 的旧驱动目录中。

## 7. 修改范围、状态与交接

实现/测试：`tools/collab/lib/taskbook.mjs`、`hooks.mjs`、`commands.mjs`、`state.mjs`、新增 `tools/collab/tests/closure.test.mjs`。文档：工具验收清单与用户权限矩阵；新增本报告和验证副本。没有修改游戏代码或游戏任务书。

新缺陷 `ISSUE-20261018041847-27c5bf` 已按流程 open→fixing→fixed，**未标 verified/closed**。这是 Codex 修复工作区自测，不是另一方独立验收。旧 XML 缺陷维持其已核对的 closed 状态。

需要用当前修复版生成新 DEL 并复核 C09；C10 除修复回归外还需要客户端接线或不支持分支的证据。不能把当前修复自动算成 `DEL-20261018035835-766dc7` 已通过，也不能把旧 passed REV 改写成新的失败。旧 DEL/REV 均保留，新独立 REV 记录本次判定。

复现：

```powershell
# 原交付：预期 3/13
$env:COLLAB_AUDIT_TOOL_SOURCE = 'out/collab/DEL-20261018035835-766dc7/tools/collab'
$env:COLLAB_AUDIT_RUN_LABEL = 'closure-original-recheck'
node tools/collab/tests/closure.test.mjs

# 修复工作区：预期 13/13
$env:COLLAB_AUDIT_TOOL_SOURCE = 'tools/collab'
$env:COLLAB_AUDIT_RUN_LABEL = 'closure-repaired-recheck'
node tools/collab/tests/closure.test.mjs
```

生产 Assets/Packages/ProjectSettings 联合哈希保持 `e108606412e05a72cd0ca905051bb23bc77620656b5a5624b4e615444e4ff9c9`；游戏任务书 SHA256 保持 `0b2562677604c72d5094cee856764f2e3d2ba74d5b4b247d6910b962d2a51194`。修复未改变原交付清单字节或历史完成记录，无 commit/push，无清理。
