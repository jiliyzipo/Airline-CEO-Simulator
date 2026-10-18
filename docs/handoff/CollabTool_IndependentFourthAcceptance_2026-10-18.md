# 协作工具第四轮独立验收报告

验收日期：2026-10-18（Asia/Shanghai）。验收方：Codex。编号：ACCEPT-COLLAB-20261018-CODEX-04。

**结论：既有回归全部通过，blocked 新语义验证通过；本次交付整体仍不通过。** 补充验收发现三个问题：实际交付不能直接运行默认 smoke、stdout 证据未绑定哈希、截断 NUnit XML 可判通过。C07/C09/C10/C12 继续保持未完成。

## 1. 对象、操作与证据范围

- 交付：[DEL-20261017150657-dd9483](../collaboration/deliveries/DEL-20261017150657-dd9483.json)，快照 `out/collab/DEL-20261017150657-dd9483`。
- 开发说明：[CollabTool_Delivery_Fourth_2026-10-17.md](CollabTool_Delivery_Fourth_2026-10-17.md)。上轮报告：[CODEX-03](CollabTool_IndependentThirdAcceptance_2026-10-17.md)。
- 标准：[需求与操作手册 §14](../Development%20Manual/Development%20Task%20Book/Task%20book/Cursor_Codex_协作工具_开发需求与操作手册_v1.0_2026-10-17.md#14-mvp-交付与验收清单)，结合 §6.3 结果文件路径/哈希、§7 固定快照及 §14 C06 的真实交付闭环。
- 已执行根项目 sync，再 ack revision **69**。生成状态正常更新；没有更改真实游戏任务书和任务验收结论。
- 工作区与新 DEL 工具源 18 文件清单哈希均为 `3f24a449533486f82dd926dff2a6c998d7cdd621564d01ba7743ec36d582aa89`；交付整体实现指纹为 `efdddc5f2a11639ad3454a21b6092114b8d692f244bc0b4f2e475e82d28fdd1c`，两者范围不同。

检查全部串行。驱动脚本经阅读后使用独立 label `codex-20261018-delivery/current`；每套显式覆盖 COLLAB_AUDIT_TOOL_SOURCE，未依赖 shell 旧变量。新增行为检查只操作隔离 fixture，包括实际快照的副本和仅清单重建副本。没有清理原快照，没有启动 Unity，没有修改实现或游戏业务代码，没有提交/推送/重置。

## 2. 代码已验证：执行结果

工作区与新 DEL 分别执行，结果一致：

- 开发方自测 **19/19** 通过。
- 原检查 **24/24** 通过。
- 补充检查 **12/12** 通过。
- 第三轮边界 **8/8** 通过。
- 本轮补充验收 **6 项中 2 项通过、4 项失败**，退出 1，无 testError。4 个失败用例归并为下述 3 个问题。

计数不可简单相加：原 24 项本身包含执行整组开发方测试。既有套件部分正例只观察是否异常或 freshness，不要求首次 finish 真正 passed；因此本轮额外明确检查了有效 JSON 的通过正例与 blocked 终态，避免把 blocked 当作成功闭环。

独立证据：

- [新 DEL 既有四组汇总](verification/CollabTool-Fourth-2026-10-17/evidence/codex-20261018-delivery/summary.json)、[工作区既有四组汇总](verification/CollabTool-Fourth-2026-10-17/evidence/codex-20261018-current/summary.json)。同目录保留各组 stdout/stderr 和原始结果。
- [新 DEL 本轮六项结果](verification/CollabTool-Fourth-Independent-2026-10-18/delivery-final.results.json)、[工作区本轮六项结果](verification/CollabTool-Fourth-Independent-2026-10-18/current-final.results.json)。
- [新增验收脚本](verification/CollabTool-Fourth-Independent-2026-10-18/acceptance-boundaries.mjs)、[实际快照清单及额外文件核对](verification/CollabTool-Fourth-Independent-2026-10-18/snapshot-inventory.json)。

原套件结果中的旧交付引用字段未改写；本轮受检版本以 subjectToolsPath、文件哈希和本报告交付绑定为准。较早的本轮 exploratory 结果保留，最终结论采用 final.results.json；最终 F03 已把工具拒绝作为明确观察捕获，不把它记作测试脚本异常。

## 3. 已验证通过的修复

认可上轮 TH-01～TH-06 所列具体负例的修复：严格格式拒绝、缺计数/失败明细识别、旧 resultFile 拒绝、运行中源码变化拒绝、同 review 双进程租约与允许编译产物复验的既有用例均通过。Unity 目录与 resultFile 配置从静态代码看也已对齐，真实 Unity 仍未执行。

新增 F01 用有效机器 JSON、完整必测 AC 与一致计数，明确得到 **passed/current，unverified=false**。

新增 F02 对无显式机器结果契约的未知输出，确认 **blocked、退出码 6、finishedAt 存在、freshness=unknown**；再次 finish 返回 **3**，随后 sync 不改变 blocked 报告字节，任务仍 unknown。用户说明中的该语义已独立验证，不能再按上一轮的 unverified passed 缺陷报告。

## 4. 尚存问题及修复要求

### FR-01：真实交付默认 smoke 无法走通（阻断；F03、F06）

交付的 **18 个清单文件哈希确实全部匹配**，但这不等于快照完整目录没有额外文件。实际目录还包含清单外 `docs/collaboration/CURRENT.md`、events、tasks、state 等生成记录，详见 inventory。F03 原样复制该目录到 fixture 后，review-run 在完整性检查处退出 **5**，拒绝这些未列文件。这一拒绝行为本身正确，问题是当前交付目录已不能直接作为所声明的固定交付执行。未推断额外文件由谁或哪一步产生，未清理它们。

为区分目录额外文件与输入依赖，F06 在全新 fixture **只重建真实交付清单的 18 文件与 manifest**，不复制额外文件，使用原配置的 smoke 命令。此次能启动，但进程退出 **1**，输出 `assertionsTotal=2 / assertionsFailed=1`，其中 `protocol-exists=false`。

原因：`tools/collab/tests/smoke-profile.mjs:18` 需要 `docs/collaboration/PROTOCOL.md`，而 demoSnapshotRoots 仅包含 `tools/collab`，该依赖没有进入交付。现有三套外部检查主要使用自定义 fixture 命令，故不会发现默认 profile 的缺依赖。

要求：用新的 DEL 生成洁净且完整的固定交付，不覆盖或清理旧交付；明确纳入 smoke 必需的协议输入，或合理调整并说明 smoke 的真实检查范围。需在**实际新 DEL 副本上使用原配置**完成默认交付→run→结果判定的正例，不以临时替换命令代替。不要简单取消完整性检查或抹掉失败断言。

### FR-02：stdout 结果没有内容绑定，后改日志可将失败变通过（阻断；F04）

位置：`tools/collab/lib/commands.mjs:398–402、1265–1268、1290–1304、1485`。

F04 的命令退出 0，但真实 stdout JSON 含 `assertionsFailed=1`。命令结束后在 fixture 中将该 stdout 文件替换为失败数 0，提交一致 evidence，finish 仍得到 **passed/current**。针对声明 resultFile 的 SHA256 保护没有覆盖 stdout JSON/文本适配器。

代码同时创建了 `run-<runToken>` 目录，却仍将 stdout/stderr 写在同一 review 的固定 `stdout.txt/stderr.txt`。顺序重跑同一 review 时，旧 run 指向的日志路径也可能被新输出替代，这是日常工作流的证据保全问题，不是角色身份认证结论。

要求：所有机器证据（含 stdout/stderr）按 runToken 使用独立、不可覆盖路径；run 结束保存字节数与 SHA256，finish 核对后再解析。历史 runs 必须一直指向各自原始输出。应同时测试手动改写和合法顺序重跑后旧证据仍不变。对应 §6.3 的证据路径/哈希和 C08 历史保全要求。

### FR-03：NUnit 用正则提取根标签，仍接受截断 XML（阻断；F05）

位置：`commands.mjs:247–286、444–452`。

F05 本次运行产生的完整文件内容仅为：

```xml
<test-run total="1" passed="1" failed="0" skipped="0" result="Passed">
```

缺少闭合根标签，不是完整 XML 文档。结果绑定检查、哈希和计数都存在，但正则仍返回有效计数，finish 判为 **passed/current**。这不是上轮已修复的缺属性问题，而是结果文件结构本身未被验证。运行中断或异常写入可能留下这种内容，结果文件“存在且本次有变化”不能替代格式有效性。

要求：使用完整 XML 文档解析，拒绝截断、错误嵌套、重复根、非法属性/计数关系；解析成功后再检查 test-run 与明细。保留目前已通过的缺字段/失败明细负例，添加有效完整 NUnit XML 正例及截断负例。不得用只补一个 closing-tag 字符串检查代替 XML 解析。

## 5. §14 与未完成范围

本轮可以确认“既有 24+12+8 用例回归通过”以及 blocked 语义通过，不能确认“完整协作工具验收通过”。

- C02 的基础命令及既有 C01/C03/C04/C05/C11 相关回归：本轮未见这些已测用例回退，保留通过证据；不扩展为全部条款已穷尽。
- **C06：未通过。** 实际交付默认 profile 失败，FR-01。
- **C07：未完成。** 真实 Unity 串联、占用恢复仍没有本轮执行证据；FR-03 的 XML 接受闸门也需修复。
- **C08：未通过。** stdout 证据可被后续更改或重跑覆盖，FR-02。
- **C09/C10/C12：未完成。** 动态任务书映射/总完成闸门、Hook 端到端/去重、完整用户操作演练仍与交付声明一致，尚待实现或实测。
- `cleanGitCheckoutVerified=false` 保留原意；本轮没有做干净检出，不把它写为已验证，也不更新游戏 T02 状态。

不更改手册验收框，不覆盖根项目原 REV，不把本报告写成游戏功能回归结论。

## 6. 复验方法和保全

根目录执行，每次显式设置 subject；脚本只创建 fixture，不修改原快照、不启动 Unity：

```powershell
$env:COLLAB_AUDIT_TOOL_SOURCE = 'out/collab/DEL-20261017150657-dd9483/tools/collab'
$env:COLLAB_AUDIT_RUN_LABEL = 'fourth-final-recheck'
node docs/handoff/verification/CollabTool-Fourth-Independent-2026-10-18/acceptance-boundaries.mjs
```

F03/F06 专门绑定本次真实 DEL，以确保验证原交付；下一次新交付需要显式更新这两个用例引用，不能仅换工具源码环境变量后就声称验证了新 DEL 的目录。

最终两份结果均记录生产 Assets/Packages/ProjectSettings 联合清单前后相同：`e108606412e05a72cd0ca905051bb23bc77620656b5a5624b4e615444e4ff9c9`。原 DEL/REV、第二轮/第三轮/第四轮 DEL、游戏任务书均逐一保全，详见 preservation。第四轮 DEL JSON SHA256 为 `c6df5f36ddc987b761c1bc4e16288de04626d6e71c08138de4e89cdc874696ad`。

工具实现未修改；历史证据及实际快照额外文件均保留。新增交付物仅为独立报告、检查脚本和证据。
