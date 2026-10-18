import json, re, io, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()

# CATALOG
cs = h.index("var CATALOG = [") + 14
depth = 0; ce = cs
while ce < len(h):
    ch = h[ce]
    if ch in '{[': depth += 1
    elif ch in '}]':
        depth -= 1
        if depth == 0: break
    ce += 1
cat = json.loads(h[cs:ce+1])
print(f"CATALOG: {len(cat)} entries")

# EDGES
de = h.index("var DEFAULT_EDGES = [") + 20
depth = 0; dend = de
while dend < len(h):
    ch = h[dend]
    if ch == '[': depth += 1
    elif ch == ']':
        depth -= 1
        if depth == 0: break
    dend += 1
edges = json.loads(h[de:dend+1])
print(f"EDGES: {len(edges)}")

hr_edges = [e for e in edges if any(e[0].startswith(p) or e[1].startswith(p) for p in ('n18','n86','n87','n88','n89','n90','n91'))]
print(f"HR edges: {len(hr_edges)}")
for e in hr_edges:
    print(f"  {e[0]}\t-> {e[1]}\t: {e[2]}")

# Check n18
c18 = [c for c in cat if c['id'].startswith('n18')][0]
print(f"\nn18 items: {len(c18['items'])}  clicks: {len(c18['clicks'])}")

# Check new
for n in range(86, 92):
    found = any(c['id'].startswith(f'n{n}') for c in cat)
    print(f"n{n}: {'OK' if found else 'MISSING'}")

js = h.split('<script>')[1].split('</script>')[0]
b_ok = js.count('{') == js.count('}')
m = re.search(r"CONTENT_REV\s*=\s*'([^']+)'", h)
print(f"\nCONTENT_REV: {m.group(1)} | Braces: {js.count('{')}/{js.count('}')} {'OK' if b_ok else 'ERROR'}")
print("ALL DONE" if b_ok else "CHECK")
