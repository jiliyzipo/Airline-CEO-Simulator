import urllib.request, re, io
from datetime import datetime, timezone

meta = urllib.request.urlopen("https://tiles.zoom.earth/times/radar.json").read().decode()
m = re.search(r'"reflectivity"\s*:\s*\{([^\}]+)\}', meta)
pairs = re.findall(r'"(\d+)"\s*:\s*"([0-9a-f]+)"', m.group(1))
ts, h = max(pairs, key=lambda p: int(p[0]))
dt = datetime.fromtimestamp(int(ts), tz=timezone.utc)
path = f"{dt:%Y-%m-%d}/{dt:%H%M}/{h}"
base = f"https://tiles.zoom.earth/radar/reflectivity/{path}"
print("path", path)

try:
    from PIL import Image
except ImportError:
    print("no PIL")
    raise SystemExit(1)

z = 3
n = 8
stats = {"webp": 0, "png": 0, "small": 0, "white_heavy": 0}
for y in range(n):
    for x in range(n):
        data = urllib.request.urlopen(f"{base}/{z}/{y}/{x}.webp", timeout=15).read()
        if len(data) <= 200:
            stats["small"] += 1
            continue
        if data[:4] == b"\x89PNG":
            stats["png"] += 1
            continue
        if data[:4] != b"RIFF":
            continue
        stats["webp"] += 1
        im = Image.open(io.BytesIO(data)).convert("RGBA")
        px = list(im.getdata())
        white = sum(1 for r, g, b, a in px if r > 235 and g > 235 and b > 235 and a > 200)
        if white > len(px) * 0.5:
            stats["white_heavy"] += 1
            print(f"  white tile z={z} y={y} x={x} white_pct={100*white/len(px):.0f}%")

print("stats", stats)

for label, z, y, x in [("china", 5, 13, 26), ("africa", 5, 10, 15), ("ocean", 5, 8, 12)]:
    data = urllib.request.urlopen(f"{base}/{z}/{y}/{x}.webp", timeout=15).read()
    print(label, "len", len(data), "head", data[:4])
    if data[:4] == b"RIFF" and len(data) > 200:
        im = Image.open(io.BytesIO(data)).convert("RGBA")
        px = list(im.getdata())
        white = sum(1 for r, g, b, a in px if r > 235 and g > 235 and b > 235 and a > 200)
        trans = sum(1 for r, g, b, a in px if a < 16)
        green = sum(1 for r, g, b, a in px if g > r + 20 and g > 80 and a > 80)
        print(f"  white={white} trans={trans} green={green} total={len(px)}")
