h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()

# The exact pattern: ["渠道预算", "L2 渠道预算"]], "kind": "core"}, {
marker = '["渠道预算", "L2 渠道预算"]], "kind": "core"}, {'
replacement = '["渠道预算", "L2 渠道预算"], ["机务部 \u00b7 维修预算", "L1 机务工程部"]], "kind": "core"}, {'

if marker in h:
    h = h.replace(marker, replacement, 1)
    with open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'w', encoding='utf-8') as f:
        f.write(h)
    print("DONE OK")
else:
    print("NOT FOUND")
