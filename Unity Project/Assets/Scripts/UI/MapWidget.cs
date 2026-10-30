using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// n6 主页「地图舞台」微件——对齐飞常准地图：
    /// 两种底图样式（卫星/默认）+ Web Mercator 投影
    /// + 晨昏夜幕 + 气象云图/台风路径 + 机场圆点（大小∝客流、颜色∝拥堵）
    /// + 机场城市/机场名标注 + 发光航带 + 航班。
    /// </summary>
    public static class MapWidget
    {
        // 逻辑画布：Web Mercator 正方形世界（对齐 Leaflet/ArcGIS 瓦片），三联横向循环。
        public const float MapW = 2048f;
        public const float MapH = 2048f;

        // ── 调色 ──
        static readonly Color RouteLineCol = new Color(0.38f, 0.10f, 0.52f, 1f);     // 深紫色细航线
        /// <summary>屏幕像素，不随缩放变粗（基准线宽，密集航线在此基础上加粗）。</summary>
        const float RouteScreenThickness = 2.8f;
        /// <summary>
        /// ★T03.8：线宽随班次密度变化。
        ///
        /// 旧版所有航线一律 2.4 px，于是「天天飞的干线」和「一周两班的支线」在地图上长得一模一样，
        /// 地图看不出运力分布 —— 而地图本来就是主视觉。这里按**周离港班次**分三档：
        /// ≤7 班细（1.8）、8–20 班基准（2.8）、>20 班粗（4.0）。
        ///
        /// **三档必须拉开到 ≥1 px**，这是 2026-10-27 上机实测后的修正：
        /// 首版是 2.0 / 2.4 / 3.0，相邻只差 0.4 / 0.6 px —— 比一个像素还小，
        /// 玩家在 1920 屏上完全看不出差别（用户原话「线条粗细一样」），功能等于没做。
        /// 缩放到最大也救不了：<see cref="MapTrackZoom"/> 除以视口缩放，
        /// 屏幕厚度恒等于这里的返回值，放大只会让线整体变清楚、不会放大档位差。
        ///
        /// 档位边界（7 / 20）按运力分布定，不要为了「更平滑」改成连续函数 ——
        /// 连续映射在缩放时抖动，而这里的取值是屏幕像素常量。
        /// </summary>
        public static float RouteThickness(RouteData r)
        {
            if (r == null) return RouteScreenThickness;
            int w = Mathf.Max(0, r.WeeklyFlightsDeparture());
            if (w <= 7) return 1.8f;
            if (w <= 20) return RouteScreenThickness;
            return 4.0f;
        }
        static readonly Color HubCoreCol = new Color(0.10f, 0.42f, 1f, 1f);          // 主基地
        static readonly Color PlaneCol = new Color(1f, 1f, 1f, 0.98f);

        // 机场经纬度表（IATA → lon/lat，WGS-84，4~5 位小数）。
        // 旧版 0.1° 精度（约 10km）在矢量底图上会导致机场圆点/标注明显偏离跑道，
        // 换高德矢量图后尤其暴露（如深圳宝安纬度 22.6 实为 22.639，差 4.3km）。
        // 经纬度表已抽到 AirportGeoCatalog（Unity 无关，供地图与经济两侧共用同一口径）。
        static readonly Dictionary<string, Vector2> _geo = new Dictionary<string, Vector2>();
        static bool _geoBuilt;
        static readonly List<GameObject> _roots = new List<GameObject>();

        static Sprite _radialSprite;
        static Material _glowMat;
        static string _lastSig = string.Empty;
        static float _lastLivePlaneAt = -10f;

        /// <summary>
        /// 航线图层是否可见。**默认关**——★2026-10-30 CEO 要求地图默认只有飞机在动，
        /// 点了「航线」按钮才画航线（对齐 Airline Sim 的地图：航线是可按需打开的图层，不是常驻底图）。
        /// 默认关还有一个实际好处：开局的航线网络还没铺开时，满屏弧线会把机场和飞机压得看不清。
        /// </summary>
        public static bool RoutesVisible { get; private set; }

        static void EnsureGeo()
        {
            if (_geoBuilt) return;
            _geoBuilt = true;
            var all = AirportGeoCatalog.All;
            for (int i = 0; i < all.Length; i++)
                _geo[all[i].code] = new Vector2(all[i].lon, all[i].lat);
        }

        /// <summary>Web Mercator 投影：高德底图（GCJ-02）先做 WGS-84→GCJ-02 纠偏再投影；Google 底图（WGS-84）直接投影。</summary>
        public static Vector2 Project(float lon, float lat)
        {
            // 底图为高德（GCJ-02）时，矢量数据为 WGS-84：先纠偏到 GCJ-02 再投影，
            // 否则机场/航线/晨昏线会整体偏离底图（国内约 50~700m，放大后明显）。
            // Google 底图是 WGS-84 不偏移，绕过 GCJ 直接投影。
            if (MapTileLayer.IsGcjSource)
            {
                CoordConverter.Wgs84ToGcj02(lon, lat, out double gLon, out double gLat);
                lat = (float)gLat;
                lon = (float)gLon;
            }
            lat = Mathf.Clamp(lat, -85.05112878f, 85.05112878f);
            float x = (lon + 180f) / 360f * MapW - MapW * 0.5f;
            float latR = lat * Mathf.Deg2Rad;
            float mercN = Mathf.Log(Mathf.Tan(Mathf.PI * 0.25f + latR * 0.5f));
            float y = mercN / Mathf.PI * MapH * 0.5f; // 中心为 0，北正
            return new Vector2(x, y);
        }

        /// <summary>由 Map 局部 Y（Mercator）反算纬度，与 <see cref="Project"/> 互逆。</summary>
        public static float InverseMercatorLat(float mapY)
        {
            float mercN = mapY / (MapH * 0.5f) * Mathf.PI;
            return (2f * Mathf.Atan(Mathf.Exp(mercN)) - Mathf.PI * 0.5f) * Mathf.Rad2Deg;
        }

        static bool TryPoint(string code, out Vector2 p)
        {
            EnsureGeo();
            if (!string.IsNullOrEmpty(code) && _geo.TryGetValue(code.ToUpperInvariant(), out var ll))
            {
                p = Project(ll.x, ll.y);
                return true;
            }
            p = default;
            return false;
        }

        public static bool TryLonLat(string code, out float lon, out float lat)
            => AirportGeoCatalog.TryLonLat(code, out lon, out lat);

        public static float GreatCircleKm(string origin, string dest)
            => AirportGeoCatalog.GreatCircleKm(origin, dest);

        /// <summary>在所有已构建且激活的地图上定位某机场（IATA 码）；找不到返回 false。</summary>
        public static bool FocusAirport(string iata)
        {
            if (!TryPoint(iata, out var p)) return false;
            bool hit = false;
            for (int i = 0; i < _roots.Count; i++)
            {
                var v = _roots[i];
                if (v == null) { _roots.RemoveAt(i); i--; continue; }
                if (!v.activeInHierarchy) continue;
                var vp = v.GetComponent<MapViewport>();
                if (vp != null) { vp.CenterOnMap(p); hit = true; }
            }
            return hit;
        }

        /// <summary>在父级下构建地图微件：三联瓦片（左/中/右各一份世界）+ 视口控制（cover 铺满、拖拽、缩放、横向循环）。</summary>
        public static GameObject Build(Transform parent)
        {
            EnsureGeo();
            var root = new GameObject("MapWidget", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MapView));
            root.transform.SetParent(parent, false);
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            var rootImg = root.GetComponent<Image>();
            rootImg.color = OceanColor();
            rootImg.raycastTarget = false;

            // world = 三个并排的"世界瓦片"容器，由 MapViewport 驱动平移/缩放/回绕
            var world = new GameObject("World", typeof(RectTransform));
            world.transform.SetParent(root.transform, false);
            var worldRt = world.GetComponent<RectTransform>();
            worldRt.anchorMin = new Vector2(0.5f, 0.5f);
            worldRt.anchorMax = new Vector2(0.5f, 0.5f);
            worldRt.pivot = new Vector2(0.5f, 0.5f);
            worldRt.sizeDelta = new Vector2(MapW, MapH);

            // Tile = 仅底图；Terminator / Weather / Dynamic 全部挂在 Tile 之后，
            // 避免后画的 Tile 盖住先画 Tile 上的晨昏线/台风/机场名，也避免夜幕在日界线接缝处形成「深色方块」。
            for (int i = -1; i <= 1; i++)
            {
                var tile = new GameObject("Tile_" + i, typeof(RectTransform));
                tile.transform.SetParent(world.transform, false);
                var trt = tile.GetComponent<RectTransform>();
                trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
                trt.sizeDelta = new Vector2(MapW + 2f, MapH);
                trt.anchoredPosition = new Vector2(i * MapW, 0f);
                BuildEarth(tile.transform);
            }

            for (int i = -1; i <= 1; i++)
            {
                var host = new GameObject("Terminator_" + i, typeof(RectTransform));
                host.transform.SetParent(world.transform, false);
                var hrt = host.GetComponent<RectTransform>();
                hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
                hrt.sizeDelta = new Vector2(MapW, MapH);
                hrt.anchoredPosition = new Vector2(i * MapW, 0f);
                BuildTerminator(host.transform);
            }

            for (int i = -1; i <= 1; i++)
            {
                var host = new GameObject("Weather_" + i, typeof(RectTransform));
                host.transform.SetParent(world.transform, false);
                var hrt = host.GetComponent<RectTransform>();
                hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
                hrt.sizeDelta = new Vector2(MapW, MapH);
                hrt.anchoredPosition = new Vector2(i * MapW, 0f);
                BuildWeather(host.transform);
            }

            for (int i = -1; i <= 1; i++)
            {
                var host = new GameObject("Dynamic_" + i, typeof(RectTransform));
                host.transform.SetParent(world.transform, false);
                var hrt = host.GetComponent<RectTransform>();
                hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
                hrt.sizeDelta = new Vector2(MapW, MapH);
                hrt.anchoredPosition = new Vector2(i * MapW, 0f);
                RebuildDynamic(host, force: true);
            }

            root.AddComponent<MapViewport>().world = worldRt;
            root.AddComponent<MapDragEvents>();
            root.GetComponent<MapViewport>().ForceApply();

            _roots.Add(root);
            return root;
        }

        /// <summary>刷新地图：三份 Dynamic 叠加层全部重建（签名未变则跳过）。</summary>
        public static void Refresh()
        {
            var gs = GameState.Instance;
            string sig = Signature(gs);
            if (sig == _lastSig) return;
            _lastSig = sig;

            for (int i = _roots.Count - 1; i >= 0; i--)
            {
                var v = _roots[i];
                if (v == null) { _roots.RemoveAt(i); continue; }
                if (!v.activeInHierarchy) continue;
                var world = v.transform.Find("World");
                if (world == null) continue;
                for (int t = 0; t < world.childCount; t++)
                {
                    var child = world.GetChild(t).gameObject;
                    if (child.name.StartsWith("Dynamic_", System.StringComparison.Ordinal))
                        RebuildDynamic(child, force: true);
                }
                v.GetComponent<MapViewport>()?.ForceApply();
            }
        }

        static string Signature(GameState gs)
        {
            if (gs == null) return "none";
            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int fleet = gs.fleet != null ? gs.fleet.Count : 0;
            // 机型直接决定飞机图标的形状，必须进签名：换机后机队条数不变，
            // 只按「航线数|机队数」判会认为「什么都没变」，地图上继续画旧机型，
            // 一直等到下一次时钟刷新（1× 下最长约一分钟）才换过来 —— 那时玩家早以为换机失败了。
            var sb = new System.Text.StringBuilder(64);
            sb.Append(gs.hubIata).Append('|').Append(routes).Append('|').Append(fleet);
            sb.Append('|').Append(RoutesVisible ? '1' : '0');
            if (gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    sb.Append('|').Append(r.aircraftType).Append(':').Append(r.registration);
                }
            }
            return sb.ToString();
        }

        /// <summary>重建 Dynamic 叠加层（航线/机场/航班）。host 为 World 下 Dynamic_*，始终位于全部 Tile 之上。</summary>
        static void RebuildDynamic(GameObject host, bool force, string sig = null)
        {
            var old = host.transform.Find("Content");
            if (!force && old != null && sig == _lastSig) return;

            if (old != null)
                UnityEngine.Object.DestroyImmediate(old.gameObject);

            var layer = new GameObject("Content", typeof(RectTransform));
            layer.transform.SetParent(host.transform, false);
            var rt = layer.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // 顺序即层序：航线在最底（不能让弧线压住机场圆点/城市标签），飞机在最上。
            DrawRouteLayer(layer.transform);   // 默认关：见 RoutesVisible
            DrawAirports(layer.transform);
            DrawAirportLabels(layer.transform);
            DrawPlaneMarks(layer.transform);
        }

        /// <summary>
        /// 只画「可见图层」的那部分航线。RebuildDynamic 走这里，测试也走这里 ——
        /// 直接调 <see cref="DrawRoutes"/> 仍然无条件绘制（那是绘制原语，宽度/几何的自检要用它）。
        /// </summary>
        public static int DrawRouteLayer(Transform map)
            => RoutesVisible ? DrawRoutes(map) : 0;

        /// <summary>
        /// 开/关航线图层并重绘。切换要立刻可见，所以直接重建 Dynamic，不等下一次 Refresh 的签名变化。
        /// 开关状态是 static：切页回来不该被重置成「又看不见了」。
        /// </summary>
        public static void SetRoutesVisible(bool visible)
        {
            if (RoutesVisible == visible) return;
            RoutesVisible = visible;
            RebuildDynamicLayers();
        }

        public static void ToggleRoutes() => SetRoutesVisible(!RoutesVisible);

        // ── 持久层：ArcGIS World Imagery 瓦片（与 HTML Leaflet 同源，放大换更高清图块）──
        static void BuildEarth(Transform map)
        {
            // 海洋兜底色
            var ocean = new GameObject("Ocean", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ocean.transform.SetParent(map, false);
            var ort = ocean.GetComponent<RectTransform>();
            ort.anchorMin = ort.anchorMax = ort.pivot = new Vector2(0.5f, 0.5f);
            ort.sizeDelta = new Vector2(MapW + 2f, MapH);
            ort.anchoredPosition = Vector2.zero;
            var oimg = ocean.GetComponent<Image>();
            oimg.color = OceanColor();
            oimg.raycastTarget = false;

            MapTileLayer.Build(map);
        }

        /// <summary>气象叠加层（GPM IMERG 云图 + 台风路径），在晨昏线之上、动态标注之下。</summary>
        static void BuildWeather(Transform map)
        {
            MapWeatherTileLayer.Build(map);
            MapWeatherLayer.Build(map);
        }

        /// <summary>晨昏线（虚线 + 太阳/月亮图标 + 夜半球轻微压暗），对齐飞常准。</summary>
        static void BuildTerminator(Transform map)
        {
            MapTerminatorLayer.Build(map);
        }

        /// <summary>底图样式切换：重载瓦片 + 更新海洋底色 + 重建航线/机场（卫星/矢量下线条配色不同）。</summary>
        public static void SetStyle(MapStyle style)
        {
            MapTileLayer.SetStyle(style);
            ApplyOceanColor();
            RebuildDynamicLayers(); // 航线/机场标签颜色依赖 IsLightStyle，切样式需重绘
        }

        /// <summary>时钟推进时刷新空中飞机位置（节流，避免每分钟整图重建过重）。</summary>
        public static void RefreshLivePlanes()
        {
            if (Time.unscaledTime - _lastLivePlaneAt < 1.5f) return;
            _lastLivePlaneAt = Time.unscaledTime;
            RebuildDynamicLayers();
        }

        /// <summary>读档 / 进主页：晨昏线按当前游戏 UTC 重算。</summary>
        public static void RefreshTerminator()
        {
            for (int i = 0; i < _roots.Count; i++)
            {
                var root = _roots[i];
                if (root == null) continue;
                var layers = root.GetComponentsInChildren<MapTerminatorLayer>(true);
                for (int k = 0; k < layers.Length; k++)
                    if (layers[k] != null) layers[k].ApplySun(force: true);
            }
        }

        /// <summary>仅重建动态层（航线/机场/标签/航班），不触碰晨昏线与天气。</summary>
        static void RebuildDynamicLayers()
        {
            for (int i = 0; i < _roots.Count; i++)
            {
                var root = _roots[i];
                if (root == null || !root.activeInHierarchy) continue;
                var world = root.transform.Find("World");
                if (world == null) continue;
                for (int t = 0; t < world.childCount; t++)
                {
                    var child = world.GetChild(t).gameObject;
                    if (child.name.StartsWith("Dynamic_", StringComparison.Ordinal))
                        RebuildDynamic(child, force: true);
                }
            }
        }

        /// <summary>底图源切换（高德/Google）：重载瓦片 + 重新投影所有叠加层（机场/航线/晨昏线/台风/云图偏移）。</summary>
        public static void SetSource(MapSource source)
        {
            MapTileLayer.SetSource(source);
            RebuildAllOverlays();
        }

        static void RebuildAllOverlays()
        {
            for (int i = 0; i < _roots.Count; i++)
            {
                var root = _roots[i];
                if (root == null) continue;
                if (!root.activeInHierarchy) continue;
                var world = root.transform.Find("World");
                if (world == null) continue;
                for (int t = 0; t < world.childCount; t++)
                {
                    var child = world.GetChild(t).gameObject;
                    if (child.name.StartsWith("Dynamic_", StringComparison.Ordinal))
                        RebuildDynamic(child, force: true);
                    else if (child.name.StartsWith("Terminator_", StringComparison.Ordinal))
                        RebuildTerminator(child.transform);
                }
            }
            MapWeatherLayer.RefreshProjection();
            MapWeatherTileLayer.RefreshProjection();
        }

        static void RebuildTerminator(Transform host)
        {
            var old = host.Find("TerminatorLayer");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            BuildTerminator(host);
        }

        static void ApplyOceanColor()
        {
            Color c = OceanColor();
            for (int i = 0; i < _roots.Count; i++)
            {
                var root = _roots[i];
                if (root == null) continue;
                var rootImg = root.GetComponent<Image>();
                if (rootImg != null) rootImg.color = c;
                var world = root.transform.Find("World");
                if (world == null) continue;
                for (int t = 0; t < world.childCount; t++)
                {
                    var ocean = world.GetChild(t).Find("Ocean");
                    if (ocean == null) continue;
                    var img = ocean.GetComponent<Image>();
                    if (img != null) img.color = c;
                }
            }
        }

        static Color OceanColor()
        {
            return MapTileLayer.IsLightStyle
                ? new Color(0.76f, 0.80f, 0.85f, 1f)
                : new Color(0.008f, 0.031f, 0.063f, 1f);
        }

        // ── 航线：深紫色细线，线宽按屏幕像素补偿（放大缩小不改变粗细），无光晕 ──
        /// <summary>
        /// 把 `GameState.activeRoutes` 画成大圆弧线挂到 `map` 下，返回实际绘制的**线段数**。
        ///
        /// 返回值的用途只有一个：T03.8 的渲染链路回归。
        /// 「档位函数算对了」和「画出来的确实是那个宽度」是两件事 ——
        /// 前者一条 `RouteThickness` 单元断言就够，后者必须看真实创建的 RectTransform
        /// 与 `MapTrackZoom` 注册表。旧版只有前者，于是「函数对、渲染漏传」这类缺陷查不出来。
        /// 0 表示没有可画的航线（无 gs / 无航线 / 坐标缺失全被跳过）。
        /// </summary>
        public static int DrawRoutes(Transform map)
        {
            var gs = GameState.Instance;
            if (gs == null || gs.activeRoutes == null) return 0;
            if (map == null) return 0;

            int drawn = 0;
            var zoom = map.GetComponent<MapTrackZoom>();
            if (zoom == null) zoom = map.gameObject.AddComponent<MapTrackZoom>();
            zoom.Clear();

            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                if (!TryPoint(r.origin, out var a) || !TryPoint(r.destination, out var b)) continue;
                if ((a - b).sqrMagnitude < 4f) continue;

                var arc = GreatCirclePoints(r.origin, r.destination, 40);
                if (arc == null || arc.Count < 2) continue;
                // ★T03.8：线宽随班次密度（周离港班次）分档，干线更粗、支线更细
                float thickness = RouteThickness(r);
                for (int s = 0; s < arc.Count - 1; s++)
                {
                    var line = DrawLine(map, "Route_" + i + "_" + s, arc[s], arc[s + 1], RouteLineCol, thickness);
                    if (line != null) { zoom.RegisterLine(line, thickness); drawn++; }
                }
            }

            zoom.Reapply();
            return drawn;
        }

        public static Material GlowMat()
        {
            if (_glowMat == null)
            {
                var shader = Shader.Find("Hidden/AirlineCEO/UIAdditive");
                _glowMat = shader != null ? new Material(shader) : new Material(Shader.Find("UI/Default"));
            }
            return _glowMat;
        }

        /// <summary>径向渐变软圆斑（中心实、边缘透明），用于光晕/光带。</summary>
        public static Sprite RadialSprite
        {
            get
            {
                if (_radialSprite != null) return _radialSprite;
                const int S = 128;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                var cols = new Color32[S * S];
                float c = (S - 1) * 0.5f;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c; // 0..1
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a; // 平方衰减更像光
                        cols[y * S + x] = new Color(1f, 1f, 1f, a);
                    }
                tex.SetPixels32(cols);
                tex.Apply(false, true);
                _radialSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
                return _radialSprite;
            }
        }

        public static void DrawGlowDot(Transform map, string name, Vector2 p, float size, Color color, float alpha, Material mat)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(map, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = p;
            var img = go.GetComponent<Image>();
            img.sprite = RadialSprite;
            img.type = Image.Type.Simple;
            var c = color; c.a = alpha;
            img.color = c;
            img.material = mat;
            img.raycastTarget = false;
        }

        /// <summary>大圆弧插值（球面 Slerp），返回投影后的像素点列。</summary>
        static List<Vector2> GreatCirclePoints(string origin, string destination, int steps)
        {
            EnsureGeo();
            var o = _geo[origin.ToUpperInvariant()];
            var d = _geo[destination.ToUpperInvariant()];
            float lat1 = o.y * Mathf.Deg2Rad, lon1 = o.x * Mathf.Deg2Rad;
            float lat2 = d.y * Mathf.Deg2Rad, lon2 = d.x * Mathf.Deg2Rad;

            var v1 = new Vector3(Mathf.Cos(lat1) * Mathf.Cos(lon1), Mathf.Sin(lat1), Mathf.Cos(lat1) * Mathf.Sin(lon1));
            var v2 = new Vector3(Mathf.Cos(lat2) * Mathf.Cos(lon2), Mathf.Sin(lat2), Mathf.Cos(lat2) * Mathf.Sin(lon2));
            float dot = Mathf.Clamp(Vector3.Dot(v1, v2), -1f, 1f);
            float omega = Mathf.Acos(dot);

            var pts = new List<Vector2>(steps + 1);
            if (omega < 1e-4f)
            {
                pts.Add(Project(o.x, o.y));
                pts.Add(Project(d.x, d.y));
                return pts;
            }

            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float s1 = Mathf.Sin((1f - t) * omega) / Mathf.Sin(omega);
                float s2 = Mathf.Sin(t * omega) / Mathf.Sin(omega);
                var v = v1 * s1 + v2 * s2;
                float lat = Mathf.Asin(Mathf.Clamp(v.y, -1f, 1f)) * Mathf.Rad2Deg;
                float lon = Mathf.Atan2(v.z, v.x) * Mathf.Rad2Deg;
                pts.Add(Project(lon, lat));
            }

            // 解缠经度：跨日界线（±180°）的航段（如深圳→洛杉矶）投影后 x 会从 +MapW/2 跳到 −MapW/2，
            // 导致横贯整图的水平断线（折点 + 空隙）。把 x 连续化后，配合三份世界副本（±MapW）即可无缝回绕。
            for (int i = 1; i < pts.Count; i++)
            {
                float x = pts[i].x;
                float dx = x - pts[i - 1].x;
                if (dx > MapW * 0.5f) x -= MapW;
                else if (dx < -MapW * 0.5f) x += MapW;
                pts[i] = new Vector2(x, pts[i].y);
            }
            return pts;
        }

        // ── 机场光点：飞常准式——圆点大小∝客流、颜色∝拥堵（蓝→红），屏幕恒定大小 ──
        static readonly Dictionary<string, float> _traffic = new Dictionary<string, float>();
        static float _maxTraffic = 0f;

        static void DrawAirports(Transform map)
        {
            var gs = GameState.Instance;
            string hub = gs != null && !string.IsNullOrEmpty(gs.hubIata) ? gs.hubIata.ToUpperInvariant() : "PEK";

            _traffic.Clear();
            CollectTraffic(gs, _traffic);
            _maxTraffic = 0f;
            foreach (var kv in _traffic) if (kv.Value > _maxTraffic) _maxTraffic = kv.Value;

            var glowMat = GlowMat();
            var airports = AirportCatalog.Airports;
            int drawn = 0;
            for (int i = 0; i < airports.Count; i++)
            {
                var info = airports[i];
                string code = info.code;
                if (string.IsNullOrEmpty(code) || !TryPoint(code, out var p)) continue;
                drawn++;
                bool isHub = string.Equals(code, hub, StringComparison.OrdinalIgnoreCase);

                float core = AirportCoreSize(code);
                float glow = Mathf.Lerp(20f, 54f, TrafficNorm(code));
                Color col = CongestionColor(Congestion(Traffic(code)));

                var marker = new GameObject(isHub ? "HubMarker_" + code : "AptMarker_" + code, typeof(RectTransform));
                marker.transform.SetParent(map, false);
                var mrt = marker.GetComponent<RectTransform>();
                mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0.5f);
                mrt.sizeDelta = Vector2.zero;
                mrt.anchoredPosition = p;
                marker.AddComponent<MapScreenConstant>();

                if (isHub)
                {
                    DrawGlowDot(marker.transform, "Glow", Vector2.zero, glow * 1.15f, new Color(0.47f, 0.78f, 1f), 0.6f, glowMat);
                    DrawGlowDot(marker.transform, "Glow2", Vector2.zero, glow * 0.6f, new Color(0.9f, 0.97f, 1f), 0.7f, glowMat);
                    DrawDot(marker.transform, "Core", Vector2.zero, HubCoreCol, core * 1.2f);
                    DrawDot(marker.transform, "Inner", Vector2.zero, new Color(0.95f, 0.98f, 1f, 1f), core * 0.45f);
                }
                else
                {
                    DrawGlowDot(marker.transform, "Glow", Vector2.zero, glow, col, 0.5f, glowMat);
                    DrawDot(marker.transform, "Core", Vector2.zero, col, core);
                    DrawDot(marker.transform, "Inner", Vector2.zero, new Color(1f, 1f, 1f, 0.85f), core * 0.4f);
                }
            }
            Debug.Log($"[MapWidget] 机场标记已建：{drawn}（最大客流 {_maxTraffic:F1}）");
        }

        static float Traffic(string code)
        {
            return _traffic.TryGetValue(code.ToUpperInvariant(), out var v) ? v : 0f;
        }

        static float TrafficNorm(string code)
        {
            if (_maxTraffic <= 0f) return 0f;
            return Mathf.Clamp01(Mathf.Log(1f + Traffic(code)) / Mathf.Log(1f + _maxTraffic));
        }

        static float AirportCoreSize(string code)
        {
            return Mathf.Lerp(6f, 24f, TrafficNorm(code));
        }

        /// <summary>拥堵指数：周班次超过容量阈值即视为拥堵（近似枢纽容量）。</summary>
        static float Congestion(float traffic)
        {
            const float cap = 22f;
            return Mathf.Clamp01((traffic - cap) / cap);
        }

        static Color CongestionColor(float congestion)
        {
            // 蓝(≈0.60) → 青 → 绿 → 黄 → 橙 → 红(0)
            float hue = Mathf.Lerp(0.60f, 0.0f, congestion);
            return Color.HSVToRGB(hue, 0.72f, 1f);
        }

        static void CollectTraffic(GameState gs, Dictionary<string, float> traffic)
        {
            void Add(string code, float f)
            {
                if (string.IsNullOrEmpty(code)) return;
                string c = code.ToUpperInvariant();
                traffic.TryGetValue(c, out var v);
                traffic[c] = v + f;
            }

            if (gs != null && gs.activeRoutes != null)
            {
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null) continue;
                    float w = RouteWeekly(r);
                    Add(r.origin, w);
                    Add(r.destination, w);
                }
            }

            if (gs != null && gs.aiAirlines != null)
            {
                for (int i = 0; i < gs.aiAirlines.Count; i++)
                {
                    var ai = gs.aiAirlines[i];
                    if (ai == null || !ai.isAlive || ai.routes == null) continue;
                    float freq = Mathf.Clamp(ai.weeklyFreqOnOverlap, 1f, 28f);
                    for (int k = 0; k < ai.routes.Length; k++)
                    {
                        var parts = ai.routes[k].Split('-');
                        if (parts.Length >= 2) { Add(parts[0], freq); Add(parts[1], freq); }
                    }
                }
            }
        }

        static float RouteWeekly(RouteData r)
        {
            if (r.flights != null && r.flights.Count > 0)
            {
                float sum = 0f;
                for (int i = 0; i < r.flights.Count; i++)
                    sum += Mathf.Max(0f, r.flights[i].weeklyFlights);
                return sum;
            }
            return Mathf.Max(1f, r.weeklyFlights);
        }

        /// <summary>
        /// 机场列表标注：缩小显示城市名（同城去重），放大显示机场名；不显示 IATA。
        /// </summary>
        static void DrawAirportLabels(Transform map)
        {
            var seenCity = new HashSet<string>();
            var airports = AirportCatalog.Airports;
            int drawn = 0, primary = 0;
            for (int i = 0; i < airports.Count; i++)
            {
                var info = airports[i];
                if (string.IsNullOrEmpty(info.code) || !TryPoint(info.code, out var p)) continue;

                string aptName = string.IsNullOrEmpty(info.nameZh) ? info.code : info.nameZh;
                string city = CityOf(info.code, info.nameZh, info.nameEn);
                if (string.IsNullOrEmpty(city)) city = aptName;

                bool cityPrimary = seenCity.Add(city);

                var root = new GameObject("AptLabel_" + info.code, typeof(RectTransform));
                root.transform.SetParent(map, false);
                var rt = root.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = Vector2.zero;
                rt.anchoredPosition = p;
                root.AddComponent<MapScreenConstant>();

                float offset = -(AirportCoreSize(info.code) * 0.5f + 9f);
                var label = DrawLabel(root.transform, "Text", new Vector2(0f, offset), city, 10);
                var chip = label.transform.parent;
                var chipImg = chip != null ? chip.GetComponent<Image>() : null;
                if (chipImg != null) chipImg.raycastTarget = true;
                label.raycastTarget = true;
                if (chip != null)
                {
                    var click = chip.gameObject.GetComponent<MapAirportLabelClick>();
                    if (click == null) click = chip.gameObject.AddComponent<MapAirportLabelClick>();
                    click.Bind(info.code);
                }
                var zoomLabel = root.AddComponent<MapAirportZoomLabel>();
                zoomLabel.Bind(label, city, aptName, cityPrimary);
                drawn++;
                if (cityPrimary) primary++;
            }
            Debug.Log($"[MapWidget] 机场标签已建：{drawn}（主城市 {primary}）");
        }

        /// <summary>城市短名：优先取城市经济目录（PEK/PKX 自动合并为一个「北京」），否则从机场名提炼。</summary>
        static string CityOf(string code, string nameZh, string nameEn)
        {
            var eco = CityEconomyCatalog.Find(code);
            if (eco != null && !string.IsNullOrEmpty(eco.cityZh)) return eco.cityZh;

            if (!string.IsNullOrEmpty(nameZh))
            {
                string[] cities =
                {
                    "乌鲁木齐", "哈尔滨", "石家庄", "呼和浩特", "西双版纳", "布宜诺斯艾利斯",
                    "约翰内斯堡", "阿姆斯特丹", "伊斯坦布尔", "巴塞罗那", "哥本哈根", "斯德哥尔摩",
                    "法兰克福", "赫尔辛基", "洛杉矶", "旧金山", "亚特兰大", "芝加哥",
                    "北京", "上海", "广州", "深圳", "成都", "重庆", "西安", "昆明",
                    "杭州", "南京", "武汉", "长沙", "厦门", "福州", "青岛", "天津",
                    "大连", "沈阳", "兰州", "三亚", "海口", "香港", "澳门", "台北",
                    "首尔", "东京", "大阪", "新加坡", "曼谷", "吉隆坡", "德里", "孟买",
                    "雅加达", "马尼拉", "伦敦", "巴黎", "马德里", "罗马", "慕尼黑", "苏黎世",
                    "维也纳", "莫斯科", "纽约", "西雅图", "温哥华", "多伦多", "圣保罗",
                    "开罗", "悉尼", "墨尔本", "奥克兰", "迪拜", "多哈",
                };
                for (int i = 0; i < cities.Length; i++)
                    if (nameZh.StartsWith(cities[i], StringComparison.Ordinal))
                        return cities[i];
                return nameZh;
            }
            if (!string.IsNullOrEmpty(nameEn))
            {
                int sp = nameEn.IndexOf(' ');
                return sp > 0 ? nameEn.Substring(0, sp) : nameEn;
            }
            return null;
        }

        // ── 航班图标：对照本地时刻；空中沿航迹，落地停到达场，未起飞停出发场 ──
        // ★2026-10-30：图标按**执飞的机型**画（剪影族 + 尺寸），不再是所有航班共用一张白三角。
        public static void DrawPlaneMarks(Transform map)
        {
            var gs = GameState.Instance;
            if (gs == null || gs.activeRoutes == null) return;

            DateTime localNow = gs.SimUtc + GameState.LocalUtcOffset;
            var glowMat = GlowMat();
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null || r.schedulePending) continue;
                r.EnsureFlights();
                if (!TryPoint(r.origin, out var a) || !TryPoint(r.destination, out var b)) continue;
                if ((a - b).sqrMagnitude < 4f) continue;

                var arc = GreatCirclePoints(r.origin, r.destination, 24);
                // 无机可派：不画「幽灵航班」——飞机停在出发场，不做空中动画
                // （判据与 LiveClockSimulator 同一处，见 FlightGroundLogic）。
                bool grounded = FlightGroundLogic.IsPersistent(FlightGroundLogic.RouteOf(gs, r));
                if (grounded)
                {
                    PlacePlaneOnArc(map, arc, 0f, false, glowMat, TypeOf(gs, r, null));
                    continue;
                }

                int drawn = 0;
                for (int f = 0; f < r.flights.Count; f++)
                {
                    var fl = r.flights[f];
                    if (fl == null) continue;
                    float frac = LiveClockLogic.MapRouteFrac(localNow, fl.daysOfWeek, fl.weeklyFlights,
                        fl.depTime, fl.arrTime, r.distanceKm, r.aircraftType, r.groundStaffAssigned, out bool inAir);
                    PlacePlaneOnArc(map, arc, frac, inAir, glowMat, TypeOf(gs, r, fl));
                    drawn++;
                }
                if (drawn == 0)
                    PlacePlaneOnArc(map, arc, 0.12f, false, glowMat, TypeOf(gs, r, null));
            }
        }

        /// <summary>
        /// 这一班到底由什么机型飞。**先看机号对应的机队记录，再看航线上的 aircraftType**：
        /// 航线上的 aircraftType 是「最近一次排班用的机型」，换机之后可能还没同步；
        /// 机队里的 type 才是机号的真身。两者都没有时才退回航线值（不返回空 —— 空会让图标变成默认窄体，
        /// 而「看起来像窄体」比「不画」更难发现）。
        /// </summary>
        static string TypeOf(GameState gs, RouteData r, RouteFlight fl)
        {
            string reg = fl != null && !string.IsNullOrEmpty(fl.registration) ? fl.registration : (r != null ? r.registration : null);
            if (gs != null && gs.fleet != null && !string.IsNullOrEmpty(reg))
            {
                for (int i = 0; i < gs.fleet.Count; i++)
                {
                    var ac = gs.fleet[i];
                    if (ac == null || string.IsNullOrEmpty(ac.type)) continue;
                    if (string.Equals(ac.registration, reg, StringComparison.OrdinalIgnoreCase)) return ac.type;
                }
            }
            return r != null ? r.aircraftType : null;
        }

        static void PlacePlaneOnArc(Transform map, List<Vector2> arc, float frac, bool inAir, Material glowMat, string aircraftType)
        {
            if (arc == null || arc.Count < 2) return;
            frac = Mathf.Clamp01(frac);
            float pos = frac * (arc.Count - 1);
            int idx = Mathf.Clamp(Mathf.FloorToInt(pos), 0, arc.Count - 2);
            Vector2 p = Vector2.Lerp(arc[idx], arc[idx + 1], pos - idx);
            float ang = Mathf.Atan2(arc[idx + 1].y - arc[idx].y, arc[idx + 1].x - arc[idx].x) * Mathf.Rad2Deg;
            var planeRoot = new GameObject("PlaneMark", typeof(RectTransform));
            planeRoot.transform.SetParent(map, false);
            var prt = planeRoot.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = Vector2.zero;
            prt.anchoredPosition = p;
            planeRoot.AddComponent<MapScreenConstant>();
            float size = MapAircraftIcons.SizeFor(aircraftType);
            var glow = inAir ? new Color(0.45f, 0.95f, 1f, 0.85f) : new Color(1f, 0.92f, 0.45f, 0.7f);
            DrawGlowDot(planeRoot.transform, "PlaneGlow", Vector2.zero, size * (inAir ? 1.55f : 1.2f), glow, 0.55f, glowMat);
            DrawPlane(planeRoot.transform, "Plane", Vector2.zero, ang, aircraftType, size);
        }

        // ── 绘制原语 ──
        public static RectTransform DrawDot(Transform map, string name, Vector2 p, Color color, float size)
        {
            var rt = NewChild(map, name, size, size, p);
            var img = rt.GetComponent<Image>();
            img.sprite = UIFactory.CircleSprite;
            img.type = Image.Type.Simple;
            img.color = color;
            return rt;
        }

        static TextMeshProUGUI DrawLabel(Transform map, string name, Vector2 p, string text, int size = 15)
        {
            // 深色底牌 + 白字：卫星图亮陆地也能稳定看见（TMP Outline/Underlay 在此字体上不可靠）
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(map, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = p;

            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.04f, 0.07f, 0.12f, 0.88f);
            bg.raycastTarget = false;
            if (UIFactory.RoundedSprite != null)
            {
                bg.sprite = UIFactory.RoundedSprite;
                bg.type = Image.Type.Sliced;
            }

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(7f, 2f);
            trt.offsetMax = new Vector2(-7f, -2f);

            var label = textGo.AddComponent<TextMeshProUGUI>();
            label.font = ChineseFontProvider.FontAsset;
            label.text = ChineseFontProvider.Sanitize(text);
            label.fontSize = size;
            label.color = new Color(1f, 1f, 1f, 1f);
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.ForceMeshUpdate();
            float w = Mathf.Clamp(label.preferredWidth + 16f, 36f, 220f);
            float h = Mathf.Max(label.preferredHeight + 6f, 18f);
            rt.sizeDelta = new Vector2(w, h);
            return label;
        }

        public static RectTransform DrawLine(Transform map, string name, Vector2 a, Vector2 b, Color color, float thickness)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            // 只拒绘零长度段：台风路径/航线在高倍放大时世界段长 <0.5，
            // 之前被整体拒绘导致放大后路径断续
            if (len < 1e-5f) return null;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(map, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(len, thickness);
            rt.anchoredPosition = a;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>
        /// 画一架飞机：**机型剪影**（机头朝上）+ 一圈深色描边垫底。
        ///
        /// 描边不是装饰：地图有卫星（深色）与矢量（浅色）两套底图，纯白剪影在浅色底图上翼尖会糊掉。
        /// 深色描边用「同一张图放大 1.18 倍、垫在下面、染深色」实现 —— 比 Unity 的 <c>Outline</c>
        /// 效果好得多：Outline 只沿四个方向复制顶点，斜边会出现明显锯齿。
        /// </summary>
        static void DrawPlane(Transform map, string name, Vector2 p, float angleDeg, string aircraftType, float size)
        {
            var sprite = MapAircraftIcons.SpriteFor(aircraftType);
            float rot = angleDeg - 90f;

            var edge = NewChild(map, name + "Edge", size * 1.18f, size * 1.18f, p);
            edge.localRotation = Quaternion.Euler(0f, 0f, rot);
            var edgeImg = edge.GetComponent<Image>();
            edgeImg.sprite = sprite;
            edgeImg.type = Image.Type.Simple;
            edgeImg.color = new Color(0.02f, 0.05f, 0.10f, 0.92f);

            var rt = NewChild(map, name, size, size, p);
            rt.localRotation = Quaternion.Euler(0f, 0f, rot);
            var img = rt.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.color = PlaneCol;
        }

        static RectTransform NewChild(Transform map, string name, float w, float h, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(map, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = pos;
            rt.GetComponent<Image>().raycastTarget = false;
            return rt;
        }

        // （旧的白色纸飞机贴图 EnsurePlaneSprite 已删除：2026-10-30 起按机型剪影绘制，见 MapAircraftIcons）
    }

    /// <summary>挂 MapWidget 根上：启用时刷新动态层（航线/枢纽随存档变化）。</summary>
    public class MapView : MonoBehaviour
    {
        void OnEnable() => MapWidget.Refresh();
    }

    /// <summary>
    /// 机场标注：缩小显示城市名（同城仅主机场），放大显示机场名；不显示代码。
    /// </summary>
    public class MapAirportZoomLabel : MonoBehaviour
    {
        public const float ShowAirportNameAt = 2.4f;

        TextMeshProUGUI _text;
        string _city;
        string _airport;
        bool _cityPrimary;
        float _lastZoom = -1f;

        public void Bind(TextMeshProUGUI text, string city, string airport, bool cityPrimary)
        {
            _text = text;
            _city = city ?? "";
            _airport = airport ?? "";
            _cityPrimary = cityPrimary;
            Apply(1f);
        }

        public void Apply(float userZoom)
        {
            if (_text == null) return;
            if (Mathf.Approximately(_lastZoom, userZoom)) return;
            _lastZoom = userZoom;

            bool zoomedIn = userZoom >= ShowAirportNameAt;
            if (zoomedIn)
            {
                if (!gameObject.activeSelf) gameObject.SetActive(true);
                string t = ChineseFontProvider.Sanitize(_airport);
                if (_text.text != t) _text.text = t;
                _text.fontSize = 10f;
                ResizeChip();
            }
            else
            {
                if (gameObject.activeSelf != _cityPrimary)
                    gameObject.SetActive(_cityPrimary);
                if (!_cityPrimary) return;
                string t = ChineseFontProvider.Sanitize(_city);
                if (_text.text != t) _text.text = t;
                _text.fontSize = 11f;
                ResizeChip();
            }
        }

        void ResizeChip()
        {
            if (_text == null) return;
            _text.ForceMeshUpdate();
            var chip = _text.transform.parent as RectTransform;
            if (chip == null) return;
            float w = Mathf.Clamp(_text.preferredWidth + 16f, 36f, 220f);
            float h = Mathf.Max(_text.preferredHeight + 6f, 18f);
            chip.sizeDelta = new Vector2(w, h);
        }
    }

    /// <summary>机场名称芯片点击：打开城市详情 / 选开通航线目的地。</summary>
    public class MapAirportLabelClick : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        string _iata;

        public void Bind(string iata) => _iata = iata;

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e.button != UnityEngine.EventSystems.PointerEventData.InputButton.Left) return;
            if (e.dragging) return;
            MapCityPopup.HandleAirportClick(_iata, transform);
        }
    }

    /// <summary>
    /// 屏幕恒定大小标记（对齐 Leaflet divIcon）：父级 World 放大时，本节点 localScale = 1/viewScale，
    /// 保证枢纽光点、机场标注、航班图标在屏幕上像素尺寸不变。
    /// </summary>
    public class MapScreenConstant : MonoBehaviour
    {
        float _last = -1f;

        void OnEnable()
        {
            var vp = GetComponentInParent<MapViewport>();
            if (vp != null && vp.world != null)
            {
                float baseS = 1f;
                var rt = vp.GetComponent<RectTransform>();
                if (rt != null && rt.rect.width > 1f)
                    baseS = Mathf.Max(rt.rect.width / MapWidget.MapW, rt.rect.height / MapWidget.MapH);
                float viewScale = vp.world.localScale.x;
                if (viewScale > 0.0001f) SetViewScale(viewScale);
                else if (baseS > 0.0001f) SetViewScale(baseS);

                var zoomLabel = GetComponent<MapAirportZoomLabel>();
                if (zoomLabel != null)
                {
                    float user = baseS > 0.0001f ? viewScale / baseS : 1f;
                    zoomLabel.Apply(Mathf.Max(1f, user));
                }
            }
        }

        public void SetViewScale(float viewScale)
        {
            if (viewScale < 0.0001f) viewScale = 0.0001f;
            if (Mathf.Approximately(_last, viewScale)) return;
            _last = viewScale;
            float inv = 1f / viewScale;
            transform.localScale = new Vector3(inv, inv, 1f);
        }
    }

    /// <summary>
    /// 台风路径线/点专用缩放补偿：路径端点在世界坐标（随地图缩放保持地理位置正确），
    /// 但线宽与点直径要屏幕恒定（对齐飞常准「路径始终是条线，不受缩放影响」）。
    /// 由 MapViewport.BroadcastZoom 广播 viewScale，本组件按 1/viewScale 反向调整 thickness/直径。
    /// </summary>
    public class MapTrackZoom : MonoBehaviour
    {
        readonly List<RectTransform> _lines = new List<RectTransform>();
        readonly List<float> _lineW = new List<float>();
        readonly List<RectTransform> _dots = new List<RectTransform>();
        readonly List<float> _dotD = new List<float>();
        float _last = -1f;

        void OnEnable()
        {
            var vp = GetComponentInParent<MapViewport>();
            if (vp != null && vp.world != null)
            {
                float viewScale = vp.world.localScale.x;
                if (viewScale > 0.0001f) SetViewScale(viewScale);
            }
        }

        public void RegisterLine(RectTransform rt, float screenThickness)
        {
            if (rt == null) return;
            _lines.Add(rt);
            _lineW.Add(screenThickness);
        }

        /// <summary>已注册线段数（T03.8 渲染链路回归：确认 `DrawRoutes` 真的把线挂上来了）。</summary>
        public int LineCount => _lines.Count;

        /// <summary>每条已注册线段登记的屏幕厚度，顺序与绘制顺序一致。</summary>
        public IReadOnlyList<float> LineThicknesses => _lineW;

        /// <summary>
        /// 第 `index` 条已注册线段的 RectTransform。
        /// 用于验证「登记的厚度」与「实际写进 `sizeDelta.y` 的厚度」是同一个值 ——
        /// 只查注册表的话，绘制函数把厚度丢了也照样能过。
        /// </summary>
        public RectTransform LineAt(int index)
            => index >= 0 && index < _lines.Count ? _lines[index] : null;

        public void RegisterDot(RectTransform rt, float screenDiameter)
        {
            if (rt == null) return;
            _dots.Add(rt);
            _dotD.Add(screenDiameter);
        }

        /// <summary>重建路径前清空已注册元素并重置缓存（下次 SetViewScale 会重新应用）。</summary>
        public void Clear()
        {
            _lines.Clear();
            _lineW.Clear();
            _dots.Clear();
            _dotD.Clear();
            _last = -1f;
        }

        /// <summary>路径重建后立即按当前视口缩放应用一次，保证初始线宽正确。</summary>
        public void Reapply()
        {
            var vp = GetComponentInParent<MapViewport>();
            if (vp != null && vp.world != null)
            {
                float viewScale = vp.world.localScale.x;
                if (viewScale > 0.0001f) SetViewScale(viewScale);
            }
        }

        public void SetViewScale(float viewScale)
        {
            if (viewScale < 0.0001f) viewScale = 0.0001f;
            if (Mathf.Approximately(_last, viewScale)) return;
            _last = viewScale;
            for (int i = 0; i < _lines.Count; i++)
            {
                var rt = _lines[i];
                if (rt == null) continue;
                var sd = rt.sizeDelta;
                sd.y = Mathf.Max(1.2f, _lineW[i]) / viewScale;
                rt.sizeDelta = sd;
            }
            for (int i = 0; i < _dots.Count; i++)
            {
                var rt = _dots[i];
                if (rt == null) continue;
                float d = Mathf.Max(5f, _dotD[i]) / viewScale;
                rt.sizeDelta = new Vector2(d, d);
            }
        }
    }

    /// <summary>
    /// 沿世界坐标折线画「屏幕恒定」虚线：线宽、段长、间距均按像素设定。
    /// 缩放/平移时按视口裁剪重建，只生成可见区域附近的段，
    /// 高倍放大也不会线段变长或数量爆炸（对齐飞常准晨昏线）。
    /// </summary>
    public class MapScreenDashZoom : MonoBehaviour
    {
        List<Vector2> _poly;
        Color _color = new Color(0.55f, 0.55f, 0.57f, 0.88f);
        float _screenThick = 1.8f;
        float _screenDash = 5f;
        float _screenGap = 3.5f;
        float _lastScale = -1f;
        Vector2 _lastCenter;
        bool _hasCenter;
        float _lastRebuildAt = -1f;
        float _pendingScale;

        public void Setup(List<Vector2> unwrappedPoly, Color color,
            float screenThick, float screenDash, float screenGap)
        {
            _poly = unwrappedPoly;
            _color = color;
            _screenThick = screenThick;
            _screenDash = screenDash;
            _screenGap = screenGap;
            _lastScale = -1f;
            _hasCenter = false;
            Reapply();
        }

        void OnEnable() => Reapply();

        void Update()
        {
            // 被限频跳过的缩放在下一帧补上，避免最终状态停在旧缩放
            if (_pendingScale > 0f)
            {
                float p = _pendingScale;
                _pendingScale = 0f;
                SetViewScale(p);
            }
        }

        public void Reapply()
        {
            _lastScale = -1f;
            _hasCenter = false;
            SetViewScale(CurrentViewScale());
        }

        float CurrentViewScale()
        {
            var vp = GetComponentInParent<MapViewport>();
            if (vp != null && vp.world != null && vp.world.localScale.x > 0.0001f)
                return vp.world.localScale.x;
            return 1f;
        }

        /// <summary>缩放/平移广播入口：缩放变化或视口中心移动超阈值才重建（节流）。</summary>
        public void SetViewScale(float viewScale)
        {
            // 组件或其层级已被销毁（切底图源重建晨昏线等场景）：直接跳过，避免空引用
            if (this == null || transform == null || gameObject == null) return;
            if (_poly == null || _poly.Count < 2) return;
            if (viewScale < 0.0001f) viewScale = 0.0001f;

            bool haveVis = TryGetVisibleWorldRect(out var vis, viewScale);
            if (!haveVis) return;

            float pad = vis.width * 0.5f;
            bool scaleChanged = !Mathf.Approximately(_lastScale, viewScale);
            bool moved = !_hasCenter
                || Mathf.Abs(vis.center.x - _lastCenter.x) > pad
                || Mathf.Abs(vis.center.y - _lastCenter.y) > pad;
            if (!scaleChanged && !moved) return;

            // 缩放中频繁重建会掉帧：限频 20 次/秒，滚轮连续缩放时体感跟手即可
            if (scaleChanged && _hasCenter)
            {
                float now = Time.unscaledTime;
                if (now - _lastRebuildAt < 0.05f && Mathf.Abs(_lastScale / viewScale - 1f) < 0.25f)
                {
                    // 变化幅度小且刚重建过：跳过本轮，下帧广播会再触发
                    _pendingScale = viewScale;
                    return;
                }
                _lastRebuildAt = now;
            }

            _lastScale = viewScale;
            _lastCenter = vis.center;
            _hasCenter = true;

            // 视口外扩 pad：拖动一段距离内无需重建
            vis.xMin -= pad; vis.xMax += pad;
            vis.yMin -= pad; vis.yMax += pad;
            Rebuild(viewScale, vis);
        }

        void Rebuild(float viewScale, Rect vis)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            float dashLen = Mathf.Max(1.5f, _screenDash) / viewScale;
            float gapLen = Mathf.Max(1f, _screenGap) / viewScale;
            float thick = Mathf.Max(1f, _screenThick) / viewScale;
            float period = dashLen + gapLen;
            if (period < 1e-4f) return;

            // 细分原始折线：相邻细分点间距 ≤ 周期/4，
            // 消除「采样段边界处虚线段被整段丢弃」造成的大断档（放大后段间空隙）
            var poly = SubdivideForDash(_poly, period * 0.25f);

            int id = 0;
            for (int k = -1; k <= 1; k++)
            {
                float ox = k * MapWidget.MapW;
                float dist = 0f;
                for (int i = 0; i < poly.Count - 1; i++)
                {
                    var a = new Vector2(poly[i].x + ox, poly[i].y);
                    var b = new Vector2(poly[i + 1].x + ox, poly[i + 1].y);
                    float seg = Vector2.Distance(a, b);
                    if (seg < 1e-4f) continue;

                    // 快速剔除：整段在视口外只推进距离
                    if (!SegmentMayHit(a, b, vis))
                    {
                        dist += seg;
                        continue;
                    }

                    float pos = 0f;
                    while (pos < seg - 1e-4f)
                    {
                        float along = dist + pos;
                        float phase = along % period;
                        if (phase < 0f) phase += period;
                        bool on = phase < dashLen;
                        float remainPhase = on ? (dashLen - phase) : (period - phase);
                        float step = Mathf.Min(remainPhase, seg - pos);
                        if (step < 1e-5f) break;

                        if (on && step > 0.25f * thick)
                        {
                            var p0 = Vector2.Lerp(a, b, pos / seg);
                            var p1 = Vector2.Lerp(a, b, (pos + step) / seg);
                            if (SegmentMayHit(p0, p1, vis))
                                DrawDash(transform, id++, p0, p1, _color, thick);
                        }
                        pos += step;
                    }
                    dist += seg;
                }
            }
        }

        /// <summary>把折线按最大步长细分（保形，不产生新的大间隔）。</summary>
        static List<Vector2> SubdivideForDash(List<Vector2> src, float maxStep)
        {
            if (maxStep <= 1e-4f || src == null || src.Count < 2) return src;
            var outPts = new List<Vector2>(src.Count * 2);
            for (int i = 0; i < src.Count - 1; i++)
            {
                var a = src[i];
                var b = src[i + 1];
                outPts.Add(a);
                float seg = Vector2.Distance(a, b);
                int n = Mathf.CeilToInt(seg / maxStep);
                if (n > 1)
                    for (int s = 1; s < n; s++)
                        outPts.Add(Vector2.Lerp(a, b, s / (float)n));
            }
            outPts.Add(src[src.Count - 1]);
            return outPts;
        }

        /// <summary>
        /// 画一段虚线。不走 MapWidget.DrawLine：它有 len&lt;0.5f 保护，
        /// 高倍放大时虚线段世界长度远小于 0.5，会被整体拒绘（放大后线消失的根因）。
        /// </summary>
        static RectTransform DrawDash(Transform parent, int id, Vector2 a, Vector2 b, Color color, float thick)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f || thick < 1e-6f) return null;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            var go = new GameObject("Dash_" + id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(len, thick);
            rt.anchoredPosition = a;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        bool TryGetVisibleWorldRect(out Rect r, float viewScale)
        {
            r = default;
            var vp = GetComponentInParent<MapViewport>();
            if (vp == null || vp.world == null) return false;
            var vprt = vp.GetComponent<RectTransform>();
            if (vprt == null || vprt.rect.width < 1f) return false;

            // 本虚线所在世界瓦片的横向偏移（三份世界副本各挂一份）
            float tileOx = 0f;
            var t = transform.parent;
            while (t != null && t.parent != vp.world) t = t.parent;
            if (t != null)
            {
                var trt = t as RectTransform ?? t.GetComponent<RectTransform>();
                if (trt != null) tileOx = trt.anchoredPosition.x;
            }

            float vw = vprt.rect.width / viewScale;
            float vh = vprt.rect.height / viewScale;
            Vector2 centerLocal = -vp.world.anchoredPosition / viewScale - new Vector2(tileOx, 0f);
            r = Rect.MinMaxRect(
                centerLocal.x - vw * 0.5f, centerLocal.y - vh * 0.5f,
                centerLocal.x + vw * 0.5f, centerLocal.y + vh * 0.5f);
            return true;
        }

        static bool SegmentMayHit(Vector2 a, Vector2 b, Rect r)
        {
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
            float minY = Mathf.Min(a.y, b.y), maxY = Mathf.Max(a.y, b.y);
            return maxX >= r.xMin && minX <= r.xMax && maxY >= r.yMin && minY <= r.yMax;
        }
    }

    /// <summary>
    /// 地图视口控制器：cover 缩放铺满视口、左键拖拽平移、滚轮缩放（以指针为中心）、横向无限循环。
    /// World 下三块瓦片（-1/0/+1 各一个 MapW×MapH 世界），平移量按瓦片视觉宽度取模回绕，视觉上无缝无限延伸。
    /// 拖拽/滚轮事件由同物体上的 MapDragEvents 桥转发。
    /// </summary>
    public class MapViewport : MonoBehaviour
    {
        public RectTransform world;

        public const float MinScale = 1.0f;   // 最小 = cover 刚好铺满
        // 对齐飞常准高倍放大：MaxZ=18 时约需 userScale ≈ 2^(18-4) ≈ 16k；取 4096 兼顾性能
        public const float MaxScale = 4096f;
        public const float ZoomStep = 1.35f;
        float _scale = 1f;
        Vector2 _pan;                         // 世界容器相对视口中心的平移（根画布局部像素）
        bool _dragging;
        Vector2 _dragStartPointer;
        Vector2 _dragStartPan;
        RectTransform _rt;
        Canvas _canvas;

        void Awake()
        {
            _rt = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
            var img = GetComponent<Image>();
            if (img != null) img.raycastTarget = true; // 接收拖拽/滚轮
            Apply();
        }

        void OnEnable() => Apply();

        Vector2 _lastSize;
        void Update()
        {
            // 视口尺寸变化（首帧布局/分辨率切换）时重新计算 cover 缩放
            var s = _rt.rect.size;
            if ((s - _lastSize).sqrMagnitude > 0.01f)
            {
                _lastSize = s;
                Apply();
            }
        }

        /// <summary>外部在赋值 world 后调用，立即应用一次布局。</summary>
        public void ForceApply() => Apply();

        /// <summary>由 MapDragEvents（Pointer 事件桥）调用。</summary>
        public void BeginDrag(Vector2 screenPos)
        {
            _dragging = true;
            _dragStartPointer = screenPos;
            _dragStartPan = _pan;
        }

        public void DragTo(Vector2 screenPos)
        {
            if (!_dragging) return;
            float f = _canvas != null ? _canvas.scaleFactor : 1f;
            _pan = _dragStartPan + ((Vector2)screenPos - _dragStartPointer) / Mathf.Max(0.0001f, f);
            _pan.y = ClampPanY(_pan.y);
            Apply();
        }

        public void EndDrag() => _dragging = false;

        public void ZoomAt(Vector2 screenPos, float factor)
        {
            float old = _scale;
            _scale = Mathf.Clamp(_scale * factor, MinScale, MaxScale);
            if (Mathf.Approximately(old, _scale)) return;

            // 以指针为锚点缩放：保持指针下的地理点不动
            // 根局部 = pan + 世界局部 × s；世界局部 w = (local − pan) / s_old
            Vector2 local = ScreenToLocal(screenPos);
            float sOld = ViewScale(old), sNew = ViewScale(_scale);
            Vector2 w = (local - _pan) / sOld;
            _pan = local - w * sNew;
            _pan.y = ClampPanY(_pan.y);
            Apply();
        }

        /// <summary>按钮缩放（以视口中心为锚点，屏幕坐标）。</summary>
        public void ZoomButton(float factor)
        {
            if (_rt == null) return;
            var cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, _rt.position);
            ZoomAt(screen, factor);
        }

        /// <summary>复位视图（缩放 1×、回中心）。</summary>
        public void ResetView()
        {
            _scale = 1f;
            _pan = Vector2.zero;
            Apply();
        }

        /// <summary>
        /// 定位到地图局部坐标点（与 world 子元素同一坐标系，如 MapWidget.Project 结果）。
        /// 保持当前缩放不小于 minScale（默认 6×，城市级），并把该点置于视口中心。
        /// </summary>
        public void CenterOnMap(Vector2 mapPos, float minScale = 6f)
        {
            if (_rt == null) return;
            if (_scale < minScale) _scale = minScale;
            // 视口中心局部坐标（根画布系） = pan + worldPos × viewScale
            // 求解 pan 使 mapPos 落在中心（局部 0）：
            // 0 = pan + mapPos * s  →  pan = -mapPos * s
            float s = ViewScale(_scale);
            _pan = -mapPos * s;
            _pan.y = ClampPanY(_pan.y);
            // x 由 Apply 里的 Repeat 归一化到回绕区间
            Apply();
        }

        Vector2 ScreenToLocal(Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rt, screenPos, _canvas ? _canvas.worldCamera : null, out var local);
            return local;
        }

        // cover 基准缩放：地图（2:1）铺满视口
        float BaseScale()
        {
            if (_rt == null) return 1f;
            return Mathf.Max(_rt.rect.width / MapWidget.MapW, _rt.rect.height / MapWidget.MapH);
        }

        float ViewScale(float userScale) => BaseScale() * userScale;

        float ClampPanY(float y)
        {
            float overflow = MapWidget.MapH * ViewScale(_scale) - _rt.rect.height;
            float maxPanY = Mathf.Max(0f, overflow) * 0.5f;
            return Mathf.Clamp(y, -maxPanY, maxPanY);
        }

        void Apply()
        {
            if (world == null || _rt == null) return;
            float s = ViewScale(_scale);
            world.localScale = new Vector3(s, s, 1f);
            if (s <= 0.0001f)
            {
                // 首帧布局未完成时 rect=0 → s=0，Repeat 除零会产出 NaN。
                // 直接跳过归一化与瓦片同步，等 Update 里尺寸稳定后重新 Apply。
                return;
            }
            float tileW = MapWidget.MapW * s;
            float wrapX = Mathf.Repeat(_pan.x + tileW * 0.5f, tileW) - tileW * 0.5f;
            // _pan.x 归一化到回绕区间：SyncTiles/缩放锚点等所有消费方都用有界坐标，
            // 否则往一个方向连拖几圈后 _pan 越涨越大，瓦片范围全部越界 → 视口无瓦片 → 黑屏
            if (!float.IsNaN(wrapX) && !float.IsInfinity(wrapX))
                _pan.x = wrapX;
            world.anchoredPosition = new Vector2(_pan.x, _pan.y);

            BroadcastZoom();
            SyncTiles();
            EnsureOverlayOrder();
        }

        // —— BroadcastZoom 组件缓存：拖拽期间 Apply() 每帧调用，
        //    GetComponentsInChildren 全树搜索数百子对象会掉帧；缓存后周期性失效 ——
        MapScreenConstant[] _cacheConstants;
        MapAirportZoomLabel[] _cacheLabels;
        MapTrackZoom[] _cacheTracks;
        MapScreenDashZoom[] _cacheDashes;
        float _cacheUntil;

        T[] Cached<T>(ref T[] slot, float now) where T : Component
        {
            if (slot == null || now > _cacheUntil)
            {
                slot = world.GetComponentsInChildren<T>(true);
                _cacheUntil = now + 3f; // 层级重建后最多 3s 内自愈
                return slot;
            }
            // 层级被销毁重建（切底图源 RebuildAllOverlays 销毁旧晨昏线等）后，
            // 缓存会残留已销毁组件引用：对其调 SetViewScale 会在内部
            // GetComponentInParent 抛 NullReferenceException（滚轮每帧刷屏）。
            // 发现失效条目立即整表刷新。
            for (int i = 0; i < slot.Length; i++)
                if (slot[i] == null)
                {
                    slot = world.GetComponentsInChildren<T>(true);
                    _cacheUntil = now + 3f;
                    break;
                }
            return slot;
        }

        int _overlayChildCount = -1; // 层级结构未变时跳过（避免每帧 SetSiblingIndex 脏化 Canvas）
        float _overlayOrderUntil;

        void EnsureOverlayOrder()
        {
            if (world == null) return;
            float now = Time.unscaledTime;
            if (world.childCount == _overlayChildCount && _overlayChildCount >= 0 && now < _overlayOrderUntil) return;
            _overlayChildCount = world.childCount;
            _overlayOrderUntil = now + 3f; // 周期性兜底（同帧增删导致数量不变的场景）
            var tiles = new List<Transform>();
            var terms = new List<Transform>();
            var weathers = new List<Transform>();
            var dyns = new List<Transform>();
            for (int i = 0; i < world.childCount; i++)
            {
                var c = world.GetChild(i);
                if (c.name.StartsWith("Dynamic_", StringComparison.Ordinal)) dyns.Add(c);
                else if (c.name.StartsWith("Weather_", StringComparison.Ordinal)) weathers.Add(c);
                else if (c.name.StartsWith("Terminator_", StringComparison.Ordinal)) terms.Add(c);
                else tiles.Add(c);
            }
            int idx = 0;
            for (int i = 0; i < tiles.Count; i++) tiles[i].SetSiblingIndex(idx++);
            for (int i = 0; i < terms.Count; i++) terms[i].SetSiblingIndex(idx++);
            for (int i = 0; i < weathers.Count; i++) weathers[i].SetSiblingIndex(idx++);
            for (int i = 0; i < dyns.Count; i++) dyns[i].SetSiblingIndex(idx++);
        }

        /// <summary>广播缩放：标记屏幕恒定大小 + 机场标注城市/机场名切换。</summary>
        void BroadcastZoom()
        {
            if (world == null) return;
            float viewScale = ViewScale(_scale);
            float now = Time.unscaledTime;
            var constants = Cached(ref _cacheConstants, now);
            for (int c = 0; c < constants.Length; c++)
                constants[c].SetViewScale(viewScale);

            var labels = Cached(ref _cacheLabels, now);
            for (int c = 0; c < labels.Length; c++)
                labels[c].Apply(_scale);

            var tracks = Cached(ref _cacheTracks, now);
            for (int c = 0; c < tracks.Length; c++)
                tracks[c].SetViewScale(viewScale);

            var dashes = Cached(ref _cacheDashes, now);
            for (int c = 0; c < dashes.Length; c++)
                dashes[c].SetViewScale(viewScale);
        }

        /// <summary>驱动 ArcGIS 瓦片层：放大时提高 z 级，加载更清晰图块。</summary>
        // SyncTiles 层引用缓存：避免每次滚轮/拖拽都全树 GetComponentInChildren
        MapTileLayer[] _syncTileLayers;
        MapWeatherTileLayer[] _syncWeatherLayers;
        Transform[] _syncTiles;
        float _syncCacheUntil;

        void SyncTiles()
        {
            if (world == null || _rt == null) return;
            float s = ViewScale(_scale);
            // 视口在世界坐标中的尺寸
            Vector2 viewInWorld = new Vector2(_rt.rect.width / s, _rt.rect.height / s);
            // 当前视口中心对应的世界局部（相对中心瓦片）
            Vector2 centerLocal = -_pan / s;

            if (_syncTiles == null || Time.unscaledTime > _syncCacheUntil || _syncTiles.Length != world.childCount)
            {
                _syncTiles = new Transform[world.childCount];
                _syncTileLayers = new MapTileLayer[world.childCount];
                _syncWeatherLayers = new MapWeatherTileLayer[world.childCount];
                for (int i = 0; i < world.childCount; i++)
                {
                    _syncTiles[i] = world.GetChild(i);
                    _syncTileLayers[i] = _syncTiles[i] != null
                        ? _syncTiles[i].GetComponentInChildren<MapTileLayer>() : null;
                    _syncWeatherLayers[i] = _syncTiles[i] != null
                        ? _syncTiles[i].GetComponentInChildren<MapWeatherTileLayer>(true) : null;
                }
                _syncCacheUntil = Time.unscaledTime + 3f;
            }

            for (int i = 0; i < _syncTiles.Length; i++)
            {
                var tile = _syncTiles[i];
                if (tile == null) { _syncTiles = null; break; }
                float ox = tile.GetComponent<RectTransform>().anchoredPosition.x;
                Vector2 c = centerLocal - new Vector2(ox, 0f);

                if (_syncTileLayers[i] != null) _syncTileLayers[i].SyncVisible(_scale, c, viewInWorld, s);

                var wx = _syncWeatherLayers[i];
                if (wx != null && Mathf.Abs(c.x) < viewInWorld.x * 0.5f + MapWidget.MapW * 0.6f)
                    wx.SyncVisible(_scale, c, viewInWorld, s);
            }
        }

        /// <summary>把经纬度转为世界容器下的局部像素位置（含当前缩放）——供外部定位（暂未用）。</summary>
        public Vector2 GeoToAnchored(float lon, float lat)
        {
            float s = ViewScale(_scale);
            return new Vector2((lon + 180f) / 360f * MapWidget.MapW - MapWidget.MapW * 0.5f,
                (lat + 90f) / 180f * MapWidget.MapH - MapWidget.MapH * 0.5f) * s + _pan;
        }
    }

    /// <summary>拖拽事件桥：独立小类，把 UGUI 拖拽事件转发给 MapViewport（避免 MapViewport 同时实现多接口的初始化顺序问题）。</summary>
    public class MapDragEvents : MonoBehaviour,
        UnityEngine.EventSystems.IBeginDragHandler,
        UnityEngine.EventSystems.IDragHandler,
        UnityEngine.EventSystems.IEndDragHandler,
        UnityEngine.EventSystems.IPointerClickHandler,
        UnityEngine.EventSystems.IScrollHandler
    {
        MapViewport _vp;

        void Awake() => _vp = GetComponent<MapViewport>();

        void OnEnable()
        {
            if (_vp == null) _vp = GetComponent<MapViewport>();
        }

        public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left)
                _vp?.BeginDrag(e.position);
        }

        public void OnDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left)
                _vp?.DragTo(e.position);
        }

        public void OnEndDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left)
                _vp?.EndDrag();
        }

        public void OnScroll(UnityEngine.EventSystems.PointerEventData e)
        {
            float dy = e.scrollDelta.y;
            if (Mathf.Abs(dy) > 0.001f)
                _vp?.ZoomAt(e.position, dy > 0f ? MapViewport.ZoomStep : 1f / MapViewport.ZoomStep);
        }

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e) { }
    }

    /// <summary>桥接：让外部 UI（Dashboard 缩放按钮）能找到当前地图的 MapViewport。</summary>
    public class MapWidgetBridge : MonoBehaviour
    {
        public MapViewport vp;

        void Awake()
        {
            var root = transform;
            // 桥挂在 Stage 上，MapViewport 挂在同一物体；MapWidget.Build 之后赋值
            vp = GetComponent<MapViewport>();
            if (vp == null)
            {
                var map = root.Find("MapWidget");
                if (map != null) vp = map.GetComponent<MapViewport>();
            }
        }
    }
}
