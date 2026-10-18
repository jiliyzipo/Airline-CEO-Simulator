import json, re
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
data = json.loads(h[cs+14:ce+1])
print(f"Total: {len(data)} entries")
for c in data:
    cid = c['id']
    if cid in ('n15_财务部', 'n17_机务工程部', 'n13_运营部', 'n18_人力资源部', 'n10_飞机详情_履历', 'n19_安全质量部'):
        print(f"\n{cid} clicks ({len(c['clicks'])}):")
        for cl in c['clicks']:
            print(f"  [{cl[0]}] -> {cl[1]}")

# Count MNT new entries
mnt = [c for c in data if int(c['id'][1:3]) >= 81]
print(f"\nNew MNT entries: {len(mnt)}")
for c in mnt:
    print(f"  {c['id']} - {c['title'][:50]}")
