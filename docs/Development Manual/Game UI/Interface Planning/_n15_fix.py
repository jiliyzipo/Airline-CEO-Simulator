h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()

# Add MNT click to n15: find the n15->n16 boundary and insert
idx16 = h.index('"id": "n16_')
idx16_start = h.rfind('{"id": "n15_', 0, idx16)
chunk = h[idx16_start:idx16]

# Find the exact last click close: ]], "kind":
marker = '渠道预算"], "kind": "core"}, '
pos = chunk.rfind(marker)
if pos > 0:
    new = '渠道预算"], ["机务部 · 维修预算", "L1 机务工程部"]], "kind": "core"}, '
    pos_abs = idx16_start + pos
    h = h[:pos_abs] + new + h[pos_abs+len(marker):]
    with open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'w', encoding='utf-8') as f:
        f.write(h)
    print("DONE: n15 MNT click added")
else:
    print("marker not found")
    with open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\_n15_debug.txt', 'w', encoding='utf-8') as f:
        f.write(repr(chunk[-500:]))
    print("Debug written")
