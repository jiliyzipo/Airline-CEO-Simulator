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

        /// <summary>未读数变化（底栏角标刷新）。</summary>
        public static event System.Action Changed;

        public static int Turn(int year, int quarter) => year * 4 + (quarter - 1);

        /// <summary>邮件是否「未处理」（待办状态，含暂缓）。</summary>
        public static bool IsOpen(InboxMail m)
            => m != null && (m.status == "pending" || m.status == "deferred");

        /// <summary>剩余可搁置季度数（可为负 = 已到期）。</summary>
        public static int RemainingQuarters(InboxMail m, int year, int quarter)
        {
            if (m == null) return 0;
            int due = Turn(m.receivedYear, m.receivedQuarter) + m.deadlineQuarters;
            return due - Turn(year, quarter);
        }

        /// <summary>是否已到期（超过截止仍待处理）。</summary>
        public static bool IsDue(InboxMail m, int year, int quarter)
            => IsOpen(m) && RemainingQuarters(m, year, quarter) < 0;

        public static void Add(GameState gs, InboxMail mail)
        {
            if (gs == null || mail == null) return;
            if (gs.inbox == null) gs.inbox = new List<InboxMail>();
            StampReceived(gs, mail);
            gs.inbox.Insert(0, mail);
            Changed?.Invoke();
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
                    m.body = "市场部建议立即开通 PEK–SHA。\n\n建议：北京首都—上海虹桥。启动资金 300 万。请点关键词查阅航线网络 或 RPM，再批准。批准后下季度投入运营。";
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
        }

        public static string KindLabel(string kind) => kind switch
        {
            "regulatory" => "监管",
            "media" => "媒体",
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
                || mail.action == "buy_slot" || mail.action == "apply_right"
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
                else if (mail.action == "buy_slot")
                {
                    string apt = string.IsNullOrEmpty(mail.origin) ? (gs.hubIata ?? "HUB") : mail.origin;
                    int band = StrategyManager.ParseSlotBand(mail.dest);
                    message = $"已批准购买 {apt} {StrategyManager.SlotBandName(band)}时段时刻槽。将于第{ey}年 Q{eq} 扣款交割。";
                }
                else if (mail.action == "investment")
                    message = $"已批准投资项目。资本将于第{ey}年 Q{eq} 投入并进入执行。";
                else
                    message = $"已批准申请 {mail.origin}—{mail.dest} 航权。将于第{ey}年 Q{eq} 消耗政府关系并获批。";
                return true;
            }

            if (mail.action == "finance_crisis")
            {
                message = "请打开条款确认或重组页签署后再生效。";
                return false;
            }

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
                gs.AddDecisionLog("运力规划：批准补强，进入购机决策会议。");
                message = "已批准运力补强。请在购机决策会议选定引进方案。";
                return true;
            }

            if (mail.action == "ops_schedule")
                return ApproveOpsSchedule(gs, mail, out message);

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
            if (gs.inbox != null)
            {
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m == null || !m.effectPending) continue;
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

                    if (m.action == "buy_slot" || m.action == "apply_right")
                    {
                        EnsureStrategyManager();
                        string execMsg;
                        bool ok;
                        if (m.action == "buy_slot")
                        {
                            string apt = string.IsNullOrEmpty(m.origin) ? (gs.hubIata ?? "PEK") : m.origin;
                            ok = StrategyManager.Instance.AcquireSlot(apt, StrategyManager.ParseSlotBand(m.dest), out execMsg);
                        }
                        else
                        {
                            string o = string.IsNullOrEmpty(m.origin) ? "PEK" : m.origin;
                            string d = string.IsNullOrEmpty(m.dest) ? "SIN" : m.dest;
                            ok = StrategyManager.Instance.ApplyRouteRight(o, d, 5, out execMsg);
                        }
                        AddExecReceipt(gs, m, ok,
                            ok ? (m.action == "buy_slot" ? "时刻槽已交割" : "航权已获批")
                               : (m.action == "buy_slot" ? "时刻槽未能购入" : "航权申请未获批"),
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

            int quarters = RoutePermitLogic.ReviewQuarters(intl, mail.permitAttempts);
            NextQuarter(gs.year, gs.quarter, out int ey, out int eq);
            for (int i = 1; i < quarters; i++)
                NextQuarter(ey, eq, out ey, out eq);
            mail.permitDueYear = ey;
            mail.permitDueQuarter = eq;
            mail.effectPending = true;
            mail.effectYear = ey;
            mail.effectQuarter = eq;

            int gap = RoutePermitLogic.MissingPrerequisite(gs, o, d, intl, needsSlot, out string gapNote);
            mail.permitGate = gap == 1 ? "航权" : gap == 2 ? "时刻" : "";

            Mark(gs, mail.id, "approved");
            mail.status = "approved";

            AddPermitReceipt(gs, mail, "admit", $"准入受理：{o}-{d} {kind}",
                BuildAdmitBody(gs, o, d, mail, intl, needsSlot, gapNote, quarters, ey, eq), "受理");

            message = $"已批准开通 {o}-{d}。运营部已向{(intl ? "民航局" : "地区管理局")}递交{kind}申请，"
                    + $"预计 第{ey}年 Q{eq} 批复——批准不等于可以飞，须待许可发证。";
            if (gap != 0)
                message += $" 注意：{gapNote}（未办妥则许可将挂起）";

            gs.AddDecisionLog($"准入：受理 {o}-{d} {kind}，预计 第{ey}年 Q{eq} 批复");
            gs.NotifyChanged();
            return true;
        }

        static string BuildAdmitBody(GameState gs, string o, string d, InboxMail mail,
            bool intl, bool needsSlot, string gapNote, int quarters, int ey, int eq)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("受理号：").Append(mail.id).Append(" · 第").Append(mail.permitAttempts).Append("次递交\n");
            sb.Append($"航线：{o} → {d}（{(intl ? "国际/地区" : "国内")}航线）\n");
            sb.Append($"许可类型：{mail.permitNote}\n");
            sb.Append($"办理时限：{quarters} 个季度 · 预计 第{ey}年 Q{eq} 批复\n\n");

            sb.Append("—— 办理环节 ——\n");
            sb.Append("1) 航线经营许可：主管机构审查运力安排、安全运行能力与市场竞争状况\n");
            if (intl)
                sb.Append("2) 航权额度：占两国航空运输协定内的运力额度与指定承运人名额\n");
            else
                sb.Append("2) 航权额度：国内航线不涉及航权\n");
            sb.Append("3) 航班时刻槽：")
              .Append(needsSlot ? "协调机场需按协调季取得时刻槽（本航线需占用 1 个）" : "按现行时刻执行，无需新增时刻槽")
              .Append("\n");
            sb.Append("4) 公示期：局方公示，竞对可提出异议（可能顺延审批）\n\n");

            sb.Append("—— 通过率参考 ——\n");
            sb.Append(RoutePermitLogic.ChanceBreakdown(gs, o, d, intl, mail.permitAttempts)).Append('\n');
            sb.Append($"当前估算：{mail.permitChance * 100f:F0}%\n\n");

            sb.Append(gapNote);
            if (string.IsNullOrEmpty(gapNote))
                sb.Append("前置齐备：航权与时刻无缺口，按期审查。");
            sb.Append("\n\n请知悉：许可未发证前不得售票开航。可在「航权与时刻槽」页跟踪办理进度。");
            return sb.ToString();
        }

        /// <summary>按季推进准入许可：前置校验 → 公示异议 → 批复（发证则执行开通）。</summary>
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
                m.effectPending = false;
                m.permitNote = "航线已在运营";
                return;
            }

            // —— 前置：航权额度 / 时刻槽 —— 
            int gap = RoutePermitLogic.MissingPrerequisite(gs, o, d, intl, needsSlot, out string gapNote);
            if (gap != 0)
            {
                if (gap == 1)
                {
                    QueueRightDecision(gs, o, d, null, out _);
                    m.permitGate = "航权";
                }
                else
                {
                    string apt = string.IsNullOrEmpty(gs.hubIata) ? o : gs.hubIata;
                    QueueSlotDecision(gs, apt, 1, null, out _);
                    m.permitGate = "时刻";
                }

                DeferPermit(m, 2);
                m.permitNote = kind + " · 待" + m.permitGate;
                AddPermitReceipt(gs, m, "gate", $"准入受阻：{o}-{d} 待{m.permitGate}就绪",
                    $"准入审查暂停：{gapNote}\n\n已请战略发展部在收件箱提出{(gap == 1 ? "航权申请" : "时刻槽购买")}方案。\n"
                    + $"办妥后许可继续审查，当前预计 第{m.permitDueYear}年 Q{m.permitDueQuarter} 复核。", "受阻");
                return;
            }
            m.permitGate = "";

            // —— 公示期异议（同一许可以一次为限）—— 
            float objectionChance = intl ? 0.35f : 0.18f;
            if (m.permitStage == RoutePermitLogic.StageReview && !m.permitObjection
                && UnityEngine.Random.value < objectionChance)
            {
                m.permitObjection = true;
                m.permitStage = RoutePermitLogic.StageNotice;
                DeferPermit(m, 1);
                m.permitNote = kind + " · 公示期（有异议）";

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
                        $"受理号 {m.id}\n\n" +
                        $"局方在公示期内收到异议（涉及时刻时段与运力投放），要求补充材料，审批顺延一季。\n" +
                        $"当前预计批复：第{m.permitDueYear}年 Q{m.permitDueQuarter}。\n\n" +
                        "可选应对：\n" +
                        "  批准（公关）：动用政府关系点数 10 点，推动异议撤回，许可回到原节奏\n" +
                        "  暂缓（接受延期）：按顺延后的季度继续等待批复\n" +
                        "  拒绝（放弃）：终止本次许可办理\n\n" +
                        "注：异议记录会影响后续公示与航权谈判，必要时先改善局方关系。",
                    tag = "公示",
                    receivedYear = gs.year,
                    receivedQuarter = gs.quarter,
                    time = gs.DateLabel,
                    deadlineQuarters = DefaultDeadlineQuarters
                });
                AddPermitReceipt(gs, m, "objection", $"准入公示受阻：{o}-{d} 收到异议",
                    $"公示期内收到竞对异议，审批顺延一季，详细应对见「公示异议」邮件。", "受阻");
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
                m.permitNote = kind + " · 未获批";
                Mark(gs, m.id, "rejected");
                Add(gs, new InboxMail
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
                        $"受理号 {m.id} · 第{m.permitAttempts}次递交\n\n" +
                        $"局方对 {o}-{d} 的{kind}作出不予许可决定。\n\n" +
                        "—— 未获批评估 ——\n" +
                        RoutePermitLogic.ChanceBreakdown(gs, o, d, intl, m.permitAttempts) + "\n" +
                        $"本次估算通过率 {chance * 100f:F0}%，未命中。\n\n" +
                        "可选：\n" +
                        "  批准（重新递交）：再审更慢（+1 季），通过率下调 12%\n" +
                        "  拒绝（放弃）：终止该航线准入，可另择航线\n\n" +
                        "建议：先提升安全评级/局方关系与政府关系，或先取得航权额度与时刻槽，再重新递交。",
                    tag = "未获批",
                    receivedYear = gs.year,
                    receivedQuarter = gs.quarter,
                    time = gs.DateLabel,
                    deadlineQuarters = DefaultDeadlineQuarters
                });
                gs.AddDecisionLog($"准入：{o}-{d} {kind}未获批（估算通过率 {chance * 100f:F0}%）");
                gs.NotifyChanged();
                return;
            }

            // —— 发证 → 开航执行 —— 
            m.permitStage = RoutePermitLogic.StageGranted;
            m.permitNote = kind + " · 已发证";
            Mark(gs, m.id, "approved");
            bool slotTaken = false;
            if (needsSlot) slotTaken = RoutePermitLogic.ConsumeSlot(gs, fromHub ? (gs.hubIata ?? o) : o);

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

            if (result != null && result.success)
            {
                AddPermitReceipt(gs, m, "grant", $"航线 {o}-{d} 已获许可并投入运营",
                    $"{kind}已核准{(slotTaken ? "，并占用 1 个时刻槽" : "")}。\n\n{result.message}", "发证");
            }
            else
            {
                // 许可证已到手但公司条件不足（现金/运力）→ 退回审查队列，办妥后再开航
                m.permitStage = RoutePermitLogic.StageReview;
                m.permitNote = kind + " · 已核准（待公司条件就绪）";
                DeferPermit(m, 2);
                AddPermitReceipt(gs, m, "grantblocked", $"准入已核准，但 {o}-{d} 暂无法开航",
                    $"{kind}已核准，但开航执行受阻：\n\n{(result != null ? result.message : "未知原因")}\n\n"
                    + $"待现金/运力就绪后自动重试（预计 第{m.permitDueYear}年 Q{m.permitDueQuarter}）。", "受阻");
            }
            gs.NotifyChanged();
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

            int quarters = RoutePermitLogic.ReviewQuarters(intl, permit.permitAttempts);
            NextQuarter(gs.year, gs.quarter, out int ey, out int eq);
            for (int i = 1; i < quarters; i++)
                NextQuarter(ey, eq, out ey, out eq);
            permit.permitDueYear = ey;
            permit.permitDueQuarter = eq;
            permit.effectPending = true;
            permit.effectYear = ey;
            permit.effectQuarter = eq;

            int gap = RoutePermitLogic.MissingPrerequisite(gs, o, d, intl, needsSlot, out string gapNote);
            permit.permitGate = gap == 1 ? "航权" : gap == 2 ? "时刻" : "";

            Mark(gs, mail.id, "approved");
            AddPermitReceipt(gs, permit, "retry", $"准入重申：{o}-{d} 第{permit.permitAttempts}次递交",
                $"重新递交{kind}（第{permit.permitAttempts}次）：审查 {quarters} 个季度，估算通过率 {permit.permitChance * 100f:F0}%。\n"
                + $"预计 第{ey}年 Q{eq} 批复。\n\n" + (string.IsNullOrEmpty(gapNote) ? "前置齐备。" : gapNote), "受理");

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
            Add(gs, new InboxMail
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
                body = body ?? "",
                status = "approved",
                tag = tag,
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = 1
            });
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
            Add(gs, new InboxMail
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
            });
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

        static bool HasMailId(GameState gs, string id)
        {
            if (gs?.inbox == null || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < gs.inbox.Count; i++)
                if (gs.inbox[i] != null && gs.inbox[i].id == id) return true;
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
            float cost = HrManager.CompCostWan(hr, kind);
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
                       $"当前薪酬池　{hr.laborCostWan:F0} 万　员工满意度 {hr.employeeSatisfaction:F0}\n" +
                       $"人力成本占营收　{HrEconomics.LaborCostRatio(hr.laborCostWan, gs.monthlyRevenue * 12f) * 100:F0}%（目标 <25%）\n\n" +
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
            float cost = StrategyEconomics.SlotValueWan(0, b);
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
                body = $"枢纽 {apt} 中时段时刻槽估价约 {cost:F0} 万。\n\n" +
                    "建议：批准后下季度扣款交割。可点关键词查看航权与时刻槽。\n" +
                    "拒绝则本季不扩容时刻，高峰难以加密。",
                tag = "提案",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = DefaultDeadlineQuarters
            });
            message = "已提交收件箱：请批准购买时刻槽，下季度扣款交割。";
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
                body = $"竞标评分公式：0.4×经济性 + 0.25×投资承诺 + 0.2×政府关系 + 0.15×战略契合。\n" +
                    $"获批将消耗政府关系点数（约 {15f + 5 * 2f:F0}）。\n\n" +
                    "建议：批准后下季度办结。可点航权与时刻槽 查看持仓。拒绝则维持现有航权网络。",
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
                sender = "财务部",
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
                sender = "财务部",
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
            Add(gs, new InboxMail
            {
                id = "fx_" + mailId + "_" + gs.year + gs.quarter,
                sender = "财务部",
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
            });
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

            Add(gs, new InboxMail
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
            });
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
            if (gs?.activeRoutes == null) return;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
            {
                var r = gs.activeRoutes[i];
                if (r == null || !r.schedulePending) continue;
                QueueOpsSchedule(gs, r);
            }
        }

        public static bool ApproveOpsSchedule(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var route = FindRoute(gs, mail);
            if (route == null) { message = "未找到对应航线。"; return false; }
            route.schedulePending = false;
            route.SyncPrimaryFromFlights();
            Mark(gs, mail.id, "approved");
            message = $"已批准 {route.origin}—{route.destination}：{route.registration} 按 {route.depTime} {route.daysOfWeek} 运行。";
            gs.AddDecisionLog("运营：" + message);
            Add(gs, new InboxMail
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
            });
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
            if (route == null) { message = "未找到对应航线。"; return false; }
            float baseline = route.baseDailyProfit > 0.01f ? route.baseDailyProfit : route.dailyProfit;
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
            if (route == null) { message = "未找到对应航线。"; return false; }
            route.schedulePending = true;
            Mark(gs, mail.id, "rejected");
            message = $"已退回 {route.origin}—{route.destination} 排班方案，本航线暂不执行航班。";
            gs.AddDecisionLog("运营：" + message);
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
