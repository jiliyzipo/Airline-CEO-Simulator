# 协作工具 · 中文用户手册（C12）

本手册面向日常使用者：**Cursor（开发）、Codex（独立检测）、用户（裁决）**。
所有命令都在仓库根目录执行；工具只读写 `docs/collaboration/`、`.collab-local/`、`out/collab/`，不碰游戏代码。

2026-10-18 更新：默认使用手动 sync/ack 与前台 watch，原生 Hooks 尚未验证；快速上手和可直接给两端的指令见 [README](README.md)。`taskbook-check` 仅可写通过闸门的总项；sync 只撤回工具自己写入且条款未被手工改动的受管总项，保留日志。游戏只读引用不自动撤回。

`doctor` 只读，不创建探测文件。`watch` 就绪后以事件通知和 0.25～4 秒轮询检查变化，快速保守标记 stale；显式 sync 再读取实际字节验证。变更恢复后需 sync，不凭旧缓存变绿。监听不替双方 ack。

---

## 1. 权限矩阵（谁能写什么）

| 动作 | 命令 | Cursor | Codex | 用户 |
| --- | --- | --- | --- | --- |
| 领取任务 | `claim` | ✅ | ❌（退出码 7） | ❌（可授权开发代理） |
| 交付 | `deliver` | ✅ | ❌ | ❌（可授权开发代理） |
| 开始/执行检测 | `review-start` / `review-run` | ❌ | ✅ | ❌（可授权检测代理） |
| 写检测结论 | `review-finish` | ❌（退出码 7） | ✅ | ❌（可授权检测代理） |
| 声明「独立验收通过」 | 交付记录里的 `independentReviewPassed` | ❌ | 只能由检测结论产生 | ❌ 手工改 |
| 新建任务定义 | `task-add` | ✅ | ✅ | ✅ |
| 记录任务书基线 | `taskbook-baseline` | ✅ | ✅ | ✅ |
| 勾选任务书总条款 | `taskbook-check` | ❌ | ✅ | ✅ |
| 缺陷状态流转 | `issue-update` | 只能 `fixing`/`fixed` | `fixing`/`fixed`/`verified`/`closed`/`wontfix` | 全部，含重开 `open` |
| Hook 事件 | `hook` | 以 `cursor` 身份 | 以 `codex` 身份 | — |

规则：**Cursor 可以写“已实现、待检测”，永远不能写“Codex 已通过”。**
表中是默认权限。用户授权 Codex 开发后的特定工具交付，可凭绑定 DEL/三类指纹/profile 的授权声明，由 Cursor 使用真实身份反向独立复验；具体操作见 §5。不得把授权扩大为 Cursor 自验自己的交付。
所有越权尝试都会以退出码 7（forbidden）失败，并在 stderr 给出原因。

上述“用户”列指 CLI 的 `--actor user`，不是对人的设计裁决权限制。授权代理执行不等于 CLI 自动授予该 actor 其他角色的命令权限。

---

## 2. 一次性准备

```powershell
node tools/collab/cli.mjs doctor --json   # 环境、目录、工具、Unity 占用、Hooks 接线
node tools/collab/cli.mjs init            # 建目录；重复执行不会覆盖已有记录
```

`doctor` 常见非致命警告：

- `hooks.enabled=true but no client hook config found` → 还没把适配器接到 Cursor/Codex，手动命令仍然完整可用（见 §9）。
- `Unity occupied` → 用户的 Unity 正在运行，检测会被拒绝而不是抢锁。

---

## 3. 每日开工

```powershell
node tools/collab/cli.mjs sync                                   # 重算 freshness 并刷新 CURRENT/NEXT
node tools/collab/cli.mjs ack --actor cursor --revision <rev>    # 声明已读到该 revision
node tools/collab/cli.mjs status --json                          # 任务、待办、未关闭缺陷
```

一条命令的等价写法（推荐）：

```powershell
node tools/collab/cli.mjs stage-open --actor cursor   # 或 .\tools\collab\Collab.ps1 stage open
```

它会自动 `sync`、以该身份 `ack` 最新 revision、打印任务看板与待检测交接单。

`docs/collaboration/CURRENT.md` 与 `NEXT_CURSOR.md` / `NEXT_CODEX.md` 都是**程序生成**，不要手工改；
任务状态的唯一来源是 `tasks/*.json` + `reviews/*.json` + `issues/*.json`（缺陷状态另存于 `state.json.issueStatus`）。

---

## 4. Cursor：领取与交付

自动化路径（阶段收口）：

```powershell
node tools/collab/cli.mjs stage-close [--task S01-UI] [--note "本次修了什么"] [--gaps "未做X"]
```

`stage-close` 自动完成：sync+ack → 复用/新建阶段任务（生成标题、范围、验收条件）→ 自动选 deciding profile
→ 自动写交付摘要 → 生成 DEL → 在交付快照内跑一次该 profile 预检 → 写交接单 `out/collab/handoff/<DEL>.md`。

- 预检通过：交接单状态 `可检测`，把 `DEL` 交给 Codex。
- 预检失败：退出码 5，交接单标记 `预检未通过`，`stage-review` 会跳过该 DEL；修复后重跑同一条命令即可。
  预检会在执行前后各校验一次交付快照：清单文件被改写、缺失、夹带源码都算预检失败，
  不会出现“跑完了但快照已被污染还标成可检测”。
- Unity 类 profile 的预检与正式检测共用 `unity-review.lock` 资源租约：
  别的 review 持锁、编辑器占用、占用无法确认，三种情况都直接退出码 4 且不执行。
- 预检是开发自测，**永远不能**替代独立验收。交接单会列出哪些验收条件不被 profile 机器覆盖。

手动路径（排查或特殊场景）：

```powershell
node tools/collab/cli.mjs claim --task C-DEMO --actor cursor --expected-revision 140
# ... 开发与自测 ...
node tools/collab/cli.mjs deliver --task C-DEMO --actor cursor --summary-file path\to\summary.txt --profiles smoke
```

- `claim` 带 `--expected-revision` 时是 CAS：revision 变了就退出码 3，并给出最新 revision；`ack` 过期时也会被拒绝。
- `deliver` 会导出**隔离快照**到 `out/collab/<deliveryId>/`，并计算实现/验证/合同三份指纹。
- 交付摘要要写清：改了哪些文件、跑了哪些测试、还有什么没做（`knownGaps`）。`stage-close` 会自动生成这份摘要。

---

## 5. Codex：检测

自动化路径（一条命令）：

```powershell
node tools/collab/cli.mjs stage-review --delivery <deliveryId> --actor codex
# 省略 --delivery 时自动挑最新一份"未检测且预检通过"的交付
```

`stage-review` 自动完成 review-start → review-run → 生成证据 → review-finish；
失败时自动生成 ISSUE 文件（含 repro/expected/actual/severity），不需要手写证据 JSON。

- 同一 DEL 已检测过时直接返回上次结论，不会重复执行。
- 同一个 DEL 的检测带跨进程租约：两个并发的 `stage-review` 只会真正执行一次，第二个返回退出码 3（已有检测在跑）或上一次的结论。
- 退出码：0 通过；5 未通过且已登记缺陷；4 资源被占用（Unity 被占用 / 另一个 review 持有租约）；3 该 DEL 已有检测在执行。
- **验收条件覆盖**：只有 profile 模板生成的条款才算“机器可验证”，自动判为 passed。
  复用自定义任务、手写/改写过的条款不会被机器计数冒充通过，会被记为 `not_run` 并让本次检测不通过。
  这类条款只能由检测者显式补证：

```powershell
node tools/collab/cli.mjs stage-review --delivery <deliveryId> --actor codex --coverage AC1=passed,AC2=passed
```

  补证会连同检测者身份（反向授权时还有授权文件 sha256）写入证据的 `attestation`，
  所以“谁声明了这条通过”永远可追溯；不接受未知条款编号（退出码 2）。
- `--authorization <grant.json>`：Cursor 在用户限定授权下做反向复验时，`stage-review` 与
  `review-*` 使用同一套校验（交付、三类指纹、profile、动作、授权人），不再被默认角色表一刀切拦截。

手动路径（排查或需要逐项声明覆盖时）：

```powershell
node tools/collab/cli.mjs review-start --delivery <deliveryId> --actor codex --profile smoke
node tools/collab/cli.mjs review-run   --review <reviewId> --actor codex
node tools/collab/cli.mjs review-finish --review <reviewId> --actor codex --evidence path\to\evidence.json --auto-counters
```

evidence JSON 必填：

```json
{
  "conclusion": "passed",
  "subjectImplementationFingerprint": "<交付里的 implementation.fingerprint>",
  "coverage": [{ "id": "AC1", "result": "passed", "evidence": "run <runToken>：真实输出摘要" }],
  "assertionsTotal": 25,
  "assertionsFailed": 0,
  "assertionsSkipped": 0
}
```

判定规则（不满足即不得 passed）：

- 每条必测 AC 都必须 `passed`；`assertionsFailed>0`、`total=0`、全部跳过都不行；
- 计数必须与工具解析出的机器结果**逐项一致**；
- profile 用 `resultKind` 声明唯一接受的格式：`json` / `nunit` / `slotcheck` / `semcheck` / `text`；
- `resultFile` 必须由**本次** run 产生（runToken + 前后 SHA256 绑定），不能复用旧文件；
- 执行后快照哈希变化、或出现未声明的文件 → 该 run 失败；
- 输出无法识别 → 只能记 `blocked`，永远不会变成 passed。

一条 review 只能执行一次（`review-run-<id>.lock`），第二次调用退出码 3。
终态（passed/failed/blocked/cancelled）不可再 finish，需要新 REV。

不用手写复杂 JSON 的证据草稿：

```powershell
.\tools\collab\Collab.ps1 review-evidence REV-实际编号 passed 'AC1=passed,AC2=passed' '每项实际验证的说明'
.\tools\collab\Collab.ps1 review-finish REV-实际编号 上一步返回的evidencePath
```

草稿从绑定输出读取指纹和计数，未声明的 AC 默认 not_run；它不会自动验收，finish 仍重新核对证据和完整覆盖。多个 profile 需要各自 REV；交付示例：`Collab.ps1 deliver 任务编号 摘要路径 模型名 'compile,slot'`。不要为游戏任务只跑 smoke。

同一作者的用户授权开发例外：Codex 可用 `deliver --actor codex --authorization <grant.json>`，授权必须匹配 projectId、taskId、action、allowedRoots。交付及事件保留授权原文与哈希；同 actor 的复核独立性为 false，只能作为自测，闸门继续等待独立复核。普通 Codex claim/deliver 权限没有放开。

**反向复验例外（Codex 开发 → Cursor 检测）**：使用 Codex 随新交付准备的复验授权文件，以真实 `actor=cursor` 执行：

```powershell
node tools/collab/cli.mjs review-start --delivery DEL-实际编号 --profile collab --actor cursor --authorization 授权文件.json --json
node tools/collab/cli.mjs review-run --review REV-上一步返回编号 --actor cursor --authorization 授权文件.json --json
node tools/collab/cli.mjs review-evidence --review REV-实际编号 --actor cursor --authorization 授权文件.json --conclusion passed --coverage AC1=passed,AC2=passed,AC3=passed --note "按实际条款逐项取证，不以机器全绿代替人工核对" --json
node tools/collab/cli.mjs review-finish --review REV-实际编号 --actor cursor --authorization 授权文件.json --evidence 上一步返回的evidencePath --json
```

以上 AC 编号仅为示例，必须以该交付任务定义为准。授权声明的 projectId/taskId/deliveryId/developerActor、三类指纹、profiles/actions 必须精确匹配。每步均核对同一授权文件的 SHA256 和 REV 作者；不授权时仍退出 7。快照、绑定日志、机器计数和 AC 闸门照常执行。失败要如实写 failed，不得将终态重新 finish。

PowerShell 包装支持尾部 `[actor] [authorization.json]`，例如 `Collab.ps1 review-start DEL-实际编号 collab cursor 授权文件.json`；review-evidence 的 note 后再传 actor 和授权路径。省略 actor 仍为 codex，Cursor 不得省略并冒用默认身份。

review-cancel 也需相同作者与授权；用户可用 `actor=user` 管理性取消。全局 review-recover、缺陷 verified/closed 和任务书总项写入仍按原权限由 Codex/user 处理，不在该复验授权范围内。一个阶段需要多个 profile 时，每个必需 profile 都须有同一最新 DEL 的独立 passed，不能拿开发自测补齐。

---

## 6. 缺陷生命周期

`review-finish` 判 `failed` 且 evidence 里带 `issues[]` 时，会自动生成 `issues/ISSUE-*.json`（原始报告不可改）。
`stage-review` 在进程非零退出、`review-run` 已直接终态失败时，也会由检测方自动登记同结构 ISSUE。

查看可执行清单（Cursor 侧，一条命令）：

```powershell
node tools/collab/cli.mjs stage-fix
```

之后的流转写在共享 `state.json.issueStatus`，`listIssues` 读取时合并成唯一状态：

```powershell
node tools/collab/cli.mjs issue-update --issue ISSUE-xxx --status fixing  --actor cursor --note "开始修复"
node tools/collab/cli.mjs issue-update --issue ISSUE-xxx --status fixed   --actor cursor --note "新交付 DEL-xxx"
node tools/collab/cli.mjs issue-update --issue ISSUE-xxx --status verified --actor codex --note "复验通过"
node tools/collab/cli.mjs issue-update --issue ISSUE-xxx --status closed  --actor codex
```

允许的转换：`open → fixing|fixed|wontfix|closed`、`fixing → fixed|wontfix|open`、
`fixed → verified|wontfix|open`、`verified → closed|open`、`closed → open`、`wontfix → open`。
非法跳转退出码 3；越权退出码 7。**未关闭的 blocker/critical 缺陷会阻塞完成闸门。**

---

## 7. 任务书映射与完成闸门

```powershell
node tools/collab/cli.mjs tasks --json                       # 每个任务的映射/勾选/基线状态
node tools/collab/cli.mjs taskbook-baseline --task T03       # 冻结当前条款文本（记录 scheme）
node tools/collab/cli.mjs gate --task T03 --json             # 单任务闸门
node tools/collab/cli.mjs gate --json                        # 全项目闸门（T01..T06 + §14 C01..C12）
node tools/collab/cli.mjs taskbook-check --task T06 --actor codex   # 仅当闸门全过才允许勾总条款
```

- 工具**只读**任务书正文；唯一会写的是「总条款复选框」，且必须闸门通过。
- 基线哈希**忽略复选框状态**：勾选是结果，不是条款改动。
- 基线带 `scheme`：工具自己升级哈希算法时，会明确提示「重录基线」，不会被误报成「条款被改动」。
- T02 这类任务即使本地全过，只要 `cleanGitCheckoutVerified=false`，总条款仍然不能勾。
- §14 的 C01..C12 由 Codex 依据真实结果勾选，工具不会替你打勾。

---

## 8. Profile 与运行产物

| profile | 真实命令 | 结果契约 |
| --- | --- | --- |
| `compile` | `node tools/semcheck/run.js` | `resultKind=semcheck`，三道门 `errors=0` |
| `slot` | `dotnet run --project tools/slotcheck/slotcheck.csproj -c Release` | `resultKind=slotcheck`，明细与 `SLOTCHECK OK` 汇总一致 |
| `unity-editmode` | `tools/regression/RunUnityTests.ps1 -Platform EditMode` | `resultKind=nunit`，`resultFile=Unity Project/Logs/Tasks20261017_{runToken}.xml` |
| `unity-playmode` | 同上 `-Platform PlayMode` | 同上 |
| `smoke` | `node tools/collab/tests/smoke-profile.mjs` | `resultKind=json` |
| `collab` | `node tools/collab/tests/run-all.mjs` | `resultKind=json`，工具自身全部隔离套件 |

- 运行产物目录（`bin/ obj/ Library/ Temp/ Logs/ Build/ node_modules/`）允许出现，不会当成夹带源码；
- 工具自身写进快照的报告（如 `tools/semcheck/semcheck_report.txt`）必须在 profile 的 `runOutputs` 里**显式声明**；
  未声明的文件仍然被拒绝，且 `runOutputs` 不允许声明 `.cs/.js/.csproj` 等源码文件。

---

## 9. Hooks（可选）

两端事件与适配器：

| 端 | 事件 | 适配器示例 |
| --- | --- | --- |
| Cursor | `sessionStart` / `afterFileEdit` / `taskEnd` | `tools/collab/adapters/cursor-hooks.json.example` |
| Codex | `SessionStart` / `PostToolUse` / `Stop` | `tools/collab/adapters/codex-hooks.json.example`（TOML 备选） |

```powershell
node tools/collab/cli.mjs hooks-status --json
node tools/collab/cli.mjs hook --source cursor --event afterFileEdit --payload .\payload.json
```

- Hook 只做 `sync`，不会 ack、生成交付或验收；重复事件按 source/event 和事件身份去重；不同文件或不同文件版本不会因防抖被丢弃；
- 同一 payload 重复投递返回 `applied:false, deduped:true`；
- 客户端不支持或未接线时，`doctor` 明确提示，手动命令仍构成完整闭环；
- Windows 上带 UTF-8 BOM 的 payload 会被接受（BOM 会被剥离），非 JSON 仍然退出码 2。

---

## 10. 故障排查

| 现象 | 含义 | 处理 |
| --- | --- | --- |
| 退出码 2 | 参数/JSON/payload 用法错误 | 看 `error` 字段；payload 文件必须是合法 JSON |
| 退出码 3 | CAS 冲突或非法状态转换 | `ack` 到最新 revision 后重试；review 终态需新建 REV |
| 退出码 4 | 被锁：Unity 占用 / review 正在执行 | 用户自行保存关闭 Unity；或 `review-recover` |
| 退出码 5 | 检查不通过（快照变化、未列出文件、计数不符） | 重新 `deliver`；不要手工改快照 |
| 退出码 6 | 证据缺失/不完整（缺指纹、计数非整数、AC 未覆盖） | 按 §5 补 evidence |
| 退出码 7 | 越权 | 换正确 actor，或交给该角色执行 |
| `Unity occupied; ... pids=[...]` | 检测到用户 Unity | **不要杀进程**；用户关闭后重跑 |
| `clause text changed since baseline` | 条款确实被改过 | 人工比对任务书，再决定是否重录基线 |
| `baseline recorded under hash scheme legacy` | 工具升级了哈希算法 | `taskbook-baseline --task <id>` 重录，不是条款改动 |
| `snapshot contains unlisted files` | 出现了未声明文件 | 若确为工具产物，加进 profile `runOutputs`；否则视为夹带 |
| `review has an active run` | 上次执行未收尾 | `review-recover --actor codex`（只回收死租约） |

`review-recover` 只会回收**本工具自己的**死租约（`review-run-*.lock`、payload 指向该 review 的 `unity-review.lock`），
绝不会动用户的 Unity 实例或其锁。

---

## 11. 演练记录（2026-10-18，真实执行）

| 步骤 | 命令 | 结果 |
| --- | --- | --- |
| 基线自检 | `collab.test.mjs` / `xml.test.mjs` | 开发者测试全过；XML 26/26 |
| 编译链路 | `review-run --profile compile` | `errors=0 / tests-asm errors=0 / enc errors=0`，REV passed |
| Slot 链路 | `review-run --profile slot` | 371 PASS / 0 FAIL，`SLOTCHECK OK`，REV passed |
| 隔离 Unity | `review-run --profile unity-editmode` | `NUNIT result=Passed passed=25 failed=0`，XML 25/25，REV passed |
| Unity 占用 | 用改名进程模拟 `Unity.exe` 后 `review-run` | 退出码 4，报出占用 PID，**未杀进程**；进程随后由操作者自行停止 |
| Hook 去重 | 同 payload 连发两次 `hook` | 第一次 `applied:true`，第二次 `deduped:true` |
| 失败退回 | 第一条 C-DEMO 交付 | REV failed，自动生成 `ISSUE-…`，任务转 rework |
| 修复复验 | 新交付 + `review-finish` | REV passed；`issue-update` 走完 fixing→fixed→verified→closed |
| 待复验展示 | 临时改 `tools/collab/adapters/README.md` 后 `sync` | `freshness=stale`；按字节恢复后回到 `current`，历史 passed 不变 |
| 完成闸门 | `gate --json` | T01/T06 通过；T02 因 cleanGitCheckout 阻塞；T03/T04/T05 因未勾分项阻塞 |

演练产物：`docs/handoff/verification/CollabTool-C07-C12-2026-10-18/`。

---

## 12. 禁止事项

- 不自动 `git commit` / `push`；
- 不自动强杀 Unity、不删除 Unity 工程锁；
- 不覆盖 `deliveries/`、`reviews/`、`events/`、`issues/` 的历史记录；
- 不用一次完整的 `LogicSelfTests.Run()` 代替隔离回归；
- 不手工编辑 `CURRENT.md` / `NEXT_*.md` / `state.json` 来制造“已完成”状态。
