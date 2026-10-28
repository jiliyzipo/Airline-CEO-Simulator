using AirlineCEO.Systems;
using TMPro;
using UnityEngine;

namespace AirlineCEO.UI.Shell
{
    /// <summary>
    /// 底栏上方的状态读数（设计稿 a 栏）：财务（季度净贡献 / 支出 / 盈亏）+ 航线 + 机队，实时刷新。
    /// 由 <see cref="BottomDockView"/> 构建并定位，只占底栏左侧一段 —— 右侧让给 CEO 命令台。
    ///
    /// 财务三项的计算口径只走 <see cref="QuarterSnapshot"/>，不要在本类里另拼成本项：
    /// 2026-10-27 之前这里只算「固定开销 + 机队维护」两项，与跑道口径不同源，
    /// 同一屏上两个数会推出两条不同的烧钱速度（T03.8 逐屏查出）。
    /// </summary>
    public class StatusBarView : MonoBehaviour
    {
        /// <summary>状态读数区的历史高度（保持兼容；实际行高见 <see cref="BottomDockView.BarHeight"/>）。</summary>
        public const float BarHeight = BottomDockView.BarHeight;

        /// <summary>
        /// 三项标签前缀。抽成常量是为了让回归能断言「标签有没有说谎」，
        /// 不必起 UI —— 2026-10-27 的缺陷正是标签说谎：数字叫「收入」，实际是已扣成本后的净贡献。
        /// 三个前缀都带「季度」：数值是 ×3 的季度预测。
        /// </summary>
        public const string IncomePrefix = "季度净贡献:";
        public const string ExpensePrefix = "季度支出:";
        public const string ProfitPrefix = "季度盈亏:";

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
            // 财务三项（左）：净贡献绿 / 支出红 / 盈亏黄
            // 标签带「季度」：三个数都是 ×3 的季度预测，不是现金也不是本月实际。
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

        /// <summary>
        /// 底栏财务三项的**唯一**计算入口（万美元，季度口径 = 月度 × 3）。
        ///
        /// 口径（2026-10-27 修，T03.8 逐屏发现的两个缺陷）：
        /// <list type="number">
        /// <item><b>收入项 = 净贡献，不是毛营收</b>。<see cref="GameState.monthlyRevenue"/> 累计的是
        ///   各航线 <c>dailyProfit</c>（已扣完航线成本）乘经营天数，旧标签写「收入」会让玩家读成
        ///   「毛利 −131%」。</item>
        /// <item><b>支出项 = 全成本</b>。旧实现只算「固定开销 + 机队维护」两项，漏掉租赁 / CEO /
        ///   运行岗 / 存量人力 / 保费 / 还本付息六项，于是底栏「盈利」与跑道「
        ///   <see cref="GameState.EffectiveMonthlyBurn"/>」推出两条不同的烧钱速度。</item>
        /// </list>
        /// 现在三项与跑道同源：亏损时 <c>profitWan == -EffectiveMonthlyBurn * 3</c>（逐位相等，
        /// 由 <c>StatusBarReconcileRegression</c> 钉住）。
        /// 计算放在静态方法里是为了让 EditMode 能断言数字，不必起 UI。
        /// </summary>
        public static void QuarterSnapshot(GameState gs, out float incomeWan, out float expenseWan, out float profitWan)
        {
            if (gs == null)
            {
                incomeWan = 0f;
                expenseWan = 0f;
                profitWan = 0f;
                return;
            }

            incomeWan = gs.MonthlyOperatingIncomeWan * 3f;   // 季度净贡献（航线 + 过渡）
            expenseWan = gs.MonthlyOperatingCostWan * 3f;    // 季度运营总成本（9 项，与跑道同源）
            profitWan = incomeWan - expenseWan;
        }

        /// <summary>刷新状态数字（单位：万美元 → 展示为元级用千分位）。</summary>
        public void RefreshStats()
        {
            var gs = GameState.Instance;
            if (gs == null)
            {
                if (_statIncome != null) _statIncome.text = "净贡献 —";
                if (_statExpense != null) _statExpense.text = "支出 —";
                if (_statProfit != null) _statProfit.text = "盈亏 —";
                if (_statRoutes != null) _statRoutes.text = "航线 —";
                if (_statFleet != null) _statFleet.text = "机队 —";
                return;
            }

            QuarterSnapshot(gs, out float incomeWan, out float expenseWan, out float profitWan);
            // 美元展示：1 万 = 10,000（按设计图「121,434,590」量级 → 直接万美元×1万取整）
            string income = (incomeWan * 10000f).ToString("N0");
            string expense = (expenseWan * 10000f).ToString("N0");
            string profit = (profitWan >= 0 ? "" : "-") + Mathf.Abs(profitWan * 10000f).ToString("N0");

            // 三项都带「季度」二字：三个数都是 ×3 的季度预测，历史上真的被当成现金读过
            //（任务书 §22「325.5 万」那条，就是底栏季度支出被误认成现金）。
            if (_statIncome != null) _statIncome.text = IncomePrefix + income;
            if (_statExpense != null) _statExpense.text = ExpensePrefix + expense;
            if (_statProfit != null) _statProfit.text = ProfitPrefix + profit;

            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int fleet = FleetManager.InServiceCount(gs);   // 在册数：已退役 / 已退租不占「机队 N 架」的口径（机队页「总数」同源）
            if (_statRoutes != null) _statRoutes.text = $"航线:{routes}条";
            if (_statFleet != null) _statFleet.text = $"机队:{fleet}架";
        }
    }
}
