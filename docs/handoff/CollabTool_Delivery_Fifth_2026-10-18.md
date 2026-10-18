# 协作工具第五轮交付说明（FR-01 / FR-02 / FR-03）

日期：2026-10-18。交付方：Cursor。对应任务说明：[CollabTool_Cursor_修复任务说明_2026-10-18.md](CollabTool_Cursor_修复任务说明_2026-10-18.md)（Codex，第四轮 ACCEPT-COLLAB-20261018-CODEX-04）。

本交付只改 `tools/collab` 工具实现与协作文档，不改游戏业务代码、不改任务书勾选、不启动 Unity、不提交/推送、不清理任何旧交付或历史记录。

## 1. 交付编号与说明

本文件是正确的本轮交付说明。**注意：** 先创建的 `DEL-20261018021952-ba9a3f` 在 `summary` 字段里误引了第四轮说明
（操作失误，记录保持原样不改写），因此以其后创建的 `DEL-20261018022444-a55ae4` 为准；两者实现指纹不同，
只因后者在导出前更新了 `PROTOCOL.md`/`README.md`（协议文档现在属于演示交付的输入）。

- 正式交付：`DEL-20261018022444-a55ae4`（`out/collab/DEL-20261018022444-a55ae4`，20 个清单文件，
  `unlistedAtCreation=0`，`implementationFingerprint=ba52ba0daf508b52b3ab872d2a608465333409d0f569f103e6cc6fdde0157490`）
- 复跑命令：`node docs/handoff/verification/CollabTool-Fourth-2026-10-17/run-verification.mjs --subject out/collab/DEL-20261018022444-a55ae4/tools/collab --label <label>`

## 2. 三项缺陷的修复

### FR-01 · 真实交付默认 smoke 走不通

1. **输入依赖**：新增 `demoSnapshotFiles`，把 smoke 读取的 `docs/collaboration/PROTOCOL.md` 作为**精确文件**
   纳入演示快照（不把整个 `docs/collaboration` 当输入，避免带回 tasks/events/state）。
   该文件同时进入实现/验证指纹，导出清单、指纹与 smoke 断言口径一致。
2. **交付即污染**：`cmdDeliver()` 导出后立即做一次未列文件检查，非 0 就删除快照并失败；
   交付记录写入 `snapshotManifest.unlistedAtCreation = 0`。
3. **自测入口**：`tools/collab/tests/collab.test.mjs` 改为先在临时目录构建 scaffold
   （`docs/collaboration/PROTOCOL.md` + `tools/*` 占位 + 工具副本）再运行 CLI，
   无论在仓库还是交付快照内执行，都不会再向被测目录写 `init/ack/claim` 状态。

### FR-02 · stdout 证据没有内容绑定

- stdout/stderr 改写进 `.collab-local/logs/<REV>/run-<runToken>/stdout.txt|stderr.txt`，`flag:'wx'` 排他创建，
  一次运行一份文件，永不覆盖。
- run 记录 `stdout:{path,sha256,bytes,runToken}`、`stderr:{…}`；哈希针对写入的真实字节计算。
- `verifyRunEvidence()` 在 `parseRunResult` 与 `cmdReviewFinish` 之前核对：文件缺失、字节数不符、
  哈希不符、token 不符、无绑定（旧式 run）都返回退出码 5，不会 passed。
- 解析与校验使用同一份已验证字节；旧 run 的历史报告保持原义，不按当前内容补算新哈希。

### FR-03 · NUnit 只取根标签

- 新增零依赖严格解析器 `tools/collab/lib/xml.mjs`（`parseXmlDocument`）：完整文档校验
  （未闭合、错误嵌套、多根、重复属性、下引号缺失、未知实体、尾随内容均报错），
  支持 XML 声明、注释、CDATA、标准与数字实体；不支持 DTD 内部子集（fail-closed）。
- `parseNunitResult()` 先校验整篇文档，再读取 `test-run` 属性与所有 `test-case`；
  计数必须是安全整数，`passed+failed+skipped ≤ total`，根汇总不得掩盖明细失败；
  失败路径返回具体原因（截断在哪个 offset、哪个标签不匹配）。
- 用项目已有的真实 Unity 结果文件验证兼容性（未启动 Unity）：`EditMode.xml` → 25/0/0/25，
  `PlayMode.xml` → 99/0/0/99；两份文件由 2026-10-17 的历史运行产生，仅用于解析器兼容性，不代表本轮重跑。
- 未引入 npm 依赖：隔离快照按白名单复制且不含 `node_modules`，零依赖才能在快照内运行。

## 3. 本轮证据

四套既有检查（工作区与正式交付快照 `DEL-20261018022444-a55ae4`）：

| 套件 | 工作区 | 交付快照 |
|---|---|---|
| 开发方测试 | 22/22 | 22/22 |
| 原检查 baseline24 | 24/24 | 24/24 |
| 补充检查 supplemental | 12/12 | 12/12 |
| 第三轮边界 boundary8 | 8/8 | 8/8 |

证据：`docs/handoff/verification/CollabTool-Fourth-2026-10-17/evidence/fifth-current/`、
`.../fifth-canonical-delivery-2/`。（`.../fifth-canonical-delivery/` 那次 baseline24 为 23/24：
N20 用例在 3 秒内没等到 fixture review 进入 `running`，属时序抖动；同一 subject 重跑为 24/24，
两次结果都保留，未删除。）

本轮新增 10 项针对 FR 的检查（`docs/handoff/verification/CollabTool-Fifth-2026-10-18/fr-checks.mjs`）：

| 用例 | 断言 | 结果 |
|---|---|---|
| FR1A | 新 DEL 目录 0 个未列文件，20 个清单哈希全匹配 | 通过 |
| FR1B | 在交付副本内直接运行开发方测试，文件数不变、不生成 CURRENT.md | 通过 |
| FR1C | 新 DEL 副本 + 原配置默认 smoke → 退出 0，2 项全过 | 通过 |
| FR1D | 移除协议输入（同步修正清单）→ smoke 退出 1，`protocol-exists=false` | 通过 |
| FR1E | 交付副本新增未列 `src/evil.cs` → review-run 退出 5 | 通过 |
| FR2A | 改写失败 stdout 为通过 → finish 退出 5 | 通过 |
| FR2B | 同一 review 顺序两次 run：token/路径不同，第一次字节与哈希不变 | 通过 |
| FR2C | 去掉 run 的 stdout 绑定（模拟旧记录）→ finish 退出 5 | 通过 |
| FR3A | 截断 NUnit XML → finish 退出 5 | 通过 |
| FR3B | 完整合法 NUnit XML → finish passed | 通过 |
| FR3C | 真实 Unity 结果样本（`docs/handoff/verification/2026-10-17/EditMode.xml` 25/0/0/25、`PlayMode.xml` 99/0/0/99）严格解析 | 通过 |

同时用 Codex 的 `acceptance-boundaries.mjs` 复跑：F01/F02/F04/F05 通过；F03/F06 仍失败，原因是它们**按设计固定引用第四轮
DEL**（该目录已被历史污染，无法修复，也不应清理），需由验收方把引用换成 `DEL-20261018022444-a55ae4` 后复测。
`fr-checks.mjs` 的 FR1C/FR1D 就是这两项的新交付等价用例，已通过。

真实默认 smoke 已在主仓库对正式交付跑通一次：`REV-20261018022608-cc542d` → exit 0、
`assertionsTotal=2, failed=0`、stdout 绑定 282 字节 / `sha256:f7128cb9…`、stderr 0 字节 / `e3b0c442…`、
`postSnapshot.ok=true`。该 review 随后按开发方身份取消（`cancelled`，理由：developer-side smoke probe），
独立验收请另起 REV。

## 4. 修改清单

- `tools/collab/lib/xml.mjs`（新增）：零依赖严格 XML 文档解析与元素收集
- `tools/collab/lib/commands.mjs`：`parseNunitResult`/`parseNunitCounters` 严格化；
  `writeEvidenceOnce`/`verifyRunEvidence`；run 证据按 runToken 存储与绑定；
  `cmdReviewFinish` 先验证据再解析；`listUnlistedSnapshotFiles` 复用；
  `cmdDeliver` 导出后洁净检查；`taskRoots` 支持 `demoSnapshotFiles`
- `tools/collab/lib/state.mjs`：freshness 指纹口径包含 `demoSnapshotFiles`
- `tools/collab/config.json` / `config.example.json`：`demoSnapshotFiles`
- `tools/collab/tests/collab.test.mjs`：scaffold 隔离 + 4 条 XML 严格性用例（共 22 条）
- `docs/collaboration/PROTOCOL.md`、`docs/collaboration/README.md`
- `docs/handoff/verification/CollabTool-Fifth-2026-10-18/fr-checks.mjs`

## 5. 仍未完成

C07 真实 Unity EditMode/PlayMode 串联（本轮未启动 Unity；XML 结构闸门已用 2026-10-17 的真实结果文件验证解析兼容，
但未在本轮重新运行 Unity 产出结果）、C09 权威任务书映射与总完成闸门、C10 Hook 端到端与事件去重、
C12 完整用户流程演练。`cleanGitCheckoutVerified` 仍为 `false`。

## 6. 保全

未修改游戏业务代码、未修改任务书、未启动 Unity、未提交/推送；未改动第四轮 DEL 及其额外文件；
未改写任何历史 DEL/REV/事件记录（本轮新增 DEL、REV 与检查证据）。
