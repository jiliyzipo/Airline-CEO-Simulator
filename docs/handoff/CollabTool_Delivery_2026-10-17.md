# 协作工具开发交付单 · 待 Codex 独立验收

**状态：工具开发完成，待 Codex 验收（Cursor 不代替填写验收通过）**  
**日期：** 2026-10-17  
**开发方：** Cursor  
**模型：** deepseek-v4.1-flash（经本地网关）  
**需求依据：** `docs/Development Manual/Development Task Book/Task book/Cursor_Codex_协作工具_开发需求与操作手册_v1.0_2026-10-17.md` §1 / §14 / §16

## 1. 交付摘要

实现本地 Node CLI 协作工具，使 Cursor 交付与 Codex 检测共用任务编号、状态、指纹与不可覆盖证据。已完成手动闭环与可选 Hooks 适配示例；**未**改 Unity 玩法业务代码；**未** git commit/push。

演示走通：

| 项 | 值 |
|---|---|
| deliveryId | `DEL-20261017065745-c9b348` |
| reviewId | `REV-20261017065756-f1940a` |
| 结论 | evidence 通过后 status=passed；随后改 `tools/collab` 文件，sync 显示 C-DEMO freshness=stale（历史报告保留） |

## 2. 修改/新增文件清单（主要）

### 新增

- `AGENTS.md`
- `tools/collab/cli.mjs`
- `tools/collab/config.example.json`（init 后生成本机 `config.json`，已 gitignore 可选——当前已生成）
- `tools/collab/lib/*.mjs`（paths/io/exit/schemas/fingerprint/snapshot/state/commands）
- `tools/collab/tests/collab.test.mjs`
- `tools/collab/tests/smoke-profile.mjs`
- `tools/collab/adapters/*`
- `docs/collaboration/README.md`
- `docs/collaboration/PROTOCOL.md`
- `docs/collaboration/tasks/*.json`（C-DEMO + T01～T06 只读引用）
- `docs/collaboration/deliveries|reviews|events|state.json|CURRENT.md|NEXT_*.md`（运行生成）
- `docs/handoff/CollabTool_Delivery_2026-10-17.md`（本文件）

### 修改

- `.gitignore`：增加 `.collab-local/`、`/out/collab/`
- `docs/ProjectManifest.md`：增加协作状态入口链接

### 不进入版本 / 本机

- `.collab-local/`（锁、日志、临时证据）
- `out/collab/`（交付快照）
- 可选：`tools/collab/config.json`（可由 example 复制）

## 3. 可复制命令

```powershell
cd C:\Users\jiliy\Airline-CEO-Simulator
node tools/collab/cli.mjs doctor
node tools/collab/cli.mjs init
node tools/collab/cli.mjs sync
node tools/collab/cli.mjs status --json
node tools/collab/tests/collab.test.mjs

# 手动闭环（C-DEMO）
node tools/collab/cli.mjs claim --task C-DEMO --actor cursor --expected-revision <rev>
node tools/collab/cli.mjs deliver --task C-DEMO --actor cursor --summary-file <summary.txt> --model <name> --profiles smoke
node tools/collab/cli.mjs review-start --delivery <deliveryId> --actor codex --profile smoke
node tools/collab/cli.mjs review-run --review <reviewId> --actor codex
node tools/collab/cli.mjs review-finish --review <reviewId> --actor codex --evidence <evidence.json>
```

## 4. 自动化测试结果

`node tools/collab/tests/collab.test.mjs` → **all tests passed**（schema 角色闸门、文件锁、原子写、doctor/init/sync/ack CAS、禁止 Cursor review-finish 等）。

负例已覆盖：`--passed` 单独结束 → exit 7；无 evidence → exit 6；错误 revision ack → exit 3。

## 5. C01～C12 开发自检（非 Codex 验收勾选）

| ID | 开发自检 | 说明 |
|---|---|---|
| C01 | 完成 | schemas + 非法非零退出 |
| C02 | 完成 | init/doctor/status/sync/ack；重复 init 不覆盖 |
| C03 | 完成 | 实现/验证/合同指纹；未跟踪文件纳入扫描 |
| C04 | 完成 | 稳定快照；C-DEMO 用 demoSnapshotRoots；真实任务用完整根 |
| C05 | 完成 | 任务/Unity 锁 + CAS；双进程锁单测 |
| C06 | 完成 | C-DEMO 交付→检测→通过；失败/禁止路径已测 |
| C07 | 部分 | 已接固定 profile（smoke/compile/slot 配置）；完整 Unity 隔离批处理仍走现有脚本，由 Codex 按任务配置深验 |
| C08 | 完成 | 改源后 stale；历史 REV 文件仍在 |
| C09 | 完成 | CURRENT/NEXT 生成；T01～T06 为只读引用，不伪造游戏总完成 |
| C10 | 完成 | AGENTS.md + adapters 示例；hooks 默认关，手动可用 |
| C11 | 部分 | review 中断可标 failed/interrupted 字段预留；锁异常不删 Unity 锁；完整崩溃恢复剧本待 Codex 加压 |
| C12 | 完成 | 中文 README/PROTOCOL/本交付单 |

## 6. 指纹（演示交付）

- 策略版本：`fingerprintPolicyVersion = 1`
- C-DEMO 交付实现指纹：`d539cdf988cf9d45a921a16c4f0a3b5b51ecf73a8a99f95cb6032e486a4d1aac`
- `cleanGitCheckoutVerified`: 始终 false（本工具不声称干净检出已验证）

## 7. 角色边界

- Cursor 不能 `review-finish` / 不能写 `independentReviewPassed=true`
- Codex 不能 `claim`/`deliver`
- 不自动提交、不杀 Unity、不删 Unity 工程锁

## 8. 已知缺口（请 Codex 重点查）

1. **C07 深度**：compile/slot/unity-* profile 已配置命令白名单，但本交付未在隔离 Unity 上重跑全套游戏回归；验收协作工具请用 C-DEMO + 负例，勿改写 T01～T06 真实验收勾选。
2. **任务书 Markdown 自动勾选闸门**：MVP 未实现模糊全文替换勾选（§12）；CURRENT 为权威协作状态，任务书勾选仍由用户/Codex 按证据人工或后续任务处理。
3. **Hooks**：仅提供 example，默认关闭；需按本机 Cursor/Codex 实际 Hooks schema 再接线。
4. **watch**：前台进程监听，非服务安装。
5. **跨机器**：未做；本机锁不可搬迁。
6. 演练中写入了 `tools/collab/tests/stale-marker.txt` 用于 stale 演示，可保留或删除后重新 sync。

## 9. 停用

停止 `watch`；保留 `docs/collaboration/**` 历史记录；删除 `.collab-local` 仅清本机锁/日志。不删除游戏资源。

## 10. 请 Codex 执行

> 请按《Cursor_Codex_协作工具_开发需求与操作手册_v1.0》§14 清单与负例，对本次协作工具做**独立验收**。默认不修改生产业务代码。以真实命令输出与证据回填 C01～C12；Cursor 已声明开发完成，**不能**视为验收通过。

参考交付：`DEL-20261017065745-c9b348`、`REV-20261017065756-f1940a`、本文件。
