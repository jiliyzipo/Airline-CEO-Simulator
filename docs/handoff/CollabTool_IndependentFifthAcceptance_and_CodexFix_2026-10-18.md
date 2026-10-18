# 第五轮独立验收与 Codex 修复记录

日期：2026-10-18。编号：ACCEPT-COLLAB-20261018-CODEX-05。

**结论分开记录：原第五轮 DEL 的 FR-01/FR-02 及既有 FR-03 用例通过，但仍有非法 XML 可被接受，原交付独立验收 failed。Codex 已按用户授权直接修复工作区，新增 26 项 XML 测试全部通过，完整既有回归亦通过；这一部分是 Codex 修复后自测，不冒充独立验收通过。**

## 1. 本次受检版本和授权

- 正式受检交付：[DEL-20261018022444-a55ae4](../collaboration/deliveries/DEL-20261018022444-a55ae4.json)。开发说明：[第五轮交付说明](CollabTool_Delivery_Fifth_2026-10-18.md)。
- 开始时 sync/ack revision 82；原交付工具源码文件清单哈希：`0965e49e503e24fd05a006b341a5e678bd23ca0fcfbfcdff0cb8d3f8da50d07e`。
- 本轮按用户此前明确授权执行：“如果下一轮验收依旧不通过，则由你来修正问题”。发现可复现缺陷后直接修工具，不再只退回报告。
- 只修改工作区协作工具解析器和新增测试；没有修改游戏业务代码/任务书，没有启动 Unity，没有提交/推送/重置/清理旧交付。
- 保留旧脚本，将 F03/F06 对应实际交付 ID 更新在[新的验收脚本副本](verification/CollabTool-Fifth-Independent-2026-10-18/acceptance-boundaries.mjs)中。FR 检查也显式指定 COLLAB_DELIVERY_ID，避免其默认指向已被替代的 DEL。

## 2. 原交付独立验证：已有修复有效

对正式 DEL 串行执行、每套显式指定 subject：

- 开发方测试：22/22 通过。
- baseline24：24/24；supplemental：12/12；boundary8：8/8。
- 更新真实交付引用后的原 F01～F06：6/6。
- 开发方 FR 专项：11/11，包括默认 smoke、协议移除负例、直接测试入口隔离、stdout 改写拒绝、顺序运行证据分离及历史 NUnit 样本兼容。
- 实际交付清单 20 文件哈希无变化，目录无额外文件。本轮根项目 smoke 执行后再次检查仍然如此。

证据：[原 DEL 既有四组汇总](verification/CollabTool-Fourth-2026-10-17/evidence/codex-fifth-delivery/summary.json)、[原 DEL FR 专项结果](verification/CollabTool-Fifth-Independent-2026-10-18/original-fr.results.json)。这些通过结果予以保留，不因下述新发现而否定。

另起正式独立记录：[REV-20261018030537-cc1568](../collaboration/reviews/REV-20261018030537-cc1568.json)。真实默认 smoke 退出 0、2 项均通过；最终独立结论 failed，原因是额外 XML 审计，**不是把 smoke 的成功伪写为失败**。机器计数仍保存 2/0，其他审计范围分别写入[验收 evidence](verification/CollabTool-Fifth-Independent-2026-10-18/review-evidence.json)。

## 3. 仍有缺陷：XML 文档语法校验未完整落实

新增针对性检查共 26 项：原 DEL 通过 10、失败 16。失败项都是被错误接受的非法输入；测试脚本位置在主工作区，但通过 COLLAB_AUDIT_TOOL_SOURCE **导入原 DEL 的解析器与 commands**，没有改旧交付。

三个直接例子（根计数均为 1/1/0/0）：

```xml
<!-- 属性之间缺少空白 -->
<test-run total="1"passed="1" failed="0" skipped="0" result="Passed"/>

<!-- 普通文本中出现保留的 CDATA 结束序列 -->
<test-run total="1" passed="1" failed="0" skipped="0" result="Passed">]]></test-run>

<!-- 注释内容出现双连字符 -->
<test-run total="1" passed="1" failed="0" skipped="0" result="Passed"><!--bad--comment--></test-run>
```

原解析器返回有效通过计数。新增 F07 进一步将第一个输入放入**本次 run 真实生成且有哈希绑定的 resultFile**，原 DEL 的 finish 仍输出 passed，故这不是仅影响辅助函数的理论问题。

其他失败边界包括原始非法控制字符、数字实体指向禁止字符/代理区、XML 声明出现在内容或空白之后、重复/非法声明、无目标处理指令及未经验证的 DOCTYPE。

原因与修改点：

- 属性循环先无条件 skipWs 后读下一个名字，没有要求属性前确有分隔空白。
- 注释只寻找结束标记，普通文本只解实体，没有校验其各自禁止出现的序列。
- 字符引用只限制 1～0x10FFFF，没有按 XML 1.0 字符范围过滤；原始文本也缺少同类检查。
- 所有 `<?...?>` 都被跳过，XML 声明没有位置、目标、顺序及内容约束。
- DOCTYPE 内容没有实际验证却被当作可以跳过。

规则依据：[W3C XML 1.0 字符、注释、处理指令和起始标签语法](https://www.w3.org/TR/xml/)。本次修复按结果文件所需的明确子集实现，不宣称提供完整通用 XML 处理器。

## 4. Codex 已实施的修复

修改 [tools/collab/lib/xml.mjs](../../tools/collab/lib/xml.mjs)，新增 [tools/collab/tests/xml.test.mjs](../../tools/collab/tests/xml.test.mjs)：

1. 属性名前必须存在 XML 空白；仍支持单/双引号、多行及正常属性间隔。
2. 拒绝注释中的非法连字符和普通文本中的原始 `]]>`；保留合法 CDATA 与 `]]&gt;`。
3. 原始字符与数字引用统一应用 XML 1.0 字符范围，拒绝控制字符、孤立代理值和禁止值，保留中文、合法补充字符及合法引用。
4. 校验 PI 的目标和分隔；XML 声明仅能位于文档开头且语法正确，不得重复或位于元素内容中。
5. 明确拒绝未支持的 DOCTYPE 和 XML 1.1；支持 ASCII 标记名的 XML 1.0 结果子集。加入 256 层嵌套上限，超限返回明确错误，不靠调用栈崩溃。
6. 新测试覆盖有效声明、注释、PI、Unicode、实体、CDATA、属性格式，以及上述非法输入；保留真实 Unity 历史样本兼容性检查。

差异：[xml-fix.diff](verification/CollabTool-Fifth-Independent-2026-10-18/xml-fix.diff)。本轮未改 commands、快照清单和状态机实现；第五轮已修好的结果绑定/隔离机制保留。

## 5. 修复后自测结果

- XML 专项：**26/26**。同一套测试原 DEL 为 **10/26**，修复版为 **26/26**，见 [修复前日志](verification/CollabTool-Fifth-Independent-2026-10-18/xml-before-final.txt)、[修复后日志](verification/CollabTool-Fifth-Independent-2026-10-18/xml-after-final.txt)。
- 完整既有四套：开发方 **22/22**，baseline **24/24**，supplemental **12/12**，boundary **8/8**，均通过，见[修复版回归汇总](verification/CollabTool-Fourth-2026-10-17/evidence/codex-fifth-repaired-current/summary.json)。新增 XML 专项是单独入口，不把它冒充原开发方测试中的用例。
- 原 F01～F06 加新增 F07：原 DEL **6/7**，Codex 修复工作区 **7/7**。F07 修复前 finish passed，修复后明确拒绝，见[原交付集成结果](verification/CollabTool-Fifth-Independent-2026-10-18/original-integration.results.json)、[修复版集成结果](verification/CollabTool-Fifth-Independent-2026-10-18/repaired-integration.results.json)。
- FR 专项：修复后 **11/11**，含历史 EditMode 25 与 PlayMode 99 的解析兼容；见[修复版 FR 结果](verification/CollabTool-Fifth-Independent-2026-10-18/repaired-fr.results.json)。这是读取历史 XML，没有重跑 Unity。

旧套件计数有重叠（baseline 包含整组开发方测试），不将各组简单相加。

修复后的工作区工具源码文件清单哈希：`20a21c575814eb159d92f74e6cb640e7af2dd132cf98b6ac0c9af77dd5722c87`。该值包含新增 XML 测试文件，**不同于**旧 DEL 指纹；旧 DEL 不会因主工程已修而自动通过。

## 6. 状态与下一步边界

- 独立验收记录：`REV-20261018030537-cc1568` = failed；关联缺陷：[ISSUE-20261018030616-3028cb](../collaboration/issues/ISSUE-20261018030616-3028cb.json)。
- 当前工作区：该缺陷已由 Codex 修复并完成上述自测；缺陷记录仍等待新交付与另一方复核，不改写旧记录成“原 DEL 已通过”。
- 当前未伪装 actor=cursor 发布新 DEL。Cursor 可以复核两个变更文件，用新 DEL 交付当前修复版；要宣称另一方独立验收通过，需要独立证据，不能把本报告的自测冒名转换。
- C07 真实 Unity 串联、C09 动态任务书映射与总完成闸门、C10 Hook 端到端/去重、C12 完整用户流程仍未完成。`cleanGitCheckoutVerified=false` 保持不变，本轮未勾选这些条款。
- 根项目 sync 后 revision 87，旧 DEL 对修复工作区显示 stale。游戏 T01～T06 任务书结论没有被更改。

复现 XML 前后对比：

```powershell
# 原 DEL：预期暴露 16 个拒绝缺口（退出 1）
$env:COLLAB_AUDIT_TOOL_SOURCE = 'out/collab/DEL-20261018022444-a55ae4/tools/collab'
node tools/collab/tests/xml.test.mjs

# Codex 修复工作区：预期 26/26（退出 0）
$env:COLLAB_AUDIT_TOOL_SOURCE = 'tools/collab'
node tools/collab/tests/xml.test.mjs
```

## 7. 保全核对

生产 Assets/Packages/ProjectSettings 联合清单仍为 `e108606412e05a72cd0ca905051bb23bc77620656b5a5624b4e615444e4ff9c9`。原 DEL/REV、历轮 DEL、游戏任务书保持原哈希；正式第五轮 DEL JSON 为 `33a6ea018274f409f071a0598bfb454a76c88d2043ee8ec51e4759dc9c7fcb53`。

20 个正式快照文件均匹配交付哈希，未列文件 0；本轮没有修改旧快照。仅新增本次 REV/issue/事件和验证材料；无 commit/push，无重置、清理或用户进程操作。
