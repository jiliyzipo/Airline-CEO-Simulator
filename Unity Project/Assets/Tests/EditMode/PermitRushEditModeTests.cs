using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 「用政府关系点数换时间」两条通道的回归（2026-11-02 CEO 裁决 C 批；2026-11-03 补公关）。
    ///
    /// 钉四件事：
    /// <list type="number">
    /// <item>点数**付得起成本**就发请示；付不起一封都不发（不收噪声）。</item>
    /// <item>请示本身**不扣点** —— 只有批准才扣，拒绝/搁置不动账。</item>
    /// <item>批准后剩余工期压到 <see cref="PermitTimeline.RushFactor"/>，到期日确实提前。</item>
    /// <item>同一张许可只能加急一次；点数不足时批准被拒且不扣点。</item>
    /// <item>★2026-11-03：异议公关必须把 <c>dueUtcTicks</c> 真的往回拉 <see cref="PermitTimeline.ObjectionDeferDays"/> 天
    /// （此前只改季度镜像，天级单上等于空操作），且拉不过头、镜像与到期时刻同源。</item>
    /// </list>
    /// </summary>
    public class PermitRushEditModeTests
    {
        static GameState NewGame(out GameObject host, float govRelation)
        {
            host = new GameObject("__rush__");
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
            var stg = StrategyManager.EnsureState(gs);
            if (stg != null) stg.governmentRelation = govRelation;
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

        static InboxMail FindRushOffer(GameState gs, string permitId)
        {
            if (gs?.inbox == null) return null;
            for (int i = 0; i < gs.inbox.Count; i++)
            {
                var m = gs.inbox[i];
                if (m != null && m.action == InboxManager.PermitRushAction && m.slotPermitId == permitId) return m;
            }
            return null;
        }

        static InboxMail AcceptPermit(GameState gs, string o, string d)
        {
            var permit = NewPermitMail(o, d);
            InboxManager.Add(gs, permit);
            Assert.IsTrue(InboxManager.Approve(gs, permit, out string msg), msg);
            return permit;
        }

        /// <summary>造一封「公示异议」待办（正文由产品代码发，这里只要单据本身）。</summary>
        static InboxMail NewObjection(GameState gs, string o, string d)
        {
            var mail = new InboxMail
            {
                id = "pmobj_test_" + o + "_" + d + "_" + gs.inbox.Count,
                sender = "运营部",
                role = "准入公示",
                glyph = "运",
                kind = "regulatory",
                action = "permit_objection",
                origin = o,
                dest = d,
                subject = $"公示异议：{o}-{d}",
                body = "",
                status = "pending",
                receivedYear = gs.year,
                receivedQuarter = gs.quarter,
                time = "09:00",
                deadlineQuarters = InboxManager.DefaultDeadlineQuarters
            };
            InboxManager.Add(gs, mail);
            return mail;
        }

        static int TurnOf(int year, int quarter) => year * 4 + (quarter - 1);

        [Test]
        public void RichRelation_OffersRush_WithoutChargingPoints()
        {
            var gs = NewGame(out var host, PermitTimeline.RushMinRelation + 20f);
            try
            {
                var permit = AcceptPermit(gs, "PEK", "CTU");
                var offer = FindRushOffer(gs, permit.id);
                Assert.IsNotNull(offer, "点数够格就该收到加急请示");
                StringAssert.Contains("加急", offer.subject);

                float before = StrategyManager.EnsureState(gs).governmentRelation;
                Assert.AreEqual(PermitTimeline.RushMinRelation + 20f, before, 0.01f,
                    "发请示不许偷偷扣点：只有当 CEO 批准时才消耗");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PoorRelation_SendsNoRushOffer()
        {
            var gs = NewGame(out var host, PermitTimeline.RushCostPoint - 3f);
            try
            {
                var permit = AcceptPermit(gs, "PEK", "CTU");
                Assert.IsNull(FindRushOffer(gs, permit.id),
                    "连成本都付不起还发请示，等于给收件箱塞一封批了也办不成的信");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// ★2026-11-02 报障回归：「11 月那单航权从来没见过加急请示」。
        ///
        /// 病根是把发出下限钉在 60 点，而**开局只有 50 点**、每季回 2 点 ——
        /// 头一年多玩家根本看不到这条通道。判据只能是「付不付得起成本」：
        /// 拿默认开局点数（不给任何加成）开一局，也必须收到请示。
        /// 这条用例在 60 点判据下必败，改回成本判据才绿。
        /// </summary>
        [Test]
        public void StartRelation_StillGetsRushOffer()
        {
            float start = new StrategyState().governmentRelation;
            Assert.GreaterOrEqual(start, PermitTimeline.RushCostPoint,
                "开局点数必须付得起加急成本，否则这条通道对一局玩家等于不存在");

            var gs = NewGame(out var host, start);
            try
            {
                var permit = AcceptPermit(gs, "PEK", "CTU");
                Assert.IsNotNull(FindRushOffer(gs, permit.id),
                    "开局点数就该收到加急请示：门槛是成本，不是「攒够 60 点」");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ApproveRush_SpendsPoints_AndPullsDueDateIn()
        {
            var gs = NewGame(out var host, PermitTimeline.RushMinRelation + 20f);
            try
            {
                var permit = AcceptPermit(gs, "PEK", "CTU");
                var offer = FindRushOffer(gs, permit.id);
                Assert.IsNotNull(offer);

                long dueBefore = permit.dueUtcTicks;
                int totalBefore = permit.permitTotalDays;
                long acceptTicksBefore = permit.permitAcceptUtcTicks;
                float relBefore = StrategyManager.EnsureState(gs).governmentRelation;

                Assert.IsTrue(InboxManager.Approve(gs, offer, out string msg), msg);
                Assert.AreEqual("approved", offer.status);

                float relAfter = StrategyManager.EnsureState(gs).governmentRelation;
                Assert.AreEqual(relBefore - PermitTimeline.RushCostPoint, relAfter, 0.01f,
                    "加急必须刚好扣 RushCostPoint 点");
                Assert.IsTrue(permit.permitRush);
                Assert.Less(permit.dueUtcTicks, dueBefore, "加急后到期日必须提前");
                Assert.AreEqual(totalBefore, permit.permitTotalDays,
                    "总工期不动、只挪受理时刻：进度表六行日期整体前移，已办节点才不会倒退");

                // 进度表的六行日期由「受理时刻 + 总工期」推出，必须与到期判据同源：
                // 否则邮件说「省了 N 天」，表里还是老日子。
                var accAfter = new System.DateTime(permit.permitAcceptUtcTicks, System.DateTimeKind.Utc);
                var grantBefore = PermitTimeline.DayAt(new System.DateTime(acceptTicksBefore, System.DateTimeKind.Utc), totalBefore);
                var grantAfter = PermitTimeline.DayAt(accAfter, permit.permitTotalDays);
                Assert.Less(grantAfter, grantBefore, "发证日（进度表末行）必须提前");
                Assert.AreEqual(gs.SimUtc.AddDays(PermitTimeline.Rushed(totalBefore)).Date, grantAfter.Date,
                    "加急后剩余工期应为 Rushed(剩余)，发证日 = 现在 + 压缩后的剩余天数");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ExtraFlightSlotRequest_SettlesOnFiveDayLane()
        {
            var gs = NewGame(out var host, 70f);
            try
            {
                Assert.IsTrue(InboxManager.QueueApplySlots(gs, "CTU", "09:00", 1, null, out string qm,
                    "加班航班 temp", null, null, extraFlight: true), qm);

                InboxMail mail = null;
                for (int i = 0; i < gs.inbox.Count; i++)
                    if (gs.inbox[i] != null && gs.inbox[i].action == InboxManager.ApplySlotAction) mail = gs.inbox[i];
                Assert.IsNotNull(mail);
                Assert.IsTrue(mail.slotExtra, "加班单要打上快线标记（判据不能靠正文里有没有「加班」二字）");
                StringAssert.Contains("加班", mail.body);

                Assert.IsTrue(InboxManager.Approve(gs, mail, out string am), am);
                var due = new System.DateTime(mail.dueUtcTicks, System.DateTimeKind.Utc);
                double days = (due.Date - gs.SimUtc.Date).TotalDays;
                Assert.AreEqual(PermitTimeline.SlotExtraFlightDays, days, 0.001,
                    $"加班时刻应走 {PermitTimeline.SlotExtraFlightDays} 天快线，不该按常规排队");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void NightClock_SlotSettlesInThreeDays_EvenForExtraFlight()
        {
            var gs = NewGame(out var host, 70f);
            try
            {
                Assert.IsTrue(InboxManager.QueueApplySlots(gs, "CTU", "23:30", 1, null, out string qm,
                    "货运夜班", null, null, extraFlight: true), qm);

                InboxMail mail = null;
                for (int i = 0; i < gs.inbox.Count; i++)
                    if (gs.inbox[i] != null && gs.inbox[i].action == InboxManager.ApplySlotAction) mail = gs.inbox[i];
                Assert.IsNotNull(mail);
                StringAssert.Contains("夜间时段", mail.body, "夜间快线要在提案里写明，别只体现在天数上");

                Assert.IsTrue(InboxManager.Approve(gs, mail, out string am), am);
                var due = new System.DateTime(mail.dueUtcTicks, System.DateTimeKind.Utc);
                Assert.AreEqual(PermitTimeline.SlotNightCargoDays, (due.Date - gs.SimUtc.Date).TotalDays, 0.001,
                    "夜间时段该走最短那条快线（3 天），不能按加班 5 天算");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void DayClock_OffSeason_UsesRegularLane()
        {
            var gs = NewGame(out var host, 70f);
            try
            {
                Assert.IsTrue(InboxManager.QueueApplySlots(gs, "CTU", "09:00", 1, null, out string qm,
                    "常规申请", null, null), qm);

                InboxMail mail = null;
                for (int i = 0; i < gs.inbox.Count; i++)
                    if (gs.inbox[i] != null && gs.inbox[i].action == InboxManager.ApplySlotAction) mail = gs.inbox[i];
                Assert.IsNotNull(mail);
                Assert.IsTrue(InboxManager.Approve(gs, mail, out string am), am);
                var due = new System.DateTime(mail.dueUtcTicks, System.DateTimeKind.Utc);
                Assert.AreEqual(PermitTimeline.SlotPoolOffWindowDays, (due.Date - gs.SimUtc.Date).TotalDays, 0.001,
                    "窗口外的常规申请仍按 18 天排队：快线只能更短，不能顺手把常例也改快");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Rush_IsOneShot_AndRefusesWhenPointsRunOut()
        {
            var gs = NewGame(out var host, PermitTimeline.RushMinRelation + 20f);
            try
            {
                var permit = AcceptPermit(gs, "PEK", "CTU");
                var offer = FindRushOffer(gs, permit.id);
                Assert.IsTrue(InboxManager.Approve(gs, offer, out string m1), m1);

                // 再造一封（模拟旧档/重复投递）：点数为 0 时批准必须被拒，且不许扣成负数
                var stg = StrategyManager.EnsureState(gs);
                stg.governmentRelation = PermitTimeline.RushCostPoint - 3f;
                permit.permitRush = false;                 // 强行绕开「已加急」这一关
                var again = new InboxMail
                {
                    id = "m_rush_again",
                    action = InboxManager.PermitRushAction,
                    origin = permit.origin,
                    dest = permit.dest,
                    slotPermitId = permit.id,
                    status = "pending"
                };
                InboxManager.Add(gs, again);

                Assert.IsFalse(InboxManager.Approve(gs, again, out string m2), "点数不够不该批准");
                StringAssert.Contains("不足", m2);
                Assert.AreEqual(PermitTimeline.RushCostPoint - 3f, stg.governmentRelation, 0.01f,
                    "被拒的加急不能动账");
                Assert.AreEqual("pending", again.status, "批不成的单据要留在待办里，不能悄悄结案");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        // ———— 公示异议·公关（★2026-11-03）————
        // 病灶：公关只写 permitDueYear/Quarter 与 effectYear/Quarter，而结算判据早就是 dueUtcTicks，
        // 于是天级单上交 10 点「公关」，到期日一天都不动 —— 纯空操作。
        // 下面三条分别钉：真的前移 7 天、不许拉成已逾期、季度镜像必须由新到期时刻反推。

        [Test]
        public void ObjectionPardon_PullsDueBackByObjectionDeferDays()
        {
            var gs = NewGame(out var host, 70f);
            try
            {
                var permit = AcceptPermit(gs, "PEK", "CTU");

                // 异议已经把复核顺延过一次；这里直接把到期摆到足够远的位置，
                // 才能观察到「完整 7 天」的拉回（贴着当前时刻时会先撞上夹取边界，那由下一条用例管）。
                permit.dueUtcTicks = gs.SimUtc.AddDays(30).Ticks;
                permit.effectPending = true;
                long dueBefore = permit.dueUtcTicks;

                var objection = NewObjection(gs, "PEK", "CTU");
                Assert.IsTrue(InboxManager.Approve(gs, objection, out string msg), msg);

                double pulled = (new System.DateTime(dueBefore, System.DateTimeKind.Utc).Date
                                 - new System.DateTime(permit.dueUtcTicks, System.DateTimeKind.Utc).Date).TotalDays;
                Assert.AreEqual((double)PermitTimeline.ObjectionDeferDays, pulled, 0.001,
                    $"公关必须把复核按异议顺延的同一个量（{PermitTimeline.ObjectionDeferDays} 天）拉回来，"
                    + "只改季度镜像等于没改：判据认的是 dueUtcTicks");
                Assert.IsTrue(permit.effectPending, "提前复核后仍是待办，不能顺手结案");

                StringAssert.Contains($"提前 {PermitTimeline.ObjectionDeferDays} 天", msg,
                    "花了点数就要看见买到了几天，不能只说「回到原节奏」");
                Assert.AreEqual("approved", objection.status);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ObjectionPardon_ClampsToTomorrow_AndNeverSettlesOnTheSpot()
        {
            var gs = NewGame(out var host, 70f);
            try
            {
                var permit = AcceptPermit(gs, "PEK", "CTU");

                // 异议只顺延到 3 天后：硬拉 7 天会变成 4 天前 → 当场判到期 → 进度表突跳。
                permit.dueUtcTicks = gs.SimUtc.AddDays(3).Ticks;
                permit.effectPending = true;
                var objection = NewObjection(gs, "PEK", "CTU");
                Assert.IsTrue(InboxManager.Approve(gs, objection, out string m1), m1);

                Assert.AreEqual(gs.SimUtc.AddDays(1).Ticks, permit.dueUtcTicks,
                    "最小到期必须是「当前 + 1 天」：拉成已逾期等于用点数跳过审查");
                Assert.IsTrue(permit.effectPending);

                // 负面控制同源：此刻跑一次结算，不得把它判定到期。
                InboxManager.SettleDueEffects(gs, gs.SimUtc.AddMinutes(10));
                Assert.IsTrue(permit.effectPending, "还没到明天，公关不能把单据当场办结");
                Assert.IsTrue(gs.inbox.Contains(permit), "许可单必须还在箱里");

                // 已钉在最短期限时再公关一次：不许把到期推到过去，也不许假装省了几天。
                permit.dueUtcTicks = gs.SimUtc.AddDays(1).Ticks;
                var objection2 = NewObjection(gs, "PEK", "CTU");
                Assert.IsTrue(InboxManager.Approve(gs, objection2, out string m2), m2);
                Assert.AreEqual(gs.SimUtc.AddDays(1).Ticks, permit.dueUtcTicks,
                    "撞到边界就不动，不能越拉越早");
                StringAssert.Contains("已到最短期限", m2, "没省到天数就要如实说，不能糊弄成「已提前」");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ObjectionPardon_QuarterMirrorFollowsTheNewDueDate()
        {
            var gs = NewGame(out var host, 70f);
            try
            {
                var permit = AcceptPermit(gs, "PEK", "CTU");
                // 到期摆到 100 天后：拉回 7 天后仍在 Q2（第 93 天），
                // 而**受理时**写下的镜像季是 Q1（工期 12 天，首节点第 2 天）。
                // 这样「镜像是否跟着新到期时刻重算」才有判别力 —— 若照旧不重算，镜像会停在 Q1，
                // 与 dueUtcTicks 落点分裂（低于本季之上的真实季度）。
                permit.dueUtcTicks = gs.SimUtc.AddDays(100).Ticks;
                permit.effectPending = true;

                var objection = NewObjection(gs, "PEK", "CTU");
                Assert.IsTrue(InboxManager.Approve(gs, objection, out string msg), msg);

                // 镜像必须由**新的到期时刻**反推（与 ScheduleDays 同一套：剩余天数折成季数再加当前季）。
                var due = new System.DateTime(permit.dueUtcTicks, System.DateTimeKind.Utc);
                int remainDays = (int)(due.Date - gs.SimUtc.Date).TotalDays;
                int expectedAdd = (System.Math.Max(0, gs.simDay - 1) + remainDays) / GameState.DaysPerQuarter;
                int expectedTurn = TurnOf(gs.year, gs.quarter) + expectedAdd;

                Assert.AreEqual(expectedTurn, TurnOf(permit.effectYear, permit.effectQuarter),
                    "effectYear/Quarter 必须与新到期时刻同源，不能出现「镜像说 Q3、到期时刻说 Q2」");
                Assert.AreEqual(expectedTurn, TurnOf(permit.permitDueYear, permit.permitDueQuarter),
                    "天级单的批复季镜像也要跟着走，别留一个旧的季在别处被读到");
                Assert.GreaterOrEqual(TurnOf(permit.effectYear, permit.effectQuarter),
                    TurnOf(gs.year, gs.quarter), "镜像不得早于当前季");

                // 夹取边界上的具体反例：拉回后落在这两天，镜像就该停在当前季 —— 旧实现会一直说「下季」。
                var permit2 = AcceptPermit(gs, "PEK", "XIY");
                permit2.dueUtcTicks = gs.SimUtc.AddDays(5).Ticks;
                permit2.effectPending = true;
                var objection2 = NewObjection(gs, "PEK", "XIY");
                Assert.IsTrue(InboxManager.Approve(gs, objection2, out string m2), m2);
                Assert.AreEqual(gs.quarter, permit2.effectQuarter, "到期还在本季，镜像就必须是本季");
                Assert.AreEqual(gs.year, permit2.effectYear);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
