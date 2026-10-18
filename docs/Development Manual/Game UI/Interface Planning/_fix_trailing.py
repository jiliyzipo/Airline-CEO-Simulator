import re

h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()

# Find the edges array and fix trailing comma
# Pattern: ,] -> ]
# But only within the DEFAULT_EDGES section
de_base = h.index('var DEFAULT_EDGES = [')
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

# Now dend is the outer ], check what's before it
before_close = h[dend-3:dend]
print(f"Chars before ]: {repr(before_close)}")

# Fix: if there's a trailing comma, remove it
# Pattern: "],\n];"  or  "],];"
edges_text = h[de:dend+1]
# Remove trailing comma before closing ]
fixed_edges = re.sub(r',(\s*)\]$', r'\1]', edges_text)
if fixed_edges != edges_text:
    print("Fixed trailing comma in edges")
    h = h[:de] + fixed_edges + h[dend+1:]

# Also check for any ],\n] patterns within edges (not just at end)
# Replace all instances of ",\n]" or ",\n    ]" trailing inside the array 
# But be careful: it should only be the last entry before ]

# Let me also check for the specific combo issue
combo = h.count(',,')
print(f"Double commas remaining: {combo}")
if combo > 0:
    # Find and fix
    for i in range(len(h)-2):
        if h[i:i+2] == ',,':
            # Could be in edges, CATALOG, or elsewhere
            print(f"Found at {i}: {repr(h[max(0,i-10):i+10])}")

with open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'w', encoding='utf-8') as f:
    f.write(h)
print("Written")
