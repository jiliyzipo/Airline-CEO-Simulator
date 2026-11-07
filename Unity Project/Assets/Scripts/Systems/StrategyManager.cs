using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// STG 战略发展部系统（Phase 2 Week 9）：航权/时刻槽/联盟/投资并购/黑天鹅预案。
    /// 全部回写 GameState.stg。
    /// </summary>
    public class StrategyManager : MonoBehaviour
    {
        public static StrategyManager Instance { get; private set; }

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
            if (Instance == this) Instance = null;
        }

        public static StrategyState EnsureState(GameState gs)
        {
            if (gs == null) return null;
            if (gs.stg == null) gs.stg = new StrategyState();
            if (gs.stg.slots == null) gs.stg.slots = new List<SlotHolding>();
            if (gs.stg.rights == null) gs.stg.rights = new List<RouteRight>();
            if (gs.stg.alliance == null) gs.stg.alliance = new AllianceState();
            if (gs.stg.investments == null) gs.stg.investments = new List<InvestmentProject>();
            if (gs.stg.contingencyPlans == null) gs.stg.contingencyPlans = new List<ContingencyPlan>();
            // 时刻二级市场（2026-11-02）：旧存档没有该子状态，读档后须补建，
            // 否则挂牌/意向列表为 null，第一次打开市场页就 NRE。
            if (gs.stg.slotMarket == null) gs.stg.slotMarket = new SlotMarketState();
            if (gs.stg.slotMarket.listings == null) gs.stg.slotMarket.listings = new List<SlotMarketListing>();
            if (gs.stg.slotMarket.mine == null) gs.stg.slotMarket.mine = new List<SlotMarketListing>();
            if (gs.stg.slotMarket.offers == null) gs.stg.slotMarket.offers = new List<SlotBuyerOffer>();
            if (gs.stg.fiveYearPriority == null || gs.stg.fiveYearPriority.Length != 5)
                gs.stg.fiveYearPriority = new[] { 1, 2, 3, 4, 5 };
            // 时刻槽使用率考核（2026-10 新增）：旧存档没有该子状态，读档后须补建，
            // 否则 series/pending 列表为 null，第一次计数就会 NRE。
            if (gs.stg.slotUsage == null) gs.stg.slotUsage = new SlotUsageState();
            if (gs.stg.slotUsage.series == null) gs.stg.slotUsage.series = new List<SlotSeries>();
            if (gs.stg.slotUsage.pendingReleases == null) gs.stg.slotUsage.pendingReleases = new List<SlotPendingAction>();
            if (gs.stg.slotUsage.pendingJnus == null) gs.stg.slotUsage.pendingJnus = new List<SlotPendingAction>();
            // ★2026-10-15：时刻槽口径由「早/中/晚/夜」改为具体时刻。旧档读进来后在此一次性折算
            //（幂等，slotsTimeMigrated 置位后立即返回），免得每个调用点都各自处理两种口径。
            MigrateSlotTimes(gs);
            return gs.stg;
        }

        #region 航权与时刻槽

        public bool AcquireSlot(string airport, int band, out string message)
            => AcquireSlots(GameState.Instance, airport, band, 1, out message);

        /// <summary>批量购入时刻槽（P3：每个离港班次一条）。失败不扣款。时刻取该时段的峰点。</summary>
        public static bool AcquireSlots(GameState gs, string airport, int band, int count, out string message)
        {
            int b = band < 0 ? 0 : band > 3 ? 3 : band;
            return AcquireSlotsAt(gs, airport, ScheduleSlots.DepTimes[b], count, false, out message);
        }

        /// <summary>
        /// 批量取得**指定时刻**的时刻槽（★2026-10-15：时刻槽精确到时刻）。
        ///
        /// <paramref name="free"/> 是**现实里两套取得方式的分野**，不是折扣：
        /// - `false` = **二级市场购入**（现实里航司之间买卖时刻，LHR 单对曾拍出 7500 万美元）→ 计费；
        /// - `true`  = **协调池分配**（WASG 规定的正常渠道，向独立协调人申请）→ **不收费**。
        ///   旧版把这条渠道也照价扣款，等于对排队分到的槽又收了一道钱。
        /// </summary>
        public static bool AcquireSlotsAt(GameState gs, string airport, string clock, int count, bool free, out string message)
            => AcquireSlotsAt(gs, airport, clock, count, free, out message, out _);

        /// <summary>
        /// 同上，并回传**实际落位的时刻**（★2026-11-05）。
        ///
        /// 15 分钟粒度下每一刻只有 0/1/2 条容量，「我要 12:00」很可能已被占满，
        /// 此时按就近原则分配到同波内还有余量的时刻。调用方若要把「这批槽」记成账
        /// （如先槽后证的预占清单），必须用回传的真实时刻，否则到期退槽会按原话去找而找不到。
        /// </summary>
        public static bool AcquireSlotsAt(GameState gs, string airport, string clock, int count, bool free,
            out string message, out System.Collections.Generic.List<string> assigned)
            => AcquireSlotsAt(gs, airport, clock, count, free, out message, out assigned, null, null);

        /// <summary>
        /// 同上，并可指定**时刻类型**与**配对到港时刻**（★2026-11-06：一槽一对）。
        ///
        /// <paramref name="kind"/>：`DEP` = 离港池（默认，空串同此）/ `ARR` = 只到港池。
        /// <paramref name="arrClock"/>：该对的**到港时刻**。传了它，持仓就会把这个钟点一起存下来，
        /// 于是「这条槽对应几点落地」不再是临时推算，而是可显示、可校验的资产属性。
        /// 运营部 AI 在申请离港时刻时必须带上它 —— 现实里一次飞行要两端都获批，
        /// 只拿离港不拿到港，飞机飞得出去却落不下来。
        /// </summary>
        public static bool AcquireSlotsAt(GameState gs, string airport, string clock, int count, bool free,
            out string message, out System.Collections.Generic.List<string> assigned,
            string kind, string arrClock)
        {
            message = "";
            assigned = new System.Collections.Generic.List<string>();
            if (gs == null) { message = "无 GameState"; return false; }
            var stg = EnsureState(gs);
            int n = count < 1 ? 1 : count;
            string slotKind = Logic.RoutePermitLogic.NormalizeKind(kind);
            int clockMin = Logic.ScheduleSlots.ParseClock(clock);
            if (clockMin < 0)
            {
                message = $"时刻无效（{clock}）。时刻槽须精确到具体时刻（如 06:30 / 08:00）。";
                return false;
            }
            clock = Logic.ScheduleSlots.FormatClock(clockMin);
            if (clockMin % Logic.ScheduleSlots.SlotGridStepMinutes != 0)
            {
                message = $"时刻须落在 {Logic.ScheduleSlots.SlotGridStepMinutes} 分钟网格上（"
                          + $"如 {Logic.ScheduleSlots.FormatClock(Logic.ScheduleSlots.NearestSeriesOf(clockMin))}），"
                          + $"不接受 {clock}。";
                return false;
            }
            int band = Logic.ScheduleSlots.BandOfClock(clock);

            int room = RoutePermitLogic.SlotsAvailable(gs, airport, band, slotKind);
            if (room < n)
            {
                message = $"该机场该时段时刻池余量不足（要 {n} 条，{RoutePermitLogic.SlotPoolLine(gs, airport, band)}）。";
                return false;
            }

            // ★2026-11-05：逐刻分配。每个 15 分钟窗口只有 0/1/2 条容量。
            //   买 1 条 = 「就要这一刻」：该刻满了就如实驳回，由界面改用备选时刻
            //     （见 SlotCandidateLogic）——静默挪到别的刻会让排班对不上班次时刻。
            //   买 N 条 = 「这一波铺开拿 N 条」：按就近顺序铺到同波内还有余量的时刻上，
            //     因为一刻最多 2 条，N 条本来就落不到同一刻。
            var picks = new System.Collections.Generic.List<string>(n);
            int need = n;
            var order = RoutePermitLogic.QuarterOrder(airport, clock, band);
            if (n == 1)
            {
                int at = RoutePermitLogic.SlotsAvailableAt(gs, airport, clock, slotKind);
                if (at > 0) picks.Add(clock);
                need = picks.Count < 1 ? 1 : 0;
            }
            else
            {
                for (int i = 0; i < order.Count && need > 0; i++)
                {
                    int at = RoutePermitLogic.SlotsAvailableAt(gs, airport, order[i], slotKind);
                    while (at > 0 && need > 0)
                    {
                        picks.Add(order[i]);
                        at--;
                        need--;
                    }
                }
            }
            if (need > 0 || picks.Count < n)
            {
                // ★2026-11-05：容量改由小时容量派生后，**并非每个刻钟都是机场公布的槽位**
                // （小时容量 3 的时段只公布 3 个刻，容量 1 的只公布 1 个）。故驳回文案要分三种：
                // 该小时无容量 / 该刻不是公布槽位（并指出就近的公布刻）/ 该刻已被占满。
                // 不指路的话，CEO 看到「不设时刻槽」只能盲改时刻 —— 而这一刻本来就有别的可选项。
                int hourRate = Data.AirportCatalog.HourCapacity(airport, clockMin / 60);
                if (RoutePermitLogic.QuarterCapacityAt(airport, clock) > 0)
                {
                    message = $"{airport} {clock} 该刻已被占满（{RoutePermitLogic.SlotPoolLineAt(gs, airport, clock)}）。"
                              + "可改申请同波内其他时刻，或走二级市场。";
                }
                else if (hourRate <= 0)
                {
                    message = $"{airport} {clock} 不设时刻槽（{Data.AirportCatalog.CoordLabel(airport)}，"
                              + $"{clockMin / 60:00} 时这一小时容量为 0）。";
                }
                else
                {
                    message = $"{airport} {clock} 不是公布槽位（本小时容量 {hourRate} 条，"
                              + $"公布在 {RoutePermitLogic.PublishedClocksText(airport, clock, band, 3)}）。"
                              + "可改申请就近的公布时刻，或走二级市场。";
                }
                return false;
            }

            // ★2026-11-05：单价按时段口径会与实扣不符 —— 同一波内 06:30 与 09:00 不同价。
            // 这里逐条按**实际落位的时刻**计价（多买时是同波内几个时刻，价格可能不同），
            // 文案用均价，帐走实额。
            // ★2026-11-06（G2）：成对落账必须**两端都过**，且原子 —— 到港端不足时整单驳回，一条不落。
            //
            // 资料的 M11 场景（PEK 08:00 离港可拿、SZX 11:15 到港已满）正是旧版会错批的那类单子：
            // 旧版只判离港那一刻，`arrClock` 一路只被写进持仓、从未参与可行性判定。
            // 落地时再判一次，是因为「排队期间别人也可能占了那一格」——申请通过与交割之间有时间差。
            if (slotKind != Data.SlotHolding.SlotKindArrival
                && Logic.ScheduleSlots.ParseClock(arrClock ?? "") >= 0)
            {
                string arrClk = Logic.ScheduleSlots.NearestSeries(
                    Logic.ScheduleSlots.FormatClock(Logic.ScheduleSlots.ParseClock(arrClock)));
                int arrBand = Logic.ScheduleSlots.BandOfClock(arrClk);
                int arrRoom = Logic.RoutePermitLogic.SlotsAvailable(gs, airport, arrBand,
                    Data.SlotHolding.SlotKindArrival);
                int arrHere = Logic.RoutePermitLogic.SlotsAvailableAt(gs, airport, arrClk,
                    Data.SlotHolding.SlotKindArrival);
                if (arrRoom < n || arrHere < n)
                {
                    string arrPool = Logic.RoutePermitLogic.SlotPoolLineAt(gs, airport, arrClk,
                        Data.SlotHolding.SlotKindArrival);
                    message = $"到港端缺口：{airport} {arrClk} 的到港时刻不够 {n} 条（{arrPool}）。"
                              + "改起飞时刻让落地落到别的刻，或去二级市场买到港时刻。";
                    return false;
                }
            }

            float cost = 0f;
            if (!free)
                for (int i = 0; i < n; i++) cost += StrategyEconomics.SlotValueWanAt(airport, picks[i]);
            float unit = n > 0 ? cost / n : 0f;
            if (!free && gs.cash < cost)
            {
                message = $"现金不足，{airport} {clock} 时刻槽 {n} 条需 {cost:F0} 万"
                          + $"（{AirportCatalog.TierLabel(AirportCatalog.TierOfCode(airport))}价，单价 {unit:F0} 万）。"
                          + "若走协调池申请则不收费，该提案由运营部按需另递。";
                return false;
            }
            if (!free) gs.cash -= cost;
            int turn = RoutePermitLogic.Turn(gs.year, gs.quarter);
            for (int i = 0; i < n; i++)
            {
                stg.slots.Add(new SlotHolding
                {
                    airport = airport,
                    slotTime = picks[i],
                    // ★2026-11-06：一槽一对 —— 到港时刻随离港一起入账。
                    // 只到港持仓（kind=ARR）没有配对离港，故 arrTime 留空、由 slotTime 表示到港。
                    arrTime = slotKind == SlotHolding.SlotKindArrival
                        ? "" : (Logic.ScheduleSlots.ParseClock(arrClock ?? "") >= 0
                            ? Logic.ScheduleSlots.FormatClock(Logic.ScheduleSlots.ParseClock(arrClock))
                            : ""),
                    kind = slotKind == SlotHolding.SlotKindArrival ? slotKind : "",
                    inUse = false,
                    acquisitionCostWan = unit,
                    expiresQuarter = gs.quarter + 2,
                    // 闲置收回的时点：取得季 + 2 个季度（留一个航季的筹备窗口），见 ReleaseIdleHoldings
                    expiresTurn = turn + 2
                });
            }

            // 就近分配与原话不一致时必须说清楚落到了哪几刻，否则 CEO 会以为「我买的 12:00」被吞了。
            bool shifted = picks.Count > 0 && !string.Equals(picks[0], clock, System.StringComparison.Ordinal);
            string where = picks.Count > 0 ? picks[0] : clock;
            string note = shifted ? $"（{clock} 已满，就近分配到 {where}）" : "";
            message = free
                ? (n == 1
                    ? $"协调池已分配 {airport} {where} 时刻槽（不收费）{note}。"
                    : $"协调池已分配 {airport} {where} 起共 {n} 条时刻槽（不收费）{note}。")
                : (n == 1
                    ? $"已购 {airport} {where} 时刻槽（{cost:F0} 万）{note}。"
                    : $"已购 {airport} {where} 起共 {n} 条时刻槽（共 {cost:F0} 万）{note}。");
            gs.AddDecisionLog("战略：" + message);
            for (int i = 0; i < picks.Count; i++) assigned.Add(picks[i]);
            gs.NotifyChanged();
            return true;
        }

        public bool ApplyRouteRight(string origin, string destination, int freedom, out string message)
            => ApplyRouteRightOn(GameState.Instance, origin, destination, freedom, out message);

        /// <summary>
        /// 航权入账的**纯函数式入口**：状态由调用方给出，不走 <c>GameState.Instance</c>。
        ///
        /// 为什么要拆这一层：交割链路（InboxManager）只认单例，但回归必须能在
        /// 内存 GameState 上验「点数不够时到底扣没扣」——编辑模式下 <c>GameState.Instance</c>
        /// 不保证被 Awake 赋值（实测为 null），走单例的入口在隔离夹具里根本跑不起来，
        /// 于是这条最该被钉住的扣点逻辑反而没有回归守着。
        /// </summary>
        public static bool ApplyRouteRightOn(GameState gs, string origin, string destination,
            int freedom, out string message)
        {
            if (gs == null) { message = "无 GameState"; return false; }
            var stg = EnsureState(gs);
            float govCost = RightCost(freedom);
            if (stg.governmentRelation < govCost)
            {
                message = $"政府关系点数不足（本航权需 {govCost:F0} 点，当前 {stg.governmentRelation:F0} 点；"
                          + $"每季自然回升 {GovernmentRelationPerQuarter:F0} 点）。";
                return false;
            }
            stg.governmentRelation -= govCost;
            stg.rights.Add(new RouteRight
            {
                origin = origin,
                destination = destination,
                freedom = freedom,
                status = "approved",
                governmentRelationCost = govCost
            });
            message = $"已获批 {origin}—{destination} 第 {freedom} 航权（消耗 {govCost:F0} 关系点）。";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 一条航权的政府关系成本：`15 + 2 × 航权自由度`（第五航权 = 25 点）。
        ///
        /// 单独成函数是因为**提案正文、办理闸门、实际扣点**三处都要报同一个数。
        /// 各自写一遍 `15f + freedom * 2f` 的话，改了一处忘另一处，信里写 25 点、扣的却是别的数。
        /// </summary>
        public static float RightCost(int freedom) => 15f + freedom * 2f;

        /// <summary>政府关系点数上限。</summary>
        public const float GovernmentRelationCap = 100f;

        /// <summary>
        /// 政府关系每季自然回升（★2026-11-01）。
        ///
        /// 修的是一个**只减不增**的漏斗：全仓原先只有扣点（航权 25 点、异议公关 10 点），
        /// 初值 50、任何地方都不回补 —— 一局最多办 2 条航权，办完就再也办不了，
        /// 而国际线准入又硬性依赖航权额度（RoutePermitLogic.MissingPrerequisite 的缺口 1）。
        /// 现实里政府关系是靠持续合规、航线表现与政治资源累积起来的，不是一次性配额。
        ///
        /// 回升取 2 点/季：一条第五航权（25 点）≈ 12.5 个季度的累积，长线经营才攒得出下一条，
        /// 但不会让「透支点数」变成无痛操作。上限 100 与页面上「政府关系 / 100」的读数一致。
        /// </summary>
        public const float GovernmentRelationPerQuarter = 2f;

        /// <summary>季末：政府关系自然回升（带上限，并兜住旧档可能出现的负值）。</summary>
        public static void TickQuarter(GameState gs)
        {
            var stg = EnsureState(gs);
            if (stg == null) return;
            if (stg.governmentRelation < 0f) stg.governmentRelation = 0f;
            stg.governmentRelation = Mathf.Min(GovernmentRelationCap,
                stg.governmentRelation + GovernmentRelationPerQuarter);
        }

        public static string SlotBandName(int band) => band switch { 0 => "早", 1 => "中", 2 => "晚", _ => "夜" };

        /// <summary>
        /// 旧档迁移（★2026-10-15）：把「早/中/晚/夜」口径的持仓与系列折成具体时刻。
        /// 幂等 —— `slotsTimeMigrated` 置位后不再重复；已是 `HH:mm` 的值原样保留。
        /// 迁移落点取该时段的**峰点**（08:00/12:00/18:00/22:00），与 `ScheduleSlots.DepTimes` 同源，
        /// 免得老存档的槽在时刻上与原排班口径分叉。
        /// </summary>
        public static void MigrateSlotTimes(GameState gs)
        {
            var stg = gs != null ? gs.stg : null;
            if (stg?.slotUsage == null || stg.slotUsage.slotsTimeMigrated) return;
            if (stg.slots != null)
            {
                for (int i = 0; i < stg.slots.Count; i++)
                {
                    var h = stg.slots[i];
                    if (h == null) continue;
                    h.slotTime = ScheduleSlots.BandNameToClock(h.slotTime);
                    // 旧档没有 expiresTurn（默认 −1）：补成「现在起 + 2 季」，别让老槽一读档就被收回
                    if (h.expiresTurn < 0) h.expiresTurn = RoutePermitLogic.Turn(gs.year, gs.quarter) + 2;
                }
            }
            if (stg.slotUsage.series != null)
                for (int i = 0; i < stg.slotUsage.series.Count; i++)
                {
                    var s = stg.slotUsage.series[i];
                    if (s != null) s.time = ScheduleSlots.BandNameToClock(s.time);
                }
            if (stg.slotUsage.pendingReleases != null)
                for (int i = 0; i < stg.slotUsage.pendingReleases.Count; i++)
                {
                    var a = stg.slotUsage.pendingReleases[i];
                    if (a != null) a.time = ScheduleSlots.BandNameToClock(a.time);
                }
            if (stg.slotUsage.pendingJnus != null)
                for (int i = 0; i < stg.slotUsage.pendingJnus.Count; i++)
                {
                    var a = stg.slotUsage.pendingJnus[i];
                    if (a != null) a.time = ScheduleSlots.BandNameToClock(a.time);
                }
            stg.slotUsage.slotsTimeMigrated = true;
        }

        public static bool HasRouteRight(GameState gs, string origin, string destination)
        {
            var stg = EnsureState(gs);
            if (stg?.rights == null) return false;
            for (int i = 0; i < stg.rights.Count; i++)
            {
                var r = stg.rights[i];
                if (r == null) continue;
                if (string.Equals(r.origin, origin, System.StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.destination, destination, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static int ParseSlotBand(string dest)
        {
            if (int.TryParse(dest, out int band)) return Mathf.Clamp(band, 0, 3);
            return 1;
        }

        #endregion

        #region 联盟

        public const float CodeshareFeeWan = 25f;
        public const float ObserverJoinFeeWan = 50f;

        public static void RefreshAllianceLift(GameState gs)
        {
            var stg = EnsureState(gs);
            if (stg?.alliance == null) return;
            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            stg.alliance.revenueLiftWan = StrategyEconomics.AllianceRevenueLiftWan(
                stg.alliance.tier, routes, stg.alliance.codeSharePartners);
        }

        /// <summary>代码共享签约（不要求机队 20 架）。</summary>
        public bool SignCodeshare(string partner, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var stg = EnsureState(gs);
            if (gs.cash < CodeshareFeeWan)
            {
                message = $"现金不足（代码共享签约费 {CodeshareFeeWan:F0} 万）。";
                return false;
            }
            gs.cash -= CodeshareFeeWan;
            stg.alliance.codeSharePartners++;
            RefreshAllianceLift(gs);
            string name = string.IsNullOrEmpty(partner) ? "合作航司" : partner;
            message = $"已与 {name} 签署代码共享（伙伴 {stg.alliance.codeSharePartners}，年增量约 {stg.alliance.revenueLiftWan:F0} 万）。";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>以观察员身份入盟（机队 ≥3，适合开局）。正式成员仍走 UpgradeAlliance。</summary>
        public bool JoinAsObserver(out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var stg = EnsureState(gs);
            if (stg.alliance.tier >= 1)
            {
                message = "已是联盟成员，请走升级通道。";
                return false;
            }
            int fleet = gs.fleet != null ? gs.fleet.Count : 0;
            if (fleet < 3) { message = "入盟观察员需机队 ≥3 架。"; return false; }
            if (gs.cash < ObserverJoinFeeWan)
            {
                message = $"现金不足（入盟费 {ObserverJoinFeeWan:F0} 万）。";
                return false;
            }
            gs.cash -= ObserverJoinFeeWan;
            stg.alliance.tier = 1;
            if (string.IsNullOrEmpty(stg.alliance.allianceName))
                stg.alliance.allianceName = "星空联盟";
            RefreshAllianceLift(gs);
            message = $"已加入 {stg.alliance.allianceName}（观察员），年增量收入约 {stg.alliance.revenueLiftWan:F0} 万。";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        public bool UpgradeAlliance(out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var stg = EnsureState(gs);
            if (stg.alliance.tier >= 3) { message = "已是核心成员。"; return false; }
            int fleet = gs.fleet != null ? gs.fleet.Count : 0;
            if (fleet < 20) { message = "入盟升级需机队 ≥20 架。"; return false; }
            stg.alliance.tier++;
            stg.alliance.revenueLiftWan = StrategyEconomics.AllianceRevenueLiftWan(
                stg.alliance.tier, gs.activeRoutes != null ? gs.activeRoutes.Count : 0, stg.alliance.codeSharePartners);
            message = $"联盟等级提升至 {TierName(stg.alliance.tier)}（年增量收入约 {stg.alliance.revenueLiftWan:F0} 万）。";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static string TierName(int tier) => tier switch { 0 => "非成员", 1 => "观察员", 2 => "正式成员", _ => "核心成员" };

        #endregion

        #region 投资并购

        public bool SubmitInvestment(InvestmentProject p, out string message)
            => ProposeInvestment(p, out message);

        /// <summary>立项并送收件箱，不立刻扣款。</summary>
        public bool ProposeInvestment(InvestmentProject p, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (p == null || string.IsNullOrEmpty(p.name)) { message = "项目名称不能为空"; return false; }
            var stg = EnsureState(gs);
            if (string.IsNullOrEmpty(p.id))
                p.id = "inv_" + stg.investments.Count.ToString("D2");
            if (p.capitalWan < 1f) p.capitalWan = 280f;
            p.approved = false;
            p.stage = "待批";
            for (int i = 0; i < stg.investments.Count; i++)
            {
                var e = stg.investments[i];
                if (e == null) continue;
                if (e.id == p.id || e.name == p.name)
                {
                    if (e.stage == "待批" || e.stage == "待投入")
                    {
                        message = $"「{p.name}」已在收件箱待批，请勿重复立项。";
                        return false;
                    }
                    if (e.stage == "执行")
                    {
                        message = $"「{p.name}」已在执行。";
                        return false;
                    }
                }
            }
            int idx = stg.investments.FindIndex(x => x != null && x.id == p.id);
            if (idx >= 0) stg.investments[idx] = p;
            else stg.investments.Add(p);
            InboxManager.QueueInvestmentMail(gs, p);
            float score = StrategyEconomics.InvestmentScore(p.businessValue, p.resourceFit, p.barrier, p.strategicFit);
            message = $"「{p.name}」已提交 CEO 审批（综合 {score:F0} 分 · 资本 {p.capitalWan:F0} 万）。请到收件箱批准，下季度扣款后进入执行。";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        public bool ExecuteInvestment(string projectId, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var stg = EnsureState(gs);
            var p = FindInvestment(stg, projectId);
            if (p == null) { message = "未找到投资项目。"; return false; }
            if (p.stage == "执行" && p.approved)
            {
                message = $"「{p.name}」已在执行。";
                return true;
            }
            float cap = p.capitalWan < 1f ? 280f : p.capitalWan;
            if (gs.cash < cap)
            {
                p.stage = "待投入";
                message = $"现金不足，需 {cap:F0} 万才能启动「{p.name}」。项目仍待投入。";
                gs.NotifyChanged();
                return false;
            }
            gs.cash -= cap;
            p.approved = true;
            p.stage = "执行";
            stg.strategicConsistency = Mathf.Min(100f, stg.strategicConsistency + 2f);
            message = $"已投入 {cap:F0} 万，「{p.name}」进入执行（NPV {p.npvWan:F0} 万）。";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        public bool RejectInvestment(string projectId, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var p = FindInvestment(EnsureState(gs), projectId);
            if (p == null) { message = "未找到投资项目。"; return false; }
            p.approved = false;
            p.stage = "已拒";
            message = $"已驳回「{p.name}」，本季不扣资本。";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        public static InvestmentProject FindInvestment(StrategyState stg, string id)
        {
            if (stg?.investments == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < stg.investments.Count; i++)
                if (stg.investments[i] != null && stg.investments[i].id == id)
                    return stg.investments[i];
            return null;
        }

        public static InvestmentProject CatalogHub() => new InvestmentProject
        {
            id = "inv_hub5",
            name = "第五航权枢纽",
            businessValue = 75f,
            resourceFit = 60f,
            barrier = 55f,
            strategicFit = 82f,
            npvWan = 1200f,
            capitalWan = 280f,
            stage = "待批"
        };

        public static InvestmentProject CatalogMna() => new InvestmentProject
        {
            id = "inv_mna1",
            name = "区域航司并购",
            businessValue = 68f,
            resourceFit = 52f,
            barrier = 70f,
            strategicFit = 74f,
            npvWan = 900f,
            capitalWan = 420f,
            stage = "待批"
        };

        #endregion

        #region 黑天鹅预案

        public bool SaveContingency(ContingencyPlan c, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            if (c == null || string.IsNullOrEmpty(c.trigger)) { message = "请选择触发器"; return false; }
            var stg = EnsureState(gs);
            if (stg.contingencyPlans.Count >= 2)
            {
                message = "最多预设 2 套黑天鹅预案。";
                return false;
            }
            c.active = true;
            stg.contingencyPlans.Add(c);
            message = $"预案已激活：{c.trigger} → {c.autoMeasure}（损失降低 {c.lossReduction * 100:F0}%）。";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 战略豁免

        public bool ApplyWaiver(out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var stg = EnsureState(gs);
            int limit = ProgressionGate.WaiverLimit(gs.year);
            if (stg.waiverUsed >= limit) { message = $"本财年 {limit} 项豁免额度已用尽。"; return false; }
            stg.waiverUsed++;
            message = $"已申请战略亏损豁免（本财年已用 {stg.waiverUsed}/{limit}）。";
            if (gs.year >= 3)
                message += "（见习期软保护：战略豁免权自动获批 1 项）";
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        #endregion
    }
}
