using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AirlineCEO.Data;
using UnityEngine;

#nullable disable

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 数据输入层（GDB 29 · 全球航空动态消息流）。
    /// <para>
    /// 职责：从现实数据源拉取「事件内容」，交给 <see cref="Systems.ExternalEventManager"/> 映射到游戏时间轴。
    /// 现实只提供内容，游戏提供时间轴；拉取失败 / 无网络 / 未配置 → 静默返回空，交由 EventGenerator 兜底。
    /// </para>
    /// <para>
    /// 线程模型：抓取在后台线程（.NET HttpClient，失败不产生任何 Unity 红字），结果写入待入队缓存；
    /// 主线程通过 <see cref="FetchEvents"/> 排空缓存。网络请求绝不阻塞游戏主线程。
    /// </para>
    /// <para>
    /// 支持三类源（可用「;」分隔多个）：
    /// ① http(s):// JSON —— 契约见 <c>Assets/StreamingAssets/external_events.sample.json</c>（可带 effectType 直接改数值）；
    /// ② http(s):// RSS / Atom —— 通讯社消息稿，只作内容，不带数值影响；
    /// ③ 本地文件（绝对路径或 file://）—— 离线自测用。
    /// </para>
    /// </summary>
    public static class ExternalDataFetcher
    {
        // ———— 配置（PlayerPrefs 持久化；编辑器菜单「AirlineCEO/外部数据源」可改）————
        public const string PrefUrl = "ext.source.url";
        public const string PrefEnabled = "ext.source.enabled";
        public const string PrefInterval = "ext.source.interval";
        public const string PrefMaxPerFetch = "ext.source.maxperfetch";
        public const string PrefFilter = "ext.source.filter";
        public const string PrefDailyBudget = "ext.source.dailybudget";

        // ———— 行情源配置（油价；与新闻源分开，默认开启；玩家可在 n116 关掉）————
        public const string PrefQuoteEnabled = "ext.quote.enabled";
        public const string PrefQuoteUrl = "ext.quote.url";
        public const string PrefQuoteInterval = "ext.quote.interval";
        public const string PrefQuoteCrack = "ext.quote.crack";

        // ———— 汇率源配置（USD/CNY；默认开启，独立于油价）————
        public const string PrefFxEnabled = "ext.fx.enabled";
        public const string PrefFxInterval = "ext.fx.interval";

        /// <summary>
        /// 默认行情源：布伦特原油近月合约。
        /// ① 腾讯行情 <c>qt.gtimg.cn/q=hf_OIL</c> —— 大陆直连、无 Key，字段 0 = 美元/桶；
        /// ② 新浪外盘 <c>hq.sinajs.cn/list=hf_OIL</c> —— 大陆直连（需 sina 域 Referer，代码里已按域补）；
        /// ③ Yahoo 图表接口 —— 海外网络可用，大陆通常不可达。
        /// 任何返回「带 price / value / regularMarketPrice 字段的 JSON」「纯数字」或「腾讯/新浪行情串」的地址都能顶替。
        /// </summary>
        public static readonly string[] DefaultQuoteUrls =
        {
            "https://qt.gtimg.cn/q=hf_OIL",
            "https://hq.sinajs.cn/list=hf_OIL",
            "https://query1.finance.yahoo.com/v8/finance/chart/BZ=F?interval=1d&range=1d"
        };

        /// <summary>默认行情地址（= 首个国内源）。</summary>
        public const string DefaultQuoteUrl = "https://qt.gtimg.cn/q=hf_OIL";

        /// <summary>默认行情间隔（现实秒）：6 小时。0 = 不自动拉取。</summary>
        public const int DefaultQuoteIntervalSeconds = 21600;
        /// <summary>默认裂解价差（美元/吨）：≈19 美元/桶，覆盖原油到航油的加工与运费。</summary>
        public const float DefaultCrackSpreadUsdPerTon = 150f;
        /// <summary>1 吨航油 ≈ 7.82 桶（Jet A-1 密度 ≈0.804 kg/L；1 桶 =158.987 L → 127.8 kg）。</summary>
        public const float BarrelsPerTonJet = 7.82f;

        /// <summary>
        /// 汇率默认地址（按顺序回退，任一可用即止）：
        /// ① 腾讯行情 <c>whUSDCNY</c> —— 大陆直连、无需 Key、返回 <c>v_whUSDCNY="…~6.6792~…"</c>；
        /// ② Frankfurter（欧洲央行口径）—— 每天更新一次，JSON <c>rates.CNY</c>；
        /// ③ open.er-api —— 备用日更源，JSON <c>rates.CNY</c>。
        /// </summary>
        public static readonly string[] DefaultFxUrls =
        {
            "https://qt.gtimg.cn/q=whUSDCNY",
            "https://api.frankfurter.app/latest?from=USD&to=CNY",
            "https://open.er-api.com/v6/latest/USD"
        };

        /// <summary>汇率拉取间隔（现实秒）：6 小时。0 = 不自动拉取。</summary>
        public const int DefaultFxIntervalSeconds = 21600;
        const float FxMin = 5.50f;
        const float FxMax = 9.50f;
        /// <summary>换算式：航油（美元/吨）= 原油（美元/桶）× 7.82 + 裂解价差。</summary>
        const float QuoteMinUsdPerTon = 400f;
        const float QuoteMaxUsdPerTon = 1600f;

        /// <summary>
        /// 国内默认源（**无需 VPN**，大陆网络可直连）：
        /// ① 中新网滚动新闻 RSS（国内综合：政策、天气、突发事件，民航稿随新闻发生自然进入）；
        /// ② 中新网财经 RSS（油价/宏观，只作内容，价格仍由行情源单独取）；
        /// ③ 新浪财经 7×24 快讯 JSON（国内可直连的「全球资讯」：航司、油价、汇率、台风都在里面）。
        /// <para>
        /// 多个源用「;」分隔；任一源不可达只影响它自己，其余照常。
        /// 想回到 Google News（需可访问外网）可整串替换为 <see cref="GlobalUrl"/>。
        /// </para>
        /// <para>
        /// 已实测淘汰：<c>ccaonline.cn</c>（中国民航网）WP REST —— 最新文章栏被 SEO 广告稿占满，
        /// 关键词过滤后几乎全丢，不适合做默认源；可另填其单篇/专栏地址自用。
        /// </para>
        /// </summary>
        public const string DomesticUrl =
            "https://www.chinanews.com.cn/rss/scroll-news.xml"
            + ";https://www.chinanews.com.cn/rss/finance.xml"
            + ";https://zhibo.sina.com.cn/api/zhibo/feed?page=1&page_size=30&zhibo_id=152&tag_id=0&dire=f&dpc=1";

        /// <summary>Google News 聚合（**需可访问外网**，大陆直连通常不可达）：留作海外网络环境的备选。</summary>
        public const string GlobalUrl =
            "https://news.google.com/rss/search?q=%E8%88%AA%E7%A9%BA%20OR%20%E6%B0%91%E8%88%AA%E5%B1%80%20OR%20%E8%88%AA%E7%8F%AD%20OR%20%E8%88%AA%E7%BA%BF%20OR%20%E6%9C%BA%E5%9C%BA%20OR%20%E8%88%AA%E6%B2%B9%20OR%20%E5%8F%B0%E9%A3%8E&hl=zh-CN&gl=CN&ceid=CN:zh-Hans";

        /// <summary>默认源 = 国内源（打开即用，不依赖翻墙）。</summary>
        public const string DefaultUrl = DomesticUrl;

        /// <summary>默认拉取间隔（现实秒）。0 = 不自动拉取，仅手动/注入。</summary>
        public const int DefaultIntervalSeconds = 1800;
        /// <summary>单轮最多采纳几条（去重后按重要度排序取前 N）。</summary>
        public const int DefaultMaxPerFetch = 3;
        /// <summary>每游戏日最多入队几条外部来文（含气象实况）：真正的防洪闸，默认 8。</summary>
        public const int DefaultDailyBudget = 8;
        const int MaxSeenIds = 400;
        const int TimeoutSeconds = 12;
        const int MaxBodyChars = 2000;

        static bool _loaded;
        static bool _enabled = true;
        static string _url = DefaultUrl;
        static int _intervalSeconds = DefaultIntervalSeconds;
        static int _maxPerFetch = DefaultMaxPerFetch;
        static int _dailyBudget = DefaultDailyBudget;
        static bool _relevanceFilter = true;

        // 行情源运行时状态（报价仅缓存于内存：每次开局重新取，避免存档里留下失真的现实价）
        static bool _quoteEnabled;
        static string _quoteUrl = DefaultQuoteUrl;
        static int _quoteIntervalSeconds = DefaultQuoteIntervalSeconds;
        static float _quoteCrackSpread = DefaultCrackSpreadUsdPerTon;
        static bool _quoteInFlight;
        static DateTime _quoteLastAttemptUtc = DateTime.MinValue;
        static float _oilQuoteUsdPerTon;
        static string _oilQuoteLabel = "";
        static DateTime _oilQuoteAtUtc = DateTime.MinValue;
        static string _quoteStatus = "";

        // 汇率源运行时状态（同样只缓存于内存）
        static bool _fxEnabled;
        static int _fxIntervalSeconds = DefaultFxIntervalSeconds;
        static bool _fxInFlight;
        static DateTime _fxLastAttemptUtc = DateTime.MinValue;
        static float _usdCny;
        static string _fxLabel = "";
        static DateTime _fxAtUtc = DateTime.MinValue;
        static string _fxStatus = "";

        static readonly object Gate = new object();
        static readonly List<ExternalEventPayload> Inbox = new List<ExternalEventPayload>();
        static readonly HashSet<string> Seen = new HashSet<string>();
        /// <summary>标题指纹去重：同一件事被多家转载时只入队一次（按归一化标题，不含媒体名）。</summary>
        static readonly HashSet<string> SeenTitles = new HashSet<string>();
        static bool _inFlight;
        static DateTime _lastAttemptUtc = DateTime.MinValue;
        static string _lastStatus = "";

        // ———— 配置读写 ————

        public static bool Enabled
        {
            get { EnsureLoaded(); return _enabled; }
            set { EnsureLoaded(); _enabled = value; SaveInt(PrefEnabled, value ? 1 : 0); }
        }

        public static string SourceUrl
        {
            get { EnsureLoaded(); return _url; }
            set { EnsureLoaded(); _url = value ?? ""; PlayerPrefs.SetString(PrefUrl, _url); PlayerPrefs.Save(); }
        }

        /// <summary>拉取间隔（现实秒）；0 = 不自动拉取。</summary>
        public static int IntervalSeconds
        {
            get { EnsureLoaded(); return _intervalSeconds; }
            set
            {
                EnsureLoaded();
                _intervalSeconds = Mathf.Max(0, value);
                SaveInt(PrefInterval, _intervalSeconds);
            }
        }

        public static int MaxPerFetch
        {
            get { EnsureLoaded(); return _maxPerFetch; }
            set { EnsureLoaded(); _maxPerFetch = Mathf.Clamp(value, 1, 10); SaveInt(PrefMaxPerFetch, _maxPerFetch); }
        }

        /// <summary>
        /// 每游戏日最多入队几条外部来文（默认 8）。条数门槛的真正意义在**节奏**而不是**相关性**：
        /// 关键词过滤负责「要不要」，这里负责「一天最多几封」，否则一条事件被几十家转载就会刷满收件箱。
        /// </summary>
        public static int DailyBudget
        {
            get { EnsureLoaded(); return _dailyBudget; }
            set { EnsureLoaded(); _dailyBudget = Mathf.Clamp(value, 1, 30); SaveInt(PrefDailyBudget, _dailyBudget); }
        }

        /// <summary>关键词过滤：滤掉股市/股市行情类噪声，只留与民航相关的内容。</summary>
        public static bool RelevanceFilter
        {
            get { EnsureLoaded(); return _relevanceFilter; }
            set { EnsureLoaded(); _relevanceFilter = value; SaveInt(PrefFilter, value ? 1 : 0); }
        }

        /// <summary>最近一次拉取结果（编辑器菜单/自测查看）：如「2030-05-10 10:30 收 8 → 采纳 2」。</summary>
        public static string LastStatus { get { EnsureLoaded(); return _lastStatus; } }

        // ———— 行情源（油价）配置读写 ————

        /// <summary>
        /// 是否拉取现实行情。默认开启：开启后航油价每季最多向现实报价靠拢 ±8%（见
        /// <see cref="Systems.MacroEconomyManager.ConvergeToRealQuote"/>），报价只作参考基准，
        /// 游戏自身的季度漂移、随机事件仍是油价的主导。关掉即完全按游戏宏观引擎走。
        /// </summary>
        public static bool QuoteEnabled
        {
            get { EnsureLoaded(); return _quoteEnabled; }
            set
            {
                EnsureLoaded();
                _quoteEnabled = value;
                SaveInt(PrefQuoteEnabled, value ? 1 : 0);
                // 关掉即作废缓存：避免「已关闭」之后还被上一轮的旧报价继续收敛
                if (!value)
                {
                    lock (Gate)
                    {
                        _oilQuoteUsdPerTon = 0f;
                        _oilQuoteAtUtc = DateTime.MinValue;
                        _quoteStatus = "";
                    }
                }
            }
        }

        public static string QuoteUrl
        {
            get { EnsureLoaded(); return _quoteUrl; }
            set
            {
                EnsureLoaded();
                _quoteUrl = string.IsNullOrEmpty(value) ? DefaultQuoteUrl : value;
                PlayerPrefs.SetString(PrefQuoteUrl, _quoteUrl);
                PlayerPrefs.Save();
            }
        }

        /// <summary>行情拉取间隔（现实秒）；0 = 不自动拉取。</summary>
        public static int QuoteIntervalSeconds
        {
            get { EnsureLoaded(); return _quoteIntervalSeconds; }
            set { EnsureLoaded(); _quoteIntervalSeconds = Mathf.Max(0, value); SaveInt(PrefQuoteInterval, _quoteIntervalSeconds); }
        }

        /// <summary>裂解价差（美元/吨）：原油换算航油时的加成，按需调整。</summary>
        public static float CrackSpreadUsdPerTon
        {
            get { EnsureLoaded(); return _quoteCrackSpread; }
            set
            {
                EnsureLoaded();
                _quoteCrackSpread = Mathf.Clamp(value, 0f, 600f);
                PlayerPrefs.SetFloat(PrefQuoteCrack, _quoteCrackSpread);
                PlayerPrefs.Save();
            }
        }

        /// <summary>最近一次行情拉取结果（编辑器/面板查看）。</summary>
        public static string QuoteStatus { get { EnsureLoaded(); return _quoteStatus; } }

        /// <summary>当前缓存的航油报价（美元/吨）；0 = 尚无报价。</summary>
        public static float CachedOilPricePerTon { get { EnsureLoaded(); return _oilQuoteUsdPerTon; } }

        /// <summary>报价来源标签（如 BZ=F / 布伦特）；空 = 尚无报价。</summary>
        public static string CachedOilLabel { get { EnsureLoaded(); return _oilQuoteLabel; } }

        /// <summary>报价时刻（现实 UTC）。只用于显示「多久前」，不参与游戏时间映射。</summary>
        public static DateTime CachedOilAtUtc { get { EnsureLoaded(); return _oilQuoteAtUtc; } }

        /// <summary>
        /// 取缓存中的航油报价（美元/吨）。纯读内存，不发起网络请求，可在季度结算路径上安全调用。
        /// 未开启行情源 / 尚未拿到报价 / 报价已被清档清空时返回 false。
        /// </summary>
        public static bool TryGetOilQuote(out float pricePerTon, out string label)
        {
            EnsureLoaded();
            pricePerTon = _quoteEnabled ? _oilQuoteUsdPerTon : 0f;
            label = _oilQuoteLabel;
            return pricePerTon > 0f;
        }

        /// <summary>
        /// 一行式现实报价状态（给其它页面复用，避免各页各写一套口径）。
        /// 例：<c>现实行情：BZ=F 78.40 美元/桶 → 航油 763 美元/吨（6 小时前）</c>；
        /// 未开启 / 尚未取到时给出对应说明，绝不返回 null。
        /// <paramref name="compact"/> = 短版，只留「基准 → 航油」，适合嵌在已有的油价行里。
        /// </summary>
        public static string QuoteStatusLine(bool compact = false)
        {
            EnsureLoaded();
            if (!_quoteEnabled)
                return compact ? "现实行情 未开启" : "现实行情：未开启（油价按游戏自身宏观引擎走）";
            if (_oilQuoteUsdPerTon <= 0f)
            {
                string st = _quoteStatus;
                return compact
                    ? "现实行情 已开启 · 尚未取到报价"
                    : "现实行情：已开启，尚未取到报价"
                      + (string.IsNullOrEmpty(st) ? "（点「立即获取报价」或等下一轮自动拉取）" : $"（上一轮：{st}）");
            }

            float brent = (_oilQuoteUsdPerTon - _quoteCrackSpread) / BarrelsPerTonJet;
            string src = string.IsNullOrEmpty(_oilQuoteLabel) ? "国际油价" : _oilQuoteLabel;
            string body = $"{src} {brent:F2} 美元/桶 → 航油 {_oilQuoteUsdPerTon:F0} 美元/吨";
            return compact
                ? $"现实行情 {body}"
                : $"现实行情：{body}（{AgeLabel(_oilQuoteAtUtc)}取数）";
        }

        /// <summary>报价距现在多久（现实时间）。仅用于显示，不参与游戏时间映射。</summary>
        public static string AgeLabel(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "尚无";
            double min = (DateTime.UtcNow - utc).TotalMinutes;
            if (min < 1.5) return "刚刚";
            if (min < 60) return $"{min:F0} 分钟前";
            double hr = min / 60.0;
            if (hr < 36) return $"{hr:F0} 小时前";
            return $"{hr / 24.0:F0} 天前";
        }

        // ———— 汇率源配置读写 ————

        /// <summary>
        /// 是否拉取现实 USD/CNY。默认开启：开启后汇率每季最多向现实汇率靠拢 ±3%（见
        /// <see cref="Logic.FxEconomics.ConvergeToRealRate"/>），现实汇率只作参考基准。
        /// </summary>
        public static bool FxEnabled
        {
            get { EnsureLoaded(); return _fxEnabled; }
            set
            {
                EnsureLoaded();
                _fxEnabled = value;
                SaveInt(PrefFxEnabled, value ? 1 : 0);
                if (!value)
                {
                    lock (Gate)
                    {
                        _usdCny = 0f;
                        _fxAtUtc = DateTime.MinValue;
                        _fxStatus = "";
                    }
                }
            }
        }

        /// <summary>汇率拉取间隔（现实秒）；0 = 不自动拉取。</summary>
        public static int FxIntervalSeconds
        {
            get { EnsureLoaded(); return _fxIntervalSeconds; }
            set { EnsureLoaded(); _fxIntervalSeconds = Mathf.Max(0, value); SaveInt(PrefFxInterval, _fxIntervalSeconds); }
        }

        /// <summary>最近一次汇率拉取结果。</summary>
        public static string FxStatus { get { EnsureLoaded(); return _fxStatus; } }

        /// <summary>当前缓存的 USD/CNY；0 = 尚无。</summary>
        public static float CachedUsdCny { get { EnsureLoaded(); return _usdCny; } }

        /// <summary>汇率来源标签（如 whUSDCNY / 欧洲央行）。</summary>
        public static string CachedFxLabel { get { EnsureLoaded(); return _fxLabel; } }

        /// <summary>汇率取数时刻（现实 UTC）。</summary>
        public static DateTime CachedFxAtUtc { get { EnsureLoaded(); return _fxAtUtc; } }

        /// <summary>纯读缓存：不开网络请求，可安全用于季度结算路径。</summary>
        public static bool TryGetExchangeRate(out float usdCny, out string label)
        {
            EnsureLoaded();
            usdCny = _fxEnabled ? _usdCny : 0f;
            label = _fxLabel;
            return usdCny > 0f;
        }

        /// <summary>一行式汇率状态（各页面共用口径），绝不返回 null。</summary>
        public static string FxStatusLine(bool compact = false)
        {
            EnsureLoaded();
            if (!_fxEnabled)
                return compact ? "现实汇率 未开启" : "现实汇率：未开启（汇率按游戏自身宏观引擎走）";
            if (_usdCny <= 0f)
            {
                string st = _fxStatus;
                return compact
                    ? "现实汇率 已开启 · 尚未取到"
                    : "现实汇率：已开启，尚未取到"
                      + (string.IsNullOrEmpty(st) ? "（下一轮自动拉取）" : $"（上一轮：{st}）");
            }
            string body = $"USD/CNY {_usdCny:F4}（{(_fxLabel.Length > 0 ? _fxLabel : "行情源")}）";
            return compact ? $"现实汇率 {body}" : $"现实汇率：{body} · {AgeLabel(_fxAtUtc)}取数";
        }

        /// <summary>直接投递一条汇率（离线自测），跳过网络。</summary>
        public static void InjectExchangeRate(float usdCny, string label = "自测汇率")
        {
            EnsureLoaded();
            lock (Gate)
            {
                _usdCny = Mathf.Clamp(usdCny, FxMin, FxMax);
                _fxLabel = string.IsNullOrEmpty(label) ? "自测汇率" : label;
                _fxAtUtc = DateTime.UtcNow;
                _fxStatus = $"{DateTime.Now:MM-dd HH:mm} {_fxLabel} = {_usdCny:F4}（注入）";
            }
        }

        static void SaveInt(string key, int v)
        {
            PlayerPrefs.SetInt(key, v);
            PlayerPrefs.Save();
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                _enabled = PlayerPrefs.GetInt(PrefEnabled, 1) != 0;
                string u = PlayerPrefs.GetString(PrefUrl, DefaultUrl);
                _url = string.IsNullOrEmpty(u) ? DefaultUrl : u;
                _intervalSeconds = PlayerPrefs.GetInt(PrefInterval, DefaultIntervalSeconds);
                _maxPerFetch = PlayerPrefs.GetInt(PrefMaxPerFetch, DefaultMaxPerFetch);
                _dailyBudget = PlayerPrefs.GetInt(PrefDailyBudget, DefaultDailyBudget);
                _relevanceFilter = PlayerPrefs.GetInt(PrefFilter, 1) != 0;

                _quoteEnabled = PlayerPrefs.GetInt(PrefQuoteEnabled, 1) != 0;
                string qu = PlayerPrefs.GetString(PrefQuoteUrl, DefaultQuoteUrl);
                _quoteUrl = string.IsNullOrEmpty(qu) ? DefaultQuoteUrl : qu;
                _quoteIntervalSeconds = PlayerPrefs.GetInt(PrefQuoteInterval, DefaultQuoteIntervalSeconds);
                _quoteCrackSpread = PlayerPrefs.GetFloat(PrefQuoteCrack, DefaultCrackSpreadUsdPerTon);

                _fxEnabled = PlayerPrefs.GetInt(PrefFxEnabled, 1) != 0;
                _fxIntervalSeconds = PlayerPrefs.GetInt(PrefFxInterval, DefaultFxIntervalSeconds);
            }
            catch
            {
                // PlayerPrefs 不可用（极端环境）时退回默认值
                _enabled = true;
                _url = DefaultUrl;
                _intervalSeconds = DefaultIntervalSeconds;
                _maxPerFetch = DefaultMaxPerFetch;
                _dailyBudget = DefaultDailyBudget;
                _relevanceFilter = true;
                _quoteEnabled = true;
                _quoteUrl = DefaultQuoteUrl;
                _quoteIntervalSeconds = DefaultQuoteIntervalSeconds;
                _quoteCrackSpread = DefaultCrackSpreadUsdPerTon;
                _fxEnabled = true;
                _fxIntervalSeconds = DefaultFxIntervalSeconds;
            }
        }

        // ———— 对外接口 ————

        /// <summary>
        /// 取现实航油价（美元/吨）。只读缓存 —— 数据由 <see cref="RequestQuoteRefresh"/> 在后台补上；
        /// 未开启行情源 / 尚未拿到报价时返回 null。调用方无需区分这两种情况。
        /// </summary>
        public static string TryFetchOilPrice(out float pricePerTon)
        {
            pricePerTon = 0f;
            if (!TryGetOilQuote(out float p, out string label)) return null;
            pricePerTon = p;
            return string.IsNullOrEmpty(label) ? "国际油价" : label;
        }

        /// <summary>
        /// 取现实 USD/CNY。只读缓存 —— 数据由 <see cref="RequestFxRefresh"/> 在后台补上；
        /// 未开启汇率源 / 尚未拿到报价时返回 null。返回值是来源标签（如 whUSDCNY）。
        /// </summary>
        public static string TryFetchExchangeRate(out float usdCny)
        {
            usdCny = 0f;
            if (!TryGetExchangeRate(out float rate, out string label)) return null;
            usdCny = rate;
            return string.IsNullOrEmpty(label) ? "USD/CNY" : label;
        }

        /// <summary>新闻另走事件管道（<see cref="FetchEvents"/>），此处不再单独返回。</summary>
        public static List<DynamicFeedItem> FetchNews() => null;

        /// <summary>
        /// 主线程排空后台抓取到的载荷。
        /// 没有新数据时返回空列表；调用方无需区分「未配置 / 断网 / 无新内容」。
        /// 节奏由调用方控制（<see cref="RequestRefresh"/>），此处只取结果，不发起网络请求。
        /// </summary>
        public static List<ExternalEventPayload> FetchEvents()
        {
            var list = new List<ExternalEventPayload>();
            lock (Gate)
            {
                if (Inbox.Count > 0)
                {
                    list.AddRange(Inbox);
                    Inbox.Clear();
                }
            }
            return list;
        }

        /// <summary>
        /// 发起一轮后台拉取（非阻塞）。同一时刻只允许一个在飞请求；距上次尝试不足间隔则跳过。
        /// 由游戏时钟每拍调用，因此倍速越快、现实节拍越密，但始终受 <see cref="IntervalSeconds"/> 约束。
        /// </summary>
        public static void RequestRefresh(bool force = false)
        {
            EnsureLoaded();
            if (!_enabled) return;
            string spec = _url;
            if (string.IsNullOrEmpty(spec)) return;

            lock (Gate)
            {
                if (_inFlight) return;
                if (!force && _intervalSeconds <= 0) return;
                if (!force && (DateTime.UtcNow - _lastAttemptUtc).TotalSeconds < _intervalSeconds) return;
                _inFlight = true;
                _lastAttemptUtc = DateTime.UtcNow;
            }

            string[] sources = SplitSources(spec);
            Task.Run(async () =>
            {
                int received = 0;
                var adopted = new List<ExternalEventPayload>();
                string note = "";
                try
                {
                    for (int i = 0; i < sources.Length; i++)
                    {
                        string text = await LoadTextAsync(sources[i]).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(text)) { note += Short(sources[i]) + "=不可达 "; continue; }
                        var items = Parse(text, sources[i]);
                        note += Short(sources[i]) + "=" + items.Count + " ";
                        received += items.Count;
                        adopted.AddRange(items);
                    }
                }
                catch
                {
                    // 任何异常都不向上冒：断网 / 证书 / 解析失败一律静默
                }
                finally
                {
                    lock (Gate)
                    {
                        int max = Mathf.Clamp(_maxPerFetch, 1, 10);
                        // 重要度优先：同一轮里「空管/台风/航油/事故」级别的消息先于泛行业稿进箱
                        adopted.Sort((a, b) =>
                        {
                            int sa = ScoreOf(a), sb = ScoreOf(b);
                            if (sa != sb) return sb.CompareTo(sa);
                            var ta = a?.publishedAt ?? DateTime.MinValue;
                            var tb = b?.publishedAt ?? DateTime.MinValue;
                            return tb.CompareTo(ta);
                        });

                        var fresh = new List<ExternalEventPayload>();
                        for (int i = 0; i < adopted.Count && fresh.Count < max; i++)
                        {
                            var p = adopted[i];
                            if (p == null || string.IsNullOrEmpty(p.id)) continue;
                            if (Seen.Contains(p.id)) continue;
                            string tk = TitleKey(p.title);
                            if (tk.Length > 0 && SeenTitles.Contains(tk)) continue;
                            Seen.Add(p.id);
                            if (tk.Length > 0) SeenTitles.Add(tk);
                            fresh.Add(p);
                        }
                        while (Seen.Count > MaxSeenIds)
                        {
                            var it = Seen.GetEnumerator();
                            if (!it.MoveNext()) break;
                            Seen.Remove(it.Current);
                        }
                        while (SeenTitles.Count > MaxSeenIds)
                        {
                            var it = SeenTitles.GetEnumerator();
                            if (!it.MoveNext()) break;
                            SeenTitles.Remove(it.Current);
                        }
                        Inbox.AddRange(fresh);
                        _inFlight = false;
                        _lastStatus = fresh.Count > 0
                            ? $"{DateTime.Now:MM-dd HH:mm} 收到 {received} · 采纳 {fresh.Count}（{note.Trim()}）"
                            : $"{DateTime.Now:MM-dd HH:mm} 收到 {received} · 无新内容（{note.Trim()}）";
                    }
                }
            });
        }

        /// <summary>直接投递载荷到待入队缓存（离线测试 / 上行推送）。同样受去重与条数上限约束。</summary>
        public static void Inject(List<ExternalEventPayload> payloads)
        {
            if (payloads == null || payloads.Count == 0) return;
            EnsureLoaded();
            lock (Gate)
            {
                int max = Mathf.Clamp(_maxPerFetch, 1, 10);
                int added = 0;
                for (int i = 0; i < payloads.Count && added < max; i++)
                {
                    var p = payloads[i];
                    if (p == null) continue;
                    if (string.IsNullOrEmpty(p.id)) p.id = "inj_" + StableId(p.title + p.source + p.content);
                    if (Seen.Contains(p.id)) continue;
                    Seen.Add(p.id);
                    Inbox.Add(p);
                    added++;
                }
                int total = payloads.Count;
                _lastStatus = $"{DateTime.Now:MM-dd HH:mm} 注入 {total} · 采纳 {added}";
            }
        }

        /// <summary>清空缓存与去重表（新开局时调用）。</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                Inbox.Clear();
                Seen.Clear();
                SeenTitles.Clear();
                _inFlight = false;
                _lastAttemptUtc = DateTime.MinValue;
                _lastStatus = "";

                // 报价属运行时数据：清档后重新取，避免新局沿用上一局的现实价
                _oilQuoteUsdPerTon = 0f;
                _oilQuoteLabel = "";
                _oilQuoteAtUtc = DateTime.MinValue;
                _quoteInFlight = false;
                _quoteLastAttemptUtc = DateTime.MinValue;
                _quoteStatus = "";

                // 汇率同理
                _usdCny = 0f;
                _fxLabel = "";
                _fxAtUtc = DateTime.MinValue;
                _fxInFlight = false;
                _fxLastAttemptUtc = DateTime.MinValue;
                _fxStatus = "";
            }
        }

        // ———— 抓取 ————

        static string[] SplitSources(string spec)
        {
            var raw = spec.Replace("\r", "").Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<string>();
            for (int i = 0; i < raw.Length; i++)
            {
                string s = raw[i].Trim();
                if (s.Length > 0) list.Add(s);
            }
            return list.ToArray();
        }

        static async Task<string> LoadTextAsync(string source)
        {
            try
            {
                if (source.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    return ReadTextFile(source.Substring(7));
                if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return await DownloadTextAsync(source).ConfigureAwait(false);

                // 无协议：本地路径（绝对或相对 StreamingAssets）
                string path = source;
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(Application.streamingAssetsPath, source);
                return ReadTextFile(path);
            }
            catch
            {
                return null;
            }
        }

        static string ReadTextFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1); // 去 BOM
                return text;
            }
            catch
            {
                return null;
            }
        }

        static async Task<string> DownloadTextAsync(string url)
        {
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml,application/atom+xml,application/json,text/xml,*/*;q=0.8");
                    // 国内站点（中国民航网等）的 WAF 会拦「无来源」的裸请求，带上同源 Referer 才稳定返回
                    try
                    {
                        string referer = url.IndexOf("sinajs.cn", StringComparison.OrdinalIgnoreCase) >= 0
                            ? "https://finance.sina.com.cn"
                            : url;
                        http.DefaultRequestHeaders.Referrer = new Uri(referer);
                    }
                    catch { }
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
                return null; // 静默失败：不产生任何 Unity 日志
            }
        }

        // ———— 行情源（油价）：后台抓取 + 换算 + 缓存 ————

        /// <summary>
        /// 发起一轮后台行情拉取（非阻塞）。与新闻源各自独立节流：
        /// 未开启行情源 / 间隔未到 / 同一时刻已有在飞请求 → 直接返回。
        /// 由游戏时钟每拍调用，因此倍速越快现实节拍越密，但始终受 <see cref="QuoteIntervalSeconds"/> 约束。
        /// </summary>
        public static void RequestQuoteRefresh(bool force = false)
        {
            EnsureLoaded();
            if (!_quoteEnabled) return;
            string url = _quoteUrl;
            if (string.IsNullOrEmpty(url)) return;

            lock (Gate)
            {
                if (_quoteInFlight) return;
                if (!force && _quoteIntervalSeconds <= 0) return;
                if (!force && (DateTime.UtcNow - _quoteLastAttemptUtc).TotalSeconds < _quoteIntervalSeconds) return;
                _quoteInFlight = true;
                _quoteLastAttemptUtc = DateTime.UtcNow;
            }

            float crack = _quoteCrackSpread;
            string host = Short(url);
            // 默认地址走多源回退（腾讯 → 新浪 → Yahoo）：任一可用即止
            string[] chain = string.Equals(url, DefaultQuoteUrl, StringComparison.OrdinalIgnoreCase)
                ? DefaultQuoteUrls
                : new[] { url };
            Task.Run(async () =>
            {
                float price = 0f;
                string label = "";
                string note = "不可达";
                foreach (string src in chain)
                {
                    try
                    {
                        string text = await LoadTextAsync(src).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(text)) continue;
                        string unit;
                        float p;
                        string l;
                        if (ParseQuote(text, out p, out l, out unit))
                        {
                            price = p;
                            label = l;
                            note = string.IsNullOrEmpty(unit) ? "已换算" : unit;
                            break;
                        }
                        note = "无法识别报价字段";
                    }
                    catch
                    {
                        // 同新闻源：任何异常都不向上冒，静默跳到下一个源
                    }
                }
                lock (Gate)
                {
                    _quoteInFlight = false;
                    if (price > 0f)
                    {
                        _oilQuoteUsdPerTon = price;
                        _oilQuoteLabel = string.IsNullOrEmpty(label) ? host : label;
                        _oilQuoteAtUtc = DateTime.UtcNow;
                        _quoteStatus = $"{DateTime.Now:MM-dd HH:mm} {_oilQuoteLabel} → 航油 {price:F0} 美元/吨（{note}）";
                    }
                    else
                    {
                        _quoteStatus = $"{DateTime.Now:MM-dd HH:mm} 无报价（{host} {note}）";
                    }
                }
            });
        }

        /// <summary>直接投递一条报价（离线自测 / 上行推送），跳过网络。</summary>
        public static void InjectOilQuote(float priceUsdPerTon, string label = "自测报价")
        {
            EnsureLoaded();
            lock (Gate)
            {
                _oilQuoteUsdPerTon = Mathf.Clamp(priceUsdPerTon, QuoteMinUsdPerTon, QuoteMaxUsdPerTon);
                _oilQuoteLabel = string.IsNullOrEmpty(label) ? "自测报价" : label;
                _oilQuoteAtUtc = DateTime.UtcNow;
                _quoteStatus = $"{DateTime.Now:MM-dd HH:mm} {_oilQuoteLabel} → 航油 {_oilQuoteUsdPerTon:F0} 美元/吨（注入）";
            }
        }

        [Serializable]
        class QuoteFlatDto
        {
            public float price;
            public float value;
            public float last;
            public float regularMarketPrice;
            public string unit;
            public string symbol;
        }

        [Serializable]
        class QuoteChartWrapper
        {
            public QuoteChart chart;
        }

        [Serializable]
        class QuoteChart
        {
            public QuoteResult[] result;
            public QuoteError error;
        }

        [Serializable]
        class QuoteResult
        {
            public QuoteMeta meta;
        }

        [Serializable]
        class QuoteMeta
        {
            public float regularMarketPrice;
            public string symbol;
            public string currency;
        }

        [Serializable]
        class QuoteError
        {
            public string code;
        }

        /// <summary>
        /// 解析报价文本并换算成航油（美元/吨）。公开以便离线自测。
        /// <para>
        /// 接受三种形态：① 图表接口 <c>{"chart":{"result":[{"meta":{"regularMarketPrice":…}}]}}</c>；
        /// ② 扁平 JSON <c>{"price"|"value"|"last"|"regularMarketPrice":…,"unit":"bbl|ton"}</c>；
        /// ③ 纯数字（按桶计价）。
        /// unit 为 bbl（默认）时按「×7.82 + 裂解价差」换算；ton 时视作已是航油吨价。
        /// </para>
        /// </summary>
        public static bool ParseQuote(string text, out float pricePerTon, out string label, out string unitNote)
        {
            pricePerTon = 0f;
            label = "";
            unitNote = "";
            if (string.IsNullOrEmpty(text)) return false;

            string t = text.TrimStart('\uFEFF', '\u200B', ' ', '\r', '\n', '\t');
            float raw = 0f;
            string unit = "";
            string symbol = "";

            // 腾讯 / 新浪外盘行情串（国内直连，无需 Key）：
            //   v_hf_OIL="110.35,0.73,110.25,…" / hq_str_hf_OIL="110.249,,110.150,…"
            // 字段 0 = 最新价（美元/桶），只取数字，编码乱码不影响。
            if (t.IndexOf("hf_OIL", StringComparison.OrdinalIgnoreCase) > 0
                && (t.IndexOf("v_hf_", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("hq_str_", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                int eq = t.IndexOf('=');
                int q1 = eq > 0 ? t.IndexOf('"', eq + 1) : -1;
                int q2 = q1 > 0 ? t.IndexOf('"', q1 + 1) : -1;
                if (eq > 0 && q1 > eq && q2 > q1)
                {
                    string first = t.Substring(q1 + 1, q2 - q1 - 1).Split(',')[0].Trim();
                    if (float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out raw)
                        && raw > 0f)
                    {
                        unit = "bbl";
                        symbol = "布伦特原油";
                    }
                }
            }

            if (raw <= 0f && (t.StartsWith("{", StringComparison.Ordinal) || t.StartsWith("[", StringComparison.Ordinal)))
            {
                try
                {
                    var wrap = JsonUtility.FromJson<QuoteChartWrapper>(t);
                    if (wrap != null && wrap.chart != null && wrap.chart.result != null
                        && wrap.chart.result.Length > 0 && wrap.chart.result[0] != null
                        && wrap.chart.result[0].meta != null
                        && wrap.chart.result[0].meta.regularMarketPrice > 0f)
                    {
                        raw = wrap.chart.result[0].meta.regularMarketPrice;
                        symbol = wrap.chart.result[0].meta.symbol ?? "";
                        unit = "bbl"; // 图表接口的原油合约按桶计价
                    }
                }
                catch
                {
                    // 落到扁平契约
                }

                if (raw <= 0f)
                {
                    try
                    {
                        var flat = JsonUtility.FromJson<QuoteFlatDto>(t);
                        if (flat != null)
                        {
                            raw = FirstPositive(flat.price, flat.value, flat.last, flat.regularMarketPrice);
                            unit = flat.unit ?? "";
                            symbol = flat.symbol ?? "";
                        }
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            else
            {
                if (raw <= 0f)
                    float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out raw);
            }

            if (raw <= 0f || float.IsNaN(raw) || float.IsInfinity(raw)) return false;

            bool perTon = unit.IndexOf("ton", StringComparison.OrdinalIgnoreCase) >= 0
                          || unit.IndexOf("mt", StringComparison.OrdinalIgnoreCase) >= 0;
            if (perTon)
            {
                pricePerTon = Mathf.Clamp(raw, QuoteMinUsdPerTon, QuoteMaxUsdPerTon);
                unitNote = "吨价直取";
            }
            else
            {
                float jet = raw * BarrelsPerTonJet + _quoteCrackSpread;
                pricePerTon = Mathf.Clamp(jet, QuoteMinUsdPerTon, QuoteMaxUsdPerTon);
                unitNote = $"桶价 {raw:F2}×{BarrelsPerTonJet:F2}+{_quoteCrackSpread:F0}";
            }
            label = string.IsNullOrEmpty(symbol) ? "" : symbol;
            return pricePerTon > 0f;
        }

        static float FirstPositive(params float[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (values[i] > 0f) return values[i];
            return 0f;
        }

        // ———— 汇率源：后台抓取 + 多源回退 + 缓存 ————

        /// <summary>
        /// 发起一轮后台汇率拉取（非阻塞）。按 <see cref="DefaultFxUrls"/> 顺序回退，首个解析成功的即采用；
        /// 与油价、新闻各自独立节流。未开启 / 间隔未到 / 已有在飞请求 → 直接返回。
        /// </summary>
        public static void RequestFxRefresh(bool force = false)
        {
            EnsureLoaded();
            if (!_fxEnabled) return;

            lock (Gate)
            {
                if (_fxInFlight) return;
                if (!force && _fxIntervalSeconds <= 0) return;
                if (!force && (DateTime.UtcNow - _fxLastAttemptUtc).TotalSeconds < _fxIntervalSeconds) return;
                _fxInFlight = true;
                _fxLastAttemptUtc = DateTime.UtcNow;
            }

            Task.Run(async () =>
            {
                float rate = 0f;
                string label = "";
                string note = "";
                try
                {
                    for (int i = 0; i < DefaultFxUrls.Length && rate <= 0f; i++)
                    {
                        string url = DefaultFxUrls[i];
                        string text = await LoadTextAsync(url).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(text)) { note += Short(url) + "=不可达 "; continue; }
                        if (ParseFxRate(text, out float r, out string lb))
                        {
                            rate = r;
                            label = lb;
                        }
                        else note += Short(url) + "=无法识别 ";
                    }
                }
                catch
                {
                    // 同新闻源：任何异常静默跳过
                }
                finally
                {
                    lock (Gate)
                    {
                        _fxInFlight = false;
                        if (rate > 0f)
                        {
                            _usdCny = Mathf.Clamp(rate, FxMin, FxMax);
                            _fxLabel = string.IsNullOrEmpty(label) ? "USD/CNY" : label;
                            _fxAtUtc = DateTime.UtcNow;
                            _fxStatus = $"{DateTime.Now:MM-dd HH:mm} USD/CNY {_usdCny:F4}（{_fxLabel}）";
                        }
                        else
                        {
                            _fxStatus = $"{DateTime.Now:MM-dd HH:mm} 无汇率（{(note.Length > 0 ? note.Trim() : "不可达")}）";
                        }
                    }
                }
            });
        }

        static readonly Regex RxFxJson = new Regex(
            @"""CNY""\s*:\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled);
        static readonly Regex RxFxPlain = new Regex(
            @"^\s*([0-9]+(?:\.[0-9]+)?)\s*$", RegexOptions.Compiled);

        /// <summary>
        /// 解析 USD/CNY。公开以便离线自测。接受三种形态：
        /// ① 腾讯行情 <c>v_whUSDCNY="310~美元人民币~USDCNY~6.6792~…"</c>（取 ~ 分隔的第 4 段）；
        /// ② JSON 里含 <c>"CNY": 6.68</c>（Frankfurter / open.er-api）；
        /// ③ 纯数字（自定义代理）。
        /// </summary>
        public static bool ParseFxRate(string text, out float rate, out string label)
        {
            rate = 0f;
            label = "";
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim();

            int vq = t.IndexOf("v_wh", StringComparison.OrdinalIgnoreCase);
            if (vq >= 0)
            {
                int eq = t.IndexOf('=');
                if (eq > vq)
                {
                    string payload = t.Substring(eq + 1).Trim().Trim('"', ';', ' ', '\r', '\n');
                    string[] parts = payload.Split('~');
                    for (int i = 0; i + 1 < parts.Length; i++)
                    {
                        if (parts[i].IndexOf("USDCNY", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (float.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out rate)
                            && rate > 0f)
                        {
                            label = "腾讯行情";
                            return true;
                        }
                    }
                }
            }

            if (t.StartsWith("{", StringComparison.Ordinal))
            {
                var m = RxFxJson.Match(t);
                if (m.Success
                    && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out rate)
                    && rate > 0f)
                {
                    label = t.IndexOf("frankfurter", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "欧洲央行"
                        : t.IndexOf("er-api", StringComparison.OrdinalIgnoreCase) >= 0 ? "open.er-api" : "行情源";
                    return true;
                }
                return false;
            }

            var plain = RxFxPlain.Match(t);
            if (plain.Success
                && float.TryParse(plain.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out rate)
                && rate > 0f)
            {
                label = "自定义";
                return true;
            }
            rate = 0f;
            return false;
        }

        // ———— 解析 ————

        /// <summary>按内容自动判别 JSON / RSS 并解析（公开以便离线自测与上游预检）。</summary>
        public static List<ExternalEventPayload> ParseText(string text, string origin)
        {
            if (string.IsNullOrEmpty(text)) return new List<ExternalEventPayload>();
            return Parse(text, origin);
        }

        static List<ExternalEventPayload> Parse(string text, string origin)
        {
            string t = text.TrimStart('\uFEFF', '\u200B', ' ', '\r', '\n', '\t');
            List<ExternalEventPayload> list;
            if (t.StartsWith("{", StringComparison.Ordinal) || t.StartsWith("[", StringComparison.Ordinal))
            {
                if (t.IndexOf("\"rich_text\"", StringComparison.Ordinal) >= 0)
                    list = ParseSinaFlash(t);
                else if (t.IndexOf("\"rendered\"", StringComparison.Ordinal) >= 0)
                    list = ParseWpPosts(t);
                else
                    list = ParseJson(t);
            }
            else
                list = ParseFeed(t, origin);
            if (_relevanceFilter) list = Filter(list);
            return list;
        }

        [Serializable]
        class PayloadDto
        {
            public string id;
            public string category;
            public string title;
            public string headline;      // title 别名
            public string content;
            public string body;          // content 别名
            public string source;
            public string publishedAt;
            public string realEffectiveAt;
            public string effectType;
            public float effectValue;
            public string affectedRegion;
            public int durationHours;
            public string chainId;
            public bool expired;
        }

        [Serializable]
        class PayloadListDto
        {
            public PayloadDto[] events;
            public PayloadDto[] items;
        }

        /// <summary>JSON 契约：顶层数组 或 {"events":[...]} / {"items":[...]}。</summary>
        static List<ExternalEventPayload> ParseJson(string text)
        {
            var result = new List<ExternalEventPayload>();
            try
            {
                string body = text.TrimStart().StartsWith("[", StringComparison.Ordinal)
                    ? "{\"events\":" + text + "}"
                    : text;
                var dto = JsonUtility.FromJson<PayloadListDto>(body);
                if (dto == null) return result;
                var arr = dto.events != null ? dto.events : dto.items;
                if (arr == null) return result;
                for (int i = 0; i < arr.Length; i++)
                {
                    var p = ToPayload(arr[i]);
                    if (p != null) result.Add(p);
                }
            }
            catch
            {
                // 非法 JSON：静默丢弃
            }
            return result;
        }

        // WordPress REST（中国民航网等）：{"id":…,"date":…,"link":…,"title":{"rendered":"…"},"excerpt":{"rendered":"…"}}
        // JsonUtility 解不了嵌套对象，这里按字段正则抽取，再按同序配对；字段缺失时退化为「只有标题也有内容」。
        static readonly Regex RxWpTitle = new Regex(
            @"""title""\s*:\s*\{\s*""rendered""\s*:\s*""((?:\\.|[^""\\])*)""",
            RegexOptions.Compiled);
        static readonly Regex RxWpExcerpt = new Regex(
            @"""excerpt""\s*:\s*\{\s*""rendered""\s*:\s*""((?:\\.|[^""\\])*)""",
            RegexOptions.Compiled);
        static readonly Regex RxWpLink = new Regex(
            @"""link""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.Compiled);
        static readonly Regex RxWpDate = new Regex(
            @"""date""\s*:\s*""([^""]*)""", RegexOptions.Compiled);

        static List<ExternalEventPayload> ParseWpPosts(string text)
        {
            var result = new List<ExternalEventPayload>();
            var titles = RxWpTitle.Matches(text);
            if (titles.Count == 0) return result;
            var excerpts = RxWpExcerpt.Matches(text);
            var links = RxWpLink.Matches(text);
            var dates = RxWpDate.Matches(text);

            for (int i = 0; i < titles.Count; i++)
            {
                string title = Clean(UnescapeJson(titles[i].Groups[1].Value));
                if (string.IsNullOrEmpty(title)) continue;
                string content = i < excerpts.Count
                    ? Clean(UnescapeJson(excerpts[i].Groups[1].Value)) : "";
                string link = i < links.Count ? UnescapeJson(links[i].Groups[1].Value) : "";
                string dateText = i < dates.Count ? dates[i].Groups[1].Value : "";

                if (SameText(content, title)) content = "";
                if (content.Length > 0 && content.StartsWith(title, StringComparison.Ordinal))
                    content = content.Substring(title.Length).TrimStart(' ', '：', ':', '—', '-');

                result.Add(new ExternalEventPayload
                {
                    id = "wp_" + StableId(string.IsNullOrEmpty(link) ? title : link),
                    category = CategoryOf("中国民航网", title),
                    title = title,
                    content = content,
                    source = "中国民航网",
                    publishedAt = ParseDate(dateText),
                    effectType = "",
                    effectValue = 0f,
                    affectedRegion = "",
                    durationHours = 0,
                    chainId = ""
                });
            }

            result.Sort((a, b) =>
            {
                var ta = a.publishedAt ?? DateTime.MinValue;
                var tb = b.publishedAt ?? DateTime.MinValue;
                return tb.CompareTo(ta);
            });
            return result;
        }

        // ———— 新浪财经 7×24 快讯（国内可直连的「全球资讯」流）————

        [Serializable] class SinaItem { public string id; public string rich_text; public string create_time; public string docurl; }
        [Serializable] class SinaFeed { public SinaItem[] list; }
        [Serializable] class SinaFeedHolder { public SinaFeed feed; }
        [Serializable] class SinaData { public SinaFeedHolder data; }
        [Serializable] class SinaRoot { public SinaData result; }

        /// <summary>
        /// 解析 <c>zhibo.sina.com.cn/api/zhibo/feed</c>：<c>result.data.feed.list[]</c>。
        /// 一条快讯就是一段带【标题】的长文，拆成标题 + 正文再进通用管线。
        /// </summary>
        static List<ExternalEventPayload> ParseSinaFlash(string text)
        {
            var result = new List<ExternalEventPayload>();
            SinaRoot root = null;
            try { root = JsonUtility.FromJson<SinaRoot>(text); } catch { return result; }
            var items = root != null && root.result != null && root.result.data != null
                        && root.result.data.feed != null ? root.result.data.feed.list : null;
            if (items == null) return result;

            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i];
                if (it == null) continue;
                string body = Clean(it.rich_text ?? "");
                if (body.Length < 6) continue;

                string title, content;
                SplitFlash(body, out title, out content);
                if (string.IsNullOrEmpty(title)) continue;

                string key = string.IsNullOrEmpty(it.id) ? it.docurl : it.id;
                if (string.IsNullOrEmpty(key)) key = title;

                result.Add(new ExternalEventPayload
                {
                    id = "sina_" + StableId(key),
                    category = CategoryOf("新浪财经", title),
                    title = title,
                    content = content,
                    source = "新浪财经",
                    publishedAt = ParseBeijingTime(it.create_time),
                    effectType = "",
                    effectValue = 0f,
                    affectedRegion = "",
                    durationHours = 0,
                    chainId = ""
                });
            }

            result.Sort((a, b) =>
            {
                var ta = a.publishedAt ?? DateTime.MinValue;
                var tb = b.publishedAt ?? DateTime.MinValue;
                return tb.CompareTo(ta);
            });
            return result;
        }

        /// <summary>【标题】快讯 → 方括号内为标题；否则在句读处截一段短标题，正文保留全文。</summary>
        static void SplitFlash(string body, out string title, out string content)
        {
            title = "";
            content = body;

            if (body[0] == '【')
            {
                int close = body.IndexOf('】');
                if (close > 2 && close <= 60)
                {
                    title = body.Substring(1, close - 1).Trim();
                    string rest = body.Substring(close + 1).TrimStart(' ', '：', ':', '—', '-');
                    if (!string.IsNullOrEmpty(title) && rest.Length >= 8) content = rest;
                    return;
                }
            }

            int cut = -1;
            for (int i = 8; i < body.Length && i < 64; i++)
            {
                char c = body[i];
                if (c == '。' || c == '，' || c == '；' || c == '：' || c == '！' || c == '？')
                {
                    cut = i;
                    break;
                }
            }
            if (cut > 0)
            {
                title = body.Substring(0, cut).Trim();
                string rest = body.Substring(cut + 1).TrimStart(' ', '，', '。');
                content = rest.Length >= 8 ? rest : body;
            }
            else
            {
                title = body.Length > 48 ? body.Substring(0, 48) + "…" : body;
            }
        }

        /// <summary>北京时间字符串（无时区）→ UTC。</summary>
        static DateTime? ParseBeijingTime(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            DateTime dt;
            if (!DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out dt)) return null;
            return DateTime.SpecifyKind(dt - TimeSpan.FromHours(8), DateTimeKind.Utc);
        }

        /// <summary>JSON 字符串反转义：\\uXXXX / \\/ / \\" / \\n / \\t。</summary>
        static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '/': sb.Append('/'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'u':
                        if (i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4),
                                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        static ExternalEventPayload ToPayload(PayloadDto d)
        {
            if (d == null) return null;
            string title = Pick(d.title, d.headline);
            string content = Pick(d.content, d.body);
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(content)) return null;

            string src = d.source;
            if (string.IsNullOrEmpty(src)) src = "通讯社";
            string id = d.id;
            if (string.IsNullOrEmpty(id)) id = "json_" + StableId(src + title + content);

            return new ExternalEventPayload
            {
                id = id,
                category = d.category ?? "",
                title = title,
                content = string.IsNullOrEmpty(content) ? title : content,
                source = src,
                publishedAt = ParseDate(d.publishedAt),
                realEffectiveAt = ParseDate(d.realEffectiveAt),
                expired = d.expired,
                effectType = d.effectType ?? "",
                effectValue = d.effectValue,
                affectedRegion = d.affectedRegion ?? "",
                durationHours = d.durationHours,
                chainId = d.chainId ?? ""
            };
        }

        static readonly Regex RxItem = new Regex(@"<(item|entry)\b[^>]*>(.*?)</\1>",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        static readonly Regex RxTag = new Regex(@"<[^>]+>", RegexOptions.Compiled);

        /// <summary>RSS 2.0 / Atom：通讯社消息稿，只作内容，不带数值影响。</summary>
        static List<ExternalEventPayload> ParseFeed(string text, string origin)
        {
            var result = new List<ExternalEventPayload>();
            string channelName = FeedTitle(text);
            string host = HostOf(origin);

            var matches = RxItem.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                string block = matches[i].Groups[2].Value;
                string title = Clean(Tag(block, "title"));
                if (string.IsNullOrEmpty(title)) continue;

                string src = Clean(Tag(block, "source"));
                if (string.IsNullOrEmpty(src)) src = channelName;
                if (string.IsNullOrEmpty(src)) src = host;
                src = TrimSourceSuffix(src);

                // 「标题 - 媒体名」去掉尾巴上的媒体名
                string tail = " - " + src;
                if (src.Length > 0 && title.EndsWith(tail, StringComparison.Ordinal))
                    title = title.Substring(0, title.Length - tail.Length).Trim();
                string tail2 = "_" + src;
                if (src.Length > 0 && title.EndsWith(tail2, StringComparison.Ordinal))
                    title = title.Substring(0, title.Length - tail2.Length).Trim();

                string content = Clean(Pick(Tag(block, "description"), Tag(block, "summary"),
                    Tag(block, "content:encoded"), Tag(block, "content")));
                content = TrimSourceSuffix(content);
                if (content.EndsWith(src, StringComparison.Ordinal) && src.Length > 0)
                    content = content.Substring(0, content.Length - src.Length).Trim();
                // Google News 的 description 与标题同源：此时不重复正文，正文留空
                if (SameText(content, title)) content = "";

                string key = Clean(Tag(block, "guid"));
                if (string.IsNullOrEmpty(key)) key = Clean(Tag(block, "id"));
                if (string.IsNullOrEmpty(key)) key = Clean(Tag(block, "link"));
                if (string.IsNullOrEmpty(key)) key = title;

                string dateText = Pick(Tag(block, "pubDate"), Tag(block, "published"), Tag(block, "updated"));
                var when = ParseDate(dateText);

                result.Add(new ExternalEventPayload
                {
                    id = "rss_" + StableId(key),
                    category = CategoryOf(src, title),
                    title = title,
                    content = content,
                    source = src,
                    publishedAt = when,
                    effectType = "",
                    effectValue = 0f,
                    affectedRegion = "",
                    durationHours = 0,
                    chainId = ""
                });
            }

            // 新的在前
            result.Sort((a, b) =>
            {
                var ta = a.publishedAt ?? DateTime.MinValue;
                var tb = b.publishedAt ?? DateTime.MinValue;
                return tb.CompareTo(ta);
            });
            return result;
        }

        static string Tag(string block, string name)
        {
            var m = Regex.Match(block, "<" + Regex.Escape(name) + @"\b[^>]*>(.*?)</" + Regex.Escape(name) + ">",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            string v = m.Groups[1].Value;
            if (v.StartsWith("<![CDATA[", StringComparison.Ordinal))
            {
                int end = v.IndexOf("]]>", StringComparison.Ordinal);
                v = end > 0 ? v.Substring(9, end - 9) : v.Substring(9);
            }
            return v.Trim();
        }

        static string FeedTitle(string text)
        {
            int firstItem = text.IndexOf("<item", StringComparison.OrdinalIgnoreCase);
            if (firstItem < 0) firstItem = text.IndexOf("<entry", StringComparison.OrdinalIgnoreCase);
            string head = firstItem > 0 ? text.Substring(0, firstItem) : text;
            return Clean(Tag(head, "title"));
        }

        // ———— 清洗 ————

        /// <summary>去标签、去实体、压缩空白：把 HTML 描述变成可读正文。</summary>
        static string Clean(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string s = html;
            if (s.StartsWith("<![CDATA[", StringComparison.Ordinal))
            {
                int end = s.IndexOf("]]>", StringComparison.Ordinal);
                s = end > 0 ? s.Substring(9, end - 9) : s.Substring(9);
            }
            s = s.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n")
                 .Replace("</p>", "\n").Replace("</div>", "\n");
            s = RxTag.Replace(s, " ");
            s = s.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                 .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&apos;", "'")
                 .Replace("&#160;", " ").Replace("&mdash;", "—").Replace("&hellip;", "…");
            s = Regex.Replace(s, @"&#(\d+);", m =>
            {
                int code = 0;
                if (int.TryParse(m.Groups[1].Value, out code) && code > 0 && code < 0x10000)
                    return ((char)code).ToString();
                return "";
            });
            s = Regex.Replace(s, @"[ \t\u3000]+", " ");
            s = Regex.Replace(s, @"\n{2,}", "\n");
            s = s.Trim(' ', '\n', '\t');
            if (s.Length > MaxBodyChars) s = s.Substring(0, MaxBodyChars).TrimEnd() + "…";
            return s;
        }

        static string TrimSourceSuffix(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Trim(' ', '\u3000', '\t', '-', '—', '_', '|', '·');
        }

        static bool SameText(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string x = Regex.Replace(a, @"[\s\u3000\-—_,，。；;：:、""'（）()\[\]【】]", "");
            string y = Regex.Replace(b, @"[\s\u3000\-—_,，。；;：:、""'（）()\[\]【】]", "");
            return x.Length > 0 && x.Equals(y, StringComparison.Ordinal);
        }

        static string HostOf(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) return "";
                var u = new Uri(url);
                return u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host.Substring(4) : u.Host;
            }
            catch
            {
                return "";
            }
        }

        static string Pick(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (!string.IsNullOrEmpty(values[i])) return values[i].Trim();
            return "";
        }

        static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            DateTime dt;
            if (DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt))
                return dt;
            return null;
        }

        /// <summary>FNV-1a：跨会话稳定的短 id（不要用 string.GetHashCode，.NET 会随机化）。</summary>
        static string StableId(string seed)
        {
            unchecked
            {
                uint h = 2166136261u;
                string s = seed ?? "";
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619u;
                }
                return h.ToString("x8");
            }
        }

        // ———— 相关性过滤（只作用于真实订阅源；注入载荷与 EventGenerator 不走这里）————
        //
        // 判定顺序：① 命中股市/商品噪声 → 丢；② 自带数值影响 → 留；③ 命中民航运营主体词或极端天气 → 留；④ 其余 → 丢。
        //
        // 两个坑，改动时务必留意：
        // ① 白名单必须「窄」：「油价 / 原油 / 机票 / 延误」这类宽泛词一旦入列，财经与泛民生新闻会整批放行
        //    （实测旧口径 100 条进 96 条）。油价改由行情源单独获取，新闻源只管内容。
        // ② 关键词会被子串误伤：实测「中韩定期货运航线」里的「定期货运」含子串「期货」，
        //    导致一条合法民航新闻被判为财经噪声丢弃。凡可能跨词边界的词，都要加反向否定断言。
        //    同类隐患：全市值勤→市值、规定增长→定增。改动关键词后请用真实报文回归一次。

        static readonly Regex RxNoise = new Regex(
            @"证券|股价|涨停|跌停|主力资金|净卖出|净买入|股东|股权|(?<!规)定增|募资|ETF|基金|盘前|个股|"
            + @"A股|港股|美股|股指|道琼斯|纳斯达克|隔夜要闻|(?<!定)期货|(?<!市)市值|财报预|选股|"
            + @"航空股|民航股|航空板块|民航板块|"
            // 跨行业假朋友：「航线 / 台风」在航运、保险、救灾里同样常见，靠这些词把行业掰回来
            + @"航运|集运|海运|水运|运河|港航|码头|船|"
            + @"理赔|灾后|救灾|防汛|转移安置|"
            + @"医院",
            RegexOptions.Compiled);

        /// <summary>
        /// 民航运营主体词 + 影响运行的极端天气。
        /// <para>
        /// 不含「油价 / 原油 / 机票 / 货运 / 旅客 / 延误」等宽泛词 —— 它们不该单独构成采纳理由。
        /// 含繁体写法：订阅源会返回港澳台媒体（实测同一个 9·11 故事，简体版被保留、繁体版被丢弃）。
        /// </para>
        /// </summary>
        static readonly Regex RxRelevant = new Regex(
            @"航空|民航|航班|航司|航线|航線|机场|機場|空港|飞机|飛機|客机|客機|货机|貨機|"
            + @"机队|機隊|机组|機組|飞行员|空乘|塔台|适航|適航|航权|航權|时刻槽|值机|登机|登機|航站楼|航站樓|"
            + @"空域|空管|航空管制|流量控制|跑道|机务|签派|航路|备降|迫降|复飞|飞行安全|空中管制|客座率|通用航空|支线|"
            + @"航油|航空煤油|航空燃油|可持续航空燃料|"
            + @"波音|空客|商飞|"
            + @"台风|雷暴|大雾|暴雪|寒潮|强对流|冰雹|沙尘|火山灰",
            RegexOptions.Compiled);

        static List<ExternalEventPayload> Filter(List<ExternalEventPayload> list)
        {
            var kept = new List<ExternalEventPayload>();
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null) continue;
                string hay = (p.title ?? "") + " " + (p.content ?? "");
                if (RxNoise.IsMatch(hay)) continue;
                bool hasEffect = !string.IsNullOrEmpty(p.effectType);
                if (!hasEffect && !RxRelevant.IsMatch(hay)) continue;
                kept.Add(p);
            }
            return kept;
        }

        /// <summary>
        /// 重要度打分（同轮排序用）：直接带数值影响 &gt; 影响运行安全/成本的高权重词 &gt; 一般民航主体词。
        /// 关键词只决定「要不要」，分数决定「先要哪条」；每日上限决定「最多要几条」。
        /// </summary>
        static readonly Regex RxHighImpact = new Regex(
            @"民航局|空管局|空域|禁飞|停飞|停航|大面积延误|取消航班|关闭机场|机场关闭|"
            + @"航油|燃油附加费|适航指令|紧急适航|事故|坠机|空难|罢工|台风|熔断|"
            + @"恢复通航|复航|新增航线|时刻放量|免签",
            RegexOptions.Compiled);

        static int ScoreOf(ExternalEventPayload p)
        {
            if (p == null) return 0;
            string hay = (p.title ?? "") + " " + (p.content ?? "");
            int s = 0;
            if (!string.IsNullOrEmpty(p.effectType)) s += 5;
            if (RxHighImpact.IsMatch(hay)) s += 3;
            if (RxRelevant.IsMatch(hay)) s += 2;
            if (string.Equals(p.category, "监管", StringComparison.Ordinal)) s += 1;
            if (p.publishedAt.HasValue
                && (DateTime.UtcNow - p.publishedAt.Value).TotalHours < 6) s += 1;
            return s;
        }

        /// <summary>标题指纹：去空白/标点后的小写串，用于「同一件事被多家转载只入队一次」。</summary>
        public static string TitleKey(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            string s = Regex.Replace(title,
                @"[\s\u3000…—\-_,，。；;：:、""'“”‘’（）()\[\]【】《》!！?？·|/\\]", "");
            return s.ToLowerInvariant();
        }

        static string Short(string url)
        {
            if (string.IsNullOrEmpty(url)) return "?";
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return HostOf(url);
            try { return Path.GetFileName(url); } catch { return "file"; }
        }

        /// <summary>分类：按来源与标题粗分（市场/监管/季节/行业/竞对）。</summary>
        static string CategoryOf(string source, string title)
        {
            string hay = (source ?? "") + (title ?? "");
            if (RxSeason.IsMatch(hay)) return "季节";
            if (RxRegulation.IsMatch(hay)) return "监管";
            if (RxMarket.IsMatch(hay)) return "市场";
            if (RxIndustry.IsMatch(hay)) return "行业";
            return "行业";
        }

        static readonly Regex RxSeason = new Regex(@"台风|暴雨|雷暴|大雾|暴雪|寒潮|高温|天气|气象|地震|火山", RegexOptions.Compiled);
        static readonly Regex RxRegulation = new Regex(@"局|署|部委|委员会|监管|适航指令|空域|政策|通告|通报|协定|谈判", RegexOptions.Compiled);
        static readonly Regex RxMarket = new Regex(@"油价|燃油|航油|汇率|人民币|美元|利率|指数|价格|成本|需求", RegexOptions.Compiled);
        static readonly Regex RxIndustry = new Regex(@"波音|空客|商飞|交付|订单|产能|机型|维修|航展", RegexOptions.Compiled);

        // ———— 演示载荷（离线自测 / 编辑器菜单）————

        /// <summary>方案文档示例：现实 9/11 发布「9/12 零时起调价」+「预计 3 天后登陆」。</summary>
        public static List<ExternalEventPayload> DemoPayloads()
        {
            return new List<ExternalEventPayload>
            {
                new ExternalEventPayload
                {
                    id = "ndrc_fuel_20260911",
                    category = "市场",
                    title = "发改委调整成品油价格",
                    content = "国家发展改革委决定，自2026年9月12日零时起，国内汽、柴油价格每吨分别上调 260 元和 250 元。",
                    source = "发改委",
                    publishedAt = new DateTime(2026, 9, 11, 15, 0, 0, DateTimeKind.Utc),
                    realEffectiveAt = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
                    effectType = "fuelAbs",
                    effectValue = 26f
                },
                new ExternalEventPayload
                {
                    id = "typhoon_swa_demo",
                    category = "季节",
                    title = "台风将影响粤东机场",
                    content = "今年第14号台风预计3天后登陆粤东，汕头机场可能短时关闭。",
                    source = "中央气象台",
                    publishedAt = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc),
                    effectType = "freezeFlights",
                    effectValue = 0f,
                    affectedRegion = "SWA",
                    durationHours = 48
                },
                new ExternalEventPayload
                {
                    id = "rss_style_news_demo",
                    category = "行业",
                    title = "某航司宣布开通与本公司枢纽重叠的洲际新航线",
                    content = "",
                    source = "航空周刊",
                    publishedAt = new DateTime(2026, 9, 11, 8, 0, 0, DateTimeKind.Utc)
                }
            };
        }
    }
}
