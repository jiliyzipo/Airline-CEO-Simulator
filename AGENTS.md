# AGENTS.md · Airline-CEO-Simulator

共同规则（Cursor 与 Codex 均需遵守）。详细协议见：

- `docs/collaboration/PROTOCOL.md`
- `docs/collaboration/README.md`
- `docs/Development Manual/Development Task Book/Task book/Cursor_Codex_协作工具_开发需求与操作手册_v1.0_2026-10-17.md`
- 入口：`docs/ProjectManifest.md` → 链接到 `docs/collaboration/CURRENT.md`

## 每次开始任务 / 换模型 / 新对话

1. 阅读 `docs/collaboration/CURRENT.md` 与相关 delivery/review/issue。
2. 运行 `node tools/collab/cli.mjs sync`。
3. 运行 `node tools/collab/cli.mjs ack --actor <cursor|codex> --revision <当前revision>`。
4. 未 ack 最新 revision 前，不假设对方已知晓磁盘上的新状态。

> 等价的一条命令：`node tools/collab/cli.mjs stage-open --actor cursor|codex`（自动 sync + ack + 看板）。

## 阶段自动化（默认路径）

阶段收口与集中验证默认走 `stage` 命令，不再逐步执行 `task-add`/`deliver`/`review-*`。

- **Cursor 收口**：`node tools/collab/cli.mjs stage-close [--task <id>] [--note <text>]`
  自动复用/新建阶段任务、生成验收条件、写摘要、生成 DEL、在交付快照内跑一次 deciding profile 预检、写交接单 `out/collab/handoff/<DEL>.md`。
  预检通过才可交给对方；预检失败的 DEL 由 `stage-review` 主动跳过。
  预检是**开发自测**，不构成独立验收，不得据此声明“已通过”。
- **Codex 检测**：`node tools/collab/cli.mjs stage-review --delivery <DEL> --actor codex`
  自动 review-start → review-run → 证据 → review-finish；失败时自动登记 ISSUE。
  仍然只能由 Codex 执行；Cursor 无授权文件时不得以 `actor=cursor` 运行。
- **Cursor 取缺陷**：`node tools/collab/cli.mjs stage-fix`，修完重新 `stage-close` 生成新 DEL。

硬性约束不变：不冒用对方身份、不重复执行已完成的 review、不覆盖历史记录、不自动 commit/push、不杀 Unity。`stage-close` 与 `stage-review` 均继承这些约束。

## 角色

- **Cursor**：开发与交付；可写“已实现，待检测”；不可写“Codex 已通过”。
- **Codex**：独立检测与缺陷；默认不改生产业务代码。
- **用户**：设计裁决、是否提交/推送、关闭用户 Unity。

用户授权 Codex 开发工具后的反向复验：Cursor 只有持有匹配具体 DEL、三类指纹及 profile 的 `--authorization` 文件时，才可用真实 `actor=cursor` 执行该交付的 review-start/run/evidence/finish/cancel。不得冒用 codex/user。无授权仍按默认角色拒绝；缺陷 verified/closed、任务书总项回填及全局 recover 仍由 Codex/user 负责。Codex 自己检测自己的交付始终是开发自测。

## 阶段开发与集中验证

用户在同一聊天中连续反馈运行问题时，无需重复长开场指令。Cursor 可先修复、自测并记录问题，阶段结束再补齐任务书、阶段任务定义与验收条件；用户要求“阶段收口”时才生成 DEL，随后交 Codex 集中验证。同步与已读确认由代理按上述规则执行，不自动触发 Unity 或验收。换聊天、模型或交接时主动恢复同一阶段记录。

## 禁止

- 不自动 git commit / push。
- 不自动强杀 Unity、不删除 Unity 工程锁。
- 不覆盖 `deliveries/` / `reviews/` / `events/` 历史记录。
- 不用完整 `LogicSelfTests.Run()` 替代隔离回归。

既有协作约定仍见 `docs/AI_COLLABORATION_GUIDE.md`。
