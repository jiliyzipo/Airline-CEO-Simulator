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
                    // 已取消首航引导：旧档/旧话术落到开通航线
                    result = ExecuteOpenRoute(gs, advice ?? new AdvisorResponse
                    {
                        suggestedAction = "open_route",
                        message = "开通航线",
                        data = new AdvisorDataPayload { origin = "PEK", destination = "SHA" }
                    });
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
            => ExecuteOpenRoute(gs, advice, false);

        /// <summary>
        /// 准入许可已发证后的开通执行：航权已在准入阶段核准，这里不再掷骰，
        /// 只校验现金/运力并落航线（对应现实里「拿到许可证 → 开航」）。
        /// </summary>
        public ActionResult ExecuteOpenRouteFromPermit(AdvisorResponse advice)
        {
            var gs = GameState.Instance;
            if (gs == null) return Notify(Fail("游戏状态未就绪。"));
            return Notify(ExecuteOpenRoute(gs, advice, true));
        }

        private static ActionResult ExecuteOpenRoute(GameState gs, AdvisorResponse advice, bool rightsPreApproved)
        {
            ResolveRouteEndpoints(advice, out string origin, out string dest);

            if (gs.HasRoute(origin, dest))
            {
                return Ok(
                    $"航线 {origin}-{dest} 已经开通，无需重复执行。\n" +
                    "说明：航权审批只在「首次开通」时掷骰；已开通后再点不会出现「航权被拒」。\n" +
                    "若要复测航权：AirlineCEO → 清档，或 Debug → 移除北京-伦敦航线。");
            }

            if (gs.cash < OpenRouteStartupCost)
                return Fail($"现金不足，无法执行（开通需 {OpenRouteStartupCost:F0} 万美元，当前 {gs.cash:F0} 万）。");

            // Phase 10：需有适航飞机多于已开航线数
            if (FleetManager.Instance == null)
            {
                var go = new GameObject("FleetManager");
                go.AddComponent<FleetManager>();
            }
            FleetManager.Instance.EnsureStarterFleet();
            if (!FleetManager.Instance.HasSpareAircraftForNewRoute(gs))
            {
                return Fail(
                    "机队中无可用飞机，请先购买或维护。\n" +
                    $"（在役 {FleetManager.Instance.CountAirworthy(gs)} 架，已开航线 {gs.activeRoutes.Count} 条）");
            }

            // Phase 7：长航线先过航权审批；Phase 18：准入许可已发证时跳过（航权已在准入阶段核准）
            RouteRightResult rights;
            if (rightsPreApproved)
            {
                rights = new RouteRightResult
                {
                    approved = true,
                    skipped = false,
                    passRate = 1f,
                    competitorShare = RouteRightManager.EstimateCompetitorShare(origin, dest),
                    message = "航线经营许可已核准该航权额度。"
                };
            }
            else
            {
                var rightsMgr = RouteRightManager.Instance;
                if (rightsMgr == null)
                {
                    var go = new GameObject("RouteRightManager");
                    rightsMgr = go.AddComponent<RouteRightManager>();
                }

                float share = RouteRightManager.EstimateCompetitorShare(origin, dest);
                rights = rightsMgr.TryApprove(origin, dest, share);
            }
            if (!rights.approved)
            {
                if (rights.deferred)
                {
                    gs.AddDecisionLog($"航权暂缓核准：{origin}-{dest}（非基地航线，竞争激烈）");
                    gs.NotifyChanged();
                    return Fail(
                        $"{RouteRightManager.DeferredMessage}\n" +
                        $"（非基地航线，竞对运力约 {rights.competitorShare * 100f:F0}%，暂缓核准）\n" +
                        "从主基地开通该方向航线可优先获得航权，或稍后重新申请。现金未扣除。");
                }

                gs.AddDecisionLog($"航权审批失败：{origin}-{dest}（通过率约 {rights.passRate * 100f:F0}%）");
                gs.NotifyChanged();
                return Fail(
                    $"{RouteRightManager.DeniedMessage}\n" +
                    $"（本航线通过率约 {rights.passRate * 100f:F0}%，竞对运力约 {rights.competitorShare * 100f:F0}%）\n" +
                    "可再次点「执行建议」重新申请航权。现金未扣除。");
            }

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
            // 统一成 IATA 代码写入航线
            if (AirportCatalog.TryResolve(origin, out var ao)) origin = ao.code;
            if (AirportCatalog.TryResolve(dest, out var ad)) dest = ad.code;
            string via = advice?.data?.stopover;
            if (!string.IsNullOrWhiteSpace(via) && AirportCatalog.TryResolve(via, out var av))
                via = av.code;
            else
                via = null;

            float distance = EstimateDistanceKm(origin, dest);
            if (!string.IsNullOrEmpty(via))
                distance = EstimateDistanceKm(origin, via) + EstimateDistanceKm(via, dest);
            string acType = distance >= 5000f ? "B787-9" : "A320neo";
            var route = new RouteData
            {
                routeId = $"{origin}-{dest}-{gs.activeRoutes.Count + 1}",
                origin = origin,
                destination = dest,
                stopover = via,
                dailyProfit = dailyProfit,
                openedDate = gs.DateLabel,
                score = score,
                rating = rating,
                distanceKm = distance,
                aircraftType = acType,
                // ★新开航线取「下一个空块」的块首号，并用**航司两字码**（旧版写死的 "NV" 与
                // 接管来的航线前缀不一致，同一家公司两套号）；块的粒度见 FlightScheduleFormat.BlockStride。
                flightNumber = $"{CompanyCode(gs)}{NextFreeBase(gs)}",
                season = "夏秋",
                groundStaffAssigned = OpsEconomics.RecommendedGroundStaff(acType),
                baseDailyProfit = dailyProfit,
                schedulePending = true
            };
            if (!OpsScheduleLogic.ApplyDraft(route, gs.fleet, gs.activeRoutes, 0))
            {
                gs.cash += OpenRouteStartupCost;
                return Fail("运营部无法指派执飞机（机队无满足航程的空闲适航飞机）。现金已退回。");
            }
            OpsEconomics.ApplyToRoute(route, dailyProfit, gs.onTimePercent);
            gs.activeRoutes.Add(route);
            gs.firstFlightCompleted = true;
            string pathLabel = string.IsNullOrEmpty(via) ? $"{origin}-{dest}" : $"{origin}-{via}-{dest}";
            string rightsNote = rights.skipped ? "" : $" 航权已批（通过率约 {rights.passRate * 100f:F0}%）。";
            gs.AddDecisionLog($"开通航线 {pathLabel}，启动资金 -{OpenRouteStartupCost:F0}。运营部已提交航班计划待批。{rights.message}");
            InboxManager.QueueOpsSchedule(gs, route);
            gs.NotifyChanged();

            return Ok(
                $"已开通 {pathLabel}：扣除启动资金 {OpenRouteStartupCost:F0} 万美元。" +
                rightsNote +
                "运营部已编制执飞机与航班计划，请在收件箱批准或提出修改。" +
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

        static float EstimateDistanceKm(string origin, string dest)
        {
            return AirlineCEO.UI.MapWidget.GreatCircleKm(origin, dest);
        }

        /// <summary>本公司的两字码（航班号前缀）。缺失时退回现有航线的前缀，再退回 "NV"。</summary>
        static string CompanyCode(GameState gs)
        {
            if (gs != null && !string.IsNullOrEmpty(gs.companyIata)) return gs.companyIata;
            if (gs?.activeRoutes != null)
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    string p = AirlineCEO.Logic.FlightScheduleFormat.PrefixOf(gs.activeRoutes[i]?.flightNumber);
                    if (!string.IsNullOrEmpty(p)) return p;
                }
            return "NV";
        }

        /// <summary>
        /// 下一个**空块**的块首号：取现有各航线块首之上的第一个未占用块。
        /// 用「最大块首 + 块距」而不是「航线条数」，这样删线后再开线不会复用旧号
        /// （复用的号会撞上时刻槽里写的 usedByFlightNo）。
        /// </summary>
        static int NextFreeBase(GameState gs)
        {
            int maxBase = AirlineCEO.Logic.FlightScheduleFormat.FirstBase - AirlineCEO.Logic.FlightScheduleFormat.BlockStride;
            if (gs?.activeRoutes != null)
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    int n = AirlineCEO.Logic.FlightScheduleFormat.NumberOf(gs.activeRoutes[i]?.flightNumber);
                    if (n > maxBase) maxBase = n;
                }
            return maxBase + AirlineCEO.Logic.FlightScheduleFormat.BlockStride;
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
