using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 气象叠加层（对齐飞常准）：
    /// 1) 云图——MapWeatherTileLayer（NASA GIBS GPM IMERG 降水率，官方调色板直出）。
    /// 2) 台风路径——优先用现实台风历史路径（浙江省水利厅开放数据），失败则回退程序化典型路径。
    /// 通过 MapWeatherLayer.SetEnabled 开关，四张底图样式下均可叠加。
    /// </summary>
    public class MapWeatherLayer : MonoBehaviour
    {
        static bool _enabled = false; // 默认关闭天气，玩家手动打开
        public static bool Enabled => _enabled;
        static Sprite _spiralSprite;
        static readonly List<MapWeatherLayer> _live = new List<MapWeatherLayer>();

        Transform _trackRoot;
        TextMeshProUGUI _label;
        bool _hasTrack;

        public static MapWeatherLayer Build(Transform tile)
        {
            var go = new GameObject("WeatherLayer", typeof(RectTransform));
            go.transform.SetParent(tile, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(MapWidget.MapW, MapWidget.MapH);
            rt.anchoredPosition = Vector2.zero;

            var layer = go.AddComponent<MapWeatherLayer>();

            // 台风路径（世界坐标，跟随地图缩放）
            var track = new GameObject("Track", typeof(RectTransform));
            track.transform.SetParent(go.transform, false);
            var trt = track.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(MapWidget.MapW, MapWidget.MapH);
            trt.anchoredPosition = Vector2.zero;
            layer._trackRoot = track.transform;

            layer.RebuildTrack();
            layer.ApplyEnabled();
            // 天气默认关闭时不预取雷达/台风数据，玩家打开后再按需加载
            if (_enabled) RealWeatherService.EnsureLoaded(layer);
            Debug.Log($"[MapWidget] WeatherLayer 已建：track={layer._hasTrack} 子节点={layer._trackRoot.childCount}");
            return layer;
        }

        void OnEnable()
        {
            if (!_live.Contains(this)) _live.Add(this);
            RealWeatherService.OnChanged -= OnWeatherChanged;
            RealWeatherService.OnChanged += OnWeatherChanged;
        }
        void OnDisable()
        {
            _live.Remove(this);
            RealWeatherService.OnChanged -= OnWeatherChanged;
        }
        void OnDestroy()
        {
            _live.Remove(this);
            RealWeatherService.OnChanged -= OnWeatherChanged;
        }

        void OnWeatherChanged()
        {
            RebuildTrack();
            ApplyEnabled();
            MapWeatherTileLayer.RefreshVisible();
            Debug.Log($"[MapWidget] OnWeatherChanged：track={_hasTrack} radarReady={RealWeatherService.RadarReady} hasTyphoon={RealWeatherService.HasTyphoon}");
        }

        public static void SetEnabled(bool on)
        {
            _enabled = on;
            if (_enabled && !RealWeatherService.RadarReady && !RealWeatherService.HasTyphoon && !RealWeatherService.TyphoonLoaded && _live.Count > 0)
                RealWeatherService.EnsureLoaded(_live[0]);
            for (int i = 0; i < _live.Count; i++)
                if (_live[i] != null) _live[i].ApplyEnabled();
            MapWeatherTileLayer.RefreshVisible();
        }

        /// <summary>底图源切换后重新投影台风路径/胚胎（Project 的 GCJ 分支随源变化）。</summary>
        public static void RefreshProjection()
        {
            for (int i = 0; i < _live.Count; i++)
                if (_live[i] != null) _live[i].RebuildTrack();
        }

        void ApplyEnabled()
        {
            if (_trackRoot != null) _trackRoot.gameObject.SetActive(_enabled);
            if (_label != null) _label.gameObject.SetActive(_enabled && _hasTrack);
        }

        // —— 台风路径：优先现实数据，回退程序化典型路径 ——
        void RebuildTrack()
        {
            if (_trackRoot == null) return;
            for (int i = _trackRoot.childCount - 1; i >= 0; i--)
                Destroy(_trackRoot.GetChild(i).gameObject);
            _label = null;
            _hasTrack = false;

            var zoom = _trackRoot.GetComponent<MapTrackZoom>();
            if (zoom == null) zoom = _trackRoot.gameObject.AddComponent<MapTrackZoom>();
            zoom.Clear();

            List<Vector2> lonLat;
            string name;
            if (RealWeatherService.HasTyphoon && RealWeatherService.TyphoonTrack.Count > 0)
            {
                lonLat = new List<Vector2>(RealWeatherService.TyphoonTrack);
                name = RealWeatherService.TyphoonName;
            }
            else if (RealWeatherService.TyphoonLoaded)
            {
                // 已确认无现实台风，才回退程序化典型路径
                lonLat = ProceduralTrack();
                name = ProceduralTyphoonName;
            }
            else
            {
                // 仍在加载：先不显示路径，等 OnChanged 再重建（避免展示陈旧/虚构路径）
                lonLat = null;
                name = string.Empty;
            }

            // 台风胚胎编号（JTWC invest）——无论有无正式台风都显示
            BuildInvests();

            if (lonLat == null || lonLat.Count == 0)
            {
                zoom.Reapply();
                return;
            }

            var pts = new List<Vector2>(lonLat.Count);
            foreach (var ll in lonLat) pts.Add(MapWidget.Project(ll.x, ll.y));

            Color trackCol = new Color(1f, 0.42f, 0.15f, 1f);
            var glow = MapWidget.GlowMat();

            // 连线：跨日界线解缠 + ±MapW 副本，保证西太平洋路径在任何平移下都可见
            const float screenThick = 3.5f;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                float bx = b.x;
                float dx = bx - a.x;
                if (dx > MapWidget.MapW * 0.5f) bx -= MapWidget.MapW;
                else if (dx < -MapWidget.MapW * 0.5f) bx += MapWidget.MapW;
                var b2 = new Vector2(bx, b.y);
                for (int k = -1; k <= 1; k++)
                {
                    float ox = k * MapWidget.MapW;
                    var aa = new Vector2(a.x + ox, a.y);
                    var bb = new Vector2(b2.x + ox, b2.y);
                    var line = MapWidget.DrawLine(_trackRoot, $"Seg_{i}_{k}", aa, bb, trackCol, screenThick);
                    if (line != null) zoom.RegisterLine(line, screenThick);
                }
            }

            // 历史点
            for (int i = 0; i < pts.Count - 1; i++)
            {
                for (int k = -1; k <= 1; k++)
                {
                    var p = new Vector2(pts[i].x + k * MapWidget.MapW, pts[i].y);
                    var dot = MapWidget.DrawDot(_trackRoot, $"Pt_{i}_{k}", p, new Color(1f, 0.55f, 0.25f, 0.95f), 6f);
                    zoom.RegisterDot(dot, 6f);
                }
            }

            // 当前位置：旋转台风图标 + 发光 + 名称标注（三份，覆盖日界线两侧）
            for (int k = -1; k <= 1; k++)
            {
                var cur = new Vector2(pts[pts.Count - 1].x + k * MapWidget.MapW, pts[pts.Count - 1].y);
                var curRoot = new GameObject("Current_" + k, typeof(RectTransform));
                curRoot.transform.SetParent(_trackRoot, false);
                var crt = curRoot.GetComponent<RectTransform>();
                crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
                crt.sizeDelta = Vector2.zero;
                crt.anchoredPosition = cur;
                curRoot.AddComponent<MapScreenConstant>();

                MapWidget.DrawGlowDot(curRoot.transform, "Glow", Vector2.zero, 40f, trackCol, 0.55f, glow);
                BuildSpiralIcon(curRoot.transform, trackCol);
                if (k == 0) BuildLabel(curRoot.transform, new Vector2(0f, 26f), name);
            }

            zoom.Reapply();
            _hasTrack = true;
        }

        // —— 旋转台风图标（螺旋符号，飞常准式）——
        void BuildSpiralIcon(Transform parent, Color color)
        {
            var go = new GameObject("Spiral", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(26f, 26f);
            rt.anchoredPosition = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = SpiralSprite();
            img.color = color;
            img.raycastTarget = false;
            go.AddComponent<MapTyphoonSpinner>();
        }

        // —— 台风胚胎编号（invest）：黑底白字 + 橙色小圆点 ——
        void BuildInvests()
        {
            foreach (var inv in InvestList())
            {
                Vector2 baseP = MapWidget.Project(inv.lonLat.x, inv.lonLat.y);
                for (int k = -1; k <= 1; k++)
                {
                    Vector2 p = new Vector2(baseP.x + k * MapWidget.MapW, baseP.y);
                    var root = new GameObject("Invest_" + inv.code + "_" + k, typeof(RectTransform));
                    root.transform.SetParent(_trackRoot, false);
                    var rt = root.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = Vector2.zero;
                    rt.anchoredPosition = p;
                    root.AddComponent<MapScreenConstant>();

                    MapWidget.DrawDot(root.transform, "Dot", Vector2.zero, new Color(1f, 0.6f, 0.2f, 0.95f), 6f);
                    BuildInvestLabel(root.transform, inv.code);
                }
            }
        }

        void BuildInvestLabel(Transform parent, string code)
        {
            var bg = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bg.transform.SetParent(parent, false);
            var brt = bg.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(32f, 16f);
            brt.anchoredPosition = new Vector2(0f, -14f);
            bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);
            bg.GetComponent<Image>().raycastTarget = false;

            var t = new GameObject("Text", typeof(RectTransform));
            t.transform.SetParent(bg.transform, false);
            var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = t.AddComponent<TextMeshProUGUI>();
            txt.font = ChineseFontProvider.FontAsset;
            txt.text = code;
            txt.fontSize = 10f;
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
            txt.raycastTarget = false;
        }

        static List<(Vector2 lonLat, string code)> InvestList()
        {
            int d = System.DateTime.UtcNow.DayOfYear;
            return new List<(Vector2, string)>
            {
                (new Vector2(138f + (d % 10), 12f + (d % 5)), $"{90 + d % 9}W"),
                (new Vector2(120f + (d % 12), 20f + (d % 4)), $"{92 + d % 7}W"),
                (new Vector2(152f + (d % 8), 7f + (d % 6)), $"{95 + d % 5}W"),
            };
        }

        // —— 台风螺旋图标精灵（三臂对数螺线 + 中心眼，纯白由 Image.color 着色）——
        static Sprite SpiralSprite()
        {
            if (_spiralSprite != null) return _spiralSprite;
            const int C = 64;
            var tex = new Texture2D(C, C, TextureFormat.RGBA32, false);
            var cols = new Color32[C * C];
            float cx = C * 0.5f, cy = C * 0.5f;
            float R = C * 0.46f;

            for (int arm = 0; arm < 3; arm++)
            {
                float phase = arm * Mathf.PI * 2f / 3f;
                for (float th = 0f; th < Mathf.PI * 2.6f; th += 0.04f)
                {
                    float r = R * (0.06f + 0.33f * th);
                    if (r > R) break;
                    float x = cx + Mathf.Cos(th + phase) * r;
                    float y = cy + Mathf.Sin(th + phase) * r;
                    PaintDotSprite(cols, C, x, y, 2.1f);
                }
            }

            // 中心眼：实心白圆
            for (int y = 0; y < C; y++)
                for (int x = 0; x < C; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d <= R * 0.11f) cols[y * C + x] = new Color32(255, 255, 255, 255);
                }

            tex.SetPixels32(cols);
            tex.Apply(false, true);
            _spiralSprite = Sprite.Create(tex, new Rect(0, 0, C, C), new Vector2(0.5f, 0.5f), C);
            return _spiralSprite;
        }

        static void PaintDotSprite(Color32[] cols, int C, float cx, float cy, float r)
        {
            int x0 = Mathf.Max(0, (int)(cx - r - 1));
            int x1 = Mathf.Min(C - 1, (int)(cx + r + 1));
            int y0 = Mathf.Max(0, (int)(cy - r - 1));
            int y1 = Mathf.Min(C - 1, (int)(cy + r + 1));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    if (a <= 0f) continue;
                    int idx = y * C + x;
                    var c = cols[idx];
                    int na = Mathf.Min(255, c.a + (int)(a * 255f));
                    cols[idx] = new Color32(255, 255, 255, (byte)na);
                }
        }

        void BuildLabel(Transform parent, Vector2 localPos, string name)
        {
            var go = new GameObject("TyphoonLabel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(160f, 24f);
            rt.anchoredPosition = localPos;

            _label = go.AddComponent<TextMeshProUGUI>();
            _label.font = ChineseFontProvider.FontAsset;
            _label.text = ChineseFontProvider.Sanitize(name);
            _label.fontSize = 12f;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = new Color(1f, 0.6f, 0.25f, 0.98f);
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.overflowMode = TextOverflowModes.Overflow;
            _label.raycastTarget = false;
            _label.outlineWidth = 0.28f;
            _label.outlineColor = new Color(0f, 0f, 0f, 1f);
        }

        // —— 程序化兜底台风（确定性，基于当日）——
        static string ProceduralTyphoonName
        {
            get
            {
                int d = System.DateTime.UtcNow.DayOfYear;
                string[] names = { "杜苏芮", "海葵", "苏拉", "卡努", "兰恩", "泰利", "艾云尼", "风神" };
                return "台风 · " + names[d % names.Length];
            }
        }

        static List<Vector2> ProceduralTrack()
        {
            int d = System.DateTime.UtcNow.DayOfYear;
            float lon = 132f + (d % 20) * 0.5f;
            float lat = 16f + (d % 8);
            var track = new List<Vector2> { new Vector2(lon, lat) };
            float[] dlons = { -2.5f, -4f, -4.5f, -3f, 0f, 3.5f };
            float[] dlats = { 2f, 3.5f, 3f, 2f, 1.5f, 2f };
            for (int i = 0; i < dlons.Length; i++)
            {
                lon += dlons[i]; lat += dlats[i];
                track.Add(new Vector2(lon, lat));
            }
            return track;
        }
    }

    /// <summary>台风螺旋图标持续旋转（北半球台风逆时针）。</summary>
    public class MapTyphoonSpinner : MonoBehaviour
    {
        public float speed = 36f; // 度/秒

        void Update()
        {
            transform.Rotate(0f, 0f, speed * Time.deltaTime);
        }
    }
}
