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
        {
            message = "";
            if (gs == null) { message = "无 GameState"; return false; }
            var stg = EnsureState(gs);
            int n = count < 1 ? 1 : count;
            int clockMin = Logic.ScheduleSlots.ParseClock(clock);
            if (clockMin < 0)
            {
                message = $"时刻无效（{clock}）。时刻槽须精确到具体时刻（如 06:30 / 08:00）。";
                return false;
            }
            clock = Logic.ScheduleSlots.FormatClock(clockMin);
            int band = Logic.ScheduleSlots.BandOfClock(clock);

            int room = RoutePermitLogic.SlotsAvailable(gs, airport, band);
            if (room < n)
            {
                message = $"该机场该时段时刻池余量不足（要 {n} 条，{RoutePermitLogic.SlotPoolLine(gs, airport, band)}）。";
                return false;
            }

            float unit = free ? 0f : StrategyEconomics.SlotValueWan(airport, band);
            float cost = unit * n;
            if (!free && gs.cash < cost)
            {
                message = $"现金不足，{airport} {clock} 时刻槽 {n} 条需 {cost:F0} 万"
                          + $"（{AirportCatalog.TierLabel(AirportCatalog.TierOfCode(airport))}价，单价 {unit:F0} 万）。"
                          + "若走协调池申请则不收费，可在收件箱提「申请时刻槽」。";
                return false;
            }
            if (!free) gs.cash -= cost;
            int turn = RoutePermitLogic.Turn(gs.year, gs.quarter);
            for (int i = 0; i < n; i++)
            {
                stg.slots.Add(new SlotHolding
                {
                    airport = airport,
                    slotTime = clock,
                    inUse = false,
                    acquisitionCostWan = unit,
                    expiresQuarter = gs.quarter + 2,
                    // 闲置收回的时点：取得季 + 2 个季度（留一个航季的筹备窗口），见 ReleaseIdleHoldings
                    expiresTurn = turn + 2
                });
            }
            message = free
                ? (n == 1
                    ? $"协调池已分配 {airport} {clock} 时刻槽（不收费）。"
                    : $"协调池已分配 {airport} {clock} 时刻槽 {n} 条（不收费）。")
                : (n == 1
                    ? $"已购 {airport} {clock} 时刻槽（{cost:F0} 万）。"
                    : $"已购 {airport} {clock} 时刻槽 {n} 条（共 {cost:F0} 万）。");
            gs.AddDecisionLog("战略：" + message);
            gs.NotifyChanged();
            return true;
        }

        public bool ApplyRouteRight(string origin, string destination, int freedom, out string message)
        {
            var gs = GameState.Instance;
            if (gs == null) { message = "无 GameState"; return false; }
            var stg = EnsureState(gs);
            float govCost = 15f + freedom * 2f;
            if (stg.governmentRelation < govCost) { message = "政府关系点数不足。"; return false; }
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
