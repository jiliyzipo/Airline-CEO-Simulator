# C07 / C09 / C10 / C12 完成与独立复核记录

日期：2026-10-18。编号：DELIVERY-COLLAB-20261018-C07C12-01。
受检交付：`DEL-20261018035835-766dc7`（工具源码含 XML 解析器修复与 freshness 根集合修复）。
独立复核：`REV-20261018035841-c4d71c` = passed（smoke 2/2，freshness current）。

本记录只回填本次实际执行过、有落盘证据的结论。`cleanGitCheckoutVerified` 仍为 `false`，游戏任务书 T02～T05 的未完成项未因本文改变。

## 0. 本轮结论速览

| 条款 | 结论 | 主要证据 |
| --- | --- | --- |
| C07 | 通过 | 三个独立工程 profile 真实执行：Unity EditMode 25/25、semcheck 3 闸门 errors=0、slotcheck 371 [PASS]；占用演练拒绝启动且不杀进程 |
| C09 | 通过 | 任务书映射 + 基线 scheme 2 + `issue-update` 单一缺陷状态源；闸门在缺陷关闭前后如实变化 |
| C10 | 通过 | 两端 Hook 事件被接受、去重 2 次、BOM 负载可解析；`manualFallback` 明确手动流程可用 |
| C12 | 通过 | 中文操作手册、PowerShell 包装、四轮端到端演练（含失败退回与缺陷闭环） |

测试基线：开发方自测 38/38；XML 专项 26/26；本轮新增 XML 闸门审计 12/12（全部针对**交付快照**执行，不读工作区源码）。

## 1. C07 · 隔离工程 profile 与占用控制

已把原先混在一次“工程复验”里的三件事拆成互不影响的隔离 profile，各自有独立交付、独立快照、独立结论：

| profile | 交付 | 复核 | 真实结果 |
| --- | --- | --- | --- |
| `unity-editmode` | `DEL-20261018033132-39a1e3` | `REV-20261018033542-0f4993` passed | 隔离快照内跑 `tools/regression/RunUnityTests.ps1`，本次 run 生成的 NUnit XML 为 25/25 通过 |
| `compile` | `DEL-20261018033139-af5cca` | `REV-20261018033359-8f89f1` passed | 隔离快照内跑 `tools/semcheck/run.js`，三道闸门 `errors=0`（编码闸门扫描 352 文件），退出码 0 |
| `slot` | `DEL-20261018033145-e77a66` | `REV-20261018033157-6550bc` passed | 隔离快照内跑 `tools/slotcheck/slotcheck.csproj`，371 `[PASS]` / 0 `[FAIL]`，`SLOTCHECK OK` |

细则：

1. **运行产物与交付入参分离**。`compile` profile 声明 `runOutputs: ["tools/semcheck/semcheck_report.txt"]`，工具自己生成的报告不再被误判为“未列文件/夹带”。`resolveRunOutputRels` 同时拒绝源码扩展名与路径穿越，声明产物之外的多余文件仍判失败。
2. **非零与缺结果必须失败**。`resultKind=unity|nunit` 时结果文件缺失、被截断、含 `failed>0` 或只有 `skipped`，都不能产生 passed；这是与 C08 的“待复验”判定配套的硬条件。
3. **占用控制**。`unityOccupiedPolicy = wait-user-close`：检测到 Unity 被占用（含同一可执行名的活进程）时，review-run 直接以退出码 4 拒绝，报告占用进程 PID 与工作目录，**不**启动第二个 batchmode、**不**结束用户进程、**不**删锁。占用消失后可原样重跑。

证据：[compile](verification/CollabTool-C07-C12-2026-10-18/evidence-compile.json)、[slot](verification/CollabTool-C07-C12-2026-10-18/evidence-slot.json)、[unity](verification/CollabTool-C07-C12-2026-10-18/evidence-unity.json)、[profile 定义](verification/CollabTool-C07-C12-2026-10-18/C-ENG.summary.txt)。

## 2. C09 · 任务书映射、基线与缺陷生命周期

1. **映射显式化**。只有 `config.json.taskBook.tasks` 里显式登记的 T01～T06 才会被比对；`C-DEMO`/`C-ENG*` 这类工程任务只按协作记录判定，不会被工具伪写成游戏任务完成。
2. **基线方案版本化**。`CLAUSE_SCHEME = 2`：工具自身升级哈希算法时，旧基线被标记 `schemeUpgraded` 并要求重录，而不是谎报“任务书条款被改动”。重录后 `clause-unchanged` 才恢复为通过。
3. **单一缺陷状态源**。缺陷状态只存在于共享 `state.json.issueStatus`，由 `issue-update` 统一改；CURRENT 与事件流都是它的投影，不再有第二份人工状态。
4. **角色与顺序受控**。合法链为 `open → fixing → fixed → verified → closed`（另有 `wontfix`）。Cursor 只能申报 `fixing`/`fixed`，`verified`/`closed` 必须由 Codex 或用户执行；越权与跳步都被拒绝并给出退出码 7。
5. **未关闭缺陷门禁生效**。第五轮 XML 缺陷 `ISSUE-20261018030616-3028cb` 关闭前，全项目闸门 `blockers` 含 `issue:ISSUE-20261018030616-3028cb`；按证据关闭后同一命令的 `openIssues` 为空，缺陷不再阻塞，但 T02～T05 未完成项仍如实阻塞。

本轮缺陷闭环（事件可查）：

```
open(2026-10-18T03:06:16Z) → fixing → fixed → verified → closed  [actor: cursor, cursor, codex, codex]
REV-165 / EVT-20261018040216-5e8aa3 / EVT-20261018040232-0fae8a / EVT-20261018040253-535905 / EVT-20261018040308-a3b204
```

## 3. C10 · Hooks 适配与手动兜底

- 两端事件表：Cursor `sessionStart` / `afterFileEdit` / `taskEnd`；Codex `sessionStart` / `toolEnd` / `turnEnd`。适配示例见 `tools/collab/adapters/`。
- 去重与去抖：同 key 事件在 `dedupeWindowMs = 60000` 内只应用一次（`applied=false, deduped=true`），`debounceMs = 2000` 抑制编辑风暴。`hooks-status` 当前计数 `applied=5, deduped=2`，最近 5 条事件的 reason/revision 全部落盘可查。
- Hook **永不**创建交付或改写结论，只做 `sync` 级刷新；这一点在 reason 字段里固定写明。
- Windows 兼容：`stripBom` 让 PowerShell 5 重定向/部分编辑器带出的 UTF-8 BOM 负载正常解析；非 JSON 负载仍以退出码 2 明确报错，不静默吞掉。
- 不支持或未配置时：`hooks-status` 的 `manualFallback` 明确说明 `sync/deliver/review-*` 无需 Hook 即可完成全流程，不把 Hook 当唯一入口。

## 4. C12 · 手册、PowerShell 包装与端到端演练

1. 中文操作手册 `docs/collaboration/OPERATOR_MANUAL_ZH.md`：权限矩阵、全命令说明、profile 契约、缺陷生命周期、故障排查、演练记录；`README` 已加入口。
2. `tools/collab/Collab.ps1` 覆盖 `tasks / gate / task-add / taskbook-baseline / taskbook-check / issue / hook / hooks-status`，参数有提示，用户不必手写复杂 JSON。
3. 演练按“真实失败也要留下痕迹”的方式做满四轮：

| 轮次 | 场景 | 结果 |
| --- | --- | --- |
| 1 | 故意留一处未满足项 | 复核 failed 并生成缺陷，未伪造通过 |
| 2 | 修缺陷 + 方案版本化 + 运行产物声明 | 新交付复验通过 |
| 3 | BOM 兼容 + PowerShell 补齐 + 手册 | 新交付复验通过 |
| 4 | freshness 根集合修复（真实缺陷） | `DEL-20261018035835-766dc7` / `REV-20261018035841-c4d71c` passed，freshness current |

第 4 轮修的是工具自身的真缺陷：`refreshFreshness` 用项目默认根集合重算指纹，而交付按任务声明的 `snapshotRoots` 计算，导致窄根集合任务永远 `stale`，并在 stale 分支里把 `reviewStatus=passed` 降级。现在新鲜度与交付/快照同源（`resolveTaskRoots`），且 stale 只表达“待复验”，不再抹掉历史结论（保留 `reviewStatus` 并记 `staleForReviewId`）。

演练摘要：[1](verification/CollabTool-C07-C12-2026-10-18/drill-summary-1.txt) / [2](verification/CollabTool-C07-C12-2026-10-18/drill-summary-2.txt) / [3](verification/CollabTool-C07-C12-2026-10-18/drill-summary-3.txt) / [4](verification/CollabTool-C07-C12-2026-10-18/drill-summary-4.txt)。

## 5. 第五轮 XML 缺陷的收口复验（本轮新增）

原缺陷：`ISSUE-20261018030616-3028cb`（major，XML 闸门可接受非法文档）。修复已进入 `DEL-20261018035835-766dc7`，本轮改为**只针对交付快照**取证，避免“工作区已修、交付未修”被混为一谈。

```powershell
$env:COLLAB_AUDIT_TOOL_SOURCE = 'out/collab/DEL-20261018035835-766dc7/tools/collab'
node tools/collab/tests/xml.test.mjs          # 26/26
node docs/handoff/verification/CollabTool-C07-C12-2026-10-18/xml-gate-audit.mjs   # 12/12
```

新增审计脚本 `xml-gate-audit.mjs` 不导入工作区任何模块，只从 subject 快照导入 `commands.mjs` / `xml.mjs`，检查两件事：

- **解析层**：紧邻属性、正文 `]]>`、注释双连字符、空白后的 XML 声明、`&#1;`、未验证 DOCTYPE 六类非法输入，既让 `parseXmlDocument` 返回非 ok，也拿不到 NUnit 计数；合法中文/补充字符/CDATA 仍可解析。
- **闸门层**：把同一非法文档写成本次 run 真实生成、有哈希绑定的 `resultFile`，`review-finish` 必须拒绝（退出码 5/6）；同时验证合法的 NUnit 结果与纯 JSON 结果**仍能** passed，确认收紧没有造成误杀。

证据：[审计结果](verification/CollabTool-C07-C12-2026-10-18/xml-gate-audit-delivery.results.json)、[XML 专项日志](verification/CollabTool-C07-C12-2026-10-18/xml-tests-delivery.txt)。

## 6. 边界与未完成项（不隐藏）

- `cleanGitCheckoutVerified=false`：本轮未做干净检出验证，T02 因此仍阻塞；也未提交/推送。
- 游戏侧 T03.8、T04.3/T04.8、T05.2/T05.5/T05.6/T05.7 仍未勾选，属游戏开发范围，不由协作工具代勾。
- 本轮工具条款只回填 C07/C09/C10/C12；C01～C06、C08、C11 未在本文声称通过。
- Unity 只做隔离 EditMode 串联与占用演练；未启动 PlayMode 长时间批次。
- 未修改游戏业务代码、未改 T01～T06 任务书勾选、未覆盖任何历史 `deliveries/`、`reviews/`、`events/` 记录。

## 7. 复现命令

```powershell
node tools/collab/cli.mjs sync
node tools/collab/cli.mjs tasks
node tools/collab/cli.mjs gate
node tools/collab/cli.mjs hooks-status
node tools/collab/tests/collab.test.mjs
node tools/collab/tests/xml.test.mjs
```
