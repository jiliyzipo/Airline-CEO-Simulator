using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using AirlineCEO.Systems;
using UnityEngine;
using UnityEngine.Networking;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 现实天气数据：
    /// 1) NASA GIBS GPM IMERG 降水率（WMS GetMap 512px，官方调色板直出，~10km 网格）
    /// 2) 浙江省水利厅台风路径
    /// </summary>
    public static class RealWeatherService
    {
        public static event Action OnChanged;

        /// <summary>IMERG 无需元数据握手，服务恒可用；瓦片 404/无覆盖按空瓦处理。</summary>
        public static bool RadarReady => true;

        const string ImergLayer = "IMERG_Precipitation_Rate_30min_v7_NRT";
        const string WmsEndpoint = "https://gibs.earthdata.nasa.gov/wms/epsg3857/nrt/wms.cgi";
        const string TyphoonListUrl = "https://typhoon.slt.zj.gov.cn/Api/TyhoonActivity";
        const string TyphoonInfoUrl = "https://typhoon.slt.zj.gov.cn/Api/TyphoonInfo/";
        /// <summary>卫星估计产品时延大（实测 ~5-6h）；取 6h 前最近半点帧，保证帧存在。</summary>
        const double FrameLagHours = 6.0;
        /// <summary>WMS GetMap 输出尺寸：512 像素/瓦片（WMTS 固定 256，不支持更大）。</summary>
        public const int TileSize = 512;

        public static bool TyphoonLoaded;
        public static bool HasTyphoon;
        public static string TyphoonName;
        public static readonly List<Vector2> TyphoonTrack = new List<Vector2>();

        static bool _started;
        static MonoBehaviour _runner;

        public static void EnsureLoaded(MonoBehaviour runner)
        {
            if (runner == null) return;

            // 运行器已销毁（切场景 / 关闭 Domain Reload 后二次进 Play）→ 视为未启动，重新拉起两条循环。
            // 否则 _started 永远为 true 而 _runner 已死，雷达与台风再也不会加载。
            if (_started && _runner == null) _started = false;

            if (_started)
            {
                _runner = runner;
                return;
            }
            _started = true;
            _runner = runner;
            runner.StartCoroutine(FetchTyphoonLoop());
            runner.StartCoroutine(RefreshRadarLoop());
            Debug.Log($"[RealWeatherService] GPM IMERG 就绪，帧 {RadarFrameKey}");
        }

        /// <summary>
        /// Domain Reload 前停掉两条循环。在飞请求由 <see cref="WebRequestGuard.AbortAll"/> 统一中止
        /// （见 <c>WeatherTileDomainReload</c>）——不中止的话台风请求会在新域完成，
        /// Unity 清理旧域协程句柄失败，打印「Release of invalid GC handle」。
        /// </summary>
        public static void ReleaseDomainResources()
        {
            _runner = null;     // 循环条件 `while (_runner != null)` 随即失败，协程自然收尾
            _started = false;
        }

        /// <summary>
        /// 关闭 Domain Reload 时静态量不归零，显式清一次，避免残留状态让天气永不加载。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _started = false;
            _runner = null;
            TyphoonLoaded = false;
            HasTyphoon = false;
            TyphoonName = null;
            TyphoonTrack.Clear();
        }

        /// <summary>每分钟检查帧时间是否推进（30 分钟一帧），推进即广播刷新。</summary>
        static IEnumerator RefreshRadarLoop()
        {
            string last = RadarFrameKey;
            while (_runner != null)
            {
                yield return new WaitForSeconds(60f);
                if (RadarFrameKey != last)
                {
                    last = RadarFrameKey;
                    Debug.Log($"[RealWeatherService] GPM IMERG 帧已更新：{last}");
                    OnChanged?.Invoke();
                }
            }
        }

        public const string TileUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        static DateTime FrameTimeUtc()
        {
            var t = DateTime.UtcNow.AddHours(-FrameLagHours);
            return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute / 30 * 30, 0, DateTimeKind.Utc);
        }

        /// <summary>当前雷达帧唯一键（UTC 半点时间戳），用于瓦片缓存失效。</summary>
        public static string RadarFrameKey =>
            FrameTimeUtc().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        const double EarthCircum = 40075016.686; // Web Mercator 赤道周长（米）

        /// <summary>
        /// GIBS WMS GetMap 瓦片请求（WMTS 固定 256px 无尺寸段，实测 512 返回 400；
        /// WMS 支持 512 且 TRANSPARENT=true 原生无灰底）。bbox 取该 XYZ 瓦片的 Web Mercator 范围。
        /// </summary>
        public static string BuildRadarTileUrl(int z, int x, int y)
        {
            double tile = EarthCircum / (1 << z);
            double west = -EarthCircum / 2 + x * tile;
            double east = west + tile;
            double north = EarthCircum / 2 - y * tile;
            double south = north - tile;
            var inv = CultureInfo.InvariantCulture;
            string bbox = string.Format(inv, "{0:0.##},{1:0.##},{2:0.##},{3:0.##}", west, south, east, north);
            return $"{WmsEndpoint}?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap"
                 + $"&LAYERS={ImergLayer}&CRS=EPSG:3857&BBOX={bbox}"
                 + $"&WIDTH={TileSize}&HEIGHT={TileSize}"
                 + $"&FORMAT=image/png&TRANSPARENT=true&TIME={RadarFrameKey}&STYLES=";
        }

        /// <summary>给瓦片请求补齐 UA；勿手动设 Referer（Unity 会黄警并忽略）。</summary>
        public static void ApplyTileRequestHeaders(UnityWebRequest req)
        {
            if (req == null) return;
            req.SetRequestHeader("User-Agent", TileUserAgent);
            req.SetRequestHeader("Accept", "image/png,image/*;q=0.8,*/*;q=0.5");
        }

        static IEnumerator FetchTyphoonLoop()
        {
            for (int attempt = 0; attempt < 2 && !HasTyphoon; attempt++)
            {
                yield return FetchTyphoon();
                if (!HasTyphoon) yield return new WaitForSeconds(2f);
            }
            TyphoonLoaded = true;
            OnChanged?.Invoke();
        }

        static IEnumerator FetchTyphoon()
        {
            using (var list = UnityWebRequest.Get(TyphoonListUrl))
            {
                list.timeout = 15;
                WebRequestGuard.Track(list);
                try { yield return list.SendWebRequest(); }
                finally { WebRequestGuard.Untrack(list); }
                if (list.result != UnityWebRequest.Result.Success) yield break;

                string tfid = null;
                try { tfid = ParseTyphoonList(list.downloadHandler.text); }
                catch (Exception e) { Debug.LogWarning("[RealWeatherService] 台风列表解析失败：" + e.Message); }
                if (string.IsNullOrEmpty(tfid)) yield break;

                using (var detail = UnityWebRequest.Get(TyphoonInfoUrl + tfid))
                {
                    detail.timeout = 20;
                    WebRequestGuard.Track(detail);
                    try { yield return detail.SendWebRequest(); }
                    finally { WebRequestGuard.Untrack(detail); }
                    if (detail.result != UnityWebRequest.Result.Success) yield break;
                    try { ParseTyphoonDetail(detail.downloadHandler.text); }
                    catch (Exception e) { Debug.LogWarning("[RealWeatherService] 台风详情解析失败：" + e.Message); }
                }
            }
        }

        static string ParseTyphoonList(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var wrap = JsonUtility.FromJson<TyphoonListWrap>("{\"items\":" + json + "}");
            if (wrap == null || wrap.items == null || wrap.items.Length == 0) return null;
            return wrap.items[0].tfid;
        }

        static void ParseTyphoonDetail(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<TyphoonDetail>(json);
            if (d == null || d.points == null || d.points.Length == 0) return;

            TyphoonTrack.Clear();
            foreach (var p in d.points)
            {
                if (float.TryParse(p.lng, NumberStyles.Float, CultureInfo.InvariantCulture, out float lon)
                 && float.TryParse(p.lat, NumberStyles.Float, CultureInfo.InvariantCulture, out float lat))
                    TyphoonTrack.Add(new Vector2(lon, lat));
            }

            if (TyphoonTrack.Count > 0)
            {
                HasTyphoon = true;
                TyphoonName = "台风 · " + (string.IsNullOrEmpty(d.name) ? "气旋" : d.name);
            }
        }

        [Serializable] class TyphoonListWrap { public TyphoonItem[] items; }
        [Serializable] class TyphoonItem { public string tfid; }
        [Serializable] class TyphoonDetail { public string name; public TyphoonPoint[] points; }
        [Serializable] class TyphoonPoint { public string lng; public string lat; }
    }
}
