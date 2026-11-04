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
    /// 「开航加急」与「航材按交期扣款」的回归（2026-11-03 用户报障 + 裁定）。
    ///
    /// 钉三件事：
    /// <list type="number">
    /// <item>命令台里的「加急」**不许被静默丢掉**：报表原文「开通 PEK-SZX 的加急航线」，
    ///   当天没收到邮件、依旧 2 个工作日后回复 —— 病根就是路由层根本没读这四个字。</item>
    /// <item>加急只压**运营部内部会商**（2 个工作日 → 4 小时），不去动局方审查工期：
    ///   那一步由受理后的「加急请示」花点数换，两条通道不能互相顶替。</item>
    /// <item>航材采购批复按**交期天数**扣款，不再拖到季末（旧写法会把
    ///   <c>dueUtcTicks</c> 留成 0，结算判据退回季度，玩家批准后可能等两个月才见钱动）。</item>
    /// </list>
    /// </summary>
    public class OpenRouteRushEditModeTests
    {
        static GameState NewGame(out GameObject host)
        {
            host = new GameObject("__openrush__");
            var gs = host.AddComponent<GameState>();
            gs.inbox = new List<InboxMail>();
            gs.pendingInbox = new List<PendingInboxMail>();
            gs.activeRoutes = new List<RouteData>();
            gs.fleet = new List<Aircraft>();
            gs.cash = 3000f;
            gs.SetSimUtc(new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc));
            return gs;
        }

        static long OnlyPendingDeliverTicks(GameState gs)
        {
            Assert.AreEqual(1, gs.pendingInbox.Count, "应当恰好排入一封延迟投递的信");
            return gs.pendingInbox[0].deliverUtcTicks;
        }

        // ———————— 一、路由：加急意图必须被读进来 ————————

        [Test]
        public void RushWord_InOpenRouteCommand_SetsRushFlag()
        {
            var ask = CeoCommandRouter.Route("n12_航线网络", "开通PEK-SZX的加急航线");
            Assert.IsTrue(ask.ok, "带城对的开航令必须受理");
            Assert.AreEqual(CeoCommandRouter.TopicOpenRoute, ask.topic);
            Assert.AreEqual("PEK", ask.origin);
            Assert.AreEqual("SZX", ask.dest);
            Assert.IsTrue(ask.rush, "「加急」被丢掉，CEO 的加急令就等于没下过");
        }

        [Test]
        public void PlainOpenRouteCommand_IsNotRush()
        {
            var ask = CeoCommandRouter.Route("n12_航线网络", "开通北京—上海航线");
            Assert.IsTrue(ask.ok);
            Assert.IsFalse(ask.rush, "没写加急就不该自动升格，否则常规节奏被悄悄改掉");
        }

        [Test]
        public void AnalyzeRequest_NeverBecomesRushAct()
        {
            // 「分析开通…可行性」仍是战略部的分析件，加急词不该把它抢成开航令
            var ask = CeoCommandRouter.Route("n12_航线网络", "加急分析开通北京—纽约航线的可行性");
            Assert.AreEqual(CeoCommandRouter.CeoAskKind.Analyze, ask.kind);
            Assert.AreEqual(CeoCommandRouter.TopicFeasibility, ask.topic);
        }

        // ———————— 二、投递节奏：加急 = 当日到件 ————————

        [Test]
        public void RushStudy_LandsWithinHours_NotAfterTwoBusinessDays()
        {
            var gs = NewGame(out var host);
            try
            {
                var now = gs.SimUtc;
                Assert.IsTrue(InboxManager.QueueOpenRouteStudy(gs, "PEK", "SZX", null,
                    "请决策：开通 PEK — SZX", "正文", rush: true));

                long got = OnlyPendingDeliverTicks(gs);
                Assert.AreEqual(InboxCadence.ReplyReady(now).Ticks, got,
                    "加急会商应在 ReplyHours 内到件");
                Assert.Less(got, InboxCadence.StudyReady(now).Ticks,
                    "加急必须真的比常规研究期早，否则「加急」只是一句口号");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PlainStudy_KeepsTwoBusinessDays()
        {
            var gs = NewGame(out var host);
            try
            {
                var now = gs.SimUtc;
                Assert.IsTrue(InboxManager.QueueOpenRouteStudy(gs, "PEK", "SHA", null,
                    "请决策：开通 PEK — SHA", "正文"));

                Assert.AreEqual(InboxCadence.StudyReady(now).Ticks, OnlyPendingDeliverTicks(gs),
                    "常规开航仍按 2 个工作日研究期，不能被加急路径带快");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RushStudy_MailSaysItIsExpedited()
        {
            var gs = NewGame(out var host);
            try
            {
                var rushPlan = OpenRouteStudyLogic.Build(gs, "PEK", "SZX", null, rush: true);
                StringAssert.Contains("加急会商", rushPlan.body);
                StringAssert.Contains("局方审查工期不受此影响", rushPlan.body,
                    "不能让人以为研究所省下的时间等于许可也快了");

                var plainPlan = OpenRouteStudyLogic.Build(gs, "PEK", "SZX", null);
                Assert.IsFalse(plainPlan.body.Contains("加急会商"),
                    "常规研究会不该出现加急字样，否则那封信在替 CEO 编造指令");

                InboxManager.QueueOpenRouteStudy(gs, "PEK", "SZX", null,
                    rushPlan.subject, rushPlan.body, rush: true);
                Assert.AreEqual("加急", gs.pendingInbox[0].mail.tag,
                    "信封标签要能让 CEO 一眼看出这是加急件");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        // ———————— 三、航材采购：按交期天数扣款，不再等下季度 ————————

        static InboxMail NewPartsMail(GameState gs, string orderNo)
            => new InboxMail
            {
                id = "m_po_" + orderNo,
                sender = "机务工程部",
                role = "航材采购",
                glyph = "机",
                kind = "alert",
                action = "parts_order",
                origin = orderNo,
                dest = "PN-HYD-002",
                subject = $"缺件预警：请批准采购 PN-HYD-002",
                body = "",
                status = "pending",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = gs.DateLabel,
                deadlineQuarters = InboxManager.DefaultDeadlineQuarters
            };

        static PurchaseOrder AddOrder(GameState gs, string orderNo, int leadDays, bool urgent)
        {
            var mnt = MaintenanceManager.EnsureState(gs);
            var order = new PurchaseOrder
            {
                orderNo = orderNo,
                partNo = "PN-HYD-002",
                supplier = "OEM 原厂",
                quantity = 6,
                unitCostUsd = 80000f,
                leadDays = leadDays,
                urgent = urgent,
                status = "草稿"
            };
            mnt.orders.Add(order);
            return order;
        }

        [Test]
        public void PartsOrder_NonUrgent_SchedulesByLeadDays()
        {
            var gs = NewGame(out var host);
            try
            {
                AddOrder(gs, "PO-RUSH-1", 15, false);
                var mail = NewPartsMail(gs, "PO-RUSH-1");
                InboxManager.Add(gs, mail);

                Assert.IsTrue(InboxManager.ApprovePartsOrder(gs, mail, out string msg), msg);

                Assert.Greater(mail.dueUtcTicks, 0,
                    "到期时刻必须落到绝对时刻上；留 0 会退回季度判据，等于又拖回季末");
                Assert.AreEqual(gs.SimUtc.AddDays(15).Ticks, mail.dueUtcTicks,
                    "常规补货应在交期 15 天后扣款");
                Assert.IsTrue(mail.effectPending, "待结算标记必须留着，否则那笔钱永远不会扣");
                StringAssert.Contains("15 天", msg);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PartsOrder_LeadDaysFollowsTheOrder_NotAConstant()
        {
            var gs = NewGame(out var host);
            try
            {
                AddOrder(gs, "PO-RUSH-2", 3, false);
                var mail = NewPartsMail(gs, "PO-RUSH-2");
                InboxManager.Add(gs, mail);
                Assert.IsTrue(InboxManager.ApprovePartsOrder(gs, mail, out string msg), msg);

                Assert.AreEqual(gs.SimUtc.AddDays(3).Ticks, mail.dueUtcTicks,
                    "交期是订单自己的数字，不能被写死成 15 天");
                Assert.Less((mail.dueUtcTicks - gs.SimUtc.Ticks) / (double)TimeSpan.TicksPerDay,
                    GameState.DaysPerQuarter, "扣款不该跨季等季末");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
