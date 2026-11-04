using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using NUnit.Framework;
using UnityEngine;

namespace AirlineCEO.Tests.EditMode
{
    /// <summary>
    /// 命令台「购买 / 申请 &lt;机场&gt; &lt;时刻&gt;时刻槽」的回归（2026-11-03 报障）。
    ///
    /// 报障原话：「图二命令台输入：购买 SIN 12:00 时刻槽，收到完全无关的邮件」。
    ///
    /// 病根：这句话里只有「时刻槽」命中运营部词表（<c>CeoCommandRouter.OpsWords</c>），
    /// 于是被当成一句「汇报运营情况」，回了一封「8 条航线 · 周 112 班 · 准点 80%」的网络总账。
    /// 槽没买、现金没动、台账没动 —— 却像受理了一样，比回「没听懂」更糟。
    ///
    /// 这里钉四件事：
    /// <list type="number">
    /// <item>这句话必须被认成时刻槽事务（而不是部门汇报）；</item>
    /// <item>二级市场没有挂牌时，**如实说不成**，且**不发任何邮件**；</item>
    /// <item>有挂牌时当场成交：扣款、入持仓、下架，且同样**不发无关邮件**；</item>
    /// <item>「申请」走协调池（免费、递提案），不会扣款。</item>
    /// </list>
    /// </summary>
    public class SlotDealCommandEditModeTests
    {
        GameObject _go;
        GameState _gs;
        SlotMarketState _mkt;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("SlotDealCmdGS");
            _gs = _go.AddComponent<GameState>();
            _gs.EnsureDefaults();
            _gs.inbox = new System.Collections.Generic.List<InboxMail>();
            _gs.pendingInbox = new System.Collections.Generic.List<PendingInboxMail>();
            _gs.cash = 5000f;
            _mkt = SlotMarketLogic.Ensure(_gs);
            Assert.IsNotNull(_mkt, "二级市场状态没建起来");
            _mkt.listings.Clear();
            _mkt.mine.Clear();
            _mkt.offers.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _gs = null;
        }

        static CeoCommandRouter.CeoAsk Route(string raw)
            => CeoCommandRouter.Route("n77_航权与时刻槽", raw);

        [Test]
        public void BuySlotSentence_IsRoutedToSlotDeal_NotToOpsReport()
        {
            var ask = Route("购买SIN 12:00时刻槽");

            Assert.IsTrue(ask.ok, "这句话没被认出来");
            Assert.AreEqual(CeoCommandRouter.TopicSlotDeal, ask.topic,
                "「购买 SIN 12:00 时刻槽」被派成了部门汇报 —— 槽没买、账没动，却会回一封网络总账");
            Assert.AreEqual("SIN", ask.origin, "机场没解析出来");
            Assert.AreEqual("12:00", ask.clock, "时刻没解析出来");
            Assert.IsTrue(ask.slotBuy, "应走二级市场买入渠道");
        }

        [Test]
        public void ApplySlotSentence_UsesFreePoolChannel()
        {
            var ask = Route("申请SIN 12:00时刻槽");

            Assert.IsTrue(ask.ok);
            Assert.AreEqual(CeoCommandRouter.TopicSlotDeal, ask.topic);
            Assert.IsFalse(ask.slotBuy, "「申请」是协调池（免费），不该走付费买入");
        }

        [Test]
        public void SlotQuestion_StillGoesToDepartment()
        {
            // 问句归运营部答，不能被当成买卖指令（否则一封「为什么缺时刻槽」会去改台账）
            var ask = Route("为什么 SIN 12:00 没有时刻槽");
            Assert.AreNotEqual(CeoCommandRouter.TopicSlotDeal, ask.topic);
        }

        [Test]
        public void BuySlotWithoutAirport_GuidesInsteadOfSendingMail()
        {
            int before = _gs.inbox.Count;
            var ask = Route("购买时刻槽");
            var ans = DeptReportLogic.Build(_gs, ask, "购买时刻槽");

            Assert.IsFalse(ans.ok, "缺机场/时刻时不该执行");
            Assert.IsTrue((ans.note ?? "").Contains("机场"),
                $"引导文案没说清缺什么：{ans.note}");
            Assert.AreEqual(before, _gs.inbox.Count, "说不清就说不清，不许往收件箱塞一封无关汇报");
        }

        [Test]
        public void BuySlot_NoListing_RefusesHonestly_AndSendsNoMail()
        {
            const string raw = "购买SIN 12:00时刻槽";
            float cashBefore = _gs.cash;
            int inboxBefore = _gs.inbox.Count;
            int pendingBefore = _gs.pendingInbox.Count;
            int heldBefore = _gs.stg.slots.Count;

            var ask = Route(raw);
            var ans = DeptReportLogic.Build(_gs, ask, raw);

            Assert.IsFalse(ans.ok, "货架上没有这条槽，不该假装办成了");
            StringAssert.Contains("未成交", ans.note ?? "");
            Assert.AreEqual(cashBefore, _gs.cash, 0.01f, "没说成事却动了钱");
            Assert.AreEqual(heldBefore, _gs.stg.slots.Count, "没成交却入了持仓");
            Assert.AreEqual(inboxBefore, _gs.inbox.Count, "不该发任何邮件");
            Assert.AreEqual(pendingBefore, _gs.pendingInbox.Count, "不该排任何邮件");
        }

        [Test]
        public void BuySlot_WithListing_ExecutesOnTheSpot_AndSendsNoMail()
        {
            const string raw = "购买SIN 12:00时刻槽";
            _mkt.listings.Add(new SlotMarketListing
            {
                id = "MK-TEST-1",
                airport = "SIN",
                clock = "12:00",
                seller = "星洲联航",
                askWan = 300f,
                fairWan = 280f,
                listedYear = _gs.year,
                listedQuarter = _gs.quarter
            });
            float cashBefore = _gs.cash;
            int inboxBefore = _gs.inbox.Count;
            int pendingBefore = _gs.pendingInbox.Count;
            int heldBefore = _gs.stg.slots.Count;

            var ask = Route(raw);
            var ans = DeptReportLogic.Build(_gs, ask, raw);
            Assert.IsTrue(ans.ok);
            Assert.IsTrue(ans.needsRun, "买入要走既有成交管线");

            bool done = DeptReportLogic.RunPending(_gs, ans, raw, out string receipt);

            Assert.IsTrue(done, $"成交失败：{receipt}");
            StringAssert.Contains("已成交", receipt);
            Assert.AreEqual(cashBefore - 300f, _gs.cash, 0.01f, "没有按叫价扣款");
            Assert.AreEqual(heldBefore + 1, _gs.stg.slots.Count, "成交后没有入持仓");
            Assert.AreEqual(0, _mkt.listings.Count, "成交后挂牌应下架");
            Assert.AreEqual(inboxBefore, _gs.inbox.Count, "二级市场成交是现券交易，不该再塞一封邮件");
            Assert.AreEqual(pendingBefore, _gs.pendingInbox.Count);
        }

        [Test]
        public void ApplySlot_DeliversProposal_AndChargesNothing()
        {
            const string raw = "申请SIN 12:00时刻槽";
            float cashBefore = _gs.cash;

            var ask = Route(raw);
            var ans = DeptReportLogic.Build(_gs, ask, raw);
            Assert.IsTrue(ans.ok && ans.needsRun);

            bool done = DeptReportLogic.RunPending(_gs, ans, raw, out string receipt);

            Assert.AreEqual(cashBefore, _gs.cash, 0.01f, "协调池申请是免费的，不该扣款");
            // 池满 / 已有同刻申请时管线会拒绝（那是正当拒绝），只要求它说清原因、不放行假成功
            if (done)
            {
                int proposals = 0;
                for (int i = 0; i < _gs.inbox.Count; i++)
                    if (_gs.inbox[i] != null && _gs.inbox[i].action == InboxManager.ApplySlotAction) proposals++;
                Assert.Greater(proposals, 0, "申请成功却没递进收件箱");
            }
            else
            {
                Assert.IsFalse(string.IsNullOrEmpty(receipt), "拒绝时必须说明原因");
            }
        }
    }
}
