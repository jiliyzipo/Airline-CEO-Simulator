# NASA GIBS GPM IMERG 降水云图方案

> 状态：已落地（2026-09-03）  
> 替代已废弃的 RainViewer raw dBZ 方案（存档见 `RainViewer_DBZ_Radar.md` 文末）。

## 为什么换源

RainViewer 免费 raw 瓦片实测为**固定 ~70km 粗网格的最近邻放大**（z3–z9 地面网格完全相同，
z8/z9 有瓦片但每瓦片仅 2×2 / 1×1 个真实格点），插值只是「填充」，真实信息量就这么多。
客户端已做到官网同款的全格双线性插值 + 跨瓦片边界补点，精细度仍受源数据物理上限约束。

## 新数据源：NASA GIBS GPM IMERG

| 指标 | RainViewer 免费 raw | NASA GIBS IMERG |
|---|---|---|
| 有效地面网格 | ~70km | **~10km**（细约 7 倍） |
| 覆盖 | 全球 | 全球 |
| 更新频率 | 10 分钟 | 30 分钟一帧 |
| 数据时延 | ~10 分钟 | **约 5~6 小时**（卫星估计产品，非实时雷达） |
| 接入方式 | 元数据 API + raw 解码 + 自映射调色板 | 标准 XYZ 瓦片直出，无需元数据 |
| 颜色 | raw 灰阶，需客户端映射 | 官方调色板直出（绿→黄→橙→红，与图例同风格） |

- 端点（WMS，支持 512px；WMTS 固定 256px 且模板无尺寸段，实测插 512 返回 HTTP 400）：
  `https://gibs.earthdata.nasa.gov/wms/epsg3857/nrt/wms.cgi`
- 请求：`?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=IMERG_Precipitation_Rate_30min_v7_NRT
  &CRS=EPSG:3857&BBOX={瓦片XYZ的Mercator范围}&WIDTH=512&HEIGHT=512
  &FORMAT=image/png&TRANSPARENT=true&TIME={帧}&STYLES=`
- 缩放：z0–6（原生网格 ~10km/格，实测 z6/512px 下同值块 ~19px；WMS 512 不增加真实数据，
  纯属更细的显示载体）
- 帧：UTC 半点整时间戳（如 `2026-09-03T02:00:00Z`）；实测取 **当前 UTC − 6h 取整半点** 必有数据
- 背景：WMS `TRANSPARENT=true` 原生透明，无 WMTS 的灰底；`ImergTileProcessor.Smooth`
  对 ~10km 最近邻色块做格心双线性插值（RGB+覆盖度），硬块变连续渐变

## 数据链路

1. 帧 = `UTC now − 6h` 取整半点（`RealWeatherService.RadarFrameKey`），每 30 分钟自然推进
2. `MapWeatherTileLayer`：HttpClient 请求 WMS GetMap 512px PNG（z≤6，全局限流 6 并发；
   仅中心世界副本下载）→ `LoadImage` 可读 RGBA → `ImergTileProcessor.Smooth` 色块插值 → Atlas 合成
3. 404/无覆盖按空瓦缓存；超时不入空缓存、持续重试

请求头需带 `User-Agent`；勿手动设 `Referer`（Unity 会黄警并忽略）。

## 已实测否决的「更精细」路径

- **RainViewer z≥8**：HTTP 200 但地面网格仍 ~70km，无收益，不要抬 MaxZ
- **NOAA NEXRAD（~250m）**：仅覆盖美国本土
- **Open-Meteo**：瓦片是自定义 `om://` 文件协议，需在 Unity 实现 OM 读取器，工程量大

## 归因

需标注：GPM IMERG precipitation data by [NASA GIBS](https://gibs.earthdata.nasa.gov/)。

## 显示管线

- **`ImergTileProcessor`（CPU，入缓存前）**：~10km 最近邻色块 → 格心网格 → 三连击平滑：
  1. **跨瓦片连续**：插值网格外扩 **2 格**采相邻瓦片网格的边缘格点（`_gridCache` 缓存
     各瓦片网格 ~4KB；新瓦片入库后重平滑 4 邻居，瓦片缝双向连续）；
  2. **网格五点核高斯模糊**（[1 4 6 4 1]/16 可分离、RGB 按 alpha 预乘加权、`BlurPasses`
     趟，当前 2 趟 = 等效 σ×√2）：同值平台揉成圆顶、消灭方块直线边（瓦片内部方块感
     的主因）；实测优于「仅 alpha 模糊」（RGB 平台裸露导致方块感回升）与 [1 2 1]
     三点核（过糊）；不满改回 `BlurPasses = 1`；
  3. **quintic 缓动插值**（6t⁵−15t⁴+10t³，C2 连续）：色彩梯度比 smoothstep 更柔；
  3. **smoothstep 缓动插值**（C1 连续）：格点边界折痕变圆润过渡；
- **`UIWeatherRadar.shader`（GPU，显示端）**：标准 `SrcAlpha OneMinusSrcAlpha` 直色合成 +
  Bayer 抖动（`_DitherStrength`）；
- **`AtlasBlit.shader`**：合成时纯 Alpha 混合。

> 历史尝试（已回退，2026-09-03）：显示端 Catmull-Rom 双三次、覆盖度/强度双重羽化、
> 预乘工作流、跨瓦片边缘渗透——观感劣于上述简单管线，回退保留。

## 相关代码

- `RealWeatherService.cs`（WMS URL + 帧时间）
- `MapWeatherTileLayer.cs`（下载/缓存/Atlas）
- `ImergTileProcessor.cs`（色块格心插值平滑）
