using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace AirlineCEO.UI.Shell
{
    /// <summary>
    /// n115 外部信息源（游戏内配置，不必开编辑器）。
    /// <para>
    /// 职责边界：这里只管「取数口」——现实内容从哪来、多久取一次、每轮收几条、要不要过滤；
    /// 时间轴仍由游戏掌握（现实日期全部换成相对措辞），收到的内容进队列后按游戏时钟生效。
    /// </para>
    /// <para>
    /// 覆盖与编辑器菜单「AirlineCEO/外部数据源」同一份 PlayerPrefs 配置，两处改法等价。
    /// </para>
    /// </summary>
    public class ExternalSourcePanelUI : BasePanel
    {
        /// <summary>
        /// 页面 id（PrefabNavIntegration 注册、设置页可直达）。
        /// 命名避开 <see cref="BasePanel.PageId"/> —— 那是 BindPage 写入的运行时实例属性，
        /// 同名常量会遮蔽它并触发 CS0108。
        /// </summary>
        public const string PageKey = "n115_外部信息源";

        /// <summary>间隔候选（现实秒）：关闭 / 5 / 15 / 30 / 60 / 120 分钟。</summary>
        static readonly int[] IntervalChoices = { 0, 300, 900, 1800, 3600, 7200 };
        static readonly string[] RowKeys =
        {
            "LOC_EXT_ENABLED", "LOC_EXT_INTERVAL", "LOC_EXT_MAXPER", "LOC_EXT_FILTER", "LOC_EXT_DAILY"
        };

        /// <summary>附加管道（与新闻源独立取数）的配置键：中央气象台 / NASA 全球风暴 / 国际 SIGMET。</summary>
        static readonly string[] FeedKeys = { "LOC_TYPHOON_ENABLED", "LOC_STORM_ENABLED", "LOC_SIGMET_ENABLED" };

        readonly List<TextMeshProUGUI> _rowTitles = new List<TextMeshProUGUI>();
        readonly List<TextMeshProUGUI> _rowValues = new List<TextMeshProUGUI>();
        readonly List<TextMeshProUGUI> _feedLabels = new List<TextMeshProUGUI>();
        TextMeshProUGUI _status;
        TextMeshProUGUI _quoteLabel;   // 「行情源 ▸」按钮上的文字，实时反映油价行情开/关
        TMP_InputField _url;
        string _baseline = "";
        string _note = "";
        bool _polling;
        float _nextPoll;

        public static ExternalSourcePanelUI Build()
        {
            var root = UIFactory.Panel(null, PageKey, UIFactory.Fill, UITheme.Panel);
            var cg = root.AddComponent<CanvasGroup>();
            var title = UIFactory.Label(root.transform, "Title", Loc.T("LOC_EXT_TITLE"), 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.90f), new Vector2(0.7f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(root.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.92f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 22);

            var panel = root.AddComponent<ExternalSourcePanelUI>();
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);

            panel.BuildRows(root.transform);
            panel.BuildUrl(root.transform);

            // 状态块：取数结果 + 队列（随时间/拉取实时刷新）
            panel._status = UIFactory.Label(root.transform, "Status", "", 13,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.246f), new Vector2(0.96f, 0.410f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            // 行情源入口（油价现实报价作参考基准，配置与新闻源分开；按钮文字带开/关状态）
            var quoteBtn = UIFactory.Button(root.transform, "QuoteLink", Loc.T("LOC_QUOTE_OPEN"),
                new UIFactory.Anchor(new Vector2(0.70f, 0.90f), new Vector2(0.88f, 0.975f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () =>
                {
                    Object.FindFirstObjectByType<PrefabNavIntegration>()?.OpenSubPage(QuoteSourcePanelUI.PageKey);
                }, 13);
            panel._quoteLabel = quoteBtn.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();

            // 说明块：机制口径（固定文案，不随状态跳动）
            UIFactory.Label(root.transform, "Hint",
                "现实只提供内容，时间轴由游戏掌握：\n" +
                "· 新闻默认走国内可直连站点（中新网 + 新浪财经），无需 VPN；断网静默降级。\n" +
                "· 关键词决定「要不要」，重要度决定「先要哪条」，每游戏日上限决定「最多几条」。\n" +
                "· 附加管道与新闻分开取数：台风走中央气象台，SIGMET 走国际航行警告，全球风暴走 NASA。\n" +
                "· 额度不够时先保运行风险（SIGMET / 台风 / 风暴 / 气象实况），新闻进滞留队列补投。",
                13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.140f), new Vector2(0.96f, 0.240f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            panel.BuildBottom(root.transform);
            panel.RefreshAll();
            return panel;
        }

        /// <summary>设置面板一行的摘要文案（开着就是「已开启 · 每 30 分钟」）。</summary>
        public static string Summary()
        {
            if (!ExternalDataFetcher.Enabled) return Loc.T("LOC_EXT_SUM_OFF");
            return Loc.T("LOC_EXT_SUM_ON", IntervalLabel(ExternalDataFetcher.IntervalSeconds));
        }

        static string IntervalLabel(int seconds)
        {
            if (seconds <= 0) return Loc.T("LOC_EXT_OFF_LABEL");
            if (seconds % 3600 == 0) return Loc.T("LOC_EXT_EVERY_HOUR", seconds / 3600);
            return Loc.T("LOC_EXT_EVERY_MIN", Mathf.Max(1, seconds / 60));
        }

        void BuildRows(Transform parent)
        {
            MakeRow(parent, 0, "LOC_EXT_ENABLED", () =>
            {
                ExternalDataFetcher.Enabled = !ExternalDataFetcher.Enabled;
                RefreshAll();
            });
            MakeRow(parent, 1, "LOC_EXT_INTERVAL", () =>
            {
                int cur = ExternalDataFetcher.IntervalSeconds;
                int idx = 0;
                for (int i = 0; i < IntervalChoices.Length; i++)
                    if (IntervalChoices[i] == cur) { idx = i; break; }
                ExternalDataFetcher.IntervalSeconds = IntervalChoices[(idx + 1) % IntervalChoices.Length];
                RefreshAll();
            });
            MakeRow(parent, 2, "LOC_EXT_MAXPER", () =>
            {
                int next = ExternalDataFetcher.MaxPerFetch + 1;
                ExternalDataFetcher.MaxPerFetch = next > 5 ? 1 : next;
                RefreshAll();
            });
            MakeRow(parent, 3, "LOC_EXT_FILTER", () =>
            {
                ExternalDataFetcher.RelevanceFilter = !ExternalDataFetcher.RelevanceFilter;
                RefreshAll();
            });
            MakeRow(parent, 4, "LOC_EXT_DAILY", () =>
            {
                int next = ExternalDataFetcher.DailyBudget + 2;
                ExternalDataFetcher.DailyBudget = next > 20 ? 2 : next;
                RefreshAll();
            });
            BuildFeedRow(parent, 5);
        }

        /// <summary>
        /// 「附加管道」一行三钮：台风（中央气象台）/ 全球风暴（NASA EONET）/ SIGMET（国际航行警告）。
        /// 三者与新闻源独立取数、独立开关，挤在一行是为了不把下面的 URL 与状态块顶出可视区。
        /// </summary>
        void BuildFeedRow(Transform parent, int index)
        {
            float top = 0.878f - index * 0.060f;
            float bottom = top - 0.052f;

            var titleLabel = UIFactory.Label(parent, "FeedTitle", Loc.T("LOC_EXT_SUPP"), 16,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, bottom), new Vector2(0.28f, top), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _rowTitles.Add(titleLabel);

            float[] lefts = { 0.28f, 0.515f, 0.75f };
            float[] rights = { 0.505f, 0.74f, 0.96f };
            for (int i = 0; i < FeedKeys.Length; i++)
            {
                int idx = i;
                var anchor = new UIFactory.Anchor(new Vector2(lefts[i], bottom + 0.006f),
                    new Vector2(rights[i], top - 0.006f), Vector2.zero, Vector2.zero);
                var btn = UIFactory.Button(parent, "Feed" + i, "", anchor, UITheme.PanelDeep,
                    () => ToggleFeed(idx), 12);
                var label = btn.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Center;
                label.color = UITheme.AccentHover;
                _feedLabels.Add(label);
            }
        }

        void ToggleFeed(int index)
        {
            switch (index)
            {
                case 0: TyphoonFeed.Enabled = !TyphoonFeed.Enabled; break;
                case 1: GlobalStormFeed.Enabled = !GlobalStormFeed.Enabled; break;
                case 2: SigmetFeed.Enabled = !SigmetFeed.Enabled; break;
            }
            RefreshAll();
        }

        void MakeRow(Transform parent, int index, string titleKey, UnityAction onClick)
        {
            float top = 0.878f - index * 0.060f;
            float bottom = top - 0.052f;

            var titleLabel = UIFactory.Label(parent, "Row" + index + "_Title", Loc.T(titleKey), 16,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, bottom), new Vector2(0.44f, top), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _rowTitles.Add(titleLabel);

            var valueAnchor = new UIFactory.Anchor(new Vector2(0.46f, bottom + 0.006f),
                new Vector2(0.96f, top - 0.006f), Vector2.zero, Vector2.zero);
            var btn = UIFactory.Button(parent, "Row" + index + "_Value", "", valueAnchor, UITheme.PanelDeep, onClick, 15);
            var value = btn.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            value.alignment = TextAlignmentOptions.MidlineRight;
            value.color = UITheme.AccentHover;
            _rowValues.Add(value);
        }

        void BuildUrl(Transform parent)
        {
            UIFactory.Label(parent, "UrlTitle", Loc.T("LOC_EXT_URL"), 16,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.486f), new Vector2(0.9f, 0.514f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            _url = UIFactory.Input(parent, "UrlField", Loc.T("LOC_EXT_URL_HINT"),
                new UIFactory.Anchor(new Vector2(0.04f, 0.420f), new Vector2(0.96f, 0.478f), Vector2.zero, Vector2.zero));
            _url.text = ExternalDataFetcher.SourceUrl;
            _url.onEndEdit.AddListener(ApplyUrl);
            _url.onSubmit.AddListener(ApplyUrl);
        }

        void ApplyUrl(string value)
        {
            string v = (value ?? "").Trim();
            if (string.IsNullOrEmpty(v)) v = ExternalDataFetcher.DefaultUrl;
            if (v == ExternalDataFetcher.SourceUrl) return;
            ExternalDataFetcher.SourceUrl = v;
            // 换了源，旧的去重表不再适用，避免新源内容被当成「已见过」
            ExternalDataFetcher.Reset();
            _note = "已切换数据源，去重表已清空。";
            RefreshAll();
        }

        void BuildBottom(Transform parent)
        {
            float y0 = 0.04f, y1 = 0.135f;
            UIFactory.Button(parent, "Fetch", Loc.T("LOC_EXT_FETCH_NOW"),
                new UIFactory.Anchor(new Vector2(0.03f, y0), new Vector2(0.21f, y1), Vector2.zero, Vector2.zero),
                UITheme.Accent, FetchNow, 12);
            UIFactory.Button(parent, "Default", Loc.T("LOC_EXT_DEFAULT"),
                new UIFactory.Anchor(new Vector2(0.22f, y0), new Vector2(0.40f, y1), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, RestoreDefaults, 12);
            UIFactory.Button(parent, "Clear", Loc.T("LOC_EXT_CLEAR"),
                new UIFactory.Anchor(new Vector2(0.41f, y0), new Vector2(0.58f, y1), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, ClearCache, 12);
            UIFactory.Button(parent, "Inject", Loc.T("LOC_EXT_INJECT"),
                new UIFactory.Anchor(new Vector2(0.59f, y0), new Vector2(0.78f, y1), Vector2.zero, Vector2.zero),
                UITheme.Warning, InjectDemo, 12);
            UIFactory.Button(parent, "Return", Loc.T("LOC_EXT_BACK"),
                new UIFactory.Anchor(new Vector2(0.79f, y0), new Vector2(0.97f, y1), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, BackOne, 12);
        }

        void FetchNow()
        {
            _baseline = ExternalDataFetcher.LastStatus;
            _note = Loc.T("LOC_EXT_FETCHING");
            _polling = true;
            ExternalDataFetcher.RequestRefresh(true);
            RefreshStatus();
        }

        void RestoreDefaults()
        {
            ExternalDataFetcher.SourceUrl = ExternalDataFetcher.DefaultUrl;
            ExternalDataFetcher.IntervalSeconds = ExternalDataFetcher.DefaultIntervalSeconds;
            ExternalDataFetcher.MaxPerFetch = ExternalDataFetcher.DefaultMaxPerFetch;
            ExternalDataFetcher.DailyBudget = ExternalDataFetcher.DefaultDailyBudget;
            ExternalDataFetcher.RelevanceFilter = true;
            ExternalDataFetcher.Enabled = true;
            ExternalDataFetcher.Reset();
            TyphoonFeed.Enabled = true;
            TyphoonFeed.Reset();
            GlobalStormFeed.Enabled = true;
            GlobalStormFeed.Reset();
            SigmetFeed.Enabled = true;
            SigmetFeed.Reset();
            if (_url != null) _url.text = ExternalDataFetcher.SourceUrl;
            _note = "已恢复默认国内源（中新网 + 新浪财经），去重表与附加管道缓存已清空。";
            RefreshAll();
        }

        void ClearCache()
        {
            ExternalDataFetcher.Reset();
            _note = "已清空缓存与去重表（源地址配置保留）。";
            RefreshAll();
        }

        void InjectDemo()
        {
            ExternalDataFetcher.Inject(ExternalDataFetcher.DemoPayloads());
            var gs = GameState.Instance;
            int n = gs != null ? ExternalEventManager.DrainSource(gs) : 0;
            _note = n > 0
                ? $"已投放演示载荷 {n} 条，进入队列后按游戏时间生效。"
                : "已投放演示载荷；进入游戏后由游戏时钟入队。";
            RefreshAll();
        }

        void Update()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.4f;

            if (_polling)
            {
                string cur = ExternalDataFetcher.LastStatus;
                if (cur != _baseline)
                {
                    _polling = false;
                    _baseline = cur;
                    var gs = GameState.Instance;
                    int n = gs != null ? ExternalEventManager.DrainSource(gs) : 0;
                    _note = n > 0
                        ? $"本轮收到 {n} 条，已入队，按各自相对时间在游戏内生效。"
                        : "本轮没有新内容（源不可达或暂无更新）。";
                }
            }

            RefreshRowValues();
            RefreshStatus();
        }

        void RefreshAll()
        {
            if (titleLabel != null) titleLabel.text = Loc.T("LOC_EXT_TITLE");
            for (int i = 0; i < _rowTitles.Count && i < RowKeys.Length; i++)
                _rowTitles[i].text = Loc.T(RowKeys[i]);
            if (_rowTitles.Count > RowKeys.Length)
                _rowTitles[RowKeys.Length].text = Loc.T("LOC_EXT_SUPP");
            if (_url != null && !_url.isFocused)
                _url.text = ExternalDataFetcher.SourceUrl;
            RefreshRowValues();
            RefreshStatus();
        }

        void RefreshRowValues()
        {
            SetRow(0, Loc.T(ExternalDataFetcher.Enabled ? "LOC_ON" : "LOC_OFF"));
            SetRow(1, IntervalLabel(ExternalDataFetcher.IntervalSeconds));
            SetRow(2, Loc.T("LOC_EXT_ITEMS", ExternalDataFetcher.MaxPerFetch));
            SetRow(3, Loc.T(ExternalDataFetcher.RelevanceFilter ? "LOC_ON" : "LOC_OFF"));
            SetRow(4, Loc.T("LOC_EXT_ITEMS", ExternalDataFetcher.DailyBudget));

            for (int i = 0; i < _feedLabels.Count && i < FeedKeys.Length; i++)
            {
                bool on;
                switch (i)
                {
                    case 0: on = TyphoonFeed.Enabled; break;
                    case 1: on = GlobalStormFeed.Enabled; break;
                    default: on = SigmetFeed.Enabled; break;
                }
                string name = Loc.T(i == 0 ? "LOC_FEED_TYPHOON" : i == 1 ? "LOC_FEED_STORM" : "LOC_FEED_SIGMET");
                _feedLabels[i].text = name + "·" + (on ? "开" : "关");
                _feedLabels[i].color = on ? UITheme.AccentHover : UITheme.TextMuted;
            }
        }

        void SetRow(int index, string text)
        {
            if (index < _rowValues.Count && _rowValues[index] != null)
                _rowValues[index].text = text;
        }

        void RefreshStatus()
        {
            if (_status == null) return;
            var gs = GameState.Instance;
            var sb = new StringBuilder();
            string last = ExternalDataFetcher.LastStatus;
            sb.Append("获取状态：").Append(string.IsNullOrEmpty(last) ? Loc.T("LOC_EXT_NEVER") : last);

            var list = ExternalEventManager.Ensure(gs);
            int pending = ExternalEventManager.Pending(gs).Count;
            sb.Append('\n').Append(Loc.T("LOC_EXT_QUEUE", pending, list != null ? list.Count : 0));

            // 油价 / 汇率 / 气象各自独立管道，这里一并给出实时状态，避免只看到新闻那一半
            sb.Append('\n').Append(ExternalDataFetcher.QuoteStatusLine());
            sb.Append('\n').Append(ExternalDataFetcher.FxStatusLine());
            sb.Append('\n').Append("气象实况：")
              .Append(string.IsNullOrEmpty(WeatherFeed.Status) ? "已开启，尚未取数" : WeatherFeed.Status);
            sb.Append('\n').Append("台风公报：")
              .Append(TyphoonFeed.Enabled
                  ? (string.IsNullOrEmpty(TyphoonFeed.Status) ? "已开启，尚未取数" : TyphoonFeed.Status)
                  : "已关闭");
            sb.Append('\n').Append("全球风暴：")
              .Append(GlobalStormFeed.Enabled
                  ? (string.IsNullOrEmpty(GlobalStormFeed.Status) ? "已开启，尚未取数" : GlobalStormFeed.Status)
                  : "已关闭");
            sb.Append("　·　SIGMET：")
              .Append(SigmetFeed.Enabled
                  ? (string.IsNullOrEmpty(SigmetFeed.Status) ? "已开启，尚未取数" : SigmetFeed.Status)
                  : "已关闭");
            if (gs != null)
            {
                string forecast = MacroEconomyManager.QuoteConvergeForecast(gs);
                if (!string.IsNullOrEmpty(forecast))
                    sb.Append("　·　本季收敛后 ").Append(forecast);
                sb.Append('\n').Append("今日入队：")
                  .Append(ExternalEventManager.ReceivedToday(gs))
                  .Append(" / ").Append(ExternalDataFetcher.DailyBudget);
            }

            var latest = LatestReceived(list);
            if (latest != null)
            {
                sb.Append("\n最近：").Append(latest.title)
                  .Append("（").Append(latest.isTriggered ? "已生效" : "待生效")
                  .Append(" · ").Append(string.IsNullOrEmpty(latest.relativeDesc) ? "即刻" : latest.relativeDesc)
                  .Append("）");
            }
            if (!string.IsNullOrEmpty(_note))
                sb.Append('\n').Append(_note);

            _status.text = ChineseFontProvider.Sanitize(sb.ToString());

            if (_quoteLabel != null)
                _quoteLabel.text = Loc.T(ExternalDataFetcher.QuoteEnabled ? "LOC_QUOTE_OPEN_ON" : "LOC_QUOTE_OPEN_OFF");
        }

        /// <summary>最近一次「接收」的事件（队列按生效时间排序，故按接收时刻找最新）。</summary>
        static ExternalEvent LatestReceived(List<ExternalEvent> list)
        {
            if (list == null) return null;
            ExternalEvent best = null;
            for (int i = 0; i < list.Count; i++)
            {
                var ev = list[i];
                if (ev == null) continue;
                if (best == null || ev.gameReceivedTicks > best.gameReceivedTicks) best = ev;
            }
            return best;
        }
    }
}
