# 协作协议 · PROTOCOL

## 职责

| 角色 | 可做 | 不可做 |
|---|---|---|
| Cursor | claim / deliver / release / 修复缺陷 | 将独立检测标为 passed |
| Codex | review-start / review-run / review-finish / 写缺陷 | 默认改生产业务代码 |
| 用户 | 优先级、设计裁决、提交/推送授权 | — |

## 状态

- 开发：`todo` → `developing` → `ready_for_review` →（失败）`rework`
- 检测：`not_run` / `queued` / `running` / `passed` / `failed` / `blocked` / `cancelled` / `interrupted`
- freshness：`current` / `stale` / `unknown`
- 终态（`finishedAt` 存在且不可改写）：`passed` / `failed` / `blocked` / `cancelled`
- `blocked` 表示“机器结果无法验证结论”，只能通过新 REV 重来，绝不等于通过。

## 退出码

| 码 | 含义 |
|---|---|
| 0 | 成功 |
| 2 | 参数/用法错误 |
| 3 | 状态冲突或 CAS revision 不匹配 |
| 4 | 资源锁（任务锁 / Unity 占用） |
| 5 | 检查失败（doctor、快照不稳定等） |
| 6 | 证据不完整 |
| 7 | 角色禁止动作 |
| 1 | 未分类内部错误 |

外部工具原始退出码保存在 review.runs[].exitCode，不被掩盖。

## 指纹

- `fingerprintPolicyVersion`: 2（v1 历史记录保留；v2 排除本机 tools/collab/config.json，哈希算法不变）
- 实现 / 验证 / 合同三类分别 SHA256；清单路径用 POSIX `/`
- 写报告、事件、CURRENT 不计入实现指纹循环
- 旧 `Baseline_2026-10-17.json` 综合指纹保留历史含义，不与本策略直接等价比较

## 快照

交付时复制白名单文件到 `out/collab/<deliveryId>/`；复制中途源变化则重试或失败。检测读快照，不读正在改动的主工程并宣称适用于整版。

C-DEMO 演示任务使用收窄的 `demoSnapshotRoots`，避免为工具演练复制整份 Unity 工程；真实游戏任务交付使用完整实现/验证根。需要但不在目录根下的单文件用 `demoSnapshotFiles`（当前为 `docs/collaboration/PROTOCOL.md`，smoke 会读取它）**精确**加入，不把整个生成态目录当作运行输入。

快照在每个文件中记录 `snapshotSha256`（含隔离 overlay 之后的值）。`review-run` 执行前、执行后与 `review-finish` 都会复核：清单文件缺失/哈希不符/出现未列出的源码即拒绝；`bin/ obj/ Library/ Temp/ Logs/ Build/` 等运行产物目录不算源码，允许保留。

导出后立刻做一次同样的未列文件检查（`snapshotManifest.unlistedAtCreation` 必须为 0），不干净的交付直接拒绝发布。

## 执行与结果证据

- 每条 review 有执行租约 `review-run-<reviewId>.lock`（原子创建、持有到写回结束）：
  两个进程同时 `review-run` 时只有一个执行，另一个返回退出码 3。
- 每次执行生成 `runToken`，写入 review 的 `activeRunToken` 与 `runs[].runToken`，并用于
  写回校验、取消清理与锁释放。
- stdout/stderr 写入该 run 独有的 `.collab-local/logs/<REV>/run-<runToken>/`，排他创建，
  并记录 `{path, sha256, bytes, runToken}`。`review-finish` 先核对字节，再解析计数：
  文件缺失、字节数不符、哈希不符或没有绑定都不能 passed（退出码 5）。
- 声明 `resultFile` 的 profile 必须由本次执行产生该文件；run 记录 `before/after` 的
  SHA256 与时间戳、`producedByRun`。旧文件、运行后被改写、或文件缺失都不能支撑 passed。
- 结果解析按 `resultKind` 强制：`json` / `nunit` / `slotcheck` / `semcheck` / `text`（仅已知适配器）。
  未知或畸形结果不能通过；无法验证时 review 记为 `blocked`。
- NUnit XML 必须是**一个完整、格式良好的 XML 文档**（零依赖严格解析）：截断、未闭合、
  错误嵌套、重复根、重复属性、非法计数一律拒绝；属性必须齐全，且根汇总不能掩盖
  `<test-case result="Failed">`。`</test-run>` 字符串检查不算校验。
- 顺序重跑同一 review 时每次落在新的 run 目录，旧 run 的路径与哈希保持不变。

## 不可覆盖

`docs/collaboration/deliveries|reviews|events|issues` 下 JSON 只追加，纠错靠新记录关联。

## 1.2 补充协议

- 用户明确授权 Codex 开发时，deliver 要求作用于该 projectId/taskId/action/allowedRoots 的授权文件，交付保留真实 actor、授权原文及 SHA256。相同作者复核为 developer-self-check，independent=false；完成闸门不把自测当独立验收。
- review-start 冻结 profileDefinition；run、证据草稿、finish 都使用同一配置。当前 profile 改动时旧结果对当前验证基线失效，不能偷换执行命令或解析规则。
- review-evidence 只生成追加式草稿；绑定指纹与机器计数，未显式声明的 AC 默认 not_run。finish 仍完整复核，不因自动生成草稿放行。
- claim 检查活动任务的快照范围重叠。ack 与状态摘要在状态锁内更新；摘要采用原子替换。不可覆盖记录采用逐 ID 排他发布保护。
- 未读/损坏、外机或无法证明进程已退出的锁均保守保留。仅确认同机死进程的工具锁允许回收，绝不操作 Unity 工程锁。
- watch 就绪后用文件事件与最多 4 秒轮询保守标记待复验；不通过缓存宣布哈希仍匹配。显式 sync 读取实际字节。生成目录和结果不触发源码失效，监听不 ack、不启动检测或提交。
- 工具写入的受管任务书总项在失效后可自动撤回，并追加原记录、报告关联及阻塞原因。手工更改条款时报告冲突；只读游戏引用和人工勾选保持不动。总项已勾选也必须重新通过闸门。
- 当前两端选择手动协作模式；Hook CLI 计数不证明原生接线或信任状态。可选桥接消费 JSON stdin，按 source/event 与事件/文件版本去重，所有角色继续明确 sync/ack。

## 1.3 反向独立复验与阶段交付

- 用户授权 Codex 开发的特定交付，可由 Cursor 持限定复验授权，以真实 actor=cursor 执行 review-start/run/evidence/finish/cancel。默认角色表不扩权；授权必须绑定 projectId、taskId、deliveryId、developerActor=codex、三类交付指纹、profiles 和 actions。
- 复验授权全文及文件 SHA256 固定写入 REV 和创建事件；每一步重新检查授权哈希与记录作者。Codex 不能以自己的身份代写 Cursor 的 REV；用户保留管理性取消能力。此声明是本机协作审计约束，不是操作系统身份认证。
- 无授权、授权错 DEL/指纹/profile、Cursor 自验自己的交付、跨作者修改 REV 均拒绝。缺陷 verified/closed、任务书总项回填和全局 recover 继续按原角色权限处理。
- 同作者的 passed 永远不能满足独立验收，包括每个必需 profile；不能用一个 profile 的独立通过替另一 profile 的开发自测背书。历史记录保留。
- 日常可连续修复并自测、阶段末补齐任务书。生成 DEL 前须登记对应阶段任务与验收条件；只有明确交付后才进入集中验收。sync/ack 不自动执行检测。

## 1.4 阶段交付与 Unity 占用（2026-10-22 实证）

- `stage-close` 的预检**在交付快照内启动真实 Unity 批处理**（本工程当前 deciding profile 为 `unity-editmode`）。
  因此它要求编辑器已关闭：用户 Unity 正开着 → `EXIT.LOCKED`（4），不生成 DEL、不写交接单、不改任务状态。
  这不是故障，是设计要求：一边改工程一边出交付，快照与工程就不是同一份东西。
- 租约只有一把：Unity 预检与 `review-run` 共用 `unity-review.lock`。编辑器占用、占用无法确认、
  另一 review 持锁，三种情况一律退出码 4，**且都不执行**（不会出现“先跑了再说”）。
- 禁止为绕开退出码 4 去杀 Unity、删工程锁或改 `Library/`。正确动作只有一个：让用户关闭 Unity，然后重跑同一条命令。
- 预检失败（产物红）与预检被拒（退出码 4）要分开记：前者是代码问题，后者只是机器没空。
  两者都不得写成“已验收”；也确实都不产生可检测的 DEL。
- 历史交付的复现口径：旧 DEL 的快照是**当时**的工程状态，用它跑平衡/回归工具可以对照“这条规则改了之后到底变了什么”，
  但不能把旧快照的结果当当前验收结论。

## 1.5 交付台账与「被取代」（2026-10-26）

- freshness 只问一件事：**该任务最新的、未被取代的交付**是否仍描述当前工作区。
- 被取代的事实记在共享 CAS 状态 `state.json` 的 `deliveryStatus[<DEL>] = { status: 'superseded', supersededBy }`，
  **不写回交付记录本身**：DEL 是不可改写的历史，只能追加这条台账。
- 为什么需要它：若一条交付被错挂在别的任务名下，它对该任务永远是“最新工作”，而它的指纹永远不可能与
  该任务的工作区匹配 → 该任务被永久拖成 `stale`，每次 `sync` 都重写它的 `freshnessNote`/`staleForReviewId`，
  看起来像任务变了，其实什么都没变（S03-Docs / `DEL-20261025125421-e0fbe0`）。
- 写入路径：
  - `stage-close` 写交接单时，同任务的旧 ready 交付自动登记为被取代；
  - `stage-close --supersedes <DEL,...>` 用于显式注销**挂在别的任务下**的交付（错挂只能由人点名，工具不猜）；
  - `stage-reconcile` 把本地 `out/collab/handoff/index.json`（被 gitignore）里的取代事实补进台账，幂等。
- 台账与看板同源：`stage-board` 报的「最新交付」与 freshness 用的是同一条规则，不会再出现“看板指着一条已退休的交付”。

## 1.6 环境能力缺口 ≠ 业务回归（2026-10-26）

独立检测常在受限令牌（AppContainer/CI）里执行，那里有些检查**根本无法执行**，而不是执行后不通过：

- 系统临时目录只允许写入、不允许 `rename` → 原子写 `EPERM`（实测：`...\sandbox.{...}\AC\Temp`）；
- `git clone` 需要 fork `upload-pack` 子进程，受限令牌下 `NtCreateDirectoryObject(\BaseNamedObjects\msys-...) 0xC0000022`；
- `Get-CimInstance` 被拒 → 无法枚举 Unity 进程。

处理原则（已落地在 `collab` profile 与 `doctor`）：

1. **能执行的失败仍是失败**：探针证明环境可用之后，真实失败一律 `FAIL`。
2. **不能执行的检查报为 skip 并写明原因**，计入 `assertionsSkipped`，绝不折成通过：
   `run-all` 把子套件的 `SKIP` 行抽出（去重后写 stderr），结果 JSON 同时给出 `assertionsSkipped` 与 `skipReasons`。
3. **skip > 0 时机器条款不再自动成立**：`machineCoverageFor` 会把这些条款降级为“检测者必须显式补证”
   （`--coverage AC1=passed`）。有跳过就有人签字，签的是人的判断，不是工具替人担保。
4. **`doctor` 只由“执行了但不通过”的检查判负**：`unavailable` 的检查记为警告；
   安全相关的兜底不变——Unity 预检在无法确认占用时仍然拒绝启动。
5. **墙钟不是结论**：单子套件超时 8 分钟、Node profile 预检 20 分钟；超时才报超时。
