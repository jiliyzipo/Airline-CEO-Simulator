# -*- coding: utf-8 -*-
import json, re, sys, io
from pathlib import Path
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
h = Path(r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html").read_text(encoding="utf-8")
print("REV", re.search(r"CONTENT_REV = '([^']+)'", h).group(1))
cs = h.index("var CATALOG = ") + 14
depth = 0
for i in range(cs, len(h)):
    if h[i] == "[": depth += 1
    elif h[i] == "]":
        depth -= 1
        if depth == 0:
            ce = i + 1
            break
cat = json.loads(h[cs:ce])
for pid in ["n0_主界面框架_常驻", "n3_公司注册", "n6_主页_Dashboard", "n8_航空事务官_AO"]:
    c = next(x for x in cat if x["id"] == pid)
    print("---", pid)
    print("meta:", c.get("meta"))
    print("items sample:", c["items"][:2])
    print("clicks sample:", c["clicks"][:2])
es = h.index("var DEFAULT_EDGES = ") + 20
depth = 0
for i in range(es, len(h)):
    if h[i] == "[": depth += 1
    elif h[i] == "]":
        depth -= 1
        if depth == 0:
            ee = i + 1
            break
edges = json.loads(h[es:ee])
print("edge n0->n8:", [e for e in edges if e[0].startswith("n0") and "n8" in e[1]])
print("edge n6->n8 left:", [e for e in edges if e[0].startswith("n6") and "n8" in e[1]])
