import json, sys
sys.stdout.reconfigure(encoding='utf-8')
h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()
cs = h.index('var CATALOG = [')
brace = 0
ce = cs + 14
while ce < len(h):
    ch = h[ce]
    if ch in '{[': brace += 1
    elif ch in '}]':
        brace -= 1
        if brace == 0: break
    ce += 1
print(f"JSON ends at byte {ce}")
data = json.loads(h[cs+14:ce+1])
ids = [c['id'] for c in data]
print(f"Count: {len(ids)}")
for i in ids:
    print(i)

# Find gaps
expected = set()
# n0-n26
for n in range(27):
    expected.add(f"n{n}_")
# skip n27 (likely not used)
# n28-n40
for n in range(28, 41):
    expected.add(f"n{n}_")
# n41-n52 even-ish
for n in range(41, 53):
    expected.add(f"n{n}_")
# n55-n66
for n in range(55, 67):
    expected.add(f"n{n}_")
# n67-n85
for n in range(67, 86):
    expected.add(f"n{n}_")

actual_prefixes = set()
for i in ids:
    actual_prefixes.add(i[:3])
    
print("\nMissing:")
for e in sorted(expected):
    if not any(a.startswith(e) for a in ids):
        print(f"  {e}")
print("\nExtra:")
for a in sorted(ids):
    if not any(a.startswith(e) for e in expected):
        print(f"  {a}")
