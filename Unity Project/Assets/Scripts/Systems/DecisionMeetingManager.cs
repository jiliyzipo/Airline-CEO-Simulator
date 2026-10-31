using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// Phase 3 Week 11 · CEO 决策会议编排：
    /// 议题 → 五部门意见（红黄绿）→ 批准 / 暂缓（补条件 n104） / 拒绝。
    /// 与收件箱「发起会议」、购机/开线/招聘等上游暂缓统一对接。
    /// </summary>
    public class DecisionMeetingManager : MonoBehaviour
    {
        public static DecisionMeetingManager Instance { get; private set; }

        public event Action<string> OnMeetingResolved; // outcome: approved / deferred / rejected

        public DecisionProposal CurrentProposal { get; private set; }
        public DeptOpinion[] CurrentOpinions { get; private set; }
        public string LastOutcome { get; private set; }
        /// <summary>最近一次「暂缓 → 补条件」的表单（n104 读取）。</summary>
        public SupplementForm ActiveSupplement { get; private set; }

        private Action<string> _onResolved;

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

        private DecisionState EnsureState()
        {
            var gs = GameState.Instance;
            if (gs == null) return null;
            if (gs.decision == null) gs.decision = new DecisionState();
            if (gs.decision.history == null) gs.decision.history = new List<DecisionHistoryEntry>();
            if (gs.decision.supplementForms == null) gs.decision.supplementForms = new List<SupplementForm>();
            return gs.decision;
        }

        /// <summary>从 GameState 构建 KPI 快照。</summary>
        public static DecisionSnapshot Snapshot(GameState gs)
        {
            if (gs == null) return new DecisionSnapshot();
            return new DecisionSnapshot
            {
                cash = gs.cash,
                monthlyBurn = gs.EffectiveMonthlyBurn,
                runwayMonths = CashRunnelCalculator.GetRunwayMonths(gs.cash, gs.EffectiveMonthlyBurn),
                loadFactor = gs.loadFactorPercent,
                onTime = gs.onTimePercent,
                safetyIndex = gs.safetyIndex,
                activeRoutes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0,
                yearNetProfit = gs.yearNetProfit,
                monthlyRevenue = gs.monthlyRevenue,
                gdpPhase = gs.economicPhase,
                boardSatisfaction = gs.boardSatisfaction
            };
        }

        /// <summary>打开一次决策会议（无议题时用默认议题）。</summary>
        public void OpenMeeting(DecisionProposal proposal, Action<string> onResolved = null)
        {
            CurrentProposal = proposal ?? PrepareDefault();
            _onResolved = onResolved;
            CurrentOpinions = DecisionEconomics.EvaluateOpinions(CurrentProposal, Snapshot(GameState.Instance));
            LastOutcome = "";
            GameState.Instance?.NotifyChanged();
        }

        /// <summary>刷新意见（KPI 变化后）。</summary>
        public void RefreshOpinions()
        {
            if (CurrentProposal == null) return;
            CurrentOpinions = DecisionEconomics.EvaluateOpinions(CurrentProposal, Snapshot(GameState.Instance));
        }

        /// <summary>从收件箱/通用入口进入时的默认议题。</summary>
        public DecisionProposal PrepareDefault()
        {
            return PrepareDefault(null);
        }

        /// <summary>从收件箱进入时的默认议题；带上来源邮件 id，会议裁决后回写该函状态。</summary>
        public DecisionProposal PrepareDefault(string sourceMailId)
        {
            return new DecisionProposal
            {
                id = "meet_" + DateTime.Now.Ticks,
                topic = "综合运营议题 · 重大事项决策",
                category = DecisionCategories.Generic,
                costWan = 0f,
                sourceLabel = "收件箱",
                sourcePageId = "n7_收件箱",
                sourceMailId = sourceMailId ?? ""
            };
        }

        /// <summary>
        /// 会议裁决回写来源邮件：批准/拒绝走与收件箱按钮相同的生效分发并结案；
        /// 暂缓=补条件中（仍属待办，但收件箱不再显示决策按钮，条件齐备后可重新召集上会）。
        /// </summary>
        public static void ApplyOutcomeToMail(GameState gs, string mailId, string outcome)
        {
            if (gs?.inbox == null || string.IsNullOrEmpty(mailId) || string.IsNullOrEmpty(outcome)) return;
            InboxMail mail = null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                if (gs.inbox[i] != null && gs.inbox[i].id == mailId) { mail = gs.inbox[i]; break; }
            }
            if (mail == null) return;
            mail.meetingOutcome = outcome;

            string msg = "";
            if (outcome == "approved")
            {
                if (!InboxManager.Approve(gs, mail, out msg))
                    InboxManager.Mark(gs, mailId, "approved");
            }
            else if (outcome == "rejected")
            {
                ApplyReject(gs, mail, out msg);
            }
            else // deferred：补条件中，保持待办但隐藏收件箱决策按钮
            {
                InboxManager.Mark(gs, mailId, "deferred");
            }
            gs.AddDecisionLog($"收件箱：「{mail.subject}」经 CEO 决策会议裁决（{InboxManager.StatusLabel(mail.status)}），本函无需再次批复。");
            InboxManager.NotifyChanged();
        }

        /// <summary>会议「拒绝」= 收件箱「拒绝」按钮的同一套生效分发（无对应处理器时仅落状态）。</summary>
        static void ApplyReject(GameState gs, InboxMail mail, out string message)
        {
            message = "";
            var a = mail.action ?? "";
            if (a == "irrops") { InboxManager.RejectIrrops(gs, mail, out message); return; }
            if (a == "union_talk") { InboxManager.RejectUnion(gs, mail, out message); return; }
            if (a == InboxManager.FuelHedgeAction) { InboxManager.RejectFuelHedge(gs, mail, out message); return; }
            if (a == "finance_crisis") { InboxManager.RejectFinanceCrisis(gs, mail, out message); return; }
            if (a == "investment") { InboxManager.RejectInvestment(gs, mail, out message); return; }
            if (a == "reg_inspect") { InboxManager.ApplyInspection(gs, mail, 2, out message); return; }
            if (a == StaffRecruitment.HireAction) { StaffRecruitment.RejectOffer(gs, mail, out message); return; }
            if (a == "parts_order") { InboxManager.RejectPartsOrder(gs, mail, out message); return; }
            if (a == "comp_review") { InboxManager.RejectCompensation(gs, mail, out message); return; }
            if (a == "annual_goals") { InboxManager.RejectAnnualGoals(gs, mail, out message); return; }
            if (a == "capacity_plan") { InboxManager.RejectCapacity(gs, mail, out message); return; }
            if (a == "ops_schedule") { InboxManager.RejectOpsSchedule(gs, mail, out message); return; }
            if (a == InboxManager.CapacityDirectiveAction) { InboxManager.RejectCapacityDirective(gs, mail, out message); return; }
            if (a == InboxManager.RotationPlanAction) { InboxManager.RejectRotationPlan(gs, mail, out message); return; }
            if (a == InboxManager.SeasonReviewAction) { InboxManager.RejectSeasonReview(gs, mail, out message); return; }
            if (a == TransitionBusinessLogic.InquiryAction) { TransitionBusinessLogic.DeclineInquiry(gs, mail, out message); return; }
            InboxManager.Mark(gs, mail.id, "rejected");
            message = "已拒绝。";
        }

        public string Approve()
        {
            if (CurrentProposal == null) return "无待决议题";
            Record(CurrentProposal, "approved");
            LastOutcome = "approved";
            var p = CurrentProposal;
            CurrentProposal = null;
            var cb = _onResolved;
            _onResolved = null;
            var gs = GameState.Instance;
            gs?.AddDecisionLog($"决策会议批准：{p.topic}");
            ApplyOutcomeToMail(gs, p.sourceMailId, "approved");
            gs?.NotifyChanged();
            OnMeetingResolved?.Invoke("approved");
            cb?.Invoke("approved");
            return $"已批准：{p.topic}";
        }

        public string Defer()
        {
            if (CurrentProposal == null) return "无待决议题";
            Record(CurrentProposal, "deferred");
            LastOutcome = "deferred";
            var p = CurrentProposal;
            ActiveSupplement = CreateSupplement(p, "决策会议暂缓，需补充条件后重议。");
            CurrentProposal = null;
            var cb = _onResolved;
            _onResolved = null;
            var gs = GameState.Instance;
            gs?.AddDecisionLog($"决策会议暂缓：{p.topic}（已生成补条件任务）。");
            ApplyOutcomeToMail(gs, p.sourceMailId, "deferred");
            gs?.NotifyChanged();
            OnMeetingResolved?.Invoke("deferred");
            cb?.Invoke("deferred");
            return $"已暂缓：{p.topic}";
        }

        public string Reject()
        {
            if (CurrentProposal == null) return "无待决议题";
            Record(CurrentProposal, "rejected");
            LastOutcome = "rejected";
            var p = CurrentProposal;
            CurrentProposal = null;
            var cb = _onResolved;
            _onResolved = null;
            var gs = GameState.Instance;
            gs?.AddDecisionLog($"决策会议拒绝：{p.topic}（已标记搁置）。");
            ApplyOutcomeToMail(gs, p.sourceMailId, "rejected");
            gs?.NotifyChanged();
            OnMeetingResolved?.Invoke("rejected");
            cb?.Invoke("rejected");
            return $"已拒绝：{p.topic}";
        }

        /// <summary>取消会议（不落决策记录）。</summary>
        public void Cancel()
        {
            CurrentProposal = null;
            CurrentOpinions = null;
            _onResolved = null;
            LastOutcome = "";
        }

        /// <summary>清除当前补条件表单（n104 提交/取消后调用）。</summary>
        public void ClearSupplement()
        {
            ActiveSupplement = null;
        }

        /// <summary>上游（购机/开线/招聘）直接发起「暂缓 → 补条件」，无需完整五部门会。</summary>
        public SupplementForm DeferToSupplement(DecisionProposal p, string reason)
        {
            ActiveSupplement = CreateSupplement(p, reason);
            Record(p, "deferred");
            var gs = GameState.Instance;
            gs?.AddDecisionLog($"暂缓：{p?.topic}（补条件表单已生成）。");
            ApplyOutcomeToMail(gs, p?.sourceMailId, "deferred");
            gs?.NotifyChanged();
            OnMeetingResolved?.Invoke("deferred");
            return ActiveSupplement;
        }

        private SupplementForm CreateSupplement(DecisionProposal p, string reason)
        {
            var state = EnsureState();
            var form = new SupplementForm
            {
                proposalId = p?.id,
                topic = p?.topic ?? "未命名议题",
                category = p?.category ?? DecisionCategories.Generic,
                reason = reason,
                sourceDept = p?.sourceLabel ?? "—",
                conditions = DecisionEconomics.ConditionTemplates(p?.category ?? DecisionCategories.Generic)
            };
            if (state != null) state.supplementForms.Add(form);
            return form;
        }

        private void Record(DecisionProposal p, string outcome)
        {
            var state = EnsureState();
            if (state == null || p == null) return;
            var gs = GameState.Instance;
            state.history.Add(new DecisionHistoryEntry
            {
                year = gs != null ? gs.year : 1,
                quarter = gs != null ? gs.quarter : 1,
                topic = p.topic,
                category = p.category,
                outcome = outcome,
                opinionSummary = CurrentOpinions != null ? DecisionEconomics.OpinionSummary(CurrentOpinions) : ""
            });
            if (state.history.Count > 50) state.history.RemoveAt(0);
        }

        /// <summary>标记某条补条件完成，全部完成时提醒可重新召集。</summary>
        public bool ToggleCondition(SupplementForm form, string conditionId)
        {
            if (form == null || form.conditions == null) return false;
            for (int i = 0; i < form.conditions.Count; i++)
            {
                if (form.conditions[i].id == conditionId)
                {
                    form.conditions[i].done = !form.conditions[i].done;
                    GameState.Instance?.NotifyChanged();
                    return AllDone(form);
                }
            }
            return false;
        }

        public bool AllDone(SupplementForm form)
        {
            if (form == null || form.conditions == null || form.conditions.Count == 0) return false;
            for (int i = 0; i < form.conditions.Count; i++)
                if (!form.conditions[i].done) return false;
            return true;
        }
    }
}
