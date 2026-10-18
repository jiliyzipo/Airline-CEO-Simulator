# 协作工具独立复验报告

日期：2026-10-17。验收方：Codex。编号：ACCEPT-COLLAB-20261017-CODEX-02。

**结论：新交付仍不通过。原 24 项检查已独立复跑通过，但 §14 补充检查 12 项中 11 项失败，不能宣布工具整体验收完成。**

本报告区分已验证修复、尚存缺陷和未执行的集成验收，不否定本轮已经有效修复的部分。未修改生产业务代码，未替 Cursor 修实现，未启动真实 Unity，未执行游戏完整自测，未提交、推送、重置或清理项目。

## 1. 复验对象与证据归属

- 新交付：[DEL-20261017095048-cfd9dd](../collaboration/deliveries/DEL-20261017095048-cfd9dd.json)。快照：`out/collab/DEL-20261017095048-cfd9dd`。
- 开发说明：[CollabTool_Delivery_Reaccept_2026-10-17.md](CollabTool_Delivery_Reaccept_2026-10-17.md)。
- 上轮报告：[ACCEPT-COLLAB-20261017-CODEX-01](CollabTool_IndependentAcceptance_2026-10-17.md)。
- 验收依据仍为 [需求与操作手册 §14](../Development%20Manual/Development%20Task%20Book/Task%20book/Cursor_Codex_协作工具_开发需求与操作手册_v1.0_2026-10-17.md#14-mvp-交付与验收清单)，以及 §7/§8 的结果解析、验证基线与生成状态排除要求。补充检查没有新增产品范围。

分别检测新 DEL 内工具代码和当前工作区工具代码。两者 `tools/collab` 的 18 个文件清单哈希相同：`bfb45c45ce205e84c592009aefe1c744379c85cc2c888515e337e574209967a0`。这是工具源码范围，不是新 DEL 的整体实现指纹 `576f5bc5cc37450015f00cbfb1cfe4853c770c438d0c61ac8bd9b4e6fa6cf1f9`。

另行只读核对了实际新 DEL 的 29 项快照文件，均与交付清单相符，无缺失或哈希不符，见 [快照核对记录](verification/CollabTool-Reaccept-2026-10-17/delivery-integrity-inspection.json)。本报告指出的是校验机制仍有遗漏，不是断言这份真实交付已被篡改。

按 AGENTS.md 执行根项目 sync，并 ack revision 29；生成摘要和 ack/事件由工具正常更新。行为负例全部在新建的专用 fixture 中执行，不更改真实 C-DEMO、T01～T06 的验收结论。原 DEL、原 REV、新 DEL 和游戏任务书均保留。

## 2. 实际执行结果

两个受检版本结果一致，且最终结果没有 testError：

- 开发方自带测试：13/13 通过。
- 原独立脚本：24/24 通过，退出 0。
- 新补充脚本：12 项中 1 项通过、11 项失败，退出 1。

原 24 项中有一项是执行开发方整组测试，故不要把 13、24、12 直接相加当成独立用例总数。初次受限沙箱运行遇到临时文件替换 EPERM 和 CIM 权限限制，最终统计采用排除这些环境限制后的复跑，未将其作为产品缺陷。

证据文件：

- [当前代码原 24 项结果](verification/CollabTool-Reaccept-2026-10-17/current-baseline24-results.json)、[新 DEL 代码原 24 项结果](verification/CollabTool-Reaccept-2026-10-17/delivery-baseline24-results.json)。
- [当前代码补充结果](verification/CollabTool-Reaccept-2026-10-17/current-supplemental-results.json)、[新 DEL 代码补充结果](verification/CollabTool-Reaccept-2026-10-17/delivery-supplemental-results.json)。
- [当前开发方测试输出](verification/CollabTool-Reaccept-2026-10-17/current-developer-tests.stdout.txt)、[新 DEL 开发方测试输出](verification/CollabTool-Reaccept-2026-10-17/delivery-developer-tests.stdout.txt)。
- [本轮所用原 24 项脚本副本](verification/CollabTool-Reaccept-2026-10-17/baseline24-rerun.mjs)、[补充脚本](verification/CollabTool-Reaccept-2026-10-17/supplemental.mjs)。

已对照原脚本和 Cursor 修改的脚本：修改包括为成功 fixture 补交付指纹，以及将 N17 的 rename 屏障改成适配 state.lock 的双进程竞争。前者让正例满足新前置条件，后者避免在锁内等待另一进程造成测试死锁，可以接受；但 N03 仍将“缺指纹”和“缺真实结果”组合成一个负例，缺指纹被拒绝并不能证明结果已可靠解析。S01～S03 因此单独检验结果闸门。

原 24 项脚本输出中的 `referencedDelivery/referencedReview` 字段仍硬编码为上轮 ID，原始输出未改写；本轮实际受检源以其 `subjectToolsPath`、文件清单/哈希和本报告的新 DEL 绑定为准。

## 3. 已验证有效的修复

以下是代码和行为已验证的局部结论，不能延伸为整项 C01～C12 通过：

- N02：review-run 的 cwd 已切换到快照，工作区 B 不再替换快照 A 的执行输入。
- N03～N07：缺指纹、必测 AC 未通过、全部 skipped 会被拒绝；顺序重复 finish 被拒绝；结束时工作区变化会标 stale。
- N08～N10：稳定合同字段已排除操作状态；单次 ack 不再改变指纹；Assets/Tests 修改可进入验证指纹。
- N11、N12、N15～N19：普通非 demo 导出、ack 前置、无变更 sync 不推进 revision、失败任务状态同步、双 claim 竞争、PowerShell 无法启动时 fail-closed、按 deliveryId 配对等检查通过。
- N20：测试 worker 死亡后，sync 能识别中断状态；CLI 有 recover/cancel 入口。
- S10：`Collab.ps1 ack codex` 已在隔离副本实际运行，参数传递和 ack 成功。不能因此推定全部包装命令及用户工作流已演练通过。

## 4. 尚存缺陷：补充检查已复现

### RQ-01：真实结果仍未解析，存在错误通过（阻断；S01、S02、S03）

位置：`tools/collab/lib/commands.mjs:778–815`。

finish 只判断 stdout 文件存在，没有解析结果内容、成功标记、XML/JSON、实际断言数，也没有核对调用方计数是否与真实输出一致。

- S01：命令退出 0，真实 stdout JSON 明确 `assertionsFailed:1`，调用方 evidence 填失败 0、全部 AC passed，结果仍为 `passed/current`。
- S02：命令退出 0，stdout 为 **0 字节**，没有结果文件，仍可 `passed/current`。
- S03：三项计数填 `not-a-number`，经 Number 转成 NaN，比较闸门失效；报告仍通过，写入 JSON 的计数成为 null。

修复要求：不同 profile 定义并实际解析其结果格式；绑定结果路径与哈希，核对所属 run/交付，缺失、失败、全跳过、格式异常或计数不一致均不得通过。计数必须是有限非负整数并满足总数关系。不能把“存在 stdout”当作“存在真实通过结果”。本问题对应原 QA-02，尚未关闭。

### RQ-02：交付操作仍使自己立即过期，工具测试仍污染实现身份（阻断；S04、S12）

位置：`fingerprint.mjs:26–34、69–74、151–153`，配置 demoSnapshotRoots，以及 deliver 写交付/任务状态的调用链。

排除了事件和 CURRENT，但仍扫描 `docs/collaboration/deliveries`、`reviews`、可变任务 JSON。S04 在默认形态的 demo 根下仅 deliver 再 sync，没有业务编辑，指纹就改变，立即 stale。实际根项目的 CURRENT 在本轮接手时也显示新 DEL stale；实际工作区还包含 README 和任务文件的后续变化，不能将该实际差异全部归因于自失效，因此以独立 fixture 为因果证据。

S12 仅新增 `tools/collab/tests/OnlyVerification.mjs`，实现和验证指纹同时变化；当前只从实现清单排除 Assets/Tests，未排除工具测试。这样 QA 补测试仍会被计入 Cursor 实现交付身份。

修复要求：实现输入排除交付、验收、生成状态与可变流程字段；验证专用输入独立归类；稳定合同单独计算。报告/测试变化不能冒充实现变化。本问题对应原 QA-04，尚未完整关闭。

### RQ-03：快照完整性存在盲区，隔离设置可被绕过（阻断；S05、S06）

位置：`commands.mjs:109–126`，尤其 `:119`；`snapshot.mjs:52–68`。

当前校验只逐一遍历原清单，并直接跳过整个 ProjectSettings.asset：

- S05：将隔离 companyName/productName 恢复为原游戏身份，同时修改 scriptingDefineSymbols，review-run 仍执行。整份设置跳过校验会失去隔离身份和实际编译条件的保障。
- S06：快照中新增原清单没有的 C# 文件，review-run 不拒绝；真实编译的通配符可能将其作为额外输入。

修复要求：分别保留原始源清单和经过允许 overlay 后的快照清单，对实际运行文件进行完整集合校验；ProjectSettings 必须严格限制并核对允许的隔离替换，不可整文件豁免。必要时执行前后都验证输入变化。本问题对应原 QA-01/QA-07 的剩余边界。

### RQ-04：smoke 完成会删除别的验收资源锁（阻断；S07）

位置：`commands.mjs:898–900、951–953`，`io.mjs` 的 release 逻辑。

S07 在 fixture 创建另一个验收持有的 `unity-review.lock`，完成一个从未取得 Unity 锁的 smoke review 后，该锁被删除。finish/cancel 无条件 force release，没有校验 reviewId 或持有凭据。

这次删除的是**测试目录内模拟的本工具资源锁**，不是用户 Unity 工程锁；本轮未触碰真实 Unity 锁。修复必须让资源锁绑定具体 review 和运行所有权，仅持有者释放。不能用“不删除 Unity 工程锁”代替本工具资源锁的正确性。本问题对应原 QA-07/C05，尚未关闭。

### RQ-05：取消终态会被仍在运行的 worker 覆盖（阻断；S08）

位置：`commands.mjs:660–707、709–722、921–954`。

启动测试自己的短时 Node 命令，在 worker 运行中调用 review-cancel，得到 cancelled；命令完成后 worker 用旧内存对象覆盖同一报告，最终又变成 running，finishedAt 丢失。新增“终态禁止重复 finish”无法保护此并发路径。

修复要求：review 的全部更新统一使用资源锁/CAS/状态机；run 写回前检查最新终态。取消可采用安全停止本次自有进程或明确 pending 状态，但不能先放锁并宣称取消完成，随后又被写回运行。本轮只等待自有短时进程自然结束，没有终止用户进程。本问题对应原 QA-03/QA-08，尚未关闭。

### RQ-06：sync 的任务写入在锁外，可覆盖已成功 claim（阻断；S09）

位置：`state.mjs:166–180、241–249、263–271`。

只为最后 state 保存加锁，不能保护此前任务读写。S09 使用受控交错：sync 已读取旧任务、尚未替换文件时，插入一次成功的 claim，然后继续 sync。结果任务文件 holder=null，state 索引 holder=cursor，已成功领取被覆盖。

该测试只在测试进程内调整文件替换的时序，未修改受检实现；它代表 sync/watch 与另一端 claim 的有效并发顺序。原 N17 两个 claim 通过只证明 claim 路径之间受锁保护，未覆盖 sync。

修复要求：让 sync/recover/claim/deliver/review 全部对共享任务和索引使用统一事务边界或冲突检测；不可仅给 saveState 外套锁。本问题对应原 QA-06，尚未完整关闭。

### RQ-07：非 task schema 仍接受明显非法结构（高；S11）

位置：`schemas.mjs:73–143`。

task 的部分类型已补齐，但交付、检测、缺陷、事件仍主要检查字段存在。S11 分别传入 files/fingerprints 为数字、taskRevision 为负、时间和标识为数字、非法 severity/status 等，四类验证均返回 true。

修复要求：逐类校验全部必需字段的类型、枚举、结构、标识、路径与时间，不要用“一个 task 同时含多个坏字段被拒绝”代表整套 schema 完整。需逐字段负例。本问题对应原 QA-09，尚未关闭。

## 5. 文档记载与尚待确认的范围

**代码静态确认的缺口：** 当前配置仍只有 compile/slot/smoke，未提供 Unity profile 的实际命令适配。taskBookStatus 由初始化常量和任务 JSON 维护，未见从权威任务书读取变更、完成闸门映射的实现。Hooks 仍是“需自行核对客户端格式”的示例；doctor 只确认示例存在，不能证明当前两端事件可用。

**文档记载：** companyName 改为 AirlineCEORegression、恢复命令和 PowerShell 包装已经加入；其中公司名与现有 RunUnityTests.ps1 的静态要求一致，恢复入口/ack 包装有本轮局部执行证据。

**尚待确认：** 真实 compile/slot/隔离 Unity 的完整串联及结果绑定、真实占用后恢复重查、同一 review 重复运行及 Hooks 去重、客户端重开/换模型恢复、T02 的 cleanGitCheckoutVerified=false 阻断总完成、任务书内容变更后的自动同步，以及全部中文日常操作演练。本轮没有启动真实 Unity，也没有运行实际游戏编译和 slot profile；不能把 Node fixture 当作这些集成测试通过。

即使修完 S01～S12，仍须完成 §14 尚缺的上述验收，不以补充脚本分数代替整个需求清单。

## 6. §14 逐项状态

本次保持手册原勾选不动。以下是本报告的独立判定：

- [ ] **C01 未通过**：task 局部修复有效，其他四类 schema 和证据计数仍失败，S03/S11。
- [ ] **C02 部分通过，暂不整项勾选**：单进程 init/doctor/status/sync/ack 检查通过；sync 在实际协作并发边界会覆盖领取，S09。
- [ ] **C03 未通过**：合同/Assets 测试归类有改进，生成交付/流程状态和工具测试边界仍错，S04/S12。
- [ ] **C04 未通过**：固定 cwd 和复制变化保护通过；快照新增文件及隔离设置可绕过校验，S05/S06。
- [ ] **C05 未通过**：两个 claim 竞争通过；sync 并发丢状态、无归属释放资源锁仍失败，S07/S09。
- [ ] **C06 未通过**：原 fixture 能走通，真实可靠闭环受结果和状态机问题阻断，S01～S03/S08。
- [ ] **C07 未完成**：仍缺实际 Unity profile/完整串联；真实结果解析仍不可靠，S01/S02。
- [ ] **C08 未通过**：顺序重复 finish 保护及结束重算有效；生成记录自失效、验证身份混合及并发覆盖未关闭，S04/S08/S12。
- [ ] **C09 未完成**：只读状态显示更明确，但未见权威任务书动态映射和总完成闸门的实际演练。
- [ ] **C10 未完成**：规则/手动入口存在，客户端 Hook 能力检测与可用性/重复事件演练未完成。
- [ ] **C11 未通过**：死 worker 可标中断，但运行中取消被覆盖且资源锁归属错误，S07/S08。
- [ ] **C12 部分实现，未完成**：ack 包装实际可用；仍需手写 evidence JSON，完整日常流程及恢复演练未完成。

## 7. 下一次交付要求

先修 RQ-01 的结果解析、RQ-03 的实际快照输入校验、RQ-04～RQ-06 的锁与状态机，再统一指纹边界和 schema。采用清单式完成记录：问题、修改代码、正例、反例、实际日志、剩余范围。对新增测试注明前置条件，不通过改弱预期来消除失败。

随后创建新的 DEL，保留所有旧 DEL/REV，至少重跑原 24 项、本轮 12 项和 §14 尚缺的真实集成/用户流程验收。下一次说明不要写“阻断项全部修复”而省略尚未实现的 C07/C09/C10/C12。

以下命令都在仓库根目录执行，输出写隔离目录；没有真实 Unity 调用：

```powershell
# 新 DEL 工具源码：将该路径换成下一次交付 ID
$env:COLLAB_AUDIT_TOOL_SOURCE = 'out/collab/DEL-20261017095048-cfd9dd/tools/collab'
$env:COLLAB_AUDIT_RUN_LABEL = 'next-reaccept-baseline'
node docs/handoff/verification/CollabTool-Reaccept-2026-10-17/baseline24-rerun.mjs
$env:COLLAB_AUDIT_RUN_LABEL = 'next-reaccept-extra'
node docs/handoff/verification/CollabTool-Reaccept-2026-10-17/supplemental.mjs
```

本报告独立保存，不调用已复现存在并发覆盖问题的 finish 命令更新根项目验收记录。用户可将报告与两组 JSON 结果直接交给 Cursor 修复。

## 8. 保全核对

补充检查两次执行均记录以下哈希前后相同：

- Unity Assets/Packages/ProjectSettings 联合清单：`e108606412e05a72cd0ca905051bb23bc77620656b5a5624b4e615444e4ff9c9`。
- 原 DEL：`abd914ee12a4f27a0c25cac453109a4f000c5a02cce0e5f8fcca569d78bacacd`。
- 原 REV：`94b9eef5f1323a513f4f5bb9808e9aa2f2d2d971421c1f178dcbe74aae73d7de`。
- 新 DEL：`d63aa555fa5c7c0a044afe4a7917f883b7bf87d815c64b33e6c973a12e4d6e95`。
- 游戏任务书：`0b2562677604c72d5094cee856764f2e3d2ba74d5b4b247d6910b962d2a51194`。

详见补充结果的 preservation。本轮新增报告、证据和测试脚本；未修改 `tools/collab` 实现，未覆盖上轮验收报告及结果。原有大量修改、未跟踪文件与隔离检查目录均保留。
