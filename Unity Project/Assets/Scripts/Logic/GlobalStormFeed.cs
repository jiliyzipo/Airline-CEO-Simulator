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
    /// 全球热带气旋管道（NASA EONET）。
    /// <para>
    /// 为什么需要它：中央气象台只编报**西北太平洋与南海**的编号台风。大西洋飓风、
    /// 东北太平洋飓风、北印度洋气旋、南半球气旋都不在其中 —— 玩家的国际航线
    /// （如 LHR–JFK、SYD–AKL、DEL–DXB）遇到的那一类风暴，靠 NMC 永远收不到。
    /// 实测同一时刻：NMC 只有「蔷琵」一场，而 EONET 同时在报
    /// Champi（西北太平洋）、Trudy（东北太平洋）、Wati（南太平洋）等 7 场。
    /// </para>
    /// <para>
    /// 数据源：<c>https://eonet.gsfc.nasa.gov/api/v3/events?status=open&amp;category=severeStorms</c>
    /// （NASA 地球观测网络，无 Key、JSON、大陆可直连）。
    /// </para>
    /// <para>
    /// 分工：西北太平洋（lon ≥ 100）交给 <see cref="TyphoonFeed"/>（有官方预报路径，更准），
    /// 这里只补它覆盖不到的海域，避免同一场台风收两封。
    /// 判定：定位点距在飞机场 ≤300 公里 → 冻结 24 小时；≤600 公里 → 准点率 −5；更远不入箱。
    /// 定位点停更超过 72 小时的「僵尸事件」（EONET 会长期挂 open）直接丢弃。
    /// </para>
    /// </summary>
    public static class GlobalStormFeed
    {
        public const string PrefEnabled = "ext.storm.enabled";
        public const string PrefInterval = "ext.storm.interval";
        public const string SourceUrl =
            "https://eonet.gsfc.nasa.gov/api/v3/events?status=open&category=severeStorms";

        public const int DefaultIntervalSeconds = 3600;
        const int MaxAirports = 8;
        const int MaxEventsPerPull = 2;
        const int TimeoutSeconds = 15;
        /// <summary>定位点过期判定（小时）：EONET 会把已消散的事件长期挂 open。</summary>
        const double StaleHours = 72;

        public const float CloseKm = 300f;
        public const float NearKm = 600f;

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
                        var parsed = Parse(text, airports, DateTime.UtcNow);
                        items = Trim(parsed, MaxEventsPerPull);
                        note = $"{parsed.Count} 场影响本网络";
                    }
                }
                catch
                {
                    // 静默：全球风暴源失败不影响其它管道
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

        // ———— 解析 ————
        //
        // 不用 JsonUtility：EONET 的 geometry 可能是 Point，也可能是 Polygon（坐标是三层数组），
        // JsonUtility 遇到混合类型会把整份文档解析失败。只用正则取 Point 条目，其余直接跳过。

        static readonly Regex RxEventId = new Regex(@"""id"":\s*""(EONET_[^""]+)""", RegexOptions.Compiled);
        static readonly Regex RxTitle = new Regex(@"""title"":\s*""([^""]*)""", RegexOptions.Compiled);
        static readonly Regex RxPoint = new Regex(
            @"""date"":\s*""([^""]+)"",\s*""type"":\s*""Point"",\s*""coordinates"":\s*\[\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\]",
            RegexOptions.Compiled);

        public struct Storm
        {
            public string id;
            public string title;
            public DateTime whenUtc;
            public float lon;
            public float lat;
        }

        /// <summary>解析 EONET 事件列表；同一事件取**最新**定位点（EONET 按时间升序排列）。</summary>
        public static List<Storm> ParseStorms(string json)
        {
            var result = new List<Storm>();
            if (string.IsNullOrEmpty(json)) return result;

            var ids = RxEventId.Matches(json);
            for (int i = 0; i < ids.Count; i++)
            {
                int start = ids[i].Index;
                int end = i + 1 < ids.Count ? ids[i + 1].Index : json.Length;
                if (end <= start) continue;
                string chunk = json.Substring(start, end - start);

                var t = RxTitle.Match(chunk);
                string title = t.Success ? t.Groups[1].Value.Trim() : "";
                if (string.IsNullOrEmpty(title)) continue;

                var pts = RxPoint.Matches(chunk);
                if (pts.Count == 0) continue;
                var last = pts[pts.Count - 1];

                if (!DateTime.TryParse(last.Groups[1].Value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
                    continue;
                if (!float.TryParse(last.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float lon))
                    continue;
                if (!float.TryParse(last.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float lat))
                    continue;

                result.Add(new Storm
                {
                    id = ids[i].Groups[1].Value,
                    title = title,
                    whenUtc = when,
                    lon = lon,
                    lat = lat
                });
            }
            return result;
        }

        /// <summary>折算成可入箱来文（公开以便离线自测）。</summary>
        public static List<ExternalEventPayload> Parse(string json, List<string> airports, DateTime nowUtc)
        {
            var result = new List<ExternalEventPayload>();
            if (airports == null || airports.Count == 0) return result;
            var storms = ParseStorms(json);

            for (int i = 0; i < storms.Count; i++)
            {
                var s = storms[i];
                if ((nowUtc - s.whenUtc).TotalHours > StaleHours) continue;      // 僵尸事件
                if (s.whenUtc - nowUtc > TimeSpan.FromHours(6)) continue;        // 时间戳异常
                if (IsWestPacific(s.lon, s.lat)) continue;                      // 交给中央气象台，避免重复

                string nearCode = "";
                float nearKm = float.MaxValue;
                for (int a = 0; a < airports.Count; a++)
                {
                    if (!AirportGeoCatalog.TryLonLat(airports[a], out float alon, out float alat)) continue;
                    float d = AirportGeoCatalog.PointKm(s.lon, s.lat, alon, alat);
                    if (d < nearKm) { nearKm = d; nearCode = airports[a]; }
                }
                if (string.IsNullOrEmpty(nearCode) || nearKm > NearKm) continue;

                result.Add(Build(s, nearCode, nearKm));
            }
            return result;
        }

        static ExternalEventPayload Build(Storm s, string code, float km)
        {
            string label = AirportCatalog.Label(code);
            string basin = BasinName(s.lon, s.lat);
            string kind = NameKind(s.title);      // 飓风 / 台风 / 热带风暴…
            string name = NameBody(s.title);      // 去掉类别词后的专名

            string effectType = km <= CloseKm ? "freezeFlights" : "onTime";
            float value = effectType == "onTime" ? -5f : 0f;
            int hours = effectType == "freezeFlights" ? 24 : 0;

            string latTxt = s.lat >= 0f ? $"北纬 {s.lat:F1} 度" : $"南纬 {-s.lat:F1} 度";
            string lonTxt = s.lon >= 0f ? $"东经 {s.lon:F1} 度" : $"西经 {-s.lon:F1} 度";

            var body = new StringBuilder();
            body.Append($"NASA 全球风暴监测：{basin}{kind}“{name}”中心位于{latTxt}、{lonTxt}，")
                .Append($"距本公司在飞机场 {label} 约 {km:F0} 公里。");
            body.Append(km <= CloseKm
                ? "\n已进入关闭级影响范围：该场站按运行限制处理，受影响班次在恢复正常前不予放行。"
                : "\n处于延误高发范围：该场站运行效率下降，准点率承压。");
            body.Append("\n（西北太平洋编号台风由中央气象台公报单独跟进，本封只报其覆盖不到的海域。）");

            return new ExternalEventPayload
            {
                id = $"storm_{s.id}_{s.whenUtc:yyyyMMdd}",
                category = "气象",
                title = $"{basin}{kind}“{name}”逼近 {label}（约 {km:F0} 公里）",
                content = body.ToString(),
                source = "全球风暴监测",
                publishedAt = s.whenUtc,
                effectType = effectType,
                effectValue = value,
                affectedRegion = effectType == "freezeFlights" ? code : "",
                durationHours = hours,
                chainId = "",
                inferred = true,
                impactNote = $"EONET 定位点距 {code} 约 {km:F0} 公里，按距离阈值判定"
            };
        }

        static List<ExternalEventPayload> Trim(List<ExternalEventPayload> list, int max)
        {
            if (list == null || list.Count == 0) return list;
            if (list.Count <= max) return list;
            return list.GetRange(0, max);
        }

        /// <summary>西北太平洋（含南海）：中央气象台编号台风覆盖区，这里不重复报。</summary>
        public static bool IsWestPacific(float lon, float lat)
            => lat >= 0f && lon >= 100f;

        public static string BasinName(float lon, float lat)
        {
            if (lat < 0f) return "南半球";
            if (lon >= 100f) return "西北太平洋";
            if (lon <= -170f) return "西南太平洋";
            if (lon < -140f) return "中太平洋";
            if (lon < -100f) return "东北太平洋";
            if (lon < -20f) return "大西洋";
            if (lon < 100f) return "北印度洋";
            return "其他海域";
        }

        /// <summary>「Hurricane Trudy」→ 飓风 / 「Super Typhoon Champi」→ 超强台风。</summary>
        public static string NameKind(string title)
        {
            string t = (title ?? "").Trim();
            if (t.StartsWith("Super Typhoon", StringComparison.OrdinalIgnoreCase)) return "超强台风";
            if (t.StartsWith("Typhoon", StringComparison.OrdinalIgnoreCase)) return "台风";
            if (t.StartsWith("Hurricane", StringComparison.OrdinalIgnoreCase)) return "飓风";
            if (t.StartsWith("Post-Tropical Cyclone", StringComparison.OrdinalIgnoreCase)) return "后热带气旋";
            if (t.StartsWith("Tropical Storm", StringComparison.OrdinalIgnoreCase)) return "热带风暴";
            if (t.StartsWith("Tropical Cyclone", StringComparison.OrdinalIgnoreCase)) return "热带气旋";
            if (t.StartsWith("Subtropical Storm", StringComparison.OrdinalIgnoreCase)) return "副热带风暴";
            return "热带气旋";
        }

        public static string NameBody(string title)
        {
            string t = (title ?? "").Trim();
            string[] prefixes =
            {
                "Post-Tropical Cyclone", "Super Typhoon", "Subtropical Storm",
                "Tropical Cyclone", "Tropical Storm", "Hurricane", "Typhoon"
            };
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (t.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                    return t.Substring(prefixes[i].Length).Trim();
            }
            return t;
        }
    }
}
