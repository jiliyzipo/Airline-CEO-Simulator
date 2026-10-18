# 协作工具 FR-01～FR-03 修复任务 · 开发方核对回执

日期：2026-10-18。编写：Cursor。对应任务说明：[CollabTool_Cursor_修复任务说明_2026-10-18.md](CollabTool_Cursor_修复任务说明_2026-10-18.md)；对应验收报告：[第四轮独立验收](CollabTool_IndependentFourthAcceptance_2026-10-18.md)。

本轮**只新增本文档**：未修改工具实现、未修改游戏业务代码、未修改任务书、未启动 Unity、未提交/推送、未清理任何快照或历史记录。

## 0. 结论

1. 任务说明中的代码位置与原因链，我逐条核对**未发现失实描述**（明细见 §1）。
2. 我独立复现了本轮六项检查，结果与报告一致：**F01、F02 通过，F03～F06 失败（2/6）**，复现命令与结果文件见 §4。
3. 任务说明中唯一标为“尚待开发方核实”的一项（FR-01 额外文件来源），现已**可以确认为已查明**，并定位到具体一次运行（§2）。结论不是“来源不明”，而是“由开发方自测入口在交付目录内执行造成的一次性污染，且该入口仍可再次触发”。
4. 因此 FR-01 的修法不需要放宽完整性检查：拒绝行为是正确的，需要修的是**输入依赖**与**自测入口的写入隔离**。

## 1. 逐条核对（代码位置与验证方式）

| # | 任务说明断言 | 当前位置 | 我的核对 | 结论 |
|---|---|---|---|---|
| 1 | smoke 按执行 cwd 读取协议文件 | `tools/collab/tests/smoke-profile.mjs:13–18`（`path.resolve('docs/collaboration/PROTOCOL.md')`） | 读取源码 | 确认 |
| 2 | 演示快照白名单只有 `tools/collab` | `tools/collab/config.json`、`config.example.json` 的 `demoSnapshotRoots` | 读取两份配置，均为 `["tools/collab"]` | 确认 |
| 3 | 协议文档不在交付里 | `taskRoots()` → `exportStableSnapshot()` | 交付清单 18 个条目全部位于 `tools/collab/` 下，无 `docs/` | 确认 |
| 4 | 建了 `run-<token>` 目录却仍写固定日志 | `commands.mjs` `cmdReviewRun()`：`runDir` 约 1245 行创建，`stdoutPath` 约 1265 行写 `path.join(logDir,'stdout.txt')` | 读取源码 | 确认 |
| 5 | `runs[]` 无内容哈希 | `cmdReviewRun()` run 对象约 1290–1304 行 | 只有 `stdoutPath`/`stderrPath` | 确认 |
| 6 | finish 时重读 stdout 当前内容 | `parseRunResult()` 约 398–402 行 | 读取源码 | 确认 |
| 7 | 只有 `resultFile` 分支有 SHA 绑定 | `parseRunResult()` resultFile 分支 vs stdout 分支 | 读取源码 | 确认 |
| 8 | NUnit 只取开始标签，不做文档解析 | `parseNunitCounters()` 约 247–286 行 | 读取源码；无 `</test-run>` 要求 | 确认 |
| 9 | 测试入口可把任意目录当可写仓库 | `tools/collab/tests/collab.test.mjs:17–18` 由 `__dirname` 上推三级得 `repo`；`run()` 固定 `cwd: repo` | 读取源码 | 确认 |
| 10 | F03/F06 绑定第四轮 DEL | `acceptance-boundaries.mjs` 约 58、75 行的 `deliveryId='DEL-20261017150657-dd9483'` | 读取源码 | 确认（换 `COLLAB_AUDIT_TOOL_SOURCE` 不会改变它们测的交付） |

## 2. FR-01 额外文件来源：已查明（把“推断”升级为“已证实”）

**清点（复核）**：`out/collab/DEL-20261017150657-dd9483` 清单 18 项，目录内未列文件 24 个：

- `docs/collaboration/` 23 个：`CURRENT.md`、`NEXT_CODEX.md`、`NEXT_CURSOR.md`、`state.json`、`tasks/` 7 个、`events/` 12 个；
- `.collab-local/ack.json` 1 个。

其中 `.collab-local` 已在 `ARTIFACT_DIR_NAMES` 豁免名单内，所以**实际被 verifySnapshotIntegrity 拒绝的是 23 个 `docs/collaboration` 文件**，与报告描述一致。清单 18 项哈希我逐一复核，0 不符。

**已证实链路**：

1. 12 个事件文件的内容为：`init created=17` → `init created=0` → `sync revision=2` → `ack` → `claim C-DEMO` → `release C-DEMO`（最后两个 claim/ack 交替出现，共 12 个事件），actor 均为 `cursor`。
2. 时间戳集中在 `2026-10-17T15:07:05.883Z` ～ `15:07:09.796Z`。
3. 该目录内**最新**写入时间即 `15:07:09`，此后没有任何写入；15:11 之后的三轮验证（含本轮 F03/F06 复现）只写 `.collab-local` 下的 fixture。
4. 该时间点对应开发方在第四轮用 `--label delivery` 第一次跑套件：初版驱动把 `script` 直接指向快照内的 `tools/collab/tests/collab.test.mjs`，而测试按自身位置推导仓库根（§1 #9），于是 `init/ack/claim` 全部写进了交付目录本身。
5. 该驱动随后已改为一次性 scaffold，第二次 `--label delivery` 才得到 19/19，且没有再向快照写入（与第 3 点的时间证据一致）。

**边界说明**：这解释的是“这批文件由哪一次运行产生”，不推断更早的历史；也不改变“旧快照已不可作为固定交付直接执行”的结论。修复方向仍是新建 DEL + 入口隔离，而不是放宽校验或清理旧目录。

## 3. 修复口径的一个硬约束（建议动手前先定）

第四轮交付的 18 个清单文件**全部在 `tools/collab/` 下，不含 `node_modules`、不含 `docs/`**。隔离快照只按白名单复制并逐文件校验哈希，因此：

- 若 FR-03 引入第三方 XML 解析依赖，快照内默认**拿不到**该依赖；要么把它 vendor 进白名单目录并计入清单哈希，要么实现零依赖的严格校验；
- 同理，FR-01 若把协议文件加进白名单，必须写成**精确文件条目**，不能把整个 `docs/collaboration` 纳入，否则会把生成态（tasks/events/state）一起带进运行输入，重现同一类失败。

这两条是 §2 证据的直接推论，不是推测。

## 4. 本机复现（2026-10-18，subject = 工作区 `tools/collab`）

```powershell
$env:COLLAB_AUDIT_TOOL_SOURCE = 'tools/collab'
$env:COLLAB_AUDIT_RUN_LABEL  = 'cursor-verify-20261018'
node docs/handoff/verification/CollabTool-Fourth-Independent-2026-10-18/acceptance-boundaries.mjs
```

结果 `2/6`：

| 用例 | 结果 | 观察 |
|---|---|---|
| F01 | 通过 | 有效 JSON → passed，`unverified=false` |
| F02 | 通过 | 未声明契约 → blocked、退出 6、再次 finish 退出 3、freshness=unknown |
| F03 | 失败 | 退出 5，`snapshot contains unlisted files: docs/collaboration/...` |
| F04 | 失败 | 篡改 stdout 后 finish 仍 `status=passed`，`machineResult.adapter=stdout-json` |
| F05 | 失败 | 截断 XML 仍 `status=passed`，`machineResult.parsed.file=result.xml` 并带 `sha256` |
| F06 | 失败 | run 退出 1，`externalExitCode=1`，`resultFile=null`（缺协议输入） |

结果文件：`.collab-local/reaccept-20261017/cursor-verify-20261018/2026-10-18T02-12-18-619Z/results.json`。

## 5. 修复计划（按任务说明 §4 顺序；实施前不改代码）

**步骤 1 · FR-01**

- `cmdDeliver()` / `exportStableSnapshot()` 导出结束后立即对新建快照做一次完整性自检，把未列文件数写进交付记录（如 `snapshotManifest.unlistedAtCreation`）；非 0 则 deliver 直接失败，避免“交付即被污染”。
- 演示快照增加**精确文件**输入（例如 `demoSnapshotFiles: ["docs/collaboration/PROTOCOL.md"]`），只补 smoke 需要的协议文件；实现/验证指纹口径与 smoke 断言保持同一份清单。
- `collab.test.mjs` 改为总是自建一次性 scaffold 并只在其中运行会写状态的 CLI；若检测到当前根是交付快照，给出明确拒绝信息与安全入口，而不是把交付目录当仓库。

**步骤 2 · FR-02**

- stdout/stderr 写入 `logs/<REV>/run-<token>/`，用排他创建避免覆盖已有证据；保留 review 级“最新日志”入口时，把它定义为可变导航，历史解析始终指向具体 runToken 的文件。
- run 记录 `stdout:{path,sha256,bytes}`、`stderr:{path,sha256,bytes}`、`runToken`、`profile`、`deliveryId`；哈希针对写入的真实字节计算，解析复用同一份字节。
- finish 端**先验证绑定再解析**：缺失、长度不符、哈希不符、token 不一致一律不得 passed；旧 run 无绑定时保留历史原义，不用当前内容补算新哈希。

**步骤 3 · FR-03**

- 先做整篇 XML 文档合法性校验，再读取根节点与 `test-case`；截断、未闭合、错误嵌套、重复根、非法属性、非法计数全部拒绝。
- 计数使用安全整数并校验非负；保留既有“缺属性”与“根汇总掩盖失败明细”负例。
- 正例样本改用项目真实、完整的 NUnit XML（来源写清楚；**不据此声称 Unity 已跑通**）。

**步骤 4 · 交付**

- 新建 DEL，运行既有 19/24/12/8 四套 + 本轮 F01～F06，并在交付单逐项写修改位置、正例/负例结果、日志路径与哈希；正例显式断言 `status=passed`，负例断言具体退出码与原因，不用“没抛异常”代替。

## 6. 需要用户裁决的两点

1. **FR-03 解析方式**：零依赖严格扫描器（随快照可运行，但需自证对注释/CDATA/实体/重复根的处理）还是 vendored 第三方解析器（放入白名单目录、计入清单哈希、锁定版本与许可）。受 §3 约束，不能依赖运行时安装。
2. **smoke 检查范围**：建议维持现有两项检查（补协议文件输入），而不是删减断言来降低失败数。

## 7. 本轮保全

未修改 `tools/collab` 任何实现文件、未修改 `Unity Project` 任何内容、未修改游戏任务书、未启动 Unity、未提交/推送、未删除或清理 `out/collab/DEL-20261017150657-dd9483` 及其额外文件、未改写任何历史 DEL/REV/事件记录。本文为新增文档。
