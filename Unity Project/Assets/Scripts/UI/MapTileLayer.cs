using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using AirlineCEO.Systems;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>地图样式（对齐飞常准：卫星/默认）。</summary>
    public enum MapStyle
    {
        Satellite = 0,   // 卫星影像
        Default = 1,     // 地形/街道
    }

    /// <summary>底图源：高德（GCJ-02，国内免翻墙，默认）或 Google（WGS-84，国际）。</summary>
    public enum MapSource
    {
        Gaode = 0,   // 高德瓦片（GCJ-02 火星坐标）
        Google = 1,  // Google 瓦片（WGS-84 不偏移）
    }

    /// <summary>
    /// ArcGIS 瓦片层（对齐飞常准 / Leaflet tileLayer）。
    /// 放大时按视口宽度换更高 z 级，加载更细的 {z}/{y}/{x} 图块 → 放大依旧清晰、无方块接缝。
    /// 关键切换 z 时立即清掉旧级瓦片，避免「一块一块正方形」混级拼贴。
    /// </summary>
    public class MapTileLayer : MonoBehaviour
    {
        const int MinZ = 2;
        const int MaxZ = 18;           // ArcGIS World Imagery 可达 ~19；对齐飞常准高倍放大
        // 并发上限：高德限流较严，Google 较宽松，按底图源区分（快速拖/缩放过高会触发 CDN 限流 → 瓦片失败）
        const int MaxConcurrentGaode = 10;
        const int MaxConcurrentGoogle = 16;
        const int MaxCells = 384;
        const float TilesAcross = 12f;  // 视口横向大约铺多少块瓦片（越多越清晰）

        static MapStyle _style = MapStyle.Default;
        public static MapStyle Style => _style;
        static MapSource _source = MapSource.Google;
        public static MapSource Source => _source;

        /// <summary>当前底图源是否使用 GCJ-02 坐标（高德=是，Google=否）。供投影/天气纠偏统一读取，避免三处各自硬编码。</summary>
        public static bool IsGcjSource => _source == MapSource.Gaode;

        /// <summary>当前底图源允许的最大并发下载数。</summary>
        static int MaxConcurrent => _source == MapSource.Gaode ? MaxConcurrentGaode : MaxConcurrentGoogle;

        static readonly List<MapTileLayer> _live = new List<MapTileLayer>();

        // 高德子域轮询：webrd01-04（矢量）/ webst01-04（卫星），负载均衡 + 提升并发吞吐
        static int _subTick;
        static int NextSub() { _subTick = (_subTick + 1) & 0x7FFFFFFF; return (_subTick & 3) + 1; }
        // Google 子域轮询：mt0~mt3
        static int NextGoogleSub() { _subTick = (_subTick + 1) & 0x7FFFFFFF; return _subTick & 3; }

        // ———— 境外瓦片降级状态机（仅高德源的境外瓦片）：Google 优先 → 连续失败切 ArcGIS → 定期探测恢复 ————
        // LoadTile 为主线程协程（无真并发），普通 static 字段即安全；多 layer 实例共享同一状态
        static bool _overseasArcGIS;               // true = 境外瓦片直连 ArcGIS，不再打 Google
        static int _overseasGoogleFails;           // Google 连续失败计数（任一次成功即清零）
        static float _lastOverseasProbeAt = -999f; // 上次 Google 探测时间（ArcGIS 模式下每 60s 放行一块试探）
        const int OverseasFailThreshold = 3;       // 连续失败 3 次即降级（无翻墙时 3 块瓦片超时后即切，避免首屏久等）
        const float OverseasProbeInterval = 60f;   // 降级后每 60s 用一块瓦片探测 Google 是否恢复

        // ———— Google 全源可达性探测/降级（无翻墙时避免 libcurl 红字刷屏 + 整图空白）————
        // UnityWebRequest 走 libcurl：请求不可达域名时 Unity 原生层直接打印
        // 「Curl error 28/55/16 ...」红字，C# 层无法拦截。因此用 .NET HttpClient 静默探测
        // （失败只返回 null，无任何 Unity 日志），不可达则整源降级 ArcGIS（同为 WGS-84，零偏移）。
        static int _googleState;        // 0=未知(先按 ArcGIS 出图) 1=可达(用 Google) 2=不可达(降级 ArcGIS)
        static bool _probeRunning;
        static int _pendingGoogleState; // 后台探测结果（主线程回收）：0=无 1=可达 2=不可达
        static float _lastProbeAt = -999f;
        const float GoogleProbeInterval = 60f;     // 降级后每 60s 静默复探一次，网络恢复自动切回 Google
        const int GoogleFailThreshold = 3;         // 运行中 Google 连续失败 3 次 → 判定断线降级
        static int _googleRuntimeFails;
        static HttpClient _http;
        static bool _loggedFirstOk;

        /// <summary>瓦片 URL 分派：按当前底图源返回高德或 Google 的 URL（z/x/y 编号与当前网格一致）。
        /// Google 源被探测判定不可达时整源降级 ArcGIS（WGS-84 零偏移，坐标逻辑不变）。</summary>
        static string TileUrlFor(MapStyle s, int z, int x, int y)
        {
            if (_source == MapSource.Gaode) return GaodeTileUrl(s, z, x, y);
            return GoogleTileUrl(s, z, x, y);
        }

        /// <summary>高德瓦片 URL（GCJ-02 + 标准 Web Mercator XYZ）。</summary>
        /// <remarks>
        /// 高德 appmaptile 境外只预渲染到 z≈9，z≥10 返回 ~179 字节空白占位图（实测确认）。
        /// 因此按瓦片中心经纬度分流：境内 → 高德；境外 → Google 优先（失败自动降级 ArcGIS，见 LoadTile）。
        /// 矢量数据侧因 GCJ 透传（OutOfChina 不偏移），境外天然对齐 WGS-84 瓦片，坐标逻辑零改动。
        /// </remarks>
        static string GaodeTileUrl(MapStyle s, int z, int x, int y)
        {
            // 境外瓦片：Google 优先（风格与高德最接近，拼接痕迹最小），失败降级 ArcGIS（免翻墙兜底）
            if (TileOutsideChina(z, x, y)) return OverseasTileUrl(s, z, x, y);

            int sub = NextSub();
            switch (s)
            {
                // 卫星影像
                case MapStyle.Satellite:
                    return $"https://webst0{sub}.is.autonavi.com/appmaptile?style=6&x={x}&y={y}&z={z}";
                // 默认：矢量含路网+中文标注
                default:
                    return $"https://webrd0{sub}.is.autonavi.com/appmaptile?lang=zh_cn&size=1&scale=1&style=8&x={x}&y={y}&z={z}";
            }
        }

        /// <summary>
        /// 瓦片中心是否在中国境外（含台湾，港澳算境内）。
        /// 算法同 MapWeatherTileLayer.GcjTileOffset 的瓦片中心经纬度计算。
        /// 注意：这是纯几何判断（瓦片编号 → 经纬度），不涉及任何坐标偏移。
        /// </summary>
        static bool TileOutsideChina(int z, int x, int y)
        {
            int n = 1 << z;
            double lon = ((x + 0.5) / n) * 360.0 - 180.0;
            double lat = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * (y + 0.5) / n))) * 180.0 / Math.PI;
            return CoordConverter.OutOfChina(lon, lat);
        }

        /// <summary>
        /// 境外瓦片 URL（高德源）：Google 优先（WGS-84，全球覆盖，风格与高德接近）。
        /// 样式映射与 GoogleTileUrl 一致：卫星 lyrs=s；默认 lyrs=m。
        /// Google 不可用时的兜底见 ArcGisTileUrl + LoadTile 内降级状态机。
        /// </summary>
        static string OverseasTileUrl(MapStyle s, int z, int x, int y)
        {
            int sub = NextGoogleSub();
            switch (s)
            {
                // 卫星影像
                case MapStyle.Satellite:
                    return $"https://mt{sub}.google.com/vt/lyrs=s&x={x}&y={y}&z={z}";
                // 默认：标准矢量路网 + 中文标注
                default:
                    return $"https://mt{sub}.google.com/vt/lyrs=m&hl=zh-CN&x={x}&y={y}&z={z}";
            }
        }

        /// <summary>
        /// 境外/Google 不可达时的 WGS-84 兜底：Carto Voyager（免 key）。
        /// Esri ArcGIS Online 公开 MapServer 现已对无 token 请求返回 403，不能再当兜底。
        /// URL 为标准 OSM XYZ：/{z}/{x}/{y}.png。
        /// </summary>
        static string FallbackWgsTileUrl(MapStyle s, int z, int x, int y)
        {
            char host = (char)('a' + (NextGoogleSub() & 3));
            string layer = s == MapStyle.Satellite ? "rastertiles/voyager_nolabels" : "rastertiles/voyager";
            return $"https://{host}.basemaps.cartocdn.com/{layer}/{z}/{x}/{y}.png";
        }

        static string ArcGisTileUrl(MapStyle s, int z, int x, int y) => FallbackWgsTileUrl(s, z, x, y);

        /// <summary>Google 瓦片 URL（WGS-84，公开接口无需 key；mt0~mt3 子域轮询）。</summary>
        /// <remarks>备选「卫星+路网混合」：lyrs=y（https://mt0.google.com/vt/lyrs=y&amp;x=..&amp;y=..&amp;z=..）。</remarks>
        static string GoogleTileUrl(MapStyle s, int z, int x, int y)
        {
            int sub = NextGoogleSub();
            switch (s)
            {
                // 卫星影像
                case MapStyle.Satellite:
                    return $"https://mt{sub}.google.com/vt/lyrs=s&x={x}&y={y}&z={z}";
                // 默认：标准矢量路网 + 中文标注
                default:
                    return $"https://mt{sub}.google.com/vt/lyrs=m&hl=zh-CN&x={x}&y={y}&z={z}";
            }
        }

        public static bool IsLightStyle => _style == MapStyle.Default;

        // ———— Google 可达性静默探测（.NET HttpClient，失败无 Unity 红字）————

        /// <summary>启动/换源时调用：后台静默探测 Google 瓦片域名，不可达则整源降级 ArcGIS。</summary>
        public static void ProbeGoogleAsync()
        {
            if (_source != MapSource.Google) return;
            if (_probeRunning) return;
            _probeRunning = true;
            var task = ProbeGoogleOnceAsync();
            // 后台线程只写标记；Time/ReloadAll 等 Unity 调用一律回主线程（ResyncLoop 回收）
            task.ContinueWith(t =>
            {
                _probeRunning = false;
                _pendingGoogleState = (t.Status == TaskStatus.RanToCompletion && t.Result) ? 1 : 2;
            }, TaskScheduler.Default);
        }

        static async Task<bool> ProbeGoogleOnceAsync()
        {
            try
            {
                using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(6) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
                    using (var resp = await http.GetAsync("https://mt0.google.com/vt/lyrs=m&x=0&y=0&z=0").ConfigureAwait(false))
                        return resp.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false; // 静默失败：不产生任何 Unity 日志
            }
        }

        /// <summary>主线程回收后台探测结果（ResyncLoop 每拍调用）。</summary>
        void DrainPendingProbe()
        {
            int p = _pendingGoogleState;
            if (p == 0) return;
            _pendingGoogleState = 0;
            _lastProbeAt = Time.unscaledTime;
            ApplyGoogleReachability(p == 1);
        }

        static void ApplyGoogleReachability(bool ok)
        {
            if (ok)
            {
                _googleRuntimeFails = 0;
                if (_googleState == 2)
                {
                    _googleState = 1;
                    Debug.Log("[MapTileLayer] Google 瓦片源恢复可达，切回 Google");
                    foreach (var l in _live) if (l != null) l.ReloadAll();
                }
                else _googleState = 1;
            }
            else if (_googleState != 2)
            {
                _googleState = 2;
                Debug.Log("[MapTileLayer] Google 瓦片源不可达（无翻墙/断网），整源降级 ArcGIS；每 60s 静默复探，恢复后自动切回");
                foreach (var l in _live) if (l != null) l.ReloadAll();
            }
        }

        /// <summary>运行中 Google 请求失败计数：连续达阈值 → 判定断线，整源降级 ArcGIS（不再刷红字）。</summary>
        static void NoteGoogleRuntimeFail()
        {
            if (_source != MapSource.Google || _googleState == 2) return;
            _googleRuntimeFails++;
            if (_googleRuntimeFails >= GoogleFailThreshold)
            {
                _googleRuntimeFails = 0;
                _googleState = 2;
                Debug.Log($"[MapTileLayer] Google 连续失败 {GoogleFailThreshold} 次，整源降级 ArcGIS（60s 后静默复探）");
                foreach (var l in _live) if (l != null) l.ReloadAll();
            }
        }

        static void NoteGoogleRuntimeSuccess()
        {
            _googleRuntimeFails = 0;
            if (_googleState == 0) _googleState = 1;
        }

        readonly Dictionary<long, TileCell> _cells = new Dictionary<long, TileCell>();
        readonly Queue<long> _queue = new Queue<long>();
        readonly HashSet<long> _queued = new HashSet<long>();
        int _inflight;
        Transform _grid;
        int _activeZ = -1;

        float _lastScale = -1f;
        float _lastPx;
        Vector2 _lastCenter;
        Vector2 _lastView;
        // SyncVisible 快速路径：视口未跨瓦片、z 未变、无待重试瓦片时直接返回，
        // 拖拽/缩放期间不再每帧全量重建 keep 集合与 stale 清理
        int _lastVX0 = int.MinValue, _lastVX1, _lastVY0, _lastVY1;
        float _lastFullSyncAt = -10f;
        float _zoomActiveUntil = -10f; // 缩放进行中标记：手势停止后延迟换 z 级
        const float ZoomSettleSec = 0.25f;

        class StaleTile { public GameObject go; public float time; public long key; }
        readonly List<StaleTile> _stale = new List<StaleTile>();
        readonly Dictionary<long, GameObject> _staleByKey = new Dictionary<long, GameObject>();
        const float StaleKeepSeconds = 8f; // 旧级瓦片保留 8s：换级加载完前兜底，避免露出空白块

        class TileCell
        {
            public int z, x, y;       // 用于 URL 的 wrapped 坐标
            public int vx, vy;        // 用于定位的视觉坐标（可越界，解决接缝）
            public GameObject go;
            public RawImage img;
            public bool loading, loaded;
            public float nextRetry;   // 加载失败后的重试时间
            public int attempts;
        }

        public static MapTileLayer Build(Transform tileParent)
        {
            var go = new GameObject("TileLayer", typeof(RectTransform));
            go.transform.SetParent(tileParent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(MapWidget.MapW, MapWidget.MapH);
            rt.anchoredPosition = Vector2.zero;
            var layer = go.AddComponent<MapTileLayer>();
            layer.Init();
            Debug.Log($"[MapTileLayer] 已创建，底图源={_source} 样式={_style}");
            layer.SyncVisible(1f, Vector2.zero, new Vector2(MapWidget.MapW, MapWidget.MapH));
            return layer;
        }

        void OnEnable()
        {
            if (!_live.Contains(this)) _live.Add(this);
            StartCoroutine(ResyncLoop());
        }
        void OnDisable()
        {
            _live.Remove(this);
            StopAllCoroutines();
            // StopAllCoroutines 会直接杀死在途 LoadTile 协程，协程内的 _inflight-- 不再执行 → _inflight 永久泄漏。
            // 一旦 _inflight 攒到 MaxConcurrent，Pump 的 while(_inflight < MaxConcurrent) 永远不成立，
            // 之后所有瓦片都不再发起下载 —— 表现为「完整地图能显示、放大后瓦片不载入（灰底）」。
            // 这里显式复位下载状态，并把 cell.loading 复位，让下次 OnEnable 后的 SyncVisible 能重新入队。
            _inflight = 0;
            _queue.Clear();
            _queued.Clear();
            foreach (var kv in _cells)
                if (kv.Value != null) kv.Value.loading = false;
        }
        void OnDestroy() { _live.Remove(this); }

        /// <summary>仅中心世界副本发起下载，左右副本只读缓存（3 副本同下会打爆 CDN 限流）。</summary>
        bool IsPrimaryDownloader()
        {
            var p = transform.parent as RectTransform;
            if (p == null) return true;
            return Mathf.Abs(p.anchoredPosition.x) < 1f;
        }

        /// <summary>主页被导航藏到 alpha=0 时不要继续打瓦片，否则和前台页抢并发、回主页时南半球还是灰的。</summary>
        bool IsHiddenByCanvasGroup()
        {
            var t = transform;
            while (t != null)
            {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null && cg.alpha < 0.05f) return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>每 0.5s 重发一次可见性同步：补齐加载中/失败的瓦片，即使不拖动地图也会继续填充。</summary>
        IEnumerator ResyncLoop()
        {
            // 用 unscaled 时间：暂停（Time.timeScale=0）时 WaitForSeconds 会永久停摆，导致恢复后不再补瓦片
            var wait = new WaitForSecondsRealtime(0.5f);
            while (true)
            {
                yield return wait;
                if (_lastScale > 0f && isActiveAndEnabled)
                    SyncVisible(_lastScale, _lastCenter, _lastView, _lastPx);
            }
        }

        public static void SetStyle(MapStyle s)
        {
            if (_style == s) return;
            _style = s;
            foreach (var kv in _cache)
                if (kv.Value != null) Destroy(kv.Value);
            _cache.Clear();
            for (int i = 0; i < _live.Count; i++)
                if (_live[i] != null) _live[i].ReloadAll();
        }

        /// <summary>切换底图源（高德/Google）：清纹理缓存并触发所有存活图层重载瓦片。</summary>
        public static void SetSource(MapSource s)
        {
            if (_source == s) return;
            _source = s;
            // 境外降级状态机复位：换源后旧失败计数无意义，重新从 Google 优先开始
            _overseasArcGIS = false;
            _overseasGoogleFails = 0;
            _lastOverseasProbeAt = -999f;
            foreach (var kv in _cache)
                if (kv.Value != null) Destroy(kv.Value);
            _cache.Clear();
            for (int i = 0; i < _live.Count; i++)
                if (_live[i] != null) _live[i].ReloadAll();
        }

        void Init()
        {
            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(transform, false);
            _grid = gridGo.transform;
            var grt = gridGo.GetComponent<RectTransform>();
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = new Vector2(MapWidget.MapW, MapWidget.MapH);
            grt.anchoredPosition = Vector2.zero;
        }

        void ReloadAll()
        {
            _queue.Clear();
            _queued.Clear();
            // 不重置 _inflight：在途下载的协程完成后会自行 _inflight--，
            // 强行归零会让并发额度被「双倍占用」→ 请求洪峰触发限流（瓦片失败）
            foreach (var kv in _cells)
                if (kv.Value.go != null) Destroy(kv.Value.go);
            _cells.Clear();
            foreach (var st in _stale)
                if (st != null && st.go != null) Destroy(st.go);
            _stale.Clear();
            _staleByKey.Clear();
            _activeZ = -1;
            _lastVX0 = int.MinValue; // 失效快速路径缓存
            if (_lastScale > 0f)
                SyncVisible(_lastScale, _lastCenter, _lastView, _lastPx);
        }

        /// <summary>
        /// 按视口加载可见瓦片。userScale=用户缩放；worldCenterLocal=相对本世界中心的视口中心；
        /// viewSizeInWorld=视口在世界坐标中的宽高；pxPerWorldUnit=当前每世界单位对应屏幕像素（用于恒定像素接缝补偿）。
        /// </summary>
        public void SyncVisible(float userScale, Vector2 worldCenterLocal, Vector2 viewSizeInWorld, float pxPerWorldUnit = 0f)
        {
            if (IsHiddenByCanvasGroup())
                return;

            float prevScale = _lastScale;
            Vector2 prevView = _lastView;
            _lastScale = userScale;
            _lastCenter = worldCenterLocal;
            _lastView = viewSizeInWorld;
            _lastPx = pxPerWorldUnit;

            // 检测缩放手势：滚轮连续缩放期间 scale 每帧都在变，标记「缩放中」并顺延静默截止时间
            if (!Mathf.Approximately(prevScale, userScale))
                _zoomActiveUntil = Time.unscaledTime + ZoomSettleSec;
            bool zooming = Time.unscaledTime < _zoomActiveUntil;

            // 按视口宽度选 z：视口内约 TilesAcross 块 → 放大立即换更高清图块（对齐飞常准）
            float viewW = Mathf.Max(viewSizeInWorld.x, 1f);
            int nIdeal = Mathf.Max(1, Mathf.CeilToInt(TilesAcross * MapWidget.MapW / viewW));
            int z = Mathf.Clamp(Mathf.CeilToInt(Mathf.Log(nIdeal, 2f)), MinZ, MaxZ);
            // 打开设置浮层会改内容区尺寸，视口抖动 1 级就整图换级重下 → 南半球变灰、感觉「导入变慢」
            if (_activeZ >= 0 && z != _activeZ && prevView.x > 1f)
            {
                float ratio = viewW / Mathf.Max(prevView.x, 1f);
                if (ratio > 0.86f && ratio < 1.16f && Mathf.Abs(z - _activeZ) <= 1)
                    z = _activeZ;
            }
            int n = 1 << z;
            float tileW = MapWidget.MapW / n;
            float tileH = MapWidget.MapH / n;

            // 换级时旧 z 瓦片短暂淡出（新级未加载完时兜底，同时避免新旧混拼出现明显接缝）
            if (_activeZ != z && _activeZ >= 0)
            {
                // 缩放进行中不立即换级：保持当前 z 瓦片缩放显示（世界缩放已由 MapViewport 实时应用），
                // 等手势停止、静默后再一次性换级加载。避免滚轮每 tick 都「清空旧瓦片 + 重请求」，
                // 造成瞬时请求洪峰触发 CDN 限流（403/超时）→ 瓦片下载失败 → 空白块。
                if (zooming) return;
                Debug.Log($"[MapTileLayer] 换级 z{_activeZ}→z{z}（并发槽 {_inflight}/{MaxConcurrent}，队列 {_queue.Count}）");
                foreach (var kv in _cells)
                {
                    if (kv.Value.go == null) continue;
                    if (kv.Value.loaded)
                    {
                        var img = kv.Value.go.GetComponent<RawImage>();
                        if (img != null) img.color = new Color(1f, 1f, 1f, 0.35f);
                        var st = new StaleTile { go = kv.Value.go, time = Time.unscaledTime, key = kv.Key };
                        _stale.Add(st);
                        _staleByKey[kv.Key] = kv.Value.go;
                    }
                    else
                    {
                        Destroy(kv.Value.go);
                    }
                }
                _cells.Clear();
                _queue.Clear();
                _queued.Clear();
                // 不重置 _inflight：在途旧 z 下载完成后自行 _inflight--，
                // 归零会并发超发，放大时打爆 CDN 触发限流 → 瓦片失败
            }
            _activeZ = z;

            float halfW = Mathf.Max(viewSizeInWorld.x, tileW) * 0.5f + tileW;
            float halfH = Mathf.Max(viewSizeInWorld.y, tileH) * 0.5f + tileH;

            float minX = worldCenterLocal.x - halfW + MapWidget.MapW * 0.5f;
            float maxX = worldCenterLocal.x + halfW + MapWidget.MapW * 0.5f;
            float maxY = worldCenterLocal.y + halfH;
            float minY = worldCenterLocal.y - halfH;

            // 视觉坐标（可越界），定位用；URL 用 wrap
            int vx0 = Mathf.FloorToInt(minX / tileW);
            int vx1 = Mathf.FloorToInt(maxX / tileW);
            int vy0 = Mathf.FloorToInt((MapWidget.MapH * 0.5f - maxY) / tileH);
            int vy1 = Mathf.FloorToInt((MapWidget.MapH * 0.5f - minY) / tileH);

            // 快速路径：同一 z、视口仍在同一批瓦片内且没有未载入/待重试瓦片 → 本帧无事可做。
            // 每帧（拖拽/滚轮）跳过下面的 keep 集合重建 + stale 扫描，显著降低拖拽开销；
            // 至少 0.5s 走一次全量路径，保证失败瓦片重试/网络恢复后自愈。
            if (_activeZ == z && vx0 == _lastVX0 && vx1 == _lastVX1 && vy0 == _lastVY0 && vy1 == _lastVY1
                && _queue.Count == 0 && _inflight == 0 && _stale.Count == 0
                && Time.unscaledTime - _lastFullSyncAt < 0.5f)
            {
                return;
            }
            _lastFullSyncAt = Time.unscaledTime;
            _lastVX0 = vx0; _lastVX1 = vx1; _lastVY0 = vy0; _lastVY1 = vy1;

            var keep = new HashSet<long>();
            for (int vy = vy0; vy <= vy1; vy++)
            {
                int ty = ((vy % n) + n) % n;
                for (int vx = vx0; vx <= vx1; vx++)
                {
                    int tx = ((vx % n) + n) % n;
                    long key = PlacementKey(z, vx, vy);
                    keep.Add(key);
                    EnsureCell(z, tx, ty, vx, vy, tileW, tileH);
                }
            }

            // 延迟清理过期旧级瓦片
            if (_stale.Count > 0)
            {
                float now = Time.unscaledTime;
                for (int i = _stale.Count - 1; i >= 0; i--)
                {
                    if (_stale[i] == null || _stale[i].go == null || now - _stale[i].time > StaleKeepSeconds)
                    {
                        if (_stale[i] != null && _stale[i].go != null) Destroy(_stale[i].go);
                        if (_stale[i] != null) _staleByKey.Remove(_stale[i].key);
                        _stale.RemoveAt(i);
                    }
                }
            }

            // 移出视口的瓦片不立刻销毁：进 stale 列表保留 8s，往回拖/来回拖时秒显（防黑屏）
            if (_cells.Count > 0)
            {
                var remove = new List<long>();
                foreach (var kv in _cells)
                    if (!keep.Contains(kv.Key)) remove.Add(kv.Key);
                for (int i = 0; i < remove.Count; i++)
                {
                    var c = _cells[remove[i]];
                    if (c.go != null && c.loaded)
                    {
                        _stale.Add(new StaleTile { go = c.go, time = Time.unscaledTime, key = remove[i] });
                        _staleByKey[remove[i]] = c.go;
                    }
                    else if (c.go != null)
                        Destroy(c.go);
                    _cells.Remove(remove[i]);
                    _queued.Remove(remove[i]);
                }
            }

            // stale 上限兜底：超过 240 块就销毁最老的
            if (_stale.Count > 240)
            {
                _stale.Sort((a, b) => a.time.CompareTo(b.time));
                for (int i = 0; i < _stale.Count - 240; i++)
                {
                    if (_stale[i] != null && _stale[i].go != null) Destroy(_stale[i].go);
                    _staleByKey.Remove(_stale[i].key);
                }
                _stale.RemoveRange(0, _stale.Count - 240);
            }

            // 硬上限兜底（极端宽视口）
            if (_cells.Count > MaxCells)
            {
                var extras = new List<long>();
                foreach (var kv in _cells)
                    if (!keep.Contains(kv.Key)) extras.Add(kv.Key);
                // keep 内也超了就随便砍最老的
                if (extras.Count == 0)
                {
                    foreach (var kv in _cells) { extras.Add(kv.Key); if (extras.Count > _cells.Count - MaxCells) break; }
                }
                for (int i = 0; i < extras.Count && _cells.Count > MaxCells; i++)
                {
                    if (!_cells.TryGetValue(extras[i], out var c)) continue;
                    if (c.go != null) Destroy(c.go);
                    _cells.Remove(extras[i]);
                }
            }
        }

        void ClearAllCells()
        {
            _queue.Clear();
            _queued.Clear();
            _inflight = 0;
            foreach (var kv in _cells)
                if (kv.Value.go != null) Destroy(kv.Value.go);
            _cells.Clear();
            _staleByKey.Clear();
        }

        void EnsureCell(int z, int tx, int ty, int vx, int vy, float tileW, float tileH)
        {
            long key = PlacementKey(z, vx, vy);
            bool hasLive = _cells.TryGetValue(key, out var existing) && existing.go != null;

            // 先回收同位置的 stale 瓦片（来回拖动秒显的关键）
            if (_staleByKey.TryGetValue(key, out var staleGo))
            {
                _staleByKey.Remove(key);
                for (int i = _stale.Count - 1; i >= 0; i--)
                    if (_stale[i].key == key) { _stale.RemoveAt(i); break; }
                if (staleGo != null)
                {
                    if (hasLive)
                        Destroy(staleGo); // 同位置已有活的 cell，直接销毁 stale 副本
                    else
                    {
                        var revived = staleGo.transform;
                        revived.SetParent(_grid, false);
                        revived.SetAsLastSibling();
                        var revivedImg = staleGo.GetComponent<RawImage>();
                        if (revivedImg != null) revivedImg.color = Color.white;
                        _cells[key] = new TileCell
                        {
                            z = z, x = tx, y = ty, vx = vx, vy = vy,
                            go = staleGo,
                            img = revivedImg,
                            loaded = true,
                        };
                        return;
                    }
                }
            }

            if (hasLive)
            {
                if (!existing.loaded && !existing.loading)
                {
                    // 中心副本下完写缓存后，左右副本在这里拾取（秒显，不重复下载）
                    if (TryGetCached(_source, (int)_style, existing.z, existing.x, existing.y, out var ctex))
                    {
                        if (existing.img != null)
                        {
                            existing.img.texture = ctex;
                            existing.img.color = Color.white;
                        }
                        existing.loaded = true;
                    }
                    // 已存在但未加载成功 → 到重试时间就再入队（0.5s 重同步循环会带它进来）
                    else if (Time.unscaledTime >= existing.nextRetry && IsPrimaryDownloader())
                        Enqueue(key);
                }
                return;
            }

            var go = new GameObject($"T_{z}_{vx}_{vy}", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(_grid, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            // 恒定「半像素」重叠：屏幕上永远只重叠 ~0.6px，既盖住采样缝又不产生可见错位
            float pad = _lastPx > 0.001f ? 1f / _lastPx : 0.5f;
            rt.sizeDelta = new Vector2(tileW + pad, tileH + pad);
            float cx = (vx + 0.5f) * tileW - MapWidget.MapW * 0.5f;
            float cy = MapWidget.MapH * 0.5f - (vy + 0.5f) * tileH;
            rt.anchoredPosition = new Vector2(cx, cy);

            // 占位完全透明：加载完成前露出下方的旧级瓦片/海洋兜底色，绝不露黑
            var img = go.GetComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = Texture2D.whiteTexture;
            img.color = Color.clear;

            var cell = new TileCell { z = z, x = tx, y = ty, vx = vx, vy = vy, go = go, img = img };
            _cells[key] = cell;

            if (TryGetCached(_source, (int)_style, z, tx, ty, out var tex))
            {
                img.texture = tex;
                img.color = Color.white;
                cell.loaded = true;
            }
            else if (IsPrimaryDownloader()) Enqueue(key);
            // 左右副本：不入队，等中心副本下完写缓存后由 0.5s 重同步拾取
        }

        void Enqueue(long key)
        {
            if (_queued.Contains(key)) return;
            _queued.Add(key);
            _queue.Enqueue(key);
            Pump();
        }

        void Pump()
        {
            if (!IsPrimaryDownloader()) return;
            while (_inflight < MaxConcurrent && _queue.Count > 0)
            {
                long key = _queue.Dequeue();
                if (!_cells.TryGetValue(key, out var cell) || cell.loaded || cell.loading)
                {
                    _queued.Remove(key);
                    continue;
                }
                cell.loading = true;
                _inflight++;
                StartCoroutine(LoadTile(cell, key));
            }
        }

        IEnumerator LoadTile(TileCell cell, long key)
        {
            // 记录发起时的样式/底图源：下载期间切换则只入缓存、不贴到已重建的格子上
            var styleAtStart = _style;
            var sourceAtStart = _source;

            // 高德源 + 境外瓦片：Google 优先 / ArcGIS 兜底降级链
            // viaGoogle=false 表示直接走 ArcGIS（ArcGIS 模式下非探测瓦片，或探测放行的 Google 试探失败后）
            bool overseas = sourceAtStart == MapSource.Gaode && TileOutsideChina(cell.z, cell.x, cell.y);
            bool viaGoogle = false;
            if (overseas)
            {
                if (_overseasArcGIS)
                {
                    // ArcGIS 模式：境外瓦片直连 ArcGIS；每 60s 放行一块打 Google 试探，成功则恢复 Google 优先
                    viaGoogle = Time.unscaledTime - _lastOverseasProbeAt >= OverseasProbeInterval;
                    if (viaGoogle) _lastOverseasProbeAt = Time.unscaledTime;
                }
                else
                    viaGoogle = true;
            }
            string url = overseas
                ? (viaGoogle ? OverseasTileUrl(styleAtStart, cell.z, cell.x, cell.y)
                             : ArcGisTileUrl(styleAtStart, cell.z, cell.x, cell.y))
                : TileUrlFor(styleAtStart, cell.z, cell.x, cell.y);

            if (!_loggedFirstOk)
                Debug.Log($"[MapTileLayer] 开始下载 {url}");

            bool ok = false;
            string lastError = null;
            for (int round = 0; round < 2 && !ok; round++)
            {
                if (round == 1)
                {
                    if (url != null && url.IndexOf("cartocdn.com", StringComparison.OrdinalIgnoreCase) >= 0)
                        break;
                    url = FallbackWgsTileUrl(styleAtStart, cell.z, cell.x, cell.y);
                }

                byte[] bytes = null;
                bool cartoHost = url != null && url.IndexOf("cartocdn.com", StringComparison.OrdinalIgnoreCase) >= 0;
                if (cartoHost)
                {
                    var httpTask = DownloadBytesAsync(url);
                    yield return new WaitUntil(() => httpTask.IsCompleted);
                    bytes = httpTask.Status == TaskStatus.RanToCompletion ? httpTask.Result : null;
                    if (bytes == null) lastError = "网络失败";
                }
                else
                {
                    using (var req = UnityWebRequest.Get(url))
                    {
                        req.timeout = 15;
                        req.downloadHandler = new DownloadHandlerBuffer();
                        req.SetRequestHeader("Accept", "image/png,image/jpeg,*/*;q=0.5");
                        WebRequestGuard.Track(req);
                        try { yield return req.SendWebRequest(); }
                        finally { WebRequestGuard.Untrack(req); }
                        if (req.result == UnityWebRequest.Result.Success)
                            bytes = req.downloadHandler.data;
                        else
                            lastError = $"{req.error}（{req.responseCode}）";
                    }
                }

                if (round == 0)
                {
                    if (_inflight > 0) _inflight--;
                    _queued.Remove(key);
                }

                if (bytes != null && bytes.Length > 32)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes, markNonReadable: false))
                    {
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.filterMode = FilterMode.Bilinear;
                        Cache(sourceAtStart, (int)styleAtStart, cell.z, cell.x, cell.y, tex);
                        if (cell.img != null && _style == styleAtStart && _source == sourceAtStart)
                        {
                            cell.img.texture = tex;
                            cell.img.color = Color.white;
                            cell.loaded = true;
                        }
                        ok = true;
                        if (!_loggedFirstOk)
                        {
                            _loggedFirstOk = true;
                            Debug.Log($"[MapTileLayer] 首块瓦片已贴上 z{cell.z}/{cell.x}/{cell.y} {bytes.Length}B");
                        }
                    }
                    else
                        Destroy(tex);
                }

                if (overseas && viaGoogle)
                {
                    if (ok)
                    {
                        _overseasGoogleFails = 0;
                        if (_overseasArcGIS)
                        {
                            _overseasArcGIS = false;
                            Debug.Log("[MapTileLayer] 境外瓦片源恢复 Google（探测成功）");
                        }
                    }
                    else
                    {
                        _overseasGoogleFails++;
                        if (_overseasGoogleFails >= OverseasFailThreshold && !_overseasArcGIS)
                        {
                            _overseasArcGIS = true;
                            _lastOverseasProbeAt = Time.unscaledTime;
                            Debug.Log($"[MapTileLayer] 境外瓦片源降级为 ArcGIS（Google 连续失败 {_overseasGoogleFails} 次），60s 后探测恢复");
                        }
                    }
                }

                if (!ok && lastError == null)
                    lastError = bytes == null ? "网络失败" : "空图";

                bool googleUrl = url != null && url.IndexOf("google.com", StringComparison.OrdinalIgnoreCase) >= 0;
                if (googleUrl)
                {
                    if (ok) NoteGoogleRuntimeSuccess();
                    else NoteGoogleRuntimeFail();
                }
            }
            _queued.Remove(key);

            if (!ok)
            {
                // 失败退避重试：1s → 2s → 4s → 封顶 8s（Google+ArcGIS 两轮都失败才会走到这里）
                cell.attempts++;
                cell.nextRetry = Time.unscaledTime + Mathf.Min(8f, 1f * (1 << Mathf.Min(cell.attempts, 3)));
                if (cell.attempts <= 2)
                    Debug.Log($"[MapTileLayer] 瓦片下载失败 z{cell.z}/{cell.x}/{cell.y}：{lastError}，{cell.nextRetry - Time.unscaledTime:F0}s 后重试");
            }
            cell.loading = false;
            Pump();
        }

        static HttpClient EnsureHttp()
        {
            if (_http != null) return _http;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            _http.DefaultRequestHeaders.Accept.ParseAdd("image/png,image/jpeg,image/*;q=0.8");
            return _http;
        }

        /// <summary>
        /// Domain Reload 前释放静态 HttpClient。在飞的 UnityWebRequest（非 carto 瓦片走的那条）
        /// 由 WebRequestGuard 统一中止，见 <c>WeatherTileDomainReload</c>。
        /// </summary>
        public static void ReleaseDomainResources()
        {
            try { _http?.Dispose(); } catch { /* 旧域句柄可能已失效 */ }
            _http = null;
        }

        static async Task<byte[]> DownloadBytesAsync(string url)
        {
            try
            {
                using (var resp = await EnsureHttp().GetAsync(url).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                return null;
            }
        }

        // 视觉放置位置作为 key（同一内容瓦片可在左右两侧各出现一次，消除接缝空洞）
        static long PlacementKey(int z, int vx, int vy)
        {
            // vx/vy 可能为负，偏移到正数再打包
            unchecked
            {
                uint ux = (uint)(vx + 100000);
                uint uy = (uint)(vy + 100000);
                return ((long)z << 40) | ((long)(ux & 0xFFFFF) << 20) | (uy & 0xFFFFF);
            }
        }

        static long CacheKey(MapSource source, int style, int z, int x, int y) =>
            ((long)source << 45) | ((long)style << 44) | ((long)z << 36) | ((long)x << 18) | (uint)y;

        static readonly Dictionary<long, Texture2D> _cache = new Dictionary<long, Texture2D>();
        static bool TryGetCached(MapSource source, int style, int z, int x, int y, out Texture2D tex) =>
            _cache.TryGetValue(CacheKey(source, style, z, x, y), out tex);
        static void Cache(MapSource source, int style, int z, int x, int y, Texture2D tex)
        {
            long k = CacheKey(source, style, z, x, y);
            if (_cache.ContainsKey(k)) return;
            if (_cache.Count > 1024)
            {
                var keys = new List<long>(_cache.Keys);
                int half = keys.Count / 2;
                for (int i = 0; i < half; i++)
                {
                    if (_cache.TryGetValue(keys[i], out var t) && t != null) Destroy(t);
                    _cache.Remove(keys[i]);
                }
            }
            _cache[k] = tex;
        }
    }
}
