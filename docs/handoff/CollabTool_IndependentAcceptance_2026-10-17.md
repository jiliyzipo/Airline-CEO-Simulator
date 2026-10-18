# Cursor / Codex 协作工具独立验收报告

日期：2026-10-17。验收方：Codex。报告编号：ACCEPT-COLLAB-20261017-CODEX-01。

**结论：不通过，退回 Cursor 修复。§14 的 C01～C12 本轮均不具备完整勾选条件。**

本报告针对协作工具交付，不代表游戏开发任务验收，也不改变 R42 或 T01～T06 的结论。Cursor 的“开发完成”及演示记录中的 `passed` 不等于本次独立验收通过。

## 1. 依据、对象与边界

- 需求依据：[开发需求与操作手册 §14](../Development%20Manual/Development%20Task%20Book/Task%20book/Cursor_Codex_协作工具_开发需求与操作手册_v1.0_2026-10-17.md#14-mvp-交付与验收清单)，结合其中的指纹、隔离、并发、结果解析及恢复要求。
- 交付声明：[CollabTool_Delivery_2026-10-17.md](CollabTool_Delivery_2026-10-17.md)。
- 原交付：[DEL-20261017065745-c9b348](../collaboration/deliveries/DEL-20261017065745-c9b348.json)。原演示检测：[REV-20261017065756-f1940a](../collaboration/reviews/REV-20261017065756-f1940a.json)。原记录保留不覆盖。
- 分别检测原 DEL 隔离快照中的 `tools/collab` 与当前工作区的 `tools/collab`。两者代码并非完全一致，不能混作同一受检版本。
- 阅读共同规则、协议、操作说明、配置、CLI、schema、指纹、快照、锁和状态代码，以及开发方测试；行为检查全部在专用临时任务与目录中进行。
- 没有修改生产业务代码，没有运行游戏完整自测，没有启动真实 Unity，没有关闭用户 Unity、删除工程锁或清空玩家词库，没有提交、推送、重置或清理工作区。

按项目 AGENTS.md 执行过根项目 `sync` 和 Codex `ack`（revision 22）。因此协作工具生成状态、事件、ack 和摘要存在本轮操作更新；这与下面保留不变的原 DEL、原 REV、游戏任务书及生产目录是不同范围。独立测试自身只操作隔离 fixture。

## 2. 真实执行结果和证据

两个受检版本的结果一致：开发方自带测试 **13/13 通过**；独立检查 **24 项，5 项通过、19 项失败，进程退出码 1**。这 24 项包含开发方测试整体执行一项，不能将两组计数简单相加。负例检查的“失败”表示工具没有满足预期保护条件。

- [当前代码完整结果](verification/CollabTool-2026-10-17/current-results.json)
- [原 DEL 代码完整结果](verification/CollabTool-2026-10-17/delivery-results.json)
- [当前代码执行日志](verification/CollabTool-2026-10-17/current-run.log)
- [原 DEL 代码执行日志](verification/CollabTool-2026-10-17/delivery-run.log)
- [开发方测试输出](verification/CollabTool-2026-10-17/developer-tests.stdout.txt)
- [独立检查脚本](verification/CollabTool-2026-10-17/independent.mjs)

通过的五项是：开发方测试可在副本执行（P01）；Cursor 角色不能执行 review-finish（P02）；重复 init 保留任务和状态（P03）；外部进程非零时不能声明通过（N14）；复制过程中源变化会拒绝发布混合快照（P04）。这些局部结果不足以通过整个交付。

受检源码指纹仅覆盖各自 `tools/collab` 文件集，**不是游戏实现指纹，也不是原 DEL 的整体实现指纹**：

- 原 DEL 工具源码：`1053470a24375c378654873ef8b31f1ead75c030e9ab046c4b09b277870433e2`。
- 当前工具源码：`d0fc6965d6acf645aa4f9e65af6f09beb58de4d4212cd8b9b2c7251142bd5456`。

复现时在项目根目录执行，脚本会在 `.collab-local/acceptance-20261017/` 下新建隔离 fixture 和结果，不调用真实 Unity：

```powershell
$env:COLLAB_AUDIT_RUN_LABEL = 'current-recheck'
Remove-Item Env:COLLAB_AUDIT_TOOL_SOURCE -ErrorAction SilentlyContinue
node docs/handoff/verification/CollabTool-2026-10-17/independent.mjs

$env:COLLAB_AUDIT_RUN_LABEL = 'delivery-recheck'
$env:COLLAB_AUDIT_TOOL_SOURCE = 'out/collab/DEL-20261017065745-c9b348/tools/collab'
node docs/handoff/verification/CollabTool-2026-10-17/independent.mjs
```

原 DEL 复现依赖本机仍保存其 `out/collab` 快照。原始结果 JSON 记录了每项观察和受检文件清单。受限沙箱曾使开发方测试的临时文件替换出现 EPERM，最终统计取自允许本地子进程与临时文件操作的复跑；未把沙箱故障算作产品缺陷。并发检查使用受控交错调度，暂停两个测试进程在共享状态替换前，未改受检源码。

## 3. 阻断问题：代码与行为均已验证

### QA-01：验收执行对象不是固定交付快照（阻断；N02、N13）

`tools/collab/lib/commands.mjs:564` 以 `cfg._repoRoot` 为运行目录，只提供 `COLLAB_SNAPSHOT` 环境变量。现有配置命令没有使用该变量切换受检源，也没有在执行前核验快照内容。

复现：交付快照保存 A，将工作区文件改为 B，review-run 实际读到 `workspace-B`，报告仍关联 A 的交付；修改快照后也能继续执行成功。必须让所有 profile 明确运行所绑定的快照，并核对执行输入及快照完整性。

### QA-02：缺真实结果、未跑必测项和全跳过均可通过（阻断；N03～N05）

`commands.mjs:637–667` 对 evidence 中的 subjectImplementationFingerprint 仅在存在时比较，依赖人工填入的计数和条目，没有可靠解析实际测试结果、核对任务全部必测 AC。

独立复现三种错误通过：缺受检指纹和实际结果；必测 AC 为 `not_run`；3 项断言全部 skipped。原 REV 本身也在 AC3 为 `not_run` 时写入 `passed`。该 REV 的角色字段只是原演示输入，本次 Codex 不认可其为独立验收结论。需以真实进程结果和完整必测清单共同决定能否通过。

### QA-03：历史验收可覆盖，结束时未重新检查适用性（阻断；N06、N07）

`commands.mjs:673、710` 重写相同 review 文件，缺少终态保护。相同 review 第二次 finish 可以把 passed 改成 failed，文件哈希变化。测试执行后工作区发生变化，finish 仍将任务标记为 `passed/current`（`:679–680`）。

需保留不可覆盖的完成记录，以新 review 表达复验；当前适用性另行计算，结束时重新检查实现、验证和合同基线。

### QA-04：指纹边界和报告绑定不正确（阻断；N08～N10、N19）

- `state.mjs:134–146` 将可变任务 JSON 整体用于合同指纹。deliver 自身修改流程状态和时间后，合同指纹立即改变。
- demoSnapshotRoots 包含整个 `docs/collaboration`，一次 ack 产生事件/状态即可改变实现指纹，形成自失效。
- `fingerprint.mjs:38–45` 未将 `Unity Project/Assets/Tests` 纳入验证根；修改其中测试仅改变实现指纹，验证指纹不变。
- `fingerprint.mjs:184` 的适用性判定不包含仅验证基线变化；状态刷新也没有完整处理此分支。
- `state.mjs:150–159` 分别取同任务的最新交付和最近通过报告，未要求其 deliveryId 一致。先通过 DEL1，再失败 DEL2，摘要竟显示旧 REV1 曾通过新 DEL2。

需拆出稳定合同字段，排除生成状态，正确识别验证输入，并严格按 deliveryId 关联报告。

### QA-05：正常交付入口无法建立匹配快照（阻断；N11）

非 demo fixture 实际 deliver 退出 5：`snapshot fingerprint mismatch after export`。`commands.mjs:407–410` 的交付实现清单与 `snapshot.mjs:31–35` 合并实现/验证根的导出清单口径不同；父子根还可能重复收集文件。demo 的缩小根掩盖了正常路径问题。应统一输入清单、按文件路径去重，并分别校验各类指纹。

### QA-06：全局状态缺事务式 CAS（阻断；N17）

`state.mjs:34–50` 的 CAS 参数并未由主要调用链可靠传入；`:86–101` 先写 task 再写 state。`commands.mjs:305–337` 的 revision 检查与保存分离，各任务锁不能保护共享 state。

两个不同任务进程在同一旧 revision=1 下领取，最终两者均成功、两任务 holder 均更新，但全局 revision 仅变为 2，两次变更丢失一次版本推进。需为共享状态建立真正的提交锁/CAS；冲突应返回最新 revision，不能留下部分任务写入。

### QA-07：Unity 检查失败时误报空闲，真实入口未衔接（阻断；N18；部分静态证据）

`commands.mjs:49–75` 忽略 PowerShell 启动失败。隔离 PATH 后实际返回 `checked:true,running:false,pids:[]`，把“无法检查”当成“没有 Unity”。review-run 前也没有完整重查占用和锁归属。

配置只有 smoke/compile/slot，没有可实际使用的 Unity profile。`snapshot.mjs:58` 将隔离 companyName 写成 `AirlineCEOCollab`，现有 `RunUnityTests.ps1:10` 却要求 `AirlineCEORegression`。这是静态接口冲突，**不是本轮真实 Unity 测试失败**。本轮未启动 Unity，C07 的真实编译、slotcheck、隔离 Unity 串联仍待修复后执行。必须先可靠处理“未知占用”，再在实际运行前重查，且仅释放本次持有的锁。

### QA-08：失败、中断和恢复状态不闭合（高；N16、N20）

外部进程失败后，报告已 failed，但任务仍 running。中断本测试自己启动的 review worker 后，等待子命令结束并 sync，任务仍 running，没有跟踪 worker PID，也没有可用且文档化的恢复/取消命令。不能把无限 running 作为崩溃恢复。需提供可审计恢复状态和操作，同时保护用户 Unity 进程与锁。

### QA-09：schema 仅校验字段存在，非法类型通过（高；N01）

`schemas.mjs` 接受错误版本类型、数字 projectId、负 revision、非数组 scope、空 AC 对象及不合法 taskId 等输入。应补齐字段类型、枚举、结构、版本、路径与标识校验，失败非零退出；交付、缺陷、事件及结果也应覆盖。

### QA-10：摘要与实际任务书不一致，用户操作链未完整交付（高；静态检查）

游戏任务仅作为 readOnlyReference，CURRENT 却可呈现 T01、T06 为 todo/not_run，与已有任务书结论不一致。没有实现需求中的任务书映射与完成闸门。

Hooks 是配置示例，缺实际安装/客户端能力检测和重复事件保护的可用证据。doctor 不能据 enabled 标志证明 Hook 有效；其目录可写判断也未可靠验证。缺要求的 PowerShell 包装和无需用户手写复杂 evidence JSON 的操作入口。不能宣称“两端已实时同步”或 C10/C12 已完成。

### 其他已验证的协作风险（N12、N15）

未 ack 也能 claim；无变更 sync 仍推进 revision、更新任务时间，watch 会持续使确认落后。前者是共同协议缺少程序闸门，后者是无变更同步稳定性问题；不将其单独夸大为角色身份认证漏洞。角色 actor 由调用者声明是需求已知边界。

## 4. §14 逐项验收结论

以下均保持未勾选；“部分实现”不等于整项验收通过。

- [ ] **C01 不通过**：格式已有，类型/结构校验不足，N01；真实结果校验亦不完整。
- [ ] **C02 部分实现，未通过**：init 重复保护及部分命令可运行；doctor 对进程检查错误误判、无变更 sync 推进状态，N15、N18。
- [ ] **C03 不通过**：合同、生成状态、验证文件边界错误，N08～N10；未宣称其余全部资源/删除/包锁负例已覆盖。
- [ ] **C04 不通过**：复制期间变化保护通过 P04；运行对象和快照篡改检查失败 N02、N13，正常交付失败 N11。
- [ ] **C05 不通过**：局部锁测试通过不等于事务安全，真实双进程交错丢 revision，N17；Unity 资源入口仍有缺口。
- [ ] **C06 不通过**：演示可以生成 passed，但不满足独立完整闭环与严格通过闸门，N03～N07；正常非 demo 路径受 N11 阻断。
- [ ] **C07 未完成/不通过**：外部非零保护 N14 通过；缺结果、全跳过却可通过；本轮没有真实编译/slot/Unity 一体执行成功证据。
- [ ] **C08 不通过**：历史可覆盖、适用性错误、生成状态循环失效、报告跨交付误绑定，N06～N10、N19。
- [ ] **C09 不通过**：任务书映射/总任务完成闸门缺失，CURRENT 与事实不一致；没有 T02 cleanGitCheckoutVerified=false 的正确总任务阻断实证。
- [ ] **C10 部分实现，未通过**：共同规则和示例存在；缺两端可用 Hook、能力检测、关闭/重开后的真实操作演练证据。
- [ ] **C11 不通过**：中断后长期 running，N20；失败任务状态不同步 N16；重复事件与恢复入口未完整交付。
- [ ] **C12 部分实现，未通过**：已有中文说明和基础命令；缺 PowerShell 包装、简化证据输入、可用恢复命令及完整日常操作演练。

§14 其余尚未执行的集成负例，包括真实 Unity 非零/XML 缺失及失败解析、真实占用下恢复重查、客户端重开/换模型恢复、T02 干净检出完成闸门等，均列为**尚待确认**，不能从 Node fixture 的结果推定通过。现有已复现阻断已足以拒绝本次交付。

## 5. Cursor 修复与重新交付要求

1. 优先修复受检对象固定、真实结果解析、完整 AC 闸门、终态不可覆盖，消除错误通过；再修复指纹边界和精确交付关联。
2. 修复非 demo 快照清单及并发事务，补齐 schema；加入同任务和跨任务真实双进程竞争、冲突无部分写入的检查。
3. 完成实际编译/slot/隔离 Unity 适配，处理未知占用、运行前重查与结果缺失；补恢复/取消/重复事件处理。
4. 完成任务书映射、总任务闸门、Hooks 能力提示、PowerShell 包装和用户日常操作演练。生成摘要必须以真实源记录为准。
5. 用新的 DEL 和新的 REV 留存修复交付及复验，不修改本次引用的旧 DEL/REV。复验需绑定新的明确源码范围和验证基线，重跑本报告检查及 §14 尚缺集成负例；不得只复跑原 13 项即宣布通过。

本轮不修工具代码，也不修改原手册的验收框。为避免已验证存在覆盖/错误通过问题的 finish 命令污染记录，本报告与证据独立保存，不通过该命令覆写原 REV。用户可将本报告直接交给 Cursor 开发修复；无需用户手写新的“通过证据”。

## 6. 保全验证

两次最终检查均确认以下内容前后哈希一致，详见结果 JSON 的 preservation：

- 生产 `Unity Project/Assets`、`Packages`、`ProjectSettings` 联合清单：`e108606412e05a72cd0ca905051bb23bc77620656b5a5624b4e615444e4ff9c9`。
- 原 DEL JSON：`abd914ee12a4f27a0c25cac453109a4f000c5a02cce0e5f8fcca569d78bacacd`。
- 原 REV JSON：`94b9eef5f1323a513f4f5bb9808e9aa2f2d2d971421c1f178dcbe74aae73d7de`。
- 游戏任务书：`0b2562677604c72d5094cee856764f2e3d2ba74d5b4b247d6910b962d2a51194`。

本轮新增交付物为本报告、验证脚本/结果/日志以及忽略目录中的隔离检查证据。原有大量修改和未跟踪文件均保留。
