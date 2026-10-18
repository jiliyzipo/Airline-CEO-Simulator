# -*- coding: utf-8 -*-
from pathlib import Path
import json, re, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

p = Path(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html')
h = p.read_text(encoding='utf-8')

# Fix: CATALOG ends with }; instead of }];
idx = h.find('var DEFAULT_EDGES')
before = h[idx-30:idx]
print('Before DEFAULT_EDGES:', repr(before))

if 'new"};' in before or 'new"};\n' in h[idx-20:idx]:
    # Find the exact position of the faulty closing
    # CATALOG should close with }] before DEFAULT_EDGES
    # Currently: ..."kind": "new"};\n\n  var DEFAULT_EDGES
    bad = '"kind": "new"};'
    # Only replace the one right before DEFAULT_EDGES
    pos = h.rfind(bad, 0, idx)
    if pos < 0:
        print('ERROR: pattern not found')
        raise SystemExit(1)
    h = h[:pos] + '"kind": "new"}];' + h[pos+len(bad):]
    p.write_text(h, encoding='utf-8')
    print('FIXED: added missing ] before ;')
else:
    print('Already looks fixed?', repr(before))

# Re-read and validate
h = p.read_text(encoding='utf-8')
cs = h.index('var CATALOG = [') + 14
depth = 0
for i in range(cs, len(h)):
    if h[i] == '[': depth += 1
    elif h[i] == ']':
        depth -= 1
        if depth == 0:
            ce = i + 1
            break
cat = json.loads(h[cs:ce])
print(f'CATALOG OK: {len(cat)} entries, last={cat[-1]["id"]}')

de = h.index('var DEFAULT_EDGES = [') + 20
depth = 0
for i in range(de, len(h)):
    if h[i] == '[': depth += 1
    elif h[i] == ']':
        depth -= 1
        if depth == 0:
            dend = i + 1
            break
edges = json.loads(h[de:dend])
print(f'EDGES OK: {len(edges)}')

# Check trailing comma on edges
edges_snip = h[dend-5:dend+5]
print('EDGES end snippet:', repr(edges_snip))

js = h.split('<script>')[1].split('</script>')[0]
print(f'braces {{}} : {js.count("{")}/{js.count("}")}  brackets []: {js.count("[")}/{js.count("]")}')
print('CONTENT_REV:', re.search(r"CONTENT_REV\s*=\s*'([^']+)'", h).group(1))
print('ALL GOOD')
