# -*- coding: utf-8 -*-
"""Sync FIN 财务部金标准（Phase 2 Week 5）into the whiteboard."""
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

NEW_REV = "20260814b"


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


def replace_click(lst, old_dst_sub, new_dst):
    """Replace click description (index 1) whose text contains old_dst_sub."""
    for x in lst:
        if isinstance(x, list) and len(x) >= 2 and isinstance(x[1], str) and old_dst_sub in x[1]:
            x[1] = new_dst
            return True
    return False


def main():
    h = BOARD.read_text(encoding="utf-8")

    # CONTENT_REV
    h, n = re.subn(r"var CONTENT_REV = '[^']+';", f"var CONTENT_REV = '{NEW_REV}';", h, count=1)
    if n != 1:
        raise SystemExit(f"CONTENT_REV replace failed: {n}")

    note = (
        "  /* UX 2026-08-14b: FIN 财务部金标准 — n15/n22/n31/n32/n25/n103/n106；"
        "数值闭环（信用评级/WACC/年度预算/碳配额/紧急融资条款/破产重组）；"
        "FinanceEconomics+FinanceManager 回写；tools/balance FIN 年闭环；PlayMode≥3 */\n"
    )
    if "UX 2026-08-14b" not in h:
        h = h.replace(f"var CONTENT_REV = '{NEW_REV}';", f"var CONTENT_REV = '{NEW_REV}';\n{note}", 1)

    cs, ce, catalog = load_array(h, "var CATALOG = ")
    by_id = {c["id"]: c for c in catalog}

    fin = {
        "n15_财务部": (
            "FIN · 金标准收口（现金跑道 / 信用评级 / WACC / 年度预算 / 碳负债 / 紧急融资）",
            "金标准",
        ),
        "n22_年度预算编制_审批": (
            "财务部 · 年度预算编制/审批（各部门调额 · 董事会 ≥5 票）",
            "金标准",
        ),
        "n31_对冲工作台": (
            "财务部 · 燃油/汇率对冲（比例 0–80% · 期限 3/6/12 月）",
            "金标准",
        ),
        "n32_碳配额管理": (
            "财务部 · 碳配额（累计排放 / 已购配额 / 碳负债 / 航线碳成本）",
            "金标准",
        ),
        "n25_紧急融资_破产保护": (
            "跑道 <3 月解锁 · 银行/过桥/增资/卖资产/政府救助 + Chapter 11 重组",
            "金标准",
        ),
        "n103_条款确认": (
            "紧急融资条款确认（金额/利率/期限/抵押/风险提示 → 确认生效）",
            "金标准",
        ),
        "n106_重组条件勾选": (
            "Chapter 11 重组条件勾选（资产/人员/治理/财务约束 → 不可逆）",
            "金标准",
        ),
    }
    for pid, (meta, badge) in fin.items():
        c = by_id[pid]
        c["meta"] = meta
        c["badge"] = badge

    # 点击描述对齐实装
    replace_click(by_id["n22_年度预算编制_审批"]["clicks"],
                  "L4 董事会投票", "同页董事会投票（≥5 票通过写回 budgetApproved）")
    replace_click(by_id["n31_对冲工作台"]["clicks"],
                  "L5 确认 → 回财务", "HedgeManager 开仓（比例 0–80% · 期限 3/6/12 月）")
    replace_click(by_id["n32_碳配额管理"]["clicks"],
                  "L5 确认", "扣现金 → carbonPurchasedQuotaTons 增加")

    # n15：补快捷入口 + 已实现指标
    n15 = by_id["n15_财务部"]
    n15_items = list(n15.get("items", []))
    n15_items.append("✅ 已实现：L1 快捷入口按钮（年度预算 / 对冲工作台 / 碳配额 / 紧急融资）+ 问问财务顾问")
    n15_items.append("✅ 已实现：财务总览含信用评级 · WACC · 年度预算状态 · 碳负债 · PDP 应付 · 现金跑道")
    n15["items"] = n15_items

    new_cat = json.dumps(catalog, ensure_ascii=False, separators=(",", ":"))
    h = h[:cs] + new_cat + h[ce:]

    # NOTE_FEATURE_PACK
    marker = "var NOTE_FEATURE_PACK = "
    if marker in h:
        ns, ne, pack = load_object(h, marker)
        for pid in fin:
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
