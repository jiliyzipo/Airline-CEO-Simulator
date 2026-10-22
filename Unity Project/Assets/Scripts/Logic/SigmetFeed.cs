using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

#nullable disable

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 国际航行警告（SIGMET）管道：**真正的「空域管制 / 航空天气」数据**，而不是新闻里的转述。
    /// <para>
    /// 数据源：<c>https://aviationweather.gov/api/data/isigmet?format=json</c>
    /// （美国 FAA/AWC 汇总的全球国际 SIGMET，无 Key、大陆可直连）。
    /// 实测包含中国情报区（ZBPE 北京 / ZSHA 上海 / ZHWH 武汉 / ZJSA 三亚 / ZYSH 沈阳），
    /// 也包含 FUKUOKA、INCHEON、MANILA、BANGKOK、SINGAPORE、TAHITI 等周边与远洋情报区。
    /// </para>
    /// <para>
    /// 与新闻关键词的区别：SIGMET 自带**多边形与高度层**，可以精确判断「这个机场是不是真在警告区内」，
    /// 不需要靠「华东空域」「流量控制」这类模糊措辞去猜。报文原文（英文）一并附在邮件里，
    /// 与真实签派看到的材料一致。
    /// </para>
    /// <para>
    /// 判定：机场落在警告多边形内（或距边界 50 公里内）才生效 ——
    /// 火山灰 / 热带气旋 → 冻结该场站 6 小时；沙暴尘暴 → 准点率 −5；颠簸 / 积冰 / 山地波 / 雷暴 → 准点率 −3~−4。
    /// 同机场同现象 12 小时只投一次，避免一份报文每 3 小时重发就刷一封。
    /// </para>
    /// </summary>
    public static class SigmetFeed
    {
        public const string PrefEnabled = "ext.sigmet.enabled";
        public const string PrefInterval = "ext.sigmet.interval";
        public const string SourceUrl = "https://aviationweather.gov/api/data/isigmet?format=json";

        public const int DefaultIntervalSeconds = 1800;
        const int MaxAirports = 8;
        const int MaxEventsPerPull = 2;   // 雷雨季一天可能有十几条 TS SIGMET，只投最重的两条
        const int TimeoutSeconds = 15;
        /// <summary>机场不在多边形内、但距边界小于该值时也算受影响（多边形画得比实际范围小是常态）。</summary>
        const float EdgeToleranceKm = 50f;

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
                _status = "";
                _lastAttemptUtc = DateTime.MinValue;
            }
        }

        public static List<ExternalEventPayload> Drain()
        {
            lock (Gate)
            {
                if (Inbox.Count == 0) return null;
                var list = new List<ExternalEventPayload>(Inbox);
                Inbox.Clear();
                return list;
            }
        }

        public static void RequestRefresh(GameState gs, bool force = false)
        {
            EnsureLoaded();
            if (!_enabled || gs == null) return;

            var airports = NetworkAirports.Pick(gs, MaxAirports);
            if (airports.Count == 0) return;

            lock (Gate)
            {
                if (_inFlight) return;
                if (!force && _intervalSeconds <= 0) return;
                if (!force && (DateTime.UtcNow - _lastAttemptUtc).TotalSeconds < _intervalSeconds) return;
                _inFlight = true;
                _lastAttemptUtc = DateTime.UtcNow;
            }

            Task.Run(async () =>
            {
                List<ExternalEventPayload> items = null;
                string note = "不可达";
                try
                {
                    string text = await DownloadAsync(SourceUrl).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var parsed = Parse(text, airports);
                        items = Trim(parsed, MaxEventsPerPull);
                        note = $"{parsed.Count} 条影响本网络";
                    }
                }
                catch
                {
                    // 静默：SIGMET 源失败不影响新闻/行情/气象
                }
                finally
                {
                    lock (Gate)
                    {
                        _inFlight = false;
                        if (items != null && items.Count > 0) Inbox.AddRange(items);
                        _status = $"{DateTime.Now:MM-dd HH:mm} {note}";
                    }
                }
            });
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _enabled = PlayerPrefs.GetInt(PrefEnabled, 1) != 0;
            int iv = PlayerPrefs.GetInt(PrefInterval, DefaultIntervalSeconds);
            _intervalSeconds = iv < 0 ? DefaultIntervalSeconds : iv;
        }

        static async Task<string> DownloadAsync(string url)
        {
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                    using (var resp = await http.GetAsync(url).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        string s = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return string.IsNullOrEmpty(s) ? null : s;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        // ———— 契约（aviationweather.gov isigmet）————

        [Serializable]
        public class SigCoord
        {
            public float lon;
            public float lat;
        }

        [Serializable]
        class SigItem
        {
            public string icaoId;
            public string firId;
            public string firName;
            public long validTimeFrom;
            public long validTimeTo;
            public string seriesId;
            public string hazard;
            public string qualifier;
            public int @base;
            public int top;
            public string geom;
            public SigCoord[] coords;
            public string dir;
            public string spd;
            public string chng;
            public string rawSigmet;
        }

        [Serializable]
        class SigList
        {
            public SigItem[] items;
        }

        /// <summary>解析并折算成可入箱来文（公开以便离线自测）。</summary>
        public static List<ExternalEventPayload> Parse(string json, List<string> airports)
        {
            var result = new List<ExternalEventPayload>();
            if (string.IsNullOrEmpty(json) || airports == null || airports.Count == 0) return result;

            SigItem[] items;
            try
            {
                // 顶层是数组：包一层对象后交给 JsonUtility
                var wrap = JsonUtility.FromJson<SigList>("{\"items\":" + json + "}");
                items = wrap?.items;
            }
            catch
            {
                return result;
            }
            if (items == null) return result;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string bucket = Bucket();
            var seen = new HashSet<string>();

            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i];
                if (it == null) continue;
                if (it.validTimeTo > 0 && it.validTimeTo < now) continue;  // 已过期
                if (it.coords == null || it.coords.Length < 2) continue;

                // 现象先归类：不关心的直接跳过，省掉后面的几何计算
                if (!EffectOf(it.hazard, out string kindZh, out string effectType, out float value, out int hours))
                    continue;

                for (int a = 0; a < airports.Count; a++)
                {
                    string code = airports[a];
                    if (!AirportGeoCatalog.TryLonLat(code, out float lon, out float lat)) continue;
                    if (!Hits(code, lon, lat, it.coords, it.geom)) continue;

                    string key = code + "|" + it.hazard + "|" + bucket;
                    if (!seen.Add(key)) continue;

                    var p = Build(it, code, kindZh, effectType, value, hours, bucket);
                    if (p != null) result.Add(p);
                }
            }

            // 重的在前：火山灰/热带气旋 > 沙暴 > 颠簸积冰 > 雷暴
            result.Sort((x, y) => Weight(y).CompareTo(Weight(x)));
            return result;
        }

        /// <summary>机场是否落在警告区（多边形内，或在线/面的 50 公里缓冲带内）。</summary>
        public static bool Hits(string code, float lon, float lat, SigCoord[] poly, string geom)
        {
            if (poly == null || poly.Length < 2) return false;

            bool isLine = !string.IsNullOrEmpty(geom)
                          && geom.IndexOf("LINE", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isLine && poly.Length >= 3 && InPolygon(lon, lat, poly)) return true;

            // 边界缓冲：顶点距离 + 每段中点距离（够用且不会误判大范围多边形）
            for (int i = 0; i < poly.Length; i++)
            {
                if (AirportGeoCatalog.PointKm(lon, lat, poly[i].lon, poly[i].lat) <= EdgeToleranceKm) return true;
                int j = (i + 1) % poly.Length;
                if (isLine && i == poly.Length - 1) break;
                float midLon = (poly[i].lon + poly[j].lon) * 0.5f;
                float midLat = (poly[i].lat + poly[j].lat) * 0.5f;
                if (AirportGeoCatalog.PointKm(lon, lat, midLon, midLat) <= EdgeToleranceKm) return true;
            }
            return false;
        }

        /// <summary>射线法：经纬度平面内近似（警告区尺度小，误差可忽略）。</summary>
        public static bool InPolygon(float lon, float lat, SigCoord[] poly)
        {
            if (poly == null || poly.Length < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                float xi = poly[i].lon, yi = poly[i].lat;
                float xj = poly[j].lon, yj = poly[j].lat;
                if ((yi > lat) == (yj > lat)) continue;
                float xCross = (xj - xi) * (lat - yi) / (yj - yi) + xi;
                if (lon < xCross) inside = !inside;
            }
            return inside;
        }

        static ExternalEventPayload Build(SigItem it, string code, string kindZh,
            string effectType, float value, int hours, string bucket)
        {
            string label = AirportCatalog.Label(code);
            string fir = string.IsNullOrEmpty(it.firName) ? (it.firId ?? "情报区") : it.firName;
            string alt = AltRange(it.@base, it.top);
            string window = ValidWindow(it.validTimeFrom, it.validTimeTo);

            var body = new StringBuilder();
            body.Append($"{fir} 情报区发布 {kindZh} 航行警告：")
                .Append(string.IsNullOrEmpty(it.qualifier) ? "影响范围见报文" : it.qualifier)
                .Append($"；高度 {alt}；有效期 {window}。");
            if (!string.IsNullOrEmpty(it.dir) && !string.IsNullOrEmpty(it.spd))
                body.Append($"移向 {it.dir}，移速 {it.spd} 节。");
            body.Append($"\n本公司在飞机场 {label} 位于该警告区影响范围内。");
            body.Append(effectType == "freezeFlights"
                ? "\n按运行标准，相关班次在该场站恢复正常前不予放行。"
                : "\n该场站运行效率下降，准点率承压。");
            if (!string.IsNullOrEmpty(it.rawSigmet))
            {
                body.Append("\n\n—— 报文原文（SIGMET）——\n");
                body.Append(Regex.Replace(it.rawSigmet.Trim(), @"[ \t]+", " "));
            }

            return new ExternalEventPayload
            {
                // 同机场同现象 12 小时一封：报文每 3 小时重发，不能让收件箱跟着刷
                id = $"sig_{code}_{it.hazard}_{bucket}",
                category = "气象",
                title = $"{label} {kindZh}航行警告（{fir}）",
                content = body.ToString(),
                source = "国际航行警告",
                publishedAt = FromUnix(it.validTimeFrom),
                effectType = effectType,
                effectValue = value,
                affectedRegion = effectType == "freezeFlights" ? code : "",
                durationHours = hours,
                chainId = "",
                inferred = true,
                impactNote = $"机场 {code} 位于 SIGMET 多边形内（{kindZh} · {fir}），按运行限制处理"
            };
        }

        static List<ExternalEventPayload> Trim(List<ExternalEventPayload> list, int max)
        {
            if (list == null || list.Count == 0) return list;
            if (list.Count <= max) return list;
            return list.GetRange(0, max);
        }

        static int Weight(ExternalEventPayload p)
        {
            string t = p?.title ?? "";
            if (t.Contains("火山灰")) return 50;
            if (t.Contains("热带气旋")) return 40;
            if (t.Contains("沙尘暴")) return 30;
            if (t.Contains("颠簸") || t.Contains("积冰")) return 20;
            if (t.Contains("雷暴")) return 10;
            return 0;
        }

        /// <summary>现象 → 中文名 + 游戏影响；返回 false 表示这条不关心（不产生来文）。</summary>
        static bool EffectOf(string hazard, out string kindZh, out string effectType, out float value, out int hours)
        {
            kindZh = "";
            effectType = "";
            value = 0f;
            hours = 0;
            switch ((hazard ?? "").Trim().ToUpperInvariant())
            {
                case "VA":
                    kindZh = "火山灰"; effectType = "freezeFlights"; hours = 6; return true;
                case "TC":
                    kindZh = "热带气旋"; effectType = "freezeFlights"; hours = 6; return true;
                case "DS":
                case "SS":
                    kindZh = "沙尘暴"; effectType = "onTime"; value = -5f; return true;
                case "SEV_TURB":
                case "TURB":
                    kindZh = "严重颠簸"; effectType = "onTime"; value = -4f; return true;
                case "SEV_ICE":
                case "ICE":
                    kindZh = "严重积冰"; effectType = "onTime"; value = -4f; return true;
                case "SEV_MTW":
                case "MTW":
                    kindZh = "山地波"; effectType = "onTime"; value = -3f; return true;
                case "TS":
                    kindZh = "雷暴"; effectType = "onTime"; value = -3f; return true;
                default:
                    return false;
            }
        }

        static string AltRange(int baseFt, int topFt)
        {
            string b = baseFt <= 0 ? "地面" : "FL" + (baseFt / 100);
            string t = topFt <= 0 ? "不限" : "FL" + (topFt / 100);
            if (topFt > 0 && topFt == 99900) t = "不限";
            return b + " ~ " + t;
        }

        static string ValidWindow(long fromUnix, long toUnix)
        {
            if (fromUnix <= 0 || toUnix <= 0) return "见报文";
            var from = FromUnix(fromUnix) ?? DateTime.UtcNow;
            var to = FromUnix(toUnix) ?? DateTime.UtcNow;
            // 隐去年份，只留「日期 + 时刻」；游戏本身不做现实日期换算，这里也只用相对时长
            double h = (to - from).TotalHours;
            return $"{h:F0} 小时（{from:MM-dd HH:mm}~{to:MM-dd HH:mm} UTC）";
        }

        static DateTime? FromUnix(long unix)
        {
            if (unix <= 0) return null;
            try { return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime; }
            catch { return null; }
        }

        static string Bucket()
        {
            var now = DateTime.UtcNow;
            return now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "_" + (now.Hour / 12);
        }
    }
}
