using System;
using System.Collections.Generic;
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

        /// <summary>未读数变化（底栏角标刷新）。</summary>
        public static event System.Action Changed;

        /// <summary>供外部系统（如决策会议回写邮件）触发收件箱刷新。</summary>
        public static void NotifyChanged() => Changed?.Invoke();

        public static int Turn(int year, int quarter) => year * 4 + (quarter - 1);

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
            => IsOpen(m) && MeetingOutcomeOf(m) != "deferred" && RemainingQuarters(m, year, quarter) < 0;

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
                || mail.action == "buy_slot" || mail.action == ApplySlotAction || mail.action == "apply_right"
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
                else if (mail.action == "buy_slot" || mail.action == ApplySlotAction)
                {
                    string apt = string.IsNullOrEmpty(mail.origin) ? (gs.hubIata ?? "HUB") : mail.origin;
                    int band = StrategyManager.ParseSlotBand(mail.dest);
                    int qty = SlotQtyOf(mail);
                    message = qty > 1
                        ? $"已批准申请 {apt} {StrategyManager.SlotBandName(band)}时段时刻槽 {qty} 个。将于第{ey}年 Q{eq} 扣款交割。"
                        : $"已批准购买 {apt} {StrategyManager.SlotBandName(band)}时段时刻槽。将于第{ey}年 Q{eq} 扣款交割。";
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

            if (mail.action == CapacityDirectiveAction)
                return ApproveCapacityDirective(gs, mail, out message);

            if (mail.action == TransitionBusinessLogic.InquiryAction)
                return TransitionBusinessLogic.SignAllIdleFromInbox(gs, mail, out message);

            if (mail.action == SeasonReviewAction)
                return ApproveSeasonReview(gs, mail, out message);

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

                    if (m.action == "buy_slot" || m.action == ApplySlotAction || m.action == "apply_right")
                    {
                        EnsureStrategyManager();
                        string execMsg;
                        bool ok;
                        if (m.action == "buy_slot" || m.action == ApplySlotAction)
                        {
                            string apt = string.IsNullOrEmpty(m.origin) ? (gs.hubIata ?? "PEK") : m.origin;
                            int qty = SlotQtyOf(m);
                            ok = StrategyManager.AcquireSlots(gs, apt, StrategyManager.ParseSlotBand(m.dest), qty, out execMsg);
                        }
                        else
                        {
                            string o = string.IsNullOrEmpty(m.origin) ? "PEK" : m.origin;
                            string d = string.IsNullOrEmpty(m.dest) ? "SIN" : m.dest;
                            ok = StrategyManager.Instance.ApplyRouteRight(o, d, 5, out execMsg);
                        }
                        AddExecReceipt(gs, m, ok,
                            ok ? (m.action == "apply_right" ? "航权已获批" : "时刻槽已交割")
                               : (m.action == "apply_right" ? "航权申请未获批" : "时刻槽未能购入"),
                            execMsg);
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
                        bool ok = MaintenanceManager.Instance.ConfirmOrder(m.origin, out string execMsg);
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

            int slotIndex = -1, slotIndexB = -1;
            string originApt = fromHub ? (string.IsNullOrEmpty(gs.hubIata) ? o : gs.hubIata) : o;
            if (needsSlot)
            {
                bool needA = RoutePermitLogic.NeedsDepartureSlot(originApt);
                bool needB = RoutePermitLogic.NeedsDepartureSlot(d);
                if (needA) slotIndex = RoutePermitLogic.ReserveSlotAt(gs, originApt);
                if (needB) slotIndexB = RoutePermitLogic.ReserveSlotAt(gs, d);
                bool gotA = !needA || slotIndex >= 0;
                bool gotB = !needB || slotIndexB >= 0;
                if (!gotA || !gotB)
                {
                    // 往返两端各占一条：缺任一端都不能开航（先证后槽，此时才涉及时刻花费）
                    if (slotIndex >= 0) RoutePermitLogic.ReleaseSlot(gs, slotIndex);
                    if (slotIndexB >= 0) RoutePermitLogic.ReleaseSlot(gs, slotIndexB);
                    slotIndex = -1;
                    slotIndexB = -1;
                    bool first = m.permitGate != "时刻";
                    string apt = !gotA ? originApt : d;
                    if (first || !HasOpenAction(gs, "buy_slot"))
                        QueueSlotDecision(gs, apt, 1, null, out _);   // 已有待批提案时内部会自守
                    m.permitGate = "时刻";
                    m.permitNote = kind + " · 已核准 · 待时刻";
                    DeferPermit(m, 2);
                    AddPermitReceipt(gs, m, "slotwait", $"准入已核准：{o}-{d} 待协调时刻槽",
                        InboxLetter.Section("发证") +
                        InboxLetter.Bullet($"{kind}已核准（第{gs.year}年 Q{gs.quarter}），许可证已到手，可对外售票准备") +
                        InboxLetter.Section("当前节点") +
                        InboxLetter.Bullet($"第{RoutePermitLogic.NodeSlot}节点 时刻协调与开航：尚无可用时刻槽") +
                        InboxLetter.Bullet($"缺口端　{apt}（{AirportCatalog.TierLabelOf(apt)}，协调机场，按夏秋/冬春协调季分配，热门时段需竞拍或购买）") +
                        InboxLetter.Bullet($"两端口径　往返班次在 {originApt}（去程离港）与 {d}（回程离港）各需 1 条对应时段持仓") +
                        InboxLetter.Section("已采取的措施") +
                        InboxLetter.Bullet(first
                            ? "已请战略发展部在收件箱提出时刻槽购买方案（许可已到手，此时交割时刻费用，不会白花）"
                            : "时刻槽购买方案已在收件箱待批；若已拒绝，战略发展部会重新提出") +
                        InboxLetter.Section("为什么先证后槽") +
                        InboxLetter.Bullet("时刻（尤其竞拍时段）先花钱再等证，一旦许可未获批准费用即打水漂；先取证再办时刻更稳妥。") +
                        InboxLetter.Section("后续") +
                        InboxLetter.Bullet($"两端时刻齐备后自动排班开航（预计 第{m.permitDueYear}年 Q{m.permitDueQuarter} 复核）")
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
            bool slotTaken = slotIndex >= 0 || slotIndexB >= 0;

            if (result != null && result.success)
            {
                m.permitGate = "";
                m.permitNode = RoutePermitLogic.NodeSlot;
                m.permitNote = kind + " · 已发证并开航";
                m.effectPending = false;
                var slotApts = new System.Collections.Generic.List<string>();
                if (slotIndex >= 0) slotApts.Add(originApt);
                if (slotIndexB >= 0) slotApts.Add(d);
                AddPermitReceipt(gs, m, "grant", $"航线 {o}-{d} 已获许可并投入运营",
                    InboxLetter.Section("发证") +
                    InboxLetter.Bullet($"{kind}已核准") +
                    InboxLetter.Section("时刻协调") +
                    InboxLetter.Bullet(slotTaken
                        ? $"已占两端时刻槽（{string.Join("、", slotApts.ToArray())}），可排班飞行"
                        : "本航线无需新增时刻（非协调机场）") +
                    InboxLetter.Section("开航执行") +
                    InboxLetter.Bullet($"开航季度　第{gs.year}年 Q{gs.quarter}（第{m.permitNode}节点 {RoutePermitLogic.NodeName(RoutePermitLogic.NodeSlot)}）") +
                    InboxLetter.Bullet(result.message) +
                    PermitProgressBlock(m, intl, needsSlot, false), "发证");
                return;
            }

            // 开航未成（现金/运力等）→ 退还时刻槽，许可证继续有效，待条件就绪再试
            if (slotIndex >= 0) RoutePermitLogic.ReleaseSlot(gs, slotIndex);
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
                message = "未找到对应的许可办理单，请重新由运营部召集研究。";
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
            message = $"已放弃 {mail.origin}-{mail.dest} 的准入申请，许可办理终止（日后可重新召集研究）。";
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

        static void AddExecReceipt(GameState gs, InboxMail src, bool ok, string subject, string body)
        {
            Schedule(gs, new InboxMail
            {
                id = "fx_" + src.id + "_" + gs.year + gs.quarter,
                sender = "战略发展部",
                role = "执行回执",
                glyph = "战",
                kind = "report",
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
                QueueSlotDecision(gs, hub, 1, "m_slot_hub", out _);
                return;
            }

            if (!StrategyManager.HasRouteRight(gs, "PEK", "SIN")
                && !HasMailId(gs, "m_right_pek_sin") && !HasOpenAction(gs, "apply_right"))
            {
                QueueRightDecision(gs, "PEK", "SIN", "m_right_pek_sin", out _);
            }
        }

        public static bool QueueSlotDecision(GameState gs, string airport, int band, string id, out string message)
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
            int b = Mathf.Clamp(band, 0, 3);
            float cost = StrategyEconomics.SlotValueWan(apt, b);   // 按机场真实等级定价
            if (RoutePermitLogic.SlotPoolFull(gs, apt, b))
            {
                message = $"该机场该时段时刻池已满（{RoutePermitLogic.SlotPoolLine(gs, apt, b)}），暂不便再提申请。";
                return false;
            }
            string mailId = string.IsNullOrEmpty(id) ? "m_slot_" + apt + "_" + gs.year + "q" + gs.quarter : id;
            if (HasMailId(gs, mailId))
            {
                message = "本季已提交同时刻槽提案。";
                return false;
            }

            Add(gs, new InboxMail
            {
                id = mailId,
                sender = "战略发展部",
                role = "航权提案",
                glyph = "战",
                kind = "report",
                action = "buy_slot",
                origin = apt,
                dest = b.ToString(),
                subject = $"建议购买 {apt} {StrategyManager.SlotBandName(b)}时段时刻槽",
                body =
                    InboxLetter.Section("提案要点") +
                    InboxLetter.Bullet($"机场　{apt}（{AirportCatalog.TierLabelOf(apt)}）") +
                    InboxLetter.Bullet($"时段　{StrategyManager.SlotBandName(b)}") +
                    InboxLetter.Bullet($"估价　约 {cost:F0} 万美元") +
                    InboxLetter.Bullet($"池况　{RoutePermitLogic.SlotPoolLine(gs, apt, b)}") +
                    InboxLetter.Section("时点与影响") +
                    InboxLetter.Bullet("批准：下季度扣款交割，可加密高峰班次") +
                    InboxLetter.Bullet("拒绝：本季不扩容时刻，高峰难以加密") +
                    InboxLetter.Bullet("两端持槽：往返班次在离港机场与对端机场各占 1 个对应时段槽") +
                    InboxLetter.Bullet("持仓与进度见「航权与时刻槽」页"),
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            message = "已提交收件箱：请批准购买时刻槽，下季度扣款交割。";
            return true;
        }

        public static int SlotQtyOf(InboxMail mail)
        {
            int n = mail != null ? mail.permitAttempts : 1;
            return n < 1 ? 1 : n;
        }

        /// <summary>已批准、尚未到交割季的时刻槽提案（buy_slot / apply_slot）。</summary>
        public static List<InboxMail> PendingSlotSettlements(GameState gs)
            => PendingSettlements(gs, "buy_slot", ApplySlotAction);

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
            string band = StrategyManager.SlotBandName(StrategyManager.ParseSlotBand(m.dest));
            return $"{apt} {band}时段 {SlotQtyOf(m)} 个　待交割 · 第{m.effectYear}年 Q{m.effectQuarter}";
        }

        public static string FormatPendingRight(InboxMail m)
        {
            if (m == null) return "";
            string o = string.IsNullOrEmpty(m.origin) ? "—" : m.origin;
            string d = string.IsNullOrEmpty(m.dest) ? "—" : m.dest;
            return $"{o}—{d}  第五航权　待交割 · 第{m.effectYear}年 Q{m.effectQuarter}";
        }

        /// <summary>运力缺口里的缺槽波次 → 按「机场 × 时段」汇总（往返两端分别归集），发批量申请邮件（下季交割）。</summary>
        public static void QueueSlotApplications(GameState gs, RouteData route, List<OpsScheduleLogic.WaveShortage> shortages)
        {
            if (gs == null || route == null || shortages == null || shortages.Count == 0) return;
            var order = new List<string>();
            var counts = new Dictionary<string, int>();
            var apts = new Dictionary<string, string>();
            var bands = new Dictionary<string, int>();
            for (int i = 0; i < shortages.Count; i++)
            {
                var s = shortages[i];
                if (s == null || s.kind != "no_slot") continue;
                string apt = string.IsNullOrEmpty(s.slotAirport) ? route.origin : s.slotAirport;
                int band = s.slotBand >= 0 ? s.slotBand : ScheduleSlots.BandOfDep(s.dep);
                string key = apt.ToUpperInvariant() + "|" + band;
                if (!counts.ContainsKey(key)) { counts[key] = 0; order.Add(key); apts[key] = apt; bands[key] = band; }
                counts[key]++;
            }
            for (int k = 0; k < order.Count; k++)
            {
                string key = order[k];
                string apt = apts[key];
                int band = bands[key];
                QueueApplySlots(gs, apt, band, counts[key],
                    "m_apply_slot_" + apt + "_" + band + "_" + gs.year + "q" + gs.quarter, out _);
            }
        }

        /// <summary>批量申请 N 个时刻槽：批准后下季扣款交割。</summary>
        public static bool QueueApplySlots(GameState gs, string airport, int band, int count, string id, out string message)
        {
            message = "";
            if (gs == null) { message = "游戏未就绪"; return false; }
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            string apt = string.IsNullOrEmpty(airport) ? (gs.hubIata ?? "PEK") : airport;
            int b = Mathf.Clamp(band, 0, 3);
            int n = count < 1 ? 1 : count;
            if (RoutePermitLogic.SlotPoolFull(gs, apt, b)
                && RoutePermitLogic.FreeSlots(gs, apt, b) <= 0)
            {
                message = $"该机场该时段时刻池已满（{RoutePermitLogic.SlotPoolLine(gs, apt, b)}），暂不便再提申请。";
                return false;
            }
            string mailId = string.IsNullOrEmpty(id) ? "m_apply_slot_" + apt + "_" + b + "_" + gs.year + "q" + gs.quarter : id;
            if (HasMailId(gs, mailId) || HasOpenApplySlot(gs, apt, b))
            {
                message = "同时段已有时刻槽申请在收件箱。";
                return false;
            }

            float unit = StrategyEconomics.SlotValueWan(apt, b);
            float cost = unit * n;
            Add(gs, new InboxMail
            {
                id = mailId,
                sender = "战略发展部",
                role = "航权提案",
                glyph = "战",
                kind = "report",
                action = ApplySlotAction,
                origin = apt,
                dest = b.ToString(),
                permitAttempts = n,
                subject = n > 1
                    ? $"建议申请 {apt} {StrategyManager.SlotBandName(b)}时段时刻槽 {n} 个"
                    : $"建议申请 {apt} {StrategyManager.SlotBandName(b)}时段时刻槽",
                body =
                    InboxLetter.Section("提案要点") +
                    InboxLetter.Bullet($"机场　{apt}（{AirportCatalog.TierLabelOf(apt)}）") +
                    InboxLetter.Bullet($"时段　{StrategyManager.SlotBandName(b)}") +
                    InboxLetter.Bullet($"数量　{n} 个（往返两端各占 1 个：去程离港 + 回程离港）") +
                    InboxLetter.Bullet($"估价　约 {cost:F0} 万美元（单价 {unit:F0} 万）") +
                    InboxLetter.Bullet($"池况　{RoutePermitLogic.SlotPoolLine(gs, apt, b)}") +
                    InboxLetter.Section("时点与影响") +
                    InboxLetter.Bullet("批准：下季度扣款交割，交割后方可加密该时段班次") +
                    InboxLetter.Bullet("拒绝：该时段继续缺槽，运营部无法加班") +
                    InboxLetter.Bullet("持仓与进度见「航权与时刻槽」页"),
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            message = $"已提交收件箱：请批准申请 {apt} {StrategyManager.SlotBandName(b)}时段 {n} 个时刻槽，下季度扣款交割。";
            return true;
        }

        static bool HasOpenApplySlot(GameState gs, string airport, int band)
        {
            if (gs?.inbox == null) return false;
            string b = band.ToString();
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != ApplySlotAction) continue;
                if (!IsOpen(m) && !m.effectPending) continue;
                if (!string.Equals(m.origin, airport, StringComparison.OrdinalIgnoreCase)) continue;
                if (m.dest != b) continue;
                return true;
            }
            return false;
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

        public static void NotifyHedgeSettle(GameState gs, string settleText)
        {
            if (gs == null || string.IsNullOrEmpty(settleText)) return;
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
            gs.AddDecisionLog($"开航：运营部开始召集研究 {origin}-{dest}，纪要约 {InboxCadence.StudyBusinessDays} 个工作日后呈报。");
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

        /// <summary>运营部执行完 CEO 运力指令后，把「调整后运力 + 航班环」呈报给 CEO。</summary>
        public static bool QueueCapacityReport(GameState gs, RouteData route, CapacityDirectiveLogic.Report rep)
        {
            if (gs == null || route == null || rep == null || !rep.ok) return false;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();

            // 原排班呈报若还挂着，标记为已被指令取代，避免同一航线两封待办互相阻断
            SupersedeOpsSchedule(gs, route);
            // 同一航线更早的运力呈报也已过时（新调令已重编）
            SupersedeCapacityReports(gs, route, null);

            string id = CapacityMailId(route.routeId, route.opsPlanRevision);
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
                    m.tag = rep.slotFirst ? "待槽" : "呈报";
                    QueueSlotApplications(gs, route, rep.shortages);
                    Changed?.Invoke();
                    gs.NotifyChanged();
                    return true;
                }
            }

            Add(gs, new InboxMail
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
            });
            gs.AddDecisionLog(
                $"运营：按 CEO 指令调整 {route.origin}—{route.destination} 运力（{route.DualWeeklyLine()}），已呈报待批。");
            QueueSlotApplications(gs, route, rep.shortages);
            gs.NotifyChanged();
            return true;
        }

        static void SupersedeOpsSchedule(GameState gs, RouteData route)
        {
            if (gs?.inbox == null || route == null) return;
            string id = OpsScheduleMailId(route.routeId);
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.id != id || !IsOpen(m)) continue;
                m.status = "approved";
                m.body += "\n\n（本排班呈报已被 CEO 的运力调整指令取代，新方案见运力调整呈报。）";
                gs.AddDecisionLog("运营：原排班呈报由运力调整指令取代。");
            }
        }

        /// <summary>同一航线的旧运力呈报标记为已取代（keepId 为当前呈报，保留待批）。</summary>
        static void SupersedeCapacityReports(GameState gs, RouteData route, string keepId)
        {
            if (gs?.inbox == null || route == null) return;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m == null || m.action != CapacityDirectiveAction) continue;
                if (!string.Equals(m.origin, route.routeId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsOpen(m) || m.id == keepId) continue;
                m.status = "approved";
                m.body += "\n\n（已有更新的运力调令执行，本呈报自动作废。）";
                gs.AddDecisionLog("运营：旧运力呈报被新调令取代。");
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
            message = $"已批准 {route.origin}—{route.destination} 运力：{CapacityDirectiveLogic.RegsOf(route)} {route.DualWeeklyLine()}。";
            gs.AddDecisionLog("运营：" + message);
            Schedule(gs, new InboxMail
            {
                id = "fx_cap_" + mail.id,
                sender = "运营部",
                role = "执行回执",
                glyph = "运",
                kind = "report",
                subject = $"{route.origin}—{route.destination} 运力调整已生效",
                body = message + (string.IsNullOrEmpty(mail.ringIdsCsv)
                    ? "\n航班环未变（本航线单段执飞）。"
                    : $"\n航班环 {mail.ringIdsCsv} 已生效，机号 {route.registration} 按两段连班执行。"),
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
            CapacityDirectiveLogic.MergeAiJson(d, aiJson);
            var rep = CapacityDirectiveLogic.Execute(gs, route, d);
            if (!rep.ok)
            {
                message = rep.note;
                Changed?.Invoke();
                return false;
            }

            mail.body = CapacityDirectiveLogic.FormatReportBody(gs, route, rep);
            mail.ringIdsCsv = rep.ringIdsCsv;
            mail.directiveNote = rep.directiveRaw;
            mail.capEngineRev = CapacityDirectiveLogic.EngineRev;
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
                default: return "待处理";
            }
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
