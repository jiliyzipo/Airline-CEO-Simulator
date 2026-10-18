# 项目清单 · 2026-10-17

当前工程：`Unity Project/`，Unity 6000.3.21f1；主场景 `Assets/MVP V1.0.unity`。运行时语言 C#，uGUI/TMP 生成界面，ScriptableObject/Resources 提供静态配置，GameState 为在局状态，PlayerPrefs/JSON 为本地持久化。没有数据库服务器部署前提。

文档入口依次为：根 README、本阶段任务书、`handoff/阶段执行交接_2026-10-17.md`、`Architecture/CurrentRules_2026-10-17.md` 和 `Architecture/SystemArchitecture.md`。历史交接保留原始日期；新实测纠正历史结论时不覆盖原记录。

协作状态入口（程序生成，勿手工维护第二套）：`docs/collaboration/CURRENT.md`。协议与操作见 `docs/collaboration/README.md`、`AGENTS.md`。

设计依据：Game Design Bible v1.0（源 docx；已提取文本 `_gdb_v1.0_extract.txt` 可检索）、MVP/V3.0 CEO 视角定稿、排班与时刻槽修订。宏观三阶段实现与四阶段设计仍有差异，见当前规则；不以代码存在即视为设计批准。

Git 分支 `cursor/phase10-fleet-management`，本轮起点 HEAD `9216aab`。大量必要文件尚未跟踪；本轮保留所有既有修改和删除项，未提交、未推送、未批量暂存。必需文件及 SHA256 清单由 `tools/regression/export-baseline.js` 导出到 `handoff/Baseline_2026-10-17.json`。

测试：semcheck 两工程仅验证编译；slotcheck 为逻辑断言；Unity EditMode/PlayMode 验证实际引擎行为。R42 独立入口不执行完整自测。新测试在隔离项目中运行，防止覆盖玩家数据。

新开发必须先核对任务书和最新交接。不可直接沿用旧“全部完成”或旧时间节奏；发布准备继续后置。
