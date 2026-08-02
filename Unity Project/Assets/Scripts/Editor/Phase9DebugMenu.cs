#if UNITY_EDITOR
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>Phase 9 燃油对冲 / 融资调试。</summary>
    public static class Phase9DebugMenu
    {
        [MenuItem("Debug/Force Cash Crisis", false, 240)]
        public static void ForceCashCrisis()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("提示", "请先进入 Play 模式。", "确定");
                return;
            }

            var gs = GameState.Instance;
            if (gs == null)
            {
                EditorUtility.DisplayDialog("提示", "GameState 未就绪。", "确定");
                return;
            }

            // 用临时烧钱加成制造跑道 < 3，不永久改坏 monthlyBurnRate（否则信用/跑道长期失真）
            float baseBurn = Mathf.Max(80f, gs.monthlyBurnRate);
            if (gs.monthlyRevenue > baseBurn)
                baseBurn = gs.monthlyRevenue + 80f;
            gs.financeCrisisBurnBonus = baseBurn; // EffectiveMonthlyBurn 会加上此加成
            gs.cash = Mathf.Min(200f, baseBurn * 2.2f);
            gs.NotifyChanged();

            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            Debug.Log($"[Phase9] 强制现金危机：现金 {gs.cash:F0} 万，跑道 {runway:F1} 月。请打开「财务」面板。");
            EditorUtility.DisplayDialog(
                "现金危机",
                $"现金已设为 {gs.cash:F0} 万\n跑道约 {runway:F1} 个月\n（临时危机烧钱，融资后或跑道恢复会自动清除）\n请打开左侧「财务」查看融资选项。",
                "确定");
        }

        [MenuItem("Debug/Force Fuel Spike +20%", false, 241)]
        [MenuItem("Debug/Force Oil Price Spike", false, 242)]
        public static void ForceFuelSpike()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("提示", "请先 Play 且 GameState 就绪。", "确定");
                return;
            }

            var gs = GameState.Instance;
            gs.fuelPricePerTon *= 1.2f;
            HedgeManager.Instance?.PushPriceHistory(gs.fuelPricePerTon);
            gs.NotifyChanged();
            Debug.Log($"[Phase9] 油价拉升至 ${gs.fuelPricePerTon:F0}/吨（测期权/固定价）");
            EditorUtility.DisplayDialog("油价冲高", $"当前油价 ${gs.fuelPricePerTon:F0}/吨\n再推进季度可看对冲结算。", "确定");
        }

        [MenuItem("Debug/Force Oil Price Crash -20%", false, 243)]
        public static void ForceFuelCrash()
        {
            if (!Application.isPlaying || GameState.Instance == null)
            {
                EditorUtility.DisplayDialog("提示", "请先 Play 且 GameState 就绪。", "确定");
                return;
            }

            var gs = GameState.Instance;
            gs.fuelPricePerTon = Mathf.Max(500f, gs.fuelPricePerTon * 0.8f);
            HedgeManager.Instance?.PushPriceHistory(gs.fuelPricePerTon);
            gs.NotifyChanged();
            Debug.Log($"[Phase9] 油价下跌至 ${gs.fuelPricePerTon:F0}/吨（测区间保底/期权不行权）");
        }
    }
}
#endif
