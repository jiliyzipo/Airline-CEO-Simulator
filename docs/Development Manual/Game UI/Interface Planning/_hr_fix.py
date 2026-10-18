import re, json, io, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()

# Fix trailing comma in edges
de_base = h.index("var DEFAULT_EDGES = [")
# Find closing ]
depth = 0
de = de_base + 20
dend = de
while dend < len(h):
    ch = h[dend]
    if ch == '[': depth += 1
    elif ch == ']':
        depth -= 1
        if depth == 0: break
    dend += 1

edges_text = h[de:dend+1]
# Remove trailing comma before ]
fixed = re.sub(r',(\s*)\]$', r'\1]', edges_text)
if fixed != edges_text:
    h = h[:de] + fixed + h[dend+1:]
    print("Fixed trailing comma")
else:
    print("No trailing comma found")

# Also fix possible double comma issue
h = h.replace('"],\n,\n  ["', '"],\n  ["')
print("Fixed double comma")

with open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'w', encoding='utf-8') as f:
    f.write(h)

# Verify
h2 = h if fixed == edges_text else open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()

# CATALOG
cs = h2.index("var CATALOG = [") + 14
depth = 0; ce = cs
while ce < len(h2):
    ch = h2[ce]
    if ch in '{[': depth += 1
    elif ch in '}]':
        depth -= 1
        if depth == 0: break
    ce += 1
cat = json.loads(h2[cs:ce+1])
print(f"CATALOG: {len(cat)} entries")

# EDGES
de = h2.index("var DEFAULT_EDGES = [") + 20
depth = 0; dend = de
while dend < len(h2):
    ch = h2[dend]
    if ch == '[': depth += 1
    elif ch == ']':
        depth -= 1
        if depth == 0: break
    dend += 1
edges = json.loads(h2[de:dend+1])
print(f"EDGES: {len(edges)}")

hr_edges = [e for e in edges if any(e[0].startswith(p) or e[1].startswith(p) for p in ('n18','n86','n87','n88','n89','n90','n91'))]
print(f"HR edges: {len(hr_edges)}")
for e in hr_edges:
    print(f"  {e[0]} -> {e[1]}: {e[2]}")

js = h2.split('<script>')[1].split('</script>')[0]
b_ok = js.count('{') == js.count('}')
m = re.search(r"CONTENT_REV\s*=\s*'([^']+)'", h2)
print(f"\nCONTENT_REV: {m.group(1)} | Braces: {js.count('{')}/{js.count('}')} {'OK' if b_ok else 'ERROR'}")
print("DONE" if b_ok else "CHECK")
