using System;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// Phase 2：财年心跳——按季度推进，结算收入/烧钱，并可触发随机事件。
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance { get; private set; }

        public event Action<QuarterResult> OnQuarterAdvanced;
        public event Action<YearEndResult> OnYearEnded;

        [Header("随机事件")]
        [Range(0f, 1f)] public float oilEventChance = 0.55f;
        public float oilShockMin = -0.12f; // -12%
        public float oilShockMax = 0.18f;  // +18%

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
            if (Instance == this)
                Instance = null;
        }

        /// <summary>推进一个季度并结算。成功时更新 GameState。</summary>
        public QuarterResult AdvanceQuarter()
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                return new QuarterResult
                {
                    success = false,
                    message = "游戏状态未就绪，无法推进季度。"
                };
            }

            int fromYear = gs.year;
            int fromQuarter = gs.quarter;

            // 2.1 / 2.2 季度收入与支出
            float quarterIncome = gs.monthlyRevenue * 3f;
            float quarterExpense = gs.monthlyBurnRate * 3f;
            float net = quarterIncome - quarterExpense;

            // 2.3 更新现金
            gs.cash += net;

            // 随机事件：油价波动（影响后续快照/叙事；MVP 记入日志与结果）
            string eventText = TryOilEvent(gs);

            // 2.4 推进季度
            bool yearEnded = false;
            gs.quarter += 1;
            if (gs.quarter > 4)
            {
                gs.quarter = 1;
                gs.year += 1;
                yearEnded = true;
            }

            string summary =
                $"季度结算：收入 {quarterIncome:F0} 万 − 支出 {quarterExpense:F0} 万 = 净额 {(net >= 0 ? "+" : "")}{net:F0} 万。" +
                $" 时间 {Label(fromYear, fromQuarter)} → {gs.DateLabel}。";

            gs.AddDecisionLog(summary + (string.IsNullOrEmpty(eventText) ? "" : " " + eventText));
            gs.NotifyChanged();

            var result = new QuarterResult
            {
                success = true,
                fromYear = fromYear,
                fromQuarter = fromQuarter,
                toYear = gs.year,
                toQuarter = gs.quarter,
                quarterIncome = quarterIncome,
                quarterExpense = quarterExpense,
                netChange = net,
                cashAfter = gs.cash,
                eventText = eventText,
                yearEnded = yearEnded,
                message = summary + (string.IsNullOrEmpty(eventText) ? "" : "\n" + eventText)
            };

            // 破产检测（现金为负）
            if (gs.cash < 0f)
            {
                float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
                result.isBankruptWarning = true;
                result.message +=
                    $"\n⚠ 破产警告：现金已为负（{gs.cash:F0} 万）！" +
                    $"{CashRunnelCalculator.FormatPlayerLabel(runway)}，{CashRunnelCalculator.FormatWarningPlain(runway)}。";
            }

            OnQuarterAdvanced?.Invoke(result);

            // 2.5 Q4 结束后进入新年 → OnYearEnd
            if (yearEnded)
            {
                var yearResult = OnYearEnd(gs, fromYear);
                result.yearEnd = yearResult;
                result.message += "\n" + yearResult.message;
                OnYearEnded?.Invoke(yearResult);
            }

            // 现金流吃紧：跑道 < 3 月 → 通知 UI/AI 推送
            float months = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            if (months < 3f && gs.cash >= 0f)
            {
                result.needsFinanceWarning = true;
                result.financeWarning =
                    $"财务顾问：账上钱不多了（{CashRunnelCalculator.FormatPlayerLabel(months)}），建议削减成本或暂缓扩张。";
            }

            return result;
        }

        /// <summary>财年末：董事会目标结算（MVP 简化）。</summary>
        public YearEndResult OnYearEnd(GameState gs, int finishedYear)
        {
            // MVP：按是否开通航线、现金是否健康给满意度微调
            float delta = 0f;
            if (gs.activeRoutes.Count > 0) delta += 5f;
            if (gs.cash >= 200f) delta += 3f;
            if (gs.cash < 0f) delta -= 15f;

            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + delta, 0f, 100f);
            string msg =
                $"财年结算（第 {finishedYear} 年结束）：董事会回顾经营。" +
                $"满意度 {(delta >= 0 ? "+" : "")}{delta:F0} → 现 {gs.boardSatisfaction:F0}。" +
                $"（完整目标考核 Phase 后续开放）";

            gs.AddDecisionLog(msg);
            gs.NotifyChanged();

            return new YearEndResult
            {
                finishedYear = finishedYear,
                satisfactionDelta = delta,
                satisfactionAfter = gs.boardSatisfaction,
                message = msg
            };
        }

        private string TryOilEvent(GameState gs)
        {
            if (UnityEngine.Random.value > oilEventChance)
                return "";

            float shock = UnityEngine.Random.Range(oilShockMin, oilShockMax);
            float before = gs.fuelPricePerTon;
            gs.fuelPricePerTon = Mathf.Max(400f, before * (1f + shock));
            string dir = shock >= 0 ? "上涨" : "下跌";
            return $"事件：航油价格{dir} {Mathf.Abs(shock) * 100f:F0}%（{before:F0}→{gs.fuelPricePerTon:F0} 美元/吨）。";
        }

        private static string Label(int y, int q) => $"第 {y} 年 Q{q}";
    }

    [Serializable]
    public class QuarterResult
    {
        public bool success;
        public string message;
        public int fromYear;
        public int fromQuarter;
        public int toYear;
        public int toQuarter;
        public float quarterIncome;
        public float quarterExpense;
        public float netChange;
        public float cashAfter;
        public string eventText;
        public bool yearEnded;
        public bool isBankruptWarning;
        public bool needsFinanceWarning;
        public string financeWarning;
        public YearEndResult yearEnd;
    }

    [Serializable]
    public class YearEndResult
    {
        public int finishedYear;
        public float satisfactionDelta;
        public float satisfactionAfter;
        public string message;
    }
}
