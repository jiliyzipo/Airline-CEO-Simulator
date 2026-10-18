using System.Text;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// Phase 13：宏观经济引擎 —— 三阶段周期（繁荣/衰退/复苏）+ 五大传导。
    /// 每财年 Q1 更新 GDP 并判定阶段切换。
    /// </summary>
    public class MacroEconomyManager : MonoBehaviour
    {
        public static MacroEconomyManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ──────── 13.1 周期定义 ────────

        /// <summary>每财年 Q1 由 TimeManager 调用，更新 GDP 并判定周期。</summary>
        public string OnNewFiscalYear(GameState gs, int newYear)
        {
            float prev = gs.gdpGrowthRate;

            // GDP 随机游走 + 均值回归
            float fluctuation = 0f;
            float meanTarget = 3f; // 长期均值
            switch (gs.economicPhase)
            {
                case "boom":
                    fluctuation = Random.Range(-0.5f, 0.6f);
                    meanTarget = 4f;
                    break;
                case "recession":
                    fluctuation = Random.Range(-0.8f, 0.4f);
                    meanTarget = -1.5f;
                    break;
                default: // recovery
                    fluctuation = Random.Range(-0.4f, 0.45f);
                    meanTarget = 2f;
                    break;
            }

            // 均值回归：若偏离长期均值，向均值拉回 20%
            float reversion = (meanTarget - prev) * 0.2f;
            gs.gdpGrowthRate = Mathf.Clamp(prev + fluctuation + reversion, -10f, 10f);

            // 阶段判定
            string prevPhase = gs.economicPhase;
            gs.phaseDuration++;

            if (gs.gdpGrowthRate >= 3f)
            {
                if (prevPhase != "boom") gs.phaseDuration = 0;
                gs.economicPhase = "boom";
            }
            else if (gs.gdpGrowthRate < 0f)
            {
                // 需连续两季度负增长才判定衰退
                if (prevPhase == "recession" || prev < 0f)
                {
                    if (prevPhase != "recession") gs.phaseDuration = 0;
                    gs.economicPhase = "recession";
                }
                else
                {
                    // 首次负增长不立即变衰退，保持原阶段但记录
                    gs.economicPhase = prevPhase;
                }
            }
            else // 1%～3% 复苏
            {
                if (prevPhase != "recovery") gs.phaseDuration = 0;
                gs.economicPhase = "recovery";
            }

            string changed = prevPhase != gs.economicPhase
                ? $" → {PhaseLabel(gs.economicPhase)}"
                : "";
            string msg = $"第 {newYear} 年宏观经济展望：GDP 增速 {gs.gdpGrowthRate:F1}% · {PhaseLabel(gs.economicPhase)}{changed}（持续 {gs.phaseDuration + 1} 季）";

            gs.AddDecisionLog(msg);
            Debug.Log($"[MacroEconomy] {msg}");
            return msg;
        }

        // ──────── 13.2 需求传导 ────────

        /// <summary>获取航线需求乘数（注入 RPMCalculator.demandMultiplier 前调用）。</summary>
        public float GetDemandMultiplier(string passengerType)
        {
            var gs = GameState.Instance;
            if (gs == null) return 1f;

            switch (gs.economicPhase)
            {
                case "boom":
                    return passengerType == "business" ? 1.25f : 1.15f;
                case "recession":
                    // 总需求 −20%～−30%
                    return 1f - Mathf.Clamp(Mathf.Abs(gs.gdpGrowthRate) * 3f, 0.20f, 0.30f);
                case "recovery":
                    return passengerType == "leisure" ? 1.12f : 1.05f;
                default:
                    return 1f;
            }
        }

        // ──────── 13.3 油价与融资 ────────

        /// <summary>油价倾向：繁荣 +5%，衰退 −20～−30%，复苏持平。</summary>
        public float GetOilTendency()
        {
            var gs = GameState.Instance;
            if (gs == null) return 0f;

            return gs.economicPhase switch
            {
                "boom" => 0.05f,
                "recession" => -Random.Range(0.20f, 0.30f),
                _ => 0f
            };
        }

        /// <summary>
        /// 季度油价数据源（GDB 15.12.1 · Jet A1 基准程序化模拟）。
        /// 口径对齐 tools/balance MacroEconomyEngine：财年 Q1 宏观倾向（打 0.25 折）+ 噪声 ±4%
        /// （等价离线 TickYear 的油价项），Q2–Q4 基线漂移 ±3%（等价离线 ApplyFuelDrift），clamp [400, 1600]。
        /// 集中于此，替代原先 TimeManager 分散的 ApplyMildFuelFluctuation/ApplyMacroOilTendency。
        /// </summary>
        public void ApplyFuelTick(GameState gs)
        {
            if (gs == null) return;

            float factor;
            if (gs.quarter == 1)
            {
                float tendency = GetOilTendency();
                float noise = Random.Range(-0.04f, 0.04f);
                factor = 1f + tendency * 0.25f + noise;
            }
            else
            {
                factor = 1f + Random.Range(-0.03f, 0.03f);
            }

            gs.fuelPricePerTon = Mathf.Clamp(gs.fuelPricePerTon * factor, 400f, 1600f);
        }

        /// <summary>现实行情收敛的单季幅度上限（±8%/季）：既让油价能跟上现实，又不让单次报价掀翻经济。</summary>
        const float QuoteConvergeBand = 0.08f;

        /// <summary>
        /// 现实行情收敛（默认开启，可在 n116 行情源关掉）：把航油价按季向现实报价靠拢，单季最多动 ±8%。
        /// <para>
        /// 与 <see cref="ApplyFuelTick"/> 刻意分开：季度漂移是游戏自身引擎，这里只让现实报价当「参考基准」，
        /// 所以随机事件 / 连锁事件 / 宏观倾向仍然主导。未开启行情源、断网、报价异常时统一返回空串，不产生任何影响。
        /// </para>
        /// </summary>
        public static string ConvergeToRealQuote(GameState gs)
        {
            if (gs == null) return "";
            if (!ExternalDataFetcher.TryGetOilQuote(out float real, out string label)) return "";

            float target = Mathf.Clamp(real, 400f, 1600f);
            float before = gs.fuelPricePerTon;
            float room = before * QuoteConvergeBand;
            float step = Mathf.Clamp(target - before, -room, room);
            if (Mathf.Abs(step) < 1f) return "";   // 已足够接近：不折腾，也不往油价史里塞噪声

            gs.fuelPricePerTon = Mathf.Clamp(before + step, 400f, 1600f);
            return $"现实行情（{label}）折合 {target:F0} 美元/吨，油价按季靠拢 {before:F0}→{gs.fuelPricePerTon:F0}"
                 + $"（单季上限 ±{QuoteConvergeBand * 100f:F0}%）";
        }

        /// <summary>
        /// 本季结算时油价会收敛到哪（仅用于页面预告，不改任何状态）。
        /// 未开启行情 / 尚无报价时返回空串；已足够接近时返回「本季不动」的说法。
        /// </summary>
        public static string QuoteConvergeForecast(GameState gs)
        {
            if (gs == null) return "";
            if (!ExternalDataFetcher.TryGetOilQuote(out float real, out _)) return "";
            float target = Mathf.Clamp(real, 400f, 1600f);
            float before = gs.fuelPricePerTon;
            float room = before * QuoteConvergeBand;
            float step = Mathf.Clamp(target - before, -room, room);
            if (Mathf.Abs(step) < 1f)
                return $"已足够接近（差 {Mathf.Abs(target - before):F0} 美元/吨），本季不动";
            return $"{Mathf.Clamp(before + step, 400f, 1600f):F0} 美元/吨（单季上限 ±{QuoteConvergeBand * 100f:F0}%）";
        }

        /// <summary>融资成本修正系数：繁荣 0.85，衰退 1.25，复苏 1.0。</summary>
        public float GetLoanRateModifier()
        {
            var gs = GameState.Instance;
            if (gs == null) return 1f;

            return gs.economicPhase switch
            {
                "boom" => 0.85f,
                "recession" => 1.25f,
                _ => 1f
            };
        }

        // ──────── 13.4 竞对联动 ────────

        /// <summary>AI 扩张概率修正（用于 ApplyBehavior）。</summary>
        public float GetAiExpansionModifier()
        {
            var gs = GameState.Instance;
            if (gs == null) return 1f;

            return gs.economicPhase switch
            {
                "boom" => 1.20f,
                "recession" => 0.70f,
                _ => 1f
            };
        }

        /// <summary>AI 破产概率修正（应用于垂死期加速）。</summary>
        public float GetAiBankruptcyModifier()
        {
            var gs = GameState.Instance;
            if (gs == null) return 1f;

            return gs.economicPhase switch
            {
                "boom" => 0.50f,
                "recession" => 1.80f,
                _ => 1f
            };
        }

        // ──────── 13.5 事件权重联动 ────────

        /// <summary>正面事件权重修正。</summary>
        public float GetPositiveEventWeightModifier()
        {
            var gs = GameState.Instance;
            if (gs == null) return 1f;

            return gs.economicPhase switch
            {
                "boom" => 1.3f,
                "recession" => 0.5f,
                _ => 1f
            };
        }

        /// <summary>负面事件权重修正。</summary>
        public float GetNegativeEventWeightModifier()
        {
            var gs = GameState.Instance;
            if (gs == null) return 1f;

            return gs.economicPhase switch
            {
                "boom" => 0.7f,
                "recession" => 1.5f,
                _ => 1f
            };
        }

        // ──────── 工具方法 ────────

        /// <summary>白话阶段名。</summary>
        public static string PhaseLabel(string phase)
        {
            return phase switch
            {
                "boom" => "繁荣期",
                "recession" => "衰退期",
                "recovery" => "复苏期",
                _ => "不明"
            };
        }

        /// <summary>阶段对应 HUD 颜色：绿 / 红 / 黄。</summary>
        public static Color PhaseColor(string phase)
        {
            return phase switch
            {
                "boom" => new Color(0f, 0.78f, 0.33f),     // 绿
                "recession" => new Color(0.83f, 0f, 0f),    // 红
                "recovery" => new Color(1f, 0.84f, 0f),     // 黄
                _ => Color.grey
            };
        }
    }
}
