# 协作工具第四轮交付说明

日期：2026-10-17。交付方：Cursor。对应验收报告：ACCEPT-COLLAB-20261017-CODEX-03（第三轮，结论 failed）。

本交付只改 `tools/collab` 工具实现与协作文档，不改游戏业务代码、不改任务书勾选、不启动 Unity、不提交/推送。

## 1. 本轮针对的阻断项

| 编号 | 第三轮缺陷 | 本轮实现 |
|---|---|---|
| TH-01 | 未知/畸形结果仍可判通过（T01、T02） | `parseRunResult` 按 `resultKind` 强制格式；声明的 `resultFile` 必须成功解析，stdout 不再兜底；未识别输出只能 `blocked`/拒绝 |
| TH-02 | NUnit 缺字段被补成 0、根汇总掩盖失败用例（T03、T04） | 严格解析：四个计数属性必须存在且为纯数字；与 `<test-case result>` 明细交叉核对；计数关系必须自洽 |
| TH-03 | 旧 XML 可复用（T05） | 结果文件绑定本次执行：`runToken` + 运行前后 SHA256/时间戳 + `producedByRun`；路径支持 `{runToken}` 保证每次唯一；run 与 review 都保存路径与哈希 |
| TH-04 | 运行中改快照仍可盖通过（T06） | run 前、run 后、finish 三处复核快照清单与哈希；运行中源码变化 → run 失败、finish 拒绝 |
| TH-05 | 同一 review 双进程重复执行（T07） | 执行租约 `review-run-<id>.lock` 原子创建并持有到写回结束；第二个进程返回退出码 3；`runToken` 保护写回/取消/锁释放 |
| TH-06 | 编译产物阻断复验（T08） | 运行时产物目录（`bin/ obj/ Library/ Temp/ Logs/ Build/` 等）视为产物而非源码，允许保留；产物目录之外的新增源码仍拒绝 |

同时修正第三轮 §5 指出的 Unity 目录口径问题：
profile 现在以 `Unity Project` 为 `-ProjectPath`，结果文件为
`Unity Project/Logs/Tasks20261017_<runToken>.xml`，与快照实际布局一致。

## 2. 语义变化（请验收方核对）

1. **不再存在 “unverified 仍 passed”**。若 profile 未声明机器结果契约且输出无法识别，
   review 记为终态 `blocked`，退出码 6，任务 freshness 记 `unknown`，需开新 REV 重来。
2. 终态集合扩展为 `passed / failed / blocked / cancelled`，均不可再次 finish。
3. `compile` profile 声明 `resultKind: semcheck`（两次程序集编译门 + 编码门，共 3 个断言）；
   `slot` 声明 `resultKind: slotcheck`（由 `[PASS]/[FAIL]` 明细推导计数并与 `SLOTCHECK` 汇总核对）。
   两者的适配器已用真实工具输出验证（`errors=0` 三行、`SLOTCHECK OK`）。
4. 旧正例 fixture（任意文本 stdout 期望 passed）按第三轮 §7 的要求不再成立；
   本轮证据全部使用真实机器结果，未保留任何宽松通道。

交付编号：`DEL-20261017150657-dd9483`（快照 `out/collab/DEL-20261017150657-dd9483/`；演示任务 C-DEMO 按设计只复制 `tools/collab`）。
实现指纹：`efdddc5f2a11639ad3454a21b6092114b8d692f244bc0b4f2e475e82d28fdd1c`。

## 3. 本轮证据

驱动脚本：`docs/handoff/verification/CollabTool-Fourth-2026-10-17/run-verification.mjs`
（原样调用三份既有套件，不改其源码）。

工作区 `tools/collab` 与交付快照 `out/collab/DEL-20261017150657-dd9483/tools/collab` 均达到：

| 套件 | 工作区 | 交付快照 |
|---|---|---|
| 开发方测试 `tools/collab/tests/collab.test.mjs` | 19/19 | 19/19 |
| 原检查 `baseline24-rerun.mjs` | 24/24 | 24/24 |
| 补充检查 `supplemental.mjs` | 12/12 | 12/12 |
| 第三轮边界检查 `CollabTool-Third-2026-10-17/edge-cases.mjs` | 8/8 | 8/8 |

原始输出与结果 JSON：
`docs/handoff/verification/CollabTool-Fourth-2026-10-17/evidence/current/` 与 `.../evidence/delivery/`。

开发方测试在一次性 scaffold 中运行（`docs/collaboration` + 工具占位文件，方法同 baseline P01），
以免污染真实仓库状态，并保证工作区与快照用同一口径比较。

注意：若 shell 里残留 `COLLAB_AUDIT_TOOL_SOURCE`（上一轮指向旧快照），单独运行三份套件会**测旧代码**。
驱动脚本已对每个套件显式覆盖该变量；手工复现时请先 `Remove-Item Env:COLLAB_AUDIT_TOOL_SOURCE`。

新增开发方单测（本轮新增 6 条）：NUnit 缺字段/掩盖失败/真实形态、slotcheck 适配器、
semcheck 三门槛、以及“锁初始化期间不得被抢占”（复现 TH-05 竞态的双执行路径）。

真实适配器抽样：`node tools/semcheck/run.js` 输出 `errors=0 (raw=0)` / `tests-asm: errors=0 (raw=0)` / `enc: errors=0`；
`dotnet run --project tools/slotcheck/slotcheck.csproj -c Release` 输出 `SLOTCHECK OK`，均被对应适配器接受。

`blocked` 终态实测（一次性 fixture，未提交）：未声明结果契约的 profile 输出任意文本时，
`review-finish` 退出码 6、review 与任务均为 `blocked`、`finishedAt` 已写、freshness=`unknown`，
再次 finish 返回退出码 3（终态不可改写），没有出现 passed。

复现（仓库根）：

```powershell
node docs/handoff/verification/CollabTool-Fourth-2026-10-17/run-verification.mjs --subject tools/collab --label current
node docs/handoff/verification/CollabTool-Fourth-2026-10-17/run-verification.mjs --subject out/collab/DEL-20261017150657-dd9483/tools/collab --label delivery
```

## 4. 仍未完成（继续如实标注）

- C07 真实 Unity EditMode/PlayMode 串联：本轮**未启动 Unity**。profile 的目录与结果路径已对齐，
  结果绑定与严格 XML 解析已就绪，但真实 Unity 执行、XML 绑定、占用恢复仍需在有授权时实测。
- C09 权威任务书动态映射与总完成闸门：未实现。
- C10 客户端 Hook 端到端与事件去重：仍未验证，`doctor` 继续报告 `verified:false`。
- C12 完整用户日常流程演练：未完成。
- `cleanGitCheckoutVerified` 仍为 `false`，未把“源码含未提交文件”说成干净检出。

## 5. 改动文件

工具实现：

- `tools/collab/lib/io.mjs` — 锁初始化竞态：空/半写入的锁文件不再被当作“可回收”，只有明确失主才回收
- `tools/collab/lib/commands.mjs` — 严格结果解析（`parseRunResult` / `parseNunitCounters` /
  `parseSlotcheckText` / `parseSemcheckText`）、执行租约与 `runToken`、结果文件绑定、
  运行后与 finish 时快照复核、产物目录豁免、`blocked` 终态
- `tools/collab/lib/state.mjs` — `blocked` 的 freshness 语义（保持 `unknown`，绝不写成 current）
- `tools/collab/tests/collab.test.mjs` — 新增 6 条单测
- `tools/collab/config.json` / `config.example.json` — `resultKind`、Unity `-ProjectPath` 与
  `{runToken}` 结果路径

文档：

- `docs/collaboration/PROTOCOL.md`、`docs/collaboration/README.md`
- `docs/handoff/CollabTool_Delivery_Fourth_2026-10-17.md`（本文件）
- `docs/handoff/verification/CollabTool-Fourth-2026-10-17/run-verification.mjs` + `evidence/`

## 6. 保全

- 未修改 `docs/collaboration/deliveries|reviews|events|issues` 历史记录；本轮只新增 DEL。
- 未修改 `Unity Project/Assets|Packages|ProjectSettings`。
- 未修改游戏任务书；未提交、未推送、未清理任何历史证据。
