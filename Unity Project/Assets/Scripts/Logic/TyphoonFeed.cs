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
    /// 台风实时管道（GDB 29 补充）：**新闻源不报道 ≠ 没有台风**。
    /// <para>
    /// 实测教训：2026 年第 30 号台风「蔷琵」（CHAMPI）已达超强台风级并向西北偏西逼近，
    /// 但中新网滚动/财经与新浪 7×24 共 500 余条快讯里一条都没有 —— 它离中国大陆还远，
    /// 国内新闻尚未起量。只靠新闻关键词，「台风真要来了」这件事会整条漏掉。
    /// </para>
    /// <para>
    /// 数据源：中央气象台台风网公开 JSON（无 Key、大陆直连，**UTF-8 编码，响应头不带 charset**）：
    /// ① <c>typhoon/jsons/list_default</c> —— 当前编号台风的生死状态（start = 在编）；
    /// ② <c>typhoon/jsons/view_{id}</c> —— 逐 3 小时实况点 + 官方 12/24/…/120 小时预报路径。
    /// 命中规则：把实况与预报点换算成大圆距离，与玩家基地/在飞机场比对 ——
    /// ≤ <see cref="CloseKm"/> 冻结该机场 24 小时，≤ <see cref="NearKm"/> 只降准点率，更远则只作情报入箱。
    /// </para>
    /// <para>
    /// 每场台风每现实日最多入箱 1 封（id 带公告日期）；失败静默，不影响新闻/行情/气象三条管道。
    /// </para>
    /// </summary>
    public static class TyphoonFeed
    {
        public const string PrefEnabled = "ext.typhoon.enabled";
        public const string PrefInterval = "ext.typhoon.interval";

        public const string ListUrl = "https://typhoon.nmc.cn/weatherservice/typhoon/jsons/list_default";
        public const string ViewUrlPrefix = "https://typhoon.nmc.cn/weatherservice/typhoon/jsons/view_";

        public const int DefaultIntervalSeconds = 3600;
        const int MaxTyphoons = 3;      // 一次最多追几场（国内同期在编通常 1~3 场）
        const int MaxAirports = 8;      // 参与距离比对的机场数上限
        const int TimeoutSeconds = 12;

        /// <summary>实况/预报点落在该半径内：按机场关闭处理（冻结 24 小时）。</summary>
        public const float CloseKm = 300f;
        /// <summary>落在该半径内：按大面积延误处理（准点率 −5）。</summary>
        public const float NearKm = 600f;

        /// <summary>
        /// 只对「48 小时内」的最近接近落影响：离得还远的台风是情报，不是今天的停飞令。
        /// 台风逐日报文会持续更新，接近到 48 小时内自然转为冻结/延误。
        /// </summary>
        public const int EffectHorizonHours = 48;

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

        /// <summary>主线程排空缓存（不发起网络）。</summary>
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

        /// <summary>发起一轮后台拉取（非阻塞，受间隔约束）。</summary>
        public static void RequestRefresh(GameState gs, bool force = false)
        {
            EnsureLoaded();
            if (!_enabled || gs == null) return;

            var airports = PickAirports(gs);
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
                    string listText = await DownloadTextAsync(ListUrl).ConfigureAwait(false);
                    var active = ParseActive(listText);
                    if (active.Count == 0) note = "当前无在编台风";
                    else
                    {
                        items = new List<ExternalEventPayload>();
                        int n = Mathf.Min(active.Count, MaxTyphoons);
                        for (int i = 0; i < n; i++)
                        {
                            string viewText = await DownloadTextAsync(ViewUrlPrefix + active[i].id)
                                .ConfigureAwait(false);
                            var p = ParseView(viewText, active[i], airports);
                            if (p != null) items.Add(p);
                        }
                        note = items.Count == 0 ? $"{active.Count} 场在编 · 无可用路径" : $"{items.Count} 场在编";
                    }
                }
                catch
                {
                    // 静默：台风源失败不影响其它管道
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

        static List<string> PickAirports(GameState gs) => NetworkAirports.Pick(gs, MaxAirports);

        // ———— 编码 ————

        /// <summary>
        /// 站点编码随改版变过：早期是 GBK，2026-10 起两点位都是 <b>UTF-8</b>，
        /// 且响应头是 <c>application/octet-stream</c>、不带 charset —— 只能自己判。
        /// 判法：先按「严格 UTF-8」解（遇到非法字节序列会抛异常），失败再退回 936（GBK）。
        /// 反过来固定按 936 解，UTF-8 的「蔷琵」会变成「钄风惖」这种三字乱码（实测踩过）。
        /// </summary>
        static async Task<string> DownloadTextAsync(string url)
        {
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
                    try { http.DefaultRequestHeaders.Referrer = new Uri("https://typhoon.nmc.cn/"); } catch { }
                    using (var resp = await http.GetAsync(url).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        return DecodeText(bytes);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>先严格 UTF-8，失败再 GBK；都失败退回宽松 UTF-8（至少不抛）。</summary>
        public static string DecodeText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            int start = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) start = 3; // BOM
            try { return StrictUtf8.GetString(bytes, start, bytes.Length - start); }
            catch (Exception) { }
            try { return Encoding.GetEncoding(936).GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes, start, bytes.Length - start); }
        }

        // ———— 解析：编号台风清单 ————

        public struct ActiveTyphoon
        {
            public string id;
            public string nameEn;
            public string nameCn;
            public string number;
        }

        static readonly Regex RxListEntry = new Regex(
            @"\[(\d{6,}),""([^""]+)"",""([^""]*)""", RegexOptions.Compiled);

        /// <summary>
        /// 解析 <c>list_default</c>：只留 <c>"start"</c>（在编）的台风。
        /// 条目是扁平数组，逐条截到第一个 <c>]</c> 即可判状态，不必请 JSON 解析器。
        /// </summary>
        public static List<ActiveTyphoon> ParseActive(string text)
        {
            var result = new List<ActiveTyphoon>();
            if (string.IsNullOrEmpty(text)) return result;

            var ms = RxListEntry.Matches(text);
            for (int i = 0; i < ms.Count; i++)
            {
                int start = ms[i].Index;
                int end = text.IndexOf(']', start);
                if (end < 0) continue;
                string entry = text.Substring(start, end - start + 1);
                if (!entry.EndsWith("\"start\"]", StringComparison.Ordinal)) continue;

                string num = "";
                var n = Regex.Match(entry, @"""?(\d{4,})""?,\s*""?\d*""?,");
                if (n.Success) num = n.Groups[1].Value;

                result.Add(new ActiveTyphoon
                {
                    id = ms[i].Groups[1].Value,
                    nameEn = ms[i].Groups[2].Value.Trim(),
                    nameCn = ms[i].Groups[3].Value.Trim(),
                    number = num
                });
            }
            return result;
        }

        // ———— 解析：路径与预报 ————

        static readonly Regex RxPoint = new Regex(
            @"\[(\d{6,}),""(\d{12})"",\d+,""([A-Za-z]+)"",(-?[\d.]+),(-?[\d.]+),(\d+),(\d+),""([A-Z]+)"",(\d+),",
            RegexOptions.Compiled);

        static readonly Regex RxForecast = new Regex(
            @"\[(\d+),""(\d{12})"",(-?[\d.]+),(-?[\d.]+),(\d+),(\d+),""BABJ"",""([A-Za-z]+)""\]",
            RegexOptions.Compiled);

        /// <summary>一场台风的实况 + 官方预报（只保留最新一条实况点的对应预报）。</summary>
        public struct Track
        {
            public string timeText;     // yyyyMMddHHmm（北京时间）
            public string level;        // TD/TS/STS/TY/STY/SuperTY
            public float lon, lat;
            public int pressure;        // hPa
            public int wind;            // m/s
            public string dir;          // 移动方向
            public int speed;           // km/h
            public List<(int hour, float lon, float lat, string level)> forecast;
        }

        public static bool ParseTrack(string text, out Track track)
        {
            track = default;
            if (string.IsNullOrEmpty(text)) return false;

            var pts = RxPoint.Matches(text);
            if (pts.Count == 0) return false;
            var last = pts[pts.Count - 1];   // 报文按时间升序：最后一条即最新实况
            track.timeText = last.Groups[2].Value;
            track.level = last.Groups[3].Value;
            track.lon = Float(last.Groups[4].Value);
            track.lat = Float(last.Groups[5].Value);
            track.pressure = Int(last.Groups[6].Value);
            track.wind = Int(last.Groups[7].Value);
            track.dir = last.Groups[8].Value;
            track.speed = Int(last.Groups[9].Value);
            track.forecast = new List<(int, float, float, string)>();

            // 每个实况点都自带一份预报：只取基准时刻 = 最新实况时刻的那一份，避免把旧预报当新预报
            var fcs = RxForecast.Matches(text);
            for (int i = 0; i < fcs.Count; i++)
            {
                if (!string.Equals(fcs[i].Groups[2].Value, track.timeText, StringComparison.Ordinal)) continue;
                track.forecast.Add((Int(fcs[i].Groups[1].Value),
                    Float(fcs[i].Groups[3].Value), Float(fcs[i].Groups[4].Value),
                    fcs[i].Groups[7].Value));
            }
            track.forecast.Sort((a, b) => a.hour.CompareTo(b.hour));
            return true;
        }

        /// <summary>把一场台风折算成一条可入箱的外部来文（公开以便离线自测）。</summary>
        public static ExternalEventPayload ParseView(string text, ActiveTyphoon ty, List<string> airports)
        {
            if (!ParseTrack(text, out var tr)) return null;

            // 最近威胁：在所有实况/预报点 × 在飞机场里取最小距离
            string nearCode = "";
            float nearKm = float.MaxValue;
            int nearHour = 0;
            for (int a = 0; a < airports.Count; a++)
            {
                if (!AirportGeoCatalog.TryLonLat(airports[a], out float alon, out float alat)) continue;
                float d0 = AirportGeoCatalog.PointKm(tr.lon, tr.lat, alon, alat);
                if (d0 < nearKm) { nearKm = d0; nearCode = airports[a]; nearHour = 0; }
                for (int f = 0; f < tr.forecast.Count; f++)
                {
                    var fp = tr.forecast[f];
                    float d = AirportGeoCatalog.PointKm(fp.lon, fp.lat, alon, alat);
                    if (d < nearKm) { nearKm = d; nearCode = airports[a]; nearHour = fp.hour; }
                }
            }

            string levelZh = LevelZh(tr.level);
            string name = string.IsNullOrEmpty(ty.nameCn) ? ty.nameEn : ty.nameCn;
            // 编号形如 "2630"（第 30 号）；"20260027" 这类无名热带低压不报编号
            string numberTag = (ty.number != null && ty.number.Length == 4)
                ? $"第{ty.number.Substring(2)}号" : "";

            var body = new StringBuilder();
            body.Append($"中央气象台监测：{numberTag}台风“{name}”（{levelZh}）中心位于北纬{tr.lat:F1}度、东经{tr.lon:F1}度，")
                .Append($"中心附近最大风力{tr.wind}米/秒（{Beaufort(tr.wind)}级），中心气压{tr.pressure}百帕，")
                .Append($"正以每小时{tr.speed}公里的速度向{DirZh(tr.dir)}方向移动。");
            if (tr.forecast.Count > 0)
            {
                var f72 = tr.forecast.Find(x => x.hour >= 72);
                var f24 = tr.forecast.Find(x => x.hour >= 24);
                body.Append("\n官方预报：");
                if (f24.hour > 0)
                    body.Append($"未来 24 小时（{LevelZh(f24.level)}）中心位于北纬{f24.lat:F1}度、东经{f24.lon:F1}度；");
                if (f72.hour > 0)
                    body.Append($"未来 72 小时（{LevelZh(f72.level)}）中心位于北纬{f72.lat:F1}度、东经{f72.lon:F1}度；");
                body.Append("路径与强度以最新公报为准。");
            }
            if (!string.IsNullOrEmpty(nearCode))
            {
                body.Append(nearKm <= CloseKm
                    ? $"\n距离本公司在飞机场 {nearCode} 最近约 {nearKm:F0} 公里（{HourText(nearHour)}），已进入关闭级影响范围。"
                    : nearKm <= NearKm
                        ? $"\n距离本公司在飞机场 {nearCode} 最近约 {nearKm:F0} 公里（{HourText(nearHour)}），处于延误高发范围。"
                        : $"\n与本公司基地/在飞机场的最近距离约 {nearKm:F0} 公里（{nearCode} · {HourText(nearHour)}），暂无直接影响。");
                if (nearHour > EffectHorizonHours && nearKm <= NearKm)
                    body.Append($"该时点距今仍超过 {EffectHorizonHours} 小时，本封仅作提前预警，届时以最新公报为准。");
            }

            var payload = new ExternalEventPayload
            {
                id = $"tyc_{ty.id}_{tr.timeText.Substring(0, Mathf.Min(8, tr.timeText.Length))}",
                category = "气象",
                title = $"台风“{name}”（{levelZh}）{DirZh(tr.dir)}移动，中心气压 {tr.pressure} 百帕",
                content = body.ToString(),
                source = "中央气象台",
                publishedAt = ParseBeijing(tr.timeText),
                effectType = "",
                effectValue = 0f,
                affectedRegion = "",
                durationHours = 0,
                chainId = ""
            };

            bool imminent = nearHour <= EffectHorizonHours;   // 超出 48 小时只作情报
            if (!string.IsNullOrEmpty(nearCode) && nearKm <= CloseKm && imminent)
            {
                payload.effectType = "freezeFlights";
                payload.affectedRegion = nearCode;
                payload.durationHours = 24;
                payload.impactNote = $"官方路径 {HourText(nearHour)}最近 {nearKm:F0} 公里（≤{CloseKm:F0}），按机场关闭 24 小时处理";
                payload.inferred = true;
            }
            else if (!string.IsNullOrEmpty(nearCode) && nearKm <= NearKm && imminent)
            {
                payload.effectType = "onTime";
                payload.effectValue = -5f;
                payload.impactNote = $"官方路径 {HourText(nearHour)}最近 {nearKm:F0} 公里（≤{NearKm:F0}），按准点率 −5 个百分点处理";
                payload.inferred = true;
            }
            else if (!string.IsNullOrEmpty(nearCode) && !imminent && nearKm <= NearKm)
            {
                // 未进入影响窗口：不加数值影响，正文里已写明「仅作提前预警」
            }
            return payload;
        }

        static string HourText(int hour) => hour <= 0 ? "当前" : $"{hour} 小时内";

        static string LevelZh(string level)
        {
            switch ((level ?? "").ToUpperInvariant())
            {
                case "TD": return "热带低压";
                case "TS": return "热带风暴";
                case "STS": return "强热带风暴";
                case "TY": return "台风";
                case "STY": return "强台风";
                case "SUPERTY": return "超强台风";
                default: return "热带气旋";
            }
        }

        /// <summary>米/秒 → 蒲福风级（只给 CEO 看的量级描述）。</summary>
        static string Beaufort(int ms)
        {
            if (ms >= 51) return "16 级以上";
            if (ms >= 41) return "14";
            if (ms >= 33) return "12";
            if (ms >= 25) return "10";
            if (ms >= 17) return "8";
            return "6";
        }

        static string DirZh(string dir)
        {
            switch ((dir ?? "").ToUpperInvariant())
            {
                case "N": return "北";
                case "NNE": return "北偏东";
                case "NE": return "东北";
                case "ENE": return "东偏北";
                case "E": return "东";
                case "ESE": return "东偏南";
                case "SE": return "东南";
                case "SSE": return "南偏东";
                case "S": return "南";
                case "SSW": return "南偏西";
                case "SW": return "西南";
                case "WSW": return "西偏南";
                case "W": return "西";
                case "WNW": return "西偏北";
                case "NW": return "西北";
                case "NNW": return "北偏西";
                default: return "未知方向";
            }
        }

        /// <summary>yyyyMMddHHmm（北京时间）→ UTC。</summary>
        static DateTime? ParseBeijing(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 12) return null;
            if (!DateTime.TryParseExact(s.Substring(0, 12), "yyyyMMddHHmm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt)) return null;
            return DateTime.SpecifyKind(dt - TimeSpan.FromHours(8), DateTimeKind.Utc);
        }

        static float Float(string s)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        static int Int(string s)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
    }
}
