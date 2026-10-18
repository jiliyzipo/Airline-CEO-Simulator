import urllib.request, io, re
from datetime import datetime, timezone

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

def get(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "*/*"}), timeout=20).read()

# 最新 reflectivity
meta = get("https://tiles.zoom.earth/times/radar.json").decode("utf-8-sig")
m = re.search(r'"reflectivity"\s*:\s*\{([^\}]+)\}', meta)
pairs = re.findall(r'"(\d+)"\s*:\s*"([0-9a-fA-F]+)"', m.group(1))
ts, h = max(pairs, key=lambda p: int(p[0]))
dt = datetime.fromtimestamp(int(ts), tz=timezone.utc)
path = f"{dt:%Y-%m-%d}/{dt:%H%M}/{h}"
base = f"https://tiles.zoom.earth/radar/reflectivity/{path}"
print("path", path)

from PIL import Image

# 中国区域 z=5 有几个瓦片有回波，找几个有数据的
for z, y, x in [(5,13,26),(5,13,25),(5,12,26),(5,13,27),(4,6,13),(4,7,13)]:
    data = get(f"{base}/{z}/{y}/{x}.webp")
    if data[:4] != b"RIFF" or len(data) < 200:
        print(f"z{z}/{y}/{x}: {len(data)}B head={data[:4]} -> empty/nodata")
        continue
    im = Image.open(io.BytesIO(data)).convert("RGBA")
    px = list(im.getdata())
    n = len(px)
    # 统计颜色分布
    from collections import Counter
    opaque = [p for p in px if p[3] > 128]
    # 白/浅灰（无回波背景）
    white = sum(1 for r,g,b,a in px if r>230 and g>230 and b>230)
    # 彩色（有回波）
    colored = sum(1 for r,g,b,a in px if max(r,g,b)-min(r,g,b) > 40)
    # 采样几个彩色像素
    samples = [p for p in px if max(p[0],p[1],p[2])-min(p[0],p[1],p[2]) > 40][:8]
    print(f"z{z}/{y}/{x}: {len(data)}B total={n} white={white} colored={colored} alpha_has_value={len(opaque)}")
    print(f"   彩色样本 RGB: {samples[:6]}")
