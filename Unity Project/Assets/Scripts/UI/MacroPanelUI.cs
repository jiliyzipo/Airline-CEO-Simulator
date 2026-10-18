using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>n28 宏观经济面板（L2）：周期阶段 + GDP/油价/汇率走势 + 传导摘要。</summary>
    public class MacroPanelUI : BasePanel
    {
        TextMeshProUGUI _body;

        public static MacroPanelUI Build()
        {
            var root = UIFactory.Panel(null, "MacroPanel", UIFactory.Fill, UITheme.Panel);
            var cg = root.AddComponent<CanvasGroup>();
            var title = UIFactory.Label(root.transform, "Title", "宏观经济面板", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.92f), new Vector2(0.6f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(root.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.92f, 0.92f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 20);
            var back = UIFactory.Button(root.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.03f, 0.02f), new Vector2(0.16f, 0.09f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var body = UIFactory.ScrollableLabel(root.transform, "Body",
                new UIFactory.Anchor(new Vector2(0.03f, 0.10f), new Vector2(0.97f, 0.90f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted, 15);

            var panel = root.AddComponent<MacroPanelUI>();
            panel._body = body;
            panel.AssignRefs(root.GetComponent<RectTransform>(), title, close, cg);
            panel.BindPage("n28_宏观经济面板", "宏观经济面板");

            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(panel.BackOne);
            return panel;
        }

        protected override void Awake()
        {
            base.Awake();
            ResolveRefs();
        }

        void OnEnable() { ResolveRefs(); RefreshBody(); }
        public override void Show() { base.Show(); ResolveRefs(); RefreshBody(); }

        void ResolveRefs()
        {
            if (_body == null)
            {
                var b = transform.Find("Body_Viewport/Body_Content");
                if (b != null) _body = b.GetComponent<TextMeshProUGUI>();
            }
        }

        public void RefreshBody()
        {
            ResolveRefs();
            var gs = GameState.Instance;
            var macro = MacroEconomyManager.Instance;
            if (gs == null || macro == null || _body == null) return;

            string phase = gs.economicPhase;
            string label = MacroEconomyManager.PhaseLabel(phase);
            Color pc = MacroEconomyManager.PhaseColor(phase);
            string hex = ColorUtility.ToHtmlStringRGB(pc);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>当前阶段：</b><color=#{hex}>{label}</color>（持续 {gs.phaseDuration + 1} 季）");
            sb.AppendLine();
            sb.AppendLine($"<b>GDP 增速：</b>{gs.gdpGrowthRate:F1}%　<b>航油价：</b>{gs.fuelPricePerTon:F0} 美元/吨");
            sb.AppendLine($"<color=#7C8FA6>{AirlineCEO.Logic.ExternalDataFetcher.QuoteStatusLine(compact: true)}</color>"
                + (string.IsNullOrEmpty(MacroEconomyManager.QuoteConvergeForecast(gs))
                    ? "" : $"　·　本季收敛后 {MacroEconomyManager.QuoteConvergeForecast(gs)}"));
            sb.AppendLine($"<b>汇率：</b>1 美元 = {AirlineCEO.Logic.FxEconomics.RateOf(gs):F2} 元　<b>净美元头寸：</b>{AirlineCEO.Logic.FxEconomics.NetUsdExposureWan(gs):F0} 万美元");
            sb.AppendLine();

            sb.AppendLine("<b>■ 四阶段周期</b>");
            sb.AppendLine("  繁荣（GDP≥3%）→ 衰退（连续两季负增长）→ 复苏（0~3%）→ 繁荣");
            sb.AppendLine();

            sb.AppendLine("<b>■ 对本司的传导</b>");
            sb.AppendLine($"  需求：{DemandLine(phase)}");
            sb.AppendLine($"  油价：{OilLine(phase)}");
            sb.AppendLine($"  汇率：{FxLine(gs)}");
            sb.AppendLine($"  融资：{LoanLine(phase)}");
            sb.AppendLine($"  竞对：{AiLine(phase)}");
            sb.AppendLine($"  事件：{EventLine(phase)}");
            sb.AppendLine();

            sb.AppendLine("<b>■ 油价走势（近若干季）</b>");
            if (gs.fuelPriceHistory != null && gs.fuelPriceHistory.Count > 0)
            {
                var sb2 = new System.Text.StringBuilder();
                int start = Mathf.Max(0, gs.fuelPriceHistory.Count - 10);
                for (int i = start; i < gs.fuelPriceHistory.Count; i++)
                    sb2.Append(gs.fuelPriceHistory[i].ToString("F0")).Append(i == gs.fuelPriceHistory.Count - 1 ? "" : " → ");
                sb.AppendLine("  " + sb2);
            }
            else
            {
                sb.AppendLine("  暂无历史数据。");
            }
            sb.AppendLine();

            sb.AppendLine("<b>■ 下一财年展望</b>");
            sb.AppendLine($"  {Forecast(phase)}");
            sb.AppendLine();
            sb.AppendLine("入口：主页「经济」指示灯 / 财务部。");

            _body.text = sb.ToString();
        }

        static string DemandLine(string p) => p switch
        {
            "boom" => "↑ 商务 +25% / 休闲 +15%",
            "recession" => "↓ −20%～−30%",
            "recovery" => "↑ 休闲 +12% / 商务 +5%",
            _ => "—"
        };
        static string OilLine(string p) => p switch
        {
            "boom" => "↑ 震荡上行",
            "recession" => "↓ 暴跌 −20%～−30%",
            "recovery" => "→ 低位横盘",
            _ => "—"
        };
        static string LoanLine(string p) => p switch
        {
            "boom" => "↓ 便宜（−15%）",
            "recession" => "↑ 昂贵（+25%）",
            "recovery" => "→ 逐步恢复",
            _ => "—"
        };

        /// <summary>
        /// 汇率传导：本司净美元头寸为负（美元负债 &gt; 美元收入）时，人民币贬值即汇兑损失。
        /// 方向与宏观阶段一致 —— 衰退期资本外流、人民币承压，正是最难受的时候。
        /// </summary>
        static string FxLine(GameState gs)
        {
            float exposure = AirlineCEO.Logic.FxEconomics.NetUsdExposureWan(gs);
            string dir = exposure < 0f ? "人民币贬值受损 / 升值受益" : "人民币升值受损 / 贬值受益";
            string phase = gs.economicPhase switch
            {
                "recession" => "贬值压力 ↑",
                "boom" => "升值压力 ↓",
                _ => "双向震荡 →"
            };
            return $"{dir}（头寸 {exposure:F0} 万 · {phase}）";
        }
        static string AiLine(string p) => p switch
        {
            "boom" => "扩张积极 / 破产少",
            "recession" => "保守收缩 / 破产多",
            "recovery" => "观望回暖",
            _ => "—"
        };
        static string EventLine(string p) => p switch
        {
            "boom" => "正面事件偏多",
            "recession" => "负面事件偏多",
            "recovery" => "均衡",
            _ => "—"
        };
        static string Forecast(string p) => p switch
        {
            "boom" => "繁荣可能持续 2～4 季，注意需求过热回调。",
            "recession" => "衰退或持续 1～3 季，关注 GDP 转正信号。",
            "recovery" => "复苏周期通常 2～5 季，是布局的黄金窗口。",
            _ => "趋势不明。"
        };
    }
}
