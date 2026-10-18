# -*- coding: utf-8 -*-
from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent
BOARD = ROOT / "Airline_CEO__UI交互白板 V1.0.html"
DOC = ROOT / "Airline_CEO__UI交互图 V1.0.html"
OUT = ROOT / "交接说明_UI交互白板_给新对话.md"
SNAP = ROOT / "_handoff_snapshot.json"


def main() -> None:
    h = BOARD.read_text(encoding="utf-8")
    cat = json.loads(re.search(r"var CATALOG = (\[[\s\S]*?\]);", h).group(1))
    edges = json.loads(re.search(r"var DEFAULT_EDGES = (\[[\s\S]*?\]);", h).group(1))
    rev = re.search(r"var CONTENT_REV = '([^']+)'", h).group(1)
    sk = re.search(r"var STORAGE_KEY = '([^']+)'", h).group(1)
    screens = [
        {
            "id": c["id"],
            "level": c["level"],
            "title": c["title"],
            "badge": c.get("badge", ""),
            "n_items": len(c.get("items") or []),
            "n_clicks": len(c.get("clicks") or []),
        }
        for c in cat
    ]
    att = sorted(p.name for p in (ROOT / "attachment").glob("*.docx")) if (ROOT / "attachment").exists() else []
    SNAP.write_text(
        json.dumps(
            {
                "CONTENT_REV": rev,
                "STORAGE_KEY": sk,
                "catalog_count": len(cat),
                "edge_count": len(edges),
                "screens": screens,
                "attachments": att,
            },
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )

    # key clusters
    mkt = [s for s in screens if any(k in s["id"] for k in (
        "市场", "收益", "定价", "季节", "航季", "旺季", "辅助收入", "竞争", "外部环境", "FFP", "产品工坊"
    )) or s["id"] in ("n14_市场销售部", "n29_竞争态势_AI_航司", "n33_FFP_常旅客管理", "n34_产品工坊")]

    lines = []
    a = lines.append
    a("# UI 交互白板 · 新对话交接说明")
    a("")
    a("> 用途：新开 Cursor 对话时，把本文件路径发给 Agent，并要求先读本文件与主白板 HTML。")
    a("")
    a("## 1. 项目与主文件")
    a("")
    a("- 仓库：`C:\\Users\\jiliy\\Airline-CEO-Simulator`")
    a("- **主工作文件（拖拽连线白板）**：")
    a(f"  `{BOARD}`")
    a("- 文档版交互图（较早，未必与白板完全同步）：")
    a(f"  `{DOC}`")
    a("- 用户 Word 规格附件目录：")
    a(f"  `{ROOT / 'attachment'}`")
    a("- 识图：遇到图片时用 `node C:/Users/jiliy/.cursor/skills/claude-vision/vision.js`，不要用 Read 直接看图。")
    a("")
    a("## 2. 当前白板状态（快照）")
    a("")
    a(f"- `CONTENT_REV`：**{rev}**（变更内置目录后会 bump，浏览器会清掉内置页的 extras 覆盖以显示新 CATALOG）")
    a(f"- `STORAGE_KEY`：`{sk}`（布局/批注/自定义卡存在 localStorage）")
    a(f"- 内置界面数：**{len(cat)}**")
    a(f"- 默认连线数：**{len(edges)}**")
    a("- Word 附件存 IndexedDB：`airline_ceo_ui_board_docs_v1`（导出 JSON 的 `docs` 字段含 base64）")
    a("- 预览库：`vendor/mammoth.browser.min.js`")
    a("- 机器可读快照：`_handoff_snapshot.json`")
    a("")
    a("## 3. 工作方式（务必遵守）")
    a("")
    a("1. 用户在黄框写短批注，或点「添加 Word」挂 `.docx`（表格/分级用 Word）。")
    a("2. Agent 读取 `attachment/*.docx` 或用户导出的 JSON，把规格整理进卡片 **功能列表 `items`** 与 **点击跳转 `clicks`**，并更新 `DEFAULT_EDGES`。")
    a("3. 改完后 **bump `CONTENT_REV`**，保证浏览器刷新后看到新内容。")
    a("4. 「批注→功能框」按钮：把黄框文本整理进功能列表；**不清空 Word 附件**。")
    a("5. 未提及的页面不要改；用户还在继续标注。")
    a("6. 用户规则：不要主动 commit；不要写无关 markdown（本交接文件是用户明确要求）。")
    a("")
    a("## 4. 已完成的主要工作（按时间线）")
    a("")
    a("### 4.1 早期批注（2026-08-09 JSON）")
    a("已写入主界面框架/主菜单/玩家资料/公司注册/年度目标/主页/收件箱/机队/飞机详情/航线网络/运营部等；新增单航线、航班周总计划、航班详细计划。")
    a("")
    a("### 4.2 市场收益")
    a("- L1 `n14_市场销售部`：KPI×4、完整航线收益排行")
    a("- L2 `n44_航线收益分析`")
    a("")
    a("### 4.3 航班定价簇")
    a("- L2 `n49_航班定价`（注意：归属表有时误写 `n44_航班定价`，现网 **航班定价=n49**，**航线收益分析=n44**）")
    a("- L3 `n45_单航线定价详情` / `n46_动态定价算法` / `n47_收益管理仪表盘` / `n48_辅助收入管理`")
    a("- L3 `n50_外部环境分析`（高铁热力图；与竞争文档里的 n50_竞对详情 **ID 冲突待解**）")
    a("- 连线见 `attachment/页面间连线建议.docx`")
    a("")
    a("### 4.4 季节性簇（最近一次，CONTENT_REV=20260810a）")
    a("- L2 `n55_季节性日历`")
    a("- L3 `n56_航季对比详情` / `n57_旺季策略配置器` / `n58_历史趋势分析`")
    a("- L4 `n59_旺季影响预测弹窗`")
    a("- 交互：市场「季节性日历」、航班定价/航线收益分析「季节性趋势 Tab」→ `n55`（不再指向笼统的 L3 旺季策略）")
    a("- 依据：`attachment/季节性页面归属对照表.docx` + `attachment/L2 季节性日历.docx` 等")
    a("")
    a("## 5. 重要 ID 约定与冲突")
    a("")
    a("| 现网 ID | 页面 | 注意 |")
    a("|---|---|---|")
    a("| n14 | 市场销售部 | 已更新多入口 |")
    a("| n44 | **航线收益分析** | 勿当成航班定价 |")
    a("| n49 | **航班定价** | 归属表有时误标 n44 |")
    a("| n50 | **外部环境分析**（高铁） | 竞争文档想要 n50=竞对详情 → 冲突 |")
    a("| n29 | 竞争态势/AI航司（旧） | 与新「竞争雷达」关系未统一 |")
    a("| n55–n59 | 季节性簇 | 已落地 |")
    a("")
    a("**待办（高优先级）**：`attachment/Airline_CEO_L2_竞争雷达_设计文档.docx` + `L3 竞对详情.docx` 尚未并入白板。")
    a("文档写 `n49_竞争雷达`，与现网 `n49_航班定价` 冲突；建议新开 `n60+` 或重编号后再接连线。")
    a("")
    a("## 6. attachment 目录清单")
    a("")
    for name in att:
        a(f"- `{name}`")
    a("")
    a("## 7. 市场相关界面速查")
    a("")
    a("| ID | 层级 | 标题 | items | clicks |")
    a("|---|---|---|---:|---:|")
    for s in mkt:
        a(f"| `{s['id']}` | {s['level']} | {s['title']} | {s['n_items']} | {s['n_clicks']} |")
    a("")
    a("完整目录见 `_handoff_snapshot.json`。")
    a("")
    a("## 8. 常用操作脚本（Interface Planning 下）")
    a("")
    a("| 脚本 | 作用 |")
    a("|---|---|")
    a("| `_extract_seasonal_docs.py` | 抽取 attachment 下 docx → `_seasonal_docs_bundle.md` |")
    a("| `_apply_seasonal_ownership.py` | 落地季节性页面与连线 |")
    a("| `_apply_pricing_screens.py` | 落地航班定价 + 4 个 L3 |")
    a("| `_apply_mkt_revenue_docs.py` | 市场 + 航线收益分析 |")
    a("| `_notes_into_features.py` | 批注→功能文案包 + 按钮逻辑 |")
    a("| `_add_word_notes.py` | Word 附件 UI（IndexedDB/导出） |")
    a("| `_make_handoff.py` | 生成本交接文件 |")
    a("")
    a("改 CATALOG 推荐：写 Python 解析/替换 `var CATALOG` / `var DEFAULT_EDGES` / `NOTE_FEATURE_PACK`，并 bump `CONTENT_REV`。")
    a("")
    a("## 9. 给新 Agent 的启动指令（可复制）")
    a("")
    a("```")
    a("请先阅读并遵守：")
    a("docs/Development Manual/Game UI/Interface Planning/交接说明_UI交互白板_给新对话.md")
    a("主文件：")
    a("docs/Development Manual/Game UI/Interface Planning/Airline_CEO__UI交互白板 V1.0.html")
    a("规格附件在 attachment/。用户会继续用黄框批注或上传 Word；")
    a("请只改提到的页面，更新 items/clicks/DEFAULT_EDGES，bump CONTENT_REV。")
    a("下一个未完成项：竞争雷达设计文档（注意与 n49/n50 ID 冲突）。")
    a("```")
    a("")
    a("## 10. 用户使用提醒")
    a("")
    a("1. 打开白板 HTML 后若内容偏旧：硬刷新；或点工具栏确认 CONTENT_REV 已升级。")
    a("2. 备份：导出 JSON（含 Word `docs`）。")
    a("3. 文档版 `Airline_CEO__UI交互图 V1.0.html` 未保证与白板同步，以白板为准。")
    a("")

    OUT.write_text("\n".join(lines), encoding="utf-8")
    print("wrote", OUT)


if __name__ == "__main__":
    main()
