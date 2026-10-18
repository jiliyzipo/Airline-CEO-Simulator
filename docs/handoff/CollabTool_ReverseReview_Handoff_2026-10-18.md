# 协调工具角色修复与 Cursor 独立复验交接

日期：2026-10-18。执行者：Codex。用户已授权完成协调工具开发，并让 Cursor 保持现场，由 Codex 接手其反馈。

## 结论与受检对象

开发修复与固定快照自测完成，**正式独立验收待 Cursor 实际执行**。本文件不是 Cursor 的验收报告。

- 新交付：`DEL-20261018071756-010504`，任务 `C-COLLAB-FINAL`，真实开发者 `actor=codex`。
- 快照：`out/collab/DEL-20261018071756-010504`。
- 实现指纹：`19e78d1785779ee232d31a76c7b902d225e30114902f20de80f68a48f3b60547`。
- Cursor 限定复验声明：`docs/handoff/CollabTool_Cursor_Review_Authorization_2026-10-18.json`。
- 授权文件 SHA256：`99af662aa72fb04390da7547589526258802e20d6c89c5b19313e5ca4f248f3b`。
- 完整自测汇总：`docs/handoff/verification/CollabTool-ReverseReview-2026-10-18/delivery-010504/summary.json`。

旧 `DEL-20261018051637-e21eae` 及其开发自测 `REV-20261018051650-e59d57` 保留。Cursor 对旧 DEL 的 `cursor-replication/replication.json` 是反方复跑证据；不将它改造成正式 REV，也不借它自动验收本次新代码。

## 本次修复的两个具体问题

### RR-01：反向独立验收无合法角色通道

原因：工具给 Codex 的开发交付增加了限定例外，但 review-start/run/finish 仍只认可 Codex，review schema 也拒绝 Cursor。Codex 对自己交付的检测又正确标为 independent=false，结果没有可以真实登记 Cursor 独立复验的路径。

修复：默认权限表保持原意；新增限定复验声明。Cursor 只可检测声明指定的 Codex 交付，声明绑定 projectId/taskId/deliveryId/developerActor、三类指纹、profile 和动作列表。每个命令须使用真实 actor=cursor 并传同一授权文件，REV 与创建事件保存全文和 SHA256。后续步骤拒绝更换声明、跨作者执行或改写结论。

覆盖动作：review-start/run/evidence/finish/cancel。未授权 Cursor 仍退出 7；Cursor 自验自己的交付仍拒绝。原有快照完整性、日志哈希、结果计数、AC 全覆盖、终态不可改写继续生效。用户可管理性取消；Codex 不能以自身身份完成 Cursor 的 REV。

这不是身份认证系统：本机授权 JSON 和 actor 是协作声明，执行者须如实使用自身身份，不能用“换 actor”代替独立执行。

Cursor 缺陷 verified/closed、任务书总项回填、全局 recover 权限没有放开。Cursor 独立复验后生成报告，由 Codex/user 核查真实证据再处理缺陷状态与清单。

### RR-02：必需 profile 可混入开发自测结果

原因：原独立性闸门只检查最新整体验收；required-profiles 的已覆盖集合只检查 status=passed。于是同一 DEL 上，一个 profile 的独立通过可能使另一个 profile 的同作者自测被算作正式覆盖。

修复：每个必需 profile 的最新记录必须同时 passed 且不是 independent=false。新测试 R11 同时验证拒绝自测的负例、全部独立通过的正例。历史缺少 independent 字段的旧记录继续按原兼容规则处理，不改写旧报告。

## C09/C10 与正式清单状态

- C09 原缺陷：缺基线/缺文档误放行、依赖异常、旧通过覆盖新失败、借用其他交付的 profile、并发缺陷状态丢失等。完整 closure **13/13** 在本次新快照通过；daily 的条款冲突/勾选撤回/实时 freshness 等 **21/21** 也通过；新增 R11 覆盖多 profile 独立性遗漏。
- C10 原并发去重：closure G08 并发只应用一次且重试去重通过；daily 的事件隔离、快速不同编辑、stdin 和手动模式通过。**未验证原生客户端 Hooks**，当前仍选手动 sync/ack 与可选前台 watch，适配器示例不等于已加载。
- 需求手册 §14 仍 **2/12（C07、C12）**；未新增任何正式验收勾选。开发清单的 12/12 只表示开发自测。
- `ISSUE-20261018041847-27c5bf` 保持 fixed、待独立复验，不提前 verified/closed。
- 反向复验应逐条给出 C01–C12 的证据与建议，而不是看机器全绿就直接勾 12/12。C10 应准确写“手动模式可用，原生 Hooks 未验证”，不能伪称原生加载已完成。C07 可核对已有工程证据范围，不得说成本次重跑 Unity。

## 已运行的开发检查

以下全部以本次新 DEL 为 subject，驱动显式覆盖 COLLAB_AUDIT_TOOL_SOURCE，避免残留环境指向旧版本：

- 完整 collab 套件：**112/112**（developer38、XML26、closure13、daily21、reverse-review14），失败/跳过均为 0。
- 外部 baseline24：**24/24**；supplemental：**12/12**；boundary8：**8/8**。已有脚本与更严格的终态/恢复判据保持原样。
- XML 结果闸门：**12/12**。
- 快照完整性与工作区身份：见同目录 `final-state.json`。
- 游戏源码/资源/设置、两份原任务书及已有 DEL/REV/ISSUE/event 共 **1401 文件哈希无变化**，见 `preservation-before.json`、`preservation-after.json`。

以上不同套件存在覆盖重叠，不合计成独立用例总数。工作区先通过112，之后新 DEL 又独立跑112；交付判定以新 DEL 的结果为准。

保留了最初的测试脚手架失败（11/14、13/14）与修复后14/14结果；失败是测试访问了不存在的 listEvents/stdoutEvidence/parts 或缺映射条款，已按真实接口修正，不放宽业务断言。初次沙盒运行在临时 state.json 原子重命名处 EPERM，未进入业务断言；正常 Windows 权限重跑后才计产品结果。

排队自测 `REV-20261018071821-7f263c` 已由 Codex 取消，原因是相同快照完整驱动已在运行，避免再重复一次同套件；它未执行，不作为失败或独立通过。开发结果以驱动的真实日志保存，等待 Cursor 新建真实独立 REV。

## Cursor 接手：只需照此执行

本节命令供 **Cursor 本人**执行，Codex 不冒用该身份运行。项目根目录为 `C:\Users\jiliy\Airline-CEO-Simulator`。本次不启动 Unity，不改游戏代码或原任务书，不 commit/push，不启用原生 Hooks。

1. 读 AGENTS.md、本文件、CURRENT.md、任务 `C-COLLAB-FINAL`、新 DEL 与授权 JSON。执行 sync，读取返回的最新 revision 后，以 cursor 身份 ack；不要照抄旧 revision。

```powershell
node tools/collab/cli.mjs sync --json
# 将下条的数字替换为刚才读到的 revision：
node tools/collab/cli.mjs ack --actor cursor --revision 最新数字 --json
```

2. 以真实身份建立独立复核。授权只覆盖此 DEL 的 collab profile：

```powershell
node tools/collab/cli.mjs review-start --delivery DEL-20261018071756-010504 --profile collab --actor cursor --authorization docs/handoff/CollabTool_Cursor_Review_Authorization_2026-10-18.json --json
```

3. 用上一步生成的新 REV 编号执行正式112项套件；不要复用或覆盖已取消的 Codex REV：

```powershell
node tools/collab/cli.mjs review-run --review REV-新编号 --actor cursor --authorization docs/handoff/CollabTool_Cursor_Review_Authorization_2026-10-18.json --json
```

4. 对同一快照复跑外部与 XML 闸门。标签使用新 REV，避免覆盖旧证据：

```powershell
node docs/handoff/verification/CollabTool-Codex-Final-2026-10-18/run-external.mjs --subject out/collab/DEL-20261018071756-010504/tools/collab --label cursor-REV-新编号
$env:COLLAB_AUDIT_TOOL_SOURCE = (Resolve-Path 'out/collab/DEL-20261018071756-010504/tools/collab').Path
$env:COLLAB_AUDIT_RUN_LABEL = 'cursor-REV-新编号-xml'
node docs/handoff/verification/CollabTool-C07-C12-2026-10-18/xml-gate-audit.mjs
```

5. 逐项判断任务 AC1/AC2/AC3：AC1 正式套件、AC2 外部检查/XML、AC3 文档/操作/角色边界。记录原始输出及具体路径。全部满足才可执行以下 passed 草稿；有失败则使用 failed 并记录具体问题：

```powershell
node tools/collab/cli.mjs review-evidence --review REV-新编号 --actor cursor --authorization docs/handoff/CollabTool_Cursor_Review_Authorization_2026-10-18.json --conclusion passed --coverage AC1=passed,AC2=passed,AC3=passed --note '替换为本次各AC实测结论、runToken与外部证据路径' --json
node tools/collab/cli.mjs review-finish --review REV-新编号 --actor cursor --authorization docs/handoff/CollabTool_Cursor_Review_Authorization_2026-10-18.json --evidence 上一步真实返回的evidencePath --json
```

6. 确认新 REV：actor=cursor、reviewException=true、independent=true、profile=collab、subject 指纹/授权SHA匹配。finishApplicability 应按实际反映当前工作区。若已变化，历史快照可通过，但当前版必须待复验，不能强行覆盖状态。

7. 另写独立复验报告：列 C01–C12 逐条依据，明确 C09/C10 原缺陷是否复现、手动模式与原生 Hook 的边界；报告关联旧 ISSUE 和新 DEL/REV。**暂不修改正式清单，不代做 ISSUE verified/closed**。将报告路径和 REV 编号交回用户，由 Codex 核查后完成有证据的状态收口。

遇到权限7，核查本次授权和命令；不得改成 actor=codex/user 绕过。遇到中断先报告，必要时由 Codex/user 用原权限 recover。缺陷失败不得伪造通过；终态不可再次 finish。

## 用户后续日常操作

恢复游戏开发后：在同一 Cursor 聊天连续报告问题即可，Cursor 先修复、自测、记入阶段；用户说“阶段收口”时再汇总任务书/验收条件并生成 DEL；把阶段 DEL 交给 Codex 集中验证。此次反向授权只针对协调工具自身的特定 Codex 交付，不改变正常的 Cursor 编程 → Codex 检测分工。

T02 干净检出未验证、T03/T04/T05 未完成项继续保留。全项目 gate=false 可以继续成立，不以清除真实游戏阻塞作为协调工具可用的前提。
