# 最新阶段交付独立检测：未通过

日期：2026-10-18。检测者：Codex。

## 受检对象与结论

- 最新正式交付：`DEL-20261018093335-80f2f4`，任务 `S01-COLLAB`，开发方 actor=cursor。
- 受检输入固定在 `out/collab/DEL-20261018093335-80f2f4`，不以工作区源码代替快照。
- 规定 collab 回归：**126/126**；记录 `REV-20261018095043-e1c6f3` 为 passed，保留原样。
- 新增独立边界检查：**1/6 通过，5/6 失败**，含一个正常流程正例。最终补充判定：**`REV-20261018095357-18f7dd` = failed**，关联5个真实ISSUE。

交付方套件通过证明原用例成立，不能覆盖下面新复现的业务边界。早先工具MVP的12/12验收针对旧DEL，不自动扩展到本次新增stage自动化。不能将本轮宣布为“阶段自动化已验收通过”。

原stage-review命令在机器回归通过后自动写了passed，且交接索引可能仍显示该次机器通过；本轮更晚的独立failed REV与5个ISSUE是当前收口依据。历史passed不覆盖，也不删除。

## SA03：自定义AC未经验证，被自动声明passed（blocker）

ISSUE：`ISSUE-20261018095403-4c0790`。位置：交付快照 `tools/collab/lib/stage.mjs:911`，`buildEvidence()`。

**复现：** 隔离夹具中的已有任务有一个明确AC：必须存在`required-proof.txt`并包含人工批准记录。该文件始终不存在。选择只输出合法2/0/0机器计数的smoke命令，然后按支持的`stage-close --task`、`stage-review`路径执行。

**实测：** 正式REV被写成`passed`，该AC的coverage也是`passed`；`requiredArtifactExists=false`。不是旧证据复用，而是本次自动生成的错误覆盖声明。

**原因：** `buildEvidence`对任务的每一个AC执行同一个映射：只要profile机器结果成功，就全部标为passed。`cmdStageClose`允许复用已有自定义任务，却不要求这些AC有机器检查映射。一个profile的计数不能证明任意业务条件、人工操作或必需产物。

**针对性修复：** 区分已知机器条件与需要人工/专项检查的条件。只有可验证的AC映射才能自动passed；未覆盖项保留not_run/blocked，允许检测者明确补证。复用已有任务时检查profile与AC契约，不能把自由文本验收条件整体转换成机器通过。自动化应减少命令，不替人编造检查结论。

**复验要求：** 缺失必需文件仍必须失败；未执行的人工AC不能passed；真正覆盖的机器AC正例仍能自动通过。

## SA02：Unity预检不遵守共享资源锁（blocker）

ISSUE：`ISSUE-20261018095403-f4f17e`。位置：`tools/collab/lib/stage.mjs:320`、`:340`，`runProfileInSnapshot()`。

**复现：** 在隔离夹具建立由另一个review持有、当前进程仍活着的`.collab-local/locks/unity-review.lock`。调用实际预检函数，profile名为`unity-editmode`，命令用只写哨兵文件的Node占位程序。

**实测：** 持锁时命令仍被执行，`executed=true`、`preflightOk=true`，没有返回锁定4。**测试没有启动真实Unity**，哨兵仅证明资源被占用时执行路径没有被挡住。

**原因：** 新预检使用自己的`spawnSync`路径，没有复用正式review-run的Unity资源租约和临执行前占用检查。`cmdStageClose`只在快照生成前检查一次用户Unity进程，既不处理已预留的review资源锁，也覆盖不了导出快照期间编辑器重新打开的窗口。

**针对性修复：** 预检也必须使用共享Unity租约，持有到进程与结果核验结束；临spawn前重新检查占用，未知则fail-closed；按实际持有者释放租约。优先复用共享执行层，不建立一套更宽松的预检执行器。不得终止用户Unity、删除其锁或抢占其他review租约。

**复验要求：** 另一review持锁、用户Unity占用、无法检查占用三种情况均不得执行命令；释放自己持有的租约后正例正常执行。

## SA01：预检未校验快照完整性，污染后仍交接ready（major）

ISSUE：`ISSUE-20261018095403-f1e86c`。位置：`tools/collab/lib/stage.mjs:320`，预检执行与返回判定。

**复现：** profile在隔离交付副本内改写清单中的`tools/collab/proof-input.txt`，然后输出合法2/0/0计数，退出0；走真实stage-close。

**实测：** `preflightOk=true`、handoff状态`ready`；同一副本随后调用正式`verifySnapshotIntegrity`立即退出5。预检已把不完整输入当作可送检快照。

**原因：** 新预检只判断进程退出码与结果计数，没有执行前后manifest/清单外文件/隔离身份校验。正式review-run仍会拒绝污染，但前面的“预检通过、可检测”说明已不可信，还会把无效交付交给用户。

**针对性修复：** 复用正式完整性核验和profile运行产物白名单；运行前拒绝错误输入，运行后发现修改或夹带源码时预检失败，handoff不得ready。不要将编译/Unity合法产物与额外源码混淆，也不要改写原清单来迎合污染。

**复验要求：** 清单文件改写、输入缺失、夹带源码均拒绝；声明过的合法运行产物允许；污染交付不应进入默认待检队列。

## SA04：并发stage-review对同一DEL实际执行两次（major）

ISSUE：`ISSUE-20261018095403-a7a8ab`。位置：`tools/collab/lib/stage.mjs:957`至`:972`，查询终态与创建review之间。

**复现：** 第一进程对一个阶段DEL开始review，命令写入PID并等待释放；确认已经实际执行后，第二进程对相同DEL运行stage-review，再释放两个进程。

**实测：** 哨兵记录`executions=2`，有两个不同REV。一个完成passed，另一个在摘要状态竞争中返回4；即使后者最后报锁定，第二次实际执行已经发生。

**原因：** 只查“已结束”的review，没有对“为此DEL选择/创建/执行stage-review”加跨进程互斥。底层每个REV的执行锁只保护同一个REV，挡不住同一DEL创建两个新REV。handoff索引/摘要竞争还会造成调用者无法判断部分动作是否已提交。

**针对性修复：** 对阶段交付及所选profile使用跨进程执行租约，原子地查找/复用进行中或已终态的review，再决定是否创建。锁范围应涵盖执行与handoff结果提交；不要用一个全局锁阻塞所有无关任务，也不要把不同必需profile的合法复验一概禁止。handoff索引写入须在相应事务/锁内完成。

**复验要求：** 并发两个相同stage-review实际命令只执行一次；重试返回已有执行/终态；结果、索引和task状态一致；不同交付的合法并行继续可用。

## SA05：阶段入口拒绝底层已认可的限定反向授权（major）

ISSUE：`ISSUE-20261018095403-dd65a3`。位置：`tools/collab/lib/stage.mjs:928`，`cmdStageReview()`。

**复现：** 隔离夹具创建Codex开发的DEL与完整、匹配任务/交付/三类指纹/profile的Cursor授权。底层review-start接受该授权；随后用同一声明调用stage-review（允许新review，force=true）。

**实测：** 底层授权正例成立；stage-review退出7，尚未进入作用域授权验证。

**原因：** 函数开头虽然判断“Cursor无授权时拒绝”，紧接着仍无条件调用`assertRoleAction(actor,'review-start')`。默认角色表本来就禁止Cursor，导致任何授权都无法通过新入口；与该模块声明和已有反向复验流程矛盾。

**针对性修复：** 阶段入口复用底层限定授权判断，不全局放宽Cursor权限。创建、执行、证据、finish/cancel均需保留真实actor与同一授权。所有机器通过的提前返回路径也应验证调用者对该交付的权限，不能借“已经验收”省略授权。

**复验要求：** 匹配的Codex交付反向授权可用；无授权、错DEL/指纹/profile和Cursor自验仍拒绝；已有正常Cursor开发→Codex检测正例不回归。

## 原始证据与复跑

- 脚本：`verification/CollabTool-Stage-Independent-2026-10-18/stage-audit.mjs`。
- 6项详细结果：`verification/CollabTool-Stage-Independent-2026-10-18/results-2026-10-18T09-54-03-314Z.json`，含夹具目录、进程输出和实际值。
- 补充证据：`verification/CollabTool-Stage-Independent-2026-10-18/REV-20261018095357-18f7dd.evidence.json`。
- 正式记录：`docs/collaboration/reviews/REV-20261018095357-18f7dd.json`，stdout有runToken/hash绑定。

从项目根执行：

```powershell
node docs/handoff/verification/CollabTool-Stage-Independent-2026-10-18/stage-audit.mjs
```

该脚本固定读取本次DEL模块，只在临时夹具中改文件/启动Node进程。它输出真实机器计数，当前预期为6项中5项失败；进程退出0是为了允许review-finish绑定计数并登记具体ISSUE，**不是通过**。必须检查assertionsFailed；正式补充REV已据此failed。

`review-audit.mjs`使用仅内存中的`independent-stage` profile生成正式补充REV，profileDefinition被冻结在记录中，不修改生产config。该profile执行外部检测脚本，但业务被测模块严格来自固定DEL；不与交付者126项套件混称同一批用例。

## 当前处理建议与范围

本次检测未修改工具实现、生产配置、游戏代码、任务书、玩家数据或原DEL/终态REV，没有启动Unity、commit/push或清理已有文件。新增内容仅为验收脚本、证据、报告及正常协作REV/ISSUE/事件/状态。

修复优先级：先SA03错误通过与SA02资源锁，再SA01输入完整性、SA04并发重复执行、SA05授权回归；修复后生成新DEL，保留本轮失败记录。修复前可继续使用既有细分review-*流程，并由检测者逐项核验AC，不把stage-review的一条机器passed等同于完整阶段验收。
