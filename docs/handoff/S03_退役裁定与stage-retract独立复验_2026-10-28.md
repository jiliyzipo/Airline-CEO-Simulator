# S03 退役裁定与 stage-retract 独立复验

检测者：Codex。对象：提交 `20c94fd5e479956b237eeae245ec026abae297b0`。

## 裁定

- S03-Docs 按“由真实修正交付 S04-UI 接替”退役，不补造 DEL，不回写历史 failed 为 passed，不把 git clean 当成业务验收。
- 旧 `REV-20261019092237-60f210` 保留 failed。其机器结果确为 total=25、failed=0、skipped=0，但缺少验收条款覆盖，不能事后改成通过。
- `ISSUE-20261019092302-a70313` **保持 closed**。原始不可变 JSON 的 open 不是有效状态；`state.json.issueStatus` 已在 2026-10-19T09:47:56.929Z 记录 verified→closed，关联替代交付 `DEL-20261019092547-d6429c` 的独立检测 `REV-20261019094559-31090f`。本次尝试改为 wontfix 被 CLI 以 closed→wontfix 非法转换拒绝，未改状态、不重开。这不追认原 failed 为 passed。
- 误归属 `DEL-20261028105347-defc40` 已记录 withdrawn、supersededBy=null，保留原交付、快照、指纹；本次没有对它运行 stage-review。
- `ISSUE-20261028110156-545592` 可 closed：归属错误通过撤回已行政处置。下述工具漏洞单独登记，不混入归属错误。

## S03 退役依据与落账

初查时尚未发现单一继任者，曾追加“无继任者行政退役、待工具支持”的暂定事件。进一步核对旧 ISSUE 的关闭说明与两份交付发现真实接替关系，最终以本节为准：

- `DEL-20261019092547-d6429c`（S04-UI）的摘要明确说明它修正 S03 的任务归属和 AC/profile 合同，业务代码与原 S03 相同。
- 两份 `files.workspaceChanges` 所列 9 个业务文件逐项一致，包含材料提及的全部 5 个文件。
- S04 已有独立通过记录 `REV-20261019094559-31090f`，旧 ISSUE 的关闭说明亦已关联它。
- 因此可用现有 `stage-supersede` 将**原** `DEL-20261019091504-e23a3e` 登记为由 S04 接替，并退役 S03 任务；新误打包 `DEL-20261028105347-defc40` 继续保持 withdrawn、supersededBy=null。
- 不把 retired 当作游戏总项完成，不更改原 failed 结论；任务退役通过追加 ledger/event 完成，不手改历史记录。

## 独立复验

从指定提交导出 `tools/collab` 至 `.collab-local/audit-20c94fd-retract/source`；在隔离目录运行，不启动 Unity，不修改生产代码。

- 原 `stage.test.mjs`：32/32 通过、0 失败、0 跳过，包括 R01–R04。
- 补充 `AUDIT-R05`：失败（1/1）。撤回夹具交付后，显式 `stage-review --delivery <该夹具 DEL>` 仍创建 review 并得到 passed，review 数从 0 变 1。
- 复现日志：`.collab-local/audit-20c94fd-retract/audit-probe.stdout.txt`；探针：`.collab-local/audit-20c94fd-retract/source/tools/collab/tests/audit-retract.test.mjs`。
- 已核对真实撤回 ledger 与 handoff；真实误归属 DEL 未检测。

**结论：stage-retract 独立复验未通过。** 当前只完成待办过滤，不保证显式检测入口拒绝 withdrawn 对象。应在 stage-review、review-start 等入口按共享 ledger 拒绝撤回对象，并在锁内复核，新增无副作用断言（不创建 REV/ISSUE、不运行套件）。R01–R04 全绿不足以证明这一边界。

此报告不是新的交付或伪造的正式 REV；缺陷关联真实提交与隔离证据。未提交、未推送。
