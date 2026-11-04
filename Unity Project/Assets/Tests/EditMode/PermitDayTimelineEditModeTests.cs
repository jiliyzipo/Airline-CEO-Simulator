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
    /// 航权与时刻的**天级审批**回归（2026-11-02 CEO 裁决）。
    ///
    /// 背景：旧口径里季度是唯一的推进刻度（<c>TimeManager.AdvanceQuarterCore</c> 才结算），
    /// 于是国内许可也要等 90 天、国际重申两次要等 360 天。CEO 的原话是
    /// 「申请航权和时刻槽要等到下一年的 Q2 才批，这样符合现实吗」——答案是「不符合，也不好玩」。
    ///
    /// 本文件钉四件事，正是这次改动最容易回退的地方：
    /// <list type="number">
    /// <item><b>工期表</b>：12 / 21 / 49 天，重申每次 +7 天（不是 +90 天）。</item>
    /// <item><b>节点日期单调</b>：受理 ≤ 审查 ≤ 航权 ≤ 公示 ≤ 发证，且首尾落在 0 与总工期。</item>
    /// <item><b>当天不动、到期才动</b>：批准当天不结算（否则等于秒批），到期那天才推进一个节点。</item>
    /// <item><b>旧档不炸</b>：<c>dueUtcTicks == 0</c> 的单子仍按季度比较，不会被新逻辑当场清空。</item>
    /// </list>
    /// </summary>
    public class PermitDayTimelineEditModeTests
    {
        static GameState NewGame(out GameObject host)
        {
            host = new GameObject("__permit_day__");
            var gs = host.AddComponent<GameState>();
            gs.year = 1;
            gs.quarter = 1;
            gs.simDay = 1;
            gs.cash = 50000f;
            gs.inbox = new List<InboxMail>();
            gs.pendingInbox = new List<PendingInboxMail>();
            gs.activeRoutes = new List<RouteData>();
            gs.fleet = new List<Aircraft>();
            StrategyManager.EnsureState(gs);
            return gs;
        }

        static void AdvanceDays(GameState gs, int days)
        {
            gs.SetSimUtc(gs.SimUtc.AddDays(days));
            gs.simDay = Mathf.Min(GameState.DaysPerQuarter, gs.simDay + days);
            InboxManager.SettleDueEffects(gs, gs.SimUtc);
        }

        static InboxMail BuildPermitMail(string o, string d)
        {
            return new InboxMail
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
        }

        [Test]
        public void ReviewDays_AreDaysNotQuarters()
        {
            Assert.AreEqual(12, PermitTimeline.ReviewDays(false, false, 1), "国内 · 非协调机场应为 12 天");
            Assert.AreEqual(21, PermitTimeline.ReviewDays(false, true, 1), "国内 · 协调机场应为 21 天");
            Assert.AreEqual(49, PermitTimeline.ReviewDays(true, true, 1), "国际/地区应为 49 天");

            // 重申惩罚从「一季」压到「一周」：这是「等一年」的主要成因
            Assert.AreEqual(19, PermitTimeline.ReviewDays(false, false, 2), "重申一次应只 +7 天");
            Assert.AreEqual(49 + 21, PermitTimeline.ReviewDays(true, true, 4), "第 4 次起应封顶（+3×7）");
            Assert.AreEqual(49 + 21, PermitTimeline.ReviewDays(true, true, 9), "重申惩罚不得超过封顶");
        }

        [Test]
        public void NodeOffsets_AreMonotonicAndBounded()
        {
            int[] totals = { 12, 21, 49 };
            foreach (int total in totals)
            {
                int prev = -1;
                for (int node = RoutePermitLogic.NodeAccept; node <= RoutePermitLogic.NodeGrant; node++)
                {
                    int at = PermitTimeline.NodeDayOffset(node, total);
                    Assert.GreaterOrEqual(at, prev, $"节点 {node} 的日期不得早于前一节点（总工期 {total} 天）");
                    Assert.LessOrEqual(at, total, $"节点 {node} 不得晚于发证日（总工期 {total} 天）");
                    prev = at;
                }
                Assert.AreEqual(0, PermitTimeline.NodeDayOffset(RoutePermitLogic.NodeAccept, total));
                Assert.AreEqual(total, PermitTimeline.NodeDayOffset(RoutePermitLogic.NodeGrant, total));
            }
        }

        [Test]
        public void Permit_DoesNotSettleOnApprovalDay_ButSettlesOnDueDay()
        {
            var gs = NewGame(out var host);
            try
            {
                var mail = BuildPermitMail("PEK", "SHA");
                InboxManager.Add(gs, mail);

                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);
                Assert.IsTrue(mail.effectPending, "批准后应处于在办状态");
                Assert.Greater(mail.dueUtcTicks, 0, "批准后必须写下绝对到期时刻");

                int total = mail.permitTotalDays;
                Assert.Greater(total, 0);
                Assert.Less(total, GameState.DaysPerQuarter,
                    "国内许可的总工期必须短于一个季度 —— 否则等于没改");

                // ① 批准当天：不许有任何推进（旧实现的对照面是「等 90 天」，不是「秒批」）
                InboxManager.SettleDueEffects(gs, gs.SimUtc);
                Assert.AreEqual(RoutePermitLogic.StageReview, mail.permitStage, "批准当天不应推进许可");
                Assert.AreEqual(RoutePermitLogic.NodeAccept, mail.permitNode);

                // ② 第一个节点到期：应推进一步并继续在办
                int firstNodeDay = PermitTimeline.NodeDayOffset(RoutePermitLogic.NodeReview, total);
                AdvanceDays(gs, firstNodeDay);
                Assert.Greater(mail.permitNode, RoutePermitLogic.NodeAccept, "审查节点到期应完成该节点");

                // ③ 到发证日：必须结案（不通过季末也能收口）
                //    把通过率钉成 100%，并关掉公示异议的随机分支，避免用例随机红。
                mail.permitChance = 1f;
                mail.permitObjection = true;
                AdvanceDays(gs, total + 2);
                Assert.AreEqual(RoutePermitLogic.StageGranted, mail.permitStage,
                    $"总工期 {total} 天到期后应发证，而不是继续按季等待");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SlotPurchase_SettlesImmediately_PoolApplyWaitsDays()
        {
            var gs = NewGame(out var host);
            try
            {
                // 二级市场买断：现券交易，批准那一刻就该过户（0 天）
                var buy = new InboxMail
                {
                    id = "m_buy_slot",
                    action = "buy_slot",
                    status = "pending",
                    origin = "PEK",
                    slotClock = "08:00",
                    receivedYear = 1,
                    receivedQuarter = 1,
                    payloadCsv = "1"
                };
                InboxManager.Add(gs, buy);
                Assert.IsTrue(InboxManager.Approve(gs, buy, out string msg1), msg1);
                Assert.AreEqual(gs.SimUtc.Ticks, buy.dueUtcTicks, "二级市场买断应为当场到期（0 天）");

                // 协调池申请：季度第 1 天不在换季窗口内 → 18 天，而不是「下一季度」
                var apply = new InboxMail
                {
                    id = "m_apply_slot",
                    action = InboxManager.ApplySlotAction,
                    status = "pending",
                    origin = "PEK",
                    slotClock = "09:00",
                    receivedYear = 1,
                    receivedQuarter = 1,
                    payloadCsv = "1"
                };
                InboxManager.Add(gs, apply);
                Assert.IsTrue(InboxManager.Approve(gs, apply, out string msg2), msg2);
                int days = (int)Math.Round((new DateTime(apply.dueUtcTicks, DateTimeKind.Utc) - gs.SimUtc).TotalDays);
                Assert.AreEqual(PermitTimeline.SlotPoolOffWindowDays, days,
                    "换季窗口外的协调池申请应为 18 天，不得退回「下一季度」");
                Assert.Less(days, GameState.DaysPerQuarter);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void LegacyMail_WithoutTicks_KeepsQuarterCadence()
        {
            var gs = NewGame(out var host);
            try
            {
                // 旧档单子：没有 dueUtcTicks，只有 effectYear/Quarter（排在下个季度）
                var legacy = new InboxMail
                {
                    id = "m_legacy",
                    action = "buy_slot",
                    status = "approved",
                    origin = "PEK",
                    slotClock = "10:00",
                    effectPending = true,
                    effectYear = 1,
                    effectQuarter = 2,
                    receivedYear = 1,
                    receivedQuarter = 1
                };
                InboxManager.Add(gs, legacy);

                InboxManager.SettleDueEffects(gs, gs.SimUtc);
                Assert.IsTrue(legacy.effectPending, "旧档单子不得在当天被新逻辑提前结清");

                // 推进一个季度（时钟与季度同时到），旧档才该结案
                gs.year = 1;
                gs.quarter = 2;
                gs.SetSimUtc(gs.SimUtc.AddDays(GameState.DaysPerQuarter));
                InboxManager.SettleDueEffects(gs, gs.SimUtc);
                Assert.IsFalse(legacy.effectPending, "到了排定季度，旧档单子仍应正常结案");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ProgressLine_ShowsDaysLeft_NotQuarter()
        {
            var gs = NewGame(out var host);
            try
            {
                var mail = BuildPermitMail("PEK", "SHA");
                InboxManager.Add(gs, mail);
                Assert.IsTrue(InboxManager.Approve(gs, mail, out string msg), msg);

                string line = RoutePermitLogic.ProgressLine(mail, gs.SimUtc);
                StringAssert.Contains("还剩", line, "进度行应给出「还剩几天」，而不是要玩家自己换算季度");
                StringAssert.DoesNotContain("Q1", line, "新单的进度行不该再出现季度写法");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
