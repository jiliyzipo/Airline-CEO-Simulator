# 协调工具 MVP 正式验收收口

日期：2026-10-18。回填/收口执行者：Codex。独立复验执行者：Cursor。

## 结论

**手动协作模式 MVP 正式验收通过**。受检交付 `DEL-20261018071756-010504`，独立复验 `REV-20261018072743-74a233`，真实 actor=cursor、independent=true、reviewException=true、profile=collab。Codex 仅核验现有证据并回填，不代写或改写 Cursor 的 REV。

需求手册 §14 的 C01–C12 已回填12/12；`ISSUE-20261018041847-27c5bf` 已按 fixed→verified→closed 收口。旧 C-DEMO 的失败DEL/REV保留历史原状，修复及独立复验由新 C-COLLAB-FINAL 的 DEL/REV 承接。旧任务仍显示历史rework/failed，不据此把旧交付改成通过。

## 已核验的依据

- 本次 runToken `30cc87cca6933a0d`：退出0，stdout599字节、stderr0字节，两个绑定SHA256与实际字节一致；112/0/0，与REV和机器JSON一致。
- 同一新快照：完整112/112（38+26+13+21+14），外部24/24、12/12、8/8，XML结果闸门12/12。存在覆盖重叠，不合计作独立用例总数。
- 授权SHA256、真实actor、三类subject指纹、三项AC覆盖及finishApplicability逐项匹配；35/35快照清单输入及当前工作区字节一致。
- 6项独立探针均记录通过，但P3实际触发“任务书缺失”，不拿它单独证明缺基线闸门。缺基线证据是完整套件closure G01，完整基线正例是G09。恢复用例N20来自baseline24，不归入boundary8。
- C09原缺陷由closure13、daily相关用例及新增R11覆盖；C10由并发G08/P4与适配器/手动流程证据覆盖。多profile不能混入同作者自测结果。
- C07沿用并重新核验先前工程记录：compile三闸门 errors=0，slot371，Unity EditMode25/25；对应日志及Unity结果文件SHA均匹配。本轮没有重跑编译、slot或Unity。

机器核验记录：`verification/CollabTool-ReverseReview-2026-10-18/codex-closeout-audit.json`。Cursor报告：`CollabTool_Cursor_IndependentReverseReview_2026-10-18.md`。正式清单及证据映射见工具需求手册§14.2。

## 工作区漂移与观察项

Cursor在验收前报告CLI因模块导出不匹配而无法启动。其恢复记录中24项restore的备份哈希，Codex已逐一核对：备份等于恢复前哈希，当前文件等于交付预期哈希；交付快照保持完整。

这证明曾存在不同字节的工作区输入并完成有备份恢复，**没有证据确认哪个进程/操作造成漂移**。上次07:22Z核验时输入相符，本次复验前出现漂移，时间区间不等于责任归属。备份与历史记录保留，避免未经调查归因。

- **OBS-01：保留为非阻断后续改进。** 当前CLI在静态import阶段遇到混合版本时，可能在doctor执行前抛裸堆栈。若后续实现，需要独立于业务模块的启动检查/受控动态导入，再配合清单比较；仅在doctor函数内部增加检查不足以捕获导入前异常。正常开发中的合法源码变化也不应一律视为损坏，更不能自动用旧交付覆盖开发修改。本轮不修改已验收程序，因此不产生新DEL/新一轮验收。
- **OBS-02：现有行为符合锁与幂等约定。** 并发竞争方可退出4，重试后deduped，applied保持1；`adapters/client-hook.mjs`已经对退出4做最多4次有限重试，耗尽后提示手动sync。无需为得到更好看的deduped输出而绕过互斥锁。原生客户端实际触发仍未验证。

## 使用范围与剩余边界

默认日常流程：同一Cursor聊天持续报告问题，Cursor先修复、自测、记录；用户要求“阶段收口”时整理任务书/任务定义/验收条件并生成DEL；Codex按阶段集中检测并生成REV。用户无需每个问题重发长指令，也无需逐个问题立即验收。

sync/ack与可选前台watch可用；没有启用或验收原生Hooks加载。特定Cursor反向授权仅用于本工具的指定Codex交付，日常仍按Cursor开发、Codex检测分工。

协调工具清单12/12不代表游戏完成。cleanGitCheckoutVerified=false以及T02/T03/T04/T05真实未完成条件继续阻塞全项目gate；本次没有勾游戏任务总项，没有更改游戏代码、玩家数据、原任务书内容或时间比例，没有启动Unity、commit/push或清理未跟踪文件。

本轮实际修改为工具需求手册§14/§14.2、协作状态中的缺陷迁移、追加事件/摘要以及本收口记录与核验产物。最终状态见 `verification/CollabTool-ReverseReview-2026-10-18/closeout-state.json`。
