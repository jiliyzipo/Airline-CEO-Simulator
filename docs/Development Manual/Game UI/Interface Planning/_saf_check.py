import json, io, sys, re
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
data = json.loads(h[cs:ce+1])

# Find n19
c19 = [c for c in data if c['id'].startswith('n19')][0]
print(f"n19 title: {c19['title']}")
print(f"n19 meta: {c19['meta']}")
print(f"n19 badge: {c19.get('badge','')}")
print(f"n19 items ({len(c19['items'])}):")
for i in c19['items']: print(f"  - {i}")
print(f"n19 clicks ({len(c19['clicks'])}):")
for cl in c19['clicks']: print(f"  [{cl[0]}] -> {cl[1]}")

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
saf_edges = [e for e in edges if e[0].startswith('n19') or e[1].startswith('n19')]
print(f"\nSAF edges ({len(saf_edges)}):")
for e in saf_edges:
    print(f"  {e[0]} -> {e[1]}: {e[2]}")

# Max ID
ids = [int(c['id'][1:3]) for c in data if c['id'][1:3].isdigit()]
print(f"\nTotal: {len(data)}  Max ID: {max(ids)}  Total edges: {len(edges)}")
print(f"CONTENT_REV: {re.search(r\"CONTENT_REV\s*=\s*'([^']+)'\", h).group(1)}")
