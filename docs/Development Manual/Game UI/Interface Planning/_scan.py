import re, json, sys
sys.stdout.reconfigure(encoding='utf-8')
from pathlib import Path

d = Path(r"C:/Users/jiliy/Airline-CEO-Simulator/docs/Development Manual/Game UI/Interface Planning")
files = list(d.glob("Airline_CEO__UI*.html"))
p = files[0]
h = p.read_text(encoding="utf-8")
cat = json.loads(re.search(r'var CATALOG = (\[.*?\]);', h).group(1))
edges = json.loads(re.search(r'var DEFAULT_EDGES = (\[.*?\]);', h).group(1))
by = {c['id']: c for c in cat}

relevant = ['n14_', 'n33_', 'n34_', 'n49_', 'n6_', 'n44_', 'n55_', 'n7_', 'n60_', 'n29_']
for cid in relevant:
    for full_id in by:
        if full_id.startswith(cid):
            c = by[full_id]
            print(f"--- {full_id} {c['level']} {c['title'][:30]}")
            if c.get('items'):
                for item in c['items']:
                    if any(w in item for w in ['合同','大客','FFP','品牌','忠诚','常旅客','产品','工坊','差旅','TMC','企业','折扣']):
                        print(f"  ITEM: {item}")
            for cl in c.get('clicks', []):
                if any(w in str(cl) for w in ['合同','大客','FFP','产品','工坊','常旅客']):
                    print(f"  CLICK: {cl}")
            break

# Check how many edges exist, and what IDs are next
max_n = max(int(re.search(r'n(\d+)', cid).group(1)) for cid in by)
print(f"\nMax n ID: n{max_n}")
print(f"Total catalog entries: {len(cat)}")
print(f"Total edges: {len(edges)}")

# Check n14 market department items/clicks for depth
n14 = by.get('n14_市场销售部')
if n14:
    print(f"\n=== n14 items ===", len(n14.get('items',[])))
    for item in n14.get('items',[]):
        print(f"  {item}")
    print(f"=== n14 clicks ===", len(n14.get('clicks',[])))
    for cl in n14.get('clicks',[]):
        print(f"  {cl}")
