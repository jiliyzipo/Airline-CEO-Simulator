# T02 · 干净检出验证与 Unity 启动步骤

日期 2026-10-18。用途：让"Git 干净检出可复现"从一句无法验证的声明，变成有记录、可复查的证据。

## 已经完成的部分

- 游戏源码、工具、文档、设置已进入版本库（`2cde4f6`、`555ceb0`）。
- `.gitattributes` 关闭行尾转换（`* -text`），否则 `core.autocrlf=true` 的机器克隆出来会把 LF 变成 CRLF，逐文件 SHA256 与基线清单不符。
- 克隆到空目录后逐文件比对：**2565 / 2565 字节相同，0 差异、0 缺失**，工作区无残留改动。
- 干净检出内执行协作回归：**132 / 132**；默认场景 GUID 与 `EditorBuildSettings.asset` 一致；`SampleScene` 未启用；`manifest.json` + `packages-lock.json` 齐全。

## 还需要你做的部分（需要 Unity）

工具不会启动 Unity，也不会替你判定"实际能玩"。以下步骤请你亲自跑一次。

### 1. 先提交当前改动

`clean-checkout-verify` 只验证**已提交**的内容：克隆无法包含未提交的源码，遇到脏工作区会直接拒绝（退出码 3）。提交哪些、提不提交由你决定。

### 2. 克隆到短路径

路径过长曾导致包内样式资源异常，请用短路径、且不要在仓库内部：

```powershell
git clone --no-hardlinks . C:\t02clean
```

也可以让工具克隆并保留：

```powershell
node tools/collab/cli.mjs clean-checkout-verify --keep
# 输出里的 clonePath 就是可交给 Unity 的目录
```

### 3. 用 Unity Hub 打开克隆目录

- Add project from disk → 选 `C:\t02clean\Unity Project`
- 编辑器版本以 `ProjectSettings/ProjectVersion.txt` 为准，不要升级、不要迁移
- 首次导入没有 `Library`，会比平时慢；**全程不要动主工程**

### 4. 记录三件事

| 要确认 | 怎么看 | 建议留下的证据 |
| --- | --- | --- |
| 导入 + 编译无错 | Console 没有任何红色 error | 复制 `Editor.log` |
| 默认场景正确 | 打开 `Assets/MVP V1.0.unity`，场景名不是 `SampleScene` | 截图 |
| 实际进入玩法 | Play Mode → 主菜单 → 新建资料，能进到经营界面 | 截图或 `Editor.log` 片段 |

### 5. 把证据交给工具

把文件放到仓库内的忽略目录（例如 `out/ccv/`），然后：

```powershell
node tools/collab/cli.mjs clean-checkout-verify --evidence out/ccv/editor.log,out/ccv/playmode.png --statement "从克隆导入，编译 0 error，主菜单进入新建资料并进入经营界面"
```

命令会重新克隆比对，并把证据文件的 SHA256 与你的说明一起写进 `docs/collaboration/verifications/CCV-*.json`。

### 6. 查看闸门

```powershell
node tools/collab/cli.mjs gate --task T02 --json
```

`clean-git-checkout` 变绿即代表"当前提交内容有对应的干净检出记录"。

## 边界，不要越过

- 记录只声明**字节级重现**；Unity 是否真的导入成功，取决于你附上的证据，工具不代为判断。
- 提交内容一变，旧的记录立刻失效，闸门回到未通过。这是设计如此：旧记录不给新内容背书。
- 本轮没有启动 Unity、没有 commit/push、没有删除任何克隆或证据目录。
