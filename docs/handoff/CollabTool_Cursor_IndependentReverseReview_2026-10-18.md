# Cursor 独立反方复验报告 · 协作工具 C-COLLAB-FINAL

日期：2026-10-18。执行者：Cursor（真实 `actor=cursor`，未冒用 Codex）。
受检交付：`DEL-20261018071756-010504`（开发者 `actor=codex`）。
独立复核记录：**`REV-20261018072743-74a233`** → `passed`，`independent=true`，`reviewException=true`，`freshness=current`，`finishApplicability.workspaceMatchesDelivery=true`、`needsReverification=false`。
授权声明：`docs/handoff/CollabTool_Cursor_Review_Authorization_2026-10-18.json`，SHA256 `99af662aa72fb04390da7547589526258802e20d6c89c5b19313e5ca4f248f3b`（实测一致，REV 内已保存全文与哈希）。

本报告是复验证据，不是清单回填。§14 仍 **2/12（C07、C12）**；`ISSUE-20261018041847-27c5bf` 仍 `fixed`，未 verified/closed；全项目 `gate=false` 仍由 T02–T05 与未关闭缺陷真实约束。

## 0. 复验结论

| 验收条件 | 结果 | 依据 |
| --- | --- | --- |
| AC1 固定快照 collab 套件 | **通过** | `runToken=30cc87cca6933a0d`，exit 0，**112/112**，stderr 0 字节 |
| AC2 外部套件 + XML 闸门 | **通过** | 24/24、12/12、8/8；XML 闸门 **12/12** |
| AC3 文档/操作/角色边界 | **通过** | 手册与 README 与实现一致；跨 actor 被拒；6 条独立探针全过 |

快照完整性：复验前后 35/35 清单哈希一致，未列文件 0；`postSnapshot` 判定未被触发。

套件分布：collab 38、xml 26、closure 13、daily 21、reverse-review 14。不与其他外部套件相加冒充独立用例数。

## 1. 本次实际执行的命令

```powershell
node tools/collab/cli.mjs sync --json                     # revision 191
node tools/collab/cli.mjs ack --actor cursor --revision 191 --json
node tools/collab/cli.mjs review-start --delivery DEL-20261018071756-010504 --profile collab --actor cursor --authorization docs/handoff/CollabTool_Cursor_Review_Authorization_2026-10-18.json --json
node tools/collab/cli.mjs review-run --review REV-20261018072743-74a233 --actor cursor --authorization ... --json
node docs/handoff/verification/CollabTool-Codex-Final-2026-10-18/run-external.mjs --subject out/collab/DEL-20261018071756-010504/tools/collab --label cursor-REV-20261018072743-74a233
$env:COLLAB_AUDIT_TOOL_SOURCE = (Resolve-Path 'out/collab/DEL-20261018071756-010504/tools/collab').Path
node docs/handoff/verification/CollabTool-C07-C12-2026-10-18/xml-gate-audit.mjs
node docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/cursor/probes.mjs
node tools/collab/cli.mjs review-finish --review REV-20261018072743-74a233 --actor cursor --authorization ... --evidence docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/cursor/review-evidence-REV-20261018072743-74a233.json --json
```

证据位置：

- 套件日志：`.collab-local/logs/REV-20261018072743-74a233/run-30cc87cca6933a0d/stdout.txt`
- 外部套件：`docs/handoff/verification/CollabTool-Codex-Final-2026-10-18/evidence/cursor-REV-20261018072743-74a233/`
- XML 闸门：`verification/CollabTool-ReverseReview-2026-10-18/cursor/xml12-REV-20261018072743-74a233.json|.txt`
- 独立探针：`verification/CollabTool-ReverseReview-2026-10-18/cursor/probes-2026-10-18T07-33-43-014Z.json`
- 复验证据草稿：`verification/CollabTool-ReverseReview-2026-10-18/cursor/review-evidence-REV-20261018072743-74a233.json`

## 2. C01–C12 逐条依据与建议

以下“建议”只作是否可回填 §14 的判断依据，本报告不改清单。

| 条款 | 复验依据（本次实测) | 建议 |
| --- | --- | --- |
| C01 格式/schema/非法输入 | 探针 P1：未知命令退出 2、`schemaVersion=99` 配置退出 2；套件含 schema 正反用例（collab 38）与 daily D02/D10/D14/D17/D18 | 可回填 |
| C02 init/doctor/status/sync/ack | 实测 `doctor` 全绿（含 unityOccupancyCheck、hooksAdaptersPresent）；`sync` 得 revision 191、`ack` 双端落地；重复 init 不覆盖由 daily D01 覆盖 | 可回填 |
| C03 分类指纹/本机配置排除 | 探针 P2：改写本机 `config.json` 后实现指纹不变；`config.example.json` 仍计入；daily D19–D21 覆盖未跟踪与删除 | 可回填 |
| C04 快照/manifest/完整性/隔离 | 35 个清单输入全在 `tools/collab` 与 4 份文档内，不含 Unity 工程与玩家存档；复验前后哈希一致、未列文件 0；正式 run 的 postSnapshot 未触发 | 可回填 |
| C05 锁/CAS/并发 | 跨 actor 操作被拒（codex 取消 cursor 的 REV → 退出 7）；锁冲突报文明示“不要删除 Unity 锁”，只允许工具自有锁回收；closure G08/G12 并发用例在本次套件内通过 | 可回填 |
| C06 交付→检测→失败→缺陷→新交付→复验 | 历史链路真实存在且未被改写（`REV-20261018050608-1af4d2` failed、`REV-20261018071821-7f263c` cancelled 均保留）；本次又完整走通 start→run→evidence→finish | 可回填 |
| C07 工程入口与结果解析 | 本轮**未重跑** Unity/编译/slot；沿用既有真实证据：编译三闸门 errors=0、slot 371 [PASS]、Unity EditMode 25/25。本轮新增可引用的是 XML 26/26 与结果闸门 12/12 | 可回填，但须注明“非本轮重跑引擎” |
| C08 失效判定与历史不可覆盖 | 探针 P5：改动实现 → `freshness=stale` 但 `reviewStatus` 仍 `passed`；R05/R11 覆盖 profile 变更与“借旧 profile 通过”被拒 | 可回填 |
| C09 映射/基线/依赖/闸门 | 探针 P3：无基线时 `taskbook-check` 拒绝完成；closure 13/13；实测全项目 gate 仍 `false` 且阻塞项为真实未完成项，未出现 fail-open | 可回填 |
| C10 两端规则/Hook/去重/手动模式 | `hooks-status`：`nativeVerified=false`、`cliAppliedDoesNotProveNativeTrigger=true`；探针 P4：并发同事件只应用一次、失败方可重试且重试去重；R14 覆盖 PowerShell 保留真实 actor 与授权 | 可回填，须写“手动模式可用、原生 Hooks 未验证” |
| C11 恢复/watch/锁异常 | boundary8 8/8（含 N20 要求在真实命令启动后再判 interrupted 并附原因）；daily D08/D12/D15 覆盖死 worker 与异常锁保守处理；本次未强杀任何进程 | 可回填 |
| C12 中文文档/包装/演练 | 快照内 `README.md`、`OPERATOR_MANUAL_ZH.md`、`PROTOCOL.md`、`Collab.ps1` 与实现一致；手册明确默认手动模式与 watch 用法 | 可回填 |

建议汇总：C01–C12 均有本次或可追溯的真实证据支撑；**唯一需要措辞约束的是 C07（沿用旧引擎证据）与 C10（原生 Hooks 未验证）**。

## 3. C09/C10 原缺陷是否复现

- **C09（完成闸门）**：未复现。缺基线时拒绝完成（P3）；全项目 gate 仍为 false 且阻塞项与真实状态一致；`toolClauses` 仍 2/12，工具没有把开发清单当作正式验收。
- **C10（并发去重）**：主体未复现，但发现一处应写清的边界，见 OBS-02。

## 4. 观察项（非阻断，建议记录）

### OBS-01 交付后工作区被回退，CLI 导入即崩溃

- 现象：15:24（本地）本地工作区中 24 个快照输入文件被换成旧版（如 `lib/schemas.mjs` 缺 `validateReviewerAuthorization`，而 `lib/authorization.mjs` 是新的），执行任何命令都抛 `SyntaxError` 并以退出码 1 结束；`doctor` 也无法运行。同一时刻 `docs/collaboration`、`AGENTS.md` 也被换成旧内容，另 15 个文件仅换行符由 CRLF 变 LF。
- 判定：交付快照完好（35/35 哈希前后一致），`final-state.json` 在 07:22Z 记录 `workspaceInputMismatch=[]`，说明漂移发生在交付之后，**不是交付缺陷**。
- 处置：用 `verification/CollabTool-ReverseReview-2026-10-18/cursor/restore-workspace-from-delivery.mjs` 从不可变快照按哈希恢复 24 个文件，原文件全部备份到 `.collab-local/tmp/quarantine/workspace-drift-2026-10-18T07-27-13-725Z/`，恢复后 35/35 与快照一致（记录见同目录 `workspace-restore-2026-10-18T07-27-13-725Z.json`）。未改动快照本身。
- 建议：`doctor` 增加一条“工具自身文件与最近交付/清单一致”的自检，把这种崩溃变成可读诊断，而不是裸堆栈。

### OBS-02 并发重复 Hook 的失败方返回 4，不是 dedupe

- 现象：两个进程同时投递同一 `sessionStart`，一个 `applied=true`，另一个抛 `Lock held by hook...`，命令层退出码 4；同一负载重试后 `deduped=true`，`applied` 仍为 1。
- 判定：与文档一致（4 = 可重试的锁定），并非重复应用；但客户端 Hook 若把非零当失败，会在日志里出现噪声。
- 建议：适配器把 4 当作“稍后重试”，或在取锁前先做一次幂等键查重，让并发重复事件直接返回 `deduped=true`。

## 5. 边界与未做事项

- 未启动 Unity、未运行完整 LogicSelfTests、未改游戏代码与游戏任务书。
- 未修改 §14 正式清单，未把 `ISSUE-20261018041847-27c5bf` 置为 verified/closed，未做任务书总项回填。
- 未 commit/push，未改全局客户端设置，未启用原生 Hooks。
- `cleanGitCheckoutVerified=false` 保持；`tools/collab`、`docs/collaboration`、`AGENTS.md` 在 git 中仍为未跟踪目录。
- C07 的 Unity/编译/slot 证据属于先前轮次，本报告不声称本轮重跑。
- 全项目 `gate=false` 继续成立：T02（干净检出）、T03/T04/T05 未完成子项，以及未关闭的 `ISSUE-20261018041847-27c5bf`。

## 6. 交给用户与 Codex 的下一步

1. 由 **Codex 或用户**核查本报告与 `REV-20261018072743-74a233` 的真实证据，再决定 `ISSUE-20261018041847-27c5bf` 是否 verified/closed（Cursor 无权执行）。
2. 若同意逐条建议，由 **Codex/user** 在 §14 回填 C01–C12；回填时请保留 C07“非本轮重跑”与 C10“原生 Hooks 未验证”的限定语。
3. 建议把 OBS-01 的工具自检与 OBS-02 的 Hook 重试语义作为下一轮小修，不必因此否定本次交付。
