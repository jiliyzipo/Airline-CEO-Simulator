using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using AirlineCEO.UI;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 开航研究会**请示件**的唯一组装处：机场对 → 距离 → 可行性判据 → 各部门意见 → 请示事项。
    ///
    /// 抽出来的理由和 <see cref="RouteCloseLogic"/> 一样，是「口径只许有一个」：
    /// 页面（<see cref="OpenRouteDecisionFlow"/>）和命令台（<see cref="DeptReportLogic"/>）
    /// 是同一个动作的两个入口，若各写一份正文，同一条航线在两个入口就会给出两种说法 ——
    /// 而这种分叉往往只在某次改口径时被漏掉一处，事后极难发现。
    ///
    /// 本类**只组装、不投递**：真正的收件箱写入仍是 <c>InboxManager.QueueOpenRouteStudy</c>。
    /// </summary>
    public static class OpenRouteStudyLogic
    {
        public struct Plan
        {
            /// <summary>false = 这条机场对不该开（不可行/航程不成立），<see cref="reject"/> 是给 CEO 的理由。</summary>
            public bool ok;
            public string reject;
            /// <summary>「PEK — JFK」这类展示串；同时用作收件事的唯一键。</summary>
            public string path;
            public string subject;
            public string body;
            public float distanceKm;
            public RPMCalculator.RPMOutput rpm;
            public string origin;
            public string dest;
            public string via;
        }

        /// <summary>
        /// 组一份请示件。<paramref name="viaCode"/> 为空即直飞。
        ///
        /// 判据顺序刻意是「先否决、后算账」：航线本身不成立（例如同一城市、航程超出物理可能）时
        /// 不该去算日利 —— 那会产出一封看起来很专业的信，却在推销一条不该存在的航线。
        /// </summary>
        public static Plan Build(GameState gs, string originCode, string destCode, string viaCode)
        {
            var plan = new Plan
            {
                origin = originCode,
                dest = destCode,
                via = string.IsNullOrEmpty(viaCode) ? "" : viaCode
            };

            if (gs == null) { plan.reject = "游戏尚未就绪。"; return plan; }
            if (string.IsNullOrEmpty(originCode) || string.IsNullOrEmpty(destCode))
            {
                plan.reject = "开航要先知道出发地与目的地。例如：开通北京—纽约航线。";
                return plan;
            }

            float km = DistanceKm(originCode, destCode, plan.via);
            if (!RouteGeography.IsPlausiblePassengerOd(originCode, destCode, km))
            {
                plan.reject = RouteGeography.RejectReason(originCode, destCode, km);
                return plan;
            }

            AirportCatalog.TryResolve(originCode, out var o);
            AirportCatalog.TryResolve(destCode, out var d);
            AirportCatalog.TryResolve(plan.via, out var v);

            string originLabel = string.IsNullOrEmpty(o.Display) ? originCode : o.Display;
            string destLabel = string.IsNullOrEmpty(d.Display) ? destCode : d.Display;
            string viaLabel = string.IsNullOrEmpty(plan.via)
                ? "直飞"
                : (string.IsNullOrEmpty(v.Display) ? plan.via : v.Display);

            plan.distanceKm = km;
            plan.path = string.IsNullOrEmpty(plan.via)
                ? AirportCatalog.Od(originCode, destCode)
                : AirportCatalog.Od(originCode, destCode, plan.via);
            plan.subject = $"请决策：开通 {plan.path}（各部门研究会）";
            plan.body = BuildBody(gs, originCode, destCode, plan.path,
                originLabel, destLabel, viaLabel, km, out var rpm);
            plan.rpm = rpm;
            plan.ok = true;
            return plan;
        }

        /// <summary>经停时 = 两段航程之和；直线时 = 大圆距离。<b>与地图口径同一出处</b>。</summary>
        public static float DistanceKm(string originCode, string destCode, string viaCode)
        {
            if (string.IsNullOrEmpty(viaCode))
                return AirportGeoCatalog.GreatCircleKm(originCode, destCode);
            return AirportGeoCatalog.GreatCircleKm(originCode, viaCode)
                 + AirportGeoCatalog.GreatCircleKm(viaCode, destCode);
        }

        static string BuildBody(GameState gs, string originCode, string destCode, string path,
            string originLabel, string destLabel, string viaLabel, float dist,
            out RPMCalculator.RPMOutput rpm)
        {
            float cash = gs != null ? gs.cash : 0f;
            float cost = ActionExecutor.OpenRouteStartupCost;

            var demand = RouteGravityModel.Evaluate(gs, originCode, destCode, dist);

            // 与 n35「新航线 RPM 评估台」同一套入参装配，避免两个入口对同一条航线给出两个数。
            var preview = RpmPreview.Build(gs, originCode, destCode,
                aircraftType: null, competitorSupply: 0.4f, distanceKm: dist);
            string ac = preview.aircraftType;
            rpm = preview.output;

            bool canAfford = cash >= cost;
            string affordLine = canAfford
                ? $"现金充足：{cash:F0} → 开通后约 {cash - cost:F0} 万美元"
                : $"现金不足：需要 {cost:F0} 万，当前仅 {cash:F0} 万美元（批准后执行可能失败）";

            string ops = rpm.score >= 70 ? "绿 · 衔接与利用率可接受" : "黄 · 建议再核班次/机型";
            string mkt = rpm.score >= 60 ? "绿 · 需求尚可" : "黄 · 收益偏弱，建议再做 RPM 评估";
            string fin = canAfford ? "绿 · 启动资金可承受" : "红 · 现金不足以支付启动资金";
            string hr = "绿 · 现有机组可覆盖示意班次";
            string saf = dist >= 5000f ? "黄 · 长航线需关注执勤时限" : "绿 · 短中程风险可控";
            string mnt = "绿 · 建议机型可纳入现行维修方案";

            bool intl = RoutePermitLogic.IsInternational(originCode, destCode);
            bool needsSlot = RoutePermitLogic.NeedsSlot(originCode, destCode, intl,
                RoutePermitLogic.FromHub(originCode, destCode));
            var permit = new StringBuilder();
            permit.Append("· 主管机构　").Append(intl ? "民航局（国际/地区）" : "地区管理局（国内）\n");
            permit.Append("· 办理事项　航线经营许可");
            if (intl) permit.Append(" + 航权额度 + 对方许可");
            permit.Append(" + 公示期\n");
            permit.Append(needsSlot
                ? "· 发证之后　协调机场时刻槽（先证后槽：许可到手再办/竞拍，避免费用打水漂）\n"
                : "· 发证之后　非协调机场无时刻限制，报备航班计划即可开航\n");
            permit.Append("· 通过率参考　").Append(
                $"{RoutePermitLogic.PassChance(gs, originCode, destCode, intl, 1) * 100f:F0}%（受安全评级、局方关系与竞对运力影响）\n");

            var core = new StringBuilder();
            core.Append($"<b>议题：批准开通 {path}</b>\n");

            core.Append(InboxLetter.Section("航线与运力"));
            core.Append(InboxLetter.Row("出发地", originLabel));
            core.Append(InboxLetter.Row("目的地", destLabel));
            core.Append(InboxLetter.Row("经停", viaLabel));
            core.Append(InboxLetter.Row("航程", $"{dist:F0} km · 建议机型 {ac}（单班示意）"));

            core.Append(InboxLetter.Section("市场需求与收益（引力模型）"));
            core.Append(demand.FormatBlock()).Append('\n');

            core.Append(InboxLetter.Section("财务与资金"));
            core.Append(InboxLetter.Bullet($"综合评定　{rpm.score} 分（{rpm.rating}）"));
            core.Append(InboxLetter.Bullet($"预估日利润　约 {rpm.dailyProfit:F1} 万美元/日"));
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

            return InboxLetter.Frame(gs, "运营部", "开航研究会", $"请决策：开通 {path}（各部门研究会）",
                core.ToString(), "以上研究会结论，请您审阅后作出决策。");
        }
    }
}
