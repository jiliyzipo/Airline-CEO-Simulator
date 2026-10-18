# 协作流程操作检查表（自动化版）

> 目标：把一个阶段的"收口 → 检测 → 修复"压缩成**每边一条命令**，人工不再做逐项检查。
>
> 工具里的旧命令仍然可用，本文件只描述新的最短路径。旧版逐步流程保留在文末附录，用于排查。
>
> 相关文件：[`README.md`](README.md)、[`OPERATOR_MANUAL_ZH.md`](OPERATOR_MANUAL_ZH.md)、[`CURRENT.md`](CURRENT.md)（程序生成，勿手改）。

---

## 0. 现在的循环（一张图）

```
   你说一句话
        │
        ▼
  Cursor: stage-close ──► 自动建任务 / 写摘要 / 生成 DEL / 在快照里预检
        │                        │
        │                        ├─ 预检失败 ──► Cursor 直接修，重跑一次（不打扰 Codex）
        │                        │
        │                        └─ 预检通过 ──► 生成交接单 out/collab/handoff/DEL-xxx.md
        ▼
  Codex: stage-review ──► 自动 起检测 / 跑 profile / 写证据 / 出结论 / 失败自动登记 ISSUE
        │
        ├─ passed ──► 阶段完成，可继续开发
        └─ failed ──► Cursor: stage-fix 取回缺陷清单 ──► 修 ──► 回到 stage-close
```

人工只做三件事：**决定收口**、**把编号/命令给 Codex**、**保存关闭 Unity（如被占用）**。

---

## 1. 你只需要说两句话

| 场景 | 你说 |
| --- | --- |
| 阶段收口 | `请对本阶段收口，自测后生成 DEL，给我交付编号。` |
| 交给 Codex | `检测最新 DEL，跑 stage-review，失败就登记缺陷。` |

Cursor 和 Codex 各自只需执行一条命令（见下），其余全部由工具完成。

---

## 2. 开工（一条命令）

| ☐ | 命令 | 作用 |
| --- | --- | --- |
| ☐ | `.\tools\collab\Collab.ps1 stage open` | 自动 `doctor` 前置检查 + `sync` + 以 cursor 身份 `ack` 最新 revision + 打印任务看板 |

- 不再需要分别跑 `doctor` / `sync` / `ack` / `tasks`。
- 输出里的 `revision` 就是双方要对齐的数字；看板会列出每个任务的最新 DEL/REV 与未关缺陷数。

---

## 3. 阶段收口（Cursor 一条命令）

| ☐ | 命令 | 工具自动完成 |
| --- | --- | --- |
| ☐ | `.\tools\collab\Collab.ps1 stage close` | ① sync+ack ② 找出本阶段改动 ③ 复用或新建阶段任务（自动标题/范围/验收条件）④ 自动选择 deciding profile ⑤ 自动写交付摘要 ⑥ 生成 DEL ⑦ 在快照内跑一次预检 ⑧ 写交接单 |

常用参数：

| 参数 | 用途 |
| --- | --- |
| `--task S01-UI` | 指定任务（默认自动挑未完成的阶段任务） |
| `--profile compile` | 强制 profile（默认按改动路径自动选） |
| `--note "本次修了X"` | 追加到摘要 |
| `--gaps "未做Y;未做Z"` | 记录已知缺口 |
| `--no-preflight` | 跳过快照预检（不建议） |
| `--dry-run` | 只看计划，不写任何记录 |
| `--force` | 允许在"无改动"时也生成交付 |

**预检**是这次自动化的关键：它在**交付快照里**把 deciding profile 真跑一遍。

- 预检通过 → 交接单状态 `可检测`，DEL 可以送 Codex。
- 预检失败 → 交接单状态 `预检未通过（不要送检）`，命令退出码 5，`stage review` 会主动跳过它。Cursor 修完后重跑同一条 `stage close`，生成新的 DEL。

预检在**跑之前**和**跑之后**各校验一次快照：清单文件被改写、缺失、夹带源码都算失败，
所以不存在"跑完把快照改坏了还标成可检测"。

Unity 类 profile 预检与正式检测**共用** `unity-review.lock` 租约：
别的 review 持锁 / 编辑器占用 / 占用无法确认，三种情况都退出码 4 且**不执行**命令。

预检只是开发自测，**永远不会**被当成独立验收。交接单会列出哪些验收条件不被 profile 机器覆盖。

### 自动选择 profile 的规则

| 改动范围 | deciding profile |
| --- | --- |
| `Unity Project/**` | `unity-editmode`（Unity 开着时会直接拒绝并提示保存关闭） |
| `tools/collab/**` | `collab` |
| `*.cs` / `tools/semcheck/**` | `compile` |
| `tools/slotcheck/**` | `slot` |
| 仅文档 | `smoke` |

一个阶段只用一个 deciding profile。原因是工具语义：一份 REV 只能绑定一个 profile，而 `review-finish` 要求该 REV 覆盖全部验收条件。因此自动生成的验收条件与该 profile 对应，不做"四个 profile 各自声称全覆盖"的假证据。

---

## 4. 独立检测（Codex 一条命令）

| ☐ | 命令 | 工具自动完成 |
| --- | --- | --- |
| ☐ | `node tools/collab/cli.mjs stage-review --delivery DEL-xxx --actor codex` | review-start → review-run → 生成证据 → review-finish → 失败时自动写 ISSUE |

也可以不带 `--delivery`：`stage-review` 会自动挑选最新一份"未检测且预检通过"的交付。

- 已检测过的交付不会重复执行（幂等），直接返回上次结论。
- **并发保护**：同一个 DEL 的检测带跨进程租约，两个 `stage-review` 同时跑只会真正执行一次；
  第二个返回退出码 3（已有检测在跑）或上一次的结论。不同 DEL 之间仍可并行。
- **验收条件覆盖**：只有 profile 模板生成的条款才算"机器可验证"并自动 passed。
  复用自定义任务、或手写/改写过的条款，不会被机器计数冒充通过 —— 它们记为 `not_run`，
  本次检测直接不通过。这类条款只能由检测者显式补证：

```powershell
node tools/collab/cli.mjs stage-review --delivery DEL-xxx --actor codex --coverage AC1=passed
```

  补证会连同检测者身份写入证据 `attestation`（反向复验时还含授权文件 sha256），
  谁声明了这条通过永远可追溯；未知条款编号会直接退出码 2。
- 退出码 0 = 通过；5 = 未通过并已登记缺陷；4 = 资源被占用（Unity / 另一个 review 的租约）；3 = 该 DEL 已有检测在执行。
- 证据 JSON 由机器计数生成，不需要任何人手写。

---

## 5. 不通过时的修复（Cursor 一条命令）

| ☐ | 命令 | 作用 |
| --- | --- | --- |
| ☐ | `.\tools\collab\Collab.ps1 stage fix` | 汇总未关缺陷 + 未转成缺陷的失败检测 + 预检失败的交付，给出可执行清单 |

修完后回到第 3 步 `stage close`，生成**新的** DEL；历史 DEL/REV/ISSUE 一律不覆盖。

---

## 6. 看板与状态

| 命令 | 用途 |
| --- | --- |
| `.\tools\collab\Collab.ps1 stage board` | 任务/复验/最新交付/未关缺陷 |
| `.\tools\collab\Collab.ps1 gate <任务ID>` | 单任务完成闸门（阶段是否真正做完，以它为准） |
| `.\tools\collab\Collab.ps1 clean-checkout-verify` | 克隆 HEAD 到空目录，逐文件比对字节，证明"干净检出可复现" |
| `.\tools\collab\Collab.ps1 session` | 等价于 `stage open`，兼容旧习惯 |

全项目 `gate` 在没有完成 T01–T06 时永远是 `false`，这是事实状态，不是故障。

### 干净检出（T02 那一类任务）

`gate` 只有两条路能点亮 `clean-git-checkout`：某份交付记录了 `cleanGitCheckoutVerified=true`，或存在一份与**当前提交内容**匹配的 `clean-checkout` 记录。任务书里标成"只读参考"的任务（例如 T02）根本不接受交付，所以只能走第二条。

```powershell
# 1. 先提交，再验证：未提交的源码克隆不到，命令会直接拒绝（退出码 3）
.\tools\collab\Collab.ps1 clean-checkout-verify
# 2. 需要附上 Unity 导入/启动证据时
.\tools\collab\Collab.ps1 clean-checkout-verify "statement=从克隆导入并进入主菜单" "evidence=out/ccv/unity.log"
```

它只声明"每个跟踪文件都被逐字节重建"，**不声明** Unity 导入、编译或启动成功——那需要单独跑一次并作为 `evidence` 附上。记录的 `treeFingerprint` 一旦遇到新提交就自动失效，闸门随即回到未通过，不会拿旧记录给新内容背书。

**闸门对谁说真话：** `clean-git-checkout` 判断的是**已提交的 HEAD**，不是工作区。验证通过后再改源码但不提交，闸门仍是绿的——因为"HEAD 可复现"这件事确实成立。所以绿灯的含义是"这个提交可以被别人克隆重现"，不是"我本机现在改的东西也没问题"。要检查本机当前状态，直接跑 `clean-checkout-verify`：有未提交源码时它拒绝验证（退出码 3）。

**不要在检测运行期间提交。** 复验过程会分几次写入同一份 REV 记录（先 `running`，结束后 `passed`）。此时执行 `git add -A` 会把半成品状态一起提交，之后还得再提一次差异。等 `stage review` 返回后再提交。

---

## 7. 仍然需要人工的三件事

| 事项 | 原因 |
| --- | --- |
| 决定"什么时候收口" | 这是产品节奏判断，工具不该替你决定 |
| 关闭 Unity | 工具明确禁止杀进程/删锁；`unity-editmode` 检测前必须由你保存关闭 |
| `git commit` / `push` | 协作规则明确不自动提交；DEL 快照与 git 提交是两件事 |

除此之外，日常不再需要：手抄 revision、手建任务定义、手写摘要文件、手选 profile、手写证据 JSON、手动登记缺陷、手动勾任务书。

---

## 8. 退出码

| 码 | 含义 | 出现位置 |
| --- | --- | --- |
| 0 | 正常 | 全部命令 |
| 2 | 用法/配置错误 | 参数写错、profile 不存在 |
| 3 | 冲突/CAS 失败 | 另一端刚写入，重跑 `stage open`；同一 DEL 已有检测在执行；或工作区有未提交源码，`clean-checkout-verify` 拒绝验证 |
| 4 | 被锁/资源占用 | Unity 打开着；另一个 review 持有 `unity-review.lock`；保存关闭/等其结束后重跑同一条命令 |
| 5 | 检查未通过 | 预检失败、检测 failed、无改动可交付 |
| 6 | 证据不符 | 计数/指纹/覆盖与机器结果不一致（自动流程正常时不会出现） |
| 7 | 角色不允许 | Cursor 试图自证 passed、冒用 codex，或授权不覆盖该交付/指纹/profile |

---

## 9. 故障排查

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| `stage close` 报 `no changes` | 本阶段没有新改动 | 确认确实改过文件；或 `--force` |
| `stage close` 报 Unity 占用 | 你的编辑器还开着 | 保存关闭，重跑同一条命令 |
| `stage review` 返回 2 `no pending delivery` | 唯一待检交付的预检没过 | 先 `stage fix` 看预检失败原因 |
| `stage review` 返回 5 且 `issues` 非空 | 真实缺陷 | `stage fix` 取清单，修完 `stage close` |
| 同一 DEL 被要求重测 | 工具拒绝重复执行 | 生成新 DEL，不要改旧记录 |
| 看板 `freshness=stale` | 检测后源码又变了 | 重新 `stage close` + `stage review` |

---

## 附录 A：旧版逐步手动流程（排查用）

以下命令仍然有效，自动化命令内部也会调用它们。只有在需要单独定位问题时才逐条执行。

```powershell
# 开工
node tools/collab/cli.mjs doctor
node tools/collab/cli.mjs sync
node tools/collab/cli.mjs ack --actor cursor --revision <n>
node tools/collab/cli.mjs tasks --json

# 建任务 + 交付
node tools/collab/cli.mjs task-add --definition <file.json> --actor cursor
node tools/collab/cli.mjs claim --task <id> --actor cursor
node tools/collab/cli.mjs deliver --task <id> --actor cursor --summary-file <file> --profiles <p>

# 检测
node tools/collab/cli.mjs review-start --delivery <id> --actor codex --profile <p>
node tools/collab/cli.mjs review-run --review <id> --actor codex
node tools/collab/cli.mjs review-evidence --review <id> --conclusion passed --coverage AC1=passed
node tools/collab/cli.mjs review-finish --review <id> --actor codex --evidence <file>

# 缺陷流转
node tools/collab/cli.mjs issue-update --issue <id> --status fixed --actor cursor
node tools/collab/cli.mjs issue-update --issue <id> --status verified --actor codex

# 闸门
node tools/collab/cli.mjs gate --task <id> --json
```

## 附录 B：交付与交接文件位置

| 文件 | 内容 |
| --- | --- |
| `docs/collaboration/deliveries/DEL-*.json` | 不可变交付记录（含指纹、快照清单、摘要） |
| `docs/collaboration/reviews/REV-*.json` | 检测记录（含 run token、机器结果、结论） |
| `docs/collaboration/issues/ISSUE-*.json` | 缺陷原始报告（状态流转在 `state.json.issueStatus`） |
| `out/collab/<DEL>/` | 交付快照（检测实际执行的目录） |
| `out/collab/handoff/DEL-*.md` | 给 Codex 的交接单，含一条命令 |
| `out/collab/handoff/index.json` | 交接队列（ready / preflight-failed / accepted / rejected） |
| `.collab-local/stage/` | 自动生成的任务定义与摘要草稿 |
| `.collab-local/logs/preflight-<DEL>/` | 预检的 stdout/stderr（含字节数与 SHA256 绑定） |
| `.collab-local/evidence/` | 自动生成的证据草稿 |

`out/` 与 `.collab-local/` 都不参与指纹与快照，不会污染交付。
