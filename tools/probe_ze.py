import urllib.request, re, sys

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"

def get(url, ua=None):
    h = {}
    if ua:
        h["User-Agent"] = ua
    h["Accept"] = "*/*"
    return urllib.request.urlopen(urllib.request.Request(url, headers=h), timeout=15).read()

# 1) metadata with/without UA
for label, ua in [("no-UA", None), ("UA", UA)]:
    for i in range(3):
        try:
            d = get("https://tiles.zoom.earth/times/radar.json", ua)
            print(f"meta {label} try{i}: OK {len(d)} bytes, first={d[:20]}")
        except Exception as e:
            print(f"meta {label} try{i}: FAIL {e}")

# 2) parse latest reflectivity
try:
    meta = get("https://tiles.zoom.earth/times/radar.json", UA).decode("utf-8-sig")
    m = re.search(r'"reflectivity"\s*:\s*\{([^\}]+)\}', meta)
    pairs = re.findall(r'"(\d+)"\s*:\s*"([0-9a-fA-F]+)"', m.group(1))
    ts, h = max(pairs, key=lambda p: int(p[0]))
    from datetime import datetime, timezone
    dt = datetime.fromtimestamp(int(ts), tz=timezone.utc)
    path = f"{dt:%Y-%m-%d}/{dt:%H%M}/{h}"
    print("LATEST path", path, "hash", h)
except Exception as e:
    print("parse fail", e)
    sys.exit(0)

base = f"https://tiles.zoom.earth/radar/reflectivity/{path}"
# 3) tile with/without UA
for label, ua in [("no-UA", None), ("UA", UA)]:
    for z,y,x in [(5,13,26),(5,8,12),(3,4,4)]:
        try:
            d = get(f"{base}/{z}/{y}/{x}.webp", ua)
            print(f"tile {label} z{z}/{y}/{x}: {len(d)}B head={d[:4]}")
        except Exception as e:
            print(f"tile {label} z{z}/{y}/{x}: FAIL {e}")
