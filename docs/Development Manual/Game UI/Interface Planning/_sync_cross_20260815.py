# -*- coding: utf-8 -*-
"""Sync Phase 3 BOARD + CROSS 金标准 into the whiteboard."""
import json
import re
import sys
import io
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

BOARD = Path(
    r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning"
    r"\Airline_CEO__UI交互白板 V1.0.html"
)

NEW_REV = "20260815e"


def load_array(h, marker):
    cs = h.index(marker) + len(marker)
    depth = 0
    for i in range(cs, len(h)):
        if h[i] == "[":
            depth += 1
        elif h[i] == "]":
            depth -= 1
            if depth == 0:
                return cs, i + 1, json.loads(h[cs:i + 1])
    raise SystemExit("array not found: " + marker)


def load_object(h, marker):
    cs = h.index(marker) + len(marker)
    depth = 0
    for i in range(cs, len(h)):
        if h[i] == "{":
            depth += 1
        elif h[i] == "}":
            depth -= 1
            if depth == 0:
                return cs, i + 1, json.loads(h[cs:i + 1])
    raise SystemExit("object not found: " + marker)


def main():
    h = BOARD.read_text(encoding="utf-8")

    h, n = re.subn(r"var CONTENT_REV = '[^']+';", f"var CONTENT_REV = '{NEW_REV}';", h, count=1)
    if n != 1:
        raise SystemExit(f"CONTENT_REV replace failed: {n}")

    note = (
        "  /* UX 2026-08-15e: Phase 3 BOARD + CROSS 金标准 — "
        "n20 董事会 L1 + n26 财年结算续聘解雇 + n21 CEO 决策会议 + n23 部门负责人任命 + n104 补条件表单 + "
        "n28 宏观经济面板 + n30 随机事件日志 + n38 历史回放 + n29 竞争总览 AI 航司；"
        "DecisionTypes/DecisionEconomics + DecisionMeetingManager 回写 GameState.decision；"
        "ShellRuntimeFactory 真面板 + PrefabNavIntegration 登记；PlayMode≥3 */\n"
    )
    if "UX 2026-08-15e" not in h:
        h = h.replace(f"var CONTENT_REV = '{NEW_REV}';", f"var CONTENT_REV = '{NEW_REV}';\n{note}", 1)

    cs, ce, catalog = load_array(h, "var CATALOG = ")
    by_id = {c["id"]: c for c in catalog}

    pages = {
        "n20_董事会": (
            "董事会 · L1 总览（年度目标清单+进度条/董事成员卡派系满意度/派系分布/预算投票→n22/财年结算→n26）",
            "金标准",
        ),
        "n26_财年结算_续聘_解雇": (
            "董事会 · 财年结算续聘解雇（目标完成率结算预览/续聘线难度/保留本届/重新组建/重开三分支）",
            "金标准",
        ),
        "n21_CEO_决策会议": (
            "CROSS · CEO 决策会议（顶栏议题/五部门意见卡红黄绿量化/批准·暂缓·拒绝·取消会议/暂缓→n104）",
            "金标准",
        ),
        "n23_部门负责人任命": (
            "CROSS · 部门负责人任命（空缺部门/候选人3选1/能力薪酬特质卡片/确认任命回写）",
            "金标准",
        ),
        "n104_补条件表单": (
            "CROSS · 补条件表单（来源议题摘要/条件清单可完成标记/机会成本与连锁影响/模板库/提交·取消）",
            "金标准",
        ),
        "n28_宏观经济面板": (
            "CROSS · 宏观经济面板（四阶段/GDP油价走势/需求油价融资竞对事件传导/下一财年展望）",
            "金标准",
        ),
        "n30_随机事件日志": (
            "CROSS · 随机事件日志（倒序日志/分类标题/效果摘要）",
            "金标准",
        ),
        "n38_历史回放": (
            "CROSS · 历史回放（决策日志/决策会议历史/里程碑）",
            "金标准",
        ),
        "n29_竞争态势_AI_航司": (
            "CROSS · 竞争总览 AI 航司（玩家市占/竞对列表类型状态掠夺者/深层分析→n60）",
            "金标准",
        ),
    }
    for pid, (meta, badge) in pages.items():
        if pid not in by_id:
            raise SystemExit("missing page id: " + pid)
        by_id[pid]["meta"] = meta
        by_id[pid]["badge"] = badge

    new_cat = json.dumps(catalog, ensure_ascii=False, separators=(",", ":"))
    h = h[:cs] + new_cat + h[ce:]

    marker = "var NOTE_FEATURE_PACK = "
    if marker in h:
        ns, ne, pack = load_object(h, marker)
        for pid in pages:
            c = by_id[pid]
            pack[pid] = {
                "items": c.get("items", []),
                "clicks": c.get("clicks", []),
            }
            if "title" in c:
                pack[pid]["title"] = c["title"]
            if "meta" in c:
                pack[pid]["meta"] = c["meta"]
            if "badge" in c:
                pack[pid]["badge"] = c["badge"]
        h = h[:ns] + json.dumps(pack, ensure_ascii=False, separators=(",", ":")) + h[ne:]
        print("updated NOTE_FEATURE_PACK")

    BOARD.write_text(h, encoding="utf-8")
    print("OK wrote", BOARD.name, "CONTENT_REV=", NEW_REV)


if __name__ == "__main__":
    main()
