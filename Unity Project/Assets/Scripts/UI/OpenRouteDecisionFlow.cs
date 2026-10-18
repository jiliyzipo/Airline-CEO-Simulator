using System;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 开通航线：选机场后由运营部召集研究，把各部门意见发到收件箱请 CEO 决策。
    /// </summary>
    public class OpenRouteDecisionFlow : MonoBehaviour
    {
        Transform _canvas;
        RPMCalculator.RPMOutput _previewRpm;
        Action _onStateChanged;

        public static OpenRouteDecisionFlow Ensure(Transform canvasRoot)
        {
            if (canvasRoot == null) return null;
            var existing = canvasRoot.GetComponentInChildren<OpenRouteDecisionFlow>(true);
            if (existing != null)
            {
                if (existing._canvas == null) existing._canvas = canvasRoot;
                return existing;
            }
            var go = new GameObject("OpenRouteDecisionFlow", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            UIFactory.ApplyAnchor(go.GetComponent<RectTransform>(), UIFactory.Fill);
            var img = go.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = false;
            var comp = go.AddComponent<OpenRouteDecisionFlow>();
            comp._canvas = canvasRoot;
            return comp;
        }

        public void Bind(Action onStateChanged, Action<string> openOpsPage = null, Action<int> gotoNav = null)
        {
            _onStateChanged = onStateChanged;
            _canvas = transform.parent;
        }

        public string SubmitStudy(OpenRouteModalUI.Selection sel)
        {
            float km = string.IsNullOrEmpty(sel.viaCode)
                ? MapWidget.GreatCircleKm(sel.originCode, sel.destCode)
                : MapWidget.GreatCircleKm(sel.originCode, sel.viaCode)
                  + MapWidget.GreatCircleKm(sel.viaCode, sel.destCode);
            if (!RouteGeography.IsPlausiblePassengerOd(sel.originCode, sel.destCode, km))
                return RouteGeography.RejectReason(sel.originCode, sel.destCode, km);
            string path = FormatPath(sel);
            string subject = $"请决策：开通 {path}（各部门研究会）";
            string body = BuildStudyMailBody(sel, subject);
            InboxManager.QueueOpenRouteStudy(GameState.Instance, sel.originCode, sel.destCode, sel.viaCode,
                subject, body);
            _onStateChanged?.Invoke();
            return path;
        }

        public void BeginAfterAirportPick(OpenRouteModalUI.Selection sel)
        {
            SubmitStudy(sel);
        }

        public void BeginFromMap(string originCode, string destCode)
        {
            AirportCatalog.TryResolve(originCode, out var o);
            AirportCatalog.TryResolve(destCode, out var d);
            BeginAfterAirportPick(new OpenRouteModalUI.Selection
            {
                originCode = string.IsNullOrEmpty(o.code) ? originCode : o.code,
                destCode = string.IsNullOrEmpty(d.code) ? destCode : d.code,
                viaCode = null,
                originLabel = string.IsNullOrEmpty(o.Display) ? originCode : o.Display,
                destLabel = string.IsNullOrEmpty(d.Display) ? destCode : d.Display,
                viaLabel = "直飞"
            });
        }

        static string FormatPath(OpenRouteModalUI.Selection sel)
        {
            if (string.IsNullOrEmpty(sel.viaCode))
                return AirportCatalog.Od(sel.originCode, sel.destCode);
            return AirportCatalog.Od(sel.originCode, sel.destCode, sel.viaCode);
        }

        string BuildStudyMailBody(OpenRouteModalUI.Selection sel, string subject)
        {
            var gs = GameState.Instance;
            float cash = gs != null ? gs.cash : 0f;
            float cost = ActionExecutor.OpenRouteStartupCost;
            string path = FormatPath(sel);

            float dist = string.IsNullOrEmpty(sel.viaCode)
                ? MapWidget.GreatCircleKm(sel.originCode, sel.destCode)
                : MapWidget.GreatCircleKm(sel.originCode, sel.viaCode)
                  + MapWidget.GreatCircleKm(sel.viaCode, sel.destCode);
            string ac = dist >= 5000f ? "B787-9" : "A320neo";
            var demand = RouteGravityModel.Evaluate(gs, sel.originCode, sel.destCode, dist);
            float demandMul = CityEconomyLogic.OdDemandMultiplier(gs, sel.originCode, sel.destCode);
            _previewRpm = RPMCalculator.Calculate(new RPMCalculator.RPMInput
            {
                distance = dist,
                fuelPricePerTon = gs != null ? gs.fuelPricePerTon : 850f,
                competitorSupply = 0.4f,
                demandMultiplier = demandMul,
                brandEquity = gs != null ? gs.brandAwareness : 55f,
                aircraftType = ac,
                seats = EconomyBalanceParams.DefaultSeatsFor(ac),
                passengerType = "leisure"
            });

            bool canAfford = cash >= cost;
            string affordLine = canAfford
                ? $"现金充足：{cash:F0} → 开通后约 {cash - cost:F0} 万美元"
                : $"现金不足：需要 {cost:F0} 万，当前仅 {cash:F0} 万美元（批准后执行可能失败）";

            string ops = _previewRpm.score >= 70 ? "绿 · 衔接与利用率可接受" : "黄 · 建议再核班次/机型";
            string mkt = _previewRpm.score >= 60 ? "绿 · 需求尚可" : "黄 · 收益偏弱，建议再做 RPM 评估";
            string fin = canAfford ? "绿 · 启动资金可承受" : "红 · 现金不足以支付启动资金";
            string hr = "绿 · 现有机组可覆盖示意班次";
            string saf = dist >= 5000f ? "黄 · 长航线需关注执勤时限" : "绿 · 短中程风险可控";
            string mnt = "绿 · 建议机型可纳入现行维修方案";

            bool intl = RoutePermitLogic.IsInternational(sel.originCode, sel.destCode);
            bool needsSlot = RoutePermitLogic.NeedsSlot(sel.originCode, sel.destCode, intl,
                RoutePermitLogic.FromHub(sel.originCode, sel.destCode));
            var permit = new System.Text.StringBuilder();
            permit.Append("· 主管机构　").Append(intl ? "民航局（国际/地区）" : "地区管理局（国内）\n");
            permit.Append("· 办理事项　航线经营许可");
            if (intl) permit.Append(" + 航权额度 + 对方许可");
            permit.Append(" + 公示期\n");
            permit.Append(needsSlot
                ? "· 发证之后　协调机场时刻槽（先证后槽：许可到手再办/竞拍，避免费用打水漂）\n"
                : "· 发证之后　非协调机场无时刻限制，报备航班计划即可开航\n");
            permit.Append("· 通过率参考　").Append(
                $"{RoutePermitLogic.PassChance(gs, sel.originCode, sel.destCode, intl, 1) * 100f:F0}%（受安全评级、局方关系与竞对运力影响）\n");

            var core = new System.Text.StringBuilder();
            core.Append($"<b>议题：批准开通 {path}</b>\n");

            core.Append(InboxLetter.Section("航线与运力"));
            core.Append(InboxLetter.Row("基地", sel.originLabel));
            core.Append(InboxLetter.Row("目的地", sel.destLabel));
            core.Append(InboxLetter.Row("经停", sel.viaLabel));
            core.Append(InboxLetter.Row("航程", $"{dist:F0} km · 建议机型 {ac}（单班示意）"));

            core.Append(InboxLetter.Section("市场需求与收益（引力模型）"));
            core.Append(demand.FormatBlock()).Append('\n');

            core.Append(InboxLetter.Section("财务与资金"));
            core.Append(InboxLetter.Bullet($"综合评定　{_previewRpm.score} 分（{_previewRpm.rating}）"));
            core.Append(InboxLetter.Bullet($"预估日利润　约 {_previewRpm.dailyProfit:F1} 万美元/日"));
            core.Append(InboxLetter.Bullet($"启动资金　{cost:F0} 万美元"));
            core.Append(InboxLetter.Bullet(affordLine));

            core.Append(InboxLetter.Section("各部门讨论意见"));
            core.Append(InboxLetter.Bullet($"财务部：{fin}"));
            core.Append(InboxLetter.Bullet($"运营部：{ops}"));
            core.Append(InboxLetter.Bullet($"市场部：{mkt}"));
            core.Append(InboxLetter.Bullet($"人力资源部：{hr}"));
            core.Append(InboxLetter.Bullet($"安全监察：{saf}"));
            core.Append(InboxLetter.Bullet($"机务工程部：{mnt}"));

            core.Append(InboxLetter.Section("批准后的准入办理（批准 ≠ 可以飞）"));
            core.Append(permit);

            core.Append(InboxLetter.Section("请示事项"));
            core.Append(InboxLetter.Bullet($"批准：转入局方准入办理（本季不扣款）；取得许可并配到时刻后开航时扣启动资金 {cost:F0} 万"));
            core.Append(InboxLetter.Bullet("暂缓：保留提案，本季不动用资金，可再补充评估"));
            core.Append(InboxLetter.Bullet("拒绝：终止该航线开通计划"));

            return InboxLetter.Frame(gs, "运营部", "开航研究会", subject, core.ToString(),
                "以上研究会结论，请您审阅后作出决策。");
        }
    }
}
