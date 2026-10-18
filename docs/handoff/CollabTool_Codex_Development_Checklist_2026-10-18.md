# 协作工具 MVP · Codex 授权接手开发任务书

授权：用户于 2026-10-18 在当前对话明确要求“授权你完成协调工具的全部开发工作”。授权副本见同目录 `CollabTool_Codex_Development_Authorization_2026-10-18.json`；CLI 交付保留 actor=codex，不冒用 Cursor。

本清单记录开发与自测完成度。需求手册 §14 仍是独立验收清单；同作者自测不自动替代独立复核。历史 DEL/REV/ISSUE 保留，不回写历史通过结论。

- [x] C01 数据校验、配置路径限制、非法输入和逐命令帮助。（开发38/38、daily D02/D10/D14/D17/D18、外部24/12）
- [x] C02 初始化、只读 doctor、状态、同步、双方确认读取。（开发38/38、D01、doctor-final.json）
- [x] C03 分类指纹、本机配置排除、冻结 profile、未跟踪及删除失效。（D19/D20/D21、外部24/12）
- [x] C04 稳定快照、manifest/overlay、运行前后完整性与玩家数据隔离。（外部24/12/8、正式run postSnapshot.ok=true）
- [x] C05 状态与复核锁、CAS、范围冲突、并发发布及 ack。（closure G08/G12、D07/D08/D09、外部并发用例）
- [x] C06 固定交付→检测→失败/缺陷→新交付→复验流程。（外部24项闭环、D05/D06；正式记录保留失败和新交付）
- [x] C07 工程入口及结果解析；本轮核对已有真实工程证据，不把历史运行说成本轮重跑。（已有编译/slot/Unity证据、XML26及结果闸门12）
- [x] C08 实现/测试/合同/profile 变化失效，终态及历史证据保持不变。（外部24/12/8、D19/D20）
- [x] C09 条款映射、基线、依赖、完成闸门、受管勾选失效与手工编辑冲突。（closure13/13、D10/D11/D19）
- [x] C10 两端规则、stdin 桥接、隔离幂等键及并发去重、明确手动模式；不声称原生接线已实测。（G08、D01/D02/D03/D04/D16）
- [x] C11 死 worker 恢复、异常锁保守处理、前台 watch 与退出。（D08/D12/D15，外部N20等待真实命令启动后要求interrupted及原因）
- [x] C12 中文快速上手、自然语言指令、包装命令与隔离日常演练。（D05/D06/D09/D12/D13/D16、README/操作手册）

开发验证包括 `tools/collab/tests/run-all.mjs` 与既有外部验收回归；最终结果、快照 ID 和具体证据在完成报告回填。

最终交付 DEL-20261018051637-e21eae；开发自测 REV-20261018051650-e59d57 passed/current、independent=false。固定快照：完整套件98/98（38+26+13+21），外部24/24、12/12、8/8，XML闸门12/12。逐项勾选仅代表开发与自测完成，不替代需求手册§14的独立验收。

报告：[最终开发交付](CollabTool_Codex_Final_Development_2026-10-18.md)。证据：`verification/CollabTool-Codex-Final-2026-10-18/final-delivery-e21eae/` 及 `evidence/stable-final-e21eae/`。原外部N20时序失败、沙箱CIM失败、审批超时说明均保留。

游戏任务 T01–T06、R42 及 cleanGitCheckoutVerified 保持原有真实状态，协作工具开发完成不等于游戏开发完成。全项目 gate 仍可因游戏未完成或独立验收待办为 false。
