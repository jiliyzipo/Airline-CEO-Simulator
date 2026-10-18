import json, re, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()

# CATALOG
cs = h.index('var CATALOG = [')
brace = 0; ce = cs + 14
while ce < len(h):
    ch = h[ce]
    if ch in '{[': brace += 1
    elif ch in '}]':
        brace -= 1
        if brace == 0: break
    ce += 1
data = json.loads(h[cs+14:ce+1])

# EDGES
de = h.index('var DEFAULT_EDGES = [') + 21
brace2 = 0; dend = de
while dend < len(h):
    ch = h[dend]
    if ch == '[': brace2 += 1
    elif ch == ']':
        brace2 -= 1
        if brace2 == 0: break
    dend += 1
edges = json.loads(h[de:dend+1])

ok = True

# n15 clicks
c15 = [c for c in data if c['id'].startswith('n15_')][0]
has_mnt = any('MNT' in str(cl) or '\u673a\u52a1' in str(cl) for cl in c15['clicks'])
print(f"n15 MNT link: {'OK' if has_mnt else 'MISSING'}")
if not has_mnt: ok = False

# n17 items/clicks
c17 = [c for c in data if c['id'].startswith('n17_')][0]
print(f"n17 items: {len(c17['items'])} clicks: {len(c17['clicks'])}")
if len(c17['clicks']) < 10: print("  WARN: n17 clicks too few"); ok = False

# n81-n85
for nid in (81,82,83,84,85):
    found = any(c['id'].startswith(f'n{nid}') for c in data)
    print(f"n{nid}: {'OK' if found else 'MISSING'}")
    if not found: ok = False

# MNT edges
mnt_edges = [e for e in edges if e[0].startswith(('n17','n81','n82','n83','n84','n85')) or e[1].startswith(('n17','n81','n82','n83','n84','n85'))]
print(f"MNT edges: {len(mnt_edges)}")
min_expected = 25
if len(mnt_edges) < min_expected:
    print(f"  WARN: expected >= {min_expected}")
    ok = False

# CONTENT_REV
m = re.search(r"CONTENT_REV\s*=\s*'([^']+)'", h)
rev = m.group(1) if m else '???'
print(f"CONTENT_REV: {rev}")
if rev == '20260810n':
    print("  WARN: not bumped")

# Braces
js = h.split('<script>')[1].split('</script>')[0]
b_ok = js.count('{') == js.count('}')
print(f"Braces: {js.count('{')}/{js.count('}')} {'OK' if b_ok else 'MISMATCH!'}")
if not b_ok: ok = False

print(f"\n=== {'ALL CHECKS PASSED' if ok else 'SOME CHECKS FAILED'} ===")
