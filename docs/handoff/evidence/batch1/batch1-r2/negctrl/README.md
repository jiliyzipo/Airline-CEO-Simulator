# 反向控制（证明新判定真的会红）

目的：证明「必测场景 N/A 即失败」「九项覆盖缺一即失败」不是写在注释里的口号，
而是**真的会把测试跑成红**。两次控制都只改测试文件、只改一份一次性快照副本，
**生产代码与仓库源码未动**，控制做完已把副本文件还原（哈希见文末）。

| 控制 | 改了什么 | 结果 |
|:--|:--|:--|
| `Batch1NegCtrlCover` | 把 `Covered_("⑦");` 注释掉（模拟「某场景被条件跳过但零失败」） | **Failed**：`check.36=FAIL ; 场景 ⑦ 已实际执行`，`pass=38 fail=1 covered=8/9`，`check.all=false` |
| `Batch1NegCtrlNa` | 把 `string full = FirstFullClock(...)` 强制成 `null`（模拟「满刻场景前提缺失」） | **Failed**：`check.07=FAIL ; 必测场景无法执行（N/A）—— ②满刻驳回…`，`check.29=FAIL ; 场景 ② 已实际执行`，`pass=35 fail=2 covered=8/9` |

两次都是 Unity exit≠0 / NUnit `Failed(Child)`，与旧版「只统计 fail、N/A 静默算过」形成对照。

还原后校对：`SlotSequenceRegression.cs` 的 SHA-256 回到
`D5B70475BAB32D48FDB2E7FB71A9E6747EE93F1CA134D0E78E3FCC636B974585`，
与 `../source-hashes.txt` 里的当前实现一致。

XML：`Tasks20261017_Batch1NegCtrlCover.xml`、`Tasks20261017_Batch1NegCtrlNa.xml`（本目录）。
