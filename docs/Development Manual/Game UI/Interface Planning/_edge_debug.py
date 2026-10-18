import json, re, io, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()

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
print(f"Total edges: {len(edges)}")
print(f"Sample last 5 edges:")
for e in edges[-5:]:
    print(f"  {e}")

# Check specific MNT edge
for e in edges:
    if 'n17' in e[0] or 'n17' in e[1]:
        print(f"  MNT edge: {e[0]} -> {e[1]}: {e[2]}")
        break
else:
    print("No MNT edges found at all!")
    
# Let me check what's near the END of edges
print(f"\nFirst edge: {edges[0]}")
print(f"Edge at 520: {edges[520] if len(edges)>520 else 'MISSING'}")
print(f"Edge at 525: {edges[525] if len(edges)>525 else 'MISSING'}")
print(f"Edge at 530: {edges[530] if len(edges)>530 else 'MISSING'}")
print(f"Edge at 560: {edges[560] if len(edges)>560 else 'MISSING'}")
print(f"Edge at 565: {edges[565] if len(edges)>565 else 'MISSING'}")

# Search for n17 in ANY edge
for i, e in enumerate(edges):
    s = str(e)
    if 'n17' in s or 'n81' in s or 'n82' in s or 'n83' in s or 'n84' in s or 'n85' in s:
        print(f"  Edge[{i}]: {e}")
