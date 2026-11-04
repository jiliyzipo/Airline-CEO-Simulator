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
        public static Plan Build(GameState gs, string originCode, string destCode, string viaCode,
            bool rush = false)
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
                originLabel, destLabel, viaLabel, km, out var rpm, rush, plan.via);
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
            out RPMCalculator.RPMOutput rpm, bool rush = false, string viaCode = "")
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
                ? $"· 时刻槽并行　先槽后证：许可受理后即可先占时刻，{PermitTimeline.SlotPreHoldDays} 天内须取得航权\n"
                : "· 非协调机场　无时刻限制，报备航班计划即可开航\n");
            permit.Append("· 通过率参考　").Append(
                $"{RoutePermitLogic.PassChance(gs, originCode, destCode, intl, 1) * 100f:F0}%（受安全评级、局方关系与竞对运力影响）\n");

            var core = new StringBuilder();
            core.Append($"<b>议题：批准开通 {path}</b>\n");
            // ★2026-11-03：加急会商写在议题正下方 —— 这是「为什么这封信当晚就到了」的唯一解释，
            //   同时把边界说清：压的是运营部自己的会商，局方审查一天都不会省。
            if (rush)
                core.Append(InboxLetter.Bullet(
                    $"加急会商：运营部当日召集各部门，纪要 {InboxCadence.ReplyHours} 小时内呈报"
                    + "（局方审查工期不受此影响，受理后可用「加急请示」花点数再压）"));

            core.Append(InboxLetter.Section("航线与运力"));
            core.Append(InboxLetter.Row("出发地", originLabel));
            core.Append(InboxLetter.Row("目的地", destLabel));
            core.Append(InboxLetter.Row("经停", viaLabel));
            core.Append(InboxLetter.Row("航程", $"{dist:F0} km · 建议机型 {ac}（单班示意）"));

            core.Append(InboxLetter.Section("市场需求与收益（引力模型）"));
            core.Append(demand.FormatBlock()).Append('\n');

            core.Append(InboxLetter.Section("财务与资金"));
            core.Append(InboxLetter.Bullet($"综合评定　{rpm.score} 分（{rpm.rating}）"
                + "—— 按预估日利润、利润率与竞争强度折算的 0–100 分，70 分以上视为可开"));
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

            // ★2026-11-03 用户问询：「各部门应该检查…是否要申请航权，是否要购买或申请时刻槽，
            //   购买或申请哪个时刻收益最大化，现金是否支持」。旧版把这些全写在「批准后要办的事」里，
            //   CEO 在签字那一刻看不到「**现在**已经有 / 还缺什么」，也看不到这条航线开航前一共有多少开销。
            //
            // ★同日二次报障：「『开航前置条件』不在邮件底部」。这张清单是 CEO 落笔签字前最后要看的东西，
            //   所以单独攒一份，排在「批准后的准入办理」之后、只在「请示事项」之前 ——
            //   而不是夹在部门意见与办理流程中间。
            var gate = new StringBuilder();
            gate.Append(InboxLetter.Section("开航前置条件（现在就有 / 还缺什么）"));
            bool rightsOk = AirlineCEO.Systems.StrategyManager.HasRouteRight(gs, originCode, destCode);
            gate.Append(InboxLetter.Bullet(intl
                ? (rightsOk ? "航权　已占双边航权额度，可进入许可办理"
                            : "航权　**缺**：须先向对方申请双边航权额度；缺则许可审查会挂起（届时另函请示）")
                : "航权　国内航线不需要双边航权，凭航线经营许可即可"));
            if (intl)
                gate.Append(InboxLetter.Bullet(RoutePermitLogic.HasOverseasQualification(gs)
                    ? "资质　国际运行资质已具备（有可执飞国际/地区的机组）"
                    : "资质　**缺**：须海外引进 ≥1 名运行人员，或把培训中心升到 3 级后考取「国际运行资质」"));

            float slotTotal = 0f;
            if (needsSlot)
            {
                string hubApt = RoutePermitLogic.FromHub(originCode, destCode)
                    ? (string.IsNullOrEmpty(gs.hubIata) ? originCode : gs.hubIata)
                    : originCode;
                var apts = new System.Collections.Generic.List<string>();
                AddStation(apts, hubApt);
                AddStation(apts, viaCode);
                AddStation(apts, destCode);
                for (int i = 0; i < apts.Count; i++)
                {
                    string apt = apts[i];
                    if (!AirportCatalog.IsCoordinated(apt)) continue;
                    var pick = RoutePermitLogic.TryBestSlotClock(gs, apt);
                    if (!pick.ok)
                    {
                        gate.Append(InboxLetter.Bullet($"时刻　{apt} **四个时段池全满**，暂无法配槽——须等退槽或二级市场挂牌"));
                        continue;
                    }
                    slotTotal += pick.price;
                    gate.Append(InboxLetter.Bullet(
                        $"时刻　{apt} 建议 {pick.clock}（需求 {pick.demand:F2}、约 {pick.price:F0} 万）"
                        + $"—— {RoutePermitLogic.WhyPicked(pick.band)}"));
                }
                gate.Append(InboxLetter.Bullet(
                    $"渠道　时刻可**免费**走协调池申请（按序分配），也可付费走二级市场（当场过户）；"
                    + "不缺那几天就别花这笔钱"));
            }
            else
                gate.Append(InboxLetter.Bullet("时刻　两端都是非协调机场，无需时刻槽，报备航班计划即可开航"));

            float need = cost + slotTotal;
            gate.Append(InboxLetter.Bullet(cash >= need
                ? $"全链条现金　启动 {cost:F0} + 时刻 {slotTotal:F0} = **{need:F0} 万**，当前 {cash:F0} 万，**够**"
                : $"全链条现金　启动 {cost:F0} + 时刻 {slotTotal:F0} = **{need:F0} 万**，当前 {cash:F0} 万，"
                  + $"**差 {need - cash:F0} 万** —— 批准后仍会在开航/交割时失败，请先融资，或改走免费协调池"));

            core.Append(InboxLetter.Section("批准后的准入办理（批准 ≠ 可以飞）"));
            core.Append(permit);
            // ★2026-11-04：CEO 要「从零到航班运行的全流程」，不能只到「取得航权和时刻」。
            //   这里是九关全貌图 + 每关由谁办、卡在哪；下方「开航前置条件」再报此刻已有/还缺什么。
            core.Append(RoutePermitLogic.LifecycleChart(intl, needsSlot));
            core.Append(RoutePermitLogic.LifecycleNotes(intl, needsSlot));
            core.Append(gate);
            core.Append(InboxLetter.Section("请示事项"));
            core.Append(InboxLetter.Bullet($"批准：转入局方准入办理（本季不扣款）；取得许可并配到时刻后开航时扣启动资金 {cost:F0} 万"));
            core.Append(InboxLetter.Bullet("暂缓：保留提案，本季不动用资金，可再补充评估"));
            core.Append(InboxLetter.Bullet("拒绝：终止该航线开通计划"));

            return InboxLetter.Frame(gs, "运营部", "开航研究会", $"请决策：开通 {path}（各部门研究会）",
                core.ToString(), "以上研究会结论，请您审阅后作出决策。");
        }

        /// <summary>把停站加入清单（去重，保持本场端 → 经停端 → 对端的顺序）。</summary>
        static void AddStation(System.Collections.Generic.List<string> list, string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], code, System.StringComparison.OrdinalIgnoreCase)) return;
            list.Add(code);
        }
    }
}
