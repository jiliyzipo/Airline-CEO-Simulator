using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.AI;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// 收件箱管理（GDB 白板 n7「完整邮件模型」）：邮件增删改、7 日（季度）截止判定、到期阻断。

    /// 时间单位按游戏季度计：待办邮件自收件季度起，超过 deadlineQuarters 个季度仍未处理即到期。

    /// </summary>
    public static class InboxManager
    {
        /// <summary>待办邮件允许搁置的季度数（默认 2 个季度 ≈ 半年）。</summary>
        public const int DefaultDeadlineQuarters = 2;
        public const string FuelHedgeAction = "fuel_hedge";
        public const string FinanceProgressAction = "finance_progress";
        public const string AnnualBudgetAction = "annual_budget";
        public const string CarbonBuyAction = "carbon_buy";
        public const string FinanceLoanAction = "finance_loan";
        public const string FleetBuyAction = "fleet_buy";
        public const string HrRecruitAction = "hr_recruit";
        public const string CfoSender = "财务总监";
        /// <summary>运力调整呈报（CEO 在单航线页下达指令，运营部执行后呈报待批复）。</summary>
        public const string CapacityDirectiveAction = CapacityDirectiveLogic.Action;
        /// <summary>P3：按班次批量申请时刻槽（批准后下季交割）。</summary>
        public const string ApplySlotAction = "apply_slot";
        /// <summary>
        /// **时刻变更（改时）申请**：本端缺槽，而本线在该站**已有一条槽**、只是时刻滑点
        /// （超 15 分容差、在 4 小时改时窗口内）。★2026-10-16 第四十一次修订。
        ///
        /// 现实依据：WASG 9.1.3 要求航司「request the necessary **slot adjustments** in a timely manner」、
        /// 8.10.1「slots are not route, aircraft, or flight number specific … may be changed」——
        /// 持槽后时刻滑点是**报协调人变更为准**，不是把缺的那条槽当新槽再申请一遍。
        /// 协调人按**目标系列余量**批驳（余量 0 就是驳），15 分容差内本就算同一条（无需变更）。
        /// </summary>
        public const string ChangeSlotAction = "change_slot";
        /// <summary>
        /// 加班**时刻备选**呈报（运营部按整张窗口网格提名 2–4 条往返时刻对 → CEO 点定一条）。
        /// ★2026-10-15：与「执飞飞机提名」同源的两段式 —— 先选定时刻，再去开申请单。
        /// </summary>
        public const string SlotSelectAction = SlotCandidateLogic.Action;
        /// <summary>
        /// 排班咨询答复件（汇报件：action 留空，无需批复）。UI 靠这个 role 认出它，
        /// 好把「详见」按钮指回本线，而不是落到「航线网络 / 董事会」那对兜底按钮上。
        /// </summary>
        public const string OpsQueryRole = "排班咨询答复";

        /// <summary>未读数变化（底栏角标刷新）。</summary>
        public static event System.Action Changed;

        /// <summary>供外部系统（如决策会议回写邮件）触发收件箱刷新。</summary>
        public static void NotifyChanged() => Changed?.Invoke();

        public static int Turn(int year, int quarter) => year * 4 + (quarter - 1);

        /// <summary>
        /// 呈报**被取代而作废**的状态（不写 <c>approved</c> —— 那是「CEO 批过」的意思）。
        ///
        /// ★2026-10-16 第四十二次修订：CEO 下新调令时，同一航线还挂着的旧呈报会被自动作废
        /// （新方案另有一封），旧版把它标成 `approved`，收件箱徽章于是一律显示「已批准」——
        /// 而它正文里写着「本呈报自动作废」。与「运力调整已生效」的回执同源：**没生效的事不要说生效**。
        /// 本状态不属于待办（<see cref="IsOpen"/> 只认 pending/deferred），也不占决策按钮。
        /// </summary>
        public const string SupersededStatus = "superseded";

        /// <summary>邮件是否「未处理」（待办状态，含暂缓）。</summary>
        public static bool IsOpen(InboxMail m)
            => m != null && (m.status == "pending" || m.status == "deferred");

        /// <summary>
        /// 是否需要 CEO 决策（决定收件箱底部是否显示 批准/暂缓/拒绝 按钮）。
        /// 回执、进度、通报类邮件只需阅读，不显示批复按钮。
        /// </summary>
        public static bool RequiresDecision(InboxMail m)
        {
            if (m == null) return false;
            if (string.IsNullOrEmpty(m.action)) return false;      // 纯通报（如执行回执）
            if (m.action == "permit_progress" || m.action == "news"
                || m.action == TransitionBusinessLogic.ProgressAction
                || m.action == FinanceProgressAction) return false;
            if (!string.IsNullOrEmpty(MeetingOutcomeOf(m))) return false; // 已上会裁决：回收件箱不再显示决策按钮
            return IsOpen(m);                                       // 已批/已拒/已过期的不再需要决策
        }

        /// <summary>该函在 CEO 决策会议上的最近一次裁决（approved/deferred/rejected）；空串=尚未上会。</summary>
        public static string MeetingOutcomeOf(InboxMail m)
            => m == null || string.IsNullOrEmpty(m.meetingOutcome) ? "" : m.meetingOutcome;

        /// <summary>剩余可搁置季度数（可为负 = 已到期）。</summary>
        public static int RemainingQuarters(InboxMail m, int year, int quarter)
        {
            if (m == null) return 0;
            int due = Turn(m.receivedYear, m.receivedQuarter) + m.deadlineQuarters;
            return due - Turn(year, quarter);
        }

        /// <summary>是否已到期（超过截止仍待处理）。会议暂缓（补条件中）的邮件不阻断时间推进。</summary>
        public static bool IsDue(InboxMail m, int year, int quarter)
            => RequiresDecision(m) && RemainingQuarters(m, year, quarter) < 0;

        public static void Add(GameState gs, InboxMail mail)
        {
            if (gs == null || mail == null) return;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            StampReceived(gs, mail);
            gs.inbox.Insert(0, mail);
            Changed?.Invoke();
        }

        /// <summary>按指定公历时刻投递；到点前不出现在收件箱。</summary>
        public static void Schedule(GameState gs, InboxMail mail, DateTime whenUtc)
        {
            if (gs == null || mail == null) return;
            if (gs.pendingInbox == null) gs.pendingInbox = new List<PendingInboxMail>();
            if (!string.IsNullOrEmpty(mail.id))
            {
                for (int i = 0; i < gs.pendingInbox.Count; i++)
                {
                    var p = gs.pendingInbox[i];
                    if (p?.mail == null || p.mail.id != mail.id) continue;
                    // 同一封重排：必须一起更新送达时刻。只换内容不换点，会沿用上一次的
                    // deliverUtcTicks —— 那条通常已经到期，下一帧就投递，等于「延迟」没生效
                    // （运力呈报重复下达时就会撞上：新调令的呈报会立刻冒出来）。
                    p.deliverUtcTicks = whenUtc.Ticks;
                    p.mail = mail;
                    return;
                }
            }
            if (HasInboxId(gs, mail.id)) return;
            gs.pendingInbox.Add(new PendingInboxMail
            {
                deliverUtcTicks = whenUtc.Ticks,
                mail = mail
            });
        }

        /// <summary>
        /// 现在入箱，或按 <paramref name="delayHours"/> 延后（0 / 负数 = 立即）。
        /// 2026-10-19：CEO 下达运力调令后，运营部呈报与战略发展部的时刻槽单都不该在同一秒冒出来，
        /// 统一走这里，免得每处各写一遍 Schedule/Add 的分支。
        /// </summary>
        static void Enqueue(GameState gs, InboxMail mail, int delayHours)
        {
            if (gs == null || mail == null) return;
            if (delayHours > 0) Schedule(gs, mail, InboxCadence.AfterHours(gs.SimUtc, delayHours));
            else Add(gs, mail);
        }

        /// <summary>投递已到期的待发邮件（由 TimeManager 按游戏时钟调用）。</summary>
        public static void DeliverDue(GameState gs, DateTime nowUtc)
        {
            if (gs?.pendingInbox == null || gs.pendingInbox.Count == 0) return;
            bool any = false;
            for (int i = gs.pendingInbox.Count - 1; i >= 0; i--)
            {
                var p = gs.pendingInbox[i];
                if (p?.mail == null)
                {
                    gs.pendingInbox.RemoveAt(i);
                    continue;
                }
                if (p.deliverUtcTicks > nowUtc.Ticks) continue;
                gs.pendingInbox.RemoveAt(i);
                Add(gs, p.mail);
                any = true;
            }
            if (any) gs.NotifyChanged();
        }

        /// <summary>记下游戏公历瞬间，供列表按日分组、显示时分。</summary>
        public static void StampReceived(GameState gs, InboxMail mail, int minuteNudge = 0)
        {
            if (mail == null) return;
            bool wasEmpty = mail.receivedUtcTicks <= 0;
            if (wasEmpty && gs != null)
            {
                var utc = gs.SimUtc;
                int fromY = mail.receivedYear > 0 ? mail.receivedYear : gs.year;
                int fromQ = mail.receivedQuarter > 0 ? mail.receivedQuarter : gs.quarter;
                int qDelta = Turn(gs.year, gs.quarter) - Turn(fromY, fromQ);
                if (qDelta > 0)
                    utc = utc.AddDays(-90 * qDelta);
                if (minuteNudge > 0)
                    utc = utc.AddMinutes(-minuteNudge);
                mail.receivedUtcTicks = utc.Ticks;
            }
            if (mail.receivedUtcTicks <= 0)
                mail.receivedUtcTicks = DateTime.UtcNow.Ticks;
            mail.time = InboxTime.Clock(mail);
            if (gs != null)
            {
                if (mail.receivedYear <= 0) mail.receivedYear = gs.year;
                if (mail.receivedQuarter <= 0) mail.receivedQuarter = gs.quarter;
            }
        }

        /// <summary>点开邮件详情后标记已读（重复点开不重复计数）。</summary>
        public static void MarkRead(GameState gs, string id)
        {
            if (gs == null || gs.inbox == null || string.IsNullOrEmpty(id)) return;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.id != id || m.read) continue;
                m.read = true;
                Changed?.Invoke();
                return;
            }
        }

        /// <summary>标记邮件状态（批准/暂缓/拒绝），并写决策日志。</summary>
        public static void Mark(GameState gs, string id, string status)
        {
            if (gs == null || gs.inbox == null) return;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                if (gs.inbox[i].id == id)
                {
                    gs.inbox[i].status = status;
                    gs.AddDecisionLog($"收件箱{StatusLabel(status)}：{gs.inbox[i].subject}");
                    Changed?.Invoke();
                    return;
                }
            }
        }

        public static void Normalize(InboxMail m)
        {
            if (m == null) return;
            if (m.id == "m_route" && string.IsNullOrEmpty(m.action))
            {
                m.kind = "report";
                m.action = "open_route";
                m.origin = "PEK";
                m.dest = "SHA";
                if (!string.IsNullOrEmpty(m.body) && m.body.IndexOf("关键词", StringComparison.Ordinal) < 0)
                    m.body = "市场部建议立即开通 PEK–SHA。\n\n建议：北京首都—上海虹桥。启动资金 300 万（开航时扣）。请点关键词查阅航线网络 或 RPM，再批准。批准后转入局方准入办理，取证后开航。";
            }
            else if (m.id == "m_tutorial_szx_pek" && string.IsNullOrEmpty(m.action))
            {
                m.kind = "report";
                m.action = "open_route";
                m.origin = "SZX";
                m.dest = "PEK";
            }
            else if (m.id == "m_board" && string.IsNullOrEmpty(m.kind))
                m.kind = "report";

            // 告知类邮件（无 action / 回执 / 快讯）不应处于待办：否则会误显示批复按钮、并误判「到期待办」阻断时间推进。
            if (string.IsNullOrEmpty(m.action) || m.action == "permit_progress" || m.action == "news"
                || m.action == TransitionBusinessLogic.ProgressAction
                || m.action == FinanceProgressAction)
            {
                if (m.status == "pending" || m.status == "deferred") m.status = "approved";
            }
        }

        /// <summary>
        /// 清扫旧档中的告知类待办邮件（回执/进度/通报等不需决策的邮件）：
        /// 这类邮件只需阅读，不该阻断时间推进，也不该显示批准/拒绝按钮。每季推进前调用。
        /// </summary>
        public static void SweepReadOnlyMails(GameState gs)
        {
            if (gs?.inbox == null) return;
            TransitionBusinessLogic.UpgradeLegacyInquiryMails(gs);
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || !IsOpen(m)) continue;
                bool readOnly = string.IsNullOrEmpty(m.action)
                    || m.action == "permit_progress" || m.action == "news"
                    || m.action == TransitionBusinessLogic.ProgressAction
                    || m.action == FinanceProgressAction;
                if (readOnly) m.status = "approved";
            }
        }

        public static string KindLabel(string kind) => kind switch
        {
            "regulatory" => "监管",
            "media" => "媒体",
            "official" => "来文",
            "alert" => "警报",
            _ => "汇报"
        };

        /// <summary>批准开航线：本季只落决策，下季度才真正开通（延迟后果）。</summary>
        public static bool Approve(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null)
            {
                message = "邮件无效。";
                return false;
            }

            Normalize(mail);
            if (mail.action == "open_route")
                return AcceptRoutePermit(gs, mail, out message);

            if (mail.action == "permit_objection")
                return ApprovePermitObjection(gs, mail, out message);

            if (mail.action == "permit_retry")
                return ApprovePermitRetry(gs, mail, out message);

            if (mail.action == "codeshare" || mail.action == "alliance_join"
                || mail.action == "buy_slot" || mail.action == ApplySlotAction || mail.action == ChangeSlotAction
                || mail.action == "apply_right"
                || mail.action == "investment")
            {
                NextQuarter(gs.year, gs.quarter, out int ey, out int eq);
                mail.effectPending = true;
                mail.effectYear = ey;
                mail.effectQuarter = eq;
                Mark(gs, mail.id, "approved");
                if (mail.action == "codeshare")
                    message = $"已批准与{(string.IsNullOrEmpty(mail.dest) ? "合作航司" : mail.dest)}的代码共享。协议将于第{ey}年 Q{eq} 生效。";
                else if (mail.action == "alliance_join")
                    message = $"已批准加入星空联盟（观察员）。入盟手续将于第{ey}年 Q{eq} 办结。";
                else if (mail.action == ChangeSlotAction)
                {
                    string aptC = string.IsNullOrEmpty(mail.origin) ? (gs.hubIata ?? "HUB") : mail.origin;
                    message = $"已批准向协调人提 {aptC} 的时刻变更（{mail.slotFromClock} → {SlotMailClock(mail)}）。"
                              + $"协调人按目标系列余量批驳，第{ey}年 Q{eq} 出结果；批准后原槽即改为该时刻。";
                }
                else if (mail.action == "buy_slot" || mail.action == ApplySlotAction)
                {
                    string apt = string.IsNullOrEmpty(mail.origin) ? (gs.hubIata ?? "HUB") : mail.origin;
                    string clk = SlotMailClock(mail);
                    int qty = SlotQtyOf(mail);
                    bool free = mail.action == ApplySlotAction;
                    string chan = free ? "协调池分配（不收费）" : "二级市场购入";
                    message = qty > 1
                        ? $"已批准通过{chan}取得 {apt} {clk} 时刻槽 {qty} 条。将于第{ey}年 Q{eq} 交割。"
                        : $"已批准通过{chan}取得 {apt} {clk} 时刻槽。将于第{ey}年 Q{eq} 交割。";
                }
                else if (mail.action == "investment")
                    message = $"已批准投资项目。资本将于第{ey}年 Q{eq} 投入并进入执行。";
                else
                    message = $"已批准申请 {mail.origin}—{mail.dest} 航权。将于第{ey}年 Q{eq} 消耗政府关系并获批。";
                return true;
            }

            if (mail.action == "finance_crisis")
            {
                if (IsFinanceRestructure(mail))
                    return ApproveRestructuring(gs, mail, out message);
                return ApproveFinanceLoan(gs, mail, out message);
            }

            if (mail.action == FuelHedgeAction)
                return ApproveFuelHedge(gs, mail, out message);

            if (mail.action == AnnualBudgetAction)
                return ApproveAnnualBudget(gs, mail, out message);

            if (mail.action == CarbonBuyAction)
                return ApproveCarbonBuy(gs, mail, out message);

            if (mail.action == FinanceLoanAction)
                return ApproveFinanceLoan(gs, mail, out message);

            if (mail.action == FleetBuyAction)
                return ApproveFleetBuy(gs, mail, out message);

            if (mail.action == HrRecruitAction)
                return ApproveHrRecruit(gs, mail, out message);

            if (mail.action == "parts_order")
                return ApprovePartsOrder(gs, mail, out message);

            if (mail.action == "comp_review")
                return ApproveCompensation(gs, mail, out message);

            if (mail.action == "annual_goals")
            {
                if (BoardManager.Instance == null || !BoardManager.Instance.ConfirmAnnualGoals())
                {
                    message = "尚无年度目标可确认。";
                    return false;
                }
                Mark(gs, mail.id, "approved");
                message = $"已确认第 {gs.year} 财年年度目标。可在愿景页持续跟踪进度。";
                return true;
            }

            if (mail.action == "capacity_plan")
            {
                Mark(gs, mail.id, "approved");
                gs.AddDecisionLog("运力规划：批准补强。");
                message = "已批准运力补强。可在本函或机队页继续选定引进方案。";
                return true;
            }

            if (mail.action == StaffRecruitment.HireAction)
                return StaffRecruitment.ApproveOffer(gs, mail, out message);

            if (mail.action == "ops_schedule")
                return ApproveOpsSchedule(gs, mail, out message);

            if (mail.action == AssignFlightAction)
                return ApproveAssignFlight(gs, mail, out message);

            if (mail.action == AssignAircraftAction)
                return ApproveAssignAircraft(gs, mail, null, out message);   // 无指定机号 = 取收益最优候选

            // 时刻备选件不带机号参数（与执飞飞机提名不同）：批准 = 采纳**运营部推荐的那一条**（备选表首条，
            // 排序口径见 SlotCandidateLogic.Compare）。CEO 想挑别的，就得走 ui 的「候选 N」按钮那条路
            // （ApproveSlotSelect 带 clock 重载），而不是靠「批准」按钮替他瞎猜。
            if (mail.action == SlotSelectAction)
                return ApproveSlotSelect(gs, mail, null, out message);

            if (mail.action == CapacityDirectiveAction)
                return ApproveCapacityDirective(gs, mail, out message);

            if (mail.action == TransitionBusinessLogic.InquiryAction)
                return TransitionBusinessLogic.SignAllIdleFromInbox(gs, mail, out message);

            if (mail.action == SeasonReviewAction)
                return ApproveSeasonReview(gs, mail, out message);

            if (mail.action == SlotUsageAction)
                return ApproveSlotUsage(gs, mail, out message);

            if (mail.action == "reg_inspect")
            {
                message = "请选择全力配合 / 重点整改 / 申请延期。";
                return false;
            }

            Mark(gs, mail.id, "approved");
            message = "已批准。";
            return true;
        }

        public static void ProcessPendingEffects(GameState gs)
        {
            if (gs == null) return;
            SweepReadOnlyMails(gs);   // 旧档：清理不该阻塞时间的告知类待办邮件
            if (gs.inbox != null)
            {
                // 推进过程中会插入新邮件（进度汇报等），按快照遍历避免漏件/重复。
                var snapshot = new List<InboxMail>(gs.inbox);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var m = snapshot[i];
                    if (m == null || !m.effectPending) continue;
                    if (gs.inbox.IndexOf(m) < 0) continue;
                    if (Turn(gs.year, gs.quarter) < Turn(m.effectYear, m.effectQuarter)) continue;
                    m.effectPending = false;
                    if (m.action == "open_route")
                    {
                        // Phase 18：已受理的准入许可按季推进（审查→公示→发证/否决）
                        if (m.permitStage >= RoutePermitLogic.StageReview)
                        {
                            AdvanceRoutePermit(gs, m);
                            continue;
                        }

                        if (ActionExecutor.Instance == null)
                        {
                            var go = new GameObject("ActionExecutor");
                            go.AddComponent<ActionExecutor>();
                        }

                        var advice = new AdvisorResponse
                        {
                            suggestedAction = "open_route",
                            message = m.subject,
                            data = new AdvisorDataPayload
                            {
                                origin = m.origin,
                                destination = m.dest,
                                stopover = m.stopover
                            }
                        };
                        var result = ActionExecutor.Instance.Execute(SuggestedAction.OpenRoute, advice);
                        Add(gs, new InboxMail
                        {
                            id = "fx_" + m.id + "_" + gs.year + gs.quarter,
                            sender = "运营部",
                            role = "执行回执",
                            glyph = "运",
                            kind = "report",
                            subject = result.success
                                ? $"航线 {m.origin}-{m.dest} 已投入运营"
                                : $"航线 {m.origin}-{m.dest} 未能开通",
                            body = result.message ?? "",
                            status = "approved",
                            tag = result.success ? "回执" : "受阻",
                            receivedYear = gs.year,
                            receivedQuarter = gs.quarter,
                            time = gs.DateLabel,
                            deadlineQuarters = 1
                        });
                        continue;
                    }

                    if (m.action == "codeshare" || m.action == "alliance_join")
                    {
                        EnsureStrategyManager();
                        string execMsg;
                        bool ok = m.action == "codeshare"
                            ? StrategyManager.Instance.SignCodeshare(m.dest, out execMsg)
                            : StrategyManager.Instance.JoinAsObserver(out execMsg);
                        AddExecReceipt(gs, m, ok, ok
                                ? (m.action == "codeshare" ? "代码共享已生效" : "入盟手续已办结")
                                : (m.action == "codeshare" ? "代码共享未能签署" : "入盟未能完成"),
                            execMsg);
                        continue;
                    }

                    if (m.action == "buy_slot" || m.action == ApplySlotAction || m.action == ChangeSlotAction
                        || m.action == "apply_right")
                    {
                        EnsureStrategyManager();
                        string execMsg;
                        bool ok;
                        if (m.action == ChangeSlotAction)
                        {
                            // ★2026-10-16 第四十一次修订：改时由**协调人**批复（系列余量 + 15 分容差），
                            //   不收费、不占新容量；被驳则原槽保留、该需求波仍排不出（如实回报）。
                            ok = ExecuteSlotChange(gs, m, out execMsg);
                        }
                        else if (m.action == "buy_slot" || m.action == ApplySlotAction)
                        {
                            string apt = string.IsNullOrEmpty(m.origin) ? (gs.hubIata ?? "PEK") : m.origin;
                            int qty = SlotQtyOf(m);
                            // ★2026-10-15：协调池分配免费、二级市场购入计费 —— 两条渠道的价钱本就不同。
                            bool freeChan = m.action == ApplySlotAction;
                            ok = StrategyManager.AcquireSlotsAt(gs, apt, SlotMailClock(m), qty, freeChan, out execMsg);
                        }
                        else
                        {
                            string o = string.IsNullOrEmpty(m.origin) ? "PEK" : m.origin;
                            string d = string.IsNullOrEmpty(m.dest) ? "SIN" : m.dest;
                            ok = StrategyManager.Instance.ApplyRouteRight(o, d, 5, out execMsg);
                        }
                        string note = SlotSettleNote(m, execMsg, ok);
                        // ★2026-10-15：交割成功且单子上记了「为哪条线、为哪一刻」时，运营部自己接着办 ——
                        // 挑机、调环、呈报加机（或缺机/缺另一端的受阻汇报），不再让 CEO 回 n98 重来一遍。
                        if (ok && !string.IsNullOrEmpty(m.slotRouteId))
                        {
                            string auto = RescheduleAfterSlotSettle(gs, m);
                            if (!string.IsNullOrEmpty(auto)) note += "\n" + auto;
                        }
                        AddExecReceipt(gs, m, ok,
                            ok ? (m.action == "apply_right" ? "航权已获批"
                                    : m.action == ChangeSlotAction ? "时刻已变更" : "时刻槽已交割")
                               : (m.action == "apply_right" ? "航权申请未获批"
                                    : m.action == ChangeSlotAction ? "改时未获批准" : "时刻槽未能取得"),
                            note);
                        continue;
                    }

                    if (m.action == "investment")
                    {
                        EnsureStrategyManager();
                        bool ok = StrategyManager.Instance.ExecuteInvestment(m.origin, out string execMsg);
                        AddExecReceipt(gs, m, ok,
                            ok ? "投资资本已投入，项目进入执行" : "投资未能投入",
                            execMsg);
                        continue;
                    }

                    if (m.action == "parts_order")
                    {
                        EnsureMaintenanceManager();
                        // Instance 可能仍为空（编辑器/批处理里 AddComponent 不触发 Awake，见本文件其它同类判据）：
                        // 直接解引用会抛 NRE，把 ProcessPendingEffects 余下的**所有**待办效果一起中断。
                        string execMsg = "维保管理器不可用";
                        bool ok = MaintenanceManager.Instance != null
                            && MaintenanceManager.Instance.ConfirmOrder(m.origin, out execMsg);
                        AddExecReceipt(gs, m, ok,
                            ok ? "航材已采购入库" : "航材采购未能入库",
                            execMsg);
                    }
                }
            }

            ExpireOpenFinanceCrisis(gs);
            ExpireOpenFuelHedge(gs);
            MaybeOfferFuelHedge(gs);
            MaybeOfferAnnualBudget(gs);
            MaybeOfferCarbonBuy(gs);
            MaybeOfferAlliance(gs);
            MaybeOfferIrrops(gs);
            MaybeOfferRightsAndSlots(gs);
            MaybeOfferUnion(gs);
            MaybeOfferFinanceCrisis(gs);
            MaybeOfferOpsSchedule(gs);
            MaybeOfferInvestment(gs);
            MaybeOfferPartsOrder(gs);
            MaybeOfferInspection(gs);
            MaybeOfferCompensation(gs);
            MaybeOfferAnnualGoals(gs);
            MaybeOfferCapacity(gs);
            MaybeOfferSeasonReview(gs);

            // 时刻槽使用率考核的换季清算（判航季：航季开端 Q2/Q4 结算上一等价航季）
            OnSlotUsageSeasonTurn(gs);

            SyncFeedIntoInbox(gs);
        }

        /// <summary>消息中心/快讯并入收件箱，避免独立消息栏。</summary>
        public static void SyncFeedIntoInbox(GameState gs)
        {
            if (gs == null) return;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            var items = MessageCenter.All;
            if (items == null || items.Count == 0) return;
            bool any = false;
            for (int i = 0; i < items.Count; i++)
            {
                var msg = items[i];
                if (msg == null) continue;
                string id = "msg_" + msg.id;
                if (HasMailId(gs, id)) continue;
                int p = msg.priority;
                string kind = p == (int)MessagePriority.Emergency ? "alert"
                    : p == (int)MessagePriority.Warning ? "media" : "report";
                string role = p == (int)MessagePriority.Emergency ? "紧急快讯"
                    : p == (int)MessagePriority.Warning ? "预警快讯"
                    : p == (int)MessagePriority.Success ? "进展快讯" : "动态快讯";
                gs.inbox.Insert(0, new InboxMail
                {
                    id = id,
                    sender = string.IsNullOrEmpty(msg.deptName) ? "快讯" : msg.deptName,
                    role = role,
                    glyph = "讯",
                    kind = kind,
                    action = "news",
                    origin = msg.deptId ?? "",
                    subject = msg.title ?? "",
                    body = string.IsNullOrEmpty(msg.body) ? (msg.title ?? "") : msg.body,
                    status = "approved",
                    read = msg.read,
                    tag = p == (int)MessagePriority.Emergency ? "紧急" : "快讯",
                    receivedYear = msg.year > 0 ? msg.year : gs.year,
                    receivedQuarter = msg.quarter > 0 ? msg.quarter : gs.quarter,
                    deadlineQuarters = 1
                });
                StampReceived(gs, gs.inbox[0]);
                any = true;
            }
            if (any) Changed?.Invoke();
        }

        #region 航线准入许可（Phase 18）

        /// <summary>
        /// CEO 批准开航计划：只落"批准"，随即转入局方准入办理（许可 / 航权 / 时刻 + 公示）。
        /// 批准 ≠ 可以飞——这与现实一致：航线经营许可核准后才排班开航。
        /// </summary>
        public static bool AcceptRoutePermit(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "邮件无效。"; return false; }

            string o = string.IsNullOrEmpty(mail.origin) ? "PEK" : mail.origin;
            string d = string.IsNullOrEmpty(mail.dest) ? "SHA" : mail.dest;
            mail.origin = o;
            mail.dest = d;
            if (gs.HasRoute(o, d))
            {
                message = $"航线 {o}-{d} 已在运营，无需重复申请许可。";
                return false;
            }

            bool intl = RoutePermitLogic.IsInternational(o, d);
            bool fromHub = RoutePermitLogic.FromHub(o, d);
            bool needsSlot = RoutePermitLogic.NeedsSlot(o, d, intl, fromHub);
            string kind = intl ? "国际/地区航线经营许可" : "国内航线经营许可";

            mail.permitAttempts = Mathf.Max(1, mail.permitAttempts);
            mail.permitInternational = intl;
            mail.permitChance = RoutePermitLogic.PassChance(gs, o, d, intl, mail.permitAttempts);
            mail.permitStage = RoutePermitLogic.StageReview;
            mail.permitNote = kind + " · 审查中";
            mail.permitObjection = false;
            mail.permitAcceptYear = gs.year;
            mail.permitAcceptQuarter = gs.quarter;
            mail.permitNode = RoutePermitLogic.NodeAccept;

            int quarters = RoutePermitLogic.ReviewQuarters(intl, mail.permitAttempts);
            NextQuarter(gs.year, gs.quarter, out int ey, out int eq);
            for (int i = 1; i < quarters; i++)
                NextQuarter(ey, eq, out ey, out eq);
            mail.permitDueYear = ey;
            mail.permitDueQuarter = eq;
            // 每个季度是一个办理节点，逐季复核并汇报；到达预计批复季才作出批复。
            NextQuarter(gs.year, gs.quarter, out int cy, out int cq);
            mail.effectPending = true;
            mail.effectYear = cy;
            mail.effectQuarter = cq;

            int gap = RoutePermitLogic.MissingPrerequisite(gs, o, d, intl, out string gapNote);
            mail.permitGate = PermitGateLabel(gap);

            Mark(gs, mail.id, "approved");
            mail.status = "approved";

            message = $"已批准开通 {o}-{d}。运营部将向{(intl ? "民航局" : "地区管理局")}递交{kind}申请，"
                    + $"预计 第{ey}年 Q{eq} 批复——批准不等于可以飞，须待许可发证。"
                    + $" 受理回执约 {InboxCadence.AdmitBusinessDays} 个工作日后送达收件箱。";
            if (gap != 0)
                message += $" 注意：{gapNote}（{PermitGateLabel(gap)}未办妥则许可将挂起）";
            if (needsSlot)
                message += " 许可发证后再办时刻（协调季分配/竞拍），先证后槽可避免未获许可时时刻费用打水漂。";

            string admitBody = InboxLetter.Section("办理回执") + InboxLetter.Bullet(message)
                + BuildAdmitBody(gs, o, d, mail, intl, needsSlot, gapNote, quarters, ey, eq);
            AddPermitReceipt(gs, mail, "admit", $"准入受理：{o}-{d} {kind}", admitBody, "受理");

            gs.AddDecisionLog($"准入：受理 {o}-{d} {kind}，预计 第{ey}年 Q{eq} 批复");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 许可前置缺口码 → permitGate 标记文本。
        /// 0=无缺口（空串） 1=航权 2=国际运行资质（海外引进机组）。
        /// 集中在此，避免各调用点各自把非零 gap 都当成「航权」。
        /// </summary>
        static string PermitGateLabel(int gap) => gap switch
        {
            1 => "航权",
            2 => "国际运行资质",
            _ => ""
        };

        static string BuildAdmitBody(GameState gs, string o, string d, InboxMail mail,
            bool intl, bool needsSlot, string gapNote, int quarters, int ey, int eq)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(InboxLetter.Section("受理信息"));
            sb.Append(InboxLetter.Row("受理号", $"{RoutePermitLogic.CaseNo(mail)}（第{mail.permitAttempts}次递交）"));
            sb.Append(InboxLetter.Row("航线", $"{o} → {d}（{(intl ? "国际/地区" : "国内")}航线）"));
            sb.Append(InboxLetter.Row("许可类型", intl ? "国际/地区航线经营许可" : "国内航线经营许可"));
            sb.Append(InboxLetter.Row("当前状态", mail.permitNote));
            sb.Append(InboxLetter.Row("办理时限", $"{quarters} 个季度 · 预计 第{ey}年 Q{eq} 批复"));

            sb.Append(InboxLetter.Section("后续工作流程（许可五道关 + 开航准备）"));
            var chain = new System.Text.StringBuilder();
            for (int node = RoutePermitLogic.NodeAccept; node <= RoutePermitLogic.NodeGrant; node++)
            {
                if (chain.Length > 0) chain.Append(" → ");
                chain.Append($"{node}. {RoutePermitLogic.NodeName(node)}");
            }
            chain.Append($" → {RoutePermitLogic.NodeSlot}. {RoutePermitLogic.NodeName(RoutePermitLogic.NodeSlot)}");
            sb.Append(chain.ToString()).Append('\n');
            sb.Append("许可五道关由局方审查：任何一关不过都会顺延或退回；每到一个节点，运营部会发一封进度汇报邮件。\n");
            if (needsSlot)
                sb.Append("第 6 步在发证之后办理：先证后槽——时刻（尤其竞拍时段）等许可到手再花钱，避免未获许可时费用打水漂。\n");

            sb.Append(InboxLetter.Section("准入进度表"));
            sb.Append(RoutePermitLogic.ProgressTable(intl, needsSlot,
                mail.permitAcceptYear, mail.permitAcceptQuarter, ey, eq, mail.permitNode, mail.permitStage,
                mail.effectPending));

            sb.Append(InboxLetter.Section("通过率参考"));
            sb.Append(RoutePermitLogic.ChanceBreakdown(gs, o, d, intl, mail.permitAttempts)).Append('\n');
            sb.Append(InboxLetter.Bullet($"当前估算　{mail.permitChance * 100f:F0}%"));

            sb.Append(InboxLetter.Section("前置条件"));
            sb.Append(string.IsNullOrEmpty(gapNote)
                ? InboxLetter.Bullet("航权无缺口，按期审查。")
                : InboxLetter.Bullet(gapNote));
            sb.Append(needsSlot
                ? InboxLetter.Bullet("时刻槽不列为发证条件：本航线须在发证后取得协调机场时刻，配到时刻即可开航。")
                : InboxLetter.Bullet("本航线无需新增时刻（非协调机场），凭证报备航班计划即可开航。"));

            sb.Append(InboxLetter.Section("进度汇报与提示"));
            sb.Append(InboxLetter.Bullet("每到一个节点，运营部会发一封进度汇报邮件（含最新进度表、通过率与下一步）。"));
            sb.Append(InboxLetter.Bullet("许可未发证前不得售票开航，本邮件即该航线的「许可办理单」。"));
            sb.Append(InboxLetter.Bullet("办理进度见「航权与时刻槽」页；公示异议与不予许可的应对在本收件箱处理。"));
            return sb.ToString();
        }

        /// <summary>按季推进准入许可：逐季汇报节点 → 前置校验 → 公示异议 → 批复（发证则执行开通）。</summary>
        static void AdvanceRoutePermit(GameState gs, InboxMail m)
        {
            string o = string.IsNullOrEmpty(m.origin) ? "PEK" : m.origin;
            string d = string.IsNullOrEmpty(m.dest) ? "SHA" : m.dest;
            bool intl = m.permitInternational || RoutePermitLogic.IsInternational(o, d);
            m.permitInternational = intl;
            bool fromHub = RoutePermitLogic.FromHub(o, d);
            bool needsSlot = RoutePermitLogic.NeedsSlot(o, d, intl, fromHub);
            string kind = intl ? "国际/地区航线经营许可" : "国内航线经营许可";

            if (gs.HasRoute(o, d))
            {
                m.permitStage = RoutePermitLogic.StageGranted;
                m.permitNode = RoutePermitLogic.NodeSlot;
                m.effectPending = false;
                m.permitNote = "航线已在运营";
                return;
            }

            // —— 许可已发证：只在等时刻/公司条件，直接尝试开航，不再走许可审查 —— 
            if (m.permitStage == RoutePermitLogic.StageGranted)
            {
                EnsurePermitTimeline(m, intl);
                TryLaunchGrantedPermit(gs, m, o, d, intl, needsSlot, fromHub, kind);
                return;
            }

            EnsurePermitTimeline(m, intl);

            // —— 许可前置：航权额度（国际硬前置）+ 国际运行资质机组。时刻在发证后再办，不卡发证 —— 
            int gap = RoutePermitLogic.MissingPrerequisite(gs, o, d, intl, out string gapNote);
            if (gap != 0)
            {
                // gap==1 缺航权：走战略发展部航权申请；gap==2 缺海外引进机组：走人力招聘。
                // 两条路径的处置部门、邮件话术、permitGate 标记都不同，不能混为一谈
                //（旧实现对任何非零 gap 都当航权处理，会误触发航权申请流程）。
                bool rightGate = gap == 1;
                string gateLabel = rightGate ? "航权" : "国际运行资质";
                bool first = m.permitGate != gateLabel;
                if (rightGate && first) QueueRightDecision(gs, o, d, null, out _);
                m.permitGate = gateLabel;
                DeferPermit(m, 2);
                m.permitNote = kind + (rightGate ? " · 待航权" : " · 待国际运行资质机组");

                string remedy = rightGate
                    ? "已请战略发展部在收件箱提出航权申请方案"
                    : "请到 人力 → 招聘 用「海外引进」渠道录用至少 1 名飞行员/机务/乘务，到岗后许可自动恢复审查";

                string body = first
                    ? InboxLetter.Section("受阻原因") +
                      InboxLetter.Bullet(gapNote) +
                      InboxLetter.Section("已采取的措施") +
                      InboxLetter.Bullet(remedy) +
                      InboxLetter.Section("后续") +
                      InboxLetter.Bullet($"{gateLabel}办妥后许可自动恢复审查，当前预计 第{m.permitDueYear}年 Q{m.permitDueQuarter} 复核")
                      + PermitProgressBlock(m, intl, needsSlot, m.effectPending)
                    : InboxLetter.Section("节点停滞") +
                      InboxLetter.Bullet($"{gateLabel}仍未办妥：{gapNote}") +
                      InboxLetter.Bullet($"许可已顺延至 第{m.permitDueYear}年 Q{m.permitDueQuarter} 复核；办妥{gateLabel}即自动恢复审查") +
                      InboxLetter.Bullet($"{gateLabel}是国际/地区航线的发证硬前置，未取得前不会作出批复。")
                      + PermitProgressBlock(m, intl, needsSlot, m.effectPending);
                AddPermitReceipt(gs, m, "gate",
                    first ? $"准入受阻：{o}-{d} 待{gateLabel}就绪" : $"准入停滞：{o}-{d} 仍待{gateLabel}就绪",
                    body, "受阻");
                gs.NotifyChanged();
                return;
            }
            m.permitGate = "";

            int dueTurn = RoutePermitLogic.Turn(m.permitDueYear, m.permitDueQuarter);
            int span = Mathf.Max(1, dueTurn - RoutePermitLogic.Turn(m.permitAcceptYear, m.permitAcceptQuarter));
            bool finalTick = RoutePermitLogic.Turn(gs.year, gs.quarter) >= dueTurn;
            float objectionChance = intl ? 0.35f : 0.18f;

            // —— 中间节点：每到一个节点发一封进度汇报 —— 
            if (!finalTick)
            {
                var nodes = RoutePermitLogic.NodesAt(m.permitNode,
                    m.permitAcceptYear, m.permitAcceptQuarter, m.permitDueYear, m.permitDueQuarter,
                    gs.year, gs.quarter);
                bool noticeNow = nodes.Contains(RoutePermitLogic.NodeNotice);

                if (noticeNow && !m.permitObjection && UnityEngine.Random.value < objectionChance)
                {
                    m.permitObjection = true;
                    m.permitNode = RoutePermitLogic.NodeNotice;
                    m.permitStage = RoutePermitLogic.StageNotice;
                    DeferPermit(m, 1);
                    m.permitNote = kind + " · 公示期（有异议）";
                    AddPermitObjectionMail(gs, m, o, d, intl, needsSlot, kind);
                    AddPermitReceipt(gs, m, "objection", $"准入公示受阻：{o}-{d} 收到异议",
                        $"公示期内收到竞对异议，审批顺延一季，详细应对见「公示异议」邮件。"
                        + PermitProgressBlock(m, intl, needsSlot), "受阻");
                    gs.NotifyChanged();
                    return;
                }

                if (noticeNow) m.permitStage = RoutePermitLogic.StageNotice;

                int maxNode = 0;
                for (int i = 0; i < nodes.Count; i++)
                    if (nodes[i] > maxNode) maxNode = nodes[i];

                if (maxNode >= RoutePermitLogic.NodeReview)
                {
                    int from = m.permitNode + 1;
                    m.permitNode = maxNode;
                    AddPermitReceipt(gs, m, "node",
                        $"准入进度：{o}-{d} 第{maxNode}节点「{RoutePermitLogic.NodeName(maxNode)}」",
                        BuildNodeBody(gs, m, o, d, intl, needsSlot, kind, from, maxNode, span), "进度");
                }

                m.permitNote = kind + (m.permitStage == RoutePermitLogic.StageNotice ? " · 公示期" : " · 审查中");
                if (!string.IsNullOrEmpty(m.permitGate)) m.permitNote += " · 待" + m.permitGate;
                NextQuarter(gs.year, gs.quarter, out int ny, out int nq);
                m.effectYear = ny;
                m.effectQuarter = nq;
                m.effectPending = true;
                gs.NotifyChanged();
                return;
            }

            // —— 公示期异议（公示落在批复季时在此处理；同一许可以一次为限）—— 
            if (m.permitStage == RoutePermitLogic.StageReview && !m.permitObjection
                && UnityEngine.Random.value < objectionChance)
            {
                m.permitObjection = true;
                m.permitNode = RoutePermitLogic.NodeNotice;
                m.permitStage = RoutePermitLogic.StageNotice;
                DeferPermit(m, 1);
                m.permitNote = kind + " · 公示期（有异议）";
                AddPermitObjectionMail(gs, m, o, d, intl, needsSlot, kind);
                AddPermitReceipt(gs, m, "objection", $"准入公示受阻：{o}-{d} 收到异议",
                    $"公示期内收到竞对异议，审批顺延一季，详细应对见「公示异议」邮件。"
                    + PermitProgressBlock(m, intl, needsSlot), "受阻");
                gs.NotifyChanged();
                return;
            }

            // —— 批复 —— 
            m.effectPending = false;
            float chance = m.permitChance > 0f
                ? m.permitChance
                : RoutePermitLogic.PassChance(gs, o, d, intl, Mathf.Max(1, m.permitAttempts));

            if (UnityEngine.Random.value >= chance)
            {
                m.permitStage = RoutePermitLogic.StageDenied;
                m.permitNode = RoutePermitLogic.NodeGrant;
                m.permitNote = kind + " · 未获批";
                Mark(gs, m.id, "rejected");
                Schedule(gs, new InboxMail
                {
                    id = "pmretry_" + m.id + "_" + m.permitAttempts,
                    sender = "运营部",
                    role = "准入办理",
                    glyph = "运",
                    kind = "regulatory",
                    action = "permit_retry",
                    origin = o,
                    dest = d,
                    stopover = m.stopover ?? "",
                    subject = $"不予许可：{o}-{d} {kind}未获批准",
                    body =
                        InboxLetter.Section("审批结果") +
                        InboxLetter.Bullet($"受理号　{RoutePermitLogic.CaseNo(m)}（第{m.permitAttempts}次递交）") +
                        InboxLetter.Bullet($"决定　　局方对 {o}-{d} 的{kind}作出不予许可决定") +
                        InboxLetter.Section("未获批评估") +
                        RoutePermitLogic.ChanceBreakdown(gs, o, d, intl, m.permitAttempts) + "\n" +
                        InboxLetter.Bullet($"本次估算通过率　{chance * 100f:F0}%，未命中") +
                        PermitProgressBlock(m, intl, needsSlot) +
                        InboxLetter.Section("可选应对") +
                        InboxLetter.Bullet("批准（重新递交）　再审更慢（+1 季），通过率下调 12%") +
                        InboxLetter.Bullet("拒绝（放弃）　终止该航线准入，可另择航线") +
                        InboxLetter.Section("建议") +
                        InboxLetter.Bullet("先提升安全评级 / 局方关系与政府关系，或先取得航权额度，再重新递交。"),
                    tag = "未获批",
                    receivedYear = gs.year,
                    receivedQuarter = gs.quarter,
                    time = gs.DateLabel,
                    deadlineQuarters = DefaultDeadlineQuarters
                }, InboxCadence.AdmitReady(gs.SimUtc));
                gs.AddDecisionLog($"准入：{o}-{d} {kind}未获批（估算通过率 {chance * 100f:F0}%）");
                gs.NotifyChanged();
                return;
            }

            // —— 发证 → 开航执行（时刻协调与开航准备都在发证之后）—— 
            m.permitStage = RoutePermitLogic.StageGranted;
            m.permitNode = RoutePermitLogic.NodeGrant;
            m.permitNote = kind + " · 已发证";
            m.permitGate = "";
            Mark(gs, m.id, "approved");
            TryLaunchGrantedPermit(gs, m, o, d, intl, needsSlot, fromHub, kind);
            gs.NotifyChanged();
        }

        /// <summary>
        /// 许可已到手后的开航准备与执行（第 6 节点）：
        /// 先看时刻（需要则请战略部在收件箱办购买/竞拍），再校验现金与运力，
        /// 全部就绪即落航线；开航未成则退还时刻槽、保留许可效力，待条件就绪再试。
        /// </summary>
        static void TryLaunchGrantedPermit(GameState gs, InboxMail m, string o, string d,
            bool intl, bool needsSlot, bool fromHub, string kind)
        {
            m.permitStage = RoutePermitLogic.StageGranted;
            m.effectPending = true;

            int slotIndex = -1, slotIndexB = -1, slotIndexV = -1;
            string originApt = fromHub ? (string.IsNullOrEmpty(gs.hubIata) ? o : gs.hubIata) : o;
            string viaApt = RoutePermitLogic.StopoverOf(m.stopover, o, d);
            if (needsSlot)
            {
                // ★2026-10-16：**逐停站**占槽 —— 本场端 + 经停端 + 对端，缺任一停站都不能开航。
                // 经停端一趟往返停两次（去程一次、回程一次），但开航只需一条持仓顶着，
                // 与另两端同口径（加密时由 WaveEndNeeds 逐条补齐）。
                bool needA = RoutePermitLogic.NeedsDepartureSlot(originApt);
                bool needB = RoutePermitLogic.NeedsDepartureSlot(d);
                bool needV = viaApt != null && RoutePermitLogic.NeedsDepartureSlot(viaApt);
                if (needA) slotIndex = RoutePermitLogic.ReserveSlotAt(gs, originApt);
                if (needV) slotIndexV = RoutePermitLogic.ReserveSlotAt(gs, viaApt);
                if (needB) slotIndexB = RoutePermitLogic.ReserveSlotAt(gs, d);
                bool gotA = !needA || slotIndex >= 0;
                bool gotV = !needV || slotIndexV >= 0;
                bool gotB = !needB || slotIndexB >= 0;
                if (!gotA || !gotV || !gotB)
                {
                    // 逐停站各占一条：缺任一停站都不能开航（先证后槽，此时才涉及时刻花费）
                    if (slotIndex >= 0) RoutePermitLogic.ReleaseSlot(gs, slotIndex);
                    if (slotIndexV >= 0) RoutePermitLogic.ReleaseSlot(gs, slotIndexV);
                    if (slotIndexB >= 0) RoutePermitLogic.ReleaseSlot(gs, slotIndexB);
                    slotIndex = -1;
                    slotIndexV = -1;
                    slotIndexB = -1;
                    bool first = m.permitGate != "时刻";
                    string apt = !gotA ? originApt : !gotV ? viaApt : d;
                    if (first || !HasOpenAction(gs, "buy_slot"))
                        QueueSlotDecision(gs, apt, ScheduleSlots.DepTimes[1], null, out _);   // 已有待批提案时内部会自守
                    m.permitGate = "时刻";
                    m.permitNote = kind + " · 已核准 · 待时刻";
                    DeferPermit(m, 2);
                    AddPermitReceipt(gs, m, "slotwait", $"准入已核准：{o}-{d} 待协调时刻槽",
                        InboxLetter.Section("发证") +
                        InboxLetter.Bullet($"{kind}已核准（第{gs.year}年 Q{gs.quarter}），许可证已到手，可对外售票准备") +
                        InboxLetter.Section("当前节点") +
                        InboxLetter.Bullet($"第{RoutePermitLogic.NodeSlot}节点 时刻协调与开航：尚无可用时刻槽") +
                        InboxLetter.Bullet($"缺口端　{apt}（{AirportCatalog.TierLabelOf(apt)}，协调机场，按夏秋/冬春协调季分配，热门时段需竞拍或购买）") +
                        InboxLetter.Bullet($"停站口径　飞机在哪一站停一次，那一站就要一对（到场 + 离场）：本场端 {originApt} 1 对、对端 {d} 1 对；"
                            + "若航线经停第三地，经停站一趟往返停两次，故要 2 对") +
                        InboxLetter.Section("已采取的措施") +
                        InboxLetter.Bullet(first
                            ? "已请战略发展部在收件箱提出时刻槽购买方案（许可已到手，此时交割时刻费用，不会白花）"
                            : "时刻槽购买方案已在收件箱待批；若已拒绝，战略发展部会重新提出") +
                        InboxLetter.Section("为什么先证后槽") +
                        InboxLetter.Bullet("时刻（尤其竞拍时段）先花钱再等证，一旦许可未获批准费用即打水漂；先取证再办时刻更稳妥。") +
                        InboxLetter.Section("后续") +
                        InboxLetter.Bullet($"各停站时刻齐备后自动排班开航（预计 第{m.permitDueYear}年 Q{m.permitDueQuarter} 复核）")
                        + PermitProgressBlock(m, intl, needsSlot, true), "受阻");
                    return;
                }
            }

            EnsureActionExecutor();
            var advice = new AdvisorResponse
            {
                suggestedAction = "open_route",
                message = m.subject,
                data = new AdvisorDataPayload
                {
                    origin = o,
                    destination = d,
                    stopover = m.stopover,
                    profit = RoutePermitLogic.EstimateDailyProfitWan(gs, o, d, m.stopover)
                }
            };
            var result = ActionExecutor.Instance.ExecuteOpenRouteFromPermit(advice);
            bool slotTaken = slotIndex >= 0 || slotIndexV >= 0 || slotIndexB >= 0;

            if (result != null && result.success)
            {
                m.permitGate = "";
                m.permitNode = RoutePermitLogic.NodeSlot;
                m.permitNote = kind + " · 已发证并开航";
                m.effectPending = false;
                var slotApts = new System.Collections.Generic.List<string>();
                if (slotIndex >= 0) slotApts.Add(originApt);
                if (slotIndexV >= 0) slotApts.Add(viaApt);
                if (slotIndexB >= 0) slotApts.Add(d);
                AddPermitReceipt(gs, m, "grant", $"航线 {o}-{d} 已获许可并投入运营",
                    InboxLetter.Section("发证") +
                    InboxLetter.Bullet($"{kind}已核准") +
                    InboxLetter.Section("时刻协调") +
                    InboxLetter.Bullet(slotTaken
                        ? $"已占各停站时刻槽（{string.Join("、", slotApts.ToArray())}），可排班飞行"
                        : "本航线无需新增时刻（非协调机场）") +
                    InboxLetter.Section("开航执行") +
                    InboxLetter.Bullet($"开航季度　第{gs.year}年 Q{gs.quarter}（第{m.permitNode}节点 {RoutePermitLogic.NodeName(RoutePermitLogic.NodeSlot)}）") +
                    InboxLetter.Bullet(result.message) +
                    PermitProgressBlock(m, intl, needsSlot, false), "发证");
                return;
            }

            // 开航未成（现金/运力等）→ 退还时刻槽，许可证继续有效，待条件就绪再试
            if (slotIndex >= 0) RoutePermitLogic.ReleaseSlot(gs, slotIndex);
            if (slotIndexV >= 0) RoutePermitLogic.ReleaseSlot(gs, slotIndexV);
            if (slotIndexB >= 0) RoutePermitLogic.ReleaseSlot(gs, slotIndexB);
            m.permitGate = "运力";
            m.permitNote = kind + " · 已核准 · 待开航条件";
            DeferPermit(m, 2);
            AddPermitReceipt(gs, m, "grantblocked", $"准入已核准，但 {o}-{d} 暂无法开航",
                InboxLetter.Section("发证") +
                InboxLetter.Bullet($"{kind}已核准（许可证长期有效，不会因开航延迟失效）") +
                InboxLetter.Section("开航受阻") +
                InboxLetter.Bullet(result != null ? result.message : "未知原因") +
                InboxLetter.Section("后续") +
                InboxLetter.Bullet(needsSlot && !slotTaken
                    ? "已退还预占时刻槽（未开航不占用机场时刻，避免闲置浪费）"
                    : "待现金/运力就绪后自动重试") +
                InboxLetter.Bullet($"预计 第{m.permitDueYear}年 Q{m.permitDueQuarter} 重试开航")
                + PermitProgressBlock(m, intl, needsSlot, true), "受阻");
        }

        /// <summary>旧档补齐：受理季、已上报节点（缺失时按审查时限倒推，保证进度表可用）。</summary>
        static void EnsurePermitTimeline(InboxMail m, bool intl)
        {
            if (m == null) return;
            if (m.permitNode < RoutePermitLogic.NodeAccept) m.permitNode = RoutePermitLogic.NodeAccept;
            if (m.permitAcceptYear > 0 || m.permitAcceptQuarter > 0) return;
            int q0 = RoutePermitLogic.ReviewQuarters(intl, Mathf.Max(1, m.permitAttempts));
            int ty = m.permitDueYear > 0 ? m.permitDueYear : Mathf.Max(1, m.effectYear);
            int tq = m.permitDueQuarter > 0 ? m.permitDueQuarter : Mathf.Max(1, m.effectQuarter);
            RoutePermitLogic.After(ty, tq, -q0, out int ay, out int aq);
            if (ay < 1) { ay = 1; aq = 1; }
            m.permitAcceptYear = ay;
            m.permitAcceptQuarter = aq;
        }

        /// <summary>准入进度表小节（随各类办理邮件一起发出）。</summary>
        static string PermitProgressBlock(InboxMail m, bool intl, bool needsSlot, bool launchPending = false)
        {
            if (m == null) return "";
            return InboxLetter.Section("准入进度表")
                + RoutePermitLogic.ProgressTable(intl, needsSlot,
                    m.permitAcceptYear, m.permitAcceptQuarter,
                    m.permitDueYear, m.permitDueQuarter, m.permitNode, m.permitStage, launchPending);
        }

        /// <summary>节点进度汇报正文：本季完成哪些节点、局方查什么、下一步与预计批复。</summary>
        static string BuildNodeBody(GameState gs, InboxMail m, string o, string d,
            bool intl, bool needsSlot, string kind, int from, int to, int span)
        {
            var sb = new System.Text.StringBuilder();
            string title = from >= to ? RoutePermitLogic.NodeName(to)
                : $"{RoutePermitLogic.NodeName(from)} → {RoutePermitLogic.NodeName(to)}";
            sb.Append(InboxLetter.Section($"节点进度：{title}"));
            sb.Append(InboxLetter.Row("受理号", $"{RoutePermitLogic.CaseNo(m)}（第{m.permitAttempts}次递交）"));
            sb.Append(InboxLetter.Row("航线", $"{o} → {d}（{(intl ? "国际/地区" : "国内")}航线）"));
            sb.Append(InboxLetter.Row("许可类型", kind));
            sb.Append(InboxLetter.Row("当前节点", $"{to}/{RoutePermitLogic.NodeGrant} {RoutePermitLogic.NodeName(to)}"));

            for (int node = from; node <= to; node++)
                sb.Append(InboxLetter.Bullet($"{(node == to ? "本节点" : "并行节点")} {RoutePermitLogic.NodeName(node)}：{RoutePermitLogic.NodeDetail(node, intl, needsSlot)}"));

            sb.Append(InboxLetter.Section("下一步"));
            if (to >= RoutePermitLogic.NodeGrant)
            {
                sb.Append(InboxLetter.Bullet("已到批复节点，批复结果另行专函。"));
            }
            else
            {
                int next = to + 1;
                RoutePermitLogic.NodeQuarter(next, span, m.permitAcceptYear, m.permitAcceptQuarter,
                    out int ny, out int nq);
                sb.Append(InboxLetter.Bullet($"下个节点　{next}/{RoutePermitLogic.NodeGrant} {RoutePermitLogic.NodeName(next)}（预计 第{ny}年 Q{nq}）"));
            }
            sb.Append(InboxLetter.Bullet($"预计批复　第{m.permitDueYear}年 Q{m.permitDueQuarter} · 通过率 {m.permitChance * 100f:F0}%"));
            if (!string.IsNullOrEmpty(m.permitGate))
                sb.Append(InboxLetter.Bullet($"仍受阻于　{m.permitGate}（未办妥则该节点无法过审，批复顺延）"));

            sb.Append(PermitProgressBlock(m, intl, needsSlot));
            sb.Append(InboxLetter.Section("提示"));
            sb.Append(InboxLetter.Bullet("许可未发证前不得售票开航；办妥前置（航权/时刻）可提高通过率、避免顺延。"));
            sb.Append(InboxLetter.Bullet("进度也可在「航权与时刻槽」页查看。"));
            return sb.ToString();
        }

        /// <summary>公示异议邮件（中间节点与批复季共用）。</summary>
        static void AddPermitObjectionMail(GameState gs, InboxMail m, string o, string d,
            bool intl, bool needsSlot, string kind)
        {
            Add(gs, new InboxMail
            {
                id = "pmobj_" + m.id + "_" + m.permitAttempts,
                sender = "运营部",
                role = "准入公示",
                glyph = "运",
                kind = "regulatory",
                action = "permit_objection",
                origin = o,
                dest = d,
                stopover = m.stopover ?? "",
                subject = $"公示异议：{o}-{d} 航线经营许可被提出异议",
                body =
                    InboxLetter.Section("公示期异议") +
                    InboxLetter.Bullet($"受理号　{RoutePermitLogic.CaseNo(m)}（第{m.permitAttempts}次递交）") +
                    InboxLetter.Bullet("事由　　局方在公示期内收到异议（涉及时刻时段与运力投放），要求补充材料，审批顺延一季") +
                    InboxLetter.Bullet($"预计批复　第{m.permitDueYear}年 Q{m.permitDueQuarter}（顺延后）") +
                    PermitProgressBlock(m, intl, needsSlot) +
                    InboxLetter.Section("可选应对") +
                    InboxLetter.Bullet("批准（公关）　动用政府关系点数 10 点，推动异议撤回，许可回到原节奏") +
                    InboxLetter.Bullet("暂缓（接受延期）　按顺延后的季度继续等待批复") +
                    InboxLetter.Bullet("拒绝（放弃）　终止本次许可办理") +
                    InboxLetter.Section("提示") +
                    InboxLetter.Bullet("异议记录会影响后续公示与航权谈判，必要时先改善局方关系。"),
                tag = "公示",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
        }

        /// <summary>公示异议：批准 = 动用政府关系公关，把许可进度拉回原计划。</summary>
        public static bool ApprovePermitObjection(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "邮件无效。"; return false; }
            var stg = StrategyManager.EnsureState(gs);
            if (stg == null) { message = "战略数据不可用。"; return false; }
            const float cost = 10f;
            if (stg.governmentRelation < cost)
            {
                message = $"政府关系点数不足（公关需 {cost:F0} 点，当前 {stg.governmentRelation:F0}）。可选择「接受延期」或先做局方关系。";
                return false;
            }
            stg.governmentRelation -= cost;

            var permit = FindPermitMail(gs, mail.origin, mail.dest);
            string dueLabel = "";
            if (permit != null)
            {
                NextQuarter(gs.year, gs.quarter, out int ny, out int nq);
                if (Turn(ny, nq) < Turn(permit.permitDueYear, permit.permitDueQuarter))
                {
                    permit.permitDueYear = ny;
                    permit.permitDueQuarter = nq;
                    permit.effectYear = ny;
                    permit.effectQuarter = nq;
                    permit.effectPending = true;
                }
                dueLabel = $" 许可预计 第{permit.permitDueYear}年 Q{permit.permitDueQuarter} 复核。";
            }

            Mark(gs, mail.id, "approved");
            message = $"已公关（消耗 {cost:F0} 政府关系点），异议撤回，许可回到原节奏。{dueLabel}";
            gs.AddDecisionLog($"准入：公关异议 {mail.origin}-{mail.dest}（消耗 {cost:F0} 政府关系点）");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>未获批后重新递交（更快不了：审查 +1 季、通过率下调）。</summary>
        public static bool ApprovePermitRetry(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "邮件无效。"; return false; }

            string o = string.IsNullOrEmpty(mail.origin) ? "PEK" : mail.origin;
            string d = string.IsNullOrEmpty(mail.dest) ? "SHA" : mail.dest;
            var permit = FindPermitMail(gs, o, d);
            if (permit == null)
            {
                message = "未找到对应的许可办理单，请重新在「航线 → 开通新航线」申请开通。";
                return false;
            }

            bool intl = permit.permitInternational || RoutePermitLogic.IsInternational(o, d);
            bool fromHub = RoutePermitLogic.FromHub(o, d);
            bool needsSlot = RoutePermitLogic.NeedsSlot(o, d, intl, fromHub);
            string kind = intl ? "国际/地区航线经营许可" : "国内航线经营许可";

            permit.permitAttempts = Mathf.Min(9, Mathf.Max(1, permit.permitAttempts) + 1);
            permit.permitInternational = intl;
            permit.permitChance = RoutePermitLogic.PassChance(gs, o, d, intl, permit.permitAttempts);
            permit.permitStage = RoutePermitLogic.StageReview;
            permit.permitObjection = false;
            permit.permitNote = kind + " · 重申审查中";
            permit.permitNode = RoutePermitLogic.NodeAccept;
            permit.permitAcceptYear = gs.year;
            permit.permitAcceptQuarter = gs.quarter;
            int quarters = RoutePermitLogic.ReviewQuarters(intl, permit.permitAttempts);
            NextQuarter(gs.year, gs.quarter, out int ey, out int eq);
            for (int i = 1; i < quarters; i++)
                NextQuarter(ey, eq, out ey, out eq);
            permit.permitDueYear = ey;
            permit.permitDueQuarter = eq;
            NextQuarter(gs.year, gs.quarter, out int cy, out int cq);
            permit.effectPending = true;
            permit.effectYear = cy;
            permit.effectQuarter = cq;

            int gap = RoutePermitLogic.MissingPrerequisite(gs, o, d, intl, out string gapNote);
            permit.permitGate = PermitGateLabel(gap);

            Mark(gs, mail.id, "approved");
            AddPermitReceipt(gs, permit, "retry", $"准入重申：{o}-{d} 第{permit.permitAttempts}次递交",
                InboxLetter.Section("重新递交") +
                InboxLetter.Bullet($"受理号　{RoutePermitLogic.CaseNo(permit)}（第{permit.permitAttempts}次递交）") +
                InboxLetter.Bullet($"许可类型　{kind}") +
                InboxLetter.Bullet($"办理时限　{quarters} 个季度 · 预计 第{ey}年 Q{eq} 批复") +
                InboxLetter.Bullet($"通过率　{permit.permitChance * 100f:F0}%（重申惩罚已计入）") +
                InboxLetter.Section("前置条件") +
                InboxLetter.Bullet(string.IsNullOrEmpty(gapNote) ? "航权无缺口，按期审查。" : gapNote) +
                InboxLetter.Bullet(needsSlot
                    ? "时刻槽不列为发证条件：取证后再办时刻（先证后槽，避免费用打水漂）。"
                    : "本航线无需新增时刻（非协调机场）。") +
                PermitProgressBlock(permit, intl, needsSlot) +
                InboxLetter.Section("提示") +
                InboxLetter.Bullet("每个节点仍会发进度汇报邮件；许可未发证前不得售票开航。"), "受理");

            message = $"已重新递交 {o}-{d} 准入申请（第{permit.permitAttempts}次），预计 第{ey}年 Q{eq} 批复。";
            gs.AddDecisionLog($"准入：重申 {o}-{d}（第{permit.permitAttempts}次）");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>放弃准入申请（异议/未获批邮件上选择「放弃」）。</summary>
        public static bool RejectPermitRequest(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "邮件无效。"; return false; }
            var permit = FindPermitMail(gs, mail.origin, mail.dest);
            if (permit != null)
            {
                permit.permitStage = RoutePermitLogic.StageNone;
                permit.permitGate = "";
                permit.permitNode = 0;
                permit.permitNote = "已放弃";
                permit.effectPending = false;
            }
            Mark(gs, mail.id, "rejected");
            message = $"已放弃 {mail.origin}-{mail.dest} 的准入申请，许可办理终止（日后可重新申请开通）。";
            gs.AddDecisionLog("准入：放弃 " + mail.origin + "-" + mail.dest + " 航线经营许可申请");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>承载准入许可的「办理单」＝对应航线的开航提案邮件。</summary>
        public static InboxMail FindPermitMail(GameState gs, string origin, string dest)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != "open_route") continue;
                if (m.permitStage < RoutePermitLogic.StageReview) continue;
                if (string.Equals(m.origin, origin, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(m.dest, dest, StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            return null;
        }

        /// <summary>把许可复核季度顺延 n 季（并保持待办）。</summary>
        static void DeferPermit(InboxMail m, int quarters)
        {
            int ey = m.permitDueYear > 0 ? m.permitDueYear : m.effectYear;
            int eq = m.permitDueQuarter > 0 ? m.permitDueQuarter : m.effectQuarter;
            for (int i = 0; i < quarters; i++)
                NextQuarter(ey, eq, out ey, out eq);
            m.permitDueYear = ey;
            m.permitDueQuarter = eq;
            m.effectYear = ey;
            m.effectQuarter = eq;
            m.effectPending = true;
        }

        static void AddPermitReceipt(GameState gs, InboxMail src, string kind, string subject, string body, string tag)
        {
            // 办理回执/进度/发证属告知类：本封无需批复；受阻类给出联络与自愈说明。
            string request = tag == "发证"
                ? "许可已办结，本封为发证回执，无需批复。"
                : tag == "受阻"
                    ? "如需协调请在收件箱联络运营部；前置办妥后许可自动恢复审查，无需批复本封。"
                    : "本封为办理回执，无需批复；后续每到一个节点将继续向您汇报。";
            var mail = new InboxMail
            {
                id = "pm_" + kind + "_" + src.id + "_" + gs.year + "q" + gs.quarter,
                sender = "运营部",
                role = "准入办理",
                glyph = "运",
                kind = "report",
                action = "permit_progress",
                origin = src.origin,
                dest = src.dest,
                stopover = src.stopover ?? "",
                subject = subject,
                body = InboxLetter.Frame(gs, "运营部", "准入办理", subject, body, request, InboxTime.DayHeader(src)),
                status = "approved",
                tag = tag,
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            };
            DateTime when = tag == "受理"
                ? InboxCadence.AdmitReady(gs.SimUtc)
                : InboxCadence.ProgressReady(gs.SimUtc);
            Schedule(gs, mail, when);
        }

        static void EnsureActionExecutor()
        {
            if (ActionExecutor.Instance != null) return;
            var go = new GameObject("ActionExecutor");
            go.AddComponent<ActionExecutor>();
        }

        #endregion

        /// <summary>
        /// 时刻槽交割回执的正文：申请单上带了「为谁而提」的缘由时照抄一句。
        ///
        /// ★2026-10-15：**不再**写「请到机队·航线选择器重新呈报加机」——交割后运营部自己按单子上
        /// 结构化的 slotRouteId / slotFlightClock 接着重排并呈报（<see cref="RescheduleAfterSlotSettle"/>，
        /// 结果由调用方追加在正文里）。让 CEO 记着哪条线哪一刻再点一遍，等于申请单只办了一半。
        /// 战略部自主提案（不属于任何航线，slotRouteId 为空）才仍然只报「已可排班」。
        /// </summary>
        static string SlotSettleNote(InboxMail m, string execMsg, bool ok)
        {
            string body = execMsg ?? "";
            if (m == null) return body;
            if (m.action == ChangeSlotAction)
            {
                if (!string.IsNullOrEmpty(m.slotReason)) body += $"\n\n缘由：{m.slotReason}";
                body += ok
                    ? "\n原槽已改为新时刻，运营部随即按新时刻重排本线。"
                    : "\n改时未获批准，原槽照旧（该需求波仍排不出，运营部已如实回报）。";
                return body;
            }
            if (m.action != ApplySlotAction || string.IsNullOrEmpty(m.slotReason)) return body;
            body += $"\n\n缘由：{m.slotReason}";
            if (ok && string.IsNullOrEmpty(m.slotRouteId))
                body += "\n该时刻槽已可排班，可在排班时使用。";
            else if (!ok)
                body += "\n本次未能交割，该时段仍缺槽；请另择时段或稍后再申请。";
            return body;
        }

        /// <summary>
        /// 交割季执行**时刻变更（改时）**：协调人按「**15 分容差** + **目标系列余量**」批复。
        ///
        /// ★2026-10-16 第四十一次修订（CEO 第十八次问询衍生）。三条分支，缺一条就会出错：
        /// 1. `|原刻 − 目标刻| ≤ 15 分` → 本就算**同一条槽**（WASG 8.7.1(e)：系列 = 同一或**约同一**时刻），
        ///    协调人视为无需变更 —— **不占新容量、不动持仓**，直接算成功；
        /// 2. 目标系列**无余量** → **驳**：原槽原样保留，该需求波继续排不出（如实回报，不静默放行）；
        /// 3. 有余量 → **批**：把本线在该站的那条持仓**就地改时刻**（`slotTime` 12:00 → 12:30）。
        ///
        /// 为什么是「就地改」而不是「退还旧的 + 申请新的」：改时的现实含义就是**同一条系列的时刻被
        /// 修正**（WASG 8.10.1：槽不绑航线/航班号、可以变更）。若按退还+新取去做，同一时段内
        /// 池余量会被自己算两次，还会在旧时刻留下一条谁也不用的幽灵持仓。
        /// </summary>
        public static bool ExecuteSlotChange(GameState gs, InboxMail m, out string message)
        {
            message = "";
            if (gs == null || m == null) { message = "邮件无效。"; return false; }
            string apt = string.IsNullOrEmpty(m.origin) ? (gs.hubIata ?? "PEK") : m.origin;
            string to = SlotMailClock(m);
            string from = string.IsNullOrEmpty(m.slotFromClock)
                ? "" : RoutePermitLogic.SlotRequestClock(m.slotFromClock);
            if (ScheduleSlots.ParseClock(to) < 0 || ScheduleSlots.ParseClock(from) < 0)
            {
                message = $"改时单缺少有效的「原刻 → 目标刻」（收到 {from} → {to}）。";
                return false;
            }

            int d = ScheduleSlots.ClockDiff(from, to);
            if (d <= ScheduleSlots.SlotToleranceMinutes)
            {
                message = $"{apt} {from} 与 {to} 相差 {d} 分（≤ 容差 {ScheduleSlots.SlotToleranceMinutes} 分）——"
                          + "协调人视为同一系列，**无需变更**，原槽照旧可用。";
                return true;
            }

            int band = ScheduleSlots.BandOfDep(to);
            if (RoutePermitLogic.SlotsAvailable(gs, apt, band) <= 0)
            {
                message = $"协调人**驳回**改时申请：{RoutePermitLogic.SlotPoolLineAt(gs, apt, to)} ——"
                          + $"该系列无可分配余量，{from} 的时刻不得变更为 {to}。原槽保留，"
                          + "该需求波仍需另找时刻（或等其它航线释放）。";
                return false;
            }

            int idx = RoutePermitLogic.FreeHoldingIndexAt(gs, apt, from, m.slotRouteId);
            if (idx < 0)
            {
                message = $"{apt} {from} 那条槽已不在持仓（可能已绑班次、已退还或已被收回），"
                          + "改时申请自动作废 —— 本线该停站仍缺槽。";
                return false;
            }
            var stg = StrategyManager.EnsureState(gs);
            var hold = stg.slots[idx];
            hold.slotTime = to;
            // 获批的系列＝新取得的系列：闲置收回的倒计时应从改时季重新起算（否则会被当旧槽收回）
            int turn = RoutePermitLogic.Turn(gs.year, gs.quarter);
            hold.expiresTurn = turn + 2;
            hold.expiresQuarter = gs.quarter + 2;
            message = $"协调人**批准**时刻变更：{apt} {from} → {to}（差 {d} 分，超过容差故须报批；不收费）。"
                      + $"原槽已改为 {to}。";
            return true;
        }

        static void AddExecReceipt(GameState gs, InboxMail src, bool ok, string subject, string body)
        {
            Schedule(gs, new InboxMail
            {
                id = "fx_" + src.id + "_" + gs.year + gs.quarter,
                sender = "战略发展部",
                role = "执行回执",
                glyph = "战",
                kind = "report",
                // 槽单回执沿用源件的航线（申请单存 slotRouteId，改时单也存）。
                // 战略部自主提案没有航线，此处留空 —— 底栏于是只给部门入口。
                origin = RouteIdOf(gs, src),
                subject = subject,
                body = body ?? "",
                status = "approved",
                tag = ok ? "回执" : "受阻",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ReplyReady(gs.SimUtc));
        }

        static void EnsureStrategyManager()
        {
            if (StrategyManager.Instance != null) return;
            var go = new GameObject("StrategyManager");
            go.AddComponent<StrategyManager>();
        }

        static void EnsureHrManager()
        {
            if (HrManager.Instance != null) return;
            var go = new GameObject("HrManager");
            go.AddComponent<HrManager>();
        }

        static void EnsureMaintenanceManager()
        {
            if (MaintenanceManager.Instance != null) return;
            var go = new GameObject("MaintenanceManager");
            go.AddComponent<MaintenanceManager>();
        }

        static void EnsureSafetyManager()
        {
            if (SafetyManager.Instance != null) return;
            var go = new GameObject("SafetyManager");
            go.AddComponent<SafetyManager>();
        }

        static bool HasInboxId(GameState gs, string id)
        {
            if (gs?.inbox == null || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && gs.inbox[i].id == id) return true;
            return false;
        }

        static bool HasMailId(GameState gs, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (HasInboxId(gs, id)) return true;
            if (gs?.pendingInbox == null) return false;
            for (int i = 0; i < gs.pendingInbox.Count; i++)
            {
                var m = gs.pendingInbox[i]?.mail;
                if (m != null && m.id == id) return true;
            }
            return false;
        }

        /// <summary>按 id 取收件箱里的邮件（不限状态）；没有则 null。未投递（pendingInbox）的不算。</summary>
        static InboxMail FindMail(GameState gs, string id)
        {
            if (gs?.inbox == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m != null && m.id == id) return m;
            }
            return null;
        }

        static bool HasOpenAction(GameState gs, string action)
        {
            if (gs?.inbox == null) return false;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != action) continue;
                if (IsOpen(m) || m.effectPending) return true;
            }
            return false;
        }

        /// <summary>战略部把联盟谈判做成待决策邮件（定稿 n78/n102）。</summary>
        public static void MaybeOfferAlliance(GameState gs)
        {
            if (gs == null) return;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.alliance == null) return;

            if (!HasMailId(gs, "m_codeshare_sin") && !HasOpenAction(gs, "codeshare")
                && stg.alliance.codeSharePartners <= 0)
            {
                Add(gs, BuildAllianceMail(gs, "m_codeshare_sin", "codeshare", "SIN", "新加坡航空",
                    "建议与新加坡航空签署代码共享",
                    "战略发展部建议与新加坡航空做代码共享，补亚洲网络短板。\n\n" +
                    "建议：批准后下季度签约，签约费 25 万。可先点关键词查看联盟与战略合作 或 愿景。\n" +
                    "拒绝则维持独立运营，后续仍可在愿景页自行谈判。"));
                return;
            }

            if (!HasMailId(gs, "m_alliance_join") && !HasOpenAction(gs, "alliance_join")
                && stg.alliance.tier <= 0 && stg.alliance.codeSharePartners > 0)
            {
                Add(gs, BuildAllianceMail(gs, "m_alliance_join", "alliance_join", "", "星空联盟",
                    "建议以观察员身份加入星空联盟",
                    "战略发展部认为代码共享已落地，建议申请星空联盟观察员席位。\n\n" +
                    "建议：批准后下季度办结，入盟费 50 万，机队需 ≥3 架。可点联盟与战略合作 查看等级与收益。\n" +
                    "正式成员仍需机队 ≥20 架，在愿景页申请升级。"));
            }
        }

        public static void MaybeOfferInvestment(GameState gs)
        {
            if (gs == null) return;
            if (gs.year < 1 || (gs.year == 1 && gs.quarter < 2)) return;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            var stg = StrategyManager.EnsureState(gs);
            if (stg?.investments == null) return;
            if (stg.investments.Count > 0 || HasOpenAction(gs, "investment")) return;
            var p = StrategyManager.CatalogHub();
            stg.investments.Add(p);
            QueueInvestmentMail(gs, p);
        }

        public static bool QueueInvestmentMail(GameState gs, InvestmentProject p)
        {
            if (gs == null || p == null) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string id = "m_inv_" + (string.IsNullOrEmpty(p.id) ? "x" : p.id);
            if (HasMailId(gs, id))
            {
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m == null || m.id != id) continue;
                    if (IsOpen(m) || m.effectPending) return true;
                    m.status = "pending";
                    m.read = false;
                    m.effectPending = false;
                    m.receivedYear = gs.year;
                    m.receivedQuarter = gs.quarter;
                    m.time = gs.DateLabel;
                    m.subject = $"请批准投资「{p.name}」";
                    Changed?.Invoke();
                    return true;
                }
            }
            float score = StrategyEconomics.InvestmentScore(p.businessValue, p.resourceFit, p.barrier, p.strategicFit);
            float irr = StrategyEconomics.InvestmentIrr(p.npvWan, p.capitalWan < 1f ? 280f : p.capitalWan, 5);
            Add(gs, new InboxMail
            {
                id = id,
                sender = "战略发展部",
                role = "投资提案",
                glyph = "战",
                kind = "report",
                action = "investment",
                origin = p.id,
                dest = p.name,
                subject = $"请批准投资「{p.name}」",
                body = $"战略发展部建议立项「{p.name}」。\n\n" +
                       $"四维评分　综合 {score:F0} 分（商业 {p.businessValue:F0} / 资源 {p.resourceFit:F0} / 壁垒 {p.barrier:F0} / 契合 {p.strategicFit:F0}）\n" +
                       $"资本支出　{p.capitalWan:F0} 万（批准后下季度扣除）\n" +
                       $"NPV　{p.npvWan:F0} 万　IRR 约 {irr:F0}%\n\n" +
                       "建议：点战略投资与并购 核对组合后再批准。拒绝则本季不投入资本。",
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static bool RejectInvestment(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            EnsureStrategyManager();
            string pid = mail != null ? mail.origin : null;
            bool ok = StrategyManager.Instance != null && StrategyManager.Instance.RejectInvestment(pid, out message);
            if (mail != null) Mark(gs, mail.id, "rejected");
            if (!ok && string.IsNullOrEmpty(message)) message = "已拒绝该投资提案。";
            return true;
        }

        public static bool ApprovePartsOrder(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            EnsureMaintenanceManager();
            var mnt = MaintenanceManager.EnsureState(gs);
            var order = mnt?.orders?.Find(o => o != null && o.orderNo == mail.origin);
            if (order == null) { message = "未找到对应采购订单。"; return false; }
            if (order.urgent)
            {
                bool ok = MaintenanceManager.Instance.ConfirmOrder(order.orderNo, out message);
                Mark(gs, mail.id, "approved");
                mail.effectPending = false;
                return ok;
            }
            NextQuarter(gs.year, gs.quarter, out int ey, out int eq);
            mail.effectPending = true;
            mail.effectYear = ey;
            mail.effectQuarter = eq;
            Mark(gs, mail.id, "approved");
            float total = MaintenanceEconomics.OrderTotalWan(order.quantity, order.unitCostUsd);
            message = $"已批准航材采购 {order.orderNo}。将于第{ey}年 Q{eq} 扣款 {total:F2} 万并入库。";
            return true;
        }

        public static bool RejectPartsOrder(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            EnsureMaintenanceManager();
            bool ok = MaintenanceManager.Instance != null
                && mail != null
                && MaintenanceManager.Instance.CancelOrder(mail.origin, out message);
            if (mail != null) Mark(gs, mail.id, "rejected");
            if (!ok && string.IsNullOrEmpty(message)) message = "已拒绝该航材采购。";
            return true;
        }

        public static void OnPartsOrderFilled(GameState gs, string orderNo)
        {
            if (gs?.inbox == null || string.IsNullOrEmpty(orderNo)) return;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != "parts_order" || m.origin != orderNo) continue;
                m.effectPending = false;
                if (IsOpen(m)) Mark(gs, m.id, "approved");
            }
        }

        public static bool QueuePartsMail(GameState gs, PurchaseOrder order)
        {
            if (gs == null || order == null) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string id = "m_po_" + order.orderNo;
            if (HasMailId(gs, id))
            {
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m != null && m.id == id && (IsOpen(m) || m.effectPending)) return true;
                }
            }
            float total = MaintenanceEconomics.OrderTotalWan(order.quantity, order.unitCostUsd);
            Add(gs, new InboxMail
            {
                id = id,
                sender = "机务工程部",
                role = "航材采购",
                glyph = "机",
                kind = "alert",
                action = "parts_order",
                origin = order.orderNo,
                dest = order.partNo,
                subject = order.urgent
                    ? $"紧急：停飞缺件 {order.partNo} 请批准采购"
                    : $"缺件预警：请批准采购 {order.partNo}",
                body = $"机务工程部报告件号 {order.partNo} 库存低于安全线，已拟订单 {order.orderNo}。\n\n" +
                       $"数量 {order.quantity}　单价 {order.unitCostUsd:F0} 美元　合计 {total:F2} 万\n" +
                       $"供应商 {order.supplier}　交期 {order.leadDays} 天　{(order.urgent ? "紧急/AOG" : "常规补货")}\n\n" +
                       (order.urgent
                           ? "建议：立即批准，本季扣款入库，避免持续停飞。\n"
                           : "建议：批准后下季度扣款入库。可先点航材与供应链 或 订单确认 核对。\n") +
                       "拒绝则订单取消，缺件风险继续。",
                tag = order.urgent ? "紧急" : "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static void MaybeOfferPartsOrder(GameState gs)
        {
            if (gs == null) return;
            if (gs.year < 1 || (gs.year == 1 && gs.quarter < 2)) return;
            if (HasOpenAction(gs, "parts_order")) return;
            var mnt = MaintenanceManager.EnsureState(gs);
            if (mnt.parts.Count == 0)
            {
                mnt.parts.Add(new PartStock { partNo = "PN-ENG-001", name = "发动机单元体密封件", category = "A", quantity = 6, safetyStock = 10, unitCostUsd = 120000f });
                mnt.parts.Add(new PartStock { partNo = "PN-HYD-002", name = "液压作动筒", category = "B", quantity = 1, safetyStock = 8, unitCostUsd = 80000f });
            }
            bool shortage = false;
            for (int i = 0; i < mnt.parts.Count; i++)
                if (mnt.parts[i] != null && mnt.parts[i].quantity < mnt.parts[i].safetyStock) shortage = true;
            if (!shortage) return;
            var draft = mnt.orders.Find(o => o != null && o.status == "草稿");
            if (draft == null)
            {
                EnsureMaintenanceManager();
                // 同上：批处理/编辑器自检里 Instance 可能是空的，跳过即可（下一季还会再试），
                // 但不能让它把 ProcessPendingEffects 整条流程打断。
                if (MaintenanceManager.Instance == null) return;
                MaintenanceManager.Instance.CreateOrder("PN-HYD-002", "OEM 原厂", 6, 80000f, false, out _);
                draft = mnt.orders.Find(o => o != null && o.status == "草稿");
            }
            if (draft != null) QueuePartsMail(gs, draft);
        }

        public static bool ApplyInspection(GameState gs, InboxMail mail, int choice, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "邮件无效。"; return false; }
            EnsureSafetyManager();
            string reg = string.IsNullOrEmpty(mail.origin) ? "CAAC" : mail.origin;
            bool ok = SafetyManager.Instance.RespondInspection(reg, choice, out message);
            if (!ok) return false;
            string status = choice == 2 ? "rejected" : "approved";
            Mark(gs, mail.id, status);
            Add(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = "安全质量部",
                role = "执行回执",
                glyph = "安",
                kind = "report",
                subject = "局方检查应对已落盘",
                body = message ?? "",
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
            return true;
        }

        public static void MaybeOfferInspection(GameState gs)
        {
            MaybeOfferInspection(gs, false);
        }

        public static void MaybeOfferInspection(GameState gs, bool force)
        {
            if (gs == null) return;
            if (HasOpenAction(gs, "reg_inspect")) return;
            if (!force)
            {
                if (gs.year < 1 || (gs.year == 1 && gs.quarter < 3)) return;
                if (HasMailId(gs, "m_caac_inspect")) return;
            }
            var saf = SafetyManager.EnsureState(gs);
            char band = SafetyEconomics.RatingBand(saf.ratingScore);
            string id = HasMailId(gs, "m_caac_inspect")
                ? "m_caac_inspect_" + gs.year + gs.quarter
                : "m_caac_inspect";
            if (HasMailId(gs, id)) return;
            Add(gs, new InboxMail
            {
                id = id,
                sender = "安全质量部",
                role = "局方检查",
                glyph = "安",
                kind = "regulatory",
                action = "reg_inspect",
                origin = "CAAC",
                dest = "综合",
                subject = "CAAC 综合检查临近，请选择应对方案",
                body = $"局方将对本公司进行综合检查。当前安全评级 {band} 级（{saf.ratingScore:F0}）。\n\n" +
                       "ⓐ 全力配合：成本约 15 万，关系改善，可能暴露问题。\n" +
                       "ⓑ 重点整改后迎检：成本约 35 万，发现项减少。\n" +
                       "ⓒ 申请延期：成本约 8 万，局方关系下降，评级承压。\n\n" +
                       "请在本邮件选择，或点检查应对 打开方案对照。可同时查看局方与法规事务。",
                tag = "监管",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
        }

        public static bool QueueCompensationMail(GameState gs, string kind)
        {
            if (gs == null) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            kind = HrManager.NormalizeKind(kind);
            var hr = HrManager.EnsureState(gs);
            string id = "m_comp_" + kind + "_" + gs.year;
            if (HasMailId(gs, id))
            {
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m != null && m.id == id && (IsOpen(m) || m.effectPending)) return true;
                }
                return false;
            }
            float cost = HrManager.CompCostWan(gs, kind);
            string title = HrManager.CompTitle(kind);
            string effect = kind == "raise"
                ? "批准后立即扣款，年度薪酬池上调约 3.5%，员工与工会满意度上升。"
                : kind == "exec"
                    ? "批准后立即扣款，薪酬池增加高管激励成本，满意度小幅上升。"
                    : "批准后立即从现金划入奖金池（薪酬池总额不变），员工满意度上升。";
            Add(gs, new InboxMail
            {
                id = id,
                sender = "人力资源部",
                role = "薪酬方案",
                glyph = "人",
                kind = "report",
                action = "comp_review",
                origin = kind,
                dest = cost.ToString("F0"),
                subject = $"请审批「{title}」方案",
                body = $"人力资源部提交{title}，请 CEO 拍板。\n\n" +
                       $"现金支出　约 {cost:F0} 万\n" +
                       $"当前薪酬池　{gs.LaborCostAnnualWan:F0} 万　员工满意度 {hr.employeeSatisfaction:F0}\n" +
                       $"人力成本占营收　{HrEconomics.LaborCostRatio(gs.LaborCostAnnualWan, gs.monthlyRevenue * 12f) * 100:F0}%（目标 <25%）\n\n" +
                       effect + "\n拒绝则维持原薪酬方案，满意度与流失风险承压。\n可点绩效与薪酬 核对结构后再批。",
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static void MaybeOfferCompensation(GameState gs)
        {
            if (gs == null) return;
            if (gs.year < 1 || gs.quarter != 4) return;
            if (HasOpenAction(gs, "comp_review")) return;
            string id = "m_comp_raise_" + gs.year;
            if (HasMailId(gs, id)) return;
            QueueCompensationMail(gs, "raise");
        }

        public static bool QueueAnnualGoalsMail(GameState gs)
        {
            if (gs == null) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            if (gs.AnnualGoalsConfirmed) return false;
            string id = "m_annual_goals_" + gs.year;
            if (HasMailId(gs, id) || HasOpenAction(gs, "annual_goals")) return false;
            if (BoardManager.Instance != null)
                BoardManager.Instance.EnsureAnnualTargets();
            int n = gs.currentTargets != null ? gs.currentTargets.Count : 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"董事会请 CEO 确认第 {gs.year} 财年考核清单（共 {n} 项）。");
            sb.AppendLine();
            if (gs.currentTargets != null)
            {
                for (int i = 0; i < gs.currentTargets.Count && i < 6; i++)
                {
                    var t = gs.currentTargets[i];
                    if (t == null) continue;
                    sb.AppendLine($"· {t.ProgressLabel}");
                }
            }
            sb.AppendLine();
            sb.Append("批准即确认本财年目标。也可点年度目标 在愿景页确认。拒绝则暂缓，董事会满意度下降。");
            Add(gs, new InboxMail
            {
                id = id,
                sender = "董事会",
                role = "年度目标",
                glyph = "董",
                kind = "report",
                action = "annual_goals",
                subject = $"请确认第 {gs.year} 财年年度目标",
                body = sb.ToString().TrimEnd(),
                tag = "考核",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static void MaybeOfferAnnualGoals(GameState gs)
        {
            if (gs == null) return;
            if (gs.year < 1) return;
            if (gs.AnnualGoalsConfirmed) return;
            if (HasOpenAction(gs, "annual_goals")) return;
            QueueAnnualGoalsMail(gs);
        }

        public static bool RejectAnnualGoals(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null)
            {
                message = "邮件无效。";
                return false;
            }
            gs.boardSatisfaction = UnityEngine.Mathf.Clamp(gs.boardSatisfaction - 4f, 0f, 100f);
            BoardManager.Instance?.ApplySatisfactionDeltaToMembers(-4f);
            Mark(gs, mail.id, "rejected");
            gs.AddDecisionLog("年度目标：暂缓确认。");
            message = "已暂缓确认。可稍后在愿景「年度目标」页确认。董事会满意度 -4。";
            gs.NotifyChanged();
            return true;
        }

        public static bool QueueCapacityMail(GameState gs, int gap)
        {
            if (gs == null || gap <= 0) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            if (HasOpenAction(gs, "capacity_plan")) return true;
            string id = "m_capacity_" + gs.year + "_q" + gs.quarter;
            if (HasMailId(gs, id)) return false;
            Add(gs, new InboxMail
            {
                id = id,
                sender = "运营部",
                role = "运力规划",
                glyph = "运",
                kind = "report",
                action = "capacity_plan",
                origin = gap.ToString(),
                subject = $"运力缺口约 {gap} 架，请决策补强",
                body = $"适航运力不足以覆盖在营航网，缺口约 {gap} 架（缺口超过 20% 为红灯）。\n\n" +
                       "批准后进入购机决策会议（新机 / 二手 / 租赁）。拒绝则本季不引进，航网可能继续超负荷。\n" +
                       "可点运力规划 查看五年机队计划后再批。",
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static void MaybeOfferCapacity(GameState gs)
        {
            if (gs == null) return;
            int airworthy = FleetManager.Instance != null ? FleetManager.Instance.CountAirworthy(gs) : 0;
            int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
            int gap = CapacityPlanning.Gap(routes, airworthy);
            if (!CapacityPlanning.NeedsDecision(routes, airworthy)) return;
            QueueCapacityMail(gs, gap);
        }

        public static bool RejectCapacity(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null)
            {
                message = "邮件无效。";
                return false;
            }
            gs.boardSatisfaction = UnityEngine.Mathf.Clamp(gs.boardSatisfaction - 2f, 0f, 100f);
            Mark(gs, mail.id, "rejected");
            gs.AddDecisionLog("运力规划：本季不引进。");
            message = "已拒绝本季引进。运力缺口仍在，可稍后从机队与运力规划页再提。";
            gs.NotifyChanged();
            return true;
        }

        public static bool ApproveCompensation(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            EnsureHrManager();
            string kind = mail != null ? mail.origin : "bonus";
            bool ok = HrManager.Instance.ApplyCompensation(kind, out message);
            if (!ok) return false;
            Mark(gs, mail.id, "approved");
            Add(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = "人力资源部",
                role = "执行回执",
                glyph = "人",
                kind = "report",
                subject = HrManager.CompTitle(kind) + "已入账",
                body = message ?? "",
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
            return true;
        }

        public static bool RejectCompensation(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            EnsureHrManager();
            string kind = mail != null ? mail.origin : "bonus";
            bool ok = HrManager.Instance.RejectCompensation(kind, out message);
            if (mail != null) Mark(gs, mail.id, "rejected");
            if (!ok && string.IsNullOrEmpty(message)) message = "已拒绝该薪酬方案，薪酬池维持原状。";
            return true;
        }

        static InboxMail BuildAllianceMail(GameState gs, string id, string action, string origin, string dest,
            string subject, string body)
        {
            return new InboxMail
            {
                id = id,
                sender = "战略发展部",
                role = "联盟提案",
                glyph = "战",
                kind = "report",
                action = action,
                origin = origin,
                dest = dest,
                subject = subject,
                body = body,
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            };
        }

        /// <summary>枢纽时刻槽 + PEK–SIN 第五航权提案（定稿 n77 / P12）。</summary>
        public static void MaybeOfferRightsAndSlots(GameState gs)
        {
            if (gs == null) return;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            var stg = StrategyManager.EnsureState(gs);
            if (stg == null) return;

            string hub = string.IsNullOrEmpty(gs.hubIata) ? "PEK" : gs.hubIata;
            if ((stg.slots == null || stg.slots.Count == 0)
                && !HasMailId(gs, "m_slot_hub") && !HasOpenAction(gs, "buy_slot"))
            {
                QueueSlotDecision(gs, hub, ScheduleSlots.DepTimes[1], "m_slot_hub", out _);
                return;
            }

            if (!StrategyManager.HasRouteRight(gs, "PEK", "SIN")
                && !HasMailId(gs, "m_right_pek_sin") && !HasOpenAction(gs, "apply_right"))
            {
                QueueRightDecision(gs, "PEK", "SIN", "m_right_pek_sin", out _);
            }
        }

        /// <param name="clock">要购入的时刻槽的**具体离港时刻** `"HH:mm"`（★2026-10-15）。</param>
        public static bool QueueSlotDecision(GameState gs, string airport, string clock, string id, out string message)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪"; return false; }
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            if (HasOpenAction(gs, "buy_slot"))
            {
                message = "已有时刻槽提案在收件箱待批。";
                return false;
            }
            if (!string.IsNullOrEmpty(id) && HasMailId(gs, id))
            {
                message = "该时刻槽提案已提交过。";
                return false;
            }

            string apt = string.IsNullOrEmpty(airport) ? (gs.hubIata ?? "PEK") : airport;
            int m = ScheduleSlots.ParseClock(clock);
            if (m < 0) { message = $"时刻无效（{clock}）。时刻槽须精确到时刻，如 06:30 / 08:00。"; return false; }
            string clk = RoutePermitLogic.SlotRequestClock(ScheduleSlots.FormatClock(m));
            int b = ScheduleSlots.BandOfDep(clk);

            // ★2026-10-15：区分两条渠道（现实里它们是两码事，价钱也完全不同）。
            //   协调池申请（apply_slot）= WASG 规定的正常渠道，**免费**，排队按需分配；
            //   二级市场购入（buy_slot）= 向持槽航司买，**收费**，本函数就是这条。
            float cost = StrategyEconomics.SlotValueWan(apt, b);   // 按机场真实等级定价
            if (RoutePermitLogic.SlotPoolFull(gs, apt, b))
            {
                message = $"该机场该时段时刻池已满（{RoutePermitLogic.SlotPoolLine(gs, apt, b)}），暂不便再提申请。";
                return false;
            }
            string mailId = string.IsNullOrEmpty(id)
                ? "m_slot_" + apt + "_" + clk.Replace(":", "") + "_" + gs.year + "q" + gs.quarter : id;
            if (HasMailId(gs, mailId))
            {
                message = "本季已提交同一时刻的时刻槽提案。";
                return false;
            }

            // K19：提案必须写出「这笔钱花掉之后还剩多少个月」。
            // 为什么：批次 B 把 tier 1 新机场（KWE / TNA）带进 ★2 档，一条槽 300 万；
            // 该档开局现金 1434 万、净烧 179.5 万/月，玩家把现金花到 300 万上下时
            // 会收到 300 万的报价，信里一个字没提后果。CEO 看不到跑道就点批准，等于蒙着眼睛签字。
            // （早期记录写的「开局现金约 320 万 / 现金 325.5 万」是误读：那是底栏的季度支出
            //   3,255,000 元 = 325.5 万，而底栏 StatusBarView 并不输出金。见 K19 回归头注释。）
            // 交割期才会真正扣款（StrategyManager.AcquireSlotsAt 现金不足直接失败），
            // 所以口径必须与那条判据同源：现金 − 报价，再除以 EffectiveMonthlyBurn。
            string cashLine = FormatSlotCashImpact(gs, cost, out bool affordable, out float runwayAfter);

            Add(gs, new InboxMail
            {
                id = mailId,
                sender = "战略发展部",
                role = "航权提案",
                glyph = "战",
                kind = "report",
                action = "buy_slot",
                origin = apt,
                dest = clk,
                slotClock = clk,
                subject = $"建议购入 {apt} {clk} 时刻槽",
                body =
                    InboxLetter.Section("提案要点") +
                    InboxLetter.Bullet($"机场　{apt}（{AirportCatalog.TierLabelOf(apt)}）") +
                    InboxLetter.Bullet($"时刻　{clk}（机场当地离港时刻）—— **时刻槽精确到时刻**，"
                                       + $"06:30 与 08:00 是两条槽，不能互相顶替") +
                    InboxLetter.Bullet($"渠道　二级市场购入（向持槽航司买，需付款）") +
                    InboxLetter.Bullet($"估价　约 {cost:F0} 万美元") +
                    InboxLetter.Bullet($"现金　{cashLine}") +
                    InboxLetter.Bullet($"池况　{RoutePermitLogic.SlotPoolLineAt(gs, apt, clk)}") +
                    InboxLetter.Section("时点与影响") +
                    InboxLetter.Bullet("批准：下季度扣款交割，交割后可按该时刻加密班次") +
                    InboxLetter.Bullet("拒绝：该时刻不扩容，该时刻的班次排不进去") +
                    InboxLetter.Bullet("若不愿付费：改走**协调池申请**（收件箱「申请时刻槽」，不收费、按序分配）") +
                    InboxLetter.Bullet("两端持槽：往返班次在两个离港机场各占 1 条**对应时刻**的槽") +
                    InboxLetter.Bullet("持仓与进度见「航权与时刻槽」页") +
                    (affordable && runwayAfter < 3f
                        ? InboxLetter.Bullet($"风险　交割后跑道 {runwayAfter:F1} 个月，已跌破 3 个月危机线 —— "
                                             + "先安排融资，或改用不收费的协调池申请")
                        : ""),
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            message = $"已提交收件箱：请批准购入 {apt} {clk} 时刻槽，下季度扣款交割。";
            return true;
        }

        /// <summary>
        /// 时刻槽提案的「现金影响」一行（K19）。
        ///
        /// 单独成函数是为了能被隔离回归直接断言 —— 文案写在提案正文里，若只靠手测，
        /// 「跑道数字算错」这种错会一直躺在信里没人发现。参数与判据同源：
        /// 现金 − 报价 除以 <see cref="GameState.EffectiveMonthlyBurn"/>，与交割期
        /// <c>StrategyManager.AcquireSlotsAt</c> 的「现金不足即失败」严格对齐。
        /// </summary>
        /// <param name="canPay">交割季能否成交（现金 ≥ 报价）。</param>
        /// <param name="runwayAfter">扣款后的现金跑道（月）；无净烧钱时为 <see cref="CashRunnelCalculator.InfiniteRunwayMonths"/>。</param>
        public static string FormatSlotCashImpact(GameState gs, float cost, out bool canPay, out float runwayAfter)
        {
            canPay = false;
            runwayAfter = 0f;
            if (gs == null) return "";

            float cashAfter = gs.cash - cost;
            float burnNow = gs.EffectiveMonthlyBurn;
            runwayAfter = CashRunnelCalculator.GetRunwayMonths(cashAfter, burnNow);
            canPay = cashAfter >= 0f;

            if (!canPay)
                return $"现有 {gs.cash:F0} 万，**不足以支付**（差 {-cashAfter:F0} 万）"
                       + "；交割季无法成交，请先融资或改走免费的协调池申请";
            if (runwayAfter >= CashRunnelCalculator.InfiniteRunwayMonths - 0.1f)
                return $"现有 {gs.cash:F0} 万；按当前无净烧钱，交割扣款后约剩 {cashAfter:F0} 万";
            return $"现有 {gs.cash:F0} 万；按当前净烧钱 {burnNow:F0} 万/月，"
                   + $"交割扣款后约剩 {cashAfter:F0} 万 ≈ **{runwayAfter:F1} 个月**跑道";
        }

        /// <summary>时刻槽提案/交割单上的具体时刻（★2026-10-15：一切文案都写时刻，不写「早/中/晚/夜」）。</summary>
        public static string SlotMailClock(InboxMail m)
        {
            if (m == null) return "";
            // 一律吸附到系列时刻：旧档/旧路径可能存了 07:40 这类非系列时刻，比对与文案都要归到 07:30，
            // 否则「同一刻是否已有申请在箱」的判据（HasOpenApplySlot）会漏判。
            if (ScheduleSlots.ParseClock(m.slotClock) >= 0)
                return RoutePermitLogic.SlotRequestClock(ScheduleSlots.FormatClock(ScheduleSlots.ParseClock(m.slotClock)));
            if (ScheduleSlots.ParseClock(m.dest) >= 0)
                return RoutePermitLogic.SlotRequestClock(ScheduleSlots.FormatClock(ScheduleSlots.ParseClock(m.dest)));
            // 旧档（dest 存的是时段下标 0–3）：折成该时段峰点，别让历史邮件显示成空白
            if (int.TryParse(m.dest, out int band) && band >= 0 && band <= 3)
                return ScheduleSlots.DepTimes[band];
            return "";
        }

        public static int SlotQtyOf(InboxMail mail)
        {
            int n = mail != null ? mail.permitAttempts : 1;
            return n < 1 ? 1 : n;
        }

        /// <summary>已批准、尚未到交割季的时刻槽提案（buy_slot / apply_slot / change_slot）。</summary>
        public static List<InboxMail> PendingSlotSettlements(GameState gs)
            => PendingSettlements(gs, "buy_slot", ApplySlotAction, ChangeSlotAction);

        /// <summary>已批准、尚未到交割季的航权申请。</summary>
        public static List<InboxMail> PendingRightSettlements(GameState gs)
            => PendingSettlements(gs, "apply_right");

        static List<InboxMail> PendingSettlements(GameState gs, params string[] actions)
        {
            var list = new List<InboxMail>();
            if (gs?.inbox == null || actions == null || actions.Length == 0) return list;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || !m.effectPending) continue;
                if (!string.Equals(m.status, "approved", StringComparison.OrdinalIgnoreCase)) continue;
                bool hit = false;
                for (int a = 0; a < actions.Length; a++)
                {
                    if (string.Equals(m.action, actions[a], StringComparison.OrdinalIgnoreCase))
                    { hit = true; break; }
                }
                if (hit) list.Add(m);
            }
            return list;
        }

        public static string FormatPendingSlot(InboxMail m)
        {
            if (m == null) return "";
            string apt = string.IsNullOrEmpty(m.origin) ? "HUB" : m.origin;
            string clk = SlotMailClock(m);
            bool free = string.Equals(m.action, ApplySlotAction, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(m.action, ChangeSlotAction, StringComparison.OrdinalIgnoreCase))
                return $"{apt} {m.slotFromClock} → {clk} 改时　协调人按系列余量批复·免费"
                       + $"　待交割 · 第{m.effectYear}年 Q{m.effectQuarter}";
            return $"{apt} {clk} {SlotQtyOf(m)} 条　{(free ? "协调池分配·免费" : "二级市场购入")}　待交割 · 第{m.effectYear}年 Q{m.effectQuarter}";
        }

        public static string FormatPendingRight(InboxMail m)
        {
            if (m == null) return "";
            string o = string.IsNullOrEmpty(m.origin) ? "—" : m.origin;
            string d = string.IsNullOrEmpty(m.dest) ? "—" : m.dest;
            return $"{o}—{d}  第五航权　待交割 · 第{m.effectYear}年 Q{m.effectQuarter}";
        }

        /// <summary>
        /// 本线在途的时刻槽单（待批 / 已批待交割）列表 —— 供**批复回执**如实写出「槽在办、卡在哪一步」。
        ///
        /// ★2026-10-16 第四十二次修订：回执只说一句「缺口：缺时刻槽」还不够 —— 插槽未交割期间 CEO 会
        /// **再下同一条令**（本线已开的单不会再开第二张，`HasOpenApplySlot` 按航线判重），于是一次动静
        /// 都没有、又收一封回执。这里把在办的单子摊给他看：**这件事已经有人办了**，卡在协调人（待批）
        /// 还是卡在交割季（已批待交割），他就不必一遍遍重下。
        ///
        /// 只认 <c>slotRouteId</c> == 本线的单子（空 = 战略部自主提案，与哪天缺口无关）；
        /// 已结案（既不在待办、也不在待交割）的单子不再提。
        /// </summary>
        static string SlotInFlightLines(GameState gs, string routeId)
        {
            if (gs?.inbox == null || string.IsNullOrEmpty(routeId)) return "";
            var sb = new StringBuilder();
            int shown = 0;
            for (int i = 0; i < gs.inbox.Count && shown < 5; i++)
            {
                var m = gs.inbox[i];
                if (m == null) continue;
                if (!string.Equals(m.slotRouteId, routeId, StringComparison.OrdinalIgnoreCase)) continue;
                bool isSlot = m.action == "buy_slot" || m.action == ApplySlotAction || m.action == ChangeSlotAction;
                if (!isSlot) continue;
                if (!IsOpen(m) && !m.effectPending) continue;

                string apt = string.IsNullOrEmpty(m.origin) ? (gs.hubIata ?? "HUB") : m.origin;
                string clk = SlotMailClock(m);
                bool changeMail = m.action == ChangeSlotAction;
                string what = changeMail
                    ? $"{apt} {m.slotFromClock} → {clk} 改时（协调人按系列余量批复）"
                    : $"{apt} {clk} {SlotQtyOf(m)} 条"
                      + (m.action == ApplySlotAction ? "（协调池分配·免费）" : "（二级市场购入）");
                string step;
                if (IsOpen(m)) step = "待您批准";
                else if (m.effectPending) step = $"已批准·待交割（第{m.effectYear}年 Q{m.effectQuarter}）";
                else continue;                sb.Append(InboxLetter.Bullet($"{step}　{what}"));
                shown++;
            }
            if (shown == 0) return "";
            sb.Append(InboxLetter.Bullet("交割到账后运营部自动重排本线并另行呈报，无需重下这条指令。"));
            return sb.ToString();
        }

        /// <summary>
        /// 运力缺口里的缺槽波次 → 按「机场 × **具体时刻**」汇总，发协调池申请邮件（**每个缺槽停站各一张**）。
        /// ★2026-10-15：由「按时段归集」改为「按时刻归集」—— 缺口是 07:30 这一刻缺槽，
        /// 邮件就必须申请 **07:30** 这一条，否则交割回 08:00 的槽，缺口一点没补上。
        ///
        /// ★2026-10-16：由「每波只办一端」改为「**逐停站各办一张**」。一趟往返要办的槽是
        /// 逐个停站的（本场端 1 对、对端 1 对、经停站一趟往返停两次故 2 对），而排班闸门
        /// <see cref="RoutePermitLogic.CanScheduleClock"/> 每个协调停站都要求 —— 只办一端等于只办一半：
        /// 批准入账后本刻仍排不出班，CEO 的「增加航班量」还是没生效。
        ///
        /// 旧口径只读 <c>WaveShortage.slotAirport</c>，而那是 <see cref="RoutePermitLogic.TryShortClock"/>
        /// 返回的**第一个**缺端；本场端在需求列表里排下标 0、且新增波次在本场必然没有持仓，
        /// 于是「只办一端」在实际运行中恒定表现为「只办起飞机场」——对端一次都没被提起过。
        /// </summary>
        public static void QueueSlotApplications(GameState gs, RouteData route, List<OpsScheduleLogic.WaveShortage> shortages,
                                                 int delayHours = 0)
        {
            if (gs == null || route == null || shortages == null || shortages.Count == 0) return;
            var order = new List<string>();
            var counts = new Dictionary<string, int>();
            var apts = new Dictionary<string, string>();
            var clocks = new Dictionary<string, string>();
            var flights = new Dictionary<string, string>();
            var stations = new Dictionary<string, string>();
            for (int i = 0; i < shortages.Count; i++)
            {
                var s = shortages[i];
                if (s == null || s.kind != "no_slot") continue;

                int m = ScheduleSlots.ParseClock(s.dep);
                if (m < 0) m = ScheduleSlots.ParseClock(ScheduleSlots.DepTimes[Mathf.Clamp(s.slotBand, 0, 3)]);
                // ★2026-10-15：缺口时刻是 5 分钟粒度的（飞机晚于槽 → 07:40），而槽只在 30 分系列网格上。
                // 申请时刻必须吸附到系列时刻，否则会开出一张「申请 07:40」的空单（买不到对应系列）。
                string flightClk = RoutePermitLogic.SlotRequestClock(ScheduleSlots.FormatClock(m));

                // 本波**逐站**自问：这一刻到底缺哪几站的槽？（缺几站开几张单，一站不缺就不开）
                // ★2026-10-16：经停航线的经停站一趟往返**缺两条**（去程一次、回程一次），本循环天然各开一张。
                var ends = RoutePermitLogic.WaveEndNeeds(route, s.dep);
                for (int k = 0; k < ends.Count; k++)
                {
                    // ★2026-10-16 第四十一次修订：本端缺槽、而本线在该站**已有一条槽**、只是时刻滑点
                    //   → 现实里走「时刻变更申请」（WASG 9.1.3），不是把缺的那条当新槽再申请一遍。
                    //   协调人按目标系列余量批驳：余量为 0 → 连改时也批不下来，如实汇报，不开空单。
                    if (RoutePermitLogic.TryTimeChange(gs, route, ends[k], out _, out string cFrom, out string cTo, out bool cRoom))
                    {
                        if (cRoom)
                            QueueChangeSlot(gs, ends[k].airport, cFrom, cTo, 1,
                                ChangeSlotMailId(gs, route, ends[k].airport, cTo), out _,
                                SlotChangeReason(route, ends[k], cFrom, cTo), route.routeId, flightClk,
                                RoutePermitLogic.LegName(ends[k]) + "　" + RoutePermitLogic.LegClocks(ends[k]),
                                delayHours);
                        continue;
                    }

                    // 已有持仓可飞 / 祖父条款保住 / 非协调机场 / 时段池已满 → 都不该开单（判据与页面档位同源）
                    if (!RoutePermitLogic.CanApplyEnd(gs, route, ends[k])) continue;
                    string apt = ends[k].airport;
                    string clk = RoutePermitLogic.SlotRequestClock(RoutePermitLogic.TimeOf(ends[k]));
                    string key = apt.ToUpperInvariant() + "|" + clk;
                    if (!counts.ContainsKey(key))
                    {
                        counts[key] = 0; order.Add(key);
                        apts[key] = apt; clocks[key] = clk; flights[key] = flightClk;
                        stations[key] = RoutePermitLogic.LegName(ends[k]) + "　" + RoutePermitLogic.LegClocks(ends[k]);
                    }
                    counts[key]++;
                }
            }
            for (int k = 0; k < order.Count; k++)
            {
                string key = order[k];
                string apt = apts[key];
                string clk = clocks[key];
                QueueApplySlots(gs, apt, clk, counts[key],
                    ApplySlotMailId(gs, route, apt, clk), out _,
                    SlotApplyReason(route, apt, clk, flights[key], stations[key]), route.routeId, flights[key],
                    delayHours);
            }
        }

        /// <summary>
        /// 协调池申请单的单号。★2026-10-16：**按航线**编号 —— 两条航线同一刻各缺各的槽，
        /// 是两件事、两张单；旧单号只含「机场+时刻」，第二条航线的那张会被 <see cref="HasMailId"/>
        /// 判成重号而**静默丢弃**（调用处 `out _` 连原因都没留下）。
        /// </summary>
        static string ApplySlotMailId(GameState gs, RouteData route, string airport, string clock)
            => "m_apply_slot_" + (route == null || string.IsNullOrEmpty(route.routeId) ? "" : route.routeId + "_")
               + airport + "_" + clock.Replace(":", "") + "_" + gs.year + "q" + gs.quarter;

        /// <summary>
        /// 申请单上的「缘由」一行：写清是哪条线、哪一刻的往返、缺的是**哪一个停站**的哪一对时刻。
        /// ★2026-10-16：停站名由调用方给出（<see cref="RoutePermitLogic.LegName"/>）——
        /// 旧版自己按「机场 == destination ？」二分，经停端的单子会被写成「去程离港端」。
        /// </summary>
        static string SlotApplyReason(RouteData route, string airport, string clock, string flightClock, string stationLabel)
        {
            if (route == null) return null;
            string leg = string.IsNullOrEmpty(stationLabel) ? "停站" : stationLabel;
            return $"{route.origin}—{route.destination} 运力指令：{flightClock} 起飞的往返缺{leg}的时刻槽"
                   + $"（{airport} {clock}）。各停站槽齐备后运营部自动重排本线并呈报加机。";
        }

        /// <summary>
        /// 申请 N 条**指定时刻**的时刻槽：走**协调池**（WASG 规定的正常渠道），**不收费**，
        /// 批准后下季交割。reason = 为哪条航线/哪个机号而提（可不填）。
        ///
        /// ★2026-10-15：旧版这条渠道也照价扣款（与二级市场同价），等于对「排队分到的槽」
        /// 又收了一道钱；现实里协调人分配是不收费的，付费只出现在航司之间的二级市场交易。
        /// </summary>
        public static bool QueueApplySlots(GameState gs, string airport, string clock, int count, string id, out string message,
            string reason = null, string routeId = null, string flightClock = null, int delayHours = 0)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪"; return false; }
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string apt = string.IsNullOrEmpty(airport) ? (gs.hubIata ?? "PEK") : airport;
            int m = ScheduleSlots.ParseClock(clock);
            if (m < 0) { message = $"时刻无效（{clock}）。时刻槽须精确到时刻，如 06:30 / 08:00。"; return false; }
            string clk = RoutePermitLogic.SlotRequestClock(ScheduleSlots.FormatClock(m));
            int b = ScheduleSlots.BandOfDep(clk);
            int n = count < 1 ? 1 : count;
            if (RoutePermitLogic.SlotPoolFull(gs, apt, b)
                && RoutePermitLogic.FreeSlotsAt(gs, apt, clk) <= 0)
            {
                message = $"该机场该时刻无可用槽、所在时段池也已满（{RoutePermitLogic.SlotPoolLineAt(gs, apt, clk)}），暂不便再提申请。";
                return false;
            }
            string mailId = string.IsNullOrEmpty(id)
                ? "m_apply_slot_" + apt + "_" + clk.Replace(":", "") + "_" + gs.year + "q" + gs.quarter : id;
            if (HasMailId(gs, mailId) || HasOpenApplySlot(gs, apt, clk, routeId))
            {
                message = "同一航线的同一时刻已有时刻槽申请在收件箱。";
                return false;
            }

            float unit = StrategyEconomics.SlotValueWan(apt, b);
            Enqueue(gs, new InboxMail
            {
                id = mailId,
                sender = "战略发展部",
                role = "航权提案",
                glyph = "战",
                kind = "report",
                action = ApplySlotAction,
                origin = apt,
                dest = clk,
                slotClock = clk,
                permitAttempts = n,
                subject = n > 1
                    ? $"建议向协调池申请 {apt} {clk} 时刻槽 {n} 条"
                    : $"建议向协调池申请 {apt} {clk} 时刻槽",
                body =
                    InboxLetter.Section("提案要点") +
                    InboxLetter.Bullet($"机场　{apt}（{AirportCatalog.TierLabelOf(apt)}）") +
                    InboxLetter.Bullet($"时刻　{clk}（机场当地离港时刻）—— **时刻槽精确到时刻**，"
                                       + $"申请的就是 {clk} 这一条，交割后也只有这一刻能飞") +
                    InboxLetter.Bullet($"渠道　机场协调人问询/协调池分配（WASG 正常渠道，**不收费**）") +
                    InboxLetter.Bullet($"数量　{n} 条（**一个停站一对**：本场端 1 对、对端 1 对；经停站一趟往返停两次，故要 2 对）") +
                    InboxLetter.Bullet($"市场价参考　约 {unit * n:F0} 万美元（若走二级市场购入需付此价；协调池分配不收）") +
                    InboxLetter.Bullet($"池况　{RoutePermitLogic.SlotPoolLineAt(gs, apt, clk)}") +
                    (string.IsNullOrEmpty(reason) ? "" : InboxLetter.Section("缘由") + InboxLetter.Bullet(reason)) +
                    InboxLetter.Section("时点与影响") +
                    InboxLetter.Bullet("批准：下季度交割入账，交割后方可按该时刻加密班次") +
                    InboxLetter.Bullet("拒绝：该时刻继续缺槽，运营部无法在该时刻加班") +
                    InboxLetter.Bullet("若等不及排队：可改走二级市场购入（收件箱「购买时刻槽」，需付费）") +
                    InboxLetter.Bullet("持仓与进度见「航权与时刻槽」页"),
                tag = "提案",
                slotReason = reason ?? "",
                slotRouteId = routeId ?? "",
                slotFlightClock = flightClock ?? "",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            }, delayHours);
            message = $"已提交收件箱：请批准向协调池申请 {apt} {clk} 时刻槽 {n} 条（免费），下季度交割。";
            return true;
        }

        /// <summary>
        /// 收件箱里是否已有**同一件事**的申请单在办（待批或待交割）。
        /// ★2026-10-16：给了 <paramref name="routeId"/> 就**按航线**判重 —— 两条不同航线同一刻各缺各的槽，
        /// 是两件事，各开各的单；旧口径全局判重会把第二条航线那张静默吞掉。
        /// 不传 routeId 时保持原全局语义（旧档邮件、自测里的裸调用）。
        /// </summary>
        static bool HasOpenApplySlot(GameState gs, string airport, string clock, string routeId = null)
        {
            return HasOpenSlotMail(gs, ApplySlotAction, airport, clock, routeId);
        }

        /// <summary>同上，认任意一种时刻槽单（申请 / 改时）：按「机场 × 目标时刻 × 本线」判重。</summary>
        static bool HasOpenSlotMail(GameState gs, string action, string airport, string clock, string routeId)
        {
            if (gs?.inbox == null) return false;
            string clk = ScheduleSlots.FormatClock(ScheduleSlots.ParseClock(clock));
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != action) continue;
                if (!IsOpen(m) && !m.effectPending) continue;
                if (!string.Equals(m.origin, airport, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(SlotMailClock(m), clk, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrEmpty(routeId)
                    && !string.Equals(m.slotRouteId, routeId, StringComparison.OrdinalIgnoreCase)) continue;
                return true;
            }
            return false;
        }

        /// <summary>改时单的单号（与申请单同构，按**航线**编号，避免两条线同名互吞）。</summary>
        static string ChangeSlotMailId(GameState gs, RouteData route, string airport, string toClock)
            => "m_change_slot_" + (route == null || string.IsNullOrEmpty(route.routeId) ? "" : route.routeId + "_")
               + airport + "_" + (toClock ?? "").Replace(":", "") + "_" + gs.year + "q" + gs.quarter;

        /// <summary>改时单的「缘由」一行：写明是哪条线、哪一站、把哪一刻改到哪一刻（含差值与容差口径）。</summary>
        static string SlotChangeReason(RouteData route, RoutePermitLogic.SlotNeed need, string fromClock, string toClock)
        {
            if (route == null) return null;
            int d = ScheduleSlots.ClockDiff(fromClock, toClock);
            return $"{route.origin}—{route.destination} 运力指令：{RoutePermitLogic.LegName(need)}"
                   + $"（{need.airport}）需要 {toClock}，而本线在该站持有的是 {fromClock}"
                   + $"（差 {d} 分，超 {ScheduleSlots.SlotToleranceMinutes} 分容差）—— 提请时刻变更。";
        }

        /// <summary>
        /// 开一张**时刻变更（改时）申请**单：把本线在该站已有的 `fromClock` 请求改为 `toClock`。
        ///
        /// 现实口径（★2026-10-16 第四十一次修订，CEO 第十八次问询衍生）：
        /// - 这是**持槽后**的动作，不是新申请 —— WASG 9.1.3「necessary slot adjustments」、
        ///   8.10.1（槽不绑航线/航班号，可变更）、8.7.1(e)（系列 = 同一或**约同一**时刻 → 15 分容差）；
        /// - 协调人按**目标系列余量**批驳：余量 0 就驳（此时连空单都不该开，调用处已先问 `poolRoom`）；
        /// - **不收费**：改时走的仍是协调渠道，二级市场才是花钱的地方。
        /// </summary>
        public static bool QueueChangeSlot(GameState gs, string airport, string fromClock, string toClock, int count,
            string id, out string message, string reason = null, string routeId = null, string flightClock = null,
            string stationLabel = null, int delayHours = 0)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪"; return false; }
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string apt = string.IsNullOrEmpty(airport) ? (gs.hubIata ?? "PEK") : airport;
            int fm = ScheduleSlots.ParseClock(fromClock);
            int tm = ScheduleSlots.ParseClock(toClock);
            if (fm < 0 || tm < 0)
            {
                message = $"改时需要「原时刻 → 目标时刻」两个有效时刻（收到 {fromClock} → {toClock}）。";
                return false;
            }
            string from = RoutePermitLogic.SlotRequestClock(ScheduleSlots.FormatClock(fm));
            string to = RoutePermitLogic.SlotRequestClock(ScheduleSlots.FormatClock(tm));
            int d = ScheduleSlots.ClockDiff(from, to);
            if (d <= ScheduleSlots.SlotToleranceMinutes)
            {
                // 容差内本就算同一条槽（闸门也已放行）—— 不需要改时，更不能凭空多占容量
                message = $"{apt} {from} 与 {to} 相差 {d} 分（≤ 容差 {ScheduleSlots.SlotToleranceMinutes} 分），"
                          + "协调人视为同一系列，无需变更，该槽照旧可用。";
                return false;
            }
            int band = ScheduleSlots.BandOfDep(to);
            if (RoutePermitLogic.SlotsAvailable(gs, apt, band) <= 0)
            {
                message = $"该机场该系列无可分配余量（{RoutePermitLogic.SlotPoolLineAt(gs, apt, to)}），改时也无法批准。";
                return false;
            }
            string mailId = string.IsNullOrEmpty(id) ? ChangeSlotMailId(gs, null, apt, to) : id;
            if (HasMailId(gs, mailId) || HasOpenSlotMail(gs, ChangeSlotAction, apt, to, routeId))
            {
                message = "同一航线的同一刻改时申请已在本线收件箱。";
                return false;
            }
            int n = count < 1 ? 1 : count;
            Enqueue(gs, new InboxMail
            {
                id = mailId,
                sender = "战略发展部",
                role = "航权提案",
                glyph = "战",
                kind = "report",
                action = ChangeSlotAction,
                origin = apt,
                dest = to,
                slotClock = to,
                slotFromClock = from,
                permitAttempts = n,
                subject = $"{apt} 时刻变更申请：{from} → {to}",
                body =
                    InboxLetter.Section("提案要点") +
                    InboxLetter.Bullet($"机场　{apt}（{AirportCatalog.TierLabelOf(apt)}）") +
                    InboxLetter.Bullet($"变更　**{from} → {to}**（差 {d} 分，超 {ScheduleSlots.SlotToleranceMinutes} 分容差）") +
                    InboxLetter.Bullet($"性质　**时刻变更（改时）**：本线在该站已持有一条槽，不是新申请 —— "
                                       + "WASG 9.1.3 要求航司对已分配的时刻「及时提出必要调整」") +
                    InboxLetter.Bullet($"停站　{(string.IsNullOrEmpty(stationLabel) ? "—" : stationLabel)}") +
                    InboxLetter.Bullet($"渠道　机场协调人批复（**不收费**，也不占用新容量）") +
                    InboxLetter.Bullet($"批复口径　目标系列**有余量即批**；容量已满则驳（"
                                       + $"{RoutePermitLogic.SlotPoolLineAt(gs, apt, to)}）") +
                    (string.IsNullOrEmpty(reason) ? "" : InboxLetter.Section("缘由") + InboxLetter.Bullet(reason)) +
                    InboxLetter.Section("时点与影响") +
                    InboxLetter.Bullet("批准：下季度协调人批复；批了则原槽改为该时刻，运营部自动重排本线") +
                    InboxLetter.Bullet("被驳：原槽保留，该需求波仍排不出 —— 运营部会如实回报，不静默放行") +
                    InboxLetter.Bullet("仓位与进度见「航权与时刻槽」页"),
                tag = "提案",
                slotReason = reason ?? "",
                slotRouteId = routeId ?? "",
                slotFlightClock = flightClock ?? "",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            }, delayHours);
            message = $"已提交收件箱：请批准向协调人提 {apt} 的时刻变更（{from} → {to}，免费）。";
            return true;
        }

        public static bool QueueRightDecision(GameState gs, string origin, string dest, string id, out string message)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪"; return false; }
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            if (HasOpenAction(gs, "apply_right"))
            {
                message = "已有航权申请在收件箱待批。";
                return false;
            }
            string o = string.IsNullOrEmpty(origin) ? "PEK" : origin;
            string d = string.IsNullOrEmpty(dest) ? "SIN" : dest;
            string mailId = string.IsNullOrEmpty(id) ? "m_right_" + o + "_" + d + "_" + gs.year + "q" + gs.quarter : id;
            if (HasMailId(gs, mailId))
            {
                message = "该航权申请已提交过。";
                return false;
            }

            Add(gs, new InboxMail
            {
                id = mailId,
                sender = "战略发展部",
                role = "航权提案",
                glyph = "战",
                kind = "report",
                action = "apply_right",
                origin = o,
                dest = d,
                subject = $"建议申请 {o}—{d} 第五航权",
                body =
                    InboxLetter.Section("提案要点") +
                    InboxLetter.Bullet($"航线　{o} — {d}（第五航权）") +
                    InboxLetter.Bullet($"成本　政府关系点数约 {15f + 5 * 2f:F0} 点") +
                    InboxLetter.Bullet("评分　0.4×经济性 + 0.25×投资承诺 + 0.2×政府关系 + 0.15×战略契合") +
                    InboxLetter.Section("时点与影响") +
                    InboxLetter.Bullet("批准：下季度办结，航权额度记入持仓") +
                    InboxLetter.Bullet("拒绝：维持现有航权网络") +
                    InboxLetter.Bullet("持仓见「航权与时刻槽」页；国际航线取证前仍不得开航"),
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            message = "已提交收件箱：请批准航权申请，下季度消耗关系点并获批。";
            return true;
        }

        public static string FormatUnionMailBody(GameState gs)
        {
            var hr = HrManager.EnsureState(gs);
            var u = hr?.union;
            if (u == null) return "工会数据不可用。";
            return $"工会满意度 {u.satisfaction:F0} · 罢工倒计时 {u.strikeCountdown} 月 · CBA 到期 {u.cbaExpiresIn} 月 · 罢工风险 {hr.strikeRisk:F0}\n\n" +
                "诉求列表：加薪 8% · 改善排班 · 增加年假 · 提高夜班补贴\n\n" +
                "让步：成本高 / 满意度↑ / 风险↓\n" +
                "强硬：成本低 / 满意度↓ / 风险↑\n" +
                "拖延：折中（成本与风险居中）\n\n" +
                "请直接在本邮件选择姿态，结果回写员工满意度与准点率联动，并预警停飞风险。";
        }

        public static void CompleteUnionMail(string mailId, string summary)
        {
            var gs = GameState.Instance;
            if (gs == null || string.IsNullOrEmpty(mailId)) return;
            Mark(gs, mailId, "approved");
            Add(gs, new InboxMail
            {
                id = "fx_" + mailId + "_" + gs.year + gs.quarter,
                sender = "人力资源部",
                role = "执行回执",
                glyph = "人",
                kind = "report",
                subject = "工会谈判已落盘",
                body = summary ?? "",
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        /// <summary>0让步 1强硬 2拖延。与 n37 工会谈判桌同一套 HrManager.NegotiateUnion。</summary>
        public static bool ApplyUnion(GameState gs, InboxMail mail, int choice, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "邮件无效。"; return false; }
            EnsureHrManager();
            bool ok = HrManager.Instance.NegotiateUnion(choice, out message);
            if (!ok) return false;
            string status = choice == 0 ? "approved" : choice == 1 ? "rejected" : "deferred";
            Mark(gs, mail.id, status);
            Add(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = "人力资源部",
                role = "执行回执",
                glyph = "人",
                kind = "report",
                subject = "工会谈判已落盘",
                body = message ?? "",
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
            return true;
        }

        public static bool RejectUnion(GameState gs, InboxMail mail, out string message)
        {
            return ApplyUnion(gs, mail, 1, out message);
        }

        /// <summary>CBA 将到期或劳资压力高时发工会谈判邮件（定稿 n37 / P13）。</summary>
        public static void MaybeOfferUnion(GameState gs)
        {
            if (gs == null) return;
            if (gs.year <= 1 && gs.quarter <= 1) return;
            if (HasOpenAction(gs, "union_talk")) return;
            string id = "m_union_" + gs.year + "q" + gs.quarter;
            if (HasMailId(gs, id)) return;

            var hr = HrManager.EnsureState(gs);
            if (hr?.union == null) return;
            bool due = hr.union.cbaExpiresIn <= 9
                || hr.strikeRisk >= 40f
                || hr.union.satisfaction < 55f;
            if (!due) return;

            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            Add(gs, new InboxMail
            {
                id = id,
                sender = "人力资源部",
                role = "劳资提案",
                glyph = "人",
                kind = hr.strikeRisk >= 50f ? "alert" : "report",
                action = "union_talk",
                subject = hr.union.cbaExpiresIn <= 6
                    ? "集体协议即将到期，请拍板谈判姿态"
                    : "工会满意度承压，建议启动谈判",
                body = FormatUnionMailBody(gs),
                tag = hr.strikeRisk >= 50f ? "紧急" : "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
        }

        /// <summary>无对冲合约时发一封请示：批准即开保本稳妥仓。</summary>
        public static void MaybeOfferFuelHedge(GameState gs)
        {
            if (gs == null || gs.gameOver) return;
            if (HasOpenAction(gs, FuelHedgeAction) || HasMailId(gs, "m_fuel_hedge")) return;
            if (gs.hedgeContracts != null && gs.hedgeContracts.Count > 0) return;

            gs.hasSeenHedgeTutorial = true;
            string subject = "关于办理燃油对冲的请示";
            string core =
                InboxLetter.Section("事由") +
                InboxLetter.Bullet($"当前航油 ${gs.fuelPricePerTon:F0} / 吨") +
                InboxLetter.Bullet("尚无生效对冲合约，油价上涨将直接冲击利润") +
                InboxLetter.Section("建议方案") +
                InboxLetter.Bullet("保本稳妥：对冲比例 30% · 期限 6 个月 · 固定价锁定") +
                InboxLetter.Bullet("批准后由财务立即开仓；拒绝则维持现货敞口");
            Add(gs, new InboxMail
            {
                id = "m_fuel_hedge",
                sender = CfoSender,
                role = "对冲请示",
                glyph = "财",
                kind = "report",
                action = FuelHedgeAction,
                subject = subject,
                body = InboxLetter.Frame(gs, CfoSender, "对冲请示", subject, core,
                    "请您在本函批复。财务模块只列示账目与事项进程，决策一律在收件箱。"),
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
        }

        public static bool ApproveFuelHedge(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null)
            {
                message = "邮件无效。";
                return false;
            }
            gs.hasSeenHedgeTutorial = true;
            if (HedgeManager.Instance == null)
            {
                message = "对冲系统未就绪。";
                return false;
            }
            if (!TryOpenHedgeFromMail(gs, mail, out message))
                return false;
            Mark(gs, mail.id, "approved");
            Schedule(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = CfoSender,
                role = "执行回执",
                glyph = "财",
                kind = "report",
                action = FinanceProgressAction,
                subject = "燃油对冲已开仓",
                body = InboxLetter.Frame(gs, CfoSender, "执行回执", "燃油对冲已开仓", message,
                    "本封为执行回执，无需批复。",
                    gs.SimUtc.Add(GameState.LocalUtcOffset).ToString("yyyy年MM月dd日"),
                    needDecision: false),
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ReplyReady(gs.SimUtc));
            return true;
        }

        public static bool RejectFuelHedge(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null)
            {
                message = "邮件无效。";
                return false;
            }
            gs.hasSeenHedgeTutorial = true;
            Mark(gs, mail.id, "rejected");
            message = "已搁置燃油对冲，维持现货敞口。后续可在对冲工作台自行开仓。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
            Schedule(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_rej_" + gs.year + gs.quarter,
                sender = CfoSender,
                role = "执行回执",
                glyph = "财",
                kind = "report",
                action = FinanceProgressAction,
                subject = "燃油对冲请示已搁置",
                body = message,
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ReplyReady(gs.SimUtc));
            return true;
        }

        static bool TryOpenHedgeFromMail(GameState gs, InboxMail mail, out string message)
        {
            float ratio = 0.30f;
            int term = 6;
            string lockType = HedgeLockTypes.Fixed;
            float floor = 0.05f;
            float cap = 0.05f;
            if (mail != null && !string.IsNullOrEmpty(mail.dest))
            {
                var p = mail.dest.Split('|');
                if (p.Length >= 3)
                {
                    lockType = p[0];
                    float.TryParse(p[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out ratio);
                    int.TryParse(p[2], out term);
                    if (p.Length >= 5)
                    {
                        float.TryParse(p[3], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out floor);
                        float.TryParse(p[4], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out cap);
                    }
                }
            }
            return HedgeManager.Instance.TryOpenHedge(ratio, term, lockType, floor, cap, out message);
        }

        public static bool OfferFuelHedgeProposal(GameState gs, float ratio, int termMonths, string lockType,
            float floorOffset, float capOffset, out string message)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪。"; return false; }
            if (HasOpenAction(gs, FuelHedgeAction))
            {
                message = "对冲请示已在收件箱，请先批复。";
                return false;
            }
            string dest = $"{lockType}|{ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{termMonths}|{floorOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{capOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            string subject = "关于办理燃油对冲的请示";
            string core = InboxLetter.Section("方案") +
                InboxLetter.Bullet($"{HedgeLockTypes.DisplayName(lockType)} · 比例 {ratio * 100f:F0}% · {termMonths} 个月") +
                InboxLetter.Bullet($"现价 ${gs.fuelPricePerTon:F0} / 吨");
            Add(gs, new InboxMail
            {
                id = "m_fuel_hedge_" + gs.year + "q" + gs.quarter + "_" + UnityEngine.Random.Range(10, 99),
                sender = CfoSender,
                role = "对冲请示",
                glyph = "财",
                kind = "report",
                action = FuelHedgeAction,
                dest = dest,
                subject = subject,
                body = InboxLetter.Frame(gs, CfoSender, "对冲请示", subject, core,
                    "请您在本函批复。财务页只编制方案，不直接开仓。"),
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            message = "已呈请 CEO，请示已发到收件箱。";
            return true;
        }

        public static bool OfferAnnualBudget(GameState gs, out string message)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪。"; return false; }
            if (HasOpenAction(gs, AnnualBudgetAction))
            {
                message = "年度预算请示已在收件箱。";
                return false;
            }
            if (gs.budgetApproved || gs.budgetSubmitted)
            {
                message = gs.budgetApproved ? "本财年预算已获批准。" : "预算已提交董事会。";
                return false;
            }
            FinanceManager.Instance?.EnsureDefaultBudget(gs);
            float total = FinanceManager.BudgetTotal(gs);
            int votes = FinanceManager.EstimateVotes(gs);
            string subject = "关于提交年度预算的请示";
            var sb = new System.Text.StringBuilder();
            sb.Append(InboxLetter.Section("草案合计"));
            sb.Append(InboxLetter.Bullet($"{total:F0} 万美元 · 预估董事会 {votes}/{FinanceEconomics.BoardSeats} 票"));
            if (gs.budgetItems != null)
            {
                sb.Append(InboxLetter.Section("分项"));
                for (int i = 0; i < gs.budgetItems.Count; i++)
                    sb.Append(InboxLetter.Bullet($"{gs.budgetItems[i].deptName}　{gs.budgetItems[i].amountWan:F0} 万"));
            }
            Add(gs, new InboxMail
            {
                id = "m_budget_" + gs.year,
                sender = CfoSender,
                role = "预算请示",
                glyph = "财",
                kind = "report",
                action = AnnualBudgetAction,
                subject = subject,
                body = InboxLetter.Frame(gs, CfoSender, "预算请示", subject, sb.ToString(),
                    "请您批复后，再提交董事会表决。"),
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            message = "已呈请 CEO，年度预算请示已发到收件箱。";
            return true;
        }

        public static bool ApproveAnnualBudget(GameState gs, InboxMail mail, out string message)
        {
            if (FinanceManager.Instance == null)
            {
                message = "财务系统未就绪。";
                return false;
            }
            bool ok = FinanceManager.Instance.SubmitBudget(out message);
            Mark(gs, mail.id, "approved");
            return ok;
        }

        public static void MaybeOfferAnnualBudget(GameState gs)
        {
            if (gs == null || gs.gameOver || gs.budgetApproved || gs.budgetSubmitted) return;
            OfferAnnualBudget(gs, out _);
        }

        public static void MaybeOfferCarbonBuy(GameState gs)
        {
            if (gs == null || gs.gameOver) return;
            if (HasOpenAction(gs, CarbonBuyAction)) return;
            FinanceManager.Instance?.RefreshEmissions(gs);
            float need = FinanceManager.CarbonLiability(gs);
            if (need < 0.5f) return;
            string id = "m_carbon_" + gs.year + "q" + gs.quarter;
            if (HasMailId(gs, id)) return;
            const float tons = 5000f;
            string subject = "关于采购碳配额的请示";
            Add(gs, new InboxMail
            {
                id = id,
                sender = CfoSender,
                role = "碳配额请示",
                glyph = "财",
                kind = "report",
                action = CarbonBuyAction,
                dest = tons.ToString("F0"),
                subject = subject,
                body = InboxLetter.Frame(gs, CfoSender, "碳配额请示", subject,
                    InboxLetter.Bullet($"碳负债约 {need:F1} 万") +
                    InboxLetter.Bullet($"建议采购 {tons:F0} 吨 · 参考碳价 ${gs.carbonPricePerTon:F0}/吨"),
                    "请您在本函批复。财务页只列示配额，不直接下单。"),
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
        }

        public static bool ApproveCarbonBuy(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (FinanceManager.Instance == null) { message = "财务系统未就绪。"; return false; }
            float tons = 5000f;
            if (mail != null && !string.IsNullOrEmpty(mail.dest))
                float.TryParse(mail.dest, out tons);
            tons = Mathf.Max(100f, tons);
            bool ok = FinanceManager.Instance.PurchaseCarbon(tons, out message);
            if (ok) Mark(gs, mail.id, "approved");
            return ok;
        }

        public static bool ApproveRestructuring(GameState gs, InboxMail mail, out string message)
        {
            if (FinanceManager.Instance == null) { message = "财务系统未就绪。"; return false; }
            bool ok = FinanceManager.Instance.FileRestructuring(out message);
            if (ok)
            {
                Mark(gs, mail.id, "approved");
                CompleteFinanceCrisisMail(mail.id, message);
            }
            return ok;
        }

        public static bool OfferFinanceLoan(GameState gs, int kind, out string message)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪。"; return false; }
            if (HasOpenAction(gs, FinanceLoanAction))
            {
                message = "融资请示已在收件箱。";
                return false;
            }
            string label = kind == 2 ? "高息过桥" : kind == 3 ? "股东增资" : "银行贷款";
            string subject = "关于办理紧急融资的请示";
            Add(gs, new InboxMail
            {
                id = "m_loan_" + gs.year + "q" + gs.quarter + "_" + kind,
                sender = CfoSender,
                role = "融资请示",
                glyph = "财",
                kind = "alert",
                action = FinanceLoanAction,
                dest = kind.ToString(),
                subject = subject,
                body = InboxLetter.Frame(gs, CfoSender, "融资请示", subject,
                    InboxLetter.Bullet($"建议方案：{label}") +
                    InboxLetter.Bullet($"现金 {gs.cash:F0} 万 · {CashRunnelCalculator.FormatPlayerLabel(CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn))}"),
                    "请您在本函批复。财务页不直接提款。"),
                tag = "紧急",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
            message = "已呈请 CEO，融资请示已发到收件箱。";
            return true;
        }

        public static bool ApproveFinanceLoan(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (LoanManager.Instance == null) { message = "融资系统未就绪。"; return false; }
            int kind = ParseFinanceKind(mail);
            bool ok = kind == 2 ? LoanManager.Instance.TryBridgeLoan(out message)
                : kind == 3 ? LoanManager.Instance.TryEquityRaise(out message)
                : LoanManager.Instance.TryBankLoan(out message);
            if (ok) Mark(gs, mail.id, "approved");
            return ok;
        }

        public static bool OfferFleetBuy(GameState gs, string typeId, int count, string source, out string message)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪。"; return false; }
            if (HasOpenAction(gs, FleetBuyAction))
            {
                message = "购机请示已在收件箱。";
                return false;
            }
            string subject = $"关于引进 {typeId} ×{count} 的请示";
            Add(gs, new InboxMail
            {
                id = "m_fleet_" + gs.year + "q" + gs.quarter + "_" + (typeId ?? "x"),
                sender = "机队发展部",
                role = "购机请示",
                glyph = "机",
                kind = "report",
                action = FleetBuyAction,
                origin = count.ToString(),
                dest = typeId ?? "",
                stopover = source ?? "",
                subject = subject,
                body = InboxLetter.Frame(gs, "机队发展部", "购机请示", subject,
                    InboxLetter.Bullet($"机型 {typeId} · {count} 架 · 方式 {source}"),
                    "请您在本函批复后发 LOI。"),
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            message = "已呈请 CEO，购机请示已发到收件箱。";
            return true;
        }

        public static bool ApproveFleetBuy(GameState gs, InboxMail mail, out string message)
        {
            if (FleetManager.Instance == null) { message = "机队系统未就绪。"; return false; }
            int count = 1;
            if (mail != null) int.TryParse(mail.origin, out count);
            count = Mathf.Max(1, count);
            bool ok = FleetManager.Instance.TryAcquireFleet(mail?.dest, count, mail?.stopover, out message);
            if (ok) Mark(gs, mail.id, "approved");
            return ok;
        }

        public static bool OfferHrRecruit(GameState gs, string position, int needed, int urgency, int channel, out string message)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪。"; return false; }
            if (HasOpenAction(gs, HrRecruitAction))
            {
                message = "招聘请示已在收件箱。";
                return false;
            }
            string subject = $"关于批准招聘「{position}」的请示";

            // 运行岗编制封顶预告：批复前就让 CEO 看到「编制 / 在职 / 在途 / 还能招几个」，
            // 避免批复后被静默裁剪人数而产生困惑（needed 会在 LaunchRecruitment 里按余量封顶）。
            string capLine = "";
            int asked = Mathf.Max(1, needed);
            if (HrManager.RecruitRoomLeft(gs, position, out int est, out int onDuty, out int inFlight, out int room))
            {
                capLine = room <= 0
                    ? InboxLetter.Bullet($"编制已满：{position} 编制 {est} 人 · 在职 {onDuty} · 在途 {inFlight} —— 本函批复后将被驳回，需先扩充机队")
                    : InboxLetter.Bullet($"编制 {est} 人 · 在职 {onDuty} · 在途 {inFlight} · 余量 {room} 人"
                        + (asked > room ? $"（申请 {asked} 人将按余量裁剪为 {room} 人）" : ""));
            }

            Add(gs, new InboxMail
            {
                id = "m_hr_rec_" + gs.year + "q" + gs.quarter,
                sender = "人力资源部",
                role = "招聘请示",
                glyph = "人",
                kind = "report",
                action = HrRecruitAction,
                origin = needed.ToString(),
                dest = position ?? "",
                stopover = urgency + "|" + channel,
                subject = subject,
                body = InboxLetter.Frame(gs, "人力资源部", "招聘请示", subject,
                    InboxLetter.Bullet($"{position} · {needed} 人 · {HrEconomics.ChannelName(channel)}"),
                    capLine,
                    "请您在本函批复后启动招聘。"),
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            message = "已呈请 CEO，招聘请示已发到收件箱。";
            return true;
        }

        public static bool ApproveHrRecruit(GameState gs, InboxMail mail, out string message)
        {
            if (HrManager.Instance == null) { message = "人力系统未就绪。"; return false; }
            int needed = 1;
            int urgency = 1;
            int channel = 0;
            if (mail != null)
            {
                int.TryParse(mail.origin, out needed);
                if (!string.IsNullOrEmpty(mail.stopover))
                {
                    var p = mail.stopover.Split('|');
                    if (p.Length >= 1) int.TryParse(p[0], out urgency);
                    if (p.Length >= 2) int.TryParse(p[1], out channel);
                }
            }
            bool ok = HrManager.Instance.LaunchRecruitment(mail?.dest, Mathf.Max(1, needed), urgency, channel, out message);
            if (ok) Mark(gs, mail.id, "approved");
            return ok;
        }

        public static void NotifyFuelMove(GameState gs, string eventLine)
        {
            if (gs == null || string.IsNullOrEmpty(eventLine)) return;
            string id = "m_fuel_move_" + gs.year + "q" + gs.quarter;
            if (HasMailId(gs, id)) return;
            string subject = "进度汇报：航油价格波动";
            Schedule(gs, new InboxMail
            {
                id = id,
                sender = CfoSender,
                role = "市场通报",
                glyph = "财",
                kind = "report",
                action = FinanceProgressAction,
                subject = subject,
                body = InboxLetter.Frame(gs, CfoSender, "市场通报", subject,
                    eventLine + $"\n现价 ${gs.fuelPricePerTon:F0} / 吨。",
                    "本封为进度汇报，无需批复。",
                    gs.SimUtc.Add(GameState.LocalUtcOffset).ToString("yyyy年MM月dd日"),
                    needDecision: false),
                status = "approved",
                tag = "油价",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ProgressReady(gs.SimUtc));
        }

        /// <summary>
        /// 现实行情收敛通报（n116 行情源开启时，每季把航油现货向现实报价靠拢 ±8% 的那一封）。
        /// 与 <see cref="NotifyFuelMove"/>（游戏引擎自身的油价波动）分开：这封专门讲「现实报价 → 公司油价」的靠拢，
        /// 让 CEO 在收件箱里直接看到现实行情的影响，而不必去翻季度结算长文。
        /// 同一季只发一封（按 year+quarter 去重）；未开启行情源 / 报价异常时 <paramref name="quoteText"/> 为空，不发。
        /// </summary>
        public static void NotifyFuelQuoteConverge(GameState gs, string quoteText)
        {
            if (gs == null || string.IsNullOrEmpty(quoteText)) return;
            string id = "m_fuel_quote_" + gs.year + "q" + gs.quarter;
            if (HasMailId(gs, id)) return;
            string subject = "进度汇报：航油现货按现实行情收敛";
            var core = new System.Text.StringBuilder();
            core.Append(quoteText);
            core.Append($"\n收敛后现货 ${gs.fuelPricePerTon:F0} / 吨。");
            Schedule(gs, new InboxMail
            {
                id = id,
                sender = CfoSender,
                role = "行情通报",
                glyph = "财",
                kind = "report",
                action = FinanceProgressAction,
                subject = subject,
                body = InboxLetter.Frame(gs, CfoSender, "行情通报", subject, core.ToString(),
                    "本封为进度汇报，无需批复。",
                    gs.SimUtc.Add(GameState.LocalUtcOffset).ToString("yyyy年MM月dd日"),
                    needDecision: false),
                status = "approved",
                tag = "行情",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ProgressReady(gs.SimUtc));
        }

        public static void NotifyFxQuoteConverge(GameState gs, string fxText)
        {
            if (gs == null || string.IsNullOrEmpty(fxText)) return;
            string id = "m_fx_quote_" + gs.year + "q" + gs.quarter;
            if (HasMailId(gs, id)) return;
            string subject = "进度汇报：汇率按现实行情收敛";
            var core = new System.Text.StringBuilder();
            core.Append(fxText);
            core.Append($"\n收敛后 USD/CNY {gs.usdCny:F4}。");
            Schedule(gs, new InboxMail
            {
                id = id,
                sender = CfoSender,
                role = "行情通报",
                glyph = "财",
                kind = "report",
                action = FinanceProgressAction,
                subject = subject,
                body = InboxLetter.Frame(gs, CfoSender, "行情通报", subject, core.ToString(),
                    "本封为进度汇报，无需批复。",
                    gs.SimUtc.Add(GameState.LocalUtcOffset).ToString("yyyy年MM月dd日"),
                    needDecision: false),
                status = "approved",
                tag = "行情",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ProgressReady(gs.SimUtc));
        }

        public static void NotifyHedgeSettle(GameState gs, string settleText)
        {            if (gs == null || string.IsNullOrEmpty(settleText)) return;
            string id = "m_hedge_set_" + gs.year + "q" + gs.quarter;
            if (HasMailId(gs, id)) return;
            string subject = "进度汇报：燃油对冲本季结算";
            Schedule(gs, new InboxMail
            {
                id = id,
                sender = CfoSender,
                role = "结算通报",
                glyph = "财",
                kind = "report",
                action = FinanceProgressAction,
                subject = subject,
                body = InboxLetter.Frame(gs, CfoSender, "结算通报", subject, settleText,
                    "本封为进度汇报，无需批复。",
                    gs.SimUtc.Add(GameState.LocalUtcOffset).ToString("yyyy年MM月dd日"),
                    needDecision: false),
                status = "approved",
                tag = "结算",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ProgressReady(gs.SimUtc));
        }

        public static InboxMail FindLatestAction(GameState gs, string action)
        {
            if (gs?.inbox == null || string.IsNullOrEmpty(action)) return null;
            InboxMail best = null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != action) continue;
                if (best == null || m.receivedUtcTicks >= best.receivedUtcTicks)
                    best = m;
            }
            return best;
        }

        public static InboxMail FindLatestProgress(GameState gs, string tag)
        {
            if (gs?.inbox == null) return null;
            InboxMail best = null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != FinanceProgressAction) continue;
                if (!string.IsNullOrEmpty(tag) && m.tag != tag) continue;
                if (best == null || m.receivedUtcTicks >= best.receivedUtcTicks)
                    best = m;
            }
            return best;
        }

        /// <summary>现金跑道 &lt; 3 月时发危机决策邮件（定稿 n25/n103/n106 · P16）。</summary>
        public static void MaybeOfferFinanceCrisis(GameState gs)
        {
            if (gs == null || gs.gameOver) return;
            if (gs.restructuring != null && gs.restructuring.filed) return;
            if (HasOpenAction(gs, "finance_crisis")) return;
            string id = "m_fin_crisis_" + gs.year + "q" + gs.quarter;
            if (HasMailId(gs, id)) return;
            if (!LoanManager.IsFinanceUnlocked(gs)) return;

            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string dest = RecommendFinanceDest(gs);
            Add(gs, new InboxMail
            {
                id = id,
                sender = CfoSender,
                role = "危机提案",
                glyph = "财",
                kind = "alert",
                action = "finance_crisis",
                dest = dest,
                subject = "现金跑道过短，请拍板紧急融资",
                body = FormatFinanceCrisisBody(gs, dest),
                tag = "紧急",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        public static string RecommendFinanceDest(GameState gs)
        {
            if (gs == null) return ((int)FinancingKind.Bridge).ToString();
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            var rating = LoanManager.GetCreditRating(gs);
            if (runway < 2f && (rating < CreditRating.BBB || gs.boardSatisfaction < 40f))
                return "restructure";
            if (rating >= CreditRating.BBB) return ((int)FinancingKind.Bank).ToString();
            if (gs.boardSatisfaction >= 40f) return ((int)FinancingKind.Equity).ToString();
            return ((int)FinancingKind.Bridge).ToString();
        }

        public static string FormatFinanceCrisisBody(GameState gs, string dest)
        {
            if (gs == null) return "财务数据不可用。";
            float runway = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn);
            var rating = LoanManager.GetCreditRating(gs);
            FinanceEconomics.CrisisInaction(gs.EffectiveMonthlyBurn, out float fee, out float bonus);
            string rec = dest == "restructure"
                ? "破产保护重组（Chapter 11）"
                : dest == ((int)FinancingKind.Equity).ToString() ? "股东增资"
                : dest == ((int)FinancingKind.Bridge).ToString() ? "高息过桥"
                : "银行贷款";
            return $"现金 {gs.cash:F0} 万 · {CashRunnelCalculator.FormatPlayerLabel(runway)} · 信用 {LoanManager.CreditLabel(rating)} · 董事会 {gs.boardSatisfaction:F0}\n\n" +
                $"建议：批准后打开条款确认 或 紧急融资 签署（推荐 {rec}）。可点重组条件 查看破产保护方案。\n" +
                $"拒绝或到期：供应商催款约 {fee:F0} 万，月烧钱 +{bonus:F0} 万，董事会满意度 −12。";
        }

        public static bool IsFinanceRestructure(InboxMail mail)
            => mail != null && (mail.dest == "restructure" || mail.dest == "ch11");

        public static int ParseFinanceKind(InboxMail mail)
        {
            if (mail == null || string.IsNullOrEmpty(mail.dest)) return (int)FinancingKind.Bank;
            int k;
            return int.TryParse(mail.dest, out k) ? k : (int)FinancingKind.Bank;
        }

        public static bool RejectFinanceCrisis(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "邮件无效。"; return false; }
            ApplyFinanceCrisisPenalty(gs, out message);
            Mark(gs, mail.id, "rejected");
            Add(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = CfoSender,
                role = "执行回执",
                glyph = "财",
                kind = "alert",
                subject = "未融资：流动性继续恶化",
                body = message,
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
            return true;
        }

        public static void ApplyFinanceCrisisPenalty(GameState gs, out string message)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪。"; return; }
            FinanceEconomics.CrisisInaction(gs.EffectiveMonthlyBurn, out float fee, out float bonus);
            gs.cash -= fee;
            gs.yearNetProfit -= fee;
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction - 12f, 0f, 100f);
            gs.financeCrisisBurnBonus += bonus;
            message = $"未采取融资：现金 −{fee:F0} 万（供应商催款），月烧钱 +{bonus:F0} 万，董事会满意度 −12 → {gs.boardSatisfaction:F0}。";
            gs.AddDecisionLog("财务：" + message);
            gs.NotifyChanged();
        }

        public static void ExpireOpenFuelHedge(GameState gs)
        {
            if (gs?.inbox == null) return;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != FuelHedgeAction || !IsOpen(m)) continue;
                if (!IsDue(m, gs.year, gs.quarter)) continue;
                RejectFuelHedge(gs, m, out _);
            }
        }

        public static void ExpireOpenFinanceCrisis(GameState gs)
        {
            if (gs?.inbox == null) return;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != "finance_crisis" || !IsOpen(m)) continue;
                if (!IsDue(m, gs.year, gs.quarter)) continue;
                RejectFinanceCrisis(gs, m, out _);
            }
        }

        public static void CompleteFinanceCrisisMail(string mailId, string summary)
        {
            var gs = GameState.Instance;
            if (gs == null || string.IsNullOrEmpty(mailId)) return;
            Mark(gs, mailId, "approved");
            Schedule(gs, new InboxMail
            {
                id = "fx_" + mailId + "_" + gs.year + gs.quarter,
                sender = CfoSender,
                role = "执行回执",
                glyph = "财",
                kind = "report",
                subject = "紧急融资条款已生效",
                body = summary ?? "",
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ReplyReady(gs.SimUtc));
        }

        static string OpsScheduleMailId(string routeId) => "m_ops_sked_" + (routeId ?? "x");

        static RouteData FindRoute(GameState gs, InboxMail mail)
        {
            if (gs?.activeRoutes == null || mail == null) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null) continue;
                if (!string.IsNullOrEmpty(mail.origin) && r.routeId == mail.origin)
                    return r;
                if (r.origin == mail.origin && r.destination == mail.dest)
                    return r;
            }
            return null;
        }

        /// <summary>运营部召集各部门研究后，把开航提案呈 CEO 收件箱决策。</summary>
        public static bool QueueOpenRouteStudy(GameState gs, string origin, string dest, string via,
            string subject, string body)
        {
            if (gs == null || string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(dest)) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            if (gs.pendingInbox == null) gs.pendingInbox = new List<PendingInboxMail>();
            string viaPart = string.IsNullOrEmpty(via) ? "" : "_" + via;
            string id = "m_open_" + origin + "_" + dest + viaPart;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.id != id) continue;
                if (IsOpen(m))
                {
                    m.subject = subject;
                    m.body = body;
                    m.origin = origin;
                    m.dest = dest;
                    m.stopover = via ?? "";
                    m.time = gs.DateLabel;
                    Changed?.Invoke();
                    return true;
                }
            }

            var draft = new InboxMail
            {
                id = id,
                sender = "运营部",
                role = "开航研究会",
                glyph = "运",
                kind = "report",
                action = "open_route",
                origin = origin,
                dest = dest,
                stopover = via ?? "",
                subject = subject,
                body = body,
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            };
            Schedule(gs, draft, InboxCadence.StudyReady(gs.SimUtc));
            gs.AddDecisionLog($"开航：已申请开通 {origin}-{dest}，运营部召集会商，纪要约 {InboxCadence.StudyBusinessDays} 个工作日后呈报。");
            return true;
        }

        public static bool QueueOpsSchedule(GameState gs, RouteData route)
        {
            if (gs == null || route == null) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string id = OpsScheduleMailId(route.routeId);
            if (HasMailId(gs, id))
            {
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m == null || m.id != id) continue;
                    if (IsOpen(m))
                    {
                        m.body = OpsScheduleLogic.FormatPlanBody(route);
                        m.time = gs.DateLabel;
                        Changed?.Invoke();
                        return true;
                    }
                }
            }

            Add(gs, new InboxMail
            {
                id = id,
                sender = "运营部",
                role = "航班计划",
                glyph = "运",
                kind = "report",
                action = "ops_schedule",
                origin = route.routeId,
                dest = route.destination,
                subject = $"请批准 {route.origin}—{route.destination} 执飞机与航班计划",
                body = OpsScheduleLogic.FormatPlanBody(route),
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            return true;
        }

        public static void MaybeOfferOpsSchedule(GameState gs)
        {
            RefreshOpenCapacityReports(gs);
            if (gs?.activeRoutes == null) return;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null || !r.schedulePending) continue;
                // 已由运力调整呈报接管（CEO 已下达指令）：不再重复发排班呈报
                if (r.directivePending) continue;
                QueueOpsSchedule(gs, r);
            }
        }

        /// <summary>
        /// 待批运力呈报若按旧排班窗口编成（例如占位 08:00→10:30 把短程往返算成 6h），
        /// 按 CEO 原文令重编一次，写回邮件正文与航线班次。已按当前引擎编过的跳过。
        /// </summary>
        public static int RefreshOpenCapacityReports(GameState gs)
        {
            if (gs?.inbox == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var mail = gs.inbox[i];
                if (mail == null || mail.action != CapacityDirectiveAction || !IsOpen(mail)) continue;
                if (mail.capEngineRev >= CapacityDirectiveLogic.EngineRev) continue;
                if (RefreshOneCapacityReport(gs, mail)) n++;
            }
            if (n > 0)
            {
                Changed?.Invoke();
                gs.NotifyChanged();
            }
            return n;
        }

        static bool RefreshOneCapacityReport(GameState gs, InboxMail mail)
        {
            var route = FindRoute(gs, mail);
            if (route == null) return false;

            string note = mail.directiveNote;
            if (string.IsNullOrWhiteSpace(note))
                note = "加密到每天 4 段往返，早班到红眼都排上";

            var d = CapacityDirectiveLogic.Parse(note);
            if (d.waves <= 0 && (d.deps == null || d.deps.Length == 0) && !d.maximize)
                d.maximize = true;   // 旧档「挖满全天」解析成自动 +1 时，按挖满重编

            var rep = CapacityDirectiveLogic.Execute(gs, route, d);
            if (!rep.ok) { mail.capEngineRev = CapacityDirectiveLogic.EngineRev; return false; }

            mail.body = CapacityDirectiveLogic.FormatReportBody(gs, route, rep);
            mail.ringIdsCsv = rep.ringIdsCsv;
            mail.directiveNote = string.IsNullOrWhiteSpace(rep.directiveRaw) ? note : rep.directiveRaw;
            mail.subject = $"关于 {route.origin}—{route.destination} 运力调整与航班环的报告（请批复）";
            mail.capEngineRev = CapacityDirectiveLogic.EngineRev;
            mail.status = "pending";
            QueueSlotApplications(gs, route, rep.shortages);
            gs.AddDecisionLog(
                $"运营：按新排班口径重编 {route.origin}—{route.destination}（{route.DualWeeklyLine()}），呈报已更新。");
            return true;
        }

        // ———————————————— 运力调整呈报（CEO 指令 → 运营部自动执行 → 待批复） ————————————————

        static string CapacityMailId(string routeId, int revision) =>
            $"m_cap_dir_{routeId ?? "x"}_{revision}";

        /// <summary>
        /// 把「目标 / 指令前 / 缺口原文」随呈报一起记住 —— 批复回执的三档裁定（已生效 / 部分生效 /
        /// 未变动）全靠这三个数（★2026-10-16 第四十二次修订）。
        ///
        /// 目标波次：正数 = 目标段数；**-1 = 指令要求维持现状**（<see cref="CapacityDirectiveLogic.Report.requestedWaves"/>
        /// 为 0 且不是失败路径时就是这个意思）。回执只能读到「现在几段」，没有它就只能凭常量写「已生效」。
        /// </summary>
        static void StampCapacityFacts(InboxMail m, CapacityDirectiveLogic.Report rep)
        {
            if (m == null || rep == null) return;
            m.capTargetWaves = rep.requestedWaves > 0 ? rep.requestedWaves : -1;
            m.capBeforeWaves = rep.beforeWaves;
            m.capGapSection = rep.shortageSection ?? "";
        }

        /// <summary>运营部执行完 CEO 运力指令后，把「调整后运力 + 航班环」呈报给 CEO。</summary>
        /// <param name="delayHours">
        /// 呈报投递延迟（游戏小时）。-1 = 按 <see cref="InboxCadence.ReportHours"/> 延后（线上路径）；
        /// 0 = 立即入箱（回归用例用，断言的是「这封该不该生成」而不是「几小时后到」）。
        /// </param>
        public static bool QueueCapacityReport(GameState gs, RouteData route, CapacityDirectiveLogic.Report rep,
                                               int delayHours = -1)
        {
            if (gs == null || route == null || rep == null || !rep.ok) return false;
            // 指派/改派执飞飞机的呈报走 assign_aircraft 专用邮件（Execute 里已直接投递提名件）。
            // 这里早退，免得同一件事出现两封待批邮件（一封挑机号、一封说班次没变）。
            if (rep.assignment) return true;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();

            string id = CapacityMailId(route.routeId, route.opsPlanRevision);
            // 原排班呈报若还挂着，标记为已被指令取代，避免同一航线两封待办互相阻断
            SupersedeOpsSchedule(gs, route);
            // 同一航线更早的运力呈报也已过时（新调令已重编）
            SupersedeCapacityReports(gs, route, null);
            // 还没到点的那封旧呈报同样作废 —— 只扫 inbox 会让它过期后照样送到 CEO 桌上，
            // 那时两条呈报说的是同一航线、两个不同方案，批哪条都是错的。
            DropPendingCapacityReports(gs, route, id);

            int hours = delayHours >= 0 ? delayHours : InboxCadence.ReportHours;
            string subject = rep.slotFirst
                ? $"关于 {route.origin}—{route.destination} 运力调整（先批时刻槽）"
                : $"关于 {route.origin}—{route.destination} 运力调整与航班环的报告（请批复）";
            string body = CapacityDirectiveLogic.FormatReportBody(gs, route, rep);

            if (HasMailId(gs, id))
            {
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m == null || m.id != id) continue;
                    m.subject = subject;
                    m.body = body;
                    m.status = "pending";
                    m.read = false;
                    m.time = gs.DateLabel;
                    m.ringIdsCsv = rep.ringIdsCsv;
                    m.directiveNote = rep.directiveRaw;
                    m.capEngineRev = CapacityDirectiveLogic.EngineRev;
                    StampCapacityFacts(m, rep);
                    m.tag = rep.slotFirst ? "待槽" : "呈报";
                    QueueSlotApplications(gs, route, rep.shortages, hours);
                    Changed?.Invoke();
                    gs.NotifyChanged();
                    return true;
                }
            }

            var fresh = new InboxMail
            {
                id = id,
                sender = "运营部",
                role = "运营部负责人",
                glyph = "运",
                kind = "report",
                action = CapacityDirectiveAction,
                origin = route.routeId,
                dest = route.destination,
                subject = subject,
                body = body,
                tag = rep.slotFirst ? "待槽" : "呈报",
                ringIdsCsv = rep.ringIdsCsv,
                directiveNote = rep.directiveRaw,
                capEngineRev = CapacityDirectiveLogic.EngineRev,
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            };
            StampCapacityFacts(fresh, rep);
            // ★2026-10-19：呈报是「运营部编完班、组完环、联动完各部门之后写出来的东西」，
            // 不该在指令敲完的同一秒躺进收件箱（报障原话）。改为按游戏时钟延后投递，
            // 到点由 TimeManager → DeliverDue 送进收件箱；此间航线已按新方案排好、directivePending 挂着。
            Enqueue(gs, fresh, hours);
            gs.AddDecisionLog(hours > 0
                ? $"运营：按 CEO 指令调整 {route.origin}—{route.destination} 运力（{route.DualWeeklyLine()}），呈报约 {hours} 小时后送达。"
                : $"运营：按 CEO 指令调整 {route.origin}—{route.destination} 运力（{route.DualWeeklyLine()}），已呈报待批。");
            // 时刻槽单同样延后：缺槽是这条指令派生的活，不是「指令一敲战略发展部就到了」。
            QueueSlotApplications(gs, route, rep.shortages, hours);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 清掉还压在待发队列里的、同一航线的运力呈报（留下 <paramref name="keepId"/> 那一封）。
        /// 与 <see cref="SupersedeCapacityReports"/> 配对：那个管已经进箱的，这个管还没到点的。
        /// </summary>
        static void DropPendingCapacityReports(GameState gs, RouteData route, string keepId)
        {
            if (gs?.pendingInbox == null || route == null) return;
            for (int i = gs.pendingInbox.Count - 1; i >= 0; i--)
            {
                var m = gs.pendingInbox[i]?.mail;
                if (m == null || m.action != CapacityDirectiveAction) continue;
                if (!string.Equals(m.origin, route.routeId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(keepId) && m.id == keepId) continue;
                gs.pendingInbox.RemoveAt(i);
            }
        }

        /// <summary>
        /// 复核报告（**指令未能执行**时投的汇报件）：把「为什么没办成 + 本线现状 + 可以怎么办」回一封。
        ///
        /// 与 <see cref="QueueCapacityReport"/>（呈报件，待批复）相对：这是**汇报件**（action 留空、
        /// status=approved），不占待办、不挡时间推进。存在的理由见 2026-10-13 四次报障：
        /// 单航线页对 CEO 的承诺是「结果以邮件呈报」，失败时若只在页面状态行里说一句，他走一趟收件箱
        /// 什么都找不到 —— 报障原话「发布增加每日一段往返的指令，收件箱没有收到反馈邮件」。
        /// 与「无机可派 → 发运力核对汇报」同源：办不成也要回一封，只是无需批复。
        ///
        /// 同一航线 + 同一句原话共用一个 ID：重复下同一条办不成的令只刷新那封，不叠函。
        /// </summary>
        public static bool QueueCapacityFailureReport(GameState gs, RouteData route, string directiveRaw,
                                                      string note, out string message)
        {
            message = "";
            if (gs == null || route == null) { message = "航线不可用。"; return false; }
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            route.EnsureFlights();

            string id = $"m_cap_fail_{route.routeId}_{StableSuffix(CapacityDirectiveLogic.NormalizeRaw(directiveRaw))}";
            string subject = $"关于 {route.origin}—{route.destination} 运力指令未获执行（复核报告）";
            string body = CapacityDirectiveLogic.FormatFailureBody(gs, route, directiveRaw, note);

            var existing = FindMail(gs, id);
            if (existing != null)
            {
                existing.subject = subject;
                existing.body = body;
                existing.status = "approved";
                existing.read = false;
                existing.time = gs.DateLabel;
                existing.directiveNote = directiveRaw ?? "";
                existing.origin = route.routeId;
                existing.dest = route.destination;
                Changed?.Invoke();
            }
            else
            {
                Add(gs, new InboxMail
                {
                    id = id,
                    sender = "运营部",
                    // 汇报件（action 留空 = 无需批复）：给一封办不成事的决策邮件只是添乱
                    role = CapacityDirectiveLogic.FailureRole,
                    glyph = "运",
                    kind = "report",
                    origin = route.routeId,
                    dest = route.destination,
                    subject = subject,
                    body = body,
                    tag = "受阻",
                    status = "approved",
                    directiveNote = directiveRaw ?? "",
                    receivedYear = gs.year,
                    receivedQuarter = gs.quarter,
                    time = gs.DateLabel,
                    deadlineQuarters = 1
                });
            }
            message = $"运营部复核：{route.origin}—{route.destination} 的指令未获执行，"
                      + "已发复核报告（收件箱·汇报件，无需批复），写明原因与本线可选处置。本线维持现状。";
            // ★办不成的令会把本线回退到**上一个已批准**的班次 → 还挂着待批的旧呈报一并作废
            //   （否则收件箱里留着一封「调整后 N 班/周」的待批件，而台账早已回退）。
            SupersedeCapacityReports(gs, route, null,
                "随后下达的运力指令未获执行，本线已回退到上一个已批准的班次；本呈报自动作废。");
            gs.AddDecisionLog($"运营：{route.origin}—{route.destination} 的运力指令未能执行，已发复核报告。");
            gs.NotifyChanged();
            return true;
        }

        static void SupersedeOpsSchedule(GameState gs, RouteData route)
        {            if (gs?.inbox == null || route == null) return;
            string id = OpsScheduleMailId(route.routeId);
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.id != id || !IsOpen(m)) continue;
                // ★2026-10-16 第四十二次修订：作废 ≠ 批准。旧版把「被新调令取代」的呈报标成 approved，
                //   收件箱徽章于是写「已批准」—— CEO 从没批过这一封，而它正文里写的是「本呈报已被取代」。
                //   同一天的另一半病（回执恒写「已生效」）已在 ApproveCapacityDirective 修掉，这里一并钉住。
                m.status = SupersededStatus;
                m.body += "\n\n（本排班呈报已被 CEO 的运力调整指令取代，新方案见运力调整呈报。）";
                gs.AddDecisionLog("运营：原排班呈报由运力调整指令取代（已作废）。");
            }
        }

        /// <summary>
        /// 同一航线的旧运力呈报标记为已取代（<paramref name="keepId"/> 为当前呈报，保留待批；
        /// 传 null 表示本条线不再有待批呈报 —— 失败路径用它）。
        ///
        /// ★2026-10-14：失败路径也必须清。指令办不成时会把本线**回退到上一个已批准的班次**
        /// （`BackupScheduleForDirective` 只为「一封待批函 = 一批改动」记一次快照），于是那封
        /// 还挂着待批的旧呈报，数字描述的正是被回退掉的方案 —— 留着待批，CEO 一批就会出现
        /// 「呈报说 7 班/周、台账与回执却是 4 班/周」的自相矛盾。
        /// </summary>
        static void SupersedeCapacityReports(GameState gs, RouteData route, string keepId, string why = null)
        {
            if (gs?.inbox == null || route == null) return;
            string note = string.IsNullOrEmpty(why) ? "已有更新的运力调令执行，本呈报自动作废。" : why;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != CapacityDirectiveAction) continue;
                if (!string.Equals(m.origin, route.routeId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsOpen(m) || m.id == keepId) continue;
                m.status = SupersededStatus;
                m.body += "\n\n（" + note + "）";
                gs.AddDecisionLog("运营：旧运力呈报被新调令取代（已作废）。");
            }
        }

        public static bool ApproveCapacityDirective(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            if (route == null)
            {
                // 航线已不在运营（关停/改派）：呈报失去决策对象，自动结案，避免决策按钮残留、邮件永久挂起
                Mark(gs, mail.id, "approved");
                mail.body += "\n\n（对应航线已不在运营，本呈报自动结案，无需批复。）";
                message = "对应航线已不在运营，本呈报已自动结案。";
                gs.AddDecisionLog("运营：" + message);
                gs.NotifyChanged();
                return true;
            }

            route.directivePending = false;
            route.hasPreDirective = false;   // 新运力成为后续指令的基线
            route.schedulePending = false;
            route.SyncPrimaryFromFlights();
            Mark(gs, mail.id, "approved");
            // 批准 = 玩家明确的「这个理解是对的」信号 → 把挂起的原话正式记成关键词。
            bool learned = DirectiveLexicon.Promote(mail.directiveNote);

            // ★2026-10-16 第四十二次修订：回执按「目标 vs 实际」裁三档。
            //   旧版在这里写死「运力调整已生效」—— 呈报是「先批时刻槽」（1 班 → 1 班、缺 1 波）时，
            //   回执却说「已生效」，与呈报自己那句「批准仅确认已排定的班次生效」当场打架；
            //   插槽未交割期间重下同一条令，第二次的回执依旧写「已生效」，一班都没动也说生效。
            var verdict = CapacityDirectiveLogic.VerdictOf(route, mail.capTargetWaves, mail.capBeforeWaves);
            int nowWaves = route.FlightCount;
            string verdictText = CapacityDirectiveLogic.VerdictWord(verdict);
            string slotInFlight = SlotInFlightLines(gs, route.routeId);
            switch (verdict)
            {
                case CapacityDirectiveLogic.CapVerdict.Done:
                    message = $"已批准 {route.origin}—{route.destination} 运力：{CapacityDirectiveLogic.RegsOf(route)} {route.DualWeeklyLine()}。";
                    break;
                case CapacityDirectiveLogic.CapVerdict.Keep:
                    message = $"已批准 {route.origin}—{route.destination} 维持现有运力（{nowWaves} 段/日），班次未作改动。";
                    break;
                case CapacityDirectiveLogic.CapVerdict.Unknown:
                    // 旧档（呈报当年没记目标）：只敢说「已确认」——不拿「已生效」去糊弄
                    message = $"已批准 {route.origin}—{route.destination} 运力呈报，现 {nowWaves} 段/日，回执已发收件箱。";
                    break;
                default:
                    message = $"已批准 {route.origin}—{route.destination} 运力呈报：裁定「{verdictText}」—— "
                              + $"目标每日 {mail.capTargetWaves} 段，实际 {nowWaves} 段，缺口见回执"
                              + "（无需重下同一条指令，该办的单子已在办）。";
                    break;
            }
            gs.AddDecisionLog("运营：" + message);
            Schedule(gs, new InboxMail
            {
                id = "fx_cap_" + mail.id,
                sender = "运营部",
                role = "执行回执",
                glyph = "运",
                kind = "report",
                // 回执也要记下是哪条线：CEO 读完回执往往直接想去看这条线现在的班期与槽，
                // 底栏的「详见 · 此航线」靠 origin 认线（2026-10-19）。
                origin = route.routeId,
                dest = route.destination,
                subject = CapacityDirectiveLogic.ReceiptSubject(route.origin, route.destination, verdict, nowWaves, mail.capTargetWaves),
                body = CapacityDirectiveLogic.FormatReceiptBody(gs, route, mail.ringIdsCsv,
                           mail.capTargetWaves, mail.capBeforeWaves, mail.capGapSection, slotInFlight)
                    + (learned ? "\n（已记住这次的说法：下次您这样写，即使分析服务没接通我也照这个理解办。）\n" : ""),
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ReplyReady(gs.SimUtc));
            gs.NotifyChanged();
            return true;
        }

        public static bool RejectCapacityDirective(GameState gs, InboxMail mail, out string message)
        {
            var route = FindRoute(gs, mail);
            if (route == null)
            {
                Mark(gs, mail.id, "rejected");
                mail.body += "\n\n（对应航线已不在运营，本呈报自动结案。）";
                message = "对应航线已不在运营，本呈报已自动结案。";
                gs.AddDecisionLog("运营：" + message);
                gs.NotifyChanged();
                return true;
            }
            CapacityDirectiveLogic.Revert(gs, route, mail.ringIdsCsv, out message);
            // 驳回 = 这次理解（或这次方案）不算数 → 丢弃候选；若这句话此前已被记住，一并抹掉。
            // 宁可少记，不可记错：记忆会让一句话压过关键词猜测，误读一旦被固化就再也改不回来。
            DirectiveLexicon.Discard(mail.directiveNote);
            Mark(gs, mail.id, "rejected");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>CEO 提出修改：按新调令重编运力与航班环后再报。</summary>
        public static bool ReviseCapacityDirective(GameState gs, InboxMail mail, string ceoNote, string aiJson, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            if (route == null)
            {
                Mark(gs, mail.id, "approved");
                mail.body += "\n\n（对应航线已不在运营，本呈报自动结案。）";
                message = "对应航线已不在运营，本呈报已自动结案。";
                gs.AddDecisionLog("运营：" + message);
                Changed?.Invoke();
                gs.NotifyChanged();
                return true;
            }

            var d = CapacityDirectiveLogic.Parse(ceoNote);

            // 「提出修改」框里同样可能是**提问**（同源问题，2026-10-13 CEO 报障）。
            // 与单航线页不同的是：这里没有「先回显再确认」的位置，故直接转成答复件，
            // 原呈报**原封不动**地留在收件箱等真正的调令 —— 不能因为一句「为什么」就把呈报结掉。
            if (CapacityDirectiveLogic.LooksLikeQuestion(ceoNote))
            {
                bool queued = QueueOpsQueryAnswer(gs, route, ceoNote, out string qmsg);
                message = queued
                    ? "这句是询问，已转运营部答复（见新到邮件），本呈报未作任何改动。要改运力请写「加密到每天 N 班」这类调令。"
                    : "这句是询问，但答复件未能发出：" + qmsg;
                mail.read = false;             // 退回收件箱未读，避免 CEO 以为改稿已受理
                Changed?.Invoke();
                gs.NotifyChanged();
                return false;
            }

            CapacityDirectiveLogic.MergeAiJson(d, aiJson);
            // 与单航线页同源：只说目标的改稿（「调整为每日 N 段往返」）在这里也要能把方向定下来。
            CapacityDirectiveLogic.ResolveStatedTarget(route, d);
            // 收件箱里的「提出修改」同样是自由文本，同一个坑：措辞没命中词表时旧版默认按增班办。
            // 这里没有「先回显再确认」的交互位（改稿是逐稿往返的），故退而求其次：**拒绝执行并说清为什么**，
            // 让 CEO 补一句明确的量再提交 —— 比默默把想砍的班加密了好得多。
            if (CapacityDirectiveLogic.NeedsConfirm(d))
            {
                message = "没读懂这条改稿想增班还是减班，未做任何改动。请明确写「加密到每天 N 班」或「削减到每天 N 班」。";
                Changed?.Invoke();
                return false;
            }
            var rep = CapacityDirectiveLogic.Execute(gs, route, d);
            if (!rep.ok)
            {
                // 呈报仍留在收件箱（未改动），但**未能执行也要回一封复核报告**，
                // 免得 CEO 只看到页面/提示栏一句话就去收件箱找一个不存在的东西（2026-10-13 报障）。
                message = rep.note + (rep.failureQueued
                    ? " 已发复核报告（收件箱·汇报件，无需批复）。原呈报未改动，可另写下一条调令。"
                    : "");
                Changed?.Invoke();
                return false;
            }
            DirectiveLexicon.Stage(d.raw, d, d.fromAi);

            mail.body = CapacityDirectiveLogic.FormatReportBody(gs, route, rep);
            mail.ringIdsCsv = rep.ringIdsCsv;
            mail.directiveNote = rep.directiveRaw;
            mail.capEngineRev = CapacityDirectiveLogic.EngineRev;
            // ★2026-10-16：改稿也必须重记「目标 / 指令前 / 缺口」—— 否则回执拿的是上一稿的目标，
            //   裁定就会用旧目标去比新班次（例：原写 3 段、改稿改成 2 段并排满，回执却说「部分生效」）。
            StampCapacityFacts(mail, rep);
            mail.subject = $"关于 {route.origin}—{route.destination} 运力调整与航班环的报告（改稿 {route.opsPlanRevision + 1}，请批复）";
            mail.status = "pending";
            mail.read = false;
            message = "运营部已按您的批复重编运力与航班环，请再审。";
            gs.AddDecisionLog($"运营：{message} {route.DualWeeklyLine()}。");
            Changed?.Invoke();
            gs.NotifyChanged();
            return true;
        }

        public static bool ApproveOpsSchedule(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            if (route == null)
            {
                Mark(gs, mail.id, "approved");
                mail.body += "\n\n（对应航线已不在运营，本呈报自动结案，无需批复。）";
                message = "对应航线已不在运营，本呈报已自动结案。";
                gs.AddDecisionLog("运营：" + message);
                gs.NotifyChanged();
                return true;
            }
            route.schedulePending = false;
            route.SyncPrimaryFromFlights();
            Mark(gs, mail.id, "approved");
            message = $"已批准 {route.origin}—{route.destination}：{route.registration} 按 {route.depTime} {route.daysOfWeek} 运行。";
            gs.AddDecisionLog("运营：" + message);
            Schedule(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = "运营部",
                role = "执行回执",
                glyph = "运",
                kind = "report",
                subject = $"{route.origin}—{route.destination} 航班计划已生效",
                body = message,
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ReplyReady(gs.SimUtc));
            gs.NotifyChanged();
            return true;
        }

        /// <summary>CEO 提出修改：按批复（及可选 AI 班次 JSON）重编后再报。</summary>
        public static bool ReviseOpsSchedule(GameState gs, InboxMail mail, out string message)
        {
            return ReviseOpsSchedule(gs, mail, null, null, out message);
        }

        public static bool ReviseOpsSchedule(GameState gs, InboxMail mail, string ceoNote, string aiJson, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            if (route == null)
            {
                Mark(gs, mail.id, "approved");
                mail.body += "\n\n（对应航线已不在运营，本呈报自动结案。）";
                message = "对应航线已不在运营，本呈报已自动结案。";
                gs.AddDecisionLog("运营：" + message);
                Changed?.Invoke();
                gs.NotifyChanged();
                return true;
            }
            // 负基准＝真实亏损，必须原样透传（此前 `> 0.01f` 会把它当未设，改用日利兜底而丢失单班亏损口径）
            float baseline = OpsEconomics.ResolvedSingleFlightBaseline(route);
            if (string.IsNullOrWhiteSpace(ceoNote))
            {
                route.opsPlanRevision++;
                if (!OpsScheduleLogic.ApplyDraft(route, gs.fleet, gs.activeRoutes, route.opsPlanRevision))
                {
                    message = "运营部暂无替代飞机或时刻，请批准现稿或退回。";
                    return false;
                }
                OpsEconomics.ApplyToRoute(route, baseline, gs.onTimePercent);
                mail.body = OpsScheduleLogic.FormatPlanBody(route);
            }
            else
            {
                // 与另两处收口同源：CEO 说了话，但关键词与分析服务都读不出任何「时段 / 班期 / 铺满」
                // 这类可执行信息时，旧行为是**静默按 opsPlanRevision 取模挑一个时段**执行 ——
                // 玩家说「换 4 号机」，系统把时刻挪走了，还说「已按您的批复调整」。
                // 空批复改稿（note 为空）走的是上面 ApplyDraft 分支，那次「换个时刻」是有意的；
                // 这里的「说不清就重掷」不是，故拒编并说清。
                if (!string.IsNullOrWhiteSpace(ceoNote) && string.IsNullOrWhiteSpace(aiJson))
                {
                    var gd = CapacityDirectiveLogic.Parse(ceoNote);
                    bool understood = gd.maximize || !string.IsNullOrEmpty(gd.days)
                                      || (gd.deps != null && gd.deps.Length > 0);
                    if (!understood)
                    {
                        message = "没读懂这条批复要改什么，未做任何改动。"
                                  + "请写明时段（早班/午班/晚班/红眼 · 或 08:00 这类时刻）、"
                                  + "班期（每日/一三五/二四六），或「早班到红眼都排上」。";
                        Changed?.Invoke();
                        gs.NotifyChanged();
                        return false;
                    }
                }
                if (!OpsScheduleLogic.ApplyFromCeoNoteOrAi(route, gs.fleet, gs.activeRoutes, ceoNote, aiJson, out string opsReply))
                {
                    message = "运营部暂无替代飞机或时刻，请批准现稿或退回。";
                    return false;
                }
                OpsEconomics.ApplyToRoute(route, baseline, gs.onTimePercent);
                mail.body = OpsScheduleLogic.FormatPlanBody(route, ceoNote, opsReply);
            }
            mail.subject = $"请批准 {route.origin}—{route.destination} 航班计划（改稿 {route.opsPlanRevision + 1}）";
            mail.status = "pending";
            mail.read = false;
            message = "运营部已按您的批复调整航班计划，请再审。";
            gs.AddDecisionLog("运营：" + message + $" {route.registration} {route.depTime}");
            Changed?.Invoke();
            gs.NotifyChanged();
            return true;
        }

        public static bool RejectOpsSchedule(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            if (route == null)
            {
                Mark(gs, mail.id, "rejected");
                mail.body += "\n\n（对应航线已不在运营，本呈报自动结案。）";
                message = "对应航线已不在运营，本呈报已自动结案。";
                gs.AddDecisionLog("运营：" + message);
                gs.NotifyChanged();
                return true;
            }
            route.schedulePending = true;
            Mark(gs, mail.id, "rejected");
            message = $"已退回 {route.origin}—{route.destination} 排班方案，本航线暂不执行航班。";
            gs.AddDecisionLog("运营：" + message);
            gs.NotifyChanged();
            return true;
        }

        // ———————————————— 航班时刻加机呈报（n98 点时段 → 呈报 → CEO 批准才落班） ————————————————

        /// <summary>
        /// 在 n98 点某个「航班时刻」不再当场落班，而是走本呈报：运营部提案 → CEO 批准 → 才写进班次表。
        /// 与 ops_schedule 的区别：ops_schedule 是「整条航线的执飞机与全部班次」方案（批准前整线冻结）；
        /// 本呈报只动**被点的那几个班次**，退回即等于没提过，在飞航线不受任何牵连。
        /// </summary>
        public const string AssignFlightAction = "assign_flight";

        static string AssignFlightSubject(RouteData route, int count)
            => $"请批准 {route.origin}—{route.destination} 加机（{count} 班）";

        /// <summary>
        /// 本航线当前该用哪个呈报编号：同一条航线同一时间只允许一封未结案，
        /// 未结案期间继续点时段就累加到它；结案（批准/退回）后再点则开新的一批。
        /// 编号带序号是为了避免「同一 id 两封邮件」——Mark 按 id 只标第一封，同 id 会漏标。
        /// </summary>
        static string AssignFlightMailId(GameState gs, string routeId)
        {
            for (int n = 1; n <= 999; n++)
            {
                string id = $"m_assign_flight_{routeId}_{n}";
                var m = FindMail(gs, id);
                if (m == null || IsOpen(m)) return id;
            }
            return $"m_assign_flight_{routeId}_{gs.year}{gs.quarter}";
        }

        /// <summary>
        /// 运营部呈报「把某机号排进某航线某**具体时刻**」。返回 false 时 message 说明为何报不上
        /// （参数不全 / 已在执飞 / 已在待批 / 缺时刻槽）。
        ///
        /// ★2026-10-15：<paramref name="timeOrLabel"/> 由「时段名」改为「**时刻**」为主口径 ——
        /// 传 `"07:00"` 就排 07:00 这一刻，传 `"早班"` 才折成该波峰点 08:00（旧界面/旧调用点的兼容路径）。
        /// 于是同一需求波内 06:30 / 07:00 / 07:30 可以各报一班（各占各的槽），这就是「同段多波」。
        /// </summary>
        public static bool QueueAssignFlight(GameState gs, RouteData route, string reg, string timeOrLabel, out string message)
        {
            message = "";
            if (gs == null || route == null) { message = "航线不可用。"; return false; }
            if (string.IsNullOrEmpty(timeOrLabel)) { message = "请选择时刻。"; return false; }
            if (string.IsNullOrEmpty(reg)) { message = "请先选择执飞飞机。"; return false; }
            if (!ScheduleSlots.IsKnownTimeOrBand(timeOrLabel))
            {
                message = $"时刻无效（{timeOrLabel}）。时刻槽须精确到时刻，如 06:30 / 07:00。";
                return false;
            }
            string clock = FlightProposalLogic.ClockOf(timeOrLabel);
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            route.EnsureFlights();

            if (FlightProposalLogic.HasFlight(route, reg, clock))
            {
                message = $"机号 {reg} 已在 {route.origin}-{route.destination} 执飞 {clock} 这一班，无需再呈报。";
                return false;
            }

            // ★2026-10-15：先过**飞机**硬门槛，再过**时刻槽**门槛 —— 顺序不能反。
            // 反了就会出现「为一条根本没有合适飞机可飞的班次去申请一条时刻槽」：槽批下来照样飞不了，
            // 白白占掉一条稀缺的时刻与一个季度。这也正是界面那行「⚠ 机型可能不匹配」从一句话变成真闸门的地方。
            // 放在 HasFlight 之后：同一机同一刻已在飞的情形，先说那句更直白的（不劳排班闸门判冲突）。
            if (!AircraftNominationLogic.CanOperateClock(gs, route, reg, clock, out string acWhy, out var acKind))
            {
                message = $"机号 {reg} 此刻排不进本线：{acWhy}。本班未呈报。"
                          + AircraftNominationLogic.Advice(acKind)
                          + "（换一架可在机队页指定；或在本航线页下「安排一架合适的飞机」调令，由运营部按硬门槛提名）";
                return false;
            }

            // 缺时刻槽时不落班、也不在界面上摆一段红字：让运营部把「为加这一班要申请哪一端的哪条槽」
            // 呈报给 CEO（QueueSlotRequestForFlight，批准 → 下季交割 → 交割后再重新呈报加机）。
            // 让 CEO 批准一个排不出、飞不了的班次才是把问题推给下一环；在这里挡住并当场开单才是出路。
            var proposal = FlightProposalLogic.Make(route, reg, clock);
            if (!RoutePermitLogic.CanScheduleClock(gs, route, proposal.dep, true))
                return QueueSlotRequestForFlight(gs, route, reg, clock, out message);

            string id = AssignFlightMailId(gs, route.routeId);
            var existing = FindMail(gs, id);
            bool open = existing != null && IsOpen(existing);
            var list = open ? FlightProposalLogic.Decode(existing.flightProposalsCsv)
                            : new List<FlightProposalLogic.Proposal>();

            if (FlightProposalLogic.IndexOf(list, reg, clock) >= 0)
            {
                message = $"机号 {reg} 的 {clock} 已在待批呈报中（收件箱 · {existing.subject}）。";
                return false;
            }

            list.Add(proposal);
            string body = FlightProposalLogic.FormatBody(gs, route, list);
            string subject = AssignFlightSubject(route, list.Count);

            if (open)
            {
                existing.subject = subject;
                existing.body = body;
                existing.flightProposalsCsv = FlightProposalLogic.Encode(list);
                existing.time = gs.DateLabel;
                Changed?.Invoke();
            }
            else
            {
                Add(gs, new InboxMail
                {
                    id = id,
                    sender = "运营部",
                    role = "航班计划",
                    glyph = "运",
                    kind = "report",
                    action = AssignFlightAction,
                    origin = route.routeId,
                    dest = route.destination,
                    subject = subject,
                    body = body,
                    tag = "提案",
                    flightProposalsCsv = FlightProposalLogic.Encode(list),
                    receivedYear = gs.year,
                    receivedQuarter = gs.quarter,
                    time = gs.DateLabel,
                    deadlineQuarters = DefaultDeadlineQuarters
                });
            }

            message = $"已呈报 CEO：{reg} 排进 {route.origin}-{route.destination} 的 {clock} 班（{proposal.dep} 起飞，{proposal.slotLabel}）。"
                      + "在收件箱批准后落班。";
            gs.AddDecisionLog("运营：呈报 " + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// n98「点时刻加机」却缺时刻槽时的出口（2026-10-13 裁定「缺槽改走邮件」，
        /// ★2026-10-15 第十五次问询后改为**给备选**）：不在网页上摆一段红字警告，而是让运营部
        /// 把「这一刻拿不到，但同一波窗口内还有哪几条路、各自的代价是什么」写成决策邮件呈报 CEO。
        ///
        /// **旧版只报「办不成」**：拿调令要的那一刻去问 `FirstSchedulableClock`，问不出来就把缺槽记在
        /// 那一刻上、申请那一刻的槽 —— 既没有选择，也看不见「手里已有的其它时刻的槽」。
        /// 而排班器是**从峰点往后搜**的，于是「手上握着 07:30 的槽、调令写 08:00」这种情形会被报成
        /// 「08:00 缺槽、要去买」，是一张**假缺口**。改后由 <see cref="SlotCandidateLogic.Options"/>
        /// 在该波窗口的**整张 30 分钟网格**上（含早于峰点的时刻）找可行的**往返时刻对**，
        /// 逐条给出两端可得性 / 取槽成本 / 那一刻派不派得出机 / 利弊，CEO 点选一条才去开申请单。
        ///
        /// 整张网格都拿不到时才发「受阻」汇报件（列两端池况与三条下一步），绝不开一张没有选项的选择题。
        /// 返回 false 是因为**加机呈报没提交**（班次表一根汗毛未动）；message 说明已开的单子去了哪。
        /// </summary>
        public static bool QueueSlotRequestForFlight(GameState gs, RouteData route, string reg, string timeOrLabel, out string message)
        {
            message = "";
            if (gs == null || route == null) { message = "航线不可用。"; return false; }
            if (string.IsNullOrEmpty(timeOrLabel)) { message = "请选择时刻。"; return false; }
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            route.EnsureFlights();

            var proposal = FlightProposalLogic.Make(route, reg, FlightProposalLogic.ClockOf(timeOrLabel));
            if (RoutePermitLogic.CanScheduleClock(gs, route, proposal.dep, true))
            {
                message = "该时刻两端时刻槽齐备，可直接呈报加机。";
                return false;
            }

            // 备选以**系列时刻**为身份：排班器的 5 分钟粒度时刻（07:40）不是任何机场会公布的系列。
            string peg = RoutePermitLogic.SlotRequestClock(proposal.dep);
            string reason = $"{(string.IsNullOrEmpty(reg) ? "未定机号" : reg)} 拟加 "
                            + $"{route.origin}—{route.destination} 的 {peg} 班";

            var opts = SlotCandidateLogic.Options(gs, route, peg);
            if (opts.Count == 0)
            {
                bool fresh = QueueSlotWindowBlockedReport(gs, route, peg);
                message = $"缺时刻槽：{peg} 所在需求波整张时刻表上都拿不到可用的往返时刻对，"
                          + $"已发汇报件（收件箱{(fresh ? "" : "，同封已在")}），列明两端池况与下一步。本班未呈报。";
                gs.AddDecisionLog("运营：" + message);
                gs.NotifyChanged();
                return false;
            }

            string id = SlotSelectMailId(gs, route.routeId, peg);
            string subject = SlotCandidateLogic.Subject(route, false, opts.Count);
            string body = SlotCandidateLogic.FormatBody(gs, route, opts, peg);
            string payload = SlotCandidateLogic.Encode(opts);

            var existing = FindMail(gs, id);
            if (existing != null && IsOpen(existing))
            {
                // 同一条航线同一波未结案期间再点：按最新台账重算备选，写回同一封（不叠两封待批）
                existing.subject = subject;
                existing.body = body;
                existing.payloadCsv = payload;
                existing.slotReason = reason;
                existing.slotClock = peg;
                existing.time = gs.DateLabel;
                Changed?.Invoke();
            }
            else
            {
                Add(gs, new InboxMail
                {
                    id = id,
                    sender = "运营部",
                    role = "加班时刻提名",
                    glyph = "运",
                    kind = "report",
                    action = SlotSelectAction,
                    origin = route.routeId,
                    dest = peg,
                    subject = subject,
                    body = body,
                    tag = "提名",
                    payloadCsv = payload,
                    slotReason = reason,
                    slotClock = peg,
                    slotRouteId = route.routeId,
                    receivedYear = gs.year,
                    receivedQuarter = gs.quarter,
                    time = gs.DateLabel,
                    deadlineQuarters = DefaultDeadlineQuarters
                });
            }

            message = $"缺时刻槽：{peg} 拿不到，但同一需求波窗口还有 {opts.Count} 条路可走 —— "
                      + $"已呈报 CEO 点选一刻（收件箱待批，运营部推荐 {opts[0].depClock}）。"
                      + "选定后运营部再为缺槽的那一端开申请单，交割后自动重排本线并呈报加机。本班未呈报。";
            gs.AddDecisionLog("运营：就 " + $"{route.origin}—{route.destination} 的加班时刻提名 {opts.Count} 个备选（{peg} 不可得）。");
            gs.NotifyChanged();
            return false;
        }

        /// <summary>
        /// 加班时刻提名件的编号：同一条航线**同一波**在同一季度只允许一封未结案，
        /// 未结案期间再点同一波就按最新台账重算备选并写回它（不叠两封待批）。
        /// 分波编号是必须的：早班的备选与晚班的备选是两件互不相干的事，不该挤在一封里。
        /// </summary>
        static string SlotSelectMailId(GameState gs, string routeId, string pegClock)
        {
            int m = ScheduleSlots.ParseClock(pegClock);
            int band = m < 0 ? 0 : ScheduleSlots.BandOfDep(ScheduleSlots.FormatClock(m));
            return $"m_slot_sel_{routeId ?? "x"}_{ScheduleSlots.DepTimes[band].Replace(":", "")}_{gs.year}q{gs.quarter}";
        }

        /// <summary>
        /// 该波窗口整张网格都拿不到可用往返时刻对时的「受阻」汇报件（`action` 留空 = 无需决策）：
        /// 讲清两端池况与三条下一步，不占一封待批邮件。
        /// 返回是否新投递（同航线同波同期只留一封，重复点击不刷屏）。
        /// </summary>
        static bool QueueSlotWindowBlockedReport(GameState gs, RouteData route, string pegClock)
        {
            if (gs == null || route == null) return false;
            string id = $"m_slot_blocked_{route.routeId}_{(pegClock ?? "").Replace(":", "")}_{gs.year}q{gs.quarter}";
            if (HasMailId(gs, id)) return false;

            Add(gs, new InboxMail
            {
                id = id,
                sender = "运营部",
                role = "航班计划",
                glyph = "运",
                kind = "report",
                subject = SlotCandidateLogic.Subject(route, true),
                body = SlotCandidateLogic.FormatBlockedBody(gs, route, pegClock),
                tag = "受阻",
                status = "approved",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
            return true;
        }

        #region 加班时刻备选：CEO 点定一刻 → 开申请单 → 交割后自动重排

        /// <summary>
        /// CEO 在备选件上**点定某一刻**：为那一刻两端里缺槽的那一端开协调池申请单，
        /// 并把「为哪条线、为哪一刻的班次」**结构化**记在单子上 ——
        /// 交割后 <see cref="RescheduleAfterSlotSettle"/> 才不靠解析人话就能接着办。
        ///
        /// <paramref name="clockChoice"/> 为空 = 采用运营部推荐（备选表首条，排序见
        /// <see cref="SlotCandidateLogic.Compare"/>）——「批准」按钮走的就是这一支。
        /// 选中那一刻若两端此刻都已齐备（呈报期间别处释放了槽），就不开单、直接进入重排。
        ///
        /// **认不出的时刻一律打回**，绝不替 CEO 挑一个相近的买下去：时刻槽精确到时刻，
        /// 买错一条等于错一整季（§15.8）。
        /// </summary>
        public static bool ApproveSlotSelect(GameState gs, InboxMail mail, string clockChoice, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "邮件无效。"; return false; }

            var opts = SlotCandidateLogic.Decode(mail.payloadCsv);
            if (opts.Count == 0)
            {
                // 旧档/脏数据：备选表解不出来。结案而不是挂起 —— 挂着只会永远占一封待批。
                Mark(gs, mail.id, "approved");
                mail.body += "\n\n（本函备选表已失效，自动结案；请重新下达加机指令。）";
                message = "备选表已失效，本函自动结案。";
                gs.AddDecisionLog("运营：" + message);
                gs.NotifyChanged();
                return true;
            }

            int idx = string.IsNullOrEmpty(clockChoice)
                ? 0
                : SlotCandidateLogic.IndexOf(opts, RoutePermitLogic.SlotRequestClock(clockChoice));
            if (idx < 0)
            {
                message = $"备选表里没有 {clockChoice} 这一刻，请从候选 1–{opts.Count} 中点选。";
                return false;
            }
            var opt = opts[idx];

            var route = FindRoute(gs, mail);
            Mark(gs, mail.id, "approved");

            // 归档选定结论：重排要用**去程离港**这一刻，不是「缺槽那一端」的时刻 ——
            // 申请单上的 slotClock 写的是缺槽端（可能是对端回程离港），两者不是同一件事。
            mail.slotClock = opt.depClock;
            mail.slotFlightClock = opt.depClock;
            if (route != null) mail.slotRouteId = route.routeId;
            mail.slotReason = route == null
                ? $"选定加班时刻 {opt.depClock}（候选 {idx + 1}/{opts.Count}）"
                : $"{route.origin}—{route.destination} 选定加班时刻 {opt.depClock}（候选 {idx + 1}/{opts.Count}）";

            if (route == null)
            {
                mail.body += "\n\n（对应航线已不在运营，本函自动结案，无需申请。）";
                message = "对应航线已不在运营，本函已自动结案。";
                gs.AddDecisionLog("运营：" + message);
                gs.NotifyChanged();
                return true;
            }

            var sb = new StringBuilder();
            sb.Append($"已选定 {route.origin}—{route.destination} 的 {opt.depClock} 起飞的往返");
            sb.Append(idx == 0 ? "（运营部推荐）。" : $"，候选 {idx + 1}/{opts.Count}。");

            if (RoutePermitLogic.CanScheduleClock(gs, route, opt.depClock, true))
            {
                sb.Append("两端时刻槽此刻都已齐备，不必再开申请单；运营部直接重排本线并呈报加机。");
                message = sb.ToString();
                gs.AddDecisionLog("运营：" + message);
                gs.NotifyChanged();
                string auto = RescheduleAfterSlotSettle(gs, mail);
                if (!string.IsNullOrEmpty(auto)) message += "\n" + auto;
                gs.NotifyChanged();
                return true;
            }

            int opened = 0, closed = 0, changed = 0, denied = 0;
            var needs = RoutePermitLogic.ClockNeeds(route, opt.depClock);
            for (int i = 0; i < needs.Count; i++)
            {
                var need = needs[i];
                string st = SlotCandidateLogic.EndStateOf(gs, route, need);
                if (st != SlotCandidateLogic.EndApply && st != SlotCandidateLogic.EndChange) continue;
                string apt = need.airport;
                string clk = RoutePermitLogic.SlotRequestClock(RoutePermitLogic.TimeOf(need));
                if (st == SlotCandidateLogic.EndChange)
                {
                    // ★2026-10-16 第四十一次修订：本线在该站已有槽、只是时刻滑点 → 提**改时**（免费、批准即用），
                    //   不去当新槽申请一遍（现实里那是持槽后的调整，见 WASG 9.1.3 / 8.10.1）。
                    if (RoutePermitLogic.TryTimeChange(gs, route, need, out _, out string chFrom, out string chTo, out bool chRoom))
                    {
                        if (!chRoom) { denied++; continue; }
                        if (QueueChangeSlot(gs, apt, chFrom, chTo, 1,
                                ChangeSlotMailId(gs, route, apt, chTo), out _,
                                SlotChangeReason(route, need, chFrom, chTo), route.routeId, opt.depClock,
                                RoutePermitLogic.LegName(need) + "　" + RoutePermitLogic.LegClocks(need)))
                            changed++;
                        else
                            closed++;
                    }
                    else denied++;
                    continue;
                }
                if (QueueApplySlots(gs, apt, clk, 1,
                        ApplySlotMailId(gs, route, apt, clk),
                        out _, mail.slotReason, route.routeId, opt.depClock))
                    opened++;
                else
                    closed++;
            }

            if (opened > 0)
                sb.Append($"已为缺槽一端开{opened}张开单（协调池申请，免费，下季交割）；交割后运营部自动重排本线并呈报加机。");
            if (changed > 0)
                sb.Append($"另有{changed}站本已持有槽、只是时刻滑点，已提**时刻变更申请**（改时，免费，协调人按系列余量批）。");
            if (closed > 0)
                sb.Append("另有同刻申请已在收件箱，未重复开单。");
            if (denied > 0)
                sb.Append($"另有{denied}站既无余量可申请、改时也批不下来，运营部已如实列为受阻端。");
            if (opened == 0 && closed == 0 && changed == 0 && denied == 0)
                // 各停站都不是「可申请/可改时」态：缺槽却拿不到，只能如实汇报（不硬开空单）
                sb.Append("可这一刻各停站都取不到槽（该时刻池已满），运营部已改为发受阻汇报，列明下一步。");
            message = sb.ToString();
            gs.AddDecisionLog("运营：" + message);

            // 时刻被买定 → 备选题已答完：把**同线同波**的旧备选件一并结案，免得同一件事挂着两封。
            CloseSiblingSlotSelects(gs, mail, opt.depClock);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 同一航线同一需求波的其它备选件一并结案（本次点定后，那些选择已经过时）。
        /// 只动 `action == slot_select` 且仍待处理的同线件，不碰别的邮件。
        /// </summary>
        static void CloseSiblingSlotSelects(GameState gs, InboxMail self, string chosenClock)
        {
            if (gs?.inbox == null) return;
            int band = ScheduleSlots.BandOfDep(chosenClock);
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || ReferenceEquals(m, self)) continue;
                if (m.action != SlotSelectAction || !IsOpen(m)) continue;
                if (!string.Equals(m.origin, self.origin, StringComparison.OrdinalIgnoreCase)) continue;
                if (ScheduleSlots.BandOfDep(SlotMailClock(m)) != band) continue;
                m.status = "approved";
                m.read = true;
                m.body += $"\n\n（同波时刻已定为 {chosenClock}，本函一并结案。）";
            }
        }

        /// <summary>
        /// CEO 退回备选件（「暂缓」也走这条）：本线维持现状，不申请、不花钱。
        /// 与加机呈报的退回同源 —— 什么都没发生过，无需撤销任何台账。
        /// </summary>
        public static bool RejectSlotSelect(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "邮件无效。"; return false; }
            var route = FindRoute(gs, mail);
            string where = route == null ? "" : $"{route.origin}—{route.destination} ";
            Mark(gs, mail.id, "rejected");
            message = $"已退回{where}的加班时刻提名：本季不为加班申请时刻槽，该线维持现有班次。"
                      + "若要改到别的需求波，可在航线页重新下达加机指令。";
            gs.AddDecisionLog("运营：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 申请单上记的**目标班次时刻**（去程离港）。优先用签发时写死的
        /// <see cref="InboxMail.slotFlightClock"/>；旧档没有该字段时，按申请单所在机场反推：
        /// 单子写在**经停端或对端**（非本场停站）时，去程离港 = 该刻 − 轮挡 − 过站。
        /// </summary>
        public static string FlightClockOf(InboxMail m, RouteData route)
        {
            if (m == null) return "";
            if (ScheduleSlots.ParseClock(m.slotFlightClock) >= 0)
                return RoutePermitLogic.SlotRequestClock(m.slotFlightClock);

            int mm = ScheduleSlots.ParseClock(SlotMailClock(m));
            if (mm < 0) return "";
            if (route != null && !string.IsNullOrEmpty(route.destination)
                && string.Equals(m.origin, route.destination, StringComparison.OrdinalIgnoreCase))
            {
                mm -= OpsRotationLogic.BlockOf(route) + OpsRotationLogic.TurnOf(route);
                while (mm < 0) mm += 24 * 60;
            }
            return RoutePermitLogic.SlotRequestClock(ScheduleSlots.FormatClock(mm));
        }

        /// <summary>
        /// 时刻槽交割成功后的**自动重排**（2026-10-15 CEO 第十六问：新槽持仓后运营部派哪架机、怎么调航班环）。
        ///
        /// 旧口径把这一步留给玩家：交割回执写一句「请回 n98 重新呈报加机」，要 CEO 自己记住是哪条线、
        /// 哪一刻，再点一遍。**槽都买回来了还要 CEO 回来补手续，等于申请单只办了一半**。
        /// 现在运营部按单子上结构化的 slotRouteId / slotFlightClock 自己接着办：
        /// <list type="number">
        /// <item>本线那一刻若两端槽已齐备 → 用与排班同源的 <see cref="OpsRotationLogic.PickForWindow"/>
        ///   挑一架真排得进的机（值勤/过站/宵禁/串飞一起判）→ 交给 <see cref="QueueAssignFlight"/> 复核
        ///   <see cref="AircraftNominationLogic.CanOperateClock"/> → 呈报一封加机件待批；</item>
        /// <item>另一端还缺槽 → 顺手为重排所需的另一端开新单（闭环，不让缺口停在半路）；</item>
        /// <item>那一刻派不出机或该刻已在飞 → **如实发汇报件**，绝不硬落一班排不出来的班次。</item>
        /// </list>
        ///
        /// 边界仍然是「呈报待批」而不是直接落班：加机是 CEO 拍板的事（§15.8）。
        /// 自动化的界限在**把单子办到该 CEO 拍板的那一步为止**，不是替他把班落下去。
        /// </summary>
        static string RescheduleAfterSlotSettle(GameState gs, InboxMail m)
        {
            if (gs == null || m == null || string.IsNullOrEmpty(m.slotRouteId)) return "";
            var route = RouteById(gs, m.slotRouteId);
            if (route == null)
                return "交割后重排：原申请航线已不在运营，本线不再补班。";

            string clock = FlightClockOf(m, route);
            if (string.IsNullOrEmpty(clock))
                return "交割后重排：申请单未记下目标时刻（旧档），请到航线页重新呈报加机。";

            route.EnsureFlights();
            for (int i = 0; i < route.flights.Count; i++)
            {
                var f = route.flights[i];
                if (f == null) continue;
                if (ScheduleSlots.ClockDiff(f.depTime, clock) > ScheduleSlots.SlotToleranceMinutes) continue;
                return $"交割后重排：{clock} 这一班已在飞（{(string.IsNullOrEmpty(f.registration) ? "—" : f.registration)}），"
                       + "本线无需补班。";
            }

            if (!RoutePermitLogic.CanScheduleClock(gs, route, clock, true))
            {
                // 只补上了一端：另一端若已有申请单在等交割，就不必再开一张（否则同一件事挂两封）；
                // 若根本没有在途申请单，才把重排所需的另一端缺口当场开成新单 —— 不让缺口停在半路等人想。
                if (HasPendingApplyForRoute(gs, route.routeId))
                    return $"交割后重排：本刻另一端仍缺槽，但该端的申请单已在收件箱等候交割（{clock}）；"
                           + "交割到齐后运营部会再自动重排本线。";
                QueueSlotRequestForFlight(gs, route, route.registration, clock, out string pm);
                return "交割后重排：本刻只补齐了一端，另一端仍缺槽 —— " + pm;
            }

            int band = ScheduleSlots.BandOfDep(clock);
            int depMin = ScheduleSlots.ParseClock(clock);
            var plan = new OpsRotationLogic.RotationPlan();
            plan.LoadFrom(gs.activeRoutes);
            var ac = OpsRotationLogic.PickForWindow(route, gs.fleet, plan, band, false, gs,
                                                    out int chosenDep, out string why, depMin);
            if (ac == null || string.IsNullOrEmpty(ac.registration))
            {
                return $"交割后重排：{clock} 的槽已到手，但那一刻派不出机"
                       + (string.IsNullOrEmpty(why) ? "" : $"（{why}）")
                       + "；已发运力核对汇报，请先在机队页腾出一架或调整航班环。";
            }

            // 选出的机可能落在窗口内别的时刻（PickForWindow 会往前推到该机回场之后）：
            // 有持仓时刻却飞别的刻等于违反 WASG 1.7.2d，故以**持仓的那一刻**为准，排不进就如实说。
            string useClock = RoutePermitLogic.SlotRequestClock(ScheduleSlots.FormatClock(chosenDep));
            if (!string.Equals(useClock, clock, StringComparison.Ordinal))
                return $"交割后重排：{clock} 的槽已到手，但 {ac.registration} 最早 {useClock} 才回场，"
                       + "赶不上这一刻；请调整航班环或另派一架。";

            if (QueueAssignFlight(gs, route, ac.registration, clock, out string am))
                return $"交割后重排：已按 {clock} 的槽提名 {ac.registration}（{ac.type}）加班，请批复。{am}";
            return "交割后重排未能呈报：" + am;
        }

        /// <summary>按 routeId 找航线（交割后重排用；<see cref="FindRoute"/> 认的是邮件的 origin/dest）。</summary>
        static RouteData RouteById(GameState gs, string routeId)
        {
            if (gs?.activeRoutes == null || string.IsNullOrEmpty(routeId)) return null;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r != null && string.Equals(r.routeId, routeId, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }

        /// <summary>
        /// 这封邮件**讲的是哪一条航线**（routeId；空 = 不属于任何具体航线）。
        ///
        /// 2026-10-19：收件箱底部要给「涉及具体航线的邮件」都摆一个「详见 · 此航线」入口，
        /// 但各发信方存 routeId 的位置并不统一 —— 运力呈报/排班咨询把 routeId 放在
        /// <see cref="InboxMail.origin"/>，时刻槽单（申请/改时/备选）放在
        /// <see cref="InboxMail.slotRouteId"/>，而 <c>buy_slot</c>/<c>apply_right</c> 的 origin
        /// 是**机场三字码**。所以不能只看 origin 非空，必须确认它真的是现存航线的 routeId，
        /// 否则按钮会把 CEO 送到一条根本不存在的航线上。
        /// </summary>
        public static string RouteIdOf(InboxMail m)
        {
            var gs = GameState.Instance;
            return gs == null ? "" : RouteIdOf(gs, m);
        }

        /// <summary>同上，显式传入存档（发信路径里手里已有 <c>gs</c>，不必再取单例）。</summary>
        public static string RouteIdOf(GameState gs, InboxMail m)
        {
            if (m == null || gs == null) return "";
            if (!string.IsNullOrEmpty(m.slotRouteId) && RouteById(gs, m.slotRouteId) != null)
                return m.slotRouteId;
            if (!string.IsNullOrEmpty(m.origin) && RouteById(gs, m.origin) != null)
                return m.origin;
            // 兜底：字段没写全的单子（早期回执、第三方派生件）按**正文/标题里的航线写法**认。
            // 这是「以某条航线为主题」最直接的证据，也是 CEO 自己判断的依据。
            return RouteIdInText(gs, m);
        }

        /// <summary>
        /// 从标题与正文里认出是哪条航线：只认「现存航线自己的写法」（`PEK—PVG` / `PEK-PVG`），
        /// 不做正则泛匹配 —— 否则正文里随手提到的任意两个三字码都会被当成主题航线。
        /// 先看标题：标题写的是本函主旨，正文里可能顺带提到别的线做对比。
        /// </summary>
        static string RouteIdInText(GameState gs, InboxMail m)
        {
            if (gs?.activeRoutes == null) return "";
            string byText = Scan(m.subject);
            if (!string.IsNullOrEmpty(byText)) return byText;
            return Scan(m.body);

            string Scan(string text)
            {
                if (string.IsNullOrEmpty(text)) return "";
                for (int i = 0; i < gs.activeRoutes.Count; i++)
                {
                    var r = gs.activeRoutes[i];
                    if (r == null || string.IsNullOrEmpty(r.routeId)) continue;
                    if (string.IsNullOrEmpty(r.origin) || string.IsNullOrEmpty(r.destination)) continue;
                    // 破折号（正文最常用）、连字符，以及仓库里另外两种「航线对」写法：
                    // 箭头 → （单向）与 ⇄ （往返）。漏掉后两种会整类邮件认不出线。
                    for (int k = 0; k < RoutePairSeps.Length; k++)
                    {
                        if (text.Contains(r.origin + RoutePairSeps[k] + r.destination))
                            return r.routeId;
                    }
                }
                return "";
            }
        }

        /// <summary>—— / – / - / → / ⇄：代码里出现过的全部「航线对」连接符。</summary>
        static readonly string[] RoutePairSeps = { "—", "–", "-", "→", "⇄" };

        /// <summary>
        /// 旧档回填：给「执行回执」补上它讲的那条航线（<see cref="InboxMail.origin"/>）。
        ///
        /// 2026-10-19：回执由 <c>fx_&lt;源件 id&gt;_&lt;年&gt;&lt;季&gt;</c> 生成，早期版本没把航线抄进来，
        /// 于是读档后底栏认不出线、只能给「航线总览 / 董事会」那对兜底按钮。
        /// 源件 id 就在回执 id 里，故按**最长前缀**反查源件，抄它的 routeId / slotRouteId。
        ///
        /// 幂等：已有 origin 的回执直接跳过；只在能确认源件、且源件属于本线时写入。
        /// </summary>
        public static void BackfillReceiptRouteRefs(GameState gs)
        {
            if (gs?.inbox == null || gs.inbox.Count == 0) return;
            // 源件 id 索引：同一 id 只可能有一封（发信方用确定性 id 去重）。
            var byId = new Dictionary<string, InboxMail>(StringComparer.Ordinal);
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || string.IsNullOrEmpty(m.id)) continue;
                byId[m.id] = m;
            }

            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var r = gs.inbox[i];
                if (r == null || (r.role == null || !r.role.Contains("执行回执"))) continue;
                if (!string.IsNullOrEmpty(r.origin)) continue;
                if (string.IsNullOrEmpty(r.id)) continue;

                // 回执 id 的前缀随发信方而变：运力调令回执是 `fx_cap_<源件 id>`，
                // 其余是 `fx_<源件 id>`（再加年份季度）。只剥 `fx_` 会把运力回执剥成
                // `cap_m_cap_…`，永远匹配不上源件 —— 这正是旧档回执认不出线的原因。
                string rest = null;
                if (r.id.StartsWith("fx_cap_", StringComparison.Ordinal)) rest = r.id.Substring(7);
                else if (r.id.StartsWith("fx_", StringComparison.Ordinal)) rest = r.id.Substring(3);
                if (string.IsNullOrEmpty(rest)) continue;

                InboxMail src = null;
                int best = -1;
                foreach (var kv in byId)
                {
                    var cand = kv.Value;
                    if (cand == r) continue;
                    if (cand.role != null && cand.role.Contains("执行回执")) continue;
                    if (cand.id == null || cand.id.Length <= best) continue;
                    // 回执 id 以「源件 id + _」开头才算同一源件
                    if (rest.Length > cand.id.Length
                        && rest[cand.id.Length] == '_'
                        && rest.StartsWith(cand.id, StringComparison.Ordinal))
                    {
                        best = cand.id.Length;
                        src = cand;
                    }
                }
                if (src == null) continue;

                string routeId = RouteIdOf(gs, src);
                if (string.IsNullOrEmpty(routeId)) continue;
                r.origin = routeId;
            }
        }

        /// <summary>
        /// 这封邮件是否属于「运力 / 时刻槽」这条业务线（呈报、调令回执、槽单、加班提案）。
        ///
        /// 2026-10-19：这类邮件的底栏一律不挂「详见 · 董事会」—— 董事会页讲的是战略与治理，
        /// 回答不了「这条线现在几班、槽卡在哪一步」。而且回执类邮件有时连航线都认不出
        /// （航线已撤、或旧档没存 origin），此时也不能靠 <see cref="RouteIdOf"/> 来判断要不要摘按钮。
        /// </summary>
        public static bool IsCapacityOrSlotMail(InboxMail m)
        {
            if (m == null) return false;
            switch (m.action)
            {
                case CapacityDirectiveAction:
                case ApplySlotAction:
                case ChangeSlotAction:
                case SlotSelectAction:
                case AssignFlightAction:
                case AssignAircraftAction:
                case "buy_slot":
                case "apply_right":
                case "ops_schedule":
                case SeasonReviewAction:
                    return true;
            }
            // 旧档 / 派生件的 role 未必是干净的「执行回执」（可能带后缀，如「执行回执 · 总监 …」），
            // 故用包含匹配而不是相等；再按发信部门兜一层。运营部的回执讲调令落地，
            // 战略发展部的讲时刻槽交割 —— 两者都不该把人送去董事会页。
            if (!string.IsNullOrEmpty(m.role) && m.role.Contains("执行回执"))
            {
                if (m.sender == "运营部" || m.sender == "战略发展部") return true;
            }
            return false;
        }

        /// <summary>本线是否已有时刻槽单在等交割（申请 / 改时；避免交割后重排对同一缺口再开一张）。</summary>
        static bool HasPendingApplyForRoute(GameState gs, string routeId)
        {
            if (string.IsNullOrEmpty(routeId)) return false;
            var pend = PendingSlotSettlements(gs);
            for (int i = 0; i < pend.Count; i++)
            {
                bool slotMail = string.Equals(pend[i].action, ApplySlotAction, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(pend[i].action, ChangeSlotAction, StringComparison.OrdinalIgnoreCase);
                if (slotMail
                    && string.Equals(pend[i].slotRouteId, routeId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        #endregion

        /// <summary>CEO 批准：把呈报里的班次逐条落进航线，并按两端需求占用时刻槽。</summary>
        public static bool ApproveAssignFlight(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            if (route == null)
            {
                Mark(gs, mail.id, "approved");
                mail.body += "\n\n（对应航线已不在运营，本呈报自动结案，无需批复。）";
                message = "对应航线已不在运营，本呈报已自动结案。";
                gs.AddDecisionLog("运营：" + message);
                gs.NotifyChanged();
                return true;
            }

            route.EnsureFlights();
            var list = FlightProposalLogic.Decode(mail.flightProposalsCsv);
            int n = FlightProposalLogic.Apply(gs, route, list,
                out var applied, out var skipped, out var blocked, out var ineligible, out int slotsBound);
            Mark(gs, mail.id, "approved");

            var sb = new System.Text.StringBuilder();
            if (n > 0)
            {
                sb.Append($"已批准 {route.origin}—{route.destination} 加机 {n} 班：");
                for (int i = 0; i < applied.Count; i++)
                {
                    if (i > 0) sb.Append("、");
                    sb.Append($"{applied[i].reg} {applied[i].dep}");
                }
                sb.Append("。");
                if (slotsBound > 0) sb.Append($"已按运行需求占用时刻槽 {slotsBound} 条。");
            }
            if (skipped.Count > 0)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append($"其中 {skipped.Count} 班因同一机号同一时刻已在执飞而跳过：");
                for (int i = 0; i < skipped.Count; i++)
                {
                    if (i > 0) sb.Append("、");
                    sb.Append($"{skipped[i].reg} {skipped[i].dep}");
                }
                sb.Append("。");
            }
            if (blocked.Count > 0)
            {
                // 呈报期间时刻槽易手：宁可如实拒落，也不落一个占不到槽的班次（那等于绕开 80/20 考核硬飞）
                if (sb.Length > 0) sb.Append(' ');
                sb.Append($"另有 {blocked.Count} 班因等待批复期间时刻槽已不再满足而未落班（{SlotGapOfFirst(gs, route, blocked)}）：");
                for (int i = 0; i < blocked.Count; i++)
                {
                    if (i > 0) sb.Append("、");
                    sb.Append($"{blocked[i].reg} {blocked[i].dep}");
                }
                sb.Append("。");
                // 缺槽 = 要申请槽（§15.8）：审批过程中槽被拿走时，顺手把申请单/受阻汇报开出来 ——
                // 只回一句「请先申请」等于把 CEO 刚批过的班次丢在半路，还得玩家自己去别的页面重办一遍。
                for (int i = 0; i < blocked.Count; i++)
                    QueueSlotRequestForFlight(gs, route, blocked[i].reg, blocked[i].dep, out string _);
                sb.Append("已按缺口机场与时刻另行呈报（时刻槽申请单或受阻汇报，见收件箱）；交割后再重新呈报加机。");
            }
            if (ineligible.Count > 0)
            {
                // 机队侧硬门槛（★2026-10-15）：呈报时挑得出的机，等批复的这两三个季度里可能已调走、
                // 退役、值勤已满或换了机型。与「缺槽」分开说，因为处置方向完全不同 ——
                // 缺槽要开申请单，无机要腾运力（换一架 / 调航班环），拿缺槽的话术去答有机的问题只会误导。
                if (sb.Length > 0) sb.Append(' ');
                sb.Append($"另有 {ineligible.Count} 班因执飞机此刻已不满足执飞条件而未落班（");
                for (int i = 0; i < ineligible.Count; i++)
                {
                    if (i > 0) sb.Append("、");
                    sb.Append($"{ineligible[i].reg} {ineligible[i].dep}");
                    string why = AircraftIneligibleWhy(gs, route, ineligible[i]);
                    if (!string.IsNullOrEmpty(why)) sb.Append($"：{why}");
                }
                sb.Append("）。请到机队页换一架合适的飞机，或下「安排一架合适的飞机」调令让运营部重新提名；本班未落班。");
            }
            if (sb.Length == 0) sb.Append("本呈报没有可落班的班次，已结案。");

            message = sb.ToString();
            gs.AddDecisionLog("运营：" + message);
            Schedule(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = "运营部",
                role = "执行回执",
                glyph = "运",
                kind = "report",
                origin = route.routeId,
                dest = route.destination,
                subject = $"{route.origin}—{route.destination} 加机已落班",
                body = message + (n > 0 ? $"\n\n该线现有 {route.flights.Count} 班。" : ""),
                status = "approved",
                tag = n > 0 ? "回执" : "受阻",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ReplyReady(gs.SimUtc));
            gs.NotifyChanged();
            return true;
        }

        /// <summary>批准时被时刻槽拦下的那几班，取首条的缺口说明（供批复正文指路）。</summary>
        static string SlotGapOfFirst(GameState gs, RouteData route, List<FlightProposalLogic.Proposal> blocked)
        {
            if (route == null || blocked == null || blocked.Count == 0) return "";
            var p = blocked[0];
            string dep = FlightProposalLogic.ClockOf(p.dep);
            string gap = RoutePermitLogic.ClockGapText(gs, route, dep);
            return string.IsNullOrEmpty(gap) ? $"{dep} 各停站无可用时刻槽" : gap;
        }

        /// <summary>批准时被机队硬门槛拦下的那一班，取一句拦因（供批复正文指路：换机 / 调环）。</summary>
        static string AircraftIneligibleWhy(GameState gs, RouteData route, FlightProposalLogic.Proposal p)
        {
            if (route == null) return "";
            string dep = FlightProposalLogic.ClockOf(p.dep);
            if (AircraftNominationLogic.CanOperateClock(gs, route, p.reg, dep, out string why, out var _))
                return "";   // 复算时又合格了（如刚腾出的运力）——不编一句不存在的拦因
            return why ?? "";
        }

        /// <summary>CEO 退回：什么都不落班 —— 这正是提案存在邮件里（而非先落班等批复）的原因。</summary>
        public static bool RejectAssignFlight(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            var list = FlightProposalLogic.Decode(mail.flightProposalsCsv);
            Mark(gs, mail.id, "rejected");

            string tag = route != null
                ? $"{route.origin}—{route.destination}"
                : $"{mail.origin}—{mail.dest}";
            var sb = new System.Text.StringBuilder($"已退回 {tag} 的加机呈报 {list.Count} 班，未作任何改动。");
            if (route != null)
            {
                route.EnsureFlights();
                sb.Append($"本航线照常按现有 {route.flights.Count} 班执飞。");
            }
            message = sb.ToString();
            gs.AddDecisionLog("运营：" + message);
            gs.NotifyChanged();
            return true;
        }

        // ———————————————— 执飞飞机提名（单航线页一句话调令 → 运营部提名 → CEO 选定一架） ————————————————

        /// <summary>
        /// 单航线页**没有**「分配执飞飞机」按钮：机号只能靠 CEO 一句话调令产生（「安排一架合适的飞机
        /// 执飞此航班」）。本动作就是那条路的下半段 —— 运营部按硬门槛 + 投影收益提名 2–3 架候选，
        /// 呈报后由 CEO 在邮件里**选定一架**；退回即等于没提过，在飞航线毫发无伤。
        /// </summary>
        public const string AssignAircraftAction = AircraftNominationLogic.Action;

        /// <summary>
        /// 本航线当前该用哪个提名件编号：同一条航线同一时间只允许一封未结案，
        /// 未结案期间再下同类指令就按最新机队重算并写回它；结案（选定/退回）后再下则开新的一批。
        /// 带序号是为了避免「同一 id 两封邮件」——<c>Mark</c> 按 id 只标第一封，同 id 会漏标。
        /// </summary>
        static string AssignAircraftMailId(GameState gs, string routeId)
        {
            for (int n = 1; n <= 999; n++)
            {
                string id = $"m_assign_ac_{routeId ?? "x"}_{n}";
                var m = FindMail(gs, id);
                if (m == null || IsOpen(m)) return id;
            }
            return $"m_assign_ac_{routeId ?? "x"}_{gs.year}{gs.quarter}";
        }

        /// <summary>
        /// 运营部就「安排合适的飞机执飞」呈报候选，或（无机可派时）发一封汇报件。
        ///
        /// 返回 true 表示**已有邮件进箱**（提名件或汇报件都会说明去了哪）；false 只用于参数不全这类
        /// 真失败。之所以不把「无机可派」当失败：玩家该看到的是「为什么没有一架能飞、下一步怎么办」
        /// （<see cref="AircraftNominationLogic.FormatBlockedBody"/>），而不是界面上的一句红字。
        /// </summary>
        public static bool QueueAircraftNominationMail(GameState gs, RouteData route, string directiveRaw, string analysis, out string message)
        {
            message = "";
            if (gs == null || route == null) { message = "航线不可用。"; return false; }
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            route.EnsureFlights();

            var cands = AircraftNominationLogic.Nominate(gs, route);
            bool blocked = cands == null || cands.Count == 0;
            var rejected = AircraftNominationLogic.RejectionDetails(gs, route, blocked ? 8 : 6);

            if (blocked)
            {
                string bid = AssignAircraftMailId(gs, route.routeId) + "_blocked";
                bool existed = FindMail(gs, bid) != null;
                if (!existed)
                {
                    Add(gs, new InboxMail
                    {
                        id = bid,
                        sender = "运营部",
                        // 汇报件（action 留空 = 无需批复）：给一封挑不出东西的决策邮件只是添乱
                        role = "运力核对汇报",
                        glyph = "运",
                        kind = "report",
                        subject = AircraftNominationLogic.Subject(route, true),
                        body = AircraftNominationLogic.FormatBlockedBody(gs, route, rejected),
                        tag = "受阻",
                        status = "approved",
                        directiveNote = directiveRaw ?? "",
                        receivedYear = gs.year,
                        receivedQuarter = gs.quarter,
                        time = gs.DateLabel,
                        deadlineQuarters = 1
                    });
                }
                message = $"运营部已逐机核对：机队里没有能执飞 {route.origin}—{route.destination} 的飞机，"
                          + "已发运力核对汇报（收件箱），列明本线的真正拦因与可选处置。本线维持现状。";
                gs.AddDecisionLog("运营：" + message);
                gs.NotifyChanged();
                return true;
            }

            string id = AssignAircraftMailId(gs, route.routeId);
            string subject = AircraftNominationLogic.Subject(route, false);
            string body = AircraftNominationLogic.FormatBody(gs, route, cands, rejected);            string payload = AircraftNominationLogic.Encode(cands);

            var existing = FindMail(gs, id);
            if (existing != null && IsOpen(existing))
            {
                // 同一条航线未结案期间再下同类指令：按最新机队重算候选，写回同一封（不叠两封待批）
                existing.subject = subject;
                existing.body = body;
                existing.payloadCsv = payload;
                existing.directiveNote = directiveRaw ?? "";

                existing.status = "pending";
                existing.read = false;
                existing.time = gs.DateLabel;
                Changed?.Invoke();
            }
            else
            {
                Add(gs, new InboxMail
                {
                    id = id,
                    sender = "运营部",
                    role = "执飞飞机提名",
                    glyph = "运",
                    kind = "report",
                    action = AssignAircraftAction,
                    origin = route.routeId,
                    dest = route.destination,
                    subject = subject,
                    body = body,
                    tag = "提名",
                    payloadCsv = payload,
                    directiveNote = directiveRaw ?? "",
                    receivedYear = gs.year,
                    receivedQuarter = gs.quarter,
                    time = gs.DateLabel,
                    deadlineQuarters = DefaultDeadlineQuarters
                });
            }

            message = $"运营部已提名 {cands.Count} 架候选（{(string.IsNullOrEmpty(cands[0].reg) ? "—" : cands[0].reg)} 收益最优，"
                      + $"投影日利 {cands[0].profitWan:F2} 万），已呈报收件箱待您选定。";
            gs.AddDecisionLog("运营：按 CEO 指令提名航线 " + $"{route.origin}—{route.destination} 的执飞飞机（{cands.Count} 架候选）。");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 运营部答复件：CEO 在单航线页（或收件箱改稿框）写下的**问句**不走调令链路，
        /// 由这里把 <see cref="OpsQueryLogic.Build"/> 组好的答复投进收件箱。
        ///
        /// 三个刻意的选择，都与「别让答复变成负担」有关：
        /// <list type="number">
        /// <item><b>汇报件、无决策按钮</b>（<c>action</c> 留空、<c>status=approved</c>）：一句询问不该占收件箱的待办，
        ///   更不该因为「没批复」而卡住季度推进 —— 与「无机可派」那封汇报件同一处理；</item>
        /// <item><b>立刻投递</b>（不用 <c>Schedule</c> 的办公节奏延迟）：CEO 正盯着屏幕等答案，
        ///   答复过几个小时才到，他只会以为又没反应；</item>
        /// <item><b>按「航线 + 问题原话」去重</b>：同一句问两遍（他常会再按一次 Enter）只更新同一封，
        ///   不把收件箱刷成一片；台账变了则用最新数据覆写，正文说明口径同源。</item>
        /// </list>
        /// </summary>
        public static bool QueueOpsQueryAnswer(GameState gs, RouteData route, string raw, out string message)
        {
            message = "";
            if (gs == null || route == null) { message = "航线不可用，无法答复。"; return false; }

            var ans = OpsQueryLogic.Build(gs, route, raw);
            if (!ans.ok) { message = ans.body; return false; }

            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string id = QueryMailId(route.routeId, raw);
            var existing = FindMail(gs, id);
            if (existing != null)
            {
                existing.subject = ans.subject;
                existing.body = ans.body;
                existing.directiveNote = raw ?? "";
                existing.origin = route.routeId;      // 老件（上一版没写这两格）顺手补上，深链才认得出本线
                existing.dest = route.destination;
                existing.status = "approved";
                existing.read = false;
                existing.time = gs.DateLabel;
                existing.receivedYear = gs.year;
                existing.receivedQuarter = gs.quarter;
                Changed?.Invoke();
                message = $"运营部已按现台账更新答复（收件箱《{ans.subject}》）："
                        + OpsQueryLogic.TopicTitles(ans.topics) + "。本线班次与机号未作任何改动。";
                gs.AddDecisionLog("运营：答复 CEO 关于 " + $"{route.origin}—{route.destination} 的排班询问（同一问题，按最新台账更新）。");
                gs.NotifyChanged();
                return true;
            }

            Add(gs, new InboxMail
            {
                id = id,
                sender = "运营部",
                // 汇报件（action 留空 = 无需批复）：询问要的是答案，不是再来一封待办
                role = OpsQueryRole,
                glyph = "运",
                kind = "report",
                subject = ans.subject,
                body = ans.body,
                tag = "答复",
                status = "approved",
                // origin/dest 与同部门的呈报件同一口径（origin=routeId、dest=机场三字码）：
                // 信笺抬头、以及「详见 · 此航线」都靠它们认出是哪条线。
                origin = route.routeId,
                dest = route.destination,
                directiveNote = raw ?? "",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });

            message = $"这是一句询问，运营部未改动本线运力 —— 已按本线台账答复（收件箱《{ans.subject}》）："
                    + OpsQueryLogic.TopicTitles(ans.topics) + "。";
            gs.AddDecisionLog($"运营：答复 {route.origin}—{route.destination} 的排班询问「{raw}」。");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>答复件 ID：同一航线 + 同一句问话用同一封（重复提问只刷新，不叠函）。</summary>
        static string QueryMailId(string routeId, string raw)
            => $"m_ops_query_{routeId ?? "x"}_{StableSuffix(CapacityDirectiveLogic.NormalizeRaw(raw))}";

        /// <summary>
        /// CEO 命令台：把某个部门的答复/汇报投进收件箱。
        ///
        /// 与 <see cref="QueueOpsQueryAnswer"/> 刻意保持同一套形状：
        /// · **汇报件**（action 留空、status=approved）：一句提问不该占收件箱的待办，也不该挡住季度推进；
        /// · **立刻投递**（不走办公节奏延迟）：CEO 正盯着底栏等结论；
        /// · **按「部门 + 原话」去重**：同一句话在两面页上问两遍，只刷新同一封，不刷屏。
        /// </summary>
        public static bool QueueDeptAnswer(GameState gs, DeptReportLogic.DeptAnswer ans, string raw, out string message)
            => QueueDeptAnswer(gs, ans, raw, null, out message);

        /// <summary>
        /// <paramref name="idSalt"/> 用来给同一句原话的**后续公函**另开一封：
        /// 关线先出确认卡（不投递），确认后才有处置函 —— 若沿用同一个 id，
        /// 它会把 CEO 提问那封覆盖掉，收件箱里只剩结论、没有来龙去脉。
        /// </summary>
        public static bool QueueDeptAnswer(GameState gs, DeptReportLogic.DeptAnswer ans, string raw,
            string idSalt, out string message)
        {
            message = "";
            if (gs == null || !ans.ok)
            {
                message = string.IsNullOrEmpty(ans.note) ? "没有可投递的答复。" : ans.note;
                return false;
            }

            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string id = $"m_ceo_ask_{ans.deptCode}_{StableSuffix(CapacityDirectiveLogic.NormalizeRaw(raw) + (idSalt ?? ""))}";
            var existing = FindMail(gs, id);
            if (existing != null)
            {
                existing.sender = ans.sender;
                existing.role = ans.role;
                existing.glyph = ans.glyph;
                existing.subject = ans.subject;
                existing.body = ans.body;
                existing.directiveNote = raw ?? "";
                existing.status = "approved";
                existing.read = false;
                existing.time = gs.DateLabel;
                existing.receivedYear = gs.year;
                existing.receivedQuarter = gs.quarter;
                Changed?.Invoke();
                message = $"已按最新台账更新答复（收件箱《{ans.subject}》）。";
                gs.NotifyChanged();
                return true;
            }

            Add(gs, new InboxMail
            {
                id = id,
                sender = ans.sender,
                role = ans.role,
                glyph = ans.glyph,
                kind = "report",
                subject = ans.subject,
                body = ans.body,
                tag = "答复",
                status = "approved",     // 汇报件：无需批复
                directiveNote = raw ?? "",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });

            message = $"已由{ans.sender}答复（收件箱《{ans.subject}》）。";
            gs.AddDecisionLog($"{ans.sender}：答复 CEO 的提问「{raw}」。");
            gs.NotifyChanged();
            return true;
        }


        /// <summary>FNV-1a 摘要（不用 string.GetHashCode：跨会话不稳定，存档里的 ID 会变）。</summary>
        static string StableSuffix(string s)
        {
            uint h = 2166136261u;
            string t = s ?? "";
            for (int i = 0; i < t.Length; i++)
            {
                h ^= t[i];
                h *= 16777619u;
            }
            return h.ToString("x8");
        }

        /// <summary>
        /// CEO 在提名件里选定一架。reg 为空则取候选首位（＝投影日利最优，界面「批准」按钮即此义）。
        ///
        /// 批准时重新过一遍硬门槛（<see cref="AircraftNominationLogic.Apply"/>）：呈报可能在收件箱里
        /// 躺了好几个季度，飞机可能已退役/停场/被租出。不合格就如实拒落，邮件留在收件箱等 CEO 另选。
        /// </summary>
        public static bool ApproveAssignAircraft(GameState gs, InboxMail mail, string reg, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            if (route == null)
            {
                Mark(gs, mail.id, "approved");
                mail.body += "\n\n（对应航线已不在运营，本呈报自动结案，无需批复。）";
                message = "对应航线已不在运营，本呈报已自动结案。";
                gs.AddDecisionLog("运营：" + message);
                gs.NotifyChanged();
                return true;
            }

            var cands = AircraftNominationLogic.Decode(mail.payloadCsv);
            if (string.IsNullOrEmpty(reg) && cands.Count > 0) reg = cands[0].reg;
            if (string.IsNullOrEmpty(reg)) { message = "本呈报没有可选的候选机号，请退回后重新下达指令。"; return false; }

            if (!AircraftNominationLogic.Apply(gs, route, reg, out string applied))
            {
                // 复验不过：不落班、不结案。重算候选写回邮件，让 CEO 换一架或退回。
                message = applied;
                message += RefreshNominationCandidates(gs, mail, route);
                gs.AddDecisionLog("运营：" + applied);
                Changed?.Invoke();
                gs.NotifyChanged();
                return false;
            }

            Mark(gs, mail.id, "approved");
            message = applied;
            gs.AddDecisionLog("运营：" + message);
            Schedule(gs, new InboxMail
            {
                id = "fx_" + mail.id + "_" + gs.year + gs.quarter,
                sender = "运营部",
                role = "执行回执",
                glyph = "运",
                kind = "report",
                origin = route.routeId,
                dest = route.destination,
                subject = $"{route.origin}—{route.destination} 执飞机号已生效",
                body = message + $"\n\n本线 {route.origin}—{route.destination} 自即日起由该机号执行；"
                                + "如需调整班次（加密/减班），请在本航线页下运力调令。",
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ReplyReady(gs.SimUtc));
            gs.NotifyChanged();
            return true;
        }

        /// <summary>批准复验失败后重算候选并写回邮件；返回补在提示语后面的一句（无候选时给出汇报口径）。</summary>
        static string RefreshNominationCandidates(GameState gs, InboxMail mail, RouteData route)
        {
            if (mail == null || route == null) return "";
            var cands = AircraftNominationLogic.Nominate(gs, route);
            if (cands.Count == 0)
            {
                mail.action = "";                       // 挑不出东西了：转汇报件，避免留一封点不动的决策邮件
                mail.status = "approved";
                mail.tag = "受阻";
                mail.body = AircraftNominationLogic.FormatBlockedBody(gs, route, AircraftNominationLogic.RejectionDetails(gs, route, 8));
                return " 运营部已重核机队，当前没有能执飞本线的飞机，本函改为汇报件（无需批复）。";
            }
            mail.payloadCsv = AircraftNominationLogic.Encode(cands);
            mail.body = AircraftNominationLogic.FormatBody(gs, route, cands, AircraftNominationLogic.Rejections(gs, route, 6));
            return " 运营部已按最新机队重算候选，请另选一架或退回。";
        }

        /// <summary>CEO 退回：不指派任何机号 —— 飞机从未写进航线，故无需撤销任何东西。</summary>
        public static bool RejectAssignAircraft(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            Mark(gs, mail.id, "rejected");
            string tag = route != null ? $"{route.origin}—{route.destination}" : $"{mail.origin}—{mail.dest}";
            message = route != null && !route.HasAircraft
                ? $"已退回 {tag} 的执飞飞机提名：不指派机号，本线继续停飞（不计营收）。"
                : $"已退回 {tag} 的执飞飞机提名：不指派新机号，维持现状。";
            gs.AddDecisionLog("运营：" + message);
            gs.NotifyChanged();
            return true;
        }

        // ———————————————— 换季运力评审（每季度一次，运营部呈报待批复） ————————————————

        public const string SeasonReviewAction = "season_review";

        static string SeasonMailId(GameState gs) => $"m_season_{gs.year}_{gs.quarter}";

        /// <summary>运营部提交（或重编）换季运力评审呈报。CEO 批准后才执行。</summary>
        public static bool QueueSeasonReview(GameState gs, SeasonReviewLogic.SeasonPlan plan, string ceoNote, bool force)
        {
            if (gs == null || plan == null) return false;
            if (!force && !plan.HasProposal) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();

            string id = SeasonMailId(gs);
            string subject = $"换季运力评审（第 {gs.year} 年 Q{gs.quarter}）· 机队日循环与运力建议（请批复）";
            string body = SeasonReviewLogic.FormatBody(gs, plan, ceoNote);

            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.id != id) continue;
                m.subject = subject;
                m.body = body;
                m.status = "pending";
                m.read = false;
                m.time = gs.DateLabel;
                m.seasonRev = plan.rev;
                m.seasonDensifyCsv = plan.DensifyCsv;
                m.seasonTrimCsv = plan.TrimCsv;
                m.directiveNote = ceoNote ?? "";
                m.receivedYear = gs.year;
                m.receivedQuarter = gs.quarter;
                Changed?.Invoke();
                gs.NotifyChanged();
                return true;
            }

            Add(gs, new InboxMail
            {
                id = id,
                sender = "运营部",
                role = "换季运力评审",
                glyph = "运",
                kind = "report",
                action = SeasonReviewAction,
                subject = subject,
                body = body,
                tag = "评审",
                seasonRev = plan.rev,
                seasonDensifyCsv = plan.DensifyCsv,
                seasonTrimCsv = plan.TrimCsv,
                directiveNote = ceoNote ?? "",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            gs.AddDecisionLog(
                $"运营：提交 {gs.year} 年 Q{gs.quarter} 换季运力评审（建议加密 {plan.densify.Count} 条 / 减班 {plan.trim.Count} 条 / 释放 {plan.freedCount} 架）。");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>季度例行：本季度尚未评审且确有可优化项时，运营部发起换季评审。</summary>
        public static void MaybeOfferSeasonReview(GameState gs)
        {
            if (gs?.activeRoutes == null || gs.fleet == null) return;
            if (gs.activeRoutes.Count < 2 || gs.fleet.Count < 2) return;   // 单机/单线阶段不打扰
            if (HasMailId(gs, SeasonMailId(gs))) return;                   // 本季度已评审过

            var plan = SeasonReviewLogic.Build(gs, null, apply: false);
            if (!plan.HasProposal) return;
            QueueSeasonReview(gs, plan, null, force: false);
        }

        public static bool ApproveSeasonReview(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var plan = SeasonReviewLogic.FromMail(mail);
            message = SeasonReviewLogic.Apply(gs, plan);
            Mark(gs, mail.id, "approved");
            gs.AddDecisionLog("CEO 批准换季运力评审：" + message);
            Schedule(gs, new InboxMail
            {
                id = "fx_" + mail.id,
                sender = "运营部",
                role = "执行回执",
                glyph = "运",
                kind = "report",
                subject = $"第 {gs.year} 年 Q{gs.quarter} 换季运力评审已执行",
                body = "换季评审已按批准执行。\n\n" + message + "\n\n可在机队页逐机查看新的周排班表（去程 + 回程 + 其它时段的往返）。",
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            }, InboxCadence.ReplyReady(gs.SimUtc));
            gs.NotifyChanged();
            return true;
        }

        public static bool RejectSeasonReview(GameState gs, InboxMail mail, out string message)
        {
            Mark(gs, mail.id, "rejected");
            message = "已退回本期换季评审：维持现有排班与机队分工，本季度不再调整。";
            gs.AddDecisionLog("CEO 退回换季运力评审，维持现有排班。");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>CEO 提出修改：按新意见重编换季方案（keep/increase/reduce）后再报。</summary>
        public static bool ReviseSeasonReview(GameState gs, InboxMail mail, string ceoNote, out string message)
        {
            message = "";
            var d = CapacityDirectiveLogic.Parse(ceoNote);
            // 与运力呈报同源收口：换季方案的方向由 d.intent 决定，而 intent 默认偏增班 ——
            // 「把班次收一收」这种措辞会生成一份**加密**方案（且焦点文字写成「再加密」），
            // 与 CEO 的本意相反。这里拒编并说清怎么改写，草案与现网都不动。
            if (d.intent == "assign")
            {
                // 换季评审只处理班次与日循环：机号指派在单航线页办，别把一条派机令当成加密/减班来解读
                message = "换季评审处理的是班次与机队日循环，不办机号指派。"
                          + "请到该航线页（单航线）下达「安排一架合适的飞机执飞」这类调令。\n"
                          + "（未重编换季方案，现有方案与排班均未改动。）";
                Changed?.Invoke();
                gs?.NotifyChanged();
                return false;
            }
            if (CapacityDirectiveLogic.NeedsConfirm(d))
            {
                message = CapacityDirectiveLogic.ConfirmPrompt(null, d, false)
                          + "\n（未重编换季方案，现有方案与排班均未改动。）";
                Changed?.Invoke();
                gs?.NotifyChanged();
                return false;
            }
            var plan = SeasonReviewLogic.Build(gs, d, apply: false);
            plan.rev = (mail != null ? mail.seasonRev : 0) + 1;
            QueueSeasonReview(gs, plan, ceoNote, force: true);

            string focus = d.intent == "increase" ? "再加密"
                : d.intent == "reduce" ? "减班避险" : "只整合机队日循环";
            message = $"运营部已按您的意见重编换季方案（第 {plan.rev + 1} 稿：{focus}），请再审。";
            gs.AddDecisionLog("运营：" + message);
            Changed?.Invoke();
            gs.NotifyChanged();
            return true;
        }

        public static void NextQuarter(int year, int quarter, out int nextYear, out int nextQuarter)
        {
            nextQuarter = quarter + 1;
            nextYear = year;
            if (nextQuarter > 4)
            {
                nextQuarter = 1;
                nextYear++;
            }
        }

        // ———————————— 时刻槽使用率考核（IATA WASG 80/20 · 每航季一次） ————————————

        /// <summary>CEO 就「正当未用」豁免申请作出裁定的邮件动作。</summary>
        public const string SlotUsageAction = "slot_usage_review";

        static string SlotUsageMailId(int key) => $"m_slotusage_{key}";

        /// <summary>
        /// 换季时刻清算 —— 在每季度推进后由 <see cref="ProcessPendingEffects"/> 调用。
        ///
        /// 时点安排（与真实规则对齐，见 <see cref="SlotUsageLogic"/> 类注释）：
        /// - **航季第 2 个季度初（Q3 / Q1）＝ 历史基准日**：开始本季计数，计数器清零。
        /// - **航季开端（Q2 / Q4）＝ 换季清算**：先执行已到季的失格释放（含到季未获批的豁免申请），
        ///   再结算刚刚结束的上一等价航季，据结果发通报与豁免申请邮件。
        ///
        /// 判季不判年：一年判两次，每次覆盖一个航季的**同季** —— 与「年底判一次」是两回事。
        /// </summary>
        public static void OnSlotUsageSeasonTurn(GameState gs)
        {
            if (gs?.stg == null) return;
            int key = SlotUsageLogic.CurrentKey(gs);

            if (SlotUsageLogic.IsHbdQuarter(gs.quarter))
            {
                SlotUsageLogic.BeginSeason(gs, key);
                return;
            }
            if (!SlotUsageLogic.IsSeasonStartQuarter(gs.quarter)) return;

            // 1) 执行到季的处置：直接失格者释放持仓；待裁豁免到季仍未获批者按失格处置。
            var releaseNotes = new List<string>();
            releaseNotes.AddRange(SlotUsageLogic.ApplyDueReleases(gs, key, out int released));
            releaseNotes.AddRange(SlotUsageLogic.SettlePendingJnus(gs, key, out int jnusReleased));
            int totalReleased = released + jnusReleased;

            // 1b) ★2026-10-15：use-it-or-lose-it 的另一半 —— 收回**整季一次没绑班次**的闲置持仓。
            // 系列考核（80%）只能裁到「有过应飞记录」的槽；买了就搁着、压根不飞的槽没有系列，
            // 旧版因此漏在规则之外。这里按「取得季 + 2 个季度」到点收回，并如实记成资产损失。
            float idleLostWan = 0f;
            var idleNotes = SlotUsageLogic.ReleaseIdleHoldings(gs, RoutePermitLogic.Turn(gs.year, gs.quarter),
                out int idleReleased, out idleLostWan);
            if (idleReleased > 0)
            {
                releaseNotes.AddRange(idleNotes);
                totalReleased += idleReleased;
                QueueSlotIdleReclaimNotice(gs, idleNotes, idleReleased, idleLostWan);
            }
            if (releaseNotes.Count > 0)
                gs.AddDecisionLog($"局方：换季时刻清算 —— {totalReleased} 条持仓退回时刻池。"
                                  + string.Join("；", releaseNotes.ToArray()));

            // 2) 结算刚结束的上一等价航季。
            var verdict = SlotUsageLogic.Evaluate(gs, key - 1);

            // 3) 发信：有未达标就要有下落，避免机制静默生效。
            if (verdict.pendingJnus > 0) QueueSlotUsageReview(gs, verdict);
            if (verdict.lost > 0 || releaseNotes.Count > 0)
                QueueSlotUsageNotice(gs, verdict, releaseNotes, totalReleased);
        }

        /// <summary>
        /// 闲置持仓收回通报（★2026-10-15，use-it-or-lose-it 的另一半）。
        ///
        /// 为什么单独发一封：系列考核的通报名叫「使用率未达标」，而这里的情况是**一次都没飞**、
        /// 压根没有使用率可言。混在同一封里会让 CEO 以为「我飞了但没飞够」，实际是「买了没用」——
        /// 两句账要分开说，损失金额也要明写，否则这笔钱白花了也没人知道。
        /// </summary>
        public static bool QueueSlotIdleReclaimNotice(GameState gs, List<string> notes, int count, float lostWan)
        {
            if (gs == null || notes == null || notes.Count == 0) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();

            var sb = new StringBuilder();
            sb.Append("以下时刻槽自取得起**整个航季未投入任何班次**，"
                      + "按 use-it-or-lose-it 规则退回时刻池（WASG 8.6.1：未使用的时刻不予保留）。\n\n");
            for (int i = 0; i < notes.Count && i < 16; i++) sb.Append($"　{notes[i]}\n");
            if (notes.Count > 16) sb.Append($"　…另有 {notes.Count - 16} 条\n");
            sb.Append($"\n共退回 {count} 条，取得成本合计 {lostWan:F0} 万美元 —— 这部分支出已无法通过运营收回。\n\n");
            sb.Append("下步：\n");
            sb.Append("　1) 若确实需要这些时刻，须在时刻协调窗口内重新申请或购入；\n");
            sb.Append("　2) 时刻槽是**按具体时刻**分配的资源：06:30 的槽接不了 07:00 的班，"
                      + "买来不用就等于白买；\n");
            sb.Append("　3) 拿到槽后请尽快到「运营 → 航线排班」把它绑到对应时刻的班次上。\n");

            string id = $"m_slotidle_{gs.year}q{gs.quarter}";
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && gs.inbox[i].id == id) return false;   // 同一季只通报一次

            Add(gs, new InboxMail
            {
                id = id,
                sender = "局方（时刻协调）",
                role = "时刻槽使用率考核",
                glyph = "协",
                kind = "report",
                subject = $"时刻槽闲置收回：{count} 条未投运，成本 {lostWan:F0} 万美元作废",
                body = sb.ToString(),
                tag = "时刻",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            gs.AddDecisionLog($"局方：闲置时刻槽收回 {count} 条（成本 {lostWan:F0} 万美元）。");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>
        /// 正当未用（JNUS）豁免申请：局方把本季未达标、且**确有正当理由**的系列列出来，
        /// 由 CEO 决定是否申请豁免。批准 → 保留历史优先权；驳回或到期未处理 → 下一等价航季释放。
        /// </summary>
        public static bool QueueSlotUsageReview(GameState gs, SlotUsageLogic.SeasonVerdict v)
        {
            if (gs == null || v == null || v.jnusSeries.Count == 0) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();

            string id = SlotUsageMailId(v.key);
            string subject = $"{v.label} 时刻槽使用率未达标 · 正当未用豁免申请（请裁定）";
            string body = FormatJnusBody(gs, v);
            string csv = SlotUsageLogic.EncodeSeries(v.jnusSeries);

            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.id != id) continue;
                m.subject = subject;
                m.body = body;
                m.slotSeriesCsv = csv;
                m.status = "pending";
                m.read = false;
                m.time = gs.DateLabel;
                m.receivedYear = gs.year;
                m.receivedQuarter = gs.quarter;
                Changed?.Invoke();
                gs.NotifyChanged();
                return true;
            }

            Add(gs, new InboxMail
            {
                id = id,
                sender = "局方（时刻协调）",
                role = "时刻槽使用率考核",
                glyph = "协",
                kind = "approval",
                action = SlotUsageAction,
                subject = subject,
                body = body,
                tag = "时刻",
                slotSeriesCsv = csv,
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            gs.AddDecisionLog(
                $"局方：{v.label} 时刻槽使用率考核 —— {v.kept} 个系列达标、{v.lost} 个直接失格、{v.pendingJnus} 个待申请正当未用豁免。");
            gs.NotifyChanged();
            return true;
        }

        /// <summary>失格通报（纯通报，不需 CEO 批复）：已失格的系列、以及本季退回池子的持仓。</summary>
        public static bool QueueSlotUsageNotice(GameState gs, SlotUsageLogic.SeasonVerdict v,
            List<string> releaseNotes, int releasedCount)
        {
            if (gs == null || v == null) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();

            var sb = new StringBuilder();
            sb.Append($"{v.label} 时刻槽使用率考核已经结束（阈值 {SlotUsageLogic.Threshold * 100f:F0}%）。\n\n");
            sb.Append($"达标保留 {v.kept} 个系列");
            if (v.lost > 0) sb.Append($"，**未达标失格 {v.lost} 个**");
            if (v.pendingJnus > 0) sb.Append($"，待申请正当未用豁免 {v.pendingJnus} 个（另函呈报）");
            sb.Append("。\n\n");

            if (v.lostSeries.Count > 0)
            {
                sb.Append("一、已失格系列（下一等价航季不再自动保留历史优先权）\n");
                for (int i = 0; i < v.lostSeries.Count && i < 12; i++)
                    sb.Append($"　{SlotUsageLogic.FormatSeriesLine(v.lostSeries[i])}\n");
                if (v.lostSeries.Count > 12) sb.Append($"　…另有 {v.lostSeries.Count - 12} 个系列\n");
                sb.Append("\n");
            }

            if (releaseNotes.Count > 0)
            {
                sb.Append($"二、本季已退回时刻池的持仓（{releasedCount} 条）\n");
                for (int i = 0; i < releaseNotes.Count && i < 12; i++)
                    sb.Append($"　{releaseNotes[i]}\n");
                sb.Append("\n");
            }

            sb.Append("三、下步\n");
            sb.Append("　1) 想保住这些时刻，须在时刻协调窗口内重新申请或购买（战略发展部 →「航线网络/时刻槽」）；\n");
            sb.Append("　2) 未重新取得的时段，排班引擎会按「缺槽」处理，不得再加/改班次；\n");
            sb.Append("　3) 下一航季的计分从历史基准日（第 2 个季度初）起算，此前的班次修正不会改变本期结论。\n");

            string id = $"m_slotnotice_{v.key}";
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && gs.inbox[i].id == id) return false;   // 同一季只通报一次

            Add(gs, new InboxMail
            {
                id = id,
                sender = "局方（时刻协调）",
                role = "时刻槽使用率考核",
                glyph = "协",
                kind = "report",
                subject = $"{v.label} 时刻槽使用率考核结果：失格 {v.lost} 个系列",
                body = sb.ToString(),
                tag = "时刻",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            gs.NotifyChanged();
            return true;
        }

        static string FormatJnusBody(GameState gs, SlotUsageLogic.SeasonVerdict v)
        {
            var sb = new StringBuilder();
            sb.Append($"{v.label} 时刻槽使用率考核已完成。以下系列未达 {SlotUsageLogic.Threshold * 100f:F0}%，"
                      + "但本季确有**正当未用**事由（WASG §8.8 Justified Non-Utilisation）。\n\n");

            for (int i = 0; i < v.jnusSeries.Count; i++)
            {
                var x = v.jnusSeries[i];
                if (x == null) continue;
                int gap = x.due - x.operated;
                sb.Append($"　{SlotUsageLogic.SeriesLabel(x.airport, x.time, x.weekday)}："
                          + $"应飞 {x.due} 班、实飞 {x.operated} 班、未飞 {gap} 班，"
                          + $"使用率 {x.operated / (float)x.due * 100f:F0}%\n");
                if (!string.IsNullOrEmpty(x.jnusReasonCsv))
                    sb.Append($"　　正当事由：{x.jnusReasonCsv}（其中正当 {x.justified} 班）\n");
            }

            sb.Append("\n批准 = 向局方申请豁免：该系列历史优先权保留，下一等价航季不受影响。\n");
            sb.Append("驳回或逾期未处理 = 按未达标处置：下一等价航季历史优先权终止，"
                      + "对应持仓退回时刻池，须重新申请或购买。\n");
            sb.Append("\n提示：豁免只认「你确实飞了但飞不了」的班次；本季若因排班调整、主动减班而未飞，"
                      + "那些班次不计入正当未用。");
            return sb.ToString();
        }

        /// <summary>CEO 批准正当未用豁免：逐系列保留历史优先权。</summary>
        public static bool ApproveSlotUsage(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var list = SlotUsageLogic.DecodeSeries(mail?.slotSeriesCsv);
            int ok = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var x = list[i];
                if (SlotUsageLogic.GrantJnus(gs, x.airport, x.time, x.weekday)) ok++;
            }
            Mark(gs, mail.id, "approved");
            message = ok > 0
                ? $"已申请正当未用豁免 {ok} 个时刻系列，历史优先权予以保留。"
                : "本函已无可裁定的时刻系列（可能已被后续考核取代）。";
            gs.AddDecisionLog("CEO 批准时刻槽正当未用豁免：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>CEO 驳回正当未用豁免：转为失格，仍按「下一等价航季开端释放」执行。</summary>
        public static bool RejectSlotUsage(GameState gs, InboxMail mail, out string message)
        {
            var list = SlotUsageLogic.DecodeSeries(mail?.slotSeriesCsv);
            int ok = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var x = list[i];
                if (SlotUsageLogic.DenyJnus(gs, x.airport, x.time, x.weekday)) ok++;
            }
            Mark(gs, mail.id, "rejected");
            message = $"已放弃申请豁免：{ok} 个系列按未达标处置，将于下一等价航季开端释放对应时刻。";
            gs.AddDecisionLog("CEO 放弃时刻槽正当未用豁免：" + message);
            gs.NotifyChanged();
            return true;
        }

        /// <summary>当前是否有到期未处理的待办邮件（阻断时间推进）。</summary>
        public static bool HasDueMails(GameState gs)
        {
            if (gs == null || gs.inbox == null) return false;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (IsDue(gs.inbox[i], gs.year, gs.quarter)) return true;
            return false;
        }

        /// <summary>取第一封到期邮件（强制跳转对象）。</summary>
        public static InboxMail FirstDueMail(GameState gs)
        {
            if (gs == null || gs.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (IsDue(gs.inbox[i], gs.year, gs.quarter)) return gs.inbox[i];
            return null;
        }

        /// <summary>待处理邮件数（批准/拒绝前）。</summary>
        public static int OpenCount(GameState gs)
        {
            if (gs == null || gs.inbox == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (IsOpen(gs.inbox[i])) n++;
            return n;
        }

        /// <summary>未读邮件数（底栏红点：点开详情后减少，全部点开后为 0）。</summary>
        public static int UnreadCount(GameState gs)
        {
            if (gs == null || gs.inbox == null) return 0;
            int n = 0;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && !gs.inbox[i].read) n++;
            return n;
        }

        public static string StatusLabel(string status)
        {
            switch (status)
            {
                case "approved": return "已批准";
                case "deferred": return "已暂缓";
                case "rejected": return "已拒绝";
                case SupersededStatus: return "已作废";
                default: return "待处理";
            }
        }

        /// <summary>
        /// 收件箱状态标签（T05.2）。
        ///
        /// 旧版「已批准」同时覆盖两种情况：① 批了、事已办完（已办结）；② 批了、但要等下一个
        /// 交割季才落地（`effectPending`）。两者在列表里长得一模一样，CEO 无法判断「这封还要不要跟」。
        /// 这里把 ② 单独标成「已批准·待交割」，并带上交割季，读者不必点开正文去数季度。
        /// </summary>
        public static string StatusLabel(GameState gs, InboxMail m)
        {
            if (m == null) return "待处理";
            string baseLabel = StatusLabel(m.status);
            if (m.status == "approved" && m.effectPending)
            {
                bool future = m.effectYear > 0 && m.effectQuarter > 0
                              && (gs == null || Turn(m.effectYear, m.effectQuarter) > Turn(gs.year, gs.quarter));
                return future || gs == null ? $"{baseLabel}·待交割" : $"{baseLabel}·交割中";
            }
            return baseLabel;
        }

        /// <summary>
        /// 统一状态行（T05.2）：「在等什么 · 何时推进 · 是否需 CEO」三件事一句话说完。
        ///
        /// 旧版这三件事散落在命令条回执、详情页底注、公文正文里，且六阶段用词各不相同；
        /// 玩家必须读完整封信才知道「这封是在等我，还是在等别人」。
        /// 本函数是**唯一出口**：详情页底注、在途提示都从这里取词。
        /// </summary>
        public static string StatusLine(GameState gs, InboxMail m)
        {
            if (m == null) return "";
            string waiting, when, need;
            bool needsMe;

            if (m.action == "news" || m.kind == "media" || m.kind == "alert")
            {
                // 外部通报：不是「在等谁」，也不用 CEO 批复；只说明这条是已生效还是预告
                waiting = "不适用（外部通报）";
                when = m.tag == "生效" ? "已生效"
                     : m.tag == "预告" ? "待生效（正文有生效时点）"
                     : "—";
                needsMe = false;
            }
            else if (m.status == "approved" && m.effectPending)
            {
                waiting = "交割 / 落地";
                when = m.effectYear > 0 && m.effectQuarter > 0
                    ? $"第{m.effectYear}年 Q{m.effectQuarter}"
                    : "下季内";
                needsMe = false;
            }
            else if (m.status == "rejected")
            {
                waiting = "无（已拒绝）";
                when = "—";
                needsMe = false;
            }
            else if (m.status == "deferred")
            {
                waiting = "补条件后重新上会";
                when = "条件齐备即办";
                needsMe = true;
            }
            else if (IsOpen(m))
            {
                bool decide = RequiresDecision(m);
                waiting = decide ? "CEO 批复" : "CEO 知悉（无需批复）";
                if (gs != null)
                {
                    int rem = RemainingQuarters(m, gs.year, gs.quarter);
                    when = rem < 0 ? "已到期" : $"本季内（还可搁置 {rem + 1} 个季度）";
                }
                else when = "本季内";
                needsMe = decide;
            }
            else
            {
                waiting = "无（已办结）";
                when = "—";
                needsMe = false;
            }

            need = needsMe ? "是" : "否";
            return $"在等　{waiting}　｜　何时推进　{when}　｜　需 CEO　{need}";
        }

        /// <summary>
        /// 在途送达提示（T05.2）：`pendingInbox` 里躺着将要送达的邮件，
        /// 但旧版**没有任何 UI 读取它** —— 玩家下了令、部门说「约 2 小时后送达」，
        /// 然后收件箱一动不动，看起来像没生效。这里给出「在途几封、最近一封何时到」。
        ///
        /// 时间按游戏时钟算（1× 下 1 游戏小时 = 1 现实小时），不是现实 UTC 差值。
        /// </summary>
        public static string InflightLine(GameState gs)
        {
            if (gs?.pendingInbox == null || gs.pendingInbox.Count == 0) return "";
            int n = 0;
            long earliest = long.MaxValue;
            for (int i = 0; i < gs.pendingInbox.Count; i++)
            {
                var p = gs.pendingInbox[i];
                if (p?.mail == null) continue;
                n++;
                if (p.deliverUtcTicks < earliest) earliest = p.deliverUtcTicks;
            }
            if (n == 0) return "";
            double hours = (earliest - gs.SimUtc.Ticks) / (double)TimeSpan.TicksPerHour;
            string eta = hours <= 0.01
                ? "正在送达"
                : hours < 1
                    ? $"约 {Math.Max(1, (int)Math.Ceiling(hours * 60))} 分钟后送达"
                    : $"约 {Math.Ceiling(hours)} 小时后送达";
            return $"在途 {n} 封（最近一封{eta}）";
        }

        public static OpsIrropsLogic.IrropsKind ParseIrropsKind(InboxMail mail)
        {
            if (mail == null || !int.TryParse(mail.origin, out int k))
                return OpsIrropsLogic.IrropsKind.Weather;
            if (k < 0) k = 0;
            if (k > 3) k = 3;
            return (OpsIrropsLogic.IrropsKind)k;
        }

        public static void ApplyIrropsOutcome(GameState gs, OpsIrropsLogic.IrropsKind kind, OpsIrropsLogic.Outcome outcome)
        {
            if (gs == null) return;
            gs.cash += outcome.cashDeltaWan;
            gs.onTimePercent = Mathf.Clamp(gs.onTimePercent + outcome.onTimeDelta, 40f, 99f);
            gs.boardSatisfaction = Mathf.Clamp(gs.boardSatisfaction + outcome.boardDelta, 0f, 100f);
            gs.AddDecisionLog(
                $"航班异常[{OpsIrropsLogic.KindLabel(kind)}] {outcome.label}：" +
                $"现金 {outcome.cashDeltaWan:F0}，准点 {outcome.onTimeDelta:+0;-0}，董事会 {outcome.boardDelta:+0;-0}。");
            gs.NotifyChanged();
        }

        public static void CompleteIrropsMail(string mailId, OpsIrropsLogic.Outcome outcome)
        {
            var gs = GameState.Instance;
            if (gs == null || string.IsNullOrEmpty(mailId)) return;
            Mark(gs, mailId, "approved");
            Add(gs, new InboxMail
            {
                id = "fx_" + mailId + "_" + gs.year + gs.quarter,
                sender = "运营部",
                role = "执行回执",
                glyph = "运",
                kind = "report",
                subject = "航班异常已处置：" + (outcome.label ?? ""),
                body = outcome.summary ?? "",
                status = "approved",
                tag = "回执",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        public static bool RejectIrrops(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            if (gs == null || mail == null) { message = "邮件无效。"; return false; }
            var kind = ParseIrropsKind(mail);
            var outcome = OpsIrropsLogic.Resolve(kind, OpsIrropsLogic.Choice.C);
            ApplyIrropsOutcome(gs, kind, outcome);
            Mark(gs, mail.id, "rejected");
            message = "已拒绝专项处置，按方案 C 收口：" + outcome.summary;
            return true;
        }

        /// <summary>季度随机注入 IRROPS 警报邮件（定稿 n24）。</summary>
        public static void MaybeOfferIrrops(GameState gs)
        {
            if (gs == null) return;
            if (gs.year <= 1 && gs.quarter <= 1) return;
            if (HasOpenAction(gs, "irrops")) return;
            if (OfferedIrropsThisQuarter(gs)) return;
            int key = gs.year * 4 + gs.quarter;
            if ((key * 13 + 7) % 10 >= 4) return;

            var kinds = new[]
            {
                OpsIrropsLogic.IrropsKind.Weather,
                OpsIrropsLogic.IrropsKind.Aog,
                OpsIrropsLogic.IrropsKind.Atc,
                OpsIrropsLogic.IrropsKind.CrewShortage
            };
            var kind = kinds[Mathf.Abs(key) % kinds.Length];
            string route = "枢纽";
            if (gs.activeRoutes != null && gs.activeRoutes.Count > 0 && gs.activeRoutes[0] != null)
                route = $"{gs.activeRoutes[0].origin}-{gs.activeRoutes[0].destination}";

            Add(gs, new InboxMail
            {
                id = "m_irrops_" + gs.year + "q" + gs.quarter,
                sender = "运营部",
                role = "运行控制",
                glyph = "运",
                kind = "alert",
                action = "irrops",
                origin = ((int)kind).ToString(),
                dest = route,
                subject = $"【警报】{OpsIrropsLogic.KindLabel(kind)} · {route}",
                body = OpsIrropsLogic.ScenarioBody(kind) +
                    "\n\n建议：点批准打开航班异常 处置弹窗（A/B/C）。拒绝则按最省方案 C 收口。可点关键词查看航线网络。",
                tag = "紧急",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
        }

        static bool OfferedIrropsThisQuarter(GameState gs)
        {
            if (gs?.inbox == null) return false;
            string id = "m_irrops_" + gs.year + "q" + gs.quarter;
            return HasMailId(gs, id);
        }
    }
}
