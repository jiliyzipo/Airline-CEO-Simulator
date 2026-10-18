# 阶段自动化第二轮独立复验：通过

日期：2026-10-18。检测方：Codex。

## 受检交付与正式结论

- 本轮最终DEL：`DEL-20261018102500-1304ad`，任务`S01-COLLAB`，开发方Cursor。
- 正式REV：`REV-20261018103950-3b9460`，`passed`，检测actor=codex、independent=true、profile=collab。
- 交付自带完整回归：本轮实际在固定快照执行 **132/132**，失败/跳过均为0。
- 原独立脚本仅替换subject：**5/6**，唯一红项是SA05旧断言要求未补证的自由文本AC直接passed。
- 澄清断言并补充正例：**7/7**。仍要求未补证时failed/not_run，同时新增实际CLI显式补证、真实actor和授权哈希可追溯的正例。

本轮接受SA01–SA05修复。原失败`REV-20261018095357-18f7dd`及其5个原始ISSUE文件不改写；状态迁移与最后任务/项目闸门见 `verification/CollabTool-Stage-Reaccept-2026-10-18/closeout-state.json`。

## SA05检查契约的更正

原SA05夹具保留C-DEMO的自由文本AC，但只执行输出2/0/0计数的smoke命令。原断言同时要求“授权不被拒绝”和“该未补证任务直接passed”，后一部分与SA03的修复目标冲突。

已保留原脚本与原失败记录，另建两份副本：

1. `stage-audit-subject-only.mjs`：只替换subject为最终新DEL，原断言全部保留。本轮确实复现5/6，SA05授权已通、实际结论failed。
2. `stage-audit-reconciled.mjs`：SA05要求没有授权错误、实际生成Cursor独立REV、未补证条款全部not_run且整体验收failed；它比单纯`code !== 7`更明确。新增SA05B：在独立临时夹具，用真实CLI显式传`--coverage AC1=passed,AC2=passed,AC3=passed`，要求passed、actor=cursor、independent=true、reviewKind=independent，且证据`attestation.by=cursor`和`authorizationSha256`与授权文件逐字节哈希相符。

这两条分别证明授权路径可用与补证路径可用，不能再把“未补证必须失败”当授权未修复。补证是检测者的明确声明，不等于smoke自动验证了自由文本事实。

SA05B的Cursor身份只出现在隔离测试夹具中，验证CLI角色流程；本次真实项目正式REV由Codex创建，未在真实项目冒用Cursor身份。

原脚本SHA256、两版副本SHA256和修改理由：`verification/CollabTool-Stage-Reaccept-2026-10-18/test-contract-change.json`。原SHA保持`0688d2ad68b8c5c4f8455bf7ff6fa5b0410d185ea96c42a0cea9d0781412d179`。

## 五项修复的独立实测

- **SA01**：清单输入被预检命令改写后，preflight失败，handoff不再ready；独立完整性检查退出5。受污染副本未当成有效送检快照。
- **SA02**：其他review持有Unity资源锁时，预检退出4，哨兵未执行；本轮使用Node占位命令验证，未启动Unity。完整132项中另有占用未知时拒绝和释放租约后的正例。
- **SA03**：自定义必需产物不存在时，不能靠2/0/0计数自动验收；coverage不宣称passed，整体验收failed。机器可验证的正常模板仍可通过。
- **SA04**：两个并发stage-review实际执行数为1；没有第二个活动REV运行同一命令。不同DEL并行能力由实现的按DEL租约保留，未使用全局执行锁。交接索引读改写已有独立事务锁。
- **SA05**：授权本身不再被默认角色表误拒；未补证拒绝与显式补证通过分别成立。无授权、错交付等拒绝路径仍由132项套件覆盖。

原审计SA06正常流程正例继续通过。各套件有重叠，不把132、6、7相加成互不重叠的用例总数。

## 证据位置

- 正式日志与绑定：`docs/collaboration/reviews/REV-20261018103950-3b9460.json`的runs/stdout/stderr。
- 原断言本轮结果：`verification/CollabTool-Stage-Reaccept-2026-10-18/results-2026-10-18T10-41-55-516Z.json`。
- 澄清后7项结果：`verification/CollabTool-Stage-Reaccept-2026-10-18/results-2026-10-18T10-42-07-004Z.json`。
- 完整原始输出：同目录`stage-audit-subject-only.stdout.txt`、`stage-audit-reconciled.stdout.txt`及对应stderr。
- 证据复核：同目录`verified-evidence.json`，核对132项日志哈希、runToken、快照/工作区清单、补证身份与授权哈希。
- 缺陷状态迁移与最终闸门：同目录`closeout-state.json`。

再次复跑需要新输出目录/新文件名，现有证据采用排他创建，禁止覆盖历史结果。两份审计脚本输出JSON计数，必须检查assertionsFailed，不能仅凭进程退出0判断通过。

## 收口边界

5项旧缺陷仅在上述证据复核成立后，由Codex按fixed→verified→closed处理。修复说明中的较早中间DEL不改写，关闭备注明确关联最终DEL-20261018102500-1304ad。

旧MVP正式清单及旧失败REV保留历史；无需为此改写游戏任务书。全项目gate仍由游戏未完成项约束，不将协调工具阶段通过扩大为游戏全体验收。

本轮仅新增检测脚本副本、证据、报告和正常REV/ISSUE状态迁移；没有修改生产工具实现或配置，没有修改游戏代码/资源/玩家数据/游戏任务书，没有启动Unity、commit/push或清理文件。
