using AirlineCEO.Systems;
using TMPro;
using UnityEngine;

namespace AirlineCEO.UI.Shell
{
    /// <summary>
    /// 底部导航栏上方的独立状态栏：财务（收入/支出/盈利）+ 航线 + 机队，实时刷新。
    /// 由 PrefabNavIntegration 构建并定位（悬在 Shell 底栏正上方一条）。
    /// </summary>
    public class StatusBarView : MonoBehaviour
    {
        /// <summary>状态栏高度（像素）。</summary>
        public const float BarHeight = 30f;

        TextMeshProUGUI _statIncome;
        TextMeshProUGUI _statExpense;
        TextMeshProUGUI _statProfit;
        TextMeshProUGUI _statRoutes;
        TextMeshProUGUI _statFleet;

        /// <summary>构建状态栏（半透明玻璃条 + 五项状态文字）。</summary>
        public static StatusBarView Build(Transform parent)
        {
            var go = UIFactory.Panel(parent, "StatusBar",
                new UIFactory.Anchor(new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(12, 0), new Vector2(-12, BarHeight)),
                UITheme.Glass, rounded: false);
            go.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            UIFactory.AddBorder(go, UITheme.Border, new Vector2(1f, -1f));
            var view = go.AddComponent<StatusBarView>();
            view.BuildLabels();
            return view;
        }

        void BuildLabels()
        {
            // 财务三项（左）：收入绿 / 支出红 / 盈利黄
            _statIncome = StatLabel("Income", 0.005f, 0.20f, UITheme.Success);
            _statExpense = StatLabel("Expense", 0.20f, 0.40f, UITheme.Danger);
            _statProfit = StatLabel("Profit", 0.40f, 0.60f, UITheme.Warning);
            // 航线 / 机队（右段）
            _statRoutes = StatLabel("Routes", 0.62f, 0.76f, UITheme.TextMuted);
            _statFleet = StatLabel("Fleet", 0.76f, 0.90f, UITheme.TextMuted);
        }

        TextMeshProUGUI StatLabel(string name, float x0, float x1, Color color) =>
            UIFactory.Label(transform, name, "—", 13,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(x0, 0f), new Vector2(x1, 1f), Vector2.zero, Vector2.zero),
                color);

        void OnEnable()
        {
            var gs = GameState.Instance;
            if (gs != null)
            {
                gs.OnStateChanged -= OnGameStateChanged;
                gs.OnStateChanged += OnGameStateChanged;
            }
            RefreshStats();
        }

        void OnDisable()
        {
            var gs = GameState.Instance;
            if (gs != null) gs.OnStateChanged -= OnGameStateChanged;
        }

        void OnGameStateChanged() => RefreshStats();

        /// <summary>刷新状态数字（单位：万美元 → 展示为元级用千分位）。</summary>
        public void RefreshStats()
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                if (_statIncome != null) _statIncome.text = "收入 —";
                if (_statExpense != null) _statExpense.text = "支出 —";
                if (_statProfit != null) _statProfit.text = "盈利 —";
                if (_statRoutes != null) _statRoutes.text = "航线 —";
                if (_statFleet != null) _statFleet.text = "机队 —";
                return;
            }

            float incomeWan = gs.monthlyRevenue * 3f;                    // 季度收入（万美元）
            float expenseWan = (gs.monthlyBurnRate + gs.FleetMonthlyMaintenance) * 3f; // 季度支出
            float profitWan = incomeWan - expenseWan;
            // 美元展示：1 万 = 10,000（按设计图「121,434,590」量级 → 直接万美元×1万取整）
            string income = (incomeWan * 10000f).ToString("N0");
            string expense = (expenseWan * 10000f).ToString("N0");
            string profit = (profitWan >= 0 ? "" : "-") + Mathf.Abs(profitWan * 10000f).ToString("N0");

            if (_statIncome != null) _statIncome.text = $"收入:{income}";
            if (_statExpense != null) _statExpense.text = $"支出:{expense}";
            if (_statProfit != null) _statProfit.text = $"盈利:{profit}";

            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int fleet = FleetManager.InServiceCount(gs);   // 在册数：已退役 / 已退租不占「机队 N 架」的口径（机队页「总数」同源）
            if (_statRoutes != null) _statRoutes.text = $"航线:{routes}条";
            if (_statFleet != null) _statFleet.text = $"机队:{fleet}架";
        }
    }
}
