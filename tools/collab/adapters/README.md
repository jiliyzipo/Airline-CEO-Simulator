# Hooks 适配（可选）

适配器只转发事件到同一 CLI，不维护第二份状态。

当前交付选择**手动模式**：两端在任务边界 sync/ack，可运行前台 watch。hooks.enabled=true 仅允许 CLI 事件处理，不表示客户端接线已完成。doctor 和 hooks-status 返回各端 mode/reason/nativeVerified=false；计数只代表 CLI 执行，不代表原生触发。

client-hook.mjs 接收 stdin JSON（含 UTF-8 BOM），调用 hook CLI，输出客户端可接受的空 JSON；失败显示手动同步提示。从脚本位置定位项目根，只重试资源锁竞争，不交付、不 ack、不自动检测。相同 source/event/id 去重，不同文件版本不会被吞掉。

## Cursor

示例：`cursor-hooks.json.example`。项目根启动时使用数组式命令配置，映射 sessionStart/afterFileEdit/stop：

```text
node tools/collab/adapters/client-hook.mjs cursor afterFileEdit
```

交付仍须明确调用 deliver；本项目 Hook 不调用 deliver，不推断任务完成。示例不是已安装配置，须在对应客户端检查支持的事件和实际触发日志。

## Codex

首选 `codex-hooks.json.example`，放入项目 `.codex/hooks.json`；`codex-hooks.toml.example` 是另一种表示，不同时安装。示例命令要求从仓库根启动客户端。

OpenAI 官方文档：[Hooks](https://learn.chatgpt.com/docs/hooks) 说明项目配置位置、SessionStart/PostToolUse/Stop、stdin/stdout 及信任要求。项目配置和具体 Hook 定义须经客户端信任后才会执行；本工具不改信任库、不绕过审批。只有客户端已确认加载、真实触发且日志可关联后，才可另行记录原生接线通过；未确认时保持手动模式。

客户端版本本轮只读取：Codex CLI 0.163.0-alpha.10.2、Cursor 3.15.6；版本号不证明配置被加载。当前未安装全局或项目原生 Hook 配置，不承诺把变化注入正在生成的模型上下文。

## 原则

- 仅项目级配置；改全局用户配置需用户另行授权。
- 按事件身份去重；连续不同变更均保留，watch 提供快速保守失效提示。
- Hooks 关闭时手动 CLI 仍可用（C10）。
