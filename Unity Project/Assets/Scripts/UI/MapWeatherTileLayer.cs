using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// NASA GIBS GPM IMERG 降水率层：WMS 512px PNG → 色块插值平滑 → Atlas 合成。
    /// ~10km 有效网格；三份世界副本共用全局限流与纹理缓存。
    /// </summary>
    public class MapWeatherTileLayer : MonoBehaviour
    {
        const int MinZ = 0;
        const int MaxZ = 6; // IMERG 原生网格上限（WMS 任意 z 均可请求，超过 6 无真实细节）
        const int MaxConcurrent = 6; // GIBS 为 NASA CDN，余量充足；仍保留全局限流
        const int MaxCells = 128;
        const int TilePx = RealWeatherService.TileSize; // WMS 512×512
        const int MaxAtlasPx = 2048;
        const int MaxCacheEntries = 96; // 512² RGBA 每块 1MB，控制缓存上限
        const float TilesAcross = 4.5f; // 同缩放更早升 z，放大后更快变清晰
        const float StaleKeepSeconds = 8f;
        const int RequestTimeoutSec = 20;
        static int _loggedUrlFails;

        static readonly List<MapWeatherTileLayer> _live = new List<MapWeatherTileLayer>();
        // 云图整体不透明度：回波核心 alpha=255 处有效不透明度即此值。
        // 参照飞常准：地形/边界需清晰可辨，回波层约 30~35% 不透明度
        static readonly Color WeatherTint = new Color(1f, 1f, 1f, 0.22f);
        static Material _weatherMat;
        static Material _atlasBlitMat;

        // 共享 HttpClient：Mono TLS 栈，实测对该 CDN 稳定（UnityWebRequest libcurl 会半截卡死）。
        // 非 readonly：Domain Reload 前必须 Dispose，否则会触发
        // 「Release of invalid GC handle. The handle is from a previous domain」。
        static HttpClient _http;

        // 帧级瓦片缓存 + 全局下载限流（跨世界副本共享）
        static string _cacheFrameKey = string.Empty;
        static readonly Dictionary<long, Texture2D> _texCache = new Dictionary<long, Texture2D>();
        // 格心网格缓存（~4KB/瓦片，全量真实信息）：供跨瓦片连续插值
        static readonly Dictionary<long, ImergTileProcessor.TileGrid> _gridCache =
            new Dictionary<long, ImergTileProcessor.TileGrid>();
        static readonly HashSet<long> _emptyCache = new HashSet<long>();
        static readonly HashSet<long> _downloadingCk = new HashSet<long>();
        // 待重平滑邻居：新瓦片入库后延迟批量重算其 4 邻，后台线程处理，避免主线程一次性阻塞
        static readonly HashSet<long> _dirtyResmooth = new HashSet<long>();
        static int _globalInflight;

        readonly Dictionary<long, TileCell> _cells = new Dictionary<long, TileCell>();
        readonly Queue<long> _queue = new Queue<long>();
        readonly HashSet<long> _queued = new HashSet<long>();
        int _activeZ = -1;
        int _atlasTilePx = TilePx;

        float _lastScale = -1f;
        float _lastPx;
        Vector2 _lastCenter;
        Vector2 _lastView;

        RawImage _display;
        RectTransform _displayRt;
        RenderTexture _atlas;
        RenderTexture _staleAtlas;
        float _staleAtlasUntil;
        bool _atlasDirty;
        float _lastAtlasBuildAt = -1f;
        bool _forceAtlasRebuild; // 新瓦片到达：跳过限频立即重建
        Coroutine _resmoothRoutine;

        int _vx0, _vx1, _vy0, _vy1;
        int _lastSyncVX0 = int.MinValue, _lastSyncVX1, _lastSyncVY0, _lastSyncVY1; // SyncVisible 快速路径
        float _tileW, _tileH;

        class TileCell
        {
            public int z, x, y, vx, vy;
            public Texture2D tex;
            public bool loading, loaded, empty;
            public float nextRetry;
            public int attempts;
        }

        public static MapWeatherTileLayer Build(Transform tileParent)
        {
            var go = new GameObject("WeatherTileLayer", typeof(RectTransform));
            go.transform.SetParent(tileParent, false);
            go.transform.SetAsFirstSibling();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(MapWidget.MapW, MapWidget.MapH);
            rt.anchoredPosition = Vector2.zero;
            var layer = go.AddComponent<MapWeatherTileLayer>();
            layer.Init();
            layer.ApplyVisible();
            return layer;
        }

        /// <summary>仅中心世界副本发起下载，左右副本只读缓存（避免 ×3 打爆 CDN）。</summary>
        bool IsPrimaryDownloader()
        {
            var p = transform.parent as RectTransform;
            if (p == null) return true;
            return Mathf.Abs(p.anchoredPosition.x) < 1f;
        }

        void OnEnable()
        {
            if (!_live.Contains(this)) _live.Add(this);
            RealWeatherService.OnChanged -= OnWeatherChanged;
            RealWeatherService.OnChanged += OnWeatherChanged;
            if (!IsInvoking(nameof(ResyncTick)))
                InvokeRepeating(nameof(ResyncTick), 0.35f, 0.35f);
            if (_resmoothRoutine == null)
                _resmoothRoutine = StartCoroutine(ResmoothDrainLoop());
        }

        void OnDisable()
        {
            CancelInvoke(nameof(ResyncTick));
            if (_resmoothRoutine != null)
            {
                StopCoroutine(_resmoothRoutine);
                _resmoothRoutine = null;
            }
        }

        void OnDestroy()
        {
            _live.Remove(this);
            RealWeatherService.OnChanged -= OnWeatherChanged;
            CancelInvoke(nameof(ResyncTick));
            ReleaseStaleAtlas();
            ReleaseAtlas();
        }

        void LateUpdate()
        {
            if (!_atlasDirty) return;
            // 拖拽/缩放期间 SyncVisible 每帧标脏，限频 15 次/秒避免每帧全量 Blit 掉帧；
            // 新瓦片到达时（_forceAtlasRebuild）立即重建，保证载入即时可见
            if (!_forceAtlasRebuild && Time.unscaledTime - _lastAtlasBuildAt < 0.067f) return;
            RebuildAtlas();
        }

        void ResyncTick()
        {
            if (RealWeatherService.RadarReady && MapWeatherLayer.Enabled && _lastScale > 0f)
                EnsureSyncedFromViewport();
        }

        void OnWeatherChanged()
        {
            ApplyVisible();
            EnsureCacheFrame();
            if (RealWeatherService.RadarReady && MapWeatherLayer.Enabled)
                ReloadAll();
        }

        void ApplyVisible()
        {
            bool on = RealWeatherService.RadarReady && MapWeatherLayer.Enabled;
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
            if (_display != null) _display.enabled = on;
        }

        public static void RefreshVisible()
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var l = _live[i];
                if (l == null) continue;
                l.ApplyVisible();
                if (l.gameObject.activeInHierarchy && l._lastScale > 0f)
                    l.SyncVisible(l._lastScale, l._lastCenter, l._lastView, l._lastPx);
            }
        }

        /// <summary>底图源切换后重建云图 atlas（GcjTileOffset 随源变化：Google 为零偏移）。</summary>
        public static void RefreshProjection()
        {
            MarkAllDirty();
        }

        void EnsureSyncedFromViewport()
        {
            var vp = GetComponentInParent<MapViewport>();
            if (vp == null || vp.world == null) return;
            var rt = vp.GetComponent<RectTransform>();
            if (rt == null || rt.rect.width < 1f) return;
            float s = vp.world.localScale.x;
            if (s < 0.0001f) return;
            Vector2 viewInWorld = new Vector2(rt.rect.width / s, rt.rect.height / s);
            Vector2 centerLocal = -vp.world.anchoredPosition / s;
            float ox = transform.parent != null
                ? transform.parent.GetComponent<RectTransform>().anchoredPosition.x
                : 0f;
            float userScale = Mathf.Max(1f, s / Mathf.Max(rt.rect.width / MapWidget.MapW, rt.rect.height / MapWidget.MapH));
            SyncVisible(userScale, centerLocal - new Vector2(ox, 0f), viewInWorld, s);
        }

        void Init()
        {
            var go = new GameObject("MergedRadar", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(transform, false);
            _displayRt = go.GetComponent<RectTransform>();
            _displayRt.anchorMin = _displayRt.anchorMax = new Vector2(0.5f, 0.5f);
            _displayRt.pivot = new Vector2(0f, 0f);
            _displayRt.anchoredPosition = Vector2.zero;
            _displayRt.sizeDelta = new Vector2(MapWidget.MapW, MapWidget.MapH);

            _display = go.GetComponent<RawImage>();
            _display.color = WeatherTint;
            _display.raycastTarget = false;
            // 初始纹理必须全透明：blackTexture 是不透明黑图，
            // 地图瓦片未加载完时会整块盖住底图（进游戏先见黑块/云图后见地图的根因）
            _display.texture = CreateTransparentTexture();
            ApplyWeatherMaterial(_display);
        }

        /// <summary>4×4 全透明占位纹理：云图未就绪前不遮挡底图。</summary>
        static Texture2D _transparentTex;
        static Texture2D CreateTransparentTexture()
        {
            if (_transparentTex != null) return _transparentTex;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var clear = new Color32[16];
            for (int i = 0; i < 16; i++) clear[i] = new Color32(0, 0, 0, 0);
            tex.SetPixels32(clear);
            tex.Apply(false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            _transparentTex = tex;
            return tex;
        }

        void ReloadAll()
        {
            EnsureCacheFrame();
            ClearAllCells();
            _activeZ = -1;
            _lastSyncVX0 = int.MinValue; // 失效快速路径缓存
            _atlasDirty = true;
            _forceAtlasRebuild = true;
            if (_lastScale > 0f)
                SyncVisible(_lastScale, _lastCenter, _lastView, _lastPx);
        }

        public void SyncVisible(float userScale, Vector2 worldCenterLocal, Vector2 viewSizeInWorld, float pxPerWorldUnit = 0f)
        {
            _lastScale = userScale;
            _lastCenter = worldCenterLocal;
            _lastView = viewSizeInWorld;
            _lastPx = pxPerWorldUnit;

            if (!RealWeatherService.RadarReady || !MapWeatherLayer.Enabled) return;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            EnsureCacheFrame();

            float viewW = Mathf.Max(viewSizeInWorld.x, 1f);
            int nIdeal = Mathf.Max(1, Mathf.CeilToInt(TilesAcross * MapWidget.MapW / viewW));
            int z = Mathf.Clamp(Mathf.CeilToInt(Mathf.Log(nIdeal, 2f)), MinZ, MaxZ);
            int n = 1 << z;
            _tileW = MapWidget.MapW / n;
            _tileH = MapWidget.MapH / n;

            if (_activeZ != z && _activeZ >= 0)
            {
                // 保留旧 atlas 作过渡；不清空队列计数（协程仍在飞）
                CaptureStaleAtlas();
                ClearCellRecordsOnly();
            }
            _activeZ = z;

            float halfW = Mathf.Max(viewSizeInWorld.x, _tileW) * 0.5f + _tileW;
            float halfH = Mathf.Max(viewSizeInWorld.y, _tileH) * 0.5f + _tileH;
            float minX = worldCenterLocal.x - halfW + MapWidget.MapW * 0.5f;
            float maxX = worldCenterLocal.x + halfW + MapWidget.MapW * 0.5f;
            float maxY = worldCenterLocal.y + halfH;
            float minY = worldCenterLocal.y - halfH;

            _vx0 = Mathf.FloorToInt(minX / _tileW);
            _vx1 = Mathf.FloorToInt(maxX / _tileW);
            _vy0 = Mathf.FloorToInt((MapWidget.MapH * 0.5f - maxY) / _tileH);
            _vy1 = Mathf.FloorToInt((MapWidget.MapH * 0.5f - minY) / _tileH);

            // 快速路径：z 与视口瓦片范围均未变、无在途/未载瓦片 → 本帧无需重建（拖拽期性能）。
            // LayoutDisplay 仍每帧执行（轻量，仅改 RectTransform），保证位置精确。
            if (_lastSyncVX0 == _vx0 && _lastSyncVX1 == _vx1 && _lastSyncVY0 == _vy0 && _lastSyncVY1 == _vy1)
            {
                bool allSettled = true;
                foreach (var kv in _cells)
                {
                    var c = kv.Value;
                    if (c == null) continue;
                    if (c.loading || (!c.loaded && !c.empty)) { allSettled = false; break; }
                }
                if (allSettled)
                {
                    LayoutDisplay();
                    return;
                }
            }
            _lastSyncVX0 = _vx0; _lastSyncVX1 = _vx1; _lastSyncVY0 = _vy0; _lastSyncVY1 = _vy1;

            var keep = new HashSet<long>();
            for (int vy = _vy0; vy <= _vy1; vy++)
            {
                int ty = ((vy % n) + n) % n;
                for (int vx = _vx0; vx <= _vx1; vx++)
                {
                    int tx = ((vx % n) + n) % n;
                    long key = Key(z, vx, vy);
                    keep.Add(key);
                    EnsureCell(z, tx, ty, vx, vy);
                }
            }

            if (_cells.Count > MaxCells)
            {
                var remove = new List<long>();
                foreach (var kv in _cells)
                    if (!keep.Contains(kv.Key)) remove.Add(kv.Key);
                for (int i = 0; i < remove.Count; i++)
                    _cells.Remove(remove[i]);
            }

            LayoutDisplay();
            _atlasDirty = true;
            Pump();
        }

        void EnsureCell(int z, int tx, int ty, int vx, int vy)
        {
            long key = Key(z, vx, vy);
            if (!_cells.TryGetValue(key, out var cell))
            {
                cell = new TileCell { z = z, x = tx, y = ty, vx = vx, vy = vy };
                _cells[key] = cell;
            }
            else
            {
                cell.vx = vx;
                cell.vy = vy;
            }

            // 自愈：缓存淘汰可能销毁仍被引用的纹理，重置后走重载
            if (cell.loaded && (cell.tex == null || !cell.tex))
            {
                cell.loaded = false;
                cell.tex = null;
            }
            if (cell.loaded || cell.loading || cell.empty) return;

            long ck = ContentKey(z, tx, ty);
            if (_emptyCache.Contains(ck))
            {
                cell.empty = true;
                return;
            }
            if (_texCache.TryGetValue(ck, out var cached) && cached != null)
            {
                cell.tex = cached;
                cell.loaded = true;
                _atlasDirty = true;
                return;
            }

            // 左右副本只消费缓存，等中心副本下完
            if (!IsPrimaryDownloader()) return;
            if (Time.unscaledTime < cell.nextRetry) return;
            Enqueue(key);
        }

        void LayoutDisplay()
        {
            if (_displayRt == null) return;
            int cols = Mathf.Max(1, _vx1 - _vx0 + 1);
            int rows = Mathf.Max(1, _vy1 - _vy0 + 1);
            float x0 = _vx0 * _tileW - MapWidget.MapW * 0.5f;
            float y0 = MapWidget.MapH * 0.5f - (_vy1 + 1) * _tileH;
            _displayRt.anchoredPosition = new Vector2(x0, y0);
            _displayRt.sizeDelta = new Vector2(cols * _tileW, rows * _tileH);
        }

        void RebuildAtlas()
        {
            _atlasDirty = false;
            _lastAtlasBuildAt = Time.unscaledTime;
            _forceAtlasRebuild = false;
            if (_display == null) return;

            int cols = Mathf.Max(1, _vx1 - _vx0 + 1);
            int rows = Mathf.Max(1, _vy1 - _vy0 + 1);
            // atlas 单元分辨率跟随屏幕：放大时保持 2× 上采样渐变不被压缩回马赛克
            float wantTp = _tileW * Mathf.Max(_lastPx, 1f);
            int tp = Mathf.Clamp(Mathf.RoundToInt(wantTp), 64, 512);
            if (cols * tp > MaxAtlasPx) tp = Mathf.Max(64, MaxAtlasPx / cols);
            if (rows * tp > MaxAtlasPx) tp = Mathf.Max(64, Mathf.Min(tp, MaxAtlasPx / rows));
            _atlasTilePx = tp;
            EnsureAtlas(cols * tp, rows * tp);
            if (_atlas == null) return;

            var prev = RenderTexture.active;
            RenderTexture.active = _atlas;
            GL.Clear(true, true, Color.clear);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, _atlas.width, _atlas.height, 0);

            if (_staleAtlas != null && Time.unscaledTime < _staleAtlasUntil
                && _staleAtlas.width == _atlas.width && _staleAtlas.height == _atlas.height)
                DrawTileToAtlas(new Rect(0, 0, _atlas.width, _atlas.height), _staleAtlas, new Rect(0, 0, 1, 1), 0.4f);
            else if (_staleAtlas != null && Time.unscaledTime >= _staleAtlasUntil)
                ReleaseStaleAtlas();

            foreach (var kv in _cells)
            {
                var c = kv.Value;
                if (c == null || c.empty) continue;
                if (c.vx < _vx0 || c.vx > _vx1 || c.vy < _vy0 || c.vy > _vy1) continue;

                // 左右副本可能还没绑定：合成前再读一次全局缓存
                if (!c.loaded || c.tex == null)
                {
                    long ck = ContentKey(c.z, c.x, c.y);
                    if (_texCache.TryGetValue(ck, out var hit) && hit != null)
                    {
                        c.tex = hit;
                        c.loaded = true;
                    }
                }

                // 自愈：缓存淘汰/换帧可能销毁了仍被引用的纹理，跳过并重置等待重载
                if (c.loaded && (c.tex == null || !c.tex))
                {
                    c.loaded = false;
                    c.tex = null;
                }

                float scale = tp / _tileW; // atlas 像素 ↔ 地图像素 换算
                var off = GcjTileOffset(c.z, c.x, c.y); // 高德底图为 GCJ-02，天气为 WGS-84：逐瓦片纠偏
                float px = (c.vx - _vx0) * tp + off.x * scale;
                float py = (c.vy - _vy0) * tp + off.y * scale;
                var dst = new Rect(px, py, tp, tp);

                if (c.loaded && c.tex != null)
                {
                    DrawTileToAtlas(dst, c.tex, new Rect(0, 0, 1, 1), 1f);
                    continue;
                }

                // 高 z 未就绪：用缓存里的父级瓦片裁切填充，避免放大后整片空白/马赛克空洞
                if (TryGetParentTile(c.z, c.x, c.y, out var parentTex, out var src))
                    DrawTileToAtlas(dst, parentTex, src, 0.9f);
            }

            GL.PopMatrix();
            RenderTexture.active = prev;

            _display.texture = _atlas;
            _display.color = WeatherTint;
            ApplyWeatherMaterial(_display);
            LayoutDisplay();
        }

        /// <summary>在缓存中找更低 z 的父瓦片，并算出对应子区域（归一化 UV，DrawTexture 要求 0~1）。</summary>
        static bool TryGetParentTile(int z, int x, int y, out Texture2D tex, out Rect srcUv)
        {
            tex = null;
            srcUv = default;
            for (int pz = z - 1; pz >= MinZ; pz--)
            {
                int shift = z - pz;
                int px = x >> shift;
                int py = y >> shift;
                long ck = ContentKey(pz, px, py);
                if (!_texCache.TryGetValue(ck, out tex) || tex == null || !tex) continue;

                int sub = 1 << shift;
                int lx = x & (sub - 1);
                int ly = y & (sub - 1);
                float inv = 1f / sub;
                // Unity 纹理 v=0 在底部（南）；瓦片 y=0 在北 → 翻转 ly
                srcUv = new Rect(lx * inv, (sub - 1 - ly) * inv, inv, inv);
                return true;
            }
            return false;
        }

        void EnsureAtlas(int w, int h)
        {
            w = Mathf.Clamp(w, 64, MaxAtlasPx);
            h = Mathf.Clamp(h, 64, MaxAtlasPx);
            if (_atlas != null && _atlas.width == w && _atlas.height == h) return;
            ReleaseAtlas();
            _atlas = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                name = "RadarDbzAtlas"
            };
            _atlas.Create();
        }

        void DrawTileToAtlas(Rect dst, Texture tex, Rect srcUv, float alpha)
        {
            if (tex == null) return;
            var m = EnsureAtlasBlitMat();
            var tint = new Color(1f, 1f, 1f, alpha);
            if (m != null)
            {
                m.SetColor("_Color", tint);
                Graphics.DrawTexture(dst, tex, srcUv, 0, 0, 0, 0, tint, m);
            }
            else
                Graphics.DrawTexture(dst, tex, srcUv, 0, 0, 0, 0, tint);
        }

        static Material EnsureAtlasBlitMat()
        {
            if (_atlasBlitMat != null) return _atlasBlitMat;
            var sh = Shader.Find("Hidden/AirlineCEO/AtlasBlit");
            if (sh == null) return null;
            _atlasBlitMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            return _atlasBlitMat;
        }

        void CaptureStaleAtlas()
        {
            if (_atlas == null) return;
            ReleaseStaleAtlas();
            _staleAtlas = new RenderTexture(_atlas.width, _atlas.height, 0, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "RadarDbzStale"
            };
            _staleAtlas.Create();
            var prev = RenderTexture.active;
            Graphics.Blit(_atlas, _staleAtlas);
            RenderTexture.active = prev;
            _staleAtlasUntil = Time.unscaledTime + StaleKeepSeconds;
        }

        void ReleaseStaleAtlas()
        {
            if (_staleAtlas == null) return;
            if (RenderTexture.active == _staleAtlas)
                RenderTexture.active = null;
            _staleAtlas.Release();
            Destroy(_staleAtlas);
            _staleAtlas = null;
        }

        void ReleaseAtlas()
        {
            if (_atlas == null) return;
            if (RenderTexture.active == _atlas)
                RenderTexture.active = null;
            if (_display != null && _display.texture == _atlas)
                _display.texture = _staleAtlas != null ? (Texture)_staleAtlas : CreateTransparentTexture();
            _atlas.Release();
            Destroy(_atlas);
            _atlas = null;
        }

        static void ApplyWeatherMaterial(RawImage img)
        {
            if (img == null) return;
            if (_weatherMat == null)
            {
                var sh = Shader.Find("AirlineCEO/UIWeatherRadar");
                if (sh == null) return;
                _weatherMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                _weatherMat.SetFloat("_DitherStrength", 0.25f);
            }
            img.material = _weatherMat;
        }

        void ClearAllCells()
        {
            _queue.Clear();
            _queued.Clear();
            _cells.Clear();
        }

        void ClearCellRecordsOnly()
        {
            _queue.Clear();
            _queued.Clear();
            _cells.Clear();
            _lastSyncVX0 = int.MinValue; // 失效快速路径缓存
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

            while (_globalInflight < MaxConcurrent && _queue.Count > 0)
            {
                long key = _queue.Dequeue();
                _queued.Remove(key);
                if (!_cells.TryGetValue(key, out var cell) || cell.loaded || cell.loading || cell.empty)
                    continue;

                long ck = ContentKey(cell.z, cell.x, cell.y);
                if (_emptyCache.Contains(ck))
                {
                    cell.empty = true;
                    continue;
                }
                if (_texCache.TryGetValue(ck, out var hit) && hit != null)
                {
                    cell.tex = hit;
                    cell.loaded = true;
                    _atlasDirty = true;
                    continue;
                }
                if (_downloadingCk.Contains(ck))
                {
                    // 其它协程正在下同一 content key，稍后再取缓存
                    cell.nextRetry = Time.unscaledTime + 0.3f;
                    continue;
                }

                cell.loading = true;
                Interlocked.Increment(ref _globalInflight);
                _downloadingCk.Add(ck);
                StartCoroutine(LoadTile(key, cell, ck));
            }
        }

        IEnumerator LoadTile(long key, TileCell cell, long ck)
        {
            string url = RealWeatherService.BuildRadarTileUrl(cell.z, cell.x, cell.y);
            if (string.IsNullOrEmpty(url))
            {
                FinishDownload(cell, ck, false);
                Pump();
                yield break;
            }

            // 走 .NET HttpClient（Mono TLS 栈）；UnityWebRequest 的 libcurl 对该 CDN 会
            // 半截卡死（如 1119/2325 B），同机 OpenSSL 栈完全正常
            string frameAtStart = _cacheFrameKey;
            var task = DownloadTileAsync(url);
            // 协程可能因图层禁用而中断：并发额度在任务 continuation 里用 Interlocked 归还，避免泄漏
            task.ContinueWith(_ => Interlocked.Decrement(ref _globalInflight), TaskScheduler.Default);
            yield return new WaitUntil(() => task.IsCompleted);
            var bytes = task.Status == TaskStatus.RanToCompletion ? task.Result : null;

            bool stillTracked = _cells.ContainsKey(key);

            if (bytes != null && bytes.Length > 32)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes, markNonReadable: false))
                {
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;

                    bool ok = false;
                    // 还原格心网格 → 带邻居边缘格点的全格插值（跨瓦片连续，无拼接色带）。
                    // 像素计算放后台线程，主线程只做纹理读写，避免 512² 插值阻塞主线程。
                    Color32[] px = null;
                    ImergTileProcessor.TileGrid grid = null;
                    Task compute = null;
                    try
                    {
                        int w = tex.width, h = tex.height;
                        px = tex.GetPixels32();
                        grid = ImergTileProcessor.ExtractGrid(px, w, h);
                        var west = GetGrid(cell.z, cell.x - 1, cell.y);
                        var east = GetGrid(cell.z, cell.x + 1, cell.y);
                        var north = GetGrid(cell.z, cell.x, cell.y - 1);
                        var south = GetGrid(cell.z, cell.x, cell.y + 1);
                        compute = Task.Run(() =>
                            ImergTileProcessor.ComputePixels(px, w, h, grid, west, east, north, south));
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[MapWeatherTileLayer] IMERG 处理失败：" + e.Message);
                        compute = null;
                    }

                    if (compute != null)
                    {
                        yield return new WaitUntil(() => compute.IsCompleted);
                        if (compute.Status == TaskStatus.Faulted)
                        {
                            Debug.LogWarning("[MapWeatherTileLayer] 平滑计算失败：" + compute.Exception?.GetBaseException().Message);
                        }
                        else
                        {
                            tex.SetPixels32(px);
                            tex.Apply(false, false);
                            ok = true;

                            if (frameAtStart == _cacheFrameKey)
                            {
                                _gridCache[ck] = grid;
                                // 新网格到位：延迟批量重平滑 4 个已缓存邻居（瓦片缝双向连续）
                                ResmoothNeighbors(cell.z, cell.x, cell.y);
                            }
                        }
                    }

                    if (!ok)
                    {
                        Destroy(tex);
                        if (stillTracked)
                        {
                            cell.attempts++;
                            cell.nextRetry = Time.unscaledTime + 2f;
                        }
                        FinishDownload(cell, ck, false);
                    }
                    else
                    {
                        // 下载期间换了雷达帧：旧帧数据作废
                        if (frameAtStart != _cacheFrameKey)
                            Destroy(tex);
                        else
                            PutCache(ck, tex);
                        if (stillTracked && frameAtStart == _cacheFrameKey)
                        {
                            cell.tex = tex;
                            cell.loaded = true;
                            cell.empty = false;
                            _atlasDirty = true;
                            _forceAtlasRebuild = true;
                        }
                        MarkAllDirty();
                        FinishDownload(cell, ck, true);
                    }
                }
                else
                {
                    Destroy(tex);
                    _emptyCache.Add(ck);
                    if (stillTracked) cell.empty = true;
                    FinishDownload(cell, ck, false);
                }
            }
            else if (bytes != null) // 空数组 = 404/204 无覆盖
            {
                _emptyCache.Add(ck);
                if (stillTracked) cell.empty = true;
                if (_loggedUrlFails < 2)
                {
                    _loggedUrlFails++;
                    Debug.Log($"[MapWeatherTileLayer] 瓦片无覆盖 404 z{cell.z}/{cell.x}/{cell.y}");
                }
                FinishDownload(cell, ck, false);
            }
            else
            {
                // 超时/网络错误：绝不写入 emptyCache，允许持续重试
                if (stillTracked)
                {
                    cell.attempts++;
                    cell.nextRetry = Time.unscaledTime + Mathf.Min(8f, 1.5f * cell.attempts);
                }
                if (_loggedUrlFails < 4)
                {
                    _loggedUrlFails++;
                    Debug.LogWarning($"[MapWeatherTileLayer] 瓦片下载失败 z{cell.z}/{cell.x}/{cell.y}");
                }
                FinishDownload(cell, ck, false);
            }

            Pump();
        }

        /// <summary>HttpClient 下载；返回 null = 网络/超时错误，空数组 = 404/204 无覆盖。</summary>
        static async Task<byte[]> DownloadTileAsync(string url)
        {
            try
            {
                var http = EnsureHttp();
                using (var msg = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    msg.Headers.UserAgent.ParseAdd(RealWeatherService.TileUserAgent);
                    msg.Headers.Accept.ParseAdd("image/png,image/*;q=0.8,*/*;q=0.5");
                    using (var resp = await http.SendAsync(msg).ConfigureAwait(false))
                    {
                        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound
                            || resp.StatusCode == System.Net.HttpStatusCode.NoContent)
                            return Array.Empty<byte>();
                        resp.EnsureSuccessStatusCode();
                        return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        static HttpClient EnsureHttp()
        {
            if (_http != null) return _http;
            _http = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                       | System.Net.DecompressionMethods.Deflate
            })
            { Timeout = TimeSpan.FromSeconds(RequestTimeoutSec) };
            return _http;
        }

        /// <summary>
        /// Domain Reload / 子系统重置前调用：释放 HttpClient 原生句柄并清空静态缓存引用。
        /// 避免跨域 Dispose 触发「Release of invalid GC handle」。
        /// </summary>
        public static void ReleaseDomainResources()
        {
            try { _http?.Dispose(); } catch { /* 旧域句柄可能已失效 */ }
            _http = null;
            SoftResetStatics();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnSubsystemRegistration() => SoftResetStatics();

        static void SoftResetStatics()
        {
            // Domain Reload 后旧 Texture2D 已随旧域销毁，只清字典，勿再 Destroy
            _texCache.Clear();
            _gridCache.Clear();
            _emptyCache.Clear();
            _downloadingCk.Clear();
            _dirtyResmooth.Clear();
            _live.Clear();
            _globalInflight = 0;
            _loggedUrlFails = 0;
            _cacheFrameKey = string.Empty;
            _weatherMat = null;
            _atlasBlitMat = null;
        }

        static void FinishDownload(TileCell cell, long ck, bool ok)
        {
            if (cell != null) cell.loading = false;
            // 主线程清理占用标记；并发额度的归还在 task continuation（Interlocked）中处理
            _downloadingCk.Remove(ck);
        }

        static void MarkAllDirty()
        {
            for (int i = 0; i < _live.Count; i++)
                if (_live[i] != null)
                {
                    _live[i]._atlasDirty = true;
                    _live[i]._forceAtlasRebuild = true;
                }
        }

        static long Key(int z, int vx, int vy) => ((long)z << 42) ^ ((long)(vx + 1048576) << 21) ^ (uint)(vy + 1048576);

        static long ContentKey(int z, int x, int y) =>
            ((long)(z & 0xFF) << 40) | ((long)(x & 0xFFFFF) << 20) | (uint)(y & 0xFFFFF);

        // 逐瓦片 GCJ-02 纠偏缓存（key = ContentKey(z,x,y) → 地图像素偏移）
        static readonly Dictionary<long, Vector2> _gcjOffsetCache = new Dictionary<long, Vector2>();

        /// <summary>
        /// 瓦片中心经纬度的 GCJ-02 纠偏量（地图世界像素）。
        /// 高德底图为 GCJ-02，NASA 天气瓦片为 WGS-84：逐瓦片按中心偏移平移，云图才能对齐底图。
        /// 云图是弥散数据（~10km 分辨率），瓦片内残留的百米级非线性误差不可见。
        /// </summary>
        static Vector2 GcjTileOffset(int z, int x, int y)
        {
            if (!MapTileLayer.IsGcjSource) return Vector2.zero; // Google 底图为 WGS-84，天气瓦片无需纠偏

            long key = ContentKey(z, x, y);
            if (_gcjOffsetCache.TryGetValue(key, out var hit)) return hit;

            int n = 1 << z;
            double lon = ((x + 0.5) / n) * 360.0 - 180.0;
            double lat = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * (y + 0.5) / n))) * 180.0 / Math.PI;

            CoordConverter.Wgs84ToGcj02(lon, lat, out double gLon, out double gLat);
            float dx = (float)((gLon - lon) / 360.0 * MapWidget.MapW);
            float y0 = (float)(Math.Log(Math.Tan(Math.PI * 0.25 + lat * Math.PI / 180.0 * 0.5)) / Math.PI * MapWidget.MapH * 0.5);
            float y1 = (float)(Math.Log(Math.Tan(Math.PI * 0.25 + gLat * Math.PI / 180.0 * 0.5)) / Math.PI * MapWidget.MapH * 0.5);
            float dy = -(y1 - y0);

            var v = new Vector2(dx, dy);
            _gcjOffsetCache[key] = v;
            return v;
        }

        static void EnsureCacheFrame()
        {
            string frame = RealWeatherService.RadarFrameKey;
            if (frame == _cacheFrameKey) return;
            ClearTextureCache();
            _cacheFrameKey = frame ?? string.Empty;
        }

        static ImergTileProcessor.TileGrid GetGrid(int z, int x, int y)
        {
            return _gridCache.TryGetValue(ContentKey(z, x, y), out var g) ? g : null;
        }

        /// <summary>新瓦片网格入库后，把 4 个已缓存邻居标记为待重平滑（延迟批量、后台线程处理）。</summary>
        static void ResmoothNeighbors(int z, int x, int y)
        {
            MarkResmooth(z, x - 1, y);
            MarkResmooth(z, x + 1, y);
            MarkResmooth(z, x, y - 1);
            MarkResmooth(z, x, y + 1);
        }

        static void MarkResmooth(int z, int x, int y)
        {
            long ck = ContentKey(z, x, y);
            if (_gridCache.ContainsKey(ck) && _texCache.ContainsKey(ck))
                _dirtyResmooth.Add(ck);
        }

        /// <summary>
        /// 后台排空待重平滑邻居：每拍取 1 个，像素计算放后台线程（Task.Run），
        /// 主线程只做 GetPixels32 / SetPixels32 / Apply，避免一次重算 4 邻阻塞主线程。
        /// </summary>
        IEnumerator ResmoothDrainLoop()
        {
            var wait = new WaitForSeconds(0.08f);
            while (true)
            {
                yield return wait;
                if (_dirtyResmooth.Count == 0) continue;

                long ck = default;
                foreach (var k in _dirtyResmooth) { ck = k; break; }
                _dirtyResmooth.Remove(ck);

                int z = (int)((ck >> 40) & 0xFF);
                int x = (int)((ck >> 20) & 0xFFFFF);
                int y = (int)(ck & 0xFFFFF);

                if (!_gridCache.TryGetValue(ck, out var grid) || grid == null) continue;
                if (!_texCache.TryGetValue(ck, out var tex) || tex == null) continue;

                int w = tex.width, h = tex.height;
                var px = tex.GetPixels32();
                var west = GetGrid(z, x - 1, y);
                var east = GetGrid(z, x + 1, y);
                var north = GetGrid(z, x, y - 1);
                var south = GetGrid(z, x, y + 1);

                var compute = Task.Run(() =>
                    ImergTileProcessor.ComputePixels(px, w, h, grid, west, east, north, south));
                yield return new WaitUntil(() => compute.IsCompleted);
                if (compute.Status == TaskStatus.Faulted)
                {
                    Debug.LogWarning("[MapWeatherTileLayer] 重平滑失败：" + compute.Exception?.GetBaseException().Message);
                }
                else if (tex != null) // 缓存淘汰可能已销毁该纹理：跳过写回，协程不能死
                {
                    try
                    {
                        tex.SetPixels32(px);
                        tex.Apply(false, false);
                        MarkAllDirty();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[MapWeatherTileLayer] 重平滑写回失败：" + e.Message);
                    }
                }
            }
        }

        static void PutCache(long ck, Texture2D tex)
        {
            if (_texCache.ContainsKey(ck))
            {
                // 已有则丢弃新的，避免泄漏
                if (_texCache[ck] != tex) UnityEngine.Object.Destroy(tex);
                return;
            }
            if (_texCache.Count >= MaxCacheEntries)
            {
                // 保护清单：仍被活 cell 引用（在屏/在队列）的纹理不能销毁，
                // 否则瓦片「闪现后消失」、atlas 出现空白带、UnityException 红字
                var protectedCk = new HashSet<long>();
                for (int i = 0; i < _live.Count; i++)
                {
                    var layer = _live[i];
                    if (layer == null) continue;
                    foreach (var kv in layer._cells)
                        if (kv.Value != null && (kv.Value.loaded || kv.Value.loading) && kv.Value.tex != null)
                            protectedCk.Add(ContentKey(kv.Value.z, kv.Value.x, kv.Value.y));
                }

                var keys = new List<long>(_texCache.Keys);
                int drop = keys.Count / 2;
                int dropped = 0;
                for (int i = 0; i < keys.Count && dropped < drop; i++)
                {
                    if (protectedCk.Contains(keys[i])) continue;
                    if (_texCache.TryGetValue(keys[i], out var t) && t != null)
                        UnityEngine.Object.Destroy(t);
                    _texCache.Remove(keys[i]);
                    _gridCache.Remove(keys[i]);
                    dropped++;
                }
            }
            _texCache[ck] = tex;
        }

        static void ClearTextureCache()
        {
            foreach (var kv in _texCache)
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            _texCache.Clear();
            _gridCache.Clear();
            _emptyCache.Clear();
            _downloadingCk.Clear();
            _dirtyResmooth.Clear();
            // 不清 _globalInflight：在途任务完成时仍会递减，清零会导致负数计数放大并发
            _loggedUrlFails = 0;
        }
    }
}
