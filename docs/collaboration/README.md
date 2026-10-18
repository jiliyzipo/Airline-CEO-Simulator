# Cursor × Codex 协作工具 · 用户操作入口

当前使用方式：**Cursor 连续开发、自测 → 阶段结束时一条命令生成 DEL 并在快照内预检 → Codex 一条命令完成检测 → 失败自动登记 ISSUE → 修复后生成新的 DEL**。用户只需说一句话和传递编号，不需要执行逐项检查、手写证据 JSON 或手工创建缺陷。

## 最短路径（推荐）

```powershell
# 开工（替代 doctor + sync + ack + tasks）
.\tools\collab\Collab.ps1 stage open

# Cursor 阶段收口：自动建任务 + 自动摘要 + DEL + 快照内预检 + 交接单
.\tools\collab\Collab.ps1 stage close

# Codex 独立检测：start + run + 证据 + 结论 + 失败自动 ISSUE
node tools/collab/cli.mjs stage-review --delivery DEL-xxx --actor codex

# 不通过时，Cursor 取回可执行缺陷清单
.\tools\collab\Collab.ps1 stage fix
```

- `stage close` 在交接单（`out/collab/handoff/DEL-xxx.md`）里写明检测命令；预检失败时该 DEL 会被 `stage-review` 主动跳过，不会浪费一次独立检测。
- `stage-review` 幂等：同一 DEL 不会重复执行，已检测则直接返回上次结论。
- 详细规则、参数、退出码与故障排查见 [`OPERATION_CHECKLIST.md`](OPERATION_CHECKLIST.md)。

## 最简单的日常操作

1. **新聊天或换模型时**，给 Cursor 或 Codex 一次完整的开场指令，让它阅读 `AGENTS.md`、本文件与 `CURRENT.md`，执行 `sync`，读到最新 revision 后以自己的身份 `ack`。在同一聊天里连续报告新问题时，只要说清要改什么，不必每次重复整段指令；代理仍须按 `AGENTS.md` 自行保持同步。
2. **运行中发现问题时**，直接告诉 Cursor 现象、复现步骤和期望结果。Cursor 可以先修复、自测并记录本阶段改动；任务书可以在修复后按实际结果补充。此时无需逐个问题生成 DEL，也无需立刻请 Codex 跑完整验收。
3. **你决定阶段收口时**，让 Cursor 汇总本阶段的问题、变更、验收条件和测试，核对/补齐任务书；若本阶段尚无协作任务定义，先建立覆盖这批改动的阶段任务。然后按该任务生成一份 DEL，把编号交给你。工具的 `deliver` 必须关联已有任务 ID，不能对没有定义的任务直接交付。
4. **集中验证时**，把阶段 DEL 交给 Codex，并说明该阶段需要覆盖的功能。Codex 按实际验收条件选择必要的 profile，形成 REV。一个阶段也可有多份 DEL；每份需要单独关联相应任务和检测记录。检测失败则 Cursor 修复后生成新 DEL。

`sync`/`ack` 只更新共享状态与已读标记，**不会自动运行 Unity 或验收**。DEL 表示可供检测的交付快照；REV 表示一次检测结果。阶段还在开发时，工作区有改动、状态显示待复验都是正常的。

两端均打开 `C:\Users\jiliy\Airline-CEO-Simulator`。以下指令按所处阶段发送：

给 Cursor（**阶段开始或新聊天**）：
> 阅读 AGENTS.md、CURRENT.md 和本阶段已有任务记录，先 sync，再确认读取最新 revision 并 ack。本阶段按我陆续报告的问题开发和自测，记录各项改动；我明确要求阶段收口时，再整理任务定义、验收条件和任务书，生成 DEL 并说明变更、测试、已知缺口。不要写“独立验收通过”，不要提交或推送。

同一聊天里继续报问题：
> 新问题：【现象与复现步骤】。请先修复并自测，记入本阶段记录；暂不生成 DEL，等我说“阶段收口”。

准备集中验证时给 Cursor：
> 请对本阶段收口：汇总已修问题和未完成项，核对或补齐任务书与验收条件，在协作工具中建立或确认对应阶段任务，执行必要自测后生成 DEL，给我交付编号。

给 Codex（**阶段 DEL 产生后**）：
> 阅读 AGENTS.md 和最新协作记录，先 sync/ack。检测交付 DEL-实际编号，按任务必需 profile 在固定快照运行并检查 AC 覆盖；通过后写 REV，失败写具体缺陷。Unity 被占用时等待我保存关闭。不要改变游戏参数或提交、推送。

换聊天或换模型时让新模型读取同一任务与最新 DEL/REV/ISSUE，继续原任务。`CURRENT.md` 显示双方各自 ack 到哪个 revision；文件更新不等于另一端已经读过。

需要较快显示源码变化时，在独立终端运行 `.\tools\collab\Collab.ps1 watch`。前台监听发现相关变化后会保守标为待复验；撤销变更后运行 sync 重新核对哈希。Ctrl+C 停止，不安装常驻服务。

默认已选择**手动模式**：`sync/ack + watch` 不依赖客户端 Hooks。Hooks 桥接可选，原生加载与客户端信任未实测时始终显示未验证，CLI 的 applied/deduped 计数不替代该证据。

本目录由 `node tools/collab/cli.mjs` 管理。**CURRENT.md / NEXT_*.md 为程序生成，勿手工维护第二套状态。**

> 逐步打勾的操作检查表：[`OPERATION_CHECKLIST.md`](OPERATION_CHECKLIST.md)（每天开工、收口、验收照做即可）
>
> 完整中文手册（权限矩阵、全部命令、故障排查、演练记录）：[`OPERATOR_MANUAL_ZH.md`](OPERATOR_MANUAL_ZH.md)

## 安装（首次）

在仓库根：

```powershell
node tools/collab/cli.mjs doctor
node tools/collab/cli.mjs init
node tools/collab/cli.mjs sync
node tools/collab/cli.mjs status --json
# 或使用 PowerShell 包装（少写 JSON）：
.\tools\collab\Collab.ps1 doctor
.\tools\collab\Collab.ps1 sync
```

重复 `init` 不会覆盖已有任务/规则。

## 每天

1. 打开 `docs/collaboration/CURRENT.md`
2. 同步并确认读取：

```powershell
node tools/collab/cli.mjs sync
node tools/collab/cli.mjs ack --actor cursor --revision <CURRENT里的revision>
```

Codex 侧将 `cursor` 换成 `codex`。

## Cursor 开发闭环（示例 C-DEMO）

```powershell
node tools/collab/cli.mjs claim --task C-DEMO --actor cursor --expected-revision <rev>
# ... 开发 ...
# 写一份摘要文本后：
node tools/collab/cli.mjs deliver --task C-DEMO --actor cursor --summary-file path\to\summary.txt --model deepseek-v4.1-flash --profiles smoke
```

把输出的 `deliveryId` 交给 Codex。不要自行勾选“独立验收通过”。

## Codex 检测闭环

```powershell
node tools/collab/cli.mjs review-start --delivery <deliveryId> --actor codex --profile smoke
node tools/collab/cli.mjs review-run --review <reviewId> --actor codex
# 按真实输出核对条款后，显式声明覆盖并生成证据草稿：
node tools/collab/cli.mjs review-evidence --review <reviewId> --conclusion passed --coverage AC1=passed,AC2=passed --note "说明每项实际检查" --json
# 使用上一步返回的 evidencePath：
node tools/collab/cli.mjs review-finish --review <reviewId> --actor codex --evidence path\to\evidence.json
```

evidence 必须含：
- `subjectImplementationFingerprint`（与交付实现指纹一致）
- `coverage`（覆盖任务全部必测 AC，且均为 `passed` 才可结论 passed）
- `assertionsTotal` / `assertionsFailed` / `assertionsSkipped`（必须是有限非负整数；禁止全跳过仍判通过）

工具会解析 `review-run` 的真实机器结果并与 evidence 计数**逐项核对**：
- profile 用 `resultKind` 声明唯一接受的格式：`json` / `nunit` / `slotcheck` / `semcheck` / `text`（`text` 只接受已知适配器）；
- `resultFile` 必须由**本次 run** 产生（runToken、前后 SHA256/时间戳绑定），不得复用旧文件；
  结果路径支持 `{runToken}` 占位符（Unity profile 用它保证每次执行路径唯一）；
- stdout/stderr 也有绑定：每次 run 写自己的 `run-<runToken>/` 目录并记录字节数与 SHA256，
  finish 先核对字节再解析；事后改写日志会以退出码 5 拒绝，顺序重跑不会覆盖旧证据；
- 空 stdout、失败断言、全跳过、计数不一致、计数非整数、格式未知一律不得 passed；
- NUnit XML 必须属性齐全、是**完整合法的 XML 文档**，并与 `<test-case>` 明细一致：
  截断、未闭合、错误嵌套、重复根、根标签掩盖 Failed 用例都会被拒绝；
- 运行中源码被改动（快照哈希变化）会让 run 失败、finish 也会被拒绝；
- profile 未声明机器结果契约且输出无法识别时，review 只能记为 `blocked`（终态、不可改写成 passed）。

同一条 review 由执行租约（`review-run-<id>.lock`）保证最多执行一次：第二个进程会得到
退出码 3（冲突）而不是重复执行。每次 run 的 token 也用于保护写回与锁释放。

减少手写 JSON：

```powershell
node tools/collab/cli.mjs review-finish --review <id> --actor codex --evidence path\to\evidence.json --auto-counters --coverage AC1=passed,AC2=passed,AC3=passed
```

`--auto-counters` 从机器结果填充断言计数；`--coverage` 生成覆盖率条目。禁止只传 `--passed`。
已 `finishedAt` 的报告（passed/failed/blocked/cancelled）不可再次 finish（需新 REV）。

中断恢复：

```powershell
node tools/collab/cli.mjs review-recover --actor codex
# 或
node tools/collab/cli.mjs review-cancel --review <reviewId> --actor codex
```

若提示 Unity 占用：用户自行保存关闭 Unity 后再 `review-run`。工具不杀进程、不删 Unity 锁。PowerShell 无法启动时按占用未知 fail-closed。

真实 Unity profile（不自动串联，需用户确认后手动执行）：

```powershell
node tools/collab/cli.mjs review-start --delivery <id> --actor codex --profile unity-editmode
node tools/collab/cli.mjs review-run --review <id> --actor codex
```

`unity-editmode` / `unity-playmode` 调用 `tools/regression/RunUnityTests.ps1`，在隔离快照内运行；
项目根为快照内的 `Unity Project/`，结果文件为 `Unity Project/Logs/Tasks20261017_<runToken>.xml`。
只有 `companyName: AirlineCEORegression` 时才允许，且快照启动前会重查 Unity 占用。
Unity 资源锁绑定 `reviewId`，只有持有该 review 的 finish/cancel 才会释放。

## 快照与运行产物

- 交付快照只复制实现/验证白名单文件，并记录每个文件的 SHA256（`snapshotSha256`）；
- 需要但不在白名单目录里的单文件用 `demoSnapshotFiles` 精确加入（如 smoke 读取的 `docs/collaboration/PROTOCOL.md`）；
- 导出后立即检查未列文件（`unlistedAtCreation` 必须为 0），不干净的交付不会发布；
- review-run 前校验清单与哈希，review-run 后再校验一次，finish 时再校验一次；
- 运行产物（`bin/ obj/ Library/ Temp/ Logs/ Build/ node_modules/` 等）不算“新增源码”，
  允许保留，因此同一交付可以不改快照直接复验；
- 运行产物之外的未列出文件仍会被拒绝（防止偷偷塞入可编译 `.cs`）。
- 开发方自测 `collab.test.mjs` 先在临时目录搭 scaffold 再运行 CLI，不会向被测试目录写入协作状态。

## 任务状态与缺陷

```powershell
node tools/collab/cli.mjs tasks --json                     # 任务、映射、勾选、基线
node tools/collab/cli.mjs gate --task T03 --json           # 单任务完成闸门
node tools/collab/cli.mjs gate --json                      # 全项目闸门
node tools/collab/cli.mjs issue-update --issue ISSUE-xxx --status fixing  --actor cursor
node tools/collab/cli.mjs issue-update --issue ISSUE-xxx --status verified --actor codex
```

缺陷原始报告不可改写；`open→fixing→fixed→verified→closed` 的转化记录在共享 `state.json.issueStatus`，
未关闭的 blocker/critical 会阻塞完成闸门。Cursor 不能自证 `verified/closed`。

## 任务书映射

```powershell
node tools/collab/cli.mjs taskbook-baseline --task T03   # 冻结条款文本（含 scheme）
node tools/collab/cli.mjs taskbook-check --task T06 --actor codex   # 闸门全过才可勾总条款
```

基线哈希忽略复选框状态；工具升级哈希算法时会要求「重录基线」，不会被误报成条款改动。

## 监听（可选）

```powershell
node tools/collab/cli.mjs watch --interval 3
```

前台 Ctrl+C 停止。默认不跑 Unity、不提交、不唤起另一端模型。

## Hooks

见 `tools/collab/adapters/README.md`。当前两端均选择手动模式。`hooks.enabled` 只允许 CLI 接收事件，不表示客户端配置已安装、已信任或已触发。桥接只 sync，不 ack、不交付、不自动验收。

## 自测与角色例外

协调工具自身的完整开发回归：`node tools/collab/tests/run-all.mjs`，或对交付选用 `collab` profile；`smoke` 只有输入存在检查，不能代替完整回归，更不能代替游戏验收。

用户明确授权 Codex 开发时，使用匹配任务及源码范围的 `--authorization` 文件，以 `actor=codex` 生成交付。相同 actor 的检测标为 `developer-self-check`、`independent=false`，完成闸门不把它算作独立验收。授权标签仍不是操作系统身份认证。

Codex 开发的工具需要 Cursor 反向复验时，由 Codex 准备绑定**具体 DEL、实现/验证/合同指纹与 profile**的独立复验授权文件。Cursor 每个 review 命令都须传 `--actor cursor --authorization 授权文件`，生成真实 Cursor 作者的独立 REV；授权原文与 SHA256 随 REV/event 留档，后续命令拒绝换授权或换作者。授权不开放 Cursor 的缺陷 verified/closed、任务书写入或全局 recover 权限，这些由 Codex/user 根据实际复验结果处理。普通 Cursor 自验自己的交付仍被拒绝。用户无需手工切换身份。

## 停用

停止 `watch`；不要删游戏文件、历史 deliveries/reviews。可保留 `docs/collaboration` 记录。
