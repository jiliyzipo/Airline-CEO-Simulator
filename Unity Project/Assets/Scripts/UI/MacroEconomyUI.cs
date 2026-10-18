using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// Phase 13 · 宏观经济展望面板（财务页签）。
    /// </summary>
    public class MacroEconomyUI : MonoBehaviour
    {
        private TextMeshProUGUI _bodyText;
        private TextMeshProUGUI _forecastText;

        public static MacroEconomyUI Build(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "Panel_MacroEconomy", UIFactory.Fill, UITheme.Panel);
            var ui = panel.AddComponent<MacroEconomyUI>();

            var title = UIFactory.Label(panel.transform, "Title", "宏观经济展望", 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.03f, 0.92f), new Vector2(0.7f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            ui._bodyText = UIFactory.ScrollableLabel(panel.transform, "Body",
                new UIFactory.Anchor(new Vector2(0.03f, 0.06f), new Vector2(0.97f, 0.90f), Vector2.zero, Vector2.zero),
                UITheme.TextMuted, 14, TextAlignmentOptions.TopLeft);

            return ui;
        }

        public void Refresh()
        {
            var gs = GameState.Instance;
            if (gs == null) return;

            var macro = MacroEconomyManager.Instance;
            if (macro == null) return;

            string phase = gs.economicPhase;
            string phaseLabel = MacroEconomyManager.PhaseLabel(phase);
            Color phaseColor = MacroEconomyManager.PhaseColor(phase);
            string hex = ColorUtility.ToHtmlStringRGB(phaseColor);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>当前阶段：</b><color=#{hex}>{phaseLabel}</color>（持续 {gs.phaseDuration + 1} 季）");
            sb.AppendLine();
            sb.AppendLine($"<b>GDP 增速：</b>{gs.gdpGrowthRate:F1}%");
            sb.AppendLine();

            // 阶段描述
            sb.AppendLine("<b>阶段影响：</b>");
            sb.AppendLine($"  需求系数：{DescribeDemand(phase)}");
            sb.AppendLine($"  油价趋势：{DescribeOil(phase)}");
            sb.AppendLine($"  融资成本：{DescribeLoan(phase)}");
            sb.AppendLine($"  竞对行为：{DescribeAI(phase)}");
            sb.AppendLine($"  随机事件：{DescribeEvent(phase)}");
            sb.AppendLine();

            // 趋势
            sb.AppendLine("<b>趋势预测（下一财年）：</b>");
            sb.AppendLine($"  {ForecastNext(phase)}");

            _bodyText.text = sb.ToString();
        }

        private static string DescribeDemand(string phase) => phase switch
        {
            "boom" => "↑ 商务+25% / 休闲+15%",
            "recession" => "↓ −20%～−30%",
            "recovery" => "↑ 休闲+12% / 商务+5%",
            _ => "—"
        };

        private static string DescribeOil(string phase) => phase switch
        {
            "boom" => "↑ 震荡上行",
            "recession" => "↓ 暴跌 −20%～−30%",
            "recovery" => "→ 低位横盘",
            _ => "—"
        };

        private static string DescribeLoan(string phase) => phase switch
        {
            "boom" => "↓ 便宜（−15%）",
            "recession" => "↑ 昂贵（+25%）",
            "recovery" => "→ 逐步恢复",
            _ => "—"
        };

        private static string DescribeAI(string phase) => phase switch
        {
            "boom" => "扩张积极 / 破产少",
            "recession" => "保守收缩 / 破产多",
            "recovery" => "观望回暖",
            _ => "—"
        };

        private static string DescribeEvent(string phase) => phase switch
        {
            "boom" => "正面事件偏多",
            "recession" => "负面事件偏多",
            "recovery" => "均衡",
            _ => "—"
        };

        private static string ForecastNext(string phase) => phase switch
        {
            "boom" => "繁荣可能持续 2～4 季，注意需求过热回调。",
            "recession" => "衰退或持续 1～3 季，关注 GDP 转正信号。",
            "recovery" => "复苏周期通常 2～5 季，是布局的黄金窗口。",
            _ => "趋势不明。"
        };
    }
}
