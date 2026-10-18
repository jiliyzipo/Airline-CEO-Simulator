# 协作工具 MVP · Codex 最终开发交付

日期：2026-10-18。任务：C-COLLAB-FINAL。

## 结论与使用范围

用户授权的工具 MVP 开发已完成：手动领取、交付、固定快照检测、缺陷退回、修复新交付、版本失效、双方 ack、任务书闸门、前台 watch 及中文操作入口均已实现并自测。默认使用 **sync/ack + 可选前台 watch**；可选客户端 Hook 桥接已提供，真实客户端加载和信任仍显示未验证，没有安装原生 Hook 配置，也不把 CLI 测试计数说成客户端接线成功。

本轮开发者和测试执行者都是 Codex。交付使用真实 actor=codex，并附用户授权；本轮 REV 的 `independent=false`、`reviewKind=developer-self-check`。这是开发完成与开发自测通过，不冒称另一方独立验收。原需求手册 §14 的独立验收勾选保持其原有结论；本轮开发项在单独开发任务书勾选。

## 最终受测对象

- DEL：`DEL-20261018051637-e21eae`，目录 `out/collab/DEL-20261018051637-e21eae`。
- 正式开发自测 REV：`REV-20261018051650-e59d57`，profile=collab。
- 实现指纹：`4fcef83d0df4a8daf8f48997e5facd6a42c1d8a1df83d2e28a0641f65528678e`。
- 快照 34 个清单输入，创建时未列文件 0；本机 config.json 不进入快照。
- 分支：`cursor/phase10-fleet-management`；HEAD：`9216aabd80e4f63c89611f90e3cdaa48b6f62045`。大量既有未提交及未跟踪内容保留，没有 commit/push。
- 角色授权：[授权记录](CollabTool_Codex_Development_Authorization_2026-10-18.json)。开发任务书：[逐项清单](CollabTool_Codex_Development_Checklist_2026-10-18.md)。

## 已完成的修改

1. C09 收口：有基线才允许完成；依赖和循环得到明确结果；条款文档/章节/子项唯一定位；最新交付及各必需 profile 不能借用旧结果；按真实创建时间排序。同作者自测不能放行独立验收闸门。gate 会核对当前字节，已经勾选也不能跳过条件。只撤回工具自己写入、条款未被手工改动的受管总项，追加日志；只读游戏引用和人工条款冲突不自动覆盖。
2. 并发与恢复：缺陷更新、ack、摘要在共享锁内操作；摘要原子替换；记录逐 ID 排他发布；claim 检查活动源码范围重叠。外机、损坏及无法证明进程死亡的锁保守保留；确认同机死进程的工具锁可回收，用户 Unity 锁保持不动。
3. 指纹与执行：策略 v2 排除本机 config.json，保留共享配置示例、未跟踪输入和完整清单；交付记录分支、HEAD 和实际文件状态。review-start 冻结 profileDefinition，执行和解析使用同一配置，profile 改动使当前适用性失效；拒绝快照中的符号链接/目录联接。
4. 日常操作：review-evidence 从绑定输出生成指纹/计数草稿，未明确验证的 AC 为 not_run，finish 仍独立核验。PowerShell 包装支持显式 profiles 和证据草稿，不再通过 claim 代替实际 ack。非法配置路径、重复 AC、目录越界、未知 profile 与非法 Hook stdin 非零退出；各命令支持 --help。
5. 实时提示：watch 就绪后用原生文件事件及轮询保守标记待复验，显式 sync 再读实际字节；不靠缓存宣布仍然通过，不替客户端 ack。隔离用例断言变更后五秒内显示 stale；只读游戏引用不反复扫描整份 Unity 资源。
6. Hooks：按 source/event 与事件身份去重，跨进程互斥；不同文件或文件版本不会被短时间防抖吞掉。两端 stdin 桥接、JSON/TOML 示例和明确手动模式已提供。doctor 不再把 applied/deduped 当作原生触发验证。
7. 环境与说明：doctor 只读检查，显示 .NET、生成工程引用、实际回归脚本/配置中的 UnityPath 及占用情况；不会导入、安装或启动 Unity。中文 README、操作手册和协议已更新，可直接给两端自然语言指令。

## 验证与证据

最终快照完整工具回归 **98/98**：开发方 38、XML 26、收口 13、日常流程 21。真实正式 run 退出 0，stdout/stderr 按 runToken 绑定，执行后的快照完整性为 true。计数仍使用该 profile 的 98 项，不把外部重复套件相加伪装成更多独立断言。

最终快照的外部 baseline24、supplemental12、boundary8 和 XML 闸门12 分别有独立脚本输出；见[最终证据目录](verification/CollabTool-Codex-Final-2026-10-18/)。工作区与早先快照的结果也保留，不替代最终 DEL 的取证。

最终归档：[机器汇总 release-final.json](verification/CollabTool-Codex-Final-2026-10-18/release-final.json)，allDevelopmentChecksPassed=true；正式 REV 已 finish 为 passed/current，计数98/0/0。外部最终为24/24、12/12、8/8，XML12/12。状态 revision 186，Codex 已 ack 186；Cursor 仍为165，下一次工作应实际读取新状态并自行 sync/ack，不代替 Cursor 确认。

[最终状态与闸门](verification/CollabTool-Codex-Final-2026-10-18/final-state.json) 明确保留 independent=false；C-COLLAB-FINAL 的独立验收闸门因 developer-self-check 返回 false，全项目 gate 也保持 false。[最终 doctor](verification/CollabTool-Codex-Final-2026-10-18/doctor-final.json) 为只读检查通过，识别当前实际 Unity 路径，原生 Hook verified=false。开发任务书12项勾选与这里的待独立验收状态含义不同。

两类失败均如实保留：

- 第一份交付的正式 run `REV-20261018050608-1af4d2` 在沙箱中因 CIM 进程读取被拒，doctor 返回 5，整套为 97/98；该 REV 保持 failed，没有覆盖。自动审批曾两次超时，后续权限检查恢复，最终正式 run 在可真实读取进程的环境执行。
- 最终 DEL 首次外部 N20 为 23/24：旧夹具看到 running 就杀 worker，可能恰在锁载荷初始化期间终止；工具保守返回 locked。原输出在 `final-delivery-e21eae` 保留。新增 `baseline24-recovery.mjs` 等到实际测试命令写出启动标记再杀进程，成功条件加强为 interrupted 且有原因；原历史测试不改写。复验见 `evidence/stable-final-e21eae`。

C07 本轮仅核对先前真实编译/slot/Unity 的证据，未重跑 Unity：此前工程编译三闸门 errors=0、slot 371/371、Unity EditMode 25/25；其文件/哈希核对见[上一轮独立报告](CollabTool_Closure_IndependentAudit_and_Fix_2026-10-18.md)。本轮不得说成重新通过引擎或游戏全套回归。

## 用户如何使用

两端打开同一个项目根，按 [中文快速上手](../collaboration/README.md) 的模板：

1. Cursor 先读 CURRENT 和任务、sync/ack，领取指定任务并编程自测，生成 DEL。
2. 用户只将 DEL 编号交给 Codex；Codex 按必需 profile 检测快照、核对条款，写 REV/ISSUE。
3. 失败时 Cursor 修复并新建 DEL，再交 Codex复验；通过记录变 stale 时重新检测。
4. 可在独立终端运行 `tools/collab/Collab.ps1 watch`，Ctrl+C 停止。watch 不开启另一端模型、不发送消息、不交付、不自动运行 Unity。

源码/测试：tools/collab；共同规则：AGENTS.md；状态入口：docs/collaboration/CURRENT.md；完整命令及异常处理：[操作手册](../collaboration/OPERATOR_MANUAL_ZH.md)。

## 保留的边界

同机同工作区 MVP 已开发；跨机器/worktree 共享、MCP 服务、自动唤起代理及自动聊天发送属于后续范围。客户端原生 Hooks 可选且未启用，不需要它们即可使用手动闭环与 watch。

游戏代码、场景、资源和游戏任务书未改。生产 Assets/Packages/ProjectSettings 联合哈希保持 `e108606412e05a72cd0ca905051bb23bc77620656b5a5624b4e615444e4ff9c9`；游戏任务书保持 `0b2562677604c72d5094cee856764f2e3d2ba74d5b4b247d6910b962d2a51194`。没有启动 Unity、运行完整 LogicSelfTests、改动玩家词库或全局客户端配置。

旧问题 ISSUE-20261018041847-27c5bf 保持 fixed；本轮同作者自测不自动将其标 verified/closed。全项目 gate 仍需如实受游戏未完成项、未验证干净检出及独立验收待办约束。工具开发完成不等于游戏开发完成或全项目 gate=true。
