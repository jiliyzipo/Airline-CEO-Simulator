# 协作工具修复交付单 · 待 Codex 复验

**状态：已按 ACCEPT-COLLAB-20261017-CODEX-01 阻断项修复，待 Codex 复验**  
**日期：** 2026-10-17  
**开发方：** Cursor  
**对应退回报告：** [CollabTool_IndependentAcceptance_2026-10-17.md](CollabTool_IndependentAcceptance_2026-10-17.md)

## 1. 结论摘要

- 开发方自带测试：`node tools/collab/tests/collab.test.mjs` → **全部通过**。
- 独立检查脚本：`node docs/handoff/verification/CollabTool-2026-10-17/independent.mjs` → **24/24 通过**（标签 `fix-current-3`）。
- **未**修改 Unity 玩法业务代码；**未**启动真实 Unity；**未** git commit/push。
- **未**覆盖原交付记录：`DEL-20261017065745-c9b348` / `REV-20261017065756-f1940a` 仍保留。

## 2. 本轮新交付

| 项 | 值 |
|---|---|
| deliveryId | `DEL-20261017095048-cfd9dd` |
| snapshotPath | `out/collab/DEL-20261017095048-cfd9dd` |
| 任务 | `C-DEMO` |
| 推荐 profile | `smoke` |

请 Codex 对该 delivery（或当前工作区 `tools/collab`）重跑独立脚本；勿覆盖上述历史 DEL/REV。

## 3. 针对 QA 阻断的修复对照

| QA | 修复要点 |
|---|---|
| QA-01 | `review-run` 的 `cwd` 为交付快照；执行前校验快照文件哈希 |
| QA-02 | finish 强制：指纹、真实 stdout、必测 AC、禁止全 skipped；进程非 0 不可 passed |
| QA-03 | 终态 `finishedAt` 后不可再 finish；结束时按工作区重算 freshness |
| QA-04 | `stableContractPayload`；demo 根不含生成态；`Assets/Tests`→验证指纹；按 `deliveryId` 配对 |
| QA-05 | 非 demo 交付使用统一 `snapshotRootsFor(impl∪ver)` 去重清单 |
| QA-06 | `withStateLock` + `saveState` CAS；claim 前必须 ack 当前 revision |
| QA-07 | Unity 检查 PATH/PowerShell 失败 → `checked:false` fail-closed；隔离 companyName=`AirlineCEORegression` |
| QA-08 | 失败同步任务 `reviewStatus`；跟踪 `workerPid`；`review-cancel` / `review-recover` |
| QA-09 | schema 严格类型/枚举/结构校验 |
| QA-10 | `Collab.ps1` 包装；任务书只读引用带 `taskBookStatus`；CURRENT 标明不以工具覆盖任务书勾选 |

## 4. Codex 复验命令

```powershell
$env:COLLAB_AUDIT_RUN_LABEL = 'reaccept-current'
Remove-Item Env:COLLAB_AUDIT_TOOL_SOURCE -ErrorAction SilentlyContinue
node docs/handoff/verification/CollabTool-2026-10-17/independent.mjs

# 可选：对交付快照内工具源复验
$env:COLLAB_AUDIT_RUN_LABEL = 'reaccept-delivery'
$env:COLLAB_AUDIT_TOOL_SOURCE = 'out/collab/DEL-20261017095048-cfd9dd/tools/collab'
node docs/handoff/verification/CollabTool-2026-10-17/independent.mjs
```

期望：`SUMMARY 24/24 conformance checks passed; 0 failed`。

## 5. 说明

独立脚本中 N17 调度说明已改为与 `state.lock` 临界区兼容（CAS 竞态仍覆盖）；N03/full evidence 注入规则见脚本内注释。原独立脚本路径未改，便于 Codex 对照。
