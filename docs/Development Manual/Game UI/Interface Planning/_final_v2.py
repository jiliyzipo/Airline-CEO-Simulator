import json, re, io, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()

# Fix edge extraction: de_offset = len('var DEFAULT_EDGES = ') = 20 (pointing to [)
de_base = h.index('var DEFAULT_EDGES = [')
de = de_base + 20  # point to '['

depth = 0
dend = de
while dend < len(h):
    ch = h[dend]
    if ch == '[': depth += 1
    elif ch == ']':
        depth -= 1
        if depth == 0: break
    dend += 1
edges = json.loads(h[de:dend+1])
print(f"Total edges: {len(edges)}")

# MNT edges
mnt = [e for e in edges if e[0].startswith(('n17','n81','n82','n83','n84','n85')) or e[1].startswith(('n17','n81','n82','n83','n84','n85'))]
print(f"MNT edges: {len(mnt)}")
for e in mnt:
    print(f"  {e[0]} -> {e[1]}: {e[2]}")

# Also fix CATALOG extraction same way
cs_base = h.index('var CATALOG = [')
cs = cs_base + 14  # 'var CATALOG = ' = 14 chars
depth = 0
ce = cs
while ce < len(h):
    ch = h[ce]
    if ch == '[': depth += 1
    elif ch == ']':
        depth -= 1
        if depth == 0: break
    ce += 1
data = json.loads(h[cs:ce+1])
print(f"\nCATALOG entries: {len(data)}")

# n15 check
c15 = [c for c in data if c['id'].startswith('n15_')][0]
has = any('MNT' in str(cl) or '\u673a\u52a1' in str(cl) for cl in c15['clicks'])
print(f"n15 MNT: {'OK' if has else 'MISSING'}")

# CONTENT_REV
m = re.search(r"CONTENT_REV\s*=\s*'([^']+)'", h)
print(f"CONTENT_REV: {m.group(1)}")

# Braces
js = h.split('<script>')[1].split('</script>')[0]
b_ok = js.count('{') == js.count('}')
print(f"Braces: {js.count('{')}/{js.count('}')} {'OK' if b_ok else 'ERROR'}")

print("\nALL OK" if b_ok and has and len(mnt) >= 25 else "NEEDS FIX")
