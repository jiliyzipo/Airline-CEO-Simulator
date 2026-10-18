# 阶段自动化边界缺陷修复（第二轮）：SA01–SA05

日期：2026-10-18。修复方：Cursor。受检对象上一轮：`DEL-20261018093335-80f2f4` / `REV-20261018095357-18f7dd`（failed，5 个 ISSUE）。

本轮新交付：**`DEL-20261018102500-1304ad`**（任务 `S01-COLLAB`，profile `collab`，预检 132/132 通过）。

> 过程说明：先产出 `DEL-20261018102037-2afe59`，随后补齐 `Collab.ps1` 帮助文本又生成
> `DEL-20261018102500-1304ad`；前者已被工具自动标为 `superseded`，**以 `DEL-20261018102500-1304ad` 为准**。
> 5 个 ISSUE 的 `fixed` 备注里写的是先前的 DEL 编号，历史记录不覆盖，故在此更正。

- 自带回归：**132/132 通过**（`node tools/collab/tests/run-all.mjs`，含新增 6 个 SA 回归用例）。
- 用上一轮同一份 `stage-audit.mjs`（仅把 subject 指向新交付快照）复跑：**6 项中 5 项通过**。
- SA05 的 `status === 'passed'` 断言与 SA03 的修复要求**互斥**，见下文专门说明。这不是未修复。

## 逐项修复

### SA03（blocker）自定义 AC 被自动写成 passed → 已改

`buildEvidence()` 不再把机器计数当作任意条款的证明。新增 `machineCoverageFor(task, profile)`：
只有与 profile 模板逐字一致（同 id、同文本）的条款才算“机器可验证”。

- 机器可验证 → 自动 `passed`。
- 自定义/改写/新增条款 → `not_run`，本次检测**直接不通过**，并自动登记
  `ISSUE … 验收条件缺少机器验证`（归属 codex）。
- 唯一例外是检测者显式补证：`stage-review --coverage AC1=passed,AC2=passed`。
  补证连同检测者身份写入证据 `attestation`（反向复验时还含授权文件 sha256）。
  未知条款编号直接退出码 2，不会被静默忽略。

`stage-close` 的交接单现在会列出“哪些条款不被 profile 机器覆盖”，提示必须补证。

### SA02（blocker）Unity 预检绕过资源锁 → 已改

`runProfileInSnapshot()` 对 `unity*` profile 复用正式检测的共享租约：

1. 检查本机 `Unity.exe`；在运行 → 退出码 4，不执行。
2. 检查 `.collab-local/locks/unity-review.lock`；被别的 review 持有 → 退出码 4，不执行。
3. 原子获取自己的租约（`preflight-<DEL>-<runToken>`），持有到进程与结果核验结束。
4. **spawn 之前再查一次**占用（编辑器可能在导出快照后又打开）；查不到 → 失败关闭。
5. `finally` 里只释放自己 id 的租约。

不杀 Unity、不删用户锁、不抢占其他 review 的租约。

### SA01（major）预检中快照被改写仍标 ready → 已改

预检现在复用正式 `verifySnapshotIntegrity`：

- **执行前**：清单文件缺失/哈希不符/夹带源码 → 直接拒绝，命令不跑。
- **执行后**：带声明过的运行产物白名单再校验一次；被改写或夹带 → 预检失败。
- 预检失败 → 交接单 `preflight-failed`，`stage-review` 不会挑它。

### SA04（major）同一 DEL 并发检测执行两次 → 已改

`stage-review` 增加**按交付**的跨进程租约 `.collab-local/locks/stage-review-<DEL>.lock`：

- 租约覆盖“查终态 → 选/建 review → 执行 → 写交接结果”整段。
- 进锁后**再查一次**终态：别的进程已完成就返回它的结论，不新建 REV。
- 拿不到租约 → 退出码 3，并在错误里给出持有者 pid；不会执行第二次。
- 不同 DEL 之间不共享锁，合法并行不受影响。
- 交接索引 `index.json` 的读-改-写统一走 `handoff-index.lock` 事务。

### SA05（major）阶段入口拒绝限定反向授权 → 授权已改，判定按 SA03 收紧

`cmdStageReview()` 删掉了无条件的 `assertRoleAction(actor,'review-start')`：

- `actor=cursor` → 走与 `review-*` 完全相同的 `reviewerAuthorization(...)`：
  校验交付、三类指纹、profile、动作、授权人；通过后创建/执行/证据/finish 全程带同一 actor 与授权。
- `actor=cursor` 而没有 `--authorization`、或没写 `--delivery` → 仍然退出码 7（授权绑定到具体交付）。
- 提前返回（“已检测过”）之前也会先校验授权，不能借“已经验收”跳过权限。

**需要检测方注意的互斥点**：上一轮 `stage-audit.mjs` 的 SA05 断言是
`code !== 7 && status === 'passed'`，而它的夹具是 `C-DEMO` 的三条自由文本验收条件
（`claim→deliver→…` 等），profile 选的又是只做两项文件检查的 `smoke`。
SA03 的修复要求「自由文本条款不能被机器计数整体转成通过」，所以这组夹具现在必然得到 `failed`。

用同一份夹具、同一个授权复跑（脚本：本轮临时验证，逻辑与 `stage.test.mjs` 的 SA05 相同）：

| 调用 | 结果 |
| --- | --- |
| `stage-review --delivery … --actor cursor --authorization grant.json` | 不再 7；`failed`，因为没人证明那三条自由文本条款 |
| 同上 + `--coverage AC1=passed,AC2=passed,AC3=passed` | `passed`；REV `actor=cursor`、`reviewKind=independent`；证据带 `attestation.by=cursor` 与授权 sha256 |

也就是说：**授权路径已通**，剩下的差异是“谁来声明那三条非机器条款通过”。
按 SA03 的原始要求（“未覆盖项保留 not_run/blocked，允许检测者明确补证”），
这一步必须显式发生；如果让它默认发生，就是 SA03 本身。
建议复验方把 SA05 断言改成 `code !== 7`，并补一条带 `--coverage` 的正例。

## 新增回归用例（`tools/collab/tests/stage.test.mjs`）

| 用例 | 覆盖 |
| --- | --- |
| SA01 | 预检改写清单文件 → `preflight.ok=false`、交接非 ready、独立校验 5、不被自动挑中 |
| SA02 | 他人持 `unity-review.lock` → 退出码 4 且哨兵未生成；占用不可确认同样 4；释放后正例可跑并自动释放 |
| SA03 | 自定义 AC → 不通过、coverage `not_run`、自动登记缺陷；`--coverage` 后可通过且 attestation 可追溯；未知条款编号 2 |
| SA04 | 两个并发 `stage-review`：profile 只执行 1 次、只产生 1 个 REV |
| SA05 | 匹配授权经 `stage-review` 通过（actor=cursor、independent、授权 sha256 落盘）；错交付/无授权仍 7 |
| SA06 | 机器全覆盖的正常正例不回归 |

## 复跑命令

```powershell
node tools/collab/tests/run-all.mjs
node tools/collab/cli.mjs stage-review --delivery DEL-20261018102500-1304ad --actor codex
```

本轮未启动 Unity、未改游戏代码、未覆盖任何 `deliveries/` `reviews/` `issues/` 历史记录、未 commit/push。
