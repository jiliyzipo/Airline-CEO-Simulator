using System;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 市场开通通告与询价请示的**送达次序**回归（2026-11-03 用户裁定）。
    ///
    /// 裁定原文：「应该先收到开通包机、湿租、培训的业务邮件，再收到询价的邮件。
    /// 而且询价邮件需要隔几个小时再收到。」
    ///
    /// 旧版的两处毛病：
    /// <list type="number">
    /// <item>通告与询价都由 `ProgressReady`（同一个 +6 小时）投递 → 同一分钟并列落进收件箱，
    ///   看不出「先开会通告、再谈买卖」的顺序；</item>
    /// <item>通告本身在报价已存在时被写成**待批件**并带上单号，紧接着 `FlushInquiry`
    ///   又把同一批单号另发一封 → 收件箱里两封待批件抢同一批报价，签掉一封，另一封的单号就作废。</item>
    /// </list>
    ///
    /// 现在：通告恒为**汇报件、不带单号**，询价走 `InquiryCadence.InquiryReady`（再晚若干小时），
    /// 且全公司只有**一封**询价件。
    /// </summary>
    public class TransitionUnlockCadenceEditModeTests
    {
        GameObject _go;
        GameState _gs;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TransitionCadenceGS");
            _gs = _go.AddComponent<GameState>();
            _gs.EnsureDefaults();
            _gs.inbox = new System.Collections.Generic.List<InboxMail>();
            _gs.pendingInbox = new System.Collections.Generic.List<PendingInboxMail>();
            _gs.transitionContracts = new System.Collections.Generic.List<TransitionContract>();
            _gs.transitionOffers = new System.Collections.Generic.List<TransitionOffer>();
            _gs.transitionIntroSent = true;          // 跳过开局引导信，只看解锁那两封
            _gs.transitionCrewMailSent = false;
            _gs.transitionInstructorMailSent = false;

            // 两个解锁门槛岗位各到岗一人
            _gs.staffPool.Add(new PersonnelData
            {
                id = "cad_ops_crew", fullName = "自测·飞行运行主管",
                department = "OPS", jobTitle = TransitionBusinessLogic.CrewPost,
                isOperational = true, onMarket = false
            });
            _gs.staffPool.Add(new PersonnelData
            {
                id = "cad_ops_inst", fullName = "自测·客舱经理",
                department = "OPS", jobTitle = TransitionBusinessLogic.InstructorPost,
                isOperational = true, onMarket = false
            });

            // 解锁当天板上一张湿租报价（不靠随机）：湿租上限 1，TopUp 不会再补
            _gs.transitionOffers.Add(new TransitionOffer
            {
                id = "to_wet_cadence",
                kind = TransitionKinds.Wet,
                client = "蓝洋航空",
                rateWanPerDay = 1.20f,
                totalDays = TransitionBusinessLogic.LeaseDays
            });

            Assert.IsTrue(TransitionBusinessLogic.CrewReady(_gs), "夹具失败：机组门槛未满足");
            Assert.IsTrue(TransitionBusinessLogic.InstructorReady(_gs), "夹具失败：教员门槛未满足");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            _go = null;
            _gs = null;
        }

        static long DeliverTicks(System.Collections.Generic.List<PendingInboxMail> list, string subjectPart, out InboxMail mail)
        {
            mail = null;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p?.mail == null || p.mail.subject == null) continue;
                if (!p.mail.subject.Contains(subjectPart)) continue;
                mail = p.mail;
                return p.deliverUtcTicks;
            }
            return -1;
        }

        [Test]
        public void UnlockDay_AnnouncementIsAReport_AndInquiryComesHoursLater()
        {
            TransitionBusinessLogic.OnDayTick(_gs);

            long announceAt = DeliverTicks(_gs.pendingInbox, "市场已开通", out InboxMail announce);
            long inquiryAt = DeliverTicks(_gs.pendingInbox, "询价", out InboxMail inquiry);

            Assert.IsNotNull(announce, "没排「市场已开通」通告");
            Assert.IsTrue(announce.subject.Contains("湿租") || announce.subject.Contains("培训"),
                $"通告题目看不清开的是哪个市场：{announce.subject}");
            Assert.AreEqual("approved", announce.status, "开通通告是通报件，不该是要 CEO 批复的待批件");
            Assert.IsTrue(string.IsNullOrEmpty(announce.payloadCsv),
                "通告不许带报价单号 —— 带了就与随后的询价件抢同一批报价");

            Assert.IsNotNull(inquiry, "没排询价请示");
            // 湿租那份报价元数据挂在这封上
            StringAssert.Contains("to_wet_cadence", inquiry.payloadCsv ?? "");

            Assert.Greater(inquiryAt, announceAt, "询价必须晚于开通通告");
            long gap = inquiryAt - announceAt;
            Assert.AreEqual(InboxCadence.InquiryHours * TimeSpan.TicksPerHour, gap,
                $"通告与询价只隔 {gap / (double)TimeSpan.TicksPerHour:F1} 小时，看不出先后");
        }

        [Test]
        public void UnlockDay_HasExactlyOnePendingInquiry()
        {
            TransitionBusinessLogic.OnDayTick(_gs);

            int pending = 0;
            for (int i = 0; i < _gs.pendingInbox.Count; i++)
            {
                var m = _gs.pendingInbox[i]?.mail;
                if (m != null && m.action == TransitionBusinessLogic.InquiryAction) pending++;
            }
            for (int i = 0; i < _gs.inbox.Count; i++)
            {
                var m = _gs.inbox[i];
                if (m != null && m.action == TransitionBusinessLogic.InquiryAction) pending++;
            }

            Assert.AreEqual(1, pending,
                "同一批报价只能有一封待批询价件：两封会互相作废对方的单号");
        }

        [Test]
        public void InquiryCadence_IsStrictlyLaterThanProgress()
        {
            var t = new DateTime(2026, 11, 4, 4, 0, 0, DateTimeKind.Utc);
            Assert.Greater(InboxCadence.InquiryReady(t), InboxCadence.ProgressReady(t),
                "询价件不能与进度汇报同一时点落笔");
            Assert.AreEqual(InboxCadence.ProgressHours + InboxCadence.InquiryHours,
                (int)(InboxCadence.InquiryReady(t) - t).TotalHours);
        }
    }
}
