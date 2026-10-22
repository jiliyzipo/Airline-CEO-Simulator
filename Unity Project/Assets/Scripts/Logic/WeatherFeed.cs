using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

#nullable disable

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 机场气象实况（GDB 29 补充管道）：**不依赖新闻稿**也能扰动运行。
    /// <para>
    /// 数据源：Open-Meteo 公开预报/实况接口（无 Key、UTF-8 JSON、大陆可直连）。
    /// 采样点：基地 + 当前在飞航线两端，最多 <see cref="MaxAirports"/> 个，一次请求批量取回。
    /// 判定：风速 / 能见度 / 降水 / 天气代码 → 大风、低能见度、雷暴、强降水、降雪；
    /// 严重到关闭级别才冻结该机场，其余只降准点率。
    /// </para>
    /// <para>
    /// 与新闻管道彼此独立：任一源不可达只影响自己，失败静默（不刷 Unity 红字）。
    /// 同机场同现象 12 小时内只投一次，另有每日入队上限兜底（见 <c>ExternalEventManager</c>）。
    /// </para>
    /// </summary>
    public static class WeatherFeed
    {
        public const string PrefEnabled = "ext.weather.enabled";
        public const string PrefInterval = "ext.weather.interval";

        public const int DefaultIntervalSeconds = 1800;
        const int MaxAirports = 6;
        const int TimeoutSeconds = 12;

        static bool _loaded;
        static bool _enabled = true;
        static int _intervalSeconds = DefaultIntervalSeconds;

        static bool _inFlight;
        static DateTime _lastAttemptUtc = DateTime.MinValue;
        static string _status = "";

        static readonly object Gate = new object();
        static readonly List<ExternalEventPayload> Inbox = new List<ExternalEventPayload>();

        public static bool Enabled
        {
            get { EnsureLoaded(); return _enabled; }
            set
            {
                EnsureLoaded();
                _enabled = value;
                PlayerPrefs.SetInt(PrefEnabled, value ? 1 : 0);
                PlayerPrefs.Save();
                if (!value) lock (Gate) { Inbox.Clear(); _status = ""; }
            }
        }

        public static int IntervalSeconds
        {
            get { EnsureLoaded(); return _intervalSeconds; }
            set
            {
                EnsureLoaded();
                _intervalSeconds = Mathf.Max(0, value);
                PlayerPrefs.SetInt(PrefInterval, _intervalSeconds);
                PlayerPrefs.Save();
            }
        }

        public static string Status { get { EnsureLoaded(); return _status; } }

        public static void Reset()
        {
            lock (Gate)
            {
                Inbox.Clear();
                _inFlight = false;
                _lastAttemptUtc = DateTime.MinValue;
                _status = "";
            }
        }

        /// <summary>主线程排空：拿到待入队的气象载荷（无数据返回空列表）。</summary>
        public static List<ExternalEventPayload> Drain()
        {
            var list = new List<ExternalEventPayload>();
            lock (Gate)
            {
                if (Inbox.Count == 0) return list;
                list.AddRange(Inbox);
                Inbox.Clear();
            }
            return list;
        }

        /// <summary>发起一轮后台气象拉取（非阻塞，受间隔约束）。</summary>
        public static void RequestRefresh(GameState gs, bool force = false)
        {
            EnsureLoaded();
            if (!_enabled || gs == null) return;

            var codes = PickAirports(gs);
            if (codes.Count == 0) return;

            lock (Gate)
            {
                if (_inFlight) return;
                if (!force && _intervalSeconds <= 0) return;
                if (!force && (DateTime.UtcNow - _lastAttemptUtc).TotalSeconds < _intervalSeconds) return;
                _inFlight = true;
                _lastAttemptUtc = DateTime.UtcNow;
            }

            string url = BuildUrl(codes);
            Task.Run(async () =>
            {
                List<ExternalEventPayload> items = null;
                string note = "不可达";
                try
                {
                    string text = await DownloadTextAsync(url).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(text))
                    {
                        items = Parse(text, codes);
                        note = items == null ? "无法识别返回" : $"{items.Count} 条异常";
                    }
                }
                catch
                {
                    // 静默：气象源失败不影响任何其它管道
                }
                finally
                {
                    lock (Gate)
                    {
                        _inFlight = false;
                        if (items != null && items.Count > 0) Inbox.AddRange(items);
                        _status = $"{DateTime.Now:MM-dd HH:mm} {codes.Count} 场站 · {note}";
                    }
                }
            });
        }

        static List<string> PickAirports(GameState gs) => NetworkAirports.Pick(gs, MaxAirports);

        static string BuildUrl(List<string> codes)
        {
            var sb = new StringBuilder("https://api.open-meteo.com/v1/forecast?latitude=");
            for (int i = 0; i < codes.Count; i++)
            {
                AirportGeoCatalog.TryLonLat(codes[i], out _, out float lat);
                if (i > 0) sb.Append(',');
                sb.Append(lat.ToString("F4", CultureInfo.InvariantCulture));
            }
            sb.Append("&longitude=");
            for (int i = 0; i < codes.Count; i++)
            {
                AirportGeoCatalog.TryLonLat(codes[i], out float lon, out _);
                if (i > 0) sb.Append(',');
                sb.Append(lon.ToString("F4", CultureInfo.InvariantCulture));
            }
            sb.Append("&current=wind_speed_10m,precipitation,weather_code,visibility&timezone=UTC");
            return sb.ToString();
        }

        [Serializable]
        class OmRoot
        {
            public OmItem[] items;
        }

        [Serializable]
        class OmItem
        {
            public OmCurrent current;
        }

        [Serializable]
        class OmCurrent
        {
            public float wind_speed_10m;
            public float precipitation;
            public int weather_code;
            public float visibility;
        }

        /// <summary>解析 Open-Meteo 返回（公开以便离线自测）。</summary>
        public static List<ExternalEventPayload> Parse(string json, List<string> codes)
        {
            var result = new List<ExternalEventPayload>();
            if (string.IsNullOrEmpty(json) || codes == null || codes.Count == 0) return result;
            string t = json.TrimStart('\uFEFF', '\u200B', ' ', '\r', '\n', '\t');
            if (!t.StartsWith("[", StringComparison.Ordinal)) return result;

            OmRoot root;
            try
            {
                root = JsonUtility.FromJson<OmRoot>("{\"items\":" + t + "}");
            }
            catch
            {
                return result;
            }
            if (root?.items == null) return result;

            string bucket = Bucket();
            for (int i = 0; i < root.items.Length && i < codes.Count; i++)
            {
                var cur = root.items[i]?.current;
                if (cur == null) continue;
                string code = codes[i];
                if (!TryEvaluate(cur, code, bucket, out var payload)) continue;
                result.Add(payload);
            }
            return result;
        }

        /// <summary>同机场同现象 12 小时一档（现实时间，仅用于去重 id）。</summary>
        static string Bucket()
        {
            var now = DateTime.UtcNow;
            return now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "_" + (now.Hour / 12);
        }

        static bool TryEvaluate(OmCurrent c, string code, string bucket, out ExternalEventPayload payload)
        {
            payload = null;
            if (c == null || string.IsNullOrEmpty(code)) return false;

            float windMs = c.wind_speed_10m / 3.6f;   // Open-Meteo 风速单位 km/h
            float vis = c.visibility;
            float rain = c.precipitation;
            int wmo = c.weather_code;

            string kind = null;
            string effectType = null;
            float value = 0f;
            int hours = 0;

            if (windMs >= 20.8f) { kind = "强风"; effectType = "freezeFlights"; hours = 6; }
            else if (vis > 0f && vis <= 800f) { kind = "低能见度"; effectType = "freezeFlights"; hours = 6; }
            else if (wmo >= 95) { kind = "雷暴"; effectType = "onTime"; value = -6f; }
            else if (windMs >= 13.9f) { kind = "大风"; effectType = "onTime"; value = -5f; }
            else if (rain >= 5f) { kind = "强降水"; effectType = "onTime"; value = -4f; }
            else if (vis > 0f && vis <= 2500f) { kind = "能见度低"; effectType = "onTime"; value = -4f; }
            else if (IsSnow(wmo)) { kind = "降雪"; effectType = "onTime"; value = -5f; }

            if (kind == null) return false;

            string label = AirportCatalog.Label(code);
            string title = $"{label} {kind}（气象实况）";
            string body = $"气象实况：风速 {windMs:F1} m/s，降水 {rain:F1} mm/h，能见度 {vis:F0} m"
                          + (wmo > 0 ? $"，天气代码 {wmo}" : "") + "。";
            if (effectType == "freezeFlights")
                body += "该场站起降条件低于运行标准，相关班次按运行限制处理。";
            else
                body += "该场站运行效率下降，准点率承压。";

            payload = new ExternalEventPayload
            {
                id = "wx_" + code + "_" + KindKey(kind) + "_" + bucket,
                category = "季节",
                title = title,
                content = body,
                source = "气象实况",
                publishedAt = DateTime.UtcNow,
                effectType = effectType,
                effectValue = value,
                affectedRegion = effectType == "freezeFlights" ? code : "",
                durationHours = hours,
                chainId = "",
                impactNote = "机场气象实况自动判定（非新闻稿）"
            };
            return true;
        }

        static string KindKey(string kind)
        {
            switch (kind)
            {
                case "强风": return "wind";
                case "低能见度": return "lifr";
                case "雷暴": return "ts";
                case "大风": return "windy";
                case "强降水": return "rain";
                case "能见度低": return "ifr";
                default: return "snow";
            }
        }

        static bool IsSnow(int wmo)
        {
            return (wmo >= 71 && wmo <= 77) || wmo == 85 || wmo == 86;
        }

        static async Task<string> DownloadTextAsync(string url)
        {
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
                    using (var resp = await http.GetAsync(url).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return string.IsNullOrEmpty(text) ? null : text;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                _enabled = PlayerPrefs.GetInt(PrefEnabled, 1) != 0;
                _intervalSeconds = PlayerPrefs.GetInt(PrefInterval, DefaultIntervalSeconds);
            }
            catch
            {
                _enabled = true;
                _intervalSeconds = DefaultIntervalSeconds;
            }
        }
    }
}
