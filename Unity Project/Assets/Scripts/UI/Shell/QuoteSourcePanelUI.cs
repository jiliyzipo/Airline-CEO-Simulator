using System.Collections.Generic;
using System.Text;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace AirlineCEO.UI.Shell
{
    /// <summary>
    /// n116 行情源 · 油价（游戏内配置）。
    /// <para>
    /// 定位：现实报价只作**参考基准** —— 开启后航油价每季最多向现实价靠拢 ±8%，
    /// 游戏自身的季度漂移、随机事件、连锁事件仍是油价的主导。**默认开启**（可在本页关掉）；
    /// 关闭时不发起任何请求，行为与未接行情完全一致。
    /// </para>
    /// <para>
    /// 换算口径：航油（美元/吨）= 原油（美元/桶）× 7.82 + 裂解价差，结果裁到游戏区间 [400, 1600]。
    /// 7.82 由 Jet A-1 密度 ≈0.804 kg/L（1 桶 =158.987 L → 127.8 kg）反算。
    /// </para>
    /// </summary>
    public class QuoteSourcePanelUI : BasePanel
    {
        /// <summary>
        /// 页面 id（注册到 shell 目录；从 n115 外部信息源进入）。
        /// 命名避开 <see cref="BasePanel.PageId"/> —— 那是 BindPage 写入的运行时实例属性，
        /// 同名常量会遮蔽它并触发 CS0108。
        /// </summary>
        public const string PageKey = "n116_行情源";

        /// <summary>报价间隔候选（现实秒）：手动 / 30 分 / 1 时 / 6 时 / 12 时 / 24 时。</summary>
        static readonly int[] IntervalChoices = { 0, 1800, 3600, 21600, 43200, 86400 };
        /// <summary>裂解价差候选（美元/吨）：≈13 / 19 / 26 / 32 / 38 美元每桶。</summary>
        static readonly float[] CrackChoices = { 100f, 150f, 200f, 250f, 300f };
        static readonly string[] RowKeys =
        {
            "LOC_QUOTE_ENABLED", "LOC_QUOTE_INTERVAL", "LOC_QUOTE_CRACK"
        };

        readonly List<TextMeshProUGUI> _rowTitles = new List<TextMeshProUGUI>();
        readonly List<TextMeshProUGUI> _rowValues = new List<TextMeshProUGUI>();
        TextMeshProUGUI _status;
        TMP_InputField _url;
        string _baseline = "";
        string _note = "";
        bool _polling;
        float _nextPoll;

        public static QuoteSourcePanelUI Build()
        {
            var root = UIFactory.Panel(null, PageKey, UIFactory.Fill, UITheme.Panel);
            var cg = root.AddComponent<CanvasGroup>();
            var title = UIFactory.Label(root.transform, "Title", Loc.T("LOC_QUOTE_TITLE"), 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.90f), new Vector2(0.7f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(root.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.92f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 22);

            var panel = root.AddComponent<QuoteSourcePanelUI>();
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);

            panel.BuildRows(root.transform);
            panel.BuildUrl(root.transform);

            panel._status = UIFactory.Label(root.transform, "Status", "", 14,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.315f), new Vector2(0.96f, 0.50f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            UIFactory.Label(root.transform, "Hint",
                "现实报价只作参考基准，油价仍由游戏自身驱动（本项默认开启，可在此关掉）：\n" +
                "· 开启后每季最多向现实价靠拢 ±8%，不会一次跳到现实价。\n" +
                "· 换算：原油（美元/桶）× 7.82 + 裂解价差，结果裁到 400–1600 美元/吨。\n" +
                "· 关闭 / 断网 / 报价异常时，油价完全按游戏宏观引擎走，季报里不会有收敛条目。",
                13, TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.155f), new Vector2(0.96f, 0.305f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted);

            panel.BuildBottom(root.transform);
            panel.RefreshAll();
            return panel;
        }

        /// <summary>设置 / n115 上的摘要文案。</summary>
        public static string Summary()
        {
            if (!ExternalDataFetcher.QuoteEnabled) return Loc.T("LOC_QUOTE_SUM_OFF");
            return Loc.T("LOC_QUOTE_SUM_ON", IntervalLabel(ExternalDataFetcher.QuoteIntervalSeconds));
        }

        static string IntervalLabel(int seconds)
        {
            if (seconds <= 0) return Loc.T("LOC_EXT_OFF_LABEL");
            if (seconds % 3600 == 0) return Loc.T("LOC_QUOTE_EVERY_DAY", seconds / 3600);
            return Loc.T("LOC_EXT_EVERY_MIN", Mathf.Max(1, seconds / 60));
        }

        void BuildRows(Transform parent)
        {
            MakeRow(parent, 0, "LOC_QUOTE_ENABLED", () =>
            {
                ExternalDataFetcher.QuoteEnabled = !ExternalDataFetcher.QuoteEnabled;
                if (ExternalDataFetcher.QuoteEnabled) FetchNow();
                RefreshAll();
            });
            MakeRow(parent, 1, "LOC_QUOTE_INTERVAL", () =>
            {
                int cur = ExternalDataFetcher.QuoteIntervalSeconds;
                int idx = 0;
                for (int i = 0; i < IntervalChoices.Length; i++)
                    if (IntervalChoices[i] == cur) { idx = i; break; }
                ExternalDataFetcher.QuoteIntervalSeconds = IntervalChoices[(idx + 1) % IntervalChoices.Length];
                RefreshAll();
            });
            MakeRow(parent, 2, "LOC_QUOTE_CRACK", () =>
            {
                float cur = ExternalDataFetcher.CrackSpreadUsdPerTon;
                int idx = 0;
                float best = float.MaxValue;
                for (int i = 0; i < CrackChoices.Length; i++)
                {
                    float d = Mathf.Abs(CrackChoices[i] - cur);
                    if (d < best) { best = d; idx = i; }
                }
                ExternalDataFetcher.CrackSpreadUsdPerTon = CrackChoices[(idx + 1) % CrackChoices.Length];
                _note = "裂解价差已改：下一轮报价按新口径换算。";
                RefreshAll();
            });
        }

        void MakeRow(Transform parent, int index, string titleKey, UnityAction onClick)
        {
            float top = 0.855f - index * 0.068f;
            float bottom = top - 0.058f;

            var titleLabel = UIFactory.Label(parent, "Row" + index + "_Title", Loc.T(titleKey), 16,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, bottom), new Vector2(0.52f, top), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);
            _rowTitles.Add(titleLabel);

            var valueAnchor = new UIFactory.Anchor(new Vector2(0.54f, bottom + 0.006f),
                new Vector2(0.96f, top - 0.006f), Vector2.zero, Vector2.zero);
            var btn = UIFactory.Button(parent, "Row" + index + "_Value", "", valueAnchor, UITheme.PanelDeep, onClick, 15);
            var value = btn.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            value.alignment = TextAlignmentOptions.MidlineRight;
            value.color = UITheme.AccentHover;
            _rowValues.Add(value);
        }

        void BuildUrl(Transform parent)
        {
            UIFactory.Label(parent, "UrlTitle", Loc.T("LOC_QUOTE_URL"), 16,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.607f), new Vector2(0.9f, 0.650f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary);

            _url = UIFactory.Input(parent, "UrlField", Loc.T("LOC_QUOTE_URL_HINT"),
                new UIFactory.Anchor(new Vector2(0.04f, 0.528f), new Vector2(0.96f, 0.595f), Vector2.zero, Vector2.zero));
            _url.text = ExternalDataFetcher.QuoteUrl;
            _url.onEndEdit.AddListener(ApplyUrl);
            _url.onSubmit.AddListener(ApplyUrl);
        }

        void ApplyUrl(string value)
        {
            string v = (value ?? "").Trim();
            if (string.IsNullOrEmpty(v)) v = ExternalDataFetcher.DefaultQuoteUrl;
            if (v == ExternalDataFetcher.QuoteUrl) return;
            ExternalDataFetcher.QuoteUrl = v;
            _note = "已切换行情地址，下次获取按新地址取数。";
            RefreshAll();
        }

        void BuildBottom(Transform parent)
        {
            float y0 = 0.04f, y1 = 0.135f;
            UIFactory.Button(parent, "Fetch", Loc.T("LOC_QUOTE_FETCH"),
                new UIFactory.Anchor(new Vector2(0.03f, y0), new Vector2(0.26f, y1), Vector2.zero, Vector2.zero),
                UITheme.Accent, FetchNow, 12);
            UIFactory.Button(parent, "Default", Loc.T("LOC_EXT_DEFAULT"),
                new UIFactory.Anchor(new Vector2(0.27f, y0), new Vector2(0.47f, y1), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, RestoreDefaults, 12);
            UIFactory.Button(parent, "Inject", Loc.T("LOC_QUOTE_INJECT"),
                new UIFactory.Anchor(new Vector2(0.48f, y0), new Vector2(0.68f, y1), Vector2.zero, Vector2.zero),
                UITheme.Warning, InjectDemo, 12);
            UIFactory.Button(parent, "Return", Loc.T("LOC_EXT_BACK"),
                new UIFactory.Anchor(new Vector2(0.69f, y0), new Vector2(0.86f, y1), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, BackOne, 12);
        }

        void FetchNow()
        {
            if (!ExternalDataFetcher.QuoteEnabled)
            {
                _note = "油价行情未开启：先在上一行打开，再取报价。";
                RefreshStatus();
                return;
            }
            _baseline = ExternalDataFetcher.QuoteStatus;
            _note = Loc.T("LOC_EXT_FETCHING");
            _polling = true;
            ExternalDataFetcher.RequestQuoteRefresh(true);
            RefreshStatus();
        }

        void RestoreDefaults()
        {
            ExternalDataFetcher.QuoteUrl = ExternalDataFetcher.DefaultQuoteUrl;
            ExternalDataFetcher.QuoteIntervalSeconds = ExternalDataFetcher.DefaultQuoteIntervalSeconds;
            ExternalDataFetcher.CrackSpreadUsdPerTon = ExternalDataFetcher.DefaultCrackSpreadUsdPerTon;
            if (_url != null) _url.text = ExternalDataFetcher.QuoteUrl;
            _note = "已恢复默认行情口径（开关未改动）。";
            RefreshAll();
        }

        /// <summary>离线自测：按「布伦特 80 美元/桶」注入一条报价，方便断网时验证收敛链路。</summary>
        void InjectDemo()
        {
            if (!ExternalDataFetcher.QuoteEnabled)
            {
                _note = "油价行情未开启：先在上一行打开，再注入报价。";
                RefreshStatus();
                return;
            }
            float brent = 80f;
            float jet = brent * ExternalDataFetcher.BarrelsPerTonJet + ExternalDataFetcher.CrackSpreadUsdPerTon;
            ExternalDataFetcher.InjectOilQuote(jet, "自测·布伦特80");
            _note = $"已注入报价：布伦特 {brent:F0} 美元/桶 → 航油 {jet:F0} 美元/吨；下一季结算时按 ±8% 靠拢。";
            RefreshAll();
        }

        void Update()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.4f;

            if (_polling)
            {
                string cur = ExternalDataFetcher.QuoteStatus;
                if (cur != _baseline)
                {
                    _polling = false;
                    _baseline = cur;
                    _note = ExternalDataFetcher.CachedOilPricePerTon > 0f
                        ? "报价已更新：下一季结算时按 ±8% 向该价格靠拢。"
                        : "本轮没拿到报价（地址不可达或字段无法识别）。";
                }
            }

            RefreshRowValues();
            RefreshStatus();
        }

        void RefreshAll()
        {
            if (titleLabel != null) titleLabel.text = Loc.T("LOC_QUOTE_TITLE");
            for (int i = 0; i < _rowTitles.Count && i < RowKeys.Length; i++)
                _rowTitles[i].text = Loc.T(RowKeys[i]);
            if (_url != null && !_url.isFocused)
                _url.text = ExternalDataFetcher.QuoteUrl;
            RefreshRowValues();
            RefreshStatus();
        }

        void RefreshRowValues()
        {
            SetRow(0, Loc.T(ExternalDataFetcher.QuoteEnabled ? "LOC_ON" : "LOC_OFF"));
            SetRow(1, IntervalLabel(ExternalDataFetcher.QuoteIntervalSeconds));
            SetRow(2, ExternalDataFetcher.CrackSpreadUsdPerTon.ToString("F0"));
        }

        void SetRow(int index, string text)
        {
            if (index < _rowValues.Count && _rowValues[index] != null)
                _rowValues[index].text = text;
        }

        void RefreshStatus()
        {
            if (_status == null) return;
            var sb = new StringBuilder();

            // 当前报价 / 换算 / 来源新鲜度：统一取共享口径，避免本页与其它页面写法漂移
            float cachedNow = ExternalDataFetcher.CachedOilPricePerTon;
            if (cachedNow <= 0f)
                sb.Append(Loc.T("LOC_QUOTE_CURRENT")).Append("：").Append(Loc.T("LOC_QUOTE_NEVER"));
            else
                sb.Append(ExternalDataFetcher.QuoteStatusLine());

            string st = ExternalDataFetcher.QuoteStatus;
            sb.Append('\n').Append(Loc.T("LOC_QUOTE_CALC")).Append("：")
              .Append(string.IsNullOrEmpty(st) ? Loc.T("LOC_EXT_NEVER") : st);

            var gs = GameState.Instance;
            if (gs != null)
            {
                sb.Append('\n').Append(Loc.T("LOC_QUOTE_GAME")).Append("：")
                  .Append(gs.fuelPricePerTon.ToString("F0")).Append(" 美元/吨");
                string forecast = MacroEconomyManager.QuoteConvergeForecast(gs);
                if (!string.IsNullOrEmpty(forecast))
                    sb.Append("　·　").Append(Loc.T("LOC_QUOTE_NEXT")).Append("：").Append(forecast);
            }

            if (!string.IsNullOrEmpty(_note))
                sb.Append('\n').Append(_note);

            _status.text = ChineseFontProvider.Sanitize(sb.ToString());
        }
    }
}
