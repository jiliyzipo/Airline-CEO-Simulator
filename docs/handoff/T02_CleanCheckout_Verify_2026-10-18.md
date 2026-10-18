# T02 · 干净检出验证与 Unity 启动步骤

日期 2026-10-18。用途：让"Git 干净检出可复现"从一句无法验证的声明，变成有记录、可复查的证据。

## 已经完成的部分

- 游戏源码、工具、文档、设置已进入版本库（`2cde4f6`、`555ceb0`），之后是工具修复与记录提交。
- `.gitattributes` 关闭行尾转换（`* -text`）。本机 `core.autocrlf=true`，不关掉的话克隆会把 LF 写成 CRLF，逐文件 SHA256 与基线清单不符——第一次提交就踩过这个坑。
- 克隆到空目录逐文件比对：**2174 个基线文件全部字节相同，0 差异、0 缺失**；另有 423 个协作记录文件单独计数、不参与比对（它们是"关于基线的笔记"，工具每写一条就会变）。
- 干净检出内执行协作回归：**144 / 144**；默认场景 GUID 与 `EditorBuildSettings.asset` 一致；`SampleScene` 未启用；`manifest.json` + `packages-lock.json` 齐全。
- 闸门 `gate T02` 已绿（`clean-git-checkout` 由 `CCV-20261018134035-52b184` 支撑）。

## 这一段严格来说不是必须的

任务书 T02.6 要求的是"隔离目录导出 → 导入、编译、实际玩法启动"，2026-10-17 的本地导出已经做过这一步并有证据。现在的字节级验证证明"克隆出来的文件集合与那次被导入验证的源码集合完全一致"。

所以下面这套操作只多给一件事：**直接证据，而不是推理**。不做也不算违规；做了可以让"这个 commit 别人克隆下来就能玩"这句话不再依赖推断。

## 操作步骤（需要你在本机跑）

### 0. 前提：工作区必须是干净的

`clean-checkout-verify` 只验证**已提交**的内容，遇到未提交源码会直接拒绝（退出码 3）。先确认：

```powershell
git status --porcelain     # 应该没有任何输出
git rev-parse --short HEAD # 记下这个提交号
```

### 1. 克隆到短路径

路径过长曾导致包内样式资源异常；也不要克隆到仓库内部。

```powershell
git clone --no-hardlinks . C:\t02clean
```

> 也可以让工具克隆并保留：`node tools/collab/cli.mjs clean-checkout-verify --keep`，输出里的 `clonePath` 就是可交给 Unity 的目录。两种方式等价。

### 2. 只在克隆里隔离玩家数据（重要）

主工程的玩家身份是 `companyName: DefaultCompany` / `productName: Unity Project`。直接跑克隆会**共用同一份 PlayerPrefs**，Play Mode 里写存档可能覆盖你真实的存档。

导出器 `tools/regression/export-baseline.js` 的做法是把这两个字段覆盖成隔离值。对克隆做同样的两行修改——**只改克隆，绝不要改主工程**：

```powershell
$cfg = "C:\t02clean\Unity Project\ProjectSettings\ProjectSettings.asset"
$text = Get-Content $cfg -Raw
$text = $text -replace '(?m)^(\s*companyName:).*$', '$1 AirlineCEORegression'
$text = $text -replace '(?m)^(\s*productName:).*$', '$1 IsolatedBaseline_t02clean'
Set-Content -LiteralPath $cfg -Value $text -Encoding UTF8
```

顺带说明：这样改完，克隆目录里的 `git status` 会显示一个改动。这是预期的，克隆是丢弃品，不用提交。

### 3. 用正确的编辑器打开

必须用 `6000.3.21f1`（`ProjectSettings/ProjectVersion.txt` 里写的版本），本机路径：

```powershell
& "C:\Program Files\Unity 6000.3.21f1\Editor\Unity.exe" `
  -projectPath "C:\t02clean\Unity Project" `
  -logFile "C:\t02clean\unity-import.log"
```

- 本机还装了 `2022.3.62f2c1`，**不要**用它打开，会触发升级迁移
- 首次导入没有 `Library`，比平时慢很多；期间不要动主工程
- `-logFile` 会写一份独立日志，比全局 `Editor.log` 更适合当证据

### 4. 确认三件事

| 要确认 | 怎么看 |
| --- | --- |
| 导入 + 编译无错 | Console 没有红色 error；`unity-import.log` 里没有 `error CS` |
| 默认场景正确 | 打开 `Assets/MVP V1.0.unity`，层级/场景名不是 `SampleScene` |
| 实际进入玩法 | Play Mode → 主菜单 → 新建资料 → 进到经营界面（能操作，不只是加载） |

进入玩法后建议再截图，然后**正常关闭 Unity**（不要强杀）。

### 5. 把证据收进仓库

放到已被 gitignore 的 `out/ccv/`，避免污染工作区：

```powershell
New-Item -ItemType Directory -Force out\ccv | Out-Null
Copy-Item C:\t02clean\unity-import.log out\ccv\unity-import.log
# 截图手动拖进 out\ccv\ 即可
```

### 6. 让工具记录这条证据

```powershell
node tools/collab/cli.mjs clean-checkout-verify `
  --evidence out/ccv/unity-import.log,out/ccv/playmode.png `
  --statement "从 C:\t02clean 克隆导入 6000.3.21f1，编译 0 error，MVP V1.0 场景进入 Play Mode，主菜单新建资料进入经营界面"
```

它会**重新克隆一份**做逐字节比对，再把证据文件的 SHA256 与你的说明一起写进 `docs/collaboration/verifications/CCV-*.json`。你手工那份克隆不会被它读取或修改。

### 7. 确认闸门

```powershell
node tools/collab/cli.mjs gate --task T02 --json
```

`clean-git-checkout` 应仍为绿，且 `detail` 里的记录号是新生成的那份。

## 边界，不要越过

- 记录只声明**字节级重现**；Unity 是否真的导入成功，取决于你附上的证据，工具不代为判断。
- 提交内容一变，旧的记录立刻失效，闸门回到未通过。这是设计如此：旧记录不给新内容背书。
- 闸门判断的是**已提交的 HEAD**，不是工作区。绿灯说的是"这个 commit 别人能克隆重现"。
- 本轮没有启动 Unity、没有 commit/push、没有删除任何克隆或证据目录。
