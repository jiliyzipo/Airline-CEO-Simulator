using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 「先槽后证」并行办理的回归（2026-11-02 CEO 裁决）。
    ///
    /// 现实规则：航司可以**先拿时刻、14 个工作日内补航权**，补不上则时刻被收回。
    /// 旧实现把它写死了相反方向 —— 发证之后才办时刻（<c>NodeDetail</c> 原文「先证后槽」），
    /// 于是「抢黄金时刻」这层策略在整个游戏里不存在。
    ///
    /// 本用例钉三件事：
    /// <list type="number">
    /// <item>许可一受理，时刻提案就**同时**进收件箱（不是等发证）。</item>
    /// <item>时刻交割后立刻挂宽限期倒计时（<c>slotPreHoldUtcTicks</c>）。</item>
    /// <item>逾期未发证 → 槽退回协调池、来一封退槽回执；发证则倒计时自动解除。</item>
    /// </list>
    /// </summary>
    public class SlotPreHoldEditModeTests
    {
        static GameState NewGame(out GameObject host)
        {
            host = new GameObject("__prehold__");
            var gs = host.AddComponent<GameState>();
            gs.year = 1;
            gs.quarter = 1;
            gs.simDay = 1;
            gs.cash = 90000f;
            gs.inbox = new List<InboxMail>();
            gs.pendingInbox = new List<PendingInboxMail>();
            gs.activeRoutes = new List<RouteData>();
            gs.fleet = new List<Aircraft>();
            StrategyManager.EnsureState(gs);
            return gs;
        }

        static InboxMail NewPermitMail(string o, string d)
            => new InboxMail
            {
                id = "m_open_" + o + "_" + d,
                sender = "运营部",
                role = "准入办理",
                glyph = "运",
                kind = "regulatory",
                action = "open_route",
                origin = o,
                dest = d,
                subject = $"开通 {o}-{d}",
                body = "",
                status = "pending",
                receivedYear = 1,
                receivedQuarter = 1,
                permitAttempts = 1,
                time = "09:00"
            };

        static InboxMail FindSlotProposal(GameState gs, string permitId)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m != null && m.action == "buy_slot" && m.slotPermitId == permitId) return m;
            }
            return null;
        }

        [Test]
        public void AcceptingPermit_QueuesSlotProposalInParallel()
        {
            var gs = NewGame(out var host);
            try
            {
                var permit = NewPermitMail("PEK", "SIN");     // 国际线 → needsSlot 恒真
                InboxManager.Add(gs, permit);
                Assert.IsTrue(InboxManager.Approve(gs, permit, out string msg), msg);

                var proposal = FindSlotProposal(gs, permit.id);
                Assert.IsNotNull(proposal, "许可受理时就该并行提出时刻方案，而不是等发证之后");
                Assert.AreEqual(permit.id, proposal.slotPermitId, "时刻单必须记下为哪张许可而提");
                StringAssert.Contains("并行", proposal.body, "提案正文要讲明这是并行办理");
                StringAssert.Contains(PermitTimeline.SlotPreHoldDays.ToString(), proposal.body,
                    "提案正文要写明宽限天数");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SettledSlotBeforeGrant_StartsCountdown_AndExpires()
        {
            var gs = NewGame(out var host);
            try
            {
                var permit = NewPermitMail("PEK", "SIN");
                InboxManager.Add(gs, permit);
                Assert.IsTrue(InboxManager.Approve(gs, permit, out string msg), msg);

                var proposal = FindSlotProposal(gs, permit.id);
                Assert.IsNotNull(proposal);
                Assert.IsTrue(InboxManager.Approve(gs, proposal, out string msg2), msg2);

                int slotsBefore = StrategyManager.EnsureState(gs).slots.Count;
                int cashBefore = (int)gs.cash;

                // 二级市场买断是 0 天：走一步时钟就该交割
                gs.SetSimUtc(gs.SimUtc.AddMinutes(30));
                InboxManager.SettleDueEffects(gs, gs.SimUtc);

                int slotsAfter = StrategyManager.EnsureState(gs).slots.Count;
                Assert.AreEqual(slotsBefore + 1, slotsAfter, "预占的时刻应已入册");
                Assert.Less(gs.cash, cashBefore, "二级市场渠道应当扣款");
                Assert.Greater(permit.slotPreHoldUtcTicks, 0, "交割后必须开始 20 天倒计时");
                StringAssert.Contains("|", permit.slotPreHoldCsv, "预占清单要按「机场|时刻」记，便于精确退回");

                // 宽限期内（许可还没发证）不该退槽
                gs.SetSimUtc(gs.SimUtc.AddDays(PermitTimeline.SlotPreHoldDays - 2));
                InboxManager.SettleDueEffects(gs, gs.SimUtc);
                Assert.Greater(permit.slotPreHoldUtcTicks, 0, "没到期不得提前退槽");
                Assert.AreEqual(slotsAfter, StrategyManager.EnsureState(gs).slots.Count);

                // 逾期：退槽 + 来一封退槽回执
                gs.SetSimUtc(gs.SimUtc.AddDays(3));
                InboxManager.SettleDueEffects(gs, gs.SimUtc);
                Assert.AreEqual(0, permit.slotPreHoldUtcTicks, "到期应解除预占标记");
                Assert.AreEqual(slotsAfter - 1, StrategyManager.EnsureState(gs).slots.Count,
                    "逾期未发证，预占的时刻必须退回协调池");

                bool hasReceipt = false;
                for (int i = 0; i < gs.inbox.Count; i++)
                    if (gs.inbox[i] != null && gs.inbox[i].tag == "退槽") hasReceipt = true;
                Assert.IsTrue(hasReceipt, "退槽必须留下回执，不能悄悄消失");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// ★2026-11-02 报障回归：「11 月 4 日受理、当天买槽，24 日退槽、26 日发证」—— 差一天。
        ///
        /// 旧宽限期 20 天 **短于**国内协调机场的许可工期 21 天：**受理当天就把槽买下来**
        /// 这条最自然的打法，注定在发证前一天被退槽、扣局方关系 —— 玩家怎么打都输。
        ///
        /// 这里钉数字关系而不是走一遍流水：EditMode 下 <c>RouteRightManager.IsFromHomeBase</c>
        /// 读的是 <c>GameState.Instance</c>（编辑模式无运行时单例），起不出 21 天那一路。
        /// </summary>
        [Test]
        public void PreHoldWindow_CoversDomesticSlotPermit()
        {
            Assert.Greater(PermitTimeline.SlotPreHoldDays, PermitTimeline.DomesticSlotDays,
                "宽限期必须长于国内协调机场的许可工期（21 天）：否则受理当天买槽必在发证前一天被退槽");

            Assert.Less(PermitTimeline.SlotPreHoldDays, PermitTimeline.InternationalDays,
                "国际线 49 天仍撑不过宽限期 —— 那是设计意图：抢国际黄金时刻本来就是赌");
        }

        [Test]
        public void Grant_ReleasesCountdown_WithoutReturningSlot()
        {
            var gs = NewGame(out var host);
            try
            {
                var permit = NewPermitMail("PEK", "SIN");
                InboxManager.Add(gs, permit);
                Assert.IsTrue(InboxManager.Approve(gs, permit, out string msg), msg);

                var proposal = FindSlotProposal(gs, permit.id);
                Assert.IsNotNull(proposal);
                Assert.IsTrue(InboxManager.Approve(gs, proposal, out string msg2), msg2);
                gs.SetSimUtc(gs.SimUtc.AddMinutes(30));
                InboxManager.SettleDueEffects(gs, gs.SimUtc);
                int slotsAfter = StrategyManager.EnsureState(gs).slots.Count;
                Assert.Greater(permit.slotPreHoldUtcTicks, 0);

                // 许可发证：倒计时必须自动解除，且槽不得被退回
                permit.permitStage = RoutePermitLogic.StageGranted;
                gs.SetSimUtc(gs.SimUtc.AddDays(PermitTimeline.SlotPreHoldDays + 1));
                InboxManager.SettleDueEffects(gs, gs.SimUtc);

                Assert.AreEqual(0, permit.slotPreHoldUtcTicks, "发证后倒计时应解除");
                Assert.AreEqual(slotsAfter, StrategyManager.EnsureState(gs).slots.Count,
                    "发证后预占转为正常持仓，不得退回");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
