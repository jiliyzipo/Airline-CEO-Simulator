# RainViewer 插值实现细节（已废弃存档）

> 状态：**已废弃**（2026-09-03）。RainViewer 免费 raw 固定 ~70km 网格，无法更精细；
> 已切换 NASA GIBS GPM IMERG（~10km + 客户端插值，见 `GPM_IMERG_云图方案.md`）。
> 下文仅作技术存档。

## 一、核心插值代码（RadarDbzMapper.cs）

### 1. 块边长推断

块边长由 z 直接推断。免费 raw ≈ 固定 ~70km 地面网格，块边长随 z 倍增
（实测 z3=4、z5=16、z7=64、z8=128、z9=256；抬 z 不增加真实细节）：

```csharp
/// <summary>
/// 各 z 最近邻块边长。免费 raw ≈ 固定 ~70km 地面网格，块边长随 z 倍增：
/// 实测 z3=4、z4=8、z5=16、z6=32、z7=64、z8=128、z9=256。
/// </summary>
public static int BlockForZoom(int z)
{
    int b = z >= 7 ? 64 << (z - 7) : 64 >> (7 - z);
    return Mathf.Clamp(b, 1, 256);
}
```

### 2. 主插值（格心采样 + 全格双线性 + 软边 alpha）

把每个最近邻块的中心当作「格心采样点」，向外扩 1 格（边界由相邻瓦片 raw 数据补齐，
见 `SampleCell`），对强度 G 与覆盖度 A 同步做双线性插值：

```csharp
public static Color32[] RemapRaw(byte[] raw, int w, int h, int block, RawMargins m)
{
    var pixels = new Color32[w * h];
    if (raw == null || block <= 1)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            byte v = raw != null ? raw[i] : (byte)0;
            pixels[i] = v == 0 ? new Color32(0, 0, 0, 0) : MapDbz(v * 0.5f - 32f);
        }
        return pixels;
    }

    int cw = w / block, ch = h / block;
    int ew = cw + 2, eh = ch + 2;
    var cellG = new float[ew * eh];
    var cellA = new float[ew * eh];
    for (int ey = -1; ey <= ch; ey++)
    {
        for (int ex = -1; ex <= cw; ex++)
        {
            byte v = SampleCell(raw, w, h, block, cw, ch, m, ex, ey);
            int idx = (ey + 1) * ew + (ex + 1);
            cellG[idx] = v;
            cellA[idx] = v > 0 ? 1f : 0f;
        }
    }

    for (int y = 0; y < h; y++)
    {
        float uy = (y + 0.5f) / block - 0.5f;
        int ey = Mathf.Clamp(Mathf.FloorToInt(uy), -1, ch - 1);
        float ty = uy - ey;
        for (int x = 0; x < w; x++)
        {
            float ux = (x + 0.5f) / block - 0.5f;
            int ex = Mathf.Clamp(Mathf.FloorToInt(ux), -1, cw - 1);
            float tx = ux - ex;

            int i00 = (ey + 1) * ew + (ex + 1);
            int i10 = i00 + 1, i01 = i00 + ew, i11 = i01 + 1;
            float g = Mathf.Lerp(Mathf.Lerp(cellG[i00], cellG[i10], tx), Mathf.Lerp(cellG[i01], cellG[i11], tx), ty);
            float cov = Mathf.Lerp(Mathf.Lerp(cellA[i00], cellA[i10], tx), Mathf.Lerp(cellA[i01], cellA[i11], tx), ty);

            int idx = y * w + x;
            if (cov <= 0.02f)
            {
                pixels[idx] = new Color32(0, 0, 0, 0);
                continue;
            }
            var mapped = MapDbz(g * 0.5f - 32f);
            pixels[idx] = new Color32(mapped.r, mapped.g, mapped.b,
                (byte)Mathf.Clamp(Mathf.RoundToInt(mapped.a * cov), 0, 255));
        }
    }
    return pixels;
}
```

### 3. 边界格点取样（跨瓦片接缝修复的核心）

插值网格出界的格点从 8 邻瓦片 raw 数据（`RawMargins`）取对应块心值；
邻居未下载时逐级回退（对角 → 水平/垂直 → 自身 clamp）：

```csharp
static byte SampleCell(byte[] raw, int w, int h, int b, int cw, int ch, RawMargins m, int ex, int ey)
{
    bool cxOut = ex < 0 || ex >= cw;
    bool cyOut = ey < 0 || ey >= ch;
    int colIn = ex < 0 ? w - b / 2 : (ex >= cw ? b / 2 : ex * b + b / 2);
    int rowIn = ey < 0 ? h - b / 2 : (ey >= ch ? b / 2 : ey * b + b / 2);
    int colCl = Mathf.Clamp(ex, 0, cw - 1) * b + b / 2;
    int rowCl = Mathf.Clamp(ey, 0, ch - 1) * b + b / 2;

    if (!cxOut && !cyOut) return raw[rowIn * w + colIn];
    if (cxOut && !cyOut)
    {
        var src = ex < 0 ? m?.West : m?.East;
        return src != null ? src[rowIn * w + colIn] : raw[rowIn * w + colCl];
    }
    if (!cxOut && cyOut)
    {
        var src = ey < 0 ? m?.South : m?.North;
        return src != null ? src[rowIn * w + colIn] : raw[rowCl * w + colIn];
    }
    var diag = ex < 0 ? (ey < 0 ? m?.SW : m?.NW) : (ey < 0 ? m?.SE : m?.NE);
    if (diag != null) return diag[rowIn * w + colIn];
    var hh = ex < 0 ? m?.West : m?.East;
    if (hh != null) return hh[rowCl * w + colIn];
    var vv = ey < 0 ? m?.South : m?.North;
    if (vv != null) return vv[rowIn * w + colCl];
    return raw[rowCl * w + colCl];
}
```

`RawMargins` 由 `MapWeatherTileLayer.CollectMargins` 填充（纹理空间方位：
North = y-1、South = y+1、West = x-1、East = x+1，含 4 个对角）；
新瓦片下载完成后调用 `RemapNeighbors` 重映射 4 个已缓存邻居，
使后到的瓦片也能修掉旧缝。

## 二、瓦片加载后的具体值（MapWeatherTileLayer.LoadTile）

```csharp
var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
if (tex.LoadImage(bytes, markNonReadable: false))
{
    tex.wrapMode = TextureWrapMode.Clamp;
    tex.filterMode = FilterMode.Bilinear;
    // ... ExtractG → RemapRaw → SetPixels32 → Apply(false, false)
}
```

| 项目 | 具体值 | 说明 |
|---|---|---|
| 初始尺寸 | 2×2（占位） | `LoadImage` 后被 PNG 原生尺寸覆盖 |
| **加载后尺寸** | **256×256** | RainViewer URL 固定 `/256/…`，PNG 原生 256；重映射 `SetPixels32` 不改变尺寸 |
| 格式 | RGBA32，无 mipmap，**可读**（`markNonReadable: false`） | 重映射与邻居重映射都需要可读 |
| **filterMode** | **`FilterMode.Bilinear`** | wrapMode = Clamp |
| Atlas 单元 `tp` | `Clamp(Round(_tileW × max(_lastPx, 1)), 64, 512)`，再被 `2048/cols`、`2048/rows` 收紧 | 全屏 z5 时约 256；放大后最高 512 |
| Atlas 总尺寸 | `cols×tp × rows×tp`，上限 **2048×2048**，ARGB32，**filterMode = Bilinear**，wrap = Clamp | 见 `EnsureAtlas` |
| `Apply` 调用 | `Apply(false, false)` | 不更 mipmap、保持可读 |

即：每块瓦片最终是 **256×256、Bilinear** 的调色板贴图；
z5 时 32px 块被插值成 256 内的连续渐变（每块 8×8 像素渐变区），
z7 时 64px 块 → 4×4 格点渐变。屏幕上再经 atlas（Bilinear）二次采样。

## 三、精细度瓶颈说明

精细度瓶颈不在 filterMode / 纹理尺寸（均已是 Bilinear + 原生 256），
而在**源数据固定约 70km/格**——256×256 纹理里绝大部分是插值填充。

### 已实测：z≥8 有瓦片，但不能更精细

华东 / 东京 / 香港三地 raw 瓦片（2026-09-03）一致表明：

| z | HTTP | 块边长 | 地面网格 |
|---|---|---|---|
| 5 | 200 | 16px | ~70km |
| 7 | 200 | 64px | ~70km |
| 8 | 200 | 128px | ~70km |
| 9 | 200 | 256px | ~70km |

结论：**不要把 `MaxZ` 提到 8+**——瓦片数翻倍、CDN 压力翻倍，观感只会更糊（每瓦片只剩 2×2 / 1×1 格点）。
要真正更精细，只能换更高分辨率数据源（付费 API / 其他雷达产品）。

## 相关代码

- `Assets/Scripts/UI/RadarDbzMapper.cs`
- `Assets/Scripts/UI/MapWeatherTileLayer.cs`
