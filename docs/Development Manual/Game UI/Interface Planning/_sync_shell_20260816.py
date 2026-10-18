# -*- coding: utf-8 -*-
"""Phase 4 Week 14：SHELL 桶状态对齐实装（金标准/最小可玩）。"""
import json
import io
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

BOARD = Path(
    r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning"
    r"\Airline_CEO__UI交互白板 V1.0.html"
)
NEW_REV = "20260816f"

# 金标准（Phase 1.4 已全量落地）
GOLD = {"n8_航空事务官_AO"}
# 最小可玩（主路径已通，深度项延期）
PARTIAL = {
    "n0_主界面框架_常驻": "（最小可玩 · 延期：地图微件/术语Tooltip/暂停遮罩）",
    "n1_主菜单": "（最小可玩 · 延期：主菜单闸门）",
    "n2_玩家资料创建": "（最小可玩 · 最小表单）",
    "n3_公司注册": "（最小可玩 · 中文名校验已通）",
    "n4_董事会组建_抽卡": "（最小可玩 · 延期：真抽卡选座）",
    "n5_年度目标确认": "（最小可玩 · 最小表单）",
    "n6_主页_Dashboard": "（最小可玩 · 延期：地图微件）",
    "n7_收件箱": "（最小可玩 · 延期：完整邮件模型）",
    "n39_飞行学院": "（最小可玩 · 延期：文章详情+XP）",
    "n40_设置_存档": "（最小可玩 · 延期：显示/语言/音效）",
}


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


def main():
    h = BOARD.read_text(encoding="utf-8")

    import re
    h, n = re.subn(r"var CONTENT_REV = '[^']+';", f"var CONTENT_REV = '{NEW_REV}';", h, count=1)
    if n != 1:
        raise SystemExit(f"CONTENT_REV replace failed: {n}")

    note = (
        "  /* UX 2026-08-16f: Phase 4 Week 14 审计 — SHELL 桶状态对齐实装："
        "n8 顾问开局引导线→金标准；n0–n7/n39/n40→最小可玩（主路径通，深度项延期并注明理由）。"
        "BOARD/CROSS 已并入金标准。 */\n"
    )
    if "UX 2026-08-16f" not in h:
        h = h.replace(f"var CONTENT_REV = '{NEW_REV}';", f"var CONTENT_REV = '{NEW_REV}';\n{note}", 1)

    cs, ce, catalog = load_array(h, "var CATALOG = ")
    by_id = {c["id"]: c for c in catalog}

    for pid in GOLD:
        if pid not in by_id:
            raise SystemExit("missing " + pid)
        by_id[pid]["badge"] = "金标准"
        meta = by_id[pid].get("meta", "")
        if "金标准" not in meta:
            by_id[pid]["meta"] = meta + "（Phase 1.4 金标准 · 顾问开局引导线 S0–S6 全流程）"

    for pid, suffix in PARTIAL.items():
        if pid not in by_id:
            raise SystemExit("missing " + pid)
        by_id[pid]["badge"] = "最小可玩"
        meta = by_id[pid].get("meta", "")
        if "最小可玩" not in meta:
            by_id[pid]["meta"] = meta + suffix

    h = h[:cs] + json.dumps(catalog, ensure_ascii=False, separators=(",", ":")) + h[ce:]

    BOARD.write_text(h, encoding="utf-8")
    print("OK wrote", BOARD.name, "CONTENT_REV=", NEW_REV)
    print("GOLD:", sorted(GOLD))
    print("PARTIAL:", sorted(PARTIAL))


if __name__ == "__main__":
    main()
