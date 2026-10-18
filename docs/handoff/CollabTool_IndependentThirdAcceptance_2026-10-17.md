# 协作工具第三轮独立验收报告

日期：2026-10-17。验收方：Codex。编号：ACCEPT-COLLAB-20261017-CODEX-03。

**结论：已有两组回归全部通过；新交付仍未达到 §14 整体验收通过条件，需继续修复。** 本轮新增边界检查 8 项均复现缺陷，两份受检代码结果一致。不得以 24/24 与 12/12 代替完整验收。

## 1. 对象和方法

- 新交付：[DEL-20261017122743-589b23](../collaboration/deliveries/DEL-20261017122743-589b23.json)，快照 `out/collab/DEL-20261017122743-589b23`。
- 开发说明：[CollabTool_Delivery_Third_2026-10-17.md](CollabTool_Delivery_Third_2026-10-17.md)。上轮独立报告：[CODEX-02](CollabTool_IndependentReacceptance_2026-10-17.md)。
- 依据：[需求与操作手册 §14](../Development%20Manual/Development%20Task%20Book/Task%20book/Cursor_Codex_协作工具_开发需求与操作手册_v1.0_2026-10-17.md#14-mvp-交付与验收清单)，以及 §6.3 的真实结果/文件哈希、§7.3 的固定快照和 §14 C05 的不重复执行要求。
- 先核对交付/代码/配置，按 AGENTS.md 完成根项目 sync、ack revision 51，再**串行**复跑新快照与工作区的两组回归；新增检查同样串行。全部行为检查在专用 fixture 中执行，无真实 Unity 调用。
- 工作区与快照的 `tools/collab` 18 文件清单哈希一致：`4de91eb36478a57ee706d57a78ac5688e7bfd65d7a51956481f10ce7a0e11104`。它与新 DEL 整体实现指纹 `03af17c8136c52ef292f4420f5558bd72b9480c39b00d8c41c0dca9c693acc6c` 是不同范围。
- 实际新 DEL 的 18 项快照文件均与交付清单相符。不存在“发现这份真实快照已被篡改”的结论；以下篡改/旧结果均是隔离 fixture 的测试输入。

本轮未修改 `tools/collab` 实现或生产业务代码，未启动/关闭用户 Unity，未执行完整游戏自测，未提交/推送/清理。根项目 sync/ack 更新了工具生成摘要、确认和事件；真实游戏任务书及历史 DEL/REV 均保留。

## 2. 代码已验证：真实执行结果

新快照和当前工作区分别得到相同结果：

- 开发方测试：13/13 通过。
- `baseline24-rerun.mjs`：24/24 通过，退出 0。
- `supplemental.mjs`：12/12 通过，退出 0。
- 本轮 `edge-cases.mjs`：0/8 通过、8 项失败，退出 1，无 testError。

其中原 24 项包含开发方整组测试一项，不能把这些计数直接相加作为独立用例总数。初次根 sync 在受限沙箱出现 EPERM，随后排除沙箱文件替换限制复跑成功，没有把环境限制计作产品缺陷。

证据：

- [快照原 24 项](verification/CollabTool-Third-2026-10-17/delivery-baseline24-results.json)、[工作区原 24 项](verification/CollabTool-Third-2026-10-17/current-baseline24-results.json)。
- [快照补充 12 项](verification/CollabTool-Third-2026-10-17/delivery-supplemental-results.json)、[工作区补充 12 项](verification/CollabTool-Third-2026-10-17/current-supplemental-results.json)。
- [快照新增 8 项](verification/CollabTool-Third-2026-10-17/delivery-edge-results.json)、[工作区新增 8 项](verification/CollabTool-Third-2026-10-17/current-edge-results.json)。
- [快照开发方测试输出](verification/CollabTool-Third-2026-10-17/delivery-developer-tests.stdout.txt)、[工作区开发方测试输出](verification/CollabTool-Third-2026-10-17/current-developer-tests.stdout.txt)。
- [新增检查源码](verification/CollabTool-Third-2026-10-17/edge-cases.mjs)。全部 fixture 和原始结果还保存在 `.collab-local`。

原回归输出的 referencedDelivery/referencedReview 字段仍硬编码旧轮 ID，原始结果没有改写；本轮对象以 subjectToolsPath、文件清单哈希和本报告绑定的新 DEL 为准。

## 3. 已验证的修复范围

认可这些局部修复结果：S01～S03 的失败计数、空 stdout、非法 evidence 数值会被拒绝；S04/S12 的交付生成态与工具测试指纹边界通过；S05/S06 的运行前设置/新增文件检查通过；S07 其他 review 锁未被误释放；S08 运行中取消后的报告终态能保留；S09 sync 插入 claim 的既有交错用例通过；S11 的四类非法结构被拒绝。N 系列旧回归亦全部通过。

这些结论有明确边界：空 stdout 被拒绝不证明任意非空 stdout 就是结果；运行前快照校验不证明运行中输入未变；分段 review 写锁不等于同一任务仅执行一次；数个 schema 负例通过不代表每个字段和路径边界均已穷尽。

## 4. 尚存缺陷及修复要求

### TH-01：未知结果仍可判通过，声明的结果格式没有强制执行（阻断；T01、T02）

位置：`tools/collab/lib/commands.mjs:220–324、1100–1104`。

- T01：JSON profile 只输出 `arbitrary non-result text`，没有机器断言，仍能 finish 为 `passed/current`，同时 `machineResult.unverified=true`。标记“未验证”没有阻断通过。
- T02：声明的 resultFile 存在，但内容是 `this is not XML`；解析失败后回落到 stdout 的通过 JSON，仍通过。

要求：按 profile 强制验证其结果格式和成功条件；未知、畸形、格式不匹配及 unverified 只能 blocked/failed，不能 passed。已声明 resultFile 就必须成功解析该文件，不可用其他宽松来源绕过。compile/slot 若输出文本，应有明确且经测试的适配器，不能泛化为“没有失败字样就通过”。对应原 RQ-01，尚未完整关闭。

### TH-02：NUnit 缺字段被补成零，失败明细和汇总一致性未检查（阻断；T03、T04）

位置：`commands.mjs:177–209、235–236`。

- T03：`<test-run total="1" result="Passed"/>` 缺 passed/failed/skipped，attr 返回 null，Number(null) 变成 0；解析结果出现 total=1、passed=0、failed=0、skipped=0，仍获通过。
- T04：根汇总声称通过，但含 `<test-case result="Failed"/>`，仍通过。

要求：使用可靠 XML 解析，必需属性缺失/空值直接拒绝；检查执行通过数、实际失败用例及合理计数关系，不能仅正则提取根标签。JSON/NUnit/文本各格式独立负例覆盖。此处不是 `Number(undefined)`，而是新增 XML 路径的 `Number(null)`，不能视为已修复。

### TH-03：结果文件没有绑定到本次执行，可复用旧 XML（阻断；T05）

位置：`commands.mjs:229–236、953–969、1190–1200`。

T05 把旧 Passed XML 预先放进 fixture 源和交付快照，本次命令未生成任何 XML，仅打印一行文字；finish 仍读取旧 XML 并通过。没有唯一 run 输出目录、生成证明或本次运行关联，文件存在不足以说明测试执行过；报告亦未保存结果文件内容哈希以固定证据。

要求：每次 run 使用独立结果路径，确认结果由本次进程产生并绑定 runId/profile/交付/验证基线；保存路径和 SHA256，结束后不能用后来更改或前次遗留结果替代。对应 §6.3 结果文件及哈希要求。

### TH-04：执行期间改变快照代码，仍可为原交付盖通过（阻断；T06）

位置：`commands.mjs:915、944–969、1173–1204`。

T06 的测试命令在运行中把快照 `src/model.cs` 改为 `mutated-during-run`，然后输出有效通过 JSON。run 和 finish 均未拒绝，报告仍 `passed/current`；结束时只重算主工作区，漏了真正执行的快照变化。

要求：固定不可变输入或运行前后核对受检源码集合/哈希，finish 检查实际受检快照。源码发生变化必须失败，不可只标主工作区 current。对应原 RQ-03/C04，不能只修运行前盲区。

### TH-05：同一 review 的两个进程仍会重复执行命令（阻断；T07）

位置：`commands.mjs:884–886、919–929、944–951`。

第一进程写 running 后，启动第二进程运行相同 review。当前只拒绝有 finishedAt 的终态，不拒绝已有活跃 worker；run-start 写锁在执行前即释放。两个测试自有进程各执行一次，执行计数为 **2**，报告 runs 为 **2**。这不是 Hook 配置问题，而是命令运行闸门不具幂等/排他性。

要求：对 active run 保持执行租约/锁或原子 CAS，重复 run 应返回现有运行状态或明确冲突，不得重复启动。以 run token 保护写回、取消和锁释放。不能只保护 JSON 写入段。对应 C05 的“双进程竞争不重复执行”，也影响 C11。

### TH-06：编译产物被当成非法源码，阻断同交付再次检测（高；T08）

位置：`commands.mjs:143–159`。

T08 第一个 review 的测试命令产生 `tools/semcheck/obj/build-output.txt` 并正常结束；对同一 DEL 开始第二个 review，运行前校验直接退出 5：`snapshot contains unlisted files: tools/semcheck/obj/build-output.txt`。

该用例仅用 Node 模拟产物，没有运行实际编译或 Unity；实际编译会生成 bin/obj，Unity 会生成 Library/Logs 等。应保留对新增源码的拒绝，同时区分运行产物：将产物/结果放独立 run 目录，或对同一固定输入生成新执行副本。不能简单放开所有未列文件，也不应要求用户清理快照来复验。对应 C04/C06/C07 集成边界。

## 5. 文档记载及尚待确认

**文档明确承认的未完成项：** C07 真实 Unity 串联、C09 权威任务书动态映射与总完成闸门、C10 客户端 Hook 端到端和去重、C12 完整用户日常流程。本轮认可这种区分，不把这些缺口误写为已经验收。

**代码静态确认：** 新增了两个 Unity profile、resultFile、Hook verified:false 告警和计数辅助参数。但是 Unity profile 在快照根 cwd 下传 `-ProjectPath .`，而正常快照项目位于 `Unity Project/`；RunUnityTests.ps1 从传入目录直接找 `ProjectSettings/ProjectSettings.asset`，resultFile 配置也缺 `Unity Project/` 前缀。入口目录口径尚未对齐。专用 C-DEMO 只交付工具文件，不能作为实际 Unity 工程交付。

**尚待确认：** 真实 compile/slot/隔离 Unity 执行、XML 绑定、真实占用恢复、两端 Hook 与模型/会话重开、真实任务书变更映射、T02 的 cleanGitCheckoutVerified=false 总完成闸门及完整中文操作演练。由于总完成闸门尚未实现，“T02 已被程序闸门阻断”只能视为设计要求/开发方声明，不能声称本轮已验证。游戏 T02 的真实状态不在本次工具验收中改动。

## 6. §14 判定

- **C02：已测的基础命令路径通过。** init/doctor/status/sync/ack、重复 init 和无变化 sync 均有执行证据。该局部结论不表示并发运行整体已通过。
- **C01、C03：本轮既有结构/指纹回归通过；整体仍待完整条款覆盖。** 不把有限负例扩展为 schema 全字段、资源/meta/删除/包锁全部验收完成。
- **C04/C05/C06/C08/C11：不通过。** TH-03～TH-06 的输入、证据、重复执行和复验问题仍阻断可靠闭环。
- **C07/C09/C10/C12：未完成。** 与开发方列出的剩余范围一致，且 C07 有目录/结果适配问题待修。

本轮不更改需求手册的勾选，不覆盖任何历史 REV。已验证通过的局部修复保留，本交付整体验收仍为 failed。

## 7. 下一次交付与复现

优先按“严格 profile 结果适配 → 每次运行结果绑定 → 固定输入与独立产物目录 → 活跃 run 排他状态机”收口，而不是逐个文本特判。正例 fixture 也应提供真实机器结果；将其从任意文本改为有效 JSON 是补齐前置条件，不能反过来保留 unverified 通过以迁就旧正例。

保留新旧 DEL/REV，用新 DEL 再交付。重跑两组既有回归与本轮边界检查，并按 C07/C09/C10/C12 清单继续提供真实证据。需启动真实 Unity 时先检查占用，关闭用户实例须另获授权。

仓库根复现，输出仍只写隔离目录：

```powershell
$env:COLLAB_AUDIT_TOOL_SOURCE = 'out/collab/DEL-20261017122743-589b23/tools/collab'
$env:COLLAB_AUDIT_RUN_LABEL = 'third-boundary-recheck'
node docs/handoff/verification/CollabTool-Third-2026-10-17/edge-cases.mjs
```

## 8. 保全

两份代码的原 24 项、12 项及新增 8 项检查均记录生产目录保全一致：`e108606412e05a72cd0ca905051bb23bc77620656b5a5624b4e615444e4ff9c9`。原 DEL/REV、上轮 DEL、游戏任务书的 SHA256 与上轮报告一致，详见结果 preservation。

本轮第三次 DEL JSON 的 SHA256 为 `8d909d5ea9d6859754d08615a1b1125ccdbd8726b00a5cc97845b415103ee02a`，另行核对保持不变。源码工具哈希同样未变。新增文件仅为本报告、测试和证据，不修实现，不改真实游戏验收状态。
