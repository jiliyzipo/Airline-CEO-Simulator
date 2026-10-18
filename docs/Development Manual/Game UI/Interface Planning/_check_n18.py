import json, io, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()
cs = h.index('var CATALOG = [')
ce = cs + 14
depth = 0
while ce < len(h):
    ch = h[ce]
    if ch in '{[': depth += 1
    elif ch in '}]':
        depth -= 1
        if depth == 0: break
    ce += 1
data = json.loads(h[cs+14:ce+1])
for c in data:
    if c['id'].startswith('n18'):
        print(f"n18 items: {len(c['items'])}")
        for i in c['items']: print(f"  - {i}")
        print(f"n18 clicks: {len(c['clicks'])}")
        for cl in c['clicks']: print(f"  [{cl[0]}] -> {cl[1]}")
        break

# Also count total
print(f"\nTotal CATALOG: {len(data)}")

# Find last id number
ids = [int(c['id'][1:3]) for c in data if c['id'][1:3].isdigit()]
print(f"Max ID: {max(ids)}")
print(f"Last IDs: {sorted(ids)[-5:]}")
