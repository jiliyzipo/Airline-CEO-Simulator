h = open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'r', encoding='utf-8').read()

# The issue: there's a trailing comma after the last strategy edge AND a leading comma 
# before the MNT edges, creating ",\n," which is invalid JSON.
# Fix: remove the leading comma on the MNT edges line

# Pattern: after "机队规划长策"],\n,\n  ["
old = '机队规划长策"],\n,'
new = '机队规划长策"],'
if old in h:
    h = h.replace(old, new, 1)
    with open(r'C:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Game UI\Interface Planning\Airline_CEO__UI交互白板 V1.0.html', 'w', encoding='utf-8') as f:
        f.write(h)
    print("Fixed OK")
else:
    print("Pattern not found")
    # Find in file
    idx = h.find('机队规划长策')
    if idx > 0:
        print(f"Found at {idx}, context: {repr(h[idx-5:idx+30])}")
