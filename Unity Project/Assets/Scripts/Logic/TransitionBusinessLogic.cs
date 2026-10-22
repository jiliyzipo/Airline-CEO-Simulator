using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 开局过渡业务（GDD ·「三架飞机的盈利设计」）。
    ///
    /// 场景：公司刚注册，定期航线许可还在审批、机组还在招聘，三架飞机停在场上天天烧钱。
    /// 本模块让玩家把闲置运力投入 干租 / 湿租 / 包机 / 培训，在等许可证的空窗期里产生现金。
    ///
    /// 统一模型：四种业务都是「一份合同占用一架飞机 N 天、每天入账 rateWanPerDay 万美元」，
    /// 差异只在解锁门槛、档期长短、日费率与并发上限：
    ///   干租 dry      无门槛                     90 天   0.55 万/天基准   最多 3 架（市场长期有客户）
    ///   湿租 wet      需「飞行运行主管」          90 天   1.10             1 架（客户稀少）
    ///   包机 charter  需「飞行运行主管」          2–5 天  2.60             报价 3 天有效，抢单
    ///   培训 training 需「客舱经理」              5–7 天  1.65             同时 1 批
    ///
    /// 反向压力（让玩家有取舍而不是无脑全租出去）：
    ///   1. 合同期内的飞机不能执飞定期航线 —— 许可证批下来时要决定何时收回；
    ///   2. 提前召回要付剩余租金 30% 违约金 + 3 天交机期；
    ///   3. 湿租 / 培训有并发上限，包机机会短促 —— 全干租最稳但收益最低，留空博高价则有闲置风险。
    /// </summary>
    public static class TransitionBusinessLogic
    {
        // ———— 并发上限 ————
        public const int MaxDry = 3;
        public const int MaxWet = 1;
        public const int MaxTraining = 1;
        public const int MaxCharterOffers = 3;
        public const int MaxTrainingOffers = 1;

        // ———— 档期与条款 ————
        /// <summary>干租 / 湿租档期（1 个季度）。</summary>
        public const int LeaseDays = 90;
        /// <summary>提前召回的通知期（天），通知期内租金照付。</summary>
        public const int RecallNoticeDays = 3;
        /// <summary>提前召回违约金 = 剩余租金 × 30%。</summary>
        public const float RecallPenaltyRate = 0.30f;
        /// <summary>包机报价有效期（天）。</summary>
        public const int CharterOfferDays = 3;
        /// <summary>培训报价有效期（天）。</summary>
        public const int TrainingOfferDays = 5;

        // ———— 日费率基准（万美元/天，180 座级）————
        public const float DryRate = 0.55f;
        public const float WetRate = 1.10f;
        public const float CharterRate = 2.60f;
        public const float TrainingRate = 1.65f;

        // ———— 付款里程碑（CEO 定金/尾款制）————
        /// <summary>定金比例：签约当日收讫。</summary>
        public const float DepositRate = 0.20f;
        /// <summary>尾款比例：交机 / 到期当日结算。</summary>
        public const float BalanceRate = 0.20f;
        /// <summary>逐日入账比例：执行期内按天计入现金。</summary>
        public const float DailyShareRate = 0.60f;

        /// <summary>把合同总额拆成「定金 / 逐日 / 尾款」三段（万美元）。</summary>
        public static void SplitTotal(float totalWan, out float deposit, out float balance, out float daily)
        {
            deposit = Mathf.Round(totalWan * DepositRate * 10f) / 10f;
            balance = Mathf.Round(totalWan * BalanceRate * 10f) / 10f;
            daily = Mathf.Max(0f, totalWan - deposit - balance);
        }

        /// <summary>名义日费率下的日入账（执行期）金额。</summary>
        public static float DailyCashOf(float rateWanPerDay)
            => Mathf.Round(rateWanPerDay * DailyShareRate * 100f) / 100f;

        /// <summary>包机 / 培训 机会的每日出现概率（控制"抢单"的稀缺感）。</summary>
        const float CharterSpawnChance = 0.38f;
        const float TrainingSpawnChance = 0.14f;

        // ———— 解锁门槛所用的岗位（运营部）————
        /// <summary>机组成员的最小代理：运营部飞行运行主管到位即可配机组。</summary>
        public const string CrewPost = "飞行运行主管";
        /// <summary>培训业务需要客舱经理组织教员。</summary>
        public const string InstructorPost = "客舱经理";

        /// <summary>开局请示的正式题名（邮件主题与正文引用共用）。</summary>
        public const string IntroSubject = "关于开航前闲置运力过渡经营的请示";
        /// <summary>进度函 action，告知类，无需批复。</summary>
        public const string ProgressAction = "transition_progress";
        /// <summary>客户询价请示：收件箱底部直接签约 / 不接，不强制去机队页。</summary>
        public const string InquiryAction = "transition_inquiry";

        static bool _collectFresh;
        static readonly List<TransitionOffer> _freshOffers = new List<TransitionOffer>();
        static readonly List<TransitionOffer> _expiredOffers = new List<TransitionOffer>();

        #region 初始化与查询

        /// <summary>新开局：清空合同与报价，并允许重新投递引导信。</summary>
        public static void Reset(GameState gs)
        {
            if (gs == null) return;
            gs.transitionContracts = new List<TransitionContract>();
            gs.transitionOffers = new List<TransitionOffer>();
            gs.transitionIntroSent = false;
            gs.transitionCrewMailSent = false;
            gs.transitionInstructorMailSent = false;
            gs.liveQuarterTransitionIncome = 0f;
        }

        /// <summary>
        /// 开局引导：铺好常驻报价板并投递运营部《开航前过渡业务建议》。幂等，
        /// 由 MVPBootstrap.OnSetupCompleted 调用（读档时标记已置位，直接跳过）。
        /// </summary>
        public static void Bootstrap(GameState gs)
        {
            if (gs == null) return;
            Ensure(gs);
            if (gs.transitionIntroSent)
            {
                if (!gs.takeoverMode)
                    RefreshIntroMail(gs);
                return;
            }
            gs.transitionIntroSent = true;
            SendIntroMail(gs);
            gs.NotifyChanged();
        }

        /// <summary>确保容器就绪、清理失效合同、补足常驻报价。幂等，可从 UI 安全调用。</summary>
        public static void Ensure(GameState gs)
        {
            if (gs == null) return;
            if (gs.transitionContracts == null) gs.transitionContracts = new List<TransitionContract>();
            if (gs.transitionOffers == null) gs.transitionOffers = new List<TransitionOffer>();

            for (int i = gs.transitionContracts.Count - 1; i >= 0; i--)
            {
                var c = gs.transitionContracts[i];
                var ac = c != null ? FindAircraft(gs, c.aircraftReg) : null;
                if (c == null || ac == null || !ac.isActive || ac.status == AircraftStatuses.Retired)
                    gs.transitionContracts.RemoveAt(i);
                else MigrateContract(gs, c);
            }
            for (int i = gs.transitionOffers.Count - 1; i >= 0; i--)
                if (gs.transitionOffers[i] == null) gs.transitionOffers.RemoveAt(i);

            TopUpStandingOffers(gs);
            if (gs.transitionIntroSent) RefreshIntroMail(gs);
            UpgradeLegacyInquiryMails(gs);
            ExpireStaleInquiryMails(gs);
            RefreshInquiryMailBodies(gs);
        }

        const string LegacyInquiryCue =
            "请审阅后在本函底部直接签约或选择本次不接，无需再到机队页操作。";

        /// <summary>旧档合同迁移：按「全额逐日入账、无定金/尾款」处理，并补出真实起始日，避免重复收款。</summary>
        static void MigrateContract(GameState gs, TransitionContract c)
        {
            if (c == null) return;
            if (c.dailyCashWan <= 0f && c.depositWan <= 0f && c.balanceWan <= 0f)
            {
                c.dailyCashWan = c.rateWanPerDay;
                c.depositWan = 0f;
                c.balanceWan = 0f;
                c.depositPaid = true;
                c.balancePaid = true;
            }
            if (c.startUtcTicks <= 0 && gs != null)
                c.startUtcTicks = gs.SimUtc.Ticks - (long)Math.Max(0, c.daysUsed) * TimeSpan.TicksPerDay;
            if (c.signedUtcTicks <= 0) c.signedUtcTicks = c.startUtcTicks;
        }

        /// <summary>旧档询价进度函改为待批复请示，以便在收件箱底部直接签约。</summary>
        public static void UpgradeLegacyInquiryMails(GameState gs)
        {
            if (gs?.inbox == null) return;
            var ids = OfferIdsOf(gs, TransitionKinds.Charter, TransitionKinds.Training, TransitionKinds.Wet);
            string csv = ids.Count > 0 ? string.Join(",", ids.ToArray()) : "";
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null) continue;
                if (m.action == ProgressAction && m.tag == "询价" && ids.Count > 0)
                {
                    m.action = InquiryAction;
                    m.status = "pending";
                    m.kind = "alert";
                    m.payloadCsv = csv;
                }
                if ((m.action == InquiryAction || m.action == ProgressAction)
                    && !string.IsNullOrEmpty(m.body) && m.body.Contains(LegacyInquiryCue))
                    m.body = m.body.Replace(LegacyInquiryCue, "").Replace("现将意向整理如下。。", "现将意向整理如下。");
            }
        }

        public static Aircraft FindAircraft(GameState gs, string registration)
        {
            if (gs?.fleet == null || string.IsNullOrEmpty(registration)) return null;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a != null && string.Equals(a.registration, registration, StringComparison.OrdinalIgnoreCase))
                    return a;
            }
            return null;
        }

        /// <summary>该注册号是否已被过渡业务占用（在租执行中 或 已预约机位）。</summary>
        public static bool IsBusy(GameState gs, string registration)
            => ActiveContractFor(gs, registration) != null || ReservedContractFor(gs, registration) != null;

        /// <summary>该注册号是否有在租（执行中）的过渡业务。</summary>
        public static bool IsActiveLease(GameState gs, string registration)
            => ActiveContractFor(gs, registration) != null;

        /// <summary>该注册号是否有未来生效的预约（预租）。</summary>
        public static bool IsReserved(GameState gs, string registration)
            => ReservedContractFor(gs, registration) != null;

        /// <summary>在租合同（已生效、执行中）；无则返回 null。</summary>
        public static TransitionContract ActiveContractFor(GameState gs, string registration)
        {
            if (gs?.transitionContracts == null || string.IsNullOrEmpty(registration)) return null;
            long now = gs.SimUtc.Ticks;
            for (int i = 0; i < gs.transitionContracts.Count; i++)
            {
                var c = gs.transitionContracts[i];
                if (c == null || !Same(c.aircraftReg, registration)) continue;
                if (!c.IsReserved(now)) return c;
            }
            return null;
        }

        /// <summary>预租合同（起始日在未来）；多个时取最早的一个。</summary>
        public static TransitionContract ReservedContractFor(GameState gs, string registration)
        {
            if (gs?.transitionContracts == null || string.IsNullOrEmpty(registration)) return null;
            long now = gs.SimUtc.Ticks;
            TransitionContract best = null;
            for (int i = 0; i < gs.transitionContracts.Count; i++)
            {
                var c = gs.transitionContracts[i];
                if (c == null || !Same(c.aircraftReg, registration)) continue;
                if (!c.IsReserved(now)) continue;
                if (best == null || c.startUtcTicks < best.startUtcTicks) best = c;
            }
            return best;
        }

        /// <summary>兼容旧调用：返回该机当前在租合同。</summary>
        public static TransitionContract ContractFor(GameState gs, string registration)
            => ActiveContractFor(gs, registration);

        static bool Same(string a, string b)
            => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>该机的全部合同（在租 + 预租）。</summary>
        public static List<TransitionContract> ContractsFor(GameState gs, string registration)
        {
            var list = new List<TransitionContract>();
            if (gs?.transitionContracts == null || string.IsNullOrEmpty(registration)) return list;
            for (int i = 0; i < gs.transitionContracts.Count; i++)
            {
                var c = gs.transitionContracts[i];
                if (c != null && Same(c.aircraftReg, registration)) list.Add(c);
            }
            return list;
        }

        /// <summary>在租合同（执行中）列表，按起始日排序。</summary>
        public static List<TransitionContract> ActiveContracts(GameState gs)
        {
            var list = new List<TransitionContract>();
            if (gs?.transitionContracts == null) return list;
            long now = gs.SimUtc.Ticks;
            for (int i = 0; i < gs.transitionContracts.Count; i++)
            {
                var c = gs.transitionContracts[i];
                if (c != null && !c.IsReserved(now)) list.Add(c);
            }
            list.Sort((a, b) => a.startUtcTicks.CompareTo(b.startUtcTicks));
            return list;
        }

        /// <summary>预租合同（起始日在未来）列表，按起始日排序。</summary>
        public static List<TransitionContract> ReservedContracts(GameState gs)
        {
            var list = new List<TransitionContract>();
            if (gs?.transitionContracts == null) return list;
            long now = gs.SimUtc.Ticks;
            for (int i = 0; i < gs.transitionContracts.Count; i++)
            {
                var c = gs.transitionContracts[i];
                if (c != null && c.IsReserved(now)) list.Add(c);
            }
            list.Sort((a, b) => a.startUtcTicks.CompareTo(b.startUtcTicks));
            return list;
        }

        /// <summary>机位是否与既有合同冲突（按各合同「起始—结束」区间判定）。</summary>
        public static bool Overlaps(GameState gs, string registration, long startTicks, int days, string ignoreContractId = null)
        {
            if (gs?.transitionContracts == null || string.IsNullOrEmpty(registration)) return false;
            long now = gs.SimUtc.Ticks;
            long a0 = startTicks > 0 ? startTicks : now;
            long a1 = a0 + (long)Mathf.Max(1, days) * TimeSpan.TicksPerDay;
            for (int i = 0; i < gs.transitionContracts.Count; i++)
            {
                var c = gs.transitionContracts[i];
                if (c == null || !Same(c.aircraftReg, registration)) continue;
                if (!string.IsNullOrEmpty(ignoreContractId) && c.id == ignoreContractId) continue;
                long b0 = c.startUtcTicks > 0 ? c.startUtcTicks : now - (long)Math.Max(0, c.daysUsed) * TimeSpan.TicksPerDay;
                long b1 = b0 + (long)Mathf.Max(1, c.totalDays) * TimeSpan.TicksPerDay;
                if (a0 < b1 && b0 < a1) return true;
            }
            return false;
        }

        /// <summary>该机最早可用的起始时刻（不早于现在）；找不到空闲窗口时返回 0。</summary>
        public static long NextFreeStart(GameState gs, string registration, int days, int maxProbe = 24)
        {
            long now = gs.SimUtc.Ticks;
            long cursor = now;
            for (int probe = 0; probe < maxProbe; probe++)
            {
                if (!Overlaps(gs, registration, cursor, days)) return cursor;
                // 推进到与之冲突的合同结束点
                long next = long.MaxValue;
                if (gs?.transitionContracts != null)
                {
                    for (int i = 0; i < gs.transitionContracts.Count; i++)
                    {
                        var c = gs.transitionContracts[i];
                        if (c == null || !Same(c.aircraftReg, registration)) continue;
                        long b0 = c.startUtcTicks > 0 ? c.startUtcTicks : now - (long)Math.Max(0, c.daysUsed) * TimeSpan.TicksPerDay;
                        long b1 = b0 + (long)Mathf.Max(1, c.totalDays) * TimeSpan.TicksPerDay;
                        if (b1 > cursor && b1 < next) next = b1;
                    }
                }
                if (next == long.MaxValue) return 0;
                cursor = next;
            }
            return 0;
        }

        /// <summary>可预约机位：活跃、未退役/维护、未排入定期航线，且存在空闲窗口。</summary>
        public struct BookingOption
        {
            public Aircraft aircraft;
            public long startTicks;   // 0 = 现在即可开始
            public bool future;       // true = 需预约未来日期（预租）
        }

        public static List<BookingOption> BookingOptions(GameState gs, int days)
        {
            var list = new List<BookingOption>();
            if (gs?.fleet == null) return list;
            long now = gs.SimUtc.Ticks;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || !a.isActive) continue;
                if (a.status == AircraftStatuses.Maintenance || a.status == AircraftStatuses.Retired) continue;
                if (a.status == AircraftStatuses.InFlight) continue;
                if (RouteUsing(gs, a.registration) != null) continue;
                long start = NextFreeStart(gs, a.registration, days);
                if (start <= 0) continue;
                list.Add(new BookingOption
                {
                    aircraft = a,
                    startTicks = start,
                    future = start > now
                });
            }
            // 现成可用的排前面，其次按预约日期
            list.Sort((x, y) => x.future == y.future ? x.startTicks.CompareTo(y.startTicks) : (x.future ? 1 : -1));
            return list;
        }

        /// <summary>机队列表「状态」列用的占用说明；空闲返回空串。</summary>
        public static string BusyLabel(GameState gs, string registration)
        {
            var c = ContractFor(gs, registration);
            if (c != null)
            {
                string label = TransitionKinds.Label(c.kind);
                return c.Recalling
                    ? $"{label}·交机 {c.recallDaysLeft} 天"
                    : $"{label}·剩 {c.DaysLeft} 天";
            }
            var r = ReservedContractFor(gs, registration);
            if (r != null)
                return $"{TransitionKinds.Label(r.kind)}·预租 {DateFormat(gs, r.startUtcTicks)}";
            return "";
        }

        /// <summary>
        /// 该机为什么不能排定期航班（可排班返回空串）。
        /// 判据与 <see cref="AvailableAircraft"/> / <see cref="BusyLabel"/> 同源：不在册 / 已退役退租 / 被过渡业务占用。
        /// 供 n98 航线选择器把「为什么点不动」如实摊开（旧版把这些飞机直接禁用，点击毫无反馈）。
        /// </summary>
        public static string ScheduleBlockReason(GameState gs, string registration)
        {
            if (string.IsNullOrEmpty(registration)) return "尚未选择飞机。";
            Aircraft ac = FindFleetAircraft(gs, registration);
            if (ac == null) return $"{registration} 已不在机队（老存档残留 / 已退役退租），无可排班。";
            if (!ac.isActive || ac.status == AircraftStatuses.Retired)
                return $"{registration} 已{ac.ExitLabel}，不再执飞定期航班。";

            string busy = BusyLabel(gs, registration);
            if (!string.IsNullOrEmpty(busy))
                return $"{registration} 正投入过渡业务（{busy}），合同期内不得执飞定期航班。";
            return "";
        }

        /// <summary>
        /// 不可排班时的占用明细：机型 / 来源 / 月租，加上每一份过渡业务合同的期限、费率与召回代价。
        /// 与 <see cref="ScheduleBlockReason"/> 配套，界面只负责排版。
        /// </summary>
        public static string ScheduleBlockDetail(GameState gs, string registration)
        {
            var sb = new System.Text.StringBuilder();
            var ac = FindFleetAircraft(gs, registration);
            if (ac != null)
            {
                sb.Append($"机型 {ac.type} · 机龄 {ac.age} 年 · 来源 {(string.IsNullOrEmpty(ac.source) ? "自有" : ac.source)}")
                  .Append($" · 月租 {(ac.monthlyLeaseWan > 0.01f ? ac.monthlyLeaseWan.ToString("F1") + " 万" : "—")}")
                  .Append($" · 状态 {(string.IsNullOrEmpty(ac.ExitLabel) ? ac.status : "已" + ac.ExitLabel)}").Append('\n');
            }

            if (ac != null && ac.status == AircraftStatuses.Retired)
            {
                sb.Append("该机已退出机队：机号仍留在航线记录里备查，但不再产生航班。\n")
                  .Append("请另选一架在册飞机排班；若航线还挂着这个机号，运营部会按「无机可派」如实呈报。");
                return sb.ToString();
            }

            long now = gs != null ? gs.SimUtc.Ticks : 0L;
            var contracts = ContractsFor(gs, registration);
            if (contracts.Count == 0)
                sb.Append("（合同明细未找到，请在「过渡业务」页复核。）\n");
            for (int i = 0; i < contracts.Count; i++)
            {
                var c = contracts[i];
                if (c == null) continue;
                sb.Append(ScheduleContractLine(gs, c, c.IsReserved(now)));
            }
            sb.Append("要排定期航班，可在「过渡业务」页召回（干租 / 湿租可提前召回，须付违约金并留交机期），或另选一架飞机。");
            return sb.ToString();
        }

        static string ScheduleContractLine(GameState gs, TransitionContract c, bool reserved)
        {
            string term = reserved
                ? $"预租占位 · {DateFormat(gs, c.startUtcTicks)} 起 {c.totalDays} 天"
                : $"剩余 {c.DaysLeft} 天 / 共 {c.totalDays} 天";
            string exit;
            if (c.Recalling)
                exit = $"交机中，还剩 {c.recallDaysLeft} 天（违约金 {c.recallFeeWan:F1} 万）";
            else if (reserved)
                exit = "尚未开始，可在「过渡业务」页取消预约（定金不退）";
            else if (TransitionKinds.IsLease(c.kind))
                exit = $"可召回：通知期 {RecallNoticeDays} 天，违约金约 {RecallFeeWan(c):F1} 万";
            else
                exit = "不可中途召回，须等任务结束";
            return $"　· {TransitionKinds.Label(c.kind)}合同 · {c.client} · {term} · {c.EffectiveDailyCash:F2} 万/天 · {exit}\n";
        }

        /// <summary>在册机队里按注册号取飞机（大小写不敏感）；不在册返回 null。</summary>
        public static Aircraft FindFleetAircraft(GameState gs, string registration)
        {
            if (gs?.fleet == null || string.IsNullOrEmpty(registration)) return null;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a != null && string.Equals(a.registration, registration, StringComparison.OrdinalIgnoreCase))
                    return a;
            }
            return null;
        }

        /// <summary>可签约的飞机（在册、适航、未排入航线、今日起无合同冲突）。</summary>
        public static List<Aircraft> AvailableAircraft(GameState gs)
        {
            var list = new List<Aircraft>();
            if (gs?.fleet == null) return list;
            long now = gs.SimUtc.Ticks;
            for (int i = 0; i < gs.fleet.Count; i++)
            {
                var a = gs.fleet[i];
                if (a == null || !a.isActive) continue;
                if (a.status == AircraftStatuses.Maintenance || a.status == AircraftStatuses.Retired) continue;
                if (a.status == AircraftStatuses.InFlight) continue;
                if (RouteUsing(gs, a.registration) != null) continue;
                if (Overlaps(gs, a.registration, now, 1)) continue;   // 在租或已预约占用
                list.Add(a);
            }
            return list;
        }

        /// <summary>该注册号当前是否已排入某条在营航线（已排班的飞机不能转投过渡业务）。</summary>
        public static RouteData RouteUsing(GameState gs, string registration)
        {
            if (gs?.activeRoutes == null || string.IsNullOrEmpty(registration)) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                if (string.Equals(r.registration, registration, StringComparison.OrdinalIgnoreCase)) return r;
                if (r.flights == null) continue;
                for (int f = 0; f < r.flights.Count; f++)
                {
                    var fl = r.flights[f];
                    if (fl != null && string.Equals(fl.registration, registration, StringComparison.OrdinalIgnoreCase))
                        return r;
                }
            }
            return null;
        }

        /// <summary>在租合同（执行中）的日入账合计（万美元/天）。预租未生效不计。</summary>
        public static float DailyIncome(GameState gs)
        {
            if (gs?.transitionContracts == null) return 0f;
            long now = gs.SimUtc.Ticks;
            float s = 0f;
            for (int i = 0; i < gs.transitionContracts.Count; i++)
            {
                var c = gs.transitionContracts[i];
                if (c == null || c.IsReserved(now)) continue;
                s += Mathf.Max(0f, c.EffectiveDailyCash);
            }
            return s;
        }

        /// <summary>预租合同占用的机位数量与合同总额。</summary>
        public static int ReservedCount(GameState gs) => ReservedContracts(gs).Count;

        /// <summary>预租合同总额合计（万美元）。</summary>
        public static float ReservedTotalWan(GameState gs)
        {
            var list = ReservedContracts(gs);
            float s = 0f;
            for (int i = 0; i < list.Count; i++) s += list[i].TotalWan;
            return s;
        }

        /// <summary>预租已收定金合计（万美元）。</summary>
        public static float ReservedDepositWan(GameState gs)
        {
            var list = ReservedContracts(gs);
            float s = 0f;
            for (int i = 0; i < list.Count; i++) if (list[i].depositPaid) s += list[i].depositWan;
            return s;
        }

        /// <summary>
        /// 过渡业务月收入估计（万美元/月），供跑道与财务面板使用。
        ///
        /// **必须按剩余档期封顶**：合同有 <c>totalDays</c>，原实现是 <c>DailyIncome × 30</c>，
        /// 等于把一份 5 天包机（2.6 万/日）当成永续的月入 78 万、把 90 天干租按满额外推到未来无限月。
        /// 接管局的跑道恰恰靠这笔收入撑起来，于是选司界面读「6 个月」、实际首季多烧 30%
        /// （2026-10-21 K10 跨季度复验：★3 实测 1416.8 vs 预览×3 1088.8）。
        /// 到期后收入本就归零，折成「剩余天数与 30 取小」才是月均的真实含义。
        /// </summary>
        public static float MonthlyIncome(GameState gs)
        {
            if (gs?.transitionContracts == null) return 0f;
            long now = gs.SimUtc.Ticks;
            float s = 0f;
            for (int i = 0; i < gs.transitionContracts.Count; i++)
            {
                var c = gs.transitionContracts[i];
                if (c == null || c.IsReserved(now)) continue;
                int days = Mathf.Min(30, c.DaysLeft);
                if (days <= 0) continue;
                s += Mathf.Max(0f, c.EffectiveDailyCash) * days;
            }
            return s;
        }

        public static int ContractCount(GameState gs) => gs?.transitionContracts?.Count ?? 0;

        public static int CountContracts(GameState gs, string kind)
        {
            if (gs?.transitionContracts == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.transitionContracts.Count; i++)
                if (gs.transitionContracts[i] != null && gs.transitionContracts[i].kind == kind) n++;
            return n;
        }

        public static int CountOffers(GameState gs, string kind)
        {
            if (gs?.transitionOffers == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.transitionOffers.Count; i++)
                if (gs.transitionOffers[i] != null && gs.transitionOffers[i].kind == kind) n++;
            return n;
        }

        public static TransitionOffer FindOffer(GameState gs, string offerId)
        {
            if (gs?.transitionOffers == null || string.IsNullOrEmpty(offerId)) return null;
            for (int i = 0; i < gs.transitionOffers.Count; i++)
            {
                var o = gs.transitionOffers[i];
                if (o != null && o.id == offerId) return o;
            }
            return null;
        }

        #endregion

        #region 解锁门槛（机组 / 教员到位）

        /// <summary>某部门某岗位的在职人数（含该部门的负责人与副手）。</summary>
        public static int HeadcountOf(GameState gs, string dept, string jobTitle)
        {
            var mgr = DepartmentManager.Instance;
            int n = 0;
            var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mgr != null)
            {
                var head = SafeHead(mgr, dept);
                if (head != null)
                {
                    if (!string.IsNullOrEmpty(head.id)) occupied.Add(head.id);
                    if (StaffEstablishment.DisplayTitle(head.jobTitle, "负责人") == jobTitle) n++;
                }
                var dep = SafeDeputy(mgr, dept);
                if (dep != null)
                {
                    if (!string.IsNullOrEmpty(dep.id)) occupied.Add(dep.id);
                    if (StaffEstablishment.DisplayTitle(dep.jobTitle, "副手") == jobTitle) n++;
                }
            }
            if (gs?.staffPool == null) return n;
            for (int i = 0; i < gs.staffPool.Count; i++)
            {
                var p = gs.staffPool[i];
                if (p == null || p.onMarket) continue;
                if (!string.Equals(p.department, dept, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(p.id) && occupied.Contains(p.id)) continue;
                if (p.jobTitle != jobTitle) continue;
                n++;
            }
            return n;
        }

        /// <summary>机组就位：运营部飞行运行主管到位 —— 解锁 包机 与 湿租。</summary>
        public static bool CrewReady(GameState gs) => HeadcountOf(gs, "OPS", CrewPost) >= 1;

        /// <summary>教员就位：运营部客舱经理到位 —— 解锁 培训。</summary>
        public static bool InstructorReady(GameState gs) => HeadcountOf(gs, "OPS", InstructorPost) >= 1;

        /// <summary>该业务当前能否签约；返回空串表示可签，否则返回不可签的原因。</summary>
        public static string GateReason(GameState gs, string kind)
        {
            switch (kind)
            {
                case TransitionKinds.Dry:
                    return "";
                case TransitionKinds.Wet:
                case TransitionKinds.Charter:
                    return CrewReady(gs) ? "" : $"需先聘用「{CrewPost}」";
                case TransitionKinds.Training:
                    return InstructorReady(gs) ? "" : $"需先聘用「{InstructorPost}」";
                default:
                    return "未知业务";
            }
        }

        public static int CapOf(string kind) => kind switch
        {
            TransitionKinds.Dry => MaxDry,
            TransitionKinds.Wet => MaxWet,
            TransitionKinds.Training => MaxTraining,
            _ => 99
        };

        #endregion

        #region 签约与召回

        /// <summary>机型座位系数：以 180 座为 1.0，用于把基准日费率折算到具体飞机。</summary>
        public static float SizeFactor(Aircraft ac)
        {
            float seats = ac != null ? ac.TotalSeats : 180f;
            if (seats <= 1f) seats = 180f;
            return Mathf.Clamp(seats / 180f, 0.55f, 3.2f);
        }

        /// <summary>把报价基准费率折算到具体飞机后的实际日费率（万美元/天）。</summary>
        public static float RateFor(TransitionOffer offer, Aircraft ac)
        {
            if (offer == null) return 0f;
            return Mathf.Round(offer.rateWanPerDay * SizeFactor(ac) * 100f) / 100f;
        }

        /// <summary>
        /// 签约：把报价转成合同，占用指定飞机。startUtcTicks 传未来时刻即为「预租」（预约机位）。
        /// 签约当日收取定金（<see cref="DepositRate"/>），执行期逐日入账，交机结算尾款（<see cref="BalanceRate"/>）。
        /// </summary>
        public static bool Sign(GameState gs, string offerId, string registration, out string message,
            bool refreshInquiry = true, long startUtcTicks = 0)
        {
            message = "";
            Ensure(gs);
            var offer = FindOffer(gs, offerId);
            if (offer == null) { message = "该报价已失效，请重新选择。"; return false; }

            string gate = GateReason(gs, offer.kind);
            if (!string.IsNullOrEmpty(gate)) { message = gate + "。"; return false; }

            var ac = FindAircraft(gs, registration);
            if (ac == null) { message = "找不到该飞机。"; return false; }
            if (!ac.isActive || ac.status == AircraftStatuses.Retired) { message = $"{registration} 不在册。"; return false; }
            if (ac.status == AircraftStatuses.Maintenance) { message = $"{registration} 正在维护，暂不可签约。"; return false; }

            var bound = RouteUsing(gs, registration);
            if (bound != null)
            {
                message = $"{registration} 已排入航线 {bound.origin}—{bound.destination}，请先解除航班计划再转投过渡业务。";
                return false;
            }

            int cap = CapOf(offer.kind);
            int have = CountContracts(gs, offer.kind);
            if (have >= cap)
            {
                message = $"{TransitionKinds.Label(offer.kind)}已达上限 {cap} 架。";
                return false;
            }

            float rate = RateFor(offer, ac);
            int days = TransitionKinds.IsLease(offer.kind) ? LeaseDays : Mathf.Max(1, offer.totalDays);

            long now = gs.SimUtc.Ticks;
            long start = startUtcTicks > 0 ? startUtcTicks : now;
            if (Overlaps(gs, registration, start, days))
            {
                string when = start > now ? DateFormat(gs, start) : "即日";
                message = $"{registration} 在 {when} 起 {days} 天内已有合同占用，请另选机位或改期。";
                return false;
            }

            float total = rate * days;
            SplitTotal(total, out float deposit, out float balance, out _);
            float dailyCash = DailyCashOf(rate);

            var c = new TransitionContract
            {
                id = $"tc_{gs.year}Q{gs.quarter}_{gs.transitionContracts.Count + 1}_{registration}",
                aircraftReg = registration,
                kind = offer.kind,
                client = offer.client,
                rateWanPerDay = rate,
                totalDays = days,
                daysUsed = 0,
                startUtcTicks = start,
                signedUtcTicks = now,
                recallDaysLeft = -1,
                depositWan = deposit,
                balanceWan = balance,
                dailyCashWan = dailyCash,
                depositPaid = deposit > 0.001f
            };
            gs.transitionContracts.Add(c);
            gs.transitionOffers.Remove(offer);

            // 定金：签约当日收讫（预租、在租均即收）
            if (c.depositPaid)
            {
                gs.cash += deposit;
                gs.yearNetProfit += deposit;
                gs.liveQuarterTransitionIncome += deposit;
            }

            string label = TransitionKinds.Label(offer.kind);
            bool reserved = start > now;
            message = reserved
                ? $"已预约：{registration} {label}给{offer.client}，{DateFormat(gs, start)} 起 {days} 天，" +
                  $"总额 {total:F0} 万，定金 {deposit:F1} 万已收，尾款 {balance:F1} 万交机结算。"
                : $"已签约：{registration} {label}给{offer.client}，{rate:F2} 万/天 × {days} 天，总额 {total:F0} 万，" +
                  $"定金 {deposit:F1} 万已收，尾款 {balance:F1} 万交机结算。";
            gs.AddDecisionLog($"过渡业务：{registration} {(reserved ? "预约" : "签下")}{label}（{offer.client}，" +
                              $"{rate:F2} 万/天 × {days} 天，定金 {deposit:F1} 万）。");
            ReportSigned(gs, c);

            if (refreshInquiry)
            {
                BeginCollect();
                TopUpStandingOffers(gs);
                FlushInquiry(gs);
            }
            gs.NotifyChanged();
            return true;
        }

        /// <summary>取消预租（尚未生效）：定金不退，机位释放。</summary>
        public static bool CancelReservation(GameState gs, string contractId, out string message)
        {
            message = "";
            Ensure(gs);
            if (gs?.transitionContracts == null || string.IsNullOrEmpty(contractId))
            { message = "找不到该预约。"; return false; }

            long now = gs.SimUtc.Ticks;
            for (int i = 0; i < gs.transitionContracts.Count; i++)
            {
                var c = gs.transitionContracts[i];
                if (c == null || c.id != contractId) continue;
                if (!c.IsReserved(now)) { message = "该合同已生效，不能取消预约（可走提前召回）。"; return false; }

                gs.transitionContracts.RemoveAt(i);
                message = $"已取消 {c.aircraftReg} 的{TransitionKinds.Label(c.kind)}预约（{c.client}），定金 {c.depositWan:F1} 万不退。";
                gs.AddDecisionLog($"过渡业务：{c.aircraftReg} 取消{c.client}的{TransitionKinds.Label(c.kind)}预约，定金 {c.depositWan:F1} 万不退。");
                ReportCancel(gs, c);
                gs.NotifyChanged();
                return true;
            }
            message = "找不到该预约。";
            return false;
        }

        /// <summary>当地日期文本（yyyy年MM月dd日）。</summary>
        public static string DateFormat(GameState gs, long utcTicks)
        {
            if (utcTicks <= 0) return "—";
            return new DateTime(utcTicks, DateTimeKind.Utc).Add(GameState.LocalUtcOffset).ToString("yyyy年MM月dd日");
        }

        /// <summary>当地短日期文本（M月d日），用于表格 / 按钮等窄位。</summary>
        public static string ShortDate(GameState gs, long utcTicks)
        {
            if (utcTicks <= 0) return "—";
            var d = new DateTime(utcTicks, DateTimeKind.Utc).Add(GameState.LocalUtcOffset);
            return $"{d.Month}月{d.Day}日";
        }

        /// <summary>
        /// 提前召回违约金（万美元）= 剩余租金 × 30%。签约时的 <see cref="Recall"/> 与界面预览共用同一函数，
        /// 免得「按钮上写的钱」与「实际扣的钱」分叉。
        /// </summary>
        public static float RecallFeeWan(TransitionContract c)
        {
            if (c == null) return 0f;
            return Mathf.Round(c.DaysLeft * c.EffectiveDailyCash * RecallPenaltyRate * 10f) / 10f;
        }

        /// <summary>提前召回（仅干租 / 湿租）：付违约金，通知期结束后飞机恢复可用。</summary>
        public static bool Recall(GameState gs, string registration, out string message)
        {
            message = "";
            Ensure(gs);
            var c = ContractFor(gs, registration);
            if (c == null) { message = $"{registration} 没有过渡业务合同。"; return false; }
            if (!TransitionKinds.IsLease(c.kind))
            {
                message = $"{TransitionKinds.Label(c.kind)}任务不能中途召回，请等任务结束。";
                return false;
            }
            if (c.Recalling) { message = $"{registration} 已在交机流程中，还剩 {c.recallDaysLeft} 天。"; return false; }

            c.recallDaysLeft = RecallNoticeDays;
            c.recallFeeWan = RecallFeeWan(c);

            message = c.recallFeeWan > 0.01f
                ? $"已通知{c.client}交机：{RecallNoticeDays} 天后 {registration} 恢复可用，违约金 {c.recallFeeWan:F1} 万。"
                : $"已通知{c.client}交机：{RecallNoticeDays} 天后 {registration} 恢复可用。";
            gs.AddDecisionLog($"过渡业务：{registration} 提前召回，违约金 {c.recallFeeWan:F1} 万。");
            ReportRecall(gs, c);
            gs.NotifyChanged();
            return true;
        }

        #endregion

        #region 每日推进

        /// <summary>
        /// 每跨过一个游戏日调用一次：逐日入账、推进档期、结算到期与召回，并维护报价板。
        /// 与 LiveClockSimulator.ApplyDailyFixed 同节拍，保证收入与成本同日计提。
        /// </summary>
        public static string OnDayTick(GameState gs)
        {
            if (gs == null) return "";
            Ensure(gs);

            if (!gs.transitionIntroSent)
            {
                gs.transitionIntroSent = true;
                SendIntroMail(gs);
            }

            var sb = new StringBuilder();
            float gain = 0f;
            long now = gs.SimUtc.Ticks;

            for (int i = gs.transitionContracts.Count - 1; i >= 0; i--)
            {
                var c = gs.transitionContracts[i];
                if (c == null) { gs.transitionContracts.RemoveAt(i); continue; }

                var ac = FindAircraft(gs, c.aircraftReg);
                if (ac == null || !ac.isActive || ac.status == AircraftStatuses.Retired)
                {
                    ReportClosed(gs, c, voided: true, recalled: false);
                    gs.transitionContracts.RemoveAt(i);
                    sb.Append($"过渡业务：{c.aircraftReg} 已不在册，{TransitionKinds.Label(c.kind)}合同终止。\n");
                    continue;
                }

                // 预租：起始日未到，不执行、不入账
                if (c.IsReserved(now)) continue;

                // 预租 → 在租：起始日到达，通知并开始执行
                if (c.daysUsed == 0 && c.signedUtcTicks + TimeSpan.TicksPerDay <= c.startUtcTicks)
                    ReportReservedActivated(gs, c);

                if (c.daysUsed < c.totalDays)
                {
                    c.daysUsed++;
                    gain += c.EffectiveDailyCash;
                    MaybeMilestone(gs, c);
                }

                bool requested = c.Recalling;
                if (requested && c.recallDaysLeft > 0) c.recallDaysLeft--;

                bool done = c.daysUsed >= c.totalDays;
                bool recalled = !done && requested && c.recallDaysLeft == 0;
                if (!done && !recalled) continue;

                // 尾款：仅在自然履约完毕时结算（提前召回不退未履约部分，改以违约金互抵）
                if (done && !recalled && !c.balancePaid && c.balanceWan > 0.001f)
                {
                    c.balancePaid = true;
                    gain += c.balanceWan;
                    sb.Append($"过渡业务：{c.aircraftReg} 结算尾款 +{c.balanceWan:F1} 万。\n");
                }

                if (recalled && c.recallFeeWan > 0.01f)
                {
                    gs.cash -= c.recallFeeWan;
                    gs.yearNetProfit -= c.recallFeeWan;
                    gs.AddDecisionLog($"过渡业务：{c.aircraftReg} 提前召回，支付违约金 {c.recallFeeWan:F1} 万。");
                    sb.Append($"过渡业务：{c.aircraftReg} 提前召回，支付违约金 {c.recallFeeWan:F1} 万。\n");
                    ReportClosed(gs, c, voided: false, recalled: true);
                }
                else
                {
                    gs.AddDecisionLog($"过渡业务：{c.aircraftReg} {TransitionKinds.Label(c.kind)}合同到期，{c.client} 交还飞机。");
                    sb.Append($"过渡业务：{c.aircraftReg} {TransitionKinds.Label(c.kind)}到期，{c.client} 交还飞机。\n");
                    ReportClosed(gs, c, voided: false, recalled: false);
                }
                gs.transitionContracts.RemoveAt(i);
            }

            if (gain > 0f)
            {
                gs.cash += gain;
                gs.yearNetProfit += gain;
                gs.liveQuarterTransitionIncome += gain;
            }

            BeginCollect();
            ExpireOffers(gs);
            TopUpStandingOffers(gs);
            SpawnOpportunities(gs);
            MaybeUnlockReports(gs);
            FlushInquiry(gs);
            FlushExpired(gs);
            return sb.Length > 0 ? sb.ToString().TrimEnd('\n') : "";
        }

        /// <summary>季末：清空本季报告计数，刷新报价板。</summary>
        public static string OnQuarterTick(GameState gs)
        {
            if (gs == null) return "";
            Ensure(gs);
            float booked = gs.liveQuarterTransitionIncome;
            gs.liveQuarterTransitionIncome = 0f;
            ExpireOffers(gs);
            TopUpStandingOffers(gs);
            return booked > 0.5f ? $"过渡业务本季入账 +{booked:F0} 万。" : "";
        }

        #endregion

        #region 报价板维护

        static void ExpireOffers(GameState gs)
        {
            long now = gs.SimUtc.Ticks;
            for (int i = gs.transitionOffers.Count - 1; i >= 0; i--)
            {
                var o = gs.transitionOffers[i];
                if (o == null) { gs.transitionOffers.RemoveAt(i); continue; }
                if (o.expireUtcTicks > 0 && now >= o.expireUtcTicks)
                {
                    if (_collectFresh) _expiredOffers.Add(o);
                    gs.transitionOffers.RemoveAt(i);
                }
            }
        }

        /// <summary>干租 / 湿租客户常驻：可签架数即为挂单数。幂等。</summary>
        static void TopUpStandingOffers(GameState gs)
        {
            if (gs?.transitionOffers == null) return;

            int dryOpen = MaxDry - CountContracts(gs, TransitionKinds.Dry);
            for (int i = CountOffers(gs, TransitionKinds.Dry); i < dryOpen; i++)
                AddOffer(gs, TransitionKinds.Dry, 0);

            if (CrewReady(gs))
            {
                int wetOpen = MaxWet - CountContracts(gs, TransitionKinds.Wet);
                for (int i = CountOffers(gs, TransitionKinds.Wet); i < wetOpen; i++)
                    AddOffer(gs, TransitionKinds.Wet, 0);
            }
        }

        /// <summary>包机 / 培训机会按概率出现，且有效期短 —— 制造"接单"的稀缺与紧迫。</summary>
        static void SpawnOpportunities(GameState gs)
        {
            if (CrewReady(gs)
                && CountOffers(gs, TransitionKinds.Charter) < MaxCharterOffers
                && UnityEngine.Random.value < CharterSpawnChance)
                AddOffer(gs, TransitionKinds.Charter, CharterOfferDays);

            if (InstructorReady(gs)
                && CountOffers(gs, TransitionKinds.Training) < MaxTrainingOffers
                && CountContracts(gs, TransitionKinds.Training) + CountOffers(gs, TransitionKinds.Training) < MaxTrainingOffers + 1
                && UnityEngine.Random.value < TrainingSpawnChance)
                AddOffer(gs, TransitionKinds.Training, TrainingOfferDays);
        }

        static void AddOffer(GameState gs, string kind, int validDays)
        {
            var offer = new TransitionOffer
            {
                id = $"to_{kind}_{gs.year}Q{gs.quarter}_{gs.transitionOffers.Count}_{UnityEngine.Random.Range(1000, 9999)}",
                kind = kind,
                client = PickClient(kind),
                rateWanPerDay = Quote(kind),
                totalDays = kind switch
                {
                    TransitionKinds.Charter => UnityEngine.Random.Range(2, 6),
                    TransitionKinds.Training => UnityEngine.Random.Range(5, 8),
                    _ => LeaseDays
                },
                expireUtcTicks = validDays > 0
                    ? gs.SimUtc.AddDays(validDays).Ticks
                    : 0
            };
            gs.transitionOffers.Add(offer);
            if (_collectFresh) _freshOffers.Add(offer);
        }

        static float Quote(string kind)
        {
            float b = kind switch
            {
                TransitionKinds.Dry => DryRate,
                TransitionKinds.Wet => WetRate,
                TransitionKinds.Charter => CharterRate,
                TransitionKinds.Training => TrainingRate,
                _ => DryRate
            };
            float lo, hi;
            switch (kind)
            {
                case TransitionKinds.Dry: lo = 0.92f; hi = 1.10f; break;
                case TransitionKinds.Wet: lo = 0.95f; hi = 1.15f; break;
                case TransitionKinds.Charter: lo = 0.85f; hi = 1.35f; break;
                default: lo = 0.90f; hi = 1.15f; break;
            }
            return Mathf.Round(b * UnityEngine.Random.Range(lo, hi) * 100f) / 100f;
        }

        static readonly string[] DryClients =
        {
            "中外货运航空", "丝路快运", "云港航空", "亚洲捷运", "泛亚包机公司",
            "远东航空租赁", "南岛航空", "环宇快线", "西江航空", "金鹏货运"
        };

        static readonly string[] WetClients =
        {
            "蓝洋航空", "极地航空", "海岬航空", "新丝路航空", "星洲联航"
        };

        static readonly string[] CharterClients =
        {
            "中远商务包机", "银河旅运", "环球医疗救援", "天穹会展服务",
            "大湾区招商团", "极地科考队", "汇丰投资考察团", "远洋渔业补给"
        };

        static readonly string[] TrainingClients =
        {
            "东湖航校", "南岭飞行学院", "蓝天模拟机中心", "北方航空职院", "滨江飞行训练中心"
        };

        static string PickClient(string kind)
        {
            var pool = kind switch
            {
                TransitionKinds.Dry => DryClients,
                TransitionKinds.Wet => WetClients,
                TransitionKinds.Charter => CharterClients,
                TransitionKinds.Training => TrainingClients,
                _ => DryClients
            };
            return pool[UnityEngine.Random.Range(0, pool.Length)];
        }

        #endregion

        #region 进度汇报

        static void BeginCollect()
        {
            _collectFresh = true;
            _freshOffers.Clear();
            _expiredOffers.Clear();
        }

        static void EndCollect()
        {
            _collectFresh = false;
            _freshOffers.Clear();
        }

        static void MaybeUnlockReports(GameState gs)
        {
            if (CrewReady(gs) && !gs.transitionCrewMailSent)
            {
                gs.transitionCrewMailSent = true;
                var sb = new StringBuilder();
                sb.Append($"「{CrewPost}」已到岗，湿租合同与包机合同市场同步开放。\n\n");
                AppendOfferLines(sb, gs, TransitionKinds.Wet);
                AppendOfferLines(sb, gs, TransitionKinds.Charter);
                if (CountOffers(gs, TransitionKinds.Wet) + CountOffers(gs, TransitionKinds.Charter) == 0)
                {
                    sb.Append("客户询价将在随后数日内陆续送达，届时另函汇报。");
                    Report(gs, "解锁", "进度汇报：湿租与包机市场已开通", sb.ToString());
                }
                else
                {
                    sb.Append("以上询价请在本函底部直接签约或暂不接单。");
                    QueueInquiry(gs, "请示：湿租与包机市场已开通", sb.ToString(), OfferIdsOf(gs, TransitionKinds.Wet, TransitionKinds.Charter));
                }
            }

            if (InstructorReady(gs) && !gs.transitionInstructorMailSent)
            {
                gs.transitionInstructorMailSent = true;
                var sb = new StringBuilder();
                sb.Append($"「{InstructorPost}」已到岗，可承接培训合同。\n\n");
                AppendOfferLines(sb, gs, TransitionKinds.Training);
                if (CountOffers(gs, TransitionKinds.Training) == 0)
                    sb.Append("航校询价将在随后数日内陆续送达，届时另函汇报。");
                else
                    sb.Append("以上询价请在本函底部直接签约或暂不接单。");
                if (CountOffers(gs, TransitionKinds.Training) == 0)
                    Report(gs, "解锁", "进度汇报：培训合同市场已开通", sb.ToString());
                else
                    QueueInquiry(gs, "请示：培训合同市场已开通", sb.ToString(), OfferIdsOf(gs, TransitionKinds.Training));
            }
        }

        static void FlushInquiry(GameState gs)
        {
            if (_freshOffers.Count == 0)
            {
                EndCollect();
                return;
            }

            var byKind = new Dictionary<string, List<TransitionOffer>>();
            for (int i = 0; i < _freshOffers.Count; i++)
            {
                var o = _freshOffers[i];
                if (o == null) continue;
                if (!byKind.TryGetValue(o.kind, out var list))
                {
                    list = new List<TransitionOffer>();
                    byKind[o.kind] = list;
                }
                list.Add(o);
            }

            var titleBits = new List<string>();
            if (byKind.ContainsKey(TransitionKinds.Wet)) titleBits.Add("湿租");
            if (byKind.ContainsKey(TransitionKinds.Charter)) titleBits.Add("包机");
            if (byKind.ContainsKey(TransitionKinds.Training)) titleBits.Add("培训");
            if (byKind.ContainsKey(TransitionKinds.Dry)) titleBits.Add("干租");
            string title = titleBits.Count == 0
                ? "请示：过渡经营市场询价"
                : "请示：收到" + string.Join("、", titleBits) + "询价";

            var ids = new List<string>();
            var offerList = new List<TransitionOffer>();
            foreach (var kind in TransitionKinds.All)
            {
                if (!byKind.TryGetValue(kind, out var list) || list.Count == 0) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] == null) continue;
                    offerList.Add(list[i]);
                    if (!string.IsNullOrEmpty(list[i].id)) ids.Add(list[i].id);
                }
            }
            QueueInquiry(gs, title, BuildInquiryCore(gs, offerList), ids);
            EndCollect();
        }

        static void FlushExpired(GameState gs)
        {
            if (_expiredOffers.Count == 0) return;
            var sb = new StringBuilder();
            sb.Append("以下短促报价已过有效期，未签约即作废：\n\n");
            for (int i = 0; i < _expiredOffers.Count; i++)
                sb.Append(OfferLine(gs, _expiredOffers[i]));
            sb.Append("如仍需承接同类业务，请待下一批询价。");
            Report(gs, "过期", "进度汇报：包机/培训报价失效", sb.ToString());
            _expiredOffers.Clear();
        }

        static void ReportSigned(GameState gs, TransitionContract c)
        {
            if (c == null) return;
            string label = TransitionKinds.Label(c.kind);
            bool reserved = c.IsReserved(gs.SimUtc.Ticks);
            string subject = reserved
                ? $"签约确认：{label}合同已预约 · {c.aircraftReg}"
                : $"进度汇报：已签署{label}合同 · {c.aircraftReg}";
            var sb = new StringBuilder();
            sb.Append(reserved
                ? $"{c.aircraftReg} 已与{c.client}签署{label}合同，机位预约至 {DateFormat(gs, c.startUtcTicks)} 生效。\n\n"
                : $"{c.aircraftReg} 已按{label}合同交予{c.client}。\n\n");
            sb.Append($"合同类型　{label}合同\n");
            sb.Append($"承租方　　{c.client}\n");
            sb.Append($"执飞飞机　{c.aircraftReg}　{AircraftTitle(gs, c.aircraftReg)}\n");
            sb.Append($"名义费率　{c.rateWanPerDay:F2} 万美元/天\n");
            sb.Append($"合同总额　{c.TotalWan:F0} 万美元（{c.totalDays} 天）\n");
            sb.Append($"付款安排　定金 {c.depositWan:F1} 万（{(c.depositPaid ? "已收讫" : "待收")}）＋ 逐日 " +
                      $"{c.EffectiveDailyCash:F2} 万/天 ＋ 尾款 {c.balanceWan:F1} 万（交机结算）\n");
            sb.Append($"期限　　　{FormatTermRange(gs, c)}");
            sb.Append(reserved ? $"（{DateFormat(gs, c.startUtcTicks)} 起生效）\n\n" : "\n\n");
            if (reserved)
                sb.Append("该机在预约生效前仍计入占用，不得排入定期航线；如需取消预约，请在机队「过渡业务」页操作（定金不退）。\n");
            else
                sb.Append("履约期间该机不得执飞定期航线。干租合同、湿租合同可提前召回，须支付剩余租金 30% 违约金并预留 3 日交机。\n");
            AppendBoardSnapshot(sb, gs);
            Report(gs, "签约", subject, sb.ToString());
        }

        /// <summary>预租合同起始日到达、正式转为在租时通知。</summary>
        static void ReportReservedActivated(GameState gs, TransitionContract c)
        {
            if (c == null) return;
            string label = TransitionKinds.Label(c.kind);
            var sb = new StringBuilder();
            sb.Append($"{c.aircraftReg} 与{c.client}的{label}预约合同今日生效，正式进入执行期。\n\n");
            sb.Append($"承租方　　{c.client}\n");
            sb.Append($"执行期　　{FormatTermRange(gs, c)}（{c.totalDays} 天）\n");
            sb.Append($"逐日入账　{c.EffectiveDailyCash:F2} 万美元/天\n");
            sb.Append($"待收尾款　{c.balanceWan:F1} 万美元（合同结束当日结算）\n\n");
            sb.Append("执行期内该机不得执飞定期航线，届时另函汇报。");
            Report(gs, "生效", $"进度汇报：{label}预约合同开始执行 · {c.aircraftReg}", sb.ToString());
        }

        /// <summary>取消预约通知。</summary>
        static void ReportCancel(GameState gs, TransitionContract c)
        {
            if (c == null) return;
            string label = TransitionKinds.Label(c.kind);
            var sb = new StringBuilder();
            sb.Append($"已取消 {c.aircraftReg} 与{c.client}的{label}预约合同。\n\n");
            sb.Append($"原定起始　{DateFormat(gs, c.startUtcTicks)}\n");
            sb.Append($"定金　　　{c.depositWan:F1} 万美元（按约定不予退还）\n\n");
            sb.Append("该机机位已释放，可重新投入定期航线或另签过渡合同。");
            Report(gs, "取消", $"进度汇报：{label}预约已取消 · {c.aircraftReg}", sb.ToString());
        }

        static void MaybeMilestone(GameState gs, TransitionContract c)
        {
            if (c == null || c.totalDays < 8) return;
            int mark = 0;
            if (c.totalDays >= 60)
            {
                if (c.daysUsed >= 60) mark = 60;
                else if (c.daysUsed >= 30) mark = 30;
            }
            else
            {
                int half = Mathf.Max(2, c.totalDays / 2);
                if (c.daysUsed >= half) mark = half;
            }
            if (mark <= 0 || c.lastMilestone >= mark) return;
            c.lastMilestone = mark;

            string label = TransitionKinds.Label(c.kind);
            float earned = c.rateWanPerDay * c.daysUsed;
            string subject = $"进度汇报：{label}合同履约节点 · {c.aircraftReg}";
            var sb = new StringBuilder();
            sb.Append($"{c.aircraftReg} 与{c.client}的{label}合同已执行 {c.daysUsed}/{c.totalDays} 天。\n\n");
            sb.Append($"已入账　　{earned:F1} 万美元\n");
            sb.Append($"剩余档期　{c.DaysLeft} 天\n");
            sb.Append($"日费率　　{c.rateWanPerDay:F2} 万美元\n");
            if (c.Recalling)
                sb.Append($"召回进度　交机还剩 {c.recallDaysLeft} 天\n");
            sb.Append("\n飞机仍由对方使用，定期航线不可排班。若需提前收回，请在机队「过渡业务」发起召回。");
            Report(gs, "履约", subject, sb.ToString());
        }

        static void ReportRecall(GameState gs, TransitionContract c)
        {
            if (c == null) return;
            string label = TransitionKinds.Label(c.kind);
            var sb = new StringBuilder();
            sb.Append($"已向{c.client}发出{label}合同提前召回通知。\n\n");
            sb.Append($"执飞飞机　{c.aircraftReg}　{AircraftTitle(gs, c.aircraftReg)}\n");
            sb.Append($"已执行　　{c.daysUsed}/{c.totalDays} 天\n");
            sb.Append($"交机期　　{RecallNoticeDays} 天（通知期内租金照付）\n");
            sb.Append($"违约金　　{c.recallFeeWan:F1} 万美元（交机当日扣收）\n\n");
            sb.Append("交机完成后该机即可重新分配定期航线或另签过渡合同，届时另函汇报。");
            Report(gs, "召回", $"进度汇报：已通知提前召回 · {c.aircraftReg}", sb.ToString());
        }

        static void ReportClosed(GameState gs, TransitionContract c, bool voided, bool recalled)
        {
            if (c == null) return;
            string label = TransitionKinds.Label(c.kind);
            string subject = voided
                ? $"进度汇报：{label}合同终止 · {c.aircraftReg}"
                : recalled
                    ? $"进度汇报：召回交机完成 · {c.aircraftReg}"
                    : $"进度汇报：{label}合同到期交机 · {c.aircraftReg}";
            var sb = new StringBuilder();
            if (voided)
                sb.Append($"{c.aircraftReg} 已不在册，与{c.client}的{label}合同随即终止，不追索违约金。\n");
            else if (recalled)
                sb.Append($"{c.client} 已交还 {c.aircraftReg}。召回交机完成，违约金 {c.recallFeeWan:F1} 万美元已入账扣收。\n");
            else
                sb.Append($"{c.client} 已按期交还 {c.aircraftReg}，{label}合同履约完毕。\n");
            sb.Append($"\n已执行　　{c.daysUsed}/{c.totalDays} 天\n");
            sb.Append($"累计到账　{c.ReceivedWan:F1} 万美元（定金 {c.depositWan:F1}、逐日 {c.EffectiveDailyCash * c.daysUsed:F1}" +
                      (c.balancePaid ? $"、尾款 {c.balanceWan:F1}" : "，尾款未结算") + "）\n");
            if (!voided)
                sb.Append("\n该机现已空闲，可投入定期航线或签署新的过渡合同。");
            AppendBoardSnapshot(sb, gs);
            Report(gs, "交机", subject, sb.ToString());
        }

        static void Report(GameState gs, string tag, string subject, string body)
        {
            if (gs == null) return;
            var mail = new InboxMail
            {
                id = $"tp_{tag}_{gs.SimUtc.Ticks}_{UnityEngine.Random.Range(100, 999)}",
                sender = "运营部",
                role = "过渡经营进度",
                glyph = "运",
                subject = subject,
                body = InboxLetter.Frame(gs, "运营部", "过渡经营进度", subject, body,
                    "本封为进度汇报，无需批复。",
                    gs.SimUtc.Add(GameState.LocalUtcOffset).ToString("yyyy年MM月dd日"),
                    needDecision: false),
                status = "approved",
                kind = "report",
                action = ProgressAction,
                tag = tag,
                deadlineQuarters = 1
            };
            InboxManager.Schedule(gs, mail, InboxCadence.ProgressReady(gs.SimUtc));
        }

        static void QueueInquiry(GameState gs, string subject, string body, List<string> offerIds)
        {
            if (gs == null) return;
            if (offerIds == null || offerIds.Count == 0)
            {
                Report(gs, "询价", subject, body);
                return;
            }
            var mail = new InboxMail
            {
                id = $"ti_{gs.SimUtc.Ticks}_{UnityEngine.Random.Range(100, 999)}",
                sender = "运营部",
                role = "过渡经营请示",
                glyph = "运",
                subject = subject,
                body = InboxLetter.Frame(gs, "运营部", "过渡经营请示", subject, body,
                    "请在本函底部选择「签约闲置机」或「本次不接」。签约将自动分配当前闲置飞机。",
                    gs.SimUtc.Add(GameState.LocalUtcOffset).ToString("yyyy年MM月dd日"),
                    needDecision: true),
                status = "pending",
                kind = "alert",
                action = InquiryAction,
                tag = "询价",
                payloadCsv = string.Join(",", offerIds.ToArray()),
                deadlineQuarters = 1
            };
            InboxManager.Schedule(gs, mail, InboxCadence.ProgressReady(gs.SimUtc));
        }

        static List<string> OfferIdsOf(GameState gs, params string[] kinds)
        {
            var ids = new List<string>();
            if (gs?.transitionOffers == null || kinds == null) return ids;
            for (int i = 0; i < gs.transitionOffers.Count; i++)
            {
                var o = gs.transitionOffers[i];
                if (o == null || string.IsNullOrEmpty(o.id)) continue;
                for (int k = 0; k < kinds.Length; k++)
                {
                    if (o.kind == kinds[k])
                    {
                        ids.Add(o.id);
                        break;
                    }
                }
            }
            return ids;
        }

        static List<string> ParsePayload(InboxMail mail)
        {
            var ids = new List<string>();
            if (mail == null || string.IsNullOrEmpty(mail.payloadCsv)) return ids;
            var parts = mail.payloadCsv.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i] != null ? parts[i].Trim() : "";
                if (p.Length > 0 && !ids.Contains(p)) ids.Add(p);
            }
            return ids;
        }

        static void WritePayload(InboxMail mail, List<string> ids)
        {
            if (mail == null) return;
            mail.payloadCsv = ids == null || ids.Count == 0 ? "" : string.Join(",", ids.ToArray());
        }

        public static List<TransitionOffer> OffersOnMail(GameState gs, InboxMail mail)
        {
            var list = new List<TransitionOffer>();
            var ids = ParsePayload(mail);
            for (int i = 0; i < ids.Count; i++)
            {
                var o = FindOffer(gs, ids[i]);
                if (o != null) list.Add(o);
            }
            return list;
        }

        /// <summary>过期作废说明（追加到询价函正文末尾，供 UI 识别「已作废」态）。</summary>
        public const string ExpiredNotice = "（报价已过有效期，本函自动作废，无需批复。）";

        /// <summary>
        /// 询价函是否已过 CEO 决策期限：仍待批复，但其挂单报价已全部过有效期（包机 3 天 / 培训 5 天）或被移除。
        /// 这类邮件不应再占「待处理」，也不该再显示批复按钮。
        /// </summary>
        public static bool IsInquiryExpired(GameState gs, InboxMail mail)
        {
            if (gs == null || mail == null) return false;
            if (mail.action != InquiryAction) return false;
            if (!InboxManager.IsOpen(mail)) return false;
            var ids = ParsePayload(mail);
            if (ids.Count == 0) return true;
            for (int i = 0; i < ids.Count; i++)
                if (FindOffer(gs, ids[i]) != null) return false;
            return true;
        }

        /// <summary>
        /// 过期询价函自动作废：status → rejected（取消「待处理」标签与底部决策按钮），正文追加作废说明。
        /// 在 Ensure / 每日推进中调用，保证收件箱打开时即已收口。
        /// </summary>
        public static void ExpireStaleInquiryMails(GameState gs)
        {
            if (gs?.inbox == null) return;
            bool any = false;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (!IsInquiryExpired(gs, m)) continue;
                m.status = "rejected";
                gs.AddDecisionLog($"收件箱：询价函「{m.subject}」报价已过有效期，自动作废，无需批复。");
                if (!string.IsNullOrEmpty(m.body) && m.body.IndexOf(ExpiredNotice, System.StringComparison.Ordinal) < 0)
                    m.body += "\n\n" + ExpiredNotice;
                any = true;
            }
            if (any) gs.NotifyChanged();
        }

        /// <summary>本函逐份签约：自动分配一架可承接的飞机（闲置优先，否则预约下一空闲窗口）。</summary>
        public static bool SignOneFromInbox(GameState gs, InboxMail mail, string offerId, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "询价函不可用。"; return false; }
            var offer = FindOffer(gs, offerId);
            if (offer == null) { message = "该报价已失效。"; return false; }
            var pick = PickBooking(gs, OfferDays(offer));
            if (pick == null)
            {
                message = "没有可承接的飞机（均在航线或没有空闲窗口）。";
                return false;
            }
            if (!Sign(gs, offerId, pick.Value.aircraft.registration, out message, false, pick.Value.startTicks))
                return false;
            var left = ParsePayload(mail);
            left.RemoveAll(id => id == offerId || FindOffer(gs, id) == null);
            WritePayload(mail, left);
            if (left.Count == 0)
            {
                mail.status = "approved";
                BeginCollect();
                TopUpStandingOffers(gs);
                FlushInquiry(gs);
            }
            gs.NotifyChanged();
            return true;
        }

        /// <summary>挑选最合适的机位：优先即日可用，否则最早可预约窗口。</summary>
        static TransitionBusinessLogic.BookingOption? PickBooking(GameState gs, int days)
        {
            var opts = BookingOptions(gs, days);
            if (opts.Count == 0) return null;
            for (int i = 0; i < opts.Count; i++)
                if (!opts[i].future) return opts[i];
            return opts[0];
        }

        /// <summary>挑选机位，优先未在本批使用过的飞机；没有再退回复用。</summary>
        static TransitionBusinessLogic.BookingOption? PickBookingAvoiding(GameState gs, int days, HashSet<string> used)
        {
            var opts = BookingOptions(gs, days);
            if (opts.Count == 0) return null;
            for (int i = 0; i < opts.Count; i++)
                if (!opts[i].future && (used == null || !used.Contains(opts[i].aircraft.registration))) return opts[i];
            for (int i = 0; i < opts.Count; i++)
                if (used == null || !used.Contains(opts[i].aircraft.registration)) return opts[i];
            return opts[0];
        }

        /// <summary>本函全部用闲置机签约。</summary>
        public static bool SignAllIdleFromInbox(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "询价函不可用。"; return false; }
            var ids = ParsePayload(mail);
            if (ids.Count == 0)
            {
                mail.status = "approved";
                message = "报价已失效或已处理。";
                gs.NotifyChanged();
                return true;
            }
            int n = 0;
            var notes = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ids.Count; i++)
            {
                var offer = FindOffer(gs, ids[i]);
                if (offer == null) continue;
                var pick = PickBookingAvoiding(gs, OfferDays(offer), used);
                if (pick == null)
                {
                    notes.Add("其余询价因无机可签未处理。");
                    break;
                }
                if (Sign(gs, ids[i], pick.Value.aircraft.registration, out string one, false, pick.Value.startTicks))
                {
                    n++;
                    notes.Add(one);
                    used.Add(pick.Value.aircraft.registration);
                }
                else notes.Add(one);
            }
            var left = ParsePayload(mail);
            left.RemoveAll(id => FindOffer(gs, id) == null);
            WritePayload(mail, left);
            if (left.Count == 0)
            {
                mail.status = "approved";
                BeginCollect();
                TopUpStandingOffers(gs);
                FlushInquiry(gs);
            }
            message = n > 0
                ? string.Join(" ", notes.ToArray())
                : (notes.Count > 0 ? notes[0] : "未能签约。");
            gs.NotifyChanged();
            return n > 0 || left.Count == 0;
        }

        /// <summary>本次不接：关闭请示，报价仍保留至有效期。</summary>
        public static bool DeclineInquiry(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (mail == null) { message = "询价函不可用。"; return false; }
            mail.status = "rejected";
            message = "已选择本次不接。报价仍保留至有效期，过期作废。";
            gs?.NotifyChanged();
            return true;
        }

        static void AppendOfferLines(StringBuilder sb, GameState gs, string kind)
        {
            if (gs?.transitionOffers == null) return;
            int n = 0;
            for (int i = 0; i < gs.transitionOffers.Count; i++)
            {
                var o = gs.transitionOffers[i];
                if (o == null || o.kind != kind) continue;
                if (n == 0) sb.Append(TransitionKinds.Label(kind)).Append("合同询价：\n");
                sb.Append(OfferLine(gs, o));
                n++;
            }
        }

        static string OfferLine(GameState gs, TransitionOffer o)
        {
            if (o == null) return "";
            int days = OfferDays(o);
            string valid = o.expireUtcTicks > 0
                ? $"报价有效期 {o.DaysLeftUtc(gs.SimUtc.Ticks)} 天"
                : "长期有效";
            float total = o.rateWanPerDay * days;
            SplitTotal(total, out float deposit, out float balance, out _);
            return $"　·　{o.client}　拟签{TransitionKinds.Label(o.kind)}合同　{o.rateWanPerDay:F2} 万/天　期限 " +
                   $"{FormatTermRange(gs, days, 0, 0)}　总额 {total:F0} 万（定金 {deposit:F1} / 尾款 {balance:F1}）　{valid}\n";
        }

        static int OfferDays(TransitionOffer o)
        {
            if (o == null) return 1;
            return TransitionKinds.IsLease(o.kind) ? LeaseDays : Mathf.Max(1, o.totalDays);
        }

        /// <summary>合同/报价期限：从当地日起算 totalDays 天（含首尾）。</summary>
        public static string FormatTermRange(GameState gs, TransitionContract c)
        {
            if (c == null) return "—";
            return FormatTermRange(gs, c.totalDays, c.daysUsed, c.startUtcTicks);
        }

        public static string FormatTermRange(GameState gs, int totalDays, int daysUsed, long startUtcTicks)
        {
            DateTime start;
            TimeSpan offset = GameState.LocalUtcOffset;
            if (startUtcTicks > 0)
                start = new DateTime(startUtcTicks, DateTimeKind.Utc).Add(offset).Date;
            else
            {
                DateTime today = (gs != null ? gs.SimUtc : DateTime.UtcNow).Add(offset).Date;
                start = today.AddDays(-Math.Max(0, daysUsed));
            }
            int days = Math.Max(1, totalDays);
            DateTime end = start.AddDays(days - 1);
            return $"{start:yyyy年MM月dd日}—{end:yyyy年MM月dd日}";
        }

        static void RefreshInquiryMailBodies(GameState gs)
        {
            if (gs?.inbox == null) return;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != InquiryAction || !InboxManager.IsOpen(m)) continue;
                var offers = OffersOnMail(gs, m);
                if (offers.Count == 0) continue;
                string core = BuildInquiryCore(gs, offers);
                m.body = InboxLetter.Frame(gs, "运营部", "过渡经营请示", m.subject, core,
                    "请在本函底部选择「签约闲置机」或「本次不接」。签约将自动分配当前闲置飞机。",
                    gs.SimUtc.Add(GameState.LocalUtcOffset).ToString("yyyy年MM月dd日"),
                    needDecision: true);
            }
        }

        static string BuildInquiryCore(GameState gs, List<TransitionOffer> offers)
        {
            var byKind = new Dictionary<string, List<TransitionOffer>>();
            if (offers != null)
            {
                for (int i = 0; i < offers.Count; i++)
                {
                    var o = offers[i];
                    if (o == null) continue;
                    if (!byKind.TryGetValue(o.kind, out var list))
                    {
                        list = new List<TransitionOffer>();
                        byKind[o.kind] = list;
                    }
                    list.Add(o);
                }
            }
            var sb = new StringBuilder();
            sb.Append("运营部刚接到客户询价，现将意向整理如下。\n");
            foreach (var kind in TransitionKinds.All)
            {
                if (!byKind.TryGetValue(kind, out var list) || list.Count == 0) continue;
                sb.Append("\n");
                sb.Append(TransitionKinds.Label(kind)).Append("合同询价 ").Append(list.Count).Append(" 份：\n");
                for (int i = 0; i < list.Count; i++)
                    sb.Append(OfferLine(gs, list[i]));
            }
            AppendBoardSnapshot(sb, gs);
            var idle = AvailableAircraft(gs);
            sb.Append("\n当前可签闲置机　");
            if (idle.Count == 0) sb.Append("无（需先从航线抽机或等待交机）");
            else
            {
                for (int i = 0; i < idle.Count; i++)
                {
                    if (i > 0) sb.Append('、');
                    sb.Append(idle[i].registration);
                }
            }
            sb.Append("\n\n付款安排　签约先收定金 20%，执行期逐日入账 60%，交机结算尾款 20%。");
            sb.Append("\n预约说明　如机位在目标日期已被占用，可把合同预约到未来空闲窗口（预租），当日先收定金、到期自动转入在租。");
            return sb.ToString();
        }

        static void AppendBoardSnapshot(StringBuilder sb, GameState gs)
        {
            if (gs?.transitionContracts == null || gs.transitionContracts.Count == 0) return;
            var active = ActiveContracts(gs);
            var reserved = ReservedContracts(gs);
            if (active.Count > 0)
            {
                sb.Append("\n\n在租一览：\n");
                for (int i = 0; i < active.Count; i++)
                {
                    var c = active[i];
                    sb.Append($"　·　{c.aircraftReg}　{TransitionKinds.Label(c.kind)}合同 · {c.client}　期限 {FormatTermRange(gs, c)}　{c.rateWanPerDay:F2} 万/天\n");
                }
            }
            if (reserved.Count > 0)
            {
                sb.Append("\n预租一览：\n");
                for (int i = 0; i < reserved.Count; i++)
                {
                    var c = reserved[i];
                    sb.Append($"　·　{c.aircraftReg}　{TransitionKinds.Label(c.kind)}合同 · {c.client}　{DateFormat(gs, c.startUtcTicks)} 起 {c.totalDays} 天　定金 {c.depositWan:F1} 万已收\n");
                }
            }
        }

        static string AircraftTitle(GameState gs, string reg)
        {
            var ac = FindAircraft(gs, reg);
            if (ac == null) return "";
            var cat = AircraftCatalog.Find(ac.type);
            return cat != null ? cat.displayName : (ac.type ?? "");
        }

        #endregion

        #region 开局引导

        static InboxMail FindIntroMail(GameState gs)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m != null && m.id == "transition_intro") return m;
            }
            return null;
        }

        /// <summary>旧档口语题名改写为正式请示名，并补齐四类合同全称。</summary>
        static void RefreshIntroMail(GameState gs)
        {
            var m = FindIntroMail(gs);
            if (m == null) return;
            if (m.subject == IntroSubject && !string.IsNullOrEmpty(m.body) && m.body.Contains("干租合同"))
                return;
            FillIntroMail(gs, m);
        }

        static void FillIntroMail(GameState gs, InboxMail mail)
        {
            var free = AvailableAircraft(gs);
            var sb = new StringBuilder();
            for (int i = 0; i < free.Count; i++)
            {
                if (i > 0) sb.Append('、');
                sb.Append(free[i].registration);
            }
            string regs = sb.Length > 0 ? sb.ToString() : "暂无空闲飞机";

            mail.id = "transition_intro";
            mail.sender = "运营部";
            mail.role = "过渡经营请示";
            mail.glyph = "运";
            mail.subject = IntroSubject;
            mail.body = InboxLetter.Frame(gs, "运营部", "过渡经营请示",
                IntroSubject,
                $"航线经营许可尚在局方审查，飞行机组亦在招聘。三架飞机停场期间仍需计提维护与固定成本，空窗期内不宜闲置。\n\n" +
                $"建议将闲置运力转入过渡经营，待定期航线许可获批后再收回执飞：\n\n" +
                $"干租合同　日租金基准 {DryRate:F2} 万美元。仅出租飞机，机组与维护由承租方自备；开局即可签署，同时最多 3 架。\n" +
                $"湿租合同　日租金基准 {WetRate:F2} 万美元。飞机连同机组一并出租，租金约干租两倍；须「{CrewPost}」到位，同时仅 1 份。\n" +
                $"包机合同　日费率基准 {CharterRate:F2} 万美元。按趟接单、单价最高；报价有效期 3 天，逾期作废。\n" +
                $"培训合同　日费率基准 {TrainingRate:F2} 万美元。为航校提供带飞与模拟机训练；须「{InstructorPost}」到位组织教员。\n\n" +
                $"付款安排　签约当日先收定金 {DepositRate * 100f:F0}%，执行期逐日入账 {DailyShareRate * 100f:F0}%，交机结算尾款 {BalanceRate * 100f:F0}%。\n" +
                $"预约机位　可把合同起始日预约到未来日期（预租）：当日先收定金锁定客户，到期自动转入在租执行；如另行取消，定金不退。\n\n" +
                $"当前可签约飞机　{regs}\n\n" +
                $"条款提请注意：干租合同、湿租合同履行期间，所涉飞机不得执飞定期航线。" +
                $"提前召回须支付剩余租金 30% 违约金，并预留 {RecallNoticeDays} 个工作日交机。" +
                $"定期航线许可获批后，请权衡「支付违约金立即收回」与「待合同自然到期」。",
                "以上方案请审阅批示。",
                gs.SimUtc.Add(GameState.LocalUtcOffset).ToString("yyyy年MM月dd日"));
            mail.status = "approved";
            mail.kind = "report";
            mail.action = "transition_guide";
            mail.tag = "请示";
            mail.deadlineQuarters = 3;
        }

        static void SendIntroMail(GameState gs)
        {
            if (FindIntroMail(gs) != null)
            {
                RefreshIntroMail(gs);
                return;
            }

            var mail = new InboxMail();
            FillIntroMail(gs, mail);
            InboxManager.Add(gs, mail);
            gs.AddDecisionLog($"运营部呈报《{IntroSubject}》：建议先以干租合同消化闲置运力。");
        }

        #endregion

        #region UI 辅助

        /// <summary>解锁进度清单（供「过渡业务」页展示）。</summary>
        public static string UnlockChecklist(GameState gs)
        {
            int crew = HeadcountOf(gs, "OPS", CrewPost);
            int inst = HeadcountOf(gs, "OPS", InstructorPost);
            var sb = new StringBuilder();
            sb.Append(Mark(crew >= 1)).Append($" {CrewPost} {Mathf.Min(crew, 1)}/1　→ 解锁 湿租 · 包机\n");
            sb.Append(Mark(inst >= 1)).Append($" {InstructorPost} {Mathf.Min(inst, 1)}/1　→ 解锁 培训\n");
            sb.Append("（在「人员 → 人力资源部」审批入职，约需 30 天）");
            return sb.ToString();
        }

        static string Mark(bool ok) => ok ? "●" : "○";

        #endregion

        static PersonnelData SafeHead(DepartmentManager mgr, string code)
        {
            if (mgr == null) return null;
            try { return mgr.GetHead(code); } catch { return null; }
        }

        static PersonnelData SafeDeputy(DepartmentManager mgr, string code)
        {
            if (mgr == null) return null;
            try { return mgr.GetDeputy(code); } catch { return null; }
        }
    }
}
