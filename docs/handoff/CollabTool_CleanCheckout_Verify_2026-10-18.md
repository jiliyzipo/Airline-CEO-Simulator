# 协作工具变更：clean-checkout-verify（T02 闸门死结修复）

日期 2026-10-18。开发方 Cursor。

## 问题

`config.example.json` 给 T02 标了 `requiresCleanCheckout: true`，`finishTaskGate` 要求某份交付的 `git.cleanGitCheckoutVerified === true`。但代码里这个字段**只有 false 的赋值**：

- `snapshot.mjs` 写入快照清单时固定 `false`；
- `fingerprint.mjs` 的 `gitMeta()` 固定 `false`；
- `commands.mjs` 的交付 `git` 块直接沿用；`review-finish` 再把 review 上的同名字段写 `false`。

同时 T02 是 `readOnlyReference: true`，`deliver` 会拒绝为它建交付。两者叠加的结果：**T02 的 `clean-git-checkout` 检查在结构上永远不可能通过**，即使证据齐全。这不是"证据不足"，是工具设计缺陷。

## 变更

新增 `clean-checkout-verify` 命令（`tools/collab/lib/cleancheckout.mjs`）：

1. 读取 HEAD、分支与改动列表。**非 git 仓库**退出 5；**存在未提交源码**退出 3，并列出路径——克隆 HEAD 不可能包含它们，先提交再验证。
2. `git clone --no-hardlinks` 到系统临时目录空目录，要求克隆 HEAD 与原仓库 HEAD 一致。
3. 对 `git ls-tree -r HEAD` 的每个文件计算克隆侧与工作区侧的 SHA256，逐字节比对；结果与差异清单写入不可变记录。
4. 记录关键字段：`gitHead`、`treeFingerprint`（`path:blobSha` 的哈希，用于识别"内容没变"与"内容变了"）、`fileCount`、`byteIdentical`、`different[]`、`missing[]`、`ok`、`actor`、`evidence[]`、`statement`、`claims`、`doesNotClaim`。
5. 可选 `--evidence a.log,b.xml --statement "..."`：证据文件逐个记录 SHA256，缺文件或没有说明都退出 2，不允许只贴路径不给解释。
6. 记录写入 `docs/collaboration/verifications/CCV-*.json`，只追加不覆盖。

闸门侧：

- `clean-git-checkout` 现在接受两种证据：交付上的 `cleanGitCheckoutVerified=true`，或**与本提交内容 `treeFingerprint` 匹配**且 `ok` 的 CCV 记录。只读参考任务（T02）只能走第二种。
- 提交内容一变，`treeFingerprint` 立刻不同，旧记录不再被采信——旧的通过记录不会给新内容背书。
- 交付侧：`deliver` 只有在当前提交存在匹配记录、且工作区无源码改动时，才写 `cleanGitCheckoutVerified=true`，并把 `cleanCheckoutRecordId` 一起记下。

## 明确不声明的事

CCV 只声明"每个跟踪文件都被逐字节重建"。Unity 导入、编译、实际启动需要单独跑一次，作为 `--evidence` 附上；记录的 `doesNotClaim` 字段把这条边界固定下来，避免以后被理解成"工具说能玩"。

因此 `gate T02` 变绿表示"当前提交有字节级重现记录"，不等于"Unity 已从克隆启动验证"——后者以你附上的证据为准，见 `docs/handoff/T02_CleanCheckout_Verify_2026-10-18.md`。

## 脏工作区判定的例外

写交付、复验、验证记录本身会改动工作区。若把这些也算成"基线不可复现"，工具就永远无法验证自己。判定时排除三个协作输出目录：`docs/collaboration/`、`out/collab/`、`.collab-local/`；其余任何改动（含未跟踪源码）一律拒绝。记录里写明排除项（`ignoredForDirtyCheck`），不隐藏规则。

## 匹配键的例外（第二轮）

第一版把"全部跟踪文件"的哈希当作匹配键，结果是**记录一提交就自我失效**：`docs/collaboration/verifications/CCV-*.json` 自己也在跟踪文件之内，提交它等于换了树。同理，任何一次 `stage close`/复验产生的记录提交都会把 T02 的验证打成 stale，闸门周期性变红——这不是基线变了，是记账变了。

现在匹配键 `treeFingerprint` 只覆盖**基线文件**，排除 `docs/collaboration/`、`out/collab/`、`.collab-local/` 三个记账目录。校验范围本身没变：`clean-checkout-verify` 仍然逐字节比对**所有**跟踪文件，只是"这条记录算不算当前"的判据不再包含记账文件。

- 提交新的交付/复验/验证记录 → 记录继续有效。
- 改动任何源码、资源、设置或工具 → 记录立刻失效。
- 新增 CCV07B 用例专门守住这条边界（先追加记录→仍有效，再改源码→失效）。

依赖这一点，验证记录本身可以进版本库，成为可复查的证据，而不是只躺在某台机器的磁盘上。

## 测试

`tools/collab/tests/cleancheckout.test.mjs`，9 项，已并入 `run-all.mjs`：

| 编号 | 覆盖 |
| --- | --- |
| CCV01 | 正常克隆逐字节一致并落记录 |
| CCV02 | 重复执行追加而非覆盖 |
| CCV03 | 未提交源码修改 → 退出 3，且不产生记录 |
| CCV04 | 未跟踪源码 → 退出 3 |
| CCV05 | 协作输出目录的改动不阻塞验证 |
| CCV06 | 非 git 目录 → 退出 5 |
| CCV07 | 新提交使旧记录失效，且不删除历史 |
| CCV07B | 只追加协作记录的提交不让记录失效；改源码后仍失效 |
| CCV08 | 证据缺文件/缺说明退出 2；附证据时 SHA256 与文件一致 |
| CCV09 | 只读参考任务的闸门：验证前红、验证后绿 |

完整回归：**142 / 142**（原 132 + 新增 10）。

## 未做

- 未启动 Unity、未 commit/push、未修改游戏代码与任务书。
- 未改变交付/复验的角色约束与授权校验。
