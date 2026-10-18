# NASA GIBS GPM IMERG 降水云图方案

> 状态：已落地（2026-09-03）  
> 替代已废弃的 RainViewer raw dBZ 方案（文档存档见文末）。

## 为什么换源

RainViewer 免费 raw 瓦片实测为**固定 ~70km 粗网格的最近邻放大**（z3–z9 地面网格完全相同，
z8/z9 有瓦片但每瓦片仅 2×2 / 1×1 个真实格点），插值只是「填充」，真实信息量就这么多。
客户端已做到官网同款的全格双线性插值 + 跨瓦片边界补点，精细度仍受源数据物理上限约束。

## 新数据源：NASA GIBS GPM IMERG

| 指标 | RainViewer 免费 raw | NASA GIBS IMERG |
|---|---|---|
| 有效地面网格 | ~70km | **~10km**（细约 7 倍，客户端再做插值平滑） |
| 覆盖 | 全球 | 全球 |
| 更新频率 | 10 分钟 | 30 分钟一帧 |
| 数据时延 | ~10 分钟 | **约 5~6 小时**（卫星估计产品，非实时雷达） |
| 接入方式 | 元数据 API + raw 解码 + 自映射调色板 | 标准 XYZ 瓦片直出，无需元数据 |
| 颜色 | raw 灰阶，需客户端映射 | 官方调色板直出（绿→黄→橙→红，与图例同风格） |

- 端点：`https://gibs.earthdata.nasa.gov/wmts/epsg3857/nrt/IMERG_Precipitation_Rate_30min_v7_NRT/default`
- URL 模板：`{endpoint}/{Time}/GoogleMapsCompatible_Level6/{z}/{y}/{x}.png`（REST 模板 **y 在前**）
- 缩放：z0–6（Level6，原生网格 ~10km/格；WMTS 256px 下同值块 ~9px）
- 帧：UTC 半点整时间戳（如 `2026-09-03T02:00:00Z`）；实测取 **当前 UTC − 6h 取整半点** 必有数据
- 背景处理：PNG 调色板把无降水区画成不透明浅灰（palette 0/1 ≈ RGB 128/191），
  `MakeGibsBackgroundTransparent` 把纯灰度像素转透明，只留降水回波

## 数据链路

1. 帧 = `UTC now − 6h` 取整半点（`RealWeatherService.RadarFrameKey`），每 30 分钟自然推进
2. `MapWeatherTileLayer`：HttpClient 下载 PNG（z≤6，全局限流 6 并发；仅中心世界副本下载）→
   `LoadImage` 可读 RGBA → 灰度背景转透明 → Atlas 合成
3. 404/无覆盖按空瓦缓存；超时不入空缓存、持续重试

请求头需带 `User-Agent`；勿手动设 `Referer`（Unity 会黄警并忽略）。

## 已实测否决的「更精细」路径

- **RainViewer z≥8**：HTTP 200 但地面网格仍 ~70km，无收益，不要抬 MaxZ
- **NOAA NEXRAD（~250m）**：仅覆盖美国本土
- **Open-Meteo**：瓦片是自定义 `om://` 文件协议，需在 Unity 实现 OM 读取器，工程量大

## 归因

需标注：GPM IMERG precipitation data by [NASA GIBS](https://gibs.earthdata.nasa.gov/)。

## 相关代码

- `RealWeatherService.cs`（帧时间 + 瓦片 URL）
- `MapWeatherTileLayer.cs`（下载/缓存/透明化/Atlas）

---

# 存档：RainViewer 原始 dBZ 雷达云图方案（已废弃）

## 核心思路（历史）

RainViewer `color=255` raw 瓦片把强度编码在像素里：`pixel = (dBZ + 32) × 2`（强度在 G 通道），
客户端用气象标准调色板映射。

## 数据分辨率实测（历史结论）

| z | 同值块边长（实测） | 每瓦片格点数 | 折合地面网格 |
|---|---|---|---|
| z3 | 4px | 64×64 | ~70km |
| z5 | 16px | 16×16 | ~70km |
| z7 | 64px | 4×4 | ~70km |
| z8 | 128px | 2×2 | ~70km（有瓦片，无更多细节） |
| z9 | 256px | 1×1 | ~70km |

即：原生数据约 70km/格，抬高 z 不增加真实细节；`smooth=1` 对 raw 瓦片无效。
客户端 `RemapRaw` 曾做格心采样全格双线性插值 + `RawMargins` 跨瓦片边界补点
（等效官网 smooth 渲染），已随方案移除。
