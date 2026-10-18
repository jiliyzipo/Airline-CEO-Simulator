import json, re, io, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()

# Find edges array
de_start = h.index('var DEFAULT_EDGES = [')
# Find matching close
depth = 0
de_end = de_start + 21  # skip 'var DEFAULT_EDGES = '
for i in range(de_end, len(h)):
    ch = h[i]
    if ch == '[': depth += 1
    elif ch == ']':
        depth -= 1
        if depth == 0:
            de_end = i
            break

raw = h[de_start+21:de_end+1]  # include the ]
print(f"Edges raw length: {len(raw)}")
print(f"First 200: {repr(raw[:200])}")
print(f"Last 200: {repr(raw[-200:])}")

# Check for problematic patterns
for p in [',,', ', ,', '],\n,']:
    if p in raw:
        idx = raw.index(p)
        print(f"Found '{repr(p)}' at {idx}: {repr(raw[max(0,idx-20):idx+20])}")

# Try json5-style fix: remove any standalone commas on lines
# Count commas vs arrays
from collections import Counter
print(f"\nCounts: '['={raw.count('[')}, ']'={raw.count(']')}, ',,'={raw.count(',,')}, ', '={raw.count(', ')}")

# Check for newline-comma-newline patterns
nl_comma = [i for i in range(len(raw)-2) if raw[i:i+3] == '\n,\n']
print(f"Number of '\\n,\\n' patterns: {len(nl_comma)}")
for pos in nl_comma[:3]:
    print(f"  at {pos}: {repr(raw[pos-30:pos+30])}")
