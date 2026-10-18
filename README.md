# Airline CEO Simulator

航空公司 CEO 经营模拟游戏。当前可运行客户端是 **Unity 6000.3.21f1 / C# / uGUI + TextMeshPro**，工程目录为 `Unity Project/`。Node.js 与 .NET 控制台工程用于开发检查和平衡分析；目前没有 React、TypeScript 或 PostgreSQL 运行服务。

## 开发入口

1. 阅读 [项目清单](docs/ProjectManifest.md)、[本阶段任务书](docs/Development%20Manual/Development%20Task%20Book/Task%20book/Airline_CEO_Dev_Task_2026-10-17.md) 和 [最新执行交接](docs/handoff/阶段执行交接_2026-10-17.md)。
2. 用对应 Unity 编辑器打开 `Unity Project/`；实际玩法与默认构建场景是 `Assets/MVP V1.0.unity`。运行后选择“从零创建”或“新游戏 · 接管”，完成表单进入经营界面。
3. 构建使用该场景；原 `SampleScene` 不再作为默认入口。编辑器 Play Mode 已有启动回归，独立 Player 构建与发布验收仍待执行。

现有工作区包含必要的未跟踪源码和资源，**当前 HEAD 的干净检出尚不能代表本地可玩版本**。未获明确授权不会提交或推送。详见 [基线报告](docs/handoff/T02_可复现基线_2026-10-17.md)。

## 验证

先保存并自行关闭占用中的 Unity；脚本检测到任何 Unity 实例会停止，不会关闭用户工程。

本机验证环境为 Node 24.18.0、.NET SDK 8.0.423。首次换机先在 Unity 导入工程并用 External Tools 重新生成 C# 项目，再运行 `node tools/semcheck/gen.js` 生成本机引用路径，按各 `.csproj` 执行 `dotnet restore`。生成的 semcheck 工程含本机 Unity/Library 引用，不能直接当作跨机器免导入依赖。

```powershell
node tools/semcheck/run.test.js
dotnet build tools/semcheck/semcheck.csproj --no-restore -v minimal -nologo
dotnet build tools/semcheck/semcheck_tests.csproj --no-restore -v minimal -nologo
node tools/semcheck/enccheck.js
dotnet run --project tools/slotcheck/slotcheck.csproj -c Release --no-restore

# 目的目录必须尚不存在；已有导出保留，复跑使用 out/b17-2 等新目录。
node tools/regression/export-baseline.js out/b17
& tools/regression/RunUnityTests.ps1 -ProjectPath 'out/b17/Unity Project' -Platform EditMode
& tools/regression/RunUnityTests.ps1 -ProjectPath 'out/b17/Unity Project' -Platform PlayMode
& tools/regression/RunUnityRegression.ps1 -ProjectPath 'out/b17/Unity Project' -Method AirlineCEO.EditorTools.DevelopmentPlaySmoke.Run -LogName ScratchSmoke -AsyncMethod
& tools/regression/RunUnityRegression.ps1 -ProjectPath 'out/b17/Unity Project' -Method AirlineCEO.EditorTools.DevelopmentPlaySmoke.RunTakeover -LogName TakeoverSmoke -AsyncMethod
& tools/regression/RunUnityRegression.ps1 -ProjectPath 'out/b17/Unity Project' -Method AirlineCEO.EditorTools.CapacityReceiptRegression.Run -LogName R42
```

`--no-restore` 要求先按项目文件恢复依赖；实际 Unity 测试与语义编译用途不同，二者不能互代。导出工程改变 companyName/productName，隔离 PlayerPrefs、持久目录和词库。**不要对玩家工程运行完整 `LogicSelfTests.Run()`：它会清空已学习指令词库。**

## 设计与架构

- [当前规则及差异](docs/Architecture/CurrentRules_2026-10-17.md)：退休、宏观、时间策略和范围。
- [系统架构与数据流](docs/Architecture/SystemArchitecture.md)：状态权威、执飞、交割、存档、账务和拆分边界。
- [排班与时刻槽设计](docs/GameDesignBible/MVP/V3.0/航班排班与时刻槽_重构方案_v1.0.md)：现行修订及 R42 背景。
- [CEO 视角设计](docs/GameDesignBible/MVP/V3.0/CEO视角重构_设计定稿_v1.0.md)。

验收以任务书勾选和实际结果为准。历史 MVP、发布、商店与云存档计划不自动成为本阶段已完成功能。
