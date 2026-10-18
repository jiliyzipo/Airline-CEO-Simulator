import urllib.request, io, re
from datetime import datetime, timezone
from collections import Counter

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

def get(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "*/*"}), timeout=20).read()

meta = get("https://tiles.zoom.earth/times/radar.json").decode("utf-8-sig")
m = re.search(r'"reflectivity"\s*:\s*\{([^\}]+)\}', meta)
pairs = re.findall(r'"(\d+)"\s*:\s*"([0-9a-fA-F]+)"', m.group(1))
ts, h = max(pairs, key=lambda p: int(p[0]))
dt = datetime.fromtimestamp(int(ts), tz=timezone.utc)
path = f"{dt:%Y-%m-%d}/{dt:%H%M}/{h}"
base = f"https://tiles.zoom.earth/radar/reflectivity/{path}"
print("path", path)

from PIL import Image

data = get(f"{base}/5/13/26.webp")
im = Image.open(io.BytesIO(data))
print("原图 mode:", im.mode)
rgba = im.convert("RGBA")
px = list(rgba.getdata())
n = len(px)

# alpha 分布
alpha_counts = Counter(p[3] for p in px)
print("alpha 分布 top:", alpha_counts.most_common(6))

# 背景（非绿色）像素颜色
bg = [p for p in px if max(p[0],p[1],p[2])-min(p[0],p[1],p[2]) <= 40]
print("背景像素数:", len(bg), "/", n)
bgc = Counter((p[0]//16*16, p[1]//16*16, p[2]//16*16) for p in bg)
print("背景颜色(量化) top:", bgc.most_common(8))

# 直接采样中间无回波区域的几个像素
for yy, xx in [(128,128),(10,10),(250,250),(100,200),(200,50)]:
    print(f"  像素({xx},{yy}) =", px[yy*256+xx])
