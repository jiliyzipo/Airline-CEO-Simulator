using System;
using AirlineCEO.AI;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// Phase 1：把顾问 suggestedAction 真正落到游戏状态（需玩家点「执行建议」确认）。
    /// </summary>
    public class ActionExecutor : MonoBehaviour
    {
        public static ActionExecutor Instance { get; private set; }

        public const float OpenRouteStartupCost = 300f; // 万美元

        public event Action<ActionResult> OnExecuted;

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

        public ActionResult Execute(AdvisorResponse advice)
        {
            if (advice == null)
                return Notify(Fail("没有可执行的建议。"));

            var action = SuggestedActionParser.Parse(advice.suggestedAction);
            return Execute(action, advice);
        }

        public ActionResult Execute(SuggestedAction action, AdvisorResponse advice)
        {
            var gs = GameState.Instance;
            if (gs == null)
                return Notify(Fail("游戏状态未就绪。"));

            ActionResult result;
            switch (action)
            {
                case SuggestedAction.OpenRoute:
                    result = ExecuteOpenRoute(gs, advice);
                    break;
                case SuggestedAction.Delay:
                case SuggestedAction.Reject:
                    result = ExecuteDefer(gs, action);
                    break;
                case SuggestedAction.FirstFlight:
                    result = Ok("首航流程请使用中央「确认首航」按钮，此处无需重复执行。");
                    break;
                case SuggestedAction.CheckCash:
                    float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
                    result = Ok(
                        $"当前现金 {gs.cash:F0} 万美元。" +
                        $"{CashRunnelCalculator.FormatPlayerLabel(runway)}。" +
                        $"已开通航线 {gs.activeRoutes.Count} 条，月收入约 {gs.monthlyRevenue:F0} 万。");
                    break;
                case SuggestedAction.None:
                default:
                    result = Fail("这条建议没有可执行动作（none）。");
                    break;
            }

            return Notify(result);
        }

        private ActionResult Notify(ActionResult result)
        {
            OnExecuted?.Invoke(result);
            return result;
        }

        private static ActionResult ExecuteOpenRoute(GameState gs, AdvisorResponse advice)
        {
            ResolveRouteEndpoints(advice, out string origin, out string dest);

            if (gs.HasRoute(origin, dest))
                return Fail($"航线 {origin}-{dest} 已经开通，无需重复执行。");

            if (gs.cash < OpenRouteStartupCost)
                return Fail($"现金不足，无法执行（开通需 {OpenRouteStartupCost:F0} 万美元，当前 {gs.cash:F0} 万）。");

            float dailyProfit = advice?.data != null && advice.data.profit != 0f
                ? advice.data.profit
                : GameDataSnapshot.PekLhrRpm.dailyProfit;

            // 若快照尚未计算，补一次
            if (Mathf.Approximately(dailyProfit, 0f))
            {
                GameDataSnapshot.Build();
                dailyProfit = GameDataSnapshot.PekLhrRpm.dailyProfit;
            }

            int score = GameDataSnapshot.PekLhrRpm.score;
            string rating = GameDataSnapshot.PekLhrRpm.rating;

            gs.cash -= OpenRouteStartupCost;
            var route = new RouteData
            {
                routeId = $"{origin}-{dest}-{gs.activeRoutes.Count + 1}",
                origin = origin,
                destination = dest,
                dailyProfit = dailyProfit,
                openedDate = gs.DateLabel,
                score = score,
                rating = rating
            };
            gs.activeRoutes.Add(route);
            gs.AddDecisionLog($"开通航线 {origin}-{dest}，启动资金 -{OpenRouteStartupCost:F0}，日利润 {dailyProfit:F1}");
            gs.NotifyChanged();

            return Ok(
                $"已开通 {origin}-{dest}：扣除启动资金 {OpenRouteStartupCost:F0} 万美元。" +
                $"日利润约 {dailyProfit:F1} 万，月收入约 {gs.monthlyRevenue:F0} 万。" +
                $"当前现金 {gs.cash:F0} 万。");
        }

        private static ActionResult ExecuteDefer(GameState gs, SuggestedAction action)
        {
            string label = action == SuggestedAction.Delay ? "暂缓" : "否决";
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + 2f, 0f, 100f);
            gs.AddDecisionLog($"{label}顾问建议，董事会满意度 +2 → {gs.boardSatisfaction:F0}");
            gs.NotifyChanged();
            return Ok($"已记录「{label}」。董事会觉得你比较稳健，满意度 +2（现 {gs.boardSatisfaction:F0}）。");
        }

        private static void ResolveRouteEndpoints(AdvisorResponse advice, out string origin, out string dest)
        {
            origin = advice?.data?.origin;
            dest = advice?.data?.destination;

            if (string.IsNullOrWhiteSpace(origin)) origin = "北京";
            if (string.IsNullOrWhiteSpace(dest)) dest = "伦敦";

            // 从 message 里粗略识别（可选增强）
            string msg = advice?.message ?? "";
            if (msg.Contains("深圳") && msg.Contains("洛杉"))
            {
                origin = "深圳";
                dest = "洛杉矶";
            }
            else if (msg.Contains("北京") && msg.Contains("伦敦"))
            {
                origin = "北京";
                dest = "伦敦";
            }

            origin = origin.Trim();
            dest = dest.Trim();
        }

        private static ActionResult Ok(string message) =>
            new ActionResult { success = true, message = message };

        private static ActionResult Fail(string message) =>
            new ActionResult { success = false, message = message };
    }

    [Serializable]
    public class ActionResult
    {
        public bool success;
        public string message;
    }
}
