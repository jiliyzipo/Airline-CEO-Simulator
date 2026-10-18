# 协作工具修复任务说明：FR-01～FR-03

日期：2026-10-18。编写：Codex。供 Cursor 直接按项开发。

依据：[第四轮独立验收报告](CollabTool_IndependentFourthAcceptance_2026-10-18.md)，受影响交付 `DEL-20261017150657-dd9483`。本文是对已有问题的细化，不是新一轮验收，也没有修改工具实现。

**目标：修复真实交付闭环和证据可靠性，不以测试套件总分代替真实入口验收。** 既有 19/24/12/8 组检查均已通过，blocked 语义也已验证，不应回退。下列新增验收共 6 项，F01/F02 通过，F03/F04/F05/F06 失败。

代码位置对应第四轮源码；修改后行号可能变化，以函数名为准。工具源码清单哈希为 `3f24a449533486f82dd926dff2a6c998d7cdd621564d01ba7743ec36d582aa89`。

## 1. FR-01：默认 smoke 失败，分成两个原因修复

### 1.1 已验证现象 A：实际快照含额外状态文件

**触发条件：** 对第四轮 DEL 的实际目录做副本，使用交付记录及原 smoke 配置执行 review-run。

**预期：** 完整性检查通过，启动交付规定的 smoke。

**实际：** 启动测试前退出 5，提示 `snapshot contains unlisted files`。清单里的 18 个文件哈希全部正确，但目录中另有 24 个未列文件，包括 `.collab-local/ack.json`、`docs/collaboration/CURRENT.md`、tasks、events、state。不同类别的文件是否被运行产物规则允许应分别判断；实际拒绝的是清单外协作状态文件，不能把“24 个额外文件”全部说成校验器拒绝的文件。

证据：[目录清点及哈希](verification/CollabTool-Fourth-Independent-2026-10-18/snapshot-inventory.json)、[F03 原始结果](verification/CollabTool-Fourth-Independent-2026-10-18/delivery-final.results.json)。

**直接原因，代码已验证：** `commands.mjs` 的 `verifySnapshotIntegrity()` 会核验文件集合，而本目录在清单之外已有协作状态。它拒绝执行是正确行为，不能通过放宽完整性规则来“修复”。

**额外文件的可能来源，尚待开发方核实：**

- `tools/collab/tests/collab.test.mjs:18–24` 从测试文件自身位置计算 repo，再运行该 repo 下的 CLI。
- 同文件约 212 行以后直接调用 init、sync、ack、claim、release。这些命令会生成协作状态和事件。
- 因此，若曾直接执行 `node out/collab/<DEL>/tools/collab/tests/collab.test.mjs`，该测试会把快照当作可写仓库，而不是只读输入，足以产生这类文件。
- `paths.mjs` 也从工具文件路径推导 REPO_ROOT，所以单纯改变 shell cwd 不能消除该问题。

以上解释的是**可由代码确认的污染路径**，不是认定历史上一定执行过该命令。第四轮驱动已使用 scaffold 隔离开发方自测，方向正确，但直接测试入口仍需防误用。

**建议改动：**

1. 不覆盖、不清理旧 DEL。修复后创建全新交付，由正常导出入口生成正式快照。
2. 开发方测试入口自身也应创建一次性 scaffold，并从 scaffold 中运行会写状态的 CLI；或者对直接在正式快照内运行可写测试明确拒绝、给出安全入口。不能只靠操作者记住“先改 cwd”。
3. 把“交付源码目录”和“检测运行状态/日志目录”明确分开。开发方自测、独立验收均不得向固定交付输入写 tasks/events。
4. 在发布交付记录前后检查清单文件哈希和额外文件集合，并输出两类结论；不能只打印“18 项哈希匹配”。

**完成标准：** 新 DEL 实际目录能通过完整性校验；自测前后固定输入及文件集合不变；人为在源码区新增未列 `.cs` 或协作状态文件仍会被拒绝；正常 bin/obj/Logs 产物规则不回退。

### 1.2 已验证现象 B：即使无额外文件，smoke 仍缺依赖

**触发条件：** 在全新 fixture 中只重建真实清单中的 18 个文件及 manifest，再运行原配置的 smoke。此操作未删改原快照。

**实际：** 完整性检查通过，smoke 进程退出 1；结果为 2 项检查、1 项失败，`cli-exists=true`，`protocol-exists=false`。见同一结果文件中的 **F06**。

**直接原因，代码已验证：**

- `tools/collab/tests/smoke-profile.mjs:16–18` 读取相对于执行 cwd 的 `docs/collaboration/PROTOCOL.md`。
- `tools/collab/config.json` 与 `config.example.json` 的 demoSnapshotRoots 仅有 `tools/collab`。
- `cmdDeliver()` → `taskRoots()` → `exportStableSnapshot()` 按该白名单导出，协议文档根本不在交付里。
- review-run 以快照根目录为 cwd，这是已修复且正确的行为。过去从主仓库运行时可能读到主仓库协议文件，切换到真正隔离执行后，遗漏依赖才暴露出来。

**推荐修改：** 在实际生效配置和示例配置中，都将所需的 `docs/collaboration/PROTOCOL.md` **作为精确输入文件**纳入演示快照。不要把整个 docs/collaboration 重新纳入运行输入，避免带回生成态。同步确认实现/验证指纹、导出清单与 smoke 依赖的口径一致。

若开发方决定改变 smoke 的检查目的，需要说明替代断言验证什么；不能只删掉 protocol-exists，以便减少失败数。最小修复通常是补齐输入依赖。

**完成标准：** 使用实际新 DEL 的原 profile，且不临时替换 command，smoke 退出 0，结果两项均通过；移除副本中的协议文件时仍失败。smoke 只证明其两项检查，不能用它直接覆盖任务的全部业务 AC。

### 1.3 为什么原套件没有发现

外部套件主要在 fixture 中配置专用 Node command，验证锁、状态和结果闸门；开发方测试又在 scaffold 中提供协议占位文件。因此它们没有执行“实际交付白名单 + 实际原配置 + 默认 smoke”这一组合。

**需保留一条真实交付正例：** 正常导出新 DEL → 原样复制到隔离运行位置 → 用原配置运行默认 profile → 检查真实退出码和结果。此正例应绑定实际新交付 ID，不只绑定工具代码路径。

## 2. FR-02：stdout 没有哈希绑定，修复证据存储和解析两端

### 2.1 精确复现

F04 的步骤如下，均在测试自己的 fixture 中进行：

1. 命令退出 0，但打印 JSON：`assertionsTotal=3, assertionsFailed=1, assertionsSkipped=0`。
2. review-run 完成，真实失败输出落盘。
3. 将该 stdout 文件内容改为 `assertionsFailed=0`，其余计数不变。
4. 提交与改后计数一致的 evidence，并将必测 AC 声明为 passed。
5. 当前 finish 输出 **passed/current**，没有发现 stdout 字节已变化。

这证明的是证据未固定，不是声称文件系统上的角色不可伪装，也不是声称用户原始日志真的被改过。

### 2.2 原因链，代码已验证

- `cmdReviewRun()` 约 1245 行创建 `run-<runToken>` 目录，并传出 COLLAB_RUN_DIR。
- 但约 1265 行实际仍写 `path.join(logDir, 'stdout.txt')` 和 stderr.txt，没有使用 runDir。
- runs[] 约 1290–1304 行只保存 stdoutPath/stderrPath，没有各自内容哈希和字节数。
- `parseRunResult()` 约 398–402 行在 finish 时直接重读 stdoutPath 当前内容。
- resultFile 分支会核对绑定 SHA256；没有 resultFile、以 stdout JSON/文本为结果的分支没有同等保护。

因此，校验计数与 evidence 一致只说明“改后的输出与声明一致”，不说明它是原始进程输出。顺序重跑同一 review 还会写同一日志路径，旧 runs[] 可正常地指向被覆盖后的新日志。

### 2.3 推荐实现顺序

1. **存储端：** 每次 run 的 stdout/stderr 实际写入 runDir，例如 `.collab-local/logs/<REV>/run-<token>/stdout.txt`，stderr 同理；使用排他创建或等价方式避免覆盖现有证据。
2. **记录端：** 将路径、SHA256、字节数、runToken、profile、deliveryId 绑定到同一 run。哈希针对写入文件的真实字节计算，解析使用同一份字节。
3. **验证端：** 在 parseRunResult/finish 中核对文件存在、长度及哈希。必须先验证绑定，再解析计数、匹配 evidence。stdout 与声明 resultFile 使用统一的证据核验逻辑，避免下一次只修到一种格式。
4. **失败端：** 文件缺失、缺绑定、哈希不符、runToken 不一致均不得 passed；保留原 run 和明确诊断，不用当前内容“补算新哈希”覆盖旧值。
5. **兼容端：** 旧 run 没有绑定时，保留历史报告原义；要重新判断当前交付，应使用新 REV。不要把旧日志重新哈希后当作已证明原始输出。
6. 若为用户方便保留 review 级“最新日志入口”，将其定义为可变导航；历史 runs[] 和机器解析始终引用具体 run 的固定文件。

记录字段名可自行选择，但建议统一结构，例如：

```json
{
  "runToken": "本次唯一标识",
  "stdout": {"path": "logs/REV/run-token/stdout.txt", "sha256": "64位哈希", "bytes": 123},
  "stderr": {"path": "logs/REV/run-token/stderr.txt", "sha256": "64位哈希", "bytes": 0}
}
```

此为结构示意，不是可直接提交的 evidence。改 schema 时同步调整生成端、消费端、文档与测试。

### 2.4 完成标准

- [ ] 有效原始 stdout、正确绑定和匹配 evidence 可以通过。
- [ ] F04 修改 stdout 后，finish 非零，明确指出证据变化，报告不通过。
- [ ] stdout 缺失、截断、缺绑定均不能通过；空 stderr 可以被正确记录及验证。
- [ ] 同一 review 顺序执行两次，两个 runToken 不同、日志路径不同；第二次完成后第一次日志及哈希保持原样。
- [ ] 两次计数刻意不同，解析各自得到各自计数，不能都指向最后一次结果。
- [ ] 已有终态、blocked、重复执行租约、取消状态和 resultFile 改写保护仍通过回归。

## 3. FR-03：NUnit 只提取根标签，必须验证完整 XML 文档

### 3.1 精确复现

F05 的本次进程生成文件内容只有下面一行，之后正常退出；文件确实由该 run 产生，且绑定哈希正确：

```xml
<test-run total="1" passed="1" failed="0" skipped="0" result="Passed">
```

根标签没有闭合。当前 parseNunitCounters() 仍返回通过计数，finish 得到 passed/current。

### 3.2 原因链，代码已验证

- `commands.mjs:247–286` 的 parseNunitCounters() 用正则搜索 `<test-run ...>` 的**开始标签**。
- 获取开始标签后只提取属性，再正则扫描 test-case；没有执行 XML 文档语法解析。
- 没有 test-case 时，扫描结果是空数组，不会报告文档被截断。
- resultFile 分支在函数返回非空计数后就调用 judgeCounters()。哈希仅保证“解析的是刚才那份截断文件”，不能保证其语法有效。

第四轮已修复“属性缺失变零”和“根汇总掩盖 Failed 明细”这两类问题，应保留；它们并没有覆盖“文件本身不是合法完整 XML”。

### 3.3 推荐修改

1. 选择具备严格 XML 文档校验能力的解析方式。先验证整份字节可被解析为一个完整文档，再读取 test-run 和 test-case；若解析库默认宽松，必须显式执行严格校验。
2. 要求唯一、正确的根节点及必要结构。缺闭合标签、错误嵌套、多个根、重复属性等语法错误直接拒绝；不能因为某个片段可匹配就继续。
3. 从解析后的节点读取必需属性，执行非负安全整数、有效状态、非全跳过及失败数约束；计数过大不能静默失去精度。
4. 根汇总与明细一致性以项目实际 NUnit 结果样本为契约。若框架有其他状态，明确其处理规则，不能为通过某个简化 fixture 而错误假定所有结果只有 Passed/Failed/Skipped。
5. 保留对 resultFile 的本次运行绑定和哈希检查；XML 解析失败时不得回退到 stdout 的成功标记。
6. 如果引入解析依赖，同步处理依赖锁定、隔离快照可用性和干净环境安装；不能只在开发机已有 node_modules 时通过。

不要只追加 `text.includes('</test-run>')` 检查。错误闭合、注释中的伪结束标签或重复根仍可绕过这种修补。

### 3.4 完成标准

- [ ] 项目真实、完整、通过的 NUnit XML 样本可被正确解析；记录样本来源和预期计数。
- [ ] F05 截断文档不能通过。
- [ ] 错误嵌套、重复根、缺属性、非法计数和失败明细不能通过。
- [ ] 完整但报告失败/只有跳过的 XML 不能通过；合法通过样本仍能通过。
- [ ] 与 stdout 声称通过相冲突的非法 resultFile 仍拒绝，不能兜底。
- [ ] 原第三轮 T02/T03/T04/T05、本轮 F05 以及 resultFile 绑定测试全部通过。

## 4. 建议开发顺序与交付方式

1. 先补齐默认 smoke 的输入依赖和自测隔离，建立**真实新 DEL 正例**。这一步让后续验证不再只依赖临时命令。
2. 再统一 run 证据的独立存储、内容绑定和解析前校验，修 FR-02。
3. 接入完整 XML 校验并补正反例，修 FR-03。
4. 创建全新 DEL，运行既有三套外部检查及本轮 F01～F06；显式核对每套 subjectToolsPath 和源码哈希。
5. 针对实际新 DEL 更新 F03/F06 的交付引用。这两个用例目前固定引用第四轮 DEL，**仅改变 COLLAB_AUDIT_TOOL_SOURCE 不会把它们自动变成新 DEL 的交付测试**。
6. 交付单逐项写：修改位置、正例结果、负例结果、日志路径/哈希、仍未完成范围。不能只提供“全部测试通过”的总分。

复用检查入口：[acceptance-boundaries.mjs](verification/CollabTool-Fourth-Independent-2026-10-18/acceptance-boundaries.mjs)。第四轮原始结果：[新 DEL](verification/CollabTool-Fourth-Independent-2026-10-18/delivery-final.results.json)、[工作区](verification/CollabTool-Fourth-Independent-2026-10-18/current-final.results.json)。

既有套件正例应明确断言最终 status=passed，负例应明确断言拒绝原因/退出码；不要只用“不抛异常”或“不是 current”判断正例通过，否则终态 blocked 也可能让测试看似全绿。

## 5. 仍未完成的范围与下一轮授权

C07 真实 Unity 串联、C09 权威任务书映射与总完成闸门、C10 Hook 端到端/去重、C12 完整用户流程仍未完成；修完 FR-01～FR-03 不自动意味着这些条款完成。它们也不能被合并进三项缺陷的“关闭”结论。

用户已于本轮明确授权：**如果下一轮独立验收仍不通过，由 Codex 直接修正问题。** 下一轮先进行独立验收；若协作工具仍有可复现缺陷，Codex 将在现有修改基础上直接修复、补测试并复验，不再仅出退回报告，也不就相同修复范围重复申请许可。

该授权用于协作工具及其必要测试、配置和文档；不擅改游戏业务代码，不重置/清理，不擅自提交/推送。真实 Unity 测试仍须检查占用，关闭用户实例仍须取得授权。工具缺陷修复后的验证应明确标注“Codex 修复并自测”，不能冒充另一方独立验收。

本文清单默认未勾选，供修复时逐项填写证据。本文编写期间未修改工具实现、游戏代码、游戏任务书或历史验收结果。
