# -*- coding: utf-8 -*-
import json, io, sys
from pathlib import Path
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

BOARD = Path(r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html")
h = BOARD.read_text(encoding="utf-8")

def load_json_array(h, marker):
    cs = h.index(marker) + len(marker)
    depth = 0
    for i in range(cs, len(h)):
        if h[i] == "[":
            depth += 1
        elif h[i] == "]":
            depth -= 1
            if depth == 0:
                return json.loads(h[cs:i+1])

catalog = load_json_array(h, "var CATALOG = ")
ids = ["n15_财务部", "n22_年度预算编制_审批", "n31_对冲工作台", "n32_碳配额管理", "n25_紧急融资_破产保护", "n103_条款确认", "n106_重组条件勾选"]
by_id = {c["id"]: c for c in catalog}
for i in ids:
    c = by_id.get(i)
    if not c:
        print("MISSING:", i); continue
    print("="*90)
    print("ID:", i, "| level:", c.get("level"), "| badge:", c.get("badge"))
    print("title:", c.get("title"))
    print("meta:", c.get("meta"))
    print("entry:", c.get("entry"))
    print("items:")
    for it in c.get("items", []):
        print("   -", it)
    print("clicks:")
    for cl in c.get("clicks", []):
        print("   -", cl)

# edges involving FIN ids
print("\n\n===== EDGES involving FIN =====")
edges = load_json_array(h, "var DEFAULT_EDGES = ")
fin_ids = set(ids)
for e in edges:
    if isinstance(e, list) and len(e) >= 2 and (e[0] in fin_ids or e[1] in fin_ids):
        print("  ", e)
