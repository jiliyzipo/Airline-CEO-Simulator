using AirlineCEO.Systems;
using TMPro;
using UnityEngine;

namespace AirlineCEO.UI.Shell
{
    /// <summary>
    /// 底栏上方的状态读数（设计稿 a 栏）：财务（收入/支出/盈利）+ 航线 + 机队，实时刷新。
    /// 由 <see cref="BottomDockView"/> 构建并定位，只占底栏左侧一段 —— 右侧让给 CEO 命令台。
    /// </summary>
    public class StatusBarView : MonoBehaviour
    {
        /// <summary>状态读数区的历史高度（保持兼容；实际行高见 <see cref="BottomDockView.BarHeight"/>）。</summary>
        public const float BarHeight = BottomDockView.BarHeight;

        TextMeshProUGUI _statIncome;
        TextMeshProUGUI _statExpense;
        TextMeshProUGUI _statProfit;
        TextMeshProUGUI _statRoutes;
        TextMeshProUGUI _statFleet;

        /// <summary>独立构建（满宽）：测试或临时面板用；正式壳层走 <see cref="Build(Transform, float, float)"/>。</summary>
        public static StatusBarView Build(Transform parent) => Build(parent, 0f, 1f);

        /// <summary>在父级的一段里构建读数区（x0/x1 为父级归一化坐标）。</summary>
        public static StatusBarView Build(Transform parent, float x0, float x1)
        {
            var go = UIFactory.Panel(parent, "StatusStats",
                new UIFactory.Anchor(new Vector2(x0, 0f), new Vector2(x1, 1f), Vector2.zero, Vector2.zero),
                new Color(0f, 0f, 0f, 0f), rounded: false);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.raycastTarget = false;
            var view = go.AddComponent<StatusBarView>();
            view.BuildLabels();
            return view;
        }

        void BuildLabels()
        {
            // 财务三项（左）：收入绿 / 支出红 / 盈利黄
            _statIncome = StatLabel("Income", 0.005f, 0.205f, UITheme.Success);
            _statExpense = StatLabel("Expense", 0.205f, 0.405f, UITheme.Danger);
            _statProfit = StatLabel("Profit", 0.405f, 0.625f, UITheme.Warning);
            // 航线 / 机队（右段）
            _statRoutes = StatLabel("Routes", 0.625f, 0.785f, UITheme.TextMuted);
            _statFleet = StatLabel("Fleet", 0.785f, 0.985f, UITheme.TextMuted);
        }

        TextMeshProUGUI StatLabel(string name, float x0, float x1, Color color)
        {
            // 12 号 + 单行省略：窗口变窄时宁可截尾，也不要两个数字叠在一起
            var text = UIFactory.Label(transform, name, "—", 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(x0, 0f), new Vector2(x1, 1f), Vector2.zero, Vector2.zero),
                color);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

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
