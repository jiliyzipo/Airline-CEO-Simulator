# -*- coding: utf-8 -*-
import re, json, sys
sys.stdout.reconfigure(encoding="utf-8")

path = r"C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html"
s = open(path, encoding="utf-8").read()
m = re.search(r"var CATALOG = (\[.*?\]);", s, re.S)
data = json.loads(m.group(1))

prefixes = ("n20", "n26", "n21", "n23", "n104", "n28", "n30", "n38", "n4", "n29", "n60")
for d in data:
    if d["id"].startswith(prefixes):
        print("=" * 80)
        print("ID:", d["id"])
        print("LEVEL:", d.get("level"))
        print("TITLE:", d.get("title"))
        print("BADGE:", d.get("badge"))
        print("META:", d.get("meta"))
        print("--- ITEMS ---")
        for it in d.get("items", []):
            print("  *", it)
        print("--- CLICKS ---")
        for c in d.get("clicks", []):
            print("  >", c[0], "=>", c[1] if len(c) > 1 else "")
